using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using InFalsus.Gameplay.Falsum.Core;
using InFalsus.Gameplay.Falsum.Localization;
using InFalsus.Gameplay.Falsum.Runtime;
using InFalsus.Gameplay.Falsum.Tools;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public sealed class FalsumToolDialogView
    {
        private sealed class FieldBinding
        {
            public FalsumToolFieldDefinition Definition;
            public TextField Text;
            public DropdownField Select;
            public Toggle Check;
            public Button NoteButton;
            public FalsumToolFileSelection FileSelection;
            public readonly List<FalsumToolNoteReference> NoteSelection =
                new List<FalsumToolNoteReference>();
        }

        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        private readonly VisualElement _modalLayer;
        private readonly VisualTreeAsset _template;
        private readonly List<FalsumToolManifest> _tools =
            new List<FalsumToolManifest>();
        private readonly List<FieldBinding> _fields =
            new List<FieldBinding>();
        private readonly Dictionary<string, FieldBinding> _fieldsByKey =
            new Dictionary<string, FieldBinding>(StringComparer.Ordinal);

        private VisualElement _overlay;
        private VisualElement _window;
        private VisualElement _managerView;
        private VisualElement _formView;
        private ScrollView _managerContent;
        private ScrollView _formContent;
        private Label _dialogTitle;
        private Label _dialogSubtitle;
        private Label _toolCount;
        private Label _formTitle;
        private Label _formSubtitle;
        private Label _status;
        private Button _close;
        private Button _done;
        private Button _run;
        private Button _back;
        private Button _openFolder;
        private Button _reload;
        private VisualElement _timePickOverlay;
        private VisualElement _notePickOverlay;
        private Label _notePickCount;
        private Label _notePickHint;
        private Button _notePickCancel;
        private Button _notePickConfirm;
        private FieldBinding _pickingField;
        private FieldBinding _notePickField;
        private FalsumToolNotePickRequest _notePickRequest;
        private Vector2 _formScrollBeforeNotePick;
        private double _timePickCandidate;
        private bool _busy;
        private bool _closing;
        private string _currentToolId;

        public FalsumToolDialogView(
            VisualElement modalLayer,
            VisualTreeAsset template)
        {
            _modalLayer = modalLayer;
            _template = template;
            Build();
        }

        public event Action<string> ToolOpenRequested;
        public event Action ReloadRequested;
        public event Action OpenFolderRequested;
        public event Action<string, bool> ToolEnabledChanged;
        public event Action<FalsumToolRunRequest> RunRequested;
        public event Action BackRequested;
        public event Action Closed;
        public event Action TimePickStarted;
        public event Action TimePickEnded;
        public event Action<FalsumToolNotePickRequest> NotePickRequested;
        public event Action NotePickCancelled;
        public event Action NotePickConfirmed;
        public event Action<Vector2> NotePickPointerDown;

        /// <summary>
        /// Wheel steps during note selection, already converted to the
        /// editor's wheel convention: positive = forward in time (the same
        /// sign <c>Input.mouseScrollDelta</c> uses everywhere else in the
        /// chart editor). See <see cref="NormalizeWheelDelta"/>.
        /// </summary>
        public event Action<float> NotePickScrolled;

        public Func<Vector2, double?> TimeResolver;

        public bool IsOpen
        {
            get
            {
                return _overlay != null &&
                       !_overlay.ClassListContains("hidden");
            }
        }

        public bool IsTimePicking
        {
            get { return _pickingField != null; }
        }

        public bool IsNotePicking
        {
            get { return _notePickField != null; }
        }

        public void ShowManager(
            IList<FalsumToolManifest> tools)
        {
            var wasOpen = ShowOverlay();
            CancelNotePick();
            CancelTimePick();
            _managerView.RemoveFromClassList("hidden");
            _formView.AddToClassList("hidden");
            _window.RemoveFromClassList("is-tool-mode");
            _back.AddToClassList("hidden");
            _run.AddToClassList("hidden");
            _dialogTitle.text = L.Get("falsum.tools.title");
            _dialogSubtitle.text = L.Get("falsum.tools.subtitle");
            SetTools(tools);
            SetStatus(string.Empty, false);
            if (wasOpen)
            {
                FalsumUiMotion.AnimateWindowIn(_window);
            }
            else
            {
                FalsumUiMotion.AnimateModalIn(_overlay, _window);
            }
            _close.Focus();
        }

        public void ShowTool(
            FalsumToolManifest manifest,
            FalsumToolFormDefinition form)
        {
            if (manifest == null || form == null)
            {
                return;
            }
            var wasOpen = ShowOverlay();
            CancelNotePick();
            CancelTimePick();
            _managerView.AddToClassList("hidden");
            _formView.RemoveFromClassList("hidden");
            _window.AddToClassList("is-tool-mode");
            _back.RemoveFromClassList("hidden");
            _run.RemoveFromClassList("hidden");
            _run.SetEnabled(
                manifest.Enabled &&
                manifest.IsReady &&
                !_busy);
            _currentToolId = manifest.Id;
            _dialogTitle.text = manifest.DisplayName;
            _dialogSubtitle.text = L.Get(
                "falsum.tools.toolMeta",
                ("api", manifest.ApiVersion),
                ("version", manifest.Version),
                ("id", manifest.Id));
            _formTitle.text = string.IsNullOrWhiteSpace(form.Title)
                ? manifest.DisplayName
                : form.Title;
            _formSubtitle.text = string.IsNullOrWhiteSpace(
                manifest.DisplayDescription)
                ? L.Get("falsum.tools.formSubtitleFallback")
                : manifest.DisplayDescription;
            BuildForm(form);
            SetStatus(string.Empty, false);
            if (wasOpen)
            {
                FalsumUiMotion.AnimateWindowIn(_window);
            }
            else
            {
                FalsumUiMotion.AnimateModalIn(_overlay, _window);
            }
            if (_fields.Count > 0 &&
                _fields[0].NoteButton != null)
            {
                _fields[0].NoteButton.Focus();
            }
            else if (_fields.Count > 0 &&
                     _fields[0].Text != null)
            {
                _fields[0].Text.Focus();
            }
        }

        public void SetTools(
            IList<FalsumToolManifest> tools)
        {
            _tools.Clear();
            if (tools != null)
            {
                for (var i = 0; i < tools.Count; i++)
                {
                    _tools.Add(tools[i]);
                }
            }
            if (_managerContent != null)
            {
                RebuildManager();
            }
        }

        public void ShowValidationIssues(
            IList<FalsumToolValidationIssue> issues)
        {
            ClearInvalidFields();
            if (issues == null || issues.Count == 0)
            {
                return;
            }
            var firstMessage = issues[0].Message;
            for (var i = 0; i < issues.Count; i++)
            {
                FieldBinding field;
                if (!string.IsNullOrWhiteSpace(issues[i].Field) &&
                    _fieldsByKey.TryGetValue(
                        issues[i].Field,
                        out field))
                {
                    MarkInvalid(field);
                    if (i == 0)
                    {
                        firstMessage =
                            field.Definition.Label + "：" +
                            issues[i].Message;
                    }
                }
            }
            SetStatus(firstMessage, true);
        }

        public void SetStatus(
            string message,
            bool error)
        {
            if (_status == null)
            {
                return;
            }
            _status.text = message ?? string.Empty;
            _status.EnableInClassList("is-error", error);
            _status.EnableInClassList(
                "is-success",
                !error && !string.IsNullOrWhiteSpace(message));
            SetVisible(_status, !string.IsNullOrWhiteSpace(message));
            if (!string.IsNullOrWhiteSpace(message))
            {
                AnimateIn(
                    _status,
                    0.10f,
                    0.0f,
                   0.35f,
                    1.0f,
                    0.0f,
                    1.0f);
            }
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            if (_run != null)
            {
                _run.SetEnabled(!busy);
            }
            if (_done != null)
            {
                _done.SetEnabled(!busy);
            }
            if (_close != null)
            {
                _close.SetEnabled(!busy);
            }
            for (var i = 0; i < _fields.Count; i++)
            {
                if (_fields[i].NoteButton != null)
                {
                    _fields[i].NoteButton.SetEnabled(!busy);
                }
            }
        }

        public void Close()
        {
            if (!IsOpen || _closing)
            {
                return;
            }
            CancelNotePick();
            CancelTimePick();
            SetBusy(false);
            _closing = true;
            FalsumUiMotion.AnimateModalOut(
                _overlay,
                _window,
                delegate
                {
                    _overlay.AddToClassList("hidden");
                    _closing = false;
                    Raise(Closed);
                });
        }

        public void CloseImmediate()
        {
            if (!IsOpen)
            {
                return;
            }
            CancelNotePick();
            CancelTimePick();
            SetBusy(false);
            _closing = false;
            _overlay.AddToClassList("hidden");
            Raise(Closed);
        }

        public string DebugStatus
        {
            get { return _status != null ? _status.text : string.Empty; }
        }

        public int DebugToolCount
        {
            get { return _tools.Count; }
        }

        public int DebugFieldCount
        {
            get { return _fields.Count; }
        }

        public void DebugBeginFirstTimePick()
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                if (_fields[i].Definition.Type == "time" &&
                    _fields[i].Definition.ChartPick)
                {
                    BeginTimePick(_fields[i]);
                    return;
                }
            }
        }

        public void DebugCancelTimePick()
        {
            CancelTimePickSelection();
        }

        public void DebugBeginFirstNotePick()
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                if (_fields[i].Definition.IsNoteField)
                {
                    BeginNotePick(_fields[i]);
                    return;
                }
            }
        }

        public void DebugConfirmNotePick()
        {
            ConfirmNotePick();
        }

        public void DebugCancelNotePick()
        {
            CancelNotePickSelection();
        }

        public int DebugNotePickSelectionCount
        {
            get
            {
                return _notePickField != null
                    ? _notePickField.NoteSelection.Count
                    : 0;
            }
        }

        public bool DebugNotePickConfirmEnabled
        {
            get
            {
                return _notePickConfirm != null &&
                       _notePickConfirm.enabledSelf;
            }
        }

        public string DebugFirstNoteFieldSummary
        {
            get
            {
                for (var i = 0; i < _fields.Count; i++)
                {
                    if (_fields[i].Definition.IsNoteField)
                    {
                        return _fields[i].Text != null
                            ? _fields[i].Text.value
                            : string.Empty;
                    }
                }
                return string.Empty;
            }
        }

        public void CancelTimePickSelection()
        {
            CancelTimePick();
        }

        public void CancelNotePickSelection()
        {
            CancelNotePick();
        }

        public void SetNotePickCount(int count)
        {
            if (_notePickRequest == null ||
                _notePickCount == null ||
                _notePickConfirm == null)
            {
                return;
            }
            count = Math.Max(0, count);
            _notePickCount.text = L.Get(
                "falsum.tools.notePick.count",
                ("count", count));
            _notePickHint.text = _notePickRequest.MaxCount ==
                                 int.MaxValue
                ? L.Get(
                    "falsum.tools.notePick.hintUnbounded",
                    ("min", _notePickRequest.MinCount))
                : L.Get(
                    "falsum.tools.notePick.hintRange",
                    ("min", _notePickRequest.MinCount),
                    ("max", _notePickRequest.MaxCount));
            _notePickHint.RemoveFromClassList("is-error");
            _notePickConfirm.SetEnabled(
                count >= _notePickRequest.MinCount &&
                count <= _notePickRequest.MaxCount);
        }

        public void SetNotePickError(string message)
        {
            if (_notePickHint == null)
            {
                return;
            }
            _notePickHint.text = message ?? string.Empty;
            _notePickHint.AddToClassList("is-error");
        }

        public void CompleteNotePick(
            IList<FalsumToolNoteReference> notes)
        {
            if (_notePickField == null)
            {
                return;
            }
            var selection = _notePickField.NoteSelection;
            selection.Clear();
            if (notes != null)
            {
                for (var i = 0; i < notes.Count; i++)
                {
                    if (notes[i] != null)
                    {
                        selection.Add(notes[i]);
                    }
                }
            }
            UpdateNoteFieldSummary(_notePickField);
            FinishNotePick();
        }

        public void DebugSubmitCurrentForm()
        {
            SubmitRun();
        }

        private void Build()
        {
            var instance = _template.Instantiate();
            L.ApplyStaticText(instance);
            _overlay = Require<VisualElement>(
                instance,
                "ToolDialogOverlay");
            _window = Require<VisualElement>(
                instance,
                "ToolDialogWindow");
            _dialogTitle = Require<Label>(
                instance,
                "ToolDialogTitle");
            _dialogSubtitle = Require<Label>(
                instance,
                "ToolDialogSubtitle");
            _toolCount = Require<Label>(
                instance,
                "ToolCount");
            _managerView = Require<VisualElement>(
                instance,
                "ToolManagerView");
            _formView = Require<VisualElement>(
                instance,
                "ToolFormView");
            _managerContent = Require<ScrollView>(
                instance,
                "ToolManagerContent");
            _formContent = Require<ScrollView>(
                instance,
                "ToolFormContent");
            _formTitle = Require<Label>(
                instance,
                "ToolFormTitle");
            _formSubtitle = Require<Label>(
                instance,
                "ToolFormSubtitle");
            _status = Require<Label>(
                instance,
                "ToolStatus");
            _close = Require<Button>(
                instance,
                "ToolDialogClose");
            _done = Require<Button>(
                instance,
                "ToolDialogDone");
            _run = Require<Button>(instance, "ToolRun");
            _back = Require<Button>(instance, "ToolBack");
            _openFolder = Require<Button>(
                instance,
                "OpenToolsFolder");
            _reload = Require<Button>(
                instance,
                "ReloadTools");
            L.Bind(_openFolder, "falsum.tools.openFolder");
            L.Bind(_reload, "falsum.tools.reload");
            L.Bind(_back, "falsum.tools.back");
            L.Bind(_run, "falsum.tools.run");
            L.Bind(_done, "falsum.common.close");
            L.BindTooltip(
                _close,
                "falsum.tools.closeTooltip");
            _timePickOverlay = Require<VisualElement>(
                instance,
                "ToolTimePickOverlay");
            _timePickOverlay.focusable = true;
            _notePickOverlay = Require<VisualElement>(
                instance,
                "ToolNotePickOverlay");
            _notePickCount = Require<Label>(
                instance,
                "ToolNotePickCount");
            _notePickHint = Require<Label>(
                instance,
                "ToolNotePickHint");
            _notePickCancel = Require<Button>(
                instance,
                "ToolNotePickCancel");
            _notePickConfirm = Require<Button>(
                instance,
                "ToolNotePickConfirm");
            _notePickOverlay.focusable = true;
            L.Bind(_notePickCancel, "falsum.common.cancel");
            L.Bind(_notePickConfirm, "falsum.common.confirm");
            _overlay.focusable = true;
            _managerContent.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _managerContent.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _formContent.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _formContent.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _modalLayer.Add(_overlay);
            _close.clicked += Close;
            _done.clicked += Close;
            _run.clicked += SubmitRun;
            _back.clicked += delegate
            {
                CancelTimePick();
                Raise(BackRequested);
            };
            _openFolder.clicked += delegate
            {
                Raise(OpenFolderRequested);
            };
            _reload.clicked += delegate
            {
                Raise(ReloadRequested);
            };
            _overlay.RegisterCallback<KeyDownEvent>(
                OnKeyDown,
                TrickleDown.TrickleDown);
            _overlay.RegisterCallback<PointerDownEvent>(
                OnOverlayPointerDown,
                TrickleDown.TrickleDown);
            _timePickOverlay.RegisterCallback<PointerMoveEvent>(
                OnTimePickMove);
            _timePickOverlay.RegisterCallback<PointerDownEvent>(
                OnTimePickPointerDown);
            _notePickCancel.clicked += CancelNotePick;
            _notePickConfirm.clicked += ConfirmNotePick;
            _notePickOverlay.RegisterCallback<PointerDownEvent>(
                OnNotePickPointerDown,
                TrickleDown.TrickleDown);
            _notePickOverlay.RegisterCallback<WheelEvent>(
                OnNotePickWheel);
            _overlay.AddToClassList("hidden");
        }

        private bool ShowOverlay()
        {
            var wasOpen = IsOpen;
            _overlay.RemoveFromClassList("hidden");
            _overlay.BringToFront();
            return wasOpen;
        }

        private void RebuildManager()
        {
            _managerContent.Clear();
            _toolCount.text = L.Get(
                "falsum.tools.toolCount",
                ("count", _tools.Count));
            if (_tools.Count == 0)
            {
                var empty = new Label(
                    L.Get("falsum.tools.empty"))
                {
                    pickingMode = PickingMode.Ignore,
                };
                empty.AddToClassList("tool-form-empty");
                _managerContent.Add(empty);
                return;
            }

            for (var i = 0; i < _tools.Count; i++)
            {
                var card = BuildToolCard(_tools[i]);
                _managerContent.Add(card);
                AnimateIn(
                    card,
                    0.12f,
                    i * 18.0f,
                    0.0f,
                    0.992f,
                    10.0f,
                    _tools[i].Enabled ? 1.0f : 0.68f);
            }
        }

        private VisualElement BuildToolCard(
            FalsumToolManifest manifest)
        {
            var card = new VisualElement
            {
                pickingMode = PickingMode.Position,
            };
            card.AddToClassList("tool-manager-card");
            card.EnableInClassList(
                "is-ready",
                manifest.Enabled && manifest.IsReady);
            card.EnableInClassList(
                "is-disabled",
                !manifest.Enabled);
            card.EnableInClassList(
                "is-error",
                !manifest.IsReady);

            var head = new VisualElement();
            head.AddToClassList("tool-card-head");
            var title = new Label(manifest.DisplayName)
            {
                pickingMode = PickingMode.Ignore,
            };
            title.AddToClassList("tool-card-title");
            head.Add(title);
            var badge = new Label(
                !manifest.IsReady
                    ? L.Get("falsum.common.error")
                    : manifest.Enabled
                        ? L.Get("falsum.tools.status.ready")
                        : L.Get("falsum.tools.status.disabled"))
            {
                pickingMode = PickingMode.Ignore,
            };
            badge.AddToClassList("tool-card-badge");
            badge.EnableInClassList(
                "ready",
                manifest.Enabled && manifest.IsReady);
            badge.EnableInClassList("error", !manifest.IsReady);
            head.Add(badge);
            card.Add(head);

            var meta = new Label(
                L.Get(
                    "falsum.tools.cardMeta",
                    ("version", manifest.Version),
                    ("id", manifest.Id),
                    ("permissions", manifest.PermissionText)))
            {
                pickingMode = PickingMode.Ignore,
            };
            meta.AddToClassList("tool-card-meta");
            card.Add(meta);

            var description = new Label(
                string.IsNullOrWhiteSpace(manifest.DisplayDescription)
                    ? L.Get("falsum.tools.noDescription")
                    : manifest.DisplayDescription)
            {
                pickingMode = PickingMode.Ignore,
            };
            description.AddToClassList("tool-card-description");
            card.Add(description);
            if (!manifest.IsReady)
            {
                var error = new Label(manifest.LoadError)
                {
                    pickingMode = PickingMode.Ignore,
                };
                error.AddToClassList("tool-card-error");
                card.Add(error);
            }

            var actions = new VisualElement();
            actions.AddToClassList("tool-card-actions");
            var enabled = new Toggle
            {
                value = manifest.Enabled,
            };
            enabled.AddToClassList("tool-card-toggle");
            var enableRow = new VisualElement();
            enableRow.AddToClassList("tool-card-enable");
            enableRow.Add(enabled);
            var enableLabel = new Label(
                L.Get("falsum.tools.enabled"));
            enableLabel.AddToClassList("tool-card-enable-label");
            enableRow.Add(enableLabel);
            var capturedId = manifest.Id;
            enabled.RegisterValueChangedCallback(
                evt => Raise(
                    ToolEnabledChanged,
                    capturedId,
                    evt.newValue));
            actions.Add(enableRow);

            var open = new Button(
                delegate { Raise(ToolOpenRequested, capturedId); })
            {
                text = L.Get("falsum.tools.open"),
            };
            open.AddToClassList("tool-card-button");
            open.SetEnabled(manifest.Enabled && manifest.IsReady);
            actions.Add(open);
            card.Add(actions);
            return card;
        }

        private void BuildForm(FalsumToolFormDefinition form)
        {
            _formContent.Clear();
            _fields.Clear();
            _fieldsByKey.Clear();
            if (form.Fields.Count == 0)
            {
                var empty = new Label(
                    L.Get("falsum.tools.noFields"))
                {
                    pickingMode = PickingMode.Ignore,
                };
                empty.AddToClassList("tool-form-empty");
                _formContent.Add(empty);
                return;
            }
            for (var i = 0; i < form.Fields.Count; i++)
            {
                var row = BuildFieldRow(form.Fields[i]);
                _formContent.Add(row);
                AnimateIn(
                    row,
                    0.10f,
                    i * 12.0f,
                    0.0f,
                    1.0f,
                    8.0f,
                    1.0f);
            }
        }

        private static void AnimateIn(
            VisualElement element,
            float duration,
            float delayMs,
            float fromOpacity,
            float fromScale,
            float fromOffsetX,
            float targetOpacity)
        {
            if (element == null)
            {
                return;
            }
            var start =
                Time.unscaledTimeAsDouble + delayMs / 1000.0f;
            element.style.opacity = fromOpacity;
            element.style.scale =
                new Scale(new Vector2(fromScale, fromScale));
            element.style.translate =
                new StyleTranslate(
                    new Translate(fromOffsetX, 0.0f));
            IVisualElementScheduledItem animation = null;
            animation = element.schedule.Execute(
                    (Action)delegate
                    {
                        var now = Time.unscaledTimeAsDouble;
                        if (now < start)
                        {
                            return;
                        }
                        var progress = Mathf.Clamp01(
                            (float)((now - start) /
                                    Math.Max(0.001f, duration)));
                        var eased = 1.0f -
                                    Mathf.Pow(1.0f - progress, 3.0f);
                        element.style.opacity =
                            Mathf.LerpUnclamped(
                                fromOpacity,
                                targetOpacity,
                                eased);
                        var scale = Mathf.LerpUnclamped(
                            fromScale,
                            1.0f,
                            eased);
                        element.style.scale =
                            new Scale(new Vector2(scale, scale));
                        element.style.translate =
                            new StyleTranslate(
                                new Translate(
                                    Mathf.LerpUnclamped(
                                        fromOffsetX,
                                        0.0f,
                                        eased),
                                    0.0f));
                        if (progress < 1.0f)
                        {
                            return;
                        }
                        element.style.opacity = targetOpacity;
                        element.style.scale =
                            new Scale(Vector2.one);
                        element.style.translate =
                            new StyleTranslate(
                                new Translate(0.0f, 0.0f));
                        if (animation != null)
                        {
                            animation.Pause();
                        }
                    })
                .Every(16)
                .StartingIn((long)Mathf.Max(0.0f, delayMs));
        }

        private VisualElement BuildFieldRow(
            FalsumToolFieldDefinition definition)
        {
            var row = new VisualElement();
            row.AddToClassList("tool-form-row");
            var copy = new VisualElement();
            copy.AddToClassList("tool-form-copy");
            var label = new Label(
                definition.Label + (definition.Required ? " *" : ""))
            {
                pickingMode = PickingMode.Ignore,
            };
            label.AddToClassList("tool-form-label");
            copy.Add(label);
            if (!string.IsNullOrWhiteSpace(
                    definition.Description))
            {
                var description = new Label(
                    definition.Description)
                {
                    pickingMode = PickingMode.Ignore,
                };
                description.AddToClassList(
                    "tool-form-description");
                copy.Add(description);
            }
            row.Add(copy);

            var control = new VisualElement();
            control.AddToClassList("tool-form-control");
            var binding = new FieldBinding
            {
                Definition = definition,
            };
            if (definition.Type == "select")
            {
                var choices = new List<string>();
                var index = 0;
                var selected = Convert.ToString(
                    definition.DefaultValue,
                    Invariant);
                for (var i = 0; i < definition.Options.Count; i++)
                {
                    choices.Add(definition.Options[i].Label);
                    if (string.Equals(
                            definition.Options[i].Value,
                            selected,
                            StringComparison.Ordinal))
                    {
                        index = i;
                    }
                }
                binding.Select = new DropdownField(
                    choices,
                    Mathf.Clamp(index, 0, Math.Max(0, choices.Count - 1)));
                binding.Select.AddToClassList("tool-form-field");
                control.Add(binding.Select);
            }
            else if (definition.Type == "checkbox")
            {
                binding.Check = new Toggle(
                    L.Get("falsum.tools.enabled"))
                {
                    value = Convert.ToBoolean(
                        definition.DefaultValue ?? false,
                        Invariant),
                };
                binding.Check.AddToClassList("tool-form-check");
                control.Add(binding.Check);
            }
            else if (definition.IsNoteField)
            {
                binding.Text = new TextField
                {
                    value = FormatNoteSelection(
                        binding.NoteSelection),
                };
                binding.Text.AddToClassList("tool-form-field");
                binding.Text.SetEnabled(false);
                binding.NoteButton = new Button(
                    delegate { BeginNotePick(binding); })
                {
                    text = L.Get("falsum.tools.notePick.choose"),
                };
                binding.NoteButton.AddToClassList("tool-form-pick");
                control.Add(binding.Text);
                control.Add(binding.NoteButton);
            }
            else
            {
                binding.Text = new TextField
                {
                    value = FormatDefault(definition),
                };
                binding.Text.AddToClassList("tool-form-field");
                binding.Text.maxLength = 4096;
                if (definition.Type == "number" ||
                    definition.Type == "integer" ||
                    definition.Type == "time")
                {
                    var input = binding.Text;
                    input.RegisterValueChangedCallback(
                        evt =>
                        {
                            var sanitized =
                                FalsumEditorView
                                    .SanitizeNumericInput(
                                        evt.newValue);
                            if (!string.Equals(
                                    input.value,
                                    sanitized,
                                    StringComparison.Ordinal))
                            {
                                input.SetValueWithoutNotify(
                                    sanitized);
                            }
                        });
                }
                if (definition.Type == "file")
                {
                    binding.Text.SetEnabled(false);
                    var fileButton = new Button(
                        delegate { ChooseFile(binding); })
                    {
                        text = definition.FileMode == "save"
                            ? L.Get("falsum.tools.chooseOutput")
                            : L.Get("falsum.tools.chooseFile"),
                    };
                    fileButton.AddToClassList("tool-form-pick");
                    control.Add(binding.Text);
                    control.Add(fileButton);
                }
                else
                {
                    control.Add(binding.Text);
                    if (definition.Type == "time" &&
                        definition.ChartPick)
                    {
                        var pick = new Button(
                            delegate { BeginTimePick(binding); })
                        {
                            text = L.Get("falsum.tools.pickFromChart"),
                        };
                        pick.AddToClassList("tool-form-pick");
                        control.Add(pick);
                    }
                }
            }
            row.Add(control);
            _fields.Add(binding);
            _fieldsByKey[definition.Key] = binding;
            return row;
        }

        private static string FormatDefault(
            FalsumToolFieldDefinition definition)
        {
            if (definition.DefaultValue == null)
            {
                return string.Empty;
            }
            if (definition.DefaultValue is double)
            {
                return ((double)definition.DefaultValue).ToString(
                    "0.######",
                    Invariant);
            }
            if (definition.DefaultValue is float)
            {
                return ((float)definition.DefaultValue).ToString(
                    "0.######",
                    Invariant);
            }
            return Convert.ToString(
                definition.DefaultValue,
                Invariant);
        }

        private void ChooseFile(FieldBinding binding)
        {
            var definition = binding.Definition;
            var path = definition.FileMode == "save"
                ? FalsumFileDialog.SaveToolFile(
                    definition.DefaultFileName,
                    definition.Extensions)
                : FalsumFileDialog.OpenToolFile(
                    definition.Extensions);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            binding.FileSelection = new FalsumToolFileSelection
            {
                FieldKey = definition.Key,
                Path = path,
                Save = definition.FileMode == "save",
            };
            binding.Text.SetValueWithoutNotify(
                Path.GetFileName(path));
            binding.Text.tooltip = path;
        }

        private void SubmitRun()
        {
            if (_busy)
            {
                return;
            }
            ClearInvalidFields();
            var request = new FalsumToolRunRequest
            {
                ToolId = _currentToolId,
            };
            var error = string.Empty;
            FieldBinding invalid = null;
            for (var i = 0; i < _fields.Count; i++)
            {
                var field = _fields[i];
                if (!ReadField(field, request, out error))
                {
                    invalid = field;
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                if (invalid != null)
                {
                    MarkInvalid(invalid);
                }
                SetStatus(error, true);
                return;
            }
            Raise(RunRequested, request);
        }

        private bool ReadField(
            FieldBinding binding,
            FalsumToolRunRequest request,
            out string error)
        {
            error = string.Empty;
            var definition = binding.Definition;
            if (binding.Check != null)
            {
                request.Values[definition.Key] = binding.Check.value;
                return true;
            }
            if (binding.Select != null)
            {
                var index = binding.Select.index;
                if (index < 0 ||
                    index >= definition.Options.Count)
                {
                    if (definition.Required)
                    {
                        error = L.Get(
                            "falsum.error.tool.fieldSelectRequired",
                            ("field", definition.Label));
                        return false;
                    }
                    request.Values[definition.Key] = null;
                    return true;
                }
                request.Values[definition.Key] =
                    definition.Options[index].Value;
                return true;
            }
            if (definition.IsNoteField)
            {
                var selection = binding.NoteSelection;
                if (selection.Count <
                    definition.EffectiveNoteMinimum ||
                    selection.Count >
                    definition.EffectiveNoteMaximum)
                {
                    error = L.Get(
                        "falsum.error.tool.noteSelectionCount",
                        ("field", definition.Label),
                        ("min", definition.EffectiveNoteMinimum),
                        ("max",
                            definition.EffectiveNoteMaximum ==
                            int.MaxValue
                                ? L.Get(
                                    "falsum.tools.notePick.unbounded")
                                : definition
                                    .EffectiveNoteMaximum
                                    .ToString(Invariant)));
                    return false;
                }

                var keys = new HashSet<string>(
                    StringComparer.Ordinal);
                var values = new List<object>(selection.Count);
                for (var i = 0; i < selection.Count; i++)
                {
                    var note = selection[i];
                    if (note == null ||
                        string.IsNullOrWhiteSpace(note.Key) ||
                        !keys.Add(note.Key) ||
                        !definition.AllowsNoteKind(note.Kind))
                    {
                        error = L.Get(
                            "falsum.error.tool.noteSelectionInvalid",
                            ("field", definition.Label));
                        return false;
                    }
                    values.Add(note.ToJsonObject());
                }
                request.Values[definition.Key] = values;
                return true;
            }
            if (definition.Type == "file")
            {
                if (binding.FileSelection == null)
                {
                    if (definition.Required)
                    {
                        error = L.Get(
                            "falsum.error.tool.fieldFileRequired",
                            ("field", definition.Label));
                        return false;
                    }
                    request.Values[definition.Key] = null;
                    return true;
                }
                request.Values[definition.Key] =
                    new FalsumJsonObject
                    {
                        {
                            "token",
                            "external:" + definition.Key
                        },
                        {
                            "name",
                            Path.GetFileName(
                                binding.FileSelection.Path)
                        },
                    };
                request.Files[definition.Key] =
                    binding.FileSelection;
                return true;
            }

            var text = binding.Text != null
                ? binding.Text.value ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                if (definition.Required)
                {
                    error = L.Get(
                        "falsum.error.tool.fieldRequired",
                        ("field", definition.Label));
                    return false;
                }
                request.Values[definition.Key] = null;
                return true;
            }
            if (definition.Type == "number" ||
                definition.Type == "integer" ||
                definition.Type == "time")
            {
                double number;
                if (!double.TryParse(
                        text,
                        NumberStyles.Float,
                        Invariant,
                        out number) ||
                    double.IsNaN(number) ||
                    double.IsInfinity(number))
                {
                    error = L.Get(
                        "falsum.error.tool.fieldNumberInvalid",
                        ("field", definition.Label));
                    return false;
                }
                if (definition.Min.HasValue &&
                    number < definition.Min.Value)
                {
                    error = L.Get(
                        "falsum.error.tool.fieldBelowMin",
                        ("field", definition.Label),
                        ("min", definition.Min.Value.ToString(
                            "0.######",
                            Invariant)));
                    return false;
                }
                if (definition.Max.HasValue &&
                    number > definition.Max.Value)
                {
                    error = L.Get(
                        "falsum.error.tool.fieldAboveMax",
                        ("field", definition.Label),
                        ("max", definition.Max.Value.ToString(
                            "0.######",
                            Invariant)));
                    return false;
                }
                if (definition.Type == "integer" ||
                    definition.Type == "time")
                {
                    if (Math.Abs(
                            number - Math.Round(number)) >
                        0.000001)
                    {
                        error = L.Get(
                            "falsum.error.tool.fieldIntegerInvalid",
                            ("field", definition.Label));
                        return false;
                    }
                    if (definition.Type == "time" && number < 0.0)
                    {
                        error = L.Get(
                            "falsum.error.tool.fieldNegative",
                            ("field", definition.Label));
                        return false;
                    }
                    request.Values[definition.Key] =
                        (int)Math.Round(number);
                }
                else
                {
                    request.Values[definition.Key] = number;
                }
                return true;
            }
            request.Values[definition.Key] = text;
            return true;
        }

        private void BeginNotePick(FieldBinding binding)
        {
            if (_notePickField != null ||
                binding == null ||
                !binding.Definition.IsNoteField)
            {
                return;
            }
            var handler = NotePickRequested;
            if (handler == null)
            {
                SetStatus(
                    L.Get(
                        "falsum.error.tool.notePickUnavailable"),
                    true);
                return;
            }

            var request = new FalsumToolNotePickRequest
            {
                FieldKey = binding.Definition.Key,
                Multiple = binding.Definition.Multiple,
                MinCount =
                    binding.Definition.EffectiveNoteMinimum,
                MaxCount =
                    binding.Definition.EffectiveNoteMaximum,
            };
            for (var i = 0;
                 i < binding.Definition.NoteKinds.Count;
                 i++)
            {
                request.NoteKinds.Add(
                    binding.Definition.NoteKinds[i]);
            }
            for (var i = 0; i < binding.NoteSelection.Count; i++)
            {
                request.SelectedNotes.Add(
                    binding.NoteSelection[i]);
            }

            _notePickField = binding;
            _notePickRequest = request;
            _formScrollBeforeNotePick = _formContent.scrollOffset;
            SetNotePickCount(binding.NoteSelection.Count);
            _window.AddToClassList("hidden");
            _overlay.AddToClassList("is-note-picking");
            _overlay.style.backgroundColor = Color.clear;
            _notePickOverlay.RemoveFromClassList("hidden");
            _notePickOverlay.BringToFront();
            _notePickOverlay.Focus();
            handler(request);
        }

        private void OnNotePickPointerDown(
            PointerDownEvent evt)
        {
            if (_notePickField == null)
            {
                return;
            }
            if (evt.target != _notePickOverlay)
            {
                return;
            }
            if (evt.button == 1)
            {
                evt.StopPropagation();
                CancelNotePick();
                return;
            }
            if (evt.button != 0)
            {
                return;
            }
            evt.StopPropagation();
            var handler = NotePickPointerDown;
            if (handler != null)
            {
                handler(Input.mousePosition);
            }
        }

        /// <summary>
        /// Converts UI Toolkit's wheel delta into the editor's wheel
        /// convention.
        ///
        /// These two signs are OPPOSITE, and that is not an accident:
        /// <c>Input.mouseScrollDelta.y</c> is POSITIVE when the wheel is
        /// rolled UP (away from the user), while <c>WheelEvent.delta.y</c>
        /// is POSITIVE when the wheel is rolled DOWN. Unity documents the
        /// split only implicitly -- its own uGUI -> UI Toolkit bridge
        /// negates the value explicitly (`PanelEventHandler.OnScroll`:
        /// `uitkScrollDelta.y = -uitkScrollDelta.y`), and UITK's own
        /// `ScrollView` treats a positive delta as "scroll down".
        ///
        /// The chart editor everywhere else reads the legacy sign
        /// (`FalsumChartEditor.HandleKeyboard`), so any UITK wheel that is
        /// forwarded into timeline stepping has to be negated here, once,
        /// at the boundary. Skipping this made the note picker scroll the
        /// timeline BACKWARDS while the chart behind it scrolled forwards.
        /// </summary>
        public static float NormalizeWheelDelta(float uitkDeltaY)
        {
            return -uitkDeltaY;
        }

        private void OnNotePickWheel(WheelEvent evt)
        {
            if (_notePickField == null)
            {
                return;
            }
            evt.StopPropagation();
            var handler = NotePickScrolled;
            if (handler != null)
            {
                handler(NormalizeWheelDelta(evt.delta.y));
            }
        }

        private void ConfirmNotePick()
        {
            if (_notePickField == null ||
                _notePickConfirm == null ||
                !_notePickConfirm.enabledSelf)
            {
                return;
            }
            var handler = NotePickConfirmed;
            if (handler == null)
            {
                SetStatus(
                    L.Get(
                        "falsum.error.tool.notePickUnavailable"),
                    true);
                return;
            }
            handler();
        }

        private void CancelNotePick()
        {
            if (_notePickField == null)
            {
                return;
            }
            FinishNotePick();
            var handler = NotePickCancelled;
            if (handler != null)
            {
                handler();
            }
        }

        private void FinishNotePick()
        {
            var field = _notePickField;
            _notePickField = null;
            _notePickRequest = null;
            if (_notePickOverlay != null)
            {
                _notePickOverlay.AddToClassList("hidden");
            }
            if (_overlay != null)
            {
                _overlay.RemoveFromClassList("is-note-picking");
                _overlay.style.backgroundColor =
                    FalsumUiMotion.DefaultBackdrop;
            }
            if (_window != null)
            {
                _window.RemoveFromClassList("hidden");
            }
            if (_formContent != null)
            {
                _formContent.scrollOffset =
                    _formScrollBeforeNotePick;
            }
            if (field != null)
            {
                if (field.NoteButton != null)
                {
                    field.NoteButton.Focus();
                }
                else if (field.Text != null)
                {
                    field.Text.Focus();
                }
            }
        }

        private static void UpdateNoteFieldSummary(
            FieldBinding field)
        {
            if (field != null && field.Text != null)
            {
                field.Text.SetValueWithoutNotify(
                    FormatNoteSelection(field.NoteSelection));
            }
        }

        private static string FormatNoteSelection(
            IList<FalsumToolNoteReference> notes)
        {
            if (notes == null || notes.Count == 0)
            {
                return string.Empty;
            }
            var parts = new List<string>();
            var shown = Math.Min(3, notes.Count);
            for (var i = 0; i < shown; i++)
            {
                var note = notes[i];
                if (note == null)
                {
                    continue;
                }
                parts.Add(
                    L.Get(
                        "falsum.tools.notePick.summary",
                        ("kind", NoteKindDisplayName(note.Kind)),
                        ("id", note.Id),
                        ("time",
                            note.TimeMs.ToString(
                                "0.###",
                                Invariant))));
            }
            if (notes.Count > shown)
            {
                parts.Add(
                    L.Get(
                        "falsum.tools.notePick.more",
                        ("count", notes.Count - shown)));
            }
            return string.Join(", ", parts.ToArray());
        }

        private static string NoteKindDisplayName(string kind)
        {
            switch (kind)
            {
                case "tap":
                    return L.Get("falsum.noteKind.tap");
                case "hold":
                    return L.Get("falsum.noteKind.hold");
                case "flick":
                    return L.Get("falsum.noteKind.flick");
                case "skyArea":
                    return L.Get("falsum.noteKind.skyArea");
                default:
                    return kind ?? string.Empty;
            }
        }

        private void BeginTimePick(FieldBinding binding)
        {
            if (binding == null || TimeResolver == null)
            {
                SetStatus(
                    L.Get("falsum.error.tool.chartTimePickUnavailable"),
                    true);
                return;
            }
            _pickingField = binding;
            double current;
            if (!double.TryParse(
                    binding.Text.value,
                    NumberStyles.Float,
                    Invariant,
                    out current))
            {
                current = 0.0;
            }
            _timePickCandidate = Math.Max(0.0, Math.Round(current));
            _window.AddToClassList("hidden");
            _overlay.AddToClassList("is-time-picking");
            _overlay.style.backgroundColor = Color.clear;
            _timePickOverlay.RemoveFromClassList("hidden");
            _timePickOverlay.BringToFront();
            _timePickOverlay.Focus();
            Raise(TimePickStarted);
        }

        private void OnTimePickMove(PointerMoveEvent evt)
        {
            if (_pickingField == null || TimeResolver == null)
            {
                return;
            }
            var resolved = TimeResolver(Input.mousePosition);
            if (!resolved.HasValue)
            {
                return;
            }
            _timePickCandidate = Math.Max(
                0.0,
                Math.Round(resolved.Value));
        }

        private void OnTimePickPointerDown(PointerDownEvent evt)
        {
            if (_pickingField == null)
            {
                return;
            }
            if (evt.button == 1)
            {
                evt.StopPropagation();
                CancelTimePick();
                return;
            }
            if (evt.button != 0)
            {
                return;
            }
            evt.StopPropagation();
            if (TimeResolver == null)
            {
                CancelTimePick();
                return;
            }
            var resolved = TimeResolver(Input.mousePosition);
            if (resolved.HasValue)
            {
                _timePickCandidate = Math.Max(
                    0.0,
                    Math.Round(resolved.Value));
            }
            _pickingField.Text.SetValueWithoutNotify(
                ((long)_timePickCandidate).ToString(Invariant));
            EndTimePick();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_notePickField != null)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopImmediatePropagation();
                    CancelNotePick();
                }
                return;
            }
            if (_pickingField != null)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopImmediatePropagation();
                    CancelTimePick();
                }
                return;
            }
            if (evt.keyCode == KeyCode.Escape &&
                !_busy)
            {
                evt.StopImmediatePropagation();
                Close();
            }
        }

        private void OnOverlayPointerDown(PointerDownEvent evt)
        {
            if (_busy ||
                _pickingField != null ||
                _notePickField != null)
            {
                return;
            }
            if (evt.target == _overlay)
            {
                Close();
            }
        }

        private void CancelTimePick()
        {
            if (_pickingField == null)
            {
                if (_timePickOverlay != null)
                {
                    _timePickOverlay.AddToClassList("hidden");
                }
                _overlay.RemoveFromClassList("is-time-picking");
                _overlay.style.backgroundColor =
                    FalsumUiMotion.DefaultBackdrop;
                return;
            }
            EndTimePick();
        }

        private void EndTimePick()
        {
            if (_pickingField == null)
            {
                return;
            }
            _pickingField = null;
            _timePickOverlay.AddToClassList("hidden");
            _overlay.RemoveFromClassList("is-time-picking");
            _overlay.style.backgroundColor =
                FalsumUiMotion.DefaultBackdrop;
            _window.RemoveFromClassList("hidden");
            Raise(TimePickEnded);
        }

        private void MarkInvalid(FieldBinding field)
        {
            if (field == null)
            {
                return;
            }
            if (field.Text != null)
            {
                field.Text.AddToClassList("is-invalid");
            }
            if (field.Select != null)
            {
                field.Select.AddToClassList("is-invalid");
            }
        }

        private void ClearInvalidFields()
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                if (_fields[i].Text != null)
                {
                    _fields[i].Text.RemoveFromClassList("is-invalid");
                }
                if (_fields[i].Select != null)
                {
                    _fields[i].Select.RemoveFromClassList(
                        "is-invalid");
                }
            }
        }

        private static T Require<T>(
            VisualElement root,
            string name)
            where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException(
                    L.Get(
                        "falsum.error.ui.missingToolElement",
                        ("element", name)));
            }
            return element;
        }

        private static void SetVisible(
            VisualElement element,
            bool visible)
        {
            if (element == null)
            {
                return;
            }
            element.EnableInClassList("hidden", !visible);
            element.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private static void Raise(Action action)
        {
            if (action != null)
            {
                action();
            }
        }

        private static void Raise<T>(
            Action<T> action,
            T value)
        {
            if (action != null)
            {
                action(value);
            }
        }

        private static void Raise<T1, T2>(
            Action<T1, T2> action,
            T1 value1,
            T2 value2)
        {
            if (action != null)
            {
                action(value1, value2);
            }
        }
    }
}

