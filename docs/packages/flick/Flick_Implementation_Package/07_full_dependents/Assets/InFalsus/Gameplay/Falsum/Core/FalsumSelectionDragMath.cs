using System;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>
    /// The time arithmetic for dragging a MULTI-selection (invariant A-25).
    ///
    /// <para>WHY THIS IS ITS OWN TYPE: the rule "a group moves by one shared
    /// delta, and the leading note's floor at time 0 clamps the GROUP rather
    /// than each note" is the whole reason multi-select dragging works. It used
    /// to be implicit in <c>FalsumChartEditor.DragSelection</c>, where the only
    /// way to test it was to stand up a live editor in play mode. As a pure
    /// function it is checked directly by
    /// <c>FalsumSelfCheck.CheckSelectionDragMath</c>, and the editor keeps no
    /// second copy of the rule.</para>
    /// </summary>
    public static class FalsumSelectionDragMath
    {
        /// <summary>
        /// The shared time delta for a group drag.
        ///
        /// <para>The delta is derived from the DRAG ANCHOR only: the anchor's
        /// snapped target minus the anchor's own press-time value. Every member
        /// receives that same absolute delta, so the group's internal spacing is
        /// preserved exactly and a member that already sat on a grid line still
        /// moves with the block (a per-note snap left it behind — it kept
        /// its original time because that was the closer grid line).</para>
        ///
        /// <para>The result is clamped so the EARLIEST member never crosses
        /// time 0. Clamping each note separately would squash the group flat
        /// against zero, one note at a time.</para>
        /// </summary>
        /// <param name="anchorOriginalMs">Anchor's time when the press began.</param>
        /// <param name="anchorTargetMs">Snapped target time for the anchor.</param>
        /// <param name="earliestOriginalMs">
        /// Smallest press-time value in the selection; pass
        /// <see cref="double.MaxValue"/> for an empty selection.
        /// </param>
        public static double ResolveDelta(
            double anchorOriginalMs,
            double anchorTargetMs,
            double earliestOriginalMs)
        {
            if (double.IsNaN(anchorOriginalMs) ||
                double.IsNaN(anchorTargetMs) ||
                double.IsInfinity(anchorOriginalMs) ||
                double.IsInfinity(anchorTargetMs))
            {
                return 0.0;
            }
            var delta = anchorTargetMs - anchorOriginalMs;
            if (double.IsNaN(earliestOriginalMs) ||
                double.IsInfinity(earliestOriginalMs))
            {
                return delta;
            }
            // Only the negative direction can cross zero.
            var floor = -Math.Max(0.0, earliestOriginalMs);
            return delta < floor ? floor : delta;
        }

        /// <summary>
        /// True when the drag must use one shared delta instead of snapping each
        /// note against its own original time.
        /// </summary>
        /// <param name="memberCount">Notes being dragged.</param>
        /// <param name="hasGroupSnap">
        /// True when the SkyArea whole-group snap already produced the delta;
        /// that delta is shared by definition.
        /// </param>
        public static bool UsesSharedDelta(
            int memberCount,
            bool hasGroupSnap)
        {
            return hasGroupSnap || memberCount > 1;
        }
    }
}
