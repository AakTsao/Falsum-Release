# FALSUM GRID 网格标尺设计

## 范围

把模式平面的全高网格线替换为一条横向的**标尺条**，其渲染方式与小节线一致；新增左侧信息框，
显示 `GRID` / 起始格 / 终止格读数；并把网格分母改为由偏好设置持有的序列，通过 `-` / `=`
逐级步进。

范围内：透视 3D 视图下的 Flick 放置、SkyArea 放置、中键编辑与锚点横向拖拽。范围外：正交
2D 视图保留其现有的平面刻度（用户决定），谱面/音符序列化不变。

## 现状

| 关注点 | 当前 |
|---|---|
| 模式平面 | `FalsumModePlane` 世界空间 uGUI 画布：面板、5 px 边框、`divisions + 1` 条全高纵向 `Image` 线、4 条说明文字 |
| 说明文字 | 世界空间 `Text` 仅用于驱动布局；像素由 `FalsumWorldTextOverlay`（屏幕空间）绘制 |
| 小节线 | `FalsumEditorWorld.BuildGrid` → `AddScreenSpaceLineQuad` + `Falsum/Editor/Grid Line`，像素宽度，辉光 2.8×，队列 3030 |
| Flick / SkyArea 分母 | `_flickDenominator = 24`，`-`/`=` 走硬编码的 `12 → 24 → 36 → 48` 阶梯 |
| 中键编辑网格 | `_middleEdit.Grid`，`-`/`=` 在 `1..192` 范围内加倍/减半（另一条无关规则） |
| 持久化 | 无：两个值都不是偏好设置 |
| 锚点横向拖拽 | **不**显示模式平面：`TryGetMiddlePlaneState` 读取 `_middleEdit`，而锚点拖拽从不设置它 |

本设计同时修复两个现存缺陷：

1. `FalsumPreferencesMigration.LegacyPreferencesDto` 没有 `Grid` 成员
   （`:101-113`），因此 `Build` 总是用全新的默认值替换网格分组（`:307`）。目前
   `LineColors` / `LineWidthScale` 在每次重新加载时都会被静默丢弃，该分组中新增的任何字段
   都会继承这一问题。
2. `-`/`=` 键没有经过 `FalsumInputAction` 路由，且
   `falsum.modePlane.gridAdjustHint`（“`[-] / [=]`”）对其标注有误。按键仍保持硬编码
   （行为不变）；仅修正标签文本。

## 架构

标尺沿现有的分界拆开：**几何体在 3D 世界空间，外框与文字在模式平面**。

- **几何体** —— `FalsumEditorWorld` 持有一个新的 `MeshBuffer _rulerMesh`
  （队列 **3032**，位于 3030 的网格与 3035 的 `LineMaterial` 之间），通过
  `CreateMesh(name, queue, useVertexColorLineShader: true)` 创建，因而取用与小节线
  *相同*的 `FalsumEditorGridLine` 材质。每一笔都是一次 `AddScreenSpaceLineQuad`
  调用，所以宽度是由 UV.y 挤出的屏幕像素，与 `BuildGrid`（`:4673-4715`）完全一致。
- **外框** —— `FalsumModePlane` 保留其面板与 4 个边框部件，删除
  `_gridLines[]` / `SetGridLines` / `MaxGridCells`，并新增一个 `_infoBox` `Image`
  加三行说明文字。
- **文字** —— 信息框各行是世界空间 `Text` 实例，仅用作布局驱动，与现有说明文字完全一致；
  像素由 `FalsumWorldTextOverlay.Place` 绘制，该处会把像素尺寸除以
  `CanvasScaleFactor`。这与时间平面文字已在使用的路径相同，因此读数在任意深度都保持清晰，
  `DebugOverlayAlignment` 也能继续工作。

每帧数据流，形态不变：

```
FalsumChartEditor.UpdateOverlays
  → FalsumModePlane.SetState(kind, prompt, divisions, focusLine, …, startCell, endCell)
  → FalsumEditorWorld.Refresh(…, FalsumRulerState ruler, …)
       → BuildRuler(nowMs, timePlaneMs, ruler)
```

`FalsumRulerState` 是一个小结构体，**取代** `FalsumEditorWorld.Refresh` 现有的
`snapLine` / `snapDivisions` / `snapColor` 参数。该结构体声明在
`FalsumEditorWorld.cs`（不在 Core）中，因为它携带 `UnityEngine.Color`；
其依赖的纯层级/步进数学位于 `FalsumBeatGrid`，以便离线自检无需场景即可断言。
BuildSnapArrow 读取同一结构体，因此这是净参数减少：

```csharp
public struct FalsumRulerState
{
    public bool Visible;
    public int Divisions;        // 1..192, 0 == hidden
    public int FocusLine;        // -1 == no focus tick
    public float StartNormalized; // NaN == no start marker
    public float EndNormalized;   // NaN == no end marker
    public Color Color;
}
```

2D 世界保留自己的 `(flickDivision, snapLine, snapDivisions,
snapColor)` 参数，不受影响。

## 标尺几何

位于时间平面的切片内，`z = _lineLocal.z + NoteZAt(timePlaneMs,
nowMs)`，跨度 `-TrackHalfWidth..+TrackHalfWidth`。基线在
`_floorY + PlaneHeight * 0.34`，刻度从此向上生长，长度取
`ModePlaneWorldHeight()` 的分数，使标尺随平面缩放：

| 元素 | 长度 | 核心宽度 | 辉光 |
|---|---|---|---|
| 基线 | 全跨度 | 2.2 px | 2.8× |
| 主刻度 | 0.12 × 高度 | 2.6 px | 2.8× |
| 中刻度 | 0.085 × 高度 | 2.2 px | 2.8× |
| 短刻度 | 0.055 × 高度 | 1.8 px | 2.8× |
| 起始标记 | 菱形，0.05 × 宽度 | — | — |
| 终止标记 | 方形，0.045 × 宽度 | — | — |
| 选择带 | 起始..终止，全高 | — | alpha 0.10 |

每条核心宽度都经过 `BuildGrid` 所用的同一因子
`_grid.LineWidthScale * max(0.5, Screen.height / 1080)`（`:1407-1412`），
每个刻度都像小节线一样先辉光后核心（`:1413-1430`）。

### 刻度层级

整数精确，无浮点容差 —— 层级由整除性决定：

- `i * 4 % divisions == 0` → 主刻度
- 否则 `i * 8 % divisions == 0` → 中刻度
- 否则 → 短刻度

因此默认值 24 在 0/6/12/18/24 处产生主刻度，在 0/3/…/24 处产生中刻度，每个格都有短刻度。
对于八等分为非整数的分母，层级会平滑退化：12 和 36 把中刻度并入主刻度，50 和 100 只在
0/四分之一/… 处给出主刻度，48、72 和 96 则保留全部三级。

### 密度保护

当短刻度的屏幕间距低于 4 px 时将其丢弃。间距通过每帧投影一次基线端点来测量
（`Camera.main.WorldToScreenPoint`）；`stride = ceil(4.0 / pxPerCell)`，仅当
`i % stride == 0` 时才绘制短刻度。主刻度与中刻度从不跳步。这与 2D 世界现有的
`visibleStep` 保护（`Falsum2DWorld.cs:1362-1372`）一致。

### 各模式标记

| 模式 | 起始 | 终止 | 焦点 |
|---|---|---|---|
| Flick 放置 | `_flickStart` | `_flickCurrent` | 吸附格 |
| SkyArea 放置 | `_skyStartAnchor` | `_skyEndAnchor` | 吸附格 |
| 中键编辑 | 焦点侧值 | 无 | 边界/位置线 |
| 锚点横向拖拽 | 被拖拽端点 | 无 | 吸附格 |

仅当拖拽的 `Shared` 状态存在且描述的是面向平面的操作（`Translate`、`GroundBoundary`、
`SkyBoundary`）时，锚点拖拽才显示该平面。`DragEasing`、`EndpointLateral`
以及两个组缩放操作不显示标尺。锚点层读取 `shared.Grid` / `shared.ProjectionTimeMs`，
自身**不做**任何网格数学 —— 不变量 A-18。

## 信息框

一个 `Image` 固定在平面左上角（偏移 `(16, -16)`，尺寸
`330 × 104` 画布单位，填充 `(0.16, 0.45, 0.82, 0.96)`，4 px 内边距），
承载三行通过覆盖层绘制的说明文字：

| 行 | 键 | 值 |
|---|---|---|
| 1 | `falsum.modePlane.gridValue`（现有） | `GRID: {grid}` |
| 2 | `falsum.modePlane.rulerStart`（新增） | `起始位置: {cell}` / `Start: {cell}` |
| 3 | `falsum.modePlane.rulerEnd`（新增） | `终止位置: {cell}` / `End: {cell}` |

格为整数 `round(normalized * divisions)`，钳制到
`0..divisions`，与标尺的点阵完全对齐，便于作者数刻度。第 2、3 行仅在对应标记存在时渲染；
没有起始/终止的模式只显示 `GRID`。

## 偏好设置

`FalsumGridPreferences` 新增一个字段，以文本形式存储，原因与 `LineColors`
相同（手工编辑的文件必须能携带一个畸形值并由 `Normalize` 修复，而 `Clone()` 是
JsonUtility 往返）：

```csharp
public const string DefaultDenominatorSequence =
    "12, 24, 36, 48, 50, 72, 96, 100";
public string DenominatorSequence = DefaultDenominatorSequence;
```

`FalsumBeatGrid` 在颜色辅助函数旁新增解析/格式化这一对：

- `MaxGridDenominator = 192`（单一事实来源；编辑器的
  `MaxEditableFlickGrid` 成为它的别名，保持不变量 A-8）。
- `TryParseDenominatorSequence` —— 按逗号/分号/竖线/空白切分
  （与 `TryParseColorSequence` 相同的分隔符），每项为 `1..192` 的整数，
  不允许重复，最多 8 项，**保持顺序**（不排序），空输入拒绝。
- `FormatDenominatorSequence` —— 规范化 `", "` 拼接；空列表回退到默认序列。
- 错误使用新键 `falsum.error.gridSequence.required` / `.tooMany`
  （`{max}`）/ `.range`（`{value}`）/ `.duplicate`（`{value}`）。

`Normalize()` 扩展其网格块（`FalsumPreferences.cs:451-465`），采用与
`LineColors` 相同的解析 → 替换为默认值 → 重新序列化形态。

### 迁移修复

`LegacyPreferencesDto` 新增 `public FalsumGridPreferences Grid;`，
`JsonPresence` 新增一个 `PresenceGrid` 位，通过 `HasTopLevelKey("Grid")` 探测，
`Build` 调用新的 `BuildGrid(dto.Grid, present)` 而不再总是构造默认值。pre-v6 的
`Advanced` 提升仍会运行，但仅在 `Grid` 分组缺失时执行，因此 v6 文件不会被陈旧的
`Advanced` 值覆盖。`CurrentVersion` 从 7 → 8，使存储在下次加载时用新分组重写每个配置。

这正是 `LineColors` / `LineWidthScale` 首次能够持久化的原因；新字段依赖于此。

## 分母步进

一个序列，一个算子，三个消费方：

```csharp
// nearest entry strictly below current, clamped to the first entry
static int StepDown(int current);
// nearest entry strictly above current, clamped to the last entry
static int StepUp(int current);
```

两者都位于 `FalsumBeatGrid`，且只做整数运算。

| 消费方 | 变更 |
|---|---|
| `_flickDenominator` | `Decrease/IncreaseFlickDenominator` 调用 `StepDown`/`StepUp`；提示文本不变（`falsum.hint.flick.denominator` 只插值该值） |
| 中键编辑 `StepMiddleEditGrid` | 替换为 `StepDown`/`StepUp`，保留 `1..192` 钳制；不是序列成员的原生网格（例如来自 `1/200` 分数的 200）会步进到最近的成员，这正是混合分母可用的原因 |
| 会话重置 | `SetMode` / 径向放置**不再**重置为 24；最后选择的值在会话内保留（用户决定）。应用偏好设置时将该值钳制到序列范围内 |

中键编辑自检断言（`FalsumChartEditor.MiddleEdit.cs:533-539`）
随新语义变更：`(16, -1) == 12`、`(16, +1) == 24`、
`(192, +1) == 192`、`(1, -1) == 1`。

两个魔法数字变为推导值：

- `CommitFlick` 的 `denominator < 48` 加倍保护（`:9353`）变为
  `denominator * 2 <= MaxGridDenominator`，在不使用字面量的前提下保留原意
  （表示奇数格数且不超过 192 上限）。
- `SnapNormalizedCell` 的 `1..48` 钳制（`:12605-12609`）放宽为
  `1..MaxGridDenominator`，以便序列中较高的项可以被吸附。

## 偏好设置界面

`FalsumEditingPreferencesPage.BuildGrid` 在网格宽度之后追加一行：

- `TextField` 绑定到 `preferences.Grid.DenominatorSequence`，
  类为 `preferences-field preferences-grid-colors`（8 项列表所需的 380 px 变体）。
- 变更时：解析；无效 → `is-invalid` 类 +
  `DenominatorSequenceInvalid` 页面标志，草稿不变；有效 → 规范化重新序列化 +
  `host.ClearStatus()`。
- `FalsumPreferencesView.RequestCommit` 新增一个与现有
  `GridColorsInvalid` 并列的保护，使用
  `falsum.error.preferences.fixGridSequence`；关闭时重置该标志。

新增键，两个语言区域，占位符集合完全一致：
`falsum.preferences.editing.denominatorSequence`（+ `…Description`）、
`falsum.error.preferences.fixGridSequence`、四个
`falsum.error.gridSequence.*`，以及两个 `falsum.modePlane.ruler*`。

## 自检

`FalsumSelfCheck` 新增内容：

- `CheckBeatGrid`：解析/格式化往返、顺序保持、重复/范围/空输入拒绝、
  `StepDown`/`StepUp` 首尾钳制，以及非成员原生值步进到最近成员。
- `CheckPreferences`：默认序列文本、`Normalize` 对畸形序列的修复，
  以及迁移修复引入的 v7 `Grid` 往返。
- `middle_grid_step` 更新为上述序列语义。

## 验证

1. `Unity -batchmode -quit -projectPath <proj> -logFile Diagnostics/UnityCompile.log`
2. `Unity … -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run`
3. `python Tools/falsum_localization_audit.py`（0 问题）
4. `Capture Editor` 截图，与参考设计稿对比：
   标尺层级、信息框文字、四个模式下标记均存在
5. 手动：进入 Flick，在整个序列上按 `-`/`=`，确认读数、刻度层级，
   以及离开再进入后值保持不变；打开偏好设置，确认该行拒绝畸形序列

## 风险

- 标尺仅在平面可见时每帧增加一个 mesh 和约 `divisions`+2 个四边形对；
  由于没有缓存任何内容，现有的每帧重建预算不受影响。
- 序列成员资格对中键编辑是建议性的：携带序列之外原生网格的音符会保持该值，直到用户步进它，
  因此带 `1/200` 分母的谱面仍可原样打开。
- 由于 `-`/`=` 现在让中键编辑网格沿序列步进，原生网格为 `200` 的音符不再能*向上*步进
  （200 已超过最后一项 100）。这一点被接受：`100` 是序列提供的最大分母，
  而 `FalsumFraction.MaxDenominator`（200）仍是存储上限，不是交互上限。
- 提升 `CurrentVersion` 会在首次加载时重写每个配置；新增的 `Grid` 分组是唯一新内容，
  迁移存在位保证旧文件无损。
