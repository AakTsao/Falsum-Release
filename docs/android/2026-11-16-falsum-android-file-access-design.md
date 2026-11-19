# FALSUM Android 文件选取与保存适配

日期：2026-11-16。状态：用户已批准；文件适配已实现，验证结果见 §8。

用户已确认优先覆盖 Android 原生版。本设计聚焦打开、选择素材、保存、另存为，以及与这些操作直接相关的应用内文件路径。

## 1. 当前问题与依据

- `FalsumFileDialog.cs` 的八个入口只实现 Unity Editor / Windows。其他平台返回空字符串。
- `FalsumChartEditor.cs` 的导入、新建音频与封面、偏好设置素材选择、天空区分享文件读取，以及 `FalsumChartEditor.Bank.cs` 的导出，均直接消费同步文件路径。
- `SavePackageInternal(bool)` 同步返回 `bool`。`ConfirmDiscardOrSave` 立即用它决定是否继续打开、新建或退出；改为系统文件选择后，必须等待真实写入结果。
- 包容器、素材转换和工具沙箱均使用 `System.IO` 的本地路径。Android 文档 URI 必须经文档提供方的流读写，不能当作磁盘路径传入这些现有组件。
- `FalsumBackupService.RootDirectory` 和部分默认谱包路径仍从 `Application.dataPath` 的上一级推导程序目录。Android 的编辑副本与备份应落在 `Application.persistentDataPath`。
- 旧 Android 可行性文档关于 ffmpeg / 缺少 Android BASS 的描述已经过期：当前音频导入使用进程内 BASS，工程已有 Android 各 ABI 的 `.so`。本次复用当前媒体管线。
- 当前安装的 Unity 6000.3.26f1 只有 Windows / Metro 构建支持，且用户已在 Unity 中打开工程。完整 Android 打包与真机结果需单独取得，不能用桌面编译结论替代。

## 2. 方案比较与选择

| 方案 | 优点 | 代价 |
|---|---|---|
| 原生 SAF 文档接口 + 应用内工作副本（推荐） | 用系统界面选文件和目的地；文档身份与谱包格式分离；可以保留完整本地恢复副本 | 需要 Java 桥接、异步完成通知及 URI 映射 |
| 接入第三方文件选择插件 | 部分原生桥接可以复用 | 新增依赖；仍需改保存事务、URI 处理与回调链 |
| 只使用应用目录内的浏览器 | 实现范围小 | 无法满足从用户下载目录、其他文档提供方选取与保存的目标 |

选择第一种。保留现有菜单中的“打开”“保存”“另存为”含义，让系统界面承担外部文件与目的地选择。

## 3. 用户行为

### 打开谱包

点击“打开”后暂停编辑播放，完成现有未保存确认，再打开 Android 系统文件选择界面。所选文档先通过输入流复制为应用内工作副本，复制完成后才调用现有 `LoadPackage`。

保留原文档 URI、显示文件名和实际取得的访问能力。再次打开同一 URI 使用稳定的工作副本身份，避免自动备份被随机临时路径分成多组。只读来源也允许打开，第一次保存时选择一个可写目标。

### 选取音频、封面与天空区分享文件

新建窗口和偏好设置里的“选择音频／封面”调用同一异步文件门面。将选中的文档复制到独立导入目录后，复用现有音频转换和封面预览逻辑。

`.fsky` 导入同样先读取本地副本，再将文本填回原导入窗口，保留用户确认步骤。异步结果到达时窗口已关闭、编辑器已销毁，或已被新的选择替代，就丢弃结果并释放临时资源。

自定义扩展名的谱包与分享文件使用可发现文件的通用 MIME 选择，并在返回后检验扩展名和文件内容。媒体使用对应 MIME；实际解码能力由现有转换器判断，转换错误继续显示现有本地化提示。

### 保存与另存为

- 对已获得写权限的文档，“保存”写回已绑定的 URI，不反复要求选择目录。
- 新建谱包和只读导入的谱包，首次“保存”调用系统创建文档界面，让用户选择文件名和位置。
- “另存为”每次调用系统创建文档界面，成功后将当前编辑目标绑定到新文档。
- 文件名提示来自用户可见的文档名或 `songId`，并补齐 `.falsum`。URI 本身不经过 `Path.ChangeExtension`。
- Android 新建窗口先准备应用内草稿路径，首次真实保存时再选择系统目标；避免仅打开新建窗口就创建外部空文件。新建完成后保持待保存状态，直至外部保存完成。
- 先用现有包写入流程生成完整的本地候选文件，再用输出流写入系统文档，流关闭成功后才宣布保存成功、清理脏标记、更新绑定和继续后续操作。
- 选择取消、权限撤销、文件移走或写入失败时，保持未保存状态与当前目的地绑定，保留本地候选文件供重试；“保存后继续”停留在当前谱面。

外部文档提供方的覆盖写入不能统一保证原子替换。因此本地候选谱包始终先完整生成，失败时明确报告结果并保留可恢复文件，不能声称系统目标具备本地 `File.Move` 的原子性。

### 天空区与工具文件导出

`.fsky` 导出在生成分享码后选择目标，将完整文本通过输出流写入，实际成功后才显示导出完成。

工具表单的读取文件先复制成本地输入。工具保存字段先准备本地暂存路径交给现有沙箱；脚本执行及现有文件写入阶段成功后，再选择系统目标并复制完整输出，避免预先创建外部空文档。导出失败或取消不能显示为工具文件保存完成。沙箱权限、超时与内存限制保持现行语义。

## 4. 组件与数据流

### 文件门面

在现有 `FalsumFileDialog` 上增加异步请求入口，以结构化结果区分成功、用户取消、忙碌及失败。结果提供本地路径、显示文件名和外部文档身份；所有 UI 完成通知在 Unity 主线程处理。

Editor / Windows 的适配器复用现有同步对话框并立即交付结果，调用方统一使用完成通知。未支持平台给出明确结果，避免把不可用能力伪装为用户取消。

一次只允许一个系统选择或文档写入请求。每次请求有唯一标识，回调只能完成一次；重复、过期及跨编辑器实例的结果不能改变当前状态。

### Android 原生桥接

用工程内 Java 源码插件提供小型中转 Activity，处理 `ACTION_OPEN_DOCUMENT` / `ACTION_CREATE_DOCUMENT` 及其结果。通过 Manifest 合并注册内部 Activity，无需替换 Unity 的主 Activity。

桥接负责查询显示文件名、按实际授予标志保存 URI 权限、读写 `ContentResolver` 流、关闭资源与传回结果。复制使用后台线程和固定大小缓冲区；读取元数据、启动系统界面和必要的回调派发遵循 Android 线程要求。

不从 URI 推导 `/storage/...` 路径，也不申请广泛存储管理权限。导入名称经过文件名清理并与请求身份结合，禁止把提供方的显示文件名直接拼成可越界路径。

### 本地副本与文档绑定

Android 可写根统一从主线程取得 `persistentDataPath`。拟定布局：

```text
<persistentDataPath>/Falsum/Documents/<document-id>/document.falsum
<persistentDataPath>/Falsum/Imports/<request-id>/<safe-name>
<persistentDataPath>/Falsum/Exports/<request-id>/<safe-name>
<persistentDataPath>/Falsum/document-bindings.json
<persistentDataPath>/autobackups/<原有歌曲与路径身份目录>/d<难度>/...
```

外部 URI 仅存在于文档绑定记录中；`FalsumPackage.SourcePath` 保持为真实本地文件路径。绑定记录原子写入，持久 URI 权限被撤销时允许重新选择目标。成功保存后保留完整工作副本，清理已消费的短期素材和成功导出暂存文件，失败恢复文件保留。

备份只改 Android 可写根，不改桌面根、时间戳、路径哈希算法或难度目录语义。桌面模拟移动 UI 时仍使用桌面文件后端，平台能力由编译平台决定。

### 编辑器保存事务

把现有保存操作拆成准备候选谱包、选择／解析目标、写入、提交状态四步。“保存后继续打开／新建／退出”改用完成回调，只在真实成功后执行 continuation。

操作期间锁住重复保存和会改变待保存谱包的编辑入口；取消或失败后解除。回调需校验捕获的包实例和请求身份。失败或取消不能清理 `_isDirty`，不能错误地更新用户可见文件名，也不能提交另存为的新绑定。

沿用 `ChartWriter` 的时间轴换算、天空区分组和现有包临时写入机制；文件适配层不解析或重写谱面 DSL。

## 5. 实现范围

预计改动集中于文件门面、Android 文档桥接及绑定服务、`FalsumChartEditor` 文件操作相关入口、天空区导出、工具文件完成阶段、备份根路径和两语言提示。新增原生插件及 C# 文件附 Unity `.meta`。

不在这轮扩展到 iOS / WebGL，也不增加通用文件管理器。APK 内 StreamingAssets 的全面迁移、背景库管理和整套 Android 发布流程需要分别验收；仅在文件选择与保存的直接依赖上修正路径。本次不重写音频编解码或渲染逻辑。

## 6. 验证与验收

1. 按项目要求先运行 Unity 编译，再运行编辑器自检和本地化审计。工程占用导致批处理无法启动时，记录具体日志，并用现有基于 Unity 编译响应文件的离线编译检查补充验证；后者不算完整 Unity 自检。
2. 增加文件操作专用检查，验证结构化取消结果、回调恰好一次、过期结果丢弃、后端选择、文件名处理、稳定副本身份、绑定往返，以及失败保存不清脏／不继续操作。
3. 用可控制成功与失败的文档后端检查谱包导出：写出后回读，比较音符、事件、delay、清单与未知 ZIP 条目；覆盖写入失败及另存为取消后的状态。
4. 检查工具与 `.fsky` 在实际输出成功之前不会报告文件导出成功。
5. Android 构建验证 Java 插件与合并 Manifest，再在真机验证打开下载目录谱包、只读来源另存、音频／封面选择、普通保存、另存为、取消、权限撤销、切后台恢复、大文件复制和备份可写。
6. 导出的 `.falsum` 需能在桌面制谱器中再次打开，导出的 `.fsky` 需能再次导入。

桌面编译、离线模拟、Android 构建和真机验证分别报告，不互相替代。

## 7. 设计审阅

已检查：范围固定为 Android；系统 URI 与本地文件身份分离；首次保存、新建和只读导入行为明确；失败保存保留状态；工具输出有真实完成阶段；备份沿用现有身份规则；没有新增沙箱权限。

用户已批准实施。Android 桥接的设备行为必须由真机验证确认。

本轮基线检查：本地化审计通过。Unity 批处理编译启动后以返回码 1 退出，没有进入编译阶段；日志为 `Diagnostics/UnityMobileFilesBaselineCompile.log`。当前存在已打开本工程的 Unity 实例，因此此项不能报告通过。Git Bash 离线检查入口也以返回码 1 结束且没有检查输出，本轮不据此作编译结论。运行时实现尚未开始。

## 8. 实现与环境

- 异步入口位于 `FalsumFileDialog.Async.cs`；文档身份、文件名与暂存操作位于 `FalsumDocumentStore.cs`；单请求、过期结果和错误回调处理位于 `FalsumAndroidFileBridge.cs`。
- `FalsumChartEditor.Files.cs` 负责保存快照、保存结果和后续操作；原有数据对象在外部写入成功后才提交。`FalsumPackage.CreateSaveSnapshot` 保留清单、所有谱文与未知 ZIP 条目。
- 原生代码位于 `Assets/Plugins/Android/FalsumDocuments.androidlib/`。显式 Gradle namespace、源码目录与 consumer ProGuard 规则保证 Activity 编入原生库；复制在工作线程执行，常见 Activity 重建与进程终止恢复不会留下空的中转窗口。
- Android 天空区“文件”按钮直接进入 `.fsky` 导入确认窗口；桌面文件夹入口沿用原行为。临时素材只在消费完成后清理，谱包工作副本保留；只有谱包请求持久 URI 权限。
- 用户要求补装 SDK/NDK/JDK，并明确不使用 Unity Hub。`Tools/falsum_install_android_dependencies.py` 从 Unity 精确版本清单取得直链，支持缓存与分段下载，按官方 SHA 校验后安装。
- Windows 拒绝写入 Unity 安装目录，所以依赖安装到 `C:/Users/AakTsao/AppData/Local/Falsum/AndroidToolchain/6000.3.26f1/`。包含 OpenJDK 17.0.18+8、NDK r27c、SDK build-tools 36.0.0、命令行工具 16.0 及清单所列平台与配套组件。SDK 许可证处理成功。
- `FalsumAndroidToolchain.ConfigureFromEnvironment` 已将此目录配置到 Unity 外部工具设置；`Diagnostics/FalsumAndroidToolchain.json` 为 `passed:true`、`androidBuildSupport:true`。
- 文件专用自检已注册到完整 Editor Self Check。检查重复／过期／无效 JSON 回调、文件名与 UTF-8 字节边界、真实 I/O 失败后的候选文件保留、包快照、未知条目与谱文往返。报告为 `Diagnostics/FalsumFileAccessSelfCheck.json`。

最终编译、自检和原生库验证的具体结果记录在 `docs/reviews/2026-11-16-android-file-access-results.md`。没有连接真机执行 SAF 操作，尚不把系统选择、云提供方写回或 APK 整体运行当作已验证。

## 9. 安卓启动修正与默认界面

用户要求安卓版默认打开移动端 UI 和编辑器，并报告启动调用 `Screen.mainWindowDisplayInfo` 时出现 `NotSupportedException`。

- `FalsumWindowPlacement.CenterMainWindow` 的窗口 API 仅在 `UNITY_STANDALONE && !UNITY_EDITOR` 下编译；Android 的启动和偏好设置调用均为空操作。
- APK 的唯一启动场景保持为 `GameplayRuntime.unity`，该场景由 `GameplayBootstrap` 初始化 `FalsumChartEditor` 并进入编辑器模式。
- 原生移动平台自动挂载 `FalsumMobileShell`。启动时为自动挂载的壳层应用 `MobileUiScale`，无需启用桌面开发者的强制移动布局开关。
- 修正版执行完整编译、自检及 APK 校验，并检查实际 Android IL2CPP 生成的方法已不包含桌面窗口 API。真机启动结果仍由设备复测确认。
