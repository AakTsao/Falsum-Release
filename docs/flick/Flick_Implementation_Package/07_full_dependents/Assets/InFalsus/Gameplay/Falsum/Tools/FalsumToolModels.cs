using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Tools
{
    public sealed class FalsumToolManifest
    {
        private static readonly Regex IdPattern = new Regex(
            "^[a-z0-9][a-z0-9._-]{2,127}$",
            RegexOptions.CultureInvariant);

        public int ApiVersion;
        public string Engine;
        public string Id;
        public string Name;
        public string Version;
        public string Author;
        public string Description;
        public string Entry;
        public readonly List<string> Permissions =
            new List<string>();
        public string DirectoryPath;
        public string ScriptPath;
        public string LoadError;
        public bool Enabled = true;
        private readonly Dictionary<string, string> _localizedText =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public bool IsReady
        {
            get { return string.IsNullOrWhiteSpace(LoadError); }
        }

        public string DisplayName
        {
            get { return GetText("tool.name", Name); }
        }

        public string DisplayDescription
        {
            get { return GetText("tool.description", Description); }
        }

        public IReadOnlyDictionary<string, string> LocalizedText
        {
            get { return _localizedText; }
        }

        public string GetText(string key, string fallback)
        {
            string value;
            return !string.IsNullOrEmpty(key) &&
                   _localizedText.TryGetValue(key, out value)
                ? value
                : fallback;
        }

        public bool HasPermission(string permission)
        {
            for (var i = 0; i < Permissions.Count; i++)
            {
                if (string.Equals(
                        Permissions[i],
                        permission,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public string PermissionText
        {
            get
            {
                return Permissions.Count == 0
                    ? L.Get("falsum.tools.permissions.none")
                    : string.Join(
                        L.Get("falsum.tools.permissions.separator"),
                        Permissions.ToArray());
            }
        }

        public static FalsumToolManifest Load(string directoryPath)
        {
            var manifest = new FalsumToolManifest
            {
                DirectoryPath = Path.GetFullPath(directoryPath),
                Id = Path.GetFileName(
                    Path.GetFullPath(directoryPath)
                        .TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)),
                Name = L.Get("falsum.tools.unnamed"),
                Engine = "javascript",
                Version = "0.0.0",
            };
            try
            {
                var manifestPath = Path.Combine(
                    manifest.DirectoryPath,
                    "tool.json");
                if (!File.Exists(manifestPath))
                {
                    throw new InvalidOperationException(
                        L.Get("falsum.error.tool.manifestMissing"));
                }
                var root = FalsumJson.AsObject(
                    FalsumJson.Parse(
                        File.ReadAllText(manifestPath)));
                manifest.ApiVersion = FalsumJson.GetInt(
                    root,
                    "apiVersion",
                    0);
                manifest.Engine = FalsumJson.GetString(
                    root,
                    "engine",
                    string.Empty);
                manifest.Id = FalsumJson.GetString(
                    root,
                    "id",
                    manifest.Id);
                manifest.Name = FalsumJson.GetString(
                    root,
                    "name",
                    manifest.Name);
                manifest.Version = FalsumJson.GetString(
                    root,
                    "version",
                    manifest.Version);
                manifest.Author = FalsumJson.GetString(
                    root,
                    "author",
                    string.Empty);
                manifest.Description = FalsumJson.GetString(
                    root,
                    "description",
                    string.Empty);
                manifest.Entry = FalsumJson.GetString(
                    root,
                    "entry",
                    "tool.js");
                manifest.LoadLocalizedText();

                var permissions = FalsumJson.GetArray(
                    root,
                    "permissions");
                if (permissions != null)
                {
                    for (var i = 0; i < permissions.Count; i++)
                    {
                        var permission = Convert.ToString(
                            permissions[i],
                            CultureInfo.InvariantCulture);
                        if (permission == "files.read" ||
                            permission == "files.write")
                        {
                            if (!manifest.Permissions.Contains(permission))
                            {
                                manifest.Permissions.Add(permission);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                L.Get(
                                    "falsum.error.tool.unknownPermission",
                                    ("permission", permission)));
                        }
                    }
                }

                ValidateManifest(manifest);
            }
            catch (Exception exception)
            {
                manifest.LoadError = L.Get(
                    "falsum.error.tool.loadFailedWithDetails",
                    ("type", exception.GetType().Name),
                    ("error", exception.Message));
            }
            return manifest;
        }

        private void LoadLocalizedText()
        {
            var path = Path.Combine(
                DirectoryPath,
                "i18n",
                L.Locale + ".json");
            if (!File.Exists(path))
            {
                return;
            }

            var root = FalsumJson.AsObject(
                FalsumJson.Parse(
                    File.ReadAllText(path)));
            foreach (var pair in root)
            {
                var text = pair.Value as string;
                if (text != null)
                {
                    _localizedText[pair.Key] = text;
                }
            }
        }

        private static void ValidateManifest(
            FalsumToolManifest manifest)
        {
            if (manifest.ApiVersion != 1)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.apiVersion"));
            }
            if (!string.Equals(
                    manifest.Engine,
                    "javascript",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.engine"));
            }
            if (!IdPattern.IsMatch(manifest.Id ?? string.Empty))
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.invalidId"));
            }
            if (!string.Equals(
                    Path.GetFileName(
                        manifest.DirectoryPath.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)),
                    manifest.Id,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.directoryIdMismatch"));
            }
            if (string.IsNullOrWhiteSpace(manifest.Name) ||
                manifest.Name.Length > 64)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.invalidName"));
            }
            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.versionRequired"));
            }

            var root = Path.GetFullPath(manifest.DirectoryPath);
            var scriptPath = Path.GetFullPath(
                Path.Combine(root, manifest.Entry));
            var rootPrefix = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!scriptPath.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetExtension(scriptPath),
                    ".js",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.invalidEntry"));
            }
            if (!File.Exists(scriptPath))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.entryMissing",
                        ("entry", manifest.Entry)));
            }
            var info = new FileInfo(scriptPath);
            if (info.Length > 2 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    L.Get("falsum.error.tool.entryTooLarge"));
            }
            manifest.ScriptPath = scriptPath;
        }
    }

    public sealed class FalsumToolSelectOption
    {
        public string Value;
        public string Label;
    }

    public sealed class FalsumToolFieldDefinition
    {
        private static readonly string[] SupportedNoteKinds =
        {
            "tap",
            "hold",
            "flick",
            "skyArea",
        };

        public string Key;
        public string Type;
        public string Label;
        public string Description;
        public bool Required;
        public bool Multiple;
        public object DefaultValue;
        public double? Min;
        public double? Max;
        public double? Step;
        public bool ChartPick;
        public string FileMode = "open";
        public string DefaultFileName;
        public readonly List<FalsumToolSelectOption> Options =
            new List<FalsumToolSelectOption>();
        public readonly List<string> Extensions =
            new List<string>();

        public readonly List<string> NoteKinds =
            new List<string>();

        public bool IsNoteField
        {
            get
            {
                return string.Equals(
                    Type,
                    "note",
                    StringComparison.Ordinal);
            }
        }

        public int EffectiveNoteMinimum
        {
            get
            {
                return Min.HasValue
                    ? (int)Math.Round(Min.Value)
                    : (Required ? 1 : 0);
            }
        }

        public int EffectiveNoteMaximum
        {
            get
            {
                if (Max.HasValue)
                {
                    return (int)Math.Round(Max.Value);
                }
                return Multiple ? int.MaxValue : 1;
            }
        }

        public bool AllowsNoteKind(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return false;
            }
            for (var i = 0; i < NoteKinds.Count; i++)
            {
                if (string.Equals(
                        NoteKinds[i],
                        kind,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsSupportedNoteKind(string kind)
        {
            for (var i = 0; i < SupportedNoteKinds.Length; i++)
            {
                if (string.Equals(
                        SupportedNoteKinds[i],
                        kind,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class FalsumToolNoteReference
    {
        public string Key;
        public int Id;
        public string Kind;
        public double TimeMs;

        public FalsumJsonObject ToJsonObject()
        {
            return new FalsumJsonObject
            {
                { "key", Key },
                { "id", Id },
                { "kind", Kind },
                { "timeMs", TimeMs },
            };
        }
    }

    public sealed class FalsumToolNotePickRequest
    {
        public string FieldKey;
        public bool Multiple;
        public int MinCount;
        public int MaxCount;
        public readonly List<string> NoteKinds =
            new List<string>();
        public readonly List<FalsumToolNoteReference> SelectedNotes =
            new List<FalsumToolNoteReference>();
    }

    public sealed class FalsumToolFormDefinition
    {
        public string Title;
        public readonly List<FalsumToolFieldDefinition> Fields =
            new List<FalsumToolFieldDefinition>();

        public static FalsumToolFormDefinition FromJson(
            object value)
        {
            var root = FalsumJson.AsObject(value);
            var form = new FalsumToolFormDefinition
            {
                Title = FalsumJson.GetString(
                    root,
                    "title",
                    string.Empty),
            };
            var fields = FalsumJson.GetArray(root, "fields");
            if (fields == null)
            {
                throw new FormatException(
                    L.Get("falsum.error.tool.formFieldsMissing"));
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < fields.Count; i++)
            {
                var field = ParseField(
                    FalsumJson.AsObject(fields[i]));
                if (!keys.Add(field.Key))
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.duplicateField",
                            ("field", field.Key)));
                }
                form.Fields.Add(field);
            }
            return form;
        }

        private static FalsumToolFieldDefinition ParseField(
            FalsumJsonObject source)
        {
            var field = new FalsumToolFieldDefinition
            {
                Key = FalsumJson.GetString(
                    source,
                    "key",
                    string.Empty),
                Type = FalsumJson.GetString(
                    source,
                    "type",
                    string.Empty),
                Label = FalsumJson.GetString(
                    source,
                    "label",
                    string.Empty),
                Description = FalsumJson.GetString(
                    source,
                    "description",
                    string.Empty),
                Required = FalsumJson.GetBool(
                    source,
                    "required",
                    false),
                ChartPick = FalsumJson.GetBool(
                    source,
                    "chartPick",
                    false),
                FileMode = FalsumJson.GetString(
                    source,
                    "mode",
                    "open"),
                DefaultFileName = FalsumJson.GetString(
                    source,
                    "defaultFileName",
                    string.Empty),
            };
            if (string.IsNullOrWhiteSpace(field.Key) ||
                !Regex.IsMatch(
                    field.Key,
                    "^[A-Za-z_][A-Za-z0-9_]*$",
                    RegexOptions.CultureInvariant))
            {
                throw new FormatException(
                    L.Get("falsum.error.tool.invalidFieldKey"));
            }
            if (field.Type != "text" &&
                field.Type != "number" &&
                field.Type != "integer" &&
                field.Type != "time" &&
                field.Type != "select" &&
                field.Type != "checkbox" &&
                field.Type != "file" &&
                field.Type != "note")
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.unknownFieldType",
                        ("type", field.Type)));
            }

            object defaultValue;
            if (source.TryGetValue("defaultValue", out defaultValue))
            {
                field.DefaultValue = defaultValue;
            }

            object min;
            if (source.TryGetValue("min", out min) && min != null)
            {
                field.Min = Convert.ToDouble(
                    min,
                    CultureInfo.InvariantCulture);
            }
            object max;
            if (source.TryGetValue("max", out max) && max != null)
            {
                field.Max = Convert.ToDouble(
                    max,
                    CultureInfo.InvariantCulture);
            }
            object step;
            if (source.TryGetValue("step", out step) && step != null)
            {
                field.Step = Convert.ToDouble(
                    step,
                    CultureInfo.InvariantCulture);
            }

            var options = FalsumJson.GetArray(source, "options");
            if (options != null)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    var option = options[i] as FalsumJsonObject;
                    if (option == null)
                    {
                        field.Options.Add(
                            new FalsumToolSelectOption
                            {
                                Value = Convert.ToString(
                                    options[i],
                                    CultureInfo.InvariantCulture),
                                Label = Convert.ToString(
                                    options[i],
                                    CultureInfo.InvariantCulture),
                            });
                    }
                    else
                    {
                        var value = FalsumJson.GetString(
                            option,
                            "value",
                            string.Empty);
                        field.Options.Add(
                            new FalsumToolSelectOption
                            {
                                Value = value,
                                Label = FalsumJson.GetString(
                                    option,
                                    "label",
                                    value),
                            });
                    }
                }
            }

            var extensions = FalsumJson.GetArray(
                source,
                "extensions");
            if (extensions != null)
            {
                for (var i = 0; i < extensions.Count; i++)
                {
                    var extension = Convert.ToString(
                        extensions[i],
                        CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        field.Extensions.Add(
                            extension.TrimStart('.'));
                    }
                }
            }

            field.Multiple = FalsumJson.GetBool(
                source,
                "multiple",
                false);

            var noteKinds = FalsumJson.GetArray(
                source,
                "noteKinds");
            if (field.IsNoteField)
            {
                if (string.IsNullOrWhiteSpace(field.Label))
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.noteLabelRequired",
                            ("field", field.Key)));
                }
                if (noteKinds == null)
                {
                    field.NoteKinds.Add("tap");
                    field.NoteKinds.Add("hold");
                    field.NoteKinds.Add("flick");
                    field.NoteKinds.Add("skyArea");
                }
                else
                {
                    for (var i = 0; i < noteKinds.Count; i++)
                    {
                        var kind = Convert.ToString(
                            noteKinds[i],
                            CultureInfo.InvariantCulture);
                        if (!FalsumToolFieldDefinition.IsSupportedNoteKind(
                                kind))
                        {
                            throw new FormatException(
                                L.Get(
                                    "falsum.error.tool.unknownNoteKind",
                                    ("kind", kind),
                                    ("field", field.Key)));
                        }
                        if (field.NoteKinds.Contains(kind))
                        {
                            throw new FormatException(
                                L.Get(
                                    "falsum.error.tool.duplicateNoteKind",
                                    ("kind", kind),
                                    ("field", field.Key)));
                        }
                        field.NoteKinds.Add(kind);
                    }
                }

                if (field.NoteKinds.Count == 0)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.noteKindsRequired",
                            ("field", field.Key)));
                }

                ValidateNoteSelectionRange(field);
            }
            return field;
        }

        private static void ValidateNoteSelectionRange(
            FalsumToolFieldDefinition field)
        {
            if (field.Min.HasValue &&
                (field.Min.Value < 0.0 ||
                 field.Min.Value > int.MaxValue ||
                 Math.Abs(field.Min.Value -
                          Math.Round(field.Min.Value)) >
                 0.000001))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.noteMinInvalid",
                        ("field", field.Key)));
            }
            if (field.Max.HasValue &&
                (field.Max.Value <= 0.0 ||
                 field.Max.Value > int.MaxValue ||
                 Math.Abs(field.Max.Value -
                          Math.Round(field.Max.Value)) >
                 0.000001))
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.noteMaxInvalid",
                        ("field", field.Key)));
            }

            var min = field.EffectiveNoteMinimum;
            var max = field.EffectiveNoteMaximum;
            if (max < min)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.noteRangeInvalid",
                        ("field", field.Key)));
            }
            if (!field.Multiple && max > 1)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.noteSingleRange",
                        ("field", field.Key)));
            }
        }
    }

    public sealed class FalsumToolValidationIssue
    {
        public string Field;
        public string Message;
    }

    public sealed class FalsumToolFileSelection
    {
        public string FieldKey;
        public string Path;
        public bool Save;
    }

    public sealed class FalsumToolRunRequest
    {
        public string ToolId;
        public readonly Dictionary<string, object> Values =
            new Dictionary<string, object>(StringComparer.Ordinal);
        public readonly Dictionary<string, FalsumToolFileSelection> Files =
            new Dictionary<string, FalsumToolFileSelection>(
                StringComparer.Ordinal);
    }

    public sealed class FalsumToolReport
    {
        public string SourceToolName;
        public string Title;
        public string Summary;
        public string EmptyText;
        public readonly List<FalsumToolReportItem> Items =
            new List<FalsumToolReportItem>();
    }

    public sealed class FalsumToolReportItem
    {
        public string Title;
        public string Message;
        public string Severity = "error";
        public double TimeMs = -1.0;
        public readonly List<ChartNote> TargetNotes =
            new List<ChartNote>();

        public bool CanLocate
        {
            get { return TimeMs >= 0.0; }
        }
    }
}

