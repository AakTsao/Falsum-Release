# Falsum UI 层记忆（附录）

> 正文版见 [`../01-architecture.md`](../01-architecture.md) §C 与 [`../02-invariants.md`](../02-invariants.md) §C。
> 行数口径：本文件用 `(Get-Content).Count`（PowerShell `Measure-Object -Line` 会少数，
> 例如 `FalsumEditorView.cs` 实际 **7267** 行）。
>
> 范围：`Assets/InFalsus/Gameplay/Falsum/UI/`、`Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/`、`Assets/InFalsus/Gameplay/Falsum/Localization/FalsumLocalization.cs`、`Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Localization/{zh-CN,en-US}.json`。所有结论带 `path:line`；未逐行核实者标注"未核实"。

## 1. UI 组件总表

| UI 组件 | 文件 | 职责 | 行数 |
|---|---|---|---|
| FalsumEditorView | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs` | 编辑器主界面：顶栏/底栏/属性面板/事件面板、菜单、弹窗宿主、歌曲信息卡、IME、数字输入清洗 | 6936 |
| FalsumPreferencesView | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs` | 偏好设置弹窗（多 Tab、按键捕获、UI 风格预览） | 3371 |
| FalsumToolDialogView | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolDialogView.cs` | 工具运行对话框（表单字段、取点/取时间、结果提交） | 1706 |
| FalsumToolReportView | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolReportView.cs` | 工具报告弹窗（列表、定位跳转、严重度着色） | 735 |
| FalsumRadialMenu | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumRadialMenu.cs` | 右键径向菜单（扇区布局、屏幕→面板坐标换算、边缘夹取） | 388 |
| FalsumModePlane | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumModePlane.cs` | 模式平面提示（uGUI 网格/背景/标签，非 UIElements） | 432 |
| FalsumSongFont | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSongFont.cs` | 加密字体包加载（PBKDF2/AES 头校验，供歌曲信息卡用） | 372 |
| FalsumSelectionOverlayGraphic | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionOverlayGraphic.cs` | 选区/框选多边形绘制（MaskableGraphic 三角化填充） | 273 |
| FalsumUiFactory | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiFactory.cs` | uGUI 侧工厂：Rect/Image/Text 创建、白图 Sprite、字体 | 184 |
| FalsumPerformanceWindow | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPerformanceWindow.cs` | 性能面板（CPU/GPU/预算折线图、计量条） | 858 |
| FalsumUiMotion | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiMotion.cs` | 弹窗/窗口进出场动画（透明度+缩放，`ModalDurationSeconds = 0.16f`） | 216 |
| FalsumUiStyles | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiStyles.cs` | 按 `FalsumUiStyle` 挂/摘 Studio、Canvas 样式表 | 71 |
| FalsumFonts | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumFonts.cs` | 内置 CJK 字体加载单例（`FalsumFonts.Cjk`） | 60 |

行数为 `Measure-Object -Line` 实测，与给定值一致。

资源侧（`Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/`）：UXML 13 个 —— `FalsumEditor.uxml`(10871B)、`FalsumNewChartDialog.uxml`(8170B)、`FalsumPreferencesDialog.uxml`(5674B)、`FalsumToolDialog.uxml`(4403B)、`FalsumToolReportDialog.uxml`(1734B)、`FalsumDisclaimerDialog.uxml`(1660B)、`FalsumRadialMenu.uxml`(1598B)、`FalsumConfirmDialog.uxml`(1097B)、`FalsumMenuItem.uxml`(570B)、`FalsumPropertyRow.uxml`(547B)、`FalsumMenuPanel.uxml`(371B)、`FalsumMenuSeparator.uxml`(243B)、`FalsumRadialEntry.uxml`(238B)。USS 4 个 —— `FalsumEditor.uss`(114414B)、`FalsumTools.uss`(25548B)、`FalsumStudio.uss`(8395B)、`FalsumCanvas.uss`(8013B)，另有 `FalsumRuntimeTheme.tss`(55B)、`FalsumPanelSettings.asset`(1905B)。`Icons/` 5 个 SVG（play/pause/undo-2/redo-2/audio-lines）+ LICENSE；歌曲卡背景 `SongInfoCard{MIN,EVO,ULT,FBD}.png` 与 `FalsumLogo.png` 位于同级目录。

## 2. 构造与数据流

**谁创建 root**：`FalsumChartEditor.Initialize` 里 `_view = new FalsumEditorView(transform)` 后立即 `_view.Build()`：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs:279-280`。`FalsumEditorView` 自持 `UIDocument`（`FalsumEditorView.cs:149`）。`Build()` 中 `Resources.Load<PanelSettings>("Falsum/UI/FalsumPanelSettings")`（`:796-797`，常量 `:31-32`），失败回退 `CreateRuntimePanelSettings()`（`:804`，实现 `:4914-4926`，其中 `Resources.Load<ThemeStyleSheet>("Falsum/UI/FalsumRuntimeTheme")` 于 `:4919`）；再 `Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor")`（`:807`，常量 `:30`），`_document = CreateDocument(panelSettings, mainUxml)`（`:808`，实现 `:4898-4911`），`_root = _document.rootVisualElement`（`:809`）。UXML 缺失时报 `falsum.error.ui.mainResourceMissing` 并 `ShowBuildError`（`:812-820`）。

**Build 顺序（关键不变量）**：`L.Initialize()` → LoadTemplates → QueryMainElements → `L.ApplyStaticText` → `ApplyLocalizedStaticText` → ConfigureFonts → ConfigureSongInfoCard → UpdateResponsiveClasses → 两个 `ConfigureSliderFill` → BindTopBar/BindBottomBar/BindPropertyPanel/BindEventsPanel → 构造 PreferencesView/PerformanceWindow/ToolDialog/ToolReport → `FalsumUiStyles.Apply(_falsumRoot ?? _root, _uiStyle)`：`FalsumEditorView.cs:793-864`。子视图构造顺序固定于 `:842-864`，任何异常被收口为 `ShowBuildError`（`:866-870`）。

**子模板加载**：`LoadTemplates()`（`:4929`）经 `RequireTemplate(path)`（`:4947-4958`）逐个 `Resources.Load<VisualTreeAsset>`；13 条路径常量集中在 `:30-57`（PropertyRow/MenuPanel/MenuItem/MenuSeparator/ConfirmDialog/DisclaimerDialog/NewChartDialog/PreferencesDialog/ToolDialog/ToolReportDialog 及 `SongInfoCardBackdropPathPrefix`）。

**USS 按 `FalsumUiStyle` 切换**：枚举 `Classic/Studio/Canvas` 定义在 `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs:110-116`（注意：任务给的 `FalsumPreferences.cs:111-128` 实际覆盖枚举 `:111-116` 与默认值 `UIStyle = FalsumUiStyle.Classic` `:127`，属 `FalsumAdvancedPreferences`）。`FalsumUiStyles.Apply` 先移除已缓存的 `_studio`/`_canvas`（`FalsumUiStyles.cs:13-27`），再按枚举 `AddStyle(root, ref sheet, "Falsum/UI/FalsumStudio" | "Falsum/UI/FalsumCanvas")`（`:28-49`）；`Classic` 不加载任何附加 sheet，即只保留 UXML 自带样式。`AddStyle` 内 `Resources.Load<StyleSheet>` 失败打印 `[Falsum] UI style missing: <path>`（`:55-59`），`Contains` 判重（`:61-64`）。入口 `FalsumEditorView.SetUiStyle`（`:646-655`，作用于 `_falsumRoot ?? _root` `:651`），应用后不重跑静态文本，只做 `RefreshMiddleEditStatusText()`（`:657-671`，Canvas 下清空 `middleEdit.alternatives` `:664-670`）与 `RefreshTransportIconTint()`（`:673-698`，按风格换播放/暂停 `tintColor`，颜色常量 `:68-81`）。`SetShortcutPreferences` 内部隐式调用 `SetUiStyle`（`:636-644`），而 `FalsumChartEditor` 又在 `:281` 显式调用一次同值风格，形成幂等双应用。

**回调方向（View→Editor，事件）**：View 声明 40+ 个 `public event`，集中在 `FalsumEditorView.cs:382-434`（`OpenFileRequested:382`、`PlayPauseClicked:389`、`SpeedSubmitted:414`、`EventSubmitted:431` 等）。Editor 在 `WireView()` 一次性订阅（`FalsumChartEditor.cs:4417-4486`），并分派到 `WireToolDialog()`（`:4488-4517`，含 `TimeResolver:4495`、`RunRequested:4506`）与 `WirePreferencesView()`（`:4519-4555`）。View 内部统一经 `Raise(action)`/`Raise<T>`（`FalsumEditorView.cs:7097-7104`、`:7248-7262`）抛事件，控件绑定集中在 `BindTopBar`（`:5745-5812`）、`BindBottomBar`（`:6091-6130`）、`BindEventsPanel`（`:6139-6186`）。

**回调方向（Editor→View，方法推状态）**：`SetPlaybackState`（Editor `FalsumChartEditor.cs:323` / View `:1209`）、`SetSpectrumState`（`:283` / `:1476`）、`SetGridEnabled`（`:285` / `:1303`）、`SetToolCatalog`（`:286` / `:2192`）、`SetUiStyle`（`:281` / `:646`）、`SetEventsPanelOpen`（`:1328,1332` / `:6187`）、`SetChartLayerVisibility`（`:968` / `:1295`）、`SetPerformanceMonitor`（`:282` / `:600`）。**方向规则**：View 不持有 `FalsumChartEditor`，Editor 只经 `_view` 门面与事件订阅通信。

**风格预览回路**：`FalsumPreferencesView.UiStylePreviewRequested`（`FalsumPreferencesView.cs:147`，触发点 `:2125` 处 `(FalsumUiStyle)style.index`，备份字段 `_styleOnOpen:97`）→ `FalsumChartEditor.PreviewUiStyle`（`FalsumChartEditor.cs:4557-4564`）→ 仅当偏好弹窗打开时 `_view.SetUiStyle(style)`（`:4559-4561`）。

## 3. 本地化绑定规则

- **前缀与资源根**：`LocalizedTextPrefix = "l10n."`、`LocalizedTooltipPrefix = "l10nt."`（`Assets/InFalsus/Gameplay/Falsum/Localization/FalsumLocalization.cs:16-17`）；`ResourceRoot = "Falsum/Localization/"`（`:15`）；`zh-CN`/`en-US`（`:12-13`，列表 `:19-23`）。
- **UXML 自动绑定**：`ApplyStaticText(root)` 遍历 `root.Query<VisualElement>()`——name 以 `l10n.` 开头则把后缀 key 赋给 `TextElement.text`（`:184-196`）；以 `l10nt.` 开头则赋给 `element.tooltip`（`:197-204`）；无 name 直接跳过（`:180-183`）。调用点 `L.ApplyStaticText(_root)`（`FalsumEditorView.cs:827`）。`ApplyLocalizedStaticText()`（`:873-929`）再用 `L.Bind`/`L.BindTooltip` 手工补齐无前缀元素（`ClearHistory:889-893`、`EventCount:909-910`、`AddBpmEvent:915-917`、`playTooltip:924` 等）。
- **代码侧 API**：`Bind(TextElement, key)` → `element.text = Get(key)`（`FalsumLocalization.cs:149-157`）；`BindTooltip(VisualElement, key)`（`:159-167`）。
- **具名占位符**：`Get(string key, params (string Name, object Value)[] variables)` 拼 `"{" + Name + "}"` 后 `string.Replace`，按传入顺序替换，`Name` 为空则跳过（`FalsumLocalization.cs:97-130`，拼接 `:124`，替换 `:125-127`，值格式化 `FormatValue` `:127`）。key 为空返回 `""`（`:101-104`）；未命中先查 `Strings` 再查 `FallbackStrings`，仍未命中**返回 key 原文**（`:108-112`）——UI 会显裸 key。回退表在 `LoadLocale` 中按 `_fallbackLocale` 装载（`:216-228` 起，具体回退语言选择未逐行核实）。
- **校验脚本** `Tools/falsum_localization_audit.py`（348 行）检查六项：(1) 两语言 key 集合对称，逐条报 `missing`/`extra`（`:263-271`）；(2) 占位符集合一致且无残缺花括号（`:273-288`，正则 `PLACEHOLDER_PATTERN` `:26-28`）；(3) 代码引用的 key 必须存在于参考语言（`collect_used_keys:149`，判定 `:290-296`）；(4) `Falsum/**/*.cs` 字符串字面量禁止汉字（`HAN_PATTERN:30`，扫描 `:298-305`）；(5) C# 里 `new Label("…")`/`text = "…"`/`tooltip = "…"` 含任意字母或汉字即报 "direct UI text literal"（`CSHARP_UI_LITERAL_PATTERN:37-40`，`:306-313`）；(6) UXML 的 `text=`/`tooltip=` 属性含字母或汉字即报 "visible UXML attribute is not localized"（`UXML_VISIBLE_ATTRIBUTE_PATTERN:34-36`，`:315-325`）。另有 `validate_tool_localizations`（`:163`，调用 `:327`）校验 UserTools JSON（细节未核实）。JSON 重复 key 抛错（`:50-58`）；失败退出码 1，异常 2（`:332-344`）。

## 4. UI 不变量

1. **硬编码可见文本禁止**，一律走 `L.Get`/`l10n.*`（审计脚本双向拦截：`Tools/falsum_localization_audit.py:306-325`）。**唯一已知例外**：`FalsumPreferencesView.cs:2511-2529` 的 `IsVerboseDescription` 前缀表——4 条中文前缀（文件内为乱码字节 `:2513-2520`）与 4 条英文前缀（`:2521-2528`），用于判定文案是否走紧凑布局（调用点 `:2493`）。该中文前缀为乱码，若 `zh-CN.json` 是正常中文将永不命中，疑似既存回归（未核实 JSON 原始字节）。
2. **数字输入必须过 `SanitizeNumericInput`**：公开静态入口 `SanitizeNumericInput(string, bool allowFraction = false)`（`FalsumEditorView.cs:720-725`）→ `SanitizeNumericText`（`:7184`）→ `NormalizeNumericCharacter`（`:7208`）/`IsNumericCharacter`（`:7237`）；字段级安装器 `RegisterNumericInput`（`:7105`）、单字段清洗 `SanitizeNumericField`（`:7165`）。跨文件复用：`FalsumPreferencesView.cs:1186`、`FalsumToolDialogView.cs:988`。自检用例 `SanitizeNumericInput("eA1.f2E")`（`:4665`，位于 `DebugRunImeFocusChecks`）。
3. **焦点/IME 定位**：仅当焦点在 `TextField` 且"指针授予焦点或弹窗打开"时开 `IMECompositionMode.On`，否则 `DisableIme()`（`FalsumEditorView.cs:5478-5491`、`:5534-5540`）；字段不可见也禁用（`IsEffectivelyVisible` `:5550`）；锚点 = 光标位置 + 字段高度（`ImeAnchor:5542-5548`）；`Input.compositionCursorPos` 由 `PanelTopLeftToWindow` 换算（`:5500-5502`）；native 侧 `FalsumImePosition.TrySetNormalizedPosition` 用 `ImmSetCompositionWindow`(CFS_POINT|CFS_FORCE_POSITION)+`ImmSetCandidateWindow` 定位（`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumImePosition.cs:14-74`，常量 `:10-12`），窗口按进程归属校验（`:83-108`）。节流不变量：归一化坐标未变且未到 `_nextImeSyncAt` 则跳过，成功延 0.25s、失败延 0.05s（`FalsumEditorView.cs:5513-5531`）。
4. **径向菜单坐标**：屏幕点先 `RuntimePanelUtils.ScreenToPanel` 再翻 Y（`layer.layout.height - raw.y`）（`FalsumRadialMenu.cs:374-382`）；中心夹取 `Margin=8`（`:324`、`:335-348`）；框尺寸夹取 260–440 × 220–390（`:325-332`）；半径下限 54、上限由到四边余量减 70 决定（`:350-358`）；条目偏移固定 `-62/-23`（`:302-305`）。菜单根在构造期即 `_layer.Add(_root)` 并 `Hide()`（`:115-116`），打开只改坐标不改层级。
5. **歌曲信息卡尺寸**：设计稿 `3013 × 559`（`:58-59`），宽度比 `0.32175`（32.175%，`:60`），`scale = rootWidth * ratio / 3013` 并统一推算高低左右；右留 24、上留 18（`:61-62`）；左上从 `_topBar.layout.yMax + 18` 起算并联动 `_hintPanel`（`FalsumEditorView.cs:5593-5636`）；`_songInfoCard.style.scale = new Scale(scale, scale)`（`:5627-5628`）；卡片 `SendToBack()`（`:5113`）。
6. **响应式断点**：`CompactScreenWidth=1500`、`CompactScreenHeight=840`、`NarrowScreenWidth=900`、`ShortScreenHeight=700`、`LowScaleThreshold=0.82`（`FalsumEditorView.cs:90-94`）；`Tick()` 比较 `Screen.width/height` 变化即重算 `UpdateResponsiveClasses()`（`:931-940`，实现 `:5259`）；验证入口 `DebugRunLayoutChecks`（`:4698`）。
7. **重排条数上限**：本层**未核实**存在"视觉树重排条数上限"常量（全目录 `Reorder` 仅命中备份排序，无 UI 相关）。可核实的相关约束：事件行复用 `_eventRows` 列表（`:144-145`）与计数 `DebugEventRowCount`/`DebugEventRefreshCount`（`:705-713`）、属性行 `_propertyRows`（`:140-141`）与 `DebugPropertyRefreshCount`（`:715-718`）；`FalsumModePlane` 网格上限 `MaxGridCells = 192`（`FalsumModePlane.cs:18`，`Mathf.Clamp(divisions, 1, MaxGridCells)` `:421`）；难度下拉最多 4 项（`FalsumEditorView.cs:5245` 判定 + 数组 `:83-89`）。
8. **弹层互斥与焦点归还**：`IsEditorFocusHandledByOverlay`（`:5408`）、`IsFloatingToolReportTarget`（`:5399`）决定根级 PointerDown 是否吞事件；`GetFocusedElement`（`:6955`）、`RestoreFocus`（`:6965`）、`TrapFocus`（`:6975`）、`ClearInputFocus`（`:4262`）、`IsTyping`（`:4248`）供快捷键让位。
9. **模态时长单一来源**：`FalsumUiMotion.ModalDurationSeconds = 0.16f`（`FalsumUiMotion.cs:9`），被 `AnimateModalIn`（`:13`）、`AnimateModalOut`（`:73`）、`AnimateWindowIn`（`:135`）复用；另有两个独立常量 `EventsPanelAnimationSeconds=0.22`、`ChartPreviewChromeAnimationSeconds=0.24`（`FalsumEditorView.cs:64-65`），不得混用。
10. **字体单一来源**：UI 字体取 `FalsumFonts.Cjk`（`FalsumEditorView.cs:5092`），歌曲卡字体 `FalsumSongFont.CardFont ?? FalsumFonts.Cjk`（`:5671-5675`），唯一书写点为 `ApplySongInfoTextFont`（`:5664`）与 `ApplyDisplayFont`（`:5685`）。

## 5. 已知粗糙点

- **超大且无分区**：`FalsumEditorView.cs` 6936 行、`FalsumPreferencesView.cs` 3371 行，但 `#region` 计数为 0，逐行扫描也**没有以 `//` 开头的独立分区标题行**——任务预期的 `// ` section comments 在此文件不存在，只能靠 `Build*`/`Bind*`/`Configure*`/`Debug*` 方法名推断分区。头部 40+ 事件（`:382-434`）与尾部一堆静态助手（`:7030-7262`）构成典型上帝类。
- **重复代码**：`Require<T>` 四份（`FalsumEditorView.cs:7062`、`FalsumRadialMenu.cs:396`、`FalsumToolDialogView.cs:1727`、`FalsumToolReportView.cs:763`）；`SetVisible` 三份（`FalsumEditorView.cs:7044`、`FalsumToolDialogView.cs:1743`、`FalsumToolReportView.cs:751`）；`Raise` 家族两套（`FalsumEditorView.cs:7097-7262`、`FalsumToolDialogView.cs:1757-1785`）；`NoteKindLabel` 两份（`FalsumEditorView.cs:4233`、`FalsumToolReportView.cs:723`）；`IsWithinElement`/`IsWithin` 两份（`FalsumEditorView.cs:5415,6387`、`FalsumToolReportView.cs:571`）；边缘夹取方言三处（`FalsumPerformanceWindow.cs:869-882`、`FalsumToolReportView.cs:555-568`、`FalsumRadialMenu.cs:335-348`）。
- **死代码/低使用**：`FalsumUiStyles.RemoveStyle`（`FalsumUiStyles.cs:61-69`）无调用者；`Classic` 分支不装 sheet，却仍维护 `_studio`/`_canvas` 静态缓存（`:10-11`）；`FalsumEditorView` 的 9 个 `DebugRun*Checks`（`:1247,4352,4378,4473,4585,4607,4698,4777,4830`）在发布构建是否剔除未核实。
- **样式引用关系待查**：`FalsumTools.uss`(25.5KB) 在本层 C# 未见 `Resources.Load` 或 `styleSheets.Add`（未核实是否由 UXML 的 `style=` 或工具弹窗间接引用）；`FalsumRuntimeTheme.tss` 仅经 PanelSettings 路径加载（`FalsumEditorView.cs:4919`）；`FalsumEditor.uss`(114KB) 主样式与 `FalsumStudio.uss`/`FalsumCanvas.uss` 的选择器覆盖优先级未核实。
- **本地化盲区**：`Get` 未命中返回裸 key（`FalsumLocalization.cs:111`）；审计脚本只比较 key 集合与占位符集合（以参考语言为基准 `:273-288`），无法拦截"运行期传入的 variables 缺项导致残留 `{x}`"。
- **风格双应用与回滚**：`FalsumChartEditor.cs:281` 与 `FalsumEditorView.cs:640-643` 两处应用同名风格；`PreviewUiStyle`（`FalsumChartEditor.cs:4557-4564`）直接改 View 风格、不经偏好存储，取消时的回滚路径未核实（`_styleOnOpen` `FalsumPreferencesView.cs:97` 疑为回滚依据）。
