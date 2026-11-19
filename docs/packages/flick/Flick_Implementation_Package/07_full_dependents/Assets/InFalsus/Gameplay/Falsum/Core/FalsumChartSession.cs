using System;
using System.Collections.Generic;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Core
{
    public sealed class FalsumChartSession
    {
        private sealed class Snapshot
        {
            public double Bpm;
            public double Division;
            public string SongId;
            public int Difficulty;
            public double DelayMs;
            public string ExternalState;
            public readonly List<ChartEvent> Events =
                new List<ChartEvent>();
            public readonly List<ChartNote> Notes =
                new List<ChartNote>();
        }

        private sealed class HistoryEntry
        {
            public string Label;
            public Snapshot State;
        }

        private readonly List<HistoryEntry> _undo =
            new List<HistoryEntry>();
        private readonly List<HistoryEntry> _redo =
            new List<HistoryEntry>();
        private Snapshot _pendingState;
        private string _pendingLabel;
        private Func<string> _captureExternalState;
        private Action<string> _restoreExternalState;

        public Chart Chart { get; private set; }
        public double DelayMs
        {
            get { return Chart.DelayMs; }
            set { Chart.DelayMs = value; }
        }
        public int UndoCount { get { return _undo.Count; } }
        public int RedoCount { get { return _redo.Count; } }
        public bool HasPendingEdit { get { return _pendingState != null; } }

        public event Action Changed;
        public event Action HistoryChanged;

        public FalsumChartSession(Chart chart, double delayMs)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            Chart = chart;
            DelayMs = delayMs;
        }

        public void Replace(Chart chart, double delayMs)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            FalsumSkyAreaGrouping.Normalize(chart);
            _pendingState = null;
            _pendingLabel = null;
            _undo.Clear();
            _redo.Clear();
            RestoreOwned(Capture(chart, delayMs));
            RaiseChanged();
            RaiseHistoryChanged();
        }

        public void SetExternalStateProvider(
            Func<string> capture,
            Action<string> restore)
        {
            _captureExternalState = capture;
            _restoreExternalState = restore;
        }

        public void BeginChange(string label)
        {
            if (_pendingState != null)
            {
                return;
            }
            _pendingState = Capture();
            _pendingLabel = label ?? L.Get("falsum.history.edit");
        }

        public bool CommitChange()
        {
            if (_pendingState == null)
            {
                return false;
            }

            var before = _pendingState;
            var label = _pendingLabel ?? L.Get("falsum.history.edit");
            try
            {
                FalsumSkyAreaGrouping.Normalize(Chart);
                Chart.InvalidateTimelineDerived();
                if (Signature(before) == Signature(Chart, DelayMs))
                {
                    _pendingState = null;
                    _pendingLabel = null;
                    return false;
                }

                _undo.Add(new HistoryEntry
                {
                    Label = label,
                    State = before,
                });
                if (_undo.Count > 100)
                {
                    _undo.RemoveAt(0);
                }
                _redo.Clear();
                _pendingState = null;
                _pendingLabel = null;
            }
            catch
            {
                CancelChange();
                throw;
            }
            RaiseChanged();
            RaiseHistoryChanged();
            return true;
        }

        public void CancelChange()
        {
            if (_pendingState == null)
            {
                return;
            }
            RestoreOwned(_pendingState);
            _pendingState = null;
            _pendingLabel = null;
            RaiseChanged();
        }

        public bool Undo()
        {
            if (_pendingState != null)
            {
                CancelChange();
                return true;
            }
            if (_undo.Count == 0)
            {
                return false;
            }

            var index = _undo.Count - 1;
            var entry = _undo[index];
            _undo.RemoveAt(index);
            _redo.Add(new HistoryEntry
            {
                Label = entry.Label,
                State = DetachCurrent(),
            });
            RestoreOwned(entry.State);
            RaiseChanged();
            RaiseHistoryChanged();
            return true;
        }

        public bool Redo()
        {
            if (_pendingState != null)
            {
                CancelChange();
                return true;
            }
            if (_redo.Count == 0)
            {
                return false;
            }

            var index = _redo.Count - 1;
            var entry = _redo[index];
            _redo.RemoveAt(index);
            _undo.Add(new HistoryEntry
            {
                Label = entry.Label,
                State = DetachCurrent(),
            });
            RestoreOwned(entry.State);
            RaiseChanged();
            RaiseHistoryChanged();
            return true;
        }

        public void ClearHistory()
        {
            if (_pendingState != null)
            {
                CancelChange();
            }
            _undo.Clear();
            _redo.Clear();
            RaiseHistoryChanged();
        }

        public bool AddEvent(ChartEvent chartEvent, string label)
        {
            if (chartEvent == null)
            {
                throw new ArgumentNullException("chartEvent");
            }
            if (chartEvent.IsChartBase)
            {
                return false;
            }
            if (Chart.Events.Contains(chartEvent))
            {
                return false;
            }

            BeginChange(label ?? L.Get("falsum.history.addEvent"));
            Chart.Events.Add(chartEvent);
            AddEventToKindList(chartEvent);
            Chart.RebuildTimeline();
            return CommitChange();
        }

        public bool RemoveEvent(ChartEvent chartEvent, string label)
        {
            if (chartEvent == null ||
                chartEvent.IsChartBase ||
                !Chart.Events.Contains(chartEvent))
            {
                return false;
            }

            BeginChange(label ?? L.Get("falsum.history.removeEvent"));
            Chart.Events.Remove(chartEvent);
            RemoveEventFromKindList(chartEvent);
            Chart.RebuildTimeline();
            return CommitChange();
        }

        public bool ApplyBatch(
            string label,
            Action<Chart> apply,
            double? delayMs = null)
        {
            if (apply == null)
            {
                throw new ArgumentNullException("apply");
            }
            if (_pendingState != null)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.chartSession.pendingBatch"));
            }

            BeginChange(label ?? L.Get("falsum.history.batchEdit"));
            try
            {
                if (delayMs.HasValue)
                {
                    if (double.IsNaN(delayMs.Value) ||
                        double.IsInfinity(delayMs.Value))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.chartSession.nonFiniteDelay"));
                    }
                    DelayMs = delayMs.Value;
                }
                apply(Chart);
                RebuildEventKindLists();
                Chart.RebuildTimeline();
                AssignIds();
                return CommitChange();
            }
            catch
            {
                CancelChange();
                throw;
            }
        }

        public void NotifyChanged()
        {
            FalsumSkyAreaGrouping.Normalize(Chart);
            Chart.RebuildTimeline();
            RaiseChanged();
        }

        private Snapshot Capture()
        {
            var snapshot = Capture(Chart, DelayMs);
            snapshot.ExternalState = _captureExternalState != null
                ? _captureExternalState()
                : null;
            return snapshot;
        }

        private static Snapshot Capture(Chart chart, double delayMs)
        {
            var snapshot = new Snapshot
            {
                Bpm = chart.Bpm,
                Division = chart.Division,
                SongId = chart.SongId,
                Difficulty = chart.Difficulty,
                DelayMs = delayMs,
            };
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var source = chart.Events[i];
                snapshot.Events.Add(new ChartEvent
                {
                    Kind = source.Kind,
                    IsChartBase = source.IsChartBase,
                    TimeMs = source.TimeMs,
                    Multiplier = source.Multiplier,
                    Bpm = source.Bpm,
                    Division = source.Division,
                    Lane = source.Lane,
                    Enabled = source.Enabled,
                    SourceLine = source.SourceLine,
                });
            }
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                snapshot.Notes.Add(CloneNote(chart.Notes[i]));
            }
            return snapshot;
        }

        private Snapshot DetachCurrent()
        {
            var snapshot = new Snapshot
            {
                Bpm = Chart.Bpm,
                Division = Chart.Division,
                SongId = Chart.SongId,
                Difficulty = Chart.Difficulty,
                DelayMs = DelayMs,
                ExternalState = _captureExternalState != null
                    ? _captureExternalState()
                    : null,
            };
            snapshot.Events.AddRange(Chart.Events);
            snapshot.Notes.AddRange(Chart.Notes);
            Chart.Events.Clear();
            Chart.SpeedEvents.Clear();
            Chart.BpmEvents.Clear();
            Chart.LaneEvents.Clear();
            Chart.Notes.Clear();
            return snapshot;
        }

        private void RestoreOwned(Snapshot snapshot)
        {
            Chart.Bpm = snapshot.Bpm;
            Chart.Division = snapshot.Division;
            Chart.SongId = snapshot.SongId;
            Chart.Difficulty = snapshot.Difficulty;
            DelayMs = snapshot.DelayMs;
            Chart.Events.Clear();
            Chart.SpeedEvents.Clear();
            Chart.BpmEvents.Clear();
            Chart.LaneEvents.Clear();
            Chart.Notes.Clear();
            Chart.InvalidateTimelineDerived();

            for (var i = 0; i < snapshot.Events.Count; i++)
            {
                var evt = snapshot.Events[i];
                Chart.Events.Add(evt);
                AddEventToKindList(evt);
            }
            for (var i = 0; i < snapshot.Notes.Count; i++)
            {
                Chart.Notes.Add(snapshot.Notes[i]);
            }
            if (!string.IsNullOrEmpty(snapshot.ExternalState) &&
                _restoreExternalState != null)
            {
                _restoreExternalState(snapshot.ExternalState);
            }
            snapshot.Events.Clear();
            snapshot.Notes.Clear();
            Chart.RebuildTimeline();
            AssignIds();
        }

        private void AddEventToKindList(ChartEvent chartEvent)
        {
            if (chartEvent.Kind == ChartEventKind.TrackSpeed)
            {
                Chart.SpeedEvents.Add(chartEvent);
            }
            else if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                Chart.BpmEvents.Add(chartEvent);
            }
            else if (chartEvent.Kind == ChartEventKind.LaneLayout)
            {
                Chart.LaneEvents.Add(chartEvent);
            }
        }

        private void RemoveEventFromKindList(ChartEvent chartEvent)
        {
            if (chartEvent.Kind == ChartEventKind.TrackSpeed)
            {
                Chart.SpeedEvents.Remove(chartEvent);
            }
            else if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                Chart.BpmEvents.Remove(chartEvent);
            }
            else if (chartEvent.Kind == ChartEventKind.LaneLayout)
            {
                Chart.LaneEvents.Remove(chartEvent);
            }
        }

        private void RebuildEventKindLists()
        {
            Chart.SpeedEvents.Clear();
            Chart.BpmEvents.Clear();
            Chart.LaneEvents.Clear();
            for (var i = 0; i < Chart.Events.Count; i++)
            {
                AddEventToKindList(Chart.Events[i]);
            }
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

        private void AssignIds()
        {
            for (var i = 0; i < Chart.Notes.Count; i++)
            {
                Chart.Notes[i].Id = i;
            }
        }

        private string Signature(Snapshot snapshot)
        {
            var builder = new StringBuilder(256);
            builder.Append(snapshot.Bpm.ToString("R"));
            builder.Append('|').Append(snapshot.Division.ToString("R"));
            builder.Append('|').Append(snapshot.SongId);
            builder.Append('|').Append(snapshot.Difficulty);
            builder.Append('|').Append(snapshot.DelayMs.ToString("R"));
            builder.Append('|').Append(snapshot.ExternalState);
            for (var i = 0; i < snapshot.Events.Count; i++)
            {
                var evt = snapshot.Events[i];
                builder.Append('|').Append((int)evt.Kind);
                builder.Append(',').Append(evt.IsChartBase ? '1' : '0');
                builder.Append(',').Append(evt.TimeMs.ToString("R"));
                builder.Append(',').Append(evt.Multiplier.ToString("R"));
                builder.Append(',').Append(evt.Bpm.ToString("R"));
                builder.Append(',').Append(evt.Division.ToString("R"));
                builder.Append(',').Append(evt.Lane);
                builder.Append(',').Append(evt.Enabled ? '1' : '0');
            }
            for (var i = 0; i < snapshot.Notes.Count; i++)
            {
                var note = snapshot.Notes[i];
                builder.Append('|').Append(note.Id);
                builder.Append(',').Append((int)note.Kind);
                builder.Append(',').Append(note.TimeMs.ToString("R"));
                builder.Append(',').Append(note.StartLane);
                builder.Append(',').Append(note.WidthLanes);
                builder.Append(',').Append(note.DurationMs.ToString("R"));
                builder.Append(',').Append(note.StartPos.ToString("R"));
                builder.Append(',').Append(note.EndPos.ToString("R"));
                builder.Append(',').Append(note.StartWidth.ToString("R"));
                builder.Append(',').Append(note.EndWidth.ToString("R"));
                builder.Append(',').Append(note.StartCellCount);
                builder.Append(',').Append(note.EndCellCount);
                builder.Append(',').Append(note.LeftEasing);
                builder.Append(',').Append(note.RightEasing);
                builder.Append(',').Append(note.GroupId);
                builder.Append(',').Append(note.FlickDirection);
            }
            return builder.ToString();
        }

        private string Signature(Chart chart, double delayMs)
        {
            var builder = new StringBuilder(256);
            builder.Append(chart.Bpm.ToString("R"));
            builder.Append('|').Append(chart.Division.ToString("R"));
            builder.Append('|').Append(chart.SongId);
            builder.Append('|').Append(chart.Difficulty);
            builder.Append('|').Append(delayMs.ToString("R"));
            builder.Append('|').Append(
                _captureExternalState != null
                    ? _captureExternalState()
                    : string.Empty);
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var evt = chart.Events[i];
                builder.Append('|').Append((int)evt.Kind);
                builder.Append(',').Append(evt.IsChartBase ? '1' : '0');
                builder.Append(',').Append(evt.TimeMs.ToString("R"));
                builder.Append(',').Append(evt.Multiplier.ToString("R"));
                builder.Append(',').Append(evt.Bpm.ToString("R"));
                builder.Append(',').Append(evt.Division.ToString("R"));
                builder.Append(',').Append(evt.Lane);
                builder.Append(',').Append(evt.Enabled ? '1' : '0');
            }
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                builder.Append('|').Append(note.Id);
                builder.Append(',').Append((int)note.Kind);
                builder.Append(',').Append(note.TimeMs.ToString("R"));
                builder.Append(',').Append(note.StartLane);
                builder.Append(',').Append(note.WidthLanes);
                builder.Append(',').Append(note.DurationMs.ToString("R"));
                builder.Append(',').Append(note.StartPos.ToString("R"));
                builder.Append(',').Append(note.EndPos.ToString("R"));
                builder.Append(',').Append(note.StartWidth.ToString("R"));
                builder.Append(',').Append(note.EndWidth.ToString("R"));
                builder.Append(',').Append(note.StartCellCount);
                builder.Append(',').Append(note.EndCellCount);
                builder.Append(',').Append(note.LeftEasing);
                builder.Append(',').Append(note.RightEasing);
                builder.Append(',').Append(note.GroupId);
                builder.Append(',').Append(note.FlickDirection);
            }
            return builder.ToString();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        private void RaiseHistoryChanged()
        {
            var handler = HistoryChanged;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
