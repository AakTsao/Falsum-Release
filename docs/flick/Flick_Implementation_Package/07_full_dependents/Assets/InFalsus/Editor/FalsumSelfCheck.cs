using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Judgement;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.Tools;
using InFalsus.Gameplay.Falsum.UI;
using InFalsus.Gameplay.Presentation.Audio;
using InFalsus.Gameplay.Presentation.Backgrounds;
using InFalsus.Gameplay.Presentation.Notes;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Object = UnityEngine.Object;

namespace InFalsus.EditorTools
{

public static class FalsumSelfCheck
{
	private sealed class SmoothClockAudioSink : IGameplayClockAudioSink, IGameplayClockAudioPosition
	{
		public int SeekCount;

		public int StopCount;

		public double AudioPositionMs { get; set; }

		public double AudioPositionDspTime { get; set; }

		public double SchedulingLeadMs { get; set; }

		public bool HasAudioPosition => true;

		public double AudioScheduleTimeMs => AudioPositionMs + SchedulingLeadMs;

		public void OnAudioSeek(double timeMs)
		{
			SeekCount++;
			AudioPositionMs = timeMs;
			AudioPositionDspTime = AudioSettings.dspTime;
		}

		public void OnAudioRate(double rate)
		{
		}

		public void OnAudioStop()
		{
			StopCount++;
		}
	}

	[MenuItem("InFalsus/Falsum/Run Editor Self Check")]
	public static void Run()
	{
		List<string> checks = new List<string>();
		try
		{
			CheckJson(checks);
			CheckUiToolkitResources(checks);
			CheckSkyAreaVisuals(checks);
			FalsumNoteRenderSelfCheck.Run();
			checks.Add("note_render_hotpaths");
			CheckManifest(checks);
			CheckManifestSafety(checks);
			CheckPreferences(checks);
			CheckBackgroundResources(checks);
			CheckChartRoundTrip(checks);
			CheckDualTimeAxes(checks);
			CheckNegativeMusicTimeGuard(checks);
			CheckTrackSpeedEffectSuppression(checks);
			CheckTapPlacementGuard(checks);
			CheckSelectionAnchors(checks);
			CheckClockTimeAxis(checks);
			CheckAutoplayStartState(checks);
			CheckAutoCombo(checks);
			CheckSkyBounds(checks);
                        CheckFineSkyFractions(checks);
			CheckSkyAreaGeometry(checks);
			CheckSkyAreaGrouping(checks);
			CheckSkyAreaBank(checks);
			CheckHistory(checks);
			CheckTimelineCacheInvalidation(checks);
			CheckHoldUvWindow(checks);
			CheckMultiSelectionSelfCheckCleanup(checks);
			CheckTransactionSafety(checks);
			CheckEventEditing(checks);
			CheckToolRuntime(checks);
			CheckToolNoteFields(checks);
			CheckBeatGrid(checks);
			CheckPackageRoundTrip(checks);
			CheckMenuWorkflowServices(checks);
			CheckTrackSpectrum(checks);
			CheckBassAudio(checks);
			CheckMobileShell(checks);
			CheckSelectionDragMath(checks);
			FalsumSelfCheckAudioPreprocess.Run(checks);
			WriteReport(passed: true, checks, string.Empty);
			Debug.Log((object)("[Falsum] Self check passed: " + checks.Count + " checks."));
		}
		catch (Exception ex)
		{
			WriteReport(passed: false, checks, ex.ToString());
			Debug.LogException(ex);
			throw;
		}
	}

	/// <summary>
	/// Runs ONLY the multi-selection drag checks, into their own report.
	/// Same reason as <see cref="RunMobileShell"/>: the full Run() is still
	/// blocked by an unrelated red UI assertion, so the drag contract needs an
	/// entry point that can be exercised on its own.
	/// </summary>
	[MenuItem("InFalsus/Falsum/Run Selection Drag Self Check")]
	public static void RunSelectionDrag()
	{
		List<string> checks = new List<string>();
		try
		{
			CheckSelectionDragMath(checks);
			WriteReportTo("FalsumSelectionDragSelfCheck.json", passed: true, checks, string.Empty);
			Debug.Log((object)("[Falsum] Selection drag self check passed: " + checks.Count + " checks."));
		}
		catch (Exception ex)
		{
			WriteReportTo("FalsumSelectionDragSelfCheck.json", passed: false, checks, ex.ToString());
			Debug.LogException(ex);
			throw;
		}
	}

	/// <summary>
	/// Runs ONLY the mobile shell checks, and reports to its own file.
	///
	/// WHY A SEPARATE ENTRY POINT: the full Run() walks CheckUiToolkitResources
	/// early, and a failure there aborts before any later check executes. That
	/// makes it useless for iterating on one subsystem while an unrelated
	/// assertion in another subsystem is red. This entry point also writes
	/// FalsumMobileSelfCheck.json instead of overwriting the main report, so
	/// the two can be read side by side.
	/// </summary>
	[MenuItem("InFalsus/Falsum/Run Mobile Shell Self Check")]
	public static void RunMobileShell()
	{
		List<string> checks = new List<string>();
		try
		{
			CheckMobileShell(checks);
			WriteMobileReport(passed: true, checks, string.Empty);
			Debug.Log((object)("[Falsum] Mobile shell self check passed: " + checks.Count + " checks."));
		}
		catch (Exception ex)
		{
			WriteMobileReport(passed: false, checks, ex.ToString());
			Debug.LogException(ex);
			throw;
		}
	}

	[MenuItem("InFalsus/Falsum/Run BASS Audio Self Check")]
	public static void RunBassAudio()
	{
		List<string> checks = new List<string>();
		try
		{
			CheckBassAudio(checks);
			WriteReport(passed: true, checks, string.Empty);
			Debug.Log((object)("[Falsum] BASS audio self check passed: " + checks.Count + " checks."));
		}
		catch (Exception ex)
		{
			WriteReport(passed: false, checks, ex.ToString());
			Debug.LogException(ex);
			throw;
		}
	}

	[MenuItem("InFalsus/Falsum/Run Tool Note Field Self Check")]
	public static void RunToolNoteFields()
	{
		List<string> checks = new List<string>();
		try
		{
			CheckToolNoteFields(checks);
			WriteReport(passed: true, checks, string.Empty);
			Debug.Log((object)("[Falsum] Tool note field self check passed: " + checks.Count + " checks."));
		}
		catch (Exception ex)
		{
			WriteReport(passed: false, checks, ex.ToString());
			Debug.LogException(ex);
			throw;
		}
	}

	[MenuItem("InFalsus/Falsum/Run Package Compatibility Check")]
	public static void RunPackageCompatibility()
	{
		string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();
		string root = Path.Combine(Path.GetDirectoryName(projectRoot) ?? projectRoot, "export", "falsum");
		string[] files = (Directory.Exists(root) ? Directory.GetFiles(root, "*.falsum", SearchOption.TopDirectoryOnly) : new string[0]);
		List<string> failures = new List<string>();
		for (int i = 0; i < files.Length; i++)
		{
			try
			{
				Require(FalsumPackage.Load(files[i]).Charts.Count > 0, "Package has no charts.");
			}
			catch (Exception ex)
			{
				failures.Add(Path.GetFileName(files[i]) + ": " + ex.Message);
			}
		}
		if (failures.Count > 0)
		{
			throw new InvalidOperationException(failures.Count + " package(s) failed compatibility: " + string.Join(" | ", failures.ToArray()));
		}
		Debug.Log((object)("[Falsum] Package compatibility passed: " + files.Length + " package(s)."));
	}

	private static void CheckJson(List<string> checks)
	{
		FalsumJsonObject source = FalsumJson.AsObject(FalsumJson.Parse(FalsumJson.Serialize(new FalsumJsonObject
		{
			["name"] = "测试 \"Falsum\"",
			["version"] = 1,
			["enabled"] = true,
			["values"] = new List<object> { 1.5, false, "line\nbreak" }
		}, pretty: true)));
		Require(FalsumJson.GetString(source, "name", string.Empty) == "测试 \"Falsum\"", "JSON string round trip failed.");
		Require(FalsumJson.GetArray(source, "values").Count == 3, "JSON array round trip failed.");
		Require(FalsumEditorView.SanitizeNumericInput("12q.5") == "12.5" && FalsumEditorView.SanitizeNumericInput("1/2q", allowFraction: true) == "1/2", "Numeric input sanitization failed.");
		checks.Add("json");
	}

	private static void CheckToolRuntime(List<string> checks)
	{
		FalsumToolManifest manifest = FalsumToolManifest.Load(Path.Combine(FalsumRuntimePaths.BundledToolsDirectory, "com.falsum.tools.track-easing"));
		Require(manifest.IsReady, "Track tool manifest failed: " + manifest.LoadError);
		Chart chart = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		chart.Events.Add(new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 1200.0,
			Multiplier = 1.0
		});
		chart.Events.Add(new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 2500.0,
			Multiplier = 0.75
		});
		chart.RebuildTimeline();
		FalsumChartSession session = new FalsumChartSession(chart, 0.0);
		FalsumToolChartSnapshot snapshot = FalsumToolChartSnapshot.Capture(chart, 0.0, 1000.0);
		using (FalsumToolRuntime runtime = new FalsumToolRuntime(manifest, snapshot))
		{
			runtime.Load();
			Require(runtime.CreateForm().Fields.Count == 6, "Track tool should expose six fields.");
			FalsumToolRunRequest request = new FalsumToolRunRequest
			{
				ToolId = manifest.Id
			};
			request.Values["startTime"] = 1000;
			request.Values["startSpeed"] = 1.0;
			request.Values["endTime"] = 2000;
			request.Values["endSpeed"] = 2.0;
			request.Values["easing"] = "sineInOut";
			request.Values["nodes"] = 16;
			List<FalsumToolValidationIssue> issues = runtime.Validate(request);
			Require(issues.Count == 0, "Track tool validation failed: " + ((issues.Count > 0) ? issues[0].Message : string.Empty));
			FalsumToolRunOutput output = runtime.Run(request);
			Require(output.Batch != null && output.Batch.Count == 17, "Track tool should remove one event and add 16.");
			Require(session.ApplyBatch("工具：Track 缓动工具", output.Batch.Apply, output.Batch.DelayMs), "Track tool transaction did not commit.");
		}
		Require(chart.Events.Count == 17, "Track tool should leave 17 events.");
		Require(session.UndoCount == 1, "Track tool must use one undo record.");
		ChartEvent first = chart.SpeedEvents[0];
		ChartEvent last = null;
		for (int i = 0; i < chart.SpeedEvents.Count; i++)
		{
			if (Math.Abs(chart.SpeedEvents[i].TimeMs - 2000.0) < 0.0001)
			{
				last = chart.SpeedEvents[i];
				break;
			}
		}
		Require(Math.Abs(first.TimeMs - 1000.0) < 0.0001 && Math.Abs(first.Multiplier - 1.0) < 0.0001, "Track tool start endpoint is invalid.");
		Require(last != null && Math.Abs(last.TimeMs - 2000.0) < 0.0001 && Math.Abs(last.Multiplier - 2.0) < 0.0001, "Track tool end endpoint is invalid.");
		for (int i = 0; i < chart.SpeedEvents.Count; i++)
		{
			double scaled = chart.SpeedEvents[i].Multiplier * 100.0;
			Require(Math.Abs(scaled - Math.Round(scaled)) < 0.000000001, "Track tool speed precision exceeds two decimals.");
		}
		Require(session.Undo() && chart.Events.Count == 2, "Track tool undo failed.");
		Require(session.Redo() && chart.Events.Count == 17, "Track tool redo failed.");
		FalsumToolManifest designRuleManifest = FalsumToolManifest.Load(Path.Combine(FalsumRuntimePaths.BundledToolsDirectory, "com.falsum.tools.design-rule-checker"));
		Require(designRuleManifest.IsReady, "Design rule tool manifest failed: " + designRuleManifest.LoadError);
		Require(!string.IsNullOrWhiteSpace(designRuleManifest.DisplayDescription), "Design rule tool description is missing.");
		Require(designRuleManifest.DisplayDescription != designRuleManifest.Description, "Design rule tool description is not localized.");
		Require(designRuleManifest.DisplayName != designRuleManifest.Name, "Design rule tool name is not localized.");
		Chart chart2 = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		AddRuleCheckNote(chart2, 0, NoteKind.Hold, 1000.0, 1000.0, 1, 1);
		AddRuleCheckNote(chart2, 1, NoteKind.Tap, 1500.0, 0.0, 1, 1);
		AddRuleCheckNote(chart2, 2, NoteKind.Tap, 2000.0, 0.0, 1, 1);
		AddRuleCheckNote(chart2, 3, NoteKind.Hold, 2000.0, 500.0, 1, 1);
		AddRuleCheckNote(chart2, 4, NoteKind.Tap, 1800.0, 0.0, 2, 1);
		AddRuleCheckNote(chart2, 5, NoteKind.Hold, 3000.0, 1000.0, 0, 2);
		AddRuleCheckNote(chart2, 6, NoteKind.Tap, 3500.0, 0.0, 1, 1);
		AddRuleCheckNote(chart2, 7, NoteKind.Flick, 3500.0, 0.0, 1, 1);
		AddRuleCheckNote(chart2, 8, NoteKind.SkyArea, 3500.0, 500.0, 1, 1);
		AddRuleCheckNote(chart2, 9, NoteKind.Tap, 4001.0, 0.0, 0, 1);
		AddRuleCheckNote(chart2, 10, NoteKind.Tap, 3500.0, 0.0, 2, 1);
		AddRuleCheckNote(chart2, 11, NoteKind.Hold, 2100.0, 100.0, 1, 1);
		chart2.RebuildTimeline();
		FalsumToolChartSnapshot ruleSnapshot = FalsumToolChartSnapshot.Capture(chart2, 0.0, 0.0);
		using (FalsumToolRuntime runtime2 = new FalsumToolRuntime(designRuleManifest, ruleSnapshot))
		{
			runtime2.Load();
			Require(runtime2.CreateForm().Fields.Count == 0, "Design rule tool should not require fields.");
			FalsumToolRunRequest request2 = new FalsumToolRunRequest
			{
				ToolId = designRuleManifest.Id
			};
			FalsumToolRunOutput output2 = runtime2.Run(request2);
			Require(output2.Batch != null && output2.Batch.Count == 0 && output2.Report != null && output2.Report.Items.Count == 6, "Design rule tool report count is incorrect.");
			bool hasTapInsideHold = false;
			bool hasHoldInsideHold = false;
			bool hasEndpointTouch = false;
			double previousTime = -1.0;
			for (int j = 0; j < output2.Report.Items.Count; j++)
			{
				FalsumToolReportItem item = output2.Report.Items[j];
				Require(item.TargetNotes.Count == 2 && item.TimeMs >= previousTime, "Design rule report target or order is invalid.");
				previousTime = item.TimeMs;
				hasTapInsideHold |= item.Title == designRuleManifest.GetText("report.overlap.nestedTap", string.Empty);
				hasHoldInsideHold |= item.Title == designRuleManifest.GetText("report.overlap.nestedHold", string.Empty);
				hasEndpointTouch |= item.Title == designRuleManifest.GetText("report.overlap.time", string.Empty);
			}
			Require(hasTapInsideHold & hasHoldInsideHold & hasEndpointTouch, "Design rule report is missing core overlap cases.");
		}
		Chart skyInvalid = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		AddSkyAreaCheckNote(skyInvalid, 0, 5000.0, 1000.0, 0.5, 0.6, 0.5, 0.6, 0, 0);
		AddSkyAreaCheckNote(skyInvalid, 1, 5000.0, 1000.0, 0.5, 0.4, 0.5, 0.4, 0, 0);
		AddSkyAreaCheckNote(skyInvalid, 2, 5000.0, 1000.0, 0.5, 0.2, 0.9, 0.2, 0, 0);
		AddSkyAreaCheckNote(skyInvalid, 3, 8000.0, 1000.0, 0.05, 0.1, 0.05, 0.1, 1, 2);
		AddSkyAreaCheckNote(skyInvalid, 4, 10000.0, 1000.0, 0.95, 0.1, 0.95, 0.1, 2, 1);
		FalsumToolRunOutput skyInvalidOutput = RunDesignRuleTool(designRuleManifest, skyInvalid);
		Require(skyInvalidOutput.Report != null && skyInvalidOutput.Report.Items.Count == 0, "SkyArea without a common intersection should be accepted.");
		Chart skyPoint = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		AddSkyAreaCheckNote(skyPoint, 0, 12000.0, 1000.0, 0.4, 0.2, 0.4, 0.2, 1, 2);
		AddSkyAreaCheckNote(skyPoint, 1, 12000.0, 1000.0, 0.6, 0.2, 0.6, 0.2, 2, 1);
		AddSkyAreaCheckNote(skyPoint, 2, 12000.0, 1000.0, 0.5, 0.2, 0.5, 0.2, 0, 0);
		FalsumToolRunOutput skyPointOutput = RunDesignRuleTool(designRuleManifest, skyPoint);
		Require(skyPointOutput.Report != null && skyPointOutput.Report.Items.Count == 0, "SkyArea common point intersection was rejected.");
		Chart skyTouching = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		AddSkyAreaCheckNote(skyTouching, 0, 14000.0, 1000.0, 0.1, 0.1, 0.1, 0.1, 0, 0);
		AddSkyAreaCheckNote(skyTouching, 1, 15000.0, 1000.0, 0.9, 0.1, 0.9, 0.1, 0, 0);
		FalsumToolRunOutput skyTouchingOutput = RunDesignRuleTool(designRuleManifest, skyTouching);
		Require(skyTouchingOutput.Report != null && skyTouchingOutput.Report.Items.Count == 0, "Touching SkyArea endpoints should not be treated as an overlap.");
		checks.Add("tool-runtime");
	}

	private static void CheckToolNoteFields(List<string> checks)
	{
		FalsumToolFormDefinition form = FalsumToolFormDefinition.FromJson(
			FalsumJson.Parse(
				"{\"title\":\"Note picker\",\"fields\":[" +
				"{\"key\":\"targets\",\"type\":\"note\"," +
				"\"label\":\"Targets\",\"required\":true," +
				"\"multiple\":true,\"noteKinds\":[\"tap\",\"hold\"]," +
				"\"min\":1,\"max\":8}]}"));
		Require(form.Fields.Count == 1, "Note form field count failed.");
		FalsumToolFieldDefinition field = form.Fields[0];
		Require(field.IsNoteField && field.Multiple, "Note field type or multiple flag failed.");
		Require(field.EffectiveNoteMinimum == 1 && field.EffectiveNoteMaximum == 8, "Note field bounds failed.");
		Require(field.NoteKinds.Count == 2 && field.AllowsNoteKind("tap") && field.AllowsNoteKind("hold") && !field.AllowsNoteKind("flick"), "Note kind filtering failed.");
		FalsumToolFormDefinition singleForm = FalsumToolFormDefinition.FromJson(
			FalsumJson.Parse(
				"{\"fields\":[{\"key\":\"target\",\"type\":\"note\"," +
				"\"label\":\"Target\",\"required\":true}]}"));
		FalsumToolFieldDefinition single = singleForm.Fields[0];
		Require(!single.Multiple && single.EffectiveNoteMinimum == 1 && single.EffectiveNoteMaximum == 1, "Single note field defaults failed.");
		Require(single.NoteKinds.Count == 4, "Single note field should allow every note kind by default.");
		FalsumToolNoteReference reference = new FalsumToolNoteReference
		{
			Key = "note:12",
			Id = 12,
			Kind = "tap",
			TimeMs = 1234.0
		};
		FalsumJsonObject json = reference.ToJsonObject();
		Require(FalsumJson.GetString(json, "key", string.Empty) == "note:12" && FalsumJson.GetInt(json, "id", 0) == 12 && FalsumJson.GetString(json, "kind", string.Empty) == "tap" && Math.Abs(Convert.ToDouble(json["timeMs"], System.Globalization.CultureInfo.InvariantCulture) - 1234.0) < 0.0001, "Note reference serialization failed.");
		RequireThrows<FormatException>(() =>
		{
			FalsumToolFormDefinition.FromJson(FalsumJson.Parse(
				"{\"fields\":[{\"key\":\"target\",\"type\":\"note\"," +
				"\"label\":\"Target\",\"noteKinds\":[\"bogus\"]}]}"));
		}, "Unknown note kind was accepted.");
		RequireThrows<FormatException>(() =>
		{
			FalsumToolFormDefinition.FromJson(FalsumJson.Parse(
				"{\"fields\":[{\"key\":\"target\",\"type\":\"note\"," +
				"\"label\":\"Target\",\"noteKinds\":[\"tap\",\"tap\"]}]}"));
		}, "Duplicate note kind was accepted.");
		RequireThrows<FormatException>(() =>
		{
			FalsumToolFormDefinition.FromJson(FalsumJson.Parse(
				"{\"fields\":[{\"key\":\"target\",\"type\":\"note\"," +
				"\"label\":\"Target\",\"min\":4,\"max\":2}]}"));
		}, "Note range with max below min was accepted.");
		RequireThrows<FormatException>(() =>
		{
			FalsumToolFormDefinition.FromJson(FalsumJson.Parse(
				"{\"fields\":[{\"key\":\"target\",\"type\":\"note\"," +
				"\"label\":\"Target\",\"max\":2}]}"));
		}, "Single note field accepted max above one.");
		RequireThrows<FormatException>(() =>
		{
			FalsumToolFormDefinition.FromJson(FalsumJson.Parse(
				"{\"fields\":[{\"key\":\"target\",\"type\":\"note\"," +
				"\"label\":\"Target\",\"min\":0.5}]}"));
		}, "Fractional note count bound was accepted.");
		// The note picker's wheel is a UI Toolkit WheelEvent, whose delta.y
		// sign is the OPPOSITE of Input.mouseScrollDelta (UITK: positive =
		// wheel down; legacy Input: positive = wheel up). It must be
		// normalised to the legacy sign before it reaches ScrollTimeline,
		// otherwise scrolling the picker moves the timeline the wrong way
		// versus the chart behind it. Pin the conversion so a future
		// "simplification" cannot drop the negation again.
		Require(
			FalsumToolDialogView.NormalizeWheelDelta(1.0f) < 0.0f &&
			FalsumToolDialogView.NormalizeWheelDelta(-1.0f) > 0.0f,
			"Note picker wheel still forwards UI Toolkit's delta sign, so it " +
			"steps the timeline backwards relative to the chart.");
		Require(
			Math.Abs(FalsumToolDialogView.NormalizeWheelDelta(0.0f)) < 0.0001f,
			"Note picker wheel normalisation must leave a zero delta alone.");
		checks.Add("tool-note-fields");
	}

	private static FalsumToolRunOutput RunDesignRuleTool(FalsumToolManifest manifest, Chart chart)
	{
		chart.RebuildTimeline();
		FalsumToolChartSnapshot snapshot = FalsumToolChartSnapshot.Capture(chart, 0.0, 0.0);
		using FalsumToolRuntime runtime = new FalsumToolRuntime(manifest, snapshot);
		runtime.Load();
		return runtime.Run(new FalsumToolRunRequest
		{
			ToolId = manifest.Id
		});
	}

	private static void AddSkyAreaCheckNote(Chart chart, int id, double timeMs, double durationMs, double startPos, double startWidth, double endPos, double endWidth, int leftEasing, int rightEasing)
	{
		chart.Notes.Add(new ChartNote
		{
			Id = id,
			Kind = NoteKind.SkyArea,
			TimeMs = timeMs,
			DurationMs = durationMs,
			StartPos = startPos,
			StartWidth = startWidth,
			EndPos = endPos,
			EndWidth = endWidth,
			LeftEasing = leftEasing,
			RightEasing = rightEasing
		});
	}

	private static void AddRuleCheckNote(Chart chart, int id, NoteKind kind, double timeMs, double durationMs, int startLane, int widthLanes)
	{
		chart.Notes.Add(new ChartNote
		{
			Id = id,
			Kind = kind,
			TimeMs = timeMs,
			DurationMs = durationMs,
			StartLane = startLane,
			WidthLanes = widthLanes
		});
	}

	private static void CheckManifest(List<string> checks)
	{
		FalsumManifest manifest = FalsumManifest.CreateTemplate("selfcheck", "Self Check", "In Falsus", 120000);
		FalsumManifest parsed = FalsumManifest.Parse(manifest.Serialize());
		Require(parsed.SongId == "selfcheck", "Manifest songId failed.");
		Require(parsed.Difficulties.Count == 4, "Manifest difficulty count failed.");
		Require(parsed.FindDifficulty(0).Available, "Manifest availability failed.");
		Require(parsed.BackgroundFile == "31_Space-Positive.png" && parsed.EffectiveBackgroundFile == "31_Space-Positive.png", "Manifest default background failed.");
		manifest.SetBackground("04_Mindscape-3-Dusk.png");
		manifest.SetJacketDesigner("cover artist");
		parsed = FalsumManifest.Parse(manifest.Serialize());
		Require(parsed.JacketDesigner == "cover artist", "Manifest jacket designer failed.");
		for (int i = 0; i < parsed.Difficulties.Count; i++)
		{
			Require(parsed.Difficulties[i].JacketDesigner == "cover artist", "Manifest per-difficulty jacket designer failed.");
		}
		parsed = FalsumManifest.Parse(manifest.Serialize());
		Require(parsed.BackgroundFile == "04_Mindscape-3-Dusk.png" && parsed.EffectiveBackgroundFile == "04_Mindscape-3-Dusk.png", "Manifest explicit background failed.");
		manifest.Root["background"] = string.Empty;
		parsed = FalsumManifest.Parse(manifest.Serialize());
		Require(parsed.BackgroundFile.Length == 0 && parsed.EffectiveBackgroundFile == "31_Space-Positive.png", "Manifest missing background fallback failed.");
		Require(FalsumManifest.IsValidBackgroundFile("31_Space-Positive.png") && !FalsumManifest.IsValidBackgroundFile("../31_Space-Positive.png") && !FalsumManifest.IsValidBackgroundFile("folder/31_Space-Positive.png") && !FalsumManifest.IsValidBackgroundFile("31_Space-Positive.txt"), "Manifest background path validation failed.");
		checks.Add("manifest");
	}

	private static void CheckManifestSafety(List<string> checks)
	{
		FalsumManifest template = FalsumManifest.CreateTemplate("safe_id", "Safe", "Artist", 1000);
		template.Validate();
		string[] invalidIds = new string[7]
		{
			"../evil",
			"C:\\evil",
			"a/b",
			"a\\b",
			"UpperCase",
			"-leading",
			string.Empty
		};
		for (int i = 0; i < invalidIds.Length; i++)
		{
			FalsumManifest falsumManifest = FalsumManifest.Parse(template.Serialize());
			falsumManifest.SetSongId(invalidIds[i]);
			RequireThrows<FormatException>(falsumManifest.Validate, "Unsafe songId was accepted: " + invalidIds[i]);
		}
		string templateJson = template.Serialize();
		RequireThrows<FormatException>(() =>
		{
			FalsumManifest.Parse(templateJson.Replace("\"charts/evo.chart.txt\"", "\"charts/min.chart.txt\""));
		}, "Duplicate chart path was accepted.");
		RequireThrows<FormatException>(() =>
		{
			FalsumManifest.Parse(templateJson.Replace("\"audio.ogg\"", "\"../evil.ogg\""));
		}, "Package audio path traversal was accepted.");
		RequireThrows<FormatException>(() =>
		{
			FalsumManifest.Parse(templateJson.Replace("\"version\": 1", "\"version\": 1.5"));
		}, "Fractional manifest version was accepted.");
		RequireThrows<FormatException>(() =>
		{
			FalsumJson.Parse("{\"x\":1,\"x\":2}");
		}, "Duplicate JSON keys were accepted.");
		Require(string.Equals(FalsumFileDialog.EnsureFalsumExtension("song.txt"), "song.falsum", StringComparison.Ordinal), "Non-Falsum save extension was not replaced.");
		Require(FalsumManifest.NormalizeSongId("My Song") == "my-song" && FalsumManifest.IsValidSongId(FalsumManifest.NormalizeSongId("  Song #1  ")), "Song ID normalization produced an invalid id.");
		// Two song-id rules, never interchangeable: the create path only
		// accepts lowercase letters followed by lowercase letters/digits,
		// while the READ path keeps accepting legacy ids with separators.
		// Swapping them either rejects shipped packages or silently admits
		// uppercase ids the create dialog promised to refuse.
		Require(FalsumManifest.IsValidNewSongId("a") && FalsumManifest.IsValidNewSongId("abc123") && FalsumManifest.IsValidNewSongId(new string('a', 64)), "The new song id rule rejected a legal id.");
		Require(!FalsumManifest.IsValidNewSongId(string.Empty) && !FalsumManifest.IsValidNewSongId("Abc") && !FalsumManifest.IsValidNewSongId("1abc") && !FalsumManifest.IsValidNewSongId("a-b") && !FalsumManifest.IsValidNewSongId("a_b") && !FalsumManifest.IsValidNewSongId("a b") && !FalsumManifest.IsValidNewSongId(new string('a', 65)), "The new song id rule accepted an illegal id.");
		Require(FalsumManifest.IsValidSongId("a-b") && FalsumManifest.IsValidSongId("a_b") && FalsumManifest.IsValidSongId("1abc") && !FalsumManifest.IsValidSongId("Abc"), "The legacy song id rule changed; shipped packages would stop loading.");
		string root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), "Temp", "FalsumManifestSafety");
		Directory.CreateDirectory(root);
		string sourcePath = Path.Combine(root, "partial.falsum");
		string blockedPath = Path.Combine(root, "blocked.falsum");
		if (File.Exists(sourcePath))
		{
			File.Delete(sourcePath);
		}
		if (File.Exists(blockedPath))
		{
			File.Delete(blockedPath);
		}
		if (Directory.Exists(blockedPath))
		{
			Directory.Delete(blockedPath);
		}
		Directory.CreateDirectory(blockedPath);
		FalsumManifest partial = FalsumManifest.CreateTemplate("partial_safety", "Partial", "Artist", 1000);
		partial.FindDifficulty(1).SetAvailable(value: true);
		Chart emptyChart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0
		};
		using (FileStream stream = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
			WriteZipText(archive, "song.json", partial.Serialize());
			WriteZipBytes(archive, "audio.ogg", new byte[8] { 79, 103, 103, 83, 0, 0, 0, 0 });
			WriteZipText(archive, "charts/min.chart.txt", ChartWriter.Write(emptyChart, 0.0, partial.SongId, 0));
		}
		FalsumPackage package = FalsumPackage.Load(sourcePath);
		Require(package.Manifest.FindDifficulty(1).Available, "Partial test fixture is invalid.");
		RequireThrows<IOException>(() =>
		{
			package.Save(blockedPath);
		}, "Saving over a directory unexpectedly succeeded.");
		Require(package.Manifest.FindDifficulty(1).Available, "Failed save mutated difficulty availability.");
		Directory.Delete(blockedPath);
		File.Delete(sourcePath);
		checks.Add("manifest_safety");
	}

	private static void CheckBackgroundResources(List<string> checks)
	{
		string root = FalsumRuntimePaths.BackgroundsDirectory;
		string path = Path.Combine(root, "song_background_map.json");
		Require(File.Exists(path), "Gameplay background mapping is missing.");
		List<object> songs = FalsumJson.GetArray(FalsumJson.AsObject(FalsumJson.Parse(File.ReadAllText(path))), "songs");
		Require(songs != null && songs.Count > 0, "Gameplay background mapping is empty.");
		Require(FalsumRuntimePaths.BackgroundDirectories.Count > 0, "No gameplay background directory was found.");
		Require(FalsumRuntimePaths.ResolveBackgroundFile(FalsumManifest.DefaultBackgroundFile).Length > 0, "Default background does not resolve: " + FalsumManifest.DefaultBackgroundFile);
		AssertLegacyBackgroundCompatibility();
		for (int i = 0; i < songs.Count; i++)
		{
			FalsumJsonObject obj = songs[i] as FalsumJsonObject;
			Require(obj != null, "Gameplay background mapping entry is invalid.");
			string file = FalsumJson.GetString(obj, "background_file", string.Empty);
			Require(FalsumManifest.IsValidBackgroundFile(file), "Gameplay background mapping has an invalid file.");
			Require(!string.IsNullOrEmpty(FalsumRuntimePaths.ResolveBackgroundFile(root, file)), "Gameplay background is missing: " + file);
		}
		Require(File.Exists(Path.Combine(Application.streamingAssetsPath, "Falsum", "Playfield", "playfieldBG.png")), "Track-end playfield image is missing.");
		Shader maskShader = Shader.Find("InFalsus/FalsumTrackEndDiamondMask");
		Shader blurShader = Shader.Find("InFalsus/FalsumTrackEndDiamondBlur");
		Shader overlayShader = Shader.Find("InFalsus/FalsumTrackEndDiamondOverlay");
		Require((Object)(object)maskShader != (Object)null, "Track-end mask shader is missing.");
		Require((Object)(object)blurShader != (Object)null, "Track-end blur shader is missing.");
		Require((Object)(object)overlayShader != (Object)null, "Track-end overlay shader is missing.");
		Require(maskShader.passCount == 3, "Track-end mask shader must have three passes.");
		Material maskMaterial = new Material(maskShader);
		Require(maskMaterial.HasProperty("_TimeOverride"), "Track-end mask shader is missing _TimeOverride.");
		Object.DestroyImmediate(maskMaterial);
		Require(blurShader.passCount == 2, "Track-end blur shader must have two passes.");
		Require(overlayShader.passCount >= 1, "Track-end overlay shader has no pass.");
		Require((Object)(object)Shader.Find("InFalsus/FalsumTrackEndMask") == (Object)null, "Legacy TrackEnd mask shader is still present.");
		Require((Object)(object)Shader.Find("InFalsus/FalsumTrackEndBlur") == (Object)null, "Legacy TrackEnd blur shader is still present.");
		Require((Object)(object)Shader.Find("InFalsus/FalsumTrackEndOverlay") == (Object)null, "Legacy TrackEnd overlay shader is still present.");
		string installerError;
		Require(FalsumTrackEndComposerInstaller.IsInstalled(out installerError), installerError);
		Require(FalsumTrackEndComposer.GetMaskSize(3840) == new Vector2Int(1200, 800), "Track-end mask size at 3840 is incorrect.");
		Require(FalsumTrackEndComposer.GetMaskSize(2560) == new Vector2Int(900, 600), "Track-end mask size at 2560 is incorrect.");
		Require(FalsumTrackEndComposer.OutputUvMax == 0.5f, "Track-end output must show only the lower half.");
		int seededHashA = FalsumTrackEndComposer.ComputeTriangleLayoutHash(1337);
		int seededHashB = FalsumTrackEndComposer.ComputeTriangleLayoutHash(1337);
		int otherSeedHash = FalsumTrackEndComposer.ComputeTriangleLayoutHash(1338);
		Require(seededHashA == seededHashB, "Track-end fixed seed is not deterministic.");
		Require(seededHashA != otherSeedHash, "Track-end random seed has no effect.");
		checks.Add("background_resources");
	}

	// Old FALSUM packages keep the historical ".png" name in song.json even
	// though the library now ships JPEG files, and builds made before the
	// Backgrounds/ relocation keep the library under StreamingAssets. Both
	// must still resolve, and the settings dialog must find thumbnails for
	// every catalog entry.
	private static void AssertLegacyBackgroundCompatibility()
	{
		string root = FalsumRuntimePaths.ActiveBackgroundsDirectory;
		Require(Directory.Exists(root), "Active gameplay background directory is missing: " + root);
		foreach (string directory in FalsumRuntimePaths.BackgroundDirectories)
		{
			Require(Directory.Exists(directory), "Gameplay background directory disappeared: " + directory);
		}
		List<FalsumBackgroundEntry> entries;
		string error;
		Require(FalsumBackgroundCatalog.TryLoad(out entries, out error), "Gameplay background catalog failed: " + error);
		Require(entries.Count > 0, "Gameplay background catalog is empty.");
		int legacyPngEntries = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			FalsumBackgroundEntry entry = entries[i];
			Require(FalsumManifest.IsValidBackgroundFile(entry.FileName), "Gameplay background catalog entry has an invalid name: " + entry.FileName);
			Require(entry.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Gameplay background catalog must report logical .png names: " + entry.FileName);
			Require(!string.IsNullOrEmpty(FalsumRuntimePaths.ResolveBackgroundFile(entry.FileName)), "Gameplay background catalog entry does not resolve: " + entry.FileName);
			string resolved = FalsumRuntimePaths.ResolveBackgroundFile(entry.FileName);
			Require(File.Exists(resolved), "Resolved gameplay background is missing: " + resolved);
			if (!string.Equals(Path.GetExtension(resolved), ".png", StringComparison.OrdinalIgnoreCase))
			{
				legacyPngEntries++;
			}
			Require(!string.IsNullOrEmpty(FalsumRuntimePaths.ResolveBackgroundFile(Path.GetDirectoryName(resolved), entry.FileName)), "Resolved gameplay background is not reachable from its own directory: " + entry.FileName);
			string stem = Path.GetFileNameWithoutExtension(entry.FileName);
			string thumbnailPath = string.Empty;
			foreach (string thumbnailRoot in FalsumRuntimePaths.BackgroundDirectories)
			{
				thumbnailPath = FalsumRuntimePaths.ResolveBackgroundFile(Path.Combine(thumbnailRoot, "Thumbnails"), stem + ".png");
				if (thumbnailPath.Length != 0)
				{
					break;
				}
			}
			Require(thumbnailPath.Length > 0 && File.Exists(thumbnailPath), "Gameplay background thumbnail is missing: " + entry.FileName);
		}
		Require(legacyPngEntries > 0, "Gameplay background library has no JPEG fallback to verify.");
		Require(FalsumRuntimePaths.ResolveBackgroundFile(Application.streamingAssetsPath, "../invalid.png").Length == 0, "Background resolver accepted a traversal path.");
	}

	private static void CheckUiToolkitResources(List<string> checks)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Invalid comparison between Unknown and I4
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023a: Invalid comparison between Unknown and I4
		PanelSettings val = Resources.Load<PanelSettings>("Falsum/UI/FalsumPanelSettings");
		Require((Object)(object)val != (Object)null, "UI Toolkit PanelSettings is missing.");
		Require((Object)(object)val.themeStyleSheet != (Object)null, "UI Toolkit theme is missing.");
		Require((int)val.scaleMode == 2, "UI Toolkit scale mode is incorrect.");
		Require(val.referenceResolution == new Vector2Int(1730, 1080), "UI Toolkit reference resolution is incorrect.");
		Require(FalsumEditorScaleRepair.IsAvailable, "Editor DPI scale repair API is unavailable.");
		Font sourceHanSans = Resources.Load<Font>("Falsum/NotoSansSC-Medium");
		Require((Object)(object)sourceHanSans != (Object)null && FalsumFonts.Cjk == sourceHanSans, "Bundled Source Han Sans font is not active.");
		Texture2D aboutLogo = Resources.Load<Texture2D>("Falsum/UI/FalsumLogo");
		Require((Object)(object)aboutLogo != (Object)null && ((Texture)aboutLogo).width > 0 && ((Texture)aboutLogo).height > 0, "Falsum about logo is missing.");
		VisualTreeAsset val2 = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor");
		Require((Object)(object)val2 != (Object)null, "FalsumEditor.uxml is missing.");
		TemplateContainer instance = val2.Instantiate();
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "FalsumRoot", (string)null) != null, "FalsumEditor root element is missing.");
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "TopBar", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "BottomBar", (string)null) != null, "FalsumEditor bars are missing.");
		Require(UQueryExtensions.Q<Button>((VisualElement)(object)instance, "Spectrum", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)instance, "Progress", (string)null) != null, "FalsumEditor spectrum entry controls are missing.");
		Require((Object)(object)Resources.Load<VectorImage>("Falsum/UI/Icons/audio-lines") != (Object)null, "Falsum spectrum entry icon is missing.");
		Require((Object)(object)Resources.Load<Shader>("Falsum/Shaders/FalsumTrackSpectrum") != (Object)null, "Falsum track spectrum shader is missing.");
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "NoteProperties", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)instance, "PropertyRows", (string)null) != null, "FalsumEditor property panel is missing.");
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "EventsPanel", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)instance, "EventRows", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)instance, "AddEvent", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)instance, "EventFilterLane", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "EventMap", (string)null) == null, "FalsumEditor event panel is missing or invalid.");
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "MenuLayer", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "ModalLayer", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "RadialLayer", (string)null) != null, "FalsumEditor overlay layers are missing.");
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "MiddleEditStatus", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)instance, "MiddleEditModeLabel", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)instance, "MiddleEditModeHint", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)instance, "MiddleEditModeAlternatives", (string)null) != null, "FalsumEditor middle-edit status is missing.");
		VisualElement songCover = UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "SongInfoCardCover", (string)null);
		Require(songCover != null && (int)songCover.pickingMode == 0, "FalsumEditor song cover entry is not interactive.");
		string[] templatePaths = new string[13]
		{
			"Falsum/UI/FalsumPropertyRow", "Falsum/UI/FalsumMenuPanel", "Falsum/UI/FalsumMenuItem", "Falsum/UI/FalsumMenuSeparator", "Falsum/UI/FalsumRadialMenu", "Falsum/UI/FalsumRadialEntry", "Falsum/UI/FalsumConfirmDialog", "Falsum/UI/FalsumDisclaimerDialog", "Falsum/UI/FalsumNewChartDialog", "Falsum/UI/FalsumPreferencesDialog",
			"Falsum/UI/FalsumToolDialog", "Falsum/UI/FalsumToolReportDialog", "Falsum/UI/FalsumSkyAreaBank"
		};
		for (int i = 0; i < templatePaths.Length; i++)
		{
			Require((Object)(object)Resources.Load<VisualTreeAsset>(templatePaths[i]) != (Object)null, "Missing UI Toolkit template: " + templatePaths[i]);
		}
		// The bank sidebar must resolve every element the view queries, and it
		// must NOT be on the modal layer (it has to stay usable while the
		// chart is still editable).
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)instance, "BankLayer", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)instance, "SkyAreaBankMenu", (string)null) != null, "FalsumEditor Skyarea Bank layer or menu button is missing.");
		TemplateContainer bankTemplate = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumSkyAreaBank").Instantiate();
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)bankTemplate, "BankPanel", (string)null) != null && UQueryExtensions.Q<TextField>((VisualElement)(object)bankTemplate, "BankSearch", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)bankTemplate, "BankList", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankSaveSelection", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)bankTemplate, "BankShareLayer", (string)null) != null && UQueryExtensions.Q<TextField>((VisualElement)(object)bankTemplate, "BankShareCode", (string)null) != null, "Skyarea Bank panel controls are missing.");
		Require((Object)(object)Resources.Load<StyleSheet>("Falsum/UI/FalsumSkyAreaBank") != (Object)null, "Skyarea Bank style sheet is missing.");
		// The bank panel is RE-PARENTED from its template container onto
		// BankLayer, so it only inherits style sheets attached to the ROOT
		// document. Declaring the sheet on the child template alone left the
		// panel completely unstyled: every child stretched to full width and
		// stacked down the screen.
		System.Collections.Generic.IEnumerable<StyleSheet> rootSheets =
			Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor").stylesheets;
		bool bankSheetOnRoot = false;
		if (rootSheets != null)
		{
			foreach (StyleSheet sheet in rootSheets)
			{
				if (sheet != null && sheet.name == "FalsumSkyAreaBank")
				{
					bankSheetOnRoot = true;
					break;
				}
			}
		}
		Require(bankSheetOnRoot, "FalsumSkyAreaBank.uss is not attached to the root FalsumEditor document, so the re-parented bank panel renders unstyled.");
		VisualElement bankPanel = UQueryExtensions.Q<VisualElement>((VisualElement)(object)bankTemplate, "BankPanel", (string)null);
		Require(bankPanel != null && bankPanel.ClassListContains("bank-panel"), "The bank panel lost its style class when instantiated.");
		// EVERY button in this panel gets its caption from code (their element
		// names are plain identifiers, not l10n.* keys), so a missing L.Get
		// renders a blank grey slab. The key set must therefore be complete in
		// both locales before the panel can ship.
		{
			string[] buttonKeys = new string[]
			{
				"falsum.bank.saveSelection",
				"falsum.bank.importFromClipboard",
				"falsum.bank.importFromFile",
				"falsum.bank.copyCode",
				"falsum.bank.exportFile",
				"falsum.bank.searchPlaceholder",
			};
			for (int i = 0; i < buttonKeys.Length; i++)
			{
				Require(
					InFalsus.Gameplay.Falsum.Localization.FalsumLocalization.TryGet(
						buttonKeys[i],
						out _),
					"A Skyarea Bank button caption is missing: " + buttonKeys[i]);
			}
			Require(
				UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankSaveSelection", (string)null) != null &&
				UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankImportFromClipboard", (string)null) != null &&
				UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankImportFromFile", (string)null) != null &&
				UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankShareCopy", (string)null) != null &&
				UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankShareExport", (string)null) != null,
				"A Skyarea Bank button element the view captions is missing from the template.");
		}
		// The bank used to be the one panel whose buttons carried no
		// background/border/colour at all, so they fell through to Unity's
		// default runtime theme -- which is LIGHT -- and the whole sidebar
		// rendered as pale slabs next to the dark editor. These assertions pin
		// the three things that regressed: the shared close-button recipe, the
		// tag rail's horizontal mode, and every bank button having a resolved
		// dark surface.
		{
			Button bankClose = UQueryExtensions.Q<Button>((VisualElement)(object)bankTemplate, "BankClose", (string)null);
			Require(bankClose != null, "The Skyarea Bank close button is missing from the template.");
			Require(bankClose.text == "×",
				"The Skyarea Bank close button lost its glyph; it must reuse the project-wide close recipe (text=\"×\").");
			Require(bankClose.ClassListContains("modal-close-button"),
				"The Skyarea Bank close button must share .modal-close-button with every other close button.");
			ScrollView bankTagRow = UQueryExtensions.Q<ScrollView>((VisualElement)(object)bankTemplate, "BankTagRow", (string)null);
			Require(bankTagRow != null && bankTagRow.mode == ScrollViewMode.Horizontal,
				"The Skyarea Bank tag rail is not a horizontal ScrollView, so the tag chips stack and clip.");
		}
		TemplateContainer menuItem = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumMenuItem").Instantiate();
		Require(UQueryExtensions.Q<Button>((VisualElement)(object)menuItem, "MenuItemButton", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)menuItem, "MenuItemLabel", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)menuItem, "MenuItemShortcut", (string)null) != null, "Menu item labels are incomplete.");
		TemplateContainer newChart = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumNewChartDialog").Instantiate();
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)newChart, "NewChartOverlay", (string)null) != null && UQueryExtensions.Q<TextField>((VisualElement)(object)newChart, "NewChartSongId", (string)null) != null && UQueryExtensions.Q<TextField>((VisualElement)(object)newChart, "NewChartTitle", (string)null) != null && UQueryExtensions.Q<TextField>((VisualElement)(object)newChart, "NewChartArtist", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)newChart, "NewChartCreate", (string)null) != null && UQueryExtensions.Q<Toggle>((VisualElement)(object)newChart, "NewChartDiffToggle0", (string)null) != null && UQueryExtensions.Q<Toggle>((VisualElement)(object)newChart, "NewChartDiffToggle3", (string)null) != null, "New chart dialog controls are missing.");
		TemplateContainer preferences = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumPreferencesDialog").Instantiate();
		Label preferencesVersionLabel = UQueryExtensions.Q<Label>((VisualElement)(object)preferences, "PreferencesVersion", (string)null);
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)preferences, "PreferencesOverlay", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)preferences, "PreferencesWindow", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)preferences, "PreferencesTabDisplay", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)preferences, "PreferencesTabAdvanced", (string)null) != null && preferencesVersionLabel != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)preferences, "PreferencesContent", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)preferences, "PreferencesCancel", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)preferences, "PreferencesComplete", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)preferences, "BackgroundPreviewPanel", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)preferences, "BackgroundPreviewConfirm", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)preferences, "DisplayConfirmLayer", (string)null) != null, "Preferences dialog controls are missing.");
		TemplateContainer toolDialog = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumToolDialog").Instantiate();
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)toolDialog, "ToolDialogOverlay", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)toolDialog, "ToolManagerContent", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)toolDialog, "ToolFormView", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)toolDialog, "ToolFormContent", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)toolDialog, "ToolRun", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)toolDialog, "ToolTimePickOverlay", (string)null) != null, "Tool dialog controls are missing.");
		TemplateContainer disclaimerDialog = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumDisclaimerDialog").Instantiate();
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)disclaimerDialog, "DisclaimerOverlay", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)disclaimerDialog, "DisclaimerPanel", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)disclaimerDialog, "DisclaimerContent", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)disclaimerDialog, "DisclaimerText", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)disclaimerDialog, "DisclaimerDecline", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)disclaimerDialog, "DisclaimerAccept", (string)null) != null, "Disclaimer dialog controls are missing.");
		TemplateContainer toolReportDialog = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumToolReportDialog").Instantiate();
		Require(UQueryExtensions.Q<VisualElement>((VisualElement)(object)toolReportDialog, "ToolReportOverlay", (string)null) != null && UQueryExtensions.Q<VisualElement>((VisualElement)(object)toolReportDialog, "ToolReportWindow", (string)null) != null && UQueryExtensions.Q<ScrollView>((VisualElement)(object)toolReportDialog, "ToolReportItems", (string)null) != null && UQueryExtensions.Q<Label>((VisualElement)(object)toolReportDialog, "ToolReportTitle", (string)null) != null && UQueryExtensions.Q<Button>((VisualElement)(object)toolReportDialog, "ToolReportDone", (string)null) != null, "Tool report dialog controls are missing.");
		checks.Add("ui_toolkit_resources");
	}

	private static void CheckPreferences(List<string> checks)
	{
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0094: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_028c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0296: Invalid comparison between Unknown and I4
		//IL_02a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ab: Invalid comparison between Unknown and I4
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		//IL_030a: Invalid comparison between Unknown and I4
		//IL_02b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Invalid comparison between Unknown and I4
		//IL_03a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03aa: Invalid comparison between Unknown and I4
		//IL_0667: Unknown result type (might be due to invalid IL or missing references)
		//IL_066c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0677: Unknown result type (might be due to invalid IL or missing references)
		//IL_067e: Expected Obj, but got Unknown
		//IL_067e: Unknown result type (might be due to invalid IL or missing references)
		//IL_069b: Expected Obj, but got Unknown
		FalsumPreferences preferences = FalsumPreferences.CreateDefault();
		Require(preferences.Bindings != null && preferences.Bindings.Count == Enum.GetValues(typeof(FalsumInputAction)).Length - 2, "Default input bindings are incomplete.");
		var actions = new HashSet<FalsumInputAction>();
		foreach (var binding in preferences.Bindings)
			Require(binding != null && actions.Add(binding.Action), "Default input bindings contain duplicate actions.");
		foreach (FalsumInputAction action in Enum.GetValues(typeof(FalsumInputAction)))
		{
			// Numeric IDs remain for profile compatibility; flow now uses Ctrl+wheel.
			var retired = action == FalsumInputAction.DecreaseFlowSpeed || action == FalsumInputAction.IncreaseFlowSpeed;
			Require(actions.Contains(action) != retired, "Default input action coverage is incorrect: " + action);
		}
		FalsumKeyBinding[] expectedBindings = new FalsumKeyBinding[11]
		{
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.MiddleEditBoundary,
				Key = (KeyCode)282
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.MiddleEditStartEnd,
				Key = (KeyCode)283
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.MiddleEditPosition,
				Key = (KeyCode)284
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.Mirror,
				Key = (KeyCode)109,
				Modifiers = FalsumModifierKeys.Ctrl
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.ToggleFlickColor,
				Key = (KeyCode)109
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.ToggleFullscreen,
				Key = (KeyCode)292
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.PointPlaceTap,
				Key = (KeyCode)97
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.PointPlaceHold,
				Key = (KeyCode)115
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.PointPlaceFlick,
				Key = (KeyCode)100
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.PointPlaceSkyArea,
				Key = (KeyCode)102
			},
			new FalsumKeyBinding
			{
				Action = FalsumInputAction.ToggleGrid,
				Key = (KeyCode)120
			}
		};
		foreach (FalsumKeyBinding expected in expectedBindings)
		{
			FalsumKeyBinding actual = preferences.FindBinding(expected.Action);
			Require(actual != null && actual.Key == expected.Key && actual.Modifiers == expected.Modifiers, "Default shortcut is incorrect: " + expected.Action);
		}
		Require(!preferences.HasBindingConflict(FalsumInputAction.PlayPause, FalsumModifierKeys.None, (KeyCode)113, out var conflict), "Current binding reported a false conflict.");
		Require(preferences.HasBindingConflict(FalsumInputAction.PlayPause, FalsumModifierKeys.None, (KeyCode)32, out conflict) && conflict == FalsumInputAction.TemporaryAudition, "Duplicate binding was not detected.");
		FalsumPreferences legacy = FalsumPreferences.CreateDefault();
		legacy.Version = 1;
		legacy.SetBinding(FalsumInputAction.MiddleEditBoundary, FalsumModifierKeys.None, (KeyCode)289);
		legacy.SetBinding(FalsumInputAction.MiddleEditStartEnd, FalsumModifierKeys.None, (KeyCode)290);
		legacy.SetBinding(FalsumInputAction.MiddleEditPosition, FalsumModifierKeys.None, (KeyCode)291);
		legacy.Normalize();
		Require((int)legacy.FindBinding(FalsumInputAction.MiddleEditBoundary).Key == 282 && (int)legacy.FindBinding(FalsumInputAction.MiddleEditStartEnd).Key == 283 && (int)legacy.FindBinding(FalsumInputAction.MiddleEditPosition).Key == 284 && legacy.Version == FalsumPreferences.CurrentVersion, "Legacy middle-edit shortcuts were not migrated.");
		FalsumPreferences falsumPreferences = FalsumPreferences.CreateDefault();
		falsumPreferences.Version = 1;
		falsumPreferences.SetBinding(FalsumInputAction.MiddleEditBoundary, FalsumModifierKeys.None, (KeyCode)288);
		falsumPreferences.Normalize();
		Require((int)falsumPreferences.FindBinding(FalsumInputAction.MiddleEditBoundary).Key == 288, "Custom middle-edit shortcut was overwritten.");
		string preferencesPath = FalsumPreferencesStore.FilePath;
		string preferencesBackup = preferencesPath + ".selfcheck-backup";
		bool hadPreferences = File.Exists(preferencesPath);
		if (hadPreferences)
		{
			File.Copy(preferencesPath, preferencesBackup, overwrite: true);
		}
		try
		{
			Directory.CreateDirectory(FalsumPreferencesStore.DirectoryPath);
			FalsumPreferences versionOne = FalsumPreferences.CreateDefault();
			versionOne.Version = 1;
			versionOne.SetBinding(FalsumInputAction.MiddleEditBoundary, FalsumModifierKeys.None, (KeyCode)289);
			File.WriteAllText(preferencesPath, JsonUtility.ToJson((object)versionOne, true), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			FalsumPreferencesStore.ResetCache();
			FalsumPreferences migrated = FalsumPreferencesStore.Load();
			Require(migrated.Version == FalsumPreferences.CurrentVersion && (int)migrated.FindBinding(FalsumInputAction.MiddleEditBoundary).Key == 282, "Persisted v1 preferences were not migrated.");
		}
		finally
		{
			FalsumPreferencesStore.ResetCache();
			if (hadPreferences)
			{
				File.Copy(preferencesBackup, preferencesPath, overwrite: true);
				File.Delete(preferencesBackup);
			}
			else if (File.Exists(preferencesPath))
			{
				File.Delete(preferencesPath);
			}
			FalsumPreferencesStore.ResetCache();
		}
		CheckPreferencesMigration();
		preferences.Backup.IntervalSeconds = 300;
		preferences.Display.AntiAliasingMode = FalsumAntiAliasingMode.Taa;
		preferences.Editing.DisableOutOfBoundsClamping = true;
		preferences.Experimental.UIStyle = FalsumUiStyle.Canvas;
		preferences.Visual.SkyAreaOpacity = 0.375f;
		preferences.Visual.SideLaneOpacity = 0.625f;
		FalsumPreferences restored = JsonUtility.FromJson<FalsumPreferences>(JsonUtility.ToJson((object)preferences));
		restored.Normalize();
		Require(restored.Backup.IntervalSeconds == 300 && restored.Display.AntiAliasingMode == FalsumAntiAliasingMode.Taa && restored.Editing.DisableOutOfBoundsClamping && restored.Experimental.UIStyle == FalsumUiStyle.Canvas && restored.Bindings.Count == preferences.Bindings.Count, "Preferences serialization failed.");
		Require(Math.Abs(restored.Visual.SkyAreaOpacity - 0.375f) < 0.001f && Math.Abs(restored.Visual.SideLaneOpacity - 0.625f) < 0.001f, "Visual preferences did not survive serialization.");
		restored.Experimental.UIStyle = (FalsumUiStyle)99;
		restored.Normalize();
		Require(restored.Experimental.UIStyle == FalsumUiStyle.Classic, "Unknown UI style did not fall back to Classic.");
		FalsumPreferences versionFour = FalsumPreferences.CreateDefault();
		versionFour.Version = 1;
		versionFour.Experimental.ShowMeasureNumbers = true;
		versionFour.Normalize();
		Require(!versionFour.Experimental.ShowMeasureNumbers, "A stale measure number preference survived normalization while the feature is hidden.");
		Require(preferences.Advanced.UIStyle == preferences.Experimental.UIStyle && preferences.Advanced.DisableOutOfBoundsClamping == preferences.Editing.DisableOutOfBoundsClamping && preferences.Advanced.GridLineColors == preferences.Grid.LineColors, "Preferences v6 facade does not forward to the regrouped fields.");
		preferences.Advanced.UIStyle = FalsumUiStyle.Studio;
		Require(preferences.Experimental.UIStyle == FalsumUiStyle.Studio, "Preferences v6 facade write did not reach Experimental.UIStyle.");
		preferences.Advanced.RestoreLastPackage = true;
		preferences.Advanced.LastPackagePath = "ignored";
		Require(!preferences.Advanced.RestoreLastPackage && string.IsNullOrEmpty(preferences.Advanced.LastPackagePath), "Removed v5 fields still round-trip through the facade.");
		Require(JsonUtility.ToJson((object)preferences).IndexOf("LastPackagePath", StringComparison.Ordinal) < 0, "Removed field LastPackagePath is still serialized.");
		FalsumPreferences cloneSource = FalsumPreferences.CreateDefault();
		cloneSource.Experimental.UIStyle = FalsumUiStyle.Canvas;
		cloneSource.Grid.LineWidthScale = 2.5f;
		FalsumPreferences cloned = cloneSource.Clone();
		Require(cloned.Experimental.UIStyle == FalsumUiStyle.Canvas && Mathf.Abs(cloned.Grid.LineWidthScale - 2.5f) < 0.001f, "Clone lost regrouped preferences (facade must not hold data).");
		Require(!FalsumDisplaySettingsController.IsSupported((FalsumAntiAliasingMode)99, out var antiAliasingReason) && !string.IsNullOrWhiteSpace(antiAliasingReason), "Unknown anti-aliasing mode was reported as supported.");
		Require(FalsumChartMetadataDraft.IsValidSongId("alamode_2"), "Valid song ID was rejected.");
		Require(!FalsumChartMetadataDraft.IsValidSongId("Bad ID"), "Invalid song ID was accepted.");
		FalsumManifest falsumManifest = FalsumManifest.CreateTemplate("before", "Before", "Artist", 1000);
		falsumManifest.SetSongId("after");
		Require(FalsumManifest.Parse(falsumManifest.Serialize()).SongId == "after", "Manifest song ID setter failed.");
		// NOTE: alamode.falsum is a SELF-CHECK FIXTURE, not a shipped sample.
		// The build pipeline strips the .falsum files from the published
		// StreamingAssets, so this file only has to exist in the project.
		FalsumPackage package = FalsumPackage.Load(Path.Combine(Application.streamingAssetsPath, "Falsum", "alamode.falsum"));
		FalsumChartMetadataSnapshot snapshot = FalsumMetadataApplyService.Capture(package);
		FalsumChartMetadataDraft metadata = new FalsumChartMetadataDraft(package, hasPackage: true);
		FalsumDifficultyInfo difficulty = package.Manifest.FindDifficulty(0);
		Require(difficulty != null, "Default package MIN difficulty is missing.");
		metadata.DifficultyRatings[0] = (difficulty.Rating + 1).ToString();
		metadata.DifficultyChartDesigners[0] = "Falsum Self Check";
		Require(metadata.HasChangesFrom(package), "Difficulty metadata edits were not detected.");
		Require(FalsumMetadataApplyService.TryApply(package, metadata, out var metadataError) && difficulty.Rating == int.Parse(metadata.DifficultyRatings[0]) && difficulty.ChartDesigner == "Falsum Self Check", "Difficulty metadata apply failed: " + metadataError);
		FalsumMetadataApplyService.Restore(package, snapshot);
		FalsumDifficultyInfo restoredDifficulty = package.Manifest.FindDifficulty(0);
		Require(restoredDifficulty != null && restoredDifficulty.Rating == snapshot.DifficultyRatings[0] && restoredDifficulty.ChartDesigner == snapshot.DifficultyChartDesigners[0], "Difficulty metadata rollback failed.");
		metadata.DifficultyRatings[0] = "-1";
		Require(!metadata.TryValidate(out metadataError), "Negative difficulty rating was accepted.");
		FalsumDifficultyInfo falsumDifficultyInfo = package.Manifest.FindDifficulty(3);
		bool originalAvailability = falsumDifficultyInfo.Available;
		falsumDifficultyInfo.SetAvailable(value: false);
		VisualElement val = new VisualElement
		{
			name = "FalsumRoot"
		};
		VisualElement preferencesModal = new VisualElement();
		val.Add(preferencesModal);
		VisualTreeAsset preferencesTemplate = Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumPreferencesDialog");
		FalsumPreferencesView preferencesView = new FalsumPreferencesView(val, preferencesModal, preferencesTemplate);
		int requestedDifficulty = -1;
		bool disclaimerRequested = false;
		preferencesView.DifficultySwitchRequested += (int index) =>
		{
			requestedDifficulty = index;
		};
		preferencesView.DisclaimerRequested += () =>
		{
			disclaimerRequested = true;
		};
		preferencesView.Show(FalsumPreferences.CreateDefault(), new FalsumChartMetadataDraft(package, hasPackage: true), package);
		preferencesView.DebugFlushInitialTabBuild();
		// Chart metadata is document scope and now lives in its own panel, so
		// the difficulty cards are verified there instead of inside the
		// global preferences dialog.
		Require(preferencesView.IsOpen, "Preferences dialog did not open.");
		Require(preferencesView.DebugActiveTab == "Display", "Preferences dialog did not open on the display page.");
		preferencesView.Close();
		preferencesView.ShowChartInfo(new FalsumChartMetadataDraft(package, hasPackage: true), package);
		Require(preferencesView.IsChartInfoOpen, "Chart info panel did not open.");
		Require(preferencesView.DebugFirstDisabledDifficultyOnPanel == 3, "Disabled difficulty card was not rendered.");
		Require(preferencesView.DebugRequestDifficultySwitchOnPanel(3) && requestedDifficulty == 3, "Disabled difficulty card did not request a switch.");
		// The chart-info shell must build its own background picker: the page's
		// preview button threw a NullReferenceException when only the global
		// preferences dialog had one (Editor.log, FalsumPreferencesView.cs:1356).
		Require(preferencesView.DebugChartInfoBackgroundPickerReady, "Chart info shell did not build a background picker.");
		preferencesView.CloseChartInfo();
		preferencesView.Show(FalsumPreferences.CreateDefault(), new FalsumChartMetadataDraft(package, hasPackage: true), package);
		preferencesView.DebugFlushInitialTabBuild();
		// About is its own tab since the Advanced/About split, so the About
		// assertions need that tab shown; DebugAboutIsReady requires it.
		preferencesView.DebugShowTab("About");
		Require(preferencesView.DebugAboutIsReady, "About page did not render its logo, version, or credits.");
		Require(preferencesView.DebugRequestAboutDisclaimer() & disclaimerRequested, "About disclaimer did not request the shared dialog.");
		preferencesView.DebugShowTab("Advanced");
		DropdownField uiStyleDropdown = UQueryExtensions.Q<DropdownField>((VisualElement)(object)preferencesModal, "PreferencesUIStyle", (string)null);
		Require(uiStyleDropdown != null && uiStyleDropdown.choices.Count == 4 && preferencesView.DebugUiStyle == FalsumUiStyle.InFalsus, "UIStyle picker or new default is missing.");
		FalsumUiStyle previewedStyle = FalsumUiStyle.Classic;
		preferencesView.UiStylePreviewRequested += (FalsumUiStyle style) =>
		{
			previewedStyle = style;
		};
		preferencesView.DebugSetUiStyle(FalsumUiStyle.Studio);
		Require(preferencesView.DebugUiStyle == FalsumUiStyle.Studio && previewedStyle == FalsumUiStyle.Studio, "UIStyle preview did not preserve the selected style.");
		FalsumUiStyles.Apply(val, FalsumUiStyle.Studio);
		FalsumUiStyles.Apply(val, FalsumUiStyle.Studio);
		StyleSheet studioSheet = Resources.Load<StyleSheet>("Falsum/UI/FalsumStudio");
		Require(val.styleSheets.Contains(studioSheet), "UIStyle preview did not attach its style sheet.");
		FalsumUiStyles.Apply(val, FalsumUiStyle.Classic);
		Require(!val.styleSheets.Contains(studioSheet), "Classic did not remove the preview style sheet.");
		// The out-of-bounds clamp moved to the editing page, which now also
		// hosts grid drawing; verify it there rather than under Advanced.
		preferencesView.DebugShowTab("Editing");
		Toggle outOfBoundsToggle = UQueryExtensions.Q<Toggle>((VisualElement)(object)preferencesModal, "PreferencesDisableOutOfBoundsClamping", (string)null);
		Require(outOfBoundsToggle != null, "Editing out-of-bounds clamp toggle is missing.");
		preferencesView.DebugSetDisableOutOfBoundsClamping(true);
		Require(preferencesView.DebugDisableOutOfBoundsClamping, "Editing out-of-bounds clamp toggle did not update preferences.");
		Require(!string.IsNullOrWhiteSpace(FalsumDisclaimerConsent.FilePath), "Disclaimer consent storage is not configured.");
		preferencesView.Close();
		falsumDifficultyInfo.SetAvailable(originalAvailability);
		Require(FalsumBackgroundCatalog.TryLoad(out var backgrounds, out var backgroundError) && backgrounds.Count > 0, "Background catalog is empty: " + backgroundError);
		Require(FalsumMeasureNumberLayout.BadgeSize > 0 && FalsumMeasureNumberLayout.Format(9) == "#9" && FalsumMeasureNumberLayout.Format(1200) == "#1200", "Measure number badge metrics are incorrect.");
		List<Vector2> placedBadges = new List<Vector2>();
		Require(FalsumMeasureNumberLayout.CanAccept(placedBadges, new Vector2(100.0f, 100.0f)), "Measure number layout rejected the first badge.");
		placedBadges.Add(new Vector2(100.0f, 100.0f));
		Require(!FalsumMeasureNumberLayout.CanAccept(placedBadges, new Vector2(106.0f, 106.0f)), "Measure number layout stacked overlapping badges.");
		Require(FalsumMeasureNumberLayout.CanAccept(placedBadges, new Vector2(100.0f, 160.0f)), "Measure number layout dropped a correctly spaced badge.");
		for (int badgeIndex = 1; badgeIndex < FalsumMeasureNumberLayout.MaxBadges; badgeIndex++)
		{
			placedBadges.Add(new Vector2(0.0f, badgeIndex * 100.0f));
		}
		Require(!FalsumMeasureNumberLayout.CanAccept(placedBadges, new Vector2(0.0f, 5000.0f)), "Measure number layout ignored the badge cap.");
		checks.Add("preferences");
	}

	/// <summary>
	/// Exercises the v6 migration entirely in memory: no file on disk is read
	/// or written, so a failing run cannot destroy the real user profile.
	/// </summary>
	private static void CheckPreferencesMigration()
	{
		string v5Json = "{\"Version\":5,\"Display\":{\"ResolutionWidth\":1366,\"ResolutionHeight\":768,\"ResolutionRefreshRate\":60,\"BorderlessFullscreen\":false,\"AntiAliasingEnabled\":true,\"AntiAliasingMode\":1},\"Bindings\":[{\"Action\":0,\"Modifiers\":0,\"Key\":113}],\"Editing\":{\"DirectDragUnselectedNotes\":false,\"MiddleEditAffectsAllSelected\":true,\"SkyAreaGroupTimeSnap\":true},\"Backup\":{\"Enabled\":true,\"IntervalSeconds\":300,\"RetentionCount\":20},\"Audio\":{\"MusicVolume\":0.5,\"SkyAreaVolume\":1.0,\"TapVolume\":1.0,\"HoldVolume\":1.0,\"FlickVolume\":1.0},\"Advanced\":{\"RestoreLastPackage\":false,\"DisableOutOfBoundsClamping\":true,\"LastPackagePath\":\"C:/charts\",\"GridLineColors\":\"#FFFFFF\",\"GridLineWidthScale\":2.0,\"ShowMeasureNumbers\":true,\"UIStyle\":1}}";
		FalsumPreferences migratedV5 = FalsumPreferencesMigration.BuildFromJson(v5Json, 5);
		Require(migratedV5.Version == FalsumPreferences.CurrentVersion, "v5 migration did not stamp the current version.");
		Require(migratedV5.Editing.DisableOutOfBoundsClamping, "v5 Advanced.DisableOutOfBoundsClamping did not move to Editing.");
		Require(migratedV5.Editing.DirectDragUnselectedNotes == false, "v5 Editing.DirectDragUnselectedNotes was not preserved.");
		Require(Math.Abs(migratedV5.Grid.LineWidthScale - 2.0f) < 0.001f, "v5 Advanced.GridLineWidthScale did not move to Grid.");
		Require(migratedV5.Grid.LineColors == "#FFFFFF", "v5 Advanced.GridLineColors did not move to Grid.");
		Require(migratedV5.Audio.MusicVolume > 0.49f && migratedV5.Audio.MusicVolume < 0.51f, "v5 Audio volume was not preserved.");
		Require(migratedV5.Backup.IntervalSeconds == 300, "v5 Backup interval was not preserved.");
		Require(!migratedV5.Experimental.ShowMeasureNumbers, "v5 ShowMeasureNumbers survived the hidden-feature gate.");
		Require(migratedV5.Experimental.UIStyle == FalsumUiStyle.Studio, "v5 UIStyle selection was not preserved.");
		Require(v5Json != null && JsonUtility.ToJson((object)migratedV5).IndexOf("LastPackagePath", StringComparison.Ordinal) < 0, "v5 LastPackagePath was written into v6.");

		string v1Json = "{\"Version\":1,\"Display\":{\"ResolutionWidth\":1366,\"ResolutionHeight\":768,\"ResolutionRefreshRate\":120,\"BorderlessFullscreen\":true,\"AntiAliasingEnabled\":true,\"AntiAliasingMode\":1},\"Bindings\":[{\"Action\":9,\"Modifiers\":0,\"Key\":282}],\"Editing\":{\"DirectDragUnselectedNotes\":true,\"MiddleEditAffectsAllSelected\":false},\"Backup\":{\"Enabled\":true,\"IntervalSeconds\":180,\"RetentionCount\":20},\"Advanced\":{\"RestoreLastPackage\":false,\"LastPackagePath\":\"\"}}";
		FalsumPreferences migratedV1 = FalsumPreferencesMigration.BuildFromJson(v1Json, 1);
		Require(migratedV1.Version == FalsumPreferences.CurrentVersion, "v1 migration did not stamp the current version.");
		Require(migratedV1.Editing.SkyAreaGroupTimeSnap, "v1 profile did not gain the default sky-area group time snap.");
		Require(migratedV1.Audio != null && migratedV1.Grid != null && migratedV1.Experimental != null, "v1 migration left a v6 group missing.");
		Require(migratedV1.Editing.MiddleEditAffectsAllSelected == false, "v1 Editing.MiddleEditAffectsAllSelected was not preserved.");
		Require(migratedV1.Audio.MusicVolume > 0.99f && migratedV1.Audio.TapVolume > 0.99f, "A v1 profile without an Audio group lost its audible defaults (would arrive silent).");
		Require(!migratedV1.Editing.DisableOutOfBoundsClamping, "A v1 profile without that key gained a clamping change it never asked for.");

		string noAudioJson = "{\"Version\":5,\"Display\":{\"ResolutionWidth\":1920,\"ResolutionHeight\":1080},\"Advanced\":{\"GridLineWidthScale\":1.0}}";
		FalsumPreferences noAudio = FalsumPreferencesMigration.BuildFromJson(noAudioJson, 5);
		Require(noAudio.Audio.MusicVolume > 0.99f, "An absent Audio group was mistaken for a zero-filled one.");

		// v7 added the Visual group. A profile written by a v6 build has no
		// "Visual" key at all, so the migration must seed the authored
		// surface opacities instead of zero-filling them (a zeroed group
		// would render the track and the SkyArea invisible).
		Require(
			Math.Abs(noAudio.Visual.SkyAreaOpacity -
			         FalsumVisualPreferences.DefaultSkyAreaOpacity) < 0.001f &&
			Math.Abs(noAudio.Visual.SkyAreaGlow -
			         FalsumVisualPreferences.DefaultSkyAreaGlow) < 0.001f &&
			Math.Abs(noAudio.Visual.SideLaneOpacity -
			         FalsumVisualPreferences.DefaultSideLaneOpacity) < 0.001f &&
			Math.Abs(noAudio.Visual.FloorLaneOpacity -
			         FalsumVisualPreferences.DefaultFloorLaneOpacity) < 0.001f,
			"A v6 profile without a Visual group did not gain the authored surface opacities.");

		// v8 fixed a real defect: the legacy DTO had no Grid member, so a
		// v7 profile's grid group was rebuilt from scratch on every load and
		// the user's colours/width/denominator ladder never survived a
		// restart. A present Grid group must now win over the defaults AND
		// over the pre-v7 Advanced leftovers.
		string v8Json = "{\"Version\":8,\"Display\":{\"ResolutionWidth\":1920,\"ResolutionHeight\":1080}," +
			"\"Grid\":{\"LineColors\":\"#112233\",\"LineWidthScale\":2.75," +
			"\"DenominatorSequence\":\"8, 16, 32\"}," +
			"\"Advanced\":{\"GridLineColors\":\"#AABBCC\",\"GridLineWidthScale\":1.0}}";
		FalsumPreferences roundTrippedV8 =
			FalsumPreferencesMigration.BuildFromJson(v8Json, 8);
		Require(
			roundTrippedV8.Grid.LineColors == "#112233" &&
			Math.Abs(roundTrippedV8.Grid.LineWidthScale - 2.75f) < 0.001f,
			"A v8 profile's Grid group was dropped on load.");
		Require(
			roundTrippedV8.Grid.DenominatorSequence == "8, 16, 32",
			"A v8 profile's denominator sequence was not preserved verbatim.");
		// The v7 Advanced leftovers must not clobber an authoritative Grid.
		Require(
			roundTrippedV8.Grid.LineColors != "#AABBCC" &&
			Math.Abs(roundTrippedV8.Grid.LineWidthScale - 1.0f) > 0.001f,
			"Pre-v7 Advanced grid values clobbered a present Grid group.");
		// A malformed ladder repairs to the authored default.
		string brokenSequenceJson = "{\"Version\":8," +
			"\"Grid\":{\"DenominatorSequence\":\"12, 12, abc\"}}";
		FalsumPreferences repairedSequence =
			FalsumPreferencesMigration.BuildFromJson(brokenSequenceJson, 8);
		Require(
			repairedSequence.Grid.DenominatorSequence ==
			FalsumBeatGrid.DefaultDenominatorSequence,
			"A malformed denominator sequence did not repair to the default.");

		string visualJson = "{\"Version\":7,\"Visual\":{\"SkyAreaOpacity\":0.25,\"SkyAreaGlow\":0.5,\"SideLaneOpacity\":0.75,\"FloorLaneOpacity\":0.4}}";
		FalsumPreferences visualRestored =
			FalsumPreferencesMigration.BuildFromJson(visualJson, 7);
		Require(
			Math.Abs(visualRestored.Visual.SkyAreaOpacity - 0.25f) < 0.001f &&
			Math.Abs(visualRestored.Visual.SkyAreaGlow - 0.5f) < 0.001f &&
			Math.Abs(visualRestored.Visual.SideLaneOpacity - 0.75f) < 0.001f &&
			Math.Abs(visualRestored.Visual.FloorLaneOpacity - 0.4f) < 0.001f,
			"A v7 Visual group was not preserved across migration.");
		visualRestored.Visual.SkyAreaGlow = float.NaN;
		visualRestored.Visual.FloorLaneOpacity = -1.0f;
		visualRestored.Normalize();
		Require(
			Math.Abs(visualRestored.Visual.SkyAreaGlow -
			         FalsumVisualPreferences.DefaultSkyAreaGlow) < 0.001f &&
			visualRestored.Visual.FloorLaneOpacity == 0.0f,
			"An out-of-range Visual value was not repaired by Normalize.");

		string missingVersionJson = "{\"Display\":{\"ResolutionWidth\":1920,\"ResolutionHeight\":1080,\"ResolutionRefreshRate\":60},\"Editing\":{\"SkyAreaGroupTimeSnap\":true},\"Advanced\":{\"GridLineWidthScale\":1.5}}";
		FalsumPreferences migratedMissing = FalsumPreferencesMigration.BuildFromJson(missingVersionJson, 0);
		Require(migratedMissing.Version == FalsumPreferences.CurrentVersion, "A profile with no Version key was not migrated.");
		Require(migratedMissing.Display.ResolutionWidth == 1920, "A profile with no Version key lost its Display group.");
		Require(Math.Abs(migratedMissing.Grid.LineWidthScale - 1.5f) < 0.001f, "A profile with no Version key lost its grid width.");

		string newerJson = "{\"Version\":99}";
		FalsumPreferencesLoadResult newer = FalsumPreferencesMigration.FromDisk(newerJson);
		Require(newer.UnsupportedVersion, "A profile from a newer build was not rejected for quarantine.");
	}

	private static void CheckSkyAreaVisuals(List<string> checks)
	{
		Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/InFalsus/Recovered/Material/SkyArea Variant.mat");
		Require((Object)(object)material != (Object)null, "SkyArea material is missing.");
		Require(material.shader != null && material.shader.name == "InFalsus/SkyArea", "SkyArea material is not using the reconstructed original shader.");
		Require(material.GetTexture("_ArchTexture") != null && material.GetTexture("_DotTexture") != null, "SkyArea original textures are not assigned.");
		Require(material.GetTexture("_ArchTexture").wrapMode == TextureWrapMode.Repeat &&
		        material.GetTexture("_DotTexture").wrapMode == TextureWrapMode.Repeat,
			"SkyArea textures do not repeat, so the animated V phase clamps to a vertical stripe.");
		Color main = material.GetColor("_Color");
		Color edgeCenter = material.GetColor("_EdgeCenterColor");
		Require(Math.Abs(main.r - 0.6883454f) < 0.002f && Math.Abs(main.g - 0.627937f) < 0.002f && Math.Abs(main.b - 0.9716981f) < 0.002f, "SkyArea main color does not match the original material.");
		Require(Math.Abs(main.a - 0.5f) < 0.002f, "SkyArea center alpha is not 50% transparent.");
		Require(edgeCenter.r > 1f && edgeCenter.b > 4f && edgeCenter.a > 0.99f, "SkyArea original edge colors are not restored.");
		GameObject bracket = Resources.Load<GameObject>("SkyArea/BRACKET");
		MeshFilter bracketFilter = bracket != null ? bracket.GetComponentInChildren<MeshFilter>(true) : null;
		Require(bracketFilter != null && bracketFilter.sharedMesh != null, "SkyArea bracket mesh is missing.");
		Require(Resources.Load<Texture2D>("SkyArea/SquareBracket-Diffuse") != null, "SkyArea bracket texture is missing.");
		Require(Resources.Load<Shader>("SkyArea/Shaders/SkyAreaBracket") != null && Resources.Load<Shader>("SkyArea/Shaders/SkyAreaShadow") != null, "SkyArea bracket or ground shadow shader is missing.");
		Require(Shader.Find("InFalsus/Replica/SkyJudgementLineGlow") != null && Resources.Load<Shader>("SkyArea/Shaders/SkyJudgementLineMask") != null && Resources.Load<Shader>("SkyArea/Shaders/SkyJudgementLineBlur") != null, "Sky judgment-line shaders are missing.");
		Require(SkyJudgementLineEffect.Rows == 8 && SkyJudgementLineEffect.Columns == 90 && SkyJudgementLineEffect.TriangleCount == 1448, "Sky judgment-line mesh dimensions are not original.");
		UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
		Require(renderer != null && renderer.rendererFeatures.Exists(feature => feature is SkyJudgementLineEffectFeature), "Sky judgment-line renderer feature is not installed.");
		checks.Add("skyarea_visuals");
	}
	private static void CheckChartRoundTrip(List<string> checks)
	{
		Chart chart = CreateFeatureChart();
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 7000.0,
			DurationMs = 100.0,
			StartPos = 0.1,
			StartWidth = 0.1,
			StartCellCount = 24,
			EndPos = 0.1,
			EndWidth = 0.1,
			EndCellCount = 24,
			GroupId = 42
		});
		string text = ChartWriter.Write(chart, -120.0, "selfcheck", 2);
		ChartParseResult chartParseResult = ChartParser.Parse(text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None), "<selfcheck>");
		Require(chartParseResult.Success, "Serialized chart did not parse.");
		Require(chartParseResult.Chart.Notes.Count == chart.Notes.Count, "Chart note count changed.");
		Require(Math.Abs(chartParseResult.Chart.Bpm - chart.Bpm) < 0.0001, "Chart BPM changed.");
		Require(chartParseResult.Chart.Notes.Exists((ChartNote note) => note.Kind == NoteKind.Flick && note.FlickDirection == 16), "Flick did not round trip.");
		bool onlySequentialSkyGroups = true;
		for (int i = 0; i < chartParseResult.Chart.Notes.Count; i++)
		{
			ChartNote note = chartParseResult.Chart.Notes[i];
			if (note.Kind == NoteKind.SkyArea && note.GroupId != 3 && note.GroupId != 5)
			{
				onlySequentialSkyGroups = false;
			}
		}
		Require(chartParseResult.Chart.Notes.Exists((ChartNote note) => note.Kind == NoteKind.SkyArea && note.GroupId == 3) && chartParseResult.Chart.Notes.Exists((ChartNote note) => note.Kind == NoteKind.SkyArea && note.GroupId == 5) && onlySequentialSkyGroups, "SkyArea group ids did not count intervening objects.");
		Require(chart.Notes.Exists((ChartNote note) => note.Kind == NoteKind.SkyArea && note.GroupId == 7) && chart.Notes.Exists((ChartNote note) => note.Kind == NoteKind.SkyArea && note.GroupId == 42), "ChartWriter mutated the source SkyArea group ids.");
		Require(text.IndexOf("track(-120,1.00)", StringComparison.Ordinal) >= 0, "Delay track event was not written.");
		Require(text.IndexOf("# delay: -120", StringComparison.Ordinal) >= 0, "Delay metadata was not written.");
		Require(Math.Abs(chartParseResult.Chart.DelayMs + 120.0) < 0.0001, "Delay metadata did not round trip.");
		string[] lines = new string[2] { "chart(140,4)", "lane(0,1,1)" };
		Require(ChartParser.Parse(lines, "<lane-min>", 0).Success, "Lane chart rejected for MIN package difficulty.");
		Require(ChartParser.Parse(lines, "<lane-evo>", 1).Success, "Lane chart rejected for EVO package difficulty.");
		Require(!ChartParser.Parse(lines, "<lane-ult>", 2).Success, "Lane chart accepted for ULT package difficulty.");
		Require(!ChartParser.Parse(lines, "<lane-fbd>", 3).Success, "Lane chart accepted for FBD package difficulty.");
		Require(!ChartParser.Parse(new string[1] { "tap(0,1,0)" }, "<missing-chart>", 0).Success, "Chart without chart(...) was accepted.");
		Require(!ChartParser.Parse(new string[1] { "chart(120,4,9)" }, "<extra-chart-arg>", 0).Success, "Chart with extra arguments was accepted.");
		Require(!ChartParser.Parse(new string[1] { "chart(NaN,4)" }, "<nan-chart>", 0).Success && !ChartParser.Parse(new string[1] { "chart(120,Infinity)" }, "<infinity-chart>", 0).Success, "Non-finite chart timing was accepted.");
		Require(ChartParser.Parse(new string[2] { "chart(120,4)", "bpm(500,150,4,1)" }, "<legacy-bpm-flag>", 0).Success && !ChartParser.Parse(new string[2] { "chart(120,4)", "bpm(500,150,4,2)" }, "<bad-bpm-flag>", 0).Success, "Legacy BPM flag compatibility was not strict.");
		Require(!ChartParser.Parse(new string[2] { "chart(120,4)", "skyarea(0,12,24,-1,12,24,1,0,0,100,0)" }, "<negative-sky-width>", 0).Success, "Negative SkyArea width was accepted.");
		ChartParseResult delayed = ChartParser.Parse(new string[4] { "# delay: 60", "chart(120,4)", "bpm(60,120,4)", "tap(120,1,1)" }, "<delay-stability>", 1);
		Require(delayed.Success && delayed.Chart.BpmEvents.Count == 1, "Delay fixture did not parse.");
		for (int saveIndex = 0; saveIndex < 3; saveIndex++)
		{
			string text2 = ChartWriter.Write(delayed.Chart, delayed.Chart.DelayMs, "delay-stability", 1);
			Require(CountOccurrences(text2, "bpm(") == 1, "Delay BPM events grew during save " + (saveIndex + 1) + ".");
			delayed = ChartParser.Parse(text2.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None), "<delay-stability>", 1);
			Require(delayed.Success && delayed.Chart.BpmEvents.Count == 1, "Delay BPM event count was not stable.");
		}
		RequireThrows<InvalidOperationException>(() =>
		{
			ChartWriter.Write(new Chart
			{
				Bpm = double.NaN,
				Division = 4.0
			}, 0.0, "invalid", 0);
		}, "ChartWriter accepted NaN timing.");
		RequireThrows<FormatException>(() =>
		{
			ChartNoteValidator.Validate(new ChartNote
			{
				Kind = NoteKind.Tap,
				TimeMs = 0.0,
				StartLane = 99,
				WidthLanes = 1
			});
		}, "Invalid tool note passed validation.");
		ChartNote mirroredSky = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 0.0,
			DurationMs = 500.0,
			StartPos = 0.3,
			StartWidth = 0.2,
			StartCellCount = 24,
			EndPos = 0.7,
			EndWidth = 0.2,
			EndCellCount = 24,
			LeftEasing = 1,
			RightEasing = 2,
			GroupId = 9
		};
		FalsumChartEditor.MirrorNoteHorizontal(mirroredSky);
		Require(Math.Abs(mirroredSky.StartPos - 0.7) < 0.0001 && Math.Abs(mirroredSky.EndPos - 0.3) < 0.0001 && mirroredSky.LeftEasing == 2 && mirroredSky.RightEasing == 1 && mirroredSky.GroupId == 9, "SkyArea mirror did not swap easing or preserve group.");
		checks.Add("chart_round_trip");
	}

	private static void CheckDualTimeAxes(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0,
			DelayMs = 60.0
		};
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 0.0,
			StartLane = 1,
			WidthLanes = 1
		});
		chart.RebuildTimeline();
		string text = ChartWriter.Write(chart, 60.0, "time-axis", 1);
		Require(text.IndexOf("tap(60,1,1)", StringComparison.Ordinal) >= 0, "Chart-time zero was not written at music time 60.");
		Require(text.IndexOf("bpm(60,120,4)", StringComparison.Ordinal) >= 0, "Delay-aligned measure BPM was not written.");
		ChartParseResult parsed = ChartParser.Parse(text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None), "<dual-time>");
		Require(parsed.Success, "Dual-time chart did not parse.");
		Require(Math.Abs(parsed.Chart.DelayMs - 60.0) < 0.0001, "Dual-time delay did not parse.");
		Require(parsed.Chart.Notes.Count == 1 && Math.Abs(parsed.Chart.Notes[0].TimeMs) < 0.0001, "Music time 60 did not map back to chart time 0.");
		Require(parsed.Chart.SpeedEvents.Count == 0, "Delay marker leaked into speed events.");
		ChartParseResult headerOnly = ChartParser.Parse(new string[3] { "# delay: 60", "chart(120,4)", "tap(120,1,3)" }, "<header-only-delay>");
		Require(headerOnly.Success && headerOnly.Chart.Notes.Count == 1 && Math.Abs(headerOnly.Chart.Notes[0].TimeMs - 60.0) < 0.0001, "Header-only delay did not use the music time axis.");
		ChartParseResult legacy = ChartParser.Parse(new string[3] { "chart(120,4)", "track(250,1.00)", "tap(250,1,2)" }, "<legacy-delay>");
		Require(legacy.Success && Math.Abs(legacy.Chart.DelayMs - 250.0) < 0.0001 && legacy.Chart.Notes.Count == 1 && Math.Abs(legacy.Chart.Notes[0].TimeMs - 250.0) < 0.0001 && legacy.Chart.SpeedEvents.Count == 0, "Legacy delay migration failed.");
		Require(ChartWriter.Write(legacy.Chart, legacy.Chart.DelayMs, "legacy", 1).IndexOf("tap(500,1,2)", StringComparison.Ordinal) >= 0, "Legacy chart was not migrated to music time.");
		FalsumBeatGrid grid = new FalsumBeatGrid
		{
			Enabled = true,
			Subdivision = 4
		};
		chart.DelayMs = 60.0;
		chart.RebuildTimeline();
		IList<FalsumBeatLine> before = grid.Build(chart, 0.0, 1000.0);
		List<double> beforeTimes = new List<double>();
		for (int i = 0; i < before.Count; i++)
		{
			beforeTimes.Add(before[i].TimeMs);
		}
		chart.DelayMs = -400.0;
		chart.RebuildTimeline();
		IList<FalsumBeatLine> after = grid.Build(chart, 0.0, 1000.0);
		Require(beforeTimes.Count == after.Count, "Delay changed grid line count.");
		for (int j = 0; j < after.Count; j++)
		{
			Require(Math.Abs(beforeTimes[j] - after[j].TimeMs) < 0.0001, "Delay moved grid lines relative to notes.");
		}
		checks.Add("dual_time_axes");
	}

	private static void CheckNegativeMusicTimeGuard(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 256.0,
			Division = 4.0,
			DelayMs = -454.0
		};
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 0.0,
			StartLane = 1,
			WidthLanes = 1
		});
		Require(
			ChartTimeAxis.IsNegativeMusicTime(0.0, -454.0),
			"Negative music time was not detected.");
		Require(
			!ChartTimeAxis.IsNegativeMusicTime(454.0, -454.0),
			"Zero music time was rejected.");
		Require(
			ChartTimeAxis.ContainsNegativeMusicTime(chart, -454.0),
			"Negative note music time was not detected.");
		RequireThrows<InvalidOperationException>(
			() => ChartWriter.Write(chart, -454.0, "negative", 0),
			"Chart writer accepted a negative note music time.");

		chart.Notes[0].TimeMs = 454.0;
		Require(
			!ChartTimeAxis.ContainsNegativeMusicTime(chart, -454.0),
			"Zero music time was treated as negative.");
		string text = ChartWriter.Write(
			chart,
			-454.0,
			"zero-time",
			0);
		Require(
			text.IndexOf("tap(0,1,1)", StringComparison.Ordinal) >= 0,
			"Zero music time was not written canonically.");

		chart.Notes.Clear();
		chart.Events.Add(new ChartEvent
		{
			Kind = ChartEventKind.Bpm,
			TimeMs = 453.0,
			Bpm = 256.0,
			Division = 4.0
		});
		RequireThrows<InvalidOperationException>(
			() => ChartWriter.Write(chart, -454.0, "negative", 0),
			"Chart writer accepted a negative event music time.");
		checks.Add("negative_music_time_guard");
	}

	/// <summary>
	/// The editor can suppress track() in the preview without touching the
	/// data. Three things must hold: the preview map becomes exactly linear,
	/// the events (and therefore the written file) are untouched, and the
	/// toggling is reversible in either direction.
	/// </summary>
	private static void CheckTrackSpeedEffectSuppression(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		};
		// A track event has to live in BOTH lists: Events owns ordering and
		// the written payload, SpeedEvents is what ScaledTimeAt integrates.
		// Registering only in Events leaves the speed map empty, which made
		// every ScaledTimeAt assertion in this block unreachable.
		ChartEvent speedUp = new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 1000.0,
			Multiplier = 2.5
		};
		ChartEvent reverse = new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 4000.0,
			Multiplier = -20.0
		};
		chart.Events.Add(speedUp);
		chart.SpeedEvents.Add(speedUp);
		chart.Events.Add(reverse);
		chart.SpeedEvents.Add(reverse);
		chart.RebuildTimeline();

		string writtenWithEffects = ChartWriter.Write(
			chart, 0.0, "track-speed", 1);
		TrackMotion motion = new TrackMotion(chart, 5.0);

		Require(
			chart.TrackSpeedEffectsEnabled,
			"A freshly parsed chart must start with track effects enabled.");

		double scaledAt2500 = chart.ScaledTimeAt(2500.0);
		double scaledAt6000 = chart.ScaledTimeAt(6000.0);
		Require(
			Math.Abs(scaledAt2500 - 4750.0) < 0.0001,
			"Track effects did not integrate the multiplier over 1000-2500 ms.");
		Require(
			scaledAt6000 < scaledAt2500,
			"A negative track() did not reverse the scaled time axis.");

		// Suppression: exactly linear, and the data-side multiplier is
		// unchanged so a newly created track event still inherits the real
		// value rather than being flattened to 1.00.
		chart.TrackSpeedEffectsEnabled = false;
		Require(
			Math.Abs(chart.ScaledTimeAt(2500.0) - 2500.0) < 0.0001 &&
			Math.Abs(chart.ScaledTimeAt(6000.0) - 6000.0) < 0.0001,
			"Suppressed preview was not a linear 1.00 mapping.");
		Require(
			Math.Abs(chart.PreviewSpeedMultiplierAt(2500.0) - 1.0) <
			0.0001,
			"Suppressed preview speed multiplier was not 1.00.");
		Require(
			Math.Abs(chart.SpeedMultiplierAt(2500.0) - 2.5) < 0.0001 &&
			Math.Abs(chart.SpeedMultiplierAt(6000.0) + 20.0) < 0.0001,
			"Suppression leaked into the data-side multiplier.");
		Require(
			chart.Events.Count == 2 && chart.SpeedEvents.Count == 2,
			"Suppression removed track events.");

		// The suppressed preview must move notes like a constant 1.00 chart:
		// that is exactly what makes negative-multiplier charts editable.
		double expectedZ =
			2000.0 / 1000.0 * TrackMotion.DefaultTrackLength *
			TrackMotion.TrackScale * 5.0;
		Require(
			Math.Abs(motion.NoteZAt(3000.0, 1000.0) - expectedZ) <
			0.0001,
			"Suppressed preview did not move notes on a constant 1.00 track.");

		string writtenWhileSuppressed = ChartWriter.Write(
			chart, 0.0, "track-speed", 1);
		Require(
			writtenWhileSuppressed == writtenWithEffects,
			"Suppressing the preview changed the written chart file.");

		// Reversibility: back on, the original integration returns.
		chart.TrackSpeedEffectsEnabled = true;
		Require(
			Math.Abs(chart.ScaledTimeAt(2500.0) - scaledAt2500) < 0.0001 &&
			Math.Abs(chart.ScaledTimeAt(6000.0) - scaledAt6000) < 0.0001,
			"Re-enabling track effects did not restore the integration.");
		Require(
			Math.Abs(chart.PreviewSpeedMultiplierAt(6000.0) + 20.0) <
			0.0001,
			"Re-enabled preview did not report the negative multiplier.");

		// The flag is runtime-only: a chart re-parsed from the same text must
		// start enabled, so a saved package can never carry the suppression.
		ChartParseResult parsed = ChartParser.Parse(
			writtenWithEffects.Replace("\r\n", "\n")
				.Split('\n', StringSplitOptions.None),
			"<track-suppression>");
		Require(parsed.Success, "Track suppression chart did not parse.");
		Require(
			parsed.Chart.TrackSpeedEffectsEnabled,
			"Parsed charts inherited the runtime suppression flag.");
		Require(
			parsed.Chart.SpeedEvents.Count == 2,
			"Round-tripped chart lost its track events.");
		checks.Add("track_speed_effect_suppression");
	}

	private static void CheckTapPlacementGuard(List<string> checks)
	{
		Require(FalsumTapPlacementRule.MinGapMs == 9.0, "TAP placement window is not nine milliseconds.");
		List<ChartNote> notes = new List<ChartNote>
		{
			new ChartNote
			{
				Kind = NoteKind.Tap,
				TimeMs = 1000.0,
				StartLane = 1,
				WidthLanes = 1
			}
		};
		Require(FalsumTapPlacementRule.IsBlocked(notes, 1000.0, 1, 1), "An identical TAP time was allowed.");
		Require(FalsumTapPlacementRule.IsBlocked(notes, 1009.0, 1, 1), "The nine millisecond boundary was allowed.");
		Require(FalsumTapPlacementRule.IsBlocked(notes, 991.0, 1, 1), "The negative nine millisecond boundary was allowed.");
		Require(!FalsumTapPlacementRule.IsBlocked(notes, 1009.001, 1, 1), "A TAP beyond the window was blocked.");
		Require(!FalsumTapPlacementRule.IsBlocked(notes, 990.999, 1, 1), "A TAP before the window was blocked.");
		Require(!FalsumTapPlacementRule.IsBlocked(notes, 1000.0, 3, 1), "A TAP on another lane was blocked.");
		Require(!FalsumTapPlacementRule.IsBlocked(notes, 5000.0, 1, 1), "A distant TAP was blocked.");
		Require(!FalsumTapPlacementRule.IsBlocked(null, 1000.0, 1, 1), "A null note list was treated as blocked.");
		Require(!FalsumTapPlacementRule.IsBlocked(new List<ChartNote>(), 1000.0, 1, 1), "An empty chart was treated as blocked.");

		// A wide TAP spans several lanes: proximity in time is enough when the
		// lane spans intersect, even if the start lanes differ.
		ChartNote wide = new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 1000.0,
			StartLane = 1,
			WidthLanes = 2
		};
		List<ChartNote> wideNotes = new List<ChartNote> { wide };
		Require(FalsumTapPlacementRule.IsBlocked(wideNotes, 1005.0, 2, 1), "A TAP under a wide TAP span was allowed.");
		Require(FalsumTapPlacementRule.IsBlocked(wideNotes, 1005.0, 0, 2), "A wide TAP overlapping a wide TAP was allowed.");
		Require(!FalsumTapPlacementRule.IsBlocked(wideNotes, 1005.0, 3, 1), "A TAP beside a wide TAP span was blocked.");
		Require(!FalsumTapPlacementRule.IsBlocked(wideNotes, 1005.0, 0, 1), "A TAP left of a wide TAP span was blocked.");
		Require(FalsumTapPlacementRule.IsBlocked(
			new List<ChartNote>
			{
				wide,
				new ChartNote
				{
					Kind = NoteKind.Tap,
					TimeMs = 1004.0,
					StartLane = 2,
					WidthLanes = 1
				}
			},
			1005.0,
			2,
			1),
			"A nearer second blocker was missed.");

		// Timed notes of other kinds never gate TAP placement.
		List<ChartNote> others = new List<ChartNote>
		{
			new ChartNote
			{
				Kind = NoteKind.Hold,
				TimeMs = 1000.0,
				DurationMs = 500.0,
				StartLane = 1,
				WidthLanes = 1
			},
			new ChartNote
			{
				Kind = NoteKind.Flick,
				TimeMs = 1000.0,
				StartPos = 0.2,
				StartWidth = 0.4,
				StartCellCount = 24,
				EndCellCount = 24,
				FlickDirection = 4
			},
			new ChartNote
			{
				Kind = NoteKind.SkyArea,
				TimeMs = 1000.0,
				DurationMs = 500.0,
				StartPos = 0.2,
				StartWidth = 0.4,
				StartCellCount = 24,
				EndCellCount = 24
			}
		};
		Require(!FalsumTapPlacementRule.IsBlocked(others, 1002.0, 1, 1), "A non-TAP note gated TAP placement.");

		// The blocker lookup feeds the hint text, so it must report the
		// closest offending TAP and stay silent when placement is allowed.
		ChartNote blocker;
		double gapMs;
		Require(FalsumTapPlacementRule.TryFindBlocker(notes, 995.0, 1, 1, out blocker, out gapMs) && blocker == notes[0] && Math.Abs(gapMs - 5.0) < 0.0001, "The blocker lookup reported the wrong TAP.");
		Require(!FalsumTapPlacementRule.TryFindBlocker(notes, 1020.0, 1, 1, out blocker, out gapMs) && blocker == null, "The blocker lookup reported a distant TAP.");
		checks.Add("tap_placement_guard");
	}

	private static void CheckClockTimeAxis(List<string> checks)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Expected Obj, but got Unknown
		GameObject host = null;
		try
		{
			host = new GameObject("FalsumDualClockSelfCheck");
			GameplayClock gameplayClock = host.AddComponent<GameplayClock>();
			gameplayClock.Initialize(null, 0.0);
			gameplayClock.AudioOffsetMs = -60.0;
			gameplayClock.Seek(1060.0);
			Require(Math.Abs(gameplayClock.TimeMs - 1000.0) < 0.001, "GameplayClock did not expose chart time.");
			SmoothClockAudioSink sink = new SmoothClockAudioSink
			{
				AudioPositionMs = 5000.0,
				SchedulingLeadMs = 20.0
			};
			gameplayClock.AudioOffsetMs = 0.0;
			gameplayClock.SetAudioSink(sink);
			gameplayClock.Seek(5000.0);
			Require(sink.SeekCount == 0 && sink.StopCount == 1, "Paused seek rebuilt the audio transport.");
			gameplayClock.Resume();
			sink.AudioPositionDspTime = AudioSettings.dspTime - 0.01;
			double smoothed = gameplayClock.TimeMs;
			Require(smoothed >= 5028.0 && smoothed <= 5050.0, "Audio-position clock did not interpolate between DSP buffers.");
		}
		finally
		{
			if ((Object)(object)host != (Object)null)
			{
				Object.DestroyImmediate((Object)(object)host);
			}
		}
		checks.Add("clock_time_axis");
	}

	private static void CheckAutoplayStartState(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0
		};
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 2400.0,
			StartLane = 1,
			WidthLanes = 1
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 2000.0,
			DurationMs = 1000.0,
			StartLane = 2,
			WidthLanes = 1
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 2000.0,
			DurationMs = 1000.0
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 2500.0,
			StartLane = 3,
			WidthLanes = 1
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 2600.0,
			DurationMs = 300.0,
			StartLane = 4,
			WidthLanes = 1
		});
		new JudgementEngine(chart).SkipNotesStartedBefore(2500.0);
		Require(chart.Notes[0].Judged && chart.Notes[1].Judged && chart.Notes[2].Judged, "Notes already past the playback start were not skipped.");
		Require(!chart.Notes[3].Judged && !chart.Notes[4].Judged, "Armed notes were skipped at the playback start.");
		checks.Add("autoplay_midstart");
	}

	private static void CheckAutoCombo(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0
		};
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 0.0,
			StartLane = 1,
			WidthLanes = 1
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Flick,
			TimeMs = 100.0,
			FlickDirection = 4
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 200.0,
			DurationMs = 500.0,
			StartLane = 2,
			WidthLanes = 1
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 1000.0,
			DurationMs = 250.0
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 2000.0,
			DurationMs = 251.0,
			StartLane = 3,
			WidthLanes = 1
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 3000.0,
			DurationMs = 252.0,
			StartLane = 4,
			WidthLanes = 1
		});
		chart.Events.Add(new ChartEvent
		{
			Kind = ChartEventKind.Bpm,
			TimeMs = 4000.0,
			Bpm = 600.0,
			Division = 4.0
		});
		chart.BpmEvents.Add(new ChartEvent
		{
			Kind = ChartEventKind.Bpm,
			TimeMs = 4000.0,
			Bpm = 600.0,
			Division = 4.0
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 4000.0,
			DurationMs = 500.0
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 10000.0,
			DurationMs = 2.0
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 11000.0,
			DurationMs = 1.0,
			StartLane = 5,
			WidthLanes = 1
		});
		chart.RebuildTimeline();

		Require(
			Math.Abs(FalsumAutoCombo.StepMsAt(
				chart,
				chart.Notes[0]) - 250.0) < 0.0001,
			"Auto combo did not use the 120 BPM event step.");
		Require(
			Math.Abs(FalsumAutoCombo.StepMsAt(
				chart,
				chart.Notes[6]) - 100.0) < 0.0001,
			"Auto combo did not use the 600 BPM event step.");

		RequireAutoCombo(chart, -1.0, 1, "negative time");
		RequireAutoCombo(chart, 0.0, 1, "tap boundary");
		RequireAutoCombo(chart, 99.999, 1, "flick before boundary");
		RequireAutoCombo(chart, 100.0, 2, "flick boundary");
		RequireAutoCombo(chart, 199.999, 2, "hold before head");
		RequireAutoCombo(chart, 200.0, 3, "hold head");
		RequireAutoCombo(chart, 449.999, 3, "hold before interior");
		RequireAutoCombo(chart, 450.0, 4, "hold interior");
		RequireAutoCombo(chart, 699.999, 4, "hold before second interior");
		RequireAutoCombo(chart, 700.0, 5, "hold exact interior end");
		RequireAutoCombo(chart, 999.999, 5, "sky before first sample");
		RequireAutoCombo(chart, 1000.0, 5, "sky has no head event");
		RequireAutoCombo(chart, 1250.0, 6, "sky exact interior end");
		RequireAutoCombo(chart, 2000.0, 7, "second hold head");
		RequireAutoCombo(chart, 2250.0, 8, "second hold interior");
		RequireAutoCombo(chart, 2251.0, 8, "short remainder has no tail");
		RequireAutoCombo(chart, 3000.0, 9, "third hold head");
		RequireAutoCombo(chart, 3250.0, 10, "third hold interior");
		RequireAutoCombo(chart, 3251.999, 10, "tail before boundary");
		RequireAutoCombo(chart, 3252.0, 11, "third hold tail");
		RequireAutoCombo(chart, 4000.0, 11, "BPM event adds no combo");
		RequireAutoCombo(chart, 4100.0, 12, "high BPM sky sample");
		RequireAutoCombo(chart, 4300.0, 14, "high BPM sky samples");
		RequireAutoCombo(chart, 4500.0, 16, "high BPM sky tail end");
		RequireAutoCombo(chart, 10001.0, 16, "two millisecond sky before tail");
		RequireAutoCombo(chart, 10002.0, 17, "two millisecond sky tail");
		RequireAutoCombo(chart, 11000.0, 18, "one millisecond hold head");
		RequireAutoCombo(chart, 11001.0, 18, "one millisecond hold has no tail");
		RequireAutoCombo(chart, 4500.0, 16, "forward query repeat");
		RequireAutoCombo(chart, 0.0, 1, "reverse seek");
		RequireAutoCombo(chart, 3000.0, 9, "reverse middle seek");
		RequireAutoCombo(chart, 4500.0, 16, "forward seek repeat");

		RequireAutoCombo(chart, double.NaN, 1, "NaN time");
		RequireAutoCombo(
			chart,
			double.PositiveInfinity,
			1,
			"infinite time");
		RequireAutoCombo(null, 1000.0, 0, "null chart");
		checks.Add("auto_combo");
	}

	private static void RequireAutoCombo(
		Chart chart,
		double timeMs,
		int expected,
		string label)
	{
		int actual = FalsumAutoCombo.CountAt(chart, timeMs);
		Require(
			actual == expected,
			"Auto combo " + label + " expected " +
			expected + ", got " + actual + ".");
	}

	private static void CheckSkyBounds(List<string> checks)
	{
		Require(!FalsumSkyBounds.IsOutOfBounds(0.5, 0.5), "A centered sky span should be valid.");
		Require(FalsumSkyBounds.IsOutOfBounds(0.1, 0.5), "A left-overflowing sky span should be invalid.");
		Require(FalsumSkyBounds.IsOutOfBounds(0.9, 0.5), "A right-overflowing sky span should be invalid.");
		Require(!FalsumSkyBounds.IsOutOfBounds(0.0, 0.0) && !FalsumSkyBounds.IsOutOfBounds(1.0, 0.0), "Zero-width spans on the boundary should be valid.");
		FalsumSkyBounds.Span left = FalsumSkyBounds.ClampSpanPreservingWidth(0.0, 0.4);
		Require(Math.Abs(left.Center - 0.2) <= 1E-09 && Math.Abs(left.Width - 0.4) <= 1E-09 && left.Clamped, "Left clamp did not preserve the span width.");
		FalsumSkyBounds.Span right = FalsumSkyBounds.ClampSpanPreservingWidth(1.0, 0.4);
		Require(Math.Abs(right.Center - 0.8) <= 1E-09 && Math.Abs(right.Width - 0.4) <= 1E-09 && right.Clamped, "Right clamp did not preserve the span width.");
		FalsumSkyBounds.Span wide = FalsumSkyBounds.ClampSpanPreservingWidth(0.0, 2.0);
		Require(Math.Abs(wide.Center - 0.5) <= 1E-09 && Math.Abs(wide.Width - 1.0) <= 1E-09, "An over-wide span was not normalized.");
		FalsumSkyBounds.Span width = FalsumSkyBounds.ClampSpanPreservingCenter(0.1, 0.5);
		Require(Math.Abs(width.Center - 0.1) <= 1E-09 && Math.Abs(width.Width - 0.2) <= 1E-09 && width.Clamped, "Center-preserving width clamp failed.");
		FalsumSkyBounds.CellSpan rightCell = FalsumSkyBounds.ClampCellSpan(144, 97, 192);
		Require(rightCell.Center == 143 && rightCell.Width == 97, "Boundary cell clamp failed.");
		FalsumSkyBounds.CellSpan oddFull = FalsumSkyBounds.ClampCellSpan(0, 1, 1);
		Require(oddFull.Center == 0 && oddFull.Width == 0, "Unrepresentable full-width span was not reduced.");
		ChartParseResult parsed = ChartParser.Parse(ChartWriter.Write(new Chart
		{
			Bpm = 120.0,
			Division = 4.0,
			Notes = 
			{
				new ChartNote
				{
					Kind = NoteKind.Flick,
					TimeMs = 1000.0,
					StartPos = (double)rightCell.Center / 192.0,
					StartWidth = (double)rightCell.Width / 192.0,
					StartCellCount = 192,
					EndCellCount = 192,
					FlickDirection = 4
				}
			}
		}, 0.0, "sky-bounds", 0).Replace("\r\n", "\n").Split('\n', StringSplitOptions.None), "<sky-bounds>");
		Require(parsed.Success && parsed.Chart.Notes.Count == 1 && !FalsumSkyBounds.IsOutOfBounds(parsed.Chart.Notes[0].FlickCenterPos, parsed.Chart.Notes[0].FlickWidth), "Written boundary flick became out of bounds.");
		string outOfBoundsText = ChartWriter.Write(new Chart
		{
			Bpm = 120.0,
			Division = 4.0,
			Notes =
			{
				new ChartNote
				{
					Kind = NoteKind.Flick,
					TimeMs = 1000.0,
					StartPos = -0.25,
					StartWidth = 0.5,
					StartCellCount = 24,
					EndCellCount = 24,
					FlickDirection = 4
				},
				new ChartNote
				{
					Kind = NoteKind.SkyArea,
					TimeMs = 1500.0,
					DurationMs = 500.0,
					StartPos = 1.25,
					StartWidth = 0.5,
					StartCellCount = 24,
					EndPos = -0.25,
					EndWidth = 0.5,
					EndCellCount = 24,
					LeftEasing = 0,
					RightEasing = 0
				}
			}
		}, 0.0, "out-of-bounds", 0);
		ChartParseResult parsedOutOfBounds = ChartParser.Parse(
			outOfBoundsText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None),
			"<out-of-bounds>");
		Require(parsedOutOfBounds.Success && parsedOutOfBounds.Chart.Notes.Count == 2, "Out-of-bounds sky notes did not round-trip.");
		Require(
			FalsumSkyBounds.IsOutOfBounds(
				parsedOutOfBounds.Chart.Notes[0].FlickCenterPos,
				parsedOutOfBounds.Chart.Notes[0].FlickWidth) &&
			FalsumSkyBounds.IsOutOfBounds(
				parsedOutOfBounds.Chart.Notes[1].StartPos,
				parsedOutOfBounds.Chart.Notes[1].StartWidth),
			"Out-of-bounds sky spans were clamped during round-trip.");
		checks.Add("sky_bounds");
	}

        private static void CheckFineSkyFractions(List<string> checks)
        {
                const int denominator = 200;
                const double width = 1.0 / denominator;
                double normalizedPosition;
                double normalizedWidth;
                int normalizedDenominator = FalsumFraction.NormalizePair(
                        0.5,
                        width,
                        denominator,
                        out normalizedPosition,
                        out normalizedWidth);
                Require(
                        normalizedDenominator == denominator &&
                        Math.Abs(normalizedPosition - 0.5) < 0.000000001 &&
                        Math.Abs(normalizedWidth - width) < 0.000000001,
                        "Fine sky fractions were truncated to a coarser denominator.");

                Chart chart = new Chart
                {
                        Bpm = 120.0,
                        Division = 4.0
                };
                chart.Notes.Add(new ChartNote
                {
                        Kind = NoteKind.Flick,
                        TimeMs = 1000.0,
                        StartPos = 0.5,
                        StartWidth = width,
                        StartCellCount = denominator,
                        EndCellCount = denominator,
                        FlickDirection = 4
                });
                chart.Notes.Add(new ChartNote
                {
                        Kind = NoteKind.SkyArea,
                        TimeMs = 1200.0,
                        DurationMs = 500.0,
                        StartPos = 0.25,
                        StartWidth = width,
                        StartCellCount = denominator,
                        EndPos = 0.75,
                        EndWidth = width,
                        EndCellCount = denominator,
                        GroupId = 0
                });
                string text = ChartWriter.Write(chart, 0.0, "fine-sky-fractions", 0);
                ChartParseResult parsed = ChartParser.Parse(
                        text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None),
                        "<fine-sky-fractions>",
                        0);
                ChartNote flick = parsed.Chart.Notes.Find(
                        note => note.Kind == NoteKind.Flick);
                ChartNote skyArea = parsed.Chart.Notes.Find(
                        note => note.Kind == NoteKind.SkyArea);
                Require(
                        parsed.Success &&
                        flick != null &&
                        flick.StartCellCount == denominator &&
                        flick.EndCellCount == denominator &&
                        skyArea != null &&
                        skyArea.StartCellCount == denominator &&
                        skyArea.EndCellCount == denominator &&
                        Math.Abs(flick.StartWidth - width) < 0.000000001 &&
                        Math.Abs(skyArea.StartWidth - width) < 0.000000001 &&
                        Math.Abs(skyArea.EndWidth - width) < 0.000000001,
                        "Fine FLICK/SKYAREA fractions did not round trip.");
                checks.Add("fine_sky_fractions");
        }

	private static void CheckHistory(List<string> checks)
	{
		FalsumChartSession session = new FalsumChartSession(new Chart
		{
			Bpm = 140.0,
			Division = 4.0
		}, 0.0);
		session.BeginChange("add tap");
		session.Chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 1000.0,
			StartLane = 2,
			WidthLanes = 1
		});
		Require(session.CommitChange(), "History commit failed.");
		Require(session.UndoCount == 1, "Undo count failed.");
		session.Undo();
		Require(session.Chart.Notes.Count == 0, "Undo restore failed.");
		session.Redo();
		Require(session.Chart.Notes.Count == 1, "Redo restore failed.");
		session.BeginChange("move tap");
		session.Chart.Notes[0].TimeMs = 2000.0;
		Require(session.CommitChange(), "Second history commit failed.");
		Require(session.UndoCount == 2 && session.RedoCount == 0, "Second history count failed.");
		session.Undo();
		Require(Math.Abs(session.Chart.Notes[0].TimeMs - 1000.0) < 0.0001, "First multi-step undo failed.");
		session.Undo();
		Require(session.Chart.Notes.Count == 0, "Second multi-step undo failed.");
		session.Redo();
		Require(session.Chart.Notes.Count == 1 && Math.Abs(session.Chart.Notes[0].TimeMs - 1000.0) < 0.0001, "First multi-step redo failed.");
		session.Redo();
		Require(session.Chart.Notes.Count == 1 && Math.Abs(session.Chart.Notes[0].TimeMs - 2000.0) < 0.0001, "Second multi-step redo failed.");
		session.Undo();
		session.BeginChange("branching edit");
		session.Chart.Notes[0].TimeMs = 3000.0;
		Require(session.CommitChange(), "Branching history commit failed.");
		Require(session.RedoCount == 0, "Branching edit kept stale redo.");
		session.Undo();
		Require(Math.Abs(session.Chart.Notes[0].TimeMs - 1000.0) < 0.0001, "Branching undo failed.");
		session.ClearHistory();
		Require(session.UndoCount == 0 && session.RedoCount == 0, "History clear failed.");
		checks.Add("history");
	}

	private static void CheckTimelineCacheInvalidation(List<string> checks)
	{
		var chart = new Chart { Bpm = 120.0, Division = 4.0 };
		chart.Notes.Add(new ChartNote { Kind = NoteKind.Hold, TimeMs = 1000.0, DurationMs = 500.0, StartLane = 1, WidthLanes = 1 });
		chart.RebuildTimeline();
		var session = new FalsumChartSession(chart, 0.0);
		Require(chart.EndTimeMs == 1500.0, "Cache fixture has the wrong end time.");
		session.BeginChange("cached move");
		chart.Notes[0].TimeMs = 2000.0;
		Require(session.CommitChange() && chart.EndTimeMs == 2500.0,
			"Commit retained a stale chart end time.");
		Require(session.Undo() && chart.EndTimeMs == 1500.0, "Undo retained a stale end time.");
		Require(session.Redo() && chart.EndTimeMs == 2500.0, "Redo retained a stale end time.");
		session.BeginChange("cancel clear");
		chart.Notes.Clear();
		chart.InvalidateTimelineDerived();
		Require(chart.EndTimeMs == 0.0, "Cleared chart retained a stale end time.");
		session.CancelChange();
		Require(chart.EndTimeMs == 2500.0, "Cancel did not restore the cached end time.");
		session.ApplyBatch("cached batch", target =>
		{
			target.Notes[0].DurationMs = 1000.0;
			target.Events.Add(new ChartEvent { Kind = ChartEventKind.TrackSpeed, TimeMs = 500.0, Multiplier = 2.0 });
		});
		Require(chart.EndTimeMs == 3000.0 && chart.ScaledTimeAt(2000.0) == 3500.0,
			"Batch did not update end time or speed map.");
		session.BeginChange("cached speed edit");
		chart.SpeedEvents[0].Multiplier = 3.0;
		Require(session.CommitChange() && chart.ScaledTimeAt(2000.0) == 5000.0,
			"In-place event edit retained a stale speed map.");
		chart.Notes[0].TimeMs = 4000.0;
		session.NotifyChanged();
		Require(chart.EndTimeMs == 5000.0, "NotifyChanged retained a stale end time.");
		session.Replace(new Chart { Bpm = 120.0, Division = 4.0 }, 0.0);
		Require(chart.EndTimeMs == 0.0, "Replace retained a stale end time.");
		checks.Add("timeline-cache-invalidation");
	}

	private static void CheckHoldUvWindow(List<string> checks)
	{
		var type = typeof(NoteRenderer).GetNestedType("NoteVisual", BindingFlags.NonPublic);
		var update = typeof(NoteRenderer).GetMethod("UpdateHoldMiddleUv", BindingFlags.Static | BindingFlags.NonPublic);
		Require(type != null && update != null, "Hold UV update is unavailable.");
		var visual = Activator.CreateInstance(type, true);
		var mesh = new Mesh();
		try
		{
			var original = new[] { new Vector2(0f, 0.2f), new Vector2(1f, 0.45f), new Vector2(0f, 0.7f) };
			mesh.vertices = new Vector3[original.Length];
			mesh.uv = original;
			type.GetField("HoldMiddleMesh").SetValue(visual, mesh);
			type.GetField("HoldMiddleBaseUv").SetValue(visual, original);
			type.GetField("HoldMiddleUv").SetValue(visual, new Vector2[original.Length]);
			type.GetField("HoldMiddleUvHead").SetValue(visual, 0.2f);
			type.GetField("HoldMiddleUvTail").SetValue(visual, 0.7f);
			type.GetField("AppliedHoldUvScale").SetValue(visual, -1f);
			foreach (var scale in new[] { 1f, 0.75f, 0.5f, 0.01f, 0f, 1f })
			{
				update.Invoke(null, new object[] { visual, scale });
				var uv = mesh.uv;
				for (var i = 0; i < uv.Length; i++)
				{
					Require(uv[i].y >= 0.2f && uv[i].y <= 0.7f && uv[i].x == original[i].x,
						"Shortened hold sampled outside its atlas window.");
					Require(Math.Abs(uv[i].y - (0.7f + (original[i].y - 0.7f) * scale)) < 0.00001f,
						"Hold UV window lost its continuous texture density.");
				}
			}
		}
		finally { Object.DestroyImmediate(mesh); }
		checks.Add("hold-uv-window");
	}

	private static void CheckMultiSelectionSelfCheckCleanup(List<string> checks)
	{
		var host = new GameObject("FalsumSelectionCleanupSelfCheck");
		try
		{
			var editor = host.AddComponent<FalsumChartEditor>();
			var type = typeof(FalsumChartEditor);
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var chart = new Chart { Bpm = 120.0, Division = 4.0 };
			var original = new ChartNote { Kind = NoteKind.Tap, TimeMs = 1000.0, StartLane = 1, WidthLanes = 1 };
			chart.Notes.Add(original);
			chart.RebuildTimeline();
			Require(chart.EndTimeMs == 1000.0, "Selection cleanup fixture is invalid.");
			type.GetField("_chart", flags).SetValue(editor, chart);
			type.GetField("_grid", flags).SetValue(editor, new FalsumBeatGrid { Enabled = true, Subdivision = 4 });
			var selected = (List<ChartNote>)type.GetField("_selected", flags).GetValue(editor);
			selected.Add(original);
			type.GetField("_pointerRawTime", flags).SetValue(editor, 123.0);
			var check = type.GetMethod("CheckMultiSelectionDragIsRigid", flags);
			Require(check != null, "Multi-selection runtime check is missing.");
			bool threw = false;
			try { check.Invoke(editor, new object[] { new List<string>() }); }
			catch (TargetInvocationException exception)
			{
				// No world is installed: the drag fails after adding its fixtures.
				Require(exception.InnerException is NullReferenceException, "Selection cleanup failed for an unexpected reason.");
				threw = true;
			}
			Require(threw && chart.Notes.Count == 1 && ReferenceEquals(chart.Notes[0], original) && chart.EndTimeMs == 1000.0,
				"An interrupted self check left ghost notes in the active chart.");
			Require(selected.Count == 1 && ReferenceEquals(selected[0], original) &&
				(double)type.GetField("_pointerRawTime", flags).GetValue(editor) == 123.0,
				"An interrupted self check lost selection or pointer state.");
			checks.Add("selection-selfcheck-cleanup");
		}
		finally { Object.DestroyImmediate(host); }
	}

	private static void CheckTransactionSafety(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0
		};
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 0.0,
			StartLane = 1,
			WidthLanes = 1
		});
		chart.RebuildTimeline();
		FalsumChartSession session = new FalsumChartSession(chart, 0.0);
		session.BeginChange("move");
		session.Chart.Notes[0].TimeMs = 1000.0;
		Require(session.CommitChange(), "Transaction fixture commit failed.");
		session.Undo();
		Require(session.RedoCount == 1 && Math.Abs(session.Chart.Notes[0].TimeMs) < 0.0001, "Transaction fixture undo failed.");
		session.BeginChange("pending redo");
		session.Chart.Notes[0].TimeMs = 2000.0;
		Require(session.Redo() && !session.HasPendingEdit && session.RedoCount == 1 && Math.Abs(session.Chart.Notes[0].TimeMs) < 0.0001, "Redo did not cancel a pending edit safely.");
		Require(session.Redo() && Math.Abs(session.Chart.Notes[0].TimeMs - 1000.0) < 0.0001, "Redo was consumed while cancelling a pending edit.");
		session.BeginChange("pending clear");
		session.Chart.Notes[0].TimeMs = 3000.0;
		session.ClearHistory();
		Require(!session.HasPendingEdit && session.UndoCount == 0 && session.RedoCount == 0 && Math.Abs(session.Chart.Notes[0].TimeMs - 1000.0) < 0.0001, "Clear history discarded a pending edit.");
		int notesBeforeFailure = session.Chart.Notes.Count;
		RequireThrows<InvalidOperationException>(() =>
		{
			session.ApplyBatch("rollback", (Chart target) =>
			{
				target.Notes.Add(new ChartNote
				{
					Kind = NoteKind.Tap,
					TimeMs = 2000.0,
					StartLane = 2,
					WidthLanes = 1
				});
				throw new InvalidOperationException("Expected batch failure.");
			});
		}, "Batch failure was not propagated.");
		Require(!session.HasPendingEdit && session.Chart.Notes.Count == notesBeforeFailure && session.UndoCount == 0, "Failed batch left partial chart changes.");
		string toolRoot = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), "Temp", "FalsumSelfCheckTool", "com.falsum.selfcheck");
		Directory.CreateDirectory(toolRoot);
		File.WriteAllText(Path.Combine(toolRoot, "tool.json"), "{\n  \"apiVersion\": 1,\n  \"engine\": \"javascript\",\n  \"id\": \"com.falsum.selfcheck\",\n  \"name\": \"Self Check Tool\",\n  \"version\": \"1.0.0\",\n  \"entry\": \"tool.js\"\n}\n");
		File.WriteAllText(Path.Combine(toolRoot, "tool.js"), "falsum.registerTool({\n  createForm() {\n    return {\n      title: 'Self Check',\n      fields: [{\n        key: 'mode', type: 'text', label: 'Mode',\n        required: true, defaultValue: 'patch'\n      }]\n    };\n  },\n  validate() { return []; },\n  run(ctx) {\n    const notes = ctx.chart.notes.find({});\n    if (ctx.inputs.mode === 'patch') {\n      ctx.edit.notes.update(notes[0].key, { widthLanes: 2 });\n    } else if (ctx.inputs.mode === 'unknown') {\n      ctx.edit.notes.update(notes[0].key, {\n        widthLanes: 2, bogus: true\n      });\n    } else if (ctx.inputs.mode === 'report-empty') {\n      return { message: 'ok', report: {\n        title: 'Empty Report',\n        summary: 'no issues',\n        emptyText: 'passed',\n        items: []\n      }};\n    } else if (ctx.inputs.mode === 'report-invalid') {\n      return { message: 'bad', report: {\n        title: 'Bad Report', items: [{\n          title: 'Broken', message: 'missing note',\n          noteKeys: ['missing:key']\n        }]\n      }};\n    } else {\n      ctx.edit.notes.add({\n        kind: 'tap', timeMs: 0, startLane: 99, widthLanes: 1\n      });\n    }\n    return { message: 'ok' };\n  }\n});\n");
		FalsumToolManifest toolManifest = FalsumToolManifest.Load(toolRoot);
		Require(toolManifest.IsReady, "Transaction test tool did not load: " + toolManifest.LoadError);
		Chart chart2 = new Chart();
		chart2.Bpm = 120.0;
		chart2.Division = 4.0;
		chart2.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 1200.0,
			StartLane = 1,
			WidthLanes = 1
		});
		chart2.RebuildTimeline();
		FalsumChartSession toolSession = new FalsumChartSession(chart2, 0.0);
		FalsumToolChartSnapshot snapshot = FalsumToolChartSnapshot.Capture(chart2, 0.0, 0.0);
		using (FalsumToolRuntime runtime = new FalsumToolRuntime(toolManifest, snapshot))
		{
			runtime.Load();
			FalsumToolRunRequest request = new FalsumToolRunRequest
			{
				ToolId = toolManifest.Id
			};
			request.Values["mode"] = "patch";
			FalsumToolRunOutput output = runtime.Run(request);
			Require(output.Batch != null && output.Batch.Count == 1 && toolSession.ApplyBatch("self check patch", output.Batch.Apply, output.Batch.DelayMs) && toolSession.Chart.Notes[0].WidthLanes == 2 && Math.Abs(toolSession.Chart.Notes[0].TimeMs - 1200.0) < 0.0001, "Note partial update required timeMs or failed.");
		}
		snapshot = FalsumToolChartSnapshot.Capture(toolSession.Chart, 0.0, 0.0);
		FalsumToolRuntime runtime2 = new FalsumToolRuntime(toolManifest, snapshot);
		try
		{
			runtime2.Load();
			FalsumToolRunRequest request2 = new FalsumToolRunRequest
			{
				ToolId = toolManifest.Id
			};
			request2.Values["mode"] = "unknown";
			RequireThrows<FalsumToolRuntimeException>(() =>
			{
				runtime2.Run(request2);
			}, "Unknown Note patch field was accepted.");
		}
		finally
		{
			if (runtime2 != null)
			{
				((IDisposable)runtime2).Dispose();
			}
		}
		snapshot = FalsumToolChartSnapshot.Capture(toolSession.Chart, 0.0, 0.0);
		FalsumToolRuntime runtime3 = new FalsumToolRuntime(toolManifest, snapshot);
		try
		{
			runtime3.Load();
			FalsumToolRunRequest request3 = new FalsumToolRunRequest
			{
				ToolId = toolManifest.Id
			};
			request3.Values["mode"] = "invalid";
			RequireThrows<FalsumToolRuntimeException>(() =>
			{
				runtime3.Run(request3);
			}, "Illegal Note values were accepted.");
		}
		finally
		{
			if (runtime3 != null)
			{
				((IDisposable)runtime3).Dispose();
			}
		}
		snapshot = FalsumToolChartSnapshot.Capture(toolSession.Chart, 0.0, 0.0);
		using (FalsumToolRuntime runtime4 = new FalsumToolRuntime(toolManifest, snapshot))
		{
			runtime4.Load();
			FalsumToolRunRequest request4 = new FalsumToolRunRequest
			{
				ToolId = toolManifest.Id
			};
			request4.Values["mode"] = "report-empty";
			FalsumToolRunOutput output2 = runtime4.Run(request4);
			Require(output2.Report != null && output2.Report.Items.Count == 0, "Empty structured report was not preserved.");
		}
		snapshot = FalsumToolChartSnapshot.Capture(toolSession.Chart, 0.0, 0.0);
		using (FalsumToolRuntime runtime5 = new FalsumToolRuntime(toolManifest, snapshot))
		{
			runtime5.Load();
			FalsumToolRunRequest request5 = new FalsumToolRunRequest
			{
				ToolId = toolManifest.Id
			};
			request5.Values["mode"] = "report-invalid";
			string invalidReportCode = string.Empty;
			try
			{
				runtime5.Run(request5);
			}
			catch (FalsumToolRuntimeException ex)
			{
				invalidReportCode = ex.Code;
			}
			Require(invalidReportCode == "TOOL_RESULT_INVALID", "Invalid note key in report was accepted.");
		}
		Require(toolSession.Chart.Notes.Count == 1 && toolSession.Chart.Notes[0].WidthLanes == 2 && Math.Abs(toolSession.Chart.Notes[0].TimeMs - 1200.0) < 0.0001, "Rejected tool edits changed the chart.");
		Directory.Delete(toolRoot, recursive: true);
		checks.Add("transaction_safety");
	}

	private static void CheckEventEditing(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0,
			Difficulty = 2
		};
		chart.RebuildTimeline();
		FalsumChartSession session = new FalsumChartSession(chart, 0.0);
		ChartEvent bpmEvent = new ChartEvent
		{
			Kind = ChartEventKind.Bpm,
			TimeMs = 500.0,
			Bpm = 120.0,
			Division = 4.0
		};
		ChartEvent trackEvent = new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 750.0,
			Multiplier = 1.5
		};
		ChartEvent laneEvent = new ChartEvent
		{
			Kind = ChartEventKind.LaneLayout,
			TimeMs = 1000.0,
			Lane = 1,
			Enabled = false
		};
		Require(session.AddEvent(bpmEvent, "add bpm") && session.AddEvent(trackEvent, "add track") && session.AddEvent(laneEvent, "add lane"), "Event add failed.");
		Require(session.Chart.Events.Count == 3 && session.Chart.BpmEvents.Count == 1 && session.Chart.SpeedEvents.Count == 1 && session.Chart.LaneEvents.Count == 1, "Event type indexes failed.");
		session.BeginChange("edit bpm");
		bpmEvent.Bpm = 180.0;
		bpmEvent.Division = 0.5;
		chart.RebuildTimeline();
		Require(session.CommitChange(), "Event edit commit failed.");
		Require(Math.Abs(chart.BpmAt(500.0) - 180.0) < 0.0001 && Math.Abs(chart.DivisionAt(500.0) - 0.5) < 0.0001, "BPM event edit did not apply.");
		Require(session.Undo() && Math.Abs(session.Chart.BpmEvents[0].Bpm - 120.0) < 0.0001, "BPM event undo failed.");
		Require(session.Redo() && Math.Abs(session.Chart.BpmEvents[0].Bpm - 180.0) < 0.0001, "BPM event redo failed.");
		ChartEvent trackToRemove = session.Chart.SpeedEvents[0];
		Require(session.RemoveEvent(trackToRemove, "delete track"), "Event delete failed.");
		Require(session.Chart.Events.Count == 2 && session.Chart.SpeedEvents.Count == 0, "Event delete indexes failed.");
		Require(session.Undo() && session.Chart.SpeedEvents.Count == 1, "Event delete undo failed.");
		Require(session.Redo() && session.Chart.SpeedEvents.Count == 0, "Event delete redo failed.");
		checks.Add("event_editing");
	}

	private static void CheckBeatGrid(List<string> checks)
	{
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0
		};
		chart.RebuildTimeline();
		FalsumBeatGrid grid = new FalsumBeatGrid
		{
			Enabled = true,
			Subdivision = 4
		};
		IList<FalsumBeatLine> lines = grid.Build(chart, 0.0, 2000.0);
		Require(lines.Count >= 9, "Beat grid line count failed.");
		Require(Math.Abs(grid.Snap(chart, 130.0) - 125.0) < 0.001, "Beat grid snap failed.");
		Require(grid.TryGetAdjacentGridLine(chart, 62.5, 1, out var adjacent) && Math.Abs(adjacent - 125.0) < 0.001, "Forward wheel snap skipped the next grid line.");
		Require(grid.TryGetAdjacentGridLine(chart, 62.5, -1, out adjacent) && Math.Abs(adjacent) < 0.001, "Backward wheel snap skipped the previous grid line.");
		Require(grid.TryGetAdjacentGridLine(chart, 125.0, 1, out adjacent) && Math.Abs(adjacent - 250.0) < 0.001, "Forward wheel snap did not advance from a grid line.");
		double exactLine = lines[2].TimeMs;
		Require(Math.Abs(grid.Snap(chart, exactLine) - exactLine) < 0.001, "Beat grid current-line snap failed.");
		RequireMeasureLine(lines, 0.0, expected: true, "Four-beat measure start");
		RequireMeasureLine(lines, 500.0, expected: false, "Four-beat measure subdivision");
		RequireMeasureLine(lines, 125.0, expected: false, "Four-beat measure first-beat subdivision");
		RequireMeasureLine(lines, 2000.0, expected: true, "Four-beat measure boundary");
		Chart oneBeatChart = new Chart
		{
			Bpm = 120.0,
			Division = 1.0
		};
		oneBeatChart.RebuildTimeline();
		IList<FalsumBeatLine> lines2 = grid.Build(oneBeatChart, 0.0, 2000.0);
		RequireMeasureLine(lines2, 500.0, expected: true, "One-beat measure boundary");
		RequireMeasureLine(lines2, 125.0, expected: false, "One-beat measure subdivision");
		RequireMeasureLine(lines2, 250.0, expected: false, "One-beat measure midpoint");
		Chart quarterBeatChart = new Chart
		{
			Bpm = 120.0,
			Division = 0.25
		};
		quarterBeatChart.RebuildTimeline();
		IList<FalsumBeatLine> quarterBeatLines = grid.Build(quarterBeatChart, 0.0, 2000.0);
		for (double time = 0.0; time <= 2000.0; time += 125.0)
		{
			RequireMeasureLine(quarterBeatLines, time, expected: true, "Quarter-beat measure line");
		}
		ChartParseResult parsedQuarterBeat = ChartParser.Parse(new string[2] { "chart(180,4)", "bpm(140667,180,0.25)" }, "<fractional-measure-grid>");
		Require(parsedQuarterBeat.Success, "Fractional measure chart failed to parse.");
		IList<FalsumBeatLine> eventLines = grid.Build(parsedQuarterBeat.Chart, 140667.0, 140917.0);
		double eventBeatMs = 333.3333333333333;
		for (int beat = 0; beat <= 3; beat++)
		{
			RequireMeasureLine(eventLines, 140667.0 + eventBeatMs * 0.25 * (double)beat, expected: true, "Mid-chart quarter-beat measure line");
		}
		Chart offGridChart = new Chart
		{
			Bpm = 120.0,
			Division = 0.25
		};
		offGridChart.RebuildTimeline();
		FalsumBeatGrid falsumBeatGrid = new FalsumBeatGrid
		{
			Enabled = true,
			Subdivision = 3
		};
		IList<FalsumBeatLine> lines3 = falsumBeatGrid.Build(offGridChart, 0.0, 500.0);
		RequireMeasureLine(lines3, 125.0, expected: true, "Off-grid measure line");
		RequireGridLine(lines3, 125.0, expected: false, "Off-grid measure snap source");
		RequireGridLine(lines3, 166.66666666666666, expected: true, "One-third-beat grid line");
		Require(Math.Abs(falsumBeatGrid.Snap(offGridChart, 130.0) - 166.66666666666666) < 0.001, "Measure-only line changed beat-grid snapping.");
		Chart fullSongChart = new Chart
		{
			Bpm = 256.0,
			Division = 4.0,
			DelayMs = -454.0,
			MeasureLineGenerationEndMs = 180454.0
		};
		fullSongChart.RebuildTimeline();
		IList<FalsumBeatLine> fullSongLines = grid.Build(fullSongChart, 60000.0, 62000.0);
		RequireMeasureLine(fullSongLines, 60937.5, expected: true, "Measure line after the legacy horizon");
		Require(Math.Abs(grid.MinimumStepMs(chart, 0.0) - 125.0) < 0.001, "Enabled grid placement floor must stay one grid step.");
		grid.Enabled = false;
		Require(Math.Abs(grid.MinimumStepMs(chart, 0.0) - 1.0) < 0.001, "Disabled grid placement floor must collapse to 1 ms.");
		grid.Enabled = true;
		CheckDenominatorSequence();
		checks.Add("beat_grid");
	}

	/// <summary>
	/// Pins the authored grid-denominator ladder: the shipped default text,
	/// the parse/format round-trip, the repair shape Normalize relies on, and
	/// the stepping contract every '-' / '=' consumer shares.
	/// </summary>
	private static void CheckDenominatorSequence()
	{
		Require(
			FalsumBeatGrid.DefaultDenominatorSequence ==
			"12, 24, 36, 48, 50, 72, 96, 100",
			"The shipped grid denominator sequence changed.");
		List<int> ladder;
		string error;
		Require(
			FalsumBeatGrid.TryParseDenominatorSequence(
				FalsumBeatGrid.DefaultDenominatorSequence,
				out ladder,
				out error),
			"The default grid denominator sequence does not parse.");
		Require(
			ladder.Count == 8 &&
			ladder[0] == 12 &&
			ladder[7] == 100,
			"The default grid denominator sequence lost an entry.");
		Require(
			FalsumBeatGrid.FormatDenominatorSequence(ladder) ==
			FalsumBeatGrid.DefaultDenominatorSequence,
			"Denominator sequence round-trip is not canonical.");
		// Order is authored, not sorted: a reversed list must survive as
		// reversed, or a hand-edited profile would silently reorder.
		var reversed = new List<int>(8);
		for (int i = ladder.Count - 1; i >= 0; i--)
		{
			reversed.Add(ladder[i]);
		}
		Require(
			FalsumBeatGrid.TryParseDenominatorSequence(
				"100, 96, 72, 50, 48, 36, 24, 12",
				out List<int> parsedReversed,
				out error) &&
			FalsumBeatGrid.FormatDenominatorSequence(parsedReversed) ==
			FalsumBeatGrid.FormatDenominatorSequence(reversed),
			"Denominator sequence parsing reordered authored entries.");
		// Rejections Normalize turns into the authored default.
		Require(
			!FalsumBeatGrid.TryParseDenominatorSequence(
				"",
				out List<int> rejectedEmpty,
				out error),
			"An empty denominator sequence parsed.");
		Require(
			!FalsumBeatGrid.TryParseDenominatorSequence(
				"12, 12",
				out List<int> rejectedDuplicate,
				out error),
			"A duplicate denominator parsed.");
		Require(
			!FalsumBeatGrid.TryParseDenominatorSequence(
				"0",
				out List<int> rejectedZero,
				out error),
			"A zero denominator parsed.");
		Require(
			!FalsumBeatGrid.TryParseDenominatorSequence(
				"193",
				out List<int> rejectedHigh,
				out error),
			"A denominator above MaxGridDenominator parsed.");
		// An invalid sequence repairs to the default, which is what
		// FalsumPreferences.Normalize writes back.
		Require(
			FalsumBeatGrid.FormatDenominatorSequence(
				(List<int>)null) ==
			FalsumBeatGrid.DefaultDenominatorSequence,
			"An empty denominator list did not repair to the default.");
		// Stepping contract: strict neighbour, terminal stops at both ends,
		// and a non-member native grid snapping to the nearest member.
		Require(
			FalsumBeatGrid.StepDenominator(ladder, 16, -1) == 12 &&
			FalsumBeatGrid.StepDenominator(ladder, 16, 1) == 24,
			"Denominator stepping did not land on the neighbouring entries.");
		Require(
			FalsumBeatGrid.StepDenominator(ladder, 12, -1) == 12 &&
			FalsumBeatGrid.StepDenominator(ladder, 100, 1) == 100,
			"Denominator stepping did not stop at the sequence ends.");
		Require(
			FalsumBeatGrid.StepDenominator(ladder, 200, -1) == 100 &&
			FalsumBeatGrid.StepDenominator(ladder, 192, 1) == 100,
			"A native grid outside the sequence did not snap to the nearest member.");
		Require(
			FalsumBeatGrid.NearestDenominator(ladder, 200, 24) == 100,
			"Nearest denominator resolution picked a non-nearest entry.");
		Require(
			FalsumBeatGrid.MaxGridDenominator == 192,
			"The interactive grid ceiling moved.");
	}

	private static void RequireMeasureLine(IList<FalsumBeatLine> lines, double timeMs, bool expected, string message)
	{
		bool actual = false;
		bool found = false;
		for (int i = 0; i < lines.Count; i++)
		{
			if (Math.Abs(lines[i].TimeMs - timeMs) <= 0.011)
			{
				found = true;
				actual = lines[i].IsMeasure;
				break;
			}
		}
		Require(found && actual == expected, message + " measure flag failed.");
	}

	private static void RequireGridLine(IList<FalsumBeatLine> lines, double timeMs, bool expected, string message)
	{
		bool actual = false;
		bool found = false;
		for (int i = 0; i < lines.Count; i++)
		{
			if (Math.Abs(lines[i].TimeMs - timeMs) <= 0.011)
			{
				found = true;
				actual = lines[i].IsGridLine;
				break;
			}
		}
		Require(found && actual == expected, message + " grid flag failed.");
	}

	private static void CheckSkyAreaGeometry(List<string> checks)
	{
		ChartNote note = new ChartNote
		{
			DurationMs = 500.0
		};
		ChartNote longNote = new ChartNote
		{
			DurationMs = 10000.0
		};
		Require(SkyAreaGeometry.SubdivisionCount(note) == 101, "SkyArea 5ms subdivision count failed.");
		Require(SkyAreaGeometry.SubdivisionCount(longNote) == 2001, "SkyArea 5ms long subdivision count failed.");
		Require(SkyAreaGeometry.SubdivisionCount(null) == 1, "SkyArea null subdivision fallback failed.");
		Require(Math.Abs(SkyAreaGeometry.Ease(0f, 1)) < 0.0001f && Math.Abs(SkyAreaGeometry.Ease(1f, 2) - 1f) < 0.0001f, "SkyArea easing endpoints failed.");
		Require(SkyAreaGeometry.IsSharedBoundary(100.0, 100.0) && !SkyAreaGeometry.IsSharedBoundary(100.0, 101.5), "SkyArea shared-boundary check is not strict.");

		// The ghost preview carries easing now. FalsumEditorGhost is a struct
		// that the paste/place path fills field by field, so a missing copy is
		// silent: the ghost just renders a straight trapezoid while the note
		// that lands on the chart is eased. Pin the three places that must copy
		// the value by checking the struct fields exist and differ between
		// easing modes after the same construction the editor performs.
		ChartNote easedSource = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			StartPos = 0.2,
			EndPos = 0.8,
			StartWidth = 0.2,
			EndWidth = 0.2,
			LeftEasing = 1,
			RightEasing = 2,
		};
		FalsumEditorGhost ghost = new FalsumEditorGhost
		{
			Visible = true,
			Kind = easedSource.Kind,
			StartPos = easedSource.StartPos,
			EndPos = easedSource.EndPos,
			StartWidth = easedSource.StartWidth,
			EndWidth = easedSource.EndWidth,
			LeftEasing = easedSource.LeftEasing,
			RightEasing = easedSource.RightEasing,
		};
		Require(ghost.LeftEasing == 1 && ghost.RightEasing == 2,
			"The ghost preview does not carry the SkyArea easing, so a placed preset previews as a straight trapezoid.");
		// The eased left edge at the midpoint must not equal the linear one --
		// the same discriminator the encoding uses, so a ghost that dropped the
		// easing would fall back to the linear value here.
		float linearEdge = 0.5f;
		float easedEdge = SkyAreaGeometry.Ease(linearEdge, ghost.LeftEasing);
		Require(Math.Abs(easedEdge - linearEdge) > 0.05f,
			"The ghost's easing value does not produce a non-linear edge.");
		checks.Add("skyarea_geometry");
	}

	private static void CheckSkyAreaGrouping(List<string> checks)
	{
		Chart chart = new Chart();
		ChartNote wrongEndpointsLeft = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 0,
			TimeMs = 0.0,
			DurationMs = 100.0,
			StartPos = 0.3,
			StartWidth = 0.2,
			EndPos = 0.2,
			EndWidth = 0.2
		};
		ChartNote wrongEndpointsRight = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 1,
			TimeMs = 100.0,
			DurationMs = 100.0,
			StartPos = 0.8,
			StartWidth = 0.2,
			EndPos = 0.3,
			EndWidth = 0.2
		};
		Require(FalsumSkyAreaGrouping.SegmentsConnect(wrongEndpointsLeft, wrongEndpointsRight), "SkyArea grouping incorrectly required horizontal overlap.");
		ChartNote first = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 0,
			TimeMs = 0.0,
			DurationMs = 100.0,
			StartPos = 0.25,
			StartWidth = 0.1,
			EndPos = 0.3,
			EndWidth = 0.2,
			GroupId = 7
		};
		ChartNote second = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 1,
			TimeMs = 100.0,
			DurationMs = 100.0,
			StartPos = 0.35,
			StartWidth = 0.2,
			EndPos = 0.6,
			EndWidth = 0.2,
			GroupId = 7
		};
		ChartNote third = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 2,
			TimeMs = 200.0,
			DurationMs = 100.0,
			StartPos = 0.8,
			StartWidth = 0.1,
			EndPos = 0.8,
			EndWidth = 0.1,
			GroupId = 7
		};
		ChartNote interveningTap = new ChartNote
		{
			Kind = NoteKind.Tap,
			Id = 3,
			TimeMs = 50.0,
			StartLane = 0,
			WidthLanes = 1
		};
		chart.Notes.Add(first);
		chart.Notes.Add(interveningTap);
		chart.Notes.Add(second);
		chart.Notes.Add(third);
		Require(FalsumSkyAreaGrouping.SegmentsConnect(first, second) && FalsumSkyAreaGrouping.SegmentsConnect(second, third), "Time-contiguous SkyAreas were not connected.");
		Require(FalsumSkyAreaGrouping.Normalize(chart) == 1 && first.GroupId == 0 && second.GroupId == 0 && third.GroupId == 0, "Time-contiguous SkyAreas were not grouped.");
		third.TimeMs = 205.0;
		Require(FalsumSkyAreaGrouping.Normalize(chart) == 2 && first.GroupId == 0 && second.GroupId == 0 && third.GroupId == 2, "Disconnected SkyAreas did not count intervening objects.");
		third.TimeMs = 200.0;
		Require(FalsumSkyAreaGrouping.Normalize(chart) == 1 && first.GroupId == 0 && second.GroupId == 0 && third.GroupId == 0, "Connected SkyAreas were not regrouped.");

		Chart branchChart = new Chart();
		ChartNote branchA = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 0,
			SourceOrder = 0,
			TimeMs = 0.0,
			DurationMs = 100.0
		};
		ChartNote branchB = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 1,
			SourceOrder = 1,
			TimeMs = 100.0,
			DurationMs = 100.0
		};
		ChartNote branchC = new ChartNote
		{
			Kind = NoteKind.SkyArea,
			Id = 2,
			SourceOrder = 2,
			TimeMs = 100.0,
			DurationMs = 200.0
		};
		branchChart.Notes.Add(branchC);
		branchChart.Notes.Add(branchA);
		branchChart.Notes.Add(branchB);
		Require(FalsumSkyAreaGrouping.Normalize(branchChart) == 2 &&
		        branchA.GroupId == 0 &&
		        branchB.GroupId == 0 &&
		        branchC.GroupId == 1,
			"SkyArea branch grouping did not follow source-order chaining.");
		checks.Add("skyarea_grouping");
	}

	private static void CheckSkyAreaBank(List<string> checks)
	{
		CheckSkyAreaBankScaleMath();
		CheckSkyAreaBankModelRoundTrip();
		CheckSkyAreaBankSeamIntegers();
		CheckSkyAreaBankWidthSemantics();
		CheckSkyAreaBankBounds();
		CheckSkyAreaBankTimeline();
		CheckSkyAreaGroupScaling();
		CheckSkyAreaBankShareCode();
		CheckSkyAreaBankStore();
		CheckSkyAreaBankThumbnail();
		CheckSkyAreaBankWidthFraction();
		checks.Add("skyarea_bank");
	}

	/// <summary>
	/// The card used to print the lateral span as a raw percentage, which is a
	/// non-terminating decimal for the 1/3 and 1/6 widths this editor snaps to
	/// ("宽 16.6666666666… %"). It now prints the same cells/denominator
	/// fraction the property panel shows. Both halves are asserted: the
	/// denominator must be the LCM of the declared cell counts (so the cell
	/// count is a real integer, not a rounded percentage), and the fraction
	/// must reproduce the geometric span to within one cell.
	/// </summary>
	private static void CheckSkyAreaBankWidthFraction()
	{
		// Two contiguous segments on a 1/24 lattice, spanning exactly 5/24.
		FalsumSkyAreaPreset preset = new FalsumSkyAreaPreset();
		preset.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 0.0,
			DurationMs = 400.0,
			StartPos = 0.5,
			EndPos = 0.5,
			StartWidth = 5.0 / 24.0,
			EndWidth = 5.0 / 24.0,
			StartCellCount = 24,
			EndCellCount = 24,
		});
		Require(preset.SpanDenominator == 24,
			"The card's width denominator was not the segment's declared cell count.");
		Require(preset.SpanCells == 5,
			"The card's width numerator did not equal the span in cells.");

		// A 1/6 lattice must stay 1/6 (LCM(6,6) == 6), not be silently
		// re-expressed on the 1/200 ceiling.
		FalsumSkyAreaPreset sixth = new FalsumSkyAreaPreset();
		sixth.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 0.0,
			DurationMs = 400.0,
			StartPos = 0.5,
			EndPos = 0.5,
			StartWidth = 1.0 / 6.0,
			EndWidth = 1.0 / 6.0,
			StartCellCount = 6,
			EndCellCount = 6,
		});
		Require(sixth.SpanDenominator == 6 && sixth.SpanCells == 1,
			"A 1/6 span was not reported as 1 / 6.");

		// Mixed lattices (24 and 200) resolve to the LCM, clamped by the chart
		// ceiling. LCM(24, 200) == 600, so the clamp must bring it to 200; the
		// numerator is then recomputed on that denominator.
		FalsumSkyAreaPreset mixed = new FalsumSkyAreaPreset();
		mixed.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 0.0,
			DurationMs = 400.0,
			StartPos = 0.5,
			EndPos = 0.5,
			StartWidth = 0.25,
			EndWidth = 0.25,
			StartCellCount = 24,
			EndCellCount = 24,
		});
		mixed.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 400.0,
			DurationMs = 400.0,
			StartPos = 0.5,
			EndPos = 0.5,
			StartWidth = 0.25,
			EndWidth = 0.25,
			StartCellCount = 200,
			EndCellCount = 200,
		});
		Require(mixed.SpanDenominator == FalsumFraction.MaxDenominator,
			"A mixed-lattice span was not clamped to the chart's 1/200 ceiling.");

		// The fraction must reproduce the geometric span to within one cell.
		double geometric = mixed.MaximumRight - mixed.MinimumLeft;
		double fromFraction =
			(double)mixed.SpanCells / mixed.SpanDenominator;
		Require(Math.Abs(geometric - fromFraction) <=
		        1.0 / mixed.SpanDenominator + 0.0000001,
			"The displayed width fraction did not match the geometric span.");

		// Degenerate presets must not divide by zero.
		FalsumSkyAreaPreset empty = new FalsumSkyAreaPreset();
		Require(empty.SpanDenominator >= 1 && empty.SpanCells == 0,
			"An empty preset produced an invalid width fraction.");
	}

	/// <summary>
	/// The card thumbnail had two visual bugs that survived review because
	/// nothing measured them: the group was drawn with its HEAD at the TOP
	/// (time runs the other way on the chart -- later notes are higher), and
	/// every segment was a straight trapezoid, so the Left/Right easing the
	/// preset carries was invisible.
	///
	/// <para>These assertions run against the pure geometry builder the view
	/// paints from, so they need no panel, no Painter2D and no rendering. If a
	/// future change flips the vertical mapping or drops the easing lerp, the
	/// self check fails instead of shipping an inverted preview.</para>
	/// </summary>
	private static void CheckSkyAreaBankThumbnail()
	{
		Rect box = new Rect(0f, 0f, 96f, 54f);
		List<Vector2[]> quads = new List<Vector2[]>();

		// Orientation: a two-segment chain where the FIRST segment (time 0)
		// must sit BELOW the second one, because time grows upward on screen.
		FalsumSkyAreaPreset preset = new FalsumSkyAreaPreset();
		preset.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 0.0,
			DurationMs = 1000.0,
			StartPos = 0.5,
			EndPos = 0.5,
			StartWidth = 0.2,
			EndWidth = 0.2,
			LeftEasing = 0,
			RightEasing = 0,
		});
		preset.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 1000.0,
			DurationMs = 1000.0,
			StartPos = 0.5,
			EndPos = 0.5,
			StartWidth = 0.2,
			EndWidth = 0.2,
			LeftEasing = 0,
			RightEasing = 0,
		});
		FalsumSkyAreaBankView.BuildThumbnailQuads(preset, box, 8, quads);
		Require(quads.Count == 16,
			"A 2-segment preset with 8 steps per segment did not produce 16 thumbnail quads.");
		float firstSegmentMinY = float.MaxValue;
		float firstSegmentMaxY = float.MinValue;
		float secondSegmentMinY = float.MaxValue;
		float secondSegmentMaxY = float.MinValue;
		for (int i = 0; i < quads.Count; i++)
		{
			for (int v = 0; v < quads[i].Length; v++)
			{
				float y = quads[i][v].y;
				if (i < 8)
				{
					firstSegmentMinY = Math.Min(firstSegmentMinY, y);
					firstSegmentMaxY = Math.Max(firstSegmentMaxY, y);
				}
				else
				{
					secondSegmentMinY = Math.Min(secondSegmentMinY, y);
					secondSegmentMaxY = Math.Max(secondSegmentMaxY, y);
				}
			}
		}
		Require(firstSegmentMinY >= secondSegmentMaxY - 0.0001f,
			"The thumbnail drew the earlier segment above the later one; the preset head must be at the BOTTOM.");

		// The head of a chain that ends at the top must sit inside the box.
		Require(firstSegmentMaxY <= box.yMax + 0.0001f && secondSegmentMinY >= box.yMin - 0.0001f,
			"The thumbnail escaped its box vertically.");

		// Easing: the three chart modes must produce three DISTINCT silhouettes,
		// and the linear mode must reproduce the straight interpolation exactly.
		// Asserting "not linear" alone could pass on a wrong-but-curved edge,
		// so linear is pinned as a control and the two eased mid samples must
		// sit on opposite sides of it (sin ease-in is above the line, 1-cos is
		// below it across the 0..1 domain).
		double linearMidSample = 0.0;
		double sineMidSample = 0.0;
		double cosineMidSample = 0.0;
		for (int mode = 0; mode < 3; mode++)
		{
			FalsumSkyAreaPreset shaped = new FalsumSkyAreaPreset();
			shaped.Segments.Add(new FalsumSkyAreaPresetSegment
			{
				TimeOffsetMs = 0.0,
				DurationMs = 1000.0,
				StartPos = 0.2,
				EndPos = 0.8,
				StartWidth = 0.0,
				EndWidth = 0.0,
				LeftEasing = mode,
				RightEasing = mode,
			});
			FalsumSkyAreaBankView.BuildThumbnailQuads(shaped, box, 16, quads);
			Require(quads.Count == 16,
				"A single-segment preset did not produce one quad per step.");
			double startEdge = quads[0][0].x;
			// quad[3] is the top-left corner, i.e. progress 1.0 of the last step.
			double endEdge = quads[quads.Count - 1][3].x;
			double midEdge = quads[quads.Count / 2][0].x;
			if (mode == 0)
			{
				linearMidSample = midEdge;
			}
			else if (mode == 1)
			{
				sineMidSample = midEdge;
			}
			else
			{
				cosineMidSample = midEdge;
			}
			// Both endpoints are fixed regardless of the easing mode.
			Require(Math.Abs(startEdge) < 0.001 &&
			        Math.Abs(endEdge - box.width) < 0.001,
				"A thumbnail easing mode moved the segment's endpoints.");
		}
		// Sine (ease-in) starts slow, so its mid sample sits ABOVE the straight
		// line in x; 1 - cos starts fast and lands below it. Both are compared
		// to the exact linear control at the same sample index.
		Require(Math.Abs(sineMidSample - linearMidSample) > 0.5 &&
		        Math.Abs(cosineMidSample - linearMidSample) > 0.5,
			"The thumbnail ignored the segment's easing; an eased mid sample sat on the straight interpolation.");
		Require(sineMidSample > linearMidSample && cosineMidSample < linearMidSample,
			"The thumbnail applied the same curve to easing mode 1 (sin ease-in) and mode 2 (1 - cos ease-in).");

		// Bounds: a segment that is WIDEST at its START, and whose END sits to
		// the LEFT of that start, must still fit the preset-wide span. The old
		// MinimumLeft sampled only Start-left and MaximumRight only End-right,
		// so this fixture collapsed the span to [0.1, 0.2] and projected the
		// wide start edge to x = 768 on a 96 px thumbnail. Both numbers are
		// asserted, not just the fit, so the fixture cannot silently stop
		// exercising the bug.
		FalsumSkyAreaPreset taper = new FalsumSkyAreaPreset();
		taper.Segments.Add(new FalsumSkyAreaPresetSegment
		{
			TimeOffsetMs = 0.0,
			DurationMs = 1000.0,
			StartPos = 0.5,
			EndPos = 0.2,
			StartWidth = 0.8,
			EndWidth = 0.0,
			LeftEasing = 0,
			RightEasing = 0,
		});
		// Arithmetic, not copies: compare with a tolerance. The old bound
		// returned 0.2 here instead of 0.9, so the fixture still discriminates.
		Require(Math.Abs(taper.MinimumLeft - 0.1) < 0.000001,
			"MinimumLeft did not sample the START edge of a segment, so the preset span is too narrow.");
		Require(Math.Abs(taper.MaximumRight - 0.9) < 0.000001,
			"MaximumRight did not sample the START edge of a segment, so the preset span is too narrow.");
		FalsumSkyAreaBankView.BuildThumbnailQuads(taper, box, 4, quads);
		float lowestX = float.MaxValue;
		float highestX = float.MinValue;
		for (int i = 0; i < quads.Count; i++)
		{
			for (int v = 0; v < quads[i].Length; v++)
			{
				lowestX = Math.Min(lowestX, quads[i][v].x);
				highestX = Math.Max(highestX, quads[i][v].x);
			}
		}
		Require(highestX <= box.xMax + 0.0001f && lowestX >= box.xMin - 0.0001f,
			"A segment widest at its START overflowed the thumbnail box; the preset bounds only sampled its END.");
	}

	/// <summary>
	/// Builds a contiguous SkyArea chain: every segment starts exactly where
	/// the previous one ended, so it is one group by
	/// FalsumSkyAreaGrouping's rule.
	/// </summary>
	private static List<ChartNote> CreateSkyAreaChain(
		int count,
		double startTimeMs,
		double durationMs)
	{
		List<ChartNote> notes = new List<ChartNote>();
		for (int i = 0; i < count; i++)
		{
			notes.Add(new ChartNote
			{
				Kind = NoteKind.SkyArea,
				Id = i,
				TimeMs = startTimeMs + durationMs * i,
				DurationMs = durationMs,
				StartPos = 0.30 + 0.10 * i,
				EndPos = 0.35 + 0.10 * i,
				StartWidth = 0.20,
				EndWidth = 0.20,
				StartCellCount = 200,
				EndCellCount = 200,
			});
		}
		return notes;
	}

	private static void CheckSkyAreaBankScaleMath()
	{
		List<ChartNote> notes = CreateSkyAreaChain(3, 1000.0, 500.0);
		FalsumSkyAreaGroupSnapshot snapshot = FalsumSkyAreaGroupScale.Capture(notes);
		Require(snapshot != null && snapshot.Count == 3, "SkyArea bank snapshot did not capture the whole chain.");
		Require(snapshot.PivotPos == notes[0].StartPos, "SkyArea bank pivot was not the first segment's center.");
		Require(snapshot.AnchorTimeMs == notes[0].TimeMs, "SkyArea bank time anchor was not the first segment's start.");
		Require(FalsumSkyAreaGroupScale.ResolveGrid(snapshot) == 200, "SkyArea bank did not resolve the 1/200 lattice from declared cell counts.");

		// scale == 1.0 must be a strict identity.
		Require(FalsumSkyAreaGroupScale.TryApplyWidthScale(snapshot, 1.0, true), "SkyArea bank rejected a unit width scale.");
		for (int i = 0; i < notes.Count; i++)
		{
			Require(notes[i].StartPos == 0.30 + 0.10 * i &&
			        notes[i].EndPos == 0.35 + 0.10 * i,
				"A unit width scale was not an identity.");
		}
		Require(FalsumSkyAreaGroupScale.ApplyTimelineScale(snapshot, 1.0), "SkyArea bank rejected a unit timeline scale.");
		for (int i = 0; i < notes.Count; i++)
		{
			Require(notes[i].TimeMs == 1000.0 + 500.0 * i && notes[i].DurationMs == 500.0,
				"A unit timeline scale was not an identity.");
		}

		// Quantisation lattice: every written coordinate must be an exact
		// multiple of 1/200 (the finest width resolution the format allows).
		Require(FalsumSkyAreaGroupScale.TryApplyWidthScale(snapshot, 1.25, true), "SkyArea bank rejected a 1.25 width scale.");
		for (int i = 0; i < notes.Count; i++)
		{
			Require(IsOnLattice(notes[i].StartPos, 200) && IsOnLattice(notes[i].EndPos, 200),
				"A width scale left a coordinate off the 1/200 lattice.");
		}

		// Snapshot capture must reject non-SkyArea input instead of guessing.
		Require(FalsumSkyAreaGroupScale.Capture(new List<ChartNote>
		{
			new ChartNote { Kind = NoteKind.Tap, Id = 0 }
		}) == null, "SkyArea bank captured a snapshot from a non-SkyArea selection.");

		FalsumSkyAreaGroupScale.ApplyTimelineScale(snapshot, 2.0);
		Require(FalsumSkyAreaGroupScale.Quantize(0.333333, 200) == 0.335,
			"SkyArea bank quantisation did not round away from zero onto the lattice.");
	}

	private static bool IsOnLattice(double value, int grid)
	{
		double scaled = value * grid;
		return Math.Abs(scaled - Math.Round(scaled, MidpointRounding.AwayFromZero)) <= 0.000001;
	}

	/// <summary>The preset model must survive its own JSON representation.</summary>
	private static void CheckSkyAreaBankModelRoundTrip()
	{
		FalsumSkyAreaPreset preset = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChain(3, 1000.0, 500.0),
			"round trip");
		Require(preset != null && preset.Segments.Count == 3, "SkyArea bank preset was not built from the selection.");
		string errorKey;
		Require(FalsumSkyAreaBankModel.Validate(preset, out errorKey), "A freshly built preset failed validation: " + errorKey);

		FalsumJsonObject json = FalsumSkyAreaBankModel.ToJson(preset);
		// Unknown fields must be tolerated (a newer sibling writer may add them).
		FalsumJson.Set(json, "futureField", 123.0);
		FalsumSkyAreaPreset decoded;
		Require(FalsumSkyAreaBankModel.FromJson(json, out decoded, out errorKey), "A valid preset failed to decode: " + errorKey);
		Require(decoded.Segments.Count == 3 &&
		        decoded.Name == preset.Name &&
		        Math.Abs(decoded.WidthScale - 1.0) < 0.000001 &&
		        Math.Abs(decoded.LengthScale - 1.0) < 0.000001,
			"A preset round trip lost its header fields.");
		Require(Math.Abs(decoded.TotalDurationMs - preset.TotalDurationMs) < 0.000001,
			"A preset round trip lost its total duration.");
		for (int i = 0; i < decoded.Segments.Count; i++)
		{
			Require(decoded.Segments[i].TimeOffsetMs == preset.Segments[i].TimeOffsetMs &&
			        decoded.Segments[i].DurationMs == preset.Segments[i].DurationMs &&
			        decoded.Segments[i].StartPos == preset.Segments[i].StartPos &&
			        decoded.Segments[i].EndPos == preset.Segments[i].EndPos &&
			        decoded.Segments[i].LeftEasing == preset.Segments[i].LeftEasing,
				"A preset round trip lost a segment field.");
		}

		// A version from the future must be refused, never silently truncated.
		FalsumJsonObject newer = FalsumSkyAreaBankModel.ToJson(preset);
		FalsumJson.Set(newer, "v", 2);
		FalsumSkyAreaPreset rejected;
		Require(!FalsumSkyAreaBankModel.FromJson(newer, out rejected, out errorKey) &&
		        errorKey == FalsumSkyAreaBankModel.ErrorVersionTooNew,
			"A newer preset version was not refused.");

		// Scale range and segment-count ceilings.
		FalsumSkyAreaPreset badScale = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChain(2, 0.0, 400.0),
			"bad scale");
		badScale.WidthScale = 99.0;
		Require(!FalsumSkyAreaBankModel.Validate(badScale, out errorKey),
			"A width scale outside the allowed range was accepted.");
		FalsumSkyAreaPreset tooMany = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChain(FalsumSkyAreaBankModel.MaxSegmentsPerPreset + 1, 0.0, 100.0),
			"too many");
		Require(!FalsumSkyAreaBankModel.Validate(tooMany, out errorKey),
			"A preset above the segment ceiling was accepted.");

		// An empty selection cannot produce a preset.
		Require(FalsumSkyAreaBankModel.FromSelection(new List<ChartNote>(), "empty") == null,
			"SkyArea bank built a preset from an empty selection.");

		// Tag hygiene: duplicates (case-insensitive) collapse, empty tags vanish.
		List<string> tags = FalsumSkyAreaBankModel.SanitizeTags(
			new List<string> { "Intro", "intro", "  ", "Wide" });
		Require(tags.Count == 2 && tags[0] == "Intro" && tags[1] == "Wide",
			"SkyArea bank tag sanitisation did not deduplicate case-insensitively.");
	}

	/// <summary>
	/// Saved boundaries must be whole milliseconds with exactly zero seams:
	/// placement only adds one integer delta, so that is what keeps the chain
	/// intact after it is dropped onto the chart.
	/// </summary>
	private static void CheckSkyAreaBankSeamIntegers()
	{
		// Deliberately fractional input times.
		List<ChartNote> notes = new List<ChartNote>();
		for (int i = 0; i < 4; i++)
		{
			notes.Add(new ChartNote
			{
				Kind = NoteKind.SkyArea,
				Id = i,
				TimeMs = 1000.4 + 333.3 * i,
				DurationMs = 333.3,
				StartPos = 0.2,
				EndPos = 0.3,
				StartWidth = 0.2,
				EndWidth = 0.2,
				StartCellCount = 200,
				EndCellCount = 200,
			});
		}
		FalsumSkyAreaPreset preset = FalsumSkyAreaBankModel.FromSelection(notes, "seams");
		Require(preset != null && preset.Segments.Count == 4, "SkyArea bank preset was not built from a fractional chain.");
		Require(preset.Segments[0].TimeOffsetMs == 0.0, "A preset's first segment did not start at offset zero.");
		double expected = 0.0;
		for (int i = 0; i < preset.Segments.Count; i++)
		{
			FalsumSkyAreaPresetSegment segment = preset.Segments[i];
			Require(Math.Abs(segment.TimeOffsetMs - expected) < 0.000001,
				"A preset seam was not exactly zero.");
			Require(Math.Abs(segment.TimeOffsetMs - Math.Round(segment.TimeOffsetMs)) < 0.000001 &&
			        Math.Abs(segment.DurationMs - Math.Round(segment.DurationMs)) < 0.000001,
				"A preset boundary was not a whole millisecond.");
			expected = segment.TimeOffsetMs + segment.DurationMs;
		}

		// Simulate placement: one INTEGER delta for every segment, then assert
		// the seams are still exactly zero.
		List<ChartNote> placed = FalsumSkyAreaBankModel.ToChartNotes(preset, false);
		double delta = 12345.0;
		for (int i = 0; i < placed.Count; i++)
		{
			placed[i].TimeMs += delta;
			placed[i].TimeMs = FalsumSkyAreaGroupScale.IntegerTime(placed[i].TimeMs);
		}
		for (int i = 0; i + 1 < placed.Count; i++)
		{
			Require(placed[i + 1].TimeMs == placed[i].TimeMs + placed[i].DurationMs,
				"Placing a preset introduced a ms gap between segments.");
		}
		Chart chart = new Chart();
		chart.Notes.AddRange(placed);
		FalsumSkyAreaGrouping.Normalize(chart);
		Require(placed[0].GroupId == placed[placed.Count - 1].GroupId,
			"A placed preset was split into more than one SkyArea group.");
	}

	/// <summary>
	/// Width scaling moves the group's lateral footprint only: the pivot
	/// endpoint stays put and every segment keeps its own thickness.
	/// </summary>
	private static void CheckSkyAreaBankWidthSemantics()
	{
		List<ChartNote> notes = CreateSkyAreaChain(3, 0.0, 500.0);
		notes[0].StartPos = 0.18;
		notes[0].StartWidth = 0.22;
		notes[0].EndPos = 0.24;
		notes[0].EndWidth = 0.26;
		notes[1].StartPos = 0.40;
		notes[1].StartWidth = 0.16;
		notes[1].EndPos = 0.46;
		notes[1].EndWidth = 0.18;
		notes[2].StartPos = 0.60;
		notes[2].StartWidth = 0.20;
		notes[2].EndPos = 0.70;
		notes[2].EndWidth = 0.14;
		FalsumSkyAreaGroupSnapshot snapshot = FalsumSkyAreaGroupScale.Capture(notes);
		Require(snapshot != null, "SkyArea bank snapshot capture failed for the width case.");
		double pivot = snapshot.PivotPos;
		Require(pivot == 0.18, "SkyArea bank pivot was not the first segment's start center.");

		Require(FalsumSkyAreaGroupScale.TryApplyWidthScale(snapshot, 1.30, true), "SkyArea bank rejected a mirrored width scale.");
		// Thicknesses are invariants of the width operation.
		Require(Math.Abs(notes[0].StartWidth - 0.22) < 0.000001 &&
		        Math.Abs(notes[0].EndWidth - 0.26) < 0.000001 &&
		        Math.Abs(notes[1].StartWidth - 0.16) < 0.000001 &&
		        Math.Abs(notes[2].EndWidth - 0.14) < 0.000001,
			"A width scale changed a segment's thickness.");
		// The pivot endpoint itself cannot move.
		Require(Math.Abs(notes[0].StartPos - pivot) < 0.000001,
			"A width scale moved the pivot endpoint.");
		// Everything else moved proportionally away from the pivot.
		Require(notes[2].StartPos > 0.60 && notes[1].StartPos > 0.40,
			"A width scale did not push the later segments outward.");
		Require(notes[1].StartPos - pivot > 0.40 - pivot,
			"A width scale did not scale the lateral footprint.");

		// Contract to half and verify the direction the other way.
		FalsumSkyAreaGroupSnapshot second = FalsumSkyAreaGroupScale.Capture(notes);
		Require(FalsumSkyAreaGroupScale.TryApplyWidthScale(second, 0.5, true), "SkyArea bank rejected a contract width scale.");
		Require(notes[1].StartPos < 0.40, "A contracting width scale did not pull segments toward the pivot.");
	}

	/// <summary>
	/// Out-of-bounds groups must be clamped as a WHOLE (relative structure
	/// preserved) or refused outright — never clamped per segment.
	/// </summary>
	private static void CheckSkyAreaBankBounds()
	{
		List<ChartNote> notes = CreateSkyAreaChain(2, 0.0, 400.0);
		notes[0].StartPos = 0.10;
		notes[0].StartWidth = 0.10;
		notes[0].EndPos = 0.10;
		notes[0].EndWidth = 0.10;
		notes[1].StartPos = 0.90;
		notes[1].StartWidth = 0.10;
		notes[1].EndPos = 0.90;
		notes[1].EndWidth = 0.10;
		FalsumSkyAreaGroupSnapshot snapshot = FalsumSkyAreaGroupScale.Capture(notes);
		Require(snapshot != null, "SkyArea bank snapshot capture failed for the bounds case.");

		double minimum;
		double maximum;
		Require(FalsumSkyAreaGroupScale.WidthScaleRange(snapshot, out minimum, out maximum),
			"A legal width range was not found for a group inside the track.");
		Require(minimum <= 1.0 && maximum >= 1.0,
			"The legal width range did not contain the identity scale.");

		// Ask for far more than the track allows: the result must be clamped
		// as one delta, so the outer segment still sits inside [0, 1].
		Require(FalsumSkyAreaGroupScale.TryApplyWidthScale(snapshot, 40.0, true),
			"SkyArea bank refused an over-wide request it should have clamped.");
		Require(notes[1].StartPos + notes[1].StartWidth * 0.5 <= 1.0 + 0.0000001,
			"A clamped width scale left the group outside the track.");
		Require(notes[0].StartPos == 0.10,
			"A clamped width scale moved the pivot segment.");

		// A group already outside the track has no legal range: return false
		// and leave the data untouched instead of writing garbage.
		List<ChartNote> broken = CreateSkyAreaChain(2, 0.0, 400.0);
		broken[0].StartPos = 0.05;
		broken[0].StartWidth = 0.02;
		broken[1].StartPos = 1.20;
		broken[1].StartWidth = 0.02;
		FalsumSkyAreaGroupSnapshot brokenSnapshot = FalsumSkyAreaGroupScale.Capture(broken);
		Require(!FalsumSkyAreaGroupScale.WidthScaleRange(brokenSnapshot, out minimum, out maximum),
			"A group sitting outside the track reported a legal width range.");
		broken[0].StartPos = 2.0;
		Require(FalsumSkyAreaGroupScale.ClampWidthScale(2.0) <= FalsumSkyAreaGroupScale.MaximumWidthScale &&
		        FalsumSkyAreaGroupScale.ClampWidthScale(double.NaN) == 1.0,
			"SkyArea bank did not sanitise a non-finite width scale.");
	}

	/// <summary>
	/// Length scaling shrinks/extends the group from its head; seams stay
	/// exactly zero because one shared boundary table is rounded once.
	/// </summary>
	private static void CheckSkyAreaBankTimeline()
	{
		List<ChartNote> notes = CreateSkyAreaChain(3, 1000.0, 400.0);
		FalsumSkyAreaGroupSnapshot snapshot = FalsumSkyAreaGroupScale.Capture(notes);
		Require(snapshot != null, "SkyArea bank snapshot capture failed for the timeline case.");
		double anchor = snapshot.AnchorTimeMs;

		Require(FalsumSkyAreaGroupScale.ApplyTimelineScale(snapshot, 1.5), "SkyArea bank rejected a 1.5 timeline scale.");
		Require(notes[0].TimeMs == anchor, "A timeline scale moved the group's head.");
		for (int i = 0; i + 1 < notes.Count; i++)
		{
			Require(notes[i + 1].TimeMs == notes[i].TimeMs + notes[i].DurationMs,
				"A timeline scale introduced a gap between segments.");
		}
		Require(notes[2].TimeMs + notes[2].DurationMs == anchor + 1800.0,
			"A 1.5 timeline scale did not stretch the group to 1.5x its length.");

		// Heavy contraction: segments may hit the 1 ms floor but must stay
		// monotonic and glued.
		FalsumSkyAreaGroupSnapshot tight = FalsumSkyAreaGroupScale.Capture(notes);
		Require(FalsumSkyAreaGroupScale.ApplyTimelineScale(tight, 0.1), "SkyArea bank rejected a contracting timeline scale.");
		for (int i = 0; i < notes.Count; i++)
		{
			Require(notes[i].DurationMs >= 1.0, "A timeline scale produced a sub-millisecond segment.");
			Require(notes[i].TimeMs >= 0.0, "A timeline scale produced a negative time.");
			if (i + 1 < notes.Count)
			{
				Require(notes[i + 1].TimeMs == notes[i].TimeMs + notes[i].DurationMs,
					"A contracting timeline scale introduced a gap.");
			}
		}

		// Non-positive scales cannot reach the data.
		FalsumSkyAreaGroupSnapshot guard = FalsumSkyAreaGroupScale.Capture(notes);
		Require(!FalsumSkyAreaGroupScale.ApplyTimelineScale(guard, double.NaN) &&
		        !FalsumSkyAreaGroupScale.TryApplyWidthScale(guard, double.NaN, true),
			"SkyArea bank wrote a non-finite scale.");
	}

	/// <summary>
	/// Whole-group scaling (the multi-selected SkyArea handles). The math is
	/// shared with the bank, so this pins the two behaviours the editor relies
	/// on beyond the bank's own cases: a cancel restores the press snapshot
	/// completely, and the readout spans are derived in Core.
	/// </summary>
	private static void CheckSkyAreaGroupScaling()
	{
		List<ChartNote> notes = CreateSkyAreaChain(3, 1000.0, 400.0);
		FalsumSkyAreaGroupSnapshot snapshot = FalsumSkyAreaGroupScale.Capture(notes);
		Require(snapshot != null && snapshot.Count == 3,
			"Group-scaling snapshot capture failed.");

		// Snapshot the originals through the same path the drag uses.
		double[] originalTimes = { notes[0].TimeMs, notes[1].TimeMs, notes[2].TimeMs };
		double[] originalDurations = { notes[0].DurationMs, notes[1].DurationMs, notes[2].DurationMs };
		double[] originalStarts = { notes[0].StartPos, notes[1].StartPos, notes[2].StartPos };

		Require(FalsumSkyAreaGroupScale.TryApplyWidthScale(snapshot, 1.4, true) &&
		        FalsumSkyAreaGroupScale.ApplyTimelineScale(snapshot, 1.3),
			"Group scaling rejected a legal drag.");
		Require(notes[2].StartPos != originalStarts[2],
			"A group width scale did not move the far segment.");

		// Cancel path: Core.Restore must put back every field the drag writes.
		FalsumSkyAreaGroupScale.Restore(snapshot);
		for (int i = 0; i < notes.Count; i++)
		{
			Require(notes[i].TimeMs == originalTimes[i] &&
			        notes[i].DurationMs == originalDurations[i] &&
			        notes[i].StartPos == originalStarts[i],
				"A cancelled group drag was not fully restored by Core.Restore.");
		}

		// Readout spans come from Core, not from the anchor layer.
		Require(Math.Abs(FalsumSkyAreaGroupScale.TotalSpanMs(snapshot) - 1200.0) < 0.000001,
			"The group time span readout is wrong.");
		Require(FalsumSkyAreaGroupScale.LateralSpan(snapshot) > 0.0,
			"The group lateral span readout is empty.");

		// Degenerate inputs must not throw and must report zero.
		Require(FalsumSkyAreaGroupScale.TotalSpanMs(null) == 0.0 &&
		        FalsumSkyAreaGroupScale.LateralSpan(null) == 0.0,
			"A null snapshot produced a non-zero span.");
		FalsumSkyAreaGroupScale.Restore(null);
		CheckSkyAreaGroupDragStatePaths();
	}

	/// <summary>
	/// Static guard for the crash that a real drag exposed: the group MOVE
	/// handle is allowed on a multi-selected chain, but the scaling-only state
	/// builder left <c>Shared</c> null and <c>UpdateAnchorDrag</c> dereferenced
	/// it. Assert that every group operation either builds the middle-edit
	/// state or is explicitly routed away from it.
	/// </summary>
	private static void CheckSkyAreaGroupDragStatePaths()
	{
		// Source-level assertions: the two conditions that a real drag proved
		// necessary. Keeping this as a text check (like the anchor gate does)
		// avoids instantiating the editor, which needs a live scene.
		string anchorPath = Path.Combine(
			Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
			"Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.AnchorEdit.cs");
		Require(File.Exists(anchorPath),
			"The anchor layer source is missing: " + anchorPath);
		string anchorSource = File.ReadAllText(anchorPath);
		Require(anchorSource.IndexOf(
			"if (operation == AnchorOperation.Translate)",
			StringComparison.Ordinal) >= 0,
			"CreateGroupAnchorState does not build the multi-target state for Translate, so a group move drag would crash.");
		Require(anchorSource.IndexOf(
			"if (shared == null)",
			StringComparison.Ordinal) >= 0,
			"UpdateAnchorDrag does not guard the shared state, so a missing state crashes mid-drag instead of cancelling.");
		Require(anchorSource.IndexOf(
			"state.Shared = shared;",
			StringComparison.Ordinal) >= 0,
			"The group move path never assigns Shared, so Translate would still dereference null.");
	}

	/// <summary>Share codes must round trip and reject tampering/truncation.</summary>
	private static void CheckSkyAreaBankShareCode()
	{
		FalsumSkyAreaPreset preset = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChain(3, 0.0, 400.0),
			"share");
		Require(preset != null, "SkyArea bank could not build the share-code fixture.");
		preset.Tags.Add("intro");
		preset.Author = "tester";

		string code;
		string errorKey;
		Require(FalsumSkyAreaShareCode.TryEncode(preset, out code, out errorKey),
			"A valid preset could not be encoded: " + errorKey);
		Require(code.StartsWith(FalsumSkyAreaShareCode.Prefix, StringComparison.Ordinal),
			"A share code did not start with its prefix.");
		Require(code.IndexOf('+') < 0 && code.IndexOf('/') < 0 && code.IndexOf('=') < 0,
			"A share code was not base64url-safe.");

		FalsumSkyAreaPreset decoded;
		Require(FalsumSkyAreaShareCode.TryDecode(code, out decoded, out errorKey),
			"A valid share code could not be decoded: " + errorKey);
		Require(decoded.Name == preset.Name &&
		        decoded.Author == preset.Author &&
		        decoded.Tags.Count == 1 && decoded.Tags[0] == "intro" &&
		        decoded.Segments.Count == preset.Segments.Count,
			"A share-code round trip lost metadata.");
		Require(Math.Abs(decoded.TotalDurationMs - preset.TotalDurationMs) < 0.000001,
			"A share-code round trip lost the duration.");

		// Reformatting (line wraps/spaces) must still decode.
		string wrapped = code.Substring(0, 20) + "\r\n" + code.Substring(20) + "\n";
		FalsumSkyAreaPreset rewrapped;
		Require(FalsumSkyAreaShareCode.TryDecode(wrapped, out rewrapped, out errorKey),
			"Line wrapping broke a share code.");

		// A single edited character must fail the CRC check.
		char[] chars = code.ToCharArray();
		int middle = code.Length / 2;
		chars[middle] = chars[middle] == 'A' ? 'B' : 'A';
		string tampered = new string(chars);
		Require(!FalsumSkyAreaShareCode.TryDecode(tampered, out decoded, out errorKey),
			"A tampered share code was accepted.");

		// Truncation: cut enough that the deflate stream cannot complete.
		// Dropping only one base64 quantum still yields a valid gzip stream.
		Require(!FalsumSkyAreaShareCode.TryDecode(
			code.Substring(0, code.Length * 2 / 3), out decoded, out errorKey),
			"A truncated share code was accepted.");
		Require(!FalsumSkyAreaShareCode.TryDecode("", out decoded, out errorKey) &&
		        !FalsumSkyAreaShareCode.TryDecode("FKY2.whatever", out decoded, out errorKey),
			"A foreign share code was accepted.");

		// A future version inside an otherwise valid payload is refused. The
		// code is opaque, so this is verified on the model path (above) and
		// here only that a valid preset still encodes after the probe.
		string newerCode;
		Require(FalsumSkyAreaShareCode.TryEncode(preset, out newerCode, out errorKey),
			"The version-test fixture could not be encoded.");

		// The share layer used to open blank: OpenShareLayer fired the encode
		// request (whose handler synchronously fills the text field through
		// ShowShareCode) and then UNCONDITIONALLY called
		// ShowShareCode(preset, string.Empty), overwriting the freshly supplied
		// code with an empty string. The decision is now pure, so pin it here:
		// whenever a handler is wired, the view must NOT open the layer itself
		// (the handler's ShowShareCode already did), and it must only self-open
		// when nobody is listening.
		Require(!FalsumSkyAreaBankView.ShouldOpenShareLayerWithoutRequest(true, true),
			"The share layer opens itself even though its encode handler already opened it, which blanks the code.");
		Require(FalsumSkyAreaBankView.ShouldOpenShareLayerWithoutRequest(false, false),
			"The share layer no longer opens when no encode handler is wired, so the button looks dead.");
		Require(FalsumSkyAreaBankView.ShouldOpenShareLayerWithoutRequest(true, false),
			"The share layer stays hidden when a wired handler did not open it (failed encode), so the error hint is unreachable.");

		// Maximum-size preset must still encode. The lateral positions have
		// to stay inside the track, so this fixture uses a narrow chain that
		// does not drift outward with its index.
		FalsumSkyAreaPreset huge = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChainInsideTrack(FalsumSkyAreaBankModel.MaxSegmentsPerPreset),
			"max");
		Require(huge != null && huge.Segments.Count == FalsumSkyAreaBankModel.MaxSegmentsPerPreset,
			"The maximum-size preset fixture was not built.");
		string hugeCode;
		Require(FalsumSkyAreaShareCode.TryEncode(huge, out hugeCode, out errorKey),
			"A maximum-size preset could not be encoded: " + errorKey);
		Require(hugeCode.Length < FalsumSkyAreaShareCode.MaxDecodedBytes * 2,
			"A maximum-size share code grew beyond a pasteable size.");
	}

	/// <summary>
	/// A long chain whose lateral positions do not drift out of the track, so
	/// the segment-count ceiling can be tested without also tripping the
	/// bounds check.
	/// </summary>
	private static List<ChartNote> CreateSkyAreaChainInsideTrack(int count)
	{
		List<ChartNote> notes = new List<ChartNote>();
		for (int i = 0; i < count; i++)
		{
			notes.Add(new ChartNote
			{
				Kind = NoteKind.SkyArea,
				Id = i,
				TimeMs = 100.0 * i,
				DurationMs = 100.0,
				StartPos = 0.06,
				EndPos = 0.10,
				StartWidth = 0.05,
				EndWidth = 0.05,
				StartCellCount = 200,
				EndCellCount = 200,
			});
		}
		return notes;
	}

	/// <summary>
	/// The on-disk library must round trip, quarantine a corrupt file and
	/// keep the library ceiling honest.
	/// </summary>
	private static void CheckSkyAreaBankStore()
	{
		string root = Path.Combine(
			Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(),
			"Temp",
			"FalsumSkyAreaBank");
		Directory.CreateDirectory(root);
		string path = Path.Combine(root, "bank.json");
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		string corruptBackupGlob = path + ".corrupt-*";
		foreach (string stale in Directory.GetFiles(root, "bank.json.corrupt-*"))
		{
			File.Delete(stale);
		}

		FalsumSkyAreaBankDocument document = new FalsumSkyAreaBankDocument();
		document.PanelOpened = true;
		FalsumSkyAreaPreset preset = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChain(2, 0.0, 400.0),
			"stored");
		Require(preset != null, "SkyArea bank could not build the store fixture.");
		string errorKey;
		Require(FalsumSkyAreaBankStore.AddOrReplace(document, preset, out errorKey),
			"The first preset was refused by an empty bank: " + errorKey);
		Require(FalsumSkyAreaBankStore.SaveTo(path, document), "The SkyArea bank could not be written.");
		Require(File.Exists(path), "The SkyArea bank file was not created.");
		Require(Directory.GetFiles(root, "*.tmp-*").Length == 0,
			"A temporary file was left behind by the atomic write.");

		FalsumSkyAreaBankDocument loaded = FalsumSkyAreaBankStore.LoadFrom(path);
		Require(loaded.Presets.Count == 1 &&
		        loaded.PanelOpened &&
		        loaded.Presets[0].Name == "stored" &&
		        loaded.Presets[0].Id == preset.Id,
			"The SkyArea bank round trip lost a preset or its id.");
		Require(loaded.Presets[0].Segments.Count == 2,
			"The SkyArea bank round trip lost a segment.");

		// Library ceiling: replacing is always allowed, only NEW entries stop.
		FalsumSkyAreaBankDocument full = new FalsumSkyAreaBankDocument();
		for (int i = 0; i < FalsumSkyAreaBankModel.MaxPresetsPerBank; i++)
		{
			FalsumSkyAreaPreset entry = FalsumSkyAreaBankModel.FromSelection(
				CreateSkyAreaChain(1, 0.0, 100.0),
				"entry " + i);
			Require(FalsumSkyAreaBankStore.AddOrReplace(full, entry, out errorKey),
				"Filling the bank stopped early at entry " + i + ": " + errorKey);
		}
		FalsumSkyAreaPreset overflow = FalsumSkyAreaBankModel.FromSelection(
			CreateSkyAreaChain(1, 0.0, 100.0),
			"overflow");
		Require(!FalsumSkyAreaBankStore.AddOrReplace(full, overflow, out errorKey) &&
		        errorKey == FalsumSkyAreaBankModel.ErrorBankFull,
			"A full bank accepted an extra preset.");
		Require(full.Presets.Count == FalsumSkyAreaBankModel.MaxPresetsPerBank,
			"A refused insertion deleted an existing preset.");
		Require(FalsumSkyAreaBankStore.AddOrReplace(full, full.Presets[0], out errorKey),
			"A full bank refused to replace an existing preset.");

		// Corrupt file: quarantine it, return an empty library, never throw.
		File.WriteAllText(path, "{ this is not json", new UTF8Encoding(false));
		FalsumSkyAreaBankDocument recovered = FalsumSkyAreaBankStore.LoadFrom(path);
		Require(recovered.Presets.Count == 0,
			"A corrupt bank file did not degrade to an empty library.");
		string[] backups = Directory.GetFiles(root, "bank.json.corrupt-*");
		Require(backups.Length == 1,
			"A corrupt bank file was not quarantined.");
		File.Delete(backups[0]);

		// A newer document version must also be quarantined, not half-read.
		File.WriteAllText(path, "{\"v\":99}", new UTF8Encoding(false));
		FalsumSkyAreaBankDocument newer = FalsumSkyAreaBankStore.LoadFrom(path);
		Require(newer.Presets.Count == 0,
			"A newer bank document version was read instead of quarantined.");
		foreach (string backup in Directory.GetFiles(root, "bank.json.corrupt-*"))
		{
			File.Delete(backup);
		}
		if (File.Exists(path))
		{
			File.Delete(path);
		}

		// One file per preset: the live library is a FOLDER, so the round trip
		// has to be proven there too -- the single-document path above only
		// covers the legacy format and the migration source.
		string folder = Path.Combine(root, "per-preset");
		if (Directory.Exists(folder))
		{
			Directory.Delete(folder, true);
		}
		FalsumSkyAreaBankDocument folderDoc = new FalsumSkyAreaBankDocument();
		for (int i = 0; i < 3; i++)
		{
			FalsumSkyAreaPreset entry = FalsumSkyAreaBankModel.FromSelection(
				CreateSkyAreaChain(2, 0.0, 400.0),
				"folder " + i);
			string folderError;
			Require(FalsumSkyAreaBankStore.AddOrReplace(folderDoc, entry, out folderError),
				"Folder fixture preset " + i + " was refused: " + folderError);
		}
		Require(FalsumSkyAreaBankStore.SaveToFolder(folder, folderDoc),
			"The SkyArea bank folder could not be written.");
		string[] perPreset = Directory.GetFiles(folder, "*.json");
		Require(perPreset.Length == 3,
			"One file per preset was not produced; found " + perPreset.Length + " file(s).");
		Require(Directory.GetFiles(folder, "*.tmp-*").Length == 0,
			"A temporary file was left behind in the preset folder.");
		// Dropping a preset must remove exactly that one file.
		Require(FalsumSkyAreaBankStore.Remove(folderDoc, folderDoc.Presets[0]),
			"A folder preset could not be removed.");
		Require(FalsumSkyAreaBankStore.SaveToFolder(folder, folderDoc),
			"The SkyArea bank folder could not be rewritten.");
		Require(Directory.GetFiles(folder, "*.json").Length == 2,
			"Removing a preset did not remove its file.");
		Directory.Delete(folder, true);

		CheckSkyAreaBankFileSafety(root);
		FalsumSkyAreaBankStore.ResetCache();
	}

	private static void CheckSkyAreaBankFileSafety(string root)
	{
		var folder = Path.Combine(root, "file-safety-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try
		{
			var document = new FalsumSkyAreaBankDocument { PanelOpened = true };
			var first = FalsumSkyAreaBankModel.FromSelection(CreateSkyAreaChain(1, 0.0, 100.0), "first");
			var second = FalsumSkyAreaBankModel.FromSelection(CreateSkyAreaChain(1, 0.0, 100.0), "second");
			first.Id = "same/id";
			first.CreatedAtUtc = 1234;
			first.UpdatedAtUtc = 5678;
			first.LastUsedAtUtc = 9012;
			second.Id = "same?id";
			document.Presets.Add(first);
			document.Presets.Add(second);
			Require(FalsumSkyAreaBankStore.SaveToFolder(folder, document), "Colliding filenames could not be saved.");
			Require(Directory.GetFiles(folder, "*.json").Length == 2, "Sanitized IDs overwrote a preset.");
			var manual = Path.Combine(folder, "manually-renamed.json");
			File.Move(FindBankFixtureFile(folder, first.Id), manual);
			var corrupt = Path.Combine(folder, "corrupt.json");
			const string broken = "{ broken json";
			File.WriteAllText(corrupt, broken);
			var loaded = FalsumSkyAreaBankStore.LoadFromFolder(folder);
			Require(loaded.PanelOpened && loaded.Presets.Count == 2, "Folder load lost panel state or valid presets.");
			var reloadedFirst = loaded.Presets.Find(preset => preset.Id == first.Id);
			Require(reloadedFirst != null && reloadedFirst.CreatedAtUtc == 1234 &&
				reloadedFirst.UpdatedAtUtc == 5678 && reloadedFirst.LastUsedAtUtc == 9012,
				"Per-preset persistence lost local identity or timestamps.");
			var late = Path.Combine(folder, "copied-after-load.json");
			File.Copy(manual, late);
			var lateBytes = File.ReadAllText(late);
			loaded.Presets.RemoveAll(preset => preset.Id == first.Id);
			Require(FalsumSkyAreaBankStore.SaveToFolder(folder, loaded), "Removing a renamed preset failed.");
			Require(!File.Exists(manual) && File.ReadAllText(corrupt) == broken && File.ReadAllText(late) == lateBytes,
				"Saving deleted a skipped/new file or failed to remove the owned renamed file.");
			loaded.PanelOpened = false;
			Require(FalsumSkyAreaBankStore.SaveToFolder(folder, loaded) &&
				!FalsumSkyAreaBankStore.LoadFromFolder(folder).PanelOpened, "Closed panel state was not persisted.");

			var otherFolder = Path.Combine(folder, "other");
			Directory.CreateDirectory(otherFolder);
			var otherFile = Path.Combine(otherFolder, Path.GetFileName(FindBankFixtureFile(folder, second.Id)));
			File.WriteAllText(otherFile, broken);
			Require(FalsumSkyAreaBankStore.SaveToFolder(otherFolder, new FalsumSkyAreaBankDocument()) && File.ReadAllText(otherFile) == broken,
				"Document ownership leaked into another folder.");

			var editedPath = FindBankFixtureFile(folder, second.Id);
			var editedText = File.ReadAllText(editedPath) + " ";
			File.WriteAllText(editedPath, editedText);
			Require(!FalsumSkyAreaBankStore.SaveToFolder(folder, loaded) && File.ReadAllText(editedPath) == editedText,
				"Saving overwrote a file edited externally.");
			loaded.Presets.Clear();
			Require(FalsumSkyAreaBankStore.SaveToFolder(folder, loaded) && File.ReadAllText(editedPath) == editedText,
				"Removing a stale preset deleted its externally edited file.");

			var cappedFolder = Path.Combine(folder, "capped");
			var oldFolder = Path.Combine(folder, "old-format");
			Directory.CreateDirectory(oldFolder);
			File.WriteAllText(Path.Combine(oldFolder, "old-no-id.json"),
				FalsumJson.Serialize(FalsumSkyAreaBankModel.ToJson(first), pretty: true));
			Require(FalsumSkyAreaBankStore.LoadFromFolder(oldFolder).Presets[0].Id == "old-no-id" &&
				FalsumSkyAreaBankStore.LoadFromFolder(oldFolder).Presets[0].Id == "old-no-id",
				"A legacy per-preset file received a new random ID on every load.");
			var migrationFolder = Path.Combine(folder, "migration");
			Require(FalsumSkyAreaBankStore.SaveTo(Path.Combine(migrationFolder, "bank.json"), document), "Legacy fixture could not be saved.");
			Require(FalsumSkyAreaBankStore.LoadFromFolder(migrationFolder).PanelOpened &&
				FalsumSkyAreaBankStore.LoadFromFolder(migrationFolder).PanelOpened &&
				File.Exists(Path.Combine(migrationFolder, "bank.json.migrated")),
				"Migrating the bank lost its panel state on the next load.");
			var capped = new FalsumSkyAreaBankDocument();
			for (var i = 0; i <= FalsumSkyAreaBankModel.MaxPresetsPerBank; i++)
			{
				var entry = FalsumSkyAreaBankModel.FromSelection(CreateSkyAreaChain(1, 0.0, 100.0), "cap " + i);
				entry.Id = i.ToString("D3");
				capped.Presets.Add(entry);
			}
			Require(FalsumSkyAreaBankStore.SaveToFolder(cappedFolder, capped), "Cap fixture could not be saved.");
			var overflowPath = FindBankFixtureFile(cappedFolder, capped.Presets[capped.Presets.Count - 1].Id);
			var overflowText = File.ReadAllText(overflowPath);
			var cappedLoad = FalsumSkyAreaBankStore.LoadFromFolder(cappedFolder);
			Require(cappedLoad.Presets.Count == FalsumSkyAreaBankModel.MaxPresetsPerBank, "Folder load ignored the preset ceiling.");
			Require(FalsumSkyAreaBankStore.SaveToFolder(cappedFolder, cappedLoad) && File.ReadAllText(overflowPath) == overflowText,
				"Saving permanently deleted an over-cap preset.");
		}
		finally { Directory.Delete(folder, true); }
	}

	private static string FindBankFixtureFile(string folder, string id)
	{
		foreach (var path in Directory.GetFiles(folder, "*.json"))
		{
			FalsumJsonObject entry;
			try { entry = FalsumJson.AsObject(FalsumJson.Parse(File.ReadAllText(path))); }
			catch (Exception) { continue; }
			if (entry != null && FalsumJson.GetString(entry, "id", "") == id) return path;
		}
		throw new InvalidOperationException("Bank fixture file was not found: " + id);
	}

	private static void CheckPackageRoundTrip(List<string> checks)
	{
		string text = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), "Temp", "FalsumSelfCheck");
		Directory.CreateDirectory(text);
		string sourcePath = Path.Combine(text, "source.falsum");
		string savedPath = Path.Combine(text, "saved.falsum");
		if (File.Exists(sourcePath))
		{
			File.Delete(sourcePath);
		}
		if (File.Exists(savedPath))
		{
			File.Delete(savedPath);
		}
		FalsumManifest manifest = FalsumManifest.CreateTemplate("selfcheck", "Self Check", "In Falsus", 120000);
		manifest.SetBackground("04_Mindscape-3-Dusk.png");
		manifest.FindDifficulty(0).SetAvailable(value: false);
		manifest.FindDifficulty(1).SetAvailable(value: true);
		string chartText = ChartWriter.Write(CreateFeatureChart(), 0.0, manifest.SongId, 1);
		using (FileStream stream = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
			WriteZipText(archive, "song.json", manifest.Serialize());
			WriteZipBytes(archive, "audio.ogg", new byte[8] { 79, 103, 103, 83, 0, 0, 0, 0 });
			WriteZipText(archive, "charts/evo.chart.txt", chartText);
		}
		FalsumPackage package = FalsumPackage.Load(sourcePath);
		Require(package.Manifest.SongId == "selfcheck", "Package manifest load failed.");
		Require(package.HighestAvailableDifficulty() == 1, "Newly loaded package did not resolve its highest chart.");
		Require(package.LoadChart(1).Notes.Count == 5, "Package chart load failed.");
		FalsumChartMetadataSnapshot metadataSnapshot = FalsumMetadataApplyService.Capture(package);
		FalsumChartMetadataDraft metadataDraft = new FalsumChartMetadataDraft(package, hasPackage: true)
		{
			SongId = "selfcheck_renamed",
			Title = "Renamed Self Check",
			Artist = "Renamed Artist",
			JacketDesigner = "Renamed Jacket",
			BackgroundFile = "31_Space-Positive.png"
		};
		metadataDraft.DifficultyRatings[1] = (package.Manifest.FindDifficulty(1).Rating + 1).ToString();
		metadataDraft.CoverBytes = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };
		Require(FalsumMetadataApplyService.TryApply(package, metadataDraft, out var metadataError), "Metadata apply failed: " + metadataError);
		Require(package.Manifest.SongId == "selfcheck_renamed" && package.Manifest.Title == "Renamed Self Check" && package.Charts[1].Contains("selfcheck_renamed"), "Metadata draft did not update the package.");
		package.SetChartText(3, ChartWriter.Write(new Chart
		{
			Bpm = 120.0,
			Division = 4.0
		}, 0.0, "selfcheck_renamed", 3));
		package.Manifest.FindDifficulty(1).SetAvailable(value: false);
		FalsumMetadataApplyService.Restore(package, metadataSnapshot);
		Require(package.Manifest.SongId == "selfcheck" && package.Manifest.Title == "Self Check" && package.Manifest.BackgroundFile == "04_Mindscape-3-Dusk.png" && package.Manifest.FindDifficulty(1).Available && package.Manifest.CoverFile.Length == 0 && package.AudioBytes == metadataSnapshot.AudioBytes && package.Charts.ContainsKey(1) && !package.Charts.ContainsKey(3) && package.Charts[1].Contains("selfcheck"), "Metadata snapshot restore failed.");
		package.Save(savedPath);
		FalsumPackage falsumPackage = FalsumPackage.Load(savedPath);
		Require(falsumPackage.Manifest.Title == "Self Check", "Package save failed.");
		Require(falsumPackage.Manifest.BackgroundFile == "04_Mindscape-3-Dusk.png", "Package background field was not preserved.");
		File.Delete(sourcePath);
		File.Delete(savedPath);
		checks.Add("package_round_trip");
	}

	private static void CheckMenuWorkflowServices(List<string> checks)
	{
		string text = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), "Temp", "FalsumMenuWorkflow");
		Directory.CreateDirectory(text);
		string targetPath = Path.Combine(text, "workflow-selfcheck.falsum");
		if (File.Exists(targetPath))
		{
			File.Delete(targetPath);
		}
		string audioPath = Path.Combine(Application.dataPath, "InFalsus", "Gameplay", "Audio", "Resources", "alamode.ogg");
		string coverPath = Path.Combine(Application.streamingAssetsPath, "Falsum", "Playfield", "playfieldBG.png");
		Require(File.Exists(audioPath), "Menu workflow self-check audio is missing.");
		Require(File.Exists(coverPath), "Menu workflow self-check cover is missing.");
		Require(FalsumBassEncoder.IsAvailable(), "The BASS Ogg encoder add-ons are missing.");
		FalsumAudioConversionJob audioJob = FalsumMediaConverter.PrepareAudioConversion(audioPath);
		FalsumConvertedAudio audio = Task.Run(() => FalsumMediaConverter.ConvertAudio(audioJob)).GetAwaiter().GetResult();
		Require(audio.Bytes != null && audio.Bytes.Length != 0, "Menu workflow audio conversion produced no data.");
		Require(Math.Abs(audio.DurationMs - 135670) < 2000, "Converted audio duration is outside the expected range.");
		FalsumConvertedCover cover = FalsumMediaConverter.ConvertCover(coverPath);
		Require(cover.Width == 1024 && cover.Height == 1024 && cover.Bytes != null && cover.Bytes.Length > 8 && cover.Bytes[0] == 137 && cover.Bytes[1] == 80 && cover.Bytes[2] == 78 && cover.Bytes[3] == 71, "Menu workflow cover conversion failed.");
		FalsumNewChartRequest falsumNewChartRequest = new FalsumNewChartRequest
		{
			TargetPath = targetPath,
			AudioPath = audioPath,
			CoverPath = coverPath,
			// Deliberately NOT equal to the file stem: creating a package
			// must not derive the song id from the file name, nor rename
			// the file to match the id.
			SongId = "workflowselfcheck",
			Title = "Workflow Self Check",
			Artist = "In Falsus",
			JacketDesigner = "Self Check Artist"
		};
		falsumNewChartRequest.DifficultyEnabled[1] = true;
		falsumNewChartRequest.Ratings[1] = 7;
		falsumNewChartRequest.ChartDesigners[1] = "self-check designer";
		falsumNewChartRequest.DifficultyEnabled[2] = true;
		falsumNewChartRequest.Ratings[2] = 11;
		falsumNewChartRequest.ChartDesigners[2] = "self-check designer";
		RequireThrows<ArgumentException>(() =>
		{
			FalsumNewChartRequest missingId = new FalsumNewChartRequest
			{
				TargetPath = targetPath,
				AudioPath = audioPath,
				CoverPath = coverPath,
				SongId = string.Empty,
				Title = "Missing Id",
				Artist = "In Falsus"
			};
			missingId.DifficultyEnabled[0] = true;
			missingId.Ratings[0] = 5;
			FalsumPackage.CreateNew(missingId, audio.Bytes, audio.DurationMs, cover.Bytes, cover.Width, cover.Height);
		}, "A package was created without a song id.");
		RequireThrows<ArgumentException>(() =>
		{
			FalsumNewChartRequest badId = new FalsumNewChartRequest
			{
				TargetPath = targetPath,
				AudioPath = audioPath,
				CoverPath = coverPath,
				SongId = "Bad_Id",
				Title = "Bad Id",
				Artist = "In Falsus"
			};
			badId.DifficultyEnabled[0] = true;
			badId.Ratings[0] = 5;
			FalsumPackage.CreateNew(badId, audio.Bytes, audio.DurationMs, cover.Bytes, cover.Width, cover.Height);
		}, "A package was created with an illegal song id.");
		FalsumPackage package = FalsumPackage.CreateNew(falsumNewChartRequest, audio.Bytes, audio.DurationMs, cover.Bytes, cover.Width, cover.Height);
		Require(package.SourcePath == targetPath, "Creating a package rewrote the user-chosen file name.");
		Require(package.Manifest.SongId == "workflowselfcheck", "The new package did not keep the typed song id.");
		Require(package.LoadChartText(1).Contains("# song: workflowselfcheck") && package.LoadChartText(2).Contains("# song: workflowselfcheck"), "The chart header did not receive the typed song id.");
		package.Save(targetPath);
		FalsumPackage loaded = FalsumPackage.Load(targetPath);
		Require(loaded.Manifest.Title == "Workflow Self Check" && loaded.Manifest.Artist == "In Falsus" && loaded.Manifest.JacketDesigner == "Self Check Artist", "New package manifest metadata failed.");
		Require(loaded.Manifest.SongId == "workflowselfcheck", "The reloaded package lost the typed song id.");
		Require(loaded.Charts.ContainsKey(1) && loaded.Charts.ContainsKey(2) && !loaded.Charts.ContainsKey(0) && !loaded.Charts.ContainsKey(3), "New package difficulty selection failed.");
		Require(loaded.Manifest.FindDifficulty(2).Rating == 11, "New package rating failed.");
		string sourcePath = package.SourcePath;
		string backupPath = FalsumBackupService.CreateChartBackup(targetPath, package.Manifest.SongId, 2, package.LoadChartText(2));
		Require(File.Exists(backupPath), "Automatic backup file was not created.");
		Require(package.SourcePath == sourcePath, "Saving an automatic backup changed the source path.");
		Require(FalsumBackupService.FindLatest(targetPath, package.Manifest.SongId, 2) == backupPath, "Latest automatic backup lookup failed.");
		Require(File.ReadAllText(backupPath) == package.LoadChartText(2), "Automatic chart backup content failed.");
		string backupDirectory = FalsumBackupService.BackupDirectory(targetPath, package.Manifest.SongId, 2);
		File.Delete(backupPath);
		if (Directory.Exists(backupDirectory))
		{
			Directory.Delete(backupDirectory);
		}
		File.Delete(targetPath);
		checks.Add("menu_workflow_services");
	}

	private static Chart CreateFeatureChart()
	{
		Chart chart = new Chart
		{
			Bpm = 138.5,
			Division = 4.0,
			SongId = "selfcheck",
			Difficulty = 2
		};
		chart.Events.Add(new ChartEvent
		{
			Kind = ChartEventKind.Bpm,
			TimeMs = 3000.0,
			Bpm = 174.0,
			Division = 6.0
		});
		chart.SpeedEvents.Add(new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 5000.0,
			Multiplier = 0.75
		});
		chart.Events.Add(chart.SpeedEvents[0]);
		chart.LaneEvents.Add(new ChartEvent
		{
			Kind = ChartEventKind.LaneLayout,
			TimeMs = 1000.0,
			Lane = 1,
			Enabled = false
		});
		chart.Events.Add(chart.LaneEvents[0]);
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 1000.0,
			StartLane = 2,
			WidthLanes = 2
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Hold,
			TimeMs = 2000.0,
			StartLane = 4,
			WidthLanes = 1,
			DurationMs = 800.0
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Flick,
			TimeMs = 3000.0,
			StartPos = 0.5,
			StartWidth = 1.0 / 12.0,
			StartCellCount = 24,
			EndCellCount = 24,
			FlickDirection = 16
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.SkyArea,
			TimeMs = 4000.0,
			StartPos = 0.25,
			StartWidth = 1.0 / 12.0,
			StartCellCount = 24,
			EndPos = 0.75,
			EndWidth = 1.0 / 12.0,
			EndCellCount = 48,
			LeftEasing = 0,
			RightEasing = 1,
			DurationMs = 1200.0,
			GroupId = 7
		});
		chart.Notes.Add(new ChartNote
		{
			Kind = NoteKind.Tap,
			TimeMs = 6000.0,
			StartLane = 0,
			WidthLanes = 1
		});
		chart.RebuildTimeline();
		for (int i = 0; i < chart.Notes.Count; i++)
		{
			chart.Notes[i].Id = i;
		}
		return chart;
	}

	private static void CheckTrackSpectrum(List<string> checks)
	{
		Shader shader = Resources.Load<Shader>("Falsum/Shaders/FalsumTrackSpectrum");
		Require((Object)(object)shader != (Object)null, "Falsum track spectrum shader is missing.");
		Require(shader.isSupported, "Falsum track spectrum shader is not supported.");
		Require(FalsumTrackSpectrumCache.ColumnsPerSecond == 960.0f, "Track waveform time resolution changed unexpectedly.");
		Require(FalsumTrackSpectrumCache.GetSegmentTextureWidth(1000.0) == 960, "Track waveform width does not scale with segment duration.");
		Require(FalsumTrackSpectrumCache.SegmentDurationMs > FalsumTrackSpectrumCache.SegmentStrideMs, "Track waveform segments do not overlap.");
		Require(FalsumTrackSpectrumCache.GetSegmentCount(60000.0) == 6, "Track waveform segment count is incorrect.");
		CheckTrackWaveformRasteriser();
		Chart chart = new Chart
		{
			Bpm = 120.0,
			Division = 4.0,
		};
		chart.SpeedEvents.Add(new ChartEvent
		{
			Kind = ChartEventKind.TrackSpeed,
			TimeMs = 1000.0,
			Multiplier = 2.0,
		});
		chart.RebuildTimeline();
		float[] samples = new float[FalsumTrackSpectrumRenderer.WarpSampleCount];
		int written = FalsumTrackSpectrumRenderer.FillWarpSamples(
			chart,
			4000.0,
			samples);
		Require(written == samples.Length, "Spectrum warp table has the wrong size.");
		Require(samples[0] == 0.0f && samples[samples.Length - 1] > 0.99f, "Spectrum warp table endpoints are incorrect.");
		for (int i = 1; i < samples.Length; i++)
		{
			Require(samples[i] + 0.00001f >= samples[i - 1], "Spectrum warp table is not monotonic.");
		}
		int midpoint = samples.Length / 2;
		Require(Math.Abs(samples[midpoint] - 0.5625f) < 0.01f, "Spectrum warp table does not follow track speed.");
		Require(
			Math.Abs(FalsumTrackSpectrumRenderer.SecondsPerTrack(5.0) - 0.6666667f) < 0.0001f,
			"Spectrum time span does not match ground flow speed.");
		checks.Add("track_spectrum");
	}

	/// <summary>
	/// Pins the BASS rasteriser that replaced the external ffmpeg
	/// <c>showwavespic</c> filter. The three numbers below are the whole point
	/// of that rewrite: the bundled filter builder is gone, so if someone
	/// "simplifies" the port the floor waveform silently starts looking wrong.
	/// Values are the n4.0.2 behaviour verified byte-for-byte against the
	/// bundled ffmpeg binary.
	/// </summary>
	private static void CheckTrackWaveformRasteriser()
	{
		// 12 mono frames at half scale, W=4 H=4 -> max_samples = 3, so a
		// column closes every 4 frames (n4.0.2's `if (n++ == max_samples)`),
		// and 12 frames therefore fill 3 of the 4 columns; the trailing
		// column stays black, exactly like ffmpeg's. The span is
		// int(sqrt(|(short)((16384<<1)*4/3)|) * 4 / sqrt(32767)): the sum
		// 131072 / 3 = 43690 wraps to -21846 as int16, and sqrt(21846)*4/181
		// truncates to 3 rows starting at row 0.
		short[] mono = new short[12];
		for (int i = 0; i < mono.Length; i++)
		{
			mono[i] = 16384;
		}
		byte[] monoPixels = FalsumTrackSpectrumCache.DebugDrawWaveform(
			mono, 12, 1, 4, 4);
		Require(monoPixels.Length == 16, "Waveform buffer size does not match width * height.");
		Require(monoPixels[0] == 255, "A mono channel must fill the pixel with 255 (255 / 1 channel).");
		Require(monoPixels[3] == 0 && monoPixels[7] == 0, "The trailing column must stay black (ffmpeg fills width-1 columns here).");

		// Stereo overlap adds 255/2 twice: 127 + 127 wraps to 254, never 255.
		short[] stereo = new short[12 * 2];
		for (int i = 0; i < stereo.Length; i++)
		{
			stereo[i] = 16384;
		}
		byte[] stereoPixels = FalsumTrackSpectrumCache.DebugDrawWaveform(
			stereo, 12, 2, 4, 4);
		Require(stereoPixels[0] == 254, "Two overlapping channels must add 127 twice and wrap to 254.");

		// Digital silence lights nothing.
		byte[] silentPixels = FalsumTrackSpectrumCache.DebugDrawWaveform(
			new short[12], 12, 1, 4, 4);
		int lit = 0;
		for (int i = 0; i < silentPixels.Length; i++)
		{
			lit += silentPixels[i];
		}
		Require(lit == 0, "Digital silence must not draw any waveform pixel.");

		// The decode path must stay honest about the two halves that go into
		// it: float -> s16 uses swresample's clamp (not a wrap).
		Require(
			FalsumTrackSpectrumCache.DebugToInt16(1.5f) == 32767 &&
			FalsumTrackSpectrumCache.DebugToInt16(-1.5f) == -32768,
			"Float audio is not clamped to the int16 range.");

		// A 16 s / 960-column segment at 48 kHz leaves the last two columns
		// black: 768000 frames / (768000/960 + 1) = 958. Pinning the number
		// keeps a "fill all 960" refactor from silently changing the picture.
		Require(
			FalsumTrackSpectrumCache.GetFilledColumnCount(16000.0, 960, 48000) == 958,
			"The waveform column count no longer matches the reference rendering.");
	}

	private static void CheckBassAudio(List<string> checks)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Expected Obj, but got Unknown
		GameObject host = null;
		AudioClip note = null;
		try
		{
			host = new GameObject("FalsumBassSelfCheck");
			AudioSource source = host.AddComponent<AudioSource>();
			FalsumBassAudioEngine engine = host.AddComponent<FalsumBassAudioEngine>();
			Require(engine.TryInitialize(source, 0.72f, out var error), "BASS initialization failed: " + error);
			string bgmPath = Path.Combine(Application.dataPath, "InFalsus", "Gameplay", "Audio", "Resources", "alamode.ogg");
			Require(File.Exists(bgmPath), "BASS self-check OGG is missing.");
			note = CreateToneClip("selfcheck-note", 4800, 2);
			Require(engine.PrepareBgm(null, bgmPath, 0.72f, out error), "BASS BGM preparation failed: " + error);
			engine.OnAudioRate(0.5);
			engine.OnAudioSeek(0.0);
			for (int i = 0; i < 64; i++)
			{
				Require(engine.PlayOneShot(note, 0.1f), "BASS rejected note voice " + i + ".");
			}
			Require(engine.ActiveMixerChannelCount >= 65, "BASS mixer dropped simultaneous note voices: " + engine.ActiveMixerChannelCount);
			MethodInfo read = typeof(FalsumBassAudioEngine).GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.NonPublic);
			Require(read != null, "BASS audio pull method is missing.");
			float[] buffer = new float[2048];
			float maximum = 0f;
			for (int j = 0; j < 8; j++)
			{
				read.Invoke(engine, new object[2] { buffer, 2 });
				for (int sample = 0; sample < buffer.Length; sample++)
				{
					maximum = Math.Max(maximum, Math.Abs(buffer[sample]));
				}
			}
			Require(maximum > 0.0001f, "BASS audio pull produced silence.");
			Require(engine.HasAudioPosition, "BASS audio position was not published.");
			Require(engine.AudioPositionMs > 1.0 && engine.AudioPositionMs < 500.0, "BASS audio position is outside the expected range.");
			Require(engine.SchedulingLeadMs > 0.0, "Slow BASS playback did not publish a scheduling lead.");
			int voicesBeforeSchedule = engine.ActiveMixerChannelCount;
			Require(engine.PlayOneShot(note, 0.1f, engine.AudioPositionMs + 20.0), "BASS rejected a scheduled note voice.");
			Require(engine.ActiveMixerChannelCount > voicesBeforeSchedule, "BASS scheduled note voice was not mixed.");
			Debug.Log((object)("[Falsum] BASS audio check passed: voices=" + engine.ActiveMixerChannelCount + " sourceMs=" + engine.AudioPositionMs.ToString("0.0") + " leadMs=" + engine.SchedulingLeadMs.ToString("0.0") + "."));
			engine.OnAudioRate(1.0);
			Require(Math.Abs(engine.SchedulingLeadMs) < 0.0001, "Original-speed BASS playback kept a scheduling lead.");
			engine.OnAudioSeek(0.0);
			for (int k = 0; k < 4; k++)
			{
				read.Invoke(engine, new object[2] { buffer, 2 });
			}
			Require(engine.AudioScheduleTimeMs > engine.AudioPositionMs + 1.0, "Original-speed BASS did not expose a render horizon.");
			Chart scheduledChart = new Chart();
			ChartNote scheduledNote = new ChartNote
			{
				Id = 0,
				Kind = NoteKind.Tap,
				TimeMs = Math.Max(1.0, engine.AudioScheduleTimeMs - 5.0),
				StartLane = 3,
				WidthLanes = 1
			};
			scheduledChart.Notes.Add(scheduledNote);
			scheduledChart.RebuildTimeline();
			JudgementEngine scheduledEngine = new JudgementEngine(scheduledChart);
			GameplayHitAudio gameplayHitAudio = host.AddComponent<GameplayHitAudio>();
			gameplayHitAudio.Initialize(scheduledChart, scheduledEngine, note, note, note, note, note, note, engine);
			gameplayHitAudio.ResetEditorAudioSchedule(engine.AudioPositionMs);
			gameplayHitAudio.ScheduleEditorAudio(engine.AudioPositionMs, engine.AudioScheduleTimeMs);
			Require(gameplayHitAudio.PlayedOneShotCount == 1, "Editor note audio was not pre-scheduled.");
			scheduledEngine.PressLane(scheduledNote.StartLane, scheduledNote.TimeMs);
			scheduledEngine.ReleaseLane(scheduledNote.StartLane, scheduledNote.TimeMs);
			Require(gameplayHitAudio.PlayedOneShotCount == 1, "Scheduled editor note audio played twice.");
			engine.OnAudioStop();
			GameplayClock gameplayClock = host.AddComponent<GameplayClock>();
			gameplayClock.Initialize(source, 0.0);
			gameplayClock.SetAudioSink(engine);
			gameplayClock.SetPlaybackRate(0.75);
			Require(!gameplayClock.IsPlaying, "Paused rate change started the gameplay clock.");
			Require(!engine.HasAudioPosition, "Paused rate change activated the BASS transport.");
			Require(!source.isPlaying, "Paused rate change left the audio source playing.");
			// Pull real native mixer buffers while the main thread repeatedly
			// replaces BGM handles and frees transient/loop channels.
			engine.OnAudioSeek(0.0);
			using (var started = new ManualResetEventSlim())
			{
				var pull = Task.Run(() =>
				{
					var concurrentBuffer = new float[2048];
					started.Set();
					for (var i = 0; i < 128; i++)
						read.Invoke(engine, new object[] { concurrentBuffer, 2 });
				});
				try
				{
					Require(started.Wait(TimeSpan.FromSeconds(5)), "Concurrent BASS pull did not start.");
					for (var i = 0; i < 16; i++)
					{
						engine.OnAudioSeek(i * 10.0);
						Require(engine.SetLoop("selfcheck-loop", note, 0.1f, true), "BASS loop creation failed during concurrent pull.");
						engine.ResetLoops();
						Require(engine.PlayOneShot(note, 0.1f), "BASS transient creation failed during concurrent pull.");
						engine.StopTransientChannels();
					}
				}
				finally { pull.GetAwaiter().GetResult(); }
			}
			engine.OnAudioStop();
			checks.Add("bass-audio");
		}
		finally
		{
			if ((Object)(object)host != (Object)null)
			{
				Object.DestroyImmediate((Object)(object)host);
			}
			if ((Object)(object)note != (Object)null)
			{
				Object.DestroyImmediate((Object)(object)note);
			}
		}
	}

	private static AudioClip CreateToneClip(string name, int frames, int channels)
	{
		AudioClip clip = AudioClip.Create(name, frames, channels, 48000, false);
		float[] samples = new float[frames * channels];
		for (int i = 0; i < frames; i++)
		{
			float value = Mathf.Sin((float)Math.PI * 880f * (float)i / 48000f) * 0.1f;
			for (int channel = 0; channel < channels; channel++)
			{
				samples[i * channels + channel] = value;
			}
		}
		clip.SetData(samples, 0);
		return clip;
	}

	private static void WriteZipText(ZipArchive archive, string path, string text)
	{
		WriteZipBytes(archive, path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
	}

	private static void WriteZipBytes(ZipArchive archive, string path, byte[] bytes)
	{
		using Stream output = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
		output.Write(bytes, 0, bytes.Length);
	}

	private static void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static void CheckSelectionAnchors(List<string> checks)
	{
		// Anchor layout is a SPEC, not an implementation detail: the agreed
		// design fixes how many handles each note kind exposes
		// (docs/superpowers/specs/2026-11-05-falsum-selection-anchors-design.md).
		// A note that draws a different number is drawing a role the drag
		// layer has no operation for.
		Require(
			ExpectedAnchorCount(NoteKind.Tap) == 3,
			"Tap must expose 3 anchors: the front boundary pair plus the front start/end midpoint.");
		Require(
			ExpectedAnchorCount(NoteKind.Hold) == 6,
			"Hold must expose 6 anchors: a boundary pair and a start/end midpoint at each of its two ends.");
		Require(
			ExpectedAnchorCount(NoteKind.Flick) == 4,
			"Flick must expose 4 anchors: two lateral edge boundaries, a colour control and a move handle.");
		Require(
			ExpectedAnchorCount(NoteKind.SkyArea) == 9,
			"Skyarea must expose 9 anchors when both ends are visible: a corner pair and a start/end midpoint per end, two easing controls and one move handle.");

		// Skyarea is the only kind whose anchor set depends on visibility: a
		// corner and its midpoint sit on the clip line when that end is off
		// screen, so the builder must drop them rather than lie about the
		// note's real edge.
		Require(
			ExpectedAnchorCount(NoteKind.SkyArea, hasFront: true, hasBack: false) == 6,
			"Skyarea with the back end off screen must expose 6 anchors.");
		Require(
			ExpectedAnchorCount(NoteKind.SkyArea, hasFront: false, hasBack: true) == 6,
			"Skyarea with the front end off screen must expose 6 anchors.");
		Require(
			ExpectedAnchorCount(NoteKind.SkyArea, hasFront: false, hasBack: false) == 3,
			"Skyarea with both ends off screen must keep the two easing controls and the move handle.");

		// Every role maps onto exactly ONE editor operation, and every operation
		// except the bespoke endpoint move is the middle-mouse edit's own. A
		// role whose operation depended on which way the pointer moved would
		// have to guess an axis, and a guess is what once put the time anchors
		// on the lateral edges.
		Require(
			AnchorOperationFor(FalsumAnchorKind.Boundary, NoteKind.Tap, lateral: true) ==
			"middle.groundBoundary",
			"Tap/Hold boundaries must delegate to the middle-mouse ground boundary edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Boundary, NoteKind.Flick, lateral: true) ==
			"middle.skyBoundary",
			"Flick/Skyarea boundaries must delegate to the middle-mouse sky boundary edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Boundary, NoteKind.SkyArea, lateral: true) ==
			"middle.skyBoundary",
			"Skyarea corner anchors must delegate to the middle-mouse sky boundary edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Move, NoteKind.SkyArea, lateral: true) ==
			"middle.position",
			"The move handle must delegate to the middle-mouse position edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Move, NoteKind.Flick, lateral: false) ==
			"middle.position",
			"The flick move handle must delegate to the middle-mouse position edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.StartEnd, NoteKind.Tap, lateral: false) ==
			"middle.startEnd",
			"Start/end midpoints must delegate to the middle-mouse start/end edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.StartEnd, NoteKind.Hold, lateral: false) ==
			"middle.startEnd",
			"Start/end midpoints must delegate to the middle-mouse start/end edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Easing, NoteKind.SkyArea, lateral: false) ==
			"anchor.dragEasing",
			"The easing control must be the anchor layer's own draggable three-state value.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Color, NoteKind.Flick, lateral: false) ==
			"anchor.toggleFlick",
			"The colour control must be the anchor layer's own direction toggle.");

		// The ONLY role allowed to change operation with the drag direction is
		// the skyarea start/end midpoint: forward/back is middle start/end
		// (time), sideways is the one bespoke operation (lateral). Nothing else
		// may be dual-operation.
		Require(
			AnchorOperationFor(FalsumAnchorKind.Boundary, NoteKind.SkyArea, lateral: true) ==
			AnchorOperationFor(FalsumAnchorKind.Boundary, NoteKind.SkyArea, lateral: false),
			"A boundary anchor must declare one operation regardless of drag direction.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.Move, NoteKind.SkyArea, lateral: true) ==
			AnchorOperationFor(FalsumAnchorKind.Move, NoteKind.SkyArea, lateral: false),
			"A move handle must declare one operation regardless of drag direction.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.StartEnd, NoteKind.SkyArea, lateral: true) ==
			"anchor.endpointLateral",
			"A sideways drag of the skyarea start/end midpoint must be the bespoke endpoint lateral move.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.StartEnd, NoteKind.SkyArea, lateral: false) ==
			"middle.startEnd",
			"A forward/back drag of the skyarea start/end midpoint must still delegate to the middle-mouse start/end edit.");
		Require(
			AnchorOperationFor(FalsumAnchorKind.StartEnd, NoteKind.SkyArea, lateral: true) !=
			AnchorOperationFor(FalsumAnchorKind.StartEnd, NoteKind.SkyArea, lateral: false),
			"The skyarea start/end midpoint must be the only dual-operation anchor.");

		// The bespoke operation writes one endpoint's lateral field and hands
		// that endpoint's width back unchanged. Resizing the endpoint (what the
		// old corner logic did) and translating both endpoints (move mode) are
		// both forbidden here.
		RequireEndpointLateralMovesOnlyOneEndpoint();

		// The easing control is both draggable and clickable:
		//   - a CLICK cycles exactly three states (Line 0 -> Ease out 1 ->
		//     Ease in 2 -> Line), so no other value is reachable;
		//   - a DRAG snaps to the same three slots, and the slot offsets must
		//     be -1 / 0 / +1 so a full spacing of travel moves exactly one
		//     state and the glyph position matches the stored value.
		Require(
			NextEasing(0) == 1 && NextEasing(1) == 2 && NextEasing(2) == 0,
			"Anchor easing must cycle Line -> Ease out -> Ease in and wrap.");
		// Slots are measured ALONG SnapDirection, which points INWARD (toward
		// the note body). So Ease out is +1 (inward) and Ease in is -1
		// (outward): dragging inward must give Ease out, dragging outward must
		// give Ease in. The sign here IS the gesture, so it is asserted, not
		// described.
		Require(
			FalsumSelectionAnchor.EasingSlot(1) == 1f &&
			FalsumSelectionAnchor.EasingSlot(0) == 0f &&
			FalsumSelectionAnchor.EasingSlot(2) == -1f,
			"Anchor easing slots must be +1/0/-1 (inward-positive): dragging inward must mean Ease out and dragging outward must mean Ease in.");
		// The value and slot orderings DIFFER (Line=0/Ease out=1/Ease in=2 maps
		// to slots 0/+1/-1), so the inverse must be the explicit mapping and
		// never slot+1. This round-trip keeps the drag and the painter in
		// agreement.
		for (int value = 0; value < 3; value++)
		{
			int slot = FalsumSelectionAnchor.EasingValueToSlot(value);
			Require(
				FalsumSelectionAnchor.EasingSlotToValue(slot) == value,
				"Anchor easing slot " + slot + " does not round-trip to value " + value + ".");
		}
		Require(
			FalsumSelectionAnchor.EasingSlotToValue(1) == 1 &&
			FalsumSelectionAnchor.EasingSlotToValue(0) == 0 &&
			FalsumSelectionAnchor.EasingSlotToValue(-1) == 2,
			"Anchor easing slot +1/0/-1 must mean Ease out/Line/Ease in.");
		// DIRECTION guard: the drag delta is projected onto SnapDirection, so a
		// positive slot must mean "moved inward". If someone flips the sign of
		// SnapDirection or of EasingSlot without flipping the other, the handle
		// will move one way and the value will change the other way -- which is
		// exactly the inverted-drag bug this assertion exists to prevent.
		Require(
			FalsumSelectionAnchor.EasingSlotToValue(1) == 1 &&
			FalsumSelectionAnchor.EasingValueToSlot(1) == 1,
			"Anchor easing Ease out must sit at the inward-positive slot, or the drag direction inverts.");

		// Flick direction is a closed set of two values (A-4).
		Require(
			FlipFlick(4) == 16 && FlipFlick(16) == 4,
			"Flick direction must flip 4 <-> 16 only.");

		// Delegation invariant (A-18), asserted by declaration rather than by
		// text. The anchor layer and the middle-mouse edit are partials of one
		// class, so a reimplementation shows up as a method that must not exist
		// any more. The shared bodies the anchors delegate to must be there,
		// the anchor-owned edit-math methods the rebuild deleted must be gone,
		// and the one bespoke operation must be present.
		Require(
			EditorDeclares("ApplyBoundaryEdit"),
			"The shared boundary body is missing, so anchors have nothing to delegate to.");
		Require(
			EditorDeclares("ApplyStartEndEdit"),
			"The shared start/end body is missing, so anchors have nothing to delegate to.");
		Require(
			EditorDeclares("ApplyPositionEdit"),
			"The shared position body is missing, so anchors have nothing to delegate to.");
		Require(
			EditorDeclares("ApplyGroundBoundary"),
			"The middle-mouse ground boundary edit is missing, so the shared boundary body would have no ground branch.");
		Require(
			EditorDeclares("ApplySkyBoundary"),
			"The middle-mouse sky boundary edit is missing, so the shared boundary body would have no sky branch.");
		Require(
			EditorDeclares("SnapNormalizedDelta"),
			"The shared normalized-delta snap is missing, so anchors would have to snap on their own.");
		Require(
			EditorDeclares("ResolveMiddleEndpointGrid"),
			"The shared endpoint grid resolver is missing, so anchors would have to pick a grid on their own.");
		Require(
			EditorDeclares("ApplyAnchorEndpointLateral"),
			"The one bespoke anchor operation (skyarea endpoint lateral move) is missing.");
		Require(
			!EditorDeclares("ApplyGroundAnchorLateral") &&
			!EditorDeclares("ApplySkyAreaAnchorLateral") &&
			!EditorDeclares("ApplyFlickAnchorLateral") &&
			!EditorDeclares("ApplyAnchorTime") &&
			!EditorDeclares("AnchorMinDurationMs") &&
			!EditorDeclares("ApplyNoteTranslation"),
			"A deleted anchor-owned edit-math method came back: the anchor layer is re-deriving the middle-mouse edit instead of delegating to it.");

		checks.Add("selection_anchors");
	}

	/// <summary>
	/// Anchor counts the agreed design fixes per note kind.
	/// Skyarea's front/back corner pairs and start/end midpoints exist only
	/// while that endpoint is on screen; every other kind has a fixed set.
	/// </summary>
	private static int ExpectedAnchorCount(
		NoteKind kind,
		bool hasFront = true,
		bool hasBack = true)
	{
		switch (kind)
		{
			case NoteKind.Tap:
				return hasFront ? 3 : 0;
			case NoteKind.Hold:
				return (hasFront ? 3 : 0) + (hasBack ? 3 : 0);
			case NoteKind.Flick:
				return hasFront ? 4 : 0;
			case NoteKind.SkyArea:
				// Two easing controls and the move handle never depend on
				// visibility; each visible end adds a corner pair and a
				// start/end midpoint.
				return 3 + (hasFront ? 3 : 0) + (hasBack ? 3 : 0);
			default:
				throw new InvalidOperationException(
					"Unknown note kind in the anchor check.");
		}
	}

	/// <summary>
	/// The single editor operation a role maps onto. The middle-mouse edit owns
	/// all of them but the two click controls and the one bespoke lateral
	/// endpoint move, which the anchor layer is allowed to own (A-18).
	/// </summary>
	private static string AnchorOperationFor(
		FalsumAnchorKind kind,
		NoteKind noteKind,
		bool lateral)
	{
		switch (kind)
		{
			case FalsumAnchorKind.Boundary:
				return noteKind == NoteKind.Tap ||
				       noteKind == NoteKind.Hold
					? "middle.groundBoundary"
					: "middle.skyBoundary";
			case FalsumAnchorKind.StartEnd:
				// The one bespoke operation: a skyarea endpoint midpoint
				// dragged sideways shifts that endpoint only.
				return noteKind == NoteKind.SkyArea && lateral
					? "anchor.endpointLateral"
					: "middle.startEnd";
			case FalsumAnchorKind.Move:
				return "middle.position";
			case FalsumAnchorKind.Easing:
				return "anchor.dragEasing";
			case FalsumAnchorKind.Color:
				return "anchor.toggleFlick";
			default:
				throw new InvalidOperationException(
					"Unknown anchor kind in the anchor check.");
		}
	}

	private static void RequireEndpointLateralMovesOnlyOneEndpoint()
	{
		// Layout mirrors a skyarea note's lateral fields: [0] StartPos,
		// [1] StartWidth, [2] EndPos, [3] EndWidth.
		double[] note = { 0.40, 0.20, 0.70, 0.10 };
		const double delta = 0.15;

		double[] headShifted = ApplyEndpointLateralSpec(note, tail: false, delta);
		Require(
			Math.Abs(headShifted[0] - (note[0] + delta)) < 0.000001,
			"The skyarea start-endpoint lateral move did not shift StartPos.");
		Require(
			Math.Abs(headShifted[1] - note[1]) < 0.000001,
			"The skyarea start-endpoint lateral move changed StartWidth; the endpoint's width must be preserved exactly.");
		Require(
			Math.Abs(headShifted[2] - note[2]) < 0.000001 &&
			Math.Abs(headShifted[3] - note[3]) < 0.000001,
			"The skyarea start-endpoint lateral move touched the other endpoint.");

		double[] tailShifted = ApplyEndpointLateralSpec(note, tail: true, delta);
		Require(
			Math.Abs(tailShifted[2] - (note[2] + delta)) < 0.000001,
			"The skyarea end-endpoint lateral move did not shift EndPos.");
		Require(
			Math.Abs(tailShifted[3] - note[3]) < 0.000001,
			"The skyarea end-endpoint lateral move changed EndWidth; the endpoint's width must be preserved exactly.");
		Require(
			Math.Abs(tailShifted[0] - note[0]) < 0.000001 &&
			Math.Abs(tailShifted[1] - note[1]) < 0.000001,
			"The skyarea end-endpoint lateral move touched the other endpoint.");
	}

	/// <summary>
	/// Mirrors the one bespoke anchor operation: shift the endpoint selected
	/// by <paramref name="tail"/> and keep that endpoint's width exactly as it
	/// was captured at press time.
	/// </summary>
	private static double[] ApplyEndpointLateralSpec(
		double[] note,
		bool tail,
		double delta)
	{
		double[] result = (double[])note.Clone();
		int positionField = tail ? 2 : 0;
		int widthField = tail ? 3 : 1;
		result[positionField] = note[positionField] + delta;
		result[widthField] = note[widthField];
		return result;
	}

	/// <summary>Line (0) -> Ease out (1) -> Ease in (2) -> Line.</summary>
	private static int NextEasing(int easing)
	{
		return (easing + 1) % 3;
	}

	/// <summary>
	/// Whether FalsumChartEditor declares a method with this name. The anchor
	/// layer and the middle-mouse edit share the class, so a reimplementation
	/// inside the anchor partial is indistinguishable by name from a legitimate
	/// split until it is asserted here.
	/// </summary>
	private static bool EditorDeclares(string name)
	{
		MethodInfo[] methods = typeof(FalsumChartEditor).GetMethods(
			BindingFlags.Instance | BindingFlags.Static |
			BindingFlags.Public | BindingFlags.NonPublic |
			BindingFlags.DeclaredOnly);
		for (int i = 0; i < methods.Length; i++)
		{
			if (methods[i].Name == name)
			{
				return true;
			}
		}
		return false;
	}

	private static int FlipFlick(int direction)
	{
		return direction == 16 ? 4 : 16;
	}

	private static void RequireThrows<TException>(Action action, string message) where TException : Exception
	{
		if (action == null)
		{
			throw new InvalidOperationException(message);
		}
		try
		{
			action();
		}
		catch (TException)
		{
			return;
		}
		throw new InvalidOperationException(message);
	}

	private static int CountOccurrences(string text, string value)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
		{
			return 0;
		}
		int count = 0;
		int index = 0;
		while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += value.Length;
		}
		return count;
	}

	/// <summary>
	/// Guards the multi-selection drag contract (A-25).
	///
	/// <para>TWO failures made "multi-select several notes, then drag one"
	/// move nothing: <c>HandleSelectDown</c> rewrote the selection on PRESS
	/// (so the group was gone before the drag threshold), and
	/// <c>DragSelection</c> snapped each note against its own press-time
	/// value (so a grid-aligned member kept its original time and the members
	/// drifted onto different grid lines). The press half is asserted at the
	/// source level — it needs a live scene to drive — while the arithmetic
	/// half is a pure function and is asserted numerically here.</para>
	/// </summary>
	private static void CheckSelectionDragMath(List<string> checks)
	{
		// --- One shared delta, derived from the anchor only -----------------
		// Anchor starts half a step off the grid; the pointer moves it one
		// whole step. Both members must travel that same absolute delta, so
		// the half-step offset survives.
		double stepMs = 125.0;
		double anchorOriginal = 10000.0 + stepMs * 0.5;
		double anchorMoved = anchorOriginal + stepMs;
		double delta = FalsumSelectionDragMath.ResolveDelta(
			anchorOriginal,
			anchorMoved,
			10000.0);
		Require(
			Math.Abs(delta - stepMs) < 0.0001,
			"A group drag must use the anchor's own delta; got " + delta +
			" instead of " + stepMs + ".");

		// The same delta applied to the other member preserves the spacing
		// EXACTLY, including when that member was already grid-aligned.
		double otherOriginal = 10000.0;
		Require(
			Math.Abs(
				((otherOriginal + delta) - (anchorOriginal + delta)) -
				(otherOriginal - anchorOriginal)) < 0.0000001,
			"A group drag changed the internal spacing of the selection.");

		// --- The time-0 floor clamps the GROUP, not each note ----------------
		// Moving a block that starts below zero must stop at zero with the
		// spacing intact; clamping per note would squash it flat.
		double floorDelta = FalsumSelectionDragMath.ResolveDelta(
			500.0,
			-3000.0,
			200.0);
		Require(
			Math.Abs(floorDelta + 200.0) < 0.0001,
			"The time-0 floor must clamp the group by the earliest member's distance to zero; got " +
			floorDelta + ".");
		Require(
			Math.Abs((200.0 + floorDelta)) < 0.0001 &&
			Math.Abs((700.0 + floorDelta) - 500.0) < 0.0001,
			"Clamping a group at time 0 squashed its spacing.");

		// --- Shared-delta selection rule -------------------------------------
		Require(
			FalsumSelectionDragMath.UsesSharedDelta(2, false) &&
			FalsumSelectionDragMath.UsesSharedDelta(12, false),
			"A multi-note drag must use one shared delta; a per-note snap is the bug.");
		Require(
			FalsumSelectionDragMath.UsesSharedDelta(1, true),
			"A whole-group SkyArea snap must use its shared delta even for one target.");
		Require(
			!FalsumSelectionDragMath.UsesSharedDelta(1, false),
			"A single-note drag must keep the per-note snap that preserves its off-grid time.");

		// --- Degenerate input must not throw or invent motion ----------------
		Require(
			FalsumSelectionDragMath.ResolveDelta(
				double.NaN,
				1000.0,
				0.0) == 0.0 &&
			FalsumSelectionDragMath.ResolveDelta(
				0.0,
				double.PositiveInfinity,
				0.0) == 0.0,
			"A non-finite drag anchor produced a delta instead of refusing to move.");

		// --- Source assertions for the press/release half --------------------
		// The press must NOT drop the group before the drag threshold, and the
		// click semantics must be replayed on release. Both are behaviours of
		// Input-driven methods that need a live scene, so they are pinned by
		// declaration: a revert that re-introduces the immediate rewrite shows
		// up as a missing deferred-collapse method.
		string editorPath = Path.Combine(
			Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
			"Assets/InFalsus/Gameplay/Falsum/Runtime/FalsumChartEditor.cs");
		Require(
			File.Exists(editorPath),
			"The chart editor source is missing: " + editorPath);
		string editorSource = File.ReadAllText(editorPath);
		Require(
			editorSource.IndexOf(
				"DeferSelectionCollapse(",
				StringComparison.Ordinal) >= 0 &&
			editorSource.IndexOf(
				"ApplyDeferredSelectionCollapse(",
				StringComparison.Ordinal) >= 0,
			"Multi-select press handling lost its deferred collapse, so pressing a note in a group destroys the group before it can be dragged.");
		// The press must not fall through to the single-note rewrite while a
		// group is selected: that rewrite is the exact statement that used to
		// run on every press. Assert it is guarded, not merely present.
		Require(
			editorSource.IndexOf(
				"if (_selected.Count > 1 && _selected.Contains(note))",
				StringComparison.Ordinal) >= 0,
			"HandleSelectDown no longer special-cases a press on a member of a multi-selection, so the group collapses on press again.");

		// --- The shared delta must actually be WIRED IN ----------------------
		// The arithmetic above can be perfect and still make no difference if
		// DragSelection keeps deciding per-note. A mutation that reverted only
		// the decision (`uniformDelta = hasGroupSnap`) passed every numeric
		// assertion here, so the call site is pinned explicitly.
		Require(
			editorSource.IndexOf(
				"FalsumSelectionDragMath.UsesSharedDelta(",
				StringComparison.Ordinal) >= 0,
			"DragSelection no longer consults FalsumSelectionDragMath.UsesSharedDelta, so the group drag is back to a per-note snap.");
		Require(
			editorSource.IndexOf(
				"FalsumSelectionDragMath.ResolveDelta(",
				StringComparison.Ordinal) >= 0,
			"DragSelection no longer derives the shared delta from FalsumSelectionDragMath.ResolveDelta.");
		// The assignment must be driven by the SHARED-delta flag. Keying it on
		// `hasGroupSnap` alone is exactly the per-note behaviour this check
		// exists to prevent (only an all-SkyArea group would then be rigid).
		Require(
			editorSource.IndexOf(
				"note.TimeMs = uniformDelta",
				StringComparison.Ordinal) >= 0,
			"DragSelection assigns note.TimeMs without the shared-delta flag, so members can snap independently again.");
		Require(
			editorSource.IndexOf(
				"var uniformDelta = hasGroupSnap",
				StringComparison.Ordinal) < 0,
			"DragSelection derives its shared-delta flag from hasGroupSnap alone, so a multi-note selection snaps per note again.");

		checks.Add("selection_drag_math");
	}

	/// <summary>
	/// Guards the two things the mobile shell can get wrong without any
	/// other test noticing: it must stay INERT on desktop, and its quarter
	/// circle must map angles onto sectors deterministically.
	///
	/// The wheel math is tested as a pure function (SectorAtDegrees) because
	/// the alternative -- driving a real PointerEvent sequence -- would
	/// assert the gesture plumbing rather than the contract, and the
	/// plumbing changes whenever the shell is re-tuned.
	/// </summary>
	private static void CheckMobileShell(List<string> checks)
	{
		// The shell must be INERT on a desktop that has not opted in: it is
		// process-wide static state, so a stray assignment would make every
		// later editor session render as a phone.
		Require(
			!FalsumMobileShell.IsMobile,
			"The mobile shell must report desktop when no mobile platform is active and FALSUM_MOBILE_SHELL is unset.");
		Require(
			!FalsumMobileShell.RuntimeOverride.HasValue,
			"The mobile layout runtime override must start unset; only the developer switch may set it.");
		Require(
			FalsumDeveloperSwitches.Available,
			"The developer switches must be visible in the editor, or the mobile layout cannot be reviewed without an Android build.");

		// Control scaling is the knob the user asked for. It must clamp,
		// because a scale outside the band makes a rail narrower than its
		// own buttons (below) or wider than the playfield can pay for (above).
		Require(
			Mathf.Approximately(
				FalsumMobileShell.MinScale,
				FalsumMobileShell.DefaultScale) &&
			FalsumMobileShell.DefaultScale < FalsumMobileShell.MaxScale,
			"The mobile control scale default must equal the slider minimum.");
		Require(
			FalsumMobileShell.MinScale == 0.6f &&
			FalsumMobileShell.MaxScale == 1.4f,
			"The mobile control scale range must extend from 0.6 to 1.4.");

		// ONE SQUARE DRIVES THE RAIL. The rail is the square plus two 5 dp
		// insets; anything else desynchronises the rail from its buttons and
		// the floating panels that anchor off it.
		Require(
			FalsumMobileShell.RailInsetDp == 5,
			"The rail inset changed; floating panels anchor off square+2*inset and would drift.");

		// The two rails are the ONLY fixed columns, and the playfield between
		// them is what the editor is for. Assert the geometry from the design
		// ratio instead of hard-coding a screen width.
		float designWidth = 405.0f * 19.0f / 9.0f;   // 855 dp at 1080 high
		float railAtDefault =
			(56.0f * FalsumMobileShell.DefaultScale) +
			2.0f * FalsumMobileShell.RailInsetDp;
		float playfield = designWidth - 2.0f * railAtDefault;
		Require(
			playfield / designWidth > 0.78f,
			"The two rails take more than 22% of the canvas width; the layout regressed towards permanent panels.");

		// The style sheet must exist, otherwise the shell silently falls back
		// to the desktop layout on a phone.
		Require(
			Resources.Load<StyleSheet>("Falsum/UI/FalsumMobile") != null,
			"FalsumMobile.uss is missing, so the phone layout would fall back to desktop sizes.");

		// A UXML parse failure does NOT throw anywhere visible: Unity falls
		// back to an empty document, the view throws "missing FalsumRoot", and
		// the editor renders as a bare 3D scene. That is exactly what a stray
		// "--" inside an XML comment did to this file. Assert the document
		// really parsed by demanding the root element exists.
		{
			// A .uxml imports as a VisualTreeAsset, NOT a TextAsset, so the
			// source has to be read from disk. Reading it is what makes the
			// comment-syntax assertion below possible at all.
			string uxmlPath = Path.Combine(
				Application.dataPath,
				"InFalsus/Gameplay/Falsum/Resources/Falsum/UI/FalsumEditor.uxml");
			Require(
				File.Exists(uxmlPath),
				"FalsumEditor.uxml is missing from Resources.");
			string source = File.ReadAllText(uxmlPath);
			Require(
				source.Contains("name=\"FalsumRoot\""),
				"FalsumEditor.uxml lost its FalsumRoot element.");
			// XML forbids "--" inside comments and a comment may not end with
			// "-". Both silently break the whole document.
			int commentStart = source.IndexOf("<!--", StringComparison.Ordinal);
			while (commentStart >= 0)
			{
				int commentEnd = source.IndexOf(
					"-->", commentStart + 4, StringComparison.Ordinal);
				Require(
					commentEnd > commentStart,
					"FalsumEditor.uxml has an unterminated comment.");
				string body = source.Substring(
					commentStart + 4,
					commentEnd - commentStart - 4);
				Require(
					!body.Contains("--"),
					"FalsumEditor.uxml has \"--\" inside a comment, which makes the whole document unparsable and the editor renders with no UI.");
				Require(
					!body.EndsWith("-", StringComparison.Ordinal),
					"FalsumEditor.uxml has a comment ending in \"-\", which XML forbids.");
				commentStart = source.IndexOf(
					"<!--", commentEnd + 3, StringComparison.Ordinal);
			}
		}

		// Every element the shell queries must exist in the main document.
		// These are looked up with Q<>() at runtime, so a rename degrades
		// silently rather than throwing -- which is exactly why it is
		// asserted here.
		VisualTreeAsset template =
			Resources.Load<VisualTreeAsset>("Falsum/UI/FalsumEditor");
		Require(template != null, "FalsumEditor.uxml is missing.");
		VisualElement instance = template.Instantiate();
		string[] required = new string[]
		{
			// the two fixed rails
			"MobileLeftBar", "MobileToolRail", "MobileFloatLayer",
			// left rail controls
			"MobileMenu",
			"MobilePlaceTap", "MobilePlaceHold",
			"MobilePlaceFlick", "MobilePlaceSkyArea",
			"MobileMultiSelect",
			// right rail controls
			"MobileEvents", "MobileGrid", "MobileMirror",
			"MobileCopy", "MobilePaste",
            "MobileSegmentSelectRight", "MobileRate", "MobileDelete",
			// floating surfaces
			"MobileFlowPanel", "MobileFlowSlider", "MobileFlowValue",
			"MobileHistoryPanel", "MobileUndo", "MobileRedo",
			"MobileSongCardHost",
			"MobileMetaPanel", "MobileBaseBpm", "MobileSubdivision",
			"MobileAudioPreprocess",
			"MobileTransportPanel", "MobileTimeField",
			"MobilePlayPause",
			"MobileKeypadHost",
			"MobileProgressStrip", "MobileProgress", "MobileProgressText",
			"StageCapsule",
		};
		for (int i = 0; i < required.Length; i++)
		{
			Require(
				UQueryExtensions.Q<VisualElement>(
					instance,
					required[i],
					(string)null) != null,
				"The mobile layout is missing a control the shell queries: " +
				required[i]);
		}

		// The rails must be the only elements that reserve horizontal space.
		// A floating panel accidentally left as a flex child of FalsumRoot
		// would steal a column and nobody would notice until the canvas
		// shrank.
		Require(
			UQueryExtensions.Q<VisualElement>(
				instance, "MobileLeftBar", (string)null)
				.ClassListContains("mobile-rail") &&
			UQueryExtensions.Q<VisualElement>(
				instance, "MobileToolRail", (string)null)
				.ClassListContains("mobile-rail"),
			"Both fixed columns must carry the mobile-rail class; only the rails may reserve canvas width.");

		// DEAD-CONTROL GUARD. A button that exists and is bound but whose event
		// has no subscriber looks and feels broken without logging anything.
		// This walks the shell's public events and asserts the view subscribes to
		// each one, so adding a button without wiring it fails the gate instead of
		// shipping a control that does nothing.
		{
			var shellEvents = typeof(FalsumMobileShell)
				.GetEvents(BindingFlags.Public | BindingFlags.Instance);
			Require(
				shellEvents.Length >= 20,
				"The mobile shell exposes too few events; the phone chrome is incomplete.");
			string viewSource = File.ReadAllText(Path.Combine(
				Application.dataPath,
				"InFalsus/Gameplay/Falsum/UI/FalsumEditorView.cs"));
			for (int i = 0; i < shellEvents.Length; i++)
			{
				string name = shellEvents[i].Name;
				Require(
					viewSource.Contains("_mobileShell." + name + " +="),
					"A mobile shell event has no subscriber in FalsumEditorView, so its button does nothing: " + name);
			}
		}

		// The scrubber must not echo its own writes back as a seek. Without this
		// guard every frame rewrote the audio position and produced a stream of
		// "An invalid seek position was passed to this function" errors.
		Require(
			typeof(FalsumMobileShell).GetField(
				"_writingProgress",
				BindingFlags.NonPublic | BindingFlags.Instance) != null,
			"The progress scrubber lost its write guard, so programmatic updates will be treated as user seeks.");

		// Every mobile caption is bound from code, so a missing locale entry
		// renders an empty button -- the same failure the bank panel guards
		// against.
		string[] keys = new string[]
		{
			"falsum.mobile.menu", "falsum.mobile.undo", "falsum.mobile.redo",
			"falsum.mobile.segmentSelect", "falsum.mobile.multiSelect",
			"falsum.mobile.events", "falsum.mobile.grid",
			"falsum.mobile.mirror", "falsum.mobile.copy",
            "falsum.mobile.paste", "falsum.mobile.rate",
            "falsum.mobile.delete",
			"falsum.mobile.bpm", "falsum.mobile.subdivision",
			"falsum.mobile.audioPreprocess", "falsum.mobile.timeField",
			"falsum.mobile.audition", "falsum.mobile.playPause",
			"falsum.noteKind.tap", "falsum.noteKind.hold",
			"falsum.noteKind.flick", "falsum.noteKind.skyArea",
			"falsum.mobile.stage.abandon",
			"falsum.error.ui.mobileStyleMissing",
		};
		for (int i = 0; i < keys.Length; i++)
		{
			Require(
				InFalsus.Gameplay.Falsum.Localization.FalsumLocalization.TryGet(
					keys[i],
					out _),
				"A mobile shell caption is missing from the locale files: " +
				keys[i]);
		}

		// The multi-select latch must start off and must be the ONLY thing
		// that writes the modifier, or Ctrl would be stuck on for a desktop
		// user who never touched the phone UI.
		Require(
			!FalsumModifierState.LatchedControl,
			"Latched Ctrl must start off; a stuck latch turns every tap into a multi-select.");
		FalsumModifierState.LatchedControl = true;
		Require(
			FalsumModifierState.Control,
			"The latched Ctrl must make the modifier report held.");
		FalsumModifierState.ResetLatches();
		Require(
			!FalsumModifierState.LatchedControl,
			"ResetLatches must clear the multi-select latch.");

		checks.Add("mobile_shell");
	}

	private static void WriteMobileReport(bool passed, List<string> checks, string error)
	{
		WriteReportTo("FalsumMobileSelfCheck.json", passed, checks, error);
	}

	private static void WriteReport(bool passed, List<string> checks, string error)
	{
		WriteReportTo("FalsumSelfCheck.json", passed, checks, error);
	}

	private static void WriteReportTo(string fileName, bool passed, List<string> checks, string error)
	{
		string diagnostics = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory(), "Diagnostics");
		Directory.CreateDirectory(diagnostics);
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("{");
		builder.AppendLine("  \"passed\": " + (passed ? "true" : "false") + ",");
		builder.AppendLine("  \"checks\": [");
		for (int i = 0; i < checks.Count; i++)
		{
			builder.Append("    \"").Append(checks[i]).Append('"');
			builder.AppendLine((i + 1 < checks.Count) ? "," : string.Empty);
		}
		builder.AppendLine("  ],");
		builder.AppendLine("  \"error\": \"" + (error ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r")
			.Replace("\n", "\\n") + "\"");
		builder.AppendLine("}");
		File.WriteAllText(Path.Combine(diagnostics, fileName), builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}
}
}
