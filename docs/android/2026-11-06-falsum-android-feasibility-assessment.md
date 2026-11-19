# 可行性评估：把 InFalsus-Gameplay 出到安卓（重点：性能）

> 状态：**评估结论，未实现任何改动**（2026-11-06）。
> 口径基准：中端安卓平板/手机（骁龙 7 系 / Mali-G / Adreno 中端，1080p 左右，目标 60 fps）。
> 证据分级：本文全部结论来自**静态源码/资产阅读 + 谱面语料统计**；本机无 Android 工具链、
> 无真机、未跑任何 Unity 构建。凡写「未实测」的，都是需要上真机才能定的数，不要当结论引用。
> 所有 `path:line` 相对仓库根 `InFalsus-Gameplay/`。

---

## 0. 结论摘要

### 0.1 一句话

**技术上可以做，但不是「加个平台再打个包」——是「换掉输入层、换掉文件层、换掉音频后端、重做一套移动端渲染档」的移植工程。** 性能上真正的难点不是画质，而是两处「与音符数量无关的每帧恒定开销」：天空区 mesh 每帧整链重建（CPU），以及两条全屏 5-tap blur 链（GPU，PC 档专属）。

### 0.2 分层结论

| 层 | 可行性 | 主要代价 | 性能判断 |
|---|---|---|---|
| **游玩层**（`GameplayRuntime` 场景 + 判定 + 表现） | **可行**，但当前形态无法作为「音游」发布 | 无玩家输入通道（现为 autoplay 诊断驱动）；判定抖动预算未实测 | GPU 与 CPU 都**必须降级**才能稳 60 fps（§4.1 为估算，非实测） |
| **FALSUM 制谱器**（编辑器） | **部分可行**：10 类不可达动作中约 4 项补入口、6 项砍掉 | 输入层缺 10 类动作入口；文件 I/O 与 ffmpeg 全线失效 | 编辑器比游玩更重（UI Toolkit 全量重建 + 3D 拾取），小屏体验是**产品问题不是性能问题** |

### 0.3 三个必须先解决的前置项（按顺序）

1. **工具链**：本机 Unity 6000.3.26f1 **没有装 AndroidPlayer 模块**，也没有 SDK/NDK/JDK/adb — 现在连包都出不来。
   `Editor\Data\PlaybackEngines` 只有 `MetroSupport` + `windowsstandalonesupport`；`ANDROID_HOME`/`JAVA_HOME` 为空，
   `%LOCALAPPDATA%\Android\Sdk` 不存在，`adb`/`java` 都不在 PATH（本机实测）。
2. **启动路径**：`GameplayBootstrap.LoadChart` 对 `StreamingAssets/Charts` 做 `File.Exists`，安卓上恒 false → 抛异常，
   而 `Start()` 无 try/catch — 场景**启动即死**（`Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs:1068-1070`、
   `:1111`、`:1116`）。
3. **编辑器初始化**：`FalsumChartEditor.Initialize` → `FalsumToolManager.Initialize` 对 APK 内路径
   `Directory.CreateDirectory`，同样无保护（`.../Falsum/Tools/FalsumToolManager.cs:23-24`，调用点
   `.../Falsum/Runtime/FalsumChartEditor.cs:286-287`）。

---

## 1. 平台与构建层

### 1.1 硬阻塞（不改代码/不换依赖就过不去）

| # | 结论 | 证据 |
|---|---|---|
| P1 | 本机无 Android 构建目标，当前**不能** Build Android | `Editor\Data\PlaybackEngines` 目录列举（只有 MetroSupport、windowsstandalonesupport） |
| P2 | 无 SDK/NDK/JDK/adb | 环境探测：`ANDROID_HOME`/`ANDROID_SDK_ROOT` 空、`%LOCALAPPDATA%\Android\Sdk` 不存在、`where adb`/`where java` 无结果 |
| P3 | 构建脚本只有 Windows 一条路 | `Assets/InFalsus/Editor/FalsumBuild.cs:41`（MenuItem "Build Windows Player"）、`:70-74`（强制 D3D11）、`:76-78`（SwitchActiveBuildTarget）、`:98`（StandaloneWindows64）、`:112-121`（打 zip）；全仓 `BuildTarget.Android` **0 处** |
| P4 | BASS 原生库是 Windows x64，安卓没有替代 | `Assets/Plugins/x86_64/{bass,bass_fx,bassmix}.dll` PE 头 `machine=0x8664`（本机实测）；`Assets/Plugins/ManagedBass/README.md:5-6` 明写 "Windows x64"；仓库无 `Assets/Plugins/Android`、Assets 下 **0 个 `.so`** |
| P5 | 三个 BASS `.dll` 的 `.meta` 只有 guid、无平台限定 → 会被原样打进任何平台包 | `Assets/Plugins/x86_64/bass.dll.meta:1-2`（`fileFormatVersion` + `guid`，无 `platformData`） |
| P6 | `ManagedBass*` 是纯托管包装但 P/Invoke 原生 `bass` → 安卓需 `libbass.so/libbass_fx.so/libbassmix.so` | `Assets/Plugins/ManagedBass/ManagedBass.dll` PE `machine=0x014C` + CLI 头（托管）；`README.md:6-7` |
| P7 | 文件对话框在非 Windows 退化为**全返回空串** → 打开/保存 `.falsum`、选音频/封面全部不可用 | `.../Falsum/Runtime/FalsumFileDialog.cs:126-158`（`#else` 分支 6 个入口全 `return string.Empty`） |
| P8 | ffmpeg 解析必抛 + 执行走 `Process.Start`（安卓无进程能力） | `.../Falsum/Runtime/FalsumMediaConverter.cs:301-341`（`ffmpeg.exe` 候选全 `File.Exists=false` → throw）、`:344-444`；`Assets/StreamingAssets/Falsum/Tools/ffmpeg.exe` 61.47 MB |
| P9 | 9 处直接用 `streamingAssetsPath` 拼盘内路径做 `File.Exists/ReadAllBytes` —— 安卓上必失效 | `.../Falsum/UI/FalsumSongFont.cs:152`、`.../Presentation/Backgrounds/FalsumTrackEndComposer.cs:387`、`.../Presentation/Backgrounds/FalsumBackgroundPresenter.cs:316`、`.../Falsum/Runtime/FalsumRuntimePaths.cs:122/152/163`、`GameplayBootstrap.cs:1111`、`.../Falsum/Runtime/FalsumChartEditor.cs:1242/2875` |
| P10 | 「程序目录」模型整体失效（`GetDirectoryName(Application.dataPath)` 在安卓是 APK 内/根） | `.../Falsum/Runtime/FalsumRuntimePaths.cs:40-42`（`MainProgramDirectory`）、`:66-73`（`BackgroundsDirectory` 非编辑器分支拼 exe 同级）、`:102-127`（`BackgroundDirectories` 候选表）；`.../Falsum/Runtime/FalsumBackupService.cs:26-30`（`autobackups/` 写在程序目录） |
| P11 | 备份目录不可写 → `EnsureRootDirectory` 抛；自动备份默认开启（180 s） | 同上 `:26-37`；`.../Falsum/Runtime/FalsumChartEditor.cs:76`、`:188-190` |
| P12 | `FalsumPlatform.OpenDirectory` 先 `Directory.CreateDirectory` 再开 shell | `.../Falsum/Runtime/FalsumPlatform.cs:16-29` |
| P13 | 构建产物仍是 PC 形态：把 `GameplayBackgrounds/UserTools` 拷到 exe 同级，再删 StreamingAssets 副本 | `Assets/InFalsus/Editor/FalsumBuild.cs:129-169` |
| P14 | IME 候选窗定位在非 Win 编译为 `return false`（**安全降级，不崩**） | `.../Falsum/Runtime/FalsumImePosition.cs:9`、`:176-183` |

### 1.2 配置项（改设置/补模块即可）

| # | 结论 | 证据 |
|---|---|---|
| C1 | 装 Unity Android Build Support（含 OpenJDK + SDK/NDK）即可解 P1/P2 | `6000.3.26f1` 的 `modules.json` 仍列 android 模块（未安装） |
| C2 | `scriptingBackend(Android)=1` = IL2CPP，**已就绪** | `ProjectSettings/ProjectSettings.asset:841-842` |
| C3 | `AndroidTargetArchitectures=2`、`AndroidMinSdkVersion=25`、`AndroidTargetSdkVersion=0`(自动) | `ProjectSettings/ProjectSettings.asset:274`、`:183-184` |
| C4 | `activeInputHandler=0`（旧 Input Manager）——**不要切新 Input System**，代码全依赖旧 API | `ProjectSettings/ProjectSettings.asset:935`；全仓 `Input.*` 调用 **144 处**（本机统计） |
| C5 | 质量档已按平台分离：Android 默认走 Mobile 档（index 0），PC 档显式排除 Android | `ProjectSettings/QualitySettings.asset:120`、`:115-117` |
| C6 | Mobile 档指向 `Mobile_RPAsset`（guid 5e6cbd92…） | `ProjectSettings/QualitySettings.asset:51` ↔ `Assets/Settings/Mobile_RPAsset.asset.meta:2` |
| C7 | 待改：模板包名 `com.UnityTechnologies.com.unity.template.urpblank`、无 keystore（仅 debug 签名） | `ProjectSettings/ProjectSettings.asset:172-173`、`:278-279`、`:291` |
| C8 | `EditorBuildSettings` 引用了**已不存在**的场景 `Assets/Scenes/SampleScene.unity` | `ProjectSettings/EditorBuildSettings.asset:11-13`（该目录不存在） |
| C9 | 安卓侧主动开了「画到刘海区」：`androidRenderOutsideSafeArea=1`，且全仓 `Screen.safeArea` **0 处** | `ProjectSettings/ProjectSettings.asset:74` |
| C10 | `defaultScreenOrientation=4`、`androidResizeableActivity=1`、`androidFullscreenMode=1` | `ProjectSettings/ProjectSettings.asset:11`、`:78`、`:83` |

### 1.3 静默降级（不崩，但画面/功能缺一块）

- **Mobile_Renderer 的 `m_RendererFeatures: []`** —— TrackEnd 菱形合成与 SkyJudgementLine 辉光两个 Feature
  只装在 `PC_Renderer`，安卓上**不执行**（`Assets/Settings/Mobile_Renderer.asset:29` vs `Assets/Settings/PC_Renderer.asset:27-30`）。
  画面代价：天空判定线辉光、轨道尽头菱形遮罩整体缺失。**但这恰好也是移动端最需要的降级**（见 §2.3）。
- 渲染模式：PC = Forward+（`:53` = 2）、Mobile = Forward（`Mobile_Renderer.asset:48` = 0），SSAO 只挂 PC（`:70`）。
- 场景里 **Light 对象数 = 0**（`GameplayRuntime.unity` 中 `class 108` 计数 0，93 个 MeshRenderer）——
  所有 cascade/2048 shadowmap 字段都是**死配置**，GPU 阴影预算实际为 0，不要把该项算进成本。

---

## 2. 性能：GPU

### 2.1 移动档实际渲染分辨率

`Mobile_RPAsset`：`m_RenderScale: 0.8`（`:29`）、`m_MSAA: 1`（`:28`）、无深度纹理（`:22`）、无 opaque 纹理（`:23`）。

→ 1080p 画面实际渲染 = **1536×864**。

### 2.2 ⚠ MSAA 被偏好默认值覆盖成 4x

`Mobile_RPAsset:28` 的 `m_MSAA: 1` **不是运行值**：
`FalsumChartEditor.cs:363` 调 `ApplyDisplayPreferencesAtStartup`（`:7955`）→
`.../Falsum/Runtime/FalsumDisplaySettingsController.cs:177`（`asset.msaaSampleCount = ...`）、
`:182`（`QualitySettings.antiAliasing`）、`:192`（`camera.allowMSAA`）；
而偏好默认 `AntiAliasingEnabled = true` + `Msaa4x`
（`.../Falsum/Runtime/FalsumPreferences.cs:80`、`:82`）。

→ 安卓实际是 **1536×864 × 4x MSAA + HDR RT**。这是**资产字段误导性最强的一处**，也是第一个该关的开关。

### 2.3 两条全屏 blur 链（PC 专属，恒定开销）

| 链 | RT 尺寸（1080p 宽 1920） | 每帧 pass | 写像素合计 | 取样量（写像素 × 5 tap） |
|---|---|---|---|---|
| SkyJudgementLine | 全屏 mask **1536×1536** + 2 张 1536×384（ARGB32） | 1 Draw + **7 Blit** | 1 695 744 px | ≈ **8.48 M** |
| TrackEnd 菱形 | mask **900×600**（`clamp(pxW/3840, 0.75, 1) × 1200×800`） | 1 Draw + **7 Blit** | 1 147 500 px | ≈ **5.74 M** |
| 合计 | — | 2 Draw + **14 Blit** | 2.84 M px | **14.2 M 取样/帧 ≈ 读 57 MB + 写 11 MB ≈ 68 MB/帧 ≈ 4.1 GB/s @60 fps** |

（逐 pass 的写像素：Sky 链 = 1536×384 ×2 + 768×192 ×3 + 384×96 ×2；
TrackEnd 链 = 900×600 ×1 + 450×300 ×4 + 225×150 ×2。两条链各自的金字塔都是「下采 2 级 → 上采 2 级」。）

证据：`.../Presentation/Notes/SkyJudgementLineEffect.cs:21`（`MinimumMaskScale = 0.75`）、`:646-655`（尺寸换算）、
`:664-678`（三张 RT）、`:19`（`BufferCount = 4`）、`:579`（轮转分配）；
`.../Presentation/Notes/SkyJudgementLineEffectFeature.cs:94-135`（7 次 `AddBlit`）；
`Assets/InFalsus/Gameplay/Resources/SkyArea/Shaders/SkyJudgementLineBlur.shader:26-53`（5 tap 权重）；
`.../Presentation/Backgrounds/FalsumTrackEndComposer.cs:253`（1200×scale）、`:701`（`CreateOutputQuad`）；
`.../Presentation/Backgrounds/FalsumTrackEndComposerFeature.cs:92-133`（7 次 `AddBlit`）。

**关键判断**：这段开销**与音符数量无关**，是每帧恒定项。而且与分辨率平方相关 ——
中端移动 GPU 在 4x MSAA 下跑 14 个全屏 5-tap pass，是**确定性爆预算**的组合。
好消息是：这两个 Feature 本来就没挂到 `Mobile_Renderer`，**保持不挂就是正确降级**；
要保留视觉效果，至少把 4 层金字塔降到 1 层（省 4 个 Blit）或再减半 mask RT。

> 注：两条 blur 链本身**没有 GLES 兼容问题**（`#pragma target 3.0` + `SAMPLE_TEXTURE2D_X_LOD`，
> `SkyJudgementLineBlur.shader:93`），纯带宽问题。真正的着色器风险在 §2.5。

### 2.4 透明 overdraw

62 个材质，其中 **51 个**设了自定义 RenderQueue，落在 **3000–3012 之间有 48 个**（3000×12、3001×9、3002×4、3003×4、3004×4、3008×3、3012×6 …）。
运行时另有三处固定分层：背景 **2900**（`.../Presentation/Backgrounds/FalsumBackgroundPresenter.cs:16`）、
TrackEnd 输出 **2950**（`.../Presentation/Backgrounds/FalsumTrackEndComposer.cs:35`）、
频谱 **3002**（`.../Falsum/Runtime/FalsumTrackSpectrumRenderer.cs:19`）。
B-8 的分层是**刻意设计**（`02-invariants.md` §B-8，不要改）；
但 48 个材质挤在 3000–3012 这 13 个槽里，意味着**同一深度存在大量同序透明层** ——
移动端需要一次实测 overdraw 统计（本次未做）。

### 2.5 着色器可移植性（真风险，未实测）

| 风险 | 写法 | 证据 |
|---|---|---|
| **高** | `SkyJudgementLineMask.shader` 用 `StructuredBuffer<float>` + `SV_VertexID` + `#pragma target 4.5` | `Assets/InFalsus/Gameplay/Resources/SkyArea/Shaders/SkyJudgementLineMask.shader:23`、`:29`、`:44`、`:47`（`vertexId / 3` 取 opacity） |
| **高** | `b3.shader`（SkyArea 主 shader）`#pragma target 4.5` + `uint` 位运算/整数除法 | `Assets/InFalsus/Recovered/b3.shader:56`、`:123-131`（`h = y ^ 0x41c64e6d; h >>= 5`） |
| 中 | 项目未要求 ES 3.1/3.2（`openGLRequireES31: 0`），而上面两条需要 ES 3.1+ 才有 SSBO/`gl_VertexID` | `ProjectSettings/ProjectSettings.asset:551-553` |
| 低 | 编辑器侧 legacy CG 写法（`fixed4`/`tex2D`/`CGPROGRAM`/`UnityCG.cginc`），GLES3 会被翻译，`fixed` 在 Mali 上实为 fp16 → 精度静默降级 | `.../Falsum/Resources/Falsum/Shaders/FalsumComboText.shader:26-29`、`FalsumComboBacking.shader:29`、`FalsumTrackSpectrum.shader:34` |
| 低 | `SkyAreaPrompt.shader` `#pragma target 4.0` + `fwidth`（ES3.0 可用） | `Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader:33` |
| — | `StenciledUnlitSprite` **名不符实**：全文无 `Stencil{}` 块 → 移动端 stencil 无风险 | `Assets/InFalsus/Shaders/Compat/StenciledUnlitSprite.shader:28-36` |
| — | 全量 grep：`GrabPass` / Geometry Shader / Compute / `tex2Dlod` / `D3DCOLOR` 命中 **0** | `Assets/InFalsus/**/*.shader`（29 个） |

`b3.shader` 被 `Recovered/Material/SkyArea Variant.mat` 引用（guid `044533ae…`），是**全屏天空区的主 shader** ——
一旦在 GLES3 上编译失败或降级，代价是整片天空区错色，不是局部特效。

### 2.6 纹理资产（移动端最容易被忽略的一项）

本机实测（用 System.Drawing 逐张读尺寸）：

| 指标 | 值 |
|---|---|
| PNG 数量 | **89** |
| 总像素 | **240.88 Mpix** |
| ASTC 4×4（1 B/px） | **≈ 230 MiB**（240.88） |
| ASTC 6×6（0.444 B/px） | **≈ 102 MiB** |
| 未压缩 RGBA32（4 B/px） | ≈ 919 MiB（必然 OOM） |

- **0 张有 Android platform override**（89 个 `.png.meta` 中 `platformSettings` 只有 `DefaultTexturePlatform`，
  少数多一个 `Standalone`）→ 出安卓包时不会自动转 ASTC/ETC2，必须补 per-platform override。
- `streamingMipmapsActive: 0`（两档都是）—— 全部 mip 常驻，没有按需加载（`ProjectSettings/QualitySettings.asset:40`、`:94`）。
- 3 张 4096 纹理（`FlickBurst.png` / `FlickBurstR.png` / `Texture-09-2.png`），其中前两张 **`enableMipMap:0`** —— 移动端典型陷阱。

---

## 3. 性能：CPU（每帧）

### 3.1 天空区 mesh：**每帧整链重建**

这是 CPU 侧最贵、也最容易被误判的一条。B-3「每条连续链一个 mesh」与 B-4「5 ms 采样」都是**刻意设计**
（`02-invariants.md` §B-3/§B-4），问题不在结构，在**每帧重写**：

`.../Presentation/Notes/NoteRenderer.cs:1949`（`UpdateSky`）→ `:1980-1986`（清 7 个 List）→
`:2026-2045`（逐段逐行 `AppendSkyAreaQuad`）→ `:2047-2054`
（`Mesh.Clear(false)` + `SetVertices` + **5 次 `SetUVs`** + `SetTriangles` + 重设 bounds）。

量的计算（`SkyAreaGeometry.cs:9` `MillisecondsPerSubdivision = 5.0`、`:12-23` `SubdivisionCount`）：

- 单段最长 skyarea = **21552 ms**（`coldsea.falsum` → `charts/fbd.chart.txt`，`skyarea(88793,…,21552,697)`）
  → `rows = 21552/5 + 1 = 4311` → **17 244 顶点 / 25 866 索引**，每帧写入。
- 我按 `[t−3300 ms, end]` 窗口对 74 个包 × 每难度谱面做并发扫描（脚本实测，非估算）：
  **最坏并发行数 4414**（`coldsea` fbd @88.8 s）→ **17 656 顶点/帧**；
  次高 `alterededge` ult 3539 行、`tactom` fbd 3446 行、`chronophobia` fbd 3124 行。

### 3.2 `ScaledTimeAt` 的线性扫描被乘进了行数

`.../Core/Chart/ChartModels.cs:479-518`：`ScaledTimeAt` 每次**从头遍历 `SpeedEvents`**（无二分、无缓存）。

每个「行带」（`AppendSkyAreaQuad`，`NoteRenderer.cs:2136-2235`）会调用 **3 次** `ScaledTimeAt`：
`EvaluateSkyAreaRow` 两次各含 1 次 `NoteZAt`（`:2153`、`:2163` → `:2262`），再加 `TrackTravelAt(segment.TimeMs)`（`:2186`）；
`NoteZAt` / `TrackTravelAt` 内部都转发到 `ScaledTimeAt`（`.../Core/Timing/TrackMotion.cs:29-33`、`:43-47`）。

谱面最坏情况实测：`chronophobia.falsum` fbd 有 **165 个 `track()` 事件**（`coldsea` fbd 是 0，它的 `ScaledTimeAt` 几乎免费）。

→ 估算：`3124 行带 × 3 = 9 372 次调用`，每次平均遍历约 82 个事件 → **≈ 0.77 M 事件迭代/帧**，
按 2 ns/迭代 ≈ **0.8–1.5 ms**（假设，未实测）。叠加每行带的 List 写入与 5 次 `SetUVs` 上传，
`NoteRenderer.Update` 整体落在 **1.5–2.5 ms** 量级。

这是**唯一一条能把 CPU 主线程打穿的路径**，而且是纯算法问题
（加二分 / 前缀和缓存即可，不动 A-16「`ScaledTimeAt` 是唯一换算入口」的语义）。

### 3.3 小节线：三条 mesh 每帧全量重建

`.../Presentation/Tracks/MeasureLineRenderer.cs:99`（`Update`）→ `:101-109`（3× 清 `Vertices/Colors/Triangles`）
→ 逐小节线重算 → 3× `ApplyMesh`（`Mesh.Clear` + `SetVertices` + `SetColors` + `SetTriangles` + `RecalculateBounds`），
见 `:298-312`。小节线总量与 `bpm`/`division` 成正比，长谱可达数百条（未精确实测）。

### 3.4 每帧时间轴窗口与创建预算（这块其实是健康的）

`NoteRenderer.cs:211`（`VisualWindowCeilingMs = 40000`）、`:216`（`VisualWindowTrailingMs = 1500`）、
`:221`（`VisualCreateBudgetPerFrame = 48`）。B-16 已经把「可见窗口必须覆盖整条轨道」处理过了，
移动端**不需要**再改这套窗口逻辑 —— 这是本项目在性能上少有的「已经做对」的部分。

### 3.5 托管分配不是主要风险

天空/小节线热路径用的都是 struct（`new Vector4 metadata`、`new Vector2` 写进复用 List），不产生 GC；
`new List`/`Dictionary`/`Sort` 闭包只发生在 `BuildSkyChains`（`NoteRenderer.cs:1241`、`:1250-1256`）重建时。
`MeasureLineRenderer` / `NoteRenderer` / `GameplayBootstrap` / `FalsumChartEditor` 的 LINQ 命中数为 0/0/0/10，
且编辑器那 10 处不在每帧路径。→ **不要把「GC 抖动」写进风险清单**，这里的瓶颈是顶点重写与事件重复遍历。

### 3.6 其它每帧项（可忽略量级）

- `SideLaneSurfaceScroller.cs:24-25`（复用 MPB）、`:56-80` —— 2 个 renderer 的 MPB 写，无分配。
- `LaneStateController` 每帧 6 面 × Get/SetPropertyBlock + 遍历 `LaneEvents`（最多 59 条）。
- `FalsumTrackEndComposer.SetPresentationTime`（`:174`）→ `_TimeOverride` 写入（`:192`），移动端该 Feature 不跑。

---

## 4. 性能预算表与「先爆点」

### 4.1 16.7 ms（60 fps）预算拆分

假设：Mobile 档 1536×864 + **4x MSAA**（按 §2.2 覆盖后的实际值）；两项 blur 链**不挂**（当前安卓现状）。

| 子系统 | 预算 ms | 依据 |
|---|---|---|
| GPU 主场景 opaque + transparent（20+ 透明层） | 4.0 | §2.4、93 个 MeshRenderer |
| GPU 两条 blur 链 | **0**（不挂） | `Mobile_Renderer.asset:29`；若挂上则 +5.0 |
| GPU MSAA 4x resolve + HDR RT | 1.5 | §2.2 |
| GPU Bloom/Tonemap（Bloom intensity = 0，实际可省） | 1.0 | `Assets/Settings/DefaultVolumeProfile.asset` |
| GPU 阴影 | **0** | 场景 Light = 0（§1.3） |
| CPU `NoteRenderer.Update`（天空 mesh 每帧重建） | **2.5** | §3.1 + §3.2 估算 1.5–2.5 ms |
| CPU `MeasureLineRenderer.Update` | 0.8 | §3.3 |
| CPU `engine.Update` + `hitAudio.Tick` | 1.0 | `GameplayBootstrap.cs:409-413` |
| CPU SideLane + LaneState + MPB | 0.5 | §3.6 |
| CPU draw submit / culling | 1.5 | 93 MR + 音符实例 |
| **余量** | **3.9** | 上列合计 12.8，合计 16.7 |

**90 fps（11.1 ms）**：GPU 侧需再砍约 2 ms → RenderScale 0.8 → 0.7，或 MSAA 4x → 2x（或关）。
CPU 侧 2.5 ms 的天空重建必须降到 <1.5 ms，否则主线程先于 GPU 撞线。

### 4.2 最可能先爆的 3 个点 + 对应降级开关

| # | 先爆点 | 证据 | 降级开关（按性价比排序） |
|---|---|---|---|
| 1 | **CPU：天空区每帧整链重建 × `ScaledTimeAt` 线性扫描** | `NoteRenderer.cs:1979-2054`、`:2163-2172`、`ChartModels.cs:501-514` | ① `ScaledTimeAt` 加二分或前缀和缓存（**纯性能，不动 A-16 唯一入口语义**，收益最大且零风险）；② 未变化的链按 `nowMs` 阈值跳过重建（需实测接缝）；③ 5 ms 细分放大 —— **改它必须同步 `02-invariants.md` §B-4 与自检**，不要顺手改 |
| 2 | **GPU：两条全屏 5-tap blur 链** | `SkyJudgementLineEffectFeature.cs:94-135`、`FalsumTrackEndComposerFeature.cs:92-133` | 保持 `Mobile_Renderer` 不挂（现状即最优）；若要效果：4 层金字塔 → 1 层、mask RT 减半 |
| 3 | **资产：240.88 Mpix 全量常驻、0 张 Android override、4096 无 mip** | §2.6 | ASTC 6×6（230→102 MiB）、开 `streamingMipmapsActive`、4096→2048、给 `FlickBurst*` 开 mipmap |

**清单一（第一周就该做）**：关 MSAA（`FalsumDisplaySettingsController.cs:177` 在移动端跳过）、
`ScaledTimeAt` 加二分、`targetFrameRate` 设 60（当前全仓 **0 处**）。

### 4.2a 已落地的第一轮优化（2026-11-06，本机实测）

上面的「先爆点」第 1 项（CPU 时间轴开销）已经改完并通过三道门禁：

| 改动 | 位置 | 实测收益（离线基准，非真机） | 语义等价验证 |
|---|---|---|---|
| `ScaledTimeAt` O(n) → 前缀表 + 二分 | `ChartModels.cs`（`RebuildSpeedIndex` / `_speedIndexBases`） | 最重谱面 **1.02 → 0.05 ms/帧（约 20×）**；`tactom` 0.14 → 0.05 | 90 000+ 组随机 + 边界对照**逐位一致** |
| 无 `track()` 谱面加空表短路（防回归） | `ScaledTimeAt` 开头 `SpeedEvents.Count == 0` | `coldsea`/`alterededge`/`rainshower` **0.000 变化**（噪声内） | 同上 |
| `BpmAt` / `DivisionAt` 同款前缀表 | `ChartModels.cs`（`RebuildBpmIndex`） | 消除审计实测的最大浪费：`shouldhavebeen.fbd` 曾达 **84 178 条 bpm 迭代/帧** | 326 个 bpm 事件 × 边界 × 抑制往复，**逐位一致** |
| `EndTimeMs` 缓存（按 `TimelineVersion` 失效） | `ChartModels.cs` | 省 2×2236 次比较/帧（2071 音符谱） | 与线性扫描逐位一致，编辑后正确跟随 |
| `TrackMotion` 播放头 memo | `TrackMotion.cs`（`ScaledNowAt`） | 同帧同一 `nowMs` 的重复求值 → 1 次 | 缓存的表达式就是原调用，**必然同值** |
| 天空区行带共享接缝行（每行只采样 1 次） | `NoteRenderer.cs`（`SkyRowSample` / `SampleSkyAreaRow`） | 采样数 **2N → N+1**（`coldsea` 4414 行：8828 → 4415） | UV/顶点写入顺序不变，几何逐行等价 |

> 所有改动都保留了原实现作为**线性兜底**：索引未构建（例如测试里直接 `new Chart` 不调 `RebuildTimeline`）
> 或列表被事务临时清空（`FalsumChartSession.DetachCurrent`）时，行为与改前完全相同。
> A-16（`ScaledTimeAt` 是唯一换算入口）与 B-3/B-4（每链一 mesh、5 ms 细分）均未被破坏。

### 4.2b 第二轮：扫描游标（2026-11-06，含两处**纠正**）

| 改动 | 位置 | 实测收益 | 等价验证 |
|---|---|---|---|
| `JudgementEngine.Update` 判定前缀游标 | `JudgementEngine.cs`（`_scanStart`） | 2071 音符 300 帧：0.888 → 0.632 ms，**1.4×** | 150 组 × 500 步差分（含**有序与无序**谱面），判定/连击/分数**全部一致** |
| `AutoPlayStep` 判定前缀游标 | `GameplayBootstrap.cs`（`_autoPlayScanStart`） | autoplay+Update 实测 13.8 → 13.1 us/帧，**仅 1.1×** | 同上（引擎态一致） |

**两处必须记录的纠正**：

1. **审计高估了「双游标」的收益**。实测 autoplay 只值 **0.7 us/帧**：因为 `PressLane`/`PressFlick`/`PressSky`
   内部各自还要全扫一遍 `_notes`，游标跳过的那段又被按键路径重新扫了回来。
   真正的开销在**按键路径的全扫**（2071 音符：5.5 ms/整曲 ≈ 2.7 us/次按键），
   但按键是稀疏事件、摊到每帧约 0.004 ms —— **不值得为它引入二分索引**（会改变平局时的候选选择顺序，语义有风险）。
   结论：`Update` 游标保留（零风险、真实收益），按键路径**明确不做**。

2. **审计称 `FalsumAutoCombo` 每帧 84 178 次 bpm 迭代 —— 这条前提不成立**。
   游玩每帧路径（`GameplayBootstrap.Update`：`AutoPlayStep` → `engine.Update` → `hitAudio.Tick` →
   `noteRenderer` → `sideLane` → `laneState` → `measureLines`）**没有 combo 调用**；
   `RefreshPresentationAt`（其中才调 `_comboDisplay.Update` → `FalsumAutoCombo.CountAt`）
   只被编辑器的 seek/刷新路径调用。`BpmAt` 前缀表仍然做了（编辑器侧确实受益，且零风险），
   但「每帧 84 178 次」这个数字要降级为「编辑时刷新一次的量」。

**门禁结果**：编译 0 error；`FalsumSelfCheck` **`passed:true`，31/31 项**；
`GameplaySelfCheck` 通过（含 `Judgement engine lifecycle checks passed`、`Judgement boundary checks passed`）；
`python Tools/falsum_localization_audit.py` 通过。

### 4.2c 第三轮：选中长 skyarea 的每帧重建（2026-11-06）

审计点名的「每帧重建 8622 次采样 + 约 200 KB 分配」已修。**关键洞察**：`TryBuildSkyAreaScreenPolygon`
里只有视图 Z 依赖播放头 —— 左右 X 边缘来自音符自身几何（`Ease` 是纯函数），
每个采样点的 `ScaledTimeAt(sampleTime)` 也不随 `nowMs` 变。于是把这两样缓存起来。

| 改动 | 位置 | 实测收益 |
|---|---|---|
| 采样几何缓存（X 边缘 + 每点 scaled time） | `FalsumEditorWorld.cs`（`_skyPolyCache*` / `RefreshSkyAreaPolygonCache`） | 21552 ms 长 skyarea（4311 步）：**0.372 → 0.242 ms/帧（1.5×）** |
| 四个 scratch List 复用 | 同上（`_skyPolyLeftScratch` 等） | 托管分配 **345 KB → 69 KB / 帧（5.0×，省 270 KB）** |
| Z 运算顺序对齐 `NoteZAt` | 同上 | 22 000 样本 **逐位一致** |

**这项的真实收益是 GC 压力，不是 CPU**：因为 `ScaledTimeAt` 已被第一轮优化成二分，
8622 次调用本身不再昂贵（0.37 ms 里 CPU 只占小头）。每帧省 270 KB 才是重点。

**缓存键必须包含音符自己的时间跨度** —— 这是个差点漏掉的坑：
`FalsumChartSession.CommitChange` 只调 `FalsumSkyAreaGrouping.Normalize`、**不调 `RebuildTimeline`**，
所以 `TimelineVersion` 在拖拽时**不变**。缓存键因此包含
`TimeMs` / `DurationMs` / `StartPos` / `EndPos` / `StartWidth` / `EndWidth` / 两个 easing，
而不只是 version。漏掉 `TimeMs`/`DurationMs` 会让拖动后的选中框停在旧位置。

**门禁结果**：编译 0 error；`FalsumSelfCheck` **`passed:true`，31/31 项**（含 `selection_anchors`）；
`python Tools/falsum_localization_audit.py` 通过。

> 注意：这些是**桌面 CPU 上的离线基准**，不等于真机帧时间。它证明的是「这条路径的算法复杂度已不再是瓶颈」，
> 不是「安卓已经 60 fps」。

### 4.3 不存在「像素级不可能」的结论

没有任何一项证明「中端安卓一定跑不动」。上面全是**工程可解的降级项**。
但有一个硬前提：**必须上真机**。仓库现有的性能插桩
（`.../Falsum/UI/FalsumPerformanceWindow.cs`、`FalsumPerformanceMonitor.cs`、`Assets/InFalsus/Editor/FalsumHeavyChartPerformanceCapture.cs`）
只测**帧内 CPU/GPU 分段**，没有真机数据、没有 present 时间戳、没有输入时间戳 ——
本机也没有任何一次 Heavy Chart 性能采集产物（`Diagnostics/` 下无匹配日志）。

---

## 5. 包体与资源

| 项 | 值 | 说明 |
|---|---|---|
| `Assets/StreamingAssets` 合计 | **120.6 MB** | 剔除 `.meta` 后本机实测 |
| └ `Falsum/Tools/ffmpeg.exe` | **61.47 MB** | PC 专用，安卓上完全无用 —— 出包应剔除（`FalsumMediaConverter.cs:303-307` 解析它） |
| └ `Falsum/GameplayBackgrounds` | 26.8 MB（41 个 JPEG） | 逻辑名仍是 `.png`（`02-invariants.md` §D-3b） |
| └ `Falsum/Fonts/SongInfoCard.fpk` | 8.3 MB | **D-1：缺失则构建直接抛异常**，不能省 |
| └ `Charts/` | 4.7 MB | 场景默认谱面 `cryogenic_4.chart.txt`（`GameplayRuntime.unity:16211`） |
| `../export/falsum` 74 个包 | **719.2 MB** | 仓库外、构建不拷贝；安卓若要内置需自己加（平均 9.7 MB，最大 14.55 MB） |

**android 上 StreamingAssets 在 APK 内**：`File.Exists`/`Directory.GetFiles`/`File.ReadAllBytes` 全部不通，
必须走 `UnityWebRequest` 或首次启动拷到 `persistentDataPath`。全仓 `streamingAssetsPath` 引用 **13 处**
（4 处是 Editor 自检，9 处是运行时代码）；`UnityWebRequest` **只有 2 处**
（`FalsumBackgroundPresenter.cs:316`、`FalsumChartEditor.cs:5453`），
而且 `FalsumBackgroundPresenter` 在发起请求**之前**先做 `File.Exists(path)`（`:310`）——
安卓上这一步就返回 false，所以这两处**也不能算正确的安卓实现**。

**已有 5 处写 `persistentDataPath` 是正确的**：`.../Falsum/Runtime/FalsumCacheStore.cs:19`、
`.../Falsum/Runtime/FalsumPreferencesStore.cs:17`、`.../Falsum/Runtime/FalsumMediaConverter.cs:59`、
`.../Falsum/Runtime/FalsumDisclaimerConsent.cs:38`、`.../Falsum/Runtime/FalsumRuntimePaths.cs:174`。

---

## 6. 输入与触控（制谱器视角）

### 6.1 现状：25 个输入动作，双指手势只覆盖 3 个

`.../Falsum/Runtime/FalsumPreferences.cs:26-53` 定义了 **25 个** `FalsumInputAction`（0..24）。
`.../Falsum/Runtime/FalsumChartEditor.TouchGestures.cs` 只有 4 个手势
（双指左滑 Undo / 右滑 Redo / 上滑时间轴后 / 下滑时间轴前，`:11-14`、`:165-182`、`:199-226`），
判据常量 `TwoFingerSwipeFraction = 0.08`（`:44`）、`TwoFingerAxisDominance = 1.6`（`:51`）、
`TwoFingerScrollIntervalSeconds = 0.06`（`:80`）、必须正好两指（`:89`）。
→ 覆盖 `Undo` / `Redo` / `时间轴` 三个语义，占 25 个动作的 **约 12%**。
（`时间轴步进` 在枚举里没有对应项，它是鼠标滚轮路径，由手势复刻。）

由于 Unity 旧 Input 会把单指合成为鼠标事件，**左键路径全部可达**（放置/选择/拖拽/框选）；
但下列 10 类动作**没有任何等价入口**：

| 动作 | 现触发 | 证据 |
|---|---|---|
| 径向菜单（复制/剪切/镜像/粘贴/网格/段选） | 右键 | `FalsumChartEditor.cs:8479`（`GetMouseButtonDown(1)` → `ShowRadial`） |
| 中键三模式精修（Boundary/StartEnd/Position） | 中键 + F1/F2/F3 | `FalsumChartEditor.cs` 中键分支；`FalsumPreferences.cs` 键位默认 |
| 加选/减选/候选轮换 | Ctrl | `FalsumChartEditor.cs`（Ctrl 分支） |
| 2D 视图缩放 | Ctrl + 滚轮 | `FalsumChartEditor.cs`（`mouseScrollDelta`） |
| 删除选中 | Delete | `FalsumChartEditor.cs`（`DeleteSelection` 无 UI 入口） |
| 悬停预亮 / tooltip | hover | `.../FalsumChartEditor.AnchorEdit.cs:830`（`UpdateAnchorHover`） |
| 双击切缓动 | 双击 | `.../FalsumChartEditor.MiddleEdit.cs:84`（`SkyMiddleDoubleClickSeconds = 0.32`） |
| Ctrl+逗号开偏好 | 键盘 | `FalsumChartEditor.cs`（`HandleKeyboard`） |
| 时间轴步进 | 滚轮（有双指替代） | `FalsumChartEditor.cs` |
| 光标精确拾取 | mousePosition 共 29 处 | 全仓统计 |

**关键点**：这些**能力全部已经实现**，缺的只是**触控触发点** —— 改造成本远低于「重写交互」。

### 6.2 UI Toolkit 触控可用性：断点用错了维度

`Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumPanelSettings.asset:21-29`：
`ScaleWithScreenSize` / 参考分辨率 **1730×1080** / `MatchWidthOrHeight` / `match = 0.5`。

换算到物理尺寸（自算）：

| 控件 | USS 尺寸 | 6.5" 手机(dp) | 11" 平板(dp) | 证据 |
|---|---|---|---|---|
| `.bar-button` | **34 px 高 / 64 px 宽** | ≈ **15.8 dp** | ≈ 29.4 dp | `FalsumEditor.uss:523-534` |
| `.menu-item-button` | **min-height 24 px** | ≈ **11 dp** | ≈ 21 dp | `FalsumEditor.uss:1964-1965` |
| `.property-field` | **26 px 高** | ≈ 12 dp | ≈ 22 dp | `FalsumEditor.uss:843` |
| 正文 | 13 px | ≈ 6 dp | ≈ 11 dp | USS 共 171 条 `font-size`，115 条 ≤14 px |

**最关键的适配缺陷不是「px 太小」，而是唯一的放大档用错了判据**：
`.is-low-scale` 会把字号抬到 18 px、按钮抬到 40 px（`FalsumEditor.uss` 的 low-scale 段），
但触发条件是 `physicalScale < 0.82`（`.../UI/FalsumEditorView.cs:94`、`:5536`），
而手机实际 `physicalScale ≈ 1.178` —— **永远不命中**。四档响应式断点
（`CompactScreenWidth=1500`、`CompactScreenHeight=840`、`NarrowScreenWidth=900`、`ShortScreenHeight=700`，`:90-93`）
全是「小窗口桌面」语义，1080p 手机横屏（2400×1080）三档全不命中。

**改动量估算**：USS 4 个文件 **7564 行** / UXML 13 个文件 **515 行**（本机实测）。按 ≥48 dp 重标约 **40 组规则 ≈ 150–250 行 USS**，
外加 `FalsumEditorView.cs` 新增一段**按 dpi 而非窗口宽度**的断点判定（约 20 行）。
`Screen.safeArea` 需新增（当前 0 处），且要先关掉 `androidRenderOutsideSafeArea=1`（`ProjectSettings/ProjectSettings.asset:74`）。

### 6.3 锚点精修：物理上不可用（不是性能问题）

`.../Runtime/FalsumEditorWorld.cs:59`（`AnchorMinHitRadius = 20` 画布单位）、
`.../Runtime/FalsumChartEditor.AnchorEdit.cs:37`（`AnchorPickRadiusPixels = 14`）、`:43`（`AnchorHoverRadiusScale = 1.45`）、
`:590`（`AnchorDirectionDeadZonePixels = 4`）。

按 1.178 画布缩放换算：抓取直径 ≈ **1.5 mm**（手机），方向死区 ≈ **0.25 mm** ——
而缓动三档间距本身只有 8–15 画布像素（`02-invariants.md` §A-19 附近）。

→ **A-18/A-19/A-20 那一整套锚点精修在手机上必然不可用**，与性能无关，是分辨率×手指尺寸的物理问题。

---

## 7. 延迟（游玩层视角）

### 7.1 前提：现在没有「玩家输入」

`GameplayRuntime.unity:16239` 是 `autoPlayForDiagnostics: 1`，`AutoPlayStep` 直接拿
`note.TimeMs` 当输入时间（`GameplayBootstrap.cs:881-917`）；真实输入路径 `ReadInput`
（`:919-944`，键位 LeftShift/A/S/D/F/Space + 方向键）只在 autoplay 关闭时走。

→ **本仓现状下延迟无法测量**，因为根本没有玩家输入的时序数据。

### 7.2 判定窗口（真值，不要改）

`.../Core/Judgement/JudgementTypes.cs:26-30`：Shiny **±25** / Perfect **±50** / Near **±100** /
Bad(=early Break 上界) **150** / Sky Near **±200**，边界归内层（`:80-92`）；
与 `02-invariants.md` §B-14 一致；全量真值表 `Diagnostics/JudgementMatrix.csv`。

### 7.3 端到端预算（估算，设备相关项标「未实测」）

| 环节 | 60 Hz | 90 Hz | 120 Hz | 来源 |
|---|---|---|---|---|
| 触摸数字化采样（均值/最差） | 8.3 / 16.7 | 5.5 / 11.1 | 4.2 / 8.3 | 未实测 |
| Android 框架触摸预测/合并 | 0～1 帧 | 0～1 帧 | 0～1 帧 | 未实测 |
| Unity 输入轮询到 `Update` | 0～16.7 | 0～11.1 | 0～8.3 | `FalsumChartEditor.cs:4141`、`GameplayBootstrap.cs:367` |
| 判定时钟误差 | ±0～2 | ±0～2 | ±0～2 | `.../Core/Timing/GameplayClock.cs:70-99` |
| **音频输出 DSP 缓冲** | **21.3** | 21.3 | 21.3 | `ProjectSettings/AudioManager.asset:12`（`m_DSPBufferSize: 1024` @48 kHz） |
| BASS → 设备硬件延迟 | 10～25 | 10～25 | 10～25 | 未实测 |
| GPU 队列 | 8～16 | 5～11 | 4～8 | `FalsumPerformanceMonitor.cs` 可取 `gpuFrameTime` |
| 显示扫描（均值/最差） | 8.3 / 16.7 | 5.5 / 11.1 | 4.2 / 8.3 | 未实测 |
| **中位合计** | **≈55～75** | ≈48～65 | ≈44～60 | — |
| **抖动（不可标定部分）** | **≈±25～35** | ≈±18～25 | ≈±13～18 | — |

### 7.4 结论（分场景）

- **游玩层当音游**：绝对偏置（≈50–75 ms）可以用 `AudioOffsetMs` 整体标定掉
  （`GameplayClock.cs:48`，编辑器写 `-delay`）。**只有抖动吃窗口**。60 Hz 下抖动中位 ±25–35 ms
  已经**超过 Shiny ±25 的整个半径** → 60 Hz 手机上「Shiny 事实上不可获得，Perfect ±50 才是上限」；
  120 Hz + 稳定调度才有机会保住 Shiny 的一部分。**这是推算，不是实测结论。**
- **制谱器当工具**：±25 ms 窗口**不适用**，需求降为「点按到高亮 < 1 帧」，现有面板在 60 Hz 已够；
  真风险是 34 px 控件 + 无 hover 的手感，不是时间精度。

### 7.5 必须实测的唯一决定性指标

**触屏 `Down` 事件 → `PressLane(nowMs)` 被调用那一刻的 P50/P95/P99 抖动分布**（不是平均值）。
当前 `Input.GetKeyDown` 不携带事件时间戳（全仓 `evt.timestamp` 0 处），
所以这个量**现在测不出来**，必须先加时间戳埋点。其余两项（音频偏置、present 延迟）可标定，不决定成败。

---

## 8. 分阶段路线图

### 阶段 0：工具链与「能不能起来」（1–2 天，纯环境）

1. 通过 Unity Hub 安装 Android Build Support（含 OpenJDK、SDK、NDK）。
2. `FalsumBuild` 加一条 `BuildAndroid`：`BuildTarget.Android`、ARM64、APK、输出 `Builds/FalsumAndroid-<Version>/`。
3. **只跑一个空场景 + 一个空谱面**，验证「包能装、能启动」。

> 退出判据：设备上出现一个能启动的 APK。此时所有功能都不通，这是正常的。

### 阶段 1：平台抽象层（3–5 天，最大的一块）

这是把 PC 假设一次性收口的地方，**不要零散地改调用点**：

| 抽象 | 现在 | 应该 |
|---|---|---|
| 数据根 | `GetDirectoryName(Application.dataPath)`（`FalsumRuntimePaths.cs:40-42`） | 一个 `IFalsumStorage`：编辑器/PC = 程序目录，安卓 = `persistentDataPath` |
| 内置资源读 | 9 处 `File.*` on `streamingAssetsPath` | 统一 `ReadBundledAsync(path)`（安卓走 `UnityWebRequest`，首启拷贝到 `persistentDataPath`） |
| 文件选择 | `FalsumFileDialog.cs:126-158` 返回空串 | 安卓走 SAF（`ACTION_OPEN_DOCUMENT`）或内置「最近谱面」列表 |
| 音频后端 | BASS x86_64（`Assets/Plugins/x86_64/*.dll`） | 安卓放 `libbass*.so`（或首次就跑 managed fallback 并把 `FalsumBassAudioEngine` 明确禁用） |
| 媒体转换 | ffmpeg.exe + `Process.Start`（`FalsumMediaConverter.cs:301-444`） | 安卓**砍掉**（或接 Android MediaCodec），并在 UI 上明示不可用 |

> 退出判据：安卓上能打开一个内置 `.falsum` 包、能播放、能保存一次。

### 阶段 2：性能降级档（3–4 天）

1. **关 MSAA**：`FalsumDisplaySettingsController.cs:177` 在 `Application.isMobilePlatform` 时跳过覆盖。
2. **`ScaledTimeAt` 加二分/前缀和**（`ChartModels.cs:479-518`）—— 零风险，收益最大。
3. `targetFrameRate = 60`（当前 0 处）+ 关 `enableFrameTimingStats`（`ProjectSettings/ProjectSettings.asset:160`）。
4. 纹理：补 Android platform override 至 ASTC、开 `streamingMipmapsActive`、4096→2048、`FlickBurst*` 开 mipmap。
5. 出包剔除 `StreamingAssets/Falsum/Tools/ffmpeg.exe`（61.47 MB）。

> 退出判据：真机在**最重谱面**（`coldsea` fbd / `chronophobia` fbd）上稳定 60 fps 的 P95 ≤ 16.7 ms。

### 阶段 3：触控（仅制谱器，5–7 天）

1. **长按 400 ms = 右键径向菜单**（`ShowRadial` 能力已在）。
2. **中键三模式改成三个可点按钮**（`middle-edit-status` 现在是只读 Label）。
3. **删除进径向菜单**（`DeleteSelection` 能力已在）。
4. 长按加选替代 Ctrl。
5. USS 按 ≥48 dp 重标 + 新增 dpi 断点（约 150–250 行）+ safe-area。

> 退出判据：`Docs` 级操作（放置/选择/拖拽/删除/撤销/时间轴/属性编辑）全部可触控完成。

### 阶段 4：明确不做（负向清单）

| 不做 | 理由 |
|---|---|
| 锚点精修（A-18/A-19/A-20 整套） | 抓取直径 ≈1.5 mm、方向死区 ≈0.25 mm，物理不可用（§6.3） |
| 2D 视图编辑 | 2D 下锚点拾取返回 false、双指指针放弃，且无玩家侧入口 |
| ffmpeg 音频转码 / 波形切片 | 依赖外部进程（`02-invariants.md` §A-12、§A-15 的假设在安卓不成立） |
| 用户工具（Jint） | IL2CPP 下反射/泛型剥离风险未证实；沙箱语义（5 s/1e6 语句）在移动端体验也差 |
| 内置 74 个 `.falsum` 包（719 MB） | 包体会到 800 MB+；应让用户导入 |

---

## 9. 需要实测才能定的清单（不要当结论引用）

1. `b3.shader` / `SkyJudgementLineMask.shader` 在真机 GLES3 上是否**真的**编译失败或降级（目前只是「高风险」）。
2. 天空区每帧重建的实际毫秒数（本文 1.5–2.5 ms 是公式推算）。
3. 最坏并发 skyarea 行数 4414 是否会随 `track()` 变速窗口进一步变大（我按固定 3300 ms 窗口算的）。
4. `Process.Start` 在 Android 抛什么异常（决定 ffmpeg 路径是「报错」还是「崩溃」）。
5. `ManagedBass` 在缺 `.so` 时的降级是否干净（`FalsumBassAudioEngine.cs:134-191` 在 try 内，但需真机确认）。
6. `Jint/Acornima` 在 IL2CPP 剥离后是否可用（无 `link.xml`）。
7. 触控抖动分布（§7.5，决定游玩层能不能作为音游）。
8. Mobile 质量档是否真的在安卓生效（`ProjectSettings/QualitySettings.asset` 的 `m_CurrentQuality: 1` 是编辑器当前档，运行期由平台默认档决定）。
9. `AndroidTargetArchitectures = 2` 具体映射哪个 ABI（Unity 枚举未实测）。
10. 透明层 overdraw 实测（§2.4）。

---

## 10. 给决策者的三句话

1. **先做「游玩层能不能跑」比「制谱器能不能用」便宜得多**：游玩层只卡在文件系统 + 音频后端；
   制谱器还卡在文件对话框、ffmpeg、输入层、UI 缩放四处，工作量差 3 倍以上。
2. **性能不是主要障碍，正确性才是**：性能全是可解的降级开关（§4.2），
   而 P7/P8/P9 这类「安卓上直接返回空/抛异常」的硬阻断必须逐个改。
3. **本仓库有一条现成的好消息**：`Mobile_Renderer` 本来就没挂那两个全屏 blur Feature，
   这是移动端最需要的降级，**不要为了「还原度一致」把它挂上去**。

---

## 11. 修订记录

- 2026-11-06：首版。基于静态源码/资产阅读 + 74 个谱面包的语料统计。
  本机无 Android 工具链，**无任何真机数据**。
