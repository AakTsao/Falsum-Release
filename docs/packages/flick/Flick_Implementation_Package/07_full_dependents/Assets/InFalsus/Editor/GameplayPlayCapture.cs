using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using InFalsus.Gameplay.Presentation.Backgrounds;
using InFalsus.Gameplay.Presentation.Notes;
using InFalsus.Gameplay.Runtime.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InFalsus.EditorTools
{
    public static class GameplayPlayCapture
    {
        private const string RuntimeScenePath =
            "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity";
        private const string ActiveKey = "InFalsus.Capture.Active";
        private const string StartKey = "InFalsus.Capture.Start";
        private const string IndexKey = "InFalsus.Capture.Index";
        private const string TimesKey = "InFalsus.Capture.Times";
        private const string PrefixKey = "InFalsus.Capture.Prefix";
        private const int DefaultWidth = 1152;
        private const int DefaultHeight = 720;

        private static readonly double[] DefaultCaptureTimes =
        {
            1.5, 3.0, 6.2, 7.0, 8.8,
        };

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("InFalsus/Gameplay/Capture Runtime Play")]
        public static void Run()
        {
            Environment.SetEnvironmentVariable(
                "INFALSUS_CAPTURE_PRESERVE_CHART",
                "1");
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
            SessionState.SetInt(IndexKey, 0);
            SessionState.SetString(
                TimesKey,
                Environment.GetEnvironmentVariable(
                    "INFALSUS_CAPTURE_TIMES") ??
                JoinTimes(DefaultCaptureTimes));
            SessionState.SetString(
                PrefixKey,
                Environment.GetEnvironmentVariable(
                    "INFALSUS_CAPTURE_PREFIX") ??
                "gameplay_runtime_t");
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<GameplayBootstrap>(
                FindObjectsInactive.Include);
            if (bootstrap != null)
            {
                bootstrap.autoPlayForDiagnostics =
                    Environment.GetEnvironmentVariable(
                        "INFALSUS_AUTOPLAY") != "0";
            }
            Install();
            EditorApplication.isPlaying = true;
            Debug.Log("[InFalsus] Entered runtime play mode for capture.");
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            var start = SessionState.GetFloat(StartKey, 0.0f);
            var index = SessionState.GetInt(IndexKey, 0);
            var elapsed = EditorApplication.timeSinceStartup - start;
            var captureTimes = ParseTimes(
                SessionState.GetString(
                    TimesKey,
                    JoinTimes(DefaultCaptureTimes)));
            var prefix = SessionState.GetString(
                PrefixKey,
                "gameplay_runtime_t");
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<GameplayBootstrap>(
                FindObjectsInactive.Include);
            var gameplayTime = bootstrap != null
                ? bootstrap.CurrentTimeMs
                : elapsed * 1000.0;
            if (index >= captureTimes.Length)
            {
                Finish(0);
                return;
            }
            if (gameplayTime < captureTimes[index] * 1000.0)
            {
                return;
            }

            try
            {
                var camera = Camera.main;
                if (camera == null)
                {
                    var cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);
                    camera = cameras.Length > 0 ? cameras[0] : null;
                }
                if (camera == null)
                {
                    throw new InvalidOperationException(
                        "Runtime capture found no active camera.");
                }

                var root = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
                var directory = Path.Combine(
                    root,
                    "Diagnostics",
                    "Screenshots");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(
                    directory,
                    prefix +
                    captureTimes[index].ToString(
                        "0.000",
                        CultureInfo.InvariantCulture) + ".png");
                Capture(camera, path);
                Debug.Log("[InFalsus] Runtime capture: " + path);
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
                    var maskPath = Path.Combine(
                        directory,
                        prefix + "sky_mask.png");
                    if (effect != null &&
                        effect.SaveMaskDiagnostics(maskPath))
                    {
                        Debug.Log(
                            "[InFalsus] SkyArea mask capture: " +
                            maskPath +
                            " ready=" + effect.IsReady +
                            " active=" + effect.RangeActive +
                            " range=" + effect.RangeLeft +
                            ".." + effect.RangeRight +
                            " size=" + effect.FullMaskTexture.width +
                            "x" + effect.FullMaskTexture.height);
                    }
                }
                var skyRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                var loggedSky = 0;
                foreach (var skyRenderer in skyRenderers)
                {
                    if (!skyRenderer.name.StartsWith(
                            "PhaseA_SkyArea_Group_",
                            StringComparison.Ordinal) ||
                        loggedSky >= 2)
                    {
                        continue;
                    }
                    loggedSky++;
                    var skyBlock = new MaterialPropertyBlock();
                    skyRenderer.GetPropertyBlock(skyBlock);
                    Debug.Log("[InFalsus] Sky capture check: " +
                              skyRenderer.name +
                              " layer=" + skyRenderer.gameObject.layer +
                              " visible=" + skyRenderer.isVisible +
                              " bounds=" + skyRenderer.bounds.center + "/" +
                              skyRenderer.bounds.size +
                              " camera=" + camera.name +
                              " camPos=" + camera.transform.position +
                              " camEuler=" + camera.transform.eulerAngles +
                              " mask=" + camera.cullingMask +
                              " visualProgress=" +
                              skyBlock.GetFloat(
                                  Shader.PropertyToID(
                                      "_VisualChartProgress")));
                }
                var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                var loggedFlickVertices = false;
                var loggedPlaneVertices = false;
                foreach (var renderer in allRenderers)
                {
                    var size = renderer.bounds.size;
                    var rendererPath =
                        InFalsus.Gameplay.Runtime.SceneBinding
                            .GameplaySceneBinding.BuildPath(
                                renderer.transform);
                    if (renderer.name.StartsWith(
                            "PhaseA_SkyArea_Prompt_",
                            StringComparison.Ordinal))
                    {
                        Debug.Log("[InFalsus] Sky prompt: " +
                                  renderer.name +
                                  " visible=" + renderer.isVisible +
                                  " localPos=" +
                                  renderer.transform.localPosition +
                                  " localScale=" +
                                  renderer.transform.localScale +
                                  " bounds=" + renderer.bounds.center +
                                  "/" + size +
                                  " screen=" +
                                  camera.WorldToScreenPoint(
                                      renderer.bounds.center));
                    }
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
                    if (renderer.name.StartsWith(
                            "hotehold_",
                            StringComparison.Ordinal))
                    {
                        Debug.Log("[InFalsus] Hold segment: " +
                                  renderer.name +
                                  " path=" + rendererPath +
                                  " localPos=" +
                                  renderer.transform.localPosition +
                                  " localScale=" +
                                  renderer.transform.localScale +
                                  " bounds=" + renderer.bounds.center +
                                  "/" + size);
                    }
                    if (rendererPath.IndexOf(
                            "PhaseA_Note_",
                            StringComparison.Ordinal) >= 0 &&
                        (renderer.name == "Flick" ||
                         renderer.name == "Plane"))
                    {
                        Debug.Log("[InFalsus] Flick segment: " +
                                  renderer.name +
                                  " path=" + rendererPath +
                                  " localPos=" +
                                  renderer.transform.localPosition +
                                  " localEuler=" +
                                  renderer.transform.localEulerAngles +
                                  " localScale=" +
                                  renderer.transform.localScale +
                                  " bounds=" + renderer.bounds.center +
                                  "/" + size);
                    }
                    if (!loggedFlickVertices &&
                        renderer.name == "Flick" &&
                        rendererPath.IndexOf(
                            "PhaseA_Note_",
                            StringComparison.Ordinal) >= 0)
                    {
                        loggedFlickVertices = true;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                        {
                            var vertices = filter.sharedMesh.vertices;
                            var flickRoot = renderer.transform.parent;
                            for (var i = 0; i < vertices.Length; i++)
                            {
                                var local = flickRoot.InverseTransformPoint(
                                    renderer.transform.TransformPoint(vertices[i]));
                                Debug.Log("[InFalsus] Flick vertex " +
                                          i + " path=" + rendererPath +
                                          " local=" + local);
                            }
                        }
                    }
                    if (!loggedPlaneVertices &&
                        renderer.name == "Plane" &&
                        rendererPath.IndexOf(
                            "PhaseA_Note_",
                            StringComparison.Ordinal) >= 0)
                    {
                        loggedPlaneVertices = true;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                        {
                            var vertices = filter.sharedMesh.vertices;
                            var flickRoot = renderer.transform.parent;
                            for (var i = 0; i < vertices.Length; i++)
                            {
                                var local = flickRoot.InverseTransformPoint(
                                    renderer.transform.TransformPoint(vertices[i]));
                                Debug.Log("[InFalsus] Plane vertex " +
                                          i + " path=" + rendererPath +
                                          " local=" + local);
                            }
                        }
                    }
                    var isQuad = renderer.name.IndexOf(
                        "Quad", StringComparison.OrdinalIgnoreCase) >= 0;
                    var isGuide = renderer.sharedMaterial != null &&
                                  renderer.sharedMaterial.name.IndexOf(
                                      "Guide",
                                      StringComparison.OrdinalIgnoreCase) >= 0;
                    if (size.magnitude < 2.0f && !isQuad && !isGuide)
                    {
                        continue;
                    }
                    Debug.Log("[InFalsus] Large renderer: " + renderer.name +
                              " path=" +
                              InFalsus.Gameplay.Runtime.SceneBinding
                                  .GameplaySceneBinding.BuildPath(
                                      renderer.transform) +
                              " type=" + renderer.GetType().Name +
                              " material=" +
                              (renderer.sharedMaterial != null
                                  ? renderer.sharedMaterial.name
                                  : "<null>") +
                              " bounds=" + renderer.bounds.center + "/" +
                              size);
                }
                if (bootstrap != null)
                {
                    Debug.Log("[InFalsus] Runtime state: " +
                              bootstrap.DiagnosticSummary);
                }
                SessionState.SetInt(IndexKey, index + 1);
                if (index + 1 >= captureTimes.Length)
                {
                    Finish(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static string JoinTimes(double[] times)
        {
            var parts = new string[times.Length];
            for (var i = 0; i < times.Length; i++)
            {
                parts[i] = times[i].ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);
            }
            return string.Join(",", parts);
        }

        private static double[] ParseTimes(string value)
        {
            var parsed = new List<double>();
            var parts = (value ?? string.Empty).Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                double time;
                if (double.TryParse(
                        parts[i].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out time) &&
                    time >= 0.0)
                {
                    parsed.Add(time);
                }
            }
            return parsed.Count > 0
                ? parsed.ToArray()
                : DefaultCaptureTimes;
        }

        private static void Finish(int exitCode)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(exitCode);
        }

        private static void Capture(Camera camera, string path)
        {
            var width = ReadDimension(
                "INFALSUS_CAPTURE_WIDTH",
                DefaultWidth);
            var height = ReadDimension(
                "INFALSUS_CAPTURE_HEIGHT",
                DefaultHeight);
            var skyEffect =
                UnityEngine.Object.FindFirstObjectByType<
                    SkyJudgementLineEffect>(
                    FindObjectsInactive.Include);
            if (skyEffect != null)
            {
                skyEffect.PrepareForCapture(width);
            }
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
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
            RenderTexture.active = renderTexture;
            camera.Render();

            var image = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);
            image.ReadPixels(
                new Rect(0, 0, width, height),
                0,
                0);
            image.Apply();

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            File.WriteAllBytes(path, image.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(image);
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }

        private static int ReadDimension(
            string variableName,
            int fallback)
        {
            var value = Environment.GetEnvironmentVariable(
                variableName);
            return int.TryParse(value, out var parsed) &&
                   parsed > 0
                ? parsed
                : fallback;
        }
    }
}

