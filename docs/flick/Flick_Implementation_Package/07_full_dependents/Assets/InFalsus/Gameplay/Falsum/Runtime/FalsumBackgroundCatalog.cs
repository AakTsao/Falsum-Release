using System;
using System.Collections.Generic;
using System.IO;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumBackgroundEntry
    {
        public string FileName;
        public string DisplayName;
        public string FullPath;
        public Texture2D Thumbnail;
    }

    public static class FalsumBackgroundCatalog
    {
        private const string ThumbnailDirectoryName = "Thumbnails";

        public static string RootDirectory
        {
            get
            {
                return FalsumRuntimePaths.ActiveBackgroundsDirectory;
            }
        }

        public static string ThumbnailDirectory
        {
            get
            {
                var directories =
                    FalsumRuntimePaths.BackgroundThumbnailDirectories;
                return directories.Count > 0
                    ? directories[0]
                    : Path.Combine(
                        RootDirectory,
                        ThumbnailDirectoryName);
            }
        }

        public static bool TryLoad(
            out List<FalsumBackgroundEntry> entries,
            out string error)
        {
            entries = new List<FalsumBackgroundEntry>();
            error = string.Empty;
            try
            {
                var roots = FalsumRuntimePaths.BackgroundDirectories;
                if (roots.Count == 0)
                {
                    error = L.Get(
                        "falsum.error.background.directoryMissing");
                    return false;
                }
                // Backgrounds ship as JPEG while chart data keeps the
                // historical .png name, so the catalog reports one
                // logical .png entry per stem and the loader resolves
                // the physical file. Every known root is enumerated so
                // that user additions in the editable folder and the
                // bundled fallback both appear.
                var seen = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                for (var rootIndex = 0;
                     rootIndex < roots.Count;
                     rootIndex++)
                {
                    var files = Directory.GetFiles(
                        roots[rootIndex],
                        "*.*",
                        SearchOption.TopDirectoryOnly);
                    for (var i = 0; i < files.Length; i++)
                    {
                        var file = files[i];
                        var name = Path.GetFileName(file);
                        if (!FalsumManifest.IsValidBackgroundFile(name))
                        {
                            continue;
                        }
                        var stem =
                            Path.GetFileNameWithoutExtension(name);
                        if (string.IsNullOrEmpty(stem) ||
                            !seen.Add(stem))
                        {
                            continue;
                        }
                        entries.Add(new FalsumBackgroundEntry
                        {
                            FileName = stem + ".png",
                            DisplayName = stem,
                            FullPath = file,
                        });
                    }
                }
                entries.Sort(CompareEntries);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static Texture2D LoadThumbnail(
            FalsumBackgroundEntry entry)
        {
            if (entry == null)
            {
                return null;
            }
            if (entry.Thumbnail != null)
            {
                return entry.Thumbnail;
            }
            if (string.IsNullOrWhiteSpace(entry.FileName))
            {
                return null;
            }
            var thumbnailPath = ResolveThumbnailPath(entry);
            if (string.IsNullOrWhiteSpace(thumbnailPath))
            {
                return null;
            }
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false,
                    false);
                if (!ImageConversion.LoadImage(
                        texture,
                        File.ReadAllBytes(thumbnailPath),
                        true))
                {
                    FalsumPlatform.DestroyObject(texture);
                    return null;
                }                texture.name =
                    "Falsum_BackgroundThumbnail_" +
                    Path.GetFileNameWithoutExtension(entry.FileName);
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                entry.Thumbnail = texture;
                return texture;
            }
            catch (Exception exception)
            {
                if (texture != null)
                {
                    FalsumPlatform.DestroyObject(texture);
                }
                Debug.LogWarning(
                    "[Falsum] Background thumbnail load failed: " +
                    entry.FileName + " (" + exception.Message + ")");
                return null;
            }
        }

        /// <summary>
        /// Thumbnails follow the same png→jpg fallback as the full images
        /// and may live in any known background root, so a build that
        /// predates the <c>Backgrounds</c> relocation still shows
        /// previews.
        /// </summary>
        private static string ResolveThumbnailPath(
            FalsumBackgroundEntry entry)
        {
            var stem = Path.GetFileNameWithoutExtension(entry.FileName);
            if (string.IsNullOrEmpty(stem))
            {
                return string.Empty;
            }
            var directories =
                FalsumRuntimePaths.BackgroundThumbnailDirectories;
            for (var i = 0; i < directories.Count; i++)
            {
                var resolved = FalsumRuntimePaths.ResolveBackgroundFile(
                    directories[i],
                    stem + ".png");
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }
            }
            return string.Empty;
        }

        public static void ReleaseThumbnails(
            IList<FalsumBackgroundEntry> entries)
        {
            if (entries == null)
            {
                return;
            }
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Thumbnail == null)
                {
                    continue;
                }
                FalsumPlatform.DestroyObject(entry.Thumbnail);
                entry.Thumbnail = null;
            }
        }

        private static int CompareEntries(
            FalsumBackgroundEntry left,
            FalsumBackgroundEntry right)
        {
            var leftIndex = LeadingNumber(left.FileName);
            var rightIndex = LeadingNumber(right.FileName);
            var numberCompare = leftIndex.CompareTo(rightIndex);
            if (numberCompare != 0)
            {
                return numberCompare;
            }
            return string.Compare(
                left.FileName,
                right.FileName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int LeadingNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return int.MaxValue;
            }
            var length = 0;
            while (length < value.Length &&
                   char.IsDigit(value[length]))
            {
                length++;
            }
            int result;
            return length > 0 &&
                   int.TryParse(
                       value.Substring(0, length),
                       out result)
                ? result
                : int.MaxValue;
        }
    }
}
