# FALSUM GRID 网格标尺实现计划

规格：`docs/superpowers/specs/2026-11-08-falsum-grid-ruler-design.md`

## 工作划分（互不重叠的写入范围）

| 阶段 | 负责人 | 文件 | 依赖 |
|---|---|---|---|
| P1 基础 | lead | `Core/FalsumBeatGrid.cs`, `Runtime/FalsumPreferences.cs`, `Runtime/FalsumPreferencesMigration.cs` | — |
| P2 渲染 | lead | `Runtime/FalsumEditorWorld.cs`, `UI/FalsumModePlane.cs` | P1（标尺状态结构体） |
| P3 偏好设置界面 | prefs-ui | `UI/FalsumEditingPreferencesPage.cs`, `UI/FalsumPreferencesView.cs`, 两个 `Localization/*.json` | P1 |
| P4 编辑器接线 | editor-wiring | `Runtime/FalsumChartEditor.cs`, `Runtime/FalsumChartEditor.MiddleEdit.cs`, `Runtime/FalsumChartEditor.AnchorEdit.cs` | P1 |
| P5 自检 + 文档 | lead | `Editor/FalsumSelfCheck.cs`, `docs/memory/*` | P1–P4 |

P1 落地后，P2、P3、P4 彼此不重叠，可并发执行。
任何队友都不写入 `FalsumEditorWorld.cs` 或 `FalsumModePlane.cs`。

## P1 基础（lead）

1. `FalsumBeatGrid.cs`
   - `public const int MaxGridDenominator = 192;`
   - `public const string DefaultDenominatorSequence = "12, 24, 36, 48, 50, 72, 96, 100";`
   - `TryParseDenominatorSequence(string, out List<int>, out string)` ——
     分隔符为 `, ; | \t \r \n space`，每项 `1..192`，不允许重复，
     最多 8 项，保持顺序，空输入拒绝。
   - `FormatDenominatorSequence(IList<int>)` —— `", "` 拼接，空 → 默认。
   - `StepDenominator(IList<int> seq, int current, int direction)` ——
     沿 `direction` 取严格相邻项，钳制到首/尾；非成员的
     `current` 吸附到该方向上最近的成员。
   - 通过 `L.Get` 使用的新错误键：`falsum.error.gridSequence.required`、
     `.tooMany`（`{max}`）、`.range`（`{value}`）、`.duplicate`（`{value}`）。
2. `FalsumPreferences.cs`
   - `FalsumGridPreferences.DenominatorSequence` = 默认常量。
   - `Normalize()` 网格块：解析 → 失败时替换为默认值 →
     始终重新序列化（与 `LineColors` 形态相同）。
   - `CurrentVersion` 7 → 8。
3. `FalsumPreferencesMigration.cs`
   - `LegacyPreferencesDto` 新增 `public FalsumGridPreferences Grid;`。
   - `PresenceGrid` 位 + `HasTopLevelKey(json, "Grid")` 探测。
   - `Build` 调用 `BuildGrid(dto.Grid, present)`；pre-v6 的 `Advanced`
     提升仅在 `Grid` 分组**缺失**时适用。

## P2 渲染（lead）

1. `FalsumEditorWorld.cs`
   - 声明 `public struct FalsumRulerState`（Visible、Divisions、
     FocusLine、StartNormalized、EndNormalized、Color；`NaN` = 无标记）。
   - 用单个 `FalsumRulerState` 替换 `Refresh` 的
     `snapLine/snapDivisions/snapColor` 三元组；吸附箭头的行为保持不变。
   - 新增 `MeshBuffer _rulerMesh = CreateMesh("FalsumEditorRuler", 3032, true)`。
   - `BuildRuler(nowMs, timePlaneMs, ruler)`：基线 + 层级 + 焦点 +
     起始菱形 / 终止方形 + 选择带，全部经 `AddScreenSpaceLineQuad` 并沿用
     `BuildGrid` 的宽度配方。
   - 层级判定为整数整除（`i*4 % d == 0`，否则 `i*8 % d == 0`）；
     短刻度跳步保护阈值 4 px。
2. `FalsumModePlane.cs`
   - 删除 `_gridLines`、`SetGridLines`、`MaxGridCells`，
     以及 `GridLine_` 分配循环。
   - 新增 `_infoBox` Image + 3 行说明文字（复用 `GridLabel`，另加
     `StartLabel` / `EndLabel`），全部由新的 `SetState` 参数
     `int startCell, int endCell` 提供。
   - 保留面板 + 4 个边框；每条说明文字都保留 `PlaceWorldText` / 覆盖层路径，
     使 `DebugOverlayAlignment` 继续有意义。

## P3 偏好设置界面（prefs-ui）

1. `FalsumEditingPreferencesPage.cs`
   - 与 `GridColorsInvalid` 并列的 `DenominatorSequenceInvalid` 标志。
   - 网格宽度之后新增 `TextField` 行：解析或拒绝并使用
     `is-invalid`，成功时规范化重新序列化，
     类为 `preferences-field preferences-grid-colors`。
2. `FalsumPreferencesView.cs`
   - 为新标志增加 `RequestCommit` 保护；关闭时重置。
3. 两个本地化 JSON —— 每个新键都出现在两个文件中，占位符集合一致，
   `.cs` 的 `text=`/`tooltip=` 中不出现汉字/ASCII 文本。

## P4 编辑器接线（editor-wiring）

1. `FalsumChartEditor.cs`
   - `_gridDenominatorSequence`（来自偏好设置）+ `_denominatorIndex`。
   - `Decrease/IncreaseFlickDenominator` → `StepDenominator`。
   - `SetMode` / 径向：不再重置为 24（会话记忆）。
   - `ApplyGridPreferences`：解析序列，钳制当前值。
   - `CommitFlick` 的 `< 48` 加倍保护 → `denominator * 2 <= MaxGridDenominator`。
   - `SnapNormalizedCell` 钳制 `1..48` → `1..MaxGridDenominator`。
   - 将 `FalsumRulerState`（起始/终止来自 `_flickStart`/`_flickCurrent`
     或 `_skyStartAnchor`/`_skyEndAnchor`）传入 `Refresh`。
   - `UpdateModePlane`：计算起始/终止格并交给 `SetState`。
2. `FalsumChartEditor.MiddleEdit.cs`
   - `StepMiddleEditGrid` → 序列上的 `StepDenominator`。
   - 更新 `middle_grid_step` 自检期望值
     （`16,-1 → 12`、`16,+1 → 24`、`192,+1 → 192`、`1,-1 → 1`）。
3. `FalsumChartEditor.AnchorEdit.cs`
   - 在 `Shared` 状态存在且操作为面向平面的锚点拖拽期间显示该平面；
     从 `shared.Grid` / `ProjectionTimeMs` 提供 `FalsumRulerState`。不做本地网格数学（A-18）。

## P5 验证（lead）

1. 编译：`Unity -batchmode -quit … -logFile Diagnostics/UnityCompile.log`
2. `FalsumSelfCheck.Run` → `Diagnostics/FalsumSelfCheck.json` `passed:true`
3. `python Tools/falsum_localization_audit.py` → 退出码 0
4. `Capture Editor` 截图与设计稿对比
5. 更新 `docs/memory/01-architecture.md` + `02-invariants.md`、
   `05-gaps.md`（记录 `Grid` 分组迁移修复）。
