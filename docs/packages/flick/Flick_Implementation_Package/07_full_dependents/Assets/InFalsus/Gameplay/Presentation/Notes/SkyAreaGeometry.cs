using System;
using InFalsus.Gameplay.Core.Charts;
using UnityEngine;

namespace InFalsus.Gameplay.Presentation.Notes
{
    public static class SkyAreaGeometry
    {
        public const float MillisecondsPerSubdivision = 5.0f;
        public const double SharedBoundaryEpsilonMs = 0.0001;

        public static int SubdivisionCount(ChartNote note)
        {
            if (note == null)
            {
                return 1;
            }

            var durationMs = Math.Max(0.0, note.DurationMs);
            return Math.Max(
                1,
                (int)(durationMs / MillisecondsPerSubdivision) + 1);
        }

        public static float Ease(float value, int easing)
        {
            var t = Mathf.Clamp01(value);
            if (easing == 1)
            {
                return Mathf.Sin(t * Mathf.PI * 0.5f);
            }
            if (easing == 2)
            {
                return 1.0f - Mathf.Cos(t * Mathf.PI * 0.5f);
            }
            return t;
        }

        public static bool IsSharedBoundary(
            double previousEndMs,
            double currentStartMs)
        {
            return Math.Abs(previousEndMs - currentStartMs) <=
                   SharedBoundaryEpsilonMs;
        }
    }
}
