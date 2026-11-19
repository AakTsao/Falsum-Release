using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Runtime.Bootstrap;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace InFalsus.EditorTools
{
    public static class GameplaySceneBuilder
    {
        // The sanitized source scene was consolidated into the runtime scene,
        // so rebuilding opens and rewrites the same asset idempotently.
        private const string RuntimeScenePath =
            "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity";
        private const string CleanScenePath = RuntimeScenePath;
        private const string BackgroundTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/background.png";
        private const string PhaseABackgroundMaterialPath =
            "Assets/InFalsus/Gameplay/Recovered/Materials/PhaseABackground.mat";
        private const string TrackWallTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/notes_track_matid_trackwall_AlbedoTransparency.png";
        private const string TrackWallAltTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/notes_track_matid_trackwall_alt_AlbedoTransparency.png";
        private const string TrackFloorTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/notes_track_matid_trackfloor_AlbedoTransparency.png";
        private const string LaneTerminalTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/laneterminals.png";
        private const string HoldPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/notehold floor blue v2.prefab";
        private const string FlickLeftPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/v5-3.prefab";
        private const string FlickRightPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/v5-3R.prefab";
        private const string TapPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/note single blue.prefab";
        private const string TapSpanPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/note span blue.prefab";
        private const string SideLeftTapPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/NoteSideLeftTapPrefab.prefab";
        private const string SideRightTapPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/NoteSideRightTapPrefab.prefab";
        private const string SideLeftHoldPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/NoteSideLeftHoldPrefab.prefab";
        private const string SideRightHoldPrefabPath =
            "Assets/InFalsus/Recovered/GameObject/NoteSideRightHoldPrefab.prefab";
        private const string SkyAreaMaterialPath =
            "Assets/InFalsus/Recovered/Material/SkyArea Variant.mat";
        private const string GameplayBottomLaneShaderPath =
            "Assets/InFalsus/Recovered/Shader/Shader Graphs_GameplayBottomLane.shader";
        private const string PhaseALaneMaterialPath =
            "Assets/InFalsus/Gameplay/Recovered/Materials/PhaseALane.mat";
        private const string PhaseAWallLeftMaterialPath =
            "Assets/InFalsus/Gameplay/Recovered/Materials/PhaseAWallLeft.mat";
        private const string PhaseAWallRightMaterialPath =
            "Assets/InFalsus/Gameplay/Recovered/Materials/PhaseAWallRight.mat";
        private const string FloorMaterialPath =
            "Assets/InFalsus/Recovered/Material/matid_trackfloor1.mat";
        private const string LeftWallMaterialPath =
            "Assets/InFalsus/Recovered/Material/matid_trackwall1.mat";
        private const string RightWallMaterialPath =
            "Assets/InFalsus/Recovered/Material/matid_trackwall1_alt.mat";
        private const string SongClipPath =
            "Assets/InFalsus/Gameplay/Audio/Resources/alamode.ogg";
        private const string JudgementExactPlusTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/judgement-01-ExactPlus.png";
        private const string JudgementExactTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/judgement-02-Exact.png";
        private const string JudgementNearTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/judgement-03-Near.png";
        private const string JudgementBreakTexturePath =
            "Assets/InFalsus/Recovered/Texture2D/judgement-04-Break.png";
        private const string JudgementFeedbackMaterialPath =
            "Assets/InFalsus/Recovered/judgement-02-Exact.mat";
        private const string ChartFileName = "alamode_4.chart.txt";
        private static readonly string[] NoteTemplatePrefabPaths =
        {
            HoldPrefabPath,
            FlickLeftPrefabPath,
            FlickRightPrefabPath,
            TapPrefabPath,
            TapSpanPrefabPath,
            SideLeftTapPrefabPath,
            SideRightTapPrefabPath,
            SideLeftHoldPrefabPath,
            SideRightHoldPrefabPath,
        };
        private static readonly string[] PrunableRuntimeObjectPaths =
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
            "Scene/JudgementTextContainer",
            "Scene/IntroHandPopupContainer",
            "Scene/RightAssistModePlayerContainer",
            "Scene/LeftAssistModePlayerContainer",
            "Scene/RightLaneChangeIndicatorContainer",
            "Scene/LeftLaneChangeIndicatorContainer",
            "Scene/Backgrounds/BackgroundDiamondContainer",
            "Scene/EffectsContainer/DamageAA-Core",
            "BackgroundDiamondRendererContainer",
            "FauxTextContainer-BaiJamjuree-Bold",
            "FauxTextContainer-BaiJamjuree-Light",
            "FauxTextContainer-BaiJamjuree-Medium",
            "FauxTextContainer-BaiJamjuree-Regular",
            "FauxTextContainer-BaiJamjuree-SemiBold",
            "FauxTextContainer-Multi-Bold",
            "FauxTextContainer-Multi-Regular",
            "FauxTextContainer-Numbers-Regular",
        };

        [MenuItem("InFalsus/Gameplay/Build Runtime Scene")]
        public static void BuildRuntimeScene()
        {
            var scene = EditorSceneManager.OpenScene(
                CleanScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException(
                    "Failed to open " + CleanScenePath);
            }

            ApplyPhaseAPolicy(scene);
            RemoveMissingScripts();
            PruneUnusedRuntimeObjects(scene);
            AssignBackgroundMaterial(scene);
            AssignTrackMaterials(scene);
            EnsureCamera();
            var bootstrap = EnsureBootstrap();
            bootstrap.chartFileName = ChartFileName;
            bootstrap.songTitle = "alamode";
            bootstrap.difficulty = "4";
            bootstrap.audioOffsetMs = 0.0;
            bootstrap.speedPref = 500;
            bootstrap.autoPlayForDiagnostics = true;
            // The scene's SKY mesh already carries the original SkyArea material.
            // Using it avoids cloning a prefab whose dummy MonoBehaviour is gone.
            bootstrap.skyAreaPrefab = null;
            bootstrap.skyAreaMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                SkyAreaMaterialPath);
            bootstrap.holdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HoldPrefabPath);
            bootstrap.flickLeftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FlickLeftPrefabPath);
            bootstrap.flickRightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FlickRightPrefabPath);
            bootstrap.tapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                TapPrefabPath);
            bootstrap.tapSpanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                TapSpanPrefabPath);
            bootstrap.sideLeftTapPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SideLeftTapPrefabPath);
            bootstrap.sideRightTapPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SideRightTapPrefabPath);
            bootstrap.sideLeftHoldPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SideLeftHoldPrefabPath);
            bootstrap.sideRightHoldPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SideRightHoldPrefabPath);
            bootstrap.songClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                SongClipPath);
            bootstrap.judgementExactPlusTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    JudgementExactPlusTexturePath);
            bootstrap.judgementExactTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    JudgementExactTexturePath);
            bootstrap.judgementNearTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    JudgementNearTexturePath);
            bootstrap.judgementBreakTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    JudgementBreakTexturePath);
            bootstrap.judgementFeedbackMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    JudgementFeedbackMaterialPath);

            if (bootstrap.songClip == null)
            {
                throw new InvalidOperationException(
                    "Song clip is missing: " + SongClipPath);
            }
            if (bootstrap.skyAreaMaterial == null ||
                bootstrap.holdPrefab == null ||
                bootstrap.flickLeftPrefab == null ||
                bootstrap.flickRightPrefab == null ||
                bootstrap.tapPrefab == null ||
                bootstrap.tapSpanPrefab == null ||
                bootstrap.sideLeftTapPrefab == null ||
                bootstrap.sideRightTapPrefab == null ||
                bootstrap.sideLeftHoldPrefab == null ||
                bootstrap.sideRightHoldPrefab == null ||
                bootstrap.judgementExactPlusTexture == null ||
                bootstrap.judgementExactTexture == null ||
                bootstrap.judgementNearTexture == null ||
                bootstrap.judgementBreakTexture == null ||
                bootstrap.judgementFeedbackMaterial == null)
            {
                throw new InvalidOperationException(
                    "One or more gameplay presentation assets are missing.");
            }

            PlayerSettings.defaultScreenWidth = 1152;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.runInBackground = true;

            if (!EditorSceneManager.SaveScene(scene, RuntimeScenePath))
            {
                throw new InvalidOperationException(
                    "Failed to save " + RuntimeScenePath);
            }
            AssetDatabase.SaveAssets();
            AddRuntimeSceneToBuild();
            WriteBindingReport(scene);
            Debug.Log("[InFalsus] Built runtime scene: " + RuntimeScenePath);
        }

        public static void SanitizeNoteTemplatePrefabs()
        {
            var removed = 0;
            for (var i = 0; i < NoteTemplatePrefabPaths.Length; i++)
            {
                var path = NoteTemplatePrefabPaths[i];
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    throw new InvalidOperationException(
                        "Failed to load note template prefab: " + path);
                }
                try
                {
                    var gameObjects =
                        root.GetComponentsInChildren<Transform>(true);
                    for (var j = 0; j < gameObjects.Length; j++)
                    {
                        removed +=
                            GameObjectUtility
                                .RemoveMonoBehavioursWithMissingScript(
                                    gameObjects[j].gameObject);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[InFalsus] Removed missing scripts from note templates: " +
                removed);
        }

        [MenuItem("InFalsus/Gameplay/Report Scene Bindings")]
        public static void ReportOnly()
        {
            var scene = EditorSceneManager.OpenScene(
                CleanScenePath,
                OpenSceneMode.Single);
            WriteBindingReport(scene);
        }

        private static void ApplyPhaseAPolicy(Scene scene)
        {
            foreach (var path in GameplaySceneBinding.DefaultHiddenPaths)
            {
                var target = GameplaySceneBinding.FindByPath(scene, path);
                if (target != null)
                {
                    target.SetActive(false);
                }
            }

            var gameplay = GameplaySceneBinding.FindByPath(
                scene,
                GameplaySceneBinding.GameplayPath);
            if (gameplay != null)
            {
                gameplay.SetActive(false);
            }
        }

        private static void RemoveMissingScripts()
        {
            var gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var gameObject in gameObjects)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            }
        }

        private static void PruneUnusedRuntimeObjects(Scene scene)
        {
            var removed = 0;
            for (var i = 0; i < PrunableRuntimeObjectPaths.Length; i++)
            {
                var target = GameplaySceneBinding.FindByPath(
                    scene,
                    PrunableRuntimeObjectPaths[i]);
                if (target == null)
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(target);
                removed++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                "[InFalsus] Pruned runtime-only scene roots: " + removed);
        }

        private static void AssignBackgroundMaterial(Scene scene)
        {
            var background = GameplaySceneBinding.FindByPath(
                scene,
                "Scene/Backgrounds/BackgroundObject");
            if (background == null)
            {
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                BackgroundTexturePath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    "Background texture is missing: " + BackgroundTexturePath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(
                PhaseABackgroundMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("InFalsus/Compat/StenciledUnlitSprite");
                if (shader == null)
                {
                    shader = AssetDatabase.LoadAssetAtPath<Shader>(
                        "Assets/InFalsus/Shaders/Compat/StenciledUnlitSprite.shader");
                }
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "Background shader is missing.");
                }
                material = new Material(shader)
                {
                    name = "PhaseABackground",
                };
                AssetDatabase.CreateAsset(material, PhaseABackgroundMaterialPath);
            }
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_IgnoreVertexColor", 1.0f);
            EditorUtility.SetDirty(material);

            var renderer = background.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = background.GetComponentInChildren<Renderer>(true);
            }
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void AssignTrackMaterials(Scene scene)
        {
            var laneMaterial = CreateOrUpdateLaneMaterial(
                PhaseALaneMaterialPath,
                TrackFloorTexturePath,
                LaneTerminalTexturePath);
            var leftWallMaterial = CreateOrUpdateWallMaterial(
                PhaseAWallLeftMaterialPath,
                LeftWallMaterialPath);
            var rightWallMaterial = CreateOrUpdateWallMaterial(
                PhaseAWallRightMaterialPath,
                RightWallMaterialPath);

            for (var i = 1; i <= 4; i++)
            {
                AssignRendererMaterial(
                    scene,
                    "GameCamera/master_scene_v10/MasterScenev10/MAIN_TRACK/LANE_" + i,
                    laneMaterial);
            }

            AssignRendererMaterial(
                scene,
                "GameCamera/master_scene_v10/MasterScenev10/SHIFT/SHIFT_2",
                leftWallMaterial);
            AssignRendererMaterial(
                scene,
                "GameCamera/master_scene_v10/MasterScenev10/SPACE/SPACE_2",
                rightWallMaterial);
        }

        private static Material CreateOrUpdateSurfaceMaterial(
            string assetPath,
            string texturePath,
            Color color,
            string shaderPath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    "Track texture is missing: " + texturePath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                var shader = LoadCompatShader(shaderPath);
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        "Compatible sprite shader is missing.");
                }
                material = new Material(shader)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            var desiredShader = LoadCompatShader(shaderPath);
            if (desiredShader != null && material.shader != desiredShader)
            {
                material.shader = desiredShader;
            }
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            material.SetFloat("_IgnoreVertexColor", 1.0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateOrUpdateWallMaterial(
            string assetPath,
            string sourcePath)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Wall material is missing: " + sourcePath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(source)
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            material.shader = source.shader;
            material.SetTexture(
                "_EnabledTexture",
                source.GetTexture("_EnabledTexture"));
            material.SetTexture(
                "_DisabledTexture",
                source.GetTexture("_DisabledTexture"));
            material.SetTexture("_TerminalTex", null);
            material.SetFloat("_IsFloorLane", 0.0f);
            material.SetFloat("_LaneIndex", 0.0f);
            material.SetFloat("_AlphaScale", 0.35f);
            material.SetFloat("_WallAlphaScale", 0.35f);
            material.SetFloat("_TerminalMix", 0.0f);
            material.SetFloat("_FadeStart", 0.95f);
            material.SetFloat("_FadeEnd", 1.0f);
            material.SetColor("_Color", new Color(0.0f, 0.0f, 0.0f, 0.0f));
            material.SetVector(
                "_JudgementLineWorldPosition",
                source.GetVector("_JudgementLineWorldPosition"));
            material.SetVector(
                "_EndOfTrackWorldPosition",
                source.GetVector("_EndOfTrackWorldPosition"));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateOrUpdateLaneMaterial(
            string assetPath,
            string texturePath,
            string terminalTexturePath)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(
                FloorMaterialPath);
            var terminal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                terminalTexturePath);
            if (source == null || terminal == null)
            {
                throw new InvalidOperationException(
                    "Lane material is missing: " + FloorMaterialPath + " / " +
                    terminalTexturePath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(source)
                {
                    name = "PhaseALane",
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            material.shader = source.shader;
            material.SetTexture(
                "_EnabledTexture",
                source.GetTexture("_EnabledTexture"));
            material.SetTexture(
                "_DisabledTexture",
                source.GetTexture("_DisabledTexture"));
            material.SetTexture("_TerminalTex", terminal);
            material.SetFloat("_IsFloorLane", 1.0f);
            material.SetFloat("_LaneIndex", 0.0f);
            material.SetFloat("_AlphaScale", 0.80f);
            material.SetFloat("_WallAlphaScale", 0.10f);
            material.SetFloat("_TerminalMix", 0.0f);
            material.SetFloat("_FadeStart", 0.95f);
            material.SetFloat("_FadeEnd", 1.0f);
            material.SetColor("_Color", new Color(0.0f, 0.0f, 0.0f, 0.0f));
            material.SetVector(
                "_JudgementLineWorldPosition",
                new Vector4(0.0f, 0.0f, -8.52f, 0.0f));
            material.SetVector(
                "_EndOfTrackWorldPosition",
                new Vector4(0.0f, 0.0f, 10.0f, 0.0f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader LoadCompatShader(string shaderPath)
        {
            if (!string.IsNullOrEmpty(shaderPath))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader != null)
                {
                    return shader;
                }
            }
            return Shader.Find("InFalsus/Compat/StenciledUnlitSprite");
        }

        private static void AssignRendererMaterial(
            Scene scene,
            string path,
            Material material)
        {
            var target = GameplaySceneBinding.FindByPath(scene, path);
            if (target == null)
            {
                return;
            }
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void EnsureCamera()
        {
            Camera camera = null;
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (cameras.Length > 0)
            {
                camera = cameras[0];
            }
            if (camera == null)
            {
                var go = new GameObject("CameraPerspective");
                go.tag = "MainCamera";
                camera = go.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(
                    new Vector3(0.0f, 0.0f, -10.0f),
                    Quaternion.identity);
            }

            camera.orthographic = false;
            camera.fieldOfView = 60.0f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 120.0f;
            camera.cullingMask = 32247;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;

            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null)
            {
                data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            data.renderPostProcessing = true;
        }

        private static GameplayBootstrap EnsureBootstrap()
        {
            var existing = UnityEngine.Object.FindObjectsByType<GameplayBootstrap>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (existing.Length > 0)
            {
                return existing[0];
            }

            var go = new GameObject("PhaseAGameplayBootstrap");
            return go.AddComponent<GameplayBootstrap>();
        }

        private static void AddRuntimeSceneToBuild()
        {
            var scenes = new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            scenes.RemoveAll(scene =>
                string.Equals(scene.path, RuntimeScenePath, StringComparison.Ordinal));
            scenes.Insert(0, new EditorBuildSettingsScene(RuntimeScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void WriteBindingReport(Scene scene)
        {
            var binding = GameplaySceneBinding.Resolve(scene);
            var snapshot = binding.CreateSnapshot();
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ??
                              string.Empty;
            var diagnostics = Path.Combine(projectRoot, "Diagnostics");
            Directory.CreateDirectory(diagnostics);

            var json = JsonUtility.ToJson(snapshot, true);
            File.WriteAllText(
                Path.Combine(diagnostics, "SceneBindings.json"),
                json,
                new UTF8Encoding(false));

            var report = new StringBuilder()
                .AppendLine("scene=" + scene.name)
                .AppendLine("scenePath=" + scene.path)
                .AppendLine("gameplay=" + snapshot.gameplay)
                .AppendLine("notesContainer=" + snapshot.notesContainer)
                .AppendLine("judgementLine=" + snapshot.judgementLine)
                .AppendLine("templateCount=" + snapshot.noteTemplates.Length)
                .AppendLine("keyRendererCount=" + snapshot.keyRenderers.Length);
            File.WriteAllText(
                Path.Combine(diagnostics, "SceneBindingReport.txt"),
                report.ToString(),
                new UTF8Encoding(false));

            Debug.Log(
                "[InFalsus] Scene binding report written. templates=" +
                snapshot.noteTemplates.Length +
                " renderers=" + snapshot.keyRenderers.Length);
        }
    }
}
