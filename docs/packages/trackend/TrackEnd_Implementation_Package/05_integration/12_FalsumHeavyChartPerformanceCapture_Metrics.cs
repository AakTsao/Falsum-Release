// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Editor/FalsumHeavyChartPerformanceCapture.cs
// 行范围: 226-250
// 性能测试读取 MaskRebuildCount

        private static void ForceLegacyTrackEndRebuildIfRequested()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        ForceTrackEndRebuildEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }
            var composer = FalsumTrackEndComposer.Active;
            if (composer == null)
            {
                return;
            }
            var field = typeof(FalsumTrackEndComposer).GetField(
                "_maskDirty",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(composer, true);
        }

        private static void LogMetrics(FalsumChartEditor editor)
        {
            var field = typeof(FalsumChartEditor).GetField(
                "_performanceMonitor",
