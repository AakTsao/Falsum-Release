using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using InFalsus.Gameplay.Falsum.Runtime;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// The ONE mobile-adaptation seam. Everything that turns the desktop
    /// layout into the landscape-phone layout lives here, and nothing here
    /// touches chart data, the session or the editor itself.
    ///
    /// WHY A SEPARATE CLASS: the desktop editor and the phone editor must
    /// stay the same UXML and the same view events. A second UXML and a
    /// split view interface would have to maintain the event contract twice,
    /// which is exactly the drift `02-invariants.md` §C-4 exists to prevent.
    ///
    /// LAYOUT (landscape, left to right):
    ///   left rail (square + 10 dp) | canvas | 22 dp progress | right rail
    /// The property panel docks below the left-side history controls and only
    /// appears for a selected note; the desktop top and bottom bars stay hidden.
    ///
    /// FAILURE POLICY: every adaptation degrades to the DESKTOP layout.
    /// Elements are looked up with Q<>() (never Require); a missing sheet
    /// logs and continues. The editor must never fail to start because of
    /// the phone shell.
    /// </summary>
    public sealed class FalsumMobileShell
    {
        /// <summary>
        /// Root class every mobile style rule hangs off. Absent on desktop,
        /// which is what keeps the desktop rendering unchanged.
        /// </summary>
        public const string MobileClass = "is-mobile";

        private const string MobileStylePath = "Falsum/UI/FalsumMobile";

        /// <summary>
        /// Environment override for headless verification (the capture tool
        /// sets it). The preferences developer switch uses
        /// <see cref="RuntimeOverride"/> instead.
        /// </summary>
        private const string ForceEnvironmentVariable = "FALSUM_MOBILE_SHELL";

        /// <summary>
        /// Control size multiplier. The user reported the controls as
        /// oversized, and this is the knob they asked for. It multiplies the
        /// button heights in FalsumMobile.uss, not the dp layout: the
        /// columns keep their real dp widths so the playfield does not
        /// change size when the buttons do.
        /// </summary>
        /// <summary>
        /// Padding inside each rail, in dp. The rail width is
        /// square + 2 * this, so the buttons sit flush with the screen edge
        /// and nothing else may reserve horizontal space.
        /// </summary>
        public const int RailInsetDp = 5;

        /// <summary>
        /// Width of the vertical progress scrubber, in dp. The song card and
        /// the right-hand panels offset by this so they can never overlap it.
        /// </summary>
        public const int ProgressStripWidthDp = 22;

        public const float MinScale = FalsumMobileScaleBounds.Min;
        public const float MaxScale = FalsumMobileScaleBounds.Max;
        public const float DefaultScale = FalsumMobileScaleBounds.Default;

        private readonly VisualElement _root;
        private readonly VisualElement _leftRail;
        private readonly VisualElement _rightRail;
        private readonly VisualElement _floatLayer;
        private readonly VisualElement _progressStrip;
        private readonly VisualElement _stageCapsule;
        private readonly VisualElement _songCardHost;
        private readonly VisualElement _keypadHost;
        private StyleSheet _styleSheet;
        private readonly List<Action> _unbind = new List<Action>();
        private readonly List<Action> _restoreStyles = new List<Action>();
        private readonly HashSet<VisualElement> _styled = new HashSet<VisualElement>();
        private bool _mounted;

        private float _scale = DefaultScale;
        private bool _multiSelect;
        private FalsumRadialAction _placementMode;
        private float _progress;
        private float _lastViewportWidth = -1.0f;
        private float _lastViewportHeight = -1.0f;
        private Rect _lastSafeArea;
        private Vector4 _safeInsets;

        public Rect SafeBounds
        {
            get
            {
                var width = _root.layout.width > 1.0f ? _root.layout.width : 855.0f;
                var height = _root.layout.height > 1.0f ? _root.layout.height : 405.0f;
                return new Rect(_safeInsets.x, _safeInsets.y,
                    Mathf.Max(1.0f, width - _safeInsets.x - _safeInsets.z),
                    Mathf.Max(1.0f, height - _safeInsets.y - _safeInsets.w));
            }
        }

        /// <summary>
        /// True while the shell is writing the scrubber from the playhead.
        /// Without this the per-frame SetProgress call would look like a user
        /// drag and seek the audio every frame -- which is exactly what
        /// produced a stream of "An invalid seek position was passed to this
        /// function" errors from AudioSource.Play.
        /// </summary>
        private bool _writingProgress;
        private FalsumMobileGridPicker _gridPicker;
        private VisualElement _flowFill;

        // ---- Events the view forwards onto the existing editor events ----

        public event Action MenuRequested;
        public event Action<float> FlowChanged;
        public event Action UndoRequested;
        public event Action RedoRequested;
        public event Action<FalsumRadialAction> PlacementRequested;
        public event Action ExitPlacementRequested;
        public event Action SegmentSelectRequested;
        public event Action<bool> MultiSelectChanged;
        public event Action MirrorRequested;
        public event Action GridToggleRequested;
        public event Action BankToggleRequested;
        public event Action CopyRequested;
        public event Action PasteRequested;
        public event Action DeleteRequested;
        public event Action EventsToggleRequested;
        public event Action RateRequested;
        public event Action PlayPauseRequested;
        public event Action AuditionRequested;
        public event Action AudioPreprocessRequested;
        public event Action<float> ProgressChanged;
        public event Action<string> BaseBpmRequested;
        public event Action<string> SubdivisionRequested;
        public event Action<string> TimeFieldRequested;

        public bool IsMultiSelectActive
        {
            get { return _multiSelect; }
        }

        /// <summary>
        /// True when the shell should take over.
        ///
        /// PRECEDENCE, highest first:
        /// 1. <see cref="Application.isMobilePlatform"/> — a real phone
        ///    always gets the phone layout.
        /// 2. <see cref="RuntimeOverride"/> — the developer switch, or its
        ///    persisted value replayed at startup.
        /// 3. The FALSUM_MOBILE_SHELL environment variable — headless/CI.
        ///
        /// Deliberately not cached: the switch flips it mid-session.
        /// </summary>
        public static bool IsMobile
        {
            get
            {
                if (Application.isMobilePlatform)
                {
                    return true;
                }
                if (RuntimeOverride.HasValue)
                {
                    return RuntimeOverride.Value;
                }
                return ForceMobileForVerification;
            }
        }

        /// <summary>Developer override. null = defer to platform + env.</summary>
        public static bool? RuntimeOverride { get; set; }

#if UNITY_EDITOR
        public static Rect? DebugSafeAreaOverride { get; set; }
#endif

        private static Rect CurrentSafeArea
        {
            get
            {
#if UNITY_EDITOR
                if (DebugSafeAreaOverride.HasValue) return DebugSafeAreaOverride.Value;
#endif
                return Screen.safeArea;
            }
        }

        private static bool _forceMobileProbed;
        private static bool _forceMobile;

        private static bool ForceMobileForVerification
        {
            get
            {
                if (!_forceMobileProbed)
                {
                    _forceMobileProbed = true;
                    try
                    {
                        var raw = Environment.GetEnvironmentVariable(
                            ForceEnvironmentVariable);
                        _forceMobile =
                            raw != null &&
                            (raw == "1" ||
                             raw.Equals("true",
                                 StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception)
                    {
                        // Environment access is unavailable in some
                        // sandboxes; that must never be a startup failure.
                        _forceMobile = false;
                    }
                }
                return _forceMobile;
            }
        }

        public FalsumMobileShell(VisualElement root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }
            _root = root;
            _leftRail = root.Q<VisualElement>("MobileLeftBar");
            _rightRail = root.Q<VisualElement>("MobileToolRail");
            _floatLayer = root.Q<VisualElement>("MobileFloatLayer");
            _progressStrip = root.Q<VisualElement>("MobileProgressStrip");
            _stageCapsule = root.Q<VisualElement>("StageCapsule");
            _songCardHost = root.Q<VisualElement>("MobileSongCardHost");
            _keypadHost = root.Q<VisualElement>("MobileKeypadHost");
        }

        /// <summary>
        /// Mounts the shell on a freshly built editor document. Idempotent
        /// per root: a second call on the same root is a no-op.
        /// </summary>
        public static FalsumMobileShell Attach(VisualElement root)
        {
            if (root == null || !IsMobile)
            {
                return null;
            }
            if (root.ClassListContains(MobileClass))
            {
                return null;
            }
            var shell = new FalsumMobileShell(root);
            return shell.Mount() ? shell : null;
        }

        private bool Mount()
        {
            if (!MountStyleSheet())
            {
                return false;
            }
            _mounted = true;
            _root.AddToClassList(MobileClass);
            TrackStyle(_root);
            Register(_root, (EventCallback<GeometryChangedEvent>)OnRootGeometryChanged);
            ApplyScale(_scale);
            ApplySafeArea();
            RevealChrome();
            BindLeftBar();
            BindToolRail();
            BindRightBar();
            BindProgress();
            L.LocaleChanged += RefreshChromeText;
            _unbind.Add(() => L.LocaleChanged -= RefreshChromeText);
            MakeRailScrollable(_leftRail);
            MakeRailScrollable(_rightRail);
            return true;
        }

        /// <summary>
        /// Undoes <see cref="Mount"/> so the editor returns to the desktop
        /// layout WITHOUT rebuilding the document.
        ///
        /// Rebuilding is wrong here: the editor wires its events and a dozen
        /// sub-views into this document once (FalsumChartEditor.WireView),
        /// so recreating it would either drop every subscription or need a
        /// second parallel wiring pass.
        /// </summary>
        public void Detach()
        {
            _mounted = false;
            _gridPicker?.Dispose();
            _gridPicker = null;
            _flowFill?.RemoveFromHierarchy();
            _flowFill = null;
            for (var i = _unbind.Count - 1; i >= 0; i--)
            {
                _unbind[i]();
            }
            _unbind.Clear();
            for (var i = _restoreStyles.Count - 1; i >= 0; i--)
            {
                _restoreStyles[i]();
            }
            _restoreStyles.Clear();
            _styled.Clear();
            _root.RemoveFromClassList(MobileClass);
            _root.RemoveFromClassList("is-mobile-menu-two-columns");
            _root.RemoveFromClassList("is-mobile-small");
            _root.RemoveFromClassList("is-mobile-small-rail");
            if (_styleSheet != null)
            {
                _root.styleSheets.Remove(_styleSheet);
            }
            FalsumModifierState.ResetLatches();
            _multiSelect = false;
            _root.Q<Button>("MobileMultiSelect")?.RemoveFromClassList("is-active");
            // Do not re-write the height here: _restoreStyles already put the
            // desktop inline height back, and the mobile formula would win
            // over it until the next property refresh.
            var propertyRows = _root.Q<ScrollView>("PropertyRows");
            if (propertyRows != null)
            {
                propertyRows.verticalScrollerVisibility =
                    ScrollerVisibility.Hidden;
            }
            Hide(_leftRail);
            Hide(_rightRail);
            Hide(_floatLayer);
            Hide(_progressStrip);
            Hide(_stageCapsule);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            RefreshLayout();
        }

        public void RefreshLayout()
        {
            var width = _root.layout.width;
            var height = _root.layout.height;
            var safe = CurrentSafeArea;
            if (!_mounted || float.IsNaN(width) || float.IsNaN(height) ||
                width <= 1.0f || height <= 1.0f ||
                (Mathf.Abs(width - _lastViewportWidth) < 1.0f &&
                 Mathf.Abs(height - _lastViewportHeight) < 1.0f &&
                 safe == _lastSafeArea))
            {
                return;
            }

            _lastViewportWidth = width;
            _lastViewportHeight = height;
            _lastSafeArea = safe;
            ApplySafeArea();
            _root.EnableInClassList("is-mobile-menu-two-columns", SafeBounds.width < 540.0f);
            _root.EnableInClassList("is-mobile-small", SafeBounds.width < 700.0f);
            ApplyFloatAnchors(Mathf.RoundToInt(56.0f * _scale) + 10);
        }

        private void Register<T>(VisualElement element, EventCallback<T> callback,
            TrickleDown phase = TrickleDown.NoTrickleDown) where T : EventBase<T>, new()
        {
            element.RegisterCallback(callback, phase);
            _unbind.Add(() => element.UnregisterCallback(callback, phase));
        }

        private void TrackStyle(VisualElement element)
        {
            if (element == null || !_styled.Add(element)) return;
            var style = element.style;
            var left = style.left;
            var right = style.right;
            var top = style.top;
            var bottom = style.bottom;
            var width = style.width;
            var height = style.height;
            var maxHeight = style.maxHeight;
            var scale = style.scale;
            var paddingLeft = style.paddingLeft;
            var paddingRight = style.paddingRight;
            var paddingTop = style.paddingTop;
            var paddingBottom = style.paddingBottom;
            _restoreStyles.Add(() =>
            {
                style.left = left;
                style.right = right;
                style.top = top;
                style.bottom = bottom;
                style.width = width;
                style.height = height;
                style.maxHeight = maxHeight;
                style.scale = scale;
                style.paddingLeft = paddingLeft;
                style.paddingRight = paddingRight;
                style.paddingTop = paddingTop;
                style.paddingBottom = paddingBottom;
            });
        }

        private void MakeRailScrollable(VisualElement rail)
        {
            if (rail == null) return;
            var children = new List<VisualElement>(rail.Children());
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mobile-rail-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            foreach (var child in children)
            {
                if (child.name != "MobileMultiSelect") scroll.Add(child);
            }
            // Multi-select stays at the thumb's lower-left resting position,
            // outside the scrollable placement tools.
            rail.Insert(0, scroll);
            _unbind.Add(() =>
            {
                foreach (var child in children) rail.Add(child);
                scroll.RemoveFromHierarchy();
            });
        }

        private static void Hide(VisualElement element)
        {
            if (element != null && !element.ClassListContains("hidden"))
            {
                element.AddToClassList("hidden");
            }
        }

        private bool MountStyleSheet()
        {
            if (_styleSheet != null && _root.styleSheets.Contains(_styleSheet))
            {
                return true;
            }
            var sheet = Resources.Load<StyleSheet>(MobileStylePath);
            if (sheet == null)
            {
                // Degrade, never fail: without the sheet the editor still
                // runs, it just stays at desktop sizes.
                Debug.LogWarning(
                    "[Falsum] " + L.Get("falsum.error.ui.mobileStyleMissing"));
                return false;
            }
            _styleSheet = sheet;
            if (!_root.styleSheets.Contains(_styleSheet))
            {
                _root.styleSheets.Add(_styleSheet);
            }
            return true;
        }

        /// <summary>
        /// Applies the control-size multiplier.
        ///
        /// ONE NUMBER DRIVES EVERYTHING. The rails are exactly one square
        /// button wide (plus insets), and the floating panels anchor off the
        /// same value, so shrinking the buttons narrows both rails and
        /// widens the playfield instead of leaving a gap. That is the
        /// difference between "the controls are smaller" and "the layout
        /// wastes less screen".
        /// </summary>
        public void ApplyScale(float scale)
        {
            _scale = Mathf.Clamp(scale, MinScale, MaxScale);
            _root.EnableInClassList("is-mobile-small-rail", _scale < 0.8f);
            var square = Mathf.RoundToInt(56.0f * _scale);
            // UI Toolkit's IStyle has no custom-property setter in this
            // Unity version, so the size is applied inline to the rails and
            // their buttons. USS provides initial offsets; scaled float
            // anchors are recalculated below from the real rail width.
            ApplySquareSize(_leftRail, square);
            ApplySquareSize(_rightRail, square);
            ApplyFloatingButtonScale();
            ApplyFloatAnchors(square + 10);
        }

        /// <summary>
        /// Floats carry fewer and less thumb-critical actions than the rails,
        /// so keep them slightly more compact at the small end of the scale
        /// range while leaving the left and right touch rails untouched.
        /// </summary>
        public void ApplyFloatingButtonScale()
        {
            if (_floatLayer == null)
            {
                return;
            }
            // Size reductions live in USS so layout and pointer bounds agree.
            _floatLayer.Query<Button>().ForEach(
                button =>
                {
                    if (button.ClassListContains("numeric-pad-key")) return;
                    TrackStyle(button);
                    button.style.scale = StyleKeyword.Null;
                });
        }

        /// <summary>
        /// Sets the rail and button dimensions together because this Unity
        /// version cannot update USS custom properties through IStyle.
        /// </summary>
        private void ApplySquareSize(
            VisualElement rail,
            int square)
        {
            if (rail == null)
            {
                return;
            }
            TrackStyle(rail);
            rail.style.width = square + 10;
            rail.Query<VisualElement>(className: "mobile-sq").ForEach(child =>
            {
                TrackStyle(child);
                child.style.width = square;
                child.style.height = square;
            });
        }

        /// <summary>
        /// Re-anchors the floating panels when the rail width changes.
        ///
        /// USS provides initial offsets. The real offsets are set here so
        /// narrowing the rails also moves the floats to the new canvas edge.
        /// </summary>
        private void ApplyFloatAnchors(int railWidth)
        {
            var inset = railWidth + 6;
            Anchor("MobileFlowPanel", "left", inset);
            Anchor("MobileHistoryPanel", "left", inset);
            var rightDock = railWidth + ProgressStripWidthDp + 8;
            Anchor("MobileSongCardHost", "right", rightDock);
            Anchor("MobileMetaPanel", "right", rightDock);
            Anchor("MobileTransportPanel", "right", rightDock);
            Anchor("MobileKeypadHost", "right", rightDock);
            Anchor("MobileProgressStrip", "right", railWidth);
            var available = Mathf.Max(1.0f,
                SafeBounds.width - 2.0f * railWidth - ProgressStripWidthDp - 28.0f);
            var columnBudget = available - Mathf.Min(96.0f, available * 0.2f);
            var leftColumn = Mathf.Min(144.0f, columnBudget * 0.44f);
            var rightColumn = Mathf.Min(208.0f, columnBudget - leftColumn);
            SetWidth("MobileFlowPanel", leftColumn);
            SetWidth("MobileHistoryPanel", leftColumn);
            SetWidth("MobileMetaPanel", rightColumn);
            SetWidth("MobileTransportPanel", rightColumn);
            SetWidth("MobileKeypadHost", rightColumn);
            SetWidth("MobileSongCardHost", rightColumn);
            Anchor("ModeHint", "left", Mathf.RoundToInt(SafeBounds.x) + railWidth + 160);
            SetWidth("ModeHint", Mathf.Min(320.0f, Mathf.Max(1.0f, available - 152.0f)));
            SetTop("ModeHint", SafeBounds.yMax - 30.0f);
            var cardHeight = rightColumn * (559.0f / 3013.0f);
            SetHeight("MobileSongCardHost", cardHeight);
            SetTop("MobileMetaPanel", cardHeight + 12.0f);
            SetTop("MobileTransportPanel", cardHeight + 18.0f);
            var property = _root.Q<VisualElement>("NoteProperties");
            if (property != null)
            {
                TrackStyle(property);
                property.style.left = SafeBounds.x + inset;
                // Property values need a stable reading width even when a
                // large rail scale compresses the top floating controls.
                property.style.width = Mathf.Min(144.0f, available);
                property.style.top = SafeBounds.y + 80.0f;
            }
            LayoutPropertyPanel();
            LayoutNumericWindow();
        }

        public void LayoutNumericWindow()
        {
            var window = _root.Q<VisualElement>("MobileNumericWindow");
            if (window == null) return;
            var railWidth = Mathf.RoundToInt(56.0f * _scale) + 10;
            window.style.right = railWidth + ProgressStripWidthDp + 8;
            window.style.width = Mathf.Min(196.0f,
                Mathf.Max(1.0f, SafeBounds.width - 2 * railWidth - ProgressStripWidthDp - 28));
        }

        private void SetTop(string name, float top)
        {
            var element = _root.Q<VisualElement>(name);
            if (element == null) return;
            TrackStyle(element);
            element.style.top = top;
        }

        private void SetWidth(string name, float width)
        {
            var element = _root.Q<VisualElement>(name);
            if (element != null)
            {
                TrackStyle(element);
                element.style.width = width;
            }
        }

        private void SetHeight(string name, float height)
        {
            var element = _root.Q<VisualElement>(name);
            if (element != null)
            {
                TrackStyle(element);
                element.style.height = height;
            }
        }

        public void LayoutPropertyPanel()
        {
            ApplyFloatingButtonScale();
            var panel = _root.Q<VisualElement>("NoteProperties");
            var rows = _root.Q<ScrollView>("PropertyRows");
            if (panel == null || rows == null ||
                panel.ClassListContains("hidden"))
            {
                return;
            }

            var top = panel.resolvedStyle.top;
            if (float.IsNaN(top) || top < 0.0f)
            {
                top = SafeBounds.y + 80.0f;
            }
            TrackStyle(panel);
            var available = Mathf.Max(1.0f, SafeBounds.yMax - top - 8.0f);
            // Match the real row and footer dimensions in FalsumMobile.uss.
            // The desktop header is entirely hidden, including its padding.
            var desired = 40.0f + rows.contentContainer.childCount * 22.0f;
            panel.style.height = Mathf.Min(desired, available);
            panel.style.maxHeight = available;
            rows.verticalScrollerVisibility = ScrollerVisibility.Auto;
        }

        private void Anchor(string name, string edge, int value)
        {
            var element = _root.Q<VisualElement>(name);
            if (element == null)
            {
                return;
            }
            TrackStyle(element);
            if (edge == "left")
            {
                element.style.left = value;
            }
            else
            {
                element.style.right = value;
            }
        }

        /// <summary>Current control scale, for the preferences UI.</summary>
        public float Scale
        {
            get { return _scale; }
        }

        private void ApplySafeArea()
        {
            var width = _root.layout.width;
            var height = _root.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height) ||
                width <= 1.0f || height <= 1.0f || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = CurrentSafeArea;
            _safeInsets = safe.width > 0.0f && safe.height > 0.0f
                ? new Vector4(Mathf.Max(0, safe.xMin) * width / Screen.width,
                    Mathf.Max(0, Screen.height - safe.yMax) * height / Screen.height,
                    Mathf.Max(0, Screen.width - safe.xMax) * width / Screen.width,
                    Mathf.Max(0, safe.yMin) * height / Screen.height)
                : Vector4.zero;
            // Absolute children do not honor root padding. Inset their actual hosts.
            foreach (var name in new[] { "MobileFloatLayer", "BankLayer", "MenuLayer", "ModalLayer", "RadialLayer" })
            {
                var layer = _root.Q<VisualElement>(name);
                if (layer == null) continue;
                TrackStyle(layer);
                layer.style.left = _safeInsets.x;
                layer.style.top = _safeInsets.y;
                layer.style.right = _safeInsets.z;
                layer.style.bottom = _safeInsets.w;
            }
            foreach (var rail in new[] { _leftRail, _rightRail })
            {
                if (rail == null) continue;
                TrackStyle(rail);
                rail.style.top = _safeInsets.y;
                rail.style.bottom = _safeInsets.w;
            }
            if (_leftRail != null) _leftRail.style.left = _safeInsets.x;
            if (_rightRail != null) _rightRail.style.right = _safeInsets.z;
        }

        private void RevealChrome()
        {
            Reveal(_leftRail);
            Reveal(_rightRail);
            Reveal(_floatLayer);
            Reveal(_progressStrip);
            // The stage capsule is deliberately NOT revealed here. It is the
            // HOLD/SKYAREA multi-step indicator and must only exist while a
            // multi-step placement is in progress; revealing it at mount left
            // three unexplained markers and a close button floating over the
            // canvas for the whole session.
            // The song card is re-parented into its floating host so it
            // lives over the canvas and cannot cover the rails.
            var cardViewport =
                _root.Q<VisualElement>("SongInfoCardViewport");
            if (cardViewport != null && _songCardHost != null)
            {
                // Re-parent into the floating host. The desktop path calls
                // SendToBack on this element (it wants the card behind the
                // bars); on the phone it must be on TOP of the canvas or the
                // 3D view hides it, so re-parenting is followed by moving it
                // to the front of its new parent.
                _songCardHost.Add(cardViewport);
                _unbind.Add(() =>
                {
                    _root.Add(cardViewport);
                    cardViewport.SendToBack();
                });
                cardViewport.SendToBack();
                cardViewport.BringToFront();
            }
            // The property panel is NOT revealed here on purpose.
            // FalsumEditorView.ShowProperties already gates it on
            // `note != null` (the desktop behaviour), so forcing it visible at
            // mount left an empty card floating over the canvas for the whole
            // session. The mobile layout only re-parents and positions it; its
            // visibility stays owned by the selection.
        }

        private static void Reveal(VisualElement element)
        {
            if (element != null)
            {
                element.RemoveFromClassList("hidden");
            }
        }

        // ---- Left column -------------------------------------------------

        private void BindLeftBar()
        {
            BindButton("MobileMenu", () => Raise(MenuRequested));
            BindButton("MobileUndo", () => Raise(UndoRequested));
            BindButton("MobileRedo", () => Raise(RedoRequested));
            BindPlacement("MobilePlaceTap", FalsumRadialAction.PlaceTap);
            BindPlacement("MobilePlaceHold", FalsumRadialAction.PlaceHold);
            BindPlacement("MobilePlaceFlick", FalsumRadialAction.PlaceFlick);
            BindPlacement(
                "MobilePlaceSkyArea",
                FalsumRadialAction.PlaceSkyArea);
            var multi = _root.Q<Button>("MobileMultiSelect");
            if (multi != null)
            {
                BindButton("MobileMultiSelect", ToggleMultiSelect);
            }
            var slider = _root.Q<Slider>("MobileFlowSlider");
            if (slider != null)
            {
                _flowFill = new VisualElement { pickingMode = PickingMode.Ignore };
                _flowFill.AddToClassList("slider-fill");
                var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                var input = tracker?.parent;
                if (input != null) input.Insert(input.IndexOf(tracker) + 1, _flowFill);
                Register(slider, (EventCallback<ChangeEvent<float>>)(evt =>
                    {
                        SetFlowValue(evt.newValue);
                        Raise(FlowChanged, evt.newValue);
                    }));
                SetFlowValue(slider.value);
            }
            ApplyChromeText();
        }

        /// <summary>
        /// Writes the flow multiplier next to its slider. The label used to be
        /// a fixed "5.0" from the UXML default, so it never followed the
        /// slider.
        /// </summary>
        public void SetFlowValue(float value)
        {
            if (_flowFill != null)
                _flowFill.style.width = Length.Percent(Mathf.Clamp01((value - 1.0f) / 9.0f) * 100.0f);
            var label = _root.Q<Label>("MobileFlowValue");
            if (label != null)
            {
                label.text = value.ToString("0.0");
            }
            var slider = _root.Q<Slider>("MobileFlowSlider");
            if (slider != null &&
                Mathf.Abs(slider.value - value) > 0.0005f)
            {
                slider.SetValueWithoutNotify(value);
            }
        }

        private void BindPlacement(string name, FalsumRadialAction action)
        {
            BindButton(name, () => RequestPlacementMode(action));
        }

        private void RequestPlacementMode(FalsumRadialAction action)
        {
            SetSegmentSelectActive(false);
            SetPlacementMode(action);
            Raise(PlacementRequested, action);
        }

        private void RequestSegmentSelection()
        {
            SetSegmentSelectActive(true);
            Raise(SegmentSelectRequested);
        }

        private void SetSegmentSelectActive(bool active)
        {
            var right = _root.Q<Button>("MobileSegmentSelectRight");
            if (right != null) right.EnableInClassList("is-active", active);
        }

        /// <summary>
        /// Android's Input synthesises a mouse button from a single finger,
        /// so a latched Ctrl is the only way a touch user can add to a
        /// selection. Toggling, not holding, because a touch user cannot
        /// hold one control while tapping elsewhere.
        /// </summary>
        public void ToggleMultiSelect()
        {
            _multiSelect = !_multiSelect;
            FalsumModifierState.LatchedControl = _multiSelect;
            var button = _root.Q<Button>("MobileMultiSelect");
            if (button != null)
            {
                button.EnableInClassList("is-active", _multiSelect);
            }
            var handler = MultiSelectChanged;
            if (handler != null)
            {
                handler(_multiSelect);
            }
        }

        /// <summary>
        /// Reflects the editor's current mode on the four type buttons. Called
        /// from the view when the editor changes mode, so the highlight can
        /// never disagree with the mode the editor is actually in.
        /// </summary>
        public void SetPlacementMode(FalsumRadialAction action)
        {
            _placementMode = action;
            var map = new[]
            {
                Tuple.Create("MobilePlaceTap", FalsumRadialAction.PlaceTap),
                Tuple.Create("MobilePlaceHold", FalsumRadialAction.PlaceHold),
                Tuple.Create("MobilePlaceFlick", FalsumRadialAction.PlaceFlick),
                Tuple.Create(
                    "MobilePlaceSkyArea",
                    FalsumRadialAction.PlaceSkyArea),
            };
            for (var i = 0; i < map.Length; i++)
            {
                var button = _root.Q<Button>(map[i].Item1);
                if (button == null)
                {
                    continue;
                }
                button.EnableInClassList(
                    "is-active",
                    map[i].Item2 == action);
            }
        }

        public FalsumRadialAction PlacementMode
        {
            get { return _placementMode; }
        }

        // ---- Tool rail ---------------------------------------------------

        private void BindToolRail()
        {
            BindButton("MobileEvents", () => Raise(EventsToggleRequested));
            var grid = _root.Q<Button>("MobileGrid");
            if (grid != null)
                _gridPicker = new FalsumMobileGridPicker(_root, _floatLayer, grid,
                    () => Raise(GridToggleRequested),
                    value => Raise(SubdivisionRequested, value.ToString(CultureInfo.InvariantCulture)),
                    () => SafeBounds);
            BindButton("MobileMirror", () => Raise(MirrorRequested));
            BindButton("MobileCopy", () => Raise(CopyRequested));
            BindButton("MobilePaste", () => Raise(PasteRequested));
            BindButton("MobileDelete", () => Raise(DeleteRequested));
            BindButton(
                "MobileSegmentSelectRight",
                RequestSegmentSelection);
            BindButton("MobileRate", () => Raise(RateRequested));
        }

        /// <summary>Mirrors the grid state onto the rail button.</summary>
        public void SetGridEnabled(bool enabled)
        {
            var button = _root.Q<Button>("MobileGrid");
            if (button != null)
            {
                button.EnableInClassList("is-active", enabled);
            }
        }

        public void SetGridSubdivision(int subdivision)
        {
            _gridPicker?.SetSubdivision(subdivision);
        }

        // ---- Right column ------------------------------------------------

        /// <summary>
        /// BPM, subdivision and time are EDITABLE FIELDS here, exactly as on
        /// the desktop: the bottom bar's Subdivision and Time are TextFields
        /// and the top bar's BaseBpmValue is one too. Presenting them as
        /// buttons implied a different affordance from the desktop.
        /// </summary>
        private void BindRightBar()
        {
            BindNumericField(
                "MobileBaseBpm",
                value => Raise(BaseBpmRequested, value));
            BindNumericField(
                "MobileSubdivision",
                value => Raise(SubdivisionRequested, value));
            BindNumericField(
                "MobileTimeField",
                value => Raise(TimeFieldRequested, value));
            BindButton(
                "MobileAudioPreprocess",
                () => Raise(AudioPreprocessRequested));
            BindButton("MobilePlayPause", () => Raise(PlayPauseRequested));
            // Icon-only controls. The desktop already ships these vectors, so
            // the phone uses the same artwork instead of inventing glyph text.
            ApplyIcon("MobileUndo", "Falsum/UI/Icons/undo-2");
            ApplyIcon("MobileRedo", "Falsum/UI/Icons/redo-2");
            ApplyIcon("MobilePlayPause", "Falsum/UI/Icons/play");
        }

        private void BindNumericField(string name, Action<string> handler)
        {
            var field = _root.Q<TextField>(name);
            if (field == null)
            {
                return;
            }
            Register(field, (EventCallback<ChangeEvent<string>>)(evt => Raise(handler, evt.newValue)));
        }

        /// <summary>
        /// Puts a bundled VectorImage inside a button and blanks its label.
        /// Images are shared resources, so this only adds a child element and
        /// never mutates the loaded asset.
        /// </summary>
        private void ApplyIcon(string buttonName, string iconPath)
        {
            var button = _root.Q<Button>(buttonName);
            if (button == null)
            {
                return;
            }
            var vector = Resources.Load<VectorImage>(iconPath);
            if (vector == null)
            {
                return;
            }
            button.text = string.Empty;
            button.AddToClassList("mobile-sq-icon");
            if (button.Q<Image>(className: "mobile-icon") != null) return;
            // Image.image takes a Texture; a SVG imports as a VectorImage and
            // must go through the vectorImage slot (this is the same recipe
            // FalsumEditorView.CreateTransportIcon uses for the desktop).
            var image = new Image
            {
                vectorImage = vector,
                tintColor = new Color(0.839f, 0.878f, 0.910f, 1.0f),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            image.AddToClassList("mobile-icon");
            button.Add(image);
        }

        /// <summary>
        /// Binds the vertical scrubber.
        ///
        /// IT IS DRAIVEN BY POINTER POSITION, NOT BY Slider.value. A vertical
        /// UI Toolkit Slider only reports a new value when the drag crosses the
        /// handle's own hit area, so a finger landing above or below the handle
        /// jumps it to an END (0 or 1) and nothing in between -- exactly the
        /// reported "it only drags to the start and the end". Mapping the
        /// pointer's Y across the track's full height gives a continuous value.
        /// </summary>
        private void BindProgress()
        {
            var slider = _root.Q<Slider>("MobileProgress");
            var hitTarget = _progressStrip;
            if (slider == null || hitTarget == null)
            {
                return;
            }
            Register(hitTarget, (EventCallback<PointerDownEvent>)(evt =>
                {
                    if (evt.button != 0) return;
                    hitTarget.CapturePointer(evt.pointerId);
                    hitTarget.AddToClassList("is-scrubbing");
                    ScrubTo(evt.position, slider);
                    evt.StopImmediatePropagation();
                }), TrickleDown.TrickleDown);
            Register(hitTarget, (EventCallback<PointerMoveEvent>)(evt =>
                {
                    if (!hitTarget.HasPointerCapture(evt.pointerId))
                    {
                        return;
                    }
                    ScrubTo(evt.position, slider);
                    evt.StopPropagation();
                }));
            Register(hitTarget, (EventCallback<PointerUpEvent>)(evt =>
                {
                    if (hitTarget.HasPointerCapture(evt.pointerId))
                    {
                        hitTarget.ReleasePointer(evt.pointerId);
                    }
                    hitTarget.RemoveFromClassList("is-scrubbing");
                    evt.StopPropagation();
                }));
            Register(hitTarget, (EventCallback<PointerCancelEvent>)(evt =>
            {
                if (hitTarget.HasPointerCapture(evt.pointerId)) hitTarget.ReleasePointer(evt.pointerId);
                hitTarget.RemoveFromClassList("is-scrubbing");
            }));
            Register(hitTarget, (EventCallback<PointerCaptureOutEvent>)(evt =>
                hitTarget.RemoveFromClassList("is-scrubbing")));
        }

        /// <summary>
        /// Converts a pointer position into a normalised playhead and reports it.
        /// Y grows DOWNWARD in panel space, so the value is inverted: dragging
        /// toward the bottom is earlier in the song.
        /// </summary>
        private void ScrubTo(Vector2 pointer, Slider slider)
        {
            var bound = _root.Q<VisualElement>("MobileProgressTrack")?.worldBound ?? _progressStrip.worldBound;
            if (bound.height <= 1.0f)
            {
                return;
            }
            var normalized = 1.0f -
                             (pointer.y - bound.y) / bound.height;
            normalized = Mathf.Clamp01(normalized);
            if (Mathf.Abs(normalized - _progress) < 0.0005f)
            {
                return;
            }
            _progress = normalized;
            _writingProgress = true;
            try
            {
                slider.SetValueWithoutNotify(normalized);
                UpdateProgressVisual();
            }
            finally
            {
                _writingProgress = false;
            }
            Raise(ProgressChanged, normalized);
        }

        /// <summary>Pushes the playhead onto the vertical strip.</summary>
        public void SetProgress(float normalized, string label)
        {
            _progress = Mathf.Clamp01(normalized);
            UpdateProgressVisual();
            var slider = _root.Q<Slider>("MobileProgress");
            if (slider != null &&
                Mathf.Abs(slider.value - _progress) > 0.0005f)
            {
                // Belt and braces: SetValueWithoutNotify already suppresses
                // the callback, but the flag makes the intent explicit and
                // protects against a future switch back to `value =`.
                _writingProgress = true;
                try
                {
                    slider.SetValueWithoutNotify(_progress);
                }
                finally
                {
                    _writingProgress = false;
                }
            }
            var text = _root.Q<Label>("MobileProgressText");
            if (text != null)
            {
                text.text = label ?? string.Empty;
            }
        }

        private void UpdateProgressVisual()
        {
            var fill = _root.Q<VisualElement>("MobileProgressFill");
            var thumb = _root.Q<VisualElement>("MobileProgressThumb");
            var text = _root.Q<Label>("MobileProgressText");
            if (fill != null) fill.style.height = Length.Percent(_progress * 100.0f);
            if (thumb != null) thumb.style.bottom = Length.Percent(_progress * 100.0f);
            if (text != null) text.style.bottom = Length.Percent(Mathf.Clamp(_progress, 0.04f, 0.96f) * 100.0f);
        }

        /// <summary>Shows the playback state on the transport button.</summary>
        public void SetPlaying(bool playing)
        {
            var button = _root.Q<Button>("MobilePlayPause");
            if (button != null)
            {
                button.EnableInClassList("is-active", playing);
            }
        }

        public void SetPlaybackRate(double rate)
        {
            var button = _root.Q<Button>("MobileRate");
            if (button == null)
            {
                return;
            }
            button.text = L.Get(
                "falsum.editor.playbackRate",
                ("rate", rate.ToString(
                    Math.Abs(rate - 1.0) < 0.0001 ? "0.0" : "0.##",
                    CultureInfo.InvariantCulture)));
            button.EnableInClassList(
                "is-active",
                Math.Abs(rate - 1.0) >= 0.0001);
        }

        // ---- Shared ------------------------------------------------------

        /// <summary>
        /// Binds a rail/float button.
        ///
        /// THE LOG LINE IS LOAD-BEARING, not leftover debug noise: a button that
        /// clicks but does nothing has exactly two possible causes -- the shell
        /// event has no subscriber, or the subscriber exists and the editor
        /// ignored the action. Printing the click and whether a handler ran
        /// separates those in one reproduction instead of in a code review.
        /// </summary>
        private void BindButton(string name, Action handler)
        {
            var button = _root.Q<Button>(name);
            if (button == null)
            {
                Debug.LogWarning(
                    "[Falsum] mobile control missing from UXML: " + name);
                return;
            }
            Action callback = delegate
            {
                Debug.Log("[Falsum] mobile click: " + name);
                handler();
            };
            button.clicked += callback;
            _unbind.Add(() => button.clicked -= callback);
        }

        private void Raise(Action action)
        {
            if (action == null)
            {
                Debug.LogWarning(
                    "[Falsum] mobile action has NO subscriber; the button will appear to do nothing.");
                return;
            }
            action();
        }

        private void Raise<T>(Action<T> action, T value)
        {
            if (action != null)
            {
                action(value);
            }
        }

        /// <summary>
        /// Captions. The audit forbids `text=` literals in UXML, so every
        /// visible string is bound here from the locale files like any other
        /// string in the editor. The type buttons use the NOTE KIND names
        /// (TAP/HOLD/FLICK/SKYAREA), not gesture verbs: they select what gets
        /// placed, and the gesture is how you place it.
        /// </summary>
        private void ApplyChromeText()
        {
            BindText("MobileMenu", "falsum.mobile.menu");
            BindText("MobileUndo", "falsum.mobile.undo");
            BindText("MobileRedo", "falsum.mobile.redo");
            BindText("MobileMultiSelect", "falsum.mobile.multiSelect");
            BindText("MobilePlaceTap", "falsum.noteKind.tap");
            BindText("MobilePlaceHold", "falsum.noteKind.hold");
            BindText("MobilePlaceFlick", "falsum.noteKind.flick");
            BindText("MobilePlaceSkyArea", "falsum.noteKind.skyArea");
            BindText("MobileEvents", "falsum.mobile.events");
            BindText("MobileGrid", "falsum.mobile.grid");
            _gridPicker?.RefreshText();
            BindText("MobileMirror", "falsum.mobile.mirror");
            BindText("MobileCopy", "falsum.mobile.copy");
            BindText("MobilePaste", "falsum.mobile.paste");
            BindText(
                "MobileSegmentSelectRight",
                "falsum.mobile.segmentSelect");
            BindText("MobileRate", "falsum.mobile.rate");
            BindText("MobileDelete", "falsum.mobile.delete");
            BindText("MobileBpmCaption", "falsum.mobile.bpm");
            BindText("MobileSubCaption", "falsum.mobile.subdivision");
            BindText(
                "MobileAudioPreprocess",
                "falsum.mobile.audioPreprocess");
            BindText("MobilePlayPause", "falsum.mobile.playPause");
            BindText("StageAbandon", "falsum.mobile.stage.abandon");
            // The three stage markers are shapes, not text: they carry no
            // caption and must not be bound to a string.
            BindText("MobileFlowCaption", "falsum.mobile.flow");
            var flowValue = _root.Q<Label>("MobileFlowValue");
            if (flowValue != null)
            {
                var slider = _root.Q<Slider>("MobileFlowSlider");
                flowValue.text = slider != null
                    ? slider.value.ToString("0.0")
                    : "5.0";
            }
        }

        private void RefreshChromeText()
        {
            ApplyChromeText();
            foreach (var name in new[] { "MobileUndo", "MobileRedo", "MobilePlayPause", "MobileAudition" })
            {
                var button = _root.Q<Button>(name);
                if (button?.Q<Image>(className: "mobile-icon") != null) button.text = string.Empty;
            }
        }

        private void BindText(string elementName, string key)
        {
            var text = L.Get(key);
            var button = _root.Q<Button>(elementName);
            if (button != null)
            {
                button.text = text;
                return;
            }
            var label = _root.Q<Label>(elementName);
            if (label != null)
            {
                label.text = text;
            }
        }

        /// <summary>
        /// Panel used by the view to render the song card into the right
        /// column without the card escaping it.
        /// </summary>
        public VisualElement SongCardHost
        {
            get { return _songCardHost; }
        }

        /// <summary>Where the numeric keypad is built.</summary>
        public VisualElement KeypadHost
        {
            get { return _keypadHost; }
        }
    }
}
