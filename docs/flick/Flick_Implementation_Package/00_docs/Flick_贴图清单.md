# Flick 贴图清单

> 本包内所有与 Flick 相关的贴图。**尺寸与 wrap 模式都是从工程里的 `.png` 头部和 `.meta` 实测出来的**，
> 不是抄文档。总大小约 **15.0 MB**（其中 FlickBurst 两张图就占 9.5 MB）。

---

## 一、两张 Bar 图：必须 Repeat

这是本包**唯一一条会导致「斜纹完全消失」**的导入设置。

| 文件 | 尺寸 | 大小 | wrapU/wrapV | 槽位 | 用途 |
|---|---|---|---|---|---|
| `FlickNote-Bar.png` | 64×128 | 3,283 B | **0 / 0 = Repeat** ⚠ | `_Bar` | 左/黄 flick 的单条纹单元 |
| `FlickNoteR-Bar.png` | 64×128 | 4,054 B | **0 / 0 = Repeat** ⚠ | `_Bar` | 右/绿 flick 的单条纹单元 |

**为什么必须是 Repeat：** shader 里 `_Bar` 的 U 坐标是 `uv.x * _Scale * _BarRepeat`，
`_BarRepeat` 默认 10，即一条 64px 的竖纹在 U 上重复 10 次。
如果 wrap 是 **Clamp**，超出 0..1 的部分全部被钳到边缘一列 ——
**10 条条纹会塌成一条，画面变成纯色**。这正是 2026-11-06 修过的缺陷 3。

> `flickv5.png` 和 `notes_track_matid_flicknoteBase_...Orange.png` 也恰好是 Repeat(0/0)，
> 但它们不走 `_Bar` 槽，所以与斜纹无关，属于原样保留的导入设置。

---

## 二、尾巴（Plane 网格）的三张贴图 —— 走 `dc.shader`

材质 `FlickV5 Left.mat` → 左/黄 flick；`FlickV5 Right.mat` → 右/绿 flick。

| 槽位 | 左版（Left.mat） | 右版（Right.mat） | 尺寸 | 大小 | wrap |
|---|---|---|---|---|---|
| `_Base` | `FlickNote-Base.png` | `FlickNoteR-Base.png` | 2048×128 | 196,940 / 211,472 B | 1/1 Clamp |
| `_Bar` | `FlickNote-Bar.png` | `FlickNoteR-Bar.png` | 64×128 | 3,283 / 4,054 B | **0/0 Repeat** |
| `_Core` | `FlickNote-Core.png` | `FlickNoteR-Core.png` | 2048×128 | 85,822 / 55,570 B | 1/1 Clamp |

三张图的分工（见 `dc.shader` 头部注释）：

- **`_Base`** —— 2048×128，横向一条带，**上下两端有渐变、中段近乎全黑（透明）**。提供尾巴的主体形状与 alpha。
- **`_Bar`** —— 64×128，**单条纹单元**（中间亮、两侧渐暗，自身 alpha 只有 0.44–0.89）。负责斜纹。
- **`_Core`** —— 2048×128，中间一条高亮芯线。负责中心亮线 + HDR 发光源 (`coreC.a`)。

> 三张都是"**横向 U 铺满、V 上下分布**"的条状图，所以 **V 方向不能平铺、U 方向直接用**。
> 这与 SkyArea 的 `ARCHITECT-LINES`/`DOT-GRID`（竖长、沿 V 平铺）**恰好相反** ——
> 是本工程里两类音符最硬的形态差异，改动时不要混淆。

---

## 三、箭头 / 端帽（Flick 网格）的贴图 —— 走 URP Unlit

箭头层**不走 `dc.shader`**，用的是 `Recovered/Shader/Universal Render Pipeline_Unlit_1.shader`。

| 文件 | 尺寸 | 大小 | wrap | 用于 | 说明 |
|---|---|---|---|---|---|
| `flickv5.png` | 2048×2048 | 1,679,345 B | 0/0 | `Mat.001.mat` 的 `_BaseMap` | 左/黄 flick 的箭头本体图集 |
| `FlickNoteCapR.png` | 2048×2048 | 1,429,874 B | 1/1 | `Mat.001 R.mat` 的 `_BaseMap` | 右/绿 flick 的箭头端帽图集 |

> 注意 `Mat.001.mat` 与 `Mat.001 R.mat` 的 `_BaseMap` 和 `_MainTex` **指向同一张贴图**（双份引用，是原版材质遗留）。

---

## 四、命中特效 FlickBurst 的贴图

| 文件 | 尺寸 | 大小 | wrap | 说明 |
|---|---|---|---|---|
| `FlickBurst.png` | **4096×2048** | 4,652,960 B | 1/1 | 左/黄方向的 64 帧序列图集 |
| `FlickBurstR.png` | **4096×2048** | 4,874,723 B | 1/1 | 右/绿方向的 64 帧序列图集 |

这两张是**序列帧图集**：4096×2048 平铺成 **64 个 512×512 的帧**（工程里对应
`Assets/InFalsus/Recovered/Sprite/FlickBurst_0..63.asset` 与 `FlickBurstR_0..63.asset`
共 128 个 Sprite 子资产，全部在本包 `04_assets/Sprite/`）。

> 帧是通过 `FlickBurst_0.controller` / `FlickBurst_0 R.overrideController` 驱动的
> 精灵动画播放的，不是 shader 采样图集。改动图集尺寸必须同步重建 128 个 Sprite 的 rect。

---

## 五、仅恢复、未接线的贴图（本包收录以便完整）

| 文件 | 尺寸 | 大小 | wrap | 材质 | 状态 |
|---|---|---|---|---|---|
| `notes_track_matid_flicknoteBase_AlbedoTransparencyOrange.png` | 2048×2048 | 286,788 B | 0/0 | `matid_flicknote.mat`（queue 3003） | 原版命名保留；**在 C# 里搜不到直接引用**，属于未接线的恢复资产 |
| `cursor-flick-indicator-left.png` | 512×512 | 156,191 B | 1/1 | `cursor-flick-indicator-left.mat`（queue 3008） | 同上，**鼠标 flick 指示器**，当前工程未使用 |
| `cursor-flick-indicator-right.png` | 512×512 | 163,835 B | 1/1 | `cursor-flick-indicator-right.mat`（queue 3008） | 同上 |

这三条不是 Flick 音符渲染的必需品，但属于「Flick 相关的全部贴图」，一并收进来供参考 / 未来接线。

---

## 六、两张恢复 mesh（无贴图，但必须跟贴图一起走）

| 文件 | guid | 字节 | 说明 |
|---|---|---|---|
| `Mesh/Plane.asset` | `cb51b6c7bce3f9b478194025ef511b05` | 3,834 B | 尾巴网格，**剪切平行四边形**，1.536 × 0.146 |
| `Mesh/Flick.asset` | `2ba5deb3d2fba2e4f9188d6121b2958a` | 5,832 B | 箭头网格，0.232 × 0.160 |

另有两个**未接线**的恢复 mesh（本包收录）：

| 文件 | guid | 字节 | 状态 |
|---|---|---|---|
| `Mesh/flicknote01.asset` | `5f61dc8722b8efb41ae93c3298064cdd` | 4,510 B | 在 `Assets/**/*.cs` 中搜不到引用 |
| `Mesh/flickunderscore01.asset` | `92bc5e892975b414b8ca81aaf2f8b7c5` | 5,489 B | 同上 |

---

## 七、贴图 → 材质 → shader 全链路速查

```text
FlickNote-Bar.png  ─┐
FlickNote-Base.png ─┼─→ FlickV5 Left.mat  ─→ dc.shader ("InFalsus/Replica/FlickV5")  ─┐
FlickNote-Core.png ─┘                                                                  ├─→ v5-3.prefab
flickv5.png ─────────→ Mat.001.mat ─→ URP Unlit_1.shader                               ─┘   （左/黄）

FlickNoteR-Bar.png  ─┐
FlickNoteR-Base.png ─┼─→ FlickV5 Right.mat ─→ dc.shader                               ─┐
FlickNoteR-Core.png ─┘                                                                 ├─→ v5-3R.prefab
FlickNoteCapR.png ───→ Mat.001 R.mat ─→ URP Unlit_1.shader                            ─┘   （右/绿）

FlickBurst.png  ─→ Sprite/FlickBurst_0..63  ─→ FlickBurst_0.controller  ─┐
FlickBurstR.png ─→ Sprite/FlickBurstR_0..63 ─→ FlickBurst_0 R.override  ─┴─→ FlickHitEffectRenderer（运行时克隆）
```

---

## 八、自检

```powershell
$pkg = 'Flick_Implementation_Package'

# 1) 两张 Bar 图必须是 Repeat（7089 = 0x1B91 是 wrap 字段附近，更稳的是直接读 .meta）
Get-Content "$pkg\04_assets\Texture2D\FlickNote-Bar.png.meta"  | Select-String 'wrapU|wrapV'
Get-Content "$pkg\04_assets\Texture2D\FlickNoteR-Bar.png.meta" | Select-String 'wrapU|wrapV'
# 期望两行都是 : 0

# 2) Sprite 必须是 128 个 .asset + 128 个 .meta
(Get-ChildItem "$pkg\04_assets\Sprite" -Filter *.asset).Count   # 128
(Get-ChildItem "$pkg\04_assets\Sprite" -Filter *.meta).Count    # 128

# 3) 贴图必须是 13 张 png + 13 个 meta
(Get-ChildItem "$pkg\04_assets\Texture2D" -Filter *.png).Count  # 13
```
