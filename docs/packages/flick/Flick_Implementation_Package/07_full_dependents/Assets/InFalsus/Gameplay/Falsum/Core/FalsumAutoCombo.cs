using System;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Falsum.Core
{
    public static class FalsumAutoCombo
    {
        private const double BoundaryEpsilonMs = 0.0000001;
        private const double RatioEpsilon = 0.000000001;
        private const double TailMinimumMs = 2.0;

        public static int CountAt(Chart chart, double chartTimeMs)
        {
            if (chart == null || chart.Notes == null)
            {
                return 0;
            }

            if (double.IsNaN(chartTimeMs) ||
                double.IsInfinity(chartTimeMs))
            {
                chartTimeMs = 0.0;
            }
            var nowMs = Math.Max(0.0, chartTimeMs);
            long count = 0;
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                count += CountNoteAt(chart, chart.Notes[i], nowMs);
                if (count >= int.MaxValue)
                {
                    return int.MaxValue;
                }
            }
            return (int)count;
        }

        public static int CountNoteAt(
            Chart chart,
            ChartNote note,
            double chartTimeMs)
        {
            if (chart == null ||
                note == null ||
                !IsFinite(note.TimeMs))
            {
                return 0;
            }

            if (!IsFinite(chartTimeMs))
            {
                chartTimeMs = 0.0;
            }
            var nowMs = Math.Max(0.0, chartTimeMs);
            switch (note.Kind)
            {
                case NoteKind.Tap:
                case NoteKind.Flick:
                    return nowMs + BoundaryEpsilonMs >= note.TimeMs
                        ? 1
                        : 0;

                case NoteKind.Hold:
                    return CountHold(chart, note, nowMs);

                case NoteKind.SkyArea:
                    return CountSkyArea(chart, note, nowMs);

                default:
                    return 0;
            }
        }

        public static double StepMsAt(Chart chart, ChartNote note)
        {
            if (chart == null ||
                note == null ||
                !IsFinite(note.TimeMs))
            {
                return 0.0;
            }

            var bpm = chart.BpmAt(note.TimeMs);
            if (!IsFinite(bpm) || bpm <= 0.0)
            {
                return 0.0;
            }
            if (bpm >= 249.99)
            {
                bpm *= 0.5;
            }
            return 60000.0 / bpm / 2.0;
        }

        private static int CountHold(
            Chart chart,
            ChartNote note,
            double nowMs)
        {
            if (nowMs + BoundaryEpsilonMs < note.TimeMs)
            {
                return 0;
            }

            var stepMs = StepMsAt(chart, note);
            var durationMs = Math.Max(0.0, note.DurationMs);
            if (!IsFinite(durationMs) || stepMs <= 0.0)
            {
                return 1;
            }

            var interiorCount = SampleCountAt(
                nowMs - note.TimeMs,
                durationMs,
                stepMs);
            var count = 1L + interiorCount;
            if (HasTail(durationMs, stepMs) &&
                nowMs + BoundaryEpsilonMs >=
                note.TimeMs + durationMs)
            {
                count++;
            }
            return count >= int.MaxValue
                ? int.MaxValue
                : (int)count;
        }

        private static int CountSkyArea(
            Chart chart,
            ChartNote note,
            double nowMs)
        {
            if (nowMs + BoundaryEpsilonMs <= note.TimeMs)
            {
                return 0;
            }

            var stepMs = StepMsAt(chart, note);
            var durationMs = Math.Max(0.0, note.DurationMs);
            if (!IsFinite(durationMs) ||
                durationMs <= 0.0 ||
                stepMs <= 0.0)
            {
                return 0;
            }

            var count = SampleCountAt(
                nowMs - note.TimeMs,
                durationMs,
                stepMs);
            if (HasTail(durationMs, stepMs) &&
                nowMs + BoundaryEpsilonMs >=
                note.TimeMs + durationMs)
            {
                count++;
            }
            return count >= int.MaxValue
                ? int.MaxValue
                : (int)count;
        }

        private static int SampleCountAt(
            double elapsedMs,
            double durationMs,
            double stepMs)
        {
            if (elapsedMs <= 0.0)
            {
                return 0;
            }

            var maximum = FloorNonNegative(durationMs / stepMs);
            var current = FloorNonNegative(elapsedMs / stepMs);
            return (int)Math.Min(maximum, current);
        }

        private static bool HasTail(double durationMs, double stepMs)
        {
            var interiorCount = FloorNonNegative(
                durationMs / stepMs);
            var remainder = durationMs - interiorCount * stepMs;
            return remainder + BoundaryEpsilonMs >=
                   TailMinimumMs;
        }

        private static int FloorNonNegative(double value)
        {
            if (!IsFinite(value) || value <= 0.0)
            {
                return 0;
            }
            var floored = Math.Floor(value + RatioEpsilon);
            if (floored >= int.MaxValue)
            {
                return int.MaxValue;
            }
            return (int)floored;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }
    }
}
