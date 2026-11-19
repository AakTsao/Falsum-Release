# TrackEnd 依赖清单（完整）

> 统计基线：分支 `perf/render-hotpaths`，HEAD `0738600`，工作区含 2 个未提交改动
> （见 §6）。unity **6000.3.26f1**，URP **17.3.0**。

---

## 1. 核心文件（必须全部存在，缺一即失效）

| # | 文件 | 用途 | 必需性 |
|---|---|---|---|
| 1 | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs` | 运行时资源与几何拥有者 | **硬必需** |
| 2 | `.../Backgrounds/FalsumTrackEndComposerFeature.cs` | URP RendererFeature，每帧 Mask+Blur | **硬必需** |
| 3 | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader` | 3 Pass 遮罩 | **硬必需** |
| 4 | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader` | 2 Pass 五采样模糊 | **硬必需** |
| 5 | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader` | 最终透明合成 | **硬必需** |
| 6 | `Assets/StreamingAssets/Falsum/Playfield/playfieldBG.png` | 底图（1024×1024 RGBA） | **硬必需** |
| 7 | `Assets/Settings/FalsumTrackEndComposerFeature.asset` | Feature 的 ScriptableObject 实例 | **硬必需** |
| 8 | `Assets/Settings/PC_Renderer.asset` | 持有 Feature 引用的 URP Renderer | **硬必需** |

**注意 1-8 全部是「硬必需」**：Composer 对 1、3、4、5、6 逐项 `Find/Exists` 检查，
任一失败就 `LogError` 并整体放弃初始化；Feature 不在 Renderer 上则遮罩永远不会被
绘制（画面只有底图，没有菱形）。

## 2. 编辑器工具

| 文件 | 用途 |
|---|---|
| `Assets/InFalsus/Editor/FalsumTrackEndComposerInstaller.cs` | 把 Feature 装进 `PC_Renderer.asset`；`[InitializeOnLoad]` 兜底 + 菜单 `InFalsus/Gameplay/Repair TrackEnd Composer` |

安装/修复逻辑（`EnsureInstalled`）：先移除类型名以 `FalsumTrackEnd` 开头的
**旧 Feature**，再保证当前 Feature 恰好出现一次。因此**改名会触发自动清理**，
不要在类名上做前缀兼容。

## 3. 场景依赖

| 项 | 值 |
|---|---|
| 场景文件 | `Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity`（GUID `17978ab3e409bac4597af88f78899e52`） |
| 锚点路径 | `GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/v5-3R/Plane/PLANE_END` |
| 锚点 GameObject fileID | `782`（Transform `4062`），Layer `7` |
| 同名干扰节点 | `.../v5-3/Plane/PLANE_END`（GameObject `247` / Transform `4635`）**未被绑定** |
| Layer 31 | 遮罩渲染层 `HiddenMaskLayer`，由 Feature 手动 `DrawRenderer`，不需要进任何相机 Culling Mask |

详见 `06_scene_anchor/`。

## 4. 代码调用点（共 11 个文件，逐行）

### 4.1 运行时

| 文件 | 行 | 内容 |
|---|---|---|
| `Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs` | 24 | 字段 `_trackEndComposer` |
| 同上 | 67-74 | `PlayfieldVisible` 属性（读 `Visible`） |
| 同上 | 76-79 | `TrackEndComposer` 只读属性 |
| 同上 | 133 | `_trackEndAnchor = binding?.TrackEndAnchor;` |
| 同上 | 141-153 | **唯一实例化点**：`AddComponent` + `Initialize` |
| `Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs` | 52-54 | `TrackEndAnchorPath` 常量 |
| 同上 | 73 | `public Transform TrackEndAnchor { get; private set; }` |
| 同上 | 206-207 | `binding.TrackEndAnchor = Require(scene, TrackEndAnchorPath).transform;` |
| `Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs` | 312 | `SetPresentationTime(0.0)`（初始化时） |
| 同上 | 444 | `SetPresentationTime(seconds)`（每帧） |
| 同上 | 988 | `SetPresentationTime(0.0)`（重开谱面） |
| `Gameplay/Falsum/Runtime/FalsumChartEditor.cs` | 2831-2833 | 性能/调试摘要读 `TrackEndComposer` |
| 同上 | 4360-4363 | 预览取景读 `composer` 并 `SetPreviewFraming` |
| 同上 | 4377-4391 | `RestoreChartPreviewFraming` → `SetPreviewFraming(1.0f, 0.0f)` |
| 同上 | 4671 | 退出预览调用 `RestoreChartPreviewFraming()` |
| `Gameplay/Presentation/Tracks/MeasureLineRenderer.cs` | 73、398 | `ResolveTrackEndZ`（**同名但无关**：它是小节线的轨道末端 Z） |
| `Gameplay/Falsum/Runtime/FalsumEditorWorld.cs` | 673、4938 | 同上，`ResolveTrackEndZ` |

### 4.2 编辑器

| 文件 | 行 | 内容 |
|---|---|---|
| `Editor/FalsumBuild.cs` | 58 | 构建前 `FalsumTrackEndComposerInstaller.EnsureInstalled(true)` |
| `Editor/FalsumSelfCheck.cs` | 632-657 | 完整断言块（见 §5） |
| `Editor/FalsumBackgroundCapture.cs` | 170-181 | `INFALSUS_CAPTURE_TRACK_END_MASK=1` 时导出遮罩 |
| `Editor/GameplayPlayCapture.cs` | 147-174 | 运行时截图导出遮罩 |
| `Editor/FalsumHeavyChartPerformanceCapture.cs` | 30 | `INFALSUS_FORCE_TRACKEND_REBUILD` 常量 |
| 同上 | 110、226-245 | 反射写 `_maskDirty`（**该字段已不存在，见 §6**） |
| 同上 | 285 | 读 `FalsumTrackEndComposerFeature.MaskRebuildCount` |

> `FalsumEditorWorld.cs` / `MeasureLineRenderer.cs` 的 `ResolveTrackEndZ` 与
> `_trackEndZ` 是**小节线轨道末端**，名字里带 TrackEnd 但和本项目无关，
> 列在这里是为了避免复刻时误以为是依赖。

## 5. `FalsumSelfCheck` 断言块（`FalsumSelfCheck.cs:632-657`）

复刻时这就是一组现成的验收用例：

```csharp
// 贴图存在
Require(File.Exists(Path.Combine(Application.streamingAssetsPath,
        "Falsum", "Playfield", "playfieldBG.png")), ...);

// 三个 Shader 都能 Find 到
Shader maskShader    = Shader.Find("InFalsus/FalsumTrackEndDiamondMask");
Shader blurShader    = Shader.Find("InFalsus/FalsumTrackEndDiamondBlur");
Shader overlayShader = Shader.Find("InFalsus/FalsumTrackEndDiamondOverlay");

// Pass 数
Require(maskShader.passCount == 3, ...);
Require(blurShader.passCount == 2, ...);
Require(overlayShader.passCount >= 1, ...);

// _TimeOverride 属性存在
Material maskMaterial = new Material(maskShader);
Require(maskMaterial.HasProperty("_TimeOverride"), ...);

// 旧 shader 名必须查不到（防回流）
Require(Shader.Find("InFalsus/FalsumTrackEndMask")    == null, ...);
Require(Shader.Find("InFalsus/FalsumTrackEndBlur")    == null, ...);
Require(Shader.Find("InFalsus/FalsumTrackEndOverlay") == null, ...);

// Feature 恰好装一次
Require(FalsumTrackEndComposerInstaller.IsInstalled(out installerError), ...);

// 遮罩尺寸
Require(FalsumTrackEndComposer.GetMaskSize(3840) == new Vector2Int(1200, 800), ...);
Require(FalsumTrackEndComposer.GetMaskSize(2560) == new Vector2Int(900, 600), ...);

// 只显示下半部分
Require(FalsumTrackEndComposer.OutputUvMax == 0.5f, ...);

// 固定种子可复现、换种子会变
int a = ComputeTriangleLayoutHash(1337);
int b = ComputeTriangleLayoutHash(1337);
int c = ComputeTriangleLayoutHash(1338);
Require(a == b, ...); Require(a != c, ...);
```

## 6. 已知问题（复刻时要注意）

### 6.1 两个文件的未提交改动

工作区相对 `HEAD` 有 2 个改动，**本包导出的是工作区版本（含改动）**：

| 文件 | 改动 |
|---|---|
| `FalsumTrackEndComposer.cs` | +33 行：`TimeOverrideId`、`_presentationTimeSeconds`、`SetPresentationTime`、`PresentationTimeSeconds`、`CreateMaterials` 末尾补写一次 |
| `FalsumTrackEndDiamondMask.shader` | +14 行：`Properties` 声明 `_TimeOverride`、`CBUFFER` 声明、`TriangleOffset` 用它替换 `_Time.y` |

**必须两个一起用**。只给 C# 不给 Shader → `HasProperty` 为 false，每帧回退
`_Time.y`，暂停时粒子还在动。只给 Shader 不给 C# → `_TimeOverride` 恒为 `-1`，
行为等同旧代码，功能上能跑但丢掉了暂停/seek 语义。

### 6.2 `_maskDirty` 已被移除但还有代码反射它

`Editor/FalsumHeavyChartPerformanceCapture.cs:226-245`：

```csharp
var field = typeof(FalsumTrackEndComposer).GetField(
    "_maskDirty", BindingFlags.Instance | BindingFlags.NonPublic);
field?.SetValue(composer, true);
```

`FalsumTrackEndComposer` 里**没有** `_maskDirty` 字段（grep 全仓仅此一处引用），
所以这段是 `null` 安全的死代码。`INFALSUS_FORCE_TRACKEND_REBUILD=1` 现在
不起任何作用——遮罩本来就每帧重建。复刻时可以删掉，或保留不处理。

### 6.3 `FalsumSelfCheck` 当前整体不通过（与 TrackEnd 无关）

`Diagnostics/FalsumSelfCheck.json` 的 `passed:false` 卡在
`CheckToolRuntime` 的 tool.json 描述断言，**TrackEnd 这 25 项本身是通过的**。

## 7. 网络/包依赖

TrackEnd **没有**任何第三方包依赖，只用 URP 自带内容：

| 依赖 | 来源 | 用途 |
|---|---|---|
| `Packages/com.unity.render-pipelines.universal` | URP 17.3.0 | `ScriptableRendererFeature`、`ScriptableRenderPass`、`RenderPassEvent`、`UniversalRenderer.CreateRenderGraphTexture`、`UniversalCameraData` |
| `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl` | URP | HLSL 基础（Mask/Overlay） |
| `Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl` | Core RP | `Vert` / `Varyings` / `_BlitTexture` / `Blitter.BlitTexture`（Blur） |
| `UnityEngine.Rendering.RenderGraphModule` | Unity 6000.x 内置 | `RenderGraph`、`TextureHandle`、`RasterGraphContext` |
| `UnityEngine.Rendering.RTHandles` | Core RP | `RTHandles.Alloc` |
| `UnityEngine.Rendering.Blitter` | Core RP | `Blitter.BlitTexture` |
| `UnityEngine.ImageConversion`（`com.unity.modules.imageconversion`） | Unity 内置模块 | `ImageConversion.LoadImage` 解 PNG |

`Packages/manifest.json` 里 URP 与 shadergraph 都是 `17.3.0`，完整清单见
`07_full_dependents/manifest.json`。

### 7.1 项目设置依赖：Always Included Shaders（容易被忽略）

三个 TrackEnd Shader **没有任何 Material 资产引用**（全部在运行时
`new Material(Shader.Find(...))` 创建），因此必须登记到
`ProjectSettings/GraphicsSettings.asset` 的 `m_AlwaysIncludedShaders`，
否则构建时会被剥离，出包后 `Shader.Find` 返回 null。

原工程的登记位置 `GraphicsSettings.asset:40-42`：

```yaml
- {fileID: 4800000, guid: cda440bcf1eaddf4ea218fdd5d506d52, type: 3}  # DiamondMask
- {fileID: 4800000, guid: b4dc0ac89338a114bbfb4c54df08eab2, type: 3}  # DiamondBlur
- {fileID: 4800000, guid: 1e9353ecedd0dae44aa450604f8d13ec, type: 3}  # DiamondOverlay
```

该文件已随包提供（`03_config/GraphicsSettings.asset`），
它列出的 **14 个 Always Included Shader** 中有 3 个属于 TrackEnd，
另外 11 个属于工程其他部分（内置 Shader、SkyArea、MeasureLine、a7.shader），
**不要整体覆盖目标工程的文件**，只抄这 3 行。

## 8. 文件 GUID 对照（换工程时需要保持一致）

| 资产 | GUID |
|---|---|
| `FalsumTrackEndComposer.cs` | `06b85cf73a221ba4e9700d291fae593c` |
| `FalsumTrackEndComposerFeature.cs` | `87dafc005e9d266488f11f62729f736a` |
| `FalsumTrackEndComposerInstaller.cs` | `fb124df1d2b7f054f99cd59288687d0e` |
| `FalsumTrackEndDiamondMask.shader` | `cda440bcf1eaddf4ea218fdd5d506d52` |
| `FalsumTrackEndDiamondBlur.shader` | `b4dc0ac89338a114bbfb4c54df08eab2` |
| `FalsumTrackEndDiamondOverlay.shader` | `1e9353ecedd0dae44aa450604f8d13ec` |
| `FalsumTrackEndComposerFeature.asset` | `1492a48a019339145a4013148bb1fdba` |
| `PC_Renderer.asset` | `f288ae1f4751b564a96ac7587541f7a2` |
| `PC_RPAsset.asset` | （`PC_Renderer` 的引用方，见 `03_config/`） |
| `playfieldBG.png` | `bb8aad3598351a5488e96aaefbbefb72` |
| `GameplayRuntime.unity` | `17978ab3e409bac4597af88f78899e52` |
| `GraphicsSettings.asset`（ProjectSettings 下，无 .meta，Guid 由 Unity 内部维护） | — |

`FalsumTrackEndComposerFeature.asset` 的 `m_Script` 直接写死
`guid: 87dafc005e9d266488f11f62729f736a`（即 Feature 的 .cs.meta）。
**如果换了 Feature 脚本 GUID，必须同步改这个 asset，否则 Feature 变 Missing Script。**
