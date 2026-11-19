// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Editor/FalsumBuild.cs
// 行范围: 55-60
// 构建时强制安装 RendererFeature

                    ProtectedSongFontAssetPath);
            }
            FalsumUiAssetBuilder.EnsureAssets();
            FalsumTrackEndComposerInstaller.EnsureInstalled(true);
            GameplaySceneBuilder.SanitizeNoteTemplatePrefabs();
            FalsumAppIcon.Apply();
