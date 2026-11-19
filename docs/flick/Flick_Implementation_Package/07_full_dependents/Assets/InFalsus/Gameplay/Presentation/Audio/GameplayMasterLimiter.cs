using System;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Audio
{
    [DisallowMultipleComponent]
    public sealed class GameplayMasterLimiter : MonoBehaviour
    {
        public const float CeilingDb = -1.5f;
        public const float LookaheadMs = 2.0f;
        public const float ReleaseMs = 120.0f;

        private GameplayLimiterDsp _processor;

        private void OnEnable()
        {
            _processor = CreateProcessor(2);
        }

        private void OnDisable()
        {
            _processor = null;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0 || channels <= 0)
            {
                return;
            }

            if (_processor == null || _processor.Channels != channels)
            {
                _processor = CreateProcessor(channels);
            }
            _processor.Process(data);
        }

        private static GameplayLimiterDsp CreateProcessor(int channels)
        {
            var sampleRate = AudioSettings.outputSampleRate > 0
                ? AudioSettings.outputSampleRate
                : 48000;
            return new GameplayLimiterDsp(
                sampleRate,
                channels,
                CeilingDb,
                LookaheadMs,
                ReleaseMs);
        }
    }

    public sealed class GameplayLimiterDsp
    {
        private readonly int _channels;
        private readonly float _ceiling;
        private readonly float _releaseCoefficient;
        private readonly float[] _delayBuffer;
        private int _delayIndex;
        private float _gain = 1.0f;

        public int Channels { get { return _channels; } }
        public float Ceiling { get { return _ceiling; } }

        public GameplayLimiterDsp(
            int sampleRate,
            int channels,
            float ceilingDb,
            float lookaheadMs,
            float releaseMs)
        {
            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException("channels");
            }

            sampleRate = Math.Max(1, sampleRate);
            _channels = channels;
            _ceiling = Mathf.Clamp(
                Mathf.Pow(10.0f, ceilingDb / 20.0f),
                0.001f,
                1.0f);

            var lookaheadFrames = Math.Max(
                1,
                Mathf.RoundToInt(sampleRate * Mathf.Max(0.1f, lookaheadMs) / 1000.0f));
            _delayBuffer = new float[lookaheadFrames * channels];

            var releaseSamples = Math.Max(
                1.0f,
                sampleRate * Mathf.Max(1.0f, releaseMs) / 1000.0f);
            _releaseCoefficient =
                1.0f - Mathf.Exp(-1.0f / releaseSamples);
        }

        public void Process(float[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            // Detect each incoming peak one look-ahead window before that
            // sample reaches the output, then clamp as a final safeguard.
            var frames = data.Length / _channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var input = frame * _channels;
                var peak = 0.0f;
                for (var channel = 0; channel < _channels; channel++)
                {
                    peak = Math.Max(peak, Math.Abs(data[input + channel]));
                }

                var targetGain = peak > _ceiling
                    ? _ceiling / peak
                    : 1.0f;
                if (targetGain < _gain)
                {
                    _gain = targetGain;
                }
                else
                {
                    _gain += (1.0f - _gain) * _releaseCoefficient;
                    if (_gain > 1.0f)
                    {
                        _gain = 1.0f;
                    }
                }

                var delayed = _delayIndex;
                for (var channel = 0; channel < _channels; channel++)
                {
                    var sample = _delayBuffer[delayed + channel];
                    _delayBuffer[delayed + channel] = data[input + channel];
                    data[input + channel] = Mathf.Clamp(
                        sample * _gain,
                        -_ceiling,
                        _ceiling);
                }

                _delayIndex += _channels;
                if (_delayIndex >= _delayBuffer.Length)
                {
                    _delayIndex = 0;
                }
            }
        }
    }
}
