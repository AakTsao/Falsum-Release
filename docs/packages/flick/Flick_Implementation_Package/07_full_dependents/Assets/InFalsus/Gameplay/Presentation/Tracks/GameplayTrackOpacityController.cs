using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Tracks
{
    public interface IGameplayTrackOpacity
    {
        float TrackOpacity { get; }
        float FloorOpacity { get; }
        float SideOpacity { get; }
        int SurfaceCount { get; }

        void SetTrackOpacity(float opacity);
        void FadeTrackOpacity(float opacity, float durationSeconds);
        void SetLaneOpacity(int lane, float opacity);
        void FadeLaneOpacity(
            int lane,
            float opacity,
            float durationSeconds);
        void SetSurfaceOpacityScales(
            float floorOpacity,
            float sideOpacity);
    }

    /// <summary>
    /// Drives the two independent alpha channels of the gameplay track shader.
    ///
    /// The shader picks its alpha scale with
    /// <code>lerp(_WallAlphaScale, _AlphaScale, _IsFloorLane)</code>, so the
    /// floor lanes (LANE_1..4) and the side walls (SHIFT_2 / SPACE_2) already
    /// have one channel each. <see cref="FloorOpacity"/> feeds
    /// <code>_AlphaScale</code> and <see cref="SideOpacity"/> feeds
    /// <code>_WallAlphaScale</code>; per-surface fades still multiply on top
    /// through <see cref="Surface.Opacity"/>, and a surface only writes the
    /// channel its own material actually uses.
    ///
    /// The authored material values are 0.8 for the floor and 0.35 for the
    /// walls; <see cref="Initialize"/> seeds both from
    /// <see cref="FalsumVisualPreferences"/>'s defaults so the standalone
    /// gameplay scene and the editor agree instead of rendering the track at
    /// an implied 100%.
    /// </summary>
    public sealed class GameplayTrackOpacityController :
        MonoBehaviour,
        IGameplayTrackOpacity
    {
        private sealed class Surface
        {
            public int Lane;
            public Renderer Renderer;
            public bool IsFloor;
            public float Opacity = 1.0f;
            public float StartOpacity = 1.0f;
            public float TargetOpacity = 1.0f;
            public float Duration;
            public float Elapsed;
        }

        private static readonly int FloorAlphaId =
            Shader.PropertyToID("_AlphaScale");
        private static readonly int WallAlphaId =
            Shader.PropertyToID("_WallAlphaScale");
        private static readonly int IsFloorLaneId =
            Shader.PropertyToID("_IsFloorLane");

        private readonly List<Surface> _surfaces = new List<Surface>();
        private MaterialPropertyBlock _properties;

        public float TrackOpacity { get; private set; } = 1.0f;

        /// <summary>Multiplier written to the floor lanes' <code>_AlphaScale</code>.</summary>
        public float FloorOpacity { get; private set; } =
            FalsumVisualPreferences.DefaultFloorLaneOpacity;

        /// <summary>Multiplier written to the side walls' <code>_WallAlphaScale</code>.</summary>
        public float SideOpacity { get; private set; } =
            FalsumVisualPreferences.DefaultSideLaneOpacity;

        public int SurfaceCount
        {
            get { return _surfaces.Count; }
        }

        public void Initialize(GameplaySceneBinding binding)
        {
            _properties = new MaterialPropertyBlock();
            _surfaces.Clear();
            if (binding == null)
            {
                return;
            }
            AddSurface(binding, 0, binding.LeftSideLane);
            for (var lane = 1; lane <= 4; lane++)
            {
                AddSurface(
                    binding,
                    lane,
                    binding.LaneAnchors[lane]);
            }
            AddSurface(binding, 5, binding.RightSideLane);
            SetTrackOpacity(1.0f);
            SetSurfaceOpacityScales(
                FalsumVisualPreferences.DefaultFloorLaneOpacity,
                FalsumVisualPreferences.DefaultSideLaneOpacity);
        }

        public void SetTrackOpacity(float opacity)
        {
            TrackOpacity = Mathf.Clamp01(opacity);
            for (var i = 0; i < _surfaces.Count; i++)
            {
                var surface = _surfaces[i];
                surface.Opacity = TrackOpacity;
                surface.StartOpacity = TrackOpacity;
                surface.TargetOpacity = TrackOpacity;
                surface.Duration = 0.0f;
                surface.Elapsed = 0.0f;
                Apply(surface);
            }
        }

        public void FadeTrackOpacity(
            float opacity,
            float durationSeconds)
        {
            TrackOpacity = Mathf.Clamp01(opacity);
            for (var i = 0; i < _surfaces.Count; i++)
            {
                BeginFade(
                    _surfaces[i],
                    TrackOpacity,
                    durationSeconds);
            }
        }

        public void SetLaneOpacity(int lane, float opacity)
        {
            var value = Mathf.Clamp01(opacity);
            for (var i = 0; i < _surfaces.Count; i++)
            {
                var surface = _surfaces[i];
                if (surface.Lane != lane)
                {
                    continue;
                }
                surface.Opacity = value;
                surface.StartOpacity = value;
                surface.TargetOpacity = value;
                surface.Duration = 0.0f;
                surface.Elapsed = 0.0f;
                Apply(surface);
            }
        }

        public void FadeLaneOpacity(
            int lane,
            float opacity,
            float durationSeconds)
        {
            var value = Mathf.Clamp01(opacity);
            for (var i = 0; i < _surfaces.Count; i++)
            {
                var surface = _surfaces[i];
                if (surface.Lane == lane)
                {
                    BeginFade(surface, value, durationSeconds);
                }
            }
        }

        /// <summary>
        /// Sets the two global alpha channels. Surface fades keep running on
        /// top, so a lane that is mid-fade does not snap.
        /// </summary>
        public void SetSurfaceOpacityScales(
            float floorOpacity,
            float sideOpacity)
        {
            FloorOpacity = Mathf.Clamp01(floorOpacity);
            SideOpacity = Mathf.Clamp01(sideOpacity);
            for (var i = 0; i < _surfaces.Count; i++)
            {
                Apply(_surfaces[i]);
            }
        }

        private void Update()
        {
            var delta = Time.unscaledDeltaTime;
            for (var i = 0; i < _surfaces.Count; i++)
            {
                var surface = _surfaces[i];
                if (surface.Duration <= 0.0f)
                {
                    continue;
                }
                surface.Elapsed += delta;
                var t = Mathf.Clamp01(
                    surface.Elapsed / surface.Duration);
                t = t * t * (3.0f - 2.0f * t);
                surface.Opacity = Mathf.Lerp(
                    surface.StartOpacity,
                    surface.TargetOpacity,
                    t);
                Apply(surface);
                if (t >= 1.0f)
                {
                    surface.Duration = 0.0f;
                }
            }
        }

        private void AddSurface(
            GameplaySceneBinding binding,
            int lane,
            Transform root)
        {
            if (root == null)
            {
                return;
            }
            var renderer = root.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = root.GetComponentInChildren<Renderer>(true);
            }
            var material = renderer != null
                ? renderer.sharedMaterial
                : null;
            if (material == null ||
                (!material.HasProperty(FloorAlphaId) &&
                 !material.HasProperty(WallAlphaId)))
            {
                return;
            }
            // The shader decides floor vs. wall from the material's own
            // _IsFloorLane, so read it from there rather than from the lane
            // index: SHIFT_2 / SPACE_2 are walls even though they sit at
            // lane 0 and 5.
            var isFloor = material.HasProperty(IsFloorLaneId) &&
                          material.GetFloat(IsFloorLaneId) > 0.5f;
            var surface = new Surface
            {
                Lane = lane,
                Renderer = renderer,
                IsFloor = isFloor,
            };
            _surfaces.Add(surface);
            Apply(surface);
        }

        private void BeginFade(
            Surface surface,
            float target,
            float durationSeconds)
        {
            surface.StartOpacity = surface.Opacity;
            surface.TargetOpacity = target;
            surface.Duration = Mathf.Max(0.0f, durationSeconds);
            surface.Elapsed = 0.0f;
            if (surface.Duration <= 0.0f)
            {
                surface.Opacity = target;
                Apply(surface);
            }
        }

        private void Apply(Surface surface)
        {
            if (surface == null || surface.Renderer == null)
            {
                return;
            }
            // Per-surface fade × the surface's own global channel. Writing
            // only the channel the material uses keeps the unused property out
            // of the block, so a floor lane cannot accidentally pick up the
            // wall scale (and vice versa) if a material ever gets retargeted.
            var scaled = surface.Opacity *
                         (surface.IsFloor ? FloorOpacity : SideOpacity);
            surface.Renderer.GetPropertyBlock(_properties);
            if (surface.IsFloor)
            {
                _properties.SetFloat(FloorAlphaId, scaled);
            }
            else
            {
                _properties.SetFloat(WallAlphaId, scaled);
            }
            surface.Renderer.SetPropertyBlock(_properties);
        }
    }
}
