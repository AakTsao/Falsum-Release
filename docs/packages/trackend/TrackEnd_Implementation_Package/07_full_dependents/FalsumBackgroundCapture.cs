using System;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Runtime.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InFalsus.EditorTools
{
    public static class FalsumBackgroundCapture
    {
        private const string RuntimeScenePath =
            "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity";
        private const string ActiveKey =
            "InFalsus.FalsumBackgroundCapture.Active";
        private const string StartKey =
            "InFalsus.FalsumBackgroundCapture.Start";
        private const string StageKey =
            "InFalsus.FalsumBackgroundCapture.Stage";
        private const string StageStartKey =
            "InFalsus.FalsumBackgroundCapture.StageStart";
        private const int Width = 1152;
        private const int Height = 720;

        private static readonly StringBuilder ReportBuilder =
            new StringBuilder();
        private static int _stageCount;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("InFalsus/Falsum/Capture Backgrounds")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(
                RuntimeScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException(
                    "Failed to open " + RuntimeScenePath);
            }
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetFloat(
                StartKey,
                (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetFloat(
                StageStartKey,
                (float)EditorApplication.timeSinceStartup);
            ReportBuilder.Clear();
            _stageCount = 0;
            ReportBuilder.AppendLine("{");
            ReportBuilder.AppendLine("  \"stages\": [");
            Install();
            EditorApplication.isPlaying = true;
            Debug.Log("[Falsum] Background capture started.");
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var start = SessionState.GetFloat(StartKey, (float)now);
            if (now - start < 1.0)
            {
                return;
            }
            var stageStart = SessionState.GetFloat(
                StageStartKey,
                (float)now);
            var stage = SessionState.GetInt(StageKey, 0);
            if (now - stageStart < 1.5)
            {
                return;
            }

            try
            {
                var bootstrap =
                    UnityEngine.Object.FindFirstObjectByType<
                        GameplayBootstrap>(
                        FindObjectsInactive.Include);
                if (bootstrap == null || bootstrap.Backgrounds == null)
                {
                    throw new InvalidOperationException(
                        "Background presenter was not created.");
                }
                var editor =
                    UnityEngine.Object.FindFirstObjectByType<
                        FalsumChartEditor>(
                        FindObjectsInactive.Include);
                if (editor == null)
                {
                    throw new InvalidOperationException(
                        "Falsum editor was not created.");
                }
                var presenter = bootstrap.Backgrounds;
                if (stage == 0 &&
                    (!presenter.BackgroundTextureReady ||
                     !presenter.PlayfieldVisible))
                {
                    if (now - start < 90.0)
                    {
                        return;
                    }
                    throw new InvalidOperationException(
                        "Background load timed out. file=" +
                        presenter.CurrentBackgroundFile +
                        " ready=" + presenter.BackgroundTextureReady +
                        " playfield=" + presenter.PlayfieldVisible +
                        " error=" + presenter.LastError);
                }
                if (stage == 1 &&
                    (presenter.CurrentBackgroundFile !=
                     FalsumManifest.DefaultBackgroundFile ||
                     !presenter.BackgroundTextureReady ||
                     !presenter.PlayfieldVisible))
                {
                    if (now - stageStart < 30.0)
                    {
                        return;
                    }
                    throw new InvalidOperationException(
                        "Default background load timed out. file=" +
                        presenter.CurrentBackgroundFile +
                        " ready=" + presenter.BackgroundTextureReady +
                        " error=" + presenter.LastError);
                }
                var directory = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ??
                    Directory.GetCurrentDirectory(),
                    "Diagnostics",
                    "Screenshots");
                Directory.CreateDirectory(directory);

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
                    AppendStage(
                        presenter.CurrentBackgroundFile,
                        true,
                        true);
                    bootstrap.SetGameplayBackground(string.Empty);
                    AdvanceStage(now);
                    return;
                }
                if (stage == 1)
                {
                    Require(
                        presenter.CurrentBackgroundFile ==
                        FalsumManifest.DefaultBackgroundFile,
                        "Default background was not selected.");
                    Capture(
                        Path.Combine(
                            directory,
                            "falsum_background_default.png"));
                    AppendStage(
                        presenter.CurrentBackgroundFile,
                        presenter.BackgroundVisible,
                        presenter.PlayfieldVisible);
                    bootstrap.SetGameplayBackground("../invalid.png");
                    AdvanceStage(now);
                    return;
                }
                if (stage == 2)
                {
                    Require(
                        !string.IsNullOrWhiteSpace(presenter.LastError),
                        "Invalid background did not produce a warning.");
                    Require(
                        presenter.CurrentBackgroundFile ==
                        "../invalid.png",
                        "Invalid background request was not retained.");
                    Capture(
                        Path.Combine(
                            directory,
                            "falsum_background_invalid.png"));
                    AppendStage(
                        presenter.CurrentBackgroundFile,
                        presenter.BackgroundVisible,
                        presenter.PlayfieldVisible);
                    Finish(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ReportBuilder.AppendLine(
                    "    {\"error\": \"" +
                    Escape(exception.Message) + "\"}");
                Finish(1);
            }
        }

        private static void AppendStage(
            string background,
            bool backgroundVisible,
            bool playfieldVisible)
        {
            if (_stageCount == 0)
            {
                ReportBuilder.AppendLine("    {");
            }
            else
            {
                ReportBuilder.AppendLine("    ,{");
            }
            _stageCount++;
            ReportBuilder.AppendLine(
                "      \"background\": \"" +
                Escape(background) + "\",");
            ReportBuilder.AppendLine(
                "      \"backgroundVisible\": " +
                (backgroundVisible ? "true" : "false") + ",");
            ReportBuilder.AppendLine(
                "      \"playfieldVisible\": " +
                (playfieldVisible ? "true" : "false"));
            ReportBuilder.AppendLine("    }");
            Debug.Log(
                "[Falsum] Background capture: " + background +
                " visible=" + backgroundVisible +
                " playfield=" + playfieldVisible);
        }

        private static void LogLineRenderers()
        {
            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer.name.StartsWith(
                        "FalsumEditorGrid",
                        StringComparison.Ordinal) &&
                    !renderer.name.StartsWith(
                        "PhaseA_MeasureLines",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                var filter = renderer.GetComponent<MeshFilter>();
                Debug.Log(
                    "[Falsum] Line renderer: " + renderer.name +
                    " enabled=" + renderer.enabled +
                    " visible=" + renderer.isVisible +
                    " queue=" +
                    (renderer.sharedMaterial != null
                        ? renderer.sharedMaterial.renderQueue
                        : -1) +
                    " vertices=" +
                    (filter != null && filter.sharedMesh != null
                        ? filter.sharedMesh.vertexCount
                        : -1) +
                    " bounds=" + renderer.bounds);
            }
        }

        private static void Capture(string path)
        {
            var width = GetCaptureDimension(
                "INFALSUS_CAPTURE_WIDTH",
                Width);
            var height = GetCaptureDimension(
                "INFALSUS_CAPTURE_HEIGHT",
                Height);
            var camera = Camera.main;
            if (camera == null)
            {
                throw new InvalidOperationException(
                    "Background capture found no active camera.");
            }
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousAspect = camera.aspect;
            var renderTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
            };
            renderTexture.Create();
            camera.targetTexture = renderTexture;
            camera.aspect = (float)width / height;
            RenderTexture.active = renderTexture;
            camera.Render();
            var image = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            RenderTexture.active = previousActive;
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            Debug.Log("[Falsum] Background screenshot: " + path);
        }

        private static int GetCaptureDimension(
            string environmentVariable,
            int fallback)
        {
            var text = Environment.GetEnvironmentVariable(
                environmentVariable);
            return int.TryParse(text, out var value) && value > 0
                ? value
                : fallback;
        }

        private static void AdvanceStage(double now)
        {
            SessionState.SetInt(StageKey, SessionState.GetInt(StageKey, 0) + 1);
            SessionState.SetFloat(StageStartKey, (float)now);
        }

        private static void Finish(int exitCode)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            ReportBuilder.AppendLine("  ],");
            ReportBuilder.AppendLine(
                "  \"passed\": " +
                (exitCode == 0 ? "true" : "false"));
            ReportBuilder.AppendLine("}");
            var directory = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ??
                Directory.GetCurrentDirectory(),
                "Diagnostics");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(
                    directory,
                    "FalsumBackgroundCapture.json"),
                ReportBuilder.ToString(),
                new UTF8Encoding(false));
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(exitCode);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
