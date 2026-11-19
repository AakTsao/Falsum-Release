using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using InFalsus.Gameplay.Falsum.Core;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumChartMetadataDraft
    {
        private static readonly Regex SongIdPattern = new Regex(
            "^[a-z0-9][a-z0-9_-]{0,63}$",
            RegexOptions.CultureInvariant);

        public string SongId;
        public string Title;
        public string Artist;
        public string JacketDesigner;
        public string BackgroundFile;
        public byte[] CoverBytes;
        public int CoverWidth;
        public int CoverHeight;
        public byte[] AudioBytes;
        public int AudioDurationMs;
        public string PendingCoverPath;
        public string PendingAudioPath;
        public readonly string[] DifficultyRatings =
            new string[FalsumManifest.DifficultyNames.Length];
        public readonly string[] DifficultyChartDesigners =
            new string[FalsumManifest.DifficultyNames.Length];
        public readonly List<string> TemporaryFiles =
            new List<string>();
        public readonly bool HasPackage;

        public FalsumChartMetadataDraft()
        {
        }

        public FalsumChartMetadataDraft(
            FalsumPackage package,
            bool hasPackage)
        {
            HasPackage = hasPackage;
            if (package == null)
            {
                return;
            }
            var manifest = package.Manifest;
            SongId = manifest.SongId;
            Title = manifest.Title;
            Artist = manifest.Artist;
            JacketDesigner = manifest.JacketDesigner;
            BackgroundFile = manifest.EffectiveBackgroundFile;
            CoverBytes = package.CoverBytes;
            CoverWidth = 1024;
            CoverHeight = 1024;
            AudioBytes = package.AudioBytes;
            AudioDurationMs = manifest.AudioDurationMs;
            for (var i = 0;
                 i < FalsumManifest.DifficultyNames.Length;
                 i++)
            {
                var difficulty = manifest.FindDifficulty(i);
                DifficultyRatings[i] = difficulty != null
                    ? difficulty.Rating.ToString()
                    : "0";
                DifficultyChartDesigners[i] = difficulty != null
                    ? difficulty.ChartDesigner ?? string.Empty
                    : string.Empty;
            }
        }

        public bool HasPendingCover
        {
            get { return !string.IsNullOrWhiteSpace(PendingCoverPath); }
        }

        public bool HasPendingAudio
        {
            get { return !string.IsNullOrWhiteSpace(PendingAudioPath); }
        }

        public bool HasChangesFrom(FalsumPackage package)
        {
            if (package == null || package.Manifest == null)
            {
                return false;
            }
            var manifest = package.Manifest;
            if (!string.Equals(
                    SongId,
                    manifest.SongId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Title ?? string.Empty,
                    manifest.Title,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Artist ?? string.Empty,
                    manifest.Artist,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    JacketDesigner ?? string.Empty,
                    manifest.JacketDesigner,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    BackgroundFile ?? string.Empty,
                    manifest.EffectiveBackgroundFile,
                    StringComparison.Ordinal) ||
                HasPendingAudio ||
                HasPendingCover)
            {
                return true;
            }

            for (var i = 0;
                 i < FalsumManifest.DifficultyNames.Length;
                 i++)
            {
                var difficulty = manifest.FindDifficulty(i);
                if (difficulty == null || !difficulty.Available)
                {
                    continue;
                }
                if (!string.Equals(
                        (DifficultyRatings[i] ?? string.Empty).Trim(),
                        difficulty.Rating.ToString(),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        (DifficultyChartDesigners[i] ?? string.Empty)
                        .Trim(),
                        difficulty.ChartDesigner,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryValidate(out string error)
        {
            if (!HasPackage)
            {
                error = L.Get("falsum.error.metadata.packageRequired");
                return false;
            }
            if (!IsValidSongId(SongId))
            {
                error = L.Get("falsum.error.metadata.invalidSongId");
                return false;
            }
            if (string.IsNullOrWhiteSpace(Title))
            {
                error = L.Get("falsum.error.metadata.titleRequired");
                return false;
            }
            if (string.IsNullOrWhiteSpace(Artist))
            {
                error = L.Get("falsum.error.metadata.artistRequired");
                return false;
            }
            if (!FalsumManifest.IsValidBackgroundFile(
                    BackgroundFile))
            {
                error = L.Get(
                    "falsum.error.metadata.invalidBackgroundFile");
                return false;
            }
            for (var i = 0;
                 i < FalsumManifest.DifficultyNames.Length;
                 i++)
            {
                var value = (DifficultyRatings[i] ?? string.Empty)
                    .Trim();
                int rating;
                if (!int.TryParse(value, out rating) || rating < 0)
                {
                    error = L.Get(
                        "falsum.error.metadata.invalidDifficultyRating",
                        ("difficulty", DifficultyDisplayName(i)));
                    return false;
                }
            }
            if (AudioBytes == null || AudioBytes.Length == 0)
            {
                error = L.Get("falsum.error.metadata.audioRequired");
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static bool IsValidSongId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   SongIdPattern.IsMatch(value);
        }

        private static string DifficultyDisplayName(int index)
        {
            switch (index)
            {
                case 1:
                    return L.Get("falsum.difficulty.evo");
                case 2:
                    return L.Get("falsum.difficulty.ult");
                case 3:
                    return L.Get("falsum.difficulty.fbd");
                default:
                    return L.Get("falsum.difficulty.min");
            }
        }

        public void AddTemporaryFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                !TemporaryFiles.Contains(path))
            {
                TemporaryFiles.Add(path);
            }
        }

        public void DisposeTemporaryFiles()
        {
            for (var i = 0; i < TemporaryFiles.Count; i++)
            {
                try
                {
                    if (File.Exists(TemporaryFiles[i]))
                    {
                        File.Delete(TemporaryFiles[i]);
                    }
                }
                catch (Exception)
                {
                }
            }
            TemporaryFiles.Clear();
            PendingCoverPath = string.Empty;
            PendingAudioPath = string.Empty;
        }
    }
}
