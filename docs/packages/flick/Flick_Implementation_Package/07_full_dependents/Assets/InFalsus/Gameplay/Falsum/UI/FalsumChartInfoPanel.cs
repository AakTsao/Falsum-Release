using System;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Standalone "chart info" panel: the document-scoped counterpart of the
    /// global preferences dialog.
    ///
    /// Chart metadata belongs to the loaded .falsum package, not to the user's
    /// global settings, so it gets its own panel and its own entry point (the
    /// song-info cover).
    ///
    /// The panel reuses the preferences UXML, so it has to strip everything
    /// that only makes sense for global preferences: the tab rail, the
    /// "preferences" eyebrow, the reset/cancel/complete footer and the scope
    /// note. What remains is a single scrolling document panel whose only
    /// action is Done, which commits the edited draft.
    /// </summary>
    internal sealed class FalsumChartInfoPanel
    {
        private readonly VisualTreeAsset _template;
        private readonly VisualElement _modalLayer;

        private VisualElement _overlay;
        private VisualElement _window;
        private ScrollView _content;
        private Label _status;
        private FalsumChartInfoPage _page;
        private bool _open;

        private readonly VisualElement _editorRoot;
        private readonly VisualElement _visualRoot;
        private readonly Action<string> _onBackgroundChanged;
        private readonly Action _onBackgroundExited;
        private FalsumBackgroundPreviewPanel _backgroundPreview;

        public FalsumChartInfoPanel(
            VisualTreeAsset template,
            VisualElement modalLayer,
            VisualElement editorRoot,
            VisualElement visualRoot,
            Action<string> onBackgroundChanged,
            Action onBackgroundExited)
        {
            _template = template;
            _modalLayer = modalLayer;
            _editorRoot = editorRoot;
            _visualRoot = visualRoot;
            _onBackgroundChanged = onBackgroundChanged;
            _onBackgroundExited = onBackgroundExited;
        }

        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>The panel's visual tree root, for debug/self-check queries.</summary>
        public VisualElement Root
        {
            get { return _overlay; }
        }

        /// <summary>The cover preview element, so the host can push textures.</summary>
        public VisualElement CoverPreview
        {
            get { return _page != null ? _page.CoverPreview : null; }
        }

        /// <summary>
        /// Raised when the user confirms. The host applies the draft and then
        /// calls <see cref="CloseImmediate"/>; the panel does not mutate the
        /// package itself.
        /// </summary>
        public event Action ConfirmRequested;

        /// <summary>Raised when the user cancels or closes without confirming.</summary>
        public event Action CancelRequested;

        /// <summary>
        /// Shows the panel for the given draft. The caller keeps ownership of
        /// the draft and the package; the panel only edits the draft through
        /// the page host.
        /// </summary>
        public void Show(IFalsumPreferencesPageHost host)
        {
            CloseImmediate();
            if (_template == null || _modalLayer == null || host == null)
            {
                return;
            }
            var draft = host.MetadataDraft;
            if (draft == null || !draft.HasPackage || host.Package == null)
            {
                return;
            }
            var instance = _template.Instantiate();
            L.ApplyStaticText(instance);
            _overlay = instance.Q<VisualElement>("PreferencesOverlay");
            _window = instance.Q<VisualElement>("PreferencesWindow");
            _content = instance.Q<ScrollView>("PreferencesContent");
            _status = instance.Q<Label>("PreferencesStatus");
            if (_overlay == null || _window == null || _content == null)
            {
                return;
            }

            AdaptShell(instance, draft);
            BindBackgroundPreview(instance);

            _modalLayer.Add(_overlay);
            _modalLayer.BringToFront();
            _overlay.RemoveFromClassList("hidden");
            _open = true;
            _page = new FalsumChartInfoPage();
            _page.Build(_content, host);
            FalsumUiMotion.AnimateModalIn(_overlay, _window);
        }

        /// <summary>
        /// Removes everything that is specific to the global preferences
        /// dialog and rewrites the header for the chart document.
        /// </summary>
        private void AdaptShell(
            VisualElement instance,
            FalsumChartMetadataDraft draft)
        {
            // The tab rail is meaningless here: there is exactly one document.
            var nav = instance.Q<VisualElement>(
                className: "preferences-nav");
            if (nav != null)
            {
                nav.RemoveFromHierarchy();
            }

            var title = instance.Q<Label>("PreferencesTitle");
            if (title != null)
            {
                title.text = L.Get("falsum.chartinfo.title");
            }
            var subtitle = instance.Q<Label>("PreferencesSubtitle");
            if (subtitle != null)
            {
                subtitle.text = L.Get(
                    "falsum.chartinfo.subtitle",
                    ("package",
                     draft.SongId ?? string.Empty));
            }

            // Only Done survives: reset/cancel/complete belong to global prefs.
            var reset = instance.Q<Button>("PreferencesResetDefaults");
            if (reset != null)
            {
                reset.RemoveFromHierarchy();
            }
            var cancel = instance.Q<Button>("PreferencesCancel");
            if (cancel != null)
            {
                cancel.RemoveFromHierarchy();
            }
            var complete = instance.Q<Button>("PreferencesComplete");
            if (complete != null)
            {
                complete.text = L.Get("falsum.chartinfo.done");
                complete.clicked += ConfirmRequested;
            }

            var close = instance.Q<Button>("PreferencesClose");
            if (close != null)
            {
                close.clicked += CancelRequested;
            }

            // Esc closes without committing, matching the old cancel path.
            _overlay.RegisterCallback<KeyDownEvent>(
                OnKeyDown,
                TrickleDown.TrickleDown);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // The background picker is a sub-modal of this shell too: while
            // it is up, Esc closes the picker instead of the whole panel and
            // Tab stays inside it. Before this, only the global preferences
            // shell routed those keys.
            if (IsBackgroundPreviewOpen)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    _backgroundPreview.Cancel();
                    return;
                }
                if (evt.keyCode == KeyCode.Tab)
                {
                    FalsumPreferencesUiHelpers.TrapFocus(
                        _backgroundPreview.Root,
                        evt);
                }
                return;
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                var cancel = CancelRequested;
                if (cancel != null)
                {
                    cancel();
                }
                return;
            }
            if (evt.keyCode == KeyCode.Tab && _window != null)
            {
                FalsumPreferencesUiHelpers.TrapFocus(_window, evt);
            }
        }

        /// <summary>
        /// Binds the background picker that lives in this shell's own copy of
        /// the preferences UXML, exactly as the global preferences dialog
        /// binds its instance. Without this the chart-info page's preview
        /// button ran against a null picker (EnterBackgroundPreview threw).
        /// </summary>
        private void BindBackgroundPreview(VisualElement instance)
        {
            var panel = instance.Q<VisualElement>("BackgroundPreviewPanel");
            if (panel == null)
            {
                return;
            }
            // The picker's button captions are bound by the preferences
            // dialog's own BindStaticControls, which never runs for this
            // shell; bind them here or the buttons show up blank.
            var cancelButton = instance.Q<Button>("BackgroundPreviewCancel");
            if (cancelButton != null)
            {
                L.Bind(cancelButton, "falsum.common.cancel");
            }
            var confirmButton = instance.Q<Button>("BackgroundPreviewConfirm");
            if (confirmButton != null)
            {
                L.Bind(
                    confirmButton,
                    "falsum.preferences.background.confirm");
            }
            var closeButton = instance.Q<Button>("BackgroundPreviewClose");
            if (closeButton != null)
            {
                L.BindTooltip(closeButton, "falsum.common.close");
            }
            _backgroundPreview = new FalsumBackgroundPreviewPanel(
                panel,
                instance.Q<VisualElement>("BackgroundPreviewHero"),
                instance.Q<Label>("BackgroundPreviewName"),
                instance.Q<Label>("BackgroundPreviewMeta"),
                instance.Q<ScrollView>("BackgroundPreviewOptions"),
                instance.Q<Label>("BackgroundPreviewSelection"),
                instance.Q<Label>("BackgroundPreviewStatus"),
                _overlay,
                _editorRoot,
                _visualRoot,
                _onBackgroundChanged,
                _onBackgroundExited);
        }

        /// <summary>True when this shell built a background picker.</summary>
        public bool HasBackgroundPreview
        {
            get { return _backgroundPreview != null; }
        }

        /// <summary>True while this shell's picker is on screen.</summary>
        public bool IsBackgroundPreviewOpen
        {
            get
            {
                return _backgroundPreview != null &&
                       _backgroundPreview.IsOpen;
            }
        }

        /// <param name="current">Background the draft currently holds.</param>
        public void OpenBackgroundPreview(string current)
        {
            if (_backgroundPreview == null)
            {
                return;
            }
            _backgroundPreview.Show(current);
        }

        /// <summary>Esc/close path; the self-check uses it to unwind.</summary>
        public void CancelBackgroundPreview()
        {
            if (_backgroundPreview != null)
            {
                _backgroundPreview.Cancel();
            }
        }

        /// <summary>Enter/Done path: keeps the highlighted background.</summary>
        public void ConfirmBackgroundPreview()
        {
            if (_backgroundPreview != null)
            {
                _backgroundPreview.Confirm();
            }
        }

        public void ShowBackgroundError(string message)
        {
            if (_backgroundPreview == null)
            {
                ShowError(message);
                return;
            }
            _backgroundPreview.ShowStatus(message);
        }

        public void ShowError(string message)
        {
            if (_status == null)
            {
                return;
            }
            _status.text = message ?? string.Empty;
            _status.RemoveFromClassList("is-info");
            FalsumPreferencesUiHelpers.SetVisible(
                _status,
                !string.IsNullOrWhiteSpace(message));
        }

        /// <summary>
        /// Test hook: invokes the Done path without synthesising a pointer
        /// event, which UI Toolkit does not let an editor harness do reliably.
        /// </summary>
        public void DebugConfirm()
        {
            var confirm = ConfirmRequested;
            if (confirm != null)
            {
                confirm();
            }
        }

        /// <summary>Test hook: invokes the cancel path.</summary>
        public void DebugCancel()
        {
            var cancel = CancelRequested;
            if (cancel != null)
            {
                cancel();
            }
        }

        public void CloseImmediate()
        {
            if (_page != null)
            {
                _page.ReleaseCoverTexture();
            }
            if (_backgroundPreview != null)
            {
                // CloseImmediate runs Exit(), which fires the exit callback so
                // the editor restores its preview state exactly once. Disposing
                // without it would leave the editor stuck in "preview" mode.
                _backgroundPreview.CloseImmediate();
                _backgroundPreview.Dispose();
                _backgroundPreview = null;
            }
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
            }
            _overlay = null;
            _window = null;
            _content = null;
            _status = null;
            _page = null;
            _open = false;
        }

        /// <summary>
        /// Pushes a freshly converted cover into the panel preview, for the
        /// "replace cover" flow.
        /// </summary>
        public void SetCoverBytes(byte[] bytes)
        {
            if (_page != null)
            {
                _page.SetCoverBytes(bytes);
            }
        }
    }
}
