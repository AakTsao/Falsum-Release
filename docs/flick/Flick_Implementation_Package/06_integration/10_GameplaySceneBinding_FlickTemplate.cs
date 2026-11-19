// ==== 片段：★ flick 模板与 burst 模板的绑定（Require 抛错而非静默） ====
// 源文件: Assets/InFalsus/Gameplay/Runtime/SceneBinding/GameplaySceneBinding.cs
// 行范围: 223-248 （共 26 行；本片段为逐字摘录，未改代码）
// 说明  : FLICK_NOTE 是场景里的 flick 音符模板节点；FlickBurst_0 / FlickBurstR_0 是判定特效模板。
// ---------------------------------------------------------------------------

            }

            binding.FlickTemplate = Require(
                scene,
                "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/FLICK_NOTE");
            binding.SkyTemplate = Require(
                scene,
                "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/SKY");
            binding.NoteFXRadialTemplate = Require(
                scene,
                NoteFXRadialPath);
            binding.NoteFXHoldTemplate = Require(
                scene,
                NoteFXHoldPath);
            binding.NoteFXMainTemplate = Require(
                scene,
                NoteFXMainPath);
            binding.FlickBurstLeftTemplate = Require(
                scene,
                FlickBurstLeftPath);
            binding.FlickBurstRightTemplate = Require(
                scene,
                FlickBurstRightPath);
            return binding;
        }

