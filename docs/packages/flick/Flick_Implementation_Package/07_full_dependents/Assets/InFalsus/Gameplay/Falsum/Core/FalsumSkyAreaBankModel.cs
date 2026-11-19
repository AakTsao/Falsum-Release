using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>One SkyArea segment inside a bank preset.</summary>
    public sealed class FalsumSkyAreaPresetSegment
    {
        public double TimeOffsetMs;
        public double DurationMs;
        public double StartPos;
        public double EndPos;
        public double StartWidth;
        public double EndWidth;
        public int StartCellCount;
        public int EndCellCount;
        public int LeftEasing;
        public int RightEasing;
    }

    /// <summary>
    /// A named, shareable bundle of SkyArea segments. Ids and timestamps are
    /// local bookkeeping and never travel in a share code.
    /// </summary>
    public sealed class FalsumSkyAreaPreset
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public readonly List<string> Tags = new List<string>();
        public string Author = string.Empty;
        public double WidthScale = 1.0;
        public double LengthScale = 1.0;
        public long CreatedAtUtc;
        public long UpdatedAtUtc;
        public long LastUsedAtUtc;
        public readonly List<FalsumSkyAreaPresetSegment> Segments =
            new List<FalsumSkyAreaPresetSegment>();

        /// <summary>
        /// Denominator the preset's lateral span is expressed in: the LCM of
        /// every segment's declared cell counts, clamped to the chart's 1/200
        /// lattice ceiling (<see cref="FalsumFraction.MaxDenominator"/>).
        ///
        /// <para>WHY THE SPAN NEEDS A DENOMINATOR AT ALL: the card used to
        /// print the raw percentage ("宽 16.666666666… %"), which is a
        /// non-terminating decimal for exactly the 1/3-and-1/6 widths this
        /// editor snaps to. The chart itself already thinks in integer cells
        /// (1/200 = 0.5 %), so the card shows the same <c>cells / denominator</c>
        /// fraction the property panel shows, and the number terminates.</para>
        /// </summary>
        public int SpanDenominator
        {
            get
            {
                if (Segments.Count == 0)
                {
                    return 1;
                }
                var denominator = 1;
                for (var i = 0; i < Segments.Count; i++)
                {
                    denominator = LeastCommonMultiple(
                        denominator,
                        ClampCellCount(Segments[i].StartCellCount));
                    denominator = LeastCommonMultiple(
                        denominator,
                        ClampCellCount(Segments[i].EndCellCount));
                }
                return Math.Min(denominator, FalsumFraction.MaxDenominator);
            }
        }

        /// <summary>
        /// The lateral span in cells of <see cref="SpanDenominator"/>, i.e. the
        /// numerator to display over that denominator. Integerised the same way
        /// the chart quantises positions, so the fraction the user reads matches
        /// the cells that will be written to the file.
        /// </summary>
        public int SpanCells
        {
            get
            {
                if (Segments.Count == 0)
                {
                    return 0;
                }
                var denominator = SpanDenominator;
                var span = (MaximumRight - MinimumLeft) * denominator;
                return (int)Math.Round(span, MidpointRounding.AwayFromZero);
            }
        }

        private static int ClampCellCount(int cells)
        {
            if (cells <= 0)
            {
                return 1;
            }
            return Math.Min(cells, FalsumFraction.MaxDenominator);
        }

        private static int LeastCommonMultiple(int left, int right)
        {
            if (left <= 0 || right <= 0)
            {
                return Math.Max(1, Math.Max(left, right));
            }
            return left / GreatestCommonDivisor(left, right) * right;
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                var remainder = left % right;
                left = right;
                right = remainder;
            }
            return Math.Max(1, Math.Abs(left));
        }

        public double TotalDurationMs
        {
            get
            {
                if (Segments.Count == 0)
                {
                    return 0.0;
                }
                var last = Segments[Segments.Count - 1];
                return last.TimeOffsetMs + last.DurationMs;
            }
        }

        /// <summary>
        /// Leftmost x the group ever occupies. Both endpoints of every segment
        /// are sampled: a segment may be widest at its START (a taper that
        /// shrinks towards the END), so reading only <c>StartPos -
        /// StartWidth/2</c> would let that left edge escape the bound and the
        /// thumbnail would then be normalised against too small a span.
        /// </summary>
        public double MinimumLeft
        {
            get
            {
                var left = double.MaxValue;
                for (var i = 0; i < Segments.Count; i++)
                {
                    var segment = Segments[i];
                    left = Math.Min(
                        left,
                        segment.StartPos - segment.StartWidth * 0.5);
                    left = Math.Min(
                        left,
                        segment.EndPos - segment.EndWidth * 0.5);
                }
                return Segments.Count == 0 ? 0.0 : left;
            }
        }

        /// <summary>
        /// Rightmost x the group ever occupies. Mirror of
        /// <see cref="MinimumLeft"/>: a segment may be widest at its END but
        /// also at its START, so both endpoints have to be sampled.
        /// </summary>
        public double MaximumRight
        {
            get
            {
                var right = double.MinValue;
                for (var i = 0; i < Segments.Count; i++)
                {
                    var segment = Segments[i];
                    right = Math.Max(
                        right,
                        segment.StartPos + segment.StartWidth * 0.5);
                    right = Math.Max(
                        right,
                        segment.EndPos + segment.EndWidth * 0.5);
                }
                return Segments.Count == 0 ? 0.0 : right;
            }
        }
    }

    /// <summary>
    /// Preset model, validation and JSON mapping for the Skyarea Bank.
    ///
    /// <para>WHY THE SEGMENT BOUNDARIES ARE INTEGERISED HERE: placement
    /// translates every segment by ONE integer millisecond delta, so if the
    /// saved offsets/durations are integers and the seams are exactly zero,
    /// the seams stay exactly zero after placement and
    /// <code>FalsumSkyAreaGrouping.Normalize</code> still folds the whole
    /// group into one chain. Saving float seams would let each segment round
    /// independently, produce a +/-1 ms gap and risk splitting the chain
    /// (the grouping tolerance is only 2 ms).</para>
    ///
    /// <para>Error reporting is via localisation keys only: Core must not
    /// contain user-visible prose (the audit rejects hardcoded text).</para>
    /// </summary>
    public static class FalsumSkyAreaBankModel
    {
        public const int CurrentVersion = 1;
        public const int MaxSegmentsPerPreset = 64;
        public const int MaxPresetsPerBank = 200;
        public const int MaxNameLength = 48;
        public const int MaxTags = 8;
        public const int MaxTagLength = 16;
        public const int MaxAuthorLength = 32;
        public const double MinScale = 0.25;
        public const double MaxScale = 4.0;

        public const string ErrorBadCode = "falsum.bank.error.badCode";
        public const string ErrorVersionTooNew =
            "falsum.bank.error.versionTooNew";
        public const string ErrorInvalidSegments =
            "falsum.bank.error.invalidSegments";
        public const string ErrorInvalidScale =
            "falsum.bank.error.invalidScale";
        public const string ErrorBankFull = "falsum.bank.error.bankFull";
        public const string ErrorTooManySegments =
            "falsum.bank.error.tooManySegments";

        private const double BoundEpsilon = 0.000000001;

        /// <summary>
        /// Builds a preset from the currently selected SkyArea notes.
        /// Returns null when the selection holds no SkyArea.
        /// </summary>
        public static FalsumSkyAreaPreset FromSelection(
            IList<ChartNote> skyNotes,
            string name)
        {
            var snapshot = FalsumSkyAreaGroupScale.Capture(skyNotes);
            if (snapshot == null)
            {
                return null;
            }

            var preset = new FalsumSkyAreaPreset
            {
                Id = NewId(),
                Name = SanitizeText(name, MaxNameLength),
                WidthScale = 1.0,
                LengthScale = 1.0,
            };
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            preset.CreatedAtUtc = now;
            preset.UpdatedAtUtc = now;

            // Every boundary is integerised INDIVIDUALLY from the original
            // value. Contiguity then survives on its own: if segment i ended
            // exactly where segment i+1 started, both integerise to the same
            // number, so the seam stays exactly zero. Gapped selections keep
            // their gap instead of being silently glued together.
            var anchor = FalsumSkyAreaGroupScale.IntegerTime(
                snapshot.AnchorTimeMs);
            for (var i = 0; i < snapshot.Count; i++)
            {
                var start = FalsumSkyAreaGroupScale.IntegerTime(
                    snapshot.TimeMs[i]);
                var end = FalsumSkyAreaGroupScale.IntegerTime(
                    snapshot.TimeMs[i] + snapshot.DurationMs[i]);
                if (end < start + 1.0)
                {
                    end = start + 1.0;
                }
                preset.Segments.Add(new FalsumSkyAreaPresetSegment
                {
                    TimeOffsetMs = start - anchor,
                    DurationMs = end - start,
                    StartPos = snapshot.StartPos[i],
                    EndPos = snapshot.EndPos[i],
                    StartWidth = snapshot.StartWidth[i],
                    EndWidth = snapshot.EndWidth[i],
                    StartCellCount = snapshot.Notes[i].StartCellCount > 0
                        ? snapshot.Notes[i].StartCellCount
                        : snapshot.Grid[i],
                    EndCellCount = snapshot.Notes[i].EndCellCount > 0
                        ? snapshot.Notes[i].EndCellCount
                        : snapshot.Grid[i],
                    LeftEasing = ClampEasing(snapshot.Notes[i].LeftEasing),
                    RightEasing = ClampEasing(
                        snapshot.Notes[i].RightEasing),
                });
            }
            return preset;
        }

        public static string NewId()
        {
            return "p-" + Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Full constraint check. Returns false and a localisation key on
        /// failure; never mutates the preset.
        /// </summary>
        public static bool Validate(
            FalsumSkyAreaPreset preset,
            out string errorKey)
        {
            errorKey = string.Empty;
            if (preset == null)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }
            if (preset.Segments == null ||
                preset.Segments.Count <= 0)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }
            if (preset.Segments.Count > MaxSegmentsPerPreset)
            {
                errorKey = ErrorTooManySegments;
                return false;
            }
            if (!IsScaleInRange(preset.WidthScale) ||
                !IsScaleInRange(preset.LengthScale))
            {
                errorKey = ErrorInvalidScale;
                return false;
            }
            if (preset.Name == null)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }
            // An empty name is legal: the UI renders its own localised
            // placeholder rather than forcing a hardcoded name in Core.
            if (preset.Name.Length > MaxNameLength)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }
            if (preset.Tags != null && preset.Tags.Count > MaxTags)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }

            var expectedOffset = 0.0;
            for (var i = 0; i < preset.Segments.Count; i++)
            {
                var segment = preset.Segments[i];
                if (segment == null)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (!IsFinite(segment.TimeOffsetMs) ||
                    !IsFinite(segment.DurationMs) ||
                    !IsFinite(segment.StartPos) ||
                    !IsFinite(segment.EndPos) ||
                    !IsFinite(segment.StartWidth) ||
                    !IsFinite(segment.EndWidth))
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                // Offsets/durations are whole milliseconds by construction,
                // but a selection may legitimately have gaps between its
                // segments. Only require monotonic, integral boundaries: a
                // contiguous chain then keeps an exact 0 ms seam for free.
                if (segment.TimeOffsetMs < -BoundEpsilon)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (Math.Abs(segment.TimeOffsetMs -
                             Math.Round(segment.TimeOffsetMs)) >
                    BoundEpsilon)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (segment.TimeOffsetMs < expectedOffset - BoundEpsilon)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (segment.DurationMs < 1.0 - BoundEpsilon)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (Math.Abs(segment.DurationMs -
                             Math.Round(segment.DurationMs)) >
                    BoundEpsilon)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (!IsSpanInBounds(segment.StartPos, segment.StartWidth) ||
                    !IsSpanInBounds(segment.EndPos, segment.EndWidth))
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (segment.StartCellCount <= 0 ||
                    segment.EndCellCount <= 0 ||
                    segment.StartCellCount > FalsumFraction.MaxDenominator ||
                    segment.EndCellCount > FalsumFraction.MaxDenominator)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                if (!IsEasingInRange(segment.LeftEasing) ||
                    !IsEasingInRange(segment.RightEasing))
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                expectedOffset = segment.TimeOffsetMs + segment.DurationMs;
            }
            return true;
        }

        /// <summary>Normalises user-supplied text before it is stored or compared.</summary>
        public static string SanitizeText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            var builder = new System.Text.StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\r' || c == '\n' || c == '\t')
                {
                    builder.Append(' ');
                    continue;
                }
                if (c < ' ')
                {
                    continue;
                }
                builder.Append(c);
            }
            var text = builder.ToString().Trim();
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).Trim();
            }
            return text;
        }

        public static List<string> SanitizeTags(IList<string> tags)
        {
            var result = new List<string>();
            if (tags == null)
            {
                return result;
            }
            for (var i = 0; i < tags.Count && result.Count < MaxTags; i++)
            {
                var tag = SanitizeText(tags[i], MaxTagLength);
                if (tag.Length == 0)
                {
                    continue;
                }
                var duplicate = false;
                for (var j = 0; j < result.Count; j++)
                {
                    if (string.Equals(
                            result[j],
                            tag,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    result.Add(tag);
                }
            }
            return result;
        }

        public static FalsumJsonObject ToJson(FalsumSkyAreaPreset preset)
        {
            var root = new FalsumJsonObject();
            FalsumJson.Set(root, "v", CurrentVersion);
            FalsumJson.Set(root, "name", preset.Name ?? string.Empty);
            var tags = new List<object>();
            if (preset.Tags != null)
            {
                for (var i = 0; i < preset.Tags.Count; i++)
                {
                    tags.Add(preset.Tags[i]);
                }
            }
            FalsumJson.Set(root, "tags", tags);
            FalsumJson.Set(root, "author", preset.Author ?? string.Empty);
            FalsumJson.Set(root, "cw", preset.WidthScale);
            FalsumJson.Set(root, "cl", preset.LengthScale);

            var segments = new List<object>();
            for (var i = 0; i < preset.Segments.Count; i++)
            {
                var segment = preset.Segments[i];
                var entry = new FalsumJsonObject();
                FalsumJson.Set(entry, "t", segment.TimeOffsetMs);
                FalsumJson.Set(entry, "dt", segment.DurationMs);
                FalsumJson.Set(entry, "sp", segment.StartPos);
                FalsumJson.Set(entry, "ep", segment.EndPos);
                FalsumJson.Set(entry, "sw", segment.StartWidth);
                FalsumJson.Set(entry, "ew", segment.EndWidth);
                FalsumJson.Set(entry, "sc", segment.StartCellCount);
                FalsumJson.Set(entry, "ec", segment.EndCellCount);
                FalsumJson.Set(entry, "le", segment.LeftEasing);
                FalsumJson.Set(entry, "re", segment.RightEasing);
                segments.Add(entry);
            }
            FalsumJson.Set(root, "segs", segments);
            return root;
        }

        /// <summary>
        /// Reads a preset out of a JSON object. Unknown keys are ignored (so a
        /// newer sibling field never breaks an older reader), a newer
        /// <c>v</c> is rejected outright instead of silently dropping data.
        /// </summary>
        public static bool FromJson(
            FalsumJsonObject root,
            out FalsumSkyAreaPreset preset,
            out string errorKey)
        {
            preset = null;
            errorKey = string.Empty;
            if (root == null)
            {
                errorKey = ErrorBadCode;
                return false;
            }

            var version = FalsumJson.GetInt(root, "v", CurrentVersion);
            if (version > CurrentVersion)
            {
                errorKey = ErrorVersionTooNew;
                return false;
            }

            var loaded = new FalsumSkyAreaPreset
            {
                Id = NewId(),
                Name = SanitizeText(
                    FalsumJson.GetString(root, "name", string.Empty),
                    MaxNameLength),
                Author = SanitizeText(
                    FalsumJson.GetString(root, "author", string.Empty),
                    MaxAuthorLength),
                WidthScale = FalsumJson.GetNumber(root, "cw", 1.0),
                LengthScale = FalsumJson.GetNumber(root, "cl", 1.0),
            };
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            loaded.CreatedAtUtc = now;
            loaded.UpdatedAtUtc = now;

            var tags = FalsumJson.GetArray(root, "tags");
            if (tags != null)
            {
                var raw = new List<string>();
                for (var i = 0; i < tags.Count; i++)
                {
                    var tag = tags[i] as string;
                    if (tag != null)
                    {
                        raw.Add(tag);
                    }
                }
                var sanitized = SanitizeTags(raw);
                for (var i = 0; i < sanitized.Count; i++)
                {
                    loaded.Tags.Add(sanitized[i]);
                }
            }

            var segments = FalsumJson.GetArray(root, "segs");
            if (segments == null || segments.Count == 0)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }
            if (segments.Count > MaxSegmentsPerPreset)
            {
                errorKey = ErrorTooManySegments;
                return false;
            }
            for (var i = 0; i < segments.Count; i++)
            {
                var entry = segments[i] as FalsumJsonObject;
                if (entry == null)
                {
                    errorKey = ErrorInvalidSegments;
                    return false;
                }
                var segment = new FalsumSkyAreaPresetSegment
                {
                    TimeOffsetMs = FalsumJson.GetNumber(entry, "t", 0.0),
                    DurationMs = FalsumJson.GetNumber(entry, "dt", 0.0),
                    StartPos = FalsumJson.GetNumber(entry, "sp", 0.0),
                    EndPos = FalsumJson.GetNumber(entry, "ep", 0.0),
                    StartWidth = FalsumJson.GetNumber(entry, "sw", 0.0),
                    EndWidth = FalsumJson.GetNumber(entry, "ew", 0.0),
                    StartCellCount = FalsumJson.GetInt(
                        entry,
                        "sc",
                        FalsumFraction.MaxDenominator),
                    EndCellCount = FalsumJson.GetInt(
                        entry,
                        "ec",
                        FalsumFraction.MaxDenominator),
                    LeftEasing = FalsumJson.GetInt(entry, "le", 0),
                    RightEasing = FalsumJson.GetInt(entry, "re", 0),
                };
                loaded.Segments.Add(segment);
            }

            if (loaded.Name.Length > 0 &&
                loaded.Name.Length > MaxNameLength)
            {
                errorKey = ErrorInvalidSegments;
                return false;
            }
            if (!Validate(loaded, out errorKey))
            {
                preset = null;
                return false;
            }
            preset = loaded;
            return true;
        }

        /// <summary>Rebuilds chart notes from a preset, ready to be placed.</summary>
        public static List<ChartNote> ToChartNotes(
            FalsumSkyAreaPreset preset,
            bool applyStoredScales)
        {
            var notes = new List<ChartNote>();
            if (preset == null || preset.Segments.Count == 0)
            {
                return notes;
            }
            for (var i = 0; i < preset.Segments.Count; i++)
            {
                var segment = preset.Segments[i];
                notes.Add(new ChartNote
                {
                    Kind = NoteKind.SkyArea,
                    TimeMs = segment.TimeOffsetMs,
                    DurationMs = segment.DurationMs,
                    StartPos = segment.StartPos,
                    EndPos = segment.EndPos,
                    StartWidth = segment.StartWidth,
                    EndWidth = segment.EndWidth,
                    StartCellCount = segment.StartCellCount,
                    EndCellCount = segment.EndCellCount,
                    LeftEasing = segment.LeftEasing,
                    RightEasing = segment.RightEasing,
                    FlickDirection = 4,
                });
            }
            if (applyStoredScales)
            {
                var snapshot = FalsumSkyAreaGroupScale.Capture(notes);
                if (snapshot != null)
                {
                    FalsumSkyAreaGroupScale.ApplyStoredScales(
                        snapshot,
                        preset.WidthScale,
                        preset.LengthScale,
                        true);
                }
            }
            return notes;
        }

        public static bool IsScaleInRange(double scale)
        {
            return !double.IsNaN(scale) &&
                   !double.IsInfinity(scale) &&
                   scale >= MinScale - BoundEpsilon &&
                   scale <= MaxScale + BoundEpsilon;
        }

        private static bool IsSpanInBounds(double center, double width)
        {
            if (width < -BoundEpsilon)
            {
                return false;
            }
            var half = width * 0.5;
            return center - half >= -BoundEpsilon &&
                   center + half <= 1.0 + BoundEpsilon;
        }

        private static bool IsEasingInRange(int easing)
        {
            return easing >= 0 && easing <= 2;
        }

        private static int ClampEasing(int easing)
        {
            return easing < 0 || easing > 2 ? 0 : easing;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
