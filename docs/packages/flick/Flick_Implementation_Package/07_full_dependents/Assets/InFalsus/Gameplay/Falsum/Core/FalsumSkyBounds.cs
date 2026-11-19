using System;

namespace InFalsus.Gameplay.Falsum.Core
{
    public static class FalsumSkyBounds
    {
        public const double Minimum = 0.0;
        public const double Maximum = 1.0;
        public const double Epsilon = 0.000000001;

        public struct Span
        {
            public double Center;
            public double Width;
            public bool Clamped;
        }

        public struct CellSpan
        {
            public int Center;
            public int Width;
            public bool Clamped;
        }

        public static bool IsOutOfBounds(double center, double width)
        {
            if (double.IsNaN(center) ||
                double.IsInfinity(center) ||
                double.IsNaN(width) ||
                double.IsInfinity(width) ||
                width < 0.0)
            {
                return true;
            }

            var left = center - width * 0.5;
            var right = center + width * 0.5;
            return left < Minimum - Epsilon ||
                   right > Maximum + Epsilon;
        }

        public static Span ClampSpanPreservingWidth(
            double center,
            double width)
        {
            var clampedWidth = ClampWidth(width);
            var halfWidth = clampedWidth * 0.5;
            var clampedCenter = ClampFinite(
                center,
                Minimum + halfWidth,
                Maximum - halfWidth,
                0.5);
            return CreateSpan(center, width, clampedCenter, clampedWidth);
        }

        public static Span ClampSpanPreservingCenter(
            double center,
            double width)
        {
            var clampedCenter = ClampFinite(
                center,
                Minimum,
                Maximum,
                0.5);
            var maxWidth = 2.0 * Math.Min(
                clampedCenter,
                Maximum - clampedCenter);
            var clampedWidth = Math.Min(
                ClampWidth(width),
                maxWidth);
            return CreateSpan(center, width, clampedCenter, clampedWidth);
        }

        public static CellSpan ClampCellSpan(
            int center,
            int width,
            int cellCount)
        {
            var cells = Math.Max(1, cellCount);
            var clampedWidth = Math.Max(0, Math.Min(width, cells));
            if ((clampedWidth & 1) != 0 && clampedWidth == cells)
            {
                clampedWidth--;
            }
            var minimumCenter = (clampedWidth + 1) / 2;
            var maximumCenter = (cells * 2 - clampedWidth) / 2;
            var clampedCenter = Math.Max(
                minimumCenter,
                Math.Min(maximumCenter, center));
            return new CellSpan
            {
                Center = clampedCenter,
                Width = clampedWidth,
                Clamped = clampedCenter != center ||
                          clampedWidth != width,
            };
        }

        private static Span CreateSpan(
            double originalCenter,
            double originalWidth,
            double center,
            double width)
        {
            return new Span
            {
                Center = center,
                Width = width,
                Clamped = !NearlyEqual(originalCenter, center) ||
                          !NearlyEqual(originalWidth, width),
            };
        }

        private static double ClampWidth(double width)
        {
            return ClampFinite(width, Minimum, Maximum, 0.0);
        }

        private static double ClampFinite(
            double value,
            double minimum,
            double maximum,
            double fallback)
        {
            if (double.IsNaN(value))
            {
                return fallback;
            }
            if (value <= minimum)
            {
                return minimum;
            }
            if (value >= maximum)
            {
                return maximum;
            }
            return value;
        }

        private static bool NearlyEqual(double left, double right)
        {
            if (double.IsNaN(left) ||
                double.IsNaN(right) ||
                double.IsInfinity(left) ||
                double.IsInfinity(right))
            {
                return left.Equals(right);
            }
            return Math.Abs(left - right) <= Epsilon;
        }
    }
}
