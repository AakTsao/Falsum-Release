# FALSUM AP Combo 显示设计

状态：已实现，并通过 Unity 自检、运行时截图和 Windows Player 构建

## 1. 目标

FALSUM 播放与编辑预览不模拟玩家输入，Combo 按“从谱面起点到当前时间全部
AP”计算。暂停、播放、拖动进度条、文本跳转、滚轮跳转和反向跳转后，显示值
都必须只由当前谱面时间决定，不能继承上一次播放或跳转时的增量状态。

## 2. 计算语义

Combo 以原版 `_Tf` 判定事件为最小单位，不使用可见音符数。

- TAP、FLICK 各产生 1 个事件。
- HOLD 产生头事件、固定步长中段事件和可选尾事件。
- SKYAREA 产生固定步长采样事件和可选尾事件，没有起点事件。
- 所有音符共享同一个 Combo；AP 状态下每个事件都使 Combo 加 1。

事件步长使用音符起点处的 BPM：

```text
bpmForEvents = bpm >= 249.99 ? bpm * 0.5 : bpm
stepMs = 60000 / bpmForEvents / 2
```

持续时间为 `D` 时：

```text
interiorCount = floor(D / stepMs)
tailCount = D - interiorCount * stepMs >= 2 ? 1 : 0

HOLD events    = 1 + interiorCount + tailCount
SKYAREA events = interiorCount + tailCount
```

查询任意时间 `T` 时，直接计算每个音符在 `T` 之前已经到达的 AP 事件数并求和。
这样不需要维护播放增量，也不会因 seek 丢失历史 Combo。

## 3. 显示

沿用原版世界空间层级：

```text
GameCamera
└─ Combo
   ├─ ComboCount
   └─ Quad
```

- 保留 `Combo` 在 GameCamera 本地坐标中的位置、旋转和缩放。
- 保留 `Quad` 底光；未加载谱面时可关闭整个 `Combo`，加载后底光持续显示。
- 在 `ComboCount` 上创建运行时文字网格，使用项目已有的
  `Falsum/Exo-SemiBold` 字体。
- 只创建一个文字网格，不叠加复制数字或虚影。
- 字宽整体为 `0.03`；填充色固定为 `#215F9A`，填充透明度为 90%
  （Alpha 值为 10%）。
- 文字使用 0.45 像素宽的 `#C7EAE9` 不透明描边。
- 底光颜色透明度为 30%，四周使用 18% 的 UV 距离渐变淡出。
- Combo `< 2` 时清空并隐藏数字，`>= 2` 时显示无前缀、无补零、无千位分隔的
  普通十进制数字。
- 不增加弹跳、缩放、滚动或 Tween。
- 2D/3D 编辑视图和谱面预览模式使用同一显示逻辑。

## 4. 更新路径

新增独立的 AP Combo 计算单元，只依赖 `Chart` 和查询时间，不依赖
`JudgementEngine`。`FalsumChartEditor` 每帧把当前 `_playheadMs` 传给显示
组件；拖动进度条或直接跳转时会立即刷新，而不是等待下一轮播放事件。

显示组件负责一次性的场景准备、字体材质配置和数值变化缓存。谱面被替换或
重新加载后，下一次查询自然读取新谱面，不需要保留旧时间线。

## 5. 错误处理

- 无谱面、无音符或 `Combo < 2` 时不显示数字。
- 非有限查询时间按 0 处理；负时间按 0 处理。
- 找不到 Combo 节点时记录一次明确警告，不影响编辑器和播放。
- BPM 非有限或非正时不生成事件步长，该音符不贡献 Combo。

## 6. 测试

必须覆盖：

- TAP、FLICK 的边界时间。
- HOLD 头、每个 step、精确整除尾部和不足 2ms 的剩余尾部。
- SKYAREA 无起点事件、按 step 采样和可选尾事件。
- 多个音符同时间事件。
- 音符起点前存在 BPM 事件时的逐音符步长选择。
- 任意正反向 seek 后，Combo 等于从零重新计算的结果。
- Combo 0/1 隐藏，2 开始显示。
- 桌面与窄窗口下数字不遮挡编辑 UI，预览模式下正常显示。

## 7. 非目标

- 不实现人工输入、Miss、Near 或判定窗口。
- 不改变分数、最高 Combo 或结算逻辑。
- 不恢复 FastText 的 SDF 描边生成；本次只完成 AP Combo 的计算与可读显示。

## 8. 验证记录

- `FalsumSelfCheck` 全部 25 项通过，其中 `auto_combo` 覆盖 TAP/FLICK 边界、
  HOLD/SKYAREA 步进与尾判、逐音符 BPM、正反向查询和非有限时间。
- `Capture Combo` 在 `cryogenic.falsum` 上验证：
  - `2310 ms` -> Combo `12`
  - `8000 ms` -> Combo `66`
  - 反向进度条跳转到 `5740 ms` -> Combo `48`
  - 播放中 Combo 继续增长，且与独立 AP 前缀计算一致。
- 视觉截图验证单层数字、半号字宽、`#215F9A` 高透明填充、细 `#C7EAE9`
  不透明描边
  和半透明底光。
- Windows Player 构建成功：
  `Builds/FalsumEditor-Alpha0.31-20261026/FalsumEditor.exe`。
