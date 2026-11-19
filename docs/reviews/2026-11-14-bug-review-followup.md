# 2026-11-14 Code Review 复核与续修

范围：复核用户给出的 14 个问题，接续已有未提交修复。没有对全部工作区变更做等价全量审查；没有提交、暂存或清理既有改动。

## 逐项结论

| 报告问题 | 接手时核验 | 本轮处理 / 当前事实 |
| --- | --- | --- |
| AppIcon 未跟踪导致干净检出缺图标 | 成立于版本控制；本地 8 个 PNG 和各自 `.meta` 均在，尺寸正确 | 增加 `FalsumAppIcon.ValidateAssets()`；`Apply()` 和 `BuildWindows()` 在修改配置前检查完整源集并列出所有缺失路径。选择报告建议的明确前置检查方案；图标仍未提交，发布源集时需带上 PNG 和 `.meta`。 |
| Bank 保存删除损坏 / 超限预设 | 原问题成立，已有修复未完成；全局文件名集合未在 Load 中填充，且 SaveTo 引用了未定义的 wanted | 将所有权绑定到文档和完整实际路径，只删除文档确实管理且未被外部修改的文件；保留损坏、超限、后来拷入以及其它目录里的文件。修复编译错误。 |
| EndTimeMs 缓存失效缺失 | 已有修复覆盖 CommitChange、RestoreOwned；NotifyChanged、ApplyBatch、清空流程会 RebuildTimeline | 保留现有实现；新增缓存预热后的提交、撤销、重做、取消、批处理、原地事件修改、通知和替换回归断言。 |
| BankTagRow 未设置 Horizontal | 已修复，UXML 明确 `mode="Horizontal"` | 完整自检已通过原资源检查关卡。 |
| 创建预算用 break 跳过后续紧迫音符 | 已修复，预算耗尽分支为 continue | 保留现有修复；不添加依赖音符有序的假设。 |
| Hold UV 未限制纹理窗口 | 已有逐顶点 Repeat 修复，但将窗口两端折到同一 V，原长度也会失真 | 改为以尾端为锚点连续缩放采样窗口并 Clamp 到原 atlas 区间；检查全长、缩短、接近结束、归零和恢复全长。 |
| eventsPanel.parent 空引用 | 已修复，父节点为空直接返回 | 保留现有修复。 |
| MobileShell.Detach 重写桌面属性高度 | 已修复，删除二次高度赋值 | 保留现有恢复流程。 |
| 轨道默认透明度覆盖 0.8 / 0.35 | 已修复，属性与 Initialize 均使用共享默认常量 0.8 / 0.35 | 保留现有默认值。 |
| Bank PanelOpened 不落盘 | 成立 | 使用不会被预设扫描识别的 `.panel-state` 原子保存；同时验证开启、关闭、旧 bank.json 迁移及再次加载。 |
| Bank sanitized / 截断文件名冲突覆盖 | 成立 | 新预设、导入、迁移分配未占用文件名，已加载预设保留实际路径；同 ID 的明确替换复用原路径。 |
| BASS mixer 通道释放原生竞争 | 已有锁只保护音频回调、StopBgm 与销毁，BGM 重建与循环/瞬态释放仍不完整 | PrepareBgm、OnAudioSeek、OnAudioStop、RebuildBgm、SetLoop、ResetLoops、ClearTransientChannels 共享整个操作的句柄锁；新增真实原生并发拉取和重建/释放检查。 |
| GameplaySelfCheck 缺 Charts 目录抛异常 | 已修复，Directory.Exists 后按空数组处理 | 保留修复；不恢复已删除的 StreamingAssets/Charts。它是自检夹具目录问题，不能据此断定 Windows 构建必定缺资产。 |
| 多选拖拽自检留下两个 TAP | 接手时正常返回已有 RemoveDebugNote，外层调试流程也恢复原谱；报告所述正常返回路径已不存在。但异常时仍可遗留 | 在 finally 删除两个测试音符并失效缓存，恢复选区、拖拽及指针状态；注入缺失 world 的异常，检查活动谱无幽灵音符且原选区保留。 |

## 完整验证揭示的其它遗漏

- 单预设文件曾直接使用共享码序列化，省略本地 ID 与 createdAt / updatedAt / lastUsedAt，加载时会产生新身份。本轮只在存储层补上这些字段；缺 ID 的旧文件稳定使用文件名，共享码格式保持原样。
- 默认快捷键自检把保留的 DecreaseFlowSpeed / IncreaseFlowSpeed 数值 ID 当作仍应有默认按键。源码已明确改用 Ctrl+滚轮。本轮校验全部有效动作恰好一次，并明确排除两个已停用默认动作。
- 负 delay 烘焙自检的说明为 620 ms，却比较 500 ms。按 `musicTime = chartTime + delay`，560 ms 音乐时间与 -60 ms delay 对应 620 ms 谱面时间；烘焙在音频头部补 60 ms，谱面时间仍为 620 ms。本轮修正断言并独立钉住解析值；没有修改生产时间换算。

## 验证

- Unity 6000.3.26f1 最终编译：退出码 0，`Diagnostics/UnityCompile.log` 无 C# 编译错误。
- 完整编辑器自检：退出码 0，`Diagnostics/FalsumSelfCheck.json` 的 `passed=true`、`error=""`，37 项通过。涵盖原资源检查、Bank 文件保护与迁移、本地 ID / 时间戳、缓存、Hold UV、幽灵音符异常清理、BASS 音频并发、移动端及音频预处理。
- Phase A 自检：退出码 0，`Diagnostics/UnityGameplaySelfCheck.log` 正常结束，`Diagnostics/ChartParseReport.json` 正常生成。Charts 不存在时得到 0 个夹具、0 个解析错误；证明缺目录不再中断，不代表验证了全部官方谱面。
- `python Tools/falsum_localization_audit.py`：通过。
- 本轮 8 个源码文件的 `git diff --check`：通过。
- 图标源检查：8/8 PNG 尺寸正确，均有 `.meta`。
- 检查相对接手快照的状态变化，没有清理或提交已有工作区改动。

构建前置检查不等于已证明干净检出可发布。本轮未执行 Windows 发布构建，工作区仍有大量既有未跟踪资产。
