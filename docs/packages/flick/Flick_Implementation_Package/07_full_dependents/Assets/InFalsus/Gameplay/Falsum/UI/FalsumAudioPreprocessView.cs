using System;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Parameters the window hands back when the user confirms. The audio
    /// rewrite and the chart migration live in the editor; this type only
    /// carries the three numbers the user can type.
    /// </summary>
    public sealed class FalsumAudioPreprocessRequest
    {
        public double Bpm;
        public double BeatsPerMeasure;
        public double DelayMs;
    }

    /// <summary>
    /// Modal window for the audio preprocess step.
    ///
    /// Data flow follows invariant C-4: this view never holds the editor. It
    /// raises three events (audition start, audition stop, apply) and receives
    /// state through Set* methods, exactly like the other dialog views.
    ///
    /// The view owns no audio. It cannot: the audition has to play the package
    /// audio through an editor-owned source so its clock and the playhead stay
    /// independent, so it only reports "the user wants to hear X ms onwards".
    /// </summary>
    public sealed class FalsumAudioPreprocessView
    {
        private readonly VisualElement _modalLayer;
        private readonly VisualTreeAsset _template;

        private VisualElement _overlay;
        private VisualElement _host;
        private VisualElement _panel;
        private VisualElement _busy;
        private Label _busyText;
        private Label _range;
        private Label _hint;
        private Label _status;
        private TextField _bpmInput;
        private TextField _beatsInput;
        private TextField _delayInput;
        private Button _auditionButton;
        private Button _applyButton;
        private Button _closeButton;
        private Button[] _zoomButtons;
        private FalsumAudioPreprocessWaveform _waveform;
        private bool _open;
        private bool _auditionPlaying;
        private bool _busyActive;
        private bool _styleSheetAttached;
        private double _bpm;
        private double _beatsPerMeasure;
        private double _delayMs;
        private double _audioDurationMs;

        public FalsumAudioPreprocessView(
            VisualElement modalLayer,
            VisualTreeAsset template)
        {
            _modalLayer = modalLayer;
            _template = template;
        }

        public event Action<double> AuditionRequested;
        public event Action AuditionStopRequested;
        public event Action<FalsumAudioPreprocessRequest> ApplyRequested;

        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>
        /// Whether the window managed to attach its own stylesheet. Exposed for
        /// the self check: without the sheet every class is a no-op and the
        /// panel renders as overlapping zero-size children.
        /// </summary>
        public bool DebugStyleSheetAttached
        {
            get { return _styleSheetAttached; }
        }

        /// <summary>
        /// Whether the reparented overlay itself carries the style sheet. The
        /// overlay is detached from the TemplateContainer that a
        /// &lt;ui:Style&gt; line would attach to, so this is the property that
        /// actually decides whether .audio-preprocess-* rules match.
        /// </summary>
        public bool DebugOverlayHasStyleSheet
        {
            get
            {
                var style = Resources.Load<StyleSheet>(
                    "Falsum/UI/FalsumAudioPreprocess");
                return _overlay != null &&
                       style != null &&
                       _overlay.styleSheets.Contains(style);
            }
        }

        /// <summary>
        /// Opens the window. <paramref name="delayMs"/> is expressed on the
        /// new timeline (positive = insert silence, negative = trim), so a
        /// caller that wants "no change to the audio" passes the value derived
        /// from the chart's stored delay.
        /// </summary>
        public void Show(
            double bpm,
            double beatsPerMeasure,
            double delayMs,
            double audioDurationMs,
            string songTitle,
            string difficultyLabel)
        {
            if (_template == null || _modalLayer == null)
            {
                return;
            }
            CloseImmediate();
            _bpm = bpm;
            _beatsPerMeasure = Math.Max(1.0, beatsPerMeasure);
            _delayMs = delayMs;
            _audioDurationMs = Math.Max(0.0, audioDurationMs);
            _auditionPlaying = false;
            _busyActive = false;

            var instance = _template.Instantiate();
            var style = Resources.Load<StyleSheet>(
                "Falsum/UI/FalsumAudioPreprocess");
            L.ApplyStaticText(instance);
            _overlay = instance.Q<VisualElement>("AudioPreprocessOverlay");
            _panel = instance.Q<VisualElement>("AudioPreprocessPanel");
            _busy = instance.Q<VisualElement>("AudioPreprocessBusy");
            _busyText = instance.Q<Label>("AudioPreprocessBusyText");
            _range = instance.Q<Label>("AudioPreprocessRange");
            _hint = instance.Q<Label>("AudioPreprocessHint");
            _status = instance.Q<Label>("AudioPreprocessStatus");
            L.BindTooltip(
                instance.Q<Button>("AudioPreprocessClose"),
                "falsum.audioPreprocess.close");
            _bpmInput = instance.Q<TextField>("AudioPreprocessBpm");
            _beatsInput = instance.Q<TextField>("AudioPreprocessBeats");
            _delayInput = instance.Q<TextField>("AudioPreprocessDelay");
            _auditionButton = instance.Q<Button>("AudioPreprocessAudition");
            _applyButton = instance.Q<Button>("AudioPreprocessApply");
            _closeButton = instance.Q<Button>("AudioPreprocessClose");
            _waveform = new FalsumAudioPreprocessWaveform();
            var waveformHost = instance.Q<VisualElement>(
                "AudioPreprocessWaveform");
            if (waveformHost != null)
            {
                // Keep the host element and put the drawing surface INSIDE it.
                // The previous version replaced the host in its parent, which
                // dropped the host's own flex sizing (and, when the host had
                // no parent, skipped the insert entirely so the waveform was
                // never added to the tree at all — an invisible plot with no
                // error).
                _waveform.style.flexGrow = 1;
                _waveform.style.flexShrink = 1;
                _waveform.AddToClassList("audio-preprocess-waveform-surface");
                waveformHost.Add(_waveform);
            }
            else
            {
                Debug.LogWarning(
                    "[Falsum] Audio preprocess: waveform host element is missing; " +
                    "the plot cannot be shown.");
            }

            _zoomButtons = new[]
            {
                instance.Q<Button>("AudioPreprocessZoom5"),
                instance.Q<Button>("AudioPreprocessZoom10"),
                instance.Q<Button>("AudioPreprocessZoom20"),
                instance.Q<Button>("AudioPreprocessZoom30"),
                instance.Q<Button>("AudioPreprocessZoom60"),
            };
            var zoomSeconds = new[] { 5.0, 10.0, 20.0, 30.0, 60.0 };
            for (var i = 0; i < _zoomButtons.Length; i++)
            {
                var button = _zoomButtons[i];
                if (button == null)
                {
                    continue;
                }
                // The zoom labels are numbers plus a unit, so they come from
                // one localized template rather than five hard-coded literals
                // (invariant C-1 has no exemption for "just numbers").
                L.Bind(
                    button,
                    "falsum.audioPreprocess.zoomSeconds");
                button.text = L.Get(
                    "falsum.audioPreprocess.zoomSeconds",
                    ("seconds", zoomSeconds[i].ToString("0")));
                var durationMs = zoomSeconds[i] * 1000.0;
                button.clicked += delegate
                {
                    if (_waveform == null)
                    {
                        return;
                    }
                    _waveform.SetZoom(durationMs);
                    RefreshRangeLabel();
                    UpdateZoomHighlight();
                };
            }

            if (_bpmInput != null)
            {
                _bpmInput.SetValueWithoutNotify(FormatNumber(_bpm));
                _bpmInput.RegisterValueChangedCallback(
                    delegate(ChangeEvent<string> evt)
                    {
                        OnParameterChanged();
                    });
            }
            if (_beatsInput != null)
            {
                _beatsInput.SetValueWithoutNotify(
                    FormatNumber(_beatsPerMeasure));
                _beatsInput.RegisterValueChangedCallback(
                    delegate(ChangeEvent<string> evt)
                    {
                        OnParameterChanged();
                    });
            }
            if (_delayInput != null)
            {
                _delayInput.SetValueWithoutNotify(FormatNumber(_delayMs));
                _delayInput.RegisterValueChangedCallback(
                    delegate(ChangeEvent<string> evt)
                    {
                        OnParameterChanged();
                    });
            }
            if (_auditionButton != null)
            {
                _auditionButton.clicked += OnAuditionClicked;
            }
            if (_applyButton != null)
            {
                L.Bind(_applyButton, "falsum.audioPreprocess.apply");
                _applyButton.clicked += OnApplyClicked;
            }
            if (_closeButton != null)
            {
                _closeButton.clicked += delegate { Close(); };
            }

            // The design sheet carries the "leave a full measure" guidance
            // under the parameters. That label is filled from the localization
            // key in the UXML (l10n.falsum.audioPreprocess.hint); the
            // AudioPreprocessHint label below it is for *dynamic* messages
            // (validation failures), so it starts empty instead of repeating
            // the same sentence twice.
            if (_status != null)
            {
                _status.text = string.Empty;
            }
            if (_hint != null)
            {
                _hint.text = string.Empty;
            }
            if (_busy != null)
            {
                _busy.AddToClassList("hidden");
            }
            // Attach the OVERLAY, not the TemplateContainer: a
            // TemplateContainer is not a laid-out box, so an overlay inside it
            // resolves "absolute inset 0" against a zero-size parent and every
            // child stacks at the same origin (transparent panel, title over
            // the fields, no plot). FalsumToolDialogView adds _overlay too.
            //
            // BECAUSE WE REPARENT, THE SHEET MUST TRAVEL WITH THE OVERLAY.
            // <ui:Style src="..."> attaches the sheet to the TemplateContainer,
            // so pulling _overlay out of it leaves the subtree with no ancestor
            // carrying the sheet and EVERY .audio-preprocess-* rule stops
            // matching — the window then renders with default UI Toolkit chrome
            // (row children stack vertically, no colours), which is exactly what
            // the last capture showed.
            //
            // Attach it in BOTH places: on the overlay (root of the reparented
            // subtree, always sufficient) and on the modal layer (guards the
            // case where the overlay is replaced later). Attaching the same
            // StyleSheet twice on two ancestors is harmless; UITK dedupes per
            // element, not per chain.
            if (style != null)
            {
                if (!_overlay.styleSheets.Contains(style))
                {
                    _overlay.styleSheets.Add(style);
                }
                if (!_modalLayer.styleSheets.Contains(style))
                {
                    _modalLayer.styleSheets.Add(style);
                }
                _styleSheetAttached = true;
            }
            else
            {
                Debug.LogError(
                    "[Falsum] Audio preprocess style sheet is missing: " +
                    "Falsum/UI/FalsumAudioPreprocess");
            }
            _modalLayer.Add(_overlay);
            _modalLayer.BringToFront();
            _host = instance;
            _open = true;
            _waveform.SetEnvelope(null, _audioDurationMs);
            // The default view is the first 30 seconds, the audition range the
            // spec fixes; the user can drag the view from there.
            _waveform.SetZoom(Math.Min(_audioDurationMs, 30000.0) > 0.0
                ? Math.Min(_audioDurationMs, 30000.0)
                : 30000.0);
            _waveform.CenterOn(15000.0);
            ApplyParametersToWaveform();
            RefreshRangeLabel();
            UpdateZoomHighlight();
            UpdateAuditionButton();
        }

        public void Close()
        {
            if (!_open)
            {
                CloseImmediate();
                return;
            }
            RaiseAuditionStop();
            _open = false;
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            _host = null;
            _panel = null;
            _waveform = null;
        }

        private void CloseImmediate()
        {
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
            }
            _overlay = null;
            _host = null;
            _panel = null;
            _waveform = null;
            _busy = null;
            _busyText = null;
            _range = null;
            _hint = null;
            _status = null;
            _bpmInput = null;
            _beatsInput = null;
            _delayInput = null;
            _auditionButton = null;
            _applyButton = null;
            _closeButton = null;
            _zoomButtons = null;
            _open = false;
        }

        public void SetStatus(string text)
        {
            if (_status == null)
            {
                return;
            }
            _status.text = text ?? string.Empty;
        }

        public void SetHint(string text)
        {
            if (_hint == null)
            {
                return;
            }
            _hint.text = text ?? string.Empty;
        }

        public void SetEnvelope(
            FalsumAudioPreprocessEnvelopeSample[] samples)
        {
            if (_waveform == null)
            {
                return;
            }
            _waveform.SetEnvelope(samples, _audioDurationMs);
        }

        public void SetProgress(double newTimeMs)
        {
            _waveform?.SetProgress(newTimeMs);
        }

        public void SetAuditionPlaying(bool playing)
        {
            if (_auditionPlaying == playing)
            {
                return;
            }
            _auditionPlaying = playing;
            if (!playing)
            {
                _waveform?.ClearProgress();
            }
            UpdateAuditionButton();
        }

        public void SetBusy(bool busy, string statusText)
        {
            _busyActive = busy;
            if (_busy != null)
            {
                if (busy)
                {
                    _busy.RemoveFromClassList("hidden");
                }
                else
                {
                    _busy.AddToClassList("hidden");
                }
            }
            if (_busyText != null)
            {
                _busyText.text = statusText ?? string.Empty;
            }
            if (!busy && !string.IsNullOrEmpty(statusText) && _status != null)
            {
                _status.text = statusText;
            }
            if (_applyButton != null)
            {
                _applyButton.SetEnabled(!busy);
            }
            if (_auditionButton != null)
            {
                _auditionButton.SetEnabled(!busy);
            }
        }

        public bool TryGetParameters(
            out double bpm,
            out double beatsPerMeasure,
            out double delayMs)
        {            bpm = _bpm;
            beatsPerMeasure = _beatsPerMeasure;
            delayMs = _delayMs;
            if (!ParseInputs(out var parsedBpm, out var parsedBeats, out var parsedDelay))
            {
                return false;
            }
            bpm = parsedBpm;
            beatsPerMeasure = parsedBeats;
            delayMs = parsedDelay;
            return true;
        }

        private bool ParseInputs(
            out double bpm,
            out double beatsPerMeasure,
            out double delayMs)
        {
            bpm = _bpm;
            beatsPerMeasure = _beatsPerMeasure;
            delayMs = _delayMs;
            if (!TryParseField(_bpmInput, out var parsedBpm) ||
                !TryParseField(_beatsInput, out var parsedBeats) ||
                !TryParseField(_delayInput, out var parsedDelay))
            {
                return false;
            }
            bpm = parsedBpm;
            beatsPerMeasure = parsedBeats;
            delayMs = parsedDelay;
            return true;
        }

        private static bool TryParseField(
            TextField field,
            out double value)
        {
            value = 0.0;
            if (field == null)
            {
                return false;
            }
            var text = FalsumEditorView.SanitizeNumericInput(
                field.value,
                false);
            if (string.IsNullOrEmpty(text) ||
                !double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value))
            {
                return false;
            }
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void OnParameterChanged()
        {
            if (!ParseInputs(
                    out var parsedBpm,
                    out var parsedBeats,
                    out var parsedDelay))
            {
                // A half-typed value must not repaint the waveform with garbage;
                // keep the last good parameters until the field is valid again.
                return;
            }
            _bpm = parsedBpm;
            _beatsPerMeasure = Math.Max(1.0, parsedBeats);
            _delayMs = parsedDelay;
            ApplyParametersToWaveform();
        }

        private void ApplyParametersToWaveform()
        {
            if (_waveform == null)
            {
                return;
            }
            _waveform.SetParams(
                _delayMs,
                _bpm,
                _beatsPerMeasure);
        }

        private void RefreshRangeLabel()
        {
            if (_range == null || _waveform == null)
            {
                return;
            }
            _range.text = L.Get(
                "falsum.audioPreprocess.range",
                ("start", FormatSeconds(_waveform.ViewStartMs)),
                ("end",
                 FormatSeconds(
                     _waveform.ViewStartMs +
                     _waveform.ViewDurationMs)));
        }

        private void UpdateZoomHighlight()
        {
            if (_zoomButtons == null || _waveform == null)
            {
                return;
            }
            var zoomSeconds = new[] { 5.0, 10.0, 20.0, 30.0, 60.0 };
            for (var i = 0; i < _zoomButtons.Length; i++)
            {
                var button = _zoomButtons[i];
                if (button == null)
                {
                    continue;
                }
                var active =
                    Math.Abs(
                        _waveform.ViewDurationMs -
                        zoomSeconds[i] * 1000.0) < 1.0;
                if (active)
                {
                    button.AddToClassList("is-active");
                }
                else
                {
                    button.RemoveFromClassList("is-active");
                }
            }
        }

        private void UpdateAuditionButton()
        {
            if (_auditionButton == null)
            {
                return;
            }
            L.Bind(
                _auditionButton,
                _auditionPlaying
                    ? "falsum.audioPreprocess.stop"
                    : "falsum.audioPreprocess.audition");
        }

        private void OnAuditionClicked()
        {
            if (_busyActive)
            {
                return;
            }
            if (_auditionPlaying)
            {
                RaiseAuditionStop();
                return;
            }
            if (!ParseInputs(
                    out var parsedBpm,
                    out var parsedBeats,
                    out var parsedDelay))
            {
                SetStatus(L.Get(
                    "falsum.audioPreprocess.error.bpmNotPositive"));
                return;
            }
            _bpm = parsedBpm;
            _beatsPerMeasure = Math.Max(1.0, parsedBeats);
            _delayMs = parsedDelay;
            var request = AuditionRequested;
            if (request == null)
            {
                return;
            }
            // 30 s is the default audition length; the window has no handle to
            // extend it, so the value is simply the visible span.
            var endMs = Math.Min(30000.0, _audioDurationMs);
            if (endMs <= 0.0)
            {
                SetStatus(L.Get(
                    "falsum.audioPreprocess.error.sourceAudioMissing"));
                return;
            }
            request(endMs);
        }

        private void RaiseAuditionStop()
        {
            if (!_auditionPlaying)
            {
                return;
            }
            _auditionPlaying = false;
            UpdateAuditionButton();
            _waveform?.ClearProgress();
            var stop = AuditionStopRequested;
            if (stop != null)
            {
                stop();
            }
        }

        private void OnApplyClicked()
        {
            if (_busyActive)
            {
                return;
            }
            if (!ParseInputs(
                    out var parsedBpm,
                    out var parsedBeats,
                    out var parsedDelay))
            {
                SetStatus(L.Get(
                    "falsum.audioPreprocess.error.bpmNotPositive"));
                return;
            }
            var apply = ApplyRequested;
            if (apply == null)
            {
                return;
            }
            apply(new FalsumAudioPreprocessRequest
            {
                Bpm = parsedBpm,
                BeatsPerMeasure = Math.Max(1.0, parsedBeats),
                DelayMs = parsedDelay,
            });
        }

        // ----------------------------------------------------------------
        // Diagnostics for the capture tool
        // ----------------------------------------------------------------

        /// <summary>
        /// Whether the waveform received a real layout rect and an envelope.
        /// A laid-out window reports a positive width; a window that never got
        /// a panel layout reports zero, which is the failure the capture tool
        /// is there to catch.
        /// </summary>
        public string DebugWaveformSummary
        {
            get
            {
                if (_waveform == null)
                {
                    return "waveform=<none>";
                }
                return "waveformWidth=" +
                       _waveform.resolvedStyle.width.ToString("0.0") +
                       " waveformHeight=" +
                       _waveform.resolvedStyle.height.ToString("0.0") +
                       " viewStartMs=" +
                       _waveform.ViewStartMs.ToString("0.0") +
                       " viewDurationMs=" +
                       _waveform.ViewDurationMs.ToString("0.0") +
                       " envelopeSamples=" + _waveform.EnvelopeSampleCount;
            }
        }

        /// <summary>
        /// Writes the delay field the way a user would, so the capture tool can
        /// photograph both the trim and the pad state.
        /// </summary>
        public bool DebugSetDelay(double delayMs)
        {
            if (!_open || _delayInput == null)
            {
                return false;
            }
            _delayInput.SetValueWithoutNotify(FormatNumber(delayMs));
            _delayMs = delayMs;
            ApplyParametersToWaveform();
            return true;
        }

        private static string FormatNumber(double value)        {
            if (Math.Abs(value - Math.Round(value)) < 0.0000001)
            {
                return Math.Round(value).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            return value.ToString(
                "0.###",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatSeconds(double milliseconds)
        {
            var seconds = milliseconds / 1000.0;
            return seconds.ToString(
                "0.0",
                System.Globalization.CultureInfo.InvariantCulture) + "s";
        }
    }
}
