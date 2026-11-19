# P0 实施计划：FALSUM 首选项 v6 数据层（带 Advanced 兼容门面）

依据：`docs/superpowers/specs/2026-11-03-falsum-preferences-restructure-design.md`
决策：采用方案 **A** —— P0 加兼容门面，独立合入；门面在 P4 下线。

## P0 范围

**做**：
- v6 字段结构（`Grid` / `Experimental` 新组，`Editing` 吸收一个字段）
- `Advanced` 兼容门面（非序列化属性）
- legacy 单超集 DTO + `Migration.FromDisk`
- `Store` 改造：版本闸门放宽、去掉 `Save` 副作用、单写口
- 删除 2 个死字段 + 其编译依赖（**必须在本阶段做完，否则不编译**）
- 自检迁移用例改到 v6

**不做**（留给 P1–P4）：
- UI 拆页、UIPanel 重组、`FalsumChartInfoPanel`
- 本地化键迁移、`Debug*` 测试面搬迁
- 门面下线

## 前置事实（已实测，非推测）

| # | 事实 | 证据 |
|---|---|---|
| F1 | `CurrentVersion = 5`；`Version` 字段带初始值 `= CurrentVersion` | `FalsumPreferences.cs:165`、`:169` |
| F2 | `Save` 内先 `preferences.Normalize()` 再写盘 | `FalsumPreferencesStore.cs:48` |
| F3 | `LoadFromDisk` 在 `Normalize` **之前**拒绝 `loadedVersion < 1` | `FalsumPreferencesStore.cs:112-120`，`Normalize` 在 `:121` |
| F4 | `Clone()` 走 `JsonUtility.ToJson(this)` —— **只序列化字段，不序列化属性** | `FalsumPreferences.cs:199-203` |
| F5 | `CreateDefault()` 自己会调 `Normalize()` | `FalsumPreferences.cs:183-197` |
| F6 | `.Advanced` 全树 **69 处引用**，其中 2 处是本地化键误命中（`FalsumChartEditor.cs:6317` 的 `clearCache` 键、`FalsumPreferencesView.cs:2494` 的 `gridWidthValue` 键），真实字段引用 **67 处**，分布在 5 个文件 | grep `\.Advanced\b` |
| F7 | 其中 **19 处是写点，6 个可写成员**；门面属性必须有 setter | 见任务 2 表 |
| F8 | **存在整组赋值**：`FalsumPreferences.cs:231` 在 `Normalize()` 内 `Advanced = new FalsumAdvancedPreferences();`，另有字段初始化器 `:180-181`。**只读门面会在此处编译失败** | grep |
| F9 | `ApplyGridPreferences(FalsumAdvancedPreferences advanced)` 以**类型**为参数，门面类型名与签名必须在 P0 存活 | `FalsumChartEditor.cs:7690`、`:7698-7699` |
| F10 | `RestoreLastPackage` 全树**从未被赋值**（恒为 `false`），`LastPackagePath` 仅被 `Normalize` 空白重置读取 | `FalsumPreferences.cs:152,154,242-245`；门在 `FalsumChartEditor.cs:5173` |
| F11 | `RememberLastPackage` 的调用点只有 2 处 | `FalsumChartEditor.cs:5136`、`:5891` |
| F12 | git HEAD 的提交版本是 **v3**（`CurrentVersion=3`，`Advanced` 无 `ShowMeasureNumbers`/`UIStyle`） | `git show HEAD:...FalsumPreferences.cs` |
| F13 | 真实 v1 profile 键数 15，是 v5（26 键）的**真子集**，无改名 | 本机两份 profile 实测 |
| F14 | `RestoreLastPackage` / `LastPackagePath` **在 v6 里没有归宿**，无法「转发到新组」；门面必须为它们提供**恒假/恒空的固定实现**，而不是转发 | F10 + 任务 5 |
| F15 | 非空不变量：`FalsumPreferencesView.cs:178-179`、`FalsumChartEditor.cs:9493-9494` 断言 `Advanced != null`。门面 getter **绝不能在 `Normalize()` 中途返回 null** | grep |
| F16 | 迁移测试会**毁掉自己的夹具**：`LoadFromDisk:127` 在版本升级时回写盘，而 `FalsumSelfCheck.cs:836-861` 只备份一条路径 | 验证员实测 |

## 任务 1：v6 字段结构

文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs`

步骤：

1. `CurrentVersion` 改为 `6`（`FalsumPreferences.cs:165`）。
2. 新增两个组类型：

```csharp
[Serializable]
public sealed class FalsumGridPreferences
{
    public string LineColors = FalsumBeatGrid.DefaultColorSequence;
    public float LineWidthScale = 1.0f;
}

[Serializable]
public sealed class FalsumExperimentalPreferences
{
    public FalsumUiStyle UIStyle = FalsumUiStyle.Classic;
    public bool ShowMeasureNumbers = false;
}
```

3. `FalsumEditingPreferences` 增加 `public bool DisableOutOfBoundsClamping;`
4. `FalsumPreferences` 的字段改为：

```csharp
public int Version = CurrentVersion;          // 内存态保留初始值（见说明）
public FalsumDisplayPreferences Display      = new FalsumDisplayPreferences();
public List<FalsumKeyBinding> Bindings       = new List<FalsumKeyBinding>();
public FalsumEditingPreferences Editing      = new FalsumEditingPreferences();
public FalsumBackupPreferences Backup        = new FalsumBackupPreferences();
public FalsumAudioPreferences Audio          = new FalsumAudioPreferences();
public FalsumGridPreferences Grid            = new FalsumGridPreferences();
public FalsumExperimentalPreferences Experimental = new FalsumExperimentalPreferences();
```

> **`Version` 初始值保留的理由**：`Clone()`（F4）与 `CreateDefault()`（F5）都依赖构造即有值的
> 内存对象。磁盘版本语义改由 DTO 承担（任务 3），两者不冲突。
> **绝不要**把 `Version` 的 0 默认值加到 `FalsumPreferences` 上 ——
> `CreateDefault()` 会 `Normalize()`（F5），届时会把自己当「缺键」再全链迁移。

5. `Normalize()` 迁走与本次无关的逻辑：
   - 删除 `if (Advanced == null) Advanced = new ...`（`:229-232`）
   - 网格逻辑改为操作 `Grid`（原 `:246-260`）
   - `UIStyle` / `ShowMeasureNumbers` 逻辑改到 `ApplyProductGates()`

6. 新增 `ApplyProductGates()`，原样承接三处门禁：

```csharp
private void ApplyProductGates()
{
    // 原 :233-241 —— 风格开关隐藏时强制 Classic，并做枚举合法性夹取
    if (!FalsumUiStyleSwitcher.Available ||
        !Enum.IsDefined(typeof(FalsumUiStyle), Experimental.UIStyle))
    {
        Experimental.UIStyle = FalsumUiStyle.Classic;
    }
    // 原 :261-271 —— 计量线号开关隐藏时强制 false
    if (!FalsumMeasureNumberFeature.Available)
    {
        Experimental.ShowMeasureNumbers = false;
    }
}
```

验证：
- 编译通过。
- 自检中 `restored.Advanced.UIStyle == FalsumUiStyle.Classic` 等断言仍为绿（走门面，见任务 2）。

## 任务 2：`Advanced` 兼容门面（本计划最关键的一步）

文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs`

**设计约束（三个硬约束共同决定门面形态）**：

1. **F4**：`Clone()` 用 `JsonUtility.ToJson(this)`，而 **JsonUtility 只序列化字段、忽略属性**。
   因此门面**不能持有数据** —— 数据必须留在新组的字段里，门面只做转发。
2. **F8**：`Normalize():231` 有整组赋值 `Advanced = new FalsumAdvancedPreferences();`。
   **只读属性（`{ get; }`）在此行直接编译失败。**
3. **F14**：`RestoreLastPackage` / `LastPackagePath` 在 v6 没有归宿，
   无法「转发到新组」—— 必须给固定实现。

结论：`Advanced` **保留为可写属性**（有 getter 也有 setter），
旧代码里的整组赋值因此仍能编译，但 setter 语义必须是「忽略」而非「替换」。

步骤：

1. 把 `FalsumAdvancedPreferences` 改为转发门面（**不再是 `[Serializable]` 数据组**）：

```csharp
public sealed class FalsumAdvancedPreferences
{
    private readonly FalsumPreferences _owner;

    internal FalsumAdvancedPreferences(FalsumPreferences owner)
    {
        _owner = owner;
    }

    public FalsumUiStyle UIStyle
    {
        get { return _owner.Experimental.UIStyle; }
        set { _owner.Experimental.UIStyle = value; }
    }

    public bool ShowMeasureNumbers
    {
        get { return _owner.Experimental.ShowMeasureNumbers; }
        set { _owner.Experimental.ShowMeasureNumbers = value; }
    }

    public string GridLineColors
    {
        get { return _owner.Grid?.LineColors ?? string.Empty; }
        set { if (_owner.Grid != null) _owner.Grid.LineColors = value; }
    }

    public float GridLineWidthScale
    {
        get { return _owner.Grid != null ? _owner.Grid.LineWidthScale : 1.0f; }
        set { if (_owner.Grid != null) _owner.Grid.LineWidthScale = value; }
    }

    public bool DisableOutOfBoundsClamping
    {
        get { return _owner.Editing != null && _owner.Editing.DisableOutOfBoundsClamping; }
        set { if (_owner.Editing != null) _owner.Editing.DisableOutOfBoundsClamping = value; }
    }

    // F14：v6 无归宿，固定实现，不转发、不持久化
    public bool RestoreLastPackage
    {
        get { return false; }
        set { /* 有意忽略：P0 起该字段不再存在 */ }
    }

    public string LastPackagePath
    {
        get { return string.Empty; }
        set { /* 有意忽略 */ }
    }
}
```

2. 在 `FalsumPreferences` 上保留**可写属性**，替掉原字段（`:180-181`）：

```csharp
private FalsumAdvancedPreferences _advancedFacade;

/// <summary>
/// P0 过渡门面。数据实际存放于 Editing / Grid / Experimental。
/// setter 有意忽略赋值：旧代码的整组替换不生效，也不会丢掉新结构里的数据。
/// </summary>
[Obsolete("Use Editing / Grid / Experimental directly. Removed in P4.")]
public FalsumAdvancedPreferences Advanced
{
    get
    {
        // F15：Normalize() 中途也必须非 null
        if (_advancedFacade == null)
        {
            _advancedFacade = new FalsumAdvancedPreferences(this);
        }
        return _advancedFacade;
    }
    set
    {
        // 有意空实现。见任务 2 说明第 2 条：Normalize():231 会走到这里。
    }
}
```

> **`[Obsolete]` 的实测风险**：本工程没有 `.asmdef`/`csc.rsp` 定制，
> `[Obsolete]` 大概率只是**警告**而非错误（仓库内也无既有用法）。
> 若它意外升级为错误，改用普通注释 —— 但**不要**在调用点加 `#pragma`，
> 那等于把门面永久化，P4 会更难删。

3. 门面必须覆盖的 **19 个写点 / 6 个可写成员**（缺一个就编译失败）：

| 成员 | 写点 |
|---|---|
| `UIStyle` (6) | `FalsumPreferences.cs:240`；`FalsumPreferencesView.cs:207`、`:2140`；`FalsumSelfCheck.cs:866`、`:870`；`FalsumUiStyleCapture.cs:37` |
| `ShowMeasureNumbers` (5) | `FalsumPreferences.cs:265`、`:270`；`FalsumPreferencesView.cs:2331`；`FalsumSelfCheck.cs:875`；`FalsumChartEditor.cs:9496` |
| `GridLineColors` (2) | `FalsumPreferences.cs:255`；`FalsumPreferencesView.cs:2267` |
| `GridLineWidthScale` (2) | `FalsumPreferences.cs:257`；`FalsumPreferencesView.cs:2301` |
| `DisableOutOfBoundsClamping` (2) | `FalsumPreferencesView.cs:2350`；`FalsumSelfCheck.cs:865` |
| `LastPackagePath` (2) | `FalsumPreferences.cs:244`；`FalsumChartEditor.cs:5172`（**任务 5 删除**） |

（`RestoreLastPackage` 只有读点 `FalsumChartEditor.cs:5173`，无写点。）

4. **`FalsumPreferences.cs` 内部的 7 处赋值要就地改写**，不要留给门面：
   `:231`（删，改 RecreateGroups 逻辑）、`:240`、`:244`、`:255`、`:257`、`:265`、`:270`
   全部改为直接操作 `Experimental` / `Grid` / `Editing`。
   这样 `Normalize()` 不再依赖门面，门面只服务**外部**调用点。

5. `ApplyGridPreferences(FalsumAdvancedPreferences)` 签名**不用改**（F9）：
   `ApplyGridPreferences(preferences.Advanced)` 仍拿到门面实例，内部读取经属性转发。

验证：
- 编译通过，`Advanced` 引用无 `CS1061`/`CS0117`/`CS0200` 类错误。
- 自检 `CheckPreferences` 全绿。
- **专项 1（F4）**：`Clone()` 往返不丢数据 ——
  `var c = prefs.Clone(); c.Experimental.UIStyle` 与原值相等。
  这是门面设计的直接后果，必须显式测，否则门面看似工作但 clone 已丢值。
- **专项 2（F8/F15）**：`var p = new FalsumPreferences(); p.Normalize();`
  不抛异常，且 `p.Advanced != null`。
- **专项 3（F14）**：`p.Advanced.RestoreLastPackage` 恒为 `false`；
  对其赋值后仍为 `false`。

## 任务 3：legacy 单超集 DTO + 迁移

新增文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesMigration.cs`

步骤：

1. 定义单超集 DTO（F13：v1 是 v5 子集，无改名，故不需要 6 个版本类）：

```csharp
[Serializable]
internal sealed class FalsumLegacyAdvancedDto
{
    public bool RestoreLastPackage;
    public bool DisableOutOfBoundsClamping;
    public string LastPackagePath;
    public string GridLineColors;
    public float GridLineWidthScale;
    public bool ShowMeasureNumbers;
    public int UIStyle;              // 注意：DTO 里用 int，避免非法枚举值污染
}

[Serializable]
internal sealed class FalsumLegacyPreferencesDto
{
    public int Version;              // 无初始值 → 缺键时为 0
    public FalsumDisplayPreferences Display;
    public List<FalsumKeyBinding> Bindings;
    public FalsumEditingPreferences Editing;
    public FalsumBackupPreferences Backup;
    public FalsumAudioPreferences Audio;
    public FalsumLegacyAdvancedDto Advanced;
}
```

2. `FromDisk(string json)` 逻辑：

```csharp
var dto = JsonUtility.FromJson<FalsumLegacyPreferencesDto>(json);
// 1) 缺组判定（不依赖 Version，逐组看是否为 null）
// 2) 按需补默认组
// 3) 老绑定迁移：dto.Version < 2 时对 MiddleEdit* 做 F8/F9/F10 → F1/F2/F3
// 4) dto.Version < 3 时 Editing.SkyAreaGroupTimeSnap = true
// 5) Advanced 拆解：逐字段写入 Editing / Grid / Experimental
// 6) Version = 6
```

3. **不要**把 `dto.Advanced.RestoreLastPackage` / `LastPackagePath` 搬进新结构 —— 直接丢弃。

验证：
- `FromDisk` 对 v1 样本、v5 样本各跑一次，字段落到正确组。
- 对「无 `Version` 键」的 JSON，`dto.Version == 0` 且走 v1 路径（此断言需在自检里显式写）。
- 幂等：同一输入连跑两次，逻辑值相等。

## 任务 4：`FalsumPreferencesStore` 改造

文件：`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferencesStore.cs`

步骤：

1. **放宽版本闸门**（F3，否则任务 3 的 0 兜底形同虚设）：`:112-120` 改为
   `if (loadedVersion < 0 || loadedVersion > FalsumPreferences.CurrentVersion)`，
   即 **0 合法**（视为 v1），负值仍判崩。
2. 读盘改用 `FalsumPreferencesMigration.FromDisk(json)`，不再直接
   `JsonUtility.FromJson<FalsumPreferences>`。
3. 去掉 `Save` 里的 `preferences.Normalize()`（F2，`:48`）：
   写盘前只做 `preferences.Version = FalsumPreferences.CurrentVersion`，
   不再就地改写调用方对象的显示/音量等字段。
4. 迁移回写保留（`loadedVersion < CurrentVersion` 时写回），但改用新 `Save`。

验证：
- `Store.Load()` 对两份真实样本返回 v6 结构。
- `Save` 前后传入对象的关键字段（`Display.ResolutionWidth` 等）**不被改动**（针对 F2 的回归断言）。
- 降级（`Version > 6`）仍走 `.corrupt-*` 备份路径。

## 任务 5：删除死字段与编译依赖（**必须在本阶段完成**）

依据 F10/F11：`RestoreLastPackage` 恒为 false，`LastPackagePath` 无人消费，
`RememberLastPackage` 因此是死方法（写入后立即因 `RestoreLastPackage` 为 false 返回，永不写盘）。

文件：
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumPreferences.cs`
- `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs`

步骤：

1. 删除 `FalsumAdvancedPreferences` 中的 `RestoreLastPackage` / `LastPackagePath`（连同 DTO 里的对应字段使用）。
2. 删除 `Normalize()` 的 `LastPackagePath` 空白重置（`:242-245`）。
3. 删除 `FalsumChartEditor.RememberLastPackage` 整个方法（`:5166-5187`）。
4. 删除其 2 个调用点：`:5136`、`:5891`。
5. 确认 `_preferences` 不再需要这 2 个字段的任何引用。

验证（**行为中性证明**）：
- 导入包 / 保存包后，`preferences.json` 的 `LastPackagePath` 键消失，
  且日志中无「Last package path could not be saved」。
- 其余偏好写入路径不受影响（`Save` 调用次数从 2 条业务路径降为 0 条与该字段相关）。

> 这一步不可推迟：不删就会在任务 2 的门面上留下两个「恒假/恒空」的转发属性，
> 且 `FalsumChartEditor.cs:5172-5173` 会继续编译失败（字段已不存在）。

## 任务 6：自检用例改写

文件：`Assets/InFalsus/Editor/FalsumSelfCheck.cs`

步骤：

1. `:863-877` 的序列化断言改为走新组：
   `preferences.Experimental.UIStyle = ...`、`preferences.Editing.DisableOutOfBoundsClamping = ...`。
2. `:873-877` 的 `versionFour` 用例改为 v5 → v6 用例，断言
   `ShowMeasureNumbers` 被门禁强制 false（注意：F13 + 门禁，与版本无关）。
3. `:831-862` 的 v1 迁移用例：把写出的 JSON 换成真实 v1 形状
   （`Editing` 只有 2 个字段、`Advanced` 只有 2 个字段），断言迁移后落到 v6。
4. 新增断言：`Version` 缺键的 JSON 也能迁移（`dto.Version == 0` 分支）。
5. **补夹具备份（F16）**：现有 `:836-861` 只备份/还原**一条**路径。
   P0 起迁移会在 `LoadFromDisk:127` 回写盘，会**把 v5 样本就地升级成 v6**，
   夹具当场失效。必须扩展为备份所有将被读到的 profile 路径，
   或在测试前把它们复制到临时目录再对副本跑迁移。

验证：
- `UnityFalsumSelfCheck.log` 中 `preferences` 检查项为通过。
- 整个过程**不得**触碰真实用户 profile；跑完后 v5 样本文件仍应为 `Version: 5`。

## 验收（P0 完成定义）

按顺序执行，前一步不过不进下一步：

0. **先复制夹具**（F16，跳过这步就会毁掉验证依据）：
   把下列 profile 复制到工作区外的临时目录，只对副本跑迁移：

   | 目录 | 版本 | 用途 |
   |---|---|---|
   | `%LocalLow%\Team AakTsao\Falsum Editor Preview\Falsum\` | v5 | 主用例 |
   | `%LocalLow%\DefaultCompany\InFalsus-Gameplay\Falsum\` | v1 | 老 profile |
   | `%LocalLow%\DefaultCompany\` 下其余 `Falsum Editor Preview` 类目录 | v3（`Advanced` 仅 4 键，无 `DisableOutOfBoundsClamping`） | **注意：不得假设该字段默认 true** |

1. **编译**：`Unity.exe -batchmode -quit -projectPath <proj> -logFile Diagnostics\UnityCompile.log`，无 `error CS`。
2. **自检**：`-executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run`，`passed:true`。
3. **迁移实测**（对第 0 步的**副本**）：
   - 启动编辑器后检查副本变为 `Version: 6`，`Audio`/`Grid`/`Experimental` 分组正确
   - 其余字段值与原文件**逐字段相等**（这是唯一能证明迁移无损的手段）
4. **回归**：进游戏 → 打开首选项（UI 未拆，仍走旧 View）→ 显示/音量/键位/备份/高级页各项可读可写。
5. `python Tools\falsum_localization_audit.py` 退出码 0（P0 不动文案，应保持绿）。

## 风险与回滚

| 风险 | 处置 |
|---|---|
| `Clone()` 丢门面数据（F4） | 任务 2 专项验证 1 必须执行；数据放字段，门面**只**转发 |
| `[Obsolete]` 产生 67 条警告淹没真实警告，或意外升级为错误 | 降级为普通注释即可；**不**在调用点加 `#pragma` |
| v6 迁移写坏真实 profile | 验收第 0 步先复制夹具；`Store` 保留 `.corrupt-*` 备份路径 |
| 迁移测试毁掉自己的夹具（F16） | 验收第 0 步；任务 6 步骤 5 扩展备份 |
| 自检触碰真实 profile | 沿用现有 selfcheck-backup 机制并确认还原 |
| 截图基线 | P0 不动 UI，基线**应当不变**；若变了说明误改了 View，需查 |

回滚：P0 是单一提交，`git revert` 即可；用户 profile 需从 `.corrupt-*` 或第 0 步的副本恢复。

---

## 执行结果（2026-11-03 实际落地）

计划里被实测推翻的一处假设：**不能用 `null` 判断「这一版还没有这个组」**。
`JsonUtility` 对 JSON 里缺失的对象字段不会留 null，而是构造带字段初始化器的
默认实例（用探针在真实工程里验证过：解析 `{"V":1}` 到一个含 `Inner I` 成员的
类，`I` 非 null 且成员是初始化值）。因此 v1 profile（没有 `Audio` 组）会被当成
「存在但全零」，迁移后音乐音量变 0 —— 所有人升级即静音。
实际实现改为**按 JSON 顶层键是否存在**判断（`JsonPresence`），并补了回归断言。

另外 `Version` 必须在 `Normalize()` **之前**按磁盘值写入，否则 `Normalize` 里
`previousVersion < 2/3/5` 的分支会被跳过（自检「v1 preferences were not migrated」抓到过）。

### 提交序列

| commit | 阶段 | 内容 |
|---|---|---|
| `df21e3f` | P0 | v6 结构 + `Advanced` 门面 + `Migration` + `Store` 改造 |
| `a50cd71` | P1 | 抽出显示确认 / 背景预览 / 键位捕获三个共享件 |
| `e807965` | P2-1 | 编辑页 + `FalsumPreferencesWidgets` 工厂 |
| `ee46a44` | P2-2 | 音量页 |
| `eb590d2` | P2-3 | 显示页 |
| `e0b3ffb` | P2-4 | 快捷键页 + `IFalsumPreferencesPageHost` / `FalsumPreferencesTab` |
| `f746d7e` | P2-5 | 关于页 |
| `6f48362` | P2-6 | 高级页 |
| `7fac870` | P2-7 | 备份页 |
| `79eed26` | P2-8 | 谱面信息页 |
| `5adc8e3` | P2-9 | 删除页面拆分后的转发存根 |
| `f823c30` | P3 | 谱面信息提升为独立面板，首选项移除 Chart 页 |
| `be40005` | P4-1 | 删除 `_updating` 与 `_coverTextureShared` 两个死标志 |
| `0779371` | P4-2 | 删除文档作用域头部构件（scope 标签 / footer 提示） |

### 结果

| 指标 | 重构前 | 重构后 |
|---|---|---|
| `FalsumPreferencesView.cs` | 3569 行 | 1450 行 |
| 页面 | 1 个 3569 行的上帝类 | 8 个独立页面文件 + 3 个共享件 + 壳 |
| 排查一个页面的阅读量 | 全文件 | 70–370 行 |
| 验证 | — | 编译 0 错误、自检 28/28、本地化审计通过 |

### 未完成 / 交接

1. **编辑器侧接线在工作区未提交**：`FalsumChartEditor.cs`、`FalsumEditorView.cs`、
   `FalsumSelfCheck.cs` 三个文件含大量接手前他人未提交的工作（分别 +1680/-530、
   +683/-221、+257/-12 行），P3 的入口接线叠加在其上，无法按文件拆分。
   按约定「只提交干净的部分」，这些接线保留在工作区，交由基线作者一并入库。
2. **P4 剩余**：`PreferencesTabChart` 等本地化死键已在代码侧不再被引用，但
   `Localization/*.json` 是**未跟踪文件**（属他人未提交工作），未删除其中条目。
3. **截图基线未重做**：`FalsumPreferencesCapture.cs` 的 12 张截图中有 4 张
   （chart / chart_difficulties / background / background_japan）因 Chart 页移出
   而必然变化，需在接线合入后重跑。

