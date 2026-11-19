using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using InFalsus.Gameplay.Core.Timing;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Audio
{
    public sealed class FalsumBassAudioEngine :
        MonoBehaviour,
        IGameplayClockAudioSink,
        IGameplayClockAudioPosition
    {
        private const int OutputChannels = 2;
        private const int MixerPositionRecordMs = 10000;
        private const double PositionScale = 1000000.0;

        private static readonly object InitGate = new object();
        private static int ActiveInstanceCount;

        /// <summary>
        /// Guards the BASS handles (<c>_mixer</c>, <c>_bgmTempo</c>,
        /// <c>_bgmSource</c>) against the ONE genuine cross-thread race here:
        /// <see cref="OnAudioFilterRead"/> runs on Unity's audio thread and
        /// reads them every buffer, while <see cref="StopBgm"/>,
        /// <see cref="ClearTransientChannels"/> and <see cref="Dispose"/> free
        /// them on the main thread.
        ///
        /// <para>Without this, tearing down the mixer while the audio thread is
        /// inside <c>BassMix.ChannelGetPosition</c> hands BASS a freed handle.
        /// The native call then faults on a non-main thread, which is what makes
        /// Unity try to reach its crash report writer off-thread and log
        /// "CrashReporter::GetInsightsSignedUrlAsync should only be called from
        /// the main thread". It appeared intermittently because it needs the
        /// free to land inside one audio buffer.</para>
        ///
        /// <para>Deliberately separate from <see cref="InitGate"/>: that one is
        /// static and serialises process-wide BASS init, while this one is per
        /// engine instance and must be held for a whole audio buffer.</para>
        /// </summary>
        private readonly object _handleGate = new object();

        private readonly Dictionary<int, byte[]> _clipWavCache =
            new Dictionary<int, byte[]>();
        private readonly Dictionary<string, int> _loopChannels =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private AudioSource _driver;
        private AudioClip _bgmClip;
        private string _bgmPath;
        private float _songGain = 1.0f;

        private int _mixer;
        private int _bgmSource;
        private int _bgmTempo;
        private int _sampleRate;
        private int _lastOutputFrames;
        private float[] _mixBuffer;

        private long _audioPositionMicros;
        private long _latestPositionMicros;
        private long _audioPositionDspBits;
        private long _schedulingLeadMicros;
        private double _rate = 1.0;
        private bool _initialized;
        private bool _active;
        private bool _hasRendered;
        private bool _disposed;

        public bool IsAvailable
        {
            get { return _initialized && _mixer != 0; }
        }

        public bool HasAudioPosition
        {
            get { return _initialized && _active && _bgmTempo != 0; }
        }

        public double AudioPositionMs
        {
            get
            {
                return Interlocked.Read(ref _audioPositionMicros) /
                       PositionScale;
            }
        }

        public double AudioPositionDspTime
        {
            get
            {
                return BitConverter.Int64BitsToDouble(
                    Interlocked.Read(ref _audioPositionDspBits));
            }
        }

        public double SchedulingLeadMs
        {
            get
            {
                if (_rate >= 0.9999 || !_hasRendered)
                {
                    return 0.0;
                }
                return Interlocked.Read(ref _schedulingLeadMicros) /
                       PositionScale;
            }
        }

        public double AudioScheduleTimeMs
        {
            get
            {
                if (!_hasRendered)
                {
                    return AudioPositionMs;
                }
                var latest = Interlocked.Read(
                    ref _latestPositionMicros) /
                    PositionScale;
                var nextBufferMs = _lastOutputFrames /
                                   (double)Math.Max(1, _sampleRate) *
                                   1000.0 *
                                   _rate;
                return latest + Math.Max(0.0, nextBufferMs);
            }
        }

        public int LoadedClipCount
        {
            get { return _clipWavCache.Count; }
        }

        public bool TryInitialize(
            AudioSource driver,
            float songGain,
            out string error)
        {
            error = null;
            if (_initialized)
            {
                return true;
            }
            if (driver == null)
            {
                error = "BASS requires an AudioSource callback driver.";
                return false;
            }

            try
            {
                _driver = driver;
                _songGain = Mathf.Clamp01(songGain);
                _sampleRate = AudioSettings.outputSampleRate > 0
                    ? AudioSettings.outputSampleRate
                    : 48000;

                lock (InitGate)
                {
                    if (ActiveInstanceCount == 0 &&
                        !Bass.Init(
                            0,
                            _sampleRate,
                            DeviceInitFlags.Stereo,
                            IntPtr.Zero,
                            IntPtr.Zero) &&
                        Bass.LastError != Errors.Already)
                    {
                        error = "BASS_Init failed: " +
                                Bass.LastError + ".";
                        return false;
                    }
                    ActiveInstanceCount++;
                }

                BassMix.MixerPositionEx = MixerPositionRecordMs;
                _mixer = BassMix.CreateMixerStream(
                    _sampleRate,
                    OutputChannels,
                    BassFlags.Decode |
                    BassFlags.Float |
                    BassFlags.MixerNonStop |
                    BassFlags.MixerPositionEx);
                if (_mixer == 0)
                {
                    error = "BASSMix mixer creation failed: " +
                            Bass.LastError + ".";
                    ReleaseBassInstance();
                    return false;
                }

                _driver.playOnAwake = false;
                _driver.loop = true;
                _driver.spatialBlend = 0.0f;
                _driver.pitch = 1.0f;
                _driver.volume = 1.0f;
                _initialized = true;
                return true;
            }
            catch (Exception exception)
            {
                error = "BASS initialization failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message;
                ReleaseNativeResources();
                return false;
            }
        }

        public void SetSongGain(float gain)
        {
            _songGain = Mathf.Clamp01(gain);
            if (!_initialized)
            {
                return;
            }
            if (_bgmTempo != 0)
            {
                Bass.ChannelSetAttribute(
                    _bgmTempo,
                    ChannelAttribute.Volume,
                    _songGain);
            }
            else if (_bgmSource != 0)
            {
                Bass.ChannelSetAttribute(
                    _bgmSource,
                    ChannelAttribute.Volume,
                    _songGain);
            }
        }

        public bool PrepareBgm(
            AudioClip clip,
            string filePath,
            float songGain,
            out string error)
        {
            lock (_handleGate)
            {
                error = null;
                if (!_initialized)
                {
                    error = "BASS is not initialized.";
                    return false;
                }

                _bgmClip = clip;
                _bgmPath = !string.IsNullOrWhiteSpace(filePath) &&
                           File.Exists(filePath)
                    ? filePath
                    : null;
                _songGain = Mathf.Clamp01(songGain);
                return RebuildBgm(0.0, out error);
            }
        }

        public void OnAudioSeek(double timeMs)
        {
            lock (_handleGate)
            {
                if (!_initialized)
                {
                    return;
                }

                ClearTransientChannels();
                string error;
                if (!RebuildBgm(Math.Max(0.0, timeMs), out error))
                {
                    Debug.LogError("[Falsum] BASS seek failed: " + error);
                    _active = false;
                    return;
                }

                _active = true;
                _hasRendered = false;
                StorePositions(Math.Max(0.0, timeMs), Math.Max(0.0, timeMs), 0.0);
            }
        }

        public void OnAudioRate(double rate)
        {
            _rate = Math.Max(0.01, rate);
            if (!_initialized || _bgmTempo == 0)
            {
                return;
            }

            var tempo = (_rate - 1.0) * 100.0;
            if (!Bass.ChannelSetAttribute(
                    _bgmTempo,
                    ChannelAttribute.Tempo,
                    (float)tempo))
            {
                Debug.LogWarning(
                    "[Falsum] BASS tempo update failed: " +
                    Bass.LastError + ".");
            }
        }

        public void OnAudioStop()
        {
            lock (_handleGate)
            {
                _active = false;
                _hasRendered = false;
                Interlocked.Exchange(ref _schedulingLeadMicros, 0);
                if (_initialized)
                {
                    ClearTransientChannels();
                }
            }
        }

        public bool PlayOneShot(
            AudioClip clip,
            float gain,
            double targetSourceTimeMs = double.NaN)
        {
            if (!_initialized || _mixer == 0 || clip == null)
            {
                return false;
            }

            var wav = GetOrCreateWav(clip);
            if (wav == null)
            {
                return false;
            }

            int channel;
            try
            {
                channel = Bass.CreateStream(
                    wav,
                    0,
                    wav.LongLength,
                    BassFlags.Decode |
                    BassFlags.Float);
            }
            catch
            {
                return false;
            }
            if (channel == 0)
            {
                Debug.LogWarning(
                    "[Falsum] BASS note stream failed: " +
                    Bass.LastError + ".");
                return false;
            }

            Bass.ChannelSetAttribute(
                channel,
                ChannelAttribute.Volume,
                Mathf.Clamp01(gain));

            var startBytes = ComputeMixerStartBytes(targetSourceTimeMs);
            var flags = BassFlags.AutoFree |
                        BassFlags.MixerChanDownMix;
            var added = BassMix.MixerAddChannel(
                _mixer,
                channel,
                flags,
                startBytes,
                0);
            if (!added)
            {
                Bass.StreamFree(channel);
                Debug.LogWarning(
                    "[Falsum] BASS note mix failed: " +
                    Bass.LastError + ".");
                return false;
            }
            return true;
        }

        public bool SetLoop(
            string key,
            AudioClip clip,
            float gain,
            bool shouldPlay)
        {
            lock (_handleGate)
            {
                if (!_initialized || _mixer == 0)
                {
                    return false;
                }
                if (string.IsNullOrEmpty(key))
                {
                    key = clip != null ? clip.GetInstanceID().ToString() : "loop";
                }

                int existing;
                if (!shouldPlay || clip == null)
                {
                    if (_loopChannels.TryGetValue(key, out existing))
                    {
                        BassMix.MixerRemoveChannel(existing);
                        _loopChannels.Remove(key);
                    }
                    return true;
                }
                if (_loopChannels.ContainsKey(key))
                {
                    Bass.ChannelSetAttribute(
                        _loopChannels[key],
                        ChannelAttribute.Volume,
                        Mathf.Clamp01(gain));
                    return true;
                }

                var wav = GetOrCreateWav(clip);
                if (wav == null)
                {
                    return false;
                }

                var channel = Bass.CreateStream(
                    wav,
                    0,
                    wav.LongLength,
                    BassFlags.Decode |
                    BassFlags.Float |
                    BassFlags.Loop);
                if (channel == 0)
                {
                    return false;
                }

                Bass.ChannelSetAttribute(
                    channel,
                    ChannelAttribute.Volume,
                    Mathf.Clamp01(gain));
                if (!BassMix.MixerAddChannel(
                        _mixer,
                        channel,
                        BassFlags.AutoFree |
                        BassFlags.MixerChanDownMix))
                {
                    Bass.StreamFree(channel);
                    return false;
                }

                _loopChannels[key] = channel;
                return true;
            }
        }

        public void ResetLoops()
        {
            lock (_handleGate)
            {
                var channels = new List<int>(_loopChannels.Values);
                _loopChannels.Clear();
                for (var i = 0; i < channels.Count; i++)
                {
                    if (channels[i] != 0)
                    {
                        BassMix.MixerRemoveChannel(channels[i]);
                    }
                }
            }
        }

        public void StopTransientChannels()
        {
            if (_initialized)
            {
                ClearTransientChannels();
            }
        }

        public int ActiveMixerChannelCount
        {
            get
            {
                if (_mixer == 0)
                {
                    return 0;
                }
                var channels = BassMix.MixerGetChannels(_mixer);
                return channels != null ? channels.Length : 0;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || data.Length == 0 || channels <= 0)
            {
                return;
            }
            // Same gate the main-thread teardown takes, so a buffer can never
            // read a handle while StopBgm/Dispose is freeing it. The body is
            // bounded (one buffer of mixing) so the main thread is not stalled
            // meaningfully.
            lock (_handleGate)
            {
                if (!_active || !_initialized || _mixer == 0)
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                var frames = data.Length / channels;
                var mixSamples = frames * OutputChannels;
                EnsureMixBuffer(mixSamples);
                var bytes = Bass.ChannelGetData(
                    _mixer,
                    _mixBuffer,
                    mixSamples * sizeof(float));
                if (bytes <= 0)
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                var decodedSamples = Math.Min(
                    mixSamples,
                    bytes / sizeof(float));
                MapToUnityOutput(data, channels, frames, decodedSamples);
                UpdatePositionFromMixer(frames);
            }
        }

        private void MapToUnityOutput(
            float[] data,
            int outputChannels,
            int frames,
            int decodedSamples)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var leftIndex = frame * OutputChannels;
                var rightIndex = leftIndex + 1;
                var left = leftIndex < decodedSamples
                    ? _mixBuffer[leftIndex]
                    : 0.0f;
                var right = rightIndex < decodedSamples
                    ? _mixBuffer[rightIndex]
                    : left;
                var output = frame * outputChannels;

                if (outputChannels == 1)
                {
                    data[output] = (left + right) * 0.5f;
                    continue;
                }

                data[output] = left;
                if (outputChannels >= 2)
                {
                    data[output + 1] = right;
                }
                for (var channel = 2; channel < outputChannels; channel++)
                {
                    data[output + channel] = 0.0f;
                }
            }
        }

        private void UpdatePositionFromMixer(int frames)
        {
            _lastOutputFrames = frames;
            _hasRendered = true;

            var delayBytes = frames *
                             OutputChannels *
                             sizeof(float);
            var latest = BassMix.ChannelGetPosition(
                _bgmTempo,
                PositionFlags.Bytes);
            var audible = BassMix.ChannelGetPosition(
                _bgmTempo,
                PositionFlags.Bytes,
                delayBytes);
            if (latest < 0)
            {
                return;
            }
            if (audible < 0)
            {
                audible = latest;
            }

            var audibleMs = Bass.ChannelBytes2Seconds(
                _bgmSource,
                audible) * 1000.0;
            var latestMs = Bass.ChannelBytes2Seconds(
                _bgmSource,
                latest) * 1000.0;
            var fallbackLeadMs = frames /
                                 (double)Math.Max(1, _sampleRate) *
                                 1000.0 *
                                 _rate;
            var leadMs = latestMs > audibleMs
                ? latestMs - audibleMs
                : fallbackLeadMs;
            StorePositions(audibleMs, latestMs, Math.Max(0.0, leadMs));
        }

        private bool RebuildBgm(double timeMs, out string error)
        {
            lock (_handleGate)
            {
                error = null;
                StopBgm();

                try
                {
                    _bgmSource = CreateBgmSource(out error);
                    if (_bgmSource == 0)
                    {
                        return false;
                    }

                    if (timeMs > 0.0)
                    {
                        var position = Bass.ChannelSeconds2Bytes(
                            _bgmSource,
                            timeMs / 1000.0);
                        Bass.ChannelSetPosition(
                            _bgmSource,
                            position,
                            PositionFlags.Bytes);
                    }

                    Bass.ChannelSetAttribute(
                        _bgmSource,
                        ChannelAttribute.Volume,
                        1.0f);

                    _bgmTempo = BassFx.TempoCreate(
                        _bgmSource,
                        BassFlags.Decode |
                        BassFlags.Float |
                        BassFlags.FxFreeSource);
                    if (_bgmTempo == 0)
                    {
                        error = "BASS_FX tempo creation failed: " +
                                Bass.LastError + ".";
                        Bass.StreamFree(_bgmSource);
                        _bgmSource = 0;
                        return false;
                    }

                    Bass.ChannelSetAttribute(
                        _bgmTempo,
                        ChannelAttribute.Volume,
                        _songGain);
                    Bass.ChannelSetAttribute(
                        _bgmTempo,
                        ChannelAttribute.Tempo,
                        (float)((_rate - 1.0) * 100.0));
                    if (!BassMix.MixerAddChannel(
                            _mixer,
                            _bgmTempo,
                            BassFlags.MixerChanDownMix |
                            BassFlags.MixerChanNoRampin))
                    {
                        error = "BASSMix BGM add failed: " +
                                Bass.LastError + ".";
                        StopBgm();
                        return false;
                    }
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.GetType().Name + ": " +
                            exception.Message;
                    StopBgm();
                    return false;
                }
            }
        }

        private int CreateBgmSource(out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(_bgmPath))
            {
                var flags = BassFlags.Decode |
                            BassFlags.Float |
                            BassFlags.Prescan |
                            BassFlags.AsyncFile;
                if (Application.platform == RuntimePlatform.WindowsPlayer ||
                    Application.platform == RuntimePlatform.WindowsEditor)
                {
                    flags |= BassFlags.Unicode;
                }
                var stream = Bass.CreateStream(
                    _bgmPath,
                    0,
                    0,
                    flags);
                if (stream == 0)
                {
                    error = "BASS could not open " + _bgmPath +
                            ": " + Bass.LastError + ".";
                }
                return stream;
            }

            if (_bgmClip == null)
            {
                error = "No BGM clip or file was supplied.";
                return 0;
            }
            var wav = GetOrCreateWav(_bgmClip);
            if (wav == null)
            {
                error = "BGM clip could not be converted to PCM.";
                return 0;
            }
            var memoryStream = Bass.CreateStream(
                wav,
                0,
                wav.LongLength,
                BassFlags.Decode |
                BassFlags.Float |
                BassFlags.Prescan);
            if (memoryStream == 0)
            {
                error = "BASS could not decode the BGM clip: " +
                        Bass.LastError + ".";
            }
            return memoryStream;
        }

        private long ComputeMixerStartBytes(double targetSourceTimeMs)
        {
            if (double.IsNaN(targetSourceTimeMs) || !_hasRendered)
            {
                return 0;
            }

            var currentSourceMs = Interlocked.Read(
                ref _latestPositionMicros) /
                PositionScale;
            var sourceDeltaMs = targetSourceTimeMs - currentSourceMs;
            if (sourceDeltaMs <= 0.0)
            {
                return 0;
            }

            var outputSeconds = sourceDeltaMs /
                                1000.0 /
                                Math.Max(0.01, _rate);
            return Math.Max(
                0,
                Bass.ChannelSeconds2Bytes(_mixer, outputSeconds));
        }

        private void StopBgm()
        {
            lock (_handleGate)
            {
                if (_bgmTempo != 0)
                {
                    BassMix.MixerRemoveChannel(_bgmTempo);
                    Bass.StreamFree(_bgmTempo);
                    _bgmTempo = 0;
                    _bgmSource = 0;
                }
                else if (_bgmSource != 0)
                {
                    Bass.StreamFree(_bgmSource);
                    _bgmSource = 0;
                }
            }
        }

        private void ClearTransientChannels()
        {
            lock (_handleGate)
            {
                ResetLoops();
                if (_mixer == 0)
                {
                    return;
                }

                var channels = BassMix.MixerGetChannels(_mixer);
                if (channels == null)
                {
                    return;
                }
                for (var i = 0; i < channels.Length; i++)
                {
                    if (channels[i] != _bgmTempo)
                    {
                        BassMix.MixerRemoveChannel(channels[i]);
                    }
                }
            }
        }

        private void StorePositions(
            double audibleMs,
            double latestMs,
            double leadMs)
        {
            Interlocked.Exchange(
                ref _audioPositionMicros,
                (long)Math.Round(audibleMs * PositionScale));
            Interlocked.Exchange(
                ref _latestPositionMicros,
                (long)Math.Round(latestMs * PositionScale));
            Interlocked.Exchange(
                ref _schedulingLeadMicros,
                (long)Math.Round(leadMs * PositionScale));
            Interlocked.Exchange(
                ref _audioPositionDspBits,
                BitConverter.DoubleToInt64Bits(
                    AudioSettings.dspTime));
        }

        private void EnsureMixBuffer(int samples)
        {
            if (_mixBuffer == null || _mixBuffer.Length < samples)
            {
                _mixBuffer = new float[samples];
            }
        }

        private byte[] GetOrCreateWav(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }
            var key = clip.GetInstanceID();
            byte[] wav;
            if (_clipWavCache.TryGetValue(key, out wav))
            {
                return wav;
            }

            try
            {
                if (clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }
                if (clip.loadState == AudioDataLoadState.Loading ||
                    clip.loadState == AudioDataLoadState.Unloaded)
                {
                    return null;
                }
                if (clip.loadState == AudioDataLoadState.Failed)
                {
                    Debug.LogWarning(
                        "[Falsum] AudioClip data failed to load: " +
                        clip.name);
                    return null;
                }
                var samples = clip.samples;
                var channels = Math.Max(1, clip.channels);
                var data = new float[samples * channels];
                if (!clip.GetData(data, 0))
                {
                    return null;
                }
                wav = BuildWaveFile(
                    data,
                    channels,
                    Math.Max(1, clip.frequency));
                _clipWavCache[key] = wav;
                return wav;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Could not prepare BASS clip " +
                    clip.name + ": " + exception.Message);
                return null;
            }
        }

        public IEnumerator PreloadClip(AudioClip clip)
        {
            if (clip == null)
            {
                yield break;
            }
            var key = clip.GetInstanceID();
            byte[] cached;
            if (_clipWavCache.TryGetValue(key, out cached))
            {
                yield break;
            }
            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }
            while (clip.loadState == AudioDataLoadState.Loading)
            {
                yield return null;
            }
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                Debug.LogWarning(
                    "[Falsum] BASS preload failed for clip " +
                    clip.name + ".");
                yield break;
            }
            GetOrCreateWav(clip);
        }

        private static byte[] BuildWaveFile(
            float[] samples,
            int channels,
            int sampleRate)
        {
            var dataLength = samples.Length * sizeof(short);
            var bytes = new byte[44 + dataLength];
            using (var stream = new MemoryStream(bytes, true))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataLength);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * sizeof(short));
                writer.Write((short)(channels * sizeof(short)));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataLength);
                for (var i = 0; i < samples.Length; i++)
                {
                    var value = Mathf.Clamp(samples[i], -1.0f, 1.0f);
                    writer.Write((short)Math.Round(value * short.MaxValue));
                }
            }
            return bytes;
        }

        private void OnDestroy()
        {
            ReleaseNativeResources();
        }

        private void ReleaseNativeResources()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _active = false;
            _initialized = false;

            try
            {
                // The audio thread may be inside a buffer right now. Taking the
                // gate here means it either finishes that buffer before the
                // handles are freed, or sees `_active = false` above and outputs
                // silence. Freeing without it is the intermittent native fault
                // that made Unity log off-thread from CrashReporter.
                lock (_handleGate)
                {
                    if (_mixer != 0)
                    {
                        ClearTransientChannels();
                        StopBgm();
                        Bass.StreamFree(_mixer);
                        _mixer = 0;
                    }
                }
            }
            catch
            {
                // Native shutdown must not mask scene teardown.
            }
            ReleaseBassInstance();
        }

        private static void ReleaseBassInstance()
        {
            lock (InitGate)
            {
                if (ActiveInstanceCount <= 0)
                {
                    return;
                }
                ActiveInstanceCount--;
                if (ActiveInstanceCount == 0)
                {
                    Bass.Free();
                }
            }
        }
    }
}
