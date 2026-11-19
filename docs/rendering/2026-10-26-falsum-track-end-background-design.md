# FALSUM TrackEnd 动态背景设计

状态：已被
`2026-10-27-falsum-trackend-clean-rebuild-design.md` 取代

## 1. 目标

把 FALSUM 当前静态 `Falsum_TrackEndPlayfield` 图片 Overlay 替换为
In Falsus 发行版同构的 `BackgroundDiamond` 渲染。

本次只处理 TrackEnd 动态背景，不修改：

- 四 Lane、Shift/Space 侧轨和墙体。
- TAP、HOLD、FLICK、SKYAREA。
- 判定、Combo、HUD、相机、音频和谱面格式。
- 全屏 `GameplayBackground` 的选择与加载。

## 2. 已恢复的发行版行为

TrackEnd 背景不是一张静态图。每帧执行：

```text
清空 Mask RT
 -> 白三角形 Pass 0
 -> 白 Donut Pass 3
 -> 黑三角形 Pass 1
 -> 白六边形 Pass 2
 -> 七段横向/纵向五采样模糊
 -> 与 playfieldBG 合成
 -> 透明输出 Quad
```

FALSUM 最终接入为避免小尺寸 Quad 中出现静态六边形描边，实际 Draw 顺序为：

```text
等比放大 2.2 倍的 Hexagon 白色底层
 -> 白色三角形
 -> 黑色三角形
```

Hexagon 六条边全部位于 RT 视口之外，因此只提供整块填充，不出现顶边或描边。
Donut Pass 保留在 Shader 中，但 FALSUM 运行时不发出该 Draw。

关键常量：

```text
triangleCount = 100
triangleScale = 0.5
hexagonScale = 2.0
donut outer radius = 1.0
donut inner radius = 0.9
blurRange = 0.017
blur scalar = blurRange * 0.011 = 0.000187
glow threshold = 0.27
glow slope = 4.545455
glow power = 18.8
glow color = (0.250260, 0.288646, 0.400663)
```

最终合成使用：

```text
alpha = saturate(blurredMask.r)
backingUV = uv + (0, 0.5)
glow = min(pow(smoothstep(0.27, 0.49, frac(mask)), 18.8), 1)
color = lerp(backing, glowColor, glow)
blend = SrcAlpha, OneMinusSrcAlpha
```

## 3. 架构

### 3.1 `FalsumTrackEndBackground`

新增运行时组件，由 `FalsumBackgroundPresenter` 创建并持有。

职责：

- 加载 `StreamingAssets/Falsum/Playfield/playfieldBG.png`。
- 生成三角形、六边形、Donut 三套 Mesh。
- 创建三套 Mask 材质、Blur 材质和最终合成材质。
- 创建并维护 Mask RT、Half RT、Quarter RT。
- 创建相机朝向的透明输出 Quad。
- 提供当前渲染资源给 RendererFeature。
- 处理分辨率变化、销毁和随机种子。

该组件不继承 `BackgroundDiamond` 的 `Constrained2D` UI 依赖，直接使用相机视口
摆放输出 Quad。

### 3.2 `FalsumTrackEndRendererFeature`

新增 URP `ScriptableRendererFeature` 和 `ScriptableRenderPass`。

职责：

- 通过静态 Active 实例取得当前 `FalsumTrackEndBackground`。
- 仅在 `Camera.main` 上工作。
- 在 `RenderPassEvent.AfterRenderingOpaques` 记录 RenderGraph Pass。
- 每帧写入 Mask RT，并执行完整七段 Blur。
- 不直接修改屏幕颜色，最终仍由输出 Quad 的透明 Pass 合成。

RendererFeature 需要在 `PC_Renderer.asset` 中持久安装。安装由编辑器工具完成，
不依赖场景 Bootstrap 手工配置。

### 3.3 Shader

新增：

```text
Assets/InFalsus/Shaders/Compat/FalsumTrackEndMask.shader
Assets/InFalsus/Shaders/Compat/FalsumTrackEndBlur.shader
```

修改：

```text
Assets/InFalsus/Shaders/Compat/FalsumTrackEndOverlay.shader
```

Mask Shader 包含四个精确 Pass：

| Pass | 内容 | 输出 |
|---|---|---|
| 0 | 三角形时间漂移、压缩和 L1 裁剪 | 白色 |
| 1 | 三角形竖直速度 0.7，无压缩/裁剪 | 黑色 |
| 2 | 六边形原样顶点 | 白色 |
| 3 | Donut 径向和切线位移 | 白色 |

四个 Pass 均使用 `ZWrite Off`、`Cull Off`、`Blend One Zero`。

Blur Shader 包含 Vertical/Horizontal 两个 Pass。采样核和偏移必须按发行 DXBC：

```text
weights = 0.227027, 0.316216, 0.070270
near UV offset = 0.593407 * r
far UV offset  = 1.384616 * r
```

这等价于以 `0.428571 * r` 为近邻单位，再取
`1.384615` 与 `3.230769` 两个核位置。

Overlay Shader 改为最终 `BackgroundDiamondOutput` 等价数学。

最终 Quad 对底图使用 `uv + (0, 0.5)`，因此 FALSUM 运行时把底图 Wrap
设为 Repeat，避免上半段 UV 被 Clamp 到纹理最上边缘后拉成竖条。

## 4. 数据与生命周期

### 4.1 RT

```text
scale = clamp(mainCamera.pixelWidth / 3840, 0.75, 1)
width = floor(1200 * scale)
height = floor(800 * scale)
```

所有 RT 使用：

```text
ARGB32
no mipmap
no depth
MSAA 1
```

分辨率变化时重建 RT。正常 16:9 下：

```text
3840x2160 -> 1200x800
2880x1620 -> 900x600
2560x1440 -> 900x600
```

### 4.2 随机布局

三角形使用无种子的 `System.Random` 作为默认行为，保持发行版每次启用可能不同
的特性。

支持 `INFALSUS_TRACK_END_SEED` 环境变量。设置后使用该整数初始化
`System.Random`，供截图回归和调试使用。

保留原版首次初始化时的随机布局，不尝试恢复截图中的某一帧。

### 4.3 输出 Quad

输出 Quad 取代当前静态 Playfield，保留现有 TrackEnd 和 Background Capture
所依赖的屏幕位置：

```text
viewport x = 0.50
top margin = -0.01
width fraction = 0.40
height fraction = 0.23
size scale = 0.80
maximum height fraction = 0.82
```

它朝向主相机，深度取 `TrackEndAnchor` 的相机空间深度。Quad 使用透明合成
Shader，不参与阴影、碰撞或命中检测。

## 5. 错误处理

- 缺少底图：记录一次错误，不创建输出 Quad。
- 缺少任一 Shader：记录缺失的 Shader 名称，不创建输出 Quad。
- RendererFeature 未安装：组件报告一次明确错误，其他编辑器功能继续运行。
- 没有 `Camera.main`：保持禁用，直到找到主相机。
- RT 创建失败：释放已分配资源，不抛出到编辑器主循环。
- 组件销毁：释放所有 RT、Mesh、Material、纹理和运行时 GameObject。

## 6. 测试

### 6.1 静态检查

- 三个 Shader 可加载，Mask 有 4 个 Pass，Blur 有 2 个 Pass。
- `PC_Renderer.asset` 包含且只包含一个 TrackEnd RendererFeature。
- `FalsumBackgroundPresenter.PlayfieldVisible` 在资源就绪后为 true。
- RT 尺寸符合当前相机宽度规则。
- `INFALSUS_TRACK_END_SEED=1337` 时连续两次初始化产生相同三角形布局。

### 6.2 运行时截图

用现有 `InFalsus/Falsum/Capture Backgrounds` 验证：

- 背景加载前后不出现黑帧或明显闪烁。
- 输出 Quad 位于原有 TrackEnd 区域。
- Mask、模糊和底图均可见。
- 无 RenderGraph 错误、无 Shader 编译错误、无资源泄漏警告。

至少测试：

```text
1152x720
1920x1080
```

### 6.3 构建

- Unity 批处理编译通过。
- `FalsumSelfCheck` 通过。
- `FalsumBuild.BuildWindows` 构建成功。

## 7. 非目标

- 不恢复原始 Shader Graph 节点资产。
- 不恢复原始 `Constrained2D` 层级，只复刻可见渲染结果和当前 TrackEnd 构图。
- 不实现原版 `Backgrounds` 数组中的其他全屏背景切换。
- 不在运行时加载发行版二进制 Shader。
- 不增加 UI 控件或用户可见设置。

## 8. 验收标准

1. 当前静态 TrackEnd 图片 Overlay 完全移除。
2. TrackEnd 区域显示动态三角形、Donut、六边形遮罩和模糊辉光。
3. 默认随机布局，固定环境变量种子后可重复。
4. 背景在暂停时随 `_Time` 停止，继续播放时恢复。
5. 现有背景、轨道、音符和编辑器 UI 行为不变。
6. Unity 自检、Background Capture 和 Windows Player 构建全部通过。

## 9. 验证记录

- `FalsumSelfCheck` 全部 25 项通过，覆盖 Shader Pass 数、RendererFeature 安装、
  RT 尺寸和固定种子确定性。
- `GameplayPlayCapture` 在 `1152x720` 下完成渲染，未出现 RenderGraph、
  Shader 编译或资源异常。
- 最终画面中菱形底图、三角形分布和六边形/Donut 遮罩均可见。
- Mask RT 已单独回读验证，左右两侧均有三角形分布。
- Windows Player 构建成功：
  - `Builds/FalsumEditor-Alpha0.31-20261026/FalsumEditor.exe`
