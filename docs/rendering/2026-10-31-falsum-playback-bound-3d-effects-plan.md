# FALSUM 播放态绑定 3D 轨道背景实施计划

依据：
`docs/superpowers/specs/2026-10-31-falsum-playback-bound-3d-effects-design.md`

## 任务 1：新增展示动画时钟

文件：

- `Assets/InFalsus/Gameplay/Presentation/Timing/PresentationAnimationClock.cs`

步骤：

1. 实现 `TimeSeconds`、`Running` 和 `Reset()`。
2. 只有 Running 时累加时间。
3. 单帧增量限制为 `0.1s`。
4. 为暂停、恢复和失焦恢复提供可自检状态。

验证：

- 暂停 Tick 不增加时间。
- 播放 Tick 单调增加。
- Reset 后归零。

## 任务 2：修正左右外侧墙三角形

文件：

- `Assets/InFalsus/Gameplay/Presentation/Tracks/GameplayWallScroller.cs`

步骤：

1. 使用 `LeftWallTriangles` / `RightWallTriangles` 提供
   `WallMaterial` 与两张纹理。
2. 禁用原始窄 Quad Renderer。
3. 创建独立的 `12 x 3` Quad，保持纹理 `4:1` 比例。
4. 平面绕 Y 轴旋转 `90°`，使长边沿轨道纵深。
5. 相对原外侧墙偏移 `(±1.1, -2.4, -8.0)`。
6. 使用 `GameplayWallRectangleTriangles` 保留遮罩、扫描和颜色数学。
7. 直接采样完整纹理，不做 UV 旋转。
8. 每帧写入 `_WallTimeOverride` 和 `_Offset`。
9. 不添加可见度基线、额外亮度或自定义高光。
10. 销毁时删除独立平面并恢复原始 Renderer 状态。

验证：

- SurfaceCount 为 2。
- 平面是 `12 x 3` Quad，父节点为 SHIFT/SPACE。
- 平面长边沿轨道纵深，方向为 B。
- 平面偏移为 `(±1.1, -2.4, -8.0)`。
- RenderQueue 为 3010。
- `SHIFT_2/SPACE_2` 下没有三角形 Overlay。

## 任务 3：绑定 TrackEnd 时间

文件：

- `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs`
- `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader`

步骤：

1. 在遮罩 Shader 增加 `_TimeOverride`。
2. `TriangleOffset` 优先读取 `_TimeOverride`。
3. Composer 增加 `SetPresentationTime(double seconds)`。
4. 初始化时写入 0，之后由 `GameplayBootstrap` 每帧更新。
5. 属性缺失时记录一次警告并回退旧时间。

验证：

- 暂停时遮罩顶点位置不变化。
- 播放时遮罩顶点位置连续变化。
- Blur、Output 和构图不受影响。

## 任务 4：统一 GameplayBootstrap 时间流

文件：

- `Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs`

步骤：

1. 创建并持有 `PresentationAnimationClock`。
2. 在游戏和编辑分支之前 Tick。
3. 使用 `GameplayClock.IsPlaying` 作为运行状态。
4. 将同一个时间写给 Wall 与 TrackEnd。
5. 删除 Wall 按 `chartTimeMs` 更新的调用。
6. 重开谱面时 Reset 展示时钟。
7. 诊断摘要增加展示时间。

验证：

- 游戏和 Falsum 3D 使用同一时间。
- Seek 不改变展示时间。
- Pause 后两侧效果同帧冻结。

## 任务 5：清理侧轨 Overlay

文件：

- `Assets/InFalsus/Gameplay/Resources/WallTriangleOverlay/`

步骤：

1. 删除旧 `GameplayWallTriangles` 近似 Shader。
2. 新 Shader `GameplayWallRectangleTriangles` 只用于独立矩形面。
3. 保留完整 `GameplayWall` 的遮罩、扫描和颜色数学。
4. 不把任何三角形材质挂到 `SHIFT_2/SPACE_2`。

验证：

- 全仓库只存在独立外侧墙
  `GameplayWallRectangleTriangles` 路径。
- 编译无缺失 Shader 引用。

## 任务 6：自检与截图

文件：

- `Assets/InFalsus/Editor/GameplayWallSelfCheck.cs`
- `Assets/InFalsus/Editor/GameplayWallCapture.cs`
- `Assets/InFalsus/Editor/FalsumSelfCheck.cs`

步骤：

1. 将自检从旧 Overlay 改为 `GameplayWallRectangleTriangles`。
2. 校验独立父节点、`12 x 3` 尺寸、偏移和 RenderQueue 3010。
3. 校验暂停冻结、播放递增、两个效果同时间。
4. 截图使用显式 `presentationTime`，不按谱面时间反推。
5. 增加 TrackEnd 时间覆盖属性检查。

验证：

- `InFalsus/Gameplay/Run Gameplay Wall Self Check`
- `InFalsus/Falsum/Run Editor Self Check`
- `Diagnostics/GameplayWallSelfCheck.txt`
- `Diagnostics/GameplayWallCapture.txt`

## 任务 7：Unity 验证与结果记录

步骤：

1. 使用 Unity `6000.3.26f1` 编译运行时与编辑器程序集。
2. 运行两个自检入口。
3. 生成暂停、播放和 Seek 后截图。
4. 检查游戏与 Falsum 3D 的 Wall/TrackEnd 同步。
5. 检查 Falsum 2D 无新增效果。
6. 在本计划末尾记录实施结果。

## 最终验证

1. Shader 编译通过。
2. 自检通过。
3. 截图显示发行版比例的空心三角形。
4. 暂停冻结、播放推进、Seek 不跳相。
5. 2D 视图无回归。
6. 不回退或提交与本任务无关的现有工作区改动。

## 实施结果

- Unity `6000.3.26f1`
  `Assembly-CSharp` 与 `Assembly-CSharp-Editor` 编译通过。
- `GameplayWallSelfCheck` 通过，覆盖：
  - `GameplayWallRectangleTriangles` Shader 编译；
  - 独立外侧矩形面、`12 x 3` 比例和 `(±1.1, -2.4, -8.0)` 偏移；
  - RenderQueue `3010`；
  - 展示时钟暂停冻结、播放推进、单帧增量上限和 Reset；
  - TrackEnd Composer 初始化与 `_TimeOverride` 写入；
  - 销毁后独立平面删除和原始 Renderer 状态恢复。
- `GameplayWallCapture` 通过：
  - `visibleChangedPixels=10758`；
  - `animatedChangedPixels=14869`；
  - 8 个扫描相位均生成完整全场景截图。
- 全场景截图确认三角形位于左右侧轨之外的背景平面，
  原侧轨表面未被覆盖。
- `FalsumSelfCheck` 已执行到背景资源检查并通过
  `_TimeOverride` 校验；随后被既有
  “Design rule tool description is incorrect” 工具描述检查阻断，
  与本次 3D 背景改动无关。

截图：

```text
Diagnostics/Screenshots/gameplay_wall_full_presentation_t0.0.png
Diagnostics/Screenshots/gameplay_wall_full_presentation_t1.5.png
Diagnostics/Screenshots/gameplay_wall_full_presentation_t3.5.png
```

报告：

```text
Diagnostics/GameplayWallSelfCheck.txt
Diagnostics/GameplayWallCapture.txt
```
