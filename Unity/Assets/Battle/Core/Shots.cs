using UnityEngine;

namespace SdfBattle
{
    /// <summary>The geometry of shots that needs no physics, so it can be tested: a ray against a player's head.</summary>
    public static class Shots
    {
        /// <summary>
        /// Where a ray from <paramref name="origin"/> along the unit <paramref name="direction"/> first meets an upright ellipsoid (radius
        /// <paramref name="across"/> round, <paramref name="up"/> high), at <paramref name="t"/> metres along it; false when it misses, or
        /// starts inside.
        /// </summary>
        public static bool HitEllipsoid(Vector3 origin, Vector3 direction, Vector3 centre, float across, float up, out float t)
        {
            // Squashed into a unit sphere
            var scale = new Vector3(1 / across, 1 / up, 1 / across);
            var o = Vector3.Scale(origin - centre, scale);
            var d = Vector3.Scale(direction, scale);
            var a = Vector3.Dot(d, d);
            var b = 2 * Vector3.Dot(o, d);
            var c = Vector3.Dot(o, o) - 1;
            t = 0;
            if (c <= 0)
                return false;
            var disc = b * b - 4 * a * c;
            if (disc < 0)
                return false;
            t = (-b - Mathf.Sqrt(disc)) / (2 * a);
            return t >= 0;
        }
    }
}
