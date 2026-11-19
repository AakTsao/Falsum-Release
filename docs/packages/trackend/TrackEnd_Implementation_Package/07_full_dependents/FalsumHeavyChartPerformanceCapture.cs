using System;
using System.IO;
using System.Reflection;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Presentation.Backgrounds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace InFalsus.EditorTools
{
    public static class FalsumHeavyChartPerformanceCapture
    {
        private const string RuntimeScenePath =
            "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity";
        private const string ActiveKey =
            "InFalsus.HeavyChartPerformanceCapture.Active";
        private const string StageKey =
            "InFalsus.HeavyChartPerformanceCapture.Stage";
        private const string StageStartKey =
            "InFalsus.HeavyChartPerformanceCapture.StageStart";
        private const string PackageLoadedAtKey =
            "InFalsus.HeavyChartPerformanceCapture.PackageLoadedAt";
        private const string PendingExitCodeKey =
            "InFalsus.HeavyChartPerformanceCapture.PendingExitCode";
        private const string PackageEnvironmentVariable =
            "INFALSUS_PERFORMANCE_PACKAGE";
        private const string PackageArgument =
            "-falsum-performance-package";
        private const string ForceTrackEndRebuildEnvironmentVariable =
            "INFALSUS_FORCE_TRACKEND_REBUILD";
        private const string SettingsBackupDirectoryName =
            "FalsumHeavyChartPerformanceCapture";
        private const float StartupDelaySeconds = 2.5f;
        private const float PackageSettleSeconds = 4.0f;
        private const float MidpointSettleSeconds = 1.0f;
        private const float PlaybackWarmupSeconds = 2.0f;
        private const int RequiredHistoryFrames = 120;

        private static int _shutdownTicks;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem("InFalsus/Falsum/Capture Heavy Chart Performance")]
        public static void Run()
        {
            var packagePath = ResolvePackagePath();
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException(
                    "Heavy chart package was not found.",
                    packagePath);
            }
            BackupProjectSettings();

            var scene = EditorSceneManager.OpenScene(
                RuntimeScenePath,
                OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException(
                    "Failed to open " + RuntimeScenePath);
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetFloat(
                StageStartKey,
                (float)EditorApplication.timeSinceStartup);
            SessionState.SetFloat(PackageLoadedAtKey, 0.0f);
            Install();
            EditorApplication.isPlaying = true;
            Debug.Log(
                "[Falsum] Heavy chart performance capture: " +
                packagePath);
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false) ||
                !EditorApplication.isPlaying)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var stage = SessionState.GetInt(StageKey, 0);
            var stageStart = SessionState.GetFloat(
                StageStartKey,
                (float)now);
            if (stage == 0 &&
                now - stageStart < StartupDelaySeconds)
            {
                return;
            }

            try
            {
                var editor = FindEditor();
                if (editor == null)
                {
                    throw new InvalidOperationException(
                        "Falsum chart editor was not created.");
                }
                ForceLegacyTrackEndRebuildIfRequested();

                if (stage == 0)
                {
                    Screen.SetResolution(
                        1920,
                        1080,
                        FullScreenMode.Windowed);
                    LoadPackage(editor);
                    SessionState.SetFloat(
                        PackageLoadedAtKey,
                        (float)now);
                    AdvanceStage(now);
                    return;
                }

                var packageLoadedAt = SessionState.GetFloat(
                    PackageLoadedAtKey,
                    0.0f);
                if (stage == 1)
                {
                    if (now - packageLoadedAt <
                        PackageSettleSeconds)
                    {
                        return;
                    }
                    editor.DebugSeekNormalized(0.5f);
                    AdvanceStage(now);
                    return;
                }

                if (stage == 2)
                {
                    if (now - stageStart < MidpointSettleSeconds)
                    {
                        return;
                    }
                    CaptureScreenshot(
                        "falsum_tokonagi_mid_reference.png");
                    editor.DebugTogglePlayback();
                    AdvanceStage(now);
                    return;
                }

                if (stage == 3)
                {
                    if (now - stageStart <
                        PlaybackWarmupSeconds)
                    {
                        return;
                    }
                    editor.DebugTogglePerformanceWindow();
                    if (!editor.DebugPerformanceWindowOpen)
                    {
                        throw new InvalidOperationException(
                            "Performance window did not open.");
                    }
                    AdvanceStage(now);
                    return;
                }

                if (stage == 4)
                {
                    if (editor.DebugPerformanceHistoryCount <
                        RequiredHistoryFrames)
                    {
                        return;
                    }
                    CaptureScreenshot(
                        "falsum_tokonagi_mid_performance.png");
                    LogMetrics(editor);
                    editor.DebugTogglePerformanceWindow();
                    if (editor.DebugPerformanceWindowOpen)
                    {
                        throw new InvalidOperationException(
                            "Performance window did not close.");
                    }
                    Finish(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static FalsumChartEditor FindEditor()
        {
            return UnityEngine.Object.FindFirstObjectByType<
                FalsumChartEditor>(
                FindObjectsInactive.Include);
        }

        private static void LoadPackage(FalsumChartEditor editor)
        {
            var method = typeof(FalsumChartEditor).GetMethod(
                "LoadPackage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(
                    typeof(FalsumChartEditor).FullName,
                    "LoadPackage");
            }
            method.Invoke(
                editor,
                new object[]
                {
                    ResolvePackagePath(),
                    false,
                    null,
                    null,
                });
        }

        private static void ForceLegacyTrackEndRebuildIfRequested()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        ForceTrackEndRebuildEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }
            var composer = FalsumTrackEndComposer.Active;
            if (composer == null)
            {
                return;
            }
            var field = typeof(FalsumTrackEndComposer).GetField(
                "_maskDirty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(composer, true);
        }

        private static void LogMetrics(FalsumChartEditor editor)
        {
            var field = typeof(FalsumChartEditor).GetField(
                "_performanceMonitor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var monitor = field?.GetValue(editor);
            if (monitor == null)
            {
                throw new InvalidOperationException(
                    "Performance monitor was unavailable.");
            }
            var type = monitor.GetType();
            var p95 = InvokeFloat(type, monitor, "GetPercentile95Ms");
            var recentPeak = InvokeFloat(
                type,
                monitor,
                "GetRecentPeakMs");
            var stutterFrames = InvokeInt(
                type,
                monitor,
                "GetStutterFrameCount");
            var worstFrame = ReadFloatProperty(
                type,
                monitor,
                "WorstFrameMs");
            var scriptFrame = ReadFloatProperty(
                type,
                monitor,
                "ScriptFrameMs");
            var managedAllocation = ReadFloatProperty(
                type,
                monitor,
                "ManagedAllocationKb");
            var gcCollections = ReadIntProperty(
                type,
                monitor,
                "GcCollections");
            var maskRebuilds =
                FalsumTrackEndComposerFeature.MaskRebuildCount;
            Debug.Log(
                "[Falsum] Heavy chart performance: " +
                editor.DebugPerformanceSummary +
                " p95=" + p95.ToString("0.0") +
                " peak=" + recentPeak.ToString("0.0") +
                " worst=" + worstFrame.ToString("0.0") +
                " stutter=" + stutterFrames +
                "/" + RequiredHistoryFrames +
                " script=" + scriptFrame.ToString("0.0") +
                " alloc=" + managedAllocation.ToString("0.0") +
                "KB gc=" + gcCollections +
                " trackEndMaskRebuilds=" + maskRebuilds);
        }

        private static float InvokeFloat(
            Type type,
            object target,
            string methodName)
        {
            return Convert.ToSingle(
                type.GetMethod(methodName)?.Invoke(
                    target,
                    null) ?? 0.0f);
        }

        private static int InvokeInt(
            Type type,
            object target,
            string methodName)
        {
            return Convert.ToInt32(
                type.GetMethod(methodName)?.Invoke(
                    target,
                    null) ?? 0);
        }

        private static float ReadFloatProperty(
            Type type,
            object target,
            string propertyName)
        {
            return Convert.ToSingle(
                type.GetProperty(propertyName)?.GetValue(
                    target) ?? 0.0f);
        }

        private static int ReadIntProperty(
            Type type,
            object target,
            string propertyName)
        {
            return Convert.ToInt32(
                type.GetProperty(propertyName)?.GetValue(
                    target) ?? 0);
        }

        private static string ResolvePackagePath()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(
                        arguments[i],
                        PackageArgument,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(arguments[i + 1]);
                }
            }
            var configured =
                Environment.GetEnvironmentVariable(
                    PackageEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "export",
                "falsum",
                "tokonagi.falsum"));
        }

        private static void CaptureScreenshot(string fileName)
        {
            var directory = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ??
                Directory.GetCurrentDirectory(),
                "Diagnostics",
                "Screenshots");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(
                    path,
                    texture.EncodeToPNG());
                Debug.Log("[Falsum] Screenshot: " + path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void AdvanceStage(double now)
        {
            SessionState.SetInt(
                StageKey,
                SessionState.GetInt(StageKey, 0) + 1);
            SessionState.SetFloat(
                StageStartKey,
                (float)now);
        }

        private static void Finish(int exitCode)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.update -= CompleteFinish;
            EditorApplication.update += CompleteFinish;
            SessionState.SetInt(PendingExitCodeKey, exitCode);
            EditorApplication.isPlaying = false;
            _shutdownTicks = 0;
            CompleteFinish();
        }

        private static void CompleteFinish()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            _shutdownTicks++;
            if (_shutdownTicks < 2)
            {
                return;
            }
            EditorApplication.update -= CompleteFinish;
            var restored = RestoreProjectSettings();
            if (!restored)
            {
                Debug.LogError(
                    "[Falsum] Project settings backup could not be restored.");
            }
            EditorApplication.Exit(
                SessionState.GetInt(PendingExitCodeKey, 1));
        }

        private static void BackupProjectSettings()
        {
            var projectRoot = Path.GetDirectoryName(
                Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }
            var backupDirectory = Path.Combine(
                projectRoot,
                "Library",
                SettingsBackupDirectoryName);
            Directory.CreateDirectory(backupDirectory);
            CopyProjectSetting(
                projectRoot,
                backupDirectory,
                "ProjectSettings.asset");
            CopyProjectSetting(
                projectRoot,
                backupDirectory,
                "QualitySettings.asset");
        }

        private static void CopyProjectSetting(
            string projectRoot,
            string backupDirectory,
            string fileName)
        {
            var source = Path.Combine(
                projectRoot,
                "ProjectSettings",
                fileName);
            var destination = Path.Combine(
                backupDirectory,
                fileName);
            if (File.Exists(source))
            {
                File.Copy(source, destination, true);
            }
        }

        private static bool RestoreProjectSettings()
        {
            var projectRoot = Path.GetDirectoryName(
                Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return false;
            }
            var backupDirectory = Path.Combine(
                projectRoot,
                "Library",
                SettingsBackupDirectoryName);
            var restored = true;
            restored &= RestoreProjectSetting(
                projectRoot,
                backupDirectory,
                "ProjectSettings.asset");
            restored &= RestoreProjectSetting(
                projectRoot,
                backupDirectory,
                "QualitySettings.asset");
            if (restored)
            {
                AssetDatabase.Refresh();
            }
            return restored;
        }

        private static bool RestoreProjectSetting(
            string projectRoot,
            string backupDirectory,
            string fileName)
        {
            var source = Path.Combine(
                backupDirectory,
                fileName);
            var destination = Path.Combine(
                projectRoot,
                "ProjectSettings",
                fileName);
            if (!File.Exists(source))
            {
                return false;
            }
            File.Copy(source, destination, true);
            return true;
        }

    }
}
