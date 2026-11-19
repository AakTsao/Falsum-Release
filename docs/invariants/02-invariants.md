# 02 · 不变量（改之前必读）

每条都是**踩过的坑**，不是风格建议。破一条的代价通常是「保存后包打不开」「画面静默错位」
「撤销跳步」这类难以复现的事故。编号 `A-*` 数据/编辑器，`B-*` 渲染/表现，`C-*` UI/本地化，
`D-*` 工程与流程。

---

## A. 数据与编辑器

### A-26 Android URI 与本地文件路径分离，保存完成必须等外部流关闭（2026-11-16）

Android 系统选择返回的是文档 URI。不要传给 `File.*`、`Path.GetFullPath` 或包容器；
由 `FalsumAndroidFileBridge` 处理流，本地 `SourcePath` 固定为
`Falsum/Documents/<URI SHA-256>/document.falsum`，显示文件名另存，改名不能改变备份身份。
保存先生成独立包快照和完整候选文件，外部写入真实成功后才提交路径、清单、
`_isDirty=false` 或执行打开/退出的后续动作。取消、失败、过期回调都不能冒充保存成功。
素材导入不保留持久 URI 权限；天空区和工具输出也必须等待真实导出结果。
文件门禁 `FalsumFileAccessSelfCheck` 已加入完整自检。

### A-27 BASS 回调驱动必须与混音流同采样率（2026-11-16）

`FalsumBassAudioEngine` 使用独立的双声道静音流式 AudioClip 驱动回调，频率与
`_sampleRate` 一致；长度仍取实际 BGM 时长，供 `GameplayClock` 的时长和 seek 使用。
不要重新把歌曲 Clip 挂作 BASS 驱动，否则 Unity 的源格式转换可能使音频变慢、降调，
并重复解码歌曲。`CheckBassCallbackSampleRates` 用 44.1/48 kHz 的真实 1 kHz PCM 音调
经过 BASS 混音验证输出频率、驱动采样率与时长。

### A-1 `skyarea` 的 `groupId` 是算出来的，不是数组下标
`FalsumSkyAreaGrouping.Normalize` 里 `nextGroupId` **对每个非 skyarea 音符也自增**，
只有「上一段 `EndTimeMs` 与本段 `TimeMs` 差 ≤ **2.0 ms**」才继承组号。
→ 组号稀疏、不连续。任何「用数组下标当 groupId」的写法都会写出错误的链路。
`F/Core/FalsumSkyAreaGrouping.cs`（`TimeToleranceMs = 2.0`，`Normalize`）。

�༭�ύ�����֪ͨ��д��ʹ�� `Normalize(..., useOutputOrder: true)`����
`TimeMs �� Kind �� StartLane �� Id` ����ʱ�б����򣻵��뱣������ `SourceOrder` ���ļ�˳��
ȱʧ���ظ� `SourceOrder` ʱҲ���밴���˳����ˣ���������׷��˳��
��һ����������ԭ�����б����д `SourceOrder`���·��������� `Id` �������ύǰȷ����
����ͬʱ���֧���ڱ༭�뱣��֮�任������2026-11-18��latentduality FBD 67412 ms����

### A-2 `Normalize` 有四个必须调用的时机
`CommitChange`、`Replace`、`NotifyChanged`、`ChartWriter.CreateCanonicalChart`。
缺任一处 → 写出的文件 groupId 与界面不一致。
`FalsumChartSession.cs`、`ChartWriter.cs`。

### A-3 输出顺序决定回读后的 `Id`
写谱前必须按 `TimeMs → Kind → StartLane → Id` 排序并重置 `SourceOrder`。
乱序 → 解析回来 `Id` 变了 → 剪贴板/撤销里保存的引用失效。
`FalsumSkyAreaGrouping.CompareNotesForOutput`、`ChartWriter.cs:111`。

### A-4 `flick` 方向只能是 4 或 16
`4 = 绿/右`，`16 = 黄/左`。解析强校验、`ChartNoteValidator` 强校验、镜像时 16↔4 翻转。
写别的值 → 包直接拒载。

### A-5 带 delay 的写谱派生行
`|delay| > 0.0001` 时必须**先写 `track(delay,1.00)`，再写 `bpm(delay,bpm,division)`**，
且跳过「谱面时间 0 处、与基准 bpm/division 完全相同」的那个 `bpm` 事件。
漏 skip → 每存一次多一条重复事件；漏 `track(delay,1.00)` → 遗留解析器判不出 delay。
`ChartWriter.cs:55-84`。

### A-6 事件与音符写进文件的都是**音乐时间**
`track` / `bpm` / `lane` / 所有 note 都经 `MusicTime(chartTime, delay)`。
直接把谱面时间写进文件 = 整体错位一个 delay。

### A-7 负音乐时间禁止
写谱抛异常；编辑侧 `RejectNegativeMusicTime`；改 delay 时对整谱预检。
容差 `0.0000005 ms`（`ChartModels.cs` `ChartTimeAxis`）。

### A-8 两套分母上限，别混
- 存档量化上限：`FalsumFraction.MaxDenominator = 200`
- 交互网格上限：`FalsumBeatGrid.MaxGridDenominator = 192`
  （仅交互用；>192 的网格无法在编辑器里表示）。
  **`MaxEditableFlickGrid` 现在只是它的别名**，不要再写 `192` 字面量。

### A-24 GRID 分母序列是唯一阶梯，`-`/`=` 的所有消费者共享它（2026-11-08）
`FalsumGridPreferences.DenominatorSequence`（默认 `12, 24, 36, 48, 50, 72, 96, 100`）
是**唯一**的分母阶梯，保存在文本里（与 `LineColors` 同构：解析 → 失败则回落默认 →
永远重写成规范形式）。

- 三个 `-`/`=` 消费者（Flick/SkyArea 放置、中键快捷编辑、锚点横向拖动）**必须**
  走 `FalsumBeatGrid.StepDenominator(sequence, current, direction)`，不许各自写阶梯。
  曾经是两套：Flick 用硬编码 `12→24→36→48`，中键用 2 的幂倍增 —— 换分母时两边对不上。
- `StepDenominator` 语义：**严格相邻**（不返回等于 current 的项，否则连按不前进）；
  到两端**夹住不回绕**；`current` 不在序列里（例如 1/200 谱面的 200）时
  按方向落到最近的成员（200 向下 → 100）。
- 尺子刻度与信息框格号必须来自**同一个 `divisions`**，否则刻度与 `起始/终止位置`
  会算不到一起。刻度的三档是**整数可整除**判定（1/4 处高、1/8 处中，其余短），
  不用浮点容差：50/100 这类八等分不整除的分母会自然退化成两档，而不是间距不均。
- `FalsumSelfCheck.CheckDenominatorSequence` 钉死默认序列文本、往返、四类拒绝、
  两端夹取与非成员吸附。

### A-9 写谱量化方向固定 `AwayFromZero`
flick 宽度下限 0，skyarea 宽度下限 1.0，hold duration 直接透传。
改成银行家舍入 → 与编辑器拾取的整数化不一致。

### A-10 播放速率集合固定 `{1.0, 0.75, 0.5, 0.25}`，且保音高
有音频 sink 时 `AudioSource.pitch` 恒为 1，由 `FalsumPitchPreservingPlayback`
用 4096 帧颗粒 / hop 2048 做变速率重采样；`rate >= 0.9999` 才走直通。
绕过 sink 直接改 `pitch` → 双变速。

### A-11 备份目录布局即产品行为
`<程序目录>/autobackups/<songId>-<路径哈希>/d<难度>/yyyyMMdd-HHmmss-fff.chart.txt`
哈希 = 全路径大写后 SHA-256 前 6 字节。改哈希算法或大小写处理 → 旧备份「消失」。

### A-12 波形切片只存在于内存，且由 BASS 在进程内栅格化
轨道波形由 `FalsumTrackSpectrumCache` 用 BASS 解码后栅格化成灰度字节，交给
`FalsumTrackWaveformCache`（容量 2 片，LRU 淘汰，上限约 32 MB），**不写磁盘**，
`FalsumCache/` 里只留音频 ogg。切片 16 s / 步长 10 s、960 px/s、高度 1024 这些常量
仍决定纹理尺寸与着色器采样，改动会让 `FalsumSelfCheck.CheckTrackSpectrum` 与
`FalsumTrackSpectrum.shader` 对不上。波形默认关闭，关闭时不启动任何生成任务。

**这三个反直觉的细节是原版外观的一部分，不许「化简」**（移植自随包 ffmpeg
`N-92722-gf22fcd4483` 的 n4.0.2 `avf_showwaves.c`，已逐像素验证 0 差）：

1. `sum[ch] += abs(s16) << 1` —— 幅度翻倍但仍除以 `max_samples`；
2. `if (n++ == max_samples)` —— 一列吃到第 `max_samples+1` 帧才收，却除以
   `max_samples`，所以填满的列数是 `frames / (max_samples+1)`，**通常是 width-1 或
   width-2，尾部列永远是黑的**；
3. `(short)(sum / max_samples)` —— 商在 int16 处**回绕**，响的段落反而画得更矮；
   灰度路径每声道加 `255/声道数`（立体声 127），两声道重叠即 254，不是 255。

`FalsumSelfCheck.CheckTrackWaveformRasteriser` 用 12 帧小样本把这三条钉死。

### A-13 包保存的原子性与副作用隔离
先写 `.tmp-<guid>` 再 copy/move；`song.json` 由**克隆**的清单序列化，
`available` 降级只改克隆，成功后才回写真实 Manifest。直接改 Manifest 再写会污染内存态。

### A-14 编辑事务只能有一个挂起快照
`BeginChange` 重入被静默忽略；`ApplyBatch` 在有挂起事务时抛异常。
嵌套 `BeginChange` 会吞掉内层起点 → 一次撤销跨两个操作。

### A-15 工具沙箱上限
递归 128 / 语句 1e6 / 内存 64 MB / 超时 5 s / 禁字符串编译 / 关 Interop；
权限白名单只有 `files.read`、`files.write`。放宽任何一条都是安全面扩大。

### A-16 `ChartTimeAxis` 的容差与符号不能改
`ToChartTime` / `ToMusicTime` 是**唯一**换算入口；别的地方手写 `± delay` 一定会漏。

### A-17 放置 TAP 前必须过叠键检查（±9 ms + 轨道区间相交）
同一轨道区间上，若已有 TAP 与新 TAP 的谱面时间差 **≤ 9 ms（含边界，容差 1e-9）**，
放置必须被拒绝：两条 TAP 会落进同一个判定窗口，一次击打被吞掉。
`FalsumTapPlacementRule`（`MinGapMs = 9.0`，`IsBlocked` / `TryFindBlocker`）是唯一判定入口，
调用点在 `FalsumChartEditor.PlaceTap()`（左键放置与「点立得 TAP」都走这里）。
- 轨道口径是**区间相交**（`[StartLane, StartLane+WidthLanes)`），不是「起始轨相同」：
  宽 TAP 覆盖到相邻轨同样算冲突，与 `design-rule-checker` 的 `lanesOverlap` 一致。
- 拦截必须发生在 `_session.BeginChange` **之前**，否则被拒的一次点击会留下一条空的撤销记录。
- 只有放置路径拦：粘贴、拖拽、改属性不拦（Paste 与拖动是用户对既有谱面的显式改写）。
- 失败反馈走 `ShowHint` + `falsum.hint.tap.tooClose`（不新增硬编码文案）。
  **文案报的是实际时间差 Δ（`{delta}`，按 0.1 ms 取整），不是窗口宽度 9**：
  两条差 2 ms 的 TAP 就是 2 ms 重叠，作者要靠这个数字判断该挪多远。
  `TryFindBlocker` 返回的 `gapMs` 就是为此存在的——不要删成 `IsBlocked` 单值返回。
`FalsumSelfCheck.CheckTapPlacementGuard` 覆盖 9/9.001 ms 边界、宽 TAP 相交、异轨放行、
以及 HOLD/FLICK/SKYAREA 不参与判定。（2026-11-04。）

### A-18 锚点层只能委托中键精修，不得自研夹取/吸附/边界数学
`FalsumChartEditor.AnchorEdit.cs` 里**每一个锚点角色只声明一个操作**，然后把
`MiddleEditState` 交给 `MiddleEdit.cs` 的 `ApplyBoundaryEdit` / `ApplyStartEndEdit` /
`ApplyPositionEdit`（内部各自落到 `ApplyGroundBoundary` / `ApplySkyBoundary` /
`SnapNormalizedDelta` / `ResolveMiddleEndpointGrid`）执行。锚点层自己不再有
`ClampGroundBoundary`、`AnchorMinDurationMs`、`Math.Round`、`Mathf.RoundToInt` 这类实现。
唯一例外是 `ApplyAnchorEndpointLateral`：skyarea 前/后边线中点被**横向**拖动时，
只写那一个端点的 `StartPos`/`EndPos` 并把该端点宽度原样回写（宽度不变），
因为中键的三个模式里确实没有对应物。
→ 两套独立实现必然漂移：曾经锚点层那份把 Flick 边界拖成整体平移、Hold 能缩到 1 ms、
前后/左右由轮廓宽高比反推（时间锚点跑到左右边）。一份实现才不会再分歧。
`F/C/Runtime/FalsumChartEditor.AnchorEdit.cs`、`FalsumChartEditor.MiddleEdit.cs`。
**不要**在锚点层加第二个 `ApplyXxxAnchorLateral` 式的本地数学；要改语义先改中键那份。
`FalsumSelfCheck.CheckSelectionAnchors` 用反射断言旧方法已消失、新委托存在；
`Tools/anchor_offline_check.py` 在离线门里复刻同一组断言，并额外断言锚点层没有
`Math.Round` / `Mathf.RoundToInt` / 本地 `ClampGroundBoundary`。（2026-11-05。）

### A-19 锚点可见性按**单个端点 + 屏幕空间**判定，不是按音符整体（2026-11-06）
`hasFront`/`hasBack` 在 `FalsumEditorWorld.ResolveBoxCorners` 里由
`IsEndpointOnScreen(note, tail, nowMs)` 决定：该端点的投影是否落在屏幕矩形内（2 px 容差）。

- **只让那一个端点的「角点对 + 时间中点」消失**。同音符的另一个端点照常显示；
  缓动锚点与移动手柄与端点是否出屏无关，必须一直显示。
  曾经的写法是「任一端投影失败就 `return false`」，长 skyarea 只要一头出屏就
  一个手柄都不剩——这是「选中 skyarea 却完全没有锚点」的直接原因。
- **缓动锚点位置必须在音符的「进度参数」空间里解析求出**
  （`SkyAreaSideMidpoint`：先解出可见 z 区间 → 换算成 0..1 进度 → 取中点 →
  按 `SkyAreaGeometry.Ease` 求该侧坐标 → 投影）。
  **绝不可以**回去「取多边形外侧若干顶点求平均」（已删除的
  `LeftEdgeMidpoint` / `RightEdgeMidpoint` / `EdgeMidpointByExtreme`）：
  哪些顶点落进那个带宽会逐帧离散跳变，平均值就跟着**上下乱飘**；
  而且后退透视条带的 X 极值在**远端**，那个「边中点」根本不在侧边中部。
  **`ResolveVisibleProgressRange` 的三条硬约束**（`Tools/easing_anchor_continuity_check.py` 覆盖）：
  1. **连续**：音符匀速平移时中点不能有阶跃（曾被顶点带宽实现破坏）。
  2. **整段出屏时塌向「离轨道最近的那一端」**，不是固定 0.5。
     第一版数值实现返回 0.5，音符一离开视野锚点就瞬移到中间——这是本轮实测抓到的真 bug。
  3. **头尾互换是反对称**：progress p ↔ 1−p，两个中点之和为 1，**不是相等**。
     把「头尾对称」写成 `fwd == bwd` 是错的断言。
  判据是连续性：位置只能是相机与音符数据的连续函数。
- **不要**用轨道 z 判屏幕可见性（`IsEndpointVisible` 仍然存在，但只回答
  「端点是否越过轨道末端」）。z 在轨道内 ≠ 投影在屏幕内。
- 手柄位置必须**吸附到实际绘制的多边形顶点**（`MatchCornerToBox`）；解析投影只用来
  **认领角色**（哪个顶点是前左/后右）。先投影再夹进轮廓 3 px 会让所有手柄系统性
  内缩几个像素，就是「手柄总差一点对不上框」的残余偏移。
- 侧轨（lane 0 / 5）上的 TAP/HOLD，其横向极值必须走 `TryGetSidePoints` 的侧轨几何
  （`TryProjectGroundExtreme`），不能用地面的 lane 边界 X：那会把侧轨音符的手柄
  画到地轨平面上。
- **缓动锚点可拖动，且点击不丢失**：按下未越过吸附档位 → 点击循环
  （`Line→Ease out→Ease in→Line`）；越过档位 → 沿锚点自身轴按档位改值。
  三档几何**只有一份**：`FalsumSelectionAnchor.SnapCenter` / `SnapDirection` /
  `SnapSpacing` + `EasingSlot`，构建器用它绘制、拖动用它吸附。
  拖动若再算一遍间距，两套公式漂移后手柄会跳到它从未被画过的档位。
  **档位沿 `SnapDirection` 量，而 `SnapDirection` 指向「向内」**，所以档位符号
  就是手势本身：**向内拖 = `+1` = Ease out，向外拖 = `-1` = Ease in**。
  值↔档位**不是恒等映射**：值 `0/1/2`（Line/Ease out/Ease in）对应档位 `0/+1/-1`。
  逆映射必须用 `EasingSlotToValue`，**写成 `slot + 1` 会把 Ease out 变成 Line**。
  本轮先后踩了两个方向 bug，都由这条规则兜住：
  1. `slot + 1` 当逆映射 → 值整体错位一档（Unity 自检往返断言抓到）；
  2. `EasingSlot` 的符号取反 → **手柄画在一侧、拖动却改另一侧**（用户实测报回）。
     这一条编译器与文本 grep 都看不出来，所以 `Tools/anchor_offline_check.py` 第 20 组
     把「哪个分支返回 `+1`/`-1`」逐条解析断言，并做了变异测试：
     把两分支符号对调后闸门必须失败（已实测会失败）。
  拖动是**相对手势**：以按下时的 `PressEasingSlot` 为基准加偏移，
  不能把指针绝对位置当值；同一档位内用 `PressEasingSlots` 去重，点击时手抖不翻档。
- tap→hold 转换已于 2026-11-06 按用户要求删除：拖时长中点不再改写 `Kind`；
  TAP 属性面板没有 `end_time_ms` 行，且 `ApplyPropertyEdit` 对 `NoteKind.Tap`
  直接返回。不要再把「拖长变 HOLD」加回来。
### A-20 锚点的绘制中心与抓取中心必须一致，抓取半径必须按分辨率换算（2026-11-06）
`FalsumSelectionAnchor` 构造时把 `HitCenter` 设成传入点。**任何构造后又被移动**
（改 `CanvasPosition`）的锚点，必须同时重指 `HitCenter`；否则抓取区留在旧位置，
看起来能点的手柄其实点不到。缓动锚点就是典型：它被移到三档档位上，
`HitCenter` 留在 `edgeMid`，两者差一个 `SnapSpacing`（8–15 px），
**恰好把手柄推到自己的抓取半径之外**——用户报的「缓动点太难点了」就是这个。

- 抓取半径必须是**屏幕像素当量**，不能是固定的画布单位：
  `FindAnchorAt` 用 `max(anchor.HitRadius, ScreenRadiusToCanvas(px) * AnchorPickSafetyScale)`。
  旧写法 `max(fallback, HitRadius)` 里 `HitRadius`（20 画布单位）永远压过像素项，
  于是抓取区**随参考分辨率变化而实际变小**，在高分屏上小到被邻近音符的拾取区吃掉。
- **锚点必须赢过音符拾取**：`HandleLeftDown` 先试 `TryBeginAnchorDrag`，
  但它要求「恰好单选」。两者在屏幕上重叠（如 hold 边上的 tap）时，
  锚点这条路的半径必须足够大，否则点击落到音符上。
  `Tools/anchor_offline_check.py` 第 21 组装载真实的 `AddEasingAnchor` 方法体
  断言 `HitCenter` 被重指，并对所有 `CanvasPosition =` 赋值点检查同一 builder 内
  是否有 `HitCenter` 赋值。
（2026-11-06。）

### A-21 双指触控手势：四个动作固定，上下滑方向与滚轮相反（2026-11-06）
`FalsumChartEditor.TouchGestures.cs` 是**独立输入通道**，不复用鼠标路径：

| 手势 | 动作 |
|---|---|
| 双指左滑 | 撤销 |
| 双指右滑 | 重做 |
| 双指上滑 | 时间轴**向后** |
| 双指下滑 | 时间轴**向前** |

- **必须正好两指**（`Input.touchCount >= 2`），单指绝不触发这些动作：
  单指在 Unity 里会合成鼠标事件，用于常规编辑。
- 手势生效时**必须吞掉这一帧**（`IsTwoFingerGestureActive` 时不跑
  `HandleKeyboard`/`HandleMouse`），否则同一次双指滑动会同时被当成谱面拖拽。
- **换手即结束**：任一手指抬起就 `ResetTwoFingerGesture()`，
  不允许「抬一半继续滚」。
- 横向是**一次性**（`_twoFingerGestureFired` 闩住），否则一次滑动每帧都撤销一次。
- 纵向是**按总位移算已完成步数**（`_twoFingerScrollSteps`），
  不能把每帧增量累加——那会把同一段距离重复计费、越滚越快。
- **上下滑方向与鼠标滚轮相反**是有意的：触控用户的直觉是「拖内容」，
  下滑把更早的内容拉进来。按滚轮符号写（上滑=向前）实测被用户判为反向。
  闸门里对 `dy > 0 ? -1 : 1` 做断言并做过变异测试，**不要「修正」回去**。
（2026-11-06。）

### A-22 判定/自动播放**不得**用「已判定前缀游标」跳过扫描（2026-11-06）

座位上的教训：为省每帧扫描开销，曾在两处加了「跳过已判定前缀」的游标，
结果**音符打击特效与 Flick 特效全部消失**，而所有离线差分测试（含 141 张真实谱面）
都没能复现。手工回退两处游标后功能立刻恢复。

- **`JudgementEngine.Update` 必须从 0 全扫**。理由不是性能，而是**游标无法自证其前提**：
  `Update` 自己会 `MarkMiss`，`PressLane/PressFlick/PressSky` 又从**全表**挑
  「时间最近」的候选（`FindLaneCandidate`/`PressSky` 都从 0 遍历），
  所以音符被判定的顺序**不保证等于数组下标顺序**。游标一旦停在一个
  「永远不会被判定」的音符上，**它后面的所有音符此后都不再被访问**。
- **`AutoPlayStep` 同样必须从 0 全扫**。更致命的是它跨方法持有状态：
  `GameplayBootstrap` 有**三处** `_engine = new JudgementEngine(...)`
  （`Start()`、`RebuildChartRuntime()`、`ResetAutoplayStateAt()`），
  引擎是新建的，**但 bootstrap 侧的游标不会自动归零**。
  `ResetAutoplayStateAt` 还会用 `SkipNotesStartedBefore(now)` 把 seek 点之前的音符
  直接标成 `Judged` —— 那些音符**从未被 autoplay 循环访问过**。
  于是游标停在旧位置，seek/重开后 `AutoPlayStep` 从错误下标起扫，
  一大批音符永远按不到 → `Judged`/`NoteStarted` 事件不发 → 特效全灭。
- **正确的省法是「别每帧重复求值」，不是「少看音符」**：
  `ScaledTimeAt` 前缀表、`EndTimeMs` 缓存、播放头 memo 这类**纯函数加速**
  没有这个问题（它们不改变谁被访问）。要做访问裁剪，前提是有一个
  **能证明单调**的结构（例如按 `TimeMs` 排序并用二分定位窗口），且该结构的
  失效点必须与 `JudgementEngine` 的**每一次重建**挂钩——现有代码没有这种单一入口，
  所以这里选择**不做**。
- 特效触发链是 `JudgementEngine.Judged` / `NoteStarted` → `NoteHitEffectRenderer`
  （只收 `Tap`）/ `FlickHitEffectRenderer`（只收 `Flick`）。**任何吞掉或跳过判定的
  改动，症状都是「特效没了」而不是报错**，所以不要用「有没有崩」来判断这类改动是否安全。
（2026-11-06，实测回归：加游标→特效消失；回退→恢复。）

### A-23 音频预处理的延迟符号是 `S = −D`，写反等于整体错位 2D（2026-11-06）

音频预处理窗口把 `# delay:` 烘焙进音频、然后把延迟清零。映射只有一种自洽写法：

```
新音频(t) = 旧音频(t + D)        // D = 当前难度旧 delay
音频延迟 S = −D                  // 正=头部插空音，负=裁头
新时长   = 旧时长 − S = 旧时长 + D
```

- **旧 delay 为正是裁头（红块），不是插空音**。用户的直觉常反过来；这里以数学为准：
  旧 `delay = D` 已经意味着「音频第 D 毫秒 = 谱面 0」，烘焙就是把这个起读点去掉。
  符号写反会同时错两次（一次在音频前置/裁剪，一次在音符平移），结果偏移 **2D**。
  `FalsumAudioPreprocessMath.SilenceMsForDelay` / `NewAudioDurationMs` 是唯一入口。
- **音符与事件时间一律不动**（它们本来就是谱面时间，新音频仍对齐同一段内容）。
  唯一的例外是**多难度基线不一致**：难度 k 按 `D_k − D_ref` 平移，才能和同一份音频对齐
  （`AlignmentDeltaMs`）。
- **旧 delay 派生的那条 `bpm(0,bpm,division)` 必须清掉**（`RemoveRedundantBaseBpmEvents`），
  否则每次保存多一条（§A-5）。注意 `Chart.RebuildTimeline` **只排序、不重建 kind 列表**，
  所以清除后必须 `RebuildEventKindLists`，否则 `BpmEvents` 仍留着已删事件、`BpmAt` 读错。
- **BASS 必须先 `FalsumBassSession.Acquire`**：编码器与包络都要解码包内音频，而编辑器里
  BASS 可能还没被 `FalsumBassAudioEngine` 初始化。未初始化时 `Bass.CreateStream` 返回 0 且
  `LastError == Init`——这条错误信息表面上像「音频坏了」。
- **解码通道必须泵数据**（`ChannelGetData` 到 `Ended`），否则 `BASSenc_OGG` 只写出约 3.8 KB
  的 Ogg 头：文件存在、魔数正确、时长 0。这个失败模式**看起来像成功**，所以
  `FalsumAudioPreprocessEncoder` 自己持有泵循环，并额外用时长断言兜底。
- **验收必须含真实音频的端到端探针**（`FalsumAudioPreprocessProbe`）：
  纯数学与资源断言抓不到「泵没跑」这件事。探针断言裁头/前置/零位移三种情形的时长，
  以及包络非空。
- **模态模板的样式表必须由代码 `styleSheets.Add`**：本项目的对话框模板**不用**
  `<ui:Style src>`（`FalsumNewChartDialog.uxml` 连 Style 行都没有），它们依赖主文档
  已经挂上的 `FalsumEditor.uss`。新窗口若带自己的 USS，必须在
  `Instantiate()` 之后显式挂上，否则**所有 class 静默失效**：窗口能开、日志显示
  `open=true`，但面板与子元素全是 0 尺寸并重叠在左上角。
  这个失败模式**没有任何异常**，只有截图能看出来——所以
  `FalsumSelfCheckAudioPreprocess` 断言 `DebugStyleSheetAttached`，
  `FalsumAudioPreprocessCapture` 断言 class 名单与 selector 命中。
- **试听音源不能挂在编辑器自己的 GameObject 上**：`FalsumBassAudioEngine` 是
  `OnAudioFilterRead` 组件，Unity 规定这类组件只能被**一个** AudioSource/Listener 调用；
  在同一个 GameObject 上再加 AudioSource 并 `PlayScheduled`，引擎的 filter 回调会被
  拒绝（日志："GameObject has multiple AudioSources ... may only be used by either
  one"）。试听必须用独立子 GameObject 的 AudioSource。
- **不要用「进 play 模式」的截图工具验证本窗口**：批处理模式的编辑器进入 play 模式后
  可能残留一个 GUI Unity 进程占住工程锁，之后所有 `-batchmode -quit` 编译都静默返回 1。
  编辑器模式下 UITK 不做布局 pass（`resolvedStyle` 是 NaN），拿不到像素；可行的是
  **模板实例 + 宿主尺寸 + 显式挂表的几何/class 断言**（`FalsumAudioPreprocessCapture`）。
（2026-11-06，实测：alamode.falsum 135670 ms → 裁头 135420 / 前置 135920 / 零位移 135670。）

### A-24 歌曲 ID 有两套规则，读侧与新建侧不得互换（2026-11-08）

新建谱面包时必须在对话框里**手填** `songId`，规则是 `^[a-z][a-z0-9]{0,63}$`
（以小写字母开头，其后只允许小写字母与数字）。**读包校验不跟着收紧**，
仍是 `^[a-z0-9][a-z0-9_-]{0,63}$`。

- 两处常量都在 `FalsumManifest.cs`：`SongIdPattern`（读）与 `NewSongIdPattern`（新建），
  谓词分别是 `IsValidSongId` / `IsValidNewSongId`。**不要合并成一条**：
  收紧读侧会让带 `_`/`-` 的既有包与自检固件（`partial_safety`、`localization-audit`）当场拒载；
  放宽新建侧则等于默许用户敲进大写 ID——那是本功能的唯一目的。
  `Tools/songid_rule_offline_check.py` 复刻同一组边界并断言两条规则确实不同。
- **新建不再要求「文件名 == songId」**：`FalsumPackage.CreateNew` 删掉了把目标路径
  重写成 `<songId>.falsum` 的那段，`SourcePath` 就是用户选的路径。ID 的身份只由
  `song.json` 的 `songId` 与各难度 chart 的 `# song:` 头承载。
- 二次闸：`CreateNew` 自己用 `IsValidNewSongId` 校验，不依赖 UI。它也被自检直接调用，
  漏填必须 fail-closed 抛异常，而不是写出一个空 ID 的包。
- 不做即时改写（不自动转小写、不剔非法字符）：非法输入留到点「创建」时报错，
  所见即所存。唯一例外是首尾 `Trim()`，与 `Title`/`Artist` 一致。
- 74 个官方包已逐包核对，**两套规则都满足**，因此该变更不触碰既有语料。

### A-25 多选拖拽：按下时**不得**改写选区，且整组共享**一个**时间增量（2026-11-10）

用户报的原话：「多选 NOTE 时不能一起移动时间，点击 note 后自动取消多选然后单选点击的 note 了」。
两个独立的 bug 叠在一起，必须同时修，缺一个症状都还在：

1. **按下即重写选区**。`HandleSelectDown` 在 `_selected.Count > 1` 时也执行
   `_selected.Clear(); _selected.Add(note);` —— 光标一按下，组就没了，
   `_dragStates` 只剩那一个音符，`DragSelection` 自然只拖一个。
   带 Ctrl（手机端是「多选」锁存）时更糟：按下**已选**音符立刻把它从选区**移除**，
   于是「按住 Ctrl 拖整组」永远拖不动。
   → **点击语义推迟到松开**：按下只记录 `_selectionCollapseOnRelease`
   （`DeferSelectionCollapse`），越过拖动阈值时 `ActivateSelectionDrag` 把它清掉，
   没越过阈值就在 `HandleLeftUp` 里 `ApplyDeferredSelectionCollapse`：
   普通点击 = 塌缩成单选被点音符，Ctrl 点击 = 从选区移除该音符。
   **已选中的音符必须无条件武装拖拽**：`DirectDragUnselectedNotes` 管的是
   「未选中」的音符，不适用于「组内按下」。
2. **每个音符各自吸附**。旧实现把每个音符的
   `(原时间 + 增量)` 与网格比、取更近的一侧：
   - 本来就在网格上的音符 → 原时间更近 → **原地不动**，看起来像只有被点的那一个在动；
   - 位于不同分数上的音符 → 各自吸到不同网格线 → **组内间距被改变**（整组被拉散）。
   → 多选时**只对锚点吸附一次**，得到绝对增量 `Δ`，其余音符一律 `原时间 + Δ`。
   时间 0 的下界也是**整组**行为：按最靠前音符到 0 的距离收缩 `Δ`，
   而不是逐个压到 0（否则整组被压扁成同一时间）。
   纯算术在 `F/ Core/FalsumSelectionDragMath.cs`（`ResolveDelta` / `UsesSharedDelta`），
   编辑器只做取值与调用，**不保留第二份**。

`FalsumSelfCheck.CheckSelectionDragMath` 覆盖：共享增量、间距不变、时间 0 整组夹取、
`UsesSharedDelta` 的三种情形、非有限输入不产生位移，以及
**调用点接线**（`UsesSharedDelta` / `ResolveDelta` / `note.TimeMs = uniformDelta` 必须还在）。
最后一条是踩出来的：最初只断言纯函数，把一个变异（`uniformDelta = hasGroupSnap`，
即退回逐音符吸附）放进去**自检照样通过**；补上调用点断言后该变异立刻失败。
入口 `InFalsus/Falsum/Run Selection Drag Self Check`（写
`Diagnostics/FalsumSelectionDragSelfCheck.json`）——独立入口是因为完整 `Run()`
目前卡在 `CheckUiToolkitResources` 的 Bank 标签栏断言（与本节无关，见 05-gaps.md）。
（2026-11-10。）

---

## B. 渲染与表现

### B-1 Flick 的方向/颜色与 trail 缩放
`4 = 绿/右`（模板 `flickRightPrefab`、基础旋转非 identity），
`16 = 黄/左`（identity）。颜色 `(0.2,0.95,0.55)` 与 `(1,0.82,0.18)`。
Trail 宽度按 `span / 1.54` 缩放后回接头部。
`NoteRenderer.cs`（模板选择、`FlickLeftPos/FlickHeadPos`、`AlignFlickPlane`）。

### B-2 Hold 必须三段齐全
`HoldFront` / `HoldMiddle` / `HoldBack` + 两个 Renderer；捏合重叠 0.05，flick 头重叠 0.08；
只在判定面裁剪，**尾端过线才回收**（不得整件提前隐藏）。

### B-3 SkyArea 每条连续链**一个** mesh
聚合键是 `GroupId`（链路关系由 A-1 的规则在写谱阶段确定），不是「几何相邻」。
逐段建 mesh 会破坏接缝与曲线。

### B-4 SkyArea 几何细节
5 ms 采样步长、共享边界 epsilon `0.0001`、每行 4 顶点、索引 `0,1,3,3,1,2`；
纹理必须 `Repeat`（`Clamp` 会拉出长竖条）。

2026-11-15：`SkyAreaRowCache` 缓存固定行，单调段裁去轨道外零面积面片，非单调段保留完整检查。
缓存必须随时间轴版本、流速、轨道长度和音符几何字段失效；暂停/停走可跳过 Mesh 上传，
但仍须每帧向 `SkyJudgementLineEffect.SetRange` 提交当前范围，否则辉光会误淡出。
`FalsumNoteRenderSelfCheck` 已接入完整自检；详见 `docs/reviews/2026-11-15-note-render-optimization-results.md`。

### B-4a 组号一变，整组 mesh 必须重建（与「哪种 kind 被改」无关）
`GroupId` 只在 `FalsumSkyAreaGrouping.Normalize` 里重算，而 `Normalize` 挂在
`CommitChange` / `NotifyChanged` / `Replace` / `ApplyBatch` 上。**任何**谱面改动都可能
改变分组：删掉夹在两段 sky 之间的一个 tap、把 tap 拖过链边界、改 tap 的 `time_ms`、
粘贴一组纯地面音符、工具批量改写——此时被改的都不是 skyarea，但两段 sky 会并成一（或拆开）。
→ 不能沿用 `NoteRenderer` 里「被改 note 的 kind 是 SkyArea 才 `RebuildSkyAreas`」的
增量判断（`AddNoteVisual(s)` / `RemoveNoteVisual(s)` / `RefreshNoteVisual(s)` 都有这个分支）。
`FalsumChartEditor` 的做法：`OnSessionChanged` 置 `_skyGroupingDirty`，在 `Update` 帧末
`FlushSkyGroupingDirty()` 重建一次；走 `SyncFromChart` 的路径（`RefreshEditorVisuals`、
`RefreshEditorAfterHistory`、`MiddleEdit` 的 `touchesSkyArea` 分支）已全量重建，顺手清脏。
新加编辑路径若整组重建过，也要清脏，否则会多一次重建。

### B-4a′ 重建之后必须补一次表现刷新（否则天空区「消失一帧」）
`NoteRenderer.RebuildSkyAreas()` **只** `DestroySkyVisuals()` + `BuildSkyChains()` ——
它销毁所有链根、重填 `_skies` 列表，但**不建 mesh、不激活 Root**；Root 的创建与
mesh 的写入在 `EnsureVisualsForTime` / `UpdateSky` 里，要等下一次 `NoteRenderer.Update`。
→ 大部分编辑路径先调 `UpdateChartPlaybackFrame` **再** `CommitChange/NotifyChanged`
（`FinishEditAdded`、`DeleteSelection`、中键 `CommitMiddleEdit`、属性面板 `ApplyPropertyEdit`、
拖拽 `HandleLeftUp`、`MiddleEdit` 缓动切换），所以帧末的 `FlushSkyGroupingDirty` 重建出的链
在整个这一帧里没有任何 Root；编辑器暂停时 `Update` 也不再刷新表现 → 天空区要等到下一次
seek / 滚轮 / 鼠标移动才冒出来。表现就是「放置、删除、移动音符、改缓动时天空区突然消失」。
→ `FlushSkyGroupingDirty` 重建后必须立刻 `_host.RefreshSkyPresentation(_playheadMs)`
（= `RefreshPresentationAt`；只刷 NoteRenderer 与其它表现件，不重建 engine / autoplay / 音频，
比再调一次 `UpdateChartPlaybackFrame` 便宜且不会重复推进 autoplay）。
新加任何「重建天空链」的路径都要跟这一句。
（2026-11-02。）
（2026-11-01，滞后修复。）

### B-4b Combo 是纯前缀函数，必须在每次 commit 后重算
`FalsumAutoCombo.CountAt(chart, t)` 不存增量，只读 `chart.Notes` 与 `Chart.BpmAt`
（即 `BpmEvents` 的排序）。`CommitChange` 只做 `Normalize`、**不** `RebuildTimeline`，
BPM 事件表是否排好序取决于调用方有没有先 `RebuildEditorTimeline`。
→ `OnSessionChanged` 里调 `_host.RefreshComboAt(_playheadMs)`（`GameplayBootstrap`
新增的公开入口，只刷 Combo，不碰 engine / autoplay / 表现时钟）。
不要指望编辑器 `Update` 在暂停时会刷新 Combo：暂停分支只调 `UpdateChartPlaybackFrame`，
且各编辑处理函数是否调用它并不统一。

### B-5 小节线只画判定线 → 轨道末端
目标 z 必须落在 `[判定线 - 0.001, 轨道末端 + 0.001]`，否则整条跳过。
单次 alpha 混合，无 glow、无 outline。`bpm` 事件与 `chart` 驱动小节线，`track` **不驱动**。

### B-6 外侧墙三角形特效已移除（2026-11-01）
旧的 `12×3` Quad 重建（`GameplayWallScroller` + `WallMaterial.mat` +
`GameplayWallRectangleTriangles`）因还原度不足已整体删除；
不要重新挂回 `SHIFT_2` / `SPACE_2`，也不要把已废弃的方案
当成发行版事实。见 `docs/memory/05-gaps.md` §G-13。

### B-7 TrackEnd 遮罩的时间源 `_TimeOverride`
`FalsumTrackEndDiamondMask.shader:5` 声明 `_TimeOverride = -1`，
`:50-52` 用 `_TimeOverride >= 0 ? _TimeOverride : _Time.y`；Composer 每帧写入
`PresentationAnimationClock.TimeSeconds`。改 shader 必须保留这个属性，
否则遮罩动画会脱离暂停/seek 语义（回退到 `_Time.y` 后暂停时仍会动）。

### B-8 RenderQueue 分层
背景 2900 < 频谱 3002 <（TrackEnd 输出 2950 属于另一条链）。
调换会立刻穿插。

### B-9 表现层时间轴独立于谱面时间
`PresentationAnimationClock`：Running 时 `+= min(unscaledDeltaTime, 0.1)`，否则冻结；
**只有显式重开谱面才 `Reset()`**。Seek 不得让墙/TrackEnd 跳相。
编辑器改 `PlaybackRate` 不应改表现相位。

### B-10 音符 prefab 清单与 `NoteRenderer` 构造签名
`NoteRenderer` 13 参构造，由 `GameplayBootstrap` 的序列化字段注入。
加参数会打断编辑器/构建时的装配。

### B-11 `matid_*` 与原版 prefab 不可改名
`Assets/InFalsus/Recovered/` 下的路径被代码硬编码；改名 = 运行时静默丢失。

### B-12 恢复 Shader 的兼容层只能在三处
`Assets/InFalsus/Shaders/Compat/`、`Assets/InFalsus/Gameplay/Resources/`、
`Assets/InFalsus/Gameplay/Falsum/Resources/`。
改 shader 名必须同步改 C# 字符串常量，否则静默回退。

### B-13 Hold 中段长度 = `meshLengthZ × localScale.z`
不是固定常数。

### B-14 判定窗口与边界归属
Lane：Shiny ±25、Perfect ±50、Near ±100、early Break +100..150、late Break < -100、None > +150。
Sky 窗口是对应 Lane 的**两倍**。边界值 `50/100/200` 归**内层**。
`Diagnostics/JudgementMatrix.csv` 是全量真值表。

### B-15 音频密度约束
谱面时间差 ≤ 1 ms 的 tap 合并为单一 transient（chord 仍逐个判定）；
one-shot 池 48 槽，满则抢「最早结束」的槽，**不得丢新击**；
限幅硬顶 -1.5 dBFS、lookahead 2 ms、release 120 ms。

### B-16 音符可视窗口必须覆盖整条可见轨道（2026-11-02）
`NoteRenderer` 只保留窗口内的音符实例，而**窗口是时间长度、音符消失却是空间事件**
（`TrackMotion.IsVisible` 用 `|z| < TrackLength`，`Fade` 在 `z == TrackLength` 才归零）。
旧的 `1250 / (0.3 × |flow × trackSpeed|)` 加 5 s 上限只覆盖约 1.25 个轨道长；
只要 `flow × trackSpeed < 0.7`，音符就会在轨道**中段、完全不透明**处被回收后又重新生成
——即低流速下「音符突然出现」。

正确做法：把可见轨道长度按当前轨道速度换算成时间
（`VisualLeadMsForTrackSpeed`，`visibleZ = TrackLength × 1.1`，1.1 保证创建帧早于首个可见帧），
再套 `VisualWindowCeilingMs = 40000`（覆盖到 `trackSpeed ≈ 0.09`；更慢只能优雅退化，绝不再缩到判定线）。
配套约束：
- 后向不再用 `×1.75` 保留系数，改为 `VisualWindowTrailingMs = 1500`：过线音符本已淡到 0，留着只占实例；
- **反向段**（谱面里确有 `track(-20)` / `track(-74)`）窗口的宽边必须落在**过去**一侧，
  由 `_lastVisualDirection` 决定（`VisualWindowEdges`）；否则反向段等于把窗口放错边；
- 变速点会整窗换尺寸，所以每帧创建量受 `VisualCreateBudgetPerFrame = 48` 限制；
  回收不受限（回收比创建便宜，延迟回收只会让死对象继续占位）。
- 停住（`trackSpeed == 0`）没有距离↔时间映射，沿用上一帧的 `_lastVisualLeadMs`，不能返回 0。

自检 `GameplaySelfCheck.ValidateNoteVisualLeadTime` 断言 `0.1…10` 速度范围内窗口都覆盖整条轨道。

### B-16a TAP 是否「已过判定线」必须按谱面时间判定，不能看 `z < 0`（2026-11-04）
TAP 是唯一一过线就立即隐藏的 note（没有尾巴可画）。过去写的是
`note.Kind == NoteKind.Tap && z < 0.0 → visible = false`，**判定线正上方放过一个 TAP
会显示不出来**（谱面里确实有它，但抓不到模型）。两条独立的舍入路径会把「正好在线上」的
note 推到线的错误一侧：
- `IntegerTimeMs` 用 `MidpointRounding.AwayFromZero`：播放头 10000.4 ms 时存的 note 是
  10000 ms，即最多领先播放头 0.5 ms；
- `z` 由 `ScaledTimeAt` 的分段累加重建（`track()` 变速点处的浮点求和误差），
  note 时间与播放头**完全相等**也可能算出极小的负 `z`。

正确做法：`IsTapPastJudgeLine(note, nowMs) = nowMs > note.TimeMs + JudgeLineVisibilityToleranceMs`
（`JudgeLineVisibilityToleranceMs = 1.0`，覆盖 0.5 ms 整数化 + 标度时间误差；
换算回世界坐标在所有支持的轨道速度下都远小于一像素）。
→ 不要改回 `z` 符号判断，也不要为了「更精确」把这个容差压到 1e-9：
它防的正是**上游**的取整，不是同一帧内的比较误差。
`NoteRenderer.cs`（`JudgeLineVisibilityToleranceMs`、`IsTapPastJudgeLine`、`UpdateVisual`）。

### B-17 Track 效果可以「只关预览、不动数据」，但只能有一个覆盖点（2026-11-05）
编辑器需要在不改谱面的前提下停止 `track()` 对预览的影响（方便编辑负数 `track` 的段落），
做法是 `Chart.TrackSpeedEffectsEnabled`（`[NonSerialized]`，默认 `true`）：
关闭时 `ScaledTimeAt(t)` 直接返回 `t`，即等价于整谱只有 `track(t,1.00)`。

**不能分系统各改一遍**：`TrackMotion`（note z / hold 长度 / 轨道行进）、`MeasureLineRenderer`、
`LaneStateController`、`FalsumEditorWorld.TimeAtZ`（3D 拾取反投影）、频谱 warp 全都读
`ScaledTimeAt`；在 `ScaledTimeAt` 一处覆盖，它们才会一致。若只改 `TrackMotion`，
音符是线性的而网格/小节线/拾取反投影仍按变速走，放下的物件会落到错误时间（同 A-16 的坑）。

**显示用 `PreviewSpeedMultiplierAt`，数据用 `SpeedMultiplierAt`**：
- `PreviewSpeedMultiplierAt(t)` 关时恒 `1.0`，供视觉窗口方向/宽度（`NoteRenderer.VisualLeadMs`）
  与轨道禁用区间方向（`LaneStateController`）使用；
- `SpeedMultiplierAt(t)` **永远返回真实值**，新建 track 事件取默认倍率、工具脚本、自检都必须用它。
  若让抑制泄漏进数据值，抑制状态下新建/编辑的每个 track 事件都会被压成 `1.00` 并写回包。
- `FalsumEditorWorld.TimeAtZ` 在抑制时走解析解，不再做 40 次二分（指针移动/建网格的热路径）。

**开关是运行时状态，不是谱面数据**：`[NonSerialized]` + 不进 undo 签名 + 不写包；
`FalsumChartSession.Replace` 复用同一个 `Chart` 实例，所以重开谱面时要显式重置（当前实现
在初始化路径同步）。谱面预览模式（View → 进入谱面预览）会临时强制开启，退出时恢复。
切换必须只重绘表现（`RefreshEditorAt` + `UpdateOverlays`），**不得** `RebuildTimeline` /
`SyncFromChart` / 重建渲染器或音频，否则大谱面切换会卡顿。
自检 `FalsumSelfCheck.CheckTrackSpeedEffectSuppression` 覆盖：线性化、数据值不被改写、
事件数不变、写谱字节一致、开→关→开可逆、重新解析后回到默认开启。

---

## C. UI 与本地化

### C-1 禁止硬编码可见文本
C# 走 `L.Get(key, ...)`；UXML 走 `l10n.*` / `l10nt.*` 元素名。
`Tools/falsum_localization_audit.py` 会拦。
**前缀表也走本地化**（原「唯一已知例外」已于 2026-11-02 清除）：
`FalsumPreferencesView.IsVerboseDescription` 用 `L.Get("falsum.preferences.layout.verbosePrefixes")`
（`|` 分隔的前缀串，两语言各一份）判定文案是否走紧凑布局，代码里不再有中文/英文字面量。
再出现「功能性的字符串表」也照此办理，不要给审计加白名单。

### C-2 数字输入必须过 `SanitizeNumericInput`
公开入口 `FalsumEditorView.SanitizeNumericInput(string, bool allowFraction = false)`；
偏好页与工具对话框都复用它。自己 `TryParse` 会导致 IME/全角/多小数点漏网。

### C-3 具名占位符
`Get(key, ("name", value), ...)` 只替换 `{name}`；禁止字符串拼接生成显示文本。
未命中 key 时**返回 key 原文**（会显示裸 key）——新增 key 必须同时补两套语言。

### C-4 View 不持有 Editor
View → Editor 只有 `public event`；Editor → View 只有 `_view` 门面的 `Set*`。
双向持有会形成循环并在编辑器销毁后留下悬挂引用。

### C-5 交互 UI 全归 UITK
UI Toolkit 管全部输入与布局；只有「选中轮廓」与「模式平面」留在 uGUI 世界空间 Canvas，
且**不参与 UITK 命中测试**。

### C-6 风格切换只换样式表
`FalsumUiStyles.Apply` 先摘旧 sheet 再挂新 sheet；`Classic` 不挂附加表。
风格**不得**改变谱面世界颜色、背景或重建编辑器。
2026-11-15 恢复 `偏好设置 → 高级 → UI 风格`（`FalsumUiStyleSwitcher.Available = true`）。
新风格 `InFalsus` 追加在枚举末尾，旧 `Classic/Studio/Canvas` 数值不变。
`UiStyleChosen` 区分用户主动回退与历史强制 Classic 默认：主动选择必须置 true，
否则下一次 Normalize 会把历史默认升级为 InFalsus。未知枚举回落 Classic 并置 true。
`FalsumPreferencesMigration` 必须读取现行 `Experimental` 组；仅克隆 JSON 而不走磁盘
读取会漏掉回退丢失。切回 Classic 必须移除新样式表与 `is-infalsus`，并恢复旧属性面板高度。

### C-7 PanelSettings 与圆角
PanelSettings 参考分辨率 1730×1080 + MatchWidthOrHeight；所有控件圆角 0。

### C-8 歌曲信息卡尺度
设计稿 `3013 × 559`，按面板宽度 **32.175%** 缩放；右留 24 px、上留 18 px。

### C-9 径向菜单坐标
屏幕点先 `ScreenToPanel` 再翻 Y；中心夹取 8 px；框尺寸 260–440 × 220–390；
半径下限 54，上限 = 到四边余量 − 70。

### C-10 编码与分隔符
所有 `.cs` / `.json` / `.md` / `.uxml` / `.uss` / `.shader` 是 **UTF-8**；
`.gitattributes` 强制 LF。用 `-Encoding UTF8` 读写，否则中文会变乱码
（工作区里已有几处历史乱码，见 `05-gaps.md`）。

### C-11 UITK 的 `WheelEvent.delta.y` 与 `Input.mouseScrollDelta` **符号相反**（2026-11-09）
同一个「向上滚滚轮」在两个输入通道里取到的 `y` **正负号相反**：

| 通道 | 向上滚 | 向下滚 |
|---|---|---|
| `Input.mouseScrollDelta.y`（编辑器主路径） | 正 | 负 |
| `WheelEvent.delta`（UI Toolkit） | **负** | **正** |

这不是 Unity 的 bug，是两套约定：UITK 的 `ScrollView` 把**正值当「向下滚动」**，
而 uGUI 的 `ScrollRect` 反过来；Unity 自己的桥接代码在
`PanelEventHandler.OnScroll` 里显式翻转一次
（`uitkScrollDelta.y = -uitkScrollDelta.y`，注释 `ISXB-808`）。

- **凡是从 UITK 回调转发到 `FalsumChartEditor` 滚筒逻辑（`ScrollTimeline`）的地方，
  必须在 UITK 边界翻转一次符号**，否则方向就与谱面本身的滚轮相反。
  已踩坑：外置工具 note 选择界面（`FalsumToolDialogView.OnNotePickWheel`）
  直接透传 `evt.delta.y`，于是选择器里滚轮**反向**，而背后谱面正向。
  修法：统一走 `FalsumToolDialogView.NormalizeWheelDelta()`，`NotePickScrolled`
  事件的契约是**已归一化的、与 `mouseScrollDelta` 同号**（正=向前）。
- **只有「转发到谱面滚筒」这一种用途要翻转**。UITK 自己消费滚轮时（例如
  偏好设置、工具表单的 `ScrollView`）**不要**动，它们内部已按 UITK 约定处理。
- `FalsumSelfCheck.CheckToolNoteFields` 对该转换做了双向断言，
  别再「顺手化简」把 `-` 去掉。
- 与 A-21 区分：双指上下滑**故意**与滚轮相反（拖内容直觉），
  而 UITK→滚轮这一条是**要抹平**的通道差异，两者结论方向不同。

---

## D. 工程与流程

### D-1 没有保护字体不能构建
`Assets/StreamingAssets/Falsum/Fonts/SongInfoCard.fpk` 缺失 → 构建直接抛异常。
只能用 `InFalsus/Falsum/Rebuild Protected Song Font` 重建，源 ttf 必须 SHA-256 匹配。

### D-2 构建不是只读操作
构建会改写 `FalsumPanelSettings.asset`、`Assets/Settings/PC_Renderer.asset`、
音符 prefab 与 `PlayerSettings`。构建后必须 `git status` 检查资产漂移。

### D-3 构建把外部内容拷出 StreamingAssets
`GameplayBackgrounds → GameplayBackgrounds`、`UserTools → ExternalTools`，
随后删除 `FalsumEditor_Data/StreamingAssets/Falsum/{GameplayBackgrounds,UserTools}`。
源目录缺失会抛异常。构建末尾的 `VerifyPublishedBackgrounds` 会逐个校验
`GameplayBackgrounds/` 能解析映射表里的每个 `background_file` 和每个物理文件的
`.png` 逻辑名，解析不到就直接失败（不要注释掉它）。发布目录名**必须**是
`GameplayBackgrounds`：构建会主动删掉误留的 `Backgrounds/` 旧副本。

### D-3b 背景的物理扩展名与逻辑名分开
库里的背景是 **JPEG**（2026-11-01 精简），但谱面语义**永远是 `.png`**：
`song.json`、默认背景、映射表都写 `.png`。这是制谱器单方面的减重手段，
不是格式变更。规则：

- **逻辑名永远是 `xxx.png`**：`FalsumManifest.IsValidBackgroundFile` 接受
  `.png/.jpg/.jpeg`，但只有 `.png` 是规范名。`FalsumManifest.SetBackground`
  与 `CreateTemplate` 都过 `NormalizeBackgroundName`，`xx.jpg` 写进包会被
  改写成 `xx.png`；`FalsumBackgroundCatalog` 报给 UI / 写回 `song.json` 的也
  都是 `.png`。不要往包里写 `.jpg`——旧播放器只认 PNG 名。
- **物理文件用 `FalsumRuntimePaths.ResolveBackgroundFile` 解析**，它按
  `请求扩展名 → .png → .jpg → .jpeg → 忽略大小写的同 stem 扫描` 顺序找。
  不要再用 `Path.Combine(BackgroundsDirectory, name)` 直接拼路径。
- **背景根目录是有序候选表**（`BackgroundDirectories`）：exe 同级
  `GameplayBackgrounds/`（原位置、规范）→ exe 同级 `Backgrounds/`（迁移期
  旧构建的目录，仅作回退）→ StreamingAssets 里的 `GameplayBackgrounds/` 与
  `Backgrounds/`。覆盖解压会同时留下两个同级目录，所以这些位置都要能加载。
- 缩略图同理：`<root>/Thumbnails/xxx.jpg` 仍然回答 `xxx.png` 的请求。

### D-4 版本串有两个来源
`FalsumEditorInfo.Version = "0.46-Canary"`（决定构建目录名）与
`FalsumBuild.BuildVersion = "0.46.0"`（`PlayerSettings.bundleVersion`）。
改版本必须同时改这两处 + README。

### D-5 门禁顺序
编译 → Editor Self Check → Build。
前一步失败不要继续（构建会覆盖 `Builds/FalsumEditor-<Version>` 与同名 zip）。

### D-6 批处理会打开运行时场景
`Phase A 自检`、`Capture Runtime Play` 等截图/自检工具用
`OpenScene(..., Single)` 且不保存。不要在有人正在编辑的场景上跑；
先确认没有 Unity 进程与 `Temp/UnityLockfile`。
（旧的 `GameplayWallSelfCheck` / `GameplayWallCapture` 已于 2026-11-01 删除）。

### D-7 自检报告无条件覆盖写
`FalsumSelfCheck.WriteReport` 每次写 `Diagnostics/FalsumSelfCheck.json`，失败也写。
`checks` 数组**只在失败点之前**的项——不能当覆盖率证据。

### D-8 不要清理别人的工作区
接手时 68 改 + 32 新增（接手前既有）未提交。禁止 `git checkout` / `reset` / `stash` 清理。

### D-9 安卓渲染与帧率构建门禁（2026-11-16）

Android 的 `Mobile_Renderer.asset` 必须同时包含启用的 `SkyJudgementLineEffectFeature`
和 `FalsumTrackEndComposerFeature`；只给 PC 渲染器接线会导致安卓丢失特效。
完整自检中的 `android_render_features_and_refresh_rate` 检查这两条接线和帧率选择。
`FalsumFrameRate` 从同分辨率支持模式与系统刷新率上限选择目标帧率，并在重新获得焦点时更新；
OEM 的 0/1/2 模式编号不能作为 Hz。桌面窗口 API 只在独立桌面平台编译。
`FalsumBuild.BuildAndroid` 使用 Release IL2CPP 编译和安卓帧调度；可以用进程环境变量
`FALSUM_ANDROID_APK_NAME` 指定新文件名，避免覆盖正在被占用的旧 APK。
