using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Presentation.Audio;
using InFalsus.Gameplay.Presentation.Notes;
using InFalsus.Gameplay.Presentation.Tracks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsus.EditorTools
{

public static class GameplaySelfCheck
{
	private const string ChartDirectory = "Assets/StreamingAssets/Charts";

	private static readonly CultureInfo Invariant =
		CultureInfo.InvariantCulture;

	[MenuItem("InFalsus/Gameplay/Run Phase A Self Check")]
	public static void Run()
	{
		GameplaySceneBuilder.ReportOnly();
		ValidateJudgementBoundaries();
		ValidateTrackTravel();
		ValidateLaneStateRendering();
		ValidateChartTimingFeatures();
		ValidateFlickParsing();
		ValidateJudgementEngine();
		ValidateGameplayAudio();
		ValidateGameplayAudioMix();
		ValidateHoldStartHitAudio();
		ValidateOneShotVoiceStealing();
		ValidateFlickEffects();
		ValidateNoteEffects();
		WriteJudgementMatrix();
		WriteChartReport();
	}

	private static void ValidateFlickEffects()
	{
		RuntimeAnimatorController val = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorOverrideController/FlickBurst_0 R.overrideController");
		RuntimeAnimatorController leftController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorController/FlickBurst_0.controller");
		Texture2D exact = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/InFalsus/Recovered/Texture2D/judgement-02-Exact.png");
		Texture2D exactPlus = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/InFalsus/Recovered/Texture2D/judgement-01-ExactPlus.png");
		Sprite rightFrame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/InFalsus/Recovered/Sprite/FlickBurstR_0.asset");
		Sprite leftFrame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/InFalsus/Recovered/Sprite/FlickBurst_0.asset");
		if ((Object)(object)val == (Object)null || (Object)(object)leftController == (Object)null || (Object)(object)exact == (Object)null || (Object)(object)exactPlus == (Object)null || (Object)(object)rightFrame == (Object)null || (Object)(object)leftFrame == (Object)null)
		{
			throw new InvalidOperationException("Flick hit-effect assets are incomplete.");
		}
		AnimationClip[] rightClips = val.animationClips;
		AnimationClip[] leftClips = leftController.animationClips;
		if (rightClips.Length == 0 || leftClips.Length == 0 || rightClips[0].length < 0.5f || leftClips[0].length < 0.5f)
		{
			throw new InvalidOperationException("FlickBurst animation clips are missing or too short.");
		}
		Debug.Log((object)("[InFalsus] Flick effects passed: right=" + rightClips[0].length.ToString("0.000") + "s left=" + leftClips[0].length.ToString("0.000") + "s"));
	}

	private static void ValidateNoteEffects()
	{
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		RuntimeAnimatorController val = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorController/NoteFX_Radial_0.controller");
		RuntimeAnimatorController hold = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorController/NoteHoldFXV2_0.controller");
		RuntimeAnimatorController main = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/InFalsus/Recovered/AnimatorController/NoteFX_Main_0.controller");
		AnimationClip mainLoop = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/InFalsus/Recovered/AnimationClip/NoteFX_MainHoldLoop.anim");
		if ((Object)(object)val == (Object)null || (Object)(object)hold == (Object)null || (Object)(object)main == (Object)null || (Object)(object)mainLoop == (Object)null || !((Motion)mainLoop).isLooping)
		{
			throw new InvalidOperationException("Note hit-effect assets are incomplete.");
		}
		if (val.animationClips.Length == 0 || hold.animationClips.Length == 0 || main.animationClips.Length < 2)
		{
			throw new InvalidOperationException("Note hit-effect animation clips are missing.");
		}
		bool holdLoop = false;
		AnimationClip[] holdClips = hold.animationClips;
		for (int i = 0; i < holdClips.Length; i++)
		{
			if ((Object)(object)holdClips[i] != (Object)null && ((Object)holdClips[i]).name == "NoteHoldFXV2" && ((Motion)holdClips[i]).isLooping)
			{
				holdLoop = true;
				break;
			}
		}
		if (!holdLoop)
		{
			throw new InvalidOperationException("NoteHoldFXV2 must contain a looping clip.");
		}
		EditorCurveBinding[] loopBindings = AnimationUtility.GetObjectReferenceCurveBindings(mainLoop);
		if (loopBindings.Length != 1)
		{
			throw new InvalidOperationException("NoteFX_MainHoldLoop must contain one sprite curve.");
		}
		int[] expectedFrames = new int[30]
		{
			9, 10, 11, 12, 13, 14, 15, 16, 17, 18,
			19, 20, 21, 22, 23, 24, 23, 22, 21, 20,
			19, 18, 17, 16, 15, 14, 13, 12, 11, 10
		};
		ObjectReferenceKeyframe[] loopKeys = AnimationUtility.GetObjectReferenceCurve(mainLoop, loopBindings[0]);
		if (loopKeys.Length != expectedFrames.Length)
		{
			throw new InvalidOperationException("NoteFX_MainHoldLoop must use the 9-24 ping-pong range.");
		}
		for (int j = 0; j < loopKeys.Length; j++)
		{
			Object value = loopKeys[j].value;
			Sprite sprite = (Sprite)(object)((value is Sprite) ? value : null);
			if ((Object)(object)sprite == (Object)null || ((Object)sprite).name != "NoteFX_Main_" + expectedFrames[j])
			{
				throw new InvalidOperationException("NoteFX_MainHoldLoop frame " + j + " does not match NoteFX_Main_" + expectedFrames[j] + ".");
			}
		}
		Debug.Log((object)("[InFalsus] Note effects passed: mainLoop=" + mainLoop.length.ToString("0.000") + "s"));
	}

	private static void ValidateJudgementEngine()
	{
		JudgementEngine engine = new JudgementEngine(new Chart
		{
			Bpm = 140.0,
			Division = 4.0,
			Notes = 
			{
				new ChartNote
				{
					Kind = NoteKind.Tap,
					TimeMs = 1000.0,
					StartLane = 1,
					WidthLanes = 1
				},
				new ChartNote
				{
					Kind = NoteKind.Hold,
					TimeMs = 2000.0,
					StartLane = 2,
					WidthLanes = 1,
					DurationMs = 500.0
				},
				new ChartNote
				{
					Kind = NoteKind.Flick,
					TimeMs = 3000.0,
					StartPos = 0.5,
					StartWidth = 0.2,
					FlickDirection = 4
				},
				new ChartNote
				{
					Kind = NoteKind.SkyArea,
					TimeMs = 4000.0,
					StartPos = 0.2,
					EndPos = 0.8,
					StartWidth = 0.2,
					EndWidth = 0.2,
					DurationMs = 200.0,
					GroupId = 1
				}
			}
		});
		List<NoteKind> startedKinds = new List<NoteKind>();
		engine.NoteStarted += (JudgementEvent note) =>
		{
			startedKinds.Add(note.Note.Kind);
		};
		Assert(engine.PressLane(1, 1000.0), JudgementResult.Shiny, "tap");
		Assert(engine.PressLane(2, 2000.0), JudgementResult.Shiny, "hold start");
		engine.Update(2500.0);
		Assert(engine.PressFlick(4, 3000.0), JudgementResult.Shiny, "flick");
		Assert(engine.PressSky(4000.0), JudgementResult.Shiny, "sky");
		if (engine.JudgedCount != 4 || engine.Combo != 4 || engine.ShinyCount != 4 || engine.Score != 40004)
		{
			throw new InvalidOperationException("Judgement engine lifecycle failed: judged=" + engine.JudgedCount + " combo=" + engine.Combo + " shiny=" + engine.ShinyCount + " score=" + engine.Score);
		}
		if (startedKinds.Count != 2 || startedKinds[0] != NoteKind.Hold || startedKinds[1] != NoteKind.SkyArea)
		{
			throw new InvalidOperationException("Judgement start events failed. Expected hold and skyarea.");
		}
		JudgementEngine missEngine = new JudgementEngine(new Chart
		{
			Bpm = 140.0,
			Division = 4.0,
			Notes = 
			{
				new ChartNote
				{
					Kind = NoteKind.Tap,
					TimeMs = 1000.0,
					StartLane = 3,
					WidthLanes = 1
				}
			}
		});
		missEngine.Update(1200.0);
		if (missEngine.JudgedCount != 1 || missEngine.LastResult != JudgementResult.Miss)
		{
			throw new InvalidOperationException("Judgement miss lifecycle failed.");
		}
		JudgementEngine holdEngine = new JudgementEngine(new Chart
		{
			Bpm = 140.0,
			Division = 4.0,
			Notes = 
			{
				new ChartNote
				{
					Kind = NoteKind.Hold,
					TimeMs = 1000.0,
					StartLane = 4,
					WidthLanes = 1,
					DurationMs = 500.0
				}
			}
		});
		holdEngine.PressLane(4, 1000.0);
		holdEngine.ReleaseLane(4, 1200.0);
		if (holdEngine.JudgedCount != 1 || holdEngine.LastResult != JudgementResult.Miss)
		{
			throw new InvalidOperationException("Hold release lifecycle failed.");
		}
		Debug.Log((object)"[InFalsus] Judgement engine lifecycle checks passed.");
	}

	private static void ValidateFlickParsing()
	{
		ChartParseResult parsed = ChartParser.Parse(new string[3] { "chart(140,4)", "flick(1000,9,24,6,4)", "flick(1100,9,24,6,16)" }, "<flick-self-check>");
		if (!parsed.Success || parsed.Chart.Notes.Count != 2)
		{
			throw new InvalidOperationException("Flick parser self-check failed.");
		}
		ChartNote flick = parsed.Chart.Notes[0];
		ChartNote chartNote = parsed.Chart.Notes[1];
		AssertNear(flick.FlickCenterPos, 0.375, "flick center");
		AssertNear(flick.FlickWidth, 0.25, "flick width");
		AssertNear(flick.FlickLeftPos, 0.25, "flick left edge");
		AssertNear(flick.FlickRightPos, 0.5, "flick right edge");
		AssertNear(flick.FlickHeadPos, 0.5, "green/right head edge");
		AssertNear(chartNote.FlickHeadPos, 0.25, "yellow/left head edge");
		if (flick.StartCellCount != 24 || flick.EndCellCount != 24)
		{
			throw new InvalidOperationException("Flick parser did not preserve the shared grid.");
		}
		Debug.Log((object)"[InFalsus] Flick parser semantics check passed.");
	}

	private static void ValidateChartTimingFeatures()
	{
		ChartParseResult parsed = ChartParser.Parse(new string[9] { "# difficulty : 2", "chart(120,2)", "lane(0,4,0)", "lane(0,1,1)", "track(500,0)", "bpm(1000,60,0.25)", "track(1250,-0.5)", "lane(1500,4,1)", "lane(1500,1,0)" }, "<timing-self-check>");
		if (!parsed.Success)
		{
			throw new InvalidOperationException("Timing parser self-check failed: " + string.Join(" | ", parsed.Diagnostics));
		}
		Chart chart = parsed.Chart;
		AssertNear(chart.BpmAt(999.0), 120.0, "initial bpm");
		AssertNear(chart.BpmAt(1000.0), 60.0, "mid-chart bpm");
		AssertNear(chart.DivisionAt(999.0), 2.0, "initial beat count");
		AssertNear(chart.DivisionAt(1000.0), 0.25, "mid-chart beat count");
		if (chart.LaneEnabledAt(4, 999.0) || !chart.LaneEnabledAt(4, 1500.0) || !chart.LaneEnabledAt(1, 1499.0) || chart.LaneEnabledAt(1, 1500.0))
		{
			throw new InvalidOperationException("Lane enable/disable state tracking failed.");
		}
		if (!chart.TryGetLaneDisabledInterval(4, 1000.0, out var start, out var end))
		{
			throw new InvalidOperationException("Disabled lane interval was not found.");
		}
		AssertNear(start, 0.0, "disabled lane start");
		AssertNear(end, 1500.0, "disabled lane end");
		double[] expectedLines = new double[4] { 0.0, 1000.0, 1250.0, 1500.0 };
		for (int i = 0; i < expectedLines.Length; i++)
		{
			if (!ContainsTime(chart.MeasureLineTimes, expectedLines[i]))
			{
				throw new InvalidOperationException("Measure line is missing at " + expectedLines[i].ToString("0.###") + " ms.");
			}
		}
		if (ContainsTime(chart.MeasureLineTimes, 500.0))
		{
			throw new InvalidOperationException("track() must not refresh measure lines.");
		}
		if (ChartParser.Parse(new string[3] { "# difficulty : 4", "chart(120,4)", "lane(0,4,0)" }, "<lane-difficulty-self-check>").Success)
		{
			throw new InvalidOperationException("lane directives must be rejected above difficulty 2.");
		}
		Debug.Log((object)("[InFalsus] Chart timing checks passed: measures=" + chart.MeasureLineTimes.Count));
	}

	private static bool ContainsTime(List<double> values, double expected)
	{
		for (int i = 0; i < values.Count; i++)
		{
			if (Math.Abs(values[i] - expected) < 0.01)
			{
				return true;
			}
		}
		return false;
	}

	private static void ValidateTrackTravel()
	{
		Chart chart = new Chart
		{
			Bpm = 140.0,
			Division = 4.0,
			SpeedEvents = 
			{
				new ChartEvent
				{
					TimeMs = 1000.0,
					Multiplier = 2.0
				}
			}
		};
		TrackMotion trackMotion = new TrackMotion(chart, 5.0);
		double second = 30.27271842956543;
		AssertNear(trackMotion.TrackTravelAt(1000.0), second, "track travel");
		AssertNear(trackMotion.TrackTravelAt(2000.0), second * 3.0, "track travel with speed event");
		TrackMotion trackMotion2 = new TrackMotion(new Chart
		{
			Bpm = 140.0,
			Division = 4.0,
			SpeedEvents = 
			{
				new ChartEvent
				{
					TimeMs = 1000.0,
					Multiplier = 0.0
				},
				new ChartEvent
				{
					TimeMs = 2000.0,
					Multiplier = -1.0
				}
			}
		}, 5.0);
		AssertNear(trackMotion2.TrackTravelAt(1500.0), second, "track travel while stopped");
		AssertNear(trackMotion2.TrackTravelAt(3000.0), 0.0, "track travel while reversed");
		TrackMotion trackMotion3 = new TrackMotion(chart, 1.0);
		AssertNear(trackMotion3.Fade(trackMotion3.TrackLength), 0.0, "track entry alpha at tail");
		AssertNear(trackMotion3.Fade(trackMotion3.TrackLength * 0.95), 0.5, "track entry alpha midpoint");
		AssertNear(trackMotion3.Fade(trackMotion3.TrackLength * 0.9), 1.0, "track entry alpha full");
            var fadeMotion = new TrackMotion(chart, 1.0);
            AssertNear(
                fadeMotion.Fade(fadeMotion.TrackLength),
                0.0,
                "track entry alpha at tail");
            AssertNear(
                fadeMotion.Fade(fadeMotion.TrackLength * 0.95),
                0.5,
                "track entry alpha midpoint");
            AssertNear(
                fadeMotion.Fade(fadeMotion.TrackLength * 0.9),
                1.0,
                "track entry alpha full");
		ValidateNoteVisualLeadTime();
		Debug.Log((object)"[InFalsus] Track travel checks passed.");
	}

	/// <summary>
	/// A note must never be created after it is already drawable, and must
	/// never be recycled while it can still be drawn. Both reduce to one
	/// requirement: the visual lead time covers the whole visible track at
	/// the current track speed. Regression guard for the low-speed pop-in
	/// (the old 5 s cap covered only ~1.25 track lengths).
	/// </summary>
	private static void ValidateNoteVisualLeadTime()
	{
		double trackLength = new TrackMotion(
			new Chart
			{
				Bpm = 140.0,
				Division = 4.0
			},
			1.0).TrackLength;
		// Every slow section that matters in the shipped charts is >= 0.1.
		double[] speeds = { 10.0, 5.0, 1.0, 0.5, 0.25, 0.2, 0.15, 0.1 };
		for (int i = 0; i < speeds.Length; i++)
		{
			double unitsPerSecond =
				trackLength * TrackMotion.TrackScale * speeds[i];
			double leadMs = NoteRenderer.VisualLeadMsForTrackSpeed(
				trackLength,
				unitsPerSecond,
				40000.0);
			double coveredZ = unitsPerSecond * leadMs / 1000.0;
			if (coveredZ + 0.0001 < trackLength)
			{
				throw new InvalidOperationException(
					"Note visual lead time covered only " +
					coveredZ.ToString("0.###", Invariant) +
					" of " + trackLength.ToString("0.###", Invariant) +
					" track units at track speed " +
					speeds[i].ToString("0.###", Invariant) +
					" (lead " + leadMs.ToString("0.###", Invariant) +
					" ms): notes would pop in mid-track instead of " +
					"entering from the far end.");
			}
		}
		double slowLead = NoteRenderer.VisualLeadMsForTrackSpeed(
			trackLength,
			trackLength * TrackMotion.TrackScale * 0.25,
			40000.0);
		if (slowLead <= 5000.0)
		{
			throw new InvalidOperationException(
				"Note visual lead time did not grow at low track speed " +
				"(lead " + slowLead.ToString("0.###", Invariant) + " ms).");
		}
		Debug.Log(
			"[InFalsus] Note visual lead time checks passed (slow lead " +
			slowLead.ToString("0", Invariant) + " ms).");
	}

	private static void ValidateLaneStateRendering()
	{
		Chart chart = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		AddLaneEvent(chart, 85286.0, 1, enabled: true);
		AddLaneEvent(chart, 92571.0, 1, enabled: false);
		AddLaneEvent(chart, 99429.0, 1, enabled: true);
		chart.RebuildTimeline();
		TrackMotion motion = new TrackMotion(chart, 5.0);
		Vector4[] intervals = new Vector4[4];
		int count = LaneStateController.BuildVisibleDisableIntervals(chart, motion, 1, 92471.0, 1f, intervals);
		double expectedStart = motion.NoteZAt(92571.0, 92471.0);
		if (count != 1 || intervals[0].z < 0.5f || Math.Abs((double)intervals[0].x - expectedStart) > 0.009999999776482582 || Math.Abs((double)intervals[0].y - motion.VisibleLimit) > 0.009999999776482582)
		{
			throw new InvalidOperationException("Upcoming lane disable was not rendered along the track: count=" + count + " start=" + intervals[0].x + " expectedStart=" + expectedStart + " end=" + intervals[0].y + " expectedEnd=" + motion.VisibleLimit);
		}
		if (LaneStateController.BuildVisibleDisableIntervals(chart, motion, 1, 99429.0, 1f, intervals) != 0)
		{
			throw new InvalidOperationException("Lane disable interval remained after its enable event.");
		}
		Debug.Log((object)("[InFalsus] Lane state rendering checks passed: upcoming disable starts at distance " + expectedStart.ToString("0.###") + "."));
	}

	private static void AddLaneEvent(Chart chart, double timeMs, int lane, bool enabled)
	{
		ChartEvent evt = new ChartEvent
		{
			Kind = ChartEventKind.LaneLayout,
			TimeMs = timeMs,
			Lane = lane,
			Enabled = enabled
		};
		chart.Events.Add(evt);
		chart.LaneEvents.Add(evt);
	}

	private static void ValidateGameplayAudio()
	{
		AudioClip skyStart = LoadAudio("Assets/InFalsus/Gameplay/Audio/Resources/", "58_note_hit_skylane_draft1");
		AudioClip laneTap = LoadAudio("Assets/InFalsus/Gameplay/Audio/Resources/", "59_note_hit_3_draft2");
		AudioClip sideTap = LoadAudio("Assets/InFalsus/Gameplay/Audio/Resources/", "59_note_side_hit_draft2");
		AudioClip flick = LoadAudio("Assets/InFalsus/Gameplay/Audio/Resources/", "60_note_flick_5_draft2");
		AudioClip groundHold = LoadAudio("Assets/InFalsus/Gameplay/Audio/Resources/", "58_note_hold_floor+sides_draft1");
		AudioClip skyHold = LoadAudio("Assets/InFalsus/Gameplay/Audio/Resources/", "58_note_hold_skylane_5");
		if ((Object)(object)GameplayAudioRouting.TapClipForLane(0, laneTap, sideTap) != (Object)(object)sideTap || (Object)(object)GameplayAudioRouting.TapClipForLane(5, laneTap, sideTap) != (Object)(object)sideTap)
		{
			throw new InvalidOperationException("Side-lane tap audio routing failed.");
		}
		for (int lane = 1; lane <= 4; lane++)
		{
			if ((Object)(object)GameplayAudioRouting.TapClipForLane(lane, laneTap, sideTap) != (Object)(object)laneTap)
			{
				throw new InvalidOperationException("Center-lane tap audio routing failed at lane " + lane);
			}
		}
		if (skyStart.length <= 0f || laneTap.length <= 0f || sideTap.length <= 0f || flick.length <= 0f || groundHold.length <= 0f || skyHold.length <= 0f)
		{
			throw new InvalidOperationException("One or more gameplay audio clips are empty.");
		}
		Debug.Log((object)("[InFalsus] Gameplay audio checks passed: skyStart=" + skyStart.length.ToString("0.###") + "s laneTap=" + laneTap.length.ToString("0.###") + "s sideTap=" + sideTap.length.ToString("0.###") + "s flick=" + flick.length.ToString("0.###") + "s groundHold=" + groundHold.length.ToString("0.###") + "s skyHold=" + skyHold.length.ToString("0.###") + "s"));
	}

	private static AudioClip LoadAudio(string root, string name)
	{
		string path = root + name + ".wav";
		AudioClip val = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
		if ((Object)(object)val == (Object)null)
		{
			throw new InvalidOperationException("Gameplay audio asset is missing: " + path);
		}
		return val;
	}

	private static void ValidateGameplayAudioMix()
	{
		GameplayLimiterDsp limiter = new GameplayLimiterDsp(48000, 2, -1.5f, 2f, 120f);
		float[] samples = new float[8192];
		for (int i = 0; i < samples.Length; i++)
		{
			samples[i] = (((i & 1) == 0) ? 2f : (-2f));
		}
		limiter.Process(samples);
		float maximum = 0f;
		for (int j = 0; j < samples.Length; j++)
		{
			maximum = Math.Max(maximum, Math.Abs(samples[j]));
		}
		if (maximum > limiter.Ceiling + 1E-05f)
		{
			throw new InvalidOperationException("Gameplay master limiter exceeded its ceiling: " + maximum.ToString("0.000000") + " > " + limiter.Ceiling.ToString("0.000000"));
		}
		GameplayLimiterDsp transparentLimiter = new GameplayLimiterDsp(48000, 2, -1.5f, 2f, 120f);
		float[] transparentSamples = new float[8192];
		for (int k = 0; k < transparentSamples.Length; k++)
		{
			transparentSamples[k] = 0.25f;
		}
		transparentLimiter.Process(transparentSamples);
		float transparentMaximum = 0f;
		for (int l = 0; l < transparentSamples.Length; l++)
		{
			transparentMaximum = Math.Max(transparentMaximum, Math.Abs(transparentSamples[l]));
		}
		if (transparentMaximum < 0.24999f || transparentMaximum > 0.25001f)
		{
			throw new InvalidOperationException("Gameplay master limiter is not transparent below the ceiling: " + transparentMaximum.ToString("0.000000"));
		}
		Debug.Log((object)("[InFalsus] Gameplay audio mix checks passed: oneShotGain=" + 0.42f.ToString("0.##") + " holdGain=" + 0.24f.ToString("0.##") + " songGain=" + 0.72f.ToString("0.##") + " oneShotPriority=" + 2 + " ceiling=" + (20.0 * Math.Log10(limiter.Ceiling)).ToString("0.0") + "dBFS"));
	}

	private static void ValidateHoldStartHitAudio()
	{
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected Obj, but got Unknown
		Chart chart = new Chart();
		chart.Notes.Add(new ChartNote
		{
			Id = 0,
			Kind = NoteKind.Hold,
			TimeMs = 1000.0,
			StartLane = 0,
			WidthLanes = 1,
			DurationMs = 500.0
		});
		chart.Notes.Add(new ChartNote
		{
			Id = 1,
			Kind = NoteKind.Tap,
			TimeMs = 1000.0,
			StartLane = 5,
			WidthLanes = 1
		});
		JudgementEngine engine = new JudgementEngine(chart);
		GameObject host = new GameObject("GameplayHoldHitAudioSelfCheck");
		GameplayHitAudio hitAudio = host.AddComponent<GameplayHitAudio>();
		AudioClip skyStart = AudioClip.Create("selfcheck-sky", 100, 1, 44100, false);
		AudioClip laneTap = AudioClip.Create("selfcheck-lane", 100, 1, 44100, false);
		AudioClip sideTap = AudioClip.Create("selfcheck-side", 100, 1, 44100, false);
		AudioClip flick = AudioClip.Create("selfcheck-flick", 100, 1, 44100, false);
		AudioClip groundHold = AudioClip.Create("selfcheck-ground-hold", 100, 1, 44100, false);
		AudioClip skyHold = AudioClip.Create("selfcheck-sky-hold", 100, 1, 44100, false);
		try
		{
			hitAudio.Initialize(chart, engine, skyStart, laneTap, sideTap, flick, groundHold, skyHold);
			engine.PressLane(0, 1000.0);
			engine.PressLane(5, 1000.0);
			engine.ReleaseLane(0, 1500.0);
			if (hitAudio.StartedHoldCount != 1 || hitAudio.EvaluatedTapCount != 2 || hitAudio.PlayedOneShotCount != 2 || hitAudio.CoalescedTapCount != 0)
			{
				throw new InvalidOperationException("Hold start tap audio check failed: starts=" + hitAudio.StartedHoldCount + " evaluated=" + hitAudio.EvaluatedTapCount + " played=" + hitAudio.PlayedOneShotCount + " merged=" + hitAudio.CoalescedTapCount);
			}
			Debug.Log((object)"[InFalsus] Hold start tap audio check passed: one hold/tap chord preserved both tap sounds.");
		}
		finally
		{
			Object.DestroyImmediate((Object)(object)host);
			Object.DestroyImmediate((Object)(object)skyStart);
			Object.DestroyImmediate((Object)(object)laneTap);
			Object.DestroyImmediate((Object)(object)sideTap);
			Object.DestroyImmediate((Object)(object)flick);
			Object.DestroyImmediate((Object)(object)groundHold);
			Object.DestroyImmediate((Object)(object)skyHold);
		}
	}

	private static void ValidateOneShotVoiceStealing()
	{
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Expected Obj, but got Unknown
		Chart chart = new Chart();
		for (int i = 0; i <= 48; i++)
		{
			chart.Notes.Add(new ChartNote
			{
				Id = i,
				Kind = NoteKind.Tap,
				TimeMs = 1000.0 + (double)i * 2.0,
				StartLane = 3,
				WidthLanes = 1
			});
		}
		JudgementEngine engine = new JudgementEngine(chart);
		GameObject host = new GameObject("GameplayVoiceStealingSelfCheck");
		GameplayHitAudio hitAudio = host.AddComponent<GameplayHitAudio>();
		AudioClip skyStart = AudioClip.Create("selfcheck-sky", 100, 1, 44100, false);
		AudioClip laneTap = AudioClip.Create("selfcheck-long-lane", 441000, 1, 44100, false);
		AudioClip sideTap = AudioClip.Create("selfcheck-side", 100, 1, 44100, false);
		AudioClip flick = AudioClip.Create("selfcheck-flick", 100, 1, 44100, false);
		AudioClip groundHold = AudioClip.Create("selfcheck-ground-hold", 100, 1, 44100, false);
		AudioClip skyHold = AudioClip.Create("selfcheck-sky-hold", 100, 1, 44100, false);
		try
		{
			hitAudio.Initialize(chart, engine, skyStart, laneTap, sideTap, flick, groundHold, skyHold);
			for (int j = 0; j < chart.Notes.Count; j++)
			{
				engine.PressLane(chart.Notes[j].StartLane, chart.Notes[j].TimeMs);
			}
			if (hitAudio.PlayedOneShotCount != 49 || hitAudio.StolenOneShotCount != 1 || hitAudio.ActiveOneShotVoiceCount != 48)
			{
				throw new InvalidOperationException("One-shot voice stealing check failed: played=" + hitAudio.PlayedOneShotCount + " stolen=" + hitAudio.StolenOneShotCount + " active=" + hitAudio.ActiveOneShotVoiceCount + " capacity=" + 48);
			}
			Debug.Log((object)("[InFalsus] One-shot voice stealing check passed: capacity=" + 48 + " stolen=" + hitAudio.StolenOneShotCount));
		}
		finally
		{
			Object.DestroyImmediate((Object)(object)host);
			Object.DestroyImmediate((Object)(object)skyStart);
			Object.DestroyImmediate((Object)(object)laneTap);
			Object.DestroyImmediate((Object)(object)sideTap);
			Object.DestroyImmediate((Object)(object)flick);
			Object.DestroyImmediate((Object)(object)groundHold);
			Object.DestroyImmediate((Object)(object)skyHold);
		}
	}

	private static void AssertNear(double actual, double expected, string label)
	{
		if (Math.Abs(actual - expected) > 1E-07)
		{
			throw new InvalidOperationException("Flick parser check failed: " + label + " expected=" + expected + " actual=" + actual);
		}
	}

	private static void ValidateJudgementBoundaries()
	{
		Assert(Judgement.Evaluate(-25.0, NoteZone.Lane), JudgementResult.Shiny, "lane -25");
		Assert(Judgement.Evaluate(-25.1, NoteZone.Lane), JudgementResult.PerfectLate, "lane -25.1");
		Assert(Judgement.Evaluate(-50.0, NoteZone.Lane), JudgementResult.PerfectLate, "lane -50");
		Assert(Judgement.Evaluate(-50.1, NoteZone.Lane), JudgementResult.NearLate, "lane -50.1");
		Assert(Judgement.Evaluate(-100.0, NoteZone.Lane), JudgementResult.NearLate, "lane -100");
		Assert(Judgement.Evaluate(-100.1, NoteZone.Lane), JudgementResult.Miss, "lane -100.1");
		Assert(Judgement.Evaluate(25.0, NoteZone.Lane), JudgementResult.Shiny, "lane 25");
		Assert(Judgement.Evaluate(25.1, NoteZone.Lane), JudgementResult.PerfectEarly, "lane 25.1");
		Assert(Judgement.Evaluate(50.0, NoteZone.Lane), JudgementResult.PerfectEarly, "lane 50");
		Assert(Judgement.Evaluate(50.1, NoteZone.Lane), JudgementResult.NearEarly, "lane 50.1");
		Assert(Judgement.Evaluate(100.0, NoteZone.Lane), JudgementResult.NearEarly, "lane 100");
		Assert(Judgement.Evaluate(100.1, NoteZone.Lane), JudgementResult.Miss, "lane 100.1");
		Assert(Judgement.Evaluate(150.0, NoteZone.Lane), JudgementResult.Miss, "lane 150");
		Assert(Judgement.Evaluate(150.1, NoteZone.Lane), JudgementResult.None, "lane 150.1");
		Assert(Judgement.Evaluate(-50.0, NoteZone.Sky), JudgementResult.Shiny, "sky -50");
		Assert(Judgement.Evaluate(-50.1, NoteZone.Sky), JudgementResult.PerfectLate, "sky -50.1");
		Assert(Judgement.Evaluate(-100.0, NoteZone.Sky), JudgementResult.PerfectLate, "sky -100");
		Assert(Judgement.Evaluate(-100.1, NoteZone.Sky), JudgementResult.NearLate, "sky -100.1");
		Assert(Judgement.Evaluate(-200.0, NoteZone.Sky), JudgementResult.NearLate, "sky -200");
		Assert(Judgement.Evaluate(-200.1, NoteZone.Sky), JudgementResult.Miss, "sky -200.1");
		Assert(Judgement.Evaluate(50.0, NoteZone.Sky), JudgementResult.Shiny, "sky 50");
		Assert(Judgement.Evaluate(50.1, NoteZone.Sky), JudgementResult.PerfectEarly, "sky 50.1");
		Assert(Judgement.Evaluate(100.0, NoteZone.Sky), JudgementResult.PerfectEarly, "sky 100");
		Assert(Judgement.Evaluate(100.1, NoteZone.Sky), JudgementResult.NearEarly, "sky 100.1");
		Assert(Judgement.Evaluate(200.0, NoteZone.Sky), JudgementResult.NearEarly, "sky 200");
		Assert(Judgement.Evaluate(200.1, NoteZone.Sky), JudgementResult.None, "sky 200.1");
		Debug.Log((object)"[InFalsus] Judgement boundary checks passed.");
	}

	private static void Assert(JudgementResult actual, JudgementResult expected, string label)
	{
		if (actual != expected)
		{
			throw new InvalidOperationException("Judgement boundary failed: " + label + " expected=" + expected.ToString() + " actual=" + actual);
		}
	}

	private static void WriteJudgementMatrix()
	{
		string root = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
		string path = Path.Combine(root, "Diagnostics", "JudgementMatrix.csv");
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
		using StreamWriter writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		writer.WriteLine("delta_ms,lane_internal,lane_tier,sky_internal,sky_tier");
		for (int i = 0; i <= 5000; i++)
		{
			double delta = -250.0 + (double)i * 0.1;
			JudgementResult lane = Judgement.Evaluate(delta, NoteZone.Lane);
			JudgementResult sky = Judgement.Evaluate(delta, NoteZone.Sky);
			writer.WriteLine(delta.ToString("0.0", CultureInfo.InvariantCulture) + "," + lane.ToString() + "," + Judgement.TierOf(lane).ToString() + "," + sky.ToString() + "," + Judgement.TierOf(sky));
		}
	}

	private static void WriteChartReport()
	{
		string chartDirectory = Path.Combine(Application.dataPath, "StreamingAssets/Charts");
		string[] files = Directory.Exists(chartDirectory)
			? Directory.GetFiles(chartDirectory, "*.chart.txt", SearchOption.TopDirectoryOnly)
			: Array.Empty<string>();
		Array.Sort(files, StringComparer.Ordinal);
		List<ChartCheckEntry> entries = new List<ChartCheckEntry>();
		List<string> errors = new List<string>();
		int totalNotes = 0;
		int failed = 0;
		string[] array = files;
		foreach (string file in array)
		{
			ChartParseResult result = ChartParser.ParseFile(file);
			Chart chart = result.Chart;
			int noteCount = chart.Notes.Count;
			totalNotes += noteCount;
			if (!result.Success)
			{
				failed++;
				foreach (ChartParseDiagnostic diagnostic in result.Diagnostics)
				{
					errors.Add(diagnostic.ToString());
				}
			}
			entries.Add(new ChartCheckEntry
			{
				file = Path.GetFileName(file),
				song = chart.SongId,
				difficulty = chart.Difficulty,
				bpm = chart.Bpm,
				division = chart.Division,
				notes = noteCount,
				events = chart.Events.Count,
				errors = result.Diagnostics.Count
			});
		}
		ChartCheckReport report = new ChartCheckReport
		{
			generated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
			files = files.Length,
			failedFiles = failed,
			totalNotes = totalNotes,
			songs = entries.ToArray(),
			errors = errors.ToArray()
		};
		string text = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, "Diagnostics");
		Directory.CreateDirectory(text);
		File.WriteAllText(Path.Combine(text, "ChartParseReport.json"), JsonUtility.ToJson((object)report, true), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.WriteAllText(contents: new StringBuilder().AppendLine("chartFiles=" + files.Length).AppendLine("failedFiles=" + failed).AppendLine("totalNotes=" + totalNotes)
			.AppendLine("errors=" + errors.Count)
			.AppendLine("judgementBoundaryChecks=passed")
			.AppendLine("chartTimingFeatures=passed")
			.AppendLine("flickSemanticCheck=passed")
			.AppendLine("flickEffectAssetCheck=passed")
			.AppendLine("noteEffectAssetCheck=passed")
			.AppendLine("gameplayAudioCheck=passed")
			.ToString(), path: Path.Combine(text, "PhaseASelfCheck.txt"), encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		if (failed > 0)
		{
			throw new InvalidOperationException("Chart parsing failed for " + failed + " file(s).");
		}
		Debug.Log((object)("[InFalsus] Parsed " + files.Length + " charts, " + totalNotes + " notes."));
	}
}

[Serializable]
public sealed class ChartCheckEntry
{
	public string file;
	public string song;
	public int difficulty;
	public double bpm;
	public double division;
	public int notes;
	public int events;
	public int errors;
}

[Serializable]
public sealed class ChartCheckReport
{
	public string generated;
	public int files;
	public int failedFiles;
	public int totalNotes;
	public ChartCheckEntry[] songs;
	public string[] errors;
}
}
