# FALSUM 新建谱面强制填写歌曲 ID 设计

状态：设计已确认（2026-11-08），待实现。
范围：FALSUM 谱面编辑器（`Assets/InFalsus/Gameplay/Falsum/`）+ 一处编辑器自检 + 两份本地化资源。
用户诉求原文：**创建谱面时强制填写 id 字段，id 字段的填写规则为全小写字母**。

已与用户确认的两条裁决：

1. 字符规则 = **以小写字母开头，其余只允许小写字母与数字**：`^[a-z][a-z0-9]{0,63}$`。
2. `.falsum` 文件名**与 id 解耦**：id 只进 `song.json` 与谱面头 `# song:`，文件名沿用用户在保存对话框里选的路径。
3. `id` 输入框**默认留空**，不在打开时用文件名词干预填（预填会使「强制填写」形同虚设）。
4. **不做即时改写**：不把输入实时转小写、不静默丢弃非法字符；一切非法输入留到点「创建」时报错，做到键位所见即所存。
5. 唯一的例外是**首尾空白 `Trim()`**：与 `Title`/`Artist`（`FalsumEditorView.cs:3262-3264`）和 `FalsumManifest.SetSongId`（`FalsumManifest.cs:373`）一致，粘贴带来的首尾空格不视为非法内容。

---

## 1. 目标与非目标

### 1.1 目标

1. 新建谱面包对话框里出现**歌曲 ID**输入框，为空时创建被拒绝。
2. 创建时 ID 必须匹配 `^[a-z][a-z0-9]{0,63}$`，否则拒绝并逐条说明原因。
3. 通过校验的 ID 同时写入三处：`song.json` 的 `songId`、每个可用难度 chart 头的 `# song:`、`Chart.SongId`（与既有的 `_manifest.SongId` 传播路径一致）。
4. 新建流程**不再改写**用户选定的保存路径，文件名与 ID 独立。
5. 既有 74 个官方包的读取路径**一律不变**，旧 ID（含 `_`、`-`）仍可加载、另存、备份。

### 1.2 非目标

| 不做 | 理由 |
|---|---|
| 改 `FalsumManifest.Validate` / `IsValidSongId` 的既有正则 | 74 个官方包虽全部满足新规则（已逐包核对），但自检固件 `partial_safety`、`localization-audit` 等带 `_`/`-` 的 ID 会被当场拒载；用户手上的旧包同理。收紧读规则不是本需求的授权范围 |
| 给老包提供「ID 迁移向导」 | 元数据页（`FalsumChartInfoPanel`）已有可编辑 ID 字段并走 `FalsumMetadataApplyService.RewriteSongIds` 全难度重写，覆盖了改 ID 的诉求 |
| 新建时检查 ID 全局唯一 | 规范本身写明「规范不引入 UUID；发行者负责保证 `songId` 在自己管理的曲库中唯一」（`../docs/superpowers/specs/2026-10-18-falsum-package-format-design.md` §3.3）。本地无从枚举用户曲库 |
| 实时输入过滤（转小写 / 剔非法字符） | 用户明确要求「创建时报错」。静默改写会让用户拿到的 ID 与敲进去的不是同一个字面量 |

---

## 2. 关键设计裁决（每条都有代码事实支撑）

### 2.1 两套 ID 规则并存，互不越界

现有正则集中在两处，都是**读侧**：

- `FalsumManifest.SongIdPattern = ^[a-z0-9][a-z0-9_-]{0,63}$`（`FalsumManifest.cs:85-87`），被 `IsValidSongId` 用于 `Validate()`（`:562`），进而门控一切 `FalsumPackage.Load`。
- `FalsumChartMetadataDraft.SongIdPattern`（`FalsumChartMetadataDraft.cs:12-14`）是同一正则的副本，供元数据页校验。

裁决：**保留这两处原样**，另外在 `FalsumManifest` 上新增仅用于新建的规则：

```csharp
private static readonly Regex NewSongIdPattern =
    new Regex("^[a-z][a-z0-9]{0,63}$", RegexOptions.CultureInvariant);

public static bool IsValidNewSongId(string value)
{
    return !string.IsNullOrEmpty(value) && NewSongIdPattern.IsMatch(value);
}
```

命名上刻意与 `IsValidSongId` 区分：调用点一眼能看出自己走的是哪套规则，不会出现「顺手把新建规则套到读侧」的漂移。这条写进 `02-invariants.md` 新增条目 A-24。

`NormalizeSongId`（`:387-428`，唯一调用点是 `FalsumPackage.CreateNew`）保留不删：它仍要处理「用户给的 ID 恰好合法、无需归一」以外的历史调用（自检与将来的导入路径），但**新建的主路径不再依赖它**——新建改为「严格校验，不归一」。

### 2.2 `CreateNew` 不再改写文件名

现状（`FalsumPackage.cs:192-205`）：

```csharp
var songId = FalsumManifest.NormalizeSongId(
    Path.GetFileNameWithoutExtension(targetPath));
var normalizedTarget = Path.Combine(..., songId + ".falsum");
if (!string.Equals(targetPath, normalizedTarget, OrdinalIgnoreCase))
{
    targetPath = normalizedTarget;
    request.TargetPath = targetPath;      // 直接改用户请求
}
```

这段是「文件名 == songId」惯例的唯一实现点，也是本需求的直接冲突点。裁决：

- 删除整段路径改写，`SourcePath` 就是 `Path.GetFullPath(request.TargetPath)`。
- `songId` 改取 `request.SongId`（`Trim()` 后），并在 `CreateNew` 内用 `IsValidNewSongId` 做一次 **fail-closed 二次校验**：对话框校验是第一道闸，`CreateNew` 是第二道闸——它也在自检里被直接调用，不能只依赖 UI。
- 校验失败抛 `ArgumentException`，消息走本地化键 `falsum.error.package.songIdRequired` / `falsum.error.package.songIdInvalid`，与文件里其它参数校验（`:180-191`）同风格。

影响面确认：

- 备份目录布局是 `<songId>-<路径哈希>`（`FalsumBackupService.cs`），`songId` 仍在，哈希来自路径，**两个分量都继续存在**，`02-invariants.md` A-11 不受影响。
- Save-As 的默认文件名仍可建议 `_manifest.SongId + ".falsum"`（`FalsumChartEditor.cs:6121`），那只是对话框初始值，用户可改；解耦只意味着「不再被迫相等」。
- `FalsumPackage.Load` 从不推导 songId，只读 `song.json`，因此新包（文件名 ≠ ID）可被正常读回。

### 2.3 对话框字段位置与绑定

`FalsumNewChartDialog.uxml` 当前右列首个字段是标题（`:25-28`）。ID 作为包的**主键**，应排在标题之前，与「唯一标识 → 展示信息」的阅读顺序一致：

```xml
<ui:VisualElement class="new-chart-title-field">
    <ui:Label name="l10n.falsum.newChart.songIdField" class="new-chart-field-label" />
    <ui:TextField name="NewChartSongId" class="falsum-field new-chart-input" max-length="64" />
</ui:VisualElement>
```

`max-length="64"` 与正则上限 `{0,63}` + 1 位首字母对齐。

`FalsumEditorView` 侧按现有模式接：

- 字段 `private TextField _newChartSongId;`，在 `ShowNewChartDialog` 的 `Require<TextField>` 段（`:2312-2326` 附近）解析。
- `ShowNewChartDialog` 里 `_newChartSongId.value = string.Empty;`（与 `_newChartAudioPath = string.Empty;` 同一处，`~:2259`），**不预填**。
- `placeholder = L.Get("falsum.newChart.songIdPlaceholder")`，与其它 placeholder 同批（`:2375-2384`）。
- `RegisterValueChangedCallback` 只做 `_newChartDirty = true; ClearNewChartError();`，与 `_newChartTitle` 完全同构（`:2891-2896`）——**不转小写、不过滤**。
- 校验：在 `TryValidateNewChart`（`:3169`）里，紧跟标题/作者之后加两条：

```csharp
var songId = (_newChartSongId != null ? _newChartSongId.value : string.Empty).Trim();
if (songId.Length == 0)
{
    error = L.Get("falsum.error.newChart.songIdRequired");
    MarkInvalid(_newChartSongId);
    return false;
}
if (!FalsumManifest.IsValidNewSongId(songId))
{
    error = L.Get("falsum.error.newChart.songIdInvalid");
    MarkInvalid(_newChartSongId);
    return false;
}
```

- `BuildNewChartRequest`（`:3247`）增加 `SongId = songId`（同一 `Trim()` 结果）。
- `ClearNewChartFieldValidation`（`:3137`）增加 `_newChartSongId.RemoveFromClassList("is-invalid")`。
- `Request.CloseNewChartDialog` 的 `_newChartDirty` 已有语义覆盖（输入即 dirty），字段清空不需要新增分支。

### 2.4 数据模型

`FalsumNewChartRequest`（`FalsumNewChartRequest.cs`）加一行：

```csharp
public string SongId;
```

不设非空默认值（默认 `null`，让「漏填」在 `CreateNew` 的二次校验里显式失败，而不是悄悄变成空串通过某处 `?? string.Empty`）。

### 2.5 本地化键

两个 locale 必须同步补齐，否则 `Tools/falsum_localization_audit.py` 在 CI 里拦（铁律 5）：

| 键 | zh-CN | en-US |
|---|---|---|
| `falsum.newChart.songIdField` | `歌曲 ID  *` | `Song ID  *` |
| `falsum.newChart.songIdPlaceholder` | `全小写字母，可带数字` | `Lowercase letters, digits only` |
| `falsum.error.newChart.songIdRequired` | `歌曲 ID 不能为空。` | `Song ID is required.` |
| `falsum.error.newChart.songIdInvalid` | `歌曲 ID 必须以小写字母开头，只能包含小写字母和数字，最多 64 个字符。` | `Song ID must start with a lowercase letter and contain only lowercase letters and digits, up to 64 characters.` |
| `falsum.error.package.songIdRequired` | `新建谱面包必须提供歌曲 ID。` | `A song ID is required to create a package.` |
| `falsum.error.package.songIdInvalid` | `歌曲 ID 格式不合法：{songId}` | `The song ID is invalid: {songId}` |

`*` 号与 `falsum.newChart.titleField` / `artistField` 的「必填」标记一致（`titleField` 现为 `标题  *`）。

---

## 3. 验证

自检项数（`checks.Add("…")` 清单，当前 28 项）**不新增**：新断言挂在既有项内，避免文档里的项数事实源再次过期。

| 位置 | 新增断言 |
|---|---|
| `FalsumSelfCheck.CheckUiToolkitResources`（`:776-777`） | `NewChartSongId` 的 `Q<TextField>` 存在（并进既有那条 `Require`） |
| `FalsumSelfCheck.CheckManifestSafety`（`:518` 附近） | 边界表：`IsValidNewSongId("a")`/`("abc123")`/64 字符 真；`("")`/`("Abc")`/`("1abc")`/`("a-b")`/`("a_b")`/`("a b")`/65 字符 假；同时断言 `IsValidSongId("a-b")` **仍为真**，锁死两套规则不串味 |
| `FalsumSelfCheck.CheckMenuWorkflowServices`（`:3343-3368`） | request 补 `SongId = "workflow-selfcheck"`；断言 `loaded.Manifest.SongId` 等于该值；断言 `package.SourcePath` **仍等于** 用户给的 `targetPath` 且文件名为 `workflow-selfcheck.falsum`（即不再被改写）；再断言 ID 与文件名不一致时（用另一个词干路径）也成功建包 |
| 同上，反向 | `FalsumPackage.CreateNew` 收到空 ID / 非法 ID 时抛异常（`RequireThrows<ArgumentException>`） |

命令级验收（`docs/memory/03-pipeline.md` §2）：

1. `python Tools\falsum_localization_audit.py` —— 退出码 0（新增 6 个键在两侧都齐）。
2. Unity 编译（`-batchmode -quit`）—— 日志无 `error CS`。
3. 编辑器自检（`-executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run`）—— `passed=true`。

另跑一次包兼容门（`RunPackageCompatibility`），确认 74 个官方包仍全部通过——它是对「读侧的旧正则没被动过」的直接证据。

---

## 4. 文档同步

- `docs/memory/01-architecture.md` §A2 的 `songId` 行：注明「读侧正则不变；新建侧另有 `^[a-z][a-z0-9]{0,63}$`，且新建不再要求文件名 == songId」。
- `docs/memory/02-invariants.md` 新增 A-24：两套 ID 规则不得混用 + 新建不归一文件名。
- `docs/memory/04-history.md` **不动**：历史记录里的旧正则当时是唯一规则，符合事实。

---

## 5. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 用户把 ID 敲成 `Song A` 后点创建，得到「格式不合法」而不是被悄悄改写成 `songa` | 这是用户明确选择的行为；错误文案把完整规则写死，一次就能改对 |
| 旧包能否继续用 | 读侧正则一字未动；`RunPackageCompatibility` 74 包全过作为证据 |
| 文件名解耦后「找不到包」的错觉 | 编辑器顶栏、Song Info 卡片、偏好页路径显示均来自 `_package.SourcePath`，不看 ID；备份目录按 `songId + 路径哈希` 生成，不依赖相等关系 |
| 自检固件里其它 `CreateNew` 调用点漏填 `SongId` | 全仓仅 2 处调用（`FalsumSelfCheck.cs:3358`、`FalsumChartEditor.cs:5303`）；`CreateNew` 的二次校验会在漏填时 fail-closed 抛异常，自检立刻暴露 |
