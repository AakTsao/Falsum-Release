using System.Collections.Generic;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Screen-space polygon helpers for selection boxes and their anchors.
    ///
    /// The selection outline, the anchor positions and the hit-test area all
    /// come from the same polygon, so an anchor can never sit off its own
    /// box edge: whatever the projection does to the box, the anchors follow.
    /// </summary>
    internal static class FalsumSelectionGeometry
    {
        /// <summary>
        /// Index of the polygon edge that best matches a screen direction:
        /// 0 = left, 1 = right, 2 = front (toward the camera),
        /// 3 = back (away from the camera).
        /// </summary>
        public static int ClassifyEdge(
            Vector2 a,
            Vector2 b,
            Vector2 towardCamera)
        {
            var delta = b - a;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return 0;
            }
            var normal = new Vector2(-delta.y, delta.x).normalized;
            // The normal points to one side of the edge; align its sign with
            // the camera so left/right and front/back read consistently.
            var horizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
            return horizontal ? 0 : 1;
        }

        public static Vector2 Midpoint(Vector2 a, Vector2 b)
        {
            return (a + b) * 0.5f;
        }

        public static Vector2 EdgeDirection(Vector2 a, Vector2 b)
        {
            var delta = b - a;
            return delta.sqrMagnitude < 0.0001f
                ? Vector2.right
                : delta.normalized;
        }

        /// <summary>Outward normal pointing away from the polygon centre.</summary>
        public static Vector2 Outward(
            Vector2 a,
            Vector2 b,
            Vector2 center)
        {
            var mid = Midpoint(a, b);
            var delta = b - a;
            var n = new Vector2(-delta.y, delta.x).normalized;
            return Vector2.Dot(n, mid - center) < 0.0f ? -n : n;
        }

        public static Vector2 Centroid(IList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count == 0)
            {
                return Vector2.zero;
            }
            var sum = Vector2.zero;
            for (var i = 0; i < polygon.Count; i++)
            {
                sum += polygon[i];
            }
            return sum / polygon.Count;
        }

        /// <summary>Longest polygon edge, used to size handles against the box.</summary>
        public static float LongestEdge(IList<Vector2> polygon)
        {
            var best = 0.0f;
            if (polygon == null)
            {
                return best;
            }
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                best = Mathf.Max(
                    best,
                    (polygon[next] - polygon[i]).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        public static bool ContainsPoint(
            IList<Vector2> polygon,
            Vector2 point)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }
            var inside = false;
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                var a = polygon[i];
                var b = polygon[next];
                if (a.y > point.y != b.y > point.y &&
                    point.x < (b.x - a.x) * (point.y - a.y) /
                    (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// The point where a ray from <paramref name="origin"/> along
        /// <paramref name="direction"/> leaves the polygon.
        ///
        /// Used to keep a handle visible when its true position is off-screen:
        /// the handle slides to where that corner's own direction meets the
        /// drawn outline, so it keeps its role and stays grabbable.
        /// </summary>
        public static Vector2 BoundaryPointAlongDirection(
            IList<Vector2> polygon,
            Vector2 origin,
            Vector2 direction)
        {
            if (polygon == null || polygon.Count < 3 ||
                direction.sqrMagnitude < 0.0001f)
            {
                return origin;
            }
            var dir = direction.normalized;
            var bestDistance = -1.0f;
            var bestPoint = origin;
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                var a = polygon[i];
                var b = polygon[next];
                if (!TryRaySegment(
                        origin, dir, a, b, out var distance))
                {
                    continue;
                }
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestPoint = origin + dir * distance;
                }
            }
            return bestDistance > 0.0f ? bestPoint : origin;
        }

        private static bool TryRaySegment(
            Vector2 origin,
            Vector2 direction,
            Vector2 a,
            Vector2 b,
            out float distance)
        {
            distance = 0.0f;
            var edge = b - a;
            var denominator = direction.x * edge.y - direction.y * edge.x;
            if (Mathf.Abs(denominator) < 0.000001f)
            {
                return false;
            }
            var diff = a - origin;
            var t = (diff.x * edge.y - diff.y * edge.x) / denominator;
            if (t <= 0.0f)
            {
                return false;
            }
            var u = (diff.x * direction.y - diff.y * direction.x) /
                    denominator;
            if (u < 0.0f || u > 1.0f)
            {
                return false;
            }
            distance = t;
            return true;
        }

        /// <summary>Distance from a point to the polygon boundary.</summary>
        public static float DistanceToBoundary(
            IList<Vector2> polygon,
            Vector2 point)
        {
            var best = float.MaxValue;
            if (polygon == null || polygon.Count < 2)
            {
                return best;
            }
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                var a = polygon[i];
                var b = polygon[next];
                best = Mathf.Min(best, DistanceToSegment(point, a, b));
            }
            return best;
        }

        public static float DistanceToSegment(
            Vector2 point,
            Vector2 a,
            Vector2 b)
        {
            var ab = b - a;
            var length = ab.sqrMagnitude;
            if (length < 0.0001f)
            {
                return (point - a).magnitude;
            }
            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
            return (point - (a + ab * t)).magnitude;
        }

        /// <summary>
        /// Clamps a point onto (or just inside) the polygon.
        ///
        /// This is what keeps a handle reachable when its analytic position
        /// projects outside the drawn outline -- e.g. a skyarea whose endpoint
        /// time lies beyond the visible track, so the real corner is off-screen
        /// but the box is still huge and selected. Without it the handle
        /// disappeared entirely.
        /// </summary>
        public static Vector2 ClampToPolygon(
            IList<Vector2> polygon,
            Vector2 point,
            float inset)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return point;
            }
            if (ContainsPoint(polygon, point) &&
                DistanceToBoundary(polygon, point) >= inset)
            {
                return point;
            }
            // Hoist the centroid out of the loop. It used to be recomputed per
            // edge, which made this O(n^2); a long skyarea carries hundreds of
            // sampled vertices, so this was the hot spot that made anchors
            // crawl at low track speeds.
            var centre = Centroid(polygon);
            var bestDistance = float.MaxValue;
            var bestPoint = point;
            var bestNormal = Vector2.zero;
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                var a = polygon[i];
                var b = polygon[next];
                var ab = b - a;
                var length = ab.sqrMagnitude;
                if (length < 0.0001f)
                {
                    continue;
                }
                var t = Mathf.Clamp01(
                    Vector2.Dot(point - a, ab) / length);
                var candidate = a + ab * t;
                // Compare squared distances; the sqrt is only needed once.
                var distanceSquared = (point - candidate).sqrMagnitude;
                if (distanceSquared < bestDistance)
                {
                    bestDistance = distanceSquared;
                    bestPoint = candidate;
                    var normal = new Vector2(-ab.y, ab.x).normalized;
                    // Point inward: toward the polygon centre.
                    if (Vector2.Dot(normal, centre - candidate) < 0.0f)
                    {
                        normal = -normal;
                    }
                    bestNormal = normal;
                }
            }
            return bestPoint + bestNormal * inset;
        }

        /// <summary>
        /// Projects a screen point onto the two dominant axes of the polygon,
        /// yielding a 0..1 fraction along each. Used to turn a pointer drag into
        /// "how far along this box did the user move".
        /// </summary>
        public static Vector2 DominantAxes(IList<Vector2> polygon)
        {
            var left = Vector2.zero;
            var right = Vector2.zero;
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            left = new Vector2(minX, minY);
            right = new Vector2(maxX, maxY);
            return right - left;
        }
    }
}
