using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// "Advanced" page: cache housekeeping and the global reset.
    ///
    /// This used to be a catch-all holding grid rendering, out-of-bounds
    /// clamping, cache actions and the reset. Grid and clamping moved to the
    /// editing page; what remains here is genuinely "rarely touched, affects
    /// the whole editor" work. Product information has its own About page.
    /// </summary>
    internal sealed class FalsumAdvancedPreferencesPage
    {
        private Label _cacheSizeLabel;

        public void Build(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            if (FalsumUiStyleSwitcher.Available)
            {
                BuildExperimental(content, preferences, host);
            }
            if (FalsumDeveloperSwitches.Available)
            {
                BuildDeveloper(content, preferences, host);
            }
            BuildMaintenance(content, preferences, host);
        }

        /// <summary>
        /// Developer-only controls. Present in the editor and in development
        /// builds, absent from a release player (FalsumDeveloperSwitches).
        ///
        /// The mobile-layout switch exists so the Android phone layout can
        /// be inspected on this machine: there was no Android build chain,
        /// and reviewing a phone layout from screenshots alone hides exactly
        /// the things that go wrong (see the five USS defects recorded in
        /// 2026-11-09-falsum-mobile-editor-ui-design §11.2, every one of
        /// which a code review had passed).
        /// </summary>
        private static void BuildDeveloper(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.developer.group"));
            var toggle = new Toggle
            {
                name = "PreferencesForceMobileLayout",
                value = preferences.Experimental.ForceMobileLayout,
            };
            toggle.AddToClassList("preferences-field");
            toggle.RegisterValueChangedCallback(
                evt => host.PreviewMobileLayout(evt.newValue));
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.developer.mobileLayout"),
                L.Get("falsum.preferences.developer.mobileLayoutDescription"),
                string.Empty,
                string.Empty,
                toggle,
                "stretch"));
            // Control size. A slider rather than a dropdown: the useful
            // values are a continuum, and the user's complaint was "a bit
            // too big", not "pick one of four".
            var scale = new Slider(
                FalsumMobileScaleBounds.Min,
                FalsumMobileScaleBounds.Max)
            {
                name = "PreferencesMobileUiScale",
                value = preferences.Experimental.MobileUiScale,
            };
            scale.AddToClassList("preferences-field");
            scale.RegisterValueChangedCallback(
                evt => host.PreviewMobileScale(evt.newValue));
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.developer.mobileScale"),
                L.Get("falsum.preferences.developer.mobileScaleDescription"),
                string.Empty,
                string.Empty,
                scale,
                "stretch"));
            content.Add(group);
        }

        /// <summary>
        /// Live style preview; Classic remains the original-UI fallback.
        /// </summary>
        private static void BuildExperimental(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var experimentalGroup = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.advanced.experimentalGroup"));
            var styleChoices = new List<string>
            {
                L.Get("falsum.preferences.advanced.uiStyle.classic"),
                L.Get("falsum.preferences.advanced.uiStyle.studio"),
                L.Get("falsum.preferences.advanced.uiStyle.canvas"),
                L.Get("falsum.preferences.advanced.uiStyle.infalsus"),
            };
            var style = new DropdownField
            {
                name = "PreferencesUIStyle",
                choices = styleChoices,
                index = (int)preferences.Experimental.UIStyle,
            };
            style.AddToClassList("preferences-field");
            style.RegisterValueChangedCallback(
                evt =>
                {
                    if (style.index < 0)
                    {
                        return;
                    }
                    preferences.Advanced.UIStyle =
                        (FalsumUiStyle)style.index;
                    host.PreviewUiStyle(preferences.Experimental.UIStyle);
                });
            experimentalGroup.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.advanced.uiStyle.label"),
                string.Empty,
                string.Empty,
                string.Empty,
                style,
                "stretch"));
            content.Add(experimentalGroup);
        }

        private void BuildMaintenance(
            VisualElement content,
            FalsumPreferences preferences,
            IFalsumPreferencesPageHost host)
        {
            var group = FalsumPreferencesWidgets.CreateGroup(
                L.Get("falsum.preferences.advanced.group"));
            var cacheControls = new VisualElement();
            cacheControls.style.flexDirection = FlexDirection.Row;
            cacheControls.style.alignItems = Align.Center;
            cacheControls.style.justifyContent = Justify.FlexEnd;
            cacheControls.style.flexGrow = 1;
            _cacheSizeLabel = new Label();
            _cacheSizeLabel.AddToClassList("preferences-row-description");
            _cacheSizeLabel.style.marginRight = 12;
            cacheControls.Add(_cacheSizeLabel);
            var cache = new Button
            {
                text = L.Get("falsum.preferences.advanced.openCache"),
            };
            cache.AddToClassList("preferences-inline-button");
            cache.clicked += delegate
            {
                host.OpenCacheFolder();
            };
            cacheControls.Add(cache);
            var clearCache = new Button
            {
                text = L.Get("falsum.preferences.advanced.clearCache"),
            };
            clearCache.AddToClassList("preferences-inline-button");
            clearCache.style.marginLeft = 8;
            clearCache.clicked += delegate
            {
                host.ClearCache();
            };
            cacheControls.Add(clearCache);
            RefreshCacheSize();
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.advanced.cache"),
                L.Get("falsum.preferences.advanced.cacheDescription"),
                string.Empty,
                string.Empty,
                cacheControls,
                "stretch"));

            var reset = new Button
            {
                text = L.Get("falsum.preferences.resetDefaults"),
            };
            reset.AddToClassList("preferences-inline-button");
            reset.clicked += delegate
            {
                host.ResetAllDefaults(FalsumPreferencesTab.Advanced);
            };
            group.Add(FalsumPreferencesWidgets.CreateRow(
                L.Get("falsum.preferences.advanced.resetAll"),
                L.Get(
                    "falsum.preferences.advanced.resetAllDescription"),
                string.Empty,
                string.Empty,
                reset,
                string.Empty));
            content.Add(group);
        }

        /// <summary>Refreshes the cache-size readout after a cache action.</summary>
        public void RefreshCacheSize()
        {
            if (_cacheSizeLabel == null)
            {
                return;
            }
            _cacheSizeLabel.text = L.Get(
                "falsum.preferences.advanced.cacheSize",
                ("size", FalsumCacheStore.FormatSize(
                    FalsumCacheStore.GetSizeBytes())));
        }
    }
}
