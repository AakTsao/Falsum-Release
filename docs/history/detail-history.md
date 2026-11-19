# FALSUM 决策日志（附录：18 篇设计文档逐条核对）

> `S` = `docs/superpowers/specs`，`P` = `docs/superpowers/plans`（部分早期文档只在父目录
> `../docs/superpowers/`），`G` = `Assets/InFalsus/Gameplay/Falsum`，`D` = `Diagnostics`。
> 条目内的裸 `:N` 指该条标题所示文档；代码引用写全名。
> 正文版（按时间线重排、含失效标记）见 [`../04-history.md`](../04-history.md)。

## 逐篇

**1｜S/2026-10-19-gameplay-hit-audio-density-design.md｜设计规范｜治理 `G/Presentation/Audio/*`、AudioManager.asset**
- 谱面时间差 ≤1ms 的 tap 合并为单一 transient，chord 仍逐个判定与特效（`:13-14`；`GameplayHitAudio.cs:13`）
- 增益固定 music .72 / one-shot .42 / hold-loop .24（`GameplayBootstrap.cs:193`、`GameplayHitAudio.cs:36-37`）；real voice 32→64（`ProjectSettings/AudioManager.asset`）
- one-shot 池 48 槽，满则抢“结束时间最早”槽，不得丢新击（`GameplayHitAudio.cs:40`）；限幅硬顶 -1.5dBFS、lookahead 2ms、release 120ms（`GameplayMasterLimiter.cs:9-11`）
状态：实现+验证（`GameplaySelfCheck.cs:560` 断言 49/1 抢占）。

**2｜S/2026-10-19-falsum-editor-note-selection-geometry-design.md｜设计规范｜治理 `G/Runtime/FalsumEditorWorld.cs`、`Falsum2DWorld.cs`（已于 2026-11-12 删除）、`FalsumSelectionOverlayGraphic`**
- 拾取与描边唯一真值 = NoteRenderer 渲染网格点，同一屏幕多边形既描边又命中（`:18-24`）
- hold 可见性 = 头尾区间与可见轨道相交，头可离开视野（`:20-22`）
- 拾取分层：Flick/SkyArea=1 上层，Tap/Hold=0（`FalsumEditorWorld.cs:670-676`）；`FalsumNotePickMask` + `FalsumChartEditor.SelectionMask` 是唯一集成点（`FalsumEditorWorld.cs:16-25`、`FalsumChartEditor.cs:1229`）
状态：已实现；边距数值漂移——文档 4-6 屏幕像素（`:25-27`），代码 `clamp(h*0.0025,3,7)/canvasScale`（`FalsumEditorWorld.cs:684-693`）。

**3｜S/2026-10-19-falsum-ui-toolkit-rewrite-design.md｜设计规范｜治理 `G/UI/*`、`Resources/Falsum/UI/*`**
- 交互 UI 全归 UITK；仅“选中轮廓 + 模式平面”保留 uGUI 世界空间 Canvas 且不参与命中（`:56-63,139-142,180-181`）
- PanelSettings 1730×1080 + MatchWidthOrHeight（`FalsumPanelSettings.asset:21-29`）；所有控件圆角 0（`FalsumEditor.uss:99`）
- 程序侧改值必须走不触发回调路径（`:123-125`）
状态：主体已实现，尺寸段已过期——顶栏 56、底栏 64、属性面板 320（`FalsumEditor.uss:143,151,693`）；文档自称 56/68（`:89,102`）、72/84（`:154`）、358（`:119`），三处互相矛盾且与代码不符。

**4｜S/2026-10-22-falsum-editor-preferences-mode-status-design.md｜设计规范｜治理 `UI/FalsumEditorView.cs`、`FalsumPreferencesView.cs`、`FalsumChartMetadataDraft`**
- 难度卡：启用=等级+谱师，禁用=`--`/`谱面未启用`；点禁用卡走同一切换路径且首选项不关（`:16-22`）
- 难度编辑只进 Draft：完成提交、取消丢弃（`:24-25`）
- 中键状态块固定 left:55%（`FalsumEditor.uss:234-236`、`FalsumEditorView.cs:1852`）；快捷键固定四组（`:46-54`）
状态：已实现，无结果节。

**5｜S/2026-10-26-falsum-auto-combo-display-design.md｜设计规范｜治理 `G/Runtime/FalsumAutoCombo.cs`、`FalsumComboDisplay.cs`**
- `bpmForEvents = bpm>=249.99 ? bpm*0.5 : bpm`；`stepMs=60000/bpmForEvents/2`，取音符起点 BPM（`FalsumAutoCombo.cs:87`、`:23-25`）
- `HOLD=1+interior+tail`，`SKYAREA=interior+tail`，`tail = D-interior*stepMs>=2ms ?1:0`（`:29-35`）
- 纯前缀函数：任意 T 全量重算，不存增量，seek 不丢历史（`:37-38`）
- 显示常量字宽 0.03、填充 α0.10、描边 0.45px、底光 0.30/边缘 0.18、Combo<2 隐藏（`FalsumComboDisplay.cs:16-22`）
状态：实现+验证（`:102-113`）；其中“25 项自检”数字已过时。

**6｜S/2026-10-26-falsum-track-end-background-design.md｜设计规范——已废弃**
- 首行自述被 10-27 clean-rebuild 取代（`:3-4`）；`FalsumTrackEndBackground/RendererFeature` 全仓 0 引用
- 仍有史价值的分工：Mask 4 Pass、Blur 2 Pass、输出 Quad 0.50/0.40/0.23/0.80/0.82、RT 1200×800=clamp(px/3840,0.75,1)（`:47-58,150-154,188-195`）
状态：取代，不可再作为实现依据。

**7｜S/2026-10-27-falsum-chart-preview-framing-design.md｜设计规范｜治理 `UI/FalsumEditorView.cs`、`FalsumTrackEndComposer.cs`**
- 复用 0.24s SmoothStep，不新增 Tween（`FalsumEditorView.cs:65`、`:18`）
- `targetZoom = 1/visibleFraction*(1.157*0.95)`（`FalsumEditorView.cs:67,495-497`）→1080p≈1.237（`:50-51`）
- 只缩放 MasterScene/NotesContainer/ComboRoot 的 X/Y；背景靠 FOV 反向补偿（`:53-75`）
- TrackEnd `OutputUvMax=0.5`（仅下半）+ 普通态反推 1.12/(1.157*0.95)≈1.0191（`FalsumTrackEndComposer.cs:41-52`）
状态：实现（`:3`；`§7.1` 版本验证）。

**8｜S/2026-10-27-falsum-original-skyarea-replication.md｜设计规范｜治理 `Recovered/b3.shader`、`a7.shader`、`SkyAreaGeometry.cs`、`SkyJudgementLineEffect.cs`、`G/Core/FalsumSkyAreaGrouping.cs`**
- 按源文件顺序且只与前一段比较，时间隙 ≤2ms 才成组（`FalsumSkyAreaGrouping.cs:9,99-100`）
- 接缝要求首尾差 ≤0.0001ms——2ms 容差不构成接缝（`SkyAreaGeometry.cs:10`、`:58-60`）
- 5ms 采样 `rowCount=floor(dur/5)+1`，每行 4 顶点、索引 `0,1,3,3,1,2`（`SkyAreaGeometry.cs:9`、`:46-49`）
- 纹必须 Repeat（Clamp 会拉出长竖条）（`:80-82`）；判定线 8×90=1448 三角、四缓冲轮转、blur 0.0001、权重 .227027/.316216/.070270（`:110-132`）
状态：实现+验证；工作区另有未提交 3 行 `_Color.a=0.5` 记录（`:83-85`）。

**9｜S/2026-10-27-falsum-trackend-clean-rebuild-design.md｜设计规范｜治理 `FalsumTrackEndComposer` + Feature/Installer + 3 个 Diamond shader**
- Mask 只 3 Pass：白三角→黑三角（速度系数 0.7）→白六边形；Donut 无 Mesh/Material/Draw（`:54-71`；shader 实为 3 Pass）
- Blur 7 段降/升采样，`near=0.428571*r, far=r`，禁用旧 0.593407/1.384616（`FalsumTrackEndDiamondBlur.shader:50,62`）
- 描边只外侧淡出 `smoothstep(0.02,0.34)*(1-smoothstep(0.46,0.72))*0.055`（`:110-116`）
- 底图 `uv+(0,0.5)` 且必 `TextureWrapMode.Mirror`（Repeat 有横向接缝）（`FalsumTrackEndComposer.cs:415`）
状态：实现（`§10`），但两处与代码冲突：文档“输出 UV y=0.7、×1.12、六边形半径 1.0”（`:127,135,70`）——代码为 `OutputUvMax=0.5`（`FalsumTrackEndComposer.cs:52`，ff48afc 改）且 `HexagonScale=2.0f`（`FalsumTrackEndComposer.cs:31`）。

**10｜S/2026-10-28-falsum-performance-debug-window-design.md｜设计规范｜治理 `UI/FalsumPerformanceWindow.cs`、`FalsumPerformanceMonitor`**
- 入口 About 版本标签左键五连点，任一间隔 >1.2s 归零（`:19-26`）
- 超预算 15% 才判卡顿，顺序 轨道→编辑刷新→UI 同步→UITK→GPU→渲染线程→主线程→VSync（`:88-100`）
- 关窗必须恢复 `Profiler.enabled` 与 `ProfilerArea.GPU` 原状态并释放 3 个 Recorder（`:71-81`）；`enableFrameTimingStats` 必开（`:83`）
- 120 帧 + P95，窗口每 0.1s 刷新，未开窗不采样（`:100-108`）
状态：实现+验证（`§8`）。

**11｜S/2026-10-28-falsum-spectrum-placeholder-design.md｜设计规范——未执行，且被反向执行**
- 声明：Spectrum 按钮保留为惰性占位（无事件/无可用态），删除分析/渲染/捕获/Shader（`:11-16,44-52`）
- 与代码矛盾：4 个实现文件全在（`G/Resources/Falsum/Shaders/FalsumTrackSpectrum.shader`、`G/Runtime/FalsumTrackSpectrumRenderer.cs`、`…Cache.cs`、`Editor/FalsumSpectrumCapture.cs`）；自检仍含 `track_spectrum`（`FalsumSelfCheck.cs:624,2219`）；按钮仍 `Raise(SpectrumToggleRequested)`（`FalsumEditorView.cs:6124-6126`）
状态：未实现。直接原因 ff48afc(10-29) 反而新增波形渲染；其要删的 `S/2026-10-28-falsum-track-spectrum-design.md` 从未入仓。

**12｜S/2026-10-30-falsum-ui-style-switching-design.md｜设计规范｜治理 `FalsumPreferences.FalsumUiStyle`、`FalsumUiStyles.cs`、`FalsumStudio.uss`/`FalsumCanvas.uss`**
- 三值 `Classic/Studio/Canvas`，缺失/非法归一化 Classic（`FalsumPreferences.cs:111-127`）
- 经典不加载附加表；切换 = 先移除再加载，唯一 `Apply` 入口（`FalsumUiStyles.cs:9-34`）
- 只换样式，不重建编辑器/谱面/播放；谱面世界、物件颜色、背景不随风格换色（`:31,49`）
- 草稿事务：即时预览→完成保存→取消/失败恢复进页前风格（`:46-48`）
状态：代码已实现，无结果节。

**13｜S/2026-10-30-falsum-tool-note-picker-design.md｜设计规范｜治理 `G/Tools/FalsumToolModels.cs`、`UI/FalsumToolDialogView.cs`、`EditorMode.ToolNotePick`**
- 字段契约 `type:"note"`；`min` 必须为 1 否则 0，`max>=min`，单选 `max` 必为 1，`noteKinds` 为四类非空子集（`:51-70`）
- `ctx.inputs.<key>` 恒为数组 `{key,id,kind,timeMs}`，按确认顺序、不重复（`:75-101`）
- 选择期禁拖拽/删除/放置/撤销/播放，Esc=取消，滚轮仍滚动，不产生撤销记录（`:179-184,211`）；确定门槛 数量∈[min,max] 且类型全符合（`:148-152`）
状态：实现（模型与隔离工程自检通过，完整自检被既有 Sky 检查阻断，Play Mode 捕获未做）。

**14｜S/2026-10-31-falsum-playback-bound-3d-effects-design.md｜设计规范｜治理 `PresentationAnimationClock.cs`、`Tracks/GameplayWallScroller.cs`、`FalsumTrackEndComposer.cs`**
- 单一时钟：Running 时 `+= clamp(unscaledDeltaTime,0,0.1)`，否则冻结；只有显式重开才 `Reset()`，seek 不动相位（`:113-124`）
- 游戏与 Falsum 3D 共用 `GameplayClock.IsPlaying`，ChartEditor 不自算相位（`:92-99`）
- 外侧墙独立 `12×3` Quad（4:1，同 2048×512）、绕 Y 90°、长轴沿纵深、偏移 (±1.1,-2.4,-8.0)、RenderQueue 3010（`GameplayWallScroller.cs:12-16,207-208`）；禁 0.65 亮度基线与 SHIFT_2/SPACE_2 三角 overlay（`:24-35,152`）
- Mask 时间源 `_TimeOverride>=0 ? _TimeOverride : _Time.y`（`FalsumTrackEndDiamondMask.shader:5,50-51`）；2D 视图不得出现三角/TrackEnd 替代层（`:253-256`）——注：2D 视图已于 2026-11-12 删除，本条仅作历史约束保留
状态：部分验证——编译 + `GameplayWallSelfCheck` 通过，`FalsumSelfCheck` 被“Design rule tool description is incorrect”检查阻断（`P/2026-10-31-…-plan.md:159-179`），与本次改动无关。
**2026-11-01 追记**：本条描述的外侧墙实现已整体移除
（还原度不足），只保留 `PresentationAnimationClock` 与
TrackEnd 时间源；见 `05-gaps.md` §G-13。

**15｜P/2026-10-30-falsum-tool-note-picker-plan.md｜实施计划**
7 任务：字段模型→选择层→编辑器会话→事件接入→本地化→自检→文档（`:6-186`）；硬约束：不得回退无关工作区改动（`:186`）。
状态：有结果节（`:188-202`）——编译通过、`note-picker-model: pass`、隔离工程 1 项自检通过；本地化审计仍报 `FalsumPreferencesView.cs` 4 处既有硬编码中文。

**16｜P/2026-10-31-falsum-playback-bound-3d-effects-plan.md｜实施计划**
实证口径：`visibleChangedPixels=10758`、`animatedChangedPixels=14869`、8 个扫描相位（`:170-173`）。
状态：执行到结果节（`:159-179`），自检未全绿（同 14）。

**17｜D/FalsumEditor_Audit_2026-10-24.md｜审计报告（非 spec/plan）**
34 条 F-01..F-34：已修 31、暂缓 F-10/F-18/F-20（`:12-15`，已于 2026-11-12 随 2D 模式删除而关闭）。
- `songId` 必须匹配 `^[a-z0-9][a-z0-9_-]{0,63}$`（F-01；`FalsumManifest.cs:85`）
- Manifest 所有 ZIP 路径唯一且不与 `song.json` 冲突，保存前再校验并 fail-closed（F-02）
- 必须见过 `chart(...)`、参数精确数量、拒绝 NaN/Infinity（F-03；`ChartParser.cs:114` sawChart、`FalsumChartEditor.cs:12484` TryParseFinite）
- 带 delay 谱面反复保存 `BpmEvents` 不得增长（F-04）
- 自检门禁须含 15 条负向回归（`:800-818`）
状态：暂缓三项均为 2D 模式遗留，**已于 2026-11-12 随 2D 模式整体删除而关闭**（当时：`SnapPlaneCell` 仍用 `Camera.main`+`Input.mousePosition` 无 2D 分支（`FalsumChartEditor.cs:12157-12184`，F-10）；2D Flick 仍 `Dictionary<int,FlickVisual>`（`Falsum2DWorld.cs:103-104`，F-20）；2D SkyArea 仍用裁剪后 t 采样边缘（`Falsum2DWorld.cs:1101-1111`，F-18））。文中“22/23/25 项自检”与本 `:20-24` 的 20 项清单彼此不一致。

**18｜D/PhaseA_Progress_2026-10-17.md｜进度总结（Phase A 基线）**
- 坐标唯一约定：一切基准在 `MasterScene` 局部空间；中四轨用 `note1..4` 局部 y + `JUDGEMENT_LINE` 局部 z，侧轨用 `1_LEFT/5_RIGHT` 局部 x,y，Flick 用 `JUDGEMENT_LINE.y+SkyY`（`:22-27`）
- Hold 中段长度 = `meshLengthZ × localScale.z`（0.5×0.4=0.2）（`:68`）
- Flick 语义固定 `4`=绿/右、`16`=黄/左，参数 t,center,grid,width,side（`:65-67`）
- 长 Hold/长 SkyArea 只从判定平面几何裁切，尾端过线才回收，不得整件隐藏（`:163,178`）
状态：历史基线；坐标与裁切条仍有效，`repeatMultiplier=0.1` 尾段 UV 仍为近似（`:90,113`）。

## 五条跨篇主题
1. 发行版保真优先：视觉项一律以逆向数值为不变量（SkyArea 的 2ms/0.0001ms/1448 三角、TrackEnd 的 0.428571r 与 Mirror 环绕、墙面禁 0.65 基线）（`S/2026-10-27-falsum-original-skyarea-replication.md:58-60,128-132`；`S/2026-10-31-…-design.md:24-35`）。
2. 两条时间轴必须分离：chart time 可 seek，presentation time 只受 IsPlaying 驱动（单帧 ≤0.1s、seek 不跳相）；Combo 反向依赖 chart time 的纯前缀函数（`S/2026-10-31-…-design.md:111-124`；`S/2026-10-26-…-auto-combo:37-38`）。
3. 编辑事务 fail-closed：Draft/`BeginChange`→`CancelChange`、先验后写、失败不留半提交（`FalsumChartSession.cs:129-131,287-289`；`S/2026-10-24 audit:261-263`）。
4. 自检 + Capture 是唯一验收通道：数值断言 + 1152×720/1920×1080 截图 + 构建（`S/2026-10-27-…-clean-rebuild:224-232`）。
5. UI 与渲染硬分界：UITK 管全部输入与布局（圆角 0、1730×1080），仅“选中轮廓 + 模式平面”留 uGUI 且不参与命中；3D 谱面世界独立于 UI 风格（`S/2026-10-19-…-ui-toolkit-rewrite-design.md:56-63`；`S/2026-10-30-…-ui-style-switching-design.md:31`）。

## 三个可直接执行的结论
- 必须先修：`S/2026-10-28-…spectrum-placeholder` 与代码完全相反（占位功能实际仍在运行），以及 `S/2026-10-19-…ui-toolkit` 的尺寸段、`S/2026-10-27-…clean-rebuild` 的 0.7/1.0 数值，均已过期。
- 可直接引用为真：第 1、2、5、9（3 Pass/0.428571r/Mirror）、10、14 篇的常量与顺序规则。
- 自检项数在文档间不一致（20/22/23/25），重构时应改为“以 `FalsumSelfCheck.cs` 的 `checks.Add` 清单为唯一事实源”。
