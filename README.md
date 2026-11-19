# FALSUM Release

FALSUM（In Falsus 社区制谱器）的发行版与公开设计文档。

**本体不开源。** 这里发布可执行文件，以及经过脱敏的架构、渲染与交互设计文档。
编辑器源码不在本仓库。

## 下载

最新版本见 [Releases](../../releases/latest)。

| 平台 | 包名 | 说明 |
|---|---|---|
| Windows | `FalsumEditor-<ver>-Windows.zip` | 桌面制谱器，解压即用 |
| Android | `FalsumPE-<ver>-Android-ARM64.apk` | Falsum PE，ARM64，独立产品线 |

Android 版应用 id 为 `cn.aaktsao.falsum.pe`，版本号带 `PE-` 前缀。

## 文档

完整索引见 [DOCS.md](DOCS.md)。按主题分组：

| 目录 | 内容 |
|---|---|
| `docs/architecture/` | 总体架构、项目记忆入口 |
| `docs/invariants/` | 工程不变量：每一条都对应一次踩过的坑 |
| `docs/rendering/` | 轨道、音符、TrackEnd、**SKYAREA 判定线** |
| `docs/ui-mobile/` | UI Toolkit 界面体系与移动端布局 |
| `docs/android/` | Android / Falsum PE 专项 |
| `docs/editor/` | 编辑器交互：选择、锚点、网格、工具链 |
| `docs/preferences/` | 偏好设置体系 |
| `docs/audio/` | 音频管线与打击音密度 |
| `docs/verification/` | 验证流水线与性能优化 |
| `docs/history/` | 历史决策与已知缺口 |
| `docs/external-tools/` | 外置 JS 工具：设计规则检查、Track 缓动 |
| `docs/reviews/` | 实测评审结果 |

### 重点：SKYAREA 天空判定线

天空区域是 FALSUM 中信息密度最高的编辑对象，原版行为从未公开。
仓库内的推导基于运行时数据反向分析，包含分组规则、判定线几何与量化参数：

- [原版行为复刻推导](docs/rendering/2026-10-27-falsum-original-skyarea-replication.md)
- [预设库设计](docs/rendering/2026-11-06-falsum-skyarea-bank-design.md)
- [编辑顺序修正](docs/rendering/2026-11-18-skyarea-editing-order-fix-design.md)
- [天空判定线参数总表](docs/rendering/天空判定线参数总表.md)

## 外置工具

编辑器支持 JS 插件式工具，随包内置两个：

| 工具 | 作用 |
|---|---|
| `com.falsum.tools.design-rule-checker` | 检查 TAP/HOLD 的时间与轨道冲突，报告可定位跳转；只读，不产生撤销记录 |
| `com.falsum.tools.track-easing` | 按起止速度与缓动类型生成 Track 变速节点，整次生成只占一条撤销记录 |

两者的说明与清单见 `docs/external-tools/`。用户也可在
`Assets/StreamingAssets/Falsum/UserTools/` 下自行放置工具包。

## 参考实现包

两个上游交付的完整参考包，保持着色器、运行时与资产清单：

- `docs/packages/flick/` — FLICK 特效复刻（749 个文件）
- `docs/packages/trackend/` — TrackEnd 复刻（58 个文件）

## 构建依赖

复现构建环境需要：

- Unity **6000.3.26f1**
- URP **17.3.0** / Shader Graph **17.3.0**

完整锁定版本见 `docs/dependencies/`（Unity 包清单、锁文件、编辑器版本）。

## 版本

### 0.47.0 · Canary

**安卓 / Falsum PE**
- 新增独立的项目库场景：卡片网格、详情面板、分享与“用其他应用打开”导入
- 游戏内版本号显示 `PE-` 前缀，APK 版本为 `PE-0.47.0`
- Falsum PE 使用独立应用图标，桌面图标不受影响

**场景间加载页**
- 全屏启动序列取代原先项目库内的小弹窗
- 三阶段读数、缓动进度轨、未知进度扫光动画
- 修复样式表资源名与模板冲突导致的文字发黑、字标尺寸失控

**项目库性能**
- 切换卡片不再重建网格、不再重解封面
- 封面逐帧解码，单帧峰值从 1.18 GB 降到约 17 MB
- 扫描阶段不再把每个封面读进内存（减少 432 MB 托管分配）
- 搜索防抖、刷新节流、安全区写入去重

**移动端编辑**
- 时间框与播放键拆为两个独立浮动控件
- 单指竖直拖动滚动谱面，一旦成为物件操作立即让位
- 多选状态不再把滚动误判为流速调节

**偏好设置**
- UI 风格仅保留「经典」与当前设计（0.5），默认当前设计
- 布局覆盖改为三态下拉：自动 / 强制手机 / 强制桌面，双向可切
- 开发者选项在正式版中可见

## 许可

本体为闭源发行，仅提供二进制。文档与参考包版权归原作者所有。
In Falsus 及相关素材版权归其权利人。
