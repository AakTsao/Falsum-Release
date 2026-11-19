// ==== 片段：Play 截图时按 PhaseA_FlickBurst_ 前缀统计/取证 ====
// 源文件: Assets/InFalsus/Editor/GameplayPlayCapture.cs
// 行范围: 252-279 （共 28 行；本片段为逐字摘录，未改代码）
// 说明  : 运行时特效对象统一叫 PhaseA_FlickBurst_Runtime / PhaseA_FlickBurst_*。
// ---------------------------------------------------------------------------

                    if (renderer.name.StartsWith(
                            "PhaseA_FlickBurst_",
                            StringComparison.Ordinal))
                    {
                        var spriteRenderer = renderer as SpriteRenderer;
                        var animator = renderer.GetComponent<Animator>();
                        var state = animator != null
                            ? animator.GetCurrentAnimatorStateInfo(0)
                            : default(AnimatorStateInfo);
                        Debug.Log("[InFalsus] FlickBurst: " +
                                  renderer.name +
                                  " sprite=" +
                                  (spriteRenderer != null &&
                                   spriteRenderer.sprite != null
                                      ? spriteRenderer.sprite.name
                                      : "<null>") +
                                  " state=" +
                                  (animator != null ? animator.name : "<null>") +
                                  " normalized=" +
                                  state.normalizedTime.ToString("F3") +
                                  " localPos=" +
                                  renderer.transform.localPosition +
                                  " worldBounds=" +
                                  renderer.bounds.center + "/" + size +
                                  " screen=" +
                                  camera.WorldToScreenPoint(
                                      renderer.bounds.center));
                    }
