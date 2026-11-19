using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InFalsus.Gameplay.Core.Charts;
using InFalsus.Gameplay.Core.Timing;
using InFalsus.Gameplay.Presentation.Notes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsus.EditorTools
{
    public static class FalsumNoteRenderSelfCheck
    {
        private static readonly Type CacheType = typeof(NoteRenderer).Assembly.GetType(
            "InFalsus.Gameplay.Presentation.Notes.SkyAreaRowCache", true);
        private static readonly Type PropertyType = typeof(NoteRenderer).Assembly.GetType(
            "InFalsus.Gameplay.Presentation.Notes.NoteRendererPropertyState", true);

        [MenuItem("InFalsus/Falsum/Run Note Render Regression Check")]
        public static void Run()
        {
            var checks = new List<string>();
            var error = string.Empty;
            try
            {
                CheckRows(checks);
                CheckProperties(checks);
                var renderer = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(
                    "Assets/Settings/PC_Renderer.asset");
                var ssao = renderer.rendererFeatures.Find(feature => feature != null &&
                    feature.GetType().Name == "ScreenSpaceAmbientOcclusion");
                Require(ssao != null && new SerializedObject(ssao).FindProperty("m_Settings.Downsample").boolValue,
                    "PC SSAO must use half resolution.");
                checks.Add("ssao_half_resolution_preserves_feature");
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                Debug.LogException(exception);
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "../Diagnostics/FalsumNoteRenderSelfCheck.json"),
                JsonUtility.ToJson(new Report { passed = error.Length == 0, checks = checks.ToArray(), error = error }, true));
            if (error.Length != 0) throw new InvalidOperationException(error);
            Debug.Log("[Falsum] Note render regression checks passed.");
        }

        private static void CheckRows(List<string> checks)
        {
            var chart = new Chart { Bpm = 180, Division = 4 };
            var note = new ChartNote { Kind = NoteKind.SkyArea, TimeMs = 1000, DurationMs = 30000,
                StartPos = 0.2, StartWidth = 0.3, EndPos = 0.7, EndWidth = 0.2,
                LeftEasing = 1, RightEasing = 2 };
            chart.Notes.Add(note);
            var motion = new TrackMotion(chart, 5);
            var cache = Activator.CreateInstance(CacheType, true);
            for (var scenario = 0; scenario < 4; scenario++)
            {
                chart.SpeedEvents.Clear();
                if (scenario > 0)
                {
                    chart.SpeedEvents.Add(new ChartEvent { Kind = ChartEventKind.TrackSpeed, TimeMs = 0,
                        Multiplier = scenario == 1 ? 0.1 : 1 });
                    if (scenario >= 2)
                    {
                        chart.SpeedEvents.Add(new ChartEvent { Kind = ChartEventKind.TrackSpeed, TimeMs = 5000,
                            Multiplier = scenario == 2 ? 0 : -2 });
                        chart.SpeedEvents.Add(new ChartEvent { Kind = ChartEventKind.TrackSpeed, TimeMs = 9000,
                            Multiplier = 1.5 });
                    }
                }
                chart.RebuildTimeline();
                var length = Math.Max(0.0001, motion.HoldLength(note));
                Require((bool)Call(cache, "Prepare", chart, motion, note, length), "Changed timing must rebuild rows.");
                Require(!(bool)Call(cache, "Prepare", chart, motion, note, length), "Unchanged rows must be reused.");
                var count = (int)CacheType.GetProperty("Count").GetValue(cache);
                foreach (var now in new[] { 0.0, 1000.0, 1666.7, 6000.0, 9999.0, 24567.8, 31000.0 })
                {
                    var scaledNow = motion.ScaledNowAt(now);
                    object[] range = { scaledNow, motion, 0, 0 };
                    Call(cache, "VisibleRows", range);
                    var first = (int)range[2];
                    var last = (int)range[3];
                    for (var row = 0; row <= count; row++)
                    {
                        var sample = Call(cache, "Get", row);
                        var t = (float)row / count;
                        var actualZ = (float)CallStatic(CacheType, "RawZ", sample, scaledNow, motion);
                        Require(actualZ == (float)motion.NoteZAt(note.TimeMs + note.DurationMs * t, now),
                            "Cached row Z diverged from TrackMotion.");
                        var start = Mathf.Lerp(-0.81f, 0.81f, (float)note.StartPos);
                        var end = Mathf.Lerp(-0.81f, 0.81f, (float)note.EndPos);
                        var width0 = Mathf.Max(0.0001f, (float)note.StartWidth * 1.62f);
                        var width1 = Mathf.Max(0.0001f, (float)note.EndWidth * 1.62f);
                        Require(Field<float>(sample, "Left") == Mathf.Lerp(start - width0 * 0.5f,
                            end - width1 * 0.5f, SkyAreaGeometry.Ease(t, note.LeftEasing)) &&
                            Field<float>(sample, "Right") == Mathf.Lerp(start + width0 * 0.5f,
                            end + width1 * 0.5f, SkyAreaGeometry.Ease(t, note.RightEasing)),
                            "Cached row edges changed the original sampling.");
                        if (row == 0) continue;
                        var previousT = (float)(row - 1) / count;
                        var z0 = Mathf.Clamp((float)motion.NoteZAt(note.TimeMs + note.DurationMs * previousT, now),
                            0f, (float)motion.TrackLength);
                        var z1 = Mathf.Clamp(actualZ, 0f, (float)motion.TrackLength);
                        var collapsed = z0 == z1 && (z0 == 0f || z0 == (float)motion.TrackLength);
                        Require(collapsed || (row >= first && row <= last),
                            "Visible row bounds skipped a nondegenerate quad.");
                    }
                }
            }
            checks.Add("exact_sky_rows_and_visible_bounds_slow_stop_reverse_seek");
            chart.TrackSpeedEffectsEnabled = false;
            Require((bool)Call(cache, "Prepare", chart, motion, note, motion.HoldLength(note)),
                "Track suppression must invalidate rows.");
            note.StartPos += 0.1;
            Require((bool)Call(cache, "Prepare", chart, motion, note, motion.HoldLength(note)),
                "Editing note edges must invalidate rows at the same playhead.");
            motion.Speed = 7;
            Require((bool)Call(cache, "Prepare", chart, motion, note, motion.HoldLength(note)),
                "Flow changes must invalidate row distances.");
            motion.TrackLength *= 1.1;
            Require((bool)Call(cache, "Prepare", chart, motion, note, motion.HoldLength(note)),
                "Track length changes must invalidate rows.");
            checks.Add("sky_cache_edit_timing_flow_and_suppression_invalidation");
        }

        private static void CheckProperties(List<string> checks)
        {
            var root = new GameObject("NotePropertyRegression");
            Material material = null;
            Material other = null;
            try
            {
                var renderer = root.AddComponent<MeshRenderer>();
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/InFalsus/Recovered/dc.shader");
                Require(shader != null, "Flick shader is missing.");
                material = new Material(shader);
                renderer.sharedMaterial = material;
                var original = new MaterialPropertyBlock();
                var extra = Shader.PropertyToID("_RegressionUnrelated");
                original.SetFloat(extra, 17);
                renderer.SetPropertyBlock(original);
                var state = Activator.CreateInstance(PropertyType, new object[] { renderer });
                Require((bool)Call(state, "Apply", NoteKind.Flick, 0.6f, 0f, false, Color.white, 4, 3f, false),
                    "Initial Flick properties must be applied.");
                Require(!(bool)Call(state, "Apply", NoteKind.Flick, 0.6f, 0f, false, Color.white, 4, 3f, false),
                    "Unchanged Flick properties must not be uploaded.");
                var output = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(output);
                Require(output.GetFloat(extra) == 17 && output.GetFloat("_BarRepeat") == 3 &&
                    output.GetColor("_Color") == new Color(0.2f, 0.95f, 0.55f, 0.6f),
                    "Flick properties or unrelated overrides changed.");
                Require((bool)Call(state, "Apply", NoteKind.Flick, 0.6f, 0f, false, Color.white, 16, 3f, false),
                    "Changing Flick direction must refresh its color.");
                renderer.GetPropertyBlock(output);
                Require(output.GetColor("_Color") == new Color(1f, 0.82f, 0.18f, 0.6f), "Left Flick color is wrong.");
                other = new Material(shader);
                renderer.sharedMaterial = other;
                Require((bool)Call(state, "Apply", NoteKind.Flick, 0.6f, 0f, false, Color.white, 16, 3f, false),
                    "Changing a material must rebuild its property capabilities.");
                renderer.GetPropertyBlock(output);
                output.SetFloat(extra, 29);
                renderer.SetPropertyBlock(output);
                Require((bool)Call(state, "Apply", NoteKind.Flick, 0.6f, 0f, false, Color.white, 16, 3f, true),
                    "Pool reuse must force initial state application.");
                renderer.GetPropertyBlock(output);
                Require(output.GetFloat(extra) == 29,
                    "Pool reuse must preserve unrelated overrides applied while the note was inactive.");
                checks.Add("renderer_state_preserves_overrides_direction_material_and_pool_reset");
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (material != null) Object.DestroyImmediate(material);
                if (other != null) Object.DestroyImmediate(other);
            }
        }

        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
        private static object CallStatic(Type type, string name, params object[] args) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        [Serializable] private sealed class Report { public bool passed; public string[] checks; public string error; }
    }
}
