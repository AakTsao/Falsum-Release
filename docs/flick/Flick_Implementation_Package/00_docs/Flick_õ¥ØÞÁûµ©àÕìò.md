# Flick 依赖清单

> 目标：拿到本包的人，用这一篇就能判断「还差什么」。
> 所有 guid 均从仓库内 `.meta` 实测 grep 得出，所有路径均为仓库内真实相对路径。
> 行号以 2026-11-15 工作区（**含未提交改动**，见 §8.1）为准。
> 标注「未核实」的条目表示本次只做了静态核对、未跑 Unity 实机验证。

---

## 0. 一分钟结论

Flick 的渲染由 **2 个 mesh + 2 个 shader + 7 个材质 + 13 张贴图** 组成：

| 层 | 网格 | 材质 | Shader |
|---|---|---|---|
| 箭头（head） | `Mesh/Flick.asset`（24 顶点） | `Mat.001`（左）/ `Mat.001 R`（右） | `Universal Render Pipeline_Unlit_1.shader`（URP Unlit，**不是** Flick 专用） |
| 尾巴（tail） | `Mesh/Plane.asset`（4 顶点，实长 1.536） | `FlickV5 Left` / `FlickV5 Right` | `dc.shader`（`InFalsus/Replica/FlickV5`，**唯一 Flick 专用 shader**） |

两者由 prefab `v5-3.prefab`（左）/ `v5-3R.prefab`（右）组装，运行时 **复制** 到场景并靠 `MaterialPropertyBlock` 逐帧改色/改条纹密度。
命中特效是另一条链：`FlickBurst_0` / `FlickBurstR_0` 场景节点 + 128 张 Sprite + 2 个 AnimationClip + `FlickHitEffectRenderer`。

**最容易漏的三样**：① `Infalsus_Common.hlsl`（`dc.shader` 用相对路径 include，必须与它同目录）；② 两张 `*Bar.png` 的 Repeat 导入设置；③ `DefaultVolumeProfile.asset` 的 Bloom。

---

## 1. 引擎与包版本

### 1.1 Unity

`ProjectSettings/ProjectVersion.txt:1-2`（原文）：

```
m_EditorVersion: 6000.3.26f1
m_EditorVersionWithRevision: 6000.3.26f1 (de8167e7440c)
```

### 1.2 包（`Packages/manifest.json:4-5` 原文）

```json
    "com.unity.render-pipelines.universal": "17.3.0",
    "com.unity.shadergraph": "17.3.0",
```

`Packages/packages-lock.json` 实测解析结果：

| 包 | 版本 | source |
|---|---|---|
| `com.unity.render-pipelines.universal` | 17.3.0 | builtin |
| `com.unity.shadergraph` | 17.3.0 | builtin |
| `com.unity.render-pipelines.core` | 17.3.0 | builtin |

### 1.3 为什么这三个包是硬依赖

- `dc.shader:149 / :345` include `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl` → **缺 URP 直接编译失败**。
- `dc.shader:390` `CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"`，该类在 `com.unity.shadergraph` 的 `Editor/ShaderGUI/GenericShaderGraphMaterialGUI.cs` → 缺 ShaderGraph 时材质 Inspector 报错（shader 本身仍能编译）。
- `dc.shader:389` `Fallback "Hidden/Shader Graph/FallbackError"`。本次未在 `Library/PackageCache` 内 grep 到该 fallback shader 的定义（标注**未核实**）；它只在 shader 编译失败兜底时才用，不影响正常路径。

---

## 2. 文件依赖三层表

### 2.a 硬依赖（缺了 Flick 渲染不出来）

全部位于 `Assets/InFalsus/Recovered/`（Audio 例外）。

| # | 资产 | 仓库路径 | guid |
|---|---|---|---|
| 1 | Flick 专用 Shader | `dc.shader` | `55058b1d3da57a04db6ad7341b35e048` |
| 2 | Shader 公共库 | `Infalsus_Common.hlsl` | `7ff1713785ac4914e8866a83365b3c0c` |
| 3 | 公共库重复副本（**无人 include，见 §8.4**） | `Shader/Infalsus_Common.hlsl` | `175499b66b6d59b4f9a6f07487ab0416` |
| 4 | 尾巴材质（左） | `Material/FlickV5 Left.mat` | `5ce7c37f26e917d499575f6e71d17b34` |
| 5 | 尾巴材质（右） | `Material/FlickV5 Right.mat` | `96bf87a014d661d4f8e5c1b55e79fe18` |
| 6 | 箭头材质（左） | `Material/Mat.001.mat` | `5b65e1ef0a12ed24380b367ec857314d` |
| 7 | 箭头材质（右） | `Material/Mat.001 R.mat` | `65cb8f12c4a4fd64dbacf621e55977e4` |
| 8 | 箭头 Shader（URP Unlit 复刻） | `Shader/Universal Render Pipeline_Unlit_1.shader` | `f75247fd87768534bb91926c2f61d0e4` |
| 9 | 箭头网格 | `Mesh/Flick.asset` | `2ba5deb3d2fba2e4f9188d6121b2958a` |
| 10 | 尾巴网格 | `Mesh/Plane.asset` | `cb51b6c7bce3f9b478194025ef511b05` |
| 11 | 左 prefab | `GameObject/v5-3.prefab` | `c19bfe2a8cc8c2b4781ed2d7fd72fcbe` |
| 12 | 右 prefab | `GameObject/v5-3R.prefab` | `23de1814ce5aa2a45ac1f402234a489f` |
| 13 | 尾巴贴图 Bar（左） | `Texture2D/FlickNote-Bar.png` | `acc55bc2210e247479ba756f78493f6b` |
| 14 | 尾巴贴图 Base（左） | `Texture2D/FlickNote-Base.png` | `689af15bdfc563d458e755a5e2068e64` |
| 15 | 尾巴贴图 Core（左） | `Texture2D/FlickNote-Core.png` | `329ff01dbde61b24abcbd1980a42b130` |
| 16 | 尾巴贴图 Bar（右） | `Texture2D/FlickNoteR-Bar.png` | `a6d4070345bd3ce49871046d32d28b03` |
| 17 | 尾巴贴图 Base（右） | `Texture2D/FlickNoteR-Base.png` | `2ee46976e002c504886b634b4749d872` |
| 18 | 尾巴贴图 Core（右） | `Texture2D/FlickNoteR-Core.png` | `a5f2570298917b54584d4fbb5f897f64` |
| 19 | 箭头贴图（左） | `Texture2D/flickv5.png` | `5e37adda88330934ba564a8f3e176fd8` |
| 20 | 箭头贴图（右） | `Texture2D/FlickNoteCapR.png` | `e7e6858aa74220a4eaec272c99738a7f` |

材质 → 贴图绑定（实测，逐条 grep 过 `m_TexEnvs`）：

| 材质 | 槽位 | 贴图 guid | 贴图 |
|---|---|---|---|
| FlickV5 Left | `_Bar` | `acc55bc2…` | FlickNote-Bar |
| | `_Base` | `689af15b…` | FlickNote-Base |
| | `_Core` | `329ff01d…` | FlickNote-Core |
| FlickV5 Right | `_Bar` | `a6d40703…` | FlickNoteR-Bar |
| | `_Base` | `2ee46976…` | FlickNoteR-Base |
| | `_Core` | `a5f25702…` | FlickNoteR-Core |
| Mat.001 | `_BaseMap` / `_MainTex` | `5e37adda…` | flickv5 |
| Mat.001 R | `_BaseMap` / `_MainTex` | `e7e6858a…` | FlickNoteCapR |
| matid_flicknote | `_BaseMap` / `_MainTex` | `ca899750…` | notes_track_matid_flicknoteBase_AlbedoTransparencyOrange |

网格规模（实测 YAML）：`Flick.asset` 24 顶点 / 36 索引，AABB extent `x=0.116, y=0.08`；`Plane.asset` 4 顶点 / 6 索引，extent `x=0.768`（即实长 **1.536**，与 `NoteRenderer.cs:1633` 的 `float meshLen = 1.536f;` 兜底值一致）。

### 2.b 代码依赖（缺了编译不过 / 行为丢失）

Flick 不在独立 asmdef 里（`find Assets -name "*.asmdef"` 无输出 → 全部走 `Assembly-CSharp`），因此**没有程序集级依赖，只有类型级依赖**。

| # | 文件 | 行 | 对 Flick 的作用 |
|---|---|---|---|
| 1 | `Gameplay/Presentation/Notes/NoteRenderer.cs` | 462 | 构造函数（**实测 14 个参数**，任务书写的 13 是笔误）：`binding, motion, chart, skyPrefab, skyAreaMaterial, holdPrefab, flickLeftPrefab, flickRightPrefab, tapPrefab, tapSpanPrefab, sideLeftTapPrefab, sideRightTapPrefab, sideLeftHoldPrefab, sideRightHoldPrefab` |
| 2 | 同上 | 297-299 / 510-511 | 持有 `_flickLeftPrefab` / `_flickRightPrefab` |
| 3 | 同上 | 1525-1527 | `FlickDirection == 4` → 右 prefab，否则左 prefab |
| 4 | 同上 | 1623-1624 | `FindChildContaining(clone.transform, "Flick")` / `"Plane"` —— **靠子节点名字**定位两层，改名即断 |
| 5 | 同上 | 1629-1661 | 尾巴 X 按宽度缩放、Y 只吃 `FlickTailHeightScale`；用 `sharedMesh.bounds.size.x` 反算 `widthRatio` |
| 6 | 同上 | 1792-1950 | `AlignFlickPlane` 顶点级对齐（±`FlickTailInset`、`FlickVertexSeamOffset`） |
| 7 | 同上 | 228 / 231 | `FlickStripesPerWorldUnit = 11.0`、`FlickTailHeightScale = 0.93` |
| 8 | 同上 | 2632-2633 / 2624-2663 | `repeat = FlickTailWorldLength * FlickStripesPerUnit` → 传给属性块 |
| 9 | `Gameplay/Presentation/Notes/NoteRendererPropertyState.cs` | 16-23 | 缓存 `_Color/_BaseColor/_MainColor/_Opacity/_Held/_Progress/_ModulateProgress/_BarRepeat` 的 `PropertyToID` |
| 10 | 同上 | 77 / 82-97 / 105 | Flick 的 `relevant` 掩码 = `Opacity \| Color \| BarRepeat`；真正写 MPB 的唯一出口 |
| 11 | `Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs` | 全部 | 命中爆闪：池化 4+4 burst、8 feedback；`Shader.Find("InFalsus/Compat/StenciledUnlitSprite")` |
| 12 | `Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs` | 55-58 / 225-245 / 367-377 | 场景锚点常量 + `Require()`（`throw new InvalidOperationException` 在 `:372`，**找不到直接抛异常，不是静默**） |
| 13 | `Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs` | 74/76 / 301 | 序列化 `flickLeftPrefab`/`flickRightPrefab` → 传进 NoteRenderer 构造 |
| 14 | 同上 | 306-307 | `AddComponent<FlickHitEffectRenderer>()` + `Initialize(...)` |
| 15 | 同上 | 419-421 | `_flickEffects.Tick(_clock.TimeMs, !_clock.IsPlaying)` |
| 16 | 同上 | 928-930 / 954 / 958 | autoplay `PressFlick(note.FlickDirection, …)`；手动键 `PressFlick(16)` / `PressFlick(4)` |
| 17 | `Gameplay/Presentation/Audio/GameplayHitAudio.cs` | 155 / 164 / 591-595 | Flick 音效：`Initialize(..., flickClip, ...)` → `case NoteKind.Flick: PlayOneShot(_flickClip, HitSoundCategory.Flick, ...)` |
| 18 | 同上 | 906 | `_flickVolume` 从偏好 `FlickVolume` 来（`FalsumPreferences.cs:113`） |
| 19 | `Gameplay/Core/Judgement/JudgementEngine.cs` | — | `PressFlick(int direction, double timeMs)`；4/16 → `Judged` 事件 → FlickHitEffectRenderer |

### 2.c 场景依赖（缺了运行时异常 / 静默不显示）

`GameplaySceneBinding` 全部走 `Require()`（`GameplaySceneBinding.cs:367-377`），**缺失即抛异常硬失败**（`throw new InvalidOperationException("Required gameplay object is missing: " + path)` 在 `:372-374`），不会静默。

| # | 用途 | 完整路径 | 代码位置 |
|---|---|---|---|
| 1 | 左爆闪模板（**必须带 Animator + SpriteRenderer**） | `GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/FlickBurst_0` | `GameplaySceneBinding.cs:55-56`，用于 `FlickHitEffectRenderer.cs:104-107`（缺 Animator/SpriteRenderer 在 `:219-224` 抛异常） |
| 2 | 右爆闪模板 | `GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/FlickBurstR_0` | `:57-58` |
| 3 | Flick 模板节点（**仅被要求存在，随后被 SetActive(false)**，见 §8.3） | `GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/FLICK_NOTE` | `:225-227`；`:292-295` 关闭 |
| 4 | 左尾巴+箭头实例（被隐藏但材质引用必须可解析） | `…/JUDGE_SKY/v5-3`（子节点 `Plane`、`Flick`） | `DefaultHiddenPaths` `:158` |
| 5 | 右尾巴+箭头实例 | `…/JUDGE_SKY/v5-3R`（子节点 `Plane`、`Flick`） | `:159` |
| 6 | 光标指示容器（被隐藏，见 §8.3） | `…/JUDGE_SKY/SkyCursorExtrasContainer`（子 `cursor-flick-indicator-left/right`） | `:160` |
| 7 | 爆闪挂载父节点 | `…/JUDGE_SKY` | `GameplaySceneBinding.JudgementLine`，`FlickHitEffectRenderer.cs:211-214` 用 `Instantiate(template, templateTransform.parent, false)` |
| 8 | 反馈条父节点 | `GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE` | `FlickHitEffectRenderer.cs:305` |
| 9 | 渲染根 | `GameCamera/master_scene_v10/MasterScenev10` | `NoteRenderer.cs:465` 附近 `_renderRoot = binding.MasterScene` |

**Bootstrap 序列化字段**（`GameplayRuntime.unity:16231-16232` 实测）：

```
flickLeftPrefab:  {fileID: 1709254077376921, guid: c19bfe2a8cc8c2b4781ed2d7fd72fcbe, type: 3}
flickRightPrefab: {fileID: 1709254077376921, guid: 23de1814ce5aa2a45ac1f402234a489f, type: 3}
```

> `1709254077376921` 在 `v5-3.prefab` / `v5-3R.prefab` 里都是 `--- !u!1 &1709254077376921`，即两个 prefab 的**根 GameObject fileID 相同**（不同文件内不冲突）。

**prefab 内部层级**（实测 `v5-3.prefab` / `v5-3R.prefab` 完全同构）：

```
v5-3(根)
├── Plane   ← MeshFilter(mesh=Plane.asset) + MeshRenderer(material=FlickV5 Left/Right)
│   ├── PLANE_START
│   └── PLANE_END
└── Flick   ← MeshFilter(mesh=Flick.asset)  + MeshRenderer(material=Mat.001 / Mat.001 R)
```

`PLANE_END` 同时被 `GameplaySceneBinding.cs:52-54` 当作 `TrackEndAnchorPath` 使用（TrackEnd 功能的锚点，与本包只是共用 prefab）。

---

## 3. GUID 对照表（guid ↔ 资产 ↔ 谁引用它）

反向引用用「全 `Assets/` 扫 626 个文本资产 + guid 匹配」得出，非猜测。

| guid | 资产 | 被谁引用 |
|---|---|---|
| `55058b1d3da57a04db6ad7341b35e048` | `Recovered/dc.shader` | `FlickV5 Left.mat`、`FlickV5 Right.mat`（仅此 2 处） |
| `7ff1713785ac4914e8866a83365b3c0c` | `Recovered/Infalsus_Common.hlsl` | **0 处 guid 引用**；靠 `dc.shader:150/346`、`a9.shader:137`、`c.shader:126` 的相对路径 `#include "Infalsus_Common.hlsl"` 使用 |
| `175499b66b6d59b4f9a6f07487ab0416` | `Recovered/Shader/Infalsus_Common.hlsl` | **0 处引用（死副本）**，见 §8.4 |
| `5ce7c37f26e917d499575f6e71d17b34` | `Material/FlickV5 Left.mat` | `v5-3.prefab`（`Plane` 的 MeshRenderer `:166`）、`GameplayRuntime.unity`（renderer `!u!23 &5593` = `v5-3/Plane`） |
| `96bf87a014d661d4f8e5c1b55e79fe18` | `Material/FlickV5 Right.mat` | `v5-3R.prefab`、`GameplayRuntime.unity` |
| `5b65e1ef0a12ed24380b367ec857314d` | `Material/Mat.001.mat` | `v5-3.prefab`（`Flick` 的 MeshRenderer `:256`）、`GameplayRuntime.unity`（renderer `!u!23 &6050` = `v5-3/Flick`） |
| `65cb8f12c4a4fd64dbacf621e55977e4` | `Material/Mat.001 R.mat` | `v5-3R.prefab`、`GameplayRuntime.unity` |
| `2ba5deb3d2fba2e4f9188d6121b2958a` | `Mesh/Flick.asset` | `v5-3.prefab`、`v5-3R.prefab`、`GameplayRuntime.unity`（MeshFilter `&7781` / `&8449`） |
| `cb51b6c7bce3f9b478194025ef511b05` | `Mesh/Plane.asset` | `v5-3.prefab`、`v5-3R.prefab`、`GameplayRuntime.unity`（MeshFilter `&6786` / `&7310`） |
| `5f61dc8722b8efb41ae93c3298064cdd` | `Mesh/flicknote01.asset` | 仅 `GameplayRuntime.unity`（MeshFilter `&7805`，GO `flicknote01`） |
| `92bc5e892975b414b8ca81aaf2f8b7c5` | `Mesh/flickunderscore01.asset` | 仅 `GameplayRuntime.unity`（MeshFilter `&7129`，GO `flickunderscore01`） |
| `c19bfe2a8cc8c2b4781ed2d7fd72fcbe` | `GameObject/v5-3.prefab` | `GameplayRuntime.unity:16231`（Bootstrap `flickLeftPrefab`） |
| `23de1814ce5aa2a45ac1f402234a489f` | `GameObject/v5-3R.prefab` | `GameplayRuntime.unity:16232`（Bootstrap `flickRightPrefab`） |
| `f75247fd87768534bb91926c2f61d0e4` | `Shader/Universal Render Pipeline_Unlit_1.shader` | **24 个材质**（`Mat.001`、`Mat.001 R`、`matid_flicknote`，以及 `matid_*` 系列、`judgement_line*` 等） |
| `29378f1c321fc3d4581007835b579e98` | `Recovered/9a.shader`（`InFalsus/Compat/StenciledUnlitSprite`） | **8 个材质**：`cursor-flick-indicator-left`、`cursor-flick-indicator-right`、`backing-combo`、`cursor-line-yellow`、`cursor-precisionpoint`、`judgement-below-line`、`judgement-line`、`judgement-02-Exact` |
| `b1f84eec5fee4104a813ec4b386eb015` | `Material/FlickBurstMaterial.mat` | 仅 `GameplayRuntime.unity`（`FlickBurst_0` / `FlickBurstR_0` 的 SpriteRenderer，共 8 处 fileID 引用） |
| `2cbee7aa38863194ba98555da64f340f` | `Material/matid_flicknote.mat` | 仅 `GameplayRuntime.unity`（`flicknote01` 与 `flickunderscore01` 的 MeshRenderer） |
| `1e13dfe28f83df54ab327ea0c8521bf2` | `Material/cursor-flick-indicator-left.mat` | 仅 `GameplayRuntime.unity`（`SkyCursorExtrasContainer/cursor-flick-indicator-left`） |
| `f908fb52d509403449a695d92d70e05c` | `Material/cursor-flick-indicator-right.mat` | 仅 `GameplayRuntime.unity`（同名右节点） |
| `acc55bc2210e247479ba756f78493f6b` | `Texture2D/FlickNote-Bar.png` | `FlickV5 Left.mat` `_Bar` |
| `689af15bdfc563d458e755a5e2068e64` | `Texture2D/FlickNote-Base.png` | `FlickV5 Left.mat` `_Base` |
| `329ff01dbde61b24abcbd1980a42b130` | `Texture2D/FlickNote-Core.png` | `FlickV5 Left.mat` `_Core` |
| `a6d4070345bd3ce49871046d32d28b03` | `Texture2D/FlickNoteR-Bar.png` | `FlickV5 Right.mat` `_Bar` |
| `2ee46976e002c504886b634b4749d872` | `Texture2D/FlickNoteR-Base.png` | `FlickV5 Right.mat` `_Base` |
| `a5f2570298917b54584d4fbb5f897f64` | `Texture2D/FlickNoteR-Core.png` | `FlickV5 Right.mat` `_Core` |
| `5e37adda88330934ba564a8f3e176fd8` | `Texture2D/flickv5.png` | `Mat.001.mat` `_BaseMap`/`_MainTex` |
| `e7e6858aa74220a4eaec272c99738a7f` | `Texture2D/FlickNoteCapR.png` | `Mat.001 R.mat` `_BaseMap`/`_MainTex` |
| `ca899750f79dad140a6a10e716a3390e` | `Texture2D/notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png` | `matid_flicknote.mat` |
| `73eaf6dfbbb220943835635eff68fba6` | `Texture2D/cursor-flick-indicator-left.png` | `cursor-flick-indicator-left.mat` `_MainTex` |
| `84e25481c81100142a7bdaf20566b4d0` | `Texture2D/cursor-flick-indicator-right.png` | `cursor-flick-indicator-right.mat` `_MainTex` |
| `bd6bc08b96d83b74c85a11cd15b19afb` | `Texture2D/FlickBurst.png`（4096×2048） | 64 个 `Sprite/FlickBurst_0..63.asset`（每个 sprite 的 `m_RD.texture`） |
| `8cc04feeb5505714eaaaeba565a1d9d5` | `Texture2D/FlickBurstR.png`（4096×2048） | 64 个 `Sprite/FlickBurstR_0..63.asset` |
| `ecbdb5dfd9f05614d885738f327fbce4` | `AnimatorController/FlickBurst_0.controller` | `GameplayRuntime.unity` 里 `FlickBurst_0` 的 Animator（4 处）+ `FlickBurst_0 R.overrideController` 的 `m_Controller` |
| `1008c027bd275a84aa4d59fcc953439f` | `AnimatorOverrideController/FlickBurst_0 R.overrideController` | `GameplayRuntime.unity` 里 `FlickBurstR_0` 的 Animator（4 处） |
| `15a1ca7fbadc11c43aa9ae62bc560bb9` | `AnimationClip/FlickBurstAnim.anim`（`m_StopTime: 0.5333333`，120 fps，不循环） | `FlickBurst_0.controller:127` 的 `m_Motion`；也是 override 的 `m_OriginalClip` |
| `066799a41cdcc3c498f65da5d429a4fc` | `AnimationClip/FlickBurstAnimR.anim`（同样 0.5333s） | `FlickBurst_0 R.overrideController:13` 的 `m_OverrideClip` |
| `3513d8209888d7645b748438178acf65` | `Sprite/FlickBurst_0.asset` | `GameplayRuntime.unity`（`FlickBurst_0` SpriteRenderer 的 `m_Sprite`）；被 `FlickBurstAnim.anim` 第 1 帧引用 |
| `85389c78eeaff27439fcec9fdb3914ca` | `Sprite/FlickBurstR_0.asset` | `GameplayRuntime.unity`（`FlickBurstR_0`）；`FlickBurstAnimR.anim` 第 1 帧 |
| `ab09877e2e707104187f6f83e2f62510` | `Assets/Settings/DefaultVolumeProfile.asset` | `UniversalRenderPipelineGlobalSettings.asset:365` 的 `m_VolumeProfile`（**这就是 Flick 发光能出现的前提**） |
| `4b83569d67af61e458304325a23e5dfd` | `Assets/Settings/PC_RPAsset.asset` | `ProjectSettings/GraphicsSettings.asset:47` + `QualitySettings.asset:105` |
| `1bf8e8880b288944eb6edcdfea4591dd` | `Gameplay/Audio/Resources/60_note_flick_5_draft2.wav` | **0 处 guid 引用**，靠 `Resources.Load<AudioClip>("60_note_flick_5_draft2")` 按名加载（`GameplayBootstrap.cs:1007`） |

**引擎内置 guid（不是工程文件，不要去找）**：

| guid / fileID | 含义 | 出现在 |
|---|---|---|
| `0000000000000000f000000000000000` + `fileID: 10753` | 内置 `Sprites/Default` | `FlickBurstMaterial.mat:11` |
| `d0353a89b1f911e48b9e16bdc9f2e058` | URP 包内的 `Editor/AssetVersion.cs`（`Unity.RenderPipelines.Universal.Editor`，实测 `Library/PackageCache/com.unity.render-pipelines.universal@746735fb06f2/Editor/AssetVersion.cs.meta`） | `Mat.001.mat`、`Mat.001 R.mat`、`matid_flicknote.mat` 尾部 `!u!114` 块（`version: 10`）。**升级 URP 版本时这个块会被 Unity 重写，属正常** |

Sprite 完整性实测：`Sprite/FlickBurst_*.asset` = **64** 个、`FlickBurstR_*.asset` = **64** 个，共 128 个 asset + 128 个 `.meta`，**全部存在**；两个 AnimationClip 各 128 条 sprite 关键帧、64 个唯一 guid，**0 个 guid 解析失败**。

---

## 4. Shader 参数 → 写入方对照

### 4.1 `dc.shader`（尾巴）—— 材质静态配置

`dc.shader:69-117` 的 Properties，实测两个材质里 **只有 4 个键被序列化**（`_Opacity/_QueueControl/_QueueOffset/_Scale`），其余全部吃 shader 默认值。

| 参数 | 默认（`dc.shader`） | 作用 | 谁写 |
|---|---|---|---|
| `_Base` / `_Bar` / `_Core` | `"white"` | 三张贴图槽 | 材质静态（`[NoScaleOffset]`，`_ST` 恒为 (1,1,0,0)） |
| `_Scale` | 1.0 | UV 缩放（乘法） | 材质静态 = 1 |
| `_Opacity` | 1.0 | **唯一的整体不透明度通道**（`dc.shader:246` `a = max(base.a, core.a) * _Opacity`） | 材质静态 1 + **MPB 运行时** |
| `_Color` | (1,1,1,1) | RGB 染色乘子（`dc.shader:254`）；**alpha 通道不参与合成** | 材质静态白 + **MPB 运行时**写 RGB |
| `_CoreBoost` | 1.6 | 芯线额外亮度（`dc.shader:318`） | 材质静态 1.6（仅 Left 有，Right 缺省→吃 shader 默认） |
| `_BarRepeat` | 10.0 | 条纹条数（`dc.shader:227` `barU = uv.x*_Scale*_BarRepeat + _Bar_ST.z`） | **仅 MPB 运行时**（材质里没这个键） |
| `_BarContrast` | 2.6 | 条纹对比（`dc.shader:239`） | 材质静态（缺省→shader 默认 2.6） |
| `_TailLift` | 1.45 | 尾巴提亮（`dc.shader:264`） | shader 默认 |
| `_TailOpacity` | 1.0 | 尾巴单独降透明（`dc.shader:306`） | shader 默认 |
| `_TailDetint` | 0.40 | 尾巴去染色（`dc.shader:263`） | shader 默认 |
| `_BarStrength` | 0.55 | 条纹强度（`dc.shader:273`） | shader 默认 |
| `_Desaturate` | 0.0 | 去饱和 | shader 默认 |
| `_SaturationTrim` | 0.72 | 饱和修剪（`dc.shader:293`） | shader 默认 |
| `_OverallDim` | 0.86 | 整体压暗（`dc.shader:287`） | shader 默认 |
| `_GlowPower` | 1.8 | HDR 越过 Bloom 阈值（`dc.shader:328` 在**预乘之后**叠加） | shader 默认 |
| `_Surface/_Blend/_SrcBlend/.../_QueueControl` | 见 `:102-116` | Shader Graph 表面设置存根 | 材质静态（Left 有显式值，Right 缺省） |

### 4.2 `MaterialPropertyBlock` 运行时写入（唯一写入点）

`NoteRendererPropertyState.Apply(...)`（`NoteRendererPropertyState.cs:41-107`）是 Flick 唯一的着色入口：

| 参数 | PropertyToID 行 | 写入行 | Flick 是否写 | 值 |
|---|---|---|---|---|
| `_Color` | `:16` | `:83` | ✅ | `direction == 4` → **(0.2, 0.95, 0.55, fade)** 绿；否则 **(1, 0.82, 0.18, fade)** 黄（`:72-74`） |
| `_Opacity` | `:19` | `:89` | ✅ | `fade`（`:88`） |
| `_BarRepeat` | `:23` | `:97` | ✅ 仅 `repeat > 0` | `FlickTailWorldLength * 11.0`（`NoteRenderer.cs:2632-2633`） |
| `_BaseColor` | `:17` | `:85` | ❌ Flick 不写（仅 Tap 走 `:78`） | — |
| `_MainColor` | `:18` | `:87` | ❌ | — |
| `_Held/_Progress/_ModulateProgress` | `:20-22` | `:91-95` | ❌（仅 Hold） | — |

写回渲染器只有一处：`NoteRendererPropertyState.cs:105` `if (dirty) _renderer.SetPropertyBlock(_block);`
调用方：`NoteRenderer.cs:2652`，变更检测在 `:2636-2680`（`AppliedFade / AppliedDirection / AppliedRepeat` 等），命中即跳过——**改 MPB 但不改这些字段不会生效**。

**Flick 的 MPB `relevant` 掩码**（`NoteRendererPropertyState.cs:77`）：

```csharp
: kind == NoteKind.Flick ? Properties.Opacity | Properties.Color | (repeat > 0 ? Properties.BarRepeat : Properties.None)
```

⚠️ 由此推出一条重要事实（见 §8.5）：**箭头的材质 `Mat.001` / `Mat.001 R` 用的是 URP Unlit shader，采样的是 `_BaseColor`（`UnlitInput.hlsl` 里 `half4 _BaseColor;`），而 C# 对 Flick 只写 `_Color`**。所以运行时写入箭头 renderer 的 `_Color` 是空转；箭头的绿/黄来自**贴图本身烘死的颜色**（`flickv5.png` 偏黄 ↔ 左/dir16，`FlickNoteCapR.png` 偏青绿 ↔ 右/dir4，实测均值见 §8.5）。

---

## 5. 贴图导入设置依赖（踩过的坑，必须原样保留）

### 5.1 强制 Repeat 的两张

`dc.shader:235` 用 `SAMPLE_TEXTURE2D(_Bar, sampler_Bar, uvBar)`，而 `barU = uv.x * _Scale * _BarRepeat + _Bar_ST.z`（`:227`）在 `_BarRepeat > 1` 时 **U 会大幅超出 0..1**。`_Bar` 是 64×128 的**单条纹单元**，一旦被 Clamp，超出的部分全钳到边缘一列 → **斜纹完全消失，尾巴变纯色**。

| 文件 | 行 | 值 | 含义 |
|---|---|---|---|
| `Texture2D/FlickNote-Bar.png.meta` | `:42-43` | `wrapU: 0` / `wrapV: 0` | **Repeat / Repeat** ✅ 必需 |
| 同上 | `:39` | `filterMode: 1` | Bilinear |
| 同上 | `:11` | `enableMipMap: 0` | 无 mip（单条纹 + 高频重复，有 mip 会糊） |
| `Texture2D/FlickNoteR-Bar.png.meta` | `:42-43` | `wrapU: 0` / `wrapV: 0` | **Repeat / Repeat** ✅ 必需 |
| 同上 | `:39` | `filterMode: 1` | Bilinear |
| 同上 | `:11` | `enableMipMap: 0` | 无 mip |

### 5.2 实测真值表（全部 17 张相关贴图，逐 meta 读的）

| 贴图 | 尺寸 | filterMode | wrapU/wrapV | enableMipMap | sRGB |
|---|---|---|---|---|---|
| FlickNote-Bar | 64×128 | 1 | **0/0** | 0 | 1 |
| FlickNoteR-Bar | 64×128 | 1 | **0/0** | 0 | 1 |
| FlickNote-Base | 2048×128 | 2 | 1/1 | 1 | 1 |
| FlickNote-Core | 2048×128 | 2 | 1/1 | 1 | 1 |
| FlickNoteR-Base | 2048×128 | 2 | 1/1 | 1 | 1 |
| FlickNoteR-Core | 2048×128 | 2 | 1/1 | 1 | 1 |
| FlickBurst | 4096×2048 | 2 | 1/1 | 0 | 1 |
| FlickBurstR | 4096×2048 | 2 | 1/1 | 0 | 1 |
| FlickNoteCapR | 2048×2048 | 2 | 1/1 | 0 | 1 |
| flickv5 | 2048×2048 | 1 | **0/0** | 1 | 1 |
| cursor-flick-indicator-left | 512×512 | 1 | 1/1 | 0 | 1 |
| cursor-flick-indicator-right | 512×512 | 1 | 1/1 | 0 | 1 |
| notes_track_…Orange | 2048×2048 | 1 | **0/0** | 1 | 1 |
| judgement-01-ExactPlus | 512×512 | 1 | 1/1 | 0 | 1 |
| judgement-02-Exact | 512×512 | 1 | 1/1 | 0 | 1 |
| judgement-03-Near | 512×512 | 1 | 1/1 | 0 | 1 |
| judgement-04-Break | 512×512 | 1 | 1/1 | 0 | 1 |

> 说明：`_Base` / `_Core` 在 shader 里走 `uvBase = float2(saturate(uv.x), saturate(uv.y))`（`dc.shader:215`），Clamp/Repeat 都不影响；`flickv5` / `notes_track_…Orange` 的 `0/0`（Repeat）是恢复资产自带的，不是本包改的。

### 5.3 这个设置目前只在工作区里，没进 git

`git show HEAD:` 对比实测：

| 项 | HEAD | 工作区 |
|---|---|---|
| FlickNote-Bar `wrapU/V` | `1 / 1`（Clamp） | **`0 / 0`** |
| FlickNote-Bar `filterMode` | `2`（Point） | **`1`**（Bilinear） |
| FlickNoteR-Bar `wrapU/V` | `1 / 1` | **`0 / 0`** |
| FlickNoteR-Bar `filterMode` | `2` | **`1`** |

⚠️ 等于说：**如果有人从 git 干净 checkout，斜纹会全部消失**。见 §8.1。

---

## 6. 渲染队列 / 排序依赖

### 6.1 实测队列值

| 资产 | `m_CustomRenderQueue` | 行 | 说明 |
|---|---|---|---|
| `FlickV5 Left.mat` | **3012** | `:19` | 尾巴 |
| `FlickV5 Right.mat` | **3012** | `:19` | 尾巴 |
| `Mat.001.mat` | **3012** | `:20` | 箭头（**不是 3014**，见 §8.2） |
| `Mat.001 R.mat` | **3012** | `:33` | 箭头 |
| `FlickBurstMaterial.mat` | **3015** | `:19` | 场景模板用（运行时被替换，见 §6.3） |
| `matid_flicknote.mat` | **3003** | `:20` | 配 `_QueueOffset: 3` |
| `cursor-flick-indicator-left.mat` | **3008** | `:19` | |
| `cursor-flick-indicator-right.mat` | **3008** | `:19` | |

队列由 C# 运行时决定的：

| 材质 | 队列 | 代码 |
|---|---|---|
| `PhaseA_FlickBurst_Runtime` | **3015** | `FlickHitEffectRenderer.cs:184` |
| `PhaseA_FlickFeedback_*` | **3110** | `FlickHitEffectRenderer.cs:335`（+ `sortingOrder = 20`，`:310`） |
| Firmament 括号材质 | 3012 | `NoteRenderer.cs:496`（非 Flick，但同队列，注意遮蔽顺序） |

`_QueueOffset` 只影响 URP 的 `BaseShaderGUI` 一次性写回（`AssignQueue` 在 URP 包 `Editor/ShaderGUI/BaseShaderGUI.cs` 里做 `renderQueue = (int)RenderQueue.Transparent + _QueueOffset`），运行时以 **`m_CustomRenderQueue` 字面值为准**。实测：`Mat.001` / `Mat.001 R` 的 `_QueueOffset: 12`（3000+12=3012，自洽）、`matid_flicknote` 的 `_QueueOffset: 3`（3000+3=3003，自洽）、`FlickV5 *` 与 `cursor-flick-indicator-*` 都是 `0`（靠 `_QueueControl` + 显式 `m_CustomRenderQueue` 生效）。

### 6.2 Bloom（发光的唯一来源）

`dc.shader:308-328` 的 HDR 推力必须由 Bloom 接住，否则只是「亮一点」而没有光晕。

`Assets/Settings/DefaultVolumeProfile.asset`（guid `ab09877e2e707104187f6f83e2f62510`）：

```
:358   m_Name: Bloom
:360   active: 1
:362   skipIterations: m_OverrideState: 1 / m_Value: 1
:364   threshold:  m_OverrideState: 1 / m_Value: 1.5      ← 三个都必须 OverrideState=1
:367   intensity:  m_OverrideState: 1 / m_Value: 2
:370   scatter:    m_OverrideState: 1 / m_Value: 0.821
:373   clamp:      m_OverrideState: 1 / m_Value: 65472
```

接线链（缺一环就没有 Bloom）：

1. `ProjectSettings/GraphicsSettings.asset:47` → `m_CustomRenderPipeline` = `PC_RPAsset.asset`（`4b83569d…`）
2. `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset:365` → `m_VolumeProfile` = `DefaultVolumeProfile.asset`（`ab09877e…`）
3. 相机 `UniversalAdditionalCameraData`（`GameplayRuntime.unity`，`!u!114 &1457164153`）：`m_RenderPostProcessing: 1`、`m_VolumeLayerMask.m_Bits: 1`
4. PC RP Asset：`m_SupportsHDR: 1`、`m_HDRColorBufferPrecision: 0`、`m_MSAA: 4`

⚠️ 场景里**没有任何 `Volume` 组件**（`grep "UnityEngine.Rendering.Volume"` 计数 = 0），全靠 URP 的 **Default Volume Profile**。所以「移植到别的工程」时如果只拷场景不拷 GlobalSettings/Volume profile，**Flick 一定不发光**。

### 6.3 FlickBurst 排序细节

- 场景里的 `FlickBurst_0` / `FlickBurstR_0` SpriteRenderer：`m_SortingOrder: 0`、`m_SortingLayer: 0`、material = `FlickBurstMaterial`（3015）。
- 运行时 `FlickHitEffectRenderer.BuildBurstPool` 把池化 clone 的 `sharedMaterial` 整体替换成 `CreateBurstMaterial()` 产出的 `PhaseA_FlickBurst_Runtime`（`Shader.Find("InFalsus/Compat/StenciledUnlitSprite")`，`renderQueue = 3015`，`_ZTest = Always`）。
- → **`FlickBurstMaterial.mat` 的 3015 只在「没跑 FlickHitEffectRenderer 的裸场景」里生效**；其 `m_Shader` 是内置 `Sprites/Default`（`fileID: 10753, guid: 0000000000000000f000000000000000`），属**引擎内置依赖**，不需要拷贝任何工程文件。
- `_ZTest = Always` 让爆闪无视深度，`sortingOrder = 20` 只作用于 feedback 条。

---

## 7. 音频依赖

| 项 | 值 |
|---|---|
| 文件 | `Assets/InFalsus/Gameplay/Audio/Resources/60_note_flick_5_draft2.wav` |
| guid | `1bf8e8880b288944eb6edcdfea4591dd` |
| 格式（实测 wave 头） | 2ch / 16bit / **48000 Hz** / 42392 frames → **0.8832 s** |
| 大小 | 169,612 bytes |
| AudioImporter | `loadType: 0`（Decompress On Load）、`compressionFormat: 0`（PCM）、`sampleRateOverride: 44100`、`preloadAudioData: 1`、`normalize: 1`、`forceToMono: 0` |

**引用方式是字符串，不是 guid**：

- `GameplayBootstrap.cs:49` `public AudioClip flickHitClip;`（可在 Inspector 挂；实测场景未挂）
- `GameplayBootstrap.cs:299` `LoadRequiredClip(flickHitClip, "60_note_flick_5_draft2")`
- `GameplayBootstrap.cs:1001-1013` `LoadRequiredClip`：字段为空 → `Resources.Load<AudioClip>(resourceName)`（`:1007`）；**再为空直接抛 `InvalidOperationException("Gameplay audio clip is missing: …")`（`:1010`）**
- → 该 wav 必须在某个 `Resources/` 目录下，且**名字逐字符一致**（含 `_5_draft2`）。改名 = 运行时崩。
- 消费方：`GameplayHitAudio.cs:155/164`（`flickClip` → `_flickClip`）、`:591-595`（`case NoteKind.Flick` → `PlayOneShot(_flickClip, HitSoundCategory.Flick, note.TimeMs + audioTimeOffsetMs)`）、音量 `:906` 取 `_flickVolume`（来源 `FalsumPreferences.cs:113` `FlickVolume`，默认 1.0）。
- 自检覆盖：`Editor/GameplaySelfCheck.cs:465` 也在按同一路径加载它，缺失会在自检阶段报 `Gameplay audio clip is missing`。

---

## 8. 已知缺口 / 注意事项（全部经实测确认）

### 8.1 ⚠️ 最大风险：整套 Flick 修复**都还没提交**

`git status --porcelain` + `git show HEAD:` 对比：

| 文件 | git 状态 | HEAD 里的样子 |
|---|---|---|
| `Recovered/dc.shader` | ` M` | **261 行**，无 `_BarRepeat`（`grep -c _BarRepeat` = 0）→ 无斜纹、无 tail lift、无 HDR glow |
| `Recovered/Material/FlickV5 Left.mat` | ` M` | 结构是旧序列化格式 |
| `Recovered/Material/Mat.001.mat` / `Mat.001 R.mat` | ` M` | `m_ValidKeywords`/`m_InvalidKeywords` 位置不同（Unity 重写导致，无功能差异） |
| `Texture2D/FlickNote-Bar.png.meta` | ` M` | `wrapU/V = 1/1`（Clamp）、`filterMode = 2` |
| `Texture2D/FlickNoteR-Bar.png.meta` | ` M` | 同上 |
| `Texture2D/FlickNote-{Base,Core}.meta`、`FlickNoteR-{Base,Core}.meta` | ` M` | `enableMipMap: 0`、`FlickNote-Core`/`FlickNoteR-Core` 的 `wrapU/V` 曾是 `0/0` |
| `Settings/DefaultVolumeProfile.asset` | ` M` | Bloom `threshold 0.9 / intensity 0 / scatter 0.7` → **HEAD 的 Bloom 是关的（intensity=0）** |

**结论**：本包里的 `00_docs/design/Flick_*.md` 描述的效果，**只在当前工作区成立**。任何「干净 checkout + 只拷包」的组合都会退回无斜纹、无发光的状态。打包时必须连带这些 `.meta` 与 `dc.shader` 的**工作区版本**，并且不要相信 `git stash`/`checkout` 能还原。

### 8.2 「箭头队列 3014」不存在（文档承诺未落地）

`Flick_Round5_Fix.md:32/:37/:116` 声称 `Mat.001` / `Mat.001 R` 的 renderQueue 已从 3012 提到 **3014**，让箭头盖住尾巴。实测：

- `Mat.001.mat:20` → `m_CustomRenderQueue: 3012`
- `Mat.001 R.mat:33` → `m_CustomRenderQueue: 3012`
- `git show HEAD:` 两者同为 `3012`
- 全工程 `grep -rn "3014"` 在 `*.mat/*.cs/*.asset/*.prefab/*.unity/*.shader` 内 **零命中**（唯一命中是 `FZG_HAX.otf` 的二进制字节）
- `git log -S "3014" --all` 只命中初始提交（即从未有过 3014 的文本）

**真相：箭头与尾巴当前同为 3012**，同一队列内的绘制顺序只由 URP 的排序策略（材质/距离）决定，**没有显式保证箭头在尾巴之上**。若观察到「箭头被尾巴盖住」，不要以为改了 3014——那是没改过的。要么真去改 `m_CustomRenderQueue`（并把两份材质+HEAD 一起改），要么改 shader 的 `ZTest`/`Cull`。这一条建议列入后续 TODO。

### 8.3 三个恢复资产是「未接线」的（C# 里零引用）

| 资产 | 场景里的位置 | 状态判定 |
|---|---|---|
| `Mesh/flicknote01.asset`（guid `5f61dc87…`，10 顶点） | `…/GAMEPLAY/FLICK_NOTE/flicknote01`（MeshRenderer 用 `matid_flicknote`） | ⛔ 不参与 Flick 渲染 |
| `Mesh/flickunderscore01.asset`（guid `92bc5e89…`） | `…/GAMEPLAY/FLICK_NOTE/flickunderscore01` | ⛔ 同上 |
| `Material/matid_flicknote.mat`（guid `2cbee7aa…`） | 只被上面两个 MeshRenderer 用 | ⛔ 只服务这两个死节点 |
| `Material/cursor-flick-indicator-left/right.mat` + 两张贴图 | `…/JUDGE_SKY/SkyCursorExtrasContainer/cursor-flick-indicator-{left,right}` | ⛔ 容器在 `DefaultHiddenPaths`（`GameplaySceneBinding.cs:160`）被 `SetActive(false)`，且 C# 里搜不到任何引用 |

证据（逐条 grep 过）：

- `grep -rn "flicknote01\|flickunderscore01" Assets --include="*.cs"` → **0 命中**
- `grep -rn "cursor-flick-indicator" Assets --include="*.cs"` → **0 命中**
- `grep -rn "matid_flicknote" Assets --include="*.cs"` → **0 命中**
- `FLICK_NOTE` 的唯一用途是 `Require(...)` 拿到引用后 `SetActive(false)`（`GameplaySceneBinding.cs:225-227` + `:292-295`）。**`NoteRenderer` 生成 flick 时用的是 `_flickLeftPrefab` / `_flickRightPrefab`（`NoteRenderer.cs:1527`），从来不用 `binding.FlickTemplate`。**
- `GameplaySceneBuilder.cs:138` / `GameplayBootstrap.cs:260` 都会跑 `ApplyPhaseAPolicy()`，每次都把 `v5-3`、`v5-3R`、`SkyCursorExtrasContainer`、`FLICK_NOTE` 关掉。

**结论**：这 4 类资产是**原始 bundle 里的对应物，恢复进来了但没接线**。它们的存在意义是「完整性/复核」，不是运行时必需。打包时**可以不带**（但 `v5-3`/`v5-3R` 节点本身不能删——`Require` 会在 `DefaultHiddenPaths` 之前就把它们当普通节点处理，删了虽不抛异常但会让场景与原版不一致；`matid_flicknote` 只被死节点用，删了那俩节点的材质变粉，视觉上无感）。

### 8.4 `Infalsus_Common.hlsl` 有两份，`Shader/` 下那份是死的

- `Recovered/Infalsus_Common.hlsl`（`7ff17137…`）
- `Recovered/Shader/Infalsus_Common.hlsl`（`175499b6…`）
- `md5` 完全相同：`0d5612ede74699ca2f7a58a47f26efd0`
- include 方式是**相对路径**：`grep 'Infalsus_Common.hlsl'` 只命中 `a9.shader:137`、`c.shader:126`、`dc.shader:150/:346`，这三个 shader 都在 `Recovered/` 根下 → 解析到的都是**根目录那份**。
- → `Recovered/Shader/Infalsus_Common.hlsl` **没有任何 includer**，可以整份删掉（但删前确认 `Recovered/Shader/` 下 4 个 shader 都没 include 它——实测确实没有）。

打包建议：只放根目录一份，与 `dc.shader` 同级；`Shader/` 那份不要放（避免两份不一致导致排错时改错文件）。

### 8.5 箭头不吃 MPB 的 `_Color`（方向色是烘焙的）

- 箭头 shader = URP Unlit 复刻（`Universal Render Pipeline_Unlit_1.shader:5-6`：`_BaseMap` + `[MainColor] _BaseColor`），HLSL 走 `UnlitInput.hlsl` 的 `half4 _BaseColor;`。
- C# 对 Flick 只写 `_Color`（`NoteRendererPropertyState.cs:77` 掩码里没有 `BaseColor`）。
- `_Color` 在该 shader 里只是 `:29` 的 `[HideInInspector]` 兼容属性，**HLSL 不采样**。
- 方向色实际来自贴图（实测不透明像素均值，0-255）：
  - `flickv5.png`（左 prefab / dir 16）= **(135, 131, 72)** → 偏黄 ✅ 与「16 = 黄/左」一致
  - `FlickNoteCapR.png`（右 prefab / dir 4）= **(72, 130, 110)** → 偏青绿 ✅ 与「4 = 绿/右」一致
- 另：`Mat.001` / `Mat.001 R` 的 `m_Floats` 里**没有 `_Opacity`**，而 `_Color.a` 在 `dc.shader` 里也不参与合成（`:246` 只用 `_Opacity`）→ **箭头完全不受 fade 影响**，只有尾巴会随 `fade` 淡出。
- 想「运行时按方向改箭头颜色」，必须额外写 `_BaseColor`（改 `NoteRendererPropertyState.cs:77` 的掩码 + `:85` 的写入条件），否则改了没用。

### 8.6 死了的常量

`NoteRenderer.cs:218` `private const float FlickHeadOverlap = 0.08f;` —— 全文件出现 **1 次**（仅声明），**从未被引用**。改成任何值都不会影响渲染。真正有效的是 `:222 FlickTailInset = 0.0`（3 次）、`:225 FlickVertexSeamOffset = 0.0`（4 次）。

### 8.7 `Shader.Find` 名字冲突（6 个同 name shader）

```
Assets/InFalsus/Recovered/8.shader    guid 4d3eeb8996f29c34ab192c0820e9ecbe
Assets/InFalsus/Recovered/9a.shader   guid 29378f1c321fc3d4581007835b579e98   ← cursor-flick-indicator-* 用这个
Assets/InFalsus/Recovered/a4.shader   guid c79253176b482bb42a85f08147a4a8ef
Assets/InFalsus/Recovered/be6.shader  guid eefff922dad7e67469d8b0a325af3516
Assets/InFalsus/Recovered/f4.shader   guid 4b21c754a9ce9a146a80f6593dd39aa5
Assets/InFalsus/Shaders/Compat/StenciledUnlitSprite.shader  guid 987469678201cd54e97ad21a588090fb
```

6 个文件的 `Shader "InFalsus/Compat/StenciledUnlitSprite"` **完全同名**；前 5 个（`Recovered/` 下）内容**逐字节相同**（`diff` 全为 0）；只有 `Compat/` 那份多出 `_IgnoreVertexColor` 属性与对应 lerp（108 行 vs 102 行）。

`FlickHitEffectRenderer.cs:170` 用 `Shader.Find("InFalsus/Compat/StenciledUnlitSprite")` —— **名字歧义**，Unity 会返回它内部找到的任意一个（通常是最先导入/第一个匹配）。这意味着爆闪实际用的可能是 `Recovered/` 那 5 个无 `_IgnoreVertexColor` 的版本。属**潜在不一致**，建议后续把 5 个重复的删掉或改名（但删前要确认没有材质引用它们：`8/a4/be6/f4` 的 guid 在工程里 **0 引用**，`9a` 被 8 个材质引用 → 只能删 8/a4/be6/f4）。

### 8.8 其它易漏点

- `dc.shader` 的 `Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha`（`:138`）必须与 `IF_Premultiply`（`Infalsus_Common.hlsl:127`，`INFALSUS_PREMULTIPLY` 默认 1）成对。改成 `Blend SrcAlpha OneMinusSrcAlpha` 会让 alpha 乘两次 → 暗一档且 HDR 顶不到 Bloom 阈值（`Infalsus_Common.hlsl:117-134` 的注释就是这么写的）。
- `dc.shader:227-228` 的 `barV = uv.y * _Scale * 0.0 + _Bar_ST.w` —— V 的乘数**必须是 0**（照原版 DXBC 的 `l(32.0,0.0,0.0,0.0)`）。给 V 加重复会让条纹变网格。
- `dc.shader:215` 的 `saturate(uv.x/uv.y)` 只对 `_Base`/`_Core` 生效；`_Bar` 用未饱和的 `uvBar`，这是斜纹能铺满的前提。
- `dc.shader:323` `if (a <= 0.0005) discard;` —— 不要加更硬的 alpha 阈值，会让板材边缘出现阶梯锯齿（`:320-322` 注释记载过这个坑）。
- `dc.shader:389` Fallback 到 ShaderGraph 包内 shader；`dc.shader:390` CustomEditor 依赖 `com.unity.shadergraph`。
- flicks 的**子节点名** `Flick` / `Plane` 被 `NoteRenderer.cs:1623-1624` 硬编码查找；`Plane` 下还有 `PLANE_START` / `PLANE_END` 两个空节点（被 TrackEnd 功能用作锚点），改名会连带打断 TrackEnd。
- 场景里 `FlickBurst_0` / `FlickBurstR_0` 的 `m_IsActive: 0`（模板常态关闭），`FLICK_NOTE` / `flicknote01` / `flickunderscore01` 是 `m_IsActive: 1`（靠运行时 `ApplyPhaseAPolicy` 关）。

---

## 9. 完整性自检命令（PowerShell，可在工程根直接跑）

```powershell
# 0) 准备工作
$proj = (Get-Location).Path          # 必须是 InFalsus-Gameplay 根
Set-Location $proj
$R = "Assets/InFalsus/Recovered"

# 1) 硬依赖文件 + .meta 是否齐全（缺一即列红）
$hard = @(
  "$R/dc.shader", "$R/Infalsus_Common.hlsl",
  "$R/Material/FlickV5 Left.mat", "$R/Material/FlickV5 Right.mat",
  "$R/Material/Mat.001.mat", "$R/Material/Mat.001 R.mat",
  "$R/Material/FlickBurstMaterial.mat", "$R/Material/matid_flicknote.mat",
  "$R/Material/cursor-flick-indicator-left.mat", "$R/Material/cursor-flick-indicator-right.mat",
  "$R/Mesh/Flick.asset", "$R/Mesh/Plane.asset",
  "$R/Mesh/flicknote01.asset", "$R/Mesh/flickunderscore01.asset",
  "$R/GameObject/v5-3.prefab", "$R/GameObject/v5-3R.prefab",
  "$R/Texture2D/FlickNote-Bar.png", "$R/Texture2D/FlickNote-Base.png", "$R/Texture2D/FlickNote-Core.png",
  "$R/Texture2D/FlickNoteR-Bar.png", "$R/Texture2D/FlickNoteR-Base.png", "$R/Texture2D/FlickNoteR-Core.png",
  "$R/Texture2D/flickv5.png", "$R/Texture2D/FlickNoteCapR.png",
  "$R/Texture2D/notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png",
  "$R/Texture2D/FlickBurst.png", "$R/Texture2D/FlickBurstR.png",
  "$R/Texture2D/cursor-flick-indicator-left.png", "$R/Texture2D/cursor-flick-indicator-right.png",
  "$R/AnimatorController/FlickBurst_0.controller",
  "$R/AnimatorOverrideController/FlickBurst_0 R.overrideController",
  "$R/AnimationClip/FlickBurstAnim.anim", "$R/AnimationClip/FlickBurstAnimR.anim",
  "Assets/InFalsus/Gameplay/Audio/Resources/60_note_flick_5_draft2.wav",
  "Assets/Settings/DefaultVolumeProfile.asset"
)
$missing = $hard | Where-Object { -not (Test-Path $_) }
$noMeta  = $hard | Where-Object { (Test-Path $_) -and -not (Test-Path "$_.meta") }
if ($missing) { "MISSING FILE : $($missing -join '; ')" } else { "files OK ($($hard.Count))" }
if ($noMeta)  { "MISSING META : $($noMeta  -join '; ')" } else { "metas OK" }

# 2) 128 张爆闪 Sprite + 128 个 meta
$sp = Get-ChildItem "$R/Sprite" -Filter "FlickBurst*.asset"
"sprites = $($sp.Count)  metas = $(($sp | Where-Object { Test-Path "$($_.FullName).meta" }).Count)   (应为 128 / 128)"

# 3) guid 逐条核对（值必须与 §3 完全一致）
$expect = @{
  "$R/dc.shader"            = "55058b1d3da57a04db6ad7341b35e048"
  "$R/Infalsus_Common.hlsl" = "7ff1713785ac4914e8866a83365b3c0c"
  "$R/Material/FlickV5 Left.mat"  = "5ce7c37f26e917d499575f6e71d17b34"
  "$R/Material/FlickV5 Right.mat" = "96bf87a014d661d4f8e5c1b55e79fe18"
  "$R/Material/Mat.001.mat"       = "5b65e1ef0a12ed24380b367ec857314d"
  "$R/Material/Mat.001 R.mat"     = "65cb8f12c4a4fd64dbacf621e55977e4"
  "$R/Mesh/Flick.asset"           = "2ba5deb3d2fba2e4f9188d6121b2958a"
  "$R/Mesh/Plane.asset"           = "cb51b6c7bce3f9b478194025ef511b05"
  "$R/Texture2D/FlickNote-Bar.png" = "acc55bc2210e247479ba756f78493f6b"
  "$R/Texture2D/FlickNoteR-Bar.png"= "a6d4070345bd3ce49871046d32d28b03"
}
foreach ($k in $expect.Keys) {
  $g = (Select-String -Path "$k.meta" -Pattern '^guid: (\w+)').Matches[0].Groups[1].Value
  if ($g -ne $expect[$k]) { "GUID MISMATCH: $k  want=$($expect[$k])  got=$g" }
}
"guid check done"

# 4) 斜纹前提：两张 Bar 必须是 wrapU=0 / wrapV=0
foreach ($t in "FlickNote-Bar","FlickNoteR-Bar") {
  $m = "$R/Texture2D/$t.png.meta"
  $u = (Select-String -Path $m -Pattern 'wrapU: (\d)').Matches[0].Groups[1].Value
  $v = (Select-String -Path $m -Pattern 'wrapV: (\d)').Matches[0].Groups[1].Value
  if ($u -ne "0" -or $v -ne "0") { "WRAP BAD: $t = $u/$v  (必须 0/0，否则斜纹全消失)" } else { "wrap OK: $t = 0/0" }
}

# 5) 渲染队列
$q = @{}
foreach ($m in "FlickV5 Left","FlickV5 Right","Mat.001","Mat.001 R","FlickBurstMaterial","matid_flicknote","cursor-flick-indicator-left","cursor-flick-indicator-right") {
  $q[$m] = (Select-String -Path "$R/Material/$m.mat" -Pattern 'm_CustomRenderQueue: (\d+)').Matches[0].Groups[1].Value
}
$q.GetEnumerator() | Sort-Object Value | Format-Table -AutoSize
# 期望：matid_flicknote=3003, cursor-flick-indicator-*=3008,
#       FlickV5 L/R=3012, Mat.001 / Mat.001 R=3012（注意：不是 3014）, FlickBurstMaterial=3015

# 6) Bloom 必须是开的（intensity 0 = 无发光）
$p = "Assets/Settings/DefaultVolumeProfile.asset"
$blk = (Get-Content $p)[357..374] -join "`n"
if ($blk -match 'intensity:[\s\S]*?m_Value: 2\b' -and $blk -match 'threshold:[\s\S]*?m_Value: 1\.5\b') {
  "Bloom OK (threshold 1.5 / intensity 2)"
} else { "BLOOM BAD —— 对照 §6.2 检查 threshold/intensity/scatter 的 OverrideState" }

# 7) 场景锚点是否还在（按 m_Name 逐节点计数，比匹配扁平路径可靠）
#    Unity 的 .unity 是「扁平 YAML 文档 + m_Father 指针」，文件里根本不存在
#    "GAMEPLAY/FLICK_NOTE" 这种斜杠拼接串。所以只统计节点名出现次数。
$scene = "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity"
$names = @("master_scene_v10","MasterScenev10","GAMEPLAY","FLICK_NOTE",
           "JUDGEMENT_LINE","JUDGE_SKY","FlickBurst_0","FlickBurstR_0",
           "v5-3","v5-3R","SkyCursorExtrasContainer")
$txt = Get-Content $scene -Raw
foreach ($n in $names) {
  $c = ([regex]::Matches($txt, "(?m)^  m_Name: " + [regex]::Escape($n) + "$")).Count
  if ($c -lt 1) { "ANCHOR MISSING: $n" } else { "anchor ok: $n (x$c)" }
}
"anchor check done"

# 8) 工作区是否带着未提交的 Flick 修复（§8.1 —— 缺了斜纹/发光都会没）
#    ⚠ 注意：本沙箱里 PowerShell 捕获 git 输出会报「拒绝访问」，
#    所以不要用 `$x = git status ...` 的写法。这里改成「直接读文件内容」判定，
#    等价且不依赖 git：这三项是 §8.1 里最关键的三个未提交改动。
$ok = $true
$dc = Get-Content "$R/dc.shader" -Raw
if ($dc -notmatch '_BarRepeat') { $ok = $false; "WARN: dc.shader 里没有 _BarRepeat —— 是 HEAD 旧版（无斜纹）" }
$barL = Get-Content "$R/Texture2D/FlickNote-Bar.png.meta" -Raw
$barR = Get-Content "$R/Texture2D/FlickNoteR-Bar.png.meta" -Raw
if ($barL -notmatch '(?m)^\s+wrapU: 0' -or $barR -notmatch '(?m)^\s+wrapU: 0') { $ok = $false; "WARN: Bar 贴图还是 Clamp（HEAD 版）—— 无斜纹" }
$vol = Get-Content "Assets/Settings/DefaultVolumeProfile.asset" -Raw
$blk2 = ($vol -split "`n")[357..374] -join "`n"
# 用字符串包含判定，避开 PS 正则转义坑（这里写正则很容易被 shell/编辑器吃掉反斜杠）
if ($blk2 -notlike "*m_Value: 2*") { $ok = $false; "WARN: Bloom intensity 不是 2（HEAD 版是 0）—— 无发光" }
if ($ok) { "工作区带着完整 Flick 修复（等价于 git 有未提交改动）" }
else     { "注意：以上 WARN 说明当前文件是 git 旧版；打包必须确认带的是修复版" }

# 若确实要用 git，请改用「重定向到文件再读文件」的形式（PowerShell 5.1 捕获 git stdout 不可靠）：
#   git status --porcelain *> Diagnostics\git_status.txt
#   Get-Content Diagnostics\git_status.txt
#   （或直接在 Git Bash / cmd 里跑 git status --porcelain）

# 9) 编译（最权威的静态检查，约 1-3 分钟）
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
& $unity -batchmode -quit -projectPath $proj -logFile Diagnostics\UnityCompile.log
Select-String -Path Diagnostics\UnityCompile.log -Pattern "error CS|Shader error" | Select-Object -First 20

# 10) 编辑器自检（含 FlickBurst 动画断言，见 GameplaySelfCheck.cs:46-64）
& $unity -batchmode -quit -projectPath $proj `
  -logFile Diagnostics\UnityFalsumSelfCheck.log `
  -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run
Select-String -Path Diagnostics\UnityFalsumSelfCheck.log -Pattern "Flick effects passed|Flick" | Select-Object -First 10
```

---

## 附录 A：目录归属速查（本包 04_assets 内保持原目录形状）

| 包内目录 | 对应原工程路径 | 内容 |
|---|---|---|
| `01_shader/` | `Assets/InFalsus/Recovered/` | `dc.shader` + `.meta` |
| `02_runtime/` | `Assets/InFalsus/Gameplay/` | `NoteRenderer.cs`、`NoteRendererPropertyState.cs`、`FlickHitEffectRenderer.cs`、`GameplayHitAudio.cs` |
| `03_data/` | `Assets/InFalsus/Gameplay/Core/`、`Falsum/Core/` | Chart/Judgement/Falsum 数据结构 |
| `04_assets/Material/` | `Assets/InFalsus/Recovered/Material/` | 8 个 `.mat`（见 §2.a） |
| `04_assets/Mesh/` | `Assets/InFalsus/Recovered/Mesh/` | 4 个 `.asset` |
| `04_assets/GameObject/` | `Assets/InFalsus/Recovered/GameObject/` | `v5-3.prefab`、`v5-3R.prefab` |
| `04_assets/Texture2D/` | `Assets/InFalsus/Recovered/Texture2D/` | 13 张贴图 |
| `04_assets/Sprite/` | `Assets/InFalsus/Recovered/Sprite/` | 128 个 sprite + meta |
| `04_assets/AnimationClip/`、`AnimatorController/`、`AnimatorOverrideController/` | 同名目录 | 爆闪动画三件套 |
| `04_assets/Audio/Resources/` | `Assets/InFalsus/Gameplay/Audio/Resources/` | `60_note_flick_5_draft2.wav` |
| `05_config/` | `Assets/Settings/`、`Packages/`、`ProjectSettings/` | `DefaultVolumeProfile.asset`、`manifest.json`、`ProjectVersion.txt` |

## 附录 B：本篇的事实来源

| 结论 | 取证方式 |
|---|---|
| 所有 guid | 逐文件 `grep '^guid:' *.meta` |
| 反向引用（谁引用 guid） | 扫描 `Assets/` 下 626 个文本资产（`.mat/.prefab/.unity/.asset/.controller/.overrideController/.shader/.hlsl/.cs/.json/.txt`，排除 `.meta`），正则匹配 32 位 guid |
| 队列值 | `grep 'm_CustomRenderQueue'` 每个 `.mat` |
| 贴图尺寸 | Python 读 PNG IHDR |
| 贴图导入设置 | 逐 `.meta` 正则读 `wrapU/wrapV/filterMode/enableMipMap/sRGBTexture` |
| Bloom | `DefaultVolumeProfile.asset:358-375` 原文 |
| 场景锚点是否存在 | 解析 `GameplayRuntime.unity` 的 `!u!1/!u!4` 文档，重建完整 transform 路径后逐条比对 |
| 网格规模 | 读 `Mesh/*.asset` 的 `m_SubMeshes` |
| 音频格式 | Python `wave` 读头 |
| 未提交状态 | `git status --porcelain` + `git show HEAD:` + `git diff` 逐文件对比 |
| C# 引用缺口 | `grep -rn "<name>" Assets --include="*.cs"` |
| 以上全部 | **未跑 Unity 实机验证**（本次任务约定只读） |
