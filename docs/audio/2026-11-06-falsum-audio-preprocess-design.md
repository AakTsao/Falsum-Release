# FALSUM 音频预处理窗口（替代延迟）设计

日期：2026-11-06
状态：已获用户确认，进入实现

---

## 1. 目标

把制谱器里「延迟」（`# delay:` + 谱面时间的偏移）替换为一个**独立窗口**，名字叫
**音频预处理**。窗口把延迟**真正写进音频**，然后把谱面延迟清零，从此
`musicTime = chartTime`，作者只面对一条时间轴。

窗口里同时给出**基准 BPM / 拍数 / 音频延迟**三个参数，并配一张**带拍子提示的波形图**，
让作者一边看波形一边对齐第一拍。

## 2. 为什么必须这样做（数学）

现状：`musicTime = chartTime + delay`（`ChartTimeAxis`，`02-invariants.md` §A-4/A-6）。
旧谱面的 `delay = D` 意味着**音频里的第 D 毫秒就是谱面时间 0**，音频头部已经天生带着
一个 D 毫秒的引入段。

要把它烘焙进音频、同时保持**音符与音频内容的相对关系不变**（音符相对小节线也不变），
唯一自洽的映射是平移音频起读点：

```
新音频(t) = 旧音频(t + D)
```

等价地，新音频的“音频延迟”（前置空音量）为 `−D`：

| 旧谱 `delay` | 音频延迟（前置空音） | 视觉 |
|---|---|---|
| `+60` | `−60` | 红色块，裁掉音频开头 60 ms |
| `0` | `0` | 无块 |
| `−60` | `+60` | 绿色块，头部插入 60 ms 空音 |

**这条是用户明确的口径：旧 delay 的正值对应音频延迟的负值。**
不要把它“修正”成正延迟插空音——那会让音符相对音频内容整体漂移一个 `D`。

### 2.1 音符时间

`delay` 清零后，**音符与事件的时间一律不动**——它们本来就是谱面时间，而谱面时间 0
在新音频里仍然落在同一个音频内容位置。

唯一的例外是**多难度基线不一致**：包内四个难度各自的 `# delay:` 可以不同。烘焙音频
只能选一条基线（当前难度）。基线之外的难度 `k` 需要按

```
newTime(k) = oldTime(k) + (D_k − D_ref)
```

平移，才能与同一份音频对齐。`D_ref` 取当前难度。

### 2.2 派生事件

旧写法在文件头会写 `track(D,1.00)` + `bpm(D,bpm,division)`（`02-invariants.md` §A-5）。
`D = 0` 时 `ChartWriter` 不写这两行，所以**不需要手工删**——只要把 `DelayMs` 置 0
交给 `ChartWriter.Write` 就会自然消失。编辑器内存里若存在「谱面时间 0 且与基准
bpm/division 完全相同」的 bpm 事件，要一并清掉（否则写盘走 A-5 的 skip 但界面里多一条）。

## 3. 窗口

UITK 模态，挂在 `ModalLayer`，模板 `Falsum/UI/FalsumAudioPreprocessDialog`（新文件）。

### 3.1 布局

```
┌ 音频预处理 ─────────────────────────────────────────── × ┐
│ 本指引将帮助您预处理音频，避免谱面延迟。                  │
│                                                          │
│ 基准 BPM [ 129 ]   拍数 [ 4 ]   音频延迟 [ −60 ] ms      │
│                                                          │
│ 建议您在拍点前预留一个完整的小节                          │
│                                                          │
│ ┌ 波形区（自绘）────────────────────────────────────┐   │
│ │ [红块 −60] │▏▎▍▌▋▊▉▊▋▌▍▎▏│  拍线每拍一条，小节首拍高亮 │   │
│ └──────────────────────────────────────────────────┘   │
│ 缩放 5s 10s 20s 30s 60s                                  │
│                                                          │
│ [ 试听 ]                                     [ 确定 ]    │
└──────────────────────────────────────────────────────────┘
```

### 3.2 参数

| 参数 | 默认 | 范围 | 说明 |
|---|---|---|---|
| 基准 BPM | 当前谱 `chart.Bpm` | > 0 | 写进当前难度 `chart(bpm,division)` |
| 拍数 | 当前谱 `chart.Division` | ≥ 1 整数 | 每小节拍数；决定小节首拍 |
| 音频延迟 | `−当前难度旧 delay`（取整到 0.1 ms） | 有限值 | 正=前置空音，负=裁头 |

数字输入必须走 `FalsumEditorView.SanitizeNumericInput`（`02-invariants.md` §C-2）。

### 3.3 波形

- **自绘**：UITK `generateVisualContent` + `Painter2D`（先例：`FalsumPerformanceWindow`）。
  不引入新 shader / 新贴图资产。
- 内容：整包音频的峰值包络（每像素 min/max 竖线），横轴 = **新时间轴**。
- 延迟块：`延迟 > 0` 画绿色块 `[0, 延迟]`；`延迟 < 0` 画红色块 `[0, −延迟]`（被裁区间），
  并说明该区间已不在音频里。
- **拍线从新时间轴 0 开始铺满全图**（用户确认）。间隔 = `60000 / BPM`。
  `beatIndex % 拍数 == 0` 为小节首拍：画通高高亮线；其余拍画短淡线。
  只画可视区内的拍线，避免长音频铺几十万条。
- 缩放档位固定 `{5, 10, 20, 30, 60}` 秒/屏；可用拖动平移。

### 3.4 试听

- 独立播放源（不经过编辑器 `GameplayPitchPreservingPlayback`，避免污染时钟与 `pitch`）。
- **默认试听前 30 秒**，末端有一个可拖动的延长手柄，可延长到音频结尾（用户口径）。
- 播放映射：`t_src = max(0, t_new − 延迟)`；`t_new < 延迟` 区间只有拍点音、没有音频。
- **进度线**随播放沿波形移动。
- 拍点音：
  - **每小节第一拍 → 地轨 TAP 判定音**（`laneTapHitClip`）
  - **其余拍 → 侧轨 TAP 判定音**（`sideTapHitClip`）
  - 选轨逻辑复用 `GameplayAudioRouting.TapClipForLane`。
- 拍点音按 `dspTime` 预排（`AudioSource.PlayScheduled`），不逐帧轮询。
- 停止试听 / 关闭窗口 / 点确定 → 先停掉所有排程。

## 4. 应用（确定）

1. **校验**（不通过则禁用确定并在窗口内给出本地化提示）
   - 基准 BPM > 0、拍数 ≥ 1、音频延迟有限
   - 新音频时长 = `旧音频时长 − 延迟`，必须 > 0
   - 每个难度平移后不得出现负音乐时间（`ChartTimeAxis.ContainsNegativeMusicTime`）
2. **二次确认**（不可撤销：音频被改写、当前难度撤销栈清空）
3. **音频重写**（BASS，见 §5）
4. 数据落盘：
   - `package.SetAudio(newBytes, newDurationMs)`
   - 四个难度的谱面文本全部以 `delay = 0` 重写；基线难度直接用当前会话，
     其余难度按 §2.1 平移
   - 修正当前会话 `DelayMs = 0`、`Bpm`/`Division` = 窗口参数
   - 清理 §2.2 的冗余 bpm 事件
5. 重新加载音频 clip（否则时钟仍指旧音频）、`RebuildEditorTimeline`、刷新表现、
   清撤销栈、标脏、提示
6. 波形包络只在内存，不落盘（延续 `02-invariants.md` §A-12）

## 5. 音频后端：BASS（不用 ffmpeg）

用户明确要求不要把 61 MB 的 `ffmpeg.exe` 绑进这条路径（正在做的 BASS 替代方案）。
已核实本仓库可用：

- `Assets/Plugins/x86_64/`：`bass.dll`（165 KB）、`bassenc.dll`（38 KB）、
  `bassenc_ogg.dll`（184 KB）、`bassmix.dll`、`bassflac.dll`、`bass_fx.dll`
- `Assets/Plugins/ManagedBass/`：`ManagedBass.dll`（含 `Bass.Init` / `CreateStream` /
  `StreamPutData` / `ChannelSetPosition`）、`ManagedBass.Mix` 等

`FalsumBassEncoder.cs` 已经把「解码通道必须**泵数据**才写出真实音频，否则只剩 3.8 KB
Ogg 头」这个坑记录得很清楚，实现要沿用同一个泵循环。

### 5.1 重写流水线

```
Bass.CreateStream(rate, ch, Decode|Float, StreamProcedureType.Push /* 或回调流 */)
  → 先吐 silenceSamples 帧静音（延迟 > 0 时）
  → 再吐解码出的源 PCM
  → StreamPutData 推给该通道
  → FalsumBassEncoder.TryEncodeToOgg(channel, outPath, quality 6)
  → 回读字节 → 校验 Ogg 时长（FalsumMediaConverter.ProbeOggDurationMs）
```

- `延迟 < 0` 时跳过开头 `−延迟` 毫秒的源音频（解码后丢弃，不重新编码整段）。
- 采样率 / 声道沿用包规范：48000 Hz / 2 声道、`libvorbis` 等价质量 6。
- **不新增临时可执行文件、不解析 stdout**。
- BASSenc 不可用时（非 Windows 或缺失 add-on）→ 明确失败提示，不静默降级到 ffmpeg。

### 5.2 波形包络

同样用 BASS 解码取峰值（每桶 min/max 压到目标像素宽度），**只在内存**。
不要复用 `FalsumTrackSpectrumCache`（那是轨道地面波形，16 s 切片 / 960 px 每速、
走 ffmpeg stdout，语义与资产都不同）。

## 6. 接线点

| 位置 | 改动 |
|---|---|
| `FalsumEditor.uxml` | 「延迟」标签 + `DelayValue` 换成 `AudioPreprocess` 按钮 |
| `FalsumEditorView.cs` | 新增 `AudioPreprocessRequested` 事件；`SetDelay` 保留（数据层仍会推）；门面 `OpenAudioPreprocess/CloseAudioPreprocess` |
| `FalsumChartEditor.cs` | 新 partial `FalsumChartEditor.AudioPreprocess.cs`：打开/试听/应用/刷新 |
| `GameplayHitAudio.cs` / `GameplayBootstrap.cs` | 新增按轨选音的试听音入口（不改既有 `PlayTapPreviewCue` 语义） |
| 本地化 | `zh-CN.json` / `en-US.json` 成对新增 `falsum.audioPreprocess.*` |

## 7. 不变量（本功能必须守住）

- **不新增硬编码可见文本**（§C-1），两语言同步，`Tools/falsum_localization_audit.py` 不许倒退。
- **数字输入走 `SanitizeNumericInput`**（§C-2）。
- **时间换算只经 `ChartTimeAxis`**（§A-16）；`delay = 0` 时不得写出派生 `track`/`bpm` 行（§A-5）。
- **`CommitChange` / `NotifyChanged` 里 `Normalize` 一个都不能漏**（§A-2）；应用后重建天空分组。
- **不改 `Recovered/` 资产路径**（§B-11）。
- **不动别人的 SkyArea Bank 工作区改动**（§D-8）。

## 8. 验收

基线当前是**红的**（工作区里未完成的 SkyArea Bank：`FalsumEditorView` 缺
`SetBankPresets/SetBankSelectionSummary/SetBankPanelOpen`，`FalsumFileDialog` 缺
`*SkyAreaBank`，缺 `FalsumTextClipboard`，11 个 `falsum.bank.*` key 未补）。
因此本轮口径是**相对基线不新增**：

1. `Diagnostics/UnityCompile.log` 的 `error CS` 集合与基线差集为空
2. `FalsumSelfCheck` 新增 `CheckAudioPreprocess`，覆盖：
   - 延迟 ↔ 前置空音/裁头的映射与边界
   - 拍网格切分与小节首拍分类
   - 多难度 `D_k − D_ref` 平移
   - `delay = 0` 写谱不产生派生行、不产生重复 bpm
   - 平移后无负音乐时间
   - 新 UXML 元素可查、本地化 key 齐备
3. `python Tools/falsum_localization_audit.py` 不新增 missing key
4. 窗口可开、可试听（拍点音轨正确）、确定后音频时长与谱面时间轴一致

## 9. 明确不做（YAGNI）

- 不做「音频延迟」的撤销栈（音频改写不可逆，二次确认即可）
- 不做波形精确到采样点的放大（只到 5 s/屏）
- 不做多首音频批处理
- 不保留顶栏延迟编辑入口
- 不改 `ChartTimeAxis`、不改 `ChartWriter` 的既有语义
