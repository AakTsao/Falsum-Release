using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Falsum.Tools
{
    public sealed class FalsumSkyAreaDisconnectedInterval
    {
        public double StartTimeMs;
        public double EndTimeMs;
        public int ActiveCount;
        public readonly List<string> NoteKeys =
            new List<string>();
    }

    public static class FalsumSkyAreaPlayability
    {
        private const double IntersectionEpsilon = 0.000001;

        private sealed class SkyRecord
        {
            public string Key;
            public ChartNote Note;
            public int StartMs;
            public int EndMs;
        }

        private struct CrossSection
        {
            public double Left;
            public double Right;
        }

        public static List<FalsumSkyAreaDisconnectedInterval>
            FindDisconnectedIntervals(
                IList<FalsumToolNoteRecord> records)
        {
            var result =
                new List<FalsumSkyAreaDisconnectedInterval>();
            if (records == null)
            {
                return result;
            }

            var notes = new List<SkyRecord>();
            var boundaries = new HashSet<int>();
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record == null ||
                    record.Note == null ||
                    record.Note.Kind != NoteKind.SkyArea)
                {
                    continue;
                }
                var start = Math.Max(
                    0,
                    (int)Math.Round(record.Note.TimeMs));
                var end = Math.Max(
                    start,
                    (int)Math.Round(record.Note.EndTimeMs));
                var sky = new SkyRecord
                {
                    Key = record.Key,
                    Note = record.Note,
                    StartMs = start,
                    EndMs = end,
                };
                notes.Add(sky);
                boundaries.Add(start);
                boundaries.Add(end + 1);
            }
            if (notes.Count == 0)
            {
                return result;
            }

            var times = new List<int>(boundaries);
            times.Sort();
            var active = new List<SkyRecord>();
            FalsumSkyAreaDisconnectedInterval current = null;
            for (var i = 0; i + 1 < times.Count; i++)
            {
                var segmentStart = times[i];
                var segmentEnd = times[i + 1];
                if (segmentEnd <= segmentStart)
                {
                    continue;
                }
                active.Clear();
                for (var j = 0; j < notes.Count; j++)
                {
                    var note = notes[j];
                    if (note.StartMs <= segmentStart &&
                        note.EndMs >= segmentStart)
                    {
                        active.Add(note);
                    }
                }
                if (active.Count == 0)
                {
                    current = null;
                    continue;
                }
                for (var timeMs = segmentStart;
                     timeMs < segmentEnd;
                     timeMs++)
                {
                    if (IntersectionExists(active, timeMs))
                    {
                        current = null;
                        continue;
                    }
                    if (current == null)
                    {
                        current =
                            new FalsumSkyAreaDisconnectedInterval
                            {
                                StartTimeMs = timeMs,
                                EndTimeMs = timeMs,
                                ActiveCount = active.Count,
                            };
                        for (var j = 0; j < active.Count; j++)
                        {
                            current.NoteKeys.Add(active[j].Key);
                        }
                        result.Add(current);
                    }
                    else
                    {
                        current.EndTimeMs = timeMs;
                    }
                }
            }
            return result;
        }

        private static bool IntersectionExists(
            List<SkyRecord> active,
            int timeMs)
        {
            var commonLeft = double.NegativeInfinity;
            var commonRight = double.PositiveInfinity;
            for (var i = 0; i < active.Count; i++)
            {
                var section = CrossSectionAt(
                    active[i].Note,
                    timeMs);
                if (section.Left >
                    section.Right + IntersectionEpsilon)
                {
                    return false;
                }
                commonLeft = Math.Max(
                    commonLeft,
                    section.Left);
                commonRight = Math.Min(
                    commonRight,
                    section.Right);
                if (commonLeft >
                    commonRight + IntersectionEpsilon)
                {
                    return false;
                }
            }
            return active.Count > 0;
        }

        private static CrossSection CrossSectionAt(
            ChartNote note,
            double timeMs)
        {
            var duration = Math.Max(1.0, note.DurationMs);
            var progress = Math.Max(
                0.0,
                Math.Min(
                    1.0,
                    (timeMs - note.TimeMs) / duration));
            var startHalfWidth =
                Math.Max(0.0, note.StartWidth) * 0.5;
            var endHalfWidth =
                Math.Max(0.0, note.EndWidth) * 0.5;
            var startLeft = note.StartPos - startHalfWidth;
            var startRight = note.StartPos + startHalfWidth;
            var endLeft = note.EndPos - endHalfWidth;
            var endRight = note.EndPos + endHalfWidth;
            var leftProgress = Ease(
                progress,
                note.LeftEasing);
            var rightProgress = Ease(
                progress,
                note.RightEasing);
            return new CrossSection
            {
                Left = startLeft +
                       (endLeft - startLeft) * leftProgress,
                Right = startRight +
                        (endRight - startRight) * rightProgress,
            };
        }

        private static double Ease(
            double progress,
            int easing)
        {
            if (easing == 1)
            {
                return Math.Sin(progress * Math.PI * 0.5);
            }
            if (easing == 2)
            {
                return 1.0 -
                       Math.Cos(progress * Math.PI * 0.5);
            }
            return progress;
        }
    }
}
