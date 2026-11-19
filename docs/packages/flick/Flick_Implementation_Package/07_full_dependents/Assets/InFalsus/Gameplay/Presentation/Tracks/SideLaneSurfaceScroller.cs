using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Tracks
{
    public sealed class SideLaneSurfaceScroller
    {
        private const float SideWallTextureWorldLength = 4.0f;

        private sealed class Surface
        {
            public Renderer Renderer;
            public Vector4 BaseTextureTransform;
        }

        private static readonly int EnabledTextureSTId =
            Shader.PropertyToID("_EnabledTexture_ST");
        private static readonly int DisabledTextureSTId =
            Shader.PropertyToID("_DisabledTexture_ST");

        private readonly TrackMotion _motion;
        private readonly Surface[] _surfaces;
        private readonly MaterialPropertyBlock _properties =
            new MaterialPropertyBlock();

        public float CurrentOffset { get; private set; }
        public int SurfaceCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _surfaces.Length; i++)
                {
                    if (_surfaces[i] != null)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public SideLaneSurfaceScroller(
            GameplaySceneBinding binding,
            TrackMotion motion)
        {
            _motion = motion;
            _surfaces = new[]
            {
                CreateSurface(binding.LeftSideLane),
                CreateSurface(binding.RightSideLane),
            };
        }

        public void Update(double nowMs)
        {
            // The visible wall-grain UV islands repeat every four world
            // units. Using the full 16-unit V span makes the texture 4x slow.
            CurrentOffset = (float)(
                _motion.TrackTravelAt(nowMs) /
                SideWallTextureWorldLength);
            for (var i = 0; i < _surfaces.Length; i++)
            {
                var surface = _surfaces[i];
                if (surface == null || surface.Renderer == null)
                {
                    continue;
                }

                var textureTransform = surface.BaseTextureTransform;
                textureTransform.w =
                    surface.BaseTextureTransform.w +
                    CurrentOffset * surface.BaseTextureTransform.y;
                _properties.Clear();
                surface.Renderer.GetPropertyBlock(_properties);
                _properties.SetVector(EnabledTextureSTId, textureTransform);
                _properties.SetVector(DisabledTextureSTId, textureTransform);
                surface.Renderer.SetPropertyBlock(_properties);
            }
        }

        private static Surface CreateSurface(Transform lane)
        {
            var renderer = lane != null
                ? lane.GetComponent<Renderer>()
                : null;
            var material = renderer != null
                ? renderer.sharedMaterial
                : null;
            if (material == null ||
                !material.HasProperty(EnabledTextureSTId))
            {
                return null;
            }

            return new Surface
            {
                Renderer = renderer,
                BaseTextureTransform =
                    material.GetVector(EnabledTextureSTId),
            };
        }
    }
}
