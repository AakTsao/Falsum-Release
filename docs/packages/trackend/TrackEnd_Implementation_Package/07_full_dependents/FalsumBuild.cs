using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace InFalsus.EditorTools
{
    public static class FalsumBuild
    {
        private const int DefaultWindowWidth = 1366;
        private const int DefaultWindowHeight = 768;
        private const string CompanyName = "Team AakTsao";
        private const string ProductName = "Falsum Editor Preview";
        private const string BuildVersion = "0.46.0";
        private const string BuildsDirectoryName = "Builds";
        private const string BuildDirectoryName =
            "FalsumEditor-" + FalsumEditorInfo.Version;
        private const string RuntimeScenePath =
            "Assets/InFalsus/Gameplay/Recovered/Scenes/GameplayRuntime.unity";
        private const string BackgroundSourceDirectory =
            "Assets/StreamingAssets/Falsum/GameplayBackgrounds";
        /// <summary>
        /// Published folder next to the executable. This is the original
        /// location of the library; the build must not rename it.
        /// </summary>
        private const string PublishedBackgroundDirectoryName =
            "GameplayBackgrounds";
        private const string ToolSourceDirectory =
            "Assets/StreamingAssets/Falsum/UserTools";
        private const string ProtectedSongFontAssetPath =
            "Assets/StreamingAssets/Falsum/Fonts/SongInfoCard.fpk";

        [MenuItem("InFalsus/Falsum/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(RuntimeScenePath))
            {
                throw new FileNotFoundException(
                    "Runtime scene is missing.",
                    RuntimeScenePath);
            }
            if (!File.Exists(ProtectedSongFontAssetPath))
            {
                throw new FileNotFoundException(
                    "Protected song card font is missing. Run " +
                    "InFalsus/Falsum/Rebuild Protected Song Font first.",
                    ProtectedSongFontAssetPath);
            }
            FalsumUiAssetBuilder.EnsureAssets();
            FalsumTrackEndComposerInstaller.EnsureInstalled(true);
            GameplaySceneBuilder.SanitizeNoteTemplatePrefabs();
            FalsumAppIcon.Apply();

            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.bundleVersion = BuildVersion;
            PlayerSettings.defaultScreenWidth = DefaultWindowWidth;
            PlayerSettings.defaultScreenHeight = DefaultWindowHeight;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });

            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);
            var projectRoot =
                Path.GetDirectoryName(Application.dataPath) ??
                Directory.GetCurrentDirectory();
            var outputDirectory = Path.Combine(
                projectRoot,
                BuildsDirectoryName,
                BuildDirectoryName);
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(
                outputDirectory,
                "FalsumEditor.exe");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { RuntimeScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Falsum player build failed: " +
                    report.summary.result +
                    " errors=" + report.summary.totalErrors);
            }
            CopyRuntimeContent(projectRoot, outputDirectory);
            RemoveBundledRuntimeCopies(outputDirectory);
            VerifyNativeAudioPlugins(outputDirectory);
            VerifyPublishedBackgrounds(projectRoot, outputDirectory);
            var packagePath = outputDirectory + ".zip";
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
            ZipFile.CreateFromDirectory(
                outputDirectory,
                packagePath,
                System.IO.Compression.CompressionLevel.Optimal,
                false);
            Debug.Log(
                "[Falsum] Windows player built: " +
                outputPath +
                " size=" + report.summary.totalSize +
                " package=" + packagePath);
        }

        private static void CopyRuntimeContent(
            string projectRoot,
            string outputDirectory)
        {
            CopyDirectory(
                Path.Combine(projectRoot, BackgroundSourceDirectory),
                Path.Combine(
                    outputDirectory,
                    PublishedBackgroundDirectoryName),
                true);
            CopyDirectory(
                Path.Combine(projectRoot, ToolSourceDirectory),
                Path.Combine(outputDirectory, "ExternalTools"),
                true);
            // Builds from the short-lived relocation shipped the library
            // as Backgrounds/; drop that copy so a rebuilt folder never
            // carries two libraries (the resolver would prefer the old
            // GameplayBackgrounds/ and the new files would appear stale).
            RemoveDirectoryIfExists(
                Path.Combine(
                    outputDirectory,
                    "Backgrounds"));
        }

        private static void RemoveBundledRuntimeCopies(
            string outputDirectory)
        {
            var falsumDataDirectory = Path.Combine(
                outputDirectory,
                "FalsumEditor_Data",
                "StreamingAssets",
                "Falsum");
            RemoveDirectoryIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "GameplayBackgrounds"));
            RemoveDirectoryIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "UserTools"));
            // FALSUM ships the EDITOR, not a playable sample chart. The startup
            // chart under StreamingAssets/Charts and the two demo .falsum
            // packages exist only so the project itself has something to open
            // in the editor and to act as self-check fixtures; a build must not
            // carry ~23 MB of them. The editor now boots into an empty chart
            // when no start-up chart is present (GameplayBootstrap.LoadChart),
            // so stripping them is safe.
            RemoveDirectoryIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "Charts"));
            RemoveFileIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "alamode.falsum"));
            RemoveFileIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "cryogenic.falsum"));
            RemoveFileIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "alamode.falsum.meta"));
            RemoveFileIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "cryogenic.falsum.meta"));
            // The media converter is in-process BASS now, so a player build
            // must not carry the old 61 MB ffmpeg.exe or its GPL text. The
            // sources are gone from the project as well; this drop is a
            // belt-and-braces guard for a player built from a stale checkout.
            RemoveFileIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "Tools",
                    "ffmpeg.exe"));
            RemoveFileIfExists(
                Path.Combine(
                    falsumDataDirectory,
                    "Tools",
                    "FFMPEG_GPLv3.txt"));
        }

        /// <summary>
        /// A player build links the native BASS add-ons the audio import path
        /// needs. A build that silently missed them would report
        /// "audio engine missing" on every import, so the failure has to
        /// surface here, at build time, instead.
        /// </summary>
        private static void VerifyNativeAudioPlugins(
            string outputDirectory)
        {
            var pluginsDirectory = Path.Combine(
                outputDirectory,
                "FalsumEditor_Data",
                "Plugins",
                "x86_64");
            if (!Directory.Exists(pluginsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Player build has no native plugin directory: " +
                    pluginsDirectory);
            }
            string[] required =
            {
                "bass.dll",
                "bassmix.dll",
                "bassenc.dll",
                "bassenc_ogg.dll",
                "bassflac.dll",
            };
            for (var i = 0; i < required.Length; i++)
            {
                var candidate = Path.Combine(
                    pluginsDirectory,
                    required[i]);
                if (!File.Exists(candidate))
                {
                    throw new FileNotFoundException(
                        "Player build is missing the native audio plugin " +
                        required[i] + ", so audio import cannot run.",
                        candidate);
                }
            }
        }

        /// <summary>
        /// The published <c>GameplayBackgrounds</c> folder is the only
        /// place a player build looks for gameplay backgrounds, so verify
        /// that every logical <c>.png</c> name used by song data resolves
        /// there (the shipped files are JPEG after the 2026-11-01
        /// slimming, and chart data keeps the PNG spelling).
        /// A missing library would otherwise surface as black backgrounds
        /// only after shipping.
        /// </summary>
        private static void VerifyPublishedBackgrounds(
            string projectRoot,
            string outputDirectory)
        {
            var publishedRoot = Path.Combine(
                outputDirectory,
                PublishedBackgroundDirectoryName);
            if (!Directory.Exists(publishedRoot))
            {
                throw new DirectoryNotFoundException(
                    "Published gameplay background directory is missing: " +
                    publishedRoot);
            }
            var sourceRoot = Path.Combine(
                projectRoot,
                BackgroundSourceDirectory);
            var names = CollectBackgroundNames(sourceRoot);
            var missing = new StringBuilder();
            var missingCount = 0;
            for (var i = 0; i < names.Count; i++)
            {
                if (!string.IsNullOrEmpty(
                        FalsumRuntimePaths.ResolveBackgroundFile(
                            publishedRoot,
                            names[i])))
                {
                    continue;
                }
                missingCount++;
                if (missingCount <= 8)
                {
                    missing.AppendLine("  " + names[i]);
                }
            }
            if (missingCount > 0)
            {
                throw new InvalidOperationException(
                    "Published gameplay backgrounds cannot resolve " +
                    missingCount +
                    " logical file name(s):\n" + missing);
            }
            var mappingPath = Path.Combine(
                publishedRoot,
                "song_background_map.json");
            if (!File.Exists(mappingPath))
            {
                mappingPath = Path.Combine(
                    sourceRoot,
                    "song_background_map.json");
            }
            if (File.Exists(mappingPath))
            {
                var mappingFiles = CollectMappingBackgroundFiles(
                    mappingPath);
                for (var i = 0; i < mappingFiles.Count; i++)
                {
                    if (string.IsNullOrEmpty(
                            FalsumRuntimePaths.ResolveBackgroundFile(
                                publishedRoot,
                                mappingFiles[i])))
                    {
                        throw new InvalidOperationException(
                            "Published gameplay backgrounds are missing " +
                            "a mapping entry: " + mappingFiles[i]);
                    }
                }
            }
            Debug.Log(
                "[Falsum] Published backgrounds verified: " +
                names.Count + " logical names in " + publishedRoot);
        }

        /// <summary>
        /// Logical background names referenced by the source library:
        /// mapping entries plus the default <c>.png</c> alias.
        /// </summary>
        private static List<string>
            CollectBackgroundNames(string sourceRoot)
        {
            var names =
                new List<string>();
            var seen =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(
                    FalsumManifest.DefaultBackgroundFile) &&
                seen.Add(FalsumManifest.DefaultBackgroundFile))
            {
                names.Add(FalsumManifest.DefaultBackgroundFile);
            }
            var mappingPath = Path.Combine(
                sourceRoot,
                "song_background_map.json");
            if (!File.Exists(mappingPath))
            {
                return names;
            }
            var mappingFiles = CollectMappingBackgroundFiles(mappingPath);
            for (var i = 0; i < mappingFiles.Count; i++)
            {
                if (seen.Add(mappingFiles[i]))
                {
                    names.Add(mappingFiles[i]);
                }
            }
            // Every physical file must also answer to its logical .png
            // name so freshly authored backgrounds stay loadable by old
            // packages that spell them with the PNG extension.
            if (Directory.Exists(sourceRoot))
            {
                var files = Directory.GetFiles(sourceRoot);
                for (var i = 0; i < files.Length; i++)
                {
                    var name = Path.GetFileName(files[i]);
                    if (!FalsumManifest.IsValidBackgroundFile(name))
                    {
                        continue;
                    }
                    var logical =
                        Path.GetFileNameWithoutExtension(name) + ".png";
                    if (seen.Add(logical))
                    {
                        names.Add(logical);
                    }
                }
            }
            return names;
        }

        private static List<string>
            CollectMappingBackgroundFiles(string mappingPath)
        {
            var files =
                new List<string>();
            var json = FalsumJson.Parse(
                File.ReadAllText(mappingPath));
            var songs = FalsumJson.GetArray(
                FalsumJson.AsObject(json),
                "songs");
            if (songs == null)
            {
                return files;
            }
            for (var i = 0; i < songs.Count; i++)
            {
                var entry = songs[i] as FalsumJsonObject;
                if (entry == null)
                {
                    continue;
                }
                var file = FalsumJson.GetString(
                    entry,
                    "background_file",
                    string.Empty);
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }
                file = file.Trim();
                if (!FalsumManifest.IsValidBackgroundFile(file))
                {
                    throw new InvalidOperationException(
                        "Gameplay background mapping has an invalid " +
                        "file name: " + file);
                }
                files.Add(file);
            }
            return files;
        }

        private static void RemoveDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static void RemoveFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void CopyDirectory(
            string source,
            string destination,
            bool overwrite)
        {
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    "Falsum runtime content directory is missing: " +
                    source);
            }
            Directory.CreateDirectory(destination);
            var files = Directory.GetFiles(source);
            for (var i = 0; i < files.Length; i++)
            {
                if (string.Equals(
                        Path.GetExtension(files[i]),
                        ".meta",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var target = Path.Combine(
                    destination,
                    Path.GetFileName(files[i]));
                if (overwrite || !File.Exists(target))
                {
                    File.Copy(files[i], target, overwrite);
                }
            }
            var directories = Directory.GetDirectories(source);
            for (var i = 0; i < directories.Length; i++)
            {
                CopyDirectory(
                    directories[i],
                    Path.Combine(
                        destination,
                        Path.GetFileName(directories[i])),
                    overwrite);
            }
        }
    }
}
