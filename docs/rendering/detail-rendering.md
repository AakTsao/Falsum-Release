# In Falsus 渲染/表现层地图（附录）

> 范围：`Assets/InFalsus/Gameplay/Presentation/`、`Gameplay/Falsum/Runtime/`、`Gameplay/Runtime/`。
>
> 注意：表现层大部分 C# 是 **IL 反编译产物**（大量 `(Object)(object)` 转型，见
> `GameplayBootstrap.cs:203`），而 `Falsum/` 下的编辑器代码是手写的。行号可直接引用，
> 但读代码时别被反编译风格误导——行为是原版的，注释是后加的。

## 1. 子系统总表

| 子系统 | 文件 | 职责 |
|---|---|---|
| Notes | `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs` | 全部音符视觉：tap/宽 tap/侧 tap/hold/侧 hold/flick/skyarea，按 kind 分池与预制体克隆（`NoteRenderer.cs:15`、`:357` 构造函数 13 个预制体参数） |
| Notes | `.../Notes/SkyAreaGeometry.cs` | SkyArea 细分与缓动数学：5ms 步长、共享边界 epsilon（`:9`、`:12`、`:25`、`:39`） |
| Notes | `.../Notes/SkyJudgementLineEffect.cs` | 判定线辉光独立相机/RT 链（4 buffer、0.0001 模糊、0.3s 淡出，`:16-22`、`:162`） |
| Notes | `.../Notes/SkyJudgementLineEffectFeature.cs` | URP RenderGraph 注入：DrawPass + BlitPass（`:8`、`:38`、`:206`、`:211`） |
| Effects | `.../Effects/NoteHitEffectRenderer.cs` | NoteFX_Main / NoteFX_Radial / NoteHoldFXV2 三池各 16（`:21`、`:99-113`、`:135-137`） |
| Effects | `.../Effects/FlickHitEffectRenderer.cs` | FlickBurst 左右各 4 池 + 判定反馈字（`:13-22`、`:377`、`:474`） |
| Tracks | `.../Tracks/MeasureLineRenderer.cs` | 三条动态 mesh（Middle/Shift/Space）逐帧重建小节线（`:11-16`、`:64-66`、`:99`） |
| Tracks | `.../Tracks/SideLaneSurfaceScroller.cs` | 侧轨纹理滚动（4.0m 周期，`:9`、`:56`） |
| Tracks | `.../Tracks/GameplayTrackOpacityController.cs` | 全轨/单轨透明度与渐变（`:21`、`:70`、`:85`、`:99`、`:118`） |
| Tracks | `.../Tracks/LaneStateController.cs` | 难度 1/2 `lane(...)` 启停贴图切换（`:9`、`:54`） |
| Backgrounds | `.../Backgrounds/FalsumBackgroundPresenter.cs` | 背景/playfield 贴图与 renderQueue 2900（`:16`、`:66`、`:137`） |
| Backgrounds | `.../Backgrounds/FalsumTrackEndComposer.cs` | TrackEnd 菱形遮罩合成（100 三角、0.5/2.0 缩放、0.017 模糊，`:29-32`、`:50`、`:215`） |
| Backgrounds | `.../Backgrounds/FalsumTrackEndComposerFeature.cs` | TrackEnd 的 URP RendererFeature 注入（`:8`） |
| Audio | `.../Audio/GameplayHitAudio.cs` | 判定音/长条音/侧音，池与语音上限（`:34`、`:42`、`:52`） |
| Audio | `.../Audio/FalsumBassAudioEngine.cs` | BASS 后端 BGM/增益，失败回退托管（`:14`、`:118`） |
| Audio | `.../Audio/GameplayMasterLimiter.cs` | 主输出限制器 DSP（`:7`、`:53`） |
| Hud | `.../Hud/GameplayHud.cs` | HUD 绑定与运行时状态注入（`:9`、`:23`、`:39`） |
| Timing | `.../Timing/PresentationAnimationClock.cs` | 仅表现层用的独立累加时钟，0.1s 帧钳制（`:7`、`:12`、`:22`） |
| Falsum | `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumComboDisplay.cs` | Combo 数字/底板显示（`:10`、`:68`、`:120`、`:150`） |
| Falsum | `.../Falsum/Runtime/FalsumTrackSpectrumRenderer.cs` | 编辑器轨道频谱贴图（Warp 256、queue 3002，`:13-17`、`:194`） |
| Bootstrap | `Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs` | 唯一 Update 驱动、组件装配与编辑器帧刷新（`:266-318`、`:371`、`:809`） |
| SceneBinding | `Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs` | 场景路径常量与模板/材质句柄（`:35-66`、`:86-90`、`:241-255`） |

## 2. 每帧执行顺序（GameplayBootstrap.Update 为唯一入口）

`Update()` 先调 `UpdatePresentationAnimation()`（`GameplayBootstrap.cs:373`），再按 `_running && !EditorModeActive` 分支（`:374`）：

1. **GameplayClock**（`Assets/InFalsus/Gameplay/Core/Timing/GameplayClock.cs:35`）：`_clock.TimeMs` 由音频位置 + `_playbackRate` 推算（`:90-96`）；游玩模式下 `P` 键切 Pause/Resume（`GameplayBootstrap.cs:378`，`(KeyCode)112`），`R` 键 Restart（`:397`，`(KeyCode)114`）。注意这是**非编辑器模式**（`EditorModeActive` 时 `Update` 提前返回），编辑器里用 `Q`/`Space`。
2. **判定与音频**：`_engine.Update(now)`、`_hitAudio.Tick(now)`（`GameplayBootstrap.cs:413-417`）。
3. **NoteRenderer.Update(now)**（`:418`）→ 4. **SideLaneSurfaceScroller**（`:419`）→ 5. **LaneStateController**（`:420`）→ 6. **MeasureLineRenderer**（`:421`）。这四者只在 `_clock.IsPlaying` 时跑（`:402`）。
7. **FlickHitEffectRenderer.Tick / NoteHitEffectRenderer.Tick**（`:425`、`:429`）**在播放判断之外**，暂停时仍以 `paused=true` 推进淡出。
8. **PresentationAnimationClock.Tick(running, Time.unscaledDeltaTime)**（`:444`）→ `_wallScroller.SetPresentationTime(seconds)`（`:448`）→ `BackgroundPresenter.TrackEndComposer.SetPresentationTime(seconds)`（`:449`）。running 取自 `_clock.IsPlaying`（`:440-443`）。
9. **FalsumComboDisplay.Update(timeMs, _chart)** 只在编辑器路径 `RefreshPresentationAt` 里（`:813`），与 2-6 同序（`:817-829`）。
10. **FalsumTrackSpectrumRenderer.Update** 仅编辑器世界：`FalsumEditorWorld.cs:359`；`NoteRenderer.UpdateEditorGhost`（同文件 `:2058`）。
11. **LateUpdate**（`GameplayBootstrap.cs:452`）：编辑器暂停时补停音频，不刷新视觉。

**暂停**：`_clock.Pause()` 使 3-6 停更、8 的 running 变 false 而 TimeSeconds 冻结、7 转为淡出模式；`_hitAudio.SetPaused(true)`（`:383-386`）。
**Seek**：`RefreshEditorAt` → `_clock.Seek` + `RefreshPresentationAt` 逐项重算（`:547`、`:809-830`）；不进 `Update` 的视觉项（wall/TrackEnd）不会随之刷新。
**变速**：`SetPlaybackRate` 只改 `_clock` 与音频调度（`:679`、`:686`）；表现时钟按真实秒累加，故变速会改变墙/TrackEnd 的动画相位，不改变音符位移时间轴。

## 3. 恢复资源约定

- **材质命名**：`Assets/InFalsus/Recovered/Material/` 下 `matid_*` 原名保留（共 15 个：`matid_trackfloor1` / `matid_trackwall1` / `matid_trackwall1_alt` / `matid_notesingle` / `matid_notespan` / `matid_floornote` / `matid_wallnote` / `matid_notehold` / `matid_flicknote` / `matid_judgement` / `matid_judgement_line` / `matid_lanedividers` / `matid_skylanes` / `matid_wallhighlight` + `matid_wallhighlight_alt` / `matid_notesidesingle` + `matid_notesidesingle_alt`）。轨道侧统一从 `matid_trackfloor1`/`matid_trackwall1`/`matid_trackwall1_alt` 派生（`Editor/GameplaySceneBuilder.cs:62-66`；贴图 `Recovered/Texture2D/notes_track_matid_track*_AlbedoTransparency.png`，`:26-30`）。
- **音符预制体**（均在 `Recovered/GameObject/`，由 `Editor/GameplaySceneBuilder.cs:34-50` 装到 Bootstrap 序列化字段 `GameplayBootstrap.cs:63-88`）：`note single blue.prefab`、`note span blue.prefab`、`notehold floor blue v2.prefab`、`v5-3.prefab`（skyarea）、`v5-3R.prefab`、`NoteSideLeft/RightTapPrefab`、`NoteSideLeft/RightHoldPrefab`。侧轨/主轨都用原预制体而非场景占位（README:146-147）。
- **Shader 兼容层两条腿**：① `Assets/InFalsus/Shaders/Compat/` 存 URP 重写版 `FalsumTrackEndDiamondMask/Blur/Overlay`（名称 `InFalsus/FalsumTrackEndDiamond*`，`FalsumTrackEndComposer.cs:15-20`）与 `InFalsus/Compat/StenciledUnlitSprite`；② `Assets/InFalsus/Gameplay/Resources/` 走 `Resources.Load`：`SkyArea/Shaders/SkyAreaBracket`(`InFalsus/Replica/SkyAreaBracket`)、`SkyAreaShadow`、`SkyJudgementLineBlur/Mask`(`InFalsus/Recovered/*`)，另有 `SkyArea/BRACKET.obj` 与 `SquareBracket-Diffuse.png`（`NoteRenderer.cs:374-378`、`:438`）。Falsum 侧 shader 走 `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Shaders/*`（`FalsumTrackSpectrumRenderer.cs:105`）。
- **路径规则**：新增视觉资产只允许落在 `Recovered/`（原始二进制恢复，保持原名与小写空格风格）或 `Gameplay/Resources/`、`Gameplay/Falsum/Resources/`（`Resources.Load` 可达）或 `Shaders/Compat/`（URP 兼容改写）。改 shader 名必须同步改 C# 里的字符串常量，否则运行时静默回退（`FalsumTrackEndComposer.cs:183-188`）。不要给 `Recovered/` 内资产改名——路径被硬编码。

## 4. 昂贵视觉不变量（改任何一条都会破坏画面）

1. **Flick 方向/颜色编码固定**：`4 = 绿/右`，`16 = 黄/左`。模板选择 `note.FlickDirection == 4 ? _flickRightPrefab : _flickLeftPrefab`（`NoteRenderer.cs:1311`）；基础旋转仅 16 用 identity（`:1365`）；颜色 `(0.2,0.95,0.55)` vs `(1,0.82,0.18)`（`:2286`）；Trail 宽度按 `span / 1.54f` 缩放后回接头部（`:1414-1415`、`AlignFlickPlane` `:1485`）。
2. **Hold 必须三段齐全**：`HoldFront/HoldMiddle/HoldBack` + 两个 Renderer（`NoteRenderer.cs:91-99`），捏合重叠 `HoldJoinOverlap = 0.05f`（`:184`）、flick 头重叠 `0.08f`（`:186`），只在判定面裁剪且尾端过线前保持可见。
3. **SkyArea 每条连续链一个 mesh**：`BuildSkyChains()` 按 `GroupId` 聚合并只在 `TimeMs`/`Id` 排序后建组（`NoteRenderer.cs:1067-1107`、`:414`）；几何按 5ms 细分 + 共享边界 epsilon（`SkyAreaGeometry.cs:9-12`、`:39-45`）。禁止逐段建 mesh。
3a. **音符可视窗口按「距离」换算，不是固定时间**（2026-11-02）：`NoteRenderer.EnsureVisualsForTime`
   只保留窗口内实例，而音符何时不可见由 `TrackMotion.IsVisible`（`|z| < TrackLength`）决定。
   窗口长度必须等于「走完 `TrackLength × 1.1` 所需时间」（`VisualLeadMsForTrackSpeed`），
   上限 `VisualWindowCeilingMs = 40000`，后向固定 `VisualWindowTrailingMs = 1500`，
   反向段（`track(-20)` 等）宽边落在过去（`VisualWindowEdges`），每帧创建上限
   `VisualCreateBudgetPerFrame = 48`。旧公式只在 `flow × trackSpeed ≥ 0.7` 时覆盖整条轨道，
   低流速下音符会在轨道中段凭空出现。见 `02-invariants.md` §B-16。
4. **Measure line 只画判定线→轨道末端**：`targetZ` 必须落在 `[_lineBaseZ-0.001, _trackEndZ+0.001]` 否则整条跳过（`MeasureLineRenderer.cs:121-126`）；宽度 0.018、贴地抬升 0.003（`:14-15`）；单次 alpha 混合、无 glow（README:136-138）。
5. **外侧墙三角形特效已整体移除**（2026-11-01）：`GameplayWallScroller`、`WallMaterial.mat`、`GameplayWallRectangleTriangles` 已删除，场景里的 `LeftWallTriangles` / `RightWallTriangles` 也一并移除（见 `05-gaps.md` §G-13）。
6. **TrackEnd 遮罩依赖 `_TimeOverride`**：`FalsumTrackEndComposer.cs:50` 定义 ID，`:174` 写入；shader 无该属性时只警告一次并回退 Unity 时间（`:183-188`），改 shader 必须保留 `_TimeOverride`。
7. **音符预制体清单不可随意替换**：见 §3；Bootstrap 以序列化字段注入（`GameplayBootstrap.cs:63-88`），`NoteRenderer` 构造签名 13 参（`:357`）——加参数会打断编辑器装配。
8. **调参常量**：FlickBurst 0.5333s / 淡出幂 2.0 / 反馈 0.58s / 高 0.045 / 缩放 0.09 / 半宽 0.81 / 池 4+8（`FlickHitEffectRenderer.cs:13-21`）；NoteFX 池 16（`NoteHitEffectRenderer.cs:21`）、`AlwaysAnimate` + `sortingOrder`（`:200-202`）；TrackEnd 100 三角/0.5/2.0/0.017（`FalsumTrackEndComposer.cs:29-32`）；SkyJudgementLine 4 buffer/0.0001/0.75/0.3s（`SkyJudgementLineEffect.cs:19-22`）；表现时钟 0.1s 钳制（`PresentationAnimationClock.cs:7`）。
9. **渲染队列分层**：背景 2900（`FalsumBackgroundPresenter.cs:16`）< 频谱 3002（`FalsumTrackSpectrumRenderer.cs:17`）；顺序改动会立刻穿插。
10. **侧轨纹理周期 4.0m**（`SideLaneSurfaceScroller.cs:9`）+ `_Offset` 属性驱动。

## 5. 已知缺口

- **NoteFX 三件套处于"已接线但原件被隐藏"的中间态**：`NoteFX_Main_0` / `NoteFX_Radial_0` / `NoteHoldFXV2_0` 在场景存在且被 `Require` 绑定（`GameplaySceneBinding.cs:44-49`、`:241-249`）；`NoteHitEffectRenderer` 订阅 `NoteStarted` 与 `Judged`（`NoteHitEffectRenderer.cs:121-122`）并建三池（`:99-137`），模板来自 binding 的 `NoteFXRadial/Hold/MainTemplate`（`:86-88`）。同时 `HidePreexistingNoteEffects()` 把 `NotesContainer` 下所有非 `PhaseA_Note_` 子物体 `SetActive(false)`（`GameplayBootstrap.cs:950-960`），即场景原件只作模板、显示由运行时克隆池负责。因此 README:163-164 "存在但未重挂判定事件"已过时——代码确实挂了；但"原件可见"仍为否。冲突点归属 **未核实**。
- **`EnableExactFeedback` 关闭**：`FlickHitEffectRenderer.cs:22` `private const bool EnableExactFeedback = false`，`:688-694` 使 ExactPlus/Exact 不弹反馈字；`:691` 只在置 true 时才走精确反馈。
- **FalsumTrackSpectrumRenderer 只服务编辑器**：`FalsumEditorWorld.cs:127` 持有、`:359` 调 `Update`；游戏内 `GameplayBootstrap` 无引用，故游戏画面没有频谱条。
- **TrackEnd / SkyJudgementLine 的 RendererFeature 需挂在 URP Renderer 上**（`FalsumTrackEndComposerFeature.cs:8`、`SkyJudgementLineEffectFeature.cs:8`）。TrackEnd 有自动安装兜底：静态构造在非 batch 模式下 `delayCall → EnsureInstalled(false)`（`Editor/FalsumTrackEndComposerInstaller.cs:17-27`），构建时 `FalsumBuild.cs:49` 也会强制装；菜单入口 `InFalsus/Gameplay/Repair TrackEnd Composer`（`:30-33`），SkyJudgementLine 由 `Editor/SkyAreaInstaller.cs:16` 安装 `Assets/Settings/SkyJudgementLineEffectFeature.asset`（`:32-37`），但只暴露手动菜单 `In Falsus/SkyArea/Install Original Renderer`（`:18-19`），没有 delayCall 兜底；自检 `FalsumSelfCheck.cs:918` 会断言该 feature 已挂。
- **README 与代码出入**：README:155-156 称 SkyArea 为"one mesh per contiguous group chain"，代码聚合键是 `GroupId` 而非几何连续性（`NoteRenderer.cs:1089-1107`），连续性实际在前置的 groupId 写入阶段（README:157-160）完成；README:144-145 称轨道从 `matid_*` 派生，实际是 `Editor/GameplaySceneBuilder.cs` 以 `matid_trackfloor1` / `matid_trackwall1` 为源复制材质后改参数（`CreateOrUpdateLaneMaterial` / `CreateOrUpdateWallMaterial`）。
