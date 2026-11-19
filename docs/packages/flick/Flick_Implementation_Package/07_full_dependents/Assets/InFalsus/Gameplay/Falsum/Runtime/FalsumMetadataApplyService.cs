using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using InFalsus.Gameplay.Falsum.Core;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumChartMetadataSnapshot
    {
        public string ManifestJson;
        public string SongId;
        public string Title;
        public string Artist;
        public string JacketDesigner;
        public string BackgroundFile;
        public byte[] AudioBytes;
        public byte[] CoverBytes;
        public int AudioDurationMs;
        public int CoverWidth;
        public int CoverHeight;
        public readonly Dictionary<int, string> Charts =
            new Dictionary<int, string>();
        public readonly Dictionary<int, int> DifficultyRatings =
            new Dictionary<int, int>();
        public readonly Dictionary<int, string> DifficultyChartDesigners =
            new Dictionary<int, string>();

        public string CreateHistoryKey()
        {
            var builder = new StringBuilder(1024);
            builder.Append(ManifestJson);
            builder.Append('\n').Append(CoverWidth);
            builder.Append('x').Append(CoverHeight);
            builder.Append("|audio:").Append(
                AudioBytes != null
                    ? RuntimeHelpers.GetHashCode(AudioBytes)
                    : 0);
            builder.Append("|cover:").Append(
                CoverBytes != null
                    ? RuntimeHelpers.GetHashCode(CoverBytes)
                    : 0);
            var difficulties = new List<int>(Charts.Keys);
            difficulties.Sort();
            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];
                builder.Append("\nchart:")
                    .Append(difficulty)
                    .Append(':')
                    .Append(Charts[difficulty]);
            }
            return builder.ToString();
        }
    }

    public static class FalsumMetadataApplyService
    {
        public static FalsumChartMetadataSnapshot Capture(
            FalsumPackage package)
        {
            if (package == null)
            {
                return null;
            }
            var snapshot = new FalsumChartMetadataSnapshot
            {
                ManifestJson = package.Manifest.Serialize(),
                SongId = package.Manifest.SongId,
                Title = package.Manifest.Title,
                Artist = package.Manifest.Artist,
                JacketDesigner = package.Manifest.JacketDesigner,
                BackgroundFile =
                    package.Manifest.EffectiveBackgroundFile,
                AudioBytes = package.AudioBytes,
                CoverBytes = package.CoverBytes,
                AudioDurationMs =
                    package.Manifest.AudioDurationMs,
                CoverWidth = package.Manifest.CoverWidth,
                CoverHeight = package.Manifest.CoverHeight,
            };
            foreach (var pair in package.Charts)
            {
                snapshot.Charts[pair.Key] = pair.Value;
            }
            foreach (var difficulty in package.Manifest.Difficulties)
            {
                snapshot.DifficultyRatings[difficulty.Index] =
                    difficulty.Rating;
                snapshot.DifficultyChartDesigners[difficulty.Index] =
                    difficulty.ChartDesigner;
            }
            return snapshot;
        }

        public static bool TryApply(
            FalsumPackage package,
            FalsumChartMetadataDraft draft,
            out string error)
        {
            error = string.Empty;
            if (package == null || draft == null)
            {
                error = L.Get("falsum.error.metadata.noEditablePackage");
                return false;
            }
            if (!draft.TryValidate(out error))
            {
                return false;
            }

            var rollback = Capture(package);
            try
            {
                var manifest = package.Manifest;
                var songIdChanged = !string.Equals(
                    manifest.SongId,
                    draft.SongId,
                    StringComparison.Ordinal);
                manifest.SetSongId(draft.SongId);
                manifest.SetTitle(draft.Title.Trim());
                manifest.SetArtist(draft.Artist.Trim());
                manifest.SetJacketDesigner(
                    (draft.JacketDesigner ?? string.Empty).Trim());
                manifest.SetBackground(draft.BackgroundFile);
                for (var i = 0;
                     i < FalsumManifest.DifficultyNames.Length;
                     i++)
                {
                    var difficulty = manifest.FindDifficulty(i);
                    if (difficulty == null || !difficulty.Available)
                    {
                        continue;
                    }
                    difficulty.SetRating(
                        int.Parse(draft.DifficultyRatings[i].Trim()));
                    difficulty.SetChartDesigner(
                        (draft.DifficultyChartDesigners[i] ??
                         string.Empty).Trim());
                }

                if (draft.HasPendingAudio ||
                    !ReferenceEquals(
                        draft.AudioBytes,
                        package.AudioBytes))
                {
                    package.SetAudio(
                        draft.AudioBytes,
                        draft.AudioDurationMs);
                }
                if (draft.HasPendingCover ||
                    !ReferenceEquals(
                        draft.CoverBytes,
                        package.CoverBytes))
                {
                    package.SetCover(
                        draft.CoverBytes,
                        draft.CoverWidth,
                        draft.CoverHeight);
                }

                if (songIdChanged)
                {
                    RewriteSongIds(package, draft.SongId);
                }
                manifest.Validate();
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    Restore(package, rollback);
                }
                catch (Exception restoreException)
                {
                    error = L.Get(
                        "falsum.error.metadata.restoreFailed",
                        ("error", exception.Message),
                        ("restoreError", restoreException.Message));
                    return false;
                }
                error = exception.Message;
                return false;
            }
        }

        public static void Restore(
            FalsumPackage package,
            FalsumChartMetadataSnapshot snapshot)
        {
            if (package == null || snapshot == null)
            {
                return;
            }
            var manifest = string.IsNullOrWhiteSpace(
                    snapshot.ManifestJson)
                ? FalsumManifest.Parse(snapshot.SongId == null
                    ? "{}"
                    : CreateLegacyManifestJson(snapshot))
                : FalsumManifest.Parse(snapshot.ManifestJson);
            package.RestoreState(
                manifest,
                snapshot.Charts,
                snapshot.AudioBytes,
                snapshot.CoverBytes);
        }

        private static string CreateLegacyManifestJson(
            FalsumChartMetadataSnapshot snapshot)
        {
            var manifest = FalsumManifest.CreateTemplate(
                snapshot.SongId,
                snapshot.Title,
                snapshot.Artist,
                snapshot.AudioDurationMs,
                snapshot.BackgroundFile);
            manifest.SetJacketDesigner(snapshot.JacketDesigner);
            foreach (var pair in snapshot.DifficultyRatings)
            {
                var difficulty = manifest.FindDifficulty(pair.Key);
                if (difficulty != null)
                {
                    difficulty.SetRating(pair.Value);
                }
            }
            foreach (var pair in snapshot.DifficultyChartDesigners)
            {
                var difficulty = manifest.FindDifficulty(pair.Key);
                if (difficulty != null)
                {
                    difficulty.SetChartDesigner(pair.Value);
                }
            }
            return manifest.Serialize();
        }

        private static void RewriteSongIds(
            FalsumPackage package,
            string songId)
        {
            var indices = new List<int>(package.Charts.Keys);
            for (var i = 0; i < indices.Count; i++)
            {
                var difficulty = indices[i];
                var chart = package.LoadChart(difficulty);
                chart.SongId = songId;
                package.SetChartText(
                    difficulty,
                    ChartWriter.Write(
                        chart,
                        chart.DelayMs,
                        songId,
                        difficulty));
            }
        }
    }
}
