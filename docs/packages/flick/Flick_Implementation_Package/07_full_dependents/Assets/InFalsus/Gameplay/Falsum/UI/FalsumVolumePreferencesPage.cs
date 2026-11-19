using System;
using System.Globalization;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "Volume" page: five 0..1 sliders over the audio group. Pure global
    /// preferences, no cross-page state.
    /// </summary>
    internal static class FalsumVolumePreferencesPage
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        public static void Build(
            VisualElement content,
            FalsumPreferences preferences)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.tab.volume"));
            AddRow(
                group,
                L.Get("falsum.preferences.volume.music"),
                L.Get("falsum.preferences.volume.musicDescription"),
                preferences.Audio.MusicVolume,
                value =>
                {
                    preferences.Audio.MusicVolume = value;
                });
            AddRow(
                group,
                L.Get("falsum.preferences.volume.skyArea"),
                L.Get(
                    "falsum.preferences.volume.skyAreaDescription"),
                preferences.Audio.SkyAreaVolume,
                value =>
                {
                    preferences.Audio.SkyAreaVolume = value;
                });
            AddRow(
                group,
                L.Get("falsum.preferences.volume.tap"),
                L.Get("falsum.preferences.volume.tapDescription"),
                preferences.Audio.TapVolume,
                value =>
                {
                    preferences.Audio.TapVolume = value;
                });
            AddRow(
                group,
                L.Get("falsum.preferences.volume.hold"),
                L.Get("falsum.preferences.volume.holdDescription"),
                preferences.Audio.HoldVolume,
                value =>
                {
                    preferences.Audio.HoldVolume = value;
                });
            AddRow(
                group,
                L.Get("falsum.preferences.volume.flick"),
                L.Get("falsum.preferences.volume.flickDescription"),
                preferences.Audio.FlickVolume,
                value =>
                {
                    preferences.Audio.FlickVolume = value;
                });
            content.Add(group);
        }

        private static void AddRow(
            VisualElement group,
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
            group.Add(FalsumPreferencesWidgets.CreateRow(
                title,
                description,
                string.Empty,
                string.Empty,
                control,
                "stretch"));
        }

        private static string Percent(float value)
        {
            return Mathf.RoundToInt(
                       Mathf.Clamp01(value) * 100.0f)
                   .ToString(Invariant) +
                   "%";
        }
    }
}
