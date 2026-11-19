using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace InFalsus.EditorTools
{
    [InitializeOnLoad]
    public static class FalsumEditorScaleRepair
    {
        private const string RepairMenuPath =
            "InFalsus/Gameplay/Repair Editor DPI Scale";

        private static readonly MethodInfo ScaleChangedMethod =
            typeof(EditorWindow).GetMethod(
                "OnBackingScaleFactorChangedInternal",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static float _lastScale = -1.0f;
        private static double _lastRefreshTime;

        static FalsumEditorScaleRepair()
        {
            if (Application.isBatchMode)
            {
                return;
            }
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.focusChanged -= OnFocusChanged;
            EditorApplication.focusChanged += OnFocusChanged;
            EditorApplication.delayCall += RefreshAllWindows;
        }

        public static bool IsAvailable
        {
            get { return ScaleChangedMethod != null; }
        }

        [MenuItem(RepairMenuPath)]
        public static void RepairFromMenu()
        {
            RefreshAllWindows();
        }

        private static void Tick()
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            if (Mathf.Approximately(scale, _lastScale))
            {
                return;
            }
            _lastScale = scale;
            RefreshAllWindows();
        }

        private static void OnFocusChanged(bool focused)
        {
            if (focused)
            {
                RefreshAllWindows();
            }
        }

        private static void RefreshAllWindows()
        {
            if (ScaleChangedMethod == null)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastRefreshTime < 0.1)
            {
                return;
            }
            _lastRefreshTime = now;

            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (var index = 0; index < windows.Length; index++)
            {
                var window = windows[index];
                if (window == null)
                {
                    continue;
                }
                try
                {
                    ScaleChangedMethod.Invoke(window, null);
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogWarning(
                        "[Falsum] Editor DPI scale refresh failed for " +
                        window.GetType().Name + ": " +
                        exception.InnerException?.Message);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[Falsum] Editor DPI scale refresh failed: " +
                        exception.Message);
                }
            }
        }
    }
}
