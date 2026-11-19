# 接入指南（把 TrackEnd 移植到别的 URP 工程）

> 目标：在一套全新的 Unity 6 + URP 17 工程里复刻出 TrackEnd 菱形动态背景。
> 源工程是 InFalsus-Gameplay，所有文件都在本包里，按下面顺序落地即可。

---

## 0. 前置条件

| 项 | 要求 |
|---|---|
| Unity | 6000.x（代码用了 `RenderGraph` API，URP 17 才有） |
| URP 包 | `com.unity.render-pipelines.universal@17.3.0`（`RenderGraphModule` 依赖） |
| Renderer 模式 | **必须**是 `UniversalRenderer`（`PC_Renderer.asset` 里的 `UniversalRendererData`），不是 2D Renderer |
| 相机 | 必须是透视相机（`camera.orthographic == false`），且是 `Camera.main` |

## 1. 拷贝文件（保持路径一致最省事）

```text
01_runtime/InFalsus/Gameplay/Presentation/Backgrounds/*.cs   →  同名 Assets 路径
01_runtime/InFalsus/Shaders/Compat/*.shader                  →  同名 Assets 路径
02_editor/InFalsus/Editor/*.cs                               →  同名 Assets 路径
03_config/FalsumTrackEndComposerFeature.asset                →  Assets/Settings/
04_textures/playfieldBG.png                                  →  Assets/StreamingAssets/Falsum/Playfield/
```

**每个 `.cs` / `.shader` 都要连 `.meta` 一起拷**，因为：

- `FalsumTrackEndComposerFeature.asset` 的 `m_Script` 写死了
  `guid: 87dafc005e9d266488f11f62729f736a`；
- 如果只拷 `.cs` 让 Unity 重新生成 GUID，asset 会变成 Missing Script。

如果目标工程的目录结构不同（比如没有 `InFalsus/` 命名空间层），可以换路径，
但 **`.meta` 里的 GUID 不能变**。拷贝 `.meta` 时保留 GUID 即可。

## 2. 配置 Renderer

### 2.1 挂 Feature（推荐：用 Installer 自动完成）

把 `FalsumTrackEndComposerInstaller.cs` 放到任意 `Editor/` 目录，改开头的两个常量：

```csharp
private const string RendererPath =
    "Assets/Settings/PC_Renderer.asset";          // ← 改成你的 Renderer 路径
private const string FeaturePath =
    "Assets/Settings/FalsumTrackEndComposerFeature.asset";
```

然后在 Unity 里执行菜单：**`InFalsus/Gameplay/Repair TrackEnd Composer`**。

它会：
1. 加载 `UniversalRendererData`；
2. 没有 Feature asset 就 `CreateInstance` + `CreateAsset`；
3. 移除所有类型名以 `FalsumTrackEnd` 开头的旧 Feature；
4. 保证当前 Feature 恰好出现一次；
5. `SetDirty` + `SaveAssets`。

`[InitializeOnLoad]` 静态构造在**非 batch 模式**下 `delayCall` 会自动跑一次
`EnsureInstalled(false)`，所以正常情况下打开工程就装好了。

### 2.2 手动挂（等价的 GUI 操作）

```text
选中 PC_Renderer.asset
→ Inspector 最下方 Add Renderer Feature
→ 选 FalsumTrackEndComposerFeature
→ 确保它只出现一次
```

### 2.3 确认 RP Asset 指向这个 Renderer

`PC_RPAsset.asset` 的 `m_RendererDataList` 第一项必须是 `PC_Renderer.asset`
（GUID `f288ae1f4751b564a96ac7587541f7a2`）。参考 `03_config/PC_RPAsset.asset:19-20`：

```yaml
m_RendererDataList:
- {fileID: 11400000, guid: f288ae1f4751b564a96ac7587541f7a2, type: 2}
```

再确认 Graphics Settings / Quality Settings 里用的是这个 RP Asset。

## 3. 准备场景锚点

TrackEnd 需要场景里有一个 `Transform` 作为锚点。两种做法：

### 3.1 沿用原路径（直接拷场景）

如果连 `GameplayRuntime.unity` 一起用，路径已经存在：

```text
GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/v5-3R/Plane/PLANE_END
```

注意场景里**有两个** `PLANE_END`（`v5-3` 和 `v5-3R`），只有 `v5-3R` 那支被绑定。
详见 `06_scene_anchor/README_scene_anchor.md`。

### 3.2 换成自己的锚点

改 `GameplaySceneBinding.cs:52-54` 的常量，或在自己的代码里直接把一个
`Transform` 传给 `Initialize`：

```csharp
var composer = gameObject.AddComponent<FalsumTrackEndComposer>();
composer.Initialize(myTrackEndTransform);
```

锚点的语义是"菱形层在屏幕上的定位参考点"，要求：

- 位于相机前方（`WorldToViewportPoint(anchor.position).z > nearClipPlane`）；
- 相机旋转变化时会跟着改变投影位置（所以挂在一个"世界空间固定点"就行，
  本例是轨道尽头）。

## 4. 接好时间驱动（关键）

**不做这一步也能显示，但粒子会一直动、暂停/seek 失效。**

在每帧更新里写时间：

```csharp
// 你的表现时钟（单调递增、暂停时冻结），下面用 seconds 代表
_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(seconds);
```

同时在"重开谱面/重进场景"时归零：

```csharp
_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(0.0);
```

原工程的两个位置：`GameplayBootstrap.cs:312`（初始化）、`:444`（每帧）、
`:988`（重开）。参考 `05_integration/03_*.cs` 与 `04_*.cs`。

> 配方速记：**暂停 → 时钟不涨 → 相位冻结；Seek → 时钟不重置 → 相位不跳。**

## 5. 准备底图

必须放在 **`StreamingAssets/Falsum/Playfield/playfieldBG.png`**（路径硬编码在
`FalsumTrackEndComposer.cs:11-12`）：

```csharp
private const string PlayfieldDirectory = "Falsum/Playfield";
private const string PlayfieldFile = "playfieldBG.png";
```

### 关于贴图本身

| 属性 | 值 |
|---|---|
| 尺寸 | 1024 × 1024 |
| 格式 | PNG，8-bit，Color Type 6（RGBA） |
| 大小 | 917,452 字节 |
| MD5 | `cb795c6e8eb5911c490bc609b4da81b5` |
| 导入设置 | `DefaultImporter`（StreamingAssets 不参与导入，运行时按字节读） |
| 运行时加载 | `File.ReadAllBytes` + `ImageConversion.LoadImage` |
| 运行时 wrapMode | **`TextureWrapMode.Mirror`（代码硬写）** |
| 运行时 filterMode | `Bilinear` |

`playfieldBG` 是一张 **上下镜像对称的菱形网格 + 底部三角粒子** 的底图。
上半部分永远不会显示在屏幕上（`OutputUvMax = 0.5`），但 **必须保留**：
Overlay 用 `uv + (0, 0.5)` 采样它，正是靠这张图的上下两部分做合成。

如果你要换自己的底图：

1. 保持 1:1 正方形（代码里 `fullHeightFraction` 与宽高比有关系，非方图会变形）；
2. 保持上下边缘可镜像（`Mirror` 环绕，上下不匹配会在 V 跨界处出横向接缝）；
3. 分辨率可以任意，但越小粒子越糊。

## 6. 验证

### 6.1 静态检查（不用跑游戏）

在 Unity Console 里应**没有**这些报错：

```text
[Falsum] TrackEnd playfield image is missing: ...
[Falsum] TrackEnd mask shader is missing: InFalsus/FalsumTrackEndDiamondMask
[Falsum] TrackEnd blur shader is missing: InFalsus/FalsumTrackEndDiamondBlur
[Falsum] TrackEnd overlay shader is missing: InFalsus/FalsumTrackEndDiamondOverlay
[Falsum] TrackEnd composer is missing its anchor.
[Falsum] TrackEnd mask RT creation failed: ...
```

三条 `Shader.Find` 必须在**首次 `Initialize` 之前**就成功，而这三个 Shader
**没有任何 Material 资产引用它们**（全部 `new Material(shader)` 运行时创建），
所以它们**必须**被登记进 Always Included Shaders，否则会被构建剥离，
运行时报 `Shader is missing`。

原工程的做法（`ProjectSettings/GraphicsSettings.asset:29-43`，
`m_AlwaysIncludedShaders` 共 14 项，其中 3 项是 TrackEnd）：

```yaml
m_AlwaysIncludedShaders:
- {fileID: 7,    guid: 0000000000000000f000000000000000, type: 0}   # 内置
... （其余内置项省略）...
- {fileID: 4800000, guid: 0533dccb4ecbc1a4da648743d8fc5de4, type: 3}  # SkyAreaPrompt
- {fileID: 4800000, guid: 677e1f951de339f42be481acc1958f97, type: 3}  # MeasureLine
- {fileID: 4800000, guid: e7e1915fd7984d34ca40c808c48c0fed, type: 3}  # 内置
- {fileID: 4800000, guid: cda440bcf1eaddf4ea218fdd5d506d52, type: 3}  # ★ DiamondMask
- {fileID: 4800000, guid: b4dc0ac89338a114bbfb4c54df08eab2, type: 3}  # ★ DiamondBlur
- {fileID: 4800000, guid: 1e9353ecedd0dae44aa450604f8d13ec, type: 3}  # ★ DiamondOverlay
- {fileID: 4800000, guid: 7ab49a0cdf61f6e47a6f860802539df9, type: 3}  # a7.shader
```

**移植时必须做这一步。** 三种做法任选：

1. **GUI（推荐）**：`Project Settings → Graphics → Always Included Shaders`，
   把三个 `FalsumTrackEndDiamond*.shader` 拖进去，数量从 0 变 3。
2. **拷配置**：把 `03_config/GraphicsSettings.asset` 里的对应 3 行 YAML 抄进
   你工程的同名文件（保持 GUID，因为本包的 `.shader.meta` 和它一致）。
3. **建占位 Material**：新建一个 `.mat` 依次引用三个 Shader（一个材质只能引一个，
   所以需要 3 个），放进任意被引用的资产里。不推荐，容易误删。

> 验证方法：Editor 里一切都好，**只有出包后**才报
> `[Falsum] TrackEnd mask shader is missing`，几乎肯定是漏了这一步。

### 6.2 运行时快速自检

```csharp
var composer = FalsumTrackEndComposer.Active;
Debug.Log(composer != null && composer.IsReady);   // 应为 true
Debug.Log(composer.Visible);                        // 相机正常时应为 true
Debug.Log(composer.MaskWidth + "x" + composer.MaskHeight);
```

### 6.3 导出遮罩看链路

`SaveMaskForDiagnostics` 把当前 Mask RT 存成 PNG，能直观看出三个 Pass 是否都画上了：

```csharp
composer.SaveMaskForDiagnostics(@"C:\temp\trackend_mask.png");
```

期望看到：**中央白色六边形填满 + 左右下角若干白色三角**。
如果全黑 → Feature 没挂 / 没被 Enqueue / Draw 顺序错。
如果只有六边形没三角 → Mask Shader 的 Pass 索引或顶点着色器坏了。
如果三角位置全部堆在中央 → `uv0.x` 种子没写进去。

原工程的自动化版本（环境变量 `INFALSUS_CAPTURE_TRACK_END_MASK=1`）见
`05_integration/10_*.cs` 与 `11_*.cs`。

### 6.4 回归断言（照抄 `FalsumSelfCheck.cs:632-657`）

见 `00_docs/TrackEnd_依赖清单.md` §5，共 14 条 `Require`。

## 7. 常见失败模式

| 症状 | 原因 | 修法 |
|---|---|---|
| 完全没有菱形层，只看到底图 | Feature 没挂在 Renderer 上 | 跑 Installer / 手动 Add Renderer Feature |
| 画面全黑一块 | Mask RT 全 0，Draw 没执行 | 检查 `AllowPassCulling(false)`；检查是不是 `Camera.main` |
| 粒子不动 | `_TimeOverride` 没被写入 | 检查是否每帧调 `SetPresentationTime`；检查 Shader 有没有该属性 |
| 暂停后粒子还在动 | 同上，回退到了 `_Time.y` | 同上 |
| 有内侧亮线 | Overlay 用了旧公式 | 用本包 Overlay（只有 `outerFade * 0.055`） |
| 底图上出现横向接缝 | wrapMode 被改成 Repeat | 必须 `Mirror`（`FalsumTrackEndComposer.cs:415`） |
| 只显示上半张图 | UV 用了 0..1 | 输出 Quad UV 的 V 上限必须是 `0.5` |
| 画面上下颠倒/错位 | 采样没做 `uv + (0, 0.5)` | Overlay 里必须偏移 |
| 菱形层被背景/频谱盖住 | renderQueue 顺序 | 背景 2900 < TrackEnd 输出 **2950** < 频谱 3002 |
| blob 边缘太硬 | 模糊链没跑完 7 段 | 检查 Feature 的 7 次 `AddBlit` 顺序 |
| 分辨率变化后糊掉/拉伸 | 没重建 RT | `Update` 里按 `camera.pixelWidth` 重建（`:335`） |

## 8. 最小落地清单

```text
[ ] 8 个核心文件全部拷入（含 .meta，GUID 不变）
[ ] Shader 名与 C# 常量一致（InFalsus/FalsumTrackEndDiamondMask|Blur|Overlay）
[ ] 三个 Shader 已加入 Always Included Shaders（否则出包后丢失）
[ ] Renderer 上恰好一个 FalsumTrackEndComposerFeature
[ ] RP Asset 指向该 Renderer
[ ] 场景里有锚点 Transform，并传给 Initialize
[ ] StreamingAssets/Falsum/Playfield/playfieldBG.png 存在
[ ] 每帧调 SetPresentationTime(seconds)，重开时归零
[ ] 相机是透视 + Camera.main
[ ] SaveMaskForDiagnostics 导出的遮罩里能看到六边形 + 三角
```
