// ==== 片段：模板节点必须 SetActive(false) ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs
// 行范围: 288-298 （共 11 行；本片段为逐字摘录，未改代码）
// 说明  : 否则场景里会固定显示一个不动的 flick 音符。
// ---------------------------------------------------------------------------

                {
                    NoteTemplates[i].SetActive(false);
                }
            }
            if (FlickTemplate != null)
            {
                FlickTemplate.SetActive(false);
            }
            if (SkyTemplate != null)
            {
                SkyTemplate.SetActive(false);
