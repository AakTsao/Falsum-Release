using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// The Skyarea Bank sidebar.
    ///
    /// <para>IT IS NOT A MODAL. The panel floats over the left of the chart,
    /// does not take part in <c>IsModalOpen</c>, and the camera viewport is
    /// never resized. That is what lets the user keep editing while the bank
    /// is open (place notes, scroll, right-click radial menu); clicks that
    /// land on the panel are stopped by the editor's ordinary
    /// <c>IsPointerOverUi</c> hit test because the panel uses
    /// <c>picking-mode="Position"</c>.</para>
    ///
    /// <para>View never reaches into the editor (invariant C-4): everything
    /// leaves through <c>public event</c>, and the editor pushes state back
    /// through the <c>Set*</c> methods below.</para>
    /// </summary>
    public sealed class FalsumSkyAreaBankView
    {
        /// <summary>
        /// Product switch, mirroring the hidden-feature pattern used for the
        /// UI style switcher: flipping this to false hides the sidebar and
        /// disables Ctrl+B without touching any of the data layer.
        /// </summary>
        public static bool Available = true;

        private const string TemplatePath = "Falsum/UI/FalsumSkyAreaBank";

        /// <summary>Sidebar slide duration, mirroring the events panel.</summary>
        private const float PanelAnimationSeconds = 0.22f;

        /// <summary>
        /// Used until the panel has been laid out at least once: the USS width
        /// is 360px, and the very first open may run before the first layout
        /// pass has resolved <c>resolvedStyle.width</c>.
        /// </summary>
        private const float PanelFallbackWidth = 360.0f;

        private const int ThumbnailStepsPerSegment = 20;
        private const float ThumbnailFallbackWidth = 96.0f;
        private const float ThumbnailFallbackHeight = 54.0f;

        private static readonly Color ThumbnailBackgroundColor =
            new Color(0.0f, 0.0f, 0.0f, 0.55f);
        private static readonly Color ThumbnailFillColor =
            new Color(0.42f, 0.78f, 1.0f, 0.55f);
        private static readonly Color ThumbnailStrokeColor =
            new Color(0.62f, 0.88f, 1.0f, 0.95f);

        /// <summary>
        /// Reused by every repaint so the card list does not churn through a
        /// fresh list per frame; <see cref="BuildThumbnailQuads"/> clears it.
        /// </summary>
        private readonly List<Vector2[]> _thumbnailQuads =
            new List<Vector2[]>();

        private readonly VisualElement _layer;
        private readonly VisualElement _panel;
        private readonly ScrollView _tagRow;
        private readonly ScrollView _list;
        private readonly Label _empty;
        private readonly Label _summary;
        private readonly Label _status;
        private readonly TextField _search;
        private readonly TextField _shareCode;
        private readonly VisualElement _shareLayer;
        private readonly VisualElement _sharePanel;

        // Import dialog: a text field the author can inspect and fix before
        // committing, plus a "pick a .fsky file" path that fills the same field.
        private readonly VisualElement _importLayer;
        private readonly TextField _importCode;

        private bool _importLayerOpen;
        private readonly List<Tuple<string, Button>> _tagButtons =
            new List<Tuple<string, Button>>();

        private FalsumSkyAreaBankDocument _document;
        private readonly HashSet<string> _activeTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FalsumSkyAreaPreset _sharePreset;
        private TextField _renaming;
        private bool _open;

        // Sidebar slide state. Offsets are negative: the panel enters from the
        // left edge it is anchored to, so the closed position is -width.
        private IVisualElementScheduledItem _panelAnimation;
        private float _panelOffset;
        private float _panelAlpha = 1.0f;
        private float _panelAnimationFrom;
        private float _panelAnimationTo;
        private float _panelAlphaFrom;
        private float _panelAlphaTo;
        private float _panelAnimationStart;
        private bool _panelAnimatingOpen;

        // Share layer exit guard: a token is bumped by every show/hide so a
        // completion callback from a superseded animation cannot tear down a
        // layer that has been re-opened in the meantime.
        private bool _shareLayerOpen;
        private bool _shareLayerClosing;
        private int _shareLayerToken;

        public event Action<bool> PanelVisibilityChanged;
        public event Action<string, IList<string>> SaveSelectionRequested;
        public event Action<FalsumSkyAreaPreset> PlacePresetRequested;
        public event Action<FalsumSkyAreaPreset> DeletePresetRequested;
        public event Action<FalsumSkyAreaPreset, string> RenamePresetRequested;
        public event Action<FalsumSkyAreaPreset, double, double>
            PresetScalesRequested;
        public event Action<FalsumSkyAreaPreset> CopyCodeRequested;
        public event Action<FalsumSkyAreaPreset> ExportFileRequested;
        public event Action<string> ImportCodeRequested;

        /// <summary>
        /// Raised when the import dialog's file button is pressed. The editor
        /// opens a file picker, reads the <c>.fsky</c> text and answers through
        /// <see cref="SetImportCode"/>, so the dialog shows what will be
        /// imported before the author confirms it.
        /// </summary>
        public event Action ImportFileTextRequested;

        /// <summary>
        /// Fills the import dialog's field (used by the file picker path).
        /// </summary>
        public void SetImportCode(string code)
        {
            if (_importCode == null)
            {
                return;
            }
            _importCode.SetValueWithoutNotify((code ?? string.Empty).Trim());
            _importCode.Focus();
        }

        /// <summary>
        /// Raised by the footer's folder button.
        ///
        /// <para>NAME IS HISTORICAL. It used to mean "import a .fsky share-code
        /// file"; it now means "show the Skyarea Bank folder in the OS file
        /// manager", which <c>FalsumChartEditor.OpenBankFolder</c> performs.
        /// The name is kept because the hop from this event to the editor runs
        /// through <c>FalsumEditorView.BankImportFileRequested</c>, which is
        /// outside this change's file scope; renaming one end would break the
        /// other two.</para>
        /// </summary>
        public event Action ImportFileRequested;

        public bool IsOpen
        {
            get { return _open && Available; }
        }

        /// <summary>
        /// True when a PANEL-SPACE point falls inside the bank sidebar.
        ///
        /// <para>Exists for <c>FalsumEditorView.ShouldBlockChartScroll</c>.
        /// That gate is what actually keeps the wheel off the chart, and it
        /// currently knows about the events panel and the floating tool report
        /// only. <c>OnBankListWheel</c> below stops the UITK event, but the
        /// chart's wheel comes from the legacy <c>Input.mouseScrollDelta</c>
        /// path, which a UITK event cannot cancel -- the gate is the only
        /// lever. Add the bank to it with:</para>
        ///
        /// <code>
        /// if (IsSkyAreaBankOpen &amp;&amp;
        ///     _skyAreaBankView != null &amp;&amp;
        ///     _skyAreaBankView.ContainsPanelPoint(
        ///         ScreenToPanelTopLeft(screenPosition)))
        /// {
        ///     return true;
        /// }
        /// </code>
        ///
        /// <para>Kept on the view (invariant C-4 still holds: this answers a
        /// geometry question about its own element and changes nothing).</para>
        /// </summary>
        public bool ContainsPanelPoint(Vector2 panelPoint)
        {
            if (!IsOpen || _panel == null)
            {
                return false;
            }
            return _panel.worldBound.Contains(panelPoint);
        }

        public FalsumSkyAreaBankView(
            VisualElement layer,
            VisualTreeAsset template)
        {
            if (layer == null || template == null)
            {
                throw new ArgumentNullException(
                    layer == null ? "layer" : "template");
            }
            _layer = layer;

            // The UXML imports FalsumSkyAreaBank.uss itself (see the template);
            // attaching a StyleSheet to the layer from code did not cascade
            // into the instantiated subtree, which left the panel unstyled.
            var instance = template.Instantiate();
            _panel = Require<VisualElement>(instance, "BankPanel");
            _tagRow = Require<ScrollView>(instance, "BankTagRow");
            _list = Require<ScrollView>(instance, "BankList");
            _empty = Require<Label>(instance, "BankEmpty");
            _summary = Require<Label>(instance, "BankSelectionSummary");
            _status = Require<Label>(instance, "BankStatus");
            _search = Require<TextField>(instance, "BankSearch");
            _shareLayer = Require<VisualElement>(instance, "BankShareLayer");
            _sharePanel = Require<VisualElement>(instance, "BankSharePanel");
            _shareCode = Require<TextField>(instance, "BankShareCode");
            _importLayer = Require<VisualElement>(instance, "BankImportLayer");
            _importCode = Require<TextField>(instance, "BankImportCode");

            // A ScrollView instantiated from UXML without an explicit mode
            // defaults to Vertical, which stacks the chips in a 26px-tall row
            // and clips all but the first one.
            _tagRow.mode = ScrollViewMode.Horizontal;

            _shareCode.SetValueWithoutNotify(string.Empty);
            _shareCode.isReadOnly = true;
            L.ApplyStaticText(instance);

            // BetterButton text: every button in this panel is dynamic, so
            // without these explicit L.Get calls the panel renders as blank
            // grey slabs (there is no l10n.* element name that can carry it).
            Require<Button>(instance, "BankSaveSelection").text =
                L.Get("falsum.bank.saveSelection");
            Require<Button>(instance, "BankImportFromClipboard").text =
                L.Get("falsum.bank.importFromClipboard");
            // The folder button keeps the falsum.bank.importFromFile key: it is
            // the same control in the same place, and the key is retitled on
            // the localization side ("打开预设库文件夹" / "Open preset folder").
            // Renaming the key here would strand the Lead's translation work.
            Require<Button>(instance, "BankImportFromFile").text =
                L.Get("falsum.bank.importFromFile");
            Require<Button>(instance, "BankShareCopy").text =
                L.Get("falsum.bank.copyCode");
            Require<Button>(instance, "BankShareExport").text =
                L.Get("falsum.bank.exportFile");
            Require<Button>(instance, "BankShareClose").text =
                L.Get("falsum.common.close");
            Require<Button>(instance, "BankImportConfirm").text =
                L.Get("falsum.bank.importConfirm");
            Require<Button>(instance, "BankImportPickFile").text =
                L.Get("falsum.bank.importPickFile");
            Require<Button>(instance, "BankImportCancel").text =
                L.Get("falsum.bank.importCancel");
            // TextField has no SetPlaceholderText; the placeholder lives on the
            // text edition object. TryGet tells us whether the key resolved,
            // so no prefix-prefix comparison is needed (and a bare
            // "falsum.bank." string literal would look like a key reference to
            // the localization audit).
            string searchKey;
            if (L.TryGet("falsum.bank.searchPlaceholder", out searchKey) &&
                !string.IsNullOrEmpty(searchKey))
            {
                _search.textEdition.placeholder = searchKey;
            }

            // The glyph comes from the UXML (the project-wide close recipe).
            // Do NOT blank .text here -- that used to erase it.
            var closeButton = Require<Button>(instance, "BankClose");
            closeButton.tooltip = L.Get("falsum.common.close");
            closeButton.clicked += delegate { SetOpen(false, true); };
            var saveButton = Require<Button>(instance, "BankSaveSelection");
            saveButton.clicked += delegate
            {
                var request = SaveSelectionRequested;
                if (request != null)
                {
                    request(
                        _search.value ?? string.Empty,
                        new List<string>(_activeTags));
                }
            };
            var importClipboard =
                Require<Button>(instance, "BankImportFromClipboard");
            importClipboard.clicked += delegate
            {
                // Pasting used to read the clipboard and import straight away,
                // which gave the author no chance to see or correct what was
                // about to be imported (and no way to paste a code the
                // clipboard refused to expose). Ask first: the dialog owns the
                // text field, and confirming is what raises the import.
                OpenImportLayer();
            };
            // Same footer slot, new meaning: the view only RAISES the intent
            // (invariant C-4, the view never touches the filesystem). The
            // editor owns FalsumSkyAreaBankStore.DirectoryPath and does the
            // actual Process.Start.
            var openFolder = Require<Button>(instance, "BankImportFromFile");
            openFolder.clicked += delegate
            {
                var request = ImportFileRequested;
                if (request != null)
                {
                    request();
                }
            };
            // Import dialog: confirm raises the code import with whatever the
            // field holds; the pick-file button fills the same field from a
            // .fsky file so both entry points converge on one confirm action.
            Require<Button>(instance, "BankImportConfirm").clicked += delegate
            {
                var code = (_importCode.value ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(code))
                {
                    ShowStatus(L.Get("falsum.bank.error.emptyCode"));
                    return;
                }
                CloseImportLayer();
                var request = ImportCodeRequested;
                if (request != null)
                {
                    request(code);
                }
            };
            Require<Button>(instance, "BankImportPickFile").clicked += delegate
            {
                // The view never touches the filesystem (invariant C-4): it
                // asks the editor for the text of a chosen .fsky file.
                var request = ImportFileTextRequested;
                if (request != null)
                {
                    request();
                }
            };
            Require<Button>(instance, "BankImportCancel").clicked +=
                delegate { CloseImportLayer(); };
            Require<Button>(instance, "BankShareCopy").clicked += delegate
            {
                if (_sharePreset != null)
                {
                    var request = CopyCodeRequested;
                    if (request != null)
                    {
                        request(_sharePreset);
                    }
                }
            };
            Require<Button>(instance, "BankShareExport").clicked += delegate
            {
                if (_sharePreset != null)
                {
                    var request = ExportFileRequested;
                    if (request != null)
                    {
                        request(_sharePreset);
                    }
                }
            };
            Require<Button>(instance, "BankShareClose").clicked +=
                delegate { HideShareLayer(); };

            _search.RegisterValueChangedCallback(
                delegate { RefreshList(); });
            _panel.RegisterCallback<KeyDownEvent>(
                OnPanelKeyDown,
                TrickleDown.TrickleDown);

            // THE BANK LIST IS A WHEEL BOUNDARY.
            //
            // The chart reads its wheel from the legacy input path
            // (FalsumChartEditor.HandleKeyboard -> Input.mouseScrollDelta), not
            // from UI Toolkit, so this callback cannot stop the chart on its
            // own -- see ContainsPanelPoint for the other half of the fix. What
            // this DOES guarantee is that the wheel never leaves the list as a
            // UITK event: it is consumed at the list boundary instead of
            // bubbling into the panel and from there into the editor overlay.
            //
            // BUBBLE phase, deliberately, NOT TrickleDown: the ScrollView's own
            // wheel handler is registered on this same element by its
            // constructor, so a bubble handler here runs after it (same
            // element, same phase, registration order) and the list still
            // scrolls normally. A TrickleDown handler would swallow the event
            // before it ever reached the ScrollView and freeze the list.
            // Stopping propagation here is what keeps the event from leaving
            // the list for any ancestor.
            //
            // NO SIGN FLIP HERE. UITK's WheelEvent.delta.y is positive when
            // the wheel is rolled DOWN, the opposite of Input.mouseScrollDelta,
            // and the project flips at the boundary exactly once -- but only
            // when a UITK wheel is FORWARDED into the legacy timeline API (see
            // FalsumToolDialogView.NormalizeWheelDelta). Nothing is forwarded
            // here; this is a consume, so flipping would be a second negation
            // and would invert the list's scroll direction.
            _list.RegisterCallback<WheelEvent>(OnBankListWheel);
            _layer.Add(_panel);
            SetOpen(false, false);
        }

        /// <summary>
        /// Consumes the wheel at the bank list's boundary so the event cannot
        /// travel on to any ancestor element.
        ///
        /// <para>PROPAGATION IS ONLY HALF THE JOB, and the other half is not in
        /// this file: the chart's wheel comes from the legacy
        /// <c>Input.mouseScrollDelta</c> path in <c>FalsumChartEditor</c>, which
        /// no UI Toolkit event can cancel. Stopping the event here keeps the
        /// list's own scroll authoritative inside UITK; keeping the CHART still
        /// requires the bank in <c>FalsumEditorView.ShouldBlockChartScroll</c>
        /// (see <see cref="ContainsPanelPoint"/>).</para>
        ///
        /// <para>The sign of <c>evt.delta.y</c> is intentionally left alone: it
        /// is only flipped when a UITK wheel is forwarded into the legacy
        /// timeline API, and nothing is forwarded here.</para>
        /// </summary>
        private void OnBankListWheel(WheelEvent evt)
        {
            if (evt == null)
            {
                return;
            }
            evt.StopPropagation();
        }

        private void OnPanelKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                // Only the sidebar's own Esc is consumed here; the editor's
                // global Esc (cancel paste preview / leave a placement mode)
                // must keep working everywhere else.
                //
                // The test is the share layer's LOGICAL state, not its
                // classList: during a 140ms fade-out the layer is still
                // un-hidden, but the share layer is already logically closed,
                // so Esc has to fall through and close the sidebar instead of
                // being swallowed by a close that is already running.
                if (_shareLayerOpen)
                {
                    HideShareLayer();
                }
                else
                {
                    SetOpen(false, true);
                }
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        public void SetOpen(bool open, bool notify)
        {
            var visible = open && Available;
            _open = visible;
            if (!visible)
            {
                HideShareLayer();
            }
            // "Fully open / fully closed" early-out, same shape as
            // FalsumEditorView.SetEventsPanelOpen. This is also what keeps the
            // constructor's initial SetOpen(false, false) from animating: the
            // UXML already ships the panel hidden at offset 0.
            var fullyOpen = visible &&
                            !_panel.ClassListContains("hidden") &&
                            _panelAnimation == null &&
                            Math.Abs(_panelOffset) < 0.5f &&
                            Math.Abs(_panelAlpha - 1.0f) < 0.001f;
            var fullyClosed = !visible &&
                              _panel.ClassListContains("hidden") &&
                              _panelAnimation == null;
            if (fullyOpen || fullyClosed)
            {
                // Normalise the resting pose explicitly instead of trusting the
                // UXML/USS defaults: the FIRST real open must never inherit a
                // stale translate or opacity from an interrupted slide, and the
                // constructor's SetOpen(false, false) ends here.
                if (fullyClosed)
                {
                    SetVisible(_panel, false);
                    ApplyPanelOffset(0.0f);
                    ApplyPanelAlpha(1.0f);
                }
                // Nothing to animate, but a caller-driven notify still has to
                // fire (IsOpen/PanelVisibilityChanged semantics are unchanged).
                if (notify)
                {
                    var early = PanelVisibilityChanged;
                    if (early != null)
                    {
                        early(visible);
                    }
                }
                return;
            }

            AnimatePanel(visible);
            if (notify)
            {
                var handler = PanelVisibilityChanged;
                if (handler != null)
                {
                    handler(visible);
                }
            }
        }

        /// <summary>
        /// Starts (or reverses) the sidebar slide. The panel is anchored to the
        /// left edge of the overlay, so "closed" is a translate of -width:
        /// it slides out of view towards the left while fading. Mirrors the
        /// events panel's translate + SmoothStep recipe in FalsumEditorView.
        /// </summary>
        private void AnimatePanel(bool open)
        {
            var wasHidden = _panel.ClassListContains("hidden");
            // Re-toggling mid-flight cancels the in-flight animation and
            // restarts from wherever the panel is right now.
            CancelPanelAnimation();
            // Visible BEFORE the first frame, otherwise the slide would be
            // invisible for its whole duration.
            SetVisible(_panel, true);

            var width = ResolvePanelWidth();
            _panelAnimationFrom = open && wasHidden ? -width : _panelOffset;
            _panelAnimationTo = open ? 0.0f : -width;
            _panelAlphaFrom = open && wasHidden ? 0.0f : _panelAlpha;
            _panelAlphaTo = open ? 1.0f : 0.0f;
            // Apply the starting pose SYNCHRONOUSLY. The first scheduled tick
            // is ~16ms away, and until it runs the freshly un-hidden panel would
            // sit at translate 0 / opacity 1 -- a one-frame flash of the fully
            // open sidebar at the start of an open, and of a fully opaque panel
            // at the start of a close.
            ApplyPanelOffset(_panelAnimationFrom);
            ApplyPanelAlpha(_panelAlphaFrom);
            _panelAnimationStart = (float)Time.unscaledTimeAsDouble;
            _panelAnimatingOpen = open;
            _panelAnimation = _panel.schedule
                .Execute((Action)TickPanelAnimation)
                .Every(16);
        }

        private float ResolvePanelWidth()
        {
            var width = _panel.resolvedStyle.width;
            if (float.IsNaN(width) || width < 1.0f)
            {
                width = _panel.layout.width;
            }
            if (float.IsNaN(width) || width < 1.0f)
            {
                width = PanelFallbackWidth;
            }
            return width;
        }

        private void TickPanelAnimation()
        {
            var elapsed =
                (float)Time.unscaledTimeAsDouble - _panelAnimationStart;
            var progress = Mathf.Clamp01(elapsed / PanelAnimationSeconds);
            var eased = Mathf.SmoothStep(0.0f, 1.0f, progress);
            ApplyPanelOffset(Mathf.LerpUnclamped(
                _panelAnimationFrom,
                _panelAnimationTo,
                eased));
            ApplyPanelAlpha(Mathf.LerpUnclamped(
                _panelAlphaFrom,
                _panelAlphaTo,
                eased));
            if (progress < 1.0f)
            {
                return;
            }
            CancelPanelAnimation();
            if (_panelAnimatingOpen)
            {
                // Snap to the exact final pose: the lerp alone can stop a
                // fraction of a pixel short.
                ApplyPanelOffset(0.0f);
                ApplyPanelAlpha(1.0f);
            }
            else
            {
                SetVisible(_panel, false);
                ApplyPanelAlpha(1.0f);
            }
        }

        private void CancelPanelAnimation()
        {
            if (_panelAnimation == null)
            {
                return;
            }
            _panelAnimation.Pause();
            _panelAnimation = null;
        }

        private void ApplyPanelOffset(float offset)
        {
            _panelOffset = offset;
            if (_panel != null)
            {
                _panel.style.translate =
                    new StyleTranslate(new Translate(offset, 0.0f));
            }
        }

        private void ApplyPanelAlpha(float alpha)
        {
            _panelAlpha = Mathf.Clamp01(alpha);
            if (_panel != null)
            {
                _panel.style.opacity = _panelAlpha;
            }
        }

        public void SetPresets(FalsumSkyAreaBankDocument document)
        {
            _document = document;
            RefreshTagRow();
            RefreshList();
        }

        public void SetSelectionSummary(int skyAreaCount, int totalCount)
        {
            _summary.text = L.Get(
                "falsum.bank.selectionSummary",
                ("skyArea", skyAreaCount),
                ("total", totalCount));
        }

        public void ShowStatus(string text)
        {
            _status.text = text ?? string.Empty;
        }

        /// <summary>Shows the read-only code layer for one preset.</summary>
        public void ShowShareCode(
            FalsumSkyAreaPreset preset,
            string code)
        {
            _sharePreset = preset;
            _shareCode.SetValueWithoutNotify(code ?? string.Empty);
            // A second call while the layer is already up (e.g. the editor
            // answering the encode request) must only refresh the text, not
            // restart the intro fade from opacity 0 and leave a second scheduled
            // animation running against the same window.
            var wasVisible = _shareLayerOpen && !_shareLayerClosing;
            // Bump the token first: a still-running fade-out from an earlier
            // close must not be able to hide this newly-opened layer.
            _shareLayerToken++;
            _shareLayerOpen = true;
            _shareLayerClosing = false;
            if (wasVisible)
            {
                return;
            }
            SetVisible(_shareLayer, true);
            _shareLayer.BringToFront();
            FalsumUiMotion.AnimateModalIn(_shareLayer, _sharePanel);
        }

        /// <summary>
        /// Fades the share layer out and only then hides it. Idempotent: a
        /// second call while a close is already in flight does nothing, and a
        /// close during the open animation is rejected by the token guard in
        /// the completion callback.
        /// </summary>
        public void HideShareLayer()
        {
            _sharePreset = null;
            if (_shareLayerClosing)
            {
                // A close is already in flight; let it finish. Hiding here as
                // well would cut the fade short.
                return;
            }
            if (!_shareLayerOpen)
            {
                // Never shown (the constructor reaches here via
                // SetOpen(false, false)); make sure the layer really is hidden
                // so teardown stays idempotent.
                SetVisible(_shareLayer, false);
                return;
            }
            _shareLayerClosing = true;
            _shareLayerOpen = false;
            var token = ++_shareLayerToken;
            FalsumUiMotion.AnimateModalOut(
                _shareLayer,
                _sharePanel,
                delegate
                {
                    // The layer was re-opened (or re-closed) while this
                    // fade-out ran: leave the newer animation alone and do not
                    // hide a layer that is visible again.
                    if (token != _shareLayerToken || _shareLayerOpen)
                    {
                        return;
                    }
                    _shareLayerClosing = false;
                    SetVisible(_shareLayer, false);
                });
        }

        private void RefreshTagRow()
        {
            _tagRow.Clear();
            _tagButtons.Clear();
            if (_document == null)
            {
                return;
            }
            var seen = new List<string>();
            for (var i = 0; i < _document.Presets.Count; i++)
            {
                var preset = _document.Presets[i];
                for (var t = 0; t < preset.Tags.Count; t++)
                {
                    var tag = preset.Tags[t];
                    var duplicate = false;
                    for (var s = 0; s < seen.Count; s++)
                    {
                        if (string.Equals(
                                seen[s],
                                tag,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        seen.Add(tag);
                    }
                }
            }
            seen.Sort(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < seen.Count; i++)
            {
                var tag = seen[i];
                var button = new Button();
                button.text = tag;
                button.AddToClassList("bank-tag");
                button.clicked += delegate { ToggleTag(tag); };
                _tagRow.Add(button);
                _tagButtons.Add(Tuple.Create(tag, button));
            }
            RefreshTagButtonState();
        }

        private void ToggleTag(string tag)
        {
            if (!_activeTags.Remove(tag))
            {
                _activeTags.Add(tag);
            }
            RefreshTagButtonState();
            RefreshList();
        }

        private void RefreshTagButtonState()
        {
            for (var i = 0; i < _tagButtons.Count; i++)
            {
                var pair = _tagButtons[i];
                var active = _activeTags.Contains(pair.Item1);
                if (active)
                {
                    pair.Item2.AddToClassList("is-active");
                }
                else
                {
                    pair.Item2.RemoveFromClassList("is-active");
                }
            }
        }

        private void RefreshList()
        {
            _list.Clear();
            if (_document == null)
            {
                SetVisible(_empty, true);
                return;
            }
            var query = (_search.value ?? string.Empty).Trim();
            var visibleCount = 0;
            var ordered = new List<FalsumSkyAreaPreset>(
                _document.Presets);
            ordered.Sort(CompareForDisplay);
            for (var i = 0; i < ordered.Count; i++)
            {
                var preset = ordered[i];
                if (!MatchesQuery(preset, query) ||
                    !MatchesTags(preset))
                {
                    continue;
                }
                _list.Add(BuildCard(preset));
                visibleCount++;
            }
            SetVisible(_empty, visibleCount == 0);
            // An empty ScrollView still claims its flex space and pushes the
            // empty-state copy to the very bottom of a black void. Collapse it
            // instead so the message sits right under the toolbar.
            SetVisible(_list, visibleCount > 0);
            if (visibleCount == 0)
            {
                _empty.text = ordered.Count == 0
                    ? L.Get("falsum.bank.empty")
                    : L.Get("falsum.bank.emptyFiltered");
            }
        }

        private static int CompareForDisplay(
            FalsumSkyAreaPreset left,
            FalsumSkyAreaPreset right)
        {
            if (left.LastUsedAtUtc != right.LastUsedAtUtc)
            {
                return right.LastUsedAtUtc.CompareTo(left.LastUsedAtUtc);
            }
            if (left.UpdatedAtUtc != right.UpdatedAtUtc)
            {
                return right.UpdatedAtUtc.CompareTo(left.UpdatedAtUtc);
            }
            return right.Name.CompareTo(left.Name);
        }

        private bool MatchesQuery(
            FalsumSkyAreaPreset preset,
            string query)
        {
            if (query.Length == 0)
            {
                return true;
            }
            if (preset.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            for (var i = 0; i < preset.Tags.Count; i++)
            {
                if (preset.Tags[i].IndexOf(
                        query,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private bool MatchesTags(FalsumSkyAreaPreset preset)
        {
            if (_activeTags.Count == 0)
            {
                return true;
            }
            var matched = 0;
            for (var i = 0; i < preset.Tags.Count; i++)
            {
                if (_activeTags.Contains(preset.Tags[i]))
                {
                    matched++;
                }
            }
            return matched > 0;
        }

        private VisualElement BuildCard(FalsumSkyAreaPreset preset)
        {
            var card = new VisualElement();
            card.AddToClassList("bank-card");

            var thumb = new VisualElement();
            thumb.AddToClassList("bank-card-thumb");
            var captured = preset;
            // Painter2D thumbnail: the group outline normalised into the card
            // box. This reads only the preset's own numbers -- no 3D world, no
            // mesh, no camera, so it stays cheap and testable.
            thumb.generateVisualContent +=
                delegate(MeshGenerationContext context)
                {
                    DrawThumbnail(context, thumb, captured);
                };
            card.Add(thumb);

            var body = new VisualElement();
            body.AddToClassList("bank-card-body");
            // The name is a BUTTON, not a Label: a single click (not only a
            // double click) puts it into rename mode, which is what the user
            // asked for -- "双击改名" is a discoverability trap on a card that
            // is otherwise full of click targets. It keeps the Label look.
            var name = new Button();
            name.name = "BankCardName";
            name.AddToClassList("bank-card-name");
            name.text = string.IsNullOrEmpty(preset.Name)
                ? L.Get("falsum.bank.unnamed")
                : preset.Name;
            name.tooltip = L.Get("falsum.bank.renameHint");
            body.Add(name);

            var meta = new Label();
            meta.AddToClassList("bank-card-meta");
            // Width as cells / denominator, the same fraction the property
            // panel shows. The old raw percentage produced non-terminating
            // decimals for the 1/3 and 1/6 widths this editor snaps to
            // ("16.6666666666…").
            meta.text = L.Get(
                "falsum.bank.cardMeta",
                ("segments", preset.Segments.Count),
                ("duration", (long)Math.Round(preset.TotalDurationMs)),
                ("width", L.Get(
                    "falsum.property.fraction",
                    ("value", preset.SpanCells.ToString(
                        "0",
                        System.Globalization.CultureInfo.InvariantCulture)),
                    ("cells", preset.SpanDenominator.ToString(
                        "0",
                        System.Globalization.CultureInfo.InvariantCulture)))),
                ("w", preset.WidthScale),
                ("l", preset.LengthScale));
            body.Add(meta);

            // Per-card scale factors. They are what "place" actually applies,
            // so they have to be editable here rather than only remembered.
            var scales = new VisualElement();
            scales.AddToClassList("bank-card-scales");
            AddScaleField(
                scales,
                L.Get("falsum.bank.scaleWidthShort"),
                captured,
                true);
            AddScaleField(
                scales,
                L.Get("falsum.bank.scaleLengthShort"),
                captured,
                false);
            body.Add(scales);

            var actions = new VisualElement();
            actions.AddToClassList("bank-card-actions");
            // Secondary actions on the left, the primary action last so it
            // sits at the card's right edge (the row is justify-content:
            // flex-end and 放置 gets extra width via .bank-place-button).
            AddCardButton(
                actions,
                L.Get("falsum.bank.share"),
                delegate { OpenShareLayer(captured); });
            AddCardButton(
                actions,
                L.Get("falsum.bank.delete"),
                delegate
                {
                    var request = DeletePresetRequested;
                    if (request != null)
                    {
                        request(captured);
                    }
                },
                true);
            AddCardButton(
                actions,
                L.Get("falsum.bank.place"),
                delegate
                {
                    var request = PlacePresetRequested;
                    if (request != null)
                    {
                        request(captured);
                    }
                },
                false,
                "bank-place-button");
            body.Add(actions);
            card.Add(body);

            // A single click on the name swaps it for an edit field. It used
            // to require a double click, which nobody discovers on a card that
            // already has three buttons; the tooltip states the affordance.
            name.clicked += delegate
            {
                BeginRename(card, captured);
            };
            return card;
        }

        /// <summary>Swaps the card's name label for an edit field.</summary>
        private void BeginRename(
            VisualElement card,
            FalsumSkyAreaPreset preset)
        {
            if (_renaming != null)
            {
                CancelRename();
            }
            // The name is a Button now (single-click rename), so it must be
            // queried as a Button. Q<Label> returned null and the rename
            // silently did nothing.
            var existing = card.Q<Button>("BankCardName");
            if (existing == null)
            {
                return;
            }
            var field = new TextField();
            field.name = "BankCardRename";
            field.AddToClassList("falsum-field");
            field.AddToClassList("bank-card-rename");
            field.maxLength = FalsumSkyAreaBankModel.MaxNameLength;
            field.SetValueWithoutNotify(preset.Name ?? string.Empty);
            _renaming = field;
            var parent = existing.parent;
            if (parent == null)
            {
                return;
            }
            var index = parent.IndexOf(existing);
            existing.AddToClassList("hidden");
            parent.Insert(index + 1, field);
            field.Focus();
            field.SelectAll();
            field.RegisterCallback<KeyDownEvent>(
                delegate(KeyDownEvent evt)
                {
                    if (evt.keyCode == KeyCode.Return ||
                        evt.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitRename(preset, field.value ?? string.Empty);
                        evt.StopPropagation();
                    }
                    else if (evt.keyCode == KeyCode.Escape)
                    {
                        CancelRename();
                        evt.StopPropagation();
                    }
                },
                TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(
                delegate { CancelRename(); });
        }

        private void CommitRename(
            FalsumSkyAreaPreset preset,
            string name)
        {
            var trimmed = FalsumSkyAreaBankModel.SanitizeText(
                name,
                FalsumSkyAreaBankModel.MaxNameLength);
            CancelRename();
            if (trimmed.Length == 0)
            {
                return;
            }
            var request = RenamePresetRequested;
            if (request != null)
            {
                request(preset, trimmed);
            }
        }

        private void CancelRename()
        {
            var field = _renaming;
            _renaming = null;
            if (field != null && field.parent != null)
            {
                field.parent.Remove(field);
            }
            RefreshList();
        }

        /// <summary>
        /// One numeric scale field. Values go through the editor's numeric
        /// sanitiser (C-2) and are clamped to the model's range before they
        /// reach the preset, so the UI can never store an illegal factor.
        /// </summary>
        private void AddScaleField(
            VisualElement parent,
            string label,
            FalsumSkyAreaPreset preset,
            bool width)
        {
            var row = new VisualElement();
            row.AddToClassList("bank-scale-row");
            var caption = new Label();
            caption.AddToClassList("bank-scale-caption");
            caption.text = label;
            row.Add(caption);

            var field = new TextField();
            field.AddToClassList("falsum-field");
            field.AddToClassList("bank-scale-field");
            field.maxLength = 8;
            field.isDelayed = true;
            field.SetValueWithoutNotify(
                (width ? preset.WidthScale : preset.LengthScale)
                    .ToString("0.00"));
            row.Add(field);
            parent.Add(row);

            var captured = preset;
            var isWidth = width;
            field.RegisterValueChangedCallback(
                delegate(ChangeEvent<string> evt)
                {
                    var sanitized = FalsumEditorView.SanitizeNumericInput(
                        evt.newValue,
                        true);
                    double parsed;
                    if (!double.TryParse(
                            sanitized,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out parsed))
                    {
                        return;
                    }
                    ApplyScale(captured, isWidth, parsed, field);
                });
        }

        private void ApplyScale(
            FalsumSkyAreaPreset preset,
            bool width,
            double value,
            TextField field)
        {
            if (!FalsumSkyAreaBankModel.IsScaleInRange(value))
            {
                field.SetValueWithoutNotify(
                    (width ? preset.WidthScale : preset.LengthScale)
                        .ToString("0.00"));
                ShowStatus(
                    InFalsus.Gameplay.Falsum.Localization.FalsumLocalization.Get(
                        "falsum.bank.error.invalidScale",
                        ("min", FalsumSkyAreaBankModel.MinScale),
                        ("max", FalsumSkyAreaBankModel.MaxScale)));
                return;
            }
            var request = PresetScalesRequested;
            if (request == null)
            {
                return;
            }
            request(
                preset,
                width ? value : preset.WidthScale,
                width ? preset.LengthScale : value);
        }

        /// <summary>
        /// Opens the import dialog with the clipboard already in the field when
        /// it holds something plausible. The author confirms (or fixes) the
        /// text before anything is imported.
        /// </summary>
        private void OpenImportLayer()
        {
            if (_importLayer == null || _importCode == null)
            {
                return;
            }
            var seed = string.Empty;
            try
            {
                seed = GUIUtility.systemCopyBuffer ?? string.Empty;
            }
            catch (Exception)
            {
                // Clipboard access can fail (no focused window, remote session);
                // an empty field is still a usable dialog.
                seed = string.Empty;
            }
            _importCode.SetValueWithoutNotify(seed.Trim());
            _importLayerOpen = true;
            SetVisible(_importLayer, true);
            _importLayer.BringToFront();
            _importCode.Focus();
        }

        private void CloseImportLayer()
        {
            _importLayerOpen = false;
            if (_importLayer != null)
            {
                SetVisible(_importLayer, false);
            }
        }

        public bool IsImportLayerOpen
        {
            get { return _importLayerOpen; }
        }

        private void OpenShareLayer(FalsumSkyAreaPreset preset)
        {
            // The editor answers SYNCHRONOUSLY, through
            // ShowBankShareCode -> ShowShareCode(preset, code), so the request
            // must be the only thing that opens the layer on the success path.
            //
            // The previous version fired the request and then unconditionally
            // called ShowShareCode(preset, string.Empty) to open the layer. That
            // trailing call overwrote the code the editor had just supplied
            // with an empty string, so the read-only field was always blank --
            // this is the bug behind "分享码看不到".
            //
            // The decision is factored into ShouldOpenShareLayerWithoutRequest
            // so FalsumSelfCheck can assert it without a live panel.
            var request = CopyCodeRequested;
            if (request != null)
            {
                request(preset);
            }
            // Only open it ourselves when the request did NOT (no handler wired,
            // or the encode failed before the editor could call us back). This
            // keeps the layer reachable and shows the status hint from
            // BuildShareCode instead of the button appearing dead.
            if (ShouldOpenShareLayerWithoutRequest(
                    request != null,
                    _shareLayerOpen))
            {
                ShowShareCode(preset, string.Empty);
            }
        }

        /// <summary>
        /// True only when the share layer still has to be opened by the view
        /// because nobody else did. A wired request handler answers
        /// synchronously and opens the layer itself, so opening here as well is
        /// what used to blank the code out.
        ///
        /// <para>PUBLIC for the same reason BuildThumbnailQuads is: this project
        /// has no assembly definitions and no InternalsVisibleTo, so
        /// Assembly-CSharp-Editor cannot see an internal member of
        /// Assembly-CSharp and FalsumSelfCheck could not assert against it.</para>
        /// </summary>
        public static bool ShouldOpenShareLayerWithoutRequest(
            bool handlerWired,
            bool layerAlreadyOpen)
        {
            if (!handlerWired)
            {
                return true;
            }
            return !layerAlreadyOpen;
        }

        private void AddCardButton(
            VisualElement parent,
            string label,
            Action action,
            bool danger = false,
            string extraClass = null)
        {
            var button = new Button();
            button.text = label;
            button.AddToClassList("bank-card-button");
            if (danger)
            {
                button.AddToClassList("is-danger");
            }
            if (!string.IsNullOrEmpty(extraClass))
            {
                button.AddToClassList(extraClass);
            }
            button.clicked += action;
            parent.Add(button);
        }

        /// <summary>
        /// Builds the thumbnail's fill geometry as quads, one per time sample.
        /// Painting only replays this list, which keeps the interesting part
        /// (orientation + easing) pure, deterministic and callable from a
        /// headless batch check with no VisualElement and no camera.
        ///
        /// <para>LAYOUT. <paramref name="rect"/> is in local UI Toolkit
        /// coordinates where y grows DOWNWARD. Time grows UPWARD on the chart
        /// (TrackMotion.NoteZAt returns a larger local z for a later time and
        /// the camera maps +z to screen up), so the group HEAD
        /// (TimeOffsetMs == 0) sits on the BOTTOM edge of the box
        /// (y == rect.yMax) and the TAIL on the TOP edge (y == rect.yMin).
        /// Vertical placement is proportional to the preset's
        /// <see cref="FalsumSkyAreaPreset.TotalDurationMs"/>.</para>
        ///
        /// <para>QUAD ORDER. Quads are appended segment-major, then sample
        /// ascending, so segment <c>s</c> owns
        /// <c>quads[s * stepsPerSegment .. (s + 1) * stepsPerSegment - 1]</c>.
        /// Each <c>Vector2[4]</c> is wound
        /// <c>[headLeft, headRight, tailRight, tailLeft]</c>, i.e. in screen
        /// terms bottom-left, bottom-right, top-right, top-left (the
        /// head-side edge is the visually lower one). Sample <c>i</c> covers
        /// normalised progress <c>p in [i / steps, (i + 1) / steps]</c>, so an
        /// EVEN <paramref name="stepsPerSegment"/> puts a sample boundary
        /// exactly on the segment midpoint.</para>
        ///
        /// <para>EASING. The left and right edges are eased independently with
        /// the same three-case formula as SkyAreaGeometry.Ease /
        /// NoteRenderer.EvaluateSkyNormalizedEdges (0 = linear, 1 = sin
        /// ease-in, 2 = 1 - cos ease-in), which is why the silhouette is a
        /// curve rather than a straight trapezoid.</para>
        ///
        /// <para>ALLOCATION. No LINQ and no per-call List; the only
        /// allocations are the four-element quad arrays themselves.</para>
        ///
        /// <para>VISIBILITY. This is <c>public</c> rather than <c>internal</c>
        /// on purpose: the project has no asmdefs, so the headless checks in
        /// <c>Assets/InFalsus/Editor/</c> land in <c>Assembly-CSharp-Editor</c>
        /// and cannot see <c>internal</c> members of
        /// <c>Assembly-CSharp</c> (there is no InternalsVisibleTo anywhere).
        /// It is a pure geometry helper with no state, so exposing it costs
        /// nothing; it is not a UI surface.</para>
        /// </summary>
        public static void BuildThumbnailQuads(
            FalsumSkyAreaPreset preset,
            Rect rect,
            int stepsPerSegment,
            List<Vector2[]> quads)
        {
            if (quads == null)
            {
                return;
            }
            quads.Clear();
            if (preset == null || preset.Segments.Count == 0)
            {
                return;
            }
            // Zero-height/width presets must still render a hairline, and a
            // bogus step count must not divide by zero.
            var steps = stepsPerSegment < 1 ? 1 : stepsPerSegment;
            var total = Math.Max(1.0, preset.TotalDurationMs);
            var left = Math.Min(preset.MinimumLeft, preset.MaximumRight);
            var span = Math.Max(0.0001, preset.MaximumRight - preset.MinimumLeft);

            for (var s = 0; s < preset.Segments.Count; s++)
            {
                var segment = preset.Segments[s];
                var startLeft = segment.StartPos - segment.StartWidth * 0.5;
                var startRight = segment.StartPos + segment.StartWidth * 0.5;
                var endLeft = segment.EndPos - segment.EndWidth * 0.5;
                var endRight = segment.EndPos + segment.EndWidth * 0.5;
                var timeStart = segment.TimeOffsetMs / total;
                var timeSpan = segment.DurationMs / total;
                for (var i = 0; i < steps; i++)
                {
                    var p0 = (double)i / steps;
                    var p1 = (double)(i + 1) / steps;
                    var easedLeft0 = Ease((float)p0, segment.LeftEasing);
                    var easedLeft1 = Ease((float)p1, segment.LeftEasing);
                    var easedRight0 = Ease((float)p0, segment.RightEasing);
                    var easedRight1 = Ease((float)p1, segment.RightEasing);

                    var xLeft0 = ProjectX(
                        startLeft + (endLeft - startLeft) * easedLeft0,
                        rect,
                        left,
                        span);
                    var xLeft1 = ProjectX(
                        startLeft + (endLeft - startLeft) * easedLeft1,
                        rect,
                        left,
                        span);
                    var xRight0 = ProjectX(
                        startRight + (endRight - startRight) * easedRight0,
                        rect,
                        left,
                        span);
                    var xRight1 = ProjectX(
                        startRight + (endRight - startRight) * easedRight1,
                        rect,
                        left,
                        span);
                    // Head at the bottom: progress 0 maps to rect.yMax.
                    var y0 = rect.yMax -
                             rect.height *
                             (float)(timeStart + timeSpan * p0);
                    var y1 = rect.yMax -
                             rect.height *
                             (float)(timeStart + timeSpan * p1);

                    var quad = new Vector2[4];
                    quad[0] = new Vector2(xLeft0, y0);
                    quad[1] = new Vector2(xRight0, y0);
                    quad[2] = new Vector2(xRight1, y1);
                    quad[3] = new Vector2(xLeft1, y1);
                    quads.Add(quad);
                }
            }
        }

        private static float ProjectX(
            double value,
            Rect rect,
            double left,
            double span)
        {
            return rect.xMin +
                   rect.width * (float)((value - left) / span);
        }

        /// <summary>
        /// The chart's three easing modes (0 = linear, 1 = sin ease-in,
        /// 2 = 1 - cos ease-in), inlined from SkyAreaGeometry.Ease so the UI
        /// layer keeps no dependency on the presentation layer.
        /// </summary>
        private static float Ease(float value, int easing)
        {
            var t = Mathf.Clamp01(value);
            if (easing == 1)
            {
                return Mathf.Sin(t * Mathf.PI * 0.5f);
            }
            if (easing == 2)
            {
                return 1.0f - Mathf.Cos(t * Mathf.PI * 0.5f);
            }
            return t;
        }

        private void DrawThumbnail(
            MeshGenerationContext context,
            VisualElement element,
            FalsumSkyAreaPreset preset)
        {
            // Use the element's real box: cards can be wider than the USS
            // default, and a hardcoded 96x54 box would clip the silhouette.
            var rect = new Rect(0.0f, 0.0f, 0.0f, 0.0f);
            if (element != null)
            {
                rect = element.contentRect;
            }
            if (rect.width < 1.0f || rect.height < 1.0f)
            {
                rect = new Rect(
                    0.0f,
                    0.0f,
                    ThumbnailFallbackWidth,
                    ThumbnailFallbackHeight);
            }

            var painter = context.painter2D;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.fillColor = ThumbnailBackgroundColor;
            painter.Fill();

            BuildThumbnailQuads(
                preset,
                rect,
                ThumbnailStepsPerSegment,
                _thumbnailQuads);
            for (var i = 0; i < _thumbnailQuads.Count; i++)
            {
                var quad = _thumbnailQuads[i];
                painter.BeginPath();
                painter.MoveTo(quad[0]);
                painter.LineTo(quad[1]);
                painter.LineTo(quad[2]);
                painter.LineTo(quad[3]);
                painter.ClosePath();
                painter.fillColor = ThumbnailFillColor;
                painter.Fill();
                painter.strokeColor = ThumbnailStrokeColor;
                painter.lineWidth = 1.0f;
                painter.Stroke();
            }
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null)
            {
                return;
            }
            if (visible)
            {
                element.RemoveFromClassList("hidden");
            }
            else
            {
                element.AddToClassList("hidden");
            }
        }

        private static T Require<T>(VisualElement root, string name)
            where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.ui.bankElementMissing",
                        ("name", name)));
            }
            return element;
        }
    }
}
