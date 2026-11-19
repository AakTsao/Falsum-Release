# 05 · 当前的坑、坏掉的东西、过期文档

> 接手时（2026-11-01）的真实状态。修掉一条就把这条划掉并注明日期。

---

## 1. 红色：正在失败

### G-0 背景 PNG→JPEG 后旧谱黑屏（**2026-11-02 已修**）
- 症状：`Player-prev.log` 里 `Gameplay background preload failed: Background file not found.`
  → `Gameplay background load failed: 31_Space-Positive.png (file not found: )`，
  旧 `.falsum` 打开后背景全黑；设置页缩略图也缺。
- 根因两件事叠在一起：
  1. 库文件改成 `.jpg`，而 `song.json` / 映射表仍写 `.png`；
     `FalsumBackgroundCatalog` 只把磁盘文件名当逻辑名，于是列表里出现 `.jpg`，
     与谱面里的 `.png` 对不上。
  2. 背景根目录从 exe 同级 `GameplayBackgrounds/` 迁到 `Backgrounds/`，
     旧构建仍只认新位置，覆盖解压又会同时留下两个目录。
- 修法（已落地，规则写进 `02-invariants.md` §D-3b）：
  - **目录回到原位置**：发布目录仍是 exe 同级 `GameplayBackgrounds/`；
    `Backgrounds/` 只作为迁移期旧构建的回退候选，构建会删掉误留的副本。
  - **谱面语义仍是 `.png`**：`.jpg` 只是制谱器为减重换用的物理格式，
    `FalsumManifest.SetBackground` / `CreateTemplate` 统一经
    `NormalizeBackgroundName` 成 `.png`，映射表也回退成 `.png`；
    UI 与写回的 `song.json` 一律 `.png`。
  - `FalsumRuntimePaths.ResolveBackgroundFile` 统一解析：
    请求扩展名 → `.png` → `.jpg` → `.jpeg` → 忽略大小写的同 stem 扫描；
    拒绝 `../`、`/`、`\`、`:`；目录不存在返回空而不是拼出不存在的路径。
  - `BackgroundDirectories` 是有序候选表（exe 同级 `GameplayBackgrounds/` →
    exe 同级 `Backgrounds/` → StreamingAssets 的两个位置），
    `FalsumBackgroundCatalog` 遍历所有根、缩略图走同一解析器。
  - `FalsumBuild.VerifyPublishedBackgrounds` 在构建末尾校验发布目录，
    解析不到直接失败。
  - `FalsumSelfCheck.AssertLegacyBackgroundCompatibility` 断言全部 40 个逻辑
    `.png` 名、缩略图、默认背景都能解析，且仍然拒绝路径穿越。
- 验证：编译 0 error、`FalsumSelfCheck` `passed:true`、
  构建日志 `Published backgrounds verified: 80 logical names`、
  新构玩家里 `Background preload: 35ms 2048x1024`、
  `backgroundCheck=pass externalPathsCheck=pass`。

### G-1 编辑器自检 28 项里跑到第 22 项就中断
- 症状：`Diagnostics/FalsumSelfCheck.json` → `passed:false`，
  `error` = `InvalidOperationException: Design rule tool description is incorrect.`
- 根因：`Assets/InFalsus/Editor/FalsumSelfCheck.cs:251` 断言
  `designRuleManifest.Description == "检查设计规则"`；而工作区的
  `tool.json` 已把 `description` 改成 `""`（本地化迁移未同步断言）。
  `FalsumToolManifest.Description` 只读 `tool.json` 原始字段，不读 `i18n/`。
- 影响：**只有自检被阻断**，产品本身没问题；但它让「全绿」这个信号失效。
- 修法（推荐 1）：
  1. 断言改为 `DisplayDescription`，或直接校验 `i18n/zh-CN.json` 的 `tool.description`；
  2. 恢复 `tool.json` 的中文描述（会重新引入硬编码，与 C-1 冲突，不推荐）。
- ~~顺带：同一处可能还有 `Design rule tool should not require fields` 之类依赖旧文本的断言，一并检查。~~
- **已修（2026-11-02）**：断言改为校验 `DisplayDescription` 非空且与 `Description`
  不同、`DisplayName` 与 `Name` 不同（即「文本确实来自 `i18n/`」），不再比对具体文案；
  其它硬编码字符串（`report.overlap.*` 的期望标题）改为从 manifest 的
  `GetText(key, ...)` 取，语言无关。`Diagnostics/FalsumSelfCheck.json` 现为
  `passed:true`，28/28 项全跑完。

### G-2 本地化审计 4 处硬编码 —— **已修（2026-11-02）**
- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs:2514/2516/2518/2520`
- 位置：`IsVerboseDescription` 的中文前缀表（`调整` / `点击` / `只备份` / `使用 FullScreenMode`）。
  它是**功能性**判断（文案是否走紧凑布局），不是展示文本，但审计脚本不区分。
- 修法（已落地）：前缀表移进本地化资源 `falsum.preferences.layout.verbosePrefixes`
  （`zh-CN.json` / `en-US.json` 各一份，`|` 分隔），代码从 `L.Get` 读并切分匹配。
  `python Tools/falsum_localization_audit.py` 与运行时审计（`FalsumLocalizationAudit.RunBatch`）均通过。

### G-13 外侧墙三角形特效已整体移除（2026-11-01）
- 原因：`WallMaterial.mat` / `4d.shader` 的重建还原度不足，与发行版差距明显。
- 已删除：`Gameplay/Presentation/Tracks/GameplayWallScroller.cs`、
  `Gameplay/Resources/WallTriangleOverlay/`、`Editor/GameplayWallSelfCheck.cs`、
  `Editor/GameplayWallCapture.cs`、`Recovered/Material/WallMaterial.mat`、
  `Recovered/4d.shader`；运行时场景移除 `LeftWallTriangles` /
  `RightWallTriangles`，`GameplayBootstrap` 与 `GameplaySceneBinding` 的接线一并回退。
- 保留：`PresentationAnimationClock` + `FalsumTrackEndComposer`（`_TimeOverride` 时间源不变）；
  原版 `LeftWall` / `RightWall` 窄 Quad 仍在场景里但保持禁用。
- 影响：游玩时左右外侧墙不再出现三角形滚动特效；
  恢复只能从 git 历史取回。

---

## 2. 黄色：已知但未修的缺陷

### G-14 `FalsumGridPreferences` 掉组 —— **已修（2026-11-08）**
- 症状：首选项里改过的网格线颜色 / 宽度，重启编辑器后全部回到默认值。
- 根因：v7 开始把网格样式写进独立的 `Grid` 分组，但
  `FalsumPreferencesMigration.LegacyPreferencesDto` **没有 `Grid` 成员**，
  `Build` 因此每次都 `new FalsumGridPreferences()` 重建，只从 v6 以前的
  `Advanced` 组里抬值 —— v7 写下来的东西读回时被静默丢弃。
- 修法：DTO 补 `Grid` 字段、`JsonPresence` 补 `PresenceGrid` 位（`HasTopLevelKey("Grid")` 探测）、
  `Build` 改走 `BuildGrid(dto.Grid, present)`；`Advanced` 的旧值**只在 Grid 组缺失时**才抬，
  避免 v7 文件被陈旧值覆盖。`CurrentVersion` 7 → 8 触发一次重写。
- 顺带收益：新增的 `Grid.DenominatorSequence` 有了可持久化的落点。
- 断言：`FalsumSelfCheck.CheckPreferences` 新增 v8 往返 + 陈旧 `Advanced` 不覆盖 + 坏序列修复三项。

### G-3 2D 模式三条遗留（审计 F-10 / F-18 / F-20）—— **已关闭（2026-11-12，2D 模式整体删除）**
> 墓碑：2D 视图（`Falsum2DWorld.cs` / `Falsum2DProjection.cs` / `Editor/Falsum2DCapture.cs`）
> 已于 2026-11-12 删除，`FalsumViewMode` 只剩 `Perspective3D`；下列三条只存在于已删代码，
> 无需修复。以下保留原文仅供参考，**不要再修**。
- **F-10**：`FalsumChartEditor.SnapPlaneCell` 用 `Camera.main` + `Input.mousePosition`，
  没有 2D 分支 → 2D 下的时间/位置吸附可能取错平面。
- **F-18**：`Falsum2DWorld` 的 SkyArea 用「裁剪后的 t」采样边缘 → 长链越界段的形状偏差。
- **F-20**：`Falsum2DWorld` 的 Flick 仍用 `Dictionary<int, FlickVisual>` → 与 3D 路径的
  刷新语义不同（3D 已改字典查找）。
- 影响：只在 2D 视图可见；3D 与游玩不受影响。

### G-4a Phase A 自检跑不起来：场景路径过期（2026-11-02 发现，当日已修）
- 症状：`-executeMethod InFalsus.EditorTools.GameplaySelfCheck.Run` 立刻抛
  `ArgumentException: Scene file not found: 'Assets/InFalsus/Recovered/Scenes/GameplayClean.unity'`
  （`GameplaySceneBuilder.cs:270`），整个 Phase A 一项都没跑。
- 根因：场景已迁到 `Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity`，
  但 `GameplaySceneBuilder` / `SceneCapture` / `SceneSanitizer` / `GameplayMeshDiagnostics` /
  `ShaderDiagnostics` 五处仍硬编码旧路径 `Assets/InFalsus/Recovered/Scenes/GameplayClean.unity`。
- 影响：**Phase A 自检与所有场景截图工具全部失效**，不是渲染问题。
- 修法（已落地）：五处常量改为 `GameplayRuntime.unity`；`GameplaySceneBuilder`
  的 `CleanScenePath` 直接复用 `RuntimeScenePath`，`SceneSanitizer` 的
  `SourceScenePath` 同样指向运行时场景、`CleanScenePath = SourceScenePath`。
  Phase A 自检现已完整跑通（`Diagnostics/UnityGameplaySelfCheck.log`）。

### G-4 `_drafts` 里 24 处需要人工确认的「未核实」
各个 subagent 在 `docs/memory/_drafts/` 留下的地图里，标了「未核实」的点。
主要几处：
- 偏好页 `IsVerboseDescription` 的中文前缀是否会命中——**已验证会命中**（源码为正常 UTF-8 中文），
  但某次 `Get-Content` 不带 `-Encoding UTF8` 会显示乱码，曾误判为「乱码字节」。
- `FalsumTools.uss`（25.5 KB）在本层 C# 无 `Resources.Load`——是否由 UXML 的 `style=` 间接引用，
  未逐一核对。
- `FalsumLocalization.LoadLocale` 的回退语言选择逻辑未逐行核对。
- `FalsumChartSession` 的 `ExternalState` 各字段语义未逐项核对。

> 这些是**地图的注释**，不是产品的缺陷；需要时按 `_drafts` 里的 `path:line` 追。

### G-5 README.md 与代码冲突 —— **已于 2026-11-01 全部修正**

| README 原位置 | 原来写的 | 实际 / 现在的处理 |
|---|---|---|
| `:163-164` | `NoteFX_*`「存在但未重挂判定事件」 | 已订阅 `NoteStarted` / `Judged`（`NoteHitEffectRenderer.cs:121-122`）；场景原 object 被 `HidePreexistingNoteEffects` 隐藏，显示由克隆池负责 → **已改写** |
| `:252-256` | 构建输出 `Builds/FalsumEditor-Alpha0.31-20261026` | 代码产出 `Builds/FalsumEditor-0.5-Alpha`（`FalsumBuild.cs:21`）→ **已改写，并补了「构建会改写资产」的警告** |
| `:273-282` | Diagnostics 菜单清单 | 缺 `Run Gameplay Wall Self Check`、`Capture Gameplay Wall`、`Run Package Compatibility Check`、`Tools/FALSUM/Localization/Audit` → **已补齐** |
| `:287-305` | 用 `INFALSUS_CHART_PATH` 指定调试谱面 | **本来是对的**（`GameplayBootstrap.ResolveChartPath` `:1056-1058` 读取）；早期审计只搜 `Editor/` 而误判失效 → **已补注读取点** |
| `:300-301` | 「See `Diagnostics/Evidence.md`」 | 该文件是 2026-10-17 快照 → **已改写为指向当前真相报告 + 本目录** |

仍存的小瑕疵：README `:44` 的链接指向 `../docs/superpowers/…`（父目录文件），
在本仓库内点不开——这是**有意为之**（该文档确实只存在于父目录），已加说明。

### G-6 文档里已过期的数值
- `S/2026-10-19-falsum-ui-toolkit-rewrite-design.md` 的顶栏/底栏/属性面板尺寸段。
- `S/2026-10-27-falsum-trackend-clean-rebuild-design.md` 的「输出 UV y=0.7 / ×1.12 / 六边形半径 1.0」
  （代码是 `OutputUvMax=0.5` / `HexagonScale=2.0`）。
- `S/2026-10-28-falsum-spectrum-placeholder-design.md` **整篇作废**（频谱功能仍在运行）。
- `S/2026-10-26-falsum-track-end-background-design.md` **整篇作废**（被 clean-rebuild 取代）。

---

## 3. 结构性的技术债（不是 bug，但会拖慢每次改动）

### G-7 `FalsumChartEditor.cs` 12565 行，零分区
没有 `#region`，几乎没有 `//` 分区注释，只能靠方法名导航。
测试代码也寄生在里面：`DebugRun*Checks` 系列 + `FalsumChartEditor.MiddleEdit.cs` 里的
`DebugRunMiddleEditChecks`（约 1280 行，占该文件 40%）。

### G-8 `CloneNote` 4 份副本，语义不同
`ChartWriter.cs`、`FalsumChartSession.cs`、`FalsumChartEditor.cs`、`FalsumToolRuntime.cs`。
其中编辑器版**丢**了 `Consumed/Judged/Holding/Result/HoldStartResult`，会话版保留
→ 复制粘贴与撤销后的判定态不一致。

### G-9 3D/2D 两套并列几何实现 —— **已关闭（2026-11-12，2D 旁系删除）**
> 墓碑：`Falsum2DWorld.cs`（2142）已于 2026-11-12 删除，只剩 `FalsumEditorWorld.cs`，
> 不再有「两套必须手工同构」的问题；这也是 G-3 的来源，现已一并关闭。以下为原文存档。
`FalsumEditorWorld.cs`（3020）与已删除的 `Falsum2DWorld.cs`（2142）各自实现了 `MeshBuffer`、
`TryPick`、`PointerTrack`、`SnapTime`，必须手工保持同构 —— 这正是 G-3 的来源。

### G-10 三套「最短时长」
`MiddleMinimumDurationMs = 1.0` / 属性面板 `Math.Max(1.0, …)` / 天空区落点 `Math.Max(10.0, …)`。

### G-11 硬编码占位
`FalsumChartEditor.InitializeManifest` 固定标题 `"a la mode"` / 作者 `"aran"`；
幽灵预览 `GroupId = 900000`。

### G-12 全仓零 TODO/FIXME
技术债都是结构性的、没有标注。找坑只能靠本目录的文档和自检。
