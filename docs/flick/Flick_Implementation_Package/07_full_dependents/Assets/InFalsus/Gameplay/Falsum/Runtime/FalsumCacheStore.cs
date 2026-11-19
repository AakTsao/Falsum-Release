using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumCacheStore
    {
        private const string CacheDirectoryName = "FalsumCache";
        private const string WaveformFilePrefix = "waveform-v";

        public static string DirectoryPath
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    CacheDirectoryName);
            }
        }

        public static long GetSizeBytes()
        {
            var root = DirectoryPath;
            if (!Directory.Exists(root))
            {
                return 0L;
            }
            long total = 0L;
            try
            {
                var files = Directory.GetFiles(
                    root,
                    "*",
                    SearchOption.AllDirectories);
                for (var i = 0; i < files.Length; i++)
                {
                    try
                    {
                        total += new FileInfo(files[i]).Length;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return total;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024L)
            {
                return L.Get(
                    "falsum.unit.bytes",
                    ("value", bytes));
            }
            if (bytes < 1024L * 1024L)
            {
                return L.Get(
                    "falsum.unit.kilobytes",
                    ("value", (bytes / 1024.0).ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)));
            }
            if (bytes < 1024L * 1024L * 1024L)
            {
                return L.Get(
                    "falsum.unit.megabytes",
                    ("value", (bytes / (1024.0 * 1024.0)).ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)));
            }
            return L.Get(
                "falsum.unit.gigabytes",
                ("value", (bytes / (1024.0 * 1024.0 * 1024.0)).ToString(
                    "0.0",
                    CultureInfo.InvariantCulture)));
        }

        public static long Clear()
        {
            var root = DirectoryPath;
            var clearedBytes = GetSizeBytes();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
            Directory.CreateDirectory(root);
            return clearedBytes;
        }

        /// <summary>
        /// Removes leftover waveform segment files. Track waveforms now live
        /// in memory only, so anything named waveform-v* is a stale artifact
        /// from an earlier build. Audio cache (.ogg) is left untouched.
        /// </summary>
        public static long DeleteWaveformCacheFiles()
        {
            var root = DirectoryPath;
            if (!Directory.Exists(root))
            {
                return 0L;
            }
            long clearedBytes = 0L;
            try
            {
                var files = Directory.GetFiles(
                    root,
                    WaveformFilePrefix + "*",
                    SearchOption.TopDirectoryOnly);
                for (var i = 0; i < files.Length; i++)
                {
                    try
                    {
                        var length = new FileInfo(files[i]).Length;
                        File.Delete(files[i]);
                        clearedBytes += length;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return clearedBytes;
        }
    }
}
