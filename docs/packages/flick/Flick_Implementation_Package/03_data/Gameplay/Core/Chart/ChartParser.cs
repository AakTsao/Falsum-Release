using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace InFalsus.Gameplay.Core.Charts
{
    public static class ChartParser
    {
        private sealed class DelayMetadata
        {
            public double DelayMs;
            public int MarkerLine = -1;
            public bool UsesMusicTimeAxis;
        }

        public static ChartParseResult ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Chart path is empty.", "path");
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Chart file not found.", path);
            }

            return Parse(File.ReadAllLines(path), path);
        }

        public static ChartParseResult Parse(string[] lines, string sourcePath)
        {
            return Parse(lines, sourcePath, null);
        }

        public static ChartParseResult Parse(
            string[] lines,
            string sourcePath,
            int? expectedDifficulty)
        {
            var result = new ChartParseResult
            {
                Chart = new Chart
                {
                    SourcePath = sourcePath ?? string.Empty,
                    Difficulty = expectedDifficulty ?? 0,
                },
            };

            if (lines == null)
            {
                AddError(result, 0, "<file>", string.Empty, "Chart has no lines.");
                return result;
            }

            var delay = ResolveDelay(lines, result);
            result.Chart.DelayMs = delay.DelayMs;
            var sawChart = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var lineNumber = index + 1;
                var raw = lines[index] ?? string.Empty;
                var line = raw.Trim();

                if (line.Length == 0)
                {
                    continue;
                }
                if (line[0] == '#')
                {
                    ParseHeaderComment(result.Chart, line);
                    continue;
                }

                var open = line.IndexOf('(');
                var close = line.LastIndexOf(')');
                if (open <= 0 || close <= open)
                {
                    AddError(result, lineNumber, "<syntax>", raw,
                        "Expected directive(args).");
                    continue;
                }
                if (close != line.Length - 1)
                {
                    AddError(result, lineNumber, "<syntax>", raw,
                        "Unexpected content after directive.");
                    continue;
                }

                var directive = line.Substring(0, open).Trim();
                if (string.Equals(
                        directive,
                        "chart",
                        StringComparison.Ordinal))
                {
                    sawChart = true;
                }
                var argsText = line.Substring(open + 1, close - open - 1);
                var args = SplitArguments(argsText);
                if (args.Count == 0)
                {
                    AddError(result, lineNumber, directive, raw,
                        "Directive has no arguments.");
                    continue;
                }

                ParseDirective(
                    result,
                    lineNumber,
                    directive,
                    raw,
                    args,
                    delay);
            }

            if (!sawChart)
            {
                AddError(
                    result,
                    0,
                    "chart",
                    string.Empty,
                    "Chart is missing the chart(bpm,division) directive.");
            }
            for (var i = 0; i < result.Chart.Notes.Count; i++)
            {
                result.Chart.Notes[i].SourceOrder = i;
            }
            result.Chart.Notes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            result.Chart.RebuildTimeline();
            if (expectedDifficulty.HasValue)
            {
                result.Chart.Difficulty = expectedDifficulty.Value;
            }
            ValidateLaneEvents(result, expectedDifficulty);
            for (var i = 0; i < result.Chart.Notes.Count; i++)
            {
                result.Chart.Notes[i].Id = i;
            }

            return result;
        }

        private static void ParseDirective(
            ChartParseResult result,
            int lineNumber,
            string directive,
            string raw,
            List<string> args,
            DelayMetadata delay)
        {
            var chart = result.Chart;

            try
            {
                if (lineNumber == delay.MarkerLine)
                {
                    return;
                }

                switch (directive)
                {
                    case "chart":
                        RequireExact(
                            args,
                            2,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        chart.Bpm = Number(args[0]);
                        chart.Division = Number(args[1]);
                        if (chart.Bpm <= 0.0 || chart.Division <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "chart bpm and division must be positive.");
                        }
                        break;

                    case "track":
                        RequireExact(
                            args,
                            2,
                            directive,
                            lineNumber,
                            raw,
                            result);
                    {
                        var evt = new ChartEvent
                        {
                            Kind = ChartEventKind.TrackSpeed,
                            TimeMs = ChartTime(args[0], delay),
                            Multiplier = Number(args[1]),
                            SourceLine = lineNumber,
                        };
                        chart.Events.Add(evt);
                        chart.SpeedEvents.Add(evt);
                        break;
                    }

                    case "bpm":
                    {
                        RequireRange(
                            args,
                            3,
                            4,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        if (args.Count == 4 &&
                            Integer(args[3]) != 1)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "legacy bpm flag must be 1.");
                            break;
                        }
                        var evt = new ChartEvent
                        {
                            Kind = ChartEventKind.Bpm,
                            TimeMs = ChartTime(args[0], delay),
                            Bpm = Number(args[1]),
                            Division = Number(args[2]),
                            SourceLine = lineNumber,
                        };
                        if (evt.Bpm <= 0.0 || evt.Division <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "bpm and beat count must be positive.");
                            break;
                        }
                        chart.Events.Add(evt);
                        chart.BpmEvents.Add(evt);
                        break;
                    }

                    case "lane":
                    {
                        RequireExact(
                            args,
                            3,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        var lane = Integer(args[1]);
                        var enabledValue = Integer(args[2]);
                        if (lane != 0 && lane != 1 && lane != 4 && lane != 5)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "lane must be 0, 1, 4, or 5; switchable " +
                                "middle lanes are 1 and 4.");
                            break;
                        }
                        if (enabledValue != 0 && enabledValue != 1)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "lane state must be 0 (disabled) or 1 (enabled).");
                            break;
                        }
                        var evt = new ChartEvent
                        {
                            Kind = ChartEventKind.LaneLayout,
                            TimeMs = ChartTime(args[0], delay),
                            Lane = lane,
                            Enabled = enabledValue == 1,
                            SourceLine = lineNumber,
                        };
                        chart.Events.Add(evt);
                        chart.LaneEvents.Add(evt);
                        break;
                    }

                    case "tap":
                    {
                        RequireExact(
                            args,
                            3,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        var width = Integer(args[1]);
                        var lane = Integer(args[2]);
                        if (!ValidateLaneRange(lane, width, lineNumber, directive, raw, result))
                        {
                            break;
                        }
                        chart.Notes.Add(new ChartNote
                        {
                            Kind = NoteKind.Tap,
                            TimeMs = ChartTime(args[0], delay),
                            WidthLanes = width,
                            StartLane = lane,
                        });
                        break;
                    }

                    case "hold":
                    {
                        RequireExact(
                            args,
                            4,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        var lane = Integer(args[1]);
                        var width = Integer(args[2]);
                        var duration = Number(args[3]);
                        if (!ValidateLaneRange(lane, width, lineNumber, directive, raw, result))
                        {
                            break;
                        }
                        if (duration <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "hold duration must be positive.");
                            break;
                        }
                        chart.Notes.Add(new ChartNote
                        {
                            Kind = NoteKind.Hold,
                            TimeMs = ChartTime(args[0], delay),
                            StartLane = lane,
                            WidthLanes = width,
                            DurationMs = duration,
                        });
                        break;
                    }

                    case "flick":
                    {
                        RequireExact(
                            args,
                            5,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        var grid = Number(args[2]);
                        var center = Number(args[1]) / grid;
                        var widthCells = Number(args[3]);
                        if (grid <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "flick grid must be positive.");
                            break;
                        }
                        var gridCells = CellCount(args[2]);
                        if (widthCells < 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "flick width must not be negative.");
                            break;
                        }
                        var width = widthCells / grid;
                        var direction = Integer(args[4]);
                        if (direction != 4 && direction != 16)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "flick direction must be 4 or 16.");
                            break;
                        }
                        chart.Notes.Add(new ChartNote
                        {
                            // p1/grid is the midpoint; p2/grid is the width.
                            Kind = NoteKind.Flick,
                            TimeMs = ChartTime(args[0], delay),
                            StartPos = center,
                            StartWidth = width,
                            StartCellCount = gridCells,
                            EndCellCount = gridCells,
                            FlickDirection = direction,
                        });
                        break;
                    }

                    case "skyarea":
                    {
                        RequireExact(
                            args,
                            11,
                            directive,
                            lineNumber,
                            raw,
                            result);
                        var startCells = CellCount(args[2]);
                        var endCells = CellCount(args[5]);
                        var duration = Number(args[9]);
                        if (startCells <= 0.0 || endCells <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "skyarea cell counts must be positive.");
                            break;
                        }
                        if (duration <= 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "skyarea duration must be positive.");
                            break;
                        }
                        var startPos = Number(args[1]) / startCells;
                        var startWidth = Number(args[3]) / startCells;
                        var endPos = Number(args[4]) / endCells;
                        var endWidth = Number(args[6]) / endCells;
                        var leftEasing = Integer(args[7]);
                        var rightEasing = Integer(args[8]);
                        var groupId = Integer(args[10]);
                        if (startWidth < 0.0 || endWidth < 0.0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "skyarea widths must not be negative.");
                            break;
                        }
                        if (leftEasing < 0 || leftEasing > 2 ||
                            rightEasing < 0 || rightEasing > 2)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "skyarea easing must be within 0..2.");
                            break;
                        }
                        if (groupId < 0)
                        {
                            AddError(result, lineNumber, directive, raw,
                                "skyarea group id must not be negative.");
                            break;
                        }
                        chart.Notes.Add(new ChartNote
                        {
                            Kind = NoteKind.SkyArea,
                            TimeMs = ChartTime(args[0], delay),
                            StartPos = startPos,
                            StartCellCount = startCells,
                            StartWidth = startWidth,
                            EndPos = endPos,
                            EndCellCount = endCells,
                            EndWidth = endWidth,
                            LeftEasing = leftEasing,
                            RightEasing = rightEasing,
                            DurationMs = duration,
                            GroupId = groupId,
                        });
                        break;
                    }

                    default:
                        AddError(result, lineNumber, directive, raw,
                            "Unknown chart directive.");
                        break;
                }
            }
            catch (FormatException)
            {
                AddError(result, lineNumber, directive, raw,
                    "A numeric argument is invalid.");
            }
        }

        private static DelayMetadata ResolveDelay(
            string[] lines,
            ChartParseResult result)
        {
            var metadata = new DelayMetadata();
            var hasExplicitDelay = false;
            var hasValidExplicitDelay = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = (lines[index] ?? string.Empty).Trim();
                if (line.Length == 0 || line[0] != '#')
                {
                    continue;
                }
                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length < 2)
                {
                    continue;
                }
                var key = parts[0].Trim('#', ' ', '\t');
                if (!string.Equals(
                        key,
                        "delay",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasExplicitDelay = true;
                if (!TryNumber(parts[1].Trim(), out var value) ||
                    !IsFinite(value))
                {
                    AddError(
                        result,
                        index + 1,
                        "delay",
                        lines[index],
                        "delay must be a finite number.");
                    metadata.DelayMs = 0.0;
                    continue;
                }
                metadata.DelayMs = value;
                hasValidExplicitDelay = true;
            }

            metadata.UsesMusicTimeAxis = hasValidExplicitDelay;
            int markerLine;
            double markerDelay;
            if (!TryFindLegacyDelayMarker(
                    lines,
                    out markerLine,
                    out markerDelay))
            {
                return metadata;
            }

            if (!hasExplicitDelay)
            {
                metadata.DelayMs = markerDelay;
                metadata.MarkerLine = markerLine;
            }
            else if (hasValidExplicitDelay &&
                     Math.Abs(markerDelay - metadata.DelayMs) <= 0.0001)
            {
                metadata.MarkerLine = markerLine;
            }
            return metadata;
        }

        private static bool TryFindLegacyDelayMarker(
            string[] lines,
            out int lineNumber,
            out double delayMs)
        {
            lineNumber = -1;
            delayMs = 0.0;
            var sawChartDirective = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = (lines[index] ?? string.Empty).Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var open = line.IndexOf('(');
                var close = line.LastIndexOf(')');
                if (open <= 0 || close <= open)
                {
                    return false;
                }
                var directive = line.Substring(0, open).Trim();
                if (string.Equals(
                        directive,
                        "chart",
                        StringComparison.Ordinal) &&
                    !sawChartDirective)
                {
                    sawChartDirective = true;
                    continue;
                }
                if (!string.Equals(
                        directive,
                        "track",
                        StringComparison.Ordinal))
                {
                    return false;
                }
                var args = SplitArguments(
                    line.Substring(open + 1, close - open - 1));
                if (args.Count < 2 ||
                    !TryNumber(args[0], out delayMs) ||
                    !TryNumber(args[1], out var multiplier) ||
                    !IsFinite(delayMs) ||
                    Math.Abs(multiplier - 1.0) > 0.000001)
                {
                    return false;
                }
                lineNumber = index + 1;
                return true;
            }
            return false;
        }

        private static double ChartTime(
            string text,
            DelayMetadata delay)
        {
            if (!delay.UsesMusicTimeAxis)
            {
                return Number(text);
            }
            return ChartTimeAxis.ToChartTime(
                Number(text),
                delay.DelayMs);
        }

        private static bool TryNumber(string text, out double value)
        {
            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ValidateLaneEvents(
            ChartParseResult result,
            int? expectedDifficulty)
        {
            var laneSupported = expectedDifficulty.HasValue
                ? expectedDifficulty.Value == 0 ||
                  expectedDifficulty.Value == 1
                : result.Chart.Difficulty == 1 ||
                  result.Chart.Difficulty == 2;
            for (var i = 0; i < result.Chart.LaneEvents.Count; i++)
            {
                var evt = result.Chart.LaneEvents[i];
                if (!laneSupported)
                {
                    AddError(result, evt.SourceLine, "lane", string.Empty,
                        "lane events are only supported by MIN and EVO.");
                }
            }
        }

        private static bool ValidateLaneRange(
            int lane,
            int width,
            int lineNumber,
            string directive,
            string raw,
            ChartParseResult result)
        {
            if (lane < 0 || width <= 0 || lane + width > 6)
            {
                AddError(result, lineNumber, directive, raw,
                    "lane range must be within 0..5 and width must be positive.");
                return false;
            }
            return true;
        }

        private static void RequireExact(
            List<string> args,
            int count,
            string directive,
            int lineNumber,
            string raw,
            ChartParseResult result)
        {
            if (args.Count != count)
            {
                throw new FormatException(
                    directive + " expects exactly " + count + " arguments.");
            }
        }

        private static void RequireRange(
            List<string> args,
            int minimum,
            int maximum,
            string directive,
            int lineNumber,
            string raw,
            ChartParseResult result)
        {
            if (args.Count < minimum || args.Count > maximum)
            {
                throw new FormatException(
                    directive + " expects " + minimum +
                    " to " + maximum + " arguments.");
            }
        }

        private static void AddError(
            ChartParseResult result,
            int line,
            string directive,
            string raw,
            string message)
        {
            result.Diagnostics.Add(new ChartParseDiagnostic
            {
                File = result.Chart.SourcePath,
                Line = line,
                Directive = directive,
                Raw = raw,
                Message = message,
            });
        }

        private static void ParseHeaderComment(Chart chart, string line)
        {
            var parts = line.Split(new[] { ':' }, 2);
            if (parts.Length < 2)
            {
                return;
            }

            var key = parts[0].Trim('#', ' ', '\t');
            var value = parts[1].Trim();
            if (key == "song")
            {
                chart.SongId = value;
            }
            else if (key == "difficulty")
            {
                var split = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int difficulty;
                if (split.Length > 0 &&
                    int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out difficulty))
                {
                    chart.Difficulty = difficulty;
                }
            }
        }

        private static List<string> SplitArguments(string text)
        {
            var args = new List<string>();
            foreach (var part in text.Split(','))
            {
                args.Add(part.Trim());
            }
            return args;
        }

        private static double Number(string text)
        {
            var value = double.Parse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
            if (!IsFinite(value))
            {
                throw new FormatException(
                    "Numeric arguments must be finite.");
            }
            return value;
        }

        private static int Integer(string text)
        {
            var value = Number(text);
            var rounded = Math.Round(
                value,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(value - rounded) > 0.0000001 ||
                rounded < int.MinValue ||
                rounded > int.MaxValue)
            {
                throw new FormatException(
                    "Integer arguments must be Int32 values.");
            }
            return (int)rounded;
        }

        private static int CellCount(string text)
        {
            var value = Integer(text);
            if (value <= 0)
            {
                throw new FormatException(
                    "Cell count must be a positive integer.");
            }
            return value;
        }

        private static bool IsOutsideSkyBounds(
            double center,
            double width,
            double tolerance = 0.000000001)
        {
            return !IsFinite(center) ||
                   !IsFinite(width) ||
                   width < 0.0 ||
                   center - width * 0.5 < -tolerance ||
                   center + width * 0.5 > 1.0 + tolerance;
        }
    }
}
