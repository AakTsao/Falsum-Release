# FALSUM 音频预处理窗口 — 实施计划

日期：2026-11-06
对应 spec：`docs/superpowers/specs/2026-11-06-falsum-audio-preprocess-design.md`

## 0. 工作区现状（必须先知道）

- **基线编译是红的**，原因不在本任务：工作区里另一个会话正在实现 SkyArea Bank
  （`FalsumChartEditor.Bank.cs`、`FalsumSkyAreaBankModel/Store/View.cs`），
  引用了尚不存在的 `FalsumEditorView.SetBankPresets/SetBankSelectionSummary/SetBankPanelOpen`、
  `FalsumFileDialog.*SkyAreaBank`、`FalsumTextClipboard`，且缺 11 个 `falsum.bank.*` key。
- **另一个会话正在并行编辑**：`FalsumEditorView.cs`、`FalsumChartEditor.cs`、
  `FalsumEditorWorld.cs`、`FalsumChartEditor.AnchorEdit.cs`、`FalsumSelfCheck.cs`、
  `localization/*.json`、`FalsumEditor.uxml` 都是它的热点。
- 因此：**新功能一律落独立新文件**；对热点文件只做最小插入，改前必须重读；
  `FS_STALE_VERSION` 出现就重读后重放。
- 验收口径：**相对基线不新增编译错误**，不是“全绿”。

## 1. 新增文件清单（互不重叠的写域）

| 文件 | 归属 | 职责 |
|---|---|---|
| `Gameplay/Falsum/Core/FalsumAudioPreprocessMath.cs` | Lead | 纯函数：延迟↔前置空音/裁头映射、拍网格、跨难度 `D_k−D_ref` 平移、冗余 bpm 清理、新时长/校验 |
| `Gameplay/Falsum/Runtime/FalsumAudioPreprocessEncoder.cs` | A | BASS 解码 → 前置静音/裁头 → `BASSenc_OGG` 编码 |
| `Gameplay/Falsum/Runtime/FalsumAudioPreprocessEnvelope.cs` | A | BASS 解码 → 峰值包络（内存，多分辨率桶） |
| `Gameplay/Falsum/Runtime/FalsumAudioPreprocessAudition.cs` | A | 试听：dsp 排程拍点音 + 音频起播/停止/进度 |
| `Gameplay/Falsum/UI/FalsumAudioPreprocessView.cs` | B | 模态窗口逻辑：参数、缩放/平移、试听控制、确定/取消 |
| `Gameplay/Falsum/UI/FalsumAudioPreprocessWaveform.cs` | B | `generateVisualContent` + `Painter2D` 自绘波形/延迟块/拍线/进度线 |
| `Resources/Falsum/UI/FalsumAudioPreprocessDialog.uxml` | B | 窗口模板 |
| `Resources/Falsum/UI/FalsumAudioPreprocess.uss` | B | 窗口样式（不碰 `FalsumEditor.uss`） |
| `Gameplay/Falsum/Runtime/FalsumChartEditor.AudioPreprocess.cs` | Lead | 编辑器 partial：打开/试听/应用/刷新 |

## 2. 最小插入（热点文件，Lead 独做，最后执行）

| 文件 | 插入内容 |
|---|---|
| `Resources/Falsum/UI/FalsumEditor.uxml` | `DelayValue` 那组控件换成 `AudioPreprocess` 按钮 |
| `UI/FalsumEditorView.cs` | 模板路径常量、`AudioPreprocessRequested` 事件、按钮绑定与门面方法 |
| `Runtime/FalsumChartEditor.cs` | 一处事件订阅 + 调用 partial |
| `Presentation/Audio/GameplayHitAudio.cs` | 按轨选音的试听音入口（新增方法，不改既有语义） |
| `Runtime/Bootstrap/GameplayBootstrap.cs` | 一个转发方法 |
| `Editor/FalsumSelfCheck.cs` | 追加 `CheckAudioPreprocess` 调用与实现 |
| `Resources/Falsum/Localization/{zh-CN,en-US}.json` | `falsum.audioPreprocess.*` 成对新增 |

## 3. 核心数学（Lead 先落，A/B 依赖）

```
新音频(t) = 旧音频(t + D_ref)            // D_ref = 当前难度旧 delay
音频延迟 S = −D_ref                       // 正=前置空音，负=裁头
新时长 = 旧时长 − S                       // = 旧时长 + D_ref
难度 k 时间平移 = (D_k − D_ref)           // note / event / 全部
应用后 DelayMs = 0
```

校验：
- `BPM > 0`、`拍数 ≥ 1`
- `S` 有限，`新时长 > 0`
- 每个难度平移后 `ContainsNegativeMusicTime(chart, 0)` 必须为 false
- 清理：`TimeMs == 0` 且与基准 bpm/division 完全相同的非 base `Bpm` 事件

## 4. 试听约定

- `t_src = max(0, t_new − S)`（`S > 0` 时开头一段是空的）
- 拍点：`t_new = k * 60000 / BPM`，`k % 拍数 == 0` → 地轨音（`laneTapHitClip`），否则侧轨音（`sideTapHitClip`）
- 默认试听前 30 s，可拖手柄延长到结尾
- 排程用 `dspTime`，窗口 2 s 预排；停止/关闭/确定必须清空排程

## 5. 顺序

1. Lead：`FalsumAudioPreprocessMath.cs` + 单测式断言（先可编译、可自检）
2. A 与 B 并行（各自独立新文件）
3. Lead：最小插入接线 + 本地化 + 自检
4. 门禁：编译差集 = ∅、`FalsumSelfCheck` 的 `CheckAudioPreprocess` 通过、
   本地化审计不新增 missing

## 6. 禁止

- 不碰 Bank 相关文件、不修基线红
- 不改 `ChartTimeAxis` / `ChartWriter` 既有语义
- 不引入新 shader / 新贴图 / 新字体
- 不做 ffmpeg 临时进程（BASS 路径为主）
- 不硬编码可见文本（两语言同步）
