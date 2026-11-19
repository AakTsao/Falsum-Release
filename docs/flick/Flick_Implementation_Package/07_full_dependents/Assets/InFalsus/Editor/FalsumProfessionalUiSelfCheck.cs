using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.EditorTools
{
    public static class FalsumProfessionalUiSelfCheck
    {
        public static void RunAll()
        {
            Run();
            FalsumSelfCheck.Run();
            FalsumMobileUiSelfCheck.Run();
        }

        [MenuItem("InFalsus/Falsum/Run Professional UI Self Check")]
        public static void Run()
        {
            var checks = new List<string>();
            var report = new Report();
            var path = FalsumPreferencesStore.FilePath;
            var existed = File.Exists(path);
            var saved = existed ? File.ReadAllBytes(path) : null;
            try
            {
                Require(FalsumUiStyleSwitcher.Available, "UI style picker is unavailable.");
                Require(FalsumPreferences.CreateDefault().Advanced.UIStyle == FalsumUiStyle.InFalsus,
                    "New profiles do not use In Falsus.");
                var legacy = FalsumPreferencesMigration.FromDisk(
                    "{\"Version\":10,\"Experimental\":{\"UIStyle\":0}}").Preferences;
                Require(legacy.Advanced.UIStyle == FalsumUiStyle.InFalsus,
                    "Historical forced Classic did not upgrade.");
                var unknown = FalsumPreferencesMigration.FromDisk(
                    "{\"Version\":10,\"Experimental\":{\"UIStyle\":99}}").Preferences;
                unknown.Normalize();
                Require(unknown.Advanced.UIStyle == FalsumUiStyle.Classic,
                    "Unknown styles must remain on the safe Classic fallback.");
                checks.Add("defaults_legacy_upgrade_and_unknown_fallback");

                var root = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor")
                    .Instantiate().Q<VisualElement>("FalsumRoot");
                var sheet = Resources.Load<StyleSheet>("Falsum/UI/FalsumInFalsus");
                Require(sheet != null && Resources.Load<VectorImage>("Falsum/UI/Icons/frame-corners") != null,
                    "New UI paint resources are missing.");
                Require(Resources.Load<Font>("Falsum/Exo-Medium") != null,
                    "The bundled numeric face is missing.");
                var baselineCount = root.styleSheets.count;
                FalsumUiStyles.Apply(root, FalsumUiStyle.InFalsus);
                FalsumUiStyles.Apply(root, FalsumUiStyle.InFalsus);
                Require(root.ClassListContains("is-infalsus") && root.styleSheets.Contains(sheet) &&
                        root.styleSheets.count == baselineCount + 1, "Applying the theme duplicates sheets.");
                FalsumUiStyles.Apply(root, FalsumUiStyle.Classic);
                Require(!root.ClassListContains("is-infalsus") && !root.styleSheets.Contains(sheet) &&
                        root.styleSheets.count == baselineCount, "Classic leaves new theme state behind.");
                checks.Add("theme_resources_idempotence_and_classic_cleanup");

                foreach (FalsumUiStyle style in Enum.GetValues(typeof(FalsumUiStyle)))
                {
                    var preferences = FalsumPreferences.CreateDefault();
                    preferences.Advanced.UIStyle = style;
                    preferences.Grid.TimeSubdivision = 6;
                    preferences.Experimental.MobileUiScale = 0.9f;
                    FalsumPreferencesStore.Save(preferences);
                    FalsumPreferencesStore.ResetCache();
                    var loaded = FalsumPreferencesStore.Load();
                    Require(loaded.Advanced.UIStyle == style && loaded.Experimental.UiStyleChosen,
                        "Saved UI style was lost after clearing the cache: " + style);
                    Require(loaded.Grid.TimeSubdivision == 6 &&
                            Mathf.Abs(loaded.Experimental.MobileUiScale - 0.9f) < 0.001f,
                        "Loading the style overwrote independent mobile/grid preferences.");
                    checks.Add("disk_round_trip_" + style);
                }

                var modal = new VisualElement();
                root.Add(modal);
                var view = new FalsumPreferencesView(root, modal,
                    Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumPreferencesDialog"));
                var source = FalsumPreferences.CreateDefault();
                var sourceJson = JsonUtility.ToJson(source);
                FalsumUiStyles.Apply(root, source.Advanced.UIStyle);
                var previewed = source.Advanced.UIStyle;
                view.UiStylePreviewRequested += style =>
                {
                    previewed = style;
                    FalsumUiStyles.Apply(root, style);
                };
                view.Show(source, null, null);
                view.DebugFlushInitialTabBuild();
                view.DebugShowTab("Advanced");
                var picker = modal.Q<DropdownField>("PreferencesUIStyle");
                Require(picker != null && picker.choices.Count == 4, "The fallback picker is missing.");
                view.DebugSetUiStyle(FalsumUiStyle.Classic);
                Require(previewed == FalsumUiStyle.Classic && !root.ClassListContains("is-infalsus"),
                    "Classic cannot be previewed from preferences.");
                typeof(FalsumPreferencesView).GetMethod("RequestCancel",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                Require(previewed == FalsumUiStyle.InFalsus && root.ClassListContains("is-infalsus") &&
                        JsonUtility.ToJson(source) == sourceJson, "Cancel did not restore the original UI/draft.");
                view.CloseImmediate();
                checks.Add("picker_live_classic_preview_and_cancel_restore");
                report.passed = true;
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                Debug.LogException(exception);
            }
            finally
            {
                if (existed) File.WriteAllBytes(path, saved);
                else if (File.Exists(path)) File.Delete(path);
                FalsumPreferencesStore.ResetCache();
                report.checks = checks.ToArray();
                var directory = Path.Combine(Application.dataPath, "../Diagnostics/UiProfessional");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "FalsumProfessionalUiSelfCheck.json"),
                    JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string[] checks;
            public string error = string.Empty;
        }
    }
}
