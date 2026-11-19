// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs
// 行范围: 68-90
// TrackEndComposer 属性 + PlayfieldVisible

        {
            get
            {
                return _trackEndComposer != null &&
                       _trackEndComposer.Visible;
            }
        }

        public FalsumTrackEndComposer TrackEndComposer
        {
            get { return _trackEndComposer; }
        }

        public void Initialize(GameplaySceneBinding binding)
        {
            if (_initialized)
            {
                return;
            }
            _binding = binding;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            _backgroundObject = GameplaySceneBinding.FindByPath(
                scene,
