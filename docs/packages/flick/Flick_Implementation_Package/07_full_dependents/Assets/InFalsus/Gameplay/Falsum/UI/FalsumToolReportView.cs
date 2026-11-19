using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Falsum.Localization;
using InFalsus.Gameplay.Falsum.Tools;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public sealed class FalsumToolReportView
    {
        private static readonly CultureInfo Invariant =
            CultureInfo.InvariantCulture;

        private readonly VisualElement _modalLayer;
        private readonly VisualTreeAsset _template;
        private VisualElement _overlay;
        private VisualElement _window;
        private VisualElement _head;
        private Label _title;
        private Label _subtitle;
        private Label _count;
        private Label _summary;
        private ScrollView _items;
        private Label _empty;
        private Label _footerNote;
        private Button _close;
        private Button _done;
        private FalsumToolReport _report;
        private bool _closing;
        private bool _floating;
        private bool _dragging;
        private int _dragPointerId = -1;
        private Vector2 _dragPointerOffset;

        public FalsumToolReportView(
            VisualElement modalLayer,
            VisualTreeAsset template)
        {
            _modalLayer = modalLayer;
            _template = template;
            Build();
        }

        public event Action<FalsumToolReportItem>
            LocateRequested;

        public bool IsOpen
        {
            get
            {
                return _overlay != null &&
                       !_overlay.ClassListContains("hidden");
            }
        }

        public bool IsFloating
        {
            get { return _floating; }
        }

        public bool ContainsElement(VisualElement element)
        {
            return IsWithinElement(element, _window);
        }

        public int DebugItemCount
        {
            get
            {
                return _report != null
                    ? _report.Items.Count
                    : 0;
            }
        }

        public void Show(FalsumToolReport report)
        {
            if (report == null)
            {
                return;
            }
            var wasOpen = IsOpen;
            var wasFloating = _floating;
            ResetFloatingState();
            _report = report;
            _title.text = string.IsNullOrWhiteSpace(report.Title)
                ? L.Get("falsum.tools.reportTitle")
                : report.Title;
            _subtitle.text = string.IsNullOrWhiteSpace(
                    report.SourceToolName)
                ? L.Get("falsum.tools.report.userResult")
                : L.Get(
                    "falsum.tools.report.generatedBy",
                    ("tool", report.SourceToolName));
            _summary.text = report.Summary ?? string.Empty;
            _count.text = report.Items.Count == 0
                ? L.Get("falsum.tools.report.passed")
                : L.Get(
                    "falsum.tools.report.itemCount",
                    ("count", report.Items.Count));
            _empty.text = string.IsNullOrWhiteSpace(
                    report.EmptyText)
                ? L.Get("falsum.tools.report.noIssuesReturned")
                : report.EmptyText;
            _footerNote.text = report.Items.Count == 0
                ? L.Get("falsum.tools.report.noIssuesFound")
                : L.Get("falsum.tools.reportFooter");
            RebuildItems(report);
            SetVisible(_items, report.Items.Count > 0);
            SetVisible(_empty, report.Items.Count == 0);
            _overlay.RemoveFromClassList("hidden");
            _overlay.BringToFront();
            if (wasOpen && !wasFloating)
            {
                FalsumUiMotion.AnimateWindowIn(_window);
            }
            else
            {
                FalsumUiMotion.AnimateModalIn(
                    _overlay,
                    _window);
            }
            if (report.Items.Count > 0)
            {
                var first = _items.Q<Button>(
                    "ToolReportLocate0");
                if (first != null)
                {
                    first.Focus();
                    return;
                }
            }
            _done.Focus();
        }

        public void Close()
        {
            CloseInternal();
        }

        public void CloseImmediate()
        {
            if (_overlay == null)
            {
                return;
            }
            _closing = false;
            CompleteClose();
        }

        public bool DebugRequestFirstLocate()
        {
            if (_report == null)
            {
                return false;
            }
            for (var i = 0; i < _report.Items.Count; i++)
            {
                if (_report.Items[i].CanLocate)
                {
                    return RequestLocate(_report.Items[i]);
                }
            }
            return false;
        }

        public bool DebugRequestLastLocate()
        {
            if (_report == null)
            {
                return false;
            }
            for (var i = _report.Items.Count - 1; i >= 0; i--)
            {
                if (_report.Items[i].CanLocate)
                {
                    return RequestLocate(_report.Items[i]);
                }
            }
            return false;
        }

        public bool DebugRequestLocateByTitle(
            string title)
        {
            if (_report == null)
            {
                return false;
            }
            for (var i = 0; i < _report.Items.Count; i++)
            {
                var item = _report.Items[i];
                if (item.CanLocate &&
                    string.Equals(
                        item.Title,
                        title,
                        StringComparison.Ordinal))
                {
                    return RequestLocate(item);
                }
            }
            return false;
        }

        public void DebugScrollItemsToBottom()
        {
            if (_items != null)
            {
                _items.scrollOffset =
                    new Vector2(0.0f, 100000.0f);
            }
        }

        private void Build()
        {
            var instance = _template.Instantiate();
            L.ApplyStaticText(instance);
            _overlay = Require<VisualElement>(
                instance,
                "ToolReportOverlay");
            _window = Require<VisualElement>(
                instance,
                "ToolReportWindow");
            _head = Require<VisualElement>(
                instance,
                "ToolReportHead");
            _title = Require<Label>(
                instance,
                "ToolReportTitle");
            _subtitle = Require<Label>(
                instance,
                "ToolReportSubtitle");
            _count = Require<Label>(
                instance,
                "ToolReportCount");
            _summary = Require<Label>(
                instance,
                "ToolReportSummary");
            _items = Require<ScrollView>(
                instance,
                "ToolReportItems");
            _empty = Require<Label>(
                instance,
                "ToolReportEmpty");
            _footerNote = Require<Label>(
                instance,
                "ToolReportFooterNote");
            _close = Require<Button>(
                instance,
                "ToolReportCloseButton");
            _done = Require<Button>(
                instance,
                "ToolReportDone");
            L.Bind(_title, "falsum.tools.reportCheck");
            L.Bind(_footerNote, "falsum.tools.reportFooter");
            L.Bind(_done, "falsum.tools.reportDone");
            L.BindTooltip(
                _close,
                "falsum.tools.reportCloseTooltip");
            _items.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            _items.verticalScrollerVisibility =
                ScrollerVisibility.Auto;
            _head.tooltip = L.Get(
                "falsum.tools.report.dragHint");
            _overlay.focusable = true;
            _modalLayer.Add(_overlay);
            _close.clicked += Close;
            _done.clicked += Close;
            _overlay.RegisterCallback<KeyDownEvent>(
                OnKeyDown,
                TrickleDown.TrickleDown);
            _head.RegisterCallback<PointerDownEvent>(
                OnFloatingHeaderPointerDown);
            _head.RegisterCallback<PointerMoveEvent>(
                OnFloatingHeaderPointerMove);
            _head.RegisterCallback<PointerUpEvent>(
                OnFloatingHeaderPointerUp);
            _head.RegisterCallback<PointerCaptureOutEvent>(
                OnFloatingHeaderCaptureOut);
            _overlay.AddToClassList("hidden");
        }

        private void RebuildItems(FalsumToolReport report)
        {
            _items.Clear();
            for (var i = 0; i < report.Items.Count; i++)
            {
                _items.Add(CreateItem(
                    report.Items[i],
                    i));
            }
        }

        private VisualElement CreateItem(
            FalsumToolReportItem item,
            int index)
        {
            var row = new VisualElement();
            row.AddToClassList("tool-report-item");
            row.AddToClassList(
                "is-" + (item.Severity ?? "error"));

            var number = new Label(
                (index + 1).ToString(
                    "00",
                    Invariant));
            number.AddToClassList("tool-report-number");
            row.Add(number);

            var copy = new VisualElement();
            copy.AddToClassList("tool-report-item-copy");
            var heading = new VisualElement();
            heading.AddToClassList(
                "tool-report-item-heading");
            var itemTitle = new Label(item.Title);
            itemTitle.AddToClassList(
                "tool-report-item-title");
            heading.Add(itemTitle);
            var severity = new Label(
                SeverityLabel(item.Severity));
            severity.AddToClassList(
                "tool-report-severity");
            severity.AddToClassList(
                "is-" + (item.Severity ?? "error"));
            heading.Add(severity);
            copy.Add(heading);

            var message = new Label(item.Message);
            message.AddToClassList(
                "tool-report-item-message");
            copy.Add(message);

            var meta = new Label(
                BuildMeta(item));
            meta.AddToClassList("tool-report-item-meta");
            copy.Add(meta);
            row.Add(copy);

            if (item.CanLocate)
            {
                var captured = item;
                var locate = new Button
                {
                    name = "ToolReportLocate" +
                           index.ToString(Invariant),
                    text = L.Get("falsum.tools.report.locate"),
                };
                locate.AddToClassList(
                    "tool-report-locate");
                locate.clicked += delegate
                {
                    RequestLocate(captured);
                };
                row.Add(locate);
            }
            return row;
        }

        private bool RequestLocate(
            FalsumToolReportItem item)
        {
            if (_closing ||
                _overlay == null ||
                item == null)
            {
                return false;
            }
            EnterFloatingMode();
            var locate = LocateRequested;
            if (locate != null)
            {
                locate(item);
            }
            return true;
        }

        private void EnterFloatingMode()
        {
            if (_floating ||
                _overlay == null ||
                _window == null)
            {
                return;
            }
            _floating = true;
            _overlay.AddToClassList("is-floating");
            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.backgroundColor = Color.clear;
            _window.pickingMode = PickingMode.Position;
            if (_footerNote != null)
            {
                _footerNote.text = string.Empty;
            }

            SetFloatingWindowPosition(
                16.0f,
                74.0f,
                420.0f,
                540.0f);
            FalsumUiMotion.AnimateWindowIn(
                _window,
                0.14f,
                0.0f,
                0.97f);
        }

        private void CloseInternal()
        {
            if (_closing || _overlay == null)
            {
                return;
            }
            _closing = true;
            if (_floating)
            {
                CompleteClose();
                return;
            }
            FalsumUiMotion.AnimateModalOut(
                _overlay,
                _window,
                CompleteClose);
        }

        private void CompleteClose()
        {
            if (_overlay != null)
            {
                _overlay.AddToClassList("hidden");
            }
            if (_items != null)
            {
                _items.Clear();
            }
            _report = null;
            _closing = false;
            ResetFloatingState();
        }

        private void ResetFloatingState()
        {
            var pointerId = _dragPointerId;
            _dragging = false;
            _dragPointerId = -1;
            if (_head != null && pointerId >= 0)
            {
                PointerCaptureHelper.ReleasePointer(
                    _head,
                    pointerId);
            }
            _floating = false;
            if (_overlay != null)
            {
                _overlay.RemoveFromClassList("is-floating");
                _overlay.pickingMode = PickingMode.Position;
            }
            if (_window != null)
            {
                _window.pickingMode = PickingMode.Position;
                _window.style.left = 0.0f;
                _window.style.top = 0.0f;
            }
        }

        private void OnFloatingHeaderPointerDown(
            PointerDownEvent evt)
        {
            if (!_floating ||
                evt.button != 0 ||
                IsWithinElement(
                    evt.target as VisualElement,
                    _close))
            {
                return;
            }
            var pointer = _overlay.WorldToLocal(
                (Vector2)evt.position);
            _dragPointerOffset = new Vector2(
                pointer.x - _window.layout.x,
                pointer.y - _window.layout.y);
            _dragging = true;
            _dragPointerId = evt.pointerId;
            PointerCaptureHelper.CapturePointer(
                _head,
                evt.pointerId);
            evt.StopPropagation();
        }

        private void OnFloatingHeaderPointerMove(
            PointerMoveEvent evt)
        {
            if (!_dragging ||
                evt.pointerId != _dragPointerId)
            {
                return;
            }
            var pointer = _overlay.WorldToLocal(
                (Vector2)evt.position);
            SetFloatingWindowPosition(
                pointer.x - _dragPointerOffset.x,
                pointer.y - _dragPointerOffset.y,
                420.0f,
                540.0f);
            evt.StopPropagation();
        }

        private void OnFloatingHeaderPointerUp(
            PointerUpEvent evt)
        {
            if (!_dragging ||
                evt.pointerId != _dragPointerId)
            {
                return;
            }
            ResetDrag();
            evt.StopPropagation();
        }

        private void OnFloatingHeaderCaptureOut(
            PointerCaptureOutEvent evt)
        {
            _dragging = false;
            _dragPointerId = -1;
        }

        private void ResetDrag()
        {
            var pointerId = _dragPointerId;
            _dragging = false;
            _dragPointerId = -1;
            if (_head != null && pointerId >= 0)
            {
                PointerCaptureHelper.ReleasePointer(
                    _head,
                    pointerId);
            }
        }

        private void SetFloatingWindowPosition(
            float left,
            float top,
            float width,
            float height)
        {
            if (_overlay == null || _window == null)
            {
                return;
            }
            var maxLeft = Mathf.Max(
                16.0f,
                _overlay.layout.width - width - 16.0f);
            var maxTop = Mathf.Max(
                16.0f,
                _overlay.layout.height - height - 16.0f);
            _window.style.left = Mathf.Clamp(
                left,
                16.0f,
                maxLeft);
            _window.style.top = Mathf.Clamp(
                top,
                16.0f,
                maxTop);
        }

        private static bool IsWithinElement(
            VisualElement element,
            VisualElement ancestor)
        {
            if (element == null || ancestor == null)
            {
                return false;
            }
            for (var current = element;
                 current != null;
                 current = current.parent)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopImmediatePropagation();
                Close();
                return;
            }
            if (evt.keyCode != KeyCode.Tab)
            {
                return;
            }
            var candidates = new List<VisualElement>();
            _overlay.Query<VisualElement>().ForEach(
                element =>
                {
                    if (element.focusable &&
                        element.enabledSelf &&
                        element.resolvedStyle.display !=
                        DisplayStyle.None)
                    {
                        candidates.Add(element);
                    }
                });
            if (candidates.Count == 0)
            {
                evt.StopImmediatePropagation();
                return;
            }
            evt.StopImmediatePropagation();
            var current = _overlay.panel != null
                ? _overlay.panel.focusController
                    .focusedElement as VisualElement
                : null;
            var currentIndex = candidates.IndexOf(current);
            var direction =
                evt.shiftKey ? -1 : 1;
            var nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + direction +
                   candidates.Count) % candidates.Count;
            candidates[nextIndex].Focus();
        }

        private static string BuildMeta(
            FalsumToolReportItem item)
        {
            var time = FormatTime(item.TimeMs);
            if (item.TargetNotes.Count == 0)
            {
                return time;
            }
            var separator = L.Get(
                "falsum.tools.report.metaSeparator");
            var notes = new List<string>();
            var count = Math.Min(4, item.TargetNotes.Count);
            for (var i = 0; i < count; i++)
            {
                var note = item.TargetNotes[i];
                if (note.Kind == NoteKind.Tap ||
                    note.Kind == NoteKind.Hold)
                {
                    var laneRange =
                        note.StartLane.ToString(Invariant);
                    if (Mathf.Max(1, note.WidthLanes) > 1)
                    {
                        laneRange = L.Get(
                            "falsum.tools.report.range",
                            ("start", note.StartLane),
                            ("end", note.StartLane +
                             Mathf.Max(1, note.WidthLanes) - 1));
                    }
                    notes.Add(L.Get(
                        "falsum.tools.report.noteLane",
                        ("kind", NoteKindLabel(note.Kind)),
                        ("id", note.Id),
                        ("range", laneRange)));
                }
                else if (note.Kind == NoteKind.SkyArea)
                {
                    notes.Add(L.Get(
                        "falsum.tools.report.noteSky",
                        ("kind", NoteKindLabel(note.Kind)),
                        ("id", note.Id),
                        ("start", FormatSpan(
                            note.StartPos,
                            note.StartWidth)),
                        ("end", FormatSpan(
                            note.EndPos,
                            note.EndWidth))));
                }
            }
            if (item.TargetNotes.Count > count)
            {
                notes.Add(L.Get(
                    "falsum.tools.report.moreNotes",
                    ("count", item.TargetNotes.Count - count)));
            }
            return L.Get(
                "falsum.tools.report.meta",
                ("time", time),
                ("separator", separator),
                ("notes", string.Join(
                    separator,
                    notes.ToArray())));
        }

        private static string FormatSpan(
            double center,
            double width)
        {
            var halfWidth = Math.Max(0.0, width) * 0.5;
            return L.Get(
                "falsum.tools.report.span",
                ("start", (center - halfWidth).ToString(
                    "0.000",
                    Invariant)),
                ("end", (center + halfWidth).ToString(
                    "0.000",
                    Invariant)));
        }

        private static string FormatTime(double timeMs)
        {
            var total = Math.Max(
                0L,
                (long)Math.Round(timeMs));
            return L.Get(
                "falsum.tools.report.time",
                ("value", total.ToString(Invariant)));
        }

        private static string NoteKindLabel(NoteKind kind)
        {
            switch (kind)
            {
                case NoteKind.Hold:
                    return L.Get("falsum.noteKind.hold");
                case NoteKind.Flick:
                    return L.Get("falsum.noteKind.flick");
                case NoteKind.SkyArea:
                    return L.Get("falsum.noteKind.skyArea");
                default:
                    return L.Get("falsum.noteKind.tap");
            }
        }

        private static string SeverityLabel(string severity)
        {
            if (severity == "warning")
            {
                return L.Get("falsum.common.warning");
            }
            if (severity == "info")
            {
                return L.Get("falsum.common.info");
            }
            return L.Get("falsum.common.error");
        }

        private static void SetVisible(
            VisualElement element,
            bool visible)
        {
            if (element != null)
            {
                element.EnableInClassList(
                    "hidden",
                    !visible);
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
                        "falsum.error.ui.missingToolReportElement",
                        ("element", name)));
            }
            return element;
        }
    }
}
