using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumRuntimePaths
    {
        private const string BackgroundsDirectoryName = "Backgrounds";
        private const string ExternalToolsDirectoryName = "ExternalTools";
        private const string FalsumDirectoryName = "Falsum";
        /// <summary>
        /// The original, canonical external folder name for gameplay
        /// backgrounds, kept as the published folder next to the
        /// executable. Newer builds briefly renamed it to
        /// <c>Backgrounds</c>, so that name remains a fallback.
        /// </summary>
        private const string GameplayBackgroundsDirectoryName =
            "GameplayBackgrounds";
        private const string ThumbnailsDirectoryName = "Thumbnails";

        /// <summary>
        /// Accepted background extensions, in resolution order. Chart data
        /// keeps the historical <c>.png</c> name while the shipped
        /// library is JPEG, so every name is resolved against all of
        /// them.
        /// </summary>
        private static readonly string[] BackgroundExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
        };

        public static string MainProgramDirectory
        {
            get
            {
                return Path.GetFullPath(
                    Path.GetDirectoryName(Application.dataPath) ??
                    Directory.GetCurrentDirectory());
            }
        }

        public static string ExternalToolsDirectory
        {
            get
            {
                return Path.Combine(
                    MainProgramDirectory,
                    ExternalToolsDirectoryName);
            }
        }

        /// <summary>
        /// Preferred background root: the editable
        /// <c>GameplayBackgrounds</c> folder next to the player
        /// executable (its historical location), or the bundled library
        /// inside StreamingAssets in the editor.
        /// </summary>
        public static string BackgroundsDirectory
        {
            get
            {
#if UNITY_EDITOR
                return BundledBackgroundsDirectory;
#else
                return Path.Combine(
                    MainProgramDirectory,
                    GameplayBackgroundsDirectoryName);
#endif
            }
        }

        /// <summary>
        /// First background root that actually exists, falling back to the
        /// preferred one so callers can still report a meaningful
        /// "directory missing" diagnostic.
        /// </summary>
        public static string ActiveBackgroundsDirectory
        {
            get
            {
                var candidates = BackgroundDirectories;
                return candidates.Count > 0
                    ? candidates[0]
                    : BackgroundsDirectory;
            }
        }

        /// <summary>
        /// Ordered background roots that are searched when resolving a
        /// logical background name. The canonical location is
        /// <c>GameplayBackgrounds</c> next to the executable. Builds
        /// released during the short-lived relocation looked in
        /// <c>Backgrounds</c>, and players can also carry the library
        /// under StreamingAssets, so every known location stays a
        /// fallback: old charts must never fall back to a black screen
        /// just because the folder was renamed.
        /// </summary>
        public static IList<string> BackgroundDirectories
        {
            get
            {
                var directories = new List<string>();
                AddExistingDirectory(
                    directories,
                    BackgroundsDirectory);
                var programDirectory = MainProgramDirectory;
                AddExistingDirectory(
                    directories,
                    Path.Combine(
                        programDirectory,
                        BackgroundsDirectoryName));
                AddExistingDirectory(
                    directories,
                    BundledBackgroundsDirectory);
                AddExistingDirectory(
                    directories,
                    Path.Combine(
                        Application.streamingAssetsPath,
                        FalsumDirectoryName,
                        BackgroundsDirectoryName));
                return directories;
            }
        }

        public static IList<string> BackgroundThumbnailDirectories
        {
            get
            {
                var directories = new List<string>();
                var roots = BackgroundDirectories;
                for (var i = 0; i < roots.Count; i++)
                {
                    AddExistingDirectory(
                        directories,
                        Path.Combine(
                            roots[i],
                            ThumbnailsDirectoryName));
                }
                return directories;
            }
        }

        public static string BundledToolsDirectory
        {
            get
            {
                return Path.Combine(
                    Application.streamingAssetsPath,
                    FalsumDirectoryName,
                    "UserTools");
            }
        }

        public static string BundledBackgroundsDirectory
        {
            get
            {
                return Path.Combine(
                    Application.streamingAssetsPath,
                    FalsumDirectoryName,
                    GameplayBackgroundsDirectoryName);
            }
        }

        public static string LegacyToolsDirectory
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    FalsumDirectoryName,
                    "Tools");
            }
        }

        /// <summary>
        /// Resolves a logical background file name (as stored in
        /// <c>song.json</c> / the manifest, for example
        /// <c>31_Space-Positive.png</c>) to a file that exists on disk.
        /// Backgrounds are shipped as JPEG to save space while the chart
        /// data keeps its historical <c>.png</c> name, so a missing exact
        /// match falls back to the same stem with any accepted extension.
        /// </summary>
        public static string ResolveBackgroundFile(string fileName)
        {
            var roots = BackgroundDirectories;
            for (var i = 0; i < roots.Count; i++)
            {
                var resolved = ResolveBackgroundFile(
                    roots[i],
                    fileName);
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }
            }
            return string.Empty;
        }

        public static string ResolveBackgroundFile(
            string directory,
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }
            var name = fileName.Trim();
            if (name.IndexOf('/') >= 0 ||
                name.IndexOf('\\') >= 0 ||
                name.IndexOf(':') >= 0 ||
                name == "." ||
                name == "..")
            {
                return string.Empty;
            }
            if (!Directory.Exists(directory))
            {
                return string.Empty;
            }
            var exact = Path.Combine(directory, name);
            if (File.Exists(exact))
            {
                return exact;
            }
            var stem = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrEmpty(stem))
            {
                return string.Empty;
            }
            var requestedExtension = Path.GetExtension(name);
            var extensions = ExtensionOrder(requestedExtension);
            for (var i = 0; i < extensions.Length; i++)
            {
                var candidate = Path.Combine(
                    directory,
                    stem + extensions[i]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return ResolveByFileName(
                directory,
                stem);
        }

        /// <summary>
        /// Last-resort lookup that ignores extension and casing. Packages
        /// authored on case-sensitive platforms or renamed by hand still
        /// find their image instead of falling back to a black screen.
        /// </summary>
        private static string ResolveByFileName(
            string directory,
            string stem)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Background directory scan failed: " +
                    directory + " (" + exception.Message + ")");
                return string.Empty;
            }
            var best = string.Empty;
            var bestRank = int.MaxValue;
            for (var i = 0; i < files.Length; i++)
            {
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(files[i]),
                        stem,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var rank = ExtensionRank(
                    Path.GetExtension(files[i]));
                if (rank >= bestRank)
                {
                    continue;
                }
                best = files[i];
                bestRank = rank;
            }
            return best;
        }

        private static string[] ExtensionOrder(
            string requestedExtension)
        {
            if (string.IsNullOrEmpty(requestedExtension) ||
                ExtensionRank(requestedExtension) == int.MaxValue)
            {
                return BackgroundExtensions;
            }
            var requested = requestedExtension.ToLowerInvariant();
            var ordered = new string[BackgroundExtensions.Length];
            ordered[0] = requested;
            var count = 1;
            for (var i = 0; i < BackgroundExtensions.Length; i++)
            {
                if (string.Equals(
                        BackgroundExtensions[i],
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                ordered[count++] = BackgroundExtensions[i];
            }
            return ordered;
        }

        private static int ExtensionRank(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return int.MaxValue;
            }
            for (var i = 0; i < BackgroundExtensions.Length; i++)
            {
                if (string.Equals(
                        extension,
                        BackgroundExtensions[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return int.MaxValue;
        }

        private static void AddExistingDirectory(
            List<string> directories,
            string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }
            string full;
            try
            {
                full = Path.GetFullPath(directory);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Background path is invalid: " +
                    directory + " (" + exception.Message + ")");
                return;
            }
            for (var i = 0; i < directories.Count; i++)
            {
                if (string.Equals(
                        directories[i],
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            if (Directory.Exists(full))
            {
                directories.Add(full);
            }
        }
    }
}
