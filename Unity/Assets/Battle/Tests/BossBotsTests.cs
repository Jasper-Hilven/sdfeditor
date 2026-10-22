using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 33.6: the plain-C# parts of the boss runtime that the bots and the match use: the crew that is benched and brought back,
    /// the phase temperaments and the markers the bots step out of.
    /// </summary>
    public sealed class BossBotsTests
    {
        private static MatchState Match(out PlayerState blue)
        {
            var match = new MatchState(new MatchRules { countdownSeconds = 0, objective = MatchObjective.Boss, singleRound = true });
            match.AddPlayer("42", Team.Red, false);
            match.AddPlayer("Mushroom 7", Team.Red, true);
            blue = match.AddPlayer("Runner-Up", Team.Blue, true);
            match.AddPlayer("Also-Ran", Team.Blue, true);
            match.Begin();
            return match;
        }

        // ---------------------------------------------------------------- Benching

        [Test]
        public void Bench_TakesALivingPlayerOut_WithARemovedDeath_AndNoRespawn()
        {
            var match = Match(out var blue);
            var deaths = new List<Death>();
            match.Died += deaths.Add;

            match.SetBenched(blue, true);

            Assert.IsTrue(blue.Benched);
            Assert.IsFalse(blue.Alive);
            Assert.AreEqual(1, deaths.Count);
            Assert.AreEqual(DeathCause.Removed, deaths[0].Cause);
            Assert.IsFalse(deaths[0].CountsAsKill);
            for (var i = 0; i < 100; i++)
                match.Tick(1);
            Assert.IsFalse(blue.Alive, "a benched player never respawns");
        }

        [Test]
        public void Bench_SurvivesANewRound()
        {
            var match = Match(out var blue);
            match.SetBenched(blue, true);

            match.Restart();

            Assert.IsFalse(blue.Alive, "a new round does not bring a benched player back");
            Assert.IsTrue(match.Players[0].Alive);
        }

        [Test]
        public void Unbench_BringsThemBackAtOnce_AndTwiceDoesNothing()
        {
            var match = Match(out var blue);
            var spawned = new List<PlayerState>();
            match.SetBenched(blue, true);
            match.Spawned += spawned.Add;

            match.SetBenched(blue, false);
            match.SetBenched(blue, false);

            Assert.IsTrue(blue.Alive);
            Assert.IsFalse(blue.Benched);
            Assert.AreEqual(1, spawned.Count);
            Assert.AreEqual(blue, spawned[0]);
        }

        [Test]
        public void Bench_OfADeadPlayer_StopsTheirRespawn()
        {
            var match = Match(out var blue);
            match.ReportDeath(new Death(blue, DeathCause.Fell));
            Assert.IsFalse(blue.Alive);

            match.SetBenched(blue, true);
            for (var i = 0; i < 20; i++)
                match.Tick(1);

            Assert.IsFalse(blue.Alive);
        }

        // ---------------------------------------------------------------- Crew

        [Test]
        public void CrewCount_FollowsThePhase_AndSoreLoserAddsTwoInPhaseThreeOnly()
        {
            var rules = new BossRules();

            Assert.AreEqual(4, rules.CrewCount(BossPhase.Phase1, false));
            Assert.AreEqual(2, rules.CrewCount(BossPhase.Phase2, false));
            Assert.AreEqual(2, rules.CrewCount(BossPhase.Phase3, false));
            Assert.AreEqual(4, rules.CrewCount(BossPhase.Phase3, true));
            Assert.AreEqual(2, rules.CrewCount(BossPhase.Phase2, true), "Sore Loser is a phase 3 thing");
        }

        [Test]
        public void MostCrew_IsWhatTheMatchMustMakeBodiesFor_PerDifficulty()
        {
            Assert.AreEqual(4, BossRules.For(BossDifficulty.Normal).MostCrew);
            Assert.AreEqual(4, BossRules.For(BossDifficulty.Easy).MostCrew, "2 + 2 in phase 3; phase 1 has 3");
            Assert.AreEqual(5, BossRules.For(BossDifficulty.Hard).MostCrew, "3 + 2 in phase 3");
        }

        // ---------------------------------------------------------------- Temperaments

        [Test]
        public void RedTemperaments_PhaseOne_RaiderGuardianHunter()
        {
            Assert.Greater(BossTemperaments.Red(BossPhase.Phase1, 0).attack, BossTemperaments.Red(BossPhase.Phase1, 0).defend);
            Assert.Greater(BossTemperaments.Red(BossPhase.Phase1, 1).defend, BossTemperaments.Red(BossPhase.Phase1, 1).attack);
            Assert.Greater(BossTemperaments.Red(BossPhase.Phase1, 2).hunt, BossTemperaments.Red(BossPhase.Phase1, 2).attack);
        }

        [Test]
        public void RedTemperaments_PhaseTwo_AllHuntAndFetch_NoFlagPlay()
        {
            for (var place = 0; place < 3; place++)
            {
                var t = BossTemperaments.Red(BossPhase.Phase2, place);
                Assert.AreEqual(0, t.attack);
                Assert.AreEqual(0, t.defend);
                Assert.Greater(t.hunt, 1);
                Assert.Greater(t.fetch, 1, "they fetch the electro-gun");
            }
        }

        [Test]
        public void RedTemperaments_PhaseThree_OneRaiderForTheCrown_TheRestHarass()
        {
            Assert.Greater(BossTemperaments.Red(BossPhase.Phase3, 0).attack, 1);
            Assert.Less(BossTemperaments.Red(BossPhase.Phase3, 1).attack, 0.5f);
            Assert.Greater(BossTemperaments.Red(BossPhase.Phase3, 2).hunt, 1);
        }

        [Test]
        public void BlueTemperaments_OnlyPlayCaptureTheFlagInPhaseOne()
        {
            Assert.Greater(BossTemperaments.Blue(BossPhase.Phase1, 0).attack, 1);
            for (var place = 0; place < 4; place++)
            {
                Assert.AreEqual(0, BossTemperaments.Blue(BossPhase.Phase2, place).attack);
                Assert.AreEqual(0, BossTemperaments.Blue(BossPhase.Phase3, place).attack);
            }
            Assert.Greater(BossTemperaments.Blue(BossPhase.Phase3, 0).defend, 1, "one scout guards the crown");
        }

        [Test]
        public void Of_PicksByTeam()
        {
            Assert.AreEqual(BossTemperaments.Red(BossPhase.Phase1, 0).attack, BossTemperaments.Of(Team.Red, BossPhase.Phase1, 0).attack);
            Assert.AreEqual(BossTemperaments.Blue(BossPhase.Phase3, 0).defend, BossTemperaments.Of(Team.Blue, BossPhase.Phase3, 0).defend);
        }

        // ---------------------------------------------------------------- Markers

        [Test]
        public void Danger_OutsideEveryCircle_NoEscape()
        {
            var zones = new List<DangerZone> { new DangerZone(new Vector2(10, 0), 3) };

            Assert.IsFalse(BotDanger.Escape(new Vector2(0, 0), zones, out var direction));
            Assert.AreEqual(Vector2.zero, direction);
            Assert.IsFalse(BotDanger.Escape(Vector2.zero, null, out _));
        }

        [Test]
        public void Danger_Inside_StepsAwayFromTheMiddle()
        {
            var zones = new List<DangerZone> { new DangerZone(new Vector2(10, 0), 3) };

            Assert.IsTrue(BotDanger.Escape(new Vector2(9, 0), zones, out var direction));
            Assert.AreEqual(-1, direction.x, 1e-4f);
            Assert.AreEqual(0, direction.y, 1e-4f);
        }

        [Test]
        public void Danger_TheMarginCountsToo_ButNotBeyondIt()
        {
            var zones = new List<DangerZone> { new DangerZone(Vector2.zero, 3) };

            Assert.IsTrue(BotDanger.Escape(new Vector2(3 + BotDanger.Margin * 0.5f, 0), zones, out _));
            Assert.IsFalse(BotDanger.Escape(new Vector2(3 + BotDanger.Margin + 0.1f, 0), zones, out _));
        }

        [Test]
        public void Danger_InTwoCircles_LeavesTheDeeperOne()
        {
            var zones = new List<DangerZone>
            {
                new DangerZone(new Vector2(0, 0), 2),
                new DangerZone(new Vector2(4, 0), 14),
            };

            Assert.IsTrue(BotDanger.Escape(new Vector2(1, 0), zones, out var direction));
            Assert.Less(direction.x, 0, "away from the 14 m ring's middle at x = 4");
        }

        [Test]
        public void Danger_RightOnTheMiddle_PicksAWay()
        {
            var zones = new List<DangerZone> { new DangerZone(new Vector2(5, 5), 3) };

            Assert.IsTrue(BotDanger.Escape(new Vector2(5, 5), zones, out var direction));
            Assert.AreEqual(1, direction.magnitude, 1e-4f);
        }
    }
}
