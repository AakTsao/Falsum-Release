# Falsum 原版 SkyArea 复刻

日期：2026-10-27

## 范围

本次改动用从已发布 In Falsus 构建中恢复的渲染链路，替换 Falsum 里近似的
SkyArea 表现。

来源分析：

- `D:\AakTsao WorkSpace\In Falsus Demo\analysis\reports\SkyArea_Unity_Full_Replication.md`
- `D:\AakTsao WorkSpace\In Falsus Demo\analysis\reconstructed`

开发副本：

- `D:\AakTsao WorkSpace\In Falsus Demo\backups\InFalsus-Gameplay-SkyArea-20261027`
- Git 分支：`feature/original-skyarea`

原版游戏工程目录未被修改。

## 运行时结构

```text
JUDGE_SKY
├─ PhaseA_SkyArea_Group_N
│  ├─ MeshRenderer                  SkyArea body
│  ├─ GroundShadow                  same mesh, queue 3006
│  └─ PhaseA_SkyArea_Prompt_N
│     ├─ SquareBracketLeft          original BRACKET mesh
│     └─ SquareBracketRight         Y-rotated 180 degrees
├─ SkyJudgementLineGlow             quad, queue 3011
└─ SkyJudgementLineMaskResources    hidden 8 x 90 triangle mesh
```

## 分组与几何重建

- SkyArea 按源文件顺序处理。每段只与紧邻的前一个 SkyArea 段比较。
- 前一段的终点与当前段的起点相差不超过 2 ms 时，当前段并入前一组。不要求
  水平方向重叠。
- 一个不相连的 SkyArea 消耗一个共享的谱面对象序号。地面对象同样消耗序号，
  与已发布解析器的行为一致。
- 首/末组的渲染由最终得到的 GroupId 推导，而不是再跑一次独立的水平重叠启发式。
- 每个主体段每 5 ms 采样一次：`rowCount = floor(durationMs / 5) + 1`。
- 每一行输出一个含四个顶点的四边形：
  `left(t0), left(t1), right(t1), right(t0)`。
- 索引为 `0,1,3, 3,1,2`。
- UV0 存归一化的 track X 与 `t`。
- UV1 存侧向与沿 track 的视觉距离。
- UV2 存当前宽度与段视觉长度。
- UV3 存用于接缝消除的起点/终点重叠范围。
- UV4 存首段/末段标志与段视觉起点。
- 左、右边界分别独立使用线性、sine-out 或 sine-in 缓动。
- 接缝抑制要求前一段终点与当前段起点在 `0.0001 ms` 内相等。2 ms 的分组容差
  不会让 1-2 ms 的间隙成为接缝相连。

单网格的组表示是 Falsum 的一种集成选择。逐段的输入与片元数学与已发布的
多网格 SkyArea 实现一致。

## 主体着色器

`Assets/InFalsus/Recovered/b3.shader`

- 复原 24 位整数哈希梯度噪声。
- 采样 `ARCHITECT-LINES` 与 `DOT-GRID` 的 alpha 通道。
- 重建原始的纹理空间行进与慢速相位漂移。
- 重建 `_MissingRange` 的组接缝抑制。
- 重建内部的程序化描边、深度增强、HDR 边缘色、linear-to-sRGB 转换，以及
  围绕亮度的 2 倍饱和度。
- 使用原始默认颜色与队列 3010。
- `_VisualStart` 是该段的源时间行进距离，`_VisualChartProgress` 是当前谱面
  行进距离，因此 V 相位随时间推进。
- `ARCHITECT-LINES` 与 `DOT-GRID` 使用 Repeat 包裹模式。Clamp 包裹会把累积的
  V 相位钉在纹理边缘，产生错误的长竖条外观。
- Falsum 对中心主体填充使用 `_Color.a = 0.5`，相对原始 alpha 值 1.0 把不透明度
  减半。独立渲染的边缘中心色与边缘辉光色保持不变。

## 入口括号

- 网格：原始 `BRACKET.obj`，通过其导入的 `MeshFilter` 加载。
- 纹理：原始 `SquareBracket-Diffuse.png`。
- 两个实例放置在第一段起点的边界处。
- 右侧标记绕 Y 轴旋转 180 度。
- 仅当组起点距离大于 5 ms 时才显示括号。
- 渲染队列为 3012。

## 地面阴影

- 阴影渲染器共享完全相同的 body Mesh。
- Y 轴本地偏移为 `+0.005`。
- 着色器只返回材质颜色。
- 颜色为 `(0.1415094, 0.1415094, 0.1415094, 0.2941177)`。
- 队列为 3006，ZTest 为 Always，ZWrite 为 Off。
- 不使用阴影贴图、投影器或深度采样。

## 判定线三角形

`SkyJudgementLineEffect` 重建已发布的 CPU 与 GPU 数据流：

- 网格：8 行 x 90 列，每行 181 个三角形，共 1448 个三角形。
- 每个三角形拥有三个独立顶点，因此 `SV_VertexID / 3` 等于原始三角形 id。
- 四个循环使用的结构化缓冲区，防止 CPU 写入覆盖 GPU 当前正在消费的缓冲区。
- 绝对单调的 Stopwatch 时钟驱动 Perlin 闪烁。
- 行距离削弱外侧行。
- 有效范围内的三角形以 2.5x 衰减；范围外的三角形以 5x 衰减。
- 范围很窄时进入全三角形快速衰减分支。

RenderGraph pass 在不透明物体之后执行：

1. 把三角形网格绘制到完整遮罩。
2. 水平与垂直五点模糊，写入 `W x H/4`。
3. 两个降采样阶段与两个升采样阶段。
4. 最终垂直模糊写入第二张 `W x H/4` 纹理。

模糊半径为 `0.0001`，权重为：

```text
0.227027, 0.316216, 0.316216, 0.070270, 0.070270
```

## 判定线合成

`Assets/InFalsus/Recovered/a7.shader`

- 采样经过滤波与模糊的三角形遮罩。
- 使用 `pow(max(grid, blurred), 0.3)` 与 smoothstep。
- 使用原始解析线 SDF，常数为 40、2、16.8 与 0.1。
- 白色边缘使用 `_TriangleBoundary = 0.95`。
- 使用原始强度阈值 0.450004、0.638239、颜色渐变，以及 linear-to-sRGB 转换。
- 使用 109 rad/s 与 34 rad/s 的双正弦脉冲。
- 有效 SkyArea 结束后 0.3 秒内线性淡出。

## 分辨率策略

已发布的完整遮罩使用相机宽度除以 3840 与 2048 的参考尺寸。该公式在 1080p
以上保持不变。

在 1152 x 720 下，该精确公式在批处理捕获路径中产生了 341 x 341 的遮罩，
使三角形出现可见的块状。运行时现在把遮罩缩放钳制到 0.75，在低分辨率下产生
1536 x 1536，同时在 4K 下仍产生 2048。几何、三角形数量、不透明度时序、模糊核、
颜色与合成数学均未改变。

## 诊断捕获

`GameplayPlayCapture` 现在：

- 在播放模式捕获中保留解析后的源谱面。
- 启动诊断用玩法时钟。
- 在渲染前把三角形遮罩调整到请求的捕获宽度。
- 支持 `INFALSUS_CAPTURE_WIDTH` 与 `INFALSUS_CAPTURE_HEIGHT`。
- 可选写入滤波后的三角形遮罩。

## 验证

Unity 批处理自检：

```text
[Falsum] Self check passed: 25 checks.
```

运行时捕获：

- 1152 x 720，活动 SkyArea 位于 0.9 秒与 6.2 秒。
- 1920 x 1080，活动 SkyArea 位于 0.9 秒与 6.2 秒。
- 两个验证分辨率下三角形遮罩均为 1536 x 1536。
- 主体纹理、组入口括号、组接缝、地面阴影、判定线辉光与随机化三角形均可见。

截图：

- `Diagnostics/Screenshots/skyarea_final_1152_0.900.png`
- `Diagnostics/Screenshots/skyarea_final_1152_6.200.png`
- `Diagnostics/Screenshots/skyarea_final_1152_sky_mask.png`
- `Diagnostics/Screenshots/skyarea_crisp1536_0.900.png`
- `Diagnostics/Screenshots/skyarea_crisp1536_6.200.png`
- `Diagnostics/Screenshots/skyarea_crisp1536_sky_mask.png`
