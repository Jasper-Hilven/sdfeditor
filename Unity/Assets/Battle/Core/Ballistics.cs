using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Where to aim a lobbed weapon (ticket 22's poison- and blob-gun) so its shot lands on a point (ticket 23's bots): the low arc for a
    /// launch speed and gravity, and the aim that gives it, as the shot leaves <see cref="WeaponStats.projectileLift"/> degrees above
    /// the aim. Plain math, tested.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>
        /// The launch elevation, radians above level, of the low arc that reaches a point <paramref name="across"/> metres away level and
        /// <paramref name="up"/> metres higher (negative: lower) at <paramref name="speed"/> m/s under <paramref name="gravity"/>; false
        /// when it is out of reach (then the 45-degree elevation, which goes furthest).
        /// </summary>
        public static bool LowArc(float across, float up, float speed, float gravity, out float elevation)
        {
            across = Mathf.Max(0.01f, across);
            if (gravity <= 0)
            {
                elevation = Mathf.Atan2(up, across);
                return true;
            }
            var v2 = speed * speed;
            var root = v2 * v2 - gravity * (gravity * across * across + 2 * up * v2);
            if (root < 0)
            {
                elevation = Mathf.PI / 4;
                return false;
            }
            elevation = Mathf.Atan((v2 - Mathf.Sqrt(root)) / (gravity * across));
            return true;
        }

        /// <summary>Seconds the shot takes to cover <paramref name="across"/> metres level, leaving at <paramref name="elevation"/>.</summary>
        public static float FlightTime(float across, float speed, float elevation) =>
            Mathf.Max(0, across) / Mathf.Max(0.01f, speed * Mathf.Cos(elevation));

        /// <summary>
        /// The aim's pitch (degrees, positive down, as the controller's) that lands a weapon's lobbed shot from <paramref name="from"/>
        /// on <paramref name="to"/>; false when out of reach (the pitch then throws it as far as it goes).
        /// </summary>
        public static bool AimPitch(WeaponStats weapon, Vector3 from, Vector3 to, out float pitch, out float flightTime)
        {
            var delta = to - from;
            var across = new Vector2(delta.x, delta.z).magnitude;
            var reachable = LowArc(across, delta.y, weapon.projectileSpeed, weapon.projectileGravity, out var elevation);
            flightTime = FlightTime(across, weapon.projectileSpeed, elevation);
            pitch = -(elevation * Mathf.Rad2Deg - weapon.projectileLift);
            return reachable;
        }

        /// <summary>Where a shot from <paramref name="from"/> at <paramref name="velocity"/> is after <paramref name="seconds"/>.</summary>
        public static Vector3 PositionAfter(Vector3 from, Vector3 velocity, float gravity, float seconds) =>
            from + velocity * seconds + Vector3.down * (0.5f * gravity * seconds * seconds);
    }
}
