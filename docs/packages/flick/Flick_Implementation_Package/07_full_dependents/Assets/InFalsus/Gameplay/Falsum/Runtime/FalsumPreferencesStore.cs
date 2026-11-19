using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumPreferencesStore
    {
        private static FalsumPreferences _cached;

        public static string DirectoryPath
        {
            get
            {
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
                    "preferences.json");
            }
        }

        public static FalsumPreferences Load()
        {
            if (_cached != null)
            {
                return _cached.Clone();
            }
            _cached = LoadFromDisk();
            return _cached.Clone();
        }

        public static void Save(FalsumPreferences preferences)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException("preferences");
            }
            // Stamping the version is the only mutation Save performs on the
            // caller's object. Normalize() used to run here as well, which
            // meant saving could silently rewrite unrelated fields (resolution
            // repaired from Screen.currentResolution) in the caller's instance.
            preferences.Version = FalsumPreferences.CurrentVersion;
            Directory.CreateDirectory(DirectoryPath);
            var path = FilePath;
            var temporary = path + ".tmp-" +
                            Guid.NewGuid().ToString("N");
            var json = JsonUtility.ToJson(preferences, true);
            var bytes = new UTF8Encoding(false).GetBytes(json);
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
                _cached = preferences.Clone();
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        public static void ResetCache()
        {
            _cached = null;
        }

        private static FalsumPreferences LoadFromDisk()
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return FalsumPreferences.CreateDefault();
            }
            try
            {
                var json = File.ReadAllText(
                    path,
                    new UTF8Encoding(false, true));
                var migration = FalsumPreferencesMigration.FromDisk(json);
                if (migration.Preferences == null)
                {
                    throw new InvalidDataException(
                        "Preferences JSON produced no object.");
                }
                if (migration.UnsupportedVersion)
                {
                    throw new InvalidDataException(
                        "Unsupported preferences version: " +
                        migration.LoadedVersion);
                }
                var preferences = migration.Preferences;
                var loadedVersion = migration.LoadedVersion;
                if (loadedVersion <
                    FalsumPreferences.CurrentVersion)
                {
                    try
                    {
                        Save(preferences);
                    }
                    catch (Exception saveException)
                    {
                        Debug.LogWarning(
                            "[Falsum] Migrated preferences could " +
                            "not be written: " +
                            saveException.Message);
                    }
                }
                return preferences;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Preferences could not be loaded: " +
                    exception.Message);
                PreserveCorruptFile(path);
                return FalsumPreferences.CreateDefault();
            }
        }

        private static void PreserveCorruptFile(string path)
        {
            try
            {
                var backup = path + ".corrupt-" +
                             DateTime.UtcNow.ToString(
                                 "yyyyMMdd-HHmmss-fff");
                File.Copy(path, backup, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Falsum] Corrupt preferences backup failed: " +
                    exception.Message);
            }
        }
    }
}
