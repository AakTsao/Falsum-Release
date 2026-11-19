using System;
using System.IO;
using InFalsus.Gameplay.Falsum.Core;
using ManagedBass;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Rewrites the packaged audio for the audio preprocess window: shift the
    /// read position by <c>silenceMs</c> (positive inserts silence at the head,
    /// negative trims the head), then re-encode to Ogg Vorbis.
    ///
    /// WHY BASS AND NOT FFMPEG: the package format needs Ogg Vorbis, and the
    /// old path shelled out to a 61 MB <c>ffmpeg.exe</c>. That can never work
    /// on Android (no child processes) and it was the dependency the BASS
    /// migration removed: the executable no longer ships. BASS already decodes
    /// the package audio on every platform here, and <c>bassenc_ogg.dll</c>
    /// encodes in-process.
    ///
    /// THE PUMP IS MANDATORY. A <c>BASS_STREAM_DECODE</c> channel produces
    /// nothing by itself: there is no output device pulling it. Starting an
    /// encoder on such a channel and stopping it immediately writes only the
    /// Ogg header (about 3.8 KB) with a reported duration of zero. The file
    /// exists and has the right magic, so that failure looks like success —
    /// which is exactly why this type owns the decode loop instead of leaving
    /// it to the caller (the same trap <c>FalsumBassEncoder</c> documents).
    ///
    /// The process-shifted PCM is written to a temporary WAV and then handed to
    /// <see cref="FalsumBassEncoder"/>. That is one extra disk round trip, but
    /// it keeps the transform independent of BASS stream topology (push
    /// streams, mixer offsets) and therefore of the BASS add-on versions this
    /// project ships. The temporary file lives in the conversion directory and
    /// is deleted in a finally block.
    /// </summary>
    public static class FalsumAudioPreprocessEncoder
    {
        private const int ReadBufferFloats = 16384;

        /// <summary>
        /// Rewrites <paramref name="sourceOggBytes"/> with
        /// <paramref name="silenceMs"/> applied to the head. Returns the new
        /// bytes, or null with <paramref name="error"/> filled.
        ///
        /// <paramref name="workDirectory"/> must be resolved by the CALLER on
        /// the main thread: this method runs on a worker (the editor hands it
        /// to <c>Task.Run</c>), and <c>Application.persistentDataPath</c> throws
        /// "can only be called from the main thread" off-thread. That throw
        /// used to surface only inside the worker, which made the apply path
        /// fail with no visible cause.
        /// </summary>
        public static byte[] RewriteToOgg(
            byte[] sourceOggBytes,
            double silenceMs,
            int quality,
            string workDirectory,
            out int newDurationMs,
            out string error)
        {
            newDurationMs = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(workDirectory))
            {
                error = "A temporary work directory is required.";
                return null;
            }
            if (sourceOggBytes == null || sourceOggBytes.Length == 0)
            {
                error = "No audio bytes were supplied.";
                return null;
            }
            if (double.IsNaN(silenceMs) || double.IsInfinity(silenceMs))
            {
                error = "The audio delay is not a finite number.";
                return null;
            }

            string wavPath = null;
            string oggPath = null;
            var channel = 0;
            var bassAcquired = false;
            try
            {
                bassAcquired = FalsumBassSession.Acquire(out string sessionError);
                if (!bassAcquired)
                {
                    error = sessionError;
                    return null;
                }
                channel = Bass.CreateStream(
                    sourceOggBytes,
                    0,
                    sourceOggBytes.LongLength,
                    BassFlags.Decode | BassFlags.Float);
                if (channel == 0)
                {
                    error = "BASS could not decode the packaged audio: " +
                            Bass.LastError + ".";
                    return null;
                }
                var info = Bass.ChannelGetInfo(channel);
                var rate = Math.Max(1, info.Frequency);
                var channels = Math.Max(1, info.Channels);

                var directory = workDirectory;
                Directory.CreateDirectory(directory);
                wavPath = Path.Combine(
                    directory,
                    "preprocess-" + Guid.NewGuid().ToString("N") + ".wav");
                oggPath = Path.Combine(
                    directory,
                    "preprocess-" + Guid.NewGuid().ToString("N") + ".ogg");

                var frames = ShiftAndCollect(
                    channel,
                    channels,
                    rate,
                    silenceMs,
                    wavPath,
                    out string collectError);
                if (collectError != null)
                {
                    error = collectError;
                    return null;
                }
                if (frames <= 0)
                {
                    error = "The rewritten audio would be empty.";
                    return null;
                }

                var wavChannel = Bass.CreateStream(
                    wavPath,
                    0,
                    0,
                    BassFlags.Decode | BassFlags.Float);
                if (wavChannel == 0)
                {
                    error = "BASS could not reopen the rewritten PCM: " +
                            Bass.LastError + ".";
                    return null;
                }
                try
                {
                    if (!FalsumBassEncoder.TryEncodeToOgg(
                            (uint)wavChannel,
                            oggPath,
                            quality,
                            out string encodeError))
                    {
                        error = string.IsNullOrEmpty(encodeError)
                            ? "Ogg encoding failed."
                            : encodeError;
                        return null;
                    }
                }
                finally
                {
                    Bass.StreamFree(wavChannel);
                }

                if (!File.Exists(oggPath) ||
                    new FileInfo(oggPath).Length == 0)
                {
                    error = "The encoder produced no output.";
                    return null;
                }
                var bytes = File.ReadAllBytes(oggPath);
                var durationMs = FalsumMediaConverter.ProbeOggDurationMs(
                    bytes,
                    rate);
                if (durationMs <= 0)
                {
                    // A header-only file also reports zero here, so this check
                    // is the second line of defence behind the pump guarantee.
                    error = "The encoded audio has no readable duration.";
                    return null;
                }
                newDurationMs = durationMs;
                return bytes;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return null;
            }
            finally
            {
                if (channel != 0)
                {
                    Bass.StreamFree(channel);
                }
                if (bassAcquired)
                {
                    FalsumBassSession.Release();
                }
                TryDelete(wavPath);
                TryDelete(oggPath);
            }
        }

        /// <summary>
        /// Number of frames the rewritten audio will contain, useful for the
        /// window's "processed length" readout before committing.
        /// </summary>
        public static double ExpectedDurationMs(
            double sourceDurationMs,
            double silenceMs)
        {
            return FalsumAudioPreprocessMath.NewAudioDurationMs(
                sourceDurationMs,
                silenceMs);
        }

        private static long ShiftAndCollect(
            int channel,
            int channels,
            int rate,
            double silenceMs,
            string wavPath,
            out string error)
        {
            error = null;
            var buffer = new float[ReadBufferFloats];
            var padFrames = silenceMs > 0.0
                ? (int)Math.Round(silenceMs / 1000.0 * rate)
                : 0;
            var skipFrames = silenceMs < 0.0
                ? (int)Math.Round(-silenceMs / 1000.0 * rate)
                : 0;
            var skipped = 0L;
            var written = 0L;

            using (var stream = new MemoryStream())
            {
                if (padFrames > 0)
                {
                    // Silence at the head of the new timeline: the rewritten
                    // audio needs padFrames of it so newTime 0 maps to the same
                    // music position the chart used to reach through its delay.
                    var padBytes = new byte[padFrames * channels *
                                            sizeof(float)];
                    stream.Write(padBytes, 0, padBytes.Length);
                    written += padFrames;
                }

                while (true)
                {
                    var got = Bass.ChannelGetData(
                        channel,
                        buffer,
                        buffer.Length * sizeof(float));
                    if (got <= 0)
                    {
                        var last = Bass.LastError;
                        if (last == Errors.OK || last == Errors.Ended)
                        {
                            break;
                        }
                        error = "Decoding the packaged audio failed: " +
                                last + ".";
                        return 0;
                    }
                    var frames = got / (sizeof(float) * channels);
                    var firstFrame = 0;
                    if (skipped < skipFrames)
                    {
                        var toSkip = (int)Math.Min(
                            skipFrames - skipped,
                            frames);
                        firstFrame = toSkip;
                        skipped += toSkip;
                    }
                    if (firstFrame >= frames)
                    {
                        continue;
                    }
                    var remaining = frames - firstFrame;
                    var bytes = new byte[remaining * channels * sizeof(float)];
                    Buffer.BlockCopy(
                        buffer,
                        firstFrame * channels * sizeof(float),
                        bytes,
                        0,
                        bytes.Length);
                    stream.Write(bytes, 0, bytes.Length);
                    written += remaining;
                }

                if (written <= 0)
                {
                    return 0;
                }
                WriteWaveFile(wavPath, stream.ToArray(), rate, channels);
            }
            return written;
        }

        private static void WriteWaveFile(
            string path,
            byte[] pcmFloatBytes,
            int rate,
            int channels)
        {
            // 16-bit PCM WAVE: half the temporary size of float, and the
            // encoder re-quantises anyway. The source was already decoded from
            // a lossy Vorbis stream, so 16 bits is not the limiting factor.
            var frameCount = pcmFloatBytes.Length /
                             (channels * sizeof(float));
            var dataBytes = new byte[frameCount * channels * sizeof(short)];
            for (var i = 0; i < frameCount * channels; i++)
            {
                var value = BitConverter.ToSingle(
                    pcmFloatBytes,
                    i * sizeof(float));
                var clamped = value < -1.0f
                    ? -1.0f
                    : (value > 1.0f ? 1.0f : value);
                var sample = (short)Math.Round(
                    clamped * short.MaxValue);
                dataBytes[i * sizeof(short)] = (byte)(sample & 0xFF);
                dataBytes[i * sizeof(short) + 1] =
                    (byte)((sample >> 8) & 0xFF);
            }
            using (var writer = new BinaryWriter(
                       File.Create(path)))
            {
                var blockAlign = channels * sizeof(short);
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + dataBytes.Length);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(rate);
                writer.Write(rate * blockAlign);
                writer.Write((short)blockAlign);
                writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(dataBytes.Length);
                writer.Write(dataBytes);
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
