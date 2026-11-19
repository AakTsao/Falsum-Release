# FALSUM 性能优化实施计划（高刷 / 3060 跑不满 165Hz）

依据：本轮只读诊断（渲染侧 + CPU/帧率侧）+ 既有评估
`docs/superpowers/specs/2026-11-06-falsum-android-feasibility-assessment.md`（下称「android spec」，条款沿用如 §2.3）。

## 0. 结论摘要

**「强制 D3D11 导致跑不满 165Hz」这个假设不成立。** 三条独立证据：

1. GPU 侧总量估算 **1.5–2.5 ms**，占 165Hz 预算（6.06 ms）的 25–40%。主导项是 4x MSAA 的附件 store/resolve（≈120 MB/帧），不是 API。
2. 两条自定义全屏 blur 链在 165Hz 下是 **11.2 GB/s ≈ 3060 带宽的 3%**（0.19–0.27 ms/帧）。
3. Native Render Pass 在本工程**确实生效**（`RenderGraph.cs` 默认 `nativeRenderPassesEnabled = true`，全仓无按设备类型关闭的门控），但 D3D11 无 tile memory，带宽收益 ≈0；即便人为回退也只多 0.4–1.2 ms。**D3D11 vs D3D12 在本工程是几百 µs 量级的差别。**

真正能解释「设了 165 却只有 120」的是**两个已确认的缺陷 + 一个测量误导**：

| # | 缺陷 | 后果 |
|---|---|---|
| D1 | 窗口化模式下 `ApplyResolution` 整段被跳过 | 用户存的 165/240 **从未下发**给 `Screen.SetResolution` |
| D2 | 偏好版本号错位 + 损坏文件不自愈 | 每次启动丢弃整份显示/画质设置，AA 被重置回 4x MSAA |
| D3 | 性能窗归因链在 `CpuMainThreadMs > 0` 时无条件归因主线程 | DWM 呈现节流被 100% 误报成「CPU 主线程瓶颈」 |

因此本计划的顺序是：**先修「设置不生效」与「测量不可信」（P0/P1），再谈优化（P2/P3）**。
在 D1/D3 修复前做的任何性能调优都无法被验证。

---

## 1. 事实基线

> 行号是 2026-11-14 快照。工作区有大量未提交改动（别人的活），**行号会漂，实施时一律用符号名定位**
> （方法名 / 字段名 / 常量名 / YAML key）。本文的 `path:line` 只用于缩短首次检索。

### 1.1 实测（可直接采信）

| # | 事实 | 证据 |
|---|---|---|
| F1 | 构建强制 **仅 D3D11**，无 D3D12 回退 | `Assets/InFalsus/Editor/FalsumBuild.cs:74-76` `SetGraphicsAPIs(..., new[]{ GraphicsDeviceType.Direct3D11 })` |
| F2 | 工程 D3D11 设置 | `ProjectSettings/ProjectSettings.asset:580-589` `WindowsStandaloneSupport m_APIs: 02000000`（0x02 = D3D11） |
| F3 | 运行日志确认为 D3D11 / level 11.1 | `Builds/FalsumEditor-0.46-Canary/` 的 `Player.log`：`Version: Direct3D 11.0 [level 11.1]`、`kGfxThreadingModeClientWorkerJobs` |
| F4 | 本机 GPU 是 **RTX 5060 Laptop**（不是 3060） | 同上 `Renderer: NVIDIA GeForce RTX 5060 Laptop GPU (ID=0x2d59)` |
| F5 | **场景里没有任何光源** | `GameplayRuntime.unity` 中 `!u!108`（Light）计数 = **0**，全仓 `AddComponent<Light>` = 0 |
| F6 | 因此阴影配置**恒为 0 成本** | `PC_RPAsset.asset` 的 2048/4 级联/软阴影（`:46-69`）因无主光不入队（URP 用 `mainLightIndex != -1` 门控 `supportsMainLightShadows`） |
| F7 | **SSAO 是开着的** | `Assets/Settings/PC_Renderer.asset:70` `m_Name: ScreenSpaceAmbientOcclusion`；`:72` `m_Active: 1`；`:77` `Source: 1`（DepthNormals） |
| F8 | HDR 缓冲是 **R11G11B10（4 B/px）**，不是 RGBA16F | `PC_RPAsset.asset:27` `m_HDRColorBufferPrecision: 0` + `ProjectSettings.asset:68` `preserveFramebufferAlpha: 0` |
| F9 | `m_RequireDepthTexture: 1` / `m_RequireOpaqueTexture: 1`，但**全仓 30 个 shader 无一采样** `_CameraDepthTexture` / `_CameraOpaqueTexture` / `SampleSceneDepth` / `SampleSceneColor` | `PC_RPAsset.asset:22-23`；grep over `Assets/**/*.shader|*.hlsl|*.cginc` 命中 0 |
| F10 | 两条自定义 feature 各 **1 Draw + 7 Blit** | `FalsumTrackEndComposerFeature.cs:92-133`、`SkyJudgementLineEffectFeature.cs:94-135`（各 8 次 `AddBlit`/`DrawMask` 调用点） |
| F11 | Sky 链 mask 在 1080p 下 **1536×1536（比屏幕还大）** + 2 张 1536×384 | `SkyJudgementLineEffect.cs:640-661`：`scale = max(pxW/3840, 0.75)`，`0.75 × 2048` |
| F12 | TrackEnd 链 mask 在 1080p 下 **900×600（非全屏）** | `FalsumTrackEndComposer.cs:246-256` `GetMaskSize`：`clamp(pxW/3840, 0.75, 1) × 1200×800` |
| F13 | 两条链合计 **68 MB/帧**（android spec §2.3 逐 pass 统计） | sky 1.70 Mpx + trackEnd 1.15 Mpx，14 个 5-tap BLIT |
| F14 | Sky 链每帧还有 CPU 侧 1448 个三角形的 PerlinNoise + `SetData` | `SkyJudgementLineEffect.cs:11-14`（`TriangleCount = 8×181 = 1448`）、`:752-792` |
| F15 | 每帧 7 个编辑器动态 mesh 全量重传 | `FalsumEditorWorld.cs:791-823`（`Refresh` 里 7 次 `Apply()`）、`:200-226`（`Mesh.Clear + SetVertices + SetColors + SetUVs + SetNormals + SetTriangles + RecalculateBounds`） |
| F16 | `_world.Refresh` **只有一个调用点**，且每帧无条件调用 | `FalsumChartEditor.cs:12210`（在 `UpdateOverlays` 内，`UpdateOverlays` 由 `LateUpdate` 每帧调） |
| F17 | 小节线每帧重建 3 个 mesh | `MeasureLineRenderer.cs:99-152`（`Update`）→ `:298-312`（`ApplyMesh`） |
| F18 | 网格每帧重建 + `Sort` | `FalsumEditorWorld.cs:1984-2068`（`BuildGrid`）；`FalsumBeatGrid.cs:434-470`（`MergeLines` 内 `_scratch.Sort`） |
| F19 | 每个音符的 MPB **每帧必然写入**（状态缓存永不命中） | `NoteRenderer.cs:2217` → `TrackMotion.cs:106-110` `Fade(z)` 连续变化 → `NoteRenderer.cs:2705-2714` 提前返回条件失效 → `:2757/:2791/:2821` |
| F20 | 每音符 Renderer 数：tap/span **1**、侧 tap **3**、hold **3**、侧 hold **5**、flick **2** | `Assets/InFalsus/Recovered/GameObject/*.prefab` 的 `MeshRenderer:` 计数 |
| F21 | 重谱面峰值可见 ~54–80 个 MeshRenderer（游玩态 120–200 draw call） | 对 `../Charts/**/*.falsum` 三张谱按 ±0.667 s 视窗加权统计 |
| F22 | 全仓 **0 处** `Application.targetFrameRate` / `OnDemandRendering` / `-force-*` | grep |
| F23 | 两档 `vSyncCount: 0`；PC 档 `antiAliasing: 4` | `ProjectSettings/QualitySettings.asset:32`、`:86`；`:58` |
| F24 | player 默认 **1366×768 Windowed** | `ProjectSettings.asset:45-46`、`:114 fullscreenMode: 3`；`FalsumBuild.cs:64-70` |
| F25 | **窗口化时刷新率不下发** | `FalsumChartEditor.cs:419` 调 `TryApply(pref, applyResolution: pref.Display.BorderlessFullscreen, ...)` → `FalsumDisplaySettingsController.cs:112-115` `if (applyResolution) ApplyResolution(...)` 整段跳过 |
| F26 | 全屏切换用**回读的当前**刷新率，不用存值 | `FalsumChartEditor.cs:11866-11912`（`Screen.currentResolution.refreshRateRatio`） |
| F27 | 偏好版本号 **构建 8 / 工作区 10** 错位 | `git show HEAD:.../FalsumPreferences.cs` → `CurrentVersion = 8`；工作区 `FalsumPreferences.cs:446` → `= 10` |
| F28 | 版本不匹配 → 抛异常 → 回落默认，且**坏文件留在原地** | `FalsumPreferencesMigration.cs:142-149` → `FalsumPreferencesStore.cs:115-120`（throw）→ `:140-147`（catch → `CreateDefault()`）→ `:150-165`（`PreserveCorruptFile` 只 Copy 不删） |
| F29 | 实测已发生：`Unsupported preferences version: 10`（本版）、`: 9`（上一版） | `AppData/LocalLow/Team AakTsao/Falsum Editor Preview/Player.log` / `Player-prev.log` |
| F30 | 回落默认值 = 1366×768 + 桌面刷新率 + **AA 开 / MSAA4x** | `FalsumPreferences.cs:447-448`（窗口尺寸常量）、`:492-506`（`CreateDefault`）、`:84-86`（AA 默认） |
| F31 | 性能窗预算只有 **30/60/120/144** 四档，无 165 | `FalsumPerformanceMonitor.cs:240-257`（`CycleTargetFps`）、`:98-101`（`FrameBudgetMs = 1000/TargetFps`） |
| F32 | 归因链：`CpuMainThreadMs > 0` 即判主线程，vsync 分支近乎不可达 | `FalsumPerformanceMonitor.cs:694-717` |
| F33 | 开性能窗会启 `ProfilerArea.GPU` + 3 个 `ProfilerRecorder`（含 GpuRecorder） | `FalsumPerformanceMonitor.cs:492-529` |
| F34 | 发行版（非 Dev Build）**不会**打开 `Profiler.enabled` | `FalsumPerformanceMonitor.cs:499-505` 条件 `isEditor || isDebugBuild`；`FalsumBuild.cs:101` `BuildOptions.None` |
| F35 | 每帧 `FrameTimingManager.CaptureFrameTimings()`；只读 cpuMain/cpuRender/gpu，**不读 present wait** | `FalsumPerformanceMonitor.cs:293-297`、`:597-636` |
| F36 | `enableFrameTimingStats: 1` | `ProjectSettings.asset:160` |
| F37 | 编辑器网格都 `MarkDynamic()` 了（说明它们本来就该走动态路径） | `FalsumEditorWorld.cs:5173`、`:5201`；`MeasureLineRenderer.cs:289` |
| F38 | `TimelineVersion` 是可用的失效信号（编辑 + 拖拽的缓存键已在用） | `ChartModels.cs:401`（字段）、`:340`/`:413`/`:561`/`:885`（自增）；`FalsumEditorWorld.cs:4027`/`:4048`（现有 sky 多边形缓存以它 + 音符字段为键） |
| F38a | `_ghostMesh` **已经有**脏标记实现（`_lastGhostKey`），不是「待优化项」 | `FalsumEditorWorld.cs:335`（字段）、`:4529-4546`（键比较与更新） |
| F39 | 现有门禁基线：`FalsumSelfCheck` **passed=true，37 项**；本地化审计 **passed** | `Diagnostics/FalsumSelfCheck.json`；`python Tools/falsum_localization_audit.py` |
| F40 | Heavy Chart 采集**从未产出过 JSON**，只有 `Debug.Log` + 截图 | `FalsumHeavyChartPerformanceCapture.cs:247-296`（`LogMetrics`）、`:365-390`（`CaptureScreenshot`）；`Diagnostics/` 下无匹配产物 |
| F41 | `FrameTiming.cpuMainThreadPresentWaitTime` **确实存在于 Unity 6000.3.26f1** | `UnityEngine.CoreModule.xml` 成员 `F:UnityEngine.FrameTiming.cpuMainThreadPresentWaitTime`（"The CPU time the last frame spent in waiting for Present on the main thread, in ms."）。同代 `FrameTiming` 还提供 `syncInterval` / `widthScale` / `heightScale` |
| F42 | 小节线是 **3 个独立 material**，`new Material(shader)` 各建一份（同 shader、`renderQueue = 3035`） | `MeasureLineRenderer.cs:264-290`（`CreateLineMesh` 里 `new Material(shader){ name = name+"_Material", renderQueue = 3035 }`）、`:64-66`（三次调用）。**合并 mesh 会失去三次不同侧轨几何的 per-mesh 变换**，见 T4 |
| F43 | `MeasureLineRenderer` **没有 `Dispose`**，mesh / material 不显式释放 | `MeasureLineRenderer.cs` 全文无 `Dispose`/`Destroy`（与 `NoteRenderer` 的 `DestroyObject` 不同） |
| F44 | `_overlayMesh` 只在 `BuildSelection` → `AddSelectionBox` 里写；**无选中时是空的** | `FalsumEditorWorld.cs:4990-5033`（6 次 `AddQuad(_overlayMesh, ...)`）、`:2311-2336`（`BuildSelection` 在 `selected.Count == 0` 时早退） |
| F45 | `NoteZAt` 显式依赖 `nowMs`：`(ScaledTimeAt(noteTime) - ScaledNowAt(now)) / 1000 * …` | `TrackMotion.cs:76-81`。**因此网格/小节线/标尺/选择框/时间面/光标箭头/ghost 的 Z 都随播放头连续变化；7 个编辑器 mesh 里只有 `_guideMesh` 不含 `nowMs`** |
| F45a | `BuildGuide` **完全不含 `nowMs`**（只用 `ruler` + 两个常量） | `FalsumEditorWorld.cs:1062-1100`：`zNear = _lineLocal.z`、`zFar = _trackEndZ`、`x = Lerp(±TrackHalfWidth, ruler.GuideNormalized)` |
| F45b | `BuildSnapArrow`（写 `_cursorMesh`）**含 `nowMs`**，所以光标箭头也每帧变 | `FalsumEditorWorld.cs:825-855`：`center = ModePlaneLocalPosition(timePlaneMs.Value, nowMs)`、`z = center.z - 0.012f` |
| F46 | Unity 6000.3.26f1 的 `UnityEngine` 文档里**没有**公开的 "Batches" `ProfilerRecorder` 名字常量（grep `Batch|SetPass` 只命中 Editor/AdaptivePerformance 相关项） | `UnityEngine.*.xml` 全量 grep；T11 的 `drawBatches` 需改用 `ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches")` **字符串**形式并在运行时判 `Valid`，**不能当硬依赖** |

### 1.2 推断（带宽/成本模型，误差 ±40%，**必须真机复核**）

| # | 推断 | 依据 |
|---|---|---|
| I1 | 1080p + 4x MSAA + HDR + SSAO + 两条 blur 链 ≈ **350–400 MB/帧 ≈ 1.5–2.5 ms**（3060 有效 250 GB/s） | 逐 pass 带宽求和；见 §3 预算表 |
| I2 | 165Hz 下两条 blur 链 = **11.2 GB/s ≈ 带宽 3%**，0.19–0.27 ms | F13 × 165 |
| I3 | 重谱面播放主线程 **2.5–5 ms**（临界偏上，非稳定破 6.06 ms） | F15/F17/F19/F21 逐项成本模型 |
| I4 | 窗口化 + flip-model 下 Present 受 **DWM 合成节拍**约束，「贴刷新率、超不上去、偶发 1 帧抖动」 | DXGI flip-model 行为；**需 Alt+Enter 对比实测定性** |
| I5 | D3D11 vs D3D12 在本工程 = **数百 µs** 级 | 无光源 / 无 compute / 无 RT，无 tile memory 收益 |

---

## 2. 目标与非目标

### 2.1 目标（按优先级）

1. **让用户存的刷新率/画质真的生效**（D1/D2）——这是「跑不满 165」的第一性原因。
2. **让性能窗说真话**（D3）——否则后面所有优化都无法验收。
3. **削掉 GPU 上的三项纯浪费**：SSAO（无光源画面做 AO）、无用的 depth/opaque texture copy、Sky mask 超屏 RT。
4. **在不碰表现层常量的前提下瘦身主线程**：小节线固定 AABB、网格量化缓存。
5. **建立可复现的真机基线**：Heavy Chart 采集落盘成 JSON，能在 3060 上一键跑。

### 2.2 非目标（明确不做）

- **不换图形 API**。F1 是硬编码单 API，且 §1.2-I5 证明收益是 µs 级，不值得为此重做构建/兼容回归。
- **不改 `02-invariants.md` 里的任何一条**：B-3（每链一 mesh）、B-4（5 ms 细分）、B-8（RenderQueue 分层）、A-16（`ScaledTimeAt` 唯一入口）、B-10（NoteRenderer 13 参构造）、B-16（可视窗口覆盖整条轨道）全部原样保留。
- **不碰 SRP Batcher 掉出的问题（F19/F20）**。它需要把每音符 MPB 改成 `MaterialPropertyBlock` → 实例化 / `Graphics.RenderMeshInstanced`，属于表现层重构，风险远高于收益（120–200 draw call 在 D3D11 上 ≈ 0.4–1.0 ms，不是墙）。列为 P4「观察项」，只有真机数据证明它是瓶颈才做。
- **不动未完成的移动端 UI 工作区改动**（别人的活）。不 `git checkout` / `reset` / `stash`。

### 2.3 待确认前提（实施前必须先回答）

> **Q1：165Hz 是否真的在线？** 本机 WMI/DEVMODE 枚举被沙箱拒绝，只能从用户陈述 + `preferences.json` 里的 240Hz 间接判断。若显示器实际工作在 120Hz，「跑不满 165」可能根本不成立。
> **Q2：用户的窗口模式？** D1 只在 `BorderlessFullscreen == false` 时生效。若用户已是无边框全屏，D1 不适用，问题会落到 I4。
> **Q3：3060 真机数据。** 本机是 5060 Laptop（F4），工程内无任何 Heavy Chart 产物（F40）。**所有 ms 数都是模型推算。**

**回答方式**：让用户跑一次 §P3-T13 的采集流程，回传 `Diagnostics/FalsumPerformanceCapture.json`（由 T11 新增）。

---

## 3. 预算模型

165Hz 帧预算 = **6.06 ms**。

### 3.1 现状（推断，I1–I3）

| 子系统 | 现状 ms | 性质 | 归属 |
|---|---|---|---|
| GPU 4x MSAA 附件 store/reload/resolve | 0.5–0.8 | 固定，与内容无关 | P2 |
| GPU SSAO（DepthNormals prepass + 4 pass） | 0.2–0.4 | **纯浪费**（无光源） | **P2-T6** |
| GPU depth copy + opaque copy | 0.1–0.2 | **纯浪费**（无采样者，F9） | **P2-T7** |
| GPU 两条 blur 链 | 0.19–0.27 | 固定地板 | P2-T8 |
| GPU Bloom(6 iter) + tonemap + final blit | 0.15–0.25 | 可降 | P2-T9 |
| GPU 透明 overdraw（48 个材质挤 3000–3012） | 0.3–0.6 | 内容相关 | — |
| **GPU 小计** | **1.5–2.5** | | |
| CPU `_world.Refresh` 7 mesh 全量上传 | 0.8–1.5 | 固定，主线程 | **T3 实测否决**（7 个里 5 个 Z 每帧变） |
| CPU `MeasureLineRenderer.Update` 3 mesh | 0.2–0.4 | 固定 | **P2-T4** |
| CPU `BuildGrid` + `Sort` | 0.2–0.5（**实测存疑，可能仅 0.1**） | 固定 | 可选 T5 |
| CPU `NoteRenderer.Update`（逐 visual + 天空链） | 0.8–1.8 | 内容相关 | — |
| CPU 判定引擎 / autoplay / 音频 tick | 0.1–0.3 | | — |
| **CPU 小计**（各项求和 2.1–4.5） | **2.1–4.5** | | |
| 性能窗自身（开窗时） | +0.5–1.5 | **测量污染** | **P1-T10** |

**关键**：GPU 和 CPU 是**并行**的，单帧时间 ≈ `max(GPU, CPU) + 同步开销`。
当 CPU 达 4.5 ms、GPU 达 2.5 ms 时，**墙在主线程**。
这也是为什么「换 D3D12 没用」——它只改善 GPU 提交（数百 µs），不改善 mesh 重建（ms 级）。

### 3.2 目标（P2 确定性项完成后）

| 子系统 | 目标 ms | 手段 |
|---|---|---|
| GPU 小计 | **0.9–1.6** | 关 SSAO（−0.3）、关 depth/opaque copy（−0.15）、Sky mask 金字塔降级（−0.06）、Bloom 6→4 iter |
| CPU 小计 | **2.0–4.4** | 10 个 mesh 固定 AABB（−0.02～0.05）；可选 T5 再 −0.05～0.15 |
| 单帧 | **≤ 4.4 ms** | 余量 1.6 ms |

**必须说清楚的现实**：P2 的 CPU 侧改动（T4/T5）**合计只有 0.05–0.2 ms**，
对 6.06 ms 预算几乎无感。这个计划的真实价值集中在：

1. **P0**（D1/D2）：让用户的显示设置真的生效 —— 这是「跑不满 165」最可能的根因；
2. **P1**（D3）：让诊断不再指错方向；
3. **P2-T6/T7**：砍掉 0.4–0.5 ms 的**确定性 GPU 浪费**（无光源的 SSAO + 无采样者的 depth/opaque copy）。

如果这四组都没解决，那问题就在 `NoteRenderer` 的天空区重建或呈现路径本身，
**需要单独立项**，而不是继续在这个计划里加小优化。

**修正说明（诚实版，两次下修）**：

- **第一次高估**：最初估 CPU 能降到 1.6–3.0 ms（省 0.7 ms），基于「7 mesh 里 6 个可跳过」的错误假设。
- **第二次修正**：核对源码后确认（F45/F45a/F45b + §4-T3 的逐项表），**7 个编辑器 mesh 里只有 `_guideMesh` 不含 `nowMs`**，
  `_ghostMesh` 已有缓存，其余 5 个的 Z 每帧都在变。脏标记收益 ≈ 0 → **T3 否决**。
- **最终量级**：CPU 侧剩下能做的只有「10 个 mesh 的 `RecalculateBounds` → 固定 AABB」（T4）
  和「网格量化缓存」（T5，可选），**合计 0.05–0.2 ms**。
  这不是「省 0.7 ms」，是「顺手清掉一处每帧无谓遍历」。

**最重要的结论**：**本工程「跑不满 165」的主因在呈现路径（D1/D2/I4），不在每帧计算量。**
如果 T13 的真机数据证明 `cpuMainMs` 确实 > 6.06 ms，那瓶颈只能是 `NoteRenderer.Update`
的天空区链重建（android spec §3.1/§3.2 已量化，最坏 810 行 ≈ 3240 顶点/帧）。
**那条路径的优化属于表现层重构（需触碰 B-3/B-4 不变量），不在本计划范围内，需单独立项。**
本计划**不承诺**在 CPU 侧拿到超过 0.3 ms 的收益。

---

## 4. 任务清单

阶段依赖：

```
P0（设置生效）                        P1（测量可信）
  T1 preferences 自愈+版本对齐          T10 性能窗归因 + 165 档 + presentWait
  T2 窗口化刷新率下发                   T11 HeavyChart 采集落盘 JSON
        │                                     │
        └──────────────┬──────────────────────┘
                       ▼
              P2（削恒定开销，每项独立）
  必做（纯资产 / 零热路径）        可选（先测量再决定）
  T6  关 SSAO                          T5  网格量化缓存
  T7  关 depth/opaque copy             T9  Bloom 6→4 iter
  T4  小节线固定 AABB + 瘦身           T3  mesh 脏标记（**实测否决：7 个里只有 1 个可跳**）
  T8  Sky mask 金字塔降级（1+2 步）
                       ▼
              P3（真机验证与收尾）
  T13 3060 基线采集 ──> T14 结果归档 + 05-gaps/00-orientation 更新
```

**写作用域冲突**：T1/T2 都要动 `FalsumChartEditor.cs`（串行）；T4/T6/T7/T8 分属不同文件，可并行；
T10 动 `FalsumPerformanceMonitor.cs` + `FalsumPerformanceWindow.cs` + 两个 localization json；
T11 动 `FalsumHeavyChartPerformanceCapture.cs`（+ 在 monitor 里加一个 recorder 字段）。
**T10 与 T11 都要碰 `FalsumPerformanceMonitor.cs`，必须串行，先做 T10。**

---

### P0 · 让设置真的生效

#### T1 · 偏好自愈 + 版本号对齐

**问题**：F27 + F28 + F29 —— 版本号错位导致每次启动丢弃整份偏好，且坏文件不自愈，AA 被重置回 4x MSAA。

**改法**（三处，缺一不可）：

1. **`FalsumPreferencesStore.LoadFromDisk` 的 catch 分支**（**先做这条，零语义争议**）：
   `PreserveCorruptFile` 之后**必须**把原文件从 `preferences.json` 挪走（改名成 `.corrupt-<ts>`），
   而不是 `File.Copy` 后留着原文件。当前实现让一次失败**永久复现**（F28）。
   改完的效果：坏文件被隔离，下次启动读到「文件不存在」→ `CreateDefault()` → 用户重新设置一次即可恢复。
2. **`FalsumPreferencesMigration.FromDisk` 的宽松解析**（**有语义变更，需评审**）：
   `loaded > CurrentVersion` 时不再直接判不支持，而是按 `LegacyPreferencesDto` 尽力反序列化、
   保留能认出的字段，只有**无法解析成对象**才回落默认。
   理由：偏好文件是**向前兼容友好**的（只有新增字段，无重命名 —— 迁移器注释已确认），
   "未来版本"不等于"不可读"。**但这条改变了既有契约（未来版本 = 未知），见 §8 未决问题 6。**
   保守替代：只做第 1 条 + 第 3 条，接受「当前这份丢一次」。
3. **版本号收敛**：让**新构建**发 10（即重新出包），而不是把工作区降到 8（会丢 v9/v10 已存的字段迁移）。
   注意 `FalsumEditorInfo.Version` 仍是 `0.46-Canary`，出包前应同步 bump 版本串，否则用户分不清包。

**验收**：
- `FalsumSelfCheck.CheckPreferencesMigration` 新增 3 条断言：
  - `loaded = CurrentVersion + 1` 的文件 → **不** `UnsupportedVersion`，且 `Display` 字段被保留；
  - 不可解析的垃圾文件 → 回落默认，且原文件**被改名**（目录里只剩 `.corrupt-*`，无 `preferences.json`）；
  - 二次 `Load()` 在改名后返回默认值且**不再抛异常**。
- 手工：把 `Version` 改成一个更大的数写进 `preferences.json`，启动一次，确认分辨率/刷新率/AA 被保留。

**风险**：低。第 1 条改变了「未来版本 = 未知」的既有契约，需要在 `05-gaps.md` 记录这处语义变更。
**写作用域**：`FalsumPreferences.cs`、`FalsumPreferencesMigration.cs`、`FalsumPreferencesStore.cs`、`FalsumSelfCheck.cs`。
**估时**：0.5 天。

---

#### T2 · 窗口化模式下也下发刷新率

**问题**：F25 —— 窗口化时 `ApplyResolution` 整段跳过，存的刷新率是装饰值。

**方案对比**：

| 方案 | 做法 | 优点 | 缺点 |
|---|---|---|---|
| **A（推荐）** | 窗口化时**仍调用** `Screen.SetResolution(width, height, Windowed, refreshRate)` | 一行改动，语义直白 | 窗口化 + flip-model 的呈现节流仍在（I4），效果有限 |
| B | 窗口化时把刷新率**降级为「无偏好」**，UI 上明确灰掉分辨率/刷新率控件 | 诚实（不假装生效） | 用户仍拿不到高刷 |
| C | 窗口化时引导/自动切到 `FullScreenWindow` | 真正拿满高刷（I4） | 改变用户窗口习惯，需产品决策 |

**建议**：**A + B 组合** —— 窗口化也下发刷新率（成本近乎为零），同时把「窗口化下高刷受 DWM 节流」
作为提示文案写进偏好页（走 `FalsumLocalization`，键名 `falsum.preferences.display.windowedHint`）。
C 单独作为一个开关暴露，不默认打开。

**改法**：
1. `FalsumChartEditor.ApplyDisplayPreferencesAtStartup`：`applyResolution` 改为恒 `true`（不再是 `BorderlessFullscreen`）。
2. `FalsumDisplaySettingsController.ApplyResolution`：窗口化分支补 `Screen.SetResolution(..., Windowed, BuildRefreshRate(refresh))`。
3. `FalsumChartEditor.ToggleBorderlessFullscreen`：刷新率改用 `_preferences.Display.ResolutionRefreshRate` 而非回读（F26）。
4. 新增 l10n 键 × 2 语言。

**验收**：
- 自检新增 `CheckDisplayResolutionApply`：用假 `FalsumDisplayPreferences`（1366×768@165）调用
  `FalsumDisplaySettingsController` 的纯函数部分，断言 `BuildRefreshRate(165) == {165,1}`；并断言
  「窗口化分支也会走到 `SetResolution`」这一路径的分支覆盖（把 `ApplyResolution` 的可测部分提取成返回
  `(w,h,mode,refresh)` 记录的纯函数，避免自检里真的调 `Screen.SetResolution`）。
- 手工：窗口化 → 改刷新率 → 重启 → 用 `Screen.currentResolution.refreshRateRatio` 打日志确认。
- `python Tools/falsum_localization_audit.py` 通过（新键）。

**风险**：中低。`Screen.SetResolution` 在窗口化下传不支持的刷新率，Unity 会静默回退（文档口径 "if supported"）。必须**不**把它当错误抛。
**写作用域**：`FalsumChartEditor.cs`、`FalsumDisplaySettingsController.cs`、`FalsumDisplayPreferencesPage.cs`、两个 localization json、`FalsumSelfCheck.cs`。
**估时**：1 天（含自检）。

---

### P1 · 让测量可信

#### T10 · 性能窗归因修正 + 165 档

**问题**：F31 + F32 + F35 —— 归因链在 `CpuMainThreadMs > 0` 时无条件判主线程；预算档没有 165。

**改法**：
1. **读取 present wait**：`FrameTiming.cpuMainThreadPresentWaitTime` **确实存在于 6000.3.26f1**
   （F41，已核对 `UnityEngine.CoreModule.xml`），表达为 "The CPU time the last frame spent in
   waiting for Present on the main thread, in ms"。
   `ReadFrameTimings` 补读它，`FalsumPerformanceMonitor` 加 `float PresentWaitMs`。
   **顺手记录 `timing.syncInterval`**（同一结构体，公开字段）—— 它直接说明 Present 用的同步间隔，
   是客观判定「是否被 vsync 节流」的证据，不需要靠窗口化/无边框对比猜测。
2. **调整归因顺序**（关键）：在 `CpuMainThreadMs` 分支**之前**插入
   「`PresentWaitMs >= budget * 0.35` **或** `PresentWaitMs >= FrameMs * 0.3` → 归因『呈现等待 / 刷新率节流』」。
   这条分支必须**先于** CPU 主线程判定，否则永远不可达。
3. `CycleTargetFps` 追加 **165**（序列 30 → 60 → 120 → 144 → 165 → 30）；`TargetFps` 默认值仍 60。
4. l10n：新增 `falsum.performance.bottleneck.presentWait` + `falsum.performance.detail.presentWait`。

**验收**：
- 自检新增 `CheckPerformanceAttribution`：纯函数化归因逻辑
  （把 `UpdateBottleneck` 的判定提成 `static FalsumBottleneckKind Classify(samples, budget)`），
  用 4 组构造样本断言：① 纯呈现等待（main=1, present=5, budget=6.06）→ `PresentWait`；
  ② 纯主线程（main=8, present=0）→ `CpuMain`；③ GPU 主导（gpu=9 → `Gpu`）；④ 预算内 → `Flow`。
- 手工：窗口化 + 165 屏，开性能窗，确认不再显示「CPU 主线程瓶颈」。

**风险**：中。归因逻辑是纯诊断代码，不影响渲染；但 `Classify` 提纯要注意**不要改变现有 4 个分支的相对优先级**（Gpu > CpuRender > CpuMain 顺序保留）。
**写作用域**：`FalsumPerformanceMonitor.cs`、`FalsumPerformanceWindow.cs`、两个 localization json、`FalsumSelfCheck.cs`。
**估时**：1 天。

---

#### T11 · Heavy Chart 采集落盘 JSON

**问题**：F40 —— 采集工具只 `Debug.Log`，产物无法回传比对。

**改法**：
1. `FalsumHeavyChartPerformanceCapture.LogMetrics` 之后，把指标写成
   `Diagnostics/FalsumPerformanceCapture.json`（UTF-8 无 BOM、LF），字段：
   `{ "schema": 1, "chart": "<package>", "resolution": "1920x1080", "gfxApi": "...", "gpu": "<Renderer 串>",
   "p95Ms":…, "peakMs":…, "worstMs":…, "stutterFrames":…, "frameMs":…, "cpuMainMs":…, "cpuRenderMs":…,
   "gpuMs":…, "presentWaitMs":…, "scriptMs":…, "allocKb":…, "gc":…, "drawBatches":… }`
2. 新增 `drawBatches` 来源：`FalsumPerformanceMonitor` 补一个
   `ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches")`。
   **注意 F46**：Unity 6000.3.26f1 的公开 API 文档里查不到这个 marker 名的常量，
   所以必须**运行时判 `recorder.Valid`**，无效就写 `-1` 并置 `"drawBatchesAvailable": false`。
   **不能**把它做成硬依赖，否则在不支持的环境下会抛异常或写 0 误导人。
   （若实测 `Valid == false`，退而在编辑器构建里用 `UnityEditor.Rendering.RenderPipelineManager` 或直接放弃该字段 —— 它只是辅助信息。）
3. `LogMetrics` 里的 `Debug.Log` 保留（人读），JSON 用于机器比对。
4. **同时把 gfx API 与 GPU 串写进 JSON** —— 这样回传的文件自证「这是 3060 / D3D11」。

**验收**：
- 用 `INFALSUS_PERFORMANCE_PACKAGE` 指向 `../export/falsum/tokonagi.falsum` 跑一次，
  断言 `Diagnostics/FalsumPerformanceCapture.json` 存在且 `schema == 1`、`p95Ms > 0`。
- 采集**不**必须在 3060 上做；本机跑通即可证明工具链可用。

**风险**：低（只在编辑器工具里加输出）。
**写作用域**：`FalsumHeavyChartPerformanceCapture.cs`、`FalsumPerformanceMonitor.cs`（仅加一个 recorder 字段）。
**估时**：0.5 天。

---

### P2 · 削恒定开销

> **统一验收口径**：每个 P2 任务都要在**同一场景 + 同一谱面 + 同一分辨率**下，
> 用 T11 的 JSON 采到 `frameMs / cpuMainMs / gpuMs` 三项 before/after。
> 没有 before/after 数字的优化**不得合并**（这是本计划唯一的性能验收方式）。
> 另需一组 `Diagnostics/Screenshots/` 前后对比图，证明**视觉零差异**（除 T8/T9 的显式画质变更）。

#### T3 · 编辑器 mesh 脏标记（**已实测否决，保留记录**）

> **判定：不做。** 本节保留作为「为什么不做」的证据记录，避免后来者重复评估。

**问题**：F15 + F16 —— `_world.Refresh` 每帧无条件重建 7 个 mesh 并全量上传。
最初判断「大部分内容帧间不变」，因此估了 −0.7 ms 的收益。**这个判断是错的。**

**先判定哪些真的能跳过**：由 F45，`NoteZAt` 显式依赖 `nowMs`（`(ScaledTimeAt(note) - ScaledNowAt(now)) / 1000 * …`），
所以**凡是 Z 随播放头变化的 mesh 都不能整体跳过**。逐项判定（每行都已核对源码）：

| mesh | Z 随 `nowMs` 变？ | 能否跳过 | 依据 |
|---|---|---|---|
| `_gridMesh` | **是** | **不能** | `BuildGrid` 逐线 `NoteZAt(time, now)`（`:2011-2012`） |
| `_rulerMesh` | **是** | **不能** | `BuildRuler` 用 `ModePlaneLocalPosition(timePlane, now)`（`:887-889`） |
| `_guideMesh` | **否** | **能** | `BuildGuide`（`:1062-1100`）只用 `ruler` + `_lineLocal.z` / `_trackEndZ` 两个**常量**，**完全不含 `nowMs`** |
| `_overlayMesh` | **是** | **不能** | `BuildSelection(nowMs, …)` → `AddSelectionBox` 的 Z 来自音符几何 + 播放头 |
| `_planeMesh` | **是** | **不能** | `BuildTimePlane(nowMs, timePlaneMs, …)` |
| `_ghostMesh` | **是** | 已有 `_lastGhostKey` 缓存 | `UpdateGhosts(nowMs, …)`；F38a |
| `_cursorMesh` | **是** | **不能** | `BuildSnapArrow` 的 `z = center.z - 0.012f`，`center = ModePlaneLocalPosition(timePlaneMs, nowMs)`（`:840-848`） |

**结论（第二次修正）**：7 个里只有 **1 个**（`_guideMesh`）能靠脏标记跳过。
`_ghostMesh` 已有缓存（F38a），其余 5 个的 Z 每帧都随播放头变化。
`_overlayMesh` 在无选中时虽然为空（F44），但空 mesh 的 `Apply()` 本身已是 `Renderer.enabled = false` 的早退
（`FalsumEditorWorld.cs:200-208`），**跳过它没有收益**。

> **T3 的预期收益从最初估算的「−0.7 ms」下修两次，最终为「≈0，不建议做」。**
> 详见 §8 未决问题 2。**T3 应从任务清单中移除，除非 T11 的真机数据推翻了 F45 的结论。**

**改法（仅作为假设记录，不建议实施）**：
- 即便只对 `_guideMesh` 加脏标记，收益也只是省掉一个约 30 顶点级别 mesh 的上传 —— **属于噪声量级**。
- 若真要动这里，正确方向是**把 `Mesh.RecalculateBounds()` 换成固定 AABB**（`MeshBuffer.Apply`，见下方），
  那是 7 个 mesh 每次都要付的 O(顶点数) 遍历，比脏标记实在。
- 注意 `Mesh.SetNormals(Normals)` **不能删** —— `FalsumEditorGridLine.shader` 把 NORMAL 当切线，
  用来做「屏幕像素宽度挤出」。

**若仍要确认（不推荐）**：
- 自检新增 `CheckEditorMeshDirtyKeys`：`_guideMesh` 单 mesh 的键相等性断言。
- 视觉：`Diagnostics/Screenshots/` 前后对比图**逐像素一致**。
- 性能：**先实测再定阈值**。若 `cpuMainMs` 变化在噪声内（< 0.05 ms），确认否决结论。

**风险**：中。脏标记漏一个键 = 画面停在旧位置（本仓库历史上踩过同类坑：sky 缓存的键漏 `TimeMs/DurationMs`
导致拖动后选框停在旧位置，见 android spec §4.2c）。
**写作用域**：`FalsumEditorWorld.cs`、`FalsumSelfCheck.cs`。
**估时**：**0（不做）**。若某天被迫做，1 天。

**替代建议（真正值得做、收益确定性更高）**：把 `MeshBuffer.Apply` 的
`Mesh.RecalculateBounds()`（`FalsumEditorWorld.cs:225`）改成固定 AABB。
7 个 mesh 每帧都要付一次 O(顶点数) 遍历，而编辑器几何的可见范围是确定的常量区域。
这与 T4 的第 1 条是同一个手法，**合并成一项做**（见 T4）。

---

#### T4 · 每帧 mesh 上传瘦身：10 个 mesh 的 `RecalculateBounds` → 固定 AABB

**问题**：F17 —— 每帧清 3 组 List + 逐小节线重算 + 3 次 `ApplyMesh`（每次 `Mesh.Clear + SetVertices + SetColors + SetTriangles + RecalculateBounds`）。

**先说清楚什么是做不到的**：由 F45，`NoteZAt` 显式依赖 `nowMs`，所以小节线的 Z **每帧都在变**，
不可能用脏标记整体跳过。只能瘦身单帧成本。

**改法**（按风险从低到高，全部可独立提交）：

1. **`RecalculateBounds` → 直接赋固定 AABB**（零风险）。涉及两处、共 10 个 mesh：
   - `MeasureLineRenderer.ApplyMesh`（`MeasureLineRenderer.cs:305`）的 3 个 mesh；
   - `FalsumEditorWorld.MeshBuffer.Apply`（`FalsumEditorWorld.cs:225`）的 7 个 mesh。
   两者的可见范围都是确定的常量区域（轨道 ±`_trackEndZ` / 编辑器画布固定一小块），
   每帧 10 次 O(顶点数) 遍历可以整体变成 `mesh.bounds = _fixedBounds`。
   **这是本计划里唯一一处「改一处、覆盖 10 个 mesh」的清扫项。**
2. **`new Color(0.72f, 0.94f, 1.0f, 0.92f)` 提成 `static readonly Color`**（零风险；在 `for` 内每行 new 一次，是 struct 不产生 GC，纯粹是可读性 + 微小收益）。
3. **`AddSideLine` 的空几何早退**（**收益为零，仅列作已知行为，不需要改**）：
   `_leftGeometry` / `_rightGeometry` 为 null 时（侧轨未绑定）`AddSideLine` 会 early return，
   该 mesh 的顶点数为 0 → `ApplyMesh` 走 `Renderer.enabled = false` 早退，**本来就没有上传**。
   这条**不要动**：把它「优化」成跳过整条 mesh 会让 renderer 保持上一次的可见状态，是 bug 而不是优化。
4. **不做**「3 mesh 合并成 1」（详见下方否决理由）。

**明确否决：3 mesh 合 1**

F42 显示三个 mesh 各持一份 `new Material(shader)`（同 shader、同 queue 3035）。
它们承载的是**三块不同物理位置**的几何（主判定线 / 左 Shift 轨 / 右 Space 轨），
合并后仍需要分别着色，而 `MeasureLine.shader` 的顶点色已经承载了颜色差异 —— 理论上能合。
但：
- 合并要重算三个 `SideGeometry` 的坐标到单一 mesh 空间，**这是表现层坐标重构，属于「逆向恢复的视觉重现」范畴**；
- 收益只是省 2 次 mesh 上传（约 0.05–0.1 ms），
- 风险是侧轨位置偏移（`02-invariants.md` §B 的表现常量）。
**收益/风险比不成立，不做。** 只做第 1/2/3 条。

**验收**：视觉零差异（小节线 + 编辑器网格前后截图）；`cpuMainMs` 下降**以实测为准**。
**收益的诚实量级**：`RecalculateBounds` 是 O(顶点数) 的简单遍历。按本工程实际顶点量估算
（小节线 3 mesh 各 ~1000–1800 顶点、编辑器 7 mesh 合计 ~2–3 千顶点），
**总计约 6–12 k 顶点/帧，收益量级 0.02–0.05 ms —— 是「零风险清扫」，不是性能救命项。**
不要把它当成解决 165Hz 的手段；它的价值是**消除一处每帧无谓的全量遍历**，顺手做掉。
**风险**：低。固定 AABB 赋值必须**恰好**覆盖所有可见几何，否则会被视锥剔除掉一片 —— 用「比实际最大范围再放大 10%」保守处理。
**另外**：`Mesh.bounds` 同时被 CPU 用于视锥剔除，所以 AABB 偏小会**静默丢失物体**，这是本任务唯一真实的坑。
建议先只改 `MeasureLineRenderer`（3 个 mesh、几何范围明确），验证无视觉差异后再改 `FalsumEditorWorld`（7 个 mesh）。
**注意 F43**：`MeasureLineRenderer` 没有 `Dispose`，mesh/material 不显式释放。这不是性能问题（场景唯一、长生命周期），
但若后续加 `Dispose` 要一并处理，别把这次改动变成引入泄漏的借口。
**写作用域**：`MeasureLineRenderer.cs`、`FalsumEditorWorld.cs`、`FalsumSelfCheck.cs`。
**估时**：0.5 天。

---

#### T5 · 网格线 CPU 侧瘦身（`BuildGrid`）

**问题**：F18 —— 每帧 `FalsumBeatGrid.Build`（生成 + `Sort` + 合并）后逐线生成 6 个 quad。

**先纠正一个常见误判**：`_scratch.Sort((a,b) => a.TimeMs.CompareTo(b.TimeMs))` 里的 lambda
**不捕获任何变量**，C# 编译器会把它缓存成 `static` 委托字段，**不是每帧分配**。
所以「替掉 Sort 的 lambda」无收益。真正每帧花的是 `List.Sort` 的 O(n log n) 本身。

**改法**（按收益排序）：

1. **量化窗口键缓存 `Build` 结果**（主收益）。输入的 `from/to` 由 `nowMs` 推出：
   `from = max(0, now-2000)`、`to = max(now+2000, visibleEnd)`（`FalsumEditorWorld.cs:2001-2004`）。
   把 `from/to` **向外**量化到 100 ms 网格（`floor(from/100)*100` / `ceil(to/100)*100`），
   键 = `(chart.TimelineVersion, quantFrom, quantTo, Subdivision)`，命中就复用 `_merged`。
   **必须向外量化**（扩大范围），否则边缘线会闪烁。
   100 ms 量化意味着：正常播放时键入约 20 次/秒才变一次 → **~92% 的帧完全跳过 Build+Sort+merge**。
2. **`AddHorizontalGridLine` / `AddGridSideLine` 的写入前置剪枝**：现有代码是「先 Build 全部线，再在
   `for` 里 `if (z < ... || z > ...) continue`」（`FalsumEditorWorld.cs:2008-2017`）。
   把 z 判定提到 `AddHorizontalGridLine` 之前已经做到了，但 `Build` 已经付过成本 —— 只有第 1 条能真正省。
3. **不做**「把剔除提前到 `Build` 内部」：`Build` 是纯时间域生成，不知道 Z 映射（`NoteZAt` 在
   `TrackMotion` 里），把 Z 逻辑塞进 Core 层会破坏分层。

**修正后的收益预期**：`Build` 本身是「时间域步进 + 排序」，量级与窗口内线数成正比，
而在 `MaxLines = 20000` 的硬顶下普通谱面窗口约 37–80 条线。
**因此第 1 条收益大概 0.05–0.15 ms，不是我上一版假设的 0.2–0.5 ms。**
→ 本任务优先级与 T3 同级（可选），先测量再投入。

**验收**：视觉零差异（网格线前后截图，必须含**变速段 `track()`** 的谱面）；`cpuMainMs` 实测下降。
**风险**：低。量化窗口向外取整是唯一需要小心的点。
**写作用域**：`FalsumEditorWorld.cs`（`BuildGrid`）、`FalsumBeatGrid.cs`（缓存）。
**估时**：0.5 天。

---

#### T6 · 关闭 SSAO

**问题**：F7 —— 无光源的 2D 画面上开 SSAO，它强制生成全屏 DepthNormals prepass + 4 个 pass。

**改法**：`Assets/Settings/PC_Renderer.asset` 的 `ScreenSpaceAmbientOcclusion` 块 `m_Active: 1` → `0`。
**或者**（更好）：把 SSAO 从 `PC_Renderer` 里**删除**，因为本工程不存在任何需要 AO 的画面（无光源、无 3D 体积）。

**验收**：前后截图**逐像素对比** —— 若为零差异，说明它本来就没起作用（最可能的结果，因为 AO 依赖法线/深度且
画面是半透明 quad 堆叠）；若有差异，说明它在压暗某些边缘，需确认是否可接受。
**风险**：**极低**。这是本计划性价比最高的一项。
**写作用域**：`Assets/Settings/PC_Renderer.asset`。
**估时**：0.2 天（含截图）。

---

#### T7 · 关闭 `m_RequireDepthTexture` / `m_RequireOpaqueTexture`

**问题**：F9 —— 这两个开关每次都产生 CopyDepth / CopyOpaqueColor pass，但**全仓 30 个 shader 没有一个采样它们**。

**改法**：`PC_RPAsset.asset:22-23` 两个都改 `0`（`Mobile_RPAsset` 本来就是 0，可直接对齐）。

**前置校验（必须做，不能跳）**：
1. 再次全量 grep：`_CameraDepthTexture`、`_CameraOpaqueTexture`、`SampleSceneDepth`、`SampleSceneColor`、
   `DeclareOpaqueTexture`、`DeclareDepthTexture`（**含 `.hlsl`/`.cginc`/`.compute`/`Resources` 目录**）。
2. 确认两个自定义 feature **不**隐式依赖相机深度：`SkyJudgementLineEffectFeature` / `FalsumTrackEndComposerFeature`
   的 `RecordRenderGraph` 里都**没有** `ConfigureInput(ScriptableRenderPassInput.Depth)`（已核对）；
   它们用自己 Alloc 的 mask RT，不读 `_CameraDepthTexture`。
3. 关掉后跑一次 **游戏内截图 + 编辑器截图**，逐像素对比。

**验收**：截图逐像素一致；`gpuMs` 下降 ≥ 0.1 ms。
**风险**：低（F9 是全量 grep 结论，但 grep 不能证明运行时无 `Shader.SetGlobalTexture` 之类；所以第 3 条是保险）。
**写作用域**：`Assets/Settings/PC_RPAsset.asset`。
**估时**：0.3 天。

---

#### T8 · Sky 链 mask RT 降级

**问题**：F11 + F14 —— Sky 链 mask 在 1080p 下是 **1536×1536（比屏幕还大）**，是两条链里更贵的一条；
且每帧 CPU 跑 1448 个三角形的 PerlinNoise + `SetData`。

**改法**（分三步，**前两步零表现常量改动，可独立提交**）：

1. **金字塔层级 2 级 → 1 级**（**主收益，零常量改动**）：去掉 `DownTwo/UpTwo` 两次 blit（7 → 5 Blit）。
   `SkyJudgementLineEffectFeature.RecordRenderGraph` 里 `downTwo`/`upTwo` 两张纹理与 3 次 `AddBlit` 一起删掉；
   `_blurredMaskTexture`（`2048×512×scale`）保留，它是 pyramid 的终点。
2. **`_opacity` 的 `SetData` 改为隔帧上传**（**零常量改动**）：PerlinNoise 频率是 19.5/17.7，
   噪声本身逐帧变化极小，隔帧视觉无差异。同时把 `UpdateOpacityBuffer` 的 1448 次 PerlinNoise 计算也隔帧执行。
3. **`MinimumMaskScale` `0.75 → 0.6`**（**可选，需视觉负责人确认**）：1080p 下 mask 从 1536² → 1228²，像素量 −36%。
   **注意**：这是逆向出来的下界常量（注释说为了「小窗口下 8×90 三角形仍然清晰」），
   见 §8 未决问题 3。**没有视觉负责人确认就不做这一步。**

**验收**：
- 视觉：**必须有** `SkyJudgementLine` 的前后截图对比。这是本任务唯一不可省的验证。
- 性能：`gpuMs` 下降（第 1 步约 0.05–0.08 ms）；CPU `scriptMs` 下降（第 2 步约 0.03–0.05 ms）。
- **第 3 步单独验收**：`MinimumMaskScale` 改动必须逐档（0.75/0.6）出对比图。
**风险**：第 1/2 步低；第 3 步中（改观感 + 碰表现常量）。
**写作用域**：`SkyJudgementLineEffect.cs`、`SkyJudgementLineEffectFeature.cs`。
**估时**：0.5 天（1+2 步）；+0.5 天（第 3 步含观感确认）。

---

#### T9 · Bloom 降级（可选，产品决策）

**问题**：`DefaultVolumeProfile.asset` 的 Bloom 是 `maxIterations: 6` + `downscale: 0` + `highQualityFiltering: 0`。
但注意：`FalsumDisplaySettingsController.ApplyEditorPostProcessClarity` **在编辑器模式下**会把
`Bloom.intensity` override 成 `0.0`、`Tonemapping` 成 `None`、`Vignette` 成 `0`（一个 priority=1000 的全局 Volume）。

**结论**：**编辑器模式下 Bloom 实际不消耗**（intensity 被 override 到 0，URP 会跳过 iterating，只留一次 blit）。
所以本任务**只对「游玩态 / 谱面预览」有意义**。

**改法**：`maxIterations: 6 → 4`。仅当 T13 真机数据显示游玩态 GPU 超预算时才做。
**验收**：游玩态截图对比 + `gpuMs`。
**风险**：中（观感变化）。**建议本轮不做**，列为观察项。
**写作用域**：`Assets/Settings/DefaultVolumeProfile.asset`。
**估时**：0.5 天。

---

### P3 · 真机验证与收尾

#### T13 · 3060 基线采集

**流程**（交付给用户，直接发这段）：

```powershell
# 1. 关掉所有其它 3D 程序，显示器确认工作在 165Hz（NVIDIA 控制面板 → 更改分辨率）
# 2. 用 T11 之后的新构建
$exe = 'Builds\FalsumEditor-<Version>\FalsumEditor.exe'
# 3. 分别采集「窗口化」和「无边框全屏」两组
& $exe
# 在编辑器里：Alt+Enter 切无边框 → 打开性能窗 → 播放最重的一张谱 60 秒 → 截图性能窗
# 4. 把 Diagnostics\FalsumPerformanceCapture.json 和截图回传
```

**判定树**（注意 §8-5：`syncInterval` 已在 T10 记进 JSON，所以第一层不必靠对比猜）：

```
JSON.syncInterval
├─ > 0  → Present 被 vsync 节流，帧率被人为贴到刷新率 → 关 vSync 或改用无边框
└─ == 0 → 无 vsync。看 presentWaitMs
          ├─ presentWaitMs 占比高 → DWM 合成节流（I4）。对比窗口化/无边框两组 p95 定性
          └─ presentWaitMs 低 → 真计算瓶颈，看 cpuMainMs / gpuMs
                ├─ cpuMainMs > 6.06 → 主线程瓶颈
                │     └─ 若已知 NoteRenderer 天空链是主因 → 需单独立项（不在本计划）,
                │        T3/T5 的 0.15–0.3 ms 收益不够
                ├─ gpuMs > 6.06 → P2-T6/T7/T8 的收益是决定性的
                └─ 两者都 < 6.06 且 frameMs > 6.06 → 归因缺失项（提交/驱动/DWM），
                                                        先补 T10 的提交分段再判断
```

#### T14 · 结果归档

- 更新 `docs/memory/05-gaps.md`：删除/标记 G-1、G-2（已在本轮前的会话中修好与否需复核），新增本轮发现的三条缺陷（D1/D2/D3）及其状态。
- 更新 `docs/memory/00-orientation.md`：把「D3D11」那句补上「D3D11 不是性能瓶颈，见 plan 2026-11-14」指针。
- 把 T11 的 JSON 产物放进 `Diagnostics/`，并在 `03-pipeline.md` 的门禁表里加一行「性能采集」。

---

## 5. 阶段门禁

**任何一个任务结束都必须能编译**：

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$proj  = (Get-Location).Path
& $unity -batchmode -quit -projectPath $proj -logFile Diagnostics\UnityCompile.log
```

**每个阶段结束跑全套**：

```powershell
# 1) 编译
& $unity -batchmode -quit -projectPath $proj -logFile Diagnostics\UnityCompile.log
# 2) 编辑器自检（必须 passed=true，当前基线 37 项 F39）
& $unity -batchmode -quit -projectPath $proj -logFile Diagnostics\UnityFalsumSelfCheck.log `
  -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run
Get-Content Diagnostics\FalsumSelfCheck.json -Raw
# 3) 本地化审计（新增 l10n 键时必须跑）
python Tools\falsum_localization_audit.py
# 4) Phase A 自检（动了 Presentation 层才需要）
& $unity -batchmode -quit -projectPath $proj -logFile Diagnostics\UnityPhaseA.log `
  -executeMethod InFalsus.EditorTools.GameplaySelfCheck.Run
```

**P2 的每个任务额外要求**：before/after 的 `FalsumPerformanceCapture.json` + 截图对比。**没有这两个不进下一个任务。**

---

## 6. 回滚策略

| 任务 | 回滚方式 | 风险窗口 |
|---|---|---|
| T1 | 恢复 `UnsupportedVersion` 判定；恢复 `File.Copy` | 低（只会回到「设置丢失」的现状） |
| T2 | `applyResolution` 改回 `BorderlessFullscreen` | 低 |
| T3 | 删掉脏标记，`Refresh` 无条件重建（现状） | **已否决，无回滚需要** |
| T4/T5 | 删缓存 / 恢复 `RecalculateBounds` | 低 |
| T6/T7/T9 | 改 YAML 数值回原值（`m_Active: 0 → 1`、`m_Require*Texture: 0 → 1`、`maxIterations: 4 → 6`） | **极低**（纯资产，无需改代码） |
| T8 | 金字塔回 2 级、`SetData` 回每帧 | 低（**不动 `MinimumMaskScale`，见 §8-3**） |
| T10 | `PresentWaitMs` 分支删掉，归因顺序恢复 | 低 |
| T11 | 删 JSON 输出 | 低 |

**明确禁止**：`git checkout` / `git reset` / `git stash`（工作区有别人未提交的 100+ 改动）。回滚一律手工改回。

---

## 7. 工作量汇总

| 阶段 | 任务 | 估时 |
|---|---|---|
| P0 | T1 偏好自愈 | 0.5 |
| P0 | T2 刷新率下发 | 1.0 |
| P1 | T10 归因 + 165 档 | 1.0 |
| P1 | T11 采集落盘 | 0.5 |
| P2 | T4 小节线 + 编辑器 mesh 固定 AABB（10 个 mesh） | 0.5 |
| P2 | T6 关 SSAO | 0.2 |
| P2 | T7 关 depth/opaque | 0.3 |
| P2 | T8 Sky mask 金字塔降级（1+2 步） | 0.5 |
| P2（可选） | T8 第 3 步 `MinimumMaskScale` | 0.5 |
| P2（可选） | T5 网格缓存（需先测量） | 0.5 |
| P2（可选） | T9 Bloom | 0.5 |
| ~~P2（否决）~~ | ~~T3 mesh 脏标记~~ | ~~0~~ |
| P3 | T13/T14 真机验证 + 归档 | 1.5 |
| | **确定性工作量合计** | **6.0 人日** |
| | **含全部可选任务** | **7.5 人日** |

**最小可交付子集**（如果只有 1.5 天）：**T6 + T7 + T10**。
理由：T6/T7 是**纯 YAML 资产改动**（`m_Active: 1 → 0`、`m_Require*Texture: 1 → 0`），
零代码风险、可独立提交、可独立回滚，直接砍掉 GPU 侧两项纯浪费；
T10 让用户看到真实归因，而不是被误报成 CPU 瓶颈。
**这三项加起来不动一行热路径代码。**

**次小可交付（3 天）**：上面三项 **+ T1 + T2 + T11** —— 把「设置不生效」和「测量不可信」两个根问题一起解决，
并让用户能回传真机数据。

---

## 8. 未决问题

1. **Q1–Q3（§2.3）必须先回答**，否则 T13 的判定树无法收敛。
2. **T3 已被实测数据否决，T5 收益也需先测量**。F45/F45a/F45b 逐项核对源码后确认：
   7 个编辑器 mesh 里**只有 `_guideMesh` 不含 `nowMs`**（`_ghostMesh` 已有缓存，其余 5 个的 Z 每帧都变）。
   所以 T3 的收益 ≈ 0，**建议直接从任务清单移除**。
   若仍想确认，投入前先跑 T11 采集「空选择 + 播放中」的 `cpuMainMs`：
   - 若 `cpuMainMs < 3 ms` → **T5 也砍掉**，不值得动热路径；
   - 若 `cpuMainMs > 5 ms` → 瓶颈大概率在 `NoteRenderer.Update` 的天空链重建，
     **T5 仍不解决根因**，应单独立项（android spec §3.1 已有量化数据）。
3. **T8 的 `MinimumMaskScale = 0.75` 下界是逆向出来的魔数**（注释说是为了「小窗口下仍然清晰」，
   见 android spec §2.3）。改动它属于「碰逆向恢复的表现常量」。
   保守做法：**只做「金字塔 2 级 → 1 级」（零常量改动）+「`SetData` 隔帧」**，
   `MinimumMaskScale` 保持 0.75 不动，直到有视觉负责人确认。
4. **T9 完全不建议本轮做**：`FalsumDisplaySettingsController.ApplyEditorPostProcessClarity`
   在编辑器模式下已经把 Bloom `intensity` override 到 0（一个 priority=1000 的全局 Volume），
   收益只存在于游玩态，而游玩态正是「和原版一模一样」的视觉目标所在（`00-orientation.md` §2）。
   改它要承担画面重现度风险，收益不确定。
5. **`FrameTiming.syncInterval` 可以直接用于验证 I4**。F41 显示它是公开字段，
   表示 Present 时使用的同步间隔。T10 顺手把它记进 JSON，就能**客观**回答「是否被 vsync 节流」，
   不需要靠 Alt+Enter 对比猜测。**建议把这条提前到 T10，而不是等 T13。**
6. **T1 的语义变更需要评审**。「未来版本号 ≠ 不可读」这个契约变更影响所有后续版本升级路径，
   建议与 `02-invariants.md` 的维护者确认后再动；保守替代方案是**只修第 2 条**（`PreserveCorruptFile` 改名），
   这样坏文件不再永久复现，用户手动删掉 `.corrupt-*` 后能自愈，但当前这份还是丢一次。
