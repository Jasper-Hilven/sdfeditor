using System.Collections.Generic;
using UnityEngine;

namespace SdfBattle
{
    /// <summary>What a bot is out to do (ticket 23).</summary>
    public enum BotGoal
    {
        None,
        /// <summary>Carrying the enemy flag: back to its own stand (and wait there until its own flag is home).</summary>
        BringFlagHome,
        /// <summary>To the enemy flag, on its stand or where it was dropped, to take it.</summary>
        TakeFlag,
        /// <summary>Its own flag lies dropped: touch it to send it home.</summary>
        ReturnFlag,
        /// <summary>Its own flag is carried: after the carrier, to kill them.</summary>
        ChaseCarrier,
        /// <summary>Its own flag is home: stay round its stand.</summary>
        DefendFlag,
        /// <summary>Look for enemies and fight them.</summary>
        Hunt,
        /// <summary>To a weapon pod whose weapon it does not hold.</summary>
        FetchWeapon,
        /// <summary>To a health pod, hurt or for the overheal.</summary>
        FetchHealth,
    }

    /// <summary>A pod a bot could walk to.</summary>
    public struct PodOption
    {
        /// <summary>The pod's index in the scene's list of pods (the bot's own numbering).</summary>
        public int index;
        /// <summary>A health pod; else a weapon pod.</summary>
        public bool health;
        /// <summary>A weapon pod's weapon id.</summary>
        public string weapon;
        /// <summary>Metres to it.</summary>
        public float distance;
        /// <summary>It has its weapon or cross now.</summary>
        public bool available;
        /// <summary>Walking onto it would take something (a weapon not held, health under the overheal limit).</summary>
        public bool useful;
    }

    /// <summary>What a bot knows when it picks a goal: its own state, the flags, the enemies it sees, the pods.</summary>
    public sealed class BotSituation
    {
        public bool carrying;
        public FlagStatus ownFlag = FlagStatus.Home;
        public FlagStatus enemyFlag = FlagStatus.Home;
        /// <summary>Metres to its own stand, to the enemy flag (wherever it is), to its own flag (wherever it is).</summary>
        public float toOwnStand, toEnemyFlag, toOwnFlag;
        public float health = 100, maxHealth = 100, overheal = 200;
        /// <summary>Weapon slots filled (1: the single pistol only).</summary>
        public int weaponsHeld = 1;
        public bool enemyInSight;
        /// <summary>The enemy carrying its own flag is in sight.</summary>
        public bool carrierInSight;
        public readonly List<PodOption> pods = new List<PodOption>();
    }

    /// <summary>A bot's goal and, for fetching, which pod; with the score it won by.</summary>
    public struct BotDecision
    {
        public BotGoal goal;
        public int pod;
        public float score;

        public BotDecision(BotGoal goal, int pod = -1, float score = 0)
        {
            this.goal = goal;
            this.pod = pod;
            this.score = score;
        }

        public bool Same(BotDecision other) => goal == other.goal && (pod == other.pod || (goal != BotGoal.FetchWeapon && goal != BotGoal.FetchHealth));

        public override string ToString() => pod >= 0 ? $"{goal} (pod {pod})" : goal.ToString();
    }

    /// <summary>
    /// How a bot picks its goal (ticket 23): each goal the situation allows is scored from the bot's <see cref="Temperament"/>, how
    /// urgent it is (its flag gone, low health) and how near (nearer is better), plus a little chance, and the best wins; the goal it
    /// already has gets a bonus so it does not flip back and forth. A carrier always heads home. No team plan: two bots of a team may
    /// well pick the same goal. Plain C#; the scene's bot asks again every few seconds and on events (its flag taken, a carrier seen, low
    /// health, a goal reached).
    /// </summary>
    public static class BotBrain
    {
        /// <summary>The bonus of the goal a bot already has.</summary>
        public const float KeepBonus = 0.2f;

        /// <summary>Health under which a bot counts as hurt enough to think again.</summary>
        public const float LowHealth = 40;

        /// <summary>The best goal now; <paramref name="current"/> is the bot's goal so far (its bonus), <paramref name="jitter"/> the chance.</summary>
        public static BotDecision Choose(in Temperament t, BotSituation s, BotDecision current, System.Random random = null, float jitter = 0.1f)
        {
            if (s.carrying)
                return new BotDecision(BotGoal.BringFlagHome, -1, 10);

            var best = new BotDecision(BotGoal.Hunt, -1, float.NegativeInfinity);
            void Offer(BotGoal goal, float score, int pod = -1)
            {
                if (score <= 0)
                    return;
                var decision = new BotDecision(goal, pod, score);
                if (decision.Same(current))
                    score += KeepBonus;
                if (random != null && jitter > 0)
                    score += jitter * (float)random.NextDouble();
                decision.score = score;
                if (score > best.score)
                    best = decision;
            }

            // The enemy flag: on its stand or lying dropped, which anyone near snatches up (a teammate carrying it needs no one else)
            if (s.enemyFlag == FlagStatus.Home)
                Offer(BotGoal.TakeFlag, t.attack * Near(s.toEnemyFlag, 120));
            else if (s.enemyFlag == FlagStatus.Dropped)
                Offer(BotGoal.TakeFlag, t.attack * 1.3f * Near(s.toEnemyFlag, 120) + 0.6f * Near(s.toEnemyFlag, 20));

            // Its own flag: guarded at home, sent home when dropped, the carrier chased when taken
            switch (s.ownFlag)
            {
                case FlagStatus.Home:
                    Offer(BotGoal.DefendFlag, t.defend * 0.9f * Near(s.toOwnStand, 150));
                    break;
                case FlagStatus.Dropped:
                    Offer(BotGoal.ReturnFlag, (t.defend + t.chase) * 0.8f * Near(s.toOwnFlag, 60));
                    break;
                case FlagStatus.Carried:
                    Offer(BotGoal.ChaseCarrier, (t.chase * 1.6f + t.defend * 0.5f) * Near(s.toOwnFlag, 80) + (s.carrierInSight ? 0.4f : 0));
                    break;
            }

            Offer(BotGoal.Hunt, t.hunt * (s.enemyInSight ? 1.1f : 0.65f));

            // Pods: health when hurt (and a little for the overheal when near), weapons it lacks, more so with only the single pistol
            foreach (var pod in s.pods)
            {
                if (!pod.available || !pod.useful)
                    continue;
                if (pod.health)
                    Offer(BotGoal.FetchHealth, (0.4f + t.fetch) * 2.2f * HealthNeed(s) * Near(pod.distance, 30), pod.index);
                else
                    Offer(BotGoal.FetchWeapon, t.fetch * WeaponValue(pod.weapon) * (s.weaponsHeld <= 1 ? 1.4f : 1) * 1.3f * Near(pod.distance, 25), pod.index);
            }
            return best;
        }

        /// <summary>How much a bot wants health, 0 to 1: steeply under the most (100), a little for the overheal above it.</summary>
        public static float HealthNeed(BotSituation s)
        {
            if (s.health < s.maxHealth)
                return Mathf.Clamp01((s.maxHealth - s.health) / s.maxHealth);
            if (s.health >= s.overheal || s.overheal <= s.maxHealth)
                return 0;
            return 0.15f * (s.overheal - s.health) / (s.overheal - s.maxHealth);
        }

        /// <summary>How much a weapon pod is worth to a bot, 0 to 1.</summary>
        public static float WeaponValue(string weapon) => weapon switch
        {
            WeaponIds.Pistol => 0.45f,
            WeaponIds.Minigun => 0.8f,
            WeaponIds.Sniper or WeaponIds.ElectroGun => 0.7f,
            WeaponIds.Flamethrower or WeaponIds.PoisonGun or WeaponIds.BlobGun => 0.75f,
            _ => 0.3f,
        };

        /// <summary>1 right there, a half at <paramref name="scale"/> metres, less further.</summary>
        public static float Near(float distance, float scale) => 1 / (1 + Mathf.Max(0, distance) / Mathf.Max(1, scale));
    }
}
