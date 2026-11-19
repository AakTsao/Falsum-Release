# Flick · 实现方式与贴图 / 代码 / 依赖归档包

In Falsus / FALSUM 编辑器里 **Flick 音符**（天空判定线上的斜纹箭头音符）的完整实现、
贴图、Shader、代码以及全部依赖的归档包。

- **源工程**：`InFalsus-Gameplay`
- **引擎**：Unity **6000.3.26f1**（`de8167e7440c`）
- **渲染管线**：URP **17.3.0** + ShaderGraph 17.3.0
- **分支**：`perf/render-hotpaths`
- **导出时间**：2026-11-15
- **导出内容**：**工作区当前状态**（含未提交的 Flick 修复 —— 这一点至关重要，见下方「必须知道的坑」）
- **包内文件**：**744** 个，约 **18.4 MB**；逐文件 SHA256 见 `MANIFEST_SHA256.txt`

---

## ⚠️ 必须知道的坑（先读这段）

**这套 Flick 修复目前全部停留在工作区，没有提交到 git。** 用 `git show HEAD:` 比对会看到：

| 项 | HEAD 版本 | 工作区（本包内容） |
|---|---|---|
| `dc.shader` | **261 行**，无 `_BarRepeat` / 无 `_TailLift` / 无 HDR glow | **391 行**，完整 |
| `FlickNote-Bar.png.meta` / `FlickNoteR-Bar.png.meta` | `wrapU/V = 1/1`（**Clamp**）→ **斜纹全部消失** | `0/0`（**Repeat**） |
| `DefaultVolumeProfile.asset` Bloom | `intensity = 0`（**完全关闭**）→ **没有发光** | `intensity = 2 / threshold 1.5 / scatter 0.821` |
| `Mat.001` / `Mat.001 R` | 3012 | 3012（Round5 承诺的 3014 **never landed**） |

⇒ **若要复现这个包里的效果，不能只 `git checkout` 干净代码再把包拷进去**，
必须同时保留这 4 类工作区改动。详见 `00_docs/Flick_依赖清单.md` §8.1。

---

## 一分钟看懂

Flick **不是一张图**，是**两层网格 + 两个不同 shader** 叠出来的：

```text
v5-3.prefab (root, 绕 Y 180°)
├── Flick   ← 箭头层  Mesh=Flick.asset (0.232×0.160)  材质=Mat.001      → URP Unlit（1 张 2048² 图集）
└── Plane   ← 尾巴层  Mesh=Plane.asset (1.536×0.146)  材质=FlickV5 Left → dc.shader（_Base/_Bar/_Core 三贴图合成）
```

**斜纹的「斜」不是 UV 旋转**，而是因为 `Plane` 网格本身是**剪切平行四边形**（高 0.146，上边相对下边 X 位移 −0.146 ⇒ dx/dy = 1.000 ⇒ **精确 45°**），
再把一张 64×128 的**单条纹贴图**沿 U 重复若干次 —— 45° 斜带就自然出现了。

```text
片元合成（dc.shader:244-246）：
  rgb = (bar.rgb*bar.a + base.rgb*(1-bar.a)) * (1-core.a) + core.rgb*core.a
  a   = max(base.a, core.a) * _Opacity
  然后：× _Color → 尾巴去染色(_TailDetint) → 尾巴提亮(_TailLift) → 条纹强化(_BarStrength)
      → 压暗(_OverallDim) → 降饱和(_SaturationTrim) → 预乘 alpha → 预乘之后再加 HDR glow(_GlowPower)
```

---

## 目录结构

```text
Flick_Implementation_Package/
├─ README.md                            ← 本文件
├─ MANIFEST_SHA256.txt                  ← 全包逐文件校验和（744 条）
│
├─ 00_docs/                             ★ 文档（从这里开始读）
│  ├─ Flick_实现方式说明.md              ★★ 原理篇（964 行）：数据链 / 渲染链 / 斜纹机理 / 参数表 / 时序
│  ├─ Flick_依赖清单.md                  ★★ 依赖篇（679 行）：三层依赖 / GUID 表 / 导入设置 / 队列 / 缺口
│  ├─ Flick_接入指南.md                  ★★ 移植篇（774 行）：最小落地清单 / GUID 冲突 / 排错对照 / 验证
│  ├─ Flick_贴图清单.md                  ★  贴图篇：13 张图的尺寸 / wrap / 槽位 / 贴图→材质→shader 全链路
│  ├─ Flick_资产搬运清单.md                  逐文件搬运记录（1070 行，原路径→包内路径→字节→sha256→guid）
│  └─ design/                             四篇原始修复报告（历史记录，非当前状态快照）
│     ├─ Flick_Stripe_Fix.md               斜纹修复（_Bar guid 错 / U 没重复 / wrap=Clamp）
│     ├─ Flick_Tail_Fix.md                 尾巴接箭头 / 提亮
│     ├─ Flick_Glow_Width_Height_Fix.md    发光 + 尾巴高度 + 宽度
│     └─ Flick_Round5_Fix.md               端帽覆盖 / 尾巴亮度 / 斜纹密度 / 高度独立
│
├─ 01_shader/                           ★ 核心 shader（4 个文件，保留 .meta 即保留 GUID）
│  ├─ dc.shader                            "InFalsus/Replica/FlickV5" —— 尾巴专用，391 行
│  ├─ dc.shader.meta                       guid 55058b1d3da57a04db6ad7341b35e048
│  ├─ Infalsus_Common.hlsl                  IF_Premultiply 等公共函数（IF_PREMULTIPLY=1）
│  └─ Infalsus_Common.hlsl.meta
│
├─ 02_runtime/                          ★ 运行时代码（13 文件，连 .meta）
│  └─ Gameplay/Presentation/
│     ├─ Notes/NoteRenderer.cs             2902 行，flick 几何/对齐/每帧写参全在这里
│     ├─ Notes/NoteRendererPropertyState.cs  ★ 方向→颜色 / _Color / _BarRepeat / _Opacity 的唯一写入点
│     ├─ Effects/FlickHitEffectRenderer.cs  738 行，FlickBurst 池 + 判定反馈字
│     └─ Audio/GameplayHitAudio.cs         996 行，flick 判定音
│
├─ 03_data/                             ★ 数据链（14 文件）
│  ├─ Gameplay/Core/Chart/ChartModels.cs     FlickCenterPos/FlickWidth/FlickHeadPos + 强校验
│  ├─ Gameplay/Core/Chart/ChartParser.cs     flick(t,center,grid,width,dir) 解析
│  ├─ Gameplay/Core/Judgement/JudgementEngine.cs  PressFlick(direction, nowMs)
│  ├─ Gameplay/Falsum/Core/ChartWriter.cs    写谱：flick 行生成（AwayFromZero 量化）
│  └─ debug_charts/                          两张现成的调试谱（长度扫描 / 命中特效）
│
├─ 04_assets/                           ★ 资产（335 文件，保留原始子目录形状）
│  ├─ Material/ (16)                       8 个材质 + .meta
│  ├─ Mesh/ (8)                            Flick / Plane / flicknote01 / flickunderscore01
│  ├─ GameObject/ (4)                      v5-3.prefab / v5-3R.prefab
│  ├─ Texture2D/ (26)                      13 张 png + .meta
│  ├─ Sprite/ (256)                        FlickBurst_0..63 + FlickBurstR_0..63（128 asset + 128 meta）
│  ├─ AnimationClip/ (4)                   FlickBurstAnim.anim / FlickBurstAnimR.anim
│  ├─ AnimatorController/ (2)              FlickBurst_0.controller
│  ├─ AnimatorOverrideController/ (2)      FlickBurst_0 R.overrideController
│  ├─ Shader/ (4)                          URP Unlit 复刻 + 9a.shader（额外依赖）
│  └─ Audio/ (3)                           60_note_flick_5_draft2.wav
│
├─ 05_config/                           ★ 引擎配置（39 文件）
│  ├─ Assets/Settings/DefaultVolumeProfile.asset   ★ Bloom 在这儿（intensity 2 / threshold 1.5 / scatter 0.821）
│  ├─ Assets/Settings/PC_RPAsset.asset / PC_Renderer.asset
│  ├─ Assets/Settings/UniversalRenderPipelineGlobalSettings.asset
│  ├─ Packages/manifest.json                     URP 17.3.0 / ShaderGraph 17.3.0
│  ├─ ProjectSettings/ProjectVersion.txt         6000.3.26f1
│  └─ ProjectSettings/GraphicsSettings.asset     预加载 shader 列表
│
├─ 06_integration/                      ★ 28 段调用点片段（带真实行号，逐字摘录不含全文）
│  ├─ 01..11  GameplayBootstrap / GameplaySceneBinding 的 flick 接线
│  ├─ 12..18  NoteRenderer / NoteRendererPropertyState 的 flick 分支
│  ├─ 19..23  JudgementEngine / ChartParser / ChartModels / ChartWriter
│  └─ 24..28  FlickHitEffectRenderer 常量与池 + 自检断言 + 截图入口
│
└─ 07_full_dependents/                  依赖方完整源码（305 文件：139 cs + 165 meta + 场景）
   └─ Assets/InFalsus/...                片段不够时查这里；含 GameplayRuntime.unity
```

★ = 必读

---

## 快速使用

| 你想做的事 | 读这个 |
|---|---|
| **搞懂 Flick 为什么长这样** | `00_docs/Flick_实现方式说明.md`（配 `06_integration/13/15/16` 三段） |
| **知道它到底依赖什么 / 缺什么** | `00_docs/Flick_依赖清单.md` |
| **搬到别的 URP 工程** | `00_docs/Flick_接入指南.md`（最小落地清单在 §2） |
| **只想换贴图 / 调效果** | `00_docs/Flick_贴图清单.md` + 本文末「调参速查」 |
| **Flick 显示不对，排错** | `00_docs/Flick_接入指南.md` §6 排错对照表 |
| **查某个文件是从哪拷的** | `00_docs/Flick_资产搬运清单.md` |

---

## 调参速查（当前工程实际值，**不是**旧文档里的值）

| 想要的效果 | 调什么 | 当前值 | 在哪 |
|---|---|---|---|
| 斜纹更密 / 更疏 | `FlickStripesPerWorldUnit` | **11.0**（每条世界单位的条纹数，与宽度解耦） | `NoteRenderer.cs:228` |
| 斜纹对比更强 | `_BarStrength` | 0.55 | `dc.shader:91` |
| 尾巴更亮（去染色） | `_TailDetint` | 0.40 | `dc.shader:89` |
| 尾巴更亮（乘性） | `_TailLift` | 1.45 | `dc.shader:84` |
| 整体压暗程度 | `_OverallDim` | 0.86 | `dc.shader:98` |
| 发光更强 | `_GlowPower` | 1.8（需 Bloom 开启才有效） | `dc.shader:100` |
| 发光更强（管线侧） | Bloom `intensity` | 2.0 | `DefaultVolumeProfile.asset:368` |
| 尾巴更矮 | `FlickTailHeightScale` | **0.93** | `NoteRenderer.cs:231` |
| 尾巴接缝 | `FlickTailInset` | 0.0（0 = 严丝合缝） | `NoteRenderer.cs:222` |
| 尾巴更窄/更宽 | `FlickWorldWidth` 里的 `1.62` | 1.62 | `NoteRenderer.cs:2679` |
| 命中特效时长 | `BurstDurationFallback` | 0.5333 s | `FlickHitEffectRenderer.cs:13` |

> ⚠️ **别去材质面板里调 `Bar Repeat`。** 材质里 `_BarRepeat` 虽然写着 10.0，
> 但运行时每帧被 `MaterialPropertyBlock` 用「尾巴世界长度 × `FlickStripesPerWorldUnit`」覆盖掉
> （`NoteRenderer.cs:2632-2633` + `NoteRendererPropertyState.cs:96-97`）。实测宽度 0.5 时实际值约 **6.35**。

---

## 六条铁律

1. **`flick` 方向只有 `4` 和 `16`**（`4` = 绿/右，`16` = 黄/左）。解析层强校验（`ChartParser.cs:359-364`）、
   模型层强校验（`ChartModels.cs:168-174`）。**写别的值，整个谱面包直接拒载。**

2. **两张 Bar 贴图必须 `wrapU=0 / wrapV=0`（Repeat）。** 改成 Clamp，U 重复超过 1 的部分全被钳到边缘
   → 10 条条纹塌成 1 条 → **画面变纯色**。这是唯一一条「改一个导入勾选就让整个斜纹消失」的设置。

3. **`Blend One OneMinusSrcAlpha` 必须与 `IF_Premultiply` 成对。**
   片元返回的是已预乘颜色，若改回 `Blend SrcAlpha OneMinusSrcAlpha`，alpha 会被乘两次
   → 暗一档，且 HDR 值顶不到 Bloom 阈值 → **发光消失**。

4. **HDR 提亮必须写在预乘之后**（`dc.shader:327-328`）。
   写在前面会被 `rgb * a` 拉回阈值以下，Bloom 永远不触发。

5. **箭头层不走 `dc.shader`。** 箭头用 `URP Unlit_1.shader` 采样 `_BaseColor`，而 C# 对 Flick 只写 `_Color`
   （`NoteRendererPropertyState.cs:77` 的掩码里没有 `BaseColor`）。**方向色是烘死在贴图里的**
   （实测：`flickv5.png` 均值 (135,131,72) 偏黄；`FlickNoteCapR.png` 均值 (72,130,110) 偏青绿）。
   ⇒ 想运行时改箭头颜色，只改 `_Color` **没用**；且 `Mat.001` 里没有 `_Opacity`，
   所以 **fade 只作用于尾巴，箭头不会淡出**。

6. **左右两侧的斜纹强度天生不同。** 两张 `_Bar` 的 alpha 范围不一样
   （左 `FlickNote-Bar` = 0.443~0.890；右 `FlickNoteR-Bar` = **0.184~0.698**），
   而 `dc.shader:239/:272` 的归一化常量（0.66 / 0.44 / 0.45）只按**左版**硬编码
   ⇒ **右/绿 flick 的斜纹比左侧弱约 29%**，且调 `_BarContrast` 无法单独修一侧。

---

## 完整性校验

```powershell
# 在解压后的目录里执行：逐文件比对 MANIFEST_SHA256.txt
$pkg = $PWD
$bad = 0
Get-Content .\MANIFEST_SHA256.txt |
  Where-Object { $_ -and $_ -notmatch '^#' } |
  ForEach-Object {
    $h, $size, $rel = $_ -split '  ', 3
    $f = Join-Path $pkg $rel
    if (-not (Test-Path -LiteralPath $f)) { Write-Host "MISSING $rel"; $bad++; return }
    $actual = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $h) { Write-Host "HASH MISMATCH $rel"; $bad++ }
  }
if ($bad -eq 0) { "OK: 全部文件校验通过" } else { "FAILED: $bad 个文件不符合" }
```

关键资产指纹（可直接核对）：

| 文件 | SHA256（前 16） |
|---|---|
| `01_shader/dc.shader` | `53f717cd466771a1` |
| `04_assets/Mesh/Flick.asset` | `d81fd351f18899a8` |
| `04_assets/Sprite/FlickBurst_63.asset` | `19ea321d175b870a` |
| `04_assets/Audio/Resources/60_note_flick_5_draft2.wav` | `a2e8c525c7a03b8b` |

---

## 关键 GUID

| 资产 | GUID |
|---|---|
| `dc.shader`（FlickV5） | `55058b1d3da57a04db6ad7341b35e048` |
| `FlickV5 Left.mat` | `5ce7c37f26e917d499575f6e71d17b34` |
| `FlickV5 Right.mat` | `96bf87a014d661d4f8e5c1b55e79fe18` |
| `Mat.001.mat`（箭头左） | `5b65e1ef0a12ed24380b367ec857314d` |
| `Mat.001 R.mat`（箭头右） | `65cb8f12c4a4fd64dbacf621e55977e4` |
| `FlickBurstMaterial.mat` | `b1f84eec5fee4104a813ec4b386eb015` |
| `matid_flicknote.mat` | `2cbee7aa38863194ba98555da64f340f` |
| `Flick.asset`（箭头 mesh） | `2ba5deb3d2fba2e4f9188d6121b2958a` |
| `Plane.asset`（尾巴 mesh） | `cb51b6c7bce3f9b478194025ef511b05` |
| `v5-3.prefab`（左） | `c19bfe2a8cc8c2b4781ed2d7fd72fcbe` |
| `v5-3R.prefab`（右） | `23de1814ce5aa2a45ac1f402234a489f` |
| `URP Unlit_1.shader`（箭头用） | `f75247fd87768534bb91926c2f61d0e4` |

> 完整 GUID 对照表（含每张贴图、谁引用谁）见 `00_docs/Flick_依赖清单.md` §3。

---

## 文档与代码冲突时

四篇 `design/` 文档是**修复过程的流水账，不是当前状态快照**。
本轮打包时逐项实测，发现 **4 处文档承诺与代码实际不符**，一律**以代码为准**：

| 项 | 文档说 | 代码实际 |
|---|---|---|
| 箭头 renderQueue | 3014（Round5 §1） | **3012**（`autobackups/flick_fix/fix_r5.py:21` 里写好了 3014 补丁，但**从未执行**；全工程 grep 零命中） |
| `FlickTailHeightScale` | 1.0（Round5 §4） | **0.93**（尾巴实际高度 = 84.86%，不是文档说的 91.25%） |
| `_BarRepeat` | 5.5（Stripe）/ 8.0（Round5） | shader 里 **10.0**，且运行时被 **尾巴长度 × 11.0** 覆盖 |
| `Cull` | Back（dc.shader 头注释） | **Off**（dc.shader:141） |
| `NoteRenderer` 构造 | 13 参（工程内 4 处文档） | **14 参**（`NoteRenderer.cs:462`；flick 两支在第 7、8 位） |
| 箭头染色 | 由 C# 写 `_Color` 控制 | **箭头完全不读 `_Color`**（URP Unlit 只消费 `_BaseColor`；`_Color` 是 `[HideInInspector]` 废弃字段）。方向色烘死在贴图里 |

完整对照见 `00_docs/Flick_实现方式说明.md` 附录 A 与 `00_docs/Flick_接入指南.md` §0。

---

## 包内未包含（有意为之）

- **`Library/`、`Builds/`、`Logs/`、`Temp/`、`autobackups/`** —— 生成物与备份，无归档价值。
- **`GameplayRuntime.unity` 依赖的轨道资产**（`LANE_*` / `JUDGE_*` / `NoteFX_*` / `cursor2.mat` / `alamode.ogg` 等 98 处）
  —— 本包是 **Flick 纵向切片**，场景仅作 `FLICK_NOTE` 挂载点与层级参考，**不承诺独立可跑**。
  清单见 `Flick_依赖清单.md` §7b。
- **Flick 渲染链路本身 guid 引用完全闭合**，不依赖包外任何工程内资产（`00_docs/Flick_依赖清单.md` §3 已逐条验证）。
