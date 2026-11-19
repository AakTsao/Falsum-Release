using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InFalsus.Gameplay.Runtime.SceneBinding
{
    [Serializable]
    public sealed class RendererSnapshot
    {
        public string path;
        public string shader;
        public string material;
        public bool enabled;
    }

    [Serializable]
    public sealed class SceneBindingSnapshot
    {
        public string scene;
        public string gameCamera;
        public string masterScene;
        public string gameplay;
        public string notesContainer;
        public string judgementLine;
        public string[] lanes;
        public string[] noteTemplates;
        public string[] hiddenPaths;
        public float[] laneCenterX;
        public RendererSnapshot[] keyRenderers;
    }

    public sealed class GameplaySceneBinding
    {
        public const string GameCameraPath = "GameCamera";
        public const string MasterScenePath =
            "GameCamera/master_scene_v10/MasterScenev10";
        public const string GameplayPath =
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY";
        public const string NotesContainerPath = "GameCamera/NotesContainer";
        public const string ComboPath = "GameCamera/Combo";
        public const string ComboCountPath = "GameCamera/Combo/ComboCount";
        public const string ComboBackingPath = "GameCamera/Combo/Quad";
        public const string NoteFXRadialPath =
            "GameCamera/NotesContainer/NoteFX_Radial_0";
        public const string NoteFXHoldPath =
            "GameCamera/NotesContainer/NoteHoldFXV2_0";
        public const string NoteFXMainPath =
            "GameCamera/NotesContainer/NoteFX_Main_0";
        public const string JudgementLinePath =
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE";
        public const string TrackEndAnchorPath =
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/" +
            "JUDGE_SKY/v5-3R/Plane/PLANE_END";
        public const string FlickBurstLeftPath =
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/FlickBurst_0";
        public const string FlickBurstRightPath =
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/FlickBurstR_0";
        public const string LeftSideLanePath =
            "GameCamera/master_scene_v10/MasterScenev10/SHIFT/SHIFT_2";
        public const string RightSideLanePath =
            "GameCamera/master_scene_v10/MasterScenev10/SPACE/SPACE_2";

        public string SceneName { get; private set; }
        public Transform GameCamera { get; private set; }
        public Transform MasterScene { get; private set; }
        public Transform Gameplay { get; private set; }
        public Transform NotesContainer { get; private set; }
        public Transform ComboRoot { get; private set; }
        public Transform ComboCount { get; private set; }
        public Transform ComboBacking { get; private set; }
        public Transform JudgementLine { get; private set; }
        public Transform TrackEndAnchor { get; private set; }
        public Transform LeftSideLane { get; private set; }
        public Transform RightSideLane { get; private set; }
        public Transform[] LaneAnchors { get; private set; }
        public GameObject[] NoteTemplates { get; private set; }
        public GameObject FlickTemplate { get; private set; }
        public GameObject SkyTemplate { get; private set; }
        public GameObject NoteFXRadialTemplate { get; private set; }
        public GameObject NoteFXHoldTemplate { get; private set; }
        public GameObject NoteFXMainTemplate { get; private set; }
        public GameObject FlickBurstLeftTemplate { get; private set; }
        public GameObject FlickBurstRightTemplate { get; private set; }
        public GameObject SceneRoot { get; private set; }

        public float[] LaneCenterX
        {
            get
            {
                var values = new float[6];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = NoteTemplates[i] != null
                        ? NoteTemplates[i].transform.position.x
                        : 0.0f;
                }
                return values;
            }
        }

        private static readonly string[] LanePaths =
        {
            "GameCamera/master_scene_v10/MasterScenev10/FRAME/1_LEFT",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_1",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_2",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_3",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_4",
            "GameCamera/master_scene_v10/MasterScenev10/FRAME/5_RIGHT",
        };

        private static readonly string[] TemplatePaths =
        {
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/note_left",
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/note1",
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/note2",
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/note3",
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/note4",
            "GameCamera/master_scene_v10/MasterScenev10/GAMEPLAY/note_right",
        };

        public static readonly string[] DefaultHiddenPaths =
        {
            "Scene/LeftPlayerContainer",
            "Scene/RightPlayerContainer ",
            "Scene/OpponentCombatNumberContainer",
            "Scene/PlayerCombatNumberContainer",
            "Scene/StartingConflictPopupContainer",
            "Scene/StartingMeditationPopupContainer",
            "Scene/ResultsSplash",
            "Scene/ClearStateContainer",
            "Scene/FpsCounter",
            "Scene/PauseLayerContainer",
            "Scene/ScoreContainer",
            "Scene/SongInfoContainer",
            "Scene/EffectsContainer/DamageAA-Core",
            "Scene/JudgementTextContainer",
            "Scene/IntroHandPopupContainer",
            "Scene/RightAssistModePlayerContainer",
            "Scene/LeftAssistModePlayerContainer",
            "Scene/RightLaneChangeIndicatorContainer",
            "Scene/LeftLaneChangeIndicatorContainer",
            "Scene/Backgrounds/BackgroundDiamondContainer",
            "BackgroundDiamondRendererContainer",
            "GameCamera/cursor",
            "GameCamera/cursor_ghost",
            "GameCamera/Combo",
            "GameCamera/NotesContainer/TestSphere",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/Keybind3DContainer",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/Keybind3DContainer (1)",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/Keybind3DContainer (2)",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/Keybind3DContainer (3)",
            "GameCamera/master_scene_v10/MasterScenev10/SHIFT/LeftKeybind3DContainer",
            "GameCamera/master_scene_v10/MasterScenev10/SPACE/RightKeybind3DContainer",
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/Quad",
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/Quad (1)",
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/SKY",
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/v5-3",
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/v5-3R",
            "GameCamera/master_scene_v10/MasterScenev10/JUDGEMENT_LINE/JUDGE_SKY/SkyCursorExtrasContainer",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_1_HLIGHT",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_2_HLIGHT",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_3_HLIGHT",
            "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_4_HLIGHT",
            "GameCamera/master_scene_v10/MasterScenev10/SHIFT/SHIFT_HIGHLIGHT",
            "GameCamera/master_scene_v10/MasterScenev10/SPACE/SPACE_HIGHLIGHT",
            "GameCamera/master_scene_v10/MasterScenev10/SHIFT/LeftWall",
            "GameCamera/master_scene_v10/MasterScenev10/SPACE/RightWall",
            "FauxTextContainer-BaiJamjuree-Bold",
            "FauxTextContainer-BaiJamjuree-Light",
            "FauxTextContainer-BaiJamjuree-Medium",
            "FauxTextContainer-BaiJamjuree-Regular",
            "FauxTextContainer-BaiJamjuree-SemiBold",
            "FauxTextContainer-Multi-Bold",
            "FauxTextContainer-Multi-Regular",
            "FauxTextContainer-Numbers-Regular",
        };

        public static GameplaySceneBinding Resolve(Scene scene)
        {
            if (!scene.IsValid())
            {
                throw new InvalidOperationException("Gameplay scene is not valid.");
            }

            var binding = new GameplaySceneBinding
            {
                SceneName = scene.name,
                SceneRoot = Require(scene, "Scene"),
            };

            binding.GameCamera = Require(scene, GameCameraPath).transform;
            binding.MasterScene = Require(scene, MasterScenePath).transform;
            binding.Gameplay = Require(scene, GameplayPath).transform;
            binding.NotesContainer = Require(scene, NotesContainerPath).transform;
            binding.ComboRoot = FindByPath(scene, ComboPath) != null
                ? FindByPath(scene, ComboPath).transform
                : null;
            binding.ComboCount = FindByPath(scene, ComboCountPath) != null
                ? FindByPath(scene, ComboCountPath).transform
                : null;
            binding.ComboBacking = FindByPath(scene, ComboBackingPath) != null
                ? FindByPath(scene, ComboBackingPath).transform
                : null;
            binding.JudgementLine = Require(scene, JudgementLinePath).transform;
            binding.TrackEndAnchor =
                Require(scene, TrackEndAnchorPath).transform;
            binding.LeftSideLane =
                Require(scene, LeftSideLanePath).transform;
            binding.RightSideLane =
                Require(scene, RightSideLanePath).transform;

            binding.LaneAnchors = new Transform[LanePaths.Length];
            for (var i = 0; i < LanePaths.Length; i++)
            {
                binding.LaneAnchors[i] = Require(scene, LanePaths[i]).transform;
            }

            binding.NoteTemplates = new GameObject[TemplatePaths.Length];
            for (var i = 0; i < TemplatePaths.Length; i++)
            {
                binding.NoteTemplates[i] = Require(scene, TemplatePaths[i]);
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

        public GameObject TemplateForLane(int lane)
        {
            if (lane < 0 || lane >= NoteTemplates.Length)
            {
                throw new ArgumentOutOfRangeException("lane");
            }
            return NoteTemplates[lane];
        }

        public Transform SideLaneForLane(int lane)
        {
            if (lane == 0)
            {
                return LeftSideLane;
            }
            if (lane == 5)
            {
                return RightSideLane;
            }
            return null;
        }

        public void ApplyPhaseAPolicy()
        {
            foreach (var path in DefaultHiddenPaths)
            {
                var target = FindByPath(
                    SceneManager.GetActiveScene(),
                    path);
                if (target != null)
                {
                    target.SetActive(false);
                }
            }

            // Templates remain available for Instantiate even when inactive.
            for (var i = 0; i < NoteTemplates.Length; i++)
            {
                if (NoteTemplates[i] != null)
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
            }
            if (Gameplay != null)
            {
                Gameplay.gameObject.SetActive(false);
            }
        }

        public SceneBindingSnapshot CreateSnapshot()
        {
            var snapshot = new SceneBindingSnapshot
            {
                scene = SceneName,
                gameCamera = BuildPath(GameCamera),
                masterScene = BuildPath(MasterScene),
                gameplay = BuildPath(Gameplay),
                notesContainer = BuildPath(NotesContainer),
                judgementLine = BuildPath(JudgementLine),
                lanes = new string[LaneAnchors.Length],
                noteTemplates = new string[NoteTemplates.Length],
                hiddenPaths = DefaultHiddenPaths,
                laneCenterX = LaneCenterX,
            };

            for (var i = 0; i < LaneAnchors.Length; i++)
            {
                snapshot.lanes[i] = BuildPath(LaneAnchors[i]);
            }
            for (var i = 0; i < NoteTemplates.Length; i++)
            {
                snapshot.noteTemplates[i] = BuildPath(NoteTemplates[i].transform);
            }

            var renderers = new List<RendererSnapshot>();
            AddRendererSnapshots(renderers, JudgementLine);
            for (var i = 0; i < NoteTemplates.Length; i++)
            {
                AddRendererSnapshots(renderers, NoteTemplates[i].transform);
            }
            snapshot.keyRenderers = renderers.ToArray();
            return snapshot;
        }

        private static void AddRendererSnapshots(
            List<RendererSnapshot> output,
            Transform root)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var material = renderer.sharedMaterial;
                output.Add(new RendererSnapshot
                {
                    path = BuildPath(renderer.transform),
                    shader = material != null && material.shader != null
                        ? material.shader.name
                        : "<null>",
                    material = material != null ? material.name : "<null>",
                    enabled = renderer.enabled,
                });
            }
        }

        public static GameObject Require(Scene scene, string path)
        {
            var found = FindByPath(scene, path);
            if (found == null)
            {
                throw new InvalidOperationException(
                    "Required gameplay object is missing: " + path +
                    " (scene=" + scene.name + ")");
            }
            return found;
        }

        public static GameObject FindByPath(Scene scene, string path)
        {
            var split = path.Split('/');
            if (split.Length == 0)
            {
                return null;
            }

            GameObject root = null;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == split[0])
                {
                    root = roots[i];
                    break;
                }
            }
            if (root == null)
            {
                return null;
            }

            var current = root.transform;
            for (var i = 1; i < split.Length; i++)
            {
                Transform next = null;
                for (var childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    var child = current.GetChild(childIndex);
                    if (child.name == split[i])
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null)
                {
                    return null;
                }
                current = next;
            }
            return current.gameObject;
        }

        public static GameObject TryFindByPath(GameObject root, string relativePath)
        {
            if (root == null)
            {
                return null;
            }

            var split = relativePath.Split('/');
            var current = root.transform;
            var start = current.name == split[0] ? 1 : 0;
            for (var i = start; i < split.Length; i++)
            {
                Transform next = null;
                for (var childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    var child = current.GetChild(childIndex);
                    if (child.name == split[i])
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null)
                {
                    return null;
                }
                current = next;
            }
            return current.gameObject;
        }

        public static string BuildPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var parts = new List<string>();
            var current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
