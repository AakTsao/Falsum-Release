# Android 项目库卡片与启动切场景修正

## 行为

卡片采用左侧方形曲绘、右侧 ID / 曲名 / 艺术家、底部单行难度数字。难度保留
MIN `#079fd2`、EVO `#078a72`、ULT `#8e2b93`、FBD `#e63260` 的原有颜色，
卡片底部只显示数字。项目卡和难度框使用主场景的 `Icons/button-corners.png`
与 6px 九宫格切片；详情保留难度名和谱师，装饰竖条也已替换为四角。

`LauncherActivity` 记录桌面图标启动请求并唤醒 Unity。Unity 的 `singleTask` Activity
重新进入前台时，`FalsumSceneNavigation` 消费该请求并让制谱器沿现有未保存确认流程
返回项目库。导入 Activity 的启动请求也经过该入口。Android 的首次场景回调另有
保护：误把 Gameplay 放在首位时，在其初始化前返回项目库。PC 的正常启动保持原样。

`FalsumAndroidNavigationManifest` 在 Gradle 项目生成后移除 Unity 默认的 MAIN / LAUNCHER
过滤器，让最终清单只保留一个桌面入口。普通 Unity Android Build / Build And Run
同样使用项目库、Gameplay 两个场景的顺序。

跨场景加载层单独放在 `DontDestroyOnLoad`，UI 根节点及遮挡节点全屏不透明，排序 10000。
加载层在场景激活后继续阻挡输入，等制谱器、指定谱面、音频、背景和音效初始化完成，
并给界面与世界两帧刷新时间后撤下。打开失败或初始化超时返回项目库并显示错误。

## 验证

- Unity 编译通过；完整 `FalsumSelfCheck` 43 项与本地化审计通过。
- 3200×1440 / 973×438 逻辑画布与 2280×1020 / 760×340 逻辑画布，后者含 24 / 8
  安全区，卡片横排、等宽间距、数字完整、无名称无竖条、搜索与封面检查均通过。
- `FalsumSceneNavigationCheck.Run` 使用真实 Cryogenic 包跑启动保护、打开、返回、新建。
  780 次遮挡采样、322 次初始化采样都通过；同时检查谱面、音频、背景已就绪及玩法卸载。
  自检退出时恢复原场景列表与项目库场景文件。
- Release ARM64 APK 构建、唯一桌面入口、实际有序 BuildSettings、Java 桥、加载层元数据、
  新卡片资源、签名、16KB 对齐及 97 项内置文件哈希均通过。

产物：`Builds/FalsumAndroid-0.46-Canary/FalsumPE-Android-ARM64-LibraryStartup-Corners.apk`，
126,665,812 字节，SHA-256 `69288645f582012ec320eb135b0678f122a8f7934113912414e47985b34e5a81`。
构建沿用此前已成功 APK 的 `--burst-disable-compilation`，未改变项目的 Burst 设置。

报告：`Diagnostics/AndroidLibrary/NavigationCheck.json`、
`Diagnostics/AndroidLibrary/project-library-cardcorners-report.json`、
`Diagnostics/AndroidLibrary/project-library-cardcorners-small-report.json`、
`Diagnostics/AndroidSettings/NavigationApkVerification.json`。

检查时 ADB 没有连接设备，尚未验证手机系统的实际冷启动和图标唤醒流程。
