using System;
using System.Collections;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Timing;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumPitchPreservingPlayback :
        MonoBehaviour,
        IGameplayClockAudioSink
    {
        private sealed class Grain
        {
            public int StartFrame;
            public int Age;
        }

        private const int GrainLength = 4096;
        private const int OutputHop = GrainLength / 2;

        private readonly List<Grain> _grains = new List<Grain>(3);
        private readonly object _gate = new object();
        private AudioSource _source;
        private AudioClip _clip;
        private float[] _samples;
        private int _channels;
        private int _sampleFrames;
        private int _frequency;
        private double _rate = 1.0;
        private double _sourcePosition;
        private int _outputFrame;
        private int _nextGrainAt;
        private bool _ready;
        private bool _preparing;
        private bool _stopped;

        public bool Ready
        {
            get { return _ready; }
        }

        public void Initialize(AudioSource source)
        {
            _source = source;
            if (_source != null)
            {
                _source.playOnAwake = false;
                _source.loop = true;
                _source.Stop();
            }
        }

        public IEnumerator Prepare(AudioClip clip, double initialTimeMs)
        {
            if (clip == null || _preparing)
            {
                yield break;
            }
            _preparing = true;
            try
            {
                _clip = clip;
                _frequency = Math.Max(1, clip.frequency);
                _channels = Math.Max(1, clip.channels);
                _sampleFrames = Math.Max(0, clip.samples);
                var data = clip.LoadAudioData();
                while (clip.loadState == AudioDataLoadState.Loading)
                {
                    yield return null;
                }
                if (clip.loadState == AudioDataLoadState.Failed)
                {
                    throw new InvalidOperationException(
                        L.Get(
                            "falsum.error.audio.clipDataLoadFailed"));
                }
                var total = _sampleFrames * _channels;
                var samples = new float[total];
                if (!clip.GetData(samples, 0))
                {
                    throw new InvalidOperationException(
                        L.Get(
                            "falsum.error.audio.getDataFailed"));
                }
                lock (_gate)
                {
                    _samples = samples;
                    _ready = true;
                    _stopped = false;
                    ResetGrains(
                        initialTimeMs * _frequency / 1000.0);
                }
                if (_source != null)
                {
                    _source.loop = true;
                    _source.pitch = 1.0f;
                }
                Debug.Log(
                    "[Falsum] Pitch-preserving audio ready: " +
                    clip.name +
                    " frames=" + _sampleFrames +
                    " channels=" + _channels +
                    " rate=" + _rate.ToString("0.##"));
            }
            finally
            {
                _preparing = false;
            }
        }

        public void OnAudioSeek(double timeMs)
        {
            if (!_ready)
            {
                return;
            }
            lock (_gate)
            {
                ResetGrains(timeMs * _frequency / 1000.0);
                _stopped = false;
            }
        }

        public void OnAudioRate(double rate)
        {
            if (!_ready)
            {
                return;
            }
            lock (_gate)
            {
                _rate = Math.Max(0.01, rate);
            }
        }

        public void OnAudioStop()
        {
            if (!_ready)
            {
                return;
            }
            lock (_gate)
            {
                _stopped = true;
                _grains.Clear();
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_stopped)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            if (!_ready || _samples == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            lock (_gate)
            {
                Generate(data, channels);
            }
        }

        private void Generate(float[] data, int outputChannels)
        {
            if (_rate >= 0.9999)
            {
                WriteNormalSpeed(data, outputChannels);
                return;
            }

            var frames = data.Length / outputChannels;
            var hopIn = Math.Max(
                1,
                (int)Math.Round(OutputHop * _rate));
            for (var frame = 0; frame < frames; frame++)
            {
                if (_outputFrame == _nextGrainAt)
                {
                    SpawnGrain(
                        (int)Math.Round(_sourcePosition));
                    _sourcePosition += hopIn;
                    _nextGrainAt += OutputHop;
                }

                for (var channel = 0; channel < outputChannels; channel++)
                {
                    data[frame * outputChannels + channel] = 0.0f;
                }
                var weightTotal = 0.0f;
                for (var i = 0; i < _grains.Count; i++)
                {
                    var grain = _grains[i];
                    if (grain.Age < 0 || grain.Age >= GrainLength)
                    {
                        continue;
                    }
                    var sourceFrame = grain.StartFrame + grain.Age;
                    var weight = Hann(grain.Age);
                    for (var channel = 0;
                         channel < outputChannels;
                         channel++)
                    {
                        data[frame * outputChannels + channel] +=
                            ReadSample(
                            sourceFrame,
                            channel,
                            outputChannels) *
                            weight;
                    }
                    weightTotal += weight;
                    grain.Age++;
                }
                if (weightTotal > 0.00001f)
                {
                    for (var channel = 0;
                         channel < outputChannels;
                         channel++)
                    {
                        data[frame * outputChannels + channel] /=
                            weightTotal;
                    }
                }
                _outputFrame++;
                while (_grains.Count > 0 &&
                       _grains[0].Age >= GrainLength)
                {
                    _grains.RemoveAt(0);
                }
            }
        }

        private void WriteNormalSpeed(float[] data, int outputChannels)
        {
            var frames = data.Length / outputChannels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sourceFrame = (int)Math.Round(_sourcePosition);
                for (var channel = 0; channel < outputChannels; channel++)
                {
                    data[frame * outputChannels + channel] =
                        ReadSample(sourceFrame, channel, outputChannels);
                }
                _sourcePosition += 1.0;
            }
        }

        private void ResetGrains(double sourceFrame)
        {
            _grains.Clear();
            _outputFrame = 0;
            _nextGrainAt = OutputHop;
            var hopIn = Math.Max(
                1,
                (int)Math.Round(OutputHop * _rate));
            if (_rate < 0.9999)
            {
                _grains.Add(new Grain
                {
                    StartFrame = Math.Max(
                        0,
                        (int)Math.Round(sourceFrame) - hopIn),
                    Age = OutputHop,
                });
                _grains.Add(new Grain
                {
                    StartFrame = Math.Max(
                        0,
                        (int)Math.Round(sourceFrame)),
                    Age = 0,
                });
                _sourcePosition = sourceFrame + hopIn;
            }
            else
            {
                _sourcePosition = sourceFrame;
            }
        }

        private void SpawnGrain(int startFrame)
        {
            _grains.Add(new Grain
            {
                StartFrame = Math.Max(0, startFrame),
                Age = 0,
            });
        }

        private float ReadSample(
            int sourceFrame,
            int outputChannel,
            int outputChannels)
        {
            if (sourceFrame < 0 ||
                sourceFrame >= _sampleFrames ||
                _samples == null)
            {
                return 0.0f;
            }
            if (outputChannels == 1 && _channels == 2)
            {
                var index = sourceFrame * 2;
                return (_samples[index] + _samples[index + 1]) * 0.5f;
            }
            if (outputChannels == 2 && _channels == 1)
            {
                return _samples[sourceFrame];
            }
            var sourceChannel = Mathf.Clamp(
                outputChannel,
                0,
                _channels - 1);
            return _samples[sourceFrame * _channels + sourceChannel];
        }

        private static float Hann(int age)
        {
            if (age <= 0 || age >= GrainLength - 1)
            {
                return 0.0f;
            }
            return 0.5f *
                   (1.0f - Mathf.Cos(
                       2.0f * Mathf.PI * age /
                       (GrainLength - 1)));
        }
    }
}
