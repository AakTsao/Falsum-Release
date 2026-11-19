using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>
    /// Placement guard that keeps two TAPs from stacking on the same lane.
    /// A TAP may not be placed when an existing TAP sits within
    /// <see cref="MinGapMs"/> of the target time on a lane span that
    /// intersects the new note's span.
    /// </summary>
    public static class FalsumTapPlacementRule
    {
        /// <summary>
        /// Maximum allowed chart-time distance to a neighbouring TAP on the
        /// same lanes. The boundary value itself is rejected: with the window
        /// being closed, a 9 ms difference already counts as a stack.
        /// </summary>
        public const double MinGapMs = 9.0;

        /// <summary>
        /// Floating point guard for the closed boundary so that a
        /// mathematically exact 9 ms offset is always rejected.
        /// </summary>
        private const double TimeEpsilon = 0.000000001;

        /// <summary>
        /// Returns true when placing a TAP at <paramref name="timeMs"/> over
        /// [<paramref name="startLane"/>,
        /// <paramref name="startLane"/> + <paramref name="widthLanes"/>) would
        /// stack on an existing TAP.
        /// </summary>
        public static bool IsBlocked(
            IList<ChartNote> notes,
            double timeMs,
            int startLane,
            int widthLanes)
        {
            if (notes == null)
            {
                return false;
            }
            var width = widthLanes < 1 ? 1 : widthLanes;
            var laneEnd = startLane + width;
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note == null || note.Kind != NoteKind.Tap)
                {
                    continue;
                }
                if (Math.Abs(note.TimeMs - timeMs) >
                    MinGapMs + TimeEpsilon)
                {
                    continue;
                }
                var otherWidth = note.WidthLanes < 1 ? 1 : note.WidthLanes;
                var otherLaneEnd = note.StartLane + otherWidth;
                if (startLane < otherLaneEnd && note.StartLane < laneEnd)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Finds the nearest blocked TAP, reporting the lane-overlapping
        /// neighbour with the smallest time distance. Returns false when the
        /// placement is allowed.
        /// </summary>
        public static bool TryFindBlocker(
            IList<ChartNote> notes,
            double timeMs,
            int startLane,
            int widthLanes,
            out ChartNote blocker,
            out double gapMs)
        {
            blocker = null;
            gapMs = 0.0;
            if (notes == null)
            {
                return false;
            }
            var width = widthLanes < 1 ? 1 : widthLanes;
            var laneEnd = startLane + width;
            var blocked = false;
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note == null || note.Kind != NoteKind.Tap)
                {
                    continue;
                }
                var gap = Math.Abs(note.TimeMs - timeMs);
                if (gap > MinGapMs + TimeEpsilon)
                {
                    continue;
                }
                var otherWidth = note.WidthLanes < 1 ? 1 : note.WidthLanes;
                var otherLaneEnd = note.StartLane + otherWidth;
                if (startLane >= otherLaneEnd || note.StartLane >= laneEnd)
                {
                    continue;
                }
                if (!blocked || gap < gapMs)
                {
                    blocked = true;
                    blocker = note;
                    gapMs = gap;
                }
            }
            return blocked;
        }
    }
}
