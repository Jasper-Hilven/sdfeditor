using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfCampaign.Tests
{
    /// <summary>The campaign core (ticket 32.2): order, setups, unlocks, saving, medals and difficulty.</summary>
    public sealed class CampaignTests
    {
        static readonly CampaignDefinition Def = CampaignDefinition.Default();

        static CampaignProgress Fresh(MemoryCampaignStorage store = null, System.Func<string, bool> available = null)
        {
            return CampaignProgress.Load(Def, store ?? new MemoryCampaignStorage(), available);
        }

        /// <summary>Plays the campaign up to and including <paramref name="lastId"/> with plain wins.</summary>
        static void WinThrough(CampaignProgress p, string lastId)
        {
            foreach (var step in Def.Steps)
            {
                if (step.IsBattle) p.RecordWin(step.id, 3, 1, 400, Difficulty.Normal);
                else p.CompleteCutscene(step.id);
                if (step.id == lastId) return;
            }
        }

        // --- definition ---

        [Test]
        public void Order_IsTheCampaignOfTheTicket()
        {
            var ids = Def.Steps.Select(s => s.id).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "intro", "meadow", "beat-fungal", "fungal", "beat-chessboard", "chessboard", "beat-toybox", "toybox", "beat-kitchen",
                "kitchen", "beat-bathroom", "bathroom", "beat-computer", "computer", "beat-boss", "boss", "ending", "credits",
            }, ids);
            Assert.AreEqual(8, Def.Battles().Count());
            Assert.AreEqual(StepKind.Credits, Def.Steps.Last().kind);
        }

        [Test]
        public void Setups_FollowTheTable()
        {
            var meadow = Def.Find("meadow").setup;
            Assert.AreEqual((3, 3, 0.15f, 0.35f), (meadow.redSize, meadow.blueSize, meadow.blueSkill, meadow.redSkill));
            Assert.AreEqual((2, 480f, false, true), (meadow.capturesToWin, meadow.timeLimitSeconds, meadow.sporesOn, meadow.warmUp));
            Assert.IsTrue(meadow.singleRound);
            CollectionAssert.AreEqual(new[] { "Mushroom 42", "Mushroom 7", "Mushroom 13" }, meadow.redNames);

            var fungal = Def.Find("fungal").setup;
            Assert.AreEqual(4, fungal.redSize);
            Assert.IsTrue(fungal.sporesOn);
            Assert.IsFalse(fungal.warmUp);
            Assert.AreEqual("Mushroom 99", fungal.redNames[3]);

            var bathroom = Def.Find("bathroom").setup;
            Assert.AreEqual(5, bathroom.blueSize);
            Assert.AreEqual("Participation Award", bathroom.blueNames[4]);
            Assert.AreEqual(5f, bathroom.blueRespawnSeconds);

            var computer = Def.Find("computer").setup;
            Assert.AreEqual((0.70f, 0.60f, 720f, 3f), (computer.blueSkill, computer.redSkill, computer.timeLimitSeconds, computer.blueRespawnSeconds));
        }

        [Test]
        public void Setups_NamesMatchTeamSizes_AndSkillClimbs()
        {
            var last = 0f;
            foreach (var b in Def.Battles())
            {
                Assert.AreEqual(b.setup.redSize, b.setup.redNames.Length, b.id);
                Assert.AreEqual(b.setup.blueSize, b.setup.blueNames.Length, b.id);
                Assert.GreaterOrEqual(b.setup.blueSkill, last, b.id);
                last = b.setup.blueSkill;
            }
        }

        // --- progress ---

        [Test]
        public void FreshSave_StartsAtTheIntro()
        {
            var p = Fresh();
            Assert.IsFalse(p.HasSave);
            Assert.AreEqual("intro", p.NextStep().id);
            Assert.IsTrue(p.IsUnlocked("intro"));
            Assert.IsTrue(p.IsUnlocked("meadow"), "the first battle is open");
            Assert.IsFalse(p.IsUnlocked("fungal"));
            Assert.AreEqual(Difficulty.Normal, p.Difficulty);
        }

        [Test]
        public void Unlocks_AfterEachWin()
        {
            var p = Fresh();
            Assert.IsFalse(p.IsUnlocked("beat-fungal"));
            Assert.IsFalse(p.IsUnlocked("fungal"));

            p.CompleteCutscene("intro");
            Assert.AreEqual("meadow", p.NextStep().id);

            var result = p.RecordWin("meadow", 2, 0, 200, Difficulty.Normal);
            Assert.IsTrue(result.newBest);
            Assert.AreEqual("beat-fungal", p.NextStep().id);
            Assert.IsTrue(p.IsUnlocked("fungal"), "the previous battle is won");
            Assert.IsFalse(p.IsUnlocked("chessboard"));

            p.CompleteCutscene("beat-fungal");
            Assert.AreEqual("fungal", p.NextStep().id);
            p.RecordWin("fungal", 3, 2, 500, Difficulty.Normal);
            Assert.IsTrue(p.IsUnlocked("chessboard"));
            Assert.AreEqual("beat-chessboard", p.NextStep().id);
            Assert.AreEqual(2, p.WonCount());
        }

        [Test]
        public void WholeCampaign_EndsInTheCredits()
        {
            var p = Fresh();
            WinThrough(p, "ending");
            Assert.AreEqual("credits", p.NextStep().id);
            Assert.IsFalse(p.Completed);
            p.CompleteCutscene("credits");
            Assert.IsTrue(p.Completed);
            Assert.IsNull(p.NextStep());
            Assert.AreEqual(8, p.WonCount());
        }

        [Test]
        public void ReplayingAnEarlierBattle_DoesNotMoveTheCampaignBack()
        {
            var p = Fresh();
            WinThrough(p, "chessboard");
            Assert.AreEqual("beat-toybox", p.NextStep().id);
            p.RecordWin("meadow", 2, 1, 300, Difficulty.Hard);
            p.CompleteCutscene("beat-fungal");
            Assert.AreEqual("beat-toybox", p.NextId);
        }

        [Test]
        public void NewCampaign_KeepsMedalsAndWins_ResetsTheRest()
        {
            var p = Fresh();
            WinThrough(p, "fungal");
            p.RecordWin("meadow", 2, 0, 100, Difficulty.Normal);
            p.RecordLoss("chessboard");
            p.NewCampaign(Difficulty.Hard);

            Assert.AreEqual("intro", p.NextId);
            Assert.AreEqual(Difficulty.Hard, p.Difficulty);
            Assert.IsFalse(p.Completed);
            Assert.IsFalse(p.Seen("intro"));
            Assert.AreEqual(0, p.LossStreak("chessboard"));
            Assert.IsTrue(p.Won("meadow"));
            Assert.AreEqual(Medal.Gold, p.BestMedal("meadow"));
            Assert.IsTrue(p.IsUnlocked("fungal"), "the arenas stay open in Level Select");
        }

        [Test]
        public void BrokenSave_IsKept_AndAFreshOneStarts()
        {
            var store = new MemoryCampaignStorage();
            store.Set(CampaignProgress.Key, "{ this is not json");
            var p = Fresh(store);
            Assert.IsNotNull(p.LoadProblem);
            Assert.IsFalse(p.HasSave);
            Assert.AreEqual("intro", p.NextStep().id);
            Assert.AreEqual("{ this is not json", store.values[CampaignProgress.BrokenKey]);

            p.CompleteCutscene("intro");
            Assert.IsTrue(Fresh(store).Seen("intro"));
        }

        [Test]
        public void UnknownVersion_IsKeptAsBroken()
        {
            var store = new MemoryCampaignStorage();
            const string json = "{\"version\":7,\"next\":\"fungal\"}";
            store.Set(CampaignProgress.Key, json);
            var p = Fresh(store);
            StringAssert.Contains("version", p.LoadProblem);
            Assert.AreEqual(json, store.values[CampaignProgress.BrokenKey]);
            Assert.AreEqual("intro", p.NextId);
        }

        [Test]
        public void JsonRoundTrip_KeepsEverything()
        {
            var store = new MemoryCampaignStorage();
            var p = Fresh(store);
            p.SetDifficulty(Difficulty.Hard);
            WinThrough(p, "beat-chessboard");
            p.RecordLoss("chessboard");
            p.RecordLoss("chessboard");
            p.RecordWin("meadow", 2, 0, 100, Difficulty.Hard);

            StringAssert.Contains("\"version\":1", store.values[CampaignProgress.Key]);
            var q = Fresh(store);
            Assert.IsTrue(q.HasSave);
            Assert.AreEqual(Difficulty.Hard, q.Difficulty);
            Assert.AreEqual("chessboard", q.NextId);
            Assert.IsTrue(q.Seen("beat-chessboard"));
            Assert.AreEqual(2, q.LossStreak("chessboard"));
            Assert.IsTrue(q.CanRetryEasier("chessboard"));
            var meadow = q.Level("meadow");
            Assert.AreEqual("gold", meadow.medal);
            Assert.AreEqual("2-0", meadow.bestScore);
            Assert.AreEqual("hard", meadow.difficulty);
            Assert.AreEqual(100f, meadow.bestSeconds);
            var before = store.values[CampaignProgress.Key];
            q.Save();
            Assert.AreEqual(before, store.values[CampaignProgress.Key]);
        }

        [Test]
        public void LossStreak_CountsInARow_AndAWinClearsIt()
        {
            var p = Fresh();
            Assert.IsFalse(p.CanRetryEasier("fungal"));
            Assert.AreEqual(1, p.RecordLoss("fungal"));
            Assert.IsFalse(p.CanRetryEasier("fungal"));
            Assert.AreEqual(2, p.RecordLoss("fungal"));
            Assert.IsTrue(p.CanRetryEasier("fungal"));
            Assert.IsFalse(p.CanRetryEasier("meadow"));
            p.RecordWin("fungal", 3, 0, 700, Difficulty.Normal);
            Assert.AreEqual(0, p.LossStreak("fungal"));
        }

        [Test]
        public void RecordWin_KeepsTheBestMedal_AndReportsNewBest()
        {
            var p = Fresh();
            var first = p.RecordWin("fungal", 3, 2, 500, Difficulty.Normal);
            Assert.AreEqual((Medal.Bronze, true), (first.medal, first.newBest));
            var better = p.RecordWin("fungal", 3, 0, 200, Difficulty.Normal);
            Assert.AreEqual((Medal.Gold, true), (better.medal, better.newBest));
            var worse = p.RecordWin("fungal", 3, 2, 100, Difficulty.Normal);
            Assert.AreEqual((Medal.Bronze, false), (worse.medal, worse.newBest));
            Assert.AreEqual(Medal.Gold, p.BestMedal("fungal"));
            var faster = p.RecordWin("fungal", 3, 0, 150, Difficulty.Normal);
            Assert.IsTrue(faster.newBest);
            Assert.AreEqual(150f, p.Level("fungal").bestSeconds);
        }

        [Test]
        public void RecordWin_RejectsANonBattleAndANonWin()
        {
            var p = Fresh();
            Assert.Throws<System.ArgumentException>(() => p.RecordWin("intro", 3, 0, 1, Difficulty.Normal));
            Assert.Throws<System.ArgumentException>(() => p.RecordWin("fungal", 2, 2, 1, Difficulty.Normal));
        }

        // --- unavailable scenes ---

        [Test]
        public void UnavailableStep_IsSkippedByNextStep_AndReachedOnceAvailable()
        {
            var have = new HashSet<string> { "Home", "Meadow", "Fungal", "Chessboard", "Boot" };
            var store = new MemoryCampaignStorage();
            var p = Fresh(store, have.Contains);
            WinThrough(p, "chessboard");

            Assert.AreEqual("beat-toybox", p.NextId);
            Assert.AreEqual("ending", p.NextStep().id, "toy box to boss have no scene yet");
            Assert.IsFalse(p.IsUnlocked("toybox"));

            have.Add("ToyBox");
            Assert.AreEqual("beat-toybox", p.NextStep().id);
            p.CompleteCutscene("beat-toybox");
            Assert.AreEqual("toybox", p.NextStep().id);
            Assert.IsTrue(p.IsUnlocked("toybox"));
            Assert.IsFalse(p.IsUnlocked("kitchen"));
        }

        // --- medals ---

        [Test]
        public void Medals_AtTheirEdges()
        {
            const float limit = 600;
            Assert.AreEqual(Medal.None, Medals.Of(2, 2, 10, limit, Difficulty.Normal, false));
            Assert.AreEqual(Medal.None, Medals.Of(1, 3, 10, limit, Difficulty.Normal, false));
            Assert.AreEqual(Medal.Bronze, Medals.Of(3, 2, 10, limit, Difficulty.Normal, false));
            Assert.AreEqual(Medal.Silver, Medals.Of(3, 1, 10, limit, Difficulty.Normal, false));
            Assert.AreEqual(Medal.Bronze, Medals.Of(2, 1, 10, limit, Difficulty.Normal, false));
            Assert.AreEqual(Medal.Gold, Medals.Of(3, 0, 300, limit, Difficulty.Normal, false), "exactly half the time");
            Assert.AreEqual(Medal.Silver, Medals.Of(3, 0, 300.1f, limit, Difficulty.Normal, false), "just over half");
            Assert.AreEqual(Medal.Gold, Medals.Of(3, 0, 10, limit, Difficulty.Hard, false));
            Assert.AreEqual(Medal.Silver, Medals.Of(3, 0, 10, limit, Difficulty.Easy, false), "not on Easy");
            Assert.AreEqual(Medal.Silver, Medals.Of(3, 0, 10, limit, Difficulty.Normal, true), "not after RETRY EASIER");
            Assert.AreEqual(Medal.Bronze, Medals.Of(1, 0, 10, limit, Difficulty.Normal, true), "a one-capture win stays bronze");
        }

        // --- difficulty ---

        [Test]
        public void Difficulty_EasyAndHard()
        {
            var bathroom = Def.Find("bathroom").setup;
            var easy = DifficultyRules.Apply(bathroom, Difficulty.Easy);
            Assert.AreEqual(0.45f, easy.blueSkill, 1e-5f);
            Assert.AreEqual(4, easy.blueSize);
            Assert.AreEqual(4, easy.blueNames.Length);
            Assert.AreEqual(bathroom.redSkill, easy.redSkill);

            var hard = DifficultyRules.Apply(bathroom, Difficulty.Hard);
            Assert.AreEqual(0.75f, hard.blueSkill, 1e-5f);
            Assert.AreEqual(5, hard.blueSize);

            var normal = DifficultyRules.Apply(bathroom, Difficulty.Normal);
            Assert.AreEqual(0.60f, normal.blueSkill, 1e-5f);
            Assert.AreEqual(0.60f, bathroom.blueSkill, "the source setup is untouched");
        }

        [Test]
        public void Difficulty_Clamps()
        {
            var computer = Def.Find("computer").setup;
            Assert.AreEqual(0.85f, DifficultyRules.Apply(computer, Difficulty.Hard).blueSkill, 1e-5f);

            var sharp = computer.Clone();
            sharp.blueSkill = 0.9f;
            Assert.AreEqual(0.95f, DifficultyRules.Apply(sharp, Difficulty.Hard).blueSkill, 1e-5f, "at most 0.95");

            var meadow = Def.Find("meadow").setup;
            Assert.AreEqual(0f, DifficultyRules.Apply(meadow, Difficulty.Easy).blueSkill, "never below 0");
            Assert.AreEqual(0f, DifficultyRules.Apply(meadow, Difficulty.Easy, 2).blueSkill);
        }

        [Test]
        public void Difficulty_RetryEasier_TakesTenPerPress_TwoAtMost()
        {
            var computer = Def.Find("computer").setup;
            Assert.AreEqual(0.60f, DifficultyRules.Apply(computer, Difficulty.Normal, 1).blueSkill, 1e-5f);
            Assert.AreEqual(0.50f, DifficultyRules.Apply(computer, Difficulty.Normal, 2).blueSkill, 1e-5f);
            Assert.AreEqual(0.50f, DifficultyRules.Apply(computer, Difficulty.Normal, 5).blueSkill, 1e-5f);
            Assert.AreEqual(0.70f, DifficultyRules.Apply(computer, Difficulty.Normal, -1).blueSkill, 1e-5f);
            Assert.AreEqual(0.35f, DifficultyRules.Apply(computer, Difficulty.Easy, 2).blueSkill, 1e-5f);
        }
    }
}
