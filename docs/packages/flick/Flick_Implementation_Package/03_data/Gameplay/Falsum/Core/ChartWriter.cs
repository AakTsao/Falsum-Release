using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Core
{
    public static class ChartWriter
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        public static string Write(
            Chart chart,
            double delayMs,
            string songId,
            int difficulty)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            if (!IsFinitePositive(chart.Bpm) ||
                !IsFinitePositive(chart.Division) ||
                !IsFinite(delayMs))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.chartWriter.invalidTiming"));
            }
            if (ChartTimeAxis.ContainsNegativeMusicTime(chart, delayMs))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.chartWriter.negativeMusicTime"));
            }
            var outputChart = CreateCanonicalChart(chart);

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(songId))
            {
                lines.Add("# song: " + songId);
            }
            lines.Add(
                "# difficulty: " +
                difficulty.ToString(Invariant));
            lines.Add("# delay: " + Number(delayMs));
            lines.Add(
                "chart(" +
                    Number(outputChart.Bpm) + "," +
                    Number(outputChart.Division) + ")");

            if (Math.Abs(delayMs) > 0.0001)
            {
                lines.Add(
                    "track(" + Number(delayMs) + ",1.00)");
                lines.Add(
                    "bpm(" +
                    Number(delayMs) + "," +
                    Number(outputChart.Bpm) + "," +
                    Number(outputChart.Division) + ")");
            }

            var events = new List<ChartEvent>(outputChart.Events);
            events.Sort(CompareEvents);
            for (var i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                if (evt.IsChartBase)
                {
                    continue;
                }
                if (Math.Abs(delayMs) > 0.0001 &&
                    evt.Kind == ChartEventKind.Bpm &&
                    Math.Abs(evt.TimeMs) <= 0.0001 &&
                    Math.Abs(evt.Bpm - outputChart.Bpm) <= 0.0000001 &&
                    Math.Abs(
                        evt.Division - outputChart.Division) <=
                    0.0000001)
                {
                    continue;
                }
                if (evt.Kind == ChartEventKind.TrackSpeed)
                {
                    lines.Add(
                        "track(" +
                        Number(MusicTime(evt.TimeMs, delayMs)) + "," +
                        Number(evt.Multiplier) + ")");
                }
                else if (evt.Kind == ChartEventKind.Bpm)
                {
                    lines.Add(
                        "bpm(" +
                        Number(MusicTime(evt.TimeMs, delayMs)) + "," +
                        Number(evt.Bpm) + "," +
                        Number(evt.Division) + ")");
                }
                else if (evt.Kind == ChartEventKind.LaneLayout)
                {
                    lines.Add(
                        "lane(" +
                        Number(MusicTime(evt.TimeMs, delayMs)) + "," +
                        evt.Lane.ToString(Invariant) + "," +
                        (evt.Enabled ? "1" : "0") + ")");
                }
            }

            var notes = new List<ChartNote>(outputChart.Notes);
            notes.Sort(FalsumSkyAreaGrouping.CompareNotesForOutput);
            for (var i = 0; i < notes.Count; i++)
            {
                WriteNote(lines, notes[i], delayMs);
            }
            return string.Join("\n", lines.ToArray()) + "\n";
        }

        private static Chart CreateCanonicalChart(Chart source)
        {
            var chart = new Chart
            {
                Bpm = source.Bpm,
                Division = source.Division,
                DelayMs = source.DelayMs,
                SongId = source.SongId,
                Difficulty = source.Difficulty,
                SourcePath = source.SourcePath,
            };
            for (var i = 0; i < source.Notes.Count; i++)
            {
                chart.Notes.Add(
                    source.Notes[i] == null
                        ? null
                        : CloneNote(source.Notes[i]));
            }
            for (var i = 0; i < source.Events.Count; i++)
            {
                chart.Events.Add(source.Events[i]);
            }
            chart.Notes.Sort(
                FalsumSkyAreaGrouping.CompareNotesForOutput);
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                chart.Notes[i].SourceOrder = i;
            }
            FalsumSkyAreaGrouping.Normalize(chart);
            return chart;
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
                Consumed = source.Consumed,
                Judged = source.Judged,
                Holding = source.Holding,
                Result = source.Result,
                HoldStartResult = source.HoldStartResult,
            };
        }

        private static void WriteNote(
            List<string> lines,
            ChartNote note,
            double delayMs)
        {
            ChartNoteValidator.Validate(
                note,
                "chart note",
                allowOutOfBounds: true);
            if (note.Kind == NoteKind.Tap)
            {
                lines.Add(
                    "tap(" +
                    Number(MusicTime(note.TimeMs, delayMs)) + "," +
                    Math.Max(1, note.WidthLanes).ToString(Invariant) + "," +
                    note.StartLane.ToString(Invariant) + ")");
                return;
            }
            if (note.Kind == NoteKind.Hold)
            {
                lines.Add(
                    "hold(" +
                    Number(MusicTime(note.TimeMs, delayMs)) + "," +
                    note.StartLane.ToString(Invariant) + "," +
                    Math.Max(1, note.WidthLanes).ToString(Invariant) + "," +
                    Number(note.DurationMs) + ")");
                return;
            }
            if (note.Kind == NoteKind.Flick)
            {
                var cells = note.StartCellCount > 0
                    ? note.StartCellCount
                    : 24;
                double normalizedPosition;
                double normalizedWidth;
                cells = FalsumFraction.NormalizePair(
                    note.StartPos,
                    note.StartWidth,
                    cells,
                    out normalizedPosition,
                    out normalizedWidth);
                var center = Math.Round(
                    normalizedPosition * cells,
                    MidpointRounding.AwayFromZero);
                var width = Math.Max(
                    0.0,
                    Math.Round(
                        normalizedWidth * cells,
                        MidpointRounding.AwayFromZero));
                lines.Add(
                    "flick(" +
                    Number(MusicTime(note.TimeMs, delayMs)) + "," +
                    Number(center) + "," +
                    cells.ToString(Invariant) + "," +
                    Number(width) + "," +
                    note.FlickDirection.ToString(Invariant) + ")");
                return;
            }

            var startCells = note.StartCellCount > 0
                ? note.StartCellCount
                : 24;
            var endCells = note.EndCellCount > 0
                ? note.EndCellCount
                : startCells;
            double normalizedStartPos;
            double normalizedStartWidth;
            double normalizedEndPos;
            double normalizedEndWidth;
            startCells = FalsumFraction.NormalizePair(
                note.StartPos,
                note.StartWidth,
                startCells,
                out normalizedStartPos,
                out normalizedStartWidth);
            endCells = FalsumFraction.NormalizePair(
                note.EndPos,
                note.EndWidth,
                endCells,
                out normalizedEndPos,
                out normalizedEndWidth);
            var startPos = Math.Round(
                normalizedStartPos * startCells,
                MidpointRounding.AwayFromZero);
            var startWidth = Math.Max(
                1.0,
                Math.Round(
                    normalizedStartWidth * startCells,
                    MidpointRounding.AwayFromZero));
            var endPos = Math.Round(
                normalizedEndPos * endCells,
                MidpointRounding.AwayFromZero);
            var endWidth = Math.Max(
                1.0,
                Math.Round(
                    normalizedEndWidth * endCells,
                    MidpointRounding.AwayFromZero));
            lines.Add(
                "skyarea(" +
                Number(MusicTime(note.TimeMs, delayMs)) + "," +
                Number(startPos) + "," +
                startCells.ToString(Invariant) + "," +
                Number(startWidth) + "," +
                Number(endPos) + "," +
                endCells.ToString(Invariant) + "," +
                Number(endWidth) + "," +
                note.LeftEasing.ToString(Invariant) + "," +
                note.RightEasing.ToString(Invariant) + "," +
                Number(note.DurationMs) + "," +
                note.GroupId.ToString(Invariant) + ")");
        }

        private static double MusicTime(double chartTimeMs, double delayMs)
        {
            return ChartTimeAxis.ToMusicTime(chartTimeMs, delayMs);
        }

        private static int CompareEvents(ChartEvent left, ChartEvent right)
        {
            var byTime = left.TimeMs.CompareTo(right.TimeMs);
            return byTime != 0
                ? byTime
                : left.Kind.CompareTo(right.Kind);
        }

        private static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.chartWriter.nonFiniteValue"));
            }
            if (Math.Abs(value) < 0.0000005)
            {
                return "0";
            }
            return value.ToString("0.######", Invariant);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinitePositive(double value)
        {
            return IsFinite(value) && value > 0.0;
        }
    }
}
