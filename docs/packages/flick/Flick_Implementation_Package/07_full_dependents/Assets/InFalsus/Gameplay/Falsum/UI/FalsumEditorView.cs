using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Localization;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.Tools;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public sealed class FalsumEventEditSubmission
    {
        public ChartEvent Target;
        public string Time;
        public string Bpm;
        public string Division;
        public string Multiplier;
        public int Lane;
        public bool Enabled;
    }

    public sealed class FalsumEditorView
    {
        private const string MainUxmlPath = "Falsum/UI/FalsumEditor";
        private const string PanelSettingsPath =
            "Falsum/UI/FalsumPanelSettings";
        private const string ThemePath = "Falsum/UI/FalsumRuntimeTheme";
        private const string PropertyRowPath =
            "Falsum/UI/FalsumPropertyRow";
        private const string MenuPanelPath =
            "Falsum/UI/FalsumMenuPanel";
        private const string MenuItemPath =
            "Falsum/UI/FalsumMenuItem";
        private const string MenuSeparatorPath =
            "Falsum/UI/FalsumMenuSeparator";
        private const string ConfirmDialogPath =
            "Falsum/UI/FalsumConfirmDialog";
        private const string DisclaimerDialogPath =
            "Falsum/UI/FalsumDisclaimerDialog";
        private const string NewChartDialogPath =
            "Falsum/UI/FalsumNewChartDialog";
        private const string PreferencesDialogPath =
            "Falsum/UI/FalsumPreferencesDialog";
        private const string ToolDialogPath =
            "Falsum/UI/FalsumToolDialog";
        private const string ToolReportDialogPath =
            "Falsum/UI/FalsumToolReportDialog";
        private const string AudioPreprocessDialogPath =
            "Falsum/UI/FalsumAudioPreprocessDialog";
        private const string SkyAreaBankPath =
            "Falsum/UI/FalsumSkyAreaBank";
        private const string SongInfoCardBackdropPathPrefix =
            "Falsum/UI/SongInfoCard";
        private const string DefaultCoverPath =
            "Falsum/DefaultCover";
        private const float SongInfoCardDesignWidth = 3013.0f;
        private const float SongInfoCardDesignHeight = 559.0f;
        private const float SongInfoCardWidthRatio = 0.32175f;
        private const float SongInfoCardRightGap = 24.0f;
        private const float SongInfoCardTopGap = 18.0f;
        private const float EventsPanelFallbackWidth = 432.0f;
        private const float EventsPanelAnimationSeconds = 0.22f;
        private const float ChartPreviewChromeAnimationSeconds = 0.24f;
        private const float ChartPreviewTrackScaleMultiplier =
            1.157f * 0.95f;
        private static readonly Color TransportIconTint =
            new Color(0.902f, 0.918f, 0.925f, 1.0f);
        private static readonly Color PlayIconTint =
            new Color(0.043f, 0.051f, 0.059f, 1.0f);
        private static readonly Color PauseIconTint =
            new Color(0.490f, 0.718f, 0.949f, 1.0f);
        private static readonly Color StudioPlayIconTint =
            new Color(0.043f, 0.075f, 0.063f, 1.0f);
        private static readonly Color StudioPauseIconTint =
            new Color(0.635f, 0.867f, 0.788f, 1.0f);
        private static readonly Color CanvasPlayIconTint =
            new Color(0.106f, 0.082f, 0.043f, 1.0f);
        private static readonly Color CanvasPauseIconTint =
            new Color(0.922f, 0.827f, 0.620f, 1.0f);

        private static readonly string[] SongInfoCardBackdropNames =
        {
            "MIN",
            "EVO",
            "ULT",
            "FBD",
        };
        private const int CompactScreenWidth = 1500;
        private const int CompactScreenHeight = 840;
        private const int NarrowScreenWidth = 900;
        private const int ShortScreenHeight = 700;
        private const float LowScaleThreshold = 0.82f;

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        private sealed class PropertyRow
        {
            public string Key;
            public VisualElement Root;
            public TextField Field;
            public Button Button;
        }

        private sealed class MenuEntry
        {
            public string Label;
            public bool Separator;
            public bool Checked;
            public bool Hidden;
            public bool Enabled = true;
            public string Shortcut;
            public Action Action;
        }

        private enum EventFilter
        {
            All,
            Bpm,
            Track,
            Lane,
        }

        private sealed class EventLaneSelection
        {
            public int Value;
            public readonly List<Button> Buttons =
                new List<Button>();
        }

        private sealed class EventStatusSelection
        {
            public bool Value;
            public readonly List<Button> Buttons =
                new List<Button>();
        }

        private readonly Transform _host;
        private readonly List<PropertyRow> _propertyRows =
            new List<PropertyRow>();
        private readonly List<ChartEvent> _events =
            new List<ChartEvent>();
        private readonly List<VisualElement> _eventRows =
            new List<VisualElement>();
        private readonly List<Button> _menuButtons =
            new List<Button>();

        private UIDocument _document;
        private VisualElement _root;

        // One-entry cache for IsPointerOverUi: the question is asked several
        // times per frame and Panel.Pick walks the whole tree. See the method.
        private bool _pointerOverUiValid;
        private int _pointerOverUiFrame;
        private int _pointerOverUiX;
        private int _pointerOverUiY;
        private bool _pointerOverUiResult;
        private VisualElement _falsumRoot;
        private VisualElement _topBar;
        private VisualElement _bottomBar;
        private VisualElement _propertyPanel;
        private VisualElement _eventsPanel;
        private ScrollView _eventRowsView;
        private VisualElement _eventAddMenu;
        private Label _eventCount;
        private Label _eventValidation;
        private ScrollView _propertyRowsView;
        private VisualElement _hintPanel;
        private VisualElement _middleEditStatus;
        private Label _middleEditModeLabel;
        // The tutorial text and the alternative-mode listing are no longer
        // rendered in the top bar: the labels stay in the tree (the self-check
        // asserts their existence) but are hidden and emptied. The runtime's
        // values are kept here so DebugMiddleEditStatus can still report them.
        private Label _middleEditModeHint;
        private Label _middleEditModeAlternatives;
        private string _middleEditHintText = string.Empty;
        private string _middleEditAlternativesText = string.Empty;
        private VisualElement _menuLayer;
        private VisualElement _bankLayer;
        private VisualElement _modalLayer;
        private VisualElement _radialLayer;
        private VisualElement _songInfoCardViewport;
        private Image _songInfoCardBackdrop;
        private VisualElement _songInfoCard;
        private VisualElement _songInfoCardCover;
        private TextField _songInfoCardTitle;
        private TextField _songInfoCardArtist;
        private TextField _songInfoCardRating;
        private TextField _songInfoCardChartDesigner;
        private Button _songInfoCardDifficultyHotspot;
        private VisualElement _songInfoCardDifficultyMenu;
        private VisualElement _songInfoCardDifficultyOptions;
        private VisualElement _songInfoCardProgressFill;
        private Texture2D _songInfoCardCoverTexture;
        private float _songInfoCardProgress = -1.0f;
        private int _songInfoCardDifficultyIndex = -1;
        private readonly List<int> _songInfoCardAvailableDifficulties =
            new List<int>();
        private readonly bool[] _songInfoCardDifficultyEnabled =
            new bool[4];
        private Button _fileMenuButton;
        private Button _editMenuButton;
        private Button _viewMenuButton;
        private Button _toolMenuButton;
        private Button _skyAreaBankMenuButton;
        private Slider _speedSlider;
        private Slider _timelineSlider;
        private VisualElement _speedFill;
        private VisualElement _timelineFill;
        private TextField _speedInput;
        private Button _audioPreprocessButton;
        private FalsumAudioPreprocessView _audioPreprocessView;
        private TextField _baseBpmInput;
        private TextField _subdivisionInput;
        private TextField _timeInput;
        private Label _undoText;
        private Label _redoText;
        private Label _hintText;
        private bool _textInputFocusGrantedByPointer;
        private Label _propertyHeader;
        private Label _progressText;
        private Button _playPauseButton;
        private Image _undoIcon;
        private Image _redoIcon;
        private Image _playPauseIcon;
        private VectorImage _playVectorImage;
        private VectorImage _pauseVectorImage;
        private VectorImage _spectrumVectorImage;
        private Button _undoButton;
        private Button _redoButton;
        private Button _spectrumButton;
        private Button _rateButton;
        private Button _jumpToNoteButton;
        private Button _eventsButton;
        private Button _eventFilterAll;
        private Button _eventFilterBpm;
        private Button _eventFilterTrack;
        private Button _eventFilterLane;
        private Button _addEventButton;
        private Button _addLaneEventButton;
        private Font _uiFont;
        private Font _displayFont;
        private double _timelineDurationMs;
        private double _lastTimelineSliderValue = double.NaN;
        private VisualElement _timelineDragContainer;
        private VisualElement _timelineDragger;
        private float _timelineThumbLeft = float.NaN;
        private float _timelineThumbRange = float.NaN;
        private float _timelineThumbTrackWidth = float.NaN;
        private float _timelineThumbWidth = float.NaN;
        private bool _timelineSliderValueStale;
        private double _lastDisplayedTimeMs = double.NaN;
        private double _lastDisplayedDurationMs = double.NaN;
        private int _displayedUndoCount = -1;
        private int _displayedRedoCount = -1;
        private double _displayedPlaybackRate = double.NaN;
        private float _displayedSpeed = float.NaN;
        private double _displayedDelayMs = double.NaN;
        private double _displayedBaseBpm = double.NaN;
        private int _displayedSubdivision = int.MinValue;
        private bool _displayedPlaying;
        private bool _hasDisplayedPlaying;
        private string _displayedHint;
        private bool _displayedHintVisible;
        private bool _updatingTimeline;
        private int _lastImePanelX = int.MinValue;
        private int _lastImePanelY = int.MinValue;
        private float _nextImeSyncAt;
        private string _activeMenu;
        private bool _menuSwitchedOnHover;
        private int _propertyRefreshCount;
        private VisualElement _menuPanel;
        private VisualElement _menuDismissLayer;
        private VisualElement _easeDropdown;
        private VisualElement _modal;
        private VisualTreeAsset _propertyRowTemplate;
        private VisualTreeAsset _menuPanelTemplate;
        private VisualTreeAsset _menuItemTemplate;
        private VisualTreeAsset _menuSeparatorTemplate;
        private VisualTreeAsset _confirmDialogTemplate;
        private VisualTreeAsset _disclaimerDialogTemplate;
        private VisualTreeAsset _newChartDialogTemplate;
        private VisualTreeAsset _preferencesDialogTemplate;
        private VisualTreeAsset _toolDialogTemplate;
        private VisualTreeAsset _toolReportDialogTemplate;
        private VisualTreeAsset _audioPreprocessDialogTemplate;
        private VisualTreeAsset _skyAreaBankTemplate;
        private FalsumSkyAreaBankView _skyAreaBankView;
        private FalsumPreferencesView _preferencesView;
        private FalsumUiStyle _uiStyle = FalsumUiStyle.InFalsus;
        private FalsumPerformanceMonitor _performanceMonitor;
        private FalsumPerformanceWindow _performanceWindow;
        private FalsumToolDialogView _toolDialog;
        private FalsumToolReportView _toolReportView;
        private FalsumPreferences _shortcutPreferences;
        private readonly List<FalsumToolManifest> _toolCatalog =
            new List<FalsumToolManifest>();
        private VisualElement _newChartOverlay;
        private VisualElement _newChartPanel;
        private VisualElement _newChartCoverPreview;
        private VisualElement _newChartBusy;
        private Label _newChartCoverPrompt;
        private Label _newChartAudioName;
        private Label _newChartCoverName;
        private Label _newChartDifficultyIndex;
        private Label _newChartChartFile;
        private Label _newChartValidation;
        private Label _newChartBusyText;
        private VisualElement _newChartDifficultyFields;
        private TextField _newChartSongId;
        private TextField _newChartTitle;
        private TextField _newChartArtist;
        private TextField _newChartJacketDesigner;
        private TextField _newChartRating;
        private TextField _newChartChartDesigner;
        private Button _newChartBrowseAudio;
        private Button _newChartBrowseCover;
        private Button _newChartCreate;
        private Button _newChartCancel;
        private Button _newChartClose;
        private VisualElement _newChartDiscardConfirm;
        private Button _newChartDiscardContinue;
        private Button _newChartDiscardConfirmButton;
        private Button[] _newChartDifficultyButtons;
        private Toggle[] _newChartDifficultyToggles;
        private VisualElement[] _newChartDifficultyTabs;
        private Label[] _newChartDifficultyStates;
        private Texture2D _newChartPreviewTexture;
        private readonly bool[] _newChartDifficultyEnabled =
            new bool[4];
        private readonly string[] _newChartRatings =
        {
            "1",
            "5",
            "9",
            "11",
        };
        private readonly string[] _newChartDesigners =
        {
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
        };
        private int _newChartActiveDifficulty;
        private string _newChartTargetPath;
        private string _newChartAudioPath;
        private string _newChartCoverPath;
        private bool _newChartUpdating;
        private bool _newChartBusyState;
        private bool _newChartDirty;
        private bool _newChartClosing;
        private bool _selectGroundOnly;
        private bool _selectSkyOnly;
        private FalsumChartLayer _layerVisibility =
            FalsumChartLayer.Both;
        private ChartEvent _selectedEvent;
        private bool _focusEventRowOnNextRender;
        private EventFilter _eventFilter;
        private bool _laneEventsEnabled = true;
        private int _eventRefreshCount;
        private bool _eventsPanelOpen;
        private float _eventPanelOffset;
        private float _eventPanelAnimationStart;
        private float _eventPanelAnimationFrom;
        private float _eventPanelAnimationTo;
        private bool _eventPanelAnimatingOpen;
        private IVisualElementScheduledItem _eventPanelAnimation;
        private VisualElement _menuFocusSource;
        private VisualElement _newChartFocusBefore;
        private VisualElement _newChartDiscardFocusBefore;
        private VisualElement _confirmFocusBefore;
        private VisualElement _preferencesFocusBefore;
        private VisualElement _disclaimerOverlay;
        private VisualElement _disclaimerPanel;
        private VisualElement _disclaimerFocusBefore;
        private Action _confirmCancelAction;
        private Action _disclaimerAccepted;
        private Action _disclaimerDeclined;
        private VisualElement _confirmPanel;
        private bool _confirmClosing;
        private bool _disclaimerMandatory;
        private bool _disclaimerClosing;
        private bool _gridEnabled = true;
        private bool _measureNumbersEnabled = true;
        private bool _trackSpeedEffectsEnabled = true;
        private bool _chartPreviewMode;
        private bool _chartPreviewTransitionActive;
        private bool _chartPreviewTransitionToHidden;
        private float _chartPreviewTransitionStart;
        private float _chartPreviewTopFrom;
        private float _chartPreviewTopTo;
        private float _chartPreviewBottomFrom;
        private float _chartPreviewBottomTo;
        private float _chartPreviewTopOffset;
        private float _chartPreviewBottomOffset;
        private float _chartPreviewTopHiddenOffset;
        private float _chartPreviewBottomHiddenOffset;
        private float _chartPreviewCollapse;
        private float _chartPreviewCollapseFrom;
        private float _chartPreviewCollapseTo;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;

        /// <summary>
        /// The mobile shell, or null on desktop. It owns everything that
        /// makes the same UXML usable on a landscape phone; this view only
        /// forwards its events onto the ordinary editor events.
        /// </summary>
        private FalsumMobileShell _mobileShell;
        private PanelSettings _mobilePanelSettings;
        private VisualElement _mobileMenuPanel;
        private VisualElement _mobileNumericOverlay;
        private VisualElement _mobileEventsOverlay;
        private VisualElement _desktopEventsParent;
        private int _desktopEventsIndex;
        private int _mobileDismissFrame = -1;

        public event Action OpenFileRequested;
        public event Action NewFileRequested;
        public event Action OpenPreferencesRequested;
        public event Action DeleteSelectionClicked;

        /// <summary>
        /// Raised when the song-info cover is clicked. Chart metadata is
        /// document scope, so this opens that chart's info panel rather than
        /// the global preferences dialog.
        /// </summary>
        public event Action ChartInfoRequested;
        public event Action ToolManagerRequested;
        public event Action<string> ToolOpenRequested;
        public event Action<FalsumToolReportItem>
            ToolReportLocateRequested;
        public event Action PlayPauseClicked;
        public event Action SaveClicked;
        public event Action SaveAsClicked;
        public event Action RestoreBackupRequested;
        public event Action OpenBackupFolderRequested;
        public event Action ExitRequested;
        public event Action UndoClicked;
        public event Action RedoClicked;
        public event Action ClearHistoryClicked;
        public event Action CopyRequested;
        public event Action PasteRequested;
        public event Action CutRequested;
        public event Action MirrorRequested;
        public event Action MirrorFrontBackRequested;
        public event Action ToggleFullscreenRequested;
        public event Action ChartPreviewRequested;
        public event Action<FalsumChartLayer>
            ChartLayerVisibilityRequested;
        public event Action<bool> GridVisibilityRequested;
        public event Action<bool> MeasureNumberVisibilityRequested;
        /// <summary>
        /// True enables the chart's track() effects in the preview (the
        /// default); false previews a constant 1.00 scroll while the events
        /// themselves stay untouched in the chart data.
        /// </summary>
        public event Action<bool> TrackSpeedEffectsRequested;
        public event Action<bool, bool> SelectionFilterRequested;
        public event Action BrowseNewChartAudioRequested;
        public event Action BrowseNewChartCoverRequested;
        public event Action<FalsumNewChartRequest>
            NewChartCreateRequested;
        public event Action<float> SpeedChanged;
        public event Action<string> SpeedSubmitted;
        public event Action<string> BaseBpmSubmitted;
        public event Action<string> SubdivisionSubmitted;
        public event Action<string> TimeSubmitted;
        public event Action<float> TimelineChanged;
        public event Action SpectrumToggleRequested;
        public event Action AudioPreprocessRequested;
        public event Action RateClicked;
        public event Action<string> SongTitleSubmitted;
        public event Action<string> SongArtistSubmitted;
        public event Action<string> SongRatingSubmitted;
        public event Action<string> SongChartDesignerSubmitted;
        public event Action<int> DifficultySelected;
        public event Action<string, string> PropertySubmitted;
        public event Action JumpToNoteClicked;
        public event Action<bool> EventsPanelVisibilityChanged;
        public event Action<ChartEventKind> EventAddRequested;
        public event Action<FalsumEventEditSubmission> EventSubmitted;
        public event Action<ChartEvent> EventDeleteRequested;
        public event Action<ChartEvent> EventDuplicateRequested;
        public event Action<ChartEvent> EventJumpRequested;
        public event Action<bool> BankPanelVisibilityChanged;
        public event Action<string, IList<string>> BankSaveSelectionRequested;
        public event Action<FalsumSkyAreaPreset> BankPlacePresetRequested;
        public event Action<FalsumSkyAreaPreset> BankDeletePresetRequested;
        public event Action<FalsumSkyAreaPreset, string>
            BankRenamePresetRequested;
        public event Action<FalsumSkyAreaPreset, double, double>
            BankPresetScalesRequested;
        public event Action<FalsumSkyAreaPreset> BankCopyCodeRequested;
        public event Action<FalsumSkyAreaPreset> BankExportFileRequested;
        public event Action<string> BankImportCodeRequested;
        public event Action BankImportFileTextRequested;
        public event Action BankImportFileRequested;
        public event Action BankToggleRequested;

        public VisualElement Root
        {
            get { return _root; }
        }

        public VisualElement RadialLayer
        {
            get { return _radialLayer; }
        }

        /// <summary>
        /// The mobile shell has no editor knowledge: it raises plain
        /// delegates and this view forwards them onto the SAME events the
        /// desktop chrome raises. Adding a parallel event channel would
        /// double the wiring contract that 02-invariants.md §C-4 protects.
        /// </summary>
        /// <summary>
        /// The mobile shell has no editor knowledge: it raises plain
        /// delegates and this view forwards them onto the SAME events the
        /// desktop chrome raises. A parallel event channel would double the
        /// wiring contract 02-invariants.md §C-4 protects.
        ///
        /// Every one of these already had a desktop trigger (a menu item, a
        /// bar button, a keyboard shortcut). The phone layout is a second
        /// face on the same editor, not a second editor.
        /// </summary>
        private void WireMobileShell()
        {
            _mobileShell.MenuRequested += delegate { ToggleMenuFromMobile(); };
            _mobileShell.FlowChanged += delegate(float value)
            {
                Raise(SpeedChanged, value);
            };
            _mobileShell.UndoRequested += delegate { Raise(UndoClicked); };
            _mobileShell.RedoRequested += delegate { Raise(RedoClicked); };
            _mobileShell.PlacementRequested +=
                delegate(FalsumRadialAction action)
                {
                    var handler = MobilePlacementRequested;
                    if (handler != null)
                    {
                        handler(action);
                    }
                };
            _mobileShell.ExitPlacementRequested += delegate
            {
                var handler = MobileExitPlacementRequested;
                if (handler != null)
                {
                    handler();
                }
            };
            _mobileShell.SegmentSelectRequested += delegate
            {
                var handler = MobileSegmentSelectRequested;
                if (handler != null)
                {
                    handler();
                }
            };
            _mobileShell.MultiSelectChanged += delegate(bool active)
            {
                SetHint(
                    active
                        ? L.Get("falsum.mobile.multiSelect")
                        : string.Empty);
            };
            _mobileShell.MirrorRequested += delegate
            {
                Raise(MirrorRequested);
            };
            _mobileShell.GridToggleRequested += delegate
            {
                _gridEnabled = !_gridEnabled;
                _mobileShell.SetGridEnabled(_gridEnabled);
                Raise(GridVisibilityRequested, _gridEnabled);
            };
            _mobileShell.BankToggleRequested += delegate
            {
                Raise(BankToggleRequested);
            };
            _mobileShell.CopyRequested += delegate
            {
                Raise(CopyRequested);
            };
            _mobileShell.PasteRequested += delegate
            {
                Raise(PasteRequested);
            };
            _mobileShell.DeleteRequested += delegate
            {
                Raise(DeleteSelectionClicked);
            };
            _mobileShell.EventsToggleRequested += delegate
            {
                SetEventsPanelOpen(!IsEventsPanelOpen);
            };
            _mobileShell.RateRequested += delegate { Raise(RateClicked); };
            _mobileShell.PlayPauseRequested += delegate
            {
                Raise(PlayPauseClicked);
            };
            _mobileShell.AuditionRequested += delegate
            {
                var handler = MobileAuditionRequested;
                if (handler != null)
                {
                    handler();
                }
            };
            _mobileShell.AudioPreprocessRequested += delegate
            {
                Raise(AudioPreprocessRequested);
            };
            _mobileShell.ProgressChanged += delegate(float value)
            {
                Raise(TimelineChanged, value);
            };
            _mobileShell.BaseBpmRequested +=
                value => Raise(BaseBpmSubmitted, value);
            _mobileShell.SubdivisionRequested +=
                value => Raise(SubdivisionSubmitted, value);
            _mobileShell.TimeFieldRequested +=
                value => Raise(TimeSubmitted, value);
            _mobileShell.SetGridEnabled(_gridEnabled);
            _mobileShell.SetGridSubdivision(_displayedSubdivision > 0 ? _displayedSubdivision : 4);
            BindMobileNumericInput(_root.Q<TextField>("MobileBaseBpm"), L.Get("falsum.mobile.bpm"), false);
            BindMobileNumericInput(_root.Q<TextField>("MobileSubdivision"), L.Get("falsum.mobile.subdivision"), false);
            BindMobileNumericInput(_root.Q<TextField>("MobileTimeField"), L.Get("falsum.property.time"), false);
            ConfigureMobileEventsWindow();
        }

        /// <summary>
        /// Opens the phone menu: EVERY entry the desktop top and bottom bars
        /// hold, in one full-screen list.
        ///
        /// WHY A SINGLE LIST: the phone layout has no permanent bars, so
        /// this is the only path to New/Open/Save/Preferences/preview/
        /// difficulty and the rest. Reusing the desktop menu builders means
        /// the two can never drift -- a new File entry appears on both.
        /// </summary>
        private void ToggleMenuFromMobile()
        {
            CloseMobileNumericEntry();
            var panel = _mobileMenuPanel;
            if (panel == null)
            {
                panel = new VisualElement
                {
                    name = "MobileMenuPanel",
                };
                _mobileMenuPanel = panel;
                panel.AddToClassList("mobile-menu-panel");
                panel.AddToClassList("hidden");
                var card = new VisualElement();
                card.AddToClassList("mobile-menu-scroll");
                var header = new VisualElement();
                header.AddToClassList("mobile-menu-header");
                var title = new Label(L.Get("falsum.mobile.menu")) { name = "MobileMenuTitle" };
                title.AddToClassList("mobile-menu-title");
                header.Add(title);
                var close = new Button { text = "×" };
                L.BindTooltip(close, "falsum.mobile.cancel");
                close.AddToClassList("mobile-menu-close");
                close.clicked += delegate
                {
                    panel.AddToClassList("hidden");
                };
                header.Add(close);
                card.Add(header);
                var columns = new VisualElement { name = "MobileMenuColumns" };
                columns.AddToClassList("mobile-menu-columns");
                card.Add(columns);
                panel.Add(card);
                _menuLayer.Add(panel);
                // Tapping outside closes it; the panel itself swallows the
                // tap so a button press does not dismiss before it fires.
                // Tap the scrim to dismiss, but a tap that lands on the card
                // must not close the menu before its button fires.
                panel.RegisterCallback<PointerDownEvent>(
                    evt =>
                    {
                        if (evt.target == panel)
                        {
                            panel.AddToClassList("hidden");
                        }
                        evt.StopPropagation();
                    });
                panel.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Escape) return;
                    panel.AddToClassList("hidden");
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
            }
            var visible = !panel.ClassListContains("hidden");
            if (visible)
            {
                panel.AddToClassList("hidden");
                return;
            }
            var content = panel.Q<VisualElement>("MobileMenuColumns");
            if (content != null)
            {
                content.Clear();
                BuildMobileMenuEntries(content);
            }
            panel.RemoveFromClassList("hidden");
            panel.Q<Label>("MobileMenuTitle").text = L.Get("falsum.mobile.menu");
            panel.BringToFront();
        }

        /// <summary>
        /// One flat list, grouped by origin. Order matters: the destructive
        /// and the occasional actions (Exit, restore) sit at the bottom, out
        /// of thumb reach, so they cannot be hit while aiming for Undo.
        /// </summary>
        private void BuildMobileMenuEntries(VisualElement list)
        {
            AddMobileMenuGroup(list, "falsum.menu.file", CreateFileMenuEntries());
            AddMobileMenuGroup(list, "falsum.menu.edit", CreateEditMenuEntries());
            AddMobileMenuGroup(list, "falsum.menu.view", CreateViewMenuEntries());
            AddMobileMenuGroup(list, "falsum.menu.tool", CreateToolMenuEntries());
        }

        private void AddMobileMenuGroup(
            VisualElement list,
            string captionKey,
            MenuEntry[] entries)
        {
            var column = new VisualElement();
            column.AddToClassList("mobile-menu-column");
            var caption = new Label(L.Get(captionKey));
            caption.AddToClassList("mobile-menu-caption");
            column.Add(caption);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mobile-menu-body");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            column.Add(scroll);
            list.Add(column);
            if (entries == null)
            {
                return;
            }
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Separator || entry.Hidden)
                {
                    continue;
                }
                var button = new Button
                {
                    text = entry.Checked
                        ? L.Get("falsum.menu.checkedItem", ("label", entry.Label))
                        : entry.Label,
                };
                button.AddToClassList("mobile-menu-entry");
                button.SetEnabled(entry.Enabled);
                var action = entry.Action;
                button.clicked += delegate
                {
                    var panel = _root.Q<VisualElement>("MobileMenuPanel");
                    if (panel != null)
                    {
                        panel.AddToClassList("hidden");
                    }
                    if (action != null)
                    {
                        action();
                    }
                };
                scroll.Add(button);
            }
        }

        /// <summary>
        /// Numeric entry for the phone: a compact modal keypad,
        /// never the IME.
        ///
        /// WHY NOT THE IME: a landscape software keyboard covers the whole
        /// playfield, and FalsumImePosition already returns false off
        /// Windows, so the caret would not even be positioned. Every press is
        /// sanitised through SanitizeNumericInput, the same entry point the
        /// desktop fields use, so a full-width comma or a second decimal
        /// point cannot slip through.
        /// </summary>
        private void ShowMobileNumericEntry(
            string caption,
            string initial,
            bool allowFraction,
            Action<string> commit)
        {
            if (_mobileShell == null) return;
            CloseMobileNumericEntry();
            _mobileNumericOverlay = new VisualElement { name = "MobileNumericOverlay" };
            _mobileNumericOverlay.AddToClassList("mobile-numeric-overlay");
            _mobileNumericOverlay.pickingMode = PickingMode.Ignore;
            var host = new VisualElement { name = "MobileNumericWindow" };
            host.AddToClassList("mobile-numeric-window");
            _mobileNumericOverlay.Add(host);
            _menuLayer.Add(_mobileNumericOverlay);
            var activeOverlay = _mobileNumericOverlay;
            _mobileNumericOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _mobileNumericOverlay) CloseMobileNumericEntry();
                evt.StopPropagation();
            });
            var buffer = initial ?? string.Empty;
            var replaceInitial = true;
            var pad = new VisualElement();
            pad.AddToClassList("numeric-pad");
            var head = new Label(buffer);
            head.tooltip = caption;
            head.AddToClassList("numeric-pad-head");
            pad.Add(head);
            var refresh = (Action)delegate
            {
                head.text = buffer;
            };
            string[] keys = allowFraction
                ? new[] { "7", "8", "9", "/", "4", "5", "6", "-", "1", "2", "3", ".", "0", "00", "000", "" }
                : new[] { "7", "8", "9", "-", "4", "5", "6", ".", "1", "2", "3", "00", "0", "000", "", "" };
            var row = new VisualElement();
            row.AddToClassList("numeric-pad-row");
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var button = new Button { text = key };
                button.SetEnabled(key.Length > 0);
                button.AddToClassList("numeric-pad-key");
                button.clicked += delegate
                {
                    buffer = SanitizeNumericInput(
                        (replaceInitial ? string.Empty : buffer) + key,
                        allowFraction);
                    replaceInitial = false;
                    refresh();
                };
                row.Add(button);
                if ((i + 1) % 4 == 0)
                {
                    pad.Add(row);
                    row = new VisualElement();
                    row.AddToClassList("numeric-pad-row");
                }
            }
            var actions = new VisualElement();
            actions.AddToClassList("numeric-pad-row");
            var back = new Button { text = "⌫" };
            back.AddToClassList("numeric-pad-key");
            back.clicked += delegate
            {
                if (buffer.Length > 0)
                {
                    replaceInitial = false;
                    buffer = buffer.Substring(0, buffer.Length - 1);
                    refresh();
                }
            };
            actions.Add(back);
            var cancel = new Button { text = L.Get("falsum.mobile.cancel") };
            cancel.AddToClassList("numeric-pad-key");
            cancel.clicked += CloseMobileNumericEntry;
            actions.Add(cancel);
            var confirm = new Button { text = L.Get("falsum.mobile.confirm") };
            confirm.AddToClassList("numeric-pad-key");
            confirm.AddToClassList("is-confirm");
            confirm.clicked += delegate
            {
                if (_mobileNumericOverlay != activeOverlay) return;
                CloseMobileNumericEntry();
                if (commit != null)
                {
                    commit(SanitizeNumericInput(buffer, allowFraction));
                }
            };
            actions.Add(confirm);
            pad.Add(actions);
            host.Add(pad);
            _mobileShell.LayoutNumericWindow();
            _mobileNumericOverlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                CloseMobileNumericEntry();
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        private void CloseMobileNumericEntry()
        {
            _mobileNumericOverlay?.RemoveFromHierarchy();
            _mobileNumericOverlay = null;
        }

        private void BindMobileNumericInput(TextField field, string caption, bool allowFraction)
        {
            if (field == null || field.ClassListContains("is-mobile-numeric-bound")) return;
            field.AddToClassList("is-mobile-numeric-bound");
            var pointerHost = field.parent ?? field;
            pointerHost.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_mobileShell == null || evt.button != 0 ||
                    FindAncestorTextField(evt.target as VisualElement) != field) return;
                evt.StopImmediatePropagation();
                field.panel?.focusController.IgnoreEvent(evt);
                field.Blur();
                ShowMobileNumericEntry(caption, field.value, allowFraction, value =>
                {
                    if (field.panel != null) field.value = value;
                });
            }, TrickleDown.TrickleDown);
            field.hideMobileInput = true;
        }

        private void ConfigureMobileEventsWindow()
        {
            if (_eventsPanel == null || _mobileEventsOverlay != null) return;
            _desktopEventsParent = _eventsPanel.parent;
            if (_desktopEventsParent == null) return;
            _desktopEventsIndex = _desktopEventsParent.IndexOf(_eventsPanel);
            _eventPanelAnimation?.Pause();
            _eventPanelAnimation = null;
            SetEventPanelOffset(0.0f);
            _mobileEventsOverlay = new VisualElement { name = "MobileEventsOverlay" };
            _mobileEventsOverlay.AddToClassList("mobile-events-overlay");
            _eventsPanel.AddToClassList("is-mobile-window");
            _mobileEventsOverlay.Add(_eventsPanel);
            _menuLayer.Add(_mobileEventsOverlay);
            SetVisible(_mobileEventsOverlay, _eventsPanelOpen);
            SetVisible(_eventsPanel, _eventsPanelOpen);
            _mobileEventsOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _mobileEventsOverlay) SetEventsPanelOpen(false);
                evt.StopPropagation();
            });
        }

        private void RestoreMobileWindows()
        {
            CloseMobileNumericEntry();
            _mobileMenuPanel?.RemoveFromHierarchy();
            _mobileMenuPanel = null;
            if (_mobileEventsOverlay == null) return;
            _eventsPanel.RemoveFromClassList("is-mobile-window");
            _eventValidation?.RemoveFromClassList("hidden");
            _desktopEventsParent.Insert(
                Mathf.Min(_desktopEventsIndex, _desktopEventsParent.childCount), _eventsPanel);
            _mobileEventsOverlay.RemoveFromHierarchy();
            _mobileEventsOverlay = null;
            SetEventPanelOffset(0.0f);
        }

        private bool DismissMobileOverlay()
        {
            if (_mobileDismissFrame == Time.frameCount) return true;
            if (_mobileNumericOverlay != null)
            {
                _mobileDismissFrame = Time.frameCount;
                CloseMobileNumericEntry();
                return true;
            }
            if (_modal != null || _disclaimerOverlay != null || IsToolDialogOpen ||
                IsToolReportOpen || IsChartInfoOpen ||
                (_preferencesView != null && _preferencesView.IsOpen))
            {
                return false;
            }
            if (_mobileMenuPanel != null && !_mobileMenuPanel.ClassListContains("hidden"))
            {
                _mobileDismissFrame = Time.frameCount;
                _mobileMenuPanel.AddToClassList("hidden");
                return true;
            }
            if (_mobileEventsOverlay != null && _eventsPanelOpen)
            {
                _mobileDismissFrame = Time.frameCount;
                SetEventsPanelOpen(false);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Applies the persisted phone control scale. Safe to call when no
        /// shell is mounted: the developer switch may be off while the scale
        /// is still set.
        /// </summary>
        public void ApplyMobileUiScale(float scale)
        {
            if (_mobileShell != null)
            {
                _mobileShell.ApplyScale(scale);
            }
        }

        /// <summary>Raised when the phone audition button is pressed.</summary>
        public event Action MobileAuditionRequested;

        /// <summary>Raised when the phone segment-select button is pressed.</summary>
        public event Action MobileSegmentSelectRequested;

        /// <summary>
        /// Pushes the playhead onto the phone's vertical progress strip and
        /// the playback state onto its transport button. Called from the same
        /// place the desktop bars are updated, so the two can never disagree.
        /// </summary>
        public void SetMobileTransport(float normalized, string label, bool playing)
        {
            if (_mobileShell == null)
            {
                return;
            }
            _mobileShell.SetProgress(normalized, label);
            _mobileShell.SetPlaying(playing);
        }

        /// <summary>
        /// Reflects the editor's placement mode on the phone's four type
        /// buttons, so the highlight cannot disagree with the real mode.
        /// </summary>
        public void SetMobilePlacementMode(FalsumRadialAction action)
        {
            if (_mobileShell != null)
            {
                _mobileShell.SetPlacementMode(action);
            }
        }

        /// <summary>The phone shell, or null on desktop.</summary>
        public FalsumMobileShell MobileShell
        {
            get { return _mobileShell; }
        }

        /// <summary>
        /// Text for the mobile-only chrome. The audit forbids `text=` and
        /// `tooltip=` literals in UXML, so every label on the shell is bound
        /// here from the two locale files like any other visible string.
        /// The glyphs are still localized on purpose: an icon that reads as
        /// "mirror" in one language may not in another, and keeping them as
        /// keys means a translator can replace them without a code change.
        /// </summary>
        private void ApplyMobileChromeText()
        {
            BindGlyph("ToolSelect", "falsum.mobile.tool.select");
            BindGlyph("ToolGrid", "falsum.mobile.tool.grid");
            BindGlyph("ToolMirror", "falsum.mobile.tool.mirror");
            BindGlyph("ToolBank", "falsum.mobile.tool.bank");
            BindGlyph("StageAbandon", "falsum.mobile.stage.abandon");
            BindGlyph("WheelCenter", "falsum.mobile.wheel.center");
            var tapLabel = _root.Q<Label>("WheelLabelTap");
            if (tapLabel != null)
            {
                tapLabel.text = L.Get("falsum.noteKind.tap");
            }
            var holdLabel = _root.Q<Label>("WheelLabelHold");
            if (holdLabel != null)
            {
                holdLabel.text = L.Get("falsum.noteKind.hold");
            }
            var flickLabel = _root.Q<Label>("WheelLabelFlick");
            if (flickLabel != null)
            {
                flickLabel.text = L.Get("falsum.noteKind.flick");
            }
            var skyLabel = _root.Q<Label>("WheelLabelSky");
            if (skyLabel != null)
            {
                skyLabel.text = L.Get("falsum.noteKind.skyArea");
            }
        }

        private void BindGlyph(string elementName, string key)
        {
            var button = _root.Q<Button>(elementName);
            if (button != null)
            {
                button.text = L.Get(key);
                return;
            }
            var label = _root.Q<Label>(elementName);
            if (label != null)
            {
                label.text = L.Get(key);
            }
        }

        /// <summary>Raised when the placement wheel picks a note kind.</summary>
        public event Action<FalsumRadialAction> MobilePlacementRequested;

        /// <summary>Raised when the wheel centre is tapped.</summary>
        public event Action MobileExitPlacementRequested;

        public bool IsChartPreviewChromeCollapsed
        {
            get
            {
                return _chartPreviewMode &&
                       !_chartPreviewTransitionActive &&
                       _topBar != null &&
                       _bottomBar != null &&
                       _topBar.ClassListContains("hidden") &&
                       _bottomBar.ClassListContains("hidden");
            }
        }

        /// <summary>
        /// On the phone BOTH bars are `display: none`, so the desktop formula
        /// -- which subtracts their heights -- reports "nothing collapsed" and
        /// the camera and the track-end composer keep the editing framing.
        /// The scene therefore stayed small and the track end never reached the
        /// top of the screen.
        ///
        /// The phone layout IS the preview framing: with no permanent bars the
        /// playfield owns the whole screen, so the collapse value is pinned to 1
        /// and the zoom / inset properties below compute the preview numbers
        /// from it.
        /// </summary>
        public float ChartPreviewCollapse
        {
            get
            {
                if (_mobileShell != null)
                {
                    return 1.0f;
                }
                return _chartPreviewCollapse;
            }
        }

        public float ChartPreviewTopInsetFraction
        {
            get
            {
                // No bars on the phone, so nothing occludes the top edge.
                if (_mobileShell != null)
                {
                    return 0.0f;
                }
                if (_chartPreviewMode &&
                    !_chartPreviewTransitionActive &&
                    _chartPreviewCollapse >= 1.0f - 0.0001f)
                {
                    return 0.0f;
                }
                var rootHeight = GetChromeRootHeight();
                var topHeight = MeasureBarHeight(_topBar, 56.0f);
                var top = Mathf.Max(
                    0.0f,
                    topHeight + _chartPreviewTopOffset);
                return Mathf.Clamp01(top / rootHeight);
            }
        }

        public float ChartPreviewZoomScale
        {
            get
            {
                // The chrome occupies NO height on the phone: both bars are
                // display:none. MeasureBarHeight cannot tell "hidden" from
                // "not laid out yet" and falls back to its 56 / 64 defaults,
                // so the visible fraction stayed at the editing value and the
                // scene never grew.
                //
                // The phone value is NOT just the base multiplier: the desktop
                // formula is 1/visibleFraction * multiplier, and with the bars
                // gone visibleFraction is 1. Returning the bare multiplier
                // (1.099) left the scene at editing size, which is why the
                // track end still did not reach the top of the screen.
                if (_mobileShell != null)
                {
                    // visibleFraction is 1 with both bars hidden, so the
                    // desktop's 1/visibleFraction factor is 1 here; the
                    // multiplier still applies because it compensates for the
                    // perspective track's own foreshortening.
                    return ChartPreviewTrackScaleMultiplier;
                }
                var rootHeight = GetChromeRootHeight();
                var topFraction = Mathf.Clamp01(
                    MeasureBarHeight(_topBar, 56.0f) / rootHeight);
                var bottomFraction = Mathf.Clamp01(
                    MeasureBarHeight(_bottomBar, 64.0f) / rootHeight);
                var visibleFraction = Mathf.Max(
                    0.5f,
                    1.0f - topFraction - bottomFraction);
                var targetZoom =
                    1.0f / visibleFraction *
                    ChartPreviewTrackScaleMultiplier;
                return Mathf.LerpUnclamped(
                    1.0f,
                    targetZoom,
                    _chartPreviewCollapse);
            }
        }

        public bool IsNewChartDialogOpen
        {
            get { return _newChartOverlay != null; }
        }

        public bool IsModalOpen
        {
            get
            {
                return _modal != null ||
                       (_mobileMenuPanel != null && !_mobileMenuPanel.ClassListContains("hidden")) ||
                       (_mobileEventsOverlay != null && _eventsPanelOpen) ||
                       _disclaimerOverlay != null ||
                       IsToolReportOpen ||
                       IsToolDialogOpen ||
                       IsChartInfoOpen ||
                       (_preferencesView != null &&
                        _preferencesView.IsOpen);
            }
        }

        public bool IsToolDialogOpen
        {
            get { return _toolDialog != null && _toolDialog.IsOpen; }
        }

        public bool IsToolReportOpen
        {
            get
            {
                return _toolReportView != null &&
                       _toolReportView.IsOpen &&
                       !_toolReportView.IsFloating;
            }
        }

        public bool IsToolReportFloating
        {
            get
            {
                return _toolReportView != null &&
                       _toolReportView.IsOpen &&
                       _toolReportView.IsFloating;
            }
        }

        public bool IsPreferencesOpen
        {
            get
            {
                return _preferencesView != null &&
                       _preferencesView.IsOpen;
            }
        }

        public FalsumToolDialogView ToolDialog
        {
            get { return _toolDialog; }
        }

        public FalsumPreferencesView PreferencesView
        {
            get { return _preferencesView; }
        }

        public string DebugUiStyle
        {
            get
            {
                var color = _topBar != null
                    ? _topBar.resolvedStyle.backgroundColor.ToString()
                    : "none";
                var height = _topBar != null
                    ? _topBar.resolvedStyle.height
                    : 0.0f;
                var menuBorder = _fileMenuButton != null
                    ? _fileMenuButton.resolvedStyle.borderBottomWidth
                    : 0.0f;
                var fieldBorder = _speedInput != null
                    ? _speedInput.resolvedStyle.borderTopWidth
                    : 0.0f;
                return _uiStyle.ToString() +
                       " top=" + color +
                       " height=" + height.ToString("0") +
                       " menuBorder=" + menuBorder.ToString("0") +
                       " fieldBorder=" + fieldBorder.ToString("0");
            }
        }

        public bool IsPerformanceWindowOpen
        {
            get
            {
                return _performanceWindow != null &&
                       _performanceWindow.IsOpen;
            }
        }

        public void SetPerformanceMonitor(
            FalsumPerformanceMonitor monitor)
        {
            _performanceMonitor = monitor;
            if (_performanceWindow != null)
            {
                _performanceWindow.SetMonitor(monitor);
            }
        }

        public void TickPerformanceWindow()
        {
            if (_performanceWindow != null)
            {
                _performanceWindow.Tick();
            }
        }

        public bool DebugTogglePerformanceWindow()
        {
            if (_performanceWindow == null ||
                _performanceMonitor == null)
            {
                return false;
            }
            if (_performanceWindow.IsOpen)
            {
                _performanceWindow.Close();
            }
            else
            {
                _performanceWindow.Open();
            }
            return _performanceWindow.IsOpen;
        }

        public void SetShortcutPreferences(
            FalsumPreferences preferences)
        {
            _shortcutPreferences = preferences;
            SetUiStyle(
                preferences != null
                    ? preferences.Advanced.UIStyle
                    : FalsumUiStyle.Classic);
        }

        public void SetUiStyle(FalsumUiStyle style)
        {
            _uiStyle = style;
            if (_root != null)
            {
                FalsumUiStyles.Apply(_falsumRoot ?? _root, style);
            }
            RefreshMiddleEditStatusText();
            RefreshTransportIconTint();
            RefreshPropertyPanelHeight();
        }

        /// <summary>
        /// The top bar shows only the current mode name, so there is no
        /// style-dependent tutorial/alternatives text left to re-render.
        /// Kept as the hook the UI-style switch calls.
        /// </summary>
        private void RefreshMiddleEditStatusText()
        {
            ApplyMiddleEditStatusVisibility();
        }

        /// <summary>
        /// Collapses the indicator down to the mode name, anchored at the far
        /// right of the top bar. The tutorial label and the alternative-mode
        /// label are emptied and hidden, so no empty stretch is left where the
        /// hint used to sit.
        /// <para><c>left: auto</c> is applied inline on purpose: the Studio and
        /// Canvas style sheets (not owned by this change) re-pin this block
        /// with a higher-specificity <c>left: 59%</c> under <c>.is-compact</c>,
        /// which would otherwise keep the empty gap. Inline is the only rule
        /// that wins in every style.</para>
        /// </summary>
        private void ApplyMiddleEditStatusVisibility()
        {
            if (_middleEditModeHint != null)
            {
                _middleEditModeHint.text = string.Empty;
                _middleEditModeHint.tooltip = string.Empty;
                SetVisible(_middleEditModeHint, false);
            }
            if (_middleEditModeAlternatives != null)
            {
                _middleEditModeAlternatives.text = string.Empty;
                SetVisible(_middleEditModeAlternatives, false);
            }
            if (_middleEditStatus != null)
            {
                _middleEditStatus.style.left = StyleKeyword.Auto;
                _middleEditStatus.style.width = StyleKeyword.Auto;
            }
        }

        private void RefreshTransportIconTint()
        {
            if (_playPauseIcon == null)
            {
                return;
            }
            var playing = _hasDisplayedPlaying && _displayedPlaying;
            switch (_uiStyle)
            {
                case FalsumUiStyle.InFalsus:
                    _playPauseIcon.tintColor = playing
                        ? new Color(0.70f, 0.90f, 0.91f, 1.0f)
                        : new Color(0.04f, 0.08f, 0.10f, 1.0f);
                    break;
                case FalsumUiStyle.Studio:
                    _playPauseIcon.tintColor = playing
                        ? StudioPauseIconTint
                        : StudioPlayIconTint;
                    break;
                case FalsumUiStyle.Canvas:
                    _playPauseIcon.tintColor = playing
                        ? CanvasPauseIconTint
                        : CanvasPlayIconTint;
                    break;
                default:
                    _playPauseIcon.tintColor = playing
                        ? PauseIconTint
                        : PlayIconTint;
                    break;
            }
        }

        public bool IsEventsPanelOpen
        {
            get { return _eventsPanelOpen; }
        }

        public bool IsSkyAreaBankOpen
        {
            get
            {
                return _skyAreaBankView != null &&
                       _skyAreaBankView.IsOpen;
            }
        }

        /// <summary>
        /// Fills the bank's import dialog with the text of a chosen
        /// <c>.fsky</c> file. The editor reads the file (the view never touches
        /// the filesystem) and the author still confirms inside the dialog.
        /// </summary>
        public void SetBankImportCode(string code)
        {
            if (_skyAreaBankView == null)
            {
                return;
            }
            _skyAreaBankView.SetImportCode(code);
        }

        /// <summary>Pushes the whole library into the sidebar list.</summary>
        public void SetBankPresets(FalsumSkyAreaBankDocument document)
        {
            if (_skyAreaBankView == null)
            {
                return;
            }
            _skyAreaBankView.SetPresets(document);
        }

        public void SetBankPanelOpen(bool open)
        {
            if (_skyAreaBankView == null)
            {
                return;
            }
            _skyAreaBankView.SetOpen(open, false);
        }

        public void SetBankSelectionSummary(int skyAreaCount, int totalCount)
        {
            if (_skyAreaBankView == null)
            {
                return;
            }
            _skyAreaBankView.SetSelectionSummary(
                skyAreaCount,
                totalCount);
        }

        public void ShowBankShareCode(
            FalsumSkyAreaPreset preset,
            string code)
        {
            if (_skyAreaBankView == null)
            {
                return;
            }
            _skyAreaBankView.ShowShareCode(preset, code);
        }

        public int DebugEventRowCount
        {
            get { return _eventRows.Count; }
        }

        public int DebugEventRefreshCount
        {
            get { return _eventRefreshCount; }
        }

        public int DebugPropertyRefreshCount
        {
            get { return _propertyRefreshCount; }
        }

        public static string SanitizeNumericInput(
            string value,
            bool allowFraction = false)
        {
            return SanitizeNumericText(value, allowFraction);
        }

        public string DebugPropertyValue(string key)
        {
            for (var i = 0; i < _propertyRows.Count; i++)
            {
                if (_propertyRows[i].Key != key)
                {
                    continue;
                }
                var row = _propertyRows[i];
                if (row.Field != null)
                {
                    return row.Field.value;
                }
                if (row.Button != null)
                {
                    return row.Button.text;
                }
            }
            return string.Empty;
        }

        public string DebugFlickDirectionControls()
        {
            for (var i = 0; i < _propertyRows.Count; i++)
            {
                var row = _propertyRows[i];
                if (row.Key != "flick_direction" ||
                    row.Root == null)
                {
                    continue;
                }
                var toggle = row.Root.Q<VisualElement>(
                    "FlickDirectionToggle");
                if (toggle == null)
                {
                    return "missing";
                }
                var count = 0;
                var selected = 0;
                var selectedText = string.Empty;
                foreach (var child in toggle.Children())
                {
                    var button = child as Button;
                    if (button == null)
                    {
                        continue;
                    }
                    count++;
                    if (button.ClassListContains("is-selected"))
                    {
                        selected++;
                        selectedText = button.text;
                    }
                }
                return "count=" + count +
                       " selected=" + selected +
                       " value=" + selectedText;
            }
            return "missing";
        }

        public FalsumEditorView(Transform host)
        {
            _host = host;
        }

        /// <summary>
        /// Switches the live editor between the desktop and the Android
        /// phone layout, in place.
        ///
        /// THREE THINGS CHANGE, AND NOTHING IS REBUILT:
        /// 1. <see cref="UIDocument.panelSettings"/> is repointed at a panel
        ///    whose reference resolution makes one panel unit one dp. This
        ///    is a live property; the visual tree survives it, which is the
        ///    whole point -- the editor wires its events into this document
        ///    once, and rebuilding would either drop them or need a second
        ///    wiring pass.
        /// 2. The shell is attached or detached, which toggles `.is-mobile`
        ///    and the mobile style sheet.
        /// 3. The root gets a repaint so the new cascade resolves.
        ///
        /// The desktop panel asset is never mutated: the mobile panel is a
        /// runtime clone, so FalsumSelfCheck's 1730x1080 assertion keeps
        /// holding after a round trip.
        /// </summary>
        /// <summary>
        /// Mounts the phone shell. Called by the editor AFTER WireView(), so
        /// that every event the shell forwards lands on a live subscription.
        ///
        /// Idempotent: a second call is a no-op, and on a desktop session
        /// (where FalsumMobileShell.IsMobile is false) it does nothing at all.
        /// </summary>
        public void MountMobileShellIfNeeded()
        {
            if (_mobileShell != null ||
                _document == null ||
                !FalsumMobileShell.IsMobile)
            {
                return;
            }
            var mobilePanel = _mobilePanelSettings ?? CreateMobilePanelSettings(
                Resources.Load<PanelSettings>(PanelSettingsPath));
            if (mobilePanel != null)
            {
                _mobilePanelSettings = mobilePanel;
                _document.panelSettings = mobilePanel;
            }
            _mobileShell = FalsumMobileShell.Attach(_falsumRoot ?? _root);
            if (_mobileShell == null)
            {
                return;
            }
            WireMobileShell();
            SyncMobileFieldsFromDesktop();
            SetUiStyle(_uiStyle);
            _root.MarkDirtyRepaint();
        }

        internal void SetMobileLayoutForPages(bool mobile)
        {
            if (_document == null || _root == null)
            {
                return;
            }
            if (mobile == (_mobileShell != null))
            {
                return;
            }
            var desktopPanel =
                Resources.Load<PanelSettings>(PanelSettingsPath) ??
                CreateRuntimePanelSettings();
            if (mobile)
            {
                FalsumMobileShell.RuntimeOverride = true;
                var mobilePanel = CreateMobilePanelSettings(desktopPanel);
                if (mobilePanel == null)
                {
                    // Cannot honour the switch; leave the desktop layout
                    // alone rather than half-apply it.
                    FalsumMobileShell.RuntimeOverride = null;
                    return;
                }
                _document.panelSettings = mobilePanel;
                _mobilePanelSettings = mobilePanel;
                _mobileShell =
                    FalsumMobileShell.Attach(_falsumRoot ?? _root);
                if (_mobileShell != null)
                {
                    WireMobileShell();
                    // Seed from the desktop fields HERE as well as in
                    // Build(). On a desktop session the shell does not exist
                    // at Build time (IsMobile is false), so this toggle is
                    // the first moment the phone fields exist -- and without
                    // this the BPM / subdivision / time boxes stayed empty
                    // until something incidentally called a setter again.
                    SyncMobileFieldsFromDesktop();
                }
            }
            else
            {
                if (_mobileShell != null)
                {
                    RestoreMobileWindows();
                    _mobileShell.Detach();
                    _mobileShell = null;
                }
                FalsumMobileShell.RuntimeOverride = null;
                _document.panelSettings = desktopPanel;
                if (_mobilePanelSettings != null)
                {
                    FalsumPlatform.DestroyObject(_mobilePanelSettings);
                    _mobilePanelSettings = null;
                }
            }
            SetUiStyle(_uiStyle);
            _root.MarkDirtyRepaint();
        }

        public void Build()
        {
            L.Initialize();
            var panelSettings =
                Resources.Load<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                Debug.LogError(
                    L.Get(
                        "falsum.error.ui.panelSettingsMissing",
                        ("path", "Resources/" + PanelSettingsPath)));
                panelSettings = CreateRuntimePanelSettings();
            }
            // THE MOBILE PANEL IS A SEPARATE SETTINGS OBJECT, NOT A USS
            // OVERRIDE. The desktop panel pins the reference resolution at
            // 1730x1080 (FalsumPanelSettings.asset:27), so on a 2400x1080
            // phone one panel unit resolves to ~0.47 dp and every USS
            // constant written in "dp" would land at HALF its intended touch
            // size -- the exact defect docs/memory describes as "the breakpoint
            // tests the wrong dimension". Repointing the reference resolution
            // at 900x405 makes one panel unit equal one dp on that device, so
            // FalsumMobile.uss can state real dp numbers and the shell's hit
            // test can compare against them directly.
            var mobilePanelSettings = CreateMobilePanelSettings(panelSettings);
            _mobilePanelSettings = mobilePanelSettings;
            var documentPanel = mobilePanelSettings ?? panelSettings;
            var mainUxml = Resources.Load<VisualTreeAsset>(MainUxmlPath);
            _document = CreateDocument(documentPanel, mainUxml);
            _root = _document.rootVisualElement;
            _root.style.flexGrow = 1;

            if (mainUxml == null)
            {
                var message =
                    L.Get(
                        "falsum.error.ui.mainResourceMissing",
                        ("path", "Resources/" + MainUxmlPath + ".uxml"));
                Debug.LogError("[Falsum] " + message);
                ShowBuildError(message);
                return;
            }

            try
            {
                LoadTemplates();
                QueryMainElements();
                L.ApplyStaticText(_root);
                ApplyLocalizedStaticText();
                ConfigureFonts();
                ConfigureSongInfoCard();
                UpdateResponsiveClasses();
                _speedFill = ConfigureSliderFill(
                    _speedSlider,
                    "flow-fill");
                _timelineFill = ConfigureSliderFill(
                    _timelineSlider,
                    "progress-fill");
                _timelineDragContainer =
                    _timelineSlider.Q<VisualElement>(
                        "unity-drag-container");
                _timelineDragger =
                    _timelineDragContainer != null
                        ? _timelineDragContainer.Q<VisualElement>(
                            "unity-dragger")
                        : null;
                BindTopBar();
                BindBottomBar();
                BindPropertyPanel();
                BindEventsPanel();
                _audioPreprocessView = new FalsumAudioPreprocessView(
                    _modalLayer,
                    _audioPreprocessDialogTemplate);
                _preferencesView = new FalsumPreferencesView(
                    _root,
                    _modalLayer,
                    _preferencesDialogTemplate);
                _preferencesView.ChartInfoCommitRequested +=
                    delegate(FalsumChartMetadataDraft draft,
                             FalsumPackage package)
                    {
                        var commit = ChartInfoCommitRequested;
                        if (commit != null)
                        {
                            commit(draft, package);
                        }
                    };
                _preferencesView.ChartInfoCancelRequested +=
                    delegate
                    {
                        var cancel = ChartInfoCancelRequested;
                        if (cancel != null)
                        {
                            cancel();
                        }
                    };
                _performanceWindow =
                    new FalsumPerformanceWindow(
                        _falsumRoot ?? _root);
                _preferencesView.PerformanceWindowToggleRequested +=
                    TogglePerformanceWindowFromAbout;
                _toolDialog = new FalsumToolDialogView(
                    _modalLayer,
                    _toolDialogTemplate);
                _toolReportView = new FalsumToolReportView(
                    _modalLayer,
                    _toolReportDialogTemplate);
                _toolReportView.LocateRequested +=
                    delegate(FalsumToolReportItem item)
                    {
                        Raise(
                            ToolReportLocateRequested,
                            item);
                    };
                // The bank lives on its own layer, NOT the modal layer: it must
                // stay usable while the chart is still editable, so it cannot
                // set IsModalOpen or swallow editor input.
                _skyAreaBankView = new FalsumSkyAreaBankView(
                    _bankLayer,
                    _skyAreaBankTemplate);
                _skyAreaBankView.PanelVisibilityChanged +=
                    delegate(bool visible)
                    {
                        Raise(BankPanelVisibilityChanged, visible);
                    };
                _skyAreaBankView.SaveSelectionRequested +=
                    delegate(string name, IList<string> tags)
                    {
                        var handler = BankSaveSelectionRequested;
                        if (handler != null)
                        {
                            handler(name, tags);
                        }
                    };
                _skyAreaBankView.PlacePresetRequested +=
                    delegate(FalsumSkyAreaPreset preset)
                    {
                        Raise(BankPlacePresetRequested, preset);
                    };
                _skyAreaBankView.DeletePresetRequested +=
                    delegate(FalsumSkyAreaPreset preset)
                    {
                        Raise(BankDeletePresetRequested, preset);
                    };
                _skyAreaBankView.RenamePresetRequested +=
                    delegate(FalsumSkyAreaPreset preset, string name)
                    {
                        var handler = BankRenamePresetRequested;
                        if (handler != null)
                        {
                            handler(preset, name);
                        }
                    };
                _skyAreaBankView.PresetScalesRequested +=
                    delegate(FalsumSkyAreaPreset preset,
                             double width,
                             double length)
                    {
                        var handler = BankPresetScalesRequested;
                        if (handler != null)
                        {
                            handler(preset, width, length);
                        }
                    };
                _skyAreaBankView.CopyCodeRequested +=
                    delegate(FalsumSkyAreaPreset preset)
                    {
                        var handler = BankCopyCodeRequested;
                        if (handler != null)
                        {
                            handler(preset);
                        }
                    };
                _skyAreaBankView.ExportFileRequested +=
                    delegate(FalsumSkyAreaPreset preset)
                    {
                        Raise(BankExportFileRequested, preset);
                    };
                _skyAreaBankView.ImportCodeRequested +=
                    delegate(string code)
                    {
                        Raise(BankImportCodeRequested, code);
                    };
                _skyAreaBankView.ImportFileTextRequested +=
                    delegate
                    {
                        var handler = BankImportFileTextRequested;
                        if (handler != null)
                        {
                            handler();
                        }
                    };
                _skyAreaBankView.ImportFileRequested +=
                    delegate
                    {
                        var handler = BankImportFileRequested;
                        if (handler != null)
                        {
                            handler();
                        }
                    };
                FalsumUiStyles.Apply(_falsumRoot ?? _root, _uiStyle);
                // THE SHELL IS NOT MOUNTED HERE.
                //
                // Build() runs BEFORE FalsumChartEditor.WireView(), so the
                // editor has not subscribed to this view's events yet. Mounting
                // the shell at this point wired its buttons to delegates that
                // forwarded into nothing -- every phone control clicked and
                // did nothing, which is exactly the reported symptom, while the
                // desktop layout was unaffected (Attach() returns null there).
                //
                // AttachMobileShell() is called from the editor after
                // WireView(), through MountMobileShellIfNeeded().
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowBuildError(exception.Message);
            }
        }

        private void ApplyLocalizedStaticText()
        {
            ApplyMobileChromeText();
            L.Bind(_fileMenuButton, "falsum.menu.file");            L.Bind(_editMenuButton, "falsum.menu.edit");
            L.Bind(_viewMenuButton, "falsum.menu.view");
            L.Bind(_toolMenuButton, "falsum.menu.tool");
            if (_skyAreaBankMenuButton != null)
            {
                L.Bind(_skyAreaBankMenuButton, "falsum.menu.skyAreaBank");
            }
            L.Bind(_middleEditModeLabel, "falsum.editor.middleEdit.boundary");
            // The hint and the alternatives labels are hidden; they are no
            // longer bound so no tutorial text is ever pushed into them.
            L.Bind(
                _audioPreprocessButton,
                "falsum.audioPreprocess.entry");
            L.BindTooltip(
                _audioPreprocessButton,
                "falsum.audioPreprocess.tooltip");
            L.BindTooltip(_baseBpmInput, "falsum.editor.baseBpmTooltip");
            L.BindTooltip(_timeInput, "falsum.editor.timeTooltip");
            L.BindTooltip(_undoButton, "falsum.editor.history.undoTooltip");
            L.BindTooltip(_redoButton, "falsum.editor.history.redoTooltip");
            var clearHistory = Require<Button>(_root, "ClearHistory");
            L.Bind(clearHistory, "falsum.editor.history.clear");
            L.BindTooltip(
                clearHistory,
                "falsum.editor.history.clearTooltip");
            L.Bind(_jumpToNoteButton, "falsum.editor.jumpToNote");
            L.BindTooltip(
                _songInfoCardCover,
                "falsum.editor.editChartInfoTooltip");
            L.BindTooltip(
                _songInfoCardDifficultyHotspot,
                "falsum.editor.switchDifficultyTooltip");
            L.Bind(_eventsButton, "falsum.editor.events");
            L.BindTooltip(
                _eventsButton,
                "falsum.editor.eventsTooltip");
            L.Bind(_eventFilterAll, "falsum.editor.eventFilter.all");
            L.Bind(_eventFilterBpm, "falsum.editor.eventFilter.bpm");
            L.Bind(_eventFilterTrack, "falsum.editor.eventFilter.track");
            L.Bind(_eventFilterLane, "falsum.editor.eventFilter.lane");
            var eventCount = Require<Label>(_root, "EventCount");
            L.BindTooltip(eventCount, "falsum.editor.eventCountTooltip");
            var closeEvents = Require<Button>(_root, "CloseEvents");
            L.BindTooltip(closeEvents, "falsum.editor.closeEventsTooltip");
            var addEvent = Require<Button>(_root, "AddEvent");
            L.BindTooltip(addEvent, "falsum.editor.addEventTooltip");
            L.Bind(
                Require<Button>(_root, "AddBpmEvent"),
                "falsum.editor.eventFilter.bpm");
            L.Bind(
                Require<Button>(_root, "AddTrackEvent"),
                "falsum.editor.eventFilter.track");
            L.Bind(
                _addLaneEventButton,
                "falsum.editor.eventFilter.lane");
            L.BindTooltip(_playPauseButton, "falsum.editor.playTooltip");
            L.BindTooltip(
                _spectrumButton,
                "falsum.editor.spectrumTooltip");
            L.BindTooltip(_rateButton, "falsum.editor.rateTooltip");
        }

        public void Tick()
        {
            _mobileShell?.RefreshLayout();
            if (_mobileShell != null && Input.GetKeyDown(KeyCode.Escape))
            {
                DismissMobileOverlay();
            }
            TickChartPreviewChromeAnimation();
            if (Screen.width != _lastScreenWidth ||
                Screen.height != _lastScreenHeight)
            {
                UpdateResponsiveClasses();
            }
            UpdateImeForFocusedTextInput();
        }

        private void BeginChartPreviewChromeTransition(bool hide)
        {
            if (_topBar == null || _bottomBar == null)
            {
                return;
            }

            if (hide)
            {
                _chartPreviewTopHiddenOffset =
                    -MeasureBarHeight(_topBar, 56.0f);
                _chartPreviewBottomHiddenOffset =
                    MeasureBarHeight(_bottomBar, 64.0f);
            }
            else
            {
                SetVisible(_topBar, true);
                SetVisible(_bottomBar, true);
            }

            _chartPreviewTransitionActive = true;
            _chartPreviewTransitionToHidden = hide;
            _chartPreviewTransitionStart =
                (float)Time.unscaledTimeAsDouble;
            _chartPreviewTopFrom = _chartPreviewTopOffset;
            _chartPreviewBottomFrom = _chartPreviewBottomOffset;
            _chartPreviewTopTo =
                hide ? _chartPreviewTopHiddenOffset : 0.0f;
            _chartPreviewBottomTo =
                hide ? _chartPreviewBottomHiddenOffset : 0.0f;
            _chartPreviewCollapseFrom = _chartPreviewCollapse;
            _chartPreviewCollapseTo = hide ? 1.0f : 0.0f;
        }

        private void TickChartPreviewChromeAnimation()
        {
            if (!_chartPreviewTransitionActive ||
                _topBar == null ||
                _bottomBar == null)
            {
                return;
            }

            var elapsed =
                (float)Time.unscaledTimeAsDouble -
                _chartPreviewTransitionStart;
            var progress = Mathf.Clamp01(
                elapsed / ChartPreviewChromeAnimationSeconds);
            var eased = Mathf.SmoothStep(0.0f, 1.0f, progress);
            _chartPreviewCollapse = Mathf.LerpUnclamped(
                _chartPreviewCollapseFrom,
                _chartPreviewCollapseTo,
                eased);
            SetChartPreviewBarOffsets(
                Mathf.LerpUnclamped(
                    _chartPreviewTopFrom,
                    _chartPreviewTopTo,
                    eased),
                Mathf.LerpUnclamped(
                    _chartPreviewBottomFrom,
                    _chartPreviewBottomTo,
                    eased));
            if (progress < 1.0f)
            {
                return;
            }

            _chartPreviewTransitionActive = false;
            _chartPreviewCollapse = _chartPreviewCollapseTo;
            if (_chartPreviewTransitionToHidden)
            {
                SetVisible(_topBar, false);
                SetVisible(_bottomBar, false);
            }
            UpdateSongInfoCardTransform();
        }

        private void SetChartPreviewBarOffsets(
            float topOffset,
            float bottomOffset)
        {
            _chartPreviewTopOffset = topOffset;
            _chartPreviewBottomOffset = bottomOffset;
            if (_topBar != null)
            {
                _topBar.style.translate =
                    new StyleTranslate(new Translate(0.0f, topOffset));
            }
            if (_bottomBar != null)
            {
                _bottomBar.style.translate =
                    new StyleTranslate(new Translate(0.0f, bottomOffset));
            }
        }

        private static float MeasureBarHeight(
            VisualElement element,
            float fallback)
        {
            if (element == null)
            {
                return fallback;
            }
            var height = element.resolvedStyle.height;
            if (!float.IsNaN(height) && height > 1.0f)
            {
                return height;
            }
            height = element.layout.height;
            return !float.IsNaN(height) && height > 1.0f
                ? height
                : fallback;
        }

        private float GetChromeRootHeight()
        {
            var height = _root != null
                ? _root.layout.height
                : 0.0f;
            return height > 1.0f
                ? height
                : Mathf.Max(1.0f, Screen.height);
        }

        public void Dispose()
        {
            if (_mobileShell != null)
            {
                RestoreMobileWindows();
                _mobileShell.Detach();
                _mobileShell = null;
            }
            Input.imeCompositionMode = IMECompositionMode.Auto;
            CloseDisclaimerImmediate();
            CloseNewChartDialog(true);
            if (_toolReportView != null)
            {
                _toolReportView.CloseImmediate();
                _toolReportView = null;
            }
            if (_preferencesView != null)
            {
                _preferencesView.PerformanceWindowToggleRequested -=
                    TogglePerformanceWindowFromAbout;
                _preferencesView.CloseImmediate();
            }
            if (_performanceWindow != null)
            {
                _performanceWindow.Dispose();
                _performanceWindow = null;
            }
            _performanceMonitor = null;
            if (_toolDialog != null)
            {
                _toolDialog.CloseImmediate();
                _toolDialog = null;
            }
            if (_root != null)
            {
                _root.UnregisterCallback<GeometryChangedEvent>(
                    OnRootGeometryChanged);
                _root.UnregisterCallback<KeyDownEvent>(
                    OnRootKeyDown,
                    TrickleDown.TrickleDown);
                _root.UnregisterCallback<PointerDownEvent>(
                    OnRootPointerDown,
                    TrickleDown.TrickleDown);
                _root.UnregisterCallback<NavigationMoveEvent>(
                    OnRootNavigationMove,
                    TrickleDown.TrickleDown);
                _root.UnregisterCallback<FocusInEvent>(
                    OnRootFocusIn,
                    TrickleDown.TrickleDown);
            }
            if (_songInfoCardCoverTexture != null)
            {
                FalsumPlatform.DestroyObject(
                    _songInfoCardCoverTexture);
                _songInfoCardCoverTexture = null;
            }
            if (_eventPanelAnimation != null)
            {
                _eventPanelAnimation.Pause();
                _eventPanelAnimation = null;
            }
            if (_document != null)
            {
                FalsumPlatform.DestroyObject(_document.gameObject);
            }
            _document = null;
            if (_mobilePanelSettings != null)
            {
                FalsumPlatform.DestroyObject(_mobilePanelSettings);
                _mobilePanelSettings = null;
            }
            _preferencesView = null;
            _toolDialog = null;
            _root = null;
            _falsumRoot = null;
            _radialLayer = null;
            _songInfoCardViewport = null;
            _songInfoCardBackdrop = null;
            _songInfoCard = null;
            _songInfoCardCover = null;
            _songInfoCardTitle = null;
            _songInfoCardArtist = null;
            _songInfoCardRating = null;
            _songInfoCardChartDesigner = null;
            _songInfoCardDifficultyHotspot = null;
            _songInfoCardDifficultyMenu = null;
            _songInfoCardDifficultyOptions = null;
        }

        private void ConfigureTransportIcons()
        {
            var undoVector = Resources.Load<VectorImage>(
                "Falsum/UI/Icons/undo-2");
            var redoVector = Resources.Load<VectorImage>(
                "Falsum/UI/Icons/redo-2");
            _playVectorImage = Resources.Load<VectorImage>(
                "Falsum/UI/Icons/play");
            _pauseVectorImage = Resources.Load<VectorImage>(
                "Falsum/UI/Icons/pause");
            _spectrumVectorImage = Resources.Load<VectorImage>(
                "Falsum/UI/Icons/audio-lines");
            _undoIcon = CreateTransportIcon(
                _undoButton,
                undoVector,
                TransportIconTint,
                L.Get("falsum.editor.history.undoTooltip"));
            _redoIcon = CreateTransportIcon(
                _redoButton,
                redoVector,
                TransportIconTint,
                L.Get("falsum.editor.history.redoTooltip"));
            _playPauseIcon = CreateTransportIcon(
                _playPauseButton,
                _playVectorImage,
                PlayIconTint,
                "▶");
            CreateTransportIcon(
                _spectrumButton,
                _spectrumVectorImage,
                TransportIconTint,
                "≈");
            SetPlaybackState(false);
        }

        private static Image CreateTransportIcon(
            Button button,
            VectorImage vectorImage,
            Color tint,
            string fallbackText)
        {
            if (button == null)
            {
                return null;
            }
            button.text = string.Empty;
            if (vectorImage == null)
            {
                button.text = fallbackText;
                Debug.LogWarning(
                    "[Falsum] Transport icon could not be loaded.");
                return null;
            }
            var image = new Image
            {
                vectorImage = vectorImage,
                tintColor = tint,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            image.AddToClassList("control-icon");
            button.Add(image);
            return image;
        }

        public void SetPlaybackState(bool playing)
        {
            // Playback leaves the Slider's own value stale on purpose (see
            // SetTime); hand control back as soon as playback ends, including
            // when this call is a repeated "not playing" report, so clamping
            // and keyboard stepping always see the real position.
            if (!playing)
            {
                FlushTimelineSliderValue();
            }
            if (_hasDisplayedPlaying &&
                _displayedPlaying == playing)
            {
                return;
            }
            _hasDisplayedPlaying = true;
            _displayedPlaying = playing;
            // Pointer-move cost: while a chart plays, hovering must not start
            // transitions. Every :hover enter/exit otherwise runs a 0.12 s
            // transition on that element and keeps the UI Toolkit panel dirty
            // for roughly seven frames after the cursor stops, on top of a
            // frame that already rewrites the timeline fill and the song
            // progress. The class only zeroes the transition duration
            // (".falsum-root.is-playback" in FalsumEditor.uss); hover colours
            // still apply immediately, so nothing visible changes. The class
            // lives on FalsumRoot because that is the element the stylesheet
            // anchors on; before that element is queried this is a no-op.
            if (_falsumRoot != null)
            {
                _falsumRoot.EnableInClassList(
                    "is-playback",
                    playing);
            }
            if (_playPauseButton != null)
            {
                var hasVectorIcons =
                    _playPauseIcon != null &&
                    _playVectorImage != null &&
                    _pauseVectorImage != null;
                _playPauseButton.text = hasVectorIcons
                    ? string.Empty
                    : (playing ? "Ⅱ" : "▶");
                if (hasVectorIcons)
                {
                    _playPauseIcon.vectorImage = playing
                        ? _pauseVectorImage
                        : _playVectorImage;
                    _playPauseIcon.tintColor = playing
                        ? PauseIconTint
                        : PlayIconTint;
                }
                _playPauseButton.tooltip = L.Get(
                    playing
                        ? "falsum.editor.pauseTooltip"
                        : "falsum.editor.playTooltip");
                _playPauseButton.EnableInClassList(
                    "is-playing",
                    playing);
                RefreshTransportIconTint();
            }
        }

        public string DebugRunTransportLayoutChecks()
        {
            if (_timelineSlider == null ||
                _progressText == null ||
                _spectrumButton == null ||
                _rateButton == null)
            {
                return "fail missing-control";
            }
            var slider = _timelineSlider.worldBound;
            var progress = _progressText.worldBound;
            var rate = _rateButton.worldBound;
            var gap = progress.xMin - slider.xMax;
            var progressAdjacent = gap >= -0.5f && gap <= 12.0f;
            if (!FalsumSpectrumFeature.Available)
            {
                // The spectrum button is hidden, so only the progress ->
                // rate ordering still has to hold.
                var progressBeforeRate =
                    progress.xMax <= rate.xMin + 0.5f;
                return progressAdjacent && progressBeforeRate
                    ? "pass gap=" + gap.ToString("0.0") +
                      " spectrum=hidden"
                    : "fail gap=" + gap.ToString("0.0") +
                      " progressAdjacent=" + progressAdjacent +
                      " progressBeforeRate=" + progressBeforeRate;
            }
            var spectrum = _spectrumButton.worldBound;
            var spectrumBeforeRate =
                spectrum.xMax <= rate.xMin + 0.5f;
            var progressBeforeSpectrum =
                progress.xMax <= spectrum.xMin + 0.5f;
            return progressAdjacent &&
                   spectrumBeforeRate &&
                   progressBeforeSpectrum
                ? "pass gap=" + gap.ToString("0.0")
                : "fail gap=" + gap.ToString("0.0") +
                  " progressAdjacent=" + progressAdjacent +
                  " spectrumBeforeRate=" +
                  spectrumBeforeRate +
                  " progressBeforeSpectrum=" +
                  progressBeforeSpectrum;
        }

        public void SetChartLayerVisibility(
            FalsumChartLayer visibility)
        {
            _layerVisibility = visibility == FalsumChartLayer.None
                ? FalsumChartLayer.Both
                : visibility;
        }

        public void SetGridEnabled(bool enabled)
        {
            _gridEnabled = enabled;
        }

        public void SetMeasureNumbersEnabled(bool enabled)
        {
            _measureNumbersEnabled = enabled;
        }

        public void SetTrackSpeedEffectsEnabled(bool enabled)
        {
            _trackSpeedEffectsEnabled = enabled;
        }

        public bool TrackSpeedEffectsEnabled
        {
            get { return _trackSpeedEffectsEnabled; }
        }

        public bool MeasureNumbersEnabled
        {
            get { return _measureNumbersEnabled; }
        }

        public void SetChartPreviewMode(bool active)
        {
            if (_chartPreviewMode == active)
            {
                return;
            }
            _chartPreviewMode = active;
            if (active)
            {
                CloseMenu();
                ClearInputFocus();
            }
            BeginChartPreviewChromeTransition(active);
        }

        public Rect GetChartViewportScreenRect()
        {
            const float chartWidthFraction = 0.4f;
            if (_root == null ||
                _topBar == null ||
                _bottomBar == null ||
                _root.layout.height < 1.0f)
            {
                var fallbackWidth =
                    Screen.width * chartWidthFraction;
                return new Rect(
                    (Screen.width - fallbackWidth) * 0.5f,
                    Screen.height * 0.09f,
                    fallbackWidth,
                    Screen.height * 0.82f);
            }
            var rootHeight = _root.layout.height;
            var top = Mathf.Max(
                0.0f,
                MeasureBarHeight(_topBar, 56.0f) +
                _chartPreviewTopOffset);
            var bottom = Mathf.Min(
                rootHeight,
                rootHeight -
                MeasureBarHeight(_bottomBar, 64.0f) +
                _chartPreviewBottomOffset);
            var scaleY = Screen.height /
                         Mathf.Max(1.0f, _root.layout.height);
            var height = Mathf.Max(
                1.0f,
                (bottom - top) * scaleY);
            var yFromBottom = Mathf.Max(
                0.0f,
                Screen.height - bottom * scaleY);
            var chartWidth =
                Screen.width * chartWidthFraction;
            return new Rect(
                (Screen.width - chartWidth) * 0.5f,
                yFromBottom,
                chartWidth,
                height);
        }

        public void SetHistory(int undo, int redo)
        {
            if (_displayedUndoCount == undo &&
                _displayedRedoCount == redo)
            {
                return;
            }
            _displayedUndoCount = undo;
            _displayedRedoCount = redo;
            if (_undoText != null)
            {
                _undoText.text = L.Get(
                    "falsum.editor.history.undoCount",
                    ("count", undo));
            }
            if (_redoText != null)
            {
                _redoText.text = L.Get(
                    "falsum.editor.history.redoCount",
                    ("count", redo));
            }
            if (_undoButton != null)
            {
                _undoButton.SetEnabled(undo > 0);
            }
            if (_redoButton != null)
            {
                _redoButton.SetEnabled(redo > 0);
            }
        }

        public void SetPlaybackRate(double rate)
        {
            if (_mobileShell != null)
            {
                _mobileShell.SetPlaybackRate(rate);
            }
            if (_rateButton == null)
            {
                return;
            }
            if (!double.IsNaN(_displayedPlaybackRate) &&
                Math.Abs(_displayedPlaybackRate - rate) < 0.0001)
            {
                return;
            }
            _displayedPlaybackRate = rate;
            _rateButton.text = L.Get(
                "falsum.editor.playbackRate",
                ("rate", rate.ToString(
                    Math.Abs(rate - 1.0) < 0.0001
                        ? "0.0"
                        : "0.##",
                    Invariant)));
        }

        public void SetSongSpeed(double speed)
        {
        }

        public void SetSpectrumState(
            bool visible,
            bool available,
            bool pending)
        {
            if (_spectrumButton == null)
            {
                return;
            }
            _spectrumButton.EnableInClassList(
                "is-active",
                visible && (available || pending));
            _spectrumButton.SetEnabled(available || pending);
            _spectrumButton.tooltip = pending
                ? L.Get("falsum.editor.spectrum.generating")
                : !available
                    ? L.Get("falsum.editor.spectrum.unavailable")
                    : visible
                        ? L.Get("falsum.editor.spectrum.hide")
                        : L.Get("falsum.editor.spectrum.show");
        }

        public void SetSongInfo(
            FalsumManifest manifest,
            FalsumDifficultyInfo difficulty,
            IList<int> availableDifficultyIndices)
        {
            if (_songInfoCardTitle == null ||
                _songInfoCardArtist == null ||
                _songInfoCardRating == null ||
                _songInfoCardChartDesigner == null)
            {
                return;
            }

            var title = manifest != null &&
                        !string.IsNullOrWhiteSpace(manifest.Title)
                ? manifest.Title.Trim()
                : string.Empty;
            var artist = manifest != null &&
                         !string.IsNullOrWhiteSpace(manifest.Artist)
                ? manifest.Artist.Trim()
                : string.Empty;
            var rating = difficulty != null
                ? difficulty.Rating.ToString(Invariant)
                : string.Empty;
            var chartDesigner = difficulty != null
                ? difficulty.ChartDesigner ?? string.Empty
                : string.Empty;

            _songInfoCardTitle.SetValueWithoutNotify(title);
            _songInfoCardArtist.SetValueWithoutNotify(artist);
            _songInfoCardRating.SetValueWithoutNotify(rating);
            _songInfoCardChartDesigner.SetValueWithoutNotify(
                chartDesigner);
            ApplySongInfoTextFont(_songInfoCardTitle);
            ApplySongInfoTextFont(_songInfoCardArtist);
            ApplySongInfoTextFont(_songInfoCardChartDesigner);
            _songInfoCardDifficultyIndex =
                difficulty != null ? difficulty.Index : -1;
            _songInfoCardAvailableDifficulties.Clear();
            if (availableDifficultyIndices != null)
            {
                for (var i = 0;
                     i < availableDifficultyIndices.Count;
                     i++)
                {
                    _songInfoCardAvailableDifficulties.Add(
                        availableDifficultyIndices[i]);
                }
            }
            Array.Clear(
                _songInfoCardDifficultyEnabled,
                0,
                _songInfoCardDifficultyEnabled.Length);
            if (manifest != null)
            {
                for (var i = 0;
                     i < manifest.Difficulties.Count;
                     i++)
                {
                    var item = manifest.Difficulties[i];
                    if (item.Index >= 0 &&
                        item.Index <
                        _songInfoCardDifficultyEnabled.Length)
                    {
                        _songInfoCardDifficultyEnabled[item.Index] =
                            item.Available;
                    }
                }
            }
            HideSongInfoDifficultyMenu();
            SetSongInfoCardBackdrop(
                difficulty != null ? difficulty.Index : 3);
        }

        public void SetSongInfoCover(byte[] coverBytes)
        {
            if (_songInfoCardCover == null)
            {
                return;
            }

            Texture2D texture = null;
            if (coverBytes != null && coverBytes.Length > 0)
            {
                try
                {
                    texture = new Texture2D(
                        2,
                        2,
                        TextureFormat.RGBA32,
                        false);
                    if (ImageConversion.LoadImage(
                            texture,
                            coverBytes,
                            false))
                    {
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                    }
                    else
                    {
                        FalsumPlatform.DestroyObject(texture);
                        texture = null;
                    }
                }
                catch (Exception exception)
                {
                    if (texture != null)
                    {
                        FalsumPlatform.DestroyObject(texture);
                    }
                    texture = null;
                    Debug.LogWarning(
                        "[Falsum] Song info cover could not be loaded: " +
                        exception.Message);
                }
            }

            if (texture == null)
            {
                _songInfoCardCover.style.backgroundImage =
                    new StyleBackground();
                var fallback = Resources.Load<Texture2D>(
                    DefaultCoverPath);
                if (fallback != null)
                {
                    _songInfoCardCover.style.backgroundImage =
                        new StyleBackground(fallback);
                }
                if (_songInfoCardCoverTexture != null)
                {
                    FalsumPlatform.DestroyObject(
                        _songInfoCardCoverTexture);
                    _songInfoCardCoverTexture = null;
                }
                return;
            }

            if (_songInfoCardCoverTexture != null)
            {
                FalsumPlatform.DestroyObject(
                    _songInfoCardCoverTexture);
            }
            _songInfoCardCoverTexture = texture;
            _songInfoCardCover.style.backgroundImage =
                new StyleBackground(texture);
        }

        public void SetSongProgress(float progress)
        {
            if (_songInfoCardProgressFill == null)
            {
                return;
            }
            var normalized = Mathf.Clamp01(progress);
            if (Math.Abs(_songInfoCardProgress - normalized) < 0.0005f)
            {
                return;
            }
            _songInfoCardProgress = normalized;
            _songInfoCardProgressFill.style.width =
                Length.Percent(normalized * 100.0f);
        }

        public string DebugSongInfoCardLayout
        {
            get
            {
                if (_root == null || _songInfoCardViewport == null ||
                    _songInfoCard == null ||
                    _songInfoCardCover == null ||
                    _songInfoCardRating == null)
                {
                    return "none";
                }
                var root = _root.worldBound;
                var viewport = _songInfoCardViewport.worldBound;
                var card = _songInfoCard.worldBound;
                var cover = _songInfoCardCover.worldBound;
                var rating = _songInfoCardRating.worldBound;
                return "root=" + root.width.ToString("F1", Invariant) +
                       "x" + root.height.ToString("F1", Invariant) +
                       " viewport=" + viewport.x.ToString("F1", Invariant) +
                       "," + viewport.y.ToString("F1", Invariant) +
                       "," + viewport.width.ToString("F1", Invariant) +
                       "," + viewport.height.ToString("F1", Invariant) +
                       " card=" + card.x.ToString("F1", Invariant) +
                       "," + card.y.ToString("F1", Invariant) +
                       "," + card.width.ToString("F1", Invariant) +
                       "," + card.height.ToString("F1", Invariant) +
                       " cover=" + cover.x.ToString("F1", Invariant) +
                       "," + cover.y.ToString("F1", Invariant) +
                       "," + cover.width.ToString("F1", Invariant) +
                       "," + cover.height.ToString("F1", Invariant) +
                       " rating=" + rating.x.ToString("F1", Invariant) +
                       "," + rating.y.ToString("F1", Invariant) +
                       "," + rating.width.ToString("F1", Invariant) +
                       "," + rating.height.ToString("F1", Invariant);
            }
        }

        public void DebugShowSongInfoDifficultyMenu()
        {
            ShowSongInfoDifficultyMenu();
        }

        public void DebugClickSongInfoCover()
        {
            OnSongInfoCoverClicked(null);
        }

        public void SetSpeed(float speed)
        {
            if (!float.IsNaN(_displayedSpeed) &&
                Math.Abs(_displayedSpeed - speed) <= 0.001f)
            {
                return;
            }
            _displayedSpeed = speed;
            if (_speedSlider != null &&
                Math.Abs(_speedSlider.value - speed) > 0.001f)
            {
                _speedSlider.SetValueWithoutNotify(speed);
            }
            UpdateSliderFill(_speedSlider, _speedFill);
            if (_speedInput != null && !IsFocused(_speedInput))
            {
                _speedInput.SetValueWithoutNotify(
                    speed.ToString("0.0", Invariant));
            }
        }

        /// <summary>
        /// The delay is no longer edited from the top bar — the audio
        /// preprocess window replaced that field — but the value still has to
        /// round-trip, because a package that has not been preprocessed yet
        /// carries a non-zero stored delay and the tooltips and the singleton
        /// read it.
        /// </summary>
        public void SetDelay(double delayMs)
        {
            if (!double.IsNaN(_displayedDelayMs) &&
                Math.Abs(_displayedDelayMs - delayMs) <= 0.000001)
            {
                return;
            }
            _displayedDelayMs = delayMs;
        }

        public FalsumAudioPreprocessView AudioPreprocessView
        {
            get { return _audioPreprocessView; }
        }

        public void OpenAudioPreprocess()
        {
            Raise(AudioPreprocessRequested);
        }

        /// <summary>
        /// Writes a value into one of the phone layout's editable fields.
        ///
        /// The mobile fields are separate TextFields from the desktop ones
        /// (the desktop bars are hidden, not reused), so every setter that
        /// feeds a desktop field has to feed the mobile one too. Skipping this
        /// is why the phone BPM / subdivision / time boxes rendered empty:
        /// they were bound for input but nothing ever pushed a value in.
        ///
        /// The focus guard matches the desktop guards: overwriting the field
        /// the user is currently typing into would erase their edit mid-entry.
        /// </summary>
        /// <summary>
        /// Copies the current desktop field values into the phone fields.
        /// Called when the shell mounts, because the editor builds the chart
        /// and the desktop fields BEFORE the phone layout exists.
        /// </summary>
        private void SyncMobileFieldsFromDesktop()
        {
            if (_mobileShell == null)
            {
                return;
            }
            if (_baseBpmInput != null)
            {
                SetMobileFieldValue("MobileBaseBpm", _baseBpmInput.value);
            }
            if (_subdivisionInput != null)
            {
                SetMobileFieldValue(
                    "MobileSubdivision",
                    _subdivisionInput.value);
            }
            if (_timeInput != null)
            {
                SetMobileFieldValue("MobileTimeField", _timeInput.value);
            }
            if (!double.IsNaN(_displayedPlaybackRate))
            {
                _mobileShell.SetPlaybackRate(_displayedPlaybackRate);
            }
            else
            {
                _mobileShell.SetPlaybackRate(1.0);
            }
        }

        private void SetMobileFieldValue(string elementName, string value)
        {
            if (_mobileShell == null)
            {
                return;
            }
            var field = _root.Q<TextField>(elementName);
            if (field == null || IsFocused(field))
            {
                return;
            }
            if (field.value != value)
            {
                field.SetValueWithoutNotify(value);
            }
        }

        public void SetBaseBpm(double bpm)
        {
            if (!double.IsNaN(_displayedBaseBpm) &&
                Math.Abs(_displayedBaseBpm - bpm) <= 0.000001)
            {
                return;
            }
            _displayedBaseBpm = bpm;
            if (_baseBpmInput != null && !IsFocused(_baseBpmInput))
            {
                _baseBpmInput.SetValueWithoutNotify(
                    bpm.ToString("0.###", Invariant));
            }
            // The phone layout has its own editable BPM field. Pushing from
            // the same setter keeps the two in lockstep: a value that arrives
            // from a chart load or a preferences apply must appear in both.
            SetMobileFieldValue(
                "MobileBaseBpm",
                bpm.ToString("0.###", Invariant));
        }

        public void SetSubdivision(int subdivision)
        {
            _mobileShell?.SetGridSubdivision(subdivision);
            if (_displayedSubdivision == subdivision)
            {
                return;
            }
            _displayedSubdivision = subdivision;
            if (_subdivisionInput != null &&
                !IsFocused(_subdivisionInput))
            {
                _subdivisionInput.SetValueWithoutNotify(
                    subdivision.ToString(Invariant));
            }
            SetMobileFieldValue(
                "MobileSubdivision",
                subdivision.ToString(Invariant));
        }

        public void SetTime(
            double timeMs,
            double durationMs,
            bool updateText = true,
            bool deferSliderValue = false)
        {
            _timelineDurationMs = Math.Max(0.001, durationMs);
            var textChanged = updateText &&
                              (double.IsNaN(_lastDisplayedTimeMs) ||
                               Math.Abs(
                                   _lastDisplayedTimeMs -
                                   timeMs) >= 0.5 ||
                               double.IsNaN(
                                   _lastDisplayedDurationMs) ||
                               Math.Abs(
                                   _lastDisplayedDurationMs -
                                   durationMs) >= 0.5);
            if (textChanged)
            {
                _lastDisplayedTimeMs = timeMs;
                _lastDisplayedDurationMs = durationMs;
                if (_timeInput != null && !IsFocused(_timeInput))
                {
                    _timeInput.SetValueWithoutNotify(
                        timeMs.ToString("0", Invariant));
                }
                SetMobileFieldValue(
                    "MobileTimeField",
                    timeMs.ToString("0", Invariant));
            }
            // Phone scrubber: same normalized position, plus the readout the
            // desktop shows next to its timeline.
            if (_mobileShell != null)
            {
                _mobileShell.SetProgress(
                    (float)(timeMs / _timelineDurationMs),
                    timeMs.ToString("0", Invariant) + "s");
            }
            if (_timelineSlider != null)
            {
                var normalized = Mathf.Clamp01(
                    (float)(timeMs / _timelineDurationMs));
                if (double.IsNaN(_lastTimelineSliderValue) ||
                    Math.Abs(
                        _lastTimelineSliderValue -
                        normalized) > 0.000001)
                {
                    _lastTimelineSliderValue = normalized;
                    // Pointer-move cost: BaseSlider.SetValueWithoutNotify
                    // always calls UpdateDragElementPosition(), and that helper
                    // reads dragElement.resolvedStyle and dragContainer.layout
                    // size, so every call resolves the panel's style pass synchronously.
                    // At this threshold that happens on every playback frame,
                    // which means any :hover change the mouse just made (and
                    // any transition it started) is flushed and paid for right
                    // here - the perf window then blames UI sync whenever the
                    // pointer moves. While playing, drive the two visuals
                    // straight from the computed value instead; the Slider's
                    // own value is only needed for a user drag, so it is
                    // pushed again as soon as playback stops.
                    UpdateTimelineVisuals(normalized);
                    _timelineSliderValueStale = true;
                    if (!deferSliderValue)
                    {
                        SyncTimelineSliderValue(normalized);
                    }
                }
            }
            if (textChanged && _progressText != null)
            {
                _progressText.text =
                    ToTimeText(timeMs) +
                    " / " +
                    ToTimeText(durationMs);
            }
        }

        /// <summary>
        /// Pushes the playback position into the real
        /// <see cref="Slider"/>, so that clamping, keyboard stepping and
        /// dragging behave as before. Only called when the deferred value is
        /// actually needed (playback stopped, or a pointer interaction landed
        /// on the timeline).
        /// </summary>
        private void SyncTimelineSliderValue(double normalized)
        {
            if (_timelineSlider == null || !_timelineSliderValueStale)
            {
                return;
            }
            _timelineSliderValueStale = false;
            _updatingTimeline = true;
            _timelineSlider.SetValueWithoutNotify(
                Mathf.Clamp01((float)normalized));
            _updatingTimeline = false;
            // BaseSlider rewrote the thumb translate from the value it just
            // received; drop our cached offset so the next tick re-applies the
            // authoritative position instead of believing it is already there.
            _timelineThumbLeft = float.NaN;
        }

        /// <summary>
        /// Re-applies the deferred slider value before any code path that
        /// relies on <see cref="Slider.value"/> being current.
        /// </summary>
        public void FlushTimelineSliderValue()
        {
            if (_timelineSliderValueStale &&
                !double.IsNaN(_lastTimelineSliderValue))
            {
                SyncTimelineSliderValue(_lastTimelineSliderValue);
            }
        }

        public void SetHint(string text)
        {
            if (_hintPanel == null || _hintText == null)
            {
                return;
            }
            var visible = !string.IsNullOrWhiteSpace(text);
            if (_displayedHintVisible == visible &&
                string.Equals(
                    _displayedHint,
                    text,
                    StringComparison.Ordinal))
            {
                return;
            }
            _displayedHintVisible = visible;
            _displayedHint = text;
            SetVisible(_hintPanel, visible);
            if (visible)
            {
                _hintText.text = text;
            }
        }

        /// <summary>
        /// Renders only the current mode name in the top bar. The operation
        /// tutorial (<paramref name="hint"/>) and the alternative-mode listing
        /// (<paramref name="alternatives"/>) are recorded but never displayed:
        /// the top bar is an indicator, not a manual.
        /// </summary>
        public void SetMiddleEditStatus(
            string modeLabel,
            string hint,
            string alternatives)
        {
            _middleEditHintText = hint ?? string.Empty;
            _middleEditAlternativesText = alternatives ?? string.Empty;
            if (_middleEditModeLabel != null)
            {
                _middleEditModeLabel.text =
                    modeLabel ?? string.Empty;
            }
            ApplyMiddleEditStatusVisibility();
        }

        /// <summary>
        /// "mode|hint|alternatives". Only the first field is on screen now; the
        /// other two are the last values the runtime handed over, kept so the
        /// capture/self-check flow can still verify the runtime refreshes all
        /// three when the mode changes.
        /// </summary>
        public string DebugMiddleEditStatus
        {
            get
            {
                return (_middleEditModeLabel != null
                           ? _middleEditModeLabel.text
                           : string.Empty) +
                       "|" +
                       _middleEditHintText +
                       "|" +
                       _middleEditAlternativesText;
            }
        }

        public void SetSelectionFilterState(
            bool groundOnly,
            bool skyOnly)
        {
            _selectGroundOnly = groundOnly;
            _selectSkyOnly = skyOnly;
        }

        public void ShowNewChartDialog(string targetPath)
        {
            if (_modalLayer == null || _newChartDialogTemplate == null)
            {
                return;
            }
            CloseNewChartDialog(true);
            _newChartFocusBefore = GetFocusedElement();
            _newChartTargetPath = targetPath ?? string.Empty;
            _newChartAudioPath = string.Empty;
            _newChartCoverPath = string.Empty;
            _newChartDirty = false;
            _newChartDiscardFocusBefore = null;
            _newChartActiveDifficulty = 0;
            for (var i = 0; i < 4; i++)
            {
                _newChartDifficultyEnabled[i] = i == 0;
                _newChartDesigners[i] = string.Empty;
            }
            _newChartRatings[0] = "1";
            _newChartRatings[1] = "5";
            _newChartRatings[2] = "9";
            _newChartRatings[3] = "11";

            var instance = _newChartDialogTemplate.Instantiate();
            L.ApplyStaticText(instance);
            _newChartOverlay = Require<VisualElement>(
                instance,
                "NewChartOverlay");
            _newChartPanel = Require<VisualElement>(
                instance,
                "NewChartPanel");
            _newChartCoverPreview = Require<VisualElement>(
                instance,
                "NewChartCoverPreview");
            _newChartCoverPrompt = Require<Label>(
                instance,
                "NewChartCoverPrompt");
            _newChartAudioName = Require<Label>(
                instance,
                "NewChartAudioName");
            _newChartCoverName = Require<Label>(
                instance,
                "NewChartCoverName");
            _newChartDifficultyIndex = Require<Label>(
                instance,
                "NewChartDifficultyIndex");
            _newChartChartFile = Require<Label>(
                instance,
                "NewChartChartFile");
            _newChartValidation = Require<Label>(
                instance,
                "NewChartValidation");
            _newChartBusy = Require<VisualElement>(
                instance,
                "NewChartBusy");
            _newChartBusyText = Require<Label>(
                instance,
                "NewChartBusyText");
            _newChartDifficultyFields = Require<VisualElement>(
                instance,
                "NewChartDifficultyFields");
            _newChartTitle = Require<TextField>(
                instance,
                "NewChartTitle");
            _newChartSongId = Require<TextField>(
                instance,
                "NewChartSongId");
            // Left empty on purpose: the song id is a mandatory field the
            // author must type, so pre-filling it from the file name would
            // make "required" meaningless.
            _newChartSongId.value = string.Empty;
            _newChartArtist = Require<TextField>(
                instance,
                "NewChartArtist");
            _newChartJacketDesigner = Require<TextField>(
                instance,
                "NewChartJacketDesigner");
            _newChartRating = Require<TextField>(
                instance,
                "NewChartRating");
            _newChartChartDesigner = Require<TextField>(
                instance,
                "NewChartChartDesigner");
            var newChartContent = Require<ScrollView>(
                instance,
                "NewChartContent");
            newChartContent.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            newChartContent.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _newChartBrowseAudio = Require<Button>(
                instance,
                "NewChartBrowseAudio");
            _newChartBrowseCover = Require<Button>(
                instance,
                "NewChartBrowseCover");
            _newChartCreate = Require<Button>(
                instance,
                "NewChartCreate");
            _newChartCancel = Require<Button>(
                instance,
                "NewChartCancel");
            _newChartClose = Require<Button>(
                instance,
                "NewChartClose");
            _newChartDiscardConfirm = Require<VisualElement>(
                instance,
                "NewChartDiscardConfirm");
            _newChartDiscardContinue = Require<Button>(
                instance,
                "NewChartDiscardContinue");
            _newChartDiscardConfirmButton = Require<Button>(
                instance,
                "NewChartDiscardConfirmButton");
            L.Bind(_newChartCreate, "falsum.newChart.create");
            L.Bind(_newChartCancel, "falsum.common.cancel");
            L.BindTooltip(_newChartClose, "falsum.common.close");
            L.Bind(
                _newChartDiscardContinue,
                "falsum.newChart.discard.continue");
            L.Bind(
                _newChartDiscardConfirmButton,
                "falsum.newChart.discard.confirm");
            Require<Label>(
                instance,
                "NewChartPath").text =
                string.IsNullOrWhiteSpace(_newChartTargetPath)
                    ? L.Get("falsum.newChart.savePathUnselected")
                    : L.Get(
                        "falsum.newChart.savePath",
                        ("path", _newChartTargetPath));
            _newChartTitle.textEdition.placeholder =
                L.Get("falsum.newChart.titlePlaceholder");
            _newChartSongId.textEdition.placeholder =
                L.Get("falsum.newChart.songIdPlaceholder");
            _newChartArtist.textEdition.placeholder =
                L.Get("falsum.newChart.artistPlaceholder");
            _newChartJacketDesigner.textEdition.placeholder =
                L.Get("falsum.newChart.jacketPlaceholder");
            _newChartRating.textEdition.placeholder =
                L.Get("falsum.newChart.rating");
            _newChartChartDesigner.textEdition.placeholder =
                L.Get("falsum.newChart.chartDesignerPlaceholder");
            _newChartBrowseAudio.text =
                L.Get("falsum.newChart.chooseAudio");
            _newChartBrowseCover.tooltip = L.Get(
                "falsum.newChart.chooseCoverTooltip");

            _newChartDifficultyButtons = new Button[4];
            _newChartDifficultyToggles = new Toggle[4];
            _newChartDifficultyTabs = new VisualElement[4];
            _newChartDifficultyStates = new Label[4];
            for (var i = 0; i < 4; i++)
            {
                _newChartDifficultyButtons[i] = Require<Button>(
                    instance,
                    "NewChartDiffButton" + i);
                L.Bind(
                    _newChartDifficultyButtons[i],
                    i == 0
                        ? "falsum.difficulty.min"
                        : i == 1
                            ? "falsum.difficulty.evo"
                            : i == 2
                                ? "falsum.difficulty.ult"
                                : "falsum.difficulty.fbd");
                _newChartDifficultyToggles[i] = Require<Toggle>(
                    instance,
                    "NewChartDiffToggle" + i);
                _newChartDifficultyTabs[i] = Require<VisualElement>(
                    instance,
                    "NewChartDiffTab" + i);
                _newChartDifficultyStates[i] = Require<Label>(
                    instance,
                    "NewChartDiffState" + i);
            }

            BindNewChartDialog();
            _modalLayer.Add(_newChartOverlay);
            _modal = _newChartOverlay;
            _newChartOverlay.BringToFront();
            UpdateNewChartDialog();
            _textInputFocusGrantedByPointer = true;
            _newChartSongId.Focus();
            ClearNewChartError();
            ClearNewChartFieldValidation();
            FalsumUiMotion.AnimateModalIn(
                _newChartOverlay,
                _newChartPanel);
        }

        public void CloseNewChartDialog()
        {
            CloseNewChartDialog(false);
        }

        private void CloseNewChartDialog(bool immediate)
        {
            if (_newChartOverlay == null)
            {
                return;
            }
            if (_newChartClosing && !immediate)
            {
                return;
            }
            if (!immediate &&
                _newChartOverlay.panel != null &&
                _newChartPanel != null)
            {
                HideNewChartDiscardConfirmation();
                _newChartClosing = true;
                var overlay = _newChartOverlay;
                var panel = _newChartPanel;
                FalsumUiMotion.AnimateModalOut(
                    overlay,
                    panel,
                    FinishCloseNewChartDialog);
                return;
            }
            FinishCloseNewChartDialog();
        }

        private void FinishCloseNewChartDialog()
        {
            if (_newChartOverlay == null)
            {
                return;
            }
            if (_modal == _newChartOverlay)
            {
                _modal = null;
            }
            var focusTarget = _newChartFocusBefore;
            _textInputFocusGrantedByPointer = false;
            _newChartOverlay.RemoveFromHierarchy();
            _newChartOverlay = null;
            _newChartPanel = null;
            if (_newChartPreviewTexture != null)
            {
                FalsumPlatform.DestroyObject(
                    _newChartPreviewTexture);
                _newChartPreviewTexture = null;
            }
            _newChartBusy = null;
            _newChartCoverPrompt = null;
            _newChartDifficultyFields = null;
            _newChartCreate = null;
            _newChartCancel = null;
            _newChartClose = null;
            _newChartDiscardConfirm = null;
            _newChartDiscardContinue = null;
            _newChartDiscardConfirmButton = null;
            _newChartBusyState = false;
            _newChartDirty = false;
            _newChartClosing = false;
            _newChartDiscardFocusBefore = null;
            _newChartDifficultyButtons = null;
            _newChartDifficultyToggles = null;
            _newChartDifficultyTabs = null;
            _newChartDifficultyStates = null;
            _newChartFocusBefore = null;
            RestoreFocus(focusTarget);
        }

        public void ShowPreferences(
            FalsumPreferences preferences,
            FalsumChartMetadataDraft metadata,
            FalsumPackage package)
        {
            if (_preferencesView == null)
            {
                return;
            }
            CloseMenu();
            CloseMobileNumericEntry();
            _preferencesFocusBefore = GetFocusedElement();
            _preferencesView.Show(
                preferences,
                metadata,
                package,
                _songInfoCardCoverTexture);
        }

        /// <summary>
        /// Shows the standalone chart-info panel (document scope). Replaces the
        /// former in-dialog Chart tab; the song-info cover is the entry point.
        /// </summary>
        public void ShowChartInfo(
            FalsumChartMetadataDraft metadata,
            FalsumPackage package)
        {
            if (_preferencesView == null)
            {
                return;
            }
            CloseMenu();
            ClosePreferences();
            _preferencesFocusBefore = GetFocusedElement();
            _preferencesView.ShowChartInfo(
                metadata,
                package);
        }

        public void CloseChartInfo()
        {
            if (_preferencesView != null)
            {
                _preferencesView.CloseChartInfo();
            }
        }

        public bool IsChartInfoOpen
        {
            get
            {
                return _preferencesView != null &&
                       _preferencesView.IsChartInfoOpen;
            }
        }

        /// <summary>Forwarded from the chart info panel's Done button.</summary>
        public event Action<FalsumChartMetadataDraft, FalsumPackage>
            ChartInfoCommitRequested;

        /// <summary>Forwarded when the chart info panel closes without confirming.</summary>
        public event Action ChartInfoCancelRequested;

        /// <summary>Shows an error inside the chart info panel.</summary>
        public void ShowChartInfoError(string message)
        {
            if (_preferencesView != null)
            {
                _preferencesView.ShowChartInfoError(message);
            }
        }

        public void RefreshChartInfoPanel()
        {
            if (_preferencesView != null)
            {
                _preferencesView.RefreshChartInfoPanel();
            }
        }

        public void SetToolCatalog(
            IList<FalsumToolManifest> tools)
        {
            _toolCatalog.Clear();
            if (tools != null)
            {
                for (var i = 0; i < tools.Count; i++)
                {
                    _toolCatalog.Add(tools[i]);
                }
            }
            if (_toolDialog != null && _toolDialog.IsOpen)
            {
                _toolDialog.SetTools(_toolCatalog);
            }
        }

        public void ShowToolManager()
        {
            if (_toolDialog == null)
            {
                return;
            }
            CloseMenu();
            ClosePreferences();
            _toolDialog.ShowManager(_toolCatalog);
        }

        public void ShowTool(
            FalsumToolManifest manifest,
            FalsumToolFormDefinition form)
        {
            if (_toolDialog == null || manifest == null ||
                form == null)
            {
                return;
            }
            CloseMenu();
            ClosePreferences();
            _toolDialog.ShowTool(manifest, form);
        }

        public void CloseToolDialog()
        {
            if (_toolDialog != null)
            {
                _toolDialog.Close();
            }
        }

        public void ShowToolReport(FalsumToolReport report)
        {
            if (_toolReportView == null || report == null)
            {
                return;
            }
            CloseMenu();
            _toolReportView.Show(report);
        }

        public void CloseToolReport()
        {
            if (_toolReportView != null)
            {
                _toolReportView.Close();
            }
        }

        public string DebugToolReportSummary
        {
            get
            {
                if (_toolReportView == null)
                {
                    return "missing";
                }
                return "open=" + _toolReportView.IsOpen +
                       " modal=" + IsToolReportOpen +
                       " floating=" +
                       _toolReportView.IsFloating +
                       " items=" +
                       _toolReportView.DebugItemCount;
            }
        }

        public bool DebugRequestFirstToolReportLocate()
        {
            return _toolReportView != null &&
                   _toolReportView.DebugRequestFirstLocate();
        }

        public bool DebugRequestLastToolReportLocate()
        {
            return _toolReportView != null &&
                   _toolReportView.DebugRequestLastLocate();
        }

        public bool DebugRequestToolReportLocateByTitle(
            string title)
        {
            return _toolReportView != null &&
                   _toolReportView.DebugRequestLocateByTitle(
                       title);
        }

        public void DebugScrollToolReportToBottom()
        {
            if (_toolReportView != null)
            {
                _toolReportView.DebugScrollItemsToBottom();
            }
        }

        public void ClosePreferences()
        {
            var focusTarget = _preferencesFocusBefore;
            _preferencesFocusBefore = null;
            if (_preferencesView != null)
            {
                _preferencesView.Close();
            }
            RestoreFocus(focusTarget);
        }

        private void TogglePerformanceWindowFromAbout()
        {
            if (_performanceWindow == null)
            {
                return;
            }
            if (_performanceWindow.IsOpen)
            {
                _performanceWindow.Close();
                return;
            }
            ClosePreferences();
            _performanceWindow.Open();
        }

        public void SetNewChartAudio(string path)
        {
            _newChartAudioPath = path ?? string.Empty;
            _newChartDirty = true;
            if (_newChartBrowseAudio != null)
            {
                _newChartBrowseAudio.text =
                    string.IsNullOrWhiteSpace(_newChartAudioPath)
                        ? L.Get("falsum.newChart.chooseAudio")
                        : L.Get(
                            "falsum.newChart.audioSelected",
                            ("file", Path.GetFileName(
                                _newChartAudioPath)));
            }
            if (_newChartAudioName != null)
            {
                _newChartAudioName.text =
                    string.IsNullOrWhiteSpace(_newChartAudioPath)
                        ? string.Empty
                        : Path.GetFileName(_newChartAudioPath);
            }
            ClearNewChartError();
        }

        public void SetNewChartCover(string path)
        {
            _newChartCoverPath = path ?? string.Empty;
            _newChartDirty = true;
            if (_newChartCoverName != null)
            {
                _newChartCoverName.text =
                    string.IsNullOrWhiteSpace(_newChartCoverPath)
                        ? L.Get(
                            "falsum.newChart.defaultCoverHint")
                        : Path.GetFileName(_newChartCoverPath);
            }
            if (_newChartCoverPrompt != null)
            {
                SetVisible(
                    _newChartCoverPrompt,
                    string.IsNullOrWhiteSpace(_newChartCoverPath));
            }
            LoadNewChartCoverPreview();
            ClearNewChartError();
        }

        public void SetNewChartBusy(bool busy, string message)
        {
            _newChartBusyState = busy;
            if (_newChartBusy != null)
            {
                SetVisible(_newChartBusy, busy);
            }
            if (busy && _newChartBusyText != null)
            {
                _newChartBusyText.text =
                    string.IsNullOrWhiteSpace(message)
                        ? L.Get("falsum.newChart.preparing")
                        : message;
            }
            if (_newChartCreate != null)
            {
                _newChartCreate.SetEnabled(!busy);
            }
            if (_newChartCancel != null)
            {
                _newChartCancel.SetEnabled(!busy);
            }
            if (_newChartClose != null)
            {
                _newChartClose.SetEnabled(!busy);
            }
            if (_newChartDiscardConfirmButton != null)
            {
                _newChartDiscardConfirmButton.SetEnabled(!busy);
            }
            if (_newChartBrowseAudio != null)
            {
                _newChartBrowseAudio.SetEnabled(!busy);
            }
            if (_newChartBrowseCover != null)
            {
                _newChartBrowseCover.SetEnabled(!busy);
            }
        }

        public void SetNewChartError(string message)
        {
            SetNewChartBusy(false, null);
            if (_newChartValidation == null)
            {
                return;
            }
            _newChartValidation.text = message ?? string.Empty;
            SetVisible(_newChartValidation, true);
            _newChartValidation.AddToClassList("is-error");
        }

        private void BindNewChartDialog()
        {
            _newChartBrowseAudio.clicked += delegate
            {
                Raise(BrowseNewChartAudioRequested);
            };
            _newChartBrowseCover.clicked += delegate
            {
                Raise(BrowseNewChartCoverRequested);
            };
            _newChartCreate.clicked += delegate
            {
                var request = BuildNewChartRequest();
                if (request != null)
                {
                    Raise(NewChartCreateRequested, request);
                }
            };
            _newChartCancel.clicked += RequestCloseNewChartDialog;
            _newChartClose.clicked += RequestCloseNewChartDialog;
            _newChartDiscardContinue.clicked +=
                HideNewChartDiscardConfirmation;
            _newChartDiscardConfirmButton.clicked += delegate
            {
                _newChartDirty = false;
                CloseNewChartDialog();
            };
            _newChartOverlay.RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    if (evt.target == _newChartOverlay &&
                        !_newChartBusyState)
                    {
                        RequestCloseNewChartDialog();
                    }
                });
            _newChartOverlay.RegisterCallback<KeyDownEvent>(
                OnNewChartKeyDown,
                TrickleDown.TrickleDown);
            _newChartRating.RegisterValueChangedCallback(
                evt =>
                {
                    if (_newChartUpdating)
                    {
                        return;
                    }
                    var rating = SanitizeNumericField(
                        _newChartRating,
                        evt.newValue,
                        false);
                    _newChartRatings[_newChartActiveDifficulty] =
                        rating;
                    _newChartDirty = true;
                    UpdateNewChartDialog();
                    ClearNewChartError();
                });
            _newChartChartDesigner.RegisterValueChangedCallback(
                evt =>
                {
                    if (_newChartUpdating)
                    {
                        return;
                    }
                    _newChartDesigners[_newChartActiveDifficulty] =
                        evt.newValue ?? string.Empty;
                    _newChartDirty = true;
                    ClearNewChartError();
                });
            _newChartSongId.RegisterValueChangedCallback(
                delegate
                {
                    _newChartDirty = true;
                    ClearNewChartError();
                });
            _newChartTitle.RegisterValueChangedCallback(
                delegate
                {
                    _newChartDirty = true;
                    ClearNewChartError();
                });
            _newChartArtist.RegisterValueChangedCallback(
                delegate
                {
                    _newChartDirty = true;
                    ClearNewChartError();
                });
            _newChartJacketDesigner.RegisterValueChangedCallback(
                delegate
                {
                    _newChartDirty = true;
                    ClearNewChartError();
                });
            for (var i = 0; i < 4; i++)
            {
                var difficulty = i;
                _newChartDifficultyButtons[i].clicked += delegate
                {
                    SelectNewChartDifficulty(difficulty);
                };
                _newChartDifficultyToggles[i]
                    .RegisterValueChangedCallback(
                        evt =>
                        {
                            if (_newChartUpdating)
                            {
                                return;
                            }
                            StoreNewChartDifficultyFields();
                            _newChartDifficultyEnabled[difficulty] =
                                evt.newValue;
                            _newChartDirty = true;
                            if (evt.newValue)
                            {
                                _newChartActiveDifficulty =
                                    difficulty;
                            }
                            UpdateNewChartDialog();
                            ClearNewChartError();
                        });
            }
        }

        private void RequestCloseNewChartDialog()
        {
            if (_newChartOverlay == null || _newChartBusyState)
            {
                return;
            }
            if (!_newChartDirty)
            {
                CloseNewChartDialog();
                return;
            }
            ShowNewChartDiscardConfirmation();
        }

        private void OnNewChartKeyDown(KeyDownEvent evt)
        {
            if (_newChartOverlay == null)
            {
                return;
            }

            if (IsElementVisible(_newChartDiscardConfirm))
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopImmediatePropagation();
                    HideNewChartDiscardConfirmation();
                    return;
                }
                if (evt.keyCode == KeyCode.Tab)
                {
                    TrapFocus(_newChartDiscardConfirm, evt);
                    return;
                }
                return;
            }

            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopImmediatePropagation();
                RequestCloseNewChartDialog();
                return;
            }

            if (evt.keyCode == KeyCode.Tab)
            {
                TrapFocus(_newChartOverlay, evt);
            }
        }

        private void ShowNewChartDiscardConfirmation()
        {
            if (_newChartDiscardConfirm == null)
            {
                CloseNewChartDialog();
                return;
            }
            _newChartDiscardFocusBefore = GetFocusedElement();
            SetVisible(_newChartDiscardConfirm, true);
            _newChartDiscardConfirm.BringToFront();
            FalsumUiMotion.AnimateWindowIn(
                _newChartDiscardConfirm.Q<VisualElement>(
                    className: "inline-confirm-panel"),
                0.14f,
                0.0f,
                0.97f);
            _newChartDiscardContinue.Focus();
        }

        private void HideNewChartDiscardConfirmation()
        {
            if (_newChartDiscardConfirm == null)
            {
                return;
            }
            SetVisible(_newChartDiscardConfirm, false);
            var focusTarget = _newChartDiscardFocusBefore;
            _newChartDiscardFocusBefore = null;
            RestoreFocus(focusTarget);
        }

        private void SelectNewChartDifficulty(int difficulty)
        {
            if (difficulty == _newChartActiveDifficulty)
            {
                return;
            }
            StoreNewChartDifficultyFields();
            _newChartActiveDifficulty = difficulty;
            UpdateNewChartDialog();
            ClearNewChartError();
        }

        private void StoreNewChartDifficultyFields()
        {
            if (_newChartRating == null ||
                _newChartChartDesigner == null)
            {
                return;
            }
            _newChartRatings[_newChartActiveDifficulty] =
                _newChartRating.value ?? string.Empty;
            _newChartDesigners[_newChartActiveDifficulty] =
                _newChartChartDesigner.value ?? string.Empty;
        }

        private void UpdateNewChartDialog()
        {
            if (_newChartOverlay == null)
            {
                return;
            }
            _newChartUpdating = true;
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    var enabled = _newChartDifficultyEnabled[i];
                    _newChartDifficultyTabs[i].EnableInClassList(
                        "active",
                        i == _newChartActiveDifficulty);
                    _newChartDifficultyTabs[i].EnableInClassList(
                        "enabled",
                        enabled);
                    _newChartDifficultyTabs[i].EnableInClassList(
                        "off",
                        !enabled);
                    _newChartDifficultyToggles[i]
                        .SetValueWithoutNotify(enabled);
                    _newChartDifficultyStates[i].text =
                        enabled
                            ? string.IsNullOrWhiteSpace(
                                _newChartDesigners[i])
                                ? L.Get(
                                    "falsum.newChart.difficultyStateEnabled",
                                    ("rating", _newChartRatings[i]))
                                : L.Get(
                                    "falsum.newChart.difficultyStateWithDesigner",
                                    ("rating", _newChartRatings[i]),
                                    ("designer", _newChartDesigners[i]))
                            : L.Get(
                                "falsum.newChart.difficultyStateDisabled");
                }
                _newChartRating.SetValueWithoutNotify(
                    _newChartRatings[_newChartActiveDifficulty]);
                _newChartChartDesigner.SetValueWithoutNotify(
                    _newChartDesigners[_newChartActiveDifficulty]);
                _newChartDifficultyIndex.text =
                    L.Get(
                        "falsum.newChart.difficultyIndex",
                        ("difficulty", DifficultyDisplayName(
                            _newChartActiveDifficulty)),
                        ("index", _newChartActiveDifficulty));
                _newChartDifficultyFields.EnableInClassList(
                    "new-chart-diff-min",
                    _newChartActiveDifficulty == 0);
                _newChartDifficultyFields.EnableInClassList(
                    "new-chart-diff-evo",
                    _newChartActiveDifficulty == 1);
                _newChartDifficultyFields.EnableInClassList(
                    "new-chart-diff-ult",
                    _newChartActiveDifficulty == 2);
                _newChartDifficultyFields.EnableInClassList(
                    "new-chart-diff-fbd",
                    _newChartActiveDifficulty == 3);
                _newChartChartFile.text =
                    L.Get(
                        "falsum.newChart.chartFile",
                        ("file", FalsumManifest.DifficultyNames[
                            _newChartActiveDifficulty]
                            .ToLowerInvariant()));
                var activeEnabled =
                    _newChartDifficultyEnabled[
                        _newChartActiveDifficulty];
                _newChartRating.SetEnabled(activeEnabled);
                _newChartChartDesigner.SetEnabled(activeEnabled);
            }
            finally
            {
                _newChartUpdating = false;
            }
        }

        private void ClearNewChartError()
        {
            ClearNewChartFieldValidation();
            if (_newChartValidation != null)
            {
                _newChartValidation.text = string.Empty;
                _newChartValidation.RemoveFromClassList("is-error");
                SetVisible(_newChartValidation, false);
            }
            if (_newChartCreate != null)
            {
                _newChartCreate.SetEnabled(!_newChartBusyState);
            }
        }

        private void ClearNewChartFieldValidation()
        {
            if (_newChartBrowseAudio != null)
            {
                _newChartBrowseAudio.RemoveFromClassList("is-invalid");
            }
            if (_newChartCoverPreview != null)
            {
                _newChartCoverPreview.RemoveFromClassList("is-invalid");
            }
            if (_newChartTitle != null)
            {
                _newChartTitle.RemoveFromClassList("is-invalid");
            }
            if (_newChartSongId != null)
            {
                _newChartSongId.RemoveFromClassList("is-invalid");
            }
            if (_newChartArtist != null)
            {
                _newChartArtist.RemoveFromClassList("is-invalid");
            }
            if (_newChartRating != null)
            {
                _newChartRating.RemoveFromClassList("is-invalid");
            }
        }

        private static void MarkInvalid(VisualElement element)
        {
            if (element != null)
            {
                element.AddToClassList("is-invalid");
            }
        }

        private bool TryValidateNewChart(out string error)
        {
            ClearNewChartFieldValidation();
            if (string.IsNullOrWhiteSpace(_newChartTargetPath))
            {
                error = L.Get(
                    "falsum.error.newChart.savePathRequired");
                return false;
            }
            if (string.IsNullOrWhiteSpace(_newChartAudioPath) ||
                !File.Exists(_newChartAudioPath))
            {
                error = L.Get(
                    "falsum.error.newChart.audioRequired");
                MarkInvalid(_newChartBrowseAudio);
                return false;
            }
            if (!string.IsNullOrWhiteSpace(_newChartCoverPath) &&
                !File.Exists(_newChartCoverPath))
            {
                error = L.Get(
                    "falsum.error.newChart.coverInvalid");
                MarkInvalid(_newChartCoverPreview);
                return false;
            }
            if (_newChartTitle == null ||
                string.IsNullOrWhiteSpace(_newChartTitle.value))
            {
                error = L.Get(
                    "falsum.error.newChart.titleRequired");
                MarkInvalid(_newChartTitle);
                return false;
            }
            if (_newChartArtist == null ||
                string.IsNullOrWhiteSpace(_newChartArtist.value))
            {
                error = L.Get(
                    "falsum.error.newChart.artistRequired");
                MarkInvalid(_newChartArtist);
                return false;
            }
            var songId = _newChartSongId == null
                ? string.Empty
                : (_newChartSongId.value ?? string.Empty).Trim();
            if (songId.Length == 0)
            {
                error = L.Get(
                    "falsum.error.newChart.songIdRequired");
                MarkInvalid(_newChartSongId);
                return false;
            }
            if (!FalsumManifest.IsValidNewSongId(songId))
            {
                error = L.Get(
                    "falsum.error.newChart.songIdInvalid");
                MarkInvalid(_newChartSongId);
                return false;
            }
            var enabledCount = 0;
            for (var i = 0; i < 4; i++)
            {
                if (!_newChartDifficultyEnabled[i])
                {
                    continue;
                }
                enabledCount++;
                int rating;
                if (!int.TryParse(
                        _newChartRatings[i],
                        NumberStyles.Integer,
                        Invariant,
                        out rating) ||
                    rating < 1 ||
                    rating > 99)
                {
                    StoreNewChartDifficultyFields();
                    _newChartActiveDifficulty = i;
                    UpdateNewChartDialog();
                    MarkInvalid(_newChartRating);
                    error = L.Get(
                        "falsum.error.newChart.ratingRange",
                        ("difficulty", DifficultyDisplayName(i)));
                    return false;
                }
            }
            if (enabledCount == 0)
            {
                error = L.Get(
                    "falsum.error.newChart.difficultyRequired");
                return false;
            }
            error = string.Empty;
            return true;
        }

        private FalsumNewChartRequest BuildNewChartRequest()
        {
            StoreNewChartDifficultyFields();
            string error;
            if (!TryValidateNewChart(out error))
            {
                SetNewChartError(error);
                return null;
            }
            ClearNewChartError();
            var request = new FalsumNewChartRequest
            {
                TargetPath = _newChartTargetPath,
                AudioPath = _newChartAudioPath,
                CoverPath = _newChartCoverPath ?? string.Empty,
                SongId = (_newChartSongId.value ?? string.Empty).Trim(),
                Title = _newChartTitle.value.Trim(),
                Artist = _newChartArtist.value.Trim(),
                JacketDesigner = _newChartJacketDesigner.value.Trim(),
            };
            for (var i = 0; i < 4; i++)
            {
                request.DifficultyEnabled[i] =
                    _newChartDifficultyEnabled[i];
                request.Ratings[i] = int.Parse(
                    _newChartRatings[i],
                    NumberStyles.Integer,
                    Invariant);
                request.ChartDesigners[i] =
                    (_newChartDesigners[i] ?? string.Empty).Trim();
            }
            return request;
        }

        private string EnabledDifficultySummary()
        {
            var names = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                if (_newChartDifficultyEnabled[i])
                {
                    names.Add(DifficultyDisplayName(i));
                }
            }
            return string.Join(
                L.Get("falsum.listSeparator.slash"),
                names.ToArray());
        }

        private void LoadNewChartCoverPreview()
        {
            if (_newChartPreviewTexture != null)
            {
                FalsumPlatform.DestroyObject(
                    _newChartPreviewTexture);
                _newChartPreviewTexture = null;
            }
            if (_newChartCoverPreview == null)
            {
                return;
            }
            _newChartCoverPreview.style.backgroundImage =
                new StyleBackground();
            if (string.IsNullOrWhiteSpace(_newChartCoverPath))
            {
                var fallback = Resources.Load<Texture2D>(
                    DefaultCoverPath);
                if (fallback != null)
                {
                    _newChartCoverPreview.style.backgroundImage =
                        new StyleBackground(fallback);
                }
                return;
            }
            if (!File.Exists(_newChartCoverPath))
            {
                return;
            }
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            try
            {
                if (!ImageConversion.LoadImage(
                        texture,
                        File.ReadAllBytes(_newChartCoverPath),
                        false))
                {
                    FalsumPlatform.DestroyObject(texture);
                    return;
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                _newChartPreviewTexture = texture;
                _newChartCoverPreview.style.backgroundImage =
                    new StyleBackground(texture);
            }
            catch (Exception exception)
            {
                FalsumPlatform.DestroyObject(texture);
                SetNewChartError(
                    L.Get(
                        "falsum.error.newChart.coverPreviewFailed",
                        ("error", exception.Message)));
            }
        }

        public bool DebugDisclaimerOpen
        {
            get { return _disclaimerOverlay != null; }
        }

        public bool DebugDisclaimerMandatory
        {
            get { return _disclaimerMandatory; }
        }

        public string DebugDisclaimerText
        {
            get
            {
                var label = _disclaimerOverlay != null
                    ? _disclaimerOverlay.Q<Label>(
                        "DisclaimerText")
                    : null;
                return label != null
                    ? label.text
                    : string.Empty;
            }
        }

        public void DebugAcceptDisclaimer()
        {
            if (_disclaimerOverlay == null)
            {
                return;
            }
            CloseDisclaimer(_disclaimerAccepted);
        }

        public void DebugDeclineDisclaimer()
        {
            if (_disclaimerOverlay == null)
            {
                return;
            }
            CloseDisclaimer(_disclaimerDeclined);
        }

        public bool DebugCloseDisclaimer()
        {
            if (_disclaimerOverlay == null ||
                _disclaimerMandatory)
            {
                return false;
            }
            CloseDisclaimer(null);
            return true;
        }

        public void ShowDisclaimer(
            bool mandatory,
            Action accepted,
            Action declined)
        {
            if (_disclaimerOverlay != null ||
                _disclaimerClosing)
            {
                return;
            }
            if (_modalLayer == null ||
                _disclaimerDialogTemplate == null)
            {
                Debug.LogError(
                    "[Falsum] Disclaimer dialog is unavailable.");
                if (mandatory)
                {
                    FalsumPlatform.Quit();
                }
                return;
            }

            _disclaimerFocusBefore = GetFocusedElement();
            _disclaimerMandatory = mandatory;
            _disclaimerAccepted = accepted;
            _disclaimerDeclined = declined;
            var instance =
                _disclaimerDialogTemplate.Instantiate();
            L.ApplyStaticText(instance);
            var overlay = Require<VisualElement>(
                instance,
                "DisclaimerOverlay");
            var panel = Require<VisualElement>(
                instance,
                "DisclaimerPanel");
            var eyebrow = Require<Label>(
                instance,
                "DisclaimerEyebrow");
            var close = Require<Button>(
                instance,
                "DisclaimerClose");
            L.BindTooltip(
                close,
                "falsum.disclaimer.closeTooltip");
            var content = Require<ScrollView>(
                instance,
                "DisclaimerContent");
            var text = Require<Label>(
                instance,
                "DisclaimerText");
            var footerNote = Require<Label>(
                instance,
                "DisclaimerFooterNote");
            var decline = Require<Button>(
                instance,
                "DisclaimerDecline");
            var accept = Require<Button>(
                instance,
                "DisclaimerAccept");

            eyebrow.text = mandatory
                ? L.Get("falsum.disclaimer.eyebrow")
                : L.Get("falsum.disclaimer.aboutEyebrow");
            text.text = FalsumEditorInfo.DisclaimerBody;
            footerNote.text = mandatory
                ? L.Get(
                    "falsum.disclaimer.mandatoryFooter",
                    ("product", FalsumEditorInfo.ProductName))
                : L.Get("falsum.disclaimer.aboutFooter");
            accept.text = mandatory
                ? L.Get("falsum.disclaimer.accept")
                : L.Get("falsum.common.close");
            decline.text = L.Get("falsum.disclaimer.decline");
            SetVisible(close, !mandatory);
            SetVisible(decline, mandatory);
            content.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            content.verticalScrollerVisibility =
                ScrollerVisibility.Auto;
            content.scrollOffset = Vector2.zero;

            close.clicked += delegate
            {
                if (!_disclaimerMandatory)
                {
                    CloseDisclaimer(null);
                }
            };
            decline.clicked += delegate
            {
                CloseDisclaimer(_disclaimerDeclined);
            };
            accept.clicked += delegate
            {
                CloseDisclaimer(_disclaimerAccepted);
            };
            overlay.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode == KeyCode.Escape)
                    {
                        evt.StopImmediatePropagation();
                        if (!_disclaimerMandatory)
                        {
                            CloseDisclaimer(null);
                        }
                    }
                    else if (evt.keyCode == KeyCode.Tab)
                    {
                        TrapFocus(overlay, evt);
                    }
                },
                TrickleDown.TrickleDown);

            overlay.RemoveFromClassList("hidden");
            _disclaimerOverlay = overlay;
            _disclaimerPanel = panel;
            _modalLayer.Add(overlay);
            overlay.BringToFront();
            FalsumUiMotion.AnimateModalIn(overlay, panel);
            accept.Focus();
        }

        private void CloseDisclaimerImmediate()
        {
            if (_disclaimerOverlay != null)
            {
                _disclaimerOverlay.RemoveFromHierarchy();
            }
            ClearDisclaimerState(false);
        }

        private void CloseDisclaimer(Action afterClosed)
        {
            if (_disclaimerClosing)
            {
                return;
            }
            if (_disclaimerOverlay == null ||
                _disclaimerPanel == null)
            {
                ClearDisclaimerState(true);
                if (afterClosed != null)
                {
                    afterClosed();
                }
                return;
            }

            _disclaimerClosing = true;
            var overlay = _disclaimerOverlay;
            FalsumUiMotion.AnimateModalOut(
                overlay,
                _disclaimerPanel,
                delegate
                {
                    if (overlay.parent != null)
                    {
                        overlay.RemoveFromHierarchy();
                    }
                    ClearDisclaimerState(true);
                    if (afterClosed != null)
                    {
                        afterClosed();
                    }
                });
        }

        private void ClearDisclaimerState(bool restoreFocus)
        {
            var focusTarget = _disclaimerFocusBefore;
            _disclaimerFocusBefore = null;
            _disclaimerAccepted = null;
            _disclaimerDeclined = null;
            _disclaimerOverlay = null;
            _disclaimerPanel = null;
            _disclaimerMandatory = false;
            _disclaimerClosing = false;
            if (restoreFocus)
            {
                RestoreFocus(focusTarget);
            }
        }

        public void ShowConfirm(string message, Action confirmed)
        {
            ShowConfirm(
                message,
                L.Get("falsum.common.cancel"),
                L.Get("falsum.common.confirm"),
                confirmed,
                null,
                null);
        }

        public void ShowAlert(string message, string dismissText = null)
        {
            ShowConfirm(message, delegate { });
            if (_modal == null)
            {
                return;
            }
            var cancel = _modal.Q<Button>("ConfirmCancel");
            if (cancel != null)
            {
                cancel.AddToClassList("hidden");
            }
            var accept = _modal.Q<Button>("ConfirmAccept");
            if (accept != null)
            {
                accept.text = string.IsNullOrWhiteSpace(dismissText)
                    ? L.Get("falsum.common.acknowledge")
                    : dismissText;
                accept.Focus();
            }
        }

        public void ShowConfirm(
            string message,
            string cancelText,
            string acceptText,
            Action confirmed,
            string alternativeText,
            Action alternative,
            Action cancelled = null)
        {
            if (_modalLayer == null || _confirmDialogTemplate == null)
            {
                return;
            }
            _confirmFocusBefore = GetFocusedElement();
            if (_modal != null)
            {
                _modal.RemoveFromHierarchy();
            }

            var instance = _confirmDialogTemplate.Instantiate();
            L.ApplyStaticText(instance);
            var overlay = Require<VisualElement>(
                instance,
                "ConfirmOverlay");
            var panel = Require<VisualElement>(
                instance,
                "ConfirmPanel");
            _confirmPanel = panel;
            var text = Require<Label>(
                instance,
                "ConfirmMessage");
            var cancel = Require<Button>(
                instance,
                "ConfirmCancel");
            var alternativeButton = Require<Button>(
                instance,
                "ConfirmAlternative");
            var accept = Require<Button>(
                instance,
                "ConfirmAccept");
            text.text = message ?? string.Empty;
            cancel.text = cancelText ?? L.Get("falsum.common.cancel");
            accept.text = acceptText ?? L.Get("falsum.common.confirm");
            _confirmCancelAction = cancelled;
            if (string.IsNullOrWhiteSpace(alternativeText))
            {
                alternativeButton.AddToClassList("hidden");
            }
            else
            {
                alternativeButton.RemoveFromClassList("hidden");
                alternativeButton.text = alternativeText;
            }
            cancel.clicked += delegate
            {
                CancelConfirmDialog();
            };
            alternativeButton.clicked += delegate
            {
                CloseConfirmDialog(alternative);
            };
            accept.clicked += delegate
            {
                CloseConfirmDialog(confirmed);
            };
            _modalLayer.Add(overlay);
            _modal = overlay;
            overlay.BringToFront();
            FalsumUiMotion.AnimateModalIn(overlay, panel);
            overlay.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode == KeyCode.Escape)
                    {
                        evt.StopImmediatePropagation();
                        CancelConfirmDialog();
                    }
                    else if (evt.keyCode == KeyCode.Tab)
                    {
                        TrapFocus(overlay, evt);
                    }
                },
                TrickleDown.TrickleDown);
            cancel.Focus();
        }

        private void CloseConfirmDialog(Action afterClosed)
        {
            if (_confirmClosing)
            {
                return;
            }
            if (_modal == null || _confirmPanel == null)
            {
                if (_modal != null)
                {
                    _modal.RemoveFromHierarchy();
                    _modal = null;
                }
                _confirmCancelAction = null;
                _confirmPanel = null;
                if (afterClosed != null)
                {
                    afterClosed();
                }
                return;
            }
            _confirmClosing = true;
            var overlay = _modal;
            FalsumUiMotion.AnimateModalOut(
                overlay,
                _confirmPanel,
                delegate
                {
                    if (overlay.parent != null)
                    {
                        overlay.RemoveFromHierarchy();
                    }
                    if (_modal == overlay)
                    {
                        _modal = null;
                    }
                    _confirmClosing = false;
                    _confirmCancelAction = null;
                    _confirmPanel = null;
                    var focusTarget = _confirmFocusBefore;
                    _confirmFocusBefore = null;
                    RestoreFocus(focusTarget);
                    if (afterClosed != null)
                    {
                        afterClosed();
                    }
                });
        }

        private void CancelConfirmDialog()
        {
            var cancel = _confirmCancelAction;
            CloseConfirmDialog(cancel);
        }

        public void ShowProperties(ChartNote note)
        {
            _propertyRefreshCount++;
            ClearPropertyRows();
            if (_propertyPanel == null)
            {
                return;
            }
            SetVisible(_propertyPanel, note != null);
            if (note == null)
            {
                if (_mobileShell != null)
                {
                    _mobileShell.LayoutPropertyPanel();
                }
                return;
            }

            _propertyHeader.text = L.Get(
                "falsum.editor.propertyHeader",
                ("id", note.Id.ToString("D3", Invariant)),
                ("kind", NoteKindLabel(note.Kind)));

            if (note.Kind == NoteKind.Tap)
            {
                AddPropertyRow(
                    "time_ms",
                    L.Get("falsum.property.time"),
                    note.TimeMs);
                AddPropertyRow(
                    "width_lanes",
                    L.Get("falsum.property.width"),
                    note.WidthLanes);
                AddPropertyRow(
                    "start_lane",
                    L.Get("falsum.property.lane"),
                    note.StartLane);
            }
            else if (note.Kind == NoteKind.Hold)
            {
                AddPropertyRow(
                    "time_ms",
                    L.Get("falsum.property.startTime"),
                    note.TimeMs);
                AddPropertyRow(
                    "end_time_ms",
                    L.Get("falsum.property.endTime"),
                    note.EndTimeMs);
                AddPropertyRow(
                    "width_lanes",
                    L.Get("falsum.property.width"),
                    note.WidthLanes);
                AddPropertyRow(
                    "start_lane",
                    L.Get("falsum.property.lane"),
                    note.StartLane);
            }
            else if (note.Kind == NoteKind.Flick)
            {
                double position;
                double width;
                var cells = FalsumFraction.NormalizePair(
                    note.StartPos,
                    note.StartWidth,
                    note.StartCellCount,
                    out position,
                    out width);
                AddPropertyRow(
                    "time_ms",
                    L.Get("falsum.property.time"),
                    note.TimeMs);
                AddPropertyRow(
                    "flick_position",
                    L.Get("falsum.property.position"),
                    L.Get(
                        "falsum.property.fraction",
                        ("value", Math.Round(position * cells).ToString(
                            "0",
                            Invariant)),
                        ("cells", cells)));
                AddPropertyRow(
                    "flick_width",
                    L.Get("falsum.property.width"),
                    L.Get(
                        "falsum.property.fraction",
                        ("value", Math.Round(width * cells).ToString(
                            "0",
                            Invariant)),
                        ("cells", cells)));
                AddFlickDirectionRow(
                    "flick_direction",
                    L.Get("falsum.property.direction"),
                    note.FlickDirection);
            }
            else
            {
                double startPosition;
                double startWidth;
                double endPosition;
                double endWidth;
                var startCells = FalsumFraction.NormalizePair(
                    note.StartPos,
                    note.StartWidth,
                    note.StartCellCount,
                    out startPosition,
                    out startWidth);
                var endCells = FalsumFraction.NormalizePair(
                    note.EndPos,
                    note.EndWidth,
                    note.EndCellCount,
                    out endPosition,
                    out endWidth);
                AddPropertyRow(
                    "time_ms",
                    L.Get("falsum.property.startTime"),
                    note.TimeMs);
                AddPropertyRow(
                    "end_time_ms",
                    L.Get("falsum.property.endTime"),
                    note.EndTimeMs);
                AddPropertyRow(
                    "sky_start",
                    L.Get("falsum.property.startPosition"),
                    L.Get(
                        "falsum.property.fraction",
                        ("value", Math.Round(startPosition * startCells).ToString(
                            "0",
                            Invariant)),
                        ("cells", startCells)));
                AddPropertyRow(
                    "sky_start_width",
                    L.Get("falsum.property.startWidth"),
                    L.Get(
                        "falsum.property.fraction",
                        ("value", Math.Round(startWidth * startCells).ToString(
                            "0",
                            Invariant)),
                        ("cells", startCells)));
                AddPropertyRow(
                    "sky_end",
                    L.Get("falsum.property.endPosition"),
                    L.Get(
                        "falsum.property.fraction",
                        ("value", Math.Round(endPosition * endCells).ToString(
                            "0",
                            Invariant)),
                        ("cells", endCells)));
                AddPropertyRow(
                    "sky_end_width",
                    L.Get("falsum.property.endWidth"),
                    L.Get(
                        "falsum.property.fraction",
                        ("value", Math.Round(endWidth * endCells).ToString(
                            "0",
                            Invariant)),
                        ("cells", endCells)));
                AddEaseDropdown(
                    "left_ease",
                    L.Get("falsum.property.leftEasing"),
                    note.LeftEasing);
                AddEaseDropdown(
                    "right_ease",
                    L.Get("falsum.property.rightEasing"),
                    note.RightEasing);
            }

            RefreshPropertyPanelHeight();
        }

        private void RefreshPropertyPanelHeight()
        {
            if (_propertyPanel == null) return;
            if (_mobileShell != null)
            {
                _mobileShell.LayoutPropertyPanel();
                return;
            }
            var compact = _uiStyle == FalsumUiStyle.InFalsus;
            _propertyPanel.style.height = compact
                ? Mathf.Clamp(40.0f + _propertyRows.Count * 28.0f + 34.0f, 110.0f, 440.0f)
                : Mathf.Clamp(86.0f + _propertyRows.Count * 36.0f + 44.0f, 220.0f, 600.0f);
        }

        public void ShowEvents(
            IList<ChartEvent> events,
            bool laneEventsEnabled)
        {
            _eventRefreshCount++;
            _laneEventsEnabled = laneEventsEnabled;
            _events.Clear();
            if (events != null)
            {
                for (var i = 0; i < events.Count; i++)
                {
                    if (events[i] != null)
                    {
                        _events.Add(events[i]);
                    }
                }
            }
            if (!_laneEventsEnabled &&
                _eventFilter == EventFilter.Lane)
            {
                _eventFilter = EventFilter.All;
            }
            _eventFilterLane.SetEnabled(_laneEventsEnabled);
            _addLaneEventButton.SetEnabled(_laneEventsEnabled);
            RenderEventRows();
        }

        public void SelectEvent(ChartEvent chartEvent)
        {
            if (chartEvent != null &&
                !MatchesEventFilter(chartEvent))
            {
                _eventFilter = EventFilterForKind(chartEvent.Kind);
            }
            _selectedEvent = chartEvent;
            RenderEventRows();
        }

        public void ShowEventValidation(
            ChartEvent chartEvent,
            string message)
        {
            if (_eventValidation == null ||
                chartEvent == null ||
                !ReferenceEquals(_selectedEvent, chartEvent))
            {
                return;
            }
            _eventValidation.text = message ?? string.Empty;
            _eventValidation.EnableInClassList("hidden",
                _mobileShell != null && string.IsNullOrWhiteSpace(message));
        }

        private void RenderEventRows()
        {
            if (_eventRowsView == null)
            {
                return;
            }

            UpdateEventFilterState();
            var visible = VisibleEvents();
            if (_eventCount != null)
            {
                _eventCount.text =
                    visible.Count == _events.Count
                        ? _events.Count.ToString(Invariant)
                        : L.Get(
                            "falsum.editor.eventCountFiltered",
                            ("visible", visible.Count),
                            ("total", _events.Count));
            }
            if (_selectedEvent == null ||
                !visible.Contains(_selectedEvent))
            {
                _selectedEvent = visible.Count > 0
                    ? visible[0]
                    : null;
            }

            _eventRowsView.contentContainer.Clear();
            _eventRows.Clear();
            _eventValidation = null;
            if (visible.Count == 0)
            {
                var empty = new Label(
                    L.Get("falsum.editor.eventsEmpty"));
                empty.AddToClassList("event-empty");
                _eventRowsView.contentContainer.Add(empty);
                return;
            }

            var selectedRow = default(VisualElement);
            for (var i = 0; i < visible.Count; i++)
            {
                var chartEvent = visible[i];
                var selected = ReferenceEquals(
                    chartEvent,
                    _selectedEvent);
                var row = CreateEventRow(chartEvent, selected);
                _eventRowsView.contentContainer.Add(row);
                _eventRows.Add(row);
                if (selected)
                {
                    selectedRow = row;
                }
            }
            if (selectedRow != null)
            {
                var shouldFocus = _focusEventRowOnNextRender;
                _focusEventRowOnNextRender = false;
                _eventRowsView.schedule.Execute(
                    (Action)delegate
                    {
                        if (selectedRow.parent != null)
                        {
                            _eventRowsView.ScrollTo(selectedRow);
                            if (shouldFocus)
                            {
                                selectedRow
                                    .Q<VisualElement>(
                                        className: "event-summary")
                                    ?.Focus();
                            }
                        }
                    }).ExecuteLater(1);
            }
            else
            {
                _focusEventRowOnNextRender = false;
            }
        }

        private VisualElement CreateEventRow(
            ChartEvent chartEvent,
            bool selected)
        {
            var row = new VisualElement();
            row.AddToClassList("event-row");
            row.AddToClassList(TypeClass(chartEvent.Kind));
            if (selected)
            {
                row.AddToClassList("is-selected");
            }

            var summary = new VisualElement();
            summary.AddToClassList("event-summary");
            summary.focusable = true;
            summary.delegatesFocus = true;
            summary.RegisterCallback<PointerDownEvent>(
                evt =>
                {
                    if (evt.button == 0 &&
                        !ReferenceEquals(_selectedEvent, chartEvent))
                    {
                        _focusEventRowOnNextRender = true;
                        SelectEvent(chartEvent);
                    }
                    summary.Focus();
                });
            summary.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode != KeyCode.Return &&
                        evt.keyCode != KeyCode.KeypadEnter &&
                        evt.keyCode != KeyCode.Space)
                    {
                        return;
                    }
                    evt.StopImmediatePropagation();
                    _focusEventRowOnNextRender = true;
                    SelectEvent(chartEvent);
                });

            var type = new Label(KindLabel(chartEvent));
            type.AddToClassList("event-type");
            var time = new Label(
                chartEvent.IsChartBase
                    ? "—"
                    : L.Get(
                        "falsum.editor.eventTime",
                        ("time", FormatEventNumber(chartEvent.TimeMs))));
            time.AddToClassList("event-time");
            var value = new Label(EventSummaryValue(chartEvent));
            value.AddToClassList("event-value");
            var locate = new Button(
                delegate
                {
                    SelectEvent(chartEvent);
                    Raise(EventJumpRequested, chartEvent);
                })
            {
                text = L.Get("falsum.action.locate"),
            };
            locate.AddToClassList("event-locate");
            locate.RegisterCallback<PointerDownEvent>(
                evt => evt.StopPropagation());
            summary.Add(type);
            summary.Add(time);
            summary.Add(value);
            if (!chartEvent.IsChartBase)
            {
                summary.Add(locate);
            }
            row.Add(summary);

            if (selected)
            {
                row.Add(CreateEventEditor(chartEvent));
            }
            return row;
        }

        private VisualElement CreateEventEditor(ChartEvent chartEvent)
        {
            var editor = new VisualElement();
            editor.AddToClassList("event-editor");
            var fields = new VisualElement();
            fields.AddToClassList("event-fields");
            editor.Add(fields);

            TextField timeField = null;
            if (!chartEvent.IsChartBase)
            {
                timeField = AddEventTextField(
                    fields,
                    L.Get("falsum.property.time"),
                    FormatEventNumber(chartEvent.TimeMs),
                    false);
            }
            TextField bpmField = null;
            TextField divisionField = null;
            TextField multiplierField = null;
            EventLaneSelection laneSelection = null;
            EventStatusSelection statusSelection = null;

            if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                bpmField = AddEventTextField(
                    fields,
                    "BPM",
                    FormatEventNumber(chartEvent.Bpm),
                    false);
                divisionField = AddEventTextField(
                    fields,
                    L.Get("falsum.label.division"),
                    FormatEventNumber(chartEvent.Division),
                    true);
            }
            else if (chartEvent.Kind == ChartEventKind.TrackSpeed)
            {
                multiplierField = AddEventTextField(
                    fields,
                    L.Get("falsum.label.multiplier"),
                    FormatEventNumber(chartEvent.Multiplier),
                    true);
            }
            else
            {
                laneSelection = AddEventLaneField(
                    fields,
                    chartEvent.Lane,
                    false);
                statusSelection = AddEventStatusField(
                    fields,
                    chartEvent.Enabled,
                    true);
            }

            var validation = new Label();
            validation.AddToClassList("event-validation");
            validation.EnableInClassList("hidden", _mobileShell != null);
            editor.Add(validation);
            _eventValidation = validation;

            var actions = new VisualElement();
            actions.AddToClassList("event-actions");
            if (!chartEvent.IsChartBase)
            {
                AddEventAction(
                    actions,
                    L.Get("falsum.action.locate"),
                    null,
                    delegate { Raise(EventJumpRequested, chartEvent); });
                AddEventAction(
                    actions,
                    L.Get("falsum.action.copy"),
                    null,
                    delegate { Raise(EventDuplicateRequested, chartEvent); });
                AddEventAction(
                    actions,
                    L.Get("falsum.action.delete"),
                    "is-danger",
                    delegate { Raise(EventDeleteRequested, chartEvent); });
            }
            AddEventAction(
                actions,
                L.Get("falsum.action.apply"),
                "is-apply",
                delegate
                {
                    Raise(
                        EventSubmitted,
                        new FalsumEventEditSubmission
                        {
                            Target = chartEvent,
                            Time = timeField != null
                                ? SanitizeNumericField(
                                    timeField,
                                    timeField.value,
                                    false)
                                : string.Empty,
                            Bpm = bpmField != null
                                ? SanitizeNumericField(
                                    bpmField,
                                    bpmField.value,
                                    false)
                                : string.Empty,
                            Division = divisionField != null
                                ? SanitizeNumericField(
                                    divisionField,
                                    divisionField.value,
                                    false)
                                : string.Empty,
                            Multiplier = multiplierField != null
                                ? SanitizeNumericField(
                                    multiplierField,
                                    multiplierField.value,
                                    false)
                                : string.Empty,
                            Lane = laneSelection != null
                                ? laneSelection.Value
                                : chartEvent.Lane,
                            Enabled = statusSelection != null
                                ? statusSelection.Value
                                : chartEvent.Enabled,
                        });
                });
            editor.Add(actions);
            return editor;
        }

        private TextField AddEventTextField(
            VisualElement parent,
            string label,
            string value,
            bool last)
        {
            var field = new VisualElement();
            field.AddToClassList("event-field");
            if (last)
            {
                field.AddToClassList("is-last");
            }
            var labelElement = new Label(label);
            labelElement.AddToClassList("event-field-label");
            var input = new TextField
            {
                maxLength = 32,
            };
            input.AddToClassList("falsum-field");
            input.AddToClassList("event-input");
            input.SetValueWithoutNotify(value);
            RegisterNumericInput(input, null);
            field.Add(labelElement);
            field.Add(input);
            BindMobileNumericInput(input, label, false);
            parent.Add(field);
            return input;
        }

        private EventLaneSelection AddEventLaneField(
            VisualElement parent,
            int lane,
            bool last)
        {
            var selection = new EventLaneSelection
            {
                Value = lane,
            };
            var field = new VisualElement();
            field.AddToClassList("event-field");
            if (last)
            {
                field.AddToClassList("is-last");
            }
            var labelElement = new Label(
                L.Get("falsum.property.lane"));
            labelElement.AddToClassList("event-field-label");
            var lanes = new VisualElement();
            lanes.AddToClassList("event-lanes");
            var values = new[] { 0, 1, 4, 5 };
            for (var i = 0; i < values.Length; i++)
            {
                var laneValue = values[i];
                var option = new Button(
                    delegate
                    {
                        selection.Value = laneValue;
                        for (var buttonIndex = 0;
                             buttonIndex < selection.Buttons.Count;
                             buttonIndex++)
                        {
                            selection.Buttons[buttonIndex]
                                .EnableInClassList(
                                    "is-active",
                                    buttonIndex ==
                                    IndexOfLane(
                                        selection.Value));
                        }
                    })
                {
                    text = laneValue.ToString(Invariant),
                };
                option.AddToClassList("event-lane-option");
                if (laneValue == 5)
                {
                    option.AddToClassList("is-last");
                }
                option.EnableInClassList(
                    "is-active",
                    laneValue == lane);
                lanes.Add(option);
                selection.Buttons.Add(option);
            }
            field.Add(labelElement);
            field.Add(lanes);
            parent.Add(field);
            return selection;
        }

        private EventStatusSelection AddEventStatusField(
            VisualElement parent,
            bool enabled,
            bool last)
        {
            var selection = new EventStatusSelection
            {
                Value = enabled,
            };
            var field = new VisualElement();
            field.AddToClassList("event-field");
            if (last)
            {
                field.AddToClassList("is-last");
            }
            var labelElement = new Label(
                L.Get("falsum.property.status"));
            labelElement.AddToClassList("event-field-label");
            var options = new VisualElement();
            options.AddToClassList("event-status-toggle");
            var values = new[] { true, false };
            var labels = new[]
            {
                L.Get("falsum.property.enabled"),
                L.Get("falsum.property.disabled"),
            };
            for (var i = 0; i < values.Length; i++)
            {
                var statusValue = values[i];
                var option = new Button(
                    delegate
                    {
                        selection.Value = statusValue;
                        UpdateEventStatusButtons(selection);
                    })
                {
                    text = labels[i],
                };
                option.AddToClassList("event-status-option");
                if (i == values.Length - 1)
                {
                    option.AddToClassList("is-last");
                }
                option.EnableInClassList(
                    "is-active",
                    statusValue == enabled);
                options.Add(option);
                selection.Buttons.Add(option);
            }
            field.Add(labelElement);
            field.Add(options);
            parent.Add(field);
            return selection;
        }

        private static void UpdateEventStatusButtons(
            EventStatusSelection selection)
        {
            for (var i = 0; i < selection.Buttons.Count; i++)
            {
                var buttonEnabled = i == 0;
                selection.Buttons[i].EnableInClassList(
                    "is-active",
                    buttonEnabled == selection.Value);
            }
        }

        private static int IndexOfLane(int lane)
        {
            if (lane == 1)
            {
                return 1;
            }
            if (lane == 4)
            {
                return 2;
            }
            if (lane == 5)
            {
                return 3;
            }
            return 0;
        }

        private static void AddEventAction(
            VisualElement parent,
            string text,
            string modifierClass,
            Action action)
        {
            var button = new Button(action)
            {
                text = text,
            };
            button.AddToClassList("event-action");
            if (!string.IsNullOrEmpty(modifierClass))
            {
                button.AddToClassList(modifierClass);
            }
            parent.Add(button);
        }

        private void UpdateEventFilterState()
        {
            _eventFilterAll.EnableInClassList(
                "is-active",
                _eventFilter == EventFilter.All);
            _eventFilterBpm.EnableInClassList(
                "is-active",
                _eventFilter == EventFilter.Bpm);
            _eventFilterTrack.EnableInClassList(
                "is-active",
                _eventFilter == EventFilter.Track);
            _eventFilterLane.EnableInClassList(
                "is-active",
                _eventFilter == EventFilter.Lane);
        }

        private List<ChartEvent> VisibleEvents()
        {
            var visible = new List<ChartEvent>();
            for (var i = 0; i < _events.Count; i++)
            {
                if (MatchesEventFilter(_events[i]))
                {
                    visible.Add(_events[i]);
                }
            }
            visible.Sort(CompareEvents);
            return visible;
        }

        private bool MatchesEventFilter(ChartEvent chartEvent)
        {
            if (chartEvent == null)
            {
                return false;
            }
            if (_eventFilter == EventFilter.All)
            {
                return true;
            }
            return chartEvent.Kind == KindForFilter(_eventFilter);
        }

        private static int CompareEvents(
            ChartEvent left,
            ChartEvent right)
        {
            if (left.IsChartBase != right.IsChartBase)
            {
                return left.IsChartBase ? -1 : 1;
            }
            var time = left.TimeMs.CompareTo(right.TimeMs);
            return time != 0
                ? time
                : left.Kind.CompareTo(right.Kind);
        }

        private static EventFilter EventFilterForKind(
            ChartEventKind kind)
        {
            if (kind == ChartEventKind.Bpm)
            {
                return EventFilter.Bpm;
            }
            if (kind == ChartEventKind.TrackSpeed)
            {
                return EventFilter.Track;
            }
            return EventFilter.Lane;
        }

        private static ChartEventKind KindForFilter(EventFilter filter)
        {
            if (filter == EventFilter.Bpm)
            {
                return ChartEventKind.Bpm;
            }
            if (filter == EventFilter.Track)
            {
                return ChartEventKind.TrackSpeed;
            }
            return ChartEventKind.LaneLayout;
        }

        private static string TypeClass(ChartEventKind kind)
        {
            if (kind == ChartEventKind.Bpm)
            {
                return "type-bpm";
            }
            return kind == ChartEventKind.TrackSpeed
                ? "type-track"
                : "type-lane";
        }

        private static string KindLabel(ChartEvent chartEvent)
        {
            if (chartEvent.IsChartBase)
            {
                return L.Get("falsum.eventKind.chart");
            }
            if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                return L.Get("falsum.eventKind.bpm");
            }
            return chartEvent.Kind == ChartEventKind.TrackSpeed
                ? L.Get("falsum.eventKind.track")
                : L.Get("falsum.eventKind.lane");
        }

        private static string EventSummaryValue(ChartEvent chartEvent)
        {
            if (chartEvent.IsChartBase)
            {
                return L.Get(
                    "falsum.editor.eventSummary.initialBpm",
                    ("bpm", FormatEventNumber(chartEvent.Bpm)),
                    ("division", FormatEventNumber(
                        chartEvent.Division)));
            }
            if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                return L.Get(
                    "falsum.editor.eventSummary.bpm",
                    ("bpm", FormatEventNumber(chartEvent.Bpm)),
                    ("division", FormatEventNumber(
                        chartEvent.Division)));
            }
            if (chartEvent.Kind == ChartEventKind.TrackSpeed)
            {
                return L.Get(
                    "falsum.editor.eventSummary.track",
                    ("multiplier", FormatEventNumber(
                        chartEvent.Multiplier)));
            }
            return L.Get(
                "falsum.editor.eventSummary.lane",
                ("lane", chartEvent.Lane),
                ("state", L.Get(
                    chartEvent.Enabled
                        ? "falsum.property.enabled"
                        : "falsum.property.disabled")));
        }

        private static string FormatEventNumber(double value)
        {
            return value.ToString("0.###", Invariant);
        }

        private static string DifficultyDisplayName(int index)
        {
            switch (index)
            {
                case 1:
                    return L.Get("falsum.difficulty.evo");
                case 2:
                    return L.Get("falsum.difficulty.ult");
                case 3:
                    return L.Get("falsum.difficulty.fbd");
                default:
                    return L.Get("falsum.difficulty.min");
            }
        }

        private static string NoteKindLabel(NoteKind kind)
        {
            switch (kind)
            {
                case NoteKind.Hold:
                    return L.Get("falsum.noteKind.hold");
                case NoteKind.Flick:
                    return L.Get("falsum.noteKind.flick");
                case NoteKind.SkyArea:
                    return L.Get("falsum.noteKind.skyArea");
                default:
                    return L.Get("falsum.noteKind.tap");
            }
        }

        public bool IsTyping()
        {
            var focused = GetFocusedElement();
            for (var current = focused; current != null;
                 current = current.parent)
            {
                if (current is TextField)
                {
                    return true;
                }
            }
            return false;
        }

        public void ClearInputFocus()
        {
            _textInputFocusGrantedByPointer = false;
            var focused = GetFocusedElement();
            if (focused != null)
            {
                focused.Blur();
            }
        }

        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (_root == null || _root.panel == null)
            {
                return false;
            }
            // PANEL.PICK IS THE SINGLE MOST EXPENSIVE CALL ON THE POINTER PATH.
            // It walks the whole visual tree with real geometry, and the editor
            // asks this question several times per frame (pointer update, anchor
            // hover, mouse handling). While the chart plays, moving the mouse
            // therefore turned a per-frame query into a per-frame tree walk and
            // the editor crawled -- even with no placement mode active.
            //
            // The answer only changes when the pointer moves or the tree
            // changes, so cache it against the last query position. The position
            // is quantised to whole pixels: sub-pixel wobble cannot change which
            // element is under the cursor, and it is exactly the jitter a mouse
            // produces while the user thinks they are holding still.
            var keyX = Mathf.RoundToInt(screenPosition.x);
            var keyY = Mathf.RoundToInt(screenPosition.y);
            // Scoped to ONE frame as well as one position: several callers ask
            // during the same frame (pointer update, anchor hover, mouse
            // handling) and they all deserve the same answer, but nothing may
            // outlive the frame that produced it -- a panel opening or closing
            // changes the tree, and a `Time.frameCount` guard clears the cache
            // without having to hunt down every mutation site.
            var frame = Time.frameCount;
            if (_pointerOverUiValid &&
                frame == _pointerOverUiFrame &&
                keyX == _pointerOverUiX &&
                keyY == _pointerOverUiY)
            {
                return _pointerOverUiResult;
            }
            var panelPoint = ScreenToPanelTopLeft(screenPosition);
            var picked = _root.panel.Pick(panelPoint);
            var overUi = false;
            while (picked != null && picked != _root)
            {
                if (picked.pickingMode == PickingMode.Position)
                {
                    overUi = true;
                    break;
                }
                picked = picked.parent;
            }
            _pointerOverUiValid = true;
            _pointerOverUiFrame = frame;
            _pointerOverUiX = keyX;
            _pointerOverUiY = keyY;
            _pointerOverUiResult = overUi;
            return overUi;
        }

        /// <summary>
        /// Drops the cached hit test, for callers that know they just changed
        /// the visual tree mid-frame.
        /// </summary>
        public void InvalidatePointerOverUi()
        {
            _pointerOverUiValid = false;
        }

        public bool IsPointerOverFloatingToolReport(
            Vector2 screenPosition)
        {
            if (!IsToolReportFloating ||
                _toolReportView == null ||
                _root == null ||
                _root.panel == null)
            {
                return false;
            }
            var panelPoint = ScreenToPanelTopLeft(
                screenPosition);
            var picked = _root.panel.Pick(panelPoint);
            return _toolReportView.ContainsElement(picked);
        }

        public Vector2 DebugScreenToPanel(Vector2 screenPosition)
        {
            return _root != null && _root.panel != null
                ? RuntimePanelUtils.ScreenToPanel(
                    _root.panel,
                    screenPosition)
                : Vector2.zero;
        }

        public Vector2 DebugScreenToPanelTopLeft(
            Vector2 screenPosition)
        {
            var raw = DebugScreenToPanel(screenPosition);
            return new Vector2(
                raw.x,
                DebugPanelSize.y - raw.y);
        }

        public Vector2 DebugPanelSize
        {
            get { return _root != null ? _root.layout.size : Vector2.zero; }
        }

        public string DebugPickNamedAncestor(Vector2 screenPosition)
        {
            if (_root == null || _root.panel == null)
            {
                return "none";
            }
            var panelPoint = ScreenToPanelTopLeft(screenPosition);
            var picked = _root.panel.Pick(panelPoint);
            var names = new List<string>();
            for (var current = picked; current != null;
                 current = current.parent)
            {
                if (!string.IsNullOrEmpty(current.name))
                {
                    names.Add(current.name);
                }
            }
            return names.Count > 0
                ? string.Join("/", names.ToArray())
                : "none";
        }

        public string DebugRunMenuChecks()
        {
            if (_fileMenuButton == null)
            {
                return "fail no-menu-button";
            }
            ToggleMenu(
                "FileMenu",
                _fileMenuButton,
                CreateFileMenuEntries());
            var open = _activeMenu == "FileMenu" &&
                       _menuPanel != null;
            var itemCount = 0;
            if (_menuPanel != null)
            {
                _menuPanel.Query<Button>(
                    className: "menu-item-button").ForEach(
                    delegate { itemCount++; });
            }
            CloseMenu();
            return open && itemCount > 0
                ? "pass items=" + itemCount
                : "fail open=" + open +
                  " items=" + itemCount;
        }

        public string DebugRunEditMenuChecks()
        {
            if (_editMenuButton == null)
            {
                return "fail no-edit-button";
            }
            SetSelectionFilterState(true, false);
            ToggleMenu(
                "EditMenu",
                _editMenuButton,
                CreateEditMenuEntries());
            var itemCount = 0;
            var hasCopy = false;
            var hasPaste = false;
            var hasCut = false;
            var hasMirror = false;
            var hasMirrorFrontBack = false;
            var hasCheckedGroundFilter = false;
            var hasCopyShortcut = false;
            var hasMirrorShortcut = false;
            var hasMirrorFrontBackShortcut = false;
            var copyLabel = L.Get("falsum.menu.edit.copy");
            var pasteLabel = L.Get("falsum.menu.edit.paste");
            var cutLabel = L.Get("falsum.menu.edit.cut");
            var mirrorLabel = L.Get("falsum.menu.edit.mirror");
            var mirrorFrontBackLabel =
                L.Get("falsum.menu.edit.mirrorFrontBack");
            var checkedGroundLabel = L.Get(
                "falsum.menu.checkedItem",
                ("label", L.Get("falsum.menu.edit.selectGround")));
            if (_menuPanel != null)
            {
                _menuPanel.Query<Button>(
                    className: "menu-item-button").ForEach(
                    button =>
                    {
                        itemCount++;
                        var label = button.Q<Label>(
                            "MenuItemLabel");
                        var shortcut = button.Q<Label>(
                            "MenuItemShortcut");
                        var labelText = label != null
                            ? label.text
                            : string.Empty;
                        hasCopy |= labelText == copyLabel;
                        hasPaste |= labelText == pasteLabel;
                        hasCut |= labelText == cutLabel;
                        hasMirror |= labelText == mirrorLabel;
                        hasMirrorFrontBack |=
                            labelText == mirrorFrontBackLabel;
                        hasCheckedGroundFilter |=
                            labelText == checkedGroundLabel;
                        hasCopyShortcut |=
                            labelText == copyLabel &&
                            shortcut != null &&
                            !string.IsNullOrWhiteSpace(
                                shortcut.text);
                        hasMirrorShortcut |=
                            labelText == mirrorLabel &&
                            shortcut != null &&
                            !string.IsNullOrWhiteSpace(
                                shortcut.text);
                        hasMirrorFrontBackShortcut |=
                            labelText == mirrorFrontBackLabel &&
                            shortcut != null &&
                            !string.IsNullOrWhiteSpace(
                                shortcut.text);
                    });
            }
            CloseMenu();
            SetSelectionFilterState(false, false);
            return itemCount == 7 &&
                   hasCopy &&
                   hasPaste &&
                   hasCut &&
                   hasMirror &&
                   hasMirrorFrontBack &&
                   hasCheckedGroundFilter &&
                   hasCopyShortcut &&
                   hasMirrorShortcut &&
                   hasMirrorFrontBackShortcut
                ? "pass items=" + itemCount
                : "fail items=" + itemCount +
                  " copy=" + hasCopy +
                  " paste=" + hasPaste +
                  " cut=" + hasCut +
                  " mirror=" + hasMirror +
                  " frontBack=" + hasMirrorFrontBack +
                  " checked=" + hasCheckedGroundFilter +
                  " copyShortcut=" + hasCopyShortcut +
                  " mirrorShortcut=" + hasMirrorShortcut +
                  " frontBackShortcut=" +
                  hasMirrorFrontBackShortcut;
        }

        public string DebugRunViewMenuChecks()
        {
            if (_viewMenuButton == null)
            {
                return "fail no-view-button";
            }
            ToggleMenu(
                "ViewMenu",
                _viewMenuButton,
                CreateViewMenuEntries());
            var itemCount = 0;
            var hasPreview = false;
            var hasGroundNotes = false;
            var hasSkyNotes = false;
            var hasFullscreenShortcut = false;
            var hasMeasureNumbers = false;
            var hasTrackSpeedEffects = false;
            var trackSpeedEffectsShortcut = false;
            var previewLabel = L.Get("falsum.menu.view.chartPreview");
            var groundLabel = L.Get("falsum.menu.view.groundNotes");
            var skyLabel = L.Get("falsum.menu.view.skyNotes");
            var measureNumbersLabel =
                L.Get("falsum.menu.view.measureNumbers");
            var trackSpeedEffectsLabel =
                L.Get("falsum.menu.view.trackSpeedEffects");
            var fullscreenLabel =
                L.Get("falsum.menu.view.toggleFullscreen");
            if (_menuPanel != null)
            {
                _menuPanel.Query<Button>(
                    className: "menu-item-button").ForEach(
                    button =>
                    {
                        itemCount++;
                        var label = button.Q<Label>(
                            "MenuItemLabel");
                        var shortcut = button.Q<Label>(
                            "MenuItemShortcut");
                        var text = label != null
                            ? label.text
                            : string.Empty;
                        hasPreview |= text == previewLabel;
                        // Checked entries are rendered with a check prefix, so
                        // match the label inside the text.
                        hasGroundNotes |= text.Contains(groundLabel);
                        hasSkyNotes |= text.Contains(skyLabel);
                        hasMeasureNumbers |=
                            text.Contains(measureNumbersLabel);
                        hasTrackSpeedEffects |=
                            text.Contains(trackSpeedEffectsLabel);
                        trackSpeedEffectsShortcut |=
                            text.Contains(trackSpeedEffectsLabel) &&
                            shortcut != null &&
                            !string.IsNullOrWhiteSpace(
                                shortcut.text);
                        hasFullscreenShortcut |= text == fullscreenLabel &&
                            shortcut != null &&
                            !string.IsNullOrWhiteSpace(
                                shortcut.text);
                    });
            }
            CloseMenu();
            return itemCount == (FalsumMeasureNumberFeature.Available ? 7 : 6) &&
                   hasPreview &&
                   hasGroundNotes &&
                   hasSkyNotes &&
                   hasMeasureNumbers ==
                       FalsumMeasureNumberFeature.Available &&
                   hasTrackSpeedEffects &&
                   trackSpeedEffectsShortcut &&
                   hasFullscreenShortcut
                ? "pass items=" + itemCount
                : "fail items=" + itemCount +
                  " preview=" + hasPreview +
                  " ground=" + hasGroundNotes +
                  " sky=" + hasSkyNotes +
                  " measureNumbers=" + hasMeasureNumbers +
                  " trackSpeedEffects=" + hasTrackSpeedEffects +
                  " trackSpeedEffectsShortcut=" +
                  trackSpeedEffectsShortcut +
                  " fullscreenShortcut=" +
                  hasFullscreenShortcut;
        }

        public void DebugShowFileMenu()
        {
            CloseMenu();
            ToggleMenu(
                "FileMenu",
                _fileMenuButton,
                CreateFileMenuEntries());
        }

        public void DebugHideMainMenu()
        {
            CloseMenu();
        }

        public void DebugConfigureNewChartDialogPreview(
            string targetPath,
            string audioPath,
            string coverPath)
        {
            if (_newChartOverlay == null)
            {
                return;
            }
            _newChartTitle.SetValueWithoutNotify(
                "Nightfall Protocol");
            _newChartArtist.SetValueWithoutNotify("aran");
            _newChartJacketDesigner.SetValueWithoutNotify(
                "cover_artist");
            _newChartDifficultyEnabled[0] = false;
            _newChartDifficultyEnabled[1] = true;
            _newChartDifficultyEnabled[2] = true;
            _newChartDifficultyEnabled[3] = false;
            _newChartActiveDifficulty = 2;
            _newChartTargetPath = targetPath;
            Require<Label>(
                _newChartOverlay,
                "NewChartPath").text =
                L.Get(
                    "falsum.debug.atPath",
                    ("path", targetPath));
            SetNewChartAudio(audioPath);
            SetNewChartCover(coverPath);
            _newChartRatings[1] = "7";
            _newChartRatings[2] = "11";
            _newChartDesigners[1] = "chart_designer";
            _newChartDesigners[2] = "aran";
            UpdateNewChartDialog();
        }

        public string DebugRunFocusChecks()
        {
            if (_speedInput == null)
            {
                return "fail no-speed-input";
            }
            _eventsButton.Focus();
            var nonTextReleased =
                !ReferenceEquals(
                    GetFocusedElement(),
                    _eventsButton);
            _speedInput.Focus();
            var typing = IsTyping();
            ClearInputFocus();
            var released = !IsTyping();
            return typing && released && nonTextReleased
                ? "pass"
                : "fail typing=" + typing +
                  " released=" + released +
                  " nonTextReleased=" + nonTextReleased;
        }

        public string DebugRunImeFocusChecks()
        {
            if (_speedInput == null)
            {
                return "fail no-speed-input";
            }
            var previousMode = Input.imeCompositionMode;
            var previousCursor = Input.compositionCursorPos;
            try
            {
                ClearInputFocus();
                UpdateImeForFocusedTextInput();
                var idleOff =
                    Input.imeCompositionMode ==
                    IMECompositionMode.Off;
                _eventsButton.Focus();
                var nonTextReleased =
                    !ReferenceEquals(
                        GetFocusedElement(),
                        _eventsButton);
                ClearInputFocus();

                _speedInput.Focus();
                UpdateImeForFocusedTextInput();
                var staleFocusOff =
                    Input.imeCompositionMode ==
                    IMECompositionMode.Off;
                _textInputFocusGrantedByPointer = true;
                UpdateImeForFocusedTextInput();
                var editingOn =
                    Input.imeCompositionMode ==
                    IMECompositionMode.On;
                var expected = PanelTopLeftToWindow(
                    ImeAnchor(_speedInput));
                var cursorNearField =
                    (Input.compositionCursorPos - expected)
                    .sqrMagnitude <= 1.0f;
                var cursorUsesTopOrigin =
                    Input.compositionCursorPos.y <
                    Screen.height * 0.5f;
                var originalCursorIndex =
                    _speedInput.cursorIndex;
                _speedInput.cursorIndex = 0;
                var cursorAtStart = ImeAnchor(_speedInput);
                _speedInput.cursorIndex =
                    _speedInput.value.Length;
                var cursorAtEnd = ImeAnchor(_speedInput);
                _speedInput.cursorIndex =
                    originalCursorIndex;
                var cursorTracksText =
                    cursorAtEnd.x > cursorAtStart.x;

                ClearInputFocus();
                UpdateImeForFocusedTextInput();
                var releasedOff =
                    Input.imeCompositionMode ==
                    IMECompositionMode.Off;
                var lettersRejected = string.Equals(
                    SanitizeNumericInput("eA1.f2E"),
                    "1.2",
                    StringComparison.Ordinal);
                return idleOff &&
                       nonTextReleased &&
                       staleFocusOff &&
                       editingOn &&
                       cursorNearField &&
                       cursorUsesTopOrigin &&
                       cursorTracksText &&
                       releasedOff &&
                       lettersRejected
                    ? "pass idle=off nonText=released stale=off editing=on " +
                      "cursor=near,top-left,tracks-text " +
                      "released=off letters=rejected"
                    : "fail idleOff=" + idleOff +
                      " nonTextReleased=" + nonTextReleased +
                      " staleFocusOff=" + staleFocusOff +
                      " editingOn=" + editingOn +
                      " cursorNear=" + cursorNearField +
                      " cursorTopOrigin=" + cursorUsesTopOrigin +
                      " cursorTracksText=" + cursorTracksText +
                      " releasedOff=" + releasedOff +
                      " lettersRejected=" + lettersRejected;
            }
            finally
            {
                ClearInputFocus();
                Input.imeCompositionMode = previousMode;
                Input.compositionCursorPos = previousCursor;
            }
        }

        public string DebugRunLayoutChecks()
        {
            if (_root == null || _topBar == null || _bottomBar == null)
            {
                return "fail missing-bars";
            }
            var issues = new List<string>();
            var responsiveRoot = _falsumRoot ?? _root;
            var compact =
                responsiveRoot.ClassListContains("is-compact");
            var shortHeight =
                responsiveRoot.ClassListContains("is-short");
            var expectedTop = compact ? 52.0f : 56.0f;
            var expectedBottom =
                shortHeight ? 58.0f : 64.0f;
            if (Math.Abs(_topBar.layout.height - expectedTop) > 2.0f)
            {
                issues.Add("top-height");
            }
            if (Math.Abs(
                    _bottomBar.layout.height -
                    expectedBottom) > 2.0f)
            {
                issues.Add("bottom-height");
            }
            if (_fileMenuButton.layout.height < 32.0f)
            {
                issues.Add("menu-button");
            }
            if (_undoButton.layout.height < 32.0f ||
                _playPauseButton.layout.height < 36.0f)
            {
                issues.Add("bar-button");
            }
            if (_timelineSlider.layout.height < 30.0f)
            {
                issues.Add("timeline-height");
            }
            if (_timelineSlider.layout.width < 80.0f)
            {
                issues.Add("timeline");
            }
            var timelineTracker = _timelineSlider.Q<VisualElement>(
                className: "unity-base-slider__tracker");
            var timelineDragger = _timelineSlider.Q<VisualElement>(
                className: "unity-base-slider__dragger");
            if (timelineTracker == null ||
                timelineTracker.layout.height < 3.0f)
            {
                issues.Add("timeline-track");
            }
            if (timelineDragger == null ||
                timelineDragger.layout.width < 9.0f ||
                timelineDragger.layout.height < 20.0f)
            {
                issues.Add("timeline-thumb");
            }
            for (var i = 0; i < _bottomBar.hierarchy.childCount; i++)
            {
                var child = _bottomBar.hierarchy[i];
                if (child.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }
                if (child.layout.xMin < -1.0f ||
                    child.layout.xMax >
                    _bottomBar.layout.width + 1.0f)
                {
                    issues.Add("bottom-overflow-" + i);
                }
            }
            return issues.Count == 0
                ? "pass root=" + _root.layout.size.ToString("F1") +
                  " bottom=" + _bottomBar.layout.size.ToString("F1") +
                  " timeline=" +
                  _timelineSlider.layout.width.ToString("F1")
                : "fail " + string.Join(",", issues.ToArray());
        }

        public string DebugRunPropertyFieldChecks()
        {
            if (_propertyRows.Count == 0)
            {
                return "skip no-property-rows";
            }

            var issues = new List<string>();
            var summaries = new List<string>();
            for (var i = 0; i < _propertyRows.Count; i++)
            {
                var field = _propertyRows[i].Field;
                if (field == null ||
                    field.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }
                var input = field.Q<VisualElement>(
                    className: "unity-text-field__input");
                if (input == null)
                {
                    issues.Add("input-" + i);
                    continue;
                }
                var text = input.Q<TextElement>();
                if (text == null)
                {
                    issues.Add("text-" + i);
                    continue;
                }
                if (text.layout.height <
                    input.layout.height - 1.5f)
                {
                    issues.Add("text-fill-" + i);
                }
                if (Mathf.Abs(text.layout.yMin) > 1.0f)
                {
                    issues.Add("text-offset-" + i);
                }
                summaries.Add(
                    input.layout.height.ToString("F1") +
                    "/" + text.layout.height.ToString("F1") +
                    "@" + text.layout.yMin.ToString("F1"));
            }
            var summary =
                " inputs=" + string.Join("/", summaries.ToArray());
            return issues.Count == 0
                ? "pass" + summary
                : "fail " +
                  string.Join(",", issues.ToArray()) +
                  summary;
        }

        public string DebugRunEventPanelChecks()
        {
            if (!IsEventsPanelOpen ||
                _eventsPanel == null ||
                _eventRowsView == null)
            {
                return "fail panel-hidden";
            }
            var issues = new List<string>();
            if (_eventsPanel.layout.width < 340.0f ||
                _eventsPanel.layout.width > 460.0f)
            {
                issues.Add("panel-width");
            }
            if (_eventsPanel.layout.height < 500.0f)
            {
                issues.Add("panel-height");
            }
            if (_eventRows.Count == 0)
            {
                issues.Add("rows");
            }
            if (_eventsPanel.Q("EventMap") != null)
            {
                issues.Add("event-bus");
            }
            if (Math.Abs(_eventPanelOffset) > 0.5f)
            {
                issues.Add("panel-animation");
            }
            var selectedRow = _eventRows.Count > 0
                ? _eventRows[0]
                : null;
            var editor = selectedRow != null
                ? selectedRow.Q<VisualElement>(
                    className: "event-editor")
                : null;
            var input = editor != null
                ? editor.Q<TextField>(className: "event-input")
                : null;
            if (editor == null || input == null)
            {
                issues.Add("editor");
            }
            else if (input.layout.width < 80.0f ||
                     input.layout.height < 30.0f)
            {
                issues.Add("input-size");
            }
            else
            {
                input.Focus();
                var scrollBlocked =
                    ShouldBlockChartScroll(Vector2.zero);
                input.Blur();
                if (!scrollBlocked)
                {
                    issues.Add("scroll-guard");
                }
            }
            return issues.Count == 0
                ? "pass rows=" + _eventRows.Count +
                  " panel=" +
                  _eventsPanel.layout.size.ToString("F1")
                : "fail " + string.Join(",", issues.ToArray());
        }

        private UIDocument CreateDocument(
            PanelSettings panelSettings,
            VisualTreeAsset mainUxml)
        {
            var documentObject = new GameObject(
                "FalsumEditorDocument",
                typeof(UIDocument));
            documentObject.SetActive(false);
            documentObject.transform.SetParent(_host, false);
            var document = documentObject.GetComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = mainUxml;
            document.sortingOrder = 200;
            documentObject.SetActive(true);
            return document;
        }

        /// <summary>
        /// A copy of the shipped panel retargeted so that ONE PANEL UNIT IS
        /// ONE DP on the actual device. Returns null on desktop so the
        /// desktop path is untouched (same asset instance, same resolution,
        /// same scale).
        ///
        /// WHY NOT A FIXED 855x405: that number is only correct for a 1080p
        /// panel at 400 dpi. The same 1080p resolution spans roughly 360-460
        /// dp of height depending on the panel's reported DPI, so a fixed
        /// reference would make a 48 dp touch target land anywhere between
        /// 42 and 54 dp -- below the minimum on the denser devices. Deriving
        /// the reference from Screen.dpi keeps the phys-to-dp ratio at 1 on
        /// every device, which is what lets FalsumMobile.uss state plain dp
        /// numbers and have them mean what they say.
        ///
        /// The 19:9 aspect is kept as the DESIGN RATIO (855x405 at the
        /// 1080-high reference); only the scale changes per device.
        ///
        /// WHY A CLONE: the desktop panel is also asserted by
        /// FalsumSelfCheck (scale mode + 1730x1080), and PanelSettings is a
        /// serialized asset -- mutating it at runtime would make the desktop
        /// layout change under a phone session and would break that
        /// assertion. A runtime clone keeps both truths.
        /// </summary>
        private static PanelSettings CreateMobilePanelSettings(
            PanelSettings source)
        {
            if (!FalsumMobileShell.IsMobile || source == null)
            {
                return null;
            }
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "FalsumMobilePanel";
            panel.hideFlags = HideFlags.DontSave;
            panel.themeStyleSheet = source.themeStyleSheet;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = ResolveMobileReferenceResolution();
            panel.screenMatchMode =
                PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 200;
            return panel;
        }

        /// <summary>Design aspect: 19:9, the landscape phone target.</summary>
        private const float MobileAspect = 19.0f / 9.0f;

        /// <summary>Reference height used to derive the design size.</summary>
        private const float MobileDesignHeight = 405.0f;

        /// <summary>
        /// Reference resolution in dp, i.e. the on-screen layout size at
        /// which one panel unit equals one dp.
        ///
        /// Derivation: Android reports Screen.dpi (physical px per inch) and
        /// Android defines one dp as one CSS pixel at 160 dpi. So
        ///   dp_width  = Screen.width  * 160 / Screen.dpi
        ///   dp_height = Screen.height * 160 / Screen.dpi
        /// Using the ACTUAL dp size as the reference makes the panel's
        /// ScaleWithScreenSize factor exactly 1, so a rule that says 48px
        /// renders at 48 dp.
        ///
        /// Desktop verification (FALSUM_MOBILE_SHELL=1) has no meaningful
        /// DPI, so it falls back to the design size -- which is what the
        /// capture tool renders against.
        /// </summary>
        private static Vector2Int ResolveMobileReferenceResolution()
        {
            var design = new Vector2Int(
                Mathf.RoundToInt(MobileDesignHeight * MobileAspect),
                Mathf.RoundToInt(MobileDesignHeight));
            // Desktop verification (FALSUM_MOBILE_SHELL=1) reports a desktop
            // DPI, which would derive a ~948x711 reference and silently
            // review the layout at 4:3 instead of 19:9. There is no real
            // phone density to honour there, so take the design size.
            if (!Application.isMobilePlatform)
            {
                return design;
            }
            var dpi = Screen.dpi;
            if (dpi <= 1.0f || Screen.width <= 0 || Screen.height <= 0)
            {
                return design;
            }
            var dpWidth = Screen.width * 160.0f / dpi;
            var dpHeight = Screen.height * 160.0f / dpi;
            // Clamp to a sane band: a misreported DPI must not produce a
            // 40 dp or 4000 dp reference and wreck the layout. The width
            // band is anchored on the 19:9 design ratio so a device that
            // reports an odd aspect still lands on a usable layout.
            dpHeight = Mathf.Clamp(dpHeight, 320.0f, 640.0f);
            dpWidth = Mathf.Clamp(
                dpWidth,
                dpHeight * 1.6f,
                dpHeight * MobileAspect * 1.15f);
            return new Vector2Int(
                Mathf.RoundToInt(dpWidth),
                Mathf.RoundToInt(dpHeight));
        }

        private static PanelSettings CreateRuntimePanelSettings()
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "FalsumRuntimeDiagnosticPanel";
            panel.themeStyleSheet =
                Resources.Load<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1730, 1080);
            panel.screenMatchMode =
                PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 200;
            return panel;
        }

        private void LoadTemplates()
        {
            _propertyRowTemplate = RequireTemplate(PropertyRowPath);
            _menuPanelTemplate = RequireTemplate(MenuPanelPath);
            _menuItemTemplate = RequireTemplate(MenuItemPath);
            _menuSeparatorTemplate = RequireTemplate(MenuSeparatorPath);
            _confirmDialogTemplate = RequireTemplate(ConfirmDialogPath);
            _disclaimerDialogTemplate =
                RequireTemplate(DisclaimerDialogPath);
            _newChartDialogTemplate = RequireTemplate(NewChartDialogPath);
            _preferencesDialogTemplate =
                RequireTemplate(PreferencesDialogPath);
            _toolDialogTemplate =
                RequireTemplate(ToolDialogPath);
            _toolReportDialogTemplate =
                RequireTemplate(ToolReportDialogPath);
            _audioPreprocessDialogTemplate =
                RequireTemplate(AudioPreprocessDialogPath);
            _skyAreaBankTemplate = RequireTemplate(SkyAreaBankPath);
        }

        private static VisualTreeAsset RequireTemplate(string path)
        {
            var template = Resources.Load<VisualTreeAsset>(path);
            if (template == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.ui.templateMissing",
                        ("path", "Resources/" + path + ".uxml")));
            }
            return template;
        }

        private void QueryMainElements()
        {
            _falsumRoot = Require<VisualElement>(_root, "FalsumRoot");
            _topBar = Require<VisualElement>(_root, "TopBar");
            _bottomBar = Require<VisualElement>(_root, "BottomBar");
            _propertyPanel =
                Require<VisualElement>(_root, "NoteProperties");
            _propertyRowsView =
                Require<ScrollView>(_root, "PropertyRows");
            _propertyRowsView.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _propertyRowsView.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _hintPanel = Require<VisualElement>(_root, "ModeHint");
            _middleEditStatus =
                Require<VisualElement>(_root, "MiddleEditStatus");
            _middleEditModeLabel =
                Require<Label>(_root, "MiddleEditModeLabel");
            _middleEditModeHint =
                Require<Label>(_root, "MiddleEditModeHint");
            _middleEditModeAlternatives =
                Require<Label>(
                    _root,
                    "MiddleEditModeAlternatives");
            // The mode indicator is a name-only readout now; the tutorial and
            // the alternative-mode listing must never take space in the bar.
            ApplyMiddleEditStatusVisibility();
            _menuLayer = Require<VisualElement>(_root, "MenuLayer");
            _bankLayer = Require<VisualElement>(_root, "BankLayer");
            _modalLayer = Require<VisualElement>(_root, "ModalLayer");
            _radialLayer =
                Require<VisualElement>(_root, "RadialLayer");
            _songInfoCardViewport =
                Require<VisualElement>(
                    _root,
                    "SongInfoCardViewport");
            _songInfoCardBackdrop =
                Require<Image>(
                    _root,
                    "SongInfoCardBackdrop");
            _songInfoCard =
                Require<VisualElement>(_root, "SongInfoCard");
            _songInfoCardCover =
                Require<VisualElement>(
                    _root,
                    "SongInfoCardCover");
            _songInfoCardTitle =
                Require<TextField>(_root, "SongInfoCardTitle");
            _songInfoCardArtist =
                Require<TextField>(_root, "SongInfoCardArtist");
            _songInfoCardRating =
                Require<TextField>(_root, "SongInfoCardRating");
            _songInfoCardChartDesigner =
                Require<TextField>(
                    _root,
                    "SongInfoCardChartDesigner");
            _songInfoCardDifficultyHotspot =
                Require<Button>(
                    _root,
                    "SongInfoCardDifficultyHotspot");
            _songInfoCardDifficultyMenu =
                Require<VisualElement>(
                    _root,
                    "SongInfoCardDifficultyMenu");
            _songInfoCardDifficultyOptions =
                Require<VisualElement>(
                    _root,
                    "SongInfoCardDifficultyOptions");
            _songInfoCardProgressFill =
                Require<VisualElement>(
                    _root,
                    "SongInfoCardProgressFill");
            _fileMenuButton = Require<Button>(_root, "FileMenu");
            _editMenuButton = Require<Button>(_root, "EditMenu");
            _viewMenuButton = Require<Button>(_root, "ViewMenu");
            _toolMenuButton = Require<Button>(_root, "ToolMenu");
            _skyAreaBankMenuButton =
                Require<Button>(_root, "SkyAreaBankMenu");
            _speedSlider = Require<Slider>(_root, "FlowSlider");
            _speedInput = Require<TextField>(_root, "FlowValue");
            _audioPreprocessButton =
                Require<Button>(_root, "AudioPreprocess");
            _baseBpmInput =
                Require<TextField>(_root, "BaseBpmValue");
            _undoButton = Require<Button>(_root, "Undo");
            _redoButton = Require<Button>(_root, "Redo");
            _undoText = Require<Label>(_root, "UndoCount");
            _redoText = Require<Label>(_root, "RedoCount");
            _menuDismissLayer =
                Require<VisualElement>(_root, "MenuDismissLayer");
            _playPauseButton = Require<Button>(_root, "PlayPause");
            _timelineSlider =
                Require<Slider>(_root, "ChartProgress");
            _progressText = Require<Label>(_root, "Progress");
            _spectrumButton = Require<Button>(_root, "Spectrum");
            if (!FalsumSpectrumFeature.Available)
            {
                // Hidden until the spectrum view ships.
                SetVisible(_spectrumButton, false);
                _spectrumButton.SetEnabled(false);
            }
            _rateButton = Require<Button>(_root, "Rate");
            _subdivisionInput =
                Require<TextField>(_root, "Subdivision");
            _timeInput = Require<TextField>(_root, "Time");
            ConfigureTransportIcons();
            _hintText = Require<Label>(_root, "HintText");
            _propertyHeader =
                Require<Label>(_root, "PropertyHeader");
            _jumpToNoteButton =
                Require<Button>(_root, "JumpToNote");
            _eventsPanel =
                Require<VisualElement>(_root, "EventsPanel");
            _eventRowsView =
                Require<ScrollView>(_root, "EventRows");
            _eventAddMenu =
                Require<VisualElement>(_root, "EventAddMenu");
            _eventCount =
                Require<Label>(_root, "EventCount");
            _eventsButton =
                Require<Button>(_root, "Events");
            _eventFilterAll =
                Require<Button>(_root, "EventFilterAll");
            _eventFilterBpm =
                Require<Button>(_root, "EventFilterBpm");
            _eventFilterTrack =
                Require<Button>(_root, "EventFilterTrack");
            _eventFilterLane =
                Require<Button>(_root, "EventFilterLane");
            _addEventButton =
                Require<Button>(_root, "AddEvent");
            _addLaneEventButton =
                Require<Button>(_root, "AddLaneEvent");
        }

        private void ConfigureFonts()
        {
            _uiFont = FalsumFonts.Cjk;
            if (_uiFont != null)
            {
                _root.style.unityFontDefinition =
                    new StyleFontDefinition(
                        FontDefinition.FromFont(_uiFont));
            }

            _displayFont = Resources.Load<Font>("Falsum/Exo-Medium");
            if (_displayFont == null)
            {
                return;
            }
            ApplyDisplayFont(_rateButton);
            ApplyDisplayFont(_playPauseButton);
            ApplyDisplayFont(_progressText);
            ApplyDisplayFont(_songInfoCardRating);
        }

        private void ConfigureSongInfoCard()
        {
            _songInfoCardViewport.SendToBack();
            _songInfoCardTitle.textEdition.placeholder =
                L.Get("falsum.editor.songTitlePlaceholder");
            _songInfoCardArtist.textEdition.placeholder =
                L.Get("falsum.editor.songArtistPlaceholder");
            _songInfoCardChartDesigner.textEdition.placeholder =
                L.Get(
                    "falsum.editor.songChartDesignerPlaceholder");
            BindSongInfoCardInteractions();
            SetSongInfo(null, null, null);
            SetSongInfoCover(null);
            SetSongProgress(0.0f);
            _root.RegisterCallback<GeometryChangedEvent>(
                OnRootGeometryChanged);
            _root.RegisterCallback<KeyDownEvent>(
                OnRootKeyDown,
                TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerDownEvent>(
                OnRootPointerDown,
                TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationMoveEvent>(
                OnRootNavigationMove,
                TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusInEvent>(
                OnRootFocusIn,
                TrickleDown.TrickleDown);
            UpdateSongInfoCardTransform();
        }

        private void BindSongInfoCardInteractions()
        {
            _songInfoCardTitle.RegisterValueChangedCallback(
                evt =>
                {
                    ApplySongInfoTextFont(_songInfoCardTitle);
                    Raise(SongTitleSubmitted, evt.newValue);
                });
            _songInfoCardArtist.RegisterValueChangedCallback(
                evt =>
                {
                    ApplySongInfoTextFont(_songInfoCardArtist);
                    Raise(SongArtistSubmitted, evt.newValue);
                });
            RegisterNumericInput(
                _songInfoCardRating,
                value => Raise(SongRatingSubmitted, value));
            _songInfoCardChartDesigner
                .RegisterValueChangedCallback(
                    evt =>
                    {
                        ApplySongInfoTextFont(
                            _songInfoCardChartDesigner);
                        Raise(
                            SongChartDesignerSubmitted,
                            evt.newValue);
                    });
            _songInfoCardCover.RegisterCallback<ClickEvent>(
                OnSongInfoCoverClicked);
            _songInfoCardDifficultyHotspot.clicked +=
                ToggleSongInfoDifficultyMenu;
        }

        private void OnSongInfoCoverClicked(ClickEvent evt)
        {
            if (evt != null)
            {
                evt.StopPropagation();
            }
            Raise(ChartInfoRequested);
        }

        private void ToggleSongInfoDifficultyMenu()
        {
            if (_songInfoCardDifficultyMenu == null)
            {
                return;
            }
            if (_songInfoCardDifficultyMenu
                    .resolvedStyle.display != DisplayStyle.None)
            {
                HideSongInfoDifficultyMenu();
                return;
            }
            ShowSongInfoDifficultyMenu();
        }

        private void ShowSongInfoDifficultyMenu()
        {
            if (_songInfoCardDifficultyMenu == null ||
                _songInfoCardDifficultyOptions == null)
            {
                return;
            }
            _songInfoCardDifficultyOptions.Clear();
            for (var i = 0;
                 i < _songInfoCardAvailableDifficulties.Count;
                 i++)
            {
                var difficulty =
                    _songInfoCardAvailableDifficulties[i];
                if (difficulty < 0 || difficulty > 3 ||
                    difficulty == _songInfoCardDifficultyIndex)
                {
                    continue;
                }
                var captured = difficulty;
                var option = new Button(
                    delegate
                    {
                        HideSongInfoDifficultyMenu();
                        Raise(DifficultySelected, captured);
                    })
                {
                    text = DifficultyDisplayName(difficulty),
                };
                option.AddToClassList(
                    "song-info-card-difficulty-option");
                option.AddToClassList(
                    FalsumManifest.DifficultyNames[difficulty]
                        .ToLowerInvariant());
                var enabled =
                    _songInfoCardDifficultyEnabled[difficulty];
                option.EnableInClassList("is-empty", !enabled);
                if (!enabled)
                {
                    option.tooltip = L.Get(
                        "falsum.editor.emptyDifficultyTooltip");
                }
                _songInfoCardDifficultyOptions.Add(option);
            }
            SetVisible(
                _songInfoCardDifficultyMenu,
                _songInfoCardDifficultyOptions.childCount > 0);
        }

        private void HideSongInfoDifficultyMenu()
        {
            SetVisible(_songInfoCardDifficultyMenu, false);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateSongInfoCardTransform();
            UpdateResponsiveClasses();
            // The timeline thumb is positioned from cached track/thumb widths,
            // so a resize invalidates it; let the next tick repaint it and let
            // the Slider recompute its own drag position from the real value.
            _timelineThumbLeft = float.NaN;
            FlushTimelineSliderValue();
        }

        private void UpdateResponsiveClasses()
        {
            var target = _falsumRoot ?? _root;
            if (target == null)
            {
                return;
            }

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            var panelHeight = _root.layout.height;
            var physicalScale = panelHeight > 1.0f
                ? _lastScreenHeight / panelHeight
                : 1.0f;
            var compact =
                _lastScreenWidth <= CompactScreenWidth ||
                _lastScreenHeight <= CompactScreenHeight ||
                physicalScale < LowScaleThreshold;
            var narrow = _lastScreenWidth < NarrowScreenWidth;
            var shortHeight = _lastScreenHeight < ShortScreenHeight;
            var lowScale = physicalScale < LowScaleThreshold;

            target.EnableInClassList("is-compact", compact);
            target.EnableInClassList("is-narrow", narrow);
            target.EnableInClassList("is-short", shortHeight);
            target.EnableInClassList("is-low-scale", lowScale);
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (_mobileShell != null && evt.keyCode == KeyCode.Escape &&
                DismissMobileOverlay())
            {
                evt.StopImmediatePropagation();
                return;
            }
            if (IsFloatingToolReportTarget(
                    evt.target as VisualElement))
            {
                return;
            }
            if (IsEditorFocusHandledByOverlay())
            {
                return;
            }
            var focusedField = FindAncestorTextField(
                GetFocusedElement());
            var textEditing =
                focusedField != null &&
                (_textInputFocusGrantedByPointer ||
                 _modal != null);
            if (focusedField != null &&
                IsTextFieldEditShortcut(evt))
            {
                return;
            }
            if (textEditing)
            {
                if (evt.keyCode == KeyCode.Tab)
                {
                    evt.StopImmediatePropagation();
                }
                else if (focusedField.ClassListContains(
                             "falsum-numeric-input") &&
                         IsAlphabeticInput(evt))
                {
                    evt.StopImmediatePropagation();
                }
                return;
            }

            ClearInputFocus();
            if (IsAlphabeticInput(evt))
            {
                evt.StopImmediatePropagation();
            }
        }

        private void OnRootNavigationMove(
            NavigationMoveEvent evt)
        {
            if (IsFloatingToolReportTarget(
                    evt.target as VisualElement))
            {
                return;
            }
            if (IsEditorFocusHandledByOverlay())
            {
                return;
            }
            evt.StopImmediatePropagation();
        }

        private void OnRootFocusIn(FocusInEvent evt)
        {
            var target = evt.target as VisualElement;
            if (IsFloatingToolReportTarget(target))
            {
                return;
            }
            if (IsEditorFocusHandledByOverlay())
            {
                return;
            }
            if (FindAncestorTextField(target) != null)
            {
                return;
            }
            evt.StopPropagation();
            if (target != null)
            {
                target.Blur();
            }
        }

        private void OnRootPointerDown(PointerDownEvent evt)
        {
            var target = evt.target as VisualElement;
            if (IsFloatingToolReportTarget(target))
            {
                return;
            }
            if (IsEditorFocusHandledByOverlay())
            {
                return;
            }
            if (_songInfoCardDifficultyMenu != null &&
                _songInfoCardDifficultyMenu
                    .resolvedStyle.display != DisplayStyle.None &&
                !IsWithinElement(
                    target,
                    _songInfoCardDifficultyMenu) &&
                !IsWithinElement(
                    target,
                    _songInfoCardDifficultyHotspot))
            {
                HideSongInfoDifficultyMenu();
            }
            if (IsWithinTextField(target))
            {
                _textInputFocusGrantedByPointer = true;
                return;
            }
            ClearInputFocus();
        }

        private bool IsFloatingToolReportTarget(
            VisualElement target)
        {
            return IsToolReportFloating &&
                   target != null &&
                   _toolReportView != null &&
                   _toolReportView.ContainsElement(target);
        }

        private bool IsEditorFocusHandledByOverlay()
        {
            return IsModalOpen ||
                   _mobileNumericOverlay != null ||
                   IsNewChartDialogOpen ||
                   _activeMenu != null;
        }

        private static bool IsWithinElement(
            VisualElement element,
            VisualElement ancestor)
        {
            if (element == null || ancestor == null)
            {
                return false;
            }
            for (var current = element;
                 current != null;
                 current = current.parent)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsWithinTextField(VisualElement element)
        {
            return FindAncestorTextField(element) != null;
        }

        private static TextField FindAncestorTextField(
            VisualElement element)
        {
            for (var current = element;
                 current != null;
                 current = current.parent)
            {
                var field = current as TextField;
                if (field != null)
                {
                    return field;
                }
            }
            return null;
        }

        private static bool IsAlphabeticInput(KeyDownEvent evt)
        {
            return char.IsLetter(evt.character) ||
                   (evt.keyCode >= KeyCode.A &&
                    evt.keyCode <= KeyCode.Z);
        }

        private static bool IsTextFieldEditShortcut(
            KeyDownEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey)
            {
                return false;
            }
            return evt.keyCode == KeyCode.A ||
                   evt.keyCode == KeyCode.C ||
                   evt.keyCode == KeyCode.V ||
                   evt.keyCode == KeyCode.X ||
                   evt.keyCode == KeyCode.Y ||
                   evt.keyCode == KeyCode.Z;
        }

        private void UpdateImeForFocusedTextInput()
        {
            var focused = GetFocusedElement();
            var field = FindAncestorTextField(focused);
            var textEditing =
                field != null &&
                (_textInputFocusGrantedByPointer ||
                 _modal != null);
            if (!textEditing ||
                !IsEffectivelyVisible(field))
            {
                DisableIme();
                return;
            }
            var bounds = field.worldBound;
            if (bounds.width <= 0.0f || bounds.height <= 0.0f)
            {
                DisableIme();
                return;
            }

            Input.imeCompositionMode = IMECompositionMode.On;
            var windowPosition = PanelTopLeftToWindow(
                ImeAnchor(field));
            Input.compositionCursorPos = windowPosition;

            var anchor = ImeAnchor(field);
            var normalizedX =
                anchor.x / _root.layout.width;
            var normalizedY =
                anchor.y / _root.layout.height;
            var panelX =
                Mathf.RoundToInt(normalizedX * 10000.0f);
            var panelY =
                Mathf.RoundToInt(normalizedY * 10000.0f);
            var now = Time.unscaledTime;
            if (panelX == _lastImePanelX &&
                panelY == _lastImePanelY &&
                now < _nextImeSyncAt)
            {
                return;
            }
            if (FalsumImePosition.TrySetNormalizedPosition(
                    normalizedX,
                    normalizedY))
            {
                _lastImePanelX = panelX;
                _lastImePanelY = panelY;
                _nextImeSyncAt = now + 0.25f;
            }
            else
            {
                _nextImeSyncAt = now + 0.05f;
            }
        }

        private void DisableIme()
        {
            Input.imeCompositionMode = IMECompositionMode.Off;
            _lastImePanelX = int.MinValue;
            _lastImePanelY = int.MinValue;
            _nextImeSyncAt = 0.0f;
        }

        private static Vector2 ImeAnchor(TextField field)
        {
            return field.LocalToWorld(field.cursorPosition) +
                   new Vector2(
                       0.0f,
                       field.worldBound.height);
        }

        private static bool IsEffectivelyVisible(
            VisualElement element)
        {
            if (element == null || !element.enabledInHierarchy)
            {
                return false;
            }
            for (var current = element;
                 current != null;
                 current = current.parent)
            {
                if (!current.visible ||
                    current.resolvedStyle.display ==
                    DisplayStyle.None)
                {
                    return false;
                }
            }
            return true;
        }

        private Vector2 PanelTopLeftToWindow(
            Vector2 panelPosition)
        {
            if (_root == null ||
                _root.layout.width <= 0.0f ||
                _root.layout.height <= 0.0f)
            {
                return Vector2.zero;
            }
            var scaleX = Screen.width / _root.layout.width;
            var scaleY = Screen.height / _root.layout.height;
            return new Vector2(
                Mathf.Clamp(
                    panelPosition.x * scaleX,
                    0.0f,
                    Screen.width),
                Mathf.Clamp(
                    panelPosition.y * scaleY,
                    0.0f,
                    Screen.height));
        }

        private void UpdateSongInfoCardTransform()
        {
            if (_root == null || _songInfoCardViewport == null ||
                _songInfoCard == null)
            {
                return;
            }
            var rootWidth = _root.layout.width;
            var rootHeight = _root.layout.height;
            if (rootWidth <= 1.0f || rootHeight <= 1.0f)
            {
                return;
            }
            // On the phone the card lives in the right-most floating host, and
            // this per-frame desktop formula would fight it: it anchors the
            // card to `rootWidth - gap`, i.e. UNDER the right rail, and it
            // calls SendToBack from a different path, which buries the card
            // behind the 3D view. The host owns the position; only the scale
            // is computed here so the card keeps its C-8 proportions.
            if (_mobileShell != null)
            {
                var host = _mobileShell.SongCardHost;
                var hostWidth = host != null ? host.layout.width : 0.0f;
                if (hostWidth > 1.0f)
                {
                    var mobileScale =
                        hostWidth / SongInfoCardDesignWidth;
                    _songInfoCardViewport.style.left = 0;
                    _songInfoCardViewport.style.top = 0;
                    _songInfoCardViewport.style.width = hostWidth;
                    _songInfoCardViewport.style.height =
                        SongInfoCardDesignHeight * mobileScale;
                    _songInfoCard.style.scale =
                        new Scale(new Vector2(mobileScale, mobileScale));
                    return;
                }
            }

            var scale =
                rootWidth * SongInfoCardWidthRatio /
                SongInfoCardDesignWidth;
            var visualWidth =
                SongInfoCardDesignWidth * scale;
            var visualHeight =
                SongInfoCardDesignHeight * scale;
            var visualLeft =
                rootWidth -
                SongInfoCardRightGap -
                visualWidth;
            var visualTop =
                (_topBar != null
                    ? _topBar.layout.yMax
                    : 72.0f) +
                SongInfoCardTopGap;
            _songInfoCardViewport.style.left = visualLeft;
            _songInfoCardViewport.style.top = visualTop;
            _songInfoCardViewport.style.width = visualWidth;
            _songInfoCardViewport.style.height = visualHeight;
            _songInfoCard.style.scale =
                new Scale(new Vector2(scale, scale));
            if (_hintPanel != null)
            {
                _hintPanel.style.left = visualLeft;
                _hintPanel.style.top =
                    visualTop + visualHeight + 8.0f;
                _hintPanel.style.width = visualWidth;
            }
        }

        private void SetSongInfoCardBackdrop(int difficultyIndex)
        {
            if (_songInfoCardBackdrop == null)
            {
                return;
            }
            var index = Mathf.Clamp(
                difficultyIndex,
                0,
                SongInfoCardBackdropNames.Length - 1);
            var path =
                SongInfoCardBackdropPathPrefix +
                SongInfoCardBackdropNames[index];
            var backdrop = Resources.Load<Texture2D>(path);
            if (backdrop == null)
            {
                Debug.LogWarning(
                    "[Falsum] Song info card backdrop is missing: " +
                    "Resources/" + path);
                return;
            }
            _songInfoCardBackdrop.image = backdrop;
            _songInfoCardBackdrop.scaleMode =
                ScaleMode.StretchToFill;
        }

        private void ApplySongInfoTextFont(
            VisualElement label)
        {
            if (label == null)
            {
                return;
            }
            var font = FalsumSongFont.CardFont != null
                ? FalsumSongFont.CardFont
                : _uiFont;
            if (font == null)
            {
                return;
            }
            var definition = new StyleFontDefinition(
                FontDefinition.FromFont(font));
            label.style.unityFontDefinition = definition;
            label.Query<TextElement>().ForEach(
                child => child.style.unityFontDefinition = definition);
        }

        private static void ApplyDisplayFont(VisualElement element)
        {
            var font = Resources.Load<Font>("Falsum/Exo-Medium");
            if (element == null || font == null)
            {
                return;
            }
            var definition = new StyleFontDefinition(
                FontDefinition.FromFont(font));
            element.style.unityFontDefinition = definition;
            element.Query<TextElement>().ForEach(
                child => child.style.unityFontDefinition = definition);
        }

        private static VisualElement ConfigureSliderFill(
            Slider slider,
            string fillClass)
        {
            if (slider == null)
            {
                return null;
            }
            var input = slider.Q<VisualElement>(
                className: "unity-base-slider__input");
            var drag = input != null
                ? input.Q<VisualElement>("unity-drag-container")
                : null;
            if (drag == null)
            {
                return null;
            }
            var fill = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            fill.AddToClassList("slider-fill");
            fill.AddToClassList(fillClass);
            drag.Insert(1, fill);
            UpdateSliderFill(slider, fill);
            return fill;
        }

        private void UpdateTimelineVisuals(float normalized)
        {
            UpdateSliderFillNormalized(_timelineFill, normalized);
            UpdateTimelineThumb(normalized);
        }

        /// <summary>
        /// Writes the timeline thumb offset without touching
        /// <see cref="Slider.value"/>. Writing through BaseSlider would run
        /// its own drag-position update, which reads resolved style and layout
        /// (a synchronous style flush) on every playback frame.
        /// The thumb travel width is cached because the only inputs it needs -
        /// the drag container width, the thumb width and the thumb margins -
        /// change on resize, not per frame. Reading those lazily keeps the
        /// per-frame path to a subtraction and one translate write.
        /// </summary>
        private void UpdateTimelineThumb(float normalized)
        {
            if (_timelineDragger == null ||
                _timelineDragContainer == null ||
                _timelineDragContainer.panel == null)
            {
                return;
            }
            var trackWidth = _timelineDragContainer.layout.width;
            if (float.IsNaN(trackWidth) || trackWidth <= 0.0f)
            {
                return;
            }
            var thumbWidth = _timelineDragger.layout.width;
            if (float.IsNaN(thumbWidth))
            {
                thumbWidth = 0.0f;
            }
            if (float.IsNaN(_timelineThumbRange) ||
                !Mathf.Approximately(_timelineThumbTrackWidth, trackWidth) ||
                !Mathf.Approximately(_timelineThumbWidth, thumbWidth))
            {
                // Rare path (first tick or a resize): the margins come from
                // USS, so they are resolved once here instead of per frame.
                var margins =
                    _timelineDragger.resolvedStyle.marginLeft +
                    _timelineDragger.resolvedStyle.marginRight;
                _timelineThumbTrackWidth = trackWidth;
                _timelineThumbWidth = thumbWidth;
                _timelineThumbRange = Mathf.Max(
                    trackWidth + margins - thumbWidth,
                    0.0f);
                _timelineThumbLeft = float.NaN;
            }
            var left = Mathf.Clamp01(normalized) * _timelineThumbRange;
            if (!float.IsNaN(_timelineThumbLeft) &&
                Mathf.Abs(_timelineThumbLeft - left) < 0.01f)
            {
                return;
            }
            _timelineThumbLeft = left;
            _timelineDragger.style.translate =
                new StyleTranslate(new Translate(left, 0.0f));
        }

        private static void UpdateSliderFillNormalized(
            VisualElement fill,
            float normalized)
        {
            if (fill == null)
            {
                return;
            }
            fill.style.width = new Length(
                Mathf.Clamp01(normalized) * 100.0f,
                LengthUnit.Percent);
        }

        private static void UpdateSliderFill(
            Slider slider,
            VisualElement fill)
        {
            if (slider == null || fill == null)
            {
                return;
            }
            var range = slider.highValue - slider.lowValue;
            var normalized = Mathf.Approximately(range, 0.0f)
                ? 0.0f
                : Mathf.Clamp01(
                    (slider.value - slider.lowValue) / range);
            fill.style.width = new Length(
                normalized * 100.0f,
                LengthUnit.Percent);
        }

        private void BindTopBar()
        {
            _fileMenuButton.clicked += delegate
            {
                ToggleMenu(
                    "FileMenu",
                    _fileMenuButton,
                    CreateFileMenuEntries());
            };
            _editMenuButton.clicked += delegate
            {
                ToggleMenu(
                    "EditMenu",
                    _editMenuButton,
                    CreateEditMenuEntries());
            };
            _viewMenuButton.clicked += delegate
            {
                ToggleMenu(
                    "ViewMenu",
                    _viewMenuButton,
                    CreateViewMenuEntries());
            };
            _toolMenuButton.clicked += delegate
            {
                ToggleMenu(
                    "ToolMenu",
                    _toolMenuButton,
                    CreateToolMenuEntries());
            };
            BindMenuHover(
                _fileMenuButton,
                "FileMenu",
                CreateFileMenuEntries);
            BindMenuHover(
                _editMenuButton,
                "EditMenu",
                CreateEditMenuEntries);
            BindMenuHover(
                _viewMenuButton,
                "ViewMenu",
                CreateViewMenuEntries);
            BindMenuHover(
                _toolMenuButton,
                "ToolMenu",
                CreateToolMenuEntries);
            if (_skyAreaBankMenuButton != null)
            {
                _skyAreaBankMenuButton.clicked += delegate
                {
                    var handler = BankToggleRequested;
                    if (handler != null)
                    {
                        handler();
                    }
                };
            }
            _topBar.RegisterCallback<PointerDownEvent>(
                OnTopBarPointerDown);
            _menuDismissLayer.RegisterCallback<PointerDownEvent>(
                delegate { CloseMenu(); });

            _speedSlider.RegisterValueChangedCallback(
                evt =>
                {
                    UpdateSliderFill(_speedSlider, _speedFill);
                    Raise(SpeedChanged, evt.newValue);
                });
            RegisterNumericInput(
                _speedInput,
                value => Raise(SpeedSubmitted, value));
            if (_audioPreprocessButton != null)
            {
                _audioPreprocessButton.clicked += delegate
                {
                    Raise(AudioPreprocessRequested);
                };
            }
            RegisterNumericInput(
                _baseBpmInput,
                value => Raise(BaseBpmSubmitted, value));
        }

        private string ShortcutFor(FalsumInputAction action)
        {
            if (_shortcutPreferences == null)
            {
                return string.Empty;
            }
            var binding =
                _shortcutPreferences.FindBinding(action);
            return binding == null ||
                   binding.Key == KeyCode.None
                ? string.Empty
                : FalsumInputRouter.Format(binding);
        }

        private MenuEntry[] CreateFileMenuEntries()
        {
            return new[]
            {
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.new"),
                    Action = delegate
                    {
                        Raise(NewFileRequested);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.open"),
                    Action = delegate
                    {
                        Raise(OpenFileRequested);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.save"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.Save),
                    Action = delegate
                    {
                        Raise(SaveClicked);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.saveAs"),
                    Action = delegate
                    {
                        Raise(SaveAsClicked);
                    },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.restoreBackup"),
                    Action = delegate
                    {
                        Raise(RestoreBackupRequested);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.openBackupFolder"),
                    Action = delegate
                    {
                        Raise(OpenBackupFolderRequested);
                    },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.preferences"),
                    Shortcut = L.Get("falsum.shortcut.ctrlComma"),
                    Action = delegate
                    {
                        Raise(OpenPreferencesRequested);
                    },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.file.exit"),
                    Action = delegate
                    {
                        Raise(ExitRequested);
                    },
                },
            };
        }

        private MenuEntry[] CreateEditMenuEntries()
        {
            return new[]
            {
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.copy"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.Copy),
                    Action = delegate { Raise(CopyRequested); },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.paste"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.Paste),
                    Action = delegate { Raise(PasteRequested); },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.cut"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.Cut),
                    Action = delegate { Raise(CutRequested); },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.mirror"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.Mirror),
                    Action = delegate { Raise(MirrorRequested); },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.mirrorFrontBack"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.MirrorFrontBack),
                    Action = delegate
                    {
                        Raise(MirrorFrontBackRequested);
                    },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.selectGround"),
                    Checked = _selectGroundOnly,
                    Action = delegate
                    {
                        _selectGroundOnly = !_selectGroundOnly;
                        if (_selectGroundOnly)
                        {
                            _selectSkyOnly = false;
                        }
                        Raise(
                            SelectionFilterRequested,
                            _selectGroundOnly,
                            _selectSkyOnly);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.edit.selectSky"),
                    Checked = _selectSkyOnly,
                    Action = delegate
                    {
                        _selectSkyOnly = !_selectSkyOnly;
                        if (_selectSkyOnly)
                        {
                            _selectGroundOnly = false;
                        }
                        Raise(
                            SelectionFilterRequested,
                            _selectGroundOnly,
                            _selectSkyOnly);
                    },
                },
            };
        }

        private MenuEntry[] CreateViewMenuEntries()
        {
            return new[]
            {
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.view.chartPreview"),
                    Action = delegate
                    {
                        Raise(ChartPreviewRequested);
                    },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.view.groundNotes"),
                    Checked =
                        (_layerVisibility & FalsumChartLayer.Ground) != 0,
                    Enabled =
                        _layerVisibility != FalsumChartLayer.Ground,
                    Action = delegate
                    {
                        Raise(
                            ChartLayerVisibilityRequested,
                            _layerVisibility ^
                            FalsumChartLayer.Ground);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.view.skyNotes"),
                    Checked =
                        (_layerVisibility & FalsumChartLayer.Sky) != 0,
                    Enabled =
                        _layerVisibility != FalsumChartLayer.Sky,
                    Action = delegate
                    {
                        Raise(
                            ChartLayerVisibilityRequested,
                            _layerVisibility ^
                            FalsumChartLayer.Sky);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.view.grid"),
                    Checked = _gridEnabled,
                    Action = delegate
                    {
                        _gridEnabled = !_gridEnabled;
                        Raise(
                            GridVisibilityRequested,
                            _gridEnabled);
                    },
                },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.view.measureNumbers"),
                    Hidden = !FalsumMeasureNumberFeature.Available,
                    Checked = _measureNumbersEnabled,
                    Action = delegate
                    {
                        _measureNumbersEnabled =
                            !_measureNumbersEnabled;
                        Raise(
                            MeasureNumberVisibilityRequested,
                            _measureNumbersEnabled);
                    },
                },
                new MenuEntry
                {
                    // Checked means "effects are running", matching the
                    // wording of every other toggle in this menu. Turning it
                    // off keeps the track() events but previews a constant
                    // 1.00 scroll, which is what makes charts authored with
                    // negative track() multipliers editable.
                    Label = L.Get(
                        "falsum.menu.view.trackSpeedEffects"),
                    Checked = _trackSpeedEffectsEnabled,
                    Shortcut = ShortcutFor(
                        FalsumInputAction.ToggleTrackSpeedEffects),
                    Action = delegate
                    {
                        _trackSpeedEffectsEnabled =
                            !_trackSpeedEffectsEnabled;
                        Raise(
                            TrackSpeedEffectsRequested,
                            _trackSpeedEffectsEnabled);
                    },
                },
                new MenuEntry { Separator = true },
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.view.toggleFullscreen"),
                    Shortcut = ShortcutFor(
                        FalsumInputAction.ToggleFullscreen),
                    Action = delegate
                    {
                        Raise(ToggleFullscreenRequested);
                    },
                },
            };
        }

        private MenuEntry[] CreateToolMenuEntries()
        {
            var entries = new List<MenuEntry>();
            for (var i = 0; i < _toolCatalog.Count; i++)
            {
                var tool = _toolCatalog[i];
                if (!tool.Enabled || !tool.IsReady)
                {
                    continue;
                }
                var capturedId = tool.Id;
                entries.Add(
                    new MenuEntry
                    {
                        Label = tool.DisplayName,
                        Action = delegate
                        {
                            Raise(ToolOpenRequested, capturedId);
                        },
                    });
            }
            if (entries.Count > 0)
            {
                entries.Add(new MenuEntry { Separator = true });
            }
            entries.Add(
                new MenuEntry
                {
                    Label = L.Get("falsum.menu.tool.manage"),
                    Action = delegate
                    {
                        Raise(ToolManagerRequested);
                    },
                });
            return entries.ToArray();
        }

        private void BindBottomBar()
        {
            _undoButton.clicked += delegate { Raise(UndoClicked); };
            _redoButton.clicked += delegate { Raise(RedoClicked); };
            var clear = Require<Button>(_root, "ClearHistory");
            clear.clicked += delegate
            {
                ShowConfirm(
                    L.Get(
                        "falsum.editor.confirmClearHistory"),
                    delegate { Raise(ClearHistoryClicked); });
            };
            RegisterNumericInput(
                _subdivisionInput,
                value => Raise(SubdivisionSubmitted, value));
            RegisterNumericInput(
                _timeInput,
                value => Raise(TimeSubmitted, value));
            _playPauseButton.clicked += delegate
            {
                Raise(PlayPauseClicked);
            };
            // Playback leaves Slider.value stale on purpose (see SetTime), so
            // re-sync it as soon as the pointer lands on the timeline, before
            // ClampedDragger derives the new value from the old one.
            _timelineSlider.RegisterCallback<PointerDownEvent>(
                delegate { FlushTimelineSliderValue(); },
                TrickleDown.TrickleDown);
            _timelineSlider.RegisterValueChangedCallback(
                evt =>
                {
                    if (!_updatingTimeline)
                    {
                        _timelineSliderValueStale = false;
                        UpdateSliderFill(
                            _timelineSlider,
                            _timelineFill);
                        Raise(TimelineChanged, evt.newValue);
                    }
                });
            _spectrumButton.clicked += delegate
            {
                Raise(SpectrumToggleRequested);
            };
            _rateButton.clicked += delegate { Raise(RateClicked); };
        }

        private void BindPropertyPanel()
        {
            _jumpToNoteButton.clicked += delegate
            {
                Raise(JumpToNoteClicked);
            };
        }

        private void BindEventsPanel()
        {
            _eventFilterTrack.AddToClassList("is-track");
            _eventFilterLane.AddToClassList("is-lane");
            _eventsButton.clicked += delegate
            {
                SetEventsPanelOpen(!IsEventsPanelOpen);
            };
            Require<Button>(_root, "CloseEvents").clicked += delegate
            {
                SetEventsPanelOpen(false);
            };
            _eventFilterAll.clicked += delegate
            {
                SetEventFilter(EventFilter.All);
            };
            _eventFilterBpm.clicked += delegate
            {
                SetEventFilter(EventFilter.Bpm);
            };
            _eventFilterTrack.clicked += delegate
            {
                SetEventFilter(EventFilter.Track);
            };
            _eventFilterLane.clicked += delegate
            {
                if (_laneEventsEnabled)
                {
                    SetEventFilter(EventFilter.Lane);
                }
            };
            _addEventButton.clicked += delegate
            {
                SetVisible(_eventAddMenu, !IsElementVisible(_eventAddMenu));
            };
            Require<Button>(_eventAddMenu, "AddBpmEvent").clicked +=
                delegate { RequestAddEvent(ChartEventKind.Bpm); };
            Require<Button>(_eventAddMenu, "AddTrackEvent").clicked +=
                delegate { RequestAddEvent(ChartEventKind.TrackSpeed); };
            _addLaneEventButton.clicked += delegate
            {
                RequestAddEvent(ChartEventKind.LaneLayout);
            };
            _root.RegisterCallback<PointerDownEvent>(
                OnEventsPointerDown,
                TrickleDown.NoTrickleDown);
        }

        public void SetEventsPanelOpen(bool open)
        {
            if (_eventsPanel == null)
            {
                return;
            }
            if (_mobileEventsOverlay != null)
            {
                if (open) CloseMobileNumericEntry();
                var changed = _eventsPanelOpen != open;
                _eventsPanelOpen = open;
                _eventsButton.EnableInClassList("is-active", open);
                _root.Q<Button>("MobileEvents")?.EnableInClassList("is-active", open);
                SetVisible(_eventAddMenu, false);
                SetVisible(_eventsPanel, open);
                SetVisible(_mobileEventsOverlay, open);
                if (open) _mobileEventsOverlay.BringToFront();
                else CloseMobileNumericEntry();
                if (changed) Raise(EventsPanelVisibilityChanged, open);
                return;
            }

            var fullyOpen = open &&
                            _eventsPanelOpen &&
                            Math.Abs(_eventPanelOffset) < 0.5f;
            var fullyClosed = !open &&
                              !_eventsPanelOpen &&
                              _eventsPanel.ClassListContains("hidden");
            if (fullyOpen || fullyClosed)
            {
                return;
            }

            var wasHidden =
                _eventsPanel.ClassListContains("hidden");
            _eventsPanelOpen = open;
            _eventsButton.EnableInClassList("is-active", open);
            if (!open)
            {
                SetVisible(_eventAddMenu, false);
            }
            SetVisible(_eventsPanel, true);

            if (_eventPanelAnimation != null)
            {
                _eventPanelAnimation.Pause();
                _eventPanelAnimation = null;
            }
            var panelWidth = _eventsPanel.resolvedStyle.width;
            if (float.IsNaN(panelWidth) || panelWidth < 1.0f)
            {
                panelWidth = _eventsPanel.layout.width;
            }
            if (panelWidth < 1.0f)
            {
                panelWidth = EventsPanelFallbackWidth;
            }
            _eventPanelAnimationFrom =
                open && wasHidden
                    ? panelWidth
                    : _eventPanelOffset;
            _eventPanelAnimationTo = open ? 0.0f : panelWidth;
            SetEventPanelOffset(_eventPanelAnimationFrom);
            _eventPanelAnimationStart =
                (float)Time.unscaledTimeAsDouble;
            _eventPanelAnimatingOpen = open;
            _eventPanelAnimation = _eventsPanel.schedule
                .Execute((Action)TickEventsPanelAnimation)
                .Every(16);
            Raise(EventsPanelVisibilityChanged, open);
        }

        private void TickEventsPanelAnimation()
        {
            var elapsed =
                (float)Time.unscaledTimeAsDouble -
                _eventPanelAnimationStart;
            var progress = Mathf.Clamp01(
                elapsed / EventsPanelAnimationSeconds);
            var eased = Mathf.SmoothStep(0.0f, 1.0f, progress);
            SetEventPanelOffset(Mathf.LerpUnclamped(
                _eventPanelAnimationFrom,
                _eventPanelAnimationTo,
                eased));
            if (progress < 1.0f)
            {
                return;
            }

            if (_eventPanelAnimation != null)
            {
                _eventPanelAnimation.Pause();
                _eventPanelAnimation = null;
            }
            if (!_eventPanelAnimatingOpen)
            {
                SetVisible(_eventsPanel, false);
            }
        }

        private void SetEventPanelOffset(float offset)
        {
            _eventPanelOffset = offset;
            if (_eventsPanel != null)
            {
                _eventsPanel.style.translate =
                    new StyleTranslate(new Translate(offset, 0.0f));
            }
        }

        public bool ShouldBlockChartScroll(Vector2 screenPosition)
        {
            if (IsPointerOverFloatingToolReport(screenPosition))
            {
                return true;
            }
            // The bank is a wheel boundary too. Consuming the WheelEvent inside
            // the bank list cannot stop the chart: the timeline reads the legacy
            // Input.mouseScrollDelta, which never passes through UITK. This gate
            // is the only place the two can be separated.
            if (IsSkyAreaBankOpen &&
                _skyAreaBankView != null &&
                _skyAreaBankView.ContainsPanelPoint(
                    ScreenToPanelTopLeft(screenPosition)))
            {
                return true;
            }
            if (!_eventsPanelOpen ||
                _eventsPanel == null ||
                _root == null ||
                _root.panel == null)
            {
                return false;
            }
            var focused = GetFocusedElement();
            if (IsWithin(focused, _eventsPanel))
            {
                return true;
            }
            var panelPoint = ScreenToPanelTopLeft(screenPosition);
            return _eventsPanel.worldBound.Contains(panelPoint);
        }

        private void SetEventFilter(EventFilter filter)
        {
            _eventFilter = filter;
            _eventAddMenu.AddToClassList("hidden");
            if (_selectedEvent != null &&
                !MatchesEventFilter(_selectedEvent))
            {
                _selectedEvent = null;
            }
            RenderEventRows();
        }

        private void RequestAddEvent(ChartEventKind kind)
        {
            if (kind == ChartEventKind.LaneLayout &&
                !_laneEventsEnabled)
            {
                return;
            }
            SetVisible(_eventAddMenu, false);
            Raise(EventAddRequested, kind);
        }

        private void OnEventsPointerDown(PointerDownEvent evt)
        {
            if (!IsElementVisible(_eventAddMenu))
            {
                return;
            }
            var target = evt.target as VisualElement;
            if (!IsWithin(target, _eventAddMenu) &&
                !IsWithin(target, _addEventButton))
            {
                SetVisible(_eventAddMenu, false);
            }
        }

        private static bool IsElementVisible(VisualElement element)
        {
            return element != null &&
                   !element.ClassListContains("hidden");
        }

        private void BindMenuHover(
            Button button,
            string menuName,
            Func<MenuEntry[]> createEntries)
        {
            button.RegisterCallback<PointerEnterEvent>(
                delegate
                {
                    if (_activeMenu == null ||
                        _activeMenu == menuName)
                    {
                        return;
                    }
                    OpenMenu(
                        menuName,
                        button,
                        createEntries(),
                        false,
                        true);
                    _menuSwitchedOnHover = true;
                });
        }

        private void OnTopBarPointerDown(PointerDownEvent evt)
        {
            if (_activeMenu == null)
            {
                return;
            }
            var target = evt.target as VisualElement;
            if (IsWithin(target, _fileMenuButton) ||
                IsWithin(target, _editMenuButton) ||
                IsWithin(target, _viewMenuButton) ||
                IsWithin(target, _toolMenuButton))
            {
                return;
            }
            CloseMenu();
        }

        private static bool IsWithin(
            VisualElement element,
            VisualElement ancestor)
        {
            for (var current = element; current != null;
                 current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }
            return false;
        }

        private void ToggleMenu(
            string menuName,
            Button source,
            MenuEntry[] entries)
        {
            if (_activeMenu == menuName)
            {
                if (_menuSwitchedOnHover)
                {
                    _menuSwitchedOnHover = false;
                    return;
                }
                CloseMenu();
                return;
            }
            OpenMenu(menuName, source, entries, false, true);
        }

        private void OpenMenu(
            string menuName,
            Button source,
            MenuEntry[] entries,
            bool focusFirst,
            bool focusPanel = false)
        {
            CloseMenu();
            _activeMenu = menuName;
            _menuFocusSource = source;
            UpdateMenuButtonState();
            SetVisible(_menuDismissLayer, true);
            _menuButtons.Clear();

            _menuPanel = _menuPanelTemplate.Instantiate();
            var items = Require<VisualElement>(
                _menuPanel,
                "MenuItems");
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Hidden)
                {
                    continue;
                }
                if (entry.Separator)
                {
                    var separator =
                        _menuSeparatorTemplate.Instantiate();
                    items.Add(separator);
                    continue;
                }

                var item = _menuItemTemplate.Instantiate();
                var button = Require<Button>(
                    item,
                    "MenuItemButton");
                button.text = string.Empty;
                var label = Require<Label>(
                    item,
                    "MenuItemLabel");
                var shortcut = Require<Label>(
                    item,
                    "MenuItemShortcut");
                button.text = string.Empty;
                label.pickingMode = PickingMode.Ignore;
                shortcut.pickingMode = PickingMode.Ignore;
                label.text = entry.Checked
                    ? L.Get(
                        "falsum.menu.checkedItem",
                        ("label", entry.Label))
                    : entry.Label;
                shortcut.text = entry.Shortcut ?? string.Empty;
                SetVisible(
                    shortcut,
                    !string.IsNullOrWhiteSpace(entry.Shortcut));
                button.SetEnabled(entry.Enabled);
                var captured = entry;
                button.clicked += delegate
                {
                    CloseMenu(true);
                    if (captured.Action != null)
                    {
                        captured.Action();
                    }
                };
                items.Add(item);
                _menuButtons.Add(button);
            }

            PositionPopup(
                _menuPanel,
                source,
                0.0f,
                0.0f);
            _menuPanel.style.top =
                Mathf.Max(0.0f, _topBar.layout.height) + 2.0f;
            _menuLayer.Add(_menuPanel);
            _menuPanel.BringToFront();
            _menuPanel.RegisterCallback<KeyDownEvent>(
                OnMenuKeyDown,
                TrickleDown.TrickleDown);
            if (focusFirst)
            {
                FocusMenuButton(0, 0);
            }
            else if (focusPanel)
            {
                _menuPanel.focusable = true;
                _menuPanel.Focus();
            }
        }

        private void OnMenuKeyDown(KeyDownEvent evt)
        {
            if (_activeMenu == null)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopImmediatePropagation();
                CloseMenu(true);
                return;
            }

            if (evt.keyCode == KeyCode.DownArrow)
            {
                evt.StopImmediatePropagation();
                FocusMenuButton(1, 0);
                return;
            }

            if (evt.keyCode == KeyCode.UpArrow)
            {
                evt.StopImmediatePropagation();
                FocusMenuButton(-1, _menuButtons.Count - 1);
                return;
            }

            if (evt.keyCode == KeyCode.RightArrow)
            {
                evt.StopImmediatePropagation();
                CycleTopMenu(1);
                return;
            }

            if (evt.keyCode == KeyCode.LeftArrow)
            {
                evt.StopImmediatePropagation();
                CycleTopMenu(-1);
            }
        }

        private void FocusMenuButton(
            int direction,
            int fallbackIndex)
        {
            if (_menuButtons.Count == 0)
            {
                return;
            }
            var focused = GetFocusedElement();
            var current = _menuButtons.IndexOf(focused as Button);
            var index = current < 0
                ? Mathf.Clamp(
                    fallbackIndex,
                    0,
                    _menuButtons.Count - 1)
                : (current +
                   direction +
                   _menuButtons.Count) %
                  _menuButtons.Count;
            for (var step = 0; step < _menuButtons.Count; step++)
            {
                if (_menuButtons[index].enabledSelf)
                {
                    _menuButtons[index].Focus();
                    return;
                }
                index = (index + direction + _menuButtons.Count) %
                        _menuButtons.Count;
            }
        }

        private void CycleTopMenu(int direction)
        {
            var buttons = new[]
            {
                _fileMenuButton,
                _editMenuButton,
                _viewMenuButton,
                _toolMenuButton,
            };
            var index = Array.IndexOf(buttons, _menuFocusSource);
            if (index < 0)
            {
                index = 0;
            }
            index = (index + direction + buttons.Length) % buttons.Length;
            var button = buttons[index];
            MenuEntry[] entries;
            if (button == _fileMenuButton)
            {
                entries = CreateFileMenuEntries();
                OpenMenu("FileMenu", button, entries, true);
            }
            else if (button == _editMenuButton)
            {
                entries = CreateEditMenuEntries();
                OpenMenu("EditMenu", button, entries, true);
            }
            else if (button == _viewMenuButton)
            {
                entries = CreateViewMenuEntries();
                OpenMenu("ViewMenu", button, entries, true);
            }
            else
            {
                entries = CreateToolMenuEntries();
                OpenMenu("ToolMenu", button, entries, true);
            }
        }

        private void UpdateMenuButtonState()
        {
            _fileMenuButton.EnableInClassList(
                "is-open",
                _activeMenu == "FileMenu");
            _editMenuButton.EnableInClassList(
                "is-open",
                _activeMenu == "EditMenu");
            _viewMenuButton.EnableInClassList(
                "is-open",
                _activeMenu == "ViewMenu");
            _toolMenuButton.EnableInClassList(
                "is-open",
                _activeMenu == "ToolMenu");
        }

        private void CloseMenu(bool restoreFocus = false)
        {
            var focusTarget = restoreFocus ? _menuFocusSource : null;
            _activeMenu = null;
            _menuSwitchedOnHover = false;
            _menuButtons.Clear();
            _menuFocusSource = null;
            UpdateMenuButtonState();
            SetVisible(_menuDismissLayer, false);
            if (_menuPanel != null)
            {
                _menuPanel.RemoveFromHierarchy();
                _menuPanel = null;
            }
            RestoreFocus(focusTarget);
        }

        private void AddPropertyRow(
            string key,
            string label,
            object value)
        {
            var instance = _propertyRowTemplate.Instantiate();
            var labelElement = Require<Label>(
                instance,
                "PropertyLabel");
            var field = Require<TextField>(
                instance,
                "PropertyField");
            var button = Require<Button>(
                instance,
                "PropertyButton");
            labelElement.text = label;
            field.SetValueWithoutNotify(
                Convert.ToString(value, Invariant));
            button.AddToClassList("hidden");
            var capturedKey = key;
            var allowFraction =
                key == "flick_position" ||
                key == "flick_width" ||
                key == "sky_start" ||
                key == "sky_start_width" ||
                key == "sky_end" ||
                key == "sky_end_width";
            RegisterNumericInput(
                field,
                value => Raise(
                    PropertySubmitted,
                    capturedKey,
                    value),
                allowFraction);
            BindMobileNumericInput(field, label, allowFraction);
            _propertyRowsView.contentContainer.Add(instance);
            _propertyRows.Add(new PropertyRow
            {
                Key = key,
                Root = instance,
                Field = field,
                Button = null,
            });
        }

        private void AddFlickDirectionRow(
            string key,
            string label,
            int direction)
        {
            var instance = _propertyRowTemplate.Instantiate();
            instance.AddToClassList("flick-direction-row");
            var labelElement = Require<Label>(
                instance,
                "PropertyLabel");
            var field = Require<TextField>(
                instance,
                "PropertyField");
            var unusedButton = Require<Button>(
                instance,
                "PropertyButton");
            labelElement.text = label;
            field.RemoveFromHierarchy();
            unusedButton.RemoveFromHierarchy();

            var toggle = new VisualElement();
            toggle.name = "FlickDirectionToggle";
            toggle.AddToClassList("flick-direction-toggle");
            var left = new Button
            {
                name = "FlickDirectionLeft",
                text = L.Get("falsum.direction.left"),
            };
            var right = new Button
            {
                name = "FlickDirectionRight",
                text = L.Get("falsum.direction.right"),
            };
            left.AddToClassList("flick-direction-button");
            left.AddToClassList("flick-direction-button-first");
            right.AddToClassList("flick-direction-button");
            left.EnableInClassList(
                "is-selected",
                direction == 16);
            right.EnableInClassList(
                "is-selected",
                direction != 16);
            var capturedKey = key;
            left.clicked += delegate
            {
                left.AddToClassList("is-selected");
                right.RemoveFromClassList("is-selected");
                Raise(
                    PropertySubmitted,
                    capturedKey,
                    "16");
            };
            right.clicked += delegate
            {
                right.AddToClassList("is-selected");
                left.RemoveFromClassList("is-selected");
                Raise(
                    PropertySubmitted,
                    capturedKey,
                    "4");
            };
            toggle.Add(left);
            toggle.Add(right);
            instance.Add(toggle);
            _propertyRowsView.contentContainer.Add(instance);
            _propertyRows.Add(new PropertyRow
            {
                Key = key,
                Root = instance,
                Field = null,
                Button = direction == 16 ? left : right,
            });
            instance.RegisterCallback<GeometryChangedEvent>(
                delegate
                {
                    SyncFlickDirectionLayout(instance, toggle);
                });
            instance.schedule.Execute(
                (Action)delegate
                {
                    SyncFlickDirectionLayout(instance, toggle);
                });
        }

        private void SyncFlickDirectionLayout(
            VisualElement row,
            VisualElement toggle)
        {
            float left = 0.0f;
            var width = 0.0f;
            for (var i = 0; i < _propertyRows.Count; i++)
            {
                var field = _propertyRows[i].Field;
                if (field != null && field.layout.width > 0.0f)
                {
                    left = field.layout.x;
                    width = field.layout.width;
                    break;
                }
            }
            if (width <= 0.0f ||
                row.layout.height <= 0.0f)
            {
                return;
            }
            var top = Math.Max(
                0.0f,
                (row.layout.height - toggle.layout.height) * 0.5f);
            toggle.style.position = Position.Absolute;
            toggle.style.left = left;
            toggle.style.top = top;
            toggle.style.width = width;
        }

        private void AddEaseDropdown(
            string key,
            string label,
            int current)
        {
            var instance = _propertyRowTemplate.Instantiate();
            var labelElement = Require<Label>(
                instance,
                "PropertyLabel");
            var field = Require<TextField>(
                instance,
                "PropertyField");
            var button = Require<Button>(
                instance,
                "PropertyButton");
            labelElement.text = label;
            field.AddToClassList("hidden");
            button.RemoveFromClassList("hidden");
            button.text = EaseName(current);
            var capturedKey = key;
            var capturedButton = button;
            capturedButton.clicked += delegate
            {
                ShowEaseDropdown(capturedKey, capturedButton);
            };
            _propertyRowsView.contentContainer.Add(instance);
            _propertyRows.Add(new PropertyRow
            {
                Key = key,
                Root = instance,
                Field = null,
                Button = button,
            });
        }

        private void ShowEaseDropdown(
            string key,
            Button source)
        {
            if (_easeDropdown != null)
            {
                _easeDropdown.RemoveFromHierarchy();
            }
            _easeDropdown = _menuPanelTemplate.Instantiate();
            _easeDropdown.AddToClassList("ease-dropdown");
            var items = Require<VisualElement>(
                _easeDropdown,
                "MenuItems");
            var values = new[] { 0, 1, 2 };
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var item = _menuItemTemplate.Instantiate();
                var button = Require<Button>(
                    item,
                    "MenuItemButton");
                var label = Require<Label>(
                    item,
                    "MenuItemLabel");
                var shortcut = Require<Label>(
                    item,
                    "MenuItemShortcut");
                label.text = EaseName(value);
                shortcut.text = string.Empty;
                shortcut.style.display = DisplayStyle.None;
                button.clicked += delegate
                {
                    if (_easeDropdown != null)
                    {
                        _easeDropdown.RemoveFromHierarchy();
                        _easeDropdown = null;
                    }
                    Raise(
                        PropertySubmitted,
                        key,
                        value.ToString(Invariant));
                };
                items.Add(item);
            }
            PositionPopup(
                _easeDropdown,
                source,
                10.0f,
                -4.0f);
            _menuLayer.Add(_easeDropdown);
            _easeDropdown.BringToFront();
            CloseMenu();
        }

        private void ClearPropertyRows()
        {
            if (_easeDropdown != null)
            {
                _easeDropdown.RemoveFromHierarchy();
                _easeDropdown = null;
            }
            if (_propertyRowsView != null)
            {
                _propertyRowsView.contentContainer.Clear();
            }
            _propertyRows.Clear();
        }

        private void PositionPopup(
            VisualElement popup,
            VisualElement source,
            float xOffset,
            float yOffset)
        {
            if (_root == null || source == null || popup == null)
            {
                return;
            }
            var rootBound = _root.worldBound;
            var sourceBound = source.worldBound;
            popup.style.left = sourceBound.xMin - rootBound.xMin +
                                xOffset;
            popup.style.top = sourceBound.yMax - rootBound.yMin +
                              yOffset;
        }

        private void ShowBuildError(string message)
        {
            if (_root == null)
            {
                return;
            }
            var label = new Label(
                L.Get(
                    "falsum.error.ui.initializationFailed",
                    ("error", message)));
            label.AddToClassList("build-error");
            label.style.position = Position.Absolute;
            label.style.left = 24;
            label.style.top = 24;
            label.style.right = 24;
            label.style.paddingLeft = 18;
            label.style.paddingRight = 18;
            label.style.paddingTop = 14;
            label.style.paddingBottom = 14;
            _root.Add(label);
        }

        private VisualElement GetFocusedElement()
        {
            if (_root == null || _root.panel == null)
            {
                return null;
            }
            return _root.panel.focusController.focusedElement
                as VisualElement;
        }

        private static void RestoreFocus(VisualElement target)
        {
            if (target != null &&
                target.panel != null &&
                target.focusable)
            {
                target.Focus();
            }
        }

        private static void TrapFocus(
            VisualElement root,
            KeyDownEvent evt)
        {
            if (root == null)
            {
                evt.StopImmediatePropagation();
                return;
            }

            var candidates = new List<VisualElement>();
            root.Query<VisualElement>().ForEach(
                element =>
                {
                    if (!element.focusable ||
                        !element.enabledSelf ||
                        element.resolvedStyle.display ==
                        DisplayStyle.None)
                    {
                        return;
                    }
                    candidates.Add(element);
                });
            if (candidates.Count == 0)
            {
                evt.StopImmediatePropagation();
                return;
            }

            var focused = root.panel != null
                ? root.panel.focusController.focusedElement as VisualElement
                : null;
            var current = candidates.IndexOf(focused);
            var reverse =
                (evt.modifiers & EventModifiers.Shift) != 0;
            var next = current < 0
                ? reverse ? candidates.Count - 1 : 0
                : (current +
                   (reverse ? -1 : 1) +
                   candidates.Count) %
                  candidates.Count;
            candidates[next].Focus();
            evt.StopImmediatePropagation();
        }

        private Vector2 ScreenToPanelTopLeft(Vector2 screenPosition)
        {
            var raw = RuntimePanelUtils.ScreenToPanel(
                _root.panel,
                screenPosition);
            return new Vector2(
                raw.x,
                _root.layout.height - raw.y);
        }

        private bool IsFocused(TextField field)
        {
            var focused = GetFocusedElement();
            for (var current = focused; current != null;
                 current = current.parent)
            {
                if (current == field)
                {
                    return true;
                }
            }
            return false;
        }

        private static void SetVisible(
            VisualElement element,
            bool visible)
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

        private static T Require<T>(
            VisualElement root,
            string name)
            where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.ui.missingTypedElement",
                        ("element", name),
                        ("type", typeof(T).Name)));
            }
            return element;
        }

        private static string EaseName(int easing)
        {
            if (easing == 1)
            {
                return L.Get("falsum.easing.easeOut");
            }
            if (easing == 2)
            {
                return L.Get("falsum.easing.easeIn");
            }
            return L.Get("falsum.easing.line");
        }

        private static string ToTimeText(double ms)
        {
            return Math.Max(0.0, ms).ToString("0", Invariant);
        }

        private static void Raise(Action action)
        {
            if (action != null)
            {
                action();
            }
        }

        private void RegisterNumericInput(
            TextField field,
            Action<string> submit,
            bool allowFraction = false)
        {
            if (field == null)
            {
                return;
            }
            field.AddToClassList("falsum-numeric-input");
            field.RegisterCallback<KeyDownEvent>(
                delegate(KeyDownEvent evt)
                {
                    if (IsTextFieldEditShortcut(evt))
                    {
                        return;
                    }
                    if (evt.keyCode == KeyCode.Return ||
                        evt.keyCode == KeyCode.KeypadEnter)
                    {
                        var value = SanitizeNumericField(
                            field,
                            field.value,
                            allowFraction);
                        if (submit != null)
                        {
                            submit(value);
                        }
                        ClearInputFocus();
                        evt.StopImmediatePropagation();
                        return;
                    }
                    if (evt.character < ' ')
                    {
                        return;
                    }
                    var character = NormalizeNumericCharacter(
                        evt.character,
                        allowFraction);
                    if (!IsNumericCharacter(
                            character,
                            allowFraction))
                    {
                        evt.StopImmediatePropagation();
                    }
                });
            field.RegisterValueChangedCallback(
                delegate(ChangeEvent<string> evt)
                {
                    var value = SanitizeNumericField(
                        field,
                        evt.newValue,
                        allowFraction);
                    if (submit != null)
                    {
                        submit(value);
                    }
                });
        }

        private static string SanitizeNumericField(
            TextField field,
            string value,
            bool allowFraction)
        {
            var sanitized = SanitizeNumericText(
                value,
                allowFraction);
            if (field != null &&
                !string.Equals(
                    field.value,
                    sanitized,
                    StringComparison.Ordinal))
            {
                field.SetValueWithoutNotify(sanitized);
            }
            return sanitized;
        }

        private static string SanitizeNumericText(
            string text,
            bool allowFraction)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var character = NormalizeNumericCharacter(
                    text[i],
                    allowFraction);
                if (IsNumericCharacter(
                        character,
                        allowFraction))
                {
                    builder.Append(character);
                }
            }
            return builder.ToString();
        }

        private static char NormalizeNumericCharacter(
            char character,
            bool allowFraction)
        {
            if (character >= '\uFF10' &&
                character <= '\uFF19')
            {
                return (char)(character - '\uFF10' + '0');
            }
            if (character == '\uFF0D')
            {
                return '-';
            }
            if (character == '\uFF0B')
            {
                return '+';
            }
            if (character == '\uFF0E')
            {
                return '.';
            }
            if (character == '\uFF0F' &&
                allowFraction)
            {
                return '/';
            }
            return character;
        }

        private static bool IsNumericCharacter(
            char character,
            bool allowFraction)
        {
            return (character >= '0' && character <= '9') ||
                   character == '+' ||
                   character == '-' ||
                   character == '.' ||
                   (allowFraction && character == '/');
        }

        private void Raise<T>(Action<T> action, T value)
        {
            if (action != null)
            {
                action(value);
            }
        }

        private void Raise<T1, T2>(
            Action<T1, T2> action,
            T1 first,
            T2 second)
        {
            if (action != null)
            {
                action(first, second);
            }
        }
    }
}
