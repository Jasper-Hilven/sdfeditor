using SdfLevel;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// What the touch controls ask of the world (ticket 56), all through <see cref="Combat"/>'s own trace, so a teammate or a wall in
    /// the way is what a bullet would meet: whether the crosshair is on an enemy within the weapon's range (the auto-fire), and how much
    /// an enemy near the crosshair slows the look (the aim assist). It never moves the view by itself.
    /// </summary>
    public static class TouchAssist
    {
        /// <summary>Metres above an enemy's feet that the assist aims for: its middle.</summary>
        public const float ChestHeight = 1.1f;

        /// <summary>
        /// The crosshair is on an enemy's body or head, or on an enemy machine, within the weapon in hand's range, and nothing else is
        /// in front of it: a teammate or a wall in the way ends the trace on that. The lobbed weapons use their own arc, the minigun an enemy near the crosshair.
        /// </summary>
        public static bool OnEnemy(Combat combat, Arsenal arsenal) => EnemyUnder(combat, arsenal) != null;

        /// <summary>
        /// The enemy (a player or a machine) under the crosshair as <see cref="OnEnemy"/> sees it, else null. The trace is the one a bullet
        /// takes, along the aim: head and body count alike, nothing is snapped to the head.
        /// </summary>
        public static object EnemyUnder(Combat combat, Arsenal arsenal)
        {
            var stats = arsenal != null ? arsenal.Weapon : null;
            var you = arsenal != null ? arsenal.Player : null;
            if (combat == null || stats == null || you == null || you.State == null || !you.State.Alive)
                return null;
            if (stats.mode == FireMode.Cloud || stats.mode == FireMode.Blob)
                return LobbedEnemy(combat, arsenal, stats, you);
            if (IsMinigun(stats))
                return NearEnemy(combat, arsenal, stats, you);
            var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, stats.range, you);
            if (!hit.hit)
                return null;
            if (hit.player != null)
                return hit.player.Team != you.Team && hit.player.State != null && hit.player.State.Alive ? hit.player : null;
            return hit.target != null && hit.target.Alive && hit.target.Team != you.Team ? hit.target : null;
        }

        /// <summary>The weapon with a spin-up (the minigun): its auto-fire starts on an enemy near the crosshair, see <see cref="TouchRules.MinigunNearScale"/>.</summary>
        public static bool IsMinigun(WeaponStats stats) => stats != null && stats.id == WeaponIds.Minigun;

        /// <summary>The seconds the crosshair must rest on a target before the auto-fire starts, for the weapon in hand.</summary>
        public static float DwellFor(WeaponStats stats) => IsMinigun(stats) ? TouchRules.MinigunDwell : TouchRules.AutoFireDwell;

        private static bool Friendly(Player you, Combat.ShotHit hit) =>
            hit.player != null ? hit.player.Team == you.Team : hit.target != null && hit.target.Team == you.Team;

        /// <summary>
        /// The nearest-to-the-crosshair enemy player in sight, within range and within <see cref="TouchRules.MinigunNearAngle"/> of the
        /// aim, if the line the bullets take (the aim itself) does not first meet a teammate or a wall in front of that enemy. An enemy
        /// right under the crosshair counts like any (the plain trace is tried first). The shots still go along the aim.
        /// </summary>
        private static object NearEnemy(Combat combat, Arsenal arsenal, WeaponStats stats, Player you)
        {
            var eye = arsenal.EyePosition;
            var aim = arsenal.AimDirection;
            var line = combat.Trace(eye, aim, stats.range, you);
            if (line.hit && Friendly(you, line))
                return null;
            if (line.hit && line.player != null && line.player.Team != you.Team && line.player.State != null && line.player.State.Alive)
                return line.player;
            if (line.hit && line.target != null && line.target.Alive && line.target.Team != you.Team)
                return line.target;
            if (combat.Match == null)
                return null;
            object best = null;
            var bestAngle = float.MaxValue;
            foreach (var other in combat.Match.Players)
            {
                if (other == null || other == you || other.State == null || !other.State.Alive || other.Team == you.Team)
                    continue;
                var to = other.transform.position + Vector3.up * ChestHeight - eye;
                var distance = to.magnitude;
                if (distance > stats.range || distance < 0.3f)
                    continue;
                var angle = Vector3.Angle(aim, to);
                if (angle > TouchRules.MinigunNearAngle(distance) || angle >= bestAngle)
                    continue;
                // The enemy in sight (the nearest thing towards its middle is the enemy), and the bullets' line not stopped short by a wall
                var sight = combat.Trace(eye, to / distance, distance + 0.5f, you);
                if (!sight.hit || sight.player != other)
                    continue;
                if (line.hit && line.distance < distance - 1f)
                    continue;
                best = other;
                bestAngle = angle;
            }
            return best;
        }

        /// <summary>
        /// For the lobbed guns: an enemy the shot would hit if fired now, from <see cref="Combat.PredictLob"/> (the projectile's own path,
        /// so a wall or a teammate in the way ends it there). A direct hit on an enemy player or machine counts; the poison cloud also
        /// counts when it lands within its radius of an enemy, unless a teammate stands inside that radius too.
        /// </summary>
        private static object LobbedEnemy(Combat combat, Arsenal arsenal, WeaponStats stats, Player you)
        {
            var hit = combat.PredictLob(arsenal, arsenal.EyePosition, arsenal.AimDirection);
            if (!hit.hit)
                return null;
            if (Friendly(you, hit))
                return null;
            if (hit.player != null)
                return hit.player.State != null && hit.player.State.Alive ? hit.player : null;
            if (hit.target != null)
                return hit.target.Alive ? hit.target : null;
            if (stats.mode != FireMode.Cloud || combat.Match == null)
                return null;
            object enemy = null;
            var reach = stats.areaRadius * 0.8f;
            foreach (var other in combat.Match.Players)
            {
                if (other == null || other == you || other.State == null || !other.State.Alive)
                    continue;
                var d = Vector3.Distance(other.transform.position + Vector3.up, hit.point);
                if (other.Team == you.Team)
                {
                    if (d < stats.areaRadius + 1)
                        return null;
                }
                else if (d <= reach && enemy == null)
                    enemy = other;
            }
            return enemy;
        }

        /// <summary>
        /// The share of the look that is left: 1 with no enemy near the crosshair, down to <see cref="TouchRules.AssistSlowest"/> with
        /// one on it. Enemies in sight only (the trace from the eye to their middle ends on them), within the weapon's range.
        /// </summary>
        public static float LookScale(Combat combat, Arsenal arsenal)
        {
            var stats = arsenal != null ? arsenal.Weapon : null;
            var you = arsenal != null ? arsenal.Player : null;
            if (combat == null || you == null || you.State == null || !you.State.Alive || combat.Match == null)
                return 1;
            var range = stats != null ? stats.range : 100;
            var eye = arsenal.EyePosition;
            var aim = arsenal.AimDirection;
            var scale = 1f;
            foreach (var other in combat.Match.Players)
            {
                if (other == null || other == you || other.State == null || !other.State.Alive || other.Team == you.Team)
                    continue;
                var middle = other.transform.position + Vector3.up * ChestHeight;
                var to = middle - eye;
                var distance = to.magnitude;
                if (distance > range || distance < 0.3f)
                    continue;
                var angle = Vector3.Angle(aim, to);
                var s = TouchRules.AssistScale(angle, distance);
                if (s >= scale)
                    continue;
                // In sight: the nearest thing on the way to its middle is the enemy itself
                var hit = combat.Trace(eye, to / distance, distance + 0.5f, you);
                if (hit.hit && hit.player == other)
                    scale = s;
            }
            return scale;
        }
    }
}
