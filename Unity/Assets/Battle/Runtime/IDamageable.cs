using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Something shots hurt that is not a player (ticket 22's hook for 24's machines): a component on (or above) its colliders, with a
    /// team and its own <see cref="Health"/>. <see cref="Combat"/> finds it behind a collider a shot, a bolt's jump, the flame or a
    /// blob hits, checks the team (<see cref="CombatState.CanHurt(Team, Team)"/>: friendly fire off, nothing in the countdown) and hands
    /// it the damage; it is never in the kill feed as a victim and gives no kill credit. Register it with <see cref="Combat.Register"/>
    /// so the electro-gun's jump and the flamethrower's cone, which look for targets near a point, find it too.
    /// </summary>
    public interface IDamageable
    {
        Team Team { get; }

        bool Alive { get; }

        /// <summary>Where a bolt jumps to and the flame aims at: about its middle.</summary>
        Vector3 Center { get; }

        /// <summary>Takes the damage at <paramref name="point"/>; returns the health it took (0 when it is already dead).</summary>
        float TakeDamage(Damage damage, Vector3 point);
    }
}
