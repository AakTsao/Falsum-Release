using System;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Tracks
{
    public sealed class LaneStateController
    {
        public const int MaxVisibleDisableIntervals = 4;

        private sealed class Surface
        {
            public int Lane;
            public Renderer Renderer;
            public float DistanceScale;
        }

        private static readonly int[] DisableIntervalIds =
        {
            Shader.PropertyToID("_LaneDisableInterval0"),
            Shader.PropertyToID("_LaneDisableInterval1"),
            Shader.PropertyToID("_LaneDisableInterval2"),
            Shader.PropertyToID("_LaneDisableInterval3"),
        };

        private readonly Chart _chart;
        private readonly TrackMotion _motion;
        private readonly Surface[] _surfaces;
        private readonly MaterialPropertyBlock _properties =
            new MaterialPropertyBlock();
        private readonly Vector4[] _disableIntervals =
            new Vector4[MaxVisibleDisableIntervals];

        public int DisabledSurfaceCount { get; private set; }

        public LaneStateController(
            GameplaySceneBinding binding,
            Chart chart,
            TrackMotion motion)
        {
            _chart = chart;
            _motion = motion;
            _surfaces = new[]
            {
                CreateSurface(binding, 0, binding.LeftSideLane),
                CreateSurface(binding, 1, binding.LaneAnchors[1]),
                CreateSurface(binding, 4, binding.LaneAnchors[4]),
                CreateSurface(binding, 5, binding.RightSideLane),
            };
        }

        public void Update(double nowMs)
        {
            DisabledSurfaceCount = 0;
            for (var i = 0; i < _surfaces.Length; i++)
            {
                var surface = _surfaces[i];
                if (surface == null || surface.Renderer == null)
                {
                    continue;
                }

                // One lane-state query per surface. BuildVisibleDisableIntervals
                // used to call TryGetLaneDisabledInterval a SECOND time with the
                // same (lane, nowMs) arguments purely to recover its `disabled`
                // flag, so Update walked every LaneEvent list twice per surface
                // (eight scans per frame across four surfaces). The overload
                // below hands the flag back instead of re-deriving it.
                var intervalCount = BuildVisibleDisableIntervals(
                    _chart,
                    _motion,
                    surface.Lane,
                    nowMs,
                    surface.DistanceScale,
                    _disableIntervals,
                    out var disabled);
                if (disabled)
                {
                    DisabledSurfaceCount++;
                }

                surface.Renderer.GetPropertyBlock(_properties);
                for (var interval = 0;
                     interval < DisableIntervalIds.Length;
                     interval++)
                {
                    _properties.SetVector(
                        DisableIntervalIds[interval],
                        interval < intervalCount
                            ? _disableIntervals[interval]
                            : Vector4.zero);
                }
                surface.Renderer.SetPropertyBlock(_properties);
            }
        }

        /// <summary>
        /// Same result as <see cref="BuildVisibleDisableIntervals(Chart,
        /// TrackMotion, int, double, float, Vector4[])"/> plus the lane's
        /// current disabled flag, so a per-frame caller needs only one pass
        /// over <c>LaneEvents</c> instead of two.
        /// </summary>
        public static int BuildVisibleDisableIntervals(
            Chart chart,
            TrackMotion motion,
            int lane,
            double nowMs,
            float distanceScale,
            Vector4[] output,
            out bool disabledAtNow)
        {
            disabledAtNow = false;
            if (chart == null || motion == null || output == null ||
                output.Length == 0)
            {
                return 0;
            }

            double startTime;
            double endTime;
            var disabled = chart.TryGetLaneDisabledInterval(
                lane,
                nowMs,
                out startTime,
                out endTime);
            disabledAtNow = disabled;
            var intervalStartTime = startTime;
            var count = 0;
            var resumeAfterTimeMs = nowMs;

            if (disabled)
            {
                AddVisibleInterval(
                    chart,
                    motion,
                    nowMs,
                    distanceScale,
                    startTime,
                    endTime,
                    output,
                    ref count);
                if (double.IsPositiveInfinity(endTime))
                {
                    return count;
                }
                resumeAfterTimeMs = endTime;
                disabled = false;
            }

            for (var i = 0; i < chart.LaneEvents.Count; i++)
            {
                var evt = chart.LaneEvents[i];
                if (evt.Lane != lane ||
                    evt.TimeMs <= resumeAfterTimeMs + 0.0001)
                {
                    continue;
                }

                if (evt.Enabled)
                {
                    if (disabled)
                    {
                        AddVisibleInterval(
                            chart,
                            motion,
                            nowMs,
                            distanceScale,
                            intervalStartTime,
                            evt.TimeMs,
                            output,
                            ref count);
                        disabled = false;
                    }
                }
                else if (!disabled)
                {
                    intervalStartTime = evt.TimeMs;
                    disabled = true;
                }
            }

            if (disabled)
            {
                AddVisibleInterval(
                    chart,
                    motion,
                    nowMs,
                    distanceScale,
                    intervalStartTime,
                    double.PositiveInfinity,
                    output,
                    ref count);
            }

            return count;
        }

        /// <summary>
        /// Backwards-compatible entry point for callers that only need the
        /// interval list (the gameplay self check). Delegates to the overload
        /// that also reports the disabled flag, so there is exactly ONE
        /// implementation of the lane-state walk.
        /// </summary>
        public static int BuildVisibleDisableIntervals(
            Chart chart,
            TrackMotion motion,
            int lane,
            double nowMs,
            float distanceScale,
            Vector4[] output)
        {
            return BuildVisibleDisableIntervals(
                chart,
                motion,
                lane,
                nowMs,
                distanceScale,
                output,
                out _);
        }

        private static void AddVisibleInterval(
            Chart chart,
            TrackMotion motion,
            double nowMs,
            float distanceScale,
            double startTimeMs,
            double endTimeMs,
            Vector4[] output,
            ref int count)
        {
            if (count >= output.Length ||
                count >= MaxVisibleDisableIntervals)
            {
                return;
            }

            var startDistance =
                motion.NoteZAt(startTimeMs, nowMs) * distanceScale;
            double endDistance;
            if (double.IsPositiveInfinity(endTimeMs))
            {
                var direction =
                    chart.PreviewSpeedMultiplierAt(nowMs) >= 0.0
                        ? 1.0
                        : -1.0;
                endDistance = startDistance +
                              direction *
                              motion.TrackLength *
                              64.0 *
                              distanceScale;
            }
            else
            {
                endDistance =
                    motion.NoteZAt(endTimeMs, nowMs) * distanceScale;
            }

            var near = Math.Min(startDistance, endDistance);
            var far = Math.Max(startDistance, endDistance);
            var trackLength = motion.TrackLength;
            if (far <= 0.0 || near >= trackLength)
            {
                return;
            }

            output[count] = new Vector4(
                (float)Math.Max(0.0, near),
                (float)Math.Min(trackLength, far),
                1.0f,
                0.0f);
            count++;
        }

        private static Surface CreateSurface(
            GameplaySceneBinding binding,
            int lane,
            Transform transform)
        {
            var renderer = transform != null
                ? transform.GetComponent<Renderer>()
                : null;
            if (renderer == null)
            {
                return null;
            }

            var distanceScale = 1.0f;
            var material = renderer.sharedMaterial;
            if (material != null &&
                material.HasProperty("_JudgementLineWorldPosition") &&
                material.HasProperty("_EndOfTrackWorldPosition"))
            {
                var judgement = (Vector3)material.GetVector(
                    "_JudgementLineWorldPosition");
                var end = (Vector3)material.GetVector(
                    "_EndOfTrackWorldPosition");
                var axis = end - judgement;
                if (axis.sqrMagnitude > 0.0001f)
                {
                    var trackForward = binding.MasterScene.TransformDirection(
                        Vector3.forward);
                    distanceScale = Mathf.Abs(Vector3.Dot(
                        axis.normalized,
                        trackForward));
                }
            }
            if (distanceScale < 0.01f)
            {
                distanceScale = 1.0f;
            }

            return new Surface
            {
                Lane = lane,
                Renderer = renderer,
                DistanceScale = distanceScale,
            };
        }
    }
}
