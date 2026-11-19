using System.IO;
using UnityEngine;
using InFalsus.Gameplay.Runtime.Bootstrap;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumSmokeTest : MonoBehaviour
    {
        private bool _startupPaused;

        private void Start()
        {
            var editor = FindFirstObjectByType<FalsumChartEditor>();
            _startupPaused =
                editor != null &&
                !editor.DebugSongAudioPlaying &&
                !editor.DebugPlaybackPlaying;
            Invoke(nameof(Finish), 12.0f);
        }

        private void Finish()
        {
            var editor = FindFirstObjectByType<FalsumChartEditor>();
            var historyPerformanceCheck = editor != null
                ? editor.DebugRunHistoryPerformanceCheck()
                : "fail missing-editor";
            var rewindCheck = editor != null
                ? editor.DebugRunRewindStateCheck()
                : "missing-editor";
            var skySelectionCheck = editor != null
                ? editor.DebugRunSkyAreaSelectionCheck()
                : "missing-editor";
            var pointPlaceCheck = editor != null
                ? editor.DebugRunPointPlaceCheck()
                : "missing-editor";
            var layoutCheck = editor != null
                ? editor.DebugRunLayoutChecks()
                : "fail missing-editor";
            var selectionFilterCheck = editor != null
                ? editor.DebugRunSelectionFilterChecks()
                : "fail missing-editor";
            var playbackContinuityCheck = editor != null
                ? editor.DebugRunPlaybackContinuityCheck()
                : "fail missing-editor";
            var imeFocusCheck = editor != null
                ? editor.DebugRunImeFocusChecks()
                : "fail missing-editor";
            var directDragDefault =
                editor != null && editor.DirectDragUnselectedNotes;
            var emptyStartupCheck =
                editor != null && !editor.DebugPackageLoaded;
            var bootstrap = FindFirstObjectByType<GameplayBootstrap>();
            var backgrounds =
                bootstrap != null ? bootstrap.Backgrounds : null;
            var backgroundFileCheck =
                backgrounds != null &&
                !string.IsNullOrWhiteSpace(
                    backgrounds.CurrentBackgroundFile) &&
                !string.IsNullOrEmpty(
                    FalsumRuntimePaths.ResolveBackgroundFile(
                        backgrounds.CurrentBackgroundFile));
            var backgroundCheck =
                backgroundFileCheck &&
                backgrounds.BackgroundTextureReady &&
                backgrounds.BackgroundVisible &&
                backgrounds.PlayfieldVisible;
            var externalPathsCheck =
                Directory.Exists(
                    FalsumRuntimePaths.ExternalToolsDirectory) &&
                Directory.Exists(
                    FalsumRuntimePaths.ActiveBackgroundsDirectory);
            var trackOpacity =
                bootstrap != null ? bootstrap.TrackOpacity : null;
            var opacityApiWorks = false;
            if (trackOpacity != null && bootstrap != null)
            {
                bootstrap.SetTrackOpacity(0.5f);
                opacityApiWorks =
                    Mathf.Abs(trackOpacity.TrackOpacity - 0.5f) <
                    0.001f;
                bootstrap.SetTrackOpacity(1.0f);
            }
            var trackOpacityCheck =
                trackOpacity != null &&
                trackOpacity.SurfaceCount >= 6 &&
                Mathf.Abs(trackOpacity.TrackOpacity - 1.0f) < 0.001f &&
                opacityApiWorks;
            var stillPaused =
                editor != null &&
                !editor.DebugSongAudioPlaying &&
                !editor.DebugPlaybackPlaying;
            var startupAudioCheck =
                _startupPaused && stillPaused ? "pass" : "fail";
            Debug.Log(
                "[Falsum] Standalone smoke test complete: " +
                (editor != null ? editor.DebugSummary : "<no-editor>") +
                " historyPerformanceCheck=" + historyPerformanceCheck +
                " rewindCheck=" + rewindCheck +
                " skySelectionCheck=" + skySelectionCheck +
                " pointPlaceCheck=" + pointPlaceCheck +
                " selectionFilterCheck=" + selectionFilterCheck +
                " playbackContinuityCheck=" +
                playbackContinuityCheck +
                " imeFocusCheck=" + imeFocusCheck +
                " layoutCheck=" + layoutCheck +
                " startupAudioCheck=" + startupAudioCheck +
                " directDragDefault=" + directDragDefault +
                " emptyStartupCheck=" +
                (emptyStartupCheck ? "pass" : "fail") +
                " backgroundCheck=" +
                (backgroundCheck ? "pass" : "fail") +
                " externalPathsCheck=" +
                (externalPathsCheck ? "pass" : "fail") +
                " trackOpacityCheck=" +
                (trackOpacityCheck ? "pass" : "fail"));
            Application.Quit(
                (historyPerformanceCheck.StartsWith("pass ") ||
                 historyPerformanceCheck == "skip") &&
                (rewindCheck == "pass" || rewindCheck == "skip") &&
                (skySelectionCheck == "pass" ||
                 skySelectionCheck == "skip") &&
                (pointPlaceCheck == "pass" ||
                 pointPlaceCheck == "skip") &&
                selectionFilterCheck.StartsWith("pass") &&
                playbackContinuityCheck.StartsWith("pass") &&
                imeFocusCheck.StartsWith("pass") &&
                layoutCheck.StartsWith("pass") &&
                startupAudioCheck == "pass" &&
                directDragDefault &&
                emptyStartupCheck &&
                backgroundCheck &&
                externalPathsCheck &&
                trackOpacityCheck
                    ? 0
                    : 1);
        }
    }
}
