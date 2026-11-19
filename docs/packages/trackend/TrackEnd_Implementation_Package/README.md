# TrackEnd 动态背景 · 实现与依赖打包

In Falsus / FALSUM 编辑器里 **TrackEnd（轨道尽头菱形动态背景）** 的完整实现、
贴图、Shader、代码以及全部依赖的归档包。

- **源工程**：`InFalsus-Gameplay`（Unity **6000.3.26f1**，URP **17.3.0**）
- **分支**：`perf/render-hotpaths`，HEAD `0738600`
- **导出时间**：2026-11-10
- **导出内容**：工作区当前状态（含 2 个未提交改动，见 `00_docs/TrackEnd_依赖清单.md` §6.1）
- **包内文件**：见 `MANIFEST_SHA256.txt`

---

## 一分钟看懂

TrackEnd 不是一张图，是**每帧在 GPU 上重算**的菱形遮罩：

```text
清空 Mask RT
 → 白色三角 Pass 0（100 个，上浮）
 → 黑色三角 Pass 1（同批三角 ×0.7 速度，擦除重叠）
 → 白色六边形 Pass 2（中央网格）
 → 七段五采样模糊（full→half→quarter→half→full）
 → 与 playfieldBG.png 合成（uv + 0.5，取源图下半）
 → 相机朝向的透明 Quad 输出
```

核心只有 **4 个文件**：

| 文件 | 作用 |
|---|---|
| `FalsumTrackEndComposer.cs`（912 行） | 拥有 RT / Mesh / Material，处理锚点与分辨率 |
| `FalsumTrackEndComposerFeature.cs`（232 行） | URP RenderGraph Feature，每帧执行 Mask + Blur |
| `FalsumTrackEndDiamondMask/Blur/Overlay.shader` | 3 Pass / 2 Pass / 1 Pass |
| `FalsumTrackEndComposerInstaller.cs`（140 行） | 自动把 Feature 装进 `PC_Renderer.asset` |

---

## 目录结构

```text
TrackEnd_Implementation_Package/
├─ README.md                          ← 本文件
├─ MANIFEST_SHA256.txt                ← 全包文件校验和
├─ 00_docs/
│  ├─ TrackEnd_实现方式说明.md          ← ★ 实现原理详解（数值、流程、时序）
│  ├─ TrackEnd_依赖清单.md              ← ★ 依赖全景（文件/调用点/断言/GUID/已知问题）
│  ├─ TrackEnd_接入指南.md              ← ★ 移植到别的 URP 工程的步骤与排错
│  └─ design/                          ← 原工程的设计文档（含已被取代的历史版本）
│     ├─ 2026-10-26-falsum-track-end-background-design.md   （旧方案，已被取代）
│     ├─ 2026-10-27-falsum-trackend-clean-rebuild-design.md （★ 现行方案）
│     └─ 2026-10-27-falsum-chart-preview-framing-design.md  （预览取景联动）
├─ 01_runtime/                        ← 运行时代码 + Shader（连 .meta 一起，GUID 不变）
│  └─ InFalsus/
│     ├─ Gameplay/Presentation/Backgrounds/
│     │  ├─ FalsumTrackEndComposer.cs
│     │  └─ FalsumTrackEndComposerFeature.cs
│     └─ Shaders/Compat/
│        ├─ FalsumTrackEndDiamondMask.shader
│        ├─ FalsumTrackEndDiamondBlur.shader
│        └─ FalsumTrackEndDiamondOverlay.shader
├─ 02_editor/
│  └─ InFalsus/Editor/FalsumTrackEndComposerInstaller.cs
├─ 03_config/                         ← URP 配置资产
│  ├─ FalsumTrackEndComposerFeature.asset    （Feature 的 SO 实例）
│  ├─ PC_Renderer.asset                      （持有 Feature 的 UniversalRendererData）
│  ├─ PC_RPAsset.asset                       （指向 PC_Renderer 的 RP Asset）
│  └─ GraphicsSettings.asset                 ★ Always Included Shaders（3 个 TrackEnd Shader）
├─ 04_textures/
│  ├─ playfieldBG.png                        ★ 唯一贴图 1024×1024 RGBA
│  ├─ playfieldBG.png.meta
│  └─ 贴图清单.md
├─ 05_integration/                    ← 13 段调用点片段（带真实行号，不含全文）
│  ├─ 01_FalsumBackgroundPresenter_TrackEnd.cs
│  ├─ 02_FalsumBackgroundPresenter_Initialize.cs   ★ 唯一实例化点
│  ├─ 03_GameplayBootstrap_Tick.cs                 ★ 每帧写时间
│  ├─ 04_GameplayBootstrap_Reset.cs                ★ 重开归零
│  ├─ 05_GameplaySceneBinding_Anchor.cs            ★ 锚点路径
│  ├─ 06_GameplaySceneBinding_Bind.cs
│  ├─ 07_FalsumChartEditor_PreviewFraming.cs       （预览取景联动）
│  ├─ 08_FalsumBuild_EnsureInstalled.cs
│  ├─ 09_FalsumSelfCheck_Assertions.cs             ★ 14 条验收断言
│  ├─ 10_FalsumBackgroundCapture_Mask.cs           （遮罩导出）
│  ├─ 11_GameplayPlayCapture_Mask.cs
│  ├─ 12_FalsumHeavyChartPerformanceCapture_Metrics.cs
│  └─ 13_FalsumHeavyChartPerformanceCapture_MaskRebuild.cs
├─ 06_scene_anchor/                   ← 场景锚点
│  ├─ README_scene_anchor.md                 ★ 锚点路径/两个同名节点/缺失行为
│  └─ PLANE_END_scene_yaml.txt               场景与 prefab 原始 YAML 片段
└─ 07_full_dependents/                ← 依赖方完整源码（片段不够时查这里）
   ├─ FalsumBackgroundPresenter.cs            （创建 Composer）
   ├─ GameplaySceneBinding.cs                 （锚点绑定）
   ├─ GameplayBootstrap.cs                    （时间驱动）
   ├─ FalsumChartEditor.cs                    （预览取景）
   ├─ FalsumEditorWorld.cs                    （仅 ResolveTrackEndZ，与本项目无关）
   ├─ MeasureLineRenderer.cs                  （仅 ResolveTrackEndZ，与本项目无关）
   ├─ FalsumSelfCheck.cs                      （断言块）
   ├─ FalsumBackgroundCapture.cs              （截图）
   ├─ GameplayPlayCapture.cs                  （截图）
   ├─ FalsumHeavyChartPerformanceCapture.cs   （性能诊断）
   ├─ FalsumBuild.cs                          （构建时装 Feature）
   ├─ manifest.json                           （URP 17.3.0 等包依赖）
   └─ ProjectVersion.txt                      （Unity 6000.3.26f1）
```

★ = 必读

---

## 快速使用

### 只想搞懂原理
读 `00_docs/TrackEnd_实现方式说明.md`，配 `05_integration/02/03/04` 三个片段。

### 想移植到别的 URP 工程
按 `00_docs/TrackEnd_接入指南.md` 走，最小落地清单在文末。

### 想改效果
所有可调参数集中在 `FalsumTrackEndComposer.cs:29-48`
（三角 100 个 / 缩放 0.5 / 六边形 2.0 / 模糊 0.017×0.011 / 输出队列 2950）。
`00_docs/TrackEnd_实现方式说明.md` §10 有速查表。

### 想验证
`05_integration/09_FalsumSelfCheck_Assertions.cs` 是现成的 14 条断言；
`SaveMaskForDiagnostics` 能把遮罩导成 PNG 直观看链路。

---

## 三个必须记住的约束

1. **`_TimeOverride` 不能删。** C# 每帧写表现时钟，Shader 用
   `_TimeOverride >= 0 ? _TimeOverride : _Time.y`。删了会导致暂停时粒子还在动。
   Mask Shader 与 Composer **必须成对使用**（这两个文件在源工程里都还是未提交改动）。

2. **底图必须 `TextureWrapMode.Mirror`。** 代码硬写在
   `FalsumTrackEndComposer.cs:415`；改 `Repeat` 会在 V 跨界处出横向接缝，
   改 `Clamp` 会出无网格竖条。

3. **Overlay 的 `uv + (0, 0.5)` 不能改成直接采样。** 它与输出 Quad 的
   `OutputUvMax = 0.5` 是一对：Quad 送下半段 UV，Overlay 反着偏移 0.5 去采底图，
   合起来才是发行版的构图关系。

---

## 文档与代码冲突时

旧设计文档（`00_docs/design/2026-10-27-*`）里有若干数值与当前代码不符，
**一律以代码为准**：

| 项 | 文档 | 代码 |
|---|---|---|
| 输出 UV | 0.7 | **0.5** |
| 六边形 | 半径 1.0 无放大 | **`HexagonScale = 2.0`** |
| 模糊偏移 | `0.593407r` / `1.384616r` | **`0.428571r` / `r`** |
| Mask Pass | — | **3**（Donut 已删除） |

完整对照见 `00_docs/TrackEnd_实现方式说明.md` §9。

---

## 完整性校验

```powershell
# 在解压后的目录里执行
Get-FileHash -Algorithm SHA256 -LiteralPath (Get-ChildItem -Recurse -File |
  Where-Object Name -ne 'MANIFEST_SHA256.txt').FullName |
  Format-Table Path, Hash -AutoSize
```

或直接比对 `MANIFEST_SHA256.txt`（格式：`SHA256  <相对路径>`）。

`playfieldBG.png` 的 MD5 应为 `cb795c6e8eb5911c490bc609b4da81b5`，
大小 `917452` 字节。
