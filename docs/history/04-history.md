# 04 · 决策历史（18 篇设计文档的浓缩版）

本仓库的 `docs/superpowers/` 只保留 16 篇（`specs/` 14 + `plans/` 2）；**早期文档的完整集合在父目录**
`../docs/`：`superpowers/specs/` 25 篇、`superpowers/plans/` 4 篇，外加
`in_falsus_text_to_spc_encoding.md` 与 `skyarea_event_crossings_2026-10-26.md`。
下面按时间线浓缩「当时决定了什么、现在还有效吗」。

记号：`S` = `docs/superpowers/specs`，`P` = `docs/superpowers/plans`，
`G` = `Assets/InFalsus/Gameplay/Falsum`，`D` = `Diagnostics`。
条目内的裸 `:N` 指该条标题所示文档。

---

## 时间线

```
2026-10-17  Phase A：恢复玩法场景（坐标约定 / 判定矩阵 / 音频明文恢复）
2026-10-18  .falsum 包格式 v1；编辑器运行时雏形
2026-10-19  UI Toolkit 全量重写；命中音密度；音符拾取几何
2026-10-20  BASS 音频迁移；游戏背景显示；菜单工作流；分数拍小节线
2026-10-21  2D 模式（2026-11-12 已移除）；中键精修；偏好设置
2026-10-22  偏好页「难度/状态」语义
2026-10-23  用户工具脚本（Jint）
2026-10-24  Bug 审计 34 条（31 条当日修完）
2026-10-25  外置内容目录（ExternalTools/Backgrounds）；遮挡音符轮选
2026-10-26  AP Combo 显示；TrackEnd 背景（后被 clean-rebuild 取代）
2026-10-27  TrackEnd 干净重建；SkyArea 原版复刻；谱面预览取景
2026-10-28  性能调试窗；频谱占位（**未执行，且被反向执行**）
2026-10-29  时间轴与波形渲染改进（ff48afc，本次未提交工作区的起点）
2026-10-30  UI 风格切换（Studio/Canvas）；用户工具 Note 选择字段
2026-10-31  播放态绑定的 3D 轨道效果（TrackEnd 时间源；外侧墙于 11-01 移除）
```

---

## 逐篇要点

### 1 · S/2026-10-17 Phase A（跨工程，本仓库无副本）｜已实现
- 坐标唯一约定：一切基准在 `MasterScene` 局部空间；中四轨用 `note1..4` 的局部 y +
  `JUDGEMENT_LINE` 的局部 z，侧轨用 `1_LEFT`/`5_RIGHT` 的局部 x,y，Flick 用
  `JUDGEMENT_LINE.y + SkyY`。
- 判定窗口：Lane Shiny ±25 / Perfect ±50 / Near ±100 / Break 更高；Sky 是两倍；
  边界值 `50/100/200` 归内层。
- 音频恢复是**明文副本恢复**，不是通用解密算法（`alamode.ogg` 是 48 kHz stereo 135.670 s）。
- 状态：坐标与判定条仍有效；`repeatMultiplier = 0.1` 的尾段 UV 仍是近似。

### 2 · S/2026-10-18 `.falsum` 包格式 v1（跨工程）｜已实现，仍是规范
- ZIP 容器，`song.json` 必选，`audio.ogg` 必选，`cover.png` 可选，`charts/` 至少一份。
- 上限：`song.json` 1 MiB / 单谱面 16 MiB / `cover.png` 32 MiB / `audio.ogg` 128 MiB。
  > 注：代码里 `MaxManifestBytes` 是 1 MiB，未知条目保留上限也是 16 MiB。
- 难度映射固定 `0→MIN / 1→EVO / 2→ULT / 3→FBD`。
- `songId` 正则 `^[a-z0-9][a-z0-9_-]{0,63}$`。

### 3 · S/2026-10-19 UI Toolkit 重写｜已实现，**尺寸段已过期**
- 交互 UI 全归 UITK；只有「选中轮廓 + 模式平面」保留 uGUI 世界空间，且不参与命中测试。
- PanelSettings 1730×1080 + MatchWidthOrHeight；控件圆角 0。
- 程序侧改值必须走不触发回调的路径（防事件回环）。
- ⚠ 文档里的顶栏/底栏/属性面板尺寸与实际不一致，**以 USS 为准**。

### 4 · S/2026-10-19 命中音密度｜已实现+验证
- 谱面时间差 ≤ 1 ms 的 tap 合并为单一 transient；chord 仍逐个判定与特效。
- 增益 music `.72` / one-shot `.42` / hold-loop `.24`。
- one-shot 池 48 槽，满则抢「最早结束」的槽，**不得丢新击**。
- 限幅硬顶 -1.5 dBFS、lookahead 2 ms、release 120 ms。

### 5 · S/2026-10-19 音符拾取几何｜已实现（边距数值漂移）
- 拾取与描边的**唯一真值**是 `NoteRenderer` 的渲染网格点：同一个屏幕多边形既描边又命中。
- hold 可见性 = 头尾区间与可见轨道相交，头可以离开视野。
- 拾取分层：Flick/SkyArea 在上层（1），Tap/Hold 在下层（0）。
- `FalsumNotePickMask` + `FalsumChartEditor.SelectionMask` 是唯一集成点。

### 6 · S/2026-10-20 BASS 音频迁移（跨工程）｜已实现
- BGM 走 BASS 后端，失败回退托管 `AudioSource`；播放位置用 `dspTime` 在两次 DSP buffer 之间插值。

### 7 · S/2026-10-21 2D 模式（跨工程，28 KB，最详细）｜已实现，但有 3 条遗留缺陷 —— **已移除（2026-11-12）**
- 2D 与 3D 是同一份数据的两套呈现，当时 `FalsumViewMode ∈ {Perspective3D, Orthographic2D}`。
  **2026-11-12 起 2D 视图整体删除**（`Falsum2DWorld.cs` / `Falsum2DProjection.cs` /
  `Editor/Falsum2DCapture.cs` 均已删，`FalsumViewMode` 只剩 `Perspective3D`），此条仅存历史。
- ⚠ 审计遗留（随 2D 模式一起关闭）：F-10（`SnapPlaneCell` 无 2D 分支）、
  F-18（2D SkyArea 用裁剪后 t 采样边缘）、F-20（2D Flick 仍用 `Dictionary<int, FlickVisual>`）。

### 8 · S/2026-10-21 中键精修｜已实现
- 三种模式：Boundary / StartEnd / Position；`FalsumChartEditor.MiddleEdit.cs`。

### 9 · S/2026-10-21 偏好设置（跨工程，22 KB）｜已实现
- `FalsumPreferences` 当前 `CurrentVersion = 4`；键位可自定义；持久化到
  `persistentDataPath/Falsum/preferences.json`（原子替换）。

### 10 · S/2026-10-22 偏好页「难度/状态」｜已实现
- 难度卡：启用 = 等级 + 谱师，禁用 = `--` / `谱面未启用`；点禁用卡走同一切换路径。
- 难度编辑只进 Draft：完成提交、取消丢弃。
- 中键状态块固定 `left: 55%`；快捷键固定四组。

### 11 · S/2026-10-23 用户工具脚本（跨工程，30 KB，**最长的规范**）｜已实现
- Jint 4.17.0 嵌入，每工具每次执行独立 Engine；无 ESM/Promise/定时器/网络/反射。
- 表单字段类型：text / number / integer / time / note / select / checkbox / file。
- 工具产生的谱面修改**整体作为一次撤销记录**提交。
- 文件 API 需声明权限；外部写盘需宿主确认，且不纳入撤销。
- 路径更新（2026-10-25）：工具根目录改为 `<程序目录>/ExternalTools/`。

### 12 · S/2026-10-24 审计 34 条｜31 条已修，3 条暂缓（已于 2026-11-12 关闭）
- 已修的关键项：`songId` 路径穿越（F-01）、ZIP 路径冲突（F-02）、缺 `chart(...)`/NaN（F-03）、
  带 delay 重复写 `bpm`（F-04）。
- 暂缓：F-10 / F-18 / F-20（全是 2D 模式投影/裁剪/视觉缓存）——**随 2D 模式于 2026-11-12 删除而全部关闭**。
- 文档里「22/23/25 项自检」数字彼此不一致——**以 `FalsumSelfCheck.cs` 的 `checks.Add` 清单为准**。

### 13 · S/2026-10-26 AP Combo 显示｜已实现+验证
- 以原版判定事件为最小单位：TAP/FLICK = 1，HOLD = 1 + 中段 + 可选尾，SKYAREA = 中段 + 可选尾。
- `bpmForEvents = bpm >= 249.99 ? bpm * 0.5 : bpm`；`stepMs = 60000 / bpmForEvents / 2`。
- 纯前缀函数：任意时刻全量重算，不存增量，seek 不丢历史。Combo < 2 隐藏。

### 14 · S/2026-10-26 TrackEnd 背景｜**已被取代**
- 首行自述被 10-27 clean-rebuild 取代；`FalsumTrackEndBackground/RendererFeature` 全仓 0 引用。
- 仍可参考：Mask 4 Pass / Blur 2 Pass / 输出 Quad 分量 / RT 1200×800。

### 15 · S/2026-10-27 SkyArea 原版复刻｜已实现+验证
- 5 ms 采样、每行 4 顶点、索引 `0,1,3,3,1,2`、接缝要求首尾差 ≤ 0.0001 ms。
- 纹理必须 `Repeat`（`Clamp` 会拉出长竖条）。
- 判定线辉光：8×90 = 1448 三角、四缓冲轮转、blur 0.0001、权重 `.227027`/`.316216`/`.070270`。

### 16 · S/2026-10-27 TrackEnd 干净重建｜已实现，**两处数值与代码不符**
- Mask 只 3 Pass：白三角 → 黑三角（速度 0.7）→ 白六边形；Donut 无 Mesh/Material/Draw。
- Blur 7 段降/升采样，`near = 0.428571 * r`，`far = r`。
- 底图 `uv + (0, 0.5)` 且必须 `TextureWrapMode.Mirror`（`Repeat` 有横向接缝）。
- ⚠ 文档说「输出 UV y = 0.7、×1.12、六边形半径 1.0」；代码是 `OutputUvMax = 0.5`、
  `HexagonScale = 2.0f`。**以代码为准**。

### 17 · S/2026-10-27 谱面预览取景｜已实现
- 复用 0.24 s SmoothStep，不新增 Tween。
- `targetZoom = 1 / visibleFraction * (1.157 * 0.95)`，1080p ≈ 1.237。
- 只缩放 MasterScene/NotesContainer/ComboRoot 的 X/Y；背景靠 FOV 反向补偿。
- TrackEnd `OutputUvMax = 0.5`（只下半），普通态反推 ≈ 1.0191。

### 18 · S/2026-10-28 性能调试窗｜已实现+验证
- 入口：About 版本标签左键五连点，任一间隔 > 1.2 s 归零。
- 超预算 15% 才判卡顿；关窗必须恢复 `Profiler.enabled` 与 `ProfilerArea.GPU` 并释放 Recorder。
- 120 帧 + P95，窗口每 0.1 s 刷新，未开窗不采样。

### 19 · S/2026-10-28 频谱占位｜**未执行，且被反向执行**
- 文档要求把 Spectrum 按钮做成惰性占位、删除分析/渲染/捕获/Shader。
- 实际情况：4 个实现文件全在，自检仍含 `track_spectrum`，按钮仍抛事件；
  `ff48afc`(10-29) 反而**新增**了波形渲染。
- → 该文档已作废；频谱是**在用功能**。

### 20 · S/2026-10-30 UI 风格切换｜已实现
- `Classic / Studio / Canvas` 三值，非法值归一化 Classic。
- `FalsumUiStyles.Apply` 是唯一入口，先摘后挂；只换样式，不重建编辑器、不换谱面世界颜色。
- 草稿事务：即时预览 → 完成保存 → 取消恢复进页前风格。

### 21 · S/2026-10-30 用户工具 Note 选择字段｜已实现
- 字段契约 `type: "note"`；`min` 必须 ≥ 1，`max >= min`，单选 `max` 必须为 1。
- `ctx.inputs.<key>` 恒为数组 `{key, id, kind, timeMs}`，按确认顺序、不重复。
- 选择期禁拖拽/删除/放置/撤销/播放；`Esc` 取消；**不产生撤销记录**。

### 22 · S/2026-10-31 播放态绑定 3D 效果｜部分验证
- 单一时钟 `PresentationAnimationClock`：Running 时 `+= clamp(unscaledDeltaTime, 0, 0.1)`。
- 游戏与 Falsum 3D 共用 `GameplayClock.IsPlaying`，编辑器不自算相位。
- Mask 时间源 `_TimeOverride >= 0 ? _TimeOverride : _Time.y`。
- 状态：编译 + `GameplayWallSelfCheck` 通过；`FalsumSelfCheck` 被工具描述断言阻断（与本次改动无关）。
- **2026-11-01**：外侧墙特效（`GameplayWallScroller`、`WallMaterial.mat`、
  `4d.shader`、`GameplayWallRectangleTriangles.shader`、
  `GameplayWallSelfCheck` / `GameplayWallCapture`、场景
  `LeftWallTriangles` / `RightWallTriangles`）已整体移除，见
  `05-gaps.md` §G-13。

---

## 五条跨篇主题

1. **发行版保真优先**：视觉项一律以逆向数值为不变量
   （SkyArea 的 2 ms / 0.0001 ms / 1448 三角、TrackEnd 的 `0.428571 * r` 与 Mirror 环绕、
   墙面禁用 0.65 亮度基线）。
2. **两条时间轴必须分离**：chart time 可 seek；presentation time 只受 `IsPlaying` 驱动，
   seek 不跳相；Combo 反过来是 chart time 的纯前缀函数。
3. **编辑事务 fail-closed**：Draft / `BeginChange` → `CancelChange`；先验证后写；失败不留半提交。
4. **自检 + 截图是唯一验收通道**：数值断言 + 1152×720 / 1920×1080 截图 + Windows 构建。
5. **UI 与渲染硬分界**：UITK 管全部输入与布局；3D 谱面世界独立于 UI 风格。

---

## 三条给维护者的结论

- **必须先修**：`S/2026-10-28-spectrum-placeholder` 与代码完全相反；
  `S/2026-10-19-ui-toolkit` 的尺寸段、`S/2026-10-27-trackend-clean-rebuild` 的 0.7/1.0 数值均已过期。
- **可直接引用为真**：命中音密度、拾取几何、TrackEnd 干净重建的 3 Pass / `0.428571 r` / Mirror、
  性能窗、播放态 3D 效果的常量与顺序规则。
- **自检项数的唯一事实源是** `Assets/InFalsus/Editor/FalsumSelfCheck.cs` 的 `checks.Add("…")` 清单
  （当前 28 项）。任何文档里写的 20/22/23/25 都过时了。
