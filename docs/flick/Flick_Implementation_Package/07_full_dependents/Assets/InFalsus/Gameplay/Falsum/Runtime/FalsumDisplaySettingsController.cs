using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumDisplayConfiguration
    {
        public int Width;
        public int Height;
        public int RefreshRate;
        public FullScreenMode FullScreenMode;
        public bool AntiAliasingEnabled;
        public FalsumAntiAliasingMode AntiAliasingMode;
    }

    public static class FalsumDisplaySettingsController
    {
        private const string ClarityVolumeObjectName =
            "Falsum Editor Post Process Clarity";
        private static Volume _clarityVolume;
        private static VolumeProfile _clarityProfile;

        public static FalsumDisplayConfiguration Capture(
            FalsumPreferences preferences)
        {
            var current = Screen.currentResolution;
            return new FalsumDisplayConfiguration
            {
                Width = Screen.width > 0
                    ? Screen.width
                    : current.width,
                Height = Screen.height > 0
                    ? Screen.height
                    : current.height,
                RefreshRate = Mathf.RoundToInt(
                    (float)current.refreshRateRatio.value),
                FullScreenMode = Screen.fullScreenMode,
                AntiAliasingEnabled =
                    preferences != null &&
                    preferences.Display.AntiAliasingEnabled,
                AntiAliasingMode =
                    preferences != null
                        ? preferences.Display.AntiAliasingMode
                        : FalsumAntiAliasingMode.Msaa4x,
            };
        }

        public static bool IsSupported(
            FalsumAntiAliasingMode mode,
            out string reason)
        {
            var asset =
                GraphicsSettings.currentRenderPipeline as
                    UniversalRenderPipelineAsset;
            if (asset == null)
            {
                reason = L.Get("falsum.error.display.urpRequired");
                return false;
            }
            if (!IsMsaa(mode) &&
                mode != FalsumAntiAliasingMode.Smaa &&
                mode != FalsumAntiAliasingMode.Taa)
            {
                reason = L.Get(
                    "falsum.error.display.unknownAntiAliasingMode");
                return false;
            }
            var camera = Camera.main;
            if (camera == null)
            {
                reason = L.Get("falsum.error.display.mainCameraMissing");
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public static bool TryApply(
            FalsumPreferences preferences,
            out string error)
        {
            return TryApply(preferences, true, out error);
        }

        public static bool TryApply(
            FalsumPreferences preferences,
            bool applyResolution,
            out string error)
        {
            error = string.Empty;
            if (preferences == null)
            {
                error = L.Get("falsum.error.display.preferencesMissing");
                return false;
            }
            try
            {
                if (preferences.Display.AntiAliasingEnabled)
                {
                    string unsupportedReason;
                    if (!IsSupported(
                            preferences.Display.AntiAliasingMode,
                            out unsupportedReason))
                    {
                        error = unsupportedReason;
                        return false;
                    }
                }
                if (applyResolution)
                {
                    ApplyResolution(preferences.Display);
                }
                ApplyAntiAliasing(preferences.Display);
                ApplyEditorPostProcessClarity();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static void ApplyConfiguration(
            FalsumDisplayConfiguration configuration)
        {
            if (configuration == null)
            {
                return;
            }
            Screen.SetResolution(
                configuration.Width,
                configuration.Height,
                configuration.FullScreenMode,
                BuildRefreshRate(configuration.RefreshRate));
            ApplyAntiAliasing(
                configuration.AntiAliasingEnabled,
                configuration.AntiAliasingMode);
        }

        private static void ApplyResolution(
            FalsumDisplayPreferences display)
        {
            var width = Mathf.Max(1280, display.ResolutionWidth);
            var height = Mathf.Max(720, display.ResolutionHeight);
            var refresh = Mathf.Max(0, display.ResolutionRefreshRate);
            var mode = display.BorderlessFullscreen
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.Windowed;
            Screen.SetResolution(
                width,
                height,
                mode,
                BuildRefreshRate(refresh));
        }

        private static void ApplyAntiAliasing(
            FalsumDisplayPreferences display)
        {
            ApplyAntiAliasing(
                display.AntiAliasingEnabled,
                display.AntiAliasingMode);
        }

        private static void ApplyAntiAliasing(
            bool enabled,
            FalsumAntiAliasingMode mode)
        {
            var asset =
                GraphicsSettings.currentRenderPipeline as
                    UniversalRenderPipelineAsset;
            if (asset != null)
            {
                asset.msaaSampleCount = enabled &&
                                        IsMsaa(mode)
                    ? MsaaSamples(mode)
                    : 1;
            }
            QualitySettings.antiAliasing = enabled &&
                                           IsMsaa(mode)
                ? MsaaSamples(mode)
                : 0;

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }
            camera.allowMSAA = enabled && IsMsaa(mode);
            var cameraData =
                camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData =
                    camera.gameObject.AddComponent<
                        UniversalAdditionalCameraData>();
            }
            if (!enabled)
            {
                cameraData.antialiasing = AntialiasingMode.None;
                return;
            }
            switch (mode)
            {
                case FalsumAntiAliasingMode.Smaa:
                    cameraData.antialiasing =
                        AntialiasingMode
                            .SubpixelMorphologicalAntiAliasing;
                    break;
                case FalsumAntiAliasingMode.Taa:
                    cameraData.antialiasing =
                        AntialiasingMode.TemporalAntiAliasing;
                    break;
                default:
                    cameraData.antialiasing =
                        AntialiasingMode.None;
                    break;
            }
        }

        private static void ApplyEditorPostProcessClarity()
        {
            // The template volume's tonemapping and soft bloom wash out the view.
            if (_clarityVolume == null)
            {
                var existing = GameObject.Find(
                    ClarityVolumeObjectName);
                if (existing != null)
                {
                    _clarityVolume =
                        existing.GetComponent<Volume>();
                }
                if (_clarityVolume == null)
                {
                    var volumeObject = new GameObject(
                        ClarityVolumeObjectName);
                    _clarityVolume =
                        volumeObject.AddComponent<Volume>();
                }
            }

            if (_clarityProfile == null)
            {
                _clarityProfile =
                    ScriptableObject.CreateInstance<VolumeProfile>();
                _clarityProfile.name =
                    "Falsum Editor Clarity";

                var tonemapping =
                    _clarityProfile.Add<Tonemapping>(true);
                tonemapping.mode.Override(TonemappingMode.None);

                var bloom = _clarityProfile.Add<Bloom>(true);
                bloom.intensity.Override(0.0f);

                var vignette =
                    _clarityProfile.Add<Vignette>(true);
                vignette.intensity.Override(0.0f);
            }

            _clarityVolume.isGlobal = true;
            _clarityVolume.priority = 1000.0f;
            _clarityVolume.weight = 1.0f;
            _clarityVolume.sharedProfile = _clarityProfile;
            _clarityVolume.enabled = true;
        }

        private static bool IsMsaa(FalsumAntiAliasingMode mode)
        {
            return mode == FalsumAntiAliasingMode.Msaa2x ||
                   mode == FalsumAntiAliasingMode.Msaa4x ||
                   mode == FalsumAntiAliasingMode.Msaa8x;
        }

        private static int MsaaSamples(FalsumAntiAliasingMode mode)
        {
            switch (mode)
            {
                case FalsumAntiAliasingMode.Msaa2x:
                    return 2;
                case FalsumAntiAliasingMode.Msaa8x:
                    return 8;
                default:
                    return 4;
            }
        }

        private static RefreshRate BuildRefreshRate(int value)
        {
            return new RefreshRate
            {
                numerator = (uint)Mathf.Max(0, value),
                denominator = 1,
            };
        }
    }
}
