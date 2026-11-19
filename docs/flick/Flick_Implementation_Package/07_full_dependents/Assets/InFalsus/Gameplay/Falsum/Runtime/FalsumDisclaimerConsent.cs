using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumDisclaimerConsent
    {
        public const int CurrentRevision = 1;
        private const string FileName =
            "disclaimer-consent.json";
        private static bool? _accepted;

        [Serializable]
        private sealed class ConsentRecord
        {
            public int Revision;
            public string AcceptedAtUtc;
            public string EditorVersion;
        }

        public static string DirectoryPath
        {
            get
            {
                if (Application.isEditor)
                {
                    var projectRoot =
                        Path.GetDirectoryName(Application.dataPath) ??
                        Directory.GetCurrentDirectory();
                    return Path.Combine(
                        projectRoot,
                        "UserSettings",
                        "Falsum");
                }
                return Path.Combine(
                    Application.persistentDataPath,
                    "Falsum");
            }
        }

        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    DirectoryPath,
                    FileName);
            }
        }

        public static bool HasAccepted
        {
            get
            {
                if (!_accepted.HasValue)
                {
                    _accepted = LoadFromDisk();
                }
                return _accepted.Value;
            }
        }

        public static void Accept()
        {
            var record = new ConsentRecord
            {
                Revision = CurrentRevision,
                AcceptedAtUtc = DateTime.UtcNow.ToString("O"),
                EditorVersion = FalsumEditorInfo.Version,
            };
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = FilePath;
                var temporary = path + ".tmp-" +
                                Guid.NewGuid().ToString("N");
                var bytes = new UTF8Encoding(false).GetBytes(
                    JsonUtility.ToJson(record, true));
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(
                                temporary,
                                path,
                                null);
                        }
                        catch (Exception)
                        {
                            File.Copy(temporary, path, true);
                            File.Delete(temporary);
                        }
                    }
                    else
                    {
                        File.Move(temporary, path);
                    }
                    _accepted = true;
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
            catch (Exception exception)
            {
                _accepted = true;
                Debug.LogWarning(
                    "[Falsum] Disclaimer consent could not be saved: " +
                    exception.Message);
            }
        }

        public static void ResetForTests()
        {
            _accepted = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Disclaimer consent reset failed: " +
                    exception.Message);
            }
        }

        private static bool LoadFromDisk()
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                var record = JsonUtility.FromJson<ConsentRecord>(
                    File.ReadAllText(
                        path,
                        new UTF8Encoding(false, true)));
                return record != null &&
                       record.Revision >= CurrentRevision &&
                       !string.IsNullOrWhiteSpace(
                           record.AcceptedAtUtc);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Disclaimer consent could not be read: " +
                    exception.Message);
                return false;
            }
        }
    }
}
