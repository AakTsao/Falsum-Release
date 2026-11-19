# 00 · 从零上手

> 本文面向**第一次**打开这个仓库的人。读完约 8 分钟，之后你应该能独立跑起自检并定位任何模块。

## 1. 目录在哪、周围有什么

```
In.Falsus/                                  ← 原版游戏目录（含 infalsus.exe / GameAssembly.dll）
├─ InFalsus-Gameplay/                       ← 本仓库（Unity 工程，git repo）
│  ├─ Assets/InFalsus/{Editor,Gameplay,Recovered,Shaders,Plugins}
│  ├─ Assets/StreamingAssets/{Charts,Falsum}
│  ├─ docs/{memory,superpowers}
│  ├─ Diagnostics/  Logs/  Builds/  autobackups/  Tools/
│  └─ README.md  AGENTS.md
├─ docs/superpowers/                        ← 跨工程共享的 33 篇设计文档（本仓库只保留 17 篇）
├─ export/falsum/                           ← 74 个 `.falsum` 谱面包（兼容性回归语料）
├─ falsum_patcher/                          ← Python 静态写壳工具（把 .falsum 写进原版游戏）
└─ Mods/ MelonLoader/ UserLibs/ ...         ← 其他逆向/实验产物，与本仓库无关
```

Unity 版本 **6000.3.26f1**，URP **17.3.0**，D3D11，目标 Windows x64。

## 2. 一分钟版本

- **玩法层**（`Assets/InFalsus/Gameplay/Presentation/`）是从原版 Unity 资源里**恢复**出来的：
  音符预制体、`matid_*` 材质、SkyArea/TrackEnd 的 DXBC Shader 都被搬运并重写成 URP 兼容版本。
  视觉目标是「和原版一模一样」，所以里面大量常量都是逆向出来的魔数（见 `02-invariants.md` §B）。
- **编辑器层**（`Assets/InFalsus/Gameplay/Falsum/`）是**新写的**：
  同一场景兼作制谱画布，UI 全部是 Unity UI Toolkit（UXML/USS），数据模型是 `.falsum` ZIP 包。
- 两层通过 `GameplayBootstrap` 装配，通过 `EditorModeActive` 开关互斥。
- 表现层的时间与谱面时间**是两套**：编辑器可以 seek/暂停/变速，但轨道背景的动画相位由独立
  `PresentationAnimationClock` 驱动（见 `02-invariants.md` §B-9）。

## 3. 关键文件（按重要性）

| 文件 | 行数 | 为什么重要 |
|---|---:|---|
| `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs` | 12565 | 编辑器全部行为（模式机/输入/放置/拖拽/保存）。**没有 `#region`，靠方法名导航** |
| `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.MiddleEdit.cs` | 3185 | 中键三种精修模式 |
| `Assets/InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs` | 7267 | 全部 UI 布局与控件；40+ 事件回灌编辑器 |
| `Assets/InFalsus/Gameplay/Presentation/Notes/NoteRenderer.cs` | 2340 | 所有音符几何与可见性 |
| `Assets/InFalsus/Gameplay/Runtime/Bootstrap/GameplayBootstrap.cs` | 973 | **唯一**的每帧驱动与全部组件装配 |
| `Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumEditorWorld.cs` | 3020 | 3D 拾取/描边/幽灵预览 |
| `Assets/InFalsus/Gameplay/Falsum/Core/ChartWriter.cs` | 330 | 谱面真值的写出一侧（时间轴换算都在这里） |
| `Assets/InFalsus/Gameplay/Core/Chart/ChartParser.cs` | 786 | 读入一侧 |
| `Assets/InFalsus/Editor/FalsumSelfCheck.cs` | 2440 | 28 项自检的唯一事实源 |

> 行数用 `(Get-Content file).Count` 计；PowerShell `Measure-Object -Line` 会少数。

## 4. 五分钟跑起来

```powershell
cd <仓库根>

# a) 只编译不跑（最快，约 1-2 分钟）
& 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe' `
  -batchmode -quit -projectPath . -logFile Diagnostics\UnityCompile.log

# b) 跑编辑器自检
& 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe' `
  -batchmode -quit -projectPath . -logFile Diagnostics\UnityFalsumSelfCheck.log `
  -executeMethod InFalsus.EditorTools.FalsumSelfCheck.Run
Get-Content Diagnostics\FalsumSelfCheck.json

# c) 纯 Python 的本地化审计（秒级）
python Tools\falsum_localization_audit.py
```

图形界面里则直接：Unity 打开工程 → 打开
`Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity` → Play。
编辑器会以「无谱面」状态启动；`File > Open` 打开
`Assets/StreamingAssets/Falsum/cryogenic.falsum` 或 `alamode.falsum`。

## 5. 常见误解（都验证过）

| 误解 | 实际 |
|---|---|
| 「这是原版游戏的源码」 | 不是。是**逆向恢复 + 重写**；`Recovered/` 里是搬来的二进制资产，`Presentation/` 是重写的 C# |
| 「README.md 是最新文档」 | 不是。多处过期（NoteFX 已接线、构建版本号已变、文档链接跨工程），以 `docs/memory/` 为准 |
| 「`Diagnostics/` 里的日志都是当前状态」 | 不是。300+ 个文件里只有 4-5 个是「当前真相」，其余是历史一次性日志（清单见 `03-pipeline.md` §4） |
| 「`FalsumSelfCheck` 挂了就是代码坏了」 | 不一定。当前挂在一条**自检自身过期**的断言上（见 `05-gaps.md`） |
| 「工作区是干净的」 | 不是。接手时 68 改 + 32 新增（接手前既有）未提交，这是**别人正在做的事**，不要清理 |

## 6. 接下来

- 要改东西 → `01-architecture.md` 找到你那一层 → `02-invariants.md` 看你不能碰什么。
- 要判断「这算不算做完」→ `03-pipeline.md`。
- 想理解某段怪代码的来源 → `04-history.md` 按日期查。
