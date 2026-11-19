using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Falsum.Core
{
    public static class FalsumSkyAreaGrouping
    {
        public const double TimeToleranceMs = 2.0;

        private const double SpanEpsilon = 0.000001;

        public static bool SegmentsConnect(
            ChartNote left,
            ChartNote right)
        {
            if (left == null ||
                right == null ||
                left.Kind != NoteKind.SkyArea ||
                right.Kind != NoteKind.SkyArea)
            {
                return false;
            }
            return left.TimeMs <= right.TimeMs
                ? ConnectsAtBoundary(left, right)
                : ConnectsAtBoundary(right, left);
        }

        public static int Normalize(Chart chart)
        {
            if (chart == null || chart.Notes == null)
            {
                return 0;
            }

            var orderedNotes = new List<ChartNote>();
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                if (note != null)
                {
                    orderedNotes.Add(note);
                }
            }
            if (orderedNotes.Count == 0)
            {
                return 0;
            }

            var sourceOrders = new HashSet<int>();
            var hasCompleteSourceOrder = true;
            for (var i = 0; i < orderedNotes.Count; i++)
            {
                var order = orderedNotes[i].SourceOrder;
                if (order < 0 || !sourceOrders.Add(order))
                {
                    hasCompleteSourceOrder = false;
                    break;
                }
            }
            if (hasCompleteSourceOrder)
            {
                orderedNotes.Sort(
                    (left, right) =>
                        left.SourceOrder.CompareTo(right.SourceOrder));
            }

            var nextGroupId = 0;
            var skyGroupCount = 0;
            ChartNote previousSkyArea = null;
            for (var i = 0; i < orderedNotes.Count; i++)
            {
                var note = orderedNotes[i];
                if (note.Kind != NoteKind.SkyArea)
                {
                    nextGroupId++;
                    continue;
                }

                if (previousSkyArea != null &&
                    SegmentsConnect(previousSkyArea, note))
                {
                    note.GroupId = previousSkyArea.GroupId;
                }
                else
                {
                    note.GroupId = nextGroupId++;
                    skyGroupCount++;
                }
                previousSkyArea = note;
            }
            return skyGroupCount;
        }

        private static bool ConnectsAtBoundary(
            ChartNote left,
            ChartNote right)
        {
            return Math.Abs(left.EndTimeMs - right.TimeMs) <=
                   TimeToleranceMs;
        }

        public static bool SpansOverlap(
            double leftCenter,
            double leftWidth,
            double rightCenter,
            double rightWidth)
        {
            var leftMin = leftCenter - Math.Max(0.0, leftWidth) * 0.5;
            var leftMax = leftCenter + Math.Max(0.0, leftWidth) * 0.5;
            var rightMin = rightCenter - Math.Max(0.0, rightWidth) * 0.5;
            var rightMax = rightCenter + Math.Max(0.0, rightWidth) * 0.5;
            return Math.Min(leftMax, rightMax) >
                   Math.Max(leftMin, rightMin) + SpanEpsilon;
        }

        public static int CompareNotesForOutput(
            ChartNote left,
            ChartNote right)
        {
            var byTime = left.TimeMs.CompareTo(right.TimeMs);
            if (byTime != 0)
            {
                return byTime;
            }
            var byKind = left.Kind.CompareTo(right.Kind);
            if (byKind != 0)
            {
                return byKind;
            }
            var byLane = left.StartLane.CompareTo(right.StartLane);
            return byLane != 0
                ? byLane
                : left.Id.CompareTo(right.Id);
        }

    }
}
