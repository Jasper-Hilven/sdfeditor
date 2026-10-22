using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One of the Prime-copter's four spore sacs as something shots hit (ticket 33.5): on a sphere collider hanging with the sac under the
    /// copter's rim, registered with <see cref="Combat.Register"/> so the electro-gun's bolt jumps between the sacs and the flame's
    /// cone finds them. What it takes goes to the copter (<see cref="PrimeCopter.HurtSac"/>), which hands it to
    /// <see cref="BossState.HurtSac"/>: the sac's own 200 health and the bar's. Blue, so friendly fire from the blue crew does nothing.
    /// </summary>
    public sealed class PrimeSac : MonoBehaviour, IDamageable
    {
        private PrimeCopter copter;
        private int index;

        public int Index => index;

        public Team Team => Team.Blue;

        public bool Alive => copter != null && copter.SacAlive(index);

        public Vector3 Center => transform.position;

        public void Bind(PrimeCopter copter, int index)
        {
            this.copter = copter;
            this.index = index;
        }

        public float TakeDamage(Damage damage, Vector3 point) => copter != null ? copter.HurtSac(index, damage) : 0;
    }
}
