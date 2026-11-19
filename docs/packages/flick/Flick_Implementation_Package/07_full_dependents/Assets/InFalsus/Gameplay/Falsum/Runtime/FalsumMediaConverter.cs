using System;
using System.IO;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumConvertedAudio
    {
        public byte[] Bytes;
        public int DurationMs;
        public int SampleRate = 48000;
        public int Channels = 2;
    }

    public sealed class FalsumConvertedCover
    {
        public byte[] Bytes;
        public int Width = 1024;
        public int Height = 1024;
    }

    public sealed class FalsumAudioConversionJob
    {
        public string SourcePath;
        public string OutputPath;

        /// <summary>
        /// Absolute path of the FLAC add-on, resolved on the main thread by
        /// <see cref="FalsumMediaConverter.PrepareAudioConversion"/> because
        /// <c>Application.dataPath</c> is not usable on a worker. Null means
        /// "no native FLAC decoder"; WAV/MP3/OGG are unaffected.
        /// </summary>
        public string FlacPluginPath;
    }

    /// <summary>
    /// Converts the media a FALSUM package needs: an Ogg Vorbis soundtrack at
    /// 48 kHz stereo, and a square PNG cover.
    ///
    /// AUDIO IS CONVERTED IN-PROCESS. The audio half used to shell out to a
    /// 61 MB <c>ffmpeg.exe</c>; it now decodes and re-encodes through BASS
    /// (<see cref="FalsumBassMediaImporter"/> plus
    /// <see cref="FalsumBassEncoder"/>), so nothing spawns a child process, no
    /// executable ships with the build, and the same route can run on platforms
    /// that have no child processes at all. The cover half never needed an
    /// external tool: it decodes, centre-crops and rescales with Unity's own
    /// image conversion.
    ///
    /// THREADING CONTRACT. <see cref="PrepareAudioConversion"/> touches Unity
    /// APIs (persistent data path, plugin path) and must run on the main
    /// thread; it only validates the input and builds absolute paths.
    /// <see cref="ConvertAudio(FalsumAudioConversionJob)"/> touches no Unity API
    /// and is meant to run on a worker (the editor hands it to <c>Task.Run</c>).
    /// <see cref="ConvertCover"/> and <see cref="LoadPreview"/> are main-thread
    /// because they create textures.
    ///
    /// FORMAT COVERAGE, measured against BASS 2.4.17: WAV/MP3/OGG decode
    /// natively; FLAC uses the bundled <c>bassflac</c> add-on (without it the
    /// Windows OS codec decodes the audio but reports a wrong stream length);
    /// M4A/AAC rely on the Windows OS codecs (Media Foundation / ACM). A machine
    /// without those codecs cannot import M4A/AAC: the attempt fails with an
    /// "unsupported format" error instead of writing a broken package.
    /// </summary>
    public static class FalsumMediaConverter
    {
        public const int AudioSampleRate = 48000;
        public const int AudioChannels = 2;
        public const int CoverSize = 1024;

        private const int AudioQuality = FalsumBassMediaImporter.DefaultQuality;
        private const string FlacPluginFileName = "bassflac.dll";

        public static FalsumConvertedAudio ConvertAudio(string sourcePath)
        {
            return ConvertAudio(
                PrepareAudioConversion(sourcePath));
        }

        public static FalsumAudioConversionJob PrepareAudioConversion(
            string sourcePath)
        {
            // Unity path APIs are main-thread only. Resolve everything here
            // before the caller hands the job to a worker thread.
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.media.audioSourceMissing"),
                    sourcePath);
            }
            var directory = Path.Combine(
                Application.persistentDataPath,
                "FalsumConversion");
            Directory.CreateDirectory(directory);
            return new FalsumAudioConversionJob
            {
                SourcePath = sourcePath,
                OutputPath = Path.Combine(
                    directory,
                    Guid.NewGuid().ToString("N") + ".ogg"),
                FlacPluginPath = ResolveFlacPluginPath(),
            };
        }

        public static FalsumConvertedAudio ConvertAudio(
            FalsumAudioConversionJob job)
        {
            if (job == null)
            {
                throw new ArgumentNullException("job");
            }
            if (string.IsNullOrWhiteSpace(job.SourcePath) ||
                !File.Exists(job.SourcePath))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.media.audioSourceMissing"),
                    job.SourcePath);
            }
            if (string.IsNullOrWhiteSpace(job.OutputPath))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.media.outputPathEmpty"),
                    "job");
            }
            try
            {
                var failure = FalsumBassMediaImporter.ConvertToOgg(
                    job.SourcePath,
                    job.OutputPath,
                    job.FlacPluginPath,
                    AudioSampleRate,
                    AudioChannels,
                    AudioQuality,
                    out var error);
                if (failure != FalsumBassImportFailure.None)
                {
                    throw new InvalidOperationException(
                        DescribeFailure(failure, error));
                }
                var bytes = File.ReadAllBytes(job.OutputPath);
                var durationMs = ProbeOggDurationMs(
                    bytes,
                    AudioSampleRate);
                if (durationMs <= 0)
                {
                    // A header-only stream reports zero here. The encoder
                    // already rejects a file it failed to write, so this is the
                    // second line of defence against a silent truncation.
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.media.durationUnreadable"));
                }
                return new FalsumConvertedAudio
                {
                    Bytes = bytes,
                    DurationMs = durationMs,
                };
            }
            finally
            {
                if (File.Exists(job.OutputPath))
                {
                    File.Delete(job.OutputPath);
                }
            }
        }

        /// <summary>
        /// Turns an importer failure into user-facing text. Every branch is a
        /// localization key; the importer itself never produces visible copy.
        /// </summary>
        private static string DescribeFailure(
            FalsumBassImportFailure failure,
            string error)
        {
            var detail = string.IsNullOrEmpty(error)
                ? L.Get("falsum.error.media.conversionFailed")
                : error;
            switch (failure)
            {
                case FalsumBassImportFailure.InitFailed:
                    return L.Get(
                        "falsum.error.media.bassInitFailed",
                        ("error", detail));
                case FalsumBassImportFailure.UnsupportedFormat:
                    return L.Get(
                        "falsum.error.media.unsupportedFormat",
                        ("error", detail));
                case FalsumBassImportFailure.SourceOpenFailed:
                    return L.Get(
                        "falsum.error.media.sourceOpenFailed",
                        ("error", detail));
                case FalsumBassImportFailure.EncodeFailed:
                    return L.Get(
                        "falsum.error.media.encodeFailed",
                        ("error", detail));
                case FalsumBassImportFailure.MixerFailed:
                    return L.Get(
                        "falsum.error.media.conversionFailedWithDetails",
                        ("error", detail));
                default:
                    return L.Get(
                        "falsum.error.media.conversionFailed");
            }
        }

        /// <summary>
        /// Finds <c>bassflac.dll</c> next to the native BASS libraries. Both the
        /// editor (<c>&lt;project&gt;/Assets/Plugins/x86_64</c>) and a player
        /// build (<c>&lt;exe&gt;/&lt;name&gt;_Data/Plugins/x86_64</c>) resolve to
        /// the same path relative to <c>Application.dataPath</c>. Returns null
        /// when the add-on is not shipped for this platform, which is not fatal:
        /// only FLAC loses its native decoder.
        /// </summary>
        private static string ResolveFlacPluginPath()
        {
            try
            {
                var dataPath = Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath))
                {
                    var candidate = Path.Combine(
                        dataPath,
                        "Plugins",
                        "x86_64",
                        FlacPluginFileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                // Application.dataPath is main-thread only; if this ever runs
                // off-thread the add-on simply stays unloaded.
            }
            return null;
        }

        public static FalsumConvertedCover ConvertCover(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.media.coverSourceMissing"),
                    sourcePath);
            }

            Texture2D source = null;
            Texture2D output = null;
            try
            {
                source = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false);
                if (!ImageConversion.LoadImage(
                        source,
                        File.ReadAllBytes(sourcePath),
                        false))
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.media.coverDecodeFailed"));
                }
                if (source.width <= 0 || source.height <= 0 ||
                    (long)source.width * source.height >
                    100L * 1024L * 1024L)
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.media.coverTooLarge"));
                }

                var cropSize = Mathf.Min(source.width, source.height);
                var cropX = (source.width - cropSize) * 0.5f;
                var cropY = (source.height - cropSize) * 0.5f;
                var pixels = source.GetPixels32();
                var scaled = new Color32[CoverSize * CoverSize];
                var scale = cropSize / (float)CoverSize;
                for (var y = 0; y < CoverSize; y++)
                {
                    var sourceY = cropY +
                                  (y + 0.5f) * scale -
                                  0.5f;
                    for (var x = 0; x < CoverSize; x++)
                    {
                        var sourceX = cropX +
                                      (x + 0.5f) * scale -
                                      0.5f;
                        scaled[y * CoverSize + x] =
                            SampleBilinear(
                                pixels,
                                source.width,
                                source.height,
                                sourceX,
                                sourceY);
                    }
                }

                output = new Texture2D(
                    CoverSize,
                    CoverSize,
                    TextureFormat.RGBA32,
                    false);
                output.SetPixels32(scaled);
                output.Apply(false, false);
                return new FalsumConvertedCover
                {
                    Bytes = ImageConversion.EncodeToPNG(output),
                    Width = CoverSize,
                    Height = CoverSize,
                };
            }
            finally
            {
                DestroyTexture(output);
                DestroyTexture(source);
            }
        }

        public static Texture2D LoadPreview(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                return null;
            }
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            if (ImageConversion.LoadImage(
                    texture,
                    File.ReadAllBytes(sourcePath),
                    false))
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            DestroyTexture(texture);
            return null;
        }

        public static int ProbeOggDurationMs(
            byte[] bytes,
            int sampleRate)
        {
            if (bytes == null || bytes.Length < 28 ||
                sampleRate <= 0)
            {
                return 0;
            }
            var offset = 0;
            ulong lastGranule = 0;
            var foundPage = false;
            while (offset + 27 <= bytes.Length)
            {
                if (bytes[offset] != (byte)'O' ||
                    bytes[offset + 1] != (byte)'g' ||
                    bytes[offset + 2] != (byte)'g' ||
                    bytes[offset + 3] != (byte)'S')
                {
                    offset++;
                    continue;
                }

                var granule = ReadUInt64LittleEndian(
                    bytes,
                    offset + 6);
                if (granule != ulong.MaxValue)
                {
                    lastGranule = granule;
                    foundPage = true;
                }
                var segmentCount = bytes[offset + 26];
                var headerLength = 27 + segmentCount;
                if (offset + headerLength > bytes.Length)
                {
                    break;
                }
                var bodyLength = 0;
                for (var i = 0; i < segmentCount; i++)
                {
                    bodyLength += bytes[offset + 27 + i];
                }
                offset += headerLength + bodyLength;
            }
            if (!foundPage || lastGranule == 0)
            {
                return 0;
            }
            return Math.Max(
                1,
                (int)Math.Round(
                    lastGranule * 1000.0 / sampleRate,
                    MidpointRounding.AwayFromZero));
        }

        private static Color32 SampleBilinear(
            Color32[] pixels,
            int width,
            int height,
            float x,
            float y)
        {
            x = Mathf.Clamp(x, 0.0f, width - 1.0f);
            y = Mathf.Clamp(y, 0.0f, height - 1.0f);
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var y1 = Mathf.Min(y0 + 1, height - 1);
            var tx = x - x0;
            var ty = y - y0;
            var c00 = pixels[y0 * width + x0];
            var c10 = pixels[y0 * width + x1];
            var c01 = pixels[y1 * width + x0];
            var c11 = pixels[y1 * width + x1];
            return new Color32(
                Blend(c00.r, c10.r, c01.r, c11.r, tx, ty),
                Blend(c00.g, c10.g, c01.g, c11.g, tx, ty),
                Blend(c00.b, c10.b, c01.b, c11.b, tx, ty),
                Blend(c00.a, c10.a, c01.a, c11.a, tx, ty));
        }

        private static byte Blend(
            byte c00,
            byte c10,
            byte c01,
            byte c11,
            float tx,
            float ty)
        {
            var top = c00 + (c10 - c00) * tx;
            var bottom = c01 + (c11 - c01) * tx;
            return (byte)Mathf.Clamp(
                Mathf.RoundToInt(top + (bottom - top) * ty),
                0,
                255);
        }

        private static ulong ReadUInt64LittleEndian(
            byte[] bytes,
            int offset)
        {
            ulong value = 0;
            for (var i = 7; i >= 0; i--)
            {
                value = (value << 8) | bytes[offset + i];
            }
            return value;
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
