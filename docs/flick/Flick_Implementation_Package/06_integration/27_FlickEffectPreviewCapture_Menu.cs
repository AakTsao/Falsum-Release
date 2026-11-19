// ==== 片段：菜单入口：InFalsus/Gameplay/Capture FlickBurst Preview ====
// 源文件: Assets/InFalsus/Editor/FlickEffectPreviewCapture.cs
// 行范围: 35-60 （共 26 行；本片段为逐字摘录，未改代码）
// 说明  : 把 FlickBurstR_0 复制一份并播一遍，用来截图看特效。
// ---------------------------------------------------------------------------

        [MenuItem("InFalsus/Gameplay/Capture FlickBurst Preview")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(
                RuntimeScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException(
                    "Failed to open " + RuntimeScenePath);
            }

            var source = GameplaySceneBinding.FindByPath(
                scene,
                GameplaySceneBinding.FlickBurstRightPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "FlickBurstR preview source is missing.");
            }

            var effect = UnityEngine.Object.Instantiate(
                source,
                source.transform.parent,
                false);
            effect.name = "Preview_FlickBurstR";
