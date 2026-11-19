using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>
    /// A point-in-time capture of every SkyArea note in one selection, plus
    /// the lattice every scale operation is applied against.
    ///
    /// <para>WHY A SNAPSHOT: dragging must always write absolute values from
    /// the values captured at press time. Multiplying the live note values
    /// frame by frame accumulates rounding drift and the group slowly deforms
    /// (the same reason the middle-mouse edit bodies keep
    /// <c>MiddleEditTarget</c> press snapshots).</para>
    /// </summary>
    public sealed class FalsumSkyAreaGroupSnapshot
    {
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<double> StartPos = new List<double>();
        public readonly List<double> EndPos = new List<double>();
        public readonly List<double> StartWidth = new List<double>();
        public readonly List<double> EndWidth = new List<double>();
        public readonly List<double> TimeMs = new List<double>();
        public readonly List<double> DurationMs = new List<double>();
        public readonly List<int> Grid = new List<int>();

        /// <summary>Lateral origin of the group (the earliest segment's center).</summary>
        public double PivotPos;

        /// <summary>Temporal origin of the group (the earliest segment's start).</summary>
        public double AnchorTimeMs;

        /// <summary>Quantisation lattice for lateral writes, 1..200.</summary>
        public int GroupGrid = 1;

        public int Count
        {
            get { return Notes.Count; }
        }
    }

    /// <summary>
    /// The ONE implementation of "scale a whole SkyArea group".
    ///
    /// <para>Everything here is pure <c>System.Math</c> on purpose: no
    /// <c>UnityEngine</c> and no editor state. The anchor layer is only
    /// allowed to delegate to this file (the invariant that keeps two copies
    /// of clamp/quantise math from drifting apart — every past regression in
    /// the selection-handle work came from a second implementation).</para>
    ///
    /// <para>Semantics, fixed by design:</para>
    /// <list type="bullet">
    /// <item><b>Width</b> scales the group's LATERAL FOOTPRINT around
    /// <see cref="FalsumSkyAreaGroupSnapshot.PivotPos"/>; each segment keeps
    /// its own thickness, so <c>StartWidth</c>/<c>EndWidth</c> are restored
    /// verbatim.</item>
    /// <item><b>Length</b> scales every time boundary around
    /// <see cref="FalsumSkyAreaGroupSnapshot.AnchorTimeMs"/>, rounded once on
    /// a shared boundary table so adjacent segments stay exactly glued (the
    /// chain must survive <c>FalsumSkyAreaGrouping.Normalize</c>).</item>
    /// </list>
    /// </summary>
    public static class FalsumSkyAreaGroupScale
    {
        /// <summary>
        /// Whether the SKYAREA easing handle on <paramref name="leftSide"/> has
        /// its EASE IN and EASE OUT detents traded, judged ONLY by that side's
        /// own geometry.
        ///
        /// <para>This swaps IN for OUT; it never swaps the left handle with the
        /// right one. The handle stays on its own edge -- what changes is which
        /// of the two outer detents means "ease in" and which means "ease out",
        /// because a body that expands outward past its start point reads the
        /// curtain as running the other way.</para>
        ///
        /// <para>The two sides are independent: neither waits for the other to
        /// also match, so a note that expands on only one edge trades only that
        /// edge's detents.</para>
        ///
        /// <para>Lives here, not on the editor, because the anchor BUILDER
        /// (FalsumEditorWorld) and the drag/apply bodies (FalsumChartEditor)
        /// must agree exactly -- if the two mappings drift apart, a handle
        /// paints one curve and edits the other.</para>
        /// </summary>
        public static bool EasingInOutIsSwapped(
            ChartNote note,
            bool leftSide)
        {
            if (note == null || note.Kind != NoteKind.SkyArea)
            {
                return false;
            }
            var startEdge = leftSide
                ? note.StartPos - note.StartWidth * 0.5
                : note.StartPos + note.StartWidth * 0.5;
            var endEdge = leftSide
                ? note.EndPos - note.EndWidth * 0.5
                : note.EndPos + note.EndWidth * 0.5;
            return leftSide
                ? endEdge < startEdge
                : endEdge > startEdge;
        }

        /// <summary>Ease in (2) and Ease out (1) traded; Line (0) is fixed.</summary>
        public static int SwapEasingInOut(int easing)
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

        /// <summary>
        /// The value the handle on <paramref name="leftSide"/> serves a storage
        /// value from, after the in/out rule. The handle still owns the same
        /// note field; only the detent meaning is inverted.
        /// </summary>
        public static int EasingToHandleValue(
            ChartNote note,
            bool leftSide,
            int stored)
        {
            return EasingInOutIsSwapped(note, leftSide)
                ? SwapEasingInOut(stored)
                : stored;
        }

        /// <summary>
        /// Inverse of <see cref="EasingToHandleValue"/>: the value to persist
        /// when the handle on <paramref name="leftSide"/> lands on
        /// <paramref name="handleValue"/>. Same swap, so the mapping is its own
        /// inverse.
        /// </summary>
        public static int HandleValueToStored(
            ChartNote note,
            bool leftSide,
            int handleValue)
        {
            return EasingInOutIsSwapped(note, leftSide)
                ? SwapEasingInOut(handleValue)
                : handleValue;
        }

        /// <summary>Hard ceiling from <see cref="FalsumFraction.MaxDenominator"/>.</summary>
        public const int MaxGrid = FalsumFraction.MaxDenominator;

        public const double MinimumTimelineScale = 0.1;
        public const double MaximumTimelineScale = 10.0;
        public const double MinimumWidthScale = 0.02;
        public const double MaximumWidthScale = 50.0;

        private const double MinimumSegmentDurationMs = 1.0;
        private const double PositionEpsilon = 0.000000001;

        /// <summary>
        /// Captures the press-time state of every SkyArea in the list.
        /// Returns null when the list holds no usable SkyArea note.
        /// </summary>
        public static FalsumSkyAreaGroupSnapshot Capture(
            IList<ChartNote> skyNotes)
        {
            if (skyNotes == null)
            {
                return null;
            }

            var usable = new List<ChartNote>();
            for (var i = 0; i < skyNotes.Count; i++)
            {
                var note = skyNotes[i];
                if (note != null && note.Kind == NoteKind.SkyArea)
                {
                    usable.Add(note);
                }
            }
            if (usable.Count == 0)
            {
                return null;
            }

            usable.Sort(CompareByTimeThenId);

            var snapshot = new FalsumSkyAreaGroupSnapshot();
            var first = usable[0];
            snapshot.PivotPos = first.StartPos;
            snapshot.AnchorTimeMs = first.TimeMs;

            var groupGrid = 1;
            for (var i = 0; i < usable.Count; i++)
            {
                var note = usable[i];
                var grid = LeastCommonMultiple(
                    EndpointGrid(note, false),
                    EndpointGrid(note, true));
                snapshot.Notes.Add(note);
                snapshot.StartPos.Add(note.StartPos);
                snapshot.EndPos.Add(note.EndPos);
                snapshot.StartWidth.Add(note.StartWidth);
                snapshot.EndWidth.Add(note.EndWidth);
                snapshot.TimeMs.Add(note.TimeMs);
                snapshot.DurationMs.Add(note.DurationMs);
                snapshot.Grid.Add(grid);
                groupGrid = LeastCommonMultiple(groupGrid, grid);
            }
            snapshot.GroupGrid = ClampGrid(groupGrid);
            return snapshot;
        }

        /// <summary>
        /// The closed interval of lateral scales that keeps every endpoint
        /// inside <c>[0, 1]</c>. Returns false when no such scale exists (or
        /// when an endpoint sits on the pivot, so its position cannot move).
        /// </summary>
        public static bool WidthScaleRange(
            FalsumSkyAreaGroupSnapshot snapshot,
            out double minimum,
            out double maximum)
        {
            minimum = 0.0;
            maximum = double.MaxValue;
            if (snapshot == null || snapshot.Count == 0)
            {
                return false;
            }

            var pivot = snapshot.PivotPos;
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (!AccumulateWidthLimits(
                        snapshot.StartPos[i],
                        snapshot.StartWidth[i],
                        pivot,
                        ref minimum,
                        ref maximum))
                {
                    minimum = 0.0;
                    maximum = 0.0;
                    return false;
                }
                if (!AccumulateWidthLimits(
                        snapshot.EndPos[i],
                        snapshot.EndWidth[i],
                        pivot,
                        ref minimum,
                        ref maximum))
                {
                    minimum = 0.0;
                    maximum = 0.0;
                    return false;
                }
            }

            if (minimum > maximum + PositionEpsilon)
            {
                minimum = 0.0;
                maximum = 0.0;
                return false;
            }
            // A legal range that does not contain the identity scale would
            // make a drag SNAP the group into bounds on the first pixel. Out
            // of bounds data is already invalid; refuse instead of jumping.
            if (minimum > 1.0 + PositionEpsilon ||
                maximum < 1.0 - PositionEpsilon)
            {
                minimum = 0.0;
                maximum = 0.0;
                return false;
            }
            minimum = ClampWidthScale(minimum);
            maximum = ClampWidthScale(maximum);
            return true;
        }

        /// <summary>
        /// Scales the lateral footprint of the whole group. Segment
        /// thicknesses are written back unchanged by design.
        /// </summary>
        public static bool TryApplyWidthScale(
            FalsumSkyAreaGroupSnapshot snapshot,
            double scale,
            bool clampToBounds)
        {
            if (snapshot == null ||
                snapshot.Count == 0 ||
                IsInvalid(scale))
            {
                return false;
            }

            if (clampToBounds)
            {
                double minimum;
                double maximum;
                if (WidthScaleRange(snapshot, out minimum, out maximum))
                {
                    scale = Math.Max(minimum, Math.Min(maximum, scale));
                }
                else
                {
                    // No legal scale exists: leave the chart untouched rather
                    // than write an out-of-bounds value the writer would reject.
                    scale = 1.0;
                }
            }
            scale = ClampWidthScale(scale);

            // A unit scale must be a strict NO-OP: quantising 1.0 would still
            // rewrite every coordinate and can move a value by one ULP,
            // which shows up as "placing a preset at 1.00 deformed it".
            if (Math.Abs(scale - 1.0) <= PositionEpsilon)
            {
                return true;
            }

            var grid = snapshot.GroupGrid > 0
                ? snapshot.GroupGrid
                : 1;
            var pivot = snapshot.PivotPos;
            for (var i = 0; i < snapshot.Count; i++)
            {
                var note = snapshot.Notes[i];
                note.StartPos = Quantize(
                    pivot + (snapshot.StartPos[i] - pivot) * scale,
                    grid);
                note.EndPos = Quantize(
                    pivot + (snapshot.EndPos[i] - pivot) * scale,
                    grid);
                note.StartWidth = snapshot.StartWidth[i];
                note.EndWidth = snapshot.EndWidth[i];
                // Keep the declared denominator at least as fine as the
                // lattice we quantised on, otherwise ChartWriter re-quantises
                // with the note's own (coarser) cell count and the 1/200
                // precision is silently lost.
                var cellCount = Math.Max(grid, 1);
                note.StartCellCount = Math.Max(
                    note.StartCellCount,
                    cellCount);
                note.EndCellCount = Math.Max(
                    note.EndCellCount,
                    cellCount);
            }
            return true;
        }

        /// <summary>
        /// Scales the temporal extent of the whole group around
        /// <see cref="FalsumSkyAreaGroupSnapshot.AnchorTimeMs"/>.
        /// Boundaries are rounded ONCE on a shared table so adjacent
        /// segments stay exactly glued.
        /// </summary>
        public static bool ApplyTimelineScale(
            FalsumSkyAreaGroupSnapshot snapshot,
            double scale)
        {
            return ApplyTimelineScale(
                snapshot,
                snapshot != null
                    ? snapshot.AnchorTimeMs
                    : 0.0,
                scale);
        }

        /// <summary>
        /// Scales the group's temporal extent around an explicit
        /// <paramref name="pivotTimeMs"/> instead of the snapshot's first
        /// segment. The anchor drag passes the OPPOSITE end from the handle
        /// being dragged, so grabbing the front handle pins the group's last
        /// time and grabbing the back handle pins its first -- which is what
        /// "adjust the start" / "adjust the end" has to mean for a chain.
        /// </summary>
        public static bool ApplyTimelineScale(
            FalsumSkyAreaGroupSnapshot snapshot,
            double pivotTimeMs,
            double scale)
        {
            if (snapshot == null ||
                snapshot.Count == 0 ||
                IsInvalid(scale) ||
                IsInvalid(pivotTimeMs))
            {
                return false;
            }
            scale = Math.Max(
                MinimumTimelineScale,
                Math.Min(MaximumTimelineScale, scale));

            var boundaries = new List<double>(snapshot.Count * 2);
            for (var i = 0; i < snapshot.Count; i++)
            {
                boundaries.Add(snapshot.TimeMs[i]);
                boundaries.Add(
                    snapshot.TimeMs[i] + snapshot.DurationMs[i]);
            }
            boundaries.Sort();
            boundaries = Deduplicate(boundaries);

            // The pinned end is the pivot: boundaries grow away from it and it
            // is the only point that maps to itself.
            var anchor = pivotTimeMs;
            var scaled = new double[boundaries.Count];
            for (var i = 0; i < boundaries.Count; i++)
            {
                scaled[i] = IntegerTime(
                    anchor + (boundaries[i] - anchor) * scale);
            }
            // Monotonicity guard: a heavy downward scale on a very long group
            // must not fold later boundaries back over earlier ones.
            for (var i = 1; i < scaled.Length; i++)
            {
                if (scaled[i] < scaled[i - 1])
                {
                    scaled[i] = scaled[i - 1];
                }
            }

            for (var i = 0; i < snapshot.Count; i++)
            {
                var note = snapshot.Notes[i];
                var startIndex = BoundaryIndex(
                    boundaries,
                    snapshot.TimeMs[i]);
                var endIndex = BoundaryIndex(
                    boundaries,
                    snapshot.TimeMs[i] + snapshot.DurationMs[i]);
                var start = scaled[startIndex];
                var end = scaled[endIndex];
                note.TimeMs = start;
                note.DurationMs = Math.Max(
                    MinimumSegmentDurationMs,
                    end - start);
            }
            return true;
        }

        /// <summary>
        /// Finds the shared boundary-table slot a note value belongs to.
        /// The table is built from the very same note values, so this is the
        /// identity index; the epsilon walk only guards float formatting
        /// noise and never merges genuinely distinct boundaries (they differ
        /// by whole milliseconds or more).
        /// </summary>
        private static int BoundaryIndex(
            List<double> boundaries,
            double value)
        {
            for (var i = 0; i < boundaries.Count; i++)
            {
                if (Math.Abs(boundaries[i] - value) <= PositionEpsilon)
                {
                    return i;
                }
            }
            return boundaries.Count - 1;
        }

        /// <summary>
        /// Puts every segment back to its press-time values. The group drag
        /// writes notes directly (the middle-edit bodies are not involved), so
        /// the snapshot is the only record of "before"; cancelling has to
        /// restore it. Kept here, next to the writes it undoes, so the anchor
        /// layer never assigns note fields itself.
        /// </summary>
        public static void Restore(FalsumSkyAreaGroupSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            for (var i = 0; i < snapshot.Count; i++)
            {
                var note = snapshot.Notes[i];
                note.TimeMs = snapshot.TimeMs[i];
                note.DurationMs = snapshot.DurationMs[i];
                note.StartPos = snapshot.StartPos[i];
                note.EndPos = snapshot.EndPos[i];
                note.StartWidth = snapshot.StartWidth[i];
                note.EndWidth = snapshot.EndWidth[i];
            }
        }

        /// <summary>Lateral footprint of the snapshot, as a percentage of the track.</summary>
        public static double LateralSpan(
            FalsumSkyAreaGroupSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                return 0.0;
            }
            var left = double.MaxValue;
            var right = double.MinValue;
            for (var i = 0; i < snapshot.Count; i++)
            {
                left = Math.Min(
                    left,
                    snapshot.StartPos[i] - snapshot.StartWidth[i] * 0.5);
                right = Math.Max(
                    right,
                    snapshot.EndPos[i] + snapshot.EndWidth[i] * 0.5);
            }
            return Math.Max(0.0, right - left) * 100.0;
        }

        /// <summary>Total time span of the snapshot, in milliseconds.</summary>
        public static double TotalSpanMs(
            FalsumSkyAreaGroupSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                return 0.0;
            }
            var earliest = double.MaxValue;
            var latest = double.MinValue;
            for (var i = 0; i < snapshot.Count; i++)
            {
                earliest = Math.Min(earliest, snapshot.TimeMs[i]);
                latest = Math.Max(
                    latest,
                    snapshot.TimeMs[i] + snapshot.DurationMs[i]);
            }
            return Math.Max(0.0, latest - earliest);
        }

        /// <summary>
        /// Applies a preset's remembered width/length factors to a cloned
        /// segment list. Shared by "place from the bank" and the drag path so
        /// the two can never disagree.
        /// </summary>
        public static bool ApplyStoredScales(
            FalsumSkyAreaGroupSnapshot snapshot,
            double widthScale,
            double lengthScale,
            bool clampToBounds)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                return false;
            }
            if (!TryApplyWidthScale(snapshot, widthScale, clampToBounds))
            {
                return false;
            }
            return ApplyTimelineScale(snapshot, lengthScale);
        }

        /// <summary>Grid used for lateral writes (1..200).</summary>
        public static int ResolveGrid(
            FalsumSkyAreaGroupSnapshot snapshot)
        {
            if (snapshot == null || snapshot.GroupGrid <= 0)
            {
                return 1;
            }
            return ClampGrid(snapshot.GroupGrid);
        }

        public static double ClampWidthScale(double scale)
        {
            if (IsInvalid(scale))
            {
                return 1.0;
            }
            return Math.Max(
                MinimumWidthScale,
                Math.Min(MaximumWidthScale, scale));
        }

        /// <summary>
        /// The per-endpoint lattice, mirroring the order the middle-mouse edit
        /// resolves it: a declared positive cell count wins, otherwise the
        /// denominator is inferred from the position/width pair.
        /// </summary>
        public static int EndpointGrid(
            ChartNote note,
            bool tail)
        {
            if (note == null || note.Kind != NoteKind.SkyArea)
            {
                return 4;
            }
            var declared = tail
                ? note.EndCellCount
                : note.StartCellCount;
            if (declared > 0)
            {
                return ClampGrid(declared);
            }
            var position = tail ? note.EndPos : note.StartPos;
            var width = tail ? note.EndWidth : note.StartWidth;
            double normalizedPosition;
            double normalizedWidth;
            var inferred = FalsumFraction.NormalizePair(
                position,
                width,
                1,
                out normalizedPosition,
                out normalizedWidth);
            return ClampGrid(inferred);
        }

        public static int LeastCommonMultiple(int left, int right)
        {
            left = Math.Max(1, Math.Abs(left));
            right = Math.Max(1, Math.Abs(right));
            var a = left;
            var b = right;
            while (b > 0)
            {
                var remainder = a % b;
                a = b;
                b = remainder;
            }
            var result = (long)left * (long)right / Math.Max(1, a);
            return result > MaxGrid
                ? MaxGrid
                : (int)result;
        }

        /// <summary>Rounds to the nearest 1/grid step, away from zero (A-9).</summary>
        public static double Quantize(double value, int grid)
        {
            grid = ClampGrid(grid);
            if (IsInvalid(value))
            {
                return 0.0;
            }
            var scaled = Math.Round(
                value * grid,
                MidpointRounding.AwayFromZero);
            return scaled / grid;
        }

        /// <summary>Millisecond integerisation used by every temporal write.</summary>
        public static double IntegerTime(double valueMs)
        {
            if (IsInvalid(valueMs))
            {
                return 0.0;
            }
            var clamped = Math.Max(0.0, valueMs);
            return Math.Round(
                clamped,
                MidpointRounding.AwayFromZero);
        }

        private static bool AccumulateWidthLimits(
            double center,
            double width,
            double pivot,
            ref double minimum,
            ref double maximum)
        {
            var halfWidth = Math.Max(0.0, width) * 0.5;
            if (halfWidth > 0.5 + PositionEpsilon)
            {
                return false;
            }
            var delta = center - pivot;
            if (Math.Abs(delta) <= PositionEpsilon)
            {
                // The endpoint sits ON the pivot: its coordinate cannot move,
                // so the only question is whether it is already legal.
                var low = center - halfWidth;
                var high = center + halfWidth;
                return low >= -PositionEpsilon &&
                       high <= 1.0 + PositionEpsilon;
            }
            // center(s) = pivot + delta * s
            // left  <= 0  ->  depends on the sign of delta
            // right >= 1  ->  depends on the sign of delta
            var minimumFromLeft = (halfWidth - pivot) / delta;
            var maximumFromRight = (1.0 - pivot - halfWidth) / delta;
            if (delta > 0.0)
            {
                // left grows with s, right grows with s.
                minimum = Math.Max(minimum, minimumFromLeft);
                maximum = Math.Min(maximum, maximumFromRight);
            }
            else
            {
                // left shrinks as s grows, right shrinks as s grows.
                maximum = Math.Min(maximum, minimumFromLeft);
                minimum = Math.Max(minimum, maximumFromRight);
            }
            return minimum <= maximum + PositionEpsilon;
        }

        private static List<double> Deduplicate(List<double> sorted)
        {
            var result = new List<double>(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                if (result.Count == 0 ||
                    Math.Abs(result[result.Count - 1] - sorted[i]) >
                    PositionEpsilon)
                {
                    result.Add(sorted[i]);
                }
            }
            return result;
        }

        private static int CompareByTimeThenId(
            ChartNote left,
            ChartNote right)
        {
            var byTime = left.TimeMs.CompareTo(right.TimeMs);
            return byTime != 0
                ? byTime
                : left.Id.CompareTo(right.Id);
        }

        private static int ClampGrid(int grid)
        {
            if (grid <= 0)
            {
                return 1;
            }
            return grid > MaxGrid ? MaxGrid : grid;
        }

        private static bool IsInvalid(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }
    }
}
