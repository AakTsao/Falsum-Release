using System;
using System.IO;
using ManagedBass;
using ManagedBass.Mix;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Why a conversion did not produce audio. The converter maps each value to
    /// a localized message; keeping the mapping out of this file is what lets
    /// this type stay free of Unity localization and run on a worker thread.
    /// </summary>
    public enum FalsumBassImportFailure
    {
        None = 0,
        InitFailed,
        SourceOpenFailed,
        UnsupportedFormat,
        MixerFailed,
        EncodeFailed,
        EmptyAudio,
    }

    /// <summary>
    /// Decodes an arbitrary audio file and re-encodes it as the package format
    /// (Ogg Vorbis, 48 kHz stereo) entirely in-process through BASS.
    ///
    /// WHY THIS EXISTS: the previous route shelled out to a 61 MB
    /// <c>ffmpeg.exe</c> through <c>Process.Start</c>. That cannot work on
    /// Android (no child processes, no <c>.exe</c>, and StreamingAssets is not
    /// a filesystem), and it forced a 61 MB binary into every build. BASS is
    /// already the audio backend here on every platform, and
    /// <c>bassenc_ogg</c> does the encode step, so no external process is
    /// involved at any point.
    ///
    /// THE ROUTE IS A MIXER, NOT A NON-STOP MIXER. The source keeps its own
    /// sample rate and channel count, and the package needs exactly 48 kHz
    /// stereo (<c>libvorbis -q:a 6 -ac 2 -ar 48000</c> semantics). A BASSMIX
    /// mixer stream created at 48 kHz/2ch with
    /// <c>BassFlags.MixerChanDownMix</c> does the resample and the fold-down in
    /// one step. <c>BassFlags.MixerNonStop</c> MUST NOT be set: it declares the
    /// mixer endless, so the decode pump never reaches end-of-stream and the
    /// conversion hangs forever instead of finishing.
    ///
    /// THE PUMP BELONGS TO THE ENCODER. A decode channel produces nothing on
    /// its own (no output device pulls it), so an encoder started and stopped
    /// without pumping writes only the ~3.8 KB Ogg header, with a duration of
    /// zero. That failure looks like success: the file exists and has the right
    /// magic. <see cref="FalsumBassEncoder.TryEncodeToOgg"/> owns that loop.
    ///
    /// FORMAT COVERAGE, measured against BASS 2.4.17 on this machine:
    /// WAV, MP3 and OGG decode natively. FLAC decodes through the Windows OS
    /// codec too, but its reported stream length is wrong that way (a 2.994 s
    /// file reports 2.000 s), so <c>bassflac.dll</c> is loaded when present to
    /// get correct lengths and to avoid depending on the OS codec at all.
    /// M4A/AAC have no native decoder here: they rely on the Windows OS codecs
    /// (Media Foundation / ACM). On a machine without them, opening the file
    /// fails and this type reports <see cref="FalsumBassImportFailure.UnsupportedFormat"/>
    /// rather than producing a broken package.
    ///
    /// Everything here is worker-thread safe: no Unity API is touched, and the
    /// plugin path is resolved by the caller on the main thread because
    /// <c>Application.dataPath</c> is not guaranteed off-thread.
    /// </summary>
    public static class FalsumBassMediaImporter
    {
        public const int DefaultQuality = 6;

        private static readonly float[] PumpBuffer = new float[16384];

        private static readonly object FlacGate = new object();
        private static bool _flacAttempted;
        private static bool _flacLoaded;

        /// <summary>
        /// True once <c>bassflac.dll</c> has been loaded into this process.
        /// A false value only means FLAC falls back to the OS codec (if any);
        /// it is not an error for WAV/MP3/OGG/M4A.
        /// </summary>
        public static bool IsFlacPluginLoaded
        {
            get
            {
                lock (FlacGate)
                {
                    return _flacLoaded;
                }
            }
        }

        /// <summary>
        /// Loads the FLAC add-on at most once per process. The path is resolved
        /// by the caller (main thread); pass null or empty to leave it alone.
        /// Returns true when the add-on ended up loaded, false otherwise. Never
        /// throws: a missing add-on degrades to "no native FLAC", it does not
        /// break WAV/MP3/OGG/M4A import.
        /// </summary>
        public static bool TryLoadFlacPlugin(string pluginPath)
        {
            lock (FlacGate)
            {
                if (_flacAttempted)
                {
                    return _flacLoaded;
                }
                _flacAttempted = true;
                if (string.IsNullOrWhiteSpace(pluginPath))
                {
                    return false;
                }
                // A bare file name is only resolvable if the process can see
                // it; a path we resolved ourselves has to exist on disk.
                var hasDirectory =
                    pluginPath.IndexOf(
                        Path.DirectorySeparatorChar) >= 0 ||
                    pluginPath.IndexOf(
                        Path.AltDirectorySeparatorChar) >= 0;
                if (hasDirectory && !File.Exists(pluginPath))
                {
                    return false;
                }
                try
                {
                    var handle = Bass.PluginLoad(pluginPath);
                    // BASS returns the same handle when the add-on is already
                    // loaded, and reports Errors.Already instead of failing.
                    _flacLoaded = handle != 0 ||
                                  Bass.LastError == Errors.Already;
                }
                catch (DllNotFoundException)
                {
                    _flacLoaded = false;
                }
                catch (EntryPointNotFoundException)
                {
                    _flacLoaded = false;
                }
                catch (BadImageFormatException)
                {
                    _flacLoaded = false;
                }
                return _flacLoaded;
            }
        }

        /// <summary>
        /// Converts <paramref name="sourcePath"/> to an Ogg Vorbis file at
        /// <paramref name="outputPath"/>, resampled to
        /// <paramref name="sampleRate"/> and folded down to
        /// <paramref name="channels"/> channels.
        ///
        /// Runs on a worker thread and reports failure through the return value
        /// instead of throwing, so the caller can pick a localized message.
        /// </summary>
        public static FalsumBassImportFailure ConvertToOgg(
            string sourcePath,
            string outputPath,
            string flacPluginPath,
            int sampleRate,
            int channels,
            int quality,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                error = "The source audio file is missing.";
                return FalsumBassImportFailure.SourceOpenFailed;
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                error = "The output path is empty.";
                return FalsumBassImportFailure.EncodeFailed;
            }
            if (sampleRate <= 0 || channels <= 0)
            {
                error = "The requested sample rate or channel count is invalid.";
                return FalsumBassImportFailure.MixerFailed;
            }

            if (!FalsumBassSession.Acquire(out var sessionError))
            {
                error = sessionError;
                return FalsumBassImportFailure.InitFailed;
            }
            if (!FalsumBassEncoder.IsAvailable())
            {
                error = "The BASS Ogg encoder add-ons are not available.";
                return FalsumBassImportFailure.EncodeFailed;
            }
            TryLoadFlacPlugin(flacPluginPath);

            var source = 0;
            var mixer = 0;
            try
            {
                // Decode|Float mirrors the rest of this project's BASS code
                // (FalsumBassAudioEngine). ManagedBass adds BASS_UNICODE to the
                // file name itself, so non-ASCII paths work unchanged.
                source = Bass.CreateStream(
                    sourcePath,
                    0,
                    0,
                    BassFlags.Decode | BassFlags.Float);
                if (source == 0)
                {
                    var openError = Bass.LastError;
                    error = DescribeError(openError);
                    return IsUnsupportedFormat(openError)
                        ? FalsumBassImportFailure.UnsupportedFormat
                        : FalsumBassImportFailure.SourceOpenFailed;
                }

                // Decode|Float only: MixerNonStop would make the stream endless
                // and the pump below would never terminate. The mixer is what
                // resamples to sampleRate and folds the source down to
                // `channels` (MixerChanDownMix), matching the packaged
                // 48 kHz stereo format.
                mixer = BassMix.CreateMixerStream(
                    sampleRate,
                    channels,
                    BassFlags.Decode | BassFlags.Float);
                if (mixer == 0)
                {
                    error = DescribeError(Bass.LastError);
                    return FalsumBassImportFailure.MixerFailed;
                }
                if (!BassMix.MixerAddChannel(
                        mixer,
                        source,
                        BassFlags.MixerChanDownMix))
                {
                    error = DescribeError(Bass.LastError);
                    return FalsumBassImportFailure.MixerFailed;
                }

                // The encoder owns the decode pump and the StopEx flush; it
                // also verifies that the output file exists and is non-empty.
                if (!FalsumBassEncoder.TryEncodeToOgg(
                        (uint)mixer,
                        outputPath,
                        quality,
                        out var encodeError))
                {
                    error = string.IsNullOrEmpty(encodeError)
                        ? "Ogg encoding failed."
                        : encodeError;
                    return FalsumBassImportFailure.EncodeFailed;
                }
                if (!File.Exists(outputPath) ||
                    new FileInfo(outputPath).Length == 0)
                {
                    error = "The encoder produced no output.";
                    return FalsumBassImportFailure.EmptyAudio;
                }
                return FalsumBassImportFailure.None;
            }
            catch (DllNotFoundException exception)
            {
                error = "The BASS native libraries are missing: " +
                        exception.Message;
                return FalsumBassImportFailure.InitFailed;
            }
            catch (BadImageFormatException exception)
            {
                error = "The BASS native libraries have the wrong " +
                        "architecture: " + exception.Message;
                return FalsumBassImportFailure.InitFailed;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return FalsumBassImportFailure.EncodeFailed;
            }
            finally
            {
                // Free the mixer first: it holds a reference to the source.
                if (mixer != 0)
                {
                    Bass.StreamFree(mixer);
                }
                if (source != 0)
                {
                    Bass.StreamFree(source);
                }
            }
        }

        /// <summary>
        /// The file exists and was found, but no decoder claimed it, or it
        /// carries no audio at all: either the format has no native support here
        /// (FLAC without the add-on, M4A/AAC without a Windows OS codec), the
        /// container has no audio stream (a video-only MP4 reports
        /// <c>NotAudioTrack</c>), or the file is corrupt.
        /// </summary>
        private static bool IsUnsupportedFormat(Errors error)
        {
            return error == ManagedBass.Errors.NotFile ||
                   error == ManagedBass.Errors.FileFormat ||
                   error == ManagedBass.Errors.Codec ||
                   error == ManagedBass.Errors.Decode ||
                   error == ManagedBass.Errors.Type ||
                   error == ManagedBass.Errors.NotAudioTrack;
        }

        private static string DescribeError(Errors error)
        {
            return error.ToString();
        }
    }
}
