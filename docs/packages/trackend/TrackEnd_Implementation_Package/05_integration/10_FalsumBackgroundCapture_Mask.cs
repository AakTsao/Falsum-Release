// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Editor/FalsumBackgroundCapture.cs
// 行范围: 148-182
// 截图流程与 mask 导出（INFALSUS_CAPTURE_TRACK_END_MASK）


                if (stage == 0)
                {
                    Require(
                        presenter.CurrentBackgroundFile ==
                        editor.DebugBackgroundFile,
                        "Package background was not selected. actual=" +
                        presenter.CurrentBackgroundFile +
                        " expected=" + editor.DebugBackgroundFile);
                    Require(
                        presenter.BackgroundVisible,
                        "Gameplay background is not visible.");
                    Require(
                        presenter.PlayfieldVisible,
                        "Track-end playfield is not visible.");
                    if (Environment.GetEnvironmentVariable(
                            "INFALSUS_CAPTURE_TRACK_END_MASK") == "1")
                    {
                        var maskPath = Path.Combine(
                            directory,
                            "trackend_clean_mask.png");
                        Require(
                            presenter.TrackEndComposer != null &&
                            presenter.TrackEndComposer
                                .SaveMaskForDiagnostics(maskPath),
                            "Track-end mask capture failed.");
                        Debug.Log(
                            "[Falsum] TrackEnd mask capture: " +
                            maskPath);
                    }
                    Capture(
                        Path.Combine(
                            directory,
                            "falsum_background_alamode.png"));
                    LogLineRenderers();
