# 设计：FALSUM 首选项重构（模型 + 弹窗，2026-11-03）

> 目标：把「首选项」从**一个承担四种职责的数据类 + 一个承担两种作用域的弹窗**，
> 拆成**分层的持久化模型 + 全局首选项弹窗 + 独立谱面信息面板**。
> 用户可见的全局偏好项与文案不变；数据层升到 v6 并一次性迁移。

## 1. 现状诊断（每条都对应实测位置）

| # | 问题 | 证据 |
|---|---|---|
| D1 | `Normalize()` 把「null 补齐 / 版本迁移 / 区间夹取 / 功能门禁」四件事塞进一个方法 | `Runtime/FalsumPreferences.cs:205-315` |
| D2 | JSON 缺 `Version` 键时按字段初始值解析成 v5，**静默跳过全部迁移** | `FalsumPreferences.cs:169` |
| D3 | `RestoreLastPackage` 无 UI、无恢复逻辑，唯一读点只是决定「是否保存 LastPackagePath」；`LastPackagePath` 仅被 `Normalize` 的空白重置读取，无任何消费方 | `FalsumPreferences.cs:152-160`；门在 `FalsumChartEditor.cs:5172-5176`，空白重置在 `FalsumPreferences.cs:242-245` |
| D4 | `_updating` 重入守卫在 5 处读取，全仓库**从未赋值** | `UI/FalsumPreferencesView.cs:109`，读点 `1353/1378/1414/1441/2136` |
| D5 | `_coverTextureShared` 仅被赋 `false`，`true` 分支为死码 | `FalsumPreferencesView.cs:3219/3255/3265/3273` |
| D6 | `ExitBackgroundPreview(notify)` 两个分支发出同一事件，参数无效 | `FalsumPreferencesView.cs:3128-3143` |
| D7 | 「恢复默认」有三套语义：footer 整份替换并预览风格、Keys 页只重置键位、Advanced 页整份替换**却不预览风格**。后者还**不重置 `_gridColorsInvalid`**，残留的网格色错误会继续在 `RequestCommit` 拦下保存 | `:816-828` / `:1630-1638` / `:2201-2208`；`_gridColorsInvalid` 声明 `:110`、提交门 `:2774` |
| D8 | 作用域只是一个 bool：`SetHeader(..., document)` 只隐藏 `PreferencesScope` 与 footer 提示，仅 Chart 页传 `true` | `:1009-1024`、`:911-963` |
| D9 | 21 个 `Debug*` 共 262 行无 `#if` 保护，随发布版发出 | `:168-408`(241) + `:988-1008`(21) |
| D10 | `preferences.json` 有 **5 条显式 Save + 1 条隐式迁移回写**，其中编辑器截图工具会覆盖**真实用户文件** | 显式：`FalsumChartEditor.cs:5179/7523/9499`、`Editor/FalsumUiStyleCapture.cs:38/253`；隐式：`FalsumPreferencesStore.cs:127`（`LoadFromDisk` 内） |
| D11 | `FalsumDisplayConfiguration` / `Capture` / `ApplyConfiguration` 无任何调用者，是并行的重复真相。**但 `FalsumDisplaySettingsController` 本身仍活着**（`IsSupported`/`TryApply` 有 7 个调用点） | `Runtime/FalsumDisplaySettingsController.cs:9-17,26-49,127-142`；活调用点 `FalsumChartEditor.cs:7487/7492/7562/7589/7755`、`FalsumPreferencesView.cs:2791`、`FalsumSelfCheck.cs:878` |
| D12 | 纯构建方法 994L；其中 Backup(269L)+Advanced(235L) 占**50.7%**，Editing 仅 68L，粒度严重不均 | `FalsumPreferencesView.cs:1844-2112`、`:2113-2346`、`:1776-1843` |
| D13 | 本地化死键 `falsum.preferences.chartTitle` / `chartSubtitle` 定义后从未引用 | 两语言各 2 键 = **4 处定义**；C# 中零引用（`chartTitle` 的 14 处命中均为 `_newChartTitle` 标识符，与键无关） |

`FalsumPreferencesView.cs` 3569 行的构成（实测重算，见 §1.1）：
页面专属方法合计 **1534 行 / 43.0%**，`Build*Page` 本体 1042 行 / 29.2%，
测试面 262 行 / 7.3%。

### 1.1 数字口径（防止复用时搬错）

| 指标 | 值 | 口径 |
|---|---|---|
| 页面专属方法合计 | **1534 / 3569 = 43.0%** | 各页 `Build*` + 该页专用辅助方法（`BuildDifficultyStrip`、`CreateFormField`、`CreateMediaRow`、`BuildResolutionChoices`、`AddVolumeRow`、`AddKeyBindingGroup`、`ActionLabel`、`SetDisableOutOfBoundsClamping`、`RefreshCacheSize`、`GridWidthLabel`、`OnAboutVersionPointerDown`、`RegisterAboutVersionTap`） |
| 纯 `Build*Page` + `Add*/Create*` | **994** | `BuildChartPage`108 + `BuildDisplayPage`146 + `(BuildVolumePage+AddVolumeRow)`102 + `(BuildKeysPage+AddKeyBindingGroup)`154 + `BuildEditingPage`68 + `BuildBackupPage`269 + `BuildAdvancedPage`235 + `BuildAboutPage`81 = 994 |
| `Build*Page` 本体 | **1042 / 3569 = 29.2%** | 只算 8 个 `Build*Page` 方法本身（含 Chart 的 108 与 Backup 的 269 等） |
| Backup + Advanced | **504 行** | 占 994 那一档的 **50.7%**，占 1534 那一档的 32.9%；两种分母都写出来以免误引 |
| 测试面（View 内） | **262 行 = 7.3%**，非空 240 行 | 21 个 `Debug*` 的完整块 `168-408`(241) + `988-1008`(21) |
| 测试面（ChartEditor 内） | **17 个 `Debug*Preferences*` + 3 个偏好桥 = 201 行** | 17 个纯转发器；`DebugClickSongInfoCover:2456`、`DebugPreviewUiStyle:2484`、`DebugUiStyle:2474` 也进 PreferenceView，一并计。`2451-2819` 整段 369 行夹着非偏好成员，不可直接引用 |
| 方法数 | **73**（严格签名+`{`） / **94**（含属性与表达式体） | 「91」不对应任何口径，已弃用 |

## 2. 重构后的分层

三层职责，物理分开：

| 层 | 载体 | 生命周期 | 落盘 |
|---|---|---|---|
| 用户偏好 | `FalsumPreferences`（v6） | 全局，跨工程 | `persistentDataPath/Falsum/preferences.json` |
| 会话态 | `FalsumChartEditor` 私有字段 | 进程内 | 不落盘 |
| 文档态 | `FalsumChartMetadataDraft`（既有，不改内部） | 随 `.falsum` 包 | `song.json` + 包内资源 |

> 分界线：**能被另一台机器的另一个工程复用的配置才是用户偏好**。
> `LastPackagePath` 属于会话态（D3），已经错位一次，v6 删除。

## 3. 数据层：v6 schema

### 3.1 磁盘结构

7 组，组名即职责，无「垃圾桶组」：

```jsonc
{
  "Version": 6,                                  // DTO 缺此键 → 0 → 按 v1 起跑全链（需放宽 Store 闸门，见 §3.2-2）
  "Display": {
    "ResolutionWidth": 1366, "ResolutionHeight": 768, "ResolutionRefreshRate": 60,
    "BorderlessFullscreen": false,
    "AntiAliasingEnabled": true, "AntiAliasingMode": 1
  },
  "Audio": {
    "MusicVolume": 1.0, "SkyAreaVolume": 1.0,
    "TapVolume": 1.0, "HoldVolume": 1.0, "FlickVolume": 1.0
  },
  "Editing": {
    "DirectDragUnselectedNotes": true,
    "MiddleEditAffectsAllSelected": true,
    "SkyAreaGroupTimeSnap": true,
    "DisableOutOfBoundsClamping": false           // ← 自 Advanced 并入
  },
  "Backup": { "Enabled": true, "IntervalSeconds": 180, "RetentionCount": 20 },
  "Bindings": [                                 // ← 原顶层字段，升为一级组
    { "Action": 0, "Modifiers": 0, "Key": 81 }
  ],
  "Grid": {                                     // ← 自 Advanced 提出
    "LineColors": "…", "LineWidthScale": 1.0
  },
  "Experimental": {                             // ← 门禁字段收容，字段保留但被强制
    "UIStyle": 0, "ShowMeasureNumbers": false
  }
}
```

**`Advanced` 组消失**，7 个字段的归宿：

| 字段 | 去向 | 理由 |
|---|---|---|
| `RestoreLastPackage` | **删除** | D3，无 UI 无读取方 |
| `LastPackagePath` | **删除** | D3，只写不读，属会话态 |
| `DisableOutOfBoundsClamping` | → `Editing` | 是编辑行为开关 |
| `GridLineColors` / `GridLineWidthScale` | → 新 `Grid` 组 | 是渲染样式，与「高级」无关 |
| `UIStyle` / `ShowMeasureNumbers` | → `Experimental` | 均为临时产品门禁，保留字段以便恢复功能时不必再升版本 |

**`Bindings` 不套 `Input` 壳。** 它天然唯一，多套一层 `{ "Input": { "Bindings": [...] } }`
只是为对称而对称，徒增一层迁移。保持顶层数组。

### 3.2 代码结构

```csharp
FalsumLegacyPreferencesDto              // 单超集 DTO（v1…v5 全覆盖，字段无初始值），见 §6.2
FalsumPreferencesMigration.FromDisk()  // 按 JSON 顶层键是否存在推断缺口 → 补齐并升到 v6
FalsumPreferences.Normalize()          // 只做：null 补齐 + 区间夹取 + 枚举合法性 + ApplyProductGates()
FalsumPreferences.ApplyProductGates()  // 显式承载 UIStyle / ShowMeasureNumbers 门禁（不可省略）
FalsumPreferencesStore.Save()          // 唯一正常写盘口，不再 Normalize 调用方对象
```

四个关键修复：

1. **`Version` 的 0 默认值只加在 DTO 上，不加在 `FalsumPreferences` 上。**
   `JsonUtility.FromJson` 先跑字段初始化器再覆盖 JSON 中出现的键，所以
   `public int Version = CurrentVersion;`（`FalsumPreferences.cs:169`）正是 D2 的成因：
   缺键文件必然拿到 `CurrentVersion`。
   正确做法：`FalsumPreferencesV1Dto` … `V6Dto` 的 `Version` **一律不带初始值**（默认 0）；
   `FalsumPreferences` 本体不再承载磁盘版本语义。
   **反向约束**：绝不能把 0 默认加到 `FalsumPreferences` 上 ——
   `CreateDefault()`（`:183-197`）自己就会 `Normalize()`，届时会把自己当成「缺键」再全链迁移一次。

2. **`Store.LoadFromDisk` 的版本闸门必须一并放宽**（否则修复 1 无效）。
   现有 `:113-120` 在 `Normalize`（`:121`）**之前**就拒绝 `loadedVersion < 1`，
   直接走 corrupt 分支（`:139-146`）。也就是说：即便真的读到 `Version: 0`，
   也永远到不了迁移链。目标行为：
   `0 → 视为 v1 起跑迁移`；`1…6 → 各自迁移`；`> 6 → 显式警告`。

3. **`Normalize()` 去掉版本判断与 I/O，但必须保留产品门禁。**
   直接删成「无版本、无迁移、不读 Screen」会顺手删掉三处仍有效的东西：
   - `!FalsumUiStyleSwitcher.Available` 强制 Classic + `Enum.IsDefined` 合法性夹取（`:233-241`）
   - `!FalsumMeasureNumberFeature.Available` 强制 false（`:267-271`）
   它们必须搬到具名的 `ApplyProductGates()`，否则门禁一旦翻回 `true`，
   `UIStyle` 就失去校验。`Screen.currentResolution` 的修复逻辑移到
   `CreateDefault()` / 显式 `RepairDisplay()`。

4. **`Save` 不再有副作用**：不 `Normalize` 调用方对象（现在是 `FalsumPreferencesStore.cs:48`），
   也不读 `Screen.currentResolution`。当前「保存一次就按当前显示器改写分辨率」
   （`FalsumPreferences.cs:273-290`）由此消除。

**写盘口收敛**（D10）：显式 Save 5 条 + 隐式迁移回写 1 条 → 目标 2 条职责：
- 正常：`Store.Save`（提交、迁移回写）
- 只读：自检与测试一律走内存态，**不再触碰用户文件**
截图工具若必须落盘，先备份、`finally` 恢复，失败不得留下中间态。

### 3.3 迁移契约

按**实测的现有分支**描述（`Normalize` 里只有三处版本判断，其余都是无条件逻辑）：

| 现有分支 | 位置 | 行为 |
|---|---|---|
| `previousVersion < 2` | `NormalizeBindings`，`:423-437` + `MigrateDefaultBinding:461-473` | 仅当用户改过默认值时才把 F8/F9/F10 改成 F1/F2/F3（三个 MiddleEdit 动作） |
| `previousVersion < 3` | `:217-220` | `Editing.SkyAreaGroupTimeSnap = true` |
| `previousVersion < 5` | `:261-266` | `Advanced.ShowMeasureNumbers = false` |
| 无 `<4` 分支 | — | v4 不存在独立分支，与 v3 走同一路径 |

目标契约：

| 起点 | 行为 |
|---|---|
| DTO 无 `Version` 键 / `Version == 0` | 视为 v1 起跑全链（**前提**：`LoadFromDisk` 的 `<1` 闸门必须放宽，见 §3.2-2） |
| v1 | `Editing.SkyAreaGroupTimeSnap = true`（补 `<3` 逻辑）；F8/F9/F10 → F1/F2/F3 需按 `<2` 判断 |
| v2 | 跑 `<3`（`SkyAreaGroupTimeSnap = true`）与补齐缺失组 |
| v3（**git HEAD 的基线**） | 已含 `SkyAreaGroupTimeSnap`；补齐 `Audio` 组 |
| v4 | 与 v3 同路径；补齐 `Audio` 组与 `ShowMeasureNumbers` |
| v5 | `Advanced` 拆解重排；删除 2 个死字段；`Version = 6` |
| `> 6` | **降级警告**：警告 + `.corrupt-*` 备份 + 回落默认值（现状 `Store.cs:139-163` 已是这个行为，目标只是把它从「仅日志」升级为可见提示） |

> 注意：`ShowMeasureNumbers = false` 对**任何版本**都成立，因为
> `!FalsumMeasureNumberFeature.Available`（`:267-271`）是无条件强制。
> 所以它不是一个「v3/v4 的迁移」，而是产品门禁 —— 已归入 `ApplyProductGates()`。

迁移必须幂等：对同一份输入重复跑 `FromDisk` 结果一致。
（「字节级一致」需 Unity 实测，我未运行，暂按「逻辑值一致」验收。）

**回归样本**（本机实存，可直接当迁移测试夹具）：

| 路径 | 版本 | 用途 |
|---|---|---|
| `%LocalLow%\Team AakTsao\Falsum Editor Preview\Falsum\preferences.json` | v5 | 主迁移用例 |
| `%LocalLow%\DefaultCompany\InFalsus-Gameplay\Falsum\preferences.json` | v1 | 老 profile 迁移用例 |

## 4. UI 层：壳 + 页面类

### 4.1 页面接口

```csharp
public interface IFalsumPreferencesPage
{
    string TitleKey { get; }
    string SubtitleKey { get; }
    bool IsDocumentScoped { get; }              // 取代 D8 的 bool 参数
    void Build(VisualElement container, FalsumPreferencesHost host);
    void OnDeactivate();                        // 切页：取消键捕获、停计时
    bool Validate(out string message);          // 提交前门禁，取代集中式判断
}
```

`host` 只暴露四样：草稿 `Preferences`、状态栏 `ShowError/ShowInfo/Clear`、
`RequestRebuild()`、`Scheduler`。**页面拿不到 `_overlay` / `_window` / `_content`**。

**`OnDeactivate` 不做纹理释放**：封面纹理 `_coverTexture`（`:92`）与
`_previewHeroTexture`（`:95`）的生命周期归壳的 `CompleteClose:592-622` 与
`ReleaseCoverTexture:3263-3274`。`ShowTab:896` 现在只 `_content.Clear()`、
并不释放纹理；若把释放挂到 `OnDeactivate`，切页会双重释放并让 Chart 页
下次构建拿到已销毁的纹理。

### 4.2 文件切分

| 新文件 | 来源 | 估计行数 |
|---|---|---|
| `FalsumPreferencesDialog.cs`（壳） | 新写，接 `409-708` / `743-1007` / `2595-2848` / `3463-3569` 骨架（**不含** `709-742`） | ~600 |
| `Preferences/PreferencesPage.cs`（接口 + 工厂） | `2481-2490`（`CreateGroup`）+ `2499-2553`（`CreateRow`）+ `3373-3450`（`ActionLabel`） | ~180 |
| `Preferences/LayoutPolicy.cs` | `2554-2573`（`IsVerboseDescription`，走键 `falsum.preferences.layout.verbosePrefixes`，见 C-1） | ~25 |
| `Preferences/DisplayPreferencesPage.cs` | `1322-1467` + `1571-1621` | ~200 |
| `Preferences/VolumePreferencesPage.cs` | `1468-1562` | ~95 |
| `Preferences/InputPreferencesPage.cs` | `1622-1775` | ~230 |
| `Preferences/EditingPreferencesPage.cs` | `1776-1843` + `2348-2352`（`SetDisableOutOfBoundsClamping`，字段迁入 `Editing`） | ~75 |
| `Preferences/BackupPreferencesPage.cs` | `1844-2112` | ~270 |
| `Preferences/AdvancedPreferencesPage.cs` | `2113-2346` + `2491-2498`（`GridWidthLabel`）；**`RefreshCacheSize:2354-2365` 不归此页**，由壳持有并在 `Advanced` 页激活时调用 | ~250 |
| `Preferences/AboutPreferencesPage.cs` | `2366-2480` | ~115 |
| **`FalsumChartInfoPanel.cs`**（文档作用域） | `1025-1321` | ~450 |
| `SubDialogs/BackgroundPreviewPanel.cs` | `2868-3144` | ~280 |
| `SubDialogs/DisplayConfirmDialog.cs` | `709-742`（`ShowDisplayConfirmation`）+ `2597-2609` + `3146-3195` | ~130 |
| `Input/KeyCaptureController.cs` | 状态 `86-88` + `IsCapturingKey:154-157` + `BeginKeyCapture:2575-2593` + 捕获分支 `2611-2681` + `CancelKeyCapture:2748-2763`；含 `_bindingButtons/_bindingResetButtons/_bindingDefaults`（现于 `:897-899` 清空） | ~200 |

壳保留约 600 行：overlay、导航、标题、状态栏、滚动容器、焦点陷阱、
提交校验链（`RequestCommit:2765-2825`）、关闭动画、footer 文案。

**行范围归属唯一性**：上表各行的区间**互不重叠**；此前 `709-742` 同时被壳与
`DisplayConfirmDialog` 占用，已按「子模态独立成文件」原则划给后者。

### 4.3 双层入口

```
File ▸ Preferences… (Ctrl+,)  ──▶ FalsumPreferencesDialog   ← 全局，Done 才落盘
点歌曲卡封面                  ──▶ FalsumChartInfoPanel     ← 文档，关掉即丢弃
```

行为变化：

- 弹窗**默认页改为 `Display`**。Chart 页已移出，继续开在 Chart 会撞空白页；
  `Show` 里 `:526-543` 的延迟构建 Chart 那段一并改。
- `PreferencesTabChart` 按钮从 UXML 移除，nav 由 8 项变 7 项。
- `NavigateTabs` 现在是 `:2714-2746`，数组在 `:2721-2730`，**只列 7 个按钮且刻意排除
  `_tabAbout`**。改为读页注册表时必须显式保留「About 不进方向键循环」这一现状，
  否则是无声的行为变更。
- `PreferencesScope` 标签删除需动**三处**，且顺序不能反：
  UXML `FalsumPreferencesDialog.uxml:26`、`View:449` 的 `Require<Label>(...)`（`Require`
  在缺节点时**直接抛异常**，`:3539-3552`）、`SetHeader:1016-1018`。
  必须先删 `Require` 与 `SetHeader` 的用法，最后才删 UXML 节点。
- footer 提示文案（`SetHeader:1019-1022`，键 `falsum.preferences.footer.globalApply`）
  由**壳**持有；文档作用域面板不显示它，因此该键在 P4 删除。

## 5. 死码与遗留处置

| 项 | 处置 | 依据 / 陷阱 |
|---|---|---|
| `_updating` 字段 + 5 处判断 | 删除**判断**，但保留 `:2136` 里 `style.index < 0` 那半句 | D4。`DebugSetUiStyle:212` 直接设 `DropdownField.index`，UI Toolkit 会为程序化改值触发回调，所以「删掉全部守卫」会引入真实回归 |
| `_coverTextureShared` + 死分支 | 删除字段，统一走 `SetCoverTexture` | D5 |
| `ExitBackgroundPreview(notify)` 参数 | 删参数（两分支已相同） | D6 |
| `RestoreLastPackage` / `LastPackagePath` | 删除 | D3；`RestoreLastPackage` 判断的分支最终什么也不做，整条链路可安全移除 |
| **仅** `FalsumDisplayConfiguration` + `Capture` + `ApplyConfiguration` | 删除这**三项**，**保留文件里其余部分** | D11。该 Controller 的 `IsSupported`/`TryApply` 有 7 个活调用点（`FalsumChartEditor.cs:7487/7492/7562/7589/7755`、`FalsumPreferencesView.cs:2791`、`FalsumSelfCheck.cs:878`），**删整个文件会连锁断 7 处** |
| 本地化 `chartTitle` / `chartSubtitle` | 删除（每语言 2 键，共 4 处定义） | D13 |
| Chart 页本地化键 | 迁到 `falsum.chartinfo.*`；精确计数：`falsum.preferences.chart.*` = **21 键/语言**（另 2 个是上面要删的死键 `chartTitle`/`chartSubtitle`，合起来才是 23） | 与全局偏好解耦 |
| 21 个 `Debug*`（262 行） | 移入 `Editor/FalsumPreferencesTestApi.cs`，View 仅留 `TestApi` 出口 | D9；测试面不进发布包 |
| `DebugResetAboutVersionTaps` | **先改成内联 `_aboutVersionTapCount = 0`** 再搬走 | 它被**生产代码**调用两次：`ShowTab:886`、`BuildAboutPage:2368`。直接把它当测试面搬走会让两处编译失败 |

### 5.1 执行状态

| 项 | 状态 | 说明 |
|---|---|---|
| `_updating` 字段 | ✅ 已删（`be40005`） | 判断本身在 P2 拆页时已随迁移代码一起消失 |
| `_coverTextureShared` | ✅ 已删（`be40005`） | `ReleaseCoverTexture` 改为无条件销毁 |
| `ExitBackgroundPreview(notify)` | ✅ 已删（`a50cd71`） | 随背景预览抽成独立面板一并去掉 |
| `RestoreLastPackage` / `LastPackagePath` | ✅ 已删（`df21e3f`） | 门面仅保留恒假/恒空实现供旧调用点编译 |
| `PreferencesScope` / footer 提示 | ✅ 已删（`0779371`） | Chart 页移出后 `document` 恒为 false，整组构件失去意义 |
| `FalsumDisplayConfiguration` 三件套 | ⏸ **未删** | `FalsumDisplaySettingsController.cs` 含他人未提交的本地化改动（5 处字符串替换），单独删会夹带他人工作。零调用者已确认，留给基线作者 |
| 本地化死键 `chartTitle`/`chartSubtitle` 等 | ⏸ **代码侧已无引用** | `Localization/*.json` 是**未跟踪文件**（他人未提交工作），未删除其中条目 |
| `Debug*` 测试面搬移 | ⏸ **未做** | 需要动 `FalsumSelfCheck.cs`（含他人 +257/-12 行），同上原因 |
| Chart 页 21 个本地化键 | ✅ **无需迁移** | Chart 页成为独立面板后仍使用原有 `falsum.preferences.chart.*` 键，未改名 |


## 6. 落地顺序

每阶段独立可验证、可回滚：

| 阶段 | 内容 | 验收 |
|---|---|---|
| **P0** | v6 schema + DTO + `Migration.FromDisk` + `ApplyProductGates` + `Store` 单写口 + `Advanced` 兼容门面 | 编译；v1/v5 profile 自动迁移；自检迁移用例改到 v6 |
| **P1** | 抽三个共享件（键捕获 / 背景预览 / 显示确认），行为不变 | 编译；截图基线**一张不改** |
| **P2** | 拆壳 + 7 个页类，Chart 页暂留壳内 | 编译；截图基线不改 |
| **P3** | 抽 `FalsumChartInfoPanel`，双层入口生效 | 编译 + 自检 + 12 张截图重做 + **5 个** capture/自检脚本改接新入口 |
| **P4** | 死码清理、本地化键迁移、`Advanced` 门面下线、文档同步 | `python Tools/falsum_localization_audit.py` 退出 0 |

### 6.1 P0 不能真正独立，必须带兼容门面

「P0 可单独合入、数据层与 UI 互不影响」这个判断是**错的**。
`Advanced` 组一旦在 v6 里拆散，下列读取点会立刻编译失败或读不到值：

| 消费方 | 读取点 |
|---|---|
| `FalsumPreferencesView.cs` | `178-180, 189, 207, 424, 2130, 2140, 2224, 2247, 2267, 2284, 2301, 2325, 2331, 2350, 2855` |
| `FalsumChartEditor.cs` | `300, 306, 5172-5173, 7246, 7573, 7600, 7625, 7676, 7690, 9494-9496` |
| `FalsumEditorView.cs` | `645` |
| `FalsumSelfCheck.cs` | `865-876` |
| `FalsumUiStyleCapture.cs` | `37`（写）、`204`（读 `Load().Advanced.UIStyle`） |

两条可行路径，**二选一**：

- **A（已选定）**：P0 里给 `FalsumPreferences` 加 `[Obsolete]` 兼容门面 ——
  `Advanced => _advancedFacade`，其属性读写转发到新组的字段。
  这样 P0 可独立合入，P4 删门面。
- **B（未采用）**：把 P0 挪到 P2 之后，让 UI 一并改完再切 schema。
  代价是 v6 迁移与 UI 拆解绑在一起，回滚粒度变粗。

**决策记录**：2026-11-03 采用 A。P0 独立合入，门面在 P4 下线。

无论哪条，**P0 的验收都必须包含一次真实 v5/v1 profile 的迁移**，
两者本机都有样本（§4.3 回归样本表）。

### 6.2 迁移 DTO 只需一个超集，不需要 6 个

实测两份真实 profile 后确认：**v1 的键集合是 v5 的真子集**，
整个演进过程只加字段、从未改名或挪动。

| 文件 | 键数 | 与 v5 的关系 |
|---|---|---|
| v1（`LocalLow\DefaultCompany\…`） | 15 | v5 的 26 个键**全部包含**它；反向差集为空 |
| v5（`LocalLow\Team Aak Tsao\…`） | 26 | 超集 |

因此 `FalsumPreferencesV1Dto … V6Dto` 六个类的方案可以砍掉，改为：

```csharp
// 一个覆盖 v1…v5 全部键的超集，所有字段不带初始值
sealed class FalsumLegacyPreferencesDto
{
    public int Version;                    // 0 = 缺键
    public FalsumDisplayPreferences Display;
    public List<FalsumKeyBinding> Bindings;
    public FalsumEditingPreferences Editing;
    public FalsumBackupPreferences Backup;
    public FalsumAudioPreferences Audio;
    public FalsumLegacyAdvancedDto Advanced;
}
```

- ~~每个字段默认是类型零值，`null` 就是「这一版还不存在该组」的标记~~
  **实测推翻**：`JsonUtility` 对 JSON 缺失的对象字段**不留 null**，而是构造
  带字段初始化器的默认实例（探针验证：解析 `{"V":1}` 到含 `Inner I` 的类，
  `I` 非 null 且成员为初始化值）。所以 v1（无 `Audio` 组）会被当成「存在但全零」，
  迁移后音乐音量变 0 —— 升级即静音。
- 正确做法：**按 JSON 顶层键是否存在判断**（`FalsumPreferencesMigration.JsonPresence`），
  逐组探测 `"Display"` / `"Bindings"` / `"Editing"` / `"Backup"` / `"Audio"` / `"Advanced"`
  是否出现，只对真正缺失的组套默认值。已补回归断言锁死「v1 不得静音」。
- `Version` 只在三处使用：`> 6` 的降级警告、迁移回写判定、以及
  **写入 `Normalize()` 之前**的 `previousVersion`（用于 `<2/<3/<5` 三个版本分支）。
  后者是实测踩到的坑：若先写 `CurrentVersion` 再 `Normalize()`，
  所有版本键控迁移都会被跳过（自检「v1 preferences were not migrated」抓到过）。
- 若日后真出现改名，才需要在这里分叉出按版本 DTO；目前无此需要。

> 注：仓库 `HEAD` 提交的版本是 **v3**（`CurrentVersion = 3`，`Advanced` 里
> 还没有 `ShowMeasureNumbers`/`UIStyle`）；v4–v5 只存在于工作区未提交改动。
> 所以「v3 → v6」这条路径必须支持，且它是当前 git 里的基线状态。

## 7. 风险

1. **v6 是破坏性迁移**：必须覆盖 v1–v5 全部旧 profile。
   关键前提是 §3.2-2 的闸门放宽 —— 现有 `Store.LoadFromDisk:113-120`
   在 `Normalize` 前就拒掉 `<1`，不改这处，`Version=0` 兜底形同虚设。
2. **截图基线必变**：`falsum_preferences_chart*.png` 四张在 P3 后重做，
   P3 之前不得当回归处理。
3. **自检耦合真实 View**：`FalsumSelfCheck.cs:901-940` 会
   `new FalsumPreferencesView(...)`（`:908`）再按控件名断言
   （`DebugFlushInitialTabBuild:920`、`PreferencesUIStyle` 查询 `:927`）；
   拆页后控件名会变，必须同步迁到 `TestApi`，否则自检直接挂。
4. **`_updating` 是死守卫**：删判断**不改变任何现有行为**，
   但 `:2136` 的 `style.index < 0` 必须保留（见 §5 陷阱栏）；
   若日后确有重入需求，需重新引入并**正确地赋值**。
5. **无法在本机验证的三项**（需 Unity 实跑）：JsonUtility 对「缺键」与
   `"Version": null` 的具体语义、迁移的字节级幂等、`OnDeactivate` 的
   纹理释放时序。P0 落地时这三项都要用真实 profile 跑一遍。

## 8. 不在本次范围

- 恢复 `FalsumUiStyleSwitcher` / `FalsumMeasureNumberFeature` / `FalsumSpectrumFeature`
  （三者仍是产品门禁，字段保留在 `Experimental`）。
- `FalsumChartEditor.cs`（现约 12908 行）的整体分区重构，
  只改与首选项接线相关的约 400 行。
- 备份历史列表（`BuildBackupPage` 内的文件列表交互）只搬位置，不改交互。
