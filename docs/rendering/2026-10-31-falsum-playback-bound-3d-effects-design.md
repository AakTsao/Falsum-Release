# FALSUM 播放态绑定的 3D 轨道背景设计

日期：2026-10-31

状态：设计已由用户确认

## 1. 目标

同时修正以下两套 3D 轨道背景：

1. 左右侧轨外侧的红色空心三角形。
2. TrackEnd 的动态菱形背景。

两套效果必须跟随 Falsum 的播放状态：

- 播放时继续推进。
- 暂停时冻结。
- 继续播放时从冻结相位继续。
- 拖动谱面进度时不重新计算动画相位。
- 只作用于游戏运行时与 Falsum 的三维透视编辑视图。

## 2. 问题与边界

现有 `GameplayWallScroller` 为了提高可见性，把
`triangle_fx_2_thin` 和 `triangle_fx_timemap-1` 采样到
`SHIFT_2/SPACE_2` 的克隆 Mesh 上，并加入：

```text
0.65 可见度基线
额外亮度插值
额外透明高光
自定义 RenderQueue
```

这些补偿改变了发行版视觉，也是当前还原度偏低的主要原因。

发行版三角形材质与 Shader 实际属于：

```text
LeftWall / RightWall
Shader Graphs/GameplayWall
Material: WallMaterial
Thin texture: triangle_fx_2_thin
Time texture: triangle_fx_timemap-1
```

原始墙面是极窄的本地 Quad：

```text
scale: (25, 0.375, 0.375)
```

用户视觉复核确认：三角形必须位于侧轨外侧的独立墙面，
不能铺在 `SHIFT_2/SPACE_2` 的轨道表面上。

因此最终方案采用：

```text
独立 12 x 3 矩形 Quad
        -> 4:1，与 2048 x 512 纹理同比例
        -> 长轴沿轨道纵深（Hold 方向）
        -> 放在左右侧轨外侧

WallMaterial 的纹理与 GameplayWall 数学
        -> 保留侧向遮罩、扫描和颜色公式

播放状态驱动的显式时间覆盖
```

矩形面不挂在 `SHIFT_2/SPACE_2` 下，因此不会被当作侧轨表面渲染。
原始 `LeftWall/RightWall` Quad 只作为 `WallMaterial` 与纹理来源，
其 Renderer 保持禁用，避免出现窄 Quad 的长线。

本次不处理：

- Falsum 2D 正交编辑视图。
- 按谱面绝对时间推算动画相位。
- TrackEnd 的随机布局、模糊链、构图和颜色重做。
- 轨道主体、音符、判定线、HUD 或编辑器 UI。

## 3. 架构

`GameplayBootstrap` 拥有唯一的展示动画时钟，并广播给两个效果：

```text
GameplayBootstrap
├─ PresentationAnimationClock
├─ GameplayWallScroller
└─ FalsumTrackEndComposer
```

游戏运行时与 Falsum 3D 视图共用同一个时钟状态来源：

```text
GameplayClock.IsPlaying
```

`FalsumChartEditor` 不直接计算动画相位，只通过既有播放/暂停控制改变
`GameplayClock.IsPlaying`。

### 3.1 PresentationAnimationClock

新增一个独立、可测试的轻量时钟。

状态：

```text
double TimeSeconds
bool Running
```

规则：

```text
if Running:
    TimeSeconds += clamp(unscaledDeltaTime, 0, 0.1)

if !Running:
    TimeSeconds 不变
```

不使用谱面绝对时间，不响应 seek 跳变。

明确重启谱面时调用 `Reset()`；普通暂停、继续和拖动进度不重置。

### 3.2 GameplayWallScroller

`GameplayWallScroller` 保留现有名称，避免扩大改动面，但职责改为：

1. 从 `GameplaySceneBinding.LeftWallTriangles` 和
   `RightWallTriangles` 取得 `WallMaterial` 与两张纹理。
2. 禁用原始窄 Quad Renderer。
3. 创建独立的 `12 x 3` Quad，保持纹理的 `4:1` 比例。
4. 平面绕 Y 轴旋转 `90°`，使长边沿轨道纵深。
5. 左右平面分别放在原外侧墙位置基础上：
   - 向轨道外侧偏移 `1.1`；
   - 沿纵深偏移 `-8.0`；
   - 向下偏移 `2.4`。
6. 使用 `GameplayWallRectangleTriangles`：
   - 保留原 Shader 的世界 X 侧向遮罩；
   - 保留 brightness 与粉红/白色核心颜色公式；
   - 保留 time-map 扫描；
   - 直接采样整张纹理，不做 45°/90° UV 扭转。
7. 每帧写入：

```text
_WallTimeOverride = presentationTime
_Offset = -0.1 * presentationTime
_WallUvTiling = (1, 1, 0, 0)
```

8. 不添加可见度基线、额外亮度或自定义高光。
9. 释放时删除矩形平面并恢复原始 Renderer 状态。

材质与纹理来源：

```text
Assets/InFalsus/Recovered/Material/WallMaterial.mat
```

原始材质与外侧墙适配 Shader：

```text
Assets/InFalsus/Recovered/4d.shader
Assets/InFalsus/Gameplay/Resources/WallTriangleOverlay/
GameplayWallRectangleTriangles.shader
```

### 3.3 FalsumTrackEndComposer

保留现有：

```text
FalsumTrackEndComposer
FalsumTrackEndComposerFeature
FalsumTrackEndDiamondMask
FalsumTrackEndDiamondBlur
FalsumTrackEndDiamondOverlay
```

只替换时间源。

新增：

```text
SetPresentationTime(double seconds)
```

该值写入三角形遮罩材质的：

```text
_TimeOverride
```

Shader 规则：

```hlsl
float effectTime = _TimeOverride >= 0.0
    ? _TimeOverride
    : _Time.y;
```

`_TimeOverride` 替代 `FalsumTrackEndDiamondMask` 中原有的
`_Time.y`。现有 RT、采样、模糊、构图和输出保持不变。

## 4. 数据流

`GameplayBootstrap.Update()` 在进入游戏/编辑分支前执行：

```text
running = gameplayClock != null && gameplayClock.IsPlaying
presentationClock.Tick(running, Time.unscaledDeltaTime)

seconds = presentationClock.TimeSeconds

wallScroller.SetPresentationTime(seconds)
trackEndComposer.SetPresentationTime(seconds)
```

这样游戏和 Falsum 3D 视图不会各自推进一套不同相位。

### 4.1 播放

```text
GameplayClock.IsPlaying = true
-> presentationTime 增加
-> 左右墙三角形扫描与横向 UV 偏移继续
-> TrackEnd 三角形漂移继续
```

### 4.2 暂停

```text
GameplayClock.IsPlaying = false
-> presentationTime 不变
-> 左右墙三角形冻结
-> TrackEnd 三角形冻结
```

### 4.3 拖动进度

```text
seek 只改谱面内容
-> presentationTime 保持当前相位
```

### 4.4 继续与重开

- 继续播放：从冻结相位继续累加。
- 明确重开谱面：`presentationClock.Reset()`。
- 焦点恢复：单帧增量上限 `0.1s`，避免长时间失焦后跳相。

### 4.5 2D 视图

2D 正交视图使用独立相机层。该时间流仍可继续用于其他 3D 状态，
但不得创建或显示侧轨三角形与 TrackEnd 的 2D 替代层。

## 5. 错误处理

- Shader 属性不存在：只记录一次明确警告；采样现有 Shader 时间作为降级，
  不让整个效果消失。
- 绑定目标缺失：记录一次警告，跳过对应墙面。
- TrackEnd Composer 未初始化：`SetPresentationTime` 为空操作。
- 展示时钟为空：时间冻结在最后一次有效值。
- 不写全局 `_Time`，避免影响音符、UI 或其他动画着色器。
- Wall 控制器释放时恢复原始材质与 Renderer 状态。

## 6. 文件影响

预计修改：

```text
Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs
Assets/InFalsus/Gameplay/Presentation/Tracks/GameplayWallScroller.cs
Assets/InFalsus/Gameplay/Resources/WallTriangleOverlay/
GameplayWallOuterTriangles.shader
Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs
Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader
Assets/InFalsus/Editor/GameplayWallSelfCheck.cs
Assets/InFalsus/Editor/GameplayWallCapture.cs
```

可能新增：

```text
Assets/InFalsus/Gameplay/Presentation/Timing/PresentationAnimationClock.cs
```

现有 `LeftWallTriangles` / `RightWallTriangles` 绑定保留。

所有把三角形直接铺在侧轨表面的 Shader 退役。
独立外侧墙平面使用保留 `GameplayWall` 数学的
`GameplayWallRectangleTriangles`。

## 7. 验证

### 7.1 静态与自检

- Unity Shader 编译通过。
- 左右墙共有两个有效 Wall Surface。
- 原始窄 Quad Renderer 已禁用。
- 两个独立平面分别是 `SHIFT/SPACE` 的子节点，而不是侧轨子节点。
- 平面尺寸为 `12 x 3`，保持纹理 `4:1` 比例。
- 平面长边沿轨道纵深，没有 UV 扭转。
- 平面相对原外侧墙偏移 `(±1.1, -2.4, -8.0)`。
- `WallMaterial` 保留 `InFalsus/Replica/GameplayWall` 的纹理来源。
- 独立平面使用 `InFalsus/Replica/GameplayWallRectangleTriangles`。
- RenderQueue 为 `3010`。
- `SHIFT_2/SPACE_2` 下不存在三角形 Overlay。
- TrackEnd 遮罩材质存在 `_TimeOverride`。

### 7.2 展示时钟

- 开始播放后时间单调增加。
- 暂停后连续 Tick 两次，时间完全不变。
- 恢复播放后从暂停值继续。
- 左右墙与 TrackEnd 在同一帧收到完全相同的 `presentationTime`。
- 拖动进度不改变 `presentationTime`。
- 重开谱面后时间归零。

### 7.3 运行画面

至少检查：

1. 游戏播放中，外侧墙三角形和 TrackEnd 同时运动。
2. 游戏暂停时，两套效果同时冻结。
3. Falsum 3D 视图播放、暂停、恢复行为与游戏一致。
4. 拖动进度条后，两套效果保持原相位，不跳帧。
5. Falsum 2D 视图不出现新增三角形或 TrackEnd 层。
6. 三角形位于侧轨外侧墙面，不覆盖轨道表面。

### 7.4 构建与截图

- 运行现有 `FalsumSelfCheck`。
- 运行相关 `GameplayWallSelfCheck`。
- 生成游戏与 Falsum 3D 的暂停/播放截图。
- 确认现有编辑器 UI、轨道点击、音符编辑和 2D 视图没有回归。

## 8. 验收标准

以下全部成立才视为完成：

1. 游戏与 Falsum 3D 视图都使用独立外侧背景平面。
2. 三角形恢复发行版 `GameplayWall` 的遮罩、扫描和颜色数学，
   不包含人工亮度基线。
3. TrackEnd 和外侧墙三角形共享同一展示时钟。
4. 播放/暂停/继续表现与谱面播放状态一致。
5. Seek 不强制重算或跳跃动画相位。
6. Falsum 2D 视图不受影响。
7. Shader、自检、截图和相关构建全部通过。
