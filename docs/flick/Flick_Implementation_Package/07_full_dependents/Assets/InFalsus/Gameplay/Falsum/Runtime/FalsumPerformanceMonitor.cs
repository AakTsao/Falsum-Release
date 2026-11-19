using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using L = InFalsus.Gameplay.Falsum.Localization.FalsumLocalization;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public enum FalsumPerformanceSeries
    {
        FrameTime,
        CpuMainThread,
        CpuRenderThread,
        Gpu,
    }

    public sealed class FalsumPerformanceMonitor : IDisposable
    {
        public const int HistoryCapacity = 120;

        private sealed class HistoryBuffer
        {
            private readonly float[] _values =
                new float[HistoryCapacity];
            private int _head;

            public int Count { get; private set; }

            public void Add(float value)
            {
                _values[_head] = Mathf.Max(0.0f, value);
                _head = (_head + 1) % HistoryCapacity;
                Count = Mathf.Min(Count + 1, HistoryCapacity);
            }

            public float Get(int index)
            {
                if (index < 0 || index >= Count)
                {
                    return 0.0f;
                }
                var physical =
                    (_head - Count + index + HistoryCapacity) %
                    HistoryCapacity;
                return _values[physical];
            }

            public void Clear()
            {
                Array.Clear(_values, 0, _values.Length);
                _head = 0;
                Count = 0;
            }
        }

        private readonly HistoryBuffer _frameHistory =
            new HistoryBuffer();
        private readonly HistoryBuffer _cpuMainHistory =
            new HistoryBuffer();
        private readonly HistoryBuffer _cpuRenderHistory =
            new HistoryBuffer();
        private readonly HistoryBuffer _gpuHistory =
            new HistoryBuffer();
        private readonly FrameTiming[] _frameTimings =
            new FrameTiming[1];
        private readonly float[] _sortBuffer =
            new float[HistoryCapacity];

        private bool _frameActive;
        private bool _frameTimingFeatureEnabled;
        private bool _frameTimingReadFailed;
        private bool _profilerRecordersStarted;
        private bool _profilerWasEnabled;
        private bool _gpuProfilerAreaWasEnabled;
        private long _frameStartedAt;
        private long _managedBytesStartedAt;
        private int _gcCollectionsStartedAt;
        private double _viewTickMs;
        private double _overlayMs;
        private double _viewSyncMs;
        private double _editorRefreshMs;
        private float _smoothedFrameMs;
        private float _smoothedCpuMainMs;
        private float _smoothedCpuRenderMs;
        private float _smoothedGpuMs;
        private float _smoothedScriptFrameMs;
        private float _worstFrameMs;
        private string _worstFrameBottleneck = string.Empty;
        private double _worstFrameAt;
        private int _historyVersion;
        private ProfilerRecorder _cpuMainRecorder;
        private ProfilerRecorder _cpuRenderRecorder;
        private ProfilerRecorder _gpuRecorder;

        public bool Enabled { get; private set; }
        public bool Paused { get; private set; }
        public int TargetFps { get; private set; } = 60;
        public float FrameBudgetMs
        {
            get { return 1000.0f / Mathf.Max(1, TargetFps); }
        }

        public float FrameMs { get; private set; }
        public float SmoothedFrameMs
        {
            get { return _smoothedFrameMs; }
        }
        public float Fps
        {
            get
            {
                return _smoothedFrameMs > 0.0001f
                    ? 1000.0f / _smoothedFrameMs
                    : 0.0f;
            }
        }
        public float CpuMainThreadMs { get; private set; }
        public float CpuRenderThreadMs { get; private set; }
        public float GpuMs { get; private set; }
        public float ScriptFrameMs { get; private set; }
        public float ViewTickMs
        {
            get { return (float)_viewTickMs; }
        }
        public float OverlayMs
        {
            get { return (float)_overlayMs; }
        }
        public float ViewSyncMs
        {
            get { return (float)_viewSyncMs; }
        }
        public float EditorRefreshMs
        {
            get { return (float)_editorRefreshMs; }
        }
        public float ManagedAllocationKb { get; private set; }
        public int GcCollections { get; private set; }
        public bool FrameTimingAvailable { get; private set; }
        public bool GpuTimingAvailable { get; private set; }
        public bool FrameTimingFeatureEnabled
        {
            get { return _frameTimingFeatureEnabled; }
        }
        public bool CpuMainRecorderValid
        {
            get { return _cpuMainRecorder.Valid; }
        }
        public bool CpuRenderRecorderValid
        {
            get { return _cpuRenderRecorder.Valid; }
        }
        public bool GpuRecorderValid
        {
            get { return _gpuRecorder.Valid; }
        }
        public string Bottleneck { get; private set; } =
            L.Get("falsum.performance.waiting");
        public string BottleneckDetail { get; private set; } =
            L.Get("falsum.performance.openHint");
        public string WorstFrameBottleneck
        {
            get { return _worstFrameBottleneck; }
        }
        public float WorstFrameMs
        {
            get { return _worstFrameMs; }
        }
        public double WorstFrameAgeSeconds
        {
            get
            {
                return _worstFrameAt > 0.0
                    ? Math.Max(
                        0.0,
                        Time.unscaledTimeAsDouble -
                        _worstFrameAt)
                    : 0.0;
            }
        }
        public int HistoryCount
        {
            get { return _frameHistory.Count; }
        }
        public int HistoryVersion
        {
            get { return _historyVersion; }
        }
        public int NoteCount { get; private set; }
        public int EventCount { get; private set; }
        public int GridVertexCount { get; private set; }
        public string DeviceSummary { get; private set; }

        public FalsumPerformanceMonitor()
        {
            DeviceSummary =
                L.Get(
                    "falsum.performance.deviceSummary",
                    ("deviceType", SystemInfo.graphicsDeviceType),
                    ("deviceName", SystemInfo.graphicsDeviceName));
        }

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled)
            {
                return;
            }
            Enabled = enabled;
            if (enabled)
            {
                Paused = false;
                _frameTimingFeatureEnabled =
                    FrameTimingManager.IsFeatureEnabled();
                _frameTimingReadFailed = false;
                StartProfilerRecorders();
                ResetFrameState();
            }
            else
            {
                StopProfilerRecorders();
                _frameActive = false;
                _viewTickMs = 0.0;
                _overlayMs = 0.0;
                _viewSyncMs = 0.0;
                _editorRefreshMs = 0.0;
            }
        }

        public void TogglePaused()
        {
            if (!Enabled)
            {
                return;
            }
            Paused = !Paused;
            _frameActive = false;
        }

        public void CycleTargetFps()
        {
            switch (TargetFps)
            {
                case 30:
                    TargetFps = 60;
                    break;
                case 60:
                    TargetFps = 120;
                    break;
                case 120:
                    TargetFps = 144;
                    break;
                default:
                    TargetFps = 30;
                    break;
            }
        }

        public void Reset()
        {
            _frameHistory.Clear();
            _cpuMainHistory.Clear();
            _cpuRenderHistory.Clear();
            _gpuHistory.Clear();
            _smoothedFrameMs = 0.0f;
            _smoothedCpuMainMs = 0.0f;
            _smoothedCpuRenderMs = 0.0f;
            _smoothedGpuMs = 0.0f;
            _smoothedScriptFrameMs = 0.0f;
            _worstFrameMs = 0.0f;
            _worstFrameBottleneck = string.Empty;
            _worstFrameAt = 0.0;
            _historyVersion++;
            ResetFrameState();
        }

        public void BeginFrame()
        {
            if (!Enabled || Paused)
            {
                _frameActive = false;
                return;
            }
            _frameActive = true;
            _frameStartedAt = Timestamp();
            _managedBytesStartedAt =
                GC.GetAllocatedBytesForCurrentThread();
            _gcCollectionsStartedAt = GC.CollectionCount(0);
            _viewTickMs = 0.0;
            _overlayMs = 0.0;
            _viewSyncMs = 0.0;
            _editorRefreshMs = 0.0;
            if (_frameTimingFeatureEnabled &&
                !_frameTimingReadFailed)
            {
                FrameTimingManager.CaptureFrameTimings();
            }
        }

        public void EndFrame()
        {
            if (!_frameActive)
            {
                return;
            }
            _frameActive = false;

            FrameMs = Mathf.Max(
                0.0f,
                Time.unscaledDeltaTime * 1000.0f);
            ScriptFrameMs = (float)ElapsedMilliseconds(
                _frameStartedAt);
            CpuMainThreadMs = 0.0f;
            CpuRenderThreadMs = 0.0f;
            GpuMs = 0.0f;
            ReadFrameTimings();
            ReadProfilerRecorderFallback();
            if (!FrameTimingAvailable ||
                CpuMainThreadMs <= 0.0001f)
            {
                CpuMainThreadMs = ScriptFrameMs;
            }

            ManagedAllocationKb = Mathf.Max(
                0.0f,
                (GC.GetAllocatedBytesForCurrentThread() -
                 _managedBytesStartedAt) / 1024.0f);
            GcCollections = Mathf.Max(
                0,
                GC.CollectionCount(0) - _gcCollectionsStartedAt);

            _smoothedFrameMs = Smooth(
                _smoothedFrameMs,
                FrameMs,
                0.12f);
            _smoothedCpuMainMs = Smooth(
                _smoothedCpuMainMs,
                CpuMainThreadMs,
                0.12f);
            _smoothedCpuRenderMs = Smooth(
                _smoothedCpuRenderMs,
                CpuRenderThreadMs,
                0.12f);
            _smoothedGpuMs = Smooth(
                _smoothedGpuMs,
                GpuMs,
                0.12f);
            _smoothedScriptFrameMs = Smooth(
                _smoothedScriptFrameMs,
                ScriptFrameMs,
                0.12f);

            _frameHistory.Add(FrameMs);
            _cpuMainHistory.Add(CpuMainThreadMs);
            _cpuRenderHistory.Add(CpuRenderThreadMs);
            _gpuHistory.Add(GpuMs);
            _historyVersion++;

            UpdateBottleneck();
            if (FrameMs > _worstFrameMs)
            {
                _worstFrameMs = FrameMs;
                _worstFrameBottleneck = Bottleneck;
                _worstFrameAt = Time.unscaledTimeAsDouble;
            }
        }

        public void RecordViewTick(double milliseconds)
        {
            if (_frameActive)
            {
                _viewTickMs += Math.Max(0.0, milliseconds);
            }
        }

        public void RecordOverlay(double milliseconds)
        {
            if (_frameActive)
            {
                _overlayMs += Math.Max(0.0, milliseconds);
            }
        }

        public void RecordViewSync(double milliseconds)
        {
            if (_frameActive)
            {
                _viewSyncMs += Math.Max(0.0, milliseconds);
            }
        }

        public void RecordEditorRefresh(double milliseconds)
        {
            if (_frameActive)
            {
                _editorRefreshMs += Math.Max(0.0, milliseconds);
            }
        }

        public void SetSceneContext(
            int noteCount,
            int eventCount,
            int gridVertexCount)
        {
            NoteCount = Mathf.Max(0, noteCount);
            EventCount = Mathf.Max(0, eventCount);
            GridVertexCount = Mathf.Max(0, gridVertexCount);
        }

        public float GetHistory(
            FalsumPerformanceSeries series,
            int index)
        {
            switch (series)
            {
                case FalsumPerformanceSeries.CpuMainThread:
                    return _cpuMainHistory.Get(index);
                case FalsumPerformanceSeries.CpuRenderThread:
                    return _cpuRenderHistory.Get(index);
                case FalsumPerformanceSeries.Gpu:
                    return _gpuHistory.Get(index);
                default:
                    return _frameHistory.Get(index);
            }
        }

        public float GetRecentPeakMs()
        {
            var peak = 0.0f;
            for (var i = 0; i < HistoryCount; i++)
            {
                peak = Mathf.Max(peak, _frameHistory.Get(i));
            }
            return peak;
        }

        public float GetPercentile95Ms()
        {
            var count = HistoryCount;
            if (count == 0)
            {
                return 0.0f;
            }
            for (var i = 0; i < count; i++)
            {
                _sortBuffer[i] = _frameHistory.Get(i);
            }
            Array.Sort(_sortBuffer, 0, count);
            var index = Mathf.Clamp(
                Mathf.CeilToInt(count * 0.95f) - 1,
                0,
                count - 1);
            return _sortBuffer[index];
        }

        public int GetStutterFrameCount()
        {
            var threshold = FrameBudgetMs * 1.25f;
            var count = 0;
            for (var i = 0; i < HistoryCount; i++)
            {
                if (_frameHistory.Get(i) > threshold)
                {
                    count++;
                }
            }
            return count;
        }

        public static long Timestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public static double ElapsedMilliseconds(long startedAt)
        {
            if (startedAt <= 0)
            {
                return 0.0;
            }
            return
                (Stopwatch.GetTimestamp() - startedAt) *
                1000.0 /
                Stopwatch.Frequency;
        }

        public void Dispose()
        {
            SetEnabled(false);
        }

        private void StartProfilerRecorders()
        {
            if (_profilerRecordersStarted)
            {
                StopProfilerRecorders();
            }
            _profilerWasEnabled =
                UnityEngine.Profiling.Profiler.enabled;
            if (!_profilerWasEnabled &&
                (Application.isEditor ||
                 UnityEngine.Debug.isDebugBuild))
            {
                UnityEngine.Profiling.Profiler.enabled = true;
            }
            _gpuProfilerAreaWasEnabled =
                UnityEngine.Profiling.Profiler.GetAreaEnabled(
                    UnityEngine.Profiling.ProfilerArea.GPU);
            if (!_gpuProfilerAreaWasEnabled)
            {
                UnityEngine.Profiling.Profiler.SetAreaEnabled(
                    UnityEngine.Profiling.ProfilerArea.GPU,
                    true);
            }
            _cpuMainRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "CPU Main Thread Frame Time",
                1);
            _cpuRenderRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "CPU Render Thread Frame Time",
                1);
            _gpuRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "GPU Frame Time",
                1,
                ProfilerRecorderOptions.GpuRecorder);
            _profilerRecordersStarted = true;
        }

        private void StopProfilerRecorders()
        {
            if (!_profilerRecordersStarted)
            {
                return;
            }
            if (_cpuMainRecorder.Valid)
            {
                _cpuMainRecorder.Stop();
            }
            if (_cpuRenderRecorder.Valid)
            {
                _cpuRenderRecorder.Stop();
            }
            if (_gpuRecorder.Valid)
            {
                _gpuRecorder.Stop();
            }
            _cpuMainRecorder.Dispose();
            _cpuRenderRecorder.Dispose();
            _gpuRecorder.Dispose();
            _profilerRecordersStarted = false;
            if (!_gpuProfilerAreaWasEnabled)
            {
                UnityEngine.Profiling.Profiler.SetAreaEnabled(
                    UnityEngine.Profiling.ProfilerArea.GPU,
                    false);
            }
            if (!_profilerWasEnabled &&
                (Application.isEditor ||
                 UnityEngine.Debug.isDebugBuild))
            {
                UnityEngine.Profiling.Profiler.enabled = false;
            }
        }

        private void ReadProfilerRecorderFallback()
        {
            if (!_profilerRecordersStarted)
            {
                return;
            }
            if (CpuMainThreadMs <= 0.0001f &&
                _cpuMainRecorder.Valid)
            {
                CpuMainThreadMs = ToMilliseconds(
                    _cpuMainRecorder.LastValueAsDouble);
            }
            if (CpuRenderThreadMs <= 0.0001f &&
                _cpuRenderRecorder.Valid)
            {
                CpuRenderThreadMs = ToMilliseconds(
                    _cpuRenderRecorder.LastValueAsDouble);
            }
            if (GpuMs <= 0.0001f &&
                _gpuRecorder.Valid)
            {
                GpuMs = ToMilliseconds(
                    _gpuRecorder.LastValueAsDouble);
            }
            FrameTimingAvailable =
                CpuMainThreadMs > 0.0001f ||
                CpuRenderThreadMs > 0.0001f;
            GpuTimingAvailable = GpuMs > 0.0001f;
        }

        private void ReadFrameTimings()
        {
            FrameTimingAvailable = false;
            GpuTimingAvailable = false;
            if (!_frameTimingFeatureEnabled ||
                _frameTimingReadFailed)
            {
                return;
            }
            try
            {
                var count = FrameTimingManager.GetLatestTimings(
                    1,
                    _frameTimings);
                if (count <= 0)
                {
                    return;
                }
                var timing = _frameTimings[0];
                CpuMainThreadMs = Mathf.Max(
                    0.0f,
                    (float)timing.cpuMainThreadFrameTime);
                CpuRenderThreadMs = Mathf.Max(
                    0.0f,
                    (float)timing.cpuRenderThreadFrameTime);
                GpuMs = Mathf.Max(
                    0.0f,
                    (float)timing.gpuFrameTime);
                FrameTimingAvailable =
                    CpuMainThreadMs > 0.0001f ||
                    CpuRenderThreadMs > 0.0001f;
                GpuTimingAvailable = GpuMs > 0.0001f;
            }
            catch (Exception)
            {
                _frameTimingReadFailed = true;
                FrameTimingAvailable = false;
                GpuTimingAvailable = false;
            }
        }

        private void UpdateBottleneck()
        {
            var budget = FrameBudgetMs;
            if (FrameMs <= budget * 1.15f)
            {
                Bottleneck = L.Get("falsum.performance.flow");
                BottleneckDetail = L.Get(
                    "falsum.performance.budgetWithin");
                return;
            }

            if (_overlayMs >= budget * 0.55 ||
                _overlayMs >= FrameMs * 0.34)
            {
                Bottleneck = L.Get(
                    "falsum.performance.bottleneck.trackGrid");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.overlay"),
                    (float)_overlayMs);
            }
            else if (_editorRefreshMs >= budget * 0.55 ||
                     _editorRefreshMs >= FrameMs * 0.34)
            {
                Bottleneck = L.Get(
                    "falsum.performance.editorRefresh");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.chartRefresh"),
                    (float)_editorRefreshMs);
            }
            else if (_viewSyncMs >= budget * 0.5 ||
                     _viewSyncMs >= FrameMs * 0.3)
            {
                Bottleneck = L.Get("falsum.performance.uiSync");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.fieldSync"),
                    (float)_viewSyncMs);
            }
            else if (_viewTickMs >= budget * 0.5)
            {
                Bottleneck = L.Get(
                    "falsum.performance.bottleneck.uiToolkitTick");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.layout"),
                    (float)_viewTickMs);
            }
            else if (GpuTimingAvailable &&
                     GpuMs >= Mathf.Max(
                         CpuMainThreadMs,
                         CpuRenderThreadMs))
            {
                Bottleneck = L.Get(
                    "falsum.performance.bottleneck.gpu");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.gpuFrame"),
                    GpuMs);
            }
            else if (CpuRenderThreadMs >= CpuMainThreadMs &&
                     CpuRenderThreadMs > 0.0001f)
            {
                Bottleneck = L.Get(
                    "falsum.performance.bottleneck.cpuRender");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.renderThread"),
                    CpuRenderThreadMs);
            }
            else if (CpuMainThreadMs > 0.0001f)
            {
                Bottleneck = L.Get(
                    "falsum.performance.bottleneck.cpuMain");
                BottleneckDetail = FormatDetail(
                    L.Get("falsum.performance.detail.mainThread"),
                    CpuMainThreadMs);
            }
            else
            {
                Bottleneck = L.Get(
                    "falsum.performance.bottleneck.vsync");
                BottleneckDetail = L.Get(
                    "falsum.performance.detail.noExcessWork");
            }
        }

        private void ResetFrameState()
        {
            FrameTimingAvailable = false;
            GpuTimingAvailable = false;
            CpuMainThreadMs = 0.0f;
            CpuRenderThreadMs = 0.0f;
            GpuMs = 0.0f;
            ScriptFrameMs = 0.0f;
            ManagedAllocationKb = 0.0f;
            GcCollections = 0;
        }

        private static float Smooth(
            float current,
            float next,
            float amount)
        {
            return current <= 0.0001f
                ? next
                : Mathf.Lerp(current, next, amount);
        }

        private static float ToMilliseconds(double nanoseconds)
        {
            return Mathf.Max(
                0.0f,
                (float)(nanoseconds / 1000000.0));
        }

        private static string FormatDetail(
            string label,
            float milliseconds)
        {
            return L.Get(
                "falsum.performance.detail.value",
                ("label", label),
                ("value", milliseconds.ToString("0.0")));
        }
    }
}
