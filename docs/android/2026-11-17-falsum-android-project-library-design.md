# Falsum PE Android 项目库、存储与分享导入设计

日期：2026-11-17。状态：用户已批准设计方向。

## 1. 目标

Android 版从本轮起作为独立产品 **Falsum PE**：

- Android application id：`cn.aaktsao.falsum.pe`。
- Android 应用名：`Falsum PE`。
- Unity 启动画面结束后立即进入轻量项目库，不先加载 `GameplayRuntime.unity`。
- 项目库直接管理应用专属外置目录里的 `.falsum` 文件；普通打开、新建和保存不再依赖 SAF 文件管理器。
- 支持 Android 的“用 Falsum PE 打开”和“分享到 Falsum PE”导入入口。
- PC 构建继续直接启动现有玩法／编辑器场景，文件布局和产品身份不变。

包名改变意味着新版与旧的 `com.aaktsao.falsum.editor` 是两个独立 Android 应用。新应用不能越过 Android 沙箱直接迁移旧应用目录；旧项目需要从旧版导出／分享后由新版导入。

## 2. 方案选择

采用独立的轻量 Unity 项目库场景。Android 构建顺序为项目库场景在前、玩法场景在后；选择项目后才异步加载玩法场景。

不采用以下方案：

- 在 `GameplayRuntime.unity` 上覆盖项目库：玩法资产仍在 Unity 启动画面之后同步加载，无法消除用户看到的数秒停顿。
- 原生 Android Activity 实现完整项目库：首屏更轻，但会形成 Java 与 UI Toolkit 两套界面、主题和本地化体系，维护成本过高。

原生层只负责 Android Intent、ContentResolver 流复制和启动参数队列；所有可见项目库界面仍由 Unity UI Toolkit 实现。

## 3. Android 存储布局

`Application.persistentDataPath` 在 Android 构建中作为应用专属外置 `files` 根。首次启动确保以下目录存在，目录名固定为小写：

```text
/storage/emulated/0/Android/data/cn.aaktsao.falsum.pe/files/
├─ falsums/
│  ├─ <songId>-<safe-title>.falsum
│  ├─ .backups/
│  └─ .recovery/
├─ temp/
│  ├─ incoming/
│  ├─ imports/
│  ├─ exports/
│  ├─ media/
│  ├─ thumbnails/
│  └─ cache/
├─ externaltools/
└─ backgrounds/
```

职责如下：

- `falsums/`：项目库唯一主目录。普通保存、另存为默认目标、新建项目和系统分享导入的最终文件都在这里。
- `falsums/.backups/`：Android 自动备份根；沿用现有歌曲／难度／时间戳语义。
- `falsums/.recovery/`：后台回收恢复快照。它不是可随意删除的缓存，不能放进启动时清理的 `temp/`。
- `temp/`：可重建数据，包括 URI 输入副本、导出候选、媒体转换中间文件、项目库缩略图和音频缓存。启动时清理过期项，不删除正在处理或最近失败后保留的候选。
- `externaltools/`：Android 外置 JS 工具根。内置工具从 StreamingAssets 同步到这里，用户工具也从这里发现。
- `backgrounds/`：用户背景根。解析背景时先查此目录，再回退 APK 内置背景库。

所有路径都由 `FalsumRuntimePaths` 提供，业务代码不得自行拼接 `Android/data`。桌面继续使用现有程序目录布局。

## 4. 项目库启动场景

新增 `FalsumAndroidLibrary.unity`，只包含项目库所需的 Camera、UIDocument 和启动控制器，不引用玩法预制体、恢复材质或玩法场景对象。

Android 构建场景顺序：

```text
0  FalsumAndroidLibrary.unity
1  GameplayRuntime.unity
```

PC 构建仍只把 `GameplayRuntime.unity` 作为启动场景。

启动顺序：

1. Unity 显示许可要求的启动画面。
2. 首场景立即显示项目库骨架、应用标题和加载指示。
3. 后台线程创建目录、消费待导入 Intent 队列并扫描 `falsums/*.falsum`。
4. 项目卡分批回到主线程显示；损坏文件单独显示错误状态，不阻塞整个库。
5. 用户选择项目后显示项目库内加载遮罩，设置一次性启动请求，再异步加载 `GameplayRuntime.unity`。
6. `GameplayBootstrap` 在编辑器初始化之前消费启动请求并打开指定包。
7. Android 编辑器提供“返回项目库”；有未保存修改时沿用现有保存／放弃确认，成功后卸载玩法场景并返回项目库。

项目库场景不把 Gameplay 场景预加载为 Additive。只有真正打开或新建项目时才支付玩法场景加载成本。

## 5. 项目库界面

视觉结构参考用户提供的图 2，但保持 Falsum 现有深色、青色描边和方角设计：

- 左侧／中部：项目卡网格，可滚动。
- 卡片按用户最新确认改为左曲绘、右信息；右侧自上而下为 songId、曲名、艺术家，每个可用难度独占一行。曲绘保留原比例。
- 手机采用两列等宽卡片，间距为 6 个逻辑单位；低宽度画布缩小曲绘和行高。
- 右侧：当前项目详情、四难度信息及主操作按钮。
- 顶部左侧直接显示现有 `FalsumLogo.png`；刷新、导入、新建按钮字号为 16，按钮高度至少 44。排序和搜索文字同步提高可读性。
- 底部：项目数量、扫描状态、打开按钮；窄屏时详情面板改为覆盖抽屉。

首屏先显示骨架卡，不等待全部 ZIP 解析。扫描器只读取 ZIP 中的 `song.json` 和封面条目，不把 `audio.ogg` 或所有谱文载入内存。解析结果和缩略图缓存到 `temp/`，缓存键包含文件路径、大小和最后修改时间。

操作行为：

- 单击卡片：选择并展示详情。
- 双击卡片或点击“打开”：加载编辑器。
- 新建：打开项目库内新建窗口。
- 导入：启动现有 Android SAF 打开流程，成功后复制进 `falsums/`。
- 删除：二次确认后删除主项目文件，并清理对应临时缩略图；不自动删除用户背景或工具。
- 刷新：取消旧扫描任务后重新枚举。
- 搜索：匹配标题、作者和 songId。
- 排序：最近修改、标题、songId。

全部可见文字必须走 `FalsumLocalization`。

## 6. 新建项目与默认命名

项目库新建窗口复用现有新建谱面校验、音频导入、封面导入和 `FalsumPackage.CreateNew` 服务，不复制清单规则。

Android 默认目标为：

```text
files/falsums/<songId>-<safe-title>.falsum
```

规则：

- 文件名必须以原始 `songId` 开头。
- 标题经过文件名安全化、空白折叠和长度限制；安全化后为空时只用 `<songId>.falsum`。
- 同名文件不覆盖，依次使用 `-2`、`-3` 等后缀。
- 文件名只是 Android 默认名，`song.json` 中的 songId 仍是唯一数据真值；现有“文件名与 id 解耦”不变量保持成立。
- 新包写入完整成功后才进入编辑器。失败时保留窗口输入，不生成零字节项目卡。

编辑器内普通“保存”直接原子替换当前 `falsums` 文件。“另存为”默认仍在 `falsums` 中生成 id 开头的建议名；另有“导出／分享”用于发给其他应用或用户选择外部位置。

## 7. 分享与“用其他应用打开”导入

Android 原生库新增导入 Activity，声明 `exported=true`，接收：

- `ACTION_VIEW`：文件管理器或聊天应用中的“用 Falsum PE 打开”。
- `ACTION_SEND`：分享到 Falsum PE。
- `ACTION_SEND_MULTIPLE`：批量导入。

Intent 只把 URI 当作 ContentResolver 流，不尝试还原 `/storage/...` 文件路径。支持 `.falsum` 常见 MIME：自定义 MIME、`application/zip`、`application/octet-stream` 和常见压缩包 MIME；无可靠 MIME 时以显示文件名和内容校验作为最终判断。

导入流程：

1. 原生 Activity 读取显示名和 URI 授权，把内容复制到 `temp/incoming/<request-id>/`。
2. 原生层把待处理记录写入小型持久队列，然后启动／唤醒 Unity 主 Activity；冷启动和热启动走同一队列。
3. 项目库消费队列，用 `FalsumPackage.Load` 完整验证格式、大小、清单和谱文。
4. 从清单生成 id 开头的安全文件名，原子复制到 `falsums/`。
5. 冲突时追加编号，不静默覆盖。
6. 成功后选中新项目并显示导入结果；失败项保留在 `temp` 供诊断或重试，错误文本走本地化。

项目库内“导入”按钮继续使用 `ACTION_OPEN_DOCUMENT`，但结果同样进入上述验证和落库流程。外部分享／导出使用 `FileProvider` 或系统创建文档流，不暴露 `file://` URI。

## 8. 原有 SAF 与文档绑定迁移

Android 普通项目不再以外部 URI 作为编辑目标，也不再要求每次保存回写原文档提供方。`FalsumDocumentStore` 保留给：

- 从 SAF 或 Intent 导入的临时输入身份。
- 明确的外部“导出／分享”操作。
- 音频、封面和其他素材选择。

导入成功后，`FalsumPackage.SourcePath` 指向 `files/falsums` 内的真实本地路径，之后保存完全走 `System.IO` 原子替换。旧 `Falsum/Documents/<URI hash>/document.falsum` 工作副本不再作为项目库主项目。

## 9. 应用身份与构建

只在 Android 构建过程中覆盖：

```text
PlayerSettings.productName = "Falsum PE"
PlayerSettings Android application id = "cn.aaktsao.falsum.pe"
```

Android build 完成后恢复编辑器原设置，避免改变 PC 包。APK 清单必须验证新 application id、label、导入 Activity、Intent filters、FileProvider 和启动场景顺序。

包名改变后不能覆盖安装旧包；两者可并存。签名配置保持现有发布签名。

## 10. 失败与生命周期

- 扫描取消、场景切换和 Activity 重建不能重复导入同一队列项。
- 导入使用请求 id 和内容哈希去重；队列提交状态原子写入。
- 进程在复制期间被杀死时，未提交项下次启动重试或清理，不生成半个 `.falsum`。
- 编辑器被后台回收时，恢复快照保存在 `falsums/.recovery/`，项目库启动后先提示恢复，再打开项目。
- `temp/` 清理按年龄和总大小执行，不清理当前请求、失败候选和仍被恢复记录引用的文件。
- 项目库扫描不在主线程读取 ZIP、解码封面或计算哈希。
- Gameplay 场景加载失败时回到项目库并显示错误，不停留在黑屏。

## 11. 验证

至少覆盖：

1. 编译检查、完整 `FalsumSelfCheck`、本地化审计。
2. 路径自检：四个顶层目录、隐藏备份／恢复目录、Android 与 PC 分流、越界拒绝。
3. 项目库索引：正常包、无封面、四难度、损坏 ZIP、重复 songId、同名文件和取消扫描。
4. 新建：id 开头命名、安全标题、同名编号、失败不留空文件、回读完整包。
5. Intent：VIEW、SEND、SEND_MULTIPLE、冷启动、热启动、重复回调、无权限 URI、错误 MIME、伪造 ZIP、超大输入。
6. 场景：Android 首场景只含项目库；打开项目后 Gameplay 初始化指定包；返回项目库释放玩法对象；PC 首场景保持 Gameplay。
7. UI：横屏安全区、项目网格、详情抽屉、空库、扫描中、导入错误和加载遮罩截图。
8. APK：`cn.aaktsao.falsum.pe`、`Falsum PE`、Release IL2CPP、ARM64、签名、16 KB 对齐、Intent filters、原生桥和两个场景。
9. 真机：Made with Unity 后立即出现项目库；新建、保存、重启回读、分享导入、用其他应用打开、后台回收恢复及项目场景往返。

## 12. 设计自审

- 存储目录已按用户确认包含 `files/falsums`、`files/temp`、`files/externaltools`、`files/backgrounds`。
- 启动等待通过独立轻量场景解决，没有把重场景藏在项目库背后预加载。
- Android 项目以真实本地路径为主，URI 仅作为导入／导出通道。
- 新建文件名以 id 开头，但不破坏文件名与 songId 解耦的不变量。
- 恢复数据没有放入会被清理的缓存目录。
- PC 产品身份、启动场景和文件行为保持原样。
- 分享导入覆盖冷启动、热启动和重复消费。
- 没有占位符、TBD 或未裁决的核心行为。
