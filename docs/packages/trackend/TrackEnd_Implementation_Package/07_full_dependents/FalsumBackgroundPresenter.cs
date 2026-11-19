using System;
using System.Collections;
using System.IO;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Networking;

namespace InFalsus.Gameplay.Presentation.Backgrounds
{
    public sealed class FalsumBackgroundPresenter : MonoBehaviour
    {
        private const string BackgroundObjectPath =
            "Scene/Backgrounds/BackgroundObject";
        private const int BackgroundRenderQueue = 2900;

        private GameplaySceneBinding _binding;
        private Transform _backgroundObject;
        private Renderer _backgroundRenderer;
        private Material _backgroundMaterial;
        private Texture2D _backgroundTexture;
        private Transform _trackEndAnchor;
        private FalsumTrackEndComposer _trackEndComposer;
        private Coroutine _backgroundRoutine;
        private Vector3 _backgroundBaseScale;
        private int _backgroundGeneration;
        private string _requestedBackgroundFile;
        private bool _initialized;
        private bool _loadingPriorityRaised;
        private UnityEngine.ThreadPriority _previousLoadingPriority;

        /// <summary>
        /// Inputs of <see cref="FitBackgroundToCamera"/> that can actually
        /// change its result: the camera's aspect/FOV/depth inputs and the
        /// texture aspect. Everything else in that method (the trigonometry,
        /// the scale write, the two material writes) is recomputed only when
        /// one of these differs. Resolution changes and a new background
        /// texture therefore still take effect on the very next frame, while a
        /// steady camera costs one comparison instead of a full refit.
        /// </summary>
        private float _fitLastAspect = float.NaN;
        private float _fitLastFieldOfView = float.NaN;
        private float _fitLastForwardDepth = float.NaN;
        private float _fitLastTextureAspect = float.NaN;
        private Transform _fitLastCameraTransform;

        public string CurrentBackgroundFile { get; private set; }
        public string LastError { get; private set; }

        public bool BackgroundVisible
        {
            get
            {
                return _backgroundObject != null &&
                       _backgroundObject.gameObject.activeInHierarchy &&
                       _backgroundRenderer != null &&
                       _backgroundRenderer.enabled;
            }
        }

        public bool BackgroundTextureReady
        {
            get { return _backgroundTexture != null; }
        }

        public bool PlayfieldVisible
        {
            get
            {
                return _trackEndComposer != null &&
                       _trackEndComposer.Visible;
            }
        }

        public FalsumTrackEndComposer TrackEndComposer
        {
            get { return _trackEndComposer; }
        }

        public void Initialize(GameplaySceneBinding binding)
        {
            if (_initialized)
            {
                return;
            }
            _binding = binding;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            _backgroundObject = GameplaySceneBinding.FindByPath(
                scene,
                BackgroundObjectPath)?.transform;
            if (_backgroundObject == null)
            {
                LastError = "Gameplay background object is missing.";
                Debug.LogError("[Falsum] " + LastError);
                return;
            }

            _backgroundRenderer =
                _backgroundObject.GetComponent<Renderer>();
            if (_backgroundRenderer == null)
            {
                _backgroundRenderer =
                    _backgroundObject.GetComponentInChildren<Renderer>(true);
            }
            if (_backgroundRenderer == null)
            {
                LastError = "Gameplay background renderer is missing.";
                Debug.LogError("[Falsum] " + LastError);
                return;
            }

            _backgroundBaseScale = _backgroundObject.localScale;
            var source = _backgroundRenderer.sharedMaterial;
            _backgroundMaterial = source != null
                ? new Material(source)
                : CreateMaterial("Falsum_Background");
            if (_backgroundMaterial == null)
            {
                LastError = "Gameplay background shader is missing.";
                Debug.LogError("[Falsum] " + LastError);
                return;
            }
            _backgroundMaterial.SetTexture(
                "_MainTex",
                Texture2D.blackTexture);
            _backgroundMaterial.SetColor("_Color", Color.white);
            _backgroundMaterial.SetFloat("_IgnoreVertexColor", 1.0f);
            _backgroundMaterial.renderQueue = BackgroundRenderQueue;
            _backgroundRenderer.sharedMaterial = _backgroundMaterial;
            _backgroundObject.gameObject.SetActive(true);
            _backgroundRenderer.enabled = true;
            _trackEndAnchor = binding?.TrackEndAnchor;
            _initialized = true;
            FitBackgroundToCamera();
            ApplyBlackBackground();
            _requestedBackgroundFile = null;
            CurrentBackgroundFile = string.Empty;
            if (_trackEndAnchor != null)
            {
                _trackEndComposer =
                    gameObject.AddComponent<FalsumTrackEndComposer>();
                _trackEndComposer.Initialize(_trackEndAnchor);
            }
            else
            {
                LastError = "Track-end anchor is missing.";
                Debug.LogError("[Falsum] " + LastError);
            }
        }

        public void SetBackground(string backgroundFile)
        {
            if (!_initialized)
            {
                return;
            }

            var requested = string.IsNullOrWhiteSpace(backgroundFile)
                ? FalsumManifest.DefaultBackgroundFile
                : backgroundFile.Trim();
            if (!FalsumManifest.IsValidBackgroundFile(requested))
            {
                LastError =
                    "Invalid gameplay background path: " + requested;
                Debug.LogWarning("[Falsum] " + LastError);
                ApplyBlackBackground();
                CurrentBackgroundFile = requested;
                return;
            }
            if (string.Equals(
                    _requestedBackgroundFile,
                    requested,
                    StringComparison.Ordinal) &&
                (_backgroundRoutine != null ||
                 _backgroundTexture != null))
            {
                CurrentBackgroundFile = requested;
                return;
            }

            _requestedBackgroundFile = requested;
            CurrentBackgroundFile = requested;
            PreloadBackground(requested);
        }

        private void StartBackgroundLoad(string fileName)
        {
            var generation = ++_backgroundGeneration;
            RaiseLoadingPriority();
            if (_backgroundRoutine != null)
            {
                StopCoroutine(_backgroundRoutine);
            }
            _backgroundRoutine = StartCoroutine(
                LoadBackground(fileName, generation));
        }

        private void PreloadBackground(string fileName)
        {
            var startedAt =
                System.Diagnostics.Stopwatch.StartNew();
            var path = FalsumRuntimePaths.ResolveBackgroundFile(fileName);
            Texture2D texture = null;
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    throw new FileNotFoundException(
                        "Background file not found.",
                        fileName);
                }
                var bytes = File.ReadAllBytes(path);
                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false,
                    false);
                if (!ImageConversion.LoadImage(texture, bytes, true))
                {
                    throw new InvalidDataException(
                        "ImageConversion.LoadImage returned false.");
                }
                texture.name = "Falsum_Background_" + fileName;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                DestroyTexture(ref _backgroundTexture);
                _backgroundTexture = texture;
                _requestedBackgroundFile = fileName;
                CurrentBackgroundFile = fileName;
                _backgroundMaterial.SetTexture(
                    "_MainTex",
                    _backgroundTexture);
                _backgroundMaterial.SetColor("_Color", Color.white);
                LastError = string.Empty;
                FitBackgroundToCamera();
                Debug.Log(
                    "[Falsum] Background preload: " +
                    startedAt.ElapsedMilliseconds + "ms " +
                    texture.width + "x" + texture.height);
            }
            catch (Exception exception)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
                LastError =
                    "Gameplay background preload failed: " +
                    exception.Message;
                Debug.LogWarning("[Falsum] " + LastError);
                ApplyBlackBackground();
                StartBackgroundLoad(fileName);
            }
        }

        private void LateUpdate()
        {
            if (_initialized)
            {
                FitBackgroundToCamera();
            }
        }

        private void OnDestroy()
        {
            _backgroundGeneration++;
            RestoreLoadingPriority();
            DestroyMaterial(ref _backgroundMaterial);
            DestroyTexture(ref _backgroundTexture);
        }

        private IEnumerator LoadBackground(
            string fileName,
            int generation)
        {
            var path = FalsumRuntimePaths.ResolveBackgroundFile(fileName);
            Texture2D texture = null;
            string error = null;
            yield return LoadTexture(
                path,
                true,
                result => texture = result,
                message => error = message);

            if (generation != _backgroundGeneration)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
                yield break;
            }

            _backgroundRoutine = null;
            RestoreLoadingPriority();
            if (texture == null)
            {
                LastError =
                    "Gameplay background load failed: " + fileName +
                    (string.IsNullOrWhiteSpace(error)
                        ? string.Empty
                        : " (" + error + ")");
                Debug.LogWarning("[Falsum] " + LastError);
                ApplyBlackBackground();
                yield break;
            }

            DestroyTexture(ref _backgroundTexture);
            _backgroundTexture = texture;
            _backgroundTexture.name = "Falsum_Background_" + fileName;
            _backgroundMaterial.SetTexture("_MainTex", _backgroundTexture);
            _backgroundMaterial.SetColor("_Color", Color.white);
            LastError = string.Empty;
            FitBackgroundToCamera();
        }

        private IEnumerator LoadTexture(
            string path,
            bool nonReadable,
            Action<Texture2D> completed,
            Action<string> failed)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                failed?.Invoke("file not found: " + path);
                yield break;
            }

            using (var request = UnityWebRequestTexture.GetTexture(
                       new Uri(path).AbsoluteUri,
                       nonReadable))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed?.Invoke(request.error);
                    yield break;
                }
                var texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                {
                    failed?.Invoke("decoded texture is null");
                    yield break;
                }
                completed?.Invoke(texture);
            }
        }

        private void RaiseLoadingPriority()
        {
            if (_loadingPriorityRaised)
            {
                return;
            }
            _previousLoadingPriority =
                Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority =
                UnityEngine.ThreadPriority.High;
            _loadingPriorityRaised = true;
        }

        private void RestoreLoadingPriority()
        {
            if (!_loadingPriorityRaised)
            {
                return;
            }
            Application.backgroundLoadingPriority =
                _previousLoadingPriority;
            _loadingPriorityRaised = false;
        }

        private void FitBackgroundToCamera()
        {
            if (_backgroundObject == null ||
                _backgroundRenderer == null)
            {
                return;
            }
            var camera = Camera.main;
            if (camera == null)
            {
                // A camera that disappeared invalidates the memo, so the next
                // frame with a camera refits instead of trusting stale numbers.
                _fitLastCameraTransform = null;
                _fitLastAspect = float.NaN;
                return;
            }
            var forwardDepth = Vector3.Dot(
                _backgroundObject.position -
                camera.transform.position,
                camera.transform.forward);
            if (forwardDepth <= camera.nearClipPlane)
            {
                _fitLastCameraTransform = null;
                _fitLastAspect = float.NaN;
                return;
            }

            // Cheap change detection. The transform reference is part of the
            // key because a camera swap (or an editor camera becoming main)
            // must refit even when aspect and FOV happen to coincide.
            var textureAspect =
                _backgroundTexture != null &&
                _backgroundTexture.height > 0
                    ? (float)_backgroundTexture.width /
                      _backgroundTexture.height
                    : 0.0f;
            if (_fitLastAspect == camera.aspect &&
                _fitLastFieldOfView == camera.fieldOfView &&
                _fitLastForwardDepth == forwardDepth &&
                _fitLastTextureAspect == textureAspect &&
                _fitLastCameraTransform == camera.transform)
            {
                return;
            }
            _fitLastAspect = camera.aspect;
            _fitLastFieldOfView = camera.fieldOfView;
            _fitLastForwardDepth = forwardDepth;
            _fitLastTextureAspect = textureAspect;
            _fitLastCameraTransform = camera.transform;

            var worldHeight =
                2.0f * forwardDepth *
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            const float overscan = 1.04f;
            _backgroundObject.localScale = new Vector3(
                worldHeight * camera.aspect * overscan,
                worldHeight * overscan,
                _backgroundBaseScale.z);

            if (_backgroundTexture == null ||
                _backgroundMaterial == null ||
                _backgroundTexture.height <= 0)
            {
                return;
            }

            var scaleUv = Vector2.one;
            var offsetUv = Vector2.zero;
            if (camera.aspect > textureAspect)
            {
                scaleUv.y = textureAspect / camera.aspect;
                offsetUv.y = (1.0f - scaleUv.y) * 0.5f;
            }
            else
            {
                scaleUv.x = camera.aspect / textureAspect;
                offsetUv.x = (1.0f - scaleUv.x) * 0.5f;
            }
            _backgroundMaterial.SetTextureScale("_MainTex", scaleUv);
            _backgroundMaterial.SetTextureOffset("_MainTex", offsetUv);
        }

        private void ApplyBlackBackground()
        {
            DestroyTexture(ref _backgroundTexture);
            if (_backgroundMaterial != null)
            {
                _backgroundMaterial.SetTexture(
                    "_MainTex",
                    Texture2D.blackTexture);
                _backgroundMaterial.SetColor("_Color", Color.white);
            }
        }

        private static Material CreateMaterial(string name)
        {
            var shader = Shader.Find(
                "InFalsus/Compat/StenciledUnlitSprite");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            return shader != null
                ? new Material(shader)
                {
                    name = name,
                }
                : null;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            Destroy(texture);
            texture = null;
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }
            Destroy(material);
            material = null;
        }
    }
}

