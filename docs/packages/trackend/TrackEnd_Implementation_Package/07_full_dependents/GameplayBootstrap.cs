using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Presentation.Audio;
using InFalsus.Gameplay.Presentation.Backgrounds;
using InFalsus.Gameplay.Presentation.Effects;
using InFalsus.Gameplay.Presentation.Hud;
using InFalsus.Gameplay.Presentation.Notes;
using InFalsus.Gameplay.Presentation.Timing;
using InFalsus.Gameplay.Presentation.Tracks;
using InFalsus.Gameplay.Runtime.SceneBinding;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace InFalsus.Gameplay.Runtime.Bootstrap
{

public sealed class GameplayBootstrap : MonoBehaviour
{
	public const float SongGain = 0.72f;

	[Header("Chart")]
	public string chartFileName = "cryogenic_4.chart.txt";

	public string songTitle = "cryogenic";

	public string difficulty = "4";

	[Header("Audio")]
	public AudioClip songClip;

	public double audioOffsetMs;

	public AudioClip skyAreaStartClip;

	public AudioClip laneTapHitClip;

	public AudioClip sideTapHitClip;

	public AudioClip flickHitClip;

	public AudioClip groundHoldLoopClip;

	public AudioClip skyAreaHoldLoopClip;

	public Texture2D judgementExactPlusTexture;

	public Texture2D judgementExactTexture;

	public Texture2D judgementNearTexture;

	public Texture2D judgementBreakTexture;

	public Material judgementFeedbackMaterial;

	[Header("Presentation")]
	public int speedPref = 500;

	public GameObject skyAreaPrefab;

	public Material skyAreaMaterial;

	public GameObject holdPrefab;

	public GameObject flickLeftPrefab;

	public GameObject flickRightPrefab;

	public GameObject tapPrefab;

	public GameObject tapSpanPrefab;

	public GameObject sideLeftTapPrefab;

	public GameObject sideRightTapPrefab;

	public GameObject sideLeftHoldPrefab;

	public GameObject sideRightHoldPrefab;

	public bool autoPlayForDiagnostics = true;

	private GameplaySceneBinding _binding;

	private ChartParseResult _parseResult;

	private Chart _chart;

	private JudgementEngine _engine;

	private TrackMotion _motion;

	private NoteRenderer _noteRenderer;

	private SideLaneSurfaceScroller _sideLaneScroller;

	private PresentationAnimationClock _presentationClock;

	private LaneStateController _laneState;

	private MeasureLineRenderer _measureLines;

	private GameplayClock _clock;

	private GameplayHitAudio _hitAudio;

	private GameplayTrackOpacityController _trackOpacity;

	private FlickHitEffectRenderer _flickEffects;

	private NoteHitEffectRenderer _noteEffects;

	private AudioSource _audio;

	private GameplayHud _hud;

	private FalsumChartEditor _falsumEditor;

	private FalsumPitchPreservingPlayback _pitchPlayback;

	private FalsumBassAudioEngine _bassAudio;

	private FalsumBackgroundPresenter _backgroundPresenter;

	private FalsumComboDisplay _comboDisplay;

	private string songAudioPath;

	private float _musicVolume = 1f;

	private bool _running;

	private static readonly KeyCode[] LaneKeys;

	public bool EditorModeActive { get; private set; }

	public GameplaySceneBinding Binding => _binding;

	public Chart CurrentChart => _chart;

	public TrackMotion Motion => _motion;

	public double PresentationTimeSeconds =>
		_presentationClock != null
			? _presentationClock.TimeSeconds
			: 0.0;

	public PresentationAnimationClock PresentationClock =>
		_presentationClock;

	public NoteRenderer NoteVisuals => _noteRenderer;

	public GameplayClock Clock => _clock;

	public AudioSource SongAudio => _audio;

	public FalsumBassAudioEngine BassAudio => _bassAudio;

	public FalsumBackgroundPresenter Backgrounds => _backgroundPresenter;

	public GameplayTrackOpacityController TrackOpacity => _trackOpacity;

	public int AutoCombo
	{
		get
		{
			return _comboDisplay != null
				? _comboDisplay.CurrentCombo
				: 0;
		}
	}

	public bool AutoComboVisible
	{
		get
		{
			return _comboDisplay != null &&
			       _comboDisplay.NumberVisible;
		}
	}

	private float EffectiveSongGain => 0.72f * _musicVolume;

	public string DiagnosticSummary
	{
		get
		{
			if (_engine == null)
			{
				return "engine=<null>";
			}
			return "time=" + (((Object)(object)_clock != (Object)null) ? _clock.TimeMs : 0.0).ToString("0.000") + " presentationTime=" + PresentationTimeSeconds.ToString("0.000") + " autoCombo=" + AutoCombo + " autoComboVisible=" + AutoComboVisible + " score=" + _engine.Score + " combo=" + _engine.Combo + " max=" + _engine.MaxCombo + " judged=" + _engine.JudgedCount + " shiny=" + _engine.ShinyCount + " perfect=" + _engine.PerfectCount + " near=" + _engine.NearCount + " break=" + _engine.BreakCount + " holdAudio=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.ActiveHoldCount : 0) + " skyAudio=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.ActiveSkyGroupCount : 0) + " sfx=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.PlayedOneShotCount : 0) + " sfxVoices=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.ActiveOneShotVoiceCount : 0) + "/" + 48 + " sfxStolen=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.StolenOneShotCount : 0) + " taps=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.EvaluatedTapCount : 0) + " tapMerged=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.CoalescedTapCount : 0) + " holdStarts=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.StartedHoldCount : 0) + " skyStarts=" + (((Object)(object)_hitAudio != (Object)null) ? _hitAudio.StartedSkyGroupCount : 0) + " flickFx=" + (((Object)(object)_flickEffects != (Object)null) ? _flickEffects.ActiveBurstCount : 0) + " flickFxPlayed=" + (((Object)(object)_flickEffects != (Object)null) ? _flickEffects.PlayedBurstCount : 0) + " noteFx=" + (((Object)(object)_noteEffects != (Object)null) ? _noteEffects.ActiveEffectCount : 0) + " noteFxPlayed=" + (((Object)(object)_noteEffects != (Object)null) ? _noteEffects.PlayedNoteCount : 0) + " holdFx=" + (((Object)(object)_noteEffects != (Object)null) ? _noteEffects.ActiveHoldEffectCount : 0) + " skyActive=" + ((_noteRenderer != null) ? _noteRenderer.SkyActiveCount : 0) + "/" + ((_noteRenderer != null) ? _noteRenderer.SkyTotalCount : 0) + " sideScroll=" + ((_sideLaneScroller != null) ? _sideLaneScroller.CurrentOffset : 0f) + " sideSurfaces=" + ((_sideLaneScroller != null) ? _sideLaneScroller.SurfaceCount : 0) + " disabledLanes=" + ((_laneState != null) ? _laneState.DisabledSurfaceCount : 0) + " measureLines=" + ((_measureLines != null) ? _measureLines.VisibleLineCount : 0) + " background=" + (((Object)(object)_backgroundPresenter != (Object)null) ? _backgroundPresenter.CurrentBackgroundFile : "<null>") + " backgroundVisible=" + ((Object)(object)_backgroundPresenter != (Object)null && _backgroundPresenter.BackgroundVisible) + " playfieldVisible=" + ((Object)(object)_backgroundPresenter != (Object)null && _backgroundPresenter.PlayfieldVisible) + " trackOpacity=" + (((Object)(object)_trackOpacity != (Object)null) ? _trackOpacity.TrackOpacity : 0f) + " trackSurfaces=" + (((Object)(object)_trackOpacity != (Object)null) ? _trackOpacity.SurfaceCount : 0) + " backgroundError=" + (((Object)(object)_backgroundPresenter != (Object)null && !string.IsNullOrWhiteSpace(_backgroundPresenter.LastError)) ? _backgroundPresenter.LastError : "-");
		}
	}

	public int SkyAreaGroupCount
	{
		get
		{
			if (_noteRenderer == null)
			{
				return 0;
			}
			return _noteRenderer.SkyTotalCount;
		}
	}

	public double CurrentTimeMs
	{
		get
		{
			if (!((Object)(object)_clock != (Object)null))
			{
				return 0.0;
			}
			return _clock.TimeMs;
		}
	}

	public void ApplyAudioPreferences(FalsumAudioPreferences preferences)
	{
		if (preferences != null)
		{
			_musicVolume = Mathf.Clamp01(preferences.MusicVolume);
			if ((Object)(object)_bassAudio != (Object)null)
			{
				_bassAudio.SetSongGain(EffectiveSongGain);
			}
			else if ((Object)(object)_audio != (Object)null)
			{
				_audio.volume = EffectiveSongGain;
			}
			if ((Object)(object)_hitAudio != (Object)null)
			{
				_hitAudio.SetVolumes(preferences.SkyAreaVolume, preferences.TapVolume, preferences.HoldVolume, preferences.FlickVolume);
			}
		}
	}

	private void Start()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ef: Unknown result type (might be due to invalid IL or missing references)
		Stopwatch startupWatch = Stopwatch.StartNew();
		FalsumWindowPlacement.CenterMainWindow();
		Scene scene = SceneManager.GetActiveScene();
		_binding = GameplaySceneBinding.Resolve(scene);
		_binding.ApplyPhaseAPolicy();
		EnsureCamera();
		EnsureAudioLimiter();
		long sceneBindingMs = startupWatch.ElapsedMilliseconds;
		_backgroundPresenter = ((Component)this).gameObject.AddComponent<FalsumBackgroundPresenter>();
		_backgroundPresenter.Initialize(_binding);
		_trackOpacity = ((Component)this).gameObject.AddComponent<GameplayTrackOpacityController>();
		_trackOpacity.Initialize(_binding);
		long presentationMs = startupWatch.ElapsedMilliseconds;
		_chart = LoadChart();
		_comboDisplay = new FalsumComboDisplay(_binding);
		_comboDisplay.Initialize();
		_engine = new JudgementEngine(_chart);
		_motion = new TrackMotion(_chart, (double)speedPref * 0.01);
		long chartMs = startupWatch.ElapsedMilliseconds;
		_audio = ((Component)this).gameObject.GetComponent<AudioSource>();
		if ((Object)(object)_audio == (Object)null)
		{
			_audio = ((Component)this).gameObject.AddComponent<AudioSource>();
		}
		if (string.IsNullOrEmpty(songAudioPath))
		{
			string defaultAudioPath = Path.Combine(Application.dataPath, "InFalsus", "Gameplay", "Audio", "Resources", "alamode.ogg");
			if (File.Exists(defaultAudioPath))
			{
				songAudioPath = defaultAudioPath;
			}
		}
		_audio.clip = songClip;
		_audio.playOnAwake = false;
		_audio.loop = false;
		_audio.spatialBlend = 0f;
		_audio.volume = EffectiveSongGain;
		_audio.priority = 0;
		_audio.Stop();
		_clock = ((Component)this).gameObject.AddComponent<GameplayClock>();
		_clock.Initialize(_audio, audioOffsetMs);
		long audioMs = startupWatch.ElapsedMilliseconds;
		_hitAudio = ((Component)this).gameObject.AddComponent<GameplayHitAudio>();
		_hitAudio.Initialize(_chart, _engine, LoadRequiredClip(skyAreaStartClip, "58_note_hit_skylane_draft1"), LoadRequiredClip(laneTapHitClip, "59_note_hit_3_draft2"), LoadRequiredClip(sideTapHitClip, "59_note_side_hit_draft2"), LoadRequiredClip(flickHitClip, "60_note_flick_5_draft2"), LoadRequiredClip(groundHoldLoopClip, "58_note_hold_floor+sides_draft1"), LoadRequiredClip(skyAreaHoldLoopClip, "58_note_hold_skylane_5"), _bassAudio);
		long hitAudioMs = startupWatch.ElapsedMilliseconds;
		_noteRenderer = new NoteRenderer(_binding, _motion, _chart, skyAreaPrefab, skyAreaMaterial, holdPrefab, flickLeftPrefab, flickRightPrefab, tapPrefab, tapSpanPrefab, sideLeftTapPrefab, sideRightTapPrefab, sideLeftHoldPrefab, sideRightHoldPrefab);
		SkyJudgementLineEffect skyLineEffect =
			((Component)this).gameObject.AddComponent<SkyJudgementLineEffect>();
		skyLineEffect.Initialize(_binding);
		long noteRendererMs = startupWatch.ElapsedMilliseconds;
		_flickEffects = ((Component)this).gameObject.AddComponent<FlickHitEffectRenderer>();
		_flickEffects.Initialize(_engine, _binding, judgementExactPlusTexture, judgementExactTexture, judgementNearTexture, judgementBreakTexture, judgementFeedbackMaterial);
		_noteEffects = ((Component)this).gameObject.AddComponent<NoteHitEffectRenderer>();
		_noteEffects.Initialize(_engine, _binding);
		_sideLaneScroller = new SideLaneSurfaceScroller(_binding, _motion);
		_presentationClock = new PresentationAnimationClock();
		_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(0.0);
		_laneState = new LaneStateController(_binding, _chart, _motion);
		_measureLines = new MeasureLineRenderer(_binding, _chart, _motion);
		HidePreexistingNoteEffects();
		long effectsMs = startupWatch.ElapsedMilliseconds;
		_running = true;
		_falsumEditor = ((Component)this).gameObject.AddComponent<FalsumChartEditor>();
		_falsumEditor.Initialize(this);
		if (autoPlayForDiagnostics &&
			Environment.GetEnvironmentVariable(
				"INFALSUS_CAPTURE_PRESERVE_CHART") == "1")
		{
			Restart();
		}
		if ((Object)(object)_backgroundPresenter != (Object)null && !_backgroundPresenter.BackgroundTextureReady)
		{
			_backgroundPresenter.SetBackground(string.Empty);
		}
		long editorMs = startupWatch.ElapsedMilliseconds;
		((MonoBehaviour)this).StartCoroutine(InitializeAudioDeferred());
		if (Array.IndexOf(Environment.GetCommandLineArgs(), "-falsum-smoketest") >= 0)
		{
			((Component)this).gameObject.AddComponent<FalsumSmokeTest>();
		}
		Debug.Log((object)("[Falsum] Startup timings: scene=" + sceneBindingMs + "ms presentation=" + (presentationMs - sceneBindingMs) + "ms chart=" + (chartMs - presentationMs) + "ms audio=" + (audioMs - chartMs) + "ms hitAudio=" + (hitAudioMs - audioMs) + "ms notes=" + (noteRendererMs - hitAudioMs) + "ms effects=" + (effectsMs - noteRendererMs) + "ms editor=" + (editorMs - effectsMs) + "ms total=" + editorMs + "ms"));
		Debug.Log((object)("[InFalsus] Gameplay started: " + chartFileName + " notes=" + _chart.Notes.Count + " bpm=" + _chart.Bpm + " audio=" + (((Object)(object)songClip != (Object)null) ? ((Object)songClip).name : "<none>") + " speed=" + ((double)speedPref * 0.01).ToString("0.##") + " camera=" + ((object)((Component)Camera.main).transform.position/*cast due to constrained. prefix*/).ToString()));
	}

	private IEnumerator InitializeAudioDeferred()
	{
		yield return null;
		yield return (object)new WaitForSecondsRealtime(0.15f);
		if ((Object)(object)songClip == (Object)null && (Object)(object)_audio != (Object)null && (Object)(object)_audio.clip == (Object)null)
		{
			ResourceRequest request = Resources.LoadAsync<AudioClip>("alamode");
			yield return request;
			GameplayBootstrap gameplayBootstrap = this;
			Object asset = request.asset;
			gameplayBootstrap.songClip = (AudioClip)(object)((asset is AudioClip) ? asset : null);
		}
		if ((Object)(object)_audio != (Object)null && (Object)(object)_audio.clip == (Object)null && (Object)(object)songClip != (Object)null)
		{
			_audio.clip = songClip;
		}
		TryInitializeBassAudio();
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.SetBassAudio(_bassAudio);
			if ((Object)(object)_bassAudio != (Object)null)
			{
				yield return _hitAudio.PreloadBassClips();
			}
		}
	}

	private void Update()
	{
		UpdatePresentationAnimation();
		if (!_running || (Object)(object)_clock == (Object)null || EditorModeActive)
		{
			return;
		}
		if (Input.GetKeyDown((KeyCode)112))
		{
			if (_clock.IsPlaying)
			{
				_clock.Pause();
				if ((Object)(object)_hitAudio != (Object)null)
				{
					_hitAudio.SetPaused(paused: true);
				}
			}
			else
			{
				_clock.Resume();
				if ((Object)(object)_hitAudio != (Object)null)
				{
					_hitAudio.SetPaused(paused: false);
				}
			}
		}
		if (Input.GetKeyDown((KeyCode)114))
		{
			Restart();
			return;
		}
		if (_clock.IsPlaying)
		{
			double now = _clock.TimeMs;
			if (autoPlayForDiagnostics)
			{
				AutoPlayStep(now);
			}
			else
			{
				ReadInput(now);
			}
			_engine.Update(now);
			if ((Object)(object)_hitAudio != (Object)null)
			{
				_hitAudio.Tick(now);
			}
			_noteRenderer.Update(now);
			_sideLaneScroller.Update(now);
			_laneState.Update(now);
			_measureLines.Update(now);
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.Tick(_clock.TimeMs, !_clock.IsPlaying);
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
			_noteEffects.Tick(_clock.TimeMs, !_clock.IsPlaying);
		}
	}

	private void UpdatePresentationAnimation()
	{
		if (_presentationClock == null)
		{
			return;
		}

		var running =
			_running &&
			_clock != null &&
			_clock.IsPlaying;
		_presentationClock.Tick(
			running,
			Time.unscaledDeltaTime);
		var seconds = _presentationClock.TimeSeconds;
		_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(seconds);
	}

	private void LateUpdate()
	{
		if (EditorModeActive && !((Object)(object)_clock == (Object)null) && !_clock.IsPlaying)
		{
			if ((Object)(object)_audio != (Object)null && _audio.isPlaying)
			{
				_audio.Pause();
			}
			if ((Object)(object)_hitAudio != (Object)null)
			{
				_hitAudio.SetPaused(paused: true);
			}
		}
	}

	private void OnDestroy()
	{
		_presentationClock = null;
		if (_comboDisplay != null)
		{
			_comboDisplay.Dispose();
			_comboDisplay = null;
		}
	}

	public void SetEditorMode(bool active)
	{
		EditorModeActive = active;
		if ((Object)(object)_hud != (Object)null)
		{
			((Behaviour)_hud).enabled = !active;
		}
		if ((Object)(object)_clock != (Object)null && active)
		{
			_clock.Pause();
			if ((Object)(object)_audio != (Object)null)
			{
				_audio.Stop();
			}
		}
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.SetPaused(active);
		}
	}

	public void RebuildChartRuntime()
	{
		_chart.RebuildTimeline();
		for (int i = 0; i < _chart.Notes.Count; i++)
		{
			ChartNote chartNote = _chart.Notes[i];
			chartNote.Consumed = false;
			chartNote.Judged = false;
			chartNote.Holding = false;
			chartNote.Result = JudgementResult.None;
			chartNote.HoldStartResult = JudgementResult.None;
			chartNote.Id = i;
		}
		_engine = new JudgementEngine(_chart);
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.Rebuild(_chart, _engine);
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.SetEngine(_engine);
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
			_noteEffects.SetEngine(_engine);
		}
		if ((Object)(object)_hud != (Object)null)
		{
			_hud.SetRuntime(_engine, _chart, songTitle, difficulty, (double)speedPref * 0.01);
		}
		if (_noteRenderer != null)
		{
			_noteRenderer.Rebuild();
		}
		if (_motion != null)
		{
			_motion.Speed = (double)speedPref * 0.01;
		}
	}

	public void RefreshComboAt(double timeMs)
	{
		if (_comboDisplay != null)
		{
			_comboDisplay.Update(Math.Max(0.0, timeMs), _chart);
		}
	}

	public void RefreshEditorAt(double timeMs)
	{
		if ((Object)(object)_clock != (Object)null)
		{
			_clock.Seek(Math.Max(0.0, timeMs));
		}
		RefreshPresentationAt(timeMs);
	}

	public void UpdateChartPlaybackFrame(double timeMs)
	{
		double now = Math.Max(0.0, timeMs);
		if ((Object)(object)_clock != (Object)null && _clock.IsPlaying && _engine != null)
		{
			if ((Object)(object)_hitAudio != (Object)null)
			{
				_hitAudio.ScheduleEditorAudio(now, _clock.AudioScheduleTimeMs, 0.0 - _clock.AudioOffsetMs);
			}
			AutoPlayStep(now);
			_engine.Update(now);
			if ((Object)(object)_hitAudio != (Object)null)
			{
				_hitAudio.SetPaused(paused: false);
				_hitAudio.Tick(now);
			}
			RefreshPresentationAt(now);
			if ((Object)(object)_flickEffects != (Object)null)
			{
				_flickEffects.Tick(now, paused: false);
			}
			if ((Object)(object)_noteEffects != (Object)null)
			{
				_noteEffects.Tick(now, paused: false);
			}
		}
		else
		{
			RefreshPresentationAt(now);
		}
	}

	public void RefreshSkyPresentation(double chartTimeMs)
	{
		// RebuildSkyAreas only repopulates the chain list; the Roots and
		// meshes are recreated by the next presentation refresh. The editor
		// flushes the sky grouping after its own refresh, so it asks for one
		// explicit refresh here. Without it the rebuilt chains stay
		// invisible until the next seek/mouse move.
		RefreshPresentationAt(Math.Max(0.0, chartTimeMs));
	}

	public void PrepareChartPlaybackState(double timeMs)
	{
		ResetAutoplayStateAt(timeMs);
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.ResetEditorAudioSchedule(timeMs);
		}
	}

	public void BeginChartPlayback(double timeMs)
	{
		ResetAutoplayStateAt(timeMs);
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.ResetEditorAudioSchedule(timeMs);
			_hitAudio.SetPaused(paused: false);
		}
	}

	public void EndChartPlayback()
	{
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.SetPaused(paused: true);
			_hitAudio.ResetAudio();
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.ResetVisuals();
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
			_noteEffects.ResetVisuals();
		}
		if ((Object)(object)_audio != (Object)null && _audio.isPlaying)
		{
			_audio.Pause();
		}
	}

	public bool PlayPreviewTapCue()
	{
		if ((Object)(object)_hitAudio != (Object)null)
		{
			return _hitAudio.PlayTapPreviewCue();
		}
		return false;
	}

	public void StopPreviewTapCues()
	{
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.StopPreviewCues();
		}
	}

	/// <summary>
	/// The judgement clips the hit-audio component actually resolved at startup.
	/// The serialized clip fields above can still be null here because they are
	/// filled by LoadRequiredClip from Resources; consumers that need the real
	/// clip (the audio preprocess audition) must use these.
	/// Returns false when the component has not been created yet.
	/// </summary>
	public bool TryGetTapClips(out AudioClip laneTap, out AudioClip sideTap)
	{
		laneTap = null;
		sideTap = null;
		if ((Object)(object)_hitAudio == (Object)null)
		{
			return false;
		}
		laneTap = _hitAudio.LaneTapClip;
		sideTap = _hitAudio.SideTapClip;
		return (Object)(object)laneTap != (Object)null || (Object)(object)sideTap != (Object)null;
	}

	private void ResetAutoplayStateAt(double timeMs)
	{
		double now = Math.Max(0.0, timeMs);
		_engine = new JudgementEngine(_chart);
		_engine.SkipNotesStartedBefore(now);
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.Rebuild(_chart, _engine);
			_hitAudio.SetPaused(paused: true);
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.SetEngine(_engine);
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
			_noteEffects.SetEngine(_engine);
		}
	}

	public void SetFlowSpeed(double speed)
	{
		speed = Math.Max(1.0, Math.Min(10.0, speed));
		speedPref = Mathf.RoundToInt((float)(speed * 100.0));
		if (_motion != null)
		{
			_motion.Speed = speed;
		}
		if ((Object)(object)_hud != (Object)null)
		{
			_hud.SetRuntime(_engine, _chart, songTitle, difficulty, speed);
		}
	}

	public void SetPlaybackRate(double rate)
	{
		if ((Object)(object)_clock != (Object)null)
		{
			_clock.SetPlaybackRate(rate);
			if (!_clock.IsPlaying && (Object)(object)_audio != (Object)null)
			{
				_audio.Stop();
			}
			if ((Object)(object)_hitAudio != (Object)null)
			{
				_hitAudio.ResetEditorAudioSchedule(_clock.TimeMs);
			}
		}
	}

	private void TryInitializeBassAudio()
	{
		_bassAudio = ((Component)this).gameObject.AddComponent<FalsumBassAudioEngine>();
		if (!_bassAudio.TryInitialize(_audio, EffectiveSongGain, out var error))
		{
			Debug.LogWarning((object)("[Falsum] BASS backend unavailable, using managed fallback. " + error));
			((Behaviour)_bassAudio).enabled = false;
			Object.Destroy((Object)(object)_bassAudio);
			_bassAudio = null;
		}
		else if (!_bassAudio.PrepareBgm(songClip, songAudioPath, EffectiveSongGain, out error))
		{
			Debug.LogWarning((object)("[Falsum] BASS BGM preparation failed, using managed fallback. " + error));
			((Behaviour)_bassAudio).enabled = false;
			Object.Destroy((Object)(object)_bassAudio);
			_bassAudio = null;
		}
		if ((Object)(object)_bassAudio != (Object)null)
		{
			_audio.loop = true;
			_audio.volume = 1f;
			_clock.SetAudioSink(_bassAudio);
			return;
		}
		_pitchPlayback = ((Component)this).gameObject.AddComponent<FalsumPitchPreservingPlayback>();
		_pitchPlayback.Initialize(_audio);
		_audio.loop = true;
		_audio.volume = EffectiveSongGain;
		_clock.SetAudioSink(_pitchPlayback);
		((MonoBehaviour)this).StartCoroutine(_pitchPlayback.Prepare(songClip, 0.0));
	}

	public void SetSongClip(AudioClip clip, string title, string difficultyLabel, string audioPath = null)
	{
		if ((Object)(object)clip == (Object)null)
		{
			return;
		}
		songClip = clip;
		songAudioPath = audioPath;
		songTitle = title ?? songTitle;
		difficulty = difficultyLabel ?? difficulty;
		if ((Object)(object)_audio != (Object)null)
		{
			_audio.Stop();
			_audio.clip = clip;
			_audio.loop = (Object)(object)_bassAudio != (Object)null;
			_audio.volume = (((Object)(object)_bassAudio != (Object)null) ? 1f : EffectiveSongGain);
		}
		if ((Object)(object)_clock != (Object)null)
		{
			_clock.Stop();
			_clock.Seek(0.0);
			_clock.Pause();
		}
		if ((Object)(object)_bassAudio != (Object)null)
		{
			if (!_bassAudio.PrepareBgm(clip, songAudioPath, EffectiveSongGain, out var error))
			{
				Debug.LogError((object)("[Falsum] BASS BGM replacement failed: " + error));
			}
		}
		else if ((Object)(object)_pitchPlayback != (Object)null)
		{
			((MonoBehaviour)this).StartCoroutine(_pitchPlayback.Prepare(clip, 0.0));
		}
		if ((Object)(object)_hud != (Object)null)
		{
			_hud.SetRuntime(_engine, _chart, songTitle, difficulty, (double)speedPref * 0.01);
		}
	}

	public void SetGameplayBackground(string backgroundFile)
	{
		if ((Object)(object)_backgroundPresenter != (Object)null)
		{
			_backgroundPresenter.SetBackground(backgroundFile);
		}
	}

	public void SetTrackOpacity(float opacity)
	{
		if ((Object)(object)_trackOpacity != (Object)null)
		{
			_trackOpacity.SetTrackOpacity(opacity);
		}
	}

	public void FadeTrackOpacity(float opacity, float durationSeconds)
	{
		if ((Object)(object)_trackOpacity != (Object)null)
		{
			_trackOpacity.FadeTrackOpacity(opacity, durationSeconds);
		}
	}

	public void SetLaneOpacity(int lane, float opacity)
	{
		if ((Object)(object)_trackOpacity != (Object)null)
		{
			_trackOpacity.SetLaneOpacity(lane, opacity);
		}
	}

	public void FadeLaneOpacity(int lane, float opacity, float durationSeconds)
	{
		if ((Object)(object)_trackOpacity != (Object)null)
		{
			_trackOpacity.FadeLaneOpacity(lane, opacity, durationSeconds);
		}
	}

	/// <summary>
	/// Applies the user's scene-surface opacity preferences: the floor /
	/// side-lane alpha channels of the track and the SkyArea fill / glow.
	///
	/// The track channels are pushed straight into the material property
	/// blocks. The SkyArea values are stored on <see cref="NoteRenderer"/> and
	/// consumed by its next <code>UpdateSky</code>, so this refreshes the
	/// presentation once; otherwise, while the editor is paused, the new
	/// SkyArea alpha would only appear after the next seek or hover (the same
	/// stale-frame problem the sky rebuild path documents).
	/// </summary>
	public void ApplyVisualPreferences(FalsumVisualPreferences preferences)
	{
		if (preferences == null)
		{
			return;
		}
		SetSurfaceOpacityScales(
			preferences.FloorLaneOpacity,
			preferences.SideLaneOpacity);
		if (_noteRenderer != null)
		{
			_noteRenderer.SetSkyAreaAppearance(
				preferences.SkyAreaOpacity,
				preferences.SkyAreaGlow);
			RefreshPresentationAt(
				(Object)(object)_clock != (Object)null
					? _clock.TimeMs
					: 0.0);
		}
	}

	public void SetSurfaceOpacityScales(float floorOpacity, float sideOpacity)
	{
		if ((Object)(object)_trackOpacity != (Object)null)
		{
			_trackOpacity.SetSurfaceOpacityScales(floorOpacity, sideOpacity);
		}
	}

	public void RefreshChartAfterEdit()
	{
		RebuildChartRuntime();
		RefreshEditorAt(((Object)(object)_clock != (Object)null) ? _clock.TimeMs : 0.0);
	}

	private void RefreshPresentationAt(double timeMs)
	{
		if (_comboDisplay != null)
		{
			_comboDisplay.Update(timeMs, _chart);
		}
		if (_noteRenderer != null)
		{
			_noteRenderer.Update(Math.Max(0.0, timeMs));
		}
		if (_sideLaneScroller != null)
		{
			_sideLaneScroller.Update(Math.Max(0.0, timeMs));
		}
		if (_laneState != null)
		{
			_laneState.Update(Math.Max(0.0, timeMs));
		}
		if (_measureLines != null)
		{
			_measureLines.Update(Math.Max(0.0, timeMs));
		}
	}

	private void AutoPlayStep(double nowMs)
	{
		for (int i = 0; i < _chart.Notes.Count; i++)
		{
			ChartNote note = _chart.Notes[i];
			if (note.Judged)
			{
				continue;
			}
			if (note.Kind == NoteKind.Tap || note.Kind == NoteKind.Hold)
			{
				if (note.Consumed)
				{
					if (note.Kind == NoteKind.Hold && note.Holding && nowMs >= note.EndTimeMs)
					{
						_engine.ReleaseLane(note.StartLane, note.EndTimeMs);
					}
				}
				else if (nowMs >= note.TimeMs)
				{
					_engine.PressLane(note.StartLane, note.TimeMs);
					if (note.Kind == NoteKind.Tap)
					{
						_engine.ReleaseLane(note.StartLane, note.TimeMs);
					}
				}
			}
			else if (note.Kind == NoteKind.Flick && nowMs >= note.TimeMs)
			{
				_engine.PressFlick(note.FlickDirection, note.TimeMs);
			}
			else if (note.Kind == NoteKind.SkyArea && nowMs >= note.TimeMs)
			{
				_engine.PressSky(note.TimeMs);
			}
		}
	}

	private void ReadInput(double nowMs)
	{
		for (int lane = 0; lane < LaneKeys.Length; lane++)
		{
			if (Input.GetKeyDown(LaneKeys[lane]))
			{
				_engine.PressLane(lane, nowMs);
			}
			if (Input.GetKeyUp(LaneKeys[lane]))
			{
				_engine.ReleaseLane(lane, nowMs);
			}
		}
		if (Input.GetKeyDown((KeyCode)276))
		{
			_engine.PressFlick(16, nowMs);
		}
		if (Input.GetKeyDown((KeyCode)275))
		{
			_engine.PressFlick(4, nowMs);
		}
		if (Input.GetKeyDown((KeyCode)273))
		{
			_engine.PressSky(nowMs);
		}
	}

	private void Restart()
	{
		_engine.Reset();
		_noteRenderer.ResetVisuals();
		if ((Object)(object)_hitAudio != (Object)null)
		{
			_hitAudio.ResetAudio();
		}
		if ((Object)(object)_flickEffects != (Object)null)
		{
			_flickEffects.ResetVisuals();
		}
		if ((Object)(object)_noteEffects != (Object)null)
		{
			_noteEffects.ResetVisuals();
		}
		_clock.StartAt(0.0);
		if (_sideLaneScroller != null)
		{
			_sideLaneScroller.Update(0.0);
		}
		_presentationClock?.Reset();
		_backgroundPresenter?.TrackEndComposer?.SetPresentationTime(0.0);
		if (_laneState != null)
		{
			_laneState.Update(0.0);
		}
		if (_measureLines != null)
		{
			_measureLines.Update(0.0);
		}
		Debug.Log((object)"[InFalsus] Gameplay restarted.");
	}

	private static AudioClip LoadRequiredClip(AudioClip clip, string resourceName)
	{
		if ((Object)(object)clip != (Object)null)
		{
			return clip;
		}
		clip = Resources.Load<AudioClip>(resourceName);
		if ((Object)(object)clip == (Object)null)
		{
			throw new InvalidOperationException("Gameplay audio clip is missing: " + resourceName);
		}
		return clip;
	}

	private void HidePreexistingNoteEffects()
	{
		Transform root = _binding.NotesContainer;
		for (int i = root.childCount - 1; i >= 0; i--)
		{
			Transform child = root.GetChild(i);
			if (!((Object)child).name.StartsWith("PhaseA_Note_", StringComparison.Ordinal))
			{
				((Component)child).gameObject.SetActive(false);
			}
		}
	}

	private void EnsureCamera()
	{
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		Camera[] cameras = Object.FindObjectsByType<Camera>((FindObjectsInactive)0, (FindObjectsSortMode)0);
		Camera camera = ((cameras.Length != 0) ? cameras[0] : null);
		if ((Object)(object)camera == (Object)null)
		{
			camera = Camera.main;
		}
		if ((Object)(object)camera == (Object)null)
		{
			camera = new GameObject("CameraPerspective")
			{
				tag = "MainCamera"
			}.AddComponent<Camera>();
			((Component)camera).transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
			Debug.Log((object)"[InFalsus] Created CameraPerspective equivalent.");
		}
		camera.orthographic = false;
		camera.fieldOfView = 60f;
		camera.nearClipPlane = 0.05f;
		camera.farClipPlane = 120f;
		camera.cullingMask = 32247;
		camera.clearFlags = (CameraClearFlags)2;
		camera.backgroundColor = Color.black;
		camera.allowHDR = true;
		if ((Object)(object)Object.FindFirstObjectByType<AudioListener>() == (Object)null)
		{
			((Component)camera).gameObject.AddComponent<AudioListener>();
		}
		UniversalAdditionalCameraData data = ((Component)camera).GetComponent<UniversalAdditionalCameraData>();
		if ((Object)(object)data == (Object)null)
		{
			data = ((Component)camera).gameObject.AddComponent<UniversalAdditionalCameraData>();
		}
		data.renderPostProcessing = true;
	}

	private static void EnsureAudioLimiter()
	{
		AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
		if (!((Object)(object)listener == (Object)null) && !((Object)(object)((Component)listener).GetComponent<GameplayMasterLimiter>() != (Object)null))
		{
			((Component)listener).gameObject.AddComponent<GameplayMasterLimiter>();
		}
	}

	private Chart LoadChart()
	{
		string path = ResolveChartPath();
		bool diagnosticCapture =
			Environment.GetEnvironmentVariable(
				"INFALSUS_CAPTURE_PRESERVE_CHART") == "1";
		if (diagnosticCapture)
		{
			Debug.Log("[Falsus] Loading capture chart: " + path);
		}
		if (!File.Exists(path))
		{
			// FALSUM ships no sample chart: the editor is the product and it
			// opens an empty timeline that the author fills in (File > New).
			// Boot used to REQUIRE the sample chart, which made a build without
			// Assets/StreamingAssets/Charts fail during Awake -- long before
			// the editor could offer "New". Fall back to an empty chart with
			// the defaults Chart itself declares (140 BPM, 4/4, no delay) so
			// every downstream consumer still receives a valid, non-null chart.
			Debug.Log(
				"[Falsus] No start-up chart at '" + path +
				"'; starting with an empty chart.");
			_chartFileNameAtStartupMissing = true;
			// ChartParseResult.Success is derived from an empty diagnostics
			// list, so a result that simply carries a chart IS a success.
			_parseResult = new ChartParseResult
			{
				Chart = new Chart(),
			};
			return _parseResult.Chart;
		}
		_chartFileNameAtStartupMissing = false;
		chartFileName = Path.GetFileName(path);
		_parseResult = ChartParser.ParseFile(path);
		if (_parseResult.Success && _parseResult.Chart != null)
		{
			FalsumSkyAreaGrouping.Normalize(_parseResult.Chart);
		}
		if (diagnosticCapture)
		{
			Debug.Log("[Falsus] Parsed capture chart: success=" +
				_parseResult.Success + " notes=" +
				(_parseResult.Chart != null
					? _parseResult.Chart.Notes.Count
					: -1) + " bpm=" +
				(_parseResult.Chart != null
					? _parseResult.Chart.Bpm
					: -1.0));
		}
		if (!_parseResult.Success)
		{
			foreach (ChartParseDiagnostic diagnostic2 in _parseResult.Diagnostics)
			{
				Debug.LogError((object)("[InFalsus] Chart parse: " + diagnostic2));
			}
			throw new InvalidOperationException("Chart parsing failed: " + chartFileName);
		}
		return _parseResult.Chart;
	}

	/// <summary>
	/// True when boot found no start-up chart and substituted an empty one.
	/// The editor uses this to skip anything that only makes sense for a real
	/// chart (measure-number profiling, the legacy-delay prompt) instead of
	/// loading a package the user never asked for.
	/// </summary>
	public bool ChartFileNameAtStartupMissing
	{
		get { return _chartFileNameAtStartupMissing; }
	}

	private bool _chartFileNameAtStartupMissing;

	private string ResolveChartPath()
	{
		string overridePath = Environment.GetEnvironmentVariable("INFALSUS_CHART_PATH");
		if (!string.IsNullOrWhiteSpace(overridePath))
		{
			if (Path.IsPathRooted(overridePath))
			{
				return overridePath;
			}
			return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), overridePath));
		}
		string path = Path.Combine(Application.streamingAssetsPath, "Charts", chartFileName);
		if (File.Exists(path))
		{
			return path;
		}
		return Path.Combine(Application.dataPath, "StreamingAssets", "Charts", chartFileName);
	}

	static GameplayBootstrap()
	{
		LaneKeys = new KeyCode[]
		{
			KeyCode.LeftShift,
			KeyCode.A,
			KeyCode.S,
			KeyCode.D,
			KeyCode.F,
			KeyCode.Space,
		};
	}
}
}
