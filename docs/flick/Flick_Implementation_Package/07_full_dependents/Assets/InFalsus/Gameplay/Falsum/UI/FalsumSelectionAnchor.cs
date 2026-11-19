using UnityEngine;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// The direct-manipulation roles a selection-box anchor can play.
    ///
    /// Every role maps onto one concrete editor operation. The mapping is
    /// derived from (Kind, Edge, Tail, note.Kind) by the editor's anchor layer;
    /// the drawing layer only needs Kind, Edge and the sizing fields.
    /// </summary>
    public enum FalsumAnchorKind
    {
        /// <summary>Left/right box edge: adjusts that side's lateral boundary.</summary>
        Boundary,
        /// <summary>Skyarea easing control: CLICK cycles the three states.</summary>
        Easing,
        /// <summary>Front/back edge midpoint: adjusts start/end time (and, for
        /// skyarea, may also shift that endpoint's lateral position).</summary>
        StartEnd,
        /// <summary>Below the front edge: translates the whole note laterally.</summary>
        Move,
        /// <summary>Above the front edge: CLICK toggles flick direction.</summary>
        Color,
    }

    /// <summary>Which side of the drawn box an anchor belongs to.</summary>
    public enum FalsumAnchorEdge
    {
        None,
        Left,
        Right,
        Front,
        Back,
    }

    /// <summary>How the anchor was placed on the box, for hit testing and drawing.</summary>
    public enum FalsumAnchorPlacement
    {
        /// <summary>One of the four corners of the drawn box.</summary>
        Corner,
        /// <summary>Midpoint of a left/right edge.</summary>
        LateralEdgeMidpoint,
        /// <summary>Midpoint of the front/back edge.</summary>
        TimeEdgeMidpoint,
        /// <summary>Offset outside the box (easing, move handle, colour control).</summary>
        Offset,
    }

    /// <summary>
    /// One drawn anchor. Everything that is drawn is also hit-tested:
    /// <see cref="HitCenter"/> is the grab point, <see cref="HitRadius"/> the
    /// grab radius, and both are in canvas-local units.
    ///
    /// INVARIANT: <see cref="CanvasPosition"/> is where the glyph is painted and
    /// <see cref="HitCenter"/> is where the pointer grabs it. They are usually
    /// equal, but the hit centre may be nudged so two handles that visually
    /// overlap (a corner and the easing control on a narrow box) stay
    /// independently grabbable.
    /// </summary>
    public sealed class FalsumSelectionAnchor
    {
        public FalsumAnchorKind Kind;
        public FalsumAnchorEdge Edge;
        public FalsumAnchorPlacement Placement;

        /// <summary>True when this anchor belongs to the note's end (tail).</summary>
        public bool Tail;

        /// <summary>Where the glyph is drawn (canvas-local).</summary>
        public Vector2 CanvasPosition;

        /// <summary>Where a click grabs it; may be larger than the glyph.</summary>
        public Vector2 HitCenter;

        /// <summary>Grab radius in canvas-local units, floored for usability.</summary>
        public float HitRadius;

        /// <summary>
        /// Radius the glyph is DRAWN at. Separate from <see cref="HitRadius"/>
        /// on purpose: the grab area must stay generous (a mis-click is
        /// expensive) while the drawn handle stays small enough not to bury
        /// the outline it belongs to.
        /// </summary>
        public float GlyphRadius = 4.0f;

        /// <summary>Draw scale, so a tiny box still shows a usable handle.</summary>
        public float GlyphScale = 1.0f;

        /// <summary>Current value for value-carrying anchors (easing, flick dir).</summary>
        public int Value;

        /// <summary>Short icon key resolved by the overlay (<c>arrow</c>, <c>dot</c>...).</summary>
        public string Icon;

        /// <summary>
        /// Hit-test precedence. When two anchors are within a few pixels of the
        /// pointer, the higher priority wins. Click-only controls (colour)
        /// outrank drag handles, and the move handle outranks boundary
        /// corners, because the smaller a control's useful travel the worse a
        /// misfire feels.
        /// </summary>
        public int Priority;

        /// <summary>
        /// Easing snap geometry: the canvas point of the neutral ("Line") state.
        /// Together with <see cref="SnapDirection"/> and <see cref="SnapSpacing"/>
        /// this defines the three positions the easing control can occupy.
        ///
        /// It lives on the anchor rather than being recomputed by the drag code
        /// ON PURPOSE: the builder paints the glyph with exactly this geometry,
        /// and the drag snaps to exactly this geometry, so "the handle moved to
        /// where you see it" is true by construction instead of by agreement
        /// between two formulas that can drift.
        /// </summary>
        public Vector2 SnapCenter;

        /// <summary>Unit canvas direction in which the easing offset grows.</summary>
        public Vector2 SnapDirection = Vector2.right;

        /// <summary>Canvas distance between adjacent easing states.</summary>
        public float SnapSpacing;

        /// <summary>
        /// Signed multiple of <see cref="SnapSpacing"/> along
        /// <see cref="SnapDirection"/> for a given easing value.
        ///
        /// <see cref="SnapDirection"/> points INWARD (toward the note body),
        /// so the SIGNS here mean: Ease out = +1 (inward), Line = 0,
        /// Ease in = -1 (outward). That is the agreed gesture:
        /// dragging inward gives Ease out, dragging outward gives Ease in.
        ///
        /// The value enum is Line=0, Ease out=1, Ease in=2 while the slots run
        /// -1..+1 in the opposite order, so this is NOT the identity mapping.
        /// Use <see cref="EasingSlotToValue"/> for the inverse; do NOT write
        /// <c>slot + 1</c>, and do NOT reuse the raw value as a slot -- both
        /// put the handle (and the drag) on the wrong side.
        /// </summary>
        public static float EasingSlot(int easing)
        {
            if (easing == 1)
            {
                return 1.0f;
            }
            if (easing == 2)
            {
                return -1.0f;
            }
            return 0.0f;
        }

        /// <summary>
        /// The easing value for a slot measured ALONG
        /// <see cref="SnapDirection"/> (inward-positive): +1 is EASE OUT, 0 is
        /// LINE and -1 is EASE IN. Exact inverse of <see cref="EasingSlot"/>.
        /// </summary>
        public static int EasingSlotToValue(int slot)
        {
            if (slot > 0)
            {
                return 1;
            }
            if (slot < 0)
            {
                return 2;
            }
            return 0;
        }

        /// <summary>
        /// The slot (along <see cref="SnapDirection"/>) for an easing value.
        /// Inverse of <see cref="EasingSlotToValue"/>, provided so callers
        /// never hand-copy the mapping.
        /// </summary>
        public static int EasingValueToSlot(int easing)
        {
            return (int)EasingSlot(easing);
        }

        /// <summary>Whether this anchor carries usable easing snap geometry.</summary>
        public bool HasSnapGeometry
        {
            get { return SnapSpacing > 0.5f; }
        }

        /// <summary>
        /// The canvas point an easing anchor occupies for a given value. The
        /// builder uses this to paint, the drag uses it to snap; there is no
        /// second copy of the layout anywhere.
        /// </summary>
        public Vector2 SnapPositionFor(int easing)
        {
            return SnapCenter +
                   SnapDirection * (SnapSpacing * EasingSlot(easing));
        }

        public FalsumSelectionAnchor(
            FalsumAnchorKind kind,
            FalsumAnchorEdge edge,
            FalsumAnchorPlacement placement,
            Vector2 canvasPosition)
        {
            Kind = kind;
            Edge = edge;
            Placement = placement;
            CanvasPosition = canvasPosition;
            HitCenter = canvasPosition;
            HitRadius = 14.0f;
        }
    }
}
