# docs/memory · 索引

这是这个项目的**项目记忆**。目标：新人（或新一次会话的 AI）在**不重新逆向**的前提下，
能在 5 分钟内定位任何子系统、知道哪条规则碰不得、知道怎么验证、知道哪些文档已过期。

## 常驻上下文

根目录的 [`AGENTS.md`](../../AGENTS.md) 会在每次会话开始时自动注入。
它只放最贵的事实与指针，正文都在这里。

## 阅读顺序

| # | 文档 | 大小 | 什么时候读 |
|---|---|---|---|
| 00 | [00-orientation.md](00-orientation.md) | 5 KB | **第一次**接触本项目 |
| 01 | [01-architecture.md](01-architecture.md) | 14 KB | 找模块、改代码之前 |
| 02 | [02-invariants.md](02-invariants.md) | 12 KB | **动手之前必读**：49 条碰不得的规则（A 数据 16 / B 渲染 15 / C UI 10 / D 流程 8） |
| 03 | [03-pipeline.md](03-pipeline.md) | 10 KB | 编译/自检/截图/构建/本地化审计 |
| 04 | [04-history.md](04-history.md) | 11 KB | 「这段怪代码为什么这么写」 |
| 05 | [05-gaps.md](05-gaps.md) | 6 KB | 现在哪儿坏了、哪些文档过期了 |

## 附录：细节地图

`appendix/` 是 01-05 的**扩展阅读**，由自动化扫描 + 人工核对生成，
每条结论都带 `path:line`。当你在正文里看到一个「详见 appendix」的标记，
就去对应文件里搜关键词。

| 文件 | 内容 |
|---|---|
| [detail-editor.md](appendix/detail-editor.md) | 创作层（Core / Runtime / Tools）逐文件职责、数据模型细节、控制流 6 步、16 条不变量、已知粗糙处 |
| [detail-rendering.md](appendix/detail-rendering.md) | 表现层 23 行子系统表、每帧执行顺序、恢复资源路径规则、10 条昂贵视觉不变量 |
| [detail-ui.md](appendix/detail-ui.md) | UI Toolkit 构造链、风格切换、本地化绑定与审计规则、10 条 UI 不变量 |
| [detail-pipeline.md](appendix/detail-pipeline.md) | 14 个门禁入口的完整表、5 组实测命令、当前真相与失败原因、12 条流水线不变量 |
| [detail-history.md](appendix/detail-history.md) | 18 篇设计文档逐篇摘要（含失效标记）、五条跨篇主题 |

> `appendix/` 里标注「未核实」的条目是**地图的注脚**，不是产品缺陷。
> 追具体结论时以 `path:line` 为准，别当规范引用。

## 维护规则

1. **改代码必须同步改这里**：动了哪条不变量、哪个门禁、哪个留下的坑。
2. **只写可核实的事实**：结论要么来自代码 `path:line`，要么来自本机实测产物。
   不确定就写「未核实」，不要猜。
3. **过期比缺失更危险**：文档里的数值一旦被代码推翻，立刻改或标「已废弃」。
   `04-history.md` 末尾列了 4 篇已废弃的设计文档。
4. **不要复制 README**：README 面向使用者（怎么用编辑器），这里面向维护者（怎么改代码）。
   README 有 4 处过期，见 `05-gaps.md` §G-5。
