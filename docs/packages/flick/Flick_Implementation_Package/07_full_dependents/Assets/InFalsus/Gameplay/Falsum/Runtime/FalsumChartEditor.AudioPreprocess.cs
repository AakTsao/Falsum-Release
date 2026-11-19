using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.UI;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Editor side of the audio preprocess window. Kept in its own partial so
    /// the 13k-line <c>FalsumChartEditor.cs</c> only gains the wiring line.
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>open the window with parameters derived from the current chart;</item>
    /// <item>stream the source audio through the envelope generator so the
    /// waveform appears while the user waits;</item>
    /// <item>drive the audition (beat cues included) without touching the
    /// gameplay clock or the pitch-preserving playback sink;</item>
    /// <item>apply the rewrite: encode the new audio, migrate every
    /// difficulty's text to delay 0, reset the session and refresh the
    /// presentation.</item>
    /// </list>
    ///
    /// THE ORDER IN <see cref="ApplyRoutine"/> MATTERS. The package audio is
    /// replaced, every difficulty is rewritten, and only then is the clip
    /// reloaded: reloading first would make the clock point at the old audio
    /// for one frame and the waveform on the track would show the previous
    /// song.
    /// </summary>
    public sealed partial class FalsumChartEditor
    {
        // The audition source is a plain AudioSource on the editor object,
        // deliberately NOT the gameplay song source: that one is driven by
        // GameplayClock and a pitch-preserving sink, and starting it here
        // would move the playhead and the auto-combo.
        private AudioSource _audioPreprocessAuditionSource;
        private AudioClip _audioPreprocessAuditionClip;
        private AudioClip _audioPreprocessGroundTapClip;
        private AudioClip _audioPreprocessSideTapClip;
        private FalsumAudioPreprocessView _audioPreprocessView;
        private bool _audioPreprocessOpen;
        private bool _audioPreprocessBusy;
        private double _audioPreprocessAuditionStartDsp;
        private double _audioPreprocessAuditionStartMs;
        private double _audioPreprocessAuditionEndMs;

        /// <summary>
        /// Silence inserted before the song starts during the audition, equal
        /// to <c>max(0, delay)</c>. The progress line runs on the NEW timeline,
        /// so it has to pass through this pad before the audio begins.
        /// </summary>
        private double _audioPreprocessAuditionPadMs;
        private double _audioPreprocessSilenceMs;
        private int _audioPreprocessEnvelopeGeneration;
        private double _audioPreprocessSourceDurationMs;
        private readonly List<int> _audioPreprocessScheduledBeats =
            new List<int>();
        private int _audioPreprocessNextBeatIndex;

        private void WireAudioPreprocessView()
        {
            var view = _view != null ? _view.AudioPreprocessView : null;
            if (view == null)
            {
                return;
            }
            _audioPreprocessView = view;
            view.AuditionRequested += BeginAudioPreprocessAudition;
            view.AuditionStopRequested += StopAudioPreprocessAudition;
            view.ApplyRequested += ApplyAudioPreprocess;
        }

        // ----------------------------------------------------------------
        // Automatic prompt for legacy delays
        // ----------------------------------------------------------------
        //
        // A package that still stores a non-zero `# delay:` is one that has not
        // been migrated yet, and the top bar no longer offers a delay field, so
        // without this the author has no way to discover that the chart is
        // running on the legacy timeline. Opening the window automatically is
        // the whole point of "audio preprocessing replaces the delay": the
        // migration should find the author, not wait to be found.
        //
        // It must wait for the AUDIO to finish loading: the window sizes its
        // waveform and its audition range from the clip duration, and opening
        // early would show a zero-length song and refuse to open.

        private bool _audioPreprocessPromptPending;
        private int _audioPreprocessPromptGeneration;
        private double _audioPreprocessPromptDelayMs;

        // ----------------------------------------------------------------
        // Automatic prompt after "File > New" created a package
        // ----------------------------------------------------------------
        //
        // A freshly created package always begins its life in the audio
        // preprocess step (that is where the silence offset and the beat
        // confirmation happen), so creating one should hand the author
        // straight to the window instead of dropping them on an empty
        // timeline. Two things have to happen before the window may open, and
        // both take at least one frame:
        //
        // 1. the new-chart dialog has to finish closing -- it fades out over
        //    0.14 s and stays the view's `_modal` until the tween ends, so
        //    opening during the tween would put the window behind a modal
        //    layer that then eats every click;
        // 2. the package audio has to be ready, because the window sizes its
        //    waveform and its audition range from the clip duration.
        //
        // Hence a polled pending flag instead of a direct call at the end of
        // CreateNewPackageRoutine -- the same shape the legacy-delay prompt
        // above uses, for the same reason.
        private bool _audioPreprocessAfterCreatePending;
        private int _audioPreprocessAfterCreateGeneration;
        private double _audioPreprocessAfterCreateDeadline;

        /// <summary>
        /// How long the auto-open waits for the created package's audio to
        /// finish decoding. Past this the step is skipped with a warning
        /// rather than opened against stale audio: the window derives its
        /// whole waveform and audition range from the clip duration, and
        /// showing the previous package's song there would be worse than not
        /// showing the window at all (the top-bar button still works).
        /// </summary>
        private const double AudioPreprocessAfterCreateTimeoutSeconds = 8.0;

        /// <summary>
        /// Asks for the audio preprocess window to open as soon as the
        /// freshly created package is ready. Called by
        /// <c>CreateNewPackageRoutine</c> once the package is loaded, the
        /// dialog is closed and the busy state is cleared.
        /// </summary>
        private void ScheduleAudioPreprocessAfterCreate()
        {
            _audioPreprocessAfterCreatePending = false;
            if (!_initialized || _view == null || _package == null)
            {
                return;
            }
            _audioPreprocessAfterCreatePending = true;
            _audioPreprocessAfterCreateGeneration = _packageLoadGeneration;
            _audioPreprocessAfterCreateDeadline =
                Time.unscaledTimeAsDouble +
                AudioPreprocessAfterCreateTimeoutSeconds;
        }

        /// <summary>
        /// Polled from Update: opens the window once the new-chart dialog has
        /// finished closing and the package audio is available. Every exit
        /// path clears the flag, so a creation that fails afterwards cannot
        /// leave a window waiting to pop up against some other package.
        /// </summary>
        private void TickAudioPreprocessAfterCreate()
        {
            if (!_audioPreprocessAfterCreatePending)
            {
                return;
            }
            if (_audioPreprocessAfterCreateGeneration !=
                    _packageLoadGeneration ||
                !_initialized ||
                _view == null ||
                _package == null)
            {
                // Another package was loaded (or the editor is shutting
                // down) while we were waiting: the request is stale.
                _audioPreprocessAfterCreatePending = false;
                return;
            }
            if (_audioPreprocessOpen)
            {
                // The author already opened it in the frames we waited (or
                // the legacy-delay prompt beat us to it). OpenAudioPreprocess
                // would close and re-show, discarding whatever they had
                // already typed, so the automatic step stands down.
                _audioPreprocessAfterCreatePending = false;
                return;
            }
            if (_view.IsNewChartDialogOpen || _newChartCreateInProgress)
            {
                // The dialog is still tearing down (its overlay stays the
                // view's modal until the fade completes), or a newer
                // creation is already running. Either way, wait.
                return;
            }
            var timedOut =
                Time.unscaledTimeAsDouble >=
                _audioPreprocessAfterCreateDeadline;
            // "The audio for the package we just created has arrived". The
            // clip alone is not enough: the previous package's clip is still
            // on the song source until SetSongClip runs, so checking only
            // `clip != null` would open the window with the OLD song's
            // duration. LoadPackageAudio clears `_packageAudioLoad` on its
            // success path, right after SetSongClip, so both together mean
            // "this package's audio is on the source".
            var audioReady =
                _packageAudioLoad == null &&
                _host != null &&
                _host.SongAudio != null &&
                _host.SongAudio.clip != null;
            if (!audioReady)
            {
                if (!timedOut)
                {
                    // The clip is still decoding. Waiting for it is what
                    // makes the waveform, the duration and the audition
                    // correct on arrival.
                    return;
                }
                // The decode never finished (or the created package carries
                // no audio at all). Opening now would size the waveform from
                // whatever clip the song source still holds — the PREVIOUS
                // package's song. Give up quietly instead: the author still
                // has the top-bar button, and the creation itself succeeded.
                _audioPreprocessAfterCreatePending = false;
                Debug.LogWarning(
                    "[Falsum] The created package's audio is not ready; " +
                    "skipping the automatic audio preprocess window.");
                return;
            }
            _audioPreprocessAfterCreatePending = false;
            // OpenAudioPreprocess owns the validation (loaded package,
            // decodable source audio). Reusing it keeps one definition of
            // "the package is ready for audio preprocessing". The try/catch
            // is the defensive guard the task needs: a refusal or an
            // exception here may cost the automatic step, but it must never
            // reach back into the creation that already succeeded — and it
            // must not throw out of Update either.
            try
            {
                OpenAudioPreprocess();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Audio preprocess did not open after creating " +
                    "the package: " + exception.Message);
            }
        }

        /// <summary>
        /// Records that this freshly loaded chart still carries a legacy delay,
        /// so the window should open once its audio is ready. Called by
        /// <c>LoadPackage</c> right after it kicks off the audio load.
        /// </summary>
        private void ScheduleAudioPreprocessPromptIfNeeded(Chart loadedChart)
        {
            _audioPreprocessPromptPending = false;
            if (loadedChart == null ||
                _package == null ||
                !_initialized)
            {
                return;
            }
            var delayMs = loadedChart.DelayMs;
            if (Math.Abs(delayMs) <= 0.0001)
            {
                return;
            }
            _audioPreprocessPromptDelayMs = delayMs;
            _audioPreprocessPromptPending = true;
            _audioPreprocessPromptGeneration = _packageLoadGeneration;
            Debug.Log(
                "[Falsum] Legacy delay detected (" +
                delayMs.ToString("0.###") +
                " ms); the audio preprocess window will open once the audio loads.");
        }

        /// <summary>
        /// Polled from Update: opens the window as soon as the audio for the
        /// generation that requested it has arrived. Uses the same signals
        /// <see cref="OpenAudioPreprocess"/> validates, so a package with no
        /// audio simply never prompts.
        /// </summary>
        private void TickAudioPreprocessPrompt()
        {
            if (!_audioPreprocessPromptPending)
            {
                return;
            }
            if (_audioPreprocessPromptGeneration != _packageLoadGeneration ||
                _audioPreprocessOpen)
            {
                // The package was reloaded or closed underneath us, or the
                // author already opened the window themselves.
                _audioPreprocessPromptPending = false;
                return;
            }
            if (CurrentAudioDurationMs() <= 0.0)
            {
                return;
            }
            _audioPreprocessPromptPending = false;
            var delayMs = _audioPreprocessPromptDelayMs;
            OpenAudioPreprocess();
            // Explain why the window appeared on its own, so it does not read
            // as a stray dialog.
            if (_audioPreprocessOpen)
            {
                _audioPreprocessView?.SetStatus(
                    L.Get(
                        "falsum.audioPreprocess.status.legacyDelay",
                        ("delay", delayMs.ToString(
                            "0.###",
                            System.Globalization.CultureInfo.InvariantCulture))));
            }
        }


        /// <summary>
        /// Opens the window. Called by the top-bar button; the delay input it
        /// replaces no longer exists, so this is the only delay editing path.
        /// </summary>
        private void OpenAudioPreprocess()
        {
            if (!_initialized || _view == null)
            {
                return;
            }
            if (!RequireLoadedPackage())
            {
                return;
            }
            var view = _audioPreprocessView;
            if (view == null)
            {
                return;
            }
            if (_audioPreprocessOpen)
            {
                view.Close();
            }
            PauseForEditInteraction();
            StopAudioPreprocessAudition();
            var sourceDurationMs = CurrentAudioDurationMs();
            if (sourceDurationMs <= 0.0)
            {
                ShowHint(
                    L.Get("falsum.audioPreprocess.error.sourceAudioMissing"),
                    3.0f);
                return;
            }
            _audioPreprocessOpen = true;
            _audioPreprocessBusy = false;
            _audioPreprocessSourceDurationMs = sourceDurationMs;
            // The window opens on "no change to the AUDIO CONTENT": the audio
            // delay that reproduces the chart as it stands is -storedDelay.
            // Starting there means clicking Apply without touching anything
            // still performs the migration (delay -> 0) this feature exists
            // for, without shifting a single note against the music.
            //
            // The previous version computed this value and then passed a
            // literal 0.0 to Show(), so a chart with `# delay: 60` opened with
            // an empty field and, on Apply, rewrote the audio with no shift at
            // all — every note then landed 60 ms early against the music.
            _audioPreprocessSilenceMs =
                FalsumAudioPreprocessMath.SilenceMsForDelay(
                    _session.DelayMs);
            view.Show(
                _chart.Bpm,
                _chart.Division,
                _audioPreprocessSilenceMs,
                sourceDurationMs,
                _package != null
                    ? _package.Manifest.Title
                    : string.Empty,
                DifficultyName(_currentDifficulty));
            view.SetEnvelope(null);
            view.SetStatus(L.Get("falsum.audioPreprocess.busy.envelope"));
            BeginAudioPreprocessEnvelope(sourceDurationMs);
        }

        private double CurrentAudioDurationMs()
        {
            if (_host.SongAudio != null &&
                _host.SongAudio.clip != null)
            {
                return _host.SongAudio.clip.length * 1000.0;
            }
            if (_packageAudioDurationMs > 0)
            {
                return _packageAudioDurationMs;
            }
            if (_package != null &&
                _package.Manifest != null &&
                _package.Manifest.AudioDurationMs > 0)
            {
                return _package.Manifest.AudioDurationMs;
            }
            return 0.0;
        }

        // ----------------------------------------------------------------
        // Envelope
        // ----------------------------------------------------------------

        private void BeginAudioPreprocessEnvelope(double durationMs)
        {
            _audioPreprocessEnvelopeGeneration++;
            var generation = _audioPreprocessEnvelopeGeneration;
            StartCoroutine(AudioPreprocessEnvelopeRoutine(
                generation,
                durationMs));
        }

        private IEnumerator AudioPreprocessEnvelopeRoutine(
            int generation,
            double durationMs)
        {
            var view = _audioPreprocessView;
            if (view == null || _package == null)
            {
                yield break;
            }
            // Waveform resolution follows the widest zoom window the UI offers
            // (5 s across a ~1000 px box), so a finer bucket count would only
            // cost memory.
            var sampleCount = Mathf.Clamp(
                (int)(durationMs / 5000.0 * 1024.0),
                1024,
                4096);
            FalsumAudioPreprocessEnvelopeSample[] samples = null;
            Exception failure = null;
            var audioBytes = _package.AudioBytes;
            var task = System.Threading.Tasks.Task.Run(
                delegate
                {
                    try
                    {
                        samples =
                            FalsumAudioPreprocessEnvelope.Build(
                                audioBytes,
                                sampleCount,
                                out var buildError);
                        if (samples == null ||
                            samples.Length == 0)
                        {
                            failure = new InvalidOperationException(
                                string.IsNullOrEmpty(buildError)
                                    ? "The envelope builder returned no samples."
                                    : buildError);
                        }
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                });
            while (!task.IsCompleted)
            {
                yield return null;
            }
            if (generation != _audioPreprocessEnvelopeGeneration ||
                _audioPreprocessView == null)
            {
                yield break;
            }
            if (failure != null)
            {
                Debug.LogWarning(
                    "[Falsum] Audio preprocess envelope failed: " +
                    failure.Message);
                view.SetStatus(
                    L.Get(
                        "falsum.audioPreprocess.error.encodeFailed",
                        ("error", failure.Message)));
                yield break;
            }
            view.SetEnvelope(samples);
            view.SetStatus(string.Empty);
        }

        // ----------------------------------------------------------------
        // Audition
        // ----------------------------------------------------------------
        //
        // Beat cues are the reason this window exists: the author has to hear
        // where the downbeats fall against the waveform. Ground lanes get the
        // lane tap clip, every other beat gets the side clip, matching what the
        // gameplay hit audio does (GameplayAudioRouting.TapClipForLane).
        //
        // Cues are scheduled a little ahead on the dsp clock instead of fired
        // from Update: a frame-drop of 30 ms is inaudible in a waveform, but it
        // makes the downbeat audibly late, which is exactly the thing the
        // author is trying to align.

        private void BeginAudioPreprocessAudition(double auditionEndMs)
        {
            if (!_initialized || _view == null)
            {
                return;
            }
            var parameters = TryGetAudioPreprocessParameters();
            if (parameters == null)
            {
                return;
            }
            StopAudioPreprocessAudition();
            var view = _audioPreprocessView;
            if (view == null)
            {
                return;
            }
            var songClip = _host.SongAudio != null
                ? _host.SongAudio.clip
                : null;
            if (songClip == null)
            {
                view.SetStatus(
                    L.Get("falsum.audioPreprocess.error.sourceAudioMissing"));
                return;
            }
            EnsureAudioPreprocessAuditionSource();
            if (_audioPreprocessAuditionSource == null)
            {
                return;
            }
            _audioPreprocessAuditionClip = songClip;
            _audioPreprocessAuditionStartMs = 0.0;
            // Assign the silence BEFORE computing the audition end: the end
            // depends on it (a padded rewrite lengthens the timeline), and
            // using last time's value made the range jump by one delay.
            _audioPreprocessSilenceMs = parameters.Value.DelayMs;
            _audioPreprocessAuditionEndMs = Math.Max(
                0.0,
                Math.Min(
                    auditionEndMs,
                    FalsumAudioPreprocessMath.NewAudioDurationMs(
                        _audioPreprocessSourceDurationMs,
                        _audioPreprocessSilenceMs)));
            if (_audioPreprocessAuditionEndMs <= 0.0)
            {
                return;
            }
            _audioPreprocessAuditionStartDsp = AudioSettings.dspTime + 0.05;
            _audioPreprocessNextBeatIndex = 0;

            var beatDurationMs =
                FalsumAudioPreprocessMath.BeatDurationMs(
                    parameters.Value.Bpm);
            _audioPreprocessScheduledBeats.Clear();
            FalsumAudioPreprocessMath.CollectVisibleBeats(
                0.0,
                _audioPreprocessAuditionEndMs,
                beatDurationMs,
                1 << 20,
                _audioPreprocessScheduledBeats);

            var source = _audioPreprocessAuditionSource;
            source.clip = songClip;
            source.loop = false;
            source.pitch = 1.0f;
            // The audition must reflect the delay the user typed, exactly like
            // the waveform does. New timeline t maps to source time t - S:
            //   S > 0 (pad):   the first S ms of the new timeline are SILENT,
            //                  so the song starts reading at source 0 only
            //                  after S has elapsed;
            //   S < 0 (trim):  the new timeline starts |S| ms INTO the song.
            // The original code always started at source 0, so changing the
            // delay changed the picture but not what was heard.
            var silenceMs = parameters.Value.DelayMs;
            var sourceStartMs = Math.Max(0.0, -silenceMs);
            var padMs = Math.Max(0.0, silenceMs);
            source.time = (float)(sourceStartMs / 1000.0);
            source.PlayScheduled(
                _audioPreprocessAuditionStartDsp + padMs / 1000.0);
            _audioPreprocessAuditionPadMs = padMs;            view.SetAuditionPlaying(true);
            view.SetProgress(0.0);
        }

        private void StopAudioPreprocessAudition()
        {
            var view = _audioPreprocessView;
            if (view != null)
            {
                view.SetAuditionPlaying(false);
            }
            if (_audioPreprocessAuditionSource != null)
            {
                _audioPreprocessAuditionSource.Stop();
                _audioPreprocessAuditionSource.clip = null;
            }
            _audioPreprocessScheduledBeats.Clear();
            _audioPreprocessNextBeatIndex = 0;
            _audioPreprocessAuditionEndMs = 0.0;
            _audioPreprocessAuditionPadMs = 0.0;
        }

        private void EnsureAudioPreprocessAuditionSource()
        {
            if (_audioPreprocessAuditionSource != null)
            {
                return;
            }
            // The audition source must NOT live on this editor's GameObject.
            // FalsumBassAudioEngine is an OnAudioFilterRead component attached
            // to the gameplay AudioSource on that same object; Unity allows a
            // filter component to serve exactly one AudioSource, and a second
            // source calling PlayScheduled on that object is refused with
            // "GameObject has multiple AudioSources ... OnAudioFilterRead may
            // only be used by either one". A dedicated child object keeps the
            // audition completely outside the BASS mixer's callback.
            var auditionObject = new GameObject(
                "FalsumAudioPreprocessAudition");
            auditionObject.transform.SetParent(transform, false);
            _audioPreprocessAuditionSource = auditionObject.AddComponent<AudioSource>();
            _audioPreprocessAuditionSource.playOnAwake = false;
            _audioPreprocessAuditionSource.loop = false;
            _audioPreprocessAuditionSource.spatialBlend = 0.0f;
            _audioPreprocessAuditionSource.volume =
                _preferences != null && _preferences.Audio != null
                    ? _preferences.Audio.MusicVolume
                    : 1.0f;
            ResolveAudioPreprocessTapClips();
        }

        /// <summary>
        /// Resolves the ground/side judgement cues the audition plays.
        ///
        /// The bootstrap's serialized clip fields are NOT reliable here: they
        /// are filled by <c>LoadRequiredClip</c> from Resources during startup,
        /// so in an editor scene they can be null while the gameplays still
        /// click perfectly. Reading them directly is why the audition was
        /// silent on the first run. Ask the hit-audio component for the clips
        /// it actually resolved, then fall back to the same Resources names.
        /// </summary>
        private void ResolveAudioPreprocessTapClips()
        {
            _audioPreprocessGroundTapClip = null;
            _audioPreprocessSideTapClip = null;
            var bootstrap = _host;
            if (bootstrap != null &&
                bootstrap.TryGetTapClips(
                    out var laneTap,
                    out var sideTap))
            {
                _audioPreprocessGroundTapClip = laneTap;
                _audioPreprocessSideTapClip = sideTap;
            }
            if (_audioPreprocessGroundTapClip == null)
            {
                _audioPreprocessGroundTapClip =
                    Resources.Load<AudioClip>(
                        "59_note_hit_3_draft2");
            }
            if (_audioPreprocessSideTapClip == null)
            {
                _audioPreprocessSideTapClip =
                    Resources.Load<AudioClip>(
                        "59_note_side_hit_draft2");
            }
            if (_audioPreprocessGroundTapClip == null)
            {
                // Last resort: the UI preview cue, so the audition is never
                // silently inaudible. Better a different click than no beat
                // grid at all — the whole point of the window is hearing beats.
                _audioPreprocessGroundTapClip =
                    Resources.Load<AudioClip>("alamode");
                Debug.LogWarning(
                    "[Falsum] Audio preprocess: ground tap clip is missing; " +
                    "falling back to the song clip for beat cues.");
            }
            if (_audioPreprocessSideTapClip == null)
            {
                _audioPreprocessSideTapClip = _audioPreprocessGroundTapClip;
            }
        }

        private struct AudioPreprocessParameters
        {
            public double Bpm;
            public double BeatsPerMeasure;
            public double DelayMs;
        }

        private AudioPreprocessParameters? TryGetAudioPreprocessParameters()
        {
            var view = _audioPreprocessView;
            if (view == null ||
                !view.TryGetParameters(
                    out var bpm,
                    out var beats,
                    out var delayMs))
            {
                return null;
            }
            return new AudioPreprocessParameters
            {
                Bpm = bpm,
                BeatsPerMeasure = beats,
                DelayMs = delayMs,
            };
        }

        /// <summary>
        /// Per-frame audition tick. Called from the editor's Update while the
        /// window is open and playing: it advances the progress line and fires
        /// the beat cues whose dsp time has arrived.
        /// </summary>
        private void TickAudioPreprocessAudition()
        {
            if (!_audioPreprocessOpen ||
                _audioPreprocessAuditionSource == null ||
                _audioPreprocessAuditionEndMs <= 0.0)
            {
                return;
            }
            var parameters = TryGetAudioPreprocessParameters();
            if (parameters == null)
            {
                StopAudioPreprocessAudition();
                return;
            }
            var elapsedMs =
                (AudioSettings.dspTime - _audioPreprocessAuditionStartDsp) *
                1000.0;
            if (elapsedMs < 0.0)
            {
                return;
            }
            // The progress line lives on the NEW timeline, and the song itself
            // was scheduled to start padMs later (that is how a padded head is
            // auditioned). Both therefore advance from the same t0 with no
            // extra offset to add: elapsed == new-timeline time.
            var newTimeMs = _audioPreprocessAuditionStartMs + elapsedMs;
            if (newTimeMs >= _audioPreprocessAuditionEndMs)
            {
                StopAudioPreprocessAudition();
                _audioPreprocessView?.SetProgress(
                    _audioPreprocessAuditionEndMs);
                return;
            }
            _audioPreprocessView?.SetProgress(newTimeMs);

            var beatDurationMs =
                FalsumAudioPreprocessMath.BeatDurationMs(
                    parameters.Value.Bpm);
            if (!(beatDurationMs > 0.0))
            {
                return;
            }
            // Cue lead: fire a beat once the playhead is within one frame of
            // it, so the cue lands on the beat rather than after it.
            var cueLeadMs = 16.0;
            while (_audioPreprocessNextBeatIndex <
                   _audioPreprocessScheduledBeats.Count)
            {
                var beatIndex =
                    _audioPreprocessScheduledBeats[
                        _audioPreprocessNextBeatIndex];
                var beatTimeMs = beatIndex * beatDurationMs;
                if (newTimeMs + cueLeadMs < beatTimeMs)
                {
                    break;
                }
                _audioPreprocessNextBeatIndex++;
                var isMeasureFirst =
                    FalsumAudioPreprocessMath.IsMeasureFirstBeat(
                        beatIndex,
                        Mathf.Max(
                            1,
                            Mathf.RoundToInt(
                                (float)parameters.Value.BeatsPerMeasure)));
                var clip = isMeasureFirst
                    ? _audioPreprocessGroundTapClip
                    : _audioPreprocessSideTapClip;
                if (clip != null)
                {
                    _audioPreprocessAuditionSource.PlayOneShot(
                        clip,
                        AuditionCueGain);
                }
            }
        }

        private const float AuditionCueGain = 0.42f;

        // ----------------------------------------------------------------
        // Apply
        // ----------------------------------------------------------------

        private void ApplyAudioPreprocess(
            FalsumAudioPreprocessRequest request)
        {
            if (request == null ||
                _audioPreprocessBusy ||
                !RequireLoadedPackage())
            {
                return;
            }
            var chart = BuildAudioPreprocessReferenceChart(
                request.DelayMs);
            if (chart == null)
            {
                return;
            }
            var validation =
                FalsumAudioPreprocessMath.ValidateApplication(
                    request.Bpm,
                    request.BeatsPerMeasure,
                    request.DelayMs,
                    _audioPreprocessSourceDurationMs,
                    chart);
            if (validation !=
                FalsumAudioPreprocessMath.ValidationResult.Ok)
            {
                _audioPreprocessView?.SetStatus(
                    FalsumAudioPreprocessMath.ValidationErrorMessage(
                        validation));
                return;
            }
            StopAudioPreprocessAudition();
            _audioPreprocessBusy = true;
            _audioPreprocessView?.SetBusy(
                true,
                L.Get("falsum.audioPreprocess.status.encoding"));
            StartCoroutine(ApplyAudioPreprocessRoutine(request));
        }

        /// <summary>
        /// Deep copy of the live session chart, safe to migrate without
        /// touching what the user sees. Shares the two helpers the editor
        /// already trusts (<c>CloneNote</c>) and rebuilds the derived event
        /// lists so the copy behaves like a freshly parsed chart.
        /// </summary>
        private Chart CloneChartForAudioPreprocess(Chart source)
        {
            var chart = new Chart
            {
                Bpm = source.Bpm,
                Division = source.Division,
                DelayMs = source.DelayMs,
                SongId = source.SongId,
                Difficulty = source.Difficulty,
                SourcePath = source.SourcePath,
            };
            for (var i = 0; i < source.Notes.Count; i++)
            {
                var note = source.Notes[i];
                if (note != null)
                {
                    chart.Notes.Add(CloneNote(note));
                }
            }
            for (var i = 0; i < source.Events.Count; i++)
            {
                var evt = source.Events[i];
                if (evt == null)
                {
                    continue;
                }
                chart.Events.Add(new ChartEvent
                {
                    Kind = evt.Kind,
                    IsChartBase = evt.IsChartBase,
                    TimeMs = evt.TimeMs,
                    Multiplier = evt.Multiplier,
                    Bpm = evt.Bpm,
                    Division = evt.Division,
                    Lane = evt.Lane,
                    Enabled = evt.Enabled,
                    SourceLine = evt.SourceLine,
                });
            }
            chart.RebuildTimeline();
            FalsumAudioPreprocessMath.RebuildEventKindLists(chart);
            return chart;
        }

        /// <summary>
        /// A copy of the current chart shifted onto the new timeline, used only
        /// for validation. The real session chart is not touched until the
        /// rewrite succeeds.
        /// </summary>
        private Chart BuildAudioPreprocessReferenceChart(double silenceMs)
        {
            var referenceDelayMs =
                FalsumAudioPreprocessMath.DelayForSilenceMs(silenceMs);
            var chart = CloneChartForAudioPreprocess(_chart);
            FalsumAudioPreprocessMath.MigrateChartInPlace(
                chart,
                referenceDelayMs);
            return chart;
        }

        private IEnumerator ApplyAudioPreprocessRoutine(
            FalsumAudioPreprocessRequest request)
        {
            var newAudioBytes = (byte[])null;
            var newDurationMs = 0;
            string failure = null;
            var audioBytes = _package.AudioBytes;
            var silenceMs = request.DelayMs;
            // Resolve the temp directory HERE, on the main thread:
            // Application.persistentDataPath throws off-thread, and the encode
            // below runs on a worker. This is the same split
            // FalsumMediaConverter.PrepareAudioConversion already uses.
            var workDirectory = Path.Combine(
                Application.persistentDataPath,
                "FalsumConversion");
            var task = System.Threading.Tasks.Task.Run(
                delegate
                {
                    try
                    {
                        newAudioBytes =
                            FalsumAudioPreprocessEncoder.RewriteToOgg(
                                audioBytes,
                                silenceMs,
                                6,
                                workDirectory,
                                out newDurationMs,
                                out failure);
                    }
                    catch (Exception exception)
                    {
                        failure = exception.Message;
                    }
                });
            while (!task.IsCompleted)
            {
                yield return null;
            }
            if (newAudioBytes == null ||
                newAudioBytes.Length == 0 ||
                newDurationMs <= 0)
            {
                _audioPreprocessBusy = false;
                _audioPreprocessView?.SetBusy(false, null);
                _audioPreprocessView?.SetStatus(
                    L.Get(
                        "falsum.audioPreprocess.error.encodeFailed",
                        ("error",
                         string.IsNullOrEmpty(failure)
                             ? "unknown"
                             : failure)));
                yield break;
            }

            // Migrate every difficulty BEFORE mutating anything shared: a
            // failure in the middle of the loop must leave the package
            // untouched, not half-converted.
            string[] newTexts = new string[4];
            var referenceDelayMs =
                FalsumAudioPreprocessMath.DelayForSilenceMs(silenceMs);
            for (var i = 0; i < 4; i++)
            {
                if (i == _currentDifficulty)
                {
                    // The CURRENT difficulty is migrated in memory, never
                    // through text.
                    //
                    // Serialising it first with its OLD delay is what threw
                    // "谱面内容包含负数音乐时间" for negative-delay packages:
                    // ChartWriter emits the derived track(-D,1.00) /
                    // bpm(-D,...) headline events, which are negative music
                    // time, and its own guard rejects them. A negative-delay
                    // chart therefore could never be saved at all — and the
                    // apply path inherited that. Migrating the live chart and
                    // serialising it once with delay 0 never produces a
                    // negative headline.
                    //
                    // Work on a CLONE: a mid-loop failure must leave the live
                    // session exactly as the user left it, and the clone is
                    // what gets written into the package.
                    var sessionChart = CloneChartForAudioPreprocess(
                        _session.Chart);
                    var inPlaceResult =
                        FalsumAudioPreprocessMath.MigrateChartInPlace(
                            sessionChart,
                            referenceDelayMs);
                    if (inPlaceResult !=
                        FalsumAudioPreprocessMath.TextMigrationResult.Ok)
                    {
                        _audioPreprocessBusy = false;
                        _audioPreprocessView?.SetBusy(false, null);
                        _audioPreprocessView?.SetStatus(
                            L.Get(
                                FalsumAudioPreprocessMath
                                    .TextMigrationErrorKey(inPlaceResult),
                                ("difficulty", DifficultyName(i))));
                        yield break;
                    }
                    newTexts[i] = ChartWriter.Write(
                        sessionChart,
                        0.0,
                        _manifest.SongId,
                        i);
                    continue;
                }
                var sourceText = _package.LoadChartText(i);
                if (string.IsNullOrWhiteSpace(sourceText))
                {
                    continue;
                }
                var migrated =
                    FalsumAudioPreprocessMath.ProcessDifficultyText(
                        sourceText,
                        i,
                        _manifest.SongId,
                        referenceDelayMs,
                        out var migrationResult);
                if (migrated == null)
                {
                    _audioPreprocessBusy = false;
                    _audioPreprocessView?.SetBusy(false, null);
                    _audioPreprocessView?.SetStatus(
                        L.Get(
                            FalsumAudioPreprocessMath.TextMigrationErrorKey(
                                migrationResult),
                            ("difficulty",
                             DifficultyName(i))));
                    yield break;
                }
                newTexts[i] = migrated;
            }

            // Snapshot the pre-migration package state. If the save fails the
            // in-memory package must be put back, otherwise the editor shows the
            // rewritten audio and delay-0 charts while the file on disk still
            // holds the old ones — and the next autosave would silently push the
            // half-applied state out.
            var originalAudioBytes = _package.AudioBytes;
            var originalAudioDurationMs = _package.Manifest.AudioDurationMs;
            string[] originalTexts = new string[4];
            for (var i = 0; i < 4; i++)
            {
                originalTexts[i] = _package.LoadChartText(i);
            }
            try
            {
                // Commit. The commit is all-or-nothing from the user's point
                // of view because nothing above mutated the package yet.
                for (var i = 0; i < 4; i++)
                {
                    if (newTexts[i] != null)
                    {
                        _package.SetChartText(i, newTexts[i]);
                    }
                }
                _package.SetAudio(newAudioBytes, newDurationMs);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _audioPreprocessBusy = false;
                _audioPreprocessView?.SetBusy(false, null);
                _audioPreprocessView?.SetStatus(
                    L.Get(
                        "falsum.audioPreprocess.error.encodeFailed",
                        ("error", exception.Message)));
                yield break;
            }

            // Persist the rewritten package BEFORE reloading the audio.
            //
            // Reloading from the in-memory package is not enough: the cache
            // file the audio engine streams from is named after the audio
            // HASH, and a rewrite that produces identical bytes would reuse a
            // path that an open BASS stream is still holding — the write then
            // silently fails or the old stream keeps serving old audio, which
            // is the reported "music did not switch to the new audio". Saving
            // first makes the on-disk package the source of truth; the reload
            // below then re-enters the normal package-load path, which also
            // re-resolves the background, the manifest and every difficulty.
            var saveError = (string)null;
            if (!SavePackageAfterAudioPreprocess(out saveError))
            {
                for (var i = 0; i < 4; i++)
                {
                    if (string.IsNullOrWhiteSpace(originalTexts[i]))
                    {
                        continue;
                    }
                    _package.SetChartText(i, originalTexts[i]);
                }
                if (originalAudioBytes != null)
                {
                    _package.SetAudio(
                        originalAudioBytes,
                        originalAudioDurationMs);
                }
                _audioPreprocessBusy = false;
                _audioPreprocessView?.SetBusy(false, null);
                _audioPreprocessView?.SetStatus(
                    string.IsNullOrEmpty(saveError)
                        ? L.Get(
                            "falsum.audioPreprocess.error.saveNoPath")
                        : saveError);
                ShowHint(
                    string.IsNullOrEmpty(saveError)
                        ? L.Get(
                            "falsum.audioPreprocess.error.saveNoPath")
                        : saveError,
                    5.0f);
                yield break;
            }

            // The session chart must be the migrated text, not the pre-migration
            // one: reloading the current difficulty from the package is the
            // only way to get the same-normalized form the file now holds.
            _audioPreprocessBusy = false;
            _audioPreprocessView?.SetBusy(false, null);
            if (_audioPreprocessView != null)
            {
                _audioPreprocessView.SetStatus(
                    L.Get("falsum.audioPreprocess.status.applied"));
                _audioPreprocessView.Close();
            }
            _audioPreprocessOpen = false;
            _audioPreprocessEnvelopeGeneration++;
            SyncClockDelay();
            ReloadCurrentDifficultyAfterAudioPreprocess();
            MarkDirty();
            ShowHint(
                L.Get("falsum.audioPreprocess.status.applied"),
                3.0f);
            UpdateViews();
        }

        /// <summary>
        /// Writes the rewritten package back to disk and re-enters the normal
        /// package load path.
        ///
        /// Saving first is the fix for "the music did not switch to the new
        /// audio": the audio cache file is named after the audio hash, so a
        /// rewrite whose bytes happen to match an already-cached clip reuses a
        /// path that an open BASS stream still holds, and the reload can end up
        /// reading the old clip. Persisting the package makes disk the source
        /// of truth, and LoadPackage then re-resolves audio, manifest,
        /// background and every difficulty through the one path the rest of the
        /// editor already trusts.
        /// </summary>
        private bool SavePackageAfterAudioPreprocess(out string error)
        {
            error = null;
            var targetPath = _package != null
                ? _package.SourcePath
                : null;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                error = L.Get("falsum.audioPreprocess.error.saveNoPath");
                return false;
            }

            // The package file is often opened straight out of a download
            // directory, where a sync client, an antivirus scanner or another
            // editor instance can hold it open. FalsumPackage.SaveTo ends with
            // File.Copy(temp, target, overwrite), which throws IOException
            // while the target is locked — and that used to surface only as a
            // stack trace in the console, with the audio already rewritten in
            // memory and the package unchanged on disk.
            //
            // Retry a few times with a short backoff (scanners usually release
            // within a second), then fail with an actionable message instead of
            // a raw IOException.
            const int attempts = 4;
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    _package.Save(targetPath);
                    _isDirty = false;
                    return true;
                }
                catch (IOException exception)
                {
                    if (attempt == attempts)
                    {
                        Debug.LogWarning(
                            "[Falsum] Audio preprocess could not save " +
                            targetPath + ": " + exception.Message);
                        error = L.Get(
                            "falsum.audioPreprocess.error.packageLocked",
                            ("file", Path.GetFileName(targetPath)));
                        return false;
                    }
                    // Block briefly on purpose: this runs inside a coroutine
                    // that has already finished the expensive encode, so a
                    // couple of hundred milliseconds is cheap next to losing
                    // the whole rewrite.
                    System.Threading.Thread.Sleep(150 * attempt);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    error = exception.Message;
                    return false;
                }
            }
            return false;
        }

        private void ReloadCurrentDifficultyAfterAudioPreprocess()
        {
            // Full reload, not a partial one. LoadPackage re-reads the package
            // from disk, rebuilds the session, and restarts the audio load
            // through BeginPackageAudioLoad, which is the only place
            // _packageLoadGeneration is bumped. Doing it here by hand used to
            // hand StartCoroutine a generation that another path could revoke,
            // and the reload then silently yielded out before SetSongClip.
            var path = _package != null ? _package.SourcePath : null;
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                Debug.LogWarning(
                    "[Falsum] Audio preprocess: the package is not on disk; " +
                    "keeping the in-memory state and reloading the audio only.");
                ReloadPackageAudioOnly();
                return;
            }
            var difficulty = _currentDifficulty;
            LoadPackage(
                path,
                false,
                preferredDifficulty: difficulty);
        }

        /// <summary>
        /// Fallback when the package was never saved to disk: reload just the
        /// clip through the same cache-write path.
        /// </summary>
        private void ReloadPackageAudioOnly()
        {
            if (_packageAudioLoad != null)
            {
                StopCoroutine(_packageAudioLoad);
                _packageAudioLoad = null;
            }
            _packageLoadGeneration++;
            _packageAudioLoad = StartCoroutine(
                LoadPackageAudio(
                    _package,
                    _packageLoadGeneration,
                    _playheadMs));
        }

        private double newDurationMsForPlayhead()
        {
            return _package != null &&
                   _package.Manifest != null
                ? Math.Max(
                    0.0,
                    _package.Manifest.AudioDurationMs - _session.DelayMs)
                : _playheadMs;
        }

        private void CloseAudioPreprocess()
        {
            StopAudioPreprocessAudition();
            _audioPreprocessOpen = false;
            _audioPreprocessEnvelopeGeneration++;
        }

        // ----------------------------------------------------------------
        // Diagnostics for the capture tool
        // ----------------------------------------------------------------

        public bool DebugOpenAudioPreprocess()
        {
            OpenAudioPreprocess();
            return _audioPreprocessOpen;
        }

        /// <summary>
        /// Duration of the audio the window would preprocess right now, in
        /// milliseconds. Zero means the package or its clip has not finished
        /// loading yet, which is the only reason opening the window can
        /// legitimately fail — the capture tool waits on this.
        /// </summary>
        public double DebugAudioPreprocessSourceDurationMs
        {
            get { return CurrentAudioDurationMs(); }
        }

        public void DebugCloseAudioPreprocess()
        {
            _audioPreprocessView?.Close();
            CloseAudioPreprocess();
        }

        public bool DebugSetAudioPreprocessDelay(double delayMs)
        {
            var view = _audioPreprocessView;
            return view != null && view.DebugSetDelay(delayMs);
        }

        public bool DebugBeginAudioPreprocessAudition()
        {
            if (_audioPreprocessView == null)
            {
                return false;
            }
            BeginAudioPreprocessAudition(
                Math.Min(30000.0, _audioPreprocessSourceDurationMs));
            return _audioPreprocessAuditionEndMs > 0.0;
        }

        public void DebugStopAudioPreprocessAudition()
        {
            StopAudioPreprocessAudition();
        }

        /// <summary>
        /// One-line state dump for the capture tool: whether the window is
        /// open, whether its waveform got a real layout rect, how many envelope
        /// samples it received, and whether the audition is running.
        /// A "open=true" prefix is the field the capture asserts on.
        /// </summary>
        public string DebugAudioPreprocessSummary
        {
            get
            {
                var view = _audioPreprocessView;
                var open = view != null && view.IsOpen;
                var waveform = view != null
                    ? view.DebugWaveformSummary
                    : "<none>";
                return "open=" + (open ? "true" : "false") +
                       " panels=" + (_audioPreprocessView != null ? 1 : 0) +
                       " envelopeGeneration=" +
                       _audioPreprocessEnvelopeGeneration +
                       " sourceDurationMs=" +
                       _audioPreprocessSourceDurationMs.ToString("0.0") +
                       " silenceMs=" +
                       _audioPreprocessSilenceMs.ToString("0.###") +
                       " auditionPlaying=" +
                       (_audioPreprocessAuditionSource != null &&
                        _audioPreprocessAuditionSource.isPlaying
                            ? "true"
                            : "false") +
                       " " + waveform;
            }
        }
    }
}
