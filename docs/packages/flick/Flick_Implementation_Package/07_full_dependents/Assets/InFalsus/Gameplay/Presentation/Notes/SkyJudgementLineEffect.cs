using System;
using System.Diagnostics;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering;

namespace InFalsus.Gameplay.Presentation.Notes
{
    public sealed class SkyJudgementLineEffect : MonoBehaviour
    {
        public const int Rows = 8;
        public const int Columns = 90;
        public const int TrianglesPerRow = Columns * 2 + 1;
        public const int TriangleCount = Rows * TrianglesPerRow;

        private const string GlowShaderName =
            "InFalsus/Replica/SkyJudgementLineGlow";
        private const int HiddenMaskLayer = 31;
        private const int BufferCount = 4;
        private const float BlurAmount = 0.0001f;
        private const float MinimumMaskScale = 0.75f;
        private const float FadeOutSeconds = 0.3f;

        private static readonly int CenterXId =
            Shader.PropertyToID("_CenterX");
        private static readonly int WidthId =
            Shader.PropertyToID("_Width");
        private static readonly int OpacityId =
            Shader.PropertyToID("_Opacity");
        private static readonly int TriangleBoundaryId =
            Shader.PropertyToID("_TriangleBoundary");
        private static readonly int DownscaledGridId =
            Shader.PropertyToID("_DownscaledGrid");
        private static readonly int DownscaledBlurredGridId =
            Shader.PropertyToID("_DownscaledBlurredGrid");
        private static readonly int OpacityBufferId =
            Shader.PropertyToID("_TriId_To_Opacity");
        private static readonly int HorizontalBlurId =
            Shader.PropertyToID("_HorizontalBlur");
        private static readonly int VerticalBlurId =
            Shader.PropertyToID("_VerticalBlur");

        private Material _glowMaterial;
        private Material _maskMaterial;
        private Material _blurMaterial;
        private Mesh _triangleMesh;
        private MeshRenderer _triangleRenderer;
        private GameObject _maskObject;
        private GraphicsBuffer[] _opacityBuffers;
        private RenderTexture _fullMaskTexture;
        private RenderTexture _filteredMaskTexture;
        private RenderTexture _blurredMaskTexture;
        private RTHandle _fullMaskHandle;
        private RTHandle _filteredMaskHandle;
        private RTHandle _blurredMaskHandle;
        private float[] _opacity;
        private float[] _lastFlash;
        private float[] _triangleU;
        private float[] _rowDistance;
        private int _bufferIndex;
        private float _rangeLeft;
        private float _rangeRight;
        private bool _rangeActive;
        private bool _hasEverBeenActive;
        private bool _opacityAtRest = true;
        private double _lastActiveTime;
        private bool _initialized;

        public static SkyJudgementLineEffect Active { get; private set; }

        public bool IsReady
        {
            get
            {
                return _initialized && HasRequiredResources();
            }
        }

        public bool ShouldRender
        {
            get
            {
                if (!IsReady)
                {
                    return false;
                }
                if (_rangeActive)
                {
                    return true;
                }
                if (!_hasEverBeenActive)
                {
                    return false;
                }
                var now =
                    Stopwatch.GetTimestamp() /
                    (double)Stopwatch.Frequency;
                return now - _lastActiveTime < FadeOutSeconds;
            }
        }

        public Renderer TriangleRenderer
        {
            get { return _triangleRenderer; }
        }

        public Material TriangleMaterial
        {
            get { return _maskMaterial; }
        }

        public Material BlurMaterial
        {
            get { return _blurMaterial; }
        }

        public RenderTexture FullMaskTexture
        {
            get { return _fullMaskTexture; }
        }

        public RenderTexture FilteredMaskTexture
        {
            get { return _filteredMaskTexture; }
        }

        public RenderTexture BlurredMaskTexture
        {
            get { return _blurredMaskTexture; }
        }

        public RTHandle FullMaskRTHandle
        {
            get { return _fullMaskHandle; }
        }

        public RTHandle FilteredMaskRTHandle
        {
            get { return _filteredMaskHandle; }
        }

        public RTHandle BlurredMaskRTHandle
        {
            get { return _blurredMaskHandle; }
        }

        public float RangeLeft
        {
            get { return _rangeLeft; }
        }

        public float RangeRight
        {
            get { return _rangeRight; }
        }

        public bool RangeActive
        {
            get { return _rangeActive; }
        }

        public void Initialize(GameplaySceneBinding binding)
        {
            if (_initialized)
            {
                return;
            }
            if (binding == null ||
                binding.SkyTemplate == null ||
                binding.SkyTemplate.transform.parent == null)
            {
                UnityEngine.Debug.LogError(
                    "[Falsus] Sky judgement line effect is missing " +
                    "the JUDGE_SKY binding.");
                return;
            }

            var judgeSky = binding.SkyTemplate.transform.parent;
            if (!CreateMaterials(judgeSky) ||
                !BuildTriangleMesh() ||
                !CreateOpacityBuffers())
            {
                return;
            }

            CreateMaskRenderer(judgeSky);
            EnsureRenderTextures(Camera.main);
            _initialized = HasRequiredResources();
            if (_initialized)
            {
                Active = this;
            }
            else
            {
                UnityEngine.Debug.LogError(
                    "[Falsus] Sky judgement line effect failed to " +
                    "create its runtime resources.");
            }
        }

        public void BeginFrame()
        {
            if (!_initialized)
            {
                return;
            }
            EnsureRenderTextures(Camera.main);
            _rangeActive = false;
        }

        public void PrepareForCapture(int cameraPixelWidth)
        {
            if (_initialized && cameraPixelWidth > 0)
            {
                EnsureRenderTextures(cameraPixelWidth);
            }
        }

        public void SetRange(float left, float right)
        {
            if (!_initialized || _glowMaterial == null)
            {
                return;
            }
            left = Mathf.Clamp01(left);
            right = Mathf.Clamp01(right);
            if (right <= left)
            {
                return;
            }
            _rangeLeft = left;
            _rangeRight = right;
            _rangeActive = true;
            _opacityAtRest = false;
            _glowMaterial.SetFloat(CenterXId, (left + right) * 0.5f);
            _glowMaterial.SetFloat(WidthId, right - left);
        }

        public void EndFrame()
        {
            if (!_initialized)
            {
                return;
            }

            var now =
                Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (_rangeActive)
            {
                var highFrequencyPulse =
                    (Mathf.Sin((float)now * 109.0f) * 0.5f + 0.5f) *
                    0.1f;
                var lowFrequencyPulse =
                    (Mathf.Sin((float)now * 34.0f) * 0.5f + 0.5f) *
                    0.1f;
                _glowMaterial.SetFloat(
                    OpacityId,
                    1.0f - highFrequencyPulse - lowFrequencyPulse);
                _lastActiveTime = now;
                _hasEverBeenActive = true;
            }
            else
            {
                var fadeProgress = _hasEverBeenActive
                    ? (float)((now - _lastActiveTime) /
                              FadeOutSeconds)
                    : 1.0f;
                if (fadeProgress >= 1.0f)
                {
                    if (!_opacityAtRest)
                    {
                        _glowMaterial.SetFloat(OpacityId, 0.0f);
                        _rangeLeft = 0.0f;
                        _rangeRight = 0.0f;
                        _opacityAtRest = true;
                    }
                    return;
                }
                var opacity = Mathf.Lerp(
                    1.0f,
                    0.0f,
                    fadeProgress);
                _glowMaterial.SetFloat(OpacityId, opacity);
                _rangeLeft = 0.0f;
                _rangeRight = 0.0f;
            }

            UpdateOpacityBuffer(now);
        }

        public bool SaveMaskDiagnostics(string path)
        {
            if (_fullMaskTexture == null ||
                _filteredMaskTexture == null ||
                _blurredMaskTexture == null ||
                string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                RenderTexture.active = _filteredMaskTexture;
                copy = new Texture2D(
                    _filteredMaskTexture.width,
                    _filteredMaskTexture.height,
                    TextureFormat.RGBA32,
                    false,
                    true);
                copy.ReadPixels(
                    new Rect(
                        0,
                        0,
                        _filteredMaskTexture.width,
                        _filteredMaskTexture.height),
                    0,
                    0,
                    false);
                copy.Apply(false, false);
                System.IO.File.WriteAllBytes(
                    path,
                    copy.EncodeToPNG());
                return true;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(
                    "[Falsus] Sky judgement mask diagnostics failed: " +
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

        private bool CreateMaterials(Transform judgeSky)
        {
            var glowShader = Shader.Find(GlowShaderName);
            var maskShader = Resources.Load<Shader>(
                "SkyArea/Shaders/SkyJudgementLineMask");
            var blurShader = Resources.Load<Shader>(
                "SkyArea/Shaders/SkyJudgementLineBlur");
            if (glowShader == null ||
                maskShader == null ||
                blurShader == null)
            {
                UnityEngine.Debug.LogError(
                    "[Falsus] Sky judgement line shaders are missing.");
                return false;
            }

            var glowTransform = judgeSky.Find("SkyJudgementLineGlow");
            MeshRenderer glowRenderer = null;
            if (glowTransform != null)
            {
                glowRenderer =
                    glowTransform.GetComponent<MeshRenderer>();
            }
            if (glowRenderer == null)
            {
                var glowObject =
                    new GameObject("SkyJudgementLineGlow");
                glowObject.transform.SetParent(judgeSky, false);
                glowObject.transform.localPosition = Vector3.zero;
                glowObject.transform.localRotation =
                    Quaternion.Euler(24.329994f, 0.0f, 0.0f);
                glowObject.transform.localScale =
                    new Vector3(2.0f, 0.144f, 1.0f);
                var filter =
                    glowObject.AddComponent<MeshFilter>();
                filter.sharedMesh =
                    Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                glowRenderer =
                    glowObject.AddComponent<MeshRenderer>();
            }

            _glowMaterial = glowRenderer.sharedMaterial != null
                ? new Material(glowRenderer.sharedMaterial)
                : new Material(glowShader);
            _glowMaterial.name = "SkyJudgementLineGlow Runtime";
            _glowMaterial.shader = glowShader;
            _glowMaterial.renderQueue = 3011;
            _glowMaterial.SetFloat(TriangleBoundaryId, 0.95f);
            _glowMaterial.SetFloat(CenterXId, 0.0f);
            _glowMaterial.SetFloat(WidthId, 0.0f);
            _glowMaterial.SetFloat(OpacityId, 0.0f);
            glowRenderer.sharedMaterial = _glowMaterial;
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
            glowRenderer.lightProbeUsage = LightProbeUsage.Off;
            glowRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            glowRenderer.enabled = true;

            _maskMaterial = new Material(maskShader)
            {
                name = "SkyJudgementLineTriangleMask",
            };
            _blurMaterial = new Material(blurShader)
            {
                name = "SkyJudgementLineMaskBlur",
            };
            _blurMaterial.SetFloat(HorizontalBlurId, BlurAmount);
            _blurMaterial.SetFloat(VerticalBlurId, BlurAmount);
            return true;
        }

        private bool BuildTriangleMesh()
        {
            var vertices = new Vector3[TriangleCount * 3];
            var indices = new int[TriangleCount * 3];
            var vertexIndex = 0;
            var triangleIndex = 0;
            var cellX = 1.0f / Columns;
            var cellY = 1.0f / Rows;

            for (var row = 0; row < Rows; row++)
            {
                var top = 1.0f - row * cellY;
                var bottom = top - cellY;
                var left = 0.0f;
                var mid = cellX * 0.5f;

                if ((row & 1) == 0)
                {
                    AddTriangle(
                        vertices,
                        indices,
                        ref vertexIndex,
                        ref triangleIndex,
                        new Vector3(left, top, 0.0f),
                        new Vector3(mid, bottom, 0.0f),
                        new Vector3(left + cellX, top, 0.0f));
                    left += cellX;

                    for (var column = 0;
                         column < Columns;
                         column++)
                    {
                        AddTriangle(
                            vertices,
                            indices,
                            ref vertexIndex,
                            ref triangleIndex,
                            new Vector3(mid, bottom, 0.0f),
                            new Vector3(left, top, 0.0f),
                            new Vector3(
                                mid + cellX,
                                bottom,
                                0.0f));
                        mid += cellX;
                        AddTriangle(
                            vertices,
                            indices,
                            ref vertexIndex,
                            ref triangleIndex,
                            new Vector3(left, top, 0.0f),
                            new Vector3(mid, bottom, 0.0f),
                            new Vector3(
                                left + cellX,
                                top,
                                0.0f));
                        left += cellX;
                    }
                }
                else
                {
                    AddTriangle(
                        vertices,
                        indices,
                        ref vertexIndex,
                        ref triangleIndex,
                        new Vector3(left, bottom, 0.0f),
                        new Vector3(mid, top, 0.0f),
                        new Vector3(left + cellX, bottom, 0.0f));
                    left += cellX;

                    for (var column = 0;
                         column < Columns;
                         column++)
                    {
                        AddTriangle(
                            vertices,
                            indices,
                            ref vertexIndex,
                            ref triangleIndex,
                            new Vector3(mid, top, 0.0f),
                            new Vector3(left, bottom, 0.0f),
                            new Vector3(
                                mid + cellX,
                                top,
                                0.0f));
                        mid += cellX;
                        AddTriangle(
                            vertices,
                            indices,
                            ref vertexIndex,
                            ref triangleIndex,
                            new Vector3(left, bottom, 0.0f),
                            new Vector3(mid, top, 0.0f),
                            new Vector3(
                                left + cellX,
                                bottom,
                                0.0f));
                        left += cellX;
                    }
                }
            }

            if (triangleIndex != TriangleCount)
            {
                UnityEngine.Debug.LogError(
                    "[Falsus] Sky judgement triangle mesh has an " +
                    "unexpected triangle count.");
                return false;
            }

            _triangleMesh = new Mesh
            {
                name = "SkyJudgementLineTriangleGrid",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _triangleMesh.MarkDynamic();
            _triangleMesh.vertices = vertices;
            _triangleMesh.SetTriangles(indices, 0, true);
            _triangleMesh.RecalculateBounds();

            _opacity = new float[TriangleCount];
            _lastFlash = new float[TriangleCount];
            _triangleU = new float[TriangleCount];
            _rowDistance = new float[TriangleCount];
            var rowMidpoint = (Rows - 1) * 0.5f;
            for (var triangle = 0;
                 triangle < TriangleCount;
                 triangle++)
            {
                var withinRow = triangle % TrianglesPerRow;
                var row = triangle / TrianglesPerRow;
                _triangleU[triangle] =
                    (float)withinRow / TrianglesPerRow;
                _rowDistance[triangle] =
                    Mathf.Abs(rowMidpoint - row) / (Rows - 1.0f);
            }
            return true;
        }

        private static void AddTriangle(
            Vector3[] vertices,
            int[] indices,
            ref int vertexIndex,
            ref int triangleIndex,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            vertices[vertexIndex] = a;
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
            vertices[vertexIndex] = b;
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
            vertices[vertexIndex] = c;
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
            triangleIndex++;
        }

        private bool CreateOpacityBuffers()
        {
            _opacityBuffers = new GraphicsBuffer[BufferCount];
            for (var i = 0; i < _opacityBuffers.Length; i++)
            {
                _opacityBuffers[i] = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    TriangleCount,
                    sizeof(float));
                var zeros = new float[TriangleCount];
                _opacityBuffers[i].SetData(zeros);
                _opacityBuffers[i].name =
                    "SkyJudgementLineOpacity" + i;
            }
            _maskMaterial.SetBuffer(
                OpacityBufferId,
                _opacityBuffers[0]);
            return true;
        }

        private void CreateMaskRenderer(Transform judgeSky)
        {
            _maskObject = new GameObject(
                "SkyJudgementLineMaskResources")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = HiddenMaskLayer,
            };
            _maskObject.transform.SetParent(judgeSky, false);
            _maskObject.transform.localPosition = Vector3.zero;
            _maskObject.transform.localRotation = Quaternion.identity;
            _maskObject.transform.localScale = Vector3.one;

            var filter = _maskObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _triangleMesh;
            _triangleRenderer =
                _maskObject.AddComponent<MeshRenderer>();
            _triangleRenderer.sharedMaterial = _maskMaterial;
            _triangleRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            _triangleRenderer.receiveShadows = false;
            _triangleRenderer.lightProbeUsage =
                LightProbeUsage.Off;
            _triangleRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            _triangleRenderer.allowOcclusionWhenDynamic = false;
            _triangleRenderer.enabled = false;
        }

        private void EnsureRenderTextures(Camera camera)
        {
            if (camera == null)
            {
                ReleaseRenderTextures();
                return;
            }
            EnsureRenderTextures(camera.pixelWidth);
        }

        private void EnsureRenderTextures(int cameraPixelWidth)
        {
            if (_glowMaterial == null || cameraPixelWidth <= 0)
            {
                ReleaseRenderTextures();
                return;
            }
            // Preserve the shipped 2048-reference resolution above 1080p,
            // but keep a 1536 floor on smaller windows so the 8 x 90
            // triangles remain legible instead of collapsing into blobs.
            var scale = Mathf.Max(
                cameraPixelWidth / 3840.0f,
                MinimumMaskScale);
            var width = Mathf.Max(1, Mathf.FloorToInt(scale * 2048.0f));
            var height = Mathf.Max(
                1,
                Mathf.FloorToInt(scale * 2048.0f));
            var sliceHeight = Mathf.Max(
                1,
                Mathf.FloorToInt(scale * 512.0f));
            if (_fullMaskTexture != null &&
                _fullMaskTexture.width == width &&
                _fullMaskTexture.height == height)
            {
                return;
            }

            ReleaseRenderTextures();
            _fullMaskTexture = CreateMaskTexture(
                width,
                height,
                "SkyJudgementLineFullMask",
                true);
            _filteredMaskTexture = CreateMaskTexture(
                width,
                sliceHeight,
                "SkyJudgementLineFilteredMask",
                false);
            _blurredMaskTexture = CreateMaskTexture(
                width,
                sliceHeight,
                "SkyJudgementLineBlurredMask",
                false);
            _fullMaskHandle = RTHandles.Alloc(_fullMaskTexture);
            _filteredMaskHandle =
                RTHandles.Alloc(_filteredMaskTexture);
            _blurredMaskHandle =
                RTHandles.Alloc(_blurredMaskTexture);
            _glowMaterial.SetTexture(
                DownscaledGridId,
                _filteredMaskTexture);
            _glowMaterial.SetTexture(
                DownscaledBlurredGridId,
                _blurredMaskTexture);
        }

        private bool HasRequiredResources()
        {
            return _glowMaterial != null &&
                   _maskMaterial != null &&
                   _blurMaterial != null &&
                   _triangleRenderer != null &&
                   _fullMaskHandle != null &&
                   _filteredMaskHandle != null &&
                   _blurredMaskHandle != null;
        }

        private static RenderTexture CreateMaskTexture(
            int width,
            int height,
            string name,
            bool randomWrite)
        {
            var descriptor = new RenderTextureDescriptor(
                width,
                height,
                RenderTextureFormat.ARGB32,
                0)
            {
                useMipMap = false,
                autoGenerateMips = false,
                depthBufferBits = 0,
                dimension = TextureDimension.Tex2D,
                msaaSamples = 1,
                enableRandomWrite = randomWrite,
            };
            var texture = new RenderTexture(descriptor)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            return texture;
        }

        private void UpdateOpacityBuffer(double now)
        {
            if (_opacityBuffers == null ||
                _maskMaterial == null)
            {
                return;
            }
            var currentTime = (float)now;
            if (_rangeRight - _rangeLeft < 0.01f)
            {
                for (var i = 0; i < _opacity.Length; i++)
                {
                    var age = currentTime - _lastFlash[i];
                    _opacity[i] = age < 0.1f
                        ? 1.0f
                        : 1.0f - Mathf.Clamp01(age * 5.0f);
                }
            }
            else
            {
                for (var i = 0; i < _opacity.Length; i++)
                {
                    var u = _triangleU[i];
                    if (_rangeLeft <= u && u <= _rangeRight)
                    {
                        var rowDistance = _rowDistance[i];
                        var noise = Mathf.PerlinNoise(
                            i + currentTime * 19.5f,
                            currentTime * 17.7f);
                        noise -= rowDistance * 0.5f;
                        if (noise > 0.7f &&
                            noise - rowDistance > 0.2f)
                        {
                            _opacity[i] = 1.0f;
                            _lastFlash[i] = currentTime;
                        }
                        else
                        {
                            var age =
                                currentTime - _lastFlash[i];
                            _opacity[i] = age < 0.1f
                                ? 1.0f
                                : 1.0f - Mathf.Clamp01(
                                    age * 2.5f);
                        }
                    }
                    else
                    {
                        var age = currentTime - _lastFlash[i];
                        _opacity[i] = age < 0.1f
                            ? 1.0f
                            : 1.0f - Mathf.Clamp01(age * 5.0f);
                    }
                }
            }

            _bufferIndex =
                (_bufferIndex + 1) % _opacityBuffers.Length;
            var buffer = _opacityBuffers[_bufferIndex];
            buffer.SetData(_opacity);
            _maskMaterial.SetBuffer(OpacityBufferId, buffer);
        }

        private void ReleaseRenderTextures()
        {
            if (_fullMaskHandle != null)
            {
                _fullMaskHandle.Release();
                _fullMaskHandle = null;
            }
            if (_filteredMaskHandle != null)
            {
                _filteredMaskHandle.Release();
                _filteredMaskHandle = null;
            }
            if (_blurredMaskHandle != null)
            {
                _blurredMaskHandle.Release();
                _blurredMaskHandle = null;
            }
            DestroyRenderTexture(ref _fullMaskTexture);
            DestroyRenderTexture(ref _filteredMaskTexture);
            DestroyRenderTexture(ref _blurredMaskTexture);
        }

        private static void DestroyRenderTexture(
            ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
            _initialized = false;
            ReleaseRenderTextures();
            if (_opacityBuffers != null)
            {
                for (var i = 0; i < _opacityBuffers.Length; i++)
                {
                    if (_opacityBuffers[i] != null)
                    {
                        _opacityBuffers[i].Dispose();
                        _opacityBuffers[i] = null;
                    }
                }
                _opacityBuffers = null;
            }
            DestroyMaterial(ref _glowMaterial);
            DestroyMaterial(ref _maskMaterial);
            DestroyMaterial(ref _blurMaterial);
            if (_triangleMesh != null)
            {
                Destroy(_triangleMesh);
                _triangleMesh = null;
            }
            if (_maskObject != null)
            {
                Destroy(_maskObject);
                _maskObject = null;
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
    }
}
