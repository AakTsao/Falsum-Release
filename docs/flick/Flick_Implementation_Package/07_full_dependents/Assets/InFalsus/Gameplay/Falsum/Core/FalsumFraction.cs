using System;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Core
{
    public static class FalsumFraction
    {
        public const int MaxDenominator = 200;

        public static int NormalizePair(
            double position,
            double width,
            int preferredDenominator,
            out double normalizedPosition,
            out double normalizedWidth)
        {
            var preferred = Mathf.Clamp(
                preferredDenominator > 0 ? preferredDenominator : 24,
                1,
                MaxDenominator);
            for (var denominator = preferred;
                 denominator <= MaxDenominator;
                 denominator += preferred)
            {
                var positionNumerator = Math.Round(
                    position * denominator,
                    MidpointRounding.AwayFromZero);
                var widthNumerator = Math.Round(
                    width * denominator,
                    MidpointRounding.AwayFromZero);
                if (IsExact(position, positionNumerator, denominator) &&
                    IsExact(width, widthNumerator, denominator))
                {
                    normalizedPosition =
                        positionNumerator / denominator;
                    normalizedWidth =
                        widthNumerator / denominator;
                    return denominator;
                }
            }

            normalizedPosition = Math.Round(
                                       position * MaxDenominator,
                                       MidpointRounding.AwayFromZero) /
                                   MaxDenominator;
            normalizedWidth = Math.Round(
                                  width * MaxDenominator,
                                  MidpointRounding.AwayFromZero) /
                              MaxDenominator;
            return MaxDenominator;
        }

        private static bool IsExact(
            double value,
            double numerator,
            int denominator)
        {
            return Math.Abs(value - numerator / denominator) <=
                   0.0000005;
        }
    }
}
