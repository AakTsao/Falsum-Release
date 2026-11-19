# FALSUM 性能调试窗口设计

状态：已实现

## 1. 目标

为开发期性能排查提供一个默认隐藏的浮动窗口：

- 在“关于”页连续点击版本号五次后打开。
- 显示制谱器的 CPU 主线程、CPU 渲染线程和 GPU 帧时间。
- 显示 FPS、帧时间、目标帧预算和最近 120 帧历史。
- 将帧时间拆分为轨道/覆盖层、编辑刷新、UI 同步和 UI Toolkit 更新。
- 在卡顿时指出主要瓶颈，并保留最近一次峰值及其原因。
- 窗口关闭后停止采样，并恢复 Profiler 的原状态。

## 2. 入口

`FalsumPreferencesView` 在 About 页版本标签上监听
`PointerDownEvent`。

- 只接受鼠标左键。
- 五次点击之间任意间隔超过 1.2 秒，计数从零重新开始。
- 满五次后发出 `PerformanceWindowToggleRequested`。
- `FalsumEditorView` 关闭偏好窗口并打开性能窗口。
- 再次完成五连点会关闭已打开的性能窗口。

版本号不增加可见提示，避免影响普通用户界面。

## 3. 窗口

`FalsumPerformanceWindow` 使用 UI Toolkit 程序化构建，挂在
`FalsumRoot` 下，以继承现有主题变量。

窗口包含：

- 顶部状态、实时/暂停状态和关闭按钮。
- FPS、当前帧时间、目标预算。
- CPU 和 GPU 的帧预算占用条。
- Frame、CPU main、GPU 和预算线的 120 帧折线图。
- 当前瓶颈、原因和最近峰值。
- 轨道/覆盖层、编辑刷新、UI 同步、UI Toolkit、托管分配和 GC。
- 2D/3D 模式、音符数、事件数、网格顶点数和图形设备摘要。
- 暂停采样、切换目标预算和重置按钮。

标题栏可拖动。窗口关闭后不参与拾取，也不阻止制谱器输入。

## 4. 采样

`FalsumPerformanceMonitor` 只在窗口打开且未暂停时采样。

帧级来源：

- `Time.unscaledDeltaTime`：实际帧时间和 FPS。
- `FrameTimingManager`：CPU 主线程、CPU 渲染线程和 GPU 帧时间。
- `ProfilerRecorder`：FrameTiming 不可用或 GPU 为零时回退到
  `Render` 分类的帧时间计数器。

脚本阶段来源：

- `Update` 中的 `FalsumEditorView.Tick()`。
- `UpdateOverlays()`。
- `UpdateViews()`。
- `RefreshEditorVisuals()`。

采样同时记录当前线程托管分配和 GC 次数。原始阶段耗时只在窗口开启期间
累计，窗口关闭后不再记录。

## 5. GPU 与 Profiler 状态

Editor 环境只有在 Profiler GPU 区域启用时才稳定提供 `GPU Frame Time`。
窗口打开时：

1. 记录 `Profiler.enabled` 和 `ProfilerArea.GPU` 原状态。
2. 临时启用 Profiler 和 GPU 区域。
3. 启动 CPU 主线程、CPU 渲染线程和 GPU 的 `ProfilerRecorder`。

窗口关闭时：

1. 停止并释放三个 Recorder。
2. 按打开前的状态恢复 GPU 区域和 Profiler。
3. 清空当前帧活动标记，阻止继续累计。

`ProjectSettings.enableFrameTimingStats` 设为启用，使开发构建可以使用
`FrameTimingManager` 获取 CPU/GPU 帧时间。

## 6. 瓶颈判定

当前帧超过目标预算 15% 时进入卡顿判定，按以下顺序检查：

1. 轨道/覆盖层刷新。
2. 编辑刷新。
3. UI 同步。
4. UI Toolkit Tick。
5. GPU。
6. CPU 渲染线程。
7. CPU 主线程。
8. 呈现等待 / VSync。

判断优先使用与当前帧对应的脚本阶段数据，再使用 CPU/GPU 帧时间。
窗口保留最近 120 帧，显示 P95、超时帧数和最近峰值。

## 7. 生命周期

- `FalsumChartEditor` 在初始化时创建 Monitor，并交给 View。
- `Update` 开始一帧采样。
- `LateUpdate` 在更新覆盖层后结束一帧。
- View 每 0.1 秒刷新一次性能窗口，避免自身造成明显 UI 分配。
- `Dispose` 依次关闭窗口、释放 Recorder 和断开偏好窗口事件。

## 8. 验证

已执行：

- `InFalsus.EditorTools.FalsumSelfCheck.Run`：全部检查通过。
- `InFalsus.EditorTools.FalsumPerformanceCapture.Run`：验证
  About 页两连点、1.2 秒重置、五连点打开、120 帧采样、GPU 数据、
  Profiler 状态恢复、关闭后停止采样。
- 非批处理 1920×1080 Play Mode 截图：
  `Diagnostics/Screenshots/falsum_performance_window.png`。

验收截图中 CPU 与 GPU 均显示实时百分比和毫秒值，帧时间折线与峰值原因
正常绘制。

## 9. 非目标

- 不保存性能窗口开关状态。
- 不替代 Unity Profiler 的帧调试器、调用栈或长期录制。
- 不改变制谱、播放、音频、判定或快捷键行为。
