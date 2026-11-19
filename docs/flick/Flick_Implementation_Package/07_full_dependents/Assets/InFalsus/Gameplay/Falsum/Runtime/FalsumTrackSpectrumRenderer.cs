using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Timing;
using UnityEngine;
using UnityEngine.Rendering;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumTrackSpectrumRenderer : IDisposable
    {
        public const int WarpSampleCount = 256;

        private const float TrackScale = (float)TrackMotion.TrackScale;
        // The floor surface is drawn at floorY; the waveform quad must sit
        // clear of it, otherwise ZTest LEqual rejects it as coplanar.
        private const float SpectrumYOffset = 0.02f;
        private const int SpectrumRenderQueue = 3002;

        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly Material _material;
        private readonly Texture2D _warpTexture;
        private readonly float[] _warpSamples =
            new float[WarpSampleCount];
        private readonly float _trackSpan;
        private Texture2D _spectrumTexture;
        private Chart _warpChart;
        private int _warpSignature;
        private double _warpDurationMs;
        private float _secondsPerTrack;
        private double _segmentStartMs;
        private double _segmentDurationMs;
        private bool _disposed;

        public bool HasTexture
        {
            get { return _spectrumTexture != null; }
        }

        public string DebugSummary
        {
            get
            {
                return "ready=" + HasTexture +
                       " visible=" +
                       (_renderer != null &&
                        _renderer.enabled) +
                       " texture=" +
                       (_spectrumTexture != null
                           ? _spectrumTexture.width + "x" +
                             _spectrumTexture.height
                           : "<none>") +
                       " warp=" +
                       WarpSampleCount +
                       "x1 span=" +
                       _trackSpan.ToString("0.000") +
                       " duration=" +
                       _warpDurationMs.ToString("0.0") +
                       " secondsPerTrack=" +
                       _secondsPerTrack.ToString("0.000") +
                       " segment=" +
                       _segmentStartMs.ToString("0.0") +
                       "+" +
                       _segmentDurationMs.ToString("0.0");
            }
        }

        public FalsumTrackSpectrumRenderer(
            Transform master,
            double judgementZ,
            double trackEndZ,
            float floorY,
            float halfWidth)
        {
            if (master == null)
            {
                throw new ArgumentNullException("master");
            }

            _trackSpan = Mathf.Max(
                0.1f,
                (float)trackEndZ - (float)judgementZ);
            _root = new GameObject("FalsumTrackSpectrum");
            _root.transform.SetParent(master, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _root.transform.localScale = Vector3.one;

            var filter = _root.AddComponent<MeshFilter>();
            _renderer = _root.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;

            _mesh = BuildQuad(
                (float)judgementZ,
                (float)judgementZ + _trackSpan,
                floorY + SpectrumYOffset,
                Mathf.Max(0.01f, halfWidth));
            filter.sharedMesh = _mesh;

            var shader = Resources.Load<Shader>(
                "Falsum/Shaders/FalsumTrackSpectrum");
            if (shader == null)
            {
                shader = Shader.Find("Falsum/Track Waveform");
            }
            if (shader == null)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.shader.spectrumMissing"));
            }

            _material = new Material(shader)
            {
                name = "FalsumTrackSpectrumMaterial",
                renderQueue = SpectrumRenderQueue,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _warpTexture = new Texture2D(
                WarpSampleCount,
                1,
                TextureFormat.RFloat,
                false,
                true)
            {
                name = "FalsumTrackSpectrumWarp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Array.Clear(_warpSamples, 0, _warpSamples.Length);
            _warpTexture.SetPixelData(
                _warpSamples,
                0);
            _warpTexture.Apply(false, false);
            _material.SetTexture("_WarpTex", _warpTexture);
            _material.SetColor(
                "_WaveColor",
                new Color32(0x45, 0xA8, 0xFF, 0xFF));
            _renderer.sharedMaterial = _material;
        }

        // The waveform texture is owned by FalsumTrackWaveformCache; the
        // renderer only borrows it and must never destroy it.
        public void SetTexture(
            Texture2D texture,
            double segmentStartMs,
            double segmentDurationMs)
        {
            if (_disposed)
            {
                return;
            }
            if (ReferenceEquals(_spectrumTexture, texture))
            {
                return;
            }
            _spectrumTexture = texture;
            _segmentStartMs = segmentStartMs;
            _segmentDurationMs = segmentDurationMs;
            if (_material != null)
            {
                _material.SetTexture("_WaveformTex", texture);
                _material.SetFloat(
                    "_WaveformTexelHeight",
                    texture != null && texture.height > 0
                        ? 1.0f / texture.height
                        : 1.0f / 512.0f);
                _material.SetFloat(
                    "_SegmentStartSeconds",
                    (float)(segmentStartMs / 1000.0));
                _material.SetFloat(
                    "_SegmentDurationSeconds",
                    (float)(segmentDurationMs / 1000.0));
            }
            if (texture == null && _renderer != null)
            {
                _renderer.enabled = false;
            }
        }

        public void ClearTexture()
        {
            SetTexture(null, 0.0, 0.0);
            _warpChart = null;
            _warpSignature = 0;
            _warpDurationMs = 0.0;
        }

        public void Update(
            Chart chart,
            double chartTimeMs,
            double audioDurationMs,
            double delayMs,
            double flowSpeed,
            bool visible)
        {
            if (_disposed ||
                _renderer == null ||
                _material == null ||
                _spectrumTexture == null ||
                chart == null ||
                !visible)
            {
                if (_renderer != null)
                {
                    _renderer.enabled = false;
                }
                return;
            }

            var audioDuration = Math.Max(0.0, audioDurationMs);
            var chartDuration = audioDuration - delayMs;
            if (audioDuration <= 0.001 || chartDuration <= 0.001)
            {
                _renderer.enabled = false;
                return;
            }

            EnsureWarpTexture(chart, chartDuration);
            var safeFlowSpeed = Math.Max(0.01, flowSpeed);
            _secondsPerTrack = SecondsPerTrack(safeFlowSpeed);
            var safeChartTime = Math.Max(0.0, chartTimeMs);
            _material.SetFloat(
                "_NowScaledSeconds",
                (float)(chart.ScaledTimeAt(safeChartTime) / 1000.0));
            _material.SetFloat(
                "_ChartDurationSeconds",
                (float)(chartDuration / 1000.0));
            _material.SetFloat(
                "_AudioDurationSeconds",
                (float)(audioDuration / 1000.0));
            _material.SetFloat(
                "_DelaySeconds",
                (float)(delayMs / 1000.0));
            _material.SetFloat(
                "_SecondsPerTrack",
                _secondsPerTrack);
            _renderer.enabled = true;
        }

        public static float SecondsPerTrack(double flowSpeed)
        {
            var safeSpeed = Math.Max(0.01, flowSpeed);
            return (float)(1.0 / (TrackScale * safeSpeed));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _spectrumTexture = null;
            DestroyTexture(_warpTexture);
            if (_material != null)
            {
                DestroyObject(_material);
            }
            if (_mesh != null)
            {
                DestroyObject(_mesh);
            }
            if (_root != null)
            {
                DestroyObject(_root);
            }
        }

        public static int FillWarpSamples(
            Chart chart,
            double chartDurationMs,
            float[] output)
        {
            if (chart == null ||
                output == null ||
                output.Length == 0 ||
                chartDurationMs <= 0.001)
            {
                return 0;
            }

            var totalScaledMs = Math.Max(
                0.001,
                chart.ScaledTimeAt(chartDurationMs));
            var last = output.Length - 1;
            for (var i = 0; i < output.Length; i++)
            {
                var targetScaledMs =
                    totalScaledMs * i /
                    Math.Max(1, last);
                var actualMs = ActualTimeAtScaledMs(
                    chart,
                    targetScaledMs,
                    chartDurationMs);
                output[i] = Mathf.Clamp01(
                    (float)(actualMs / chartDurationMs));
            }
            return output.Length;
        }

        private void EnsureWarpTexture(
            Chart chart,
            double chartDurationMs)
        {
            var signature = ComputeWarpSignature(
                chart,
                chartDurationMs);
            if (ReferenceEquals(_warpChart, chart) &&
                _warpSignature == signature &&
                Math.Abs(_warpDurationMs - chartDurationMs) < 0.01)
            {
                return;
            }

            FillWarpSamples(
                chart,
                chartDurationMs,
                _warpSamples);
            _warpTexture.SetPixelData(
                _warpSamples,
                0);
            _warpTexture.Apply(false, false);
            _material.SetTexture("_WarpTex", _warpTexture);
            _warpChart = chart;
            _warpSignature = signature;
            _warpDurationMs = chartDurationMs;
        }

        private static double ActualTimeAtScaledMs(
            Chart chart,
            double targetScaledMs,
            double durationMs)
        {
            var actualMs = 0.0;
            var scaledMs = 0.0;
            var speed = 1.0;
            var events = chart.SpeedEvents;
            for (var i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                if (evt == null ||
                    evt.TimeMs >= durationMs)
                {
                    break;
                }
                var eventMs = Math.Max(actualMs, evt.TimeMs);
                var segmentScaled =
                    (eventMs - actualMs) * speed;
                if (targetScaledMs <=
                    scaledMs + segmentScaled + 0.000001)
                {
                    return speed > 0.000001
                        ? actualMs +
                          (targetScaledMs - scaledMs) / speed
                        : actualMs;
                }
                scaledMs += segmentScaled;
                actualMs = eventMs;
                speed = Math.Max(0.0, evt.Multiplier);
            }

            return speed > 0.000001
                ? actualMs +
                  (targetScaledMs - scaledMs) / speed
                : actualMs;
        }

        private static int ComputeWarpSignature(
            Chart chart,
            double durationMs)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 +
                       durationMs.GetHashCode();
                var events = chart.SpeedEvents;
                hash = hash * 31 + events.Count;
                for (var i = 0; i < events.Count; i++)
                {
                    var evt = events[i];
                    if (evt == null)
                    {
                        continue;
                    }
                    hash = hash * 31 +
                           evt.TimeMs.GetHashCode();
                    hash = hash * 31 +
                           evt.Multiplier.GetHashCode();
                }
                return hash;
            }
        }

        private static Mesh BuildQuad(
            float startZ,
            float endZ,
            float y,
            float halfWidth)
        {
            var mesh = new Mesh
            {
                name = "FalsumTrackSpectrumMesh",
            };
            mesh.SetVertices(
                new List<Vector3>
                {
                    new Vector3(-halfWidth, y, startZ),
                    new Vector3(halfWidth, y, startZ),
                    new Vector3(-halfWidth, y, endZ),
                    new Vector3(halfWidth, y, endZ),
                });
            mesh.SetUVs(
                0,
                new List<Vector2>
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(1.0f, 1.0f),
                });
            mesh.SetTriangles(
                new[]
                {
                    0, 2, 1,
                    2, 3, 1,
                },
                0,
                false);
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.HideAndDontSave;
            return mesh;
        }

        private static void DestroyTexture(Texture texture)
        {
            if (texture == null)
            {
                return;
            }
            DestroyObject(texture);
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
