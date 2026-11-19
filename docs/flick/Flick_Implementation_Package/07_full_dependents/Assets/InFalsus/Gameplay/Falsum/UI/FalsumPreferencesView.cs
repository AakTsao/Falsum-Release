using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Localization;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public sealed class FalsumPreferencesView
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;
        private const float VersionTapWindowSeconds = 1.2f;

        private readonly VisualElement _editorRoot;
        private readonly VisualElement _visualRoot;
        private readonly VisualElement _modalLayer;
        private readonly VisualTreeAsset _template;

        private VisualElement _overlay;
        private VisualElement _window;
        private VisualElement _content;
        private VisualElement _coverPreview;
        private Label _title;
        private Label _subtitle;
        private Label _status;
        private Label _cacheSizeLabel;
        private Button _tabDisplay;
        private Button _tabVolume;
        private Button _tabKeys;
        private Button _tabEditing;
        private Button _tabBackup;
        private Button _tabAdvanced;
        private Button _tabAbout;
        private Button _completeButton;
        private IVisualElementScheduledItem _initialTabBuild;
        private Texture2D _coverTexture;
        private Texture2D _sharedCoverTexture;
        private FalsumPreferences _preferences;
        private FalsumUiStyle _styleOnOpen;
        private FalsumChartMetadataDraft _metadataDraft;
        private FalsumPackage _package;
        private FalsumDisplayConfirmDialog _displayConfirm;
        private FalsumBackgroundPreviewPanel _backgroundPreview;
        private readonly FalsumKeyCaptureController _keyCapture =
            new FalsumKeyCaptureController();
        private readonly FalsumPreferencesPageHost _pageHost;
        private FalsumPreferencesTab _activeTab;
        private bool _open;
        private bool _closing;
        private readonly FalsumAboutPreferencesPage _aboutPage;
        private readonly FalsumAdvancedPreferencesPage _advancedPage =
            new FalsumAdvancedPreferencesPage();
        private readonly FalsumEditingPreferencesPage _editingPage =
            new FalsumEditingPreferencesPage();
        private FalsumChartInfoPanel _chartInfoPanel;

        public FalsumPreferencesView(
            VisualElement editorRoot,
            VisualElement modalLayer,
            VisualTreeAsset template)
        {
            _editorRoot = editorRoot;
            _visualRoot = editorRoot != null
                ? editorRoot.Q<VisualElement>("FalsumRoot") ??
                  editorRoot
                : null;
            _modalLayer = modalLayer;
            _template = template;
            _pageHost = new FalsumPreferencesPageHost(this);
            _chartInfoPanel = new FalsumChartInfoPanel(
                template,
                modalLayer,
                _editorRoot,
                _visualRoot,
                ApplyBackgroundSelection,
                OnBackgroundPreviewExited);
            _chartInfoPanel.ConfirmRequested += RaiseChartInfoCommit;
            _chartInfoPanel.CancelRequested += RaiseChartInfoCancel;
            _aboutPage = new FalsumAboutPreferencesPage(
                () => Raise(DisclaimerRequested),
                () => Raise(PerformanceWindowToggleRequested));
        }

        private void RaiseChartInfoCommit()
        {
            var commit = ChartInfoCommitRequested;
            if (commit != null)
            {
                commit(_metadataDraft, _package);
            }
        }

        private void RaiseChartInfoCancel()
        {
            var cancel = ChartInfoCancelRequested;
            if (cancel != null)
            {
                cancel();
            }
        }

        public event Action<
            FalsumPreferences,
            FalsumChartMetadataDraft,
            FalsumPackage> CommitRequested;

        public event Action CancelRequested;
        public event Action BrowseCoverRequested;
        public event Action BrowseAudioRequested;
        public event Action OpenBackupFolderRequested;
        public event Action OpenCacheFolderRequested;
        public event Action ClearCacheRequested;
        public event Action<FalsumBackupEntry>
            RestoreSelectedBackupRequested;
        public event Action BackgroundPreviewEntered;
        public event Action BackgroundPreviewExited;
        public event Action<string> BackgroundPreviewChanged;
        public event Action<int> DifficultySwitchRequested;
        public event Action DisclaimerRequested;
        public event Action PerformanceWindowToggleRequested;
        public event Action<FalsumUiStyle> UiStylePreviewRequested;

        /// <summary>
        /// Raised when the developer switch flips between the desktop and
        /// the Android phone layout. Unlike the UI style preview this is
        /// NOT reverted on cancel: it is a review switch, and snapping the
        /// layout back while the user is still looking at it is hostile.
        /// </summary>
        public event Action<bool> MobileLayoutPreviewRequested;

        /// <summary>Raised while the developer control-size slider moves.</summary>
        public event Action<float> MobileScalePreviewRequested;
        /// <summary>
        /// Raised while the display page's scene-opacity sliders move, so
        /// the canvas updates before the user presses Complete. The event
        /// carries the edited group; the shell restores the committed group on
        /// cancel.
        /// </summary>
        public event Action<FalsumVisualPreferences> VisualPreviewRequested;

        public bool IsOpen
        {
            get { return _open; }
        }

        public bool IsCapturingKey
        {
            get { return _keyCapture.IsCapturing; }
        }

        public bool IsBackgroundPreviewOpen
        {
            get
            {
                // Two shells can own the picker: the global preferences
                // dialog and the standalone chart-info panel. Reporting only
                // the preferences one made the chart-info picker invisible to
                // the editor's preview-state queries.
                return (_backgroundPreview != null &&
                        _backgroundPreview.IsOpen) ||
                       (_chartInfoPanel != null &&
                        _chartInfoPanel.IsBackgroundPreviewOpen);
            }
        }

        public string DebugActiveTab
        {
            get { return _activeTab.ToString(); }
        }

        public bool DebugDisableOutOfBoundsClamping
        {
            get
            {
                return _preferences != null &&
                       _preferences.Advanced != null &&
                       _preferences.Advanced
                           .DisableOutOfBoundsClamping;
            }
        }

        public FalsumUiStyle DebugUiStyle
        {
            get
            {
                return _preferences != null
                    ? _preferences.Advanced.UIStyle
                    : FalsumUiStyle.Classic;
            }
        }

        public void DebugSetUiStyle(FalsumUiStyle value)
        {
            if (_preferences == null || _content == null)
            {
                return;
            }
            if (!FalsumUiStyleSwitcher.Available)
            {
                // The picker is hidden; keep debug hooks honest by pinning
                // the rendered style instead of silently accepting a value
                // no control can display.
                value = FalsumUiStyle.Classic;
            }
            _preferences.Advanced.UIStyle = value;
            var style = _content.Q<DropdownField>(
                "PreferencesUIStyle");
            if (style != null)
            {
                style.index = (int)value;
            }
            PreviewUiStyle();
        }

        public void DebugSetDisableOutOfBoundsClamping(bool value)
        {
            _preferences.Editing.DisableOutOfBoundsClamping = value;
            ClearStatus();
        }

        public bool DebugAboutIsReady
        {
            get
            {
                if (_content == null ||
                    _activeTab != FalsumPreferencesTab.About)
                {
                    return false;
                }
                var logo = _content.Q<Image>(
                    "PreferencesAboutLogo");
                var version = _content.Q<Label>(
                    "PreferencesAboutVersion");
                var credits = _content.Q<Label>(
                    "PreferencesAboutCredits");
                return logo != null &&
                       logo.image != null &&
                       version != null &&
                       version.text ==
                       FalsumEditorInfo.VersionLine &&
                       credits != null &&
                       credits.text ==
                       FalsumEditorInfo.CreditsBody;
            }
        }

        public int DebugAboutVersionTapCount
        {
            get { return _aboutPage.TapCount; }
        }

        public bool DebugTapAboutVersion()
        {
            var triggered = _aboutPage.RegisterTap(
                Time.unscaledTimeAsDouble);
            if (triggered)
            {
                Raise(PerformanceWindowToggleRequested);
            }
            return triggered;
        }

        public void DebugResetAboutVersionTaps()
        {
            _aboutPage.ResetTaps();
        }

        public bool DebugRequestAboutDisclaimer()
        {
            if (_content == null)
            {
                return false;
            }
            var button = _content.Q<Button>(
                "PreferencesAboutDisclaimerButton");
            if (button == null)
            {
                return false;
            }
            Raise(DisclaimerRequested);
            return true;
        }

        public void DebugShowTab(string tab)
        {
            FalsumPreferencesTab parsed;
            if (!Enum.TryParse(tab, true, out parsed))
            {
                parsed = FalsumPreferencesTab.Display;
            }
            ShowTab(parsed);
        }

        public void DebugCommit()
        {
            RequestCommit();
        }


        public void DebugScrollContentToBottom()
        {
            var scroll = _content as ScrollView;
            if (scroll != null)
            {
                scroll.scrollOffset = new Vector2(0.0f, 100000.0f);
            }
        }

        public void DebugScrollContentToTop()
        {
            var scroll = _content as ScrollView;
            if (scroll != null)
            {
                scroll.scrollOffset = Vector2.zero;
            }
        }

        public int DebugFirstDisabledDifficulty
        {
            get
            {
                if (_content == null)
                {
                    return -1;
                }
                for (var i = 0; i < 4; i++)
                {
                    var card = _content.Q<VisualElement>(
                        "PreferencesDifficultyCard" + i);
                    if (card != null &&
                        card.ClassListContains("is-disabled"))
                    {
                        return i;
                    }
                }
                return -1;
            }
        }

        public bool DebugRequestDifficultySwitch(int difficulty)
        {
            if (_content == null || difficulty < 0 || difficulty > 3)
            {
                return false;
            }
            var card = _content.Q<VisualElement>(
                "PreferencesDifficultyCard" + difficulty);
            if (card == null ||
                !card.ClassListContains("is-disabled"))
            {
                return false;
            }
            Raise(DifficultySwitchRequested, difficulty);
            return true;
        }

        public void DebugEnterBackgroundPreview()
        {
            if (!IsBackgroundPreviewOpen)
            {
                EnterBackgroundPreview();
            }
        }
        public void DebugSelectBackground(string fileName)
        {
            if (_backgroundPreview == null)
            {
                return;
            }
            _backgroundPreview.SelectByFileName(fileName);
        }

        public void DebugCancelBackgroundPreview()
        {
            if (_backgroundPreview != null &&
                _backgroundPreview.IsOpen)
            {
                _backgroundPreview.Cancel();
            }
            else if (_chartInfoPanel != null &&
                     _chartInfoPanel.IsBackgroundPreviewOpen)
            {
                _chartInfoPanel.CancelBackgroundPreview();
            }
        }

        public void DebugConfirmBackgroundPreview()
        {
            if (_backgroundPreview != null &&
                _backgroundPreview.IsOpen)
            {
                _backgroundPreview.Confirm();
            }
            else if (_chartInfoPanel != null &&
                     _chartInfoPanel.IsBackgroundPreviewOpen)
            {
                _chartInfoPanel.ConfirmBackgroundPreview();
            }
        }

        public void Show(
            FalsumPreferences preferences,
            FalsumChartMetadataDraft metadata,
            FalsumPackage package,
            Texture2D sharedCoverTexture = null)
        {
            CloseImmediate();
            if (_template == null || _modalLayer == null)
            {
                return;
            }
            _preferences = preferences != null
                ? preferences.Clone()
                : FalsumPreferences.CreateDefault();
            _preferences.Normalize();
            _styleOnOpen = _preferences.Advanced.UIStyle;
            _metadataDraft = metadata;
            _package = package;
            _sharedCoverTexture = sharedCoverTexture;

            var instance = _template.Instantiate();
            L.ApplyStaticText(instance);
            _overlay = Require<VisualElement>(
                instance,
                "PreferencesOverlay");
            _window = Require<VisualElement>(
                instance,
                "PreferencesWindow");
            var contentScroll = Require<ScrollView>(
                instance,
                "PreferencesContent");
            _content = contentScroll;
            contentScroll.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            contentScroll.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _title = Require<Label>(instance, "PreferencesTitle");
            _subtitle = Require<Label>(
                instance,
                "PreferencesSubtitle");
            _status = Require<Label>(
                instance,
                "PreferencesStatus");
            Require<Label>(
                instance,
                "PreferencesVersion").text =
                L.Get(
                    "falsum.preferences.versionLine",
                    ("editorName", FalsumEditorInfo.ProductName),
                    ("version", FalsumEditorInfo.VersionLine));
            _tabDisplay = Require<Button>(
                instance,
                "PreferencesTabDisplay");
            _tabVolume = Require<Button>(
                instance,
                "PreferencesTabVolume");
            _tabKeys = Require<Button>(
                instance,
                "PreferencesTabKeys");
            _tabEditing = Require<Button>(
                instance,
                "PreferencesTabEditing");
            _tabBackup = Require<Button>(
                instance,
                "PreferencesTabBackup");
            _tabAdvanced = Require<Button>(
                instance,
                "PreferencesTabAdvanced");
            // About is a page of its own, so it needs a nav button of its
            // own. The template is owned elsewhere and may not carry one
            // yet, so the button is created here and inserted after
            // Advanced; an existing button of that name is reused instead
            // of duplicated.
            _tabAbout = EnsureTabButton(
                instance,
                _tabAdvanced,
                "PreferencesTabAbout");
            _completeButton = Require<Button>(
                instance,
                "PreferencesComplete");
            _backgroundPreview = new FalsumBackgroundPreviewPanel(
                Require<VisualElement>(
                    instance,
                    "BackgroundPreviewPanel"),
                Require<VisualElement>(
                    instance,
                    "BackgroundPreviewHero"),
                Require<Label>(
                    instance,
                    "BackgroundPreviewName"),
                Require<Label>(
                    instance,
                    "BackgroundPreviewMeta"),
                Require<ScrollView>(
                    instance,
                    "BackgroundPreviewOptions"),
                Require<Label>(
                    instance,
                    "BackgroundPreviewSelection"),
                Require<Label>(
                    instance,
                    "BackgroundPreviewStatus"),
                _overlay,
                _editorRoot,
                _visualRoot,
                ApplyBackgroundSelection,
                OnBackgroundPreviewExited);
            var displayConfirmLayer = Require<VisualElement>(
                instance,
                "DisplayConfirmLayer");
            var displayCountdownLabel = Require<Label>(
                instance,
                "DisplayConfirmCountdown");
            _displayConfirm = new FalsumDisplayConfirmDialog(
                displayConfirmLayer,
                displayCountdownLabel,
                _overlay);

            BindStaticControls(instance);
            _modalLayer.Add(_overlay);
            _modalLayer.BringToFront();
            _open = true;
            _overlay.RemoveFromClassList("hidden");
            ShowInitialLoading();
            _tabDisplay.Focus();
            FalsumUiMotion.AnimateModalIn(_overlay, _window);
            _initialTabBuild = _overlay.schedule.Execute(
                    (Action)delegate
                    {
                        _initialTabBuild = null;
                        if (!_open || _content == null)
                        {
                            return;
                        }
                        ShowTab(FalsumPreferencesTab.Display);
                        if (_metadataDraft != null &&
                            !_metadataDraft.HasPackage)
                        {
                            ShowInfo(
                                L.Get(
                                    "falsum.preferences.noPackageGlobalEditable"));
                        }
                    })
                .StartingIn(16);
        }

        public void Close()
        {
            Close(false);
        }

        public void CloseImmediate()
        {
            Close(true);
        }

        private void Close(bool immediate)
        {
            if ((!_open && _overlay == null) || _closing)
            {
                return;
            }
            _displayConfirm.Hide(false);
            if (_backgroundPreview != null &&
                _backgroundPreview.IsOpen)
            {
                _backgroundPreview.CloseImmediate();
            }
            else if (_chartInfoPanel != null &&
                     _chartInfoPanel.IsBackgroundPreviewOpen)
            {
                _chartInfoPanel.CancelBackgroundPreview();
            }
            if (_initialTabBuild != null)
            {
                _initialTabBuild.Pause();
                _initialTabBuild = null;
            }
            if (!immediate &&
                _overlay != null &&
                _window != null &&
                _overlay.panel != null)
            {
                _closing = true;
                FalsumUiMotion.AnimateModalOut(
                    _overlay,
                    _window,
                    CompleteClose);
                return;
            }
            CompleteClose();
        }

        private void CompleteClose()
        {
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
            }
            if (_backgroundPreview != null)
            {
                _backgroundPreview.Dispose();
                _backgroundPreview = null;
            }
            ReleaseCoverTexture();
            _sharedCoverTexture = null;
            _keyCapture.Rebind(() => _preferences, ShowError, ClearStatus);
            _overlay = null;
            _window = null;
            _content = null;
            _coverPreview = null;
            _displayConfirm = null;
            _open = false;
            _closing = false;
            _editingPage.GridColorsInvalid = false;
            _editingPage.TimeSubdivisionInvalid = false;
            _editingPage.DenominatorSequenceInvalid = false;
        }

        public void ShowError(string message)
        {
            if (_status == null)
            {
                return;
            }
            _status.text = message ?? string.Empty;
            _status.RemoveFromClassList("is-info");
            SetVisible(_status, !string.IsNullOrWhiteSpace(message));
        }

        public void ShowInfo(string message)
        {
            if (_status == null)
            {
                return;
            }
            _status.text = message ?? string.Empty;
            _status.AddToClassList("is-info");
            SetVisible(_status, !string.IsNullOrWhiteSpace(message));
        }

        public void ClearStatus()
        {
            if (_status == null)
            {
                return;
            }
            _status.text = string.Empty;
            _status.RemoveFromClassList("is-info");
            SetVisible(_status, false);
        }

        /// <summary>
        /// Dialog-wide key capture state, exposed to pages through the page
        /// host. Capture must stay above the pages: a pending capture has to
        /// survive a tab switch and still block commit.
        /// </summary>
        internal FalsumKeyCaptureController KeyCaptureForPages
        {
            get { return _keyCapture; }
        }

        /// <summary>Re-shows a tab after a page rewrote its own draft state.</summary>
        internal void RebuildTabForPages(FalsumPreferencesTab tab)
        {
            ShowTab(tab);
        }

        /// <summary>Replaces the draft with defaults and rebuilds the given tab.</summary>
        internal void ResetAllDefaultsForPages(FalsumPreferencesTab tab)
        {
            _preferences = FalsumPreferences.CreateDefault();
            ShowInfo(
                L.Get("falsum.preferences.globalDefaultsRestored"));
            ShowTab(tab);
        }

        /// <summary>Pushes a UI-style preview to the live editor.</summary>
        internal void PreviewUiStyleForPages(FalsumUiStyle style)
        {
            var preview = UiStylePreviewRequested;
            if (preview != null)
            {
                preview(style);
            }
        }

        internal void RaiseOpenCacheFolderForPages()
        {
            Raise(OpenCacheFolderRequested);
        }

        internal void RaiseClearCacheForPages()
        {
            Raise(ClearCacheRequested);
        }

        /// <summary>The package this dialog was opened for, exposed to pages.</summary>
        internal FalsumPackage PackageForPages
        {
            get { return _package; }
        }

        internal void RaiseOpenBackupFolderForPages()
        {
            Raise(OpenBackupFolderRequested);
        }

        internal void RaiseRestoreBackupForPages(FalsumBackupEntry entry)
        {
            Raise(RestoreSelectedBackupRequested, entry);
        }

        internal FalsumChartMetadataDraft MetadataDraftForPages
        {
            get { return _metadataDraft; }
        }

        internal Texture2D SharedCoverTextureForPages
        {
            get { return _sharedCoverTexture; }
        }

        internal void RaiseBrowseCoverForPages()
        {
            Raise(BrowseCoverRequested);
        }

        /// <summary>
        /// Pushes the edited scene-opacity group to the live editor. The
        /// view owns the draft copy of the preferences, so the page cannot
        /// reach the editor directly (C-4) and goes through this event.
        /// </summary>
        internal void PreviewVisualForPages()
        {
            var preview = VisualPreviewRequested;
            if (preview != null && _preferences != null)
            {
                preview(_preferences.Visual);
            }
        }

        internal void RaiseBrowseAudioForPages()
        {
            Raise(BrowseAudioRequested);
        }

        internal void OpenBackgroundPreviewForPages()
        {
            EnterBackgroundPreview();
        }

        internal void RaiseSwitchDifficultyForPages(int difficulty)
        {
            Raise(DifficultySwitchRequested, difficulty);
        }

        /// <summary>
        /// Shows the standalone chart-info panel. Chart metadata is document
        /// scope, so it gets its own panel and entry point; the panel shares
        /// this view's page host because the services a page may use (status,
        /// media browsing, difficulty switch, background picker) live here.
        /// </summary>
        public void ShowChartInfo(
            FalsumChartMetadataDraft metadata,
            FalsumPackage package)
        {
            if (_chartInfoPanel == null)
            {
                return;
            }
            _metadataDraft = metadata;
            _package = package;
            _chartInfoPanel.Show(_pageHost);
        }

        /// <summary>
        /// Raised when the chart info panel's Done is pressed. The editor
        /// applies the draft through the metadata service; the view does not
        /// touch the package.
        /// </summary>
        public event Action<
            FalsumChartMetadataDraft,
            FalsumPackage> ChartInfoCommitRequested;

        /// <summary>Raised when the chart info panel closes without confirming.</summary>
        public event Action ChartInfoCancelRequested;

        public void CloseChartInfo()
        {
            if (_chartInfoPanel != null)
            {
                _chartInfoPanel.CloseImmediate();
            }
        }

        /// <summary>Shows an error inside the chart info panel.</summary>
        public void ShowChartInfoError(string message)
        {
            if (_chartInfoPanel != null)
            {
                _chartInfoPanel.ShowError(message);
            }
        }

        public bool IsChartInfoOpen
        {
            get
            {
                return _chartInfoPanel != null &&
                       _chartInfoPanel.IsOpen;
            }
        }

        /// <summary>Re-shows the chart info panel after a difficulty switch.</summary>
        public void RefreshChartInfoPanel()
        {
            if (_chartInfoPanel == null || !_chartInfoPanel.IsOpen)
            {
                return;
            }
            _chartInfoPanel.CloseImmediate();
            _chartInfoPanel.Show(_pageHost);
        }

        /// <summary>Test hook: runs the chart info panel's Done path.</summary>
        public void DebugConfirmChartInfo()
        {
            if (_chartInfoPanel != null)
            {
                _chartInfoPanel.DebugConfirm();
            }
        }

        /// <summary>Test hook: runs the chart info panel's cancel path.</summary>
        public void DebugCancelChartInfo()
        {
            if (_chartInfoPanel != null)
            {
                _chartInfoPanel.DebugCancel();
            }
        }

        /// <summary>
        /// Test hook: true when the open chart-info shell built its own
        /// background picker. This is the exact condition that used to be
        /// false and cause the reported crash.
        /// </summary>
        public bool DebugChartInfoBackgroundPickerReady
        {
            get
            {
                return _chartInfoPanel != null &&
                       _chartInfoPanel.IsOpen &&
                       _chartInfoPanel.HasBackgroundPreview;
            }
        }

        /// <summary>Test hook: opens the chart-info shell's background picker.</summary>
        public bool DebugOpenChartInfoBackgroundPreview()
        {
            if (_chartInfoPanel == null || !_chartInfoPanel.IsOpen)
            {
                return false;
            }
            EnterBackgroundPreview();
            return _chartInfoPanel.IsBackgroundPreviewOpen;
        }

        public bool DebugChartInfoBackgroundPreviewOpen
        {
            get
            {
                return _chartInfoPanel != null &&
                       _chartInfoPanel.IsBackgroundPreviewOpen;
            }
        }

        /// <summary>Test hook: closes the chart-info picker without confirming.</summary>
        public void DebugCancelChartInfoBackgroundPreview()
        {
            if (_chartInfoPanel != null)
            {
                _chartInfoPanel.CancelBackgroundPreview();
            }
        }

        /// <summary>
        /// First difficulty index whose card is disabled in the chart info
        /// panel, or -1. Used by the self-check now that the chart cards live
        /// outside the preferences dialog.
        /// </summary>
        public int DebugFirstDisabledDifficultyOnPanel
        {
            get
            {
                var root = _chartInfoPanel != null
                    ? _chartInfoPanel.Root
                    : null;
                if (root == null)
                {
                    return -1;
                }
                for (var i = 0; i < 4; i++)
                {
                    var card = root.Q<VisualElement>(
                        "PreferencesDifficultyCard" + i);
                    if (card != null &&
                        card.ClassListContains("is-disabled"))
                    {
                        return i;
                    }
                }
                return -1;
            }
        }

        /// <summary>Raises the difficulty-switch request from a panel card.</summary>
        public bool DebugRequestDifficultySwitchOnPanel(int difficulty)
        {
            var root = _chartInfoPanel != null
                ? _chartInfoPanel.Root
                : null;
            if (root == null || difficulty < 0 || difficulty > 3)
            {
                return false;
            }
            var card = root.Q<VisualElement>(
                "PreferencesDifficultyCard" + difficulty);
            if (card == null ||
                !card.ClassListContains("is-disabled"))
            {
                return false;
            }
            Raise(DifficultySwitchRequested, difficulty);
            return true;
        }

        /// <summary>Refreshes the advanced page cache readout after a cache action.</summary>
        public void RefreshCacheSize()
        {
            if (_advancedPage != null)
            {
                _advancedPage.RefreshCacheSize();
            }
        }

        public void SetCoverPreview(
            FalsumConvertedCover cover,
            string sourcePath)
        {
            if (_metadataDraft == null || cover == null)
            {
                return;
            }
            _metadataDraft.CoverBytes = cover.Bytes;
            _metadataDraft.CoverWidth = cover.Width;
            _metadataDraft.CoverHeight = cover.Height;
            _metadataDraft.PendingCoverPath =
                sourcePath ?? string.Empty;
            ClearStatus();
            // Repaint the open chart-info panel: writing the draft bytes is
            // not enough, the preview element must be re-fed or the user keeps
            // seeing the previous cover until the panel is reopened.
            if (_chartInfoPanel != null && _chartInfoPanel.IsOpen)
            {
                _chartInfoPanel.SetCoverBytes(cover.Bytes);
            }
        }

        public void SetAudioPreview(
            FalsumConvertedAudio audio,
            string sourcePath)
        {
            if (_metadataDraft == null || audio == null)
            {
                return;
            }
            _metadataDraft.AudioBytes = audio.Bytes;
            _metadataDraft.AudioDurationMs = audio.DurationMs;
            _metadataDraft.PendingAudioPath =
                sourcePath ?? string.Empty;
            ClearStatus();
        }

        public void ShowBackgroundError(string message)
        {
            if (_backgroundPreview != null)
            {
                _backgroundPreview.ShowStatus(message);
                return;
            }
            if (_chartInfoPanel != null &&
                _chartInfoPanel.IsBackgroundPreviewOpen)
            {
                _chartInfoPanel.ShowBackgroundError(message);
                return;
            }
            ShowError(message);
        }

        public void ShowDisplayConfirmation(
            int seconds,
            Action keepAction,
            Action revertAction)
        {
            _displayConfirm.Show(
                seconds,
                keepAction,
                revertAction);
        }

        private void BindStaticControls(VisualElement instance)
        {
            L.Bind(_tabDisplay, "falsum.preferences.tab.display");
            L.Bind(_tabVolume, "falsum.preferences.tab.volume");
            L.Bind(_tabKeys, "falsum.preferences.tab.keys");
            L.Bind(_tabEditing, "falsum.preferences.tab.editing");
            L.Bind(_tabBackup, "falsum.preferences.tab.backup");
            L.Bind(_tabAdvanced, "falsum.preferences.tab.advanced");
            L.Bind(_tabAbout, "falsum.preferences.tab.about");
            L.Bind(
                Require<Button>(instance, "PreferencesResetDefaults"),
                "falsum.preferences.resetDefaults");
            L.Bind(
                Require<Button>(instance, "PreferencesCancel"),
                "falsum.common.cancel");
            L.Bind(_completeButton, "falsum.preferences.complete");
            L.BindTooltip(
                Require<Button>(instance, "PreferencesClose"),
                "falsum.preferences.closeTooltip");
            L.Bind(
                Require<Button>(instance, "BackgroundPreviewCancel"),
                "falsum.common.cancel");
            L.Bind(
                Require<Button>(instance, "BackgroundPreviewConfirm"),
                "falsum.preferences.background.confirm");
            L.BindTooltip(
                Require<Button>(instance, "BackgroundPreviewClose"),
                "falsum.common.close");
            L.Bind(
                Require<Button>(instance, "DisplayConfirmRevert"),
                "falsum.preferences.displayConfirm.revert");
            L.Bind(
                Require<Button>(instance, "DisplayConfirmKeep"),
                "falsum.preferences.displayConfirm.keep");

            _tabDisplay.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.Display);
            };
            _tabVolume.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.Volume);
            };
            _tabKeys.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.Keys);
            };
            _tabEditing.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.Editing);
            };
            _tabBackup.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.Backup);
            };
            _tabAdvanced.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.Advanced);
            };
            _tabAbout.clicked += delegate
            {
                ShowTab(FalsumPreferencesTab.About);
            };
            Require<Button>(instance, "PreferencesCancel")
                .clicked += RequestCancel;
            Require<Button>(instance, "PreferencesClose")
                .clicked += RequestCancel;
            _completeButton.clicked += RequestCommit;
            Require<Button>(
                instance,
                "PreferencesResetDefaults").clicked +=
                delegate
                {
                    _preferences =
                        FalsumPreferences.CreateDefault();
                    PreviewUiStyle();
                    ShowInfo(
                        L.Get(
                            "falsum.preferences.globalDefaultsRestored"));
                    ShowTab(_activeTab);
                };

            _overlay.RegisterCallback<KeyDownEvent>(
                OnOverlayKeyDown,
                TrickleDown.TrickleDown);
        }

        private void ShowTab(FalsumPreferencesTab tab)
        {
            if (_content == null || _preferences == null)
            {
                return;
            }
            if (_initialTabBuild != null)
            {
                _initialTabBuild.Pause();
                _initialTabBuild = null;
            }
            _activeTab = tab;
            if (tab != FalsumPreferencesTab.About)
            {
                DebugResetAboutVersionTaps();
            }
            SetTabActive(_tabDisplay, tab == FalsumPreferencesTab.Display);
            SetTabActive(_tabVolume, tab == FalsumPreferencesTab.Volume);
            SetTabActive(_tabKeys, tab == FalsumPreferencesTab.Keys);
            SetTabActive(_tabEditing, tab == FalsumPreferencesTab.Editing);
            SetTabActive(_tabBackup, tab == FalsumPreferencesTab.Backup);
            SetTabActive(_tabAdvanced, tab == FalsumPreferencesTab.Advanced);
            SetTabActive(_tabAbout, tab == FalsumPreferencesTab.About);
            _content.Clear();
            _keyCapture.Rebind(() => _preferences, ShowError, ClearStatus);
            ClearStatus();

            switch (tab)
            {
                case FalsumPreferencesTab.Display:
                    SetHeader(L.Get("falsum.preferences.tab.display"), L.Get("falsum.preferences.page.display.subtitle"));
                    BuildDisplayPage();
                    break;
                case FalsumPreferencesTab.Volume:
                    SetHeader(L.Get("falsum.preferences.tab.volume"), L.Get("falsum.preferences.page.volume.subtitle"));
                    BuildVolumePage();
                    break;
                case FalsumPreferencesTab.Keys:
                    SetHeader(L.Get("falsum.preferences.tab.keys"), L.Get("falsum.preferences.page.keys.subtitle"));
                    BuildKeysPage();
                    break;
                case FalsumPreferencesTab.Editing:
                    SetHeader(L.Get("falsum.preferences.tab.editing"), L.Get("falsum.preferences.page.editing.subtitle"));
                    BuildEditingPage();
                    break;
                case FalsumPreferencesTab.Backup:
                    SetHeader(L.Get("falsum.preferences.tab.backup"), L.Get("falsum.preferences.page.backup.subtitle"));
                    BuildBackupPage();
                    break;
                case FalsumPreferencesTab.Advanced:
                    SetHeader(L.Get("falsum.preferences.tab.advanced"), L.Get("falsum.preferences.page.advanced.subtitle"));
                    BuildAdvancedPage();
                    break;
                case FalsumPreferencesTab.About:
                    SetHeader(L.Get("falsum.preferences.tab.about"), L.Get("falsum.preferences.page.about.subtitle"));
                    BuildAboutPage();
                    break;
            }
        }

        private void ShowInitialLoading()
        {
            _activeTab = FalsumPreferencesTab.Display;
            SetTabActive(_tabDisplay, true);
            SetTabActive(_tabVolume, false);
            SetTabActive(_tabKeys, false);
            SetTabActive(_tabEditing, false);
            SetTabActive(_tabBackup, false);
            SetTabActive(_tabAdvanced, false);
            SetTabActive(_tabAbout, false);
            SetHeader(
                L.Get("falsum.preferences.tab.display"),
                L.Get("falsum.preferences.loading.subtitle"));
            _content.Clear();
            var loading = new Label(
                L.Get("falsum.preferences.loading.label"));
            loading.AddToClassList("preferences-empty");
            _content.Add(loading);
        }

        public void DebugFlushInitialTabBuild()
        {
            if (!_open || _content == null)
            {
                return;
            }
            if (_initialTabBuild != null)
            {
                _initialTabBuild.Pause();
                _initialTabBuild = null;
            }
            if (_metadataDraft != null &&
                !_metadataDraft.HasPackage)
            {
                ShowInfo(
                    L.Get(
                        "falsum.preferences.noPackageGlobalEditable"));
            }
        }

        /// <summary>
        /// Sets the page header. The dialog only hosts global preferences now
        /// that chart metadata moved to its own panel, so there is no
        /// document-scoped tab left and the former scope label and per-page
        /// footer note are gone with it.
        /// </summary>
        private void SetHeader(string title, string subtitle)
        {
            _title.text = title;
            _subtitle.text = subtitle;
        }

        private void BuildVolumePage()
        {
            FalsumVolumePreferencesPage.Build(_content, _preferences);
        }

        private void BuildDisplayPage()
        {
            FalsumDisplayPreferencesPage.Build(
                _content,
                _preferences,
                _pageHost);
        }

        private void BuildKeysPage()
        {
            FalsumInputPreferencesPage.Build(
                _content,
                _preferences,
                _pageHost);
        }

        private void BuildEditingPage()
        {
            _editingPage.Build(
                _content,
                _preferences,
                _pageHost);
        }

        private void BuildBackupPage()
        {
            FalsumBackupPreferencesPage.Build(
                _content,
                _preferences,
                _pageHost);
        }


        private void BuildAdvancedPage()
        {
            _advancedPage.Build(
                _content,
                _preferences,
                _pageHost);
        }

        /// <summary>
        /// Builds the About page on its own tab.
        ///
        /// Product information used to be appended to the Advanced page so a
        /// dedicated tab would not "hold nothing but read-only content"; the
        /// two are separate tabs again, because Advanced is cache
        /// housekeeping the user acts on while About is text the user reads.
        /// </summary>
        private void BuildAboutPage()
        {
            _aboutPage.Build(_content);
        }

        private void OnOverlayKeyDown(KeyDownEvent evt)
        {
            if (_displayConfirm != null &&
                _displayConfirm.IsOpen)
            {
                if (evt.keyCode == KeyCode.Tab)
                {
                    FalsumPreferencesUiHelpers.TrapFocus(
                        _displayConfirm.Root,
                        evt);
                    return;
                }
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopImmediatePropagation();
                    _displayConfirm.CancelByEscape();
                }
                return;
            }
            if (_keyCapture.HandleKeyDown(evt))
            {
                return;
            }


            if (!IsBackgroundPreviewOpen && NavigateTabs(evt))
            {
                return;
            }

            if (evt.keyCode == KeyCode.Tab)
            {
                // The chart-info shell owns the picker while it is open and
                // traps Tab itself; only the preferences shell needs this.
                var trapRoot = (_backgroundPreview != null &&
                                _backgroundPreview.IsOpen)
                    ? _backgroundPreview.Root
                    : _window;
                if (trapRoot != null)
                {
                    FalsumPreferencesUiHelpers.TrapFocus(trapRoot, evt);
                }
                return;
            }

            if (evt.keyCode == KeyCode.Escape)
            {
                if (IsBackgroundPreviewOpen)
                {
                    evt.StopPropagation();
                    if (_backgroundPreview != null &&
                        _backgroundPreview.IsOpen)
                    {
                        _backgroundPreview.Cancel();
                    }
                    else if (_chartInfoPanel != null)
                    {
                        _chartInfoPanel.CancelBackgroundPreview();
                    }
                }
                else
                {
                    evt.StopPropagation();
                    RequestCancel();
                }
            }
        }

        private bool NavigateTabs(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.LeftArrow &&
                evt.keyCode != KeyCode.RightArrow)
            {
                return false;
            }
            var tabs = new[]
            {
                _tabDisplay,
                _tabVolume,
                _tabKeys,
                _tabEditing,
                _tabBackup,
                _tabAdvanced,
                _tabAbout,
            };
            var focused = _overlay != null && _overlay.panel != null
                ? _overlay.panel.focusController.focusedElement as Button
                : null;
            var current = Array.IndexOf(tabs, focused);
            if (current < 0)
            {
                return false;
            }
            var direction =
                evt.keyCode == KeyCode.RightArrow ? 1 : -1;
            var next = (current + direction + tabs.Length) %
                       tabs.Length;
            tabs[next].Focus();
            evt.StopImmediatePropagation();
            return true;
        }

        private void RequestCommit()
        {
            if (IsCapturingKey)
            {
                ShowError(
                    L.Get(
                        "falsum.error.preferences.finishKeyCapture"));
                return;
            }
            if (_editingPage.GridColorsInvalid)
            {
                ShowError(
                    L.Get(
                        "falsum.error.preferences.fixGridColors"));
                return;
            }
            if (_editingPage.TimeSubdivisionInvalid)
            {
                ShowError(L.Get("falsum.error.preferences.fixTimeSubdivision"));
                return;
            }
            if (_editingPage.DenominatorSequenceInvalid)
            {
                ShowError(
                    L.Get(
                        "falsum.error.preferences.fixGridSequence"));
                return;
            }
            if (_preferences.HasBindingConflicts())
            {
                ShowError(
                    L.Get(
                        "falsum.error.preferences.duplicateShortcuts"));
                return;
            }
            if (_preferences.Display.AntiAliasingEnabled)
            {
                string unsupportedReason;
                if (!FalsumDisplaySettingsController.IsSupported(
                        _preferences.Display.AntiAliasingMode,
                        out unsupportedReason))
                {
                    ShowError(
                        L.Get(
                            "falsum.error.preferences.antiAliasingUnavailable",
                            ("reason", unsupportedReason)));
                    return;
                }
            }
            if (_metadataDraft != null &&
                _metadataDraft.HasPackage)
            {
                if (_preferences == null)
                {
                    return;
                }
                string error;
                if (!_metadataDraft.TryValidate(out error))
                {
                    ShowError(error);
                    return;
                }
            }
            ClearStatus();
            var commit = CommitRequested;
            if (commit != null)
            {
                commit(
                    _preferences,
                    _metadataDraft,
                    _package);
            }
        }

        private void RequestCancel()
        {
            if (IsCapturingKey)
            {
                _keyCapture.Cancel();
                return;
            }
            if (IsBackgroundPreviewOpen)
            {
                if (_backgroundPreview != null &&
                    _backgroundPreview.IsOpen)
                {
                    _backgroundPreview.Cancel();
                }
                else if (_chartInfoPanel != null &&
                         _chartInfoPanel.IsBackgroundPreviewOpen)
                {
                    _chartInfoPanel.CancelBackgroundPreview();
                }
                return;
            }
            var cancel = CancelRequested;
            if (cancel == null)
            {
                RestoreUiStyleOnOpen();
            }
            if (cancel != null)
            {
                cancel();
            }
        }

        private void PreviewUiStyle()
        {
            var preview = UiStylePreviewRequested;
            if (preview != null)
            {
                preview(_preferences.Advanced.UIStyle);
            }
        }

        /// <summary>
        /// Host hook the developer toggle calls. Stored on the draft too so
        /// the choice survives the dialog (and a restart).
        /// </summary>
        internal void PreviewMobileScaleForPages(float scale)
        {
            if (_preferences == null)
            {
                return;
            }
            _preferences.Experimental.MobileUiScale =
                FalsumMobileScaleBounds.Clamp(scale);
            var preview = MobileScalePreviewRequested;
            if (preview != null)
            {
                preview(_preferences.Experimental.MobileUiScale);
            }
        }

        internal void PreviewMobileLayoutForPages(bool mobile)
        {
            if (_preferences == null)
            {
                return;
            }
            _preferences.Experimental.ForceMobileLayout = mobile;
            var preview = MobileLayoutPreviewRequested;
            if (preview != null)
            {
                preview(mobile);
            }
        }

        private void RestoreUiStyleOnOpen()
        {
            var preview = UiStylePreviewRequested;
            if (preview != null)
            {
                preview(_styleOnOpen);
            }
        }


        /// <summary>
        /// Opens the gameplay-background picker for the current chart draft.
        /// The panel reports selections back through ApplyBackgroundSelection.
        /// </summary>
        private void EnterBackgroundPreview()
        {
            if (_metadataDraft == null ||
                !_metadataDraft.HasPackage)
            {
                ShowError(
                    L.Get("falsum.error.metadata.packageRequired"));
                return;
            }
            // The picker belongs to whichever shell is on screen: the
            // preferences dialog builds one in Show(), and the chart-info
            // panel builds its own against its own UXML instance. Before
            // this only the preferences dialog had one, so opening the
            // picker from the chart-info page threw a NullReferenceException.
            if (_backgroundPreview != null)
            {
                _backgroundPreview.Show(_metadataDraft.BackgroundFile);
            }
            else if (_chartInfoPanel != null &&
                     _chartInfoPanel.IsOpen)
            {
                _chartInfoPanel.OpenBackgroundPreview(
                    _metadataDraft.BackgroundFile);
            }
            else
            {
                ShowError(
                    L.Get("falsum.error.metadata.packageRequired"));
                return;
            }
            var entered = BackgroundPreviewEntered;
            if (entered != null)
            {
                entered();
            }
        }

        /// <summary>Writes the confirmed/restored background into the draft.</summary>
        private void ApplyBackgroundSelection(string fileName)
        {
            if (_metadataDraft != null)
            {
                _metadataDraft.BackgroundFile = fileName;
            }
            var changed = BackgroundPreviewChanged;
            if (changed != null)
            {
                changed(fileName);
            }
        }

        /// <summary>
        /// Raised by the panel whenever it closes (confirm, cancel or teardown),
        /// so the editor can restore its editing state exactly once.
        /// </summary>
        private void OnBackgroundPreviewExited()
        {
            var exited = BackgroundPreviewExited;
            if (exited != null)
            {
                exited();
            }
        }

        private void CloneSharedCoverTexture(
            Texture2D source,
            byte[] fallbackBytes)
        {
            ReleaseCoverTexture();
            if (source == null)
            {
                SetCoverTexture(fallbackBytes);
                return;
            }
            Texture2D copy = null;
            try
            {
                copy = new Texture2D(
                    source.width,
                    source.height,
                    source.format,
                    source.mipmapCount > 1);
                Graphics.CopyTexture(source, copy);
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = source.filterMode;
                _coverTexture = copy;
                if (_coverPreview != null)
                {
                    _coverPreview.style.backgroundImage =
                        new StyleBackground(copy);
                }
            }
            catch (Exception)
            {
                if (copy != null)
                {
                    FalsumPlatform.DestroyObject(copy);
                }
                SetCoverTexture(fallbackBytes);
            }
        }

        private void SetCoverTexture(byte[] bytes)
        {
            ReleaseCoverTexture();
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                DestroyTexture(ref texture);
                return;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            _coverTexture = texture;
            if (_coverPreview != null)
            {
                _coverPreview.style.backgroundImage =
                    new StyleBackground(texture);
            }
        }

        private void ReleaseCoverTexture()
        {
            // The texture is always a private copy: CloneSharedCoverTexture
            // copies the song-card texture instead of aliasing it. The former
            // "shared" flag was never assigned true anywhere and its branch was
            // dead, so the destroy is now unconditional.
            DestroyTexture(ref _coverTexture);
        }

        private static void SetTabActive(
            Button button,
            bool active)
        {
            if (button != null)
            {
                button.EnableInClassList("is-active", active);
            }
        }

        private static void SetVisible(
            VisualElement element,
            bool visible)
        {
            FalsumPreferencesUiHelpers.SetVisible(element, visible);
        }

        private static void TrapFocus(
            VisualElement root,
            KeyDownEvent evt)
        {
            FalsumPreferencesUiHelpers.TrapFocus(root, evt);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            FalsumPlatform.DestroyObject(texture);
            texture = null;
        }

        private static T Require<T>(VisualElement root, string name)
            where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.ui.missingPreferencesElement",
                        ("element", name)));
            }
            return element;
        }

        /// <summary>
        /// Returns the nav button <paramref name="name"/>, creating it right
        /// after <paramref name="anchor"/> first if the template does not
        /// carry one yet. Looked up by name rather than assumed missing so a
        /// template that gains the button later does not end up with two.
        /// </summary>
        private static Button EnsureTabButton(
            VisualElement instance,
            Button anchor,
            string name)
        {
            var existing = instance.Q<Button>(name);
            if (existing != null)
            {
                return existing;
            }
            var button = new Button
            {
                name = name,
            };
            button.AddToClassList("preferences-tab");
            var nav = anchor != null ? anchor.parent : null;
            if (nav == null)
            {
                return button;
            }
            // Append rather than prepend when the anchor is not reachable
            // through its own parent: inserting at -1 + 1 would put About
            // ahead of every other tab.
            var index = nav.IndexOf(anchor);
            nav.Insert(index >= 0 ? index + 1 : nav.childCount, button);
            return button;
        }

        private static void Raise(Action action)
        {
            if (action != null)
            {
                action();
            }
        }

        private static void Raise<T>(Action<T> action, T value)
        {
            if (action != null)
            {
                action(value);
            }
        }
    }
}
