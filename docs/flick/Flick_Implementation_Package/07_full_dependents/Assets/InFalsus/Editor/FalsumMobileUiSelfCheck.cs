using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InFalsus.Gameplay.Falsum.UI;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace InFalsus.EditorTools
{
    public static class FalsumMobileUiSelfCheck
    {
        [MenuItem("InFalsus/Falsum/Run Mobile UI Regression Check")]
        public static void Run()
        {
            var checks = new List<string>();
            var previous = FalsumMobileShell.RuntimeOverride;
            FalsumEditorView view = null;
            GameObject host = null;
            var passed = false;
            var error = string.Empty;
            try
            {
                var preferences = new FalsumPreferences();
                preferences.Grid.TimeSubdivision = 6;
                var reloaded = FalsumPreferencesMigration.FromDisk(JsonUtility.ToJson(preferences)).Preferences;
                Require(reloaded.Grid.TimeSubdivision == 6,
                    "The time subdivision must survive saving and reloading.");
                var legacy = FalsumPreferencesMigration.FromDisk("{\"Version\":9,\"Grid\":{\"LineWidthScale\":1.5}}").Preferences;
                Require(legacy.Grid.TimeSubdivision == 4 && legacy.Grid.LineWidthScale == 1.5f,
                    "Old preferences must preserve grid styling and default to four time divisions.");
                preferences.Grid.TimeSubdivision = 0;
                preferences.Normalize();
                Require(preferences.Grid.TimeSubdivision == 4, "Invalid stored subdivisions must recover.");
                checks.Add("grid_time_subdivision_round_trip_and_legacy_defaults");
                FalsumMobileShell.RuntimeOverride = true;
                var tree = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor").Instantiate();
                var root = tree.Q<VisualElement>("FalsumRoot");
                var button = root.Q<Button>("MobileUndo");
                var baseline = ClickHandlers(button);
                for (var i = 0; i < 3; i++)
                {
                    var shell = FalsumMobileShell.Attach(root);
                    Require(shell != null, "Mobile shell failed to attach.");
                    var invocations = 0;
                    shell.UndoRequested += () => invocations++;
                    Click(button);
                    Require(invocations == 1, "One undo click must raise exactly one action.");
                    Require(root.Q<ScrollView>(className: "mobile-rail-scroll") != null,
                        "Rail scrolling is unavailable.");
                    shell.Detach();
                    Require(ClickHandlers(button) == baseline, "Detach leaked a click callback.");
                    Require(!root.ClassListContains("is-mobile"), "Detach left mobile styling active.");
                }
                checks.Add("shell_callbacks_and_round_trips");

                host = new GameObject("MobileUiSelfCheckHost");
                view = new FalsumEditorView(host.transform);
                view.Build();
                view.MountMobileShellIfNeeded();
                var document = GameObject.Find("FalsumEditorDocument").GetComponent<UIDocument>();
                var visual = document.rootVisualElement;
                view.ShowPreferences(new FalsumPreferences(), null, null);
                view.PreferencesView.DebugShowTab("Editing");
                var subdivision = visual.Q<TextField>("PreferencesGridTimeSubdivision");
                Require(subdivision != null, "Grid time subdivision is missing from preferences.");
                subdivision.value = "０";
                Require(subdivision.ClassListContains("is-invalid"), "Zero subdivisions must be rejected.");
                subdivision.value = "６";
                Require(subdivision.value == "6" && !subdivision.ClassListContains("is-invalid"),
                    "Full-width subdivision input must sanitize and recover.");
                view.PreferencesView.CloseImmediate();
                checks.Add("grid_time_subdivision_preferences_validation");
                Invoke(view, "ToggleMenuFromMobile");
                var menu = visual.Q<VisualElement>("MobileMenuPanel");
                Require(menu != null && !menu.ClassListContains("hidden"),
                    "The menu must open on the first request.");
                Require(menu.Query<VisualElement>(className: "mobile-menu-column").ToList().Count == 4,
                    "The menu must expose File, Edit, View and Tools columns.");
                Invoke(view, "ToggleMenuFromMobile");
                Require(menu.ClassListContains("hidden"), "Second menu request must close it.");
                checks.Add("first_menu_click_and_four_columns");

                view.SetEventsPanelOpen(true);
                Require(view.IsModalOpen &&
                    visual.Q<VisualElement>("EventsPanel").ClassListContains("is-mobile-window"),
                    "Mobile events must use the modal window.");
                view.SetEventsPanelOpen(false);
                checks.Add("events_modal_open_close");

                string committed = null;
                var commitCount = 0;
                Action<string> commit = value => { committed = value; commitCount++; };
                Invoke(view, "ShowMobileNumericEntry", "test", "1 / 24", true, commit);
                var keypad = visual.Q<VisualElement>("MobileNumericOverlay");
                Require(!view.IsModalOpen && keypad.pickingMode == PickingMode.Ignore,
                    "The docked keypad must not block the chart.");
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "7"));
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "/"));
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "4"));
                var confirm = keypad.Q<Button>(className: "is-confirm");
                Click(confirm);
                Click(confirm);
                Require(committed == "7/4" && commitCount == 1 &&
                    visual.Q<VisualElement>("MobileNumericOverlay") == null,
                    "Fraction entry must replace the initial value and commit once.");
                Invoke(view, "ShowMobileNumericEntry", "test", "1", false, commit);
                keypad = visual.Q<VisualElement>("MobileNumericOverlay");
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "-"));
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "4"));
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "."));
                Click(keypad.Query<Button>().ToList().Find(item => item.text == "7"));
                Click(keypad.Q<Button>(className: "is-confirm"));
                Require(committed == "-4.7", "Decimal and negative numeric entry are unavailable.");
                Invoke(view, "ShowMobileNumericEntry", "test", "20", false, commit);
                Invoke(view, "CloseMobileNumericEntry");
                Require(committed == "-4.7", "Cancel must not commit.");
                checks.Add("keypad_fraction_decimal_cancel");

                view.SetEventsPanelOpen(true);
                Invoke(view, "ShowMobileNumericEntry", "test", "20", false, commit);
                keypad = visual.Q<VisualElement>("MobileNumericOverlay");
                var eventsOverlay = visual.Q<VisualElement>("MobileEventsOverlay");
                Require(keypad.parent == eventsOverlay.parent &&
                    keypad.parent.IndexOf(keypad) > keypad.parent.IndexOf(eventsOverlay),
                    "The event keypad must render above the event window and scrim.");
                Invoke(view, "CloseMobileNumericEntry");
                Require(view.IsEventsPanelOpen, "Closing the keypad must preserve the event window.");
                view.SetEventsPanelOpen(false);
                checks.Add("event_keypad_layer_and_window_preservation");

                Invoke(view, "SetMobileLayoutForPages", false);
                Require(visual.Q<VisualElement>("MobileEventsOverlay") == null &&
                    visual.Q<VisualElement>("MobileNumericOverlay") == null &&
                    !visual.Q<VisualElement>("EventsPanel").ClassListContains("is-mobile-window"),
                    "Desktop restoration left a mobile window behind.");
                checks.Add("desktop_window_restoration");
                passed = true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                Debug.LogException(exception);
            }
            finally
            {
                view?.Dispose();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                FalsumMobileShell.RuntimeOverride = previous;
                var path = Path.Combine(Application.dataPath, "../Diagnostics/FalsumMobileUiSelfCheck.json");
                File.WriteAllText(path, JsonUtility.ToJson(
                    new Report { passed = passed, checks = checks.ToArray(), error = error }, true));
            }
            if (!passed) throw new InvalidOperationException(error);
            Debug.Log("[Falsum] Mobile UI regression checks passed.");
        }

        private static void Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, "Missing regression target: " + name);
            method.Invoke(target, arguments);
        }

        private static Delegate Handlers(Button button)
        {
            Require(button != null, "Missing regression button.");
            var field = typeof(Clickable).GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Clickable event could not be inspected.");
            return field.GetValue(button.clickable) as Delegate;
        }

        private static int ClickHandlers(Button button)
        {
            return Handlers(button)?.GetInvocationList().Length ?? 0;
        }

        private static void Click(Button button)
        {
            Handlers(button)?.DynamicInvoke();
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
            public string error;
        }
    }
}
