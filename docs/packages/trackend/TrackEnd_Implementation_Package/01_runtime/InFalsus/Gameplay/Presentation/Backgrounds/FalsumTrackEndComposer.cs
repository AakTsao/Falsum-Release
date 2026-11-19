using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace InFalsus.Gameplay.Presentation.Backgrounds
{
    public sealed class FalsumTrackEndComposer : MonoBehaviour
    {
        private const string PlayfieldDirectory = "Falsum/Playfield";
        private const string PlayfieldFile = "playfieldBG.png";
        private const string SeedEnvironmentVariable =
            "INFALSUS_TRACK_END_SEED";
        private const string MaskShaderName =
            "InFalsus/FalsumTrackEndDiamondMask";
        private const string BlurShaderName =
            "InFalsus/FalsumTrackEndDiamondBlur";
        private const string OverlayShaderName =
            "InFalsus/FalsumTrackEndDiamondOverlay";
        private const string OutputObjectName =
            "Falsum_TrackEndDiamondOutput";
        private const string MaskRootName =
            "Falsum_TrackEndDiamondMaskResources";
        private const string LegacyOutputObjectName =
            "Falsum_TrackEndPlayfield";
        private const string LegacyMaskRootName =
            "Falsum_TrackEndMaskResources";
        private const int TriangleCount = 100;
        private const float TriangleScale = 0.5f;
        private const float HexagonScale = 2.0f;
        private const float BlurRange = 0.017f;
        private const float BlurScalar = 0.011f;
        private const int HiddenMaskLayer = 31;
        private const int OutputRenderQueue = 2950;
        private const float PlayfieldViewportX = 0.50f;
        private const float PlayfieldSizeScale = 0.80f;
        private const float PlayfieldWidthFraction = 0.40f;
        private const float PlayfieldHeightFraction = 0.23f;
        private const float PlayfieldMaximumHeightFraction = 0.82f;
        private const float PlayfieldSourceScaleMultiplier = 1.12f;
        private const float PreviewTrackScaleMultiplier =
            1.157f * 0.95f;
        private const float PreviewTrackEndScaleFraction = 1.0f;
        private const float PlayfieldScaleMultiplier =
            PlayfieldSourceScaleMultiplier *
            PreviewTrackEndScaleFraction /
            PreviewTrackScaleMultiplier;
        private static readonly int TimeOverrideId =
            Shader.PropertyToID("_TimeOverride");

        public const float OutputUvMax = 0.5f;

        private Transform _trackEndAnchor;
        private Texture2D _playfieldTexture;
        private RenderTexture _maskTexture;
        private RTHandle _maskHandle;
        private Material _triangleMaterial;
        private Material _hexagonMaterial;
        private Material _blurMaterial;
        private Material _outputMaterial;
        private Mesh _triangleMesh;
        private Mesh _hexagonMesh;
        private Mesh _outputMesh;
        private MeshRenderer _triangleRenderer;
        private MeshRenderer _hexagonRenderer;
        private GameObject _maskRoot;
        private GameObject _outputObject;
        private MeshRenderer _outputRenderer;
        private bool _initialized;
        private bool _resourcesReady;
        private float _externalScaleMultiplier = 1.0f;
        private float _screenTopInset;
        private double _presentationTimeSeconds;
        private bool _timeOverrideWarningLogged;

        public static FalsumTrackEndComposer Active { get; private set; }

        public bool Visible
        {
            get
            {
                return _initialized &&
                       _resourcesReady &&
                       _outputObject != null &&
                       _outputObject.activeInHierarchy &&
                       _outputRenderer != null &&
                       _outputRenderer.enabled;
            }
        }

        public bool IsReady
        {
            get
            {
                return _initialized &&
                       _resourcesReady &&
                       _maskTexture != null &&
                       _maskHandle != null;
            }
        }

        public Texture2D PlayfieldTexture
        {
            get { return _playfieldTexture; }
        }

        public RenderTexture MaskTexture
        {
            get { return _maskTexture; }
        }

        public RTHandle MaskRTHandle
        {
            get { return _maskHandle; }
        }

        public Material TriangleMaterial
        {
            get { return _triangleMaterial; }
        }

        public Material HexagonMaterial
        {
            get { return _hexagonMaterial; }
        }

        public Material BlurMaterial
        {
            get { return _blurMaterial; }
        }

        public MeshRenderer TriangleRenderer
        {
            get { return _triangleRenderer; }
        }

        public MeshRenderer HexagonRenderer
        {
            get { return _hexagonRenderer; }
        }

        public int TriangleLayoutHash { get; private set; }

        public int MaskWidth
        {
            get { return _maskTexture != null ? _maskTexture.width : 0; }
        }

        public int MaskHeight
        {
            get { return _maskTexture != null ? _maskTexture.height : 0; }
        }

        public float FramingScaleMultiplier
        {
            get
            {
                return PlayfieldScaleMultiplier *
                       _externalScaleMultiplier;
            }
        }

        public float ScreenTopInset
        {
            get { return _screenTopInset; }
        }

        public double PresentationTimeSeconds
        {
            get { return _presentationTimeSeconds; }
        }

        public void SetPresentationTime(double seconds)
        {
            _presentationTimeSeconds = Math.Max(0.0, seconds);
            if (_triangleMaterial == null)
            {
                return;
            }
            if (!_triangleMaterial.HasProperty(TimeOverrideId))
            {
                if (!_timeOverrideWarningLogged)
                {
                    _timeOverrideWarningLogged = true;
                    Debug.LogWarning(
                        "[Falsum] TrackEnd mask shader is missing " +
                        "_TimeOverride; falling back to Unity time.");
                }
                return;
            }
            _triangleMaterial.SetFloat(
                TimeOverrideId,
                (float)_presentationTimeSeconds);
        }

        public void SetPreviewFraming(
            float scaleMultiplier,
            float topInset)
        {
            var nextScale = Mathf.Max(0.1f, scaleMultiplier);
            var nextInset = Mathf.Clamp01(topInset);
            if (Mathf.Approximately(
                    nextScale,
                    _externalScaleMultiplier) &&
                Mathf.Approximately(nextInset, _screenTopInset))
            {
                return;
            }
            _externalScaleMultiplier = nextScale;
            _screenTopInset = nextInset;
            UpdateOutputPlacement();
        }

        public void Initialize(Transform trackEndAnchor)
        {
            if (_initialized)
            {
                return;
            }
            if (trackEndAnchor == null)
            {
                Debug.LogError(
                    "[Falsum] TrackEnd composer is missing its anchor.");
                return;
            }

            RemoveLegacyVisuals();
            _trackEndAnchor = trackEndAnchor;
            if (!LoadPlayfieldTexture() || !CreateMaterials())
            {
                return;
            }

            BuildMeshes();
            CreateMaskRenderers();
            CreateOutputQuad();
            var camera = Camera.main;
            EnsureRenderTextures(
                camera != null ? camera.pixelWidth : 1152);
            _initialized = true;
            Active = this;
            UpdateOutputPlacement();
        }

        public static Vector2Int GetMaskSize(int cameraPixelWidth)
        {
            var scale = Mathf.Clamp(
                cameraPixelWidth / 3840.0f,
                0.75f,
                1.0f);
            return new Vector2Int(
                Mathf.FloorToInt(1200.0f * scale),
                Mathf.FloorToInt(800.0f * scale));
        }

        public static int ComputeTriangleLayoutHash(int? seed)
        {
            var random = seed.HasValue
                ? new System.Random(seed.Value)
                : new System.Random();
            BuildTriangleLayout(
                random,
                new List<Vector3>(TriangleCount * 3),
                new List<Vector4>(TriangleCount * 3),
                new List<int>(TriangleCount * 3),
                out var hash);
            return hash;
        }

        public bool SaveMaskForDiagnostics(string path)
        {
            if (_maskTexture == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                RenderTexture.active = _maskTexture;
                copy = new Texture2D(
                    _maskTexture.width,
                    _maskTexture.height,
                    TextureFormat.RGBA32,
                    false,
                    true);
                copy.ReadPixels(
                    new Rect(
                        0,
                        0,
                        _maskTexture.width,
                        _maskTexture.height),
                    0,
                    0,
                    false);
                copy.Apply(false, false);
                File.WriteAllBytes(path, copy.EncodeToPNG());
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] TrackEnd mask diagnostics failed: " +
                    exception.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (copy != null)
                {
                    Destroy(copy);
                }
            }
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                if (_outputObject != null)
                {
                    _outputObject.SetActive(false);
                }
                return;
            }

            EnsureRenderTextures(camera.pixelWidth);
            UpdateOutputPlacement();
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }

            ReleaseRenderTextures();
            DestroyMaterial(ref _triangleMaterial);
            DestroyMaterial(ref _hexagonMaterial);
            DestroyMaterial(ref _blurMaterial);
            DestroyMaterial(ref _outputMaterial);
            DestroyMesh(ref _triangleMesh);
            DestroyMesh(ref _hexagonMesh);
            DestroyMesh(ref _outputMesh);
            DestroyGameObject(ref _maskRoot);
            DestroyGameObject(ref _outputObject);
            DestroyTexture(ref _playfieldTexture);
        }

        private void RemoveLegacyVisuals()
        {
            var children = GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                if (child == null || child == transform)
                {
                    continue;
                }
                if (child.name == LegacyOutputObjectName ||
                    child.name == LegacyMaskRootName)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
            }
        }

        private bool LoadPlayfieldTexture()
        {
            var path = Path.Combine(
                Application.streamingAssetsPath,
                PlayfieldDirectory,
                PlayfieldFile);
            if (!File.Exists(path))
            {
                Debug.LogError(
                    "[Falsum] TrackEnd playfield image is missing: " +
                    path);
                return false;
            }

            Texture2D texture = null;
            try
            {
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
                texture.name = "Falsum_TrackEndDiamondPlayfield";
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Mirror;
                _playfieldTexture = texture;
                return true;
            }
            catch (Exception exception)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
                Debug.LogError(
                    "[Falsum] TrackEnd playfield load failed: " +
                    exception.Message);
                return false;
            }
        }

        private bool CreateMaterials()
        {
            var maskShader = Shader.Find(MaskShaderName);
            if (maskShader == null)
            {
                Debug.LogError(
                    "[Falsum] TrackEnd mask shader is missing: " +
                    MaskShaderName);
                return false;
            }
            var blurShader = Shader.Find(BlurShaderName);
            if (blurShader == null)
            {
                Debug.LogError(
                    "[Falsum] TrackEnd blur shader is missing: " +
                    BlurShaderName);
                return false;
            }
            var overlayShader = Shader.Find(OverlayShaderName);
            if (overlayShader == null)
            {
                Debug.LogError(
                    "[Falsum] TrackEnd overlay shader is missing: " +
                    OverlayShaderName);
                return false;
            }

            _triangleMaterial = new Material(maskShader)
            {
                name = "Falsum_TrackEndDiamondTriangles",
            };
            _hexagonMaterial = new Material(maskShader)
            {
                name = "Falsum_TrackEndDiamondHexagon",
            };
            _blurMaterial = new Material(blurShader)
            {
                name = "Falsum_TrackEndDiamondBlur",
            };
            _outputMaterial = new Material(overlayShader)
            {
                name = "Falsum_TrackEndDiamondOverlay",
            };

            var blurScalar = BlurRange * BlurScalar;
            _blurMaterial.SetFloat("_VerticalBlur", blurScalar);
            _blurMaterial.SetFloat("_HorizontalBlur", blurScalar);
            _outputMaterial.SetTexture(
                "_BackingTexture",
                _playfieldTexture);
            _outputMaterial.SetTexture(
                "_BlurredTexture",
                Texture2D.blackTexture);
            _outputMaterial.renderQueue = OutputRenderQueue;
            SetPresentationTime(_presentationTimeSeconds);
            return true;
        }

        private void BuildMeshes()
        {
            _triangleMesh = BuildTriangleMesh(out var triangleHash);
            TriangleLayoutHash = triangleHash;
            _hexagonMesh = BuildHexagonMesh();
            _outputMesh = BuildOutputQuadMesh();
        }

        private Mesh BuildTriangleMesh(out int hash)
        {
            return BuildTriangleData(
                CreateTriangleRandom(),
                out hash);
        }

        private static Mesh BuildTriangleData(
            System.Random random,
            out int hash)
        {
            var vertices = new List<Vector3>(TriangleCount * 3);
            var uvs = new List<Vector4>(TriangleCount * 3);
            var indices = new List<int>(TriangleCount * 3);
            BuildTriangleLayout(
                random,
                vertices,
                uvs,
                indices,
                out hash);
            return CreateMesh(
                "Falsum_TrackEndDiamondTriangles",
                vertices,
                uvs,
                indices);
        }

        private static void BuildTriangleLayout(
            System.Random random,
            List<Vector3> vertices,
            List<Vector4> uvs,
            List<int> indices,
            out int hash)
        {
            hash = 17;
            for (var triangleIndex = 0;
                 triangleIndex < TriangleCount;
                 triangleIndex++)
            {
                var radius =
                    0.02f + (float)random.NextDouble() * TriangleScale;
                var angle =
                    (random.NextDouble() > 0.5 ? 0.5 : 1.5) *
                    Mathf.PI;
                var halfRadius = radius * 0.5f;

                for (var vertexIndex = 0;
                     vertexIndex < 3;
                     vertexIndex++)
                {
                    var vertexAngle =
                        angle + vertexIndex * Mathf.PI * 2.0f / 3.0f;
                    var vertex = new Vector3(
                        halfRadius *
                        Mathf.Sin((float)vertexAngle) *
                        2.0f / 3.0f,
                        halfRadius * Mathf.Cos((float)vertexAngle),
                        0.0f);
                    var vertexId = vertices.Count;
                    vertices.Add(vertex);
                    uvs.Add(new Vector4(
                        triangleIndex,
                        triangleIndex,
                        triangleIndex,
                        triangleIndex));
                    indices.Add(vertexId);
                    hash = unchecked(hash * 31 +
                        BitConverter.SingleToInt32Bits(vertex.x));
                    hash = unchecked(hash * 31 +
                        BitConverter.SingleToInt32Bits(vertex.y));
                }
            }
        }

        private static Mesh BuildHexagonMesh()
        {
            var vertices = new List<Vector3>(18);
            var indices = new List<int>(18);
            var radius = HexagonScale * 0.5f;
            for (var segment = 0; segment < 6; segment++)
            {
                var angle0 = Mathf.PI / 3.0f * segment;
                var angle1 = Mathf.PI / 3.0f * (segment + 1);
                var baseIndex = vertices.Count;
                vertices.Add(Vector3.zero);
                vertices.Add(new Vector3(
                    radius * Mathf.Sin(angle0) * 2.0f / 3.0f,
                    radius * Mathf.Cos(angle0),
                    0.0f));
                vertices.Add(new Vector3(
                    radius * Mathf.Sin(angle1) * 2.0f / 3.0f,
                    radius * Mathf.Cos(angle1),
                    0.0f));
                indices.Add(baseIndex);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 2);
            }
            return CreateMesh(
                "Falsum_TrackEndDiamondHexagon",
                vertices,
                null,
                indices);
        }

        private static Mesh BuildOutputQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "Falsum_TrackEndDiamondQuad",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0.0f),
                new Vector3(0.5f, 0.5f, 0.0f),
                new Vector3(0.5f, -0.5f, 0.0f),
                new Vector3(-0.5f, 0.5f, 0.0f),
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, OutputUvMax),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, OutputUvMax),
            });
            mesh.SetTriangles(new[]
            {
                0, 1, 2,
                1, 0, 3,
            }, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateMesh(
            string name,
            List<Vector3> vertices,
            List<Vector4> uvs,
            List<int> indices)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.MarkDynamic();
            mesh.SetVertices(vertices);
            if (uvs != null)
            {
                mesh.SetUVs(0, uvs);
            }
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CreateMaskRenderers()
        {
            _maskRoot = new GameObject(MaskRootName)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = HiddenMaskLayer,
            };
            _maskRoot.transform.SetParent(transform, false);

            _triangleRenderer = CreateMaskRenderer(
                _maskRoot.transform,
                "Triangles",
                _triangleMesh,
                _triangleMaterial);
            _hexagonRenderer = CreateMaskRenderer(
                _maskRoot.transform,
                "Hexagon",
                _hexagonMesh,
                _hexagonMaterial);
        }

        private static MeshRenderer CreateMaskRenderer(
            Transform parent,
            string name,
            Mesh mesh,
            Material material)
        {
            var gameObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = HiddenMaskLayer,
            };
            gameObject.transform.SetParent(parent, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            return renderer;
        }

        private void CreateOutputQuad()
        {
            _outputObject = new GameObject(OutputObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _trackEndAnchor.gameObject.layer,
            };
            _outputObject.transform.SetParent(transform, false);
            var filter = _outputObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _outputMesh;
            _outputRenderer = _outputObject.AddComponent<MeshRenderer>();
            _outputRenderer.sharedMaterial = _outputMaterial;
            _outputRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _outputRenderer.receiveShadows = false;
            _outputRenderer.lightProbeUsage = LightProbeUsage.Off;
            _outputRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            _outputRenderer.allowOcclusionWhenDynamic = false;
            _outputObject.SetActive(false);
        }

        private void EnsureRenderTextures(int cameraPixelWidth)
        {
            if (_outputMaterial == null)
            {
                return;
            }
            var size = GetMaskSize(cameraPixelWidth);
            if (_maskTexture != null &&
                _maskTexture.width == size.x &&
                _maskTexture.height == size.y)
            {
                _resourcesReady = true;
                return;
            }

            ReleaseRenderTextures();
            try
            {
                var descriptor = new RenderTextureDescriptor(
                    size.x,
                    size.y,
                    RenderTextureFormat.ARGB32,
                    0)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    depthBufferBits = 0,
                    dimension = TextureDimension.Tex2D,
                    msaaSamples = 1,
                };
                _maskTexture = new RenderTexture(descriptor)
                {
                    name = "Falsum_TrackEndDiamondMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _maskTexture.Create();
                _maskHandle = RTHandles.Alloc(_maskTexture);
                _outputMaterial.SetTexture(
                    "_BlurredTexture",
                    _maskTexture);
                _resourcesReady = true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[Falsum] TrackEnd mask RT creation failed: " +
                    exception.Message);
                ReleaseRenderTextures();
                _resourcesReady = false;
            }
        }

        private void UpdateOutputPlacement()
        {
            if (_outputObject == null || _trackEndAnchor == null)
            {
                return;
            }
            if (!IsReady)
            {
                _outputObject.SetActive(false);
                return;
            }

            var camera = Camera.main;
            if (camera == null || camera.orthographic)
            {
                _outputObject.SetActive(false);
                return;
            }

            var anchorViewport = camera.WorldToViewportPoint(
                _trackEndAnchor.position);
            if (anchorViewport.z <= camera.nearClipPlane)
            {
                _outputObject.SetActive(false);
                return;
            }
            _outputObject.SetActive(true);

            var depth = anchorViewport.z;
            var viewportWorldHeight =
                2.0f * depth *
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var effectiveScale =
                PlayfieldScaleMultiplier * _externalScaleMultiplier;
            var fullHeightFraction = Mathf.Min(
                PlayfieldMaximumHeightFraction,
                PlayfieldHeightFraction * camera.aspect) *
                PlayfieldSizeScale *
                effectiveScale;
            var visibleHeightFraction =
                fullHeightFraction * OutputUvMax;
            var worldHeight =
                viewportWorldHeight * visibleHeightFraction;
            var worldWidth =
                viewportWorldHeight * camera.aspect *
                PlayfieldWidthFraction *
                PlayfieldSizeScale *
                effectiveScale;
            var centerViewportY =
                1.0f -
                _screenTopInset -
                visibleHeightFraction * 0.5f;
            var center = camera.ViewportToWorldPoint(
                new Vector3(
                    PlayfieldViewportX,
                    centerViewportY,
                    depth));
            _outputObject.transform.SetPositionAndRotation(
                center,
                camera.transform.rotation);
            _outputObject.transform.localScale = new Vector3(
                worldWidth,
                worldHeight,
                1.0f);
        }

        private static System.Random CreateTriangleRandom()
        {
            var seedText = Environment.GetEnvironmentVariable(
                SeedEnvironmentVariable);
            return int.TryParse(seedText, out var seed)
                ? new System.Random(seed)
                : new System.Random();
        }

        private void ReleaseRenderTextures()
        {
            _resourcesReady = false;
            if (_outputMaterial != null)
            {
                _outputMaterial.SetTexture(
                    "_BlurredTexture",
                    Texture2D.blackTexture);
            }
            if (_maskHandle != null)
            {
                _maskHandle.Release();
                _maskHandle = null;
            }
            if (_maskTexture != null)
            {
                _maskTexture.Release();
                Destroy(_maskTexture);
                _maskTexture = null;
            }
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

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }
            Destroy(mesh);
            mesh = null;
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

        private static void DestroyGameObject(ref GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }
            Destroy(gameObject);
            gameObject = null;
        }
    }
}
