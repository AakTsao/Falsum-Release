# Falsum 频谱占位设计

## 目标

移除当前的 track 频谱功能，同时保留它在 Falsum 编辑器中的可见入口。
该占位入口将在未来某个版本发布前重建。

## 范围

- 保留 `FalsumEditor.uxml` 中的 `Spectrum` 按钮。
- 保留该按钮现有的位置、图标与视觉样式。
- 保留该按钮为一个无运行时行为的惰性占位。
- 移除所有音频频谱分析、渲染、编辑器捕获与调试支持。
- 保留仓库中无关的未提交改动。

## 移除

删除频谱专用的实现文件与元数据：

- `Assets/InFalsus/Editor/FalsumSpectrumCapture.cs`
- `Assets/InFalsus/Editor/FalsumSpectrumCapture.cs.meta`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSpectrumAnalysis.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSpectrumAnalysis.cs.meta`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs.meta`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Shaders/FalsumTrackSpectrum.shader`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Shaders/FalsumTrackSpectrum.shader.meta`
- `docs/superpowers/specs/2026-10-28-falsum-track-spectrum-design.md`

从以下位置移除频谱集成：

- `FalsumChartEditor`：分析协程、分析状态、可见性状态、世界更新、调试属性
  与编辑器事件订阅。
- `FalsumEditorWorld`：渲染器所有权、分析/可见性刷新方法、每帧刷新与释放。
- `Falsum2DWorld`：渲染器所有权、分析/可见性刷新方法、每帧刷新与释放。
- `FalsumSelfCheck`：频谱分析测试与分析相关的断言。占位入口的断言保留。
- `README.md`：频谱用法与捕获工具文档。

## 入口

`FalsumEditorView` 继续解析并渲染 `Spectrum` 按钮，使该入口保持可见在同一
走带位置。它不暴露可用状态、可见性状态、事件或点击处理函数。点击该占位
不执行任何动作。

现有的 `audio-lines` 图标与 `.spectrum-toggle` 样式保留，因为它们定义了占位
的外观。

## 数据流

本次改动之后不再有频谱数据流。加载或替换音频不会启动分析，帧刷新也不会更新
频谱渲染器。

## 错误处理

不引入新的错误路径。占位没有副作用，因此音频缺失或分析缺失都不会影响编辑器
运行。

## 验证

- 在生产代码中搜索 `FalsumSpectrum`、`TrackSpectrum`、`SpectrumAnalysis`
  以及频谱渲染器引用。
- 确认 `FalsumEditor.uxml` 仍包含 `Spectrum` 按钮。
- 确认占位图标仍被加载与渲染。
- 运行可用的 Unity 编辑器编译或 Falsum 自检。
- 检查 diff，确认无关的工作区改动仍未被触碰。

## 验收标准

- Falsum 编辑器仍在原有位置显示频谱入口。
- 该入口没有任何频谱行为。
- 不再残留任何频谱分析、渲染器、着色器、捕获工具或调试路径。
- Unity 代码不再依赖被删除的频谱类型。
