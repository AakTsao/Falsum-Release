# Flick 斜纹修复报告

## 一、问题

制谱器（InFalsus-Gameplay）里的 Flick 音符**完全没有斜纹**，与原版差异明显。

## 二、根因（三个独立缺陷，全部已修）

### 缺陷 1：`_Bar` 贴图引用错了（最直接的断点）

`FlickV5 Left.mat` 里 `_Bar` 指向了 **`FlickNote-Base`** 的 guid，而不是 `FlickNote-Bar`：

| 槽位 | 修复前 guid | 修复后 guid | 对应贴图 |
|---|---|---|---|
| `_Bar` | `689af15b...` | **`acc55bc2...`** | FlickNote-Bar (64×128) |
| `_Base` | `689af15b...` | 不变 | FlickNote-Base (2048×128) |

> 修复前 `_Bar` 和 `_Base` 都指向同一张 Base 图，`_Bar` 槽根本没拿到那条竖条纹理。
> （`FlickV5 Right.mat` 本来就是对的，只有 Left 错。）

### 缺陷 2：采样 UV 没有做 U 方向的重复

原 shader 里：
```hlsl
float4 barC = SAMPLE_TEXTURE2D(_Bar, sampler_Bar, float2(uv.x, saturate(v)));
```
U 直接用 0..1，**没有任何重复**。而 `_Bar` 是"单条纹"贴图，不重复就只有一条，形不成斜纹带。

原版字节码证实应做重复（`mad r0.xy, r0.xyxx, l(32.0,0,0,0)`，注意 **V 分量的乘数是 0**）。

修复后：
```hlsl
float barU = uv.x * _Scale * _BarRepeat + _Bar_ST.z;   // U 方向重复
float barV = uv.y * _Scale * 0.0        + _Bar_ST.w;   // V 恒为 0（照原版）
```

### 缺陷 3：`_Bar` 贴图 wrap 模式是 Clamp

`FlickNote-Bar.png.meta` / `FlickNoteR-Bar.png.meta` 都是 `wrapU: 1 / wrapV: 1`（Clamp）。
U 一旦重复超过 1，Clamp 会把所有超出部分钳到边缘一列 —— **条纹全部消失，画面变成纯色**。

改为 **Repeat**（`wrapU: 0 / wrapV: 0`）。

## 三、斜纹的几何来源（关键认知）

斜纹的"斜"**不是** UV 旋转，也不是屏幕空间效果，而是：

**Plane 网格本身是剪切平行四边形，而 UV 沿长度方向线性分布。**

实测 `Plane`（mesh 590）顶点：

| 顶点 | x | y | u | v |
|---|---|---|---|---|
| v0 | +0.57422 | +0.18905 | 0.0 | 0.9991 |
| v1 | +0.72022 | +0.04305 | 0.0 | 0.0009 |
| v2 | −0.66978 | +0.04305 | 1.0 | 0.0009 |
| v3 | −0.81578 | +0.18905 | 1.0 | 0.9991 |

- 高度 = **0.1460** 世界单位
- 上边相对下边的 X 位移 = **−0.1460** 世界单位

⇒ 剪切斜率 **dx/dy = 1.000** ⇒ **条纹倾角精确 45°**

这与原版截图实测（ref_B ≈135°、ref_C ≈133–141°，即约 45°）**完全吻合**。
所以几何不需要改，只要把 UV 重复打开，45° 斜纹就会自然出现。

## 四、条纹疏密

`_Bar` 是一张 **64×128 的单条纹单元**（中间亮、两侧渐暗），所以：

**U 的重复次数 = 乐器上可见的条纹条数。**

原版截图（放大 4× 后）实测约 **5–6 条**斜带，而原 asm 里的常量 32 会让它在 0..1 的 UV 上重复 32 次——**密约 6 倍**。

因此新增可直接调的参数：

| 参数 | 默认 | 作用 |
|---|---|---|
| `_BarRepeat` | **5.5** | 斜纹条数（1–32） |
| `_BarContrast` | **2.0** | 斜纹强度。`_Bar` 自身 alpha 只有 0.44–0.89，直接混色太淡，围绕 0.66 放大偏差 |

## 五、改动清单

| 文件 | 改动 |
|---|---|
| `Assets/InFalsus/Recovered/dc.shader` | 片元重写：U 重复 + V=0 + 正确的三图层合成 + `_BarRepeat`/`_BarContrast` 两个新属性与 CBUFFER 声明 |
| `Assets/InFalsus/Recovered/Material/FlickV5 Left.mat` | `_Bar` → `FlickNote-Bar` 的正确 guid |
| `Assets/InFalsus/Recovered/Texture2D/FlickNote-Bar.png.meta` | wrapU/V → 0（Repeat） |
| `Assets/InFalsus/Recovered/Texture2D/FlickNoteR-Bar.png.meta` | wrapU/V → 0（Repeat） |

备份在 `autobackups/flick_fix/`。

## 六、验证

- Unity 编译：**0 error / 0 shader error**（`Diagnostics/UnityFinal_flick.log`）
- 自动化审计（`autobackups/flick_fix/audit.py`）：**12/12 PASS**
- 数值仿真（`autobackups/flick_fix/*.png`）：
  - `bands_rep5.5.png` —— 隔离出的斜纹形态，5–6 条 45° 暗带，与原版一致
  - `zoom_ref_B.png` / `zoom_ref_C.png` —— 原版放大 4× 基准图

## 七、怎么调

1. 打开 `FlickV5 Left.mat` / `FlickV5 Right.mat`，调 **Bar Repeat** 改疏密（默认 5.5）。
2. 调 **Bar Contrast** 改深浅（默认 2.0）。
3. 若某侧方向看起来反了，那是左右分支的 X 翻转（左 `diag(-1,+1,+1)` / 右 `diag(+1,+1,-1)`），不要靠旋转 UV 去凑。

## 八、仍需注意

端帽层（mesh 589 `Flick`，Y 跨度 0.16 比背板 0.146 更大，就是原版那个"钩状"厚端）由 `v5-3.prefab` 的 Transform 层级自动对齐；**不要**在 shader 里试图模拟它，否则会像我中间那版仿真一样错位。
