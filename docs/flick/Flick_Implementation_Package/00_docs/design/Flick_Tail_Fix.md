# Flick 尾巴修复：接在箭头后面 + 提亮/透明度

## 一、问题 1：尾巴和箭头重合

### 实测证据（`Diagnostics/MeshDiagnostics.txt`，v5-3.prefab）

| 层 | 世界 X 范围（root 空间） |
|---|---|
| 箭头 `Flick` | **0.005 → 0.235** |
| 尾巴 `Plane` | **0.07 → 1.61** |

尾巴起点 0.07 落在箭头内部（箭头右端到 0.235），**重叠约 0.165 世界单位**。

### 根因

`NoteRenderer.AlignFlickPlane()` 里用硬编码偏移把尾巴端点贴到箭头边缘：

```csharp
position.x += (extendsNegative ? (-0.08f) : 0.08f);
```

这个 `±0.08` 是按旧版/占位箭头尺寸拍的，**没有考虑箭头网格的真实宽度**（实测箭头宽 0.23），所以尾巴被推到了箭头内部。

### 修复

改为从箭头网格**实测 X 范围**推导偏移：

```csharp
Vector3[] headProbe = headFilter.sharedMesh.vertices;
float headMinX = float.MaxValue, headMaxX = float.MinValue;
for (...) { /* 把每个顶点变换到 root 空间，取 X 极值 */ }

position.x -= nearEdge;
position.x += (extendsNegative ? headMinX + FlickTailInset
                               : headMaxX - FlickTailInset);
```

新增常量（`NoteRenderer.cs` 第 189 行）：

```csharp
// 0 = 尾巴正好接在箭头后缘（不重叠）
private const float FlickTailInset = 0.0f;
```

**调参**：
- `FlickTailInset = 0.0` → 严丝合缝接在箭头后面（默认）
- `> 0`（如 `0.01`）→ 尾巴再往里收一点，轻微交叠
- `< 0`（如 `-0.005`）→ 留一条缝

## 二、问题 2：尾巴偏暗、透明度过高

### 实测：不是 alpha 的问题

沿尾巴采样 `_Base` / `_Core` 在形状内部的 alpha：

| u | base alpha | core alpha | 输出 alpha = max() |
|---|---|---|---|
| 0.00 | 0.992 | 0.941 | **0.992** |
| 0.40 | 1.000 | 0.957 | **1.000** |
| 0.65 | 0.973 | 0.698 | **0.973** |
| 0.80 | 0.929 | 0.459 | **0.929** |
| 0.90 | 0.890 | 0.220 | **0.890** |
| 0.97 | 0.882 | 0.055 | **0.882** |

**尾巴的 alpha 一直是 0.88–1.00，几乎不透明。** 所以"透明"是错觉，真实原因是**亮度被压下去了**。

### 真实根因：亮度

用 shader 的实际算式逐 u 计算输出亮度：

| u | sRGB 亮度 | 输出 alpha |
|---|---|---|
| 0.00（箭头） | **0.798** | 0.992 |
| 0.50 | 0.752 | 0.996 |
| 0.80 | 0.646 | 0.929 |
| 0.96 | **0.615** | 0.878 |

箭头 0.80 → 尾巴 0.62，**暗了约 23%**。两个来源：

1. **`_Bar` 混色**：`_Bar` 的 alpha 只有 0.44–0.89，叠在 Base 上面会拉低亮度。
2. **`_Color` 的饱和染色**：项目用 `_Color = (1, 0.82, 0.18)` 从上色。原版游戏是**三套预染色贴图**（黄/绿各一套）直接用，不做这个乘法。`0.18` 的蓝色通道会把蓝分量几乎清零，整体亮度被拉低。

### 修复

shader 新增两个参数，**只作用于尾巴**（用 `uv.x` 做渐变权重，箭头区域不受影响）：

```hlsl
float tailW = saturate((uv.x - 0.12) / 0.45);   // 箭头处为 0，尾部为 1
rgb *= lerp(1.0, _TailLift,    tailW);          // 提亮
a   *= lerp(1.0, _TailOpacity, tailW);          // 只在需要时压 alpha
```

| 参数 | 默认 | 作用 |
|---|---|---|
| **`_TailLift`** | **1.35** | 尾巴提亮倍数（1 = 原样，1.35 = 提亮 35%） |
| **`_TailOpacity`** | **1.0** | 尾巴不透明度（1 = 保持原 alpha，<1 才变透明） |

> 注意 `_TailOpacity` 默认是 **1.0**，因为实测证明尾巴本来就不透明；如果您仍觉得发虚，说明是亮度而非透明度，**优先加 `_TailLift`**。

## 三、改动清单

| 文件 | 改动 |
|---|---|
| `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs` | `AlignFlickPlane` 改用箭头实测 X 范围定位尾巴；新增 `FlickTailInset` 常量 |
| `Assets/InFalsus/Recovered/dc.shader` | 新增 `_TailLift` / `_TailOpacity` 两个属性 + CBUFFER 声明 + 渐变提亮/透明度逻辑 |

## 四、验证状态

- 静态审计（`autobackups/flick_fix/audit_tail.py`）：**12/12 PASS**
- Unity 编译：**未执行** —— 该项目的 Unity 编辑器当前已打开，batchmode 无法获取锁。
  编辑器开着时会自动热重载，请直接在编辑器里查看；若无刷新按 `Ctrl+R`。

## 五、怎么调

1. **尾巴和箭头的衔接**：改 `NoteRenderer.cs` 的 `FlickTailInset`（0 = 刚好接上）。
2. **尾巴亮度**：在材质上改 **Tail Lift**（默认 1.35），先调这个。
3. **尾巴透明度**：改 **Tail Opacity**（默认 1.0，只有确实要变透才往下调）。
4. 若觉得应该从箭头"内部"长出来（而不是紧贴），把 `FlickTailInset` 设正数即可。

## 六、遗留观察（供决策）

原版游戏的黄色/绿色是**两套独立预染色贴图**，而本工程用单一贴图 + `_Color` 染色。
这是尾巴需要"提亮"的结构性原因：`_Color = (1, 0.82, 0.18)` 的乘法本身就是偏暗的。

如果想从根上更接近原版，可选方案是：
- 保留 `_Color`，但把 `_TailLift` 调到 1.4–1.6；或
- 直接分成黄/绿两套材质，`_Color` 设白色不再相乘（更接近原版做法，但要改 `NoteRenderer` 的染色逻辑）。

当前改动选了方案一（最小侵入、可调）。
