// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Gameplay/Presentation/Backgrounds/FalsumBackgroundPresenter.cs
// 行范围: 128-150
// 创建并 Initialize Composer（唯一实例化点）

            _backgroundMaterial.SetFloat("_IgnoreVertexColor", 1.0f);
            _backgroundMaterial.renderQueue = BackgroundRenderQueue;
            _backgroundRenderer.sharedMaterial = _backgroundMaterial;
            _backgroundObject.gameObject.SetActive(true);
            _backgroundRenderer.enabled = true;
            _trackEndAnchor = binding?.TrackEndAnchor;
            _initialized = true;
            FitBackgroundToCamera();
            ApplyBlackBackground();
            _requestedBackgroundFile = null;
            CurrentBackgroundFile = string.Empty;
            if (_trackEndAnchor != null)
            {
                _trackEndComposer =
                    gameObject.AddComponent<FalsumTrackEndComposer>();
                _trackEndComposer.Initialize(_trackEndAnchor);
            }
            else
            {
                LastError = "Track-end anchor is missing.";
                Debug.LogError("[Falsum] " + LastError);
            }
        }
