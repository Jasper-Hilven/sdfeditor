using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 20: the match's phases, score, clock, sudden death, deaths, respawns and spawn protection, played through
    /// <see cref="MatchState"/> alone (EditMode, no scene). Run them in Window > General > Test Runner, EditMode tab.
    /// </summary>
    public sealed class MatchStateTests
    {
        private static MatchState FourVsFour(MatchRules rules = null)
        {
            var match = new MatchState(rules ?? new MatchRules());
            foreach (var team in Teams.All)
                for (var i = 1; i <= match.Rules.teamSize; i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            return match;
        }

        /// <summary>Ticks in small steps, as frames do.</summary>
        private static void Run(MatchState match, float seconds, float step = 0.1f)
        {
            for (var left = seconds; left > 1e-4f; left -= step)
                match.Tick(System.Math.Min(step, left));
        }

        private static MatchState Playing(MatchRules rules = null)
        {
            var match = FourVsFour(rules);
            match.Begin();
            Run(match, match.Rules.countdownSeconds + 0.05f);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
            return match;
        }

        [Test]
        public void Defaults_AreTheUsersDecisions()
        {
            var rules = new MatchRules();

            Assert.AreEqual(4, rules.teamSize);
            Assert.AreEqual(3, rules.capturesToWin);
            Assert.AreEqual(600, rules.roundSeconds);
            Assert.AreEqual(3, rules.countdownSeconds);
            Assert.AreEqual(10, rules.resultSeconds);
            Assert.AreEqual(5, rules.respawnSeconds);
            Assert.AreEqual(2, rules.spawnProtectionSeconds);
            Assert.AreEqual(100, rules.maxHealth);
            Assert.IsFalse(rules.friendlyFire);
        }

        [Test]
        public void BeforeBegin_NobodyIsAlive_AndTimeDoesNotMove()
        {
            var match = FourVsFour();

            match.Tick(5);

            Assert.IsFalse(match.Started);
            Assert.AreEqual(8, match.Players.Count);
            Assert.IsTrue(match.Players.All(p => !p.Alive));
            Assert.IsFalse(match.ReportCapture(Team.Red));
        }

        [Test]
        public void Begin_SpawnsEveryone_AndCountsDown_ThenPlays()
        {
            var match = FourVsFour();
            var spawned = new List<PlayerState>();
            var phases = new List<RoundPhase>();
            match.Spawned += spawned.Add;
            match.PhaseChanged += phases.Add;

            match.Begin();

            Assert.AreEqual(1, match.Round);
            Assert.AreEqual(RoundPhase.Countdown, match.Phase);
            Assert.IsFalse(match.CanMove);
            Assert.AreEqual(8, spawned.Count);
            Assert.IsTrue(match.Players.All(p => p.Alive && p.Health == 100));
            Assert.AreEqual(600, match.TimeLeft);

            Run(match, 2.9f);
            Assert.AreEqual(RoundPhase.Countdown, match.Phase);
            Assert.AreEqual(600, match.TimeLeft, "the clock waits for the countdown");
            Run(match, 0.2f);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
            Assert.IsTrue(match.CanMove);
            CollectionAssert.AreEqual(new[] { RoundPhase.Countdown, RoundPhase.Playing }, phases);
        }

        [Test]
        public void TheTeamsAreFourAndFour()
        {
            var match = FourVsFour();

            Assert.AreEqual(4, match.PlayersOf(Team.Red).Count());
            Assert.AreEqual(4, match.PlayersOf(Team.Blue).Count());
            Assert.AreEqual(1, match.Players.Count(p => !p.IsBot), "you, and seven bots");
        }

        [Test]
        public void ThirdCapture_WinsTheRound()
        {
            var match = Playing();
            var carrier = match.PlayersOf(Team.Blue).First();
            Team? winner = null;
            match.RoundEnded += t => winner = t;

            Assert.IsTrue(match.ReportCapture(Team.Blue, carrier));
            Assert.IsTrue(match.ReportCapture(Team.Red));
            Assert.IsTrue(match.ReportCapture(Team.Blue, carrier));
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
            Assert.IsTrue(match.ReportCapture(Team.Blue, carrier));

            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(Team.Blue, winner);
            Assert.AreEqual(Team.Blue, match.Winner);
            Assert.AreEqual(3, match.Score(Team.Blue));
            Assert.AreEqual(1, match.Score(Team.Red));
            Assert.AreEqual(3, carrier.Captures);
        }

        [Test]
        public void Captures_CountOnlyWhilePlaying()
        {
            var match = FourVsFour();
            match.Begin();

            Assert.IsFalse(match.ReportCapture(Team.Red), "not in the countdown");
            Run(match, 3.1f);
            match.ReportCapture(Team.Red);
            match.ReportCapture(Team.Red);
            match.ReportCapture(Team.Red);
            Assert.IsFalse(match.ReportCapture(Team.Red), "not once the round is over");
            Assert.AreEqual(3, match.Score(Team.Red));
        }

        [Test]
        public void ClockRunsOut_MostCapturesWins()
        {
            var match = Playing();
            match.ReportCapture(Team.Red);
            match.ReportCapture(Team.Red);
            match.ReportCapture(Team.Blue);

            Run(match, 599, 1);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
            Run(match, 1.5f);

            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(Team.Red, match.Winner);
            Assert.AreEqual(0, match.TimeLeft);
        }

        [Test]
        public void ClockRunsOut_OnATie_SuddenDeath_TheNextCaptureWins()
        {
            var match = Playing();
            var suddenDeath = 0;
            match.SuddenDeathStarted += () => suddenDeath++;
            match.ReportCapture(Team.Red);
            match.ReportCapture(Team.Blue);

            Run(match, 601, 1);

            Assert.AreEqual(RoundPhase.Playing, match.Phase);
            Assert.IsTrue(match.SuddenDeath);
            Assert.AreEqual(1, suddenDeath);
            Assert.AreEqual(0, match.TimeLeft);
            Run(match, 120, 1);
            Assert.AreEqual(RoundPhase.Playing, match.Phase, "sudden death has no time limit");

            match.ReportCapture(Team.Blue);

            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(Team.Blue, match.Winner);
            Assert.AreEqual(2, match.Score(Team.Blue));
        }

        [Test]
        public void ClockRunsOut_WithNoCaptures_IsSuddenDeath()
        {
            var match = Playing();

            Run(match, 600.5f, 1);

            Assert.IsTrue(match.SuddenDeath);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
        }

        [Test]
        public void TenSecondsAfterTheWin_ANewRound_WithEverythingReset()
        {
            var match = Playing();
            var red = match.PlayersOf(Team.Red).First();
            var blue = match.PlayersOf(Team.Blue).First();
            match.ReportDeath(new Death(blue, DeathCause.Killed, red, weapon: "pistol"));
            for (var i = 0; i < 3; i++)
                match.ReportCapture(Team.Red, red);
            Run(match, 9.9f);
            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(1, match.Round);
            Assert.IsTrue(blue.Alive, "respawned during the result");
            var resets = 0;
            var spawned = new List<PlayerState>();
            match.RoundReset += () =>
            {
                resets++;
                Assert.AreEqual(0, spawned.Count, "the reset comes before anyone spawns");
            };
            match.Spawned += spawned.Add;

            Run(match, 0.2f);

            Assert.AreEqual(2, match.Round);
            Assert.AreEqual(1, resets);
            Assert.AreEqual(RoundPhase.Countdown, match.Phase);
            Assert.AreEqual(0, match.Score(Team.Red));
            Assert.AreEqual(0, match.Score(Team.Blue));
            Assert.AreEqual(600, match.TimeLeft);
            Assert.IsFalse(match.SuddenDeath);
            Assert.IsNull(match.Winner);
            Assert.AreEqual(8, spawned.Count, "everyone spawns anew, the dead too");
            Assert.IsTrue(match.Players.All(p => p.Alive && p.Kills == 0 && p.Deaths == 0 && p.Captures == 0));
        }

        [Test]
        public void ADeadPlayer_RespawnsAfterFiveSeconds()
        {
            var match = Playing();
            var you = match.Players.First(p => !p.IsBot);
            var deaths = new List<Death>();
            var spawned = new List<PlayerState>();
            match.Died += deaths.Add;
            match.Spawned += spawned.Add;

            match.ReportFall(you);

            Assert.IsFalse(you.Alive);
            Assert.AreEqual(5, you.RespawnIn);
            Assert.AreEqual(DeathCause.Fell, deaths.Single().Cause);
            Assert.IsNull(deaths.Single().KillerName);
            Run(match, 4.9f);
            Assert.IsFalse(you.Alive);
            Assert.AreEqual(0, spawned.Count);
            Run(match, 0.2f);
            Assert.IsTrue(you.Alive);
            Assert.AreEqual(100, you.Health);
            CollectionAssert.AreEqual(new[] { you }, spawned);
        }

        [Test]
        public void ADeadPlayer_DiesOnlyOnce()
        {
            var match = Playing();
            var you = match.Players[0];
            var deaths = 0;
            match.Died += _ => deaths++;

            match.ReportFall(you);
            Run(match, 2);
            match.ReportFall(you);

            Assert.AreEqual(1, deaths);
            Assert.AreEqual(1, you.Deaths);
            Assert.AreEqual(3, you.RespawnIn, 0.01f, "the wait is not restarted");
        }

        [Test]
        public void Kills_CountForAnEnemyPlayer_NotForAMachineOrAFall()
        {
            var match = Playing();
            var red = match.PlayersOf(Team.Red).ToArray();
            var blue = match.PlayersOf(Team.Blue).ToArray();

            match.ReportDeath(new Death(blue[0], DeathCause.Killed, red[0], weapon: "pistol"));
            match.ReportDeath(new Death(blue[1], DeathCause.Killed, killerName: "Red tank", killerTeam: Team.Red, weapon: "cannon"));
            match.ReportFall(blue[2]);

            Assert.AreEqual(1, red[0].Kills);
            Assert.AreEqual(0, red.Skip(1).Sum(p => p.Kills));
            Assert.AreEqual(3, blue.Sum(p => p.Deaths));
        }

        [Test]
        public void AMachineKill_NamesTheMachine_ButGivesNoCredit()
        {
            var death = new Death(new PlayerState(0, "Red 2", Team.Red, true), DeathCause.Killed, killerName: "Blue tank", killerTeam: Team.Blue, weapon: "cannon");

            Assert.AreEqual("Blue tank", death.KillerName);
            Assert.AreEqual(Team.Blue, death.KillerTeam);
            Assert.IsNull(death.Killer);
            Assert.IsFalse(death.CountsAsKill);
        }

        [Test]
        public void SpawnProtection_TwoSeconds()
        {
            var match = Playing(new MatchRules { countdownSeconds = 0 });
            var red = match.PlayersOf(Team.Red).First();
            var blue = match.PlayersOf(Team.Blue).First();
            match.ReportFall(red);
            Run(match, 5.05f);
            Assert.IsTrue(red.Alive);

            Assert.IsTrue(red.Protected);
            Assert.IsFalse(match.CanHurt(red, Team.Blue));
            Run(match, 1.9f);
            Assert.IsFalse(match.CanHurt(red, Team.Blue));
            Run(match, 0.2f);
            Assert.IsFalse(red.Protected);
            Assert.IsTrue(match.CanHurt(red, Team.Blue));
            Assert.IsTrue(match.CanHurt(blue, Team.Red));
        }

        [Test]
        public void SpawnProtection_EndsEarly_WhenFiring()
        {
            var match = Playing(new MatchRules { countdownSeconds = 0 });
            var red = match.PlayersOf(Team.Red).First();

            Assert.IsTrue(red.Protected);
            match.ReportFired(red);

            Assert.IsFalse(red.Protected);
            Assert.IsTrue(match.CanHurt(red, Team.Blue));
        }

        [Test]
        public void FriendlyFire_IsOff()
        {
            var match = Playing();
            Run(match, 2.1f);
            var red = match.PlayersOf(Team.Red).First();

            Assert.IsFalse(match.CanHurt(red, Team.Red));
            Assert.IsTrue(match.CanHurt(red, Team.Blue));
            Assert.IsTrue(new MatchState(new MatchRules { friendlyFire = true }).Rules.friendlyFire);
        }

        [Test]
        public void NobodyIsHurt_DuringTheCountdown_OrWhenDead()
        {
            var match = FourVsFour(new MatchRules { spawnProtectionSeconds = 0 });
            match.Begin();
            var red = match.PlayersOf(Team.Red).First();

            Assert.IsFalse(match.CanHurt(red, Team.Blue));
            Run(match, 3.1f);
            Assert.IsTrue(match.CanHurt(red, Team.Blue));
            match.ReportFall(red);
            Assert.IsFalse(match.CanHurt(red, Team.Blue));
        }

        [Test]
        public void ThePlayersRespawn_DuringTheResult_Too()
        {
            var match = Playing();
            var you = match.Players[0];
            match.ReportFall(you);
            for (var i = 0; i < 3; i++)
                match.ReportCapture(Team.Blue);

            Run(match, 5.1f);

            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.IsTrue(you.Alive);
        }

        [Test]
        public void APlayerAdded_AfterTheStart_SpawnsAtOnce()
        {
            var match = Playing();
            var spawned = new List<PlayerState>();
            match.Spawned += spawned.Add;

            var late = match.AddPlayer("Blue 5", Team.Blue, true);

            Assert.IsTrue(late.Alive);
            Assert.AreEqual(8, late.Index);
            CollectionAssert.AreEqual(new[] { late }, spawned);
        }
    }
}
