# FALSUM 项目库切换卡顿修复 + 全屏加载页设计

日期：2026-11-18。状态：用户已批准（性能只改局部；加载页走「全屏启动序列」）。

## 1. 问题

两个独立问题，同一轮修：

1. **项目库切卡卡顿**。点任意卡片 → `FalsumAndroidProjectLibrary.Select` → `RebuildGrid()`：
   `_grid.Clear()`、`ClearTextures()` 销毁全部贴图、重建所有卡片、对每个项目重新
   `LoadCover(CoverBytes)`（`FalsumAndroidProjectLibrary.cs:154/189/349`）。
   实测封面为 2048×2048 PNG（约 5–6 MB/张），一次点击即重新解码整页封面：
   12 张 ≈ 200 MB RGBA32 + 12 次 PNG 解码，且详情面板 `ShowDetails()` 再解一遍
   （`:250`）。搜索框每次按键同样触发整页重建（`:109`）。
2. **各场景加载页难看**。三个场景（Android 冷启动重定向、进入编辑器、返回项目库）
   共用 `FalsumProjectLibrary.uxml` 里一个 360 px 小框：标题 + 路径 + Unity 默认
   `ProgressBar`，压在 `rgba(1,5,7,0.86)` 蒙层上（`FalsumProjectLibrary.uss:88-91`），
   与项目库本身的工业风深色界面完全不是一个产品。

## 2. 性能修复（只改局部）

改动集中在 `FalsumAndroidProjectLibrary.cs`，不动扫描器的返回契约（后台缩略图缓存
属于下一轮，见 §6）。

### 2.1 选中不再重建整页

- 网格重建时把卡片登记进 `Dictionary<summary, VisualElement>`。
- `Select()` 只做三件事：换 `selected` 类、更新详情、不碰网格。
- 网格重建只发生在：刷新扫描、搜索、排序。

### 2.2 封面缓存（解码一次，降采样到 512）

- `Dictionary<key, Texture2D>`，key = `路径|修改时间Ticks|字节数`；文件变了才重新解码，
  未变则刷新后直接复用（从编辑器返回项目库不再全量重解）。
- 解码后立刻把 `summary.CoverBytes = null` 释放编码副本。
- 解码结果经 `Graphics.Blit` + `ReadPixels` 降采样到 **512×512**（卡片逻辑宽 140 px，
  973 参考宽在 3200 物理宽上约 3.3 倍，512 足够），失败时回退原尺寸并记警告。
- 详情面板复用同一张贴图（不再单独解一次）。
- 刷新时按当前 key 集合裁剪缓存并 `Destroy` 旧贴图。

### 2.3 其它主线程浪费

- 搜索改为 120 ms 防抖（`schedule.Execute(...).StartingIn(120)`），不再每次按键重建。
- `RefreshProjects` 加并发守卫 + 0.75 s 节流，避免 `OnApplicationFocus` 与 `Start`
  同时发起两次全量扫描。
- `ApplySafeArea` 加尺寸/安全区去重，切断 `GeometryChangedEvent → 改 padding → 再排版`
  的重复触发链。

## 3. 加载页：全屏启动序列

### 3.1 结构

新增独立资源 `FalsumLoadingCover.uxml` / `.uss`（不再从项目库 UXML 里抠出蒙层；
`FalsumProjectLibrary.uxml` 里的 `LoadingOverlay` 一并移除），根元素仍叫
`LoadingOverlay`，由 `FalsumSceneNavigation` 克隆到 DontDestroyOnLoad 的独立
`UIDocument`（sortingOrder 10000，参考分辨率固定 973×438，故版式恒定）。

    ┌────────────────────────────────────────────┐
    │ FALSUM PE · EDITOR                   0.46  │  44 px chrome，1 px 底线
    │                                            │
    │   ██ FALSUM（真实字标 PNG，280×89）         │
    │   正在进入编辑器          26 px SemiBold    │
    │   cryogenic.falsum        11 px 字距 1px    │
    │                                            │
    │   ■ 载入游玩场景                            │  三行阶段表：
    │   ■ 载入音频与背景   ← 当前阶段呼吸闪烁      │   pending 空心 / active 实心
    │   □ 构建编辑器                              │   + 文字提亮 / done 灰实心
    │                                            │
    │ ─────────────────────────────────────────  │  2 px 轨，满宽
    │ 32%                        加载期间请勿退出 │  等宽读数 + 右对齐提示
    └────────────────────────────────────────────┘

- 无卡框、无圆角、无阴影；只用 1 px 分隔线与青色（`#b2e5e8` 系）单一强调色。
- 深色底 `rgb(4,9,13)` 与项目库、编辑器同族。

### 3.2 场景 → 阶段映射

| 场景 | 阶段 1 | 阶段 2 | 阶段 3 |
|---|---|---|---|
| 进入编辑器 | 载入游玩场景 | 载入音频与背景 | 构建编辑器 |
| 返回项目库 / 冷启动 | 关闭编辑器 | 读取项目库 | 刷新项目卡 |

进度语义：`FalsumSceneNavigation.SetProgress(percent)` 单调推进，动画协程把显示值
缓动到目标值；未知进度（扫描项目库、等待场景激活）走 indeterminate：轨道填充固定
24 %、一块 90 px 高亮以 `left` 百分比循环扫过、百分比读数留空。

### 3.3 动效

全部在 `FalsumSceneNavigation` 的一个协程里完成，每帧只写 2–3 个 style 属性：

- 入场：`LoadingStage` 与底栏 opacity 0 → 1，0.28 s 三次缓出（背景保持不透明，
  自检要求 `LoadingOverlay` 的 `backgroundColor.a >= 0.999`）。
- 进度：缓动填充宽度（`Length.Percent`）。
- 不确定态：高亮块扫过轨道。
- 当前阶段：行 opacity `0.55–1.0` 呼吸（3.2 rad/s）。
- 退出：无淡出，直接移除（避免场景切换期间再多两帧）。

### 3.4 文案

新增 key（两语言各一份，`{value}` 具名占位符）：

`falsum.loading.eyebrow.editor`、`falsum.loading.eyebrow.library`、
`falsum.loading.stage.scene|audio|world|unload|scan|present`、
`falsum.loading.hint.editor|hint.library`、`falsum.loading.percent`。

标题复用现有 `falsum.library.loading`（进入编辑器）/ `falsum.library.loadingLibrary`
（进入项目库）；失败仍复用 `falsum.library.openFailed`，标题加 `is-error` 类变危险色。

## 4. 必须保持的约束

- `docs/memory/02-invariants.md` §C-1：所有可见文字走 `FalsumLocalization`；
  新 UXML 不写 `text=""` 字面量。
- §C-7：PanelSettings 参考分辨率与**所有控件圆角 0**。
- `FalsumSceneNavigationCheck` 断言：cover 内联背景色 alpha ≥ 0.999、面板
  sortingOrder ≥ 10000、`pickingMode == Position`、`worldBound` 覆盖全视口、
  且编辑器未就绪时不得撤掉 cover —— 全部保留。
- `FalsumSelfCheck` 对项目库 USS 的难度色断言（`#079fd2/#078a72/#8e2b93/#e63260`）
  不受影响。
- 不移动、不改名 `Assets/InFalsus/Recovered/` 下任何资产。

## 5. 验证

1. `Tools/falsum_localization_audit.py`（新增 key 两语言齐平、占位符一致、无硬编码）。
2. Unity 编译检查（batchmode）。
3. `FalsumSelfCheck.Run`（新增 `FalsumLoadingCover` 资源存在断言；原 43 项继续全绿）。
4. `FalsumSceneNavigationCheck`（覆盖帧/初始化帧、毛细血管级断言不变）。
5. 截图：Android 项目库场景 + 加载 cover（借用 `FalsumAndroidLibraryCapture` 的
   RenderTexture 手段，人工核对视口覆盖与两档进度外观）。

## 6. 第二轮：内存峰值（2026-11-18 追加）

首轮修完切卡卡顿后，审计发现两个**与卡顿无关、但更严重**的峰值。用户要求一并修掉。

### 6.1 扫描不再复制封面字节

`FalsumAndroidProjectStore.Scan` 对每个包把完整封面 PNG 读进 `byte[]`
（实测 74 包、平均 5.8 MB、合计 **432.4 MB**），全部进托管堆 LOH，
而字节只在渲染卡片时用一次。

改法：`ReadSummary` 只索引元信息 —— `CoverEntry`（ZIP 条目名）、
`CoverLength`（解压后长度）；新增 `ReadCoverBytes(summary)` 按需重开 ZIP 读回，
保持 32 MB 上限与 `FalsumPackage` 同语义的 PNG 魔数校验，失败返回 null + 警告。
扫描路径的封面分配归零。`CoverBytes` 字段保留（`FalsumPackage` 等消费方在用），
但 summary 不再填它。

自检从「`CoverBytes != null`」改为「条目元信息存在」+「`ReadCoverBytes` 往返
取回、长度等于 `CoverLength`、PNG 魔数正确」两条，证明新路径真的可用。

### 6.2 封面渐进解码

首帧 `RebuildGrid → CreateCard → CoverFor` 对所有可见卡片同步
`LoadImage`（2048² = 16 MB RGBA32）+ `Blit` + `ReadPixels`，
`Destroy(original)` 延迟到帧末 → 74 张时单帧 74×16 MB ≈ **1.18 GB** 同时存活。

改法：建卡只放占位，解码入队，`Update()` 按每帧 1 张预算消费（12 ms 上限
提前收工），队列顺序即优先级；`RebuildGrid` 用代际作废旧队列条目，
已缓存贴图继续复用不重解。单帧峰值降到约 17 MB（1 张原图 + 1 张缩略图）。
详情面板需要立即显示，允许同步解当前选中这一张并写入缓存。

**解码入口改为 `FalsumAndroidProjectStore.ReadCoverBytes`** —— `FalsumAndroidProjectLibrary`
原先读的 `summary.CoverBytes` 在 6.1 之后恒为 null，两处必须一起改，
否则封面全空白（功能回归，不是性能问题）。

### 6.3 仍未做

- `temp/thumbnails` 磁盘缩略图缓存（跨进程冷启动仍需重解首屏那几张）。
- 扫描结果分帧投递到主线程（骨架卡 → 逐批填充）。
- 项目库骨架卡、详情抽屉、PC 端加载页。

## 7. 明确不做（首轮范围）

- 后台缩略图文件缓存 / 扫描分帧落地（`temp/thumbnails`，设计稿 §4 已规划）。
- 项目库骨架卡、详情抽屉、PC 端加载页（PC 不经过项目库场景，只有 Unity 启动画面）。
