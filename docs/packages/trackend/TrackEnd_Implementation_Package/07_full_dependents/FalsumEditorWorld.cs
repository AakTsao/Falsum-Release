using System;
using System.Collections.Generic;
using System.Globalization;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.UI;
using InFalsus.Gameplay.Presentation.Notes;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.UI;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    [Flags]
    public enum FalsumNotePickMask
    {
        None = 0,
        Tap = 1 << 0,
        Hold = 1 << 1,
        Flick = 1 << 2,
        SkyArea = 1 << 3,
        GroundNotes = Tap | Hold,
        SkyNotes = Flick | SkyArea,
        All = GroundNotes | SkyNotes,
    }

    public struct FalsumEditorGhost
    {
        public bool Visible;
        public NoteKind Kind;
        public double TimeMs;
        public double EndTimeMs;
        public double StartPos;
        public double EndPos;
        public double StartWidth;
        public double EndWidth;
        public int Lane;
        public int WidthLanes;
        public int FlickDirection;
        /// <summary>
        /// SkyArea easing, 0..2 exactly as <see cref="ChartNote.LeftEasing"/>.
        /// Without these the placed/pasted ghost drew a straight trapezoid even
        /// though the preset being placed carries real easing, so the preview
        /// showed a silhouette that would never land on the chart.
        /// </summary>
        public int LeftEasing;
        public int RightEasing;
        public Color Color;
        public bool EndAsPoint;
        /// <summary>
        /// Draw the rectangular rim around the premium mesh. Point-place turns
        /// this off for SkyArea so the preview shows only the shape being
        /// drawn; paste previews keep it.
        /// </summary>
        public bool DrawRim;
    }

    public enum FalsumRulerSurface
    {
        /// <summary>Ruler sits at the sky/note plane, hanging down from it.</summary>
        Sky,
        /// <summary>Ruler sits on the track floor, standing up from it.</summary>
        Ground,
    }

    /// <summary>
    /// Everything the GRID ruler needs to draw one frame. Replaces the old
    /// <c>(snapLine, snapDivisions, snapColor)</c> trio: the same divisions
    /// drive the ruler ticks and the mode plane's info box, so they cannot
    /// drift apart.
    ///
    /// <para>Absent markers are expressed as <see cref="float.NaN"/>, not a
    /// magic cell number: cell <c>0</c> is a legitimate start position, so a
    /// numeric sentinel would be ambiguous.</para>
    /// </summary>
    public struct FalsumRulerState
    {
        public bool Visible;
        public int Divisions;
        public int FocusLine;
        public float StartNormalized;
        public float EndNormalized;
        public Color Color;
        /// <summary>
        /// Which surface the ruler belongs to. Sky notes (FLICK / SKYAREA and
        /// the middle-edit channels that only apply to them) get a ruler at the
        /// sky plane; ground notes (TAP / HOLD) get one on the track floor.
        /// Drawing both at a fixed height put the ruler in mid-air, unrelated to
        /// the note being edited.
        /// </summary>
        public FalsumRulerSurface Surface;
        /// <summary>
        /// Lateral position (0..1 across the track) the guide line runs at,
        /// already snapped to the nearest ruler cell by the caller.
        /// <see cref="float.NaN"/> means "no pointer, draw no guide".
        /// </summary>
        public float GuideNormalized;

        public static FalsumRulerState Hidden
        {
            get
            {
                return new FalsumRulerState
                {
                    Visible = false,
                    Divisions = 0,
                    FocusLine = -1,
                    StartNormalized = float.NaN,
                    EndNormalized = float.NaN,
                    Color = Color.white,
                    Surface = FalsumRulerSurface.Sky,
                    GuideNormalized = float.NaN,
                };
            }
        }
    }

    public sealed class FalsumEditorWorld
    {
        private const float TrackHalfWidth = 0.81f;
        private const float SkyY = 0.4698f;

        // Ruler geometry, as fractions of ModePlaneWorldHeight / TrackHalfWidth.
        // The baseline itself lies ON the owning surface (sky plane or floor);
        // only the tick lengths are fractional. The bar previously sat 0.34*height
        // away from that surface, which put it in mid-air -- neither in the sky
        // nor on the ground.
        // Vertical centre of the mode plane for each surface: sky work reads in
        // the upper half (near the sky notes), ground work in the lower half
        // (near the track floor).
        private const float SkyPlaneCenterFraction = 0.62f;
        private const float GroundPlaneCenterFraction = 0.38f;
        private const float RulerMajorTickFraction = 0.12f;
        private const float RulerMidTickFraction = 0.085f;
        private const float RulerShortTickFraction = 0.055f;
        private const float RulerStartMarkerFraction = 0.05f;
        private const float RulerEndMarkerFraction = 0.045f;
        // Minimum on-screen separation before short ticks start being
        // strided away. Mirrors the 2D world's visibleStep guard.
        private const float RulerMinTickPixels = 4.0f;

        // Anchor sizing, in canvas-local units. The drawn glyph is the small
        // number and the grab radius the large one: a handle that is easy to
        // hit and small enough not to cover the box it belongs to.
        // Raised twice on user feedback ("too small", then "still small").
        private const float AnchorMinGlyph = 7.0f;
        private const float AnchorMaxGlyph = 11.0f;
        private const float AnchorGlyphRadiusScale = 0.18f;
        private const float AnchorGlyphSizeRatio = 0.34f;
        private const float AnchorMinHitRadius = 20.0f;
        private const float AnchorHitRadiusScale = 1.7f;
        private const float AnchorOffsetBase = 18.0f;

        /// <summary>
        /// Multiplier on the screen-pixel grab bound. Anchors must win over
        /// note picking for a click that is visually on a handle, so the
        /// resolution-converted pixel radius is widened before being compared
        /// against the per-anchor radius. Without this the pixel bound never
        /// took effect (the fixed per-anchor radius always won) and the grab
        /// area silently shrank as the reference resolution grew.
        /// </summary>
        private const float AnchorPickSafetyScale = 1.35f;

        private const float LineThickness = 0.018f;
        private const float GridReferenceScreenHeight = 1080.0f;

        private sealed class MeshBuffer
        {
            public GameObject Root;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            /// <summary>
            /// Optional 3-component UV channel. Only the guide mesh fills it:
            /// x = side sign, y = half-width in pixels, z = depth 0..1. Kept
            /// separate from <see cref="Uvs"/> so every other mesh keeps its
            /// 2-component channel.
            /// </summary>
            public List<Vector3> Uv3;
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();

            public void Clear()
            {
                Vertices.Clear();
                Colors.Clear();
                Uvs.Clear();
                if (Uv3 != null)
                {
                    Uv3.Clear();
                }
                Normals.Clear();
                Triangles.Clear();
            }

            public void Apply()
            {
                if (Vertices.Count == 0)
                {
                    if (Renderer.enabled)
                    {
                        Mesh.Clear();
                        Renderer.enabled = false;
                    }
                    return;
                }
                Mesh.Clear();
                Renderer.enabled = true;
                Mesh.SetVertices(Vertices);
                Mesh.SetColors(Colors);
                if (Uv3 != null && Uv3.Count == Vertices.Count)
                {
                    Mesh.SetUVs(0, Uv3);
                }
                else
                {
                    Mesh.SetUVs(0, Uvs);
                }
                Mesh.SetNormals(Normals);
                Mesh.SetTriangles(Triangles, 0, false);
                Mesh.RecalculateBounds();
            }
        }

        private sealed class Outline
        {
            public GameObject Root;
            public LineRenderer[] Lines;
        }

        private sealed class PickCandidate
        {
            public ChartNote Note;
            public int Layer;
            public float Distance;
        }

        private sealed class SideGeometry
        {
            public Transform Transform;
            public Vector3 JudgementWorld;
            public Vector3 EndWorld;
            public Vector3 JudgementLocal;
            public Vector3 EndLocal;
            public float WidthMin;
            public float WidthMax;
            public float CenterX;
        }

        private readonly GameplaySceneBinding _binding;
        private readonly Chart _chart;
        private readonly TrackMotion _motion;
        private readonly FalsumBeatGrid _grid;

        /// <summary>
        /// Cache for <see cref="TryBuildSkyAreaScreenPolygon"/>. Everything the
        /// method computes EXCEPT the view-space Z is independent of the
        /// playhead: the eased X edges come from the note's own geometry, and
        /// the per-sample scaled time comes from <c>ScaledTimeAt(sampleTime)</c>
        /// which only changes when the chart's speed map changes.
        ///
        /// Only <c>rawZ = ScaledTimeAt(sampleTime) - ScaledTimeAt(nowMs)</c> moves
        /// per frame. Caching the rest turns a selected 21.5-second skyarea from
        /// ~8622 ScaledTimeAt calls plus ~200 KB of allocations per frame into
        /// one Z recompute per sample and zero allocation.
        ///
        /// The cache key carries the note reference AND the chart timeline
        /// version, so an edit (or a track() change) can never be served stale.
        /// </summary>
        private ChartNote _skyPolyCacheNote;
        private int _skyPolyCacheVersion = -1;
        private int _skyPolyCacheSteps = -1;
        private float _skyPolyCacheLocalY;
        private float _skyPolyCacheBaseZ;
        private int _skyPolyCacheLeftEasing;
        private int _skyPolyCacheRightEasing;
        private float _skyPolyCacheStartCenter;
        private float _skyPolyCacheEndCenter;
        private float _skyPolyCacheStartWidth;
        private float _skyPolyCacheEndWidth;
        /// <summary>
        /// The note's own time span. These feed
        /// <c>ScaledTimeAt(TimeMs + DurationMs * fraction)</c>, and a drag can
        /// change them WITHOUT rebuilding the timeline (CommitChange only
        /// normalises sky grouping, it does not bump TimelineVersion) — so
        /// they have to be part of the cache key, not just the version.
        /// </summary>
        private double _skyPolyCacheTimeMs;
        private double _skyPolyCacheDurationMs;
        /// <summary>Per-sample eased X for the left and right edge, interleaved.</summary>
        private float[] _skyPolyCacheLeftEdge;
        private float[] _skyPolyCacheRightEdge;
        /// <summary>Per-sample scaled time (ScaledTimeAt at the sample's own time).</summary>
        private double[] _skyPolyCacheScaledTime;
        /// <summary>Reusable output buffers, so the frame path stops allocating.</summary>
        private readonly List<Vector3> _skyPolyLeftScratch =
            new List<Vector3>();
        private readonly List<Vector3> _skyPolyRightScratch =
            new List<Vector3>();
        private readonly List<Vector3> _skyPolyPointsScratch =
            new List<Vector3>();
        private readonly List<Vector2> _skyPolyScreenScratch =
            new List<Vector2>();
        private readonly InFalsus.Gameplay.Presentation.Notes.NoteRenderer
            _noteRenderer;
        private readonly RectTransform _canvasRect;
        private readonly FalsumSelectionOverlayGraphic _selectionOverlay;
        private readonly FalsumMeasureNumberRenderer _measureNumbers;
        private readonly FalsumTrackSpectrumRenderer _spectrumRenderer;
        private readonly Transform _master;
        private readonly Transform _skyRoot;
        private readonly Transform _line;
        private readonly Vector3 _lineLocal;
        private readonly float _trackEndZ;
        private readonly float _floorY;
        private readonly float[] _laneX;
        private readonly MeshBuffer _gridMesh;
        private readonly MeshBuffer _rulerMesh;
        private readonly MeshBuffer _guideMesh;
        private readonly MeshBuffer _overlayMesh;
        private readonly MeshBuffer _planeMesh;
        private readonly MeshBuffer _ghostMesh;
        private readonly MeshBuffer _cursorMesh;
        private readonly SideGeometry _leftGeometry;
        private readonly SideGeometry _rightGeometry;
        private readonly List<Outline> _outlines = new List<Outline>();
        private readonly List<ChartNote> _editorGhostNotes =
            new List<ChartNote>();
        private FalsumChartLayer _visibleLayers = FalsumChartLayer.Both;
        private Material _lineMaterial;
        private string _lastGhostKey;

        public int SelectionPolygonCount { get; private set; }

        /// <summary>
        /// Anchors drawn for the single selected note this frame. Zero when
        /// nothing, or more than one note, is selected.
        /// </summary>
        public int SelectionAnchorCount { get; private set; }

        /// <summary>
        /// Handles the pointer is over this frame, in canvas-local space.
        /// Returns null when nothing (or more than one note) is selected.
        /// </summary>
        /// <summary>
        /// Screen point to canvas-local point, the space anchors live in.
        /// Both sides of the hit test must use this one conversion.
        /// </summary>
        public bool TryScreenToCanvas(
            Vector2 screen,
            out Vector2 canvas)
        {
            canvas = Vector2.zero;
            if (_canvasRect == null)
            {
                return false;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                screen,
                null,
                out canvas);
        }

        /// <summary>
        /// Canvas-local point back to screen, for driving a drag in the same
        /// space the anchors were hit-tested in.
        /// </summary>
        public bool TryCanvasToScreen(
            Vector2 canvas,
            out Vector2 screen)
        {
            screen = Vector2.zero;
            if (_canvasRect == null)
            {
                return false;
            }
            var world = _canvasRect.TransformPoint(canvas);
            var camera = Camera.main;
            if (camera != null &&
                _canvasRect.GetComponent<Canvas>() != null &&
                _canvasRect.GetComponent<Canvas>().renderMode ==
                RenderMode.ScreenSpaceOverlay)
            {
                screen = world;
                return true;
            }
            if (camera == null)
            {
                return false;
            }
            var projected = camera.WorldToScreenPoint(world);
            if (projected.z <= 0.0f)
            {
                return false;
            }
            screen = projected;
            return true;
        }

        /// <summary>
        /// Finds the anchor under the pointer. The radius is in canvas-local
        /// units derived from a screen-pixel radius so the grab area does not
        /// shrink on high resolutions.
        ///
        /// Two-tier pick: the highest <see cref="FalsumSelectionAnchor.Priority"/>
        /// wins first, and only then the nearest one. A click-only control
        /// (flick colour, skyarea easing) therefore keeps working even when a
        /// corner handle overlaps it, which is common on a small or narrow box.
        /// </summary>
        public FalsumSelectionAnchor FindAnchorAt(
            Vector2 screenPosition,
            float screenRadiusPixels,
            out Vector2 canvasPosition,
            out int anchorIndex)
        {
            canvasPosition = Vector2.zero;
            anchorIndex = -1;
            if (AnchorCount <= 0 ||
                !TryScreenToCanvas(screenPosition, out canvasPosition))
            {
                return null;
            }
            // The screen-pixel radius is a LOWER BOUND in canvas space. That
            // distinction matters: with `Mathf.Max(fallback, anchor.HitRadius)`
            // and a fixed 20-canvas HitRadius, `fallback` never won, so the
            // grab area was a constant number of CANVAS units and therefore
            // shrank on screen as the reference resolution grew. On a high-DPI
            // display the handle's real grab area got small enough that a
            // click landed inside a NEIGHBOURING NOTE's pick region instead --
            // reported as "clicking an anchor selects the note under it".
            //
            // Anchors have to beat note picking for a click that is visually
            // on them, so the tolerance is resolution-aware and generous: the
            // screen-space pixel bound is scaled up and combined with the
            // per-anchor radius.
            var fallback = ScreenRadiusToCanvas(screenRadiusPixels);
            FalsumSelectionAnchor best = null;
            var bestDistance = float.MaxValue;
            var bestPriority = int.MinValue;
            for (var i = 0; i < AnchorCount; i++)
            {
                var anchor = AnchorAt(i);
                if (anchor == null)
                {
                    continue;
                }
                // Per-anchor radius, widened by the resolution-aware screen
                // bound so a handle always outranks the note geometry around
                // it for a click that visually lands on the handle.
                var radius = Mathf.Max(
                    anchor.HitRadius,
                    fallback * AnchorPickSafetyScale);
                var distance =
                    (canvasPosition - anchor.HitCenter).sqrMagnitude;
                if (distance > radius * radius)
                {
                    continue;
                }
                if (anchor.Priority < bestPriority)
                {
                    continue;
                }
                if (anchor.Priority > bestPriority || distance < bestDistance)
                {
                    bestPriority = anchor.Priority;
                    bestDistance = distance;
                    best = anchor;
                    anchorIndex = i;
                }
            }
            return best;
        }

        /// <summary>
        /// The anchor whose region the pointer is merely near, used to show the
        /// "you can grab this" affordance before the click actually lands.
        /// </summary>
        public int FindAnchorNear(
            Vector2 screenPosition,
            float screenRadiusPixels)
        {
            Vector2 canvas;
            int index;
            return FindAnchorAt(
                screenPosition, screenRadiusPixels, out canvas, out index) != null
                ? index
                : -1;
        }

        public int AnchorCount
        {
            get { return _selectionOverlay != null
                ? _selectionOverlay.AnchorCount
                : 0; }
        }

        public FalsumSelectionAnchor AnchorAt(int index)
        {
            return _selectionOverlay != null
                ? _selectionOverlay.AnchorAt(index)
                : null;
        }

        /// <summary>
        /// Highlights the anchor the pointer is near or dragging so the user
        /// sees which handle is armed before committing the click.
        /// </summary>
        public void SetAnchorHighlight(int hoveredIndex, bool dragging)
        {
            if (_selectionOverlay != null)
            {
                _selectionOverlay.SetAnchorHighlight(
                    hoveredIndex, dragging);
            }
        }

        private float ScreenRadiusToCanvas(float screenRadiusPixels)
        {
            Vector2 a;
            Vector2 b;
            if (!TryScreenToCanvas(Vector2.zero, out a) ||
                !TryScreenToCanvas(
                    new Vector2(screenRadiusPixels, 0.0f),
                    out b))
            {
                return screenRadiusPixels;
            }
            return Mathf.Max(1.0f, Mathf.Abs(b.x - a.x));
        }
        public int SelectionVertexCount { get; private set; }
        public int SelectionAttemptCount { get; private set; }
        public string SelectionBuildDebug { get; private set; } = "none";
        public int SelectionBuildSequence { get; private set; }
        public double SelectionBuildNowMs { get; private set; }
        public int CursorVertexCount
        {
            get { return _cursorMesh.Vertices.Count; }
        }
        public int TimePlaneVertexCount
        {
            get { return _planeMesh.Vertices.Count; }
        }
        public int GhostVertexCount
        {
            get { return _ghostMesh.Vertices.Count; }
        }
        public int GridVertexCount
        {
            get { return _gridMesh.Vertices.Count; }
        }
        public int GhostTriangleCount
        {
            get { return _ghostMesh.Triangles.Count / 3; }
        }
        public bool GhostEndsAsPoint { get; private set; }
        public string GhostScreenDebug { get; private set; } = "none";
        public string SelectionDebugSummary
        {
            get
            {
                return _selectionOverlay != null
                    ? _selectionOverlay.DebugSummary
                    : "none";
            }
        }

        public void SetSelectionOverlayVisible(bool visible)
        {
            if (_selectionOverlay == null)
            {
                return;
            }
            _selectionOverlay.enabled = visible;
            if (_measureNumbers != null)
            {
                _measureNumbers.SetVisible(visible);
            }
            if (!visible)
            {
                _selectionOverlay.SetPolygons(null);
            }
        }

        public int VisibleMeasureNumberCount
        {
            get
            {
                return _measureNumbers != null
                    ? _measureNumbers.VisibleBadgeCount
                    : 0;
            }
        }

        public string DebugMeasureNumberSummary
        {
            get
            {
                return _measureNumbers != null
                    ? _measureNumbers.DebugSummary +
                      " " + MeasureNumberDebug
                    : "none";
            }
        }

        public string DebugDescribeNoteGeometry(
            ChartNote note,
            double nowMs)
        {
            if (note == null || _noteRenderer == null)
            {
                return "note=none";
            }
            var points = new List<Vector3>();
            var hasPoints = _noteRenderer.TryGetNoteWorldPoints(
                note,
                nowMs,
                points);
            Vector2[] polygon;
            var hasPolygon = TryBuildNoteScreenPolygon(
                note,
                nowMs,
                out polygon);
            return note.Kind + "/lane" + note.StartLane +
                   " worldPoints=" + points.Count +
                   " hasPoints=" + hasPoints +
                   " polygon=" + (hasPolygon
                       ? polygon.Length.ToString()
                       : "0") +
                   " " + _noteRenderer.DebugDescribeNoteVisual(note);
        }

        public FalsumEditorWorld(
            GameplaySceneBinding binding,
            Chart chart,
            TrackMotion motion,
            FalsumBeatGrid grid,
            InFalsus.Gameplay.Presentation.Notes.NoteRenderer noteRenderer,
            Canvas canvas)
        {
            _binding = binding;
            _chart = chart;
            _motion = motion;
            _grid = grid;
            _noteRenderer = noteRenderer;
            _canvasRect = canvas != null
                ? canvas.transform as RectTransform
                : null;
            _selectionOverlay = CreateSelectionOverlay(canvas);
            _measureNumbers = new FalsumMeasureNumberRenderer(
                binding,
                canvas);
            _master = binding.MasterScene;
            _skyRoot = binding.SkyTemplate != null
                ? binding.SkyTemplate.transform.parent
                : binding.MasterScene;
            _line = binding.JudgementLine;
            _lineLocal = _master.InverseTransformPoint(_line.position);
            _laneX = binding.LaneCenterX;
            var floorSource = _master.InverseTransformPoint(
                binding.NoteTemplates[2].transform.position);
            _floorY = floorSource.y + 0.004f;
            _leftGeometry = CreateSideGeometry(
                binding,
                binding.LeftSideLane);
            _rightGeometry = CreateSideGeometry(
                binding,
                binding.RightSideLane);
            _trackEndZ = ResolveTrackEndZ(
                _master,
                _leftGeometry,
                _rightGeometry,
                _lineLocal.z);
            _spectrumRenderer = new FalsumTrackSpectrumRenderer(
                _master,
                _lineLocal.z,
                _trackEndZ,
                _floorY,
                TrackHalfWidth);

            _gridMesh = CreateMesh("FalsumEditorGrid", 3030, true);
            // The ruler sits just above the grid and just below the shared
            // line material: it must draw over the beat lines (it belongs to
            // the mode plane, which is foreground) but never over the plane's
            // own captions.
            _rulerMesh = CreateMesh("FalsumEditorRuler", 3032, true);
            // Dedicated mesh with its own depth-tested material: the guide has
            // to be occluded and foreshortened, which the grid's ZTest Always
            // line material would defeat.
            _guideMesh = CreateGuideMesh();
            _overlayMesh = CreateMesh("FalsumEditorSelection", 3010);
            _planeMesh = CreateMesh("FalsumEditorTimePlane", 3000);
            _ghostMesh = CreateMesh("FalsumEditorGhost", 3005);
            _cursorMesh = CreateMesh("FalsumEditorCursor", 3020);
        }

        public void Dispose()
        {
            DestroyObject(_gridMesh.Root);
            DestroyObject(_rulerMesh.Root);
            DestroyObject(_guideMesh.Root);
            DestroyObject(_overlayMesh.Root);
            DestroyObject(_planeMesh.Root);
            DestroyObject(_ghostMesh.Root);
            DestroyObject(_cursorMesh.Root);
            _spectrumRenderer.Dispose();
            if (_selectionOverlay != null)
            {
                DestroyObject(
                    _selectionOverlay.transform.parent.gameObject);
            }
            if (_noteRenderer != null)
            {
                _noteRenderer.ClearEditorGhost();
            }
            for (var i = 0; i < _outlines.Count; i++)
            {
                DestroyObject(_outlines[i].Root);
            }
            _outlines.Clear();
        }

        public void ClearGhostPreview()
        {
            _ghostMesh.Clear();
            _ghostMesh.Apply();
            _cursorMesh.Clear();
            _cursorMesh.Apply();
            _editorGhostNotes.Clear();
            if (_noteRenderer != null)
            {
                _noteRenderer.ClearEditorGhost();
            }
            _lastGhostKey = null;
            GhostEndsAsPoint = false;
            GhostScreenDebug = "none";
        }

        public void SetLayerVisibility(FalsumChartLayer layers)
        {
            _visibleLayers = layers & FalsumChartLayer.Both;
        }

        public bool SpectrumReady
        {
            get { return _spectrumRenderer.HasTexture; }
        }

        public string SpectrumDebugSummary
        {
            get { return _spectrumRenderer.DebugSummary; }
        }

        public void SetSpectrumTexture(
            Texture2D texture,
            double segmentStartMs,
            double segmentDurationMs)
        {
            _spectrumRenderer.SetTexture(
                texture,
                segmentStartMs,
                segmentDurationMs);
        }

        public void ClearSpectrumTexture()
        {
            _spectrumRenderer.ClearTexture();
        }

        public void UpdateSpectrum(
            Chart chart,
            double chartTimeMs,
            double audioDurationMs,
            double delayMs,
            double flowSpeed,
            bool visible,
            bool isTwoDimensional)
        {
            _spectrumRenderer.Update(
                chart,
                chartTimeMs,
                audioDurationMs,
                delayMs,
                flowSpeed,
                visible,
                isTwoDimensional);
        }

        public void Refresh(
            double nowMs,
            bool showGrid,
            IList<ChartNote> selected,
            double? timePlaneMs,
            int flickDivision,
            FalsumEditorGhost ghost,
            IList<FalsumEditorGhost> pasteGhosts,
            Color planeTint,
            FalsumRulerState ruler,
            bool showMeasureNumbers = true)
        {
            BuildGrid(nowMs, showGrid, flickDivision, timePlaneMs);
            BuildMeasureNumbers(nowMs, showMeasureNumbers);
            BuildSelection(nowMs, selected);
            BuildTimePlane(nowMs, timePlaneMs, planeTint);
            BuildRuler(nowMs, timePlaneMs, ruler);
            BuildGuide(ruler);
            UpdateGhosts(nowMs, ghost, pasteGhosts);
            BuildSnapArrow(
                nowMs,
                timePlaneMs,
                ruler.FocusLine,
                ruler.Divisions,
                ruler.Color);
            _gridMesh.Apply();
            _rulerMesh.Apply();
            _guideMesh.Apply();
            _overlayMesh.Apply();
            _planeMesh.Apply();
            _ghostMesh.Apply();
            _cursorMesh.Apply();
        }

        private void BuildSnapArrow(
            double nowMs,
            double? timePlaneMs,
            int snapLine,
            int snapDivisions,
            Color color)
        {
            if (!timePlaneMs.HasValue ||
                snapDivisions <= 0 ||
                snapLine < 0)
            {
                return;
            }
            var divisions = Mathf.Clamp(snapDivisions, 1, 192);
            var line = Mathf.Clamp(snapLine, 0, divisions);
            var center = ModePlaneLocalPosition(
                timePlaneMs.Value,
                nowMs);
            var top = center.y + ModePlaneWorldHeight() * 0.5f;
            var x = Mathf.Lerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                line / (float)divisions);
            var z = center.z - 0.012f;
            AddTriangle(
                _cursorMesh,
                new Vector3(x, top + 0.005f, z),
                new Vector3(x - 0.055f, top + 0.105f, z),
                new Vector3(x + 0.055f, top + 0.105f, z),
                color);
        }

        /// <summary>
        /// Draws the GRID ruler as a horizontal bar inside the time plane.
        ///
        /// <para>Ticks are drawn with <see cref="AddScreenSpaceLineQuad"/>,
        /// i.e. the exact primitive measure lines use, so a tick and a measure
        /// line are the same kind of stroke: screen-pixel width extruded by the
        /// shader, glow pass first, `ZTest Always`. There is no world-unit
        /// thickness anywhere in this method.</para>
        ///
        /// <para>Tier is decided by integer divisibility, never by a float
        /// tolerance: quarters land on majors and eighths on mids, so a
        /// denominator whose eighths are non-integral (50, 100) simply loses
        /// the middle tier instead of producing uneven spacing.</para>
        /// </summary>
        private void BuildRuler(
            double nowMs,
            double? timePlaneMs,
            FalsumRulerState ruler)
        {
            _rulerMesh.Clear();
            if (!ruler.Visible ||
                !timePlaneMs.HasValue ||
                ruler.Divisions <= 0)
            {
                return;
            }
            var divisions = Mathf.Clamp(
                ruler.Divisions,
                1,
                FalsumBeatGrid.MaxGridDenominator);
            var center = ModePlaneLocalPosition(
                timePlaneMs.Value,
                nowMs);
            var height = ModePlaneWorldHeight();
            // The ruler sits ON the surface its note lives on: the sky plane
            // for FLICK/SKYAREA, the track floor for TAP/HOLD. Do not offset it
            // away from that surface -- a ruler floating at some fraction of the
            // track height is neither "in the sky" nor "on the ground", which is
            // what the previous 0.34*height baseline did.
            var skySurface =
                ruler.Surface == FalsumRulerSurface.Sky;
            var baselineY = skySurface
                ? _lineLocal.y + SkyY
                : _floorY;
            // Ticks grow upward off the surface: below the floor is underground
            // and below the sky plane would bury the ruler in the notes it
            // annotates. Up is the only unobstructed direction for both surfaces.
            var tickDirection = 1.0f;
            var z = center.z;
            var color = ruler.Color;
            var coreColor = color;
            coreColor.a = Mathf.Max(0.35f, color.a);
            var glowColor = color;
            glowColor.a *= 0.30f;

            var resolutionScale = Mathf.Max(
                0.5f,
                Screen.height / GridReferenceScreenHeight);
            var widthScale = _grid.LineWidthScale * resolutionScale;

            // Baseline: the bar every tick grows from.
            EmitRulerStroke(
                new Vector3(-TrackHalfWidth, baselineY, z),
                new Vector3(TrackHalfWidth, baselineY, z),
                2.2f * widthScale,
                coreColor,
                glowColor);

            // On-screen spacing of one cell, used to stride short ticks away
            // once they would alias. Computed once from the baseline endpoints
            // rather than per tick.
            var pixelsPerCell = RulerCellPixels(baselineY, z, divisions);
            var stride = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    RulerMinTickPixels /
                    Mathf.Max(0.0001f, pixelsPerCell)));

            for (var i = 0; i <= divisions; i++)
            {
                var x = Mathf.Lerp(
                    -TrackHalfWidth,
                    TrackHalfWidth,
                    i / (float)divisions);
                var tier = ResolveTickClass(i, divisions);
                if (tier == RulerTickClass.Short && i % stride != 0)
                {
                    continue;
                }
                float fraction;
                float widthPixels;
                switch (tier)
                {
                    case RulerTickClass.Major:
                        fraction = RulerMajorTickFraction;
                        widthPixels = 2.6f;
                        break;
                    case RulerTickClass.Mid:
                        fraction = RulerMidTickFraction;
                        widthPixels = 2.2f;
                        break;
                    default:
                        fraction = RulerShortTickFraction;
                        widthPixels = 1.8f;
                        break;
                }
                EmitRulerStroke(
                    new Vector3(x, baselineY, z),
                    new Vector3(
                        x,
                        baselineY + tickDirection * height * fraction,
                        z),
                    widthPixels * widthScale,
                    coreColor,
                    glowColor);
            }

            // Focus tick: the snap the pointer is currently landing on.
            if (ruler.FocusLine >= 0)
            {
                var focus = Mathf.Clamp(
                    ruler.FocusLine,
                    0,
                    divisions);
                var x = Mathf.Lerp(
                    -TrackHalfWidth,
                    TrackHalfWidth,
                    focus / (float)divisions);
                var focusTall = baselineY +
                                tickDirection *
                                height * RulerMajorTickFraction;
                EmitRulerStroke(
                    new Vector3(x, baselineY, z),
                    new Vector3(x, focusTall, z),
                    3.6f * widthScale,
                    coreColor,
                    glowColor);
            }

            BuildRulerSpan(
                ruler.StartNormalized,
                ruler.EndNormalized,
                baselineY,
                height,
                z,
                tickDirection);
            BuildRulerMarker(
                ruler.StartNormalized,
                baselineY,
                z,
                MarkerShape.Diamond,
                coreColor);
            BuildRulerMarker(
                ruler.EndNormalized,
                baselineY,
                z,
                MarkerShape.Square,
                coreColor);
        }

        private enum RulerTickClass
        {
            Short,
            Mid,
            Major,
        }

        private enum MarkerShape
        {
            Diamond,
            Square,
        }

        /// <summary>
        /// Quarter cells are majors, eighth cells are mids, everything else is
        /// short. Integer arithmetic only: <c>i * 8</c> cannot overflow at the
        /// denominator ceiling (192 * 8 = 1536).
        /// </summary>
        private static RulerTickClass ResolveTickClass(
            int index,
            int divisions)
        {
            if ((index * 4) % divisions == 0)
            {
                return RulerTickClass.Major;
            }
            if ((index * 8) % divisions == 0)
            {
                return RulerTickClass.Mid;
            }
            return RulerTickClass.Short;
        }

        /// <summary>
        /// Draws the pointer guide: a world-space strip running along the track
        /// (the note-travel axis) at the snapped lateral cell under the cursor.
        ///
        /// <para>This is deliberately NOT the measure-line primitive. Measure
        /// lines are screen-pixel quads with <c>ZTest Always</c>: they keep a
        /// constant width at every depth and draw over everything. A guide drawn
        /// that way reads as a decal pasted on the screen, with no sense of the
        /// track receding. Here the strip is real geometry lying in the plane, so
        /// the perspective converges it toward the vanishing point and the
        /// depth test lets notes occlude it.</para>
        /// </summary>
        private void BuildGuide(FalsumRulerState ruler)
        {
            _guideMesh.Clear();
            if (!ruler.Visible ||
                float.IsNaN(ruler.GuideNormalized))
            {
                return;
            }
            var zNear = _lineLocal.z;
            var zFar = _trackEndZ;
            if (zFar <= zNear + 0.001f)
            {
                return;
            }
            var x = Mathf.Lerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                Mathf.Clamp01(ruler.GuideNormalized));
            // The strip width follows the cell width, with a floor so a dense
            // denominator still reads as a line rather than vanishing.
            var cellWidth = TrackHalfWidth * 2.0f /
                            Mathf.Max(1, ruler.Divisions);
            var halfWidth = Mathf.Clamp(
                cellWidth * 0.5f,
                TrackHalfWidth * 0.004f,
                TrackHalfWidth * 0.02f);
            // The line has to lie ON the surface it annotates; floating between
            // them would make the depth cue meaningless. Lift it a hair though:
            // at exactly the surface depth the strip and the track floor/wall
            // z-fight under ZTest LEqual and the line stutters.
            var y = (ruler.Surface == FalsumRulerSurface.Sky
                ? _lineLocal.y + SkyY
                : _floorY) + GuideSurfaceLift;
            var color = ruler.Color;
            color.a = Mathf.Max(0.55f, color.a);
            AddGuideStrip(
                x - halfWidth,
                x + halfWidth,
                y,
                zNear,
                zFar,
                color);
        }

        /// <summary>
        /// Emits the guide as a quad strip split into a few segments, so the
        /// per-vertex depth parameter can fade the line toward the far end.
        /// Without the split the whole strip would share one alpha and the
        /// recession would be lost.
        /// </summary>
        private void AddGuideStrip(
            float xLeft,
            float xRight,
            float y,
            float zNear,
            float zFar,
            Color color)
        {
            var segments = GuideSegments;
            for (var i = 0; i < segments; i++)
            {
                var t0 = i / (float)segments;
                var t1 = (i + 1) / (float)segments;
                var z0 = Mathf.Lerp(zNear, zFar, t0);
                var z1 = Mathf.Lerp(zNear, zFar, t1);
                var a = color;
                a.a *= Mathf.Lerp(1.0f, 0.25f, t1);
                var index = _guideMesh.Vertices.Count;
                _guideMesh.Vertices.Add(new Vector3(xLeft, y, z0));
                _guideMesh.Vertices.Add(new Vector3(xRight, y, z0));
                _guideMesh.Vertices.Add(new Vector3(xLeft, y, z1));
                _guideMesh.Vertices.Add(new Vector3(xRight, y, z1));
                for (var v = 0; v < 4; v++)
                {
                    _guideMesh.Colors.Add(a);
                }
                // guideData lives in TEXCOORD0: x = side sign, y = half-width
                // in screen pixels, z = 0 near .. 1 far (drives the width
                // tapering and the alpha falloff in the shader).
                var halfPixels = GuideWidthPixels * 0.5f;
                if (_guideMesh.Uv3 == null)
                {
                    _guideMesh.Uv3 = new List<Vector3>(
                        _guideMesh.Vertices.Count + 4);
                }
                _guideMesh.Uv3.Add(new Vector3(-1.0f, halfPixels, t0));
                _guideMesh.Uv3.Add(new Vector3(1.0f, halfPixels, t0));
                _guideMesh.Uv3.Add(new Vector3(-1.0f, halfPixels, t1));
                _guideMesh.Uv3.Add(new Vector3(1.0f, halfPixels, t1));
                // World-space centreline tangent, for the screen-space normal.
                var tangent =
                    new Vector3(0.0f, 0.0f, 1.0f);
                _guideMesh.Normals.Add(tangent);
                _guideMesh.Normals.Add(tangent);
                _guideMesh.Normals.Add(tangent);
                _guideMesh.Normals.Add(tangent);
                _guideMesh.Triangles.Add(index);
                _guideMesh.Triangles.Add(index + 1);
                _guideMesh.Triangles.Add(index + 2);
                _guideMesh.Triangles.Add(index + 2);
                _guideMesh.Triangles.Add(index + 1);
                _guideMesh.Triangles.Add(index + 3);
            }
        }

        private const int GuideSegments = 12;
        /// <summary>
        /// Screen-pixel extrusion per vertex. Together with the strip's world
        /// width this sets the visible thickness; both were cut to a quarter
        /// because the line read as a fat band instead of a hairline guide.
        /// </summary>
        private const float GuideWidthPixels = 0.75f;
        /// <summary>
        /// Millimetre-scale lift off the owning surface. Zero would z-fight with
        /// the track floor / sky wall under the guide's ZTest LEqual, which
        /// shows up as a stuttering, partially-eaten line.
        /// </summary>
        private const float GuideSurfaceLift = 0.004f;

        /// <summary>
        /// Screen pixels per cell, measured once per frame by projecting the
        /// baseline into view space. Returns a large value when the plane is
        /// off-screen so no short tick is needlessly dropped.
        /// </summary>
        private float RulerCellPixels(
            float baselineY,
            float z,
            int divisions)
        {
            var camera = Camera.main;
            if (camera == null || divisions <= 0)
            {
                return float.MaxValue;
            }
            var leftWorld = _master.TransformPoint(
                new Vector3(-TrackHalfWidth, baselineY, z));
            var rightWorld = _master.TransformPoint(
                new Vector3(TrackHalfWidth, baselineY, z));
            var left = camera.WorldToScreenPoint(leftWorld);
            var right = camera.WorldToScreenPoint(rightWorld);
            if (left.z <= 0.0f || right.z <= 0.0f)
            {
                return float.MaxValue;
            }
            var span = Mathf.Abs(right.x - left.x);
            return span / divisions;
        }

        /// <summary>
        /// Emits a glow pass and then a core pass, matching the measure-line
        /// recipe: the wide low-alpha stroke goes down first, the narrow bright
        /// one on top. Both take world-space endpoints; the shader turns the
        /// pixel width into an extruded quad, so the caller never deals in
        /// world-unit thickness.
        /// </summary>
        private void EmitRulerStroke(
            Vector3 start,
            Vector3 end,
            float widthPixels,
            Color coreColor,
            Color glowColor)
        {
            AddScreenSpaceLineQuad(
                _rulerMesh,
                start,
                end,
                widthPixels * 2.8f,
                glowColor);
            AddScreenSpaceLineQuad(
                _rulerMesh,
                start,
                end,
                widthPixels,
                coreColor);
        }

        private void BuildRulerSpan(
            float startNormalized,
            float endNormalized,
            float baselineY,
            float height,
            float z,
            float tickDirection)
        {
            if (float.IsNaN(startNormalized) ||
                float.IsNaN(endNormalized))
            {
                return;
            }
            var left = Mathf.Lerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                Mathf.Clamp01(Mathf.Min(
                    startNormalized,
                    endNormalized)));
            var right = Mathf.Lerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                Mathf.Clamp01(Mathf.Max(
                    startNormalized,
                    endNormalized)));
            if (right - left <= 0.0001f)
            {
                return;
            }
            var band = _grid.ColorFor(0);
            band.a = 0.10f;
            AddQuad(
                _rulerMesh,
                new Vector3(left, baselineY, z),
                new Vector3(right, baselineY, z),
                new Vector3(
                    left,
                    baselineY + tickDirection * height * 0.34f,
                    z),
                new Vector3(
                    right,
                    baselineY + tickDirection * height * 0.34f,
                    z),
                band);
        }

        private void BuildRulerMarker(
            float normalized,
            float baselineY,
            float z,
            MarkerShape shape,
            Color color)
        {
            if (float.IsNaN(normalized))
            {
                return;
            }
            var x = Mathf.Lerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                Mathf.Clamp01(normalized));
            var half = TrackHalfWidth *
                       (shape == MarkerShape.Diamond
                           ? RulerStartMarkerFraction
                           : RulerEndMarkerFraction);
            var verticalHalf = half;
            if (shape == MarkerShape.Diamond)
            {
                AddTriangle(
                    _rulerMesh,
                    new Vector3(x, baselineY + verticalHalf, z),
                    new Vector3(x - half, baselineY, z),
                    new Vector3(x + half, baselineY, z),
                    color);
                AddTriangle(
                    _rulerMesh,
                    new Vector3(x, baselineY - verticalHalf, z),
                    new Vector3(x + half, baselineY, z),
                    new Vector3(x - half, baselineY, z),
                    color);
                return;
            }
            AddQuad(
                _rulerMesh,
                new Vector3(x - half, baselineY - verticalHalf, z),
                new Vector3(x + half, baselineY - verticalHalf, z),
                new Vector3(x - half, baselineY + verticalHalf, z),
                new Vector3(x + half, baselineY + verticalHalf, z),
                color);
        }

        public Vector3 ModePlaneLocalPosition(
            double timePlaneMs,
            double nowMs)
        {
            return ModePlaneLocalPosition(
                timePlaneMs,
                nowMs,
                FalsumRulerSurface.Sky);
        }

        /// <summary>
        /// Centre of the mode plane. A sky ruler sits over the upper half of the
        /// time plane; a ground ruler sits over the lower half, so its info box
        /// reads next to the surface the edited note lives on instead of
        /// floating at the sky height while the ruler is on the floor.
        /// </summary>
        public Vector3 ModePlaneLocalPosition(
            double timePlaneMs,
            double nowMs,
            FalsumRulerSurface surface)
        {
            var z = _lineLocal.z +
                    (float)_motion.NoteZAt(
                        timePlaneMs,
                        nowMs);
            var top = _lineLocal.y + SkyY;
            var height = ModePlaneWorldHeight();
            var centerY = surface == FalsumRulerSurface.Ground
                ? _floorY + height * GroundPlaneCenterFraction
                : _floorY + height * SkyPlaneCenterFraction;
            return new Vector3(
                0.0f,
                centerY,
                z - 0.006f);
        }

        public float ModePlaneWorldHeight()
        {
            return Mathf.Max(
                0.36f,
                _lineLocal.y + SkyY - _floorY);
        }

        public bool TryPointerTrack(
            Vector2 screenPosition,
            double nowMs,
            out double timeMs,
            out float lanePosition,
            out int lane)
        {
            timeMs = nowMs;
            lanePosition = 0.5f;
            lane = 2;
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(
                _master.up,
                _master.TransformPoint(
                    new Vector3(0.0f, _floorY, 0.0f)));
            float distance;
            if (!plane.Raycast(ray, out distance) || distance < 0.0f)
            {
                return false;
            }
            var world = ray.GetPoint(distance);
            var local = _master.InverseTransformPoint(world);
            var z = local.z - _lineLocal.z;
            timeMs = TimeAtZ(z, nowMs);
            lanePosition = Mathf.InverseLerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                local.x);
            lane = NearestLane(local.x);
            return true;
        }

        public bool TryPick(
            Vector2 screenPosition,
            double nowMs,
            IList<ChartNote> notes,
            out ChartNote picked)
        {
            return TryPick(
                screenPosition,
                nowMs,
                notes,
                FalsumNotePickMask.All,
                out picked);
        }

        public bool TryPick(
            Vector2 screenPosition,
            double nowMs,
            IList<ChartNote> notes,
            FalsumNotePickMask pickMask,
            out ChartNote picked)
        {
            picked = null;
            List<ChartNote> candidates;
            if (!TryPickAll(
                    screenPosition,
                    nowMs,
                    notes,
                    pickMask,
                    out candidates))
            {
                return false;
            }
            picked = candidates[0];
            return true;
        }

        public bool TryPickAll(
            Vector2 screenPosition,
            double nowMs,
            IList<ChartNote> notes,
            FalsumNotePickMask pickMask,
            out List<ChartNote> picked)
        {
            picked = new List<ChartNote>();
            var camera = Camera.main;
            if (camera == null || notes == null)
            {
                return false;
            }
            var ray = camera.ScreenPointToRay(screenPosition);
            var pickPadding = PickPaddingCanvasUnits;
            Vector2 pointerLocal;
            var hasLocal = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                screenPosition,
                null,
                out pointerLocal);
            var candidates = new List<PickCandidate>();
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note == null ||
                    !IsNoteKindVisible(note.Kind) ||
                    (pickMask & PickMaskFor(note.Kind)) == 0)
                {
                    continue;
                }
                if (!IsNoteNearView(note, nowMs))
                {
                    continue;
                }
                if (note.Kind != NoteKind.SkyArea &&
                    (_noteRenderer == null ||
                     !_noteRenderer.IsNoteVisualActive(note)))
                {
                    continue;
                }
                Vector2[] polygon;
                if (hasLocal &&
                    TryBuildNoteScreenPolygon(
                        note,
                        nowMs,
                        out polygon))
                {
                    if (!PointInPolygon(pointerLocal, polygon))
                    {
                        if (DistanceToPolygon(pointerLocal, polygon) >
                            pickPadding)
                        {
                            continue;
                        }
                    }
                    var depth = Vector3.Distance(
                        camera.transform.position,
                        _master.TransformPoint(
                            NoteLocalPosition(note, nowMs)));
                    Bounds depthBounds;
                    if (_noteRenderer != null &&
                        _noteRenderer.TryGetNoteWorldBounds(
                            note,
                            nowMs,
                            out depthBounds))
                    {
                        float hitDistance;
                        depth = depthBounds.IntersectRay(
                            ray,
                            out hitDistance)
                            ? hitDistance
                            : Vector3.Distance(
                                camera.transform.position,
                                depthBounds.center);
                    }
                    candidates.Add(new PickCandidate
                    {
                        Note = note,
                        Layer = PickLayerFor(note.Kind),
                        Distance = depth,
                    });
                    continue;
                }

                Bounds bounds;
                if (_noteRenderer == null ||
                    !_noteRenderer.TryGetNoteWorldBounds(
                        note,
                        nowMs,
                        out bounds))
                {
                    continue;
                }
                float rayDistance;
                if (!bounds.IntersectRay(ray, out rayDistance))
                {
                    continue;
                }
                candidates.Add(new PickCandidate
                {
                    Note = note,
                    Layer = PickLayerFor(note.Kind),
                    Distance = rayDistance,
                });
            }
            candidates.Sort(
                delegate(PickCandidate left, PickCandidate right)
                {
                    var byLayer = right.Layer.CompareTo(left.Layer);
                    if (byLayer != 0)
                    {
                        return byLayer;
                    }
                    var byDistance =
                        left.Distance.CompareTo(right.Distance);
                    return byDistance != 0
                        ? byDistance
                        : left.Note.Id.CompareTo(right.Note.Id);
                });
            for (var i = 0; i < candidates.Count; i++)
            {
                picked.Add(candidates[i].Note);
            }
            return picked.Count > 0;
        }

        private static FalsumNotePickMask PickMaskFor(NoteKind kind)
        {
            switch (kind)
            {
                case NoteKind.Tap:
                    return FalsumNotePickMask.Tap;
                case NoteKind.Hold:
                    return FalsumNotePickMask.Hold;
                case NoteKind.Flick:
                    return FalsumNotePickMask.Flick;
                case NoteKind.SkyArea:
                    return FalsumNotePickMask.SkyArea;
                default:
                    return FalsumNotePickMask.None;
            }
        }

        private static int PickLayerFor(NoteKind kind)
        {
            return kind == NoteKind.Flick ||
                   kind == NoteKind.SkyArea
                ? 1
                : 0;
        }

        internal float PickPaddingCanvasUnits
        {
            get
            {
                var canvas = _canvasRect != null
                    ? _canvasRect.GetComponent<Canvas>()
                    : null;
                var scale = canvas != null
                    ? Mathf.Max(0.01f, canvas.scaleFactor)
                    : 1.0f;
                var screenPixels = Mathf.Clamp(
                    Screen.height * 0.0025f,
                    3.0f,
                    7.0f);
                return screenPixels / scale;
            }
        }

        internal bool DebugIsNoteNearView(
            ChartNote note,
            double nowMs)
        {
            return IsNoteNearView(note, nowMs);
        }

        internal bool TryGetNoteScreenPolygon(
            ChartNote note,
            double nowMs,
            out Vector2[] polygon)
        {
            return TryBuildNoteScreenPolygon(note, nowMs, out polygon);
        }

        internal bool TryCanvasPointToScreen(
            Vector2 canvasPoint,
            out Vector2 screen)
        {
            screen = Vector2.zero;
            if (_canvasRect == null)
            {
                return false;
            }
            screen = RectTransformUtility.WorldToScreenPoint(
                null,
                _canvasRect.TransformPoint(canvasPoint));
            return true;
        }

        public Vector3 NoteLocalPosition(ChartNote note, double nowMs)
        {
            if (note.Kind == NoteKind.Flick || note.Kind == NoteKind.SkyArea)
            {
                return new Vector3(
                    TrackX((float)(note.Kind == NoteKind.Flick
                        ? note.FlickCenterPos
                        : note.StartPos)),
                    _lineLocal.y + SkyY,
                    _lineLocal.z + (float)_motion.NoteZ(note, nowMs));
            }
            return new Vector3(
                LaneCenterX(note),
                _floorY,
                _lineLocal.z + (float)_motion.NoteZ(note, nowMs));
        }

        public bool ScreenPositionAt(
            double timeMs,
            float normalizedX,
            double nowMs,
            out Vector2 screen)
        {
            screen = Vector2.zero;
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var local = new Vector3(
                Mathf.Lerp(-TrackHalfWidth, TrackHalfWidth, normalizedX),
                _lineLocal.y + SkyY,
                _lineLocal.z +
                (float)_motion.NoteZAt(timeMs, nowMs));
            var world = _master.TransformPoint(local);
            var projected = camera.WorldToScreenPoint(world);
            screen = projected;
            return projected.z > 0.0f;
        }

        /// <summary>
        /// The projection EVERY handle uses: the middle-mouse fine edit and the
        /// selection anchors both call this, which is why they can never
        /// disagree about where a note's corner is.
        ///
        /// Side lanes matter here. A TAP/HOLD on lane 0 or 5 is drawn on the
        /// banked side rail, not on the floor, so its lateral extremes are not
        /// lane-boundary X on the floor plane. Projecting them from the lane
        /// index put the anchors on the ground track while the note itself was
        /// on the rail -- the "side-lane anchors are in the wrong place" bug.
        /// </summary>
        internal bool TryGetMiddleHandleScreenPosition(
            ChartNote note,
            bool tail,
            bool right,
            double nowMs,
            out Vector2 screen)
        {
            screen = Vector2.zero;
            var camera = Camera.main;
            if (camera == null || note == null)
            {
                return false;
            }

            Vector3 world;
            if (note.Kind == NoteKind.Tap ||
                note.Kind == NoteKind.Hold)
            {
                var timeMs = tail && note.Kind == NoteKind.Hold
                    ? note.EndTimeMs
                    : note.TimeMs;
                var z = _lineLocal.z +
                        (float)_motion.NoteZAt(timeMs, nowMs);
                if (!TryProjectGroundExtreme(note, z, right, out world))
                {
                    return false;
                }
            }
            else if (note.Kind == NoteKind.Flick)
            {
                var x = TrackX((float)(right
                    ? note.FlickRightPos
                    : note.FlickLeftPos));
                var z = _lineLocal.z +
                        (float)_motion.NoteZ(note, nowMs);
                world = _master.TransformPoint(
                    new Vector3(
                        x,
                        _lineLocal.y + SkyY,
                        z));
            }
            else if (note.Kind == NoteKind.SkyArea)
            {
                var center = tail ? note.EndPos : note.StartPos;
                var width = tail ? note.EndWidth : note.StartWidth;
                var x = TrackX((float)(center +
                    (right ? width : -width) * 0.5));
                var timeMs = tail ? note.EndTimeMs : note.TimeMs;
                var z = (float)_motion.NoteZAt(timeMs, nowMs);
                if (_skyRoot != null)
                {
                    world = _skyRoot.TransformPoint(
                        new Vector3(x, 0.0f, z));
                }
                else
                {
                    world = _master.TransformPoint(
                        new Vector3(
                            x,
                            _lineLocal.y + SkyY,
                            _lineLocal.z + z));
                }
            }
            else
            {
                return false;
            }

            var projected = camera.WorldToScreenPoint(world);
            screen = projected;
            return projected.z > 0.0f;
        }

        /// <summary>
        /// One lateral extreme of a TAP/HOLD at a given track z, in WORLD space.
        ///
        /// A central ground note (lanes 1..4) is a floor slab, so its extremes
        /// are lane boundaries projected onto the floor plane. A note on lane 0
        /// or 5 lives on the banked side rail, so its extremes have to come from
        /// the rail's own geometry -- the same <see cref="TryGetSidePoints"/>
        /// path the selection outline uses. Mixing the two is what put side-lane
        /// handles on the wrong surface.
        /// </summary>
        private bool TryProjectGroundExtreme(
            ChartNote note,
            float localZ,
            bool right,
            out Vector3 world)
        {
            world = Vector3.zero;
            var side = note.StartLane <= 0
                ? _leftGeometry
                : (note.StartLane >= 5 ? _rightGeometry : null);
            if (side != null)
            {
                Vector3 sideLeft;
                Vector3 sideRight;
                if (!TryGetSidePoints(
                        side,
                        localZ,
                        out sideLeft,
                        out sideRight))
                {
                    return false;
                }
                // TryGetSidePoints already returns MASTER-local points, so
                // they only need lifting back to world for the camera.
                world = _master.TransformPoint(right ? sideRight : sideLeft);
                return true;
            }

            var start = Mathf.Clamp(note.StartLane, 1, 5);
            var end = Mathf.Clamp(
                start + Mathf.Max(1, note.WidthLanes),
                1,
                6);
            var x = LaneBoundaryX(right ? end : start);
            world = _master.TransformPoint(
                new Vector3(x, _floorY, localZ));
            return true;
        }

        internal bool TryGetPointerNormalizedOnTimePlane(
            Vector2 screenPosition,
            double timeMs,
            double nowMs,
            out float normalizedX)
        {
            normalizedX = 0.0f;
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var z = _lineLocal.z +
                    (float)_motion.NoteZAt(timeMs, nowMs);
            var plane = new Plane(
                _master.forward,
                _master.TransformPoint(
                    new Vector3(0.0f, 0.0f, z)));
            var ray = camera.ScreenPointToRay(screenPosition);
            float distance;
            if (!plane.Raycast(ray, out distance) ||
                distance < 0.0f)
            {
                return false;
            }
            var local = _master.InverseTransformPoint(
                ray.GetPoint(distance));
            normalizedX = Mathf.InverseLerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                local.x);
            return true;
        }

        private float LaneBoundaryX(int boundary)
        {
            boundary = Mathf.Clamp(boundary, 0, 6);
            if (boundary <= 0)
            {
                var halfStep = (_laneX[1] - _laneX[0]) * 0.5f;
                return _laneX[0] - halfStep;
            }
            if (boundary >= 6)
            {
                var halfStep = (_laneX[5] - _laneX[4]) * 0.5f;
                return _laneX[5] + halfStep;
            }
            return (_laneX[boundary - 1] + _laneX[boundary]) * 0.5f;
        }

        internal float LaneBoundaryNormalizedPosition(int boundary)
        {
            return Mathf.Clamp01(Mathf.InverseLerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                LaneBoundaryX(boundary)));
        }

        public bool TryGetNoteScreenCenter(
            ChartNote note,
            double nowMs,
            out Vector2 screen)
        {
            screen = Vector2.zero;
            if (_noteRenderer == null)
            {
                return false;
            }
            Bounds bounds;
            var camera = Camera.main;
            if (camera == null ||
                !_noteRenderer.TryGetNoteWorldBounds(
                    note,
                    nowMs,
                    out bounds))
            {
                return false;
            }
            var projected = camera.WorldToScreenPoint(bounds.center);
            screen = projected;
            return projected.z > 0.0f;
        }

        public float Track(float normalizedPosition)
        {
            return Mathf.Lerp(
                -TrackHalfWidth,
                TrackHalfWidth,
                normalizedPosition);
        }

        public int NearestLane(float x)
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < _laneX.Length; i++)
            {
                var distance = Mathf.Abs(_laneX[i] - x);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        public double SnapTime(double value)
        {
            return _grid.Snap(_chart, Math.Max(0.0, value));
        }

        public double SnapTime(double value, double originalTime)
        {
            return _grid.Snap(
                _chart,
                Math.Max(0.0, value),
                originalTime);
        }

        public double TimeAtZ(double z, double nowMs)
        {
            var denominator = TrackMotion.DefaultTrackLength *
                              TrackMotion.TrackScale *
                              Math.Max(0.001, _motion.Speed);
            var deltaScaled = z / denominator * 1000.0;
            var targetScaled = _chart.ScaledTimeAt(nowMs) + deltaScaled;
            if (targetScaled <= 0.0)
            {
                return 0.0;
            }
            if (!_chart.TrackSpeedEffectsEnabled)
            {
                // Suppressed preview: the scaled axis is the identity, so the
                // inverse is the closed-form z -> time mapping. Skipping the
                // bisection matters here — TimeAtZ runs on every pointer move
                // and on every grid build, and it is the picking hot path.
                return Math.Max(0.0, nowMs + deltaScaled);
            }
            if (_chart.SpeedMultiplierAt(nowMs) <= 0.0001)
            {
                return Math.Max(0.0, nowMs + deltaScaled);
            }

            var low = 0.0;
            var high = Math.Max(600000.0, nowMs + 600000.0);
            while (_chart.ScaledTimeAt(high) < targetScaled &&
                   high < 86400000.0)
            {
                high *= 2.0;
            }
            for (var i = 0; i < 40; i++)
            {
                var middle = (low + high) * 0.5;
                if (_chart.ScaledTimeAt(middle) < targetScaled)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            return (low + high) * 0.5;
        }

        private void BuildGrid(
            double nowMs,
            bool showGrid,
            int flickDivision,
            double? timePlaneMs)
        {
            _gridMesh.Clear();
            if (!showGrid && !timePlaneMs.HasValue)
            {
                return;
            }

            if (showGrid)
            {
                var visibleEnd = TimeAtZ(
                    _motion.TrackLength,
                    nowMs);
                var lines = _grid.Build(
                    _chart,
                    Math.Max(0.0, nowMs - 2000.0),
                    Math.Max(nowMs + 2000.0, visibleEnd));
                var resolutionScale = Mathf.Max(
                    0.5f,
                    Screen.height / GridReferenceScreenHeight);
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var z = _lineLocal.z +
                            (float)_motion.NoteZAt(line.TimeMs, nowMs);
                    if (z < _lineLocal.z - 0.001f ||
                        z > _trackEndZ + 0.001f)
                    {
                        continue;
                    }
                    var overlapsMeasure =
                        line.IsMeasure && line.IsGridLine;
                    var color = _grid.ColorFor(line.ColorIndex);
                    color.a = overlapsMeasure
                        ? 1.0f
                        : line.IsMeasure ? 0.92f : 0.68f;
                    var width =
                        (overlapsMeasure
                            ? 2.6f
                            : line.IsMeasure ? 2.2f : 1.8f) *
                        _grid.LineWidthScale *
                        resolutionScale;
                    var glow = color;
                    glow.a *= overlapsMeasure ? 0.34f : 0.24f;
                    AddHorizontalGridLine(
                        _gridMesh,
                        -TrackHalfWidth,
                        TrackHalfWidth,
                        _floorY,
                        z,
                        width * 2.8f,
                        glow);
                    AddHorizontalGridLine(
                        _gridMesh,
                        -TrackHalfWidth,
                        TrackHalfWidth,
                        _floorY,
                        z,
                        width,
                        color);
                    AddGridSideLine(
                        _leftGeometry,
                        z,
                        width * 2.8f,
                        glow);
                    AddGridSideLine(
                        _leftGeometry,
                        z,
                        width,
                        color);
                    AddGridSideLine(
                        _rightGeometry,
                        z,
                        width * 2.8f,
                        glow);
                    AddGridSideLine(
                        _rightGeometry,
                        z,
                        width,
                        color);
                }
            }

        }

        private void BuildMeasureNumbers(
            double nowMs,
            bool visible)
        {
            if (_measureNumbers == null || !_measureNumbers.Available)
            {
                return;
            }
            _measureNumbers.Clear();
            // The feature is gated off for now; a stale preference can never
            // turn it back on.
            if (!visible ||
                !FalsumMeasureNumberFeature.Available ||
                !_grid.Enabled ||
                _chart == null)
            {
                return;
            }
            // Near to far so dense depth stacks keep the closest badge.
            var times = _chart.MeasureLineTimes;
            _measureTimesTotal = times.Count;
            _zRejects = 0;
            _behindRejects = 0;
            _aheadRejects = 0;
            _projectionFails = 0;
            _anchorFails = 0;
            for (var i = 0; i < times.Count; i++)
            {
                var timeMs = times[i];
                if (timeMs < 0.0)
                {
                    continue;
                }
                var z = _lineLocal.z +
                        (float)_motion.NoteZAt(timeMs, nowMs);
                if (z < _lineLocal.z - 0.001f ||
                    z > _trackEndZ + 0.001f)
                {
                    _zRejects++;
                    if (z < _lineLocal.z - 0.001f)
                    {
                        _behindRejects++;
                    }
                    else
                    {
                        _aheadRejects++;
                    }
                    continue;
                }
                Vector3 anchor;
                Vector3 outward;
                Vector3 up;
                if (!TryGetMeasureRightAnchor(
                        z,
                        out anchor,
                        out outward,
                        out up))
                {
                    _anchorFails++;
                    continue;
                }
                Vector2 screenPoint;
                var anchorWorld = _master.TransformPoint(anchor);
                var projected =
                    Camera.main.WorldToScreenPoint(anchorWorld);
                if (projected.z <= 0.0f)
                {
                    _projectionFails++;
                    continue;
                }
                screenPoint = projected;
                // The renderer parents its meshes under the master scene, so
                // anchors and directions stay in master local space.
                _measureNumbers.TryAdd(
                    anchor,
                    outward,
                    up,
                    screenPoint,
                    i + 1);
            }
            _measureNumbers.Apply();
        }

        public string MeasureNumberDebug
        {
            get
            {
                return "times=" + _measureTimesTotal +
                       " zRejects=" + _zRejects +
                       " behind=" + _behindRejects +
                       " ahead=" + _aheadRejects +
                       " anchorFails=" + _anchorFails +
                       " projectionFails=" + _projectionFails +
                       " anchor=" + _anchorDebug;
            }
        }

        private int _measureTimesTotal;
        private int _zRejects;
        private int _projectionFails;
        private int _behindRejects;
        private int _aheadRejects;
        private int _anchorFails;

        /// <summary>
        /// Screen-rightmost point of the right side rail at <paramref name="z"/>.
        /// The rail is a rotated strip, so its outward direction is decided in
        /// screen space rather than from a local axis guess.
        /// </summary>
        private bool TryGetMeasureRightAnchor(
            float z,
            out Vector3 anchor,
            out Vector3 outward,
            out Vector3 up)
        {
            anchor = Vector3.zero;
            outward = Vector3.right;
            up = Vector3.up;
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            Vector3 edgeA;
            Vector3 edgeB;
            if (_rightGeometry == null ||
                !TryGetSidePoints(
                    _rightGeometry,
                    z,
                    out edgeA,
                    out edgeB))
            {
                anchor = new Vector3(
                    TrackHalfWidth,
                    _floorY,
                    z);
                _anchorDebug = "fallback";
                return true;
            }

            // Walk the rail width and keep whichever sample lands furthest
            // right on screen; that is the rail's visible outer end.
            const int samples = 16;
            var bestX = float.MinValue;
            var bestPoint = edgeA;
            var found = false;
            for (var i = 0; i <= samples; i++)
            {
                var candidate = Vector3.Lerp(
                    edgeA,
                    edgeB,
                    i / (float)samples);
                var screen = camera.WorldToScreenPoint(
                    _master.TransformPoint(candidate));
                if (screen.z <= 0.0f)
                {
                    continue;
                }
                found = true;
                if (screen.x > bestX)
                {
                    bestX = screen.x;
                    bestPoint = candidate;
                }
            }
            if (!found)
            {
                return false;
            }
            anchor = bestPoint;
            // Build the badge basis from screen right/up so the square reads
            // upright on screen regardless of the rail's bank angle.
            var screenRight = camera.transform.right;
            var localRight =
                _master.InverseTransformDirection(screenRight);
            if (localRight.sqrMagnitude > 0.000001f)
            {
                outward = localRight.normalized;
            }
            var screenUp = camera.transform.up;
            var localUp = _master.InverseTransformDirection(screenUp);
            if (localUp.sqrMagnitude > 0.000001f)
            {
                up = localUp.normalized;
            }
            _anchorDebug =
                "a=" + edgeA.ToString("F2") +
                " b=" + edgeB.ToString("F2") +
                " pick=" + anchor.ToString("F2");
            return true;
        }

        public string MeasureNumberAnchorDebug
        {
            get { return _anchorDebug; }
        }

        private string _anchorDebug = "none";

        private void BuildTimePlane(
            double nowMs,
            double? timePlaneMs,
            Color planeTint)
        {
            _planeMesh.Clear();
            if (!timePlaneMs.HasValue)
            {
                return;
            }
            var z = _lineLocal.z +
                    (float)_motion.NoteZAt(
                        timePlaneMs.Value,
                        nowMs);
            var top = _lineLocal.y + SkyY;
            var color = planeTint;
            color.a = Mathf.Clamp(planeTint.a, 0.02f, 0.06f);
            AddQuad(
                _planeMesh,
                new Vector3(-TrackHalfWidth, _floorY, z),
                new Vector3(TrackHalfWidth, _floorY, z),
                new Vector3(-TrackHalfWidth, top, z),
                new Vector3(TrackHalfWidth, top, z),
                color);

            var topBandColor = planeTint;
            topBandColor.a = Mathf.Clamp(
                planeTint.a + 0.02f,
                0.04f,
                0.10f);
            AddQuad(
                _planeMesh,
                new Vector3(-TrackHalfWidth, top - 0.20f, z),
                new Vector3(TrackHalfWidth, top - 0.20f, z),
                new Vector3(-TrackHalfWidth, top, z),
                new Vector3(TrackHalfWidth, top, z),
                topBandColor);
        }

        private void BuildSelection(
            double nowMs,
            IList<ChartNote> selected)
        {
            if (_selectionOverlay == null)
            {
                SelectionBuildSequence++;
                SelectionBuildNowMs = nowMs;
                SelectionAttemptCount = selected != null
                    ? selected.Count
                    : 0;
                SelectionBuildDebug = "overlay=null";
                return;
            }
            if (selected == null || selected.Count == 0)
            {
                _selectionOverlay.SetPolygons(null);
                SelectionBuildSequence++;
                SelectionBuildNowMs = nowMs;
                SelectionAttemptCount = 0;
                SelectionBuildDebug = "built=0 hidden=0 failed=0";
                SelectionPolygonCount = 0;
                SelectionVertexCount = 0;
                SelectionAnchorCount = 0;
                return;
            }
            var polygons = new List<Vector2[]>();
            var anchors = new List<FalsumSelectionAnchor>();
            Vector2[] anchorPolygon = null;
            SelectionVertexCount = 0;
            var failed = 0;
            var hidden = 0;
            var failureDebug = string.Empty;
            // Anchors are single-note affordances: with two or more selected
            // notes the handles would overlap and steal each other's hits.
            //
            // THE ONE EXCEPTION is a multi-selection made ENTIRELY of SkyArea
            // notes: the user's mental model there is "this group of strips",
            // and the group (lateral footprint + time span) is exactly what
            // they want to scale. Those get a whole-group handle set instead
            // of per-note ones. A mixed selection keeps zero anchors, as
            // before.
            var anchorNote = selected != null && selected.Count == 1
                ? selected[0]
                : null;
            var isSkyAreaGroup = selected != null &&
                                 selected.Count >= 2 &&
                                 AllSelectedAreSkyAreas(selected);
            var groupPolygons = new List<Vector2[]>();
            if (selected != null)
            {
                for (var i = 0; i < selected.Count; i++)
                {
                    var note = selected[i];
                    if (note == null)
                    {
                        failed++;
                        continue;
                    }
                    if (!IsNoteSelectionVisible(note, nowMs))
                    {
                        hidden++;
                        continue;
                    }
                    Vector2[] polygon;
                    if (TryBuildSelectionScreenPolygon(
                            note,
                            nowMs,
                            out polygon))
                    {
                        polygons.Add(polygon);
                        SelectionVertexCount += polygon.Length;
                        if (isSkyAreaGroup)
                        {
                            groupPolygons.Add(polygon);
                        }
                        // Remember the single-selection polygon: building it a
                        // second time for the anchors doubled the per-frame
                        // cost of a long sampled skyarea, which is what made
                        // low-speed anchor rendering crawl.
                        if (note == anchorNote)
                        {
                            anchorPolygon = polygon;
                        }
                    }
                    else
                    {
                        failed++;
                        if (string.IsNullOrEmpty(failureDebug))
                        {
                            failureDebug = DebugDescribeNoteGeometry(
                                note,
                                nowMs);
                        }
                    }
                }
            }
            if (anchorNote != null && anchorPolygon != null)
            {
                TryBuildSelectionAnchors(
                    anchorNote,
                    nowMs,
                    anchorPolygon,
                    anchors);
            }
            else if (isSkyAreaGroup && groupPolygons.Count >= 2)
            {
                TryBuildSkyAreaGroupAnchors(
                    groupPolygons,
                    nowMs,
                    anchors);
            }
            _selectionOverlay.SetSelection(polygons, anchors);
            SelectionAnchorCount = anchors.Count;
            SelectionBuildSequence++;
            SelectionBuildNowMs = nowMs;
            SelectionAttemptCount = selected != null
                ? selected.Count
                : 0;
            SelectionBuildDebug =
                "built=" + polygons.Count +
                " hidden=" + hidden +
                " failed=" + failed +
                (failed > 0 ? " why=" + failureDebug : string.Empty);
            SelectionPolygonCount = polygons.Count;
        }

        /// <summary>
        /// True when every selected note is a SkyArea (the group-scale case).
        /// A mixed selection keeps the historic "no anchors on multi-select"
        /// behaviour, so a tap/hold selection can never sprout group handles.
        /// </summary>
        private static bool AllSelectedAreSkyAreas(
            IList<ChartNote> selected)
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var note = selected[i];
                if (note == null || note.Kind != NoteKind.SkyArea)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whole-group scaling handles for a multi-selected SkyArea chain.
        ///
        /// <para>THREE handles, not three per note: scaling is a property of the
        /// GROUP (one time span, one position), so a per-note handle set would
        /// let a two-segment selection be scaled inconsistently. Positions are
        /// taken from the AGGREGATED selection box and then snapped onto the
        /// drawn outline, exactly like the single-note handles
        /// (<c>MatchCornerToBox</c>): the aggregate decides the ROLE, the drawn
        /// geometry decides the POSITION.</para>
        ///
        /// <para>The anchors reuse the existing <c>StartEnd</c> (front/back
        /// length) and <c>Move</c> kinds. Their meaning is switched by the
        /// editor's <c>ResolveAnchorOperation</c> when the selection is a
        /// SkyArea group, so the drawing layer needs no new anchor kind and no
        /// new glyph rules.</para>
        ///
        /// <para>There is deliberately NO lateral/width handle: the group's
        /// horizontal footprint is no longer scalable from the selection, so no
        /// <c>Boundary</c> anchor is emitted here at all. Merely hiding one
        /// would leave a dead handle that still wins hit tests over the strips.</para>
        /// </summary>
        private void TryBuildSkyAreaGroupAnchors(
            List<Vector2[]> polygons,
            double nowMs,
            List<FalsumSelectionAnchor> anchors)
        {
            if (polygons == null || polygons.Count == 0)
            {
                return;
            }
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            for (var i = 0; i < polygons.Count; i++)
            {
                var polygon = polygons[i];
                if (polygon == null)
                {
                    continue;
                }
                for (var p = 0; p < polygon.Length; p++)
                {
                    var point = polygon[p];
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxY = Mathf.Max(maxY, point.y);
                }
            }
            if (minX >= maxX || minY >= maxY)
            {
                return;
            }

            var widest = maxX - minX;
            var tallest = maxY - minY;
            var shortSide = Mathf.Min(widest, tallest);
            var glyph = Mathf.Clamp(
                shortSide * AnchorGlyphSizeRatio,
                AnchorMinGlyph,
                AnchorMaxGlyph);

            // Temporal centres sit on the aggregate front/back edges. The
            // lateral (left/right) centres are NOT built: that width handle was
            // removed, and emitting it would leave an invisible-but-grabbable
            // handle stealing clicks from the strips beneath it.
            var midX = (minX + maxX) * 0.5f;
            var frontMid = SnapGroupHandle(
                polygons,
                new Vector2(midX, minY));
            var backMid = SnapGroupHandle(
                polygons,
                new Vector2(midX, maxY));

            anchors.Add(CreateGroupHandle(
                FalsumAnchorKind.StartEnd,
                FalsumAnchorEdge.Front,
                false,
                frontMid,
                glyph,
                2));
            anchors.Add(CreateGroupHandle(
                FalsumAnchorKind.StartEnd,
                FalsumAnchorEdge.Back,
                true,
                backMid,
                glyph,
                2));
            // Whole-group translation stays on the existing multi-target
            // middle-edit position body.
            var move = new FalsumSelectionAnchor(
                FalsumAnchorKind.Move,
                FalsumAnchorEdge.Front,
                FalsumAnchorPlacement.Offset,
                new Vector2(midX, minY) -
                new Vector2(0.0f, AnchorOffset(glyph)));
            move.Priority = 4;
            AnchorSizing(move, glyph);
            anchors.Add(move);
        }

        /// <summary>
        /// Builds one aggregate handle. Only the front/back TIME edges use this
        /// helper now: the group's lateral width pair is no longer emitted, so
        /// the placement is always <see cref="FalsumAnchorPlacement.TimeEdgeMidpoint"/>.
        /// </summary>
        private static FalsumSelectionAnchor CreateGroupHandle(
            FalsumAnchorKind kind,
            FalsumAnchorEdge edge,
            bool tail,
            Vector2 point,
            float glyph,
            int priority)
        {
            var anchor = new FalsumSelectionAnchor(
                kind,
                edge,
                FalsumAnchorPlacement.TimeEdgeMidpoint,
                point);
            anchor.Tail = tail;
            anchor.Priority = priority;
            AnchorSizing(anchor, glyph);
            // Draw and grab the same point: the aggregate handle never moves
            // after construction, so HitCenter stays the seed value.
            anchor.HitCenter = anchor.CanvasPosition;
            return anchor;
        }

        /// <summary>
        /// Snaps an aggregate-box point onto the nearest vertex of the drawn
        /// outlines, falling back to the raw point when the outlines are far
        /// away (a group whose strips are not contiguous).
        /// </summary>
        private static Vector2 SnapGroupHandle(
            List<Vector2[]> polygons,
            Vector2 projected)
        {
            var best = projected;
            var bestDistance = float.MaxValue;
            var found = false;
            for (var i = 0; i < polygons.Count; i++)
            {
                var polygon = polygons[i];
                if (polygon == null)
                {
                    continue;
                }
                for (var p = 0; p < polygon.Length; p++)
                {
                    var distance =
                        (polygon[p] - projected).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = polygon[p];
                        found = true;
                    }
                }
            }
            // A group handle may legitimately sit between strips; only snap
            // when a vertex is genuinely close, otherwise stay on the
            // aggregate edge so the handle still reads as "the group's edge".
            return found && bestDistance <= 900.0f
                ? best
                : projected;
        }

        /// <summary>
        /// Builds the direct-manipulation anchors for one selected note.
        ///
        /// CORNERS COME FROM THE MIDDLE-MOUSE PROJECTION and from nowhere else.
        /// <see cref="TryGetMiddleHandleScreenPosition"/> is the projection the
        /// middle-mouse fine edit has used all along; the anchors call it
        /// directly, so the handles and the note's own box can never disagree
        /// about where a corner is. Three earlier versions re-derived the four
        /// corners from the outline's shape (fixed vertex indices, then a
        /// bounding box, then an area-maximising vertex search). Every one of
        /// them guessed front/back from aspect ratio, which flips between a
        /// wide short box and a narrow long one -- that is why the purple time
        /// handles used to land on the lateral edges.
        ///
        /// Two things are layered on top of the projection:
        /// 1. Visibility is PER ENDPOINT and SCREEN-SPACE
        ///    (<see cref="IsEndpointOnScreen"/>), so an end whose handle would
        ///    land off-screen contributes nothing while the other end keeps
        ///    its own handles.
        /// 2. A projected corner is SNAPPED to the box vertex it identifies
        ///    (<see cref="MatchCornerToBox"/>) instead of being clamped inward,
        ///    so the handle sits exactly on the drawn outline. The projection
        ///    decides the ROLE; the drawn polygon decides the POSITION.
        /// </summary>
        private bool TryBuildSelectionAnchors(
            ChartNote note,
            double nowMs,
            Vector2[] polygon,
            List<FalsumSelectionAnchor> anchors)
        {
            anchors.Clear();
            if (note == null || polygon == null || polygon.Length < 3)
            {
                return false;
            }

            Vector2 frontLeft;
            Vector2 frontRight;
            Vector2 backLeft;
            Vector2 backRight;
            bool hasFront;
            bool hasBack;
            if (!ResolveBoxCorners(
                    note,
                    nowMs,
                    polygon,
                    out frontLeft,
                    out frontRight,
                    out backLeft,
                    out backRight,
                    out hasFront,
                    out hasBack))
            {
                return false;
            }

            // Handle size follows the box's SHORT side so a long thin strip
            // never gets handles wider than itself. A hidden end still carries
            // a clamped corner, but it contributes no handles, so it must not
            // take part in the size measurement either -- measuring the clip
            // line would shrink every handle on a partially scrolled note.
            var frontWidth = hasFront
                ? (frontRight - frontLeft).magnitude
                : 0.0f;
            var backWidth = hasBack && backLeft != frontLeft
                ? (backRight - backLeft).magnitude
                : 0.0f;
            var sideDepth = hasFront && hasBack && backLeft != frontLeft
                ? Mathf.Max(
                    (backLeft - frontLeft).magnitude,
                    (backRight - frontRight).magnitude)
                : 0.0f;
            var widest = Mathf.Max(frontWidth, backWidth);
            var shortSide = Mathf.Min(widest, sideDepth);
            if (widest <= 1.0f || shortSide <= 1.0f)
            {
                shortSide = FalsumSelectionGeometry.LongestEdge(polygon);
            }
            var glyph = Mathf.Clamp(
                shortSide * AnchorGlyphSizeRatio,
                AnchorMinGlyph,
                AnchorMaxGlyph);

            switch (note.Kind)
            {
                case NoteKind.Tap:
                case NoteKind.Hold:
                    BuildGroundAnchors(
                        note, frontLeft, frontRight, backLeft, backRight,
                        hasFront, hasBack, glyph, anchors);
                    break;
                case NoteKind.Flick:
                    BuildFlickAnchors(
                        note, frontLeft, frontRight, glyph, anchors);
                    break;
                case NoteKind.SkyArea:
                    BuildSkyAreaAnchors(
                        note, nowMs, frontLeft, frontRight, backLeft, backRight,
                        hasFront, hasBack, glyph, polygon, anchors);
                    break;
                default:
                    return false;
            }
            return anchors.Count > 0;
        }

        /// <summary>
        /// The four selection-box corners, plus which ends are actually
        /// grabbable.
        ///
        /// POSITIONS COME FROM <see cref="TryGetMiddleHandleScreenPosition"/>,
        /// the same projection the middle-mouse fine edit uses. That is the
        /// whole point: an earlier design read the corners off the drawn
        /// polygon, which looked right in the easy cases and failed exactly
        /// where it mattered (a clipped skyarea, a banked side rail, a long
        /// receding strip). Three successive polygon-based resolvers were
        /// written and every one of them still had to GUESS which pair of
        /// edges was "front" from the outline's aspect ratio -- and that guess
        /// flips between a wide short box and a narrow long one. Two
        /// independent geometric derivations can always disagree; one shared
        /// projection cannot.
        ///
        /// VISIBILITY is now PER AXIS, not per endpoint. A skyarea whose head
        /// has scrolled off one side of the screen space still owns its
        /// LATERAL position (that is always on screen, relative to the visible
        /// strip) even though its TIME and the corner pair that sits on that
        /// edge are not drawn. Dropping the whole endpoint -- the earlier
        /// behaviour -- made a long skyarea lose every handle as soon as one
        /// end left the view, which is the "selecting a skyarea shows no
        /// anchors at all" bug.
        ///
        /// What is layered on top of the projection:
        /// - A clamp into the drawn polygon, so a corner that projects
        ///   off-screen still yields a grabbable handle inside the selection
        ///   outline the user can see.
        /// </summary>
        private bool ResolveBoxCorners(
            ChartNote note,
            double nowMs,
            Vector2[] polygon,
            out Vector2 frontLeft,
            out Vector2 frontRight,
            out Vector2 backLeft,
            out Vector2 backRight,
            out bool hasFront,
            out bool hasBack)
        {
            frontLeft = Vector2.zero;
            frontRight = Vector2.zero;
            backLeft = Vector2.zero;
            backRight = Vector2.zero;
            hasFront = false;
            hasBack = false;
            if (note == null || polygon == null || polygon.Length < 3)
            {
                return false;
            }

            // PER-ENDPOINT, SCREEN-SPACE visibility. A HOLD/SKYAREA endpoint
            // gets its handles only while that specific end is on screen; the
            // other end keeps its own handles regardless. Testing the note as a
            // whole (or testing only track-z) is what made one clipped end erase
            // every handle on the note.
            hasFront = IsEndpointOnScreen(note, false, nowMs);
            var hasSeparateTail = note.Kind == NoteKind.Hold ||
                                  note.Kind == NoteKind.SkyArea;
            hasBack = hasSeparateTail
                ? IsEndpointOnScreen(note, true, nowMs)
                : hasFront;

            Vector2 projectedFrontLeft = Vector2.zero;
            Vector2 projectedFrontRight = Vector2.zero;
            Vector2 projectedBackLeft = Vector2.zero;
            Vector2 projectedBackRight = Vector2.zero;

            // TAP and FLICK carry a single time, so the middle-mouse projection
            // renders them as one slab: asking for `tail: true` would project
            // the same instant twice. The box's back edge IS its front edge for
            // those kinds, which matches what is drawn.
            //
            // A failed projection is NOT fatal. It used to `return false`, and
            // because the caller then emitted no anchors at all, a skyarea with
            // one end far off screen lost every handle including the ones that
            // have nothing to do with the missing end. Missing corners now just
            // stay at zero and the per-end visibility flags decide what is
            // drawn.
            var hasFrontPair = TryProjectCorner(
                note, nowMs, false, false, out projectedFrontLeft) &&
                TryProjectCorner(
                    note, nowMs, false, true, out projectedFrontRight);
            var hasBackPair = hasSeparateTail &&
                TryProjectCorner(
                    note, nowMs, true, false, out projectedBackLeft) &&
                TryProjectCorner(
                    note, nowMs, true, true, out projectedBackRight);
            if (!hasFrontPair && !hasBackPair)
            {
                return false;
            }
            if (!hasFrontPair)
            {
                // The near end failed to project but the far end did. Keep the
                // far pair; the front handles are simply not offered.
                hasFront = false;
                projectedFrontLeft = projectedBackLeft;
                projectedFrontRight = projectedBackRight;
            }
            if (!hasSeparateTail || !hasBackPair)
            {
                // A single-time kind, or a far end that failed to project, uses
                // the front pair for the back edge.
                projectedBackLeft = projectedFrontLeft;
                projectedBackRight = projectedFrontRight;
                if (!hasSeparateTail)
                {
                    hasBack = hasFront;
                }
                else if (!hasBackPair)
                {
                    hasBack = false;
                }
            }

            // POSITION comes from the vertices of the box that is actually
            // drawn; the projection is used only to decide WHICH vertex plays
            // which role. Re-projecting and then pulling the result 3 px inside
            // the outline made every handle sit slightly off its own corner --
            // the residual offset the user kept seeing. Snapping to a real
            // polygon vertex removes that by construction while keeping the
            // role assignment derived from data (which is what fixed the
            // front/back confusion).
            frontLeft = MatchCornerToBox(polygon, projectedFrontLeft);
            frontRight = MatchCornerToBox(polygon, projectedFrontRight);
            backLeft = MatchCornerToBox(polygon, projectedBackLeft);
            backRight = MatchCornerToBox(polygon, projectedBackRight);
            return true;
        }

        /// <summary>
        /// Snaps an analytically-projected corner to the nearest vertex of the
        /// drawn box, falling back to a point on the outline when the projection
        /// landed nowhere near it.
        ///
        /// Snapping and clamping are different jobs: snapping makes the handle
        /// sit exactly on the drawn edge, clamping only guarantees it is
        /// reachable. Snapping first is what stops handles drifting inward.
        ///
        /// There is deliberately NO "zero means invalid" sentinel here: canvas
        /// local coordinates legitimately include the origin, so a projected
        /// (0,0) is a real position, not an error. "Is it a real vertex of this
        /// box" is the only question, and distance answers it.
        /// </summary>
        private static Vector2 MatchCornerToBox(
            IList<Vector2> polygon,
            Vector2 projected)
        {
            var fallback = FalsumSelectionGeometry.Centroid(polygon);
            if (polygon == null || polygon.Count == 0)
            {
                return fallback;
            }
            // The snap window scales with the box so a big selection box gets a
            // proportionally larger catch radius, with a floor for tiny notes
            // and a ceiling so a clipped end cannot steal a far corner.
            var boxExtent = 0.0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                boxExtent = Mathf.Max(
                    boxExtent,
                    (polygon[i] - fallback).sqrMagnitude);
            }
            var tolerance = Mathf.Clamp(
                boxExtent * 0.25f,
                16.0f,
                2500.0f);
            Vector2 best = Vector2.zero;
            var bestDistance = float.MaxValue;
            var found = false;
            for (var i = 0; i < polygon.Count; i++)
            {
                var distance = (polygon[i] - projected).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = polygon[i];
                    found = true;
                }
            }
            if (found && bestDistance <= tolerance)
            {
                return best;
            }
            // Too far to be that corner: put the handle on the outline in the
            // projected direction instead of jumping to an unrelated vertex.
            return FalsumSelectionGeometry.BoundaryPointAlongDirection(
                polygon,
                fallback,
                projected - fallback);
        }

        /// <summary>
        /// One corner through the middle-mouse projection, converted to the
        /// canvas-local space the anchors are drawn and hit-tested in.
        ///
        /// False means the projection itself failed (no camera, or the point is
        /// behind it). It does NOT mean the corner is off-screen -- that is what
        /// <see cref="IsEndpointVisible"/> above is for.
        /// </summary>
        private bool TryProjectCorner(
            ChartNote note,
            double nowMs,
            bool tail,
            bool right,
            out Vector2 canvas)
        {
            canvas = Vector2.zero;
            Vector2 screen;
            if (!TryGetMiddleHandleScreenPosition(
                    note,
                    tail,
                    right,
                    nowMs,
                    out screen))
            {
                return false;
            }
            return TryScreenToCanvas(screen, out canvas);
        }

        /// <summary>
        /// Whether a note endpoint is inside the visible TRACK, matching the
        /// renderer's own visibility rule (<see cref="IsNoteNearView"/>).
        ///
        /// Track-visibility is the data-side test: it answers "is this endpoint
        /// beyond the track's far end". It is deliberately NOT the test for
        /// whether a handle should be drawn -- that is screen-space, see
        /// <see cref="IsEndpointOnScreen"/>, because an endpoint can be inside
        /// the track while its projection is still outside the viewport (and
        /// the reverse).
        /// </summary>
        private bool IsEndpointVisible(
            ChartNote note,
            double timeMs,
            double nowMs)
        {
            var z = _motion.NoteZAt(timeMs, nowMs);
            return z >= -_motion.TrackLength &&
                   z <= _motion.TrackLength;
        }

        /// <summary>
        /// Whether an endpoint's HANDLE would land inside the screen.
        ///
        /// This is the rule the user asked for: for HOLD and SKYAREA, an
        /// endpoint gets its corner pair and its time midpoint only while that
        /// endpoint is actually on screen. An off-screen end is not grabbable --
        /// its handle would sit on the viewport border and lie about where the
        /// note's real edge is.
        ///
        /// Crucially this is PER ENDPOINT, never per note. Dropping every handle
        /// the moment one end leaves the view is what made a long skyarea show
        /// nothing at all; the other end -- and the easing/move controls, which
        /// are anchored to the visible silhouette -- remain valid.
        /// </summary>
        private bool IsEndpointOnScreen(
            ChartNote note,
            bool tail,
            double nowMs)
        {
            if (note == null)
            {
                return false;
            }
            var timeMs = tail ? note.EndTimeMs : note.TimeMs;
            if (!IsEndpointVisible(note, timeMs, nowMs))
            {
                return false;
            }
            // A Tap/Flick has one time, so its "tail" projects the same slab.
            var projectTail = tail &&
                              (note.Kind == NoteKind.Hold ||
                               note.Kind == NoteKind.SkyArea);
            Vector2 left;
            Vector2 right;
            if (!ProjectCornerScreen(
                    note, nowMs, projectTail, false, out left) ||
                !ProjectCornerScreen(
                    note, nowMs, projectTail, true, out right))
            {
                return false;
            }
            return IsInsideScreen(left) || IsInsideScreen(right);
        }

        /// <summary>
        /// One corner in raw SCREEN space (no canvas conversion), for visibility
        /// tests that must not depend on the overlay's canvas existing.
        /// </summary>
        private bool ProjectCornerScreen(
            ChartNote note,
            double nowMs,
            bool tail,
            bool right,
            out Vector2 screen)
        {
            screen = Vector2.zero;
            return TryGetMiddleHandleScreenPosition(
                note,
                tail,
                right,
                nowMs,
                out screen);
        }

        /// <summary>
        /// Screen-inclusive test with a margin, so a handle whose centre is a
        /// couple of pixels outside still counts (it would be half-visible).
        /// </summary>
        private static bool IsInsideScreen(Vector2 screen)
        {
            const float margin = 2.0f;
            return screen.x >= -margin &&
                   screen.y >= -margin &&
                   screen.x <= Screen.width + margin &&
                   screen.y <= Screen.height + margin;
        }

        private Vector2 ToCanvas(Vector2 screen)
        {
            Vector2 canvas;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screen, null, out canvas)
                ? canvas
                : Vector2.zero;
        }

        private void BuildGroundAnchors(
            ChartNote note,
            Vector2 frontLeft,
            Vector2 frontRight,
            Vector2 backLeft,
            Vector2 backRight,
            bool hasFront,
            bool hasBack,
            float glyph,
            List<FalsumSelectionAnchor> anchors)
        {
            // Tap and hold expose a lateral pair at each endpoint they own.
            // An endpoint that is off-screen contributes nothing: drawing a
            // handle for it would sit on the clip line and lie about the note.
            //
            // A TAP has no tail at all, which is why only a HOLD gets the back
            // pair. The back end of a Tap is the front end.
            if (hasFront)
            {
                AddBoundaryPair(
                    anchors, frontLeft, frontRight, false, glyph,
                    FalsumAnchorPlacement.Corner);
                AddTimeEdge(
                    anchors,
                    FalsumSelectionGeometry.Midpoint(frontLeft, frontRight),
                    FalsumAnchorEdge.Front, false, glyph);
            }
            if (note.Kind != NoteKind.Hold || !hasBack)
            {
                return;
            }
            AddBoundaryPair(
                anchors, backLeft, backRight, true, glyph,
                FalsumAnchorPlacement.Corner);
            AddTimeEdge(
                anchors,
                FalsumSelectionGeometry.Midpoint(backLeft, backRight),
                FalsumAnchorEdge.Back, true, glyph);
        }

        private static void AddBoundaryPair(
            List<FalsumSelectionAnchor> anchors,
            Vector2 left,
            Vector2 right,
            bool tail,
            float glyph,
            FalsumAnchorPlacement placement)
        {
            AddBoundary(
                anchors, left, FalsumAnchorEdge.Left, tail, glyph, placement);
            AddBoundary(
                anchors, right, FalsumAnchorEdge.Right, tail, glyph,
                placement);
        }

        private static void AddBoundary(
            List<FalsumSelectionAnchor> anchors,
            Vector2 point,
            FalsumAnchorEdge edge,
            bool tail,
            float glyph,
            FalsumAnchorPlacement placement)
        {
            var anchor = new FalsumSelectionAnchor(
                FalsumAnchorKind.Boundary,
                edge,
                placement,
                point);
            anchor.Tail = tail;
            // Boundary handles are the most numerous and the most forgiving to
            // grab, so they lose every contested hit against a move handle or a
            // click-only control.
            anchor.Priority = 0;
            AnchorSizing(anchor, glyph);
            anchors.Add(anchor);
        }

        private static void AddTimeEdge(
            List<FalsumSelectionAnchor> anchors,
            Vector2 midpoint,
            FalsumAnchorEdge edge,
            bool tail,
            float glyph)
        {
            var anchor = new FalsumSelectionAnchor(
                FalsumAnchorKind.StartEnd,
                edge,
                FalsumAnchorPlacement.TimeEdgeMidpoint,
                midpoint);
            anchor.Tail = tail;
            anchor.Priority = 2;
            AnchorSizing(anchor, glyph);
            anchors.Add(anchor);
        }

        /// <summary>
        /// Handles must stay visible and grabbable on a tiny box, but must not
        /// swallow the box on a large one, so both the glyph and the grab
        /// radius are floored and travel with the box size.
        /// </summary>
        private static void AnchorSizing(
            FalsumSelectionAnchor anchor,
            float glyph)
        {
            // Glyph = what is drawn; HitRadius = what can be grabbed. They are
            // separate on purpose: the grab area stays generous (mis-clicks are
            // costly) while the drawn square stays small enough not to cover
            // the outline it sits on.
            anchor.GlyphRadius = Mathf.Clamp(
                glyph * AnchorGlyphRadiusScale,
                AnchorMinGlyph,
                AnchorMaxGlyph);
            anchor.GlyphScale = 1.0f;
            anchor.HitRadius = Mathf.Max(
                AnchorMinHitRadius,
                glyph * AnchorHitRadiusScale);
        }

        private void BuildFlickAnchors(
            ChartNote note,
            Vector2 frontLeft,
            Vector2 frontRight,
            float glyph,
            List<FalsumSelectionAnchor> anchors)
        {
            // Flick has a single time, so the box is a flat slab. Its two
            // lateral extremes are EDGE MIDPOINTS, not corners: the projection
            // returns the left/right ends at the slab's mid height. Drawing
            // them as edge handles (a bar) rather than corner blocks is what
            // tells the user "this adjusts the width" instead of "this moves
            // a corner".
            var left = frontLeft;
            var right = frontRight;
            AddBoundary(
                anchors, left, FalsumAnchorEdge.Left, false, glyph,
                FalsumAnchorPlacement.LateralEdgeMidpoint);
            AddBoundary(
                anchors, right, FalsumAnchorEdge.Right, false, glyph,
                FalsumAnchorPlacement.LateralEdgeMidpoint);

            var mid = FalsumSelectionGeometry.Midpoint(left, right);
            var along = right - left;
            var across = along.sqrMagnitude < 0.0001f
                ? Vector2.up
                : new Vector2(-along.y, along.x).normalized;
            if (across.y < 0.0f)
            {
                across = -across;
            }
            var offset = AnchorOffset(glyph);

            // The colour control sits on the FRONT side (the direction the
            // flick's own glyph faces) and the move handle behind it, so the
            // two never overlap and the click target is clearly distinct from
            // the drag target.
            var colour = new FalsumSelectionAnchor(
                FalsumAnchorKind.Color,
                FalsumAnchorEdge.Front,
                FalsumAnchorPlacement.Offset,
                mid - across * offset);
            colour.Value = note.FlickDirection;
            // A click-only control outranks every drag handle: its useful
            // travel is zero, so a miss is not recoverable by dragging further.
            colour.Priority = 5;
            AnchorSizing(colour, glyph);
            var move = new FalsumSelectionAnchor(
                FalsumAnchorKind.Move,
                FalsumAnchorEdge.Back,
                FalsumAnchorPlacement.Offset,
                mid + across * (offset * 1.3f));
            move.Priority = 4;
            AnchorSizing(move, glyph);
            anchors.Add(colour);
            anchors.Add(move);
        }

        /// <summary>
        /// The 0..1 progress interval of a strip that is actually inside the
        /// visible track window [0, trackLength], given the strip's head and
        /// tail z.
        ///
        /// Pure and static on purpose: this is the heart of the "the easing
        /// anchor must not jitter" rule, so it can be checked by feeding it
        /// numbers instead of by looking at the editor. A fully clipped strip
        /// collapses to a single point rather than yielding an inverted range.
        /// </summary>
        internal static void ResolveVisibleProgressRange(
            float headZ,
            float tailZ,
            float trackLength,
            out float minProgress,
            out float maxProgress)
        {
            minProgress = 0.0f;
            maxProgress = 1.0f;
            var span = tailZ - headZ;
            if (Mathf.Abs(span) <= 0.0001f)
            {
                // Zero-length strip: it occupies a single progress point.
                minProgress = 0.5f;
                maxProgress = 0.5f;
                return;
            }

            // progress(z) = (z - headZ) / span, i.e. the position along the
            // note from its head (0) to its tail (1). Mapping z DIRECTLY to
            // progress is what makes the result continuous in both directions:
            // reversing the note simply negates the mapping instead of needing
            // a separate 1.0 - x branch that could disagree with itself.
            var visibleNear = Mathf.Max(0.0f, Mathf.Min(headZ, tailZ));
            var visibleFar = Mathf.Min(trackLength, Mathf.Max(headZ, tailZ));
            if (visibleFar < visibleNear)
            {
                // Fully clipped either way. Collapse onto whichever track
                // boundary the strip is nearest, which is the continuous limit
                // of the partial case. Returning a fixed 0.5 here instead made
                // the anchor snap to the note's middle the moment the strip
                // scrolled out of view.
                var boundaryZ = Mathf.Clamp(
                    0.5f * (headZ + tailZ),
                    0.0f,
                    trackLength);
                var collapsed = Mathf.Clamp01((boundaryZ - headZ) / span);
                minProgress = collapsed;
                maxProgress = collapsed;
                return;
            }

            var nearProgress = Mathf.Clamp01((visibleNear - headZ) / span);
            var farProgress = Mathf.Clamp01((visibleFar - headZ) / span);
            minProgress = Mathf.Min(nearProgress, farProgress);
            maxProgress = Mathf.Max(nearProgress, farProgress);
        }

        /// <summary>
        /// The middle of the VISIBLE part of a skyarea's left or right side, in
        /// canvas-local space.
        ///
        /// Solved in the note's own PARAMETER space (the 0..1 progress from its
        /// head to its tail) rather than reverse-engineered from the drawn
        /// polygon, because the position must be a CONTINUOUS function of the
        /// camera and the note. Averaging polygon vertices is not continuous:
        /// the set of vertices inside the averaging band changes discretely
        /// between frames, which is exactly what made the easing anchor float
        /// up and down.
        /// </summary>
        private Vector2 SkyAreaSideMidpoint(
            ChartNote note,
            double nowMs,
            bool rightSide)
        {
            var root = _skyRoot != null ? _skyRoot : _master;
            var localY = _skyRoot != null ? 0.0f : _lineLocal.y + SkyY;
            var baseZ = _skyRoot != null ? 0.0f : _lineLocal.z;

            var headZ = (float)_motion.NoteZAt(note.TimeMs, nowMs);
            var tailZ = (float)_motion.NoteZAt(note.EndTimeMs, nowMs);
            float minProgress;
            float maxProgress;
            ResolveVisibleProgressRange(
                headZ,
                tailZ,
                (float)_motion.TrackLength,
                out minProgress,
                out maxProgress);

            var midProgress = Mathf.Clamp01((minProgress + maxProgress) * 0.5f);
            var leftEase = SkyAreaGeometry.Ease(midProgress, note.LeftEasing);
            var rightEase = SkyAreaGeometry.Ease(midProgress, note.RightEasing);
            var startCenter = TrackX((float)note.StartPos);
            var endCenter = TrackX((float)note.EndPos);
            var startWidth = Mathf.Max(
                0.01f,
                (float)note.StartWidth * (TrackHalfWidth * 2.0f));
            var endWidth = Mathf.Max(
                0.01f,
                (float)note.EndWidth * (TrackHalfWidth * 2.0f));
            var progress = rightSide ? rightEase : leftEase;
            var x = rightSide
                ? Mathf.Lerp(
                    startCenter + startWidth * 0.5f,
                    endCenter + endWidth * 0.5f,
                    progress)
                : Mathf.Lerp(
                    startCenter - startWidth * 0.5f,
                    endCenter - endWidth * 0.5f,
                    progress);
            var sampleTime = note.TimeMs + note.DurationMs * midProgress;
            var localZ = baseZ + (float)_motion.NoteZAt(sampleTime, nowMs);
            var local = new Vector3(x, localY, localZ);
            Vector2 canvas;
            if (TryProjectWorld(root.TransformPoint(local), out canvas))
            {
                return canvas;
            }
            return Vector2.zero;
        }

        private void BuildSkyAreaAnchors(
            ChartNote note,
            double nowMs,
            Vector2 frontLeft,
            Vector2 frontRight,
            Vector2 backLeft,
            Vector2 backRight,
            bool hasFront,
            bool hasBack,
            float glyph,
            Vector2[] polygon,
            List<FalsumSelectionAnchor> anchors)
        {
            // The easing anchors sit on the middle of the VISIBLE part of each
            // side, solved in the note's progress parameter (see
            // SkyAreaSideMidpoint). Deriving it from polygon vertices made the
            // anchor jump up and down between frames as vertices crossed the
            // averaging band, and on a receding strip the extreme X sits at the
            // far end so the "edge midpoint" was not on the side at all.
            var leftMid = SkyAreaSideMidpoint(note, nowMs, false);
            var rightMid = SkyAreaSideMidpoint(note, nowMs, true);

            // Four corners adjust the boundary of the endpoint they belong to.
            // A corner is drawn only when its endpoint is actually on screen:
            // pinning it to the clip line would read as the note's real edge.
            if (hasFront)
            {
                AddBoundary(
                    anchors, frontLeft, FalsumAnchorEdge.Left, false, glyph,
                    FalsumAnchorPlacement.Corner);
                AddBoundary(
                    anchors, frontRight, FalsumAnchorEdge.Right, false, glyph,
                    FalsumAnchorPlacement.Corner);
            }
            if (hasBack)
            {
                AddBoundary(
                    anchors, backLeft, FalsumAnchorEdge.Left, true, glyph,
                    FalsumAnchorPlacement.Corner);
                AddBoundary(
                    anchors, backRight, FalsumAnchorEdge.Right, true, glyph,
                    FalsumAnchorPlacement.Corner);
            }

            // The easing handles keep their OWN side; what can flip is which
            // end of the three-slot ladder means Ease in vs Ease out. A body
            // that expands outward past its start point reads the curtain as
            // running the other way, so that handle's in/out detents swap.
            AddEasingAnchor(
                anchors, leftMid,
                note.LeftEasing, FalsumAnchorEdge.Left, glyph,
                FalsumSkyAreaGroupScale.EasingInOutIsSwapped(note, true));
            AddEasingAnchor(
                anchors, rightMid,
                note.RightEasing, FalsumAnchorEdge.Right, glyph,
                FalsumSkyAreaGroupScale.EasingInOutIsSwapped(note, false));

            // Front/back edge midpoints adjust start/end time -- and, dragged
            // laterally, the lateral position of that ONE endpoint. Both ends
            // again only when that edge is visible.
            if (hasFront)
            {
                AddTimeEdge(
                    anchors,
                    FalsumSelectionGeometry.Midpoint(frontLeft, frontRight),
                    FalsumAnchorEdge.Front, false, glyph);
            }
            if (hasBack)
            {
                AddTimeEdge(
                    anchors,
                    FalsumSelectionGeometry.Midpoint(backLeft, backRight),
                    FalsumAnchorEdge.Back, true, glyph);
            }

            // Move handle sits below the visible box's front edge. When the
            // start is off-screen the box's lowest visible edge is the clip
            // line, which is where this handle belongs anyway.
            var minY = float.MaxValue;
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            for (var i = 0; i < polygon.Length; i++)
            {
                var p = polygon[i];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
            }
            var bottomMid = new Vector2((minX + maxX) * 0.5f, minY);
            var move = new FalsumSelectionAnchor(
                FalsumAnchorKind.Move,
                FalsumAnchorEdge.Front,
                FalsumAnchorPlacement.Offset,
                bottomMid - new Vector2(0.0f, AnchorOffset(glyph)));
            move.Priority = 4;
            AnchorSizing(move, glyph);
            anchors.Add(move);
        }

        /// <summary>Ease in (2) and Ease out (1) traded; Line (0) is fixed.</summary>
        private static int SwapEasingInOut(int easing)
        {
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

        private static void AddEasingAnchor(
            List<FalsumSelectionAnchor> anchors,
            Vector2 edgeMid,
            int easing,
            FalsumAnchorEdge edge,
            float glyph,
            bool swapInOut)
        {
            // The three states lie along canvas X so they read left to right:
            // inward (toward the body) = Ease out, the edge = Line, outward =
            // Ease in.
            //
            // `swapInOut` flips ONLY the two outer detents, leaving the slot
            // geometry (and therefore the handle's position) untouched: the
            // inward slot then means Ease in and the outward slot Ease out.
            // That is the SkyArea case where the curtain reads as running the
            // other way. It never swaps which SIDE the handle belongs to.
            //
            // The geometry is stored ON THE ANCHOR and the glyph is painted
            // from it. The drag that changes the value snaps through the same
            // geometry, so "the handle sits where its value says" holds by
            // construction instead of by two formulas agreeing.
            var inwardSign = edge == FalsumAnchorEdge.Left ? 1.0f : -1.0f;
            var spacing = EasingSpacing(glyph);
            var anchor = new FalsumSelectionAnchor(
                FalsumAnchorKind.Easing,
                edge,
                FalsumAnchorPlacement.Offset,
                edgeMid);
            anchor.SnapCenter = edgeMid;
            anchor.SnapDirection = new Vector2(inwardSign, 0.0f);
            anchor.SnapSpacing = spacing;
            // Slot geometry stays put; only the VALUE the outer slots stand for
            // flips when in/out is swapped, so the handle lands on the detent
            // that now means its real easing.
            anchor.CanvasPosition = anchor.SnapPositionFor(
                swapInOut ? SwapEasingInOut(easing) : easing);
            // THE HIT CENTRE MUST FOLLOW THE GLYPH. The constructor seeds
            // HitCenter from the pass-in point (edgeMid), and this anchor is
            // then MOVED to its slot -- so leaving HitCenter at edgeMid left
            // the grab area behind at the "Line" position while the handle was
            // painted one slot away. At one SnapSpacing (8-15 px) that put the
            // drawn handle just outside the grab radius, which is why the
            // easing control felt impossible to hit while every other anchor
            // was fine. Every anchor that repositions itself after
            // construction must re-point HitCenter (see the invariant note).
            anchor.HitCenter = anchor.CanvasPosition;
            anchor.Value = swapInOut ? SwapEasingInOut(easing) : easing;
            // This control both cycles (click) and drags, and it outranks the
            // drag handles around it so a click near a corner still reaches it.
            anchor.Priority = 5;
            AnchorSizing(anchor, glyph);
            // It is the SMALLEST useful control (a 3-state detent you must land
            // on precisely) and sits next to a corner, so its grab area is
            // widened past the generic anchor tolerance.
            anchor.HitRadius = Mathf.Max(
                anchor.HitRadius,
                EasingHitRadius(spacing, glyph));
            anchors.Add(anchor);
        }

        /// <summary>
        /// Grab radius for the easing control.
        ///
        /// It has to cover the whole three-state spread, otherwise the two
        /// outer states (one spacing away from the centre) are near the edge of
        /// the tolerance and the control feels unreliable. Covering the spread
        /// plus the normal anchor tolerance means every state is comfortably
        /// grabbable, including when the pointer lands slightly off the glyph.
        /// </summary>
        private static float EasingHitRadius(float spacing, float glyph)
        {
            return Mathf.Max(
                AnchorMinHitRadius,
                spacing + glyph * AnchorHitRadiusScale);
        }

        /// <summary>
        /// Canvas distance between adjacent easing states. Tracks the handle
        /// size so the three positions stay distinguishable without flying off
        /// a narrow strip.
        /// </summary>
        private static float EasingSpacing(float glyph)
        {
            return Mathf.Max(glyph * 1.4f, AnchorMinGlyph * 1.2f);
        }

        /// <summary>
        /// Ease out sits inward (-spacing), Line on the edge (0), Ease in
        /// outward (+spacing).
        /// </summary>
        private static float EasingSnapOffset(int easing, float glyph)
        {
            var spacing = EasingSpacing(glyph);
            if (easing == 1)
            {
                return -spacing;
            }
            if (easing == 2)
            {
                return spacing;
            }
            return 0.0f;
        }

        /// <summary>
        /// How far clear of the box an offset handle sits. It grows with the
        /// handle so the two never overlap on a small box.
        /// </summary>
        private static float AnchorOffset(float glyph)
        {
            return Mathf.Max(
                AnchorOffsetBase,
                AnchorMinHitRadius + glyph * AnchorHitRadiusScale);
        }

        private bool TryBuildSelectionScreenPolygon(
            ChartNote note,
            double nowMs,
            out Vector2[] polygon)
        {
            polygon = null;
            if (note.Kind == NoteKind.SkyArea)
            {
                return TryBuildSkyAreaScreenPolygon(
                    note,
                    nowMs,
                    true,
                    out polygon);
            }
            if (TryBuildNoteScreenPolygon(note, nowMs, out polygon))
            {
                if (note.Kind == NoteKind.Tap ||
                    note.Kind == NoteKind.Hold ||
                    note.Kind == NoteKind.Flick)
                {
                    return true;
                }
                var local = NoteLocalPosition(note, nowMs);
                return TryClipPolygonToTrack(
                    polygon,
                    local.y,
                    out polygon);
            }
            bool laneSelectionHandled;
            if (TryBuildClippedLaneSelectionPolygon(
                    note,
                    nowMs,
                    out polygon,
                    out laneSelectionHandled))
            {
                return true;
            }
            return false;
        }

        private bool TryBuildClippedLaneSelectionPolygon(
            ChartNote note,
            double nowMs,
            out Vector2[] polygon,
            out bool handled)
        {
            polygon = null;
            handled = false;
            if (note.Kind != NoteKind.Tap &&
                note.Kind != NoteKind.Hold)
            {
                return false;
            }

            var startZ = _lineLocal.z +
                         (float)_motion.NoteZ(note, nowMs);
            var endZ = note.Kind == NoteKind.Tap
                ? startZ + 0.06f
                : _lineLocal.z +
                  (float)_motion.NoteZAt(note.EndTimeMs, nowMs);
            if (note.Kind == NoteKind.Tap)
            {
                startZ -= 0.06f;
            }
            startZ = Mathf.Max(startZ, _lineLocal.z);
            endZ = Mathf.Min(endZ, _trackEndZ);
            if (endZ <= startZ + 0.001f)
            {
                handled = true;
                return false;
            }

            var side = note.StartLane <= 0
                ? _leftGeometry
                : (note.StartLane >= 5 ? _rightGeometry : null);
            if (side != null)
            {
                Vector3 startLeft;
                Vector3 startRight;
                Vector3 endLeft;
                Vector3 endRight;
                if (!TryGetSidePoints(
                        side,
                        startZ,
                        out startLeft,
                        out startRight) ||
                    !TryGetSidePoints(
                        side,
                        endZ,
                        out endLeft,
                        out endRight))
                {
                    return false;
                }
                handled = true;
                return TryBuildQuadPolygon(
                    startLeft,
                    startRight,
                    endRight,
                    endLeft,
                    out polygon);
            }

            var laneStep = (_laneX[2] - _laneX[1] +
                            _laneX[4] - _laneX[3]) * 0.25f;
            var start = Mathf.Clamp(note.StartLane, 1, 4);
            var end = Mathf.Clamp(
                note.StartLane + Mathf.Max(1, note.WidthLanes) - 1,
                1,
                4);
            var center = (_laneX[start] + _laneX[end]) * 0.5f;
            var halfWidth =
                Mathf.Abs(_laneX[end] - _laneX[start]) * 0.5f +
                laneStep * 0.5f;
            handled = true;
            return TryBuildQuadPolygon(
                new Vector3(
                    center - halfWidth,
                    _floorY,
                    startZ),
                new Vector3(
                    center + halfWidth,
                    _floorY,
                    startZ),
                new Vector3(
                    center + halfWidth,
                    _floorY,
                    endZ),
                new Vector3(
                    center - halfWidth,
                    _floorY,
                    endZ),
                out polygon);
        }

        private bool TryClipPolygonToTrack(
            Vector2[] polygon,
            float localY,
            out Vector2[] clipped)
        {
            clipped = polygon;
            if (polygon == null ||
                polygon.Length < 3 ||
                _trackEndZ <= _lineLocal.z + 0.001f)
            {
                return polygon != null && polygon.Length >= 3;
            }

            Vector2 startLeft;
            Vector2 startRight;
            Vector2 endLeft;
            Vector2 endRight;
            Vector2 insidePoint;
            var middleZ = (_lineLocal.z + _trackEndZ) * 0.5f;
            if (!TryProjectTrackPoint(
                    localY,
                    -TrackHalfWidth,
                    _lineLocal.z,
                    out startLeft) ||
                !TryProjectTrackPoint(
                    localY,
                    TrackHalfWidth,
                    _lineLocal.z,
                    out startRight) ||
                !TryProjectTrackPoint(
                    localY,
                    -TrackHalfWidth,
                    _trackEndZ,
                    out endLeft) ||
                !TryProjectTrackPoint(
                    localY,
                    TrackHalfWidth,
                    _trackEndZ,
                    out endRight) ||
                !TryProjectTrackPoint(
                    localY,
                    0.0f,
                    middleZ,
                    out insidePoint))
            {
                return false;
            }

            Vector2[] startClipped;
            if (!ClipPolygonByLine(
                    clipped,
                    startLeft,
                    startRight,
                    insidePoint,
                    out startClipped))
            {
                return false;
            }
            Vector2[] endClipped;
            if (!ClipPolygonByLine(
                    startClipped,
                    endLeft,
                    endRight,
                    insidePoint,
                    out endClipped))
            {
                return false;
            }
            clipped = endClipped;
            return clipped.Length >= 3;
        }

        private bool TryProjectTrackPoint(
            float localY,
            float localX,
            float localZ,
            out Vector2 point)
        {
            return TryProjectWorld(
                _master.TransformPoint(
                    new Vector3(localX, localY, localZ)),
                out point);
        }

        private static bool ClipPolygonByLine(
            Vector2[] source,
            Vector2 lineStart,
            Vector2 lineEnd,
            Vector2 insidePoint,
            out Vector2[] clipped)
        {
            clipped = source;
            var direction = lineEnd - lineStart;
            var normal = new Vector2(-direction.y, direction.x);
            var insideDistance = Vector2.Dot(
                normal,
                insidePoint - lineStart);
            if (Mathf.Abs(insideDistance) < 0.0001f)
            {
                return true;
            }
            var sign = insideDistance > 0.0f ? 1.0f : -1.0f;
            var output = new List<Vector2>(source.Length + 2);
            for (var i = 0; i < source.Length; i++)
            {
                var previous = source[
                    (i + source.Length - 1) % source.Length];
                var current = source[i];
                var previousDistance = Vector2.Dot(
                    normal,
                    previous - lineStart) * sign;
                var currentDistance = Vector2.Dot(
                    normal,
                    current - lineStart) * sign;
                var previousInside = previousDistance >= -0.001f;
                var currentInside = currentDistance >= -0.001f;
                if (previousInside != currentInside)
                {
                    var denominator =
                        previousDistance - currentDistance;
                    if (Mathf.Abs(denominator) > 0.000001f)
                    {
                        var t = previousDistance / denominator;
                        AppendUniquePoint(
                            output,
                            Vector2.LerpUnclamped(previous, current, t));
                    }
                }
                if (currentInside)
                {
                    AppendUniquePoint(output, current);
                }
            }
            if (output.Count > 2 &&
                (output[0] - output[output.Count - 1]).sqrMagnitude <
                0.0001f)
            {
                output.RemoveAt(output.Count - 1);
            }
            clipped = output.ToArray();
            return clipped.Length >= 3;
        }

        private static void AppendUniquePoint(
            List<Vector2> points,
            Vector2 point)
        {
            if (points.Count > 0 &&
                (points[points.Count - 1] - point).sqrMagnitude <
                0.0001f)
            {
                return;
            }
            points.Add(point);
        }

        private bool TryBuildNoteScreenPolygon(
            ChartNote note,
            double nowMs,
            out Vector2[] polygon)
        {
            polygon = null;
            if (note == null)
            {
                return false;
            }
            if (note.Kind == NoteKind.Flick)
            {
                if (TryBuildWorldPointsScreenHull(
                        note,
                        nowMs,
                        out polygon))
                {
                    return true;
                }
                var center = TrackX((float)note.FlickCenterPos);
                var width = FlickWorldWidth(note);
                var z = _lineLocal.z +
                        (float)_motion.NoteZ(note, nowMs);
                var y = _lineLocal.y + SkyY;
                return TryBuildQuadPolygon(
                    new Vector3(center - width * 0.5f, y - 0.045f, z),
                    new Vector3(center + width * 0.5f, y - 0.045f, z),
                    new Vector3(center + width * 0.5f, y + 0.045f, z),
                    new Vector3(center - width * 0.5f, y + 0.045f, z),
                    out polygon);
            }
            if (note.Kind == NoteKind.SkyArea)
            {
                return TryBuildSkyAreaScreenPolygon(
                    note,
                    nowMs,
                    true,
                    out polygon);
            }
            if ((note.Kind == NoteKind.Tap ||
                 note.Kind == NoteKind.Hold) &&
                TryBuildWorldPointsScreenHull(
                    note,
                    nowMs,
                    out polygon))
            {
                return true;
            }
            if (note.Kind == NoteKind.Hold &&
                note.StartLane > 0 &&
                note.StartLane < 5)
            {
                var laneStep = (_laneX[2] - _laneX[1] +
                                _laneX[4] - _laneX[3]) * 0.25f;
                var start = Mathf.Clamp(note.StartLane, 1, 4);
                var end = Mathf.Clamp(
                    note.StartLane + Mathf.Max(1, note.WidthLanes) - 1,
                    1,
                    4);
                var center = (_laneX[start] + _laneX[end]) * 0.5f;
                var halfWidth =
                    Mathf.Abs(_laneX[end] - _laneX[start]) * 0.5f +
                    laneStep * 0.5f;
                var startZ = _lineLocal.z +
                             (float)_motion.NoteZ(note, nowMs);
                var endZ = _lineLocal.z +
                           (float)_motion.NoteZAt(
                               note.EndTimeMs,
                               nowMs);
                return TryBuildQuadPolygon(
                    new Vector3(center - halfWidth, _floorY, startZ),
                    new Vector3(center + halfWidth, _floorY, startZ),
                    new Vector3(center + halfWidth, _floorY, endZ),
                    new Vector3(center - halfWidth, _floorY, endZ),
                    out polygon);
            }
            if (TryBuildWorldPointsScreenHull(
                    note,
                    nowMs,
                    out polygon))
            {
                return true;
            }
            Bounds worldBounds;
            if (_noteRenderer == null ||
                !_noteRenderer.TryGetNoteWorldBounds(
                    note,
                    nowMs,
                    out worldBounds))
            {
                return false;
            }
            return TryBuildBoundsScreenHull(
                worldBounds,
                out polygon);
        }

        /// <summary>
        /// Rebuilds the cached playhead-independent part of a SkyArea's
        /// outline: the eased X edges and each sample's scaled time.
        /// </summary>
        private void RefreshSkyAreaPolygonCache(
            ChartNote note,
            int steps,
            float localY,
            float baseZ)
        {
            var needed = steps + 1;
            if (_skyPolyCacheLeftEdge == null ||
                _skyPolyCacheLeftEdge.Length < needed)
            {
                _skyPolyCacheLeftEdge = new float[needed];
                _skyPolyCacheRightEdge = new float[needed];
                _skyPolyCacheScaledTime = new double[needed];
            }

            var startCenter = TrackX((float)note.StartPos);
            var endCenter = TrackX((float)note.EndPos);
            var startWidth = Mathf.Max(
                0.01f,
                (float)note.StartWidth * (TrackHalfWidth * 2.0f));
            var endWidth = Mathf.Max(
                0.01f,
                (float)note.EndWidth * (TrackHalfWidth * 2.0f));

            for (var step = 0; step <= steps; step++)
            {
                var fraction = step / (float)steps;
                var leftProgress = SkyAreaGeometry.Ease(
                    fraction,
                    note.LeftEasing);
                var rightProgress = SkyAreaGeometry.Ease(
                    fraction,
                    note.RightEasing);
                _skyPolyCacheLeftEdge[step] = Mathf.Lerp(
                    startCenter - startWidth * 0.5f,
                    endCenter - endWidth * 0.5f,
                    leftProgress);
                _skyPolyCacheRightEdge[step] = Mathf.Lerp(
                    startCenter + startWidth * 0.5f,
                    endCenter + endWidth * 0.5f,
                    rightProgress);
                _skyPolyCacheScaledTime[step] = _chart.ScaledTimeAt(
                    note.TimeMs + note.DurationMs * fraction);
            }

            _skyPolyCacheNote = note;
            _skyPolyCacheVersion = _chart.TimelineVersion;
            _skyPolyCacheSteps = steps;
            _skyPolyCacheLocalY = localY;
            _skyPolyCacheBaseZ = baseZ;
            _skyPolyCacheLeftEasing = note.LeftEasing;
            _skyPolyCacheRightEasing = note.RightEasing;
            _skyPolyCacheStartCenter = startCenter;
            _skyPolyCacheEndCenter = endCenter;
            _skyPolyCacheStartWidth = startWidth;
            _skyPolyCacheEndWidth = endWidth;
            _skyPolyCacheTimeMs = note.TimeMs;
            _skyPolyCacheDurationMs = note.DurationMs;
        }

        private bool SkyAreaPolygonCacheIsValid(
            ChartNote note,
            int steps,
            float localY,
            float baseZ)
        {
            return _skyPolyCacheNote == note &&
                   _skyPolyCacheVersion == _chart.TimelineVersion &&
                   _skyPolyCacheSteps == steps &&
                   _skyPolyCacheLocalY == localY &&
                   _skyPolyCacheBaseZ == baseZ &&
                   _skyPolyCacheLeftEasing == note.LeftEasing &&
                   _skyPolyCacheRightEasing == note.RightEasing &&
                   _skyPolyCacheTimeMs == note.TimeMs &&
                   _skyPolyCacheDurationMs == note.DurationMs &&
                   _skyPolyCacheStartCenter == TrackX((float)note.StartPos) &&
                   _skyPolyCacheEndCenter == TrackX((float)note.EndPos) &&
                   _skyPolyCacheStartWidth ==
                       Mathf.Max(
                           0.01f,
                           (float)note.StartWidth *
                           (TrackHalfWidth * 2.0f)) &&
                   _skyPolyCacheEndWidth ==
                       Mathf.Max(
                           0.01f,
                           (float)note.EndWidth *
                           (TrackHalfWidth * 2.0f));
        }

        private bool TryBuildSkyAreaScreenPolygon(
            ChartNote note,
            double nowMs,
            bool clipToTrack,
            out Vector2[] polygon)
        {
            polygon = null;
            var steps = SkyAreaGeometry.SubdivisionCount(note);
            var root = _skyRoot != null ? _skyRoot : _master;
            var localY = _skyRoot != null
                ? 0.0f
                : _lineLocal.y + SkyY;
            var baseZ = _skyRoot != null ? 0.0f : _lineLocal.z;

            if (!SkyAreaPolygonCacheIsValid(note, steps, localY, baseZ))
            {
                RefreshSkyAreaPolygonCache(
                    note,
                    steps,
                    localY,
                    baseZ);
            }

            // Only the view-space Z depends on the playhead: it is the only
            // term of the sample position that moves. Everything else is
            // served from the cache above.
            //
            // The arithmetic mirrors TrackMotion.NoteZAt EXACTLY
            // ((scaledDelta / 1000.0) * TrackLength * TrackScale * Speed), so
            // this produces bit-identical Z instead of a re-associated
            // product that could differ in the last place.
            var scaledNow = _motion.ScaledNowAt(nowMs);
            var trackLength = _motion.TrackLength;

            var leftEdge = _skyPolyLeftScratch;
            var rightEdge = _skyPolyRightScratch;
            leftEdge.Clear();
            rightEdge.Clear();
            for (var step = 0; step <= steps; step++)
            {
                var scaledDelta =
                    _skyPolyCacheScaledTime[step] - scaledNow;
                var rawZ = (float)(
                    scaledDelta / 1000.0 *
                    trackLength *
                    TrackMotion.TrackScale *
                    _motion.Speed);
                AppendUniqueLocalPoint(
                    leftEdge,
                    new Vector3(
                        _skyPolyCacheLeftEdge[step], localY, baseZ + rawZ));
                AppendUniqueLocalPoint(
                    rightEdge,
                    new Vector3(
                        _skyPolyCacheRightEdge[step], localY, baseZ + rawZ));
            }
            if (leftEdge.Count < 2 || rightEdge.Count < 2)
            {
                return false;
            }

            var points = _skyPolyPointsScratch;
            points.Clear();
            for (var i = 0; i < leftEdge.Count; i++)
            {
                AppendUniqueLocalPoint(points, leftEdge[i]);
            }
            for (var i = rightEdge.Count - 1; i >= 0; i--)
            {
                AppendUniqueLocalPoint(points, rightEdge[i]);
            }
            if (clipToTrack)
            {
                points = ClipPolygonByZ(
                    points,
                    baseZ,
                    baseZ + (float)_motion.TrackLength);
            }
            if (points.Count < 3)
            {
                return false;
            }

            var screenPoints = _skyPolyScreenScratch;
            screenPoints.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                AppendUniqueScreenPoint(
                    screenPoints,
                    root.TransformPoint(points[i]));
            }
            if (screenPoints.Count < 3)
            {
                return false;
            }

            // The caller RETAINS the array: BuildSelection adds it to the
            // polygons list, keeps it as `anchorPolygon`, and hands it to
            // TryBuildSelectionAnchors. Several retained polygons can coexist
            // within one frame, so a shared scratch array would alias them.
            // Allocate one array per successful build (the previous version
            // allocated four Lists plus a ToArray on top of this one).
            var result = new Vector2[screenPoints.Count];
            screenPoints.CopyTo(result, 0);
            polygon = result;
            return true;
        }

        private static void AppendUniqueLocalPoint(
            List<Vector3> points,
            Vector3 point)
        {
            if (points.Count > 0 &&
                (points[points.Count - 1] - point).sqrMagnitude <
                0.000001f)
            {
                return;
            }
            points.Add(point);
        }

        private static List<Vector3> ClipPolygonByZ(
            List<Vector3> source,
            float minZ,
            float maxZ)
        {
            var clipped = ClipPolygonByZPlane(
                source,
                minZ,
                true);
            return ClipPolygonByZPlane(
                clipped,
                maxZ,
                false);
        }

        private static List<Vector3> ClipPolygonByZPlane(
            List<Vector3> source,
            float planeZ,
            bool keepGreater)
        {
            var output = new List<Vector3>(source.Count + 2);
            if (source.Count == 0)
            {
                return output;
            }
            for (var i = 0; i < source.Count; i++)
            {
                var previous = source[
                    (i + source.Count - 1) % source.Count];
                var current = source[i];
                var previousInside = keepGreater
                    ? previous.z >= planeZ
                    : previous.z <= planeZ;
                var currentInside = keepGreater
                    ? current.z >= planeZ
                    : current.z <= planeZ;
                if (previousInside != currentInside)
                {
                    var denominator = current.z - previous.z;
                    if (Mathf.Abs(denominator) > 0.000001f)
                    {
                        var t = (planeZ - previous.z) / denominator;
                        AppendUniqueLocalPoint(
                            output,
                            Vector3.LerpUnclamped(previous, current, t));
                    }
                }
                if (currentInside)
                {
                    AppendUniqueLocalPoint(output, current);
                }
            }
            if (output.Count > 2 &&
                (output[0] - output[output.Count - 1]).sqrMagnitude <
                0.000001f)
            {
                output.RemoveAt(output.Count - 1);
            }
            return output;
        }

        private void AppendUniqueScreenPoint(
            List<Vector2> points,
            Vector3 worldPoint)
        {
            Vector2 screenPoint;
            if (TryProjectWorld(worldPoint, out screenPoint))
            {
                AppendUniqueScreenPoint(points, screenPoint);
            }
        }

        private static void AppendUniqueScreenPoint(
            List<Vector2> points,
            Vector2 point)
        {
            if (points.Count > 0 &&
                (points[points.Count - 1] - point).sqrMagnitude < 0.01f)
            {
                return;
            }
            points.Add(point);
        }

        private bool TryBuildWorldPointsScreenHull(
            ChartNote note,
            double nowMs,
            out Vector2[] polygon)
        {
            polygon = null;
            if (_noteRenderer == null)
            {
                return false;
            }
            var worldPoints = new List<Vector3>();
            if (!_noteRenderer.TryGetNoteWorldPoints(
                    note,
                    nowMs,
                    worldPoints))
            {
                return false;
            }
            var screenPoints = new List<Vector2>(worldPoints.Count);
            for (var i = 0; i < worldPoints.Count; i++)
            {
                Vector2 point;
                if (TryProjectWorld(worldPoints[i], out point))
                {
                    screenPoints.Add(point);
                }
            }
            if (screenPoints.Count < 3)
            {
                return false;
            }
            polygon = ConvexHull(screenPoints);
            return polygon.Length >= 3;
        }

        private bool TryBuildQuadPolygon(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            out Vector2[] polygon)
        {
            polygon = null;
            Vector2 pa;
            Vector2 pb;
            Vector2 pc;
            Vector2 pd;
            if (!TryProjectLocal(a, out pa) ||
                !TryProjectLocal(b, out pb) ||
                !TryProjectLocal(c, out pc) ||
                !TryProjectLocal(d, out pd))
            {
                return false;
            }
            polygon = new[] { pa, pb, pc, pd };
            return true;
        }

        private bool TryBuildBoundsScreenHull(
            Bounds worldBounds,
            out Vector2[] polygon)
        {
            polygon = null;
            var min = worldBounds.min;
            var max = worldBounds.max;
            var points = new List<Vector2>(8);
            AddProjectedCorner(points, new Vector3(min.x, min.y, min.z));
            AddProjectedCorner(points, new Vector3(max.x, min.y, min.z));
            AddProjectedCorner(points, new Vector3(min.x, max.y, min.z));
            AddProjectedCorner(points, new Vector3(max.x, max.y, min.z));
            AddProjectedCorner(points, new Vector3(min.x, min.y, max.z));
            AddProjectedCorner(points, new Vector3(max.x, min.y, max.z));
            AddProjectedCorner(points, new Vector3(min.x, max.y, max.z));
            AddProjectedCorner(points, new Vector3(max.x, max.y, max.z));
            if (points.Count < 3)
            {
                return false;
            }
            polygon = ConvexHull(points);
            return polygon.Length >= 3;
        }

        private void AddProjectedCorner(
            List<Vector2> output,
            Vector3 worldPoint)
        {
            Vector2 point;
            if (TryProjectWorld(worldPoint, out point))
            {
                output.Add(point);
            }
        }

        private bool TryProjectLocal(Vector3 local, out Vector2 point)
        {
            return TryProjectWorld(
                _master.TransformPoint(local),
                out point);
        }

        private bool TryProjectWorld(Vector3 world, out Vector2 point)
        {
            point = Vector2.zero;
            var camera = Camera.main;
            if (camera == null || _canvasRect == null)
            {
                return false;
            }
            var screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0.0f)
            {
                return false;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                screen,
                null,
                out point);
        }

        private static Vector2[] ConvexHull(List<Vector2> points)
        {
            points.Sort(delegate(Vector2 left, Vector2 right)
            {
                var byX = left.x.CompareTo(right.x);
                return byX != 0 ? byX : left.y.CompareTo(right.y);
            });
            var hull = new List<Vector2>(points.Count * 2);
            for (var i = 0; i < points.Count; i++)
            {
                while (hull.Count >= 2 &&
                       Cross(
                           hull[hull.Count - 2],
                           hull[hull.Count - 1],
                           points[i]) <= 0.0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(points[i]);
            }
            var lowerCount = hull.Count;
            for (var i = points.Count - 2; i >= 0; i--)
            {
                while (hull.Count > lowerCount &&
                       Cross(
                           hull[hull.Count - 2],
                           hull[hull.Count - 1],
                           points[i]) <= 0.0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(points[i]);
            }
            if (hull.Count > 1)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            return hull.ToArray();
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) -
                   (b.y - a.y) * (c.x - a.x);
        }

        private static bool PointInPolygon(
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

        private static float DistanceToPolygon(
            Vector2 point,
            Vector2[] polygon)
        {
            var best = float.MaxValue;
            for (var i = 0; i < polygon.Length; i++)
            {
                var next = (i + 1) % polygon.Length;
                best = Mathf.Min(
                    best,
                    DistanceToSegment(
                        point,
                        polygon[i],
                        polygon[next]));
            }
            return best;
        }

        private static float FlickWorldWidth(ChartNote note)
        {
            return Mathf.Max(0.0f, (float)note.FlickWidth) *
                   (TrackHalfWidth * 2.0f);
        }

        private void UpdateGhosts(
            double nowMs,
            FalsumEditorGhost ghost,
            IList<FalsumEditorGhost> pasteGhosts)
        {
            _cursorMesh.Clear();
            _ghostMesh.Clear();
            _editorGhostNotes.Clear();
            GhostEndsAsPoint = false;
            GhostScreenDebug = "none";

            var modelTint = new Color(0.28f, 0.92f, 1.0f, 0.42f);
            var hasModelGhost = false;
            if (ghost.Visible)
            {
                AddGhostToPreview(
                    ghost,
                    nowMs,
                    ref hasModelGhost,
                    ref modelTint);
            }
            if (pasteGhosts != null)
            {
                for (var i = 0; i < pasteGhosts.Count; i++)
                {
                    var pasteGhost = pasteGhosts[i];
                    if (!pasteGhost.Visible)
                    {
                        continue;
                    }
                    AddGhostToPreview(
                        pasteGhost,
                        nowMs,
                        ref hasModelGhost,
                        ref modelTint);
                }
            }

            if (_noteRenderer == null ||
                _editorGhostNotes.Count == 0)
            {
                if (_noteRenderer != null &&
                    !string.IsNullOrEmpty(_lastGhostKey))
                {
                    _noteRenderer.ClearEditorGhost();
                    _lastGhostKey = null;
                }
                return;
            }

            var key = BuildEditorGhostKey(_editorGhostNotes);
            if (!string.Equals(
                    key,
                    _lastGhostKey,
                    StringComparison.Ordinal))
            {
                _noteRenderer.SetEditorGhosts(
                    _editorGhostNotes,
                    modelTint);
                _lastGhostKey = key;
            }
            _noteRenderer.UpdateEditorGhost(nowMs);
        }

        private void AddGhostToPreview(
            FalsumEditorGhost ghost,
            double nowMs,
            ref bool hasModelGhost,
            ref Color modelTint)
        {
            if (!IsNoteKindVisible(ghost.Kind))
            {
                return;
            }
            if (ghost.Kind == NoteKind.Tap ||
                ghost.Kind == NoteKind.Hold)
            {
                _editorGhostNotes.Add(CreateGhostNote(ghost));
                if (!hasModelGhost)
                {
                    modelTint = ghost.Color;
                    hasModelGhost = true;
                }
                return;
            }
            GhostEndsAsPoint = ghost.EndAsPoint;
            BuildGhostMesh(ghost, nowMs);
        }

        private static string BuildEditorGhostKey(
            IList<ChartNote> notes)
        {
            var key = string.Empty;
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                key += ((int)note.Kind).ToString() + "|" +
                       note.TimeMs.ToString("R") + "|" +
                       note.DurationMs.ToString("R") + "|" +
                       note.StartLane + "|" +
                       note.WidthLanes + "|" +
                       note.StartPos.ToString("R") + "|" +
                       note.EndPos.ToString("R") + "|" +
                       note.StartWidth.ToString("R") + "|" +
                       note.EndWidth.ToString("R") + "|" +
                       note.LeftEasing + "|" +
                       note.RightEasing + "|" +
                       note.FlickDirection + ";";
            }
            return key;
        }

        private void BuildGhostMesh(
            FalsumEditorGhost ghost,
            double nowMs)
        {
            var y = _lineLocal.y + SkyY + 0.008f;
            var color = ghost.Color;
            if (ghost.Kind == NoteKind.Flick)
            {
                var z = _lineLocal.z +
                        (float)_motion.NoteZAt(
                            ghost.TimeMs,
                            nowMs);
                var left = TrackX(
                    (float)(ghost.StartPos -
                            ghost.StartWidth * 0.5));
                var right = TrackX(
                    (float)(ghost.StartPos +
                            ghost.StartWidth * 0.5));
                AddQuad(
                    _ghostMesh,
                    new Vector3(left, y, z - 0.018f),
                    new Vector3(right, y, z - 0.018f),
                    new Vector3(left, y, z + 0.018f),
                    new Vector3(right, y, z + 0.018f),
                    color);
                return;
            }

            var startZ = _lineLocal.z +
                         (float)_motion.NoteZAt(
                             ghost.TimeMs,
                             nowMs);
            var endZ = _lineLocal.z +
                       (float)_motion.NoteZAt(
                           ghost.EndTimeMs,
                           nowMs);
            var startLeft = TrackX(
                (float)(ghost.StartPos -
                        ghost.StartWidth * 0.5));
            var startRight = TrackX(
                (float)(ghost.StartPos +
                        ghost.StartWidth * 0.5));
            if (ghost.EndAsPoint)
            {
                var apex = TrackX((float)ghost.EndPos);
                var triangleColor = color;
                triangleColor.a = Mathf.Clamp01(color.a + 0.12f);
                var startLeftPoint = new Vector3(
                    startLeft,
                    y,
                    startZ);
                var startRightPoint = new Vector3(
                    startRight,
                    y,
                    startZ);
                var apexPoint = new Vector3(apex, y, endZ);
                AddTriangle(
                    _ghostMesh,
                    startLeftPoint,
                    startRightPoint,
                    apexPoint,
                    triangleColor);
                var rim = color;
                rim.a = 0.95f;
                if (ghost.DrawRim)
                {
                    AddGhostEdge(
                        startLeftPoint,
                        startRightPoint,
                        rim,
                        0.018f);
                    AddGhostEdge(startLeftPoint, apexPoint, rim, 0.018f);
                    AddGhostEdge(startRightPoint, apexPoint, rim, 0.018f);
                }
                GhostScreenDebug = ProjectGhostPoint(
                    "baseL",
                    startLeftPoint) +
                    " " +
                    ProjectGhostPoint("baseR", startRightPoint) +
                    " " +
                    ProjectGhostPoint("apex", apexPoint);
                return;
            }

            var endLeft = TrackX(
                (float)(ghost.EndPos -
                        ghost.EndWidth * 0.5));
            var endRight = TrackX(
                (float)(ghost.EndPos +
                        ghost.EndWidth * 0.5));
            // Tessellate along time and ease each edge, exactly like the real
            // SkyArea mesh (TryBuildSkyAreaScreenPolygon) and like
            // NoteRenderer's segment builder. A single quad here drew a
            // straight trapezoid, so the place/paste ghost disagreed with the
            // eased silhouette the preset actually carries.
            var ghostSteps = Math.Max(
                1,
                (int)(ghost.EndTimeMs - ghost.TimeMs) / 5);
            ghostSteps = Math.Min(ghostSteps, 64);
            var prevLeft = startLeft;
            var prevRight = startRight;
            var prevZ = startZ;
            for (var step = 1; step <= ghostSteps; step++)
            {
                var fraction = step / (float)ghostSteps;
                var leftEdge = TrackX((float)(
                    (ghost.StartPos - ghost.StartWidth * 0.5) +
                    ((ghost.EndPos - ghost.EndWidth * 0.5) -
                     (ghost.StartPos - ghost.StartWidth * 0.5)) *
                    SkyAreaGeometry.Ease(fraction, ghost.LeftEasing)));
                var rightEdge = TrackX((float)(
                    (ghost.StartPos + ghost.StartWidth * 0.5) +
                    ((ghost.EndPos + ghost.EndWidth * 0.5) -
                     (ghost.StartPos + ghost.StartWidth * 0.5)) *
                    SkyAreaGeometry.Ease(fraction, ghost.RightEasing)));
                var sampleZ = startZ + (endZ - startZ) * fraction;
                AddQuad(
                    _ghostMesh,
                    new Vector3(prevLeft, y, prevZ),
                    new Vector3(prevRight, y, prevZ),
                    new Vector3(leftEdge, y, sampleZ),
                    new Vector3(rightEdge, y, sampleZ),
                    color);
                prevLeft = leftEdge;
                prevRight = rightEdge;
                prevZ = sampleZ;
            }
            var quadRim = color;
            quadRim.a = 0.95f;
            if (ghost.DrawRim)
            {
                AddGhostEdge(
                    new Vector3(startLeft, y, startZ),
                    new Vector3(startRight, y, startZ),
                    quadRim,
                    0.018f);
                AddGhostEdge(
                    new Vector3(endLeft, y, endZ),
                    new Vector3(endRight, y, endZ),
                    quadRim,
                    0.018f);
                AddGhostEdge(
                    new Vector3(startLeft, y, startZ),
                    new Vector3(endLeft, y, endZ),
                    quadRim,
                    0.018f);
                AddGhostEdge(
                    new Vector3(startRight, y, startZ),
                    new Vector3(endRight, y, endZ),
                    quadRim,
                    0.018f);
            }
            GhostScreenDebug = ProjectGhostPoint(
                "startL",
                new Vector3(startLeft, y, startZ)) +
                " " +
                ProjectGhostPoint(
                    "endR",
                    new Vector3(endRight, y, endZ));
        }

        private void AddGhostEdge(
            Vector3 start,
            Vector3 end,
            Color color,
            float thickness)
        {
            var direction = end - start;
            var perpendicular = new Vector3(
                -direction.z,
                0.0f,
                direction.x);
            if (perpendicular.sqrMagnitude < 0.000001f)
            {
                return;
            }
            perpendicular.Normalize();
            perpendicular *= thickness * 0.5f;
            AddQuad(
                _ghostMesh,
                start - perpendicular,
                end - perpendicular,
                start + perpendicular,
                end + perpendicular,
                color);
        }

        private static string ProjectGhostPoint(
            string label,
            Vector3 point)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return label + "=noCamera";
            }
            var screen = camera.WorldToScreenPoint(point);
            return label + "=" +
                   screen.x.ToString("0") + "," +
                   screen.y.ToString("0") + "," +
                   screen.z.ToString("0");
        }

        private static ChartNote CreateGhostNote(FalsumEditorGhost ghost)
        {
            return new ChartNote
            {
                Kind = ghost.Kind,
                TimeMs = ghost.TimeMs,
                DurationMs = Math.Max(
                    0.0,
                    ghost.EndTimeMs - ghost.TimeMs),
                StartLane = ghost.Lane,
                WidthLanes = Mathf.Max(1, ghost.WidthLanes),
                StartPos = ghost.StartPos,
                EndPos = ghost.EndPos,
                StartWidth = ghost.StartWidth,
                EndWidth = ghost.EndWidth,
                StartCellCount = 24,
                EndCellCount = 24,
                FlickDirection = ghost.FlickDirection,
                LeftEasing = ghost.LeftEasing,
                RightEasing = ghost.RightEasing,
                GroupId = 900000,
            };
        }

        private void AddGridSideLine(
            SideGeometry geometry,
            float z,
            float widthPixels,
            Color color)
        {
            Vector3 lowA;
            Vector3 lowB;
            if (geometry == null ||
                !TryGetSidePoints(
                    geometry,
                    z,
                    out lowA,
                    out lowB))
            {
                return;
            }
            AddScreenSpaceLineQuad(
                _gridMesh,
                lowA,
                lowB,
                widthPixels,
                color);
        }

        private bool TryGetSidePoints(
            SideGeometry geometry,
            float targetZ,
            out Vector3 left,
            out Vector3 right)
        {
            left = Vector3.zero;
            right = Vector3.zero;
            if (geometry == null)
            {
                return false;
            }
            var normal = _master.forward;
            var axis = geometry.EndWorld - geometry.JudgementWorld;
            var denominator = Vector3.Dot(axis, normal);
            if (Mathf.Abs(denominator) < 0.00001f)
            {
                return false;
            }
            var planePoint = _master.TransformPoint(
                new Vector3(0.0f, 0.0f, targetZ));
            var progress =
                (Vector3.Dot(planePoint, normal) -
                 Vector3.Dot(geometry.JudgementWorld, normal)) /
                denominator;
            if (progress < -0.02f || progress > 1.02f)
            {
                return false;
            }
            var center = Vector3.Lerp(
                geometry.JudgementLocal,
                geometry.EndLocal,
                progress);
            left = _master.InverseTransformPoint(
                geometry.Transform.TransformPoint(new Vector3(
                    geometry.CenterX,
                    center.y,
                    geometry.WidthMin)));
            right = _master.InverseTransformPoint(
                geometry.Transform.TransformPoint(new Vector3(
                    geometry.CenterX,
                    center.y,
                    geometry.WidthMax)));
            return true;
        }

        private static SideGeometry CreateSideGeometry(
            GameplaySceneBinding binding,
            Transform transform)
        {
            if (transform == null)
            {
                return null;
            }
            var filter = transform.GetComponent<MeshFilter>();
            var renderer = transform.GetComponent<Renderer>();
            var material = renderer != null
                ? renderer.sharedMaterial
                : null;
            if (filter == null ||
                filter.sharedMesh == null ||
                material == null ||
                !material.HasProperty("_JudgementLineWorldPosition") ||
                !material.HasProperty("_EndOfTrackWorldPosition"))
            {
                return null;
            }
            var judgement = (Vector3)material.GetVector(
                "_JudgementLineWorldPosition");
            var end = (Vector3)material.GetVector(
                "_EndOfTrackWorldPosition");
            var bounds = filter.sharedMesh.bounds;
            return new SideGeometry
            {
                Transform = transform,
                JudgementWorld = judgement,
                EndWorld = end,
                JudgementLocal = transform.InverseTransformPoint(judgement),
                EndLocal = transform.InverseTransformPoint(end),
                WidthMin = bounds.min.z,
                WidthMax = bounds.max.z,
                CenterX = bounds.center.x,
            };
        }

        private static float ResolveTrackEndZ(
            Transform root,
            SideGeometry left,
            SideGeometry right,
            float fallbackStartZ)
        {
            var hasEnd = false;
            var endZ = float.MinValue;
            if (left != null)
            {
                endZ = Mathf.Max(
                    endZ,
                    root.InverseTransformPoint(left.EndWorld).z);
                hasEnd = true;
            }
            if (right != null)
            {
                endZ = Mathf.Max(
                    endZ,
                    root.InverseTransformPoint(right.EndWorld).z);
                hasEnd = true;
            }
            return hasEnd
                ? endZ
                : fallbackStartZ + (float)TrackMotion.DefaultTrackLength;
        }

        private Bounds ToLocalBounds(Bounds worldBounds)
        {
            var min = worldBounds.min;
            var max = worldBounds.max;
            var localMin = _master.InverseTransformPoint(min);
            var localMax = localMin;
            var corners = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
            };
            for (var i = 0; i < corners.Length; i++)
            {
                var local = _master.InverseTransformPoint(corners[i]);
                localMin = Vector3.Min(localMin, local);
                localMax = Vector3.Max(localMax, local);
            }
            var bounds = new Bounds();
            bounds.SetMinMax(localMin, localMax);
            return bounds;
        }

        private void AddSelectionBox(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            var color = new Color(1.0f, 0.46f, 0.08f, 0.24f);
            AddQuad(
                _overlayMesh,
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                color);
            AddQuad(
                _overlayMesh,
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
                color);
            AddQuad(
                _overlayMesh,
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                color);
            AddQuad(
                _overlayMesh,
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
                color);
            AddQuad(
                _overlayMesh,
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                color);
            AddQuad(
                _overlayMesh,
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z),
                color);
        }

        private static void SetRectangle(
            LineRenderer line,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            line.loop = true;
            line.positionCount = 4;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.SetPosition(2, c);
            line.SetPosition(3, d);
        }

        private static void SetLine(
            LineRenderer line,
            Vector3 a,
            Vector3 b)
        {
            line.loop = false;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        private bool TryProjectBounds(Bounds bounds, out Rect rect)
        {
            rect = default(Rect);
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var min = bounds.min;
            var max = bounds.max;
            var corners = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
            };
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            for (var i = 0; i < corners.Length; i++)
            {
                var projected = camera.WorldToScreenPoint(corners[i]);
                if (projected.z <= 0.0f)
                {
                    return false;
                }
                minX = Mathf.Min(minX, projected.x);
                minY = Mathf.Min(minY, projected.y);
                maxX = Mathf.Max(maxX, projected.x);
                maxY = Mathf.Max(maxY, projected.y);
            }
            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        private Outline CreateOutline()
        {
            var root = new GameObject("FalsumSelectionOutline");
            root.transform.SetParent(_master, false);
            var lines = new LineRenderer[6];
            for (var i = 0; i < lines.Length; i++)
            {
                var segment = new GameObject("Edge_" + i);
                segment.transform.SetParent(root.transform, false);
                var line = segment.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.widthMultiplier = 0.016f;
                line.numCapVertices = 2;
                line.loop = i < 2;
                line.sharedMaterial = LineMaterial;
                line.startColor = new Color(1.0f, 0.55f, 0.12f, 0.95f);
                line.endColor = new Color(1.0f, 0.55f, 0.12f, 0.95f);
                line.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                lines[i] = line;
            }
            return new Outline
            {
                Root = root,
                Lines = lines,
            };
        }

        private static FalsumSelectionOverlayGraphic CreateSelectionOverlay(
            Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }
            var graphicObject = new GameObject(
                "SelectionGraphic",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(FalsumSelectionOverlayGraphic));
            graphicObject.transform.SetParent(canvas.transform, false);
            graphicObject.transform.SetAsFirstSibling();
            var graphicRect = graphicObject.GetComponent<RectTransform>();
            graphicRect.anchorMin = Vector2.zero;
            graphicRect.anchorMax = Vector2.one;
            graphicRect.offsetMin = Vector2.zero;
            graphicRect.offsetMax = Vector2.zero;
            var graphic =
                graphicObject.GetComponent<FalsumSelectionOverlayGraphic>();
            graphic.raycastTarget = false;
            graphic.color = Color.white;
            graphic.material = Graphic.defaultGraphicMaterial;
            graphic.SetAllDirty();
            return graphic;
        }

        private MeshBuffer CreateGuideMesh()
        {
            var root = new GameObject("FalsumEditorGuide");
            root.transform.SetParent(_master, false);
            var renderer = root.AddComponent<MeshRenderer>();
            var mesh = new Mesh
            {
                name = "FalsumEditorGuide_Mesh",
            };
            mesh.MarkDynamic();
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = GuideMaterial;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return new MeshBuffer
            {
                Root = root,
                Mesh = mesh,
                Renderer = renderer,
            };
        }

        private MeshBuffer CreateMesh(
            string name,
            int queue,
            bool useVertexColorLineShader = false)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_master, false);
            var filter = root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            var mesh = new Mesh
            {
                name = name + "_Mesh",
            };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            var material = useVertexColorLineShader
                ? CreateLineMaterial(queue)
                : CreateMaterial(queue);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return new MeshBuffer
            {
                Root = root,
                Mesh = mesh,
                Renderer = renderer,
            };
        }

        private Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null)
                {
                    _lineMaterial = CreateMaterial(3035);
                    _lineMaterial.color = Color.white;
                }
                return _lineMaterial;
            }
        }

        /// <summary>
        /// Material for the pointer guide. It must DEPTH TEST (unlike the
        /// grid/ruler shader, which is <c>ZTest Always</c>): the guide is a
        /// world-space strip lying in the track plane, so perspective has to
        /// foreshorten it and notes in front of it have to occlude it. That is
        /// the whole point of drawing it as world geometry rather than as a flat
        /// screen-space line.
        /// </summary>
        private Material GuideMaterial
        {
            get
            {
                if (_guideMaterial == null)
                {
                    var shader = Resources.Load<Shader>(
                        "Falsum/Shaders/FalsumEditorDepthGuide");
                    if (shader == null)
                    {
                        shader = Shader.Find(
                            "Falsum/Editor/Depth Guide");
                    }
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.shader.editorOverlayMissing"));
                    }
                    _guideMaterial = new Material(shader)
                    {
                        renderQueue = 3034,
                    };
                    _guideMaterial.SetColor(
                        "_Color",
                        Color.white);
                }
                return _guideMaterial;
            }
        }

        private Material _guideMaterial;

        private static Material CreateMaterial(int queue)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.shader.editorOverlayMissing"));
            }
            return new Material(shader)
            {
                renderQueue = queue,
            };
        }

        private static Material CreateLineMaterial(int queue)
        {
            var shader = Resources.Load<Shader>(
                "Falsum/Shaders/FalsumEditorGridLine");
            if (shader == null)
            {
                shader = Shader.Find("Falsum/Editor/Grid Line");
            }
            if (shader == null)
            {
                shader = Shader.Find("InFalsus/Gameplay/MeasureLine");
            }
            if (shader == null)
            {
                return CreateMaterial(queue);
            }
            return new Material(shader)
            {
                renderQueue = queue,
            };
        }

        private static void AddHorizontalGridLine(
            MeshBuffer buffer,
            float left,
            float right,
            float y,
            float z,
            float widthPixels,
            Color color)
        {
            AddScreenSpaceLineQuad(
                buffer,
                new Vector3(left, y, z),
                new Vector3(right, y, z),
                widthPixels,
                color);
        }

        private static void AddVerticalQuad(
            MeshBuffer buffer,
            float x,
            float bottom,
            float top,
            float z,
            float thickness,
            Color color)
        {
            AddQuad(
                buffer,
                new Vector3(x - thickness * 0.5f, bottom, z),
                new Vector3(x + thickness * 0.5f, bottom, z),
                new Vector3(x - thickness * 0.5f, top, z),
                new Vector3(x + thickness * 0.5f, top, z),
                color);
        }

        private static void AddQuad(
            MeshBuffer buffer,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Color color)
        {
            var index = buffer.Vertices.Count;
            buffer.Vertices.Add(a);
            buffer.Vertices.Add(b);
            buffer.Vertices.Add(c);
            buffer.Vertices.Add(d);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Triangles.Add(index);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 3);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index);
            buffer.Triangles.Add(index + 3);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 2);
        }

        private static void AddTriangle(
            MeshBuffer buffer,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Color color)
        {
            var index = buffer.Vertices.Count;
            buffer.Vertices.Add(a);
            buffer.Vertices.Add(b);
            buffer.Vertices.Add(c);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Uvs.Add(Vector2.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Normals.Add(Vector3.zero);
            buffer.Triangles.Add(index);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index);
        }

        private static void AddScreenSpaceLineQuad(
            MeshBuffer buffer,
            Vector3 start,
            Vector3 end,
            float widthPixels,
            Color color)
        {
            // UV.x is the side sign, UV.y is the half-width in pixels,
            // and the normal supplies the world-space centerline tangent.
            var tangent = (end - start).sqrMagnitude > 0.0000001f
                ? (end - start).normalized
                : Vector3.right;
            var halfWidth = Mathf.Max(0.01f, widthPixels * 0.5f);
            var index = buffer.Vertices.Count;
            buffer.Vertices.Add(start);
            buffer.Vertices.Add(end);
            buffer.Vertices.Add(start);
            buffer.Vertices.Add(end);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Uvs.Add(new Vector2(-1.0f, halfWidth));
            buffer.Uvs.Add(new Vector2(-1.0f, halfWidth));
            buffer.Uvs.Add(new Vector2(1.0f, halfWidth));
            buffer.Uvs.Add(new Vector2(1.0f, halfWidth));
            buffer.Normals.Add(tangent);
            buffer.Normals.Add(tangent);
            buffer.Normals.Add(tangent);
            buffer.Normals.Add(tangent);
            buffer.Triangles.Add(index);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 3);
            buffer.Triangles.Add(index + 2);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index);
            buffer.Triangles.Add(index + 3);
            buffer.Triangles.Add(index + 1);
            buffer.Triangles.Add(index + 2);
        }

        private bool GetNoteScreenSegment(
            ChartNote note,
            double nowMs,
            out Vector2 start,
            out Vector2 end)
        {
            start = Vector2.zero;
            end = Vector2.zero;
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var local = NoteLocalPosition(note, nowMs);
            var world = _master.TransformPoint(local);
            var startProjected = camera.WorldToScreenPoint(world);
            start = startProjected;
            end = startProjected;
            if (startProjected.z <= 0.0f)
            {
                return false;
            }
            if (note.Kind == NoteKind.Hold ||
                note.Kind == NoteKind.SkyArea)
            {
                local.z = _lineLocal.z +
                          (float)_motion.NoteZAt(
                              note.EndTimeMs,
                              nowMs);
                var endProjected = camera.WorldToScreenPoint(
                    _master.TransformPoint(local));
                end = endProjected;
            }
            return true;
        }

        private bool IsNoteNearView(ChartNote note, double nowMs)
        {
            if (note.Kind == NoteKind.Hold ||
                note.Kind == NoteKind.SkyArea)
            {
                var headZ = _motion.NoteZ(note, nowMs);
                var tailZ = _motion.NoteZAt(note.EndTimeMs, nowMs);
                var nearZ = Math.Min(headZ, tailZ);
                var farZ = Math.Max(headZ, tailZ);
                return farZ >= 0.0 &&
                       nearZ <= _motion.TrackLength;
            }
            var z = _motion.NoteZ(note, nowMs);
            return z >= -_motion.TrackLength &&
                   z <= _motion.TrackLength;
        }

        private bool IsNoteSelectionVisible(
            ChartNote note,
            double nowMs)
        {
            if (note == null)
            {
                return false;
            }
            if (note.Kind == NoteKind.SkyArea)
            {
                return IsNoteNearView(note, nowMs);
            }
            return IsNoteNearView(note, nowMs) &&
                   _noteRenderer != null &&
                   _noteRenderer.IsNoteVisualActive(note);
        }

        private float NoteWidth(ChartNote note)
        {
            if (note.Kind == NoteKind.Flick)
            {
                return Mathf.Max(
                    0.03f,
                    TrackX((float)note.StartWidth) -
                    TrackX(0.0f));
            }
            if (note.Kind == NoteKind.SkyArea)
            {
                return Mathf.Max(
                    0.03f,
                    TrackX((float)note.StartWidth) -
                    TrackX(0.0f));
            }
            var start = LaneCenterX(
                note.StartLane,
                note.WidthLanes);
            var end = LaneCenterX(
                note.StartLane,
                Mathf.Max(1, note.WidthLanes));
            return Mathf.Max(0.12f, Mathf.Abs(end - start) + 0.24f);
        }

        private float LaneCenterX(ChartNote note)
        {
            return LaneCenterX(note.StartLane, note.WidthLanes);
        }

        private float LaneCenterX(int lane, int width)
        {
            var start = Mathf.Clamp(lane, 0, 5);
            var end = Mathf.Clamp(
                lane + Mathf.Max(1, width) - 1,
                0,
                5);
            return (_laneX[start] + _laneX[end]) * 0.5f;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            var segment = end - start;
            var length = segment.sqrMagnitude;
            if (length < 0.0001f)
            {
                return Vector2.Distance(point, start);
            }
            var t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / length);
            return Vector2.Distance(
                point,
                start + segment * t);
        }

        private static float TrackX(float position)
        {
            return Mathf.Lerp(-TrackHalfWidth, TrackHalfWidth, position);
        }

        private bool IsNoteKindVisible(NoteKind kind)
        {
            return kind == NoteKind.Tap ||
                   kind == NoteKind.Hold
                ? (_visibleLayers & FalsumChartLayer.Ground) != 0
                : (_visibleLayers & FalsumChartLayer.Sky) != 0;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
