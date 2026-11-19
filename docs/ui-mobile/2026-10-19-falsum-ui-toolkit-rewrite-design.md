# FALSUM 制谱器 UI Toolkit 重写设计

## 目标

将现有 FALSUM 制谱器的运行时 uGUI 界面整体迁移到 Unity UI Toolkit，
保留当前编辑能力、快捷键、播放流程、菜单占位行为和中央 3D 谱面。
视觉方向采用“精密工具”：延续现有顶栏、底栏和左侧属性面板的位置，
收紧间距、层级、焦点反馈和控件状态，不使用圆角。

## 范围

本次重写包含：

- 顶栏、底栏、歌曲信息、属性面板和模式提示。
- 文件、编辑、视图、工具菜单及其下拉层。
- 缓动选择、确认框和右键径向菜单。
- 选中音符的轮廓绘制。
- UI 命中测试、输入焦点判断和屏幕坐标转换。
- UI Toolkit 资源的加载、自检、截图验证和独立包冒烟测试。

本次不包含：

- 补齐当前菜单和按钮中的占位功能。
- 修改谱面格式、编辑状态机、选择算法或音符渲染尺寸。
- 重做快捷键和右键操作流程。
- 将中央 3D 谱面迁移到 UI Toolkit。

## 已确认约束

- 所有交互界面必须由 UI Toolkit 接管。
- 所有界面元素使用直角，圆角统一为 0。
- 静态布局和样式必须由 UXML/USS 表达。
- 动态列表项必须实例化 UXML 模板并在 USS 中定义样式。
- 模式平面保留原版世界空间 Canvas、uGUI 网格和文字，不迁移到
  UI Toolkit。
- 现有功能行为保持不变，不顺手实现占位菜单。

## 架构

### 运行时入口

`FalsumChartEditor` 继续负责编辑状态机和业务行为。初始化时创建
`UIDocument`，加载 `PanelSettings` 与主 UXML，并把根 `VisualElement`
交给 `FalsumEditorView`。UI Toolkit 面板使用屏幕空间渲染，位于 3D 世界之上。

`PanelSettings` 使用 1730×1080 参考分辨率和 `MatchWidthOrHeight`
缩放模式，适配 1152×720、1920×1080 与 4K。面板必须允许透明背景，
中央未占用区域不能阻挡谱面指针交互。

### UI 层边界

- `FalsumEditorView`：加载并缓存 UXML 元素，同步显示值，注册事件，
  将 UI 动作转发为现有 C# 事件。
- `FalsumRadialMenu`：管理 UITK 径向菜单的显示、隐藏、条目选择与位置，
  不再持有 `Canvas`。
- `FalsumModePlane`：保留原版世界空间 Canvas、uGUI 网格和文字实现。
- `FalsumSelectionOverlayGraphic`：保留原 uGUI `MaskableGraphic` 实现，
  在独立且不可交互的 Overlay Canvas 上绘制选中轮廓。
- `FalsumUiHitTester`：负责屏幕坐标到面板坐标转换、UI 命中和输入焦点判断。

`FalsumUiFactory` 仅保留模式平面所需的原版 uGUI 辅助方法。FALSUM 交互 UI
不引用 `UnityEngine.UI`、`Canvas` 或 `EventSystem`。选中轮廓和模式平面是
两个保留的非交互 uGUI 渲染路径；它们不接收输入，也不参与 UITK 命中测试。

### 资源结构

主资源放在 `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/`：

- `FalsumPanelSettings.asset`
- `FalsumRuntimeTheme.tss`
- `FalsumEditor.uxml`
- `FalsumEditor.uss`
- `FalsumPropertyRow.uxml`
- `FalsumMenuItem.uxml`
- `FalsumMenuSeparator.uxml`
- `FalsumRadialMenu.uxml`
- `FalsumRadialEntry.uxml`
- `FalsumConfirmDialog.uxml`

主 UXML 只负责固定结构。属性行、菜单项、缓动项和径向条目通过
`VisualTreeAsset.Instantiate()` 创建。运行时主题使用
`@import url("unity-theme://default")`，再叠加项目 USS。若模板增加新的
视觉样式，必须放入 USS，而不是在 C# 中设置颜色、间距或边框。

## 组件设计

### 顶栏

顶栏高度保持 56 像素，顺序为：

1. 文件、编辑、视图、工具。
2. 流速标签、滑条和数值输入。
3. 延迟输入。
4. 当前歌曲标题与艺术家/难度。
5. BPM 与音符数。

菜单使用独立弹出层，每个条目由 UXML 模板实例化。菜单项具备默认、
悬停、按下、键盘焦点和禁用状态。分隔线也是独立 UXML 模板。

### 底栏

底栏高度保持 68 像素，顺序为：

1. 撤销、重做及其计数。
2. 清空历史。
3. 节拍细分输入。
4. 当前时间输入。
5. 播放/暂停。
6. 谱面进度条。
7. 当前时间/总时间。
8. 播放倍速。
9. 事件按钮。

进度滑条占据剩余空间。其余控件保持固定或最小宽度，避免窗口缩放时
互相覆盖。所有滑条使用 UITK `Slider`，输入框使用 `TextField`。

### 属性面板

属性面板固定在左上区域，仅在选中单个音符时显示，宽度 358 像素。
属性行由 `FalsumPropertyRow.uxml` 实例化，包含标签、数值输入或缓动按钮。
面板高度根据行数计算，跳转按钮固定在内容末尾。

属性值提交继续通过 `PropertySubmitted` 事件进入现有
`FalsumChartEditor.SetProperty`。程序更新值时必须使用不触发回调的路径。

### 径向菜单

径向菜单由全屏 UITK 根层、半透明背景遮罩、中心标记和 UXML 条目模板组成。
显示位置通过 `RuntimePanelUtils.ScreenToPanel` 转换。条目布局保持当前环形
顺序，悬停和键盘焦点使用 USS 状态。

### 确认框

确认框由 UXML 模板和全屏遮罩组成。取消和确认按钮保持现有行为，
确认后继续调用原回调。重复打开确认框时销毁或复位现有实例，避免叠加。

### 选中轮廓

选中轮廓继续使用原版 `FalsumSelectionOverlayGraphic`、`Canvas` 和
`MaskableGraphic` 技术栈。该 Canvas 只承载轮廓，关闭射线检测，不包含任何
交互控件，并使用低于 UI Toolkit 面板的 sorting order。轮廓数据继续来自
现有渲染几何生成的屏幕多边形，绘制颜色、填充和描边保持原版。

## 视觉规范

- 主背景：近黑蓝灰，高不透明度。
- 次级面板：略亮的冷灰蓝。
- 主文字：接近白色。
- 次要文字：低饱和蓝灰。
- 青色：可编辑、选中和进度填充。
- 橙色：播放头、关键焦点和选中轮廓。
- 边框：低对比细线，选中状态提高亮度。
- 圆角：所有元素统一为 0。
- 顶栏高度为 72，底栏高度为 84；属性行保持 36，栏内控件同步放大。
- 菜单按钮高度 40、底栏按钮高度 42、径向菜单条目高度 46；
  栏内主要文字字号为 15-18px，输入值与操作按钮为 17px。
- 滑条使用 6px 直角轨道、12×26px 方形滑块，填充端带 1px 高光，
  并具备悬停和焦点反馈。

数值和英文使用项目现有 Exo 字体，中文使用系统中文字体回退。
文字必须设置明确的换行或溢出策略，任何控件都不能被文字撑开或覆盖相邻控件。

## 数据流

1. `FalsumChartEditor` 更新状态并调用现有 `FalsumEditorView` 同步方法。
2. `FalsumEditorView` 查询 UITK 元素并更新值；程序更新不触发业务回调。
3. 用户操作触发 UITK 事件，控制器转换为现有 C# 事件。
4. `FalsumChartEditor` 执行编辑、播放或保存逻辑。
5. 下一帧控制器把新状态同步回 UITK。

动态模板的动作绑定使用元素名称和控制器闭包。业务逻辑不得进入 UXML
或 USS，也不得在视觉元素中保存编辑模型。

## 输入与命中测试

- 键盘输入继续由现有 `Input` API 和 `FalsumChartEditor` 处理。
- 输入框正在编辑时，通过 `panel.focusController.focusedElement`
  判断并暂停快捷键处理。
- 指针命中通过 `IPanel.Pick` 检查；屏幕坐标先转换为 Panel 坐标。
- 全屏 UITK 根层设置为忽略拾取；选中轮廓 Canvas 保持
  `raycastTarget = false`，不参与 UITK 命中测试。
- 顶栏、底栏、属性面板、菜单、确认框和径向菜单的可见面板阻挡谱面操作。
- 透明根区域命中根元素时视为未命中 UI。

## 错误处理

- 启动时验证 PanelSettings、主 UXML 和 USS 是否存在。
- 任一必需资源缺失时记录包含资源路径的错误，并在 UITK 根层显示诊断标签；
  不允许静默回退到 uGUI。
- 模板实例化后验证必需子元素；缺失时抛出带模板路径和元素名称的
  `InvalidOperationException`。
- 菜单、确认框和径向菜单重复显示前先清理旧实例。
- `OnDestroy` 注销所有 UITK 回调和事件，避免重载场景后重复执行。

## 验证

### 静态检查

- 编译项目并确认无 C# 错误。
- 搜索 FALSUM 交互 UI，确认不存在 `Canvas`、`UnityEngine.UI`
  和 `EventSystem` 依赖。
- 确认选中轮廓仍由原 `FalsumSelectionOverlayGraphic` 和独立 Canvas 绘制。
- 确认模式平面仍由原 `FalsumModePlane` 世界空间 Canvas 绘制。
- 确认所有 UXML/USS 与 PanelSettings 位于 Resources 路径并可加载。

### 编辑器自检

- 扩展 `FalsumSelfCheck`，验证 UITK 资源、关键元素和模板加载。
- 验证属性提交、显示值同步、菜单实例化与径向菜单条目。
- 验证缺失资源诊断路径。

### 运行时与截图

- 运行 `FalsumEditorCapture`，覆盖默认界面、属性面板、径向菜单和播放状态。
- 检查 1152×720、1920×1080 与 4K 下的布局、文字和遮挡。
- 保留现有编辑、拾取、选中几何、Q/Space 播放和滚轮跳转检查。

### 独立包

- 执行 Windows Player 构建。
- 使用 `-falsum-smoketest` 启动独立包，确认 UITK 资源和交互初始化成功。

## 验收标准

- FALSUM 交互 UI 全部由 UI Toolkit 渲染和接收输入。
- 选中轮廓继续使用原版 uGUI `MaskableGraphic` 实现，且不参与输入。
- 所有控件为直角，视觉层级与“精密工具”方向一致。
- 现有编辑、播放、保存、菜单占位和快捷键行为不变。
- 属性面板、菜单、确认框和径向菜单在新旧状态切换时不残留、不重叠。
- 中央谱面在 UI 透明区域仍可正常选择、拖动和放置音符。
- 启动进入制谱模式时歌曲音频和播放时钟都必须保持停止，直到用户主动播放。
- 模式平面保持原版世界空间 Canvas、网格和文字实现。
- 编辑器截图、自检、Windows 构建和独立包冒烟测试通过。
