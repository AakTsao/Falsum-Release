using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Core;
using Jint;
using Jint.Native;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Tools
{
    public sealed class FalsumToolRuntimeException : Exception
    {
        public readonly string Code;

        public FalsumToolRuntimeException(
            string code,
            string message,
            Exception inner)
            : base(message, inner)
        {
            Code = code;
        }
    }

    public sealed class FalsumToolRuntime : IDisposable
    {
        private sealed class FileToken
        {
            public string Id;
            public string Kind;
            public string Path;
            public bool CanRead;
            public bool CanWrite;
            public bool RequiresConfirmation;
            public string DisplayName;
        }

        private const int MaxFileBytes = 64 * 1024 * 1024;
        private const long MaxTotalWriteBytes = 128L * 1024 * 1024;

        private readonly FalsumToolManifest _manifest;
        private readonly FalsumToolChartSnapshot _snapshot;
        private readonly Dictionary<string, FileToken> _fileTokens =
            new Dictionary<string, FileToken>(StringComparer.Ordinal);
        private readonly HashSet<string> _writtenTokens =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _removedEventKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _updatedEventKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _removedNoteKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _updatedNoteKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _logs =
            new List<string>();

        private Engine _engine;
        private JsValue _definition;
        private FalsumToolEditBatch _batch;
        private FalsumToolRunOutput _output;
        private JsValue _inputs;
        private bool _executionActive;
        private long _totalWriteBytes;
        private int _nextFileTokenId;

        public FalsumToolManifest Manifest
        {
            get { return _manifest; }
        }

        public FalsumToolChartSnapshot Snapshot
        {
            get { return _snapshot; }
        }

        public IList<string> Logs
        {
            get { return _logs; }
        }

        public FalsumToolRuntime(
            FalsumToolManifest manifest,
            FalsumToolChartSnapshot snapshot)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }
            _manifest = manifest;
            _snapshot = snapshot;
            _definition = JsValue.Undefined;
            _inputs = JsValue.Undefined;
        }

        public static void Probe(FalsumToolManifest manifest)
        {
            var snapshot = new FalsumToolChartSnapshot
            {
                Bpm = 120.0,
                Division = 4.0,
            };
            using (var runtime = new FalsumToolRuntime(
                       manifest,
                       snapshot))
            {
                runtime.Load();
            }
        }

        public void Load()
        {
            if (!_manifest.IsReady)
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_MANIFEST_INVALID",
                    _manifest.LoadError,
                    null);
            }
            try
            {
                var options = new Options()
                    .LimitRecursion(128)
                    .MaxStatements(1000000)
                    .LimitMemory(64L * 1024 * 1024)
                    .TimeoutInterval(TimeSpan.FromSeconds(5))
                    .DisableStringCompilation(true)
                    .Strict(true);
                options.Interop.Enabled = false;
                _engine = new Engine(options);
                RegisterHostCallbacks();
                _engine.Execute(FalsumToolScripts.RuntimeBootstrap);
                var script = File.ReadAllText(
                    _manifest.ScriptPath,
                    Encoding.UTF8);
                _engine.Execute(script, _manifest.ScriptPath);
                if (_definition.IsUndefined() ||
                    _definition.IsNull())
                {
                    throw new InvalidOperationException(
                        L.Get(
                            "falsum.error.tool.runtime.registerNotCalled"));
                }
            }
            catch (FalsumToolRuntimeException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_SCRIPT_SYNTAX",
                    DescribeException(exception),
                    exception);
            }
        }

        public FalsumToolFormDefinition CreateForm()
        {
            EnsureLoaded();
            var context = BuildContext(false, false);
            var createForm = GetDefinitionFunction("createForm");
            if (createForm.IsUndefined())
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_SCRIPT_SYNTAX",
                    L.Get(
                        "falsum.error.tool.runtime.createFormMissing"),
                    null);
            }
            try
            {
                var result = _engine.Invoke(
                    createForm,
                    _definition,
                    new object[] { context });
                var json = SerializeJsResult(result);
                if (string.IsNullOrWhiteSpace(json) ||
                    json == "null")
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.createFormInvalid"));
                }
                return FalsumToolFormDefinition.FromJson(
                    FalsumJson.Parse(json));
            }
            catch (Exception exception)
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_EXECUTION_FAILED",
                    L.Get(
                        "falsum.error.tool.runtime.createFormFailed",
                        ("error", DescribeException(exception))),
                    exception);
            }
        }

        public List<FalsumToolValidationIssue> Validate(
            FalsumToolRunRequest request)
        {
            EnsureLoaded();
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            var issues = new List<FalsumToolValidationIssue>();
            ConfigureExternalFiles(request);
            _inputs = CreateInputValue(request);
            var context = BuildContext(true, false);
            var validate = GetDefinitionFunction("validate");
            if (validate.IsUndefined())
            {
                return issues;
            }
            try
            {
                var result = _engine.Invoke(
                    validate,
                    _definition,
                    new object[] { _inputs, context });
                var json = SerializeJsResult(result);
                if (string.IsNullOrWhiteSpace(json) ||
                    json == "null")
                {
                    return issues;
                }
                var parsed = FalsumJson.Parse(json);
                var list = parsed as List<object>;
                if (list == null)
                {
                    if (parsed is string)
                    {
                        issues.Add(
                            new FalsumToolValidationIssue
                            {
                                Field = string.Empty,
                                Message = Convert.ToString(
                                    parsed,
                                    CultureInfo.InvariantCulture),
                            });
                        return issues;
                    }
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.validateArrayRequired"));
                }
                for (var i = 0; i < list.Count; i++)
                {
                    var item = list[i] as FalsumJsonObject;
                    if (item != null)
                    {
                        issues.Add(
                            new FalsumToolValidationIssue
                            {
                                Field = FalsumJson.GetString(
                                    item,
                                    "field",
                                    string.Empty),
                                Message = FalsumJson.GetString(
                                    item,
                                    "message",
                                    L.Get(
                                        "falsum.error.tool.runtime.invalidInput")),
                            });
                    }
                    else
                    {
                        issues.Add(
                            new FalsumToolValidationIssue
                            {
                                Field = string.Empty,
                                Message = Convert.ToString(
                                    list[i],
                                    CultureInfo.InvariantCulture),
                            });
                    }
                }
                return issues;
            }
            catch (Exception exception)
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_INPUT_INVALID",
                    L.Get(
                        "falsum.error.tool.runtime.validateFailed",
                        ("error", DescribeException(exception))),
                    exception);
            }
        }

        public FalsumToolRunOutput Run(
            FalsumToolRunRequest request)
        {
            EnsureLoaded();
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            _batch = new FalsumToolEditBatch();
            _output = new FalsumToolRunOutput
            {
                Batch = _batch,
            };
            _removedEventKeys.Clear();
            _updatedEventKeys.Clear();
            _removedNoteKeys.Clear();
            _updatedNoteKeys.Clear();
            _writtenTokens.Clear();
            _totalWriteBytes = 0;
            ConfigureExternalFiles(request);
            _inputs = CreateInputValue(request);
            var context = BuildContext(true, true);
            var run = GetDefinitionFunction("run");
            if (run.IsUndefined())
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_SCRIPT_SYNTAX",
                    L.Get(
                        "falsum.error.tool.runtime.runMissing"),
                    null);
            }
            _executionActive = true;
            try
            {
                var result = _engine.Invoke(
                    run,
                    _definition,
                    new object[] { context });
                var json = SerializeJsResult(result);
                if (!string.IsNullOrWhiteSpace(json) &&
                    json != "null")
                {
                    var parsed = FalsumJson.Parse(json) as
                        FalsumJsonObject;
                    if (parsed != null)
                    {
                        _output.Message = FalsumJson.GetString(
                            parsed,
                            "message",
                            string.Empty);
                        object details;
                        if (parsed.TryGetValue("details", out details))
                        {
                            _output.Details = details;
                        }
                        object report;
                        if (parsed.TryGetValue("report", out report) &&
                            report != null)
                        {
                            try
                            {
                                _output.Report =
                                    ParseReport(report);
                            }
                            catch (FalsumToolRuntimeException)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                throw new FalsumToolRuntimeException(
                                    "TOOL_RESULT_INVALID",
                                    L.Get(
                                        "falsum.error.tool.runtime.reportInvalid",
                                        ("error", DescribeException(exception))),
                                    exception);
                            }
                        }
                    }
                }
                return _output;
            }
            catch (FalsumToolRuntimeException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FalsumToolRuntimeException(
                    "TOOL_EXECUTION_FAILED",
                    L.Get(
                        "falsum.error.tool.runtime.executionFailed",
                        ("error", DescribeException(exception))),
                    exception);
            }
            finally
            {
                _executionActive = false;
            }
        }

        private FalsumToolReport ParseReport(object value)
        {
            var source = FalsumJson.AsObject(value);
            var report = new FalsumToolReport
            {
                SourceToolName = _manifest.DisplayName,
                Title = ReadReportText(
                    source,
                    "title",
                    _manifest.DisplayName,
                    80,
                    false),
                Summary = ReadReportText(
                    source,
                    "summary",
                    string.Empty,
                    500,
                    false),
                EmptyText = ReadReportText(
                    source,
                    "emptyText",
                    L.Get(
                        "falsum.tools.report.noIssuesReturned"),
                    300,
                    false),
            };
            var items = FalsumJson.GetArray(source, "items");
            if (items == null)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.runtime.reportItemsArrayRequired"));
            }
            if (items.Count > 1000)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.runtime.reportItemsTooMany"));
            }
            for (var i = 0; i < items.Count; i++)
            {
                var itemSource = items[i] as FalsumJsonObject;
                if (itemSource == null)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.reportItemObjectRequired",
                            ("index", i)));
                }
                var item = new FalsumToolReportItem
                {
                    Title = ReadReportText(
                        itemSource,
                        "title",
                        string.Empty,
                        80,
                        true),
                    Message = ReadReportText(
                        itemSource,
                        "message",
                        string.Empty,
                        500,
                        true),
                    Severity = ReadReportSeverity(itemSource),
                };
                ReadReportTargets(itemSource, item);
                ReadReportTime(itemSource, item);
                if (!item.CanLocate)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.reportTargetRequired"));
                }
                report.Items.Add(item);
            }
            return report;
        }

        private void ReadReportTargets(
            FalsumJsonObject source,
            FalsumToolReportItem item)
        {
            var keys = FalsumJson.GetArray(source, "noteKeys");
            if (keys == null)
            {
                return;
            }
            if (keys.Count > 32)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.runtime.tooManyNoteKeys"));
            }
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i] as string;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.noteKeyEmpty"));
                }
                var record = _snapshot.FindNote(key);
                if (record == null)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.noteKeyMissing",
                            ("key", key)));
                }
                if (!item.TargetNotes.Contains(record.Note))
                {
                    item.TargetNotes.Add(record.Note);
                }
            }
        }

        private static void ReadReportTime(
            FalsumJsonObject source,
            FalsumToolReportItem item)
        {
            object raw;
            if (source.TryGetValue("timeMs", out raw) &&
                raw != null)
            {
                var time = ReadFiniteDouble(
                    raw,
                    L.Get("falsum.label.reportTime"));
                if (time < 0.0 ||
                    Math.Abs(time - Math.Round(time)) >
                    0.000001)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.reportTimeInvalid"));
                }
                item.TimeMs = Math.Round(time);
                return;
            }
            if (item.TargetNotes.Count == 0)
            {
                return;
            }
            var earliest = double.MaxValue;
            for (var i = 0; i < item.TargetNotes.Count; i++)
            {
                earliest = Math.Min(
                    earliest,
                    item.TargetNotes[i].TimeMs);
            }
            item.TimeMs = Math.Max(0.0, Math.Round(earliest));
        }

        private static string ReadReportText(
            FalsumJsonObject source,
            string key,
            string fallback,
            int maxLength,
            bool required)
        {
            var text = FalsumJson.GetString(
                source,
                key,
                fallback);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (required)
                {
                    throw new FormatException(
                        L.Get(
                            "falsum.error.tool.runtime.reportTextRequired",
                            ("key", key)));
                }
                return fallback;
            }
            if (text.Length > maxLength)
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.runtime.reportTextTooLong",
                        ("key", key),
                        ("max", maxLength)));
            }
            return text;
        }

        private static string ReadReportSeverity(
            FalsumJsonObject source)
        {
            var severity = FalsumJson.GetString(
                source,
                "severity",
                "error");
            if (severity != "error" &&
                severity != "warning" &&
                severity != "info")
            {
                throw new FormatException(
                    L.Get(
                        "falsum.error.tool.runtime.severityInvalid"));
            }
            return severity;
        }

        public void Dispose()
        {
            _engine = null;
            _definition = JsValue.Undefined;
        }

        private void RegisterHostCallbacks()
        {
            _engine.SetValue(
                "__falsumRegister",
                new Action<JsValue>(RegisterDefinition));
            _engine.SetValue(
                "__hostNotify",
                new Action<string>(Notify));
            _engine.SetValue(
                "__hostSkyAreaDisconnectedIntervals",
                new Func<string>(
                    SkyAreaDisconnectedIntervalsJson));
            _engine.SetValue(
                "__hostLog",
                new Action<string, string>(Log));
            _engine.SetValue(
                "__hostEventAdd",
                new Action<string>(AddEventJson));
            _engine.SetValue(
                "__hostEventUpdate",
                new Action<string, string>(UpdateEventJson));
            _engine.SetValue(
                "__hostEventRemove",
                new Action<string>(RemoveEvent));
            _engine.SetValue(
                "__hostNoteAdd",
                new Action<string>(AddNoteJson));
            _engine.SetValue(
                "__hostNoteUpdate",
                new Action<string, string>(UpdateNoteJson));
            _engine.SetValue(
                "__hostNoteRemove",
                new Action<string>(RemoveNote));
            _engine.SetValue(
                "__hostSetTiming",
                new Action<string>(SetTimingJson));
            _engine.SetValue(
                "__hostFileToken",
                new Func<string, string, string>(
                    CreateInternalFileToken));
            _engine.SetValue(
                "__hostFileReadText",
                new Func<string, string>(ReadFileText));
            _engine.SetValue(
                "__hostFileReadBase64",
                new Func<string, string>(ReadFileBase64));
            _engine.SetValue(
                "__hostFileWriteText",
                new Action<string, string>(WriteFileText));
            _engine.SetValue(
                "__hostFileWriteBase64",
                new Action<string, string>(WriteFileBase64));
        }

        private void RegisterDefinition(JsValue value)
        {
            if (!_definition.IsUndefined())
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.registerOnce"));
            }
            _definition = value;
        }

        private void Notify(string message)
        {
            if (_output != null && !string.IsNullOrWhiteSpace(message))
            {
                _output.Notifications.Add(message);
            }
        }

        private string SkyAreaDisconnectedIntervalsJson()
        {
            var intervals =
                FalsumSkyAreaPlayability
                    .FindDisconnectedIntervals(
                        _snapshot.Notes);
            var result = new List<object>(intervals.Count);
            for (var i = 0; i < intervals.Count; i++)
            {
                var interval = intervals[i];
                result.Add(
                    new FalsumJsonObject
                    {
                        {
                            "startTimeMs",
                            interval.StartTimeMs
                        },
                        {
                            "endTimeMs",
                            interval.EndTimeMs
                        },
                        {
                            "activeCount",
                            interval.ActiveCount
                        },
                        {
                            "noteKeys",
                            interval.NoteKeys
                        },
                    });
            }
            return FalsumJson.Serialize(result, false);
        }

        private void Log(string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            var line = "[" + level + "] " + message;
            _logs.Add(line);
            if (level == "warn")
            {
                Debug.LogWarning(
                    "[Falsum Tool:" + _manifest.Name + "] " + message);
            }
            else if (level == "error")
            {
                Debug.LogError(
                    "[Falsum Tool:" + _manifest.Name + "] " + message);
            }
            else
            {
                Debug.Log(
                    "[Falsum Tool:" + _manifest.Name + "] " + message);
            }
        }

        private JsValue BuildContext(
            bool includeInputs,
            bool allowEdits)
        {
            var tool = new FalsumJsonObject
            {
                { "id", _manifest.Id },
                { "name", _manifest.DisplayName },
                { "version", _manifest.Version },
            };
            var texts = new FalsumJsonObject();
            foreach (var pair in _manifest.LocalizedText)
            {
                texts[pair.Key] = pair.Value;
            }
            var data = new FalsumJsonObject
            {
                { "tool", tool },
                { "locale", L.Locale },
                { "texts", texts },
                { "chart", _snapshot.CreateContextObject() },
            };
            _engine.SetValue(
                "__falsumContextData",
                JsonToJsValue(FalsumJson.Serialize(data, false)));
            if (includeInputs)
            {
                _engine.SetValue(
                    "__falsumInputsData",
                    JsonToJsValue(SerializeInputs()));
            }
            return _engine.Evaluate(
                "__falsumBuildContext(" +
                (includeInputs ? "true" : "false") +
                "," +
                (allowEdits ? "true" : "false") +
                ")");
        }

        private JsValue CreateInputValue(
            FalsumToolRunRequest request)
        {
            if (request.Values == null)
            {
                return JsonToJsValue("{}");
            }
            var values = new FalsumJsonObject();
            foreach (var pair in request.Values)
            {
                values[pair.Key] = pair.Value;
            }
            return JsonToJsValue(
                FalsumJson.Serialize(values, false));
        }

        private string SerializeInputs()
        {
            if (_inputs.IsUndefined() || _inputs.IsNull())
            {
                return "{}";
            }
            _engine.SetValue("__falsumInputsToSerialize", _inputs);
            return _engine.Evaluate(
                "JSON.stringify(__falsumInputsToSerialize)")
                .AsString();
        }

        private JsValue JsonToJsValue(string json)
        {
            _engine.SetValue("__falsumJsonPayload", json ?? "null");
            return _engine.Evaluate(
                "JSON.parse(__falsumJsonPayload)");
        }

        private JsValue GetDefinitionFunction(string name)
        {
            if (_definition.IsUndefined() ||
                _definition.IsNull())
            {
                return JsValue.Undefined;
            }
            return _definition.AsObject().Get(name);
        }

        private string SerializeJsResult(JsValue value)
        {
            if (value.IsUndefined())
            {
                return "null";
            }
            _engine.SetValue("__falsumToolResult", value);
            return _engine.Evaluate(
                "JSON.stringify(__falsumToolResult)").AsString();
        }

        private void EnsureLoaded()
        {
            if (_engine == null ||
                _definition.IsUndefined() ||
                _definition.IsNull())
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.notLoaded"));
            }
        }

        private void ConfigureExternalFiles(
            FalsumToolRunRequest request)
        {
            if (request.Files == null)
            {
                return;
            }
            foreach (var pair in request.Files)
            {
                var selection = pair.Value;
                if (selection == null ||
                    string.IsNullOrWhiteSpace(selection.Path))
                {
                    continue;
                }
                var fullPath = Path.GetFullPath(selection.Path);
                if (!selection.Save && !File.Exists(fullPath))
                {
                    throw new FalsumToolRuntimeException(
                        "TOOL_FILE_IO_FAILED",
                        L.Get(
                            "falsum.error.tool.runtime.selectedFileMissing",
                            ("path", fullPath)),
                        null);
                }
                var tokenId = "external:" + pair.Key;
                _fileTokens[tokenId] = new FileToken
                {
                    Id = tokenId,
                    Kind = "external",
                    Path = fullPath,
                    CanRead = true,
                    CanWrite = selection.Save,
                    RequiresConfirmation = true,
                    DisplayName = Path.GetFileName(fullPath),
                };
                request.Values[pair.Key] =
                    new FalsumJsonObject
                    {
                        { "token", tokenId },
                        { "name", Path.GetFileName(fullPath) },
                    };
            }
        }

        private string CreateInternalFileToken(
            string kind,
            string relativePath)
        {
            if (kind != "asset" && kind != "data")
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.unknownFileTokenKind"));
            }
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.relativePathRequired"));
            }
            var basePath = Path.GetFullPath(
                Path.Combine(
                    _manifest.DirectoryPath,
                    kind == "asset" ? "assets" : "data"));
            var fullPath = Path.GetFullPath(
                Path.Combine(basePath, relativePath));
            var prefix = basePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.pathOutsideTool"));
            }
            var tokenId = "internal-" + kind + ":" +
                          (_nextFileTokenId++)
                              .ToString(CultureInfo.InvariantCulture);
            _fileTokens[tokenId] = new FileToken
            {
                Id = tokenId,
                Kind = kind,
                Path = fullPath,
                CanRead = true,
                CanWrite = kind == "data",
                RequiresConfirmation = false,
                DisplayName = Path.GetFileName(fullPath),
            };
            return tokenId;
        }

        private FileToken RequireFileToken(string tokenId)
        {
            FileToken token;
            if (string.IsNullOrWhiteSpace(tokenId) ||
                !_fileTokens.TryGetValue(tokenId, out token))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.fileTokenInvalid"));
            }
            return token;
        }

        private string ReadFileText(string tokenId)
        {
            var token = RequireFileToken(tokenId);
            RequireReadPermission(token);
            var info = new FileInfo(token.Path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.fileMissingOrTooLarge"));
            }
            return File.ReadAllText(token.Path, Encoding.UTF8);
        }

        private string ReadFileBase64(string tokenId)
        {
            var token = RequireFileToken(tokenId);
            RequireReadPermission(token);
            var info = new FileInfo(token.Path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.fileMissingOrTooLarge"));
            }
            return Convert.ToBase64String(
                File.ReadAllBytes(token.Path));
        }

        private void WriteFileText(string tokenId, string text)
        {
            StageFileWrite(
                tokenId,
                new UTF8Encoding(false).GetBytes(text ?? string.Empty));
        }

        private void WriteFileBase64(string tokenId, string base64)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64 ?? string.Empty);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.base64Invalid"),
                    exception);
            }
            StageFileWrite(tokenId, bytes);
        }

        private void StageFileWrite(string tokenId, byte[] bytes)
        {
            if (!_executionActive || _output == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.writeOnlyDuringRun"));
            }
            var token = RequireFileToken(tokenId);
            if (!token.CanWrite)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.writeNotAllowed"));
            }
            if (!_manifest.HasPermission("files.write"))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.writePermissionRequired"));
            }
            if (!_writtenTokens.Add(token.Id))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.duplicateWrite"));
            }
            if (bytes == null || bytes.Length > MaxFileBytes)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.writeTooLarge"));
            }
            _totalWriteBytes += bytes.Length;
            if (_totalWriteBytes > MaxTotalWriteBytes)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.totalWriteTooLarge"));
            }
            _output.FileWrites.Add(
                new FalsumToolPendingFileWrite
                {
                    Path = token.Path,
                    Bytes = bytes,
                    RequiresConfirmation =
                        token.RequiresConfirmation,
                    Description = token.DisplayName,
                });
        }

        private void RequireReadPermission(FileToken token)
        {
            if (!token.CanRead)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.readNotAllowed"));
            }
            if (!_manifest.HasPermission("files.read"))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.readPermissionRequired"));
            }
        }

        private void AddEventJson(string json)
        {
            RequireExecution();
            var chartEvent = ParseEvent(json);
            _batch.Add(
                delegate(Chart chart)
                {
                    chart.Events.Add(chartEvent);
                });
        }

        private void UpdateEventJson(string key, string json)
        {
            RequireExecution();
            var record = _snapshot.FindEvent(key);
            if (record == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.eventKeyInvalid",
                        ("key", key)));
            }
            if (_removedEventKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.eventRemovedUpdate",
                        ("key", key)));
            }
            if (!_updatedEventKeys.Add(key))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.eventDuplicateUpdate"));
            }
            var patch = FalsumJson.AsObject(
                FalsumJson.Parse(json));
            ValidateEventFields(patch, false);
            _batch.Add(
                delegate
                {
                    ApplyEventPatch(record.Event, patch);
                });
        }

        private void RemoveEvent(string key)
        {
            RequireExecution();
            var record = _snapshot.FindEvent(key);
            if (record == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.eventKeyInvalid",
                        ("key", key)));
            }
            if (!_removedEventKeys.Add(key))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.eventDuplicateRemove"));
            }
            _batch.Add(
                delegate(Chart chart)
                {
                    chart.Events.Remove(record.Event);
                });
        }

        private void AddNoteJson(string json)
        {
            RequireExecution();
            var note = ParseNote(json, null);
            ChartNoteValidator.Validate(note, "Note");
            _batch.Add(
                delegate(Chart chart)
                {
                    chart.Notes.Add(note);
                });
        }

        private void UpdateNoteJson(string key, string json)
        {
            RequireExecution();
            var record = _snapshot.FindNote(key);
            if (record == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteRecordKeyInvalid",
                        ("key", key)));
            }
            if (_removedNoteKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteRemovedUpdate",
                        ("key", key)));
            }
            if (!_updatedNoteKeys.Add(key))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteDuplicateUpdate"));
            }
            var patch = FalsumJson.AsObject(
                FalsumJson.Parse(json));
            ValidateNoteFields(patch, false);
            var candidate = CloneNote(record.Note);
            ApplyNotePatch(candidate, patch);
            ChartNoteValidator.Validate(candidate, "Note");
            _batch.Add(
                delegate
                {
                    ApplyNotePatch(record.Note, patch);
                });
        }

        private void RemoveNote(string key)
        {
            RequireExecution();
            var record = _snapshot.FindNote(key);
            if (record == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteRecordKeyInvalid",
                        ("key", key)));
            }
            if (!_removedNoteKeys.Add(key))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteDuplicateRemove"));
            }
            _batch.Add(
                delegate(Chart chart)
                {
                    chart.Notes.Remove(record.Note);
                });
        }

        private void SetTimingJson(string json)
        {
            RequireExecution();
            var source = FalsumJson.AsObject(
                FalsumJson.Parse(json));
            object rawBpm;
            object rawDivision;
            object rawDelay;
            var bpm = _snapshot.Bpm;
            var division = _snapshot.Division;
            if (source.TryGetValue("bpm", out rawBpm))
            {
                bpm = ReadFiniteDouble(rawBpm, "BPM");
                if (bpm <= 0.0)
                {
                    throw new InvalidOperationException(
                        L.Get(
                            "falsum.error.tool.runtime.bpmPositive"));
                }
            }
            if (source.TryGetValue("division", out rawDivision))
            {
                division = ReadFiniteDouble(
                    rawDivision,
                    L.Get("falsum.label.division"));
                if (division <= 0.0)
                {
                    throw new InvalidOperationException(
                        L.Get(
                            "falsum.error.tool.runtime.divisionPositive"));
                }
            }
            if (source.TryGetValue("delayMs", out rawDelay))
            {
                var delay = ReadFiniteDouble(
                    rawDelay,
                    L.Get("falsum.label.delay"));
                _batch.DelayMs = delay;
            }
            _batch.Add(
                delegate(Chart chart)
                {
                    chart.Bpm = bpm;
                    chart.Division = division;
                });
        }

        private ChartEvent ParseEvent(string json)
        {
            var source = FalsumJson.AsObject(
                FalsumJson.Parse(json));
            ValidateEventFields(source, true);
            var kindText = FalsumJson.GetString(
                source,
                "kind",
                string.Empty);
            var chartEvent = new ChartEvent();
            if (kindText == "track")
            {
                chartEvent.Kind = ChartEventKind.TrackSpeed;
            }
            else if (kindText == "bpm")
            {
                chartEvent.Kind = ChartEventKind.Bpm;
            }
            else if (kindText == "lane")
            {
                chartEvent.Kind = ChartEventKind.LaneLayout;
            }
            else
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.unknownEventType",
                        ("kind", kindText)));
            }
            chartEvent.TimeMs = ReadIntegerTime(source, "timeMs");
            ApplyEventFields(
                chartEvent,
                source,
                true);
            return chartEvent;
        }

        private void ApplyEventPatch(
            ChartEvent chartEvent,
            FalsumJsonObject source)
        {
            if (source.ContainsKey("kind"))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.eventKindImmutable"));
            }
            if (source.ContainsKey("timeMs"))
            {
                chartEvent.TimeMs = ReadIntegerTime(
                    source,
                    "timeMs");
            }
            ApplyEventFields(chartEvent, source, false);
        }

        private static void ApplyEventFields(
            ChartEvent chartEvent,
            FalsumJsonObject source,
            bool required)
        {
            object value;
            if (chartEvent.Kind == ChartEventKind.TrackSpeed)
            {
                if (source.TryGetValue("multiplier", out value))
                {
                    chartEvent.Multiplier = ReadFiniteDouble(
                        value,
                        L.Get("falsum.label.multiplier"));
                }
                else if (required)
                {
                    throw new InvalidOperationException(
                        L.Get(
                            "falsum.error.tool.runtime.trackMultiplierMissing"));
                }
            }
            else if (chartEvent.Kind == ChartEventKind.Bpm)
            {
                if (required ||
                    source.TryGetValue("bpm", out value))
                {
                    if (!source.TryGetValue("bpm", out value))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.bpmMissing"));
                    }
                    chartEvent.Bpm = ReadFiniteDouble(
                        value,
                        "BPM");
                    if (chartEvent.Bpm <= 0.0)
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.bpmPositive"));
                    }
                }
                if (required ||
                    source.TryGetValue("division", out value))
                {
                    if (!source.TryGetValue("division", out value))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.divisionMissing"));
                    }
                    chartEvent.Division = ReadFiniteDouble(
                        value,
                        L.Get("falsum.label.division"));
                    if (chartEvent.Division <= 0.0)
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.divisionPositive"));
                    }
                }
            }
            else
            {
                if (required ||
                    source.TryGetValue("lane", out value))
                {
                    if (!source.TryGetValue("lane", out value))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.laneMissing"));
                    }
                    chartEvent.Lane = ReadJsonInteger(
                        value,
                        "lane");
                    if (chartEvent.Lane != 0 &&
                        chartEvent.Lane != 1 &&
                        chartEvent.Lane != 4 &&
                        chartEvent.Lane != 5)
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.laneInvalid"));
                    }
                }
                if (required ||
                    source.TryGetValue("enabled", out value))
                {
                    if (!source.TryGetValue("enabled", out value))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.laneEnabledMissing"));
                    }
                    if (!(value is bool))
                    {
                        throw new InvalidOperationException(
                            L.Get(
                                "falsum.error.tool.runtime.enabledBoolean"));
                    }
                    chartEvent.Enabled = (bool)value;
                }
            }
        }

        private ChartNote ParseNote(
            string json,
            ChartNote target)
        {
            var source = FalsumJson.AsObject(
                FalsumJson.Parse(json));
            ValidateNoteFields(source, target == null);
            var note = target ?? new ChartNote();
            if (source.ContainsKey("kind"))
            {
                note.Kind = ParseNoteKind(
                    FalsumJson.GetString(
                        source,
                        "kind",
                        string.Empty));
            }
            else if (target == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteKindMissing"));
            }
            if (target == null ||
                source.ContainsKey("timeMs"))
            {
                note.TimeMs = ReadIntegerTime(source, "timeMs");
            }
            ReadNoteInt(source, "startLane", ref note.StartLane);
            ReadNoteInt(source, "widthLanes", ref note.WidthLanes);
            ReadNoteDouble(source, "durationMs", ref note.DurationMs);
            ReadNoteDouble(source, "startPos", ref note.StartPos);
            ReadNoteDouble(source, "endPos", ref note.EndPos);
            ReadNoteDouble(source, "startWidth", ref note.StartWidth);
            ReadNoteDouble(source, "endWidth", ref note.EndWidth);
            ReadNoteInt(
                source,
                "startCellCount",
                ref note.StartCellCount);
            ReadNoteInt(
                source,
                "endCellCount",
                ref note.EndCellCount);
            ReadNoteInt(source, "leftEasing", ref note.LeftEasing);
            ReadNoteInt(source, "rightEasing", ref note.RightEasing);
            ReadNoteInt(source, "groupId", ref note.GroupId);
            ReadNoteInt(
                source,
                "flickDirection",
                ref note.FlickDirection);
            return note;
        }

        private static ChartNote CloneNote(ChartNote source)
        {
            return new ChartNote
            {
                Id = source.Id,
                SourceOrder = source.SourceOrder,
                Kind = source.Kind,
                TimeMs = source.TimeMs,
                StartLane = source.StartLane,
                WidthLanes = source.WidthLanes,
                DurationMs = source.DurationMs,
                StartPos = source.StartPos,
                EndPos = source.EndPos,
                StartWidth = source.StartWidth,
                EndWidth = source.EndWidth,
                StartCellCount = source.StartCellCount,
                EndCellCount = source.EndCellCount,
                LeftEasing = source.LeftEasing,
                RightEasing = source.RightEasing,
                GroupId = source.GroupId,
                FlickDirection = source.FlickDirection,
                Consumed = source.Consumed,
                Judged = source.Judged,
                Holding = source.Holding,
                Result = source.Result,
                HoldStartResult = source.HoldStartResult,
            };
        }

        private void ApplyNotePatch(
            ChartNote note,
            FalsumJsonObject source)
        {
            if (source.ContainsKey("kind"))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.noteKindImmutable"));
            }
            if (source.ContainsKey("timeMs"))
            {
                note.TimeMs = ReadIntegerTime(source, "timeMs");
            }
            ReadNoteInt(source, "startLane", ref note.StartLane);
            ReadNoteInt(source, "widthLanes", ref note.WidthLanes);
            ReadNoteDouble(source, "durationMs", ref note.DurationMs);
            ReadNoteDouble(source, "startPos", ref note.StartPos);
            ReadNoteDouble(source, "endPos", ref note.EndPos);
            ReadNoteDouble(source, "startWidth", ref note.StartWidth);
            ReadNoteDouble(source, "endWidth", ref note.EndWidth);
            ReadNoteInt(
                source,
                "startCellCount",
                ref note.StartCellCount);
            ReadNoteInt(
                source,
                "endCellCount",
                ref note.EndCellCount);
            ReadNoteInt(source, "leftEasing", ref note.LeftEasing);
            ReadNoteInt(source, "rightEasing", ref note.RightEasing);
            ReadNoteInt(source, "groupId", ref note.GroupId);
            ReadNoteInt(
                source,
                "flickDirection",
                ref note.FlickDirection);
        }

        private static NoteKind ParseNoteKind(string kind)
        {
            if (kind == "tap")
            {
                return NoteKind.Tap;
            }
            if (kind == "hold")
            {
                return NoteKind.Hold;
            }
            if (kind == "flick")
            {
                return NoteKind.Flick;
            }
            if (kind == "skyArea")
            {
                return NoteKind.SkyArea;
            }
            throw new InvalidOperationException(
                L.Get(
                    "falsum.error.tool.runtime.unknownNoteKind",
                    ("kind", kind)));
        }

        private static double ReadIntegerTime(
            FalsumJsonObject source,
            string key)
        {
            object raw;
            if (!source.TryGetValue(key, out raw))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.timeFieldMissing",
                        ("key", key)));
            }
            var value = ReadFiniteDouble(raw, key);
            if (value < 0.0 ||
                Math.Abs(value - Math.Round(value)) > 0.000001)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.timeInvalid"));
            }
            return Math.Round(value);
        }

        private static double ReadFiniteDouble(
            object raw,
            string label)
        {
            if (!(raw is double) &&
                !(raw is int) &&
                !(raw is long) &&
                !(raw is float) &&
                !(raw is decimal))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.numberRequired",
                        ("label", label)));
            }
            var value = Convert.ToDouble(
                raw,
                CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.finiteRequired",
                        ("label", label)));
            }
            return value;
        }

        private static void ReadNoteInt(
            FalsumJsonObject source,
            string key,
            ref int target)
        {
            object raw;
            if (source.TryGetValue(key, out raw))
            {
                target = ReadJsonInteger(raw, key);
            }
        }

        private static void ReadNoteDouble(
            FalsumJsonObject source,
            string key,
            ref double target)
        {
            object raw;
            if (source.TryGetValue(key, out raw))
            {
                target = ReadFiniteDouble(raw, key);
            }
        }

        private static void ValidateNoteFields(
            FalsumJsonObject source,
            bool allowKind)
        {
            foreach (var key in source.Keys)
            {
                if (key == "kind" && allowKind)
                {
                    continue;
                }
                if (key == "timeMs" ||
                    key == "startLane" ||
                    key == "widthLanes" ||
                    key == "durationMs" ||
                    key == "startPos" ||
                    key == "endPos" ||
                    key == "startWidth" ||
                    key == "endWidth" ||
                    key == "startCellCount" ||
                    key == "endCellCount" ||
                    key == "leftEasing" ||
                    key == "rightEasing" ||
                    key == "groupId" ||
                    key == "flickDirection")
                {
                    continue;
                }
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.unknownNoteField",
                        ("key", key)));
            }
        }

        private static void ValidateEventFields(
            FalsumJsonObject source,
            bool allowKind)
        {
            foreach (var key in source.Keys)
            {
                if (key == "kind" && allowKind)
                {
                    continue;
                }
                if (key == "timeMs" ||
                    key == "multiplier" ||
                    key == "bpm" ||
                    key == "division" ||
                    key == "lane" ||
                    key == "enabled")
                {
                    continue;
                }
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.unknownEventField",
                        ("key", key)));
            }
        }

        private static int ReadJsonInteger(
            object raw,
            string label)
        {
            var value = ReadFiniteDouble(raw, label);
            var rounded = Math.Round(
                value,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(value - rounded) > 0.000001 ||
                rounded < int.MinValue ||
                rounded > int.MaxValue)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.int32Required",
                        ("label", label)));
            }
            return (int)rounded;
        }

        private void RequireExecution()
        {
            if (!_executionActive || _batch == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.tool.runtime.editOnlyDuringRun"));
            }
        }

        private static string DescribeException(Exception exception)
        {
            var current = exception;
            while (current.InnerException != null &&
                   (current is TypeInitializationException ||
                    current is System.Reflection.TargetInvocationException))
            {
                current = current.InnerException;
            }
            return L.Get(
                "falsum.error.tool.runtime.exceptionDetail",
                ("type", current.GetType().Name),
                ("message", current.Message));
        }
    }
}

