# FALSUM 手机横屏编辑器 UI 设计（Android）

> 状态：**设计已定稿，未实现任何代码**（2026-11-09）。
> 目标设备：安卓手机，**横屏**，双手握持，拇指操作。基准分辨率 2400×1080（≈900×405 dp）。
> 所有 `path:line` 相对仓库根 `InFalsus-Gameplay/`。
> 事实来源：`docs/superpowers/specs/2026-11-06-falsum-android-feasibility-assessment.md`（性能/平台）、
> `docs/memory/`（不变量与架构）、以及本设计期间的只读 UI 能力盘点（结论见 §2）。

---

## 0. 一句话

**PC 的信息架构一条不拆**（属性面板 / 场景画布 / 点立得 / 事件面板 / 歌曲信息卡 / 库面板全部保留），
手机版做三件事：① 把面板**停靠**到拇指区并放大到 ≥48 dp；② 把「右键径向菜单 + 键盘 A/S/D/F」
换成**左下角四分之一圆技能轮**；③ 把属性面板的**文本框**换成**值 chip + 底部数字键盘**。
实现走「一套 UXML + 两套 USS + 一个移动适配器」，PC 端零行为变化。

---

## 1. 范围

### 1.1 在范围内

| # | 项目 |
|---|---|
| 1 | 横屏手机的分区布局（顶条 / 左栏 / 画布 / 右栏 / 底栏 / 技能轮）与全部尺寸 |
| 2 | 左下角四分之一圆「点立得」技能轮（几何、命中、状态机） |
| 3 | 触控手势全集（轻点 / 拖拽 / 长按 / 双击 / 轮拖动 / 双指四向） |
| 4 | 属性面板的移动端形态（值 chip + 数字键盘，无 Slider、无标签文字） |
| 5 | 移动端判定（`Application.isMobilePlatform`）与 safe-area 折算 |
| 6 | 锚点抓取半径提为偏好项（用户决定「保留锚点、按手感调」） |
| 7 | `FalsumMobileShell` 适配器、`FalsumMobile.uss`、`FalsumWheelDock.uxml` |
| 8 | 自检新增 2 项（移动类开关、轮扇区角度映射） |

### 1.2 明确不做（负向清单）

| 不做 | 理由 |
|---|---|
| **2D 视图编辑** | `ToggleTwoDView()` 全仓 **0 调用点**（只有调试入口 `FalsumChartEditor.cs:910-915`）；2D 下 `ExposeCanvasForTouch` 主动放弃指针（`FalsumChartEditor.TouchGestures.cs:262-266`） |
| **PC 端任何视觉变化** | 「界面不放说明文字」规则只作用在 `.is-mobile` 下（用户决定：不改 PC，只加手机 override） |
| 捏合缩放 / 旋转 / 三指手势 | 现有双指四向（撤销/重做/时间轴前后）已占满；再叠会破坏 `TouchGestures.cs` 的「一次滑动只触发一次」约定 |
| ffmpeg 转码 / 波形切片 | `Process.Start` 在安卓不存在（`FalsumMediaConverter.cs:301-444`） |
| 用户工具（Jint） | IL2CPP 反射剥离风险未证实；沙箱 5 s / 1e6 语句在移动端体验也差 |
| `F11` 全屏 / 打开预览目录 / DPI 对话框 / 内置 74 个包 | 平台语义不存在或包体不可接受 |
| **存储层 / 文件选择器** | 见 §10：独立前置项，单独一份 spec，不塞进本文 |

---

## 2. 现状事实（设计依据）

这些是从源码读出的既成事实，逐条决定了下文某个设计选择。

### 2.1 放置是一个显式互斥的模式机，不是「点空白就放」

`EditorMode`（`FalsumChartEditor.cs:25-36`）：
`Select / SegmentSelect / ToolNotePick / PointPlace / PlaceTap / PlaceHold / PlaceFlick / PlaceSkyArea / PastePreview`。

- 字段 `_mode`（`:157`），启动置 `Select`（`:387`）。
- 进入放置模式的**唯一两个来源**：
  1. 右键径向菜单 → `HandleRadialAction` 的 `PlaceTap/PlaceHold/PlaceFlick/PlaceSkyArea` 分支（`:9785-9804`）；
  2. 键盘 `A/S/D/F`（`:8431-8453`）→ `SetPointPlaceMode`（`:9841`）。
- 左键按下的分派：`HandleLeftDown` → `:8709-8728` → `PlaceTap()` / `HandleHoldDown()` / `HandleFlickDown()` / `HandleSkyDown()`。
- `PointPlace` 是「已进放置但未选类型」的待命态（不创建物件）。

**结论**：技能轮只需要一个调用 `SetPointPlaceMode` 的触控入口，**放置逻辑一行不用改**。

### 2.2 「王者放技能」这个交互在 PC 上已经实现

- 打开：`_mode != PastePreview && Input.GetMouseButtonDown(1) && !_radial.IsOpen && !_view.IsPointerOverUi(...)` → `ShowRadial`（`:8617-8623`）。
- 选中：右键**抬起**时 `_radial.SelectAt(Input.mousePosition)`（`:8624-8627`）。
- 命中：`SelectAt` 用 `worldBound.Contains(panelPoint)`（`FalsumRadialMenu.cs:230-255`）；未命中任何条目则 `Hide()` 不触发动作（`:253-254`）。
- 内容三态：有选中 → `ShowSelection`（Copy/Cut/Mirror/MirrorFrontBack，`:191-214`）；放置模式 → `ShowPlacement`（不创建/TAP/HOLD/FLICK/SKYAREA/网格/退出，`:153-189`）；否则 → `ShowRoot`（段选/点立得/网格 + 剪贴板非空时粘贴，`:119-151`）。

**结论**：手机技能轮 = 复用同一台「按下-移动-抬起」选择机，只换 ① 锚点（鼠标位置 → 左下角固定点）② 布局（环形 → 90° 四等分）。

### 2.3 FLICK 的黄/绿不需要单独选

`FalsumChartEditor.cs:9459`：`FlickDirection = _flickStart > _flickCurrent ? 16 : 4`（16 = 黄 / 左，4 = 绿 / 右）。
→ FLICK 只占**一个**扇区，拖动方向即颜色。符合 `02-invariants.md` §A「flick 方向只有 4 和 16」。

### 2.4 SKYAREA 与 HOLD 是「多次交互」

- SKYAREA：`_skyStage` 0 → 1 → 2（`HandleSkyDown` `:9469-9527`、`CommitSkyDrag` `:9529-9563`、`FinalizeSkyStart` `:9565-9580`、`FinalizeSkyEnd` `:9582`）。
- HOLD：两次点击定头/尾（`HandleHoldDown` `:9312`）。
- 两者的阶段提示今天靠 `_persistentHint` 一行文字（`falsum.hint.skyArea.*`、`falsum.hint.hold.*`，`zh-CN.json:791-793, 816-819`）。

### 2.5 宽度与分母在 PC 上都是键盘

- 宽度（仅 TAP/HOLD）：数字键 `1/2/3/4` → `ReadTapWidth()`（`:13550-13573`）→ `ApplyActivePlacementWidth`（`:13575-13589`），分派在 `:8478-8487`。
- 横向分母（FLICK/SKYAREA）：`-` / `=` → `Decrease/IncreaseFlickDenominator`（`:8570-8582`），中键编辑进行中由 `AdjustMiddleEditGrid(±1)` 接管（`:8555-8568`），共用阶梯 `Grid.DenominatorSequence`（`FalsumPreferences.cs:205-213`），上限 `MaxGridDenominator=192`（`FalsumBeatGrid.cs:32`）。

### 2.6 属性面板里一个 Slider 都没有

`ShowProperties`（`FalsumEditorView.cs:3803-3970`）构造的行全部是**文本框 / 下拉按钮 / 两段按钮**：

| 类别 | 字段 |
|---|---|
| TAP（`:3822-3836`） | `time_ms`、`width_lanes`、`start_lane` |
| HOLD（`:3837-3855`） | start time、`end_time_ms`、`width_lanes`、`start_lane` |
| FLICK（`:3856-3892`） | `time_ms`、`flick_position`（分数文本 "n/cells"）、`flick_width`（分数）、`flick_direction`（两段按钮 `:7329-7372`） |
| SKYAREA（`:3893-3963`） | start/end time、`sky_start`、`sky_start_width`、`sky_end`、`sky_end_width`（四个分数文本）、`left_ease`/`right_ease`（下拉 `:7423-7456`） |

- 面板浮动定位：`left 16 / top 72 / width **320 px** / min-h 220 / max-h 70%`（`FalsumEditor.uss:749-764`）。
- 行高公式 `86 + row*36 + 44`，夹 220..600（`:3966-3969`）。

**结论**：这是**唯一无法靠 USS 覆盖解决**的硬结构问题——必须把「文本框」换成触控友好的控件，并把面板从浮动改成停靠。

### 2.7 触控基建几乎为零

| 项 | 现状 |
|---|---|
| `Application.isMobilePlatform` | **0 命中**（全 Assets 扫描） |
| `Screen.safeArea` | **0 命中** |
| `Touch` / `Input.GetTouch` | 仅 `FalsumChartEditor.TouchGestures.cs:90-104` |
| 已实现手势 | 4 条双指：左滑撤销 / 右滑重做 / 上滑时间轴后 / 下滑时间轴前（`TouchGestures.cs:11-27, 154-226`） |
| 阈值 | `TwoFingerSwipeFraction=0.08`（` :44`）、`TwoFingerAxisDominance=1.6`（`:51`）、滚动节流 `0.06 s`（`:81`） |

**结论**：移动端判定与 safe-area 是必须新写的地基。

### 2.8 世界空间 UI 不抢事件（对手机是利好）

- 选中轮廓 + 拖拽把手：世界空间 `LineRenderer`（`FalsumEditorWorld.cs:5078-5105`），把手描边由屏幕空间 `FalsumSelectionOverlayGraphic` 画，`raycastTarget=false`（`:5128`）；Canvas sortingOrder **100**（`FalsumChartEditor.cs:13880-13899`）。
- 模式平面：世界空间 Canvas（`FalsumModePlane.cs:117-132`），文字走 `FalsumWorldTextOverlay`，`raycastTarget=false`（`:227`/`:244`）。
- GRID 标尺：世界空间 mesh（`FalsumEditorWorld.cs:883-919`）。
- 命中靠**纯数学**：`FindAnchorAt(screenPos, 14px)`（`FalsumEditorWorld.cs:409`）、`AnchorPickRadiusPixels=14.0f`（`AnchorEdit.cs:37`）、悬停 ×1.45（`:43`）、音符 `TryPickAll` + `PointInPolygon`（`:1441-1490`）、拾取内边距 `clamp(Screen.height*0.0025, 3, 7)` 屏幕像素（`:1593-1608`）。
- 唯一的「UI 挡住画布」判据：`IsPointerOverUi`（`FalsumEditorView.cs:4704-4721`，`panel.Pick` 后向上找 `pickingMode==Position` 的祖先）。

**结论**：手势可直接作用在 3D 画布上，不需要重建命中体系；保留锚点的额外成本只有「把 14 px 提成偏好项」。

### 2.9 触屏真正不可达的 9 个动作（能力都在，缺触发点）

`FalsumInputAction` 实为 **26 项**（`FalsumPreferences.cs:26-57`，默认键位 `:769-797`）。

| 动作 | 现仅 | 本文档的触控入口 |
|---|---|---|
| `DeleteSelection` | `Delete` 键（`:8534-8539`） | 长按径向菜单 + 右栏 ✕ |
| `MiddleEditBoundary/StartEnd/Position` | `F1/F2/F3`（`:8363-8380`） | 左栏三枚互斥图标（见 §5.4） |
| `PointPlaceTap/Hold/Flick/SkyArea` | 径向菜单 + `A/S/D/F` | **技能轮四扇区** |
| `SegmentSelect` | `Ctrl+R` + 径向菜单 | 长按径向菜单条目 |

其余 17 项已有可点入口（底部按钮 / 菜单项 / 顶栏滑块 / 库面板按钮），沿用。

### 2.10 现有尺寸与断点（为什么不能直接放大）

| 对象 | 现状 | 手机上的实际 |
|---|---|---|
| `.bar-button` | 64×34 px（`FalsumEditor.uss:532-543`） | ≈ 16 dp 高 |
| `.menu-item-button` | min-height 24 px（`:1973-1987`） | ≈ 11 dp |
| `.property-field` | height 26 px（`:847-856`） | ≈ 12 dp |
| 顶栏 / 底栏 | 56 / 64 px（`:201-215`） | 可接受 |
| PanelSettings | `ScaleWithScreenSize` / 参考 **1730×1080** / Match **0.5**（`FalsumPanelSettings.asset:21,27-29`） | — |
| 四档断点 | `CompactScreenWidth=1500`、`CompactScreenHeight=840`、`NarrowScreenWidth=900`、`ShortScreenHeight=700`、`LowScaleThreshold=0.82`（`FalsumEditorView.cs:94-98`） | 1080p 手机横屏（2400×1080）**三档全不命中** |
| 判据 | `physicalScale = Screen.height / _root.layout.height`（`:5745-5755`），`compact = w<=1500 \|\| h<=840 \|\| physicalScale<0.82` | 手机 `physicalScale ≈ 1.178` → `.is-low-scale` **永不命中** |

**结论**：断点判据用错了维度（窗口宽 vs 物理 DPI），必须新增一层「按平台/DPI 而非窗口宽度」的判定。

---

## 3. 架构

### 3.1 三方案对比（已选定 B）

| 方案 | 新增 C# | 改 UXML | 改 USS | 面板宽度 | 扇形轮 | PC 回归风险 |
|---|---|---|---|---|---|---|
| A · 纯 USS 覆盖 | 0 | 0 | 1 份 | ✗ 解决不了 | ✗ 勉强 | 极低 |
| **B · 一套 UXML + 两套 USS + 适配器** | **1（~400 行）** | ~20 行 | 1 份 | ✓ | ✓ | 低 |
| C · 独立移动端 UXML | ~2（+ 抽接口） | 新写 158 行 | 新写 | ✓ | ✓ | 中 |

选 **B**，三条理由都与既有约束有关：

1. `02-invariants.md` §C-4「View 不持有 Editor」：适配器挂在 View 内部只对 `_root` 操作，**不碰 Editor**，边界不动。
2. `FalsumRadialMenu.ShowPlacement/SelectAt` 已是完整选择机（`FalsumRadialMenu.cs:153-189, 230-255`），B 只加**布局参数**，命中逻辑一行不改。
3. 属性面板 320 px 固定宽（`FalsumEditor.uss:749-764`）是唯一 CSS 覆盖不了的硬结构；B 里它是把浮动元素从 `absolute` 改成停靠（`relative` + 固定宽），靠 UXML 容器名找到父节点。

### 3.2 组件划分

```
FalsumEditorView (7878 行, 不动数据流)
  ├─ [新] FalsumMobileShell            ← 唯一的移动端适配器
  │    ├─ IsMobile / ApplyClasses      → root 加 .is-mobile，摘/挂 FalsumMobile.uss
  │    ├─ ApplySafeArea                → Screen.safeArea 折算 root padding
  │    ├─ DockPanels                    → NoteProperties/EventsPanel 从浮动改停靠
  │    └─ WheelGeometry                 → 给 FalsumRadialMenu 供 {anchor, radius, sectors}
  ├─ [改] FalsumRadialMenu             ← 新增 LayoutMode {Ring, Quarter}，命中逻辑不变
  ├─ [改] FalsumEditorView             ← 值chip + 数字键盘弹层（仅 .is-mobile）
  └─ [改] FalsumChartEditor.TouchGestures ← 长按/双击/轮拖动/按住锚点
```

每个单元的自洽性：

- **`FalsumMobileShell`**：只做「把 PC 布局改造成手机布局」，输入 `(root, isMobile)`，输出无（副作用直接作用在 `root`）。不产生事件、不持有 Editor、不碰谱面数据。可以单独用截图自检验证。
- **`FalsumRadialMenu` 的 Quarter 模式**：输入 `(anchorScreenPos, radius, sectorCount)`，输出与原 Ring 模式**同一套** `FalsumRadialAction` 回调。调用方（`HandleRadialAction`）无感知。
- **数字键盘弹层**：输入 `(label, currentText, allowFraction)`，输出 `string`（已过 `SanitizeNumericInput`）。不直接写谱面——写回仍走原有 `SetProperty`（`FalsumChartEditor.cs:11090-11278`）。

---

## 4. 布局

参考 2400×1080 横屏 ≈ **900×405 dp**。

```
┌──────────────────────────────────────────────────────────────────────┐
│ ≡  (40dp)                                                    ⚙        │  顶条
├──────────┬───────────────────────────────────────────────┬───────────┤
│ 属性面板 │                                               │ 右栏      │
│ (可滚动) │            场景画布（flex）                    │ 88dp      │
│ 200dp    │                                               │ 5×48dp    │
│ 值chip   │   ↑ 时间向上（保留原版视觉语言）               │ 工具按钮  │
│ 宽度段选 │                                               │           │
│ 精修三模 │                                               │           │
│ 锚点半径 │                                               │           │
│──────────┤                                               │           │
│ ⊕ 技能轮 │                                               │           │
│  80dp    │                                               │           │
├──────────┴───────────────────────────────────────────────┴───────────┤
│ ↶ ↷ ▶ ───────进度─────── 01:09.750  1.0x ▤ SAVE          (56dp)      │  底栏
└──────────────────────────────────────────────────────────────────────┘
```

技能轮的定位规则（唯一，不留第二种解释）：**它在左栏内部，占左栏底部固定 80 dp 的带状区**
（68 dp 圆 + 6 dp 上下留白），圆心距左边缘与距底栏上沿各 40 dp。
所以「屏幕左下角」在横屏下成立的前提是左栏可见；左栏收起时（§4 允许），
技能轮**保持原位**并改为浮在画布左下角（`position: absolute`，同样 80 dp 带）。

| 区 | 尺寸 | PC 对应物 | 行为约束 |
|---|---|---|---|
| 顶条 | **40 dp** | PC 顶栏 56 px 内塞 5 菜单 + Flow 滑块 + BPM + 三行中键状态 | 整条重做：`≡` 折叠五组菜单进全屏列表，`⚙` 偏好。**不显示** `MiddleEditStatus` 三行文字 |
| 左栏 | **200 dp** | `NoteProperties`（原 320 px 浮动） | 可收起为 0，**不可拖拽改宽**（固定值便于验证） |
| 画布 | flex | 3D 谱面世界（不变） | 手势区；进手势前先过 `IsPointerOverUi` |
| 右栏 | **88 dp** | 选择 / 网格 / 镜像 / SKY 库 / 分母 | 单列，**不滚动**，每枚 ≥48 dp |
| 底栏 | **56 dp** | 撤销 / 重做 / 播放 / 进度 / 时间 / 变速 / 频谱 / SAVE | PC 底栏 9 项全保留 |
| 技能轮 | **68 → 216 dp** | 点立得四类 | 锚在左栏底部 80 dp 带状区（左栏收起时浮在画布左下角） |
| 浮层 | 全屏 | `EventsPanel` / `SongInfoCard` / `SkyAreaBank` / 工具对话框 / 偏好 | 从浮动改全屏覆盖；`MenuLayer`/`ModalLayer`/`RadialLayer` 层级不变 |

左栏竖向空间分配（309 dp 可用高，扣除技能轮占用后 200 dp 可排内容）：

| 段 | 高 | 内容 |
|---|---|---|
| 选中 chip | 24 dp | `L3 · 01:09.750  w1` |
| 值 chip 组 | 4 × 26 dp | `▭ ⌒ ▤ ⌛` |
| 宽度段选 | 28 dp | `1 2 3 4`（仅 TAP/HOLD 显示） |
| 精修三模 | 30 dp | `⌗ ↔ ◎` |
| 锚点半径 | 20 dp | 滑块 + 数值 |
| 余 | ~40 dp | 溢出时整段纵向滚动（`ScrollView`，非挤压） |

> 注：SKYAREA 选中时值 chip 组会涨到 6 行（起止时间 + 四个分数），此时该段进滚动，
> **不压缩其他段的高度**——触控尺寸优先于「一屏看全」。

### 4.1 为什么时间轴仍是纵向

横屏宽高比 2.2:1 会诱使人做成 DAW 式横向时间轴；**否决**，因为：

1. 原版视觉语言是「音符从下往上」，横向化等于重学音形与判定线。
2. `FalsumEditorWorld` 的透视投影、`NoteZAt`、`GridReferenceScreenHeight=1080`（`FalsumEditorWorld.cs:161`）全部按纵向判定线建；横向化是渲染层重写，不是 UI 改动。
3. 横向唯一的好处是可见时间跨度，而这一点由双指上下滑 + 频谱解决。

---

## 5. 交互

### 5.1 技能轮几何

| 参数 | 值 | 推导 |
|---|---|---|
| 闭合态直径 | **68 dp** | ≥48 dp 触控下限 + 拇指舒适余量 |
| 展开半径 | **216 dp** | 左上最远点仍满足 `405 − 216 = 189 ≥ 0` |
| 扇数 | **4 × 22.5°** | 弧长 `216 × 0.3927 = 84.8 dp` ≥ 48 dp |
| 死区 | `r < 68 dp` | 圆内松手 = 取消 |
| 命中 | `atan2(-dy, dx)` → 扇区索引 | 边界 0 / 22.5 / 45 / 67.5 / 90°；**落在边界取较小序号** |
| 扇区内容 | `0°–22.5` TAP · `22.5–45` HOLD · `45–67.5` FLICK · `67.5–90` SKY | 顺序与原 `ShowPlacement` 条目顺序一致（`:153-189`） |

状态机：

```
Idle ──按下（>150 ms 内不动）──▶ Armed ──移动越过死区──▶ Aiming ──松手──▶ Commit
 │                                  │                              │
 └──轻点（<150 ms，不动）──▶ SelectTool ◀───────────────────────────┘
                                  松手在死区内 = Cancel（回 Idle）
```

- `Commit` → `HandleRadialAction(FalsumRadialAction.PlaceTap/Hold/Flick/SkyArea)`（现有 `:9785-9804`）。
- `SelectTool` → `SetMode(EditorMode.Select)`（等价 `ExitPlacement`，`:9823-9826`）。
- **不新增** `FalsumRadialAction` 枚举值：四个扇区一一映射到已有的四个 `Place*`。

### 5.2 手势全集

| 手势 | PC 等价 | 复用点 | 备注 |
|---|---|---|---|
| 轻点画布 | 左键点选 | `TryPickAll`（`FalsumEditorWorld.cs:1441-1490`）+ `PickPaddingCanvasUnits`（`:1593-1608`） | 命中半径沿用，不改 |
| 拖拽已选 | 左键拖拽 | `DragSelection`，8 px 阈值 `SelectionDragThresholdPixels`（`FalsumChartEditor.cs:79`） | 不变 |
| **长按 250 ms** | 右键（按下-移动-抬起） | `ShowRadial`（`:8617`）+ `SelectAt`（`FalsumRadialMenu.cs:230-255`） | 三态（有选中/放置中/默认）全部保留 |
| 双击 | 双击切缓动 | `SkyMiddleDoubleClickSeconds=0.32`（`MiddleEdit.cs:84`） | 阈值不改 |
| **轮上按住拖** | `A/S/D/F` | `SetPointPlaceMode`（`:9841`） | — |
| 长按音符 → 拖动 | 中键三模式 | `BeginMiddleEdit(screenPos)` | 抓取半径见 §5.4 |
| 双指左/右滑 | `Ctrl+Z` / `Ctrl+Y` | `TouchGestures.cs:165-182` | **不动** |
| 双指上/下滑 | 滚轮 | `TouchGestures.cs:199-226` | **不动**；`IsTwoFingerGestureActive` 时吞掉本帧（§A-21） |

长按与放置模式共存规则（避免打架）：

- `_mode ∈ {Select, SegmentSelect}` → 长按 = 弹径向菜单。
- `_mode ∈ {PointPlace, PlaceTap, PlaceHold, PlaceFlick, PlaceSkyArea}` → 长按 = 弹 `ShowPlacement`（换类型 / 退出），**不弹 Root**。这与 PC 的 `ShowRadial` 分支（`_mode` 判定）一致。

### 5.3 SKYAREA 与 HOLD：阶段胶囊

用户决定 **SKYAREA 原样搬三段式**。三段语义一字不改，只把承载从「一行 `_persistentHint` 文字」升级成**常驻阶段胶囊**。

```
┌──────────────────────────────────────────────┐
│ ①▰ ②▱ ③▱   [✕]                              │   ← 画布底部胶囊条
└──────────────────────────────────────────────┘
   ① 起始边（按住拖，松开确认）
   ② 结束时间（轻点）
   ③ 终止边（按住拖，松开完成）
```

- 复用文案 key：`falsum.hint.skyArea.drawStart` / `chooseEndPoint` / `drawEnd`（`zh-CN.json:816-819`）。
- 胶囊是**图标态**（①▰ ②▱ ③▱），不放完整句子——`falsum.hint.*` 的句子只在 tooltip 出现。
- `[✕]` → 放弃当前半成品（现有 `CommitSkyDrag` 的早退路径 + 清 `_skyStage`）。
- HOLD 复用同一组件：`① 点起点 → ② 点终点`。

### 5.4 中键三模式 → 左栏三枚互斥图标（含锚点保留）

PC 三模式的语义（`MiddleEdit.cs`，切换键 `F1/F2/F3` → `SetMiddleEditMode` `:8363-8380`，拖拽中禁止切换 `:1467-1474`）：

| 模式 | 控制什么 | 精度需求 |
|---|---|---|
| **Boundary** | 改边界位置 / 宽度 | 整数 lane（≈1/6 轨宽）或 1 cell |
| **StartEnd** | 改起止时间 | 1 cell |
| **Position** | 整体平移 pos | 1 cell |

手机形态：

- 左栏三枚互斥图标 `⌗ ↔ ◎`（48 dp 方块，同排）。
- 进入：**长按音符** → `BeginMiddleEdit(screenPos)`（现有函数接受任意点，无需改签名）。
- **锚点保留**（用户决定）：`AnchorPickRadiusPixels`（现值 `14.0f`，`AnchorEdit.cs:37`）提为偏好项，范围 **12–40 px**。
  **默认值不变**：PC（`.is-mobile` 未挂）仍是 14；手机端默认 **24**。两者由 `FalsumMobileShell` 在挂类时写入初始值，
  用户之后在左栏滑块（12–40）自行调，落到 `FalsumPreferences` 持久化。
- 悬停放大倍率 `AnchorHoverRadiusScale=1.45`（`:43`）在触屏上无「悬停」，改为「按下时立刻按 1.45 判定」，即**抓取半径 = 偏好值 × 1.45**。

### 5.5 属性编辑：值 chip + 数字键盘

```
[▭ 1] [⌒ 0] [▤ 3] [⌛ 0.000]        ← 值 chip，点中即弹键盘
────────────────────────────
1 2 3  ⌫   ±   (秒:分)
4 5 6  ↵
7 8 9   [  确认  ]
```

- 每枚键 **≥48 dp**；数字键盘从底部弹起，占 ~160 dp 高（画布仍可见上半）。
- 输入**必须**过 `FalsumEditorView.SanitizeNumericInput(string, bool allowFraction)`（`02-invariants.md` §C-2）。
- 写回仍走 `SetProperty`（`FalsumChartEditor.cs:11090-11278`）：`width_lanes` 夹 1..4（`:11142-11151`）、flick 方向只收 16/4（`:11182-11190`）。
- **不用 IME**：横屏半屏软键盘会把画布全挡，且 `FalsumImePosition` 在非 Win 直接 `return false`（`FalsumImePosition.cs:176-183`，安全降级）。
- 缓动：`⇤ / ⇥` 两枚按钮轮流切 `0 → 1 → 2`（等价 `AddEaseDropdown`（`:7423-7456`），显示名走 `EaseName`（`:7690-7701`）：0=Linear / 1=EaseOut / 2=EaseIn）。
- 宽度：`1 2 3 4` 四枚段选按钮（等价 `ReadTapWidth`）。
- flick 位置/宽度：显示为 `n/cells` 分数文本，点击进入「分子 + 分母」两段式键盘。

### 5.6 「界面不放说明文字」规则（仅 `.is-mobile`）

可见文本只允许三类：

1. **数值**（时间、BPM、分母、宽度、半径）
2. **专有名词**（TAP / HOLD / FLICK / SKYAREA / MIN / EVO / ULT / FBD / SAVE / 菜单名）
3. **状态**（开 / 关、档位、当前难度）

一律不进界面：`falsum.hint.*` 的操作说明句（`zh-CN.json:803-809, 816-819`）、面板标题行（「音符属性 / NOTE PROPERTIES」）、字段标签（「时间/轨道/宽度/时长」）。
这些 key **保留**，仅供 tooltip 与偏好页使用；本地化审计不受影响（key 仍在两套 JSON 里）。

实现：`.is-mobile` 下 `.field-label`、`.property-eyebrow`、`#PropertyHeader`、`.middle-edit-status` 一律 `display: none`。**PC 端选择器优先级不变**，故 PC 视觉零变化。

---

## 6. 数据流

```
GameplayBootstrap.Start
  └─ FalsumChartEditor.Initialize
       └─ FalsumEditorView.Build()                    ← 现状：不变
            ├─ Resources.Load PanelSettings / UXML    （FalsumEditorView.cs:895-907）
            ├─ LoadTemplates / QueryMainElements / L.ApplyStaticText …
            ├─ UpdateResponsiveClasses                （:5745-5760）
            └─ [新] FalsumMobileShell.Attach(_root)    ← 唯一插入点
                 ├─ if (!IsMobile) return;            ← PC 走老路径，逐帧不变
                 ├─ root.AddToClassList("is-mobile")
                 ├─ StyleSheet 挂 FalsumMobile.uss
                 ├─ ApplySafeArea(Screen.safeArea)
                 ├─ DockPanels(NoteProperties, EventsPanel)
                 ├─ radial.SetLayoutMode(Quarter, anchor, 216dp)
                 └─ QueryMainElements 之后才执行（元素必须已存在）
```

三个方向都不新增契约：

- **View → Editor**：仍只有 40+ `public event`（`FalsumEditorView.cs:382-434`），适配器**不新增事件**。技能轮复用现有 `RadialActionRequested`。
- **Editor → View**：仍只有 `_view` 门面的 `Set*`。
- **数据**：适配器不接触 `_session` / `_chart` / `_package`。

时序约束：`Attach` 必须在 `QueryMainElements()`（`:5498-5559` 的 `Require<T>` 序列）**之后**调用，否则 `Require<T>` 抛 `falsum.error.ui.missingTypedElement`（`:7673-7688`）。

---

## 7. 错误处理

一以贯之的原则：**移动端适配失败必须降级成「PC 布局」，绝不能让编辑器起不来。**

| 情形 | 处理 |
|---|---|
| `Resources.Load<PanelSettings>` 失败 | 现状已有回退 `CreateRuntimePanelSettings()`（`:903`），不改 |
| `FalsumMobile.uss` 缺失 | `ApplyClasses` 打警告并**继续**（只有样式退化，不抛）。警告 DSL：`L.Get("falsum.error.ui.mobileStyleMissing")` |
| 新 UXML 容器名缺失（`PropertyDock` 等） | `FalsumMobileShell` 用 `Q<VisualElement>`（**不抛**），缺哪个就跳过对应停靠，其余照常 |
| `Screen.safeArea` 返回全屏矩形（部分桌面设备/编辑器） | 计算结果为 0 padding，等同不处理 |
| `Screen.safeArea` 可用高 < 320 dp | 不折叠左栏（折叠会让技能轮移位）；改为降低展开半径至 `min(216, 可用高 - 68)`，并把左栏内容段改为滚动。**唯一触发折叠的条件是用户手动收起左栏** |
| 技能轮展开后超出屏幕 | 半径按 `min(216, min(anchorX, anchorY) - 68)` 二次夹取（`anchorX`/`anchorY` 为圆心到左边/下边的 dp 距离） |
| 长按与双指手势同帧 | `IsTwoFingerGestureActive` 优先，吞掉本帧（§A-21 既有语义） |
| 数字键盘弹出后系统返回键 | 关闭键盘，**不**退出编辑器 |
| `_mode` 处于 `PastePreview` 时长按 | 不弹菜单（与 PC 的 `GetMouseButtonDown(1)` 前置条件一致 `:8617-8623`） |

新增 2 条本地化 key（`zh-CN` / `en-US` 各一份，否则审计失败）：

- `falsum.error.ui.mobileStyleMissing`
- `falsum.preferences.anchorPickRadius`

---

## 8. 测试

### 8.1 自检（`FalsumSelfCheck`）

新增 2 项，与现有 31 项并列：

| 项 | 断言 |
|---|---|
| `CheckMobileShell` | ① `IsMobile=false` 时 `FalsumMobileShell.Attach` **不**加 `.is-mobile`、**不**挂 USS（PC 零变化）；② `IsMobile=true` 时四条 class 齐全；③ 容器名缺失时不抛异常 |
| `CheckWheelSectorMapping` | 360 个采样角（每 1°）映射到 4 扇区，期望分布为 22/23/22/23；边界 0/22.5/45/67.5/90° 落在较小序号；半径 <68 一律 `Cancel` |

### 8.2 现有门禁（顺序照 `02-invariants.md` §D-5）

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
& $unity -batchmode -quit -projectPath . -logFile Diagnostics\UnityCompile.log
& $unity -batchmode -quit -projectPath . -logFile Diagnostics\UnityFalsumSelfCheck.log -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run
python Tools\falsum_localization_audit.py
```

### 8.3 界面验收（截图 / 手工）

1. **触控尺寸**：所有可点元素 ≥48 dp；用 `Screen.width/2160` 换算实测。
2. **轮几何**：4 扇区可见、边界拖过 22.5° 时高亮切换无跳变。
3. **safe-area**：在刘海/挖孔设备上，顶条与底栏控件均完整可见。当前 `androidRenderOutsideSafeArea=1`（`ProjectSettings.asset:74`）需与 safe-area 折算**同时**处理，二选一不能只做一半。
4. **PC 回归**：同机跑 PC 构建，`git diff` 逐像素比对 `Diagnostics/` 下既有截图基线。

### 8.4 不做真机帧率承诺

性能另见 `2026-11-06-falsum-android-feasibility-assessment.md` §4。本文档只保证**界面层**；`b3.shader` GLES3 可移植性与天空区每帧重建仍在「未实测」清单里。

### 8.5 分阶段验收（本设计可独立于存储层落地）

| 阶段 | 内容 | 退出判据 |
|---|---|---|
| **P1** | `FalsumMobileShell` + `FalsumMobile.uss` + 容器名：在**PC 上把窗口缩到 900×405** 即可验证分区、停靠、48 dp、safe-area=0 | 截图对比通过；PC 全尺寸下 `git diff` 无像素变化 |
| **P2** | 技能轮 Quarter 模式 + 手势（长按/双击/轮拖动） | 桌面用鼠标模拟长按也能跑通（`Input.GetMouseButtonDown` 同路径）；自检 `CheckWheelSectorMapping` 过 |
| **P3** | 值 chip + 数字键盘 + 锚点半径偏好 | 四类音符全部字段可编辑并写回；`CheckMobileShell` 过 |
| **P4** | 真机：safe-area、触控尺寸实测、连续打点手感 | 需设备；依赖存储层（§10）才能测「打开/保存」 |

P1–P3 **不依赖 Android 构建链路**，可在当前 PC 工程完成并全量验证；P4 才需要 Unity Android 模块（可行性评估 §1.1 P1/P2 当前均未满足）。

---

## 9. 改动清单

| # | 文件 | 新/改 | 估量 | 要点 |
|---|---|---|---|---|
| 1 | `Falsum/UI/FalsumMobileShell.cs` | 新 | ~400 行 | 移动判定、class、safe-area、停靠、轮几何参数 |
| 2 | `Resources/Falsum/UI/FalsumMobile.uss` | 新 | ~600 行 | 48 dp 触控尺寸、左 200 / 右 88 / 顶 40 / 底 56、去标签、值 chip |
| 3 | `Resources/Falsum/UI/FalsumWheelDock.uxml` | 新 | ~40 行 | 四分之一圆 + 四扇区 + 中心按钮 |
| 4 | `Resources/Falsum/UI/FalsumEditor.uxml` | 改 | ~20 行 | 加容器名 `TopStrip` / `PropertyDock` / `ToolRail` / `WheelDock` / `StageCapsule` |
| 5 | `Falsum/UI/FalsumRadialMenu.cs` | 改 | ~120 行 | `LayoutMode {Ring, Quarter}` + 锚点/半径参数；`SelectAt` 不动 |
| 6 | `Falsum/Runtime/FalsumChartEditor.TouchGestures.cs` | 改 | ~150 行 | 长按 250 ms、双击、轮拖动、按住锚点进中键编辑 |
| 7 | `Falsum/Runtime/FalsumPreferences.cs` | 改 | ~30 行 | `AnchorPickRadiusPixels` 持久化（12–40，默认 24） |
| 8 | `Falsum/UI/FalsumEditorView.cs` | 改 | ~250 行 | 数字键盘弹层、值 chip 绑定、移动端隐藏标签 |
| 9 | `Assets/InFalsus/Editor/FalsumSelfCheck.cs` | 改 | ~120 行 | 新增 2 项断言 |
| 10 | `Resources/Falsum/Localization/{zh-CN,en-US}.json` | 改 | +2 key | 见 §7 |

不改：`FalsumChartEditor.cs`（12565 行主文件）、`FalsumEditorWorld.cs`、`Falsum2DWorld.cs`、`FalsumModePlane.cs`、`ChartWriter/ChartParser`、任何渲染/音频代码。

---

## 10. 前置阻塞项（本文档之外）

**`FalsumFileDialog` 在非编辑器平台全线不可用**：

`FalsumFileDialog.cs:126-158` 的 `#else` 分支**六个入口全部 `return string.Empty`**（`OpenPackage` / `SavePackage` / `OpenAudio` / `OpenImage` / `OpenDirectory` 等）。
→ Android 上**打开/保存 `.falsum` 完全不可用**。

这与用户定位「移动端完整创作入口」直接冲突，但它属于**存储抽象层**，不属于 UI 设计：

| 抽象 | 现在 | 应改为 |
|---|---|---|
| 数据根 | `GetDirectoryName(Application.dataPath)`（`FalsumRuntimePaths.cs:40-42`） | `IFalsumStorage`：编辑器/PC = 程序目录，安卓 = `persistentDataPath` |
| 内置资源读 | 9 处 `File.*` on `streamingAssetsPath` | 统一 `ReadBundledAsync`（安卓走 `UnityWebRequest`，首启拷到 `persistentDataPath`） |
| 文件选择 | 返回空串 | SAF / 内置「最近谱面」列表 |
| 音频后端 | BASS x86_64 `Assets/Plugins/x86_64/*.dll` | `libbass*.so`，或首次就跑 managed fallback |
| 媒体转换 | `ffmpeg.exe` + `Process.Start` | 砍掉（本文档 §1.2 已列） |

**建议**：单独一份 `2026-11-09-falsum-mobile-storage-design.md`，先于本设计的实现落地。UI 层可以先在 PC 上把 `.is-mobile` 全部跑通（用桌面模拟 900×405 窗口），存储层并行做。

---

## 11. 实现状态（2026-11-09）

### 11.1 已落地（commit `3cdc0ba`）

| # | 文件 | 状态 |
|---|---|---|
| 1 | `Falsum/UI/FalsumMobileShell.cs`（新） | ✅ 移动判定、safe-area、面板停靠、轮几何与命中、工具列绑定 |
| 2 | `Resources/Falsum/UI/FalsumMobile.uss`（新） | ✅ 分区尺寸、≥48 dp 触控、去标签、轮扇形、数字键盘样式 |
| 3 | 移动端 chrome 进 `FalsumEditor.uxml` | ✅ `PropertyDock` / `ToolRail` / `WheelDock` / `StageCapsule` |
| 4 | `FalsumEditorView` 接入 | ✅ `Build()` 末尾 `Attach`、`WireMobileShell`、字形文案绑定 |
| 5 | `FalsumChartEditor` 接入 | ✅ `Wheel → HandleRadialAction`，复用桌面同一入口 |
| 6 | 本地化 | ✅ +11 key（两语言各 1098 条），`falsum_localization_audit.py` 通过 |
| 7 | `FalsumSelfCheck.CheckMobileShell` | ✅ 角度映射 / 边界归属 / 几何 / 资源 / 键完整性 |
| 8 | `Tools/compile_check.sh` + `build_compile_rsp.py` | ✅ 离线编译门禁（见 11.3） |

### 11.2 实现中发现并修正的五个设计错误

**（一）panel 单位不是 dp —— 原 §4 的尺寸会全部缩小到一半。**

`FalsumPanelSettings.asset:27` 把参考分辨率钉在 **1730×1080**。在 2400×1080 手机上，
1 panel 单位 ≈ 0.47 dp，所以 USS 里写 `68px` 实际渲染 ≈ 32 dp，**远低于 48 dp 下限**。
这正是 §2.10 记的「断点用错了维度」那个坑的另一面：不只是断点判据，连长度单位都错。

修法：手机文档**单独一套 PanelSettings**。桌面路径不碰同一个 asset（`FalsumSelfCheck`
断言它必须是 1730×1080）。

**（一 b）参考分辨率不能写死 —— 1080p 不等于固定 dp。**

第一版写死 `900×405`，只对 400 dpi 的面板恰好准确。同一块 1080p 屏按 dpi 换算出的
dp 高度在 **360–460** 之间，写死的参考会让 48 dp 的触控目标落在 42–54 dp。
修法：`ResolveMobileReferenceResolution()` 用 `Screen.width/height × 160 / Screen.dpi`
算出**本机真实 dp 尺寸**并作为参考分辨率，使 1 panel 单位 ≡ 1 dp。设计比例按用户要求
取 **19:9**（1080 高对应 2280 宽，参考 855×405）。桌面验证（`FALSUM_MOBILE_SHELL=1`）
没有真实 dpi，回退到设计值。

**（二）USS 不支持 `transform: rotate(...)`。**

那是 Web CSS 语法。Unity 只认独立的 `rotate:` / `translate:` / `scale:` 属性。
第一版用简写，全部声明被静默丢弃，四个扇区叠成一片。

**（三）USS 里画不出真正的四分之一圆。**

UI Toolkit 没有 `clip-path`，`overflow: hidden` 也不会把子元素裁到 `border-radius` 里。
所以「旋转的楔形 + 圆角裁切」这条路走不通（截图证实渲染成了一个填充矩形）。

**最终几何**：沿用本仓库**已经验证可行**的做法 —— `FalsumRadialMenu.CreateEntry`
就是把条目按角度摆到圆周上、不依赖任何裁切。四分之一圆改成**四个 64×48 dp 按钮摆在
半径 168 dp 的 90° 弧上**，角度取各扇区平分线 11.25° / 33.75° / 56.25° / 78.75°。
相邻中心距 `2×168×sin(11.25°) = 65.6 dp`（> 48 dp 触控间距），最外沿 200 dp 落在
216 dp 的 dock 内。命中判定仍是 `atan2` 扇区映射（`SectorAtDegrees`），与按钮位置同源。

**（四）Button 在 column flex 里会被压成 0 高。**

第一版 `WheelCenter` 落在 `WheelRing` 之后，被 216 dp 高的兄弟挤到 `height: 0`
（诊断输出 `size=(216.2, 0.0)`）——「有布局、有位置、但看不见」。
修法：`flex-shrink: 0` + `min-width/min-height` 双保险。

**（五）顶栏在手机上溢出。**

PC 顶栏把 5 个菜单 + 流速滑块 + BPM 输入 + 三行中键状态挤进 56 px。手机上第一张截图
显示「音频预处理BPM」直接跑出右边缘被裁。修法：`.is-mobile` 下顶栏只留菜单簇
（其余控件在左栏/右栏各有等价入口），并允许菜单按钮收缩。

### 11.3 验证状态（诚实记录缺口）

| 门 | 结果 |
|---|---|
| 编译（`Assembly-CSharp` + `Assembly-CSharp-Editor`） | ✅ **0 error**（Unity 自带 Roslyn + Bee 缓存 rsp，见下） |
| 本地化审计 | ✅ 通过，两语言 1098 条对齐 |
| `FalsumSelfCheck.RunMobileShell` | ✅ **`passed:true`**（`Diagnostics/FalsumMobileSelfCheck.json`）。覆盖角度映射 14 采样点、边界归小序号、扇区数、几何常量、`FalsumMobile.uss` 存在、UXML chrome 元素齐全、11 条本地化 key 命中 |
| `FalsumSelfCheck.Run`（全量） | ⚠️ **仍红**：卡在 `CheckUiToolkitResources` 的 Skyarea Bank 标签栏断言（`FalsumSelfCheck.cs:812`）。这是**工作区既有未提交改动**造成的失败，与本次移动端改动无关；它排在前面，所以全量自检跑不到移动端那项 —— 这也是新增独立入口 `RunMobileShell` 的原因 |
| 界面截图 | ✅ `Diagnostics/Screenshots/falsum_mobile_shell.png`（闭合）与 `falsum_mobile_wheel_open.png`（展开）。布局实测 `dock=199.6 / rail=87.9 / wheel=216.2 / bottom=55.9` dp，与设计值一致 |
| 真机 | ⚠️ **未做**：本机无 Android 模块（可行性评估 §1.1 P1/P2） |

**截图环境的已知限制**：batchmode 下 `Screen.SetResolution` 不生效，窗口恒为 640×480（4:3），
而设计是 19:9。因为 PanelSettings 用 `MatchWidthOrHeight / match=0.5`，4:3 窗口会把布局
按几何平均缩放，所以截图里的栏宽比例不等于真机。**尺寸数值本身是对的**（见上表实测 dp），
真机比例需在设备上确认。

**离线编译门禁为什么存在**：`Unity.exe -batchmode -quit` 在工程被编辑器打开时会以
`another Unity instance is running with this project open` 中止并返回 1
（`docs/memory/02-invariants.md` §D-5 要求先查 `Temp/UnityLockfile`）。
`Tools/compile_check.sh` 复用 Unity 自带的 `csc.dll` 与 `Library/Bee` 里 Bee 上次生成的
response file，只把源文件清单换成磁盘现状，defines/引用/langversion 与真实编辑器编译逐字节一致，
产物写 `Library/`，不碰 `Assets/`。

> 这门禁自己也有过一次 bug 并已修：第一版两个程序集写同一个 rsp，导致 runtime 源被编译两次、
> editor 源从未被验证，于是 `FalsumMobileCapture.cs` 缺两个 `using` 却通过了离线门禁、
> 被 Unity 拒收。现在每个程序集一个 rsp 文件。

### 11.4 下一步

- **P2**：`FalsumRadialMenu` 的 `LayoutMode {Ring, Quarter}`（技能轮目前是独立实现，
  还没并进径向菜单；两边都调 `HandleRadialAction`，行为已一致，但代码是两套布局代码）
- **P3**：值 chip + 数字键盘 + `AnchorPickRadiusPixels` 偏好（USS 里 `.numeric-pad` 规则已就位，控件未建）
- **P4**：真机（需先解决 §10 存储层阻塞）

---

## 12. 修订记录

- 2026-11-09：首版。基于 `2026-11-06` 安卓可行性评估、`docs/memory/`、以及一次只读 UI 能力盘点（26 个输入动作、5 个菜单、4 类音符属性字段、USS 实测 px、世界空间 UI 命中方式）。
  用户决定：定位 = 移动端完整创作入口；横屏双手；纵向轨道 + 横屏侧栏；保留 PC 页面结构与「点立得」；左下角四分之一圆技能轮；SKYAREA 原样三段式；**锚点保留、抓取半径提为偏好**；界面说明文字仅手机端去掉。
  无真机数据：全部尺寸结论来自 2400×1080 / 900×405 dp 换算，未在设备上验证。
