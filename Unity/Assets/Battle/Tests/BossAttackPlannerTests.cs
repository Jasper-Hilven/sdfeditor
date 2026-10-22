using System.Collections.Generic;
using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 33.4: <see cref="BossAttackPlanner"/> and <see cref="Targeting"/>: each attack's stages and times, the cooldowns, one
    /// attack at a time, the sight and range rules, phase 3's target order and the difficulty's wind-ups.
    /// </summary>
    public sealed class BossAttackPlannerTests
    {
        private MatchState match;
        private BossRules rules;
        private BossAttackPlanner planner;
        private readonly List<string> log = new List<string>();

        private PlayerState You => match.Players[0];
        private PlayerState Bot2 => match.Players[1];
        private PlayerState Bot3 => match.Players[2];

        [SetUp]
        public void SetUp() => Build(BossRules.For(BossDifficulty.Normal));

        private void Build(BossRules r)
        {
            rules = r;
            match = new MatchState(new MatchRules { objective = MatchObjective.Boss });
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            match.Begin();
            // Past the countdown and the spawn protection
            for (var t = 0f; t < 6; t += 0.1f)
                match.Tick(0.1f);
            planner = new BossAttackPlanner(rules);
            log.Clear();
            planner.Started += a => log.Add($"start {a.Kind}");
            planner.StageChanged += a => log.Add($"{a.Kind} {a.Stage}");
            planner.Finished += a => log.Add($"end {a.Kind}");
            planner.Cancelled += a => log.Add($"cancel {a.Kind}");
        }

        private static BossTarget T(PlayerState p, float distance, bool sight = true, bool hidden = false, bool carrier = false, float recent = 0) =>
            new BossTarget { Player = p, Distance = distance, InSight = sight, Hidden = hidden, Carrier = carrier, RecentDamage = recent };

        private void Step(BossPhase phase, float seconds, List<BossTarget> targets, bool sore = false, bool stunned = false, int tanks = 0,
            bool left = true, bool right = true)
        {
            for (var t = 0f; t < seconds - 1e-4f; t += 0.05f)
                planner.Tick(0.05f, phase, sore, stunned, targets, tanks, left, right);
        }

        private int Starts(BossAttackKind kind) => log.FindAll(l => l == $"start {kind}").Count;

        // ---------------------------------------------------------------- Stages

        [Test]
        public void Attack_GoesWindUp_Active_Recover_Ready_WithTheRulesTimes()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            // The mortar waits only its opening delay (2 s) in phase 1; the glare 6 s
            Step(BossPhase.Phase1, 2.1f, targets);
            Assert.IsNotNull(planner.Current);
            Assert.AreEqual(BossAttackKind.SporeMortar, planner.Current.Kind);
            Assert.AreEqual(BossAttackStage.WindUp, planner.Current.Stage);
            Assert.AreEqual(1.2f, planner.Current.Spec.windUp, 1e-4f);
            Step(BossPhase.Phase1, 1.0f, targets);
            Assert.AreEqual(BossAttackStage.WindUp, planner.Current.Stage);
            Step(BossPhase.Phase1, 0.3f, targets);
            Assert.AreEqual(BossAttackStage.Active, planner.Current.Stage);
            Step(BossPhase.Phase1, 1.0f, targets);
            Assert.AreEqual(BossAttackStage.Recover, planner.Current.Stage);
            Step(BossPhase.Phase1, 0.6f, targets);
            CollectionAssert.AreEqual(new[] { "start SporeMortar", "SporeMortar Active", "SporeMortar Recover", "end SporeMortar" }, log.GetRange(0, 4));
        }

        [Test]
        public void WindUpProgress_RunsFrom0To1()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            Step(BossPhase.Phase1, 2.1f, targets);
            Assert.AreEqual(0, planner.Current.WindUpProgress, 0.1f);
            Step(BossPhase.Phase1, 0.6f, targets);
            Assert.AreEqual(0.5f, planner.Current.WindUpProgress, 0.1f);
        }

        [Test]
        public void ABigTick_DoesNotSkipAStage()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            planner.Tick(2.1f, BossPhase.Phase1, false, false, targets);
            planner.Tick(10, BossPhase.Phase1, false, false, targets);
            CollectionAssert.AreEqual(new[] { "start SporeMortar", "SporeMortar Active", "SporeMortar Recover", "end SporeMortar" }, log.GetRange(0, 4));
        }

        // ---------------------------------------------------------------- Cooldowns and one at a time

        [Test]
        public void Cooldown_RunsFromTheStart_AndHoldsTheAttackBack()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            Step(BossPhase.Phase1, 2.1f, targets);
            Assert.AreEqual(1, Starts(BossAttackKind.SporeMortar));
            Assert.AreEqual(6, planner.CooldownLeft(BossAttackKind.SporeMortar), 0.1f);
            // The mortar's 2.7 s are done, the glare goes next (its opening is 6 s from the phase start)
            Step(BossPhase.Phase1, 3.0f, targets);
            Assert.AreEqual(1, Starts(BossAttackKind.SporeMortar), "6 s have not passed");
            // The glare takes its turn at 6 s (4.5 s long), then the mortar's 6 s are long over
            Step(BossPhase.Phase1, 8.0f, targets);
            Assert.GreaterOrEqual(Starts(BossAttackKind.SporeMortar), 2);
        }

        [Test]
        public void NeverTwoAttacksAtOnce_OverALongPhase()
        {
            var targets = new List<BossTarget> { T(You, 15), T(Bot2, 25) };
            foreach (var phase in new[] { BossPhase.Phase1, BossPhase.Phase2, BossPhase.Phase3 })
            {
                planner.Reset(phase);
                log.Clear();
                var inFlight = 0;
                planner.Started += _ => inFlight++;
                planner.Finished += _ => inFlight--;
                planner.Cancelled += _ => inFlight--;
                for (var t = 0f; t < 120; t += 0.05f)
                {
                    planner.Tick(0.05f, phase, false, false, targets);
                    Assert.LessOrEqual(inFlight, 1, $"{phase} at {t:0.00}");
                }
                Assert.Greater(log.Count, 3, $"{phase} attacked");
            }
        }

        [Test]
        public void Opening_NothingStartsBeforeItsDelay()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase2);
            Step(BossPhase.Phase2, 1.9f, targets);
            Assert.IsNull(planner.Current);
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void Stunned_NothingStarts_ButWhatIsRunningFinishes()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            Step(BossPhase.Phase1, 20, targets, stunned: true);
            Assert.AreEqual(0, log.Count);
            Step(BossPhase.Phase1, 0.1f, targets);
            Assert.IsNotNull(planner.Current);
            var kind = planner.Current.Kind;
            Step(BossPhase.Phase1, 0.2f, targets, stunned: true);
            Step(BossPhase.Phase1, 5, targets, stunned: true);
            CollectionAssert.Contains(log, $"end {kind}");
            Assert.IsNull(planner.Current);
        }

        [Test]
        public void Cancel_DropsTheAttack_ButKeepsTheCooldown()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            Step(BossPhase.Phase1, 2.1f, targets);
            planner.Cancel();
            Assert.IsNull(planner.Current);
            CollectionAssert.Contains(log, "cancel SporeMortar");
            Assert.Greater(planner.CooldownLeft(BossAttackKind.SporeMortar), 5);
        }

        [Test]
        public void OutsideAFight_NothingStarts()
        {
            var targets = new List<BossTarget> { T(You, 30) };
            foreach (var phase in new[] { BossPhase.Intro, BossPhase.Transition12, BossPhase.Transition23, BossPhase.Victory, BossPhase.Defeat })
                Step(phase, 30, targets);
            Assert.AreEqual(0, log.Count);
        }

        // ---------------------------------------------------------------- Sight and range

        [Test]
        public void Glare_NeedsATargetInSight()
        {
            planner.Reset(BossPhase.Phase1);
            var hidden = new List<BossTarget> { T(You, 30, sight: false) };
            // Phase 1 offers the glare first, but nobody is in sight: only the mortar can start
            Step(BossPhase.Phase1, 40, hidden);
            Assert.AreEqual(0, Starts(BossAttackKind.RoyalGlare));
            Assert.Greater(Starts(BossAttackKind.SporeMortar), 0);

            planner.Reset(BossPhase.Phase1);
            log.Clear();
            var seen = new List<BossTarget> { T(You, 30, sight: true) };
            Step(BossPhase.Phase1, 6.1f, seen);
            Assert.AreEqual(1, Starts(BossAttackKind.RoyalGlare));
        }

        [Test]
        public void Glare_InPhase3_OnlyForTargetsFartherThanTheSlamsReach()
        {
            planner.Reset(BossPhase.Phase3);
            var near = new List<BossTarget> { T(You, 15) };
            Step(BossPhase.Phase3, 30, near);
            Assert.AreEqual(0, Starts(BossAttackKind.RoyalGlare));
            Assert.Greater(Starts(BossAttackKind.CapSlam), 0);

            planner.Reset(BossPhase.Phase3);
            log.Clear();
            var far = new List<BossTarget> { T(You, 40) };
            Step(BossPhase.Phase3, 30, far);
            Assert.AreEqual(0, Starts(BossAttackKind.CapSlam), "nobody within 20 m");
            Assert.Greater(Starts(BossAttackKind.RoyalGlare), 0);
        }

        [Test]
        public void Slam_OnlyWithinTheReach()
        {
            planner.Reset(BossPhase.Phase3);
            // Both hands are down, so a grab does not take the slam's turn
            var edge = new List<BossTarget> { T(You, 20.5f) };
            Step(BossPhase.Phase3, 3, edge, left: false, right: false);
            Assert.AreEqual(0, Starts(BossAttackKind.CapSlam));
            var inside = new List<BossTarget> { T(You, 19.5f) };
            Step(BossPhase.Phase3, 0.1f, inside, left: false, right: false);
            Assert.AreEqual(1, Starts(BossAttackKind.CapSlam));
        }

        [Test]
        public void Cannon_NeedsSight_AndTheDiveDoesNot()
        {
            planner.Reset(BossPhase.Phase2);
            var hidden = new List<BossTarget> { T(You, 30, sight: false) };
            Step(BossPhase.Phase2, 30, hidden);
            Assert.AreEqual(0, Starts(BossAttackKind.CapCannon));
            Assert.Greater(Starts(BossAttackKind.DowndraftDive), 0);
        }

        [Test]
        public void TankDrop_OnlyWhileFewerThanTwoTanksAreAlive()
        {
            planner.Reset(BossPhase.Phase2);
            // Nobody to shoot at, so the drop is the only thing that can start
            var targets = new List<BossTarget>();
            Step(BossPhase.Phase2, 12, targets, tanks: 2);
            Assert.AreEqual(0, Starts(BossAttackKind.TankDrop));
            Step(BossPhase.Phase2, 0.1f, targets, tanks: 1);
            Assert.AreEqual(1, Starts(BossAttackKind.TankDrop));
            // 35 s between drops
            Step(BossPhase.Phase2, 20, targets, tanks: 0);
            Assert.AreEqual(1, Starts(BossAttackKind.TankDrop));
            Step(BossPhase.Phase2, 16, targets, tanks: 0);
            Assert.AreEqual(2, Starts(BossAttackKind.TankDrop));
        }

        [Test]
        public void NoTargets_NothingStartsButTheTankDrop()
        {
            planner.Reset(BossPhase.Phase2);
            Step(BossPhase.Phase2, 12, new List<BossTarget>());
            Assert.AreEqual(1, Starts(BossAttackKind.TankDrop));
            Assert.AreEqual(1, log.FindAll(l => l.StartsWith("start ")).Count);
        }

        [Test]
        public void HandGrab_AlternatesHands_AndSkipsABurstOne()
        {
            planner.Reset(BossPhase.Phase3);
            var far = new List<BossTarget> { T(You, 40, sight: false) };
            var hands = new List<int>();
            planner.Started += a =>
            {
                if (a.Kind == BossAttackKind.HandGrab)
                    hands.Add(a.Hand);
            };
            Step(BossPhase.Phase3, 60, far);
            Assert.GreaterOrEqual(hands.Count, 3);
            for (var i = 1; i < hands.Count; i++)
                Assert.AreNotEqual(hands[i - 1], hands[i]);

            hands.Clear();
            planner.Reset(BossPhase.Phase3);
            Step(BossPhase.Phase3, 60, far, left: false);
            Assert.Greater(hands.Count, 0);
            Assert.IsTrue(hands.TrueForAll(h => h == 1), "only the right hand is whole");

            hands.Clear();
            planner.Reset(BossPhase.Phase3);
            Step(BossPhase.Phase3, 60, far, left: false, right: false);
            Assert.AreEqual(0, hands.Count);
        }

        // ---------------------------------------------------------------- Targeting

        [Test]
        public void Targeting_Phase3_Order()
        {
            var plain = new List<BossTarget> { T(You, 30), T(Bot2, 10), T(Bot3, 25) };
            Assert.AreSame(Bot2, Targeting.Choose(plain, BossPhase.Phase3).Value.Player, "the nearest");

            var damager = new List<BossTarget> { T(You, 30, recent: 5), T(Bot2, 10), T(Bot3, 25, recent: 40) };
            Assert.AreSame(Bot3, Targeting.Choose(damager, BossPhase.Phase3).Value.Player, "most damage in the last seconds");

            var carrier = new List<BossTarget> { T(You, 30, carrier: true), T(Bot2, 10), T(Bot3, 25, recent: 40) };
            Assert.AreSame(You, Targeting.Choose(carrier, BossPhase.Phase3).Value.Player, "the crown's carrier first");
        }

        [Test]
        public void Targeting_Phase1_IsJustTheNearest()
        {
            var list = new List<BossTarget> { T(You, 30, carrier: true), T(Bot2, 10), T(Bot3, 25, recent: 40) };
            Assert.AreSame(Bot2, Targeting.Choose(list, BossPhase.Phase1).Value.Player);
        }

        [Test]
        public void Targeting_NeverTheDead_TheProtected_OrWhoPrimeCannotSee()
        {
            var dead = Bot2;
            match.ReportDeath(new Death(dead, DeathCause.Fell));
            Assert.IsFalse(dead.Alive);
            var list = new List<BossTarget> { T(dead, 5, carrier: true), T(You, 30, hidden: true), T(Bot3, 40) };
            Assert.AreSame(Bot3, Targeting.Choose(list, BossPhase.Phase3).Value.Player);
            Assert.IsNull(Targeting.Choose(new List<BossTarget> { T(dead, 5), T(You, 30, hidden: true) }, BossPhase.Phase3));

            // A fresh round: everyone is protected for a moment
            match.Restart();
            Assert.IsTrue(You.Protected);
            Assert.IsNull(Targeting.Choose(new List<BossTarget> { T(You, 10), T(Bot3, 12) }, BossPhase.Phase1));
        }

        [Test]
        public void Mortar_TargetsYouFirst_ThenTheNearestRedBots_UpToThree()
        {
            var bot4 = match.Players[3];
            var list = new List<BossTarget> { T(bot4, 5), T(Bot2, 12), T(Bot3, 8), T(You, 40) };
            var picked = Targeting.Mortar(list, 3);
            Assert.AreEqual(3, picked.Count);
            Assert.AreSame(You, picked[0].Player);
            Assert.AreSame(bot4, picked[1].Player);
            Assert.AreSame(Bot3, picked[2].Player);
        }

        [Test]
        public void PlannerMortar_AimsAtThreeTargets()
        {
            planner.Reset(BossPhase.Phase1);
            var list = new List<BossTarget> { T(You, 40), T(Bot2, 12), T(Bot3, 8), T(match.Players[3], 5) };
            BossAttack mortar = null;
            planner.Started += a => mortar ??= a.Kind == BossAttackKind.SporeMortar ? a : null;
            Step(BossPhase.Phase1, 2.1f, list);
            Assert.IsNotNull(mortar);
            Assert.AreEqual(3, mortar.Targets.Count);
            Assert.AreSame(You, mortar.Target);
        }

        // ---------------------------------------------------------------- Difficulty and Sore Loser

        [TestCase(BossDifficulty.Easy, 1.2f * 1.3f)]
        [TestCase(BossDifficulty.Normal, 1.2f)]
        [TestCase(BossDifficulty.Hard, 1.2f * 0.8f)]
        public void WindUps_FollowTheDifficulty(BossDifficulty difficulty, float expected)
        {
            Build(BossRules.For(difficulty));
            var targets = new List<BossTarget> { T(You, 30) };
            planner.Reset(BossPhase.Phase1);
            Step(BossPhase.Phase1, 2.1f, targets);
            Assert.AreEqual(expected, planner.Current.Spec.windUp, 1e-4f);
            Assert.AreEqual(expected, planner.Current.TimeLeft, 0.06f);
        }

        [Test]
        public void SoreLoser_ShortensPhase3WindUpsBy20Percent()
        {
            var targets = new List<BossTarget> { T(You, 10) };
            planner.Reset(BossPhase.Phase3);
            Step(BossPhase.Phase3, 2.1f, targets, sore: true);
            Assert.AreEqual(BossAttackKind.CapSlam, planner.Current.Kind);
            Assert.AreEqual(1.2f * 0.8f, planner.Current.Spec.windUp, 1e-4f);
        }

        [Test]
        public void EveryAttack_HasASpec_WithItsTimesAndDamage()
        {
            foreach (BossAttackKind kind in System.Enum.GetValues(typeof(BossAttackKind)))
            {
                var spec = rules.SpecOf(kind);
                Assert.AreEqual(kind, spec.kind);
                Assert.Greater(spec.windUp, 0, kind.ToString());
                Assert.Greater(spec.cooldown, spec.windUp, kind.ToString());
            }
            Assert.AreEqual(1.2f, rules.SpecOf(BossAttackKind.SporeMortar).windUp, 1e-4f);
            Assert.AreEqual(20, rules.SpecOf(BossAttackKind.SporeMortar).damage);
            Assert.AreEqual(14, rules.SpecOf(BossAttackKind.RoyalGlare).cooldown);
            Assert.AreEqual(30, rules.SpecOf(BossAttackKind.RoyalGlare).damage);
            Assert.AreEqual(12, rules.SpecOf(BossAttackKind.CapCannon).count);
            Assert.AreEqual(35, rules.SpecOf(BossAttackKind.TankDrop).cooldown);
            Assert.AreEqual(3, rules.SpecOf(BossAttackKind.CapSlam).recover, 1e-4f);
            Assert.AreEqual(9, rules.SpecOf(BossAttackKind.CapSlam).cooldown);
            Assert.AreEqual(25, rules.glareTurnRate);
        }
    }
}
