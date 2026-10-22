using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>The three kinds of machine that fight for a side (ticket 24).</summary>
    public enum MachineKind
    {
        Tank,
        Helicopter,
        Turret,
    }

    /// <summary>
    /// One kind of machine's numbers (ticket 24, the defaults the user accepted): health (tank 60, helicopter 60, turret 90), range
    /// (turret 30 m, tank 25 m, helicopter 35 m), half the pistol's damage a hit (6) about once a second, back 30 s after dying.
    /// </summary>
    [Serializable]
    public sealed class MachineStats
    {
        public MachineKind kind;
        [Tooltip("Hit points at each (re)spawn")]
        public float health = 60;
        [Tooltip("Metres it sees and shoots across")]
        public float range = 25;
        [Tooltip("Damage of a hit: half the pistol's 12")]
        public float damage = 6;
        [Tooltip("Seconds between shots, about one a second (each wait varied by a little)")]
        public float shotInterval = 1;
        [Tooltip("Seconds dead before it is back")]
        public float respawnSeconds = 30;
        [Tooltip("The weapon the kill feed names (no icon)")]
        public string weapon = "cannon";
        [Tooltip("Degrees a second its gun (the tank's turret, the turret's head, the helicopter's body) turns")]
        public float turnSpeed = 90;
        [Tooltip("Metres a second it moves at most (0 for the turret)")]
        public float speed;

        public MachineStats()
        {
        }

        public MachineStats(MachineKind kind, float health, float range, float turnSpeed, float speed)
        {
            this.kind = kind;
            this.health = health;
            this.range = range;
            this.turnSpeed = turnSpeed;
            this.speed = speed;
        }

        /// <summary>The first balance of a kind.</summary>
        public static MachineStats Of(MachineKind kind) => kind switch
        {
            MachineKind.Tank => new MachineStats(MachineKind.Tank, 60, 25, 100, 2.2f),
            MachineKind.Helicopter => new MachineStats(MachineKind.Helicopter, 60, 35, 90, 5),
            _ => new MachineStats(MachineKind.Turret, 90, 30, 70, 0),
        };
    }

    /// <summary>
    /// The machines' rules (ticket 24): per side one helicopter, two tanks and four turrets (the turrets are the level's, the others made
    /// at the start of the match); each kind's <see cref="MachineStats"/>; how well they aim (the bots' <see cref="BotSkill"/>, ticket 23:
    /// reaction time and aim error, so they are a nuisance, not a wall). Serialized on the scene's <c>Machines</c> component.
    /// </summary>
    [Serializable]
    public sealed class MachineRules
    {
        public int helicoptersPerSide = 1;
        public int tanksPerSide = 2;
        public MachineStats tank = MachineStats.Of(MachineKind.Tank);
        public MachineStats helicopter = MachineStats.Of(MachineKind.Helicopter);
        public MachineStats turret = MachineStats.Of(MachineKind.Turret);
        [Tooltip("How well they aim: the bots' skill (reaction time, aim error)")]
        public BotSkill aim = new BotSkill(0.45f);

        public MachineStats StatsOf(MachineKind kind) => kind switch
        {
            MachineKind.Tank => tank,
            MachineKind.Helicopter => helicopter,
            _ => turret,
        };

        /// <summary>The machine as the kill feed writes it: "Blue tank", "Red helicopter", "Red turret".</summary>
        public static string NameOf(MachineKind kind, Team team) => $"{team.Name()} {kind.ToString().ToLowerInvariant()}";
    }
}
