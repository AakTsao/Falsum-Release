# 小节线序号显示 · 设计

日期：2026-11-01
状态：已确认，待实现

## 目标

网格线开启时，为**每一条小节线**（`Chart.MeasureLineTimes` 中的时间点）在右侧显示一个小方块编号
（形如 `#9`），位置在该小节线右端的右上角。默认开启，可在偏好设置的「编辑器网格」分组内关闭，
也可在视图菜单里快速切换。

只做 3D 透视视图；2D 正交视图不在本次范围内。

## 非目标

- 不改网格线的绘制、配色、宽度。
- 不改小节线的生成规则（`Chart.RebuildTimeline`）。
- 不改 2D 视图（`Falsum2DWorld`）。
- 不写入谱面包，不影响时间轴换算。

## 编号规则

- 全局连续编号：`序号 = MeasureLineTimes 的数组下标 + 1`，从谱面首个时间点起算。
- bpm 切换处插入的额外小节线同样占一个号（不做段内重置）。
- 网格采样线与小节线重合时，以小节线为准（沿用 `FalsumBeatGrid.MergeLines` 的合并语义）。

## 显示位置

1. 取该小节线在 3D 世界中的 z，`z = _lineLocal.z + _motion.NoteZAt(timeMs, nowMs)`。
2. 锚点取右侧外轨该 z 处的 `WidthMax` 端点；无外侧轨时退回中央 6 轨右边界。
3. 该端点经 `Camera.main.WorldToScreenPoint` 投影，再用
   `RectTransformUtility.ScreenPointToLocalPointInRectangle` 转到选择画布本地坐标。
4. 方块中心 = 锚点 + `(halfWidth + 3 px, halfHeight + 3 px)`（画布参考像素，右上方向）。
5. z 落在 `[判定线 - 0.001, 轨道末端 + 0.001]` 之外的小节线不显示（与网格线同规则）。

## 远景密度

由近（判定线）到远遍历，若新方块与已采用的任一方块屏幕中心距离小于
`BadgeMinSpacing`（约 26 参考像素），则跳过该条。近处优先。

## 载体与绘制

- 复用 `FalsumSelectionCanvas`（ScreenSpaceOverlay，`sortingOrder = 100`，参考分辨率 1730×1080）。
  UITK 面板为 200，因此方块始终位于 UI 之下，被面板自然遮挡。
- 新增 `FalsumMeasureNumberOverlay`：固定上限 48 个池化条目，每个条目 = 黄色底 `Image` +
  深色 `#编号` `Text`（`FalsumUiFactory` 构造，`raycastTarget = false`）。
- 每帧 `Clear()` 后按顺序 `Place()`，未使用的条目隐藏；不产生新的 GameObject 分配。

## 状态与开关

| 载体 | 字段 | 说明 |
|---|---|---|
| 偏好 | `FalsumAdvancedPreferences.ShowMeasureNumbers`，默认 `true` | 落盘到 `preferences.json` |
| 偏好版本 | `CurrentVersion: 4 → 5` | `Normalize` 对 `previousVersion < 5` 置 `true`，旧存档不回退成关闭 |
| View | `_measureNumbersEnabled` | 视图菜单勾选态 |
| Editor | `_showMeasureNumbers` | 每帧传给世界层 |

生效条件：`_grid.Enabled && _showMeasureNumbers && 3D 视图 && 未处于偏好预览/2D`。
其中最后两条沿用现有的 `_world.SetSelectionOverlayVisible(...)` 通道：方块容器随选择覆盖层一起隐藏。

## UI 触点

- 视图菜单（`falsum.menu.view.measureNumbers`）：勾选项，切换后立即写回偏好并落盘。
- 偏好设置 → 实验性设置 →「编辑器网格」分组：新增一行 toggle
  （`falsum.preferences.advanced.measureNumbers` + `...Description`）。

## 本地化

新增 4 个 key，zh-CN / en-US 两套同步：

```
falsum.menu.view.measureNumbers
falsum.preferences.advanced.measureNumbers
falsum.preferences.advanced.measureNumbersDescription
```

（第三个键为描述，计入 4 项是为了含菜单项别名的一致性检查。）

## 验证

1. `Unity -batchmode -quit` 编译检查（`Diagnostics/UnityCompile.log`）。
2. `FalsumSelfCheck.Run`：新增一条迁移断言（旧 v4 偏好载入后 `ShowMeasureNumbers == true`）。
3. `python Tools/falsum_localization_audit.py`。
4. `FalsumEditorCapture` 截图目视核对黄色方块位置（`Diagnostics/Screenshots/falsum_editor_default.png`）。
