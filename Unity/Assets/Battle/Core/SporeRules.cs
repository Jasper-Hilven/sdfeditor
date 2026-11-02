using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>
    /// Ticket 49 note: the spore powers are the monster powers (the rage meter, Frenzy, Regenerate and Thick Hide). The power enum and
    /// its rule fields are renamed; the internal type names (<c>SporeRules</c>, <c>SporeState</c>, <c>Spores</c>, <c>SporeHud</c>, the
    /// <c>GuiHud</c> spore fields, the <c>spore.*</c> audio cues and clip file names) keep "Spore", as renaming them only churns prefabs.
    /// No player sees them.
    /// </summary>
    /// <summary>The three powers a full rage meter buys (ticket 25), on the keys Q, F and C by their place on the keyboard.</summary>
    public enum SporePower
    {
        /// <summary>Frenzy: super speed.</summary>
        Frenzy,
        /// <summary>Regenerate: faster healing.</summary>
        Regenerate,
        /// <summary>Thick Hide: less damage taken.</summary>
        ThickHide,
    }

    /// <summary>
    /// The spores' numbers (ticket 25): what fills the meter (a kill 25, an enemy machine 15, returning your flag 15, taking the enemy's
    /// 20, a capture 50, of 100), and each power's effect and length (Frenzy 1.6 × speed for 24 s, Regenerate 8 health a second for 20 s up
    /// to the overheal limit, Thick Hide half the damage for 24 s; doubled from 12 / 10 / 12 s on 2026-10-23). Serialized on the scene's <c>Spores</c> component.
    /// </summary>
    [Serializable]
    public sealed class SporeRules
    {
        [Tooltip("Rage in a full meter")]
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

        [Header("Boss (ticket 33)")]
        [Tooltip("Hurting Chanterelle Prime: spores per 100 damage dealt")]
        public float bossDamagePer100 = 1.5f;
        [Tooltip("Bursting one of the Prime-copter's spore sacs")]
        public float bossSac = 15;
        [Tooltip("Bursting one of Prime's hands")]
        public float bossHand = 20;
        [Tooltip("Starting a stun window: the capture that drops the shield, or the sac that crashes the copter")]
        public float bossStun = 25;

        [Header("Frenzy")]
        [Tooltip("What the walking speed is multiplied by (on top of the flag carrier's 0.9)")]
        public float frenzySpeed = 1.6f;
        public float frenzySeconds = 24;

        [Header("Regenerate")]
        [Tooltip("Points of healing a second, as a health pod's: one health a point up to 100, half above, up to the overheal limit")]
        public float regeneratePerSecond = 8;
        public float regenerateSeconds = 20;

        [Header("Thick Hide")]
        [Tooltip("What the damage taken is multiplied by")]
        public float thickHideDamage = 0.5f;
        public float thickHideSeconds = 24;

        [Header("Bots")]
        [Tooltip("A bot takes Regenerate below this share of its most health")]
        public float botRegenerateBelow = 0.5f;
        [Tooltip("A bot takes Thick Hide fighting an enemy within this many metres")]
        public float botThickHideWithin = 15;

        public float SecondsOf(SporePower power) => power switch
        {
            SporePower.Frenzy => frenzySeconds,
            SporePower.Regenerate => regenerateSeconds,
            _ => thickHideSeconds,
        };

        /// <summary>The power as the HUD writes it.</summary>
        public static string NameOf(SporePower power) => power switch
        {
            SporePower.Frenzy => "FRENZY",
            SporePower.Regenerate => "REGENERATE",
            _ => "THICK HIDE",
        };

        public static readonly SporePower[] All = { SporePower.Frenzy, SporePower.Regenerate, SporePower.ThickHide };

        /// <summary>
        /// The power a bot with a full meter takes now, or null to keep it: Rush on a flag run (carrying it home or going for it),
        /// Regenerate when hurt below <see cref="botRegenerateBelow"/>, Thick Hide fighting close (an enemy within <see cref="botThickHideWithin"/>).
        /// </summary>
        public SporePower? ChooseForBot(bool flagRun, float health, float maxHealth, float enemyDistance)
        {
            if (flagRun)
                return SporePower.Frenzy;
            if (health < maxHealth * botRegenerateBelow)
                return SporePower.Regenerate;
            if (enemyDistance <= botThickHideWithin)
                return SporePower.ThickHide;
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

        /// <summary>Healing points owed to Regenerate, given in steps of <see cref="CombatState.TickSeconds"/>.</summary>
        internal float RegenerateClock;

        internal void End()
        {
            Power = null;
            TimeLeft = 0;
            RegenerateClock = 0;
        }
    }
}
