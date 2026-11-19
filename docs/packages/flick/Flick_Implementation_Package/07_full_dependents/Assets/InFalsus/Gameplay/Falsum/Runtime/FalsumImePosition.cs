using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    public static class FalsumImePosition
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int CfsPoint = 0x0002;
        private const int CfsForcePosition = 0x0020;
        private const int CfsCandidatePos = 0x0040;

        public static bool TrySetNormalizedPosition(
            float normalizedX,
            float normalizedY)
        {
            var window = ActiveUnityWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }
            NativeRect client;
            if (!GetClientRect(window, out client))
            {
                return false;
            }
            var width = client.Right - client.Left;
            var height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }
            var x = Clamp(
                (int)System.Math.Round(normalizedX * width),
                0,
                width - 1);
            var y = Clamp(
                (int)System.Math.Round(normalizedY * height),
                0,
                height - 1);
            var context = ImmGetContext(window);
            if (context == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                var point = new NativePoint
                {
                    X = x,
                    Y = y,
                };
                var composition = new CompositionForm
                {
                    Style = CfsPoint | CfsForcePosition,
                    CurrentPosition = point,
                };
                var candidate = new CandidateForm
                {
                    Index = 0,
                    Style = CfsCandidatePos,
                    CurrentPosition = point,
                };
                var compositionSet =
                    ImmSetCompositionWindow(context, ref composition);
                ImmSetCandidateWindow(context, ref candidate);
                return compositionSet;
            }
            finally
            {
                ImmReleaseContext(window, context);
            }
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum
                ? minimum
                : value > maximum ? maximum : value;
        }

        private static IntPtr ActiveUnityWindow()
        {
            var processId = (uint)Process.GetCurrentProcess().Id;
            var window = GetActiveWindow();
            if (BelongsToProcess(window, processId))
            {
                return window;
            }
            window = GetForegroundWindow();
            return BelongsToProcess(window, processId)
                ? window
                : IntPtr.Zero;
        }

        private static bool BelongsToProcess(
            IntPtr window,
            uint processId)
        {
            if (window == IntPtr.Zero)
            {
                return false;
            }
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            return owner == processId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionForm
        {
            public int Style;
            public NativePoint CurrentPosition;
            public NativeRect Area;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CandidateForm
        {
            public int Index;
            public int Style;
            public NativePoint CurrentPosition;
            public NativeRect Area;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(
            IntPtr window,
            out NativeRect rect);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr window);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(
            IntPtr window,
            IntPtr context);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetCompositionWindow(
            IntPtr context,
            ref CompositionForm form);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetCandidateWindow(
            IntPtr context,
            ref CandidateForm form);
#else
        public static bool TrySetNormalizedPosition(
            float normalizedX,
            float normalizedY)
        {
            return false;
        }
#endif
    }
}
