# Skyarea Bank 实施计划

依据：`docs/superpowers/specs/2026-11-06-falsum-skyarea-bank-design.md`
（下称「spec」，条款编号沿用，如 §3、§6.1）

## 全局约定

- **行号是现状快照，不是契约**。工作区有 222 个已跟踪改动 + 182 个未跟踪文件（别人的活），行号会漂。
  实施时一律用**符号名**定位（方法名 / 字段名 / 常量名 / XAML 元素 name），
  本文里的 `path:line` 只用于缩短首次检索。
- 只做本文列出的文件；**不 `git checkout` / `reset` / `stash`**（D-8）。
- 新增 `.cs` 一律 UTF-8 无 BOM、LF（C-10）；`.gitattributes` 已强制 LF。
- 每个任务结束**必须能编译**（`Unity -batchmode -quit -projectPath . -logFile Diagnostics\UnityCompile.log`）。
  任务 1–6 全部是 Core 纯静态代码 + 自检，可以完全脱离 UI 验证——**先把数学钉死，再接 UI**。
- 所有阶段产物只写 `Diagnostics/`、`Temp/`（自检夹具用仓库根的 `Temp/FalsumSkyAreaBank`，
  与 `FalsumSelfCheck.CheckManifestSafety` 同款做法，见 `FalsumSelfCheck.cs:517`）。

## 任务依赖

```
T1(缩放数学) ─┬─> T3(模型) ─> T4(分享码) ─┐
T2(GetNumber) ┘                          ├─> T6(自检) ─> T7(编辑器侧) ─> T8(键位/文案) ─> T9(侧栏)
                              T5(Store) ─┘                                    │
                                                          T10(多选锚点生成) ─> T11(锚点角色)
                                                          T12(文件通道) ─> T14
                                                          T13(离线闸门) ─> T14
```

T1/T2 可并行；T10/T11 必须在 T7 之后（否则没有可缩放的组）。T13 依赖 T11。
**每个任务有独立的写作用域**，除 T7/T9 都要动 `FalsumChartEditor.cs` / `FalsumEditorView.cs`，这两处串行。

## 前置事实（已实测，实施时不必重查）

| # | 事实 | 证据 |
|---|---|---|
| F1 | 包条目白名单谓词是唯一「已知条目」定义，未知条目走私有 `_unknownEntries`，无公开 API | `FalsumPackage.cs` `IsKnownPackagePath`、`_unknownEntries` |
| F2 | `FalsumChartSession` 的 `CommitChange` 内部先 `Normalize` 再比对签名；`NotifyChanged` 再 `Normalize+RebuildTimeline` | `FalsumChartSession.cs` `CommitChange`/`NotifyChanged` |
| F3 | `CloneNote` / `IntegerTimeMs` / `NormalizeSkyNoteToCells` / `ClampNoteSkyBoundsPreservingWidth` / `NextNoteId` 均为编辑器私有 | `FalsumChartEditor.cs` 对应方法 |
| F4 | 多选时锚点数为 0；锚点拖动入口要求恰好单选 | `FalsumEditorWorld.cs` `BuildSelection` 的 `selected.Count == 1`；`AnchorEdit.cs` `AnchorSingleSelection` |
| F5 | 中键三个 `ApplyXxxEdit` 已遍历全部 `Targets`；`Targets` 是按下快照、`Notes` 是引用清单 | `MiddleEdit.cs` `ApplyBoundaryEdit` / `ApplyStartEndEdit` / `ApplyPositionEdit` / `AddMiddleTarget` |
| F6 | `AnchorOperation` 的取值必须在 6 处消费（枚举、`ResolveAnchorOperation`、`MiddleEditModeFor`、`UpdateAnchorDrag` switch、`AnchorHistoryLabel`、`ShowAnchorDragReadout`），`MiddleEditModeFor` 对未覆盖值**显式 throw** | `AnchorEdit.cs` |
| F7 | 全仓无系统剪贴板 API（`GUIUtility.systemCopyBuffer` 0 匹配）；`FalsumSongFont.cs` 已有 `GZipStream` 用法可参照 | grep |
| F8 | 无 Android/iOS 分支；`FalsumFileDialog` 三段 `#if`，非 Win/Editor 段返回 `string.Empty` | `FalsumPlatform.cs`、`FalsumFileDialog.cs` |
| F9 | `FalsumJson` 只有 `GetString/GetInt/GetBool/GetObject/GetArray`，**没有浮点读取**；解析器内部产出 `double` | `FalsumJson.cs` |
| F10 | 本地化审计 6 项，其中 UXML 的 `text=`/`tooltip=` 含任意字母即违规；C# 的 `text = "…"` 同理 | `Tools/falsum_localization_audit.py` |
| F11 | 新动作追加到枚举末尾即可自动补进旧档（`NormalizeBindings` 会用 `CreateDefaultBindings` 补齐缺失项） | `FalsumPreferences.cs` `CreateDefaultBindings` / `NormalizeBindings` |
| F12 | 编辑器每帧输入门只看 `IsTyping() || IsModalOpen`；`IsPointerOverUi` 按 `pickingMode.Position` 向上遍历 | `FalsumChartEditor.cs` `Update`；`FalsumEditorView.cs` `IsPointerOverUi` |
| F13 | `bank.json` 写盘要仿 `FalsumPreferencesStore.Save` 的临时文件 + `File.Replace` 模板 | `FalsumPreferencesStore.cs` |

---

## 任务 1：组缩放数学（唯一实现）

文件：`Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaGroupScale.cs`（新）

写作用域：仅此一个文件（T1 与其它任务零文件重叠，可与 T2 并行）。

步骤：

1. 纯 `System.Math`，**不引入 `UnityEngine`**（`Mathf` 一律换 `Math`，取整统一 `Math.Round(x, MidpointRounding.AwayFromZero)`，A-9）。
2. `FalsumSkyAreaGroupSnapshot` 字段：

```csharp
public sealed class FalsumSkyAreaGroupSnapshot
{
    public readonly List<ChartNote> Notes = new List<ChartNote>();
    public readonly List<double> StartPos = ...;   // 按下快照
    public readonly List<double> EndPos = ...;
    public readonly List<double> StartWidth = ...;
    public readonly List<double> EndWidth = ...;
    public readonly List<double> TimeMs = ...;
    public readonly List<double> DurationMs = ...;
    public readonly List<int> Grid = ...;          // 每段量化分母（head/tail 的 LCM，clamp 1..200）
    public double PivotPos;                        // 最早段 StartPos
    public double AnchorTimeMs;                    // 最早段 TimeMs
    public int GroupGrid;                          // 全部段 Grid 的 LCM，clamp 1..200
}
```

3. `Capture(IList<ChartNote> skyNotes) → snapshot | null`：
   - 过滤 `Kind != SkyArea` 与 `null`；为空返回 `null`；
   - 按 `(TimeMs, Id)` 升序；`PivotPos`/`AnchorTimeMs` 取排首元素；
   - 每段 `Grid = Lcm(EndpointGrid(note,false), EndpointGrid(note,true))`；
   - `EndpointGrid(note, tail)`：`declared = tail ? EndCellCount : StartCellCount`，
     `declared > 0` 就用它，否则用 `FalsumFraction.NormalizePair(pos, width, 1, ...)` 推出来的分母
     （与 `MiddleEdit.ResolveMiddleEndpointGrid` 同一判定顺序，**但不调用 Runtime**），最后 `Clamp(1, 200)`。
   - `GroupGrid = Lcm(全部 Grid)`，`Clamp(1, 200)`。
4. `WidthScaleRange(snapshot, out minimum, out maximum) → bool`：
   解 `scale` 的合法闭区间，使**每个端点**满足 `c(s) − w/2 >= 0` 且 `c(s) + w/2 <= 1`，
   其中 `c(s) = PivotPos + d * s`、`d = c0 − PivotPos`、`h = w/2`：

   | `d` | 来源约束 | 对 `s` 的约束 |
   |---|---|---|
   | `d > 0` | `P + d*s − h >= 0` | `s >= (h − P)/d` |
   | `d > 0` | `P + d*s + h <= 1` | `s <= (1 − P − h)/d` |
   | `d < 0` | `P + d*s − h >= 0` | `s <= (h − P)/d`（除数翻号） |
   | `d < 0` | `P + d*s + h <= 1` | `s >= (1 − P − h)/d`（除数翻号） |
   | `d == 0` | 与 `s` 无关 | 直接判 `[P−h, P+h] ⊆ [0,1]`，否则返回 `false` |

   （`P` = `PivotPos`，此时该端点自身位置不随 `s` 变化，只能整体判合法/非法。）
   - 所有端点区间取交集；交集为空、任一 `h > 0.5`、或任一端点 `d == 0` 且原位置越界 →
     返回 `false`（调用方退化为 `scale = 1.0`，**不写非法数据**）。
5. `TryApplyWidthScale(snapshot, double scale, bool clampToBounds)`：
   - `clampToBounds` 时先用 `WidthScaleRange` 夹 `scale`；`range` 不可用则 `scale` 回 1.0；
   - 对每段两端点：`Quantize(PivotPos + (c0 − PivotPos) * scale, GroupGrid)`；
   - **`StartWidth` / `EndWidth` 一律回写快照原值**（用户确认的语义：横向占位缩放、粗细不变）；
   - 写回 `note.StartCellCount = note.EndCellCount = Max(原值, GroupGrid)`（防写谱二次量化丢精度，spec §2.4）。
6. `ApplyTimelineScale(snapshot, double scale)`：
   - 收集全部 `TimeMs` 与 `EndTimeMs` 为**去重排序边界表**；
   - `newB = AnchorTimeMs + (b − AnchorTimeMs) * scale`，统一 `IntegerTimeMs` 等价的
     `Math.Max(0, Round(x, AwayFromZero))`；
   - 回写 `note.TimeMs = newB[startIndex]`、`note.DurationMs = Max(1.0, newB[endIndex] − note.TimeMs)`；
   - 段长下限 `1.0`（与 `MiddleMinimumDurationMs` 一致）。
7. `ApplyStoredScales(snapshot, widthScale, lengthScale, clampToBounds)`：先宽后长，各调一次上面的方法。
8. 常量：`MinimumTimelineScale = 0.1`、`MaximumTimelineScale = 10.0`、`MinimumWidthScale = 0.02`、`MaximumWidthScale = 50.0`
   （宽度的真实上限由越界约束决定，这两个只是数值卫生）。
9. 所有入口对 `NaN`/`Infinity`/`null` 早退并返回 `false`，**绝不把非法值写进 `ChartNote`**。

验证：

- `CheckSkyAreaBank`（任务 6）覆盖：`scale = 1.0` 恒等、粗细不变、`PivotPos` 端点不动、
  `1/GroupGrid` 步进、越界组退化、长度接缝 0、段长下限。
- 编译通过。

## 任务 2：`FalsumJson` 浮点读取

文件：`Assets/InFalsus/Gameplay/Falsum/Core/FalsumJson.cs`

步骤：

1. 在 `GetInt`/`GetBool` 旁新增：

```csharp
public static double GetNumber(FalsumJsonObject source, string key, double fallback)
```

2. 规则与 `GetInt` 一致：缺键或 `null` 返回 `fallback`；`int`/`long`/`double` 都接受；
   其它类型抛 `FormatException("JSON field '…' must be a number.")`；
   `NaN`/`Inf` 抛（解析器已保证不会从文本产生，但手工构造的对象可以）。
3. 不改 `Serialize` 的数字格式（`ChartWriter`/`song.json` 的字节稳定性依赖它）。

验证：

- `CheckJson` 里追加：`GetNumber` 读整数/小数/缺键三例；非法类型抛 `FormatException`。
- 既有 `CheckJson`、`CheckChartRoundTrip` 仍然通过。

## 任务 3：预设模型

文件：`Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankModel.cs`（新）

步骤：

1. `FalsumSkyAreaPresetSegment`：`TimeOffsetMs / DurationMs / StartPos / EndPos / StartWidth / EndWidth /
   StartCellCount / EndCellCount / LeftEasing / RightEasing`（spec §3）。
2. `FalsumSkyAreaPreset`：`Id / Name / Tags / Author / WidthScale / LengthScale /
   CreatedAtUtc / UpdatedAtUtc / LastUsedAtUtc / Segments`。
3. 常量：
   `MaxSegmentsPerPreset = 64`、`MaxPresets = 200`、`MaxNameLength = 48`、`MaxTags = 8`、
   `MaxTagLength = 16`、`MaxAuthorLength = 32`、`MinScale = 0.25`、`MaxScale = 4.0`、`CurrentVersion = 1`。
4. `public static FalsumSkyAreaPreset FromSelection(IList<ChartNote> skyNotes, string name)`：
   - `Capture` → 排序 → 首段为基准算 `TimeOffsetMs`；
   - 每段 `TimeOffsetMs`/`DurationMs` 过整数化，接缝归零（`t[i+1] = t[i] + dt[i]`）；
   - 复用任务 1 的快照做数据来源，**不重复写排序逻辑**；
   - `Tags` 空、`WidthScale/LengthScale = 1.0`、`Id = "p-" + Guid.NewGuid().ToString("N")`。
5. `public static bool Validate(FalsumSkyAreaPreset, out string errorKey)`：覆盖 spec §3 的全部约束
   （段数 1..64、首段偏移 0、`DurationMs >= 1`、`0..1` 边界、每段 `pos ± w/2` 不越界、
   easing 0..2、cell 1..200、名/标签/作者清洗与长度、`Scale ∈ [0.25, 4.0]`）。
   错误以**本地化 key** 返回（`falsum.bank.error.*`），不在 Core 拼文案（C-1）。
6. `ToJson(preset) → FalsumJsonObject` / `FromJson(FalsumJsonObject, out preset, out errorKey)`：
   字段名按 spec §3 的短名；未知键忽略；`v > CurrentVersion` 返回 `falsum.bank.error.versionTooNew`。
7. `SanitizeText(string, int maxLength)`：去首尾空白、剔除 `< 0x20`（保留无）、剔除 `\r\n`、截断。

验证：

- `CheckSkyAreaBank` 覆盖往返、未知字段忽略、`v=2` 拒绝、各约束边界、65 段拒绝。
- 编译通过。

## 任务 4：分享码

文件：`Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaShareCode.cs`（新）

步骤：

1. `public const string Prefix = "FKY1.";`
2. `Encode(FalsumSkyAreaPreset) → string`：
   - `ToJson` → 紧凑 `FalsumJson.Serialize(obj, pretty:false)`；
   - 追加校验行：`"\n#crc32:<8 位大写 hex>"`，CRC32 计算**未压缩 JSON 文本的 UTF-8 字节**
     （自实现查表或按位实现，不引入依赖）；
   - `GZipStream(CompressionLevel.Optimal)` 压缩整串 → `Base64UrlNoPad`（`+`→`-`、`/`→`_`、去 `=`）；
   - 输出 `Prefix + b64`。
3. `TryDecode(string, out preset, out errorKey) → bool`：
   - 去首尾空白/换行；前缀不符 → `falsum.bank.error.badCode`；
   - base64url 反转 → 解压，**解压总量上限 64 KB**（边读边累计，超限立即失败）；
   - 拆最后一行 `#crc32:`，校验不符 → `badCode`；
   - `FalsumJson.Parse` + `FromJson`；任何异常统一捕获成 `badCode`（**不向 UI 抛**）。
4. 上限保护：`Encode` 前先 `Validate`；`TryDecode` 结束时再过一次 `Validate`。
5. 纯 `System`（`System.IO.Compression`、`System.Text`），不依赖 Unity。

验证：

- `CheckSkyAreaBank` 覆盖：往返等值、改 1 字符失败、截断失败、`v=2` 版本拒绝、
  64 段成功 / 65 段拒绝、解压 > 64 KB 拒绝、前后缀非法字符拒绝。

## 任务 5：库存储

文件：`Assets/InFalsus/Gameplay/Falsum/Core/FalsumSkyAreaBankStore.cs`（新）

步骤：

1. `DirectoryPath` = `Path.Combine(Application.persistentDataPath, "Falsum", "SkyAreaBank")`；
   `FilePath` = `<DirectoryPath>/bank.json`。`Application.persistentDataPath` 是无条件可用（F8 结论）。
2. `Load() → FalsumSkyAreaBankDocument`（含 `List<FalsumSkyAreaPreset> Presets` 与 `bool PanelOpened`）：
   - 文件不存在 → 空文档；
   - 解析/校验失败 → `PreserveCorruptFile`（复制成 `bank.json.corrupt-yyyyMMdd-HHmmss-fff`）
     → 返回空文档 + `Warning`。**绝不阻塞编辑器启动**。
3. `Save(document)`：完全照抄 `FalsumPreferencesStore.Save` 的原子模板
   （`.tmp-<guid>` → `File.Replace` → 失败 `Copy + Delete` → 无旧文件 `Move` → `finally` 删 tmp）。
   内容用 `FalsumJson.Serialize(..., pretty:true)`，UTF-8 无 BOM。
4. `LastUsedAtUtc` 之类的**高频字段改动不立即落盘**：暴露 `MarkDirty()` + `Flush()`，
   由面板关闭 / 难度切换 / 编辑器销毁统一 flush；`Save` 只给新增/改名/删除/导入这类结构变更用。
5. `AddOrReplace(preset, out errorKey) → bool`：库上限 200 时**只拒绝新增**，不删除既有（spec §3）。
6. 提供给自检用的注入路径重载：`SaveTo(string path)` / `LoadFrom(string path)`，
   自检用它写 `Temp/FalsumSkyAreaBank`，不碰用户库。

验证：

- `CheckSkyAreaBank` 覆盖：写入→读取往返；损坏文件被隔离且返回空库；
  `.tmp` 残留被清理；第 201 条被拒且既有 200 条完好。

## 任务 6：自检 `CheckSkyAreaBank`

文件：`Assets/InFalsus/Editor/FalsumSelfCheck.cs`

步骤：

1. 在 `Run()` 的 `List<string> checks` 序列里（现有 `CheckSkyAreaBank` 之前的位置，紧跟 `CheckSkyAreaGrouping`）
   调用 `CheckSkyAreaBank(checks);` —— 顺序与 spec §11.1 对应。
2. 新增 `private static void CheckSkyAreaBank(List<string> checks)`，按 spec §11.1 的 8 组断言实现：
   ① 模型往返/未知字段/版本门；② 整数化与接缝（含 ±N 整数平移后接缝仍 0）；
   ③ 宽度缩放（缩放 1.0 恒等、1.25/0.8 后粗细逐位不变、Pivot 端点不动、步长为 1/GroupGrid）；
   ④ 宽度越界（`WidthScaleRange` 边界、退化）；⑤ 长度缩放（接缝 0、段长下限、负时间拒绝）；
   ⑥ 分享码（往返/篡改/截断/版本/段数/体积）；⑦ 库上限；⑧ 空边界。
3. 末尾 `checks.Add("skyarea_bank");`（失败用 `Require(...)` 抛，沿用现有风格）。
4. **不新增 Unity 场景依赖、不打开场景**（D-6）。

验证：

- `Unity ... -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run` → `Diagnostics/FalsumSelfCheck.json`
  的 `checks` 里出现 `skyarea_bank`。
- 既有已知失败（`CheckToolRuntime` 的 design rule tool description）**不是本任务引入**，
  记录为「本任务前后同样失败」，不要顺手改它。

## 任务 7：编辑器侧银行逻辑

文件：

- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.Bank.cs`（新）
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumTextClipboard.cs`（新）
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs`（改）

步骤：

1. `FalsumTextClipboard`：`TrySet(string) → bool`、`TryGet(out string) → bool`，
   内部 `try { GUIUtility.systemCopyBuffer = ...; } catch`；空字符串视为失败（不覆盖用户的剪贴板）。
2. `FalsumChartEditor.Bank.cs`（`partial class`）新增：
   - 字段：`_bankStore`、`_bankDocument`、`_bankClipboardBackup`（`List<ClipboardNote>`，复用既有私有类型
     `ClipboardNote`）、`_bankPlacementPending`、`_bankPendingPreset`、`_bankPanelOpen`。
   - `InitializeBank()`（由 `WireView()` 尾部调用一次）：`_bankStore.Load()` → 推给 View。
   - `SaveSelectionToBank(string name, IList<string> tags)`：收集 `_selected` 中全部 `SkyArea` →
     `FalsumSkyAreaPreset.FromSelection` → `Validate` → `_bankStore.AddOrReplace` → 刷 View →
     `ShowHint("falsum.bank.hint.saved")`。**不 `BeginChange`**（不改谱面）。
   - `BeginPlacePreset(preset)`：
     ① `if (_mode == EditorMode.PastePreview) CancelPastePreview();`
     ② `CaptureBankClipboard()`（`_bankClipboardBackup` ← 当前 `_clipboard`，逐项 `CloneNote`）；
     ③ `FalsumSkyAreaGroupScale.Capture(临时克隆段)` → `ApplyStoredScales(widthScale, lengthScale, clamp:true)`；
     ④ 装入 `_clipboard`（`TimeOffsetMs` 用相对首段的偏移）；
     ⑤ `_bankPlacementPending = true; _bankPendingPreset = preset;`
     ⑥ `PasteClipboard();`（既有方法，不复制）。
   - `RestoreBankClipboard()`：**幂等**（`!_bankPlacementPending` 直接 return）→ 还原 `_clipboard`、
     清 `_bankPlacementPending`、`_bankPendingPreset = null`
     → `_bankDocument` 里该预设 `LastUsedAtUtc = now`、`_bankStore.MarkDirty()` → 刷 View。
   - `ImportShareCode(string)` / `ExportShareCode(preset) → string`：包 `FalsumSkyAreaShareCode`，
     失败把 `errorKey` 交给 View 状态行（`ShowHint` 或面板内提示）。
   - `ExportPresetToFile(preset)` / `ImportPresetFromFile(path)`：走 `FalsumFileDialog`（任务 12），
     文件内容就是分享码单行文本。
   - `ToggleBankPanel()` / `SetBankPanelOpen(bool)`：只切 View 可见性 + `PanelOpened` 标记，
     **不 `SetMode`、不暂停播放、不进模态**。
   - 撤销标签：银行放置后 `BeginChange` 已由 `PasteClipboard` 内部用 `falsum.history.pasteObjects`；
     若要可辨识来源，在 `CommitPastePreview` 里当 `_bankPlacementPending` 为真时改用
     `falsum.history.bankPlace`（需在 `_session.BeginChange` 处取标签，注意不要改通用粘贴路径的标签）。
3. 在 `FalsumChartEditor.cs` 里**只加 4 个钩子**（其余逻辑在 partial 里）：
   - `CommitPastePreview()` 末尾：`if (_bankPlacementPending) RestoreBankClipboard();`
   - `CancelPastePreview()` 末尾：同上（取消也要还原，否则用户剪贴板永远被银行占用）；
   - `ClearPastePreviewState()` 内：同上（防御性）；
   - `WireView()` 末尾：`InitializeBank();`
   - `HandleKeyboard()` 里 `Ctrl+B`：`ToggleBankPanel();`（放在 `_package == null` 分支之前，
     允许无谱面时开关侧栏）。
4. **不要**在 `_clipboard.Clear()` 的三处（`CopySelection` / 调试入口等）加恢复逻辑：
   那是用户自己按 Ctrl+C，覆盖是正确行为；恢复只发生在「银行放置结束」这一刻，由步骤 3 的四个钩子覆盖。

验证：

- 编译通过。
- 手工：打开包 → 选中 2+ skyarea → 保存 → 放置 → **Ctrl+V 仍是用户原来的剪贴板内容**（不是 skyarea）。
- 手工：银行放置途中右击取消 → 剪贴板同样还原、谱面无变化。
- 手工：放置后 `Undo` 一次回到放置前（一条撤销记录）。

## 任务 8：键位与本地化

文件：

- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumInputRouter.cs`
- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumInputPreferencesPage.cs`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Localization/zh-CN.json`
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/Localization/en-US.json`

步骤：

1. `FalsumPreferences`：`FalsumInputAction` **末尾追加** `ToggleSkyAreaBank = 25`（不插队，JSON 存 int）；
   `CreateDefaultBindings()` 加 `Binding(FalsumInputAction.ToggleSkyAreaBank, KeyCode.B, Control)`。
   **不改 `CurrentVersion`**（F11：旧档由 `NormalizeBindings` 自动补齐）。
2. `FalsumInputRouter.ActionLabel` 加 `case`（否则 `default` 漏出英文枚举名）。
3. `FalsumInputPreferencesPage` 的 `display` 分组数组加一行（数组是硬编码的，不自动生成）。
4. 两份语言各加 `falsum.preferences.keys.action.toggleSkyAreaBank`。
5. 新增 `falsum.bank.*` 约 48 个 key（两语言**同批**加，占位符一致）：
   面板标题/搜索占位/标签筛选/保存按钮/空库/卡片元信息（段数、时长、跨度）/倍率重置/
   放置/分享码/删除/导入导出按钮/各 `falsum.bank.hint.*`/各 `falsum.bank.error.*`/`falsum.history.bankPlace`。
6. UXML 一律 `name="l10n.falsum.bank.*"` / `name="l10nt.falsum.bank.*"`，**禁止** `text=`/`tooltip=` 字面量（F10）。

验证：

- `python Tools\falsum_localization_audit.py` → 退出码 0（三项：key 对等、占位符一致、引用存在）。
- 新档与旧档（手工把 `preferences.json` 的 Bindings 删一条）启动后 `Ctrl+B` 都可用。

## 任务 9：侧栏 UI

文件：

- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumSkyAreaBank.uxml`（新）
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumSkyAreaBank.uss`（新）
- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumSkyAreaBankView.cs`（新）
- `Assets/InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumEditor.uxml`（改）
- `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs`（改）

步骤：

1. `FalsumEditor.uxml`：
   - `MenuCluster` 内 `ToolMenu` 之后加 `<ui:Button name="SkyAreaBankMenu" class="menu-button" />`；
   - 在 `MenuLayer` **之前**插入 `<ui:VisualElement name="BankLayer" class="overlay-layer bank-layer" picking-mode="Ignore" />`。
     位置在 `MenuLayer` 之前意味着菜单/模态会盖在侧栏上，符合「补全式」层级直觉。
2. `FalsumSkyAreaBank.uxml` 结构（spec §7.3）：`BankPanel`（贴左、宽 360、`picking-mode="Position"`）→
   head（标题 `l10n.falsum.bank.title` + 收起按钮）→ 搜索 `TextField`（`is-delayed="true"`）→
   标签行（`BankTagRow`，动态生成 `Button`）→ `SaveSelection` 按钮 + `SelectionSummary` Label →
   `BankList`（`ScrollView`，卡片由 `FalsumSkyAreaBankCard.uxml` 或代码构建）→ 底栏三个按钮。
   `BankPanel` 的所有可见文案走 `l10n.*`；动态生成按钮的文案走 `L.Get`（不是 UXML）。
3. `FalsumSkyAreaBankView`：
   - 构造 `(VisualTreeAsset template, VisualElement bankLayer, Action<string> statusSink)`；
   - `Show() / Hide() / bool IsOpen`；`SetPresets(IList<...>)`、`SetSelectionSummary(int skyAreaCount, int total)`、
     `ShowStatus(string)`、`ShowShareCode(string code)`（弹层里放只读 `TextField` + 复制按钮）；
   - 卡片缩略图：`VisualElement.generateVisualContent += ctx => DrawThumb(ctx, preset)`，
     用 `ctx.painter2D` 按各段 `[left,right] × [t,t+dt]` 归一化到 96×54 内框（spec §7.3），
     **不读 3D 世界、不建 mesh**；
   - 搜索：名称 + 标签模糊（`OrdinalIgnoreCase`），标签筛选多选；排序 `LastUsedAtUtc` → `UpdatedAtUtc`；
   - 倍率输入（行内）过 `FalsumEditorView.SanitizeNumericInput(str, allowFraction:true)`；
   - 全部交互以 `public event` 出（`SaveSelectionRequested / PlacePresetRequested(preset) /
     ShareCodeRequested(preset) / ImportCodeRequested(string) / RenamePresetRequested(preset, name) /
     DeletePresetRequested(preset) / SetPresetScaleRequested(preset, w, l) / ImportFileRequested /
     ExportFileRequested(preset) / CloseRequested`）。
4. `FalsumEditorView`：
   - `LoadTemplates()` 加 `_skyAreaBankTemplate = RequireTemplate("Falsum/UI/FalsumSkyAreaBank")`；
   - `QueryMainElements()` 取 `BankLayer`；
   - 构造 `_skyAreaBankView`（在 `FalsumToolReportView` 装配处附近）；
   - `BindTopBar()` 加 `_skyAreaBankMenuButton.clicked += ...`：
     策略与其它菜单一致（`ToggleMenu` + `CreateSkyAreaBankMenuEntries()`），
     但**条目只有两项**（打开/关闭面板、粘贴分享码导入），其余命令都在面板内；
   - 转发 View 的 8 个事件为 `FalsumEditorView` 的 `public event`（C-4），编辑器在 `WireView()` 订阅；
   - `Esc` 处理：**仅当焦点在 `BankPanel` 内**时收栏（在 `OnMenuKeyDown` 同层加一个
     `BankPanel.RegisterCallback<KeyDownEvent>(..., TrickleDown)`，不碰全局 Esc）。
5. `FalsumSkyAreaBank.uss`：圆角 0（C-7）、贴左 `position:absolute; left:0; top:0; bottom:0;`、
   宽 360、`display` 由隐藏类切换；卡片间距与描边沿用 `FalsumEditor.uss` 的既有变量。

验证：

- 打开侧栏后**谱面仍可操作**：拖音符、放置、滚轮改时间、右键径向菜单都正常。
- 点在面板上的点击**不会**落到谱面（`IsPointerOverUi` 拦截）。
- 面板打开时聚焦搜索框 → 按 Delete/空格 不会删音符/播放（`IsTyping()` 早退）。
- 面板外按 Esc 行为不变（取消粘贴预览/退出模式）。

## 任务 10：多选天空组锚点生成

文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorWorld.cs`

步骤：

1. `BuildSelection(nowMs, selected)`：
   - 现有 `anchorNote = selected.Count == 1 ? selected[0] : null` 保留；
   - 新增分支：`selected` 全为 `SkyArea` 且 `Count >= 2` → `TryBuildMultiSkyAreaAnchors(selected, nowMs, anchors)`；
   - 其余情况（含 tap/hold 混合多选）**维持现状：0 个锚点**（spec §11.4 风险首条）。
2. `BuildMultiSkyAreaAnchors`：
   - 收集每段投影多边形（沿用 `TryBuildSelectionScreenPolygon`）→ 聚合包围盒
     `minX/maxX/minY/maxY`（只看屏幕可见段；全不可见则返回 0 锚点）；
   - 5 个锚点：左宽（`minX` 侧边区中点）、右宽（`maxX`）、前长（`minY` 边中点）、后长（`maxY`）、移动（前边下方 `AnchorOffsetBase`）；
   - 边中点 / 角点**吸附到实际绘制顶点**（沿用 A-19 `MatchCornerToBox` 的做法：窗口内取最近顶点，
     超出窗口沿投影方向落到轮廓上）；
   - `CanvasPosition` 与 `HitCenter` 同值（A-20）；`GlyphRadius` 走 `AnchorMinGlyph..AnchorMaxGlyph`，
     `HitRadius` 由 `ScreenRadiusToCanvas(px) * AnchorPickSafetyScale` 决定（生成后**不再改** `CanvasPosition`）。
3. 新增 `FalsumSelectionAnchor` 使用既有 `Kind` 枚举：宽/长手柄复用 `Boundary`（左右）与 `StartEnd`（前后），
   靠 `AnchorEdit` 的「多选分支」区分语义，**不新增 `FalsumAnchorKind`**（少改一处公开枚举，也避免 UI 绘制层多一个分支）。
4. `hasFront/hasBack` 对组级手柄不适用：只要聚合盒可见就都给 5 个；不可见段判据仍按 A-19 的屏幕矩形。

验证：

- 多选 2 段 skyarea → 出现 5 个手柄；多选 1 段 → 仍是原有 9 个（行为不变）。
- 多选 tap + skyarea → 0 个手柄（回归）。
- 手柄画出位置与抓取位置一致（点哪拖哪）。

## 任务 11：锚点角色与整组拖动状态

文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AnchorEdit.cs`

步骤：

1. `AnchorOperation` 追加两项：`GroupWidthScale`、`GroupTimelineScale`（**6 处消费点全部同步**，F6）：
   ① 枚举；② `ResolveAnchorOperation`；③ `MiddleEditModeFor`（两者都抛/或返回 `Boundary`——按 F6 的约定，
   未走中键共享体的操作**必须在 `MiddleEditModeFor` 显式 throw**，与 `DragEasing`/`FlipFlickColor` 同款）；
   ④ `UpdateAnchorDrag` 的 `switch`；⑤ `AnchorHistoryLabel`；⑥ `ShowAnchorDragReadout`。
2. `ResolveAnchorOperation` 的 `case Easing` / `case StartEnd` 加多选分支：
   - 条件抽成 `private bool IsSkyAreaGroupSelection()`：`_selected.Count >= 2` 且每个都是 `NoteKind.SkyArea`；
   - `Easing` → 多选返回 `GroupWidthScale`，否则 `DragEasing`；
   - `StartEnd` → 多选返回 `GroupTimelineScale`，否则原逻辑（skyarea→`EndpointLateral`，其他→`StartEndTime`）；
   - 因此该方法要从 `static` 改为实例方法（`TryBeginAnchorDrag` 是唯一调用点，F6）。
3. 放宽单选门 `TryBeginAnchorDrag` 第一道判断：`AnchorSingleSelection()` 为 null 时，
   若 `IsSkyAreaGroupSelection()` 为真则继续（用聚合锚点），否则原样返回 `false`。
   注意该门后面还有 `_view.IsModalOpen || _view.IsTyping() || 播放中 || _session.HasPendingEdit`，**一个都不能放宽**。
4. 组操作的状态构建（`CreateAnchorState` 新增分支）：
   - `state = new MiddleEditState { Phase = Pressed, Side = Left, TimeSide = Head, ActiveNote = 首段 }`；
   - `state.ProjectionTimeMs = 组首段 TimeMs`；`HasPlanePointer` 用 `TryGetPointerNormalizedOnTimePlane` 试一次；
   - `state.PressRawTime = _pointerRawTime`；
   - **对 `_selected` 每个 skyarea 调 `AddMiddleTarget(shared, n, ReferenceEquals(n, 首段))`**，
     于是 `Targets` / `Notes` / `SelectionIds` 都是整组，`ComputeMiddleEditGrid` 会 LCM 汇总；
   - `AnchorDragState` 额外持有 `FalsumSkyAreaGroupSnapshot GroupSnapshot`、
     `float PressPlaneX`、`double PressRawTime`、`AnchorOperation`。
5. `UpdateAnchorDrag` 的两个新分支：
   - **两个宽度手柄映射到同一个标量**（左右手柄只是抓取位置不同，语义都是「整组横向占位」）：
     `rawScale = (currentPlaneX − PivotPos) / (pressPlaneX − PivotPos)`；
     左手柄的 `pressPlaneX` 接近聚合左边界、右手柄接近右边界，因此「左手柄向左拖 = 变宽」
     「右手柄向右拖 = 变宽」分别成立；`PivotPos` 通常落在组内，两个分母同号，公式无需分支。
   - 守卫：`|pressPlaneX − PivotPos| < 1/GroupGrid` 时**不写数据**并提示「离基准太近」；
     否则 `FalsumSkyAreaGroupScale.TryApplyWidthScale(snapshot, rawScale, clamp:!_disableOutOfBoundsClamping)`；
   - `GroupTimelineScale`：`rawScale = (pointerRawTime − AnchorTime)/(pressRawTime − AnchorTime)`
     （`pressRawTime − AnchorTime` 为 0 时早退）；`Clamp(MinimumTimelineScale, MaximumTimelineScale)`
     后 `ApplyTimelineScale(snapshot, rawScale)`；
   - 两个分支都只在 `Phase == Dragging` 后写（首次进入 `Dragging` 时 `BeginChange`，与中键同构，
     保证一次拖动 = 一条撤销）。
   - **拖动中不重算 `snapshot`**：始终基于按下快照写绝对值，否则逐帧累乘会漂移（与 `ApplyPositionEdit`
     用 `target.PressXxx` 做基准的理由相同）。
6. `CommitAnchorDrag` / `CancelAnchorDrag`：
   - 新操作提交时沿用现有收尾：`CommitChange → NotifyChanged → RefreshNoteVisuals(state.Notes) →
     RebuildSkyAreas → 清 _skyGroupingDirty → RefreshSkyPresentation(_playheadMs)`；
   - 取消时 `_session.CancelChange()` 并从 `GroupSnapshot` **回滚**整组（新操作必须显式回滚，
     中键路径靠 `ReplaceChart`/快照机制，锚点路径过去只改单音符所以不需要，现在需要）。
7. `ShowAnchorDragReadout` 显示：宽度 `W ×1.42 / 100%`、长度 `L ×1.42`；
   `AnchorHistoryLabel` 返回新 key `falsum.history.skyAreaGroupScale`（两语言补）。

验证：

- 单选 skyarea 拖缓动点 → 行为与改前**逐位一致**（回归）。
- 多选整组拖左右手柄 → 各段粗细不变、整组横向占位缩放、松开一条撤销、`Undo` 后逐位还原。
- 拖到边界 → 整组统一夹取，组内相对结构不变（不是逐段贴边）。
- 拖前后手柄 → 接缝仍 0，播放/渲染时组仍是一个 mesh（`NoteRenderer` 的 `SkyVisual` 组数不变）。
- `python Tools\anchor_offline_check.py` 通过（旧 21 组断言不得退化）。

## 任务 12：文件导入导出

文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumFileDialog.cs`

步骤：

1. 三段 `#if` 各加一对（F8）：
   - `public static string OpenSkyAreaBank()`：过滤器 `.fsky`；
   - `public static string SaveSkyAreaBank(string suggestedName)`：默认扩展名 `.fsky`；
   - 非 Win/Editor 段返回 `string.Empty`（与现状一致）。
2. `EnsureSkyAreaBankExtension(string path)`：仿 `EnsureFalsumExtension`，非 `.fsky` 就 `Path.ChangeExtension`。
3. 文件读写放在 `FalsumChartEditor.Bank.cs`（`File.ReadAllText` / `WriteAllText`，UTF-8 无 BOM），
   **对话框类只负责选路径**（与现有 `OpenFalsum/SaveFalsum` 的分工一致）。

验证：

- 导出 → 磁盘出现 `.fsky` → 另一份档导入 → 预设一致（段数/名称/倍率/缩略图）。
- 非法文件内容 → 状态行报 `falsum.bank.error.badCode`，不入库。

## 任务 13：离线闸门

文件：`Tools/anchor_offline_check.py`

步骤：

1. 新增第 22 组断言（沿用脚本的 `expect(name, condition, message)` + `method_body(source, signature)` 风格）：
   - `ResolveAnchorOperation` 里有「全部 SkyArea 且 `Count >= 2`」条件与两个新操作名；
   - 非多选仍返回 `DragEasing`；
   - `AnchorEdit.cs` 方法体里不出现 `Math.Round` / `Mathf.RoundToInt`（A-18 不得因新操作破功）；
   - `FalsumSkyAreaGroupScale` 的几何公式**只在** Core 文件里，锚点层只调用。
2. 变异测试：把脚本读到的多选条件临时替换成 `Count >= 1` 时，闸门必须失败（脚本内自测，不入库）。
3. 末尾打印风格与现有一致（`selection_anchors: all offline checks passed`）。

验证：

- `python Tools\anchor_offline_check.py` 通过；
- `python Tools\easing_anchor_continuity_check.py` 仍通过（缓动点求解未被改动）。

## 任务 14：门禁与手工验收

1. 编译：`Unity -batchmode -quit -projectPath . -logFile Diagnostics\UnityCompile.log`
2. 自检：`Unity ... -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run` →
   `Diagnostics/FalsumSelfCheck.json`，`checks` 含 `skyarea_bank`，失败项**不新增**（既有 `CheckToolRuntime` 失败除外）。
3. `python Tools\anchor_offline_check.py`
4. `python Tools\easing_anchor_continuity_check.py`
5. `python Tools\falsum_localization_audit.py` → 退出码 0
6. 手工验收单：
   - 保存 2 段组 → 侧栏出现卡片（缩略图非空）→ 改名 → 打 2 个标签 → 搜索命中；
   - 改倍率 1.30 → 放置 → 落点宽度比原组宽、段数不变、一条撤销；
   - 分享码：复制 → 清空库（临时改目录）→ 粘贴导入 → 预览确认 → 卡片一致；
   - 多选整组 → 拖左右手柄 → 粗细不变 → 拖前后手柄 → 接缝不断（渲染仍是一条链）；
   - 侧栏开着：放 tap、删音符、滚轮、右键径向菜单全部正常；
   - 面板外 Esc、面板内 Esc 两种行为正确。

## 回滚方案

- 功能可见性开关：`FalsumSkyAreaBankView.Available = true`（仿 `FalsumUiStyleSwitcher.Available` 的既有先例）。
  置 `false` → 顶栏按钮隐藏、`Ctrl+B` 无效、面板不装配；Core 与自检保留，不影响其他功能。
- 锚点扩展是唯一有回归风险的改动：若不通过 `anchor_offline_check.py`，先只回滚任务 10/11，
  保留 T1–T9（银行本身的保存/放置/分享码仍可用），缩放退回「不能拖手柄」。

## 验收标准（对应 spec §11.3）

- [ ] 编译零新增错误
- [ ] `skyarea_bank` 自检项存在且通过
- [ ] `anchor_offline_check.py`、`easing_anchor_continuity_check.py` 通过
- [ ] `falsum_localization_audit.py` 退出码 0
- [ ] 手工验收单 6 项全过
- [ ] 未触碰：`Assets/InFalsus/Recovered/`、`FalsumPackage` 白名单、`ChartWriter` 时间轴换算、他人未提交改动
