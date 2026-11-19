using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Core
{
    /// <summary>The whole content of <c>bank.json</c>.</summary>
    public sealed class FalsumSkyAreaBankDocument
    {
        public readonly List<FalsumSkyAreaPreset> Presets =
            new List<FalsumSkyAreaPreset>();

        /// <summary>Whether the sidebar was left open last session.</summary>
        public bool PanelOpened;

        // Ownership belongs to a document and its actual loaded paths. A
        // skipped file (or another folder's file) must never become deletable.
        internal readonly Dictionary<string, string> ManagedPresetFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<FalsumSkyAreaPreset, string> PresetPaths =
            new Dictionary<FalsumSkyAreaPreset, string>();
    }

    /// <summary>
    /// Persistence for the Skyarea Bank.
    ///
    /// <para>ONE FILE PER PRESET: the library is a FOLDER of
    /// <c>&lt;id&gt;.json</c> files under
    /// <c>persistentDataPath/Falsum/SkyAreaBank/</c>, not a single document.
    /// A user can drop a file in or take one out with the file manager (the
    /// panel's footer button opens exactly this folder), and one damaged entry
    /// costs one preset instead of the whole library.</para>
    ///
    /// <para>The in-memory <see cref="FalsumSkyAreaBankDocument"/> API is
    /// unchanged so callers keep working: <see cref="Load"/> assembles a
    /// document from every file, <see cref="Save"/> rewrites the folder so it
    /// updates managed entries while preserving unowned files.</para>
    ///
    /// <para>A legacy single <c>bank.json</c> is migrated once, in place, and
    /// then renamed so it can never be re-imported.</para>
    ///
    /// <para>Logging goes through <see cref="WarningSink"/> instead of
    /// <c>Debug.LogWarning</c> so this file stays testable outside Unity; the
    /// editor wires the sink to the Unity log once at startup.</para>
    /// </summary>
    public static class FalsumSkyAreaBankStore
    {
        /// <summary>
        /// Optional diagnostics override. With nothing assigned the store logs
        /// through <c>UnityEngine.Debug.LogWarning</c> like every other
        /// FALSUM service; tests that run outside the editor assign a
        /// collector so the Core file stays testable without Unity's engine
        /// intrinsics.
        /// </summary>
        public static Action<string> WarningSink;

        private static void Warn(string message)
        {
            var sink = WarningSink;
            if (sink != null)
            {
                sink(message);
                return;
            }
            Debug.LogWarning(message);
        }

        public const string FileName = "bank.json";
        public const string DirectoryName = "SkyAreaBank";
        /// <summary>Extension of one per-preset file.</summary>
        public const string PresetExtension = ".json";

        private const int DocumentVersion = 1;

        private static FalsumSkyAreaBankDocument _cached;
        private static bool _dirty;
        public const string PanelStateFileName = ".panel-state";

        public static string DirectoryPath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(
                        Application.persistentDataPath,
                        "Falsum"),
                    DirectoryName);
            }
        }

        /// <summary>
        /// The legacy single-document path. Still used as the migration source
        /// and by <see cref="SaveTo"/>'s explicit-path overload.
        /// </summary>
        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, FileName); }
        }

        /// <summary>
        /// Loads the whole library: one file per preset, plus the panel flag.
        /// A legacy <c>bank.json</c> is migrated first when no per-preset file
        /// exists yet.
        /// </summary>
        public static FalsumSkyAreaBankDocument Load()
        {
            return LoadFromFolder(DirectoryPath, true);
        }

        public static FalsumSkyAreaBankDocument LoadFromFolder(string directory)
        {
            return LoadFromFolder(directory, false);
        }

        private static FalsumSkyAreaBankDocument LoadFromFolder(
            string directory, bool importBundled)
        {
            var document = new FalsumSkyAreaBankDocument();
            try
            {
                directory = Path.GetFullPath(directory);
                Directory.CreateDirectory(directory);
                MigrateLegacyDocumentIfNeeded(directory, document);
                if (importBundled)
                {
                    ImportBundledPresetsIfNeeded(directory);
                }
                var statePath = Path.Combine(directory, PanelStateFileName);
                if (File.Exists(statePath))
                {
                    try
                    {
                        var state = FalsumJson.AsObject(FalsumJson.Parse(
                            File.ReadAllText(statePath, new UTF8Encoding(false, true))));
                        document.PanelOpened = FalsumJson.GetBool(state, "opened", false);
                    }
                    catch (Exception exception)
                    {
                        Warn("[Falsum] SkyArea Bank panel state could not be read: " +
                             exception.Message);
                    }
                }
                var files = Directory.GetFiles(
                    directory,
                    "*" + PresetExtension);
                Array.Sort(files, StringComparer.Ordinal);
                for (var i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    if (string.Equals(
                            Path.GetFileName(file),
                            FileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var preset = LoadPresetFile(file);
                    if (preset == null)
                    {
                        continue;
                    }
                    if (document.Presets.Count >=
                        FalsumSkyAreaBankModel.MaxPresetsPerBank)
                    {
                        break;
                    }
                    document.Presets.Add(preset);
                    document.PresetPaths[preset] = file;
                    document.ManagedPresetFiles[file] =
                        File.ReadAllText(file, new UTF8Encoding(false, true));
                }
                _cached = document;
                _dirty = false;
                return document;
            }
            catch (Exception exception)
            {
                Warn(
                    "[Falsum] SkyArea Bank could not be loaded: " +
                    exception.Message);
                _cached = document;
                _dirty = false;
                return document;
            }
        }

        /// <summary>
        /// Copies the presets that ship with the editor into the user's library
        /// on first run.
        ///
        /// <para>The shipped payload lives under StreamingAssets so a player
        /// carries it next to the executable, exactly like the bundled tools.
        /// A marker file records that the import already happened, so a preset
        /// the user deleted does NOT come back on the next launch -- that would
        /// make deleting a built-in preset impossible.</para>
        /// </summary>
        private static void ImportBundledPresetsIfNeeded(string directory)
        {
            var marker = Path.Combine(directory, BundledImportMarker);
            if (File.Exists(marker))
            {
                return;
            }
            var source = BundledPresetsDirectory;
            var imported = 0;
            if (!string.IsNullOrEmpty(source) && Directory.Exists(source))
            {
                try
                {
                    var files = Directory.GetFiles(
                        source,
                        "*" + PresetExtension);
                    Array.Sort(files, StringComparer.Ordinal);
                    for (var i = 0; i < files.Length; i++)
                    {
                        var preset = LoadPresetFile(files[i]);
                        if (preset == null)
                        {
                            continue;
                        }
                        // Never overwrite a preset the user already has under
                        // this id; only add what is genuinely missing.
                        var target = PresetFilePath(directory, preset);
                        if (File.Exists(target) &&
                            LoadPresetFile(target)?.Id == preset.Id)
                        {
                            continue;
                        }
                        if (WritePresetFile(directory, preset))
                        {
                            imported++;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Warn(
                        "[Falsum] Bundled SkyArea Bank presets could not be " +
                        "imported: " + exception.Message);
                }
            }
            try
            {
                // Written even when the payload was absent, so a build without
                // presets does not retry the whole import on every launch.
                File.WriteAllText(marker, imported.ToString());
            }
            catch (Exception exception)
            {
                Warn(
                    "[Falsum] Could not record the bundled preset import: " +
                    exception.Message);
            }
            if (imported > 0)
            {
                Warn(
                    "[Falsum] Imported " + imported +
                    " bundled SkyArea Bank preset(s).");
            }
        }

        /// <summary>Marker that the shipped presets were already imported once.</summary>
        public const string BundledImportMarker = ".bundled-imported";

        /// <summary>
        /// Where the editor's own presets ship from. Mirrors
        /// <c>FalsumRuntimePaths.BundledToolsDirectory</c>; kept local so this
        /// Core file keeps its no-Unity-editor dependencies.
        /// </summary>
        private static string BundledPresetsDirectory
        {
            get
            {
                try
                {
                    return Path.Combine(
                        Path.Combine(
                            Application.streamingAssetsPath,
                            "Falsum"),
                        "SkyAreaBankPresets");
                }
                catch (Exception)
                {
                    // StreamingAssets is unavailable off the main thread or in
                    // some test hosts; the import is simply skipped.
                    return null;
                }
            }
        }

        /// <summary>Reads exactly one preset file; null when it is unusable.</summary>
        private static FalsumSkyAreaPreset LoadPresetFile(string path)
        {
            try
            {
                var json = File.ReadAllText(
                    path,
                    new UTF8Encoding(false, true));
                var root = FalsumJson.AsObject(FalsumJson.Parse(json));
                if (root == null)
                {
                    return null;
                }
                FalsumSkyAreaPreset preset;
                string errorKey;
                if (!FalsumSkyAreaBankModel.FromJson(
                        root,
                        out preset,
                        out errorKey))
                {
                    // One bad file must not take the library down.
                    Warn(
                        "[Falsum] Skipped an invalid SkyArea Bank preset (" +
                        Path.GetFileName(path) + ", " + errorKey + ").");
                    return null;
                }
                preset.Id = FalsumJson.GetString(
                    root,
                    "id",
                    string.Empty);
                preset.CreatedAtUtc = (long)FalsumJson.GetNumber(
                    root,
                    "createdAt",
                    preset.CreatedAtUtc);
                preset.UpdatedAtUtc = (long)FalsumJson.GetNumber(
                    root,
                    "updatedAt",
                    preset.UpdatedAtUtc);
                preset.LastUsedAtUtc = (long)FalsumJson.GetNumber(
                    root,
                    "lastUsedAt",
                    preset.LastUsedAtUtc);
                if (string.IsNullOrEmpty(preset.Id))
                {
                    // A hand-copied file may lack an id; the file name is the
                    // only stable identity left, so adopt it.
                    preset.Id = Path.GetFileNameWithoutExtension(path);
                }
                return preset;
            }
            catch (Exception exception)
            {
                Warn(
                    "[Falsum] Skipped unreadable SkyArea Bank preset " +
                    Path.GetFileName(path) + ": " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// Imports a legacy single <c>bank.json</c> once. The file is renamed
        /// afterwards rather than deleted, so a failed migration never destroys
        /// the only copy of the user's library.
        /// </summary>
        private static void MigrateLegacyDocumentIfNeeded(
            string directory,
            FalsumSkyAreaBankDocument document)
        {
            var legacy = Path.Combine(directory, FileName);
            if (!File.Exists(legacy))
            {
                return;
            }
            var existing = Directory.GetFiles(
                directory,
                "*" + PresetExtension);
            for (var i = 0; i < existing.Length; i++)
            {
                if (!string.Equals(
                        Path.GetFileName(existing[i]),
                        FileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Per-preset files already exist: this legacy copy was
                    // already imported, or the user rebuilt the folder.
                    return;
                }
            }
            var migrated = LoadLegacyDocument(legacy);
            if (migrated == null)
            {
                return;
            }
            document.PanelOpened = migrated.PanelOpened;
            var complete = true;
            for (var i = 0; i < migrated.Presets.Count; i++)
            {
                var preset = migrated.Presets[i];
                if (WritePresetFile(directory, preset))
                {
                    Warn(
                        "[Falsum] Migrated SkyArea Bank preset '" +
                        preset.Name + "' to its own file.");
                }
                else
                {
                    complete = false;
                }
            }
            var state = new FalsumJsonObject();
            FalsumJson.Set(state, "opened", document.PanelOpened);
            if (!complete || !WriteJsonFile(
                    Path.Combine(directory, PanelStateFileName), state))
            {
                return;
            }
            try
            {
                File.Move(legacy, legacy + ".migrated");
            }
            catch (Exception exception)
            {
                Warn(
                    "[Falsum] Could not retire the old bank.json: " +
                    exception.Message);
            }
        }

        /// <summary>Parses the legacy single-document format.</summary>
        private static FalsumSkyAreaBankDocument LoadLegacyDocument(string path)
        {
            try
            {
                var json = File.ReadAllText(
                    path,
                    new UTF8Encoding(false, true));
                var root = FalsumJson.AsObject(FalsumJson.Parse(json));
                var version = FalsumJson.GetInt(
                    root,
                    "v",
                    DocumentVersion);
                if (version > DocumentVersion)
                {
                    throw new InvalidDataException(
                        "Unsupported bank document version: " + version);
                }
                var document = new FalsumSkyAreaBankDocument();
                var panel = FalsumJson.GetObject(root, "panel");
                if (panel != null)
                {
                    document.PanelOpened =
                        FalsumJson.GetBool(panel, "opened", false);
                }
                var presets = FalsumJson.GetArray(root, "presets");
                if (presets != null)
                {
                    for (var i = 0; i < presets.Count; i++)
                    {
                        var entry = presets[i] as FalsumJsonObject;
                        if (entry == null)
                        {
                            continue;
                        }
                        FalsumSkyAreaPreset preset;
                        string errorKey;
                        if (!FalsumSkyAreaBankModel.FromJson(
                                entry,
                                out preset,
                                out errorKey))
                        {
                            Warn(
                                "[Falsum] Skipped an invalid SkyArea Bank " +
                                "preset (" + errorKey + ").");
                            continue;
                        }
                        preset.Id = FalsumJson.GetString(
                            entry,
                            "id",
                            preset.Id);
                        preset.CreatedAtUtc = (long)FalsumJson.GetNumber(
                            entry,
                            "createdAt",
                            preset.CreatedAtUtc);
                        preset.UpdatedAtUtc = (long)FalsumJson.GetNumber(
                            entry,
                            "updatedAt",
                            preset.UpdatedAtUtc);
                        preset.LastUsedAtUtc = (long)FalsumJson.GetNumber(
                            entry,
                            "lastUsedAt",
                            preset.LastUsedAtUtc);
                        document.Presets.Add(preset);
                    }
                }
                return document;
            }
            catch (Exception exception)
            {
                Warn(
                    "[Falsum] SkyArea Bank could not be migrated: " +
                    exception.Message);
                PreserveCorruptFile(path);
                return null;
            }
        }

        /// <summary>
        /// Kept for callers that pass an explicit path. It still writes a single
        /// document; the folder is the canonical store now.
        /// </summary>
        public static FalsumSkyAreaBankDocument LoadFrom(string path)
        {
            var document = LoadLegacyDocument(path);
            _cached = document ?? new FalsumSkyAreaBankDocument();
            _dirty = false;
            return _cached;
        }

        public static bool Save(FalsumSkyAreaBankDocument document)
        {
            return SaveToFolder(DirectoryPath, document);
        }

        /// <summary>
        /// Updates the folder: every preset gets its own file, and unchanged
        /// files owned by this document are removed when their preset is gone.
        /// Skipped files and external edits are preserved. Each write is atomic
        /// (temp file, then replace).
        /// </summary>
        public static bool SaveToFolder(
            string directory,
            FalsumSkyAreaBankDocument document)
        {
            if (document == null || string.IsNullOrEmpty(directory))
            {
                return false;
            }
            try
            {
                directory = Path.GetFullPath(directory);
                Directory.CreateDirectory(directory);
                var wanted = new Dictionary<string, FalsumSkyAreaPreset>(
                    StringComparer.OrdinalIgnoreCase);
                // Reserve every existing file, including unreadable/over-cap
                // presets and files copied in after Load(). New entries get a
                // free name; loaded entries retain their actual file name.
                var reserved = new HashSet<string>(
                    Directory.GetFiles(directory, "*" + PresetExtension),
                    StringComparer.OrdinalIgnoreCase);
                reserved.Add(Path.Combine(directory, FileName));
                for (var i = 0; i < document.Presets.Count; i++)
                {
                    var preset = document.Presets[i];
                    if (preset == null) return false;
                    string path;
                    if (!document.PresetPaths.TryGetValue(preset, out path) ||
                        !string.Equals(Path.GetDirectoryName(path), directory,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        var stem = PresetFileStem(preset);
                        path = Path.Combine(directory, stem + PresetExtension);
                        var suffix = 1;
                        while (reserved.Contains(path))
                        {
                            path = Path.Combine(directory,
                                stem + "-" + suffix++ + PresetExtension);
                        }
                    }
                    if (wanted.ContainsKey(path)) return false;
                    wanted.Add(path, preset);
                    reserved.Add(path);
                }
                // Check before any write so external edits cannot be silently
                // overwritten by a stale in-memory document.
                foreach (var entry in wanted)
                {
                    string original;
                    if (File.Exists(entry.Key) &&
                        (!document.ManagedPresetFiles.TryGetValue(entry.Key, out original) ||
                         File.ReadAllText(entry.Key, new UTF8Encoding(false, true)) != original))
                    {
                        Warn("[Falsum] SkyArea Bank file changed on disk; reload before saving: " + entry.Key);
                        return false;
                    }
                }
                foreach (var entry in wanted)
                {
                    if (!WriteJsonFile(entry.Key, ToStoredPresetJson(entry.Value)))
                        return false;
                    document.PresetPaths[entry.Value] = entry.Key;
                    document.ManagedPresetFiles[entry.Key] =
                        File.ReadAllText(entry.Key, new UTF8Encoding(false, true));
                }
                var state = new FalsumJsonObject();
                FalsumJson.Set(state, "opened", document.PanelOpened);
                if (!WriteJsonFile(Path.Combine(directory, PanelStateFileName), state))
                    return false;

                var removed = new List<string>();
                foreach (var entry in document.ManagedPresetFiles)
                {
                    if (wanted.ContainsKey(entry.Key) ||
                        !string.Equals(Path.GetDirectoryName(entry.Key), directory,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (File.Exists(entry.Key))
                        {
                            // A file edited since loading is now outside our
                            // ownership; preserve it, even if its preset was removed.
                            if (File.ReadAllText(entry.Key, new UTF8Encoding(false, true)) != entry.Value)
                            {
                                removed.Add(entry.Key);
                                continue;
                            }
                            File.Delete(entry.Key);
                        }
                        removed.Add(entry.Key);
                    }
                    catch (Exception exception)
                    {
                        Warn("[Falsum] Could not remove SkyArea Bank file " +
                             entry.Key + ": " + exception.Message);
                        return false;
                    }
                }
                foreach (var path in removed) document.ManagedPresetFiles.Remove(path);
                _cached = document;
                _dirty = false;
                return true;
            }
            catch (Exception exception)
            {
                Warn(
                    "[Falsum] SkyArea Bank could not be saved: " +
                    exception.Message);
                return false;
            }
        }

        public static bool SaveTo(
            string path,
            FalsumSkyAreaBankDocument document)
        {
            if (document == null)
            {
                return false;
            }
            if (!WriteJsonFile(path, ToJson(document))) return false;
            _cached = document;
            _dirty = false;
            return true;
        }

        /// <summary>
        /// Filesystem-safe base file name for one preset. SaveToFolder resolves
        /// collisions without overwriting existing files. Derived from the
        /// id, which <see cref="FalsumSkyAreaBankModel.NewId"/> already keeps
        /// to lowercase hex; anything else is sanitised so a hand-edited id
        /// cannot escape the folder.
        /// </summary>
        public static string PresetFileStem(FalsumSkyAreaPreset preset)
        {
            var source = preset != null && !string.IsNullOrEmpty(preset.Id)
                ? preset.Id
                : (preset != null
                    ? FalsumSkyAreaBankModel.NewId()
                    : Guid.NewGuid().ToString("N"));
            var builder = new StringBuilder(source.Length);
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var keep = (c >= 'a' && c <= 'z') ||
                           (c >= 'A' && c <= 'Z') ||
                           (c >= '0' && c <= '9') ||
                           c == '-' || c == '_';
                builder.Append(keep ? c : '_');
            }
            var stem = builder.ToString().Trim('_');
            if (stem.Length == 0)
            {
                stem = Guid.NewGuid().ToString("N");
            }
            if (stem.Length > 48)
            {
                stem = stem.Substring(0, 48);
            }
            return stem;
        }

        public static string PresetFilePath(
            string directory,
            FalsumSkyAreaPreset preset)
        {
            return Path.Combine(
                directory,
                PresetFileStem(preset) + PresetExtension);
        }

        /// <summary>
        /// Writes one preset to its own file, creating the folder when needed.
        /// Reuses the same atomic temp-file + replace sequence as the legacy
        /// document writer so a crash mid-save cannot truncate a preset.
        /// </summary>
        private static bool WritePresetFile(
            string directory,
            FalsumSkyAreaPreset preset)
        {
            if (preset == null) return false;
            var path = PresetFilePath(directory, preset);
            // Import/migration must never overwrite an existing user file.
            var stem = PresetFileStem(preset);
            var suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(directory, stem + "-" + suffix++ + PresetExtension);
            }
            return WriteJsonFile(path, ToStoredPresetJson(preset));
        }

        // The share-code model intentionally omits local identity/timestamps.
        // Folder persistence must add them, just like the legacy bank document.
        private static FalsumJsonObject ToStoredPresetJson(FalsumSkyAreaPreset preset)
        {
            var entry = FalsumSkyAreaBankModel.ToJson(preset);
            FalsumJson.Set(entry, "id", preset.Id);
            FalsumJson.Set(entry, "createdAt", (double)preset.CreatedAtUtc);
            FalsumJson.Set(entry, "updatedAt", (double)preset.UpdatedAtUtc);
            FalsumJson.Set(entry, "lastUsedAt", (double)preset.LastUsedAtUtc);
            return entry;
        }

        private static bool WriteJsonFile(string path, FalsumJsonObject content)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var bytes = new UTF8Encoding(false).GetBytes(
                    FalsumJson.Serialize(content, pretty: true));
                var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(temporary, path, null);
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
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                return true;
            }
            catch (Exception exception)
            {
                Warn("[Falsum] SkyArea Bank file could not be written: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Adds a preset, honouring the library ceiling. Replacing an existing
        /// id is always allowed; a full library only rejects NEW entries.
        /// </summary>
        public static bool AddOrReplace(
            FalsumSkyAreaBankDocument document,
            FalsumSkyAreaPreset preset,
            out string errorKey)
        {
            errorKey = string.Empty;
            if (document == null || preset == null)
            {
                errorKey = FalsumSkyAreaBankModel.ErrorInvalidSegments;
                return false;
            }
            if (!FalsumSkyAreaBankModel.Validate(preset, out errorKey))
            {
                return false;
            }
            var index = FindIndex(document, preset.Id);
            if (index >= 0)
            {
                var previous = document.Presets[index];
                string path;
                if (document.PresetPaths.TryGetValue(previous, out path))
                {
                    document.PresetPaths.Remove(previous);
                    document.PresetPaths[preset] = path;
                }
                document.Presets[index] = preset;
                return true;
            }
            if (document.Presets.Count >=
                FalsumSkyAreaBankModel.MaxPresetsPerBank)
            {
                errorKey = FalsumSkyAreaBankModel.ErrorBankFull;
                return false;
            }
            document.Presets.Add(preset);
            return true;
        }

        public static bool Remove(
            FalsumSkyAreaBankDocument document,
            FalsumSkyAreaPreset preset)
        {
            if (document == null || preset == null)
            {
                return false;
            }
            var index = FindIndex(document, preset.Id);
            if (index < 0)
            {
                return false;
            }
            document.Presets.RemoveAt(index);
            return true;
        }

        public static int FindIndex(
            FalsumSkyAreaBankDocument document,
            string id)
        {
            if (document == null || string.IsNullOrEmpty(id))
            {
                return -1;
            }
            for (var i = 0; i < document.Presets.Count; i++)
            {
                if (string.Equals(
                        document.Presets[i].Id,
                        id,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Marks high-frequency bookkeeping (last-used time, panel state) as
        /// pending. Those changes must not hit the disk per interaction; the
        /// editor flushes them on panel close / difficulty switch / shutdown.
        /// </summary>
        public static void MarkDirty(FalsumSkyAreaBankDocument document)
        {
            _cached = document;
            _dirty = true;
        }

        public static bool Flush()
        {
            if (!_dirty || _cached == null)
            {
                return true;
            }
            return Save(_cached);
        }

        public static void ResetCache()
        {
            _cached = null;
            _dirty = false;
        }

        public static FalsumJsonObject ToJson(
            FalsumSkyAreaBankDocument document)
        {
            var root = new FalsumJsonObject();
            FalsumJson.Set(root, "v", DocumentVersion);
            var panel = new FalsumJsonObject();
            FalsumJson.Set(panel, "opened", document.PanelOpened);
            FalsumJson.Set(root, "panel", panel);

            var presets = new List<object>();
            for (var i = 0; i < document.Presets.Count; i++)
            {
                var entry = FalsumSkyAreaBankModel.ToJson(
                    document.Presets[i]);
                FalsumJson.Set(entry, "id", document.Presets[i].Id);
                FalsumJson.Set(
                    entry,
                    "createdAt",
                    (double)document.Presets[i].CreatedAtUtc);
                FalsumJson.Set(
                    entry,
                    "updatedAt",
                    (double)document.Presets[i].UpdatedAtUtc);
                FalsumJson.Set(
                    entry,
                    "lastUsedAt",
                    (double)document.Presets[i].LastUsedAtUtc);
                presets.Add(entry);
            }
            FalsumJson.Set(root, "presets", presets);
            return root;
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
                Warn(
                    "[Falsum] Corrupt SkyArea Bank backup failed: " +
                    exception.Message);
            }
        }
    }
}
