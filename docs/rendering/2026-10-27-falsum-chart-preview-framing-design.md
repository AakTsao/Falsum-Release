# FALSUM 谱面预览取景与 TrackEnd 联动设计

状态：已实现

## 1. 目标

谱面预览模式进入和退出时，游戏画面与上下栏收展使用同一动画进度：

- 背景保持不缩放。
- 轨道、侧轨、音符、判定线、TrackEnd 和游戏层特效一起缩放。
- TrackEnd 始终只显示下半部分。
- 普通编辑状态下，TrackEnd 上半边缘位于上栏下沿，完整下半部分可见。
- 预览状态下，上栏完全收起时 TrackEnd 上边缘贴到屏幕上沿。
- 进入/退出动画不得出现背景漂移、TrackEnd 与栏位错位或分帧穿帮。

## 2. 动画进度

复用现有 0.24 秒 SmoothStep 上下栏动画，不新增独立 Tween。

`FalsumEditorView` 输出：

```text
ChromeCollapse:
    0 = 上下栏完全展开
    1 = 上下栏完全收起

TopInset:
    当前上栏下沿的归一化屏幕高度
```

两个值都在现有 `TickChartPreviewChromeAnimation()` 中更新，并与栏位
`translate` 使用同一个 `eased` 值。

## 3. 游戏世界缩放

不使用相机 FOV，避免背景一起缩放。

`FalsumChartEditor` 在每帧 `_view.Tick()` 后读取 `ChromeCollapse`：

```text
normalViewportHeight =
    screenHeight - topBarHeight - bottomBarHeight

targetZoom =
    screenHeight / normalViewportHeight * 1.157 * 0.95

zoom = lerp(1, targetZoom, ChromeCollapse)
```

1080p、上栏 56、上栏 64 时，基础取景缩放约为 `1.125`，
叠加 `1.157 × 0.95 = 1.09915` 后约为 `1.237`。

缩放对象：

```text
MasterScene
NotesContainer
ComboRoot
```

仅缩放 X/Y，Z 保持不变。背景对象不在此层级中，因此不受影响。

预览缩放使用相机光学变焦，以屏幕中心为参考点：

```text
halfFovTangent =
    tan(baseFieldOfView / 2) / zoom

fieldOfView =
    2 * atan(halfFovTangent)
```

背景 Presenter 每帧按当前 FOV 反向调整背景尺寸，因此背景在屏幕上
保持原尺寸。TrackEnd 继续使用独立的屏幕空间 Framing，不跟随 FOV
产生透视形变。

## 4. TrackEnd

`FalsumTrackEndComposer` 恢复为只显示源图下方 50%：

```text
OutputUvMax = 0.5
visibleHeight = fullHeight * 0.5
```

新增运行时 Framing 接口：

```text
scaleMultiplier = current world zoom
topInset = current top bar inset
```

输出 Quad 的顶部位置：

```text
top = 1 - topInset
```

因此：

- 普通编辑状态：`topInset = topBarHeight / screenHeight`，下边缘完整显示。
- 预览收起过程：`topInset` 随上栏同步减小。
- 完全收起：`topInset = 0`，TrackEnd 上边缘贴屏幕顶部。

TrackEnd 的缩放使用与轨道相同的 `zoom`，粒子继续绑定在源图下方左右区域。

TrackEnd 普通编辑状态的基础倍率按预览比例反推：

```text
normalScale = 1.12 / (1.157 * 0.95)
            ≈ 1.0191

previewScale = normalScale * (oldPreviewZoom * 1.157 * 0.95)
             ≈ 1.12 * oldPreviewZoom
             ≈ 1.2693
```

因此预览状态 TrackEnd 的边长与原预览版本一致。

## 5. 视口计算

`GetChartViewportScreenRect()` 改用栏位的实际动画偏移：

```text
top = topBar.layout.yMax + chartPreviewTopOffset
bottom = bottomBar.layout.yMin + chartPreviewBottomOffset
```

这样预览时 2D 视口、指针命中和 3D 世界缩放使用同一可见区域。

## 6. 生命周期

- ChartEditor 初始化时记录 `MasterScene`、`NotesContainer`、`ComboRoot`
  的原始 localScale。
- 每帧用原始 scale 乘以当前 X/Y zoom，避免累计误差。
- 退出预览时使用同一动画进度恢复到 1。
- TrackEnd 未初始化时 Framing 调用为空操作。
- 不修改背景 Presenter、背景纹理或背景 Quad。

## 7. 测试

- `FalsumSelfCheck` 通过。
- 普通状态截图中 TrackEnd 上边缘位于上栏下方，且只显示下半部分。
- 预览状态截图中 TrackEnd 上边缘位于屏幕上沿，背景尺寸不变。
- 动画中途截图检查上下栏、轨道和 TrackEnd 相对位置连续。
- 1152x720 与 1920x1080 Capture 通过。
- 不执行构建，除非用户另行要求。

## 7.1 版本验证

`0.4-Alpha` 构建通过后，使用普通状态与预览状态截图确认：

- 预览轨道相对旧版本边长为 `1.157 × 0.95 = 1.09915` 倍。
- 预览 TrackEnd 相对旧版本边长为 `1.0`。
- 退出预览后轨道回到 `1.0`，TrackEnd 回到反推值 `≈1.0191`。

## 8. 非目标

- 不改变谱面、音频、判定、HUD 或输入逻辑。
- 不新增独立相机或背景 RenderTexture。
- 不改变上下栏 0.24 秒动画时长和 SmoothStep 曲线。
- 不修改全屏 GameplayBackground 的切换逻辑。

## 9. 验收标准

1. 普通状态 TrackEnd 仅显示下半部分，且完整位于上栏下方。
2. 预览状态游戏层与 TrackEnd 同速放大，背景保持不变。
3. 上栏完全收起时 TrackEnd 上边缘贴屏幕顶部。
4. 退出预览时所有缩放恢复，无累计误差或分帧错位。
5. Unity 自检与两种分辨率 Capture 通过。
