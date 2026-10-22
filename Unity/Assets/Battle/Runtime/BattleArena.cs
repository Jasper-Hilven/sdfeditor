using System;
using SdfPickups;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// A level's battle layout (ticket 17), on the scene's Battle object: its two sides (<see cref="BattleSide"/>, on Battle/West and
    /// Battle/East) and the neutral pods between them (under Battle/Neutral), one long-range and one short-range. Filled by the level
    /// builder (SdfLevel.Editor.LevelBuilder) from the level file on every rebuild, never edited by hand.
    /// </summary>
    public sealed class BattleArena : MonoBehaviour
    {
        public const string ObjectName = "Battle";

        public BattleSide west;
        public BattleSide east;
        [Tooltip("The neutral pods between the sides: one of sniper or electro-gun, one of flamethrower, poison-gun or blob-gun")]
        public WeaponPickup[] neutralPods = Array.Empty<WeaponPickup>();

        /// <summary>The battle layout of the active level scene; null where there is none.</summary>
        public static BattleArena Current { get; private set; }

        public BattleSide Side(BattleSideId id) => id == BattleSideId.West ? west : east;

        /// <summary>The side facing <paramref name="id"/>'s.</summary>
        public BattleSide Opponent(BattleSideId id) => id == BattleSideId.West ? east : west;

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        // Every pod glows, so it is seen from afar
        private void Start()
        {
            foreach (var side in new[] { west, east })
            {
                if (side == null)
                    continue;
                foreach (var pod in side.WeaponPods)
                    PodBeacon.Add(pod, false);
                foreach (var pod in side.HealthPods)
                    PodBeacon.Add(pod);
            }
            foreach (var pod in neutralPods)
                PodBeacon.Add(pod, true);
        }
    }
}
