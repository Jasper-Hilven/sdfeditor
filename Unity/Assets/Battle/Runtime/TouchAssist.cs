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
        /// in front of it: a teammate or a wall in the way ends the trace on that. The lobbed weapons are left to the player (their
        /// shot is an arc, not the line the crosshair draws).
        /// </summary>
        public static bool OnEnemy(Combat combat, Arsenal arsenal)
        {
            var stats = arsenal != null ? arsenal.Weapon : null;
            var you = arsenal != null ? arsenal.Player : null;
            if (combat == null || stats == null || you == null || you.State == null || !you.State.Alive)
                return false;
            if (stats.mode == FireMode.Cloud || stats.mode == FireMode.Blob)
                return false;
            var hit = combat.Trace(arsenal.EyePosition, arsenal.AimDirection, stats.range, you);
            if (!hit.hit)
                return false;
            if (hit.player != null)
                return hit.player.Team != you.Team && hit.player.State != null && hit.player.State.Alive;
            return hit.target != null && hit.target.Alive && hit.target.Team != you.Team;
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
