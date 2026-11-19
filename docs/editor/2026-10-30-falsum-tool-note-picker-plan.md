# FALSUM 外置工具 Note 选择字段实施计划

依据：
`docs/superpowers/specs/2026-10-30-falsum-tool-note-picker-design.md`

## 任务 1：扩展工具字段模型

文件：

- `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolModels.cs`
- `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolExecution.cs`

步骤：

1. 为 `FalsumToolFieldDefinition` 增加：
   - `bool Multiple`
   - `List<string> NoteKinds`
2. 新增纯数据类 `FalsumToolNoteReference`，包含
   `Key`、`Id`、`Kind`、`TimeMs`。
3. 将 `"note"` 加入允许的字段类型。
4. 解析 `multiple` 与 `noteKinds`，未配置类型时默认四种 Note。
5. 对 Note 字段校验数量边界和 Note 类型：
   - `min/max` 必须是非负/正整数。
   - `max >= min`。
   - 单选最大数量不得大于 1。
   - 未知或重复 Note 类型失败。
6. 为 Note 引用增加序列化辅助，生成
   `{ key, id, kind, timeMs }` 对象。

验证：

- 通过 `FalsumSelfCheck` 直接解析合法和非法表单 JSON。
- 验证默认单选、默认全类型和多选边界。

## 任务 2：增加工具对话框 Note 选择层

文件：

- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolDialogView.cs`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumToolDialog.uxml`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumTools.uss`

步骤：

1. 为字段绑定增加 Note 引用列表。
2. 为 `"note"` 字段构建只读摘要文本框和“从谱面中选择”按钮。
3. 增加事件与回调契约：
   - 进入 Note 选择。
   - 由编辑器拾取屏幕位置。
   - 更新当前数量。
   - 取消。
   - 确定并返回引用数组。
4. 添加全屏透明 `ToolNotePickOverlay`：
   - 右下角数量显示。
   - “取消”和“确定”按钮。
   - 左键拾取、右键取消、`Esc` 取消。
   - 滚轮转发时间轴滚动。
5. 选择期间隐藏工具窗口，保留表单状态和滚动位置。
6. 确定后恢复窗口、更新字段摘要和对象数据，并聚焦该字段。
7. 提交表单时将 Note 引用数组写入 `FalsumToolRunRequest.Values`。

验证：

- 字段按钮可进入和退出选择模式。
- 数量、确定按钮和摘要随选择实时更新。
- 取消保持原值，确定写回新值。

## 任务 3：实现编辑器 Note 选择会话

文件：

- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs`
- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs`
- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumModePlane.cs`

步骤：

1. 新增 `EditorMode.ToolNotePick` 和选择会话状态。
2. 进入选择时保存当前编辑器选择、模式和拾取遮罩。
3. 根据字段值预选当前 Note。
4. 将 `noteKinds` 转换为现有 `FalsumNotePickMask`。
5. 单选点击替换选择；多选点击切换选择。
6. 复用重叠 Note 循环选择逻辑。
7. 达到 `max` 后拒绝新增，但允许移除。
8. 确认时重新验证 Note 仍存在、类型有效且数量在范围内。
9. 取消时恢复进入前的编辑器选择与模式。
10. 在模式平面显示选择提示和数量约束。

验证：

- 3D/2D 拾取、重叠 Note、单选、多选均可用。
- 进入和退出不会留下拖拽、待提交编辑或错误模式。
- 选择过程不产生撤销记录。

## 任务 4：接入 ChartEditor 事件

文件：

- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs`

步骤：

1. 在 `WireToolDialog()` 中订阅 Note 选择事件。
2. 实现开始、单击拾取、滚轮、取消和确认处理。
3. 选择期间阻止普通编辑输入。
4. 工具关闭、切换谱面、难度切换和开始播放时安全退出选择会话。
5. 确认后向工具对话框返回按选择顺序排列的
   `FalsumToolNoteReference[]`。

验证：

- 工具运行时在隐藏期间保持原状态。
- 确认返回工具后所有字段值不变，只有目标 Note 字段更新。

## 任务 5：本地化

文件：

- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Localization/zh-CN.json`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Localization/en-US.json`

新增 Key：

- Note 字段选择按钮。
- Note 字段摘要格式。
- Note 选择模式标题和提示。
- 当前数量格式。
- 单选/多选提示。
- 选取数量不足或超过上限。
- 无效 Note 类型。

验证：

- 两个语言文件 Key、占位符完全一致。
- 运行 `python Tools/falsum_localization_audit.py`。

## 任务 6：自检与诊断

文件：

- `Assets/InFalsus/Editor/FalsumSelfCheck.cs`
- `Assets/InFalsus/Editor/FalsumToolCapture.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSmokeTest.cs`

步骤：

1. 增加 Note 字段模型解析与非法边界测试。
2. 增加编辑器选择会话测试入口：
   - 单选替换。
   - 多选添加/移除。
   - 类型过滤。
   - `min/max` 确认状态。
   - 取消恢复。
3. 增加表单值序列化测试，确认脚本收到对象数组。
4. 增加工具打开、进入选择、确认返回的 UI 自检。

验证：

- `InFalsus/Falsum/Run Editor Self Check`
- `Diagnostics/FalsumSelfCheck.json`

## 任务 7：文档

文件：

- `docs/superpowers/specs/2026-10-23-falsum-user-tool-scripting-design.md`
- `README.md`

内容：

- 增加 `type: "note"`、`multiple`、`noteKinds`、`min/max`。
- 增加 `ctx.inputs.<key>` 对象数组示例。
- 说明取消/确定行为和 `note:<index>` key 生命周期。

验证：

- 文档示例可被 `FalsumToolFormDefinition.FromJson` 解析。

## 最终验证

1. 运行本地化审计。
2. 使用 Unity `6000.3.26f1` 编译项目。
3. 运行 Editor Self Check。
4. 运行 Note 选择捕获，检查 3D 单选、多选和右下角操作栏截图。
5. 检查 2D 视图中的相同流程。
6. 确认没有修改或回退工作区中与本功能无关的现有变更。

## 实施结果

- 运行时 `Assembly-CSharp` 和编辑器 `Assembly-CSharp-Editor`
  已使用 Unity 6.3 Roslyn 响应文件重新编译通过。
- 独立模型测试通过：
  `note-picker-model: pass`。
- 隔离 Unity 工程中的
  `InFalsus/Falsum/Run Tool Note Field Self Check` 通过：
  `Tool note field self check passed: 1 checks.`
- 中英文 JSON 与 UXML 语法通过静态检查。
- 本地化审计未发现本次新增 Key 或占位符问题；审计仍报告
  `FalsumPreferencesView.cs` 中 4 个既有硬编码中文文本。
- 完整 Editor Self Check 在进入工具检查前被既有 Sky 渲染特性检查拦截。
- 隔离工程中的 Play Mode 捕获未能创建 Falsum 编辑器；主工程当前已有
  正在运行的 Unity 会话，因此未在该会话中强制重启或执行捕获。
