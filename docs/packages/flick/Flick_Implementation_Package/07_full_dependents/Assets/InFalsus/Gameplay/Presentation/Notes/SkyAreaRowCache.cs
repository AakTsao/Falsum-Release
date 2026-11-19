using System;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Timing;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Notes
{
    /// <summary>Fixed 5 ms samples. Only the playhead-dependent Z is evaluated per frame.</summary>
    internal sealed class SkyAreaRowCache
    {
        internal struct Row
        {
            public float Left, Right, Distance;
            public double ScaledTime;
        }

        private Row[] _rows;
        private double _time, _duration, _start, _startWidth, _end, _endWidth;
        private double _speed, _trackLength;
        private int _leftEase, _rightEase, _timelineVersion, _speedEventCount;
        private bool _ascending, _descending;

        public int Count => _rows.Length - 1;
        public int Version { get; private set; }

        public bool Prepare(Chart chart, TrackMotion motion, ChartNote note, double length)
        {
            if (_rows != null && _timelineVersion == chart.TimelineVersion &&
                _speedEventCount == chart.SpeedEvents.Count && _speed == motion.Speed &&
                _trackLength == motion.TrackLength && _time == note.TimeMs && _duration == note.DurationMs &&
                _start == note.StartPos && _startWidth == note.StartWidth &&
                _end == note.EndPos && _endWidth == note.EndWidth &&
                _leftEase == note.LeftEasing && _rightEase == note.RightEasing)
                return false;

            _time = note.TimeMs;
            _duration = note.DurationMs;
            _start = note.StartPos;
            _startWidth = note.StartWidth;
            _end = note.EndPos;
            _endWidth = note.EndWidth;
            _leftEase = note.LeftEasing;
            _rightEase = note.RightEasing;
            _speed = motion.Speed;
            _trackLength = motion.TrackLength;
            _timelineVersion = chart.TimelineVersion;
            _speedEventCount = chart.SpeedEvents.Count;
            var count = SkyAreaGeometry.SubdivisionCount(note);
            if (_rows == null || _rows.Length != count + 1) _rows = new Row[count + 1];
            _ascending = _descending = true;
            for (var i = 0; i <= count; i++)
            {
                // Preserve the original float parameter and arithmetic order.
                _rows[i] = Evaluate(chart, note, (float)i / count, length);
                if (i == 0) continue;
                _ascending &= _rows[i].ScaledTime >= _rows[i - 1].ScaledTime;
                _descending &= _rows[i].ScaledTime <= _rows[i - 1].ScaledTime;
            }
            Version++;
            return true;
        }

        public Row Get(int index) => _rows[index];

        public static Row Evaluate(Chart chart, ChartNote note, float t, double length)
        {
            t = Mathf.Clamp01(t);
            var start = Mathf.Lerp(-0.81f, 0.81f, (float)note.StartPos);
            var end = Mathf.Lerp(-0.81f, 0.81f, (float)note.EndPos);
            var startWidth = Mathf.Max(0.0001f, (float)note.StartWidth * 1.62f);
            var endWidth = Mathf.Max(0.0001f, (float)note.EndWidth * 1.62f);
            return new Row
            {
                Left = Mathf.Lerp(start - startWidth * 0.5f, end - endWidth * 0.5f,
                    SkyAreaGeometry.Ease(t, note.LeftEasing)),
                Right = Mathf.Lerp(start + startWidth * 0.5f, end + endWidth * 0.5f,
                    SkyAreaGeometry.Ease(t, note.RightEasing)),
                Distance = (float)(length * t),
                ScaledTime = chart.ScaledTimeAt(note.TimeMs + note.DurationMs * t),
            };
        }

        public static float RawZ(Row row, double scaledNow, TrackMotion motion)
        {
            // Match TrackMotion.NoteZAt, including its order of multiplication.
            return (float)((row.ScaledTime - scaledNow) / 1000.0 *
                motion.TrackLength * TrackMotion.TrackScale * motion.Speed);
        }

        public void VisibleRows(double scaledNow, TrackMotion motion, out int first, out int last)
        {
            first = 1;
            last = Count;
            // Reversing track events can make a segment nonmonotonic. Do not
            // infer visibility from its endpoints: retain the full row walk.
            if (motion.Speed < 0.0 || (!_ascending && !_descending)) return;
            if (_ascending)
            {
                first = Bound(scaledNow, motion, 0f, false, true);
                last = Math.Min(Count, Bound(scaledNow, motion, (float)motion.TrackLength, true, true));
            }
            else
            {
                first = Bound(scaledNow, motion, (float)motion.TrackLength, false, false);
                last = Math.Min(Count, Bound(scaledNow, motion, 0f, true, false));
            }
            first = Math.Max(1, first);
        }

        private int Bound(double scaledNow, TrackMotion motion, float z, bool inclusive, bool ascending)
        {
            var low = 0;
            var high = _rows.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var value = RawZ(_rows[middle], scaledNow, motion);
                var beyond = ascending ? (inclusive ? value >= z : value > z)
                    : (inclusive ? value <= z : value < z);
                if (beyond) high = middle;
                else low = middle + 1;
            }
            return low;
        }
    }
}
