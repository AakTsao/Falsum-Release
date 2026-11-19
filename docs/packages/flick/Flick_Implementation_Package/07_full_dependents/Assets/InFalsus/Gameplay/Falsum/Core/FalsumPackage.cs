using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Core
{
    public sealed class FalsumPackage
    {
        private const int MaxManifestBytes = 1024 * 1024;
        private const int MaxChartBytes = 16 * 1024 * 1024;
        private const int MaxCoverBytes = 32 * 1024 * 1024;
        private const int MaxAudioBytes = 128 * 1024 * 1024;

        private readonly Dictionary<string, byte[]> _unknownEntries =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly List<string> _warnings = new List<string>();

        public string SourcePath { get; private set; }
        public FalsumManifest Manifest { get; private set; }
        public byte[] AudioBytes { get; private set; }
        public byte[] CoverBytes { get; private set; }
        public Dictionary<int, string> Charts { get; private set; }
        public IList<string> Warnings { get { return _warnings; } }

        private FalsumPackage()
        {
            Charts = new Dictionary<int, string>();
        }

        public static FalsumPackage Load(string path)
        {
            return Load(path, null);
        }

        public static FalsumPackage Load(
            string path,
            string sourcePathOverride)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.package.pathEmpty"),
                    "path");
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    L.Get("falsum.error.package.notFound"),
                    path);
            }
            if (!string.Equals(
                    Path.GetExtension(path),
                    ".falsum",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    L.Get("falsum.error.package.extensionInvalid"));
            }

            var package = new FalsumPackage
            {
                SourcePath = Path.GetFullPath(
                    string.IsNullOrWhiteSpace(sourcePathOverride)
                        ? path
                        : sourcePathOverride),
            };
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Read,
                       false))
            {
                var entries = BuildEntryMap(archive);
                var manifestBytes = ReadRequired(
                    entries,
                    "song.json",
                    MaxManifestBytes);
                var manifestText = DecodeUtf8NoBom(
                    manifestBytes,
                    "song.json");
                package.Manifest = FalsumManifest.Parse(manifestText);
                package.AudioBytes = ReadRequired(
                    entries,
                    package.Manifest.AudioFile,
                    MaxAudioBytes);
                ValidateAudioBytes(package.AudioBytes);

                if (!string.IsNullOrWhiteSpace(
                        package.Manifest.CoverFile))
                {
                    package.CoverBytes = ReadRequired(
                        entries,
                        package.Manifest.CoverFile,
                        MaxCoverBytes);
                    ValidateCoverBytes(package.CoverBytes);
                }

                var available = 0;
                for (var i = 0; i < package.Manifest.Difficulties.Count; i++)
                {
                    var difficulty = package.Manifest.Difficulties[i];
                    if (!difficulty.Available)
                    {
                        continue;
                    }
                    try
                    {
                        var chartBytes = ReadRequired(
                            entries,
                            difficulty.ChartPath,
                            MaxChartBytes);
                        var chartText = DecodeUtf8NoBom(
                            chartBytes,
                            difficulty.ChartPath);
                        var result = ChartParser.Parse(
                            SplitLines(chartText),
                            difficulty.ChartPath);
                        if (!result.Success)
                        {
                            throw new InvalidDataException(
                                L.Get(
                                    "falsum.error.package.chartInvalid",
                                    ("path", difficulty.ChartPath),
                                    ("error", result.Diagnostics[0])));
                        }
                        result.Chart.SongId = package.Manifest.SongId;
                        result.Chart.Difficulty = difficulty.Index;
                        package.Charts[difficulty.Index] = chartText;
                        available++;
                    }
                    catch (Exception exception)
                    {
                        package._warnings.Add(
                            difficulty.Name + ": " + exception.Message);
                    }
                }
                if (available == 0)
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.package.noReadableChart"));
                }

                foreach (var pair in entries)
                {
                    if (IsKnownPackagePath(pair.Key, package.Manifest))
                    {
                        continue;
                    }
                    if (pair.Value.Length > MaxChartBytes)
                    {
                        continue;
                    }
                    package._unknownEntries[pair.Key] = ReadEntry(pair.Value);
                }
            }
            return package;
        }

        public static FalsumPackage CreateNew(
            FalsumNewChartRequest request,
            byte[] oggBytes,
            int durationMs,
            byte[] coverPngBytes,
            int coverWidth,
            int coverHeight)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            if (string.IsNullOrWhiteSpace(request.TargetPath))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.package.targetPathRequired"),
                    "request");
            }
            if (oggBytes == null || oggBytes.Length == 0)
            {
                throw new ArgumentException(
                    L.Get("falsum.error.package.audioRequired"),
                    "oggBytes");
            }
            var songId = (request.SongId ?? string.Empty).Trim();
            if (songId.Length == 0)
            {
                throw new ArgumentException(
                    L.Get("falsum.error.package.songIdRequired"),
                    "request");
            }
            if (!FalsumManifest.IsValidNewSongId(songId))
            {
                throw new ArgumentException(
                    L.Get(
                        "falsum.error.package.songIdInvalid",
                        ("songId", songId)),
                    "request");
            }
            // The package file name is user-chosen and independent of the
            // song id: writing <songId>.falsum here would silently rename
            // a file the author picked. song.json and the chart headers
            // carry the identity instead.
            var targetPath = Path.GetFullPath(request.TargetPath);

            var manifest = FalsumManifest.CreateTemplate(
                songId,
                request.Title,
                request.Artist,
                durationMs);
            manifest.SetJacketDesigner(request.JacketDesigner);
            var enabledCount = 0;
            for (var i = 0; i < FalsumManifest.DifficultyNames.Length; i++)
            {
                var info = manifest.FindDifficulty(i);
                if (info == null)
                {
                    continue;
                }
                var enabled = request.DifficultyEnabled[i];
                if (enabled)
                {
                    enabledCount++;
                }
                info.SetAvailable(enabled);
                info.SetRating(request.Ratings[i]);
                info.SetChartDesigner(
                    request.ChartDesigners[i] ?? string.Empty);
            }
            if (enabledCount == 0)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.package.difficultyRequired"));
            }

            var package = new FalsumPackage
            {
                SourcePath = targetPath,
                Manifest = manifest,
            };
            package.SetAudio(oggBytes, durationMs);
            if (coverPngBytes != null && coverPngBytes.Length > 0)
            {
                package.SetCover(
                    coverPngBytes,
                    coverWidth,
                    coverHeight);
            }
            for (var i = 0; i < FalsumManifest.DifficultyNames.Length; i++)
            {
                if (!request.DifficultyEnabled[i])
                {
                    continue;
                }
                var chart = new Chart
                {
                    Bpm = 120.0,
                    Division = 4.0,
                    SongId = songId,
                    Difficulty = i,
                };
                package.SetChartText(
                    i,
                    ChartWriter.Write(
                        chart,
                        0.0,
                        songId,
                        i));
            }
            return package;
        }

        public Chart LoadChart(int difficulty)
        {
            string text;
            if (!Charts.TryGetValue(difficulty, out text))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.package.difficultyUnavailable",
                        ("difficulty", difficulty)));
            }
            var result = ChartParser.Parse(
                SplitLines(text),
                SourcePath,
                difficulty);
            if (!result.Success)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.chartParseFailed",
                        ("error", result.Diagnostics[0])));
            }
            result.Chart.SongId = Manifest.SongId;
            result.Chart.Difficulty = difficulty;
            return result.Chart;
        }

        public string LoadChartText(int difficulty)
        {
            string text;
            return Charts.TryGetValue(difficulty, out text)
                ? text
                : string.Empty;
        }

        public int HighestAvailableDifficulty()
        {
            for (var index = 3; index >= 0; index--)
            {
                if (Charts.ContainsKey(index))
                {
                    return index;
                }
            }
            return 0;
        }

        public void SetChartText(int difficulty, string text)
        {
            Charts[difficulty] = text ?? string.Empty;
        }

        public void SetCover(byte[] pngBytes, int width, int height)
        {
            CoverBytes = pngBytes;
            var cover = FalsumJson.GetObject(
                Manifest.Root,
                "cover");
            if (cover == null)
            {
                cover = new FalsumJsonObject();
                FalsumJson.Set(Manifest.Root, "cover", cover);
            }
            FalsumJson.Set(cover, "file", "cover.png");
            FalsumJson.Set(cover, "width", width);
            FalsumJson.Set(cover, "height", height);
        }

        public void SetBackground(string backgroundFile)
        {
            Manifest.SetBackground(backgroundFile);
        }

        public void SetAudio(byte[] oggBytes, int durationMs)
        {
            AudioBytes = oggBytes;
            var audio = FalsumJson.GetObject(
                Manifest.Root,
                "audio");
            if (audio == null)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.audioObjectMissing"));
            }
            FalsumJson.Set(audio, "durationMs", durationMs);
        }

        public void Save(string targetPath)
        {
            SaveTo(targetPath, true);
        }

        public void SaveCopy(string targetPath)
        {
            SaveTo(targetPath, false);
        }

        internal void RestoreState(
            FalsumManifest manifest,
            IDictionary<int, string> charts,
            byte[] audioBytes,
            byte[] coverBytes)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }
            manifest.Validate();
            if (audioBytes == null || audioBytes.Length == 0)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.restoredAudioMissing"));
            }
            ValidateAudioBytes(audioBytes);
            if (!string.IsNullOrWhiteSpace(manifest.CoverFile))
            {
                ValidateCoverBytes(coverBytes);
            }

            var restoredCharts = new Dictionary<int, string>();
            if (charts != null)
            {
                foreach (var pair in charts)
                {
                    if (pair.Key < 0 || pair.Key > 3 ||
                        string.IsNullOrWhiteSpace(pair.Value))
                    {
                        throw new InvalidDataException(
                            L.Get(
                                "falsum.error.package.restoredChartEntryInvalid"));
                    }
                    var result = ChartParser.Parse(
                        SplitLines(pair.Value),
                        "<metadata-restore>",
                        pair.Key);
                    if (!result.Success)
                    {
                        throw new InvalidDataException(
                            L.Get(
                                "falsum.error.package.restoredChartInvalid",
                                ("difficulty", pair.Key),
                                ("error", result.Diagnostics[0].Message)));
                    }
                    restoredCharts[pair.Key] = pair.Value;
                }
            }
            for (var i = 0; i < manifest.Difficulties.Count; i++)
            {
                var difficulty = manifest.Difficulties[i];
                if (difficulty.Available &&
                    !restoredCharts.ContainsKey(difficulty.Index))
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.package.restoredDifficultyNoChart",
                            ("difficulty", difficulty.Index)));
                }
            }

            if (Manifest == null)
            {
                Manifest = manifest;
            }
            else
            {
                Manifest.ReplaceFrom(manifest);
            }
            Charts = restoredCharts;
            AudioBytes = audioBytes;
            CoverBytes = coverBytes;
        }

        private void SaveTo(
            string targetPath,
            bool updateSourcePath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.package.targetPathEmpty"),
                    "targetPath");
            }
            if (AudioBytes == null || AudioBytes.Length == 0)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.package.audioMissing"));
            }

            var fullPath = Path.GetFullPath(targetPath);
            if (!string.Equals(
                    Path.GetExtension(fullPath),
                    ".falsum",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.package.extensionRequired"));
            }
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Manifest.Validate();
            ValidateAudioBytes(AudioBytes);
            if (!string.IsNullOrWhiteSpace(Manifest.CoverFile))
            {
                ValidateCoverBytes(CoverBytes);
            }
            var saveManifest = FalsumManifest.Parse(
                Manifest.Serialize());
            for (var i = 0; i < Manifest.Difficulties.Count; i++)
            {
                var difficulty = Manifest.Difficulties[i];
                if (difficulty.Available &&
                    !Charts.ContainsKey(difficulty.Index))
                {
                    saveManifest.FindDifficulty(
                        difficulty.Index).SetAvailable(false);
                }
            }
            saveManifest.Validate();

            var temporary = fullPath + ".tmp-" +
                            Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                using (var archive = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           false))
                {
                    WriteText(
                        archive,
                        "song.json",
                        saveManifest.Serialize());
                    WriteBytes(
                        archive,
                        saveManifest.AudioFile,
                        AudioBytes,
                        CompressionLevel.NoCompression);
                    if (CoverBytes != null &&
                        CoverBytes.Length > 0 &&
                        !string.IsNullOrWhiteSpace(
                            saveManifest.CoverFile))
                    {
                        WriteBytes(
                            archive,
                            saveManifest.CoverFile,
                            CoverBytes,
                            CompressionLevel.NoCompression);
                    }
                    var chartDifficulties =
                        new List<FalsumDifficultyInfo>(
                            saveManifest.Difficulties);
                    chartDifficulties.Sort(
                        delegate(
                            FalsumDifficultyInfo left,
                            FalsumDifficultyInfo right)
                        {
                            return string.CompareOrdinal(
                                left.ChartPath,
                                right.ChartPath);
                        });
                    var writtenPaths = new HashSet<string>(
                        StringComparer.Ordinal)
                    {
                        "song.json",
                        saveManifest.AudioFile,
                    };
                    if (!string.IsNullOrWhiteSpace(
                            saveManifest.CoverFile))
                    {
                        writtenPaths.Add(saveManifest.CoverFile);
                    }
                    for (var i = 0;
                         i < chartDifficulties.Count;
                         i++)
                    {
                        var difficulty = chartDifficulties[i];
                        string text;
                        if (!difficulty.Available ||
                            !Charts.TryGetValue(difficulty.Index, out text))
                        {
                            continue;
                        }
                        if (!writtenPaths.Add(difficulty.ChartPath))
                        {
                            throw new InvalidDataException(
                                L.Get(
                                    "falsum.error.package.duplicateChartPath",
                                    ("path", difficulty.ChartPath)));
                        }
                        WriteText(
                            archive,
                            difficulty.ChartPath,
                            text);
                    }
                    foreach (var pair in _unknownEntries)
                    {
                        if (writtenPaths.Contains(pair.Key))
                        {
                            continue;
                        }
                        WriteBytes(
                            archive,
                            pair.Key,
                            pair.Value,
                            CompressionLevel.Optimal);
                    }
                }

                if (File.Exists(fullPath))
                {
                    // REPLACE, DON'T COPY-OVER.
                    //
                    // File.Copy(temp, target, overwrite: true) cannot replace a
                    // file that any other handle still holds open, and a package
                    // opened straight out of a Downloads folder is routinely held
                    // by a sync client or a scanner. That made in-place saves
                    // (which the audio preprocess must do) fail with
                    // "The process cannot access the file ... being used by
                    // another process".
                    //
                    // File.Replace is the Win32 replace primitive: it swaps the
                    // file record instead of rewriting contents, so a reader that
                    // already has the old file open keeps reading the old bytes
                    // while new opens see the new ones.
                    //
                    // File.Move(temporary, destination, overwrite: true) is a
                    // .NET Core API and does not exist in the profile this
                    // project compiles against, so the fallback is explicit:
                    // delete then move. That is not atomic, but it only runs when
                    // Replace refused, and by then the alternative is no save at
                    // all.
                    var replaced = false;
                    try
                    {
                        File.Replace(
                            temporary,
                            fullPath,
                            null,
                            true);
                        replaced = true;
                    }
                    catch (Exception)
                    {
                        replaced = false;
                    }
                    if (!replaced)
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                        }
                        File.Move(temporary, fullPath);
                    }
                }
                else
                {
                    File.Move(temporary, fullPath);
                }
                if (updateSourcePath)
                {
                    SourcePath = fullPath;
                }
                for (var i = 0; i < Manifest.Difficulties.Count; i++)
                {
                    var current = Manifest.Difficulties[i];
                    var saved = saveManifest.FindDifficulty(
                        current.Index);
                    if (saved != null)
                    {
                        current.SetAvailable(saved.Available);
                    }
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static Dictionary<string, ZipArchiveEntry> BuildEntryMap(
            ZipArchive archive)
        {
            var entries = new Dictionary<string, ZipArchiveEntry>(
                StringComparer.Ordinal);
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                var entry = archive.Entries[i];
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }
                ValidateEntryPath(entry.FullName);
                if (entries.ContainsKey(entry.FullName))
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.package.duplicateZipEntry",
                            ("entry", entry.FullName)));
                }
                entries.Add(entry.FullName, entry);
            }
            return entries;
        }

        private static void ValidateEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.IndexOf('\\') >= 0 ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.IndexOf('\0') >= 0)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.unsafeZipPath",
                        ("path", path)));
            }
            var parts = path.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == ".." || parts[i] == ".")
                {
                    throw new InvalidDataException(
                        L.Get(
                            "falsum.error.package.unsafeZipPath",
                            ("path", path)));
                }
            }
            if (path.Length >= 2 && path[1] == ':')
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.absoluteZipPath",
                        ("path", path)));
            }
        }

        private static byte[] ReadRequired(
            Dictionary<string, ZipArchiveEntry> entries,
            string path,
            int maxBytes)
        {
            ZipArchiveEntry entry;
            if (!entries.TryGetValue(path, out entry))
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.requiredEntryMissing",
                        ("entry", path)));
            }
            return ReadEntry(entry, maxBytes);
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            return ReadEntry(entry, int.MaxValue);
        }

        private static byte[] ReadEntry(
            ZipArchiveEntry entry,
            int maxBytes)
        {
            if (entry.Length < 0 || entry.Length > maxBytes)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.zipEntryTooLarge",
                        ("entry", entry.FullName)));
            }
            using (var input = entry.Open())
            using (var output = new MemoryStream(
                       entry.Length > int.MaxValue
                           ? 0
                           : (int)entry.Length))
            {
                var buffer = new byte[81920];
                var total = 0;
                while (true)
                {
                    var read = input.Read(
                        buffer,
                        0,
                        buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidDataException(
                            L.Get(
                                "falsum.error.package.zipEntryTooLarge",
                                ("entry", entry.FullName)));
                    }
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static string DecodeUtf8NoBom(
            byte[] bytes,
            string path)
        {
            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.utf8Bom",
                        ("path", path)));
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.utf8Invalid",
                        ("path", path)));
            }
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }

        private static bool IsKnownPackagePath(
            string path,
            FalsumManifest manifest)
        {
            if (path == "song.json" ||
                path == manifest.AudioFile ||
                path == manifest.CoverFile)
            {
                return true;
            }
            for (var i = 0; i < manifest.Difficulties.Count; i++)
            {
                if (path == manifest.Difficulties[i].ChartPath)
                {
                    return true;
                }
            }
            return false;
        }

        private static void WriteText(
            ZipArchive archive,
            string path,
            string text)
        {
            WriteBytes(
                archive,
                path,
                new UTF8Encoding(false).GetBytes(text ?? string.Empty),
                CompressionLevel.Optimal);
        }

        private static void WriteBytes(
            ZipArchive archive,
            string path,
            byte[] bytes,
            CompressionLevel compression)
        {
            ValidateEntryPath(path);
            var entry = archive.CreateEntry(path, compression);
            entry.LastWriteTime = new DateTimeOffset(
                1980,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);
            using (var output = entry.Open())
            {
                output.Write(bytes, 0, bytes.Length);
            }
        }

        private static void ValidateAudioBytes(byte[] bytes)
        {
            if (bytes == null ||
                bytes.Length < 4 ||
                bytes[0] != 'O' ||
                bytes[1] != 'g' ||
                bytes[2] != 'g' ||
                bytes[3] != 'S')
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.audioNotOgg"));
            }
        }

        private static void ValidateCoverBytes(byte[] bytes)
        {
            if (bytes == null ||
                bytes.Length < 8 ||
                bytes[0] != 0x89 ||
                bytes[1] != 0x50 ||
                bytes[2] != 0x4E ||
                bytes[3] != 0x47 ||
                bytes[4] != 0x0D ||
                bytes[5] != 0x0A ||
                bytes[6] != 0x1A ||
                bytes[7] != 0x0A)
            {
                throw new InvalidDataException(
                    L.Get(
                        "falsum.error.package.coverNotPng"));
            }
        }
    }
}
