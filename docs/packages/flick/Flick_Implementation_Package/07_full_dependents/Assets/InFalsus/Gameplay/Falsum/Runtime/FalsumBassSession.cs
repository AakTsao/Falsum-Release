using System;
using ManagedBass;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// BASS initialization for code paths that can run without a live
    /// <c>FalsumBassAudioEngine</c>: the audio preprocess encoder and the
    /// waveform envelope builder. Both decode packaged audio on a worker
    /// thread, and both can be reached from the editor before any scene audio
    /// exists, so they cannot assume the engine has initialized BASS.
    ///
    /// THIS CLASS NEVER CALLS <c>Bass.Free</c>, AND THAT IS DELIBERATE.
    /// <c>FalsumBassAudioEngine</c> owns a process-wide device with its own
    /// reference count (<c>ActiveInstanceCount</c>) and frees it when that
    /// count reaches zero. A second, independent count here cannot know that
    /// count, so the moment this helper released "its" reference it would tear
    /// the device out from under the engine, which would keep believing BASS
    /// was ready. Every subsequent audio call then failed with
    /// <c>BASS_ERROR_INIT</c> — the song stopped playing entirely after a
    /// single audio-preprocess apply.
    ///
    /// BASS is a per-process device; it costs nothing to leave it initialized,
    /// and the engine's own teardown still performs the one legitimate
    /// <c>Bass.Free</c>. Acquiring is therefore idempotent and releasing is a
    /// no-op.
    /// </summary>
    public static class FalsumBassSession
    {
        /// <summary>
        /// Device rate for decode-only sessions. A CONSTANT, not
        /// <c>AudioSettings.outputSampleRate</c>: acquiring happens on a worker
        /// thread, and Unity's Audio API throws "can only be called from the
        /// main thread" off-thread. Decode-only streams do not depend on the
        /// device rate beyond resampling, and this matches
        /// <c>FalsumMediaConverter.AudioSampleRate</c>.
        /// </summary>
        private const int SessionSampleRate = 48000;

        private static readonly object Gate = new object();
        private static bool _initialized;

        /// <summary>
        /// Ensures BASS is initialized. Returns false with
        /// <paramref name="error"/> filled when it cannot be. Calling this
        /// twice is free.
        /// </summary>
        public static bool Acquire(out string error)
        {
            error = null;
            lock (Gate)
            {
                if (_initialized)
                {
                    return true;
                }
                try
                {
                    if (!Bass.Init(
                            0,
                            SessionSampleRate,
                            DeviceInitFlags.Stereo,
                            IntPtr.Zero,
                            IntPtr.Zero) &&
                        Bass.LastError != Errors.Already)
                    {
                        error = "BASS initialization failed: " +
                                Bass.LastError + ".";
                        return false;
                    }
                    _initialized = true;
                    return true;
                }
                catch (Exception exception)
                {
                    error = "BASS initialization threw " +
                            exception.GetType().Name + ": " +
                            exception.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Intentionally does nothing. See the type comment: releasing here
        /// would free the device the audio engine is still using.
        /// </summary>
        public static void Release()
        {
            // No-op by design.
        }
    }
}
