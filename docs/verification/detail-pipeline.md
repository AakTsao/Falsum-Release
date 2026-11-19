# FALSUM 验证 / 构建流水线（附录）

> 正文版见 [`../03-pipeline.md`](../03-pipeline.md)（更短、更新更快）；本文件保留完整门禁表与全部实测日志行号。
>
> 只记录可复核事实。路径相对项目根 `InFalsus-Gameplay`。所有命令均为本机实测（Unity `6000.3.26f1`，项目 `ProjectSettings/ProjectVersion.txt`）。

## 1. 验证入口总表

| 验证入口 | MenuItem 路径 | -executeMethod 全名 | 产物 | 通过判据 |
|---|---|---|---|---|
| 编译检查（无 executeMethod） | — | — | `Diagnostics/UnityCompile.log` | 退出码 0，日志无 `error CS`（`Diagnostics/UnityCompile.log:380`） |
| 编辑器自检 | `InFalsus/Falsum/Run Editor Self Check`（`Assets/InFalsus/Editor/FalsumSelfCheck.cs:63`） | `InFalsus.EditorTools.FalsumSelfCheck.Run` | `Diagnostics/FalsumSelfCheck.json` | `passed=true` 且 `error=""`（写盘处 `FalsumSelfCheck.cs:2420-2437`） |
| BASS 音频自检 | `.../Run BASS Audio Self Check`（`:108`） | `...FalsumSelfCheck.RunBassAudio` | 同上（覆盖写） | 同上 |
| Note 字段自检 | `.../Run Tool Note Field Self Check`（`:126`） | `...FalsumSelfCheck.RunToolNoteFields` | 同上 | 同上 |
| 包兼容检查 | `.../Run Package Compatibility Check`（`:144`） | `...FalsumSelfCheck.RunPackageCompatibility` | 无文件 | 退出码 0；扫描 `../export/falsum/*.falsum`（`:147-149`） |
| Phase A 自检 | `InFalsus/Gameplay/Run Phase A Self Check`（`GameplaySelfCheck.cs:22`） | `...GameplaySelfCheck.Run` | `PhaseASelfCheck.txt`、`JudgementMatrix.csv`、`ChartParseReport.json`（`:628`、`:685-692`） | `failedFiles=0`、`errors=0` |
| 场景绑定报告 | `InFalsus/Gameplay/Report Scene Bindings`（`GameplaySceneBuilder.cs:267`） | `...GameplaySceneBuilder.ReportOnly` | `SceneBindings.json`、`SceneBindingReport.txt`（`:645-666`） | 无异常抛出 |
| 本地化审计 | `Tools/FALSUM/Localization/Audit`（`FalsumLocalizationAudit.cs:21`） | `InFalsus.Editor.FalsumLocalizationAudit.RunBatch`（**命名空间是 `InFalsus.Editor`**） | 无文件，仅日志 | 日志出现 `Falsum localization audit passed.`（`:27`） |
| 保护字体校验 | `InFalsus/Falsum/Validate Protected Song Font`（`FalsumProtectedSongFontBuilder.cs:149`） | `...ValidateProtectedSongFont` | 无文件 | 无异常 |
| 依赖审计 | `InFalsus/Falsum/Audit Project Dependencies`（`FalsumDependencyAudit.cs:37`） | `...FalsumDependencyAudit.Run` | `Diagnostics/FalsumDependencyAudit.tsv` | tsv 生成 |
| Windows 构建 | `InFalsus/Falsum/Build Windows Player`（`FalsumBuild.cs:32`） | `...FalsumBuild.BuildWindows` | `Builds/FalsumEditor-<Version>/FalsumEditor.exe` + 同名 `.zip`（`FalsumBuild.cs:73-116`） | `BuildResult.Succeeded`（`:93-99`） |

其余 31 个 `[MenuItem]`（共 44 个，`Assets/InFalsus/Editor/*.cs`）是截图/修复工具，**不是门禁**：`Falsum/Capture Editor`、`Capture UI Styles`、`Capture Performance Window`、`Capture Heavy Chart Performance`、`Capture Tool Workshop`、`Capture Runtime Play`、`Gameplay/Repair TrackEnd Composer`、`Falsum/Delete Unused Project Assets`（**破坏性，勿随手跑**）等。

> 2026-11-12 追记：`Capture 2D Editor`（`Falsum2DCapture.cs`）随 2D 模式删除，此表的总数口径不再准确（当前 `Assets/InFalsus/Editor/*.cs` 共 50 处 `[MenuItem]` 标注，其中 49 个字面量路径 + 1 个共享常量）。

## 2. 精确命令行（未标注者均已在本机跑通并留有日志）

设 `$proj` 为项目根，`$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe"`（已确认存在，`ProductVersion 6000.3.26f1_de8167e7440c`）。

(a) 编译检查（`Diagnostics/UnityCompile.log:12-19`，退出码 0）：
```powershell
& $unity -batchmode -quit -projectPath $proj -logFile "$proj\Diagnostics\UnityCompile.log"
```
(b) 编辑器自检（`Diagnostics/UnityFalsumSelfCheck.log:31-40`，**当前退出码 1**）：
```powershell
& $unity -batchmode -quit -projectPath $proj -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run -logFile "$proj\Diagnostics\UnityFalsumSelfCheck.log"
```
(c) Windows 构建（`Logs/falsum-build-0.4-alpha-optical.log:31-42`，`-nographics` + `-quit`）：
```powershell
& $unity -batchmode -nographics -projectPath $proj -executeMethod InFalsus.EditorTools.FalsumBuild.BuildWindows -quit -logFile "$proj\Logs\falsum-build-0.4-alpha-optical.log"
```
(d) 本地化审计（Unity 侧 `Logs/localization-runtime-audit-final-2.log:12-21`；源码侧已实测**当前退出码 1**）：
```powershell
& $unity -batchmode -quit -nographics -projectPath $proj -executeMethod InFalsus.Editor.FalsumLocalizationAudit.RunBatch -logFile "$proj\Logs\localization-runtime-audit.log"
python Tools/falsum_localization_audit.py   # 无 argv，只读，结果写 stderr/stdout
```
环境变量（唯一被构建/自检读取的是字体源，其余仅截图工具用）：`$env:FALSUM_SONG_FONT_SOURCE`（`FalsumProtectedSongFontBuilder.cs:13-17`，默认 `D:\Download\Fonts\A-OTF UD ShinGo Pr6N-Regular.ttf`，SHA-256 `71BD6D27D0B97CB4BE84AD34C00ADB6F79DA21BA52ED8E4D122E70C57D58D060`）；`INFALSUS_CAPTURE_TIMES/PREFIX/AUTOPLAY/TRACK_END_MASK/SKY_MASK/WIDTH/HEIGHT/SKIP_SCREENSHOTS/PRESERVE_CHART`（`GameplayPlayCapture.cs:60-74`、`:147`、`:442-445`）、`INFALSUS_UI_CAPTURE_WIDTH/HEIGHT`（`FalsumUiStyleCapture.cs:78-79`）、`INFALSUS_PERFORMANCE_PACKAGE`、`INFALSUS_FORCE_TRACKEND_REBUILD`（`FalsumHeavyChartPerformanceCapture.cs:27-31`）。
**已更正（原判断有误）**：README `Deterministic Visual Debugging` 里的 `$env:INFALSUS_CHART_PATH`
**确实有效**——读取点是
`Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs:1056-1058`（`ResolveChartPath`，
相对路径按项目根解析）。早期审计只搜 `Assets/InFalsus/Editor/`，因此误判为失效。
另外补充几个未列出的变量：`INFALSUS_TRACK_END_SEED`、`INFALSUS_SEAM_ONLY`（`FalsumTrackEndComposer.cs`）。

## 3. 当前真相（截至 2026-11-01 15:4x）

- **最近通过**：墙体自检（`Diagnostics/GameplayWallSelfCheck.txt`，10/31 20:11，`status=PASS`；日志 `UnityGameplayWallSelfCheck.log:327`）与墙体截图（`GameplayWallCapture.txt`，10/31 20:08，`visibleChangedPixels=10758`、`animatedChangedPixels=14869`、`status=PASS`）。编译检查 10/31 15:14，退出码 0。
- **当前失败**：`Diagnostics/FalsumSelfCheck.json`（10/31 15:35）`passed=false`，`checks` 只到 `event_editing`（21 项，28 项中）后中断，`error` 为 `InvalidOperationException: Design rule tool description is incorrect.`
- **失败原因（仍然是真失败）**：断言 `designRuleManifest.Description == "检查设计规则"`（`FalsumSelfCheck.cs:251`），而工作区 `Assets/StreamingAssets/Falsum/UserTools/com.falsum.tools.design-rule-checker/tool.json` 的 `description` 已被改成 `""`、`name` 改成 `design-rule-checker`（HEAD 版本是 `检查设计规则`）。`Description` 只读 tool.json 原始字段（`FalsumToolModels.cs:142-145`），不读 i18n；本地化迁移后断言未同步。修复二选一：恢复 tool.json 描述，或把断言改为校验 `DisplayDescription`/i18n `tool.description`。
- **最新的日志**：`Diagnostics/UnityGameplayWallSelfCheck.log`、`Diagnostics/UnityGameplayWallCapture.log`（20:08-20:11）、`Diagnostics/UnityFalsumSelfCheck.log`（15:35，末尾 `Application will terminate with return code 1`）、`Diagnostics/UnityCompile.log`（15:14）。`Logs/` 下最新为 `localization-runtime-audit-final-2.log`（10/30）。
- **README / 计划与现实的对账**：`README.md:252-256` 仍写产物是 `Builds/FalsumEditor-Alpha0.31-20261026`，实际代码产出 `Builds/FalsumEditor-0.5-Alpha`（`FalsumBuild.cs:21-22`，磁盘上两个目录都在，后者 10/28 为最新）；`README.md:273-282` 的 Diagnostics 菜单清单缺 `InFalsus/Gameplay/Run Gameplay Wall Self Check` 与 `Capture Gameplay Wall`（这两个是 10/31 新增的必跑门禁）；`README.md:287-305` 的确定性调试段已失效（见 §2）；`docs/superpowers/plans/2026-10-31-...-plan.md` 的"实施结果"与现状一致（墙体通过、Editor Self Check 被 design rule 描述拦截）；`docs/superpowers/plans/2026-10-30-...-plan.md:190-202` 声称"隔离工程中 Run Tool Note Field Self Check 通过"，本机**未验证**（主工程曾拒绝重启捕获）。
- **工作区未提交**：HEAD `f4e03c9`；`M Diagnostics/FalsumSelfCheck.json`（HEAD 里是 `passed=true`/27 项）、`?? Diagnostics/GameplayWallSelfCheck.txt`、`?? Diagnostics/GameplayWallCapture.txt`、`?? Tools/falsum_localization_audit.py`、`?? Assets/InFalsus/Editor/{GameplayWallSelfCheck,GameplayWallCapture,FalsumLocalizationAudit,FalsumUiStyleCapture}.cs`，另有 60+ 个已跟踪文件被改（`.gitignore` 不跟踪 `Diagnostics/*.log`、`Screenshots`、`Builds`、`Logs`、`autobackups`）。

## 4. 不可信的陈旧产物（新开发者请忽略）

`Diagnostics/` 顶层 391 个文件（含子目录 864 个，`Screenshots/` 471 张）中 **357 个早于 10/28**，都是逐轮迭代的一次性日志：`play_capture*.log`、`sky_debug*.log`、`ui-toolkit-*.log`、`trackend-*.log`、`falsum-ime-*.log`、`falsum-auto-combo-*.log`、`spectrum-*.log`，以及 `*.pid`、`Assembly-CSharp-*.dll/.pdb`（旧编译产物），命名带 `-2/-3/-final/-final2` 的重复尝试。具体忽略理由：

- `Diagnostics/Evidence.md`、`Diagnostics/PhaseA_Progress_2026-10-17.md`、`Diagnostics/FalsumEditor_Audit_2026-10-24.md`：10/17-10/24 快照，结论（背景纯黑、NoteFX 未接）已被后续实现取代。
- `ChartParseReport.json`、`JudgementMatrix.csv`、`SceneBindings.json`、`SceneBindingReport.txt`、`PhaseASelfCheck.txt`（均 10/25 20:35）：Phase A 单次运行输出，非当前构建产物。
- `MeshDiagnostics.txt`（10/18）、`SceneInventory.tsv`/`ShaderUsage.tsv`/`MissingScripts.tsv`/`RuntimeRenderers.tsv`（10/17）、`FalsumDependencyAudit.tsv` 与 `FalsumCleanupManifest.tsv`（10/21）：清理作业前的一次性报表，与当前资产集不符。
- `Diagnostics/falsum-ui-style-selfcheck*.log`、`capture*.log`（10/30）与 `Logs/` 中 588 个历史日志：都停在 `Preferences dialog controls are missing` / `Preferences serialization failed` 等旧失败，已被 10/30 之后的改动覆盖。
- `Diagnostics/Screenshots/` 里除 `gameplay_wall_full_presentation_t0.0…t3.5`、`gameplay_wall_presentation_t0.0…t3.5`、`gameplay_wall_control.png`（10/31 20:08）之外，`graphy… t10.x`、`falsum_ui_*`（10/30 15:21）、`wall_runtime_*`、`falsum_standalone_*` 均为旧实验。
- `Diagnostics/FalsumSelfCheck.json` 的 `checks` 数组**不能当覆盖率证据**：失败即中断，只记录已通过项；HEAD 版本还是 `passed=true`（10/29 提交），比源码（28 个 `checks.Add`）旧。

## 5. 流水线不变量

1. **没有保护字体就不许构建**：`Assets/StreamingAssets/Falsum/Fonts/SongInfoCard.fpk` 缺失时 `FalsumBuild.BuildWindows` 直接抛 `FileNotFoundException`（`FalsumBuild.cs:29-30`、`:41-47`）；只允许用 `InFalsus/Falsum/Rebuild Protected Song Font` 重建，且源 ttf 必须 SHA-256 匹配（`FalsumProtectedSongFontBuilder.cs:18-20`、`:163-181`）。
2. **没有运行时场景就不许构建**：`Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` 必须存在（`FalsumBuild.cs:23-24`、`:35-40`），且它是 `EditorBuildSettings.asset` 的首个场景。
3. **构建会把外部内容拷出 StreamingAssets**：`GameplayBackgrounds → GameplayBackgrounds`（原位置，不改名）、`UserTools → ExternalTools`（`FalsumBuild.cs:25-28`、`:119-131`），随后**删除** `FalsumEditor_Data/StreamingAssets/Falsum/{GameplayBackgrounds,UserTools}`（`:133-149`），并清掉 exe 同级误留的 `Backgrounds/` 旧副本。因此不要指望这两份内容通过 StreamingAssets 随包发布；源目录缺失会抛异常（`:164-169`）。构建末尾 `VerifyPublishedBackgrounds` 会逐个校验发布目录能解析映射表与 `.png` 逻辑名，解析不到即构建失败。
4. **版本串唯一来源**：`FalsumEditorInfo.Version = "0.46-Canary"`（`Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorInfo.cs:12`）→ 目录名 `FalsumEditor-0.46-Canary`（`FalsumBuild.cs:21-22`）；`PlayerSettings.bundleVersion` 另有硬编码 `"0.46.0"`（`FalsumBuild.cs:19`）。改版本必须同时改这两处与 README。
5. **构建不是只读操作**：`BuildWindows` 会依次调用 `FalsumUiAssetBuilder.EnsureAssets()`（改写 `FalsumPanelSettings.asset`）、`FalsumTrackEndComposerInstaller.EnsureInstalled(true)`（改写 `Assets/Settings/PC_Renderer.asset`）、`GameplaySceneBuilder.SanitizeNoteTemplatePrefabs()`（重写音符 prefab）（`FalsumBuild.cs:48-50`），并改写 `PlayerSettings`（`:52-65`）。构建后必须 `git status` 确认没有意外资产漂移。
6. **自检报告每次覆盖写，失败也写**：`WriteReport`（`FalsumSelfCheck.cs:2420-2437`）无条件写 `Diagnostics/FalsumSelfCheck.json`；只有 `passed=true && error==""` 才算过，日志出现 `executeMethod method ... threw exception` + `return code 1` 即失败。
7. **自检与用户工具元数据强耦合**：`FalsumSelfCheck.cs:251` 直接比对 `tool.json` 的 `description` 字面值；任何本地化迁移（把文本挪到 `i18n/<locale>.json`）都会打断该断言。
8. **本地化零硬编码**：UI 文本只允许走 `FalsumLocalization`；`python Tools/falsum_localization_audit.py` 退出码必须为 0。当前 4 处违规在 `Assets/InFalsus/Gameplay/Falsum/UI/FalsumPreferencesView.cs:2514/2516/2518/2520`。
9. **顺序门禁**：编译（无 `-executeMethod`）→ Editor Self Check → Gameplay Wall Self Check → Gameplay Wall Capture → Build。前一步失败不要继续，因为构建会写资产且覆盖 `Builds/FalsumEditor-0.5-Alpha` 与同名 zip。
10. **批处理场景副作用**：`GameplayWallSelfCheck`/`GameplayWallCapture` 用 `EditorSceneManager.OpenScene(..., Single)` 打开运行时场景且不保存（`GameplayWallSelfCheck.cs:244-246`、`GameplayWallCapture.cs:37-39`）。不要在有人正在编辑的场景上跑；跑前确认没有 `Temp/UnityLockfile` 与 Unity 进程。
11. **TrackEnd Composer 自恢复**：编辑器非批处理启动时会自动调用 `EnsureInstalled`（`FalsumTrackEndComposerInstaller.cs:17-28`），批处理下跳过；因此“PC_Renderer.asset 缺特性”只可能在批处理链路里出现。
12. **备份路径即产品行为**：`autobackups` 位于**可执行文件同级**（`FalsumBackupService.cs:29`），布局 `<songId>-<路径SHA256前12位>/d0..d3/*.chart.txt`（`:212-226`、`:73-77`）；`autobackups/workflow-selfcheck-*` 是自检残留，可删。
