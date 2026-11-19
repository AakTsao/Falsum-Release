using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.EditorTools
{
    public static class FalsumMobileUiCapture
    {
        private const string Active = "Falsum.MobileUiAudit.Active";
        private const string Step = "Falsum.MobileUiAudit.Step";
        private const string Viewport = "Falsum.MobileUiAudit.Viewport";
        private const string TimeKey = "Falsum.MobileUiAudit.Time";
        private static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(855, 405), new Vector2Int(640, 360),
            new Vector2Int(740, 320), new Vector2Int(855, 405),
            new Vector2Int(640, 360), new Vector2Int(512, 320),
        };
        private static RenderTexture _target;
        private static UIDocument _document;
        private static FalsumEditorView _view;
        private static readonly List<string> Checks = new List<string>();

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("InFalsus/Falsum/Capture Mobile UI Audit")]
        public static void Run()
        {
            Environment.SetEnvironmentVariable("FALSUM_MOBILE_SHELL", "1");
            EditorSceneManager.OpenScene(
                "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity",
                OpenSceneMode.Single);
            SessionState.SetBool(Active, true);
            SessionState.SetInt(Step, 0);
            SessionState.SetInt(Viewport, 0);
            AdvanceTime();
            Install();
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(TimeKey, 0) < 0.8)
            {
                if (_document != null && _target != null) RenderWorld();
                return;
            }
            try
            {
                var index = SessionState.GetInt(Viewport, 0);
                if (index >= Sizes.Length) { Finish(null); return; }
                var step = SessionState.GetInt(Step, 0);
                if (_view == null)
                {
                    var editor = UnityEngine.Object.FindFirstObjectByType<FalsumChartEditor>();
                    if (editor == null) return;
                    if (editor.DebugDisclaimerOpen) editor.DebugAcceptDisclaimer();
                    _view = typeof(FalsumChartEditor).GetField("_view",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor) as FalsumEditorView;
                    _document = GameObject.Find("FalsumEditorDocument").GetComponent<UIDocument>();
                    Require(_view != null, "Runtime editor view is missing.");
                    editor.enabled = false;
                }
                if (step == 0)
                {
                    _view.PreferencesView.CloseImmediate();
                    _view.SetEventsPanelOpen(false);
                    Invoke("CloseMobileNumericEntry");
                    if (_target != null) _target.Release();
                    var size = Sizes[index];
                    _target = new RenderTexture(size.x * 2, size.y * 2, 24, RenderTextureFormat.ARGB32);
                    _target.Create();
                    _document.panelSettings.referenceResolution = size;
                    _document.panelSettings.targetTexture = _target;
                    FalsumMobileShell.DebugSafeAreaOverride = new Rect(
                        Screen.width * 24.0f / size.x, Screen.height * 6.0f / size.y,
                        Screen.width * (size.x - 32.0f) / size.x,
                        Screen.height * (size.y - 6.0f) / size.y);
                    L.SetLocale(index == 3 ? "en-US" : "zh-CN");
                    _view.MobileShell.ApplyScale(index >= 4
                        ? FalsumMobileShell.MaxScale : FalsumMobileShell.DefaultScale);
                    L.ApplyStaticText(_document.rootVisualElement);
                    RenderWorld();
                }
                else if (step == 1)
                {
                    _view.MobileShell.RefreshLayout();
                    RenderWorld();
                }
                else if (step == 2)
                {
                    CheckBaseLayout(index);
                    Capture(index, "shell");
                    Invoke("ToggleMenuFromMobile");
                }
                else if (step == 3)
                {
                    CheckWindow("MobileMenuPanel", "mobile-menu-scroll");
                    Capture(index, "menu");
                    Invoke("ToggleMenuFromMobile");
                    _view.ShowPreferences(new FalsumPreferences(), null, null);
                    _view.PreferencesView.DebugShowTab("Editing");
                }
                else if (step == 4)
                {
                    CheckWindow("PreferencesOverlay", "falsum-preferences-window");
                    Capture(index, "preferences");
                    _view.PreferencesView.CloseImmediate();
                    var events = new List<ChartEvent>();
                    for (var i = 0; i < 12; i++)
                    {
                        events.Add(new ChartEvent
                        {
                            Kind = i % 2 == 0 ? ChartEventKind.Bpm : ChartEventKind.TrackSpeed,
                            TimeMs = i * 16000, Bpm = 150 + i, Division = 4, Multiplier = 1.0,
                        });
                    }
                    _view.SetEventsPanelOpen(true);
                    _view.ShowEvents(events, true);
                }
                else if (step == 5)
                {
                    CheckWindow("MobileEventsOverlay", "events-panel");
                    Capture(index, "events");
                    TapNumericField(_document.rootVisualElement.Q<VisualElement>("EventsPanel")
                        .Q<TextField>(className: "event-input"));
                }
                else if (step == 6)
                {
                    CheckWindow("MobileNumericOverlay", "mobile-numeric-window");
                    var keypad = _document.rootVisualElement.Q<VisualElement>("MobileNumericOverlay");
                    var key = keypad.Q<Button>(className: "is-confirm");
                    var picked = key.panel.Pick(key.worldBound.center);
                    Require(picked == key || key.Contains(picked),
                        "The event window blocks keypad pointer input.");
                    Capture(index, "events-keypad");
                    Invoke("CloseMobileNumericEntry");
                    Require(_view.IsEventsPanelOpen, "Closing the keypad also closes the event window.");
                    Checks.Add("event_keypad_pointer_and_preservation_" + Sizes[index]);
                    _view.SetEventsPanelOpen(false);
                    var field = _document.rootVisualElement.Q<TextField>("MobileTimeField");
                    TapNumericField(field);
                    Checks.Add("numeric_pointer_entry_" + Sizes[index]);
                }
                else if (step == 7)
                {
                    CheckWindow("MobileNumericOverlay", "mobile-numeric-window");
                    Capture(index, "keypad");
                    Invoke("CloseMobileNumericEntry");
                    _view.ShowProperties(new ChartNote
                    {
                        Kind = NoteKind.SkyArea, TimeMs = 123456, DurationMs = 111111,
                        StartPos = 9.0 / 24.0, StartWidth = 18.0 / 24.0,
                        EndPos = 7.0 / 24.0, EndWidth = 17.0 / 24.0,
                        StartCellCount = 24, EndCellCount = 24,
                    });
                    _view.MobileShell.SetFlowValue(5.4f);
                    _view.MobileShell.SetProgress(0.52f, "01:32 / 02:57");
                }
                else if (step == 8)
                {
                    CheckProperties();
                    Capture(index, "properties");
                    // Exercise the gesture on a separate shell with no editor
                    // subscriptions; tests must not write the user's preferences.
                    _gestureTree = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor").Instantiate();
                    _gestureTree.style.position = Position.Absolute;
                    _gestureTree.style.left = _gestureTree.style.right = 0;
                    _gestureTree.style.top = _gestureTree.style.bottom = 0;
                    _gestureTree.style.opacity = 0;
                    _gestureRoot = _gestureTree.Q<VisualElement>("FalsumRoot");
                    _document.rootVisualElement.Add(_gestureTree);
                    _gestureShell = FalsumMobileShell.Attach(_gestureRoot);
                    _gestureShell.ApplyScale(index >= 4 ? FalsumMobileShell.MaxScale : FalsumMobileShell.DefaultScale);
                    _gestureShell.SubdivisionRequested += value => { _gestureValue = value; _gestureCommits++; };
                    _gestureShell.GridToggleRequested += () => _gestureToggles++;
                    _gestureCommits = _gestureToggles = 0;
                }
                else if (step == 9)
                {
                    var grid = _gestureRoot.Q<Button>("MobileGrid");
                    Pointer(grid, EventType.MouseDown, grid.worldBound.center);
                    Pointer(grid, EventType.MouseUp, grid.worldBound.center);
                    Require(_gestureToggles == 1 && _gestureCommits == 0,
                        "Grid tap must only toggle visibility. Toggles=" + _gestureToggles +
                        " commits=" + _gestureCommits + " button=" + grid.worldBound +
                        " root=" + _gestureRoot.worldBound);
                    Pointer(grid, EventType.MouseDown, grid.worldBound.center);
                }
                else if (step == 10)
                {
                    var popup = _gestureRoot.Q<VisualElement>("MobileGridPicker");
                    Require(popup != null, "Holding the grid button did not open subdivisions.");
                    var grid = _gestureRoot.Q<Button>("MobileGrid");
                    Require(popup.worldBound.xMax <= grid.worldBound.xMin &&
                        Contains(_gestureShell.SafeBounds, popup.worldBound), "The grid picker must open to the left inside safe bounds.");
                    var option = popup.Query<Label>().ToList()[3];
                    Pointer(grid, EventType.MouseMove, option.worldBound.center);
                    Require(_gestureCommits == 0, "Sliding must not commit before release.");
                    Pointer(grid, EventType.MouseUp, option.worldBound.center);
                    Require(_gestureValue == "6" && _gestureCommits == 1 && _gestureToggles == 1,
                        "Releasing on six must commit once without toggling visibility.");
                    Pointer(grid, EventType.MouseDown, grid.worldBound.center);
                }
                else if (step == 11)
                {
                    var grid = _gestureRoot.Q<Button>("MobileGrid");
                    Pointer(grid, EventType.MouseUp, _gestureShell.SafeBounds.center);
                    Require(_gestureCommits == 1 && _gestureToggles == 1 && _gestureRoot.Q("MobileGridPicker") == null,
                        "Releasing outside the picker must cancel without changing the grid.");
                    Pointer(grid, EventType.MouseDown, grid.worldBound.center);
                }
                else if (step == 12)
                {
                    Require(_gestureRoot.Q("MobileGridPicker") != null, "Repeated grid holds must still open.");
                    _gestureShell.Detach();
                    Require(_gestureRoot.Q("MobileGridPicker") == null, "Detach must close a held grid picker.");
                    _gestureTree.RemoveFromHierarchy();
                    _gestureTree = _gestureRoot = null;
                    _gestureShell = null;
                    Checks.Add("grid_hold_slide_release_cancel_and_detach_" + Sizes[index]);
                    var grid = _document.rootVisualElement.Q<Button>("MobileGrid");
                    Pointer(grid, EventType.MouseDown, grid.worldBound.center);
                }
                else if (step == 13)
                {
                    var popup = _document.rootVisualElement.Q<VisualElement>("MobileGridPicker");
                    Require(popup != null, "The real editor grid hold did not open.");
                    var options = popup.Query<Label>().ToList();
                    Require(string.Join(",", options.ConvertAll(option => option.text)) == "2,3,4,6,8",
                        "The mobile picker must offer exactly 2, 3, 4, 6 and 8.");
                    Pointer(_document.rootVisualElement.Q<Button>("MobileGrid"),
                        EventType.MouseMove, options[3].worldBound.center);
                }
                else
                {
                    var popup = _document.rootVisualElement.Q<VisualElement>("MobileGridPicker");
                    Require(popup.Query<Label>().ToList()[3].ClassListContains("is-hovered"),
                        "Sliding onto six must highlight six.");
                    Capture(index, "grid-hold");
                    var grid = _document.rootVisualElement.Q<Button>("MobileGrid");
                    using (var source = PointerDownEvent.GetPooled(new Event
                        { type = EventType.MouseDown, button = 0, mousePosition = grid.worldBound.center }))
                    using (var cancel = PointerCancelEvent.GetPooled(source))
                    {
                        cancel.target = grid;
                        grid.SendEvent(cancel);
                    }
                    Require(_document.rootVisualElement.Q("MobileGridPicker") == null,
                        "A canceled pointer must close the grid picker.");
                    _view.ShowProperties(null);
                    SessionState.SetInt(Viewport, index + 1);
                    SessionState.SetInt(Step, 0);
                    AdvanceTime();
                    return;
                }
                SessionState.SetInt(Step, step + 1);
                AdvanceTime();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(exception);
            }
        }

        private static VisualElement _gestureTree;
        private static VisualElement _gestureRoot;
        private static FalsumMobileShell _gestureShell;
        private static string _gestureValue;
        private static int _gestureCommits;
        private static int _gestureToggles;

        private static void Pointer(VisualElement target, EventType type, Vector2 position)
        {
            var input = new Event { type = type, button = 0, mousePosition = position };
            if (type == EventType.MouseDown)
            {
                using (var evt = PointerDownEvent.GetPooled(input))
                {
                    evt.target = target;
                    target.SendEvent(evt);
                }
            }
            else if (type == EventType.MouseUp)
            {
                using (var evt = PointerUpEvent.GetPooled(input))
                {
                    evt.target = target;
                    target.SendEvent(evt);
                }
            }
            else
            {
                using (var evt = PointerMoveEvent.GetPooled(input))
                {
                    evt.target = target;
                    target.SendEvent(evt);
                }
            }
        }

        private static void CheckProperties()
        {
            var panel = _document.rootVisualElement.Q<VisualElement>("NoteProperties");
            var rows = panel.Q<ScrollView>("PropertyRows");
            Require(rows.contentContainer.childCount == 8, "SkyArea must show all eight property rows.");
            foreach (var row in rows.Query<VisualElement>(className: "property-row").ToList())
                Require(Contains(rows.contentViewport.worldBound, row.worldBound), "A property row is clipped.");
            foreach (var field in rows.Query<TextField>().ToList())
            {
                var text = field.Q<TextElement>();
                Require(text != null && text.resolvedStyle.fontSize <= 11.1f,
                    "Property inputs still inherit desktop font sizes.");
                Require(text.MeasureTextSize(field.value, 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined).x <= field.worldBound.width + 1,
                    "A property value is wider than its field.");
            }
            Require(Contains(panel.worldBound, panel.Q<Button>("JumpToNote").worldBound), "The property footer is clipped.");
            var flow = _document.rootVisualElement.Q<Slider>("MobileFlowSlider");
            var tracker = flow.Q<VisualElement>(className: "unity-base-slider__tracker");
            var dragger = flow.Q<VisualElement>(className: "unity-base-slider__dragger");
            Require(Mathf.Abs(tracker.worldBound.center.y - flow.worldBound.center.y) < 1.5f &&
                Mathf.Abs(dragger.worldBound.center.y - flow.worldBound.center.y) < 1.5f,
                "The flow tracker or handle is not vertically centered.");
            Checks.Add("complete_properties_fraction_text_and_centered_flow");
        }

        private static void TapNumericField(TextField field)
        {
            Require(field != null, "Numeric input field is missing.");
            using (var pointer = PointerDownEvent.GetPooled(
                new Event { type = EventType.MouseDown, button = 0,
                    mousePosition = field.worldBound.center }))
            {
                field.SendEvent(pointer);
            }
            Require(_document.rootVisualElement.Q("MobileNumericOverlay") != null,
                "Tapping a numeric field does not open the mobile keypad. Bound=" +
                field.ClassListContains("is-mobile-numeric-bound") + " panel=" +
                (field.panel != null) + " bounds=" + field.worldBound);
        }

        private static void RenderWorld()
        {
            var camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null) return;
            var oldTarget = camera.targetTexture;
            camera.targetTexture = _target;
            camera.Render();
            camera.targetTexture = oldTarget;
            _document.rootVisualElement.MarkDirtyRepaint();
        }

        private static void CheckBaseLayout(int index)
        {
            var root = _document.rootVisualElement.Q<VisualElement>("FalsumRoot");
            var safe = _view.MobileShell.SafeBounds;
            Require(Mathf.Abs(root.layout.width - Sizes[index].x) < 2 &&
                Mathf.Abs(root.layout.height - Sizes[index].y) < 2,
                "Capture is not using the requested UI Toolkit viewport.");
            foreach (var name in new[] { "MobileLeftBar", "MobileToolRail", "MobileFlowPanel",
                "MobileSongCardHost", "MobileTransportPanel", "MobileProgressStrip" })
            {
                var rect = root.Q<VisualElement>(name).worldBound;
                Require(Contains(safe, rect), name + " exceeds the simulated safe area: " + rect);
            }
            var left = root.Q<VisualElement>("MobileFlowPanel").worldBound;
            var right = root.Q<VisualElement>("MobileSongCardHost").worldBound;
            Require(left.xMax + 8 <= right.xMin, "Floating columns overlap.");
            var multi = root.Q<Button>("MobileMultiSelect");
            Require(Mathf.Abs(multi.worldBound.yMax - safe.yMax + 5) < 2,
                "Multi-select must stay at the lower-left edge.");
            Require(root.Q("MobileSegmentSelect") == null, "The left rail still contains segment selection.");
            Require(root.Q("MobileMetaPanel").resolvedStyle.display == DisplayStyle.None &&
                root.Q("MobileTransportPanel").Query<Button>().ToList().Count == 1,
                "The song transport must only expose the time field and play button.");
            Checks.Add("safe_layout_" + Sizes[index] + "_" + L.Locale);
        }

        private static void CheckWindow(string name, string className)
        {
            var overlay = _document.rootVisualElement.Q<VisualElement>(name);
            Require(overlay != null && overlay.resolvedStyle.display != DisplayStyle.None,
                "Expected window is hidden: " + name);
            var window = overlay.Q<VisualElement>(className: className);
            Require(window != null && window.layout.height > 100 &&
                Contains(overlay.worldBound, window.worldBound), name + " exceeds its viewport.");
            if (name == "MobileNumericOverlay")
            {
                Require(window.worldBound.center.x > overlay.worldBound.center.x &&
                    window.worldBound.yMax >= overlay.worldBound.yMax - 12 &&
                    overlay.resolvedStyle.backgroundColor.a < 0.01f &&
                    overlay.pickingMode == PickingMode.Ignore &&
                    (_view.IsEventsPanelOpen || !_view.IsModalOpen),
                    "The keypad must dock at the bottom-right without obscuring or blocking the chart.");
            }
            foreach (var control in window.Query<VisualElement>().ToList())
            {
                if (control.ClassListContains("preferences-footer") ||
                    control.ClassListContains("preferences-nav") ||
                    control.ClassListContains("events-footer") ||
                    control.ClassListContains("mobile-menu-column") ||
                    control.ClassListContains("is-confirm"))
                {
                    Require(Contains(window.worldBound, control.worldBound),
                        name + " clips a fixed navigation or action area.");
                }
                if (control.ClassListContains("preferences-row-copy"))
                {
                    Require(control.worldBound.width >= Mathf.Min(120.0f, window.worldBound.width * 0.35f),
                        name + " compresses setting text into an unreadable column.");
                }
            }
            Checks.Add("window_" + name + "_" + L.Locale);
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - 2 && inner.xMax <= outer.xMax + 2 &&
                   inner.yMin >= outer.yMin - 2 && inner.yMax <= outer.yMax + 2;
        }

        private static void Capture(int index, string state)
        {
            var old = RenderTexture.active;
            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = _target;
                texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
                texture.Apply();
                var directory = Path.Combine(Application.dataPath, "../Diagnostics/MobileUiAudit");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, Sizes[index].x + "x" + Sizes[index].y +
                    "-" + L.Locale + (index >= 4 ? "-s14" : "") + "-" + state + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log("[Falsum] Mobile UI audit capture: " + path);
            }
            finally
            {
                RenderTexture.active = old;
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static void Invoke(string name, params object[] arguments)
        {
            typeof(FalsumEditorView).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_view, arguments);
        }

        private static void AdvanceTime()
        {
            SessionState.SetFloat(TimeKey, (float)EditorApplication.timeSinceStartup);
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false);
            EditorApplication.update -= Tick;
            FalsumMobileShell.DebugSafeAreaOverride = null;
            if (_document != null) _document.panelSettings.targetTexture = null;
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            File.WriteAllText(Path.Combine(Application.dataPath, "../Diagnostics/FalsumMobileUiCapture.json"),
                JsonUtility.ToJson(new Report
                {
                    passed = error == null, checks = Checks.ToArray(), error = error?.ToString() ?? string.Empty,
                }, true));
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(error == null ? 0 : 1);
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
