using System;
using InFalsus.Gameplay.Core.Charts;
using UnityEngine;

namespace InFalsus.Gameplay.Core.Timing
{
    public sealed class TrackMotion
    {
        public const double DefaultTrackLength = 20.181812286376953;
        public const double TrackScale = 0.3;

        private readonly Chart _chart;

        public double TrackLength { get; set; }
        public double Speed { get; set; }

        /// <summary>
        /// One-entry memo for <c>ScaledTimeAt(nowMs)</c>. Every note, every
        /// SkyArea row and every measure line asks for the SAME playhead value
        /// many thousands of times per frame; evaluating the map each time was
        /// the dominant cost of the presentation update. The memo is keyed on
        /// (time, chart timeline version) so a chart edit — which can change
        /// the map without moving the playhead — can never be served stale.
        ///
        /// This is a pure acceleration: the cached expression is the identical
        /// call, so the value is bit-identical to evaluating it fresh. A-16
        /// still holds — ScaledTimeAt remains the only conversion entry point.
        /// </summary>
        private double _nowCacheMs;
        private double _nowCacheScaled;
        private int _nowCacheVersion;
        private bool _nowCacheValid;

        public TrackMotion(Chart chart, double speedMultiplier)
        {
            _chart = chart;
            TrackLength = DefaultTrackLength;
            Speed = speedMultiplier;
            _nowCacheValid = false;
        }

        /// <summary>
        /// Scaled playhead. Memoised per (time, timeline version).
        /// </summary>
        public double ScaledNowAt(double nowMs)
        {
            var version = _chart.TimelineVersion;
            if (_nowCacheValid &&
                _nowCacheVersion == version &&
                _nowCacheMs == nowMs)
            {
                return _nowCacheScaled;
            }
            _nowCacheMs = nowMs;
            _nowCacheScaled = _chart.ScaledTimeAt(nowMs);
            _nowCacheVersion = version;
            _nowCacheValid = true;
            return _nowCacheScaled;
        }

        /// <summary>
        /// Drops the memo. Call it when a chart edit mutates SpeedEvents in
        /// place without bumping the timeline version, or when the caller
        /// deliberately wants the map re-read.
        /// </summary>
        public void InvalidateNowCache()
        {
            _nowCacheValid = false;
        }

        public double NoteZ(ChartNote note, double nowMs)
        {
            return NoteZAt(note.TimeMs, nowMs);
        }

        public double NoteZAt(double noteTimeMs, double nowMs)
        {
            var scaledDelta = _chart.ScaledTimeAt(noteTimeMs) -
                              ScaledNowAt(nowMs);
            return scaledDelta / 1000.0 * TrackLength * TrackScale * Speed;
        }

        public double HoldLength(ChartNote note)
        {
            var scaledDuration = _chart.ScaledTimeAt(note.EndTimeMs) -
                                 _chart.ScaledTimeAt(note.TimeMs);
            return scaledDuration / 1000.0 * TrackLength * TrackScale * Speed;
        }

        public double TrackTravelAt(double nowMs)
        {
            return ScaledNowAt(nowMs) / 1000.0 *
                   TrackLength * TrackScale * Speed;
        }

        public double VisibleLimit
        {
            get { return TrackLength; }
        }

        public bool IsVisible(double z)
        {
            return Math.Abs(z) < TrackLength;
        }

        public float Fade(double z)
        {
            var normalized = Math.Abs(z) / TrackLength;
            return Mathf.Clamp01((float)((1.0 - normalized) / 0.1));
        }
    }
}
