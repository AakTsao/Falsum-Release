# FALSUM Skyarea Bank 设计（天空区组银行）

状态：设计已确认（2026-11-06），待实现。
范围：FALSUM 谱面编辑器（`Assets/InFalsus/Gameplay/Falsum/`）+ 一处编辑器自检。
本设计对应的用户诉求原文：**快捷保存 / 加载 / 放置 skyarea 组，生成分享码给好友，组可以整体调整宽度与长度（宽度分辨率最细 1/200）**。

---

## 1. 目标与非目标

### 1.1 目标

1. 把当前选中的**任意多个 skyarea**（不要求同 `GroupId`、不要求整链）存成命名预设。
2. 左侧栏列出预设，可搜索、打标签、看缩略图、一键放置到谱面。
3. 放置走**幽灵预览 + 确认**，落到当前网格位置，一次撤销。
4. 预设可**记忆宽/长倍率**，放置时直接按倍率生成。
5. 选中一组 skyarea 后，用**编辑器内拖拽手柄**整体调整横向占位（宽度）与时间跨度（长度）。
6. 生成**自包含分享码**（纯文本，无网络无账号）并可反向导入。
7. 侧栏打开时**谱面仍可正常编辑**（侧栏不是模态层）。

### 1.2 非目标（本版明确不做）

| 不做 | 理由 |
|---|---|
| 把预设写进 `.falsum` 包 | 包条目白名单只有 `song.json` + 音频 + 封面 + 各难度 chart（`FalsumPackage.cs:783-801` 是唯一谓词，未知条目走私有 `_unknownEntries` 无公开 API）。改包格式会引入老版本编辑器丢条目的兼容风险，而全局库已满足「一处收藏处处可用」 |
| 短分享码 / 云同步 / 账号 | 工程无网络能力（工具沙箱禁网络，`FalsumToolRuntime` 权限只有 `files.read`/`files.write`），无账号体系 |
| 自动补全同组邻接段 | 用户明确：银行接受「多个 skyarea 在一起」，是否成链由作者决定 |
| 分类树 | 用自由标签 + 搜索替代 |
| 2D 视图锚点 | `Falsum2DWorld` 从无锚点路径（`Falsum2DWorld.cs:1413-1455` 只 `SetPolygons`），本版维持只读轮廓 |
| `track`/`bpm`/`lane` 事件入预设 | 它们不是 skyarea 组的一部分；写进预设会在放置时二次产生事件，风险远大于收益 |
| 放置时的组首/组尾吸附 | 现有吸附只在 `EditorMode.PlaceSkyArea` 生效（`FalsumChartEditor.SkyAreaSnap.cs:35`），扩展需另写一处，本版从简 |

---

## 2. 关键设计裁决（每条都有代码事实支撑）

### 2.1 存储作用域 = 全局库，不进包

- 路径：`Application.persistentDataPath/Falsum/SkyAreaBank/bank.json`。
- 与 `FalsumPreferencesStore`（`preferences.json`）、`FalsumDisclaimerConsent`、`FalsumCacheStore` 同级，是第五个 `Falsum/` 下的常驻文件。
- **不动** `FalsumPackage.IsKnownPackagePath`（`FalsumPackage.cs:783-801`），因此不影响 `.falsum` 兼容性，也不触碰 `FalsumManifest.Validate` 的字段白名单。

### 2.2 放置复用 PastePreview，不新增 `EditorMode`

`EditorMode` 在 `FalsumChartEditor.cs` 有 20+ 消费点；新增模式 = 再造一条预览/输入状态机，正是 A-18 与「两套实现必然漂移」反复踩过的坑。因此：

- 复用 `_clipboard`（`FalsumChartEditor.cs:212`，元素类型 `ClipboardNote` `:55-59`）。
- 复用 `PasteClipboard()` `:10153` → `EditorMode.PastePreview` 的全部既有能力：
  幽灵预览（`BuildPastePreviewGhosts` `:10324`）、指针跟随（`BuildPastePreviewNotes` `:10286`）、
  就地取消、负音乐时间拦截（`RejectNegativeMusicTimes` `:13077`）、
  越界夹取与单元格化（`ClampNoteSkyBoundsPreservingWidth` `:12439` / `NormalizeSkyNoteToCells` `:12226`）、
  一次 `BeginChange`/`CommitChange`、放置后 `AddNoteVisuals` + `UpdateChartPlaybackFrame`。
- 银行放置期间用 `_bankClipboardBackup` 暂存用户原剪贴板，在
  `CommitPastePreview()` `:10214` 与 `CancelPastePreview()` `:10261` 末尾恢复。
- 进入银行放置前若 `_mode == EditorMode.PastePreview`（用户正拿着自己的剪贴板预览），先 `CancelPastePreview()`；
  否则两次预览会共用 `_pastePreviewClipboard` 互相污染。
- 恢复必须**幂等且可重入**：`RestoreBankClipboard()` 在 `_bankPlacementPending == false` 时直接返回。
  另在三个会清空预览的地方也调用它：切难度/重载谱面（`LoadPackage` 路径）、
  `ClearPastePreviewState()`（`:10354`）、以及 `_clipboard.Clear()` 的三处（`:3556`/`:10118`/调试入口）。
  漏一处就会出现「银行放置后用户的 Ctrl+V 粘出 skyarea」这种跨功能串味。

### 2.3 「整组」= 多选，靠既有空尖体系扩展，不新增选中概念

用户确认的交互原话：多选这个组的所有 skyarea，**多选 skyarea 时缓动控制点变为宽度控制点，前后边中点整体作用于多选的 skyarea**。

代码事实（决定了实现位置）：

- 多选时**一个锚点都不生成**：`FalsumEditorWorld.BuildSelection` `:1694-1797` 用
  `selected.Count == 1` 选 `anchorNote`（`:1729-1731`），只有它进 `TryBuildSelectionAnchors`（`:1776-1783`）；
  注释已写明这是设计意图（`:1727-1728`，"handles would overlap and steal each other's hits"）。
- 锚点拖动入口**要求恰好单选**：`AnchorSingleSelection()` `AnchorEdit.cs:116-119`（`Count == 1 ? _selected[0] : null`），
  `TryBeginAnchorDrag` 第一道门就是它（`:133`）。

→ 所以「多选整组 + 拖手柄」必须**同时**放宽两处单选限制，缺一处新角色永远走不到：
`FalsumEditorWorld.cs:1729`（生成侧）与 `AnchorEdit.cs:133`（入口侧）。

- 角色分派**唯一挂点**是 `ResolveAnchorOperation(ChartNote, FalsumSelectionAnchor)` `AnchorEdit.cs:183-210`。
  它是每 kind 唯一一次角色判定，`TryBeginAnchorDrag` 只调一次（`:162`）。现有唯一「按条件换语义」的先例是
  `StartEnd` 按 `note.Kind` 分派（`:195-203`），本设计沿用同一手法，条件改为「选择集是否全部为 SkyArea 且 ≥2」。
- 中键共享体**已经支持多目标**，不需要新写编辑数学：
  `ApplyBoundaryEdit` `MiddleEdit.cs:2295` 遍历全部 `state.Targets`（循环 `:2323-2347`）；
  `ApplyStartEndEdit` `:2649` 遍历全部 Targets（循环 `:2659-2710`，每 target 用自身按下快照做基准，相对间距保持）；
  `ApplyPositionEdit` `:2723` 两遍遍历（统一 clamp 窗口 `:2737-2753` + 写入 `:2766-2792`）。
  `state.Targets` 是按下时刻的数值快照、`state.Notes` 是引用清单，两者在 `AddMiddleTarget` `:2112-2113` 成对写入。

### 2.4 缩放的量化与上限

- **宽度分辨率最细 1/200**：量化分母 `grid = min(LCM(各段 PositionGrid), FalsumFraction.MaxDenominator)`，
  `MaxDenominator = 200`（`FalsumFraction.cs:8`）。取整方向 `AwayFromZero`，与
  `SnapNormalizedDelta`（`MiddleEdit.cs:2810-2822`）完全同一手感（A-9）。
- `PositionGrid` 直接复用现成 `ResolveMiddlePositionGrid`（`MiddleEdit.cs:2193-2208`，内部对 skyarea 取 head/tail 的 LCM）。
- `MaxEditableFlickGrid = 192`（`FalsumChartEditor.cs:72`）按 A-8 仍只作交互网格上限；
  因为 `min(..., 200)` 里 LCM 通常远小于 200，实际出手不会超过 200。
- 写回后把参与段的 `StartCellCount/EndCellCount` 提升到 `max(原值, 量化分母)`：
  否则段自身分母 < 200 时，写谱 `FalsumFraction.NormalizePair`（`ChartWriter.cs:249`）会以自己的分母再量化，
  把已算好的 1/200 坐标丢掉精度（写谱宽度下限是 1.0 格、方向 `AwayFromZero`，`ChartWriter.cs:264-276`）。

---

## 3. 数据模型

新增 `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankModel.cs`。

```csharp
public sealed class FalsumSkyAreaPresetSegment
{
    public double TimeOffsetMs;   // 相对组最早段，首段恒 0
    public double DurationMs;     // >= 1.0
    public double StartPos;       // 0..1 归一化中心
    public double EndPos;
    public double StartWidth;     // >= 0（写谱时 >= 1 格）
    public double EndWidth;
    public int StartCellCount;    // 1..200
    public int EndCellCount;
    public int LeftEasing;        // 0..2
    public int RightEasing;
}

public sealed class FalsumSkyAreaPreset
{
    public string Id;             // "p-" + Guid("N")，只作稳定引用，不参与分享码等价判断
    public string Name;           // <= 48 字符
    public List<string> Tags;     // <= 8 个，每个 <= 16 字符，去重后小写比较
    public string Author;         // <= 32 字符，可空
    public double WidthScale;     // 0.25..4.00，默认 1.00
    public double LengthScale;    // 0.25..4.00，默认 1.00
    public long CreatedAtUtc;
    public long UpdatedAtUtc;
    public long LastUsedAtUtc;
    public List<FalsumSkyAreaPresetSegment> Segments;
}
```

**存盘序列化**（`bank.json`）：

```json
{
  "v": 1,
  "panel": { "opened": false },
  "presets": [
    {
      "id": "p-8f3a...", "name": "开场大宽区", "tags": ["开场", "宽"],
      "author": "", "createdAt": 1762..., "updatedAt": 1762..., "lastUsedAt": 0,
      "widthScale": 1.0, "lengthScale": 1.0,
      "segments": [
        { "t": 0, "dt": 320, "sp": 0.18, "ep": 0.30, "sw": 0.22, "ew": 0.34, "sc": 200, "ec": 200, "le": 0, "re": 1 }
      ]
    }
  ]
}
```

约束与校验（全部在 `Validate()` 内，加载与导入都调）：

- 段数 `1..64`；预设总数 `<= 200`（超出时只拒绝新增，不删除既有）。
- `TimeOffsetMs` 首段必须 0，其余 `>= 0` 且整毫秒（存盘前已 `IntegerTimeMs`）。
- `DurationMs >= 1.0`；`StartPos/EndPos ∈ [0,1]`，`StartWidth/EndWidth ∈ [0,1]`，
  且每段满足 `pos ± width/2` 不越界（与 `FalsumSkyBounds.IsOutOfBounds` 同口径，`FalsumSkyBounds.cs:25-40`）。
- `LeftEasing/RightEasing ∈ {0,1,2}`；`StartCellCount/EndCellCount ∈ [1,200]`。
- 名称/标签/作者：去首尾空白、拒绝控制字符（`< 0x20` 除 `\t` 无）、拒绝换行。
- 未知 JSON 字段：**忽略**（前向兼容）；`v > 1`：**拒绝并提示**（不静默丢字段）。
- 浮点读取：`FalsumJson` 目前只有 `GetString/GetInt/GetBool/GetObject/GetArray`（`FalsumJson.cs:67-140`），
  没有浮点读取器 → 本设计新增 `FalsumJson.GetNumber(FalsumJsonObject, string, double)`（解析结果本就是 `double`，
  `FalsumJson.cs:611-661`），其余 JSON 全走既有无依赖树，**不用** `JsonUtility`。

**接缝整数化（关键）**：保存时把每段 `TimeOffsetMs` 与 `DurationMs` 都过 `IntegerTimeMs`，
并把相邻段接缝强制为 0（`t[i+1] = t[i] + dt[i]`）。原因见 §4.2：

> 放置只对每段做**同一个整数** `timeDelta` 平移，因此整数化后的接缝保持严格 0，
> `FalsumSkyAreaGrouping.Normalize` 仍会把整组归一为同一 `GroupId`（`FalsumSkyAreaGrouping.cs:9/29-93`，A-1）。

若保存时留浮点接缝，`IntegerTimeMs` 会分别取整，接缝可能变成 ±1 ms —— 超出 `TimeToleranceMs = 2.0` 的一半余量，
链就有断裂风险。**这一条是这个功能能「存进去再放出来还是同一组」的根本保障。**

---

## 4. 保存 / 放置

### 4.1 保存（`SaveSelectionToBank`）

1. 收集 `_selected` 中全部 `Kind == NoteKind.SkyArea` 的音符；为空 → 提示 `falsum.bank.hint.needSkyArea`。
2. 按 `TimeMs` 升序（同时间按 `Id`）排序，以最早段为基准建 `TimeOffsetMs`。
3. 逐段 `IntegerTimeMs` + 接缝归零；`StartCellCount/EndCellCount` 原样带上。
4. `WidthScale/LengthScale = 1.00`；名称默认 `L.Get("falsum.bank.defaultName", ("index", n))`；
   标签取当前侧栏的「保存时应用标签」（默认空）。
5. 存库（§5 原子写）→ 侧栏刷新 → 提示 `falsum.bank.hint.saved`（带段数占位）。

不写撤销记录：保存不改谱面。

### 4.2 放置（`BeginPlacePreset(preset)`）

```
保存 _clipboard → 按 widthScale/lengthScale 生成临时段副本 → 装入 _clipboard
                  → 标记 _bankPlacementPending = true → PasteClipboard()
```

倍率副本由 `FalsumSkyAreaGroupScale.ApplyStoredScales(...)` 生成（与拖拽缩放**同一份数学**，§6），
因此「预设记忆倍率」与「谱面上拖手柄」结果一致，不会出现两个公式。

接缝论证（对应 §3 的整数化）：

- `BuildPastePreviewNotes`（`:10286-10322`）里平移量是
  `timeDelta = IntegerTimeMs(pointerTime) - _pastePreviewAnchor.TimeMs`，两个整数相减 → **整数**；
- 每段再 `IntegerTimeMs(note.TimeMs + timeDelta)`：整数 + 整数 → 精确整数，接缝仍严格 0；
- 时间下限保护 `earliestTime < 0` 时整体回推（`:10299-10302`）同样是整数平移，接缝不变。

落到谱面后 `ClampNoteSkyBoundsPreservingWidth` 与 `NormalizeSkyNoteToCells` 逐段独立执行，
两者都只改该段的横向数据，不碰时间，因此**整组链路不受横向夹取影响**。

放置完成（`CommitPastePreview`）后：`RestoreBankClipboard()`、`preset.LastUsedAtUtc = now`、存库、侧栏刷新。

### 4.3 撤销标签

银行放置沿用 `falsum.history.pasteObjects`（`:10232`）。若希望历史里可辨识来源，改为
`falsum.history.bankPlace`（新增 key，两条语言各一份）——本设计采用后者，便于作者回看。

---

## 5. 持久化

新增 `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankStore.cs`。

- `DirectoryPath` = `Application.persistentDataPath/Falsum/SkyAreaBank`
- `FilePath` = `<DirectoryPath>/bank.json`
- 写盘完全复用 `FalsumPreferencesStore.Save` 的原子模板（`FalsumPreferencesStore.cs:55-90`）：

```
path.tmp-<guid> → File.WriteAllBytes → File.Replace(tmp, path, null)
                                   → 失败退 File.Copy(tmp, path, true) + File.Delete(tmp)
                                   → 无旧文件则 File.Move(tmp, path)
                                   → finally 删除残留 tmp
```

- 读失败：`<path>.corrupt-yyyyMMdd-HHmmss-fff` 备份后以空库继续（仿 `PreserveCorruptFile` `FalsumPreferencesStore.cs:150-165`），
  **不阻塞编辑器启动**，只在侧栏顶部显示一条状态警告。
- 侧栏开合状态写在同一文件（`panel.opened`），**不改 `FalsumPreferences` 的组结构**：
  这样 `FalsumPreferencesMigration` 的 `LegacyPreferencesDto` / `JsonPresence` 位图无需新增字段，
  `FalsumPreferences.CurrentVersion`（`:342`）保持 7。
- `LastUsedAtUtc` 更新不立即落盘（避免每次放置都写文件），由「面板关闭 / 难度切换 / 编辑器退出」统一 flush。

---

## 6. 组缩放（`FalsumSkyAreaGroupScale.cs`）

新增 `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGroupScale.cs`，**纯静态、无 Unity 依赖、无编辑器状态**。
把它放在 Core 而不是锚点层，是 A-18/A-19 的直接要求：编辑数学只能有一份，锚点层只许委托。

```csharp
public sealed class FalsumSkyAreaGroupSnapshot
{
    public readonly List<ChartNote> Notes;
    public readonly List<double> StartPos, EndPos, StartWidth, EndWidth, TimeMs, DurationMs;
    public readonly List<int> PositionGrid;
    public double PivotPos;       // 最早段的 StartPos
    public double AnchorTimeMs;   // 最早段的 TimeMs
}

public static class FalsumSkyAreaGroupScale
{
    public static FalsumSkyAreaGroupSnapshot Capture(IList<ChartNote> skyNotes);
    public static bool TryApplyWidthScale(FalsumSkyAreaGroupSnapshot s, double scale, bool clamp);
    public static void ApplyTimelineScale(FalsumSkyAreaGroupSnapshot s, double scale);
    public static void ApplyStoredScales(        // 放置用：一次给定宽/长倍率
        FalsumSkyAreaGroupSnapshot s, double widthScale, double lengthScale, bool clamp);
    public static double ClampWidthScale(        // 供拖拽实时解出合法上限/下限
        FalsumSkyAreaGroupSnapshot s, double desired);
}
```

### 6.1 宽度缩放语义（用户确认）

**整组的横向占位按比例缩/放，各段粗细不变。**

对每段、每个端点（`StartPos` / `EndPos` 当作"该端点的中心"）：

```
newCenter = PivotPos + (oldCenter - PivotPos) * scale
StartWidth / EndWidth  ← 原值不变（不乘 scale）
```

`PivotPos` = 组内**最早段**的 `StartPos`（用户确认的基准）。

- 取整：`round((c - Pivot) * grid) / grid`，`AwayFromZero`，`grid = Clamp(min(LCM(PositionGrid), 200), 1, 200)`。
  LCM 用 `FalsumChartEditor.LeastCommonMultiple`（`:12163-12170`）同样的整数逻辑，但本文件自带实现（Core 不得依赖 Runtime）。
- 夹取：**整组统一**。先由 `ClampWidthScale` 解出使
  `min(每段 左边界) >= 0` 且 `max(每段 右边界) <= 1` 的最大合法 scale 区间
  （左边界 `= c - w/2`，右边界 `= c + w/2`，与 `FalsumSkyBounds.IsOutOfBounds` `:25-40` 同口径），
  再把用户 scale 夹进该区间。**绝不逐段夹取**——逐段夹会破坏组内相对结构（A-19 的同类教训）。
- 退化保护：`|按下点 − PivotPos| < 1.0/grid` 时**不启动**宽度拖动（否则 `scale = (x−p)/(press−p)` 会除以近零）。
- 写回后 `StartCellCount/EndCellCount ← max(原值, grid)`，理由见 §2.4。

### 6.2 长度缩放语义

**以组最早段的前端时间（`AnchorTimeMs`）为基准，向后伸缩。**

```
边界表 = 去重(每段 TimeMs, 每段 EndTimeMs)
newBoundary[i] = AnchorTimeMs + (boundary[i] - AnchorTimeMs) * scale   // scale ∈ [0.1, 10]
→ IntegerTimeMs(newBoundary[i])                                       // 统一取整一次
→ 按映射回写 TimeMs/DurationMs，段长下限 1 ms
```

- **统一边界表 + 统一取整**：相邻段共享同一个边界值，因此接缝严格 0（接缝论证同 §4.2）。
- 段长下限 `1.0` ms 与 `MiddleMinimumDurationMs`（`MiddleEdit.cs:83`）一致。
- 长度缩放**不做 1/200 量化**：它是时间量，量化单位是 1 ms（`IntegerTimeMs`，`:12185-12196`），
  用户所说的「宽度分辨率 1/200」只作用于横向。

### 6.3 拖拽手柄（新增一套，只在「多选且全是 skyarea」时出现）

| 手柄 | 位置 | 拖拽语义 |
|---|---|---|
| 左宽 | 聚合轮廓左极值边中点（吸附到实际绘制顶点） | 改组宽度，`scale` 由指针横向位置解析 |
| 右宽 | 聚合轮廓右极值边中点 | 同上 |
| 前长 | 聚合轮廓前边中点 | 改组长度，`scale` 由 `_pointerRawTime` 解析 |
| 后长 | 聚合轮廓后边中点 | 同上 |
| 移动 | 聚合轮廓前边下方 | 复用 `ApplyPositionEdit` 多目标整体平移 |

- 生成：`FalsumEditorWorld.BuildSelection` 在「全部选中都是 SkyArea 且 `Count >= 2`」时改走
  `BuildMultiSkyAreaAnchors(...)`；每个音符仍各自一个轮廓（沿用现状），聚合轮廓只用于算手柄位置。
- 抓取：`HitCenter == CanvasPosition`（A-20），半径取 `max(HitRadius, ScreenRadiusToCanvas(px) * AnchorPickSafetyScale)`
  （`AnchorPickSafetyScale = 1.35`，`FalsumEditorWorld.cs:71`）。
- **拖动状态必须装进整组**：`CreateAnchorState`（`:212-321`）现在只 `shared.SelectionIds.Add(note.Id)`
  + `AddMiddleTarget(shared, note, true)` 一个音符（`:296-297`）。组操作要改为：
  对 `_selected` 中每个 skyarea 都 `AddMiddleTarget(shared, n, ReferenceEquals(n, note))`，
  并把 `state.PressRawTime`/`HasPlanePointer` 设成按下时刻的值。
  这样移动手柄走 `ApplyPositionEdit` 时才是「整体平移」（它本来就遍历全部 Targets，`MiddleEdit.cs:2737-2792`），
  且 `ComputeMiddleEditGrid`（`:2824-2859`）会按 LCM 汇总全部目标网格。
  注意 `MiddleEditState.Phase` 必须置 `Pressed` 并在首次拖动时转 `Dragging` + `BeginChange`（与中键同构）。
- 角色：`ResolveAnchorOperation` 多选分支 → `AnchorOperation.GroupWidthScale` / `GroupTimelineScale`（§2.3）。
  新增 operation 必须同步 **6 个**消费点：枚举 `AnchorEdit.cs:57-73`、`ResolveAnchorOperation:183-210`、
  `MiddleEditModeFor:331-350`、`UpdateAnchorDrag` 的 switch `:436-453`、`AnchorHistoryLabel:936-957`、
  `ShowAnchorDragReadout` 的 switch `:872-913`。**漏一处就会 throw（`MiddleEditModeFor` 显式抛）或读数空白。**
- 组操作**不走** `_middleEditAffectsAllSelected` 偏好开关：它是给中键路径用的
  （`BuildMiddleTargets` `MiddleEdit.cs:2044-2068`），锚点路径自行构建 `Targets`。
  这样「多选缩放」不会因为用户关掉「中键影响全部选中」而失效。
- **不新造投影**：横向解析用 `TryGetPointerNormalizedOnTimePlane`（`FalsumChartEditor.cs:8138`），
  纵向用 `_pointerRawTime`（`:228`）。位置是相机与数据连续函数（A-19 判据），不做集合反推。
- 提交：`BeginChange` 一次、`CommitChange` 一次（`CommitAnchorDrag` `AnchorEdit.cs:769-823` 已有），
  提交后 `RebuildSkyAreas → 清 _skyGroupingDirty → RefreshSkyPresentation(_playheadMs)`
  （`AnchorEdit.cs:807-815` 是现成样板，B-4a′）。
- 单选时行为**完全不变**：缓动点是三档点击循环 + 拖动改缓动值（`ApplyAnchorEasingDrag` `:478-519`、
  `CycleSkyAreaEasingFromAnchor` `:675-716`、`CommitAnchorDrag:777-793`）。

---

## 7. 侧栏 UI

### 7.1 结构

- UXML：`Resources/Falsum/UI/FalsumSkyAreaBank.uxml` + USS 同目录。
- 宿主：`FalsumEditorView` 持有 `FalsumSkyAreaBankView`，构造参数仿 `FalsumChartInfoPanel`
  （`template, bankLayer, editorRoot, visualRoot, Action<string> errorSink`）。
- 层：`FalsumEditor.uxml` 在 `MenuLayer`（`:145`）/`ModalLayer`（`:148`）**之前**插入
  `<ui:VisualElement name="BankLayer" class="overlay-layer bank-layer" picking-mode="Ignore" />`。
- **不挂 `_modalLayer`、不设 `IsModalOpen`**：这样
  `FalsumChartEditor.Update` 的输入门 `if (!_view.IsTyping() && !_view.IsModalOpen)`（`:4228-4230`）
  照旧放行，谱面可编辑；而点在侧栏上的指针由 `IsPointerOverUi`
  （`FalsumEditorView.cs:4492-4509`，按 `pickingMode.Position` 向上遍历）拦住，不会穿透成谱面操作。
- 侧栏是**覆盖式浮层**：左侧贴边、宽度约 360 参考像素、可折叠；不改相机视口/投影矩形，
  因此不影响 `FalsumEditorWorld` 的拾取反投影（避免动 `TimeAtZ` 那条热路径）。

### 7.2 开合

- 顶栏最右新增 `SKYAREA BANK` 菜单按钮（`FalsumEditor.uxml` 的 `MenuCluster` `:6-11`，在 `ToolMenu` `:11` 之后）。
  点击菜单项切换侧栏；菜单条目走 `CreateToolMenuEntries()` 同构的新工厂 `CreateSkyAreaBankMenuEntries()`。
- 快捷键 `Ctrl+B`（新 `FalsumInputAction.ToggleSkyAreaBank`，§8）。
- 开合状态存 `bank.json` 的 `panel.opened`，跨会话记忆。
- `Esc`：仅当焦点在侧栏内时收栏（`KeyDownEvent` TrickleDown，仿 `FalsumChartInfoPanel.OnKeyDown:184-220`），
  不抢编辑器的全局 Esc（编辑器 `HandleKeyboard` 里 Esc 用于取消粘贴预览/退出模式）。

### 7.3 内容

```
┌ 标题 SKYAREA BANK ................ [收起 ✕]
├ 搜索框（名称 + 标签模糊，is-delayed=true）
├ 标签筛选行（库内出现过的全部标签，可多选）
├ [保存当前选择]  ← 选中段数/是否全为 skyarea 的实时状态
├ 预设卡片列表（ScrollView）
│   ├ 缩略图 96×54（Painter2D 绘制组轮廓，非 3D）
│   ├ 名称（双击改名）/ 段数 · 总时长 · 宽度跨度
│   ├ 倍率徽标 W1.00 / L1.00（点开行内数值 + 重置）
│   └ 行内按钮：[放置] [分享码] [删除]（删除二次确认）
└ 底栏：[粘贴分享码导入] [从文件导入] [导出为文件]
```

- 缩略图用 `Painter2D`（`generateVisualContent` 机制已有先例：`FalsumPerformanceWindow.cs:26/71`），
  按组内各段的 `[left,right] × [t, t+dt]` 归一化到卡片内框绘制轮廓；**不读 3D 世界、不建 mesh**。
- 列表排序：`LastUsedAtUtc` 降序 → `UpdatedAtUtc` 降序；搜索时按名称匹配优先。
- 空库：显示 `falsum.bank.empty` + 一行操作提示，不留白板。
- 数值输入（倍率）一律过 `FalsumEditorView.SanitizeNumericInput(string, allowFraction: true)`（C-2）。

### 7.4 数据流（严格单向，C-4）

- View → Editor：新增 `public event Action SaveSelectionRequested` /
  `Action<FalsumSkyAreaPreset> PlacePresetRequested` / `Action <string> ShareCodeRequested` /
  `Action<string> ImportCodeRequested` / `Action RenamePresetRequested` / … 全部 `public event`。
- Editor → View：只经门面 `SetPresets(IList<FalsumSkyAreaPreset>)` / `SetSelectionSummary(int skyCount, int total)` /
  `ShowBankStatus(string)` 推状态。
- 编辑器在 `WireView()`（`FalsumChartEditor.cs:4651`）一次性订阅。

---

## 8. 分享码

新增 `Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaShareCode.cs`。

### 8.1 格式

```
FKY1.<base64url-nopad( gzip-9( JSON ) ) >
```

JSON：

```json
{ "v": 1, "name": "...", "tags": ["..."], "author": "...",
  "cw": 1.0, "cl": 1.0,
  "segs": [ {"t":0,"dt":320,"sp":0.18,"ep":0.30,"sw":0.22,"ew":0.34,"sc":200,"ec":200,"le":0,"re":1} ] }
```

- **前缀 `FKY1.`**：类型 + 版本双保险；payload 内 `v` 再做一次版本门。
- **base64url**（`-`/`_`，去 `=`）：避免聊天软件把 `+` 当空格、`/` 当 URL 分隔符。
- **gzip**：纯数字 JSON 压缩比高，6 段典型码长约 300–600 字符，64 段上限约 3–5 KB（仍在单条消息可粘贴范围）。
- 不带缩略图（导入后本地用同一 `Painter2D` 重算），不带 `Id`/时间戳（导入端生成新 `Id`）。
- 不带 `author` 以外的身份信息、不含任何可执行内容。

### 8.2 校验与失败模式

| 情形 | 处理 |
|---|---|
| 前缀不是 `FKY1.` | `falsum.bank.error.badCode` |
| base64 解不开 | 同上 |
| gzip 解不开 / 解压后 > 64 KB | `falsum.bank.error.badCode` |
| JSON 解析失败 | `falsum.bank.error.badCode` |
| `v > 1` | `falsum.bank.error.versionTooNew`（带 `{version}`） |
| 段数 0 / > 64 / 数值越界 | `falsum.bank.error.invalidSegments` |
| 名称/标签非法字符 | 清洗后截断，不报错 |

全部失败都走 `ShowHint`/面板状态行，**不抛异常到 UI、不写半条预设**。

### 8.3 剪贴板与文件通道

- 全仓**无** `GUIUtility.systemCopyBuffer` 使用（侦察 0 匹配）→ 新增
  `Runtime/FalsumTextClipboard.cs`：`TrySet(string)` / `TryGet(out string)`，
  内部 `GUIUtility.systemCopyBuffer` + `try/catch`（某些平台/无窗口环境下会抛）。
- 另在分享码弹层里放一个**只读 `TextField`** 展示码：UITK 内可 Ctrl+A/Ctrl+C；
  此时 `IsTyping()`（`FalsumEditorView.cs:4468-4480`）为真，编辑器键盘自动让位，不会误触 Delete。
- 文件通道：`FalsumFileDialog` 新增 `OpenSkyAreaBank()` / `SaveSkyAreaBank(string suggestedName)`，
  **三段 `#if` 全部实现**（`UNITY_EDITOR` / `UNITY_STANDALONE_WIN||UNITY_EDITOR_WIN` / 其他返回 `string.Empty`，
  现状 `:126-158`）；扩展名 `.fsky`；`EnsureSkyAreaBankExtension` 仿 `EnsureFalsumExtension`（`:161-173`）。
  文件内容就是那段分享码文本（一行），便于贴吧/网盘转发。

---

## 9. 键位与本地化

### 9.1 键位（新增 1 个动作）

1. `FalsumPreferences.cs:26-53` 追加 `ToggleSkyAreaBank = 25`（**追加，不插队**：JSON 里存 int）。
2. `CreateDefaultBindings()`（`:728-758`）加 `Binding(FalsumInputAction.ToggleSkyAreaBank, KeyCode.B, Control)`；
   旧档由 `NormalizeBindings`（`:692-711`）自动补齐缺失动作，**无需写迁移**，`CurrentVersion`（`:342`）保持 7。
3. `FalsumInputRouter.ActionLabel`（`:174-232`）加 `case`（否则 `default` 漏出英文枚举名 `:229-231`）。
4. `FalsumInputPreferencesPage.cs:47-121` 分组数组手动加一行到 `display` 组（侧栏开合属显示类；
   分组数组是硬编码的，不会自动生成）。
5. 两份语言各加 `falsum.preferences.keys.action.toggleSkyAreaBank`。

### 9.2 本地化

- 全部新文案走 `L.Get`，UXML 元素一律 `<ui:Label name="l10n.falsum.bank.*" />`（文本）/
  `name="l10nt.falsum.bank.*"`（tooltip），**禁止** `text=` / `tooltip=` 字面量
  （审计第 6 项 `falsum_localization_audit.py:315-325` 会拦，含字母即违规）。
- 新增约 48 个 key（`falsum.bank.*`），两语言各一份，占位符 `{count}`/`{name}`/`{segments}` 一致。
- 审计三条同时要过：key 集合对等（`:263-271`）、占位符一致（`:273-288`）、被引用 key 存在（`:290-296`）。

---

## 10. 文件清单

### 10.1 新增（9）

| 文件 | 职责 |
|---|---|
| `Falsum/Core/FalsumSkyAreaBankModel.cs` | 预设/段模型、`Validate`、JSON 编解码 |
| `Falsum/Core/FalsumSkyAreaBankStore.cs` | `bank.json` 原子读写、损坏隔离、flush 策略 |
| `Falsum/Core/FalsumSkyAreaGroupScale.cs` | **唯一的**组宽/长缩放数学（Core 纯静态） |
| `Falsum/Core/FalsumSkyAreaShareCode.cs` | `FKY1` 编解码 + gzip + base64url + 校验 |
| `Falsum/Runtime/FalsumChartEditor.Bank.cs` | 编辑器侧：保存/放置/导入/导出/快捷键/事件回灌 |
| `Falsum/Runtime/FalsumTextClipboard.cs` | 系统剪贴板薄封装 |
| `Falsum/UI/FalsumSkyAreaBankView.cs` | 侧栏视图（列表/搜索/标签/缩略图/按钮） |
| `Falsum/Resources/Falsum/UI/FalsumSkyAreaBank.uxml` | 侧栏布局 |
| `Falsum/Resources/Falsum/UI/FalsumSkyAreaBank.uss` | 侧栏样式（圆角 0，C-7） |

### 10.2 改动（13 处，14 个文件）

| 文件 | 改动点 |
|---|---|
| `FalsumEditor.uxml` | 顶栏 `MenuCluster` 加按钮（`:6-11`）；`BankLayer` 插在 `MenuLayer`(`:145`) 前 |
| `FalsumEditorView.cs` | 顶栏绑定（`BindTopBar` `:6076-6121`）、新菜单工厂、侧栏装配、事件转发 |
| `FalsumChartEditor.cs` | `WireView()`(`:4651`) 订阅；`CommitPastePreview`(`:10214`)/`CancelPastePreview`(`:10261`) 尾部恢复剪贴板；`HandleKeyboard` 处理 `Ctrl+B` |
| `FalsumChartEditor.AnchorEdit.cs` | `ResolveAnchorOperation`(`:183-210`) 多选分支；放宽 `:133` 门；`CreateAnchorState`(`:212-321`) 装整组 Targets；新增 2 个操作 + 6 处消费点 |
| `FalsumEditorWorld.cs` | `BuildSelection`(`:1694-1797`) 多选 skyarea 分支 + `BuildMultiSkyAreaAnchors`；放宽 `:1729` 门 |
| `FalsumJson.cs` | 新增 `GetNumber`（`:67-140` 旁） |
| `FalsumPreferences.cs` | 枚举 `:26-53`、默认绑定 `:728-758` |
| `FalsumInputRouter.cs` | `ActionLabel` `:174-232` |
| `FalsumInputPreferencesPage.cs` | 分组数组 `:47-121` |
| `FalsumFileDialog.cs` | `OpenSkyAreaBank`/`SaveSkyAreaBank`(三段 `#if`) + `EnsureSkyAreaBankExtension` |
| `FalsumSelfCheck.cs` | 新增 `CheckSkyAreaBank` |
| `Localization/zh-CN.json` / `en-US.json` | 约 48 个 key |
| `Tools/anchor_offline_check.py` | 新断言（见 §11.2） |

---

## 11. 测试与验收

### 11.1 `FalsumSelfCheck.CheckSkyAreaBank`（新增，纯模型层，不碰 Unity 场景）

1. **往返**：模型 → JSON → 模型，全字段等值；未知字段被忽略；`v=2` 被拒绝。
2. **整数化与接缝**：浮点接缝的段经 `Validate+Saving` 后，全部 `TimeOffsetMs` 为整数且接缝 0；
   模拟整数平移 `timeDelta = ±N` 后接缝仍为 0（这是「放置后仍是同组」的机制断言）。
3. **宽度缩放**：`scale = 1.0` 恒等；`scale = 1.25/0.8` 后各段 `StartWidth/EndWidth` **逐位不变**；
   `PivotPos` 所在端点位置不变；量化步长为 `1/grid` 的整数倍（grid ≤ 200）。
4. **宽度上限**：构造贴边组，`ClampWidthScale` 返回使左右边界都不越 `[0,1]` 的区间；
   越界区间为空时退化为 `scale = 1.0`（不产生非法数据）。
5. **长度缩放**：边界表统一 → 接缝 0；`scale` 使某段 < 1 ms 时该段被夹到 1 ms 且总长自洽；
   负时间被拒（`ChartTimeAxis.IsNegativeMusicTime`）。
6. **分享码**：编码→解码往返等值；篡改 1 个字符 → 校验失败；截断 → 失败；
   `v=2` payload → 版本拒绝；64 段上限内成功、65 段拒绝；解压后 > 64 KB 拒绝。
7. **库上限**：第 201 条新增被拒且既有 200 条不被删除；单预设 65 段被拒。
8. **空边界**：空库加载 → 空列表 + 不抛异常；单段预设的缩放/分享码全部可用。

### 11.2 离线闸门增补（`Tools/anchor_offline_check.py` + `Tools/easing_anchor_continuity_check.py`）

- `anchor_offline_check.py`：新增第 22 组断言。脚本现有结构是
  `expect(name, condition, message)` + `method_body(source, signature)`（`:41-107`），
  因此新断言写成同一风格：取 `ResolveAnchorOperation` 方法体（签名
  `private static AnchorOperation ResolveAnchorOperation(`），断言
  ① 存在「全部 SkyArea 且 `Count >= 2`」的条件分支；
  ② 该分支返回新 operation 名；
  ③ 非多选仍然返回 `DragEasing`；
  ④ 锚点层方法体里不出现 `Math.Round` / `Mathf.RoundToInt`；
  ⑤ `GroupWidthScale` / `GroupTimelineScale` 的几何公式只出现在 `FalsumSkyAreaGroupScale.cs`。
  并按 A-18/A-20 的既有惯例做**变异测试**：把多选条件临时改成 `Count >= 1` 时闸门必须失败
  （脚本末尾打印 `selection_anchors: all offline checks passed` 的风格 `:848` 保持一致）。
- `easing_anchor_continuity_check.py`：缓动点求解路径未改动，应保持原样通过；
  若多选分支误改了 `SkyAreaSideMidpoint` 或 `ResolveVisibleProgressRange`，此闸门必须失败。
- `easing_anchor_continuity_check.py`：缓动点求解路径未改动，应保持原样通过；
  若多选分支误改了 `SkyAreaSideMidpoint` 或 `ResolveVisibleProgressRange`，此闸门必须失败。

### 11.3 门禁顺序（D-5）

1. `Unity -batchmode -quit -projectPath . -logFile Diagnostics\UnityCompile.log`
2. `Unity ... -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run` → `Diagnostics/FalsumSelfCheck.json` 无新增失败
3. `python Tools\anchor_offline_check.py`（21 组断言，A-18/A-20 的离线门）
4. `python Tools\easing_anchor_continuity_check.py`（缓动锚点连续性，A-19 的离线门）
5. `python Tools\falsum_localization_audit.py` → 退出码 0
6. 手工验收：多选整组 → 拖左右手柄 → 松开一条撤销；侧栏开着仍能放置/删除/滚轮；
   保存 → 改名 → 改倍率 → 放置 → 导出码 → 另一档导入 → 缩略图一致。

### 11.4 已知风险与对策

| 风险 | 对策 |
|---|---|
| 多选放宽单选门后，**非 skyarea 多选**也可能走到锚点生成 → 手柄出现在 tap/hold 上 | 生成侧条件严格为「全部 `NoteKind.SkyArea` 且 `Count >= 2`」，否则走原有 0 锚点路径；自检加断言 |
| 多选锚点与音符拾取区重叠，点击落到音符上（A-20 的坑） | 抓取半径走屏幕像素当量 + `AnchorPickSafetyScale`；`HandleLeftDown` 里锚点先于音符拾取（现有顺序 `AnchorEdit.cs` 已保证） |
| 侧栏覆盖左侧谱面，原点落在侧栏的拖拽会中断 | 拖拽中指针进入 UI 时按现有规则 `CancelAnchorDrag()`（`FalsumChartEditor.cs:8514-8517`），不写非法数据 |
| 宽倍率放置后 `NormalizeSkyNoteToCells` 再次量化导致接缝/位置抖动 | 只改横向、不碰时间；写回 `CellCount >= grid`（§2.4） |
| `bank.json` 被其他进程写坏 | 原子写 + 损坏隔离为 `.corrupt-*`，空库继续 |

---

## 12. 不变量对照（改之前必读的清单，逐条勾掉）

| 不变量 | 本设计如何满足 |
|---|---|
| **A-1** `groupId` 是算出来的 | 全程不写 `GroupId`；提交后 `CommitChange`/`NotifyChanged` 内部 `Normalize` 重算（`FalsumSkyAreaGrouping.cs:29-93`） |
| **A-2** `Normalize` 四个时机 | 银行只走 `BeginChange/CommitChange + NotifyChanged`，不绕过 |
| **A-3** 输出顺序决定回读 `Id` | 不直接写谱；放置走 `ChartWriter` 的既有排序（`CompareNotesForOutput` `:117-135`） |
| **A-8** 两套分母上限 | 缩放量化 `min(LCM, MaxDenominator=200)`；交互网格仍 ≤ `MaxEditableFlickGrid=192` |
| **A-9** 量化方向 `AwayFromZero` | 宽度/长度/单元格化全部 `AwayFromZero` |
| **A-13** 包原子性 | 不碰包；`bank.json` 自己走同款原子模板 |
| **A-14** 单挂起事务 | 银行放置复用 PastePreview 的单次 `BeginChange`；缩放复用 `CommitAnchorDrag` |
| **A-18** 锚点层只许委托 | 缩放数学全部在 `FalsumSkyAreaGroupScale.cs`，锚点层只有分派与调用 |
| **A-19** 端点/屏幕可见性 + 进度空间解析 | 多选手柄位置走聚合轮廓 + 吸附实际顶点；缓动点逻辑不动 |
| **A-20** 绘制中心 = 抓取中心 | 新手柄构造后不改 `CanvasPosition`；若改则同步重指 `HitCenter` |
| **A-21** 双指手势吞帧 | 不改触摸路径 |
| **B-3/B-4a** 每组一个 mesh / 组号变则整组重建 | 提交后 `RebuildSkyAreas` + 清脏 + `RefreshSkyPresentation`（B-4a′） |
| **C-1** 禁硬编码文本 | 全部 `L.Get` / `l10n.*` |
| **C-2** 数值输入过 `SanitizeNumericInput` | 倍率输入框 |
| **C-3** 具名占位符 | `{count}`/`{name}`/`{segments}` |
| **C-4** View 不持有 Editor | 只走 `public event` + `Set*` |
| **C-5** 交互 UI 归 UITK | 侧栏/分享码弹层全 UITK |
| **C-6** 风格切换不动世界 | 侧栏走既有 style sheet 变量，不引入新风格 |
| **C-7** 圆角 0 | USS 里 `border-radius: 0` |
| **C-10** UTF-8 + LF | 新文件全部 UTF-8（无 BOM 的 `.cs`），`.gitattributes` 已强制 LF |
| **D-3b** 背景名 `.png` | 无关，不触碰 |
| **D-5** 门禁顺序 | §11.3 |
| **D-8** 不清理别人工作区 | 只新增/定向修改上述文件，不 `git checkout`/`reset`/`stash` |

---

## 13. 实现顺序

详细任务分解见 `docs/superpowers/plans/2026-11-06-falsum-skyarea-bank-plan.md`（14 个任务，含写作用域与依赖图）。
顺序梗概：

1. **Core 纯静态三件**：`FalsumSkyAreaGroupScale` → `FalsumSkyAreaBankModel`（含 `FalsumJson.GetNumber`）→ `FalsumSkyAreaShareCode`。
   全部可用 `FalsumSelfCheck` 驱动，不依赖 UI 与世界，先把数学钉死。
2. **Store**：原子写 + 损坏隔离 + flush 策略。
3. **编辑器侧最小闭环**：`FalsumChartEditor.Bank.cs`（保存/放置/导入/导出），先用调试入口驱动，不加 UI。
   此步即可验证「存进去 → 放出来 → 仍是同一组」。
4. **侧栏 UI**：UXML/USS/View + 顶栏菜单 + `Ctrl+B` + 本地化 key。
5. **拖拽缩放**：`FalsumEditorWorld` 多选分支 → `ResolveAnchorOperation` 新角色 → **6 个**消费点；
   最后跑 `anchor_offline_check.py` 防回归。
6. **收尾**：自检、审计、编译门禁、手工验收单。

---

## 14. 实现期决策点（已有默认做法，不阻塞设计）

以下四项在设计中已定默认值，实现时若发现更好的做法可直接改，不需要回来重新评审：

1. 缩略图配色**固定**，不跟随 `FalsumUiStyle`（该切换功能当前对用户隐藏，`FalsumUiStyleSwitcher.Available = false`，C-6）。
2. 分享码**带** `author`（可留空），导入后只读展示，不做署名保护。
3. 列表**不做**手动排序，按 `LastUsedAtUtc` 降序自动排。
4. **不做**「最近使用」虚拟文件夹，用排序替代。
