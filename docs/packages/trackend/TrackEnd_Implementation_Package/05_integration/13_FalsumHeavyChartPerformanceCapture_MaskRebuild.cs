// ==== TrackEnd 依赖片段（非完整文件）
// 源文件: Assets/InFalsus/Editor/FalsumHeavyChartPerformanceCapture.cs
// 行范围: 280-292
// MaskRebuildCount 输出

            var gcCollections = ReadIntProperty(
                type,
                monitor,
                "GcCollections");
            var maskRebuilds =
                FalsumTrackEndComposerFeature.MaskRebuildCount;
            Debug.Log(
                "[Falsum] Heavy chart performance: " +
                editor.DebugPerformanceSummary +
                " p95=" + p95.ToString("0.0") +
                " peak=" + recentPeak.ToString("0.0") +
                " worst=" + worstFrame.ToString("0.0") +
                " stutter=" + stutterFrames +
