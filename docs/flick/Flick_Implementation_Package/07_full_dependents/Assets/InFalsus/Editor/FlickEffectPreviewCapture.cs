using System;
using System.Globalization;
using System.IO;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace InFalsus.EditorTools
{
    public static class FlickEffectPreviewCapture
    {
        private const string RuntimeScenePath =
            "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity";
        private const int Width = 1152;
        private const int Height = 720;

        private static readonly float[] NormalizedTimes =
        {
            0.0f,
            0.04f,
            0.08f,
            0.12f,
            0.18f,
            0.26f,
            0.36f,
            0.48f,
            0.62f,
            0.78f,
            1.0f,
        };

        [MenuItem("InFalsus/Gameplay/Capture FlickBurst Preview")]
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

            var source = GameplaySceneBinding.FindByPath(
                scene,
                GameplaySceneBinding.FlickBurstRightPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "FlickBurstR preview source is missing.");
            }

            var effect = UnityEngine.Object.Instantiate(
                source,
                source.transform.parent,
                false);
            effect.name = "Preview_FlickBurstR";
            effect.transform.localPosition = new Vector3(
                0.81f,
                source.transform.localPosition.y,
                source.transform.localPosition.z);
            effect.transform.localRotation = source.transform.localRotation;
            effect.transform.localScale = source.transform.localScale;
            effect.SetActive(true);

            var animator = effect.GetComponent<Animator>();
            var spriteRenderer = effect.GetComponent<SpriteRenderer>();
            if (animator == null ||
                spriteRenderer == null ||
                animator.runtimeAnimatorController == null)
            {
                throw new InvalidOperationException(
                    "FlickBurstR preview components are incomplete.");
            }

            var shader = Shader.Find(
                "InFalsus/Compat/StenciledUnlitSprite");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "FlickBurst preview shader is missing.");
            }
            var material = new Material(shader)
            {
                name = "FlickBurstPreview",
                renderQueue = 3015,
            };
            material.SetFloat(
                "_ZTest",
                (float)CompareFunction.Always);
            spriteRenderer.sharedMaterial = material;
            spriteRenderer.color = Color.white;

            var camera = EnsureCamera();
            var clip = animator.runtimeAnimatorController.animationClips[0];
            var root = Path.GetDirectoryName(Application.dataPath) ??
                       string.Empty;
            var output = Path.Combine(
                root,
                "Diagnostics",
                "Screenshots");
            Directory.CreateDirectory(output);

            AnimationMode.StartAnimationMode();
            try
            {
                for (var i = 0; i < NormalizedTimes.Length; i++)
                {
                    var normalized = NormalizedTimes[i];
                    var time = clip.length * normalized;
                    AnimationMode.SampleAnimationClip(
                        effect,
                        clip,
                        time);
                    var path = Path.Combine(
                        output,
                        "flick_preview_r_" +
                        i.ToString("00", CultureInfo.InvariantCulture) +
                        "_" +
                        normalized.ToString(
                            "0.00",
                            CultureInfo.InvariantCulture) +
                        ".png");
                    Capture(camera, path);
                    Debug.Log(
                        "[InFalsus] Flick preview: " + path +
                        " time=" + time.ToString("0.000") +
                        " sprite=" +
                        (spriteRenderer.sprite != null
                            ? spriteRenderer.sprite.name
                            : "<null>"));
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(effect);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static Camera EnsureCamera()
        {
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            var camera = cameras.Length > 0 ? cameras[0] : null;
            if (camera == null)
            {
                var go = new GameObject("FlickPreviewCamera");
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
            return camera;
        }

        private static void Capture(Camera camera, string path)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(
                Width,
                Height,
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
                Width,
                Height,
                TextureFormat.RGB24,
                false);
            image.ReadPixels(
                new Rect(0, 0, Width, Height),
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
    }
}
