using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 33.4: the boss fight's rules through <see cref="BossState"/> on a running <see cref="MatchState"/> with the flags and the
    /// spores: the bars and their order, the shield, the windows and the weak points' multipliers, the sacs, the crash and the daze, the
    /// crown's immunity, the champion's minigun, lives and checkpoints, the difficulties, Sore Loser and the spore gains.
    /// </summary>
    public sealed class BossStateTests
    {
        private MatchState match;
        private CaptureTheFlagState ctf;
        private CombatState combat;
        private SporeState spores;
        private BossState boss;
        private readonly List<string> events = new List<string>();

        private PlayerState You => match.Players[0];
        private PlayerState RedBot => match.Players[1];
        private PlayerState BlueScout => match.Players[4];

        [SetUp]
        public void SetUp() => Build(BossRules.For(BossDifficulty.Normal));

        private void Build(BossRules rules)
        {
            match = new MatchState(new MatchRules { objective = MatchObjective.Boss, singleRound = true });
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            combat = new CombatState(match, WeaponTable.Default());
            ctf = new CaptureTheFlagState(match);
            spores = new SporeState(match, combat, ctf);
            boss = new BossState(rules, match, ctf, spores);
            events.Clear();
            boss.PhaseChanged += p => events.Add($"phase {p}");
            boss.Stunned += (k, s) => events.Add($"stunned {k} {s:0.#}");
            boss.StunEnded += () => events.Add("stun ended");
            boss.ShieldChanged += up => events.Add(up ? "shield up" : "shield down");
            boss.ImmunityChanged += on => events.Add(on ? "immune" : "vulnerable to all");
            boss.Healed += h => events.Add($"healed {h:0.#}");
            boss.Defeated += () => events.Add("defeated");
            boss.Lost += () => events.Add("lost");
            match.Begin();
            Run(match.Rules.countdownSeconds + 0.05f);
            Run(match.Rules.spawnProtectionSeconds + 0.05f);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
        }

        // Spends 42's full meter on Armour and waits it out: an empty meter, to see what a gain adds
        private void EmptyMeter()
        {
            Assert.IsTrue(spores.Sporulate(You, SporePower.Armour));
            Run(spores.Rules.armourSeconds + 0.1f);
            Assert.AreEqual(0, spores.MeterOf(You).Amount);
        }

        private void Run(float seconds, float step = 0.05f)
        {
            for (var left = seconds; left > 1e-4f; left -= step)
            {
                var dt = Mathf.Min(step, left);
                match.Tick(dt);
                ctf.Tick(dt);
                combat.Tick(dt);
                spores.Tick(dt);
                boss.Tick(dt);
            }
        }

        // Red takes the blue flag and brings it home: a capture
        private void RedCapture(PlayerState by = null)
        {
            by ??= You;
            Assert.IsTrue(ctf.TouchStand(by, Team.Blue), "took the blue flag");
            Assert.IsTrue(ctf.TouchStand(by, Team.Red), "captured it");
        }

        private void BlueCapture()
        {
            Assert.IsTrue(ctf.TouchStand(BlueScout, Team.Red));
            Assert.IsTrue(ctf.TouchStand(BlueScout, Team.Blue));
        }

        // Burns through the current bar with hits that are allowed (kneeling in phase 1, the dazed copter in phase 2, anything in 3)
        private void EmptyBar()
        {
            while (boss.Phase.IsFight())
            {
                var before = boss.Phase;
                boss.Hurt(100000, BossPart.Body, You);
                if (boss.Phase == before)
                    break;
            }
        }

        private void Hits(float amount, int count, BossPart part = BossPart.Gills, bool champion = false)
        {
            for (var i = 0; i < count; i++)
                boss.Hurt(amount, part, You, champion);
        }

        // ---------------------------------------------------------------- Bars and phases

        [Test]
        public void Bars_AreThreeInOrder_AndEachEndsItsPhase()
        {
            boss.Begin();
            Assert.AreEqual(BossPhase.Phase1, boss.Phase);
            Assert.AreEqual(1500, boss.BarMax(0));
            Assert.AreEqual(1500, boss.BarMax(1));
            Assert.AreEqual(2000, boss.BarMax(2));
            Assert.AreEqual(5000, boss.Health);

            RedCapture();
            Assert.AreEqual(1500, boss.CurrentBar);
            EmptyBar();
            Assert.AreEqual(BossPhase.Transition12, boss.Phase);
            Assert.AreEqual(0, boss.BarHealth(0));
            Assert.AreEqual(3500, boss.Health, "the next bars are untouched");

            boss.FinishTransition();
            Assert.AreEqual(BossPhase.Phase2, boss.Phase);
            Assert.AreEqual(1, boss.Bar);
            Assert.AreEqual(1500, boss.CurrentBar);
        }

        [Test]
        public void FinishTransition_OutsideATransition_DoesNothing()
        {
            boss.Begin();
            boss.FinishTransition();
            Assert.AreEqual(BossPhase.Phase1, boss.Phase);
        }

        [Test]
        public void Begin_AtPhase3_StartsThereWithTheEarlierBarsSpent()
        {
            boss.Begin(3);
            Assert.AreEqual(BossPhase.Phase3, boss.Phase);
            Assert.AreEqual(0, boss.BarHealth(0));
            Assert.AreEqual(0, boss.BarHealth(1));
            Assert.AreEqual(2000, boss.CurrentBar);
        }

        [Test]
        public void ThirdBar_EmptiedIsVictory_AndEndsTheBossMatchForRed()
        {
            boss.Begin(3);
            boss.PrimeTouchesCrown();
            RedCapture();
            EmptyBar();
            Assert.AreEqual(BossPhase.Victory, boss.Phase);
            CollectionAssert.Contains(events, "defeated");
            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(Team.Red, match.Winner);
        }

        // ---------------------------------------------------------------- Phase 1: shield, kneel, heal

        [Test]
        public void Shield_StopsEveryHit_UntilARedCapture()
        {
            boss.Begin();
            Assert.IsTrue(boss.ShieldUp);
            foreach (var part in new[] { BossPart.Body, BossPart.Gills, BossPart.Eyes })
            {
                var hit = boss.Hurt(100, part, You);
                Assert.IsTrue(hit.Blocked, part.ToString());
                Assert.AreEqual(0, hit.Dealt);
            }
            Assert.AreEqual(1500, boss.CurrentBar);
        }

        [Test]
        public void RedCapture_Stuns12Seconds_GillsTakeDouble_ThenTheShieldGrowsBack()
        {
            boss.Begin();
            RedCapture();
            Assert.IsFalse(boss.ShieldUp);
            Assert.IsTrue(boss.Vulnerable);
            Assert.AreEqual(BossStunKind.Kneel, boss.StunKind);
            CollectionAssert.Contains(events, "stunned Kneel 12");
            CollectionAssert.Contains(events, "shield down");

            var gills = boss.Hurt(10, BossPart.Gills, You);
            Assert.AreEqual(20, gills.Dealt);
            Assert.IsTrue(gills.WeakPoint);
            var body = boss.Hurt(10, BossPart.Body, You);
            Assert.AreEqual(10, body.Dealt);
            Assert.IsFalse(body.WeakPoint);

            Run(11.9f);
            Assert.IsTrue(boss.Vulnerable);
            Run(0.2f);
            Assert.IsFalse(boss.Vulnerable);
            Assert.IsTrue(boss.ShieldUp);
            CollectionAssert.Contains(events, "stun ended");
            Assert.IsTrue(boss.Hurt(10, BossPart.Gills, You).Blocked);
        }

        [Test]
        public void BlueCapture_Heals250_ButNotOverTheBarsTop()
        {
            boss.Begin();
            RedCapture();
            Hits(50, 2); // 200 gone
            Assert.AreEqual(1300, boss.CurrentBar);
            Run(13);
            BlueCapture();
            Assert.AreEqual(1500, boss.CurrentBar, "250 offered, 200 fit");
            CollectionAssert.Contains(events, "healed 200");

            BlueCapture();
            Assert.AreEqual(1500, boss.CurrentBar, "nothing over the top");
        }

        [Test]
        public void BlueCapture_Heals250InFull_WhenThereIsRoom()
        {
            boss.Begin();
            RedCapture();
            Hits(200, 2); // 800 gone
            Run(13);
            BlueCapture();
            Assert.AreEqual(700 + 250, boss.CurrentBar);
        }

        [Test]
        public void Overflow_AtABarsEnd_IsLost()
        {
            boss.Begin();
            RedCapture();
            var hit = boss.Hurt(100000, BossPart.Body, You);
            Assert.AreEqual(1500, hit.Dealt);
            Assert.IsTrue(hit.BarEmptied);
            Assert.AreEqual(BossPhase.Transition12, boss.Phase);
            boss.FinishTransition();
            Assert.AreEqual(1500, boss.CurrentBar, "the next bar did not take the overflow");
            Assert.AreEqual(3500, boss.Health);
        }

        // ---------------------------------------------------------------- Phase 2: sacs, crash, daze

        private void ToPhase2() => boss.Begin(2);

        [Test]
        public void Phase2_NothingHurtsPrime_WhileTheCopterFlies()
        {
            ToPhase2();
            foreach (var part in new[] { BossPart.Body, BossPart.Gills, BossPart.Eyes })
                Assert.IsTrue(boss.Hurt(500, part, You).Blocked, part.ToString());
            Assert.AreEqual(1500, boss.CurrentBar);
        }

        [Test]
        public void Sacs_HaveTheirOwnHealth_AndTheirDamageCountsOnTheBar()
        {
            ToPhase2();
            Assert.AreEqual(4, boss.Parts.SacsAlive);
            Assert.AreEqual(150, boss.HurtSac(0, 150, You));
            Assert.AreEqual(1350, boss.CurrentBar);
            Assert.AreEqual(50, boss.HurtSac(0, 150, You), "only what the sac had left");
            Assert.AreEqual(1300, boss.CurrentBar);
            Assert.AreEqual(3, boss.Parts.SacsAlive);
            Assert.AreEqual(0, boss.HurtSac(0, 50, You), "a burst sac takes nothing");
        }

        [Test]
        public void FourthSac_StartsTheCrash_ThenTheDaze_WithGillsDouble()
        {
            ToPhase2();
            for (var i = 0; i < 3; i++)
                boss.HurtSac(i, 200, You);
            Assert.AreEqual(CopterStage.Flying, boss.Copter);
            boss.HurtSac(3, 200, You);
            Assert.AreEqual(700, boss.CurrentBar, "800 came from the four sacs");
            Assert.AreEqual(CopterStage.Crashing, boss.Copter);
            Assert.IsTrue(boss.Hurt(100, BossPart.Gills, You).Blocked, "not yet dazed during the warning");

            Run(1.9f);
            Assert.AreEqual(CopterStage.Crashing, boss.Copter);
            Run(0.2f);
            Assert.AreEqual(CopterStage.Dazed, boss.Copter);
            Assert.AreEqual(BossStunKind.Daze, boss.StunKind);
            Assert.AreEqual(15, boss.VulnerableLeft, 0.2f);
            Assert.AreEqual(100, boss.Hurt(50, BossPart.Gills, You).Dealt);
        }

        [Test]
        public void DazeEnding_WithTheBarNotEmpty_RegrowsTwoSacs_AndTheCopterFliesAgain()
        {
            ToPhase2();
            for (var i = 0; i < 4; i++)
                boss.HurtSac(i, 200, You);
            Run(2.1f);
            Run(15.1f);
            Assert.IsFalse(boss.Vulnerable);
            Assert.AreEqual(CopterStage.Flying, boss.Copter);
            Assert.AreEqual(2, boss.Parts.SacsAlive);
            Assert.AreEqual(700, boss.CurrentBar, "the bar is not healed");
            Assert.IsTrue(boss.Hurt(100, BossPart.Gills, You).Blocked);
            boss.HurtSac(0, 200, You);
            boss.HurtSac(1, 200, You);
            Assert.AreEqual(CopterStage.Crashing, boss.Copter, "all the sacs are gone again: down it goes once more");
        }

        [Test]
        public void DazeWindow_CanEmptyTheBar_AndSendsTheFightToTheNextTransition()
        {
            ToPhase2();
            for (var i = 0; i < 4; i++)
                boss.HurtSac(i, 200, You);
            Run(2.1f);
            // 700 left: 350 on the gills at ×2
            boss.Hurt(350, BossPart.Gills, You);
            Assert.AreEqual(BossPhase.Transition23, boss.Phase);
        }

        // ---------------------------------------------------------------- Phase 3: crown, eyes, hands, champion's minigun

        [Test]
        public void Crown_Immunity_HalvesDamage_OffWhenTaken_BackOnPrimesTouch()
        {
            boss.Begin(3);
            Assert.IsTrue(boss.Immune);
            Assert.AreEqual(5, boss.Hurt(10, BossPart.Body, You).Dealt, "half damage on the wreck");

            Assert.IsTrue(ctf.TouchStand(You, Team.Blue), "42 takes the crown");
            Assert.IsFalse(boss.Immune);
            CollectionAssert.Contains(events, "vulnerable to all");
            Assert.AreEqual(10, boss.Hurt(10, BossPart.Body, You).Dealt);

            ctf.Drop(You, new Vector3(3, 0, 3));
            Assert.AreEqual(FlagStatus.Dropped, ctf.Flag(Team.Blue).Status);
            Assert.IsFalse(boss.Immune, "dropped, still not Prime's");

            Assert.IsTrue(boss.PrimeTouchesCrown());
            Assert.IsTrue(boss.Immune);
            Assert.AreEqual(FlagStatus.Home, ctf.Flag(Team.Blue).Status);
            Assert.AreEqual(5, boss.Hurt(10, BossPart.Body, You).Dealt);
            Assert.IsFalse(boss.PrimeTouchesCrown(), "nothing to touch");
        }

        [Test]
        public void Crown_AlsoGoesHomeByItself_AndImmunityComesBack()
        {
            boss.Begin(3);
            ctf.TouchStand(You, Team.Blue);
            ctf.Drop(You, Vector3.zero);
            Run(ctf.Rules.returnSeconds + 0.5f);
            Assert.AreEqual(FlagStatus.Home, ctf.Flag(Team.Blue).Status);
            Assert.IsTrue(boss.Immune);
        }

        [Test]
        public void Crown_Captured_EndsImmunityForGood_AndTellsTheWorld()
        {
            PlayerState capturedBy = null;
            boss.Begin(3);
            boss.CrownCapturedBy += p => capturedBy = p;
            RedCapture();
            Assert.IsTrue(boss.CrownCaptured);
            Assert.AreSame(You, capturedBy);
            Assert.IsFalse(boss.Immune);
            Assert.AreEqual(10, boss.Hurt(10, BossPart.Body, You).Dealt);
            Assert.IsFalse(boss.PrimeTouchesCrown());
            Assert.IsFalse(boss.Immune);
        }

        [Test]
        public void Phase3_RedCapture_DoesNotKneelPrime()
        {
            boss.Begin(3);
            RedCapture();
            Assert.IsFalse(boss.Vulnerable);
        }

        [Test]
        public void ChampionMinigun_DoesOneAndAHalf_AgainstPrimeOnly()
        {
            boss.Begin(3);
            RedCapture();
            Assert.AreEqual(10.5f, boss.Hurt(7, BossPart.Body, You, championMinigun: true).Dealt, 1e-4f);
            Assert.AreEqual(7, boss.Hurt(7, BossPart.Body, You).Dealt, 1e-4f, "the ordinary minigun");
        }

        [Test]
        public void Eyes_DoubleWhenOpen_AndNothingDuringAWindUp()
        {
            boss.Begin(3);
            RedCapture();
            Assert.AreEqual(20, boss.Hurt(10, BossPart.Eyes, You).Dealt);
            Assert.IsTrue(boss.Hurt(10, BossPart.Eyes, You).WeakPoint);

            var targets = new List<BossTarget> { Target(You, 30, true) };
            boss.Plan(boss.Rules.openingSeconds + 0.1f, targets);
            Assert.IsNotNull(boss.Planner.Current, "an attack began");
            Assert.AreEqual(BossAttackStage.WindUp, boss.Planner.Current.Stage);
            Assert.IsTrue(boss.EyesShut);
            var shut = boss.Hurt(10, BossPart.Eyes, You);
            Assert.IsTrue(shut.Blocked);
            Assert.AreEqual(0, shut.Dealt);
            Assert.AreEqual(10, boss.Hurt(10, BossPart.Body, You).Dealt, "the body still takes it");
        }

        [Test]
        public void SlamDamage_OnlyForSomeoneOnTheGround()
        {
            var rules = BossRules.For(BossDifficulty.Normal);
            Assert.AreEqual(35, rules.DamageTo(BossAttackKind.CapSlam, grounded: true));
            Assert.AreEqual(0, rules.DamageTo(BossAttackKind.CapSlam, grounded: false), "a jump clears the ring");
            Assert.AreEqual(40, rules.DamageTo(BossAttackKind.HandGrab, grounded: false), "other attacks do not care");
        }

        [Test]
        public void Slam_EndsWithAStuckCap_GillsDouble_ForItsRecovery()
        {
            boss.Begin(3);
            RedCapture();
            var targets = new List<BossTarget> { Target(You, 10, true) };
            boss.Plan(boss.Rules.openingSeconds + 0.1f, targets);
            for (var i = 0; i < 400 && boss.StunKind != BossStunKind.StuckCap; i++)
            {
                boss.Plan(0.05f, targets);
                Run(0.05f);
            }
            Assert.AreEqual(BossStunKind.StuckCap, boss.StunKind);
            Assert.AreEqual(BossAttackKind.CapSlam, boss.Planner.Current.Kind);
            Assert.AreEqual(3, boss.VulnerableLeft, 0.1f);
            Assert.AreEqual(20, boss.Hurt(10, BossPart.Gills, You).Dealt);
        }

        [Test]
        public void BurstHand_StunsPrime4Seconds_NotTheBar_AndGrowsBackIn20()
        {
            boss.Begin(3);
            RedCapture();
            Assert.AreEqual(300, boss.HurtHand(0, 500, You) + 0f, "a hand takes at most its health");
            Assert.AreEqual(2000, boss.CurrentBar, "a hand's health is not Prime's");
            Assert.AreEqual(BossStunKind.HandStun, boss.StunKind);
            Assert.AreEqual(4, boss.VulnerableLeft, 0.1f);
            Assert.IsFalse(boss.Parts.HandAlive(0));
            Run(4.1f);
            Assert.IsFalse(boss.Vulnerable);
            Run(16);
            Assert.IsTrue(boss.Parts.HandAlive(0));
        }

        [Test]
        public void SoreLoser_BeginsWhenBar3IsUnder25Percent_WithShorterWindUps()
        {
            var started = 0;
            boss.Begin(3);
            boss.SoreLoserStarted += () => started++;
            RedCapture();
            var normal = boss.Rules.SpecOf(BossAttackKind.CapSlam).windUp;
            boss.Hurt(1400, BossPart.Body, You);
            Assert.IsFalse(boss.SoreLoser, "600 of 2000 left");
            boss.Hurt(101, BossPart.Body, You);
            Assert.IsTrue(boss.SoreLoser, "499 left, under 500");
            Assert.AreEqual(1, started);
            boss.Hurt(10, BossPart.Body, You);
            Assert.AreEqual(1, started, "once");
            Assert.AreEqual(normal * 0.8f, boss.Rules.SpecOf(BossAttackKind.CapSlam, soreLoser: true).windUp, 1e-4f);
        }

        // ---------------------------------------------------------------- Lives, checkpoints

        [TestCase(BossDifficulty.Easy, 5)]
        [TestCase(BossDifficulty.Normal, 3)]
        [TestCase(BossDifficulty.Hard, 2)]
        public void Lives_PerDifficulty_TheLastOneIsTheDefeat(BossDifficulty difficulty, int lives)
        {
            Build(BossRules.For(difficulty));
            boss.Begin();
            Assert.AreEqual(lives, boss.Lives);
            for (var i = 1; i < lives; i++)
            {
                boss.ReportDeath(You);
                Assert.AreEqual(BossPhase.Phase1, boss.Phase, $"death {i} of {lives}");
            }
            boss.ReportDeath(You);
            Assert.AreEqual(BossPhase.Defeat, boss.Phase);
            Assert.AreEqual(lives, boss.LivesLost);
            CollectionAssert.Contains(events, "lost");
            Assert.AreEqual(Team.Blue, match.Winner, "the boss match ends for blue");
        }

        [Test]
        public void Lives_BotDeathsCostNothing_AndTransitionsAreSafe()
        {
            boss.Begin();
            boss.ReportDeath(RedBot);
            Assert.AreEqual(3, boss.Lives);
            RedCapture();
            EmptyBar();
            Assert.AreEqual(BossPhase.Transition12, boss.Phase);
            boss.ReportDeath(You);
            Assert.AreEqual(3, boss.Lives, "no life is lost in a transition");
        }

        [Test]
        public void Checkpoint_RestoresThePhaseStart_BarFull_LivesFull_FlagsHome()
        {
            boss.Begin();
            RedCapture();
            EmptyBar();
            boss.FinishTransition();
            Assert.AreEqual(BossPhase.Phase2, boss.Phase);
            boss.HurtSac(0, 200, You);
            boss.ReportDeath(You);
            boss.ReportDeath(You);
            Assert.AreEqual(1, boss.Lives);
            ctf.TouchStand(You, Team.Blue);
            Assert.AreEqual(FlagStatus.Carried, ctf.Flag(Team.Blue).Status);

            boss.RestoreCheckpoint();
            Assert.AreEqual(BossPhase.Phase2, boss.Phase);
            Assert.AreEqual(1500, boss.CurrentBar);
            Assert.AreEqual(0, boss.BarHealth(0), "phase 1 stays spent");
            Assert.AreEqual(4, boss.Parts.SacsAlive);
            Assert.AreEqual(3, boss.Lives);
            Assert.AreEqual(FlagStatus.Home, ctf.Flag(Team.Blue).Status);
            Assert.AreEqual(2, boss.LivesLost, "the lives lost stay counted for the medals");
            Assert.AreEqual(2, boss.LivesLostIn(2));
        }

        [Test]
        public void Checkpoint_PerPhase_BringsBackThatPhasesLayout()
        {
            boss.Begin(3);
            Assert.AreEqual(BossPhase.Phase3, boss.Snapshot().Phase);
            RedCapture();
            Assert.IsTrue(boss.CrownCaptured);
            boss.Hurt(500, BossPart.Body, You);
            boss.RestoreCheckpoint();
            Assert.AreEqual(BossPhase.Phase3, boss.Phase);
            Assert.AreEqual(2000, boss.CurrentBar);
            Assert.IsFalse(boss.CrownCaptured);
            Assert.IsTrue(boss.Immune, "the crown is Prime's again");
        }

        [Test]
        public void Restart_GoesBackToPhase1_AndClearsTheCounts()
        {
            boss.Begin(3);
            boss.ReportDeath(You);
            boss.Restart();
            Assert.AreEqual(BossPhase.Phase1, boss.Phase);
            Assert.AreEqual(0, boss.LivesLost);
            Assert.AreEqual(5000, boss.Health);
        }

        // ---------------------------------------------------------------- The HUD's and the end screens' numbers (33.7)

        [Test]
        public void DamageDealt_CountsWhatCameOffTheBars_ByYou_UntilARestart()
        {
            boss.Begin(3);
            RedCapture();
            boss.Hurt(300, BossPart.Body, You);
            boss.Hurt(100, BossPart.Body, RedBot);
            Assert.AreEqual(300, boss.DamageDealt, 1e-4f, "a bot's hit is not yours");
            boss.Hurt(100000, BossPart.Body, You);
            Assert.AreEqual(1900, boss.DamageDealt, 1e-4f, "the overflow is not counted");
            boss.RestoreCheckpoint();
            Assert.AreEqual(1900, boss.DamageDealt, 1e-4f, "a retried phase keeps it");
            boss.Restart();
            Assert.AreEqual(0, boss.DamageDealt);
        }

        [Test]
        public void MakeEasier_ChangesARunningFightsRules()
        {
            var rules = BossRules.For(BossDifficulty.Hard);
            Build(rules);
            boss.Begin();
            var windUp = rules.SpecOf(BossAttackKind.SporeMortar).windUp;
            rules.MakeEasier();
            Assert.IsTrue(rules.retryEasier);
            Assert.AreEqual(0.6f, rules.damageScale);
            Assert.AreEqual(1.3f, rules.windUpScale);
            Assert.AreEqual(windUp * 1.3f / 0.8f, rules.SpecOf(BossAttackKind.SporeMortar).windUp, 1e-4f, "Easy's wind-ups");
            Assert.AreEqual(6250, boss.Rules.TotalHealth, "the health stays Hard's");
            var asBuilt = BossRules.For(BossDifficulty.Hard, true);
            Assert.AreEqual((asBuilt.damageScale, asBuilt.windUpScale), (rules.damageScale, rules.windUpScale));
        }

        [Test]
        public void PhaseTitles_NameTheThreePhases()
        {
            Assert.AreEqual("THE ROYAL BOX", BossPhase.Phase1.TitleOf());
            Assert.AreEqual("THE PRIME-COPTER", BossPhase.Phase2.TitleOf());
            Assert.AreEqual("THE CROWN", BossPhase.Phase3.TitleOf());
            Assert.AreEqual("", BossPhase.Transition12.TitleOf());
        }

        // ---------------------------------------------------------------- Difficulty

        [Test]
        public void Difficulty_ScalesTheBarsDamageWindUpsAndWindows()
        {
            var easy = BossRules.For(BossDifficulty.Easy);
            var normal = BossRules.For(BossDifficulty.Normal);
            var hard = BossRules.For(BossDifficulty.Hard);
            CollectionAssert.AreEqual(new float[] { 1100, 1100, 1550 }, easy.bars);
            CollectionAssert.AreEqual(new float[] { 1500, 1500, 2000 }, normal.bars);
            CollectionAssert.AreEqual(new float[] { 1900, 1900, 2450 }, hard.bars);
            Assert.AreEqual(3750, easy.TotalHealth);
            Assert.AreEqual(5000, normal.TotalHealth);
            Assert.AreEqual(6250, hard.TotalHealth);

            var mortar = BossAttackKind.SporeMortar;
            Assert.AreEqual(12, easy.SpecOf(mortar).damage, 1e-4f);
            Assert.AreEqual(20, normal.SpecOf(mortar).damage, 1e-4f);
            Assert.AreEqual(26, hard.SpecOf(mortar).damage, 1e-4f);
            Assert.AreEqual(1.2f * 1.3f, easy.SpecOf(mortar).windUp, 1e-4f);
            Assert.AreEqual(1.2f, normal.SpecOf(mortar).windUp, 1e-4f);
            Assert.AreEqual(1.2f * 0.8f, hard.SpecOf(mortar).windUp, 1e-4f);
            Assert.AreEqual(15, easy.Window(easy.kneelSeconds), 1e-4f);
            Assert.AreEqual(12, normal.Window(normal.kneelSeconds), 1e-4f);
            Assert.AreEqual(10.2f, hard.Window(hard.kneelSeconds), 1e-4f);
            Assert.AreEqual(0.55f, easy.crewSkill);
            Assert.AreEqual(0.7f, normal.crewSkill);
            Assert.AreEqual(0.85f, hard.crewSkill);
            CollectionAssert.AreEqual(new[] { 3, 2, 2 }, easy.crewSizes);
            CollectionAssert.AreEqual(new[] { 4, 2, 2 }, normal.crewSizes);
            CollectionAssert.AreEqual(new[] { 4, 3, 3 }, hard.crewSizes);
        }

        [Test]
        public void RetryEasier_TakesEasysDamageAndWindUps_ButTheChosenHealth()
        {
            var rules = BossRules.For(BossDifficulty.Hard, retryEasier: true);
            Assert.AreEqual(0.6f, rules.damageScale);
            Assert.AreEqual(1.3f, rules.windUpScale);
            CollectionAssert.AreEqual(new float[] { 1900, 1900, 2450 }, rules.bars);
            Assert.AreEqual(0.85f, rules.stunScale, "windows stay the chosen difficulty's");
        }

        [Test]
        public void KneelWindow_FollowsTheDifficulty()
        {
            Build(BossRules.For(BossDifficulty.Hard));
            boss.Begin();
            RedCapture();
            Assert.AreEqual(10.2f, boss.VulnerableLeft, 1e-3f);
        }

        // ---------------------------------------------------------------- Transitions

        [Test]
        public void Transition_NoDamage_NoAttacks()
        {
            boss.Begin();
            RedCapture();
            EmptyBar();
            Assert.AreEqual(BossPhase.Transition12, boss.Phase);
            Assert.IsTrue(boss.Hurt(100, BossPart.Gills, You).Blocked);
            Assert.IsFalse(boss.CanAttack);
            boss.Plan(1, new List<BossTarget> { Target(You, 10, true) });
            Assert.IsNull(boss.Planner.Current);
            Assert.AreEqual(0, boss.HurtSac(0, 100, You), "no sac hits either");
        }

        [Test]
        public void MatchObjective_Boss_CapturesDoNotWin_AndTheClockDoesNotRun()
        {
            boss.Begin();
            var before = match.TimeLeft;
            for (var i = 0; i < 4; i++)
                BlueCapture();
            Run(30);
            Assert.AreEqual(RoundPhase.Playing, match.Phase, "four blue captures, no win");
            Assert.AreEqual(4, match.Score(Team.Blue));
            Assert.AreEqual(before, match.TimeLeft);
        }

        [Test]
        public void MatchObjective_Captures_StillWinsOnCaptures()
        {
            var plain = new MatchState(new MatchRules());
            plain.AddPlayer("a", Team.Red, false);
            plain.AddPlayer("b", Team.Blue, true);
            plain.Begin();
            plain.Tick(4);
            for (var i = 0; i < 3; i++)
                plain.ReportCapture(Team.Blue);
            Assert.AreEqual(RoundPhase.RoundEnd, plain.Phase);
            Assert.IsFalse(plain.EndBossFight(Team.Red), "no boss fight, no boss ending");
        }

        // ---------------------------------------------------------------- Spores

        [Test]
        public void Spores_TakingAndCapturingTheFlag_AndStartingTheStun()
        {
            boss.Begin();
            // The meter is full at every phase start
            Assert.IsTrue(spores.IsFull(You));
            EmptyMeter();
            RedCapture();
            Assert.AreEqual(20 + 50 + 25, spores.MeterOf(You).Amount, "take 20, capture 50, the stun 25");
        }

        [Test]
        public void Spores_PrimesDamage_FivePer100_AfterTheMultipliers()
        {
            boss.Begin(3);
            EmptyMeter();
            Assert.IsTrue(boss.Immune);
            boss.Hurt(100, BossPart.Body, You);
            Assert.AreEqual(2.5f, spores.MeterOf(You).Amount, 1e-4f, "50 dealt at half damage");
            boss.Hurt(60, BossPart.Eyes, You);
            Assert.AreEqual(2.5f + 3, spores.MeterOf(You).Amount, 1e-4f, "eyes double, immunity halves: 60 dealt");
        }

        [Test]
        public void Spores_SacsAndTheCrash_ThenHands()
        {
            ToPhase2();
            EmptyMeter();
            spores.Rules.bossDamagePer100 = 0;
            boss.HurtSac(0, 200, You);
            Assert.AreEqual(15, spores.MeterOf(You).Amount, 1e-4f, "a sac");
            for (var i = 1; i < 4; i++)
                boss.HurtSac(i, 200, You);
            Assert.AreEqual(4 * 15 + 25, spores.MeterOf(You).Amount, 1e-4f, "four sacs and the stun of the crash");

            boss.Begin(3);
            EmptyMeter();
            boss.HurtHand(1, 300, You);
            Assert.AreEqual(20, spores.MeterOf(You).Amount, 1e-4f, "a hand 20; the stun it causes pays nothing");
        }

        [Test]
        public void Spores_MeterIsFullAtEveryPhaseStart()
        {
            boss.Begin();
            EmptyMeter();
            boss.Begin(3);
            Assert.IsTrue(spores.IsFull(You));
        }

        // ---------------------------------------------------------------- Targets (state side)

        private static BossTarget Target(PlayerState player, float distance, bool inSight = true) =>
            new BossTarget { Player = player, Distance = distance, InSight = inSight };

        [Test]
        public void RecentDamage_CountsOnlyTheLastSeconds()
        {
            boss.Begin(3);
            RedCapture();
            boss.Hurt(100, BossPart.Body, You);
            Assert.AreEqual(100, boss.RecentDamageOf(You), 1e-4f);
            Run(11);
            Assert.AreEqual(0, boss.RecentDamageOf(You));
        }
    }
}
