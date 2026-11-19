using System;
using System.Collections.Generic;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Result of reading a preferences file from disk. Carries the upgrade
    /// to the current schema plus what was actually on disk, so the store can
    /// decide whether a migration write-back is needed.
    /// </summary>
    public struct FalsumPreferencesLoadResult
    {
        public FalsumPreferences Preferences;

        /// <summary>Version as read from the JSON. 0 means the key was absent.</summary>
        public int LoadedVersion;

        /// <summary>True when the file came from a newer build and must be quarantined.</summary>
        public bool UnsupportedVersion;
    }

    /// <summary>
    /// Reads pre-version-6 preference files and upgrades them in memory.
    ///
    /// v1..v5 only ever <em>added</em> fields (verified against real on-disk
    /// samples: the v1 key set is a strict subset of v5, with no renames),
    /// so a single superset DTO is enough. Missing groups deserialize to
    /// null, which is used as the per-group "this version did not have it yet"
    /// signal instead of trusting the Version field alone.
    /// </summary>
    public static class FalsumPreferencesMigration
    {
        [Serializable]
        internal sealed class LegacyDisplayDto
        {
            public int ResolutionWidth;
            public int ResolutionHeight;
            public int ResolutionRefreshRate;
            public bool BorderlessFullscreen;
            public bool AntiAliasingEnabled;
            public int AntiAliasingMode;
        }

        [Serializable]
        internal sealed class LegacyEditingDto
        {
            public bool DirectDragUnselectedNotes;
            public bool MiddleEditAffectsAllSelected;
            public bool SkyAreaGroupTimeSnap;
            public bool DisableOutOfBoundsClamping;
        }

        [Serializable]
        internal sealed class LegacyBackupDto
        {
            public bool Enabled;
            public int IntervalSeconds;
            public int RetentionCount;
        }

        [Serializable]
        internal sealed class LegacyAudioDto
        {
            public float MusicVolume;
            public float SkyAreaVolume;
            public float TapVolume;
            public float HoldVolume;
            public float FlickVolume;
        }

        /// <summary>
        /// v7 scene-surface appearance. Absent in every file written before v7;
        /// because the group is additive the missing case restores the authored
        /// defaults instead of zero-filling them.
        /// </summary>
        [Serializable]
        internal sealed class LegacyVisualDto
        {
            public float SkyAreaOpacity;
            public float SkyAreaGlow;
            public float SideLaneOpacity;
            public float FloorLaneOpacity;
        }

        [Serializable]
        internal sealed class LegacyAdvancedDto
        {
            public bool RestoreLastPackage;
            public bool DisableOutOfBoundsClamping;
            public string LastPackagePath;
            public string GridLineColors;
            public float GridLineWidthScale;
            public bool ShowMeasureNumbers;

            /// <summary>Stored as int so an out-of-range value cannot poison the enum.</summary>
            public int UIStyle;
        }

        [Serializable]
        internal sealed class LegacyPreferencesDto
        {
            /// <summary>No initializer on purpose: an absent key must read as 0.</summary>
            public int Version;

            public LegacyDisplayDto Display;
            public List<FalsumKeyBinding> Bindings;
            public LegacyEditingDto Editing;
            public LegacyBackupDto Backup;
            public LegacyAudioDto Audio;
            public LegacyVisualDto Visual;
            /// <summary>
            /// v7 wrote this group but the DTO lacked a member for it, so
            /// every reload silently reset the grid styling. Present so the
            /// presence probe can distinguish "absent" from "present but
            /// default".
            /// </summary>
            public FalsumGridPreferences Grid;
            public FalsumExperimentalPreferences Experimental;
            public LegacyAdvancedDto Advanced;
        }

        public static FalsumPreferencesLoadResult FromDisk(string json)
        {
            var result = new FalsumPreferencesLoadResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Preferences = FalsumPreferences.CreateDefault();
                result.LoadedVersion = 0;
                return result;
            }

            var dto = JsonUtility.FromJson<LegacyPreferencesDto>(json);
            if (dto == null)
            {
                result.Preferences = FalsumPreferences.CreateDefault();
                result.LoadedVersion = 0;
                return result;
            }

            var loaded = dto.Version;
            result.LoadedVersion = loaded;
            if (loaded > FalsumPreferences.CurrentVersion)
            {
                // Newer file from a later build: let the caller quarantine it
                // rather than silently downgrading the user's settings.
                result.UnsupportedVersion = true;
                result.Preferences = FalsumPreferences.CreateDefault();
                return result;
            }
            if (loaded < 0)
            {
                result.UnsupportedVersion = true;
                result.Preferences = FalsumPreferences.CreateDefault();
                return result;
            }

            result.Preferences = Build(dto, loaded, JsonPresence(json));
            return result;
        }

        /// <summary>
        /// Which top-level groups were actually present in the JSON text.
        ///
        /// This exists because <c>JsonUtility</c> does NOT leave absent object
        /// fields null: it constructs a default instance with the C# field
        /// initializers applied. Verified by direct probe — parsing
        /// <c>{"V":1}</c> into a type with an <c>Inner I = new Inner()</c>
        /// member yields a non-null <c>I</c> whose members are the
        /// initializer values. A null check therefore cannot distinguish
        /// "group absent (v1 had no Audio)" from "group present but zero",
        /// and a zero-filled default would silence every v1 user on upgrade.
        /// </summary>
        private static int JsonPresence(string json)
        {
            const int display = 1 << 0;
            const int bindings = 1 << 1;
            const int editing = 1 << 2;
            const int backup = 1 << 3;
            const int audio = 1 << 4;
            const int advanced = 1 << 5;
            const int visual = 1 << 6;
            const int grid = 1 << 7;
            const int experimental = 1 << 8;
            var presence = 0;
            if (HasTopLevelKey(json, "Display"))
            {
                presence |= display;
            }
            if (HasTopLevelKey(json, "Bindings"))
            {
                presence |= bindings;
            }
            if (HasTopLevelKey(json, "Editing"))
            {
                presence |= editing;
            }
            if (HasTopLevelKey(json, "Backup"))
            {
                presence |= backup;
            }
            if (HasTopLevelKey(json, "Audio"))
            {
                presence |= audio;
            }
            if (HasTopLevelKey(json, "Visual"))
            {
                presence |= visual;
            }
            if (HasTopLevelKey(json, "Grid"))
            {
                presence |= grid;
            }
            if (HasTopLevelKey(json, "Experimental"))
            {
                presence |= experimental;
            }
            if (HasTopLevelKey(json, "Advanced"))
            {
                presence |= advanced;
            }
            return presence;
        }

        private const int PresenceDisplay = 1 << 0;
        private const int PresenceBindings = 1 << 1;
        private const int PresenceEditing = 1 << 2;
        private const int PresenceBackup = 1 << 3;
        private const int PresenceAudio = 1 << 4;
        private const int PresenceAdvanced = 1 << 5;
        private const int PresenceVisual = 1 << 6;
        private const int PresenceGrid = 1 << 7;
        private const int PresenceExperimental = 1 << 8;

        /// <summary>
        /// Cheap top-level key probe. Preference files are written by
        /// JsonUtility with pretty printing, so a top-level key always sits
        /// at the start of a line as <c>"Key":</c>. This deliberately does
        /// not attempt full JSON parsing.
        /// </summary>
        private static bool HasTopLevelKey(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            var needle = "\"" + key + "\"";
            var searchFrom = 0;
            while (true)
            {
                var at = json.IndexOf(needle, searchFrom,
                    StringComparison.Ordinal);
                if (at < 0)
                {
                    return false;
                }
                var after = at + needle.Length;
                while (after < json.Length &&
                       char.IsWhiteSpace(json[after]))
                {
                    after++;
                }
                if (after < json.Length && json[after] == ':')
                {
                    return true;
                }
                searchFrom = at + needle.Length;
            }
        }

        /// <summary>
        /// In-memory upgrade for already-deserialized input. Kept separate
        /// from <see cref="FromDisk"/> so the self-check can exercise every
        /// version without touching the user's file. Takes raw JSON so the
        /// internal DTO never has to leave this assembly-facing surface.
        /// </summary>
        public static FalsumPreferences BuildFromJson(
            string json,
            int loadedVersion)
        {
            var dto = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<LegacyPreferencesDto>(json);
            return Build(dto, loadedVersion, JsonPresence(json));
        }

        private static FalsumPreferences Build(
            LegacyPreferencesDto dto,
            int loadedVersion,
            int presence)
        {
            if (dto == null)
            {
                return FalsumPreferences.CreateDefault();
            }

            var preferences = new FalsumPreferences();
            // Stamp the version as read from disk BEFORE Normalize: the
            // version-keyed migrations inside Normalize (bindings <2,
            // sky-area snap <3, measure numbers <5) run off previousVersion.
            // Normalize then stamps CurrentVersion itself.
            preferences.Version = loadedVersion;

            preferences.Display = BuildDisplay(
                dto.Display,
                (presence & PresenceDisplay) != 0);
            preferences.Editing = BuildEditing(
                dto.Editing,
                dto.Advanced,
                loadedVersion,
                (presence & PresenceEditing) != 0,
                (presence & PresenceAdvanced) != 0);
            preferences.Backup = BuildBackup(
                dto.Backup,
                (presence & PresenceBackup) != 0);
            preferences.Audio = BuildAudio(
                dto.Audio,
                (presence & PresenceAudio) != 0);
            preferences.Visual = BuildVisual(
                dto.Visual,
                (presence & PresenceVisual) != 0);
            preferences.Bindings = (presence & PresenceBindings) != 0 &&
                                   dto.Bindings != null
                ? dto.Bindings
                : new List<FalsumKeyBinding>();

            var grid = BuildGrid(
                dto.Grid,
                (presence & PresenceGrid) != 0);
            var hasExperimental = (presence & PresenceExperimental) != 0 &&
                                  dto.Experimental != null;
            var experimental = hasExperimental
                ? dto.Experimental
                : new FalsumExperimentalPreferences();
            // The pre-v7 Advanced group carried the grid styling. Only lift it
            // when the Grid group itself was absent: a file that has both is
            // a v7+ profile whose Grid is authoritative, and replaying the
            // stale Advanced values would overwrite the user's current choice.
            var liftAdvancedGrid =
                (presence & PresenceGrid) == 0;
            if ((presence & PresenceAdvanced) != 0 && dto.Advanced != null)
            {
                if (liftAdvancedGrid &&
                    !string.IsNullOrEmpty(dto.Advanced.GridLineColors))
                {
                    grid.LineColors = dto.Advanced.GridLineColors;
                }
                if (liftAdvancedGrid &&
                    dto.Advanced.GridLineWidthScale > 0.0f)
                {
                    grid.LineWidthScale =
                        dto.Advanced.GridLineWidthScale;
                }
                // ShowMeasureNumbers and UIStyle are product-gated: they are
                // carried across so Normalize/ApplyProductGates can decide,
                // but never trusted on their own.
                if (!hasExperimental)
                {
                    experimental.ShowMeasureNumbers = dto.Advanced.ShowMeasureNumbers;
                    experimental.UIStyle = (FalsumUiStyle)dto.Advanced.UIStyle;
                }
            }
            preferences.Grid = grid;
            preferences.Experimental = experimental;

            // RestoreLastPackage / LastPackagePath intentionally dropped:
            // neither is consumed anywhere and both are session state.
            preferences.Normalize();
            return preferences;
        }

        private static FalsumDisplayPreferences BuildDisplay(
            LegacyDisplayDto dto,
            bool present)
        {
            var display = new FalsumDisplayPreferences();
            if (!present || dto == null)
            {
                return display;
            }
            display.ResolutionWidth = dto.ResolutionWidth;
            display.ResolutionHeight = dto.ResolutionHeight;
            display.ResolutionRefreshRate = dto.ResolutionRefreshRate;
            display.BorderlessFullscreen = dto.BorderlessFullscreen;
            display.AntiAliasingEnabled = dto.AntiAliasingEnabled;
            display.AntiAliasingMode =
                (FalsumAntiAliasingMode)dto.AntiAliasingMode;
            return display;
        }

        private static FalsumEditingPreferences BuildEditing(
            LegacyEditingDto dto,
            LegacyAdvancedDto advanced,
            int loadedVersion,
            bool present,
            bool advancedPresent)
        {
            var editing = new FalsumEditingPreferences();
            if (present && dto != null)
            {
                editing.DirectDragUnselectedNotes =
                    dto.DirectDragUnselectedNotes;
                editing.MiddleEditAffectsAllSelected =
                    dto.MiddleEditAffectsAllSelected;
                editing.SkyAreaGroupTimeSnap =
                    dto.SkyAreaGroupTimeSnap;
            }
            // v3 introduced the sky-area group time snap and it defaulted on.
            if (loadedVersion < 3)
            {
                editing.SkyAreaGroupTimeSnap = true;
            }
            // DisableOutOfBoundsClamping used to live in Advanced; a v3 file
            // may have no such key at all, so only honour an explicit true.
            if (advancedPresent && advanced != null &&
                advanced.DisableOutOfBoundsClamping)
            {
                editing.DisableOutOfBoundsClamping = true;
            }
            return editing;
        }

        private static FalsumBackupPreferences BuildBackup(
            LegacyBackupDto dto,
            bool present)
        {
            var backup = new FalsumBackupPreferences();
            if (!present || dto == null)
            {
                return backup;
            }
            backup.Enabled = dto.Enabled;
            backup.IntervalSeconds = dto.IntervalSeconds;
            backup.RetentionCount = dto.RetentionCount;
            return backup;
        }

        private static FalsumAudioPreferences BuildAudio(
            LegacyAudioDto dto,
            bool present)
        {
            var audio = new FalsumAudioPreferences();
            if (!present || dto == null)
            {
                // v1 profiles have no Audio group at all. Keeping the
                // initializer defaults (1.0) matters: copying a zero-filled
                // default would silence every user upgrading from v1.
                return audio;
            }
            audio.MusicVolume = dto.MusicVolume;
            audio.SkyAreaVolume = dto.SkyAreaVolume;
            audio.TapVolume = dto.TapVolume;
            audio.HoldVolume = dto.HoldVolume;
            audio.FlickVolume = dto.FlickVolume;
            return audio;
        }

        private static FalsumGridPreferences BuildGrid(
            FalsumGridPreferences dto,
            bool present)
        {
            var grid = new FalsumGridPreferences();
            if (!present || dto == null)
            {
                // v1..v6 files have no Grid group (the styling lived in
                // Advanced). Keeping the initializer defaults matters: the
                // authored colour sequence and denominator sequence are what
                // the editor ships with.
                return grid;
            }
            if (!string.IsNullOrEmpty(dto.LineColors))
            {
                grid.LineColors = dto.LineColors;
            }
            if (dto.LineWidthScale > 0.0f)
            {
                grid.LineWidthScale = dto.LineWidthScale;
            }
            if (dto.TimeSubdivision > 0)
            {
                grid.TimeSubdivision = dto.TimeSubdivision;
            }
            if (!string.IsNullOrEmpty(dto.DenominatorSequence))
            {
                grid.DenominatorSequence = dto.DenominatorSequence;
            }
            return grid;
        }

        private static FalsumVisualPreferences BuildVisual(
            LegacyVisualDto dto,
            bool present)
        {
            var visual = new FalsumVisualPreferences();
            if (!present || dto == null)
            {
                // v1..v6 files have no Visual group. Keeping the initializer
                // defaults (the authored surface opacities) matters: a
                // zero-filled default would make the track and SkyArea
                // invisible for every profile written before v7.
                return visual;
            }
            visual.SkyAreaOpacity = dto.SkyAreaOpacity;
            visual.SkyAreaGlow = dto.SkyAreaGlow;
            visual.SideLaneOpacity = dto.SideLaneOpacity;
            visual.FloorLaneOpacity = dto.FloorLaneOpacity;
            return visual;
        }
    }
}
