# 01 · 架构地图

三层，按依赖方向排：**A 数据与编辑器 → B 渲染与表现 → C UI 与本地化**。
全部 `path:line` 相对仓库根。缩写：
`G/` = `Assets/InFalsus/Gameplay/`，`F/` = `Assets/InFalsus/Gameplay/Falsum/`。

---

## A. 数据与编辑器（谱面真值在这里）

2026-11-16：Android 文件通道已加入 `FalsumFileDialog.Async.cs`、
`FalsumAndroidFileBridge.cs` 与 `FalsumDocumentStore.cs`。系统 URI 经流复制为本地工作文件，
包对象仍持有真实磁盘 `SourcePath`；URI/文档名/写权限单独绑定。
`FalsumChartEditor.Files.cs` 用保存快照等待外部写入成功，再提交清单、路径与脏标记；
保存后打开/退出同样等待回调。原生桥接在 `Assets/Plugins/Android/FalsumDocuments.androidlib/`。
详见 `docs/superpowers/specs/2026-11-16-falsum-android-file-access-design.md`。

### A1 分层

| 层 | 文件 | 职责 |
|---|---|---|
| 包容器 | `F/Core/FalsumPackage.cs` | `.falsum` ZIP 读写、路径白名单、未知条目原样保留、临时文件原子替换 |
| 清单 | `F/Core/FalsumManifest.cs` | `song.json` 模型与校验 |
| JSON | `F/Core/FalsumJson.cs` | 无依赖 JSON 树 |
| 谱面读 | `G/Core/Chart/ChartParser.cs` | 8 条 DSL 指令 + 诊断 |
| 谱面写 | `F/Core/ChartWriter.cs` | 反向写谱，时间轴换算/量化全在这里 |
| 谱面模型 | `G/Core/Chart/ChartModels.cs` | `Chart` / `ChartNote` / `ChartEvent` / `ChartTimeAxis`（`:568`） |
| 编辑事务 | `F/Core/FalsumChartSession.cs` | 快照式 undo/redo（上限 100）、批次事务 |
| 天空分组 | `F/Core/FalsumSkyAreaGrouping.cs` | `groupId` 归一化、片段连接判定、输出排序 |
| 辅助 | `FalsumAutoCombo` `FalsumBeatGrid` `FalsumFraction` `FalsumSkyBounds` | 连击计数 / 网格吸附 / 分母量化 / 0..1 边界 |
| 放置规则 | `F/Core/FalsumTapPlacementRule.cs` | TAP 叠键检查（±9 ms + 轨道区间相交），只被 `PlaceTap` 调用 |
| 主编辑器 | `F/Runtime/FalsumChartEditor.cs`（12565） | 模式机、输入、放置、拖拽、保存、备份、工具调度 |
| 中键精修 | `FalsumChartEditor.MiddleEdit.cs`（3185） | Boundary / StartEnd / Position |
| 天空吸附 | `FalsumChartEditor.SkyAreaSnap.cs` | 屏内 12px 的组首尾吸附 |
| 3D 视图 | `F/Runtime/FalsumEditorWorld.cs`（3020） | 网格、拾取、反投影、幽灵预览 |
| 音频 | `FalsumPitchPreservingPlayback.cs` `FalsumMediaConverter.cs` `FalsumBassMediaImporter.cs` `FalsumBassEncoder.cs` | 颗粒合成保音高；音频转码全走进程内 BASS（解码→mixer 重采样/降混→bassenc_ogg）；封面用 Unity 图像转换 |
| 音频预处理 | `FalsumAudioPreprocessMath.cs`(Core) `FalsumAudioPreprocessEncoder.cs` `FalsumAudioPreprocessEnvelope.cs` `FalsumBassSession.cs` `FalsumChartEditor.AudioPreprocess.cs` `UI/FalsumAudioPreprocess*.cs` | 把延迟烘焙进音频（BASS 重写 + 谱面 delay 清零），替代顶栏延迟输入 |
| 偏好 | `FalsumPreferences.cs` `FalsumPreferencesStore.cs` `FalsumInputRouter.cs` `FalsumDisplaySettingsController.cs` | 键位表、持久化、动作→按键 |
| 备份/缓存 | `FalsumBackupService.cs` `FalsumCacheStore.cs` `FalsumTrackSpectrum*.cs` `FalsumTrackWaveformCache.cs` | `autobackups/` 谱面快照、`FalsumCache/` 音频 ogg；波形只在内存 |
| 元数据 | `FalsumMetadataApplyService.cs` `FalsumChartMetadataDraft.cs` | 偏好页草稿 → 校验 → 应用/回滚 |
| 用户工具 | `F/Tools/*` | Jint 沙箱、清单/权限、表单/报告、编辑批次 |

### A2 `.falsum` 包

ZIP，后缀固定 `.falsum`。条目：`song.json`（必）、`audio.ogg`（必，≤128 MB）、
`cover.png`（可选，≤32 MB）、`charts/*.chart.txt`（每难度一份，≤16 MB）、其余条目原样保留。
写包时 entry 时间戳固定 `1980-01-01` 以保证可复现（`FalsumPackage.cs:823`）。
音频/封面用 `NoCompression`，其余用 Deflate/最优化。
完整规范：`../docs/superpowers/specs/2026-10-18-falsum-package-format-design.md`（该文档在父目录）。

`song.json` 关键字段：`format="falsum"`、`version=1`（强校验）、
`songId`（**读侧** `^[a-z0-9][a-z0-9_-]{0,63}$`；**新建侧**另有
`^[a-z][a-z0-9]{0,63}$`，见 A-24）、`title`/`artist`/`jacketDesigner`、
`background`（默认 `31_Space-Positive.png`）、`audio{file,codec,sampleRate,channels,durationMs}`、
`cover{file,width,height}`、`preview`、`difficulties[]{index 0..3,name=MIN/EVO/ULT/FBD,available,chart,rating,chartDesigner,jacketDesigner}`、`source`（溯源，自制包可省）。

### A3 谱面 DSL

行格式 `directive(a,b,...)`，`#` 开头为注释（`# song:` `# difficulty:` `# delay:`）。

| 指令 | 参数 | 要点 |
|---|---|---|
| `chart` | `(bpm,division)` | 必须存在且为正；缺失直接报错（`ChartParser.cs:114`） |
| `track` | `(t,multiplier)` | 只改行进速度，**不产生小节线** |
| `bpm` | `(t,bpm,division[,1])` | 第 4 参是遗留标志，非 1 报错（`:211`） |
| `lane` | `(t, lane∈{0,1,4,5}, enabled∈{0,1})` | 仅 MIN(0)/EVO(1) 允许（`:611`） |
| `tap` | `(t, width, lane)` | `lane+width<=6` |
| `hold` | `(t, lane, width, durationMs)` | **参数顺序与 tap 相反** |
| `flick` | `(t, center, grid, widthCells, dir)` | `StartPos=center/grid`、`StartWidth=width/grid`；`dir ∈ {4,16}` |
| `skyarea` | 11 参：`(t,sCtr,sCells,sW,eCtr,eCells,eW,leftEase,rightEase,durMs,groupId)` | ease∈0..2；`groupId>=0` |

### A4 两条时间轴

```
musicTime = chartTime + delayMs      // ChartTimeAxis (ChartModels.cs:568)
```

- 编辑器和网格线用**谱面时间**；文件里与音频里用**音乐时间**。
- 有 `# delay:` 头时按音乐时间反算；否则走遗留探测：紧跟 `chart(...)` 之后的
  `track(x,1.00)` 被当作 delay 并跳过（`ChartParser.cs:529`）。
- 负音乐时间禁止：写谱抛异常（`ChartWriter.cs:33`），编辑侧 `RejectNegativeMusicTime`。
- **这套机制正在被「音频预处理」取代**：窗口把延迟烘焙进音频、随后把 delay 清零，
  于是 `musicTime == chartTime`。顶栏原有的延迟输入框已换成
  `AudioPreprocess` 按钮；`DelayMs` 字段仍保留（旧包读写不变）。
  数学与符号见 `02-invariants.md` §A-23，实现在 `FalsumAudioPreprocessMath.cs`。

### A5 undo/redo

`FalsumChartSession`：`BeginChange(label)` → 修改 → `CommitChange()`。
提交前先 `FalsumSkyAreaGrouping.Normalize`，再用全字段 `Signature` 比对，无变化则不压栈。
栈上限 100。`ApplyBatch` 供工具脚本用：一次执行 = 一条撤销记录；有挂起事务时抛异常。
外部元数据状态由编辑器注入 `ExternalState` 字符串一起进快照。

### A6 控制流

```
GameplayBootstrap.Start (GameplayBootstrap.cs:251)
  → LoadChart() (:1014) → ChartParser.ParseFile → FalsumSkyAreaGrouping.Normalize
  → 音频/时钟/渲染器 → AddComponent<FalsumChartEditor>() + Initialize(this) (:322)
      → 清空宿主谱面（INFALSUS_CAPTURE_PRESERVE_CHART=1 可保留）
      → FalsumBeatGrid / FalsumChartSession / FalsumToolManager / FalsumPreferences
      → FalsumEditorView.Build() → FalsumEditorWorld
      → WireView() → SetEditorMode(true)
```

每帧（`FalsumChartEditor.Update` `:3936`）：UI Tick → 预览/菜单/指针 → `HandleKeyboard`
(`:7672`) + `HandleMouse` (`:7947`) → `UpdateViews` (`:11984`) + `TickAutoBackup` (`:5733`)。

键位默认值来自 `FalsumPreferences.CreateDefaultBindings`：`Q` 播放/暂停、`Space` 临时试听、
`F1/F2/F3` 三种中键精修、`Ctrl+S/Z/Y/C/X/V`、`Delete` 删除、`Ctrl+M` 镜像。
（游玩模式下则是 `P` 暂停、`R` 重开，见 `GameplayBootstrap.cs:378/397`。）

编辑一次的统一收尾：`FinishEditAdded` → `_session.CommitChange()` → `NotifyChanged()`
→ `RefreshEditorVisuals()`。

保存：`SavePackageInternal` (`:5599`) → `ChartWriter.Write` → 更新难度 `available`
→ `_package.Save` → 原子替换。

### A7 用户工具（Jint）

- 根目录：`<程序目录>/ExternalTools/<tool-id>/`（编辑器内为 `Assets/StreamingAssets/Falsum/UserTools/`）。
- 一个工具 = `tool.json` + `tool.js` + 可选 `i18n/{zh-CN,en-US}.json`。
- 安全性：递归 128、语句 1e6、内存 64 MB、超时 5 s、禁字符串编译、关 Interop；
  权限只有 `files.read` / `files.write`；无网络、无进程、无反射。
- `tool.json` 只提供**稳定回退元数据**；所有显示文本走 `i18n/*.json` 的 `tool.name` / `tool.description`。
- 内置两个示例：`com.falsum.tools.track-easing`（6 字段表单）、
  `com.falsum.tools.design-rule-checker`（无表单 + 报告）。

---

## B. 渲染与表现

### B1 装配与每帧顺序

`GameplayBootstrap.Update` (`:371`) 是**唯一**驱动：

1. `UpdatePresentationAnimation()` —— 先跑，暂停时也跑（只冻结累加）。
2. 若 `_running && !EditorModeActive`：
   - `F1` 暂停/恢复、`F2` 重开；
   - `engine.Update(now)` + `hitAudio.Tick(now)`；
   - **仅当 `IsPlaying`**：`noteRenderer.Update` → `sideLaneScroller.Update`
     → `laneState.Update` → `measureLines.Update`；
   - `flickEffects.Tick` / `noteEffects.Tick` **在播放判断之外**（暂停时仍淡出）。
3. `LateUpdate`：编辑器暂停时补停音频。

编辑器路径另有 `RefreshPresentationAt`：依次刷新时钟、音符、拍线、Combo、频谱、幽灵预览。

### B2 子系统

| 子系统 | 文件 | 职责 |
|---|---|---|
| 音符 | `G/Presentation/Notes/NoteRenderer.cs`（2340） | 全部音符视觉，按 kind 分池；13 个预制体入参 |
| 天空几何 | `Notes/SkyAreaGeometry.cs` | 5 ms 细分、共享边界 epsilon |
| 判定线辉光 | `Notes/SkyJudgementLineEffect.cs` + `…Feature.cs` | 4 缓冲轮转 + URP RenderGraph |
| 命中特效 | `Effects/NoteHitEffectRenderer.cs` | `NoteFX_Main` / `NoteFX_Radial` / `NoteHoldFXV2` 三池 |
| Flick 特效 | `Effects/FlickHitEffectRenderer.cs` | FlickBurst 左右池 + 判定反馈字 |
| 小节线 | `Tracks/MeasureLineRenderer.cs` | 三条 mesh，只画判定线→轨道末端 |
| 侧轨 | `Tracks/SideLaneSurfaceScroller.cs` | 4.0 m 周期纹理滚动 |
| 轨道透明度 | `Tracks/GameplayTrackOpacityController.cs` | 全轨/单轨渐变 |
| 轨道启停 | `Tracks/LaneStateController.cs` | `lane()` → `_LaneDisableInterval0..3` |
| 背景 | `Backgrounds/FalsumBackgroundPresenter.cs` | 背景 + playfield，queue 2900 |
| TrackEnd | `Backgrounds/FalsumTrackEndComposer.cs` + `…Feature.cs` | 菱形遮罩合成，`_TimeOverride` |
| 命中音 | `Audio/GameplayHitAudio.cs` | 48 槽 one-shot 池、hold/sky loop |
| BGM | `Audio/FalsumBassAudioEngine.cs` | BASS 后端，失败回退托管 |
| 限幅 | `Audio/GameplayMasterLimiter.cs` | 主输出限制器 |
| HUD | `Hud/GameplayHud.cs` | 状态注入 |
| 表现时钟 | `Timing/PresentationAnimationClock.cs` | 独立累加时钟（0.1 s 钳制） |
| 频谱 | `F/Runtime/FalsumTrackSpectrumRenderer.cs` | 编辑器轨道波形，queue 3002 |
| Combo | `F/Runtime/FalsumComboDisplay.cs` | AP Combo 显示 |

### B3 恢复资源约定

- **材质**：`Assets/InFalsus/Recovered/Material/matid_*.mat` 保留原名，不要改名。
  轨道侧从 `matid_trackfloor1` / `matid_trackwall1` / `matid_trackwall1_alt` 派生。
- **音符预制体**：`Recovered/GameObject/` 下的原版 prefab，由
  `Editor/GameplaySceneBuilder.cs` 注入 `GameplayBootstrap` 的序列化字段。
- **Shader 兼容层两条腿**：
  1. `Assets/InFalsus/Shaders/Compat/` —— URP 重写版（`InFalsus/FalsumTrackEndDiamond*`、
     `InFalsus/Compat/StenciledUnlitSprite`）；
  2. `Assets/InFalsus/Gameplay/Resources/` —— `Resources.Load` 可达的复刻版
     （`SkyArea/Shaders/*`）。
- **Falsum 自己的 Shader** 在 `Gameplay/Falsum/Resources/Falsum/Shaders/*`。
- **路径规则**：新资产只能落在上述位置；改 shader 名必须同步改 C# 里的字符串常量，
  否则运行时静默回退。

### B4 视觉表现的关键结构

- SkyArea：按 `GroupId` 每条连续链生成**一个** mesh（不是每段一个）。
- Hold：三段（front/middle/back）拼接，只在判定面裁剪，尾端过线才回收。
- Flick：`4=绿/右`、`16=黄/左`，trail 按 `width/1.54` 缩放后回接头部。
- 小节线：目标 z 必须落在 `[判定线-0.001, 轨道末端+0.001]`，否则整条跳过。
- 外侧墙三角形特效：**已整体移除**（2026-11-01，见 05-gaps.md §G-13）。
- RenderQueue 分层：背景 2900 < 频谱 3002。

---

## C. UI 与本地化

### C1 结构

全部交互 UI 是 Unity UI Toolkit（UXML/USS）。只有两样留在 uGUI 世界空间：
**选中轮廓**与**模式平面**，且不参与 UITK 命中测试。

| 组件 | 文件 | 行数 |
|---|---|---:|
| 编辑器主界面 | `F/UI/FalsumEditorView.cs` | 7267 |
| 偏好设置 | `FalsumPreferencesView.cs` | 3371 |
| 工具对话框 | `FalsumToolDialogView.cs` | 1706 |
| 工具报告 | `FalsumToolReportView.cs` | 735 |
| 径向菜单 | `FalsumRadialMenu.cs` | 388 |
| 模式平面（uGUI） | `FalsumModePlane.cs` | 面板面/边框/左侧信息框（`GRID` + 起止格号）；文字走 `FalsumWorldTextOverlay` |
| GRID 标尺（world mesh） | `FalsumEditorWorld.BuildRuler` | `_rulerMesh` queue 3032，`AddScreenSpaceLineQuad` + 度量线同款 shader/glow/像素宽 |
| 加密字体 | `FalsumSongFont.cs` | 372 |
| 风格切换 | `FalsumUiStyles.cs` | 71 |
| 性能窗口 | `FalsumPerformanceWindow.cs` | 858 |

资源：`F/Resources/Falsum/UI/*.uxml|*.uss` + `FalsumPanelSettings.asset` + `Icons/`。

### C2 数据流（严格单向）

- **View → Editor**：40+ `public event`（`FalsumEditorView.cs:382-434`），
  编辑器在 `WireView()`（`FalsumChartEditor.cs:4417`）一次性订阅。
- **Editor → View**：只经 `_view` 门面的 `Set*` 方法推状态，View 不持有编辑器。

### C3 风格切换

`FalsumUiStyle ∈ {Classic, Studio, Canvas, InFalsus}`（`FalsumPreferences.cs`）。
`FalsumUiStyles.Apply(root, style)` 先摘旧 sheet 再挂新 sheet；`Classic` 不挂附加表。
风格只换样式，**不重建编辑器、不换谱面世界颜色**。
`InFalsus` 为新配置默认，独立样式表 `FalsumInFalsus.uss`；高级页保留 Classic 回退入口。
主动选择通过 `Experimental.UiStyleChosen` 持久化；加载必须经现行 Experimental 组，
不能只依赖旧 Advanced 的迁移字段。属性面板高度随风格切换，手机布局仍由壳层计算。

### C4 本地化

- 运行时：`FalsumLocalization`（`F/Localization/FalsumLocalization.cs`），
  资源 `F/Resources/Falsum/Localization/{zh-CN,en-US}.json`（各约 965 个 key）。
- C#：`L.Get("key", ("name", value), ...)`，占位符写 `{name}`，禁止字符串拼接。
- UXML：元素 `name` 以 `l10n.` 开头 → 绑定 `text`；`l10nt.` → 绑定 `tooltip`。
- 工具：`<tool-id>/i18n/<locale>.json`，脚本里用 `ctx.text(key, {...})`。
- 审计：`python Tools/falsum_localization_audit.py`（6 项检查，退出码非 0 即有违规）。

---

## D. 编辑器 ↔ 表现层的接缝

| 接缝 | 机制 |
|---|---|
| 场景装配 | `GameplaySceneBinding` 按路径常量取 Transform/Renderer，缺失即 `Require` 失败 |
| 谱面加载 | `GameplayBootstrap.LoadChart()` 读 `Assets/StreamingAssets/Charts/*.chart.txt` |
| 编辑器接管 | `_host.SetEditorMode(true)`；`GameplayBootstrap.Update` 在编辑器模式下跳过游玩分支 |
| 时间 | 编辑器 seek 改 `GameplayClock`；表现层动画只由 `PresentationAnimationClock` 驱动 |
| 变速 | 编辑器 `SetPlaybackRate` 同时改时钟与音频 sink；表现时钟不受影响 |
| 截图/诊断 | `Assets/InFalsus/Editor/*.cs` 的 `[MenuItem]`，产物写 `Diagnostics/` |
