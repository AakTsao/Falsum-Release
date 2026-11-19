using System;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;

namespace InFalsus.Gameplay.Core.Judgement
{
    public sealed class JudgementEvent
    {
        public ChartNote Note;
        public JudgementResult Result;
        public double InputTimeMs;
        public double DeltaMs;
        public bool HoldRelease;
    }

    public sealed class JudgementEngine
    {
        private const double StartedNoteToleranceMs = 0.01;

        private readonly Chart _chart;
        private readonly List<ChartNote> _notes;
        private readonly List<ChartNote> _activeHolds = new List<ChartNote>();

        public event Action<JudgementEvent> NoteStarted;
        public event Action<JudgementEvent> Judged;

        public int TotalNotes { get { return _notes.Count; } }
        public int JudgedCount { get; private set; }
        public int ShinyCount { get; private set; }
        public int PerfectCount { get; private set; }
        public int NearCount { get; private set; }
        public int BreakCount { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Score { get; private set; }
        public JudgementResult LastResult { get; private set; }
        public double LastDeltaMs { get; private set; }

        public double Accuracy
        {
            get
            {
                if (JudgedCount == 0)
                {
                    return 1.0;
                }
                return (ShinyCount + PerfectCount + NearCount * 0.5) / JudgedCount;
            }
        }

        public double ExactRate
        {
            get
            {
                if (JudgedCount == 0)
                {
                    return 1.0;
                }
                return (ShinyCount + PerfectCount) / (double)JudgedCount;
            }
        }

        public JudgementEngine(Chart chart)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            _chart = chart;
            _notes = new List<ChartNote>(chart.Notes);
            Reset();
        }

        public void Reset()
        {
            JudgedCount = 0;
            ShinyCount = 0;
            PerfectCount = 0;
            NearCount = 0;
            BreakCount = 0;
            Combo = 0;
            MaxCombo = 0;
            Score = 0;
            LastResult = JudgementResult.None;
            LastDeltaMs = 0.0;
            _activeHolds.Clear();

            foreach (var note in _notes)
            {
                note.Consumed = false;
                note.Judged = false;
                note.Holding = false;
                note.Result = JudgementResult.None;
                note.HoldStartResult = JudgementResult.None;
            }
        }

        public void SkipNotesStartedBefore(double timeMs)
        {
            // Mid-chart autoplay starts after the head, not on it. Notes exactly
            // at the seek position stay armed for the first playback frame.
            var cutoffMs = timeMs - StartedNoteToleranceMs;
            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.TimeMs >= cutoffMs)
                {
                    continue;
                }

                note.Consumed = true;
                note.Judged = true;
                note.Holding = false;
                note.Result = JudgementResult.None;
                note.HoldStartResult = JudgementResult.None;
            }
        }

        public void Update(double nowMs)
        {
            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Judged)
                {
                    continue;
                }

                if (note.Holding)
                {
                    if (nowMs >= note.EndTimeMs)
                    {
                        FinishHold(note, note.HoldStartResult, note.TimeMs, true);
                    }
                    continue;
                }

                if (!note.Consumed && nowMs - note.TimeMs > Judgement.MissDeadlineMs(note.Zone))
                {
                    MarkMiss(note, nowMs);
                }
            }
        }

        public JudgementResult PressLane(int lane, double nowMs)
        {
            if (lane < 0 || lane >= 6)
            {
                return JudgementResult.None;
            }

            var candidate = FindLaneCandidate(lane, nowMs);
            if (candidate == null)
            {
                return JudgementResult.None;
            }

            var delta = candidate.TimeMs - nowMs;
            var result = Judgement.Evaluate(delta, NoteZone.Lane);
            if (result == JudgementResult.None)
            {
                return result;
            }

            if (candidate.Kind == NoteKind.Hold)
            {
                candidate.Consumed = true;
                candidate.Holding = true;
                candidate.HoldStartResult = result;
                _activeHolds.Add(candidate);
                LastResult = result;
                LastDeltaMs = delta;
                RaiseNoteStarted(candidate, result, nowMs, delta);
                return result;
            }

            ApplyResult(candidate, result, nowMs, delta, false);
            return result;
        }

        public JudgementResult PressFlick(int direction, double nowMs)
        {
            if (direction != 4 && direction != 16)
            {
                return JudgementResult.None;
            }

            ChartNote candidate = null;
            var best = double.MaxValue;
            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Judged || note.Consumed || note.Kind != NoteKind.Flick)
                {
                    continue;
                }
                if (note.FlickDirection != direction)
                {
                    continue;
                }

                var delta = note.TimeMs - nowMs;
                if (delta > Judgement.BadMs || delta < -Judgement.SkyNearMs)
                {
                    continue;
                }
                var abs = Math.Abs(delta);
                if (abs < best)
                {
                    best = abs;
                    candidate = note;
                }
            }

            if (candidate == null)
            {
                return JudgementResult.None;
            }

            var candidateDelta = candidate.TimeMs - nowMs;
            var result = Judgement.Evaluate(candidateDelta, NoteZone.Sky);
            if (result != JudgementResult.None)
            {
                ApplyResult(candidate, result, nowMs, candidateDelta, false);
            }
            return result;
        }

        public JudgementResult PressSky(double nowMs)
        {
            ChartNote candidate = null;
            var best = double.MaxValue;
            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Judged || note.Consumed || note.Kind != NoteKind.SkyArea)
                {
                    continue;
                }

                var delta = note.TimeMs - nowMs;
                if (delta > Judgement.SkyNearMs || delta < -Judgement.SkyNearMs)
                {
                    continue;
                }
                var abs = Math.Abs(delta);
                if (abs < best)
                {
                    best = abs;
                    candidate = note;
                }
            }

            if (candidate == null)
            {
                return JudgementResult.None;
            }

            var candidateDelta = candidate.TimeMs - nowMs;
            var result = Judgement.Evaluate(candidateDelta, NoteZone.Sky);
            if (result != JudgementResult.None)
            {
                RaiseNoteStarted(candidate, result, nowMs, candidateDelta);
                ApplyResult(candidate, result, nowMs, candidateDelta, false);
            }
            return result;
        }

        public void ReleaseLane(int lane, double nowMs)
        {
            for (var i = _activeHolds.Count - 1; i >= 0; i--)
            {
                var note = _activeHolds[i];
                if (!note.Holding || !note.CoversLane(lane))
                {
                    continue;
                }

                if (nowMs >= note.EndTimeMs - 50.0)
                {
                    FinishHold(note, note.HoldStartResult, nowMs, false);
                }
                else
                {
                    FinishHold(note, JudgementResult.Miss, nowMs, true);
                }
            }
        }

        private ChartNote FindLaneCandidate(int lane, double nowMs)
        {
            ChartNote best = null;
            var bestDistance = double.MaxValue;

            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Judged || note.Consumed)
                {
                    continue;
                }
                if (note.Kind == NoteKind.Flick || note.Kind == NoteKind.SkyArea)
                {
                    continue;
                }
                if (!note.CoversLane(lane))
                {
                    continue;
                }

                var delta = note.TimeMs - nowMs;
                if (delta > Judgement.BadMs || delta < -Judgement.NearMs)
                {
                    continue;
                }

                var distance = Math.Abs(delta);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = note;
                }
            }

            return best;
        }

        private void FinishHold(
            ChartNote note,
            JudgementResult result,
            double inputTimeMs,
            bool release)
        {
            if (!note.Holding)
            {
                return;
            }

            note.Holding = false;
            _activeHolds.Remove(note);
            ApplyResult(note, result, inputTimeMs, 0.0, release);
        }

        private void MarkMiss(ChartNote note, double nowMs)
        {
            note.Consumed = true;
            ApplyResult(note, JudgementResult.Miss, nowMs,
                note.TimeMs - nowMs, false);
        }

        private void ApplyResult(
            ChartNote note,
            JudgementResult result,
            double inputTimeMs,
            double deltaMs,
            bool holdRelease)
        {
            if (note.Judged)
            {
                return;
            }

            note.Judged = true;
            note.Result = result;
            note.Consumed = true;
            JudgedCount++;
            LastResult = result;
            LastDeltaMs = deltaMs;

            var tier = Judgement.TierOf(result);
            switch (tier)
            {
                case JudgementTier.ExactPlus:
                    ShinyCount++;
                    Combo++;
                    break;
                case JudgementTier.Exact:
                    PerfectCount++;
                    Combo++;
                    break;
                case JudgementTier.Near:
                    NearCount++;
                    Combo++;
                    break;
                default:
                    BreakCount++;
                    Combo = 0;
                    break;
            }

            if (Combo > MaxCombo)
            {
                MaxCombo = Combo;
            }
            Score += Judgement.ScoreOf(tier);

            var handler = Judged;
            if (handler != null)
            {
                handler(CreateEvent(note, result, inputTimeMs, deltaMs, holdRelease));
            }
        }

        private void RaiseNoteStarted(
            ChartNote note,
            JudgementResult result,
            double inputTimeMs,
            double deltaMs)
        {
            var handler = NoteStarted;
            if (handler != null)
            {
                handler(CreateEvent(note, result, inputTimeMs, deltaMs, false));
            }
        }

        private static JudgementEvent CreateEvent(
            ChartNote note,
            JudgementResult result,
            double inputTimeMs,
            double deltaMs,
            bool holdRelease)
        {
            return new JudgementEvent
            {
                Note = note,
                Result = result,
                InputTimeMs = inputTimeMs,
                DeltaMs = deltaMs,
                HoldRelease = holdRelease,
            };
        }
    }
}
