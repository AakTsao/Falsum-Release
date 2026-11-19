# 03 · 验证与构建流水线

> 只有一条验收通道：**编译 + 自检 + 截图 + 构建**。没有单测框架，没有 CI 配置文件，
> 所有断言都写在 `Assets/InFalsus/Editor/*.cs` 里。
> 全部命令在本机实测过（Unity `6000.3.26f1`，路径 `C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe`）。

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$proj  = (Get-Location).Path          # 仓库根
```

---

## 1. 门禁入口总表

| 门禁 | MenuItem | `-executeMethod` | 产物 | 通过判据 |
|---|---|---|---|---|
| 编译 | — | —（不带 `-executeMethod`） | `Diagnostics/UnityCompile.log` | 退出码 0，日志无 `error CS` |
| 编辑器自检 | `InFalsus/Falsum/Run Editor Self Check` | `InFalsus.EditorTools.FalsumSelfCheck.Run` | `Diagnostics/FalsumSelfCheck.json` | `passed=true` 且 `error=""` |
| BASS 音频自检 | `…/Run BASS Audio Self Check` | `…FalsumSelfCheck.RunBassAudio` | 同上（覆盖写） | 同上 |
| Note 字段自检 | `…/Run Tool Note Field Self Check` | `…FalsumSelfCheck.RunToolNoteFields` | 同上 | 同上 |
| 包兼容 | `…/Run Package Compatibility Check` | `…FalsumSelfCheck.RunPackageCompatibility` | 无文件 | 退出码 0；扫描 `../export/falsum/*.falsum`（74 个） |
| 音频预处理探针 | `InFalsus/Falsum/Run Audio Preprocess Probe` | `…FalsumAudioPreprocessProbe.Run` | `Diagnostics/AudioPreprocessProbe.json` | `passed=true`；对真实包音频做裁头/前置/零位移三种重写并校验时长 |
| 音频预处理布局 | `InFalsus/Falsum/Verify Audio Preprocess Layout` | `…FalsumAudioPreprocessCapture.Run` | `Diagnostics/AudioPreprocessLayout.txt` | 末行 `PASSED`；断言样式表已挂、class 与 selector 命中 |
| Phase A 自检 | `InFalsus/Gameplay/Run Phase A Self Check` | `…GameplaySelfCheck.Run` | `PhaseASelfCheck.txt`、`JudgementMatrix.csv`、`ChartParseReport.json` | 0 失败文件、0 解析错误 |
| 场景绑定报告 | `InFalsus/Gameplay/Report Scene Bindings` | `…GameplaySceneBuilder.ReportOnly` | `SceneBindings.json`、`SceneBindingReport.txt` | 无异常 |
| 本地化审计（Unity） | `Tools/FALSUM/Localization/Audit` | `InFalsus.Editor.FalsumLocalizationAudit.RunBatch` ⚠ 命名空间是 `InFalsus.Editor`，不是 `EditorTools` | 仅日志 | 日志出现 `Falsum localization audit passed.` |
| 本地化审计（脚本） | — | `python Tools/falsum_localization_audit.py` | stdout/stderr | 退出码 0 |
| 保护字体校验 | `InFalsus/Falsum/Validate Protected Song Font` | `…FalsumProtectedSongFontBuilder.ValidateProtectedSongFont` | 无 | 无异常 |
| Windows 构建 | `InFalsus/Falsum/Build Windows Player` | `…FalsumBuild.BuildWindows` | `Builds/FalsumEditor-<Version>/` + 同名 `.zip` | `BuildResult.Succeeded` |

其余 `[MenuItem]` 是截图/修复工具，**不是门禁**。当前 `Assets/InFalsus/Editor/*.cs` 共有
50 处 `[MenuItem]` 标注，除上表（及 `appendix/detail-pipeline.md` 补的依赖审计）外都是截图/修复工具。
原来的 `Capture 2D Editor` 已随 2D 模式于 2026-11-12 删除（旧文档记作「31 个非门禁」）。
其中 `InFalsus/Falsum/Delete Unused Project Assets` **会删资产**，不要随手跑。

---

## 2. 命令

### (a) 编译检查（最快，先跑）

```powershell
& $unity -batchmode -quit -projectPath $proj -logFile "$proj\Diagnostics\UnityCompile.log"
```

### (b) 编辑器自检

```powershell
& $unity -batchmode -quit -projectPath $proj `
  -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run `
  -logFile "$proj\Diagnostics\UnityFalsumSelfCheck.log"
Get-Content "$proj\Diagnostics\FalsumSelfCheck.json" -Raw
```

### (c) Windows 构建

```powershell
& $unity -batchmode -nographics -quit -projectPath $proj `
  -executeMethod InFalsus.EditorTools.FalsumBuild.BuildWindows `
  -logFile "$proj\Logs\falsum-build-$(Get-Date -Format yyyyMMdd-HHmmss).log"
```

产物 `Builds/FalsumEditor-0.5-Alpha/{FalsumEditor.exe, ExternalTools/, GameplayBackgrounds/}` + 同名 zip。

背景folder 保持在**exe 同级的原位置** `GameplayBackgrounds/`（不要改名成 `Backgrounds/`）；
构建会删掉误留的 `Backgrounds/` 旧副本。构建末尾 `FalsumBuild.VerifyPublishedBackgrounds`
会用 `FalsumRuntimePaths.ResolveBackgroundFile` 校验发布目录：
映射表里每个 `background_file`、每个物理背景的 `.png` 逻辑名、默认背景都必须
在 `GameplayBackgrounds/` 里解析到实际文件；解析不到直接抛异常，构建失败。

### (d) 应用图标

Windows 播放器的窗口/任务栏/exe 图标来自 **PlayerSettings 的 Standalone Application 槽位**，
Unity 要求 **8 个固定尺寸**（1024 / 512 / 256 / 128 / 64 / 48 / 32 / 16，见
`PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Application)`）。
少给一张就会静默失败——日志里只有
`Attempting to set an incorrect number of icons for Standalone: 8 expected while 1 were specified.`，
`m_BuildTargetIcons` 保持 `[]`，而 `Debug.Log` 仍打印成功。

图标位图在 `Assets/InFalsus/Branding/AppIcon/FalsumAppIcon-<size>.png`；
256 及以下取自用户提供的 `.ico` 原生帧，1024/512 是 256 的 LANCZOS 放大。
`FalsumAppIcon.Apply()` 已接在 `FalsumBuild.BuildWindows` 里，构建前自动写 PlayerSettings；
单独执行：

```powershell
& $unity -batchmode -nographics -quit -projectPath $proj `
  -executeMethod InFalsus.EditorTools.FalsumAppIcon.Apply `
  -logFile "$proj\Diagnostics\UnityIconApply.log"
# 判据：ProjectSettings.asset 里 m_BuildTargetIcons 出现 8 条 m_Icon（不是 []）
```

换图标时替换这 8 张 PNG（保持同名同尺寸），再跑上面的命令；不要只塞一张。

### (e) 本地化审计

```powershell
python Tools/falsum_localization_audit.py            # 源码侧，秒级
& $unity -batchmode -quit -nographics -projectPath $proj `
  -executeMethod InFalsus.Editor.FalsumLocalizationAudit.RunBatch `
  -logFile "$proj\Logs\localization-runtime-audit.log"
```

---

## 3. 当前真相（2026-11-01 接手时）

| 项目 | 状态 | 证据 |
|---|---|---|
| 编译 | **PASS** | `Diagnostics/UnityCompile.log`，11-02 15:01，退出码 0 |
| 编辑器自检 | **PASS** | `Diagnostics/FalsumSelfCheck.json` 11-02，`passed=true`，28/28 项 |
| Phase A 自检 | **PASS** | `Diagnostics/UnityGameplaySelfCheck.log` 11-02，退出码 0 |
| 本地化（脚本 + 运行时） | **PASS** | `Tools/falsum_localization_audit.py` 与 `FalsumLocalizationAudit.RunBatch` 均通过 |
| 音频预处理探针 | **PASS**（2026-11-06） | `Diagnostics/AudioPreprocessProbe.json`，`passed=true`；alamode.falsum 135670 ms → 裁头 135420 / 前置 135920 / 零位移 135670 |

### 编辑器自检为什么曾失败（**2026-11-02 已修**）

`FalsumSelfCheck.cs:251` 原来断言 `designRuleManifest.Description == "检查设计规则"`，
而 `Assets/StreamingAssets/Falsum/UserTools/com.falsum.tools.design-rule-checker/tool.json`
的 `description` 已被本地化迁移改成 `""`。`FalsumToolManifest.Description` 读的是
`tool.json` 原始字段，**不读 `i18n/`**（显示层用的是 `DisplayDescription`）。

现在断言改为「`DisplayDescription` 非空，且与 `Description` 不同」，即只校验
本地化确实生效，不比对具体文案；`report.overlap.*` 的期望标题同样改为从
`manifest.GetText(key, ...)` 取。这样换语言或改文案都不会误报。

### 本地化为什么曾失败（**已修**）

`FalsumPreferencesView.IsVerboseDescription`（`:2511`）里曾有 4 条中文前缀字面量，
现改为读 `falsum.preferences.layout.verbosePrefixes`（两语言各一份，`|` 分隔）。
不要给审计脚本加白名单。

---

## 4. 不可信的陈旧产物

`Diagnostics/` 顶层 391 个文件里 **357 个早于 10-28**，全是逐轮迭代的一次性日志。
判断标准：**文件名带日期/序号后缀（`-2`、`-3`、`-final`、`-final2`）几乎都是废弃尝试**。

| 该忽略 | 为什么 |
|---|---|
| `Diagnostics/Evidence.md` | 10-17 快照；里面「背景纯黑」「NoteFX 未接线」都已被后续实现取代 |
| `Diagnostics/PhaseA_Progress_2026-10-17.md` | 同上；坐标与裁切规则仍有效，可查 |
| `Diagnostics/FalsumEditor_Audit_2026-10-24.md` | 34 条缺陷里 31 条已修，其余 F-10/F-18/F-20 随 2D 模式于 2026-11-12 整体移除而关闭（代码已不存在，无需修复） |
| `ChartParseReport.json` / `JudgementMatrix.csv` / `SceneBindings.json` / `PhaseASelfCheck.txt` | 10-25 单次运行输出，非当前构建 |
| `*Inventory.tsv` / `*Usage.tsv` / `MissingScripts.tsv` / `FalsumCleanupManifest.tsv` | 清理作业前的一次性报表，与当前资产集不符 |
| `Diagnostics/Screenshots/` 里全部 | 旧实验（`falsum_ui_*`、`trackend_*`、`sky_*`、`preview_parity_*`…） |
| `Logs/` 下 575 个历史日志 | 都停在旧失败点（`Preferences dialog controls are missing` 等） |
| `Diagnostics/FalsumSelfCheck.json` 的 `checks` 数组 | 失败即中断，只记录已通过项；**不能当覆盖率证据** |

**当前真相只有这几个**：
`Diagnostics/FalsumSelfCheck.json`、`UnityCompile.log`、`UnityFalsumSelfCheck.log`。

---

## 5. 截图工具的确定性环境变量

只影响截图/诊断，不影响自检结论：

| 变量 | 用途 |
|---|---|
| `INFALSUS_CHART_PATH` | **覆盖加载的谱面**（`GameplayBootstrap.ResolveChartPath` `:1056-1058`；相对路径按项目根解析）。这是换调试谱面的**唯一**环境变量入口 |
| `INFALSUS_CAPTURE_TIMES` | 逗号分隔的截图时间点（默认一套标准集） |
| `INFALSUS_CAPTURE_PREFIX` | 截图文件名前缀 |
| `INFALSUS_AUTOPLAY` | `1` = 自动播放到时间点再截 |
| `INFALSUS_CAPTURE_PRESERVE_CHART` | `1` = 保留初始化时加载的谱面（否则编辑器清空） |
| `INFALSUS_CAPTURE_TRACK_END_MASK` / `INFALSUS_CAPTURE_SKY_MASK` | 单独导出遮罩图 |
| `INFALSUS_CAPTURE_WIDTH/HEIGHT`、`INFALSUS_CAPTURE_SKIP_SCREENSHOTS` | 分辨率与跳过截图 |
| `INFALSUS_UI_CAPTURE_WIDTH/HEIGHT` | `Capture UI Styles` 的分辨率 |
| `INFALSUS_PERFORMANCE_PACKAGE`、`INFALSUS_FORCE_TRACKEND_REBUILD` | 性能与 TrackEnd 重建 |
| `INFALSUS_TRACK_END_SEED`、`INFALSUS_SEAM_ONLY` | TrackEnd 随机种子与接缝诊断 |
| `FALSUM_SONG_FONT_SOURCE` | 重建加密字体时的源 ttf（SHA-256 必须匹配） |

> README 的 `Deterministic Visual Debugging` 段是**正确**的：`INFALSUS_CHART_PATH`
> 由 `GameplayBootstrap.cs:1056-1058` 读取。（曾有审计只搜 `Editor/` 而误判它失效。）

调试谱面在 `Assets/InFalsus/Gameplay/Diagnostics/DebugCharts/`：
`debug_features`（侧轨/双向 FLICK/长短 HOLD/相连 SkyArea）、`debug_timing`（换轨/分数拍/BPM 变速/停走与倒走）等 5 个。

---

## 6. 门禁顺序与副作用

1. 编译 → 2. Editor Self Check → 3. Build。

前一步失败**不要继续**：构建会覆盖 `Builds/FalsumEditor-<Version>` 与同名 zip。

- 构建会改写 `FalsumPanelSettings.asset`、`Assets/Settings/PC_Renderer.asset`、
  音符 prefab 与 `PlayerSettings` ——跑完必须 `git status` 检查资产漂移。
- 自检报告每次覆盖写，失败也写。
