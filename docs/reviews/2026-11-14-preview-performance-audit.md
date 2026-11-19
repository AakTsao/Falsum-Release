# 165 Hz 预览性能审查

日期：2026-11-14。范围：当前源码、正在运行的 Windows 包的启动日志，以及隔离工程中的 CPU 微基准。本轮未修改产品代码，也未重建或替换正在运行的程序。

结论：没有找到 60/120/144 FPS 的代码限帧，但存在明确的重复 CPU 工作与渲染提交开销。不能仅凭显卡利用率低归因于 DX11。当前运行包还明显早于源码，因此它不能用来验证最近的优化。稳定 165 FPS 的每帧预算约为 6.06 ms；本次微基准没有测量玩家程序的完整 CPU/GPU 帧耗时。

## 已确认的运行包与设置问题

- 正在运行的是 `Builds/FalsumEditor-0.46-Canary/FalsumEditor.exe`，它与 `FalsumEditor_Data/Managed/Assembly-CSharp.dll` 的修改时间均为 2026-11-10 00:12:32。当前 `NoteRenderer.cs` 修改于 11-14，后续源码优化没有进入这个包。
- 实际 `Player.log` 显示 `kGfxThreadingModeClientWorkerJobs`、Direct3D 11.0、NVIDIA GeForce RTX 5060 Laptop GPU。当前程序已经使用独显及图形工作线程，不能把本次现象解释为代码强制单线程图形或选错集显。
- 启动日志第 29 行明确出现 `Preferences could not be loaded: Unsupported preferences version: 10`。`FalsumPreferencesStore.LoadFromDisk` 遇到不支持的版本后保存备份并返回默认配置。此结论针对这次启动；审查时用户已经再次保存了版本 8 的配置，不代表所有后续设置修改都失败。
- 刷新率选项只调用 `Screen.SetResolution`；产品提供的是窗口/无边框全屏，没有独占全屏。Windows 桌面刷新率与 Unity 实际帧率仍需分别确认。该数值不会自动设置 `Application.targetFrameRate`。更改刷新率而实际显示节奏不变，在目前实现下是可能的。

启动日志位置：`C:/Users/AakTsao/AppData/LocalLow/Team AakTsao/Falsum Editor Preview/Player.log`。

## 当前源码的主要嫌疑

### 1. SkyArea 每帧重建全部剩余面片，并上传完整网格

`NoteRenderer.cs:2257` 开始清空顶点、五组 UV 和三角形列表；`:2288` 以 5 ms 计算细分；`:2311` 遍历剩余细分；`:2336` 到 `:2343` 每帧重新写入 Mesh。`EvaluateSkyAreaRow` 把轨道之外的 z 夹在轨道末端，但外层仍对这些行采样和追加面片，产生大量末端退化三角形。

用现有公式检验一个 30 秒、流速 5、无变速的 SkyArea，在其开始时间：总计 6001 个面片，其中 5867 个两端都超出轨道，被夹在同一末端位置；约 97.8% 的面片没有纵向面积，但仍经历生成与上传。这是受控合成例，不代表某个官方谱面的实际面片数量。

这是优先检查的 CPU、内存传输及几何开销。优化必须保留 5 ms 外观采样、连续链单 mesh、共边、反向/停走以及 seek 语义；不能用缩短可见时间窗口或降低采样精度掩盖问题。

### 2. SkyArea 的固定段落信息重复计算，并扰动播放头缓存

`AppendSkyAreaQuad` 在 `NoteRenderer.cs:2495` 为每个面片重算相同 metadata，并调用 `TrackTravelAt(segment.TimeMs)`。`TrackMotion.cs:45` 的单项缓存原本用于播放头 now；`:90` 的 `TrackTravelAt` 复用同一缓存。于是行采样写入 now，metadata 又写入 segment.TimeMs，下一行再重新求 now。

模拟现有调用顺序、256 个变速事件及 1001 行时：固定 metadata 留在循环内约 0.0210 ms，改为每段求一次约 0.0091 ms。只证明这一小段存在重复开销，不代表整帧或整个 SkyArea 可以获得相同倍数的加速。

### 3. 音符逐 Renderer 更新 MaterialPropertyBlock，批处理收益受限

`NoteRenderer.cs:2729/2768/2805` 及相邻分支对可见 HOLD/FLICK/TAP 的 Renderer 每帧读取、修改、写回属性块。多个材质属性还每帧调用 `sharedMaterial.HasProperty`，其中 Flick 颜色和条纹密度多数时候不变。

URP 资产虽然启用了 SRP Batcher，但使用 MaterialPropertyBlock 的 Renderer 不能走其常规批处理路径；抽查 Flick/SkyArea 材质的 instancing 标志为 0。因此密集音符可能形成 CPU 提交瓶颈，表现为 GPU 利用率不高。实际 Draw Calls 与 Render Thread 耗时尚未实测，不能给出确定影响百分比。

### 4. 后处理有固定成本，抗锯齿开关不会关闭它们

- `FalsumTrackEndComposerFeature.cs:38` 每帧绘制遮罩并执行 7 次 blur blit。动态背景确实需要更新，但当前没有按变化状态或预览性能档位缩减这条链。
- `SkyJudgementLineEffectFeature` 活跃时额外绘制遮罩并执行 7 次 blit。它已有 `ShouldRender` 门控，静止且已淡出时不执行；不能把它说成永久运行。
- `PC_Renderer.asset:72` 的 SSAO 处于启用状态，`:75` 的 Downsample 为 0；PC URP 同时请求 depth 与 opaque texture。实际开销依赖相机、材质和 GPU，关闭 MSAA 不会自动去掉这些遍次。

### 5. 网格生成会从谱面起点反复遍历，主要影响编辑界面播放

`FalsumBeatGrid.cs:325` 从 segmentStart=0 开始；`:350` 从每段起点按步长遍历，`:356` 仅用 continue 跳过窗口前的时间。请求的只是当前窗口，工作量却会随播放位置和细分增大。`FalsumEditorWorld.cs:1984` 每次刷新都会重建网格。

受控 180 BPM、6 秒窗口：4 等分从 1 秒位置约 0.0067 ms，15 分钟位置约 0.0739 ms；64 等分约 0.1268 → 0.8046 ms。普通官方谱面默认细分的开销较小，高细分长谱更值得修。

`EnterChartPreviewMode` 在 `FalsumChartEditor.cs:4254` 主动关闭网格，因此这一条不能直接解释进入纯谱面预览后的掉帧。`LateUpdate` 仍调用 overlay 调度，但空 mesh 已有不重复上传的分支，不能把所有空 overlay 都计成上传开销。

## 已排除或应降低优先级的因素

- `QualitySettings.asset` 两档 `vSyncCount` 均为 0；源码未发现 `Application.targetFrameRate`、`OnDemandRendering.renderFrameInterval`、运行时主动 sleep/wait 的限帧逻辑。
- `FalsumPerformanceMonitor.TargetFps` 是性能窗口的预算标线，默认 60、最高选择 144；它不设置真实帧率。但是“流畅”的判断预算不足以验收 165 FPS，应单独观察实际帧耗时。
- 时间文字的 30 Hz 更新只针对 UI 数字；音符表现按每帧播放头刷新，不是 30 Hz 的世界更新。
- 判定、自动播放、Combo、可见实例检查都扫描全谱；但当前 `FindVisual` 已是 Dictionary 查询，不是线性查找。Tokonagi 难度 3 的 1783 个音符微基准：90% 位置的 Combo 约 0.0687 ms、判定 Update 约 0.0078 ms、默认网格 Build 约 0.0140 ms。这些单项不足以解释明显的低帧率；密集击打触发的候选搜索、音符更新、Draw Call 和完整 GPU 帧并未包含在该测试中。
- 判定/自动播放不得采用已判定前缀游标跳过扫描，这是项目 A-22 的既有回归约束；建议缓存固定计算或建立可正确失效的空间/时间索引，并先证明语义等价。

## 建议顺序与证据

先使用当前源码的新包复测，确认配置不再回落；分别记录实际 frame time、CPU main/render、GPU time 和 Present 等待。然后优先测 SkyArea 网格更新及退化面片、逐音符属性块和 Draw Call；再比较 TrackEnd/SSAO 的 GPU 时间。默认网格/Combo/判定全表扫描放在后面。

若 CPU 工作接近或超过 6.06 ms、GPU 时间较短，低 GPU 利用率符合 CPU 瓶颈；若两者都明显短于帧间隔，才继续排查桌面刷新率、驱动限帧、VSync/VRR 和 Present 节奏。源码本身无法排除驱动或显示链路限制。

CPU 微基准用 Unity 6000.3.26f1 在临时隔离工程、batchmode/nographics 下运行，机器为 Ryzen 9 8945HX。没有改变正在运行程序的设置、场景或文件。输出：`Diagnostics/PerformanceAudit/CpuMicrobenchmarks.json`，复现探针：`CpuMicrobenchmarkProbe.cs.txt`，日志：`UnityCpuMicrobenchmarks.log`。
