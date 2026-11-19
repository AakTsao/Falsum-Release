# TrackEnd 动态背景 · 实现方式说明

> 适用工程：InFalsus-Gameplay（Unity 6000.3.26f1 / URP 17.3.0，分支 `perf/render-hotpaths`）
> 本文描述的是 **当前工作区代码**，凡文档与代码冲突处一律以代码为准（见 §9）。

---

## 1. 它是什么

TrackEnd 是谱面轨道**尽头**（判定线后方、轨道消失点）的一块动态菱形网格背景。
它不是一张静态图片，而是**每帧在 GPU 上重新合成**的：

```text
清空 Mask RT
 → 白色三角形 Pass 0（100 个随机三角，向上漂浮）
 → 黑色三角形 Pass 1（同一批三角，速度 0.7，用来"擦除"重叠区）
 → 白色六边形 Pass 2（中央菱形网格填充）
 → 七段横向/纵向五采样模糊（full→half→quarter→half→full）
 → 与 playfieldBG.png 合成（取源图下半部分）
 → 相机朝向的透明 Quad 输出到屏幕
```

输出的 Quad 只显示源图**下半部分**（`OutputUvMax = 0.5`），叠加一层极弱的
外侧淡蓝渐隐（`glow = outerFade * 0.055`，颜色 `(0.54, 0.70, 1.00)`）。

底图是一张 1024×1024 的深色**菱形网格**图（平均亮度 50.9/255），
上面散布着灰白色碎片多边形。运行时那 100 个漂浮三角**不在贴图里**，
是 Mask Shader 用顶点着色器实时算出来的。

## 2. 三个文件构成全部核心

| 文件 | 角色 | 行数 |
|---|---|---|
| `01_runtime/.../FalsumTrackEndComposer.cs` | **渲染资源与几何的拥有者**（MonoBehaviour） | 912 |
| `01_runtime/.../FalsumTrackEndComposerFeature.cs` | **URP RendererFeature**，每帧执行 Mask + Blur | 232 |
| `01_runtime/.../Shaders/Compat/*.shader` | Mask（3 Pass）/ Blur（2 Pass）/ Overlay（1 Pass） | 162+96+94 |
| `02_editor/.../FalsumTrackEndComposerInstaller.cs` | 把 Feature 挂到 `PC_Renderer.asset` 的编辑器工具 | 140 |

依赖关系是**单向**的：

```text
FalsumBackgroundPresenter  ──AddComponent──>  FalsumTrackEndComposer
                                                     │ static Active
                                                     ▼
GameplayBootstrap ──SetPresentationTime──>  FalsumTrackEndComposer
                                                     ▲ 读取 Active / MaskRTHandle
                                                     │
                                       FalsumTrackEndComposerFeature
                                                     ▲ 由 PC_Renderer.asset 持有
                                                     │
                                    FalsumTrackEndComposerInstaller（编辑器）
```

`FalsumTrackEndComposer.Active` 是静态单例（`:77`、`:242`）。Feature 每帧从它取
`MaskRTHandle`、`TriangleRenderer`、`BlurMaterial` 等资源；Composer 侧不做任何
渲染调用，**所有 GPU 工作都在 Feature 里**。

## 3. 生命周期

### 3.1 创建（唯一入口）

`FalsumBackgroundPresenter.Initialize`（`05_integration/02_*.cs`）：

```csharp
if (_trackEndAnchor != null) {
    _trackEndComposer = gameObject.AddComponent<FalsumTrackEndComposer>();
    _trackEndComposer.Initialize(_trackEndAnchor);
}
```

Composer 被挂在 **Presenter 自己的 GameObject** 上，锚点作为参数传入。
`Initialize` 内部顺序（`:215-244`）：

```text
RemoveLegacyVisuals()          删除旧实现遗留的 Falsum_TrackEndPlayfield /
                               Falsum_TrackEndMaskResources 子对象（按名字）
LoadPlayfieldTexture()         读 StreamingAssets/Falsum/Playfield/playfieldBG.png
CreateMaterials()              3 个 Shader.Find + 4 个 Material
BuildMeshes()                  三角形 Mesh / 六边形 Mesh / 输出 Quad Mesh
CreateMaskRenderers()          创建 Layer 31 的隐藏 MeshRenderer
CreateOutputQuad()             创建透明输出 Quad（先 SetActive(false)）
EnsureRenderTextures()         按相机宽度算遮罩尺寸并建 RT
_initialized = true; Active = this;
UpdateOutputPlacement()
```

任何一步失败（贴图缺失 / Shader 缺失 / RT 创建失败）都会
`Debug.LogError` 并提前返回，`_initialized` 保持 `false`，整个 TrackEnd 静默不显示，
**不影响**背景 / 轨道 / 音符 / 判定。

### 3.2 每帧

`Update`（`:318-337`）：只做两件事——相机分辨率变了就重建 RT，
然后重新摆放输出 Quad。**不碰遮罩内容**。

真正逐帧重建遮罩的是 Feature 的 `RecordRenderGraph`。

### 3.3 时间驱动

`_TimeOverride` 是这里最关键的设计。Composer 每帧被 Bootstrap 写入
`PresentationAnimationClock.TimeSeconds`：

```csharp
// GameplayBootstrap.cs:440-444
_presentationClock.Tick(running, Time.unscaledDeltaTime);
var seconds = _presentationClock.TimeSeconds;
_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(seconds);
```

Mask Shader 里：

```hlsl
float effectTime = _TimeOverride >= 0.0 ? _TimeOverride : _Time.y;
```

`_TimeOverride` 默认 `-1`（`Mask` Shader `:5`），一旦被写入 ≥ 0 就用它。
这样三角粒子的动画相位跟着**表现时钟**走，而不是 `_Time.y`：

- 暂停 → 表现时钟冻结 → 粒子停住（用 `_Time.y` 会继续动，错误）
- 重开谱面 → `SetPresentationTime(0.0)`（`GameplayBootstrap.cs:312`、`:988`）→ 归零
- Seek → **不**触发归零，表现相位不跳变

Composer 侧 `SetPresentationTime`（`:174-195`）做了防御：如果 Material 没有
`_TimeOverride` 属性，只警告一次（`_timeOverrideWarningLogged`）并回退 Unity 时间。

## 4. 遮罩几何

### 4.1 三角形（100 个）

`BuildTriangleLayout`（`:525-570`），使用 `System.Random`：

```csharp
radius = 0.02 + random.NextDouble() * 0.5;          // TriangleScale = 0.5
angle  = (random.NextDouble() > 0.5 ? 0.5 : 1.5) * PI;   // 只分左右两簇
// 每个三角 3 个顶点，顶点绕中心均分，x 压扁 2/3：
vertex = (halfRadius * sin(a) * 2/3, halfRadius * cos(a), 0)
```

每个顶点写入 `uv0.x = triangleIndex`（`uv0` 的 3 个分量都填同一个 index），
Shader 用 `uv0.x` 作为伪随机种子，在**顶点着色器里**算每帧偏移：

```hlsl
float2 TriangleSeed(float id) { return frac(sin(float2(id+1, id+2)) * 43758.546875) * float2(3.0, 0.1) + float2(-1.5, 0.1); }

float2 TriangleOffset(float2 seed, float verticalSpeed) {
    float phase  = seed.y * (effectTime + 100.0) * verticalSpeed;
    float radial = OriginalSign(phase) * frac(abs(phase));   // 三角波，-1..1 往复
    return 1.5 * radial * float2(sin(seed.x), 1.35 * cos(seed.x));
}
```

所以三角形位置**完全在 GPU 上算**，CPU 每帧零分配。

- 白色 Pass：`verticalSpeed = 1.0`，额外做 `|x| > 0.7` 时横向收缩 +
  `|x| + |y| > 1.5` 时沿 L1 归一（`:66-78`），把粒子压回可视范围内。
- 黑色 Pass：`verticalSpeed = 0.7`，只做 `positionOS + offset`，输出
  `float4(0,0,0,0)`，`Blend One Zero` → **把重叠区域写成 0**，形成镂空效果。

随机种子来源（`:841-850`）：读环境变量 `INFALSUS_TRACK_END_SEED`（int），
没有就用 `new System.Random()`（每次启动不同）。自检用
`ComputeTriangleLayoutHash(1337)` 断言布局可复现。

### 4.2 六边形

`BuildHexagonMesh`（`:572-600`）：6 个扇形三角，`radius = HexagonScale * 0.5 = 1.0`。
**不放大**、不加描边、不生成 Donut。

> 注意：早期文档写「六边形半径 1.0、乘以 2.2 放大」，当前代码是
> `HexagonScale = 2.0f` 且 `radius = 1.0`，以代码为准。

### 4.3 输出 Quad

`BuildOutputQuadMesh`（`:602-631`）：4 顶点 2 三角，UV 的 V 上限是 `OutputUvMax = 0.5`：

```csharp
new Vector2(0, 0), new Vector2(1, 0.5f),
new Vector2(1, 0), new Vector2(0, 0.5f)
```

即只取遮罩纹理的下半部分。Mesh 与全部运行时对象都用
`HideFlags.HideAndDontSave`，不进场景、不参与保存。

## 5. 渲染管线（Feature）

`FalsumTrackEndComposerFeature.cs` 是 URP 17 的 **RenderGraph** 实现：

```text
Create()            → new ComposerPass()，renderPassEvent = AfterRenderingOpaques
AddRenderPasses()   → Composer = FalsumTrackEndComposer.Active
                      Composer == null || !IsReady → 直接 return（不排队）
                      renderer.EnqueuePass(_pass)
RecordRenderGraph() → ① ImportTexture(Composer.MaskRTHandle)
                      ② DrawMask：AddRasterRenderPass + ClearRenderTarget +
                         DrawRenderer ×3（白三角/黑三角/白六边形）
                      ③ 建 4 张临时 RT：halfA/halfB/quarterA/quarterB
                      ④ 7 次 Blitter.BlitTexture 组成模糊链
                      ⑤ MaskRebuildCount++（供性能诊断）
```

关键约束：

- **只对 `Camera.main` 生效**（`:48-53`），其它相机直接跳过。
- 每个 Pass 都 `AllowPassCulling(false)`。理由写在代码注释里：
  Mask Shader 用 `_TimeOverride` 驱动动画，遮罩内容每帧都变，
  **不能**依赖 RenderGraph 的缓存/裁剪，否则粒子会卡住。
- 模糊链 7 段（`:92-133`）：

  ```text
  mask   → halfA    (pass 0 = Vertical)
  halfA  → halfB    (pass 1 = Horizontal)
  halfB  → quarterA (pass 0)
  quarterA → quarterB (pass 1)
  quarterB → halfA  (pass 0)
  halfA  → halfB    (pass 1)
  halfB  → mask     (pass 1)   ← 最终写回 mask 自身
  ```

  注意最后一步写回**同一个 RT**，输出 Overlay 采样这个已模糊的 RT。

## 6. Blur Shader 数值

> **构建前提**：三个 TrackEnd Shader 没有 Material 资产引用，必须登记在
> `ProjectSettings/GraphicsSettings.asset` 的 `m_AlwaysIncludedShaders`
> （原工程 `:40-42`），否则出包后被剥离，`Shader.Find` 返回 null。

```hlsl
// 五采样高斯核
center   0.227027
near     0.316216   (±nearOffset)
far      0.070270   (±farOffset)

// 真实偏移（相对半径 r）
near = 0.428571 * r
far  = 1.0      * r

// r 的算法（按轴、按源纹理尺寸）
radius = _ScreenParams.<axis> * _<Axis>Blur / _BlitTexture_TexelSize.<axis>;
```

`_VerticalBlur` / `_HorizontalBlur` 由 CPU 写入，值是：

```csharp
BlurRange  = 0.017f
BlurScalar = 0.011f
blurScalar = 0.017f * 0.011f = 0.000187f   // 写进两个 uniform
```

> 早期文档与 `04-history.md` 提到的 `0.593407 * r` / `1.384616 * r` 是旧实现的
> 数值，当前代码是 `0.428571 * r` 与 `r`。以代码为准。

## 7. Overlay Shader（最终合成）

```hlsl
grid   = SAMPLE(_BlurredTexture, uv).r;       // 模糊后的遮罩红通道
alpha  = saturate(grid);                       // 作为输出 alpha

signedFraction = grid >= 0 ? frac(grid) : -frac(-grid);
outerFade = smoothstep(0.02, 0.34, signedFraction) *
            (1 - smoothstep(0.46, 0.72, signedFraction));
glow = outerFade * 0.055;                      // 只有外侧淡出，无内侧亮线

backing = SAMPLE(_BackingTexture, uv + (0, 0.5));   // playfieldBG，V 偏移 0.5
color   = backing + (float3(0.54, 0.70, 1.00) - backing) * glow;

Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
return float4(color, alpha);
```

两个必须记住的点：

1. **`uv + (0, 0.5)` 不能改**。这是发行版的合成关系，配合输出 Quad 的
   `OutputUvMax = 0.5`，正好取到源图的另一半。
2. **`TextureWrapMode.Mirror` 不能改成 Repeat**。`playfieldBG` 第 0 行与第 1023 行
   平均差 **23.53/255**（≈9%），Repeat 会把这两行直接拼在一起，在 V 跨界处拉出
   横向接缝（实测见 `04_textures/贴图清单.md` §1.2）。`Mirror` 是反射采样，
   边界天然连续，**不需要底图对称**。Composer 在 `LoadPlayfieldTexture` 里硬写
   `texture.wrapMode = TextureWrapMode.Mirror`（`:415`）。

另外 `_outputMaterial.renderQueue = 2950`（`OutputRenderQueue`），
背景是 2900、频谱是 3002，属于三条独立的队列链（`02-invariants.md` §B-8）。

## 8. 分辨率与取景

### 8.1 遮罩尺寸

```csharp
public static Vector2Int GetMaskSize(int cameraPixelWidth) {
    var scale = Mathf.Clamp(cameraPixelWidth / 3840f, 0.75f, 1.0f);
    return new Vector2Int(floor(1200 * scale), floor(800 * scale));
}
```

| 相机宽度 | 遮罩尺寸 |
|---|---|
| 3840 | 1200 × 800 |
| 2560 | 900 × 600 |
| 1920 | 900 × 600（0.75 下限生效） |

`Update` 里检测到 `camera.pixelWidth` 对应的尺寸变化时，走
`ReleaseRenderTextures()` → 重建 RT → 重新 `RTHandles.Alloc`。
RT 是 `ARGB32`、无深度、无 mip、`msaaSamples = 1`。

### 8.2 输出 Quad 摆位

`UpdateOutputPlacement`（`:775-838`）把锚点投到视口坐标，再按一系列比例常数
反算世界宽高：

```csharp
depth     = anchorViewport.z;
effectiveScale = PlayfieldScaleMultiplier * _externalScaleMultiplier;

fullHeightFraction = min(PlayfieldMaximumHeightFraction /*0.82*/,
                         PlayfieldHeightFraction /*0.23*/ * camera.aspect)
                     * PlayfieldSizeScale /*0.80*/ * effectiveScale;
visibleHeightFraction = fullHeightFraction * OutputUvMax /*0.5*/;

worldHeight = viewportWorldHeight * visibleHeightFraction;
worldWidth  = viewportWorldHeight * camera.aspect
              * PlayfieldWidthFraction /*0.40*/ * PlayfieldSizeScale * effectiveScale;

centerViewportY = 1 - _screenTopInset - visibleHeightFraction * 0.5;
quad.position   = camera.ViewportToWorldPoint(PlayfieldViewportX /*0.50*/, centerViewportY, depth);
quad.rotation   = camera.transform.rotation;   // 永远面向相机
```

其中 `PlayfieldScaleMultiplier` 由三个常数推出（`:42-48`）：

```text
PlayfieldSourceScaleMultiplier  = 1.12
PreviewTrackScaleMultiplier     = 1.157 * 0.95   ≈ 1.09915
PreviewTrackEndScaleFraction    = 1.0
PlayfieldScaleMultiplier = 1.12 * 1.0 / 1.09915  ≈ 1.01896
```

`_externalScaleMultiplier` / `_screenTopInset` 由编辑器谱面预览模式写入
`SetPreviewFraming(zoom, topInset)`（`:197-213`），退出预览时恢复
`SetPreviewFraming(1.0f, 0.0f)`。

## 9. 文档与代码冲突（以代码为准）

| 项 | 文档（旧） | **代码（当前）** |
|---|---|---|
| 输出 UV y 上限 | 0.7 | **0.5**（`OutputUvMax`） |
| 六边形缩放 | 半径 1.0，无放大 | **`HexagonScale = 2.0`**（`radius = 1.0`） |
| 模糊偏移 | `0.593407 * r` / `1.384616 * r` | **`0.428571 * r` / `r`** |
| Mask Pass 数 | 4（含 Donut） | **3**（Donut 完全移除） |
| Overlay 描边 | 内+外亮线 | **仅外侧 `* 0.055`** |

> `FalsumSelfCheck` 也只断言 `OutputUvMax == 0.5`、`maskShader.passCount == 3`、
> `blurShader.passCount == 2`，与代码一致。

## 10. 可调参数速查

全部在 `FalsumTrackEndComposer.cs:29-48`：

```csharp
TriangleCount                 = 100
TriangleScale                 = 0.5f
HexagonScale                  = 2.0f
BlurRange                     = 0.017f
BlurScalar                    = 0.011f
HiddenMaskLayer               = 31
OutputRenderQueue             = 2950
PlayfieldViewportX            = 0.50f
PlayfieldSizeScale            = 0.80f
PlayfieldWidthFraction        = 0.40f
PlayfieldHeightFraction       = 0.23f
PlayfieldMaximumHeightFraction= 0.82f
PlayfieldSourceScaleMultiplier= 1.12f
PreviewTrackScaleMultiplier   = 1.157f * 0.95f
PreviewTrackEndScaleFraction  = 1.0f
```

环境变量：

| 变量 | 作用 |
|---|---|
| `INFALSUS_TRACK_END_SEED` | 固定三角形布局的随机种子（int） |
| `INFALSUS_CAPTURE_TRACK_END_MASK=1` | 截图时额外导出遮罩 PNG |
