using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.Tools;
using InFalsus.Gameplay.Falsum.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.EditorTools
{
    public static class FalsumProfessionalUiCapture
    {
        private const string Active = "Falsum.ProfessionalUiCapture.Active";
        private const string Step = "Falsum.ProfessionalUiCapture.Step";
        private const string Viewport = "Falsum.ProfessionalUiCapture.Viewport";
        private const string Time = "Falsum.ProfessionalUiCapture.Time";
        private static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(1730, 1080), new Vector2Int(1152, 720), new Vector2Int(1366, 768),
            new Vector2Int(860, 600),
        };
        private static UIDocument _document;
        private static FalsumEditorView _view;
        private static RenderTexture _target;
        private static FalsumPackage _package;
        private static readonly List<string> Checks = new List<string>();

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("InFalsus/Falsum/Capture Professional UI Audit")]
        public static void Run()
        {
            Environment.SetEnvironmentVariable("FALSUM_MOBILE_SHELL", "0");
            FalsumMobileShell.RuntimeOverride = false;
            EditorSceneManager.OpenScene("Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity",
                OpenSceneMode.Single);
            SessionState.SetBool(Active, true);
            SessionState.SetInt(Step, 0);
            SessionState.SetInt(Viewport, 0);
            Advance();
            Install();
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Time, 0) < 0.8)
            {
                if (_document != null && _target != null) RenderWorld();
                return;
            }
            try
            {
                var index = SessionState.GetInt(Viewport, 0);
                if (index >= Sizes.Length) { Finish(null); return; }
                if (_view == null)
                {
                    var editor = UnityEngine.Object.FindFirstObjectByType<FalsumChartEditor>();
                    if (editor == null) return;
                    if (editor.DebugDisclaimerOpen) editor.DebugAcceptDisclaimer();
                    _view = typeof(FalsumChartEditor).GetField("_view",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor) as FalsumEditorView;
                    _document = GameObject.Find("FalsumEditorDocument").GetComponent<UIDocument>();
                    _package = FalsumPackage.Load(Path.Combine(Application.streamingAssetsPath, "Falsum/alamode.falsum"));
                    editor.enabled = false;
                }
                var step = SessionState.GetInt(Step, 0);
                switch (step)
                {
                    case 0:
                        _view.SetUiStyle(FalsumUiStyle.InFalsus);
                        L.SetLocale(index == 2 ? "en-US" : "zh-CN");
                        if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
                        _target = new RenderTexture(Sizes[index].x, Sizes[index].y, 24, RenderTextureFormat.ARGB32);
                        _target.Create();
                        _document.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                        _document.panelSettings.scale = 1;
                        _document.panelSettings.targetTexture = _target;
                        var root = _document.rootVisualElement.Q<VisualElement>("FalsumRoot");
                        // Batch Game View dimensions differ from the offscreen
                        // viewport. Derive the same classes from this viewport.
                        root.EnableInClassList("is-compact", Sizes[index].x <= Threshold("CompactScreenWidth") ||
                            Sizes[index].y <= Threshold("CompactScreenHeight"));
                        root.EnableInClassList("is-narrow", Sizes[index].x < Threshold("NarrowScreenWidth"));
                        root.EnableInClassList("is-short", Sizes[index].y < Threshold("ShortScreenHeight"));
                        root.EnableInClassList("is-low-scale", false);
                        RenderWorld();
                        break;
                    case 1:
                        Capture(index, "shell");
                        _view.ShowProperties(new ChartNote
                        {
                            Kind = NoteKind.SkyArea, TimeMs = 123456, DurationMs = 111111,
                            StartPos = 9.0 / 24, StartWidth = 18.0 / 24,
                            EndPos = 7.0 / 24, EndWidth = 17.0 / 24,
                            StartCellCount = 24, EndCellCount = 24,
                        });
                        break;
                    case 2:
                        Capture(index, "properties");
                        var events = new List<ChartEvent>();
                        for (var i = 0; i < 10; i++) events.Add(new ChartEvent
                        {
                            Kind = i % 2 == 0 ? ChartEventKind.Bpm : ChartEventKind.TrackSpeed,
                            TimeMs = i * 16000, Bpm = 150 + i, Division = 4, Multiplier = 1,
                        });
                        _view.SetEventsPanelOpen(true);
                        _view.ShowEvents(events, true);
                        break;
                    case 3:
                        Capture(index, "events");
                        _view.SetEventsPanelOpen(false);
                        _view.ShowPreferences(FalsumPreferences.CreateDefault(), null, null);
                        break;
                    case 4: Capture(index, "preferences-display"); _view.PreferencesView.DebugShowTab("Advanced"); break;
                    case 5: Capture(index, "preferences-advanced"); _view.PreferencesView.DebugShowTab("Editing"); break;
                    case 6: Capture(index, "preferences-editing"); _view.PreferencesView.DebugShowTab("Volume"); break;
                    case 7: Capture(index, "preferences-volume"); _view.PreferencesView.DebugShowTab("Keys"); break;
                    case 8: Capture(index, "preferences-keys"); _view.PreferencesView.DebugShowTab("Backup"); break;
                    case 9: Capture(index, "preferences-backup"); _view.PreferencesView.DebugShowTab("About"); break;
                    case 10:
                        Capture(index, "preferences-about");
                        _view.PreferencesView.CloseImmediate();
                        _view.ShowNewChartDialog("C:/charts/interface-audit.falsum");
                        break;
                    case 11:
                        Capture(index, "new-chart");
                        _view.CloseNewChartDialog();
                        _view.AudioPreprocessView.Show(129, 4, -60, 135670, "a la mode", "MIN");
                        break;
                    case 12:
                        Capture(index, "audio-preprocess");
                        _view.AudioPreprocessView.Close();
                        _view.ShowChartInfo(new FalsumChartMetadataDraft(_package, true), _package);
                        break;
                    case 13:
                        Capture(index, "chart-info");
                        _view.PreferencesView.DebugOpenChartInfoBackgroundPreview();
                        break;
                    case 14:
                        Capture(index, "background-picker");
                        _view.PreferencesView.DebugCancelChartInfoBackgroundPreview();
                        _view.CloseChartInfo();
                        _view.ShowToolManager();
                        break;
                    case 15:
                        Capture(index, "tools");
                        _view.CloseToolDialog();
                        var report = new FalsumToolReport
                        {
                            SourceToolName = L.Get("falsum.menu.tool"), Title = L.Get("falsum.editor.events"),
                            Summary = L.Get("falsum.common.confirm"),
                        };
                        report.Items.Add(new FalsumToolReportItem
                        {
                            Title = "SKYAREA", Message = L.Get("falsum.editor.noteProperties"),
                            TimeMs = 123456, Severity = "warning",
                        });
                        _view.ShowToolReport(report);
                        break;
                    case 16:
                        Capture(index, "tool-report");
                        _view.CloseToolReport();
                        _view.SetBankPanelOpen(true);
                        break;
                    case 17:
                        Capture(index, "bank");
                        _view.SetBankPanelOpen(false);
                        _view.ShowConfirm(L.Get("falsum.common.confirm"), delegate { });
                        break;
                    case 18:
                        Capture(index, "confirm");
                        Invoke("CancelConfirmDialog");
                        _view.ShowDisclaimer(false, null, null);
                        break;
                    case 19:
                        Capture(index, "disclaimer");
                        _view.DebugCloseDisclaimer();
                        _view.SetUiStyle(FalsumUiStyle.Classic);
                        break;
                    case 20:
                        Capture(index, "classic-fallback", FalsumUiStyle.Classic);
                        _view.SetUiStyle(FalsumUiStyle.InFalsus);
                        break;
                    case 21:
                        Capture(index, "restored-infalsus");
                        SessionState.SetInt(Viewport, index + 1);
                        SessionState.SetInt(Step, 0);
                        Advance();
                        return;
                }
                SessionState.SetInt(Step, step + 1);
                Advance();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void RenderWorld()
        {
            var camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null) return;
            var target = camera.targetTexture;
            try { camera.targetTexture = _target; camera.Render(); }
            finally { camera.targetTexture = target; }
            _document.rootVisualElement.MarkDirtyRepaint();
        }

        private static void Capture(int index, string name, FalsumUiStyle style = FalsumUiStyle.InFalsus)
        {
            var root = _document.rootVisualElement.Q<VisualElement>("FalsumRoot");
            if (Mathf.Abs(root.layout.width - Sizes[index].x) > 2 || Mathf.Abs(root.layout.height - Sizes[index].y) > 2)
                throw new InvalidOperationException("The requested viewport is not active: " + root.layout);
            if (root.ClassListContains("is-infalsus") != (style == FalsumUiStyle.InFalsus))
                throw new InvalidOperationException("The selected theme class is incorrect.");
            var old = RenderTexture.active;
            var image = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = _target;
                image.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
                image.Apply();
                // The camera clears this target. Read after a UITK paint,
                // never immediately after RenderWorld or only the scene is captured.
                var chromePixel = image.GetPixel(8, image.height - 8);
                if ((name == "shell" || name == "classic-fallback" || name == "restored-infalsus") &&
                    chromePixel.r + chromePixel.g + chromePixel.b < 0.03f)
                    throw new InvalidOperationException("UI chrome was not painted before capture: " + name);
                foreach (var label in root.Query<Label>(className: "preferences-row-title").ToList())
                {
                    if (label.resolvedStyle.display != DisplayStyle.None && label.worldBound.width < 10)
                        throw new InvalidOperationException("Setting title collapsed: " + label.text);
                }
                var directory = Path.Combine(Application.dataPath, "../Diagnostics/UiProfessional/Desktop");
                Directory.CreateDirectory(directory);
                var id = Sizes[index].x + "x" + Sizes[index].y + "-" + L.Locale + "-" + name;
                File.WriteAllBytes(Path.Combine(directory, id + ".png"), image.EncodeToPNG());
                Checks.Add(id);
            }
            finally { RenderTexture.active = old; UnityEngine.Object.Destroy(image); }
        }

        private static void Invoke(string name)
        {
            typeof(FalsumEditorView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_view, null);
        }

        private static int Threshold(string name)
        {
            return (int)typeof(FalsumEditorView).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
                .GetRawConstantValue();
        }

        private static void Advance() { SessionState.SetFloat(Time, (float)EditorApplication.timeSinceStartup); }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(Active, false);
            EditorApplication.update -= Tick;
            if (_document != null) _document.panelSettings.targetTexture = null;
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            var directory = Path.Combine(Application.dataPath, "../Diagnostics/UiProfessional");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "FalsumProfessionalUiCapture.json"),
                JsonUtility.ToJson(new Report { passed = error == null, checks = Checks.ToArray(), error = error?.ToString() ?? "" }, true));
            if (error != null) Debug.LogException(error);
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(error == null ? 0 : 1);
        }

        [Serializable]
        private sealed class Report { public bool passed; public string[] checks; public string error; }
    }
}
