using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>
    /// Pure timing arithmetic for the audio preprocess window. Everything in
    /// this class is a function of numbers and <see cref="Chart"/> data only:
    /// no Unity objects, no package I/O, no audio. That keeps the whole
    /// migration testable from <c>FalsumSelfCheck</c> without a scene.
    ///
    /// WHY THE SIGN IS NEGATED: the stored timeline is
    /// <c>musicTime = chartTime + delay</c>, so an existing chart with
    /// <c># delay: D</c> already carries D ms of lead-in at the head of the
    /// audio. To bake that lead-in into the audio and still keep every note
    /// aligned with the same audio content, the new audio has to start
    /// reading at source time D:
    /// <code>newAudio(t) = oldAudio(t + D)</code>
    /// which is an audio head trim of D ms when D &gt; 0, and a silent pad of
    /// -D ms when D &lt; 0. The window calls that number the
    /// "audio delay" <c>S</c>, and it is exactly <c>-D</c>.
    ///
    /// Getting this sign wrong shifts every note against the waveform by 2D
    /// (once from the pad/trim, once from the note shift), which is audible
    /// on any song and looks like "the whole chart drifted".
    /// </summary>
    public static class FalsumAudioPreprocessMath
    {
        /// <summary>
        /// Tolerance used when deciding whether two delays describe the same
        /// audio position. Mirrors <see cref="ChartTimeAxis"/>'s music-time
        /// tolerance rather than inventing a new one.
        /// </summary>
        public const double DelayToleranceMs = 0.0000005;

        /// <summary>
        /// Tolerance for "this bpm event is the base event written at time 0".
        /// </summary>
        public const double EventTimeToleranceMs = 0.0001;

        /// <summary>
        /// The audio delay for a chart whose stored delay is
        /// <paramref name="storedDelayMs"/>. Positive means "insert silence at
        /// the head", negative means "trim the head".
        /// </summary>
        public static double SilenceMsForDelay(double storedDelayMs)
        {
            return -storedDelayMs;
        }

        /// <summary>
        /// Inverse of <see cref="SilenceMsForDelay"/>. Used when the window
        /// reopens on a package that has not been preprocessed yet, so the
        /// selection can start at the value that would change nothing.
        /// </summary>
        public static double DelayForSilenceMs(double silenceMs)
        {
            return -silenceMs;
        }

        /// <summary>
        /// Length of the rewritten audio.
        /// <code>
        /// newDuration = oldDuration - S      (S &lt; 0 pads, S &gt; 0 trims)
        ///            = oldDuration + D
        /// </code>
        /// </summary>
        public static double NewAudioDurationMs(
            double oldAudioDurationMs,
            double silenceMs)
        {
            return oldAudioDurationMs - silenceMs;
        }

        /// <summary>
        /// The old stored delay that would produce
        /// <paramref name="newAudioDurationMs"/> from
        /// <paramref name="oldAudioDurationMs"/>. Inverse of
        /// <see cref="NewAudioDurationMs"/>; used to read back a package that
        /// was already preprocessed.
        /// </summary>
        public static double ImpliedDelayMs(
            double oldAudioDurationMs,
            double newAudioDurationMs)
        {
            return newAudioDurationMs - oldAudioDurationMs;
        }

        /// <summary>
        /// Source-time read position for a new-timeline position. While
        /// <c>t &lt; silenceMs</c> the audio has not started yet (the padded
        /// region), so the clamp keeps it at 0 instead of asking for a
        /// negative read.
        /// </summary>
        public static double SourceTimeAt(
            double newTimeMs,
            double silenceMs)
        {
            var sourceTimeMs = newTimeMs - silenceMs;
            return sourceTimeMs > 0.0 ? sourceTimeMs : 0.0;
        }

        /// <summary>
        /// True while the new timeline is still inside the padded region, i.e.
        /// the range where only beat cues play and the song has not started.
        /// </summary>
        public static bool IsInPadRegion(
            double newTimeMs,
            double silenceMs)
        {
            return silenceMs > 0.0 && newTimeMs < silenceMs;
        }

        /// <summary>
        /// Beat length in milliseconds. Kept separate from
        /// <see cref="Chart.Division"/> semantics: division here is the number
        /// of beats in a measure, which is what the window's "拍数" field means.
        /// </summary>
        public static double BeatDurationMs(double bpm)
        {
            if (!IsFinitePositive(bpm))
            {
                return 0.0;
            }
            return 60000.0 / bpm;
        }

        /// <summary>
        /// True when beat <paramref name="beatIndex"/> starts a measure. The
        /// first beat of the song is beat 0, so a chart with 4 beats per
        /// measure puts a downbeat at 0, 4, 8, ...
        /// </summary>
        public static bool IsMeasureFirstBeat(
            int beatIndex,
            int beatsPerMeasure)
        {
            if (beatsPerMeasure < 1)
            {
                beatsPerMeasure = 1;
            }
            var index = beatIndex % beatsPerMeasure;
            if (index < 0)
            {
                index += beatsPerMeasure;
            }
            return index == 0;
        }

        /// <summary>
        /// Index of the first beat at or after <paramref name="timeMs"/>.
        /// Negative when the beat length is unusable.
        /// </summary>
        public static int FirstBeatAtOrAfter(
            double timeMs,
            double beatDurationMs)
        {
            if (!(beatDurationMs > 0.0))
            {
                return -1;
            }
            var raw = timeMs / beatDurationMs;
            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                return -1;
            }
            if (raw < 0.0)
            {
                return 0;
            }
            var index = (int)Math.Ceiling(raw - 1e-9);
            return index > 0 ? index : 0;
        }

        /// <summary>
        /// Fills <paramref name="destination"/> with beat indices visible in
        /// <c>[startMs, endMs]</c> at most <paramref name="maxCount"/> of them.
        /// Writing into a caller-owned list keeps the per-frame waveform draw
        /// allocation-free.
        /// </summary>
        public static int CollectVisibleBeats(
            double startMs,
            double endMs,
            double beatDurationMs,
            int maxCount,
            List<int> destination)
        {
            if (destination == null)
            {
                return 0;
            }
            destination.Clear();
            if (!(beatDurationMs > 0.0) ||
                !(endMs > startMs) ||
                maxCount <= 0)
            {
                return 0;
            }
            var first = FirstBeatAtOrAfter(startMs, beatDurationMs);
            if (first < 0)
            {
                return 0;
            }
            // Guard the loop against a caller passing a degenerate range. A
            // 3-hour song at 1000 BPM is still under 200k beats, and the
            // visible cap is far below that, so this bound only ever fires on
            // bad input.
            var hardLimit = maxCount * 4;
            var count = 0;
            for (var index = first; count < maxCount && count < hardLimit; index++)
            {
                var timeMs = index * beatDurationMs;
                if (timeMs > endMs + 1e-9)
                {
                    break;
                }
                destination.Add(index);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Shifts every note and every non-base event of
        /// <paramref name="chart"/> by <paramref name="deltaMs"/>. Note
        /// durations, easings, group ids and multipliers are untouched: only
        /// absolute times move.
        ///
        /// The derived event lists (<see cref="Chart.SpeedEvents"/> and so on)
        /// alias the same <see cref="ChartEvent"/> objects as
        /// <see cref="Chart.Events"/>, so shifting events is enough; but the
        /// chart must be re-sorted afterwards with
        /// <see cref="Chart.RebuildTimeline"/> because a shift can reorder two
        /// events that were only ordered by their old times.
        /// </summary>
        public static void ShiftChartTimes(Chart chart, double deltaMs)
        {
            if (chart == null ||
                Math.Abs(deltaMs) <= DelayToleranceMs)
            {
                return;
            }
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                if (note == null)
                {
                    continue;
                }
                note.TimeMs += deltaMs;
            }
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var evt = chart.Events[i];
                if (evt == null || evt.IsChartBase)
                {
                    continue;
                }
                evt.TimeMs += deltaMs;
            }
        }

        /// <summary>
        /// Delta that moves difficulty <paramref name="difficultyDelayMs"/>
        /// onto the timeline the rewrite was baked against, whose reference
        /// delay is <paramref name="referenceDelayMs"/>.
        ///
        /// THE SIGN IS reference MINUS difficulty, and getting it backwards
        /// silently shifts a whole difficulty against the music.
        ///
        /// Reason it out from the audio, not from the field names: the
        /// rewritten audio has its head shifted by the reference delay, so a
        /// chart that used to sit <c>D_ref</c> ms into the old audio now sits
        /// at new-chart-time 0. A difficulty that sat <c>D_k</c> ms into the
        /// SAME old audio must therefore move by <c>D_ref - D_k</c> to keep
        /// landing on the same music:
        ///
        ///   D_ref = 60, D_k = 0  ->  old audio pos for D_k's chart-0 was 0,
        ///                            which on the new timeline is -60,
        ///                            so its notes move +60.
        ///
        /// With the previous <c>D_k - D_ref</c> a difficulty whose delay was 0
        /// while the reference was 60 was shifted by -60: every note in that
        /// difficulty arrived 120 ms early against the song.
        /// </summary>
        public static double AlignmentDeltaMs(
            double difficultyDelayMs,
            double referenceDelayMs)
        {
            return referenceDelayMs - difficultyDelayMs;
        }

        /// <summary>
        /// Removes the bpm event that a stored delay used to imply: an event at
        /// chart time 0 whose bpm/division equal the chart base. Leaving it in
        /// place while the delay becomes 0 makes the file gain one duplicate
        /// event per save — the exact failure mode documented in
        /// <c>docs/memory/02-invariants.md</c> §A-5.
        ///
        /// Returns how many events were removed.
        /// </summary>
        public static int RemoveRedundantBaseBpmEvents(Chart chart)
        {
            if (chart == null)
            {
                return 0;
            }
            var removed = 0;
            for (var i = chart.Events.Count - 1; i >= 0; i--)
            {
                var evt = chart.Events[i];
                if (evt == null ||
                    evt.IsChartBase ||
                    evt.Kind != ChartEventKind.Bpm)
                {
                    continue;
                }
                if (Math.Abs(evt.TimeMs) > EventTimeToleranceMs)
                {
                    continue;
                }
                if (Math.Abs(evt.Bpm - chart.Bpm) > DelayToleranceMs ||
                    Math.Abs(evt.Division - chart.Division) >
                    DelayToleranceMs)
                {
                    continue;
                }
                chart.Events.RemoveAt(i);
                chart.BpmEvents.Remove(evt);
                removed++;
            }
            if (removed > 0)
            {
                // Chart.RebuildTimeline sorts but never repopulates the kind
                // lists — that is FalsumChartSession's job. A caller that
                // mutates a bare chart (a fixture, or the pre-validation copy
                // built by the editor) would otherwise leave BpmEvents
                // describing the pre-cleanup list, and BpmAt would keep
                // answering with the removed event.
                RebuildEventKindLists(chart);
            }
            return removed;
        }

        /// <summary>
        /// Re-derives <see cref="Chart.SpeedEvents"/>,
        /// <see cref="Chart.BpmEvents"/> and <see cref="Chart.LaneEvents"/> from
        /// <see cref="Chart.Events"/>. Mirrors
        /// <c>FalsumChartSession.RebuildEventKindLists</c>; needed here because
        /// the geometry-free helpers in this class can be pointed at a chart
        /// that no session owns.
        /// </summary>
        public static void RebuildEventKindLists(Chart chart)
        {
            if (chart == null)
            {
                return;
            }
            chart.SpeedEvents.Clear();
            chart.BpmEvents.Clear();
            chart.LaneEvents.Clear();
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var evt = chart.Events[i];
                if (evt == null || evt.IsChartBase)
                {
                    continue;
                }
                if (evt.Kind == ChartEventKind.TrackSpeed)
                {
                    chart.SpeedEvents.Add(evt);
                }
                else if (evt.Kind == ChartEventKind.Bpm)
                {
                    chart.BpmEvents.Add(evt);
                }
                else if (evt.Kind == ChartEventKind.LaneLayout)
                {
                    chart.LaneEvents.Add(evt);
                }
            }
        }

        /// <summary>
        /// Earliest chart time across notes and non-base events. Returns
        /// <see cref="double.PositiveInfinity"/> for an empty chart.
        /// </summary>
        public static double EarliestChartTimeMs(Chart chart)
        {
            if (chart == null)
            {
                return double.PositiveInfinity;
            }
            var earliest = double.PositiveInfinity;
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                if (note == null)
                {
                    continue;
                }
                if (note.TimeMs < earliest)
                {
                    earliest = note.TimeMs;
                }
            }
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var evt = chart.Events[i];
                if (evt == null || evt.IsChartBase)
                {
                    continue;
                }
                if (evt.TimeMs < earliest)
                {
                    earliest = evt.TimeMs;
                }
            }
            return earliest;
        }

        /// <summary>
        /// Reasons the window must refuse to apply. Kept as enum values rather
        /// than strings so the view can map each one to a localized message;
        /// the localization key is supplied by the caller.
        /// </summary>
        public enum ValidationResult
        {
            Ok = 0,
            NotFiniteDelay,
            NotFiniteBpm,
            NonPositiveBpm,
            NonPositiveBeatsPerMeasure,
            NonPositiveSourceDuration,
            NonPositiveResultDuration,
            NegativeMusicTime,
        }

        /// <summary>
        /// Checks the three window parameters against the source audio length
        /// and the baseline chart. Charts whose stored delay differs from the
        /// baseline are included by
        /// <see cref="ValidateApplication(Chart, double, double, double, double)"/>'s
        /// caller only after shifting, so this overload takes the already
        /// shifted chart.
        /// </summary>
        public static ValidationResult ValidateApplication(
            double bpm,
            double beatsPerMeasure,
            double silenceMs,
            double sourceAudioDurationMs,
            Chart shiftedChart)
        {
            if (double.IsNaN(silenceMs) ||
                double.IsInfinity(silenceMs))
            {
                return ValidationResult.NotFiniteDelay;
            }
            if (!IsFinitePositive(bpm))
            {
                return double.IsNaN(bpm) || double.IsInfinity(bpm)
                    ? ValidationResult.NotFiniteBpm
                    : ValidationResult.NonPositiveBpm;
            }
            if (!IsFinitePositive(beatsPerMeasure))
            {
                return ValidationResult.NonPositiveBeatsPerMeasure;
            }
            if (!(sourceAudioDurationMs > 0.0) ||
                double.IsNaN(sourceAudioDurationMs) ||
                double.IsInfinity(sourceAudioDurationMs))
            {
                return ValidationResult.NonPositiveSourceDuration;
            }
            var newDuration =
                NewAudioDurationMs(sourceAudioDurationMs, silenceMs);
            if (!(newDuration > 0.0) ||
                double.IsNaN(newDuration) ||
                double.IsInfinity(newDuration))
            {
                return ValidationResult.NonPositiveResultDuration;
            }
            if (shiftedChart != null &&
                ChartTimeAxis.ContainsNegativeMusicTime(shiftedChart, 0.0))
            {
                return ValidationResult.NegativeMusicTime;
            }
            return ValidationResult.Ok;
        }

        /// <summary>
        /// Localization keys for each validation result, so no caller has to
        /// invent UI text for a failure (see
        /// <c>docs/memory/02-invariants.md</c> §C-1). <see cref="ValidationResult.Ok"/>
        /// maps to an empty key and must not be rendered.
        /// </summary>
        public static string ValidationErrorKey(ValidationResult result)
        {
            switch (result)
            {
                case ValidationResult.NotFiniteDelay:
                    return "falsum.audioPreprocess.error.delayNotFinite";
                case ValidationResult.NotFiniteBpm:
                    return "falsum.audioPreprocess.error.bpmNotFinite";
                case ValidationResult.NonPositiveBpm:
                    return "falsum.audioPreprocess.error.bpmNotPositive";
                case ValidationResult.NonPositiveBeatsPerMeasure:
                    return "falsum.audioPreprocess.error.beatsNotPositive";
                case ValidationResult.NonPositiveSourceDuration:
                    return "falsum.audioPreprocess.error.sourceAudioMissing";
                case ValidationResult.NonPositiveResultDuration:
                    return "falsum.audioPreprocess.error.resultAudioTooShort";
                case ValidationResult.NegativeMusicTime:
                    return "falsum.audioPreprocess.error.negativeMusicTime";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Human-readable validation message, already localized.
        /// </summary>
        public static string ValidationErrorMessage(
            ValidationResult result)
        {
            if (result == ValidationResult.Ok)
            {
                return string.Empty;
            }
            var key = ValidationErrorKey(result);
            return string.IsNullOrEmpty(key) ? string.Empty : L.Get(key);
        }

        public static bool IsFinitePositive(double value)
        {
            return value > 0.0 &&
                   !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        // ------------------------------------------------------------------
        // Difficulty text migration
        //
        // The audio rewrite itself happens elsewhere (BASS, on a worker). What
        // remains here is the part that must be identical for every difficulty
        // and therefore belongs in one pure function: parse the stored text,
        // shift its chart times onto the single new audio timeline, drop the
        // bpm event the old delay implied, and write it back with delay 0.
        // ------------------------------------------------------------------

        /// <summary>
        /// Migrates an IN-MEMORY chart onto the post-preprocess timeline,
        /// without a text round trip.
        ///
        /// THE TEXT ROUND TRIP WAS A BUG, NOT A SHORTCUT. Applying used to
        /// serialise the current difficulty with its OLD delay first
        /// (<c>ChartWriter.Write(chart, delayMs, ...)</c>) and then re-parse it.
        /// <c>ChartWriter</c> refuses any chart that would contain negative
        /// music time, and a negative-delay chart is exactly that: it writes
        /// the derived <c>track(-60,1.00)</c> and <c>bpm(-60,...)</c> headline
        /// events, which are negative. So opening a package whose delay was
        /// negative and pressing Apply threw "谱面内容包含负数音乐时间" — the
        /// editor's own known-unsavable case, dragged into the migration.
        ///
        /// Mutating the chart object directly sidesteps that entirely: no
        /// intermediate file is ever produced, so no negative headline can be
        /// written. The result is then serialised once, with delay 0, which by
        /// definition has no negative music time.
        ///
        /// <paramref name="referenceDelayMs"/> is the delay the audio was baked
        /// against; the chart is moved by
        /// <see cref="AlignmentDeltaMs"/> so it keeps landing on the same
        /// music.
        /// </summary>
        public static TextMigrationResult MigrateChartInPlace(
            Chart chart,
            double referenceDelayMs)
        {
            if (chart == null)
            {
                return TextMigrationResult.ParseFailed;
            }
            var delta = AlignmentDeltaMs(
                chart.DelayMs,
                referenceDelayMs);
            ShiftChartTimes(chart, delta);
            RemoveRedundantBaseBpmEvents(chart);
            chart.DelayMs = 0.0;
            // Re-sort: a shift can reorder two events that were only ordered by
            // their old timestamps.
            chart.RebuildTimeline();
            if (ChartTimeAxis.ContainsNegativeMusicTime(chart, 0.0))
            {
                return TextMigrationResult.NegativeMusicTime;
            }
            return TextMigrationResult.Ok;
        }

        /// <summary>
        /// Why a difficulty's text could not be migrated. Distinct from
        /// <see cref="ValidationResult"/> because a parse failure points at the
        /// stored file, not at the window parameters.
        /// </summary>
        public enum TextMigrationResult
        {
            Ok = 0,
            ParseFailed,
            NegativeMusicTime,
            WriteFailed,
        }

        /// <summary>
        /// Rewrites one difficulty's chart text for the post-preprocess
        /// timeline. Returns null when the result is not
        /// <see cref="TextMigrationResult.Ok"/>.
        ///
        /// <paramref name="referenceDelayMs"/> is the stored delay of the
        /// difficulty the audio was baked against; every other difficulty is
        /// shifted by <c>D_k - D_ref</c> so all four line up on the one audio.
        /// </summary>
        public static string ProcessDifficultyText(
            string sourceText,
            int difficulty,
            string songId,
            double referenceDelayMs,
            out TextMigrationResult result)
        {
            result = TextMigrationResult.ParseFailed;
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                return null;
            }
            ChartParseResult parsed;
            try
            {
                parsed = ChartParser.Parse(
                    sourceText.Replace("\r\n", "\n").Split('\n'),
                    "<audio-preprocess:" +
                    difficulty.ToString() +
                    ">",
                    difficulty);
            }
            catch (Exception)
            {
                return null;
            }
            if (parsed == null || !parsed.Success || parsed.Chart == null)
            {
                return null;
            }
            var chart = parsed.Chart;
            var delta = AlignmentDeltaMs(
                chart.DelayMs,
                referenceDelayMs);
            ShiftChartTimes(chart, delta);
            RemoveRedundantBaseBpmEvents(chart);
            // Re-sort after the shift: two events whose order depended on
            // their old times can be out of order now.
            chart.RebuildTimeline();
            if (ChartTimeAxis.ContainsNegativeMusicTime(chart, 0.0))
            {
                result = TextMigrationResult.NegativeMusicTime;
                return null;
            }
            try
            {
                var text = ChartWriter.Write(
                    chart,
                    0.0,
                    songId,
                    difficulty);
                result = TextMigrationResult.Ok;
                return text;
            }
            catch (Exception)
            {
                result = TextMigrationResult.WriteFailed;
                return null;
            }
        }

        /// <summary>
        /// Localized message for a text migration failure.
        /// </summary>
        public static string TextMigrationErrorKey(
            TextMigrationResult result)
        {
            switch (result)
            {
                case TextMigrationResult.ParseFailed:
                    return "falsum.audioPreprocess.error.difficultyUnparsable";
                case TextMigrationResult.NegativeMusicTime:
                    return "falsum.audioPreprocess.error.difficultyNegativeTime";
                case TextMigrationResult.WriteFailed:
                    return "falsum.audioPreprocess.error.difficultyWriteFailed";
                default:
                    return string.Empty;
            }
        }
    }
}
