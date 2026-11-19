using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using InFalsus.Gameplay.Falsum.Core;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public sealed class FalsumBackupEntry
    {
        public string FilePath;
        public int Difficulty;
        public DateTime ModifiedUtc;
    }

    public static class FalsumBackupService
    {
        public static string RootDirectory
        {
            get
            {
                var programDirectory =
                    Path.GetDirectoryName(Application.dataPath) ??
                    Directory.GetCurrentDirectory();
                return Path.Combine(programDirectory, "autobackups");
            }
        }

        public static string EnsureRootDirectory()
        {
            var root = RootDirectory;
            Directory.CreateDirectory(root);
            return root;
        }

        public static string CreateChartBackup(
            string sourcePath,
            string songId,
            int difficulty,
            string chartText)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.backup.sourceRequired"),
                    "sourcePath");
            }

            var directory = BackupDirectory(
                sourcePath,
                songId,
                difficulty);
            return CreateChartBackupInDirectory(
                directory,
                chartText);
        }

        public static string CreateChartBackupInDirectory(
            string directory,
            string chartText)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.backup.directoryRequired"),
                    "directory");
            }
            Directory.CreateDirectory(directory);
            var stamp = DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss-fff");
            var path = Path.Combine(
                directory,
                stamp + ".chart.txt");
            File.WriteAllText(
                path,
                chartText ?? string.Empty,
                new UTF8Encoding(false));
            return path;
        }

        public static string FindLatest(
            string sourcePath,
            string songId,
            int difficulty)
        {
            var directory = BackupDirectory(
                sourcePath,
                songId,
                difficulty);
            if (!Directory.Exists(directory))
            {
                return string.Empty;
            }
            return Directory
                .EnumerateFiles(
                    directory,
                    "*.chart.txt",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
        }

        public static List<FalsumBackupEntry> ListBackups(
            string sourcePath,
            string songId)
        {
            var result = new List<FalsumBackupEntry>();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return result;
            }
            for (var difficulty = 0; difficulty < 4; difficulty++)
            {
                var directory = BackupDirectory(
                    sourcePath,
                    songId,
                    difficulty);
                if (!Directory.Exists(directory))
                {
                    continue;
                }
                try
                {
                    var files = Directory.EnumerateFiles(
                        directory,
                        "*.chart.txt",
                        SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        result.Add(new FalsumBackupEntry
                        {
                            FilePath = file,
                            Difficulty = difficulty,
                            ModifiedUtc =
                                File.GetLastWriteTimeUtc(file),
                        });
                    }
                }
                catch (Exception)
                {
                }
            }
            return result
                .OrderByDescending(entry => entry.ModifiedUtc)
                .ThenByDescending(
                    entry => entry.FilePath,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Prune(
            string sourcePath,
            string songId,
            int difficulty,
            int retentionCount)
        {
            var directory = BackupDirectory(
                sourcePath,
                songId,
                difficulty);
            PruneDirectory(
                directory,
                retentionCount);
        }

        public static void PruneDirectory(
            string directory,
            int retentionCount)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }
            var keep = Math.Max(1, retentionCount);
            var files = Directory
                .EnumerateFiles(
                    directory,
                    "*.chart.txt",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .Skip(keep);
            foreach (var path in files)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        public static string BackupDirectory(
            string sourcePath,
            string songId,
            int difficulty)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException(
                    L.Get("falsum.error.backup.sourceRequired"),
                    "sourcePath");
            }
            var root = EnsureRootDirectory();
            var safeSong = SafeSegment(
                string.IsNullOrWhiteSpace(songId)
                    ? "untitled"
                    : songId);
            var key = SourcePathKey(sourcePath);
            return Path.Combine(
                root,
                safeSong + "-" + key,
                "d" + Mathf.Clamp(difficulty, 0, 3).ToString());
        }

        private static string SourcePathKey(string sourcePath)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(
                    Path.GetFullPath(sourcePath)
                        .Trim()
                        .ToUpperInvariant());
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(12);
                for (var i = 0; i < 6; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string SafeSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                builder.Append(
                    Array.IndexOf(invalid, character) >= 0 ||
                    character == '.' ||
                    character == ' '
                        ? '_'
                        : character);
            }
            var result = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result)
                ? "untitled"
                : result;
        }
    }
}
