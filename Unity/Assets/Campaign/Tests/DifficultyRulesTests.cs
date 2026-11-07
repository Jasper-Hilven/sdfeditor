using System.Linq;
using NUnit.Framework;
using SdfBattle;

namespace SdfCampaign.Tests
{
    public sealed class DifficultyRulesTests
    {
        static BattleSetup Setup(float skill = 0.5f, int size = 4) => new BattleSetup
        {
            redSize = 4,
            blueSize = size,
            redSkill = 0.5f,
            blueSkill = skill,
            redNames = new[] { "You", "R2", "R3", "R4" },
            blueNames = new[] { "B1", "B2", "B3", "B4" }.Take(size).ToArray(),
        };

        [Test]
        public void NormalChangesNothing()
        {
            var result = DifficultyRules.Apply(Setup(), Difficulty.Normal, Team.Blue);
            Assert.AreEqual(0.5f, result.blueSkill, 1e-6f);
            Assert.AreEqual(4, result.blueSize);
            Assert.AreEqual(Difficulty.Normal, result.difficulty);
        }

        [Test]
        public void EasyTakesStepOffTheEnemy()
        {
            var result = DifficultyRules.Apply(Setup(0.5f, 4), Difficulty.Easy, Team.Blue);
            Assert.AreEqual(0.35f, result.blueSkill, 1e-6f);
            Assert.AreEqual(4, result.blueSize);
        }

        [Test]
        public void SuperEasyTakesTwoStepsAndKeepsFourEnemies()
        {
            var result = DifficultyRules.Apply(Setup(0.5f, 4), Difficulty.SuperEasy, Team.Blue);
            Assert.AreEqual(0.2f, result.blueSkill, 1e-6f);
            Assert.AreEqual(4, result.blueSize, "every level is 4 vs 4, Super Easy too");
            Assert.AreEqual(4, result.blueNames.Length, "the names are kept");
        }

        [Test]
        public void HardAddsAStepAndStopsAtTheCap()
        {
            var result = DifficultyRules.Apply(Setup(0.5f), Difficulty.Hard, Team.Blue);
            Assert.AreEqual(0.65f, result.blueSkill, 1e-6f);
            var high = DifficultyRules.Apply(Setup(0.9f), Difficulty.Hard, Team.Blue);
            Assert.AreEqual(DifficultyRules.HardMaxSkill, high.blueSkill, 1e-6f);
        }

        [Test]
        public void TheSkillIsClampedAtZeroAndOne()
        {
            Assert.AreEqual(0f, DifficultyRules.Apply(Setup(0.1f), Difficulty.SuperEasy, Team.Blue).blueSkill, 1e-6f);
            Assert.AreEqual(1f, DifficultyRules.Apply(Setup(1f), Difficulty.Hard, Team.Blue).blueSkill, 1e-6f);
        }

        [Test]
        public void TheEnemySideIsTheOneThatChanges_Red()
        {
            // You play blue: red is the enemy
            var result = DifficultyRules.Apply(Setup(), Difficulty.SuperEasy, Team.Red);
            Assert.AreEqual(0.2f, result.redSkill, 1e-6f);
            Assert.AreEqual(0.5f, result.blueSkill, 1e-6f, "your side keeps its skill");
            Assert.AreEqual(4, result.blueSize, "your side keeps its size");
        }

        [Test]
        public void EveryDifficultyIsFourVsFour_OnEitherSide()
        {
            // The game loop's own setup (a new BattleSetup) at every difficulty, with you on either team: 4 vs 4 in the match's rules too
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
                foreach (var enemy in new[] { Team.Red, Team.Blue })
                {
                    var result = DifficultyRules.Apply(new BattleSetup { yourTeam = enemy.Other() }, d, enemy);
                    Assert.AreEqual(4, result.redSize, $"{d}, enemy {enemy}: red");
                    Assert.AreEqual(4, result.blueSize, $"{d}, enemy {enemy}: blue");
                    var rules = new MatchRules();
                    result.ToMatchSetup().Apply(rules);
                    Assert.AreEqual(4, rules.SizeOf(Team.Red), $"{d}, enemy {enemy}: red in the match");
                    Assert.AreEqual(4, rules.SizeOf(Team.Blue), $"{d}, enemy {enemy}: blue in the match");
                }
        }

        [Test]
        public void TheFriendlySideIsNotChangedByEitherDifficulty()
        {
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                var result = DifficultyRules.Apply(Setup(), d, Team.Blue);
                Assert.AreEqual(0.5f, result.redSkill, 1e-6f, d.ToString());
                Assert.AreEqual(4, result.redSize, d.ToString());
            }
        }

        [Test]
        public void TheInputIsNotChanged()
        {
            var setup = Setup();
            DifficultyRules.Apply(setup, Difficulty.Hard, Team.Blue);
            Assert.AreEqual(0.5f, setup.blueSkill, 1e-6f);
            Assert.AreEqual(Difficulty.Normal, setup.difficulty);
        }

        [Test]
        public void KeysRoundTrip()
        {
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
                Assert.AreEqual(d, DifficultyRules.Parse(DifficultyRules.Key(d)));
            Assert.AreEqual(Difficulty.Normal, DifficultyRules.Parse("nonsense"));
        }
    }
}
