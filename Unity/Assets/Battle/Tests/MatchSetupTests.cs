using System.Linq;
using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>
    /// Ticket 32.3: a match that takes a setup, through <see cref="MatchSetup"/>, <see cref="MatchRules"/> and <see cref="MatchState"/>
    /// alone: uneven teams, one round only, a respawn wait per team, spores off, and the defaults without a setup.
    /// </summary>
    public sealed class MatchSetupTests
    {
        private static MatchState Build(MatchRules rules)
        {
            var match = new MatchState(rules);
            foreach (var team in Teams.All)
                for (var i = 1; i <= rules.SizeOf(team); i++)
                    match.AddPlayer($"{team.Name()} {i}", team, i > 1 || team == Team.Blue);
            match.Begin();
            return match;
        }

        private static void Run(MatchState match, float seconds)
        {
            for (var left = seconds; left > 1e-4f; left -= 0.1f)
                match.Tick(System.Math.Min(0.1f, left));
        }

        [Test]
        public void NoSetup_KeepsTheDefaults()
        {
            var rules = new MatchRules();

            Assert.IsNull(MatchSetup.Pending);
            Assert.AreEqual(4, rules.SizeOf(Team.Red));
            Assert.AreEqual(4, rules.SizeOf(Team.Blue));
            Assert.AreEqual(5, rules.RespawnOf(Team.Red));
            Assert.AreEqual(5, rules.RespawnOf(Team.Blue));
            Assert.IsFalse(rules.singleRound);
            Assert.AreEqual(3, rules.capturesToWin);
            Assert.AreEqual(600, rules.roundSeconds);
        }

        [Test]
        public void TheSetup_OverridesTheRulesItNames_AndLeavesTheRest()
        {
            var rules = new MatchRules { countdownSeconds = 0, spawnProtectionSeconds = 1 };
            new MatchSetup { redSize = 3, blueSize = 5, capturesToWin = 2, timeLimitSeconds = 480, blueRespawnSeconds = 3 }.Apply(rules);

            Assert.AreEqual(3, rules.SizeOf(Team.Red));
            Assert.AreEqual(5, rules.SizeOf(Team.Blue));
            Assert.AreEqual(2, rules.capturesToWin);
            Assert.AreEqual(480, rules.roundSeconds);
            Assert.IsTrue(rules.singleRound);
            Assert.AreEqual(0, rules.countdownSeconds);
            Assert.AreEqual(1, rules.spawnProtectionSeconds);
        }

        [Test]
        public void UnevenTeams_AreFourAgainstFive()
        {
            var rules = new MatchRules();
            new MatchSetup { redSize = 4, blueSize = 5 }.Apply(rules);

            var match = Build(rules);

            Assert.AreEqual(4, match.PlayersOf(Team.Red).Count());
            Assert.AreEqual(5, match.PlayersOf(Team.Blue).Count());
            Assert.AreEqual(9, match.Players.Count);
        }

        [Test]
        public void Names_ComeFromTheSetup_ThenTheCast()
        {
            var setup = new MatchSetup { redNames = new[] { "Me", "Ally" }, blueNames = new[] { "Foe" } };

            Assert.AreEqual("Me", setup.NameOf(Team.Red, 1));
            Assert.AreEqual("Ally", setup.NameOf(Team.Red, 2));
            Assert.AreEqual(Cast.Name(Team.Red, 3), setup.NameOf(Team.Red, 3));
            Assert.AreEqual("Foe", setup.NameOf(Team.Blue, 1));
            Assert.AreEqual(Cast.Name(Team.Blue, 2), setup.NameOf(Team.Blue, 2));
        }

        [Test]
        public void SkillIsPerTeam()
        {
            var setup = new MatchSetup { redSkill = 0.35f, blueSkill = 0.15f };

            Assert.AreEqual(0.35f, setup.SkillOf(Team.Red));
            Assert.AreEqual(0.15f, setup.SkillOf(Team.Blue));
        }

        [Test]
        public void SingleRound_EndsForGood_WithTheResult()
        {
            var rules = new MatchRules { countdownSeconds = 0, resultSeconds = 2 };
            new MatchSetup { capturesToWin = 2 }.Apply(rules);
            var match = Build(rules);
            var rounds = 0;
            var results = new System.Collections.Generic.List<MatchResult>();
            match.RoundReset += () => rounds++;
            match.MatchEnded += results.Add;
            Run(match, 30);

            match.ReportCapture(Team.Red);
            Run(match, 7);
            match.ReportCapture(Team.Blue);
            match.ReportCapture(Team.Red);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(Team.Red, results[0].Winner);
            Assert.AreEqual(2, results[0].RedScore);
            Assert.AreEqual(1, results[0].BlueScore);
            Assert.AreEqual(37, results[0].Seconds, 0.2f);
            Assert.IsTrue(match.Over);

            Run(match, 60);

            Assert.AreEqual(0, rounds, "no next round");
            Assert.AreEqual(1, match.Round);
            Assert.AreEqual(RoundPhase.RoundEnd, match.Phase);
            Assert.AreEqual(2, match.Score(Team.Red), "the score stays");
            Assert.AreEqual(1, results.Count, "raised once");
        }

        [Test]
        public void WithoutSingleRound_TheNextRoundStillComes()
        {
            var rules = new MatchRules { countdownSeconds = 0, resultSeconds = 2 };
            var match = Build(rules);
            var results = 0;
            match.MatchEnded += _ => results++;

            for (var i = 0; i < 3; i++)
                match.ReportCapture(Team.Red);
            Run(match, 2.5f);

            Assert.AreEqual(2, match.Round);
            Assert.AreEqual(0, results);
            Assert.IsFalse(match.Over);
        }

        [Test]
        public void TimeUp_EndsASingleRoundMatchToo()
        {
            var rules = new MatchRules { countdownSeconds = 0 };
            new MatchSetup { timeLimitSeconds = 5 }.Apply(rules);
            var match = Build(rules);
            MatchResult? result = null;
            match.MatchEnded += r => result = r;

            match.ReportCapture(Team.Blue);
            Run(match, 5.5f);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(Team.Blue, result.Value.Winner);
        }

        [Test]
        public void Restart_PlaysTheFinishedMatchAgain()
        {
            var rules = new MatchRules { countdownSeconds = 0 };
            new MatchSetup { capturesToWin = 1 }.Apply(rules);
            var match = Build(rules);
            match.ReportCapture(Team.Blue);
            Assert.IsTrue(match.Over);

            match.Restart();

            Assert.IsFalse(match.Over);
            Assert.AreEqual(RoundPhase.Playing, match.Phase);
            Assert.AreEqual(0, match.Score(Team.Blue));
            Assert.AreEqual(rules.roundSeconds, match.TimeLeft);
            Assert.IsTrue(match.Players.All(p => p.Alive));
            Assert.IsTrue(match.ReportCapture(Team.Red));
            Assert.IsTrue(match.Over);
        }

        [Test]
        public void Respawn_IsPerTeam()
        {
            var rules = new MatchRules { countdownSeconds = 0, respawnSeconds = 5 };
            new MatchSetup { blueRespawnSeconds = 3 }.Apply(rules);
            var match = Build(rules);
            var red = match.Players.First(p => p.Team == Team.Red);
            var blue = match.Players.First(p => p.Team == Team.Blue);

            match.ReportDeath(new Death(red, DeathCause.Fell));
            match.ReportDeath(new Death(blue, DeathCause.Fell));
            Run(match, 3.2f);

            Assert.IsTrue(blue.Alive, "blue is back after 3 s");
            Assert.IsFalse(red.Alive, "red waits 5 s");
            Run(match, 2);
            Assert.IsTrue(red.Alive);
        }

        [Test]
        public void SporesOff_GainsNothing()
        {
            var match = Build(new MatchRules { countdownSeconds = 0 });
            var combat = new CombatState(match, WeaponTable.Default());
            var spores = new SporeState(match, combat) { Enabled = false };
            var player = match.Players[0];

            Assert.AreEqual(0, spores.Gain(player, 10));
            Assert.IsFalse(spores.IsFull(player));

            spores.Enabled = true;
            Assert.Greater(spores.Gain(player, 10), 0);
        }

        [Test]
        public void AHeldMatch_StandsStill_AndLetsGoOnRelease()
        {
            var match = Build(new MatchRules { countdownSeconds = 0 });
            var left = match.TimeLeft;
            match.Held = true;
            Run(match, 5);
            Assert.AreEqual(left, match.TimeLeft, "the clock stands still");
            Assert.IsFalse(match.CanMove);
            var victim = match.Players.First(p => p.Team == Team.Blue);
            Assert.IsFalse(match.CanHurt(victim, Team.Red), "nobody hurts anybody");
            match.Held = false;
            Run(match, 5);
            Assert.AreEqual(left - 5, match.TimeLeft, 0.2f);
            Assert.IsTrue(match.CanMove);
            Assert.IsTrue(match.CanHurt(victim, Team.Red) || victim.Protected);
        }

        [Test]
        public void TheSetup_CarriesTheHoldAndTheWarmUp()
        {
            var setup = new MatchSetup { held = true, warmUp = true };
            Assert.IsTrue(setup.held);
            Assert.IsTrue(setup.warmUp);
            Assert.IsFalse(new MatchSetup().held);
            Assert.IsFalse(new MatchSetup().warmUp);
        }
    }
}
