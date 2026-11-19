using System;
using System.Collections;
using System.Collections.Generic;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Audio
{
    public static class GameplayAudioRouting
    {
        public const double TapChordWindowMs = 1.0;

        public static bool IsSideLane(int lane)
        {
            return lane == 0 || lane == 5;
        }

        public static bool IsSameTapChord(double leftTimeMs, double rightTimeMs)
        {
            return Math.Abs(leftTimeMs - rightTimeMs) <= TapChordWindowMs;
        }

        public static AudioClip TapClipForLane(
            int lane,
            AudioClip laneTap,
            AudioClip sideTap)
        {
            return IsSideLane(lane) ? sideTap : laneTap;
        }
    }

    public sealed class GameplayHitAudio : MonoBehaviour
    {
        public const float OneShotGain = 0.42f;
        public const float HoldLoopGain = 0.24f;
        public const int OneShotPriority = 2;
        public const int HoldLoopPriority = 32;
        public const int OneShotVoiceCapacity = 48;

        private const double VoiceEndToleranceDsp = 0.00001;

        private enum HitSoundCategory
        {
            SkyArea,
            Tap,
            Hold,
            Flick,
        }

        private sealed class SkyGroup
        {
            public int GroupId;
            public double FirstTimeMs;
            public double LastTimeMs;
            public bool Started;
            public bool Scheduled;
            public readonly List<ChartNote> Segments = new List<ChartNote>();
        }

        private JudgementEngine _engine;
        private AudioClip _skyStartClip;
        private AudioClip _laneTapClip;
        private AudioClip _sideTapClip;
        private AudioClip _flickClip;
        private AudioClip _groundHoldLoopClip;
        private AudioClip _skyHoldLoopClip;

        private AudioSource[] _oneShotSources;
        private double[] _oneShotStartDsp;
        private double[] _oneShotEndDsp;
        private bool[] _oneShotBusy;
        private AudioSource _groundHoldSource;
        private AudioSource _skyHoldSource;
        private AudioSource _previewCueSource;
        private FalsumBassAudioEngine _bassAudio;

        private readonly List<ChartNote> _activeHolds = new List<ChartNote>();
        private readonly List<SkyGroup> _skyGroups = new List<SkyGroup>();
        private readonly Dictionary<ChartNote, SkyGroup> _skyGroupByNote =
            new Dictionary<ChartNote, SkyGroup>();
        private readonly HashSet<SkyGroup> _activeSkyGroups =
            new HashSet<SkyGroup>();
        private readonly List<SkyGroup> _expiredSkyGroups =
            new List<SkyGroup>();
        private readonly HashSet<ChartNote> _editorScheduledNotes =
            new HashSet<ChartNote>();
        private readonly List<ChartNote> _editorAudioNotes =
            new List<ChartNote>();

        private bool _initialized;
        private bool _paused;
        private bool _editorAudioScheduling;
        private int _editorAudioCursor;
        private double _editorAudioScheduledThroughMs =
            double.NegativeInfinity;
        private float _skyAreaVolume = 1.0f;
        private float _tapVolume = 1.0f;
        private float _holdVolume = 1.0f;
        private float _flickVolume = 1.0f;

        public int ActiveHoldCount
        {
            get { return _activeHolds.Count; }
        }

        public int ActiveSkyGroupCount
        {
            get { return _activeSkyGroups.Count; }
        }

        public int PlayedOneShotCount { get; private set; }
        public int StartedHoldCount { get; private set; }
        public int StartedSkyGroupCount { get; private set; }
        public int EvaluatedTapCount { get; private set; }
        public int CoalescedTapCount { get; private set; }
        public int StolenOneShotCount { get; private set; }
        public int ActiveOneShotVoiceCount
        {
            get
            {
                if (_bassAudio != null)
                {
                    return Math.Max(
                        0,
                        _bassAudio.ActiveMixerChannelCount - 1 -
                        _activeHolds.Count -
                        (_activeSkyGroups.Count > 0 ? 1 : 0));
                }
                if (_oneShotBusy == null)
                {
                    return 0;
                }
                var nowDsp = AudioSettings.dspTime;
                var count = 0;
                for (var i = 0; i < _oneShotBusy.Length; i++)
                {
                    if (_oneShotBusy[i] &&
                        nowDsp < _oneShotEndDsp[i] - VoiceEndToleranceDsp)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public void Initialize(
            Chart chart,
            JudgementEngine engine,
            AudioClip skyStartClip,
            AudioClip laneTapClip,
            AudioClip sideTapClip,
            AudioClip flickClip,
            AudioClip groundHoldLoopClip,
            AudioClip skyHoldLoopClip,
            FalsumBassAudioEngine bassAudio = null)
        {
            _engine = engine;
            _skyStartClip = skyStartClip;
            _laneTapClip = laneTapClip;
            _sideTapClip = sideTapClip;
            _flickClip = flickClip;
            _groundHoldLoopClip = groundHoldLoopClip;
            _skyHoldLoopClip = skyHoldLoopClip;
            _bassAudio = bassAudio;

            BuildSkyGroups(chart);
            BuildEditorAudioNotes(chart);
            _previewCueSource = CreateSource(
                "GameplayPreviewTapCue",
                false,
                1.0f,
                OneShotPriority);
            if (_bassAudio == null)
            {
                CreateOneShotPool();
                _groundHoldSource = CreateSource(
                    "GameplayGroundHoldLoop",
                    true,
                    HoldLoopGain,
                    HoldLoopPriority);
                _skyHoldSource = CreateSource(
                    "GameplaySkyHoldLoop",
                    true,
                    HoldLoopGain,
                    HoldLoopPriority);
            }

            _engine.NoteStarted += HandleNoteStarted;
            _engine.Judged += HandleJudged;
            _initialized = true;
        }

        public void SetBassAudio(FalsumBassAudioEngine bassAudio)
        {
            _bassAudio = bassAudio;
            _editorAudioScheduling = _bassAudio != null;
        }

        public IEnumerator PreloadBassClips()
        {
            if (_bassAudio == null)
            {
                yield break;
            }
            var clips = new[]
            {
                _skyStartClip,
                _laneTapClip,
                _sideTapClip,
                _flickClip,
                _groundHoldLoopClip,
                _skyHoldLoopClip,
            };
            var seen = new HashSet<int>();
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null ||
                    !seen.Add(clip.GetInstanceID()))
                {
                    continue;
                }
                yield return _bassAudio.PreloadClip(clip);
            }
        }

        public void SetVolumes(
            float skyAreaVolume,
            float tapVolume,
            float holdVolume,
            float flickVolume)
        {
            _skyAreaVolume = Mathf.Clamp01(skyAreaVolume);
            _tapVolume = Mathf.Clamp01(tapVolume);
            _holdVolume = Mathf.Clamp01(holdVolume);
            _flickVolume = Mathf.Clamp01(flickVolume);
            ApplyLoopState();
        }

        public bool PlayTapPreviewCue()
        {
            if (!_initialized ||
                _previewCueSource == null ||
                _laneTapClip == null)
            {
                return false;
            }
            _previewCueSource.PlayOneShot(
                _laneTapClip,
                OneShotGain * Mathf.Clamp01(_tapVolume));
            PlayedOneShotCount++;
            return true;
        }

        /// <summary>
        /// The clips this component actually plays, as resolved at
        /// <c>Initialize</c> time. The bootstrap's serialized clip fields can
        /// be null in the editor scene (they are filled by
        /// <c>LoadRequiredClip</c> from Resources at startup), so any other
        /// consumer that wants the real judgement sound — the audio preprocess
        /// audition — must ask here rather than read the bootstrap fields.
        /// </summary>
        public AudioClip LaneTapClip
        {
            get { return _laneTapClip; }
        }

        public AudioClip SideTapClip
        {
            get { return _sideTapClip; }
        }

        public void StopPreviewCues()
        {
            if (_previewCueSource != null)
            {
                _previewCueSource.Stop();
            }
        }

        public void Tick(double nowMs)
        {
            if (!_initialized || _paused)
            {
                return;
            }

            for (var i = _activeHolds.Count - 1; i >= 0; i--)
            {
                var note = _activeHolds[i];
                if (!note.Holding || nowMs >= note.EndTimeMs)
                {
                    _activeHolds.RemoveAt(i);
                }
            }

            _expiredSkyGroups.Clear();
            foreach (var group in _activeSkyGroups)
            {
                if (nowMs >= group.LastTimeMs)
                {
                    _expiredSkyGroups.Add(group);
                }
            }
            for (var i = 0; i < _expiredSkyGroups.Count; i++)
            {
                _activeSkyGroups.Remove(_expiredSkyGroups[i]);
            }

            ApplyLoopState();
        }

        public void SetPaused(bool paused)
        {
            if (!_initialized || _paused == paused)
            {
                return;
            }

            _paused = paused;
            if (paused)
            {
                if (_bassAudio != null)
                {
                    _bassAudio.StopTransientChannels();
                }
                else
                {
                    PauseOneShotPool();
                    _groundHoldSource.Pause();
                    _skyHoldSource.Pause();
                }
            }
            else
            {
                if (_bassAudio == null)
                {
                    ResumeOneShotPool();
                }
                ApplyLoopState();
            }
        }

        public void ResetAudio()
        {
            if (!_initialized)
            {
                return;
            }

            _activeHolds.Clear();
            _activeSkyGroups.Clear();
            _paused = false;
            PlayedOneShotCount = 0;
            StartedHoldCount = 0;
            StartedSkyGroupCount = 0;
            EvaluatedTapCount = 0;
            CoalescedTapCount = 0;
            StolenOneShotCount = 0;
            _editorAudioScheduling = false;
            _editorAudioCursor = 0;
            _editorAudioScheduledThroughMs =
                double.NegativeInfinity;
            _editorScheduledNotes.Clear();
            for (var i = 0; i < _skyGroups.Count; i++)
            {
                _skyGroups[i].Started = false;
                _skyGroups[i].Scheduled = false;
            }
            if (_bassAudio != null)
            {
                _bassAudio.StopTransientChannels();
            }
            else
            {
                StopOneShotPool();
                _groundHoldSource.Stop();
                _skyHoldSource.Stop();
            }
        }

        public void Rebuild(Chart chart, JudgementEngine engine)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "GameplayHitAudio is not initialized.");
            }
            if (_engine != null)
            {
                _engine.NoteStarted -= HandleNoteStarted;
                _engine.Judged -= HandleJudged;
            }
            _engine = engine;
            _activeHolds.Clear();
            _activeSkyGroups.Clear();
            _skyGroups.Clear();
            _skyGroupByNote.Clear();
            BuildSkyGroups(chart);
            BuildEditorAudioNotes(chart);
            _engine.NoteStarted += HandleNoteStarted;
            _engine.Judged += HandleJudged;
            ResetAudio();
        }

        private AudioSource CreateSource(
            string name,
            bool loop,
            float gain,
            int priority)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0.0f;
            source.volume = gain;
            source.priority = priority;
            source.dopplerLevel = 0.0f;
            return source;
        }

        private void CreateOneShotPool()
        {
            _oneShotSources = new AudioSource[OneShotVoiceCapacity];
            _oneShotStartDsp = new double[OneShotVoiceCapacity];
            _oneShotEndDsp = new double[OneShotVoiceCapacity];
            _oneShotBusy = new bool[OneShotVoiceCapacity];
            for (var i = 0; i < OneShotVoiceCapacity; i++)
            {
                _oneShotSources[i] = CreateSource(
                    "GameplayHitVoice_" + i.ToString("00"),
                    false,
                    OneShotGain,
                    OneShotPriority);
            }
        }

        private void PauseOneShotPool()
        {
            if (_oneShotSources == null)
            {
                return;
            }
            for (var i = 0; i < _oneShotSources.Length; i++)
            {
                if (_oneShotBusy[i])
                {
                    _oneShotSources[i].Pause();
                }
            }
        }

        private void ResumeOneShotPool()
        {
            if (_oneShotSources == null)
            {
                return;
            }
            var nowDsp = AudioSettings.dspTime;
            for (var i = 0; i < _oneShotSources.Length; i++)
            {
                if (!_oneShotBusy[i])
                {
                    continue;
                }

                var source = _oneShotSources[i];
                if (source.clip == null ||
                    source.time >= source.clip.length - 0.001f)
                {
                    source.Stop();
                    _oneShotBusy[i] = false;
                    continue;
                }

                source.UnPause();
                _oneShotEndDsp[i] =
                    nowDsp + source.clip.length - source.time;
            }
        }

        private void StopOneShotPool()
        {
            if (_oneShotSources == null)
            {
                return;
            }
            for (var i = 0; i < _oneShotSources.Length; i++)
            {
                _oneShotSources[i].Stop();
                _oneShotBusy[i] = false;
                _oneShotStartDsp[i] = 0.0;
                _oneShotEndDsp[i] = 0.0;
            }
        }

        public void ResetEditorAudioSchedule(double startTimeMs)
        {
            _editorScheduledNotes.Clear();
            for (var i = 0; i < _skyGroups.Count; i++)
            {
                _skyGroups[i].Scheduled = false;
            }

            _editorAudioScheduling = _bassAudio != null;
            _editorAudioCursor = LowerBoundEditorAudioNote(
                Math.Max(0.0, startTimeMs));
            _editorAudioScheduledThroughMs =
                startTimeMs - 0.001;
        }

        public void ScheduleEditorAudio(
            double visibleTimeMs,
            double scheduleThroughMs,
            double audioTimeOffsetMs = 0.0)
        {
            if (!_initialized ||
                _bassAudio == null ||
                _paused ||
                _editorAudioNotes.Count == 0)
            {
                return;
            }
            if (!_editorAudioScheduling)
            {
                ResetEditorAudioSchedule(visibleTimeMs);
            }

            var through = Math.Max(
                visibleTimeMs,
                scheduleThroughMs);
            if (through <= _editorAudioScheduledThroughMs)
            {
                return;
            }

            while (_editorAudioCursor < _editorAudioNotes.Count)
            {
                var note = _editorAudioNotes[_editorAudioCursor];
                if (note.TimeMs > through)
                {
                    break;
                }
                _editorAudioCursor++;
                if (_editorScheduledNotes.Contains(note))
                {
                    continue;
                }

                if (ScheduleEditorNoteAudio(note, audioTimeOffsetMs))
                {
                    _editorScheduledNotes.Add(note);
                }
            }

            _editorAudioScheduledThroughMs = through;
        }

        private bool ScheduleEditorNoteAudio(
            ChartNote note,
            double audioTimeOffsetMs)
        {
            if (note == null)
            {
                return false;
            }

            switch (note.Kind)
            {
                case NoteKind.Tap:
                    return PlayOneShot(
                        GameplayAudioRouting.TapClipForLane(
                            note.StartLane,
                            _laneTapClip,
                            _sideTapClip),
                        HitSoundCategory.Tap,
                        note.TimeMs + audioTimeOffsetMs);
                case NoteKind.Hold:
                    return PlayOneShot(
                        GameplayAudioRouting.TapClipForLane(
                            note.StartLane,
                            _laneTapClip,
                            _sideTapClip),
                        HitSoundCategory.Hold,
                        note.TimeMs + audioTimeOffsetMs);
                case NoteKind.Flick:
                    return PlayOneShot(
                        _flickClip,
                        HitSoundCategory.Flick,
                        note.TimeMs + audioTimeOffsetMs);
                case NoteKind.SkyArea:
                    SkyGroup group;
                    if (!_skyGroupByNote.TryGetValue(note, out group) ||
                        group.Scheduled)
                    {
                        return false;
                    }
                    if (!PlayOneShot(
                            _skyStartClip,
                            HitSoundCategory.SkyArea,
                            group.FirstTimeMs + audioTimeOffsetMs))
                    {
                        return false;
                    }
                    group.Scheduled = true;
                    return true;
                default:
                    return false;
            }
        }

        private int LowerBoundEditorAudioNote(double timeMs)
        {
            var low = 0;
            var high = _editorAudioNotes.Count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (_editorAudioNotes[mid].TimeMs < timeMs)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }

        private void HandleNoteStarted(JudgementEvent judgement)
        {
            if (!_initialized ||
                judgement == null ||
                judgement.Note == null ||
                !IsHit(judgement.Result))
            {
                return;
            }

            if (judgement.Note.Kind == NoteKind.Hold)
            {
                if (!_activeHolds.Contains(judgement.Note))
                {
                    _activeHolds.Add(judgement.Note);
                    StartedHoldCount++;
                    if (!IsEditorAudioScheduled(judgement.Note))
                    {
                        EvaluatedTapCount++;
                        PlayOneShot(
                            GameplayAudioRouting.TapClipForLane(
                                judgement.Note.StartLane,
                                _laneTapClip,
                                _sideTapClip),
                            HitSoundCategory.Hold,
                            judgement.Note.TimeMs);
                    }
                }
                ApplyLoopState();
                return;
            }

            if (judgement.Note.Kind != NoteKind.SkyArea)
            {
                return;
            }

            SkyGroup group;
            if (!_skyGroupByNote.TryGetValue(judgement.Note, out group) ||
                group.Started)
            {
                return;
            }

            group.Started = true;
            if (!group.Scheduled)
            {
                PlayOneShot(
                    _skyStartClip,
                    HitSoundCategory.SkyArea,
                    group.FirstTimeMs);
            }
            StartedSkyGroupCount++;
            _activeSkyGroups.Add(group);
            ApplyLoopState();
        }

        private void HandleJudged(JudgementEvent judgement)
        {
            if (!_initialized ||
                judgement == null ||
                judgement.Note == null)
            {
                return;
            }

            var note = judgement.Note;
            if (note.Kind == NoteKind.Hold)
            {
                _activeHolds.Remove(note);
                ApplyLoopState();
                return;
            }

            if (note.Kind == NoteKind.SkyArea || !IsHit(judgement.Result))
            {
                return;
            }

            if (note.Kind == NoteKind.Flick)
            {
                if (!IsEditorAudioScheduled(note))
                {
                    PlayOneShot(
                        _flickClip,
                        HitSoundCategory.Flick,
                        note.TimeMs);
                }
            }
            else if (note.Kind == NoteKind.Tap)
            {
                if (!IsEditorAudioScheduled(note))
                {
                    PlayTapHit(note.TimeMs, note.StartLane);
                }
            }
        }

        private bool IsEditorAudioScheduled(ChartNote note)
        {
            return _editorAudioScheduling &&
                   note != null &&
                   _editorScheduledNotes.Contains(note);
        }

        private void PlayTapHit(double noteTimeMs, int lane)
        {
            EvaluatedTapCount++;
            var clip = GameplayAudioRouting.TapClipForLane(
                lane,
                _laneTapClip,
                _sideTapClip);
            PlayOneShot(
                clip,
                HitSoundCategory.Tap,
                noteTimeMs);
        }

        private bool PlayOneShot(
            AudioClip clip,
            HitSoundCategory category,
            double targetSourceTimeMs = double.NaN)
        {
            if (clip == null)
            {
                return false;
            }
            var gain = OneShotGain * VolumeFor(category);
            if (_bassAudio != null)
            {
                if (!_bassAudio.PlayOneShot(
                        clip,
                        gain,
                        targetSourceTimeMs))
                {
                    return false;
                }
                PlayedOneShotCount++;
                return true;
            }
            if (_oneShotSources == null)
            {
                return false;
            }

            var sourceIndex = AcquireOneShotVoice(AudioSettings.dspTime);
            var source = _oneShotSources[sourceIndex];
            source.Stop();
            source.clip = clip;
            source.loop = false;
            source.volume = gain;
            source.Play();

            var nowDsp = AudioSettings.dspTime;
            _oneShotBusy[sourceIndex] = true;
            _oneShotStartDsp[sourceIndex] = nowDsp;
            _oneShotEndDsp[sourceIndex] =
                nowDsp + Math.Max(0.001, clip.length);
            PlayedOneShotCount++;
            return true;
        }

        private int AcquireOneShotVoice(double nowDsp)
        {
            for (var i = 0; i < OneShotVoiceCapacity; i++)
            {
                if (!_oneShotBusy[i] ||
                    nowDsp >= _oneShotEndDsp[i] - VoiceEndToleranceDsp)
                {
                    return i;
                }
            }

            var stealIndex = 0;
            var earliestEnd = _oneShotEndDsp[0];
            var earliestStart = _oneShotStartDsp[0];
            for (var i = 1; i < OneShotVoiceCapacity; i++)
            {
                var end = _oneShotEndDsp[i];
                if (end < earliestEnd - VoiceEndToleranceDsp ||
                    (Math.Abs(end - earliestEnd) <= VoiceEndToleranceDsp &&
                     _oneShotStartDsp[i] < earliestStart))
                {
                    stealIndex = i;
                    earliestEnd = end;
                    earliestStart = _oneShotStartDsp[i];
                }
            }

            StolenOneShotCount++;
            return stealIndex;
        }

        private void ApplyLoopState()
        {
            if (!_initialized)
            {
                return;
            }

            if (_bassAudio != null)
            {
                var hasGround = _activeHolds.Count > 0;
                var hasSky = _activeSkyGroups.Count > 0;
                _bassAudio.SetLoop(
                    "ground-hold",
                    _groundHoldLoopClip,
                    HoldLoopGain * _holdVolume,
                    !_paused && hasGround);
                _bassAudio.SetLoop(
                    "sky-hold",
                    _skyHoldLoopClip,
                    HoldLoopGain * _skyAreaVolume,
                    !_paused && hasSky);
                return;
            }

            ApplyLoop(
                _groundHoldSource,
                _groundHoldLoopClip,
                _activeHolds.Count > 0,
                HoldLoopGain * _holdVolume);
            ApplyLoop(
                _skyHoldSource,
                _skyHoldLoopClip,
                _activeSkyGroups.Count > 0,
                HoldLoopGain * _skyAreaVolume);
        }

        private void ApplyLoop(
            AudioSource source,
            AudioClip clip,
            bool shouldPlay,
            float gain)
        {
            if (!shouldPlay || clip == null)
            {
                if (source.isPlaying)
                {
                    source.Stop();
                }
                return;
            }

            if (_paused)
            {
                return;
            }

            if (source.clip == clip && source.isPlaying)
            {
                source.volume = gain;
                return;
            }

            source.clip = clip;
            source.loop = true;
            source.volume = gain;
            source.Play();
        }

        private float VolumeFor(HitSoundCategory category)
        {
            switch (category)
            {
                case HitSoundCategory.SkyArea:
                    return _skyAreaVolume;
                case HitSoundCategory.Hold:
                    return _holdVolume;
                case HitSoundCategory.Flick:
                    return _flickVolume;
                default:
                    return _tapVolume;
            }
        }

        private void BuildEditorAudioNotes(Chart chart)
        {
            _editorAudioNotes.Clear();
            if (chart == null)
            {
                return;
            }
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                if (note.Kind == NoteKind.Tap ||
                    note.Kind == NoteKind.Hold ||
                    note.Kind == NoteKind.Flick ||
                    note.Kind == NoteKind.SkyArea)
                {
                    _editorAudioNotes.Add(note);
                }
            }
            _editorAudioNotes.Sort(delegate(ChartNote left, ChartNote right)
            {
                var byTime = left.TimeMs.CompareTo(right.TimeMs);
                return byTime != 0 ? byTime : left.Id.CompareTo(right.Id);
            });
        }

        private void BuildSkyGroups(Chart chart)
        {
            var skyNotes = new List<ChartNote>();
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                if (chart.Notes[i].Kind == NoteKind.SkyArea)
                {
                    skyNotes.Add(chart.Notes[i]);
                }
            }
            skyNotes.Sort(delegate(ChartNote left, ChartNote right)
            {
                var byTime = left.TimeMs.CompareTo(right.TimeMs);
                return byTime != 0 ? byTime : left.Id.CompareTo(right.Id);
            });

            SkyGroup current = null;
            for (var i = 0; i < skyNotes.Count; i++)
            {
                var note = skyNotes[i];
                var contiguous = current != null &&
                                 current.GroupId == note.GroupId &&
                                 Math.Abs(note.TimeMs - current.LastTimeMs) < 2.0;
                if (!contiguous)
                {
                    current = new SkyGroup
                    {
                        GroupId = note.GroupId,
                        FirstTimeMs = note.TimeMs,
                        LastTimeMs = note.EndTimeMs,
                    };
                    _skyGroups.Add(current);
                }

                current.Segments.Add(note);
                current.LastTimeMs = Math.Max(current.LastTimeMs, note.EndTimeMs);
                _skyGroupByNote[note] = current;
            }
        }

        private static bool IsHit(JudgementResult result)
        {
            return result == JudgementResult.Shiny ||
                   result == JudgementResult.PerfectEarly ||
                   result == JudgementResult.PerfectLate ||
                   result == JudgementResult.NearEarly ||
                   result == JudgementResult.NearLate;
        }

        private void OnDestroy()
        {
            if (_engine == null)
            {
                return;
            }
            _engine.NoteStarted -= HandleNoteStarted;
            _engine.Judged -= HandleJudged;
        }
    }
}
