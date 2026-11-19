# FALSUM Release

FALSUM（In Falsus 社区制谱器）的发行与公开设计文档。

**本仓库不包含本体源码。** 源码在 [AakTsao/FalsumEditor](https://github.com/AakTsao/FalsumEditor)。

## 目录

```
docs/
  skyarea/   天空判定线（SKYAREA）反向复刻与原版参数推导
  flick/     FLICK 特效复刻参考实现包
  design/    可公开的系统设计文档
releases/
  0.47.0/    FALSUM 0.47 发行版
```

## 发行版

### 0.47.0 · Canary

| 平台 | 文件 | 说明 |
|---|---|---|
| Windows | `releases/0.47.0/FalsumEditor-0.47-Canary-Windows.zip` | 桌面制谱器 |
| Android | `releases/0.47.0/FalsumPE-0.47.0-Android-ARM64.apk` | Falsum PE，ARM64 |

Android 版为独立产品线 **Falsum PE**（`cn.aaktsao.falsum.pe`），版本号带 `PE-` 前缀。

### 0.47 主要变更

**安卓**
- 新增项目库场景：卡片网格、详情面板、分享/打开导入
- 游戏内版本号显示 `PE-` 前缀
- Falsum PE 独立图标

**场景间加载页**
- 全屏启动序列替代原先的项目库内小弹窗
- 三阶段读数 + 缓动进度轨 + 未知进度扫光
- 修复样式表资源名与模板冲突导致文字发黑、字标尺寸失控的问题

**项目库性能**
- 切卡不再重建网格、不再重解封面
- 封面逐帧解码，单帧峰值 1.18 GB → 约 17 MB
- 扫描不再把每个封面读进内存（减少 432 MB 托管分配）
- 搜索防抖、刷新节流

**移动端编辑**
- 时间框与播放键拆为两个独立浮动控件
- 单指竖直拖动滚动谱面，遇到物件操作立即让位
- 多选不再把滚动误判为流速调节

## 文档

### SKYAREA（天空判定线）

天空区域是 FALSUM 中信息密度最高的编辑对象，原版行为没有公开说明。
这里记录了从运行时数据反推出来的分组规则、判定线几何与参数表。

- `2026-10-27-falsum-original-skyarea-replication.md` —— 原版行为复刻推导
- `2026-11-06-falsum-skyarea-bank-design.md` —— 预设库设计
- `2026-11-18-skyarea-editing-order-fix-design.md` —— 编辑顺序修正
- `天空判定线参数总表.md` —— 实测量化参数

### FLICK

FLICK 特效的复刻参考包，含着色器、运行时与资产清单。

## 许可

本体不开源，仅发行二进制。文档部分见各文件内说明。
