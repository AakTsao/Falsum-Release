# Flick 实现方式说明（原理篇）

> 本文是 Flick 音符的**实现原理**文档：为什么长这样、每个数字从哪来、改哪个参数会怎样。
> 所有数值都从工程内的代码 / 资产实测得到，**行号引用的是本工程 HEAD 工作区的真实行号**。
>
> 适用工程：`InFalsus-Gameplay`（Unity 6000.3.26f1 / URP 17.3）
> 关键文件：
> - 资产：`Assets/InFalsus/Recovered/GameObject/v5-3.prefab`、`v5-3R.prefab`
> - 网格：`Assets/InFalsus/Recovered/Mesh/Flick.asset`（箭头）、`Plane.asset`（尾巴）
> - 材质：`Assets/InFalsus/Recovered/Material/Mat.001.mat`、`Mat.001 R.mat`、`FlickV5 Left.mat`、`FlickV5 Right.mat`
> - Shader：`Assets/InFalsus/Recovered/dc.shader`（**只贴尾巴**）、`Assets/InFalsus/Recovered/Shader/Universal Render Pipeline_Unlit_1.shader`（**贴箭头**）
> - 运行时：`Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs`、`NoteRendererPropertyState.cs`
> - 特效：`Assets/InFalsus/Gameplay/Presentation/Effects/FlickHitEffectRenderer.cs`
> - 数据：`Assets/InFalsus/Gameplay/Core/Chart/ChartParser.cs`、`ChartModels.cs`、`Falsum/Core/ChartWriter.cs`
> - 时序：`Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs`

---

## 一分钟看懂

**Flick 是「箭头网格 + 尾巴网格」两层**，两个独立 GameObject 挂在同一个 prefab 下：

```text
v5-3.prefab  (root，localPos(0,0,0)，quat(0,1,0,0) = 绕 Y 180°)
├── Flick   ← 箭头层：Mesh = Flick.asset（24 顶点，0.232 × 0.160），材质 Mat.001.mat
│             走 URP Unlit_1.shader，用一张 2048×2048 图集 sampling
└── Plane   ← 尾巴层：Mesh = Plane.asset（4 顶点，1.536 × 0.146），材质 FlickV5 Left.mat
              走 dc.shader，用 _Base / _Bar / _Core 三张贴图合成
```

- **箭头**只负责那个"钩状厚端/端帽"，颜色来自图集本身 + `_BaseColor`，**不走 `dc.shader`**。
- **尾巴**是那条会拖长的斜纹带，**只有它走 `dc.shader`**（三贴图合成 + 斜纹 + 尾巴提亮 + HDR 发光）。
- **颜色不写在 shader 属性里**：原版母材没有任何 Color 属性，颜色由 C# 运行时按方向写进 `MaterialPropertyBlock`
  （`4 = 绿/右`，`16 = 黄/左`，见 `NoteRendererPropertyState.cs:72-74`）。
- **斜纹不是 UV 旋转，也不是屏幕空间效果**：`_Bar` 是 64×128 的**单条纹单元**，
  U 方向重复 `_BarRepeat` 次 → 得到一组竖直暗带；再乘上 `Plane` 网格本身是**剪切平行四边形**（dx/dy = 1.000），
  竖带就被切成了 **45° 斜带**。

> ⚠️ **两层不要混为一谈**。这是本工程最容易被写错的一点：
> 把 `dc.shader` 的三贴图合成讲成"整个 flick"就错了，箭头层根本看不到 `_Bar`/`_Core`。

---

## 一、数据链路：从 chart 文本到 GPU 参数

### 1.1 谱面文本格式

```text
flick(时间ms, 中心位置, 格子数, 宽度格子数, 方向)
```

工程内真实样例（`Assets/InFalsus/Gameplay/Diagnostics/DebugCharts/`）：

```text
debug_features.chart.txt:10      flick(3000,18,24,12,4)     # 绿/右
debug_features.chart.txt:11      flick(3600,6,24,12,16)     # 黄/左
debug_flick_hit.chart.txt:5      flick(1000,9,12,6,4)
debug_flick_lengths.chart.txt:6  flick(1000,10,48,8,16)
```

### 1.2 解析：`ChartParser.cs:332-377`

| 步骤 | 代码 | 结果 |
|---|---|---|
| 固定 5 个参数 | `ChartParser.cs:334-340` `RequireExact(args, 5, ...)` | 多/少一个都抛 `FormatException` |
| 读 grid | `ChartParser.cs:341` `var grid = Number(args[2]);` | 双精度 |
| **中心归一化** | `ChartParser.cs:342` `center = Number(args[1]) / grid` | `StartPos ∈ [0,1]` |
| 读宽度格子 | `ChartParser.cs:343` `widthCells = Number(args[3]);` | 格子数 |
| grid 必须 > 0 | `ChartParser.cs:344-349` | 否则报 `flick grid must be positive.` |
| grid 必须是正整数 | `ChartParser.cs:350` `CellCount(args[2])` → `:763-772` | 非整数/≤0 抛异常 |
| 宽度不得为负 | `ChartParser.cs:351-356` | 否则报 `flick width must not be negative.` |
| **宽度归一化** | `ChartParser.cs:357` `var width = widthCells / grid;` | `StartWidth ∈ [0,1]` |
| **方向白名单** | `ChartParser.cs:358-364` | **只允许 4 和 16**，写别的值直接 `flick direction must be 4 or 16.` |
| 落成 `ChartNote` | `ChartParser.cs:365-375` | 见下 |

`ChartNote` 关键字段（`ChartParser.cs:368-374`）：

```csharp
Kind           = NoteKind.Flick,
TimeMs         = ChartTime(args[0], delay),
StartPos       = center,          // 归一化中心 ∈ [0,1]
StartWidth     = width,           // 归一化宽度 ∈ [0,1]
StartCellCount = gridCells,       // 原始格子数（仅用于回写谱面时还原精度）
EndCellCount   = gridCells,
FlickDirection = direction,       // 4 或 16
```

注意 `StartCellCount` **不是**数组下标，只用于 `ChartWriter` 回写时把归一化值还原成整数格子
（`ChartWriter.cs:210-228` `FalsumFraction.NormalizePair` + 四舍五入）。

### 1.2.1 加载期校验的边界（容易误以为"全都会挡"）

`ChartParser` 对 flick **只做这 5 项校验**：
`参数个数 == 5`（`:334-340`）、`grid > 0`（`:344-349`）、`grid` 是正整数（`:350`）、
`width >= 0`（`:351-356`）、`direction ∈ {4,16}`（`:359-364`）。

**它不校验 `center ± width/2` 是否落在 0..1 内**——那一条在 `ChartNoteValidator.RequireFlickSpan`
（`ChartModels.cs:263-278`，容差 `1/cellCount + Epsilon`），而该 validator **不在解析路径上**：

| 调用点 | 是否挡越界 |
|---|---|
| `ChartParser`（加载谱面） | **否**（只查上面 5 项） |
| `ChartWriter.WriteNote`（保存） | 调了 validator，但 `allowOutOfBounds: true`（`ChartWriter.cs:185-188`）→ **放行** |
| `FalsumToolRuntime.AddNoteJson` / `UpdateNoteJson`（`tool.json` 工具入参） | **是**，`ChartNoteValidator.Validate(note, "Note")`（`:1152` / `:1189`） |
| `FalsumSelfCheck`（自检，示例里用的是越界 lane） | **是**（`FalsumSelfCheck.cs:1352`） |

**实践含义**：手写 `.chart.txt` 里写一个越界 flick 是**能加载成功**的，
它会被 `TrackX` 线性外插到 `-0.81..0.81` 之外，表现为音符跑出轨道两侧。
排查"flick 跑到屏幕外"时不要指望加载期报错——用 `FlickWidth`/`StartPos` 手算 `center ± width/2`。

### 1.3 派生量：`ChartModels.cs:51-81`

```csharp
FlickCenterPos => StartPos;                              // :53-56
FlickWidth     => StartWidth;                            // :58-61
FlickLeftPos   => FlickCenterPos - FlickWidth * 0.5;     // :63-66
FlickRightPos  => FlickCenterPos + FlickWidth * 0.5;     // :68-71
FlickHeadPos   => FlickDirection == 16
                    ? FlickLeftPos : FlickRightPos;      // :73-81
```

**`FlickHeadPos` 是"箭头头"的位置**：黄/左(16) 取左端点，绿/右(4) 取右端点。
它同时被两处消费：

1. **视觉锚点** — `NoteRenderer.cs:1579`
   `BaseLocalPosition = new Vector3(FlickAnchorX(note), lineLocal.y + 0.4698f, lineLocal.z)`
   其中 `FlickAnchorX(note) = TrackX((float)note.FlickHeadPos)`（`NoteRenderer.cs:2672-2675`），
   `TrackX(position) = Mathf.Lerp(-0.81f, 0.81f, position)`（`NoteRenderer.cs:2854-2857`）。
   → 也就是说**箭头贴在判定线高度 `+0.4698` 的位置，尾巴从箭头往后长**。
   注意 `:1579` 写的是**字面量 `0.4698f`**，而不是常量 `SkyY`（`:237` 定义了 `SkyY = 0.4698f` 但**全文件未被引用**，
   属于死常量）。两者数值相同，改常量**不会**影响这里。
2. **命中特效定位** — `FlickHitEffectRenderer.cs:377-384`（burst 用 `note.FlickHeadPos`）、
   `:487`（反馈字用 `note.FlickCenterPos`）。

### 1.4 渲染层取数：`NoteRenderer.cs:1525-1527`、`:1621-1662`、`:2624-2663`

| 环节 | 位置 | 做什么 |
|---|---|---|
| 选模板 | `NoteRenderer.cs:1527` | `FlickDirection == 4 ? _flickRightPrefab : _flickLeftPrefab` |
| 父级缩放翻转 | `NoteRenderer.cs:1614-1617` | **仅 16（黄/左）** 把 `BaseLocalScale.x *= -1f`（左右镜像） |
| 基础旋转 | `NoteRenderer.cs:1580` | **仅 16** 用 `Quaternion.identity`；4 用模板自带的 `localRotation` |
| 抓子节点 | `NoteRenderer.cs:1623-1624` | `FindChildContaining(clone.transform, "Flick")` / `"Plane"`（按名字**包含**匹配，大小写不敏感，`NoteRenderer.cs:2859-2868`） |
| 记录原始缩放 | `NoteRenderer.cs:1627` | `FlickPlaneBaseScale = FlickPlane.localScale`（实测 `(-1,-1,-1)`） |
| 计算尾巴宽度 | `NoteRenderer.cs:1628-1647` | 见 §五 |
| 应用缩放 | `NoteRenderer.cs:1651-1654` | X 跟宽度、Y 只用固有比例 |
| 顶点级对齐 | `NoteRenderer.cs:1655` → `AlignFlickPlane` `:1792-1950` | 见 §三 |
| 记录尾巴世界长度 | `NoteRenderer.cs:1656` → `FlickTailWorldLength` `:1758-1790` | 用 **instance mesh**（`:1771` `mf.mesh`，不是 `sharedMesh`），因为 `AlignFlickPlane` 会改顶点 |
| 记录条纹密度 | `NoteRenderer.cs:1661` | `FlickStripesPerUnit = FlickStripesPerWorldUnit`（11.0） |
| 每帧写属性 | `NoteRenderer.cs:2624-2663` | `repeat = FlickTailWorldLength * FlickStripesPerUnit`（`:2632-2633`），再把 `_Color` / `_Opacity` / `_BarRepeat` 写进去 |

**属性写入的实权在 `NoteRendererPropertyState.cs`**：

```csharp
// :72-74  颜色语义（4 = 绿/右，16 = 黄/左）
var color = kind == NoteKind.Flick
    ? (direction == 4 ? new Color(0.2f, 0.95f, 0.55f, fade) : new Color(1f, 0.82f, 0.18f, fade))
    : ...;

// :75-78  Flick 只关心三样：_Opacity / _Color / _BarRepeat
var relevant = kind == NoteKind.Hold ? ...
    : kind == NoteKind.Flick ? Properties.Opacity | Properties.Color | (repeat > 0 ? Properties.BarRepeat : Properties.None)
    : Properties.Color | Properties.BaseColor | Properties.MainColor;

// :82-83  写 _Color（含 alpha = fade）
if ((relevant & Properties.Color) != 0 && (force || _color != color)) { _block.SetColor(ColorId, color); ... }
// :88-89  写 _Opacity = fade
if ((relevant & Properties.Opacity) != 0 && (force || _fade != fade)) { _block.SetFloat(OpacityId, fade); ... }
// :96-97  写 _BarRepeat = repeat
if ((relevant & Properties.BarRepeat) != 0 && (force || _repeat != repeat)) { _block.SetFloat(BarRepeatId, repeat); ... }
```

三个设计要点（都是踩过坑的）：

1. **`_Color` 同时承载颜色和透明度**（`a = fade`），所以 `_Opacity` 是**额外的**整体透明度乘数。
2. **属性存在性先探测再写**（`:109-112` `Cache` + `material.HasProperty(id)`），
   所以同一套代码能同时兼容 `dc.shader`（有 `_BarRepeat`）和 URP Unlit（没有）。
3. **隔帧快速返回**：`NoteRenderer.cs:2637-2642` 比较上一次写入的 kind/fade/progress/tint/direction/repeat，
   全部相同就 `return`，不碰 Unity 原生 Renderer API。这是热路径优化，**改 flick 的视觉参数时必须确认这些字段进了比较**，
   否则会出现"改了参数不刷新"。

---

## 二、渲染链路：两层两个 shader

### 2.1 prefab 层级与真实变换

`v5-3.prefab`（左/黄）与 `v5-3R.prefab`（右/绿）结构完全同构，只有数值镜像：

| 节点 | v5-3 localPos | v5-3 localRot | v5-3 localScale | v5-3R localPos |
|---|---|---|---|---|
| `v5-3`（root） | `(0, 0, 0)` | `quat(0,1,0,0)` = **绕 Y 180°** | `(1,1,1)` | `(0,0,0)`，rot = **identity** |
| `Plane`（尾巴） | `(-0.791, -0.042, 0.0008153589)` | `quat(~0, ~0, 1, -7.55e-8)` ≈ 绕 Z 180° | **`(-1,-1,-1)`** | `(-0.789, -0.04234165, 0.0008153589)` |
| `Flick`（箭头） | `(-0.799, -0.045, 0)` | identity | `(1,1,1)` | `(-0.797341, -0.045191422, 0)` |

> 数值出处：`v5-3.prefab:89-91`（root）、`:124-126`（Plane）、`:216-218`（Flick）；`v5-3R.prefab` 同构。
> 两个 root 的差异（180° vs identity）+ 子节点 X 位置的细微差（`-0.791` vs `-0.789`）就是**左右镜像**的实现方式，
> **不是**靠 UV 翻转或负缩放凑的。`NoteRenderer.cs:1616` 又额外对 16 做了一次 `scale.x *= -1`。

### 2.2 root 空间实测包围盒（逐顶点算出来的）

把网格顶点按上面的 TRS 逐级变换到 root 空间：

| prefab | 层 | x 范围 | y 范围 | 尺寸 |
|---|---|---|---|---|
| **v5-3** | `Flick`（箭头） | `0.0008 → 0.2328` | `−0.0069 → 0.1531` | **0.232 × 0.160** |
| **v5-3** | `Plane`（尾巴） | `0.0708 → 1.6068` | `0.0010 → 0.1470` | **1.536 × 0.146** |
| **v5-3R** | `Flick`（箭头） | `−0.2312 → 0.0008` | `−0.0070 → 0.1530` | 0.232 × 0.160 |
| **v5-3R** | `Plane`（尾巴） | `−1.6048 → −0.0688` | `0.0007 → 0.1467` | 1.536 × 0.146 |

两个直接结论：

1. **箭头在 Y 方向两端都超出尾巴约 0.007**（`−0.0069` vs `0.0010`；`0.1531` vs `0.1470`）——
   所以"箭头完全罩住尾巴的头部"**在几何上本来就成立**，不需要改网格。
2. **尾巴起点 0.0708 落在箭头右端 0.2328 内部**，重叠约 `0.162` 世界单位。
   这是**原始 prefab 的固有重叠**，运行时会由 `AlignFlickPlane` 顶到箭头后缘（见 §三）。

### 2.3 材质 / Shader / 贴图绑定（实测）

| 层 | prefab 节点 | 材质 | guid | Shader | 贴图槽 |
|---|---|---|---|---|---|
| 箭头（左） | `Flick`（v5-3） | `Mat.001.mat` | `5b65e1ef0a12ed24380b367ec857314d` | **URP `Unlit_1.shader`** (`f75247fd...`) | `_BaseMap` = `flickv5.png`（2048²） |
| 尾巴（左） | `Plane`（v5-3） | `FlickV5 Left.mat` | `5ce7c37f26e917d499575f6e71d17b34` | **`dc.shader`** (`55058b1d...`) | `_Bar`=`FlickNote-Bar.png`、`_Base`=`FlickNote-Base.png`、`_Core`=`FlickNote-Core.png` |
| 箭头（右） | `Flick`（v5-3R） | `Mat.001 R.mat` | `65cb8f12c4a4fd64dbacf621e55977e4` | **URP `Unlit_1.shader`** | `_BaseMap` = `FlickNoteCapR.png`（2048²） |
| 尾巴（右） | `Plane`（v5-3R） | `FlickV5 Right.mat` | `96bf87a014d661d4f8e5c1b55e79fe18` | **`dc.shader`** | `_Bar`=`FlickNoteR-Bar.png`、`_Base`=`FlickNoteR-Base.png`、`_Core`=`FlickNoteR-Core.png` |

> guid → 文件名 的对应是逐个读 `.meta` 反查的，不是猜的。
> **箭头层不走 `dc.shader`**：它用的是 URP 自带的 Unlit，片元就是
> `UnlitForwardPass.hlsl` 里的 `color = texColor.rgb * _BaseColor.rgb; alpha = texColor.a * _BaseColor.a;`。
> 所以 `dc.shader` 的 `_BarRepeat` / `_TailLift` / `_GlowPower` 等参数**对箭头完全无效**——
> 调这些参数时如果箭头没变化，那是**正确行为**，不是 bug。

**箭头层的颜色来自"预染色图集"**（这也是它为什么必须拆左右两套 prefab + 两套材质）。实测图集内
`Flick.asset` UV 覆盖区域的均值：

| 材质 | 图集 | UV 区域均值 RGB | 观感 |
|---|---|---|---|
| `Mat.001.mat`（左/黄） | `flickv5.png` | `(157, 154, 99)` | 偏黄 |
| `Mat.001 R.mat`（右/绿） | `FlickNoteCapR.png` | `(101, 154, 136)` | 偏绿 |

也就是说：**箭头的黄/绿是"贴图本身就染好的"**，而**尾巴的黄/绿是"白图 × `_Color` 算出来的"**。
两者机制不同 —— 这正是尾巴需要 `_TailLift`/`_TailDetint` 补偿、而箭头不需要的根本原因
（`dc.shader:122-124` 和 `docs/Flick_Tail_Fix.md:120-129` 都记了这条结构性差异）。

### 2.4 尾巴层的 shader 合成（`dc.shader`）

`dc.shader` 的文件头（`:1-55`）记的是从 bundle 里逆出来的原始状态；
真正生效的是 `Pass "Universal Forward"`（`:130-332`）。

**渲染状态**（`:134-141`）：

```hlsl
Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha   // :138 —— 预乘 alpha 的正确写法
ZWrite Off                                          // :139
ZTest  LEqual                                       // :140
Cull   Off                                          // :141  ← 注意与注释里的 "Cull Back" 不一致，以代码为准
```

> ⚠️ **文档/注释与代码不一致 #1**：`dc.shader:16` 的头注释写 `Cull Back`，
> 但实际 Pass 状态是 `Cull Off`（`dc.shader:141`）。**以代码为准**。
> 影响：面片双面可见，对薄片尾巴是必需的（否则从背面看会消失）。

**坐标**（`:211-229`）：

```hlsl
float2 uv     = i.uv;                                          // :211 网格原始 UV
float2 uvBase = float2(saturate(uv.x), saturate(uv.y));        // :215
float  barU   = uv.x * _Scale * _BarRepeat + _Bar_ST.z;        // :227  U 方向重复
float  barV   = uv.y * _Scale * 0.0        + _Bar_ST.w;        // :228  V 恒为 0
```

**三贴图合成**（`:235-246`，这就是原版 DXBC 的算式）：

```hlsl
float4 barC  = SAMPLE_TEXTURE2D(_Bar,  sampler_Bar,  uvBar);   // :235
barC.a = saturate(0.66 + (barC.a - 0.66) * _BarContrast);      // :239 围绕 0.66 放大对比
float4 baseC = SAMPLE_TEXTURE2D(_Base, sampler_Base, uvBase);  // :240
float4 coreC = SAMPLE_TEXTURE2D(_Core, sampler_Core, uvBase);  // :241

float3 rgb = (barC.rgb * barC.a + baseC.rgb * (1.0 - barC.a)) * (1.0 - coreC.a)
           + coreC.rgb * coreC.a;                              // :244-245
float  a   = max(baseC.a, coreC.a) * _Opacity;                 // :246
```

换个说法：
- `_Bar` 是**调制层**（用 alpha 在"条纹亮色"和 `_Base` 之间插值）；
- `_Core` 是**叠加层**（用 alpha 把高亮芯线压在最上面，`1-core.a` 让下面先被"挖空"再填）；
- **alpha 取 `max(base.a, core.a)`** —— 所以 `_Core` 亮的地方**不会**让整体更透明，
  这解释了为什么尾巴实测 alpha 一直在 0.88~1.00（见 `docs/Flick_Tail_Fix.md:54-65`）。

**染色 / 尾巴提亮 / 条纹二次调制**（`:252-275`）：

```hlsl
float3 rawRgb = rgb;                              // :252  未染色的原色（留着给尾巴回拉）
rgb *= _Color.rgb;                                // :254  C# 写入的方向色
float  tailW = saturate((uv.x - 0.12) / 0.45);    // :262  箭头处 0 → 尾部 1 的渐变权重
rgb = lerp(rgb, rawRgb, tailW * FlickTailDetint); // :263  尾巴往原色回拉（_TailDetint）
rgb *= lerp(1.0, _TailLift, tailW);               // :264  尾巴乘性提亮
float bandT    = saturate((barC.a - 0.44) / 0.45);          // :272  归一化到 0..1
float bandMask = lerp(1.0, 0.55 + 0.45 * bandT, _BarStrength); // :273
rgb *= bandMask;                                            // :275  提亮后重新压回条纹对比
```

> ⚠️ **`tailW` 用的 `uv.x` 是"尾巴的哪一段"**：`uv.x ≈ 0` 是**接箭头的那一端**（见 §四 UV 表），
> `uv.x → 1` 是尾巴尖。所以 `tailW = saturate((uv.x - 0.12) / 0.45)` 是
> **"离箭头越远、提亮/去染色越强"**。代码注释 `:256-261` 的表述方向一致。

**整体压暗 / 去饱和**（`:287-303`）：

```hlsl
rgb *= _OverallDim;                    // :287  保色相的整体压暗（0.86）
if (_SaturationTrim < 1.0) {           // :293  往"通道均值"靠，不是往 luma 靠
    float meanChannel = (rgb.r + rgb.g + rgb.b) / 3.0;
    rgb = lerp(float3(meanChannel,...), rgb, _SaturationTrim);
}
if (_Desaturate > 0.0) {               // :299  可选的手动去饱和（默认 0 = 关）
    float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    rgb = lerp(rgb, float3(luma,luma,luma), _Desaturate);
}
```

> 这里有个**反直觉但重要**的设计：去饱和**故意不用 luma**，而用**通道均值**。
> 原因写在注释 `:284-292`：黄色 `(1, 0.82, 0.18)` 的蓝通道很小，
> 往 luma 混会把蓝通道抬高好几倍 → 看起来**发白**（washed out），而不是变灰。

**alpha 尾巴软化 + discard + 预乘 + HDR**（`:306-329`）：

```hlsl
a *= lerp(1.0, _TailOpacity, tailW);       // :306  只软尾巴，箭头恒 1
float coreHot = coreC.a * coreC.a;         // :316  平方 → 只留细芯线
rgb += coreHot * (_CoreBoost - 1.0f) * 0.35f * _Color.rgb;   // :318  LDR 阶段的轻微补亮
if (a <= 0.0005) discard;                  // :323
float4 premul = IF_Premultiply(float4(rgb, a));               // :327  预乘 → (rgb*a, a)
premul.rgb += coreHot * (_GlowPower - 1.0f) * 1.6f;           // :328  HDR 提亮在预乘之后
return premul;                                                // :329
```

**这一步是全篇最关键的顺序问题**：`_GlowPower` 的提亮**必须写在预乘之后**。
写之前的话 `rgb * a` 会把 >1 的值拉回 Bloom 阈值以下，发光直接消失（这是第四轮的缺陷 C）。

---

## 三、`AlignFlickPlane`：顶点级对齐（`NoteRenderer.cs:1792-1950`）

这是 Flick 里**唯一一处会改网格顶点**的代码，也是历史上出错最多的地方。

### 为什么需要它

prefab 里尾巴与箭头**天生重叠 0.162 世界单位**（§2.2）。而 `NoteRenderer` 还会把尾巴
按谱面宽度重新缩放 X（§五），缩放后重叠量会变。所以必须在 `CreateVisual` 之后
**每个 flick 各算一次**，把尾巴顶到箭头后缘。

### 它做了三件事

**① 测量（root 空间）** `:1806-1838`

```csharp
// 箭头：逐个顶点 变到 root 空间，取 X/Y 极值（用 sharedMesh，因为箭头网格不变形）
Vector3 p = visual.RootTransform.InverseTransformPoint(visual.FlickHead.TransformPoint(headVertices[k]));
// → headMinX / headMaxX / headMinY / headMaxY      :1816-1824
float headCenterX = (headMinX + headMaxX) * 0.5f;    // :1825

// 尾巴：4 个角点同样变到 root 空间（用 mesh = instance，因为会改）
rootLocal[i] = visual.RootTransform.InverseTransformPoint(visual.FlickPlane.TransformPoint(planeVertices[i]));
// → plateMinX / plateMaxX                            :1831-1837
float plateCenterX = (plateMinX + plateMaxX) * 0.5f;  // :1838
```

**② 判断尾巴往哪边长 + 平移到后缘** `:1840-1885`

```csharp
// 用「相对中心」判断，不看绝对屏幕位置（旧的 planeCenterX < 0 写法会让行为随音符位置翻转）
bool tailGrowsPositive = plateCenterX >= headCenterX;      // :1844
float headBackEdgeX = tailGrowsPositive ? headMaxX : headMinX;  // :1847
float plateNearX    = tailGrowsPositive ? plateMinX : plateMaxX; // :1850
float targetNearX = headBackEdgeX + (tailGrowsPositive ? FlickTailInset : -FlickTailInset); // :1856-1858
float deltaRootX  = targetNearX - plateNearX;              // :1859
```

**父级带负缩放，root X 与 local X 差一个符号**，所以它**实测**映射率而不是假设：

```csharp
// :1866-1884  nudge 尾巴 +1 local X，量 root X 走了多少
float rootXBefore = ...TransformPoint(planeVertices[0])).x;   // :1871-1872
visual.FlickPlane.localPosition = savedPos + new Vector3(1f, 0f, 0f);  // :1873
float rootXAfter  = ...TransformPoint(planeVertices[0])).x;   // :1874-1875
visual.FlickPlane.localPosition = savedPos;                   // :1876
float dRootPerLocalX = rootXAfter - rootXBefore;              // :1878
moved.x += deltaRootX / dRootPerLocalX;                       // :1882
```

**③ 把「接箭头的那一对顶点」逐点贴到箭头的后缘轮廓** `:1887-1949`

```csharp
// 量箭头在「底行」和「顶行」的后缘 X（容忍 2% 的 Y 误差）
float yTolerance = Mathf.Max(0.0001f, (headMaxY - headMinY) * 0.02f);   // :1897
// → backBottomX / backTopX                                              :1898-1914

// 对每个尾巴角点：找到 Y 相近的配对点，只有"朝向箭头"的那一对才被改造
float normalizedY = Mathf.Clamp01((rootLocal[m].y - headMinY) / yRange);      // :1938
rootLocal[m].x = Mathf.Lerp(backBottomX, backTopX, normalizedY)
               + (tailGrowsPositive ? FlickVertexSeamOffset : -FlickVertexSeamOffset);  // :1941-1942
planeVertices[m] = visual.FlickPlane.InverseTransformPoint(
    visual.RootTransform.TransformPoint(rootLocal[m]));                        // :1943-1944

planeMesh.MarkDynamic();                                                       // :1947
planeMesh.vertices = planeVertices;                                            // :1948
planeMesh.RecalculateBounds();                                                 // :1949
```

因为箭头后缘是**斜的**（`Flick.asset` 的端帽是钩状），所以尾巴的对接边也被拉成同样斜度，
避免出现三角缝。`FlickVertexSeamOffset` 默认 `0.0`（`NoteRenderer.cs:225`）——
**注释明确说"必须保持 0"，它就是当初把尾巴顶进箭头里的元凶**。

### 相关常量（`NoteRenderer.cs:218-231`）

| 常量 | 值 | 位置 | 作用 |
|---|---|---|---|
| `FlickHeadOverlap` | `0.08f` | `:218` | **历史遗留，当前未使用**（全文件只有定义处出现） |
| `FlickTailInset` | **`0.0f`** | `:222` | 0 = 尾巴正好接在箭头后缘；`>0` 往外留缝；`<0` 往里塞 |
| `FlickVertexSeamOffset` | **`0.0f`** | `:225` | 对接边额外位移，**必须 0** |
| `FlickStripesPerWorldUnit` | `11.0f` | `:228` | 每世界单位 11 条纹（与宽度解耦） |
| `FlickTailHeightScale` | **`0.93f`** | `:231` | 尾巴高度系数（见 §五） |
| `TrackHalfWidth` | `0.81f` | `:233` | 轨道半宽（`TrackX` 的端点） |
| `SkyTrackWidth` | `1.62f` | `:235` | 谱面归一化宽度 → 世界宽度的系数 |
| `SkyY` | `0.4698f` | `:237` | flick 贴在判定线上方的高度；**该常量未被引用**，`:1579` 用的是同值字面量 |

> ⚠️ **文档与代码不一致 #2**：`docs/Flick_Round5_Fix.md:110` 说
> `FlickTailHeightScale` 默认 `1.0`（"= 保持 91.25% 的固有比例"），
> 但**代码里是 `0.93f`**（`NoteRenderer.cs:231`），也就是说实际尾巴高度是
> `0.146 × 0.93 = 0.13578`，相对箭头 0.160 只有 **84.86%**，不是 91.25%。**以代码 0.93 为准**。

---

## 四、斜纹（条纹）的产生机理

这是全篇最容易被误解的部分。**斜纹不是"画"出来的，是"切"出来的。**

### 4.1 `_Bar` 是单条纹单元，靠 U 重复

- `FlickNote-Bar.png` / `FlickNoteR-Bar.png`：**64 × 128**，装着**恰好一条**竖条。
- 横截面（实测该图 alpha）：中间亮、两侧暗，**alpha 全程只在 0.443 ~ 0.890 之间**
  （这也是为什么 `_BarContrast` 要围绕 `0.66` 放大偏差）。
- 沿 V 方向**恒定**（实测同一列在 128 行上 alpha 标准差 ≤ 0.0056），沿 U 方向才有变化（行内标准差可达 0.18）。
  → 它天生就是"一条竖线"，**不该沿 V 平铺**。

于是 `dc.shader:227-228` 的采样坐标：

```hlsl
float barU = uv.x * _Scale * _BarRepeat + _Bar_ST.z;   // U 重复 _BarRepeat 次
float barV = uv.y * _Scale * 0.0        + _Bar_ST.w;   // V 乘 0 → 恒为 _Bar_ST.w
```

**U 的重复次数 = 可见的竖条数**（`_BarRepeat` 默认 `10.0`，见 §七）。
V 被乘 0 是照抄原版 DXBC 的字面量 `(32.0, 0, 0, 0)`（`dc.shader:191` 的汇编注释）。

> 必要条件：**`_Bar` 贴图的 wrap 必须是 Repeat**。
> 实测 `FlickNote-Bar.png.meta` / `FlickNoteR-Bar.png.meta` 都是 `wrapU: 0 / wrapV: 0` = Repeat
> （URP/Unity 的 TextureWrapMode：0=Repeat、1=Clamp）。
> 如果是 Clamp，U 超过 1 的部分会被钳到边缘一列 → **10 条条纹塌成一条，画面变纯色**。
> 这是 2026-11-06 修过的缺陷 3，**改动贴图导入设置时最先检查这一条**。
> 对比：`_Base` / `_Core` 都是 `wrapU: 1 / wrapV: 1`（Clamp），它们不需要重复，是正确的。

### 4.2 "斜"来自 `Plane` 网格的剪切

`Plane.asset` 只有 4 个顶点（逐字节解出来的，不是抄文档）：

| 顶点 | x | y | z | u | v |
|---|---|---|---|---|---|
| v0 | `+0.57422` | `+0.18905` | `−0.00762` | `0.0` | `0.9991` |
| v1 | `+0.72022` | `+0.04305` | `−0.00762` | `0.0` | `0.0009` |
| v2 | `−0.66978` | `+0.04305` | `−0.00762` | `1.0` | `0.0009` |
| v3 | `−0.81578` | `+0.18905` | `−0.00762` | `1.0` | `0.9991` |

推导：

```text
高        = 0.18905 − 0.04305      = 0.14600        （与 root 空间测得的 0.146 一致）
长        = 0.72022 − (−0.81578)   = 1.53600        （与 root 空间测得的 1.536 一致）
上边 X 位移 = (v3.x + v0.x)/2 − (v2.x + v1.x)/2
            = (−0.81578 + 0.57422)/2 − (−0.66978 + 0.72022)/2
            = (−0.12078) − (0.02522) = −0.14600
⇒ dx/dy = |−0.14600| / 0.14600 = 1.000
⇒ 倾角 = atan(1.000) = 45.0°
```

**这就是 45° 的全部来源**：`Plane` 是一个**剪切平行四边形**（不是矩形），
而 UV 沿 X 线性分布（`u` 在 x 最大端为 0、x 最小端为 1），于是"竖条"被这层剪切拉成了 45° 斜带。

- 不需要在 shader 里旋转 UV；
- 不需要屏幕空间效果；
- **不要为了"让斜纹更斜"去改 UV**，改网格剪切量才是根因层面的做法（但没必要，45° 已与原版一致）。

### 4.3 UV 与几何的对应关系（画图时常写错，这里给实测表）

把 `Plane.asset` 顶点按 prefab TRS 变到 v5-3 root 空间：

| u | v | root X | root Y | 含义 |
|---|---|---|---|---|
| 0.000 | 0.0009 | `+0.0708` | `+0.0010` | 尾巴**接箭头的那一端**，下边 |
| 0.000 | 0.9991 | `+0.2168` | `+0.1470` | 接箭头端，上边 |
| 1.000 | 0.0009 | `+1.4608` | `+0.0010` | 尾巴**尖**，下边 |
| 1.000 | 0.9991 | `+1.6068` | `+0.1470` | 尾巴尖，上边 |

**关键：`uv.x = 0` 是靠近箭头的一端，`uv.x = 1` 是尾巴尖。**
（`Plane` 节点 `localScale = (-1,-1,-1)` 把网格原始 X 翻转了一次，
 所以网格里 x 最大的顶点 `u=0` 落在 root 的**小** X 处。）

这条直接决定了 `dc.shader:262` 的 `tailW` 方向：`uv.x` 越大 = 离箭头越远 = 提亮越多。
也决定了 `_Base`/`_Core` 的 U 语义：**`u≈0` 处应当最亮最实（箭头根部），`u→1` 处应当淡出（尾巴尖）**。
实测贴图确实如此：`FlickNote-Base.png` 在 `u=0` 处 v 全覆盖，`u=0.5` 处 v 只剩 `0..0.19`，
`u=0.97` 处只剩 `0..0.008`；`FlickNote-Core.png` 的峰值 alpha 也从 `u=0` 的 0.94 递减到 `u=0.97` 的 0.05。
（`_Core` 的 alpha 沿 U 单调衰减这件事，正是尾巴尖端"自然收细"的来源。）

### 4.4 ⚠️ 左右两张 `_Bar` 的 alpha 范围**不一样**，而 shader 常量只按左版调过

这是一个**真实存在、但历史文档从未记录**的不对称。逐像素实测 alpha 分布：

| 贴图 | alpha min | alpha max | alpha 均值 | 中位数 |
|---|---|---|---|---|
| `FlickNote-Bar.png`（左/黄） | **0.443** | **0.890** | 0.771 | 0.882 |
| `FlickNoteR-Bar.png`（右/绿） | **0.184** | **0.698** | 0.556 | 0.690 |

而 `dc.shader` 里三处常量**硬编码了左版的范围**：

```hlsl
barC.a = saturate(0.66 + (barC.a - 0.66) * _BarContrast);   // :239  轴心 0.66 ≈ 左版中点 0.6665
float bandT = saturate((barC.a - 0.44) / 0.45);             // :272  分母 0.45 ≈ 左版跨度 0.447
```

按这两条式子手算两版的条纹调制幅度（`_BarContrast = 2.6`、`_BarStrength = 0.55`）：

| | 对比后 `barC.a` | `bandT` | 最终 `bandMask` | **调制幅度** |
|---|---|---|---|---|
| 左/黄 | `0.096 → 1.000` | `0.000 → 1.000` | `0.752 → 1.000` | **0.248** |
| 右/绿 | `0.000 → 0.759` | `0.000 → 0.708` | `0.752 → 0.928` | **0.175** |

**结论：右/绿 flick 的斜纹比左/黄弱约 29%**（0.175 / 0.248），
根因是右版贴图的 alpha 整体低约 0.2（0.184 vs 0.443），而规范化常量是按左版写的。

**怎么修（三选一）**：

1. **最小改动**：把 `FlickNoteR-Bar.png` 的 alpha 整体映射到 `0.443..0.890` 区间（不改代码，只改图）。
2. **改 shader**：把 `0.66 / 0.44 / 0.45` 这三个常量提成属性，左右材质各给一组值。
3. **接受差异**：如果原版本来就是"绿色条纹更淡"，那这是**正确复刻**，不要动 —— 需拿原版截图对比确认。

> 本文只陈述实测事实，**不判断哪种是"对"的**。但排查"为什么绿色 flick 看起来条纹不明显"时，
> 这一条必须先排除，别去调 `_BarContrast`（那会同时影响左右两侧）。

### 4.5 条纹疏密是"每世界单位恒定的"，不是固定条数

**这点很容易写错**：`_BarRepeat` 在材质里的默认值 `10.0` **几乎从不生效**，
因为运行时每帧都会被 `MaterialPropertyBlock` 覆盖：

```csharp
// NoteRenderer.cs:2632-2633
float repeat = visual.FlickStripesPerUnit > 0f
    ? visual.FlickTailWorldLength * visual.FlickStripesPerUnit : 0f;

// NoteRenderer.cs:1661（初始化时）
visual.FlickStripesPerUnit = FlickStripesPerWorldUnit;   // = 11.0f (:228)

// NoteRenderer.cs:1656（初始化时，在 AlignFlickPlane 之后测）
visual.FlickTailWorldLength = FlickTailWorldLength(visual);   // :1758-1790
```

即：

```text
_BarRepeat(运行时) = 尾巴世界长度 × 11.0
```

**为什么这么做**（注释 `:1657-1660`）：尾巴的 UV 永远是 0..1，
如果 `_BarRepeat` 是常数，那么**短 flick 的条纹会显得更密、长 flick 更稀**。
改成跟世界长度成正比后，**任意宽度的 flick 条纹密度完全一致**。

举个实数（按 §5.1 的公式手算）：

| `width` | `span = width×1.62` | `tailSpan = span−0.232` | `_BarRepeat = tailSpan×11` |
|---|---|---|---|
| 0.10 | 0.162 | 0.0307（触发 `meshLen×0.02` 下限） | **0.34** |
| 0.25 | 0.405 | 0.1730 | **1.90** |
| **0.50** | 0.810 | 0.5780 | **6.36** |
| 1.00 | 1.620 | 1.3880 | **15.27** |

一个 `flick(3000,18,24,12,4)`（`width = 12/24 = 0.5`）的 `_BarRepeat ≈ 6.36` ——
比材质默认的 10.0 稀，比早期文档写的 5.5 密。

> ⚠️ **文档与代码不一致 #3**：`docs/Flick_Stripe_Fix.md:79` 说 `_BarRepeat` 默认 `5.5`，
> `docs/Flick_Round5_Fix.md:74` 说提到 `8.0`，而**现在 `dc.shader:81` 的默认是 `10.0`**，
> 且**运行时会被 `尾巴世界长度 × 11.0` 覆盖**。三个数都不是"你现在看到的效果"。
> 调密度请改 `NoteRenderer.cs:228` 的 `FlickStripesPerWorldUnit`（而不是材质上的 Bar Repeat）。

---

## 五、尾巴几何：X 跟宽度，Y 只吃固有比例

### 5.1 公式

```csharp
// NoteRenderer.cs:1628-1654
float span = Mathf.Max(0.05f, FlickWorldWidth(note));     // = width * 1.62          :2679
MeshFilter planeMeshFilter = ...;                          // :1632
float meshLen = 1.536f;                                    // :1633  兜底值
Bounds b = planeMeshFilter.sharedMesh.bounds;
meshLen = Mathf.Max(0.0001f, b.size.x);                    // :1637-1638  实测 1.536
float headWorldWidth = FlickHeadWorldWidth(visual);        // :1645 实测 0.232
float tailSpan   = Mathf.Max(meshLen * 0.02f, span - headWorldWidth);  // :1646
float widthRatio = tailSpan / meshLen;                     // :1647

visual.FlickPlane.localScale = new Vector3(
    visual.FlickPlaneBaseScale.x * widthRatio,             // :1652  X 跟宽度
    visual.FlickPlaneBaseScale.y * FlickTailHeightScale,   // :1653  Y 只吃固有比例
    visual.FlickPlaneBaseScale.z);                         // :1654
```

链式展开：

```text
世界宽度      FlickWorldWidth(note) = note.FlickWidth × 1.62                 (NoteRenderer.cs:2679)
尾巴世界长度  tailSpan = max(1.536×0.02, width×1.62 − 0.232)                 (:1646)
X 缩放        widthRatio = tailSpan / 1.536                                  (:1647)
尾巴高度      0.146 × FlickTailHeightScale = 0.146 × 0.93 = 0.13578          (:1653 + :231)
```

### 5.2 三个关键数字

| 项 | 值 | 来源 | 说明 |
|---|---|---|---|
| `Plane` 固有高度 | **0.146** | `Plane.asset` 顶点实测；root 空间也是 0.146 | 网格自身 |
| `Flick`（箭头）固有高度 | **0.160** | `Flick.asset` 顶点实测；root 空间也是 0.160 | 网格自身 |
| 比值 | **91.25%** | `0.146 / 0.160 = 0.9125` | 尾巴**天生**比箭头矮 8.75% |
| `FlickTailHeightScale` | **0.93** | `NoteRenderer.cs:231` | 在 91.25% 基础上再 ×0.93 → **实际 84.86%** |
| `1.62` | `SkyTrackWidth` | `NoteRenderer.cs:235` | 谱面归一化宽度 → 世界宽度 |
| `1.536` | `Plane` X 跨度 | `Plane.asset` 实测 | X 缩放的分母（**不再是老的 1.54 魔数**） |
| `0.232` | `Flick` X 跨度 | `Flick.asset` 实测 | 计算尾巴长度时要减掉的箭头宽度 |

### 5.3 为什么 Y 不能乘 `widthRatio`（历史教训）

第三轮（`docs/Flick_Glow_Width_Height_Fix.md:70-78`）为了修"尾巴偏矮"，把 Y 也乘了 `widthRatio`。
这导致**宽度一变高度就跟着变**，长宽比彻底失控。
第五轮（`docs/Flick_Round5_Fix.md:80-110`）纠正为"Y 只吃固有值"，这就是当前代码 `:1653` 的形态。

正确的理解是：**尾巴高度是网格固有属性，与谱面宽度无关**；
唯一的人为干预是 `FlickTailHeightScale`（乘性系数），它不再是 `widthRatio` 的函数。

### 5.4 为什么尾巴长度要减掉箭头宽度（`:1640-1647` 注释）

`note.FlickWidth` 是**整个 flick 的宽度（含箭头）**。
如果把尾巴按**全宽**缩放，尾巴会**正好超出箭头一个箭头宽度**——
这正是"尾巴看起来太宽、还从箭头里透出来"的根因。
所以 `tailSpan = span − headWorldWidth`（`:1646`），`headWorldWidth` 从箭头网格实测（`:1732-1755`）。

### 5.5 一个必须强调的区别：**网格高度 ≠ 看起来的高度**（尾巴是渐细的）

上面的 0.146 / 91.25% / 84.86% **全部是网格高度**。
但尾巴在画面上**不是一个等高矩形**：`_Base` 贴图的 alpha 沿 U 方向强烈收窄，
而 alpha 直接决定"哪里看得见"。实测（`FlickNote-Base.png`，列取 `v > 0.5` 的上边界，
`index 0 = Unity v=0`）：

| u | base alpha 上边界 v | 换算成可见高度（×0.146） |
|---|---|---|
| 0.00（接箭头端） | 0.992 | **0.1449** |
| 0.10 | 0.654 | 0.0954 |
| 0.20 | 0.465 | 0.0678 |
| 0.30 | 0.339 | 0.0494 |
| 0.50 | 0.181 | 0.0264 |
| 0.70 | 0.087 | 0.0126 |
| 0.90 | 0.024 | 0.0034 |
| 0.97 | 0.008 | 0.0011 |
| 1.00（尾巴尖） | 0.000 | 0 |

**结论**：尾巴在 `u=0` 处**几乎满高**（0.1449 ≈ 0.146），然后**单调收细到 0**，
视觉上是一条**渐细的斜带**，而不是一条等宽等高的矩形。

这解释了三件容易困惑的事：

1. **"尾巴比箭头矮 15%"只在该看根部成立**——量尾巴尖的时候会量到接近 0，那不是 bug。
2. **`FlickTailHeightScale` 只能整体放大最粗处**，改变不了收细曲线；收细曲线在贴图里。
   想让尾巴"更实、不那么尖"，应该改 `_Base` 的 alpha 分布，而不是调缩放。
3. **`_Core` 的 alpha 峰值也沿 U 衰减**（实测 `u=0` 处 0.94 → `u=0.5` 处 0.89 → `u=0.9` 处 0.22 → `u=1` 处 0），
   配合 `coreHot = coreC.a²`（`dc.shader:316`）会让芯线在尾部**加速消失**，尖端自然不发散。

> 这也意味着：**别拿"尾巴高度"和"箭头高度"直接从截图量比例来验证**。
> 要验证 91.25% 必须量**网格顶点**（`Plane.asset` 的 0.146 vs `Flick.asset` 的 0.160）。

---

## 六、颜色与发光

### 6.1 颜色语义（硬编码，改不了）

```csharp
// NoteRendererPropertyState.cs:72-74
kind == NoteKind.Flick
  ? (direction == 4  ? new Color(0.2f, 0.95f, 0.55f, fade)    // 绿，向右
                      : new Color(1f,   0.82f, 0.18f, fade))   // 黄，向左
  : ...
```

| 方向值 | 颜色 | 语义 | prefab | 尾巴材质 |
|---|---|---|---|---|
| **4** | `(0.20, 0.95, 0.55)` 绿 | 向右 | `v5-3R` | `FlickV5 Right.mat` |
| **16** | `(1.00, 0.82, 0.18)` 黄 | 向左 | `v5-3` | `FlickV5 Left.mat` |

**只有这两个值**。`ChartParser.cs:359-364` 会在解析期就拒掉其它值；
`JudgementEngine.cs:183-186` 在判定入口再挡一次（`direction != 4 && direction != 16` → 返回 `None`）。

> 注意方向与"左/右"的对应**是反直觉的**：4=右 用的是 `v5-3R`（R = Right，对），
> 16=左 用的是 `v5-3`（无后缀，即 Left）。
> 但**颜色**里 4 是绿、16 是黄。写文档/代码时不要按"黄=右"去推。

### 6.2 预乘 alpha 与混合模式

`dc.shader` 的片元返回**已预乘**的颜色（`Infalsus_Common.hlsl:127-134`）：

```hlsl
float4 IF_Premultiply(float4 c) { return float4(c.rgb * c.a, c.a); }   // INFALSUS_PREMULTIPLY = 1
```

所以混合必须是**预乘正确**的写法（`dc.shader:138`）：

```hlsl
Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
```

**如果写成 `Blend SrcAlpha OneMinusSrcAlpha`**，展开就是：

```text
dst = srcRGB × srcA + dstRGB × (1 − srcA)
    = (rgb × a) × a + ...          ← alpha 被乘了两次（a²）
```

`a ≈ 0.9` 时相当于**亮度再掉 19%**，而且 HDR 亮度永远顶不到 Bloom 阈值。
这条同时解释了历史上的"偏暗"和"不发光"（`docs/Flick_Glow_Width_Height_Fix.md:19-44`）。

### 6.3 HDR 提亮的顺序（唯一正确写法）

```hlsl
float4 premul = IF_Premultiply(float4(rgb, a));      // dc.shader:327
premul.rgb += coreHot * (_GlowPower - 1.0f) * 1.6f;  // dc.shader:328  ← 必须在预乘之后
```

`coreHot = coreC.a * coreC.a`（`:316`）——**平方**是为了只让芯线最亮的那一小段越阈值，
避免整个尾巴一起发光变成一团糊。`_GlowPower = 1.0` 时加 0（不发光），
`1.8`（默认）时最亮处加 `coreHot × 0.8 × 1.6 ≈ 1.28 × coreHot`。

### 6.4 Bloom（发光能不能看见，取决于这个后处理）

`Assets/Settings/DefaultVolumeProfile.asset`（实测）：

| 参数 | 值 |
|---|---|
| `threshold` | **1.5** |
| `intensity` | **2** |
| `scatter` | **0.821** |
| `active` | `1` |

`intensity = 0` 时后处理**完全不跑 Bloom**，`_GlowPower` 再怎么调也没有光晕。
这是第四轮的缺陷 A（`docs/Flick_Glow_Width_Height_Fix.md:7-18`）。

**调参顺序建议**：先确认 Bloom `intensity > 0`，再调 `_GlowPower`，最后才考虑 `_CoreBoost`。

### 6.5 一个容易忽略的事实：颜色被"压暗"过

`dc.shader:287-297` 会无条件乘上：

```text
rgb *= _OverallDim            (0.86)
rgb  → 往通道均值 lerp(_SaturationTrim)   (0.72)
```

也就是说**最终画面上的颜色不是 `(1, 0.82, 0.18)`**，而是被压暗 + 轻微去饱和后的版本。
所以"用取色器量出来和常量表对不上"是**正常的**，不要在常量表上纠错。
如果想让画面更接近纯正的黄/绿，应该调 `_OverallDim` 和 `_SaturationTrim`，
而**不是**改 `NoteRendererPropertyState.cs:72-74` 的常量（改了会让 C# 意图与画面双重偏移）。

---

## 七、`dc.shader` 参数速查表

以下**默认值全部逐行从 `dc.shader:71-100` 的 `Properties` 读出**（不是抄文档）。

| 属性 | 默认值 | 范围/类型 | 作用 | 改它会怎样 |
|---|---|---|---|---|
| `_Base` | `"white"` | 2D, `[NoScaleOffset]` | 尾巴主体形状与 alpha | 换成非条状图 → 尾巴形状崩掉 |
| `_Bar` | `"white"` | 2D, `[NoScaleOffset]` | 单条纹单元（64×128） | 必须 Repeat wrap，且必须是单条纹 |
| `_Core` | `"white"` | 2D, `[NoScaleOffset]` | 高亮芯线 + HDR 发光源 | 换成无芯线的图 → 没有发光 |
| `_Scale` | **`1.0`** | Float | UV 缩放（`barU`/`barV` 都乘它） | 影响**条纹密度**（与 `_BarRepeat` 相乘）；一般不动 |
| `_Opacity` | **`1.0`** | Float | 整体 alpha 乘数（`:246`） | 运行时被 `fade` 覆盖（`NoteRendererPropertyState.cs:88-89`） |
| `_Color` | **`(1,1,1,1)`** HDR | Color | 方向染色（C# 写入） | 材质里的白只是兜底；运行时被覆盖成绿/黄 |
| `_CoreBoost` | **`1.6`** | `Range(0,8)` | 芯线 LDR 额外亮度（`:318`，×0.35） | ↑ 芯线更亮（仍受 Bloom 阈值约束） |
| `_BarRepeat` | **`10.0`** | `Range(1,32)` | U 方向条纹重复次数 | **运行时被 `尾巴世界长度 × 11.0` 覆盖**；改材质无效，改 `NoteRenderer.cs:228` |
| `_BarContrast` | **`2.6`** | `Range(0,4)` | 条纹对比（围绕 0.66 放大偏差，`:239`） | ↑ 条纹更深；过大（>3.5）出现硬边台阶 |
| `_TailLift` | **`1.45`** | `Range(1,2.5)` | 尾巴乘性提亮（`:264`） | ↑ 尾巴更亮；>2.2 开始发白 |
| `_TailOpacity` | **`1.0`** | `Range(0.3,1)` | 尾巴 alpha 乘数（`:306`） | 1 = 不变；<1 尾巴变透（默认别动，实测尾巴本来就 0.88~1.0 不透明） |
| `_TailDetint` | **`0.40`** | `Range(0,1)` | 尾巴"去染色"程度（`:263`，往未染色原色回拉） | 0 = 最黄最暗；1 = 完全用原色最亮 |

> 小坑：`_TailDetint` 在片元里**不是直接用名字**，而是通过 `#define FlickTailDetint _TailDetint`（`dc.shader:179`）
> 以 `FlickTailDetint` 出现（`:263`）。所以**按 `_TailDetint` 字面量搜代码会搜不到使用点**，
> 容易误判成"参数没接线"。同理 `if (_SaturationTrim < 1.0)`（`:293`）是唯一一处**用条件包裹**的参数——
> 一旦设成 `1.0`，整个去饱和分支连编译带执行都被跳过（性能上的小优化，行为上等价）。
| `_BarStrength` | **`0.55`** | `Range(0,1)` | 提亮后**二次**压回条纹对比（`:273-275`） | 0 = 无条纹（平坦）；1 = 最深压到 55% |
| `_Desaturate` | **`0.0`** | `Range(0,1)` | 手动去饱和（往 luma，`:299-303`） | 默认关；>0 会**发白**（蓝通道被抬高） |
| `_SaturationTrim` | **`0.72`** | `Range(0,1)` | 往**通道均值**靠的去饱和（`:293-297`） | 1 = 不变；↓ 更灰（推荐用这个而不是 `_Desaturate`） |
| `_OverallDim` | **`0.86`** | `Range(0.5,1.2)` | 保色相整体压暗（`:287`） | 1 = 原亮度；↑ 更亮更艳 |
| `_GlowPower` | **`1.8`** | `Range(1,8)` | HDR 越阈值倍数（`:328`，**在预乘之后**） | 1 = 不发光；↑ 光晕更强（需 Bloom 开启） |
| `_Surface` | `1.0` | `[HideInInspector]` | Shader Graph 遗留，不参与运算 | 别动 |
| `_Blend` | `0.0` | `[HideInInspector]` | 同上 | 别动 |
| `_AlphaClip` | `0.0` | `[HideInInspector]` | 同上 | 别动 |
| `_SrcBlend` / `_DstBlend` | `1.0` / `10.0` | `[HideInInspector]` | 材质侧混合兜底；Shader 里硬编码 `Blend One OneMinusSrcAlpha` | 别动 |
| `_SrcBlendAlpha` / `_DstBlendAlpha` | `1.0` / `10.0` | `[HideInInspector]` | 同上 | 别动 |
| `_ZWrite` | `0.0` | `[HideInInspector]` | Shader 硬编码 `ZWrite Off` | 别动 |
| `_ZTest` | `4.0` | `[HideInInspector]` | LEqual | 别动 |
| `_Cull` | `2.0` | `[HideInInspector]` | **Shader 硬编码 `Cull Off`，此字段不生效** | 别动 |
| `_CastShadows` / `_AlphaToMask` | `0.0` / `0.0` | `[HideInInspector]` | Shader Graph 遗留 | 别动 |
| `_QueueOffset` / `_QueueControl` | `0.0` / `-1.0` | `[HideInInspector]` | 队列偏移 | 别动 |

**材质里实际存储的（`FlickV5 Left.mat`）**：`_Scale: 1`、`_Opacity: 1`、`_CoreBoost: 1.6`、
`_Color: (1,1,1,1)`；其余 12 个新增参数**材质里没有存**，全靠 shader 默认值。
（`FlickV5 Right.mat` 更精简，只存 `_Opacity: 1`、`_Scale: 1`、队列两项。）

> 这两份材质的不对称是**原版遗留**（右侧材质没有被完整重新序列化）。
> 只要 shader 的 `Properties` 默认值正确，两者视觉效果应当一致；
> 但如果你在 Inspector 里只改左侧材质，**右侧不会跟着变** —— 这是常见困惑点。

### 渲染顺序与队列（重要澄清）

| 材质 | `m_CustomRenderQueue` 实测 |
|---|---|
| `Mat.001.mat`（箭头） | **3012** |
| `Mat.001 R.mat`（箭头） | **3012** |
| `FlickV5 Left.mat`（尾巴） | **3012** |
| `FlickV5 Right.mat`（尾巴） | **3012** |

> ⚠️ **文档与代码不一致 #4（最需要提醒的一条）**：
> `docs/Flick_Round5_Fix.md:30-39` 与 `:116` 声称把箭头队列提到 **3014** 以实现"箭头必定盖住尾巴"，
> 但**实测四个材质全是 3012**。
> 复核证据：`git show HEAD:.../Mat.001.mat` 与工作区**都是 `m_CustomRenderQueue: 3012`**（HEAD 第 20 行、`Mat.001 R.mat` 第 33 行），
> `git grep 3014 -- "Assets/InFalsus/Recovered/Material/*"` **零命中**；
> 全工程能搜到 `3014` 的只有两处**未生效的脚本**（`autobackups/flick_fix/fix_r5.py:21`、`audit7.py:42`，
> 它们**写好了补丁但没有落地**）以及大量无关的二进制/日志文件。
> **结论：设计意图是箭头(3014) 高于尾巴(3012)，但该改动并未落地，当前两者同队列 3012。**
> 同队列内透明物体的绘制顺序**不保证**，所以"箭头有时被尾巴盖住"这个问题**在当前代码里依然存在**。
> 若要真正修复，需要把 `Mat.001.mat` / `Mat.001 R.mat` 的 `m_CustomRenderQueue` 改成 3014（或更高）。
> 本文以**代码实测的 3012** 为准。

---

## 八、命中特效：`FlickHitEffectRenderer`

### 8.1 常量表（`FlickHitEffectRenderer.cs:13-22`）

| 常量 | 值 | 位置 | 作用 |
|---|---|---|---|
| `BurstDurationFallback` | **`0.5333333f`** | `:13` | 动画片段读不到时的兜底时长（≈ 0.5333 s） |
| `BurstFadePower` | **`2.0f`** | `:15` | burst alpha 淡出幂次：`pow(1−normalized, 2.0)`（`:576`） |
| `FeedbackDuration` | **`0.58f`** | `:16` | 判定反馈字总时长 |
| `FeedbackHeight` | **`0.045f`** | `:17` | 反馈字相对判定线的抬高量（沿相机 up） |
| `FeedbackScale` | **`0.09f`** | `:18` | 反馈字缩放 |
| `TrackHalfWidth` | **`0.81f`** | `:19` | 与 `NoteRenderer.TrackHalfWidth` 同值，用于 `TrackX` |
| `BurstPoolPerDirection` | **`4`** | `:20` | 每个方向 4 个 burst 实例（左 4 + 右 4 = 8） |
| `FeedbackPoolSize` | **`8`** | `:21` | 反馈字池 8 个 |
| `EnableExactFeedback` | **`false`** | `:22` | `ExactPlus`/`Exact` **不弹反馈字**（`:695-701`） |

实际 burst 时长来自 Animator clip 长度（`ResolveClipLength`，`:247-262`），`0.5333` 只是 fallback。

### 8.2 触发链

```text
玩家输入 / autoplay
  → JudgementEngine.PressFlick(direction, nowMs)          JudgementEngine.cs:181-227
      · 方向白名单 4/16                                     :183-186
      · 遍历未判定 flick，按 |note.TimeMs − nowMs| 找最近的  :190-213
      · 窗口：delta > BadMs(150) 或 delta < −SkyNearMs(200) 跳过   :203
      · Judgement.Evaluate(delta, NoteZone.Sky)              :221
      · ApplyResult(...) → note.Judged = true                :224 / :351-402
  → 触发 Judged 事件                                        JudgementEngine.cs:397-401
  → FlickHitEffectRenderer.HandleJudged(judgement)           FlickHitEffectRenderer.cs:365-389
      · 过滤：Kind == Flick 且 IsHit(result)                 :367-374
      · AcquireBurst(note.FlickDirection == 4)               :377 → :391-409
      · StartBurst(burst, note.FlickHeadPos, InputTimeMs)    :380-384 → :411-434
      · ShouldShowFeedback(result) → StartFeedback(...)      :385-388 → :436-515
```

`IsHit`（`:686-693`）= `Shiny | PerfectEarly | PerfectLate | NearEarly | NearLate`（即**不含 Break/Miss**）。
判定窗口（`JudgementTypes.cs:26-30`）：`ShinyMs 25 / PerfectMs 50 / NearMs 100 / BadMs 150 / SkyNearMs 200`，
其中 **Sky 区域（Flick 用）的窗口是 `±200 ms`**（`JudgementTypes.cs:35-62`：`>200` → `None`，`>100` → NearEarly，…，`>=−200` → NearLate）。
这比地面轨道的 `±100 ms` 宽一倍 —— **Flick 天生判定更宽容**，调手感时要注意这是刻意设计。

### 8.3 视觉行为

- **Burst**（`:411-434`）：定位到 `TrackX(note.FlickHeadPos)`，重播 `FlickBurstAnim`（`:431` `Animator.Play(BurstStateHash,0,0)`），
  之后每帧只改 `Renderer.color` 的 alpha 为 `pow(1−normalized, 2.0)`（`:572-576`）。
  **暂停时 `animator.speed = 0` 但 alpha 继续按时间推进**（`:577`，详见 §9.1.1）。
  池取用策略：优先找 `!Running` 的空位，没有就**抢占最老的**（`AcquireBurst` `:391-409`），
  所以超密集 flick 不会"丢特效"，只会把最早的顶掉。
- **反馈字**（`:626-655`）：`fadeIn` 用 `SmoothStep(0, 0.035, t)`，`fadeOut` 用 `1 − SmoothStep(0.30, 0.58, t)`，
  缩放脉冲 `lerp(1.08, 1.0, SmoothStep(0, 0.11, t))`，并沿相机 up 方向以 `0.12/s` 上升（`:640`）。
  始终面向相机（`:614-616` / `:505-508`）。

---

## 九、时序：三个容易踩坑的地方

### 9.1 `Tick` 在播放判断之外（`GameplayBootstrap.cs:419-422`）

```csharp
if (_clock.IsPlaying)                              // :398
{
    ... _noteRenderer.Update(now); ...             // :414
}
// 注意：这里已经出了 if (_clock.IsPlaying) 的花括号
if ((Object)(object)_flickEffects != (Object)null) // :419
{
    _flickEffects.Tick(_clock.TimeMs, !_clock.IsPlaying);   // :421
}
```

**含义**：暂停时 `NoteRenderer.Update` 不再跑（音符不动），但 `_flickEffects.Tick` **仍然每帧跑**。
于是 burst 的 alpha 淡出和反馈字的动画在**暂停状态下也会走完**。
这是刻意设计（避免暂停时特效"冻"在屏幕上），但如果你发现"暂停后特效不消失"，先看这里。

对比：`UpdateChartPlaybackFrame`（`:550-580`）里调的是 `Tick(now, paused: false)`（`:569`），
即编辑器拖动播放头时**强制按未暂停**处理。但注意它的外层条件是
`_clock.IsPlaying && _engine != null`（`:553`）——**时钟没在播就整个不跑**，
所以编辑器中"scrub 播放头但不播放"时，特效不推进；这与 `Update()` 的"暂停也 Tick"**行为不同**。
两个入口不一致，是排查"编辑器里特效不消失/不出现"时最容易绕晕的地方。

### 9.1.1 burst 淡出与暂停的真实关系（比文档常写的更细）

`FlickHitEffectRenderer.TickBursts`（`:547-581`）里：

```csharp
var elapsedMs = nowMs - burst.StartedMs;                    // :561  用 nowMs，与 paused 无关
if (elapsedMs >= burst.Duration * 1000.0) { burst.Running = false; ... }   // :562-567
burst.Renderer.color = new Color(1,1,1, Mathf.Pow(1.0f - normalized, BurstFadePower));  // :572-576
burst.Animator.speed = paused ? 0.0f : 1.0f;                // :577  ← 只有 Animator 被暂停
```

即**暂停时序列帧动画冻结，但 alpha 淡出仍在走**。
所以暂停很久再恢复，burst 可能已经"淡没了"——这是刻意设计（避免暂停时特效堆积），
不是漏改。

### 9.2 Autoplay（`GameplayBootstrap.cs:901-937`）

```csharp
// :928-931
else if (note.Kind == NoteKind.Flick && nowMs >= note.TimeMs)
{
    _engine.PressFlick(note.FlickDirection, note.TimeMs);   // 注意传的是 note.TimeMs，不是 nowMs
}
```

两个细节：

1. **传 `note.TimeMs` 而不是 `nowMs`** → 自动演奏时 `delta = 0`，**必定 Shiny**。
2. 条件是 `nowMs >= note.TimeMs`（**晚到才按，不会提前**），配合 `AutoPlayStep` 只在 `_clock.IsPlaying` 时调用（`:401-404`），
   所以跳转/拖拽时不会误触发一大片。

### 9.3 手动键盘输入（`GameplayBootstrap.cs:952-959`）

```csharp
if (Input.GetKeyDown((KeyCode)276))       // :952  ← 键码 276
{
    _engine.PressFlick(16, nowMs);         // :954  黄/左
}
if (Input.GetKeyDown((KeyCode)275))       // :956  ← 键码 275
{
    _engine.PressFlick(4, nowMs);          // :958  绿/右
}
```

`276` = `KeyCode.LeftArrow`，`275` = `KeyCode.RightArrow`。
**左箭头触发 16（黄/左），右箭头触发 4（绿/右）** —— 与方向语义一致。
（源码用数字字面量而非 `KeyCode` 枚举，是反编译产物的痕迹。）

### 9.4 重置链路（编辑器/重开谱面必需）

`_flickEffects` 若不重置，池里的旧实例会带着上一个播放头的特效留在屏幕上。三处调用：

| 位置 | 场景 |
|---|---|
| `GameplayBootstrap.cs:511-514` | `RebuildChartRuntime` → `_flickEffects.SetEngine(_engine)`（换 engine 后重新订阅 `Judged`，`:164-165`） |
| `GameplayBootstrap.cs:618-621` | `EndChartPlayback` → `_flickEffects.ResetVisuals()` |
| `GameplayBootstrap.cs:974-977` | `Restart` 路径（`ResetAutoplayStateAt` `:669-687` 里也有 `SetEngine`） |

`FlickHitEffectRenderer.SetEngine`（`:152-166`）的关键动作：
`_engine.Judged -= HandleJudged;` → 换 engine → `_engine.Judged += HandleJudged;` → `ResetVisuals();`
**先退订再订阅**，否则同一个特效会响应两次判定（炸弹级 bug，历史踩过）。

---

## 十、调参速查 + 故障对照

### 10.1 想要什么效果 → 改哪里

| 想要 | 改什么 | 默认 | 位置 |
|---|---|---|---|
| 条纹更密 | `FlickStripesPerWorldUnit` ↑ | `11.0` | `NoteRenderer.cs:228` |
| 条纹更深/更浅 | `_BarContrast` | `2.6` | `dc.shader:82` |
| 条纹整体更强/关掉 | `_BarStrength` | `0.55` | `dc.shader:91`（0 = 无条纹） |
| 尾巴更亮 | `_TailLift` ↑ | `1.45` | `dc.shader:84` |
| 尾巴更亮（结构性） | `_TailDetint` ↑（往原色回拉） | `0.40` | `dc.shader:89` |
| 尾巴变透 | `_TailOpacity` ↓ | `1.0` | `dc.shader:86` |
| 尾巴更高 | `FlickTailHeightScale` ↑ | `0.93` | `NoteRenderer.cs:231` |
| 尾巴更窄 | `1.62` ↓（`FlickWorldWidth` 的系数） | `1.62` | `NoteRenderer.cs:2679` |
| 尾巴往里塞 / 留缝 | `FlickTailInset` | `0.0` | `NoteRenderer.cs:222` |
| 更发光 | `_GlowPower` ↑（先确认 Bloom 开） | `1.8` | `dc.shader:100` |
| 整体更亮 | `_OverallDim` ↑ | `0.86` | `dc.shader:98` |
| 更艳 / 更灰 | `_SaturationTrim` ↑ / ↓ | `0.72` | `dc.shader:96` |
| burst 更久 | `BurstDurationFallback` ↑（或改 clip 长度） | `0.5333` | `FlickHitEffectRenderer.cs:13` |
| burst 淡出更"拖" | `BurstFadePower` ↓ | `2.0` | `FlickHitEffectRenderer.cs:15` |
| 反馈字更久 | `FeedbackDuration` ↑ | `0.58` | `FlickHitEffectRenderer.cs:16` |
| 判定更宽容 | `SkyNearMs` ↑ | `200` | `JudgementTypes.cs:30` |

### 10.2 症状 → 根因 → 修哪里

| 症状 | 最可能的根因 | 修哪里 |
|---|---|---|
| **完全没有斜纹，尾巴是纯色** | `_Bar` 贴图 wrap 被改成 Clamp，U 重复全被钳成一列 | `FlickNote-Bar.png.meta` / `FlickNoteR-Bar.png.meta` → `wrapU/wrapV = 0`（Repeat） |
| **完全没有斜纹，但 wrap 是对的** | `_Bar` 槽绑到了 `_Base` 的 guid（历史缺陷 1） | `FlickV5 Left.mat` 的 `_Bar` → `FlickNote-Bar.png`（guid `acc55bc2...`） |
| **斜纹是横的/方向不对** | `left/right` 分支的 X 翻转被改（左 `diag(-1,+1,+1)` / 右 `diag(+1,+1,-1)`） | prefab 的 `Plane.localScale`，**不要**去旋转 UV |
| **斜纹角度不是 45°** | `Plane.asset` 网格被改（剪切量 `dx/dy ≠ 1`） | 换回原 `Plane.asset`（guid `cb51b6c7...`） |
| **短 flick 条纹特别密、长 flick 特别稀** | `_BarRepeat` 被固定成常数（没乘 `尾巴世界长度`） | `NoteRenderer.cs:2632-2633` 的 `repeat` 计算 |
| **条数比参考截图多/少** | `FlickStripesPerWorldUnit` 不合适 | `NoteRenderer.cs:228`（不是材质上的 Bar Repeat） |
| **绿色(4) flick 的斜纹明显比黄色(16)淡** | `FlickNoteR-Bar.png` 的 alpha 范围(0.184~0.698)比左版(0.443~0.890)低约 0.2，而 `dc.shader:239/:272` 的规范化常量按左版硬编码 | 见 §4.4（改图 / 提成属性，**别调 `_BarContrast`**——两侧会一起变） |
| **尾巴太暗 / 发虚** | 是**亮度**不是透明度（实测 alpha 一直 0.88~1.0） | 先 `_TailLift` ↑，再 `_TailDetint` ↑ |
| **尾巴发白（不是变灰）** | 用了 `_Desaturate > 0`（往 luma 混会抬高蓝通道） | 把 `_Desaturate` 归 0，改用 `_SaturationTrim` |
| **不发光 / 没有光晕** | Bloom `intensity = 0`；或 `Blend SrcAlpha OneMinusSrcAlpha` 让 HDR 被 a² 吃掉 | `DefaultVolumeProfile.asset` 的 Bloom；`dc.shader:138` |
| **发光很糊，整条尾巴都在发光** | `coreHot` 没做平方（`coreC.a` 直接用） | `dc.shader:316` |
| **尾巴高度跟着宽度变** | Y 缩放里混入了 `widthRatio`（第三轮的错误） | `NoteRenderer.cs:1653`，Y 只能乘 `FlickTailHeightScale` |
| **尾巴比箭头矮一大截** | `FlickTailHeightScale = 0.93`（固有 91.25% 再乘 0.93 = 84.86%） | 想恢复 91.25% 就把 `:231` 改成 `1.0` |
| **尾巴从箭头里透出来 / 看起来太宽** | 尾巴按**全宽**缩放，没减箭头宽度；或 `FlickVertexSeamOffset ≠ 0` | `NoteRenderer.cs:1646`、`:225` |
| **尾巴和箭头之间有缝** | `FlickTailInset > 0` | `NoteRenderer.cs:222` 归 0 |
| **箭头有时被尾巴盖住** | 两者同 renderQueue 3012，透明体同队列顺序不保证 | 把 `Mat.001*.mat` 的 `m_CustomRenderQueue` 改 3014（**Round5 文档说的 3014 并未落地**） |
| **箭头颜色调不动** | 箭头走 URP Unlit，不读 `dc.shader` 的任何参数 | 改 `Mat.001*.mat` 的 `_BaseColor` 或换图集；`dc.shader` 参数与它无关 |
| **改了 JSON/材质但画面不刷新** | `NoteRenderer.cs:2637-2642` 的快速返回没把新字段纳入比较 | 在 `:2637-2642` 的条件里加上该字段 |
| **暂停时特效还在动** | 这是**设计行为**：`_flickEffects.Tick` 在 `if (_clock.IsPlaying)` 之外 | `GameplayBootstrap.cs:419-422`（要改需谨慎） |
| **换播放头/重开后留下残影** | 没调 `ResetVisuals()` | `GameplayBootstrap.cs:618-621` / `:974-977` |
| **判定被响应两次（特效闪两下、计数翻倍）** | `SetEngine` 没先退订 | `FlickHitEffectRenderer.cs:161-165`（必须先 `-=` 再 `+=`） |
| **`flick` 行直接拒载** | 方向不是 4/16；或参数不是恰好 5 个；或 grid ≤ 0；或宽度为负 | `ChartParser.cs:334-364` |
| **编辑器保存后 flick 数值漂移** | `StartCellCount` 丢了，`ChartWriter` 用兜底 24 格重算 | `ChartParser.cs:372`；`ChartWriter.cs:210-228` |

---

## 附录 A：本文核对过的"文档 ↔ 代码"不一致清单

打包/交付时请以**代码**为准。以下 4 条是本轮逐项实测发现的偏差：

| # | 文档说法 | 代码/资产实测 | 影响 |
|---|---|---|---|
| 1 | `dc.shader:16` 头注释：`Cull Back` | `dc.shader:141` 实际 `Cull Off` | 低（薄片需双面），但注释误导 |
| 2 | `docs/Flick_Round5_Fix.md:110`：`FlickTailHeightScale` 默认 `1.0` | `NoteRenderer.cs:231` 实际 **`0.93f`** | **高**：尾巴实际高度 84.86%，不是 91.25% |
| 3 | `docs/Flick_Stripe_Fix.md:79` 说 `_BarRepeat` 默认 5.5；`Round5:74` 说 8.0 | `dc.shader:81` 实际 **`10.0`**，且运行时被 **`尾巴世界长度 × 11.0`** 覆盖 | **高**：调材质 Bar Repeat 无效 |
| 4 | `docs/Flick_Round5_Fix.md:30-39,116`：箭头队列改成 **3014** | 四个材质 `m_CustomRenderQueue` **全为 3012**（HEAD 与工作区一致）；`git grep 3014` 在 `Material/` 零命中，只有 `autobackups/flick_fix/fix_r5.py:21` 这个**未执行的补丁脚本** | **高**："箭头盖住尾巴"未真正修复 |

第 5 条是**本文新发现、历史文档完全没提**的：

| # | 事实 | 证据 | 影响 |
|---|---|---|---|
| 5 | 左右 `_Bar` 的 alpha 范围不同（左 `0.443~0.890`、右 `0.184~0.698`），而 `dc.shader:239`/`:272` 的规范化常量（`0.66` / `0.44` / `0.45`）只匹配左版 | 逐像素统计两张 png；实测调制幅度左 0.248 / 右 0.175 | **中**：右/绿斜纹弱约 29%；调 `_BarContrast` 无法单独修一侧 |

另有两条**不算不一致、但容易误读**的点：

- **`FlickHeadOverlap = 0.08f`（`NoteRenderer.cs:218`）当前未被任何代码使用**——
  它是 `AlignFlickPlane` 改造前遗留的常量。`docs/Flick_Tail_Fix.md:16-22` 描述的
  `position.x += (extendsNegative ? -0.08f : 0.08f)` 那段代码**已经不存在了**。
- **`docs/memory/appendix/detail-rendering.md:62` 的行号已过期**：
  它引用的 `NoteRenderer.cs:1311`（模板选择）、`:1365`（identity 旋转）、`:1414-1415`/`:1485`（Trail 宽度）、
  `:2286`（颜色）在当前 HEAD 里分别是 `:1527`、`:1580`、`:1646`、`:1792`、`:2632`。
  **以本文 §一 §二 的行号为准**。

---

## 附录 B：一页速查卡

```text
flick(t, center, grid, width, dir)
        ↓ ChartParser.cs:332-377
ChartNote{ StartPos=center/grid, StartWidth=width/grid, FlickDirection=dir }
        ↓ ChartModels.cs:73-81  FlickHeadPos = dir==16 ? left : right
        ↓ NoteRenderer.cs:1527  dir==4 ? v5-3R : v5-3
prefab v5-3 / v5-3R
├── Flick  0.232×0.160  Mat.001(.R)  URP Unlit   ← 端帽图集，不走 dc.shader
└── Plane  1.536×0.146  FlickV5 L/R dc.shader    ← 三贴图 + 斜纹 + 发光
        ↓ NoteRenderer.cs:1651-1655   X = tailSpan/1.536, Y = 0.93
        ↓ AlignFlickPlane :1792-1950  顶点级顶到箭头后缘（FlickTailInset=0）
        ↓ NoteRendererPropertyState.cs:72-104
MaterialPropertyBlock{ _Color = 绿(4)/黄(16) 或 (1,.82,.18), _Opacity = fade,
                       _BarRepeat = 尾巴世界长度 × 11.0 }
        ↓ dc.shader:244-246
rgb = (bar.rgb*bar.a + base.rgb*(1-bar.a))*(1-core.a) + core.rgb*core.a
a   = max(base.a, core.a) * _Opacity
        ↓ dc.shader:262-275  尾巴提亮 + 去染色 + 条纹二次调制
        ↓ dc.shader:327-329  premul，然后 +coreHot*(_GlowPower-1)*1.6
        ↓ Blend One OneMinusSrcAlpha  → Bloom(1.5 / 2 / 0.821)

斜纹 45° = _Bar(单条纹, U 重复) × Plane 网格剪切(dx/dy = 1.000)
颜色写死 4=绿/右、16=黄/左，其它值解析期即拒
尾巴高度 = 0.146 × 0.93 = 0.13578（箭头 0.160 的 84.86%）
```
