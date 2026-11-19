# Falsum 编辑器选择框锚点设计（重建版）

> 本文取代 2026-11-05 的初版。初版的「已修正的实现缺陷」逐轮打补丁，
> 每一轮都在锚点层里再写一套编辑数学，直到从根上重建。本文只描述重建后的
> **最终形态**，不保留过程版本。

## 问题

初版留给编辑器的是**两套独立实现的编辑数学**，这是所有具体 bug 的共同根因。

1. **编辑数学两套并存、各自漂移。**
   中键精修（`FalsumChartEditor.MiddleEdit.cs`）早有一份经过长期使用的
   `ApplyGroundBoundary` / `ApplySkyBoundary` / `ApplyStartEndEdit` /
   `ApplyPositionEdit` / `SnapNormalizedDelta` / `ResolveMiddleEndpointGrid`。
   锚点层（`FalsumChartEditor.AnchorEdit.cs`）另写了一份
   `ApplyGroundAnchorLateral` / `ApplySkyAreaAnchorLateral` /
   `ApplyFlickAnchorLateral` / `ApplyAnchorTime` / `AnchorMinDurationMs`。
   两份代码处理同一件事，取整、夹取、宽度下限、跨边（crossed）方向全都
   各写各的，于是**同一手拖动经两个通道会得到不同结果**。更糟的是锚点层那份
   还是错的：Flick 的边界拖动改成了整体平移（抓边线把整个 Flick 推走）、
   Hold 能缩到 1 ms（沿用了中键的 `MiddleMinimumDurationMs`）。

2. **四角由轮廓形状反推。**
   锚点位置不从音符数据来，而从**画出来的多边形**反推。前后/左右的判定靠
   「长边对 vs 短边对」，在不同宽高比下会翻转：宽短盒子里时间锚点落到左右边，
   窄长盒子里又翻回去。这是「紫色的时间锚点跑到左右边、蓝色的缓动锚点跑到中间」
   的直接原因。先后三版解析器（固定顶点下标 → 包围盒 → 面积最大四元组）
   全都在猜同一个东西，全错。教训：**几何形状不能承载语义**。

3. **绘制尺寸与命中尺寸混为一谈。**
   Overlay 用 `HitRadius * 0.62` 计算绘制大小，而 `HitRadius` 下限 24 px，
   于是画出来的手柄直径近 30 px。一个字段兼两种用途，任何一次「调大/调小」
   都会同时破坏两件事：用户先说太大，改小后又说太小。

4. **圆形手柄套方形边。**
   手柄按形状画成圆点，外圈却统一描axis-aligned方框，视觉上就是
   「圆 glyph 外面套一个方 rim」，手柄看起来是方的、边缘还发毛。

5. **轴锁是猜测。**
   首帧按位移分量猜一个轴，猜错一次整个拖动的语义就错了。用户明确表达过
   「Flick 就当中键位置模式来移动」「skyarea 的前后边线移动把前后边当 flick 处理」，
   而实现却在猜。

## 设计

### 角色 → 操作表

**每个锚点角色只对应一个操作**，在按下时由 `(note.Kind, anchor.Kind, Edge, Tail)`
解析一次（`ResolveAnchorOperation`），拖动过程中不再改变。
唯一例外是 skyarea 的前/后边线中点，见下节。

| 音符 | 锚点角色（`FalsumAnchorKind`） | 位置 | 对应操作（委托目标） |
|---|---|---|---|
| Tap | Boundary ×2（左/右） | 前端的两个角 | `ApplyBoundaryEdit` → `ApplyGroundBoundary` |
| Tap | StartEnd ×1 | 前端中点 | `ApplyStartEndEdit` |
| Hold | Boundary ×2 + StartEnd ×1 | 前端 | 同上 |
| Hold | Boundary ×2 + StartEnd ×1 | 后端 | 同上（`Tail = true`） |
| Flick | Boundary ×2 | 两侧边线中点 | `ApplyBoundaryEdit` → `ApplySkyBoundary` |
| Flick | Color ×1 | 前边线外侧 | 点击翻转 `4 ↔ 16` |
| Flick | Move ×1 | 后边线外侧 | `ApplyPositionEdit` |
| SkyArea | Boundary ×2 | 每端两个角（两端共 4） | `ApplyBoundaryEdit` → `ApplySkyBoundary` |
| SkyArea | StartEnd ×1 | 每端中点（共 2） | 前后 → `ApplyStartEndEdit`；左右 → **`ApplyAnchorEndpointLateral`** |
| SkyArea | Easing ×2 | 可视盒左右边线中点 | 点击循环 `Line → Ease out → Ease in` |
| SkyArea | Move ×1 | 可视盒前边线下方 | `ApplyPositionEdit` |

`ApplyBoundaryEdit` / `ApplyStartEndEdit` / `ApplyPositionEdit` 都是
`MiddleEdit.cs` 里接受 `MiddleEditState` 的共享主体；中键自己的入口是
`ApplyMiddleStartEndEdit` / `ApplyMiddlePositionEdit` 之类的薄包装，二者调的是同一份代码。

锚点总数（两端都可见时）：

| Tap | Hold | Flick | SkyArea |
|---|---|---|---|
| 3 | 6 | 4 | 9 |

SkyArea 的前/后端边界与 StartEnd 中点**只在该端点仍在可视轨道内时生成**
（`IsEndpointVisible`，与渲染层同一判定）。端点被裁掉时少了 3 个锚点，
两端都不可见时只剩 2 个缓动 + 1 个移动手柄 = 3。

### 唯一的特例：skyarea 端点横向移动

`ApplyAnchorEndpointLateral(state, normalizedDelta)` 是**整个锚点层唯一的自研操作**，
因为中键的三个模式里确实没有对应物（Boundary 改边界、Position 平移整件）。

契约：

- 由 `state.TimeSide` 选定**一个**端点：`Head` 写 `note.StartPos`，
  `Tail` 写 `note.EndPos`，另一个端点完全不动；
- 该端点的宽度**原样回写**按下时捕获的值（`PressStartWidth` / `PressEndWidth`），
  即宽度保持不变 —— 与中键 Position 模式「保持形状」的语义一致；
- **不写** `TimeMs` / `DurationMs` / `FlickDirection` / `LeftEasing` /
  `RightEasing` / `Kind`；
- 越界处理只夹**中心**（`minimumCenter` / `maximumCenter`），
  因为夹边界就等于改宽度，而这条操作的定义是宽度不变。

用户原话是「skyarea 的前后边线移动把前后边当 flick 处理」，即等同于
「编辑一个 flick 的位置」。

### 共享投影

四角位置**只从一个投影来**：`FalsumEditorWorld.TryGetMiddleHandleScreenPosition(note, tail, right, nowMs)`。
这是中键精修长期使用的投影，按音符的起止时间与 lane / 归一化位置直接算出角点。

调用链：`ResolveBoxCorners` → `TryProjectCorner` → `TryGetMiddleHandleScreenPosition`
→ `TryScreenToCanvas`。锚点层没有第二条求角路径。

投影之上只有两件事被叠加，且都不是重新求角：

1. **可见性按单个端点 + 屏幕空间**判定（`IsEndpointOnScreen`）：该端点的投影
   是否落在屏幕内。一个端点出屏只掉它自己的角点对与时间中点，另一端与
   缓动 / 移动手柄不受影响。
2. **吸附到实际轮廓顶点**（`MatchCornerToBox`，窗口内取最近顶点；超出窗口则沿
   投影方向落到轮廓上，`BoundaryPointAlongDirection`）。投影只决定**角色**，
   绘制多边形决定**位置**。

### 缓动锚点位置

缓动锚点必须在音符的**进度参数空间**里解析求出（`SkyAreaSideMidpoint`）：

1. 由 `NoteZAt(TimeMs)` / `NoteZAt(EndTimeMs)` 求出可见 z 区间
   （轨道窗口 `[0, TrackLength]`），换算回 0..1 进度区间；
2. 取区间中点 `midProgress`，按 `SkyAreaGeometry.Ease` 求该侧坐标；
3. 投影到画布空间。

旧做法「取多边形外侧三分之一顶点求平均」（`LeftEdgeMidpoint` / `RightEdgeMidpoint` /
`EdgeMidpointByExtreme`，已删除）有两个致命问题：顶点跨过带宽阈值的帧会让平均值
**离散跳变**（表现就是缓动锚点上下乱飘）；后退透视条带的 X 极值在远端，
那个「边中点」根本不在侧边中部。判据只有一条：**位置必须是相机与音符数据的连续函数**。

### 缓动锚点的交互：可拖动 + 可点击

缓动锚点同时支持两种手势，二者都不丢失：

| 手势 | 行为 |
|---|---|
| 按下后**没有**越过吸附档位就松开 | 点击：`Line(0) → Ease out(1) → Ease in(2) → Line(0)` 循环 |
| 按下后沿锚点自身轴拖动 | 按吸附档位改变值；每移动一个 `SnapSpacing` 走一档 |

**几何只有一份**：三档位置由 `FalsumSelectionAnchor` 自己持有
（`SnapCenter` / `SnapDirection` / `SnapSpacing` + `EasingSlot`），构建器用它绘制，
拖动用它吸附。拖动若再自己算一遍间距，两套公式漂移后手柄就会跳到它从未被画过的档位。

**档位是沿 `SnapDirection` 量的，而 `SnapDirection` 指向「向内」**，
所以档位的符号就是手势本身：

| 手势 | 档位 | 值（`LeftEasing`/`RightEasing`） |
|---|---|---|
| 向内（朝音符本体） | `+1` | `1` = Ease out |
| 不动 | `0` | `0` = Line |
| 向外 | `-1` | `2` = Ease in |

**值 ↔ 档位不是恒等**，这是实现时最容易踩的坑：值 `0/1/2` 对应档位 `0/+1/-1`。

所以逆映射必须用 `EasingSlotToValue`，**绝不能写成 `slot + 1`**
（那会把 Ease out 变成 Line）。`EasingValueToSlot` 提供正向反查，
自检对三个值做往返断言。

**符号错一次就是「拖一个方向、改另一个方向」**。本轮实际发生过一次：
`EasingSlot` 的符号取反后，手柄画在向外侧、拖动向内却得到 Ease in。
编译器与文本 grep 都抓不到，所以离线门把「哪个分支返回 `+1` / `-1`」逐条解析断言，
并对把两分支符号对调的变异做过实测（必须失败）。

拖动是**相对手势**：以按下时的值 `PressEasingSlot` 为基准加上档位偏移，
而不是把指针的绝对位置当值。否则从某个档位抓起手柄会立刻把值跳到指针处。
偏移量在同一个档位内不重复写入（`PressEasingSlots` 去重），
这样点击时手抖不会误翻档位。

这样锚点与中键手柄「哪个角是前/后/左/右」的分歧在结构上不可能发生：
它们调的是同一个函数，用的是同一组 `(tail, right)` 参数。

Flick 与 Tap 只有一个时间，投影按单端处理：`tail: true` 会把同一瞬间投两次，
所以后端沿用前端（与中键对零长度边的处理一致）。

### 2D 正交视图排除

`TryBeginAnchorDrag` 在 `FalsumViewMode.Orthographic2D` 下直接返回 `false`。
2D 的投影是另一个空间，把两套命中混在一起是 bug 来源；2D 保持只读轮廓，不画锚点。

### 绘制与命中分离

| 字段 | 用途 | 来源 |
|---|---|---|
| `GlyphRadius` | **绘制**半径 | `AnchorSizing`：`glyph * AnchorGlyphRadiusScale`，夹在 `AnchorMinGlyph`..`AnchorMaxGlyph` |
| `HitRadius` | **命中**半径 | `Mathf.Max(AnchorMinHitRadius, glyph * AnchorHitRadiusScale)` |
| `GlyphScale` | 小盒子的绘制补偿 | 固定 1.0 |
| `Priority` | 命中优先级 | 点击类（Color/Easing）5 > Move 4 > StartEnd 2 > Boundary 0 |

Overlay 只从 `GlyphRadius` 取尺寸，**从不读 `HitRadius`**。
手柄尺寸按盒体**短边**缩放（`AnchorGlyphSizeRatio`），
否则超长细条带会画出比自己还宽的方块。

### 角色轮廓

每个角色一种闭合轮廓（`PlateFor`），边缘沿轮廓描（`AddSilhouette`），
不再统一套方框：

| 轮廓 | 角色 |
|---|---|
| Square（倒角方） | Boundary 角点 |
| Bar（窄竖条） | Boundary 侧边中点 |
| Diamond（菱形） | StartEnd（时间） |
| Circle | Move / Color / Easing（靠内部 glyph 区分） |

角色 glyph：时间=双横条、移动=四向箭头、颜色=方向条（横=绿右 / 竖=黄左）、缓动=位置随值的横条。
所有手柄另有柔光晕（`AddHalo`），保证在花哨音符贴图上仍可见。

### 交互路由

1. Select 模式下左键按下**先**做锚点命中（`FindAnchorAt`，每锚点自带 `HitRadius`，
   取最大者按 `Priority` 决胜），命中即吞掉这次输入，不再拾取/框选/径向菜单；
2. 拖动走 `_session.BeginChange` / `CommitChange`，一次拖动正好一条撤销记录，
   与中键一致；`CancelAnchorDrag` 走 `CancelChange`；
3. 只有**恰好选中一个**音符时才有锚点（`selected.Count == 1`）；
4. 悬停用 1.45 倍命中半径（`AnchorHoverRadiusScale`）预先点亮手柄；
5. 每次拖动后 `RefreshAnchorDragVisuals` 重建框与锚点，手柄跟手。

## 不变量

1. **锚点层不得自研夹取 / 吸附 / 边界数学。**
   所有编辑必须走 `MiddleEdit` 的那份实现。唯一例外是
   `ApplyAnchorEndpointLateral`（skyarea 端点横向移动），见 `02-invariants.md` A-18。
   一旦锚点层出现第二个 `ClampGroundBoundary` 式的本地实现，两套数学就会再次漂移。

2. **四角只有一个来源：`TryGetMiddleHandleScreenPosition`。**
   任何「从多边形反推四角」的代码（固定下标 / 包围盒 / 面积最大 / 最近解析角）
   都不得复活。形状不能承载语义。

3. **前后由音符的两个时间决定，不由轮廓宽高比决定。**
   前端 = `TimeMs` 那一端，后端 = `EndTimeMs` 那一端。

4. **一个锚点一个操作，按下时定死。**
   除 skyarea 前/后边线中点外，任何锚点的操作都不得随拖动方向变化。

5. **skyarea 端点横向移动只写那一个端点的位置字段，宽度原样保留。**

6. **`GlyphRadius` 管绘制、`HitRadius` 管命中。**
   Overlay 不得从命中半径推导绘制尺寸。

7. **锚点只在单选中存在，2D 正交视图不画。**

8. **锚点总数是规格值**：Tap 3 / Hold 6 / Flick 4 / SkyArea 9（两端可见）；
   SkyArea 每个不可见端减 3。

## 验收

| 通道 | 命令 | 断言 |
|---|---|---|
| 编译门（Roslyn，免 Unity） | `python Tools/falsum_compile_check.py` | `compiler errors: 0` |
| 编辑器自检 | `FalsumSelfCheck.Run` 的 `selection_anchors` 项 | 各类型锚点数、角色→操作唯一性、skyarea 端点横向只动一个端点、缓动三态、Flick 4↔16、中键实现存在且旧的锚点自研方法已消失（反射 `EditorDeclares`） |
| 锚点离线检查 | `python Tools/anchor_offline_check.py` | 委托调用存在、旧方法不存在、四角走共享投影、绘制/命中半径分离、本地化键齐全、单选取与 2D 排除 |
| 四角角色检查 | `python Tools/quad_roles_check.py` | 每个角色落在唯一角点；前后按时间而非宽高比；并用旧规则做反面对照，确认该检查确实能抓到老 bug |
| 本地化审计 | `python Tools/falsum_localization_audit.py` | `Falsum localization audit passed.` |

Unity batchmode 在本沙箱不可用（写 `ProjectSettings/~UnityDirMonSyncFile~` 与包重注册被拒），
所以编译与逻辑门都由上表的离线通道代偿。这些通道之外没有别的验收方式。

## 非目标

- 不改中键精修、音符渲染与 `.falsum` 包格式；
- 不给 2D 视图加锚点；
- 不新增偏好项或键位；
- 不引入锚点专用的键盘微调（键盘路径走既有属性面板/快捷键）；
- 不把 `quad_resolver_check.py` 那类「多边形顶点选四角」的验证器保留下来 ——
  对应的 `ResolveQuadFromPolygon` 已删除，它测的代码不存在了。
