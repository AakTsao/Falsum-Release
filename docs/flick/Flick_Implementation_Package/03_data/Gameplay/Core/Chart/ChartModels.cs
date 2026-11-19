using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Judgement;

namespace InFalsus.Gameplay.Core.Charts
{
    public enum NoteKind
    {
        Tap,
        Hold,
        Flick,
        SkyArea,
    }

    public enum NoteZone
    {
        Lane,
        Sky,
    }

    public enum ChartEventKind
    {
        TrackSpeed,
        Bpm,
        LaneLayout,
    }

    [Serializable]
    public sealed class ChartNote
    {
        public int Id;
        public int SourceOrder = -1;
        public NoteKind Kind;
        public double TimeMs;

        public int StartLane;
        public int WidthLanes = 1;
        public double DurationMs;

        public double StartPos;
        public double EndPos;
        public double StartWidth;
        public double EndWidth;
        public int StartCellCount;
        public int EndCellCount;
        public int LeftEasing;
        public int RightEasing;
        public int GroupId;
        public int FlickDirection;

        // Flick stores its midpoint in StartPos and its width in StartWidth.
        // SkyArea stores endpoint centers and widths in Start/End pairs.
        public double FlickCenterPos
        {
            get { return StartPos; }
        }

        public double FlickWidth
        {
            get { return StartWidth; }
        }

        public double FlickLeftPos
        {
            get { return FlickCenterPos - FlickWidth * 0.5; }
        }

        public double FlickRightPos
        {
            get { return FlickCenterPos + FlickWidth * 0.5; }
        }

        public double FlickHeadPos
        {
            get
            {
                return FlickDirection == 16
                    ? FlickLeftPos
                    : FlickRightPos;
            }
        }

        public bool Consumed;
        public bool Judged;
        public bool Holding;
        public JudgementResult Result;
        public JudgementResult HoldStartResult;

        public NoteZone Zone
        {
            get { return Kind == NoteKind.SkyArea ? NoteZone.Sky : NoteZone.Lane; }
        }

        public double EndTimeMs
        {
            get { return TimeMs + DurationMs; }
        }

        public bool CoversLane(int lane)
        {
            return Kind != NoteKind.Flick &&
                   Kind != NoteKind.SkyArea &&
                   lane >= StartLane &&
                   lane < StartLane + WidthLanes;
        }
    }

    public static class ChartNoteValidator
    {
        private const double Epsilon = 0.000000001;

        public static void Validate(
            ChartNote note,
            string context = "note",
            bool allowOutOfBounds = false)
        {
            if (note == null)
            {
                throw new FormatException(context + " is null.");
            }
            RequireFinite(note.TimeMs, context + ".timeMs");
            if (note.TimeMs < 0.0)
            {
                throw new FormatException(
                    context + ".timeMs must not be negative.");
            }

            switch (note.Kind)
            {
                case NoteKind.Tap:
                case NoteKind.Hold:
                    ValidateLaneSpan(note, context);
                    if (note.Kind == NoteKind.Hold)
                    {
                        RequireFinite(
                            note.DurationMs,
                            context + ".durationMs");
                        if (note.DurationMs <= 0.0)
                        {
                            throw new FormatException(
                                context +
                                ".durationMs must be positive.");
                        }
                    }
                    break;

                case NoteKind.Flick:
                    RequireFinite(note.StartPos, context + ".startPos");
                    RequireFinite(
                        note.StartWidth,
                        context + ".startWidth");
                    if (note.StartCellCount <= 0 ||
                        note.EndCellCount <= 0)
                    {
                        throw new FormatException(
                            context +
                            ".startCellCount and endCellCount " +
                            "must be positive.");
                    }
                    if (!allowOutOfBounds)
                    {
                        RequireFlickSpan(
                            note.StartPos,
                            note.StartWidth,
                            note.StartCellCount,
                            context + " flick span");
                    }
                    if (note.FlickDirection != 4 &&
                        note.FlickDirection != 16)
                    {
                        throw new FormatException(
                            context +
                            ".flickDirection must be 4 or 16.");
                    }
                    break;

                case NoteKind.SkyArea:
                    RequireFinite(note.StartPos, context + ".startPos");
                    RequireFinite(note.EndPos, context + ".endPos");
                    RequireFinite(
                        note.StartWidth,
                        context + ".startWidth");
                    RequireFinite(
                        note.EndWidth,
                        context + ".endWidth");
                    RequireFinite(
                        note.DurationMs,
                        context + ".durationMs");
                    if (note.DurationMs <= 0.0)
                    {
                        throw new FormatException(
                            context +
                            ".durationMs must be positive.");
                    }
                    if (note.StartCellCount <= 0 ||
                        note.EndCellCount <= 0)
                    {
                        throw new FormatException(
                            context +
                            ".startCellCount and endCellCount " +
                            "must be positive.");
                    }
                    if (!allowOutOfBounds)
                    {
                        RequireSkySpan(
                            note.StartPos,
                            note.StartWidth,
                            context + " start span");
                        RequireSkySpan(
                            note.EndPos,
                            note.EndWidth,
                            context + " end span");
                    }
                    if (note.LeftEasing < 0 || note.LeftEasing > 2 ||
                        note.RightEasing < 0 || note.RightEasing > 2)
                    {
                        throw new FormatException(
                            context +
                            " easing must be within 0..2.");
                    }
                    if (note.GroupId < 0)
                    {
                        throw new FormatException(
                            context +
                            ".groupId must not be negative.");
                    }
                    break;

                default:
                    throw new FormatException(
                        context + " has an unknown note kind.");
            }
        }

        private static void ValidateLaneSpan(
            ChartNote note,
            string context)
        {
            if (note.StartLane < 0 ||
                note.WidthLanes <= 0 ||
                note.StartLane + note.WidthLanes > 6)
            {
                throw new FormatException(
                    context +
                    " lane span must be within 0..5.");
            }
        }

        private static void RequireSkySpan(
            double center,
            double width,
            string context)
        {
            if (width < 0.0 ||
                center - width * 0.5 < -Epsilon ||
                center + width * 0.5 > 1.0 + Epsilon)
            {
                throw new FormatException(
                    context + " must stay within 0..1.");
            }
        }

        private static void RequireFlickSpan(
            double center,
            double width,
            int cellCount,
            string context)
        {
            var tolerance = 1.0 / Math.Max(1, cellCount) +
                            Epsilon;
            if (width < 0.0 ||
                center - width * 0.5 < -tolerance ||
                center + width * 0.5 > 1.0 + tolerance)
            {
                throw new FormatException(
                    context + " must stay within 0..1.");
            }
        }

        private static void RequireFinite(
            double value,
            string label)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new FormatException(
                    label + " must be finite.");
            }
        }
    }

    [Serializable]
    public sealed class ChartEvent
    {
        public ChartEventKind Kind;
        public bool IsChartBase;
        public double TimeMs;
        public double Multiplier;
        public double Bpm;
        public double Division;
        public int Lane;
        public bool Enabled;
        public int SourceLine;
    }

    [Serializable]
    public sealed class Chart
    {
        public double Bpm = 140.0;
        public double Division = 4.0;
        // Chart time is the editor-facing timeline. Music time, which is
        // stored in chart files, is ChartTime + DelayMs.
        public double DelayMs;
        public string SongId = string.Empty;
        public int Difficulty;
        public string SourcePath = string.Empty;

        // Editor-only preview switch. The track() events stay in Events and
        // are written/undone as ordinary data; this flag only decides whether
        // the preview integrates them.
        //
        // Backed by a field so flipping it can invalidate every derived cache
        // (the speed prefix table and the TrackMotion playhead memo). A chart
        // is previewed both suppressed and unsuppressed inside one frame while
        // the editor toggles the flag, so a stale cache here would silently
        // keep the previous mapping.
        [NonSerialized]
        private bool _trackSpeedEffectsEnabled = true;

        public bool TrackSpeedEffectsEnabled
        {
            get { return _trackSpeedEffectsEnabled; }
            set
            {
                if (_trackSpeedEffectsEnabled == value)
                {
                    return;
                }
                _trackSpeedEffectsEnabled = value;
                TimelineVersion++;
            }
        }

        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<ChartEvent> Events = new List<ChartEvent>();
        public readonly List<ChartEvent> SpeedEvents = new List<ChartEvent>();
        public readonly List<ChartEvent> BpmEvents = new List<ChartEvent>();
        public readonly List<ChartEvent> LaneEvents = new List<ChartEvent>();
        public readonly List<double> MeasureLineTimes = new List<double>();
        // Runtime-only generation bound supplied when the song audio duration
        // is known. A negative value keeps the legacy fallback.
        public double MeasureLineGenerationEndMs { get; set; } = -1.0;

        /// <summary>
        /// Prefix table for <see cref="ScaledTimeAt"/>, rebuilt by
        /// <see cref="RebuildTimeline"/>. The track-speed map is a piecewise
        /// linear integral over <see cref="SpeedEvents"/>; the per-frame hot
        /// path (SkyArea row generation and every note's z) used to re-walk
        /// the whole event list from the beginning on every call — O(events)
        /// per call, O(rows × 3 × events) per frame.
        ///
        /// The table is the SAME function, just precomputed: this is not a
        /// semantic change, and <see cref="ScaledTimeAt"/> stays the single
        /// conversion entry point (invariant A-16).
        ///
        /// When the index has not been built (a Chart constructed literally
        /// without calling RebuildTimeline, e.g. the gameplay self check),
        /// ScaledTimeAt falls back to the original linear walk, so behaviour
        /// there is byte-for-byte unchanged.
        /// </summary>
        [NonSerialized]
        private double[] _speedIndexTimes;
        [NonSerialized]
        private double[] _speedIndexBases;
        [NonSerialized]
        private double[] _speedIndexTotals;
        [NonSerialized]
        private double[] _speedIndexSpeeds;
        [NonSerialized]
        private bool _speedIndexValid;
        /// <summary>
        /// <see cref="SpeedEvents"/> count at the moment the prefix table was
        /// built. Checked O(1) on every query so that a transaction that
        /// detaches the event list (FalsumChartSession.DetachCurrent clears
        /// SpeedEvents for the duration of a BeginChange) cannot be served by
        /// a stale table — it falls back to the linear walk exactly like the
        /// original code did.
        /// </summary>
        [NonSerialized]
        private int _speedIndexCount;

        /// <summary>
        /// Bumped whenever the derived timeline changes (event lists sorted,
        /// prefix table rebuilt, or the speed map explicitly invalidated).
        /// Consumers that memoise anything derived from
        /// <see cref="ScaledTimeAt"/> must key their cache on this value, not
        /// on time alone: a chart edit can change the map without changing
        /// the playhead.
        /// </summary>
        [NonSerialized]
        public int TimelineVersion;

        /// <summary>
        /// Forces the next <see cref="ScaledTimeAt"/> to use the linear walk.
        /// Call this from any path that mutates a <see cref="ChartEvent"/>
        /// in place (time or multiplier) without going through
        /// <see cref="RebuildTimeline"/>.
        /// </summary>
        public void InvalidateSpeedIndex()
        {
            _speedIndexValid = false;
            _bpmIndexValid = false;
            TimelineVersion++;
        }

        private void RebuildSpeedIndex()
        {
            var count = SpeedEvents.Count;
            _speedIndexCount = count;
            if (count == 0)
            {
                _speedIndexTimes = null;
                _speedIndexBases = null;
                _speedIndexTotals = null;
                _speedIndexSpeeds = null;
                _speedIndexValid = true;
                return;
            }
            // The original linear walk is the spec, including its two quirks:
            //   1. `last` only advances when the timestamp strictly INCREASES,
            //      so an event at or before 0 changes the speed but leaves the
            //      integration base pinned at 0;
            //   2. duplicate timestamps do not advance the integral, but the
            //      LAST event at that time wins the speed.
            // Both are reproduced below, so the table is the same function.
            var times = new double[count];
            var speeds = new double[count];
            var distinct = 0;
            for (var i = 0; i < count; i++)
            {
                var evt = SpeedEvents[i];
                var timeMs = evt.TimeMs;
                if (double.IsNaN(timeMs) ||
                    double.IsInfinity(timeMs) ||
                    double.IsNaN(evt.Multiplier) ||
                    double.IsInfinity(evt.Multiplier))
                {
                    // The original would propagate the non-finite value; a
                    // binary search over it is undefined. Keep the linear
                    // walk for this chart so behaviour is identical.
                    _speedIndexValid = false;
                    return;
                }
                if (distinct > 0 && times[distinct - 1] == timeMs)
                {
                    speeds[distinct - 1] = evt.Multiplier;
                    continue;
                }
                times[distinct] = timeMs;
                speeds[distinct] = evt.Multiplier;
                distinct++;
            }

            var bases = new double[distinct];
            var totals = new double[distinct];
            var lastTime = 0.0;
            var lastSpeed = 1.0;
            var total = 0.0;
            for (var i = 0; i < distinct; i++)
            {
                if (times[i] > lastTime)
                {
                    total += (times[i] - lastTime) * lastSpeed;
                    lastTime = times[i];
                }
                lastSpeed = speeds[i];
                totals[i] = total;
                // The integration base AFTER this event, which is what the
                // query must extrapolate from. For a negative timestamp this
                // stays at the previous (non-negative) base, matching the
                // original `last`.
                bases[i] = lastTime;
            }

            var length = distinct;
            if (_speedIndexTimes == null ||
                _speedIndexTimes.Length != length)
            {
                _speedIndexTimes = new double[length];
                _speedIndexBases = new double[length];
                _speedIndexTotals = new double[length];
                _speedIndexSpeeds = new double[length];
            }
            Array.Copy(times, _speedIndexTimes, length);
            Array.Copy(bases, _speedIndexBases, length);
            Array.Copy(totals, _speedIndexTotals, length);
            Array.Copy(speeds, _speedIndexSpeeds, length);
            _speedIndexValid = true;
        }

        /// <summary>
        /// Cached chart end (last note tail or last event). The getter used to
        /// walk every note and every event on each call, and the playback loop
        /// asks for it at least once per frame while paused asks twice — on a
        /// 2071-note chart that is ~2 × 2236 comparisons of pure waste.
        ///
        /// Invalidated by <see cref="RebuildTimeline"/>, which every edit path
        /// already goes through (A-2 lists the four mandatory Normalize
        /// callers, and they all end in a timeline rebuild). Paths that mutate
        /// notes/events in place without a rebuild must call
        /// <see cref="InvalidateTimelineDerived"/>.
        /// </summary>
        [NonSerialized]
        private double _endTimeCache;
        [NonSerialized]
        private int _endTimeCacheVersion = -1;

        public double EndTimeMs
        {
            get
            {
                if (_endTimeCacheVersion == TimelineVersion)
                {
                    return _endTimeCache;
                }

                var end = 0.0;
                for (var i = 0; i < Notes.Count; i++)
                {
                    var noteEnd = Notes[i].EndTimeMs;
                    if (noteEnd > end)
                    {
                        end = noteEnd;
                    }
                }
                for (var i = 0; i < Events.Count; i++)
                {
                    var eventTime = Events[i].TimeMs;
                    if (eventTime > end)
                    {
                        end = eventTime;
                    }
                }

                _endTimeCache = end;
                _endTimeCacheVersion = TimelineVersion;
                return end;
            }
        }

        /// <summary>
        /// Declares that a caller mutated note/event data in place, so every
        /// timeline-derived cache (end time, speed prefix table, consumer
        /// memos keyed on <see cref="TimelineVersion"/>) must be recomputed.
        /// Same contract as <see cref="InvalidateSpeedIndex"/>.
        /// </summary>
        public void InvalidateTimelineDerived()
        {
            _speedIndexValid = false;
            _bpmIndexValid = false;
            TimelineVersion++;
        }

        public double SpeedMultiplierAt(double timeMs)
        {
            var speed = 1.0;
            foreach (var evt in SpeedEvents)
            {
                if (evt.TimeMs > timeMs)
                {
                    break;
                }
                speed = evt.Multiplier;
            }
            return speed;
        }

        /// <summary>
        /// Track multiplier the *preview* should use, honouring
        /// <see cref="TrackSpeedEffectsEnabled"/>. Display-direction callers
        /// (visual window sizing, lane-disable direction) must use this one so
        /// a suppressed chart previews as a constant 1.00 scroll.
        ///
        /// Callers that read track speed as <em>data</em> — the default
        /// multiplier for a newly created track event, tool scripts, self
        /// checks — must keep using <see cref="SpeedMultiplierAt"/>.
        /// Suppression must never leak into event values, or every event
        /// authored while it is on would be flattened to 1.00 and written back
        /// to the package.
        /// </summary>
        public double PreviewSpeedMultiplierAt(double timeMs)
        {
            return TrackSpeedEffectsEnabled
                ? SpeedMultiplierAt(timeMs)
                : 1.0;
        }

        /// <summary>
        /// <see cref="BpmEvents"/> timestamps for the bpm/division lookup
        /// index, built alongside the speed prefix table. Both queries scan
        /// the list linearly; <see cref="FalsumAutoCombo"/> asks for the bpm
        /// prefix once per Hold/SkyArea note, which multiplies into
        /// O(notes × bpmEvents) per combo refresh (tens of thousands of list
        /// steps per frame on charts with many tempo changes).
        /// </summary>
        [NonSerialized]
        private double[] _bpmIndexTimes;
        [NonSerialized]
        private bool _bpmIndexValid;
        [NonSerialized]
        private int _bpmIndexCount;

        private void RebuildBpmIndex()
        {
            var count = BpmEvents.Count;
            _bpmIndexCount = count;
            if (count == 0)
            {
                _bpmIndexTimes = null;
                _bpmIndexValid = true;
                return;
            }
            if (_bpmIndexTimes == null || _bpmIndexTimes.Length != count)
            {
                _bpmIndexTimes = new double[count];
            }
            for (var i = 0; i < count; i++)
            {
                var timeMs = BpmEvents[i].TimeMs;
                if (double.IsNaN(timeMs) || double.IsInfinity(timeMs))
                {
                    // A non-finite timestamp makes the ordering assumption
                    // false; keep the linear scan for this chart.
                    _bpmIndexValid = false;
                    return;
                }
                _bpmIndexTimes[i] = timeMs;
            }
            _bpmIndexValid = true;
        }

        /// <summary>
        /// Index of the last BpmEvent whose TimeMs is at or before
        /// <paramref name="timeMs"/>, or -1 when none. The linear walk breaks
        /// on `evt.TimeMs > timeMs`, so an event exactly at timeMs DOES apply.
        /// </summary>
        private int BpmEventIndexAt(double timeMs)
        {
            var times = _bpmIndexTimes;
            var low = 0;
            var high = times.Length - 1;
            var active = -1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                if (times[mid] <= timeMs)
                {
                    active = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return active;
        }

        public double BpmAt(double timeMs)
        {
            if (_bpmIndexValid &&
                _bpmIndexCount == BpmEvents.Count &&
                _bpmIndexTimes != null)
            {
                var index = BpmEventIndexAt(timeMs);
                return index >= 0 ? BpmEvents[index].Bpm : Bpm;
            }
            var bpm = Bpm;
            foreach (var evt in BpmEvents)
            {
                if (evt.TimeMs > timeMs)
                {
                    break;
                }
                bpm = evt.Bpm;
            }
            return bpm;
        }

        public double DivisionAt(double timeMs)
        {
            if (_bpmIndexValid &&
                _bpmIndexCount == BpmEvents.Count &&
                _bpmIndexTimes != null)
            {
                var index = BpmEventIndexAt(timeMs);
                return index >= 0 ? BpmEvents[index].Division : Division;
            }
            var division = Division;
            foreach (var evt in BpmEvents)
            {
                if (evt.TimeMs > timeMs)
                {
                    break;
                }
                division = evt.Division;
            }
            return division;
        }

        public bool LaneEnabledAt(int lane, double timeMs)
        {
            var enabled = true;
            foreach (var evt in LaneEvents)
            {
                if (evt.TimeMs > timeMs + 0.0001)
                {
                    break;
                }
                if (evt.Lane == lane)
                {
                    enabled = evt.Enabled;
                }
            }
            return enabled;
        }

        public bool TryGetLaneDisabledInterval(
            int lane,
            double timeMs,
            out double startTimeMs,
            out double endTimeMs)
        {
            startTimeMs = 0.0;
            endTimeMs = double.PositiveInfinity;
            var disabled = false;

            for (var i = 0; i < LaneEvents.Count; i++)
            {
                var evt = LaneEvents[i];
                if (evt.TimeMs > timeMs + 0.0001)
                {
                    if (disabled && evt.Lane == lane)
                    {
                        if (evt.Enabled)
                        {
                            endTimeMs = evt.TimeMs;
                        }
                        break;
                    }
                    continue;
                }
                if (evt.Lane != lane)
                {
                    continue;
                }

                disabled = !evt.Enabled;
                if (disabled)
                {
                    startTimeMs = evt.TimeMs;
                    endTimeMs = double.PositiveInfinity;
                }
            }

            return disabled;
        }

        // The song clock is a piecewise-constant speed field. Integrating it
        // keeps notes from jumping when a track() event changes the multiplier.
        public double ScaledTimeAt(double timeMs)
        {
            if (timeMs <= 0.0)
            {
                return timeMs;
            }

            if (!TrackSpeedEffectsEnabled)
            {
                // Constant 1.00 multiplier: byte-for-byte the same map a
                // chart with only track(t, 1.00) would produce. This is the
                // single override point — TrackMotion, measure lines, lane
                // state, spectrum warp and 3D picking all read this map, so
                // suppressing effect here keeps every consumer consistent
                // instead of re-deriving the mapping per subsystem.
                return timeMs;
            }

            // Ultra-common case: a chart with no track() events at all maps
            // linearly. Checking the list count directly is a single compare,
            // and it must happen BEFORE any index bookkeeping: on these charts
            // (coldsea, alterededge, rainshower …) the original code paid
            // nothing because the foreach body never ran, so the fast path
            // must not cost more than that.
            if (SpeedEvents.Count == 0)
            {
                return timeMs;
            }

            // Fast path: the prefix table is the exact same piecewise-linear
            // integral, evaluated in O(log n) instead of O(n). The table is
            // built by RebuildTimeline; a Chart assembled without it keeps
            // using the linear walk below, so this changes nothing there.
            //
            // The count check rejects the one window where the list can shrink
            // without a RebuildTimeline: FalsumChartSession.DetachCurrent
            // clears SpeedEvents for the duration of an open BeginChange.
            // That window must behave exactly like the original (linear walk).
            if (_speedIndexValid &&
                _speedIndexCount == SpeedEvents.Count)
            {
                var indexTimes = _speedIndexTimes;
                if (indexTimes == null || indexTimes.Length == 0)
                {
                    return timeMs;
                }
                // Last index whose TimeMs is strictly below timeMs. The linear
                // walk breaks on `evt.TimeMs >= timeMs`, so an event exactly at
                // timeMs does NOT contribute a segment.
                var low = 0;
                var high = indexTimes.Length - 1;
                var active = -1;
                while (low <= high)
                {
                    var mid = low + ((high - low) >> 1);
                    if (indexTimes[mid] < timeMs)
                    {
                        active = mid;
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }

                if (active < 0)
                {
                    // No event before timeMs: pure 1.00 travel from zero.
                    return timeMs;
                }

                var total = _speedIndexTotals[active];
                var last = _speedIndexBases[active];
                var speed = _speedIndexSpeeds[active];
                return total + (timeMs - last) * speed;
            }

            var result = 0.0;
            var lastTime = 0.0;
            var currentSpeed = 1.0;

            foreach (var evt in SpeedEvents)
            {
                if (evt.TimeMs >= timeMs)
                {
                    break;
                }

                if (evt.TimeMs > lastTime)
                {
                    result += (evt.TimeMs - lastTime) * currentSpeed;
                    lastTime = evt.TimeMs;
                }
                currentSpeed = evt.Multiplier;
            }

            result += (timeMs - lastTime) * currentSpeed;
            return result;
        }

        public void RebuildTimeline()
        {
            Events.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            SpeedEvents.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            BpmEvents.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            LaneEvents.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            // The track-speed prefix table and the bpm lookup index must be
            // rebuilt wherever the sorted event lists can change — i.e. exactly
            // here, the single entry point that already sorts them. The version
            // bump invalidates every consumer cache derived from the old map
            // (see TimelineVersion).
            RebuildSpeedIndex();
            RebuildBpmIndex();
            TimelineVersion++;
            MeasureLineTimes.Clear();

            var candidates = new List<double>();
            var bpmIndex = 0;
            var segmentStart = 0.0;
            var segmentBpm = Bpm;
            var segmentDivision = Division;
            var chartEnd = Math.Max(0.0, EndTimeMs);
            var horizon =
                MeasureLineGenerationEndMs >= 0.0 &&
                !double.IsNaN(MeasureLineGenerationEndMs) &&
                !double.IsInfinity(MeasureLineGenerationEndMs)
                    ? Math.Max(chartEnd, MeasureLineGenerationEndMs)
                    : chartEnd + 60000.0;

            while (segmentStart <= horizon)
            {
                while (bpmIndex < BpmEvents.Count &&
                       BpmEvents[bpmIndex].TimeMs <= segmentStart + 0.0001)
                {
                    segmentBpm = BpmEvents[bpmIndex].Bpm;
                    segmentDivision = BpmEvents[bpmIndex].Division;
                    bpmIndex++;
                }

                var nextBpm = bpmIndex < BpmEvents.Count
                    ? BpmEvents[bpmIndex].TimeMs
                    : double.PositiveInfinity;
                var segmentEnd = Math.Min(Math.Min(nextBpm, horizon), horizon);
                var measureLength = 60000.0 / segmentBpm * segmentDivision;
                if (measureLength > 0.0001)
                {
                    var count = 0;
                    for (var time = segmentStart;
                         time <= segmentEnd + 0.0001 && count < 200000;
                         time += measureLength, count++)
                    {
                        candidates.Add(time);
                    }
                }

                if (nextBpm >= horizon || double.IsPositiveInfinity(nextBpm))
                {
                    break;
                }

                candidates.Add(nextBpm);
                segmentStart = nextBpm;
                segmentBpm = BpmEvents[bpmIndex].Bpm;
                segmentDivision = BpmEvents[bpmIndex].Division;
                bpmIndex++;
            }

            // track() only changes travel speed. It must not create or refresh
            // measure lines; measure lines come from chart/bpm timing only.
            for (var i = 0; i < BpmEvents.Count; i++)
            {
                candidates.Add(BpmEvents[i].TimeMs);
            }

            candidates.Sort();
            for (var i = 0; i < candidates.Count; i++)
            {
                var time = candidates[i];
                if (time < 0.0)
                {
                    continue;
                }
                if (MeasureLineTimes.Count == 0 ||
                    Math.Abs(
                        MeasureLineTimes[MeasureLineTimes.Count - 1] - time) >
                    0.01)
                {
                    MeasureLineTimes.Add(time);
                }
            }
        }
    }

    public static class ChartTimeAxis
    {
        private const double MusicTimeToleranceMs = 0.0000005;

        public static double ToChartTime(
            double musicTimeMs,
            double delayMs)
        {
            return musicTimeMs - delayMs;
        }

        public static double ToMusicTime(
            double chartTimeMs,
            double delayMs)
        {
            return chartTimeMs + delayMs;
        }

        public static double AudioDurationToChartTime(
            double audioDurationMs,
            double delayMs)
        {
            return Math.Max(0.0, audioDurationMs - delayMs);
        }

        public static bool IsNegativeMusicTime(
            double chartTimeMs,
            double delayMs)
        {
            return ToMusicTime(chartTimeMs, delayMs) <
                   -MusicTimeToleranceMs;
        }

        public static bool ContainsNegativeMusicTime(
            Chart chart,
            double delayMs)
        {
            if (chart == null)
            {
                return false;
            }
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                if (note != null &&
                    IsNegativeMusicTime(note.TimeMs, delayMs))
                {
                    return true;
                }
            }
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var evt = chart.Events[i];
                if (evt != null &&
                    !evt.IsChartBase &&
                    IsNegativeMusicTime(evt.TimeMs, delayMs))
                {
                    return true;
                }
            }
            return false;
        }
    }

    [Serializable]
    public sealed class ChartParseDiagnostic
    {
        public string File;
        public int Line;
        public string Directive;
        public string Raw;
        public string Message;

        public override string ToString()
        {
            return File + ":" + Line + " [" + Directive + "] " + Message + " | " + Raw;
        }
    }

    public sealed class ChartParseResult
    {
        public Chart Chart;
        public readonly List<ChartParseDiagnostic> Diagnostics =
            new List<ChartParseDiagnostic>();

        public bool Success
        {
            get { return Diagnostics.Count == 0; }
        }
    }
}
