# Flick 实现包 · 资产搬运清单

> 本文件由 `Temp/flick_copy.py` 生成，记录**每一个**从原工程拷入包内的文件：
> 原工程路径 → 包内路径 → 字节数 → sha256 前 16 位 → 资产 guid。
> 搬运方式：`shutil.copy2`（内容 + mtime 原样复制），源工程**只读、零修改**。

## 0. 总览

- 拷贝文件总数：**710**
- 总字节数：**18924917**（18.05 MiB）
- 缺失文件数：**0**
- 其中「任务清单之外、为保证引用自洽而补充」的文件：**30** 个

| 目录 | 文件数 | 字节数 |
|---|---:|---:|
| `01_shader/` | 4 | 33036 |
| `02_runtime/` | 8 | 162530 |
| `03_data/` | 8 | 90001 |
| `04_assets/` | 324 | 14723006 |
| `05_config/` | 29 | 92205 |
| `07_full_dependents/` | 280 | 3814337 |
| `dir_meta/` | 57 | 9802 |
| **合计** | **710** | **18924917** |

## 1. 与原任务清单的三处事实修正

搬运过程中核对源工程，发现任务描述里有 3 处与磁盘实际不符，已按**实际**处理：

| # | 任务描述 | 磁盘实际 | 处理 |
|---|---|---|---|
| 1 | `03_data/` ← `Falsum/Core/ChartWriter.cs` | 实际路径是 `Assets/InFalsus/Gameplay/Falsum/Core/ChartWriter.cs`（不在 `Assets/InFalsus/Falsum/` 下，后者目录根本不存在） | 按实际路径拷贝，包内保持 `Gameplay/Falsum/Core/` 层级 |
| 2 | `04_assets/Audio/Resources/` ← `Assets/InFalsus/Recovered/Audio/Resources/60_note_flick_5_draft2.wav` | `Recovered/Audio/` 目录不存在；音频实际在 `Assets/InFalsus/Gameplay/Audio/Resources/60_note_flick_5_draft2.wav`（带 .meta） | 按实际路径拷贝 |
| 3 | `01_shader/` 的 `Infalsus_Common.hlsl` 有两处同名文件 | `Recovered/Infalsus_Common.hlsl`（guid `7ff1713785ac4914e8866a83365b3c0c`）与 `Recovered/Shader/Infalsus_Common.hlsl`（guid `175499b66b6d59b4f9a6f07487ab0416`），**内容 9363 字节、sha256 完全一致**，但 guid 不同 | `dc.shader` 里的 `#include "Infalsus_Common.hlsl"` 按 shader 所在目录解析，原工程实际命中的是 `Recovered/Infalsus_Common.hlsl`；故**只放这一份**，同目录平铺保证 include 可解析。`Shader/` 下那份未被任何 shader include，不放入包 |

## 2. 为保持引用自洽而**追加**的文件（任务清单外）

这些不在原始任务清单里，但检查 guid 引用/依赖后发现不带上就会断链，故一并打包（`原因` 列标注依据）。

| 包内路径 | 原工程路径 | 原因 |
|---|---|---|
| `01_shader/Infalsus_Common.hlsl` | `Assets/InFalsus/Recovered/Infalsus_Common.hlsl` | 取自 Recovered 根（与 dc.shader 同目录，被 #include 实际命中） |
| `01_shader/Infalsus_Common.hlsl.meta` | `Assets/InFalsus/Recovered/Infalsus_Common.hlsl.meta` | 取自 Recovered 根（与 dc.shader 同目录，被 #include 实际命中） |
| `05_config/Assets/Settings/DefaultVolumeProfile.asset` | `Assets/Settings/DefaultVolumeProfile.asset` | Bloom 全局后处理配置（Flick 发光依赖） |
| `05_config/Assets/Settings/DefaultVolumeProfile.asset.meta` | `Assets/Settings/DefaultVolumeProfile.asset.meta` | Bloom 全局后处理配置（Flick 发光依赖） |
| `05_config/Assets/Settings/PC_RPAsset.asset` | `Assets/Settings/PC_RPAsset.asset` | URP 管线资产（GraphicsSettings 指向它） |
| `05_config/Assets/Settings/PC_RPAsset.asset.meta` | `Assets/Settings/PC_RPAsset.asset.meta` | URP 管线资产（GraphicsSettings 指向它） |
| `05_config/Assets/Settings/PC_Renderer.asset` | `Assets/Settings/PC_Renderer.asset` | URP Renderer 资产 |
| `05_config/Assets/Settings/PC_Renderer.asset.meta` | `Assets/Settings/PC_Renderer.asset.meta` | URP Renderer 资产 |
| `05_config/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | URP 全局设置 |
| `05_config/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset.meta` | `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset.meta` | URP 全局设置 |
| `05_config/Assets/Settings/SampleSceneProfile.asset` | `Assets/Settings/SampleSceneProfile.asset` | PC_RPAsset 的默认 Volume Profile |
| `05_config/Assets/Settings/SampleSceneProfile.asset.meta` | `Assets/Settings/SampleSceneProfile.asset.meta` | PC_RPAsset 的默认 Volume Profile |
| `05_config/Assets/Settings/FalsumTrackEndComposerFeature.asset` | `Assets/Settings/FalsumTrackEndComposerFeature.asset` | PC_Renderer 的 Renderer Feature |
| `05_config/Assets/Settings/FalsumTrackEndComposerFeature.asset.meta` | `Assets/Settings/FalsumTrackEndComposerFeature.asset.meta` | PC_Renderer 的 Renderer Feature |
| `05_config/Assets/Settings/SkyJudgementLineEffectFeature.asset` | `Assets/Settings/SkyJudgementLineEffectFeature.asset` | PC_Renderer 的 Renderer Feature |
| `05_config/Assets/Settings/SkyJudgementLineEffectFeature.asset.meta` | `Assets/Settings/SkyJudgementLineEffectFeature.asset.meta` | PC_Renderer 的 Renderer Feature |
| `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader.meta` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader.meta` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader.meta` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader.meta` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader.meta` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader.meta` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader` | `Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader.meta` | `Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader.meta` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader` | `Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader.meta` | `Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader.meta` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Recovered/a7.shader` | `Assets/InFalsus/Recovered/a7.shader` | GraphicsSettings 预加载 shader |
| `05_config/Assets/InFalsus/Recovered/a7.shader.meta` | `Assets/InFalsus/Recovered/a7.shader.meta` | GraphicsSettings 预加载 shader |
| `07_full_dependents/Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` | `Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` | 玩法场景（场景锚点/模板挂载参考） |
| `07_full_dependents/Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity.meta` | `Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity.meta` | 玩法场景（场景锚点/模板挂载参考） |

## 3. 目录结构说明（04_assets 保留原始子目录形状）

`04_assets/` 内部**完整保留**源码 `Assets/InFalsus/Recovered/<Sub>/` 的子目录形状：

- `04_assets/Material/` — 8 个资产（+ .meta）
- `04_assets/Mesh/` — 4 个资产（+ .meta）
- `04_assets/Texture2D/` — 13 个资产（+ .meta）
- `04_assets/Sprite/` — 128 个资产（+ .meta）
- `04_assets/GameObject/` — 2 个资产（+ .meta）
- `04_assets/AnimationClip/` — 2 个资产（+ .meta）
- `04_assets/AnimatorController/` — 1 个资产（+ .meta）
- `04_assets/AnimatorOverrideController/` — 1 个资产（+ .meta）
- `04_assets/Shader/` — 2 个资产 shader（`Universal Render Pipeline_Unlit_1.shader`、`9a.shader`，是材质硬依赖；`dc.shader` 归 `01_shader/`，此处不重复）
- `04_assets/Audio/Resources/` — 1 个 wav（+ .meta）

`02_runtime/`、`03_data/`、`07_full_dependents/` 内部同样保留原始 `Assets/InFalsus/...` 层级，
便于按路径直接对位回工程。

## 4. 关键资产 guid 对照

| 包内路径 | guid |
|---|---|
| `01_shader/dc.shader` | `55058b1d3da57a04db6ad7341b35e048` |
| `01_shader/Infalsus_Common.hlsl` | `7ff1713785ac4914e8866a83365b3c0c` |
| `04_assets/Material/FlickV5 Left.mat` | `5ce7c37f26e917d499575f6e71d17b34` |
| `04_assets/Material/FlickV5 Right.mat` | `96bf87a014d661d4f8e5c1b55e79fe18` |
| `04_assets/Material/Mat.001.mat` | `5b65e1ef0a12ed24380b367ec857314d` |
| `04_assets/Material/Mat.001 R.mat` | `65cb8f12c4a4fd64dbacf621e55977e4` |
| `04_assets/Material/FlickBurstMaterial.mat` | `b1f84eec5fee4104a813ec4b386eb015` |
| `04_assets/Material/matid_flicknote.mat` | `2cbee7aa38863194ba98555da64f340f` |
| `04_assets/Mesh/Flick.asset` | `2ba5deb3d2fba2e4f9188d6121b2958a` |
| `04_assets/Mesh/Plane.asset` | `cb51b6c7bce3f9b478194025ef511b05` |
| `04_assets/GameObject/v5-3.prefab` | `c19bfe2a8cc8c2b4781ed2d7fd72fcbe` |
| `04_assets/GameObject/v5-3R.prefab` | `23de1814ce5aa2a45ac1f402234a489f` |
| `04_assets/Texture2D/FlickNote-Base.png` | `689af15bdfc563d458e755a5e2068e64` |
| `04_assets/Texture2D/FlickNote-Bar.png` | `acc55bc2210e247479ba756f78493f6b` |
| `04_assets/Texture2D/FlickNote-Core.png` | `329ff01dbde61b24abcbd1980a42b130` |
| `04_assets/Texture2D/FlickNoteR-Bar.png` | `a6d4070345bd3ce49871046d32d28b03` |
| `04_assets/AnimationClip/FlickBurstAnim.anim` | `15a1ca7fbadc11c43aa9ae62bc560bb9` |
| `04_assets/AnimationClip/FlickBurstAnimR.anim` | `066799a41cdcc3c498f65da5d429a4fc` |
| `04_assets/AnimatorController/FlickBurst_0.controller` | `ecbdb5dfd9f05614d885738f327fbce4` |
| `04_assets/AnimatorOverrideController/FlickBurst_0 R.overrideController` | `1008c027bd275a84aa4d59fcc953439f` |

## 5. 贴图导入设置（重复纹理必须 Repeat，否则斜纹全消失）

从各 `.png.meta` 的 `TextureImporter` 段直接读出：`wrapU/wrapV`：0 = Repeat，1 = Clamp。

| 贴图 | wrapU | wrapV | sRGB | alphaIsTransparency |
|---|---:|---:|---:|---:|
| `FlickBurst.png` | 1 | 1 | 1 | 1 |
| `FlickBurstR.png` | 1 | 1 | 1 | 1 |
| `FlickNote-Bar.png` | 0 | 0 | 1 | 1 ⚠️ 必须 Repeat |
| `FlickNote-Base.png` | 1 | 1 | 1 | 1 |
| `FlickNote-Core.png` | 1 | 1 | 1 | 1 |
| `FlickNoteCapR.png` | 1 | 1 | 1 | 1 |
| `FlickNoteR-Bar.png` | 0 | 0 | 1 | 1 ⚠️ 必须 Repeat |
| `FlickNoteR-Base.png` | 1 | 1 | 1 | 1 |
| `FlickNoteR-Core.png` | 1 | 1 | 1 | 1 |
| `cursor-flick-indicator-left.png` | 1 | 1 | 1 | 1 |
| `cursor-flick-indicator-right.png` | 1 | 1 | 1 | 1 |
| `flickv5.png` | 0 | 0 | 1 | 1 ⚠️ 必须 Repeat |
| `notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png` | 0 | 0 | 1 | 1 ⚠️ 必须 Repeat |

## 6. 逐文件明细

### 01_shader — Shader（dc.shader + Infalsus_Common.hlsl）

文件数 4，字节数 33036。

| # | 包内路径 | 原工程路径 | 字节 | sha256[:16] | 备注 |
|---:|---|---|---:|---|---|
| 1 | `01_shader/Infalsus_Common.hlsl` | `Assets/InFalsus/Recovered/Infalsus_Common.hlsl` | 9363 | `2439fc2a9fb2946f` | 取自 Recovered 根（与 dc.shader 同目录，被 #include 实际命中） |
| 2 | `01_shader/Infalsus_Common.hlsl.meta` | `Assets/InFalsus/Recovered/Infalsus_Common.hlsl.meta` | 161 | `f0787be94e2d9f1e` | 取自 Recovered 根（与 dc.shader 同目录，被 #include 实际命中） |
| 3 | `01_shader/dc.shader` | `Assets/InFalsus/Recovered/dc.shader` | 23236 | `53f717cd466771a1` |  |
| 4 | `01_shader/dc.shader.meta` | `Assets/InFalsus/Recovered/dc.shader.meta` | 276 | `7f2418c40b1e9ad2` |  |

### 02_runtime — 运行时渲染/特效/音频代码

文件数 8，字节数 162530。

| # | 包内路径 | 原工程路径 | 字节 | sha256[:16] | 备注 |
|---:|---|---|---:|---|---|
| 1 | `02_runtime/Gameplay/Presentation/Audio/GameplayHitAudio.cs` | `Assets/InFalsus/Gameplay/Presentation/Audio/GameplayHitAudio.cs` | 30974 | `01554bcaf264a833` | 命中音（含 flick hit clip） |
| 2 | `02_runtime/Gameplay/Presentation/Audio/GameplayHitAudio.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Audio/GameplayHitAudio.cs.meta` | 59 | `dc7d21cc5faa6945` | 命中音（含 flick hit clip） |
| 3 | `02_runtime/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs` | `Assets/InFalsus/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs` | 24611 | `9df0dfa81b832bf8` | Flick 命中特效 |
| 4 | `02_runtime/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs.meta` | 240 | `3b868a68b98466d1` | Flick 命中特效 |
| 5 | `02_runtime/Gameplay/Presentation/Notes/NoteRenderer.cs` | `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs` | 100852 | `ed0dd1d40dad8537` | NoteRenderer 主力渲染器（2902 行） |
| 6 | `02_runtime/Gameplay/Presentation/Notes/NoteRenderer.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs.meta` | 59 | `f5b209ead75b49a7` | NoteRenderer 主力渲染器（2902 行） |
| 7 | `02_runtime/Gameplay/Presentation/Notes/NoteRendererPropertyState.cs` | `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRendererPropertyState.cs` | 5675 | `1820356e4a2cada1` | NoteRenderer 的属性/写法状态 |
| 8 | `02_runtime/Gameplay/Presentation/Notes/NoteRendererPropertyState.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRendererPropertyState.cs.meta` | 60 | `d5159326cf905832` | NoteRenderer 的属性/写法状态 |

### 03_data — 谱面数据/判定/写回代码

文件数 8，字节数 90001。

| # | 包内路径 | 原工程路径 | 字节 | sha256[:16] | 备注 |
|---:|---|---|---:|---|---|
| 1 | `03_data/Gameplay/Core/Chart/ChartModels.cs` | `Assets/InFalsus/Gameplay/Core/Chart/ChartModels.cs` | 36889 | `9871df6c8dbcd899` | Chart/ChartNote 模型（FlickDirection 校验） |
| 2 | `03_data/Gameplay/Core/Chart/ChartModels.cs.meta` | `Assets/InFalsus/Gameplay/Core/Chart/ChartModels.cs.meta` | 59 | `745a616edd1a2e49` | Chart/ChartNote 模型（FlickDirection 校验） |
| 3 | `03_data/Gameplay/Core/Chart/ChartParser.cs` | `Assets/InFalsus/Gameplay/Core/Chart/ChartParser.cs` | 28039 | `eaea9722e19ffef7` | chart 文本 flick(t,center,grid,width,dir) 解析 |
| 4 | `03_data/Gameplay/Core/Chart/ChartParser.cs.meta` | `Assets/InFalsus/Gameplay/Core/Chart/ChartParser.cs.meta` | 59 | `aa6c8443dd521e14` | chart 文本 flick(t,center,grid,width,dir) 解析 |
| 5 | `03_data/Gameplay/Core/Judgement/JudgementEngine.cs` | `Assets/InFalsus/Gameplay/Core/Judgement/JudgementEngine.cs` | 12847 | `e085b859fd66bfe8` | 判定引擎 PressFlick |
| 6 | `03_data/Gameplay/Core/Judgement/JudgementEngine.cs.meta` | `Assets/InFalsus/Gameplay/Core/Judgement/JudgementEngine.cs.meta` | 59 | `4f9078b4a02e17f0` | 判定引擎 PressFlick |
| 7 | `03_data/Gameplay/Falsum/Core/ChartWriter.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/ChartWriter.cs` | 11990 | `0c28cbd76727a765` | 谱面写回（flick 事件发射） |
| 8 | `03_data/Gameplay/Falsum/Core/ChartWriter.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/ChartWriter.cs.meta` | 59 | `c529f4995bacfb80` | 谱面写回（flick 事件发射） |

### 04_assets — 资产（Material/Mesh/Texture2D/Sprite/GameObject/Animation/Animator/Audio/Shader）

文件数 324，字节数 14723006。

| # | 包内路径 | 原工程路径 | 字节 | sha256[:16] | 备注 |
|---:|---|---|---:|---|---|
| 1 | `04_assets/AnimationClip/FlickBurstAnim.anim` | `Assets/InFalsus/Recovered/AnimationClip/FlickBurstAnim.anim` | 12896 | `105ddb9209d71352` |  |
| 2 | `04_assets/AnimationClip/FlickBurstAnim.anim.meta` | `Assets/InFalsus/Recovered/AnimationClip/FlickBurstAnim.anim.meta` | 227 | `0804004706b8438e` |  |
| 3 | `04_assets/AnimationClip/FlickBurstAnimR.anim` | `Assets/InFalsus/Recovered/AnimationClip/FlickBurstAnimR.anim` | 12897 | `7961d51dddc7e870` |  |
| 4 | `04_assets/AnimationClip/FlickBurstAnimR.anim.meta` | `Assets/InFalsus/Recovered/AnimationClip/FlickBurstAnimR.anim.meta` | 227 | `c5c188792347d7cd` |  |
| 5 | `04_assets/AnimatorController/FlickBurst_0.controller` | `Assets/InFalsus/Recovered/AnimatorController/FlickBurst_0.controller` | 3835 | `32e45b9c0c266b4e` |  |
| 6 | `04_assets/AnimatorController/FlickBurst_0.controller.meta` | `Assets/InFalsus/Recovered/AnimatorController/FlickBurst_0.controller.meta` | 227 | `4cdfcfdea5ee4b17` |  |
| 7 | `04_assets/AnimatorOverrideController/FlickBurst_0 R.overrideController` | `Assets/InFalsus/Recovered/AnimatorOverrideController/FlickBurst_0 R.overrideController` | 510 | `d2df75f1ef279ed7` |  |
| 8 | `04_assets/AnimatorOverrideController/FlickBurst_0 R.overrideController.meta` | `Assets/InFalsus/Recovered/AnimatorOverrideController/FlickBurst_0 R.overrideController.meta` | 228 | `6a3dab54b4309e71` |  |
| 9 | `04_assets/Audio/Resources/60_note_flick_5_draft2.wav` | `Assets/InFalsus/Gameplay/Audio/Resources/60_note_flick_5_draft2.wav` | 169612 | `a2e8c525c7a03b8b` | 任务书写 Recovered/Audio/Resources，实际在 Gameplay/Audio/Resources |
| 10 | `04_assets/Audio/Resources/60_note_flick_5_draft2.wav.meta` | `Assets/InFalsus/Gameplay/Audio/Resources/60_note_flick_5_draft2.wav.meta` | 485 | `651b2855d18e4e5d` | 任务书写 Recovered/Audio/Resources，实际在 Gameplay/Audio/Resources |
| 11 | `04_assets/GameObject/v5-3.prefab` | `Assets/InFalsus/Recovered/GameObject/v5-3.prefab` | 8147 | `5bf3e20b8e0aed30` |  |
| 12 | `04_assets/GameObject/v5-3.prefab.meta` | `Assets/InFalsus/Recovered/GameObject/v5-3.prefab.meta` | 290 | `512fed499ab10613` |  |
| 13 | `04_assets/GameObject/v5-3R.prefab` | `Assets/InFalsus/Recovered/GameObject/v5-3R.prefab` | 8158 | `1261b683778f1b24` |  |
| 14 | `04_assets/GameObject/v5-3R.prefab.meta` | `Assets/InFalsus/Recovered/GameObject/v5-3R.prefab.meta` | 290 | `a8eaf7225ea72b05` |  |
| 15 | `04_assets/Material/FlickBurstMaterial.mat` | `Assets/InFalsus/Recovered/Material/FlickBurstMaterial.mat` | 2000 | `9e07e3331a4f2888` |  |
| 16 | `04_assets/Material/FlickBurstMaterial.mat.meta` | `Assets/InFalsus/Recovered/Material/FlickBurstMaterial.mat.meta` | 227 | `ba0f84d24a295015` |  |
| 17 | `04_assets/Material/FlickV5 Left.mat` | `Assets/InFalsus/Recovered/Material/FlickV5 Left.mat` | 1993 | `f490aa5c4252c5de` |  |
| 18 | `04_assets/Material/FlickV5 Left.mat.meta` | `Assets/InFalsus/Recovered/Material/FlickV5 Left.mat.meta` | 227 | `0d7ef2b2ff71aa5d` |  |
| 19 | `04_assets/Material/FlickV5 Right.mat` | `Assets/InFalsus/Recovered/Material/FlickV5 Right.mat` | 1676 | `6199215e7b469b5c` |  |
| 20 | `04_assets/Material/FlickV5 Right.mat.meta` | `Assets/InFalsus/Recovered/Material/FlickV5 Right.mat.meta` | 227 | `d51a43fc66b1769a` |  |
| 21 | `04_assets/Material/Mat.001 R.mat` | `Assets/InFalsus/Recovered/Material/Mat.001 R.mat` | 2673 | `8bffbc3fa1bdd71f` |  |
| 22 | `04_assets/Material/Mat.001 R.mat.meta` | `Assets/InFalsus/Recovered/Material/Mat.001 R.mat.meta` | 227 | `c5c67d018f42dbef` |  |
| 23 | `04_assets/Material/Mat.001.mat` | `Assets/InFalsus/Recovered/Material/Mat.001.mat` | 2670 | `5f0314b1a221b6b4` |  |
| 24 | `04_assets/Material/Mat.001.mat.meta` | `Assets/InFalsus/Recovered/Material/Mat.001.mat.meta` | 227 | `4a93865570a04461` |  |
| 25 | `04_assets/Material/cursor-flick-indicator-left.mat` | `Assets/InFalsus/Recovered/Material/cursor-flick-indicator-left.mat` | 2456 | `a84fbe02f5844e84` |  |
| 26 | `04_assets/Material/cursor-flick-indicator-left.mat.meta` | `Assets/InFalsus/Recovered/Material/cursor-flick-indicator-left.mat.meta` | 227 | `235cc910906b2f3d` |  |
| 27 | `04_assets/Material/cursor-flick-indicator-right.mat` | `Assets/InFalsus/Recovered/Material/cursor-flick-indicator-right.mat` | 2457 | `e7686179517afbdf` |  |
| 28 | `04_assets/Material/cursor-flick-indicator-right.mat.meta` | `Assets/InFalsus/Recovered/Material/cursor-flick-indicator-right.mat.meta` | 227 | `76b345e4982ace79` |  |
| 29 | `04_assets/Material/matid_flicknote.mat` | `Assets/InFalsus/Recovered/Material/matid_flicknote.mat` | 2654 | `cf820e55d6d0f875` |  |
| 30 | `04_assets/Material/matid_flicknote.mat.meta` | `Assets/InFalsus/Recovered/Material/matid_flicknote.mat.meta` | 227 | `c014b933581b6aa2` |  |
| 31 | `04_assets/Mesh/Flick.asset` | `Assets/InFalsus/Recovered/Mesh/Flick.asset` | 5832 | `d81fd351f18899a8` |  |
| 32 | `04_assets/Mesh/Flick.asset.meta` | `Assets/InFalsus/Recovered/Mesh/Flick.asset.meta` | 227 | `a49837c1b39ba51f` |  |
| 33 | `04_assets/Mesh/Plane.asset` | `Assets/InFalsus/Recovered/Mesh/Plane.asset` | 3834 | `c825417058c03886` |  |
| 34 | `04_assets/Mesh/Plane.asset.meta` | `Assets/InFalsus/Recovered/Mesh/Plane.asset.meta` | 227 | `252ade52d2efa1d5` |  |
| 35 | `04_assets/Mesh/flicknote01.asset` | `Assets/InFalsus/Recovered/Mesh/flicknote01.asset` | 4510 | `b6399dac3b137059` |  |
| 36 | `04_assets/Mesh/flicknote01.asset.meta` | `Assets/InFalsus/Recovered/Mesh/flicknote01.asset.meta` | 227 | `5692ece09e6882f4` |  |
| 37 | `04_assets/Mesh/flickunderscore01.asset` | `Assets/InFalsus/Recovered/Mesh/flickunderscore01.asset` | 5489 | `d081a2001fadd058` |  |
| 38 | `04_assets/Mesh/flickunderscore01.asset.meta` | `Assets/InFalsus/Recovered/Mesh/flickunderscore01.asset.meta` | 227 | `e2218172fb1c4b1e` |  |
| 39 | `04_assets/Shader/9a.shader` | `Assets/InFalsus/Recovered/9a.shader` | 3397 | `d520d2880815b706` | cursor-flick-indicator-left/right 的 m_Shader 依赖 |
| 40 | `04_assets/Shader/9a.shader.meta` | `Assets/InFalsus/Recovered/9a.shader.meta` | 276 | `ed6888ae2bd323cb` | cursor-flick-indicator-left/right 的 m_Shader 依赖 |
| 41 | `04_assets/Shader/Universal Render Pipeline_Unlit_1.shader` | `Assets/InFalsus/Recovered/Shader/Universal Render Pipeline_Unlit_1.shader` | 11416 | `37ef434d3bd626d5` | Mat.001/Mat.001 R/matid_flicknote 的 m_Shader 依赖 |
| 42 | `04_assets/Shader/Universal Render Pipeline_Unlit_1.shader.meta` | `Assets/InFalsus/Recovered/Shader/Universal Render Pipeline_Unlit_1.shader.meta` | 243 | `18ad7143ba30f94f` | Mat.001/Mat.001 R/matid_flicknote 的 m_Shader 依赖 |
| 43 | `04_assets/Sprite/FlickBurstR_0.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_0.asset` | 3804 | `86e5d9eca2c4e78e` |  |
| 44 | `04_assets/Sprite/FlickBurstR_0.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_0.asset.meta` | 228 | `833aad9d1a365fe0` |  |
| 45 | `04_assets/Sprite/FlickBurstR_1.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_1.asset` | 4314 | `fb8e5dd192c4289b` |  |
| 46 | `04_assets/Sprite/FlickBurstR_1.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_1.asset.meta` | 228 | `18f3fd1492864d35` |  |
| 47 | `04_assets/Sprite/FlickBurstR_10.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_10.asset` | 4622 | `ce21867727d98750` |  |
| 48 | `04_assets/Sprite/FlickBurstR_10.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_10.asset.meta` | 228 | `e8ff05a905074c10` |  |
| 49 | `04_assets/Sprite/FlickBurstR_11.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_11.asset` | 5068 | `94f067331b1458e4` |  |
| 50 | `04_assets/Sprite/FlickBurstR_11.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_11.asset.meta` | 228 | `3705ed014b397890` |  |
| 51 | `04_assets/Sprite/FlickBurstR_12.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_12.asset` | 5350 | `51a8843a87b6d2bd` |  |
| 52 | `04_assets/Sprite/FlickBurstR_12.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_12.asset.meta` | 228 | `b693f8104fd9b757` |  |
| 53 | `04_assets/Sprite/FlickBurstR_13.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_13.asset` | 4900 | `acc3414c9e6dbe31` |  |
| 54 | `04_assets/Sprite/FlickBurstR_13.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_13.asset.meta` | 228 | `eeb4b9c84c553de4` |  |
| 55 | `04_assets/Sprite/FlickBurstR_14.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_14.asset` | 5546 | `1dca03bcd484614c` |  |
| 56 | `04_assets/Sprite/FlickBurstR_14.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_14.asset.meta` | 228 | `26b7ea4ac1e31ea9` |  |
| 57 | `04_assets/Sprite/FlickBurstR_15.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_15.asset` | 3812 | `c6cff06b41069de0` |  |
| 58 | `04_assets/Sprite/FlickBurstR_15.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_15.asset.meta` | 228 | `08b0beba6ad15a8c` |  |
| 59 | `04_assets/Sprite/FlickBurstR_16.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_16.asset` | 6662 | `1ef4410621712204` |  |
| 60 | `04_assets/Sprite/FlickBurstR_16.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_16.asset.meta` | 228 | `e2f48c9abb54ef16` |  |
| 61 | `04_assets/Sprite/FlickBurstR_17.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_17.asset` | 5360 | `3dcad1c898016068` |  |
| 62 | `04_assets/Sprite/FlickBurstR_17.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_17.asset.meta` | 228 | `aa9b5f0f5c77c1f9` |  |
| 63 | `04_assets/Sprite/FlickBurstR_18.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_18.asset` | 5864 | `08525c8f7cd3269a` |  |
| 64 | `04_assets/Sprite/FlickBurstR_18.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_18.asset.meta` | 228 | `8a8eadbcfbfd1b68` |  |
| 65 | `04_assets/Sprite/FlickBurstR_19.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_19.asset` | 6131 | `4f1f31ca0873ab86` |  |
| 66 | `04_assets/Sprite/FlickBurstR_19.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_19.asset.meta` | 228 | `193ed31b70a5e4c6` |  |
| 67 | `04_assets/Sprite/FlickBurstR_2.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_2.asset` | 3993 | `f20a1159b55a9068` |  |
| 68 | `04_assets/Sprite/FlickBurstR_2.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_2.asset.meta` | 228 | `f1acc15bf80951e2` |  |
| 69 | `04_assets/Sprite/FlickBurstR_20.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_20.asset` | 6416 | `bb8c010c663fae11` |  |
| 70 | `04_assets/Sprite/FlickBurstR_20.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_20.asset.meta` | 228 | `e3247b2aad9743ed` |  |
| 71 | `04_assets/Sprite/FlickBurstR_21.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_21.asset` | 6570 | `95392a20141ba927` |  |
| 72 | `04_assets/Sprite/FlickBurstR_21.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_21.asset.meta` | 228 | `fb581d7b290e15b3` |  |
| 73 | `04_assets/Sprite/FlickBurstR_22.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_22.asset` | 5202 | `abefa48543ae970d` |  |
| 74 | `04_assets/Sprite/FlickBurstR_22.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_22.asset.meta` | 228 | `8bbeb3cb37d70339` |  |
| 75 | `04_assets/Sprite/FlickBurstR_23.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_23.asset` | 6441 | `6b2271708f5a676c` |  |
| 76 | `04_assets/Sprite/FlickBurstR_23.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_23.asset.meta` | 228 | `753f4725ffff4dd6` |  |
| 77 | `04_assets/Sprite/FlickBurstR_24.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_24.asset` | 5630 | `21075454402c0b8b` |  |
| 78 | `04_assets/Sprite/FlickBurstR_24.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_24.asset.meta` | 228 | `4ab37b23d89842f3` |  |
| 79 | `04_assets/Sprite/FlickBurstR_25.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_25.asset` | 6862 | `ed5fd72bb976f668` |  |
| 80 | `04_assets/Sprite/FlickBurstR_25.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_25.asset.meta` | 228 | `d75c48eba93fcce3` |  |
| 81 | `04_assets/Sprite/FlickBurstR_26.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_26.asset` | 4326 | `98c729fd3badfd8b` |  |
| 82 | `04_assets/Sprite/FlickBurstR_26.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_26.asset.meta` | 228 | `4aad9a6bb26bf401` |  |
| 83 | `04_assets/Sprite/FlickBurstR_27.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_27.asset` | 5438 | `4173fd1d55fcf30f` |  |
| 84 | `04_assets/Sprite/FlickBurstR_27.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_27.asset.meta` | 228 | `6288ddae5f3106ce` |  |
| 85 | `04_assets/Sprite/FlickBurstR_28.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_28.asset` | 5239 | `adb9909d2e2773a2` |  |
| 86 | `04_assets/Sprite/FlickBurstR_28.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_28.asset.meta` | 228 | `8905402c522f8221` |  |
| 87 | `04_assets/Sprite/FlickBurstR_29.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_29.asset` | 9302 | `1a86a3de11e6fe72` |  |
| 88 | `04_assets/Sprite/FlickBurstR_29.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_29.asset.meta` | 228 | `926b5f33b2429f31` |  |
| 89 | `04_assets/Sprite/FlickBurstR_3.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_3.asset` | 3933 | `57785343c9053be2` |  |
| 90 | `04_assets/Sprite/FlickBurstR_3.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_3.asset.meta` | 228 | `716af458508ac9cc` |  |
| 91 | `04_assets/Sprite/FlickBurstR_30.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_30.asset` | 7663 | `750a703db6301e19` |  |
| 92 | `04_assets/Sprite/FlickBurstR_30.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_30.asset.meta` | 228 | `8e12c68be428d87b` |  |
| 93 | `04_assets/Sprite/FlickBurstR_31.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_31.asset` | 4654 | `67d8b7d464deb73e` |  |
| 94 | `04_assets/Sprite/FlickBurstR_31.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_31.asset.meta` | 228 | `95a8852ffb7002ef` |  |
| 95 | `04_assets/Sprite/FlickBurstR_32.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_32.asset` | 3573 | `9e1cf67304c8e983` |  |
| 96 | `04_assets/Sprite/FlickBurstR_32.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_32.asset.meta` | 228 | `c654dbbde2584c5c` |  |
| 97 | `04_assets/Sprite/FlickBurstR_33.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_33.asset` | 4558 | `76cc6414efe6a989` |  |
| 98 | `04_assets/Sprite/FlickBurstR_33.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_33.asset.meta` | 228 | `6503c640c536f153` |  |
| 99 | `04_assets/Sprite/FlickBurstR_34.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_34.asset` | 3816 | `e9c39950cc290302` |  |
| 100 | `04_assets/Sprite/FlickBurstR_34.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_34.asset.meta` | 228 | `9ad70d519429c82b` |  |
| 101 | `04_assets/Sprite/FlickBurstR_35.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_35.asset` | 3602 | `c68356879369075f` |  |
| 102 | `04_assets/Sprite/FlickBurstR_35.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_35.asset.meta` | 228 | `5a26fb726ba73525` |  |
| 103 | `04_assets/Sprite/FlickBurstR_36.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_36.asset` | 3851 | `a75f1e3ef73178ce` |  |
| 104 | `04_assets/Sprite/FlickBurstR_36.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_36.asset.meta` | 228 | `7f8a68e8c9353f17` |  |
| 105 | `04_assets/Sprite/FlickBurstR_37.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_37.asset` | 3568 | `879b37e0179119d2` |  |
| 106 | `04_assets/Sprite/FlickBurstR_37.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_37.asset.meta` | 228 | `34666775410cc312` |  |
| 107 | `04_assets/Sprite/FlickBurstR_38.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_38.asset` | 3576 | `c59348cf3b53c96e` |  |
| 108 | `04_assets/Sprite/FlickBurstR_38.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_38.asset.meta` | 228 | `931c66b79f95ec08` |  |
| 109 | `04_assets/Sprite/FlickBurstR_39.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_39.asset` | 3605 | `1b9b500e71bd9e7a` |  |
| 110 | `04_assets/Sprite/FlickBurstR_39.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_39.asset.meta` | 228 | `18db4c263f8778bf` |  |
| 111 | `04_assets/Sprite/FlickBurstR_4.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_4.asset` | 3996 | `fd0fd3fe56fe4679` |  |
| 112 | `04_assets/Sprite/FlickBurstR_4.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_4.asset.meta` | 228 | `cc8d095bf5488635` |  |
| 113 | `04_assets/Sprite/FlickBurstR_40.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_40.asset` | 3660 | `ec2d67dc7d3545c1` |  |
| 114 | `04_assets/Sprite/FlickBurstR_40.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_40.asset.meta` | 228 | `789cd4a21301f758` |  |
| 115 | `04_assets/Sprite/FlickBurstR_41.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_41.asset` | 3575 | `ffaf1268aa5e2b40` |  |
| 116 | `04_assets/Sprite/FlickBurstR_41.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_41.asset.meta` | 228 | `2c7fce05017ddb12` |  |
| 117 | `04_assets/Sprite/FlickBurstR_42.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_42.asset` | 3993 | `ff7f3ed8e774a2a3` |  |
| 118 | `04_assets/Sprite/FlickBurstR_42.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_42.asset.meta` | 228 | `a52c42598817f4cf` |  |
| 119 | `04_assets/Sprite/FlickBurstR_43.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_43.asset` | 3936 | `e7d804645c678b14` |  |
| 120 | `04_assets/Sprite/FlickBurstR_43.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_43.asset.meta` | 228 | `da9929e66ffd73f5` |  |
| 121 | `04_assets/Sprite/FlickBurstR_44.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_44.asset` | 3579 | `9e17908021fa605b` |  |
| 122 | `04_assets/Sprite/FlickBurstR_44.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_44.asset.meta` | 228 | `7c41b2ab53a7c458` |  |
| 123 | `04_assets/Sprite/FlickBurstR_45.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_45.asset` | 3518 | `a35e674fb6eb0cc5` |  |
| 124 | `04_assets/Sprite/FlickBurstR_45.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_45.asset.meta` | 228 | `aab5c25de9e8cced` |  |
| 125 | `04_assets/Sprite/FlickBurstR_46.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_46.asset` | 3577 | `57466d9acfc124ba` |  |
| 126 | `04_assets/Sprite/FlickBurstR_46.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_46.asset.meta` | 228 | `638d97ad4c6335c9` |  |
| 127 | `04_assets/Sprite/FlickBurstR_47.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_47.asset` | 4321 | `b0cb1facaec369f0` |  |
| 128 | `04_assets/Sprite/FlickBurstR_47.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_47.asset.meta` | 228 | `0122673548559048` |  |
| 129 | `04_assets/Sprite/FlickBurstR_48.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_48.asset` | 3603 | `7e901c3a9c8ee1a2` |  |
| 130 | `04_assets/Sprite/FlickBurstR_48.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_48.asset.meta` | 228 | `2c1b766cb025c66a` |  |
| 131 | `04_assets/Sprite/FlickBurstR_49.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_49.asset` | 3600 | `33da175db8bfa5ee` |  |
| 132 | `04_assets/Sprite/FlickBurstR_49.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_49.asset.meta` | 228 | `74e5379b04150d1c` |  |
| 133 | `04_assets/Sprite/FlickBurstR_5.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_5.asset` | 3994 | `cede763eaba4a4ad` |  |
| 134 | `04_assets/Sprite/FlickBurstR_5.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_5.asset.meta` | 228 | `7b84fc4b9c59744a` |  |
| 135 | `04_assets/Sprite/FlickBurstR_50.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_50.asset` | 4508 | `8bbdef0367bc543b` |  |
| 136 | `04_assets/Sprite/FlickBurstR_50.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_50.asset.meta` | 228 | `ccfee0743a2f2f72` |  |
| 137 | `04_assets/Sprite/FlickBurstR_51.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_51.asset` | 4323 | `76165e7a6b20f2e1` |  |
| 138 | `04_assets/Sprite/FlickBurstR_51.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_51.asset.meta` | 228 | `2300b5678a95f41a` |  |
| 139 | `04_assets/Sprite/FlickBurstR_52.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_52.asset` | 4167 | `ce7c1b54dc2b9c89` |  |
| 140 | `04_assets/Sprite/FlickBurstR_52.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_52.asset.meta` | 228 | `97d3417772d1ff32` |  |
| 141 | `04_assets/Sprite/FlickBurstR_53.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_53.asset` | 4533 | `7bac47d93df91f6e` |  |
| 142 | `04_assets/Sprite/FlickBurstR_53.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_53.asset.meta` | 228 | `d2f41a3e677a2ed6` |  |
| 143 | `04_assets/Sprite/FlickBurstR_54.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_54.asset` | 4543 | `9eabfdfac495f611` |  |
| 144 | `04_assets/Sprite/FlickBurstR_54.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_54.asset.meta` | 228 | `01e4fa4d0228c107` |  |
| 145 | `04_assets/Sprite/FlickBurstR_55.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_55.asset` | 4589 | `b6becd5d667537bd` |  |
| 146 | `04_assets/Sprite/FlickBurstR_55.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_55.asset.meta` | 228 | `f184384dc0ff6e96` |  |
| 147 | `04_assets/Sprite/FlickBurstR_56.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_56.asset` | 4156 | `b22638cdd8c53f7d` |  |
| 148 | `04_assets/Sprite/FlickBurstR_56.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_56.asset.meta` | 228 | `bc50a6417f27b8eb` |  |
| 149 | `04_assets/Sprite/FlickBurstR_57.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_57.asset` | 3933 | `fe7ad9bb0f41ac19` |  |
| 150 | `04_assets/Sprite/FlickBurstR_57.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_57.asset.meta` | 228 | `741407d57c694288` |  |
| 151 | `04_assets/Sprite/FlickBurstR_58.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_58.asset` | 3936 | `da967abb4a6920f7` |  |
| 152 | `04_assets/Sprite/FlickBurstR_58.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_58.asset.meta` | 228 | `1a3d1f9379f9563e` |  |
| 153 | `04_assets/Sprite/FlickBurstR_59.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_59.asset` | 3665 | `46ee2e22ac79d4a9` |  |
| 154 | `04_assets/Sprite/FlickBurstR_59.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_59.asset.meta` | 228 | `3bf2785368cd5547` |  |
| 155 | `04_assets/Sprite/FlickBurstR_6.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_6.asset` | 4670 | `4762ce8251a47950` |  |
| 156 | `04_assets/Sprite/FlickBurstR_6.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_6.asset.meta` | 228 | `7025ce31e39e296d` |  |
| 157 | `04_assets/Sprite/FlickBurstR_60.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_60.asset` | 3318 | `ee19d8d32071ea4a` |  |
| 158 | `04_assets/Sprite/FlickBurstR_60.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_60.asset.meta` | 228 | `7820aabc6b7161ee` |  |
| 159 | `04_assets/Sprite/FlickBurstR_61.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_61.asset` | 3318 | `8fcf05b5e01ac436` |  |
| 160 | `04_assets/Sprite/FlickBurstR_61.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_61.asset.meta` | 228 | `5acc2fe243f32645` |  |
| 161 | `04_assets/Sprite/FlickBurstR_62.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_62.asset` | 3316 | `26c1135d9598ab55` |  |
| 162 | `04_assets/Sprite/FlickBurstR_62.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_62.asset.meta` | 228 | `8e9230229c367e6a` |  |
| 163 | `04_assets/Sprite/FlickBurstR_63.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_63.asset` | 3316 | `c5df66cf872ecb9e` |  |
| 164 | `04_assets/Sprite/FlickBurstR_63.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_63.asset.meta` | 228 | `f21e9072bef2cb3e` |  |
| 165 | `04_assets/Sprite/FlickBurstR_7.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_7.asset` | 3521 | `6adfcc3e3082d673` |  |
| 166 | `04_assets/Sprite/FlickBurstR_7.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_7.asset.meta` | 228 | `25a7edf8d647453c` |  |
| 167 | `04_assets/Sprite/FlickBurstR_8.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_8.asset` | 4676 | `c6c09c08a488ca17` |  |
| 168 | `04_assets/Sprite/FlickBurstR_8.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_8.asset.meta` | 228 | `eb1d96b1602942de` |  |
| 169 | `04_assets/Sprite/FlickBurstR_9.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_9.asset` | 3991 | `ecea595807e8e1aa` |  |
| 170 | `04_assets/Sprite/FlickBurstR_9.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurstR_9.asset.meta` | 228 | `86b7518ae65ed4b4` |  |
| 171 | `04_assets/Sprite/FlickBurst_0.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_0.asset` | 3994 | `4f8fa4eb217f8670` |  |
| 172 | `04_assets/Sprite/FlickBurst_0.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_0.asset.meta` | 228 | `8d7a8a2db0966670` |  |
| 173 | `04_assets/Sprite/FlickBurst_1.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_1.asset` | 3570 | `a8a20f4de3e4fec2` |  |
| 174 | `04_assets/Sprite/FlickBurst_1.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_1.asset.meta` | 228 | `c6b1424f490d46b8` |  |
| 175 | `04_assets/Sprite/FlickBurst_10.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_10.asset` | 3729 | `92347536afe04739` |  |
| 176 | `04_assets/Sprite/FlickBurst_10.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_10.asset.meta` | 228 | `2b17defc1d08ea45` |  |
| 177 | `04_assets/Sprite/FlickBurst_11.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_11.asset` | 4325 | `5adb3c162f60ec8f` |  |
| 178 | `04_assets/Sprite/FlickBurst_11.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_11.asset.meta` | 228 | `9568e8927b2dd3e4` |  |
| 179 | `04_assets/Sprite/FlickBurst_12.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_12.asset` | 4198 | `35b24595d30660db` |  |
| 180 | `04_assets/Sprite/FlickBurst_12.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_12.asset.meta` | 228 | `51e1c7c68b697dfe` |  |
| 181 | `04_assets/Sprite/FlickBurst_13.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_13.asset` | 4036 | `0c49d532cbfa017b` |  |
| 182 | `04_assets/Sprite/FlickBurst_13.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_13.asset.meta` | 228 | `c9fe171da72c4a58` |  |
| 183 | `04_assets/Sprite/FlickBurst_14.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_14.asset` | 6300 | `92ce8ab62ea9589a` |  |
| 184 | `04_assets/Sprite/FlickBurst_14.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_14.asset.meta` | 228 | `1302d202d9873dfe` |  |
| 185 | `04_assets/Sprite/FlickBurst_15.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_15.asset` | 5388 | `4064ea4f2098163d` |  |
| 186 | `04_assets/Sprite/FlickBurst_15.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_15.asset.meta` | 228 | `6e00aee866c0321e` |  |
| 187 | `04_assets/Sprite/FlickBurst_16.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_16.asset` | 4990 | `b658c46965800a92` |  |
| 188 | `04_assets/Sprite/FlickBurst_16.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_16.asset.meta` | 228 | `8e9a11eba9711540` |  |
| 189 | `04_assets/Sprite/FlickBurst_17.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_17.asset` | 4127 | `b263e9d7f6f6172b` |  |
| 190 | `04_assets/Sprite/FlickBurst_17.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_17.asset.meta` | 228 | `d477afcb434958cb` |  |
| 191 | `04_assets/Sprite/FlickBurst_18.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_18.asset` | 5012 | `d8096653a8cb9f11` |  |
| 192 | `04_assets/Sprite/FlickBurst_18.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_18.asset.meta` | 228 | `f61cb5835d005ba7` |  |
| 193 | `04_assets/Sprite/FlickBurst_19.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_19.asset` | 6218 | `9b8a0948b31360ab` |  |
| 194 | `04_assets/Sprite/FlickBurst_19.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_19.asset.meta` | 228 | `895b533161e4fd64` |  |
| 195 | `04_assets/Sprite/FlickBurst_2.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_2.asset` | 4262 | `6d16d6d5cdb55125` |  |
| 196 | `04_assets/Sprite/FlickBurst_2.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_2.asset.meta` | 228 | `fc285a6a6b94e3d8` |  |
| 197 | `04_assets/Sprite/FlickBurst_20.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_20.asset` | 8837 | `b84ecfb46406d89e` |  |
| 198 | `04_assets/Sprite/FlickBurst_20.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_20.asset.meta` | 228 | `5df79b7fc4f1b8de` |  |
| 199 | `04_assets/Sprite/FlickBurst_21.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_21.asset` | 8314 | `df6c2c7ad90dfc6d` |  |
| 200 | `04_assets/Sprite/FlickBurst_21.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_21.asset.meta` | 228 | `c323f849c7bfd767` |  |
| 201 | `04_assets/Sprite/FlickBurst_22.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_22.asset` | 6463 | `b4b9f02ad1ac3f63` |  |
| 202 | `04_assets/Sprite/FlickBurst_22.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_22.asset.meta` | 228 | `24fa89f246b5e500` |  |
| 203 | `04_assets/Sprite/FlickBurst_23.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_23.asset` | 5451 | `dae23772d0c5e2e7` |  |
| 204 | `04_assets/Sprite/FlickBurst_23.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_23.asset.meta` | 228 | `0fd7cb5ccf16d0e1` |  |
| 205 | `04_assets/Sprite/FlickBurst_24.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_24.asset` | 4815 | `26ea7106af2fe220` |  |
| 206 | `04_assets/Sprite/FlickBurst_24.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_24.asset.meta` | 228 | `fd765a0dad893846` |  |
| 207 | `04_assets/Sprite/FlickBurst_25.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_25.asset` | 7157 | `440704fd2ae1626e` |  |
| 208 | `04_assets/Sprite/FlickBurst_25.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_25.asset.meta` | 228 | `cedc35be620a3ed9` |  |
| 209 | `04_assets/Sprite/FlickBurst_26.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_26.asset` | 7156 | `79d287937336bdc1` |  |
| 210 | `04_assets/Sprite/FlickBurst_26.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_26.asset.meta` | 228 | `5011db4d778d8d79` |  |
| 211 | `04_assets/Sprite/FlickBurst_27.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_27.asset` | 6366 | `62904ecbaca20d07` |  |
| 212 | `04_assets/Sprite/FlickBurst_27.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_27.asset.meta` | 228 | `929ce7b8e0a25d1c` |  |
| 213 | `04_assets/Sprite/FlickBurst_28.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_28.asset` | 5658 | `1c0ceecb8571bcff` |  |
| 214 | `04_assets/Sprite/FlickBurst_28.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_28.asset.meta` | 228 | `c7c1042339cdabbe` |  |
| 215 | `04_assets/Sprite/FlickBurst_29.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_29.asset` | 7245 | `4714b4c27d92f7bb` |  |
| 216 | `04_assets/Sprite/FlickBurst_29.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_29.asset.meta` | 228 | `25a6ce6885957ce9` |  |
| 217 | `04_assets/Sprite/FlickBurst_3.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_3.asset` | 3782 | `1a251000c928c7fc` |  |
| 218 | `04_assets/Sprite/FlickBurst_3.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_3.asset.meta` | 228 | `afc40e7d8fe73a76` |  |
| 219 | `04_assets/Sprite/FlickBurst_30.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_30.asset` | 5739 | `5de6da64d29d8a64` |  |
| 220 | `04_assets/Sprite/FlickBurst_30.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_30.asset.meta` | 228 | `383d56c2b0884d08` |  |
| 221 | `04_assets/Sprite/FlickBurst_31.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_31.asset` | 6304 | `f6a67199d06ee275` |  |
| 222 | `04_assets/Sprite/FlickBurst_31.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_31.asset.meta` | 228 | `0d79f399c27845e4` |  |
| 223 | `04_assets/Sprite/FlickBurst_32.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_32.asset` | 4478 | `96f5c2d9ceec3711` |  |
| 224 | `04_assets/Sprite/FlickBurst_32.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_32.asset.meta` | 228 | `d5c2f352acea5e74` |  |
| 225 | `04_assets/Sprite/FlickBurst_33.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_33.asset` | 4434 | `36e965aaee62609a` |  |
| 226 | `04_assets/Sprite/FlickBurst_33.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_33.asset.meta` | 228 | `5b9ca96ecc7961ab` |  |
| 227 | `04_assets/Sprite/FlickBurst_34.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_34.asset` | 3727 | `fabe21370bc49882` |  |
| 228 | `04_assets/Sprite/FlickBurst_34.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_34.asset.meta` | 228 | `1185c18e59e1f767` |  |
| 229 | `04_assets/Sprite/FlickBurst_35.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_35.asset` | 3784 | `1c43c01627ca56d7` |  |
| 230 | `04_assets/Sprite/FlickBurst_35.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_35.asset.meta` | 228 | `ce1d424d3e0d9876` |  |
| 231 | `04_assets/Sprite/FlickBurst_36.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_36.asset` | 4398 | `193489c986158a22` |  |
| 232 | `04_assets/Sprite/FlickBurst_36.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_36.asset.meta` | 228 | `9382002912f76489` |  |
| 233 | `04_assets/Sprite/FlickBurst_37.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_37.asset` | 4080 | `b9298b5b57ac1eb4` |  |
| 234 | `04_assets/Sprite/FlickBurst_37.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_37.asset.meta` | 228 | `333bd21ec45e9b11` |  |
| 235 | `04_assets/Sprite/FlickBurst_38.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_38.asset` | 3564 | `00468c8f6b9120fe` |  |
| 236 | `04_assets/Sprite/FlickBurst_38.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_38.asset.meta` | 228 | `4356f2985fedca1d` |  |
| 237 | `04_assets/Sprite/FlickBurst_39.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_39.asset` | 3577 | `eb7984b333a31fa0` |  |
| 238 | `04_assets/Sprite/FlickBurst_39.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_39.asset.meta` | 228 | `451f87143d4356e4` |  |
| 239 | `04_assets/Sprite/FlickBurst_4.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_4.asset` | 3601 | `63ac08e27c2fcfca` |  |
| 240 | `04_assets/Sprite/FlickBurst_4.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_4.asset.meta` | 228 | `f096c66444db91fb` |  |
| 241 | `04_assets/Sprite/FlickBurst_40.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_40.asset` | 3660 | `726dcd5ab60fb1ca` |  |
| 242 | `04_assets/Sprite/FlickBurst_40.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_40.asset.meta` | 228 | `35e7854bec7901ea` |  |
| 243 | `04_assets/Sprite/FlickBurst_41.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_41.asset` | 4142 | `fd53cd36b5cee0fd` |  |
| 244 | `04_assets/Sprite/FlickBurst_41.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_41.asset.meta` | 228 | `aedb1f1c42f2573c` |  |
| 245 | `04_assets/Sprite/FlickBurst_42.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_42.asset` | 3729 | `4a1100a64baaaf8e` |  |
| 246 | `04_assets/Sprite/FlickBurst_42.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_42.asset.meta` | 228 | `4e6abaaac9e2d234` |  |
| 247 | `04_assets/Sprite/FlickBurst_43.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_43.asset` | 3578 | `35b86c846998080c` |  |
| 248 | `04_assets/Sprite/FlickBurst_43.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_43.asset.meta` | 228 | `9ce64a3df72f8780` |  |
| 249 | `04_assets/Sprite/FlickBurst_44.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_44.asset` | 3515 | `0b2ad24ea54917b8` |  |
| 250 | `04_assets/Sprite/FlickBurst_44.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_44.asset.meta` | 228 | `0b2546102657d1a9` |  |
| 251 | `04_assets/Sprite/FlickBurst_45.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_45.asset` | 3604 | `295cc77313abd540` |  |
| 252 | `04_assets/Sprite/FlickBurst_45.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_45.asset.meta` | 228 | `3beecd089d7e3fc6` |  |
| 253 | `04_assets/Sprite/FlickBurst_46.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_46.asset` | 4011 | `81efa37584ce282e` |  |
| 254 | `04_assets/Sprite/FlickBurst_46.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_46.asset.meta` | 228 | `eb7139b760ca9fe5` |  |
| 255 | `04_assets/Sprite/FlickBurst_47.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_47.asset` | 3817 | `0888af4450a9568a` |  |
| 256 | `04_assets/Sprite/FlickBurst_47.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_47.asset.meta` | 228 | `8587449923b992ab` |  |
| 257 | `04_assets/Sprite/FlickBurst_48.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_48.asset` | 3663 | `bf20e47227e8fc78` |  |
| 258 | `04_assets/Sprite/FlickBurst_48.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_48.asset.meta` | 228 | `3920f9bc1c90d217` |  |
| 259 | `04_assets/Sprite/FlickBurst_49.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_49.asset` | 4034 | `44fc42957f2463bd` |  |
| 260 | `04_assets/Sprite/FlickBurst_49.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_49.asset.meta` | 228 | `31571e53f4db5609` |  |
| 261 | `04_assets/Sprite/FlickBurst_5.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_5.asset` | 3665 | `c21dadfd550754e3` |  |
| 262 | `04_assets/Sprite/FlickBurst_5.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_5.asset.meta` | 228 | `9399ea30323a6c5e` |  |
| 263 | `04_assets/Sprite/FlickBurst_50.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_50.asset` | 4431 | `a8227b47c4ea44d5` |  |
| 264 | `04_assets/Sprite/FlickBurst_50.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_50.asset.meta` | 228 | `e9c515c5ec110789` |  |
| 265 | `04_assets/Sprite/FlickBurst_51.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_51.asset` | 3662 | `f0b28bf323d0b9f8` |  |
| 266 | `04_assets/Sprite/FlickBurst_51.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_51.asset.meta` | 228 | `eea64c04ed336a78` |  |
| 267 | `04_assets/Sprite/FlickBurst_52.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_52.asset` | 3876 | `c126113ced86e94f` |  |
| 268 | `04_assets/Sprite/FlickBurst_52.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_52.asset.meta` | 228 | `7de4e3b12fd63eb8` |  |
| 269 | `04_assets/Sprite/FlickBurst_53.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_53.asset` | 3573 | `91d2f98c50941cef` |  |
| 270 | `04_assets/Sprite/FlickBurst_53.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_53.asset.meta` | 228 | `9cf3660fcfad71c6` |  |
| 271 | `04_assets/Sprite/FlickBurst_54.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_54.asset` | 3844 | `1090a0db22a7bd5b` |  |
| 272 | `04_assets/Sprite/FlickBurst_54.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_54.asset.meta` | 228 | `55a9512b303d494d` |  |
| 273 | `04_assets/Sprite/FlickBurst_55.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_55.asset` | 3605 | `1b3378a4775ac7ee` |  |
| 274 | `04_assets/Sprite/FlickBurst_55.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_55.asset.meta` | 228 | `a1f74b4b5e53e88f` |  |
| 275 | `04_assets/Sprite/FlickBurst_56.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_56.asset` | 3500 | `9ae3cc602699a08c` |  |
| 276 | `04_assets/Sprite/FlickBurst_56.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_56.asset.meta` | 228 | `379f62abe41ea874` |  |
| 277 | `04_assets/Sprite/FlickBurst_57.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_57.asset` | 3849 | `357d855970546d81` |  |
| 278 | `04_assets/Sprite/FlickBurst_57.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_57.asset.meta` | 228 | `de9a5f508181de41` |  |
| 279 | `04_assets/Sprite/FlickBurst_58.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_58.asset` | 3925 | `ba63c407f5f0ffd1` |  |
| 280 | `04_assets/Sprite/FlickBurst_58.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_58.asset.meta` | 228 | `d28792dc84ff41a4` |  |
| 281 | `04_assets/Sprite/FlickBurst_59.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_59.asset` | 3578 | `512731f823a78287` |  |
| 282 | `04_assets/Sprite/FlickBurst_59.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_59.asset.meta` | 228 | `de77c6d0a67b04b0` |  |
| 283 | `04_assets/Sprite/FlickBurst_6.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_6.asset` | 4261 | `b1868d06c35c7446` |  |
| 284 | `04_assets/Sprite/FlickBurst_6.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_6.asset.meta` | 228 | `a27a61782d4bbee5` |  |
| 285 | `04_assets/Sprite/FlickBurst_60.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_60.asset` | 3317 | `bf3dfa021731d5ee` |  |
| 286 | `04_assets/Sprite/FlickBurst_60.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_60.asset.meta` | 228 | `d5e1960a21bc2fe3` |  |
| 287 | `04_assets/Sprite/FlickBurst_61.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_61.asset` | 3318 | `76e2e4b64cf4e919` |  |
| 288 | `04_assets/Sprite/FlickBurst_61.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_61.asset.meta` | 228 | `132e36bd86864c50` |  |
| 289 | `04_assets/Sprite/FlickBurst_62.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_62.asset` | 3317 | `6a32272a590a60c3` |  |
| 290 | `04_assets/Sprite/FlickBurst_62.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_62.asset.meta` | 228 | `e3ec5e13e5555a18` |  |
| 291 | `04_assets/Sprite/FlickBurst_63.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_63.asset` | 3317 | `19ea321d175b870a` |  |
| 292 | `04_assets/Sprite/FlickBurst_63.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_63.asset.meta` | 228 | `bff7cc870e534657` |  |
| 293 | `04_assets/Sprite/FlickBurst_7.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_7.asset` | 4322 | `313b6f1a9557ea4a` |  |
| 294 | `04_assets/Sprite/FlickBurst_7.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_7.asset.meta` | 228 | `c101eec0980ff774` |  |
| 295 | `04_assets/Sprite/FlickBurst_8.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_8.asset` | 4314 | `e7991c2b532b018f` |  |
| 296 | `04_assets/Sprite/FlickBurst_8.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_8.asset.meta` | 228 | `7932cb6cf77bcdf8` |  |
| 297 | `04_assets/Sprite/FlickBurst_9.asset` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_9.asset` | 4052 | `bf6b15f03d2137c2` |  |
| 298 | `04_assets/Sprite/FlickBurst_9.asset.meta` | `Assets/InFalsus/Recovered/Sprite/FlickBurst_9.asset.meta` | 228 | `1a3be032eff26191` |  |
| 299 | `04_assets/Texture2D/FlickBurst.png` | `Assets/InFalsus/Recovered/Texture2D/FlickBurst.png` | 4652960 | `d45835da14032698` |  |
| 300 | `04_assets/Texture2D/FlickBurst.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickBurst.png.meta` | 3780 | `2827c611bf73fca1` |  |
| 301 | `04_assets/Texture2D/FlickBurstR.png` | `Assets/InFalsus/Recovered/Texture2D/FlickBurstR.png` | 4874723 | `d3e2bdc940c3e704` |  |
| 302 | `04_assets/Texture2D/FlickBurstR.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickBurstR.png.meta` | 3780 | `dfea4235fcbc07f7` |  |
| 303 | `04_assets/Texture2D/FlickNote-Bar.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNote-Bar.png` | 3283 | `3d78e571c8a26cca` |  |
| 304 | `04_assets/Texture2D/FlickNote-Bar.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNote-Bar.png.meta` | 2918 | `0450c28f5cbf1307` |  |
| 305 | `04_assets/Texture2D/FlickNote-Base.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNote-Base.png` | 196940 | `dc39dcc2a7851539` |  |
| 306 | `04_assets/Texture2D/FlickNote-Base.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNote-Base.png.meta` | 2915 | `dd52d70ef35c715c` |  |
| 307 | `04_assets/Texture2D/FlickNote-Core.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNote-Core.png` | 85822 | `bf612a2a777a030a` |  |
| 308 | `04_assets/Texture2D/FlickNote-Core.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNote-Core.png.meta` | 2915 | `4af138793de2f024` |  |
| 309 | `04_assets/Texture2D/FlickNoteCapR.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteCapR.png` | 1429874 | `cecb0ff2f48c5b36` |  |
| 310 | `04_assets/Texture2D/FlickNoteCapR.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteCapR.png.meta` | 2788 | `1e2f0f5d3b9e47c9` |  |
| 311 | `04_assets/Texture2D/FlickNoteR-Bar.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Bar.png` | 4054 | `8e5787b6302f96d3` |  |
| 312 | `04_assets/Texture2D/FlickNoteR-Bar.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Bar.png.meta` | 2918 | `d8652e5b75c32288` |  |
| 313 | `04_assets/Texture2D/FlickNoteR-Base.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Base.png` | 211472 | `1ad7e6dbd8b8d3b2` |  |
| 314 | `04_assets/Texture2D/FlickNoteR-Base.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Base.png.meta` | 2915 | `681c0cfee17a5451` |  |
| 315 | `04_assets/Texture2D/FlickNoteR-Core.png` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Core.png` | 55570 | `034a46820edf471b` |  |
| 316 | `04_assets/Texture2D/FlickNoteR-Core.png.meta` | `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Core.png.meta` | 2915 | `f98832729fc1a67d` |  |
| 317 | `04_assets/Texture2D/cursor-flick-indicator-left.png` | `Assets/InFalsus/Recovered/Texture2D/cursor-flick-indicator-left.png` | 156191 | `7a560129c577744f` |  |
| 318 | `04_assets/Texture2D/cursor-flick-indicator-left.png.meta` | `Assets/InFalsus/Recovered/Texture2D/cursor-flick-indicator-left.png.meta` | 2788 | `4620f512b9e28007` |  |
| 319 | `04_assets/Texture2D/cursor-flick-indicator-right.png` | `Assets/InFalsus/Recovered/Texture2D/cursor-flick-indicator-right.png` | 163835 | `598f992200e118fd` |  |
| 320 | `04_assets/Texture2D/cursor-flick-indicator-right.png.meta` | `Assets/InFalsus/Recovered/Texture2D/cursor-flick-indicator-right.png.meta` | 2788 | `0026b6b7f1351f0b` |  |
| 321 | `04_assets/Texture2D/flickv5.png` | `Assets/InFalsus/Recovered/Texture2D/flickv5.png` | 1679345 | `ddc9b46f6bbf1f0e` |  |
| 322 | `04_assets/Texture2D/flickv5.png.meta` | `Assets/InFalsus/Recovered/Texture2D/flickv5.png.meta` | 2788 | `4169d3a004c37268` |  |
| 323 | `04_assets/Texture2D/notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png` | `Assets/InFalsus/Recovered/Texture2D/notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png` | 286788 | `44bfaf245e51db7f` |  |
| 324 | `04_assets/Texture2D/notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png.meta` | `Assets/InFalsus/Recovered/Texture2D/notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png.meta` | 2788 | `31e2a7e0a50af260` |  |

### 05_config — 工程配置（Unity/URP/Volume）

文件数 29，字节数 92205。

| # | 包内路径 | 原工程路径 | 字节 | sha256[:16] | 备注 |
|---:|---|---|---:|---|---|
| 1 | `05_config/Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader` | `Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader` | 1652 | `4597c71b40658935` | GraphicsSettings 预加载 shader |
| 2 | `05_config/Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader.meta` | `Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLine.shader.meta` | 204 | `830972f9293fa794` | GraphicsSettings 预加载 shader |
| 3 | `05_config/Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader` | `Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader` | 4129 | `c1df24c98c9f22c6` | GraphicsSettings 预加载 shader |
| 4 | `05_config/Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader.meta` | `Assets/InFalsus/Gameplay/Recovered/Shaders/SkyAreaPrompt.shader.meta` | 204 | `68ab42ef86060d0e` | GraphicsSettings 预加载 shader |
| 5 | `05_config/Assets/InFalsus/Recovered/a7.shader` | `Assets/InFalsus/Recovered/a7.shader` | 5594 | `745d1cd34e32cdd3` | GraphicsSettings 预加载 shader |
| 6 | `05_config/Assets/InFalsus/Recovered/a7.shader.meta` | `Assets/InFalsus/Recovered/a7.shader.meta` | 276 | `255b5cc908ee5dca` | GraphicsSettings 预加载 shader |
| 7 | `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader` | 2705 | `cebb4da562ace3d3` | GraphicsSettings 预加载 shader |
| 8 | `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader.meta` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondBlur.shader.meta` | 204 | `b485a7855ba03341` | GraphicsSettings 预加载 shader |
| 9 | `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader` | 4214 | `a249498ac4feb7b3` | GraphicsSettings 预加载 shader |
| 10 | `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader.meta` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondMask.shader.meta` | 204 | `3e20ba3af0f8a28f` | GraphicsSettings 预加载 shader |
| 11 | `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader` | 2763 | `d629d16b25b3239d` | GraphicsSettings 预加载 shader |
| 12 | `05_config/Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader.meta` | `Assets/InFalsus/Shaders/Compat/FalsumTrackEndDiamondOverlay.shader.meta` | 204 | `0be9fad01de4542a` | GraphicsSettings 预加载 shader |
| 13 | `05_config/Assets/Settings/DefaultVolumeProfile.asset` | `Assets/Settings/DefaultVolumeProfile.asset` | 25023 | `48b80bc4408246e2` | Bloom 全局后处理配置（Flick 发光依赖） |
| 14 | `05_config/Assets/Settings/DefaultVolumeProfile.asset.meta` | `Assets/Settings/DefaultVolumeProfile.asset.meta` | 189 | `efbcb026cee6b8e3` | Bloom 全局后处理配置（Flick 发光依赖） |
| 15 | `05_config/Assets/Settings/FalsumTrackEndComposerFeature.asset` | `Assets/Settings/FalsumTrackEndComposerFeature.asset` | 521 | `8f1441f52a84d8e1` | PC_Renderer 的 Renderer Feature |
| 16 | `05_config/Assets/Settings/FalsumTrackEndComposerFeature.asset.meta` | `Assets/Settings/FalsumTrackEndComposerFeature.asset.meta` | 189 | `d9cebcaafe322fba` | PC_Renderer 的 Renderer Feature |
| 17 | `05_config/Assets/Settings/PC_RPAsset.asset` | `Assets/Settings/PC_RPAsset.asset` | 4669 | `38b665df3d63dfe7` | URP 管线资产（GraphicsSettings 指向它） |
| 18 | `05_config/Assets/Settings/PC_RPAsset.asset.meta` | `Assets/Settings/PC_RPAsset.asset.meta` | 189 | `bb469b9b6f318e9e` | URP 管线资产（GraphicsSettings 指向它） |
| 19 | `05_config/Assets/Settings/PC_Renderer.asset` | `Assets/Settings/PC_Renderer.asset` | 3096 | `381da0104cad9cba` | URP Renderer 资产 |
| 20 | `05_config/Assets/Settings/PC_Renderer.asset.meta` | `Assets/Settings/PC_Renderer.asset.meta` | 189 | `b5df8ddb2708c6ed` | URP Renderer 资产 |
| 21 | `05_config/Assets/Settings/SampleSceneProfile.asset` | `Assets/Settings/SampleSceneProfile.asset` | 3703 | `a8677d4bac051d24` | PC_RPAsset 的默认 Volume Profile |
| 22 | `05_config/Assets/Settings/SampleSceneProfile.asset.meta` | `Assets/Settings/SampleSceneProfile.asset.meta` | 182 | `2e23abc214069f0a` | PC_RPAsset 的默认 Volume Profile |
| 23 | `05_config/Assets/Settings/SkyJudgementLineEffectFeature.asset` | `Assets/Settings/SkyJudgementLineEffectFeature.asset` | 515 | `c887d3ccfd510239` | PC_Renderer 的 Renderer Feature |
| 24 | `05_config/Assets/Settings/SkyJudgementLineEffectFeature.asset.meta` | `Assets/Settings/SkyJudgementLineEffectFeature.asset.meta` | 189 | `4a738f3a1f5c5cc0` | PC_Renderer 的 Renderer Feature |
| 25 | `05_config/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | 26104 | `5fdd833eec773dfa` | URP 全局设置 |
| 26 | `05_config/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset.meta` | `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset.meta` | 189 | `f61661d8bc8dbe53` | URP 全局设置 |
| 27 | `05_config/Packages/manifest.json` | `Packages/manifest.json` | 1708 | `55cbc4eadaeec2e8` | URP 17.3.0 / ShaderGraph 17.3.0 |
| 28 | `05_config/ProjectSettings/GraphicsSettings.asset` | `ProjectSettings/GraphicsSettings.asset` | 3112 | `7a9ee626b6fbc882` | 渲染管线/预加载 shader 绑定 |
| 29 | `05_config/ProjectSettings/ProjectVersion.txt` | `ProjectSettings/ProjectVersion.txt` | 85 | `1ba9ef2a17d110f7` | Unity 6000.3.26f1 |

### 07_full_dependents — 完整传递依赖（.cs 为主）

文件数 280，字节数 3814337。

| # | 包内路径 | 原工程路径 | 字节 | sha256[:16] | 备注 |
|---:|---|---|---:|---|---|
| 1 | `07_full_dependents/Assets/InFalsus/Editor/FalsumEditorScaleRepair.cs` | `Assets/InFalsus/Editor/FalsumEditorScaleRepair.cs` | 3107 | `1251efbf7c1b10d3` | 传递依赖 depth=4 |
| 2 | `07_full_dependents/Assets/InFalsus/Editor/FalsumEditorScaleRepair.cs.meta` | `Assets/InFalsus/Editor/FalsumEditorScaleRepair.cs.meta` | 59 | `3466a98dbc30d835` | 传递依赖 depth=4 |
| 3 | `07_full_dependents/Assets/InFalsus/Editor/FalsumMobileUiCapture.cs` | `Assets/InFalsus/Editor/FalsumMobileUiCapture.cs` | 25812 | `0dfb5064302877dc` | 传递依赖 depth=2 |
| 4 | `07_full_dependents/Assets/InFalsus/Editor/FalsumMobileUiCapture.cs.meta` | `Assets/InFalsus/Editor/FalsumMobileUiCapture.cs.meta` | 59 | `0dcd13e1286711ca` | 传递依赖 depth=2 |
| 5 | `07_full_dependents/Assets/InFalsus/Editor/FalsumMobileUiSelfCheck.cs` | `Assets/InFalsus/Editor/FalsumMobileUiSelfCheck.cs` | 10700 | `bec08c4ac601f734` | 传递依赖 depth=2 |
| 6 | `07_full_dependents/Assets/InFalsus/Editor/FalsumMobileUiSelfCheck.cs.meta` | `Assets/InFalsus/Editor/FalsumMobileUiSelfCheck.cs.meta` | 59 | `4898c64eccf7238d` | 传递依赖 depth=2 |
| 7 | `07_full_dependents/Assets/InFalsus/Editor/FalsumNoteRenderSelfCheck.cs` | `Assets/InFalsus/Editor/FalsumNoteRenderSelfCheck.cs` | 11269 | `696c39c33c46e26a` | 传递依赖 depth=4 |
| 8 | `07_full_dependents/Assets/InFalsus/Editor/FalsumNoteRenderSelfCheck.cs.meta` | `Assets/InFalsus/Editor/FalsumNoteRenderSelfCheck.cs.meta` | 60 | `b5908c3d43f32a1c` | 传递依赖 depth=4 |
| 9 | `07_full_dependents/Assets/InFalsus/Editor/FalsumProfessionalUiCapture.cs` | `Assets/InFalsus/Editor/FalsumProfessionalUiCapture.cs` | 15084 | `8a654a741b4c3168` | 传递依赖 depth=2 |
| 10 | `07_full_dependents/Assets/InFalsus/Editor/FalsumProfessionalUiCapture.cs.meta` | `Assets/InFalsus/Editor/FalsumProfessionalUiCapture.cs.meta` | 60 | `844461d68160d3c0` | 传递依赖 depth=2 |
| 11 | `07_full_dependents/Assets/InFalsus/Editor/FalsumProfessionalUiSelfCheck.cs` | `Assets/InFalsus/Editor/FalsumProfessionalUiSelfCheck.cs` | 7207 | `8ec0ed5179845168` | 传递依赖 depth=2 |
| 12 | `07_full_dependents/Assets/InFalsus/Editor/FalsumProfessionalUiSelfCheck.cs.meta` | `Assets/InFalsus/Editor/FalsumProfessionalUiSelfCheck.cs.meta` | 60 | `8c57da0c7c950bb5` | 传递依赖 depth=2 |
| 13 | `07_full_dependents/Assets/InFalsus/Editor/FalsumSelfCheck.cs` | `Assets/InFalsus/Editor/FalsumSelfCheck.cs` | 244546 | `8e99ed23fbf15a52` | 传递依赖 depth=3 |
| 14 | `07_full_dependents/Assets/InFalsus/Editor/FalsumSelfCheck.cs.meta` | `Assets/InFalsus/Editor/FalsumSelfCheck.cs.meta` | 59 | `4b93d684a7dff968` | 传递依赖 depth=3 |
| 15 | `07_full_dependents/Assets/InFalsus/Editor/FalsumSelfCheckAudioPreprocess.cs` | `Assets/InFalsus/Editor/FalsumSelfCheckAudioPreprocess.cs` | 41981 | `6d611ac0fb34c03d` | 传递依赖 depth=4 |
| 16 | `07_full_dependents/Assets/InFalsus/Editor/FalsumSelfCheckAudioPreprocess.cs.meta` | `Assets/InFalsus/Editor/FalsumSelfCheckAudioPreprocess.cs.meta` | 59 | `17afa5f5d97cf3a3` | 传递依赖 depth=4 |
| 17 | `07_full_dependents/Assets/InFalsus/Editor/FalsumTrackEndComposerInstaller.cs` | `Assets/InFalsus/Editor/FalsumTrackEndComposerInstaller.cs` | 4321 | `38d2c112b60c6165` | 传递依赖 depth=4 |
| 18 | `07_full_dependents/Assets/InFalsus/Editor/FalsumTrackEndComposerInstaller.cs.meta` | `Assets/InFalsus/Editor/FalsumTrackEndComposerInstaller.cs.meta` | 59 | `532117a422677e2e` | 传递依赖 depth=4 |
| 19 | `07_full_dependents/Assets/InFalsus/Editor/FlickEffectPreviewCapture.cs` | `Assets/InFalsus/Editor/FlickEffectPreviewCapture.cs` | 7289 | `c23055a586d8d873` | 任务点名的依赖方（depth=0） |
| 20 | `07_full_dependents/Assets/InFalsus/Editor/FlickEffectPreviewCapture.cs.meta` | `Assets/InFalsus/Editor/FlickEffectPreviewCapture.cs.meta` | 240 | `76afadd90d2cebde` | 任务点名的依赖方（depth=0） |
| 21 | `07_full_dependents/Assets/InFalsus/Editor/GameplayPlayCapture.cs` | `Assets/InFalsus/Editor/GameplayPlayCapture.cs` | 21542 | `67638037d2168cfe` | 任务点名的依赖方（depth=0） |
| 22 | `07_full_dependents/Assets/InFalsus/Editor/GameplayPlayCapture.cs.meta` | `Assets/InFalsus/Editor/GameplayPlayCapture.cs.meta` | 59 | `36697c12dccf57e1` | 任务点名的依赖方（depth=0） |
| 23 | `07_full_dependents/Assets/InFalsus/Editor/GameplaySceneBuilder.cs` | `Assets/InFalsus/Editor/GameplaySceneBuilder.cs` | 28401 | `b39f56facf7b7d7a` | 传递依赖 depth=1 |
| 24 | `07_full_dependents/Assets/InFalsus/Editor/GameplaySceneBuilder.cs.meta` | `Assets/InFalsus/Editor/GameplaySceneBuilder.cs.meta` | 59 | `ce3692188f71af26` | 传递依赖 depth=1 |
| 25 | `07_full_dependents/Assets/InFalsus/Editor/GameplaySelfCheck.cs` | `Assets/InFalsus/Editor/GameplaySelfCheck.cs` | 33441 | `a324d6bc233644bc` | 任务点名的依赖方（depth=0） |
| 26 | `07_full_dependents/Assets/InFalsus/Editor/GameplaySelfCheck.cs.meta` | `Assets/InFalsus/Editor/GameplaySelfCheck.cs.meta` | 59 | `c6ac0c929d51f543` | 任务点名的依赖方（depth=0） |
| 27 | `07_full_dependents/Assets/InFalsus/Gameplay/Core/Judgement/JudgementTypes.cs` | `Assets/InFalsus/Gameplay/Core/Judgement/JudgementTypes.cs` | 3702 | `587b98c8d5531e49` | 传递依赖 depth=1 |
| 28 | `07_full_dependents/Assets/InFalsus/Gameplay/Core/Judgement/JudgementTypes.cs.meta` | `Assets/InFalsus/Gameplay/Core/Judgement/JudgementTypes.cs.meta` | 59 | `8f58125ee78da41d` | 传递依赖 depth=1 |
| 29 | `07_full_dependents/Assets/InFalsus/Gameplay/Core/Timing/GameplayClock.cs` | `Assets/InFalsus/Gameplay/Core/Timing/GameplayClock.cs` | 9854 | `b8643ab0b80b182c` | 传递依赖 depth=1 |
| 30 | `07_full_dependents/Assets/InFalsus/Gameplay/Core/Timing/GameplayClock.cs.meta` | `Assets/InFalsus/Gameplay/Core/Timing/GameplayClock.cs.meta` | 59 | `df590f9583cd8b34` | 传递依赖 depth=1 |
| 31 | `07_full_dependents/Assets/InFalsus/Gameplay/Core/Timing/TrackMotion.cs` | `Assets/InFalsus/Gameplay/Core/Timing/TrackMotion.cs` | 3774 | `871b08fc8c36e5f8` | 传递依赖 depth=1 |
| 32 | `07_full_dependents/Assets/InFalsus/Gameplay/Core/Timing/TrackMotion.cs.meta` | `Assets/InFalsus/Gameplay/Core/Timing/TrackMotion.cs.meta` | 59 | `d4132a63814d7682` | 传递依赖 depth=1 |
| 33 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumAudioPreprocessMath.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumAudioPreprocessMath.cs` | 27004 | `a25fdfb646101cb6` | 传递依赖 depth=2 |
| 34 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumAudioPreprocessMath.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumAudioPreprocessMath.cs.meta` | 59 | `614ec69c4320b5db` | 传递依赖 depth=2 |
| 35 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumAutoCombo.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumAutoCombo.cs` | 5725 | `6992f3538833a7b0` | 传递依赖 depth=1 |
| 36 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumAutoCombo.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumAutoCombo.cs.meta` | 60 | `b29c3627d8e2fbc3` | 传递依赖 depth=1 |
| 37 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumBeatGrid.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumBeatGrid.cs` | 19869 | `884f3ec9e3d98b09` | 传递依赖 depth=2 |
| 38 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumBeatGrid.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumBeatGrid.cs.meta` | 59 | `932ab00f718c177d` | 传递依赖 depth=2 |
| 39 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumChartSession.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumChartSession.cs` | 19952 | `ef716e48fbe187b8` | 传递依赖 depth=1 |
| 40 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumChartSession.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumChartSession.cs.meta` | 59 | `3aca88274037f10e` | 传递依赖 depth=1 |
| 41 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumFraction.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumFraction.cs` | 2156 | `9aee37da7a62dfb6` | 传递依赖 depth=1 |
| 42 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumFraction.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumFraction.cs.meta` | 59 | `5226cfa404861262` | 传递依赖 depth=1 |
| 43 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumJson.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumJson.cs` | 22372 | `994f54ed0b4f8af1` | 传递依赖 depth=2 |
| 44 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumJson.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumJson.cs.meta` | 59 | `119b5ad0d06cfad4` | 传递依赖 depth=2 |
| 45 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumManifest.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumManifest.cs` | 25586 | `8a4d12bc22a79b99` | 传递依赖 depth=2 |
| 46 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumManifest.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumManifest.cs.meta` | 59 | `c3d968c0325cc204` | 传递依赖 depth=2 |
| 47 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumNewChartRequest.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumNewChartRequest.cs` | 1041 | `c8deca3ca04667a6` | 传递依赖 depth=2 |
| 48 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumNewChartRequest.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumNewChartRequest.cs.meta` | 59 | `fa09c81da54e93e6` | 传递依赖 depth=2 |
| 49 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumPackage.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumPackage.cs` | 32642 | `63841e2451864df4` | 传递依赖 depth=2 |
| 50 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumPackage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumPackage.cs.meta` | 59 | `f86ce0563405edc9` | 传递依赖 depth=2 |
| 51 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSelectionDragMath.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSelectionDragMath.cs` | 3404 | `29b5cc1e9769cf8e` | 传递依赖 depth=2 |
| 52 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSelectionDragMath.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSelectionDragMath.cs.meta` | 60 | `23480431b27ec741` | 传递依赖 depth=2 |
| 53 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankModel.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankModel.cs` | 26994 | `62cf390d8c63147c` | 传递依赖 depth=2 |
| 54 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankModel.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankModel.cs.meta` | 59 | `b955b293ec27f335` | 传递依赖 depth=2 |
| 55 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankStore.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankStore.cs` | 35780 | `52dc91ebddce1c94` | 传递依赖 depth=2 |
| 56 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankStore.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankStore.cs.meta` | 59 | `728a623e40462543` | 传递依赖 depth=2 |
| 57 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGroupScale.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGroupScale.cs` | 27056 | `40abfae77b996b9b` | 传递依赖 depth=2 |
| 58 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGroupScale.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGroupScale.cs.meta` | 59 | `8fe2c2b8c0447a63` | 传递依赖 depth=2 |
| 59 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGrouping.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGrouping.cs` | 4203 | `84f73f8ca53fc97d` | 传递依赖 depth=1 |
| 60 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGrouping.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGrouping.cs.meta` | 60 | `71d5b53f910cc486` | 传递依赖 depth=1 |
| 61 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaShareCode.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaShareCode.cs` | 10382 | `583307eeceb2ac0e` | 传递依赖 depth=2 |
| 62 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaShareCode.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaShareCode.cs.meta` | 59 | `0f413e53e2c3027d` | 传递依赖 depth=2 |
| 63 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyBounds.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyBounds.cs` | 4399 | `b93d98ff54b7e140` | 传递依赖 depth=2 |
| 64 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyBounds.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyBounds.cs.meta` | 60 | `e0dbc5e08750b746` | 传递依赖 depth=2 |
| 65 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumTapPlacementRule.cs` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumTapPlacementRule.cs` | 4064 | `a6accb59e5551b1a` | 传递依赖 depth=2 |
| 66 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core/FalsumTapPlacementRule.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Core/FalsumTapPlacementRule.cs.meta` | 60 | `c37d190e5623ab20` | 传递依赖 depth=2 |
| 67 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Localization/FalsumLocalization.cs` | `Assets/InFalsus/Gameplay/Falsum/Localization/FalsumLocalization.cs` | 9232 | `a4824c04743bd724` | 传递依赖 depth=1 |
| 68 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Localization/FalsumLocalization.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Localization/FalsumLocalization.cs.meta` | 59 | `b43f5d2884021aa9` | 传递依赖 depth=1 |
| 69 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEncoder.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEncoder.cs` | 14032 | `8638a747a23daa1d` | 传递依赖 depth=2 |
| 70 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEncoder.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEncoder.cs.meta` | 59 | `7e7230c3304d8312` | 传递依赖 depth=2 |
| 71 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEnvelope.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEnvelope.cs` | 6431 | `906ef1a14828f3f1` | 传递依赖 depth=2 |
| 72 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEnvelope.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumAudioPreprocessEnvelope.cs.meta` | 59 | `8d2c94f8d6d94638` | 传递依赖 depth=2 |
| 73 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackgroundCatalog.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackgroundCatalog.cs` | 8400 | `775b94e386a5549e` | 传递依赖 depth=4 |
| 74 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackgroundCatalog.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackgroundCatalog.cs.meta` | 59 | `dc0af2e5c95a3493` | 传递依赖 depth=4 |
| 75 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackupService.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackupService.cs` | 8310 | `aa367ce27d2f9a50` | 传递依赖 depth=2 |
| 76 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackupService.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBackupService.cs.meta` | 59 | `69d3392ce700c6a0` | 传递依赖 depth=2 |
| 77 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassEncoder.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassEncoder.cs` | 10218 | `357c68e167e8eced` | 传递依赖 depth=3 |
| 78 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassEncoder.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassEncoder.cs.meta` | 59 | `7bc966303dcf0238` | 传递依赖 depth=3 |
| 79 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassMediaImporter.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassMediaImporter.cs` | 12893 | `62909ee7bce46e9c` | 传递依赖 depth=3 |
| 80 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassMediaImporter.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassMediaImporter.cs.meta` | 60 | `2d1179f8e8748358` | 传递依赖 depth=3 |
| 81 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassSession.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassSession.cs` | 3731 | `e289270417a896e7` | 传递依赖 depth=3 |
| 82 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassSession.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumBassSession.cs.meta` | 59 | `876499eae1e12b4a` | 传递依赖 depth=3 |
| 83 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumCacheStore.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumCacheStore.cs` | 4040 | `437176bac2a32f70` | 传递依赖 depth=2 |
| 84 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumCacheStore.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumCacheStore.cs.meta` | 60 | `356871febc1e2b79` | 传递依赖 depth=2 |
| 85 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AnchorEdit.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AnchorEdit.cs` | 58170 | `7c14737dd04cd26d` | 传递依赖 depth=1 |
| 86 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AnchorEdit.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AnchorEdit.cs.meta` | 243 | `90de98f661fbb09d` | 传递依赖 depth=1 |
| 87 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AudioPreprocess.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AudioPreprocess.cs` | 56748 | `692c7b26da0932e7` | 传递依赖 depth=1 |
| 88 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AudioPreprocess.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AudioPreprocess.cs.meta` | 59 | `6a08fc8bf8501ec6` | 传递依赖 depth=1 |
| 89 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.Bank.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.Bank.cs` | 19813 | `95829185b7a25689` | 传递依赖 depth=1 |
| 90 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.Bank.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.Bank.cs.meta` | 59 | `0045b5d44c31219e` | 传递依赖 depth=1 |
| 91 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.MiddleEdit.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.MiddleEdit.cs` | 116082 | `f4bbcc4aa2443fed` | 传递依赖 depth=1 |
| 92 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.MiddleEdit.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.MiddleEdit.cs.meta` | 60 | `3cf86355bf248171` | 传递依赖 depth=1 |
| 93 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.SkyAreaSnap.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.SkyAreaSnap.cs` | 11819 | `f1d86fc593aeb90a` | 传递依赖 depth=1 |
| 94 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.SkyAreaSnap.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.SkyAreaSnap.cs.meta` | 240 | `c6b9da46e29b4c40` | 传递依赖 depth=1 |
| 95 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.TouchGestures.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.TouchGestures.cs` | 11054 | `4ec3afa11a256cc4` | 传递依赖 depth=1 |
| 96 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.TouchGestures.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.TouchGestures.cs.meta` | 59 | `ffb270053197976f` | 传递依赖 depth=1 |
| 97 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs` | 488547 | `9fa58efd20e0277c` | 传递依赖 depth=1 |
| 98 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs.meta` | 59 | `e82ff9985fc9cc9a` | 传递依赖 depth=1 |
| 99 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartMetadataDraft.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartMetadataDraft.cs` | 8164 | `3ac135bad8b2e2ae` | 传递依赖 depth=2 |
| 100 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartMetadataDraft.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartMetadataDraft.cs.meta` | 59 | `19d2cf43549644a2` | 传递依赖 depth=2 |
| 101 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumComboDisplay.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumComboDisplay.cs` | 9544 | `ee5a787c37e0368d` | 传递依赖 depth=1 |
| 102 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumComboDisplay.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumComboDisplay.cs.meta` | 60 | `cc3f7956865feba1` | 传递依赖 depth=1 |
| 103 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisclaimerConsent.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisclaimerConsent.cs` | 4907 | `b6f8b5dfe56fc125` | 传递依赖 depth=2 |
| 104 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisclaimerConsent.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisclaimerConsent.cs.meta` | 243 | `49ca159d1cbcfc7b` | 传递依赖 depth=2 |
| 105 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisplaySettingsController.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisplaySettingsController.cs` | 10033 | `9b1912dc2336c4b4` | 传递依赖 depth=2 |
| 106 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisplaySettingsController.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumDisplaySettingsController.cs.meta` | 59 | `b78822c52b7ca7a0` | 传递依赖 depth=2 |
| 107 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorInfo.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorInfo.cs` | 1364 | `6ea41938f4b80c2f` | 传递依赖 depth=3 |
| 108 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorInfo.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorInfo.cs.meta` | 243 | `b594ec2a97a696f4` | 传递依赖 depth=3 |
| 109 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorWorld.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorWorld.cs` | 208003 | `ff12f19d841e3581` | 传递依赖 depth=2 |
| 110 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorWorld.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorWorld.cs.meta` | 59 | `54f95b1dbfa0e36f` | 传递依赖 depth=2 |
| 111 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumFileDialog.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumFileDialog.cs` | 14393 | `25dd390ab7917070` | 传递依赖 depth=2 |
| 112 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumFileDialog.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumFileDialog.cs.meta` | 59 | `b8db45f05ee93f2e` | 传递依赖 depth=2 |
| 113 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumImePosition.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumImePosition.cs` | 5368 | `e26441dc82f48c14` | 传递依赖 depth=3 |
| 114 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumImePosition.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumImePosition.cs.meta` | 60 | `d062aaa17697e88e` | 传递依赖 depth=3 |
| 115 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumInputRouter.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumInputRouter.cs` | 9856 | `bc6a90ebb052a9db` | 传递依赖 depth=2 |
| 116 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumInputRouter.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumInputRouter.cs.meta` | 59 | `45f815afb95d7a62` | 传递依赖 depth=2 |
| 117 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMeasureNumberRenderer.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMeasureNumberRenderer.cs` | 11220 | `25bd97147f1f26a0` | 传递依赖 depth=3 |
| 118 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMeasureNumberRenderer.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMeasureNumberRenderer.cs.meta` | 60 | `5b7ec4ebafee5425` | 传递依赖 depth=3 |
| 119 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMediaConverter.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMediaConverter.cs` | 17321 | `a42d7a63ea821020` | 传递依赖 depth=2 |
| 120 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMediaConverter.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMediaConverter.cs.meta` | 59 | `82a7ea452c145142` | 传递依赖 depth=2 |
| 121 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMetadataApplyService.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMetadataApplyService.cs` | 9278 | `1c8c7d0e0174e93a` | 传递依赖 depth=2 |
| 122 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMetadataApplyService.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumMetadataApplyService.cs.meta` | 59 | `e2e47cf5ab09d634` | 传递依赖 depth=2 |
| 123 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumModifierState.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumModifierState.cs` | 2106 | `c2c77d26d11b54b4` | 传递依赖 depth=2 |
| 124 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumModifierState.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumModifierState.cs.meta` | 59 | `1b326250b2d3f969` | 传递依赖 depth=2 |
| 125 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPerformanceMonitor.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPerformanceMonitor.cs` | 23905 | `54cb715f5901bc9a` | 传递依赖 depth=2 |
| 126 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPerformanceMonitor.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPerformanceMonitor.cs.meta` | 240 | `4d54334f90ef8923` | 传递依赖 depth=2 |
| 127 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPitchPreservingPlayback.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPitchPreservingPlayback.cs` | 10183 | `29c2ce5c8de62e98` | 传递依赖 depth=1 |
| 128 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPitchPreservingPlayback.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPitchPreservingPlayback.cs.meta` | 59 | `d500bb78cd6a66d5` | 传递依赖 depth=1 |
| 129 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPlatform.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPlatform.cs` | 1417 | `b4ea450f5c1590a0` | 传递依赖 depth=2 |
| 130 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPlatform.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPlatform.cs.meta` | 59 | `8388b8df66242c4a` | 传递依赖 depth=2 |
| 131 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs` | 33176 | `7ca068a75cf8dbc6` | 传递依赖 depth=1 |
| 132 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs.meta` | 59 | `c9dba2a5f9997c7f` | 传递依赖 depth=1 |
| 133 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesMigration.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesMigration.cs` | 19381 | `2fdfcd4867fa5195` | 传递依赖 depth=3 |
| 134 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesMigration.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesMigration.cs.meta` | 59 | `a295b520a9104865` | 传递依赖 depth=3 |
| 135 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesStore.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesStore.cs` | 5296 | `29c3b064d75d897b` | 传递依赖 depth=2 |
| 136 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesStore.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesStore.cs.meta` | 59 | `1a3f852f0a702ef5` | 传递依赖 depth=2 |
| 137 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumRuntimePaths.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumRuntimePaths.cs` | 12496 | `9aa23ee88c034501` | 传递依赖 depth=2 |
| 138 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumRuntimePaths.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumRuntimePaths.cs.meta` | 60 | `20a0ab128a46e0d0` | 传递依赖 depth=2 |
| 139 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSelectionGeometry.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSelectionGeometry.cs` | 10901 | `11914dba945f102d` | 传递依赖 depth=3 |
| 140 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSelectionGeometry.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSelectionGeometry.cs.meta` | 59 | `7a8240da60cf7746` | 传递依赖 depth=3 |
| 141 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSmokeTest.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSmokeTest.cs` | 5996 | `24b54e882c6262ea` | 传递依赖 depth=1 |
| 142 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSmokeTest.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumSmokeTest.cs.meta` | 59 | `d0fa8edc15596f2f` | 传递依赖 depth=1 |
| 143 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTextClipboard.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTextClipboard.cs` | 1892 | `dd2dfc63faa9280e` | 传递依赖 depth=2 |
| 144 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTextClipboard.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTextClipboard.cs.meta` | 59 | `01a41bec1de58795` | 传递依赖 depth=2 |
| 145 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumCache.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumCache.cs` | 25160 | `18611796aee869c5` | 传递依赖 depth=2 |
| 146 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumCache.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumCache.cs.meta` | 240 | `523059e42f3f9d9b` | 传递依赖 depth=2 |
| 147 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs` | 15238 | `a75db7f235ba2dcf` | 传递依赖 depth=3 |
| 148 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackSpectrumRenderer.cs.meta` | 240 | `ffdf9697c892398b` | 传递依赖 depth=3 |
| 149 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackWaveformCache.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackWaveformCache.cs` | 4890 | `e2ab09c61ce7316d` | 传递依赖 depth=2 |
| 150 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackWaveformCache.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTrackWaveformCache.cs.meta` | 61 | `69e301ec00e8a203` | 传递依赖 depth=2 |
| 151 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumViewMode.cs` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumViewMode.cs` | 280 | `fe733819992cf03d` | 传递依赖 depth=2 |
| 152 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumViewMode.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumViewMode.cs.meta` | 59 | `35c127a470ca2164` | 传递依赖 depth=2 |
| 153 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumSkyAreaPlayability.cs` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumSkyAreaPlayability.cs` | 6971 | `1ae847ec62e64977` | 传递依赖 depth=3 |
| 154 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumSkyAreaPlayability.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumSkyAreaPlayability.cs.meta` | 243 | `c2812478ede7b89a` | 传递依赖 depth=3 |
| 155 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolExecution.cs` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolExecution.cs` | 11253 | `41aa857333681784` | 传递依赖 depth=2 |
| 156 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolExecution.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolExecution.cs.meta` | 61 | `69a8f302b55848b7` | 传递依赖 depth=2 |
| 157 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolManager.cs` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolManager.cs` | 6097 | `ba421631b74e0692` | 传递依赖 depth=2 |
| 158 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolManager.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolManager.cs.meta` | 61 | `a7b4e6c9a099ac6a` | 传递依赖 depth=2 |
| 159 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolModels.cs` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolModels.cs` | 25809 | `2a38ce1f2ba69895` | 传递依赖 depth=2 |
| 160 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolModels.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolModels.cs.meta` | 61 | `440299fc53b47127` | 传递依赖 depth=2 |
| 161 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolRuntime.cs` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolRuntime.cs` | 60980 | `0ff78b80bce3a677` | 传递依赖 depth=2 |
| 162 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolRuntime.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolRuntime.cs.meta` | 61 | `8e6b72ec84b46286` | 传递依赖 depth=2 |
| 163 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolScripts.cs` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolScripts.cs` | 10454 | `331a6b4bad9a8ed3` | 传递依赖 depth=3 |
| 164 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolScripts.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/Tools/FalsumToolScripts.cs.meta` | 61 | `47fbd9d98d5f359e` | 传递依赖 depth=3 |
| 165 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAboutPreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAboutPreferencesPage.cs` | 5293 | `862729dac79d548a` | 传递依赖 depth=4 |
| 166 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAboutPreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAboutPreferencesPage.cs.meta` | 59 | `6ce7cd56dbccc742` | 传递依赖 depth=4 |
| 167 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAdvancedPreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAdvancedPreferencesPage.cs` | 8710 | `4ddcbfb962393a60` | 传递依赖 depth=4 |
| 168 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAdvancedPreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAdvancedPreferencesPage.cs.meta` | 59 | `1258dce1394259c2` | 传递依赖 depth=4 |
| 169 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessView.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessView.cs` | 25942 | `2ff96642b92046e0` | 传递依赖 depth=2 |
| 170 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessView.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessView.cs.meta` | 59 | `c742bf67e025210f` | 传递依赖 depth=2 |
| 171 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessWaveform.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessWaveform.cs` | 17863 | `72f24872d4a97e69` | 传递依赖 depth=3 |
| 172 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessWaveform.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumAudioPreprocessWaveform.cs.meta` | 59 | `df9896995bc204ac` | 传递依赖 depth=3 |
| 173 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackgroundPreviewPanel.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackgroundPreviewPanel.cs` | 13655 | `8d9a4f4e20f98e54` | 传递依赖 depth=4 |
| 174 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackgroundPreviewPanel.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackgroundPreviewPanel.cs.meta` | 59 | `86b3657b348d02aa` | 传递依赖 depth=4 |
| 175 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackupPreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackupPreferencesPage.cs` | 12391 | `3674af9db1d83d98` | 传递依赖 depth=4 |
| 176 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackupPreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumBackupPreferencesPage.cs.meta` | 59 | `e27809fb5f86041e` | 传递依赖 depth=4 |
| 177 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPage.cs` | 18192 | `b69c1e5a3a444e66` | 传递依赖 depth=5 |
| 178 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPage.cs.meta` | 59 | `b9e502c47ccdaa4b` | 传递依赖 depth=5 |
| 179 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPanel.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPanel.cs` | 13965 | `d7ee8981366b227a` | 传递依赖 depth=4 |
| 180 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPanel.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumChartInfoPanel.cs.meta` | 59 | `a3f1cef9e2867bbc` | 传递依赖 depth=4 |
| 181 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayConfirmDialog.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayConfirmDialog.cs` | 5450 | `fa0d4dc332b0dfd0` | 传递依赖 depth=4 |
| 182 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayConfirmDialog.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayConfirmDialog.cs.meta` | 59 | `809bd009f40aa1de` | 传递依赖 depth=4 |
| 183 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayPreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayPreferencesPage.cs` | 13436 | `a86c57f9c714f860` | 传递依赖 depth=4 |
| 184 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayPreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumDisplayPreferencesPage.cs.meta` | 59 | `cca6ccf8948ceac5` | 传递依赖 depth=4 |
| 185 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditingPreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditingPreferencesPage.cs` | 13162 | `d00fad44f22d2852` | 传递依赖 depth=4 |
| 186 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditingPreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditingPreferencesPage.cs.meta` | 59 | `d550b8307486b718` | 传递依赖 depth=4 |
| 187 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs` | 327280 | `626cdfd0947aa4b7` | 传递依赖 depth=2 |
| 188 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs.meta` | 59 | `74a15bebbf58dc1a` | 传递依赖 depth=2 |
| 189 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumFonts.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumFonts.cs` | 1803 | `429eaee0ad8b77de` | 传递依赖 depth=3 |
| 190 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumFonts.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumFonts.cs.meta` | 59 | `f71b589404091e85` | 传递依赖 depth=3 |
| 191 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumInputPreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumInputPreferencesPage.cs` | 7538 | `c61488ab08790ffe` | 传递依赖 depth=4 |
| 192 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumInputPreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumInputPreferencesPage.cs.meta` | 59 | `262fe9adb0bb49c0` | 传递依赖 depth=4 |
| 193 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumKeyCaptureController.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumKeyCaptureController.cs` | 6455 | `c080db1e18ea03ab` | 传递依赖 depth=4 |
| 194 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumKeyCaptureController.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumKeyCaptureController.cs.meta` | 59 | `48d14a4bd7374f7d` | 传递依赖 depth=4 |
| 195 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumMeasureNumberLayout.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumMeasureNumberLayout.cs` | 1943 | `8b8f22a88131c3e2` | 传递依赖 depth=4 |
| 196 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumMeasureNumberLayout.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumMeasureNumberLayout.cs.meta` | 60 | `29fc9227026c84c2` | 传递依赖 depth=4 |
| 197 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileGridPicker.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileGridPicker.cs` | 7598 | `5caba5f4419a22ab` | 传递依赖 depth=3 |
| 198 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileGridPicker.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileGridPicker.cs.meta` | 60 | `cc2b34bbdffad879` | 传递依赖 depth=3 |
| 199 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileShell.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileShell.cs` | 51570 | `ce3cee209371e521` | 传递依赖 depth=2 |
| 200 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileShell.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumMobileShell.cs.meta` | 243 | `2b110a9063bbd5cf` | 传递依赖 depth=2 |
| 201 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumModePlane.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumModePlane.cs` | 20874 | `8e0bec59436074ce` | 传递依赖 depth=2 |
| 202 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumModePlane.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumModePlane.cs.meta` | 59 | `206cf470e7a07b7a` | 传递依赖 depth=2 |
| 203 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPerformanceWindow.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPerformanceWindow.cs` | 32580 | `3f5cd10eb83fb81d` | 传递依赖 depth=3 |
| 204 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPerformanceWindow.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPerformanceWindow.cs.meta` | 240 | `a00f2816f4c6cbcc` | 传递依赖 depth=3 |
| 205 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesPageHost.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesPageHost.cs` | 3204 | `68fb7c3ccfc126e3` | 传递依赖 depth=4 |
| 206 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesPageHost.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesPageHost.cs.meta` | 59 | `b00f76e909f30ac7` | 传递依赖 depth=4 |
| 207 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesTab.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesTab.cs` | 1279 | `ea809ea7aaa8c733` | 传递依赖 depth=4 |
| 208 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesTab.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesTab.cs.meta` | 59 | `8eccfc1f5ff85701` | 传递依赖 depth=4 |
| 209 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesUiHelpers.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesUiHelpers.cs` | 2169 | `85a3ea4a9f18f4ee` | 传递依赖 depth=4 |
| 210 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesUiHelpers.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesUiHelpers.cs.meta` | 59 | `22398b7ef4a0c7b0` | 传递依赖 depth=4 |
| 211 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs` | 59206 | `8d6dd03e947d8ac4` | 传递依赖 depth=3 |
| 212 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs.meta` | 59 | `2fb2a8e1b7388490` | 传递依赖 depth=3 |
| 213 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesWidgets.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesWidgets.cs` | 4727 | `6889b8f4e6397168` | 传递依赖 depth=5 |
| 214 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesWidgets.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesWidgets.cs.meta` | 59 | `d96761936bac4e8d` | 传递依赖 depth=5 |
| 215 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumRadialMenu.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumRadialMenu.cs` | 13521 | `6689d9500424dd1b` | 传递依赖 depth=2 |
| 216 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumRadialMenu.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumRadialMenu.cs.meta` | 59 | `0f31adc2d04ee9a7` | 传递依赖 depth=2 |
| 217 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionAnchor.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionAnchor.cs` | 8147 | `68e88186f94c6cf3` | 传递依赖 depth=2 |
| 218 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionAnchor.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionAnchor.cs.meta` | 243 | `2eaeda00d1e66f91` | 传递依赖 depth=2 |
| 219 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionOverlayGraphic.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionOverlayGraphic.cs` | 32999 | `fe976789505add47` | 传递依赖 depth=3 |
| 220 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionOverlayGraphic.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSelectionOverlayGraphic.cs.meta` | 59 | `311105e35387285f` | 传递依赖 depth=3 |
| 221 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSkyAreaBankView.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSkyAreaBankView.cs` | 60696 | `3d0e3e8d42ec9abc` | 传递依赖 depth=3 |
| 222 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSkyAreaBankView.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSkyAreaBankView.cs.meta` | 59 | `c42f4c13803957db` | 传递依赖 depth=3 |
| 223 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSongFont.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSongFont.cs` | 12796 | `72a841c7c4a6a876` | 传递依赖 depth=3 |
| 224 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumSongFont.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSongFont.cs.meta` | 59 | `fdf567228d86ce08` | 传递依赖 depth=3 |
| 225 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolDialogView.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolDialogView.cs` | 60812 | `4118cbd31e86d0bc` | 传递依赖 depth=3 |
| 226 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolDialogView.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolDialogView.cs.meta` | 61 | `7d14a06fee5145cf` | 传递依赖 depth=3 |
| 227 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolReportView.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolReportView.cs` | 24122 | `0aac76a0ffdce2a4` | 传递依赖 depth=3 |
| 228 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolReportView.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumToolReportView.cs.meta` | 243 | `233f2a45b9956107` | 传递依赖 depth=3 |
| 229 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiFactory.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiFactory.cs` | 5699 | `0d81016fe37d70a1` | 传递依赖 depth=3 |
| 230 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiFactory.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiFactory.cs.meta` | 59 | `85ab96b33305ff13` | 传递依赖 depth=3 |
| 231 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiMotion.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiMotion.cs` | 7463 | `7d821cdb05c4bec1` | 传递依赖 depth=3 |
| 232 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiMotion.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiMotion.cs.meta` | 61 | `6782471bcbcbe20f` | 传递依赖 depth=3 |
| 233 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiStyles.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiStyles.cs` | 2212 | `3b9befd48c3f6479` | 传递依赖 depth=3 |
| 234 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiStyles.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumUiStyles.cs.meta` | 60 | `12fa9dc87db13bac` | 传递依赖 depth=3 |
| 235 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumVolumePreferencesPage.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumVolumePreferencesPage.cs` | 4117 | `2341e134bd022638` | 传递依赖 depth=4 |
| 236 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumVolumePreferencesPage.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumVolumePreferencesPage.cs.meta` | 59 | `39b1b42fe55dde49` | 传递依赖 depth=4 |
| 237 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumWorldTextOverlay.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumWorldTextOverlay.cs` | 7767 | `c81ab816c7bd4e74` | 传递依赖 depth=3 |
| 238 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/FalsumWorldTextOverlay.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/FalsumWorldTextOverlay.cs.meta` | 60 | `85d9e1e754a7d55f` | 传递依赖 depth=3 |
| 239 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/IFalsumPreferencesPageHost.cs` | `Assets/InFalsus/Gameplay/Falsum/UI/IFalsumPreferencesPageHost.cs` | 3461 | `9a3173e722949533` | 传递依赖 depth=5 |
| 240 | `07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI/IFalsumPreferencesPageHost.cs.meta` | `Assets/InFalsus/Gameplay/Falsum/UI/IFalsumPreferencesPageHost.cs.meta` | 59 | `1fbad994d70ece91` | 传递依赖 depth=5 |
| 241 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Audio/FalsumBassAudioEngine.cs` | `Assets/InFalsus/Gameplay/Presentation/Audio/FalsumBassAudioEngine.cs` | 32074 | `faad1bb3a404ce7b` | 传递依赖 depth=1 |
| 242 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Audio/FalsumBassAudioEngine.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Audio/FalsumBassAudioEngine.cs.meta` | 59 | `5d199ee4ec5b1f58` | 传递依赖 depth=1 |
| 243 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Audio/GameplayMasterLimiter.cs` | `Assets/InFalsus/Gameplay/Presentation/Audio/GameplayMasterLimiter.cs` | 4608 | `31caaff281b097fe` | 传递依赖 depth=1 |
| 244 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Audio/GameplayMasterLimiter.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Audio/GameplayMasterLimiter.cs.meta` | 59 | `54bdf4db1f00326e` | 传递依赖 depth=1 |
| 245 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs` | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs` | 17839 | `1a5ffc5053871e12` | 传递依赖 depth=1 |
| 246 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs.meta` | 59 | `d4727e53a4268b35` | 传递依赖 depth=1 |
| 247 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs` | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs` | 29948 | `f8b79fcfa87dd2a3` | 传递依赖 depth=2 |
| 248 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposer.cs.meta` | 59 | `e9c638eb2e81bf4e` | 传递依赖 depth=2 |
| 249 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposerFeature.cs` | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposerFeature.cs` | 8381 | `f412fffbdfe43133` | 传递依赖 depth=5 |
| 250 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposerFeature.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumTrackEndComposerFeature.cs.meta` | 59 | `57c48c259d18376b` | 传递依赖 depth=5 |
| 251 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Effects/NoteHitEffectRenderer.cs` | `Assets/InFalsus/Gameplay/Presentation/Effects/NoteHitEffectRenderer.cs` | 20660 | `48c44a1a7365d468` | 传递依赖 depth=1 |
| 252 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Effects/NoteHitEffectRenderer.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Effects/NoteHitEffectRenderer.cs.meta` | 240 | `8b8ecdf854a96bf9` | 传递依赖 depth=1 |
| 253 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Hud/GameplayHud.cs` | `Assets/InFalsus/Gameplay/Presentation/Hud/GameplayHud.cs` | 7232 | `b0a740268353ec74` | 传递依赖 depth=1 |
| 254 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Hud/GameplayHud.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Hud/GameplayHud.cs.meta` | 59 | `0499480e7270780f` | 传递依赖 depth=1 |
| 255 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaGeometry.cs` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaGeometry.cs` | 1257 | `d05fc689244630bc` | 传递依赖 depth=1 |
| 256 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaGeometry.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaGeometry.cs.meta` | 60 | `d5793548eaff8cde` | 传递依赖 depth=1 |
| 257 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaRowCache.cs` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaRowCache.cs` | 5444 | `421c8f00e61086ef` | 传递依赖 depth=1 |
| 258 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaRowCache.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyAreaRowCache.cs.meta` | 60 | `e816ddbf74c75d92` | 传递依赖 depth=1 |
| 259 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffect.cs` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffect.cs` | 29742 | `ae91d9b3a1e57beb` | 传递依赖 depth=1 |
| 260 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffect.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffect.cs.meta` | 59 | `8efccad88f05f89f` | 传递依赖 depth=1 |
| 261 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffectFeature.cs` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffectFeature.cs` | 7943 | `826a3df9ccba7c92` | 传递依赖 depth=4 |
| 262 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffectFeature.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Notes/SkyJudgementLineEffectFeature.cs.meta` | 59 | `b06ba960679bd269` | 传递依赖 depth=4 |
| 263 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Timing/PresentationAnimationClock.cs` | `Assets/InFalsus/Gameplay/Presentation/Timing/PresentationAnimationClock.cs` | 816 | `6b8768c028f1b68c` | 传递依赖 depth=1 |
| 264 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Timing/PresentationAnimationClock.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Timing/PresentationAnimationClock.cs.meta` | 243 | `9048a356f46bdd9d` | 传递依赖 depth=1 |
| 265 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/GameplayTrackOpacityController.cs` | `Assets/InFalsus/Gameplay/Presentation/Tracks/GameplayTrackOpacityController.cs` | 10156 | `2fad1d14633f2686` | 传递依赖 depth=1 |
| 266 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/GameplayTrackOpacityController.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Tracks/GameplayTrackOpacityController.cs.meta` | 59 | `3ac638216c44980d` | 传递依赖 depth=1 |
| 267 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/LaneStateController.cs` | `Assets/InFalsus/Gameplay/Presentation/Tracks/LaneStateController.cs` | 10650 | `5632eadc41760840` | 传递依赖 depth=1 |
| 268 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/LaneStateController.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Tracks/LaneStateController.cs.meta` | 59 | `ac2064a4ff001a26` | 传递依赖 depth=1 |
| 269 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLineRenderer.cs` | `Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLineRenderer.cs` | 14175 | `33ac98a7e487327d` | 传递依赖 depth=1 |
| 270 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLineRenderer.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Tracks/MeasureLineRenderer.cs.meta` | 59 | `dbaa8467422c498e` | 传递依赖 depth=1 |
| 271 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/SideLaneSurfaceScroller.cs` | `Assets/InFalsus/Gameplay/Presentation/Tracks/SideLaneSurfaceScroller.cs` | 3411 | `7c419320a2524acc` | 传递依赖 depth=1 |
| 272 | `07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks/SideLaneSurfaceScroller.cs.meta` | `Assets/InFalsus/Gameplay/Presentation/Tracks/SideLaneSurfaceScroller.cs.meta` | 59 | `03494dc0577ea6f1` | 传递依赖 depth=1 |
| 273 | `07_full_dependents/Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` | `Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` | 434391 | `e25f6ee94543b1cd` | 玩法场景（场景锚点/模板挂载参考） |
| 274 | `07_full_dependents/Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity.meta` | `Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity.meta` | 155 | `b94c38bf11dd310d` | 玩法场景（场景锚点/模板挂载参考） |
| 275 | `07_full_dependents/Assets/InFalsus/Gameplay/Runtime/Bootstrap/FalsumWindowPlacement.cs` | `Assets/InFalsus/Gameplay/Runtime/Bootstrap/FalsumWindowPlacement.cs` | 697 | `54c2dfbb69bee380` | 传递依赖 depth=1 |
| 276 | `07_full_dependents/Assets/InFalsus/Gameplay/Runtime/Bootstrap/FalsumWindowPlacement.cs.meta` | `Assets/InFalsus/Gameplay/Runtime/Bootstrap/FalsumWindowPlacement.cs.meta` | 59 | `5b28ba7ddec57d4b` | 传递依赖 depth=1 |
| 277 | `07_full_dependents/Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs` | `Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs` | 37503 | `ddefd462fcee0a0e` | 任务点名的依赖方（depth=0） |
| 278 | `07_full_dependents/Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs.meta` | `Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs.meta` | 59 | `7adb4208af1ad01f` | 任务点名的依赖方（depth=0） |
| 279 | `07_full_dependents/Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs` | `Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs` | 18670 | `e00f83bf9a200876` | 任务点名的依赖方（depth=0） |
| 280 | `07_full_dependents/Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs.meta` | `Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs.meta` | 59 | `c58f19a5272620b8` | 任务点名的依赖方（depth=0） |

### 目录 .meta（保持目录 guid，仅列摘要）

共 57 个，全部为 Unity 目录 `.meta`（只为保留目录 guid，内容逐字节来自源工程）：

```
02_runtime/Gameplay.meta                                                                           172  888454a7bd0e8045
02_runtime/Gameplay/Presentation.meta                                                              172  ef8a5adb9a9a8809
02_runtime/Gameplay/Presentation/Audio.meta                                                        172  0882d43086a10426
02_runtime/Gameplay/Presentation/Effects.meta                                                      172  22199e8df0393bee
02_runtime/Gameplay/Presentation/Notes.meta                                                        172  7250c1e209ad4379
03_data/Gameplay.meta                                                                              172  888454a7bd0e8045
03_data/Gameplay/Core.meta                                                                         172  d188c78f7233afe0
03_data/Gameplay/Core/Chart.meta                                                                   172  e6b3ba195d7213fe
03_data/Gameplay/Core/Judgement.meta                                                               172  f25d32cc5a907377
03_data/Gameplay/Falsum.meta                                                                       172  3193e58c7d7d4a91
03_data/Gameplay/Falsum/Core.meta                                                                  172  86df5b691d144faa
04_assets/AnimationClip.meta                                                                       172  0cf93cb571a055f6
04_assets/AnimatorController.meta                                                                  172  d0de426a7b044e26
04_assets/AnimatorOverrideController.meta                                                          172  851958a91ad40452
04_assets/Audio.meta                                                                               172  e1838281ade721b8
04_assets/Audio/Resources.meta                                                                     172  4157fc2ed72b286f
04_assets/GameObject.meta                                                                          172  13cfab8137509c90
04_assets/Material.meta                                                                            172  79bffa0750fa0af0
04_assets/Mesh.meta                                                                                172  d56f058d2c754ba6
04_assets/Shader.meta                                                                              172  0436153e6751e41e
04_assets/Sprite.meta                                                                              172  bdeb8474e03289e7
04_assets/Texture2D.meta                                                                           172  176e4083f76eb4c2
05_config/Assets/InFalsus.meta                                                                     172  81abafd96749cf97
05_config/Assets/InFalsus/Gameplay.meta                                                            172  888454a7bd0e8045
05_config/Assets/InFalsus/Gameplay/Presentation.meta                                               172  ef8a5adb9a9a8809
05_config/Assets/InFalsus/Gameplay/Presentation/Tracks.meta                                        172  d175ab1737fe5d24
05_config/Assets/InFalsus/Gameplay/Recovered.meta                                                  172  bbd67fd4027fccb5
05_config/Assets/InFalsus/Gameplay/Recovered/Shaders.meta                                          172  52564126feb633fd
05_config/Assets/InFalsus/Recovered.meta                                                           172  3ba9bdc2fe77c6be
05_config/Assets/InFalsus/Shaders.meta                                                             172  867296fbb2945c79
05_config/Assets/InFalsus/Shaders/Compat.meta                                                      172  b25fbee3a437de1b
05_config/Assets/Settings.meta                                                                     172  73c8e19843240273
07_full_dependents/Assets/InFalsus.meta                                                            172  81abafd96749cf97
07_full_dependents/Assets/InFalsus/Editor.meta                                                     172  078d61e5f297171c
07_full_dependents/Assets/InFalsus/Gameplay.meta                                                   172  888454a7bd0e8045
07_full_dependents/Assets/InFalsus/Gameplay/Core.meta                                              172  d188c78f7233afe0
07_full_dependents/Assets/InFalsus/Gameplay/Core/Judgement.meta                                    172  f25d32cc5a907377
07_full_dependents/Assets/InFalsus/Gameplay/Core/Timing.meta                                       172  f5ba5208e6277589
07_full_dependents/Assets/InFalsus/Gameplay/Falsum.meta                                            172  3193e58c7d7d4a91
07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Core.meta                                       172  86df5b691d144faa
07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Localization.meta                               172  0b5caa0b1950984b
07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Runtime.meta                                    172  8b62dd53d44d6637
07_full_dependents/Assets/InFalsus/Gameplay/Falsum/Tools.meta                                      170  292c7afcf73fa3d7
07_full_dependents/Assets/InFalsus/Gameplay/Falsum/UI.meta                                         172  3a97842dea4158b5
07_full_dependents/Assets/InFalsus/Gameplay/Presentation.meta                                      172  ef8a5adb9a9a8809
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Audio.meta                                172  0882d43086a10426
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Backgrounds.meta                          172  1f8fb2ff2edbd803
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Effects.meta                              172  22199e8df0393bee
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Hud.meta                                  172  9aba4f487599a661
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Notes.meta                                172  7250c1e209ad4379
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Timing.meta                               172  89a2d4dadb6f4b21
07_full_dependents/Assets/InFalsus/Gameplay/Presentation/Tracks.meta                               172  d175ab1737fe5d24
07_full_dependents/Assets/InFalsus/Gameplay/Recovered.meta                                         172  bbd67fd4027fccb5
07_full_dependents/Assets/InFalsus/Gameplay/Recovered/Scenes.meta                                  172  811b2529d9e2cce7
07_full_dependents/Assets/InFalsus/Gameplay/Runtime.meta                                           172  bf40401fd1a9e01a
07_full_dependents/Assets/InFalsus/Gameplay/Runtime/Bootstrap.meta                                 172  6d6a01be168fde66
07_full_dependents/Assets/InFalsus/Gameplay/Runtime/SceneBinding.meta                              172  d513fcc9c917fe74
```

## 7. 缺失清单

**无。** 任务清单上列的每一个文件（含 .meta）都存在于源工程并已成功拷贝。

搬运过程中唯一遇到的「缺失」是 `ProjectSettings/GraphicsSettings.asset.meta` —— `ProjectSettings/` 与 `Packages/` 下的文件在 Unity 里本来就不生成 `.meta`，这是正常现象，不是缺件。

## 7b. 跨包引用缺口（包内 YAML 资产指向的工程资产未入包）

用 `Temp/flick_closure_check.py` / `Temp/flick_gap_report.py` 扫了包内全部 YAML 资产（`.asset/.prefab/.mat/.controller/.anim/.overrideController/.unity`）里的每一个 `guid:`，反查回源工程后分两类：

- **UNPACKED**：能解析到工程内资产，但该资产没进包 → 潜在断链
- **EXTERNAL**：工程内查不到该 guid → 只能是 Unity builtin 或 package（URP）自带脚本，由 Unity/URP 包自行解析，**不是缺件**

实测结果：

| 包内文件 | UNPACKED 引用数 | 判定 |
|---|---:|---|
| `07_full_dependents/Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` | 98 | 见下方说明（预期缺口） |

**唯一有 UNPACKED 引用的文件是 `GameplayRuntime.unity`（98 处）。** 这是**预期且有意**的：
玩法场景本身引用整条轨道系统（`LANE_*/JUDGE_*/SKY` mesh、`NoteFX_*` 控制器、`cursor2.mat`、`alamode.ogg` 等），本包只做 Flick 纵向切片，场景作为「`FLICK_NOTE` 挂载点 / 模板节点层级」的参考样本附带，**不承诺**它能脱离原工程单独运行。

逐条列出未被覆盖的引用目标（共 98 个资产），供判断是否需要补包：

| 未入包的被引用资产 |
|---|
| `Assets/InFalsus/Gameplay/Audio/Resources/alamode.ogg` |
| `Assets/InFalsus/Gameplay/Recovered/Materials/PhaseABackground.mat` |
| `Assets/InFalsus/Gameplay/Recovered/Materials/PhaseALane.mat` |
| `Assets/InFalsus/Gameplay/Recovered/Materials/PhaseAWallLeft.mat` |
| `Assets/InFalsus/Gameplay/Recovered/Materials/PhaseAWallRight.mat` |
| `Assets/InFalsus/Recovered/AnimatorController/NoteFX_Main_0.controller` |
| `Assets/InFalsus/Recovered/AnimatorController/NoteFX_Radial_0.controller` |
| `Assets/InFalsus/Recovered/AnimatorController/NoteHoldFXV2_0.controller` |
| `Assets/InFalsus/Recovered/Constrained2DQuadInstanceMesh.asset` |
| `Assets/InFalsus/Recovered/GameObject/NoteSideLeftHoldPrefab.prefab` |
| `Assets/InFalsus/Recovered/GameObject/NoteSideLeftTapPrefab.prefab` |
| `Assets/InFalsus/Recovered/GameObject/NoteSideRightHoldPrefab.prefab` |
| `Assets/InFalsus/Recovered/GameObject/NoteSideRightTapPrefab.prefab` |
| `Assets/InFalsus/Recovered/GameObject/note single blue.prefab` |
| `Assets/InFalsus/Recovered/GameObject/note span blue.prefab` |
| `Assets/InFalsus/Recovered/GameObject/notehold floor blue v2.prefab` |
| `Assets/InFalsus/Recovered/Material/GameplayBottomLane Variant.mat` |
| `Assets/InFalsus/Recovered/Material/GuideNoteDimLeftMat.mat` |
| `Assets/InFalsus/Recovered/Material/GuideNoteDimRIghtMat.mat` |
| `Assets/InFalsus/Recovered/Material/JudgementBehindHitBurst 1Lane Variant.mat` |
| `Assets/InFalsus/Recovered/Material/JudgementBehindHitBurst 2Lane Variant.mat` |
| `Assets/InFalsus/Recovered/Material/JudgementBehindHitBurst 4Lane Variant.mat` |
| `Assets/InFalsus/Recovered/Material/Lit.mat` |
| `Assets/InFalsus/Recovered/Material/SkyArea Variant.mat` |
| `Assets/InFalsus/Recovered/Material/SkyJudgementLineGlow Variant.mat` |
| `Assets/InFalsus/Recovered/Material/SpritesDefault.mat` |
| `Assets/InFalsus/Recovered/Material/TestMeshJudgementLineEffect.mat` |
| `Assets/InFalsus/Recovered/Material/TriangleJudgementLineParticleBase.mat` |
| `Assets/InFalsus/Recovered/Material/backing-combo.mat` |
| `Assets/InFalsus/Recovered/Material/cursor-line-yellow.mat` |
| `Assets/InFalsus/Recovered/Material/cursor-precisionpoint.mat` |
| `Assets/InFalsus/Recovered/Material/cursor2.mat` |
| `Assets/InFalsus/Recovered/Material/cursor2_ghost.mat` |
| `Assets/InFalsus/Recovered/Material/judgement-below-line.mat` |
| `Assets/InFalsus/Recovered/Material/judgement-line.mat` |
| `Assets/InFalsus/Recovered/Material/judgement_line.mat` |
| `Assets/InFalsus/Recovered/Material/judgement_line_alt.mat` |
| `Assets/InFalsus/Recovered/Material/matid_edgeglows.mat` |
| `Assets/InFalsus/Recovered/Material/matid_floornote.mat` |
| `Assets/InFalsus/Recovered/Material/matid_judgement.mat` |
| `Assets/InFalsus/Recovered/Material/matid_judgement_line.mat` |
| `Assets/InFalsus/Recovered/Material/matid_lanedividers.mat` |
| `Assets/InFalsus/Recovered/Material/matid_notehold.mat` |
| `Assets/InFalsus/Recovered/Material/matid_skylanes.mat` |
| `Assets/InFalsus/Recovered/Material/matid_wallhighlight.mat` |
| `Assets/InFalsus/Recovered/Material/matid_wallhighlight_alt.mat` |
| `Assets/InFalsus/Recovered/Material/matid_wallnote.mat` |
| `Assets/InFalsus/Recovered/Mesh/1_LEFT.asset` |
| `Assets/InFalsus/Recovered/Mesh/2.asset` |
| `Assets/InFalsus/Recovered/Mesh/3.asset` |
| `Assets/InFalsus/Recovered/Mesh/4.asset` |
| `Assets/InFalsus/Recovered/Mesh/5_RIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/CR_LEFT.asset` |
| `Assets/InFalsus/Recovered/Mesh/CR_RIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/Constrained2DQuadInstanceMesh_0.asset` |
| `Assets/InFalsus/Recovered/Mesh/Constrained2DQuadInstanceMesh_1.asset` |
| `Assets/InFalsus/Recovered/Mesh/Constrained2DQuadInstanceMesh_2.asset` |
| `Assets/InFalsus/Recovered/Mesh/Constrained2DQuadInstanceMesh_3.asset` |
| `Assets/InFalsus/Recovered/Mesh/Constrained2DQuadInstanceMesh_4.asset` |
| `Assets/InFalsus/Recovered/Mesh/Cursor.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_MAIN.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_MAINGLOW.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_SHIFT.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_SHIFTGLOW.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_SKY.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_SPACE.asset` |
| `Assets/InFalsus/Recovered/Mesh/JUDGE_SPACEGLOW.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_1.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_1_HLIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_2.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_2_HLIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_3.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_3_HLIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_4.asset` |
| `Assets/InFalsus/Recovered/Mesh/LANE_4_HLIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/Mesh_13.asset` |
| `Assets/InFalsus/Recovered/Mesh/SHIFT_2.asset` |
| `Assets/InFalsus/Recovered/Mesh/SHIFT_HIGHLIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/SKY.asset` |
| `Assets/InFalsus/Recovered/Mesh/SPACE_2.asset` |
| `Assets/InFalsus/Recovered/Mesh/SPACE_HIGHLIGHT.asset` |
| `Assets/InFalsus/Recovered/Mesh/note1.asset` |
| `Assets/InFalsus/Recovered/Mesh/note2.asset` |
| `Assets/InFalsus/Recovered/Mesh/note3.asset` |
| `Assets/InFalsus/Recovered/Mesh/note4.asset` |
| `Assets/InFalsus/Recovered/Mesh/notehold01.asset` |
| `Assets/InFalsus/Recovered/Mesh/notehold01_2.asset` |
| `Assets/InFalsus/Recovered/Mesh/wallnote01_1.asset` |
| `Assets/InFalsus/Recovered/Mesh/wallnote01_1_2.asset` |
| `Assets/InFalsus/Recovered/SectionControllableHighlightBlendedVariant.mat` |
| `Assets/InFalsus/Recovered/Sprite/NoteFX_Main_0.asset` |
| `Assets/InFalsus/Recovered/Sprite/NoteFX_Radial_0.asset` |
| `Assets/InFalsus/Recovered/Sprite/NoteHoldFXV2_0.asset` |
| `Assets/InFalsus/Recovered/Texture2D/judgement-01-ExactPlus.png` |
| `Assets/InFalsus/Recovered/Texture2D/judgement-02-Exact.png` |
| `Assets/InFalsus/Recovered/Texture2D/judgement-03-Near.png` |
| `Assets/InFalsus/Recovered/Texture2D/judgement-04-Break.png` |
| `Assets/InFalsus/Recovered/judgement-02-Exact.mat` |

其余资产材料/网格/prefab/sprite/动画的引用都是闭合的。特别说明三个容易被误判为「缺 guid」的情况：

| 包内文件 | EXTERNAL guid | 真相 |
|---|---|---|
| `04_assets/Material/FlickBurstMaterial.mat` | `0000000000000000f000000000000000` (fileID 10753) | Unity **builtin Sprite shader**，内置，无需携带 |
| `04_assets/Material/Mat.001.mat`、`Mat.001 R.mat`、`matid_flicknote.mat` | `d0353a89b1f911e48b9e16bdc9f2e058` | URP 包的 `MaterialEditor` 脚本（`m_EditorClassIdentifier` 写着 `Unity.RenderPipelines.Universal.Editor:`），package 内脚本，非工程资产 |
| `05_config/Assets/Settings/*.asset` | 大量 guid | Volume 组件（Bloom/LiftGammaGain/...）与 URP Renderer Feature 的脚本/参数 guid，全部属于 URP package；`UniversalRenderPipelineGlobalSettings.asset` 里上百个是 URP 内部的资源条目 |

## 7c. `07_full_dependents/` 的构成

对 13 个种子文件（任务点名 + 01/02/03 收到的那些）做了类型级传递依赖闭包，共触达 147 个 .cs；去掉已归入 01/02/03 的 8 个种子，其余按原路径层级放入 `07_full_dependents/`。

| 传递深度 | 文件数（含 .meta） |
|---|---:|
| depth=0 | 10 |
| depth=1 | 68 |
| depth=2 | 98 |
| depth=3 | 52 |
| depth=4 | 42 |
| depth=5 | 8 |
| 任务点名依赖方（depth=0，共 5 个 .cs） | 10 |
| 玩法场景 GameplayRuntime.unity | 2 |

> 闭包口径说明：按「C# 源码里出现某个已声明类型名」的粗粒度规则展开，比真实编译依赖**偏大**（会带上同名标识符误命中）。这是有意的宁多勿少策略，代价是 `07_full_dependents/` 里混入了 FALSUM 编辑器的一部分 UI 代码。

## 8. 校验方法（可复现）

```powershell
# 在 InFalsus-Gameplay 工程根执行
python Temp\flick_copy.py        # 重新搬运（幂等，覆盖同名文件）
python Temp\flick_dep_scan.py    # 重算 C# 传递依赖闭包
python Temp\flick_ref_check.py   # 校验 04_assets 内资产 guid 引用
python Temp\flick_closure_check.py  # 校验包内所有 YAML 资产的 guid 引用
```

抽样 sha256 校验（源 vs 包内，应逐字节相同）：

```powershell
$pairs = @(
  @("Assets/InFalsus/Recovered/dc.shader", "Flick_Implementation_Package/01_shader/dc.shader"),
  @("Assets/InFalsus/Recovered/Mesh/Flick.asset", "Flick_Implementation_Package/04_assets/Mesh/Flick.asset"),
  @("Assets/InFalsus/Recovered/Sprite/FlickBurst_63.asset", "Flick_Implementation_Package/04_assets/Sprite/FlickBurst_63.asset")
)
foreach ($p in $pairs) {
  $a = (Get-FileHash $p[0] -Algorithm SHA256).Hash
  $b = (Get-FileHash $p[1] -Algorithm SHA256).Hash
  "{0}  {1}" -f ($(if ($a -eq $b) { "OK  " } else { "FAIL" })), $p[0]
}
```

