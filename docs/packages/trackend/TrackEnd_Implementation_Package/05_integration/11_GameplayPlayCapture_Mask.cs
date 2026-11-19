// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Editor/GameplayPlayCapture.cs
// 行范围: 147-174
// 运行时截图导出 mask

                if (Environment.GetEnvironmentVariable(
                        "INFALSUS_CAPTURE_TRACK_END_MASK") == "1")
                {
                    var presenter =
                        UnityEngine.Object.FindFirstObjectByType<
                            FalsumBackgroundPresenter>(
                            FindObjectsInactive.Include);
                    var trackEnd = presenter != null
                        ? presenter.TrackEndComposer
                        : null;
                    var maskPath = Path.Combine(
                        directory,
                        prefix + "track_end_mask.png");
                    if (trackEnd != null &&
                        trackEnd.SaveMaskForDiagnostics(maskPath))
                    {
                        Debug.Log(
                            "[InFalsus] TrackEnd mask capture: " +
                            maskPath);
                    }
                }
                if (Environment.GetEnvironmentVariable(
                        "INFALSUS_CAPTURE_SKY_MASK") == "1")
                {
                    var effect =
                        UnityEngine.Object.FindFirstObjectByType<
                            SkyJudgementLineEffect>(
                            FindObjectsInactive.Include);
