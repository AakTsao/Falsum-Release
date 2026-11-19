# Falsum PE Android 项目库实现计划

1. 建立 Android `files/{falsums,temp,externaltools,backgrounds}` 路径门面，迁移缓存、备份、恢复、工具和背景根。
2. 增加项目索引／导入／命名服务与一次性场景启动请求，并让编辑器消费指定项目或新建请求。
3. 增加项目库 UXML/USS、运行时控制器和轻量场景构建器；实现扫描、搜索、排序、详情、导入、新建、打开、删除和加载遮罩。
4. 增加 Android exported ImportActivity，处理 VIEW/SEND/SEND_MULTIPLE 并把 URI 流排入 `temp/incoming`。
5. Android 构建改为 `Falsum PE`、`cn.aaktsao.falsum.pe`，项目库场景在前、Gameplay 场景在后；PC 构建保持不变。
6. 补本地化、自检、移动截图、Manifest／场景／APK 校验，最后生成 Release ARM64 APK。
