using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Tools
{
    public sealed class FalsumToolEventRecord
    {
        public string Key;
        public ChartEvent Event;
    }

    public sealed class FalsumToolNoteRecord
    {
        public string Key;
        public ChartNote Note;
    }

    public sealed class FalsumToolChartSnapshot
    {
        public double PlayheadMs;
        public double Bpm;
        public double Division;
        public double DelayMs;
        public int Difficulty;
        public double DurationMs;
        public readonly List<FalsumToolEventRecord> Events =
            new List<FalsumToolEventRecord>();
        public readonly List<FalsumToolNoteRecord> Notes =
            new List<FalsumToolNoteRecord>();

        public static FalsumToolChartSnapshot Capture(
            Chart chart,
            double delayMs,
            double playheadMs)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            var snapshot = new FalsumToolChartSnapshot
            {
                PlayheadMs = Math.Max(0.0, Math.Round(playheadMs)),
                Bpm = chart.Bpm,
                Division = chart.Division,
                DelayMs = delayMs,
                Difficulty = chart.Difficulty,
                DurationMs = chart.EndTimeMs,
            };
            for (var i = 0; i < chart.Events.Count; i++)
            {
                var chartEvent = chart.Events[i];
                if (chartEvent.IsChartBase)
                {
                    continue;
                }
                snapshot.Events.Add(
                    new FalsumToolEventRecord
                    {
                        Key = "event:" + i.ToString(),
                        Event = chartEvent,
                    });
            }
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                snapshot.Notes.Add(
                    new FalsumToolNoteRecord
                    {
                        Key = "note:" + i.ToString(),
                        Note = chart.Notes[i],
                    });
            }
            return snapshot;
        }

        public FalsumJsonObject CreateContextObject()
        {
            var summary = new FalsumJsonObject
            {
                { "bpm", Bpm },
                { "division", Division },
                { "delayMs", DelayMs },
                { "difficulty", Difficulty },
                { "durationMs", DurationMs },
                { "eventCount", Events.Count },
                { "noteCount", Notes.Count },
            };
            var events = new List<object>();
            for (var i = 0; i < Events.Count; i++)
            {
                var item = Events[i].Event;
                events.Add(
                    new FalsumJsonObject
                    {
                        { "key", Events[i].Key },
                        { "kind", EventKindName(item.Kind) },
                        { "timeMs", item.TimeMs },
                        { "multiplier", item.Multiplier },
                        { "bpm", item.Bpm },
                        { "division", item.Division },
                        { "lane", item.Lane },
                        { "enabled", item.Enabled },
                    });
            }
            var notes = new List<object>();
            for (var i = 0; i < Notes.Count; i++)
            {
                var item = Notes[i].Note;
                notes.Add(
                    new FalsumJsonObject
                    {
                        { "key", Notes[i].Key },
                        { "id", item.Id },
                        { "kind", NoteKindName(item.Kind) },
                        { "timeMs", item.TimeMs },
                        { "endTimeMs", item.EndTimeMs },
                        { "startLane", item.StartLane },
                        { "widthLanes", item.WidthLanes },
                        { "durationMs", item.DurationMs },
                        { "startPos", item.StartPos },
                        { "endPos", item.EndPos },
                        { "startWidth", item.StartWidth },
                        { "endWidth", item.EndWidth },
                        { "startCellCount", item.StartCellCount },
                        { "endCellCount", item.EndCellCount },
                        { "leftEasing", item.LeftEasing },
                        { "rightEasing", item.RightEasing },
                        { "groupId", item.GroupId },
                        { "flickDirection", item.FlickDirection },
                    });
            }
            return new FalsumJsonObject
            {
                { "playheadMs", PlayheadMs },
                { "summary", summary },
                { "events", events },
                { "notes", notes },
            };
        }

        public FalsumToolEventRecord FindEvent(string key)
        {
            for (var i = 0; i < Events.Count; i++)
            {
                if (string.Equals(
                        Events[i].Key,
                        key,
                        StringComparison.Ordinal))
                {
                    return Events[i];
                }
            }
            return null;
        }

        public FalsumToolNoteRecord FindNote(string key)
        {
            for (var i = 0; i < Notes.Count; i++)
            {
                if (string.Equals(
                        Notes[i].Key,
                        key,
                        StringComparison.Ordinal))
                {
                    return Notes[i];
                }
            }
            return null;
        }

        public static string EventKindName(ChartEventKind kind)
        {
            if (kind == ChartEventKind.TrackSpeed)
            {
                return "track";
            }
            if (kind == ChartEventKind.Bpm)
            {
                return "bpm";
            }
            return "lane";
        }

        public static string NoteKindName(NoteKind kind)
        {
            if (kind == NoteKind.Tap)
            {
                return "tap";
            }
            if (kind == NoteKind.Hold)
            {
                return "hold";
            }
            if (kind == NoteKind.Flick)
            {
                return "flick";
            }
            return "skyArea";
        }
    }

    public sealed class FalsumToolEditBatch
    {
        private readonly List<Action<Chart>> _operations =
            new List<Action<Chart>>();
        public double? DelayMs;

        public int Count
        {
            get { return _operations.Count; }
        }

        public bool HasChanges
        {
            get { return _operations.Count > 0; }
        }

        public void Add(Action<Chart> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException("operation");
            }
            if (_operations.Count >= 50000)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.tooManyOperations"));
            }
            _operations.Add(operation);
        }

        public void Apply(Chart chart)
        {
            if (chart == null)
            {
                throw new ArgumentNullException("chart");
            }
            for (var i = 0; i < _operations.Count; i++)
            {
                _operations[i](chart);
            }
        }
    }

    public sealed class FalsumToolPendingFileWrite
    {
        public string Path;
        public byte[] Bytes;
        public bool RequiresConfirmation;
        public string Description;

        public void Execute()
        {
            if (string.IsNullOrWhiteSpace(Path) || Bytes == null)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.invalidPendingFile"));
            }
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(Path, Bytes);
        }

        public string DisplaySummary()
        {
            return L.Get(
                File.Exists(Path)
                    ? "falsum.tools.fileAction.overwrite"
                    : "falsum.tools.fileAction.create");
        }

        public static FalsumToolPendingFileWrite Text(
            string path,
            string text,
            bool requiresConfirmation,
            string description)
        {
            return new FalsumToolPendingFileWrite
            {
                Path = path,
                Bytes = new UTF8Encoding(false).GetBytes(
                    text ?? string.Empty),
                RequiresConfirmation = requiresConfirmation,
                Description = description,
            };
        }
    }

    public sealed class FalsumToolRunOutput
    {
        public string Message;
        public object Details;
        public FalsumToolReport Report;
        public FalsumToolEditBatch Batch;
        public readonly List<FalsumToolPendingFileWrite> FileWrites =
            new List<FalsumToolPendingFileWrite>();
        public readonly List<string> Notifications =
            new List<string>();

        public bool RequiresFileConfirmation
        {
            get
            {
                for (var i = 0; i < FileWrites.Count; i++)
                {
                    if (FileWrites[i].RequiresConfirmation)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public string BuildFileConfirmation()
        {
            var lines = new List<string>();
            for (var i = 0; i < FileWrites.Count; i++)
            {
                var write = FileWrites[i];
                lines.Add(
                    string.IsNullOrWhiteSpace(write.Description)
                        ? L.Get(
                            "falsum.tools.fileConfirmation.line",
                            ("action", write.DisplaySummary()),
                            ("file", Path.GetFileName(write.Path)))
                        : L.Get(
                            "falsum.tools.fileConfirmation.lineWithDescription",
                            ("action", write.DisplaySummary()),
                            ("file", Path.GetFileName(write.Path)),
                            ("description", write.Description)));
            }
            return L.Get(
                "falsum.tools.fileConfirmation.body",
                ("intro", L.Get("falsum.tools.fileConfirmation.intro")),
                ("lines", string.Join(Environment.NewLine, lines.ToArray())),
                ("summary", L.Get(
                    "falsum.tools.fileConfirmation.summary",
                    ("count", FileWrites.Count))));
        }
    }
}

