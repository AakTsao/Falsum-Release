using System;
using InFalsus.Gameplay.Falsum.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.UI
{
    public sealed class FalsumPerformanceWindow : IDisposable
    {
        private sealed class PerformanceGraph : VisualElement
        {
            private static readonly Color FrameColor =
                new Color(0.31f, 0.56f, 0.85f, 1.0f);
            private static readonly Color CpuColor =
                new Color(0.79f, 0.60f, 0.35f, 1.0f);
            private static readonly Color GpuColor =
                new Color(0.45f, 0.71f, 0.54f, 1.0f);
            private static readonly Color BudgetColor =
                new Color(0.79f, 0.42f, 0.40f, 0.75f);
            private FalsumPerformanceMonitor _monitor;

            public PerformanceGraph()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            public void SetMonitor(FalsumPerformanceMonitor monitor)
            {
                _monitor = monitor;
                MarkDirtyRepaint();
            }

            public void Refresh()
            {
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                var rect = contentRect;
                if (_monitor == null ||
                    rect.width < 2.0f ||
                    rect.height < 2.0f)
                {
                    return;
                }
                var count = _monitor.HistoryCount;
                if (count == 0)
                {
                    return;
                }

                var maxMs = 33.3f;
                for (var i = 0; i < count; i++)
                {
                    maxMs = Mathf.Max(
                        maxMs,
                        _monitor.GetHistory(
                            FalsumPerformanceSeries.FrameTime,
                            i));
                }
                maxMs = Mathf.Clamp(
                    Mathf.Max(
                        maxMs * 1.08f,
                        _monitor.FrameBudgetMs * 2.0f),
                    33.3f,
                    120.0f);

                var painter = context.painter2D;
                DrawBudgetLine(
                    painter,
                    rect,
                    maxMs,
                    _monitor.FrameBudgetMs);
                DrawSeries(
                    painter,
                    rect,
                    maxMs,
                    FalsumPerformanceSeries.FrameTime,
                    new Color(
                        FrameColor.r,
                        FrameColor.g,
                        FrameColor.b,
                        0.11f),
                    FrameColor,
                    true);
                DrawSeries(
                    painter,
                    rect,
                    maxMs,
                    FalsumPerformanceSeries.CpuMainThread,
                    Color.clear,
                    CpuColor,
                    false);
                DrawSeries(
                    painter,
                    rect,
                    maxMs,
                    FalsumPerformanceSeries.Gpu,
                    Color.clear,
                    GpuColor,
                    false);
            }

            private static void DrawBudgetLine(
                Painter2D painter,
                Rect rect,
                float maxMs,
                float budgetMs)
            {
                var y = ToY(rect, maxMs, budgetMs);
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.xMin, y));
                painter.LineTo(new Vector2(rect.xMax, y));
                painter.strokeColor = BudgetColor;
                painter.lineWidth = 1.0f;
                painter.Stroke();
            }

            private void DrawSeries(
                Painter2D painter,
                Rect rect,
                float maxMs,
                FalsumPerformanceSeries series,
                Color fillColor,
                Color lineColor,
                bool fill)
            {
                var count = _monitor.HistoryCount;
                if (count <= 0)
                {
                    return;
                }

                if (fill && fillColor.a > 0.0f)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(
                        ToX(rect, 0),
                        rect.yMax));
                    for (var i = 0; i < count; i++)
                    {
                        painter.LineTo(new Vector2(
                            ToX(rect, i),
                            ToY(
                                rect,
                                maxMs,
                                _monitor.GetHistory(series, i))));
                    }
                    painter.LineTo(new Vector2(
                        ToX(rect, count - 1),
                        rect.yMax));
                    painter.ClosePath();
                    painter.fillColor = fillColor;
                    painter.Fill();
                }

                painter.BeginPath();
                for (var i = 0; i < count; i++)
                {
                    var point = new Vector2(
                        ToX(rect, i),
                        ToY(
                            rect,
                            maxMs,
                            _monitor.GetHistory(series, i)));
                    if (i == 0)
                    {
                        painter.MoveTo(point);
                    }
                    else
                    {
                        painter.LineTo(point);
                    }
                }
                painter.strokeColor = lineColor;
                painter.lineWidth = series ==
                    FalsumPerformanceSeries.FrameTime
                    ? 1.6f
                    : 1.0f;
                painter.Stroke();
            }

            private static float ToX(Rect rect, int index)
            {
                var denominator = Mathf.Max(
                    1,
                    FalsumPerformanceMonitor.HistoryCapacity - 1);
                return Mathf.Lerp(
                    rect.xMin,
                    rect.xMax,
                    index / (float)denominator);
            }

            private static float ToY(
                Rect rect,
                float maxMs,
                float valueMs)
            {
                var normalized = Mathf.Clamp01(
                    valueMs / Mathf.Max(0.001f, maxMs));
                return Mathf.Lerp(
                    rect.yMax,
                    rect.yMin,
                    normalized);
            }
        }

        private readonly VisualElement _overlay;
        private readonly VisualElement _panel;
        private readonly VisualElement _header;
        private readonly Label _samplingState;
        private readonly Label _fpsValue;
        private readonly Label _fpsDetail;
        private readonly Label _frameValue;
        private readonly Label _frameDetail;
        private readonly Label _budgetValue;
        private readonly Label _budgetDetail;
        private readonly Label _cpuValue;
        private readonly Label _cpuDetail;
        private readonly Label _gpuValue;
        private readonly Label _gpuDetail;
        private readonly VisualElement _cpuFill;
        private readonly VisualElement _gpuFill;
        private readonly Label _graphStats;
        private readonly PerformanceGraph _graph;
        private readonly Label _bottleneckTitle;
        private readonly Label _bottleneckDetail;
        private readonly Label _peakLabel;
        private readonly Label _overlayMetric;
        private readonly Label _refreshMetric;
        private readonly Label _viewSyncMetric;
        private readonly Label _viewTickMetric;
        private readonly Label _allocationMetric;
        private readonly Label _gcMetric;
        private readonly Label _contextLabel;
        private readonly Label _deviceLabel;
        private readonly Button _pauseButton;
        private readonly Button _budgetButton;

        private FalsumPerformanceMonitor _monitor;
        private bool _open;
        private bool _dragging;
        private int _dragPointerId = -1;
        private Vector2 _dragPointerOffset;
        private float _nextRefreshAt;

        public FalsumPerformanceWindow(VisualElement root)
        {
            _overlay = new VisualElement
            {
                name = "FalsumPerformanceOverlay",
                pickingMode = PickingMode.Ignore,
            };
            _overlay.AddToClassList("performance-overlay");
            _overlay.AddToClassList("hidden");
            root.Add(_overlay);

            _panel = new VisualElement
            {
                name = "FalsumPerformancePanel",
                pickingMode = PickingMode.Position,
            };
            _panel.AddToClassList("performance-panel");
            _overlay.Add(_panel);

            _header = new VisualElement();
            _header.AddToClassList("performance-head");
            _header.RegisterCallback<PointerDownEvent>(
                OnHeaderPointerDown);
            _header.RegisterCallback<PointerMoveEvent>(
                OnHeaderPointerMove);
            _header.RegisterCallback<PointerUpEvent>(
                OnHeaderPointerUp);
            _header.RegisterCallback<PointerCaptureOutEvent>(
                OnHeaderCaptureOut);
            _panel.Add(_header);

            var headCopy = new VisualElement();
            headCopy.AddToClassList("performance-head-copy");
            var eyebrow = new Label(
                L.Get("falsum.performance.eyebrow"));
            eyebrow.AddToClassList("performance-eyebrow");
            headCopy.Add(eyebrow);
            var titleRow = new VisualElement();
            titleRow.AddToClassList("performance-title-row");
            var title = new Label(
                L.Get("falsum.performance.windowTitle"));
            title.AddToClassList("performance-title");
            titleRow.Add(title);
            _samplingState = new Label(
                L.Get("falsum.performance.waiting"));
            _samplingState.AddToClassList("performance-sampling-state");
            titleRow.Add(_samplingState);
            headCopy.Add(titleRow);
            _header.Add(headCopy);

            var close = new Button
            {
                name = "FalsumPerformanceClose",
                text = "×",
                tooltip = L.Get("falsum.performance.closeTooltip"),
            };
            close.AddToClassList("performance-close");
            close.clicked += Close;
            _header.Add(close);

            var contentScroll = new ScrollView(
                ScrollViewMode.Vertical);
            contentScroll.AddToClassList("performance-content");
            contentScroll.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            contentScroll.verticalScrollerVisibility =
                ScrollerVisibility.Auto;
            _panel.Add(contentScroll);

            var summary = new VisualElement();
            summary.AddToClassList("performance-summary");
            contentScroll.Add(summary);
            summary.Add(CreateValueCell(
                "falsum.performance.caption.fps",
                out _fpsValue,
                out _fpsDetail));
            summary.Add(CreateValueCell(
                "falsum.performance.caption.frame",
                out _frameValue,
                out _frameDetail));
            summary.Add(CreateValueCell(
                "falsum.performance.caption.budget",
                out _budgetValue,
                out _budgetDetail));
            summary[summary.childCount - 1].AddToClassList("is-last");

            var meters = new VisualElement();
            meters.AddToClassList("performance-meters");
            contentScroll.Add(meters);
            meters.Add(CreateMeter(
                L.Get("falsum.performance.caption.cpu"),
                true,
                out _cpuValue,
                out _cpuDetail,
                out _cpuFill));
            meters.Add(CreateMeter(
                L.Get("falsum.performance.caption.gpu"),
                false,
                out _gpuValue,
                out _gpuDetail,
                out _gpuFill));
            meters[meters.childCount - 1].AddToClassList("is-last");

            var graphHead = new VisualElement();
            graphHead.AddToClassList("performance-section-head");
            contentScroll.Add(graphHead);
            graphHead.Add(CreateSectionLabel(
                L.Get("falsum.performance.graphSection")));
            _graphStats = new Label(
                L.Get("falsum.performance.waiting"));
            _graphStats.AddToClassList("performance-graph-stats");
            graphHead.Add(_graphStats);

            _graph = new PerformanceGraph();
            _graph.AddToClassList("performance-graph");
            contentScroll.Add(_graph);

            var legend = new VisualElement();
            legend.AddToClassList("performance-legend");
            contentScroll.Add(legend);
            legend.Add(CreateLegend(
                L.Get("falsum.performance.legend.frame"),
                new Color(0.31f, 0.56f, 0.85f, 1.0f)));
            legend.Add(CreateLegend(
                L.Get("falsum.performance.legend.cpuMain"),
                new Color(0.79f, 0.60f, 0.35f, 1.0f)));
            legend.Add(CreateLegend(
                L.Get("falsum.performance.legend.gpu"),
                new Color(0.45f, 0.71f, 0.54f, 1.0f)));
            legend.Add(CreateLegend(
                L.Get("falsum.performance.legend.budget"),
                new Color(0.79f, 0.42f, 0.40f, 1.0f)));

            var bottleneck = new VisualElement();
            bottleneck.AddToClassList("performance-bottleneck");
            contentScroll.Add(bottleneck);
            _bottleneckTitle = new Label(
                L.Get("falsum.performance.waiting"));
            _bottleneckTitle.AddToClassList(
                "performance-bottleneck-title");
            bottleneck.Add(_bottleneckTitle);
            _bottleneckDetail = new Label(
                L.Get("falsum.performance.openHint"));
            _bottleneckDetail.AddToClassList(
                "performance-bottleneck-detail");
            bottleneck.Add(_bottleneckDetail);
            _peakLabel = new Label(
                L.Get("falsum.performance.peakEmpty"));
            _peakLabel.AddToClassList("performance-peak");
            bottleneck.Add(_peakLabel);

            var breakdown = new VisualElement();
            breakdown.AddToClassList("performance-breakdown");
            contentScroll.Add(breakdown);
            breakdown.Add(CreateBreakdownRow(
                L.Get("falsum.performance.trackOverlay"),
                out _overlayMetric));
            breakdown.Add(CreateBreakdownRow(
                L.Get("falsum.performance.editorRefresh"),
                out _refreshMetric));
            breakdown.Add(CreateBreakdownRow(
                L.Get("falsum.performance.uiSync"),
                out _viewSyncMetric));
            breakdown.Add(CreateBreakdownRow(
                L.Get("falsum.performance.uiToolkit"),
                out _viewTickMetric));
            breakdown.Add(CreateBreakdownRow(
                L.Get("falsum.performance.scriptAllocation"),
                out _allocationMetric));
            breakdown.Add(CreateBreakdownRow(
                L.Get("falsum.performance.caption.gc"),
                out _gcMetric));

            var footer = new VisualElement();
            footer.AddToClassList("performance-footer");
            contentScroll.Add(footer);
            _contextLabel = new Label(
                L.Get("falsum.performance.noChartData"));
            _contextLabel.AddToClassList("performance-context");
            footer.Add(_contextLabel);
            _deviceLabel = new Label(string.Empty);
            _deviceLabel.AddToClassList("performance-device");
            footer.Add(_deviceLabel);

            var actions = new VisualElement();
            actions.AddToClassList("performance-actions");
            _panel.Add(actions);
            _pauseButton = new Button
            {
                text = L.Get("falsum.performance.pauseSampling"),
            };
            _pauseButton.AddToClassList("performance-action");
            _pauseButton.clicked += TogglePause;
            actions.Add(_pauseButton);
            _budgetButton = new Button
            {
                text = L.Get(
                    "falsum.performance.budget",
                    ("fps", 60)),
            };
            _budgetButton.AddToClassList("performance-action");
            _budgetButton.clicked += CycleBudget;
            actions.Add(_budgetButton);
            var reset = new Button
            {
                text = L.Get("falsum.performance.reset"),
            };
            reset.AddToClassList("performance-action");
            reset.clicked += ResetHistory;
            actions.Add(reset);
        }

        public bool IsOpen
        {
            get { return _open; }
        }

        public void SetMonitor(FalsumPerformanceMonitor monitor)
        {
            _monitor = monitor;
            _graph.SetMonitor(monitor);
            UpdateView();
        }

        public void Toggle()
        {
            if (_open)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            if (_open || _monitor == null)
            {
                return;
            }
            _open = true;
            _overlay.RemoveFromClassList("hidden");
            _monitor.SetEnabled(true);
            _nextRefreshAt = 0.0f;
            UpdateView();
        }

        public void Close()
        {
            if (!_open)
            {
                return;
            }
            _open = false;
            ResetDrag();
            _overlay.AddToClassList("hidden");
            if (_monitor != null)
            {
                _monitor.SetEnabled(false);
            }
        }

        public void Tick()
        {
            if (!_open)
            {
                return;
            }
            var now = Time.unscaledTime;
            if (now < _nextRefreshAt)
            {
                return;
            }
            _nextRefreshAt = now + 0.1f;
            UpdateView();
        }

        public void Dispose()
        {
            Close();
            if (_overlay.parent != null)
            {
                _overlay.RemoveFromHierarchy();
            }
            _monitor = null;
            _graph.SetMonitor(null);
        }

        private void UpdateView()
        {
            if (_monitor == null)
            {
                return;
            }

            _samplingState.text = _monitor.Paused
                ? L.Get("falsum.performance.samplePaused")
                : _monitor.Enabled
                    ? L.Get("falsum.performance.sampleLive")
                    : L.Get("falsum.performance.sampleStopped");
            _samplingState.EnableInClassList(
                "is-paused",
                _monitor.Paused);
            _pauseButton.text = _monitor.Paused
                ? L.Get("falsum.performance.continueSampling")
                : L.Get("falsum.performance.pauseSampling");
            _budgetButton.text = L.Get(
                "falsum.performance.budget",
                ("fps", _monitor.TargetFps));

            _fpsValue.text = _monitor.Fps > 0.0f
                ? _monitor.Fps.ToString("0")
                : "—";
            _fpsDetail.text = L.Get(
                "falsum.performance.averageFrame",
                ("value", _monitor.SmoothedFrameMs.ToString("0.0")));

            _frameValue.text = L.Get(
                "falsum.performance.milliseconds",
                ("value", _monitor.FrameMs.ToString("0.0")));
            _frameDetail.text = L.Get(
                "falsum.performance.scriptFrame",
                ("value", _monitor.ScriptFrameMs.ToString("0.0")));

            _budgetValue.text = L.Get(
                "falsum.performance.milliseconds",
                ("value", _monitor.FrameBudgetMs.ToString("0.0")));
            var framePercent =
                _monitor.FrameMs /
                Mathf.Max(0.001f, _monitor.FrameBudgetMs) *
                100.0f;
            _budgetDetail.text = L.Get(
                "falsum.performance.budgetUsed",
                ("percent", framePercent.ToString("0")));
            SetStateClass(_budgetValue, framePercent);

            var cpuWorkMs = Mathf.Max(
                _monitor.CpuMainThreadMs,
                _monitor.CpuRenderThreadMs);
            var cpuPercent = cpuWorkMs /
                             Mathf.Max(
                                 0.001f,
                                 _monitor.FrameBudgetMs) *
                             100.0f;
            _cpuValue.text =
                cpuWorkMs > 0.0f
                    ? L.Get(
                        "falsum.performance.percent",
                        ("value", cpuPercent.ToString("0")))
                    : L.Get("falsum.common.notAvailable");
            _cpuDetail.text = L.Get(
                "falsum.performance.cpuDetail",
                ("main", _monitor.CpuMainThreadMs.ToString("0.0")),
                ("render", _monitor.CpuRenderThreadMs.ToString("0.0")));
            SetMeter(_cpuFill, cpuPercent);
            SetStateClass(_cpuValue, cpuPercent);

            var gpuPercent = _monitor.GpuTimingAvailable
                ? _monitor.GpuMs /
                  Mathf.Max(0.001f, _monitor.FrameBudgetMs) *
                  100.0f
                : 0.0f;
            _gpuValue.text = _monitor.GpuTimingAvailable
                ? L.Get(
                    "falsum.performance.percent",
                    ("value", gpuPercent.ToString("0")))
                : L.Get("falsum.common.notAvailable");
            _gpuDetail.text = _monitor.GpuTimingAvailable
                ? L.Get(
                    "falsum.performance.gpuValue",
                    ("value", _monitor.GpuMs.ToString("0.0")))
                : L.Get("falsum.performance.gpuUnavailable");
            SetMeter(_gpuFill, gpuPercent);
            SetStateClass(_gpuValue, gpuPercent);
            _gpuFill.EnableInClassList(
                "is-unavailable",
                !_monitor.GpuTimingAvailable);

            _graphStats.text = L.Get(
                "falsum.performance.graphStats",
                ("p95", _monitor.GetPercentile95Ms().ToString("0.0")),
                ("stutter", _monitor.GetStutterFrameCount()),
                ("total", _monitor.HistoryCount));
            _graph.Refresh();

            _bottleneckTitle.text = _monitor.Bottleneck;
            _bottleneckDetail.text =
                _monitor.BottleneckDetail;
            _panel.EnableInClassList(
                "is-stuttering",
                _monitor.FrameMs >
                _monitor.FrameBudgetMs * 1.15f);
            _peakLabel.text = _monitor.WorstFrameMs > 0.0f
                ? L.Get(
                    "falsum.performance.peak",
                    ("milliseconds", _monitor.WorstFrameMs.ToString("0.0")),
                    ("seconds", _monitor.WorstFrameAgeSeconds.ToString("0.0")),
                    ("bottleneck", _monitor.WorstFrameBottleneck))
                : L.Get("falsum.performance.peakEmpty");

            _overlayMetric.text = L.Get(
                "falsum.performance.milliseconds",
                ("value", _monitor.OverlayMs.ToString("0.00")));
            _refreshMetric.text = L.Get(
                "falsum.performance.milliseconds",
                ("value", _monitor.EditorRefreshMs.ToString("0.00")));
            _viewSyncMetric.text = L.Get(
                "falsum.performance.milliseconds",
                ("value", _monitor.ViewSyncMs.ToString("0.00")));
            _viewTickMetric.text = L.Get(
                "falsum.performance.milliseconds",
                ("value", _monitor.ViewTickMs.ToString("0.00")));
            _allocationMetric.text = L.Get(
                "falsum.performance.kilobytes",
                ("value", _monitor.ManagedAllocationKb.ToString("0.0")));
            _gcMetric.text = L.Get(
                "falsum.performance.gcCount",
                ("count", _monitor.GcCollections));

            _contextLabel.text = L.Get(
                "falsum.performance.sceneContext",
                ("notes", _monitor.NoteCount),
                ("events", _monitor.EventCount),
                ("vertices", _monitor.GridVertexCount));
            _deviceLabel.text = _monitor.DeviceSummary;
        }

        private static VisualElement CreateValueCell(
            string labelKey,
            out Label value,
            out Label detail)
        {
            var cell = new VisualElement();
            cell.AddToClassList("performance-value-cell");
            var caption = new Label(L.Get(labelKey));
            caption.AddToClassList("performance-caption");
            cell.Add(caption);
            value = new Label("—");
            value.AddToClassList("performance-value");
            cell.Add(value);
            detail = new Label(string.Empty);
            detail.AddToClassList("performance-detail");
            cell.Add(detail);
            return cell;
        }

        private static VisualElement CreateMeter(
            string label,
            bool isCpu,
            out Label value,
            out Label detail,
            out VisualElement fill)
        {
            var meter = new VisualElement();
            meter.AddToClassList("performance-meter");
            meter.AddToClassList(
                isCpu ? "is-cpu" : "is-gpu");
            var head = new VisualElement();
            head.AddToClassList("performance-meter-head");
            meter.Add(head);
            var caption = new Label(label);
            caption.AddToClassList("performance-caption");
            head.Add(caption);
            value = new Label(L.Get("falsum.common.notAvailable"));
            value.AddToClassList("performance-meter-value");
            head.Add(value);

            var track = new VisualElement();
            track.AddToClassList("performance-meter-track");
            meter.Add(track);
            fill = new VisualElement();
            fill.AddToClassList("performance-meter-fill");
            fill.AddToClassList(
                isCpu ? "is-cpu" : "is-gpu");
            track.Add(fill);

            detail = new Label(string.Empty);
            detail.AddToClassList("performance-detail");
            meter.Add(detail);
            return meter;
        }

        private static VisualElement CreateBreakdownRow(
            string label,
            out Label value)
        {
            var row = new VisualElement();
            row.AddToClassList("performance-breakdown-row");
            var caption = new Label(label);
            caption.AddToClassList("performance-breakdown-label");
            row.Add(caption);
            value = new Label("—");
            value.AddToClassList("performance-breakdown-value");
            row.Add(value);
            return row;
        }

        private static Label CreateSectionLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("performance-section-label");
            return label;
        }

        private static VisualElement CreateLegend(
            string text,
            Color color)
        {
            var item = new VisualElement();
            item.AddToClassList("performance-legend-item");
            var swatch = new VisualElement();
            swatch.AddToClassList("performance-legend-swatch");
            swatch.style.backgroundColor = color;
            item.Add(swatch);
            item.Add(new Label(text));
            return item;
        }

        private void TogglePause()
        {
            if (_monitor == null)
            {
                return;
            }
            _monitor.TogglePaused();
            _nextRefreshAt = 0.0f;
            UpdateView();
        }

        private void CycleBudget()
        {
            if (_monitor == null)
            {
                return;
            }
            _monitor.CycleTargetFps();
            _nextRefreshAt = 0.0f;
            UpdateView();
        }

        private void ResetHistory()
        {
            if (_monitor == null)
            {
                return;
            }
            _monitor.Reset();
            _nextRefreshAt = 0.0f;
            UpdateView();
        }

        private void OnHeaderPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 ||
                evt.target is Button)
            {
                return;
            }
            var pointer = _overlay.WorldToLocal(
                (Vector2)evt.position);
            _dragPointerOffset = new Vector2(
                pointer.x - _panel.layout.x,
                pointer.y - _panel.layout.y);
            _dragging = true;
            _dragPointerId = evt.pointerId;
            PointerCaptureHelper.CapturePointer(
                _header,
                evt.pointerId);
            evt.StopPropagation();
        }

        private void OnHeaderPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging ||
                evt.pointerId != _dragPointerId)
            {
                return;
            }
            var pointer = _overlay.WorldToLocal(
                (Vector2)evt.position);
            SetPanelPosition(
                pointer.x - _dragPointerOffset.x,
                pointer.y - _dragPointerOffset.y);
            evt.StopPropagation();
        }

        private void OnHeaderPointerUp(PointerUpEvent evt)
        {
            if (!_dragging ||
                evt.pointerId != _dragPointerId)
            {
                return;
            }
            ResetDrag();
            evt.StopPropagation();
        }

        private void OnHeaderCaptureOut(
            PointerCaptureOutEvent evt)
        {
            _dragging = false;
            _dragPointerId = -1;
        }

        private void SetPanelPosition(float left, float top)
        {
            var width = _panel.resolvedStyle.width;
            var height = _panel.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 1.0f)
            {
                width = _panel.layout.width;
            }
            if (float.IsNaN(height) || height <= 1.0f)
            {
                height = _panel.layout.height;
            }
            var maxLeft = Mathf.Max(
                12.0f,
                _overlay.layout.width - width - 12.0f);
            var maxTop = Mathf.Max(
                12.0f,
                _overlay.layout.height - height - 12.0f);
            _panel.style.left = Mathf.Clamp(
                left,
                12.0f,
                maxLeft);
            _panel.style.top = Mathf.Clamp(
                top,
                12.0f,
                maxTop);
        }

        private void ResetDrag()
        {
            var pointerId = _dragPointerId;
            _dragging = false;
            _dragPointerId = -1;
            if (_header != null && pointerId >= 0)
            {
                PointerCaptureHelper.ReleasePointer(
                    _header,
                    pointerId);
            }
        }

        private static void SetMeter(
            VisualElement fill,
            float percent)
        {
            fill.style.width = Length.Percent(
                Mathf.Clamp(percent, 0.0f, 100.0f));
            fill.EnableInClassList(
                "is-warning",
                percent > 80.0f && percent <= 100.0f);
            fill.EnableInClassList(
                "is-critical",
                percent > 100.0f);
        }

        private static void SetStateClass(
            Label label,
            float percent)
        {
            label.EnableInClassList(
                "is-warning",
                percent > 85.0f && percent <= 105.0f);
            label.EnableInClassList(
                "is-critical",
                percent > 105.0f);
        }
    }
}
