using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// One of Chanterelle Prime's hit shapes (ticket 33.6): a collider on his rig or on the spore shield with this beside it, so
    /// <see cref="Combat"/> finds an <see cref="IDamageable"/> behind a shot, a bolt's jump or the flame. Prime has no mesh colliders: a
    /// capsule for the stem, a sphere for the cap, one under it for the gills, a sphere for each eye and each hand (6 shapes and the
    /// shield's sphere, tested by the physics engine, nothing per frame). What a hit does is the boss's: <see cref="Boss.HurtPart"/> gives
    /// the part's multiplier (the gills ×2, the eyes ×2 and shut in a wind-up, the body ×1), the shield its spark and no damage.
    /// </summary>
    public sealed class PrimeHit : MonoBehaviour, IDamageable
    {
        /// <summary>Which part of the boss this is.</summary>
        public enum Part
        {
            /// <summary>The stem and the cap, the cape: anywhere else.</summary>
            Body,
            /// <summary>Under the cap: the weak point of phases 1 and 2, and of a stuck cap in phase 3.</summary>
            Gills,
            /// <summary>Phase 3's weak point, shut during a wind-up.</summary>
            Eyes,
            /// <summary>One of the two hands (phase 3), with health of their own.</summary>
            Hand,
            /// <summary>The spore shield's dome (phase 1): stops everything.</summary>
            Shield,
        }

        public Part part;
        [Tooltip("Which hand (0 or 1) or eye")]
        public int index;

        private Boss boss;

        public void Bind(Boss owner, Part kind, int number = 0)
        {
            boss = owner;
            part = kind;
            index = number;
        }

        public Team Team => Team.Blue;

        public bool Alive => boss != null && boss.PartAlive(part, index);

        public Vector3 Center
        {
            get
            {
                var collider = GetComponent<Collider>();
                return collider != null ? collider.bounds.center : transform.position;
            }
        }

        public float TakeDamage(Damage damage, Vector3 point) => boss != null ? boss.HurtPart(part, index, damage, point) : 0;
    }
}
