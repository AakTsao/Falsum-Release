using System.Collections.Generic;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.UI
{
    /// <summary>
    /// Shared metrics and placement rules for measure line number badges.
    /// Badges are world-space quads at the measure line's outer right end,
    /// so their size follows perspective; only the number text stays flat.
    /// </summary>
    public static class FalsumMeasureNumberLayout
    {
        public const int MaxBadges = 32;

        /// <summary>Badge edge length in world units.</summary>
        public const float BadgeSize = 0.16f;

        /// <summary>Gap between the anchor and the badge corner.</summary>
        public const float AnchorGap = 0.012f;

        /// <summary>Minimum screen distance between two badges, in pixels.</summary>
        public const float MinScreenSpacing = 16.0f;

        public static string Format(int number)
        {
            return "#" + number;
        }

        /// <summary>
        /// Near-to-far placement: a badge is rejected when an already
        /// accepted badge projects closer than the minimum spacing.
        /// </summary>
        public static bool Accepts(
            IList<Vector2> placed,
            Vector2 screenPoint)
        {
            if (placed == null)
            {
                return true;
            }
            var minimum =
                MinScreenSpacing * MinScreenSpacing;
            for (var i = 0; i < placed.Count; i++)
            {
                if ((placed[i] - screenPoint).sqrMagnitude <
                    minimum)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool CanAccept(
            IList<Vector2> placed,
            Vector2 screenPoint)
        {
            return placed != null &&
                   placed.Count < MaxBadges &&
                   Accepts(placed, screenPoint);
        }
    }
}
