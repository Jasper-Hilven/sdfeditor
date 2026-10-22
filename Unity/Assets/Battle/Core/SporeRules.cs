using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>The three powers a full spore meter buys (ticket 25), on the keys Q, F and C by their place on the keyboard.</summary>
    public enum SporePower
    {
        /// <summary>Mycelium Rush: super speed.</summary>
        Rush,
        /// <summary>Regrowth: faster healing.</summary>
        Regrowth,
        /// <summary>Puffball Armour: less damage taken.</summary>
        Armour,
    }

    /// <summary>
    /// The spores' numbers (ticket 25): what fills the meter (a kill 25, an enemy machine 15, returning your flag 15, taking the enemy's
    /// 20, a capture 50, of 100), and each power's effect and length (Rush 1.6 × speed for 12 s, Regrowth 8 health a second for 10 s up
    /// to the overheal limit, Armour half the damage for 12 s). Serialized on the scene's <c>Spores</c> component.
    /// </summary>
    [Serializable]
    public sealed class SporeRules
    {
        [Tooltip("Spores in a full meter")]
        public float full = 100;

        [Header("Gains")]
        [Tooltip("Killing an enemy player")]
        public float kill = 25;
        [Tooltip("Destroying an enemy machine (ticket 24)")]
        public float machine = 15;
        [Tooltip("Sending your own flag home by touching it")]
        public float flagReturn = 15;
        [Tooltip("Taking the enemy flag")]
        public float flagTake = 20;
        [Tooltip("Capturing the enemy flag")]
        public float capture = 50;

        [Header("Mycelium Rush")]
        [Tooltip("What the walking speed is multiplied by (on top of the flag carrier's 0.9)")]
        public float rushSpeed = 1.6f;
        public float rushSeconds = 12;

        [Header("Regrowth")]
        [Tooltip("Points of healing a second, as a health pod's: one health a point up to 100, half above, up to the overheal limit")]
        public float regrowthPerSecond = 8;
        public float regrowthSeconds = 10;

        [Header("Puffball Armour")]
        [Tooltip("What the damage taken is multiplied by")]
        public float armourDamage = 0.5f;
        public float armourSeconds = 12;

        [Header("Bots")]
        [Tooltip("A bot takes Regrowth below this share of its most health")]
        public float botRegrowthBelow = 0.5f;
        [Tooltip("A bot takes Armour fighting an enemy within this many metres")]
        public float botArmourWithin = 15;

        public float SecondsOf(SporePower power) => power switch
        {
            SporePower.Rush => rushSeconds,
            SporePower.Regrowth => regrowthSeconds,
            _ => armourSeconds,
        };

        /// <summary>The power as the HUD writes it.</summary>
        public static string NameOf(SporePower power) => power switch
        {
            SporePower.Rush => "MYCELIUM RUSH",
            SporePower.Regrowth => "REGROWTH",
            _ => "PUFFBALL ARMOUR",
        };

        public static readonly SporePower[] All = { SporePower.Rush, SporePower.Regrowth, SporePower.Armour };

        /// <summary>
        /// The power a bot with a full meter takes now, or null to keep it: Rush on a flag run (carrying it home or going for it),
        /// Regrowth when hurt below <see cref="botRegrowthBelow"/>, Armour fighting close (an enemy within <see cref="botArmourWithin"/>).
        /// </summary>
        public SporePower? ChooseForBot(bool flagRun, float health, float maxHealth, float enemyDistance)
        {
            if (flagRun)
                return SporePower.Rush;
            if (health < maxHealth * botRegrowthBelow)
                return SporePower.Regrowth;
            if (enemyDistance <= botArmourWithin)
                return SporePower.Armour;
            return null;
        }
    }

    /// <summary>One player's spores (ticket 25): how full the meter is, and the power on and how long it has left.</summary>
    public sealed class SporeMeter
    {
        /// <summary>Spores in the meter, 0 to the rules' full.</summary>
        public float Amount { get; internal set; }

        /// <summary>The power on; null when none is.</summary>
        public SporePower? Power { get; internal set; }

        /// <summary>Seconds the power on has left; 0 when none is.</summary>
        public float TimeLeft { get; internal set; }

        public bool Active => Power.HasValue;

        /// <summary>Healing points owed to Regrowth, given in steps of <see cref="CombatState.TickSeconds"/>.</summary>
        internal float RegrowthClock;

        internal void End()
        {
            Power = null;
            TimeLeft = 0;
            RegrowthClock = 0;
        }
    }
}
