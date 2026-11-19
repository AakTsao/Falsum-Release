using System;
using System.Collections.Generic;
using System.Globalization;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "Display" page: resolution, borderless fullscreen, anti-aliasing and
    /// the scene-surface opacity group.
    ///
    /// The opacity rows deliberately write <see cref="FalsumVisualPreferences"/>
    /// instead of <see cref="FalsumDisplayPreferences"/>: the editor treats any
    /// Display-group change as a resolution change and would demand the
    /// ten-second keep/revert confirmation for a slider that cannot lose the
    /// user's display.
    ///
    /// The old view guarded every callback with a <code>_updating</code>
    /// reentrancy flag that was never assigned anywhere in the repository,
    /// so the guard was always false and dead. The guards are dropped here
    /// rather than copied, which is behaviour-equivalent; if reentrancy ever
    /// becomes real, it has to be reintroduced with an actual assignment.
    /// </summary>
    internal static class FalsumDisplayPreferencesPage
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        private sealed class ResolutionChoice
        {
            public int Width;
            public int Height;
            public int RefreshRate;
            public string Label;
        }

        public static void Build(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host = null)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.display.group"));
            var resolutions = BuildResolutionChoices(preferences);
            var resolutionLabels = new List<string>();
            var resolutionIndex = 0;
            for (var i = 0; i < resolutions.Count; i++)
            {
                resolutionLabels.Add(resolutions[i].Label);
                if (resolutions[i].Width ==
                        preferences.Display.ResolutionWidth &&
                    resolutions[i].Height ==
                        preferences.Display.ResolutionHeight &&
                    resolutions[i].RefreshRate ==
                        preferences.Display.ResolutionRefreshRate)
                {
                    resolutionIndex = i;
                }
            }
            var resolution = new DropdownField
            {
                choices = resolutionLabels,
                index = resolutionIndex,
            };
            resolution.AddToClassList("preferences-field");
            resolution.SetEnabled(
                preferences.Display.BorderlessFullscreen);
            resolution.RegisterValueChangedCallback(
                evt =>
                {
                    var index = resolution.choices.IndexOf(evt.newValue);
                    if (index < 0 || index >= resolutions.Count)
                    {
                        return;
                    }
                    preferences.Display.ResolutionWidth =
                        resolutions[index].Width;
                    preferences.Display.ResolutionHeight =
                        resolutions[index].Height;
                    preferences.Display.ResolutionRefreshRate =
                        resolutions[index].RefreshRate;
                });

            DropdownField mode = null;
            var antiAliasing = new Toggle
            {
                value = preferences.Display.AntiAliasingEnabled,
            };
            antiAliasing.AddToClassList("preferences-toggle");
            antiAliasing.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Display.AntiAliasingEnabled =
                        evt.newValue;
                    mode.SetEnabled(evt.newValue);
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.display.antiAliasing"),
                L.Get(
                    "falsum.preferences.display.antiAliasingDescription"),
                string.Empty,
                string.Empty,
                antiAliasing,
                string.Empty));

            var modes = new List<string>
            {
                L.Get("falsum.preferences.display.msaa2x"),
                L.Get("falsum.preferences.display.msaa4x"),
                L.Get("falsum.preferences.display.msaa8x"),
                L.Get("falsum.preferences.display.smaa"),
                L.Get("falsum.preferences.display.taa"),
            };
            mode = new DropdownField
            {
                choices = modes,
                index = (int)preferences.Display.AntiAliasingMode,
            };
            mode.AddToClassList("preferences-field");
            mode.SetEnabled(
                preferences.Display.AntiAliasingEnabled);
            mode.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Display.AntiAliasingMode =
                        (FalsumAntiAliasingMode)Mathf.Clamp(
                            mode.index,
                            0,
                            modes.Count - 1);
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.display.aaMode"),
                L.Get(
                    "falsum.preferences.display.aaModeDescription"),
                string.Empty,
                string.Empty,
                mode,
                "stretch"));

            var borderless = new Toggle
            {
                value = preferences.Display.BorderlessFullscreen,
            };
            borderless.AddToClassList("preferences-toggle");
            borderless.RegisterValueChangedCallback(
                evt =>
                {
                    preferences.Display.BorderlessFullscreen =
                        evt.newValue;
                    resolution.SetEnabled(evt.newValue);
                });
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.display.borderless"),
                L.Get(
                    "falsum.preferences.display.borderlessDescription"),
                string.Empty,
                string.Empty,
                borderless,
                string.Empty));
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.display.fullscreenResolution"),
                L.Get(
                    "falsum.preferences.display.resolutionDescription"),
                string.Empty,
                string.Empty,
                resolution,
                "stretch"));
            content.Add(group);
            content.Add(BuildVisualGroup(preferences, host));
        }

        /// <summary>
        /// Scene-surface opacity group. The values live in
        /// <see cref="FalsumVisualPreferences"/> rather than in the Display
        /// group, because a change in the Display group is what makes the
        /// editor show its ten-second keep/revert window confirmation.
        ///
        /// Each slider previews immediately through the page host: these
        /// settings are purely visual, so waiting for Complete to see the
        /// effect would make them impossible to tune. Cancel / revert restore
        /// the committed group.
        /// </summary>
        private static VisualElement BuildVisualGroup(
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var visual = preferences.Visual;
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.visual.group"));
            group.Add(CreatePercentRow(
                L.Get("falsum.preferences.visual.skyAreaOpacity"),
                L.Get(
                    "falsum.preferences.visual.skyAreaOpacityDescription"),
                visual.SkyAreaOpacity,
                delegate(float value)
                {
                    visual.SkyAreaOpacity = value;
                    PreviewVisual(host);
                }));
            group.Add(CreatePercentRow(
                L.Get("falsum.preferences.visual.skyAreaGlow"),
                L.Get(
                    "falsum.preferences.visual.skyAreaGlowDescription"),
                visual.SkyAreaGlow,
                delegate(float value)
                {
                    visual.SkyAreaGlow = value;
                    PreviewVisual(host);
                }));
            group.Add(CreatePercentRow(
                L.Get("falsum.preferences.visual.skyLaneOpacity"),
                L.Get(
                    "falsum.preferences.visual.skyLaneOpacityDescription"),
                visual.SideLaneOpacity,
                delegate(float value)
                {
                    visual.SideLaneOpacity = value;
                    PreviewVisual(host);
                }));
            group.Add(CreatePercentRow(
                L.Get("falsum.preferences.visual.floorLaneOpacity"),
                L.Get(
                    "falsum.preferences.visual.floorLaneOpacityDescription"),
                visual.FloorLaneOpacity,
                delegate(float value)
                {
                    visual.FloorLaneOpacity = value;
                    PreviewVisual(host);
                }));
            return group;
        }

        private static void PreviewVisual(IFalsumPreferencesPageHost host)
        {
            if (host != null)
            {
                host.PreviewVisual();
            }
        }

        private static VisualElement CreatePercentRow(
            string title,
            string description,
            float value,
            Action<float> changed)
        {
            var slider = new Slider(0.0f, 1.0f)
            {
                value = Mathf.Clamp01(value),
            };
            slider.AddToClassList("falsum-slider");
            slider.AddToClassList("preferences-volume-slider");
            var valueLabel = new Label(Percent(slider.value));
            valueLabel.AddToClassList("preferences-volume-value");
            slider.RegisterValueChangedCallback(
                evt =>
                {
                    var next = Mathf.Clamp01(evt.newValue);
                    valueLabel.text = Percent(next);
                    if (changed != null)
                    {
                        changed(next);
                    }
                });

            var control = new VisualElement();
            control.AddToClassList("preferences-volume-control");
            control.Add(slider);
            control.Add(valueLabel);
            return FalsumPreferencesWidgets.CreateRow(
                title,
                description,
                string.Empty,
                string.Empty,
                control,
                "stretch");
        }

        private static string Percent(float value)
        {
            return Mathf.RoundToInt(
                       Mathf.Clamp01(value) * 100.0f)
                   .ToString(Invariant) +
                   "%";
        }

        private static List<ResolutionChoice> BuildResolutionChoices(
            FalsumPreferences preferences)
        {
            var result = new List<ResolutionChoice>();
            var seen = new HashSet<string>();
            var resolutions = Screen.resolutions;
            for (var i = 0; i < resolutions.Length; i++)
            {
                var resolution = resolutions[i];
                if (resolution.width < 1280 ||
                    resolution.height < 720)
                {
                    continue;
                }
                var refresh = Mathf.RoundToInt(
                    (float)resolution.refreshRateRatio.value);
                var key = resolution.width + "x" +
                          resolution.height + "@" + refresh;
                if (!seen.Add(key))
                {
                    continue;
                }
                result.Add(new ResolutionChoice
                {
                    Width = resolution.width,
                    Height = resolution.height,
                    RefreshRate = refresh,
                    Label = L.Get(
                        "falsum.preferences.display.resolutionValue",
                        ("width", resolution.width),
                        ("height", resolution.height),
                        ("refresh", refresh)),
                });
            }
            if (result.Count == 0)
            {
                result.Add(new ResolutionChoice
                {
                    Width = preferences.Display.ResolutionWidth,
                    Height = preferences.Display.ResolutionHeight,
                    RefreshRate =
                        preferences.Display.ResolutionRefreshRate,
                    Label = L.Get(
                        "falsum.preferences.display.resolutionValue",
                        ("width", preferences.Display.ResolutionWidth),
                        ("height", preferences.Display.ResolutionHeight),
                        ("refresh", preferences.Display.ResolutionRefreshRate)),
                });
            }
            return result;
        }
    }
}
