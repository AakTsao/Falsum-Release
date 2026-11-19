using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace InFalsus.EditorTools
{
    /// <summary>
    /// Self-check for the audio preprocess window. Split into its own partial
    /// file so the 3800-line FalsumSelfCheck.cs stays navigable (its size is
    /// already flagged as technical debt in docs/memory/05-gaps.md §G-7).
    /// </summary>
    public static partial class FalsumSelfCheckAudioPreprocess
    {
        private const string AudioPreprocessDialogPath =
            "Falsum/UI/FalsumAudioPreprocessDialog";
        private const string AudioPreprocessStylePath =
            "Falsum/UI/FalsumAudioPreprocess";

        public static void Run(List<string> checks)
        {
            CheckDelaySilenceMapping();
            CheckAudioDurationMapping();
            CheckBeatGrid();
            CheckMeasureFirstBeatClassification();
            CheckChartTimeShift();
            CheckRedundantBpmCleanup();
            CheckValidation();
            CheckDifficultyTextMigration();
            CheckNegativeDelayMigration();
            CheckInPlacePackageSave();
            CheckUiResources();
            CheckLocalizationKeys();
            checks.Add("audio_preprocess");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        // -----------------------------------------------------------------
        // The sign of the mapping is the single most expensive thing to get
        // wrong in this feature: S = -D. Flipping it shifts every note against
        // the audio by 2D. These assertions pin both directions.
        // -----------------------------------------------------------------

        private static void CheckDelaySilenceMapping()
        {
            Require(
                Math.Abs(FalsumAudioPreprocessMath.SilenceMsForDelay(60.0) + 60.0) < 1e-9,
                "A stored delay of +60 ms must become an audio delay of -60 ms (head trim).");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.SilenceMsForDelay(-60.0) - 60.0) < 1e-9,
                "A stored delay of -60 ms must become an audio delay of +60 ms (silent pad).");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.SilenceMsForDelay(0.0)) < 1e-9,
                "A stored delay of 0 must not move the audio.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.DelayForSilenceMs(
                    FalsumAudioPreprocessMath.SilenceMsForDelay(125.5)) - 125.5) < 1e-9,
                "Silence-to-delay is not the inverse of delay-to-silence.");
        }

        private static void CheckAudioDurationMapping()
        {
            Require(
                Math.Abs(FalsumAudioPreprocessMath.NewAudioDurationMs(180000.0, -60.0) - 180060.0) < 1e-9,
                "A +60 ms pad must lengthen the audio by 60 ms.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.NewAudioDurationMs(180000.0, 60.0) - 179940.0) < 1e-9,
                "A -60 ms trim must shorten the audio by 60 ms.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.ImpliedDelayMs(180000.0, 180060.0) - 60.0) < 1e-9,
                "Implied delay must recover the -silence it was baked from.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.SourceTimeAt(0.0, 60.0)) < 1e-9,
                "Inside the pad region the source read must clamp to 0, not go negative.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.SourceTimeAt(100.0, 60.0) - 40.0) < 1e-9,
                "Source time must be newTime - silence.");
            Require(
                FalsumAudioPreprocessMath.IsInPadRegion(30.0, 60.0) &&
                !FalsumAudioPreprocessMath.IsInPadRegion(60.0, 60.0),
                "The pad region must end exactly at the silence boundary.");
            Require(
                !FalsumAudioPreprocessMath.IsInPadRegion(10.0, -60.0),
                "A head trim must not report a pad region.");

            // The waveform samples the envelope on the NEW timeline, so this
            // conversion is what makes typing a delay visibly move the wave.
            // The original code sampled with raw t, which repainted only the
            // delay block and left the waveform identical — the user-visible
            // symptom "填写延迟后波形图没有反映延迟".
            Require(
                Math.Abs(FalsumAudioPreprocessWaveform.SourceTimeMsFor(1000.0, 0.0) - 1000.0) < 1e-9,
                "With no delay the source time must equal the new time.");
            Require(
                Math.Abs(FalsumAudioPreprocessWaveform.SourceTimeMsFor(1000.0, 60.0) - 940.0) < 1e-9,
                "A +60 ms pad must read 60 ms EARLIER in the source at the same new time.");
            Require(
                Math.Abs(FalsumAudioPreprocessWaveform.SourceTimeMsFor(1000.0, -60.0) - 1060.0) < 1e-9,
                "A -60 ms trim must read 60 ms LATER in the source at the same new time.");
            Require(
                FalsumAudioPreprocessWaveform.SourceTimeMsFor(30.0, 60.0) < 0.0,
                "Inside the padded head the source time must be negative, i.e. no audio to draw.");
        }

        private static void CheckBeatGrid()
        {
            Require(
                Math.Abs(FalsumAudioPreprocessMath.BeatDurationMs(120.0) - 500.0) < 1e-9,
                "120 BPM must be a 500 ms beat.");
            Require(
                FalsumAudioPreprocessMath.BeatDurationMs(0.0) == 0.0 &&
                FalsumAudioPreprocessMath.BeatDurationMs(double.NaN) == 0.0,
                "An unusable BPM must produce a zero beat length instead of infinity.");
            Require(
                FalsumAudioPreprocessMath.FirstBeatAtOrAfter(-50.0, 500.0) == 0,
                "A negative start time must snap to beat 0.");
            Require(
                FalsumAudioPreprocessMath.FirstBeatAtOrAfter(0.0, 500.0) == 0 &&
                FalsumAudioPreprocessMath.FirstBeatAtOrAfter(500.0, 500.0) == 1 &&
                FalsumAudioPreprocessMath.FirstBeatAtOrAfter(501.0, 500.0) == 2,
                "First-beat lookup must be inclusive of the boundary and skip strictly past it.");

            var beats = new List<int>();
            var count = FalsumAudioPreprocessMath.CollectVisibleBeats(
                1000.0,
                3000.0,
                500.0,
                64,
                beats);
            // Both edges are included because a beat landing exactly on the
            // right edge is still visible as a line at x = width. A window
            // (1000, 3000] at a 500 ms beat therefore holds 1000/1500/2000/
            // 2500/3000 — five lines, indices 2..6.
            Require(count == 5, "A 2 s window at 120 BPM must contain 5 visible beats, got " + count + ".");
            Require(beats[0] == 2 && beats[count - 1] == 6, "Visible beats must be 2..6.");
            Require(
                FalsumAudioPreprocessMath.CollectVisibleBeats(0.0, 0.0, 500.0, 64, beats) == 0,
                "An empty range must yield no beats.");
            Require(
                FalsumAudioPreprocessMath.CollectVisibleBeats(0.0, 3e9, 500.0, 8, beats) == 8,
                "Beat collection must honour the visible cap so a long song cannot emit millions of lines.");
        }

        private static void CheckMeasureFirstBeatClassification()
        {
            Require(
                FalsumAudioPreprocessMath.IsMeasureFirstBeat(0, 4) &&
                FalsumAudioPreprocessMath.IsMeasureFirstBeat(4, 4) &&
                FalsumAudioPreprocessMath.IsMeasureFirstBeat(8, 4),
                "Beat 0/4/8 must be measure downbeats at 4 beats per measure.");
            Require(
                !FalsumAudioPreprocessMath.IsMeasureFirstBeat(1, 4) &&
                !FalsumAudioPreprocessMath.IsMeasureFirstBeat(3, 4),
                "Non-downbeat indices must not be classified as measure starts.");
            Require(
                FalsumAudioPreprocessMath.IsMeasureFirstBeat(0, 1) &&
                FalsumAudioPreprocessMath.IsMeasureFirstBeat(5, 0),
                "A degenerate beats-per-measure must behave as 1 instead of dividing by zero.");
            Require(
                !FalsumAudioPreprocessMath.IsMeasureFirstBeat(1, 3) &&
                FalsumAudioPreprocessMath.IsMeasureFirstBeat(3, 3),
                "A 3-beat measure must put downbeats on multiples of 3.");
        }

        private static void CheckChartTimeShift()
        {
            // THE CROSS-DIFFICULTY SIGN, worked out from the audio rather than
            // from the parameter names. The rewrite shifts the audio head by
            // the REFERENCE delay, so a difficulty that used to sit D_k ms into
            // the old audio has to move by (reference - D_k) to still land on
            // the same music.
            Require(
                Math.Abs(FalsumAudioPreprocessMath.AlignmentDeltaMs(0.0, 60.0) - 60.0) < 1e-9,
                "A difficulty with delay 0 baked against reference 60 must move +60 ms, " +
                "not -60: the audio head was trimmed, so its old chart-0 is now 60 ms " +
                "earlier on the new timeline.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.AlignmentDeltaMs(60.0, 60.0)) < 1e-9,
                "The reference difficulty itself must not move.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.AlignmentDeltaMs(120.0, 60.0) + 60.0) < 1e-9,
                "A difficulty with a LARGER delay than the reference must move earlier.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.AlignmentDeltaMs(-60.0, 60.0) - 120.0) < 1e-9,
                "A difficulty with a negative delay must move later by the full span.");

            var chart = BuildShiftFixture();
            var trackEvent = FindEvent(
                chart,
                ChartEventKind.TrackSpeed);
            var baseEvent = FindEvent(
                chart,
                ChartEventKind.Bpm);
            Require(
                trackEvent != null && baseEvent != null,
                "The shift fixture must contain one track and one base bpm event.");
            var earliestBefore = FalsumAudioPreprocessMath.EarliestChartTimeMs(chart);
            FalsumAudioPreprocessMath.ShiftChartTimes(chart, 60.0);
            Require(
                Math.Abs(chart.Notes[0].TimeMs - 940.0) < 1e-6,
                "Shift must move note times by the delta.");
            Require(
                Math.Abs(trackEvent.TimeMs - 1000.0) < 1e-6,
                "Shift must move non-base event times by the delta.");
            Require(
                Math.Abs(baseEvent.TimeMs) < 1e-6,
                "Shift must leave the chart-base event at time 0.");
            Require(
                Math.Abs(chart.Notes[0].DurationMs - 250.0) < 1e-6,
                "Shift must not resize holds; only absolute times move.");
            Require(
                Math.Abs(trackEvent.Multiplier - 1.5) < 1e-6,
                "Shift must not touch multipliers.");
            Require(
                Math.Abs(FalsumAudioPreprocessMath.EarliestChartTimeMs(chart) - (earliestBefore + 60.0)) < 1e-6,
                "Earliest-time lookup must follow the shift.");

            FalsumAudioPreprocessMath.ShiftChartTimes(chart, 0.0);
            Require(
                Math.Abs(chart.Notes[0].TimeMs - 940.0) < 1e-6,
                "A zero delta must be a no-op.");
        }

        private static ChartEvent FindEvent(
            Chart chart,
            ChartEventKind kind)
        {
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var evt = chart.Events[i];
                if (evt != null && evt.Kind == kind)
                {
                    return evt;
                }
            }
            return null;
        }

        private static Chart BuildShiftFixture()
        {
            var chart = new Chart
            {
                Bpm = 120.0,
                Division = 4.0,
                DelayMs = 60.0,
            };
            chart.Notes.Add(new ChartNote
            {
                Kind = NoteKind.Hold,
                TimeMs = 880.0,
                DurationMs = 250.0,
            });
            chart.Events.Add(new ChartEvent
            {
                Kind = ChartEventKind.TrackSpeed,
                TimeMs = 940.0,
                Multiplier = 1.5,
            });
            chart.Events.Add(new ChartEvent
            {
                Kind = ChartEventKind.Bpm,
                IsChartBase = true,
                TimeMs = 0.0,
                Bpm = 120.0,
                Division = 4.0,
            });
            chart.RebuildTimeline();
            return chart;
        }

        // -----------------------------------------------------------------
        // Leaving the delay-implied bpm event behind is exactly the §A-5 trap:
        // every save then writes one more duplicate. The cleaner must remove
        // only the event the old delay produced.
        // -----------------------------------------------------------------

        private static void CheckRedundantBpmCleanup()
        {
            var chart = new Chart
            {
                Bpm = 129.0,
                Division = 4.0,
            };
            var redundant = new ChartEvent
            {
                Kind = ChartEventKind.Bpm,
                TimeMs = 0.0,
                Bpm = 129.0,
                Division = 4.0,
            };
            var meaningful = new ChartEvent
            {
                Kind = ChartEventKind.Bpm,
                TimeMs = 2400.0,
                Bpm = 140.0,
                Division = 4.0,
            };
            chart.Events.Add(redundant);
            chart.Events.Add(meaningful);
            chart.RebuildTimeline();
            FalsumAudioPreprocessMath.RebuildEventKindLists(chart);

            var removed = FalsumAudioPreprocessMath.RemoveRedundantBaseBpmEvents(chart);
            Require(removed == 1, "Exactly one redundant base bpm event must be removed, got " + removed + ".");
            Require(
                chart.Events.Count == 1 && chart.Events[0] == meaningful,
                "The meaningful bpm event must survive cleanup.");
            Require(
                chart.BpmEvents.Count == 1 && chart.BpmEvents[0] == meaningful,
                "The derived bpm list must drop the removed event too; a split list breaks BpmAt.");

            var again = FalsumAudioPreprocessMath.RemoveRedundantBaseBpmEvents(chart);
            Require(again == 0, "Cleanup must be idempotent.");
        }

        private static void CheckValidation()
        {
            var chart = new Chart { Bpm = 120.0, Division = 4.0 };
            chart.Notes.Add(new ChartNote { Kind = NoteKind.Tap, TimeMs = 100.0 });
            chart.RebuildTimeline();

            Require(
                FalsumAudioPreprocessMath.ValidateApplication(120.0, 4.0, -60.0, 180000.0, chart) ==
                FalsumAudioPreprocessMath.ValidationResult.Ok,
                "A trim that shortens the audio must validate.");
            Require(
                FalsumAudioPreprocessMath.ValidateApplication(120.0, 4.0, 200000.0, 180000.0, chart) ==
                FalsumAudioPreprocessMath.ValidationResult.NonPositiveResultDuration,
                "A trim longer than the song must be rejected.");
            Require(
                FalsumAudioPreprocessMath.ValidateApplication(double.NaN, 4.0, 0.0, 180000.0, chart) ==
                FalsumAudioPreprocessMath.ValidationResult.NotFiniteBpm,
                "A non-finite BPM must be rejected.");
            Require(
                FalsumAudioPreprocessMath.ValidateApplication(-120.0, 4.0, 0.0, 180000.0, chart) ==
                FalsumAudioPreprocessMath.ValidationResult.NonPositiveBpm,
                "A non-positive BPM must be rejected.");
            Require(
                FalsumAudioPreprocessMath.ValidateApplication(120.0, 0.0, 0.0, 180000.0, chart) ==
                FalsumAudioPreprocessMath.ValidationResult.NonPositiveBeatsPerMeasure,
                "A non-positive beats-per-measure must be rejected.");
            Require(
                FalsumAudioPreprocessMath.ValidateApplication(120.0, 4.0, double.NaN, 180000.0, chart) ==
                FalsumAudioPreprocessMath.ValidationResult.NotFiniteDelay,
                "A non-finite audio delay must be rejected.");

            var negative = new Chart { Bpm = 120.0, Division = 4.0 };
            negative.Notes.Add(new ChartNote { Kind = NoteKind.Tap, TimeMs = -40.0 });
            negative.RebuildTimeline();
            Require(
                FalsumAudioPreprocessMath.ValidateApplication(120.0, 4.0, 0.0, 180000.0, negative) ==
                FalsumAudioPreprocessMath.ValidationResult.NegativeMusicTime,
                "A chart that would start before the audio must be rejected.");

            Require(
                FalsumAudioPreprocessMath.ValidationErrorKey(FalsumAudioPreprocessMath.ValidationResult.Ok).Length == 0,
                "A successful validation must not map to an error key.");
            Require(
                FalsumAudioPreprocessMath.ValidationErrorKey(FalsumAudioPreprocessMath.ValidationResult.NegativeMusicTime).StartsWith("falsum.audioPreprocess.error."),
                "Validation error keys must live under the audio-preprocess namespace.");
        }

        // -----------------------------------------------------------------
        // The migration is what makes the feature "replace the delay" instead
        // of "add another offset": after it, delay is 0 and the text carries no
        // derived track/bpm line (otherwise the save loop of §A-5 starts).
        // -----------------------------------------------------------------

        private static void CheckDifficultyTextMigration()
        {
            var source =
                "# song: fixture\n" +
                "# difficulty: 0\n" +
                "# delay: 60\n" +
                "chart(120,4)\n" +
                "track(60,1.00)\n" +
                "bpm(60,120,4)\n" +
                "track(1000,2.00)\n" +
                "tap(560,1,1)\n";

            var migrated = FalsumAudioPreprocessMath.ProcessDifficultyText(
                source,
                0,
                "fixture",
                60.0,
                out var result);
            Require(
                result == FalsumAudioPreprocessMath.TextMigrationResult.Ok,
                "Migrating a well-formed delayed chart must succeed, got " + result + ".");
            Require(migrated != null, "Migration must return text on success.");
            Require(
                migrated.IndexOf("# delay: 0", StringComparison.Ordinal) >= 0,
                "Migrated text must store delay 0.");
            Require(
                migrated.IndexOf("track(60,1.00)", StringComparison.Ordinal) < 0,
                "Migrated text must not keep the delay-implied track marker (§A-5).");

            var reparsed = ChartParser.Parse(
                migrated.Replace("\r\n", "\n").Split('\n'),
                "<audio-preprocess-roundtrip>",
                0);
            Require(reparsed.Success, "Migrated text must parse.");
            Require(
                Math.Abs(reparsed.Chart.DelayMs) < 1e-6,
                "Migrated text must read back with delay 0.");
            Require(
                Math.Abs(reparsed.Chart.Notes[0].TimeMs - 500.0) < 1e-6,
                "The tap at music time 560 with delay 60 must land at chart time 500, got " + reparsed.Chart.Notes[0].TimeMs + ".");
            Require(
                CountOccurrences(migrated, "bpm(") <= 1,
                "Migrated text must not contain a duplicate base bpm event.");

            var second = FalsumAudioPreprocessMath.ProcessDifficultyText(
                migrated,
                0,
                "fixture",
                0.0,
                out var secondResult);
            Require(
                secondResult == FalsumAudioPreprocessMath.TextMigrationResult.Ok,
                "Re-migrating an already-migrated chart must succeed.");
            Require(
                CountOccurrences(second, "bpm(") == CountOccurrences(migrated, "bpm("),
                "Re-migrating must not grow the bpm event count (§A-5).");
            // The stored track event sits at music time 1000 with delay 60, i.e.
            // chart time 940. The migration keeps chart times (the audio was
            // rewritten to match them) and re-emits them with delay 0.
            // ChartWriter prints multipliers with its own number formatter, so
            // 2.0 is written as "2", not "2.00".
            Require(
                second.IndexOf("track(940,2)", StringComparison.Ordinal) >= 0,
                "A real track event must survive migration at its chart time.");

            var broken = FalsumAudioPreprocessMath.ProcessDifficultyText(
                "tap(100,1,1)\n",
                0,
                "fixture",
                0.0,
                out var brokenResult);
            Require(
                brokenResult == FalsumAudioPreprocessMath.TextMigrationResult.ParseFailed,
                "A chart without chart(...) must fail as unparsable, got " + brokenResult + ".");

            // Sign check through the migration entry point. With the corrected
            // delta (reference - difficulty), a difficulty whose own delay is
            // GREATER than the reference moves EARLIER, so a chart starting at
            // 10 ms under reference 0 and difficulty 60 lands at -50 and must
            // be refused.
            var negative = FalsumAudioPreprocessMath.ProcessDifficultyText(
                "# delay: 60\nchart(120,4)\ntap(10,1,1)\n",
                0,
                "fixture",
                0.0,
                out var negativeResult);
            Require(
                negativeResult == FalsumAudioPreprocessMath.TextMigrationResult.NegativeMusicTime,
                "Shifting a chart earlier than the audio must be rejected, got " +
                negativeResult + ".");

            // The mirror case must be ACCEPTED: a difficulty with delay 0 baked
            // against reference 60 moves +60 and stays positive.
            var positive = FalsumAudioPreprocessMath.ProcessDifficultyText(
                "# delay: 0\nchart(120,4)\ntap(10,1,1)\n",
                0,
                "fixture",
                60.0,
                out var positiveResult);
            Require(
                positiveResult == FalsumAudioPreprocessMath.TextMigrationResult.Ok,
                "A difficulty baked against a larger reference must shift later " +
                "and stay valid, got " + positiveResult + ".");
            Require(
                positive != null &&
                positive.IndexOf("tap(70,1,1)", StringComparison.Ordinal) >= 0,
                "A note at 10 ms must become 70 ms when the delta is +60; got:\n" +
                positive);
        }

        private static int CountOccurrences(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
            {
                return 0;
            }
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return count;
                }
                count++;
                index += needle.Length;
            }
        }

        private static void CheckUiResources()
        {
            var template = Resources.Load<VisualTreeAsset>(
                AudioPreprocessDialogPath);
            Require(template != null, "Audio preprocess dialog template is missing.");
            var instance = template.Instantiate();

            Require(
                instance.Q<VisualElement>("AudioPreprocessOverlay") != null &&
                instance.Q<VisualElement>("AudioPreprocessPanel") != null,
                "Audio preprocess dialog shell is missing.");
            Require(
                instance.Q<TextField>("AudioPreprocessBpm") != null &&
                instance.Q<TextField>("AudioPreprocessBeats") != null &&
                instance.Q<TextField>("AudioPreprocessDelay") != null,
                "Audio preprocess parameter fields are missing.");
            Require(
                instance.Q<VisualElement>("AudioPreprocessWaveform") != null,
                "Audio preprocess waveform host is missing.");
            Require(
                instance.Q<Button>("AudioPreprocessAudition") != null &&
                instance.Q<Button>("AudioPreprocessApply") != null &&
                instance.Q<Button>("AudioPreprocessClose") != null,
                "Audio preprocess action buttons are missing.");
            Require(
                instance.Q<Button>("AudioPreprocessZoom5") != null &&
                instance.Q<Button>("AudioPreprocessZoom60") != null,
                "Audio preprocess zoom controls are missing.");
            Require(
                Resources.Load<StyleSheet>(AudioPreprocessStylePath) != null,
                "Audio preprocess style sheet is missing.");

            // The sheet must reach the elements that were REPARENTED.
            //
            // <ui:Style src="..."> attaches to the TemplateContainer, but the
            // view moves the overlay onto the modal layer so absolute
            // positioning resolves against a real box. Once reparented, the
            // subtree no longer descends from the container, so a Style line in
            // the UXML silently stops covering it and every
            // .audio-preprocess-* rule drops out — the window rendered with
            // default UI Toolkit chrome (vertical stacking, no colours).
            // Assert on the overlay itself, which is where it has to live.
            var probeModalLayer = new VisualElement();
            probeModalLayer.name = "ProbeModalLayer";
            var probe = new FalsumAudioPreprocessView(
                probeModalLayer,
                template);
            probe.Show(
                120.0,
                4.0,
                0.0,
                180000.0,
                "fixture",
                "MIN");
            Require(
                probe.DebugStyleSheetAttached,
                "Audio preprocess view did not attach its style sheet; the window " +
                "would render with default UI Toolkit chrome.");
            Require(
                probe.DebugOverlayHasStyleSheet,
                "The reparented overlay is not carrying the audio preprocess style " +
                "sheet; every .audio-preprocess-* rule would stop matching.");
            Require(
                probeModalLayer.childCount == 1 &&
                probeModalLayer[0].name == "AudioPreprocessOverlay",
                "The audio preprocess overlay must be a DIRECT child of the modal " +
                "layer; wrapping it in a TemplateContainer makes absolute " +
                "positioning resolve against a zero-size parent.");
            probe.Close();
            Require(
                probeModalLayer.childCount == 0,
                "Closing the audio preprocess window must remove its overlay.");
        }

        /// <summary>
        /// A NEGATIVE-delay package is the case that used to throw
        /// "谱面内容包含负数音乐时间" on Apply.
        ///
        /// ChartWriter refuses any chart that would contain negative music time
        /// and a negative delay produces exactly that: it writes the derived
        /// track(-D,1.00) / bpm(-D,...) headline events, which are negative.
        /// The apply path used to serialise the live chart WITH ITS OLD DELAY
        /// before migrating it, so it hit that guard and never got out. Migrating
        /// in memory first and serialising once with delay 0 must succeed.
        /// </summary>
        private static void CheckNegativeDelayMigration()
        {
            var source =
                "# song: negative\n" +
                "# difficulty: 0\n" +
                "# delay: -60\n" +
                "chart(120,4)\n" +
                "track(-60,1.00)\n" +
                "bpm(-60,120,4)\n" +
                "tap(560,1,1)\n";

            // The OLD path is expected to throw: this pins the bug so a regression
            // that reintroduces it fails loudly rather than resurfacing in the
            // editor as a mysterious save failure.
            var parsed = ChartParser.Parse(
                source.Replace("\r\n", "\n").Split('\n'),
                "<negative-delay>",
                0);
            Require(parsed.Success, "The negative-delay fixture must parse.");
            Require(
                Math.Abs(parsed.Chart.DelayMs + 60.0) < 1e-6,
                "The fixture must parse back with delay -60.");
            Require(
                Math.Abs(parsed.Chart.Notes[0].TimeMs - 620.0) < 1e-6,
                "Music time 560 with delay -60 must parse as chart time 620.");
            var oldPathThrew = false;
            try
            {
                ChartWriter.Write(parsed.Chart, parsed.Chart.DelayMs, "negative", 0);
            }
            catch (InvalidOperationException)
            {
                oldPathThrew = true;
            }
            Require(
                oldPathThrew,
                "Serialising a negative-delay chart with its old delay must throw; " +
                "the migration must therefore never do that.");

            // The new path: migrate a CLONE in memory, then write with delay 0.
            var migratedChart = CloneForCheck(parsed.Chart);
            var result = FalsumAudioPreprocessMath.MigrateChartInPlace(
                migratedChart,
                -60.0);
            Require(
                result == FalsumAudioPreprocessMath.TextMigrationResult.Ok,
                "Migrating a negative-delay chart in memory must succeed, got " + result + ".");
            string text;
            try
            {
                text = ChartWriter.Write(migratedChart, 0.0, "negative", 0);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "Migrating a negative-delay chart then writing it must not " +
                    "throw, but it did: " + exception.Message);
            }
            Require(
                text.IndexOf("# delay: 0", StringComparison.Ordinal) >= 0,
                "The migrated negative-delay text must store delay 0.");
            Require(
                text.IndexOf("track(-", StringComparison.Ordinal) < 0,
                "The migrated text must not contain a negative track() headline.");
            Require(
                text.IndexOf("bpm(-", StringComparison.Ordinal) < 0,
                "The migrated text must not contain a negative bpm() headline.");

            var reparsed = ChartParser.Parse(
                text.Replace("\r\n", "\n").Split('\n'),
                "<negative-delay-roundtrip>",
                0);
            Require(reparsed.Success, "The migrated negative-delay text must parse.");
            Require(
                Math.Abs(reparsed.Chart.DelayMs) < 1e-6,
                "The migrated text must read back with delay 0.");
            Require(
                Math.Abs(reparsed.Chart.Notes[0].TimeMs - 620.0) < 1e-6,
                "A tap at music time 560 with delay -60 is chart time 620; " +
                "after migration it must still land on the same music, got " +
                reparsed.Chart.Notes[0].TimeMs + ".");
        }

        private static Chart CloneForCheck(Chart source)
        {
            var chart = new Chart
            {
                Bpm = source.Bpm,
                Division = source.Division,
                DelayMs = source.DelayMs,
                SongId = source.SongId,
                Difficulty = source.Difficulty,
            };
            for (var i = 0; i < source.Notes.Count; i++)
            {
                var note = source.Notes[i];
                if (note == null)
                {
                    continue;
                }
                var copy = new ChartNote();
                copy.Kind = note.Kind;
                copy.TimeMs = note.TimeMs;
                copy.StartLane = note.StartLane;
                copy.WidthLanes = note.WidthLanes;
                copy.DurationMs = note.DurationMs;
                copy.StartPos = note.StartPos;
                copy.EndPos = note.EndPos;
                copy.StartWidth = note.StartWidth;
                copy.EndWidth = note.EndWidth;
                copy.StartCellCount = note.StartCellCount;
                copy.EndCellCount = note.EndCellCount;
                copy.LeftEasing = note.LeftEasing;
                copy.RightEasing = note.RightEasing;
                copy.GroupId = note.GroupId;
                copy.FlickDirection = note.FlickDirection;
                chart.Notes.Add(copy);
            }
            for (var i = 0; i < source.Events.Count; i++)
            {
                var evt = source.Events[i];
                if (evt == null)
                {
                    continue;
                }
                chart.Events.Add(new ChartEvent
                {
                    Kind = evt.Kind,
                    IsChartBase = evt.IsChartBase,
                    TimeMs = evt.TimeMs,
                    Multiplier = evt.Multiplier,
                    Bpm = evt.Bpm,
                    Division = evt.Division,
                    Lane = evt.Lane,
                    Enabled = evt.Enabled,
                });
            }
            chart.RebuildTimeline();
            FalsumAudioPreprocessMath.RebuildEventKindLists(chart);
            return chart;
        }

        /// <summary>
        /// Saving a package ON TOP OF ITSELF must work.
        ///
        /// The existing package round-trip check only ever saves to a NEW path,
        /// which is why this was missed: overwriting an already-saved package
        /// went through <c>File.Copy(temp, target, overwrite: true)</c>, and that
        /// fails whenever anything holds the target open. The audio-preprocess
        /// apply always saves in place (it must, so the rewritten audio is
        /// persisted before the reload), so on a package opened from a
        /// Downloads folder it died with
        /// "The process cannot access the file ... being used by another
        /// process". The fix replaced Copy with Replace + Move.
        /// </summary>
        private static void CheckInPlacePackageSave()
        {
            var directory = Path.Combine(
                Application.dataPath != null
                    ? Path.GetDirectoryName(Application.dataPath) ??
                      Directory.GetCurrentDirectory()
                    : Directory.GetCurrentDirectory(),
                "Temp",
                "FalsumAudioPreprocessSave");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "inplace.falsum");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            try
            {
                // Build a minimal package by hand instead of via CreateNew: the
                // audio preprocess save path only needs a package that is
                // already on disk, and CreateNew enforces a "needs notes"
                // invariant that would make this fixture carry unrelated setup.
                var manifest = FalsumManifest.CreateTemplate(
                    "inplacecheck",
                    "In Place",
                    "In Falsus",
                    60000);
                manifest.FindDifficulty(0).SetAvailable(true);
                var chartText = ChartWriter.Write(
                    new Chart { Bpm = 120.0, Division = 4.0 },
                    0.0,
                    manifest.SongId,
                    0);
                using (var stream = new FileStream(
                           path,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                using (var archive = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create))
                {
                    WriteEntry(
                        archive,
                        "song.json",
                        System.Text.Encoding.UTF8.GetBytes(
                            manifest.Serialize()));
                    WriteEntry(
                        archive,
                        "audio.ogg",
                        new byte[] { 79, 103, 103, 83, 0, 0, 0, 0 });
                    WriteEntry(
                        archive,
                        "charts/min.chart.txt",
                        System.Text.Encoding.UTF8.GetBytes(chartText));
                }
                Require(File.Exists(path), "The fixture package was not written.");

                // Save AGAIN onto the same path, which is what the audio
                // preprocess does. This is the case File.Copy(overwrite: true)
                // failed on.
                var reloaded = FalsumPackage.Load(path);
                reloaded.SetAudio(
                    new byte[] { 79, 103, 103, 83, 1, 2, 3, 4 },
                    60100);
                reloaded.Save(path);
                Require(
                    File.Exists(path),
                    "Overwriting an existing package deleted it.");
                var reread = FalsumPackage.Load(path);
                Require(
                    reread.AudioBytes != null &&
                    reread.AudioBytes.Length == 8 &&
                    reread.AudioBytes[4] == 1,
                    "The in-place save did not persist the new audio bytes.");
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static void WriteEntry(
            ZipArchive archive,
            string name,
            byte[] bytes)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static void CheckLocalizationKeys()        {
            // C-3: a missing key renders the raw key text to the user, so the
            // window's entire key set must exist in both locales. The set is
            // enumerated here instead of trusting the UI file, because a key
            // typo inside a UXML attribute would otherwise only show up as a
            // bare key string in the running editor.
            string[] keys =
            {
                "falsum.audioPreprocess.title",
                "falsum.audioPreprocess.lead",
                "falsum.audioPreprocess.bpm",
                "falsum.audioPreprocess.beats",
                "falsum.audioPreprocess.delay",
                "falsum.audioPreprocess.hint",
                "falsum.audioPreprocess.audition",
                "falsum.audioPreprocess.stop",
                "falsum.audioPreprocess.apply",
                "falsum.audioPreprocess.close",
                "falsum.audioPreprocess.range",
                "falsum.audioPreprocess.zoom",
                "falsum.audioPreprocess.zoomSeconds",
                "falsum.audioPreprocess.error.delayNotFinite",
                "falsum.audioPreprocess.error.bpmNotFinite",
                "falsum.audioPreprocess.error.bpmNotPositive",
                "falsum.audioPreprocess.error.beatsNotPositive",
                "falsum.audioPreprocess.error.sourceAudioMissing",
                "falsum.audioPreprocess.error.resultAudioTooShort",
                "falsum.audioPreprocess.error.negativeMusicTime",
                "falsum.audioPreprocess.error.difficultyUnparsable",
                "falsum.audioPreprocess.error.difficultyNegativeTime",
                "falsum.audioPreprocess.error.difficultyWriteFailed",
                "falsum.audioPreprocess.error.encodeFailed",
                "falsum.audioPreprocess.error.saveNoPath",
                "falsum.audioPreprocess.error.packageLocked",
                "falsum.audioPreprocess.confirm.title",
                "falsum.audioPreprocess.confirm.detail",
                "falsum.audioPreprocess.status.encoding",
                "falsum.audioPreprocess.status.applied",
                "falsum.audioPreprocess.tooltip",
                "falsum.audioPreprocess.entry",
                "falsum.audioPreprocess.busy.envelope",
                "falsum.audioPreprocess.status.legacyDelay",
            };
            for (var i = 0; i < keys.Length; i++)
            {
                Require(
                    InFalsus.Gameplay.Falsum.Localization.FalsumLocalization
                        .TryGet(keys[i], out var value) &&
                    !string.IsNullOrEmpty(value),
                    "Audio preprocess localization key is missing: " + keys[i]);
            }
        }
    }
}
