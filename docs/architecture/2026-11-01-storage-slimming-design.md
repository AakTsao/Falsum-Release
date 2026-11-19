# 设计：工程与构建产物体积精简（2026-11-01）

> 目标：在不改变功能与视觉效果的前提下，压缩 `InFalsus-Gameplay` 工程目录与 Windows
> 构建包的磁盘占用。约束：**门禁全绿**（编译 + 编辑器自检 + 墙体自检/截图 + 构建）。

## 现状测量（接手时）

| 位置 | 体积 | 性质 |
|---|---|---|
| `Library/` | 2.5 G | 可再生缓存（Unity 导入产物） |
| `Builds/`（0.4 目录 + zip、0.31 目录 + zip） | 1.7 G | 构建产物 |
| `Diagnostics/` | 1.1 G | 394 个一次性日志/截图 |
| `Logs/` | 459 M | 588 个一次性日志 |
| `Assets/` | 314 M | 背景图 139 M、Recovered 贴图 51 M、ffmpeg 64 M、字体 17 M |
| `autobackups/` | 157 M | 编辑器自动备份（`alamode` 一份 97 M） |

`.git` 另有 397 M（LFS objects 392 M），**本轮不动**（删历史需 `filter-repo` 重写，风险高）。

## 方案

### A. 清可再生 / 一次性产物（约 4.4 G）

1. `Logs/` 全部历史日志。
2. `Diagnostics/` 只保留 `docs/memory/03-pipeline.md §4` 定义的"当前真相"文件：
   `FalsumSelfCheck.json`、`GameplayWallSelfCheck.txt`、`GameplayWallCapture.txt`、
   `UnityCompile.log`、`UnityFalsumSelfCheck.log`、`UnityGameplayWallSelfCheck.log`、
   `UnityGameplayWallCapture.log`，以及 `Screenshots/gameplay_wall_*.png`。
3. `autobackups/` 每份谱面只保留最新快照。
4. `Builds/` 删除旧 `FalsumEditor-Alpha0.31-20261026`（目录 + zip）与旧 zip；保留
   重建后的 `FalsumEditor-0.4-Alpha` 目录与其 zip。
5. `Library/` **最后**删除（避免重复导入耗时）。

### B. 背景图重编码（139 M → ~20 M）

- 40 张背景均为无 alpha（A 恒为 255）的 `RGBA 4096×2048` / `2048×1024` PNG。
- 转 **JPEG q93、最宽边 2048**（2048×1024 的原样保留）。`FitBackgroundToCamera`
  仅做等比裁切，不依赖像素尺寸；129 px/米 的贴合关系不变。
- **逻辑文件名保持 `.png`**：`../export/falsum/` 的 74 个原版包 `song.json` 里写死
  `"background": "xx.png"`，不能改名而破坏包兼容。
- 物理文件存 `.jpg`，新增 `FalsumRuntimePaths.ResolveBackgroundFile(name)`：
  先查同名文件，再回退 `.jpg` / `.jpeg`；`FalsumBackgroundPresenter`、
  `FalsumTrackEndComposer`、`FalsumBackgroundCatalog`（含缩略图）统一走它。
- `song_background_map.json/csv` 的 `background_file` 改为实际存在的 `.jpg`，
  以满足 `FalsumSelfCheck.CheckBackgroundResources` 的 `File.Exists`。
- 原 PNG 先移到 `_bg_original_png/`（不在 `Assets/` 下，不进构建），验证通过后删除。

### C. 贴图导入上限

`Recovered/Texture2D` 中 3 张 `4096` 上限贴图（`FlickBurst.png`、`FlickBurstR.png`、
`Texture-09-2.png`）降到 2048。运行时按其 2048 有效采样，视觉无差异，构建内贴图内存下降。

## 验收

1. `UnityCompile`（`Diagnostics/UnityCompile.log`，无 `error CS`）
2. `FalsumSelfCheck.Run`（`passed=true`）
3. `GameplayWallSelfCheck.Run` + `GameplayWallCapture.Run`（含背景截图像素对比）
4. `FalsumBuild.BuildWindows` 重建，核对目录体积
5. 删除 `Library/`

**回滚**：原 PNG 保留在 `_bg_original_png/`；`Git LFS` 中的原图可 `git checkout --` 恢复。
