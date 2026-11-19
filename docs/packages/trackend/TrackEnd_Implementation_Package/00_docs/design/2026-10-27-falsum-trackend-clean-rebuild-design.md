# FALSUM TrackEnd 清空重建设计

状态：已实现，按要求未执行 Windows Player 构建

## 1. 目标

用当前已修正的 In Falsus TrackEnd 视觉分析替换 FALSUM 现有
`FalsumTrackEndBackground` 整套实现，并清理旧运行时组件、RendererFeature、
Shader、Installer、Feature Asset、自检和调用方引用。

最终画面要求：

- 三角形粒子位于下方左右角。
- 只保留外侧极弱的淡蓝渐隐，不生成内侧淡蓝描边。
- 中央六边形与底图网格保留。
- 仅提交并显示源图下半部分，源图上半部分不得进入屏幕空间。
- 不保留旧 Donut、旧六边形放大补丁或旧宽描边逻辑。

## 2. 架构

采用清空重建，不复用旧类型和旧 Shader 名称。

新增：

```text
FalsumTrackEndComposer
FalsumTrackEndComposerFeature
FalsumTrackEndComposerInstaller
FalsumTrackEndDiamondMask.shader
FalsumTrackEndDiamondBlur.shader
FalsumTrackEndDiamondOverlay.shader
FalsumTrackEndComposerFeature.asset
```

删除：

```text
FalsumTrackEndBackground
FalsumTrackEndRendererFeature
FalsumTrackEndRendererInstaller
FalsumTrackEndMask.shader
FalsumTrackEndBlur.shader
FalsumTrackEndOverlay.shader
FalsumTrackEndRendererFeature.asset
```

同时删除上述文件的 `.meta`，并由新 Installer 更新
`PC_Renderer.asset` 中的 RendererFeature 引用。

## 3. 渲染流程

### 3.1 Mask

Mask Shader 只包含三个有效 Pass：

| Pass | 内容 | 输出 |
|---|---|---|
| 0 | 白色三角形，修正后的左右分布与下方偏移 | 白色 |
| 1 | 黑色三角形，速度系数 `0.7` | 黑色 |
| 2 | 白色六边形 | 白色 |

Draw 顺序：

```text
白色三角形
 -> 黑色三角形
 -> 白色六边形
```

六边形使用原始半径比例 `1.0`，不再应用旧实现的 `2.2` 放大补丁。
Donut 不创建 Mesh、不创建 Material、也不发生 Draw Call。

### 3.2 Blur

Blur 使用七个 Pass 的降采样/升采样链：

```text
full -> half       vertical
half -> half       horizontal
half -> quarter    vertical
quarter -> quarter horizontal
quarter -> half    vertical
half -> half       horizontal
half -> full       horizontal
```

采样核：

```text
center  0.227027
near    0.316216
far     0.070270
```

真实偏移：

```text
near = 0.428571 * r
far  = r
```

`r` 按各轴、源纹理尺寸和 `_ScreenParams` 计算。不得使用旧的
`0.593407 * r` / `1.384616 * r`。

### 3.3 Overlay

只保留外侧淡出，不生成内侧亮线：

```text
edgeFraction = signedFraction(blurredMask.r)
outerFade =
    smoothstep(0.02, 0.34, edgeFraction) *
    (1 - smoothstep(0.46, 0.72, edgeFraction))
glow = outerFade * 0.055
glowColor = (0.54, 0.70, 1.00)
```

底图仍使用 `uv + (0, 0.5)`，保持发行版合成关系。

## 4. 屏幕裁切

输出 Quad 使用完整 `0..1` UV。新建专用 Quad Mesh：

```text
UV y 范围：0.0 .. 0.7
```

Quad 顶部固定贴在屏幕顶部，不再向下偏移，宽度沿用现有 TrackEnd 构图比例。
可见高度为源图完整高度的 70%，因此：

- 不提交源图顶部 30%。
- 编辑器内显示源图下方的菱形网格和粒子。
- 粒子保持在最终画面的下方左右角。

最终布局整体宽高统一放大 `1.12` 倍，顶部不再下移。显示源图下方 70%，隐藏
顶部 30%。粒子随 Quad 等比缩放，位置保持在下方左右区域。

Overlay 保持 `uv + (0, 0.5)`，不得改为直接采样。上半段缺少菱形网格的根因
是 CPU/预览采样实现使用了 Clamp：当 V 超过 1 时被夹到纹理最上沿，形成无
网格的竖条。进一步检查发现 `playfieldBG` 上下边缘不匹配，Repeat 会在 V
跨边界处形成横向接缝。运行时因此使用 `TextureWrapMode.Mirror`，预览器也
必须按 Mirror 环绕采样。

输出 Quad 只作为透明显示层，不参与碰撞、阴影、探针或命中检测。

## 5. 生命周期与清理

`FalsumTrackEndComposer` 负责：

- 加载 `playfieldBG.png`。
- 创建三角形和六边形 Mesh。
- 创建 Mask、Blur、Overlay 材质。
- 创建 Mask RT 和专用下半部分输出 Quad。
- 分辨率变化时重建 RT。
- 销毁全部 RT、Mesh、Material、Texture 和运行时 GameObject。

`FalsumBackgroundPresenter` 初始化前按名称清理旧生成对象：

```text
Falsum_TrackEndPlayfield
Falsum_TrackEndMaskResources
```

同时更新 Capture、Build、SelfCheck 和自检 API，禁止继续引用旧类型或旧
Shader 名称。

## 6. 错误处理

- 缺少底图：记录一次错误，不创建输出 Quad。
- 缺少任一 Shader：记录缺失名称，不创建输出 Quad。
- RendererFeature 未安装：记录明确错误，编辑器其他功能继续运行。
- 没有主相机或 TrackEnd Anchor：隐藏输出，不抛异常。
- RT 创建失败：释放已分配资源。
- 销毁或重新初始化：先清理旧资源，再创建新资源。

## 7. 测试

### 7.1 静态检查

- 旧类型、旧 Shader、旧 Feature Asset 和旧 Installer 引用为零。
- Mask Shader 为 3 个 Pass。
- Blur Shader 为 2 个 Pass。
- `PC_Renderer.asset` 只包含一个 TrackEnd Composer Feature。
- 输出 Mesh 的 UV y 最大值为 `0.7`，隐藏源图顶部 30%。
- 固定种子时三角形布局可重复。

### 7.2 Unity 验证

- `FalsumSelfCheck` 通过。
- 1152x720 和 1920x1080 Capture 通过。
- 对比截图确认：
  - 只有源图下半部分可见。
  - 粒子位于下方左右角。
  - 无内侧淡蓝描边。
  - 无旧 Donut 或旧 Feature 残留。
- Windows Player 构建通过。

## 8. 非目标

- 不修改轨道、音符、判定、HUD、相机、音频或谱面格式。
- 不修改全屏 GameplayBackground 的选择逻辑。
- 不恢复原版 Constrained2D 层级。
- 不新增用户可见设置。

## 9. 验收标准

1. 旧 TrackEnd 实现及资源被完整删除。
2. 新 TrackEnd 使用新建的 Composer、Feature、Installer 和 Shader。
3. 隐藏源图顶部 30%，编辑器内显示下方 70% 的菱形网格和粒子。
4. 粒子位于下方左右角，描边仅外侧淡出。
5. 现有背景、轨道、音符和编辑器功能不变。
6. Unity 自检与 Capture 通过；Windows Player 构建另行验证。

## 10. 实施记录

- 旧 Composer、RendererFeature、Installer、3 个 Shader、Feature Asset
  及对应 `.meta` 已删除。
- 新 Composer、RendererFeature、Installer 和 3 个 Diamond Shader 已创建，
  `PC_Renderer.asset` 只替换为新的 Composer Feature。
- 输出 Quad 的 Mesh UV y 范围为 `0.0 .. 0.7`，屏幕中隐藏源图顶部 30%。
- 三角形粒子和六边形的垂直偏移已按实际 D3D RenderTexture 方向校正。
- 描边只保留外侧淡蓝渐隐，不再生成内侧亮线。
- Unity 编译通过，`FalsumSelfCheck` 25 项全部通过。
- Background Capture 在 1152x720 与 1920x1080 均通过，
  `playfieldVisible=true`，无 RenderGraph 或 Shader 错误。
- 截图：
  - `Diagnostics/Screenshots/trackend_clean_1152x720.png`
  - `Diagnostics/Screenshots/trackend_clean_1920x1080.png`
  - `Diagnostics/Screenshots/trackend_clean_1920x1080_tuned.png`
  - `Diagnostics/Screenshots/trackend_full_uv_1920x1080.png`
  - `Diagnostics/Screenshots/trackend_crop_top30_1920x1080.png`
  - `Diagnostics/Screenshots/trackend_scale112_1920x1080.png`
- 按用户要求未执行 Windows Player 构建。
