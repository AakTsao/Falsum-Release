# 第四轮：发光 + 尾巴高度 + 宽度

针对三点反馈：「还是偏暗 / 没有发光感」、「尾巴高度矮了一点」、「宽度偏宽一点」。

## 一、没有发光感 —— 两个叠加的致命缺陷

### 缺陷 A：Bloom 被完全关闭

`Assets/Settings/DefaultVolumeProfile.asset` 里的 Bloom：

| 参数 | 修复前 | 修复后 | 原版游戏真值 |
|---|---|---|---|
| `threshold` | 0.9 | **1.5** | **1.5** |
| `intensity` | **0** ← 关闭 | **2** | **2.0** |
| `scatter` | 0.7 | **0.821** | **0.82099997** |

`intensity = 0` 意味着后处理阶段**不做任何 Bloom 运算**。原版的发光感就来自 intensity 2.0 的 Bloom，而这里一直是关着的。

### 缺陷 B：预乘 alpha 被混合模式二次乘 alpha（更隐蔽）

这个缺陷同时解释了「偏暗」和「不发光」。

片元返回的是**已预乘**的颜色：
```hlsl
float4 premul = IF_Premultiply(float4(rgb, a));   // = (rgb*a, a)
```
`IF_Premultiply` 定义在 `Infalsus_Common.hlsl:127`，`INFALSUS_PREMULTIPLY=1`。

但 Pass 的混合却是：
```hlsl
Blend SrcAlpha OneMinusSrcAlpha      // 错！
```
混合方程展开：
```
dst = srcRGB * srcA + dstRGB * (1 - srcA)
    = (rgb * a) * a + ...            // alpha 乘了两次 → a²
```
`a ≈ 0.9` 时相当于**亮度再掉 19%**，而且 HDR 亮度永远顶不到 Bloom 阈值。

已改为预乘正确的写法：
```hlsl
Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
```
> 这与 shader 自己的注释记录的原版状态一致：`Blend A : One / SrcAlphaSaturate ... 预乘 alpha 透明`。

### 缺陷 C：HDR 亮度被预乘吃掉

即使加了 HDR 提亮，如果写在预乘**之前**，`rgb * a` 会把 >1 的值拉回阈值以下。
已改为在预乘**之后**叠加：
```hlsl
float4 premul = IF_Premultiply(float4(rgb, a));
premul.rgb += coreHot * (_GlowPower - 1.0f) * 1.6f;   // 越过阈值
```

新增参数 **`_GlowPower`**（默认 **2.2**，范围 1–8，1 = 不发光）。

## 二、尾巴高度偏矮

### 根因

原代码**只缩放 `.x`**，`.y` 保持 prefab 原值：
```csharp
new Vector3(baseScale.x * (span/1.54f), baseScale.y, baseScale.z)
//                                            ^^^^^^^^^^^ 没跟着变
```
所以宽度一变，尾巴的**长宽比就丢了**，看起来就是「矮了一截」。

### 修复

改为 X、Y **同比缩放**，保持网格真实比例：
```csharp
visual.FlickPlane.localScale = new Vector3(
    visual.FlickPlaneBaseScale.x * widthRatio,
    visual.FlickPlaneBaseScale.y * widthRatio * FlickTailHeightScale,
    visual.FlickPlaneBaseScale.z);
```

新增 `FlickTailHeightScale`（默认 **1.0** = 保持真实比例；>1 加高，<1 变矮）。

## 三、宽度偏宽一点

### 根因：两个不自洽的魔数

```csharp
span = FlickWidth * 1.62f          // 1.62 来源不明
scale.x = baseScale.x * (span / 1.54f)   // 但网格实长是 1.536
```

- 真正的 `Plane` 网格长度：**1.536**（原版游戏 bundle 与本项目 `Recovered/Mesh/Plane.asset` **完全一致**，已逐顶点核对）
- 代码里的除数：**1.54** → 偏小 **0.26%**
- 再叠加 `1.62` 这个无来源的系数

### 修复

除数改为**从网格实测包围盒动态读取**，不再依赖魔数：
```csharp
Bounds b = planeMeshFilter.sharedMesh.bounds;
meshLen = Mathf.Max(0.0001f, b.size.x);      // 实测 1.536
float widthRatio = span / meshLen;
```

> `1.62` 仍保留（它是 chart 单位 → 世界单位的谱面侧系数，不在本次反馈范围内）。若调完后仍偏宽，就是 `1.62` 这个系数偏大；把它改小即可，因为现在分母已经是真值，乘性误差只剩它这一个来源。

## 四、改动清单

| 文件 | 改动 |
|---|---|
| `Assets/Settings/DefaultVolumeProfile.asset` | Bloom → threshold 1.5 / intensity 2 / scatter 0.821 |
| `Assets/InFalsus/Recovered/dc.shader` | 混合改 `Blend One OneMinusSrcAlpha`；新增 `_GlowPower`；HDR 提亮移到预乘之后 |
| `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs` | 除数改实测网格长度；Y 同比缩放；新增 `FlickTailHeightScale` |

## 五、调参速查

| 想要的效果 | 调什么 | 默认 |
|---|---|---|
| 更强发光 / 光晕更大 | `_GlowPower` ↑ | 2.2 |
| 尾巴更高 | `FlickTailHeightScale` ↑ | 1.0 |
| 尾巴更窄 | `1.62` ↓（`FlickWorldWidth`） | 1.62 |
| 尾巴更亮 | `_TailLift` ↑ | 1.35 |
| 整体更亮 | Bloom `intensity` ↑ | 2.0 |

## 六、验证状态

- 静态审计：**12/12 实质项 PASS**（1 项 FAIL 为我审计脚本里匹配串写错，已人工核实为误报）
- Unity 编译：**未执行** —— 该项目编辑器当前已打开（`Temp/UnityLockfile` 存在，4 个 Unity 进程），batchmode 拿不到锁。未去关闭您的编辑器。
- 编辑器开着会自动热重载，请直接看效果；无刷新按 `Ctrl+R`。
