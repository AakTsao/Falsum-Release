using System;
using UnityEngine;

namespace InFalsus.Gameplay.Core.Timing
{
    public interface IGameplayClockAudioSink
    {
        void OnAudioSeek(double timeMs);
        void OnAudioRate(double rate);
        void OnAudioStop();
    }

    public interface IGameplayClockAudioPosition
    {
        bool HasAudioPosition { get; }
        double AudioPositionMs { get; }
        double AudioPositionDspTime { get; }
        double SchedulingLeadMs { get; }
        double AudioScheduleTimeMs { get; }
    }

    public interface IGameplayClock
    {
        double TimeMs { get; }
        bool IsPlaying { get; }
        double DurationMs { get; }
        double AudioOffsetMs { get; set; }
        void StartAt(double timeMs);
        void Pause();
        void Resume();
        void Seek(double timeMs);
        void Stop();
    }

    public sealed class GameplayClock : MonoBehaviour, IGameplayClock
    {
        private AudioSource _audio;
        private double _anchorMs;
        private double _anchorDsp;
        private double _anchorRealtime;
        private double _pausedMs;
        private bool _running;
        private bool _needsAudioRestart = true;
        private double _playbackRate = 1.0;
        private IGameplayClockAudioSink _audioSink;
        private IGameplayClockAudioPosition _audioPosition;

        public double AudioOffsetMs { get; set; }
        public bool IsPlaying { get { return _running; } }
        public double PlaybackRate
        {
            get { return _playbackRate; }
        }

        public double AudioScheduleTimeMs
        {
            get
            {
                if (_running &&
                    _audioPosition != null &&
                    _audioPosition.HasAudioPosition)
                {
                    return _audioPosition.AudioScheduleTimeMs +
                           AudioOffsetMs;
                }
                return TimeMs;
            }
        }

        public double TimeMs
        {
            get
            {
                if (!_running)
                {
                    return _pausedMs + AudioOffsetMs;
                }

                if (_audioPosition != null &&
                    _audioPosition.HasAudioPosition)
                {
                    return SmoothAudioPositionMs(true) +
                           AudioOffsetMs;
                }

                if (_audio != null && _audio.clip != null && _audio.isPlaying)
                {
                    return _anchorMs +
                           (AudioSettings.dspTime - _anchorDsp) *
                           1000.0 * _playbackRate +
                           AudioOffsetMs;
                }

                return _anchorMs +
                       (Time.realtimeSinceStartupAsDouble - _anchorRealtime) *
                       1000.0 * _playbackRate +
                       AudioOffsetMs;
            }
        }

        public double DurationMs
        {
            get
            {
                return _audio != null && _audio.clip != null
                    ? _audio.clip.length * 1000.0
                    : 0.0;
            }
        }

        public void Initialize(AudioSource audio, double audioOffsetMs)
        {
            _audio = audio;
            AudioOffsetMs = audioOffsetMs;
            _pausedMs = 0.0;
            _running = false;
            _needsAudioRestart = true;
        }

        public void SetAudioSink(IGameplayClockAudioSink sink)
        {
            _audioSink = sink;
            _audioPosition = sink as IGameplayClockAudioPosition;
            if (_audioSink != null)
            {
                _audioSink.OnAudioRate(_playbackRate);
            }
        }

        public void StartAt(double timeMs)
        {
            _anchorMs = Math.Max(0.0, timeMs);
            _pausedMs = _anchorMs;
            _anchorDsp = AudioSettings.dspTime;
            _anchorRealtime = Time.realtimeSinceStartupAsDouble;
            _running = true;
            _needsAudioRestart = false;

            if (_audio != null && _audio.clip != null)
            {
                _audio.Stop();
                _audio.time = (float)(_anchorMs / 1000.0);
                _audio.pitch = _audioSink != null
                    ? 1.0f
                    : (float)_playbackRate;
                if (_audioSink != null)
                {
                    _audioSink.OnAudioSeek(_anchorMs);
                    _audioSink.OnAudioRate(_playbackRate);
                }
                _audio.Play();
                _anchorDsp = AudioSettings.dspTime;
                _anchorRealtime = Time.realtimeSinceStartupAsDouble;
            }
        }

        public void Pause()
        {
            if (_running)
            {
                _pausedMs = CurrentSongPositionMs();
            }
            _running = false;
            _needsAudioRestart = false;
            if (_audio != null && _audio.isPlaying)
            {
                _audio.Pause();
            }
            if (_audioSink != null)
            {
                _audioSink.OnAudioStop();
            }
        }

        public void Resume()
        {
            if (_running)
            {
                return;
            }
            if (_needsAudioRestart ||
                _audio == null ||
                _audio.clip == null)
            {
                StartAt(_pausedMs);
                return;
            }
            _anchorMs = _pausedMs;
            _anchorDsp = AudioSettings.dspTime;
            _anchorRealtime = Time.realtimeSinceStartupAsDouble;
            _running = true;
            if (_audioSink != null)
            {
                _audioSink.OnAudioSeek(_pausedMs);
                _audioSink.OnAudioRate(_playbackRate);
            }
            if (_audio != null && _audio.clip != null && !_audio.isPlaying)
            {
                _audio.UnPause();
            }
        }

        public void Seek(double timeMs)
        {
            _pausedMs = Math.Max(0.0, timeMs);
            if (_running)
            {
                StartAt(_pausedMs);
            }
            else
            {
                _needsAudioRestart = true;
                if (_audio != null && _audio.isPlaying)
                {
                    _audio.Pause();
                }
                if (_audioSink != null)
                {
                    _audioSink.OnAudioStop();
                }
            }
        }

        public void Stop()
        {
            _pausedMs = 0.0;
            _running = false;
            _needsAudioRestart = true;
            if (_audio != null)
            {
                _audio.Stop();
            }
            if (_audioSink != null)
            {
                _audioSink.OnAudioStop();
            }
        }

        public void SetPlaybackRate(double rate)
        {
            rate = Math.Max(0.01, rate);
            if (Math.Abs(_playbackRate - rate) < 0.000001)
            {
                return;
            }
            var wasRunning = _running;
            var current = CurrentSongPositionMs();
            _playbackRate = rate;
            _anchorMs = Math.Max(0.0, current);
            _pausedMs = _anchorMs;
            _anchorDsp = AudioSettings.dspTime;
            _anchorRealtime = Time.realtimeSinceStartupAsDouble;
            if (!_running)
            {
                _needsAudioRestart = true;
            }
            if (_audio != null)
            {
                _audio.pitch = _audioSink != null
                    ? 1.0f
                    : (float)_playbackRate;
            }
            if (_audioSink != null)
            {
                _audioSink.OnAudioRate(_playbackRate);
                if (wasRunning)
                {
                    _audioSink.OnAudioSeek(current);
                }
                else
                {
                    _audioSink.OnAudioStop();
                }
            }
        }

        private double CurrentSongPositionMs()
        {
            if (_audioPosition != null &&
                _audioPosition.HasAudioPosition)
            {
                return Math.Max(
                    0.0,
                    SmoothAudioPositionMs(false));
            }

            if (!_running)
            {
                return _pausedMs;
            }

            if (_audio != null && _audio.clip != null && _audio.isPlaying)
            {
                return _anchorMs +
                       (AudioSettings.dspTime - _anchorDsp) *
                       1000.0 * _playbackRate;
            }

            return _anchorMs +
                   (Time.realtimeSinceStartupAsDouble - _anchorRealtime) *
                   1000.0 * _playbackRate;
        }

        private double SmoothAudioPositionMs(bool includeSchedulingLead)
        {
            if (_audioPosition == null ||
                !_audioPosition.HasAudioPosition)
            {
                return TimeMs;
            }
            var position = _audioPosition.AudioPositionMs +
                           (includeSchedulingLead
                               ? _audioPosition.SchedulingLeadMs
                               : 0.0);
            var anchorDsp = _audioPosition.AudioPositionDspTime;
            if (anchorDsp > 0.0)
            {
                var elapsedMs =
                    (AudioSettings.dspTime - anchorDsp) *
                    1000.0 *
                    _playbackRate;
                var maxAdvanceMs = Math.Max(
                    50.0,
                    _audioPosition.SchedulingLeadMs * 2.0);
                if (elapsedMs > 0.0)
                {
                    position += Math.Min(
                        elapsedMs,
                        maxAdvanceMs);
                }
            }
            return position;
        }
    }
}
