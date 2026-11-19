# Android 项目库手机布局修正

日期：2026-11-17。承接上一会话已批准的项目库设计及最后一次间距、搜索框反馈。

## 修改

- 顶栏、工具栏、底栏不参与 Flex 压缩；主内容和滚动区以剩余高度布局。
- 项目卡采用每行三等分，横向、纵向及网格内边距统一为 6 个逻辑单位。默认卡片高度 142，目标手机画布内可完整显示两行。
- 封面、难度格、标题和作者各自具有明确尺寸；难度名和等级独立排版，保留 MIN/EVO/ULT/FBD 原有正式颜色。
- 搜索输入容器只画一层边框；内部文本透明，提示文字置于输入控件上方。焦点用 USS class 标记，避免 UI Toolkit 不支持的 `focus-within`。
- 窄画布缩小封面，低画布压缩详情和顶底栏；元数据与四难度不会重叠或越过底栏。
- 搜索、排序重绘卡片后重建详情封面，避免纹理销毁导致右侧封面消失。

## 验证

真实 Unity UI Toolkit 离屏渲染，使用 12 个完整 `.falsum` 包；以下是模拟手机画布，不代表真机运行结果。

| 物理输出 | 逻辑参考画布 | 模拟安全区（左/右） | 报告 |
|---|---|---|---|
| 3200 × 1440 | 973 × 438 | 0 / 0 | `Diagnostics/AndroidLibrary/CaptureReport.json` |
| 2400 × 1080 | 800 × 360 | 24 / 8 | `Diagnostics/AndroidLibrary/project-library-compact-report.json` |
| 2280 × 1020 | 760 × 340 | 24 / 8 | `Diagnostics/AndroidLibrary/project-library-small-report.json` |

三组均通过实际几何检查：顶底栏高度、等宽三列、6 单位间距、作者文字边界、搜索框内层无边框、元数据和难度列表边界。第一组额外检查第二行卡片完整可见。

交互检查覆盖真实包搜索、搜索提示切换、详情封面保留、无匹配状态，以及清空搜索恢复全部 12 张卡片。截图工具同时保存各元素的实际几何到报告。

Unity 编译及完整 `FalsumSelfCheck` 通过，42 项含 `android_project_library`；本地化脚本审计通过。日志：`Diagnostics/AndroidLibrary/Unity-continue-selfcheck.log`。

APK Release ARM64 构建成功。包名、应用名、两个场景顺序、导入 Activity、签名、97 个内置文件哈希及原生库对齐检查通过。额外确认 IL2CPP 元数据与拆分的 `sharedassets0.assets` 都包含本轮新增的难度排版和小尺寸样式。

产物：`Builds/FalsumAndroid-0.46-Canary/FalsumPE-Android-ARM64-ProjectLibrary.apk`（126,656,966 字节）。SHA-256：`a07c71372d75b9a04f85ed4423e9e7d8857a56c5ce19925df64d95ee09be1a59`。构建日志：`Diagnostics/AndroidLibrary/Unity-continue-apk.log`；验证报告：`Diagnostics/AndroidSettings/ApkVerification.json`、`Diagnostics/AndroidLibrary/ApkUiVerification.json`。

构建后核对了接手时已有的受跟踪资产和工程设置，内容哈希没有额外漂移。

检查时 ADB 没有连接设备，真机启动、系统文件选择和触摸操作尚未验证。
