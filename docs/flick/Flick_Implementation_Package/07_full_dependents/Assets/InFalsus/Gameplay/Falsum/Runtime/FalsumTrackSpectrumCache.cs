using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ManagedBass;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumTrackSpectrumJob
    {
        public string AudioPath;
        public string AudioHash;
        public int TextureWidth;
        public int TextureHeight;
        public int SegmentIndex;
        public double SegmentStartMs;
        public double SegmentDurationMs;
    }

    public sealed class FalsumTrackSpectrumFrame
    {
        public int Width;
        public int Height;
        public byte[] Bytes;
    }

    /// <summary>
    /// Track waveform segment generation, decoded in-process by BASS.
    ///
    /// This used to shell out to a 61 MB <c>ffmpeg.exe</c> running
    /// <c>showwavespic=...:scale=sqrt,format=gray</c> and read W*H gray bytes
    /// from stdout. The filter's output is reproduced here byte-for-byte so the
    /// existing textures, shader and caching stay untouched.
    ///
    /// WHICH FFMPEG IS BEING REPRODUCED. The bundled binary is
    /// <c>N-92722-gf22fcd4483</c>, built from the <c>n4.0.2</c> branch — NOT
    /// from master. Master's <c>avf_showwaves.c</c> has a different column
    /// grouping and no magnitude doubling, so porting master's maths gives a
    /// waveform that does not match anything this project has ever shipped.
    /// The n4.0.2 behaviour below was verified pixel-exact (0 differing bytes of
    /// 983040) against the real binary on three independent windows.
    ///
    /// THE THREE COUNTER-INTUITIVE DETAILS ARE DELIBERATE:
    ///   1. <c>sum[ch] += abs(s16) &lt;&lt; 1</c>: n4.0.2 doubles the magnitude
    ///      but still divides by <c>max_samples</c>, so quiet passages are
    ///      rendered louder than a "correct" mean would suggest.
    ///   2. <c>if (n++ == max_samples)</c>: a column is closed by the
    ///      <c>(max_samples+1)</c>-th frame but divided by <c>max_samples</c>.
    ///      Because <c>max_samples = frames / width</c> truncates, the number of
    ///      filled columns is <c>frames / (max_samples + 1)</c>, i.e. usually
    ///      <c>width - 1</c> or <c>width - 2</c>. THE TRAILING COLUMNS STAY
    ///      BLACK; that is exactly what the shipped waveform looks like.
    ///   3. <c>(short)(sum / max_samples)</c>: the quotient is narrowed to
    ///      int16 and therefore wraps modulo 65536, so loud material yields
    ///      SHORTER spans. The gray path also adds <c>255/channels</c> per
    ///      channel into the same pixel, so a stereo overlap wraps to 254 and a
    ///      single channel contributes 127 — never 255.
    ///
    /// Do not "simplify" any of these: each one changes visible pixels.
    /// </summary>
    public static class FalsumTrackSpectrumCache
    {
        public const int TextureHeight = 1024;
        public const float ColumnsPerSecond = 960.0f;
        public const double SegmentDurationMs = 16000.0;
        public const double SegmentStrideMs = 10000.0;

        private const int MinimumTextureWidth = 256;
        private const int MaximumTextureWidth = 16384;
        private const double FallbackDurationSeconds = 180.0;

        /// <summary>
        /// Decode pump block size in float samples. The BASS decode channel
        /// produces nothing on its own (<c>BASS_STREAM_DECODE</c> has no output
        /// device pulling it), so the caller must read it to the end; this is
        /// the same trap <c>FalsumBassEncoder</c> documents.
        /// </summary>
        private const int DecodeBufferFloats = 16384;

        /// <summary>
        /// Upper bound for one segment's decode+raster pass. Kept from the old
        /// process path so a pathological input still fails loudly instead of
        /// stalling a worker thread forever.
        /// </summary>
        private const int DecodeTimeoutMs = 300000;

        /// <summary>
        /// <c>sqrt(INT16_MAX)</c> (INT16_MAX, not 32768). Held as a double so the
        /// span arithmetic stays in the same shape as <c>get_sqrt_h2</c>.
        /// </summary>
        private static readonly double SqrtInt16Max = Math.Sqrt(32767.0);

        private static bool _optionalPluginsAttempted;

        public static FalsumTrackSpectrumJob PrepareSegment(
            string audioPath,
            string audioHash,
            double audioDurationMs,
            int segmentIndex)
        {
            if (string.IsNullOrWhiteSpace(audioPath) ||
                !File.Exists(audioPath))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.spectrum.audioSourceMissing"),
                    audioPath);
            }
            if (string.IsNullOrWhiteSpace(audioHash) ||
                audioHash.Length < 32)
            {
                throw new ArgumentException(
                    L.Get("falsum.error.spectrum.audioHashRequired"),
                    "audioHash");
            }
            if (audioDurationMs <= 0.001 ||
                segmentIndex < 0)
            {
                return null;
            }

            var startMs = segmentIndex * SegmentStrideMs;
            if (startMs >= audioDurationMs)
            {
                return null;
            }
            var durationMs = Math.Min(
                SegmentDurationMs,
                audioDurationMs - startMs);
            var textureWidth =
                GetSegmentTextureWidth(durationMs);

            return new FalsumTrackSpectrumJob
            {
                AudioPath = Path.GetFullPath(audioPath),
                AudioHash = audioHash,
                TextureWidth = textureWidth,
                TextureHeight = TextureHeight,
                SegmentIndex = segmentIndex,
                SegmentStartMs = startMs,
                SegmentDurationMs = durationMs,
            };
        }

        public static int CacheKey(
            string audioHash,
            int segmentIndex,
            int textureWidth,
            int textureHeight)
        {
            if (string.IsNullOrWhiteSpace(audioHash))
            {
                return 0;
            }
            var safeHash = audioHash.Length > 64
                ? audioHash.Substring(0, 64)
                : audioHash;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + safeHash.GetHashCode();
                hash = hash * 31 + segmentIndex;
                hash = hash * 31 + textureWidth;
                hash = hash * 31 + textureHeight;
                return hash;
            }
        }

        public static int GetSegmentTextureWidth(
            double segmentDurationMs)
        {
            return GetTextureWidth(
                segmentDurationMs,
                ColumnsPerSecond,
                MaximumTextureWidth);
        }

        public static int GetSegmentCount(double audioDurationMs)
        {
            if (audioDurationMs <= 0.001)
            {
                return 0;
            }
            return (int)Math.Ceiling(
                audioDurationMs / SegmentStrideMs);
        }

        /// <summary>
        /// How many of the <paramref name="textureWidth"/> columns this window
        /// will actually fill. Pure arithmetic mirror of the n4.0.2 grouping
        /// (see the type comment): <c>max_samples = frames / width</c> and a
        /// column costs <c>max_samples + 1</c> frames, so the result is
        /// <c>frames / (max_samples + 1)</c> and the remainder of the width
        /// stays black. Exposed so the self-check can pin the counter-intuitive
        /// behaviour without touching real audio.
        /// </summary>
        public static int GetFilledColumnCount(
            double segmentDurationMs,
            int textureWidth,
            int sampleRate)
        {
            if (segmentDurationMs <= 0.0 ||
                textureWidth <= 0 ||
                sampleRate <= 0)
            {
                return 0;
            }
            var frames = (long)Math.Round(
                segmentDurationMs / 1000.0 * sampleRate,
                MidpointRounding.AwayFromZero);
            var maxSamples = frames / textureWidth;
            if (maxSamples <= 0)
            {
                return 0;
            }
            var filled = frames / (maxSamples + 1);
            return filled > textureWidth
                ? textureWidth
                : (int)filled;
        }

        private static int GetTextureWidth(
            double audioDurationMs,
            float columnsPerSecond,
            int maximumTextureWidth)
        {
            var durationSeconds =
                audioDurationMs > 0.001
                    ? audioDurationMs / 1000.0
                    : FallbackDurationSeconds;
            var requested = (int)Math.Ceiling(
                durationSeconds * columnsPerSecond);
            requested = Math.Max(
                MinimumTextureWidth,
                requested);
            if ((requested & 3) != 0)
            {
                requested += 4 - (requested & 3);
            }
            var hardwareLimit = SystemInfo.maxTextureSize > 0
                ? SystemInfo.maxTextureSize
                : MaximumTextureWidth;
            return Math.Min(
                requested,
                Math.Min(
                    Math.Min(maximumTextureWidth, hardwareLimit),
                    MaximumTextureWidth));
        }

        public static void Generate(
            FalsumTrackSpectrumJob job,
            FalsumTrackSpectrumFrame destination,
            CancellationToken cancellationToken)
        {
            if (job == null)
            {
                throw new ArgumentNullException("job");
            }
            if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }
            if (string.IsNullOrWhiteSpace(job.AudioPath) ||
                !File.Exists(job.AudioPath))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.spectrum.audioSourceMissing"),
                    job.AudioPath);
            }
            if (job.TextureWidth <= 0 ||
                job.TextureHeight <= 0)
            {
                throw new ArgumentException(
                    L.Get("falsum.error.spectrum.segmentDimensions"),
                    "job");
            }

            var bytes = Rasterise(job, cancellationToken);
            destination.Width = job.TextureWidth;
            destination.Height = job.TextureHeight;
            destination.Bytes = bytes;
        }

        public static Texture2D CreateTexture(
            FalsumTrackSpectrumFrame frame)
        {
            if (frame == null ||
                frame.Bytes == null ||
                frame.Width <= 0 ||
                frame.Height <= 0)
            {
                return null;
            }
            if (frame.Bytes.Length != frame.Width * frame.Height)
            {
                throw new InvalidDataException(
                    L.Get("falsum.error.spectrum.invalidBuffer"));
            }

            var texture = new Texture2D(
                frame.Width,
                frame.Height,
                TextureFormat.R8,
                false,
                true);
            try
            {
                texture.LoadRawTextureData(frame.Bytes);
                texture.Apply(false, true);
                texture.name = "FalsumTrackWaveform";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.anisoLevel = 2;
                texture.hideFlags = HideFlags.HideAndDontSave;
                return texture;
            }
            catch
            {
                DestroyTexture(texture);
                throw;
            }
        }

        /// <summary>
        /// Decodes exactly <c>SegmentDurationMs</c> of audio starting at
        /// <c>SegmentStartMs</c> and rasterises it into W*H gray bytes.
        /// Runs on a worker thread; BASS decode reads are thread safe and no
        /// Unity API beyond pure maths is touched here.
        /// </summary>
        private static byte[] Rasterise(
            FalsumTrackSpectrumJob job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!FalsumBassSession.Acquire(out string sessionError))
            {
                throw new InvalidOperationException(
                    DescribeFailure(sessionError));
            }
            TryLoadOptionalPlugins();

            var channel = 0;
            try
            {
                channel = Bass.CreateStream(
                    job.AudioPath,
                    0,
                    0,
                    BassFlags.Decode |
                    BassFlags.Float |
                    PlatformPathFlags());
                if (channel == 0)
                {
                    throw new InvalidOperationException(
                        DescribeFailure(
                            "BASS could not open the waveform audio: " +
                            Bass.LastError + "."));
                }

                var info = Bass.ChannelGetInfo(channel);
                var sampleRate = Math.Max(1, info.Frequency);
                var channels = Math.Max(1, info.Channels);

                if (job.SegmentStartMs > 0.0)
                {
                    var position = Bass.ChannelSeconds2Bytes(
                        channel,
                        job.SegmentStartMs / 1000.0);
                    if (!Bass.ChannelSetPosition(
                            channel,
                            position,
                            PositionFlags.Bytes))
                    {
                        throw new InvalidOperationException(
                            DescribeFailure(
                                "BASS could not seek into the waveform " +
                                "audio: " + Bass.LastError + "."));
                    }
                }

                // The old process path decoded exactly this window with
                // `-ss start -t duration`, and max_samples is derived from the
                // frame count, so the window itself must not drift.
                var wantFrames = (int)Math.Round(
                    Math.Max(0.0, job.SegmentDurationMs) / 1000.0 *
                    sampleRate,
                    MidpointRounding.AwayFromZero);
                if (wantFrames <= 0)
                {
                    throw new InvalidDataException(
                        DescribeFailure(
                            "The waveform window is empty."));
                }
                var sampleCount = (long)wantFrames * channels;
                if (sampleCount > int.MaxValue - 16)
                {
                    throw new InvalidDataException(
                        DescribeFailure(
                            "The waveform window is too large to buffer."));
                }

                var samples = new short[sampleCount];
                var frames = DecodeFrames(
                    channel,
                    channels,
                    wantFrames,
                    samples,
                    cancellationToken);
                if (frames <= 0)
                {
                    throw new InvalidDataException(
                        DescribeFailure(
                            "No audio was decoded for the waveform window."));
                }
                return DrawWaveform(
                    samples,
                    frames,
                    channels,
                    job.TextureWidth,
                    job.TextureHeight);
            }
            finally
            {
                if (channel != 0)
                {
                    Bass.StreamFree(channel);
                }
            }
        }

        /// <summary>
        /// Pumps the decode channel into <paramref name="destination"/> until
        /// <paramref name="wantFrames"/> frames are collected or the stream
        /// ends (the last segment is clamped to the audio end, so a short
        /// result is normal there). Returns the frames actually decoded.
        /// </summary>
        private static int DecodeFrames(
            int channel,
            int channels,
            int wantFrames,
            short[] destination,
            CancellationToken cancellationToken)
        {
            var buffer = new float[DecodeBufferFloats];
            var stride = sizeof(float) * channels;
            var watch = Stopwatch.StartNew();
            var frames = 0;
            while (frames < wantFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (watch.ElapsedMilliseconds > DecodeTimeoutMs)
                {
                    throw new TimeoutException(
                        L.Get("falsum.error.spectrum.timeout"));
                }

                var got = Bass.ChannelGetData(
                    channel,
                    buffer,
                    buffer.Length * sizeof(float));
                if (got <= 0)
                {
                    var last = Bass.LastError;
                    // ChannelGetData returns <= 0 at end-of-stream AND on
                    // failure; BASS distinguishes them with Errors.Ended.
                    if (last == Errors.OK || last == Errors.Ended)
                    {
                        break;
                    }
                    throw new InvalidOperationException(
                        DescribeFailure(
                            "Decoding the waveform audio failed: " +
                            last + "."));
                }

                var gotFrames = got / stride;
                if (gotFrames <= 0)
                {
                    break;
                }
                var take = Math.Min(
                    gotFrames,
                    wantFrames - frames);
                var count = take * channels;
                var offset = frames * channels;
                for (var i = 0; i < count; i++)
                {
                    destination[offset + i] = ToInt16(buffer[i]);
                }
                frames += take;
                if (take < gotFrames)
                {
                    break;
                }
            }
            return frames;
        }

        /// <summary>
        /// The rasteriser, transcribed from n4.0.2 <c>push_single_pic</c> +
        /// <c>draw_sample_cline_gray</c> + <c>get_sqrt_h2</c>. See the type
        /// comment for why every oddity below is intentional.
        /// </summary>
        private static byte[] DrawWaveform(
            short[] samples,
            int frames,
            int channels,
            int width,
            int height)
        {
            var pixels = new byte[width * height];
            var maxSamples = frames / width;
            if (maxSamples <= 0)
            {
                throw new InvalidDataException(
                    DescribeFailure(
                        "Too few samples for the waveform width (" +
                        frames + " frames for " + width + " columns)."));
            }

            // DRAW_SCALE on the gray path: x = 255 / (nb_channels * n) with
            // n = 1 for showwavespic. Stereo therefore contributes 127 per
            // channel, and two overlapping channels wrap to 254.
            var step = (byte)(255 / channels);
            var sums = new long[channels];
            var n = 0;
            var column = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                var baseIndex = frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    sums[ch] += Math.Abs(
                        (int)samples[baseIndex + ch]) << 1;
                }
                // n4.0.2: `if (n++ == max_samples)` — the comparison uses the
                // pre-increment value, so the column closes on frame
                // max_samples+1 while dividing by max_samples.
                var closesColumn = n == maxSamples;
                n++;
                if (!closesColumn)
                {
                    continue;
                }

                for (var ch = 0; ch < channels; ch++)
                {
                    // `int16_t sample = sum[ch] / max_samples;` — integer
                    // division then narrowing to int16 (wraps mod 65536).
                    var sample = unchecked(
                        (short)(sums[ch] / maxSamples));
                    var span = (int)(
                        Math.Sqrt(Math.Abs((double)sample)) *
                        height /
                        SqrtInt16Max);
                    if (span > height)
                    {
                        span = height;
                    }
                    var start = (height - span) / 2;
                    var pixel = start * width + column;
                    for (var row = 0;
                         row < span && start + row < height;
                         row++)
                    {
                        pixels[pixel] =
                            (byte)((pixels[pixel] + step) & 0xFF);
                        pixel += width;
                    }
                    sums[ch] = 0;
                }

                column++;
                n = 0;
                if (column >= width)
                {
                    break;
                }
            }
            return pixels;
        }

        /// <summary>
        /// swresample's float→s16 rule: <c>av_clip_int16(lrintf(f * 32768))</c>.
        /// <c>lrintf</c> honours the default FE_TONEAREST mode, i.e. rounds
        /// half to even — the same rounding <see cref="Math.Round(double)"/>
        /// uses — and the result is then clamped, not wrapped.
        /// </summary>
        private static short ToInt16(float value)
        {
            var scaled = Math.Round(value * 32768.0);
            if (scaled > 32767.0)
            {
                return 32767;
            }
            if (scaled < -32768.0)
            {
                return -32768;
            }
            return (short)scaled;
        }

        /// <summary>Self-check hook: rasterise raw s16 samples exactly as Generate does.</summary>
        public static byte[] DebugDrawWaveform(
            short[] samples,
            int frameCount,
            int channels,
            int width,
            int height)
        {
            return DrawWaveform(
                samples,
                frameCount,
                channels,
                width,
                height);
        }

        /// <summary>Self-check hook: swresample float -> s16 conversion rule.</summary>
        public static short DebugToInt16(float value)
        {
            return ToInt16(value);
        }

        /// <summary>
        /// ManagedBass marshals a string file name as ANSI unless
        /// <see cref="BassFlags.Unicode"/> is set; the editor's paths contain
        /// non-ASCII characters, so Windows needs the UTF-16 path. Mirrors
        /// <c>FalsumBassAudioEngine</c>.
        /// </summary>
        private static BassFlags PlatformPathFlags()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return BassFlags.Unicode;
#else
            return BassFlags.Default;
#endif
        }

        /// <summary>
        /// Best effort only: the segment audio is always the editor's cached
        /// <c>.ogg</c>, which stock BASS decodes without add-ons. Loading
        /// <c>bassflac</c> keeps a FLAC source working if one ever reaches this
        /// path; a missing add-on must never fail the waveform.
        /// </summary>
        private static void TryLoadOptionalPlugins()
        {
            if (_optionalPluginsAttempted)
            {
                return;
            }
            _optionalPluginsAttempted = true;
            try
            {
                Bass.PluginLoad("bassflac.dll");
            }
            catch (Exception)
            {
                // No add-on on this platform; stock BASS still covers the
                // formats this path actually sees.
            }
        }

        private static string DescribeFailure(string detail)
        {
            return L.Get(
                "falsum.error.spectrum.generationFailedWithDetails",
                ("error", detail ?? string.Empty));
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
