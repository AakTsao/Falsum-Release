using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Selection outline plus the direct-manipulation anchors drawn on it.
    ///
    /// Every anchor is painted with the CENTRE of its glyph exactly at
    /// <see cref="FalsumSelectionAnchor.CanvasPosition"/> (the grab point,
    /// <see cref="FalsumSelectionAnchor.HitCenter"/>, may be nudged for
    /// hit-testing but must never move the artwork) and sized from
    /// <see cref="FalsumSelectionAnchor.GlyphRadius"/> only. The plate is
    /// stroked along its own silhouette - a circle gets a round ring, a
    /// diamond a rhombus, a square a square - so a round handle never grows
    /// a boxy rim. The hovered/dragged anchor is brightened and gets an outer
    /// ring following the same silhouette, which is the "armed" cue.
    /// Any anchor that owns two axes (skyarea front/back midpoints and every
    /// move handle) is dragged along ONE axis per drag; the axis is locked by
    /// the editor on the first movement frame.
    /// </summary>
    public sealed class FalsumSelectionOverlayGraphic : MaskableGraphic
    {
        /// <summary>
        /// Floor for the drawn handle radius. A zero/negative
        /// <see cref="FalsumSelectionAnchor.GlyphRadius"/> must not degenerate
        /// into an invisible or backwards-wound plate.
        /// </summary>
        private const float MinimumGlyphRadius = 3.0f;

        private readonly List<Vector2[]> _polygons =
            new List<Vector2[]>();
        private readonly List<FalsumSelectionAnchor> _anchors =
            new List<FalsumSelectionAnchor>();
        /// <summary>Reused silhouette ring, one anchor at a time.</summary>
        private readonly List<Vector2> _platePoints = new List<Vector2>(16);
        private string _debugSummary = "empty";
        private int _hoveredAnchorIndex = -1;
        private bool _draggingAnchor;
        private string _anchorSummary = "count=0 active=-1 kind=none";
        // The anchor summary is rebuilt only when its inputs change, so the
        // per-frame SetSelection call does not allocate an identical string.
        // The revision counter covers a rebuilt anchor list that happens to
        // keep the same count (a different note selected at the same size).
        private int _anchorRevision;
        private int _summaryRevision = -1;
        private int _summaryAnchorCount = -1;
        private int _summaryActiveIndex = int.MinValue;
        private bool _summaryDragging = true;

        public string DebugSummary
        {
            get { return _debugSummary; }
        }

        /// <summary>
        /// Short, deterministic description of the current anchor set, used by
        /// the editor self-check to assert that handles were actually built.
        /// Shape: <c>count=8 active=4 kind=StartEnd</c>; <c>active=-1</c> and
        /// <c>kind=none</c> mean nothing is armed, and a trailing <c>drag</c>
        /// marks a dragged handle. Rebuilt only when its inputs change.
        /// </summary>
        public string DebugAnchorSummary
        {
            get { return _anchorSummary; }
        }

        public int AnchorCount
        {
            get { return _anchors.Count; }
        }

        public FalsumSelectionAnchor AnchorAt(int index)
        {
            return index >= 0 && index < _anchors.Count
                ? _anchors[index]
                : null;
        }

        /// <summary>
        /// Highlights the anchor the pointer is over (or that is being dragged).
        /// Indices refer to the anchor list passed to <see cref="SetSelection"/>.
        /// </summary>
        public void SetAnchorHighlight(int hoveredIndex, bool dragging)
        {
            if (_hoveredAnchorIndex == hoveredIndex &&
                _draggingAnchor == dragging)
            {
                return;
            }
            _hoveredAnchorIndex = hoveredIndex;
            _draggingAnchor = dragging;
            RefreshAnchorSummary();
            SetVerticesDirty();
        }

        /// <summary>
        /// Replaces both the outline polygons and the anchor set in one call so
        /// a frame never renders stale handles over a new outline.
        /// </summary>
        public void SetSelection(
            List<Vector2[]> polygons,
            List<FalsumSelectionAnchor> anchors)
        {
            _polygons.Clear();
            _anchors.Clear();
            if (polygons != null)
            {
                _polygons.AddRange(polygons);
            }
            if (anchors != null)
            {
                _anchors.AddRange(anchors);
            }
            _anchorRevision++;
            _debugSummary = Describe(polygons);
            RefreshAnchorSummary();
            SetVerticesDirty();
        }

        public void SetPolygons(List<Vector2[]> polygons)
        {
            SetSelection(polygons, null);
        }

        private string Describe(List<Vector2[]> polygons)
        {
            if (polygons == null || polygons.Count == 0)
            {
                return "empty";
            }
            if (_polygons.Count == 0 || _polygons[0].Length == 0)
            {
                return "empty";
            }
            var min = _polygons[0][0];
            var max = _polygons[0][0];
            for (var i = 0; i < _polygons[0].Length; i++)
            {
                min = Vector2.Min(min, _polygons[0][i]);
                max = Vector2.Max(max, _polygons[0][i]);
            }
            return min.ToString("F1") + ".." + max.ToString("F1");
        }

        /// <summary>
        /// Builds the compact anchor self-check string:
        /// <c>count=N active=I kind=Kind</c>, where <c>active=-1</c> means
        /// nothing is armed and a trailing <c>drag</c> marks a drag. Cached
        /// against its inputs because <see cref="SetSelection"/> runs every
        /// frame while the selection is stable.
        /// </summary>
        private void RefreshAnchorSummary()
        {
            if (_summaryRevision == _anchorRevision &&
                _summaryAnchorCount == _anchors.Count &&
                _summaryActiveIndex == _hoveredAnchorIndex &&
                _summaryDragging == _draggingAnchor)
            {
                return;
            }
            _summaryRevision = _anchorRevision;
            _summaryAnchorCount = _anchors.Count;
            _summaryActiveIndex = _hoveredAnchorIndex;
            _summaryDragging = _draggingAnchor;

            var active = _hoveredAnchorIndex;
            if (active < 0 || active >= _anchors.Count)
            {
                _anchorSummary =
                    "count=" + _anchors.Count + " active=-1 kind=none";
                return;
            }
            var anchor = _anchors[active];
            _anchorSummary =
                "count=" + _anchors.Count +
                " active=" + active +
                " kind=" + (anchor != null ? anchor.Kind.ToString() : "none") +
                (_draggingAnchor ? " drag" : string.Empty);
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            for (var p = 0; p < _polygons.Count; p++)
            {
                var polygon = _polygons[p];
                if (polygon == null || polygon.Length < 3)
                {
                    continue;
                }
                AddFill(
                    helper,
                    polygon,
                    new Color(1.0f, 0.46f, 0.08f, 0.18f));
                for (var i = 0; i < polygon.Length; i++)
                {
                    var next = (i + 1) % polygon.Length;
                    AddLine(
                        helper,
                        polygon[i],
                        polygon[next],
                        2.4f,
                        new Color(1.0f, 0.55f, 0.12f, 0.98f));
                }
            }
            DrawAnchors(helper);
        }

        /// <summary>
        /// Paints one plate per anchor. The plate centre is
        /// <see cref="FalsumSelectionAnchor.CanvasPosition"/> and its radius
        /// comes from <see cref="FalsumSelectionAnchor.GlyphRadius"/> alone, so
        /// the artwork never drifts away from the operation the handle
        /// performs.
        /// </summary>
        private void DrawAnchors(VertexHelper helper)
        {
            for (var i = 0; i < _anchors.Count; i++)
            {
                var anchor = _anchors[i];
                if (anchor == null)
                {
                    continue;
                }
                var active = i == _hoveredAnchorIndex;
                var center = anchor.CanvasPosition;
                // Drawn at the GLYPH radius, never the hit radius: the grab
                // area stays generous while the visible plate stays small.
                var size = Mathf.Max(MinimumGlyphRadius, anchor.GlyphRadius);
                if (active)
                {
                    // Modest bump only: enough to read as "armed" without
                    // growing over the box edge the handle belongs to.
                    size *= _draggingAnchor ? 1.20f : 1.12f;
                }
                var color = AnchorColor(anchor);
                if (active)
                {
                    color = new Color(color.r, color.g, color.b, 1.0f);
                }
                var ink = InkColor(color);
                var plate = PlateFor(anchor);

                // Soft halo first, so a small plate still stands out against
                // the busy note art behind it.
                AddHalo(
                    helper,
                    center,
                    size * 1.55f,
                    active
                        ? new Color(1.0f, 1.0f, 1.0f, 0.30f)
                        : new Color(color.r, color.g, color.b, 0.20f));

                BuildPlate(_platePoints, plate, center, size);
                AddPlateFill(helper, _platePoints, color);
                // The edge FOLLOWS the silhouette, so a circle keeps a round
                // edge and a diamond a rhombus. There is no axis-aligned box
                // rim, which is what made round handles read as squares.
                AddSilhouette(
                    helper,
                    _platePoints,
                    ink,
                    Mathf.Max(1.0f, size * 0.16f));

                AddRoleGlyph(helper, anchor, center, size, ink);

                if (active)
                {
                    // Armed ring: the same silhouette scaled out, in light
                    // ink, so the selected handle lights up without gaining
                    // a foreign shape.
                    BuildPlate(_platePoints, plate, center, size * 1.18f);
                    AddSilhouette(
                        helper,
                        _platePoints,
                        new Color(
                            1.0f,
                            1.0f,
                            1.0f,
                            _draggingAnchor ? 0.95f : 0.80f),
                        Mathf.Max(1.0f, size * 0.13f));
                }
            }
        }

        /// <summary>Silhouette a role's plate is drawn with.</summary>
        private enum AnchorPlate
        {
            Circle,
            Diamond,
            Square,
            Bar,
        }

        /// <summary>
        /// Distinct silhouette per role, so the five operations stay
        /// distinguishable without reading text: corners are square, lateral
        /// edge grips are bars, start/end is a diamond, and the three
        /// click/offset controls are circles (differentiated by glyph).
        /// </summary>
        private static AnchorPlate PlateFor(FalsumSelectionAnchor anchor)
        {
            switch (anchor.Kind)
            {
                case FalsumAnchorKind.StartEnd:
                    return AnchorPlate.Diamond;
                case FalsumAnchorKind.Move:
                case FalsumAnchorKind.Color:
                case FalsumAnchorKind.Easing:
                    return AnchorPlate.Circle;
                default:
                    return anchor.Placement ==
                           FalsumAnchorPlacement.LateralEdgeMidpoint
                        ? AnchorPlate.Bar
                        : AnchorPlate.Square;
            }
        }

        /// <summary>
        /// Builds the closed silhouette of a plate as absolute canvas points,
        /// walked counter-clockwise. Every plate is convex, so a triangle fan
        /// fills it and consecutive points give the outline to stroke.
        /// </summary>
        private static void BuildPlate(
            List<Vector2> points,
            AnchorPlate plate,
            Vector2 center,
            float size)
        {
            points.Clear();
            switch (plate)
            {
                case AnchorPlate.Circle:
                    for (var i = 0; i < 16; i++)
                    {
                        var angle = (i / 16.0f) * Mathf.PI * 2.0f;
                        points.Add(center + new Vector2(
                            Mathf.Cos(angle),
                            Mathf.Sin(angle)) * size);
                    }
                    return;
                case AnchorPlate.Diamond:
                    points.Add(center + new Vector2(0.0f, -size));
                    points.Add(center + new Vector2(size, 0.0f));
                    points.Add(center + new Vector2(0.0f, size));
                    points.Add(center + new Vector2(-size, 0.0f));
                    return;
                case AnchorPlate.Bar:
                    // A short vertical grip for a lateral-edge midpoint: it
                    // reads as sitting ON the edge it slides along.
                    AddChamferedRect(
                        points,
                        center,
                        size * 0.42f,
                        size,
                        size * 0.20f);
                    return;
                default:
                    AddChamferedRect(
                        points,
                        center,
                        size,
                        size,
                        size * 0.30f);
                    return;
            }
        }

        /// <summary>Chamfered rectangle ring, counter-clockwise.</summary>
        private static void AddChamferedRect(
            List<Vector2> points,
            Vector2 center,
            float halfWidth,
            float halfHeight,
            float chamfer)
        {
            var cut = Mathf.Clamp(
                chamfer,
                0.0f,
                Mathf.Min(halfWidth, halfHeight) * 0.8f);
            points.Add(center + new Vector2(halfWidth, -halfHeight + cut));
            points.Add(center + new Vector2(halfWidth, halfHeight - cut));
            points.Add(center + new Vector2(halfWidth - cut, halfHeight));
            points.Add(center + new Vector2(-halfWidth + cut, halfHeight));
            points.Add(center + new Vector2(-halfWidth, halfHeight - cut));
            points.Add(center + new Vector2(-halfWidth, -halfHeight + cut));
            points.Add(center + new Vector2(-halfWidth + cut, -halfHeight));
            points.Add(center + new Vector2(halfWidth - cut, -halfHeight));
        }

        /// <summary>
        /// Fills a convex silhouette with a triangle fan from its first ring
        /// point (valid for any convex ring, and avoids a re-computed centre).
        /// </summary>
        private static void AddPlateFill(
            VertexHelper helper,
            List<Vector2> points,
            Color color)
        {
            if (points.Count < 3)
            {
                return;
            }
            var index = helper.currentVertCount;
            helper.AddVert(points[0], color, Vector2.zero);
            for (var i = 1; i < points.Count; i++)
            {
                helper.AddVert(points[i], color, Vector2.zero);
            }
            for (var i = 1; i + 1 < points.Count; i++)
            {
                helper.AddTriangle(index, index + i, index + i + 1);
            }
        }

        /// <summary>Strokes the closed silhouette, in its own shape.</summary>
        private static void AddSilhouette(
            VertexHelper helper,
            List<Vector2> points,
            Color color,
            float width)
        {
            for (var i = 0; i < points.Count; i++)
            {
                AddLine(
                    helper,
                    points[i],
                    points[(i + 1) % points.Count],
                    width,
                    color);
            }
        }

        /// <summary>Role glyph painted inside the plate, centred.</summary>
        private static void AddRoleGlyph(
            VertexHelper helper,
            FalsumSelectionAnchor anchor,
            Vector2 center,
            float size,
            Color ink)
        {
            switch (anchor.Kind)
            {
                case FalsumAnchorKind.StartEnd:
                    AddTimeGlyph(helper, center, size, ink);
                    return;
                case FalsumAnchorKind.Move:
                    AddArrowGlyph(helper, center, size, ink);
                    return;
                case FalsumAnchorKind.Color:
                    AddDirectionGlyph(
                        helper, center, size, anchor.Value, ink);
                    return;
                case FalsumAnchorKind.Easing:
                    AddEasingGlyph(
                        helper, center, size, anchor.Value, ink);
                    return;
                default:
                    // Boundary plates carry no glyph: the square corner or
                    // the edge bar is itself the affordance.
                    return;
            }
        }

        /// <summary>Faint disc behind the handle, so it never blends into art.</summary>
        private static void AddHalo(
            VertexHelper helper,
            Vector2 center,
            float radius,
            Color color)
        {
            const int segments = 16;
            var index = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            for (var i = 0; i < segments; i++)
            {
                var angle = (i / (float)segments) * Mathf.PI * 2.0f;
                helper.AddVert(
                    center + new Vector2(
                        Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    new Color(color.r, color.g, color.b, 0.0f),
                    Vector2.zero);
            }
            for (var i = 0; i < segments; i++)
            {
                helper.AddTriangle(
                    index,
                    index + 1 + i,
                    index + 1 + (i + 1) % segments);
            }
        }

        private static Color AnchorColor(FalsumSelectionAnchor anchor)
        {
            switch (anchor.Kind)
            {
                case FalsumAnchorKind.Easing:
                    return new Color(0.55f, 0.85f, 1.0f, 0.92f);
                case FalsumAnchorKind.Move:
                    return new Color(1.0f, 0.82f, 0.18f, 0.95f);
                case FalsumAnchorKind.Color:
                    return anchor.Value == 16
                        ? new Color(1.0f, 0.82f, 0.18f, 0.95f)
                        : new Color(0.20f, 0.95f, 0.55f, 0.95f);
                case FalsumAnchorKind.StartEnd:
                    return new Color(0.75f, 0.65f, 1.0f, 0.95f);
                default:
                    return new Color(1.0f, 0.62f, 0.16f, 0.95f);
            }
        }

        /// <summary>A four-point arrow hint for the move handle.</summary>
        private static void AddArrowGlyph(
            VertexHelper helper,
            Vector2 center,
            float size,
            Color ink)
        {
            var arm = size * 0.45f;
            var head = size * 0.22f;
            AddArrowHead(helper, center + Vector2.right * arm, head, Vector2.right, ink);
            AddArrowHead(helper, center + Vector2.left * arm, head, Vector2.left, ink);
            AddArrowHead(helper, center + Vector2.up * arm, head, Vector2.up, ink);
            AddArrowHead(helper, center + Vector2.down * arm, head, Vector2.down, ink);
        }

        private static void AddArrowHead(
            VertexHelper helper,
            Vector2 tip,
            float size,
            Vector2 direction,
            Color color)
        {
            var side = new Vector2(-direction.y, direction.x) * size;
            AddTriangle(
                helper,
                tip,
                tip - direction * size + side,
                tip - direction * size - side,
                color);
        }

        /// <summary>
        /// A bar whose ORIENTATION shows the flick direction: a horizontal bar
        /// (green, right) versus a vertical bar (yellow, left). Sized to stay
        /// inside the round plate.
        /// </summary>
        private static void AddDirectionGlyph(
            VertexHelper helper,
            Vector2 center,
            float size,
            int flickDirection,
            Color ink)
        {
            var bar = size * 0.42f;
            var thick = size * 0.13f;
            if (flickDirection == 16)
            {
                AddQuad(
                    helper,
                    center + new Vector2(-bar, thick),
                    center + new Vector2(bar, thick),
                    center + new Vector2(bar, -thick),
                    center + new Vector2(-bar, -thick),
                    ink);
                return;
            }
            AddQuad(
                helper,
                center + new Vector2(-thick, -bar),
                center + new Vector2(thick, -bar),
                center + new Vector2(thick, bar),
                center + new Vector2(-thick, bar),
                ink);
        }

        /// <summary>
        /// Easing glyph, three distinct shapes so the state reads without
        /// relying on colour: a bar (Line), a bar with an inward tick (Ease
        /// out) and a bar with an outward tick (Ease in). All of the ink is
        /// kept inside the round plate so nothing pokes through its edge.
        /// </summary>
        private static void AddEasingGlyph(
            VertexHelper helper,
            Vector2 center,
            float size,
            int easing,
            Color ink)
        {
            var t = size * 0.20f;
            var h = size * 0.40f;
            AddQuad(
                helper,
                center + new Vector2(-h, t),
                center + new Vector2(h, t),
                center + new Vector2(h, -t),
                center + new Vector2(-h, -t),
                ink);
            if (easing == 0)
            {
                return;
            }
            // A short tick beside the bar marks the non-linear states.
            var tick = size * 0.20f;
            var x = easing == 1 ? -h - tick * 0.55f : h + tick * 0.55f;
            var tickHeight = size * 0.34f;
            AddQuad(
                helper,
                new Vector2(center.x + x, center.y - tickHeight),
                new Vector2(center.x + x + tick, center.y - tickHeight),
                new Vector2(center.x + x + tick, center.y + tickHeight),
                new Vector2(center.x + x, center.y + tickHeight),
                ink);
        }

        /// <summary>
        /// A stacked double bar implying the start/end time edge. Kept within
        /// the diamond's inscribed area so it never breaks the silhouette.
        /// </summary>
        private static void AddTimeGlyph(
            VertexHelper helper,
            Vector2 center,
            float size,
            Color ink)
        {
            var bar = size * 0.38f;
            var thick = size * 0.11f;
            var gap = size * 0.19f;
            AddQuad(
                helper,
                center + new Vector2(-bar, gap + thick),
                center + new Vector2(bar, gap + thick),
                center + new Vector2(bar, gap - thick),
                center + new Vector2(-bar, gap - thick),
                ink);
            AddQuad(
                helper,
                center + new Vector2(-bar, -gap + thick),
                center + new Vector2(bar, -gap + thick),
                center + new Vector2(bar, -gap - thick),
                center + new Vector2(-bar, -gap - thick),
                ink);
        }

        private static Color InkColor(Color background)
        {
            // Dark ink on a bright plate keeps the glyph legible at any zoom.
            return new Color(0.05f, 0.06f, 0.09f, 0.92f);
        }

        private static void AddQuad(
            VertexHelper helper,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            Color color)
        {
            var index = helper.currentVertCount;
            helper.AddVert(a, color, Vector2.zero);
            helper.AddVert(b, color, Vector2.zero);
            helper.AddVert(c, color, Vector2.zero);
            helper.AddVert(d, color, Vector2.zero);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index, index + 2, index + 3);
        }

        private static void AddTriangle(
            VertexHelper helper,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Color color)
        {
            var index = helper.currentVertCount;
            helper.AddVert(a, color, Vector2.zero);
            helper.AddVert(b, color, Vector2.zero);
            helper.AddVert(c, color, Vector2.zero);
            helper.AddTriangle(index, index + 1, index + 2);
        }

        private static void AddFill(
            VertexHelper helper,
            Vector2[] polygon,
            Color color)
        {
            List<Vector2> points;
            List<int> triangles;
            if (!TryTriangulate(polygon, out points, out triangles))
            {
                AddCenterFan(helper, polygon, color);
                return;
            }

            var start = helper.currentVertCount;
            for (var i = 0; i < points.Count; i++)
            {
                helper.AddVert(points[i], color, Vector2.zero);
            }
            for (var i = 0; i < triangles.Count; i += 3)
            {
                helper.AddTriangle(
                    start + triangles[i],
                    start + triangles[i + 1],
                    start + triangles[i + 2]);
            }
        }

        private static bool TryTriangulate(
            Vector2[] source,
            out List<Vector2> points,
            out List<int> triangles)
        {
            points = new List<Vector2>(source.Length);
            triangles = new List<int>();
            for (var i = 0; i < source.Length; i++)
            {
                var point = source[i];
                if (points.Count == 0 ||
                    (points[points.Count - 1] - point).sqrMagnitude >
                    0.0001f)
                {
                    points.Add(point);
                }
            }
            if (points.Count > 2 &&
                (points[0] - points[points.Count - 1]).sqrMagnitude <=
                0.0001f)
            {
                points.RemoveAt(points.Count - 1);
            }
            if (points.Count < 3)
            {
                return false;
            }

            var area = SignedArea(points);
            if (Mathf.Abs(area) < 0.01f)
            {
                return false;
            }
            if (area < 0.0f)
            {
                points.Reverse();
            }

            var remaining = new List<int>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                remaining.Add(i);
            }
            while (remaining.Count > 3)
            {
                var earFound = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var previous = remaining[
                        (i + remaining.Count - 1) % remaining.Count];
                    var current = remaining[i];
                    var next = remaining[(i + 1) % remaining.Count];
                    var a = points[previous];
                    var b = points[current];
                    var c = points[next];
                    if (Cross(b - a, c - b) <= 0.0001f)
                    {
                        continue;
                    }
                    var containsPoint = false;
                    for (var j = 0; j < remaining.Count; j++)
                    {
                        var candidate = remaining[j];
                        if (candidate == previous ||
                            candidate == current ||
                            candidate == next)
                        {
                            continue;
                        }
                        if (PointInTriangle(
                                points[candidate],
                                a,
                                b,
                                c))
                        {
                            containsPoint = true;
                            break;
                        }
                    }
                    if (containsPoint)
                    {
                        continue;
                    }

                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound)
                {
                    return false;
                }
            }
            triangles.Add(remaining[0]);
            triangles.Add(remaining[1]);
            triangles.Add(remaining[2]);
            return true;
        }

        private static void AddCenterFan(
            VertexHelper helper,
            Vector2[] polygon,
            Color color)
        {
            var center = Vector2.zero;
            for (var i = 0; i < polygon.Length; i++)
            {
                center += polygon[i];
            }
            center /= polygon.Length;
            var centerIndex = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            for (var i = 0; i < polygon.Length; i++)
            {
                helper.AddVert(polygon[i], color, Vector2.zero);
            }
            for (var i = 0; i < polygon.Length; i++)
            {
                var next = (i + 1) % polygon.Length;
                helper.AddTriangle(
                    centerIndex,
                    centerIndex + 1 + i,
                    centerIndex + 1 + next);
            }
        }

        private static float SignedArea(IList<Vector2> points)
        {
            var area = 0.0f;
            for (var i = 0; i < points.Count; i++)
            {
                var next = (i + 1) % points.Count;
                area += points[i].x * points[next].y -
                        points[next].x * points[i].y;
            }
            return area * 0.5f;
        }

        private static bool PointInTriangle(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c)
        {
            var ab = Cross(b - a, point - a);
            var bc = Cross(c - b, point - b);
            var ca = Cross(a - c, point - c);
            var hasNegative = ab < -0.0001f ||
                              bc < -0.0001f ||
                              ca < -0.0001f;
            var hasPositive = ab > 0.0001f ||
                              bc > 0.0001f ||
                              ca > 0.0001f;
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static void AddLine(
            VertexHelper helper,
            Vector2 a,
            Vector2 b,
            float width,
            Color color)
        {
            var direction = (b - a).normalized;
            var normal = new Vector2(-direction.y, direction.x) *
                         (width * 0.5f);
            var index = helper.currentVertCount;
            helper.AddVert(a - normal, color, Vector2.zero);
            helper.AddVert(a + normal, color, Vector2.zero);
            helper.AddVert(b - normal, color, Vector2.zero);
            helper.AddVert(b + normal, color, Vector2.zero);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 1, index + 3);
        }
    }
}
