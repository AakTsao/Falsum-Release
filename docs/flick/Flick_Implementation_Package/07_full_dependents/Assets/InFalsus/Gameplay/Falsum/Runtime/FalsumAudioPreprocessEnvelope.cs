using System;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// One min/max pair per horizontal pixel bucket of the waveform.
    /// </summary>
    public struct FalsumAudioPreprocessEnvelopeSample
    {
        public float Min;
        public float Max;
    }

    /// <summary>
    /// Peak envelope of the packaged audio, decoded in-process by BASS.
    ///
    /// Why not the existing <c>FalsumTrackSpectrumCache</c>: that one renders a
    /// 16 s / 960-columns-per-second track-floor strip and quantises to int16
    /// the way ffmpeg's filter did. This window needs one envelope over the
    /// whole song at the visible zoom, taken from the float samples straight
    /// out of BASS.
    ///
    /// Runs on a worker thread. It must therefore stay clear of every
    /// main-thread Unity API; BASS itself is thread safe for decode reads.
    /// </summary>
    public static class FalsumAudioPreprocessEnvelope
    {
        private const int ReadBufferFloats = 16384;

        /// <summary>
        /// Builds the envelope, or returns null and fills
        /// <paramref name="error"/>.
        /// </summary>
        public static FalsumAudioPreprocessEnvelopeSample[] Build(
            byte[] oggBytes,
            int bucketCount,
            out string error)
        {
            error = null;
            if (oggBytes == null || oggBytes.Length == 0)
            {
                error = "No audio bytes were supplied.";
                return null;
            }
            var buckets = Math.Max(1, bucketCount);
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
                    oggBytes,
                    0,
                    oggBytes.LongLength,
                    BassFlags.Decode | BassFlags.Float);
                if (channel == 0)
                {
                    error = "BASS could not decode the packaged audio: " +
                            Bass.LastError + ".";
                    return null;
                }
                var length = Bass.ChannelGetLength(
                    channel,
                    PositionFlags.Bytes);
                if (length <= 0)
                {
                    error = "BASS reported an empty audio stream.";
                    return null;
                }
                var info = Bass.ChannelGetInfo(channel);
                if (info.Channels <= 0)
                {
                    error = "BASS reported an invalid channel count.";
                    return null;
                }
                var totalFrames =
                    Bass.ChannelBytes2Seconds(channel, length) *
                    Math.Max(1, info.Frequency);
                return ReadEnvelope(
                    channel,
                    info.Channels,
                    (long)Math.Max(1.0, totalFrames),
                    buckets,
                    out error);
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
            }
        }

        private static FalsumAudioPreprocessEnvelopeSample[] ReadEnvelope(
            int channel,
            int channels,
            long totalFrames,
            int buckets,
            out string error)
        {
            error = null;
            var result = new FalsumAudioPreprocessEnvelopeSample[buckets];
            for (var i = 0; i < buckets; i++)
            {
                result[i].Min = 0.0f;
                result[i].Max = 0.0f;
            }
            // The bucket index comes from the byte offset, so the envelope does
            // not depend on how many frames each decode call happened to
            // return. That matters because chunk boundaries are not
            // deterministic across BASS versions.
            var buffer = new float[ReadBufferFloats];
            var consumed = 0L;
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
                    return null;
                }
                var frames = got / (sizeof(float) * channels);
                for (var frame = 0; frame < frames; frame++)
                {
                    var bucket = (int)((consumed + frame) * buckets /
                                       Math.Max(1, totalFrames));
                    if (bucket < 0)
                    {
                        bucket = 0;
                    }
                    if (bucket >= buckets)
                    {
                        bucket = buckets - 1;
                    }
                    for (var channelIndex = 0;
                         channelIndex < channels;
                         channelIndex++)
                    {
                        var value = buffer[frame * channels + channelIndex];
                        if (value < result[bucket].Min)
                        {
                            result[bucket].Min = value;
                        }
                        if (value > result[bucket].Max)
                        {
                            result[bucket].Max = value;
                        }
                    }
                }
                consumed += frames;
            }
            return result;
        }
    }
}
