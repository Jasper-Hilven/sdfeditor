using System;
using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Where in a side's spawn zone (ticket 20) a player appears: a random spot in the disc, seen from above, at least a clearance away
    /// from everyone standing in it, so nobody spawns inside another player. The zone was checked open and level in the pipeline
    /// (BattleSidesTests), so only the players need avoiding. With no free spot left, the spot furthest from the others.
    /// </summary>
    public static class SpawnPicker
    {
        /// <summary>A player's radius (the controller's); the spot keeps the whole player inside the zone.</summary>
        public const float BodyRadius = 0.3f;

        /// <summary>The least distance between two players' middles at a spawn: two bodies and a little room.</summary>
        public const float Clearance = 2 * BodyRadius + 0.2f;

        /// <param name="centre">The zone's middle, x and z.</param>
        /// <param name="radius">The zone's radius; the player's middle stays <see cref="BodyRadius"/> inside it.</param>
        /// <param name="occupied">Where other players stand, x and z.</param>
        /// <param name="random">Where the candidates come from, so a test can repeat a pick.</param>
        /// <param name="tries">How many random spots to try before taking the least crowded one.</param>
        public static Vector2 Pick(Vector2 centre, float radius, IReadOnlyList<Vector2> occupied, System.Random random, float clearance = Clearance, int tries = 32)
        {
            var reach = Mathf.Max(0, radius - BodyRadius);
            var best = centre;
            var bestRoom = Room(centre, occupied);
            if (bestRoom >= clearance && reach <= 0)
                return centre;
            for (var i = 0; i < tries; i++)
            {
                // Uniform over the disc: the square root keeps the middle from being favoured
                var angle = (float)(random.NextDouble() * 2 * Math.PI);
                var distance = reach * Mathf.Sqrt((float)random.NextDouble());
                var spot = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                var room = Room(spot, occupied);
                if (room >= clearance)
                    return spot;
                if (room > bestRoom)
                    (best, bestRoom) = (spot, room);
            }
            return best;
        }

        /// <summary>The distance from <paramref name="spot"/> to the nearest occupied point; infinity when there is none.</summary>
        public static float Room(Vector2 spot, IReadOnlyList<Vector2> occupied)
        {
            var room = float.PositiveInfinity;
            if (occupied == null)
                return room;
            foreach (var other in occupied)
                room = Mathf.Min(room, Vector2.Distance(spot, other));
            return room;
        }
    }
}
