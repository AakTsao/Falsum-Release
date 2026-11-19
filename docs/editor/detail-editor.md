# FALSUM 谱面编辑器创作层架构图（附录）

> 正文版见 [`../01-architecture.md`](../01-architecture.md) §A 与 [`../02-invariants.md`](../02-invariants.md) §A。
> 本文件是**逐文件级别**的细节扫描，带 `path:line`；正文版是提炼后的结论。
> 若两者冲突，以代码为准，并回来修正本文。
>
> 引用约定：所有路径省略公共前缀 `Assets/InFalsus/Gameplay/`；`Falsum/...` 即 `Assets/InFalsus/Gameplay/Falsum/...`，`Core/Chart/...` 即 `Assets/InFalsus/Gameplay/Core/Chart/...`。

## 1. 分层总表

| 层 | 文件 | 职责 |
|---|---|---|
| 包容器 | `Falsum/Core/FalsumPackage.cs` | `.falsum` zip 的读写/校验，未知条目保留，临时文件原子替换 |
| 清单 | `Falsum/Core/FalsumManifest.cs` | `song.json` 模型：songId/title/artist/audio/cover/difficulties 与路径白名单校验 |
| JSON | `Falsum/Core/FalsumJson.cs` | 无依赖 JSON 树（`FalsumJsonObject`）、深拷贝与序列化 |
| 谱面 DSL | `Core/Chart/ChartParser.cs` | 解析 `chart/bpm/track/lane/tap/hold/flick/skyarea` 与诊断 |
| 谱面 DSL | `Falsum/Core/ChartWriter.cs` | 反向写谱；delay/bpm/track 派生行、flick/skyarea 量化 |
| 谱面模型 | `Core/Chart/ChartModels.cs` `:568` | `Chart`/`ChartNote`/`ChartEvent`、`ChartTimeAxis` 两时间轴、小节线生成 |
| 编辑会话 | `Falsum/Core/FalsumChartSession.cs` | 快照式 undo/redo、批次事务、外部状态（元数据）桥接 |
| 天空区分组 | `Falsum/Core/FalsumSkyAreaGrouping.cs` | skyarea `groupId` 归一化、片段连接判定、输出排序 |
| 分数/网格辅助 | `FalsumAutoCombo.cs` `FalsumBeatGrid.cs` `FalsumFraction.cs` `FalsumSkyBounds.cs` | 自动连击计数、可视网格与吸附、分母量化、0..1 边界钳制 |
| 主编辑器 | `Falsum/Runtime/FalsumChartEditor.cs`（12565 行） | 模式机、指针/键盘、放置/拖拽/复制粘贴、保存、备份、工具调度 |
| 中键精修 | `FalsumChartEditor.MiddleEdit.cs`（3185 行） | Boundary/StartEnd/Position 三种拖拽语义与多选同步 |
| 天空区吸附 | `FalsumChartEditor.SkyAreaSnap.cs` | 屏幕空间 12px 内的组首尾吸附 |
| 3D 视图 | `FalsumEditorWorld.cs`（3020） | 网格构建、拾取、指针反投影、幽灵预览 |
| 音频/时钟 | `FalsumPitchPreservingPlayback.cs` `FalsumMediaConverter.cs` `FalsumBassMediaImporter.cs` `FalsumBassEncoder.cs` | 变速率保音高颗粒合成；音频转码全进程内 BASS 解码/重采样/编码，封面 Unity 图像转换 |
| 偏好 | `FalsumPreferences.cs` `FalsumPreferencesStore.cs` `FalsumDisplaySettingsController.cs` `FalsumInputRouter.cs` | 键位表、持久化（原子替换）、显示/抗锯齿、动作→按键路由 |
| 备份/缓存 | `FalsumBackupService.cs` `FalsumCacheStore.cs` `FalsumTrackSpectrum*.cs` `FalsumTrackWaveformCache.cs` | `autobackups/` 谱面快照、`FalsumCache/` 音频 ogg；波形只在内存 |
| 元数据 | `FalsumMetadataApplyService.cs` `FalsumChartMetadataDraft.cs` | 偏好页元数据草稿 → 校验 → 应用/回滚，变更进 undo 栈 |
| 工具脚本 | `Falsum/Tools/*` | Jint 沙箱、清单/权限、表单/报告模型、编辑批次、天空区可玩性检查 |
| 界面 | `Falsum/UI/FalsumEditorView.cs`（7267）等 | UI Toolkit 视图，全部 `event` 回灌给编辑器 |

## 2. 数据模型

**包布局**（`FalsumPackage.cs:82-163` 读、`:514-591` 写）：`song.json`（必）、`audio.ogg`（必，大小上限 128MB）、`cover.png`（可选，32MB）、`charts/<name>.chart.txt`（每难度一份，16MB）、其余条目按原字节保留（`:152-163`、`:580-591`）。写包时 entry 时间戳被固定为 `1980-01-01`（`:823-830`）以保证字节可复现；音频/封面 `NoCompression`，谱面/未知条目压缩（`:518-590`）。路径安全：禁 `\`、绝对路径、`..`（`:652-682`）。

**song.json 字段**（`FalsumManifest.cs:293-346`）：`format="falsum"`、`version=1`（`:82-83`，强校验 `:267-286`）、`songId`（正则 `^[a-z0-9][a-z0-9_-]{0,63}$`，`:85-87`）、`defaultLocale`、`title`、`artist`、`jacketDesigner`、`background`（默认 `31_Space-Positive.png`，`:84`）、`audio{file,codec,sampleRate=48000,channels=2,durationMs}`、`cover{file,width,height}`、`difficulties[]{index 0..3,name=MIN/EVO/ULT/FBD,available,chart,rating,chartDesigner,jacketDesigner}`。硬约束：`audio.file=="audio.ogg"`、`cover.file=="cover.png"`、`chart` 必须以 `charts/` 开头且以 `.chart.txt` 结尾、路径集合不许重复、至少一个 `available`（`:506-630`、`:656-668`）。

**谱面 DSL**（`ChartParser.cs:161-451`，行格式 `directive(a,b,...)`，`#` 开头为注释）：
- `chart(bpm,division)`：基准 BPM 与每拍细分数，必须存在且为正（`:163-178`、`:116-124`）。
- `track(t,multiplier)`：t 处起滚动速度倍率，只影响行进速度（`:180-199`）。
- `bpm(t,bpm,division[,1])`：变速点；第 4 参数为遗留标志，非 1 报错（`:201-235`）。
- `lane(t,0|1|4|5,0|1)`：仅 MIN/EVO 允许（`ValidateLaneEvents` `:611-629`），中键可切换侧轨。
- `tap(t,width,lane)`：`width` 为占几列、`lane` 起始 0..5，要求 `lane+width<=6`（`:274-297`、`:631-646`）。
- `hold(t,lane,width,durationMs)`：`duration>0`（`:299-330`）。注意 tap 是 `(t,width,lane)`、hold 是 `(t,lane,width,d)`，**参数顺序相反**。
- `flick(t,centerPos,grid,widthCells,dir)`：`StartPos=centerPos/grid`、`StartWidth=widthCells/grid`，`dir` 只允许 **4 或 16**（`:332-376`）；`4=右侧绿`、`16=左侧黄`（`ChartModels.cs:73-81`、`FalsumEditorView.cs:6734-6739`）。
- `skyarea(t,startCenter,startCells,startWidth,endCenter,endCells,endWidth,leftEase,rightEase,durationMs,groupId)`：共 11 参；中心/宽度都以“格/格数”比例化；easing ∈ 0..2（0=线性、1=out、2=in，`FalsumChartEditor.cs:9872`）；`groupId>=0`（`:379-445`）。

**两条时间轴**（`ChartModels.cs:568-599`）：谱面时间（编辑器/网格/播放头，注释见 `:311-313`）与音乐时间（文件内、音频内）满足 `musicTime = chartTime + delayMs`，`delayMs` 由 `# delay: <ms>` 头声明（`ChartWriter.cs:49`）。解析时**仅当存在显式 `# delay` 头**才按音乐时间轴反算（`ChartParser.cs:505`、`:584-595`）；否则走遗留探测：紧跟 `chart(...)` 之后的 `track(x,1.00)` 被当作 delay 并跳过该行（`:529-582`）。网格线始终用谱面时间（`FalsumBeatGrid.cs:172-173`）。

**undo/redo 事务**（`FalsumChartSession.cs`）：`Snapshot` 完整拷贝 `Bpm/Division/SongId/Difficulty/DelayMs/Events/Notes` + `ExternalState` 字符串（`:11-23`、`:310-341`）；`BeginChange` 只允许一个挂起快照（重入直接 return，`:87-95`）；`CommitChange` 前先 `FalsumSkyAreaGrouping.Normalize`，再用 `Signature`（`R` 格式全字段拼串，`:480-522`）比对，无差异则不压栈（`:97-137`）；栈上限 **100**，超出丢最旧（`:121-124`）；`Undo/Redo` 把当前状态 `DetachCurrent()` 反压入另一栈（`:151-201`）；`ApplyBatch` 供工具脚本用，挂起时抛异常（`:252-292`）；外部状态由编辑器注入 `CaptureMetadataHistoryState`（`FalsumChartEditor.cs:4664-4723`），撤销时反向 `Restore`（`:4686-4692`）。

## 3. 控制流

1. 启动：`GameplayBootstrap.Start`（`Runtime/Bootstrap/GameplayBootstrap.cs:251`）→ `LoadChart()`（`:1014`）→ `ChartParser.ParseFile`（`:1029`）→ `FalsumSkyAreaGrouping.Normalize`（`:1032`）→ 音频/时钟/渲染器就绪 → `AddComponent<FalsumChartEditor>()` + `Initialize(this)`（`:322-323`）。
2. 编辑器打开：`FalsumChartEditor.Initialize`（`FalsumChartEditor.cs:247`）——清空宿主谱面（`:260`，可用环境变量 `INFALSUS_CAPTURE_PRESERVE_CHART` 保留，`:255-258`）→ 建 `FalsumBeatGrid`（`:262`）→ `new FalsumChartSession(_chart, DelayMs)` 并挂 `Changed`（`:267-268`）→ `FalsumToolManager.Initialize`（`:269-270`）→ `FalsumPreferencesStore.Load`（`:271`）→ `new FalsumInputRouter` + `ApplyPreferencesToRuntime`（`:272-273`、`:7383`）→ `FalsumEditorView.Build`（`:279-280`）→ `FalsumEditorWorld`（`:293-305`）→ `WireView`（`:4417`）→ `InitializeManifest`（`:4633`，实际写死 `"a la mode"/"aran"`，`:4640-4646`）→ `_host.SetEditorMode(true)`（`:318`）。
3. 主循环：`Update`（`:3936`）→ UI `Tick`（`:3946`）→ 预览/径向菜单/指针（`:3963-4013`）→ `HandleKeyboard`（`:4027`）+ `HandleMouse`（`:4032`）→ `UpdateViews` 与 `TickAutoBackup`（`:4036-4037`）。
4. 输入：`FalsumInputRouter` 只做 `动作→KeyCode+修饰键` 查表（`FalsumInputRouter.cs:15-55`，`Apply` 来自偏好 `:15-32`）；`HandleKeyboard` 逐动作分派（`FalsumChartEditor.cs:7729-7837`，含保存 `:7769`、撤销 `:7869`）；鼠标入口 `HandleMouse`（`:7947`）→ 中键 `BeginMiddleEdit/UpdateMiddleEdit/CommitMiddleEdit`（`:8004-8009`、`FalsumChartEditor.MiddleEdit.cs:1585`/`2214`/`2884`）→ 左键 `HandleLeftDown` 按 `EditorMode` 分发（`FalsumChartEditor.cs:8022-8058`）。
5. 编辑：`PlaceTap`（`:8576`）、`HandleHoldDown`（`:8604`）、`HandleFlickDown`/`CommitFlick`（`:8648`/`:8676`）、`HandleSkyDown`/`FinalizeSkyEnd`（`:8743`/`:8856`，`groupId=NextSkyGroupId()` `:8887`）；通用收尾 `FinishEditAdded`（`:8911`，负音乐时间先 `CancelChange`）→ `_session.CommitChange()` + `NotifyChanged()` + `RefreshEditorVisuals()`（`:8960`）；属性面板 `SetProperty`（`:10212`）；工具：`OpenTool`（`:6108`）→ `RunTool`（`:6218`）→ `Validate`/`Run`（`FalsumToolRuntime.cs:207`/`:300`）→ `ExecuteToolOutput` 用 `_session.ApplyBatch`（`:6324-6329`）→ 待确认写盘 `ExecutePendingToolRun`（`:6294`）。
6. 保存：`SavePackageInternal`（`:5599`）→ `ChartWriter.Write(_session.Chart, _session.DelayMs, SongId, difficulty)`（`:5625`）→ `UpdateDifficultyAvailability`（`:5670`，空谱面难度自动置 `available=false`）→ `_package.SetChartText`（`:5634`）→ `_package.Save`（`:5643`）→ `FalsumPackage.SaveTo`（`FalsumPackage.cs:448`）；备份在 `TickAutoBackup`（`:5733`）里预生成文本后丢给 `Task.Run`（`:5768-5780`）。

## 4. 易破且昂贵的不变量

1. **skyarea groupId 规则**：`Normalize` 中 `nextGroupId` 对**每个非 skyarea note 也自增**（`FalsumSkyAreaGrouping.cs:76`），只有“上一段 `EndTimeMs` 与本段 `TimeMs` 差 ≤ **2.0ms**”才继承组号（`:9`、`:99-101`、`:80-89`）。因此组号非连续、非密集，任何“按数组下标算组号”的写法都会错。
2. **Normalize 时机**：会话 `CommitChange`（`FalsumChartSession.cs:108`）、`Replace`（`:69`）、`NotifyChanged`（`:296`）、写谱 `CreateCanonicalChart`（`ChartWriter.cs:147`）都会重排组号。缺任一处会写出与界面不一致的 `groupId`。
3. **输出顺序即物理顺序**：写谱前按 `TimeMs→Kind→StartLane→Id` 排序（`FalsumSkyAreaGrouping.cs:117-135`、`ChartWriter.cs:111`），并重置 `SourceOrder`（`ChartWriter.cs:141-146`）。乱序会让解析回来的 `Id` 变，粘贴/撤销中的引用失效。
4. **flick 方向只能 4/16**：解析强校验（`ChartParser.cs:358-364`）、`ChartNoteValidator` 强校验（`ChartModels.cs:168-174`）；镜像与“交叉手柄”都把 16↔4 翻转（`FalsumChartEditor.cs:9814-9815`、`FalsumChartEditor.MiddleEdit.cs:2605-2613`）。写成 4/16 之外的值会导致包直接拒载。
5. **delay/bpm 派生行**：`|delay|>0.0001` 时必须先写 `track(delay,1.00)` 再写 `bpm(delay,bpm,division)`（`ChartWriter.cs:55-64`），且谱面时间 0 处与基准 bpm/division 完全相同的 `bpm` 事件必须跳过（`:75-84`）。漏 skip 会重复定义基准导致回读漂移；漏 `track(delay,1.00)` 会让遗留解析器判不出 delay。
6. **事件时间写的是音乐时间**：`track/bpm/lane` 与所有 note 都经 `MusicTime(chartTime,delay)`（`ChartWriter.cs:292-295`、`:89/:96/:104/:193/:201/:231/:279`）。直接把谱面时间写进文件等于整体错位 `delay`。
7. **负音乐时间禁止**：`ContainsNegativeMusicTime` 在写谱抛异常（`ChartWriter.cs:33-38`）、`IsNegativeMusicTime` 容差 0.0000005ms（`ChartModels.cs:570`、`:593-599`）；编辑侧 `RejectNegativeMusicTime`（`FalsumChartEditor.cs:12318`）、改 delay 时整谱预检（`:10829-10836`）。放宽 tolerance 等于允许负时间进文件。
8. **flick/skyarea 分母两套上限**：保存量化上限 `FalsumFraction.MaxDenominator=200`（`FalsumFraction.cs:8`），交互网格上限 `MaxEditableFlickGrid=192`（`FalsumChartEditor.cs:72`，注释 `:71` 明说“interactive only”）。>192 的网格无法在编辑器中表示。
9. **写谱量化方向**：flick 用 `Math.Round(..., AwayFromZero)` 且宽度下限 0（`ChartWriter.cs:221-228`），skyarea 宽度下限 1.0（`:264-276`），hold duration 直接透传（`:205`）。改成银行家舍入会与编辑器拾取判定的整数化不一致（`IntegerTimeMs` `FalsumChartEditor.cs:11450`）。
10. **播放速率保音高**：速率集合固定 `{1.0,0.75,0.5,0.25}`（`FalsumChartEditor.cs:63-69`）；有 sink 时 `AudioSource.pitch` 恒为 1（`Core/Timing/GameplayClock.cs:143-145`、`:259-261`），由 `FalsumPitchPreservingPlayback` 用 4096 帧汉宁窗颗粒、hop 2048 重采样（`FalsumPitchPreservingPlayback.cs:20-21`、`:169-236`），`rate>=0.9999` 才走直通（`:171-175`、`:238-251`）。绕过 sink 直接改 `pitch` 会双变速。
11. **备份命名/定位**：根目录 `<程序目录>/autobackups`（`FalsumBackupService.cs:29`），二级 `songId-<路径哈希>/d<难度>`，哈希为“全路径大写后 SHA256 前 6 字节”（`:204-243`），文件名 `yyyyMMdd-HHmmss-fff.chart.txt`（`:73-77`），保留条数 `Math.Max(1,retention)`（`:181`）。改哈希算法或大小写处理 → 旧备份“消失”。
12. **波形只在内存**：`FalsumTrackSpectrumCache` 用 BASS 解码并按原版 `showwavespic` 语义（幅度翻倍 / 列多吃一帧 / int16 回绕 / 每声道 127）栅格化成灰度字节，主线程建 `Texture2D` 后进 `FalsumTrackWaveformCache`（容量 2 片、LRU、约 32 MB），**不再落盘**；`FalsumCacheStore.DeleteWaveformCacheFiles()` 在 `Initialize` 里清掉历史 `waveform-v*` 残留。切片 16 s / 步长 10 s / 960 px/s / 高 1024 仍决定纹理与着色器采样，改常量要同步 `FalsumSelfCheck.CheckTrackSpectrum`；栅格化那几条反直觉细节由 `CheckTrackWaveformRasteriser` 钉住，见 `02-invariants.md` §A-12。转换临时目录 `persistentDataPath/FalsumConversion`（`FalsumMediaConverter.cs`）；偏好 `persistentDataPath/Falsum/preferences.json`（`FalsumPreferencesStore.cs:12-30`）。
13. **包保存的原子性与不可变副作用**：`.tmp-<guid>` 写完再 copy/move（`FalsumPackage.cs:500-602`）；`song.json` 由**克隆**清单序列化，`available` 降级只改克隆（`:486-498`），成功后才回写真实难度（`:607-616`）。直接改 `Manifest` 再写会污染内存态。
14. **元数据历史键用引用哈希**：`CreateHistoryKey` 以 `RuntimeHelpers.GetHashCode(AudioBytes/CoverBytes)` 参与拼键（`FalsumMetadataApplyService.cs:37-44`），快照上限 512（`FalsumChartEditor.cs:84`）。同数组换内容不换对象 → 键不变，快照被覆盖。
15. **工具沙箱上限**：Jint `LimitRecursion(128)`/`MaxStatements(1e6)`/`LimitMemory(64MB)`/`Timeout 5s`/禁字符串编译/关 Interop（`FalsumToolRuntime.cs:130-137`）；单文件 64MB、累计写 128MB（`:42-43`）；权限白名单只有 `files.read`/`files.write`（`FalsumToolModels.cs:162-176`）；工具启用键 `Falsum.Tool.<id>.Enabled`（`FalsumToolManager.cs:188-191`）。任一放宽都是安全面扩大。
16. **pending 编辑的唯一性**：`BeginChange` 重入被静默忽略（`FalsumChartSession.cs:87-95`），`ApplyBatch` 在 pending 时抛（`:261-265`）。嵌套 `BeginChange` 会吞掉内层起点，撤销一步跨两个操作。

## 5. 已知粗糙处（均可从代码证实）

- **零分节**：12565 行的 `FalsumChartEditor.cs` 里**没有任何 `#region`**，全文仅 3 处 `//` 注释（`:71`、`:9872`、`:11080`），只能靠方法名导航（公开成员约 526 个）。
- **测试代码寄生在编辑器里**：`public` 的 `Debug*` 成员 128 个；`FalsumChartEditor.MiddleEdit.cs:133-1413` 单个 `DebugRunMiddleEditChecks` 占 1280 行（约占该文件 40%），`DebugRunEditingChecks` 占 `FalsumChartEditor.cs:3039-3829`（790 行）。
- **`CloneNote` 五份副本**：`ChartWriter.cs:151`、`FalsumChartSession.cs:443`、`FalsumChartEditor.cs:12424`、`FalsumToolRuntime.cs:1482`。且编辑器版**丢** `Consumed/Judged/Holding/Result/HoldStartResult`（`FalsumChartEditor.cs:12427-12445`），会话版保留（`FalsumChartSession.cs:464-468`）——复制/粘贴与撤销后的判定态语义不一致。
- **死代码**：`ChartParser.IsOutsideSkyBounds`（`:774-784`）全仓无调用点。
- **单一几何实现**：`FalsumEditorWorld`（3020 行）是唯一的网格构建/拾取/反投影实现（原来的 `Falsum2DWorld` 已于 2026-11-12 随 2D 模式删除）。
- **双重遗留兼容**：`ChartParser` 同时保留 `bpm` 第 4 参数（`legacy bpm flag must be 1`，`:211-216`）与 `track` 式 delay 标记探测（`:529-582`），共 3 条解析路径。
- **三套“最短时长”**：`MiddleMinimumDurationMs = 1.0`（`FalsumChartEditor.MiddleEdit.cs:83`）、属性面板 `Math.Max(1.0, …)`（`FalsumChartEditor.cs:10251-10253`）、天空区落点 `Math.Max(10.0, …)`（`:8876-8878`）。
- **硬编码占位**：`InitializeManifest` 固定标题 `"a la mode"` / 作者 `"aran"`（`FalsumChartEditor.cs:4644-4645`）；幽灵预览 `GroupId = 900000`（`FalsumEditorWorld.cs:2292`）。
- **全仓无 TODO/FIXME/HACK**（grep 已验证：`Falsum/` 与 `Core/Chart/` 零命中），技术债是结构性的、未标注。
- **平台分支内嵌 P/Invoke**：`FalsumFileDialog.cs` 三段 `#if`（`:12/:65/:126`，`OPENFILENAME` 结构体 `:226`），macOS/Linux 走 `#else` 简化实现，对话框行为不可跨平台对齐。

---

**给接手者的最短路径**：先读 `FalsumChartSession.cs`（事务语义）与 `ChartWriter.cs`（谱面真值），再读 `FalsumChartEditor.cs:247-329`（组装）与 `:3936-4038`（帧循环），最后按 `EditorMode`（`:25-36`）沿 `HandleLeftDown`（`:8022`）逐模式追放置逻辑；`FalsumSkyAreaGrouping.cs:29-93` 与 `ChartModels.cs:568-599` 是任何改动都最容易踩雷的两处。
