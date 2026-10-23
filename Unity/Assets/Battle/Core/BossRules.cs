using System;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>The difficulties of the boss fight (ticket 32's Super Easy / Easy / Normal / Hard; <c>SdfCampaign.Difficulty</c> converts by number).</summary>
    public enum BossDifficulty
    {
        SuperEasy = 0,
        Easy = 1,
        Normal = 2,
        Hard = 3,
    }

    /// <summary>The attacks of Chanterelle Prime (ticket 33, section 6).</summary>
    public enum BossAttackKind
    {
        /// <summary>Phase 1: three lobbed spore balls at 42 and up to two red bots.</summary>
        SporeMortar,
        /// <summary>Phases 1 and 3: a beam that follows its target.</summary>
        RoyalGlare,
        /// <summary>Phase 2: twelve hitscan shots from the copter.</summary>
        CapCannon,
        /// <summary>Phase 2: the copter dives and Prime's hands slam.</summary>
        DowndraftDive,
        /// <summary>Phase 2: a blue tank is dropped.</summary>
        TankDrop,
        /// <summary>Phase 3: a shockwave ring; the cap is stuck afterwards.</summary>
        CapSlam,
        /// <summary>Phase 3: a hand grabs whoever is in its shadow.</summary>
        HandGrab,
    }

    /// <summary>Where on Prime a hit lands.</summary>
    public enum BossPart
    {
        /// <summary>Anywhere else: stem, cap, cape.</summary>
        Body,
        /// <summary>Under the cap: the weak point of phases 1 and 2, and of a stuck cap in phase 3.</summary>
        Gills,
        /// <summary>Phase 3's weak point; shut during a wind-up.</summary>
        Eyes,
    }

    /// <summary>
    /// One attack's numbers at Normal (ticket 33, section 6): the seconds of each stage, the cooldown between starts (the attack's start to
    /// the next one's), the damage and the like. Damage is per hit (per tick of a lingering effect where the attack says so, and per
    /// second for the glare). <see cref="BossRules.SpecOf"/> scales it by the difficulty.
    /// </summary>
    [Serializable]
    public struct BossAttackSpec
    {
        public BossAttackKind kind;
        [Tooltip("The telegraph: pose, eye glow and ground marker, before anything happens")]
        public float windUp;
        [Tooltip("The effect: the glare's beam, the cannon's burst, the ring's travel")]
        public float active;
        [Tooltip("Afterwards, before Prime does anything else; the slam's is the stuck cap")]
        public float recover;
        [Tooltip("Seconds from one start to the next")]
        public float cooldown;
        [Tooltip("Damage to a player (0 when the attack hurts by other means)")]
        public float damage;
        [Tooltip("Metres a hit pushes or throws")]
        public float push;
        [Tooltip("Shots or balls, or the ring's reach in metres")]
        public float count;

        public BossAttackSpec(BossAttackKind kind, float windUp, float active, float recover, float cooldown, float damage, float push = 0, float count = 1)
        {
            this.kind = kind;
            this.windUp = windUp;
            this.active = active;
            this.recover = recover;
            this.cooldown = cooldown;
            this.damage = damage;
            this.push = push;
            this.count = count;
        }

        /// <summary>Seconds from the start of the wind-up to the end of the recovery.</summary>
        public float Total => windUp + active + recover;
    }

    /// <summary>
    /// Every number of the boss fight (ticket 33): Prime's three bars, the damage and wind-up scales, the weak points, the stun windows,
    /// the crew, 42's lives, the attacks. The fields hold Normal; <see cref="For"/> builds the numbers of a difficulty (ticket 33, section
    /// 7), RETRY EASIER included. Serialized on the scene's <c>Boss</c> component like <c>MachineRules</c>.
    /// </summary>
    [Serializable]
    public sealed class BossRules
    {
        public BossDifficulty difficulty = BossDifficulty.Normal;
        [Tooltip("RETRY EASIER: Easy's damage and wind-up rows on top of the chosen difficulty's health")]
        public bool retryEasier;

        [Header("Prime")]
        [Tooltip("The three bars: the Royal Box, the Prime-copter, the Crown. Tuned in 33.9 by BossBalanceTests (the first guesses, 1500 / 1500 / 2000, gave a 3.5 minute fight; Easy keeps Normal's bars, its ease is in the damage, the wind-ups, the windows and the lives)")]
        public float[] bars = { 1800, 4500, 14000 };
        [Tooltip("What Prime's damage to players is multiplied by")]
        public float damageScale = 1;
        [Tooltip("What the wind-ups are multiplied by (longer on Easy)")]
        public float windUpScale = 1;
        [Tooltip("What the stun windows are multiplied by (longer on Easy)")]
        public float stunScale = 1;

        [Header("Weak points")]
        public float gillsMultiplier = 2;
        public float eyesMultiplier = 2;
        [Tooltip("Phase 3: damage taken while Prime wears the crown")]
        public float immunityMultiplier = 0.5f;
        [Tooltip("The champion's minigun against Prime only")]
        public float championMinigunMultiplier = 1.5f;
        public int sacCount = 4;
        public float sacHealth = 400;
        [Tooltip("Sacs that grow back when the daze ends with the bar not empty")]
        public int sacRegrow = 2;
        public float handHealth = 300;
        public float handRegrowSeconds = 20;

        [Header("Windows (at Normal)")]
        [Tooltip("Phase 1: kneeling after a red capture")]
        public float kneelSeconds = 12;
        [Tooltip("Phase 2: from the last sac to the wreck, a warning")]
        public float crashSeconds = 2;
        [Tooltip("Phase 2: dazed on the wreck")]
        public float dazeSeconds = 10;
        [Tooltip("Phase 3: a burst hand")]
        public float handStunSeconds = 4;
        [Tooltip("Phase 1: a blue capture heals this much, at most to the bar's top")]
        public float blueCaptureHeal = 250;
        [Tooltip("Phase 3: Sore Loser below this share of the bar")]
        public float soreLoserBelow = 0.25f;
        [Tooltip("Phase 3: what the wind-ups are multiplied by in Sore Loser")]
        public float soreLoserWindUp = 0.8f;
        [Tooltip("Seconds of damage that count for Prime's target choice in phase 3")]
        public float recentDamageSeconds = 10;
        [Tooltip("Metres: the slam's reach; phase 3's glare is for targets farther than this")]
        public float closeRange = 20;

        [Header("Prime-copter (phase 2, ticket 33.5)")]
        [Tooltip("Metres over the floor the copter's body cruises at: lowest and highest (Prime's feet hang 7 m below)")]
        public float copterMinHeight = 14;
        public float copterMaxHeight = 18;
        [Tooltip("Metres it keeps over the highest thing under or ahead of it, whatever the cruise")]
        public float copterClearance = 9;
        [Tooltip("Metres a second it flies at, and metres inside the wall (the play area's edge) it stays")]
        public float copterSpeed = 6;
        public float copterMargin = 8;
        [Tooltip("The dive: metres over the floor the copter goes down to, and the circle's radius on the floor")]
        public float diveHeight = 6;
        public float diveRadius = 6;
        [Tooltip("The crash on the wreck's place: damage (at Normal) to everyone within the radius, in metres")]
        public float crashDamage = 50;
        public float crashRadius = 8;

        [Header("Crew and lives")]
        public float crewSkill = 0.7f;
        [Tooltip("Blue scouts in phases 1, 2 and 3")]
        public int[] crewSizes = { 4, 2, 2 };
        [Tooltip("Seconds a dead blue scout waits in phases 1, 2 and 3")]
        public float[] crewRespawnSeconds = { 8, 8, 10 };
        [Tooltip("Scouts that arrive with Sore Loser")]
        public int soreLoserCrew = 2;
        [Tooltip("42's lives in each phase")]
        public int lives = 3;
        [Tooltip("Tanks Prime's drop keeps alive at most")]
        public int tanksMax = 2;
        [Tooltip("The red bots' skill, and the seconds they wait to respawn (they never run out of lives)")]
        public float redBotSkill = 0.6f;
        public float redBotRespawnSeconds = 5;

        [Header("Attacks (at Normal)")]
        public BossAttackSpec[] attacks = DefaultAttacks();
        [Tooltip("The glare follows its target at this many degrees a second")]
        public float glareTurnRate = 25;
        [Tooltip("The mortar's cloud after landing: damage a second, and seconds")]
        public float mortarCloudPerSecond = 8;
        public float mortarCloudSeconds = 3;
        [Tooltip("Seconds into a phase before an attack's first start; the glare and the tank drop wait longer")]
        public float openingSeconds = 2;
        public float openingGlareSeconds = 6;
        public float openingTankSeconds = 10;

        public float BarMax(int bar) => bars[Math.Max(0, Math.Min(bars.Length - 1, bar))];

        public float TotalHealth
        {
            get
            {
                var sum = 0f;
                foreach (var bar in bars)
                    sum += bar;
                return sum;
            }
        }

        public int CrewSize(BossPhase phase) => crewSizes[Math.Max(0, Math.Min(crewSizes.Length - 1, phase.BarOf()))];

        /// <summary>Blue scouts in play in a fight phase: its crew, and with Sore Loser (phase 3) the scouts that arrive with it.</summary>
        public int CrewCount(BossPhase phase, bool soreLoser) => CrewSize(phase) + (soreLoser && phase == BossPhase.Phase3 ? soreLoserCrew : 0);

        /// <summary>The most blue scouts any phase has in play: the crew the match must make bodies for.</summary>
        public int MostCrew
        {
            get
            {
                var most = 0;
                foreach (var phase in new[] { BossPhase.Phase1, BossPhase.Phase2, BossPhase.Phase3 })
                    most = Math.Max(most, CrewCount(phase, true));
                return most;
            }
        }

        public float CrewRespawn(BossPhase phase) => crewRespawnSeconds[Math.Max(0, Math.Min(crewRespawnSeconds.Length - 1, phase.BarOf()))];

        /// <summary>The crash's damage after the difficulty's scale.</summary>
        public float CrashDamage => crashDamage * damageScale;

        /// <summary>Seconds a window lasts after the difficulty's scale.</summary>
        public float Window(float seconds) => seconds * stunScale;

        /// <summary>
        /// An attack's numbers for this fight: the wind-up scaled by the difficulty (and by Sore Loser), the damage by the difficulty, the
        /// slam's stuck cap by the stun scale.
        /// </summary>
        public BossAttackSpec SpecOf(BossAttackKind kind, bool soreLoser = false)
        {
            var spec = default(BossAttackSpec);
            foreach (var a in attacks)
                if (a.kind == kind)
                    spec = a;
            spec.kind = kind;
            spec.windUp *= windUpScale * (soreLoser ? soreLoserWindUp : 1);
            spec.damage *= damageScale;
            if (kind == BossAttackKind.CapSlam)
                spec.recover *= stunScale;
            return spec;
        }

        /// <summary>
        /// What the attack does to a player: the shockwave only hurts one who is on the ground when the ring passes (a jump clears
        /// it); everything else always does.
        /// </summary>
        public float DamageTo(BossAttackKind kind, bool grounded, bool soreLoser = false) =>
            kind == BossAttackKind.CapSlam && !grounded ? 0 : SpecOf(kind, soreLoser).damage;

        /// <summary>Seconds into a phase before an attack's first start.</summary>
        public float Opening(BossAttackKind kind) => kind switch
        {
            BossAttackKind.RoyalGlare => openingGlareSeconds,
            BossAttackKind.TankDrop => openingTankSeconds,
            _ => openingSeconds,
        };

        /// <summary>The attacks a fight phase uses, in the order the planner prefers them.</summary>
        public static BossAttackKind[] AttacksOf(BossPhase phase) => phase switch
        {
            BossPhase.Phase1 => new[] { BossAttackKind.RoyalGlare, BossAttackKind.SporeMortar },
            BossPhase.Phase2 => new[] { BossAttackKind.TankDrop, BossAttackKind.DowndraftDive, BossAttackKind.CapCannon },
            BossPhase.Phase3 => new[] { BossAttackKind.CapSlam, BossAttackKind.HandGrab, BossAttackKind.RoyalGlare },
            _ => Array.Empty<BossAttackKind>(),
        };

        public static BossAttackSpec[] DefaultAttacks() => new[]
        {
            // 3 balls, 15 each on landing, then the cloud
            new BossAttackSpec(BossAttackKind.SporeMortar, 1.2f, 1.0f, 0.5f, 8, 15, 0, 3),
            // 30 a second while on you, 3 s long
            new BossAttackSpec(BossAttackKind.RoyalGlare, 1.0f, 3.0f, 0.5f, 18, 30),
            // 12 shots of 8 over 2 s
            new BossAttackSpec(BossAttackKind.CapCannon, 0.8f, 2.0f, 0.5f, 7, 8, 0, 12),
            // 30 in the circle and a push of 6 m; the climb back is the recovery
            new BossAttackSpec(BossAttackKind.DowndraftDive, 1.5f, 1.0f, 2.0f, 12, 30, 6),
            new BossAttackSpec(BossAttackKind.TankDrop, 1.0f, 0.5f, 0.5f, 35, 0),
            // the ring runs to 20 m at 12 m/s; then the cap is stuck for 3 s
            new BossAttackSpec(BossAttackKind.CapSlam, 1.2f, 20f / 12f, 3.0f, 9, 35, 8, 20),
            // 40 and a throw of 8 m
            new BossAttackSpec(BossAttackKind.HandGrab, 1.0f, 1.0f, 0.5f, 10, 40, 8),
        };

        /// <summary>The rules of a difficulty (ticket 33, section 7); <paramref name="retryEasier"/> puts Easy's damage and wind-up rows on top.</summary>
        public static BossRules For(BossDifficulty difficulty, bool retryEasier = false)
        {
            var rules = new BossRules { difficulty = difficulty, retryEasier = retryEasier };
            switch (difficulty)
            {
                case BossDifficulty.SuperEasy:
                    // Easy's step from Normal once more: the bars stay, the rest goes one step further
                    rules.bars = new float[] { 1800, 4500, 14000 };
                    rules.damageScale = 0.2f;
                    rules.windUpScale = 1.6f;
                    rules.stunScale = 1.5f;
                    rules.crewSkill = 0.4f;
                    rules.crewSizes = new[] { 2, 2, 2 };
                    rules.lives = 7;
                    break;
                case BossDifficulty.Easy:
                    rules.bars = new float[] { 1800, 4500, 14000 };
                    rules.damageScale = 0.6f;
                    rules.windUpScale = 1.3f;
                    rules.stunScale = 1.25f;
                    rules.crewSkill = 0.55f;
                    rules.crewSizes = new[] { 3, 2, 2 };
                    rules.lives = 5;
                    break;
                case BossDifficulty.Hard:
                    rules.bars = new float[] { 2250, 5650, 17500 };
                    rules.damageScale = 1.3f;
                    rules.windUpScale = 0.8f;
                    rules.stunScale = 0.85f;
                    rules.crewSkill = 0.85f;
                    rules.crewSizes = new[] { 4, 3, 3 };
                    rules.lives = 2;
                    break;
            }
            if (retryEasier)
                rules.MakeEasier();
            return rules;
        }

        /// <summary>
        /// RETRY EASIER (ticket 33.7): Easy's damage and wind-up rows from now on, on the same object, so a running fight (whose state holds
        /// these rules) takes them at once. The health stays the chosen difficulty's.
        /// </summary>
        public void MakeEasier()
        {
            retryEasier = true;
            damageScale = Math.Min(damageScale, 0.6f);
            windUpScale = Math.Max(windUpScale, 1.3f);
        }
    }
}
