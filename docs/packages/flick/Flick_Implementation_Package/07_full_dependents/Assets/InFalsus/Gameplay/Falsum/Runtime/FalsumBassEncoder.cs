using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Minimal native binding for BASSenc + BASSenc_OGG.
    ///
    /// The managed BASS wrappers in this project (ManagedBass, ManagedBass.Fx,
    /// ManagedBass.Mix) do NOT expose the encoder add-ons — the metadata of
    /// ManagedBass.dll contains no <c>BASS_Encode_*</c> entry point — so the
    /// declarations below are the whole surface needed to turn a decoded BASS
    /// channel into an Ogg Vorbis file.
    ///
    /// WHY THIS EXISTS: converting an arbitrary audio file into the package's
    /// required Ogg Vorbis used to shell out to a 61 MB ffmpeg.exe through
    /// <c>Process.Start</c>. That can never work on Android (no child
    /// processes, no .exe, and StreamingAssets is not a filesystem), so the
    /// desktop-only path was a hard blocker for mobile. BASS is already the
    /// audio backend on every platform here, and BASSenc_OGG is a ~180 KB
    /// add-on that does the same job in-process: no temp executable, no stdout
    /// parsing, no platform-specific path handling.
    ///
    /// THIS IS NOW THE ONLY ROUTE for every Ogg write in this project: audio
    /// import (<c>FalsumBassMediaImporter</c>) and the audio-preprocess window
    /// (<c>FalsumAudioPreprocessEncoder</c>) both go through
    /// <see cref="TryEncodeToOgg"/>. There is no fallback to an external
    /// process anywhere, by design.
    /// </summary>
    public static class FalsumBassEncoder
    {
        // BASSenc flags (bassenc.h).
        private const uint EncodeFp16Bit = 4;
        private const uint EncodeTerminate = 0x10000;
        private const uint EncodeStopWait = 2;

        /// <summary>
        /// bass.h: <c>#define BASS_UNICODE 0x80000000</c> — the filename string
        /// is UTF-16 instead of ANSI. Required here because the editor's paths
        /// contain non-ASCII characters (the project lives under a Chinese
        /// directory name), and an ANSI filename would be mis-encoded on any
        /// machine whose ANSI code page does not match.
        /// </summary>
        private const uint BassUnicode = 0x80000000;

        private const string EncLib = "bassenc";
        private const string OggLib = "bassenc_ogg";

        /// <summary>
        /// Read buffer for the decode pump. Declared as float because the
        /// channel is created with <c>BassFlags.Float</c>, matching the
        /// convention the rest of this project's BASS code already uses
        /// (<c>FalsumBassAudioEngine</c> reads its mixer the same way).
        /// </summary>
        private static readonly float[] _pumpBuffer = new float[16384];

        [DllImport(EncLib, EntryPoint = "BASS_Encode_GetVersion")]
        private static extern uint BASS_Encode_GetVersion();

        [DllImport(EncLib, EntryPoint = "BASS_Encode_StopEx")]
        private static extern int BASS_Encode_StopEx(IntPtr encode, uint flags);

        [DllImport(OggLib, EntryPoint = "BASS_Encode_OGG_GetVersion")]
        private static extern uint BASS_Encode_OGG_GetVersion();

        /// <summary>
        /// Starts encoding a BASS channel to <paramref name="filename"/>.
        /// <c>HENCODE</c> is 32/64-bit sized, so it must be marshalled as
        /// <see cref="IntPtr"/>, not as an int.
        /// </summary>
        [DllImport(OggLib, EntryPoint = "BASS_Encode_OGG_StartFile",
            CharSet = CharSet.Unicode)]
        private static extern IntPtr BASS_Encode_OGG_StartFile(
            uint handle,
            string options,
            uint flags,
            string filename);

        /// <summary>
        /// True when both native modules load and report a BASS 2.4 build.
        /// Callers should read false as "no encoder on this platform" rather
        /// than as an error, so a missing add-on degrades instead of throwing.
        /// </summary>
        public static bool IsAvailable()
        {
            try
            {
                return BASS_Encode_GetVersion() > 0 &&
                       BASS_Encode_OGG_GetVersion() > 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Encodes an already-open, decodable BASS channel into an Ogg Vorbis
        /// file. Returns false and fills <paramref name="error"/> instead of
        /// throwing, because the caller runs on a worker thread.
        ///
        /// IMPORTANT: the playable data has to be PUMPED through this call.
        /// A <c>BASS_STREAM_DECODE</c> channel produces nothing on its own —
        /// there is no output device pulling it — so starting an encoder and
        /// immediately stopping it writes only the Ogg header (~3.8 KB) and no
        /// audio. That failure mode is silent and plausible-looking (the file
        /// exists, has the right magic, and reports a zero duration), which is
        /// why this method owns the read loop instead of leaving it to callers.
        /// </summary>
        /// <param name="channel">
        /// BASS channel handle (the value <c>Bass.CreateStream</c> returned),
        /// created with <c>BassFlags.Decode</c>.
        /// </param>
        /// <param name="outputPath">Absolute path of the .ogg to write.</param>
        /// <param name="quality">
        /// Vorbis quality 0..10. The editor's ffmpeg call used <c>-q:a 6</c>;
        /// OGGENC's equivalent is <c>-q 6</c>.
        /// </param>
        /// <param name="error">Receives a human-readable failure reason.</param>
        public static bool TryEncodeToOgg(
            uint channel,
            string outputPath,
            int quality,
            out string error)
        {
            error = null;
            if (channel == 0)
            {
                error = "BASS channel handle is zero.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                error = "Output path is empty.";
                return false;
            }

            try
            {
                var clamped = Mathf.Clamp(quality, 0, 10);
                var encode = BASS_Encode_OGG_StartFile(
                    channel,
                    "-q " + clamped.ToString(),
                    EncodeFp16Bit | EncodeTerminate | BassUnicode,
                    outputPath);
                if (encode == IntPtr.Zero)
                {
                    error = "BASS_Encode_OGG_StartFile failed: " +
                            BassLastError() + ".";
                    return false;
                }

                // Pump the decode channel until it is exhausted. The encoder
                // consumes each block as ChannelGetData returns it.
                if (!TryPumpChannel(channel, out string pumpError))
                {
                    error = pumpError;
                    BASS_Encode_StopEx(encode, EncodeStopWait);
                    return false;
                }

                // StopEx with WAIT flushes the tail and closes the file.
                if (BASS_Encode_StopEx(encode, EncodeStopWait) == 0)
                {
                    error = "BASS_Encode_StopEx failed.";
                    return false;
                }
                if (!File.Exists(outputPath) ||
                    new FileInfo(outputPath).Length == 0)
                {
                    error = "Encoder produced no output.";
                    return false;
                }
                return true;
            }
            catch (DllNotFoundException)
            {
                error = "BASSenc modules are not available on this platform.";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                error = "BASSenc modules are an incompatible version.";
                return false;
            }
            catch (BadImageFormatException exception)
            {
                error = "BASSenc modules have the wrong architecture: " +
                        exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Reads the decode channel to exhaustion. BASS returns the byte count
        /// per call; a non-positive result means the stream ended (or failed).
        /// </summary>
        private static bool TryPumpChannel(
            uint channel,
            out string error)
        {
            error = null;
            while (true)
            {
                var got = ManagedBass.Bass.ChannelGetData(
                    (int)channel,
                    _pumpBuffer,
                    _pumpBuffer.Length * sizeof(float));
                if (got <= 0)
                {
                    var last = ManagedBass.Bass.LastError;
                    // ChannelGetData returns <= 0 both at end-of-stream and on
                    // failure; BASS distinguishes them with Errors.Ended.
                    // Treating Ended as a failure silently loses the whole
                    // song, so both OK and Ended count as a clean exit.
                    if (last == ManagedBass.Errors.OK ||
                        last == ManagedBass.Errors.Ended)
                    {
                        return true;
                    }
                    error = "Decoding the source failed: " + last + ".";
                    return false;
                }
            }
        }

        /// <summary>
        /// Reads BASS's last error through the managed wrapper, so this file
        /// needs no additional P/Invoke into bass.dll.
        /// </summary>
        private static string BassLastError()
        {
            try
            {
                return ManagedBass.Bass.LastError.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
