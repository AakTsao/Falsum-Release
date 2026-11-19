using System;
using System.Collections.Generic;
using System.Globalization;
using InFalsus.Gameplay.Core.Charts;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Core
{
    public struct FalsumBeatLine
    {
        public double TimeMs;
        public int ColorIndex;
        public bool IsBeat;
        public bool IsMeasure;
        public bool IsGridLine;
    }

    public sealed class FalsumBeatGrid
    {
        public const string DefaultColorSequence =
            "#FF454A, #FFD636, #3DF28A, #45A8FF";

        /// <summary>
        /// Largest denominator the editor can represent on a grid. The
        /// storage ceiling (<see cref="FalsumFraction.MaxDenominator"/>, 200)
        /// is deliberately wider than this interactive one; invariant A-8
        /// keeps the two apart. Everything that clamps an interactive grid
        /// denominator must use this constant so the ruler, the mode plane and
        /// the snapping helpers cannot drift apart.
        /// </summary>
        public const int MaxGridDenominator = 192;

        /// <summary>
        /// Ordered set of grid denominators the <c>-</c> / <c>=</c> keys
        /// step through. Kept as text for the same reason
        /// <see cref="DefaultColorSequence"/> is: a hand-edited profile must be
        /// able to carry a malformed value that
        /// <c>FalsumPreferences.Normalize</c> repairs, and a canonical
        /// re-serialize keeps the stored form stable.
        /// </summary>
        public const string DefaultDenominatorSequence =
            "12, 24, 36, 48, 50, 72, 96, 100";

        private const double TimeToleranceMs = 0.01;
        private const int MaxLines = 20000;
        private const int MaxColorCount = 8;
        private const int MaxDenominatorCount = 8;
        private static readonly Color[] DefaultColors =
        {
            new Color(1.0f, 0.27f, 0.29f, 0.65f),
            new Color(1.0f, 0.84f, 0.21f, 0.65f),
            new Color(0.24f, 0.95f, 0.54f, 0.65f),
            new Color(0.27f, 0.66f, 1.0f, 0.65f),
        };
        private readonly List<Color> _colors =
            new List<Color>(DefaultColors);
        private readonly List<FalsumBeatLine> _scratch =
            new List<FalsumBeatLine>();
        private readonly List<FalsumBeatLine> _merged =
            new List<FalsumBeatLine>();
        private float _lineWidthScale = 1.0f;

        public bool Enabled { get; set; }
        public int Subdivision { get; set; } = 4;

        public float LineWidthScale
        {
            get { return _lineWidthScale; }
            set { _lineWidthScale = Mathf.Clamp(value, 0.25f, 3.0f); }
        }

        public Color ColorFor(int index)
        {
            if (_colors.Count == 0)
            {
                return DefaultColors[0];
            }
            var count = _colors.Count;
            return _colors[((index % count) + count) % count];
        }

        public void SetColorSequence(IList<Color> colors)
        {
            _colors.Clear();
            if (colors != null)
            {
                for (var i = 0;
                     i < colors.Count && i < MaxColorCount;
                     i++)
                {
                    _colors.Add(colors[i]);
                }
            }
            if (_colors.Count == 0)
            {
                _colors.AddRange(DefaultColors);
            }
        }

        public static bool TryParseColorSequence(
            string value,
            out List<Color> colors,
            out string error)
        {
            colors = new List<Color>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = L.Get("falsum.error.beatGrid.colorRequired");
                return false;
            }
            var parts = value.Split(
                new[] { ',', ';', '|', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = L.Get("falsum.error.beatGrid.colorRequired");
                return false;
            }
            if (parts.Length > MaxColorCount)
            {
                error = L.Get(
                    "falsum.error.beatGrid.tooManyColors",
                    ("max", MaxColorCount));
                return false;
            }
            for (var i = 0; i < parts.Length; i++)
            {
                Color color;
                if (!ColorUtility.TryParseHtmlString(
                        parts[i],
                        out color))
                {
                    error = L.Get(
                        "falsum.error.beatGrid.invalidColor",
                        ("color", parts[i]));
                    return false;
                }
                colors.Add(color);
            }
            return true;
        }

        public static string FormatColorSequence(
            IList<Color> colors)
        {
            if (colors == null || colors.Count == 0)
            {
                return DefaultColorSequence;
            }
            var parts = new List<string>(colors.Count);
            for (var i = 0; i < colors.Count; i++)
            {
                parts.Add(
                    "#" +
                    ColorUtility.ToHtmlStringRGB(colors[i]));
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Parses the comma-separated denominator sequence. Order is
        /// preserved (the sequence is authored, not sorted), entries must sit
        /// inside <c>1..MaxGridDenominator</c>, and duplicates are rejected so
        /// a stepping walk can never stall on two equal neighbours.
        /// </summary>
        public static bool TryParseDenominatorSequence(
            string value,
            out List<int> values,
            out string error)
        {
            values = new List<int>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = L.Get("falsum.error.gridSequence.required");
                return false;
            }
            var parts = value.Split(
                new[] { ',', ';', '|', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = L.Get("falsum.error.gridSequence.required");
                return false;
            }
            if (parts.Length > MaxDenominatorCount)
            {
                error = L.Get(
                    "falsum.error.gridSequence.tooMany",
                    ("max", MaxDenominatorCount));
                return false;
            }
            for (var i = 0; i < parts.Length; i++)
            {
                int parsed;
                if (!int.TryParse(
                        parts[i],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out parsed) ||
                    parsed < 1 ||
                    parsed > MaxGridDenominator)
                {
                    error = L.Get(
                        "falsum.error.gridSequence.range",
                        ("value", parts[i]));
                    return false;
                }
                if (values.Contains(parsed))
                {
                    error = L.Get(
                        "falsum.error.gridSequence.duplicate",
                        ("value", parts[i]));
                    return false;
                }
                values.Add(parsed);
            }
            return true;
        }

        public static string FormatDenominatorSequence(
            IList<int> values)
        {
            if (values == null || values.Count == 0)
            {
                return DefaultDenominatorSequence;
            }
            var parts = new List<string>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                parts.Add(
                    values[i].ToString(
                        CultureInfo.InvariantCulture));
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Walks one step along the authored sequence.
        ///
        /// <para>A <paramref name="current"/> that is NOT a sequence member
        /// (a note can carry any native cell count, up to
        /// <see cref="FalsumFraction.MaxDenominator"/>) resolves to the nearest
        /// member in the requested direction, so stepping a 1/200 note lands on
        /// 100 going down rather than onto an arbitrary value. The walk clamps
        /// at the ends instead of wrapping: the first and last entries are
        /// terminal stops.</para>
        /// </summary>
        public static int StepDenominator(
            IList<int> sequence,
            int current,
            int direction)
        {
            if (sequence == null || sequence.Count == 0)
            {
                return Mathf.Clamp(current, 1, MaxGridDenominator);
            }
            var count = sequence.Count;
            if (direction < 0)
            {
                var best = -1;
                for (var i = 0; i < count; i++)
                {
                    var entry = sequence[i];
                    if (entry < current &&
                        (best < 0 || entry > sequence[best]))
                    {
                        best = i;
                    }
                }
                // No member lies strictly below: the first entry is the floor.
                var index = best >= 0 ? best : 0;
                return Mathf.Clamp(sequence[index], 1, MaxGridDenominator);
            }
            var bestUp = -1;
            for (var i = 0; i < count; i++)
            {
                var entry = sequence[i];
                if (entry > current &&
                    (bestUp < 0 || entry < sequence[bestUp]))
                {
                    bestUp = i;
                }
            }
            var indexUp = bestUp >= 0 ? bestUp : count - 1;
            return Mathf.Clamp(sequence[indexUp], 1, MaxGridDenominator);
        }

        public static int NearestDenominator(
            IList<int> sequence,
            int current,
            int fallback)
        {
            if (sequence == null || sequence.Count == 0)
            {
                return Mathf.Clamp(fallback, 1, MaxGridDenominator);
            }
            var best = sequence[0];
            var bestDistance = Mathf.Abs(sequence[0] - current);
            for (var i = 1; i < sequence.Count; i++)
            {
                var distance = Mathf.Abs(sequence[i] - current);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = sequence[i];
                }
            }
            return Mathf.Clamp(best, 1, MaxGridDenominator);
        }

        public IList<FalsumBeatLine> Build(
            Chart chart,
            double startMs,
            double endMs)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            var from = Math.Max(0.0, Math.Min(startMs, endMs));
            var to = Math.Max(from, Math.Max(startMs, endMs));
            _scratch.Clear();
            var subdivision = Math.Max(1, Subdivision);
            var segmentStart = 0.0;
            var bpm = chart.Bpm;
            var eventIndex = 0;

            while (segmentStart <= to && _scratch.Count < MaxLines)
            {
                while (eventIndex < chart.BpmEvents.Count &&
                       chart.BpmEvents[eventIndex].TimeMs <=
                       segmentStart + 0.0001)
                {
                    bpm = chart.BpmEvents[eventIndex].Bpm;
                    eventIndex++;
                }

                var nextEvent = eventIndex < chart.BpmEvents.Count
                    ? chart.BpmEvents[eventIndex].TimeMs
                    : double.PositiveInfinity;
                var segmentEnd = Math.Min(nextEvent, to);
                var step = 60000.0 / Math.Max(1.0, bpm) / subdivision;
                var stepIndex = 0;
                // Grid lines use chart time. Delay only maps chart time to
                // music time during audio playback and file I/O.
                for (var time = segmentStart;
                     time <= segmentEnd + 0.0001 &&
                     _scratch.Count < MaxLines;
                     time += step, stepIndex++)
                {
                    if (time + 0.0001 < from)
                    {
                        continue;
                    }
                    var colorIndex = stepIndex % subdivision;
                    _scratch.Add(new FalsumBeatLine
                    {
                        TimeMs = time,
                        ColorIndex = colorIndex,
                        IsBeat = colorIndex == 0,
                        IsMeasure = false,
                        IsGridLine = true,
                    });
                }

                if (double.IsInfinity(nextEvent) ||
                    nextEvent > to)
                {
                    break;
                }
                segmentStart = nextEvent;
            }

            AppendMeasureLines(chart, from, to);
            return MergeLines();
        }

        public double Snap(Chart chart, double timeMs)
        {
            if (!Enabled)
            {
                return Math.Max(0.0, timeMs);
            }
            var span = 16000.0;
            var lines = Build(chart, timeMs - span, timeMs + span);
            var best = Math.Max(0.0, timeMs);
            var bestDistance = double.MaxValue;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!lines[i].IsGridLine)
                {
                    continue;
                }
                var distance = Math.Abs(lines[i].TimeMs - timeMs);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = lines[i].TimeMs;
                }
            }
            return Math.Max(0.0, best);
        }

        private void AppendMeasureLines(
            Chart chart,
            double from,
            double to)
        {
            var first = LowerBound(chart.MeasureLineTimes, from);
            for (var i = first;
                 i < chart.MeasureLineTimes.Count &&
                 _scratch.Count < MaxLines;
                 i++)
            {
                var time = chart.MeasureLineTimes[i];
                if (time > to + TimeToleranceMs)
                {
                    break;
                }
                _scratch.Add(new FalsumBeatLine
                {
                    TimeMs = time,
                    ColorIndex = 0,
                    IsBeat = false,
                    IsMeasure = true,
                    IsGridLine = false,
                });
            }
        }

        private IList<FalsumBeatLine> MergeLines()
        {
            // Measure timing stays authoritative; grid lines only supply
            // the normal subdivision points and snapping cadence.
            _merged.Clear();
            _scratch.Sort(
                (a, b) => a.TimeMs.CompareTo(b.TimeMs));
            for (var i = 0; i < _scratch.Count; i++)
            {
                var line = _scratch[i];
                if (_merged.Count == 0 ||
                    Math.Abs(
                        _merged[_merged.Count - 1].TimeMs -
                        line.TimeMs) > TimeToleranceMs)
                {
                    _merged.Add(line);
                    continue;
                }

                var previous = _merged[_merged.Count - 1];
                previous.IsMeasure |= line.IsMeasure;
                previous.IsBeat |= line.IsBeat;
                if (line.IsGridLine)
                {
                    previous.TimeMs = line.TimeMs;
                    previous.ColorIndex = line.ColorIndex;
                    previous.IsGridLine = true;
                }
                _merged[_merged.Count - 1] = previous;
            }
            return _merged;
        }

        private static int LowerBound(
            IList<double> values,
            double target)
        {
            var low = 0;
            var high = values.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (values[middle] < target)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
        }

        public double Snap(
            Chart chart,
            double timeMs,
            double originalTimeMs)
        {
            if (!Enabled)
            {
                return Math.Max(0.0, timeMs);
            }
            var gridTime = Snap(chart, timeMs);
            var original = Math.Max(0.0, originalTimeMs);
            return Math.Abs(timeMs - original) <=
                   Math.Abs(timeMs - gridTime)
                ? original
                : gridTime;
        }

        public bool TryGetAdjacentGridLine(
            Chart chart,
            double timeMs,
            int direction,
            out double adjacentTimeMs)
        {
            adjacentTimeMs = Math.Max(0.0, timeMs);
            if (!Enabled || chart == null || direction == 0)
            {
                return false;
            }

            var forward = direction > 0;
            var span = 16000.0;
            var lines = Build(
                chart,
                Math.Max(0.0, timeMs - span),
                timeMs + span);
            var best = forward
                ? double.MaxValue
                : double.MinValue;
            var found = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!line.IsGridLine)
                {
                    continue;
                }
                if (forward)
                {
                    if (line.TimeMs <= timeMs + TimeToleranceMs)
                    {
                        continue;
                    }
                    if (line.TimeMs < best)
                    {
                        best = line.TimeMs;
                        found = true;
                    }
                }
                else
                {
                    if (line.TimeMs >= timeMs - TimeToleranceMs)
                    {
                        continue;
                    }
                    if (line.TimeMs > best)
                    {
                        best = line.TimeMs;
                        found = true;
                    }
                }
            }
            if (found)
            {
                adjacentTimeMs = best;
            }
            return found;
        }

        public double StepMs(Chart chart, double timeMs)
        {
            var bpm = chart.BpmAt(timeMs);
            var subdivision = Math.Max(1, Subdivision);
            return 60000.0 / Math.Max(1.0, bpm) / subdivision;
        }

        // Placement floors derived from the beat grid must disappear when the
        // user switches grid lines off; callers use this instead of StepMs so
        // a disabled grid never drags a note onto an invisible grid line.
        public double MinimumStepMs(Chart chart, double timeMs)
        {
            return Enabled
                ? Math.Max(1.0, StepMs(chart, timeMs))
                : 1.0;
        }
    }
}
