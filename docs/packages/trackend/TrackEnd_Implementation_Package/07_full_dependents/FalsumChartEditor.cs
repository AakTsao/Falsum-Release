using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Tools;
using InFalsus.Gameplay.Falsum.UI;
using InFalsus.Gameplay.Runtime.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed partial class FalsumChartEditor : MonoBehaviour
    {
        private enum EditorMode
        {
            Select,
            SegmentSelect,
            ToolNotePick,
            PointPlace,
            PlaceTap,
            PlaceHold,
            PlaceFlick,
            PlaceSkyArea,
            PastePreview,
        }

        private enum ChartPreviewPhase
        {
            Inactive,
            WaitingForChrome,
            CountIn,
            WaitingForPlayback,
            Playback,
        }

        private sealed class DragState
        {
            public ChartNote Note;
            public double TimeMs;
            public int Lane;
            public double DurationMs;
        }

        private sealed class ClipboardNote
        {
            public ChartNote Note;
            public double TimeOffsetMs;
        }

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;
        private static readonly double[] PlaybackRates =
        {
            1.0,
            0.75,
            0.5,
            0.25,
        };
        private const double SkyAreaPointWidth = 0.0001;

        /// <summary>
        /// Temporal span of a not-yet-extended SkyArea point-place preview.
        /// Small enough to read as a sliver at the cursor rather than as a
        /// committed span; the real length is not known until the second edge.
        /// </summary>
        private const double SkyAreaPointSpanMs = 12.0;
        // Interactive grid only; chart fractions use FalsumFraction.MaxDenominator.
        // An alias of the Core ceiling (invariant A-8): the ruler, the mode
        // plane, the snapping helpers and the denominator ladder all clamp
        // against one number, so none of them can drift.
        private const int MaxEditableFlickGrid = FalsumBeatGrid.MaxGridDenominator;
        // Denominator a fresh session starts on. A preference sequence that
        // does not carry 24 re-seats the value on the nearest member.
        private const int DefaultFlickDenominator = 24;
        private const float SelectionDragThresholdPixels = 8.0f;
        private const float PickCyclePositionTolerancePixels = 6.0f;
        private const float EditModeTrackScaleMultiplier = 1.04f;
        private const double DefaultAutoBackupIntervalSeconds = 180.0;
        private static string NoChartPrompt
        {
            get { return L.Get("falsum.editor.noChartPrompt"); }
        }
        private const double PreviewTapIntervalBeats = 1.0;
        private const double PreviewNoteLeadBeats = 4.0;
        private const double PreviewStartDelaySeconds = 1.0;
        private const int MaxMetadataHistorySnapshots = 512;
        private const int SpectrumSegmentLookahead = 1;
        private static string NegativeMusicTimeWarning
        {
            get { return L.Get("falsum.editor.negativeMusicTimeWarning"); }
        }

        private GameplayBootstrap _host;
        private Chart _chart;
        private FalsumEditorView _view;
        private float _chartPreviewBaseFieldOfView = 60.0f;
        private bool _hasChartPreviewBaseFieldOfView;
        private FalsumModePlane _modePlane;
        private FalsumRadialMenu _radial;
        private FalsumEditorWorld _world;
        private Falsum2DWorld _world2D;
        private Canvas _selectionCanvas;
        private Canvas _selectionCanvas2D;
        private FalsumBeatGrid _grid;
        private FalsumChartSession _session;
        private FalsumToolManager _toolManager;
        private FalsumToolRuntime _activeToolRuntime;
        private FalsumToolRunOutput _pendingToolOutput;
        private FalsumToolManifest _pendingToolManifest;
        private FalsumToolReport _pendingToolReport;
        private bool _toolTimePickActive;
        private double _toolTimePickCandidateMs;
        private bool _toolNotePickActive;
        private FalsumToolNotePickRequest _toolNotePickRequest;
        private EditorMode _toolNotePickReturnMode;
        private FalsumNotePickMask _toolNotePickReturnMask;
        private readonly List<ChartNote> _toolNotePickPreviousSelection =
            new List<ChartNote>();
        private ChartEvent _chartBaseEvent;
        private FalsumManifest _manifest;
        private FalsumPackage _package;
        private FalsumPreferences _preferences;
        private FalsumInputRouter _inputRouter;
        private FalsumPerformanceMonitor _performanceMonitor;
        private FalsumChartMetadataSnapshot _pendingPreferencesSnapshot;
        private FalsumPreferences _pendingPreferences;
        private FalsumChartMetadataDraft _pendingPreferencesMetadata;
        private readonly Dictionary<string, FalsumChartMetadataSnapshot>
            _metadataHistorySnapshots =
                new Dictionary<string, FalsumChartMetadataSnapshot>(
                    StringComparer.Ordinal);
        private readonly Queue<string> _metadataHistoryOrder =
            new Queue<string>();
        private string _backgroundBeforePreferences;
        private bool _preferencesMetadataApplied;
        private bool _preferencesPreviewActive;
        private bool _initialized;
        private bool _spaceHeld;
        private bool _hasWindowedSizeBeforeFullscreen;
        private Vector2Int _windowedSizeBeforeFullscreen;
        private double _spaceStartMs;
        private ChartPreviewPhase _chartPreviewPhase;
        private int _chartPreviewRateIndex;
        private bool _chartPreviewGridWasEnabled;
        private int _chartPreviewTapsRemaining;
        private double _chartPreviewNextTapAt;
        private double _chartPreviewTapIntervalSeconds;
        private double _chartPreviewBeatMs;
        private int _chartPreviewBeatCount;
        private double _chartPreviewFirstNoteMs;
        private double _chartPreviewPlaybackStartMs;
        private double _chartPreviewPlaybackAt;
        private EditorMode _mode;
        private double _playheadMs;
        private double _hintUntil;
        private double _nextTimelineTextUpdateAt;

        /// <summary>
        /// Cadence for the frame-invariant top-bar values (speed, delay, BPM,
        /// subdivision, playback rate). They can still change mid-playback --
        /// Ctrl+wheel edits flow speed -- so they cannot be skipped outright,
        /// but they do not need a call on every frame either.
        /// </summary>
        private double _nextStaticChromeUpdateAt;
        private string _persistentHint;
        private int _subdivision = 4;
        private float _flowSpeed = 5.0f;
        private int _rateIndex;
        private bool _disableOutOfBoundsClamping;
        private int _currentDifficulty = 3;
        private int _tapWidth = 1;
        private bool _isDirty;
        // SkyArea groups are aggregated by GroupId, which only
        // FalsumSkyAreaGrouping.Normalize recomputes. Any chart change can
        // alter that grouping (including deleting or moving a non-SkyArea
        // note between two segments), so the flag is set on every change and
        // the rebuild happens once per frame instead of per edit kind.
        private bool _skyGroupingDirty;
        // Chart-preview takeover of the track-effect switch. The chart preview
        // must always show the real track() behaviour, so entering preview
        // forces effects on and restores the editor's own state on exit — the
        // same save/restore shape the grid visibility already uses there.
        private bool _chartPreviewTrackSpeedEffectsWasEnabled;
        private bool _allowQuit;
        private int _packageLoadGeneration;
        private string _packageAudioPath;
        private string _packageAudioHash;
        private int _packageAudioDurationMs;
        private Coroutine _packageAudioLoad;
        private Coroutine _packageSpectrumLoad;
        private CancellationTokenSource _spectrumCancellation;
        private readonly FalsumTrackWaveformCache _waveformCache =
            new FalsumTrackWaveformCache();
        private bool _spectrumVisible;
        private bool _spectrumAvailable;
        private bool _spectrumPending;
        private string _lastDifficultySwitchError;
        private bool _autoBackupEnabled = true;
        private double _autoBackupIntervalSeconds =
            DefaultAutoBackupIntervalSeconds;
        private int _backupRetentionCount = 20;
        private bool _newChartCreateInProgress;
        private double _nextAutoBackupTime;
        private Task<string> _autoBackupTask;
        private FalsumNotePickMask _selectionMask =
            FalsumNotePickMask.All;
        private FalsumViewMode _viewMode =
            FalsumViewMode.Perspective3D;
        private bool _showMeasureNumbers = true;
        private FalsumChartLayer _layerVisibility =
            FalsumChartLayer.Both;
        private readonly List<ChartNote> _selected = new List<ChartNote>();
        private readonly List<ChartNote> _pickCycleCandidates =
            new List<ChartNote>();
        private Vector2 _pickCycleScreenPosition;
        private bool _hasPickCycle;
        private bool _advancePickCycleOnRelease;
        private int _pickCycleIndex = -1;
        private readonly List<DragState> _dragStates = new List<DragState>();
        private ChartNote _selectionDragAnchor;
        private bool _directDragUnselectedNotes = true;
        private readonly List<ClipboardNote> _clipboard =
            new List<ClipboardNote>();
        private readonly List<ClipboardNote> _pastePreviewClipboard =
            new List<ClipboardNote>();
        private readonly List<ChartNote> _pastePreviewNotes =
            new List<ChartNote>();
        private readonly List<FalsumEditorGhost> _pastePreviewGhosts =
            new List<FalsumEditorGhost>();
        private ChartNote _pastePreviewAnchor;
        private EditorMode _pastePreviewReturnMode = EditorMode.Select;
        private bool _draggingSelection;
        private bool _selectionDragPending;
        private Vector2 _selectionDragOrigin;
        private double _dragStartRawTime;
        private int _dragStartLane;
        private double _pointerTime;
        private double _pointerRawTime;
        private float _pointerLanePosition;
        private int _pointerLane;
        private bool _hasPointer;
        private bool _segmentHasStart;
        private double _segmentStart;
        private double _segmentEnd;
        private bool _holdPlaced;
        private double _holdStart;
        private int _holdLane;
        private int _holdWidth;
        private bool _flickHasTime;
        private double _flickTime;
        private bool _flickDragging;
        private double _flickStart;
        private double _flickCurrent;
        private int _flickDenominator = DefaultFlickDenominator;
        // Authored denominator ladder the `-` / `=` keys walk. Seeded from the
        // preference default and replaced by ApplyGridDenominatorSequence; one
        // sequence feeds the Flick / SkyArea grid and the middle-edit grid so
        // both consumers cannot disagree about which denominators exist.
        private List<int> _gridDenominatorSequence;
        // Cached membership index of _flickDenominator, -1 when the current
        // value is not on the ladder (a native 1/200 note, for instance).
        private int _denominatorIndex = -1;
        // Ruler state an anchor drag contributes while it runs a plane-facing
        // middle edit. The anchor layer only forwards normalized positions and
        // reads the shared state's grid/time (invariant A-18); the cell math
        // and the plane text stay here.
        private bool _anchorPlaneVisible;
        private FalsumMiddleEditMode _anchorPlaneMode =
            FalsumMiddleEditMode.Boundary;
        private int _anchorPlaneGrid;
        /// <summary>
        /// Kind of the note the anchor drag is editing. Decides whether the
        /// ruler hangs from the sky plane or stands on the track floor, so it
        /// must be captured when the drag publishes its state rather than
        /// re-derived later from a selection that may have moved on.
        /// </summary>
        private NoteKind _anchorPlaneKind = NoteKind.Tap;
        private double _anchorPlaneTimeMs;
        private float _anchorPlaneStartNormalized = float.NaN;
        private float _anchorPlaneEndNormalized = float.NaN;
        private int _skyStage;
        private bool _skyDragging;
        private bool _skyStartFirstEdge;
        private bool _skyEndFirstEdge;
        private bool _skyCompletingStart;
        private bool _skyCompletingEnd;
        private double _skyStartTime;
        private double _skyStartAnchor;
        private double _skyStartPos;
        private double _skyStartWidth;
        private double _skyEndTime;
        private double _skyEndAnchor;
        private double _skyEndPos;
        private double _skyEndWidth;
        private double _lastSelectionVisualRefresh;
        private bool _debugPointerOverride;
        private int _modePlaneFocusLine = -1;
        private int _modePlaneDivisions;

        public void Initialize(GameplayBootstrap host)
        {
            if (_initialized)
            {
                return;
            }
            _host = host;
            _chart = host.CurrentChart;
            var preserveChartForCapture =
                Environment.GetEnvironmentVariable(
                    "INFALSUS_CAPTURE_PRESERVE_CHART") == "1";
            if (!preserveChartForCapture)
            {
                ClearChartForEmptyEditor(_chart);
            }
            _grid = new FalsumBeatGrid
            {
                Enabled = true,
                Subdivision = 4,
            };
            _session = new FalsumChartSession(_chart, _chart.DelayMs);
            _session.Changed += OnSessionChanged;
            _toolManager = new FalsumToolManager();
            _toolManager.Initialize();
            _preferences = FalsumPreferencesStore.Load();
            EnsureGridDenominatorSequence();
            _inputRouter = new FalsumInputRouter();
            try
            {
                FalsumCacheStore.DeleteWaveformCacheFiles();
            }
            catch (Exception)
            {
            }
            ApplyPreferencesToRuntime(_preferences);
            _flowSpeed = Mathf.Clamp(
                (float)_host.Motion.Speed,
                1.0f,
                10.0f);
            _performanceMonitor = new FalsumPerformanceMonitor();
            _view = new FalsumEditorView(transform);
            _view.Build();
            _view.SetUiStyle(_preferences.Advanced.UIStyle);
            _view.SetPerformanceMonitor(_performanceMonitor);
            _view.SetSpectrumState(false, false, false);
            CaptureChartPreviewBaseFieldOfView();
            _view.SetGridEnabled(_grid.Enabled);
            _showMeasureNumbers =
                _preferences.Advanced.ShowMeasureNumbers;
            _view.SetMeasureNumbersEnabled(_showMeasureNumbers);
            // Track effects are a per-session preview switch and are never
            // persisted, so a fresh editor always starts with them enabled.
            // Charts loaded later keep this flag in Chart (see Initialize on
            // load paths) — this only syncs the freshly built view.
            SetTrackSpeedEffectsEnabled(
                _chart.TrackSpeedEffectsEnabled,
                announce: false,
                refresh: false);
            _view.SetToolCatalog(_toolManager.Tools);
            _view.SetShortcutPreferences(_preferences);
            _modePlane = new FalsumModePlane(
                _host.Binding.MasterScene);
            _modePlane.Build();
            _radial = new FalsumRadialMenu(_view.RadialLayer);
            _selectionCanvas = CreateSelectionCanvas(transform);
            // Captions of the world-space mode plane are drawn flat on the
            // overlay canvas so they stay readable at any distance.
            _modePlane.AttachOverlay(_selectionCanvas);
            _world = new FalsumEditorWorld(
                host.Binding,
                _chart,
                host.Motion,
                _grid,
                host.NoteVisuals,
                _selectionCanvas);
            _selectionCanvas2D = CreateSelectionCanvas(transform);
            _world2D = new Falsum2DWorld(
                host.Binding,
                _chart,
                _grid,
                _selectionCanvas2D);
            _world2D.SetFlowSpeed(_flowSpeed);
            WireView();
            // AFTER WireView, and this ordering is load-bearing: the phone
            // shell forwards its buttons onto this view's events, so mounting
            // it any earlier wires them into unsubscribed delegates -- every
            // control clicks and does nothing. On a desktop session this is a
            // no-op (FalsumMobileShell.IsMobile is false).
            _view.MountMobileShellIfNeeded();
            ShowFirstRunDisclaimerIfNeeded();
            RefreshMiddleEditModeStatus();
            _persistentHint = NoChartPrompt;
            InitializeManifest();
            _isDirty = false;
            _nextAutoBackupTime =
                Time.unscaledTimeAsDouble +
                _autoBackupIntervalSeconds;
            _mode = EditorMode.Select;
            _playheadMs = 0.0;
            _host.SetEditorMode(!preserveChartForCapture);
            _host.SetGameplayBackground(
                FalsumManifest.DefaultBackgroundFile);
            RefreshEditorVisuals();
            RefreshEditorAtChartTime(_playheadMs);
            _view.SetPlaybackState(false);
            UpdateViews();
            ApplyDisplayPreferencesAtStartup();
            ApplyPersistedMobileLayout();
            _initialized = true;
            Application.wantsToQuit += OnWantsToQuit;
            ShowHint(NoChartPrompt, 4.0f);
        }

        public int DebugSelectedCount
        {
            get { return _selected.Count; }
        }

        /// <summary>
        /// Drives the developer mobile-layout switch from a capture tool or
        /// the self check, through the exact same path the preferences
        /// toggle uses. Returns the layout actually in effect afterwards,
        /// because the switch can refuse (see SetMobileLayoutForPages).
        /// </summary>
        public bool DebugSetMobileLayout(bool mobile)
        {
            SetMobileLayout(mobile);
            return _view != null && _view.MobileShell != null;
        }

        public int DebugAutoCombo
        {
            get { return _host != null ? _host.AutoCombo : 0; }
        }

        public bool DebugAutoComboVisible
        {
            get { return _host != null && _host.AutoComboVisible; }
        }

        public string DebugSelectedKinds
        {
            get
            {
                var tap = false;
                var hold = false;
                var flick = false;
                var skyArea = false;
                for (var i = 0; i < _selected.Count; i++)
                {
                    switch (_selected[i].Kind)
                    {
                        case NoteKind.Hold:
                            hold = true;
                            break;
                        case NoteKind.Flick:
                            flick = true;
                            break;
                        case NoteKind.SkyArea:
                            skyArea = true;
                            break;
                        default:
                            tap = true;
                            break;
                    }
                }
                var kinds = new List<string>();
                if (tap)
                {
                    kinds.Add("TAP");
                }
                if (hold)
                {
                    kinds.Add("HOLD");
                }
                if (flick)
                {
                    kinds.Add("FLICK");
                }
                if (skyArea)
                {
                    kinds.Add("SKYAREA");
                }
                return string.Join("/", kinds.ToArray());
            }
        }

        public bool DebugPackageLoaded
        {
            get { return _package != null; }
        }

        public bool DebugSpectrumReady
        {
            get
            {
                return _spectrumAvailable &&
                       _world != null &&
                       _world.SpectrumReady;
            }
        }

        public bool DebugSpectrumVisible
        {
            get { return _spectrumVisible; }
        }

        public string DebugSpectrumSummary
        {
            get
            {
                return "visible=" + _spectrumVisible +
                       " available=" + _spectrumAvailable +
                       " pending=" + _spectrumPending +
                       " " +
                       (_world != null
                           ? _world.SpectrumDebugSummary
                           : "world=<null>");
            }
        }

        public bool DebugTrackSpeedEffectsEnabled
        {
            get
            {
                return _chart != null &&
                       _chart.TrackSpeedEffectsEnabled;
            }
        }

        public void DebugSetTrackSpeedEffects(bool enabled)
        {
            SetTrackSpeedEffectsEnabled(enabled);
        }

        public void DebugSetSpectrumVisible(bool visible)
        {
            if (_spectrumVisible != visible &&
                (_spectrumAvailable || _spectrumPending))
            {
                ToggleSpectrum();
            }
        }

        public bool DebugGridEnabled
        {
            get { return _grid != null && _grid.Enabled; }
        }

        public int DebugGridVertexCount
        {
            get { return _world != null ? _world.GridVertexCount : 0; }
        }

        private bool HasUnsavedChanges
        {
            get
            {
                return _isDirty ||
                       (_session != null &&
                        _session.HasPendingEdit);
            }
        }

        public string DebugBackgroundFile
        {
            get
            {
                return _manifest != null
                    ? _manifest.EffectiveBackgroundFile
                    : string.Empty;
            }
        }

        public void DebugShowSongInfoDifficultyMenu()
        {
            if (_view != null)
            {
                _view.DebugShowSongInfoDifficultyMenu();
            }
        }

        public void DebugOpenToolManager()
        {
            OpenToolManager();
        }

        public void DebugOpenTrackTool()
        {
            OpenToolInternal(
                "com.falsum.tools.track-easing",
                false);
        }

        public void DebugOpenToolNotePicker()
        {
            if (_view == null)
            {
                return;
            }
            PrepareForToolInteraction();
            if (_chart != null && _chart.Notes.Count < 2)
            {
                _chart.Notes.Add(
                    new ChartNote
                    {
                        Id = 1000,
                        Kind = NoteKind.Tap,
                        TimeMs = Math.Max(
                            0.0,
                            Math.Round(_playheadMs) + 500.0),
                        StartLane = 1,
                        WidthLanes = 1,
                    });
                _chart.Notes.Add(
                    new ChartNote
                    {
                        Id = 1001,
                        Kind = NoteKind.Hold,
                        TimeMs = Math.Max(
                            0.0,
                            Math.Round(_playheadMs) + 1000.0),
                        DurationMs = 600.0,
                        StartLane = 2,
                        WidthLanes = 1,
                    });
                _chart.RebuildTimeline();
                RefreshEditorVisuals();
            }
            var manifest = new FalsumToolManifest
            {
                ApiVersion = 1,
                Engine = "javascript",
                Id = "com.falsum.tools.debug-note-picker",
                Name = "Note Picker",
                Version = "1.0.0",
                Enabled = true,
            };
            var form = FalsumToolFormDefinition.FromJson(
                FalsumJson.Parse(
                    "{\"title\":\"Note Picker\",\"fields\":[" +
                    "{\"key\":\"targets\",\"type\":\"note\"," +
                    "\"label\":\"Targets\",\"required\":true," +
                    "\"multiple\":true,\"min\":1,\"max\":4}]}"));
            _view.ShowTool(manifest, form);
            _view.ToolDialog.DebugBeginFirstNotePick();
        }

        public int DebugSelectToolNotePickNotes(int count)
        {
            if (!_toolNotePickActive || _chart == null)
            {
                return 0;
            }
            _selected.Clear();
            var max = _toolNotePickRequest != null
                ? _toolNotePickRequest.MaxCount
                : 0;
            count = Math.Max(0, Math.Min(count, max));
            for (var i = 0;
                 i < _chart.Notes.Count && _selected.Count < count;
                 i++)
            {
                var note = _chart.Notes[i];
                if (note != null &&
                    MatchesSelectionMask(note.Kind, _selectionMask))
                {
                    _selected.Add(note);
                }
            }
            RefreshToolNotePickSelection();
            return _selected.Count;
        }

        public void DebugConfirmToolNotePick()
        {
            if (_view != null && _view.ToolDialog != null)
            {
                _view.ToolDialog.DebugConfirmNotePick();
            }
        }

        public void DebugCancelToolNotePick()
        {
            if (_view != null && _view.ToolDialog != null)
            {
                _view.ToolDialog.DebugCancelNotePick();
            }
        }

        public void DebugBeginToolTimePick()
        {
            if (_view != null && _view.ToolDialog != null)
            {
                _view.ToolDialog.DebugBeginFirstTimePick();
            }
        }

        public void DebugCancelToolTimePick()
        {
            if (_view != null && _view.ToolDialog != null)
            {
                _view.ToolDialog.DebugCancelTimePick();
            }
        }

        public void DebugSubmitActiveTool()
        {
            if (_view != null && _view.ToolDialog != null)
            {
                _view.ToolDialog.DebugSubmitCurrentForm();
            }
        }

        public int DebugUndoCount
        {
            get { return _session.UndoCount; }
        }

        public bool DebugToolTimePickActive
        {
            get { return _toolTimePickActive; }
        }

        public double DebugToolTimePickCandidateMs
        {
            get { return _toolTimePickCandidateMs; }
        }

        public bool DebugToolNotePickActive
        {
            get { return _toolNotePickActive; }
        }

        public int DebugToolNotePickCount
        {
            get { return _toolNotePickActive ? _selected.Count : 0; }
        }

        public int DebugToolNotePickMax
        {
            get
            {
                return _toolNotePickRequest != null
                    ? _toolNotePickRequest.MaxCount
                    : 0;
            }
        }

        public string DebugToolNoteFieldSummary
        {
            get
            {
                return _view != null &&
                       _view.ToolDialog != null
                    ? _view.ToolDialog.DebugFirstNoteFieldSummary
                    : string.Empty;
            }
        }

        public bool DebugToolTimePickIsSnapped
        {
            get
            {
                if (!_toolTimePickActive)
                {
                    return false;
                }
                return Math.Abs(
                           _toolTimePickCandidateMs -
                           _grid.Snap(
                               _chart,
                               _toolTimePickCandidateMs)) <
                       0.01;
            }
        }

        public void DebugScrollToolTimePick(double direction)
        {
            if (_toolTimePickActive)
            {
                ScrollTimeline(direction);
            }
        }

        public string DebugToolDialogSummary
        {
            get
            {
                if (_view == null || _view.ToolDialog == null)
                {
                    return "none";
                }
                var dialog = _view.ToolDialog;
                return "open=" + dialog.IsOpen +
                       " picking=" + dialog.IsTimePicking +
                       " notePicking=" + dialog.IsNotePicking +
                       " noteCount=" + _selected.Count +
                       " noteConfirm=" +
                       dialog.DebugNotePickConfirmEnabled +
                       " tools=" + dialog.DebugToolCount +
                       " fields=" + dialog.DebugFieldCount +
                       " status=" + dialog.DebugStatus;
            }
        }

        public string DebugToolReportSummary
        {
            get
            {
                return _view != null
                    ? _view.DebugToolReportSummary
                    : "none";
            }
        }

        public void DebugOpenDesignRuleTool()
        {
            OpenToolInternal(
                "com.falsum.tools.design-rule-checker",
                false);
        }

        public bool DebugRequestFirstToolReportLocate()
        {
            return _view != null &&
                   _view.DebugRequestFirstToolReportLocate();
        }

        public bool DebugRequestLastToolReportLocate()
        {
            return _view != null &&
                   _view.DebugRequestLastToolReportLocate();
        }

        public bool DebugRequestToolReportLocateByTitle(
            string title)
        {
            return _view != null &&
                   _view.DebugRequestToolReportLocateByTitle(
                       title);
        }

        public void DebugScrollToolReportToBottom()
        {
            if (_view != null)
            {
                _view.DebugScrollToolReportToBottom();
            }
        }

        public void DebugAddDesignRuleConflictFixture()
        {
            var start = Math.Max(
                1000.0,
                _playheadMs + 1000.0);
            _chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = start,
                DurationMs = 1200.0,
                StartLane = 1,
                WidthLanes = 2,
            });
            _chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.Tap,
                TimeMs = start + 600.0,
                StartLane = 2,
                WidthLanes = 1,
            });
            var skyStart = start + 3000.0;
            _chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = skyStart,
                DurationMs = 1000.0,
                StartPos = 0.5,
                StartWidth = 0.6,
                EndPos = 0.5,
                EndWidth = 0.6,
                StartCellCount = 24,
                EndCellCount = 24,
            });
            _chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = skyStart,
                DurationMs = 1000.0,
                StartPos = 0.5,
                StartWidth = 0.4,
                EndPos = 0.5,
                EndWidth = 0.4,
                StartCellCount = 24,
                EndCellCount = 24,
            });
            _chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = skyStart,
                DurationMs = 1000.0,
                StartPos = 0.5,
                StartWidth = 0.2,
                EndPos = 0.95,
                EndWidth = 0.1,
                StartCellCount = 24,
                EndCellCount = 24,
            });
            _selected.Clear();
            _playheadMs = Math.Max(0.0, start - 800.0);
            RefreshEditorVisuals();
        }

        public bool DirectDragUnselectedNotes
        {
            get { return _directDragUnselectedNotes; }
            set { _directDragUnselectedNotes = value; }
        }

        public bool DebugTwoDViewActive
        {
            get
            {
                return _viewMode ==
                       FalsumViewMode.Orthographic2D;
            }
        }

        public FalsumChartLayer DebugLayerVisibility
        {
            get { return _layerVisibility; }
        }

        public void DebugSetTwoDView(bool active)
        {
            SetViewMode(active
                ? FalsumViewMode.Orthographic2D
                : FalsumViewMode.Perspective3D);
        }

        public void DebugSetLayerVisibility(
            FalsumChartLayer visibility)
        {
            SetChartLayerVisibility(visibility);
        }

        public string DebugRun2DViewChecks()
        {
            var issues = new List<string>();
            var originalMode = _viewMode;
            var originalLayer = _layerVisibility;
            try
            {
                SetViewMode(FalsumViewMode.Orthographic2D);
                if (_viewMode !=
                    FalsumViewMode.Orthographic2D)
                {
                    issues.Add("enter_2d");
                }
                if (Camera.main == null ||
                    !Camera.main.orthographic)
                {
                    issues.Add("camera_projection");
                }

                var viewport = _view.GetChartViewportScreenRect();
                var screen = new Vector2(
                    viewport.center.x,
                    viewport.center.y);
                double rawTime;
                float lanePosition;
                int lane;
                if (!TryPointerTrack(
                        screen,
                        _playheadMs,
                        out rawTime,
                        out lanePosition,
                        out lane))
                {
                    issues.Add("pointer_projection");
                }
                float normalizedX;
                if (!TryGetPointerNormalizedOnTimePlane(
                        screen,
                        _playheadMs,
                        _playheadMs,
                        out normalizedX) ||
                    normalizedX < 0.0f ||
                    normalizedX > 1.0f)
                {
                    issues.Add("time_plane_projection");
                }
                var normalDuration =
                    _world2D.VisibleDurationMs;
                _world2D.SetFlowSpeed(10.0);
                if (_world2D.VisibleDurationMs >=
                    normalDuration * 0.75)
                {
                    issues.Add("flow_speed_range");
                }
                _world2D.SetFlowSpeed(_flowSpeed);

                SetChartLayerVisibility(FalsumChartLayer.Ground);
                if (_layerVisibility != FalsumChartLayer.Ground ||
                    !_host.NoteVisuals.EditorShowGroundNotes ||
                    _host.NoteVisuals.EditorShowSkyNotes)
                {
                    issues.Add("ground_layer");
                }
                SetChartLayerVisibility(FalsumChartLayer.Sky);
                if (_layerVisibility != FalsumChartLayer.Sky ||
                    _host.NoteVisuals.EditorShowGroundNotes ||
                    !_host.NoteVisuals.EditorShowSkyNotes)
                {
                    issues.Add("sky_layer");
                }
                SetChartLayerVisibility(FalsumChartLayer.None);
                if (_layerVisibility != FalsumChartLayer.Sky)
                {
                    issues.Add("empty_layer_guard");
                }
            }
            catch (Exception exception)
            {
                issues.Add(exception.GetType().Name + ":" +
                           exception.Message);
            }
            finally
            {
                SetChartLayerVisibility(originalLayer);
                SetViewMode(originalMode);
            }
            return issues.Count == 0
                ? "pass"
                : "fail " + string.Join(" | ", issues.ToArray());
        }

        private void ToggleTwoDView()
        {
            SetViewMode(
                _viewMode == FalsumViewMode.Orthographic2D
                    ? FalsumViewMode.Perspective3D
                    : FalsumViewMode.Orthographic2D);
        }

        private void SetViewMode(FalsumViewMode mode)
        {
            if (_viewMode == mode)
            {
                return;
            }
            CancelMiddleEdit();
            if (mode == FalsumViewMode.Orthographic2D)
            {
                _world2D.SetViewport(
                    _view.GetChartViewportScreenRect());
                if (!_world2D.SetVisible(true))
                {
                    ShowHint(
                        L.Get(
                            "falsum.hint.twoDViewCameraUnavailable"),
                        3.0f);
                    return;
                }
                _world.ClearGhostPreview();
                _world.SetSelectionOverlayVisible(false);
            }
            else
            {
                _world2D.SetVisible(false);
                _world.SetSelectionOverlayVisible(true);
            }
            _viewMode = mode;
            ResetPickCycle();
            _view.SetTwoDViewActive(
                mode == FalsumViewMode.Orthographic2D);
            _view.SetChartLayerVisibility(_layerVisibility);
            RefreshEditorAtChartTime(_playheadMs);
            UpdateViews();
        }

        private void SetChartLayerVisibility(
            FalsumChartLayer visibility)
        {
            visibility &= FalsumChartLayer.Both;
            if (visibility == FalsumChartLayer.None ||
                visibility == _layerVisibility)
            {
                return;
            }
            CancelMiddleEdit();
            _layerVisibility = visibility;
            _world2D.SetLayerMask(visibility);
            if (_world != null)
            {
                _world.SetLayerVisibility(visibility);
            }
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.SetEditorVisibleLayers(
                    (visibility & FalsumChartLayer.Ground) != 0,
                    (visibility & FalsumChartLayer.Sky) != 0);
            }
            RemoveHiddenSelection();
            _view.SetChartLayerVisibility(visibility);
            RefreshEditorAtChartTime(_playheadMs);
            RefreshPropertyPanel();
            UpdateViews();
        }

        private void RemoveHiddenSelection()
        {
            for (var i = _selected.Count - 1; i >= 0; i--)
            {
                var note = _selected[i];
                if (note == null ||
                    !IsNoteVisibleInLayer(note))
                {
                    _selected.RemoveAt(i);
                }
            }
        }

        private bool IsNoteVisibleInLayer(ChartNote note)
        {
            if (note.Kind == NoteKind.Flick ||
                note.Kind == NoteKind.SkyArea)
            {
                return (_layerVisibility & FalsumChartLayer.Sky) != 0;
            }
            return (_layerVisibility & FalsumChartLayer.Ground) != 0;
        }

        public string DebugMode
        {
            get { return _mode.ToString(); }
        }

        public void DebugSetTime(double timeMs)
        {
            Seek(timeMs);
        }

        public void DebugSetFlowSpeed(float speed)
        {
            SetFlowSpeed(speed);
        }

        public float DebugFlowSpeed
        {
            get { return _flowSpeed; }
        }

        public void DebugPausePlayback()
        {
            if (_host != null &&
                _host.Clock != null &&
                _host.Clock.IsPlaying)
            {
                PausePlaybackAt(_host.Clock.TimeMs);
            }
        }

        public void DebugRefreshOverlays()
        {
            UpdateOverlays();
        }

        public string DebugRunModePlaneOverlayChecks()
        {
            if (_modePlane == null)
            {
                return "fail no-plane";
            }
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                return "fail 2d-mode";
            }
            if (_world == null)
            {
                return "fail no-world";
            }
            var originalFocus = _modePlaneFocusLine;
            var originalDivisions = _modePlaneDivisions;
            _modePlane.SetState(
                FalsumModePlaneKind.Flick,
                "FLICK",
                _modePlaneDivisions > 0 ? _modePlaneDivisions : 24,
                _modePlaneFocusLine,
                null,
                null);
            _modePlane.SetWorldPose(
                _world.ModePlaneLocalPosition(
                    _playheadMs,
                    _playheadMs),
                _world.ModePlaneWorldHeight());
            var result = _modePlane.DebugOverlayAlignment;
            _modePlaneFocusLine = originalFocus;
            _modePlaneDivisions = originalDivisions;
            _modePlane.Hide();
            UpdateOverlays();
            return "pass " + result;
        }

        public string DebugMeasureNumberSummary
        {
            get
            {
                return _world != null
                    ? _world.DebugMeasureNumberSummary
                    : "none";
            }
        }

        public string DebugRunMeasureNumberChecks()
        {
            if (!FalsumMeasureNumberFeature.Available)
            {
                // Hidden feature: the only acceptable state is "never drawn",
                // even if a profile still carries ShowMeasureNumbers = true.
                var hiddenVisible = _world != null
                    ? _world.VisibleMeasureNumberCount
                    : 0;
                return hiddenVisible == 0
                    ? "pass hidden=1 visible=0"
                    : "fail hidden=1 visible=" + hiddenVisible;
            }
            var originalSpeed = _flowSpeed;
            var originalTime = _playheadMs;
            var originalEnabled = _showMeasureNumbers;
            var issues = new List<string>();
            var baseline = 0;
            var toggledOff = -1;
            var toggledOn = -1;
            try
            {
                SetFlowSpeed(1.0f);
                Seek(8000.0);
                UpdateOverlays();
                baseline = _world != null
                    ? _world.VisibleMeasureNumberCount
                    : 0;
                if (baseline <= 0)
                {
                    issues.Add("no_badges");
                }
                SetMeasureNumberVisibility(false);
                UpdateOverlays();
                toggledOff = _world != null
                    ? _world.VisibleMeasureNumberCount
                    : 0;
                if (toggledOff != 0)
                {
                    issues.Add("toggle_off");
                }
                SetMeasureNumberVisibility(true);
                UpdateOverlays();
                toggledOn = _world != null
                    ? _world.VisibleMeasureNumberCount
                    : 0;
                if (toggledOn != baseline)
                {
                    issues.Add("toggle_on");
                }
            }
            finally
            {
                _showMeasureNumbers = originalEnabled;
                SetFlowSpeed(originalSpeed);
                Seek(originalTime);
                UpdateOverlays();
                // Seek() starts playback; the capture flow asserts that the
                // editor is paused, so stop the clock again.
                if (_host != null &&
                    _host.Clock != null &&
                    _host.Clock.IsPlaying)
                {
                    PausePlaybackAt(_host.Clock.TimeMs);
                }
            }
            return issues.Count == 0
                ? "pass baseline=" + baseline +
                  " off=" + toggledOff +
                  " restored=" + toggledOn
                : "fail " + string.Join(",", issues.ToArray()) +
                  " baseline=" + baseline +
                  " off=" + toggledOff +
                  " on=" + toggledOn;
        }

        public void DebugSeekNormalized(float normalized)
        {
            SeekNormalized(normalized);
        }

        public void DebugLoadComboCapturePackage()
        {
            var path = Path.Combine(
                Application.streamingAssetsPath,
                "Falsum",
                "cryogenic.falsum");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.comboCapturePackageMissing"),
                    path);
            }
            LoadPackage(path, false);
            Seek(0.0);
        }

        public string DebugRunHistoryPerformanceCheck()
        {
            if (!_initialized || _chart.Notes.Count == 0)
            {
                return "skip";
            }

            PauseForEditInteraction();
            var originalTimeMs = _chart.Notes[0].TimeMs;
            _session.BeginChange(
                L.Get("falsum.history.historyPerformanceCheck"));
            _chart.Notes[0].TimeMs = originalTimeMs + 1.0;
            if (!_session.CommitChange())
            {
                _session.CancelChange();
                return "fail commit";
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Undo();
            watch.Stop();
            var undoMs = watch.Elapsed.TotalMilliseconds;
            var undoRestored = _chart.Notes.Count > 0 &&
                               Math.Abs(
                                   _chart.Notes[0].TimeMs -
                                   originalTimeMs) < 0.0001;

            watch.Restart();
            Redo();
            watch.Stop();
            var redoMs = watch.Elapsed.TotalMilliseconds;
            var redoRestored = _chart.Notes.Count > 0 &&
                               Math.Abs(
                                   _chart.Notes[0].TimeMs -
                                   (originalTimeMs + 1.0)) < 0.0001;

            Undo();
            _session.ClearHistory();
            UpdateViews();
            var result =
                "undoMs=" + undoMs.ToString("0.0", Invariant) +
                " redoMs=" + redoMs.ToString("0.0", Invariant) +
                " notes=" + _chart.Notes.Count +
                " visuals=" + (_host.NoteVisuals != null
                    ? _host.NoteVisuals.VisualCount
                    : 0) +
                " reused=" + (_host.NoteVisuals != null
                    ? _host.NoteVisuals.LastSyncReusedCount
                    : 0) +
                " created=" + (_host.NoteVisuals != null
                    ? _host.NoteVisuals.LastSyncCreatedCount
                    : 0);
            return undoRestored &&
                   redoRestored &&
                   undoMs <= 1000.0 &&
                   redoMs <= 1000.0
                ? "pass " + result
                : "fail " + result +
                  " undoRestored=" + undoRestored +
                  " redoRestored=" + redoRestored;
        }

        public string DebugRunRewindStateCheck()
        {
            ChartNote flick = null;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                var note = _chart.Notes[i];
                if (note.Kind == NoteKind.Flick &&
                    note.TimeMs >= 500.0)
                {
                    flick = note;
                    break;
                }
            }
            if (flick == null || _host.NoteVisuals == null)
            {
                return "skip";
            }

            _playheadMs = flick.TimeMs;
            flick.Consumed = true;
            flick.Judged = true;
            flick.Holding = false;
            flick.Result = JudgementResult.Shiny;
            RefreshEditorAtChartTime(_playheadMs);
            var hiddenBefore = !_host.NoteVisuals.IsNoteVisualActive(flick);

            Seek(flick.TimeMs - 300.0);
            var stateReset = !flick.Consumed && !flick.Judged;
            var visibleAfter = _host.NoteVisuals.IsNoteVisualActive(flick);
            return hiddenBefore && stateReset && visibleAfter
                ? "pass"
                : "fail hiddenBefore=" + hiddenBefore +
                  " stateReset=" + stateReset +
                  " visibleAfter=" + visibleAfter;
        }

        public bool DebugSelectFirstVisibleNote()
        {
            ClearSelection();
            ChartNote candidate = null;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                var note = _chart.Notes[i];
                if (note.TimeMs < _playheadMs)
                {
                    continue;
                }
                var distance = note.TimeMs - _playheadMs;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    candidate = note;
                }
            }
            if (candidate != null)
            {
                _playheadMs = Math.Max(0.0, candidate.TimeMs - 250.0);
                RefreshEditorAtChartTime(_playheadMs);
                _selected.Add(candidate);
                RefreshPropertyPanel();
                UpdateOverlays();
                return true;
            }
            return false;
        }

        public void DebugTogglePlayback()
        {
            TogglePlayback();
        }

        public void DebugShowRadial()
        {
            ClearSelection();
            ShowRadial(new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f));
        }

        public void DebugHideRadial()
        {
            _radial.Hide();
        }

        public void DebugSetPlaybackRate(double rate)
        {
            _host.SetPlaybackRate(rate);
            _view.SetPlaybackRate(rate);
        }

        public void DebugBeginTemporaryPlayback()
        {
            BeginTemporaryPlayback();
        }

        public void DebugEndTemporaryPlayback()
        {
            EndTemporaryPlayback();
        }

        public double DebugCurrentTime
        {
            get { return CurrentPlaybackTime(); }
        }

        public double DebugTimelineDuration
        {
            get { return TimelineDuration(); }
        }

        public FalsumNotePickMask SelectionMask
        {
            get { return _selectionMask; }
            set
            {
                _selectionMask = value;
                ApplyEditorSelectionDimming();
            }
        }

        public bool DebugSongAudioPlaying
        {
            get
            {
                return _host.SongAudio != null &&
                       _host.SongAudio.isPlaying;
            }
        }

        public double DebugSongAudioDurationMs
        {
            get
            {
                return _host != null &&
                       _host.SongAudio != null &&
                       _host.SongAudio.clip != null
                    ? _host.SongAudio.clip.length * 1000.0
                    : 0.0;
            }
        }

        public bool DebugPlaybackPlaying
        {
            get
            {
                return _host != null &&
                       _host.Clock != null &&
                       _host.Clock.IsPlaying;
            }
        }

        public void DebugScrollTimeline(double direction)
        {
            ScrollTimeline(direction);
        }

        public string DebugRunWheelSnapCheck()
        {
            if (!_grid.Enabled)
            {
                return "skip grid-disabled";
            }

            var original = _playheadMs;
            var step = _grid.StepMs(_chart, 0.0);
            Seek(step * 0.45);
            ScrollTimeline(1.0);
            var forwardPass = Math.Abs(_playheadMs - step) < 0.01;
            Seek(step * 0.45);
            ScrollTimeline(-1.0);
            var backwardPass = Math.Abs(_playheadMs) < 0.01;
            Seek(original);
            return forwardPass && backwardPass
                ? "pass forward=" + step.ToString("0.###") +
                  " backward=0"
                : "fail forward=" + forwardPass +
                  " backward=" + backwardPass;
        }

        public string DebugRunPlaybackContinuityCheck()
        {
            if (!_initialized || _host.Clock == null)
            {
                return "skip not-initialized";
            }
            if (DebugPlaybackPlaying)
            {
                return "skip already-playing";
            }

            var originalTime = _playheadMs;
            var eventsWereOpen = _view.IsEventsPanelOpen;
            try
            {
                Seek(Math.Min(1000.0, TimelineDuration()));
                TogglePlayback();
                var started = DebugPlaybackPlaying;

                ScrollTimeline(1.0);
                var wheelContinued = DebugPlaybackPlaying;

                var seekTarget = Math.Min(
                    Math.Max(2000.0, _playheadMs + 1000.0),
                    TimelineDuration());
                Seek(seekTarget);
                var seekContinued =
                    DebugPlaybackPlaying &&
                    Math.Abs(_playheadMs - seekTarget) <= 1.0;

                _view.SetEventsPanelOpen(true);
                var eventsContinued = DebugPlaybackPlaying;
                if (!eventsWereOpen)
                {
                    _view.SetEventsPanelOpen(false);
                }
                return started &&
                       wheelContinued &&
                       seekContinued &&
                       eventsContinued
                    ? "pass wheel=true seek=true events=true"
                    : "fail started=" + started +
                      " wheel=" + wheelContinued +
                      " seek=" + seekContinued +
                      " events=" + eventsContinued;
            }
            finally
            {
                if (DebugPlaybackPlaying)
                {
                    TogglePlayback();
                }
                if (!eventsWereOpen && _view.IsEventsPanelOpen)
                {
                    _view.SetEventsPanelOpen(false);
                }
                Seek(originalTime);
            }
        }

        public string DebugRunImeFocusChecks()
        {
            return _view != null
                ? _view.DebugRunImeFocusChecks()
                : "fail missing-view";
        }

        public string DebugLoadSelectionTest(NoteKind kind)
        {
            PauseForEditInteraction();
            var chart = new Chart
            {
                Bpm = 120.0,
                Division = 4.0,
                SongId = "selection-test",
                Difficulty = _currentDifficulty,
            };
            var note = new ChartNote
            {
                Kind = kind,
                TimeMs = 1000.0,
                DurationMs = kind == NoteKind.Tap ? 0.0 : 1200.0,
                StartLane = kind == NoteKind.Hold ? 0 : 2,
                WidthLanes = 1,
                StartPos = 0.5,
                EndPos = 0.5,
                StartWidth = 2.0 / 24.0,
                EndWidth = 2.0 / 24.0,
                StartCellCount = 24,
                EndCellCount = 24,
                FlickDirection = 4,
                GroupId = 1,
                LeftEasing = kind == NoteKind.SkyArea ? 1 : 0,
                RightEasing = kind == NoteKind.SkyArea ? 2 : 0,
            };
            chart.Notes.Add(note);
            chart.RebuildTimeline();
            _session.Replace(chart, 0.0);
            _chart = _session.Chart;
            note = _chart.Notes[0];
            _selected.Clear();
            _playheadMs = 900.0;
            SetMode(EditorMode.Select);
            RefreshEditorVisuals();

            Vector2 screen;
            var hasCenter = _world.TryGetNoteScreenCenter(
                note,
                _playheadMs,
                out screen);
            ChartNote clicked;
            var hit = hasCenter &&
                      TryPickNote(screen, out clicked) &&
                      ReferenceEquals(clicked, note);
            ChartNote farClicked;
            var farHit = hasCenter &&
                         TryPickNote(
                             screen + new Vector2(
                                 Screen.width * 0.15f,
                                 0.0f),
                             out farClicked);
            var selectionBefore = string.Empty;
            var selectionAfter = string.Empty;
            if (hit)
            {
                _selected.Add(note);
                UpdateOverlays();
                selectionBefore = _world.SelectionDebugSummary;
                note.TimeMs += 120.0;
                RebuildEditorTimeline();
                RefreshEditorAtChartTime(_playheadMs);
                UpdateOverlays();
                selectionAfter = _world.SelectionDebugSummary;
            }
            RefreshPropertyPanel();
            UpdateOverlays();
            return "kind=" + kind +
                   " center=" + screen.ToString("F1") +
                   " centerFound=" + hasCenter +
                   " click=" + (hit ? "hit" : "miss") +
                   " farClick=" + (farHit ? "hit" : "miss") +
                   " selectionPolys=" + _world.SelectionPolygonCount +
                   " selectionVertices=" + _world.SelectionVertexCount +
                   " selection=" + _world.SelectionDebugSummary +
                   " follow=" +
                   (!string.IsNullOrEmpty(selectionBefore) &&
                    !string.Equals(
                        selectionBefore,
                        selectionAfter,
                        StringComparison.Ordinal)) +
                   " screen=" + Screen.width + "x" + Screen.height;
        }

        public string DebugLoadSkyAreaSeamTest()
        {
            PauseForEditInteraction();
            var chart = new Chart
            {
                Bpm = 120.0,
                Division = 4.0,
                SongId = "skyarea-seam-test",
                Difficulty = _currentDifficulty,
            };
            chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 1000.0,
                DurationMs = 500.0,
                StartLane = 2,
                WidthLanes = 1,
                StartPos = 12.0 / 24.0,
                EndPos = 8.0 / 24.0,
                StartWidth = 8.0 / 24.0,
                EndWidth = 4.0 / 24.0,
                StartCellCount = 24,
                EndCellCount = 24,
                LeftEasing = 0,
                RightEasing = 0,
                GroupId = 100,
            });
            chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 1500.0,
                DurationMs = 500.0,
                StartLane = 2,
                WidthLanes = 1,
                StartPos = 8.0 / 24.0,
                EndPos = 16.0 / 24.0,
                StartWidth = 16.0 / 24.0,
                EndWidth = 10.0 / 24.0,
                StartCellCount = 24,
                EndCellCount = 24,
                LeftEasing = 1,
                RightEasing = 2,
                GroupId = 100,
            });
            chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = 2000.0,
                DurationMs = 500.0,
                StartLane = 2,
                WidthLanes = 1,
                StartPos = 16.0 / 24.0,
                EndPos = 12.0 / 24.0,
                StartWidth = 10.0 / 24.0,
                EndWidth = 24.0 / 24.0,
                StartCellCount = 24,
                EndCellCount = 24,
                LeftEasing = 2,
                RightEasing = 1,
                GroupId = 100,
            });
            chart.RebuildTimeline();
            _session.Replace(chart, 0.0);
            _chart = _session.Chart;
            _selected.Clear();
            _playheadMs = 1510.0;
            _grid.Enabled = false;
            SetMode(EditorMode.Select);
            RefreshEditorVisuals();
            RefreshPropertyPanel();
            UpdateOverlays();
            return "kind=SkyArea group=100 segments=3 time=1510" +
                   " noteRendererGroups=" + _host.SkyAreaGroupCount;
        }

        public string DebugRunSkyAreaSelectionCheck()
        {
            DebugLoadSelectionTest(NoteKind.SkyArea);
            return _world.SelectionPolygonCount > 0 &&
                   _world.SelectionVertexCount > 4
                ? "pass"
                : "fail polygons=" + _world.SelectionPolygonCount +
                  " vertices=" + _world.SelectionVertexCount;
        }

        public string DebugRunPointPlaceCheck()
        {
            ClearSelection();
            _mode = EditorMode.Select;
            _radial.Hide();
            HandleRadialAction(FalsumRadialAction.PointPlace);
            var idleMode = _mode == EditorMode.PointPlace &&
                           !_radial.IsOpen;
            ShowRadial(new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f));
            var placementMenu = _radial.IsOpen &&
                                _radial.HasEntry(
                                    FalsumRadialAction.PointPlaceNone) &&
                                _radial.HasEntry(
                                    FalsumRadialAction.PlaceTap) &&
                                _radial.HasEntry(
                                    FalsumRadialAction.GridToggle) &&
                                !_radial.HasEntry(
                                    FalsumRadialAction.SegmentSelect);
            var gridLabelMatches =
                _radial.DebugEntryLabel(
                    FalsumRadialAction.GridToggle) ==
                L.Get(
                    _grid.Enabled
                        ? "falsum.radial.gridOn"
                        : "falsum.radial.gridOff");
            _radial.Hide();
            SetMode(EditorMode.PointPlace);
            var playbackHint = L.Get(
                "falsum.hint.pointPlace.playbackTest");
            _persistentHint = playbackHint;
            PrepareForPlayback();
            var playbackModePreserved =
                _mode == EditorMode.PointPlace;
            var playbackHintPreserved =
                _persistentHint == playbackHint;
            HandleRadialAction(FalsumRadialAction.PointPlaceNone);
            var noneMode = _mode == EditorMode.PointPlace &&
                           !_radial.IsOpen;
            return idleMode &&
                   placementMenu &&
                   gridLabelMatches &&
                   playbackModePreserved &&
                   playbackHintPreserved &&
                   noneMode
                ? "pass"
                : "fail idle=" + idleMode +
                  " placementMenu=" + placementMenu +
                  " gridLabel=" + gridLabelMatches +
                  " playbackMode=" + playbackModePreserved +
                  " playbackHint=" + playbackHintPreserved +
                  " noneMode=" + noneMode;
        }

        public string DebugShowModeTest(string mode)
        {
            PauseForEditInteraction();
            var chart = new Chart
            {
                Bpm = 120.0,
                Division = 4.0,
                SongId = "mode-plane-test",
                Difficulty = _currentDifficulty,
            };
            chart.RebuildTimeline();
            _session.Replace(chart, 0.0);
            _chart = _session.Chart;
            _selected.Clear();
            RefreshPropertyPanel();
            _debugPointerOverride = true;
            _hasPointer = true;
            _playheadMs = 9300.0;
            _pointerTime = 10000.0;
            _pointerRawTime = _pointerTime;
            _pointerLane = 2;
            _pointerLanePosition = 0.5f;

            switch ((mode ?? string.Empty).ToLowerInvariant())
            {
                case "segment":
                    SetMode(EditorMode.SegmentSelect);
                    _segmentHasStart = false;
                    _persistentHint = L.Get(
                        "falsum.hint.segment.selectStart");
                    break;
                case "flick":
                    SetMode(EditorMode.PlaceFlick);
                    _flickDenominator = FixtureDenominator();
                    _flickHasTime = true;
                    _flickTime = 9700.0;
                    _flickDragging = true;
                    _flickStart = 0.25;
                    _flickCurrent = 0.5;
                    _persistentHint = L.Get(
                        "falsum.hint.flick.dragFromStartToEnd");
                    break;
                case "skyarea":
                    SetMode(EditorMode.PlaceSkyArea);
                    _flickDenominator = FixtureDenominator();
                    _skyStage = 1;
                    _skyStartTime = 9550.0;
                    _skyStartAnchor = 0.25;
                    _skyStartPos = 0.25;
                    _skyStartWidth = 4.0 / _flickDenominator;
                    _skyStartFirstEdge = false;
                    _pointerTime = 10550.0;
                    _pointerRawTime = _pointerTime;
                    _pointerLanePosition = 0.72f;
                    _persistentHint = L.Get(
                        "falsum.hint.skyArea.chooseEndPoint");
                    break;
                default:
                    SetMode(EditorMode.Select);
                    _debugPointerOverride = false;
                    break;
            }

            UpdateModelsForModeTest();
            UpdateViews();
            UpdateOverlays();
            return "mode=" + mode +
                   " plane=" +
                   (_modePlane != null
                       ? _modePlane.DebugSummary
                       : "none") +
                   " ghostEndPoint=" + _world.GhostEndsAsPoint +
                   " ghostTriangles=" + _world.GhostTriangleCount +
                   " ghostScreen=" + _world.GhostScreenDebug +
                   " planeVertices=" + _world.TimePlaneVertexCount;
        }

        public void DebugClearPointerOverride()
        {
            _debugPointerOverride = false;
        }

        private void UpdateModelsForModeTest()
        {
            RefreshEditorAtChartTime(_playheadMs);
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.RefreshNoteVisuals(null);
            }
        }

        public string DebugProbePointerTimes()
        {
            var values = new List<string>();
            var probes = new[] { 0.32f, 0.52f, 0.72f };
            for (var i = 0; i < probes.Length; i++)
            {
                double time;
                float position;
                int lane;
                var screen = new Vector2(
                    Screen.width * 0.5f,
                    Screen.height * probes[i]);
                var hit = _world.TryPointerTrack(
                    screen,
                    _playheadMs,
                    out time,
                    out position,
                    out lane);
                values.Add(
                    hit
                        ? time.ToString("0", Invariant)
                        : "miss");
            }
            var master = _host.Binding.MasterScene;
            var line = _host.Binding.JudgementLine;
            var lineLocal = master.InverseTransformPoint(line.position);
            var floorLocal = master.InverseTransformPoint(
                _host.Binding.NoteTemplates[2].transform.position);
            return string.Join(",", values.ToArray()) +
                   " masterPos=" + master.position.ToString("F2") +
                   " masterUp=" + master.up.ToString("F2") +
                   " lineWorld=" + line.position.ToString("F2") +
                   " lineLocal=" + lineLocal.ToString("F2") +
                   " floorLocalY=" + floorLocal.y.ToString("F2");
        }

        public string DebugRunPickChecks()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return "no-camera";
            }
            var visible = 0;
            var hits = 0;
            var lane0Hits = 0;
            var lane5Hits = 0;
            var flickHits = 0;
            var skyHits = 0;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                var note = _chart.Notes[i];
                Bounds bounds;
                if (_host.NoteVisuals == null ||
                    !_host.NoteVisuals.TryGetNoteWorldBounds(
                        note,
                        _playheadMs,
                        out bounds))
                {
                    continue;
                }
                var screen = camera.WorldToScreenPoint(bounds.center);
                if (screen.z <= 0.0f ||
                    screen.x < 0.0f ||
                    screen.x > Screen.width ||
                    screen.y < 0.0f ||
                    screen.y > Screen.height)
                {
                    continue;
                }
                visible++;
                ChartNote picked = null;
                if (!TryPickNote(screen, out picked))
                {
                    continue;
                }
                hits++;
                if (picked.StartLane == 0 &&
                    picked.Kind != NoteKind.SkyArea &&
                    picked.Kind != NoteKind.Flick)
                {
                    lane0Hits++;
                }
                if (picked.StartLane == 5 &&
                    picked.Kind != NoteKind.SkyArea &&
                    picked.Kind != NoteKind.Flick)
                {
                    lane5Hits++;
                }
                if (picked.Kind == NoteKind.Flick)
                {
                    flickHits++;
                }
                if (picked.Kind == NoteKind.SkyArea)
                {
                    skyHits++;
                }
            }
            return "visible=" + visible +
                   " hits=" + hits +
                   " lane0=" + lane0Hits +
                   " lane5=" + lane5Hits +
                   " flick=" + flickHits +
                   " sky=" + skyHits;
        }

        public string DebugRunSideLanePickCheck()
        {
            var result = string.Empty;
            for (var lane = 0; lane <= 5; lane += 5)
            {
                ChartNote candidate = null;
                for (var i = 0; i < _chart.Notes.Count; i++)
                {
                    var note = _chart.Notes[i];
                    if (note.StartLane == lane &&
                        (note.Kind == NoteKind.Tap ||
                         note.Kind == NoteKind.Hold))
                    {
                        candidate = note;
                        break;
                    }
                }
                if (candidate == null)
                {
                    result += " lane" + lane + "=missing";
                    continue;
                }
                _playheadMs = Math.Max(
                    0.0,
                    candidate.TimeMs - 250.0);
                RefreshEditorAtChartTime(_playheadMs);
                Bounds bounds;
                var camera = Camera.main;
                if (_host.NoteVisuals == null ||
                    camera == null ||
                    !_host.NoteVisuals.TryGetNoteWorldBounds(
                        candidate,
                        _playheadMs,
                        out bounds))
                {
                    result += " lane" + lane + "=bounds";
                    continue;
                }
                var screen = camera.WorldToScreenPoint(bounds.center);
                ChartNote picked;
                var anyHit = TryPickNote(screen, out picked);
                var hit = anyHit && picked == candidate;
                result += " lane" + lane +
                          (hit
                              ? "=hit"
                              : anyHit
                                  ? "=other:" + picked.Kind +
                                    "/" + picked.StartLane
                                  : "=miss");
            }
            _playheadMs = 8000.0;
            RefreshEditorAtChartTime(_playheadMs);
            return result.Trim();
        }

        public string DebugRunSelectionGeometryChecks()
        {
            var issues = new List<string>();
            var originalText = ChartWriter.Write(
                _session.Chart,
                _session.DelayMs,
                _manifest.SongId,
                _currentDifficulty);
            var original = ChartParser.Parse(
                originalText.Replace("\r\n", "\n").Split('\n'),
                "<falsum-selection-check>");
            var longHold = LoadSelectionGeometryTest(
                NoteKind.Hold,
                0,
                1,
                5000.0,
                2000.0);
            Vector2 longHoldCenter;
            ChartNote picked;
            if (!_world.TryGetNoteScreenCenter(
                    longHold,
                    _playheadMs,
                    out longHoldCenter) ||
                !_world.TryPick(
                    longHoldCenter,
                    _playheadMs,
                    _chart.Notes,
                    FalsumNotePickMask.All,
                    out picked) ||
                !ReferenceEquals(picked, longHold))
            {
                issues.Add("long-hold-tail");
            }
            Vector2[] longHoldPolygon;
            if (!_world.TryGetNoteScreenPolygon(
                    longHold,
                    _playheadMs,
                    out longHoldPolygon) ||
                longHoldPolygon == null ||
                longHoldPolygon.Length < 3)
            {
                issues.Add("long-hold-polygon");
            }
            else
            {
                var farIndex = 0;
                var center = Vector2.zero;
                for (var i = 0; i < longHoldPolygon.Length; i++)
                {
                    center += longHoldPolygon[i];
                    if (longHoldPolygon[i].y >
                        longHoldPolygon[farIndex].y)
                    {
                        farIndex = i;
                    }
                }
                center /= longHoldPolygon.Length;
                var tailPoint = Vector2.Lerp(
                    longHoldPolygon[farIndex],
                    center,
                    0.2f);
                Vector2 tailScreen;
                if (!_world.TryCanvasPointToScreen(
                        tailPoint,
                        out tailScreen) ||
                    !_world.TryPick(
                        tailScreen,
                        _playheadMs,
                        _chart.Notes,
                        FalsumNotePickMask.All,
                        out picked) ||
                    !ReferenceEquals(picked, longHold))
                {
                    issues.Add("long-hold-tail-end");
                }
            }

            var wideHold = LoadSelectionGeometryTest(
                NoteKind.Hold,
                2,
                1,
                800.0,
                900.0);
            Vector2[] widePolygon;
            if (!_world.TryGetNoteScreenPolygon(
                    wideHold,
                    _playheadMs,
                    out widePolygon) ||
                widePolygon == null ||
                widePolygon.Length < 3)
            {
                issues.Add("wide-hold-polygon");
            }
            else
            {
                var rightIndex = 0;
                for (var i = 1; i < widePolygon.Length; i++)
                {
                    if (widePolygon[i].x > widePolygon[rightIndex].x)
                    {
                        rightIndex = i;
                    }
                }
                var outside = widePolygon[rightIndex];
                outside.x += _world.PickPaddingCanvasUnits + 2.0f;
                Vector2 outsideScreen;
                if (!_world.TryCanvasPointToScreen(
                        outside,
                        out outsideScreen))
                {
                    issues.Add("wide-hold-screen");
                }
                else if (_world.TryPick(
                             outsideScreen,
                             _playheadMs,
                             _chart.Notes,
                             FalsumNotePickMask.All,
                             out picked))
                {
                    issues.Add("wide-hold-outside");
                }
            }

            var tap = LoadSelectionGeometryTest(
                NoteKind.Tap,
                2,
                2,
                0.0,
                900.0);
            _selected.Add(tap);
            UpdateOverlays();
            if (_world.SelectionPolygonCount != 1 ||
                _world.SelectionVertexCount < 3)
            {
                issues.Add("tap-selection-polygon");
            }
            var tapVertexCount = _world.SelectionVertexCount;

            var overlapFlick = DebugCheckOverlapPriority(
                NoteKind.Flick);
            if (!string.IsNullOrEmpty(overlapFlick))
            {
                issues.Add(overlapFlick);
            }
            var overlapSky = DebugCheckOverlapPriority(
                NoteKind.SkyArea);
            if (!string.IsNullOrEmpty(overlapSky))
            {
                issues.Add(overlapSky);
            }

            CheckSelectionHiddenAfterLeavingView(
                NoteKind.Tap,
                issues);
            CheckSelectionHiddenAfterLeavingView(
                NoteKind.Hold,
                issues);
            CheckSelectionHiddenAfterLeavingView(
                NoteKind.Flick,
                issues);

            var groundFilter = LoadSelectionGeometryTest(
                NoteKind.Tap,
                2,
                1,
                0.0,
                900.0);
            Vector2 filterScreen;
            ChartNote filterPicked;
            if (!_world.TryGetNoteScreenCenter(
                    groundFilter,
                    _playheadMs,
                    out filterScreen) ||
                !_world.TryPick(
                    filterScreen,
                    _playheadMs,
                    _chart.Notes,
                    FalsumNotePickMask.GroundNotes,
                    out filterPicked) ||
                !ReferenceEquals(filterPicked, groundFilter) ||
                _world.TryPick(
                    filterScreen,
                    _playheadMs,
                    _chart.Notes,
                    FalsumNotePickMask.SkyNotes,
                    out filterPicked))
            {
                issues.Add("filter-ground");
            }

            var skyFilter = LoadSelectionGeometryTest(
                NoteKind.Flick,
                2,
                1,
                0.0,
                900.0);
            if (!_world.TryGetNoteScreenCenter(
                    skyFilter,
                    _playheadMs,
                    out filterScreen) ||
                !_world.TryPick(
                    filterScreen,
                    _playheadMs,
                    _chart.Notes,
                    FalsumNotePickMask.SkyNotes,
                    out filterPicked) ||
                !ReferenceEquals(filterPicked, skyFilter) ||
                _world.TryPick(
                    filterScreen,
                    _playheadMs,
                    _chart.Notes,
                    FalsumNotePickMask.GroundNotes,
                    out filterPicked))
            {
                issues.Add("filter-sky");
            }

            _selectionMask = FalsumNotePickMask.All;
            if (!original.Success)
            {
                issues.Add("restore-parse");
            }
            else
            {
                _session.Replace(original.Chart, 0.0);
                _chart = _session.Chart;
                _selected.Clear();
                _playheadMs = 0.0;
                SetMode(EditorMode.Select);
                RefreshEditorVisuals();
            }

            return issues.Count == 0
                ? "pass pad=" +
                  _world.PickPaddingCanvasUnits.ToString("0.00") +
                  " tapVertices=" + tapVertexCount
                : "fail " + string.Join(",", issues.ToArray());
        }

        public string DebugRunUiHitChecks()
        {
            if (_view == null)
            {
                return "fail no-view";
            }
            var center = new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f);
            var top = new Vector2(
                Screen.width * 0.5f,
                Mathf.Max(0.0f, Screen.height - 20.0f));
            var bottom = new Vector2(
                Screen.width * 0.5f,
                20.0f);
            var centerOver = _view.IsPointerOverUi(center);
            var topOver = _view.IsPointerOverUi(top);
            var bottomOver = _view.IsPointerOverUi(bottom);
            var topPick = _view.DebugPickNamedAncestor(top);
            var bottomPick = _view.DebugPickNamedAncestor(bottom);
            var pathsCorrect =
                topPick.IndexOf("TopBar", StringComparison.Ordinal) >= 0 &&
                bottomPick.IndexOf(
                    "BottomBar",
                    StringComparison.Ordinal) >= 0;
            return topOver && bottomOver && !centerOver && pathsCorrect
                ? "pass top=true bottom=true center=false" +
                  " topPick=" + topPick +
                  " bottomPick=" + bottomPick
                : "fail top=" + topOver +
                  " bottom=" + bottomOver +
                  " center=" + centerOver +
                  " topPick=" + topPick +
                  " bottomPick=" + bottomPick;
        }

        public string DebugRunUiControlChecks()
        {
            if (_view == null)
            {
                return "fail no-view";
            }
            var focus = _view.DebugRunFocusChecks();
            var menu = _view.DebugRunMenuChecks();
            var editMenu = _view.DebugRunEditMenuChecks();
            var viewMenu = _view.DebugRunViewMenuChecks();
            var transportLayout =
                _view.DebugRunTransportLayoutChecks();
            return focus == "pass" &&
                   menu.StartsWith(
                    "pass",
                    StringComparison.Ordinal) &&
                   editMenu.StartsWith(
                       "pass",
                       StringComparison.Ordinal) &&
                   transportLayout.StartsWith(
                       "pass",
                       StringComparison.Ordinal) &&
                   viewMenu.StartsWith(
                       "pass",
                       StringComparison.Ordinal)
                ? "pass focus=true menu=" + menu +
                  " edit=" + editMenu +
                  " transport=" + transportLayout +
                  " view=" + viewMenu
                : "fail focus=" + focus +
                  " menu=" + menu +
                  " edit=" + editMenu +
                  " transport=" + transportLayout +
                  " view=" + viewMenu;
        }

        public string DebugRunSelectionFilterChecks()
        {
            if (_host == null || _host.NoteVisuals == null)
            {
                return "fail no-note-renderer";
            }
            _selected.Clear();
            _selected.Add(new ChartNote
            {
                Kind = NoteKind.Flick,
                TimeMs = _playheadMs,
                StartPos = 0.5,
                StartWidth = 0.1,
            });
            ApplySelectionFilter(true, false);
            if (_selectionMask !=
                    FalsumNotePickMask.GroundNotes ||
                _selected.Count != 0 ||
                !_host.NoteVisuals.EditorDimSkyNotes ||
                _host.NoteVisuals.EditorDimGroundNotes)
            {
                return "fail ground-only";
            }
            var segmentStart = _playheadMs - 10.0;
            var segmentEnd = _playheadMs + 10.0;
            if (!IsNoteInSegmentSelection(
                    new ChartNote
                    {
                        Kind = NoteKind.Tap,
                        TimeMs = _playheadMs,
                    },
                    segmentStart,
                    segmentEnd,
                    FalsumNotePickMask.GroundNotes) ||
                IsNoteInSegmentSelection(
                    new ChartNote
                    {
                        Kind = NoteKind.Flick,
                        TimeMs = _playheadMs,
                    },
                    segmentStart,
                    segmentEnd,
                    FalsumNotePickMask.GroundNotes))
            {
                return "fail ground-segment-filter";
            }

            _selected.Add(new ChartNote
            {
                Kind = NoteKind.Tap,
                TimeMs = _playheadMs,
                StartLane = 2,
                WidthLanes = 1,
            });
            ApplySelectionFilter(false, true);
            if (_selectionMask !=
                    FalsumNotePickMask.SkyNotes ||
                _selected.Count != 0 ||
                _host.NoteVisuals.EditorDimSkyNotes ||
                !_host.NoteVisuals.EditorDimGroundNotes)
            {
                return "fail sky-only";
            }
            if (!IsNoteInSegmentSelection(
                    new ChartNote
                    {
                        Kind = NoteKind.SkyArea,
                        TimeMs = _playheadMs,
                    },
                    segmentStart,
                    segmentEnd,
                    FalsumNotePickMask.SkyNotes) ||
                IsNoteInSegmentSelection(
                    new ChartNote
                    {
                        Kind = NoteKind.Hold,
                        TimeMs = _playheadMs,
                    },
                    segmentStart,
                    segmentEnd,
                    FalsumNotePickMask.SkyNotes))
            {
                return "fail sky-segment-filter";
            }

            ApplySelectionFilter(false, false);
            if (_selectionMask != FalsumNotePickMask.All ||
                _host.NoteVisuals.EditorDimSkyNotes ||
                _host.NoteVisuals.EditorDimGroundNotes)
            {
                return "fail clear-filter";
            }
            return "pass";
        }

        public string DebugRunLayoutChecks()
        {
            return _view != null
                ? _view.DebugRunLayoutChecks()
                : "fail no-view";
        }

        public string DebugRunPropertyFieldChecks()
        {
            return _view != null
                ? _view.DebugRunPropertyFieldChecks()
                : "fail no-view";
        }

        public void DebugShowFileMenu()
        {
            if (_view != null)
            {
                _view.DebugShowFileMenu();
            }
        }

        public void DebugHideFileMenu()
        {
            if (_view != null)
            {
                _view.DebugHideMainMenu();
            }
        }

        public void DebugOpenPreferences()
        {
            OpenPreferences();
        }

        public void DebugClickSongInfoCover()
        {
            if (_view != null)
            {
                _view.DebugClickSongInfoCover();
            }
        }

        public void DebugShowPreferencesTab(string tab)
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView.DebugShowTab(tab);
            }
        }

        public string DebugUiStyle
        {
            get
            {
                return _view != null
                    ? _view.DebugUiStyle
                    : FalsumUiStyle.Classic.ToString();
            }
        }

        public void DebugPreviewUiStyle(FalsumUiStyle style)
        {
            if (_view != null && _view.PreferencesView != null)
            {
                _view.PreferencesView.DebugSetUiStyle(style);
            }
        }

        public void DebugCommitPreferences()
        {
            if (_view != null && _view.PreferencesView != null)
            {
                _view.PreferencesView.DebugCommit();
            }
        }

        public bool DebugTapPreferencesAboutVersion()
        {
            return _view != null &&
                   _view.IsPreferencesOpen &&
                   _view.PreferencesView != null &&
                   _view.PreferencesView.DebugTapAboutVersion();
        }

        public int DebugPreferencesVersionTapCount
        {
            get
            {
                return _view != null &&
                       _view.IsPreferencesOpen &&
                       _view.PreferencesView != null
                    ? _view.PreferencesView
                        .DebugAboutVersionTapCount
                    : -1;
            }
        }

        public bool DebugPreferencesAboutReady
        {
            get
            {
                return _view != null &&
                       _view.IsPreferencesOpen &&
                       _view.PreferencesView != null &&
                       _view.PreferencesView.DebugAboutIsReady;
            }
        }

        public bool DebugTogglePerformanceWindow()
        {
            return _view != null &&
                   _view.DebugTogglePerformanceWindow();
        }

        public bool DebugPerformanceWindowOpen
        {
            get
            {
                return _view != null &&
                       _view.IsPerformanceWindowOpen;
            }
        }

        public int DebugPerformanceHistoryCount
        {
            get
            {
                return _performanceMonitor != null
                    ? _performanceMonitor.HistoryCount
                    : 0;
            }
        }

        public string DebugPerformanceSummary
        {
            get
            {
                if (_performanceMonitor == null)
                {
                    return "none";
                }
                return
                    "fps=" +
                    _performanceMonitor.Fps.ToString("0.0", Invariant) +
                    " frame=" +
                    _performanceMonitor.FrameMs.ToString(
                        "0.0",
                        Invariant) +
                    " cpuMain=" +
                    _performanceMonitor.CpuMainThreadMs.ToString(
                        "0.0",
                        Invariant) +
                    " cpuRender=" +
                    _performanceMonitor.CpuRenderThreadMs.ToString(
                        "0.0",
                        Invariant) +
                    " gpu=" +
                    _performanceMonitor.GpuMs.ToString(
                        "0.0",
                        Invariant) +
                    " gpuAvailable=" +
                    _performanceMonitor.GpuTimingAvailable +
                    " frameTiming=" +
                    _performanceMonitor.FrameTimingFeatureEnabled +
                    " gpuRecorder=" +
                    _performanceMonitor.GpuRecorderValid +
                    " overlay=" +
                    _performanceMonitor.OverlayMs.ToString(
                        "0.0",
                        Invariant) +
                    " history=" +
                    _performanceMonitor.HistoryCount +
                    " bottleneck=" +
                    _performanceMonitor.Bottleneck;
            }
        }

        public void DebugScrollPreferencesContentToBottom()
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView.DebugScrollContentToBottom();
            }
        }

        public void DebugScrollPreferencesContentToTop()
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView.DebugScrollContentToTop();
            }
        }

        public bool DebugDisclaimerOpen
        {
            get
            {
                return _view != null &&
                       _view.DebugDisclaimerOpen;
            }
        }

        public bool DebugDisclaimerMandatory
        {
            get
            {
                return _view != null &&
                       _view.DebugDisclaimerMandatory;
            }
        }

        public string DebugDisclaimerText
        {
            get
            {
                return _view != null
                    ? _view.DebugDisclaimerText
                    : string.Empty;
            }
        }

        public void DebugAcceptDisclaimer()
        {
            if (_view != null)
            {
                _view.DebugAcceptDisclaimer();
            }
        }

        public void DebugDeclineDisclaimer()
        {
            if (_view != null)
            {
                _view.DebugDeclineDisclaimer();
            }
        }

        public bool DebugCloseDisclaimer()
        {
            return _view != null &&
                   _view.DebugCloseDisclaimer();
        }

        public bool DebugOpenPreferencesDisclaimer()
        {
            return _view != null &&
                   _view.IsPreferencesOpen &&
                   _view.PreferencesView != null &&
                   _view.PreferencesView
                       .DebugRequestAboutDisclaimer();
        }

        public int DebugCurrentDifficulty
        {
            get { return _currentDifficulty; }
        }

        public int DebugPreferencesFirstDisabledDifficulty
        {
            get
            {
                return _view != null &&
                       _view.IsPreferencesOpen &&
                       _view.PreferencesView != null
                    ? _view.PreferencesView
                        .DebugFirstDisabledDifficulty
                    : -1;
            }
        }

        public bool DebugPreferencesRequestDifficultySwitch(
            int difficulty)
        {
            return _view != null &&
                   _view.IsPreferencesOpen &&
                   _view.PreferencesView != null &&
                   _view.PreferencesView
                       .DebugRequestDifficultySwitch(difficulty);
        }

        public void DebugOpenPreferencesBackgroundPreview()
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView
                    .DebugEnterBackgroundPreview();
            }
        }

        public void DebugEnterChartPreviewMode()
        {
            EnterChartPreviewMode();
        }

        public void DebugExitChartPreviewMode()
        {
            ExitChartPreviewMode(false);
        }

        public string DebugChartPreviewFraming
        {
            get
            {
                if (_view == null || _host == null)
                {
                    return "framing=<null>";
                }
                var composer = _host.Backgrounds != null
                    ? _host.Backgrounds.TrackEndComposer
                    : null;
                var masterScale = _host.Binding.MasterScene != null
                    ? _host.Binding.MasterScene.localScale.x
                    : 0.0f;
                return "collapse=" +
                       _view.ChartPreviewCollapse.ToString("0.000") +
                       " zoom=" +
                       _view.ChartPreviewZoomScale.ToString("0.000") +
                       " inset=" +
                       _view.ChartPreviewTopInsetFraction
                           .ToString("0.000") +
                       " masterScale=" +
                       masterScale.ToString("0.000") +
                       " trackEndScale=" +
                       (composer != null
                           ? composer.FramingScaleMultiplier
                               .ToString("0.000")
                           : "<null>") +
                       " trackEndInset=" +
                       (composer != null
                           ? composer.ScreenTopInset.ToString("0.000")
                           : "<null>");
            }
        }

        public void DebugSelectPreferencesBackground(
            string fileName)
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView
                    .DebugSelectBackground(fileName);
            }
        }

        public void DebugCancelPreferencesBackgroundPreview()
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView
                    .DebugCancelBackgroundPreview();
            }
        }

        public void DebugConfirmPreferencesBackgroundPreview()
        {
            if (_view != null &&
                _view.IsPreferencesOpen &&
                _view.PreferencesView != null)
            {
                _view.PreferencesView
                    .DebugConfirmBackgroundPreview();
            }
        }

        public void DebugClosePreferences()
        {
            CancelPreferences();
        }

        public string DebugPreferencesSummary
        {
            get
            {
                if (_view == null ||
                    !_view.IsPreferencesOpen ||
                    _view.PreferencesView == null)
                {
                    return "closed";
                }
                return "tab=" +
                       _view.PreferencesView.DebugActiveTab +
                       " preview=" +
                       _view.PreferencesView
                           .IsBackgroundPreviewOpen;
            }
        }

        public void DebugShowNewChartDialog()
        {
            if (_view == null)
            {
                return;
            }
            var projectRoot =
                Path.GetDirectoryName(Application.dataPath) ??
                Directory.GetCurrentDirectory();
            _view.ShowNewChartDialog(
                Path.Combine(
                    projectRoot,
                    "Diagnostics",
                    "nightfall.falsum"));
            _view.DebugConfigureNewChartDialogPreview(
                Path.Combine(
                    projectRoot,
                    "Diagnostics",
                    "nightfall.falsum"),
                Path.Combine(
                    Application.dataPath,
                    "InFalsus",
                    "Gameplay",
                    "Audio",
                    "Resources",
                    "alamode.ogg"),
                Path.Combine(
                    Application.streamingAssetsPath,
                    "Falsum",
                    "Playfield",
                    "playfieldBG.png"));
        }

        public void DebugHideNewChartDialog()
        {
            if (_view != null)
            {
                _view.CloseNewChartDialog();
            }
        }

        public string DebugRunRadialCoordinateCheck()
        {
            if (_view == null || _radial == null)
            {
                return "fail no-ui";
            }
            var topScreen = new Vector2(
                Screen.width * 0.5f,
                Screen.height - 20.0f);
            var bottomScreen = new Vector2(
                Screen.width * 0.5f,
                20.0f);
            var topPanel =
                _view.DebugScreenToPanelTopLeft(topScreen);
            var bottomPanel =
                _view.DebugScreenToPanelTopLeft(bottomScreen);
            var panelSize = _view.DebugPanelSize;
            var correct = topPanel.y < bottomPanel.y &&
                          topPanel.y < panelSize.y * 0.5f &&
                          bottomPanel.y > panelSize.y * 0.5f;
            return (correct ? "pass" : "fail") +
                   " panelSize=" + panelSize.ToString("F1") +
                   " topPanel=" + topPanel.ToString("F1") +
                   " bottomPanel=" + bottomPanel.ToString("F1");
        }

        private string DebugCheckOverlapPriority(
            NoteKind overlayKind)
        {
            var hold = CreateSelectionGeometryTestNote(
                NoteKind.Hold,
                2,
                4,
                5000.0,
                1000.0);
            var notes = new List<ChartNote> { hold };
            for (var i = 0; i < 6; i++)
            {
                var overlay = CreateSelectionGeometryTestNote(
                    overlayKind,
                    2,
                    1,
                    overlayKind == NoteKind.SkyArea ? 1200.0 : 0.0,
                    1000.0 + i * 250.0);
                overlay.StartPos = 0.35 + i * 0.06;
                overlay.EndPos = overlay.StartPos;
                if (overlayKind == NoteKind.SkyArea)
                {
                    overlay.StartWidth = 0.8;
                    overlay.EndWidth = 0.8;
                    overlay.GroupId = 100 + i;
                }
                notes.Add(overlay);
            }
            var loaded = LoadSelectionGeometryTestNotes(
                notes.ToArray(),
                2000.0);
            hold = loaded[0];
            Vector2[] holdPolygon;
            if (!_world.TryGetNoteScreenPolygon(
                    hold,
                    _playheadMs,
                    out holdPolygon))
            {
                return "overlap-" +
                       overlayKind.ToString().ToLowerInvariant() +
                       "-hold";
            }
            for (var i = 1; i < loaded.Length; i++)
            {
                var overlay = loaded[i];
                if (_host.NoteVisuals == null ||
                    !_host.NoteVisuals.IsNoteVisualActive(overlay))
                {
                    continue;
                }
                Vector2[] overlayPolygon;
                Vector2 overlap;
                if (!_world.TryGetNoteScreenPolygon(
                        overlay,
                        _playheadMs,
                        out overlayPolygon) ||
                    !TryFindPolygonOverlap(
                        holdPolygon,
                        overlayPolygon,
                        out overlap))
                {
                    continue;
                }
                Vector2 screen;
                ChartNote picked = null;
                if (!_world.TryCanvasPointToScreen(
                        overlap,
                        out screen) ||
                    !_world.TryPick(
                        screen,
                        _playheadMs,
                        loaded,
                        FalsumNotePickMask.All,
                        out picked) ||
                    picked.Kind != overlayKind)
                {
                    return "overlap-" +
                           overlayKind.ToString().ToLowerInvariant() +
                           "-pick-" +
                           (picked != null
                               ? picked.Kind.ToString()
                               : "none");
                }
                return null;
            }
            return "overlap-" +
                   overlayKind.ToString().ToLowerInvariant() +
                   "-none";
        }

        private void CheckSelectionHiddenAfterLeavingView(
            NoteKind kind,
            List<string> issues)
        {
            var note = LoadSelectionGeometryTest(
                kind,
                2,
                1,
                500.0,
                900.0);
            _selected.Add(note);
            UpdateOverlays();
            if (_world.SelectionPolygonCount != 1)
            {
                issues.Add(
                    "selection-visible-" +
                    kind.ToString().ToLowerInvariant());
            }
            _playheadMs = 6000.0;
            RefreshEditorVisuals();
            UpdateOverlays();
            if (_world.SelectionPolygonCount != 0)
            {
                issues.Add(
                    "selection-hidden-" +
                    kind.ToString().ToLowerInvariant());
            }
        }

        private ChartNote LoadSelectionGeometryTest(
            NoteKind kind,
            int lane,
            int width,
            double durationMs,
            double playheadMs)
        {
            var note = CreateSelectionGeometryTestNote(
                kind,
                lane,
                width,
                durationMs,
                1000.0);
            return LoadSelectionGeometryTestNotes(
                new[] { note },
                playheadMs)[0];
        }

        private ChartNote CreateSelectionGeometryTestNote(
            NoteKind kind,
            int lane,
            int width,
            double durationMs,
            double timeMs)
        {
            return new ChartNote
            {
                Kind = kind,
                TimeMs = timeMs,
                DurationMs = durationMs,
                StartLane = lane,
                WidthLanes = width,
                StartPos = 0.5,
                EndPos = 0.5,
                StartWidth = 2.0 / 24.0,
                EndWidth = 2.0 / 24.0,
                StartCellCount = 24,
                EndCellCount = 24,
                GroupId = 1,
            };
        }

        private ChartNote[] LoadSelectionGeometryTestNotes(
            ChartNote[] notes,
            double playheadMs)
        {
            var chart = new Chart
            {
                Bpm = 120.0,
                Division = 4.0,
                SongId = "selection-geometry-test",
                Difficulty = _currentDifficulty,
            };
            chart.Notes.AddRange(notes);
            chart.RebuildTimeline();
            _session.Replace(chart, 0.0);
            _chart = _session.Chart;
            _selected.Clear();
            _playheadMs = playheadMs;
            SetMode(EditorMode.Select);
            RefreshEditorVisuals();
            return _chart.Notes.ToArray();
        }

        private static bool TryFindPolygonOverlap(
            Vector2[] first,
            Vector2[] second,
            out Vector2 point)
        {
            point = Vector2.zero;
            if (first == null ||
                second == null ||
                first.Length < 3 ||
                second.Length < 3)
            {
                return false;
            }
            var firstMin = first[0];
            var firstMax = first[0];
            for (var i = 1; i < first.Length; i++)
            {
                firstMin = Vector2.Min(firstMin, first[i]);
                firstMax = Vector2.Max(firstMax, first[i]);
            }
            var secondMin = second[0];
            var secondMax = second[0];
            for (var i = 1; i < second.Length; i++)
            {
                secondMin = Vector2.Min(secondMin, second[i]);
                secondMax = Vector2.Max(secondMax, second[i]);
            }
            var min = Vector2.Max(firstMin, secondMin);
            var max = Vector2.Min(firstMax, secondMax);
            if (min.x >= max.x || min.y >= max.y)
            {
                return false;
            }
            const int steps = 32;
            for (var x = 0; x <= steps; x++)
            {
                for (var y = 0; y <= steps; y++)
                {
                    var candidate = new Vector2(
                        Mathf.Lerp(min.x, max.x, x / (float)steps),
                        Mathf.Lerp(min.y, max.y, y / (float)steps));
                    if (PointInPolygonLocal(candidate, first) &&
                        PointInPolygonLocal(candidate, second))
                    {
                        point = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool PointInPolygonLocal(
            Vector2 point,
            Vector2[] polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1;
                 i < polygon.Length;
                 j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x <
                    (b.x - a.x) * (point.y - a.y) /
                    (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        public string DebugSummary
        {
            get
            {
                return "mode=" + _mode +
                       " selected=" + _selected.Count +
                       " time=" + _playheadMs.ToString("0.000", Invariant) +
                       " notes=" + _chart.Notes.Count +
                       " playing=" +
                       (_host.Clock != null && _host.Clock.IsPlaying) +
                       " autoCombo=" + DebugAutoCombo +
                       " autoComboVisible=" + DebugAutoComboVisible +
                       " songAudio=" + DebugSongAudioPlaying +
                       " selectionPolys=" +
                       (_world != null
                           ? _world.SelectionPolygonCount
                           : 0) +
                       " selectionAttempt=" +
                       (_world != null
                           ? _world.SelectionAttemptCount
                           : 0) +
                       " selectionBuild=" +
                       (_world != null
                           ? _world.SelectionBuildDebug
                           : "none") +
                       " selectionBuildSeq=" +
                       (_world != null
                           ? _world.SelectionBuildSequence
                           : 0) +
                       " selectionBuildNow=" +
                       (_world != null
                           ? _world.SelectionBuildNowMs.ToString(
                               "0.000",
                               Invariant)
                           : "0") +
                       " selection=" +
                       (_world != null
                           ? _world.SelectionDebugSummary
                           : "none") +
                       " modePlane=" +
                       (_modePlane != null
                           ? _modePlane.DebugSummary
                           : "none") +
                       " measureNumbers=" +
                       (_world != null
                           ? _world.DebugMeasureNumberSummary
                           : "none") +
                       " measureNumbersEnabled=" +
                       _showMeasureNumbers +
                       " ghostEndPoint=" +
                       (_world != null && _world.GhostEndsAsPoint) +
                       " ghostTriangles=" +
                       (_world != null
                           ? _world.GhostTriangleCount
                           : 0) +
                       " selectedGeometry=" +
                       (_world != null && _selected.Count == 1
                           ? _world.DebugDescribeNoteGeometry(
                               _selected[0],
                               _playheadMs)
                           : "none") +
                       " package=" +
                       (_package != null
                           ? Path.GetFileName(_package.SourcePath)
                           : "<none>") +
                       " songInfo=" +
                       (_view != null
                           ? _view.DebugSongInfoCardLayout
                           : "none");
            }
        }

        public string DebugRunEditingChecks()
        {
            var originalText = ChartWriter.Write(
                _session.Chart,
                _session.DelayMs,
                _manifest.SongId,
                _currentDifficulty);
            var original = ChartParser.Parse(
                originalText.Replace("\r\n", "\n").Split('\n'),
                "<falsum-runtime-check>");
            var issues = new List<string>();
            var originalMode = _mode;
            SetMode(EditorMode.PlaceHold);
            _holdPlaced = true;
            _holdStart = 1000.0;
            _holdLane = 2;
            _holdWidth = 1;
            PrepareForPlayback();
            if (!_holdPlaced ||
                Math.Abs(_holdStart - 1000.0) > 0.001 ||
                _mode != EditorMode.PlaceHold)
            {
                issues.Add("playback_preserve_hold_start");
            }
            SetMode(EditorMode.SegmentSelect);
            _segmentHasStart = true;
            _segmentStart = 2000.0;
            _segmentEnd = 2000.0;
            PrepareForPlayback();
            if (!_segmentHasStart ||
                Math.Abs(_segmentStart - 2000.0) > 0.001 ||
                _mode != EditorMode.SegmentSelect)
            {
                issues.Add("playback_preserve_segment_start");
            }
            var skyVisibilityNow = 10000.0;
            var longSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = skyVisibilityNow - 12000.0,
                DurationMs = 20000.0,
            };
            if (!_world.DebugIsNoteNearView(
                    longSky,
                    skyVisibilityNow))
            {
                issues.Add("skyarea_long_tail_pick_window");
            }
            var passedSky = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = skyVisibilityNow - 2000.0,
                DurationMs = 1000.0,
            };
            if (_world.DebugIsNoteNearView(
                    passedSky,
                    skyVisibilityNow))
            {
                issues.Add("skyarea_passed_pick_window");
            }
            SetMode(originalMode);
            var initialCount = _chart.Notes.Count;
            SetMode(EditorMode.PlaceTap);
            _hasPointer = true;
            _pointerTime = 10000.0;
            _pointerLane = 2;
            _pointerLanePosition = 0.5f;
            _tapWidth = 1;
            PlaceTap();
            if (_chart.Notes.Count != initialCount + 1)
            {
                issues.Add("tap_place");
            }
            var placed = _chart.Notes[_chart.Notes.Count - 1];
            _selected.Clear();
            _selected.Add(placed);
            SetProperty("time_ms", "10500");
            if (Math.Abs(placed.TimeMs - 10500.0) > 0.001)
            {
                issues.Add("property");
            }
            CopySelection();
            var beforePaste = _chart.Notes.Count;
            _hasPointer = true;
            _pointerTime = 10500.0;
            _pointerRawTime = 10500.0;
            _pointerLane = 4;
            _pointerLanePosition = 0.1f;
            PasteClipboard();
            UpdateOverlays();
            if (_chart.Notes.Count != beforePaste ||
                _mode != EditorMode.PastePreview ||
                _selected.Count != 0 ||
                _pastePreviewGhosts.Count != _clipboard.Count)
            {
                issues.Add("paste_preview");
            }
            if (_viewMode == FalsumViewMode.Perspective3D &&
                (_host.NoteVisuals == null ||
                 _host.NoteVisuals.EditorGhostVisualCount != 1))
            {
                issues.Add("paste_tap_model_ghost");
            }
            var originalFlowSpeed = _flowSpeed;
            SetFlowSpeed(originalFlowSpeed >= 9.0f
                ? 8.0f
                : 9.0f);
            PrepareForPlayback();
            if (_mode != EditorMode.PastePreview ||
                _pastePreviewGhosts.Count != _clipboard.Count)
            {
                issues.Add("paste_flow_playback");
            }
            SetFlowSpeed(originalFlowSpeed);
            CancelPastePreview();
            if (_chart.Notes.Count != beforePaste)
            {
                issues.Add("paste_cancel");
            }
            PasteClipboard();
            CommitPastePreview();
            if (_selected.Count != 1 ||
                !_chart.Notes.Contains(_selected[0]) ||
                _selected[0].StartLane != 2 ||
                Math.Abs(_selected[0].TimeMs - 10500.0) > 0.001)
            {
                issues.Add("paste_time_only");
            }
            if (_chart.Notes.Count != initialCount + 2)
            {
                issues.Add("paste");
            }
            MirrorSelection();
            if (_selected.Count == 0 ||
                _selected[0].StartLane != 3)
            {
                issues.Add("mirror");
            }
            DeleteSelection();
            if (_chart.Notes.Count != initialCount + 1)
            {
                issues.Add("delete");
            }
            Undo();
            if (_chart.Notes.Count != initialCount + 2)
            {
                issues.Add("undo");
            }
            Redo();
            if (_chart.Notes.Count != initialCount + 1)
            {
                issues.Add("redo");
            }

            var beforeFlick = _chart.Notes.Count;
            SetMode(EditorMode.PlaceFlick);
            _flickHasTime = true;
            _flickTime = 12000.0;
            _flickDragging = true;
            _flickStart = 0.25;
            _flickCurrent = 0.5;
            CommitFlickPress();
            if (_chart.Notes.Count != beforeFlick + 1)
            {
                issues.Add("flick_drag_create");
            }
            if (Math.Abs(
                    SnapNormalizedCell(0.51f, 24) -
                    0.5) > 0.0001)
            {
                issues.Add("flick_grid_snap");
            }
            if (_chart.Notes.Count > beforeFlick)
            {
                var mirrorFlick = _chart.Notes[_chart.Notes.Count - 1];
                var originalPosition = mirrorFlick.StartPos;
                var originalDirection = mirrorFlick.FlickDirection;
                _selected.Clear();
                _selected.Add(mirrorFlick);
                RefreshPropertyPanel();
                var refreshBefore = _view.DebugPropertyRefreshCount;
                MirrorSelection();
                var mirroredDirection =
                    originalDirection == 16 ? 4 : 16;
                var mirroredDirectionLabel = L.Get(
                    mirroredDirection == 16
                        ? "falsum.direction.left"
                        : "falsum.direction.right");
                if (Math.Abs(
                        mirrorFlick.StartPos -
                        (1.0 - originalPosition)) > 0.0001 ||
                    mirrorFlick.FlickDirection != mirroredDirection)
                {
                    issues.Add("flick_mirror");
                }
                if (_view.DebugPropertyRefreshCount <= refreshBefore ||
                    _view.DebugPropertyValue("flick_direction") !=
                    mirroredDirectionLabel)
                {
                    issues.Add("mirror_property_refresh");
                }
                var directionControls =
                    _view.DebugFlickDirectionControls();
                if (directionControls !=
                    "count=2 selected=1 value=" +
                    mirroredDirectionLabel)
                {
                    issues.Add("flick_direction_buttons");
                }
                var colorBeforeToggle =
                    mirrorFlick.FlickDirection;
                ToggleSelectedFlickColors();
                if (mirrorFlick.FlickDirection !=
                    (colorBeforeToggle == 16 ? 4 : 16))
                {
                    issues.Add("flick_color_shortcut");
                }

                var undoBeforeProperty = _session.UndoCount;
                SetProperty("flick_position", "0/24");
                if (FalsumSkyBounds.IsOutOfBounds(
                        mirrorFlick.StartPos,
                        mirrorFlick.StartWidth) ||
                    Math.Abs(
                        mirrorFlick.StartPos -
                        mirrorFlick.StartWidth * 0.5) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("flick_property_position_clamp");
                }
                if (_session.UndoCount != undoBeforeProperty + 1)
                {
                    issues.Add("flick_position_history");
                }

                SetProperty("flick_width", "24/24");
                if (FalsumSkyBounds.IsOutOfBounds(
                        mirrorFlick.StartPos,
                        mirrorFlick.StartWidth) ||
                    Math.Abs(
                        mirrorFlick.StartWidth -
                        mirrorFlick.StartPos * 2.0) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("flick_property_width_clamp");
                }

                mirrorFlick.StartPos = 0.0;
                mirrorFlick.StartWidth = 0.4;
                MirrorSelection();
                if (FalsumSkyBounds.IsOutOfBounds(
                        mirrorFlick.StartPos,
                        mirrorFlick.StartWidth) ||
                    Math.Abs(mirrorFlick.StartPos - 0.8) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("flick_mirror_clamp");
                }

                _selected.Clear();
                _selected.Add(mirrorFlick);
                _hasPointer = true;
                _pointerLane = 2;
                _pointerLanePosition = 0.5f;
                _pointerTime = 13000.0;
                _pointerRawTime = 13000.0;
                var undoBeforeDrag = _session.UndoCount;
                var lockedPosition = mirrorFlick.StartPos;
                var lockedWidth = mirrorFlick.StartWidth;
                BeginSelectionDrag();
                _pointerLanePosition = -1.0f;
                _pointerRawTime =
                    13000.0 +
                    _grid.StepMs(_chart, 13000.0) * 2.2;
                _pointerTime = IntegerTimeMs(
                    _world.SnapTime(
                        _pointerRawTime,
                        13000.0));
                DragSelection();
                if (Math.Abs(
                        mirrorFlick.StartPos -
                        lockedPosition) >
                    FalsumSkyBounds.Epsilon ||
                    Math.Abs(
                        mirrorFlick.StartWidth -
                        lockedWidth) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("flick_left_drag_position_locked");
                }
                if (Math.Abs(mirrorFlick.TimeMs - 13000.0) < 0.0001)
                {
                    issues.Add("flick_left_drag_time_missing");
                }
                if (!_session.CommitChange() ||
                    _session.UndoCount != undoBeforeDrag + 1)
                {
                    issues.Add("flick_drag_history");
                }
                _draggingSelection = false;
                _selectionDragPending = false;
                _dragStates.Clear();

                var pastedFlick = new ChartNote
                {
                    Kind = NoteKind.Flick,
                    TimeMs = 13100.0,
                    StartPos = 0.0,
                    StartWidth = 0.4,
                    StartCellCount = 24,
                    EndCellCount = 24,
                };
                _clipboard.Clear();
                _clipboard.Add(new ClipboardNote
                {
                    Note = pastedFlick,
                    TimeOffsetMs = 0.0,
                });
                _hasPointer = true;
                _pointerTime = 13100.0;
                _pointerRawTime = 13100.0;
                _pointerLane = 2;
                _pointerLanePosition = 0.0f;
                PasteClipboard();
                CommitPastePreview();
                pastedFlick = _chart.Notes[_chart.Notes.Count - 1];
                if (FalsumSkyBounds.IsOutOfBounds(
                        pastedFlick.StartPos,
                        pastedFlick.StartWidth) ||
                    Math.Abs(pastedFlick.StartPos - 0.2) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("flick_paste_clamp");
                }
            }
            var flickGridA = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartCellCount = 24,
                EndCellCount = 24,
            };
            var flickGridB = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartCellCount = 36,
                EndCellCount = 36,
            };
            _selected.Clear();
            _selected.Add(flickGridA);
            _selected.Add(flickGridB);
            if (ComputeFlickEditGrid() != 72)
            {
                issues.Add("flick_edit_lcm");
            }
            var fractionNote = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = 13.5 / 24.0,
                StartWidth = 4.0 / 24.0,
                StartCellCount = 24,
                EndCellCount = 24,
            };
            NormalizeNoteFractions(fractionNote);
            if (fractionNote.StartCellCount != 48 ||
                Math.Abs(fractionNote.StartPos * 48.0 - 27.0) > 0.0001 ||
                Math.Abs(fractionNote.StartWidth * 48.0 - 8.0) > 0.0001)
            {
                issues.Add("fraction_normalize");
            }

            var beforeSky = _chart.Notes.Count;
            SetMode(EditorMode.PlaceSkyArea);
            _skyStage = 0;
            _skyStartTime = 14000.0;
            _skyStartAnchor = 0.25;
            _skyStartPos = 0.25;
            _skyStartWidth = 0.25;
            _skyStartFirstEdge = true;
            _skyCompletingStart = true;
            FinalizeSkyStart(0.5);
            if (_skyStage != 1)
            {
                issues.Add("sky_start_drag");
            }
            if (Math.Abs(_skyStartPos - 0.375) > 0.0001 ||
                Math.Abs(_skyStartWidth - 0.25) > 0.0001)
            {
                issues.Add("sky_start_edge_semantics");
            }
            _skyStage = 0;
            _skyStartFirstEdge = true;
            _skyStartAnchor = 0.75;
            _skyStartPos = 0.75;
            _skyStartWidth = 0.25;
            FinalizeSkyStart(0.5);
            if (Math.Abs(_skyStartPos - 0.625) > 0.0001 ||
                Math.Abs(_skyStartWidth - 0.25) > 0.0001)
            {
                issues.Add("sky_start_left_edge_semantics");
            }
            _skyStartAnchor = 0.25;
            _skyStartPos = 0.25;
            _skyStartWidth = 0.25;
            FinalizeSkyStart(0.5);
            _hasPointer = true;
            _pointerTime = 16000.0;
            _pointerLanePosition = 0.7f;
            UpdateOverlays();
            if (!_world.GhostEndsAsPoint ||
                _world.GhostTriangleCount == 0)
            {
                issues.Add("sky_point_ghost");
            }
            _skyEndTime = 16000.0;
            _skyEndAnchor = 0.625;
            _skyEndPos = 0.625;
            _skyEndWidth = SkyAreaPointWidth;
            _skyStage = 2;
            _skyEndFirstEdge = true;
            _skyDragging = true;
            _skyEndPos = 0.625;
            _skyEndWidth = 0.125;
            UpdateOverlays();
            if (_world.GhostEndsAsPoint ||
                _world.GhostTriangleCount == 0)
            {
                issues.Add("sky_expand_ghost");
            }
            FinalizeSkyEnd(0.75);
            if (_chart.Notes.Count != beforeSky + 1)
            {
                issues.Add("sky_end_drag");
            }
            else
            {
                var skyNote = _chart.Notes[_chart.Notes.Count - 1];
                if (Math.Abs(skyNote.EndPos - 0.6875) > 0.0001 ||
                    Math.Abs(skyNote.EndWidth - 0.125) > 0.0001)
                {
                    issues.Add("sky_end_edge_semantics");
                }
                if (skyNote.GroupId <= 0)
                {
                    issues.Add("skyarea_group_after_create");
                }
                _selected.Clear();
                _selected.Add(skyNote);
                SetProperty("sky_start", "0/24");
                if (FalsumSkyBounds.IsOutOfBounds(
                        skyNote.StartPos,
                        skyNote.StartWidth) ||
                    Math.Abs(
                        skyNote.StartPos -
                        skyNote.StartWidth * 0.5) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("sky_start_position_clamp");
                }
                SetProperty("sky_start_width", "24/24");
                if (FalsumSkyBounds.IsOutOfBounds(
                        skyNote.StartPos,
                        skyNote.StartWidth) ||
                    Math.Abs(
                        skyNote.StartWidth -
                        skyNote.StartPos * 2.0) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("sky_start_width_clamp");
                }
                SetProperty("sky_end", "24/24");
                if (FalsumSkyBounds.IsOutOfBounds(
                        skyNote.EndPos,
                        skyNote.EndWidth) ||
                    Math.Abs(
                        1.0 - skyNote.EndPos -
                        skyNote.EndWidth * 0.5) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("sky_end_position_clamp");
                }
                SetProperty("sky_end_width", "24/24");
                if (FalsumSkyBounds.IsOutOfBounds(
                        skyNote.EndPos,
                        skyNote.EndWidth) ||
                    Math.Abs(
                        skyNote.EndWidth -
                        (1.0 - skyNote.EndPos) * 2.0) >
                    FalsumSkyBounds.Epsilon)
                {
                    issues.Add("sky_end_width_clamp");
                }
                var savedDuration = skyNote.DurationMs;
                var savedStartPos = skyNote.StartPos;
                var savedEndPos = skyNote.EndPos;
                var savedStartWidth = skyNote.StartWidth;
                var savedEndWidth = skyNote.EndWidth;
                _selected.Clear();
                _selected.Add(skyNote);
                _hasPointer = true;
                _pointerLane = 2;
                _pointerLanePosition = 0.5f;
                _pointerTime = 17000.0;
                _pointerRawTime = 17000.0;
                BeginSelectionDrag();
                _pointerRawTime = 17123.4;
                _pointerTime = IntegerTimeMs(
                    _world.SnapTime(17123.4, 17000.0));
                _pointerLanePosition = 0.8f;
                DragSelection();
                if (Math.Abs(skyNote.DurationMs - savedDuration) > 0.0001 ||
                    Math.Abs(skyNote.StartPos - savedStartPos) > 0.0001 ||
                    Math.Abs(skyNote.EndPos - savedEndPos) > 0.0001 ||
                    Math.Abs(skyNote.StartWidth - savedStartWidth) > 0.0001 ||
                    Math.Abs(skyNote.EndWidth - savedEndWidth) > 0.0001)
                {
                    issues.Add("skyarea_time_only_drag");
                }
                if (Math.Abs(
                        skyNote.TimeMs -
                        Math.Round(skyNote.TimeMs)) > 0.0001)
                {
                    issues.Add("integer_time_drag");
                }
                _session.CancelChange();
                _dragStates.Clear();
                _draggingSelection = false;
                _selectionDragPending = false;
            }

            var selectionDragThreshold =
                SelectionDragThresholdPixels *
                Mathf.Max(1.0f, Screen.height / 1080.0f);
            if (ExceedsSelectionDragThreshold(
                    Vector2.zero,
                    new Vector2(
                        selectionDragThreshold - 1.0f,
                        0.0f)) ||
                !ExceedsSelectionDragThreshold(
                    Vector2.zero,
                    new Vector2(
                        selectionDragThreshold,
                        0.0f)))
            {
                issues.Add("flick_drag_threshold");
            }

            var currentGridTime = _grid.Snap(_chart, 12345.0);
            var currentGridStep = _grid.StepMs(
                _chart,
                currentGridTime);
            var snapTestOriginal =
                currentGridTime + currentGridStep * 0.25;
            var snapTestNote = new ChartNote
            {
                Kind = NoteKind.Tap,
                TimeMs = snapTestOriginal,
                StartLane = 2,
                WidthLanes = 1,
            };
            snapTestNote.Id = _chart.Notes.Count;
            _chart.Notes.Add(snapTestNote);
            _selected.Clear();
            _selected.Add(snapTestNote);
            _hasPointer = true;
            _pointerTime = currentGridTime;
            _pointerRawTime = currentGridTime;
            _pointerLane = 2;
            _pointerLanePosition = 0.5f;
            BeginSelectionDrag();
            DragSelection();
            if (Math.Abs(
                    snapTestNote.TimeMs -
                    IntegerTimeMs(snapTestOriginal)) > 0.001)
            {
                issues.Add("drag_snap_preserve_original");
            }
            _pointerTime = currentGridTime;
            _pointerRawTime = currentGridTime;
            BeginSelectionDrag();
            _pointerRawTime =
                currentGridTime - currentGridStep * 0.2;
            DragSelection();
            if (Math.Abs(
                    snapTestNote.TimeMs -
                    IntegerTimeMs(currentGridTime)) > 0.001)
            {
                issues.Add("drag_snap_lower_grid");
            }

            _hasPointer = true;
            _pointerTime = 18000.0;
            _pointerLane = 2;
            _pointerLanePosition = 0.5f;
            SetMode(EditorMode.PlaceTap);
            UpdateOverlays();
            if (_world.CursorVertexCount != 0 ||
                _world.TimePlaneVertexCount != 0)
            {
                issues.Add("tap_helpers");
            }
            SetMode(EditorMode.PlaceHold);
            UpdateOverlays();
            if (_world.CursorVertexCount != 0 ||
                _world.TimePlaneVertexCount != 0)
            {
                issues.Add("hold_helpers");
            }

            SetPointPlaceMode(EditorMode.PlaceTap);
            if (_mode != EditorMode.PlaceTap)
            {
                issues.Add("point_mode_tap");
            }
            _tapWidth = 4;
            _pointerLane = 0;
            _pointerLanePosition = 0.0f;
            var wideTapGhost = BuildGhost();
            if (!wideTapGhost.Visible ||
                wideTapGhost.Kind != NoteKind.Tap ||
                wideTapGhost.Lane != 1 ||
                wideTapGhost.WidthLanes != 4)
            {
                issues.Add("wide_tap_ghost");
            }
            if (Math.Abs(
                    InFalsus.Gameplay.Presentation.Notes.NoteRenderer
                        .GroundNoteWidthScale(NoteKind.Tap, 2) -
                    2.0f) > 0.0001f ||
                Math.Abs(
                    InFalsus.Gameplay.Presentation.Notes.NoteRenderer
                        .GroundNoteWidthScale(NoteKind.Tap, 3) -
                    3.0f) > 0.0001f ||
                Math.Abs(
                    InFalsus.Gameplay.Presentation.Notes.NoteRenderer
                        .GroundNoteWidthScale(NoteKind.Hold, 2) -
                    1.0f) > 0.0001f ||
                Math.Abs(
                    InFalsus.Gameplay.Presentation.Notes.NoteRenderer
                        .GroundNoteWidthScale(NoteKind.Hold, 3) -
                    1.0f) > 0.0001f ||
                Math.Abs(
                    InFalsus.Gameplay.Presentation.Notes.NoteRenderer
                        .GroundNoteWidthScale(NoteKind.Hold, 4) -
                    1.0f) > 0.0001f)
            {
                issues.Add("ground_width_scale");
            }
            SetPointPlaceMode(EditorMode.PlaceHold);
            _tapWidth = 3;
            _pointerLane = 5;
            _pointerLanePosition = 1.0f;
            var wideHoldGhost = BuildGhost();
            if (_mode != EditorMode.PlaceHold ||
                !wideHoldGhost.Visible ||
                wideHoldGhost.Kind != NoteKind.Hold ||
                wideHoldGhost.Lane != 2 ||
                wideHoldGhost.WidthLanes != 3)
            {
                issues.Add("wide_hold_ghost");
            }
            HandleHoldDown();
            ApplyActivePlacementWidth(2);
            var adjustedHoldGhost = BuildGhost();
            ApplyActivePlacementWidth(4);
            var widenedHoldGhost = BuildGhost();
            if (!_holdPlaced ||
                adjustedHoldGhost.WidthLanes != 2 ||
                adjustedHoldGhost.Lane != 2 ||
                widenedHoldGhost.WidthLanes != 4 ||
                widenedHoldGhost.Lane != 1)
            {
                issues.Add("hold_width_adjust");
            }
            _holdPlaced = false;
            SetPointPlaceMode(EditorMode.PlaceFlick);
            if (_mode != EditorMode.PlaceFlick)
            {
                issues.Add("point_mode_flick");
            }
            SetPointPlaceMode(EditorMode.PlaceSkyArea);
            if (_mode != EditorMode.PlaceSkyArea)
            {
                issues.Add("point_mode_skyarea");
            }
            _tapWidth = 1;

            var firstPasteAnchor = new ChartNote
            {
                Kind = NoteKind.Tap,
                TimeMs = 12000.0,
                StartLane = 1,
                WidthLanes = 1,
            };
            var earlierSecondNote = new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = 11000.0,
                StartLane = 3,
                WidthLanes = 1,
                DurationMs = 500.0,
            };
            _selected.Clear();
            _selected.Add(firstPasteAnchor);
            _selected.Add(earlierSecondNote);
            CopySelection();
            _hasPointer = true;
            _pointerTime = 20000.0;
            _pointerRawTime = 20000.0;
            _pointerLane = 4;
            _pointerLanePosition = 0.1f;
            PasteClipboard();
            UpdateOverlays();
            BuildPastePreviewNotes(_pastePreviewNotes);
            if (_pastePreviewAnchor == null ||
                Math.Abs(_pastePreviewAnchor.TimeMs - 11000.0) > 0.001 ||
                _pastePreviewNotes.Count != 2 ||
                Math.Abs(_pastePreviewNotes[0].TimeMs - 21000.0) > 0.001 ||
                Math.Abs(_pastePreviewNotes[1].TimeMs - 20000.0) > 0.001)
            {
                issues.Add("paste_anchor_earliest");
            }
            if (_viewMode == FalsumViewMode.Perspective3D &&
                (_host.NoteVisuals == null ||
                 _host.NoteVisuals.EditorGhostVisualCount != 2))
            {
                issues.Add("paste_hold_model_ghost");
            }
            CancelPastePreview();

            var originalDisableClamping =
                _disableOutOfBoundsClamping;
            _disableOutOfBoundsClamping = true;
            var unclampedFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = -0.25,
                StartWidth = 0.5,
                StartCellCount = 24,
                EndCellCount = 24,
                FlickDirection = 4,
            };
            ClampEditedSkyBounds(
                "flick_position",
                unclampedFlick);
            NormalizeSkyNoteToCells(unclampedFlick);
            if (Math.Abs(unclampedFlick.StartPos + 0.25) >
                    0.0001 ||
                Math.Abs(unclampedFlick.StartWidth - 0.5) >
                    0.0001)
            {
                issues.Add("out_of_bounds_clamp_disabled");
            }
            _disableOutOfBoundsClamping = false;
            var clampedFlick = new ChartNote
            {
                Kind = NoteKind.Flick,
                StartPos = -0.25,
                StartWidth = 0.5,
                StartCellCount = 24,
                EndCellCount = 24,
                FlickDirection = 4,
            };
            ClampEditedSkyBounds(
                "flick_position",
                clampedFlick);
            NormalizeSkyNoteToCells(clampedFlick);
            if (FalsumSkyBounds.IsOutOfBounds(
                    clampedFlick.StartPos,
                    clampedFlick.StartWidth))
            {
                issues.Add("out_of_bounds_clamp_enabled");
            }
            _disableOutOfBoundsClamping =
                originalDisableClamping;

            if (!original.Success)
            {
                issues.Add("restore_parse");
            }
            else
            {
                _session.Replace(original.Chart, 0.0);
                _selected.Clear();
                RefreshEditorVisuals();
            }
            SetMode(EditorMode.Select);
            return issues.Count == 0
                ? "pass"
                : string.Join(",", issues.ToArray());
        }

        public void DebugShowEventsPanel()
        {
            if (!_initialized || _view == null)
            {
                return;
            }
            _view.SetEventsPanelOpen(true);
            RefreshEventsPanel();
        }

        public void DebugHideEventsPanel()
        {
            if (_initialized && _view != null)
            {
                _view.SetEventsPanelOpen(false);
            }
        }

        public string DebugRunEventPanelChecks()
        {
            return _view != null
                ? _view.DebugRunEventPanelChecks()
                : "fail view";
        }

        public string DebugRunEventEditorChecks()
        {
            var originalText = ChartWriter.Write(
                _session.Chart,
                _session.DelayMs,
                _manifest.SongId,
                _currentDifficulty);
            var original = ChartParser.Parse(
                originalText.Replace("\r\n", "\n").Split('\n'),
                "<falsum-event-editor-check>");
            var issues = new List<string>();
            _selected.Clear();
            RefreshPropertyPanel();
            _playheadMs = 12000.0;
            var initialCount = _chart.Events.Count;
            var added = CreateChartEvent(ChartEventKind.Bpm);
            if (added == null ||
                _chart.Events.Count != initialCount + 1)
            {
                issues.Add("add");
            }
            else
            {
                SubmitChartEvent(new FalsumEventEditSubmission
                {
                    Target = added,
                    Time = "12345",
                    Bpm = "177.5",
                    Division = "6",
                });
                if (Math.Abs(added.TimeMs - 12345.0) > 0.001 ||
                    Math.Abs(added.Bpm - 177.5) > 0.001 ||
                    Math.Abs(added.Division - 6.0) > 0.001)
                {
                    issues.Add("edit");
                }
                var duplicated = DuplicateChartEventInternal(added);
                if (duplicated == null ||
                    _chart.Events.Count != initialCount + 2)
                {
                    issues.Add("duplicate");
                }
                else
                {
                    DeleteChartEvent(duplicated);
                    if (_chart.Events.Count != initialCount + 1)
                    {
                        issues.Add("delete");
                    }
                }
                if (!_session.Undo() ||
                    _chart.Events.Count != initialCount + 2)
                {
                    issues.Add("undo");
                }
                if (!_session.Redo() ||
                    _chart.Events.Count != initialCount + 1)
                {
                    issues.Add("redo");
                }
            }

            if (!original.Success)
            {
                issues.Add("restore-parse");
            }
            else
            {
                _session.Replace(
                    original.Chart,
                    original.Chart.DelayMs);
                _selected.Clear();
                RefreshEditorVisuals();
                RefreshPropertyPanel();
                DebugShowEventsPanel();
            }
            return issues.Count == 0
                ? "pass"
                : string.Join(",", issues.ToArray());
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            _performanceMonitor.BeginFrame();
            var viewTickStartedAt =
                FalsumPerformanceMonitor.Timestamp();
            _view.Tick();
            _performanceMonitor.RecordViewTick(
                FalsumPerformanceMonitor.ElapsedMilliseconds(
                    viewTickStartedAt));
            ApplyChartPreviewFraming();
            if (_chartPreviewPhase != ChartPreviewPhase.Inactive &&
                (Input.GetKeyDown(KeyCode.Space) ||
                 Input.GetKeyDown(KeyCode.Escape)))
            {
                ExitChartPreviewMode();
                return;
            }

            if (_chartPreviewPhase == ChartPreviewPhase.Inactive)
            {
                HandleToolTimePickInput();
            }
            _radial.Tick();
            if (_chartPreviewPhase == ChartPreviewPhase.Inactive &&
                _spaceHeld &&
                _inputRouter.WasReleased(
                    FalsumInputAction.TemporaryAudition))
            {
                EndTemporaryPlayback();
            }

            TickChartPreviewSequence();
            TickAudioPreprocessAudition();
            TickAudioPreprocessPrompt();
            TickAudioPreprocessAfterCreate();
            double? frameDurationMs = null;
            if (_host.Clock != null && _host.Clock.IsPlaying)
            {
                _playheadMs = Math.Max(0.0, _host.Clock.TimeMs);
                var duration = TimelineDuration();
                frameDurationMs = duration;
                if (_playheadMs >= duration)
                {
                    PausePlaybackAt(duration);
                    if (_chartPreviewPhase ==
                        ChartPreviewPhase.Playback)
                    {
                        ExitChartPreviewMode(false);
                    }
                }
                else
                {
                    _host.UpdateChartPlaybackFrame(_playheadMs);
                }
            }
            if (IsMiddleEditActive &&
                (_view.IsModalOpen ||
                 (_host.Clock != null && _host.Clock.IsPlaying)))
            {
                CancelMiddleEdit();
            }
            if (_chartPreviewPhase != ChartPreviewPhase.Inactive ||
                _view.IsModalOpen)
            {
                _hasPointer = false;
            }
            else if (_host.Clock != null &&
                     _host.Clock.IsPlaying &&
                     _mode == EditorMode.Select)
            {
                _hasPointer = false;
            }
            else
            {
                UpdatePointer();
            }
            if (_chartPreviewPhase == ChartPreviewPhase.Inactive &&
                !_view.IsModalOpen &&
                (Input.GetMouseButtonDown(0) ||
                 Input.GetMouseButtonDown(1) ||
                 Input.GetMouseButtonDown(2)) &&
                !_view.IsPointerOverUi(Input.mousePosition))
            {
                _view.ClearInputFocus();
            }
            if (_chartPreviewPhase == ChartPreviewPhase.Inactive &&
                !_view.IsTyping() &&
                !_view.IsModalOpen)
            {
                // Touch gestures run BEFORE the keyboard/mouse path: a
                // two-finger swipe is a command, and it must consume the frame
                // so the same touch is not also interpreted as a chart drag.
                HandleTouchGestures();
                if (!IsTwoFingerGestureActive)
                {
                    HandleKeyboard();
                    if (_host.Clock == null ||
                        !_host.Clock.IsPlaying ||
                        _mode == EditorMode.PastePreview)
                    {
                        HandleMouse();
                    }
                }
                else
                {
                    // Legibility for the gesture: the swipe replaced the
                    // pointer this frame, so clear the hover affordances rather
                    // than leaving a stale armed handle behind.
                    _world.SetAnchorHighlight(-1, false);
                }
            }
            UpdateHint();
            FlushSkyGroupingDirty();
            UpdateViews(frameDurationMs);
            TickAutoBackup();
        }

        private void ApplyChartPreviewFraming()
        {
            if (_view == null || _host == null)
            {
                return;
            }

            var zoom = Mathf.Max(0.1f, _view.ChartPreviewZoomScale);
            var opticalZoom = Mathf.LerpUnclamped(
                EditModeTrackScaleMultiplier,
                zoom,
                _view.ChartPreviewCollapse);
            var topInset = _view.ChartPreviewTopInsetFraction;
            var camera = Camera.main;
            if (camera != null &&
                !camera.orthographic &&
                _hasChartPreviewBaseFieldOfView)
            {
                var halfFovTangent =
                    Mathf.Tan(
                        _chartPreviewBaseFieldOfView *
                        0.5f *
                        Mathf.Deg2Rad) /
                    opticalZoom;
                camera.fieldOfView =
                    2.0f *
                    Mathf.Atan(halfFovTangent) *
                    Mathf.Rad2Deg;
            }

            var composer = _host.Backgrounds != null
                ? _host.Backgrounds.TrackEndComposer
                : null;
            composer?.SetPreviewFraming(zoom, topInset);
        }

        private void CaptureChartPreviewBaseFieldOfView()
        {
            var camera = Camera.main;
            if (camera == null || camera.orthographic)
            {
                return;
            }
            _chartPreviewBaseFieldOfView = camera.fieldOfView;
            _hasChartPreviewBaseFieldOfView = true;
        }

        private void RestoreChartPreviewFraming()
        {
            if (_host == null || _host.Binding == null)
            {
                return;
            }
            var camera = Camera.main;
            if (camera != null &&
                !camera.orthographic &&
                _hasChartPreviewBaseFieldOfView)
            {
                camera.fieldOfView = _chartPreviewBaseFieldOfView;
            }
            _host.Backgrounds?.TrackEndComposer?.SetPreviewFraming(
                1.0f,
                0.0f);
        }

        private void EnterChartPreviewMode()
        {
            if (!_initialized ||
                _chartPreviewPhase != ChartPreviewPhase.Inactive)
            {
                return;
            }
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_host.Clock != null && _host.Clock.IsPlaying)
            {
                PausePlaybackAt(_host.Clock.TimeMs);
            }

            PrepareForPlayback();
            _host.StopPreviewTapCues();
            _spaceHeld = false;
            _chartPreviewRateIndex = _rateIndex;
            _rateIndex = 0;
            _host.SetPlaybackRate(PlaybackRates[_rateIndex]);
            _view.SetPlaybackRate(PlaybackRates[_rateIndex]);
            _chartPreviewGridWasEnabled = _grid.Enabled;
            _grid.Enabled = false;
            _view.SetGridEnabled(false);
            // Chart preview is the "what will the player see" view, so it
            // always runs the real track() timing regardless of the editing
            // toggle. Exit restores whatever the editor had.
            _chartPreviewTrackSpeedEffectsWasEnabled =
                _chart.TrackSpeedEffectsEnabled;
            SetTrackSpeedEffectsEnabled(
                true,
                announce: false,
                refresh: false);
            _chartPreviewPhase = ChartPreviewPhase.WaitingForChrome;
            _playheadMs = 0.0;
            RefreshEditorAtChartTime(0.0);
            _view.SetPlaybackState(false);
            _view.SetChartPreviewMode(true);
        }

        private void ExitChartPreviewMode(bool pausePlayback = true)
        {
            if (_chartPreviewPhase == ChartPreviewPhase.Inactive)
            {
                return;
            }
            if (pausePlayback &&
                _host.Clock != null &&
                _host.Clock.IsPlaying)
            {
                PausePlaybackAt(_host.Clock.TimeMs);
            }
            _host.StopPreviewTapCues();

            _chartPreviewPhase = ChartPreviewPhase.Inactive;
            _chartPreviewTapsRemaining = 0;
            _grid.Enabled = _chartPreviewGridWasEnabled;
            _view.SetGridEnabled(_grid.Enabled);
            SetTrackSpeedEffectsEnabled(
                _chartPreviewTrackSpeedEffectsWasEnabled,
                announce: false,
                refresh: false);
            _rateIndex = Mathf.Clamp(
                _chartPreviewRateIndex,
                0,
                PlaybackRates.Length - 1);
            _host.SetPlaybackRate(PlaybackRates[_rateIndex]);
            _view.SetPlaybackRate(PlaybackRates[_rateIndex]);
            _view.SetChartPreviewMode(false);
            _view.SetPlaybackState(
                _host.Clock != null && _host.Clock.IsPlaying);
            RefreshEditorAtChartTime(_playheadMs);
            UpdateViews();
        }

        private void TickChartPreviewSequence()
        {
            switch (_chartPreviewPhase)
            {
                case ChartPreviewPhase.WaitingForChrome:
                    if (_view.IsChartPreviewChromeCollapsed)
                    {
                        BeginChartPreviewCountIn();
                    }
                    break;
                case ChartPreviewPhase.CountIn:
                    TickChartPreviewCountIn();
                    break;
                case ChartPreviewPhase.WaitingForPlayback:
                    if (Time.unscaledTimeAsDouble + 0.0001 >=
                        _chartPreviewPlaybackAt)
                    {
                        StartChartPreviewPlayback();
                    }
                    break;
            }
        }

        private void BeginChartPreviewCountIn()
        {
            if (!TryGetFirstNoteTime(out _chartPreviewFirstNoteMs))
            {
                _chartPreviewPlaybackStartMs = 0.0;
                StartChartPreviewPlayback();
                return;
            }

            _chartPreviewBeatCount = Mathf.Max(
                1,
                (int)Math.Round(
                    _chart.Division,
                    MidpointRounding.AwayFromZero));
            var bpm = Math.Max(
                1.0,
                _chart.BpmAt(_chartPreviewFirstNoteMs));
            _chartPreviewBeatMs = 60000.0 / bpm;
            _chartPreviewTapIntervalSeconds =
                _chartPreviewBeatMs *
                PreviewTapIntervalBeats /
                1000.0;
            _chartPreviewTapsRemaining =
                _chartPreviewBeatCount;
            _chartPreviewNextTapAt =
                Time.unscaledTimeAsDouble +
                PreviewStartDelaySeconds;
            _chartPreviewPhase = ChartPreviewPhase.CountIn;
        }

        private void TickChartPreviewCountIn()
        {
            var now = Time.unscaledTimeAsDouble;
            if (now + 0.0001 < _chartPreviewNextTapAt)
            {
                return;
            }

            while (_chartPreviewTapsRemaining > 0 &&
                   now + 0.0001 >= _chartPreviewNextTapAt)
            {
                var cueAt = _chartPreviewNextTapAt;
                _host.PlayPreviewTapCue();
                _chartPreviewTapsRemaining--;
                _chartPreviewNextTapAt +=
                    _chartPreviewTapIntervalSeconds;
                if (_chartPreviewTapsRemaining > 0)
                {
                    continue;
                }

                var noteGapMs = PreviewDurationForBeatsBefore(
                    _chartPreviewFirstNoteMs,
                    PreviewNoteLeadBeats);
                var desiredStartMs =
                    _chartPreviewFirstNoteMs -
                    noteGapMs;
                _chartPreviewPlaybackStartMs =
                    Math.Max(0.0, desiredStartMs);
                _chartPreviewPlaybackAt =
                    cueAt +
                    Math.Max(0.0, -desiredStartMs) /
                    1000.0;
                _chartPreviewPhase =
                    ChartPreviewPhase.WaitingForPlayback;
                TickChartPreviewSequence();
            }
        }

        private void StartChartPreviewPlayback()
        {
            if (_chartPreviewPhase !=
                ChartPreviewPhase.WaitingForPlayback &&
                _chartPreviewPhase !=
                ChartPreviewPhase.WaitingForChrome)
            {
                return;
            }

            _chartPreviewPhase = ChartPreviewPhase.Playback;
            _playheadMs =
                Math.Max(0.0, _chartPreviewPlaybackStartMs);
            PrepareForPlayback();
            _host.BeginChartPlayback(_playheadMs);
            SeekClockToChartTime(_playheadMs);
            _host.Clock.Resume();
            _view.SetPlaybackState(true);
            _host.UpdateChartPlaybackFrame(_playheadMs);
        }

        private bool TryGetFirstNoteTime(out double timeMs)
        {
            timeMs = 0.0;
            if (_chart == null || _chart.Notes.Count == 0)
            {
                return false;
            }
            timeMs = double.MaxValue;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                var note = _chart.Notes[i];
                if (note != null && note.TimeMs < timeMs)
                {
                    timeMs = note.TimeMs;
                }
            }
            if (timeMs == double.MaxValue)
            {
                timeMs = 0.0;
                return false;
            }
            return true;
        }

        private double PreviewDurationForBeatsBefore(
            double timeMs,
            double beats)
        {
            var remaining = Math.Max(0.0, beats);
            var cursor = Math.Max(0.0, timeMs);
            var duration = 0.0;
            while (remaining > 0.0001 && cursor > 0.0001)
            {
                var bpm = _chart.Bpm;
                var segmentStart = 0.0;
                var lookupTime = Math.Max(0.0, cursor - 0.0001);
                for (var i = 0;
                     i < _chart.BpmEvents.Count;
                     i++)
                {
                    var evt = _chart.BpmEvents[i];
                    if (evt.TimeMs >= lookupTime)
                    {
                        break;
                    }
                    bpm = evt.Bpm;
                    segmentStart = evt.TimeMs;
                }

                var beatMs = 60000.0 / Math.Max(1.0, bpm);
                var availableBeats =
                    (cursor - segmentStart) / beatMs;
                if (availableBeats >= remaining)
                {
                    duration += remaining * beatMs;
                    remaining = 0.0;
                }
                else
                {
                    duration += availableBeats * beatMs;
                    remaining -= availableBeats;
                    cursor = segmentStart;
                }
            }
            return duration;
        }

        private void LateUpdate()
        {
            if (_initialized)
            {
                _performanceMonitor.SetSceneContext(
                    _viewMode == FalsumViewMode.Orthographic2D,
                    _chart != null ? _chart.Notes.Count : 0,
                    _chart != null ? _chart.Events.Count : 0,
                    DebugGridVertexCount);
                UpdateOverlays();
                _performanceMonitor.EndFrame();
                _view.TickPerformanceWindow();
            }
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            CloseActiveToolRuntime();
            RestoreChartPreviewFraming();
            StopPackageSpectrumLoad(true);
            _waveformCache.Dispose();
            if (_world != null)
            {
                _world.Dispose();
            }
            if (_world2D != null)
            {
                _world2D.Dispose();
            }
            if (_view != null)
            {
                _view.Dispose();
            }
            if (_performanceMonitor != null)
            {
                _performanceMonitor.Dispose();
                _performanceMonitor = null;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _initialized)
            {
                if (_toolNotePickActive)
                {
                    return;
                }
                CancelCurrentAction();
            }
        }

        private bool OnWantsToQuit()
        {
            if (_allowQuit)
            {
                return true;
            }
            if (!_newChartCreateInProgress &&
                !HasUnsavedChanges)
            {
                _allowQuit = true;
                return true;
            }
            RequestExit();
            return false;
        }

        private void WireView()
        {
            _view.OpenFileRequested += ImportPackage;
            _view.NewFileRequested += BeginNewPackage;
            _view.OpenPreferencesRequested += OpenPreferences;
            _view.ChartInfoRequested += OpenChartInfo;
            _view.ChartInfoCommitRequested += CommitChartInfo;
            _view.ChartInfoCancelRequested += CloseChartInfo;
            _view.ToolManagerRequested += OpenToolManager;
            _view.ToolOpenRequested += OpenTool;
            _view.ToolReportLocateRequested +=
                LocateToolReportItem;
            _view.PlayPauseClicked += TogglePlayback;
            _view.SaveClicked += SavePackage;
            _view.SaveAsClicked += SavePackageAs;
            _view.RestoreBackupRequested += RestoreLatestBackup;
            _view.OpenBackupFolderRequested += OpenBackupFolder;
            _view.ExitRequested += RequestExit;
            _view.UndoClicked += Undo;
            _view.RedoClicked += Redo;
            _view.CopyRequested += CopySelection;
            _view.PasteRequested += PasteClipboard;
            _view.CutRequested += CutSelection;
            _view.MirrorRequested += MirrorSelection;
            _view.MirrorFrontBackRequested +=
                MirrorSelectionFrontBack;
            _view.SelectionFilterRequested += ApplySelectionFilter;
            _view.ToggleFullscreenRequested +=
                ToggleBorderlessFullscreen;
            _view.ChartPreviewRequested += EnterChartPreviewMode;
            _view.GridVisibilityRequested += SetGridVisibility;
            _view.MeasureNumberVisibilityRequested +=
                SetMeasureNumberVisibility;
            _view.TrackSpeedEffectsRequested +=
                ApplyTrackSpeedEffectsToggle;
            _view.ChartLayerVisibilityRequested +=
                SetChartLayerVisibility;
            _view.BrowseNewChartAudioRequested += BrowseNewChartAudio;
            _view.BrowseNewChartCoverRequested += BrowseNewChartCover;
            _view.NewChartCreateRequested += CreateNewPackage;
            _view.ClearHistoryClicked += delegate
            {
                if (!RequireLoadedPackage())
                {
                    return;
                }
                _session.ClearHistory();
                ClearMetadataHistorySnapshots();
                UpdateViews();
            };
            _view.SpeedChanged += SetFlowSpeed;
            _view.SpeedSubmitted += SetFlowSpeedFromText;
            _view.AudioPreprocessRequested += OpenAudioPreprocess;
            _view.BaseBpmSubmitted += SetBaseBpm;
            _view.SubdivisionSubmitted += SetSubdivision;
            _view.TimeSubmitted += SeekFromText;
            _view.TimelineChanged += SeekNormalized;
            _view.SpectrumToggleRequested += ToggleSpectrum;
            _view.RateClicked += CycleRate;
            _view.SongTitleSubmitted += SetTitle;
            _view.SongArtistSubmitted += SetArtist;
            _view.SongRatingSubmitted += SetRating;
            _view.SongChartDesignerSubmitted +=
                SetChartDesigner;
            _view.DifficultySelected += SwitchDifficulty;
            _view.PropertySubmitted += SetProperty;
            _view.JumpToNoteClicked += JumpToSelectedNote;
            _view.EventsPanelVisibilityChanged +=
                OnEventsPanelVisibilityChanged;
            _view.EventAddRequested += AddChartEvent;
            _view.EventSubmitted += SubmitChartEvent;
            _view.EventDeleteRequested += DeleteChartEvent;
            _view.EventDuplicateRequested += DuplicateChartEvent;
            _view.EventJumpRequested += JumpToChartEvent;
            WirePreferencesView();
            WireToolDialog();
            WireSkyAreaBank();
            WireAudioPreprocessView();
            WireMobileShell();
        }

        /// <summary>
        /// The mobile shell reuses the SAME entry points as the desktop
        /// chrome: a wheel sector is exactly a HandleRadialAction call, and
        /// the wheel centre is exactly the ExitPlacement case. No new
        /// placement logic exists for touch, which is why the mobile shell
        /// cannot drift from the desktop behaviour.
        /// </summary>
        private void WireMobileShell()
        {
            if (_view == null)
            {
                return;
            }
            if (_view.MobileShell == null)
            {
                return;
            }
            _view.MobilePlacementRequested += HandleRadialAction;
            _view.MobileAuditionRequested += BeginTemporaryPlayback;
            _view.MobileSegmentSelectRequested += delegate
            {
                HandleRadialAction(FalsumRadialAction.SegmentSelect);
            };
            _view.MobileExitPlacementRequested += delegate
            {
                HandleRadialAction(FalsumRadialAction.ExitPlacement);
            };
        }

        /// <summary>
        /// Applies the developer mobile-layout switch and persists it, so
        /// the layout under review survives a restart while it is being
        /// iterated on.
        /// </summary>
        private void SetMobileLayout(bool mobile)
        {
            if (_preferences == null || _preferences.Experimental == null)
            {
                return;
            }
            _preferences.Experimental.ForceMobileLayout = mobile;
            FalsumPreferencesStore.Save(_preferences);
            if (_view != null)
            {
                _view.SetMobileLayoutForPages(mobile);
            }
        }

        /// <summary>
        /// Applies and persists the phone control-size multiplier.
        /// </summary>
        private void SetMobileUiScale(float scale)
        {
            if (_preferences == null || _preferences.Experimental == null)
            {
                return;
            }
            _preferences.Experimental.MobileUiScale =
                FalsumMobileScaleBounds.Clamp(scale);
            FalsumPreferencesStore.Save(_preferences);
            if (_view != null)
            {
                _view.ApplyMobileUiScale(
                    _preferences.Experimental.MobileUiScale);
            }
        }

        /// <summary>
        /// Re-applies the persisted developer mobile-layout switch on
        /// startup. Runs after the view is built, because the shell can only
        /// mount onto an existing document.
        /// </summary>
        private void ApplyPersistedMobileLayout()
        {
            if (_view == null ||
                _preferences == null ||
                _preferences.Experimental == null ||
                !_preferences.Experimental.ForceMobileLayout)
            {
                return;
            }
            _view.SetMobileLayoutForPages(true);
            _view.ApplyMobileUiScale(
                _preferences.Experimental.MobileUiScale);
        }

        private void WireSkyAreaBank()
        {
            InitializeSkyAreaBank();
            if (_view == null)
            {
                return;
            }
            _view.SetBankPresets(_bankDocument);
            _view.SetBankPanelOpen(_bankPanelOpen);
            // The view's events are Action-typed (void), while the bank
            // operations return bool so direct calls/tests can check success;
            // adapt here rather than changing the public event shapes.
            _view.BankSaveSelectionRequested +=
                delegate(string name, IList<string> tags)
                {
                    SaveSelectionToBank(name, tags);
                };
            _view.BankPlacePresetRequested +=
                delegate(FalsumSkyAreaPreset preset)
                {
                    BeginBankPlacement(preset);
                };
            _view.BankDeletePresetRequested +=
                delegate(FalsumSkyAreaPreset preset)
                {
                    DeleteBankPreset(preset);
                };
            _view.BankRenamePresetRequested +=
                delegate(FalsumSkyAreaPreset preset, string name)
                {
                    RenameBankPreset(preset, name);
                };
            _view.BankPresetScalesRequested +=
                delegate(FalsumSkyAreaPreset preset,
                         double width,
                         double length)
                {
                    SetBankPresetScales(preset, width, length);
                };
            _view.BankCopyCodeRequested +=
                delegate(FalsumSkyAreaPreset preset)
                {
                    CopyShareCodeToClipboard(preset);
                };
            _view.BankImportCodeRequested +=
                delegate(string code)
                {
                    var preset = DecodeShareCode(code);
                    if (preset != null)
                    {
                        StoreImportedPreset(preset);
                    }
                };
            // The import dialog's file button: read the chosen .fsky TEXT and
            // hand it back to the dialog so the author sees (and can fix) the
            // code before confirming. Importing here instead would skip the
            // confirmation the dialog exists for.
            _view.BankImportFileTextRequested += delegate
            {
                var path = FalsumFileDialog.OpenSkyAreaBank();
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                try
                {
                    _view.SetBankImportCode(
                        File.ReadAllText(
                            path,
                            new UTF8Encoding(false, true)));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[Falsum] SkyArea Bank share file could not be read: " +
                        exception.Message);
                    ShowHint(L.Get("falsum.bank.error.badCode"), 1.8f);
                }
            };
            _view.BankImportFileRequested += OpenBankFolder;
            _view.BankExportFileRequested += ExportPresetToFile;
            _view.BankToggleRequested += ToggleBankPanel;
            _view.BankPanelVisibilityChanged += SetBankPanelOpen;
        }

        private void WireToolDialog()
        {
            var dialog = _view.ToolDialog;
            if (dialog == null)
            {
                return;
            }
            dialog.TimeResolver = ResolveToolTimeAtScreen;
            dialog.OpenFolderRequested +=
                delegate { _toolManager.OpenToolsFolder(); };
            dialog.ReloadRequested += ReloadTools;
            dialog.ToolEnabledChanged +=
                delegate(string id, bool enabled)
                {
                    _toolManager.SetEnabled(id, enabled);
                    _view.SetToolCatalog(_toolManager.Tools);
                };
            dialog.ToolOpenRequested += OpenTool;
            dialog.RunRequested += RunTool;
            dialog.BackRequested += OpenToolManager;
            dialog.Closed += OnToolDialogClosed;
            dialog.TimePickStarted += BeginToolTimePick;
            dialog.TimePickEnded += EndToolTimePick;
            dialog.NotePickRequested += BeginToolNotePick;
            dialog.NotePickCancelled += CancelToolNotePick;
            dialog.NotePickConfirmed += ConfirmToolNotePick;
            dialog.NotePickPointerDown +=
                HandleToolNotePickPointerDown;
            dialog.NotePickScrolled += HandleToolNotePickScroll;
        }

        private void WirePreferencesView()
        {
            var preferencesView = _view.PreferencesView;
            if (preferencesView == null)
            {
                return;
            }
            preferencesView.CommitRequested +=
                CommitPreferences;
            preferencesView.CancelRequested +=
                CancelPreferences;
            preferencesView.MobileLayoutPreviewRequested +=
                SetMobileLayout;
            preferencesView.MobileScalePreviewRequested +=
                SetMobileUiScale;
            preferencesView.BrowseCoverRequested +=
                BrowsePreferencesCover;
            preferencesView.BrowseAudioRequested +=
                BrowsePreferencesAudio;
            preferencesView.OpenBackupFolderRequested +=
                OpenBackupFolder;
            preferencesView.OpenCacheFolderRequested +=
                OpenCacheFolder;
            preferencesView.ClearCacheRequested +=
                ClearCache;
            preferencesView.RestoreSelectedBackupRequested +=
                RestoreSelectedBackup;
            preferencesView.BackgroundPreviewEntered +=
                EnterPreferencesBackgroundPreview;
            preferencesView.BackgroundPreviewExited +=
                ExitPreferencesBackgroundPreview;
            preferencesView.BackgroundPreviewChanged +=
                SetPreferencesPreviewBackground;
            preferencesView.DifficultySwitchRequested +=
                SwitchDifficultyFromPreferences;
            preferencesView.DisclaimerRequested +=
                ShowDisclaimerFromAbout;
            preferencesView.UiStylePreviewRequested +=
                PreviewUiStyle;
            preferencesView.VisualPreviewRequested +=
                PreviewVisualPreferences;
            Debug.Log("[Falsum] UI style preview event wired.");
        }

        /// <summary>
        /// Live preview for the display page's scene-opacity sliders.
        /// Applies the edited group without committing it; CancelPreferences,
        /// RevertPreferencesDisplay and the commit-failure path all re-apply
        /// the committed group afterwards.
        /// </summary>
        private void PreviewVisualPreferences(
            FalsumVisualPreferences visual)
        {
            if (_host == null || visual == null)
            {
                return;
            }
            _host.ApplyVisualPreferences(visual);
        }

        /// <summary>
        /// Opens the chart info panel for the current package. This replaces
        /// the former "click the cover to open preferences" path: chart
        /// metadata is document scope, so it gets its own panel.
        /// </summary>
        private void OpenChartInfo()
        {
            if (_view == null ||
                _view.IsPreferencesOpen ||
                _view.IsChartInfoOpen ||
                _view.IsModalOpen)
            {
                return;
            }
            PauseForEditInteraction();
            _backgroundBeforePreferences =
                _manifest != null
                    ? _manifest.EffectiveBackgroundFile
                    : string.Empty;
            _view.ShowChartInfo(
                new FalsumChartMetadataDraft(
                    _package,
                    _package != null),
                _package);
        }

        private void CloseChartInfo()
        {
            if (_view != null)
            {
                _view.CloseChartInfo();
            }
            if (!string.IsNullOrWhiteSpace(_backgroundBeforePreferences))
            {
                _host.SetGameplayBackground(
                    _backgroundBeforePreferences);
            }
            ClearPendingPreferences();
            RefreshSongInfoCard(true);
            RefreshEditorVisuals();
        }

        /// <summary>
        /// Applies the chart info panel's edits to the loaded package.
        ///
        /// This is the document-scope commit; it deliberately does not save
        /// the package to disk (the user still presses Save) and does not
        /// touch global preferences. On failure the previous metadata state is
        /// restored from a snapshot, mirroring the preferences-page path.
        /// </summary>
        private void CommitChartInfo(
            FalsumChartMetadataDraft metadata,
            FalsumPackage package)
        {
            if (metadata == null ||
                package == null ||
                _package == null ||
                _package != package ||
                _view == null ||
                !_view.IsChartInfoOpen)
            {
                return;
            }
            if (!metadata.HasPackage)
            {
                _view.CloseChartInfo();
                return;
            }
            if (!metadata.HasChangesFrom(package))
            {
                _view.CloseChartInfo();
                return;
            }

            string error;
            if (!metadata.TryValidate(out error))
            {
                _view.PreferencesView.ShowChartInfoError(error);
                return;
            }

            var snapshot = FalsumMetadataApplyService.Capture(package);
            try
            {
                if (!FalsumMetadataApplyService.TryApply(
                        package,
                        metadata,
                        out error))
                {
                    _view.PreferencesView.ShowChartInfoError(
                        L.Get(
                            "falsum.error.metadata.applyFailed",
                            ("error", error)));
                    return;
                }
                _manifest = package.Manifest;
                _chart.SongId = _manifest.SongId;
                _host.SetGameplayBackground(
                    _manifest.EffectiveBackgroundFile);
                RefreshSongInfoCard(true);
                RefreshEditorVisuals();
                UpdateViews();
                MarkDirty();
                if (metadata.HasPendingAudio)
                {
                    BeginPackageAudioLoad(package, _playheadMs);
                }
                metadata.DisposeTemporaryFiles();
                _view.CloseChartInfo();
                ShowHint(
                    L.Get("falsum.status.chartInfoApplied"),
                    2.5f);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                try
                {
                    FalsumMetadataApplyService.Restore(
                        package,
                        snapshot);
                    _manifest = package.Manifest;
                }
                catch (Exception restoreException)
                {
                    Debug.LogException(restoreException);
                }
                _view.PreferencesView.ShowChartInfoError(
                    L.Get(
                        "falsum.error.metadata.applyFailed",
                        ("error", exception.Message)));
            }
        }

        private void PreviewUiStyle(FalsumUiStyle style)
        {
            if (_view != null && _view.IsPreferencesOpen)
            {
                _view.SetUiStyle(style);
            }
            Debug.Log("[Falsum] UI style applied: " + style + " prefsOpen=" + (_view != null && _view.IsPreferencesOpen));
        }

        private void ShowFirstRunDisclaimerIfNeeded()
        {
            if (_view == null ||
                FalsumDisclaimerConsent.HasAccepted)
            {
                return;
            }
            _view.ShowDisclaimer(
                true,
                FalsumDisclaimerConsent.Accept,
                FalsumPlatform.Quit);
        }

        private void ShowDisclaimerFromAbout()
        {
            if (_view != null)
            {
                _view.ShowDisclaimer(
                    false,
                    null,
                    null);
            }
        }

        private void SwitchDifficultyFromPreferences(int difficulty)
        {
            SwitchDifficulty(difficulty);
            // The chart info panel is the only place a difficulty can be
            // switched from now; it rebuilds itself from the new package state.
            if (_view != null && _view.IsChartInfoOpen)
            {
                _view.RefreshChartInfoPanel();
            }
        }

        private static void ClearChartForEmptyEditor(Chart chart)
        {
            if (chart == null)
            {
                return;
            }
            chart.Bpm = 140.0;
            chart.Division = 4.0;
            chart.DelayMs = 0.0;
            chart.SongId = string.Empty;
            chart.Difficulty = 0;
            chart.SourcePath = string.Empty;
            chart.Notes.Clear();
            chart.Events.Clear();
            chart.SpeedEvents.Clear();
            chart.BpmEvents.Clear();
            chart.LaneEvents.Clear();
            chart.MeasureLineTimes.Clear();
            chart.RebuildTimeline();
        }

        private bool RequireLoadedPackage()
        {
            if (_package != null)
            {
                return true;
            }
            ShowHint(NoChartPrompt, 2.5f);
            RefreshSongInfoCard(false);
            return false;
        }

        private void InitializeManifest()
        {
            var duration = _host.SongAudio != null &&
                           _host.SongAudio.clip != null
                ? Mathf.RoundToInt(
                    _host.SongAudio.clip.length * 1000.0f)
                : 0;
            _manifest = FalsumManifest.CreateTemplate(
                string.IsNullOrWhiteSpace(_chart.SongId)
                    ? "untitled"
                    : _chart.SongId,
                "a la mode",
                "aran",
                duration);
            _currentDifficulty = _chart.Difficulty >= 4
                ? 3
                : Mathf.Clamp(_chart.Difficulty, 0, 3);
            for (var i = 0; i < _manifest.Difficulties.Count; i++)
            {
                _manifest.Difficulties[i].SetAvailable(
                    _manifest.Difficulties[i].Index == _currentDifficulty);
            }
            var info = _manifest.FindDifficulty(_currentDifficulty);
            if (info != null)
            {
                info.SetRating(
                    _currentDifficulty == 3 ? 11 : _currentDifficulty + 1);
            }
            ConfigureMetadataHistory();
        }

        private void ConfigureMetadataHistory()
        {
            _session.SetExternalStateProvider(
                CaptureMetadataHistoryState,
                delegate(string key)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        return;
                    }
                    try
                    {
                        FalsumChartMetadataSnapshot snapshot;
                        if (_package == null ||
                            !_metadataHistorySnapshots.TryGetValue(
                                key,
                                out snapshot))
                        {
                            throw new InvalidDataException(
                                L.Get(
                                    "falsum.error.metadata.historySnapshotMissing"));
                        }
                        FalsumMetadataApplyService.Restore(
                            _package,
                            snapshot);
                        _manifest = _package.Manifest;
                        _host.SetGameplayBackground(
                            _manifest.EffectiveBackgroundFile);
                        RefreshSongInfoCard(false);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                });
        }

        private string CaptureMetadataHistoryState()
        {
            if (_package == null)
            {
                return string.Empty;
            }
            var snapshot =
                FalsumMetadataApplyService.Capture(_package);
            var key = snapshot.CreateHistoryKey();
            if (_metadataHistorySnapshots.ContainsKey(key))
            {
                return key;
            }
            _metadataHistorySnapshots[key] = snapshot;
            _metadataHistoryOrder.Enqueue(key);
            while (_metadataHistoryOrder.Count >
                   MaxMetadataHistorySnapshots)
            {
                var expired = _metadataHistoryOrder.Dequeue();
                _metadataHistorySnapshots.Remove(expired);
            }
            return key;
        }

        private void ClearMetadataHistorySnapshots()
        {
            _metadataHistorySnapshots.Clear();
            _metadataHistoryOrder.Clear();
        }

        private void BeginNewPackage()
        {
            PauseForEditInteraction();
            ConfirmDiscardOrSave(ShowNewPackageDialog);
        }

        private void ShowNewPackageDialog()
        {
            var path = FalsumFileDialog.SaveFalsum("untitled.falsum");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            _view.ShowNewChartDialog(
                FalsumFileDialog.EnsureFalsumExtension(path));
        }

        private void BrowseNewChartAudio()
        {
            var path = FalsumFileDialog.OpenAudio();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _view.SetNewChartAudio(path);
            }
        }

        private void BrowseNewChartCover()
        {
            var path = FalsumFileDialog.OpenImage();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _view.SetNewChartCover(path);
            }
        }

        private void CreateNewPackage(FalsumNewChartRequest request)
        {
            if (request == null || _newChartCreateInProgress)
            {
                return;
            }
            StartCoroutine(CreateNewPackageRoutine(request));
        }

        private IEnumerator CreateNewPackageRoutine(
            FalsumNewChartRequest request)
        {
            _newChartCreateInProgress = true;
            _view.SetNewChartBusy(
                true,
                L.Get("falsum.status.convertingAudio"));
            Task<FalsumConvertedAudio> audioTask;
            try
            {
                var audioJob =
                    FalsumMediaConverter.PrepareAudioConversion(
                        request.AudioPath);
                audioTask = Task.Run(
                    () => FalsumMediaConverter.ConvertAudio(
                        audioJob));
            }
            catch (Exception exception)
            {
                FailNewChartCreation(exception);
                yield break;
            }
            while (!audioTask.IsCompleted)
            {
                yield return null;
            }
            FalsumConvertedAudio audio;
            try
            {
                if (audioTask.IsFaulted)
                {
                    throw audioTask.Exception.GetBaseException();
                }
                audio = audioTask.Result;
            }
            catch (Exception exception)
            {
                FailNewChartCreation(exception);
                yield break;
            }

            _view.SetNewChartBusy(
                true,
                L.Get("falsum.status.convertingCover"));
            yield return null;
            FalsumConvertedCover cover = null;
            if (!string.IsNullOrWhiteSpace(request.CoverPath))
            {
                try
                {
                    cover = FalsumMediaConverter.ConvertCover(
                        request.CoverPath);
                }
                catch (Exception exception)
                {
                    FailNewChartCreation(exception);
                    yield break;
                }
            }
            yield return null;
            try
            {
                _view.SetNewChartBusy(
                    true,
                    L.Get("falsum.status.writingPackage"));
                var package = FalsumPackage.CreateNew(
                    request,
                    audio.Bytes,
                    audio.DurationMs,
                    cover != null ? cover.Bytes : null,
                    cover != null ? cover.Width : 0,
                    cover != null ? cover.Height : 0);
                package.Save(request.TargetPath);
                LoadPackage(request.TargetPath, false);
                _view.CloseNewChartDialog();
                _newChartCreateInProgress = false;
                _view.SetNewChartBusy(false, null);
                // A new package is created FOR audio preprocessing, so open
                // that window for it instead of leaving the author on an
                // empty timeline wondering what to do next. Deferred by a
                // tick: the dialog above is still fading out (and is still
                // the view's modal) and the audio is still decoding.
                ScheduleAudioPreprocessAfterCreate();
                ShowHint(
                    L.Get(
                        "falsum.status.packageCreated",
                        ("file", Path.GetFileName(
                            request.TargetPath)),
                        ("difficulty", DifficultyName(
                            _currentDifficulty))),
                    4.0f);
            }
            catch (Exception exception)
            {
                FailNewChartCreation(exception);
            }
        }

        private void FailNewChartCreation(Exception exception)
        {
            Debug.LogException(exception);
            _newChartCreateInProgress = false;
            _view.SetNewChartError(
                L.Get(
                    "falsum.error.newChart.createFailed",
                    ("error", exception.Message)));
            ShowHint(
                L.Get(
                    "falsum.error.newChart.createFailed",
                    ("error", exception.Message)),
                5.0f);
        }

        private void ImportPackage()
        {
            PauseForEditInteraction();
            ConfirmDiscardOrSave(ShowImportPackageDialog);
        }

        private void ShowImportPackageDialog()
        {
            var path = FalsumFileDialog.OpenFalsum();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            try
            {
                LoadPackage(path, true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowHint(
                    L.Get(
                        "falsum.error.package.importFailed",
                        ("error", exception.Message)),
                    5.0f);
            }
        }

        private void ConfirmDiscardOrSave(
            Action continueAction)
        {
            if (continueAction == null)
            {
                return;
            }
            if (!HasUnsavedChanges)
            {
                continueAction();
                return;
            }
            _view.ShowConfirm(
                L.Get("falsum.confirm.unsavedChangesContinue"),
                L.Get("falsum.common.cancel"),
                L.Get("falsum.action.saveAndContinue"),
                delegate
                {
                    if (SavePackageInternal(false))
                    {
                        continueAction();
                    }
                },
                L.Get("falsum.confirm.doNotSaveAction"),
                continueAction);
        }

        private void LoadPackage(
            string path,
            bool announce,
            string sourcePathOverride = null,
            int? preferredDifficulty = null)
        {
            var package = FalsumPackage.Load(
                path,
                sourcePathOverride);
            var difficulty =
                preferredDifficulty.HasValue &&
                package.Charts.ContainsKey(
                    preferredDifficulty.Value)
                    ? preferredDifficulty.Value
                    : package.HighestAvailableDifficulty();
            var loadedChart = package.LoadChart(difficulty);
            _package = package;
            _manifest = package.Manifest;
            _host.SetGameplayBackground(
                _manifest.EffectiveBackgroundFile);
            ClearMetadataHistorySnapshots();
            _session.Replace(loadedChart, loadedChart.DelayMs);
            _currentDifficulty = difficulty;
            _persistentHint = BuildPlaybackHint();
            RefreshSongInfoCard(true);
            _playheadMs = 0.0;
            _selected.Clear();
            RefreshEditorVisuals();
            Seek(0.0);
            _isDirty = false;
            _nextAutoBackupTime =
                Time.unscaledTimeAsDouble +
                _autoBackupIntervalSeconds;
            BeginPackageAudioLoad(package, null);
            ScheduleAudioPreprocessPromptIfNeeded(loadedChart);
            if (announce)
            {
                ShowHint(
                    L.Get(
                        "falsum.status.packageImported",
                        ("title", _manifest.Title),
                        ("difficulty", DifficultyName(
                            _currentDifficulty))),
                    3.0f);
            }
        }

        private void BeginPackageAudioLoad(
            FalsumPackage package,
            double? restoreChartTimeMs)
        {
            _packageLoadGeneration++;
            StopPackageSpectrumLoad(true);
            // The floor waveform starts disabled: nothing is generated
            // until the user turns it on.
            _spectrumVisible = false;
            _spectrumAvailable = false;
            _spectrumPending = false;
            _packageAudioPath = null;
            _packageAudioHash = null;
            _packageAudioDurationMs = 0;
            _waveformCache.Clear();
            RefreshSpectrumUi();
            if (_packageAudioLoad != null)
            {
                StopCoroutine(_packageAudioLoad);
                _packageAudioLoad = null;
            }
            _packageAudioLoad = StartCoroutine(
                LoadPackageAudio(
                    package,
                    _packageLoadGeneration,
                    restoreChartTimeMs));
        }

        private IEnumerator LoadPackageAudio(
            FalsumPackage package,
            int generation,
            double? restoreChartTimeMs = null)
        {
            if (package == null || package.AudioBytes == null)
            {
                yield break;
            }
            var cacheDirectory = FalsumCacheStore.DirectoryPath;
            Directory.CreateDirectory(cacheDirectory);
            var audioHash =
                ComputeSha256(package.AudioBytes);
            var cacheName =
                audioHash + ".ogg";
            var path = Path.Combine(
                cacheDirectory,
                cacheName);
            var cacheRoot = Path.GetFullPath(cacheDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var fullCachePath = Path.GetFullPath(path);
            if (!fullCachePath.StartsWith(
                    cacheRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.audioCachePathEscaped"));
            }
            File.WriteAllBytes(path, package.AudioBytes);
            Debug.Log("[Falsum] Package audio cache write: " + path);
            _spectrumAvailable = true;
            _spectrumPending = false;
            _packageAudioPath = path;
            _packageAudioHash = audioHash;
            _packageAudioDurationMs = package.Manifest.AudioDurationMs;
            if (_spectrumVisible)
            {
                BeginPackageSpectrumLoad(
                    path,
                    audioHash,
                    package.Manifest.AudioDurationMs,
                    generation);
            }
            RefreshSpectrumUi();
            var uri = new Uri(path).AbsoluteUri;
            using (var request =
                   UnityWebRequestMultimedia.GetAudioClip(
                       uri,
                       AudioType.OGGVORBIS))
            {
                yield return request.SendWebRequest();
                if (generation != _packageLoadGeneration)
                {
                    yield break;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning(
                        "[Falsum] Audio load failed: " +
                        request.error);
                    if (generation == _packageLoadGeneration)
                    {
                        _spectrumVisible = false;
                        _spectrumPending = false;
                        _spectrumAvailable = false;
                        _waveformCache.Clear();
                        if (_world != null)
                        {
                            _world.ClearSpectrumTexture();
                        }
                        RefreshSpectrumUi();
                    }
                    ShowHint(
                        L.Get(
                            "falsum.error.audio.loadFailed",
                            ("error", request.error)),
                        5.0f);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    ShowHint(
                        L.Get(
                            "falsum.error.audio.decodeFailed"),
                        5.0f);
                    yield break;
                }
                if (generation != _packageLoadGeneration)
                {
                    yield break;
                }
                clip.name = package.Manifest.SongId;
                package.SetAudio(
                    package.AudioBytes,
                    Mathf.RoundToInt(clip.length * 1000.0f));
                // Trust the decoded clip over the manifest: a manifest
                // duration that overshoots the real audio makes the last
                // segment empty and kills the whole waveform.
                _packageAudioDurationMs =
                    Mathf.RoundToInt(clip.length * 1000.0f);
                _host.SetSongClip(
                    clip,
                    package.Manifest.Title,
                    DifficultyName(_currentDifficulty),
                    path);
                RebuildEditorTimeline();
                if (restoreChartTimeMs.HasValue)
                {
                    Seek(Math.Min(
                        restoreChartTimeMs.Value,
                        clip.length * 1000.0));
                }
                else
                {
                    RefreshEditorAtChartTime(_playheadMs);
                }
                UpdateViews();
                _packageAudioLoad = null;
            }
        }

        private void BeginPackageSpectrumLoad(
            string audioPath,
            string audioHash,
            int audioDurationMs,
            int generation)
        {
            if (_packageSpectrumLoad != null)
            {
                StopCoroutine(_packageSpectrumLoad);
                _packageSpectrumLoad = null;
            }
            if (_spectrumCancellation != null)
            {
                _spectrumCancellation.Cancel();
                _spectrumCancellation.Dispose();
                _spectrumCancellation = null;
            }
            _packageSpectrumLoad = StartCoroutine(
                LoadPackageSpectrum(
                    audioPath,
                    audioHash,
                    audioDurationMs,
                    generation));
        }

        private IEnumerator LoadPackageSpectrum(
            string audioPath,
            string audioHash,
            int audioDurationMs,
            int generation)
        {
            var durationMs = Math.Max(0.0, audioDurationMs);
            var segmentCount =
                FalsumTrackSpectrumCache.GetSegmentCount(durationMs);
            if (segmentCount <= 0)
            {
                FailSpectrumLoad(null);
                yield break;
            }

            if (_world == null ||
                generation != _packageLoadGeneration)
            {
                _packageSpectrumLoad = null;
                yield break;
            }
            _spectrumAvailable = false;
            _spectrumPending = true;
            RefreshSpectrumUi();

            var failedSegments = new HashSet<int>();
            var displayedSegment = -1;
            while (generation == _packageLoadGeneration &&
                   _world != null)
            {
                var visibleSegment =
                    SpectrumSegmentForChartTime(
                        _playheadMs,
                        durationMs,
                        segmentCount);

                if (displayedSegment != visibleSegment)
                {
                    var currentJob =
                        FalsumTrackSpectrumCache.PrepareSegment(
                            audioPath,
                            audioHash,
                            durationMs,
                            visibleSegment);
                    if (currentJob == null)
                    {
                        FailSpectrumLoad(null);
                        yield break;
                    }

                    var currentKey = FalsumTrackSpectrumCache.CacheKey(
                        audioHash,
                        visibleSegment,
                        currentJob.TextureWidth,
                        currentJob.TextureHeight);
                    var currentTexture =
                        _waveformCache.Get(currentKey);
                    Exception currentFailure = null;
                    if (currentTexture == null)
                    {
                        yield return GenerateSpectrumJob(
                            currentJob,
                            currentKey,
                            generation,
                            delegate(Exception exception)
                            {
                                currentFailure = exception;
                            });
                        currentTexture = _waveformCache.Get(currentKey);
                    }
                    if (generation != _packageLoadGeneration ||
                        _world == null)
                    {
                        _packageSpectrumLoad = null;
                        yield break;
                    }

                    if (currentTexture == null)
                    {
                        FailSpectrumLoad(currentFailure);
                        yield break;
                    }
                    // Keep this segment resident: the prefetch below may
                    // otherwise evict it and blank the track.
                    _waveformCache.Pin(currentKey);
                    _world.SetSpectrumTexture(
                        currentTexture,
                        currentJob.SegmentStartMs,
                        currentJob.SegmentDurationMs);
                    displayedSegment = visibleSegment;
                    failedSegments.Remove(visibleSegment);
                    var firstDisplay =
                        !_spectrumAvailable;
                    _spectrumAvailable = true;
                    _spectrumPending = false;
                    RefreshSpectrumUi();
                    if (firstDisplay && _spectrumVisible)
                    {
                        ShowHint(
                            L.Get(
                                "falsum.hint.spectrum.currentRangeLoaded"),
                            2.5f);
                    }
                    yield return null;
                    continue;
                }

                var last = Math.Min(
                    segmentCount - 1,
                    visibleSegment + SpectrumSegmentLookahead);
                var candidateKey = -1;
                var candidateIndex = -1;
                FalsumTrackSpectrumJob candidateJob = null;
                for (var index = visibleSegment;
                     index <= last;
                     index++)
                {
                    if (failedSegments.Contains(index))
                    {
                        continue;
                    }
                    var probe = FalsumTrackSpectrumCache.PrepareSegment(
                        audioPath,
                        audioHash,
                        durationMs,
                        index);
                    if (probe == null)
                    {
                        failedSegments.Add(index);
                        continue;
                    }
                    var key = FalsumTrackSpectrumCache.CacheKey(
                        audioHash,
                        index,
                        probe.TextureWidth,
                        probe.TextureHeight);
                    if (_waveformCache.Get(key) != null)
                    {
                        // Already resident in memory: nothing left to
                        // prefetch below.
                        continue;
                    }
                    candidateKey = key;
                    candidateIndex = index;
                    candidateJob = probe;
                    break;
                }
                if (candidateJob == null)
                {
                    yield return null;
                    continue;
                }

                Exception failure = null;
                yield return GenerateSpectrumJob(
                    candidateJob,
                    candidateKey,
                    generation,
                    delegate(Exception exception)
                    {
                        failure = exception;
                    });
                if (generation != _packageLoadGeneration ||
                    _world == null)
                {
                    _packageSpectrumLoad = null;
                    yield break;
                }
                if (failure != null)
                {
                    failedSegments.Add(candidateIndex);
                    if (!(failure is OperationCanceledException))
                    {
                        Debug.LogWarning(
                            "[Falsum] Waveform segment " +
                            candidateIndex +
                            " failed: " +
                            failure.Message);
                    }
                    continue;
                }

                yield return null;
            }
            _packageSpectrumLoad = null;
        }

        private int SpectrumSegmentForChartTime(
            double chartTimeMs,
            double audioDurationMs,
            int segmentCount)
        {
            var musicTimeMs = Math.Max(
                0.0,
                chartTimeMs + _session.DelayMs);
            var index = (int)Math.Floor(
                musicTimeMs /
                FalsumTrackSpectrumCache.SegmentStrideMs);
            return Mathf.Clamp(index, 0, segmentCount - 1);
        }

        private IEnumerator GenerateSpectrumJob(
            FalsumTrackSpectrumJob job,
            int cacheKey,
            int generation,
            Action<Exception> completed)
        {
            CancellationTokenSource cancellation = null;
            Task<FalsumTrackSpectrumFrame> task = null;
            Exception failure = null;
            try
            {
                cancellation = new CancellationTokenSource();
                _spectrumCancellation = cancellation;
                var token = cancellation.Token;
                task = Task.Run(
                    delegate
                    {
                        var frame = new FalsumTrackSpectrumFrame();
                        FalsumTrackSpectrumCache.Generate(
                            job,
                            frame,
                            token);
                        return frame;
                    },
                    token);
                task.ContinueWith(
                    delegate(Task failed)
                    {
                        var ignored = failed.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (task != null)
            {
                while (!task.IsCompleted)
                {
                    if (generation != _packageLoadGeneration)
                    {
                        ReleaseSpectrumCancellation(cancellation);
                        yield break;
                    }
                    yield return null;
                }
                if (task.IsFaulted)
                {
                    failure = task.Exception.GetBaseException();
                }
                else if (task.IsCanceled)
                {
                    failure = new OperationCanceledException();
                }
                else if (failure == null)
                {
                    // Upload the frame on the main thread: texture
                    // creation is not allowed on the worker.
                    try
                    {
                        var frame = task.Result;
                        var texture =
                            FalsumTrackSpectrumCache.CreateTexture(
                                frame);
                        if (texture == null)
                        {
                            throw new InvalidDataException(
                                L.Get(
                                    "falsum.error.spectrum.segmentNotProduced"));
                        }
                        _waveformCache.Store(cacheKey, texture);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }
            }

            ReleaseSpectrumCancellation(cancellation);
            if (generation != _packageLoadGeneration)
            {
                yield break;
            }
            if (completed != null)
            {
                completed(failure);
            }
        }

        private void FailSpectrumLoad(Exception failure)
        {
            if (!(failure is OperationCanceledException))
            {
                Debug.LogWarning(
                    "[Falsum] Track spectrum generation failed: " +
                    (failure != null
                        ? failure.Message
                        : L.Get("falsum.error.unknown")));
                ShowHint(
                    L.Get(
                        "falsum.hint.spectrum.currentRangeFailed"),
                    4.0f);
            }
            _spectrumPending = false;
            _spectrumVisible = false;
            _spectrumAvailable = false;
            if (_world != null)
            {
                _world.ClearSpectrumTexture();
            }
            _waveformCache.Clear();
            RefreshSpectrumUi();
            _packageSpectrumLoad = null;
        }

        private void ReleaseSpectrumCancellation(
            CancellationTokenSource cancellation)
        {
            if (cancellation == null)
            {
                return;
            }
            if (ReferenceEquals(
                    _spectrumCancellation,
                    cancellation))
            {
                _spectrumCancellation = null;
            }
            cancellation.Dispose();
        }

        private void StopPackageSpectrumLoad(
            bool clearTexture)
        {
            if (_packageSpectrumLoad != null)
            {
                StopCoroutine(_packageSpectrumLoad);
                _packageSpectrumLoad = null;
            }
            if (_spectrumCancellation != null)
            {
                _spectrumCancellation.Cancel();
                _spectrumCancellation.Dispose();
                _spectrumCancellation = null;
            }
            if (!clearTexture)
            {
                return;
            }
            _spectrumAvailable = false;
            _spectrumPending = false;
            if (_world != null)
            {
                _world.ClearSpectrumTexture();
            }
            _waveformCache.Clear();
            RefreshSpectrumUi();
        }

        private void ToggleSpectrum()
        {
            if (!FalsumSpectrumFeature.Available)
            {
                // The button is hidden while the feature is gated off.
                return;
            }
            if (!_spectrumVisible && !HasSpectrumSource())
            {
                // No audio has been resolved yet, so there is nothing to
                // build a waveform from. Say so instead of starting a
                // generation job that can only fail.
                ShowHint(
                    L.Get("falsum.hint.spectrum.noneAvailable"),
                    2.0f);
                return;
            }
            _spectrumVisible = !_spectrumVisible;
            if (_spectrumVisible)
            {
                BeginPackageSpectrumLoad(
                    _packageAudioPath,
                    _packageAudioHash,
                    _packageAudioDurationMs,
                    _packageLoadGeneration);
            }
            else
            {
                StopPackageSpectrumLoad(false);
                if (_world != null)
                {
                    _world.ClearSpectrumTexture();
                }
            }
            RefreshSpectrumUi();
            ShowHint(
                _spectrumVisible
                    ? L.Get("falsum.hint.spectrum.enabled")
                    : L.Get("falsum.hint.spectrum.disabled"),
                1.2f);
            UpdateViews();
        }

        private bool HasSpectrumSource()
        {
            return !string.IsNullOrEmpty(_packageAudioPath) &&
                   File.Exists(_packageAudioPath) &&
                   !string.IsNullOrEmpty(_packageAudioHash) &&
                   _packageAudioDurationMs > 0;
        }

        private void RefreshSpectrumUi()
        {
            if (_view == null)
            {
                return;
            }
            // Availability follows the resolved audio source, not the
            // generation coroutine: the waveform can be built on demand
            // and never starts on its own.
            var available =
                _spectrumAvailable ||
                _spectrumPending ||
                HasSpectrumSource();
            _view.SetSpectrumState(
                _spectrumVisible,
                available,
                _spectrumPending);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(
                    hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(
                        hash[i].ToString(
                            "x2",
                            CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private void SavePackage()
        {
            SavePackageInternal(false);
        }

        private void SavePackageAs()
        {
            SavePackageInternal(true);
        }

        private bool SavePackageInternal(bool saveAs)
        {
            if (!RequireLoadedPackage())
            {
                return false;
            }
            PauseForEditInteraction();
            try
            {
                var targetPath = _package.SourcePath;
                if (saveAs ||
                    string.IsNullOrWhiteSpace(targetPath))
                {
                    targetPath = FalsumFileDialog.SaveFalsum(
                        Path.GetFileName(
                            string.IsNullOrWhiteSpace(targetPath)
                                ? _manifest.SongId + ".falsum"
                                : targetPath));
                    if (string.IsNullOrWhiteSpace(targetPath))
                    {
                        return false;
                    }
                    targetPath =
                        FalsumFileDialog.EnsureFalsumExtension(
                            targetPath);
                }
                var text = ChartWriter.Write(
                    _session.Chart,
                    _session.DelayMs,
                    _manifest.SongId,
                    _currentDifficulty);
                UpdateDifficultyAvailability(
                    _package,
                    _currentDifficulty,
                    text);
                _package.SetChartText(_currentDifficulty, text);
                if (_host.SongAudio != null &&
                    _host.SongAudio.clip != null)
                {
                    _package.SetAudio(
                        _package.AudioBytes,
                        Mathf.RoundToInt(
                            _host.SongAudio.clip.length * 1000.0f));
                }
                _package.Save(targetPath);
                RefreshSongInfoCard(false);
                _isDirty = false;
                _nextAutoBackupTime =
                    Time.unscaledTimeAsDouble +
                    _autoBackupIntervalSeconds;
                ShowHint(
                    L.Get(
                        "falsum.status.saved",
                        ("file", Path.GetFileName(
                            _package.SourcePath))),
                    3.0f);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowHint(
                    L.Get(
                        "falsum.error.saveFailed",
                        ("error", exception.Message)),
                    5.0f);
                return false;
            }
        }

        private bool UpdateDifficultyAvailability(
            FalsumPackage package,
            int currentDifficulty,
            string currentChartText,
            bool requireAtLeastOne = true)
        {
            var enabled = new bool[4];
            var enabledCount = 0;
            for (var i = 0; i < enabled.Length; i++)
            {
                var text = i == currentDifficulty
                    ? currentChartText
                    : package.LoadChartText(i);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                var result = ChartParser.Parse(
                    text.Replace("\r\n", "\n").Split('\n'),
                    "<falsum-save-availability>",
                    i);
                if (result.Success && result.Chart.Notes.Count > 0)
                {
                    enabled[i] = true;
                    enabledCount++;
                }
            }
            if (enabledCount == 0 && requireAtLeastOne)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.package.requiresNotes"));
            }
            var changed = false;
            for (var i = 0; i < enabled.Length; i++)
            {
                var difficulty = package.Manifest.FindDifficulty(i);
                if (difficulty != null)
                {
                    if (difficulty.Available != enabled[i])
                    {
                        changed = true;
                    }
                    difficulty.SetAvailable(enabled[i]);
                }
            }
            return changed;
        }

        private void MarkDirty()
        {
            _isDirty = true;
        }

        private void OnSessionChanged()
        {
            MarkDirty();
            // GroupId and the BPM event ordering are only settled inside
            // CommitChange/NotifyChanged. Both consumers read them lazily on
            // the next refresh, so mark the grouping stale and repaint the
            // combo once, here, instead of trusting every edit path to do it.
            _skyGroupingDirty = true;
            if (_host != null)
            {
                _host.RefreshComboAt(_playheadMs);
            }
            if (_view != null && _view.IsEventsPanelOpen)
            {
                RefreshEventsPanel();
            }
        }

        private void FlushSkyGroupingDirty()
        {
            if (!_skyGroupingDirty)
            {
                return;
            }
            _skyGroupingDirty = false;
            if (_host == null || _host.NoteVisuals == null)
            {
                return;
            }
            _host.NoteVisuals.RebuildSkyAreas();
            // RefreshPresentationAt only drives the note renderer; the editor
            // flush happens after the edit path's own presentation refresh,
            // so ask the host for one more refresh instead of rebuilding the
            // playback frame (audio/engine) twice per edit.
            _host.RefreshSkyPresentation(_playheadMs);
        }

        private void TickAutoBackup()
        {
            CompleteAutoBackupIfFinished();
            if (!_autoBackupEnabled ||
                _newChartCreateInProgress ||
                _autoBackupTask != null)
            {
                return;
            }
            var now = Time.unscaledTimeAsDouble;
            if (now < _nextAutoBackupTime)
            {
                return;
            }
            _nextAutoBackupTime =
                now + _autoBackupIntervalSeconds;
            if (!_isDirty ||
                _package == null ||
                string.IsNullOrWhiteSpace(_package.SourcePath))
            {
                return;
            }
            try
            {
                var chartText = ChartWriter.Write(
                    _session.Chart,
                    _session.DelayMs,
                    _manifest.SongId,
                    _currentDifficulty);
                var backupDirectory =
                    FalsumBackupService.BackupDirectory(
                    _package.SourcePath,
                    _manifest.SongId,
                    _currentDifficulty);
                var retentionCount = _backupRetentionCount;
                _autoBackupTask = Task.Run(
                    delegate
                    {
                        var backup =
                            FalsumBackupService
                                .CreateChartBackupInDirectory(
                                    backupDirectory,
                                    chartText);
                        FalsumBackupService.PruneDirectory(
                            backupDirectory,
                            retentionCount);
                        return backup;
                    });
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Automatic backup failed: " +
                    exception.Message);
            }
        }

        private void CompleteAutoBackupIfFinished()
        {
            if (_autoBackupTask == null ||
                !_autoBackupTask.IsCompleted)
            {
                return;
            }
            var task = _autoBackupTask;
            _autoBackupTask = null;
            try
            {
                if (task.IsFaulted)
                {
                    throw task.Exception.GetBaseException();
                }
                ShowHint(
                    L.Get(
                        "falsum.status.autoBackupCreated",
                        ("file", Path.GetFileName(task.Result))),
                    2.0f);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Automatic backup failed: " +
                    exception.Message);
            }
        }

        private void RestoreLatestBackup()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_package == null ||
                string.IsNullOrWhiteSpace(_package.SourcePath))
            {
                ShowHint(
                    L.Get("falsum.hint.backup.noPackage"),
                    3.0f);
                return;
            }
            var sourcePath = _package.SourcePath;
            var difficulty = _currentDifficulty;
            var backup = FalsumBackupService.FindLatest(
                sourcePath,
                _manifest.SongId,
                difficulty);
            if (string.IsNullOrWhiteSpace(backup))
            {
                ShowHint(
                    L.Get(
                        "falsum.hint.backup.notFound",
                        ("difficulty", DifficultyName(difficulty))),
                    3.5f);
                return;
            }
            _view.ShowConfirm(
                L.Get(
                    "falsum.confirm.restoreLatestBackup",
                    ("difficulty", DifficultyName(difficulty)),
                    ("file", Path.GetFileName(backup))),
                L.Get("falsum.common.cancel"),
                L.Get("falsum.action.restore"),
                delegate
                {
                    RestoreBackup(
                        backup,
                        difficulty);
                },
                null,
                null);
        }

        private void RestoreSelectedBackup(
            FalsumBackupEntry backup)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (backup == null ||
                _package == null ||
                string.IsNullOrWhiteSpace(_package.SourcePath))
            {
                return;
            }
            var difficultyName = DifficultyName(backup.Difficulty);
            var fileName = Path.GetFileName(backup.FilePath);
            _view.ShowConfirm(
                L.Get(
                    "falsum.confirm.restoreSelectedBackup",
                    ("difficulty", difficultyName),
                    ("file", fileName)),
                L.Get("falsum.common.cancel"),
                L.Get("falsum.preferences.backup.restoreAndWrite"),
                delegate
                {
                    if (!RestoreBackup(
                            backup.FilePath,
                            backup.Difficulty,
                            true))
                    {
                        if (_view != null &&
                            _view.IsPreferencesOpen &&
                            _view.PreferencesView != null)
                        {
                            _view.PreferencesView.ShowError(
                                L.Get(
                                    "falsum.error.backup.restoreInvalid"));
                        }
                        return;
                    }
                    if (_view != null &&
                        _view.IsPreferencesOpen &&
                        _view.PreferencesView != null)
                    {
                        _view.PreferencesView.ShowInfo(
                            L.Get(
                                "falsum.status.backupRestoredAndWritten",
                                ("difficulty", difficultyName)));
                    }
                },
                null,
                null);
        }

        private bool RestoreBackup(
            string backupPath,
            int difficulty,
            bool writeCurrentFile = false)
        {
            try
            {
                var chartText = File.ReadAllText(backupPath);
                var result = ChartParser.Parse(
                    chartText.Replace("\r\n", "\n").Split('\n'),
                    backupPath,
                    difficulty);
                if (!result.Success)
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.backup.chartInvalid",
                            ("error", result.Diagnostics[0].Message)));
                }
                _session.Replace(
                    result.Chart,
                    result.Chart.DelayMs);
                _currentDifficulty = difficulty;
                UpdateDifficultyAvailability(
                    _package,
                    difficulty,
                    chartText,
                    false);
                RefreshSongInfoCard(false);
                _playheadMs = 0.0;
                _selected.Clear();
                RefreshEditorVisuals();
                Seek(0.0);
                if (writeCurrentFile)
                {
                    if (!SavePackageInternal(false))
                    {
                        return false;
                    }
                    ShowHint(
                        L.Get(
                            "falsum.status.backupRestoredAndWritten",
                            ("difficulty", DifficultyName(difficulty))),
                        4.0f);
                }
                else
                {
                    _isDirty = true;
                    ShowHint(
                        L.Get(
                            "falsum.status.backupRestored",
                            ("difficulty", DifficultyName(difficulty))),
                        4.0f);
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowHint(
                    L.Get(
                        "falsum.error.backup.restoreFailed",
                        ("error", exception.Message)),
                    5.0f);
                return false;
            }
        }

        private void OpenBackupFolder()
        {
            try
            {
                FalsumPlatform.OpenDirectory(
                    FalsumBackupService.EnsureRootDirectory());
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowHint(
                    L.Get(
                        "falsum.error.backup.openFolderFailed",
                        ("error", exception.Message)),
                    4.0f);
            }
        }

        private void OpenCacheFolder()
        {
            try
            {
                var directory = FalsumCacheStore.DirectoryPath;
                Directory.CreateDirectory(directory);
                FalsumPlatform.OpenDirectory(directory);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowHint(
                    L.Get(
                        "falsum.error.cache.openFailed",
                        ("error", exception.Message)),
                    4.0f);
            }
        }

        private void ClearCache()
        {
            if (_view == null || _view.PreferencesView == null)
            {
                return;
            }
            var size = FalsumCacheStore.GetSizeBytes();
            if (size <= 0L)
            {
                _view.PreferencesView.RefreshCacheSize();
                _view.PreferencesView.ShowInfo(
                    L.Get("falsum.status.cacheEmpty"));
                return;
            }
            _view.ShowConfirm(
                L.Get(
                    "falsum.confirm.clearCache",
                    ("size", FalsumCacheStore.FormatSize(size))),
                L.Get("falsum.common.cancel"),
                L.Get("falsum.preferences.advanced.clearCache"),
                delegate
                {
                    try
                    {
                        var cleared = FalsumCacheStore.Clear();
                        _view.PreferencesView.RefreshCacheSize();
                        _view.PreferencesView.ShowInfo(
                            L.Get(
                                "falsum.status.cacheClearedWithSize",
                                ("size", FalsumCacheStore.FormatSize(
                                    cleared))));
                        ShowHint(
                            L.Get(
                                "falsum.status.cacheClearedWithSize",
                                ("size", FalsumCacheStore.FormatSize(
                                    cleared))),
                            3.0f);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        _view.PreferencesView.RefreshCacheSize();
                        _view.PreferencesView.ShowError(
                            L.Get(
                                "falsum.error.cache.clearFailed",
                                ("error", exception.Message)));
                    }
                },
                null,
                null);
        }

        private void OpenToolManager()
        {
            if (_view == null || _toolManager == null)
            {
                return;
            }
            PrepareForToolInteraction();
            CloseActiveToolRuntime();
            _view.SetToolCatalog(_toolManager.Tools);
            _view.ShowToolManager();
        }

        private void ReloadTools()
        {
            if (_toolManager == null)
            {
                return;
            }
            CloseActiveToolRuntime();
            _toolManager.Reload();
            _view.SetToolCatalog(_toolManager.Tools);
            _view.ShowToolManager();
            var dialog = _view.ToolDialog;
            if (dialog != null)
            {
                dialog.SetStatus(
                    L.Get(
                        "falsum.tools.reloaded",
                        ("count", _toolManager.Tools.Count)),
                    false);
            }
        }

        private void OpenTool(string id)
        {
            OpenToolInternal(id, true);
        }

        private void OpenToolInternal(
            string id,
            bool requirePackage)
        {
            if (_toolManager == null || _view == null)
            {
                return;
            }
            if (requirePackage && !RequireLoadedPackage())
            {
                return;
            }
            if (_view.IsToolDialogOpen)
            {
                CloseActiveToolRuntime();
            }
            var manifest = _toolManager.Find(id);
            if (manifest == null)
            {
                OpenToolManager();
                _view.ToolDialog.SetStatus(
                    L.Get(
                        "falsum.error.tool.notFound",
                        ("id", id)),
                    true);
                return;
            }
            if (!manifest.Enabled)
            {
                OpenToolManager();
                _view.ToolDialog.SetStatus(
                    L.Get(
                        "falsum.error.tool.disabled",
                        ("name", manifest.DisplayName)),
                    true);
                return;
            }
            if (!manifest.IsReady)
            {
                OpenToolManager();
                _view.ToolDialog.SetStatus(
                    manifest.LoadError,
                    true);
                return;
            }

            PrepareForToolInteraction();
            FalsumToolRuntime runtime = null;
            try
            {
                var snapshot = FalsumToolChartSnapshot.Capture(
                    _chart,
                    _session.DelayMs,
                    _playheadMs);
                runtime = new FalsumToolRuntime(
                    manifest,
                    snapshot);
                runtime.Load();
                var form = runtime.CreateForm();
                _activeToolRuntime = runtime;
                _view.ShowTool(manifest, form);
            }
            catch (Exception exception)
            {
                if (runtime != null)
                {
                    runtime.Dispose();
                }
                OpenToolManager();
                _view.ToolDialog.SetStatus(
                    DescribeToolError(exception),
                    true);
            }
        }

        private void CloseActiveToolRuntime()
        {
            if (_toolNotePickActive)
            {
                CancelToolNotePick();
            }
            if (_activeToolRuntime != null)
            {
                _activeToolRuntime.Dispose();
                _activeToolRuntime = null;
            }
        }

        private void OnToolDialogClosed()
        {
            CloseActiveToolRuntime();
            ShowPendingToolReport();
        }

        private void ShowPendingToolReport()
        {
            if (_pendingToolReport == null || _view == null)
            {
                return;
            }
            var report = _pendingToolReport;
            _pendingToolReport = null;
            _view.ShowToolReport(report);
        }

        private void RunTool(FalsumToolRunRequest request)
        {
            if (_activeToolRuntime == null || request == null)
            {
                return;
            }
            if (!string.Equals(
                    request.ToolId,
                    _activeToolRuntime.Manifest.Id,
                    StringComparison.Ordinal))
            {
                _view.ToolDialog.SetStatus(
                    L.Get(
                        "falsum.error.tool.changed"),
                    true);
                return;
            }

            var dialog = _view.ToolDialog;
            PrepareForToolInteraction();
            dialog.SetBusy(true);
            dialog.SetStatus(
                L.Get("falsum.tools.validating"),
                false);
            try
            {
                var issues = _activeToolRuntime.Validate(request);
                if (issues.Count > 0)
                {
                    dialog.SetBusy(false);
                    dialog.ShowValidationIssues(issues);
                    return;
                }
            }
            catch (Exception exception)
            {
                dialog.SetBusy(false);
                dialog.SetStatus(
                    DescribeToolError(exception),
                    true);
                return;
            }

            dialog.SetStatus(
                L.Get("falsum.tools.executing"),
                false);
            try
            {
                var output = _activeToolRuntime.Run(request);
                var manifest = _activeToolRuntime.Manifest;
                if (output.RequiresFileConfirmation)
                {
                    _pendingToolManifest = manifest;
                    _pendingToolOutput = output;
                    _view.CloseToolDialog();
                    _view.ShowConfirm(
                        output.BuildFileConfirmation(),
                        L.Get("falsum.common.cancel"),
                        L.Get("falsum.tools.writeAndApply"),
                        ExecutePendingToolRun,
                        null,
                        null,
                        ClearPendingToolRun);
                    return;
                }
                ExecuteToolOutput(manifest, output);
            }
            catch (Exception exception)
            {
                dialog.SetBusy(false);
                dialog.SetStatus(
                    DescribeToolError(exception),
                    true);
            }
        }

        private void ExecutePendingToolRun()
        {
            if (_pendingToolManifest == null ||
                _pendingToolOutput == null)
            {
                return;
            }
            var manifest = _pendingToolManifest;
            var output = _pendingToolOutput;
            _pendingToolManifest = null;
            _pendingToolOutput = null;
            ExecuteToolOutput(manifest, output);
        }

        private void ClearPendingToolRun()
        {
            _pendingToolManifest = null;
            _pendingToolOutput = null;
        }

        private void ExecuteToolOutput(
            FalsumToolManifest manifest,
            FalsumToolRunOutput output)
        {
            var committed = false;
            try
            {
                if (output.Batch != null &&
                    output.Batch.HasChanges)
                {
                    committed = _session.ApplyBatch(
                        L.Get(
                            "falsum.history.tool",
                            ("name", manifest.DisplayName)),
                        output.Batch.Apply,
                        output.Batch.DelayMs);
                }
                ExecuteToolFileWritesTransactional(
                    output.FileWrites);
                if (committed)
                {
                    RefreshEditorVisuals();
                }
                _pendingToolReport = output.Report;
                if (_view.IsToolDialogOpen)
                {
                    _view.CloseToolDialog();
                }
                else
                {
                    ShowPendingToolReport();
                }
                var message = string.IsNullOrWhiteSpace(
                    output.Message)
                    ? L.Get(
                        "falsum.tools.executed",
                        ("name", manifest.DisplayName))
                    : output.Message;
                if (output.Batch.Count > 0)
                {
                    message = L.Get(
                        "falsum.tools.executedWithChanges",
                        ("message", message),
                        ("count", output.Batch.Count));
                }
                ShowHint(message, 3.0f);
            }
            catch (Exception exception)
            {
                if (_session.HasPendingEdit)
                {
                    _session.CancelChange();
                }
                else if (committed)
                {
                    _session.Undo();
                }
                RefreshEditorVisuals();
                Debug.LogException(exception);
                _view.ShowConfirm(
                    DescribeToolError(exception),
                    L.Get("falsum.common.close"),
                    L.Get("falsum.common.confirm"),
                    null,
                    null,
                    null);
            }
        }

        private void LocateToolReportItem(
            FalsumToolReportItem item)
        {
            if (item == null)
            {
                return;
            }
            PauseForEditInteraction();
            SetMode(EditorMode.Select);
            var focusTimeMs = item.TimeMs;
            if (item.TargetNotes.Count > 0)
            {
                focusTimeMs = double.PositiveInfinity;
                for (var i = 0; i < item.TargetNotes.Count; i++)
                {
                    var note = item.TargetNotes[i];
                    if (note != null)
                    {
                        focusTimeMs = Math.Min(
                            focusTimeMs,
                            note.TimeMs);
                    }
                }
                if (double.IsInfinity(focusTimeMs))
                {
                    focusTimeMs = item.TimeMs;
                }
            }
            var target = Math.Max(
                0.0,
                Math.Min(
                    focusTimeMs - 20.0,
                    TimelineDuration()));
            if (_host.Clock != null &&
                _host.Clock.IsPlaying)
            {
                PausePlaybackAt(target);
            }
            else
            {
                Seek(target);
            }

            _selected.Clear();
            for (var i = 0; i < item.TargetNotes.Count; i++)
            {
                var note = item.TargetNotes[i];
                if (note != null &&
                    _chart.Notes.Contains(note))
                {
                    _selected.Add(note);
                }
            }
            RefreshPropertyPanel();
            UpdateOverlays();
            UpdateViews();
            ShowHint(
                _selected.Count > 0
                    ? L.Get(
                        "falsum.tools.report.locatedNotes",
                        ("count", _selected.Count))
                    : L.Get(
                        "falsum.tools.report.locatedTime"),
                2.5f);
        }

        private static void ExecuteToolFileWritesTransactional(
            IList<FalsumToolPendingFileWrite> writes)
        {
            if (writes == null || writes.Count == 0)
            {
                return;
            }
            var paths = new List<string>(writes.Count);
            var originalBytes = new List<byte[]>(writes.Count);
            var existed = new List<bool>(writes.Count);
            try
            {
                for (var i = 0; i < writes.Count; i++)
                {
                    var write = writes[i];
                    if (write == null ||
                        string.IsNullOrWhiteSpace(write.Path))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.invalidWriteTarget"));
                    }
                    var path = Path.GetFullPath(write.Path);
                    paths.Add(path);
                    existed.Add(File.Exists(path));
                    originalBytes.Add(
                        File.Exists(path)
                            ? File.ReadAllBytes(path)
                            : null);
                    write.Path = path;
                    write.Execute();
                }
            }
            catch
            {
                for (var i = paths.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (existed[i])
                        {
                            File.WriteAllBytes(
                                paths[i],
                                originalBytes[i]);
                        }
                        else if (File.Exists(paths[i]))
                        {
                            File.Delete(paths[i]);
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        Debug.LogError(
                            "[Falsum] Tool file rollback failed for " +
                            paths[i] + ": " +
                            rollbackException.Message);
                    }
                }
                throw;
            }
        }

        private double? ResolveToolTimeAtScreen(
            Vector2 screenPosition)
        {
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                _world2D.SetViewport(
                    _view.GetChartViewportScreenRect());
            }
            double rawTime;
            float lanePosition;
            int lane;
            if (!TryPointerTrack(
                    screenPosition,
                    _playheadMs,
                    out rawTime,
                    out lanePosition,
                    out lane))
            {
                return null;
            }
            return _grid.Snap(
                _chart,
                Math.Max(0.0, rawTime));
        }

        private void HandleToolTimePickInput()
        {
            if (!_toolTimePickActive)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) &&
                _view.ToolDialog != null)
            {
                _view.ToolDialog.CancelTimePickSelection();
                return;
            }
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                ScrollTimeline(scroll > 0.0f ? 1.0 : -1.0);
            }
        }

        private void BeginToolTimePick()
        {
            _toolTimePickActive = true;
            var resolved = ResolveToolTimeAtScreen(
                Input.mousePosition);
            _toolTimePickCandidateMs = resolved.HasValue
                ? resolved.Value
                : Math.Max(0.0, Math.Round(_playheadMs));
        }

        private void EndToolTimePick()
        {
            _toolTimePickActive = false;
            _modePlane.Hide();
            _view.SetTwoDModeOverlay(
                false,
                0.0f,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                Color.white);
        }

        private double UpdateToolTimePickCandidate()
        {
            if (!_toolTimePickActive)
            {
                return Math.Max(0.0, Math.Round(_playheadMs));
            }
            var resolved = ResolveToolTimeAtScreen(
                Input.mousePosition);
            if (resolved.HasValue)
            {
                _toolTimePickCandidateMs = resolved.Value;
            }
            return _toolTimePickCandidateMs;
        }

        private void BeginToolNotePick(
            FalsumToolNotePickRequest request)
        {
            if (request == null || _chart == null)
            {
                return;
            }
            if (_toolNotePickActive)
            {
                CancelToolNotePick();
            }

            _toolNotePickReturnMode = _mode;
            _toolNotePickReturnMask = _selectionMask;
            _toolNotePickPreviousSelection.Clear();
            for (var i = 0; i < _selected.Count; i++)
            {
                if (_selected[i] != null &&
                    _chart.Notes.Contains(_selected[i]))
                {
                    _toolNotePickPreviousSelection.Add(_selected[i]);
                }
            }

            _toolNotePickRequest = request;
            _toolNotePickActive = true;
            _selectionMask = ToolNotePickMask(request.NoteKinds);
            SetMode(EditorMode.ToolNotePick);
            _selected.Clear();
            for (var i = 0; i < request.SelectedNotes.Count; i++)
            {
                var note = FindToolPickedNote(
                    request.SelectedNotes[i]);
                if (note != null &&
                    MatchesSelectionMask(note.Kind, _selectionMask) &&
                    !_selected.Contains(note))
                {
                    _selected.Add(note);
                }
            }
            ApplyEditorSelectionDimming();
            RefreshPropertyPanel();
            UpdateViews();
            UpdateOverlays();
            var dialog = _view != null ? _view.ToolDialog : null;
            if (dialog != null)
            {
                dialog.SetNotePickCount(_selected.Count);
            }
        }

        private void HandleToolNotePickPointerDown(
            Vector2 screenPosition)
        {
            if (!_toolNotePickActive || _chart == null)
            {
                return;
            }

            List<ChartNote> candidates;
            if (!TryPickAllNotes(screenPosition, out candidates) ||
                candidates == null ||
                candidates.Count == 0)
            {
                ResetPickCycle();
                _selected.Clear();
                RefreshToolNotePickSelection();
                return;
            }

            var note = candidates[0];
            if (_toolNotePickRequest.Multiple)
            {
                ResetPickCycle();
                if (_selected.Contains(note))
                {
                    _selected.Remove(note);
                }
                else if (_selected.Count <
                         _toolNotePickRequest.MaxCount)
                {
                    _selected.Add(note);
                }
                else
                {
                    var dialog = _view != null
                        ? _view.ToolDialog
                        : null;
                    if (dialog != null)
                    {
                        dialog.SetNotePickError(
                            L.Get(
                                "falsum.hint.toolNotePick.maxReached",
                                ("max",
                                    _toolNotePickRequest.MaxCount)));
                    }
                }
            }
            else
            {
                var continuesCycle =
                    _hasPickCycle &&
                    IsSamePickCyclePosition(screenPosition) &&
                    MatchesPickCycleCandidates(candidates);
                if (continuesCycle)
                {
                    _pickCycleIndex =
                        (_pickCycleIndex + 1) %
                        candidates.Count;
                }
                else
                {
                    StorePickCycle(screenPosition, candidates);
                }
                note = candidates[_pickCycleIndex];
                _selected.Clear();
                _selected.Add(note);
            }

            RefreshToolNotePickSelection();
        }

        private void HandleToolNotePickScroll(float delta)
        {
            if (!_toolNotePickActive ||
                Math.Abs(delta) < 0.01f)
            {
                return;
            }
            // `delta` arrives in the EDITOR's wheel convention (positive =
            // forward in time), because the dialog view normalises UI
            // Toolkit's opposite-signed WheelEvent.delta.y at its boundary.
            // Do not negate here: this must step the timeline in the same
            // direction as the chart's own wheel.
            ScrollTimeline(delta > 0.0f ? 1.0 : -1.0);
        }

        private void RefreshToolNotePickSelection()
        {
            RefreshPropertyPanel();
            var dialog = _view != null ? _view.ToolDialog : null;
            if (dialog != null)
            {
                dialog.SetNotePickCount(_selected.Count);
            }
            UpdateOverlays();
            UpdateViews();
        }

        private void ConfirmToolNotePick()
        {
            if (!_toolNotePickActive)
            {
                return;
            }
            string error;
            if (!ValidateToolNotePickSelection(out error))
            {
                var errorDialog = _view != null
                    ? _view.ToolDialog
                    : null;
                if (errorDialog != null)
                {
                    errorDialog.SetNotePickError(error);
                }
                return;
            }

            var references =
                new List<FalsumToolNoteReference>(_selected.Count);
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                var index = _chart.Notes.IndexOf(note);
                if (index < 0)
                {
                    continue;
                }
                references.Add(
                    new FalsumToolNoteReference
                    {
                        Key = "note:" +
                              index.ToString(
                                  CultureInfo.InvariantCulture),
                        Id = note.Id,
                        Kind =
                            FalsumToolChartSnapshot.NoteKindName(
                                note.Kind),
                        TimeMs = note.TimeMs,
                    });
            }

            EndToolNotePickState();
            var dialog = _view != null ? _view.ToolDialog : null;
            if (dialog != null)
            {
                dialog.CompleteNotePick(references);
            }
        }

        private void CancelToolNotePick()
        {
            if (!_toolNotePickActive)
            {
                return;
            }
            EndToolNotePickState();
        }

        private void EndToolNotePickState()
        {
            var returnMode = _toolNotePickReturnMode;
            var returnMask = _toolNotePickReturnMask;
            _toolNotePickActive = false;
            _toolNotePickRequest = null;
            _selectionMask = returnMask;
            _selected.Clear();
            for (var i = 0;
                 i < _toolNotePickPreviousSelection.Count;
                 i++)
            {
                var note = _toolNotePickPreviousSelection[i];
                if (note != null &&
                    _chart != null &&
                    _chart.Notes.Contains(note) &&
                    MatchesSelectionMask(note.Kind, returnMask))
                {
                    _selected.Add(note);
                }
            }
            _toolNotePickPreviousSelection.Clear();
            SetMode(
                returnMode == EditorMode.PastePreview
                    ? EditorMode.Select
                    : returnMode);
            if (_view != null)
            {
                _view.SetSelectionFilterState(
                    returnMask == FalsumNotePickMask.GroundNotes,
                    returnMask == FalsumNotePickMask.SkyNotes);
            }
            ApplyEditorSelectionDimming();
            RefreshPropertyPanel();
            UpdateOverlays();
            UpdateViews();
        }

        private bool ValidateToolNotePickSelection(
            out string error)
        {
            error = string.Empty;
            if (_toolNotePickRequest == null)
            {
                error = L.Get(
                    "falsum.error.tool.notePickUnavailable");
                return false;
            }
            if (_selected.Count <
                _toolNotePickRequest.MinCount ||
                _selected.Count >
                _toolNotePickRequest.MaxCount)
            {
                error = L.Get(
                    "falsum.hint.toolNotePick.countRange",
                    ("count", _selected.Count),
                    ("min", _toolNotePickRequest.MinCount),
                    ("max",
                        _toolNotePickRequest.MaxCount ==
                        int.MaxValue
                            ? L.Get(
                                "falsum.tools.notePick.unbounded")
                            : _toolNotePickRequest.MaxCount
                                .ToString(Invariant)));
                return false;
            }
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                if (note == null ||
                    !_chart.Notes.Contains(note) ||
                    !_toolNotePickRequest.NoteKinds.Contains(
                        FalsumToolChartSnapshot.NoteKindName(
                            note.Kind)))
                {
                    error = L.Get(
                        "falsum.error.tool.noteSelectionInvalid",
                        ("field", _toolNotePickRequest.FieldKey));
                    return false;
                }
            }
            return true;
        }

        private ChartNote FindToolPickedNote(
            FalsumToolNoteReference reference)
        {
            if (reference == null ||
                _chart == null ||
                string.IsNullOrWhiteSpace(reference.Key) ||
                !reference.Key.StartsWith(
                    "note:",
                    StringComparison.Ordinal))
            {
                return null;
            }
            int index;
            if (!int.TryParse(
                    reference.Key.Substring("note:".Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out index) ||
                index < 0 ||
                index >= _chart.Notes.Count)
            {
                return null;
            }
            return _chart.Notes[index];
        }

        private static FalsumNotePickMask ToolNotePickMask(
            IList<string> noteKinds)
        {
            var mask = FalsumNotePickMask.None;
            if (noteKinds == null)
            {
                return mask;
            }
            for (var i = 0; i < noteKinds.Count; i++)
            {
                switch (noteKinds[i])
                {
                    case "tap":
                        mask |= FalsumNotePickMask.Tap;
                        break;
                    case "hold":
                        mask |= FalsumNotePickMask.Hold;
                        break;
                    case "flick":
                        mask |= FalsumNotePickMask.Flick;
                        break;
                    case "skyArea":
                        mask |= FalsumNotePickMask.SkyArea;
                        break;
                }
            }
            return mask;
        }

        private static string DescribeToolError(
            Exception exception)
        {
            var toolError = exception as FalsumToolRuntimeException;
            return toolError != null
                ? L.Get(
                    "falsum.toolError.detail",
                    ("code", toolError.Code),
                    ("message", toolError.Message))
                : L.Get(
                    "falsum.toolError.detail",
                    ("code", exception.GetType().Name),
                    ("message", exception.Message));
        }

        private void PrepareForToolInteraction()
        {
            PauseForEditInteraction();
            if (_session != null && _session.HasPendingEdit)
            {
                _session.CancelChange();
                RefreshEditorVisuals();
            }
        }

        private void OpenPreferences()
        {
            if (_view == null ||
                _view.IsPreferencesOpen ||
                _view.IsModalOpen)
            {
                return;
            }
            PauseForEditInteraction();
            _view.SetUiStyle(_preferences.Advanced.UIStyle);
            _backgroundBeforePreferences =
                _manifest != null
                    ? _manifest.EffectiveBackgroundFile
                    : string.Empty;
            _view.ShowPreferences(
                _preferences.Clone(),
                new FalsumChartMetadataDraft(
                    _package,
                    _package != null),
                _package);
        }

        private void BrowsePreferencesCover()
        {
            if (_view == null || !_view.IsPreferencesOpen)
            {
                return;
            }
            var path = FalsumFileDialog.OpenImage();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            try
            {
                _view.PreferencesView.ShowInfo(
                    L.Get("falsum.status.convertingCover"));
                var cover =
                    FalsumMediaConverter.ConvertCover(path);
                _view.PreferencesView.SetCoverPreview(
                    cover,
                    path);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _view.PreferencesView.ShowError(
                    L.Get(
                        "falsum.error.cover.convertFailed",
                        ("error", exception.Message)));
            }
        }

        private void BrowsePreferencesAudio()
        {
            if (_view == null || !_view.IsPreferencesOpen)
            {
                return;
            }
            var path = FalsumFileDialog.OpenAudio();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            _view.PreferencesView.ShowInfo(
                L.Get("falsum.status.convertingAudio"));
            StartCoroutine(ConvertPreferencesAudio(path));
        }

        private IEnumerator ConvertPreferencesAudio(
            string sourcePath)
        {
            Task<FalsumConvertedAudio> task;
            try
            {
                var job =
                    FalsumMediaConverter.PrepareAudioConversion(
                        sourcePath);
                task = Task.Run(
                    () => FalsumMediaConverter.ConvertAudio(job));
            }
            catch (Exception exception)
            {
                if (_view != null &&
                    _view.IsPreferencesOpen)
                {
                    _view.PreferencesView.ShowError(
                        L.Get(
                            "falsum.error.audio.convertFailed",
                            ("error", exception.Message)));
                }
                yield break;
            }
            while (!task.IsCompleted)
            {
                yield return null;
            }
            if (task.IsFaulted)
            {
                var exception = task.Exception.GetBaseException();
                Debug.LogException(exception);
                if (_view != null &&
                    _view.IsPreferencesOpen)
                {
                    _view.PreferencesView.ShowError(
                        L.Get(
                            "falsum.error.audio.convertFailed",
                            ("error", exception.Message)));
                }
                yield break;
            }
            if (_view != null && _view.IsPreferencesOpen)
            {
                _view.PreferencesView.SetAudioPreview(
                    task.Result,
                    sourcePath);
            }
        }

        private void EnterPreferencesBackgroundPreview()
        {
            if (_preferencesPreviewActive)
            {
                return;
            }
            _preferencesPreviewActive = true;
            PauseForEditInteraction();
            CancelMiddleEdit();
            if (_radial != null)
            {
                _radial.Hide();
            }
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.RefreshNoteVisuals(
                    new ChartNote[0]);
            }
            if (_world != null)
            {
                _world.SetSelectionOverlayVisible(false);
            }
        }

        private void ExitPreferencesBackgroundPreview()
        {
            if (!_preferencesPreviewActive)
            {
                return;
            }
            _preferencesPreviewActive = false;
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.SyncFromChart();
            }
            if (_world != null)
            {
                _world.SetSelectionOverlayVisible(
                    _viewMode !=
                    FalsumViewMode.Orthographic2D);
            }
            RefreshEditorAtChartTime(_playheadMs);
            RefreshPropertyPanel();
            UpdateViews();
        }

        private void SetPreferencesPreviewBackground(
            string backgroundFile)
        {
            if (string.IsNullOrWhiteSpace(backgroundFile))
            {
                return;
            }
            try
            {
                _host.SetGameplayBackground(backgroundFile);
                if (_view != null &&
                    (_view.IsPreferencesOpen ||
                     _view.IsChartInfoOpen))
                {
                    _view.PreferencesView
                        .ShowBackgroundError(string.Empty);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (_view != null &&
                    (_view.IsPreferencesOpen ||
                     _view.IsChartInfoOpen))
                {
                    _view.PreferencesView.ShowBackgroundError(
                        L.Get(
                            "falsum.error.background.loadFailed",
                            ("error", exception.Message)));
                }
            }
        }

        private void CommitPreferences(
            FalsumPreferences preferences,
            FalsumChartMetadataDraft metadata,
            FalsumPackage package)
        {
            if (preferences == null ||
                metadata == null ||
                _view == null ||
                !_view.IsPreferencesOpen)
            {
                return;
            }
            if (_preferencesPreviewActive)
            {
                _view.PreferencesView.ShowError(
                    L.Get(
                        "falsum.error.preferences.finishBackgroundPreview"));
                return;
            }

            var snapshot =
                metadata.HasPackage &&
                metadata.HasChangesFrom(package)
                    ? FalsumMetadataApplyService.Capture(package)
                    : null;
            var applied = false;
            if (metadata.HasPackage &&
                metadata.HasChangesFrom(package))
            {
                string applyError;
                if (!FalsumMetadataApplyService.TryApply(
                        package,
                        metadata,
                        out applyError))
                {
                    _view.PreferencesView.ShowError(
                        L.Get(
                            "falsum.error.metadata.applyFailed",
                            ("error", applyError)));
                    return;
                }
                applied = true;
            }

            _pendingPreferences = preferences.Clone();
            _pendingPreferencesMetadata = metadata;
            _pendingPreferencesSnapshot = snapshot;
            _preferencesMetadataApplied = applied;

            if (!DisplayEquals(
                    _preferences.Display,
                    preferences.Display))
            {
                string displayError;
                if (!FalsumDisplaySettingsController.TryApply(
                        preferences,
                        out displayError))
                {
                    RestorePendingMetadata();
                    FalsumDisplaySettingsController.TryApply(
                        _preferences,
                        out displayError);
                    _view.PreferencesView.ShowError(
                        L.Get(
                            "falsum.error.display.applyFailed",
                            ("error", displayError)));
                    ClearPendingPreferences();
                    return;
                }
                _view.PreferencesView.ShowDisplayConfirmation(
                    10,
                    FinalizePreferencesCommit,
                    RevertPreferencesDisplay);
                return;
            }

            FinalizePreferencesCommit();
        }

        private void FinalizePreferencesCommit()
        {
            if (_pendingPreferences == null)
            {
                return;
            }
            var audioChanged =
                _pendingPreferencesMetadata != null &&
                _pendingPreferencesMetadata.HasPendingAudio;
            try
            {
                FalsumPreferencesStore.Save(_pendingPreferences);
                _preferences = _pendingPreferences.Clone();
                ApplyPreferencesToRuntime(_preferences);

                if (_preferencesMetadataApplied &&
                    _package != null &&
                    _manifest != null)
                {
                    _chart.SongId = _manifest.SongId;
                    _host.SetGameplayBackground(
                        _manifest.EffectiveBackgroundFile);
                    RefreshSongInfoCard(true);
                    RefreshEditorVisuals();
                    UpdateViews();
                    MarkDirty();
                    if (audioChanged)
                    {
                        BeginPackageAudioLoad(
                            _package,
                            _playheadMs);
                    }
                }

                if (_pendingPreferencesMetadata != null)
                {
                    _pendingPreferencesMetadata
                        .DisposeTemporaryFiles();
                }
                ClearPendingPreferences();
                _view.ClosePreferences();
                ShowHint(
                    L.Get("falsum.status.preferencesSaved"),
                    2.5f);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RestorePendingMetadata();
                string displayError;
                FalsumDisplaySettingsController.TryApply(
                    _preferences,
                    out displayError);
                if (!string.IsNullOrWhiteSpace(
                        _backgroundBeforePreferences))
                {
                    _host.SetGameplayBackground(
                        _backgroundBeforePreferences);
                }
                if (_host != null)
                {
                    _host.ApplyVisualPreferences(_preferences.Visual);
                }
                RefreshSongInfoCard(true);
                RefreshEditorVisuals();
                _view.SetUiStyle(_preferences.Advanced.UIStyle);
                if (_view != null &&
                    _view.IsPreferencesOpen)
                {
                    _view.PreferencesView.ShowError(
                        L.Get(
                            "falsum.error.preferences.saveFailed",
                            ("error", exception.Message)));
                }
            }
        }

        private void RevertPreferencesDisplay()
        {
            RestorePendingMetadata();
            string displayError;
            FalsumDisplaySettingsController.TryApply(
                _preferences,
                out displayError);
            if (!string.IsNullOrWhiteSpace(
                    _backgroundBeforePreferences))
            {
                _host.SetGameplayBackground(
                    _backgroundBeforePreferences);
            }
            // The display confirmation only covers resolution / anti-aliasing,
            // but the same dialog is the only path that can lose the visual
            // settings edited in the same session, so restore those too.
            if (_host != null)
            {
                _host.ApplyVisualPreferences(_preferences.Visual);
            }
            RefreshSongInfoCard(true);
            RefreshEditorVisuals();
            _view.SetUiStyle(_preferences.Advanced.UIStyle);
            ClearPendingPreferences();
            if (_view != null && _view.IsPreferencesOpen)
            {
                _view.PreferencesView.ShowError(
                    L.Get(
                        "falsum.status.displayRestored"));
            }
        }

        private void CancelPreferences()
        {
            if (!string.IsNullOrWhiteSpace(
                    _backgroundBeforePreferences))
            {
                _host.SetGameplayBackground(
                    _backgroundBeforePreferences);
            }
            // Cancelling never committed the edited visual values, but the
            // sliders could still have been dragged. Re-apply the committed
            // group so the scene matches the preferences that survive.
            if (_host != null)
            {
                _host.ApplyVisualPreferences(_preferences.Visual);
            }
            ClearPendingPreferences();
            if (_view != null)
            {
                _view.ClosePreferences();
            }
            RefreshSongInfoCard(true);
            RefreshEditorVisuals();
            _view.SetUiStyle(_preferences.Advanced.UIStyle);
        }

        private void RestorePendingMetadata()
        {
            if (_pendingPreferencesSnapshot != null &&
                _package != null)
            {
                try
                {
                    FalsumMetadataApplyService.Restore(
                        _package,
                        _pendingPreferencesSnapshot);
                    _manifest = _package.Manifest;
                    _host.SetGameplayBackground(
                        _manifest.EffectiveBackgroundFile);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            _preferencesMetadataApplied = false;
        }

        private void ClearPendingPreferences()
        {
            _pendingPreferences = null;
            _pendingPreferencesMetadata = null;
            _pendingPreferencesSnapshot = null;
            _preferencesMetadataApplied = false;
        }

        private void ApplyPreferencesToRuntime(
            FalsumPreferences preferences)
        {
            if (preferences == null)
            {
                return;
            }
            preferences.Normalize();
            _inputRouter.Apply(preferences);
            if (_view != null)
            {
                _view.SetShortcutPreferences(preferences);
            }
            _directDragUnselectedNotes =
                preferences.Editing.DirectDragUnselectedNotes;
            _middleEditAffectsAllSelected =
                preferences.Editing.MiddleEditAffectsAllSelected;
            _disableOutOfBoundsClamping =
                preferences.Advanced.DisableOutOfBoundsClamping;
            _autoBackupEnabled = preferences.Backup.Enabled;
            _autoBackupIntervalSeconds = Mathf.Clamp(
                preferences.Backup.IntervalSeconds,
                60,
                600);
            _backupRetentionCount = Mathf.Clamp(
                preferences.Backup.RetentionCount,
                1,
                100);
            if (_host != null)
            {
                _host.ApplyAudioPreferences(preferences.Audio);
                _host.ApplyVisualPreferences(preferences.Visual);
            }
            ApplyGridPreferences(preferences.Advanced);
            _persistentHint = BuildPlaybackHint();
            RefreshMiddleEditModeStatus();
            _nextAutoBackupTime =
                Time.unscaledTimeAsDouble +
                _autoBackupIntervalSeconds;
        }

        /// <summary>
        /// Re-seats the <c>-</c> / <c>=</c> ladder from the grid preferences.
        /// The sequence lives on the <see cref="FalsumGridPreferences"/> group
        /// (not on the transitional <c>Advanced</c> facade this method is
        /// called with), so it is read from the owning preferences object.
        /// A profile that has never carried the field keeps the Core default.
        /// </summary>
        private void ApplyGridPreferenceSequence(
            FalsumPreferences preferences)
        {
            if (preferences == null || preferences.Grid == null)
            {
                ApplyGridDenominatorSequence(null);
                return;
            }
            ApplyGridDenominatorSequence(
                preferences.Grid.DenominatorSequence);
        }

        private void ApplyGridPreferences(
            FalsumAdvancedPreferences advanced)
        {
            if (_grid == null || advanced == null)
            {
                return;
            }
            List<Color> colors;
            string error;
            if (FalsumBeatGrid.TryParseColorSequence(
                    advanced.GridLineColors,
                    out colors,
                    out error))
            {
                _grid.SetColorSequence(colors);
            }
            else
            {
                _grid.SetColorSequence(null);
            }
            _grid.LineWidthScale =
                advanced.GridLineWidthScale;
            _showMeasureNumbers =
                advanced.ShowMeasureNumbers;
            ApplyGridPreferenceSequence(_preferences);
            if (_view != null)
            {
                _view.SetMeasureNumbersEnabled(
                    _showMeasureNumbers);
            }
        }

        private string BuildPlaybackHint()
        {
            if (_preferences == null)
            {
                return L.Get(
                    "falsum.hint.playback",
                    ("playPause", "Q"),
                    ("audition", "Space"));
            }
            return L.Get(
                "falsum.hint.playback",
                ("playPause", FalsumInputRouter.Format(
                    _preferences.FindBinding(
                        FalsumInputAction.PlayPause))),
                ("audition", FalsumInputRouter.Format(
                    _preferences.FindBinding(
                        FalsumInputAction.TemporaryAudition))));
        }

        private void ApplyDisplayPreferencesAtStartup()
        {
            if (_preferences == null)
            {
                return;
            }
            string error;
            if (!FalsumDisplaySettingsController.TryApply(
                    _preferences,
                    _preferences.Display.BorderlessFullscreen,
                    out error))
            {
                Debug.LogWarning(
                    "[Falsum] Display preferences were not applied: " +
                    error);
            }
            StartCoroutine(CenterWindowNextFrame());
        }

        private static bool DisplayEquals(
            FalsumDisplayPreferences left,
            FalsumDisplayPreferences right)
        {
            return left != null &&
                   right != null &&
                   left.ResolutionWidth == right.ResolutionWidth &&
                   left.ResolutionHeight == right.ResolutionHeight &&
                   left.ResolutionRefreshRate ==
                   right.ResolutionRefreshRate &&
                   left.BorderlessFullscreen ==
                   right.BorderlessFullscreen &&
                   left.AntiAliasingEnabled ==
                   right.AntiAliasingEnabled &&
                   left.AntiAliasingMode ==
                   right.AntiAliasingMode;
        }

        private void RequestExit()
        {
            if (_newChartCreateInProgress)
            {
                ShowHint(
                    L.Get("falsum.hint.newChart.waitForCreation"),
                    3.0f);
                return;
            }
            if (!HasUnsavedChanges)
            {
                _allowQuit = true;
                FalsumPlatform.Quit();
                return;
            }
            _view.ShowConfirm(
                L.Get("falsum.confirm.unsavedChangesExit"),
                L.Get("falsum.common.cancel"),
                L.Get("falsum.action.saveAndExit"),
                delegate
                {
                    if (SavePackageInternal(false))
                    {
                        _allowQuit = true;
                        FalsumPlatform.Quit();
                    }
                },
                L.Get("falsum.confirm.doNotSaveAction"),
                delegate
                {
                    _allowQuit = true;
                    FalsumPlatform.Quit();
                });
        }

        private void ApplySelectionFilter(
            bool groundOnly,
            bool skyOnly)
        {
            var mask = groundOnly
                ? FalsumNotePickMask.GroundNotes
                : (skyOnly
                    ? FalsumNotePickMask.SkyNotes
                    : FalsumNotePickMask.All);
            _selectionMask = mask;
            for (var i = _selected.Count - 1; i >= 0; i--)
            {
                if (!MatchesSelectionMask(_selected[i].Kind, mask))
                {
                    _selected.RemoveAt(i);
                }
            }
            _view.SetSelectionFilterState(
                groundOnly,
                skyOnly);
            ApplyEditorSelectionDimming();
            RefreshPropertyPanel();
            UpdateOverlays();
            UpdateViews();
            ShowHint(
                groundOnly
                    ? L.Get("falsum.hint.selection.groundOnly")
                    : (skyOnly
                        ? L.Get("falsum.hint.selection.skyOnly")
                        : L.Get("falsum.hint.selection.all")),
                1.8f);
        }

        private void ApplyEditorSelectionDimming()
        {
            if (_host == null || _host.NoteVisuals == null)
            {
                return;
            }
            _host.NoteVisuals.SetEditorDimmedKinds(
                _selectionMask == FalsumNotePickMask.SkyNotes,
                _selectionMask == FalsumNotePickMask.GroundNotes);
        }

        private static bool MatchesSelectionMask(
            NoteKind kind,
            FalsumNotePickMask mask)
        {
            switch (kind)
            {
                case NoteKind.Tap:
                    return (mask & FalsumNotePickMask.Tap) != 0;
                case NoteKind.Hold:
                    return (mask & FalsumNotePickMask.Hold) != 0;
                case NoteKind.Flick:
                    return (mask & FalsumNotePickMask.Flick) != 0;
                case NoteKind.SkyArea:
                    return (mask & FalsumNotePickMask.SkyArea) != 0;
                default:
                    return false;
            }
        }

        private void UpdatePointer()
        {
            if (_debugPointerOverride)
            {
                return;
            }
            var rawTime = _pointerRawTime;
            _hasPointer =
                !_view.IsPointerOverUi(Input.mousePosition) &&
                TryPointerTrack(
                    Input.mousePosition,
                    _playheadMs,
                    out rawTime,
                    out _pointerLanePosition,
                    out _pointerLane);
            if (_hasPointer)
            {
                _pointerRawTime = rawTime;
                _pointerTime = ResolveSkyAreaPlacementSnapTime(rawTime);
            }
        }

        private bool TryPointerTrack(
            Vector2 screenPosition,
            double nowMs,
            out double rawTime,
            out float lanePosition,
            out int lane)
        {
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                _world2D.SetViewport(
                    _view.GetChartViewportScreenRect());
                return _world2D.TryPointerTrack(
                    screenPosition,
                    nowMs,
                    out rawTime,
                    out lanePosition,
                    out lane);
            }
            return _world.TryPointerTrack(
                screenPosition,
                nowMs,
                out rawTime,
                out lanePosition,
                out lane);
        }

        private bool TryGetPointerNormalizedOnTimePlane(
            Vector2 screenPosition,
            double timeMs,
            double nowMs,
            out float normalizedX)
        {
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                _world2D.SetViewport(
                    _view.GetChartViewportScreenRect());
                return _world2D.TryGetPointerNormalizedOnTimePlane(
                    screenPosition,
                    timeMs,
                    nowMs,
                    out normalizedX);
            }
            return _world.TryGetPointerNormalizedOnTimePlane(
                screenPosition,
                timeMs,
                nowMs,
                out normalizedX);
        }

        private void HandleKeyboard()
        {
            if (_view.IsTyping())
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _view.ClearInputFocus();
                }
                return;
            }
            var ctrl = FalsumModifierState.Control;
            if (ctrl && Input.GetKeyDown(KeyCode.Comma))
            {
                OpenPreferences();
                return;
            }
            // The bank is bound through the configurable key table (Ctrl+B by
            // default) and is allowed with no package open: it is its own
            // library, not a chart operation.
            if (_inputRouter.WasPressed(
                    FalsumInputAction.ToggleSkyAreaBank))
            {
                ToggleBankPanel();
                return;
            }
            if (_package == null)
            {
                if (_inputRouter.WasPressed(
                        FalsumInputAction.ToggleFullscreen))
                {
                    ToggleBorderlessFullscreen();
                    return;
                }
                if (_inputRouter.WasPressed(
                        FalsumInputAction.ToggleGrid))
                {
                    ToggleGrid();
                    return;
                }
                if (_inputRouter.WasPressed(
                        FalsumInputAction.ToggleTrackSpeedEffects))
                {
                    ToggleTrackSpeedEffects();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_view.IsEventsPanelOpen)
                    {
                        _view.SetEventsPanelOpen(false);
                    }
                    return;
                }
                if (Input.anyKeyDown)
                {
                    ShowHint(NoChartPrompt, 2.5f);
                }
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.MiddleEditBoundary))
            {
                SetMiddleEditMode(FalsumMiddleEditMode.Boundary);
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.MiddleEditStartEnd))
            {
                SetMiddleEditMode(FalsumMiddleEditMode.StartEnd);
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.MiddleEditPosition))
            {
                SetMiddleEditMode(FalsumMiddleEditMode.Position);
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.SegmentSelect))
            {
                ClearSelection();
                SetMode(EditorMode.SegmentSelect);
                _segmentHasStart = false;
                _persistentHint = L.Get(
                    "falsum.hint.segment.selectStart");
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.PlayPause))
            {
                TogglePlayback();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.TemporaryAudition))
            {
                BeginTemporaryPlayback();
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Save))
            {
                SavePackage();
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Mirror))
            {
                MirrorSelection();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.MirrorFrontBack))
            {
                MirrorSelectionFrontBack();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.ToggleFlickColor))
            {
                ToggleSelectedFlickColors();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.ToggleFullscreen))
            {
                ToggleBorderlessFullscreen();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.PointPlaceTap))
            {
                SetPointPlaceMode(EditorMode.PlaceTap);
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.PointPlaceHold))
            {
                SetPointPlaceMode(EditorMode.PlaceHold);
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.PointPlaceFlick))
            {
                SetPointPlaceMode(EditorMode.PlaceFlick);
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.PointPlaceSkyArea))
            {
                SetPointPlaceMode(EditorMode.PlaceSkyArea);
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.ToggleGrid))
            {
                ToggleGrid();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.ToggleTrackSpeedEffects))
            {
                ToggleTrackSpeedEffects();
                return;
            }
            if (_mode == EditorMode.PlaceTap ||
                _mode == EditorMode.PlaceHold)
            {
                var width = ReadTapWidth();
                if (width > 0)
                {
                    ApplyActivePlacementWidth(width);
                    return;
                }
            }
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (_view.ShouldBlockChartScroll(
                        (Vector2)Input.mousePosition))
                {
                    return;
                }
                if (_viewMode == FalsumViewMode.Orthographic2D &&
                    ctrl)
                {
                    // 2D keeps its documented Ctrl+wheel visible-range zoom.
                    // It already owns this wheel tick (and never scrolled the
                    // timeline), and it is the 2D spelling of "how fast time
                    // runs", so it is not pushed aside for the flow shortcut.
                    Adjust2DZoom(scroll > 0.0f ? -1 : 1);
                    return;
                }
                if (ctrl)
                {
                    // Ctrl+wheel is the ONLY flow-speed shortcut now: it
                    // replaces the deleted Decrease/IncreaseFlowSpeed keys in
                    // every view mode that used to SCROLL on Ctrl+wheel.
                    // Wheel up = faster, matching the flow slider and the 2D
                    // zoom above (wheel up narrows the visible range).
                    //
                    // The whole branch RETURNS, so the ScrollTimeline call
                    // below cannot also run in the same frame: this tick is
                    // consumed, not shared. It sits after the
                    // ShouldBlockChartScroll guard, so a wheel over the
                    // events panel or the floating tool report is ignored
                    // entirely. The pointer-over-UI test keeps the shortcut
                    // from firing while the wheel belongs to a panel (top
                    // bar, dialog, preferences) — the same guard
                    // HandleMouse uses before it treats a click as a chart
                    // edit.
                    if (!_view.IsPointerOverUi(Input.mousePosition))
                    {
                        AdjustFlowSpeed(scroll > 0.0f ? 0.5f : -0.5f);
                    }
                    return;
                }
                ScrollTimeline(scroll > 0.0f ? 1.0 : -1.0);
                return;
            }
            if (_host.Clock != null && _host.Clock.IsPlaying)
            {
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Undo))
            {
                Undo();
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Redo))
            {
                Redo();
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Copy))
            {
                CopySelection();
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Cut))
            {
                CutSelection();
                return;
            }
            if (_inputRouter.WasPressed(FalsumInputAction.Paste))
            {
                PasteClipboard();
                return;
            }
            if (_inputRouter.WasPressed(
                    FalsumInputAction.DeleteSelection))
            {
                DeleteSelection();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_mode == EditorMode.PastePreview)
                {
                    CancelPastePreview();
                    return;
                }
                if (_view.IsEventsPanelOpen)
                {
                    _view.SetEventsPanelOpen(false);
                    return;
                }
                CancelCurrentAction();
                return;
            }
            if (IsMiddleEditGridAdjustable)
            {
                if (Input.GetKeyDown(KeyCode.Minus) ||
                    Input.GetKeyDown(KeyCode.KeypadMinus))
                {
                    AdjustMiddleEditGrid(-1);
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Equals) ||
                    Input.GetKeyDown(KeyCode.KeypadPlus))
                {
                    AdjustMiddleEditGrid(1);
                    return;
                }
            }
            if (_mode == EditorMode.PlaceFlick ||
                _mode == EditorMode.PlaceSkyArea)
            {
                if (Input.GetKeyDown(KeyCode.Minus) ||
                    Input.GetKeyDown(KeyCode.KeypadMinus))
                {
                    DecreaseFlickDenominator();
                }
                if (Input.GetKeyDown(KeyCode.Equals) ||
                    Input.GetKeyDown(KeyCode.KeypadPlus))
                {
                    IncreaseFlickDenominator();
                }
            }

        }

        private void HandleMouse()
        {
            if (_package == null)
            {
                if (!_view.IsPointerOverUi(Input.mousePosition) &&
                    (Input.GetMouseButtonDown(0) ||
                     Input.GetMouseButtonDown(1) ||
                     Input.GetMouseButtonDown(2)))
                {
                    ShowHint(NoChartPrompt, 2.5f);
                }
                return;
            }
            if (Input.GetMouseButtonUp(2) && IsMiddleEditActive)
            {
                if (_view.IsPointerOverUi(Input.mousePosition))
                {
                    CancelMiddleEdit();
                }
                else
                {
                    CommitMiddleEdit();
                }
            }
            if (IsMiddleEditActive &&
                !Input.GetMouseButton(2) &&
                !Input.GetMouseButtonUp(2))
            {
                CommitMiddleEdit();
            }
            if (_mode != EditorMode.PastePreview &&
                Input.GetMouseButtonDown(1) &&
                !_radial.IsOpen &&
                !_view.IsPointerOverUi(Input.mousePosition))
            {
                ShowRadial(Input.mousePosition);
            }
            if (Input.GetMouseButtonUp(1) && _radial.IsOpen)
            {
                _radial.SelectAt(Input.mousePosition);
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (IsAnchorDragActive)
                {
                    if (_view.IsPointerOverUi(Input.mousePosition))
                    {
                        CancelAnchorDrag();
                    }
                    else
                    {
                        CommitAnchorDrag();
                    }
                }
                else
                {
                    HandleLeftUp();
                }
            }

            if (_view.IsPointerOverUi(Input.mousePosition))
            {
                if (IsMiddleEditActive)
                {
                    CancelMiddleEdit();
                }
                if (IsAnchorDragActive)
                {
                    CancelAnchorDrag();
                }
                return;
            }
            if (_mode != EditorMode.PastePreview &&
                Input.GetMouseButtonDown(2))
            {
                BeginMiddleEdit(Input.mousePosition);
                return;
            }
            if (Input.GetMouseButton(2) && IsMiddleEditActive)
            {
                UpdateMiddleEdit();
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                if (IsAnchorDragActive)
                {
                    UpdateAnchorDrag();
                    return;
                }
                HandleLeftDown();
            }
            if (Input.GetMouseButton(0))
            {
                if (IsAnchorDragActive)
                {
                    UpdateAnchorDrag();
                    return;
                }
                HandleLeftDrag();
            }
        }

        private void HandleLeftDown()
        {
            // Anchors are tested first: grabbing a handle must never reselect
            // or start a box drag underneath it.
            if (_mode == EditorMode.Select &&
                TryBeginAnchorDrag((Vector2)Input.mousePosition))
            {
                return;
            }
            if (_mode == EditorMode.Select)
            {
                HandleSelectDown();
                return;
            }
            if (_mode == EditorMode.SegmentSelect)
            {
                HandleSegmentDown();
                return;
            }
            if (_mode == EditorMode.PlaceTap)
            {
                PlaceTap();
                return;
            }
            if (_mode == EditorMode.PlaceHold)
            {
                HandleHoldDown();
                return;
            }
            if (_mode == EditorMode.PlaceFlick)
            {
                HandleFlickDown();
                return;
            }
            if (_mode == EditorMode.PlaceSkyArea)
            {
                HandleSkyDown();
                return;
            }
            if (_mode == EditorMode.PastePreview)
            {
                CommitPastePreview();
            }
        }

        private void HandleLeftDrag()
        {
            if (_mode == EditorMode.Select &&
                (_draggingSelection || _selectionDragPending))
            {
                if (_draggingSelection || TryActivateSelectionDrag())
                {
                    DragSelection();
                }
            }
            else if (_mode == EditorMode.PlaceFlick && _flickDragging)
            {
                _flickCurrent = SnapPlaneCell(_flickTime);
            }
            else if (_mode == EditorMode.PlaceSkyArea && _skyDragging)
            {
                if (_skyStage == 0)
                {
                    var current = SnapPlaneCell(_skyStartTime);
                    _skyStartPos =
                        (_skyStartAnchor + current) * 0.5;
                    _skyStartWidth = Math.Max(
                        SkyAreaPointWidth,
                        Math.Abs(current - _skyStartAnchor));
                    ClampSkySpanPreservingWidth(
                        ref _skyStartPos,
                        ref _skyStartWidth);
                }
                else if (_skyStage == 2)
                {
                    var current = SnapPlaneCell(_skyEndTime);
                    _skyEndPos =
                        (_skyEndAnchor + current) * 0.5;
                    _skyEndWidth = Math.Max(
                        SkyAreaPointWidth,
                        Math.Abs(current - _skyEndAnchor));
                    ClampSkySpanPreservingWidth(
                        ref _skyEndPos,
                        ref _skyEndWidth);
                }
            }
        }

        private void HandleLeftUp()
        {
            if (_mode == EditorMode.Select &&
                _selectionDragPending &&
                !_draggingSelection)
            {
                var shouldAdvance = _advancePickCycleOnRelease;
                _selectionDragPending = false;
                _dragStates.Clear();
                if (shouldAdvance)
                {
                    AdvancePickCycleOnRelease();
                }
                else
                {
                    _advancePickCycleOnRelease = false;
                }
                return;
            }
            if (_mode == EditorMode.Select && _draggingSelection)
            {
                _advancePickCycleOnRelease = false;
                _draggingSelection = false;
                _selectionDragPending = false;
                if (ContainsNegativeMusicTime(_selected))
                {
                    _session.CancelChange();
                    RefreshEditorVisuals();
                    RefreshPropertyPanel();
                    ShowNegativeMusicTimeWarning();
                    return;
                }
                if (_session.CommitChange())
                {
                    _session.NotifyChanged();
                    if (_host.NoteVisuals != null)
                    {
                        _host.NoteVisuals.RefreshNoteVisuals(_selected);
                    }
                    _host.UpdateChartPlaybackFrame(_playheadMs);
                    UpdateViews();
                }
                return;
            }
            if (_mode == EditorMode.PlaceFlick && _flickDragging)
            {
                CommitFlickPress();
                return;
            }
            if (_mode == EditorMode.PlaceSkyArea && _skyDragging)
            {
                CommitSkyDrag();
            }
        }

        private bool TryPickNote(
            Vector2 screenPosition,
            out ChartNote note)
        {
            List<ChartNote> candidates;
            if (!TryPickAllNotes(screenPosition, out candidates))
            {
                note = null;
                return false;
            }
            note = candidates[0];
            return true;
        }

        private bool TryPickAllNotes(
            Vector2 screenPosition,
            out List<ChartNote> notes)
        {
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                _world2D.SetViewport(
                    _view.GetChartViewportScreenRect());
                return _world2D.TryPickAll(
                    screenPosition,
                    _playheadMs,
                    _chart.Notes,
                    _selectionMask,
                    out notes);
            }
            return _world.TryPickAll(
                screenPosition,
                _playheadMs,
                _chart.Notes,
                _selectionMask,
                out notes);
        }

        private void HandleSelectDown()
        {
            if (!_hasPointer)
            {
                return;
            }
            _selectionDragPending = false;
            _advancePickCycleOnRelease = false;
            var pointer = (Vector2)Input.mousePosition;
            List<ChartNote> candidates;
            if (!TryPickAllNotes(pointer, out candidates) ||
                candidates.Count == 0)
            {
                ResetPickCycle();
                if (!FalsumModifierState.Control)
                {
                    ClearSelection();
                }
                return;
            }

            var ctrl = FalsumModifierState.Control;
            var note = candidates[0];
            var continuesCycle =
                _hasPickCycle &&
                IsSamePickCyclePosition(pointer) &&
                MatchesPickCycleCandidates(candidates);
            if (ctrl)
            {
                if (continuesCycle)
                {
                    _pickCycleIndex =
                        (_pickCycleIndex + 1) %
                        candidates.Count;
                }
                else
                {
                    StorePickCycle(pointer, candidates);
                    ShowPickCycleHint(candidates.Count);
                }
                note = candidates[_pickCycleIndex];
                var wasSelected = _selected.Contains(note);
                if (wasSelected)
                {
                    _selected.Remove(note);
                    if (ReferenceEquals(
                            _selectionDragAnchor,
                            note))
                    {
                        _selectionDragAnchor = null;
                    }
                    RefreshPropertyPanel();
                    return;
                }
                _selected.Add(note);
                _selectionDragAnchor = note;
                RefreshPropertyPanel();
                if (_directDragUnselectedNotes)
                {
                    PrepareSelectionDrag();
                }
                return;
            }

            if (continuesCycle)
            {
                var cycleNote =
                    _pickCycleIndex >= 0 &&
                    _pickCycleIndex < candidates.Count
                        ? candidates[_pickCycleIndex]
                        : note;
                _selectionDragAnchor =
                    _selected.Contains(cycleNote)
                        ? cycleNote
                        : note;
                _advancePickCycleOnRelease = true;
                PrepareSelectionDrag();
                return;
            }

            StorePickCycle(pointer, candidates);
            ShowPickCycleHint(candidates.Count);
            _selected.Clear();
            _selected.Add(note);
            _selectionDragAnchor = note;
            RefreshPropertyPanel();
            if (_directDragUnselectedNotes)
            {
                PrepareSelectionDrag();
            }
        }

        private bool IsSamePickCyclePosition(Vector2 screenPosition)
        {
            var tolerance =
                PickCyclePositionTolerancePixels *
                Mathf.Max(1.0f, Screen.height / 1080.0f);
            return (screenPosition - _pickCycleScreenPosition)
                       .sqrMagnitude <=
                   tolerance * tolerance;
        }

        private bool MatchesPickCycleCandidates(
            IList<ChartNote> candidates)
        {
            if (candidates == null ||
                candidates.Count != _pickCycleCandidates.Count)
            {
                return false;
            }
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!ReferenceEquals(
                        candidates[i],
                        _pickCycleCandidates[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private void StorePickCycle(
            Vector2 screenPosition,
            IList<ChartNote> candidates)
        {
            _pickCycleCandidates.Clear();
            for (var i = 0; i < candidates.Count; i++)
            {
                _pickCycleCandidates.Add(candidates[i]);
            }
            _pickCycleScreenPosition = screenPosition;
            _hasPickCycle = _pickCycleCandidates.Count > 0;
            _pickCycleIndex = _hasPickCycle ? 0 : -1;
        }

        private void ShowPickCycleHint(int candidateCount)
        {
            if (candidateCount <= 1)
            {
                return;
            }
            ShowHint(
                L.Get(
                    "falsum.hint.pickCycle",
                    ("count", candidateCount)),
                2.5f);
        }

        private void ResetPickCycle()
        {
            _pickCycleCandidates.Clear();
            _hasPickCycle = false;
            _advancePickCycleOnRelease = false;
            _pickCycleIndex = -1;
        }

        private void AdvancePickCycleOnRelease()
        {
            _advancePickCycleOnRelease = false;
            if (!_hasPickCycle || _pickCycleCandidates.Count == 0)
            {
                return;
            }
            _pickCycleIndex =
                (_pickCycleIndex + 1) %
                _pickCycleCandidates.Count;
            var note = _pickCycleCandidates[_pickCycleIndex];
            _selected.Clear();
            _selected.Add(note);
            _selectionDragAnchor = note;
            RefreshPropertyPanel();
            UpdateOverlays();
        }

        private void PrepareSelectionDrag()
        {
            _selectionDragPending = true;
            _selectionDragOrigin = Input.mousePosition;
            CaptureSelectionDragOrigin();
        }

        private void CaptureSelectionDragOrigin()
        {
            _dragStartRawTime = _pointerRawTime;
            _dragStartLane = _pointerLane;
            _dragStates.Clear();
            if (_selectionDragAnchor == null ||
                !_selected.Contains(_selectionDragAnchor))
            {
                _selectionDragAnchor = _selected.Count > 0
                    ? _selected[0]
                    : null;
            }
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                _dragStates.Add(new DragState
                {
                    Note = note,
                    TimeMs = note.TimeMs,
                    Lane = note.StartLane,
                    DurationMs = note.DurationMs,
                });
            }
        }

        private void BeginSelectionDrag()
        {
            _selectionDragPending = false;
            _selectionDragOrigin = Input.mousePosition;
            CaptureSelectionDragOrigin();
            ActivateSelectionDrag();
        }

        private bool TryActivateSelectionDrag()
        {
            if (!_selectionDragPending)
            {
                return _draggingSelection;
            }
            if (!ExceedsSelectionDragThreshold(
                    _selectionDragOrigin,
                    Input.mousePosition))
            {
                return false;
            }
            ActivateSelectionDrag();
            return true;
        }

        private void ActivateSelectionDrag()
        {
            _selectionDragPending = false;
            _draggingSelection = true;
            _advancePickCycleOnRelease = false;
            _session.BeginChange(
                L.Get("falsum.history.moveObjects"));
        }

        private static bool ExceedsSelectionDragThreshold(
            Vector2 origin,
            Vector2 current)
        {
            var threshold =
                SelectionDragThresholdPixels *
                Mathf.Max(1.0f, Screen.height / 1080.0f);
            return (current - origin).sqrMagnitude >=
                   threshold * threshold;
        }

        private void DragSelection()
        {
            if (!_hasPointer || _dragStates.Count == 0)
            {
                return;
            }
            var rawDelta = _pointerRawTime - _dragStartRawTime;
            var laneDelta = _pointerLane - _dragStartLane;
            double snappedDelta;
            var hasGroupSnap =
                TryResolveSelectionSkyAreaGroupSnap(
                    rawDelta,
                    out snappedDelta);
            var effectiveTimeDelta = hasGroupSnap
                ? snappedDelta
                : rawDelta;
            for (var i = 0; i < _dragStates.Count; i++)
            {
                var state = _dragStates[i];
                var note = state.Note;
                var desiredTime = Math.Max(
                    0.0,
                    state.TimeMs + effectiveTimeDelta);
                note.TimeMs = hasGroupSnap
                    ? IntegerTimeMs(desiredTime)
                    : IntegerTimeMs(
                        _world.SnapTime(
                            desiredTime,
                            state.TimeMs));
                if (note.Kind == NoteKind.Tap ||
                    note.Kind == NoteKind.Hold)
                {
                    note.StartLane = ClampLaneForWidth(
                        state.Lane + laneDelta,
                        note.WidthLanes,
                        false);
                }
                else if (note.Kind == NoteKind.SkyArea)
                {
                    note.DurationMs = state.DurationMs;
                }
                if (note.Kind == NoteKind.Flick ||
                    note.Kind == NoteKind.SkyArea)
                {
                    NormalizeNoteFractions(note);
                }
            }
            var now = Time.unscaledTimeAsDouble;
            var rebuildVisuals =
                now - _lastSelectionVisualRefresh >= 0.05;
            if (rebuildVisuals)
            {
                _lastSelectionVisualRefresh = now;
                RefreshPropertyPanel();
                var previewNotes = new List<ChartNote>();
                for (var i = 0; i < _selected.Count; i++)
                {
                    var note = _selected[i];
                    if (note.Kind == NoteKind.Tap ||
                        note.Kind == NoteKind.Hold)
                    {
                        previewNotes.Add(note);
                    }
                }
                if (previewNotes.Count > 0 &&
                    _host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RefreshNoteVisuals(previewNotes);
                }
            }
            // Flick and SkyArea visuals re-read `note.TimeMs` inside
            // NoteRenderer.Update, so they need no rebuild, but they do need
            // this refresh on every drag frame. It used to be inside the
            // tap/hold-only branch, so dragging a Flick left its model parked
            // at the old time and nothing was drawn where it would land.
            if (_host != null)
            {
                _host.UpdateChartPlaybackFrame(_playheadMs);
            }
        }

        private void HandleSegmentDown()
        {
            if (!_hasPointer)
            {
                return;
            }
            var time = IntegerTimeMs(_pointerTime);
            if (!_segmentHasStart)
            {
                _segmentStart = time;
                _segmentEnd = time;
                _segmentHasStart = true;
                _persistentHint = L.Get(
                    "falsum.hint.segment.selectEndAgain");
                return;
            }
            _segmentEnd = time;
            CommitSegmentSelection();
        }

        private void CommitSegmentSelection()
        {
            var start = Math.Min(_segmentStart, _segmentEnd);
            var end = Math.Max(_segmentStart, _segmentEnd);
            _selected.Clear();
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                var note = _chart.Notes[i];
                if (IsNoteInSegmentSelection(
                        note,
                        start,
                        end,
                        _selectionMask))
                {
                    _selected.Add(note);
                }
            }
            _segmentHasStart = false;
            _mode = EditorMode.Select;
            _persistentHint = BuildPlaybackHint();
            RefreshPropertyPanel();
            UpdateViews();
        }

        private static bool IsNoteInSegmentSelection(
            ChartNote note,
            double start,
            double end,
            FalsumNotePickMask mask)
        {
            return note != null &&
                   MatchesSelectionMask(note.Kind, mask) &&
                   note.TimeMs >= start - 0.001 &&
                   note.TimeMs <= end + 0.001;
        }

        private void PlaceTap()
        {
            if (!_hasPointer)
            {
                return;
            }
            var timeMs = IntegerTimeMs(_pointerTime);
            if (RejectNegativeMusicTime(timeMs))
            {
                return;
            }
            var lane = ClampLaneForWidth(
                _pointerLane,
                _tapWidth,
                false);
            // Two TAPs may never share a lane that close together: the
            // judgement window would stack them and drop a hit. Bail out
            // before `BeginChange` so a rejected click leaves no undo step.
            double gapMs;
            if (FalsumTapPlacementRule.TryFindBlocker(
                    _chart.Notes,
                    timeMs,
                    lane,
                    _tapWidth,
                    out _,
                    out gapMs))
            {
                // Report how far apart the two notes actually are, not the
                // width of the guard window: a pair 2 ms apart is a 2 ms
                // overlap, and that is the number the designer needs to know
                // how far to move. One decimal keeps sub-millisecond offsets
                // meaningful instead of collapsing them to "0".
                ShowHint(
                    L.Get(
                        "falsum.hint.tap.tooClose",
                        ("delta", Math.Round(gapMs, 1))),
                    2.5f);
                return;
            }
            _session.BeginChange(
                L.Get("falsum.history.placeTap"));
            var note = new ChartNote
            {
                Kind = NoteKind.Tap,
                TimeMs = timeMs,
                StartLane = lane,
                WidthLanes = _tapWidth,
            };
            _chart.Notes.Add(note);
            FinishEditAdded(note);
        }

        private void HandleHoldDown()
        {
            if (!_hasPointer)
            {
                return;
            }
            if (!_holdPlaced)
            {
                var start = IntegerTimeMs(_pointerTime);
                if (RejectNegativeMusicTime(start))
                {
                    return;
                }
                _holdPlaced = true;
                _holdStart = start;
                _holdWidth = Mathf.Clamp(_tapWidth, 1, 4);
                _holdLane = ClampLaneForWidth(
                    _pointerLane,
                    _holdWidth,
                    false);
                _persistentHint = L.Get(
                    "falsum.hint.hold.selectEndAgain");
                return;
            }
            var end = IntegerTimeMs(Math.Max(
                _holdStart + MinimumHoldDurationMs(_holdStart),
                _pointerTime));
            _session.BeginChange(
                L.Get("falsum.history.placeHold"));
            var note = new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = _holdStart,
                DurationMs = end - _holdStart,
                StartLane = _holdLane,
                WidthLanes = _holdWidth,
            };
            _chart.Notes.Add(note);
            _holdPlaced = false;
            _persistentHint = L.Get(
                "falsum.hint.hold.place");
            FinishEditAdded(note);
        }

        // HOLD placement keeps the tail strictly after the head, but that
        // floor must never borrow a beat-grid step: with grid lines switched
        // off the tail has to follow the raw pointer time exactly, matching
        // the ghost preview (which only enforces the 10 ms visual floor).
        private double MinimumHoldDurationMs(double holdStartMs)
        {
            if (_grid == null || _chart == null)
            {
                return 1.0;
            }
            return _grid.MinimumStepMs(_chart, holdStartMs);
        }

        private void HandleFlickDown()
        {
            if (!_hasPointer)
            {
                return;
            }
            var timeMs = IntegerTimeMs(_pointerTime);
            if (RejectNegativeMusicTime(timeMs))
            {
                return;
            }
            _flickHasTime = true;
            _flickTime = timeMs;
            _flickDragging = true;
            var cell = SnapPlaneCell(_flickTime);
            _flickStart = cell;
            _flickCurrent = cell;
            _persistentHint = L.Get(
                "falsum.hint.flick.dragAndAdjust",
                ("denominator", _flickDenominator));
        }

        private void CommitFlickPress()
        {
            _flickDragging = false;
            CommitFlick();
        }

        private void CommitFlick()
        {
            _flickDragging = false;
            if (RejectNegativeMusicTime(_flickTime))
            {
                _flickHasTime = false;
                return;
            }
            var cells = Mathf.Max(
                1,
                Mathf.RoundToInt(
                    (float)Math.Abs(_flickCurrent - _flickStart) *
                    _flickDenominator));
            var denominator = _flickDenominator;
            // Odd cell counts cannot be represented at this denominator, so the
            // note is written at twice the resolution. The ceiling is the
            // interactive grid cap, not a literal: doubling past it would
            // produce a denominator the editor can no longer step.
            if (cells % 2 == 1 &&
                _flickDenominator * 2 <= MaxEditableFlickGrid)
            {
                denominator *= 2;
                cells *= 2;
            }
            var left = _disableOutOfBoundsClamping
                ? Math.Min(_flickStart, _flickCurrent)
                : Math.Max(
                    0.0,
                    Math.Min(_flickStart, _flickCurrent));
            var right = _disableOutOfBoundsClamping
                ? Math.Max(_flickStart, _flickCurrent)
                : Math.Min(
                    1.0,
                    Math.Max(_flickStart, _flickCurrent));
            if (right - left < 0.5 / denominator)
            {
                right = _disableOutOfBoundsClamping
                    ? left + 1.0 / denominator
                    : Math.Min(
                        1.0,
                        left + 1.0 / denominator);
            }
            var center = (left + right) * 0.5;
            var width = Math.Max(0.0, right - left);
            if (!_disableOutOfBoundsClamping)
            {
                var span = FalsumSkyBounds.ClampSpanPreservingWidth(
                    center,
                    width);
                center = span.Center;
                width = span.Width;
            }
            _session.BeginChange(
                L.Get("falsum.history.placeFlick"));
            var note = new ChartNote
            {
                Kind = NoteKind.Flick,
                TimeMs = _flickTime,
                StartPos = center,
                StartWidth = width,
                StartCellCount = denominator,
                EndCellCount = denominator,
                FlickDirection = _flickStart > _flickCurrent ? 16 : 4,
            };
            NormalizeSkyNoteToCells(note);
            _chart.Notes.Add(note);
            _flickHasTime = false;
            _persistentHint = L.Get(
                "falsum.hint.flick.place");
            FinishEditAdded(note);
        }

        private void HandleSkyDown()
        {
            if (!_hasPointer)
            {
                return;
            }
            if (_skyStage == 0)
            {
                var start = IntegerTimeMs(_pointerTime);
                if (RejectNegativeMusicTime(start))
                {
                    return;
                }
                if (!_skyStartFirstEdge)
                {
                    _skyStartTime = start;
                    _skyStartAnchor = SnapPlaneCell(_skyStartTime);
                    _skyStartPos = _skyStartAnchor;
                    _skyStartWidth = SkyAreaPointWidth;
                    ClampSkySpanPreservingWidth(
                        ref _skyStartPos,
                        ref _skyStartWidth);
                    _skyStartFirstEdge = true;
                    _skyCompletingStart = false;
                }
                else
                {
                    _skyCompletingStart = true;
                }
                _skyDragging = true;
                _persistentHint = L.Get(
                    "falsum.hint.skyArea.drawStart");
                return;
            }
            if (_skyStage == 1)
            {
                _skyEndTime = IntegerTimeMs(Math.Max(
                    _skyStartTime + 10.0,
                    _pointerTime));
                _skyEndAnchor = SnapPlaneCell(_skyEndTime);
                _skyEndPos = _skyEndAnchor;
                _skyEndWidth = SkyAreaPointWidth;
                ClampSkySpanPreservingWidth(
                    ref _skyEndPos,
                    ref _skyEndWidth);
                _skyStage = 2;
                _skyEndFirstEdge = true;
                _skyCompletingEnd = false;
                _skyDragging = true;
                _persistentHint = L.Get(
                    "falsum.hint.skyArea.drawEnd");
                return;
            }
            if (_skyStage == 2 && _skyEndFirstEdge)
            {
                _skyCompletingEnd = true;
                _skyDragging = true;
            }
        }

        private void CommitSkyDrag()
        {
            _skyDragging = false;
            if (_skyStage == 0)
            {
                var current = SnapPlaneCell(_skyStartTime);
                if (_skyCompletingStart ||
                    Math.Abs(current - _skyStartAnchor) > 0.000001)
                {
                    FinalizeSkyStart(current);
                }
                else
                {
                    _persistentHint =
                        L.Get("falsum.hint.skyArea.drawStart");
                }
                return;
            }
            if (_skyStage != 2)
            {
                return;
            }

            var endCurrent = SnapPlaneCell(_skyEndTime);
            if (_skyCompletingEnd ||
                Math.Abs(endCurrent - _skyEndAnchor) > 0.000001)
            {
                FinalizeSkyEnd(endCurrent);
            }
            else
            {
                _persistentHint =
                    L.Get("falsum.hint.skyArea.drawEnd");
            }
        }

        private void FinalizeSkyStart(double current)
        {
            _skyStartPos =
                (_skyStartAnchor + current) * 0.5;
            _skyStartWidth = Math.Max(
                1.0 / _flickDenominator,
                Math.Abs(current - _skyStartAnchor));
            ClampSkySpanPreservingWidth(
                ref _skyStartPos,
                ref _skyStartWidth);
            _skyStage = 1;
            _skyStartFirstEdge = false;
            _skyCompletingStart = false;
            _persistentHint =
                L.Get("falsum.hint.skyArea.chooseEndPoint");
        }

        private void FinalizeSkyEnd(double current)
        {
            if (RejectNegativeMusicTime(_skyStartTime))
            {
                return;
            }
            _skyEndPos =
                (_skyEndAnchor + current) * 0.5;
            _skyEndWidth = Math.Max(
                1.0 / _flickDenominator,
                Math.Abs(current - _skyEndAnchor));
            ClampSkySpanPreservingWidth(
                ref _skyEndPos,
                ref _skyEndWidth);
            _session.BeginChange(
                L.Get("falsum.history.placeSkyArea"));
            var note = new ChartNote
            {
                Kind = NoteKind.SkyArea,
                TimeMs = _skyStartTime,
                DurationMs = IntegerTimeMs(Math.Max(
                    10.0,
                    _skyEndTime - _skyStartTime)),
                StartPos = _skyStartPos,
                StartWidth = _skyStartWidth,
                StartCellCount = _flickDenominator,
                EndPos = _skyEndPos,
                EndWidth = _skyEndWidth,
                EndCellCount = _flickDenominator,
                LeftEasing = 0,
                RightEasing = 0,
                GroupId = NextSkyGroupId(),
            };
            NormalizeSkyNoteToCells(note);
            _chart.Notes.Add(note);
            _skyStage = 0;
            _skyStartFirstEdge = false;
            _skyEndFirstEdge = false;
            _skyCompletingStart = false;
            _skyCompletingEnd = false;
            _persistentHint = L.Get(
                "falsum.hint.skyArea.place");
            FinishEditAdded(note);
        }

        private void FinishEdit()
        {
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                RefreshEditSelectionAfterAdd();
                RefreshEditorVisuals();
            }
        }

        private void FinishEditAdded(ChartNote note)
        {
            if (note != null &&
                RejectNegativeMusicTime(note.TimeMs))
            {
                _session.CancelChange();
                RefreshEditorVisuals();
                return;
            }
            if (!_session.CommitChange())
            {
                return;
            }
            _session.NotifyChanged();
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                if (ReferenceEquals(_chart.Notes[i], note))
                {
                    note.Id = i;
                    break;
                }
            }
            NormalizeNoteFractions(note);
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.AddNoteVisual(note);
            }
            _host.UpdateChartPlaybackFrame(_playheadMs);
            RefreshEditSelectionAfterAdd();
            UpdateViews();
        }

        private void RefreshPresentationOnly()
        {
            RebuildEditorTimeline();
            _host.UpdateChartPlaybackFrame(_playheadMs);
            UpdateViews();
        }

        private void RefreshEditSelectionAfterAdd()
        {
            if (_selected.Count == 1 &&
                !_chart.Notes.Contains(_selected[0]))
            {
                _selected.Clear();
            }
            RefreshPropertyPanel();
        }

        private void RefreshEditorVisuals()
        {
            var performanceStartedAt =
                FalsumPerformanceMonitor.Timestamp();
            var wasPlaying = _host.Clock != null &&
                             _host.Clock.IsPlaying;
            RebuildEditorTimeline();
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                _chart.Notes[i].Id = i;
            }
            if (!wasPlaying)
            {
                _host.PrepareChartPlaybackState(_playheadMs);
            }
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.SyncFromChart();
            }
            // SyncFromChart already rebuilt every SkyArea chain from the
            // current GroupId, so the pending rebuild is satisfied.
            _skyGroupingDirty = false;
            RefreshEditorAtChartTime(_playheadMs);
            UpdateViews();
            _performanceMonitor.RecordEditorRefresh(
                FalsumPerformanceMonitor.ElapsedMilliseconds(
                    performanceStartedAt));
        }

        private void RefreshEditorAfterHistory()
        {
            var wasPlaying = _host.Clock != null &&
                             _host.Clock.IsPlaying;
            RebuildEditorTimeline();
            if (!wasPlaying)
            {
                _host.PrepareChartPlaybackState(_playheadMs);
            }
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.SyncFromChart();
            }
            // SyncFromChart already rebuilt every SkyArea chain from the
            // current GroupId, so the pending rebuild is satisfied.
            _skyGroupingDirty = false;
            RefreshEditorAtChartTime(_playheadMs);
            RefreshPropertyPanel();
            UpdateViews();
        }

        private void ShowRadial(Vector2 screenPosition)
        {
            if (_selected.Count > 0)
            {
                _radial.ShowSelection(
                    screenPosition,
                    HandleRadialAction);
                return;
            }
            if (_mode == EditorMode.PlaceTap ||
                _mode == EditorMode.PlaceHold ||
                _mode == EditorMode.PlaceFlick ||
                _mode == EditorMode.PlaceSkyArea ||
                _mode == EditorMode.PointPlace)
            {
                _radial.ShowPlacement(
                    screenPosition,
                    _grid.Enabled,
                    HandleRadialAction);
                return;
            }
            _radial.ShowRoot(
                screenPosition,
                _clipboard.Count > 0,
                _grid.Enabled,
                HandleRadialAction);
        }

        private void HandleRadialAction(FalsumRadialAction action)
        {
            switch (action)
            {
                case FalsumRadialAction.SegmentSelect:
                    ClearSelection();
                    _mode = EditorMode.SegmentSelect;
                    _segmentHasStart = false;
                    _persistentHint = L.Get(
                        "falsum.hint.segment.selectStart");
                    break;
                case FalsumRadialAction.PointPlace:
                    SetMode(EditorMode.PointPlace);
                    _persistentHint = L.Get(
                        "falsum.hint.pointPlace.chooseType");
                    break;
                case FalsumRadialAction.PointPlaceNone:
                    SetMode(EditorMode.PointPlace);
                    _persistentHint = L.Get(
                        "falsum.hint.pointPlace.noCreation");
                    break;
                case FalsumRadialAction.PlaceTap:
                    SetMode(EditorMode.PlaceTap);
                    _persistentHint = L.Get(
                        "falsum.hint.tap.place");
                    break;
                case FalsumRadialAction.PlaceHold:
                    SetMode(EditorMode.PlaceHold);
                    _persistentHint = L.Get(
                        "falsum.hint.hold.place");
                    break;
                case FalsumRadialAction.PlaceFlick:
                    SetMode(EditorMode.PlaceFlick);
                    _persistentHint = L.Get(
                        "falsum.hint.flick.place");
                    break;
                case FalsumRadialAction.PlaceSkyArea:
                    SetMode(EditorMode.PlaceSkyArea);
                    _persistentHint = L.Get(
                        "falsum.hint.skyArea.place");
                    break;
                case FalsumRadialAction.GridToggle:
                    ToggleGrid();
                    break;
                case FalsumRadialAction.Paste:
                    PasteClipboard();
                    break;
                case FalsumRadialAction.Copy:
                    CopySelection();
                    break;
                case FalsumRadialAction.Cut:
                    CutSelection();
                    break;
                case FalsumRadialAction.Mirror:
                    MirrorSelection();
                    break;
                case FalsumRadialAction.MirrorFrontBack:
                    MirrorSelectionFrontBack();
                    break;
                case FalsumRadialAction.ExitPlacement:
                    SetMode(EditorMode.Select);
                    _persistentHint = BuildPlaybackHint();
                    break;
            }
        }

        private void SetMode(EditorMode mode)
        {
            if (_mode == EditorMode.PastePreview &&
                mode != EditorMode.PastePreview)
            {
                ClearPastePreviewState();
            }
            _mode = mode;
            ResetModeInteractionState();
        }

        private void SetPointPlaceMode(EditorMode mode)
        {
            if (mode != EditorMode.PlaceTap &&
                mode != EditorMode.PlaceHold &&
                mode != EditorMode.PlaceFlick &&
                mode != EditorMode.PlaceSkyArea)
            {
                return;
            }
            if (mode == EditorMode.PlaceFlick ||
                mode == EditorMode.PlaceSkyArea)
            {
                // The last chosen denominator survives the session (user
                // decision); only re-seat it on the preference ladder, in case
                // a preferences apply replaced the ladder since it was picked.
                _flickDenominator = FalsumBeatGrid.NearestDenominator(
                    GridDenominatorSequence,
                    _flickDenominator,
                    DefaultFlickDenominator);
                RefreshDenominatorIndex();
            }
            SetMode(mode);
            switch (mode)
            {
                case EditorMode.PlaceTap:
                    ShowHint(
                        L.Get("falsum.hint.pointPlace.tap"),
                        2.0f);
                    break;
                case EditorMode.PlaceHold:
                    ShowHint(
                        L.Get("falsum.hint.pointPlace.hold"),
                        2.0f);
                    break;
                case EditorMode.PlaceFlick:
                    ShowHint(
                        L.Get("falsum.hint.pointPlace.flick"),
                        2.0f);
                    break;
                case EditorMode.PlaceSkyArea:
                    ShowHint(
                        L.Get("falsum.hint.pointPlace.skyArea"),
                        2.0f);
                    break;
            }
        }

        private void ResetModeInteractionState()
        {
            ResetPickCycle();
            _holdPlaced = false;
            _flickHasTime = false;
            _flickDragging = false;
            _skyStage = 0;
            _skyDragging = false;
            _skyStartFirstEdge = false;
            _skyEndFirstEdge = false;
            _skyCompletingStart = false;
            _skyCompletingEnd = false;
            _segmentHasStart = false;
            _draggingSelection = false;
            _selectionDragPending = false;
            _dragStates.Clear();
        }

        private void ToggleGrid()
        {
            SetGridVisibility(!_grid.Enabled);
        }

        private void SetGridVisibility(bool enabled)
        {
            _grid.Enabled = enabled;
            _view.SetGridEnabled(enabled);
            ShowHint(
                L.Get(
                    _grid.Enabled
                        ? "falsum.hint.gridEnabled"
                        : "falsum.hint.gridDisabled"),
                1.8f);
        }

        private void SetMeasureNumberVisibility(bool enabled)
        {
            _showMeasureNumbers = enabled;
            if (_view != null)
            {
                _view.SetMeasureNumbersEnabled(enabled);
            }
            if (_preferences != null &&
                _preferences.Advanced != null)
            {
                _preferences.Advanced.ShowMeasureNumbers = enabled;
                try
                {
                    FalsumPreferencesStore.Save(_preferences);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[Falsum] Measure number preference " +
                        "could not be saved: " +
                        exception.Message);
                }
            }
            ShowHint(
                L.Get(
                    enabled
                        ? "falsum.hint.measureNumbersEnabled"
                        : "falsum.hint.measureNumbersDisabled"),
                1.8f);
            UpdateOverlays();
        }

        private void ToggleTrackSpeedEffects()
        {
            SetTrackSpeedEffectsEnabled(
                !_chart.TrackSpeedEffectsEnabled);
        }

        /// <summary>
        /// Delegate target for <see cref="FalsumEditorView.TrackSpeedEffectsRequested"/>.
        /// <see cref="SetTrackSpeedEffectsEnabled"/> carries default arguments
        /// and therefore cannot bind to <c>Action&lt;bool&gt;</c> directly
        /// (a method group with optional parameters is not convertible); this
        /// one-argument wrapper is what keeps the subscription compilable.
        /// </summary>
        private void ApplyTrackSpeedEffectsToggle(bool enabled)
        {
            SetTrackSpeedEffectsEnabled(enabled);
        }

        /// <summary>
        /// Turns the chart's track() visual effect on or off for the preview
        /// only. The events themselves are neither touched nor re-written;
        /// <see cref="Chart.TrackSpeedEffectsEnabled"/> just decides whether
        /// <c>ScaledTimeAt</c> integrates them.
        ///
        /// The refresh is deliberately narrow: a flag flip cannot change
        /// note ids, group ids, BPM/measure lines or the audio schedule, so
        /// there is nothing to re-sync — only the presentation that reads the
        /// scaled-time map has to be repainted. Rebuilding the timeline or
        /// synchronising the whole chart here would make the toggle hitch on
        /// large charts for no benefit.
        /// </summary>
        private void SetTrackSpeedEffectsEnabled(
            bool enabled,
            bool announce = true,
            bool refresh = true)
        {
            if (_chart == null)
            {
                return;
            }

            var changed = _chart.TrackSpeedEffectsEnabled != enabled;
            _chart.TrackSpeedEffectsEnabled = enabled;
            if (_view != null)
            {
                // The view caches the checked state for the menu entry, so it
                // must be synced even when the flag did not actually change.
                _view.SetTrackSpeedEffectsEnabled(enabled);
            }
            if (changed && refresh)
            {
                RefreshTrackSpeedEffectPreview();
            }
            if (!changed || !announce)
            {
                return;
            }
            ShowHint(
                L.Get(
                    enabled
                        ? "falsum.hint.trackSpeedEffectsEnabled"
                        : "falsum.hint.trackSpeedEffectsDisabled"),
                1.8f);
        }

        /// <summary>
        /// Repaints everything that reads the scaled-time map after the
        /// track-effect switch flips. Playing state is preserved: while the
        /// clock runs the normal per-frame refresh would do this anyway, so
        /// this only has to cover the paused case (the common one when
        /// editing) plus the overlay/picking rebuild.
        /// </summary>
        private void RefreshTrackSpeedEffectPreview()
        {
            if (_host != null)
            {
                if (_host.Clock != null && _host.Clock.IsPlaying)
                {
                    _host.UpdateChartPlaybackFrame(_playheadMs);
                }
                else
                {
                    _host.RefreshEditorAt(_playheadMs);
                }
            }
            UpdateOverlays();
            UpdateViews();
        }

        private void TogglePlayback()
        {
            if (_spaceHeld || _host.Clock == null)
            {
                return;
            }
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_host.Clock.IsPlaying)
            {
                PausePlaybackAt(_host.Clock.TimeMs);
                return;
            }
            PrepareForPlayback();
            _host.BeginChartPlayback(_playheadMs);
            SeekClockToChartTime(_playheadMs);
            _host.Clock.Resume();
            _view.SetPlaybackState(true);
            _host.UpdateChartPlaybackFrame(_playheadMs);
        }

        private void BeginTemporaryPlayback()
        {
            if (_spaceHeld || _host.Clock == null)
            {
                return;
            }
            if (!RequireLoadedPackage())
            {
                return;
            }
            _spaceHeld = true;
            _spaceStartMs = CurrentPlaybackTime();
            _playheadMs = _spaceStartMs;
            PrepareForPlayback();
            if (!_host.Clock.IsPlaying)
            {
                _host.BeginChartPlayback(_spaceStartMs);
                SeekClockToChartTime(_spaceStartMs);
                _host.Clock.Resume();
            }
            _radial.Hide();
            _view.SetPlaybackState(true);
        }

        private void PrepareForPlayback()
        {
            CancelMiddleEdit();
            if (_session != null &&
                _session.HasPendingEdit)
            {
                _session.CancelChange();
            }
            _draggingSelection = false;
            _selectionDragPending = false;
            _dragStates.Clear();
            _flickDragging = false;
            _skyDragging = false;
            _radial.Hide();
        }

        private void EndTemporaryPlayback()
        {
            if (!_spaceHeld)
            {
                return;
            }
            _spaceHeld = false;
            PausePlaybackAt(_spaceStartMs);
        }

        private void PausePlaybackAt(double timeMs)
        {
            if (_host.Clock == null)
            {
                return;
            }
            _playheadMs = Math.Max(
                0.0,
                Math.Min(timeMs, TimelineDuration()));
            _host.Clock.Pause();
            _host.EndChartPlayback();
            _host.PrepareChartPlaybackState(_playheadMs);
            _view.SetPlaybackState(false);
            RefreshEditorAtChartTime(_playheadMs);
            UpdateViews();
        }

        private double CurrentPlaybackTime()
        {
            return _host.Clock != null && _host.Clock.IsPlaying
                ? Math.Max(0.0, _host.Clock.TimeMs)
                : _playheadMs;
        }

        private void PauseForEditInteraction()
        {
            if (_mode == EditorMode.PastePreview)
            {
                CancelPastePreview();
            }
            CancelMiddleEdit();
            if (_session != null && _session.HasPendingEdit)
            {
                _session.CancelChange();
                _draggingSelection = false;
                _selectionDragPending = false;
                _dragStates.Clear();
                _flickDragging = false;
                _skyDragging = false;
                RefreshEditorVisuals();
            }
            if (_host.Clock != null && _host.Clock.IsPlaying)
            {
                PausePlaybackAt(_host.Clock.TimeMs);
            }
        }

        private void SyncClockDelay()
        {
            if (_host.Clock != null)
            {
                _host.Clock.AudioOffsetMs = -_session.DelayMs;
            }
        }

        private void SeekClockToChartTime(double chartTimeMs)
        {
            if (_host.Clock == null)
            {
                return;
            }
            SyncClockDelay();
            _host.Clock.Seek(Math.Max(
                0.0,
                ChartTimeAxis.ToMusicTime(
                    chartTimeMs,
                    _session.DelayMs)));
        }

        private void RefreshEditorAtChartTime(double chartTimeMs)
        {
            SeekClockToChartTime(chartTimeMs);
            _host.UpdateChartPlaybackFrame(chartTimeMs);
        }

        private void Undo()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (!_session.Undo())
            {
                return;
            }
            _selected.Clear();
            RefreshEditorAfterHistory();
        }

        private void Redo()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (!_session.Redo())
            {
                return;
            }
            _selected.Clear();
            RefreshEditorAfterHistory();
        }

        private void DeleteSelection()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_selected.Count == 0)
            {
                return;
            }
            var removed = new List<ChartNote>(_selected);
            _session.BeginChange(
                L.Get("falsum.history.deleteObjects"));
            for (var i = _chart.Notes.Count - 1; i >= 0; i--)
            {
                if (_selected.Contains(_chart.Notes[i]))
                {
                    _chart.Notes.RemoveAt(i);
                }
            }
            _selected.Clear();
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                if (_host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RemoveNoteVisuals(removed);
                }
                _host.UpdateChartPlaybackFrame(_playheadMs);
                RefreshPropertyPanel();
                UpdateViews();
            }
        }

        private void CopySelection()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_mode == EditorMode.PastePreview)
            {
                CancelPastePreview();
            }
            if (_selected.Count == 0)
            {
                return;
            }
            _clipboard.Clear();
            var earliest = double.MaxValue;
            for (var i = 0; i < _selected.Count; i++)
            {
                earliest = Math.Min(earliest, _selected[i].TimeMs);
            }
            for (var i = 0; i < _selected.Count; i++)
            {
                _clipboard.Add(new ClipboardNote
                {
                    Note = CloneNote(_selected[i]),
                    TimeOffsetMs = _selected[i].TimeMs - earliest,
                });
            }
            ShowHint(
                L.Get(
                    "falsum.status.objectsCopied",
                    ("count", _selected.Count)),
                1.8f);
        }

        private void CutSelection()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_selected.Count == 0)
            {
                return;
            }
            CopySelection();
            DeleteSelection();
        }

        private void PasteClipboard()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (_clipboard.Count == 0)
            {
                ShowHint(
                    L.Get("falsum.hint.paste.empty"),
                    1.8f);
                return;
            }

            PauseForEditInteraction();
            CancelMiddleEdit();
            if (_mode != EditorMode.PastePreview)
            {
                _pastePreviewReturnMode = _mode;
            }
            _pastePreviewClipboard.Clear();
            _pastePreviewAnchor = null;
            var earliestOffsetMs = double.MaxValue;
            for (var i = 0; i < _clipboard.Count; i++)
            {
                var source = _clipboard[i];
                if (source == null || source.Note == null)
                {
                    continue;
                }
                var copy = new ClipboardNote
                {
                    Note = CloneNote(source.Note),
                    TimeOffsetMs = source.TimeOffsetMs,
                };
                _pastePreviewClipboard.Add(copy);
                if (copy.TimeOffsetMs < earliestOffsetMs)
                {
                    _pastePreviewAnchor = CloneNote(copy.Note);
                    earliestOffsetMs = copy.TimeOffsetMs;
                }
            }
            if (_pastePreviewClipboard.Count == 0 ||
                _pastePreviewAnchor == null)
            {
                ClearPastePreviewState();
                ShowHint(
                    L.Get("falsum.hint.paste.empty"),
                    1.8f);
                return;
            }
            ClearSelection();
            SetMode(EditorMode.PastePreview);
            _radial.Hide();
            ShowHint(
                L.Get("falsum.hint.pastePreview"),
                2.5f);
            UpdateOverlays();
            UpdateViews();
        }

        private void CommitPastePreview()
        {
            if (_mode != EditorMode.PastePreview)
            {
                return;
            }
            BuildPastePreviewNotes(_pastePreviewNotes);
            if (_pastePreviewNotes.Count == 0)
            {
                CancelPastePreview();
                return;
            }
            if (RejectNegativeMusicTimes(_pastePreviewNotes))
            {
                // The preview deliberately stays open so the user can move it
                // to a legal time; nothing is committed and the borrowed
                // clipboard is released by the cancel path when they give up.
                return;
            }

            var returnMode = _pastePreviewReturnMode;
            _session.BeginChange(
                L.Get("falsum.history.pasteObjects"));
            _selected.Clear();
            var nextId = NextNoteId();
            for (var i = 0; i < _pastePreviewNotes.Count; i++)
            {
                var note = _pastePreviewNotes[i];
                note.Id = nextId++;
                _chart.Notes.Add(note);
                _selected.Add(note);
            }
            _session.CommitChange();
            _session.NotifyChanged();
            if (_host.NoteVisuals != null)
            {
                _host.NoteVisuals.AddNoteVisuals(_selected);
            }
            _host.UpdateChartPlaybackFrame(_playheadMs);
            RefreshPropertyPanel();
            SetMode(returnMode);
            ShowHint(
                L.Get(
                    "falsum.status.objectsPasted",
                    ("count", _selected.Count)),
                1.8f);
            // A bank placement borrowed the clipboard for its own segments;
            // hand the user's own contents back before anything else can
            // observe them (Ctrl+V right after the drop must not paste a
            // SkyArea the user never copied).
            if (_bankPlacementPending)
            {
                TouchPlacedPreset();
                RestoreBankClipboard();
            }
            UpdateOverlays();
            UpdateViews();
        }

        private void CancelPastePreview()
        {
            if (_mode != EditorMode.PastePreview)
            {
                return;
            }
            var returnMode = _pastePreviewReturnMode;
            SetMode(returnMode);
            ShowHint(
                L.Get("falsum.hint.pasteCancelled"),
                1.2f);
            // Cancelling a bank placement must also release the borrowed
            // clipboard, otherwise Ctrl+V would paste the preset later.
            RestoreBankClipboard();
            UpdateOverlays();
            UpdateViews();
        }

        private void BuildPastePreviewNotes(List<ChartNote> destination)
        {
            destination.Clear();
            if (_mode != EditorMode.PastePreview ||
                _pastePreviewClipboard.Count == 0 ||
                _pastePreviewAnchor == null)
            {
                return;
            }

            var targetTime = IntegerTimeMs(
                _hasPointer ? _pointerTime : _playheadMs);
            var timeDelta = targetTime - _pastePreviewAnchor.TimeMs;
            var earliestTime = double.MaxValue;
            for (var i = 0;
                 i < _pastePreviewClipboard.Count;
                 i++)
            {
                earliestTime = Math.Min(
                    earliestTime,
                    _pastePreviewClipboard[i].Note.TimeMs +
                    timeDelta);
            }
            if (earliestTime < 0.0)
            {
                timeDelta -= earliestTime;
            }

            for (var i = 0; i < _pastePreviewClipboard.Count; i++)
            {
                var source = _pastePreviewClipboard[i].Note;
                var note = CloneNote(source);
                note.TimeMs = IntegerTimeMs(
                    note.TimeMs + timeDelta);
                if (note.Kind == NoteKind.Flick)
                {
                    ClampNoteSkyBoundsPreservingWidth(note);
                    NormalizeSkyNoteToCells(note);
                }
                else if (note.Kind == NoteKind.SkyArea)
                {
                    ClampNoteSkyBoundsPreservingWidth(note);
                    NormalizeSkyNoteToCells(note);
                }
                destination.Add(note);
            }
        }

        private IList<FalsumEditorGhost> BuildPastePreviewGhosts()
        {
            _pastePreviewGhosts.Clear();
            BuildPastePreviewNotes(_pastePreviewNotes);
            var color = new Color(0.28f, 0.92f, 1.0f, 0.42f);
            for (var i = 0; i < _pastePreviewNotes.Count; i++)
            {
                var note = _pastePreviewNotes[i];
                _pastePreviewGhosts.Add(new FalsumEditorGhost
                {
                    Visible = true,
                    Kind = note.Kind,
                    TimeMs = note.TimeMs,
                    EndTimeMs = note.EndTimeMs,
                    StartPos = note.StartPos,
                    EndPos = note.EndPos,
                    StartWidth = note.StartWidth,
                    EndWidth = note.EndWidth,
                    Lane = note.StartLane,
                    WidthLanes = note.WidthLanes,
                    FlickDirection = note.FlickDirection,
                    LeftEasing = note.LeftEasing,
                    RightEasing = note.RightEasing,
                    Color = color,
                    EndAsPoint =
                        note.Kind == NoteKind.SkyArea &&
                        note.EndWidth <= SkyAreaPointWidth * 2.0,
                    // Paste previews keep the rim; only point-place drops it.
                    DrawRim = true,
                });
            }
            return _pastePreviewGhosts;
        }

        private void ClearPastePreviewState()
        {
            _pastePreviewClipboard.Clear();
            _pastePreviewNotes.Clear();
            _pastePreviewGhosts.Clear();
            _pastePreviewAnchor = null;
            _pastePreviewReturnMode = EditorMode.Select;
            // Defensive: any teardown of the preview ends a bank placement.
            RestoreBankClipboard();
        }

        private int NextNoteId()
        {
            var next = 0;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                next = Math.Max(next, _chart.Notes[i].Id + 1);
            }
            return next;
        }

        private void ToggleSelectedFlickColors()
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            var flicks = new List<ChartNote>();
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                if (note != null && note.Kind == NoteKind.Flick)
                {
                    flicks.Add(note);
                }
            }
            if (flicks.Count == 0)
            {
                ShowHint(
                    L.Get("falsum.hint.selectFlickFirst"),
                    1.8f);
                return;
            }

            _session.BeginChange(
                L.Get("falsum.history.toggleFlickColor"));
            for (var i = 0; i < flicks.Count; i++)
            {
                flicks[i].FlickDirection =
                    flicks[i].FlickDirection == 16 ? 4 : 16;
            }
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                if (_host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RefreshNoteVisuals(flicks);
                }
                _host.UpdateChartPlaybackFrame(_playheadMs);
                RefreshPropertyPanel();
                UpdateViews();
                ShowHint(
                    L.Get(
                        "falsum.hint.flickColor",
                        ("color", L.Get(
                            flicks[0].FlickDirection == 16
                                ? "falsum.color.yellowLeft"
                                : "falsum.color.greenRight"))),
                    1.5f);
            }
        }

        private void MirrorSelection()
        {
            ApplySelectionTransform(
                L.Get("falsum.history.mirrorObjects"),
                note => MirrorNoteHorizontal(
                    note,
                    _disableOutOfBoundsClamping));
        }

        private void MirrorSelectionFrontBack()
        {
            if (_selected.Count == 0)
            {
                return;
            }

            var firstTime = double.PositiveInfinity;
            var lastTime = double.NegativeInfinity;
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                firstTime = Math.Min(firstTime, note.TimeMs);
                lastTime = Math.Max(lastTime, note.EndTimeMs);
            }
            if (double.IsInfinity(firstTime) ||
                double.IsInfinity(lastTime) ||
                lastTime < firstTime)
            {
                return;
            }

            var reflectionTimeMs = firstTime + lastTime;
            ApplySelectionTransform(
                L.Get("falsum.history.mirrorFrontBack"),
                note => MirrorNoteFrontBack(
                    note,
                    reflectionTimeMs));
        }

        private void ApplySelectionTransform(
            string label,
            Action<ChartNote> transform)
        {
            if (!RequireLoadedPackage() ||
                _selected.Count == 0 ||
                transform == null)
            {
                return;
            }

            _session.BeginChange(label);
            for (var i = 0; i < _selected.Count; i++)
            {
                transform(_selected[i]);
            }
            if (_session.CommitChange())
            {
                _session.NotifyChanged();
                if (_host.NoteVisuals != null)
                {
                    _host.NoteVisuals.RefreshNoteVisuals(_selected);
                }
                _host.UpdateChartPlaybackFrame(_playheadMs);
                UpdateOverlays();
                RefreshPropertyPanel();
                UpdateViews();
                ShowHint(
                    L.Get(
                        "falsum.status.transformComplete",
                        ("action", label),
                        ("count", _selected.Count)),
                    1.5f);
            }
        }

        public static void MirrorNoteHorizontal(ChartNote note)
        {
            MirrorNoteHorizontal(note, false);
        }

        private static void MirrorNoteHorizontal(
            ChartNote note,
            bool disableClamping)
        {
            if (note == null)
            {
                return;
            }
            if (note.Kind == NoteKind.Tap ||
                note.Kind == NoteKind.Hold)
            {
                note.StartLane = 5 -
                                 (note.StartLane +
                                  Mathf.Max(1, note.WidthLanes) - 1);
                return;
            }
            if (note.Kind == NoteKind.Flick)
            {
                note.StartPos = 1.0 - note.StartPos;
                note.FlickDirection =
                    note.FlickDirection == 16 ? 4 : 16;
            }
            else
            {
                note.StartPos = 1.0 - note.StartPos;
                note.EndPos = 1.0 - note.EndPos;
                var leftEasing = note.LeftEasing;
                note.LeftEasing = note.RightEasing;
                note.RightEasing = leftEasing;
            }
            if (!disableClamping)
            {
                ClampNoteSkyBoundsPreservingWidthInternal(note);
            }
            NormalizeSkyNoteToCells(note, !disableClamping);
        }

        public static void MirrorNoteFrontBack(
            ChartNote note,
            double reflectionTimeMs)
        {
            if (note == null ||
                double.IsNaN(reflectionTimeMs) ||
                double.IsInfinity(reflectionTimeMs))
            {
                return;
            }

            var originalEndTimeMs = note.EndTimeMs;
            note.TimeMs = Math.Max(
                0.0,
                reflectionTimeMs - originalEndTimeMs);
            if (note.Kind != NoteKind.SkyArea)
            {
                return;
            }

            var startPos = note.StartPos;
            note.StartPos = note.EndPos;
            note.EndPos = startPos;

            var startWidth = note.StartWidth;
            note.StartWidth = note.EndWidth;
            note.EndWidth = startWidth;

            var startCellCount = note.StartCellCount;
            note.StartCellCount = note.EndCellCount;
            note.EndCellCount = startCellCount;

            note.LeftEasing =
                InvertSkyAreaTimeEasing(note.LeftEasing);
            note.RightEasing =
                InvertSkyAreaTimeEasing(note.RightEasing);
        }

        private static int InvertSkyAreaTimeEasing(int easing)
        {
            // Chart easing values: 0 = line, 1 = out, 2 = in.
            if (easing == 1)
            {
                return 2;
            }
            if (easing == 2)
            {
                return 1;
            }
            return easing;
        }

        private void OnEventsPanelVisibilityChanged(bool open)
        {
            if (!open)
            {
                return;
            }
            RefreshEventsPanel();
        }

        private void RefreshEventsPanel()
        {
            if (_view == null)
            {
                return;
            }
            var events = new List<ChartEvent>(_chart.Events.Count + 1);
            events.Add(UpdateChartBaseEvent());
            events.AddRange(_chart.Events);
            _view.ShowEvents(
                events,
                _currentDifficulty == 0 ||
                _currentDifficulty == 1);
        }

        private ChartEvent UpdateChartBaseEvent()
        {
            if (_chartBaseEvent == null)
            {
                _chartBaseEvent = new ChartEvent
                {
                    Kind = ChartEventKind.Bpm,
                    IsChartBase = true,
                };
            }
            _chartBaseEvent.Kind = ChartEventKind.Bpm;
            _chartBaseEvent.IsChartBase = true;
            _chartBaseEvent.TimeMs = 0.0;
            _chartBaseEvent.Bpm = _chart.Bpm;
            _chartBaseEvent.Division = _chart.Division;
            return _chartBaseEvent;
        }

        private void AddChartEvent(ChartEventKind kind)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            CreateChartEvent(kind);
        }

        private ChartEvent CreateChartEvent(ChartEventKind kind)
        {
            PauseForEditInteraction();
            if (kind == ChartEventKind.LaneLayout &&
                _currentDifficulty != 0 &&
                _currentDifficulty != 1)
            {
                return null;
            }

            var time = IntegerTimeMs(Math.Max(0.0, _playheadMs));
            if (RejectNegativeMusicTime(time))
            {
                return null;
            }
            var chartEvent = new ChartEvent
            {
                Kind = kind,
                TimeMs = time,
            };
            if (kind == ChartEventKind.Bpm)
            {
                chartEvent.Bpm = _chart.BpmAt(time);
                chartEvent.Division = _chart.DivisionAt(time);
            }
            else if (kind == ChartEventKind.TrackSpeed)
            {
                chartEvent.Multiplier =
                    _chart.SpeedMultiplierAt(time);
            }
            else
            {
                chartEvent.Lane = 1;
                chartEvent.Enabled = true;
            }

            if (!_session.AddEvent(
                    chartEvent,
                    L.Get("falsum.history.addEvent")))
            {
                return null;
            }
            _view.SelectEvent(chartEvent);
            RefreshEditorVisuals();
            return chartEvent;
        }

        private void SubmitChartEvent(
            FalsumEventEditSubmission submission)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (submission == null || submission.Target == null)
            {
                return;
            }
            var chartEvent = submission.Target;
            if (!chartEvent.IsChartBase &&
                !_chart.Events.Contains(chartEvent))
            {
                _view.ShowEventValidation(
                    chartEvent,
                    L.Get("falsum.error.event.missing"));
                return;
            }
            if (chartEvent.Kind == ChartEventKind.LaneLayout &&
                _currentDifficulty != 0 &&
                _currentDifficulty != 1)
            {
                _view.ShowEventValidation(
                    chartEvent,
                    L.Get("falsum.error.event.laneUnavailable"));
                return;
            }

            var time = 0.0;
            if (!chartEvent.IsChartBase &&
                (!TryParseEventNumber(submission.Time, out time) ||
                 time < 0.0))
            {
                _view.ShowEventValidation(
                    chartEvent,
                    L.Get("falsum.error.event.timeInvalid"));
                return;
            }
            if (!chartEvent.IsChartBase)
            {
                time = IntegerTimeMs(time);
                if (RejectNegativeMusicTime(time))
                {
                    _view.ShowEventValidation(
                        chartEvent,
                        L.Get(
                            "falsum.error.event.negativeMusicTime"));
                    return;
                }
            }

            double bpm = 0.0;
            double division = 0.0;
            double multiplier = 0.0;
            if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                if (!TryParseEventNumber(submission.Bpm, out bpm) ||
                    bpm <= 0.0)
                {
                    _view.ShowEventValidation(
                        chartEvent,
                        L.Get("falsum.error.event.bpmInvalid"));
                    return;
                }
                if (!TryParseEventNumber(
                        submission.Division,
                        out division) ||
                    division <= 0.0)
                {
                    _view.ShowEventValidation(
                        chartEvent,
                        L.Get("falsum.error.event.divisionInvalid"));
                    return;
                }
            }
            else if (chartEvent.Kind ==
                     ChartEventKind.TrackSpeed)
            {
                if (!TryParseEventNumber(
                        submission.Multiplier,
                        out multiplier))
                {
                    _view.ShowEventValidation(
                        chartEvent,
                        L.Get("falsum.error.event.multiplierInvalid"));
                    return;
                }
            }
            else if (submission.Lane != 0 &&
                     submission.Lane != 1 &&
                     submission.Lane != 4 &&
                     submission.Lane != 5)
            {
                _view.ShowEventValidation(
                    chartEvent,
                    L.Get("falsum.error.event.laneInvalid"));
                return;
            }

            PauseForEditInteraction();
            _session.BeginChange(
                chartEvent.IsChartBase
                    ? L.Get("falsum.history.editInitialBpm")
                    : L.Get("falsum.history.editEvent"));
            if (!chartEvent.IsChartBase)
            {
                chartEvent.TimeMs = time;
            }
            if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                chartEvent.Bpm = bpm;
                chartEvent.Division = division;
                if (chartEvent.IsChartBase)
                {
                    _chart.Bpm = bpm;
                    _chart.Division = division;
                }
            }
            else if (chartEvent.Kind ==
                     ChartEventKind.TrackSpeed)
            {
                chartEvent.Multiplier = multiplier;
            }
            else
            {
                chartEvent.Lane = submission.Lane;
                chartEvent.Enabled = submission.Enabled;
            }
            RebuildEditorTimeline();
            if (_session.CommitChange())
            {
                RefreshEditorVisuals();
            }
            else
            {
                _view.ShowEventValidation(chartEvent, string.Empty);
            }
        }

        private void DeleteChartEvent(ChartEvent chartEvent)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (chartEvent == null || chartEvent.IsChartBase)
            {
                return;
            }
            PauseForEditInteraction();
            if (_session.RemoveEvent(
                    chartEvent,
                    L.Get("falsum.history.removeEvent")))
            {
                RefreshEditorVisuals();
            }
        }

        private void DuplicateChartEvent(ChartEvent chartEvent)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            DuplicateChartEventInternal(chartEvent);
        }

        private ChartEvent DuplicateChartEventInternal(
            ChartEvent chartEvent)
        {
            if (chartEvent == null || chartEvent.IsChartBase)
            {
                return null;
            }
            if (RejectNegativeMusicTime(chartEvent.TimeMs))
            {
                return null;
            }
            PauseForEditInteraction();
            var copy = CloneChartEvent(chartEvent);
            if (!_session.AddEvent(
                    copy,
                    L.Get("falsum.history.duplicateEvent")))
            {
                return null;
            }
            _view.SelectEvent(copy);
            RefreshEditorVisuals();
            return copy;
        }

        private void JumpToChartEvent(ChartEvent chartEvent)
        {
            if (chartEvent == null)
            {
                return;
            }
            Seek(chartEvent.TimeMs);
        }

        private static bool TryParseEventNumber(
            string text,
            out double value)
        {
            return double.TryParse(
                       text,
                       NumberStyles.Float,
                       Invariant,
                       out value) &&
                   !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private static ChartEvent CloneChartEvent(ChartEvent source)
        {
            return new ChartEvent
            {
                Kind = source.Kind,
                IsChartBase = source.IsChartBase,
                TimeMs = source.TimeMs,
                Multiplier = source.Multiplier,
                Bpm = source.Bpm,
                Division = source.Division,
                Lane = source.Lane,
                Enabled = source.Enabled,
            };
        }

        private void SetProperty(string key, string value)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            PauseForEditInteraction();
            if (_selected.Count != 1)
            {
                return;
            }
            var note = _selected[0];
            _session.BeginChange(
                L.Get("falsum.history.editNoteProperties"));
            try
            {
                if (key == "time_ms")
                {
                    var requestedTime = ParseDouble(value);
                    if (RejectNegativeMusicTime(requestedTime))
                    {
                        _session.CancelChange();
                        RefreshPropertyPanel();
                        return;
                    }
                    if (note.Kind == NoteKind.SkyArea)
                    {
                        SetSkyAreaStartTimePreservingEnd(
                            note,
                            requestedTime);
                    }
                    else
                    {
                        note.TimeMs =
                            IntegerTimeMs(requestedTime);
                    }
                }
                else if (key == "end_time_ms")
                {
                    // Only HOLD and SKYAREA own a second endpoint. A TAP has no
                    // duration at all, so this row must never be able to grow
                    // one -- giving a Tap a duration is the "Tap becomes a
                    // HOLD" behaviour that was removed on purpose.
                    if (note.Kind == NoteKind.Tap)
                    {
                        RefreshPropertyPanel();
                        return;
                    }
                    note.DurationMs = IntegerTimeMs(Math.Max(
                        1.0,
                        ParseDouble(value) - note.TimeMs));
                }
                else if (key == "width_lanes")
                {
                    note.WidthLanes = Mathf.Clamp(
                        Mathf.RoundToInt((float)ParseDouble(value)),
                        1,
                        4);
                    note.StartLane = ClampLaneForWidth(
                        note.StartLane,
                        note.WidthLanes,
                        false);
                }
                else if (key == "start_lane")
                {
                    note.StartLane = ClampLaneForWidth(
                        Mathf.RoundToInt((float)ParseDouble(value)),
                        note.WidthLanes,
                        false);
                }
                else if (key == "flick_position")
                {
                    ParseFraction(
                        value,
                        out var numerator,
                        out var denominator);
                    note.StartCellCount = denominator;
                    note.EndCellCount = denominator;
                    note.StartPos = numerator / denominator;
                }
                else if (key == "flick_width")
                {
                    ParseFraction(
                        value,
                        out var numerator,
                        out var denominator);
                    note.StartCellCount = denominator;
                    note.EndCellCount = denominator;
                    note.StartWidth = Math.Max(
                        0.0,
                        numerator / denominator);
                }
                else if (key == "flick_direction")
                {
                    note.FlickDirection =
                        value.IndexOf(
                            L.Get("falsum.direction.left"),
                            StringComparison.Ordinal) >= 0 ||
                        value.Trim() == "16"
                            ? 16
                            : 4;
                }
                else if (key == "sky_start")
                {
                    ParseFraction(
                        value,
                        out var numerator,
                        out var denominator);
                    note.StartCellCount = denominator;
                    note.StartPos = numerator / denominator;
                }
                else if (key == "sky_start_width")
                {
                    ParseFraction(
                        value,
                        out var numerator,
                        out var denominator);
                    note.StartCellCount = denominator;
                    note.StartWidth = Math.Max(
                        1.0 / denominator,
                        numerator / denominator);
                }
                else if (key == "sky_end")
                {
                    ParseFraction(
                        value,
                        out var numerator,
                        out var denominator);
                    note.EndCellCount = denominator;
                    note.EndPos = numerator / denominator;
                }
                else if (key == "sky_end_width")
                {
                    ParseFraction(
                        value,
                        out var numerator,
                        out var denominator);
                    note.EndCellCount = denominator;
                    note.EndWidth = Math.Max(
                        1.0 / denominator,
                        numerator / denominator);
                }
                else if (key == "left_ease")
                {
                    note.LeftEasing = ParseEase(value);
                }
                else if (key == "right_ease")
                {
                    note.RightEasing = ParseEase(value);
                }
                var clamped = false;
                if (IsSkyBoundsProperty(key))
                {
                    clamped = ClampEditedSkyBounds(key, note);
                    NormalizeSkyNoteToCells(note);
                }
                else
                {
                    NormalizeNoteFractions(note);
                }
                if (_session.CommitChange())
                {
                    _session.NotifyChanged();
                    if (_host.NoteVisuals != null)
                    {
                        _host.NoteVisuals.RefreshNoteVisual(note);
                    }
                    _host.UpdateChartPlaybackFrame(_playheadMs);
                    UpdateViews();
                    RefreshPropertyPanel();
                    if (clamped)
                    {
                        ShowHint(
                            L.Get(
                                "falsum.hint.skyBoundsClamped"),
                            2.4f);
                    }
                }
            }
            catch (Exception exception)
            {
                _session.CancelChange();
                ShowHint(
                    L.Get(
                        "falsum.error.propertyInvalid",
                        ("error", exception.Message)),
                    3.0f);
            }
        }

        private void SetTitle(string value)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            value = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowHint(
                    L.Get("falsum.error.titleRequired"),
                    2.5f);
                RefreshSongInfoCard(false);
                return;
            }
            if (string.Equals(
                    _manifest.Title,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }
            ApplyMetadataChange(
                L.Get("falsum.history.editTitle"),
                delegate { _manifest.SetTitle(value); });
        }

        private void SetArtist(string value)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            value = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowHint(
                    L.Get("falsum.error.artistRequired"),
                    2.5f);
                RefreshSongInfoCard(false);
                return;
            }
            if (string.Equals(
                    _manifest.Artist,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }
            ApplyMetadataChange(
                L.Get("falsum.history.editArtist"),
                delegate { _manifest.SetArtist(value); });
        }

        private void SetRating(string value)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            var info = _manifest.FindDifficulty(_currentDifficulty);
            if (info == null ||
                !int.TryParse(value, out var rating))
            {
                RefreshSongInfoCard(false);
                return;
            }
            if (info.Rating == rating)
            {
                return;
            }
            ApplyMetadataChange(
                L.Get("falsum.history.editRating"),
                delegate { info.SetRating(rating); });
        }

        private void SetChartDesigner(string value)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            var info = _manifest.FindDifficulty(_currentDifficulty);
            value = value ?? string.Empty;
            if (info != null &&
                !string.Equals(
                    info.ChartDesigner,
                    value,
                    StringComparison.Ordinal))
            {
                ApplyMetadataChange(
                    L.Get("falsum.history.editChartDesigner"),
                    delegate { info.SetChartDesigner(value); });
            }
        }

        private void SetJacketDesigner(string value)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            var info = _manifest.FindDifficulty(_currentDifficulty);
            value = value ?? string.Empty;
            if (info != null &&
                !string.Equals(
                    info.JacketDesigner,
                    value,
                    StringComparison.Ordinal))
            {
                ApplyMetadataChange(
                    L.Get("falsum.history.editJacketDesigner"),
                    delegate { info.SetJacketDesigner(value); });
            }
        }

        private void ApplyMetadataChange(
            string label,
            Action change)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (change == null)
            {
                return;
            }
            if (_session.HasPendingEdit)
            {
                ShowHint(
                    L.Get("falsum.hint.finishCurrentEdit"),
                    2.0f);
                RefreshSongInfoCard(false);
                return;
            }
            _session.BeginChange(label);
            change();
            if (_session.CommitChange())
            {
                RefreshSongInfoCard(false);
            }
            else
            {
                _session.CancelChange();
                RefreshSongInfoCard(false);
            }
        }

        private void SwitchDifficulty(int difficulty)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            if (difficulty == _currentDifficulty)
            {
                return;
            }
            if (_package == null)
            {
                ShowHint(
                    L.Get(
                        "falsum.hint.noSwitchablePackage"),
                    2.5f);
                return;
            }
            try
            {
                var wasDirty = _isDirty;
                var previousDifficulty = _currentDifficulty;
                StoreCurrentChartInPackage();
                var created = !_package.Charts.ContainsKey(
                    difficulty);
                var loadedChart = created
                    ? CreateChartCopyForDifficulty(difficulty)
                    : _package.LoadChart(difficulty);
                var targetText = ChartWriter.Write(
                    loadedChart,
                    loadedChart.DelayMs,
                    _manifest.SongId,
                    difficulty);
                _package.SetChartText(difficulty, targetText);
                _session.Replace(loadedChart, loadedChart.DelayMs);
                _currentDifficulty = difficulty;
                var availabilityChanged =
                    UpdateDifficultyAvailability(
                    _package,
                    difficulty,
                    targetText,
                    false);
                _isDirty =
                    wasDirty || created || availabilityChanged;
                RefreshSongInfoCard(false);
                _playheadMs = 0.0;
                _selected.Clear();
                RefreshEditorVisuals();
                if (created)
                {
                    ShowHint(
                        L.Get(
                            "falsum.status.difficultyCreated",
                            ("difficulty", DifficultyName(difficulty)),
                            ("source", DifficultyName(
                                previousDifficulty))),
                        3.5f);
                }
            }
            catch (Exception exception)
            {
                _lastDifficultySwitchError = exception.ToString();
                Debug.LogException(exception);
                ShowHint(
                    L.Get(
                        "falsum.error.switchDifficultyFailed",
                        ("error", exception.Message)),
                    4.0f);
            }
        }

        private Chart CreateChartCopyForDifficulty(
            int difficulty)
        {
            var source = _session.Chart;
            var chart = new Chart
            {
                Bpm = source.Bpm,
                Division = source.Division,
                DelayMs = _session.DelayMs,
                SongId = _manifest.SongId,
                Difficulty = difficulty,
                SourcePath = _package.SourcePath,
            };
            for (var i = 0; i < source.Events.Count; i++)
            {
                var evt = source.Events[i];
                chart.Events.Add(new ChartEvent
                {
                    Kind = evt.Kind,
                    IsChartBase = evt.IsChartBase,
                    TimeMs = evt.TimeMs,
                    Multiplier = evt.Multiplier,
                    Bpm = evt.Bpm,
                    Division = evt.Division,
                    Lane = evt.Lane,
                    Enabled = evt.Enabled,
                    SourceLine = evt.SourceLine,
                });
            }
            SyncMeasureLineGenerationEnd(chart);
            chart.RebuildTimeline();
            return chart;
        }

        public string DebugSwitchDifficulty(int difficulty)
        {
            _lastDifficultySwitchError = null;
            SwitchDifficulty(difficulty);
            return _lastDifficultySwitchError ?? "pass";
        }

        public string DebugRunDifficultySwitchChecks()
        {
            if (_package == null)
            {
                return "skip no-package";
            }
            var original = _currentDifficulty;
            var issues = new List<string>();
            for (var difficulty = 0; difficulty < 4; difficulty++)
            {
                if (difficulty == _currentDifficulty ||
                    !_package.Charts.ContainsKey(difficulty))
                {
                    continue;
                }
                var result = DebugSwitchDifficulty(difficulty);
                if (result == "pass")
                {
                    continue;
                }
                var firstLine = result.Split('\n')[0];
                issues.Add(difficulty + ":" + firstLine);
            }
            if (_currentDifficulty != original)
            {
                var restore = DebugSwitchDifficulty(original);
                if (restore != "pass")
                {
                    issues.Add(
                        original + ":restore:" +
                        restore.Split('\n')[0]);
                }
            }
            return issues.Count == 0
                ? "pass"
                : "fail " + string.Join(" | ", issues.ToArray());
        }

        private void StoreCurrentChartInPackage()
        {
            if (_package == null)
            {
                return;
            }
            _package.SetChartText(
                _currentDifficulty,
                ChartWriter.Write(
                    _session.Chart,
                    _session.DelayMs,
                    _manifest.SongId,
                    _currentDifficulty));
        }

        private void SetFlowSpeed(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                ShowHint(
                    L.Get("falsum.error.flowMustBeFinite"),
                    2.0f);
                return;
            }
            _flowSpeed = Mathf.Clamp(speed, 1.0f, 10.0f);
            _flowSpeed = Mathf.Round(_flowSpeed * 10.0f) / 10.0f;
            _host.SetFlowSpeed(_flowSpeed);
            if (_world2D != null)
            {
                _world2D.SetFlowSpeed(_flowSpeed);
            }
            _view.SetSpeed(_flowSpeed);
            _view.SetSongSpeed(_flowSpeed);
            _host.UpdateChartPlaybackFrame(_playheadMs);
            UpdateViews();
        }

        private void AdjustFlowSpeed(float delta)
        {
            SetFlowSpeed(_flowSpeed + delta);
            ShowHint(
                L.Get(
                    "falsum.status.flowValue",
                    ("value", _flowSpeed.ToString(
                        "0.0",
                        Invariant))),
                1.2f);
        }

        private void ToggleBorderlessFullscreen()
        {
            var borderless =
                Screen.fullScreenMode !=
                FullScreenMode.FullScreenWindow;
            var refresh = Mathf.RoundToInt(
                (float)Screen.currentResolution
                    .refreshRateRatio.value);
            int width;
            int height;
            if (borderless)
            {
                _windowedSizeBeforeFullscreen = new Vector2Int(
                    Mathf.Max(1280, Screen.width),
                    Mathf.Max(720, Screen.height));
                _hasWindowedSizeBeforeFullscreen = true;
                width = Mathf.Max(1280, Display.main.systemWidth);
                height = Mathf.Max(720, Display.main.systemHeight);
            }
            else if (_hasWindowedSizeBeforeFullscreen)
            {
                width = _windowedSizeBeforeFullscreen.x;
                height = _windowedSizeBeforeFullscreen.y;
            }
            else
            {
                width = Mathf.Max(
                    1280,
                    _preferences.Display.ResolutionWidth);
                height = Mathf.Max(
                    720,
                    _preferences.Display.ResolutionHeight);
            }
            _preferences.Display.BorderlessFullscreen = borderless;
            _preferences.Display.ResolutionWidth = width;
            _preferences.Display.ResolutionHeight = height;
            Screen.SetResolution(
                width,
                height,
                borderless
                    ? FullScreenMode.FullScreenWindow
                    : FullScreenMode.Windowed,
                new RefreshRate
                {
                    numerator = (uint)Mathf.Max(0, refresh),
                    denominator = 1,
                });
            StartCoroutine(CenterWindowNextFrame());
            ShowHint(
                borderless
                    ? L.Get("falsum.status.fullscreenEntered")
                    : L.Get("falsum.status.fullscreenExited"),
                1.5f);
        }

        private IEnumerator CenterWindowNextFrame()
        {
            yield return null;
            FalsumWindowPlacement.CenterMainWindow();
        }

        private void SetFlowSpeedFromText(string text)
        {
            if (TryParseFiniteFloat(text, out var value))
            {
                SetFlowSpeed(value);
            }
            else
            {
                _view.SetSpeed(_flowSpeed);
            }
        }

        private void SetDelay(string text)
        {
            // Kept for the preference page and for a package loaded with a
            // legacy delay; the top bar no longer edits it (the audio
            // preprocess window replaced that entry point).
            if (!RequireLoadedPackage())
            {
                return;
            }
            PauseForEditInteraction();
            if (!TryParseFiniteDouble(text, out var value))
            {
                ShowHint(
                    L.Get("falsum.error.delayMustBeFinite"),
                    2.0f);
                _view.SetDelay(_session.DelayMs);
                return;
            }
            if (ChartTimeAxis.ContainsNegativeMusicTime(
                    _chart,
                    value))
            {
                _view.SetDelay(_session.DelayMs);
                ShowNegativeMusicTimeWarning();
                return;
            }
            _session.BeginChange(
                L.Get("falsum.history.adjustDelay"));
            _session.DelayMs = value;
            SyncMeasureLineGenerationEnd(_chart);
            _session.CommitChange();
            _view.SetDelay(value);
            _session.NotifyChanged();
            RefreshEditorAtChartTime(_playheadMs);
            UpdateViews();
        }

        private void SetBaseBpm(string text)
        {
            if (!RequireLoadedPackage())
            {
                return;
            }
            PauseForEditInteraction();
            if (!TryParseEventNumber(text, out var value) ||
                value <= 0.0)
            {
                _view.SetBaseBpm(_chart.Bpm);
                ShowHint(
                    L.Get("falsum.error.baseBpmPositive"),
                    2.0f);
                return;
            }
            if (Math.Abs(_chart.Bpm - value) <= 0.000001)
            {
                _view.SetBaseBpm(_chart.Bpm);
                return;
            }

            _session.BeginChange(
                L.Get("falsum.history.editBaseBpm"));
            _chart.Bpm = value;
            RebuildEditorTimeline();
            if (_session.CommitChange())
            {
                RefreshEditorVisuals();
            }
            else
            {
                _view.SetBaseBpm(_chart.Bpm);
            }
        }

        private void SetSubdivision(string text)
        {
            PauseForEditInteraction();
            if (!int.TryParse(text, out var value))
            {
                _view.SetSubdivision(_subdivision);
                return;
            }
            ApplySubdivision(value);
        }

        private void ApplySubdivision(int value)
        {
            _subdivision = Mathf.Max(1, value);
            _grid.Subdivision = _subdivision;
            _view.SetSubdivision(_subdivision);
            RefreshPresentationOnly();
        }

        private void CycleRate()
        {
            _rateIndex = (_rateIndex + 1) % PlaybackRates.Length;
            var rate = PlaybackRates[_rateIndex];
            _host.SetPlaybackRate(rate);
            _view.SetPlaybackRate(rate);
            ShowHint(
                L.Get(
                    "falsum.status.playbackRate",
                    ("rate", rate.ToString("0.##", Invariant))),
                1.5f);
        }

        private void SeekFromText(string text)
        {
            if (TryParseFiniteDouble(text, out var value))
            {
                Seek(value);
            }
        }

        private void SeekNormalized(float normalized)
        {
            Seek(normalized * TimelineDuration());
        }

        private void Seek(double timeMs)
        {
            if (double.IsNaN(timeMs) || double.IsInfinity(timeMs))
            {
                ShowHint(
                    L.Get("falsum.error.playbackTimeFinite"),
                    2.0f);
                return;
            }
            var targetMs = Math.Max(
                0.0,
                Math.Min(timeMs, TimelineDuration()));
            var wasPlaying =
                _host.Clock != null &&
                _host.Clock.IsPlaying;
            _playheadMs = targetMs;
            if (wasPlaying)
            {
                _host.BeginChartPlayback(targetMs);
                SeekClockToChartTime(targetMs);
                _host.UpdateChartPlaybackFrame(targetMs);
                _view.SetPlaybackState(true);
                UpdateViews();
                return;
            }

            if (_host.Clock != null)
            {
                _host.Clock.Pause();
            }
            _host.EndChartPlayback();
            _host.PrepareChartPlaybackState(_playheadMs);
            RefreshEditorAtChartTime(_playheadMs);
            _view.SetPlaybackState(false);
            UpdateViews();
        }

        private void Adjust2DZoom(int direction)
        {
            if (_viewMode != FalsumViewMode.Orthographic2D ||
                _world2D == null)
            {
                return;
            }
            var scale = direction < 0 ? 0.8 : 1.25;
            _world2D.VisibleDurationMs =
                Math.Max(
                    1000.0,
                    Math.Min(
                        60000.0,
                        _world2D.VisibleDurationMs * scale));
            ShowHint(
                L.Get(
                    "falsum.status.twoDTimeRange",
                    ("seconds", (_world2D.VisibleDurationMs / 1000.0)
                        .ToString("0.##", Invariant))),
                1.0f);
            RefreshEditorAtChartTime(_playheadMs);
            UpdateViews();
        }

        private void ScrollTimeline(double direction)
        {
            if (!_grid.Enabled)
            {
                Seek(_playheadMs + direction * 100.0);
                return;
            }

            double adjacent;
            if (_grid.TryGetAdjacentGridLine(
                    _chart,
                    _playheadMs,
                    direction >= 0.0 ? 1 : -1,
                    out adjacent))
            {
                Seek(adjacent);
                return;
            }
            Seek(
                _playheadMs +
                direction * _grid.StepMs(_chart, _playheadMs));
        }

        private void JumpToSelectedNote()
        {
            if (_selected.Count == 1)
            {
                Seek(_selected[0].TimeMs);
            }
        }

        private void UpdateOverlays()
        {
            var performanceStartedAt =
                FalsumPerformanceMonitor.Timestamp();
            double? planeTime = null;
            if (_toolTimePickActive)
            {
                planeTime = UpdateToolTimePickCandidate();
            }
            else if (_toolNotePickActive)
            {
                planeTime = _playheadMs;
            }
            else if (IsMiddleEditActive)
            {
                double middlePlaneTime;
                if (TryGetMiddlePlaneTime(out middlePlaneTime))
                {
                    planeTime = middlePlaneTime;
                }
            }
            else if (_mode == EditorMode.SegmentSelect)
            {
                planeTime = _hasPointer
                    ? _pointerTime
                    : (_segmentHasStart
                        ? _segmentStart
                        : _playheadMs);
            }
            else if (_mode == EditorMode.PlaceHold)
            {
                planeTime = null;
            }
            else if (_mode == EditorMode.Select && _draggingSelection)
            {
                // A drag only changes TimeMs, so the dragged note's own model
                // already follows the pointer. The time plane marks the exact
                // landing slot the drop will snap to, which is what the user
                // needs when the model is off-screen or hard to compare with
                // the old position.
                planeTime = _selectionDragAnchor != null
                    ? _selectionDragAnchor.TimeMs
                    : (_hasPointer ? _pointerTime : _playheadMs);
            }
            else if (_mode == EditorMode.PlaceFlick)
            {
                planeTime = _flickHasTime
                    ? _flickTime
                    : (_hasPointer ? _pointerTime : _playheadMs);
            }
            else if (_mode == EditorMode.PlaceSkyArea)
            {
                planeTime = _skyStage == 0
                    ? (_skyStartFirstEdge
                        ? _skyStartTime
                        : (_hasPointer ? _pointerTime : _playheadMs))
                    : (_skyStage == 1
                        ? (_hasPointer ? _pointerTime : _skyEndTime)
                        : _skyEndTime);
            }
            else if (_mode == EditorMode.PastePreview)
            {
                planeTime = _hasPointer
                    ? _pointerTime
                    : _playheadMs;
            }
            else if (_anchorPlaneVisible)
            {
                // A plane-facing anchor drag runs a middle-edit body, so it
                // anchors the plane at the same time the equivalent middle
                // channel would. Without this the ruler would have no plane to
                // draw on during the drag.
                planeTime = _anchorPlaneTimeMs;
            }

            UpdateModePlane(planeTime ?? _playheadMs);
            var ghost = BuildGhost();
            var pasteGhosts = BuildPastePreviewGhosts();
            // Playback must not override the user's explicit grid visibility.
            var showGrid = _grid.Enabled;
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                _world2D.SetViewport(
                    _view.GetChartViewportScreenRect());
                _world2D.Refresh(
                    _playheadMs,
                    showGrid,
                    _selected,
                    planeTime,
                    _flickDenominator,
                    ghost,
                    pasteGhosts,
                    ModePlaneTint(),
                    _modePlaneFocusLine,
                    _modePlaneDivisions,
                    ModeGridColor());
            }
            else
            {
                _world.Refresh(
                    _playheadMs,
                    showGrid,
                    _selected,
                    planeTime,
                    _flickDenominator,
                    ghost,
                    pasteGhosts,
                    ModePlaneTint(),
                    BuildRulerState(),
                    _showMeasureNumbers &&
                    !_preferencesPreviewActive);
            }
            _performanceMonitor.RecordOverlay(
                FalsumPerformanceMonitor.ElapsedMilliseconds(
                    performanceStartedAt));
        }

        /// <summary>
        /// The plane description an plane-facing anchor drag contributes. The
        /// anchor layer has already resolved everything it is allowed to resolve
        /// (invariant A-18): the operation, the shared grid and the dragged
        /// normalized position. Only the presentation lives here, so the drag
        /// reads with the same labels and hint as the equivalent middle edit.
        /// </summary>
        private bool TryGetAnchorPlaneState(
            out FalsumModePlaneKind kind,
            out string prompt,
            out string modeLabel,
            out string keyHint,
            out int divisions,
            out int focusLine,
            out double planeTimeMs)
        {
            kind = FalsumModePlaneKind.None;
            prompt = string.Empty;
            modeLabel = string.Empty;
            keyHint = string.Empty;
            divisions = 0;
            focusLine = -1;
            planeTimeMs = _playheadMs;
            if (!_anchorPlaneVisible)
            {
                return false;
            }
            var active = _anchorDrag != null ? _anchorDrag.Note : null;
            if (active == null)
            {
                return false;
            }
            kind = FalsumModePlaneKind.AnchorEdit;
            modeLabel = MiddleEditModeLabel(_anchorPlaneMode);
            divisions = _anchorPlaneGrid;
            keyHint = L.Get("falsum.modePlane.gridAdjustHint");
            planeTimeMs = _anchorPlaneTimeMs;
            switch (_anchorPlaneMode)
            {
                case FalsumMiddleEditMode.Boundary:
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.boundary");
                    break;
                case FalsumMiddleEditMode.StartEnd:
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.startEnd");
                    break;
                default:
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.position");
                    break;
            }
            if (!float.IsNaN(_anchorPlaneStartNormalized))
            {
                // The focus tick is the cell the dragged endpoint is landing
                // on, which is the same normalized position the ruler marker
                // uses -- reading it from the shared state keeps focus and
                // marker on one lattice.
                focusLine = GridLineAt(
                    _anchorPlaneStartNormalized,
                    divisions);
            }
            return true;
        }

        private void UpdateModePlane(double planeTimeMs)
        {
            if (_modePlane == null)
            {
                return;
            }

            var kind = FalsumModePlaneKind.None;
            var prompt = string.Empty;
            var modeLabel = string.Empty;
            var keyHint = string.Empty;
            var divisions = 0;
            var focusLine = -1;
            FalsumModePlaneKind middleKind;
            string middlePrompt;
            string middleModeLabel;
            string middleKeyHint;
            int middleDivisions;
            int middleFocusLine;
            double middlePlaneTime;
            if (_toolNotePickActive)
            {
                kind = FalsumModePlaneKind.SegmentSelect;
                prompt = L.Get(
                    "falsum.modePlane.toolNotePick",
                    ("count", _selected.Count),
                    ("min",
                        _toolNotePickRequest != null
                            ? _toolNotePickRequest.MinCount
                            : 0),
                    ("max",
                        _toolNotePickRequest != null &&
                        _toolNotePickRequest.MaxCount != int.MaxValue
                            ? _toolNotePickRequest.MaxCount
                                .ToString(Invariant)
                            : L.Get(
                                "falsum.tools.notePick.unbounded")));
                modeLabel = L.Get(
                    "falsum.modePlane.toolNotePickLabel");
            }
            else if (_toolTimePickActive)
            {
                kind = FalsumModePlaneKind.SegmentSelect;
                prompt =
                    L.Get(
                        "falsum.modePlane.toolTimePick",
                        ("time", (long)Math.Round(planeTimeMs)));
                modeLabel = L.Get(
                    "falsum.modePlane.segmentSelect");
            }
            else if (TryGetAnchorPlaneState(
                    out FalsumModePlaneKind anchorKind,
                    out string anchorPrompt,
                    out string anchorModeLabel,
                    out string anchorKeyHint,
                    out int anchorDivisions,
                    out int anchorFocusLine,
                    out double anchorPlaneTime))
            {
                kind = anchorKind;
                prompt = anchorPrompt;
                modeLabel = anchorModeLabel;
                keyHint = anchorKeyHint;
                divisions = anchorDivisions;
                focusLine = anchorFocusLine;
                planeTimeMs = anchorPlaneTime;
            }
            else if (TryGetMiddlePlaneState(
                    out middleKind,
                    out middlePrompt,
                    out middleModeLabel,
                    out middleKeyHint,
                    out middleDivisions,
                    out middleFocusLine,
                    out middlePlaneTime))
            {
                kind = middleKind;
                prompt = middlePrompt;
                modeLabel = middleModeLabel;
                keyHint = middleKeyHint;
                divisions = middleDivisions;
                focusLine = middleFocusLine;
                planeTimeMs = middlePlaneTime;
            }
            else
            {
                switch (_mode)
                {
                    case EditorMode.SegmentSelect:
                        kind = FalsumModePlaneKind.SegmentSelect;
                        prompt = _segmentHasStart
                            ? L.Get(
                                "falsum.hint.segment.selectEndAgain")
                            : L.Get(
                                "falsum.hint.segment.selectStart");
                        break;
                    case EditorMode.PlaceFlick:
                        kind = FalsumModePlaneKind.Flick;
                        divisions = _flickDenominator;
                        prompt = L.Get(
                            "falsum.modePlane.flickDrag");
                        if (_flickDragging)
                        {
                            focusLine = GridLineAt(
                                (float)_flickCurrent,
                                divisions);
                        }
                        else if (_hasPointer)
                        {
                            focusLine = GridLineAt(
                                (float)SnapPlaneCell(planeTimeMs),
                                divisions);
                        }
                        break;
                    case EditorMode.PlaceSkyArea:
                        kind = FalsumModePlaneKind.SkyArea;
                        divisions = _flickDenominator;
                        if (_skyStage == 0)
                        {
                            prompt = L.Get(
                                "falsum.modePlane.skyStartDrag");
                            if (_skyDragging)
                            {
                                focusLine = GridLineAt(
                                    (float)SnapPlaneCell(_skyStartTime),
                                    divisions);
                            }
                            else if (_hasPointer)
                            {
                                focusLine = GridLineAt(
                                    (float)SnapPlaneCell(planeTimeMs),
                                    divisions);
                            }
                        }
                        else if (_skyStage == 1)
                        {
                            prompt = L.Get(
                                _viewMode ==
                                FalsumViewMode.Orthographic2D
                                    ? "falsum.modePlane.skyEndPosition2D"
                                    : "falsum.modePlane.skyEndPosition");
                            if (_hasPointer)
                            {
                                focusLine = GridLineAt(
                                    (float)SnapPlaneCell(planeTimeMs),
                                    divisions);
                            }
                        }
                        else
                        {
                            prompt = L.Get(
                                "falsum.modePlane.skyEndDrag");
                            if (_skyDragging)
                            {
                                focusLine = GridLineAt(
                                    (float)SnapPlaneCell(_skyEndTime),
                                    divisions);
                            }
                            else
                            {
                                focusLine = GridLineAt(
                                    (float)_skyEndAnchor,
                                    divisions);
                            }
                        }
                        break;
                }
            }

            if (kind == FalsumModePlaneKind.SegmentSelect &&
                !_toolNotePickActive)
            {
                modeLabel = L.Get(
                    "falsum.modePlane.segmentSelect");
            }
            else if (kind == FalsumModePlaneKind.Flick)
            {
                modeLabel = L.Get("falsum.modePlane.flick");
                keyHint = L.Get(
                    "falsum.modePlane.gridAdjustHint");
            }
            else if (kind == FalsumModePlaneKind.SkyArea)
            {
                modeLabel = L.Get("falsum.modePlane.skyArea");
                keyHint = L.Get(
                    "falsum.modePlane.gridAdjustHint");
            }

            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                if (kind == FalsumModePlaneKind.Flick)
                {
                    prompt = L.Get(
                        "falsum.modePlane.flick2D");
                }
                else if (kind == FalsumModePlaneKind.MiddleEdit &&
                         prompt == L.Get(
                             "falsum.middleEdit.prompt.startEnd"))
                {
                    prompt = L.Get(
                        "falsum.middleEdit.prompt.startEnd2D");
                }
            }

            _modePlane.SetState(
                kind,
                prompt,
                divisions,
                focusLine,
                modeLabel,
                keyHint);
            _modePlaneFocusLine = focusLine;
            _modePlaneDivisions = divisions;
            if (_viewMode == FalsumViewMode.Orthographic2D)
            {
                var overlayPosition = Vector2.zero;
                var showOverlay = kind != FalsumModePlaneKind.None &&
                                  _world2D.ScreenPositionAt(
                                      planeTimeMs,
                                      0.5f,
                                      _playheadMs,
                                      out overlayPosition);
                _view.SetTwoDModeOverlay(
                    showOverlay,
                    showOverlay ? overlayPosition.y : 0.0f,
                    modeLabel,
                    prompt,
                    divisions > 0
                        ? "GRID: " + divisions
                        : string.Empty,
                    keyHint,
                    ModePlaneTint());
            }
            else
            {
                _view.SetTwoDModeOverlay(
                    false,
                    0.0f,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Color.white);
                if (kind != FalsumModePlaneKind.None)
                {
                    _modePlane.SetWorldPose(
                        _world.ModePlaneLocalPosition(
                            planeTimeMs,
                            _playheadMs,
                            ResolveRulerSurface()),
                        _world.ModePlaneWorldHeight());
                }
            }
        }

        private Color ModePlaneTint()
        {
            if (_toolTimePickActive)
            {
                return new Color(1.0f, 0.62f, 0.25f, 0.05f);
            }
            if (IsMiddleEditActive)
            {
                return MiddlePlaneTint();
            }
            if (_anchorPlaneVisible)
            {
                // An anchor drag runs one of the middle-edit bodies, so it
                // reads with that body's tint rather than the Select tint a
                // drag would otherwise inherit.
                return _anchorPlaneMode == FalsumMiddleEditMode.Boundary
                    ? new Color(0.20f, 0.90f, 1.0f, 0.05f)
                    : new Color(1.0f, 0.62f, 0.25f, 0.05f);
            }
            if (_mode == EditorMode.Select && _draggingSelection)
            {
                return new Color(1.0f, 0.55f, 0.20f, 0.06f);
            }
            switch (_mode)
            {
                case EditorMode.SegmentSelect:
                    return new Color(1.0f, 0.62f, 0.25f, 0.05f);
                case EditorMode.ToolNotePick:
                    return new Color(0.31f, 0.56f, 0.85f, 0.05f);
                case EditorMode.PlaceFlick:
                    return new Color(0.20f, 0.90f, 1.0f, 0.035f);
                case EditorMode.PlaceSkyArea:
                    return new Color(0.72f, 0.46f, 1.0f, 0.05f);
                default:
                    return new Color(0.55f, 0.75f, 0.82f, 0.03f);
            }
        }

        private Color ModeGridColor()
        {
            if (IsMiddleEditActive)
            {
                return MiddleGridColor();
            }
            if (_anchorPlaneVisible)
            {
                // The drag runs a middle-edit body, so its ruler uses the same
                // grid colour that body would paint with.
                return _anchorPlaneMode == FalsumMiddleEditMode.Boundary
                    ? new Color(1.0f, 0.86f, 0.18f, 0.98f)
                    : new Color(1.0f, 0.43f, 0.47f, 0.98f);
            }
            switch (_mode)
            {
                case EditorMode.PlaceFlick:
                    return new Color(1.0f, 0.43f, 0.47f, 0.98f);
                case EditorMode.PlaceSkyArea:
                    return new Color(1.0f, 0.86f, 0.18f, 0.98f);
                default:
                    return new Color(1.0f, 0.70f, 0.24f, 0.95f);
            }
        }

        private int ComputeFlickEditGrid()
        {
            var result = 1;
            var found = false;
            for (var i = 0; i < _selected.Count; i++)
            {
                var note = _selected[i];
                if (note == null || note.Kind != NoteKind.Flick)
                {
                    continue;
                }
                var grid = note.StartCellCount > 0
                    ? note.StartCellCount
                    : note.EndCellCount;
                if (grid <= 0)
                {
                    grid = 24;
                }
                found = true;
                result = LeastCommonMultiple(result, grid);
                if (result >= MaxEditableFlickGrid)
                {
                    return MaxEditableFlickGrid;
                }
            }
            return found
                ? Mathf.Clamp(result, 1, MaxEditableFlickGrid)
                : 0;
        }

        private static int LeastCommonMultiple(int left, int right)
        {
            left = Mathf.Max(1, left);
            right = Mathf.Max(1, right);
            var divisor = GreatestCommonDivisor(left, right);
            var result = (long)left / divisor * right;
            return result > MaxEditableFlickGrid
                ? MaxEditableFlickGrid
                : (int)result;
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                var remainder = left % right;
                left = right;
                right = remainder;
            }
            return Mathf.Max(1, left);
        }

        private static double IntegerTimeMs(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    "value",
                    L.Get("falsum.error.timeMustBeFinite"));
            }
            return Math.Round(
                Math.Max(0.0, value),
                MidpointRounding.AwayFromZero);
        }

        private static double SnapNormalizedPosition(
            double value,
            int divisions,
            bool allowOutOfBounds)
        {
            divisions = Mathf.Clamp(
                divisions,
                1,
                MaxEditableFlickGrid);
            var clamped = allowOutOfBounds
                ? value
                : Math.Max(0.0, Math.Min(1.0, value));
            return Math.Round(
                       clamped * divisions,
                       MidpointRounding.AwayFromZero) /
                   divisions;
        }

        private static bool IsSkyBoundsProperty(string key)
        {
            return key == "flick_position" ||
                   key == "flick_width" ||
                   key == "sky_start" ||
                   key == "sky_start_width" ||
                   key == "sky_end" ||
                   key == "sky_end_width";
        }

        private void NormalizeSkyNoteToCells(ChartNote note)
        {
            NormalizeSkyNoteToCells(
                note,
                !_disableOutOfBoundsClamping);
        }

        private static void NormalizeSkyNoteToCells(
            ChartNote note,
            bool clampToBounds)
        {
            if (note == null ||
                (note.Kind != NoteKind.Flick &&
                 note.Kind != NoteKind.SkyArea))
            {
                return;
            }

            NormalizeNoteFractions(note);
            if (note.Kind == NoteKind.Flick)
            {
                var cells = Mathf.Clamp(
                    note.StartCellCount,
                    1,
                    FalsumFraction.MaxDenominator);
                var centerCells = (int)Math.Round(
                    note.StartPos * cells,
                    MidpointRounding.AwayFromZero);
                var widthCells = Math.Max(
                    0,
                    (int)Math.Round(
                        note.StartWidth * cells,
                        MidpointRounding.AwayFromZero));
                note.StartCellCount = cells;
                note.EndCellCount = cells;
                if (clampToBounds)
                {
                    var span = FalsumSkyBounds.ClampCellSpan(
                        centerCells,
                        widthCells,
                        cells);
                    note.StartPos = (double)span.Center / cells;
                    note.StartWidth = (double)span.Width / cells;
                }
                else
                {
                    note.StartPos = (double)centerCells / cells;
                    note.StartWidth = (double)widthCells / cells;
                }
                return;
            }

            NormalizeSkyEndpointToCells(
                ref note.StartPos,
                ref note.StartWidth,
                ref note.StartCellCount,
                clampToBounds);
            NormalizeSkyEndpointToCells(
                ref note.EndPos,
                ref note.EndWidth,
                ref note.EndCellCount,
                clampToBounds);
        }

        private static void NormalizeSkyEndpointToCells(
            ref double position,
            ref double width,
            ref int cellCount,
            bool clampToBounds)
        {
            var cells = Mathf.Clamp(
                cellCount,
                1,
                FalsumFraction.MaxDenominator);
            var centerCells = (int)Math.Round(
                position * cells,
                MidpointRounding.AwayFromZero);
            var widthCells = Math.Max(
                1,
                (int)Math.Round(
                    width * cells,
                    MidpointRounding.AwayFromZero));
            if (!clampToBounds)
            {
                cellCount = cells;
                position = (double)centerCells / cells;
                width = (double)widthCells / cells;
                return;
            }
            var span = FalsumSkyBounds.ClampCellSpan(
                centerCells,
                widthCells,
                cells);
            if (span.Width == 0 && cells < FalsumFraction.MaxDenominator)
            {
                cells *= 2;
                centerCells = (int)Math.Round(
                    position * cells,
                    MidpointRounding.AwayFromZero);
                widthCells = Math.Max(
                    1,
                    (int)Math.Round(
                        width * cells,
                        MidpointRounding.AwayFromZero));
                span = FalsumSkyBounds.ClampCellSpan(
                    centerCells,
                    widthCells,
                    cells);
            }
            cellCount = cells;
            position = (double)span.Center / cells;
            width = (double)span.Width / cells;
        }

        private static void SetSkyAreaStartTimePreservingEnd(
            ChartNote note,
            double requestedStart)
        {
            var originalEnd = note.TimeMs + note.DurationMs;
            var newStart = IntegerTimeMs(
                Math.Max(0.0, requestedStart));
            note.TimeMs = Math.Min(
                newStart,
                Math.Max(0.0, originalEnd - 1.0));
            note.DurationMs = Math.Max(
                1.0,
                originalEnd - note.TimeMs);
        }

        private bool ClampEditedSkyBounds(
            string key,
            ChartNote note)
        {
            if (_disableOutOfBoundsClamping)
            {
                return false;
            }
            if (key == "flick_position")
            {
                return ClampFlickBounds(note, false);
            }
            if (key == "flick_width")
            {
                return ClampFlickBounds(note, true);
            }
            if (key == "sky_start")
            {
                return ClampSkyStartBounds(note, false);
            }
            if (key == "sky_start_width")
            {
                return ClampSkyStartBounds(note, true);
            }
            if (key == "sky_end")
            {
                return ClampSkyEndBounds(note, false);
            }
            if (key == "sky_end_width")
            {
                return ClampSkyEndBounds(note, true);
            }
            return false;
        }

        private static bool ClampFlickBounds(
            ChartNote note,
            bool preserveCenter)
        {
            var span = preserveCenter
                ? FalsumSkyBounds.ClampSpanPreservingCenter(
                    note.StartPos,
                    note.StartWidth)
                : FalsumSkyBounds.ClampSpanPreservingWidth(
                    note.StartPos,
                    note.StartWidth);
            note.StartPos = span.Center;
            note.StartWidth = span.Width;
            note.EndCellCount = note.StartCellCount;
            return span.Clamped;
        }

        private static bool ClampSkyStartBounds(
            ChartNote note,
            bool preserveCenter)
        {
            var span = preserveCenter
                ? FalsumSkyBounds.ClampSpanPreservingCenter(
                    note.StartPos,
                    note.StartWidth)
                : FalsumSkyBounds.ClampSpanPreservingWidth(
                    note.StartPos,
                    note.StartWidth);
            note.StartPos = span.Center;
            note.StartWidth = span.Width;
            return span.Clamped;
        }

        private static bool ClampSkyEndBounds(
            ChartNote note,
            bool preserveCenter)
        {
            var span = preserveCenter
                ? FalsumSkyBounds.ClampSpanPreservingCenter(
                    note.EndPos,
                    note.EndWidth)
                : FalsumSkyBounds.ClampSpanPreservingWidth(
                    note.EndPos,
                    note.EndWidth);
            note.EndPos = span.Center;
            note.EndWidth = span.Width;
            return span.Clamped;
        }

        private bool ClampNoteSkyBoundsPreservingWidth(
            ChartNote note)
        {
            return !_disableOutOfBoundsClamping &&
                   ClampNoteSkyBoundsPreservingWidthInternal(note);
        }

        private static bool ClampNoteSkyBoundsPreservingWidthInternal(
            ChartNote note)
        {
            if (note == null)
            {
                return false;
            }
            if (note.Kind == NoteKind.Flick)
            {
                return ClampFlickBounds(note, false);
            }
            if (note.Kind != NoteKind.SkyArea)
            {
                return false;
            }
            var startClamped = ClampSkyStartBounds(note, false);
            var endClamped = ClampSkyEndBounds(note, false);
            return startClamped || endClamped;
        }

        private bool ClampSkySpanPreservingWidth(
            ref double center,
            ref double width)
        {
            if (_disableOutOfBoundsClamping)
            {
                return false;
            }
            var span = FalsumSkyBounds.ClampSpanPreservingWidth(
                center,
                width);
            center = span.Center;
            width = span.Width;
            return span.Clamped;
        }

        private void ClampGhostSkyBounds(
            ref FalsumEditorGhost ghost)
        {
            if (ghost.Kind == NoteKind.Flick)
            {
                ClampSkySpanPreservingWidth(
                    ref ghost.StartPos,
                    ref ghost.StartWidth);
                ghost.EndPos = ghost.StartPos;
                ghost.EndWidth = ghost.StartWidth;
            }
            else if (ghost.Kind == NoteKind.SkyArea)
            {
                ClampSkySpanPreservingWidth(
                    ref ghost.StartPos,
                    ref ghost.StartWidth);
                ClampSkySpanPreservingWidth(
                    ref ghost.EndPos,
                    ref ghost.EndWidth);
            }
        }

        private static double SnapNormalizedCell(
            float normalized,
            int divisions)
        {
            divisions = Mathf.Clamp(
                divisions,
                1,
                MaxEditableFlickGrid);
            var clamped = Mathf.Clamp01(normalized);
            return Math.Round(
                       clamped * divisions,
                       MidpointRounding.AwayFromZero) /
                   divisions;
        }

        private static int GridLineAt(float normalized, int divisions)
        {
            divisions = Mathf.Clamp(
                divisions,
                1,
                MaxEditableFlickGrid);
            var line = (int)Math.Round(
                Mathf.Clamp01(normalized) * divisions,
                MidpointRounding.AwayFromZero);
            return Mathf.Clamp(line, 0, divisions);
        }

        private FalsumEditorGhost BuildGhost()
        {
            var ghost = default(FalsumEditorGhost);
            if (_mode == EditorMode.PlaceFlick &&
                !_flickDragging)
            {
                return ghost;
            }
            var hasLockedFlick = _mode == EditorMode.PlaceFlick &&
                                 _flickHasTime;
            var hasLockedSky = _mode == EditorMode.PlaceSkyArea &&
                               (_skyStage > 0 || _skyStartFirstEdge);
            if (!_hasPointer && !hasLockedFlick && !hasLockedSky)
            {
                return ghost;
            }
            ghost.Visible = true;
            // Point-place shows the shape only; the rim/width box around a
            // SkyArea ghost is dropped there (see ResolveGuideNormalized for
            // the matching through-line removal).
            ghost.DrawRim = _mode != EditorMode.PlaceSkyArea;
            ghost.Color = _mode == EditorMode.PlaceSkyArea
                ? new Color(0.72f, 0.48f, 1.0f, 0.42f)
                : new Color(0.28f, 0.92f, 1.0f, 0.38f);
            if (_mode == EditorMode.PlaceTap)
            {
                ghost.Kind = NoteKind.Tap;
                ghost.TimeMs = _pointerTime;
                ghost.Lane = ClampLaneForWidth(
                    _pointerLane,
                    _tapWidth,
                    false);
                ghost.WidthLanes = _tapWidth;
            }
            else if (_mode == EditorMode.PlaceHold)
            {
                ghost.Kind = NoteKind.Hold;
                ghost.TimeMs = _holdPlaced ? _holdStart : _pointerTime;
                ghost.EndTimeMs = Math.Max(
                    ghost.TimeMs + 10.0,
                    _pointerTime);
                ghost.WidthLanes = _holdPlaced
                    ? _holdWidth
                    : Mathf.Clamp(_tapWidth, 1, 4);
                ghost.Lane = _holdPlaced
                    ? _holdLane
                    : ClampLaneForWidth(
                        _pointerLane,
                        ghost.WidthLanes,
                        false);
            }
            else if (_mode == EditorMode.PlaceFlick)
            {
                ghost.Kind = NoteKind.Flick;
                ghost.TimeMs = _flickHasTime
                    ? _flickTime
                    : _pointerTime;
                double left;
                double right;
                if (_flickDragging)
                {
                    left = Math.Min(_flickStart, _flickCurrent);
                    right = Math.Max(_flickStart, _flickCurrent);
                }
                else
                {
                    var cell = SnapPlaneCell(ghost.TimeMs);
                    left = cell - 0.5 / _flickDenominator;
                    right = cell + 0.5 / _flickDenominator;
                }
                ghost.StartPos = (left + right) * 0.5;
                ghost.EndPos = ghost.StartPos;
                ghost.StartWidth = Math.Max(
                    1.0 / _flickDenominator,
                    right - left);
                ghost.EndWidth = ghost.StartWidth;
                ghost.FlickDirection =
                    _flickDragging && _flickStart > _flickCurrent
                        ? 16
                        : 4;
            }
            else if (_mode == EditorMode.PlaceSkyArea)
            {
                ghost.Kind = NoteKind.SkyArea;
                if (_skyStage == 0)
                {
                    ghost.TimeMs = _skyStartFirstEdge
                        ? _skyStartTime
                        : (_hasPointer ? _pointerTime : _skyStartTime);
                    // Before the second edge exists this is a POINT, so the
                    // preview must be a thin sliver at that time. A full second
                    // of span drew a large box that read as an already-committed
                    // SkyArea rather than the cursor's landing spot.
                    ghost.EndTimeMs = ghost.TimeMs + SkyAreaPointSpanMs;
                    if (_skyStartFirstEdge)
                    {
                        ghost.StartPos = _skyDragging
                            ? _skyStartPos
                            : _skyStartAnchor;
                        ghost.StartWidth = _skyDragging
                            ? Math.Max(
                                SkyAreaPointWidth,
                                _skyStartWidth)
                            : SkyAreaPointWidth;
                    }
                    else
                    {
                        ghost.StartPos = SnapPlaneCell(ghost.TimeMs);
                        ghost.StartWidth = SkyAreaPointWidth;
                    }
                    ghost.EndPos = ghost.StartPos;
                    ghost.EndWidth = ghost.StartWidth;
                }
                else if (_skyStage == 1)
                {
                    ghost.TimeMs = _skyStartTime;
                    ghost.EndTimeMs = _hasPointer
                        ? Math.Max(
                            _skyStartTime + 10.0,
                            _pointerTime)
                        : _skyStartTime + 1000.0;
                    ghost.StartPos = _skyStartPos;
                    ghost.StartWidth = _skyStartWidth;
                    ghost.EndPos = _hasPointer
                        ? SnapPlaneCell(_pointerTime)
                        : _skyStartPos;
                    ghost.EndWidth = SkyAreaPointWidth;
                    ghost.EndAsPoint = true;
                }
                else
                {
                    ghost.TimeMs = _skyStartTime;
                    ghost.EndTimeMs = _skyEndTime;
                    ghost.StartPos = _skyStartPos;
                    ghost.StartWidth = _skyStartWidth;
                    var endIsPoint = _skyEndFirstEdge &&
                                     !_skyDragging;
                    ghost.EndPos = endIsPoint
                        ? _skyEndAnchor
                        : _skyEndPos;
                    ghost.EndWidth = endIsPoint
                        ? SkyAreaPointWidth
                        : _skyEndWidth;
                    ghost.EndAsPoint = endIsPoint;
                }
            }
            else
            {
                ghost.Visible = false;
            }
            ClampGhostSkyBounds(ref ghost);
            return ghost;
        }

        private void RefreshSongInfoCard(bool replaceCover)
        {
            if (_view == null)
            {
                return;
            }
            if (_package == null || _manifest == null)
            {
                _view.SetSongInfo(null, null, null);
                if (replaceCover)
                {
                    _view.SetSongInfoCover(null);
                }
                return;
            }
            _view.SetSongInfo(
                _manifest,
                _manifest.FindDifficulty(_currentDifficulty),
                AvailableDifficultyIndices());
            if (replaceCover)
            {
                _view.SetSongInfoCover(
                    _package != null ? _package.CoverBytes : null);
            }
        }

        private IList<int> AvailableDifficultyIndices()
        {
            var result = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                result.Add(i);
            }
            return result;
        }

        private void UpdateHint()
        {
            if (Time.unscaledTimeAsDouble < _hintUntil)
            {
                return;
            }
            _view.SetHint(string.Empty);
        }

        private void UpdateViews(
            double? knownDurationMs = null)
        {
            if (_view == null)
            {
                return;
            }
            // Arm the anchor the pointer is near so the handle highlights
            // before the click lands.
            UpdateAnchorHover();
            var performanceStartedAt =
                FalsumPerformanceMonitor.Timestamp();
            var playing =
                _host.Clock != null &&
                _host.Clock.IsPlaying;
            if (playing)
            {
                _playheadMs = Math.Max(0.0, _host.Clock.TimeMs);
            }
            var durationMs = knownDurationMs ??
                             TimelineDuration();
            _view.SetHistory(
                _session.UndoCount,
                _session.RedoCount);
            var now = Time.unscaledTimeAsDouble;
            var updateTimelineText =
                !playing ||
                now >= _nextTimelineTextUpdateAt;
            if (updateTimelineText)
            {
                _nextTimelineTextUpdateAt =
                    now + 1.0 / 30.0;
            }
            _view.SetTime(
                _playheadMs,
                durationMs,
                updateTimelineText,
                playing);
            // These are frame-invariant for most of a playback run, but NOT for
            // all of it: flow speed is adjustable with Ctrl+wheel while
            // playing, and playback rate can switch mid-run. Rather than
            // skipping them (which would strand a mid-run speed change) or
            // calling them every frame (a cached compare plus the occasional
            // style write on a frame that is already busy), refresh them on a
            // slow cadence. 10 Hz is far above how fast a human notices a
            // number label change and far below the frame rate.
            if (!playing ||
                now >= _nextStaticChromeUpdateAt)
            {
                _nextStaticChromeUpdateAt = now + 0.1;
                _view.SetSpeed(_flowSpeed);
                _view.SetDelay(_session.DelayMs);
                _view.SetBaseBpm(_chart.Bpm);
                _view.SetSubdivision(_subdivision);
                _view.SetPlaybackRate(PlaybackRates[_rateIndex]);
                _view.SetSongSpeed(_flowSpeed);
            }
            _view.SetPlaybackState(
                playing);
            _view.SetSongProgress(
                durationMs > 0.0
                    ? (float)(_playheadMs / durationMs)
                    : 0.0f);
            if (_world != null &&
                _chart != null &&
                _session != null)
            {
                var audioDurationMs =
                    _host.SongAudio != null &&
                    _host.SongAudio.clip != null
                        ? _host.SongAudio.clip.length * 1000.0
                        : 0.0;
                if (audioDurationMs <= 0.001 &&
                    _packageAudioDurationMs > 0)
                {
                    // The waveform already resolved its audio from the
                    // package, so fall back to that instead of letting the
                    // renderer blank itself out while the clip is pending.
                    audioDurationMs = _packageAudioDurationMs;
                }
                _world.UpdateSpectrum(
                    _chart,
                    _playheadMs,
                    audioDurationMs,
                    _session.DelayMs,
                    _flowSpeed,
                    _spectrumVisible,
                    _viewMode ==
                    FalsumViewMode.Orthographic2D);
            }
            _view.SetHint(
                Time.unscaledTimeAsDouble < _hintUntil
                    ? _viewTextHint
                    : string.Empty);
            _performanceMonitor.RecordViewSync(
                FalsumPerformanceMonitor.ElapsedMilliseconds(
                    performanceStartedAt));
        }

        private string _viewTextHint;

        private void ShowHint(string text, float duration)
        {
            _viewTextHint = text;
            _hintUntil = Time.unscaledTimeAsDouble + duration;
            if (_view != null)
            {
                _view.SetHint(text);
            }
        }

        private void RefreshPropertyPanel()
        {
            _view.ShowProperties(
                _selected.Count == 1 ? _selected[0] : null);
        }

        private void ClearSelection()
        {
            ResetPickCycle();
            _selected.Clear();
            _selectionDragPending = false;
            _dragStates.Clear();
            RefreshPropertyPanel();
        }

        private void CancelCurrentAction()
        {
            if (_toolNotePickActive)
            {
                if (_view != null && _view.ToolDialog != null)
                {
                    _view.ToolDialog.CancelNotePickSelection();
                }
                return;
            }
            if (_mode == EditorMode.PastePreview)
            {
                CancelPastePreview();
                return;
            }
            CancelMiddleEdit();
            if (_session.HasPendingEdit)
            {
                _session.CancelChange();
                RefreshEditorVisuals();
            }
            _draggingSelection = false;
            _selectionDragPending = false;
            _dragStates.Clear();
            _flickDragging = false;
            _skyDragging = false;
            _segmentHasStart = false;
            _holdPlaced = false;
            SetMode(EditorMode.Select);
            ClearSelection();
            _persistentHint = BuildPlaybackHint();
        }

        /// <summary>
        /// The active denominator ladder. Never null once
        /// <see cref="EnsureGridDenominatorSequence"/> has run: a preferences
        /// apply may hand back a malformed sequence, and falling back to the
        /// default keeps stepping usable instead of throwing on every key press.
        /// </summary>
        private IList<int> GridDenominatorSequence
        {
            get
            {
                EnsureGridDenominatorSequence();
                return _gridDenominatorSequence;
            }
        }

        private void EnsureGridDenominatorSequence()
        {
            if (_gridDenominatorSequence == null ||
                _gridDenominatorSequence.Count == 0)
            {
                List<int> parsed;
                string error;
                if (!FalsumBeatGrid.TryParseDenominatorSequence(
                        FalsumBeatGrid.DefaultDenominatorSequence,
                        out parsed,
                        out error))
                {
                    // The default is a compile-time constant that the parser
                    // accepts, so this branch is unreachable; keep it safe
                    // anyway rather than leaving the ladder null.
                    parsed = new List<int> { DefaultFlickDenominator };
                }
                _gridDenominatorSequence = parsed;
            }
        }

        /// <summary>
        /// Index of <paramref name="value"/> on the active ladder, or -1 when
        /// it is a native denominator the sequence does not carry. Purely
        /// informational: <see cref="FalsumBeatGrid.StepDenominator"/> resolves
        /// non-members on its own.
        /// </summary>
        private int DenominatorIndexFor(int value)
        {
            var sequence = GridDenominatorSequence;
            for (var i = 0; i < sequence.Count; i++)
            {
                if (sequence[i] == value)
                {
                    return i;
                }
            }
            return -1;
        }

        private void RefreshDenominatorIndex()
        {
            _denominatorIndex = DenominatorIndexFor(_flickDenominator);
        }

        /// <summary>
        /// Seeds the ladder from a preference string. A malformed value keeps
        /// the default ladder (the preference store already repairs it on save;
        /// this is the in-session guard) and the current denominator is re-seated
        /// on the nearest member so the readout never shows a value the keys
        /// cannot step from.
        /// </summary>
        private void ApplyGridDenominatorSequence(string text)
        {
            List<int> parsed;
            string error;
            if (FalsumBeatGrid.TryParseDenominatorSequence(
                    text,
                    out parsed,
                    out error))
            {
                _gridDenominatorSequence = parsed;
            }
            else
            {
                List<int> fallback;
                if (FalsumBeatGrid.TryParseDenominatorSequence(
                        FalsumBeatGrid.DefaultDenominatorSequence,
                        out fallback,
                        out error))
                {
                    _gridDenominatorSequence = fallback;
                }
            }
            _flickDenominator = FalsumBeatGrid.NearestDenominator(
                GridDenominatorSequence,
                _flickDenominator,
                DefaultFlickDenominator);
            RefreshDenominatorIndex();
        }

        /// <summary>
        /// One frame of the GRID ruler, as the 3D world draws it. The divisions
        /// come from the same field <see cref="UpdateModePlane"/> hands
        /// <see cref="FalsumModePlane.SetState"/>, so the ticks and the info box
        /// cells cannot disagree.
        /// </summary>
        private FalsumRulerState BuildRulerState()
        {
            var divisions = _modePlaneDivisions;
            var focusLine = _modePlaneFocusLine;
            float startNormalized;
            float endNormalized;
            ResolveRulerMarkers(
                divisions,
                focusLine,
                out startNormalized,
                out endNormalized);
            return new FalsumRulerState
            {
                Visible = divisions > 0,
                Divisions = divisions,
                FocusLine = focusLine,
                StartNormalized = startNormalized,
                EndNormalized = endNormalized,
                Color = ModeGridColor(),
                Surface = ResolveRulerSurface(),
                GuideNormalized = ResolveGuideNormalized(divisions),
            };
        }

        /// <summary>
        /// Which surface the ruler belongs to. FLICK / SKYAREA are sky notes,
        /// so their ruler hangs from the sky plane; TAP / HOLD are ground
        /// notes, so theirs stands on the track floor. A sky ruler drawn at the
        /// floor height (or vice versa) floats in mid-air, unrelated to the
        /// note being edited.
        /// </summary>
        private FalsumRulerSurface ResolveRulerSurface()
        {
            if (_anchorPlaneVisible)
            {
                // The anchor layer resolved the note kind when it published
                // the drag; read it rather than re-deriving from selection.
                return IsGroundNoteKind(_anchorPlaneKind)
                    ? FalsumRulerSurface.Ground
                    : FalsumRulerSurface.Sky;
            }
            if (IsMiddleEditActive)
            {
                var active = FindMiddleActiveTarget(_middleEdit);
                var kind = active != null
                    ? active.Note.Kind
                    : NoteKind.Tap;
                return IsGroundNoteKind(kind)
                    ? FalsumRulerSurface.Ground
                    : FalsumRulerSurface.Sky;
            }
            // Placement modes: FLICK and SKYAREA are both sky notes.
            return FalsumRulerSurface.Sky;
        }

        private static bool IsGroundNoteKind(NoteKind kind)
        {
            return kind == NoteKind.Tap ||
                   kind == NoteKind.Hold;
        }

        /// <summary>
        /// The lateral cell the pointer currently snaps to, as a 0..1 track
        /// position, or NaN when there is nothing to point at.
        ///
        /// <para>Both sources are already snapped lattices: the flick/skyarea
        /// placement modes keep their anchor in the snapped field, and the
        /// middle/anchor channels expose the shared <c>CurrentPlaneX</c> the
        /// middle bodies themselves quantize. So this method only rounds to the
        /// ruler's own cell, never re-derives a position from raw pixels --
        /// otherwise the guide line and the note being placed would disagree
        /// about which cell the drag is on.</para>
        /// </summary>
        private float ResolveGuideNormalized(int divisions)
        {
            if (divisions <= 0)
            {
                return float.NaN;
            }
            float raw;
            if (_anchorPlaneVisible)
            {
                // Skyarea endpoint drags report the dragged edge; every other
                // plane-facing drag reports the pointer's own plane position.
                raw = _anchorPlaneStartNormalized;
                if (float.IsNaN(raw))
                {
                    return float.NaN;
                }
            }
            else if (IsMiddleEditActive)
            {
                var target = GridAdjustTarget;
                if (target == null || !target.HasPlanePointer)
                {
                    return float.NaN;
                }
                raw = target.CurrentPlaneX;
            }
            else if (_mode == EditorMode.PlaceFlick)
            {
                // The through-line marks the chart TIME the pointer is over, so
                // it must come from the time axis, not the lateral lane
                // position -- a lane position is a horizontal coordinate and
                // pinned the line to one spot no matter where the mouse moved.
                // While dragging, `_flickCurrent` is the cell being drawn to;
                // outside a drag the pointer's own snapped cell is the answer.
                var flickCell = _flickDragging
                    ? _flickCurrent
                    : SnapPlaneCell(_pointerTime);
                raw = (float)flickCell;
            }
            else if (_mode == EditorMode.PlaceSkyArea)
            {
                // SkyArea keeps its through-line: it is the vertical time guide
                // the user reads while placing. Only the rectangular width box
                // around the ghost is dropped (ghost.DrawRim).
                var skyTime = _hasPointer
                    ? _pointerTime
                    : (_skyStage == 0 ? _skyStartTime : _skyEndTime);
                raw = (float)SnapPlaneCell(skyTime);
            }
            else
            {
                return float.NaN;
            }
            // Snap to the nearest cell so the line sits on a tick.
            var cell = RulerCellFor(raw, divisions);
            return cell >= 0
                ? cell / (float)divisions
                : float.NaN;
        }

        /// <summary>
        /// Start/end marker positions for the mode the plane is currently
        /// describing. NaN means "this mode has no such marker", which the ruler
        /// draws as nothing and the info box as an absent row.
        ///
        /// <para>The focus line is passed in rather than read from
        /// <see cref="_modePlaneFocusLine"/> because this is called from inside
        /// <see cref="UpdateModePlane"/> for the SAME frame that field is about
        /// to be set for; reading it back here would resolve the previous
        /// frame's lattice.</para>
        /// </summary>
        private void ResolveRulerMarkers(
            int divisions,
            int focusLine,
            out float startNormalized,
            out float endNormalized)
        {
            startNormalized = float.NaN;
            endNormalized = float.NaN;
            if (divisions <= 0)
            {
                return;
            }
            if (_anchorPlaneVisible)
            {
                startNormalized = _anchorPlaneStartNormalized;
                endNormalized = _anchorPlaneEndNormalized;
                return;
            }
            if (IsMiddleEditActive)
            {
                // Middle edit marks the boundary/position line it is editing and
                // has no end marker. The focus line already sits on the plane's
                // lattice, so dividing by the divisions puts the marker on the
                // exact same cell the info box reports.
                if (focusLine >= 0)
                {
                    startNormalized =
                        focusLine / (float)divisions;
                }
                return;
            }
            if (_mode == EditorMode.PlaceFlick)
            {
                startNormalized = (float)_flickStart;
                endNormalized = (float)_flickCurrent;
            }
            else if (_mode == EditorMode.PlaceSkyArea)
            {
                startNormalized = (float)_skyStartAnchor;
                endNormalized = (float)_skyEndAnchor;
            }
        }

        /// <summary>
        /// Lattice cell for a normalized position, matching the ruler's ticks.
        /// <c>-1</c> is the "no row" sentinel the mode plane expects.
        /// </summary>
        private static int RulerCellFor(
            double normalized,
            int divisions)
        {
            if (divisions <= 0 || double.IsNaN(normalized))
            {
                return -1;
            }
            return Mathf.Clamp(
                Mathf.RoundToInt((float)normalized * divisions),
                0,
                divisions);
        }

        /// <summary>
        /// Denominator a scripted mode fixture starts on: 24 when the ladder
        /// carries it, otherwise the nearest member. Fixtures must not pin a
        /// literal, because the preference ladder is user-editable and a
        /// non-member would make the readout disagree with the ruler ticks.
        /// </summary>
        private int FixtureDenominator()
        {
            var sequence = GridDenominatorSequence;
            for (var i = 0; i < sequence.Count; i++)
            {
                if (sequence[i] == DefaultFlickDenominator)
                {
                    return DefaultFlickDenominator;
                }
            }
            return FalsumBeatGrid.NearestDenominator(
                sequence,
                DefaultFlickDenominator,
                DefaultFlickDenominator);
        }

        private void DecreaseFlickDenominator()
        {
            StepFlickDenominator(-1);
        }

        private void IncreaseFlickDenominator()
        {
            StepFlickDenominator(1);
        }

        /// <summary>
        /// One <c>-</c> / <c>=</c> press. The walk is the authored ladder, not a
        /// hardcoded ladder, so a preference sequence of any length and order
        /// works and a native denominator off the ladder lands on the nearest
        /// member instead of jumping to a literal.
        /// </summary>
        private void StepFlickDenominator(int direction)
        {
            _flickDenominator = Mathf.Clamp(
                FalsumBeatGrid.StepDenominator(
                    GridDenominatorSequence,
                    _flickDenominator,
                    direction),
                1,
                MaxEditableFlickGrid);
            RefreshDenominatorIndex();
            _persistentHint = L.Get(
                "falsum.hint.flick.denominator",
                ("denominator", _flickDenominator));
        }

        private double SnapPlaneCell(double timeMs)
        {
            return SnapPlaneCell(timeMs, _flickDenominator);
        }

        private double SnapPlaneCell(double timeMs, int divisions)
        {
            var camera = Camera.main;
            var master = _host.Binding.MasterScene;
            var line = _host.Binding.JudgementLine;
            if (camera == null || master == null || line == null)
            {
                return 0.5;
            }
            var lineLocal = master.InverseTransformPoint(line.position);
            var z = lineLocal.z +
                    (float)_host.Motion.NoteZAt(timeMs, _playheadMs);
            var plane = new Plane(
                master.forward,
                master.TransformPoint(
                    new Vector3(0.0f, 0.0f, z)));
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            float distance;
            if (!plane.Raycast(ray, out distance) || distance < 0.0f)
            {
                return 0.5;
            }
            var local = master.InverseTransformPoint(
                ray.GetPoint(distance));
            var interpolated = Mathf.InverseLerp(
                -0.81f,
                0.81f,
                local.x);
            var normalized = _disableOutOfBoundsClamping
                ? interpolated
                : Mathf.Clamp01(interpolated);
            return SnapNormalizedPosition(
                normalized,
                divisions,
                _disableOutOfBoundsClamping);
        }

        private int ClampLaneForWidth(
            int lane,
            int width,
            bool allowSide)
        {
            width = Mathf.Clamp(width, 1, 4);
            if (width <= 1)
            {
                return Mathf.Clamp(lane, allowSide ? 0 : 0, 5);
            }
            var min = 1;
            var max = 5 - width;
            return Mathf.Clamp(lane, min, max);
        }

        private int ReadTapWidth()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) ||
                Input.GetKeyDown(KeyCode.Keypad1))
            {
                return 1;
            }
            if (Input.GetKeyDown(KeyCode.Alpha2) ||
                Input.GetKeyDown(KeyCode.Keypad2))
            {
                return 2;
            }
            if (Input.GetKeyDown(KeyCode.Alpha3) ||
                Input.GetKeyDown(KeyCode.Keypad3))
            {
                return 3;
            }
            if (Input.GetKeyDown(KeyCode.Alpha4) ||
                Input.GetKeyDown(KeyCode.Keypad4))
            {
                return 4;
            }
            return 0;
        }

        private void ApplyActivePlacementWidth(int width)
        {
            width = Mathf.Clamp(width, 1, 4);
            _tapWidth = width;
            if (_mode != EditorMode.PlaceHold ||
                !_holdPlaced)
            {
                return;
            }
            _holdWidth = width;
            _holdLane = ClampLaneForWidth(
                _holdLane,
                width,
                false);
            ShowHint(
                L.Get(
                    "falsum.hint.hold.widthAndLane",
                    ("width", width),
                    ("lane", _holdLane)),
                1.2f);
        }

        private double TimelineDuration()
        {
            var audioDuration = _host.SongAudio != null &&
                                _host.SongAudio.clip != null
                ? _host.SongAudio.clip.length * 1000.0
                : 0.0;
            var audioChartDuration =
                ChartTimeAxis.AudioDurationToChartTime(
                    audioDuration,
                    _session.DelayMs);
            return Math.Max(
                1000.0,
                Math.Max(audioChartDuration, _chart.EndTimeMs));
        }

        private double KnownSongDurationMs()
        {
            if (_manifest != null &&
                _manifest.AudioDurationMs > 0)
            {
                return _manifest.AudioDurationMs;
            }
            return _host.SongAudio != null &&
                   _host.SongAudio.clip != null
                ? _host.SongAudio.clip.length * 1000.0
                : 0.0;
        }

        private void SyncMeasureLineGenerationEnd(Chart chart)
        {
            if (chart == null)
            {
                return;
            }
            var songDurationMs = KnownSongDurationMs();
            if (songDurationMs <= 0.0 ||
                double.IsNaN(songDurationMs) ||
                double.IsInfinity(songDurationMs))
            {
                return;
            }
            chart.MeasureLineGenerationEndMs = Math.Max(
                0.0,
                ChartTimeAxis.AudioDurationToChartTime(
                    songDurationMs,
                    chart.DelayMs));
        }

        private void RebuildEditorTimeline()
        {
            if (_chart == null)
            {
                return;
            }
            SyncMeasureLineGenerationEnd(_chart);
            _chart.RebuildTimeline();
        }

        private bool RejectNegativeMusicTime(double chartTimeMs)
        {
            if (!ChartTimeAxis.IsNegativeMusicTime(
                    chartTimeMs,
                    _session.DelayMs))
            {
                return false;
            }
            ShowNegativeMusicTimeWarning();
            return true;
        }

        private bool RejectNegativeMusicTimes(
            IList<ChartNote> notes)
        {
            if (!ContainsNegativeMusicTime(notes))
            {
                return false;
            }
            ShowNegativeMusicTimeWarning();
            return true;
        }

        private bool ContainsNegativeMusicTime(
            IList<ChartNote> notes)
        {
            if (notes == null)
            {
                return false;
            }
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note != null &&
                    ChartTimeAxis.IsNegativeMusicTime(
                        note.TimeMs,
                        _session.DelayMs))
                {
                    return true;
                }
            }
            return false;
        }

        private void ShowNegativeMusicTimeWarning()
        {
            if (_view != null)
            {
                _view.ShowAlert(NegativeMusicTimeWarning);
            }
        }

        private int NextSkyGroupId()
        {
            var max = 0;
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                if (_chart.Notes[i].Kind == NoteKind.SkyArea)
                {
                    max = Math.Max(max, _chart.Notes[i].GroupId);
                }
            }
            return max + 1;
        }

        private static void NormalizeNoteFractions(ChartNote note)
        {
            if (note == null)
            {
                return;
            }
            double position;
            double width;
            if (note.Kind == NoteKind.Flick)
            {
                note.StartCellCount = FalsumFraction.NormalizePair(
                    note.StartPos,
                    note.StartWidth,
                    note.StartCellCount,
                    out position,
                    out width);
                note.EndCellCount = note.StartCellCount;
                note.StartPos = position;
                note.StartWidth = width;
            }
            else if (note.Kind == NoteKind.SkyArea)
            {
                note.StartCellCount = FalsumFraction.NormalizePair(
                    note.StartPos,
                    note.StartWidth,
                    note.StartCellCount,
                    out position,
                    out width);
                note.StartPos = position;
                note.StartWidth = width;
                note.EndCellCount = FalsumFraction.NormalizePair(
                    note.EndPos,
                    note.EndWidth,
                    note.EndCellCount,
                    out position,
                    out width);
                note.EndPos = position;
                note.EndWidth = width;
            }
        }

        private static ChartNote CloneNote(ChartNote source)
        {
            return new ChartNote
            {
                Id = source.Id,
                SourceOrder = source.SourceOrder,
                Kind = source.Kind,
                TimeMs = source.TimeMs,
                StartLane = source.StartLane,
                WidthLanes = source.WidthLanes,
                DurationMs = source.DurationMs,
                StartPos = source.StartPos,
                EndPos = source.EndPos,
                StartWidth = source.StartWidth,
                EndWidth = source.EndWidth,
                StartCellCount = source.StartCellCount,
                EndCellCount = source.EndCellCount,
                LeftEasing = source.LeftEasing,
                RightEasing = source.RightEasing,
                GroupId = source.GroupId,
                FlickDirection = source.FlickDirection,
            };
        }

        private static void ParseFraction(
            string text,
            out double numerator,
            out int denominator)
        {
            var parts = text.Split('/');
            numerator = ParseDouble(parts[0]);
            if (parts.Length <= 1)
            {
                denominator = 24;
                return;
            }
            var parsedDenominator = ParseDouble(parts[1]);
            if (parsedDenominator < 1.0 ||
                parsedDenominator > FalsumFraction.MaxDenominator)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.gridDenominatorRange"));
            }
            denominator = Mathf.Clamp(
                Mathf.RoundToInt((float)parsedDenominator),
                1,
                FalsumFraction.MaxDenominator);
        }

        private static double ParseDouble(string text)
        {
            if (!TryParseFiniteDouble(text, out var value))
            {
                throw new FormatException(
                    L.Get("falsum.error.finiteNumberRequired"));
            }
            return value;
        }

        private static bool TryParseFiniteDouble(
            string text,
            out double value)
        {
            return double.TryParse(
                       text,
                       NumberStyles.Float,
                       Invariant,
                       out value) &&
                   !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private static bool TryParseFiniteFloat(
            string text,
            out float value)
        {
            return float.TryParse(
                       text,
                       NumberStyles.Float,
                       Invariant,
                       out value) &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static int ParseEase(string value)
        {
            if (value.IndexOf("IN", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }
            if (value.IndexOf("OUT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }
            int parsed;
            return int.TryParse(value, out parsed)
                ? Mathf.Clamp(parsed, 0, 2)
                : 0;
        }

        private static string DifficultyName(int index)
        {
            switch (index)
            {
                case 0:
                    return "MIN";
                case 1:
                    return "EVO";
                case 2:
                    return "ULT";
                case 3:
                    return "FBD";
                default:
                    return "MIN";
            }
        }

        private static Canvas CreateSelectionCanvas(Transform parent)
        {
            var root = new GameObject(
                "FalsumSelectionCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1730.0f, 1080.0f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }
    }
}
