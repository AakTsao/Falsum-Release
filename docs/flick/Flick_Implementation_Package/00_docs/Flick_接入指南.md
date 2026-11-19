# Flick 接入指南（移植 / 排错）

> 目标读者：拿到 `Flick_Implementation_Package`，想把它接到**另一个 Unity URP 工程**里，
> 或者在本工程里排查 Flick 音符渲染问题的人。
>
> 本文所有路径、行号、菜单名、GUID 都对着本仓库**实际代码**核对过（2026-11-15）。
> 凡与 `design/Flick_*_Fix.md` 四篇历史修复报告冲突的地方，**以代码为准**，本文会明确标注冲突。

---

## 0. 先读这一段：文档与代码已知不符的三处

四篇 `design/` 文档是**修复过程记录**，不是当前状态快照。落地时请以本节为准：

| # | 文档怎么说 | 代码实际是什么 | 影响 |
|---|---|---|---|
| 1 | `Flick_Round5_Fix.md` §1：「把箭头材质 `Mat.001` / `Mat.001 R` 的 renderQueue 从 3012 提到 **3014**，箭头必定盖住尾巴」 | **改动没有落地**。两个材质当前都是 `m_CustomRenderQueue: 3012`、`_QueueOffset: 12`（URP Unlit：3000+12=3012），与尾巴 `FlickV5` 的 3012 **同队列**。`git show HEAD:` 也是 3012，`git log -S "3014"` 对该文件零命中，全工程 `Recovered/` 与 `*.cs` 里 grep `3014` 零命中。 | 「箭头被尾巴盖住」这个症状**现在依然可能发生**。详见 §6 第 7 条。 |
| 2 | 工程内文档（`docs/memory/02-invariants.md:465`、`appendix/detail-rendering.md:75`）一直写「`NoteRenderer` **13 参**构造」 | 实际签名是 **14 个参数**：`binding, motion, chart, skyPrefab, skyAreaMaterial, holdPrefab, flickLeftPrefab, flickRightPrefab, tapPrefab, tapSpanPrefab, sideLeftTapPrefab, sideRightTapPrefab, sideLeftHoldPrefab, sideRightHoldPrefab`（`NoteRenderer.cs:462`）。flick 的两支在**第 7、8 位**。 | 按「13 参」去找参数位会数错。 |
| 3 | `Flick_Stripe_Fix.md` / `Flick_Tail_Fix.md` / `Flick_Round5_Fix.md` 的「调参速查」写 `_BarRepeat 8.0`、`_TailLift 1.7`、`_TailDetint 0.55`、`_GlowPower 2.2`、`FlickTailHeightScale 1.0` | 后续 r8–r13 又改过，**当前**默认值见 §6 末尾「参数现值表」：`_BarRepeat 10.0`、`_TailLift 1.45`、`_TailDetint 0.40`、`_GlowPower 1.8`、`FlickTailHeightScale 0.93`。 | 照抄旧文档的默认值会和工程对不上。 |

另外两个「死代码」提示，便于移植时不要被误导：

- `NoteRenderer.cs:218` 的 `FlickHeadOverlap = 0.08f` **已无任何引用**（`grep -rn FlickHeadOverlap` 只命中定义行本身）。
  它就是对 `Flick_Tail_Fix.md` §1 那个「±0.08 硬编码」的遗骸。**不要再把它接回去**。
- `NoteRenderer.cs:225` 的 `FlickVertexSeamOffset = 0.0f` 是 r8 删掉「把尾巴顶点往箭头里推 3 mm」之后留下的显式旋钮，
  **必须保持 0**，改成非 0 会重新产生「尾巴从箭头里透出来」。

---

## 1. 前置条件

| 项 | 要求 | 本工程实测值 / 出处 |
|---|---|---|
| Unity | **6000.3.26f1** | `ProjectSettings/ProjectVersion.txt` → `m_EditorVersion: 6000.3.26f1` |
| URP 包 | **`com.unity.render-pipelines.universal@17.3.0`** | `Packages/manifest.json:4` |
| Renderer 类型 | **`UniversalRenderer`**（不是 2D Renderer） | `Assets/Settings/PC_Renderer.asset`（`UniversalRendererData`） |
| 相机 HDR | 必须开 | 场景里唯一相机 `CameraPerspective`（tag `MainCamera`）`m_HDR: 1` |
| 相机后处理 | 必须开 | 同一相机 `m_RenderPostProcessing: 1` |
| Bloom | **必须有，且 intensity > 0** | `Assets/Settings/DefaultVolumeProfile.asset:358` 起 `m_Name: Bloom`，`threshold 1.5` / `intensity 2` / `scatter 0.821`；该 profile 由 `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset:363-365` 的 `URPDefaultVolumeProfileSettings.m_VolumeProfile` 引用 |
| Shader 模型 | `#pragma target 3.0`（`dc.shader:144`） | 需要支持 `fwidth`（`Infalsus_Common.hlsl` 的 `IF_SoftBand`） |
| MaterialPropertyBlock | 必须可用，且 shader **不能被 SRP Batcher 拒绝** | 见下方说明 |
| 网格可读 | `Mesh/Plane.asset` 与 `Mesh/Flick.asset` 必须 `m_IsReadable: 1` | `AlignFlickPlane` 会**改写实例 mesh 顶点** |

### 1.1 为什么要求「MaterialPropertyBlock 可用」

Flick 的**颜色和斜纹密度是每音符写入的**，不是材质里写死的：

- `NoteRendererPropertyState.cs:23` `BarRepeatId = Shader.PropertyToID("_BarRepeat")`
- 同文件 `:72-73` 写颜色：`direction == 4 ? (0.2, 0.95, 0.55) : (1, 0.82, 0.18)`
- 同文件 `:97` 写 `_BarRepeat`；`NoteRenderer.cs:2632-2633` 每帧算 `repeat = FlickTailWorldLength * FlickStripesPerUnit`

所以**尾巴用的那个 shader（本工程是 `dc.shader`）必须声明 `_Color` / `_Opacity` / `_BarRepeat` 三个属性**，
否则 `NoteRendererPropertyState.Cache()`（`:109-112`）的 `HasProperty` 检查会静默跳过 ——
表现是「尾巴全白」或「斜纹密度永远等于材质默认值」，**不报错**。
（箭头那层用 URP Unlit，只吃 `_BaseColor`，不吃这三个 —— 详见 §1.2。）

这三个属性都是 **per-renderer 覆盖**（MaterialPropertyBlock），若 shader 被 SRP Batcher 判为不兼容，覆盖会失效。
`dc.shader` 已经把所有属性放进 `CBUFFER_START(UnityPerMaterial)`（`:156-178`），
所以只要你保留这个 CBUFFER 结构，SRP Batcher 就是兼容的。

⚠ 但注意：`Apply()` 是**对每个 Renderer 都调一遍**的（`NoteRenderer.cs:2650-2654` 遍历 `visual.PropertyStates`），
而一个 Flick 有两个 Renderer（尾巴 + 箭头）。所以 `HasProperty` 那层过滤是必要的 ——
它保证「箭头那一层没有 `_BarRepeat` 时不会写一个不存在的属性」。下一节说明这意味着什么。

### 1.2 一个反直觉的事实：箭头那一层**不吃**运行时染色

这一点很容易让人以为「颜色写错了」，所以单独列出来。

Flick 一个音符由**两个 Renderer** 组成，两者的 shader 消费的属性集**不同**：

| 层 | 材质 | shader | 消费 `_Color` | 消费 `_Opacity` | 消费 `_BarRepeat` |
|---|---|---|---|---|---|
| 尾巴 `Plane` | `FlickV5 Left/Right` | `dc.shader`（`InFalsus/Replica/FlickV5`） | ✅ `:254` `rgb *= _Color.rgb` | ✅ `:246` | ✅ `:227` |
| 箭头 `Flick` | `Mat.001` / `Mat.001 R` | URP Unlit | ❌ | ❌ | ❌ |

URP Unlit 的片元只有 `texColor.rgb * _BaseColor.rgb` / `texColor.a * _BaseColor.a`
（`UnlitForwardPass.hlsl:109-110`）；它 Properties 里那个 `_Color`（`Unlit.shader:29`）是
`[HideInInspector]` 的**废弃字段**，不参与任何运算；`_Opacity` 与 `_BarRepeat` 则**根本没声明**。

`NoteRendererPropertyState.Apply()` 的 Flick 分支（`:77`）只写 `Opacity | Color | BarRepeat` 三个，
所以对箭头 Renderer 来说：`_Color` 写了但 shader 不用，`_Opacity`/`_BarRepeat` 被
`Cache()`（`:109-112`）的 `HasProperty` 检查直接跳过。

**结论**：**箭头的颜色完全来自贴图**（`flickv5.png` / `FlickNoteCapR.png` 本身就是预染色的黄/绿图集），
运行时染的 `(1,0.82,0.18)` / `(0.2,0.95,0.55)` **只作用于尾巴**。
这也解释了为什么「尾巴要 `_TailDetint` 去染色、箭头不用」—— 箭头压根没被染色。

> 推论（排错时别被带偏）：**如果箭头颜色不对，去查贴图有没有拷错/拷反，不要去调 `_Color`。**
> 另外 fade 也不作用于箭头：淡出时尾巴变淡、箭头保持原 alpha。这是当前实现的实际行为。

### 1.3 一个容易踩的坑：`_Bar` 必须用 Repeat 采样器

`dc.shader:235` 是 `SAMPLE_TEXTURE2D(_Bar, sampler_Bar, uvBar)`，
`sampler_Bar` 由 `SAMPLER()` 宏声明 —— **采样器的 wrap 模式来自贴图导入设置**，不是 shader 能决定的。
`_Base` / `_Core` 用 `sampler_Base` / `sampler_Core`，走 Clamp。

---

## 2. 最小落地清单（在别的 URP 工程里把 Flick 跑起来）

包内目录与职责：

```text
Flick_Implementation_Package/
├── 00_docs/            本文 + 依赖清单 + 贴图清单 + design/（四篇历史修复报告）
├── 01_shader/          dc.shader + Infalsus_Common.hlsl（两个必须同目录）
├── 02_runtime/         运行时必需 4 个 .cs（NoteRenderer / PropertyState / FlickHitEffectRenderer / GameplayHitAudio）
├── 03_data/            谱面模型、解析、写谱、判定（Flick 的语义真源）
├── 04_assets/          ★ 原始子目录结构保留：Material/ Mesh/ Texture2D/ Sprite/ GameObject/
│                       AnimationClip/ AnimatorController/ AnimatorOverrideController/ Shader/ Audio/
├── 05_config/          VolumeProfile / RPAsset / Renderer / GlobalSettings / manifest / ProjectVersion
├── 06_integration/     28 个「逐字摘录」代码片段（改哪个文件的哪一段，按序号抄）
└── 07_full_dependents/ 整文件依赖（GameplayBootstrap / GameplaySceneBinding / GameplayRuntime.unity …）
```

**总原则：`.meta` 必须跟着文件一起拷。** guid 变了 → 材质断链 → 粉色/丢贴图，且**不会有编译错误**。

### 步骤 1 — 拷 shader（两个文件，必须同目录）

| 放哪里 | 放什么 | 为什么 |
|---|---|---|
| `Assets/InFalsus/Recovered/` | `01_shader/dc.shader` + `dc.shader.meta` | shader 名 `InFalsus/Replica/FlickV5`（`dc.shader:57`） |
| `Assets/InFalsus/Recovered/` | `01_shader/Infalsus_Common.hlsl` + `.meta` | `dc.shader:150` 是 `#include "Infalsus_Common.hlsl"`，**相对路径**，必须与 shader 同目录 |

`dc.shader` 另外依赖 `#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"`（`:149`），
只要装了 URP 17 就有，不用拷。

**验证**：Console 无 shader 编译错误；把 `FlickV5 Left.mat` 拖到 Inspector 里，
Shader 槽应显示 **`InFalsus/Replica/FlickV5`**（不是 `Hidden/InternalErrorShader`，也不是空白）。

### 步骤 2 — 拷贴图（13 张，导入设置不能错）

全部放 `Assets/InFalsus/Recovered/Texture2D/`，**带 `.meta`**。Flick 音符本体只用其中 6 张：

| 文件 | 尺寸 | 用于 | wrap | mip | filter |
|---|---|---|---|---|---|
| `FlickNote-Base.png` | 2048×128 | `FlickV5 Left.mat` → `_Base` | Clamp(1/1) | on | Trilinear(2) |
| `FlickNote-Bar.png` | 64×128 | `FlickV5 Left.mat` → `_Bar` | **Repeat(0/0)** ⚠ | **off** | Bilinear(1) |
| `FlickNote-Core.png` | 2048×128 | `FlickV5 Left.mat` → `_Core` | Clamp(1/1) | on | Trilinear(2) |
| `FlickNoteR-Base.png` | 2048×128 | `FlickV5 Right.mat` → `_Base` | Clamp(1/1) | on | Trilinear(2) |
| `FlickNoteR-Bar.png` | 64×128 | `FlickV5 Right.mat` → `_Bar` | **Repeat(0/0)** ⚠ | **off** | Bilinear(1) |
| `FlickNoteR-Core.png` | 2048×128 | `FlickV5 Right.mat` → `_Core` | Clamp(1/1) | on | Trilinear(2) |

箭头层另有 2 张（见步骤 4）：`flickv5.png`（2048×2048，Repeat，mip on）、`FlickNoteCapR.png`（2048×2048，Clamp）。
命中特效另有 2 张：`FlickBurst.png` / `FlickBurstR.png`（各 4096×2048，Clamp，`textureType: 8` = Sprite 2D，`spriteMode: 2` = Multiple）。
剩下 3 张（`cursor-flick-indicator-left/right.png`、`notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png`）是**未接线**的恢复资产，可选。

**验证**：见 §5 的 `.meta` 字段表和自检命令。

### 步骤 3 — 拷材质（4 个必需）、mesh（2 个必需）、prefab（2 个）

| 放哪里 | 放什么 | guid（本工程） |
|---|---|---|
| `Assets/InFalsus/Recovered/Material/` | `FlickV5 Left.mat` | `5ce7c37f26e917d499575f6e71d17b34` |
| 同上 | `FlickV5 Right.mat` | `96bf87a014d661d4f8e5c1b55e79fe18` |
| 同上 | `Mat.001.mat`（箭头，左/黄） | `5b65e1ef0a12ed24380b367ec857314d` |
| 同上 | `Mat.001 R.mat`（箭头，右/绿） | `65cb8f12c4a4fd64dbacf621e55977e4` |
| `Assets/InFalsus/Recovered/Mesh/` | `Flick.asset`（箭头 0.232×0.160，24 顶点） | `2ba5deb3d2fba2e4f9188d6121b2958a` |
| 同上 | `Plane.asset`（尾巴 1.536×0.146，4 顶点） | `cb51b6c7bce3f9b478194025ef511b05` |
| `Assets/InFalsus/Recovered/GameObject/` | `v5-3.prefab`（左/黄） | `c19bfe2a8cc8c2b4781ed2d7fd72fcbe` |
| 同上 | `v5-3R.prefab`（右/绿） | `23de1814ce5aa2a45ac1f402234a489f` |

可选（原版恢复资产，当前未接线）：`matid_flicknote.mat`、`cursor-flick-indicator-left/right.mat`、`FlickBurstMaterial.mat`、
`flicknote01.asset`、`flickunderscore01.asset`、`Shader/9a.shader`（= `InFalsus/Compat/StenciledUnlitSprite`）、
`Shader/Universal Render Pipeline_Unlit_1.shader`（见步骤 4 注意事项）。

`v5-3.prefab` 的内部结构（**名字不能改**，代码靠名字找子节点）：

```text
v5-3                       root, rot = quat(0,1,0,0) 即绕 Y 180°, scale (1,1,1)
├── Flick                  localPos (-0.799, -0.045, 0), rot identity, scale (1,1,1)
│   └── MeshFilter → Mesh/Flick.asset ; MeshRenderer → Mat.001.mat
└── Plane                  localPos (-0.791, -0.042, 0.0008153589), rot ≈ Z 180°, scale (-1,-1,-1)
    ├── MeshFilter → Mesh/Plane.asset ; MeshRenderer → FlickV5 Left.mat
    ├── PLANE_END
    └── PLANE_START
```

`v5-3R.prefab` 同构，只把材质换成 `Mat.001 R.mat` + `FlickV5 Right.mat`。

**验证**：双击 prefab，场景/预览里应能看到「箭头 + 长条斜纹尾巴」两件东西；
选中 `Plane` 看 MeshRenderer 的 Material 不是 `Missing`。

### 步骤 4 — 箭头材质用的是 URP Unlit，注意拷贝策略

`Mat.001.mat` / `Mat.001 R.mat` 的 shader 是 `f75247fd87768534bb91926c2f61d0e4` =
`Assets/InFalsus/Recovered/Shader/Universal Render Pipeline_Unlit_1.shader`。

**这个文件是 URP 内置 `Universal Render Pipeline/Unlit` 的逐字节副本**（已 `cmp` 验证与
`Library/PackageCache/.../Shaders/Unlit.shader` 完全一致），只是为了保住原版 guid 才单独恢复了一份。

所以目标工程有两条路，任选：

- **A（省事）**：不拷这个 shader，直接把两个材质里那一行改成 URP 内置 shader：
  ```yaml
  m_Shader: {fileID: 4800000, guid: 650dd9526735d5b46b79224bc6e94025, type: 3}   # ← 目标工程里 URP Unlit 的 guid
  ```
  （`650dd952...` 是 URP 17.3.0 包内 `Unlit.shader.meta` 的 guid；不同 URP 小版本可能不同，**以目标工程为准**。）
- **B（保 guid）**：连 `.shader` + `.meta` 一起拷，材质不用动。

两个材质的透明状态（不要改）：`m_Surface: 1`（Transparent）、`_SrcBlend: 5`、`_DstBlend: 10`、
`_ZWrite: 0`、`m_CustomRenderQueue: 3012`、`_QueueOffset: 12`（URP Unlit 的 SubShader `Queue` 是 Geometry 2000，
Inspector 按 `Surface=Transparent` 算 `3000 + _QueueOffset 12 = 3012` 写入 `m_CustomRenderQueue`）。

### 步骤 5 — 场景锚点 `FLICK_NOTE`

`GameplaySceneBinding.Resolve()` 用**硬编码路径** `Require()` 取模板节点，取不到直接抛异常：

- 模板锚点：`GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/FLICK_NOTE`（`GameplaySceneBinding.cs:225-227`）
- 取不到时的报错原文：`Required gameplay object is missing: <path> (scene=<name>)`（`:372-374`）

把 `v5-3` / `v5-3R` 挂到这个节点下即可（本工程里 `FLICK_NOTE` 下有两个子节点 `flicknote01` / `flickunderscore01`，
那是原版占位件，**不是** flick 模板 —— 真正的模板是 Bootstrap 的序列化字段，见步骤 6）。

该模板节点会在 `ApplyPhaseAPolicy()` 里被 `SetActive(false)`（`GameplaySceneBinding.cs:292-295`），
所以**场景里看不到它是正常的**，`Instantiate` 仍然可用。

**验证**：跑一次 `InFalsus/Gameplay/Report Scene Bindings`（`GameplaySceneBuilder.cs:268`），
生成的 `Diagnostics/SceneBindings.json` / `Diagnostics/SceneBindingReport.txt` 里能看到这条路径。

### 步骤 6 — 把 prefab 接进 Bootstrap 的序列化字段

| 改哪个文件 | 改哪一行 | 内容 |
|---|---|---|
| `GameplayBootstrap.cs` | `:74` / `:76` | `public GameObject flickLeftPrefab;` / `public GameObject flickRightPrefab;`（在 `[Header("Presentation")]` 下） |
| `GameplayRuntime.unity`（场景） | Bootstrap 组件的序列化块 | `flickLeftPrefab: {fileID: 1709254077376921, guid: c19bfe2a8cc8c2b4781ed2d7fd72fcbe, type: 3}`、`flickRightPrefab: {fileID: 1709254077376921, guid: 23de1814ce5aa2a45ac1f402234a489f, type: 3}` |
| `Editor/GameplaySceneBuilder.cs` | `:36-39`（路径常量）、`:158-161`（赋值）、`:199-200`（缺失断言） | 编辑器侧的自动装配入口 |

也可以不手改场景，直接跑菜单 `InFalsus/Gameplay/Build Runtime Scene`（`GameplaySceneBuilder.cs:126`）让它重灌一遍。

**验证**：选中场景里 Bootstrap 组件，`Flick Left Prefab` / `Flick Right Prefab` 两栏都不为空；
空的话运行时会抛 `No visual template for note <id> kind=Flick`（`NoteRenderer.cs:1557`）。

### 步骤 7 — 打进 `NoteRenderer` 构造（14 参，flick 在第 7、8 位）

| 改哪个文件 | 改哪一行 | 内容 |
|---|---|---|
| `NoteRenderer.cs` | `:462` | 构造函数签名，flick 两个参数是第 7、8 个 |
| `NoteRenderer.cs` | `:510-511` | `_flickLeftPrefab = flickLeftPrefab;` / `_flickRightPrefab = flickRightPrefab;` |
| `NoteRenderer.cs` | `:297` / `:299` | 两个 readonly 字段声明 |
| `GameplayBootstrap.cs` | `:301` | 唯一的实例化点，14 个实参按序传入 |

```csharp
// GameplayBootstrap.cs:301（逐字，勿改顺序）
_noteRenderer = new NoteRenderer(_binding, _motion, _chart, skyAreaPrefab, skyAreaMaterial,
    holdPrefab, flickLeftPrefab, flickRightPrefab, tapPrefab, tapSpanPrefab,
    sideLeftTapPrefab, sideRightTapPrefab, sideLeftHoldPrefab, sideRightHoldPrefab);
```

**验证**：编译通过；运行时 `Diagnostics/SceneBindings.json` 的 renderer 快照里能看到
`v5-3/Plane → FlickV5 Left` 与 `v5-3/Flick → Mat.001`。

### 步骤 8 — 注册 `FlickHitEffectRenderer`（命中特效）

| 改哪个文件 | 改哪一行 | 内容 |
|---|---|---|
| `GameplayBootstrap.cs` | `:118` | `private FlickHitEffectRenderer _flickEffects;` |
| `GameplayBootstrap.cs` | `:306-307` | `AddComponent<FlickHitEffectRenderer>()` + `Initialize(...)` 7 个参数 |
| `GameplayBootstrap.cs` | `:419-421` | 每帧 `Tick(_clock.TimeMs, !_clock.IsPlaying)` —— **注意它在「是否播放」判断之外**，暂停时必须继续 Tick 以便特效正常收尾 |
| `GameplayBootstrap.cs` | `:511-513` / `:679-681` | `RebuildChartRuntime` 后必须 `SetEngine(_engine)` 重绑（判定引擎换实例了） |
| `GameplayBootstrap.cs` | `:618-620` / `:974-976` | 重开 / Seek 时 `ResetVisuals()` |
| `FlickHitEffectRenderer.cs` | `:82-119` | `Initialize(JudgementEngine, GameplaySceneBinding, Texture2D×4, Material)` |
| `GameplaySceneBinding.cs` | `:55-58` | 两个 burst 锚点路径常量 |
| `GameplaySceneBinding.cs` | `:240-245` | `Require()` 绑定，缺失即抛 |

Burst 锚点（场景里必须存在，各挂 `Animator` + `SpriteRenderer`）：

```text
GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/FlickBurst_0     ← 左/黄
GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/FlickBurstR_0    ← 右/绿
```

运行时 `FlickHitEffectRenderer.CreateBurstMaterial()`（`:168-195`）会**丢弃场景里的材质**，改用
`Shader.Find("InFalsus/Compat/StenciledUnlitSprite")`（找不到则回退 `Sprites/Default`），
`renderQueue = 3015`、`_ZTest = Always`。所以：

- 目标工程里**必须有** `InFalsus/Compat/StenciledUnlitSprite` 这个 shader（包内 `04_assets/Shader/9a.shader`），
  否则要么回退到 `Sprites/Default`（效果会变），要么抛 `Unable to find a shader for the FlickBurst effect.`（`:177-179`）。
- 该 shader 的**保活机制很容易移植时丢掉**。实测（2026-11-15）：
  - 它**不在** `m_AlwaysIncludedShaders` 里（`ProjectSettings/GraphicsSettings.asset:29-43` 共 14 项，
    guid `29378f1c321fc3d4581007835b579e98` **零命中**）；
  - 它靠**场景引用的材质**间接保活（实测共 **8** 个材质用它，全部被 `GameplayRuntime.unity` 直接引用）：
    `Recovered/judgement-02-Exact.mat`、`Recovered/Material/{backing-combo, cursor-flick-indicator-left,
    cursor-flick-indicator-right, cursor-line-yellow, cursor-precisionpoint, judgement-below-line, judgement-line}.mat`
    → 打包时 shader 随材质进包。

  所以目标工程里**要么**把这 8 个材质里的任意一个也一起接进场景，
  **要么**（更稳）手工把 `9a.shader` 加进 `Project Settings → Graphics → Always Included Shaders`。
  只拷 shader 而不接任何引用 → 构建后 `Shader.Find` 返回 null，`FlickBurst` 静默回退到 `Sprites/Default`（editor 里看不出来）。

**验证**：见 §7 的预览捕获；或看 Console 是否有
`FlickBurst <Right|Left> template is missing.`（`:205`）/
`FlickBurst Right/Left requires Animator and SpriteRenderer.`（`:222-225`）。

### 步骤 9 — 悬空检查（一次性自检）

```powershell
# 在目标工程根目录执行。逐个检查四个 Flick 材质的关键贴图槽。
# ⚠ 注意两个 FlickV5 材质的 YAML 序列化风格不同：
#    Left 是列表式（"    - _Bar:"），Right 是映射式（"    _Bar:"）。
#    只按一种写法去 grep 会漏掉另一个材质，所以这里用正则同时匹配。
function Check-Slots {
    param([string]$MatPath, [string[]]$Slots)
    $lines = Get-Content -LiteralPath $MatPath
    "== $MatPath"
    foreach ($slot in $Slots) {
        $idx = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match ("^\s*-?\s*" + [regex]::Escape($slot) + ":\s*$")) { $idx = $i; break }
        }
        if ($idx -lt 0) { "   {0,-9} MISSING SLOT" -f $slot; continue }
        $tex = $lines[$idx + 1].Trim()
        if ($tex -match 'fileID: 0\}') { $state = 'EMPTY !!!' } else { $state = ($tex -replace '\s+',' ') }
        "   {0,-9} {1}" -f $slot, $state
    }
}
Check-Slots 'Assets\InFalsus\Recovered\Material\FlickV5 Left.mat'  @('_Base','_Bar','_Core')
Check-Slots 'Assets\InFalsus\Recovered\Material\FlickV5 Right.mat' @('_Base','_Bar','_Core')
Check-Slots 'Assets\InFalsus\Recovered\Material\Mat.001.mat'       @('_BaseMap','_MainTex')
Check-Slots 'Assets\InFalsus\Recovered\Material\Mat.001 R.mat'     @('_BaseMap','_MainTex')
```

期望输出（本工程实测值，可直接比对）：

```text
FlickV5 Left   → _Base 689af15b…  _Bar acc55bc2…  _Core 329ff01d…
FlickV5 Right  → _Base 2ee46976…  _Bar a6d40703…  _Core a5f25702…
Mat.001        → _BaseMap 5e37adda…  _MainTex 5e37adda…   （同图双引，正常）
Mat.001 R      → _BaseMap e7e6858a…  _MainTex e7e6858a…   （同图双引，正常）
```

任何一格出现 `EMPTY !!!` → 贴图没拷或 `.meta` 丢了；出现 `MISSING SLOT` → 拷来的是另一个版本的材质。

---

## 3. 如果你只想抄「渲染」，不接编辑器

`NoteRenderer.cs` 是 **2902 行**的巨物，同时管 Tap / Hold / Flick / SkyArea 四种音符 + 对象池 + 编辑器高亮，
**整体移植不现实**。下面是「只要 Flick 画对」的最小集合与连带清单。

### 3.1 单侧最小文件集（跑通一个方向的 Flick）

| # | 文件 | 必需性 |
|---|---|---|
| 1 | `01_shader/dc.shader` | 尾巴的 shader，**没有它就没有斜纹和发光** |
| 2 | `01_shader/Infalsus_Common.hlsl` | `dc.shader` 的 `#include` 依赖（`IF_Premultiply` / `IF_Vertex` / `IF_SoftBand`），**同目录** |
| 3 | `04_assets/Material/FlickV5 Left.mat` | 尾巴材质（`_Base`/`_Bar`/`_Core` 三槽） |
| 4 | `04_assets/Material/Mat.001.mat` | 箭头材质（URP Unlit） |
| 5 | `04_assets/Mesh/Plane.asset` | 尾巴网格（剪切平行四边形 → 45° 斜纹的**几何来源**） |
| 6 | `04_assets/Mesh/Flick.asset` | 箭头网格 |
| 7 | `04_assets/GameObject/v5-3.prefab` | 两级 Transform 层级（含 Plane 的 `scale(-1,-1,-1)` 与根节点的 Y180 旋转） |
| 8 | `04_assets/Texture2D/FlickNote-Base.png` + `-Bar.png` + `-Core.png` | 3 张 |
| 9 | `04_assets/Texture2D/flickv5.png` | 箭头贴图 |
| 10 | `NoteRendererPropertyState.cs` | 颜色 / `_BarRepeat` 的写入器（114 行，**建议整文件拿走**） |

要**双侧**（左黄 + 右绿）就再拷一套：`FlickV5 Right.mat`、`Mat.001 R.mat`、`v5-3R.prefab`、
`FlickNoteR-*.png` × 3、`FlickNoteCapR.png`。mesh 共用。

> 注意：`FlickNoteCapR.png` 只被 `Mat.001 R.mat` 用；`flickv5.png` 只被 `Mat.001.mat` 用。
> 也就是说左右箭头贴图**不是同一张图**，这与「尾巴左右各一套贴图」是同一个设计（原版是三套预染色贴图）。

### 3.2 只抄 flick 分支需要连带哪些字段 / 方法

**`NoteVisual` 上的 5 个字段**（`NoteRenderer.cs:127-140`）缺一不可：

```csharp
public Transform FlickHead;            // :127  箭头子节点（"Flick"）
public Transform FlickPlane;           // :129  尾巴子节点（"Plane"）
public Vector3 FlickPlaneBaseScale;    // :131  尾巴 prefab 原始 scale（= (-1,-1,-1)）
public float   FlickStripesPerUnit;    // :135  每世界单位的斜纹数（0 = 用材质默认）
public float   FlickTailWorldLength;   // :140  尾巴实例 mesh 的实测世界长度
```

**4 个常量**（`NoteRenderer.cs:222-231`）：

| 常量 | 当前值 | 行 | 作用 | 能乱改吗 |
|---|---|---|---|---|
| `FlickTailInset` | `0.0f` | `:222` | 尾巴与箭头后缘的间隙。`>0` 往箭头里收（会重叠），`<0` 留缝 | 可调，**默认 0** |
| `FlickVertexSeamOffset` | `0.0f` | `:225` | 额外推移尾巴后缘顶点 | **必须 0** |
| `FlickStripesPerWorldUnit` | `11.0f` | `:228` | 斜纹疏密（世界空间恒定，与音符宽度无关） | 可调 |
| `FlickTailHeightScale` | `0.93f` | `:231` | 尾巴高度倍数（1.0 = 网格固有比例，即箭头高度的 91.25%） | 可调 |

**5 个方法**：

| 方法 | 行范围 | 干什么 | 移植要点 |
|---|---|---|---|
| flick 几何初始化（在 `CreateVisual` 内） | `:1621-1662` | 找两个子节点、算 `widthRatio`、设 `localScale`、调 `AlignFlickPlane`、记录长度与密度 | 依赖 `FlickWorldWidth(note)`、`FlickHeadWorldWidth(visual)` |
| `FlickHeadWorldWidth` | `:1732-1755` | 把箭头网格顶点变换到 **root 空间**取 X 极值 | 返回 `Mathf.Abs(maxX-minX)`；无 mesh 时回退 `0.232f` |
| `FlickTailWorldLength` | `:1758-1790` | 尾巴**实例 mesh** 的世界 X 跨度 | ⚠ 必须 `mf.mesh`（实例），**不是** `sharedMesh` |
| `AlignFlickPlane` | `:1792-1950` | 把尾巴顶点搬到箭头后缘，并把接缝轮廓贴到箭头背侧轮廓上 | ⚠ 改写的是**实例 mesh 顶点**；用数值探针求 root→local 的符号（`:1864-1885`） |
| `FlickAnchorX` / `FlickWorldWidth` | `:2672-2680` | 锚点 X = `TrackX(note.FlickHeadPos)`；世界宽度 = `note.FlickWidth * 1.62f` | `TrackX` 是 `Mathf.Lerp(-0.81f, 0.81f, pos)`（`:2854-2857`） |

**2 个辅助**：`FindChildContaining`（`:2859-2875`，**大小写不敏感的子串匹配**，不是精确名）、
`TrackX`（`:2854-2857`，轨道半宽硬编码 0.81）。

**每帧写入链**（漏了就不会变色 / 密度不随宽度变化）：

```text
NoteRenderer.ApplyVisualState (:2624-2663)
  └ repeat = visual.FlickStripesPerUnit > 0 ? visual.FlickTailWorldLength * visual.FlickStripesPerUnit : 0   // :2632-2633
  └ visual.PropertyStates[i].Apply(kind, fade, progress, holding, tint, direction, repeat, force)            // :2652-2653
      └ NoteRendererPropertyState.Apply                                                                    // :41-107
          ├ _Color   = kind==Flick ? (direction==4 ? (0.2,0.95,0.55) : (1,0.82,0.18)) : ...                  // :72-73
          ├ _Opacity = fade                                                                                // :89
          └ _BarRepeat = repeat （仅当 repeat > 0 且材质有该属性）                                          // :77, :96-97
```

**外部依赖类型**（这些不拷过来就得自己 stub）：`GameplaySceneBinding`（`MasterScene`、`JudgementLine`、`LaneCenterX`）、
`TrackMotion`、`Chart` / `ChartNote`（`FlickDirection` / `FlickHeadPos` / `FlickWidth`）、`NoteKind`。

### 3.3 一个必须知道的性能事实

`AlignFlickPlane` 每实例化一个 flick 就调 `planeFilter.mesh`（**创建一份 mesh 副本**）并改写 4 个顶点，
再 `MarkDynamic()` + `RecalculateBounds()`（`:1800`、`:1947-1949`）。
所以每个 flick 音符在屏幕存活期间都占一份独立 mesh，**不能**改成 `sharedMesh` —— 改了所有尾巴会共用同一份被最后一次写入的顶点。

---

## 4. GUID 冲突处理

### 4.1 为什么必须带 `.meta`

Unity 的资产引用全部走 `.meta` 里的 `guid:`。只拷 `.png` / `.mat` / `.prefab` 而不拷 `.meta`，
Unity 会**现场生成新 guid**，于是：

- 材质的 `m_Shader` / `m_Texture` 指向的旧 guid 在工程里不存在 → 槽位变 `None` / 粉色
- prefab 的 `MeshFilter.m_Mesh`、`MeshRenderer.m_Materials` → `Missing`
- **没有任何编译错误**，只有运行时视觉错乱。这是本包最容易踩的坑。

### 4.2 目标工程已有同 guid 的资产怎么办

guid 是 32 位十六进制，碰撞概率极低，但**同一套原版资产被拷过两次**（比如你之前已经手动拷过 `dc.shader`）时必然冲突。
Unity 的实际行为：后导入的 `.meta` 会**覆盖**先前的，或直接报 `GUID conflict` 警告，结果不可预期。

处理流程：

```powershell
# 1) 列出本包所有 .meta 的 guid
$pkg = 'Flick_Implementation_Package'
$pkgGuids = Get-ChildItem -LiteralPath $pkg -Recurse -Filter *.meta |
    Select-String -Pattern '^guid: ([0-9a-f]{32})' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique
"package guids: " + $pkgGuids.Count

# 2) 检查目标工程里是否已存在这些 guid
$existing = Get-ChildItem -LiteralPath 'Assets' -Recurse -Filter *.meta |
    Select-String -Pattern '^guid: ([0-9a-f]{32})' |
    ForEach-Object { $_.Matches[0].Groups[1].Value }
$dup = $pkgGuids | Where-Object { $existing -contains $_ }
if ($dup) { "CONFLICT:"; $dup } else { "no conflict, safe to copy" }
```

**如果有冲突**，两条路：

- **推荐**：放弃重拷，改用工程里已有的那份资产（比较内容是否与包内一致：`Compare-Object` 或 `certutil -hashfile`）；
  不一致就以包内为准替换文件本体、**保留现有 `.meta`**（这样引用不断）。
- **必须换 guid 时**：改 `.meta` 里的 `guid:` 后，**同步改所有引用它的文件的 guid**。
  本包的引用关系很浅（一张表搞定）：

| 资产 | 被谁引用 | 引用位置 |
|---|---|---|
| `dc.shader`（`55058b1d…`） | `FlickV5 Left.mat`、`FlickV5 Right.mat` | 各自 `m_Shader: {fileID: 4800000, guid: …}` |
| `FlickV5 Left.mat`（`5ce7c37f…`） | `v5-3.prefab` | `Plane` 的 `MeshRenderer.m_Materials[0]` |
| `FlickV5 Right.mat`（`96bf87a0…`） | `v5-3R.prefab` | 同上 |
| `Mat.001.mat`（`5b65e1ef…`） | `v5-3.prefab` | `Flick` 的 `MeshRenderer.m_Materials[0]` |
| `Mat.001 R.mat`（`65cb8f12…`） | `v5-3R.prefab` | 同上 |
| `Mesh/Flick.asset`（`2ba5deb3…`） | 两个 prefab | `MeshFilter.m_Mesh` |
| `Mesh/Plane.asset`（`cb51b6c7…`） | 两个 prefab | 同上 |
| `FlickNote-Bar.png`（`acc55bc2…`） | `FlickV5 Left.mat` | `_Bar` 槽 |
| `FlickNoteR-Bar.png`（`a6d40703…`） | `FlickV5 Right.mat` | `_Bar` 槽 |
| `FlickNote-Base.png`（`689af15b…`）/ `-Core`（`329ff01d…`） | `FlickV5 Left.mat` | `_Base` / `_Core` 槽 |
| `FlickNoteR-Base.png`（`2ee46976…`）/ `R-Core`（`a5f25702…`） | `FlickV5 Right.mat` | 同上 |
| `flickv5.png`（`5e37adda…`） | `Mat.001.mat` | `_BaseMap` + `_MainTex`（**两个槽同一张图**） |
| `FlickNoteCapR.png`（`e7e6858a…`） | `Mat.001 R.mat` | 同上 |
| `v5-3.prefab`（`c19bfe2a…`）/ `v5-3R.prefab`（`23de1814…`） | 场景的 Bootstrap 序列化字段 | `flickLeftPrefab` / `flickRightPrefab` |

改完用这条命令反查漏改：

```powershell
# 把 <OLD_GUID> 换成被替换掉的旧 guid，一条命中都不能留
Select-String -Path 'Assets\InFalsus\Recovered\Material\*.mat',
                    'Assets\InFalsus\Recovered\GameObject\*.prefab' `
              -Pattern '<OLD_GUID>'
```

### 4.3 三个 shader 的 guid 语义

| shader | guid | 说明 |
|---|---|---|
| `dc.shader` | `55058b1d3da57a04db6ad7341b35e048` | 本包唯一真源，名字 `InFalsus/Replica/FlickV5` |
| `Universal Render Pipeline_Unlit_1.shader` | `f75247fd87768534bb91926c2f61d0e4` | URP 内置 Unlit 的**逐字节副本**，本体 guid 是 `650dd9526735d5b46b79224bc6e94025` |
| `9a.shader` | `29378f1c321fc3d4581007835b579e98` | 名字 `InFalsus/Compat/StenciledUnlitSprite`，`FlickBurst` 运行时用它；**未登记 Always Included**，靠场景材质间接保活（见步骤 8） |

---

## 5. 贴图导入设置（`.meta` 关键字段）

`dc.shader` 的三个采样器各有固定职责（见 shader 内注释 `:189-198`），导入设置必须对齐：

- `_Bar` → **REPEAT** 采样，U 方向 `uv.x * _Scale * _BarRepeat`（当前默认 10 次）→ **不能开 mip**（mip 会跨周期混合把条纹洗平），
  filter 用 Bilinear
- `_Base` / `_Core` → **CLAMP** 采样，一次成形的长条渐变 → 开 mip + Trilinear（不开会沿细长三角形的掠射边闪烁/锯齿）

| 文件 | `enableMipMap` | `filterMode` | `wrapU` | `wrapV` | `textureType` | 备注 |
|---|---|---|---|---|---|---|
| `FlickNote-Bar.png` | **0** | 1 (Bilinear) | **0 (Repeat)** | **0 (Repeat)** | 0 (Default) | ⚠ 唯一会导致斜纹全消失的设置 |
| `FlickNoteR-Bar.png` | **0** | 1 | **0** | **0** | 0 | 同上 |
| `FlickNote-Base.png` | 1 | 2 (Trilinear) | 1 (Clamp) | 1 (Clamp) | 0 | |
| `FlickNote-Core.png` | 1 | 2 | 1 | 1 | 0 | |
| `FlickNoteR-Base.png` | 1 | 2 | 1 | 1 | 0 | |
| `FlickNoteR-Core.png` | 1 | 2 | 1 | 1 | 0 | |
| `flickv5.png`（箭头） | 1 | 1 | 0 | 0 | 0 | 与斜纹无关，原样保留 |
| `FlickNoteCapR.png`（箭头） | 0 | 2 | 1 | 1 | 0 | |
| `FlickBurst.png` / `FlickBurstR.png` | 0 | 2 | 1 | 1 | **8 (Sprite 2D)** | `spriteMode: 2` (Multiple)，64 帧 512×512 |
| `cursor-flick-indicator-*.png` | 0 | 1 | 1 | 1 | 0 | 未接线 |
| `notes_track_matid_flicknoteBase_*.png` | 1 | 1 | 0 | 0 | 0 | 未接线 |

`FlickNote-Bar.png.meta` 的关键片段（`wrapU: 0` 就是 Repeat 的序列化值，不是 `Repeat` 字样）：

```yaml
TextureImporter:
  mipmaps:
    enableMipMap: 0
  filterMode: 1
  wrapU: 0          # 0 = Repeat, 1 = Clamp
  wrapV: 0
  alphaIsTransparency: 1
  textureType: 0
  maxTextureSize: 2048
```

**自检**（拷贝后跑一遍）：

```powershell
$t2d = 'Assets\InFalsus\Recovered\Texture2D'
foreach ($n in 'FlickNote-Bar','FlickNoteR-Bar','FlickNote-Base','FlickNote-Core',
               'FlickNoteR-Base','FlickNoteR-Core') {
    $p = Join-Path $t2d "$n.png.meta"
    $wrap = (Select-String -LiteralPath $p -Pattern 'wrapU: (\d)' ).Matches[0].Groups[1].Value
    $mip  = (Select-String -LiteralPath $p -Pattern 'enableMipMap: (\d)').Matches[0].Groups[1].Value
    $fil  = (Select-String -LiteralPath $p -Pattern 'filterMode: (\d)').Matches[0].Groups[1].Value
    "{0,-18} wrap={1} mip={2} filter={3}" -f $n, $wrap, $mip, $fil
}
# 期望：两张 Bar 是 wrap=0 mip=0 filter=1；四张 Base/Core 是 wrap=1 mip=1 filter=2
```

---

## 6. 排错对照表

**14 种症状。** 每条给「症状 / 根因 / 改哪里」。行号对应当前工作区代码。

### 1) 完全没有斜纹（画面是纯色或被单一竖条盖住）

| 项 | 内容 |
|---|---|
| 根因 A | `_Bar` 槽指向了 `FlickNote-Base`（两者 guid 前缀很像：`689af15b…` vs `acc55bc2…`） |
| 改哪 | `Assets/InFalsus/Recovered/Material/FlickV5 Left.mat` 的 `_Bar:` → `guid: acc55bc2210e247479ba756f78493f6b`（`FlickNote-Bar.png`）。注意**只有 Left 曾经错**，Right 的 `a6d4070345bd3ce49871046d32d28b03` 本来就是对的 |
| 根因 B | `_Bar` 的 wrap 是 Clamp，U 重复超过 1 后全被钳到边缘一列 |
| 改哪 | `Texture2D/FlickNote-Bar.png.meta` 与 `FlickNoteR-Bar.png.meta` 的 `wrapU: 1 → 0`、`wrapV: 1 → 0` |
| 根因 C | shader 里 U 没乘重复系数（`barU = uv.x` 而不是 `uv.x * _Scale * _BarRepeat`） |
| 改哪 | `dc.shader:227`，必须保持 `float barU = uv.x * _Scale * _BarRepeat + _Bar_ST.z;`，且 `:228` 的 V 乘数是 **0.0**（照原版 `mad r0.xy, r0.xyxx, l(32.0,0,0,0)`） |

### 2) 斜纹太密 / 太稀（条数不对）

| 项 | 内容 |
|---|---|
| 根因 | 实际密度 = `FlickTailWorldLength × FlickStripesPerUnit`，运行时通过 MaterialPropertyBlock 写进 `_BarRepeat`，**覆盖材质默认值**。所以调 shader 的 `_BarRepeat` 默认值（`dc.shader:81`，现值 10.0）**没有用** |
| 改哪 | `NoteRenderer.cs:228` `FlickStripesPerWorldUnit = 11.0f`（每世界单位条数，**唯一有效旋钮**）。想更密就调大 |
| 相关 | `NoteRenderer.cs:2632-2633` 算 repeat；`NoteRendererPropertyState.cs:97` 写入。极窄 flick 允许 repeat < 1（一份不完整条纹），**不要 clamp 到 1**，否则窄音符会突然变超密 |

### 3) 尾巴和箭头重叠 / 尾巴从箭头里透出来

| 项 | 内容 |
|---|---|
| 根因 A（历史） | 老代码 `AlignFlickPlane` 里 `position.x += (extendsNegative ? -0.08f : 0.08f)` —— 按旧版箭头尺寸拍的硬编码。遗骸是 `NoteRenderer.cs:218` 的 `FlickHeadOverlap = 0.08f`（**现已无引用**） |
| 改哪 | 现在的实现（`:1856-1858`）是从箭头网格**实测 X 极值**（`FlickHeadWorldWidth` `:1732-1755`）推 `headBackEdgeX`，再用 `FlickTailInset` 微调。默认 0 = 严丝合缝。留缝改 `FlickTailInset`(`:222`) 为负；往里收改正 |
| 根因 B | `FlickVertexSeamOffset`(`:225`) 被改成非 0，会在位置对齐**之后**再把尾巴后缘顶点往箭头方向推 |
| 改哪 | **保持 0.0f** |

### 4) 尾巴偏暗 / 看起来「发虚、不透明却暗」

| 项 | 内容 |
|---|---|
| 根因 | **不是 alpha 问题**。实测尾巴 alpha 全程 0.88–1.00。真因是 `_Color` 的蓝通道只有 **0.18**（`(1, 0.82, 0.18)` 黄 / `(0.2, 0.95, 0.55)` 绿，`NoteRendererPropertyState.cs:72-73`），乘法把亮度压掉一大截 |
| 改哪（按优先级） | ① `_TailDetint`（`dc.shader:89`，现 0.40；↑ 向原始色回拉、更亮但更不黄，范围 0→1）<br>② `_TailLift`（`:84`，现 1.45；乘性提亮）<br>③ `_OverallDim`（`:98`，现 0.86；全局压暗，调大=更亮）<br>④ `_SaturationTrim`（`:96`，现 0.72；<1 会降低鲜艳度） |
| 别调 | `_TailOpacity`（`:86`，默认 **1.0**）。它只作用于尾巴（`tailW` 权重，`:306`），调下去只会让人误以为「更透明」 |

### 5) 没有发光 / 没有光晕

| 项 | 内容 |
|---|---|
| 根因 A | Volume 的 Bloom `intensity = 0`。原版真值 `threshold 1.5` / `intensity 2` / `scatter 0.821` |
| 改哪 | `Assets/Settings/DefaultVolumeProfile.asset:358` 起的 `Bloom` 块；该 profile 由 `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset:363-365` 引用。另需相机 `m_HDR: 1` 且 `m_RenderPostProcessing: 1` |
| 根因 B | 混合模式写成 `Blend SrcAlpha OneMinusSrcAlpha`，而片元返回的已经是**预乘**颜色 → alpha 被乘两次（`a²`），HDR 亮度永远顶不到 Bloom 阈值 |
| 改哪 | `dc.shader:138` 必须是 `Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha` |
| 根因 C | HDR 提亮写在预乘**之前**，`rgb * a` 把 >1 的值拉回阈值以下 |
| 改哪 | `dc.shader:327-328`：先 `IF_Premultiply`，再 `premul.rgb += coreHot * (_GlowPower - 1.0f) * 1.6f;`。`_GlowPower` 现值 1.8（`:100`），1 = 不发光 |
| 目标工程用普通 alpha 混合？ | 把 `Infalsus_Common.hlsl:123` 的 `INFALSUS_PREMULTIPLY` 置 **0**，**同时**把 `dc.shader:138` 改成 `Blend SrcAlpha OneMinusSrcAlpha`。两处必须一起改，只改一处会更暗 |

### 6) 整体偏暗（不只是尾巴）

| 项 | 内容 |
|---|---|
| 根因 | 同 5) 根因 B：预乘输出 + `SrcAlpha` 混合 → a² |
| 改哪 | `dc.shader:138` |
| 次要 | `_OverallDim`（`:98`）= 0.86，全局压暗 14%。原版没有这层，是本工程为「降低过饱和」加的 |

### 7) 箭头被尾巴盖住（端帽没罩住 Plane 头部）

> ⚠ **这一条是「文档与代码不符」的典型，务必读完整。**

| 项 | 内容 |
|---|---|
| 设计意图 | 透明物体**同队列内绘制顺序不保证**，所以箭头材质的 renderQueue 必须**高于**尾巴。尾巴 `FlickV5 Left/Right` = 3012，箭头应 3014 |
| 代码实际 | `Mat.001.mat` 与 `Mat.001 R.mat` 的 `m_CustomRenderQueue` **都是 3012**，`_QueueOffset` 都是 12（URP Unlit：3000+12=3012）。与尾巴**同队列**，所以箭头不一定盖住尾巴 |
| 证据 | `git show HEAD:...Mat.001.mat` 也是 3012；`git log -S "3014"` 对该文件零命中；`main` 与 `perf/render-hotpaths` 都是 3012；全工程 `Recovered/` 与 `*.cs` grep `3014` 零命中 |
| 结论 | `design/Flick_Round5_Fix.md` §1 声称的「已提到 3014」**从未落地**。**以代码为准** |
| 想复现「箭头必定盖住尾巴」怎么办 | 手改 `Mat.001.mat` 与 `Mat.001 R.mat` 的 `m_CustomRenderQueue: 3012 → 3014`；或把 `_QueueOffset: 12 → 14`（Inspector 的 Queue Offset 滑杆，URP Unlit 的 `BaseShaderGUI` 会算 `3000 + offset` 并回写 `m_CustomRenderQueue`）。改完两个材质都要改 |
| 几何前提（好消息） | 箭头在 Y 方向本来就**两端各超出尾巴约 0.007**（箭头 Y `−0.0069→0.1531`，尾巴 Y `0.0010→0.1470`），所以**不需要改网格**，只差队列 |

### 8) 尾巴高度跟着宽度变（宽的音符尾巴更高）

| 项 | 内容 |
|---|---|
| 根因 | 曾经的错写法：`baseScale.y * widthRatio * FlickTailHeightScale`（Y 也乘了宽度比） |
| 改哪 | `NoteRenderer.cs:1651-1654` 必须保持：`X = BaseScale.x * widthRatio`、**`Y = BaseScale.y * FlickTailHeightScale`（不乘 widthRatio）**、`Z = BaseScale.z` |
| 依据 | 两个网格的固有高度比是固定的：箭头 0.160 / 尾巴 0.146 = **91.25%**，与宽度无关 |
| 调高度 | 只调 `FlickTailHeightScale`（`:231`，现 0.93；1.0 = 保持 91.25% 固有比例） |

### 9) 尾巴偏宽 / 比箭头长出一截

| 项 | 内容 |
|---|---|
| 根因 A（历史） | 把「音符总宽」整份给了尾巴，但总宽**已经包含箭头**（箭头 0.232），于是尾巴超出整整一个箭头宽 |
| 改哪 | `NoteRenderer.cs:1645-1647`：`tailSpan = Mathf.Max(meshLen * 0.02f, span - headWorldWidth)`，`widthRatio = tailSpan / meshLen`。`meshLen` 从 `sharedMesh.bounds.size.x` 实测（`:1637-1638`，真值 1.536），**不再用旧的 1.54 魔数** |
| 根因 B | 谱面单位 → 世界单位系数 `1.62`（`NoteRenderer.cs:2679` `FlickWorldWidth`）偏大 |
| 改哪 | 想整体变窄就调小这个 1.62。因为分母已经是网格真值，乘性误差只剩它一个来源 |

### 10) 边缘锯齿 / 毛边 / 闪烁

| 项 | 内容 |
|---|---|
| 根因 A | 硬 alpha 裁剪（`if (a <= 0.0035) discard;`）把轮廓外侧的半透明像素整片杀掉 → 硬台阶。且 `discard` 会削弱 MSAA 覆盖 |
| 改哪 | `dc.shader:323` 现在是 `if (a <= 0.0005) discard;` —— **阈值不能再往大调** |
| 根因 B | `_Base` / `_Core` 关 mip 或 filter 用 Point。2048×128 的图被拉伸到 1.5 世界单位，掠射角下必然阶梯 |
| 改哪 | 两张 Base/Core 的 `.meta`：`enableMipMap: 1`、`filterMode: 2`（Trilinear） |
| 反例 | `_Bar` 恰恰相反：**必须关 mip**（`enableMipMap: 0`），因为 Repeat 高频次采样时 mip 会跨周期混合把条纹洗平 |

### 11) 左右方向反了 / 颜色不对

| 项 | 内容 |
|---|---|
| 语义真源 | `4 = 绿 / 向右`（用 `flickRightPrefab`，染色值 `(0.2, 0.95, 0.55)`）<br>`16 = 黄 / 向左`（用 `flickLeftPrefab`，染色值 `(1, 0.82, 0.18)`） |
| 先分清是哪一层错 | **尾巴**颜色不对 → 调 `_Color` 相关参数（症状 4）。**箭头**颜色不对 → 运行时染色对箭头无效（§1.2），只能是**贴图拷错/拷反**：左黄用 `flickv5.png`、右绿用 `FlickNoteCapR.png` |
| 改哪 | 模板选择 `NoteRenderer.cs:1527`；尾巴颜色 `NoteRendererPropertyState.cs:72-73`；方向 16 的额外处理：`NoteRenderer.cs:1614-1617` **把 root 的 scale.x 取负**，`:1580` 用 `Quaternion.identity` 而不是模板旋转 |
| 铁律 | 写别的方向值 → 解析直接拒载（`ChartModels.cs:168-174` `flickDirection must be 4 or 16`）。**不要靠旋转 UV 去凑左右** |

### 12) 命中特效完全不出现（FlickBurst 没播）

| 项 | 内容 |
|---|---|
| 根因 A | 场景缺 `FlickBurst_0` / `FlickBurstR_0` 锚点 → `Require()` 抛 `Required gameplay object is missing: …` |
| 根因 B | 锚点上没有 `Animator` 或 `SpriteRenderer` → `FlickBurst Right/Left requires Animator and SpriteRenderer.`（`FlickHitEffectRenderer.cs:222-225`） |
| 根因 C | `Shader.Find("InFalsus/Compat/StenciledUnlitSprite")` 失败（构建时被剥离：该 shader 只靠场景里的 8 个材质间接保活，**没有**登记 Always Included）→ 回退 `Sprites/Default` 或抛 `Unable to find a shader for the FlickBurst effect.`（`:177-179`）。编辑器内正常、**打包后才坏**是它的典型特征 |
| 根因 D | `Tick()` 没被调用。它必须**每帧**调，且在「是否播放」判断**之外**（`GameplayBootstrap.cs:419-421`），否则暂停时特效不推进/不回收 |
| 根因 E | `RebuildChartRuntime` 后忘了 `SetEngine(_engine)`（`GameplayBootstrap.cs:511-513` / `:679-681`）→ 新判定引擎的 `Judged` 事件没人订阅 → 永远不播 |
| 根因 F | 动画 clip 缺失或时长为 0：`AnimatorController/FlickBurst_0.controller` + `AnimationClip/FlickBurstAnim(.R).anim`（0.5333s，120fps 采样）。自检断言要求 `length >= 0.5` |
| 验证 | 运行后看 Console 的 `PhaseA_FlickBurst_` 日志（§7.2），或看 Bootstrap 状态串里的 `flickFxPlayed=` 是否增长 |

### 13) Flick 音符完全不显示 / 只有一半（只有箭头或只有尾巴）

| 项 | 内容 |
|---|---|
| 根因 A | prefab 的子节点**名字里不含** `Flick` / `Plane`。代码是 `FindChildContaining(clone.transform, "Flick")` / `"Plane"`（`NoteRenderer.cs:1623-1624`），实现是**大小写不敏感的子串匹配**（`:2859-2875`） |
| 改哪 | 保持 `v5-3` / `v5-3R` 的节点名原样。改名 = 静默失效（`FlickPlane` 为 null 时整块几何初始化被 `if` 跳过，`:1625`） |
| 根因 B | `FLICK_NOTE` 锚点不存在 → `Resolve()` 抛异常，整个 Bootstrap 起不来 |
| 根因 C | prefab 里 `MeshFilter.m_Mesh` 或 `MeshRenderer.m_Materials` 断链（拷资产没带 `.meta`） |
| 判别 | 选中 prefab：`Flick` 节点应有 `MeshFilter`(Flick) + `MeshRenderer`(Mat.001)，`Plane` 节点应有 `MeshFilter`(Plane) + `MeshRenderer`(FlickV5 Left) |

### 14) 场景里能看到一个「多余的 flick」且它不动

| 项 | 内容 |
|---|---|
| 根因 | 模板节点没被隐藏。`ApplyPhaseAPolicy()` 会把 `GAMEPLAY` 整支 `SetActive(false)`（`GameplaySceneBinding.cs:300-303`），并且 `DefaultHiddenPaths` 里点名隐藏了 `JUDGE_SKY/v5-3` 与 `JUDGE_SKY/v5-3R`（`:158-159`） |
| 改哪 | 确认 Bootstrap 里 `_binding.ApplyPhaseAPolicy()`（`GameplayBootstrap.cs:260`）被调用；若你自己写 Bootstrap，务必同样隐藏模板节点（`Instantiate` 对 inactive 模板依然有效，`:284` 注释明确说明） |

### 附：参数现值表（上面 14 条症状的调参汇总）

| 参数 | 现值 | 位置 | 历史文档曾写 |
|---|---|---|---|
| `_BarRepeat` | **10.0** | `dc.shader:81` | 5.5 → 8.0（**已被 per-note 覆盖，改它无效**，见症状 2） |
| `_BarContrast` | **2.6** | `dc.shader:82` | 2.0 |
| `_TailLift` | **1.45** | `dc.shader:84` | 1.35 → 1.7 |
| `_TailOpacity` | **1.0** | `dc.shader:86` | 1.0 |
| `_TailDetint` | **0.40** | `dc.shader:89` | 0.55 |
| `_BarStrength` | **0.55** | `dc.shader:91` | — |
| `_CoreBoost` | **1.6** | `dc.shader:80` | — |
| `_OverallDim` | **0.86** | `dc.shader:98` | — |
| `_SaturationTrim` | **0.72** | `dc.shader:96` | — |
| `_Desaturate` | **0.0** | `dc.shader:94` | — |
| `_GlowPower` | **1.8** | `dc.shader:100` | 2.2 |
| `FlickStripesPerWorldUnit` | **11.0** | `NoteRenderer.cs:228` | — |
| `FlickTailHeightScale` | **0.93** | `NoteRenderer.cs:231` | 1.0 |
| `FlickTailInset` | **0.0** | `NoteRenderer.cs:222` | 0.0 / 0.002 |
| `FlickVertexSeamOffset` | **0.0** | `NoteRenderer.cs:225` | 0.003（已删） |
| Bloom `threshold/intensity/scatter` | **1.5 / 2 / 0.821** | `DefaultVolumeProfile.asset:358+` | 原版真值，一致 |

---

## 7. 验证方法

### 7.1 编译（最快的一道关）

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$proj  = (Get-Location).Path      # 必须在工程根目录下执行（含 Assets/ ProjectSettings/）

& $unity -batchmode -quit -projectPath $proj -logFile Diagnostics\UnityCompile.log
"exit=$LASTEXITCODE"
Select-String -LiteralPath Diagnostics\UnityCompile.log -Pattern 'error CS|Shader error|error:' |
    Select-Object -First 20
```

> ⚠ **本工程（以及任何 Unity 编辑器开着的工程）跑 batchmode 会拿不到锁**，表现为进程秒退、日志里
> `It looks like another Unity instance is running with this project open.` 或干脆没有编译输出。
> 判定方法：`Test-Path Temp\UnityLockfile` 为 `True` 就说明编辑器开着。
> **先关掉 Unity 编辑器再跑**（本仓库当前 `Temp/UnityLockfile` 存在，所以本文没有替你执行编译）。
> 关闭前记得让编辑器把改动存盘，否则 batchmode 读到的还是旧资产。

### 7.2 交互式验证（推荐，三条）

| # | 菜单 | 出处 | 产出 / 期望 |
|---|---|---|---|
| 1 | **`InFalsus/Gameplay/Capture FlickBurst Preview`** | `Editor/FlickEffectPreviewCapture.cs:35` | 11 张 `Diagnostics/Screenshots/flick_preview_r_00_0.00.png` … `_10_1.00.png`（归一化时间 0/0.04/0.08/0.12/0.18/0.26/0.36/0.48/0.62/0.78/1.0）。Console 每条带 `sprite=<名字>`，若全是 `<null>` 说明 Sprite 曲线没接上。**只验命中特效，不验音符本体** |
| 2 | **`InFalsus/Gameplay/Capture Runtime Play`** | `Editor/GameplayPlayCapture.cs:38` | 进 Play 模式并在 `Diagnostics/Screenshots/gameplay_runtime_t<time>.png` 出图（默认时刻 1.5/3.0/6.2/7.0/8.8）。Console 会有 `[InFalsus] FlickBurst: PhaseA_FlickBurst_<side>_<n> sprite=… state=… normalized=… localPos=… worldBounds=…/… screen=(x,y,z)`（过滤前缀在 `:252-254`）。**`screen.z > 0` 且在视口内 = 特效确实被画到了** —— 这是排查「特效播了但看不见」最有效的一条 |
| 3 | **`InFalsus/Gameplay/Run Phase A Self Check`** | `Editor/GameplaySelfCheck.cs:26` | 写 `Diagnostics/PhaseASelfCheck.txt`、`Diagnostics/JudgementMatrix.csv`、`Diagnostics/SceneBindings.json`、`Diagnostics/SceneBindingReport.txt`。任一断言失败会抛异常，Console 里能看到具体原因 |

第 3 条里与 Flick 直接相关的断言：

- `ValidateFlickParsing()`（`:231-236`）：`flick(1000,9,24,6,4)` 与 `…,16)` 解析成功、`Count == 2`，
  且 `FlickCenterPos 0.375` / `FlickWidth 0.25` / `FlickLeftPos 0.25` / `FlickRightPos 0.5`
- `ValidateFlickEffects()`（`:46-65`）：6 个资产必须都存在，且两侧 clip 长度 **≥ 0.5s**

  ```text
  Assets/InFalsus/Recovered/AnimatorOverrideController/FlickBurst_0 R.overrideController
  Assets/InFalsus/Recovered/AnimatorController/FlickBurst_0.controller
  Assets/InFalsus/Recovered/Texture2D/judgement-02-Exact.png
  Assets/InFalsus/Recovered/Texture2D/judgement-01-ExactPlus.png
  Assets/InFalsus/Recovered/Sprite/FlickBurstR_0.asset
  Assets/InFalsus/Recovered/Sprite/FlickBurst_0.asset
  ```
  缺任何一个 → `Flick hit-effect assets are incomplete.`；clip 太短 → `FlickBurst animation clips are missing or too short.`
  成功时 Console 有 `[InFalsus] Flick effects passed: right=0.533s left=0.533s`

`PhaseASelfCheck.txt` 里这两行的期望值：

```text
flickSemanticCheck=passed
flickEffectAssetCheck=passed
```

### 7.3 batchmode 跑自检（可选）

```powershell
# 编辑器必须先关闭（Temp\UnityLockfile 不存在）
& 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe' `
    -batchmode -quit -projectPath (Get-Location).Path `
    -logFile Diagnostics\UnitySelfCheck.log `
    -executeMethod InFalsus.EditorTools.GameplaySelfCheck.Run

if (Test-Path Diagnostics\PhaseASelfCheck.txt) { Get-Content Diagnostics\PhaseASelfCheck.txt }
Select-String -LiteralPath Diagnostics\UnitySelfCheck.log -Pattern 'Flick effects passed|Exception|error CS'
```

### 7.4 场景绑定报告（不跑游戏也能查锚点）

菜单 `InFalsus/Gameplay/Report Scene Bindings`（`Editor/GameplaySceneBuilder.cs:268`）→
写 `Diagnostics/SceneBindings.json` + `Diagnostics/SceneBindingReport.txt`。
重点看 `keyRenderers` 里有没有 `…/v5-3/Plane`（材质 `FlickV5 Left`）和 `…/FLICK_NOTE`。

### 7.5 纯文本体检（不用 Unity，最快定位断链）

```powershell
# 1) Flick 四材质引用的 shader guid 是否正确
$dcGuid = (Select-String -LiteralPath 'Assets\InFalsus\Recovered\dc.shader.meta' -Pattern '^guid: ([0-9a-f]{32})').Matches[0].Groups[1].Value
"dc.shader guid = $dcGuid"
foreach ($m in 'FlickV5 Left','FlickV5 Right') {
    $p = "Assets\InFalsus\Recovered\Material\$m.mat"
    $s = (Select-String -LiteralPath $p -Pattern 'm_Shader:.*guid: ([0-9a-f]{32})').Matches[0].Groups[1].Value
    "{0,-16} shader={1}  {2}" -f $m, $s, $(if ($s -eq $dcGuid) {'OK'} else {'MISMATCH'})
}
foreach ($m in 'Mat.001','Mat.001 R') {
    $p = "Assets\InFalsus\Recovered\Material\$m.mat"
    $s = (Select-String -LiteralPath $p -Pattern 'm_Shader:.*guid: ([0-9a-f]{32})').Matches[0].Groups[1].Value
    $q = (Select-String -LiteralPath $p -Pattern 'm_CustomRenderQueue: (-?\d+)').Matches[0].Groups[1].Value
    "{0,-16} shader={1} queue={2}" -f $m, $s, $q     # 期望 queue=3012（见症状 7）
}

# 2) Bar 贴图的 wrap 必须是 0
foreach ($t in 'FlickNote-Bar','FlickNoteR-Bar') {
    $p = "Assets\InFalsus\Recovered\Texture2D\$t.png.meta"
    "{0,-18} wrapU={1} wrapV={2}" -f $t,
        (Select-String -LiteralPath $p -Pattern 'wrapU: (\d)').Matches[0].Groups[1].Value,
        (Select-String -LiteralPath $p -Pattern 'wrapV: (\d)').Matches[0].Groups[1].Value
}

# 3) 两个 prefab 的子节点名（必须含 Flick / Plane）
foreach ($p in 'v5-3','v5-3R') {
    $f = "Assets\InFalsus\Recovered\GameObject\$p.prefab"
    "== $p : " + ((Select-String -LiteralPath $f -Pattern 'm_Name: (Flick|Plane)$') | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique) -join ','
}
```

---

## 8. 五条铁律

1. **`.meta` 必须跟着文件走。** guid 变了不会报错，只会静默变成粉色/Missing —— 排错成本最高的一类故障。
2. **`_Bar` 用 Repeat、`_Base`/`_Core` 用 Clamp；`_Bar` 关 mip、`Base/Core` 开 mip。** 反了就是「没斜纹」或「边缘锯齿」。
3. **斜纹疏密只在 `FlickStripesPerWorldUnit`（`NoteRenderer.cs:228`）里调，不在 shader 的 `_BarRepeat` 默认值里调。** 后者每帧被 MaterialPropertyBlock 覆盖。
4. **斜纹是几何出来的，不是 UV 转出来的。** `Plane` 网格本身是剪切平行四边形（上边比下边偏 X 0.146，高度 0.146 → 精确 45°），
   UV 沿长度线性分布；只靠 `barU` 的重复次数产生条纹。**不要给 UV 加旋转、不要改网格**。
5. **透明层顺序只在 renderQueue 里定，不能靠同队列内的绘制顺序。** 尾巴 3012；想让箭头稳定盖住尾巴，箭头必须 >3012
   （当前工程两个箭头材质仍是 3012，Round5 文档声称的 3014 **没有落地** —— 见 §0 与症状 7）。

---

## 附：包内文件速查

| 想看什么 | 去哪 |
|---|---|
| 全部依赖与 guid | `00_docs/Flick_依赖清单.md` |
| 贴图尺寸 / wrap / 用途 | `00_docs/Flick_贴图清单.md` |
| 四轮修复的症状→根因推理过程 | `00_docs/design/Flick_{Stripe,Tail,Glow_Width_Height,Round5}_Fix.md` |
| 每个改动点的逐字代码片段 | `06_integration/01..28_*.cs`（编号即推荐落地顺序） |
| 整文件依赖（Bootstrap / Binding / 场景 / 编辑器） | `07_full_dependents/Assets/InFalsus/…` |
