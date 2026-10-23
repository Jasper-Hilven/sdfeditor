using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SdfBattle;

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

            have.Add("Toybox");
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

        [Test]
        public void BossMedals_AtTheirEdges()
        {
            Assert.AreEqual(Medal.Bronze, Medals.OfBoss(4, 2, 100, Difficulty.Normal, false), "a win with many lives lost");
            Assert.AreEqual(Medal.Silver, Medals.OfBoss(3, 3, 900, Difficulty.Normal, false), "3 lives lost is silver, however slow");
            Assert.AreEqual(Medal.Bronze, Medals.OfBoss(4, 0, 100, Difficulty.Normal, false), "4 lives lost is bronze");
            Assert.AreEqual(Medal.Gold, Medals.OfBoss(0, 0, 100, Difficulty.Normal, false));
            Assert.AreEqual(Medal.Gold, Medals.OfBoss(1, 0, 600, Difficulty.Hard, false), "1 life lost, exactly 10 minutes");
            Assert.AreEqual(Medal.Silver, Medals.OfBoss(1, 0, 600.1f, Difficulty.Normal, false), "just over 10 minutes");
            Assert.AreEqual(Medal.Silver, Medals.OfBoss(1, 1, 300, Difficulty.Normal, false), "a life lost in phase 3");
            Assert.AreEqual(Medal.Silver, Medals.OfBoss(2, 0, 300, Difficulty.Normal, false), "2 lives lost in all");
            Assert.AreEqual(Medal.Silver, Medals.OfBoss(0, 0, 300, Difficulty.Easy, false), "not on Easy");
            Assert.AreEqual(Medal.Silver, Medals.OfBoss(0, 0, 300, Difficulty.Normal, true), "not after RETRY EASIER");
        }

        [Test]
        public void RecordBossWin_KeepsTheBestMedal_AndMovesOn()
        {
            var p = Fresh();
            WinThrough(p, "computer");
            p.CompleteCutscene("beat-boss");
            var first = p.RecordBossWin("boss", 3, 1, 700, Difficulty.Normal);
            Assert.AreEqual(Medal.Silver, first.medal);
            Assert.IsTrue(first.newBest);
            Assert.AreEqual("ending", p.NextStep().id);
            var worse = p.RecordBossWin("boss", 5, 2, 400, Difficulty.Normal);
            Assert.AreEqual(Medal.Bronze, worse.medal);
            Assert.IsFalse(worse.newBest, "a worse medal is not a best");
            Assert.AreEqual(Medal.Silver, p.BestMedal("boss"));
            var gold = p.RecordBossWin("boss", 0, 0, 500, Difficulty.Hard);
            Assert.IsTrue(gold.newBest);
            Assert.AreEqual(Medal.Gold, p.BestMedal("boss"));
            Assert.Throws<System.ArgumentException>(() => p.RecordBossWin("beat-boss", 0, 0, 1, Difficulty.Normal), "not a battle");
        }

        [Test]
        public void BossTaunts_ArePrimesFive()
        {
            var lines = Taunts.Of(CampaignDefinition.Boss);
            Assert.AreEqual(5, lines.Count);
            Assert.AreEqual("Silver suits you.", lines[0]);
            Assert.AreEqual("Best of one, final answer.", lines[4]);
            Assert.AreEqual(lines[2], Taunts.Pick(CampaignDefinition.Boss, 3));
            Assert.AreEqual(lines[0], Taunts.Pick(CampaignDefinition.Boss, 6), "they go round");
        }

        [Test]
        public void BossTips_AreForEveryPhase_AndGoRound()
        {
            for (var phase = 1; phase <= 3; phase++)
            {
                var tips = BossTips.Of(phase);
                Assert.GreaterOrEqual(tips.Count, 2, "phase " + phase);
                Assert.AreEqual(tips.Count, tips.Distinct().Count());
                Assert.AreEqual(tips[0], BossTips.Pick(phase, 1));
                Assert.AreEqual(tips[1], BossTips.Pick(phase, 2));
                Assert.AreEqual(tips[0], BossTips.Pick(phase, 1 + tips.Count), "round again");
            }
            Assert.AreEqual(BossTips.Of(1)[0], BossTips.Of(0)[0], "clamped");
            Assert.AreEqual(BossTips.Of(3)[0], BossTips.Of(9)[0], "clamped");
            StringAssert.Contains("shockwave", string.Join(" ", BossTips.Of(3)));
            StringAssert.Contains("electro-gun", string.Join(" ", BossTips.Of(2)));
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

        [Test]
        public void Taunts_FiveForEveryBattle_AndAllDifferent()
        {
            foreach (var battle in Def.Battles())
            {
                var lines = Taunts.Of(battle.id);
                Assert.AreEqual(Taunts.PerArena, lines.Count, battle.id);
                Assert.AreEqual(lines.Count, lines.Distinct().Count(), battle.id + " repeats a taunt");
                Assert.IsTrue(lines.All(l => !string.IsNullOrWhiteSpace(l)), battle.id);
            }
            Assert.IsEmpty(Taunts.Of("nowhere"));
            Assert.AreEqual("", Taunts.Pick("nowhere", 1));
        }

        [Test]
        public void Taunts_GoRoundWithTheLossesInARow()
        {
            var lines = Taunts.Of("meadow");
            Assert.AreEqual(lines[0], Taunts.Pick("meadow", 1));
            Assert.AreEqual(lines[1], Taunts.Pick("meadow", 2));
            Assert.AreEqual(lines[4], Taunts.Pick("meadow", 5));
            Assert.AreEqual(lines[0], Taunts.Pick("meadow", 6));
            Assert.AreEqual(lines[0], Taunts.Pick("meadow", 0), "never before the first");
        }

        [Test]
        public void RetryEasier_WinsEarnAtMostSilver_AndTheLossStreakSurvivesASave()
        {
            var store = new MemoryCampaignStorage();
            var p = Fresh(store);
            p.RecordLoss("meadow");
            p.RecordLoss("meadow");
            Assert.IsTrue(Fresh(store).CanRetryEasier("meadow"), "two losses in a row are saved");
            var win = p.RecordWin("meadow", 2, 0, 60, Difficulty.Normal, retriedEasier: true);
            Assert.AreEqual(Medal.Silver, win.medal, "a flawless fast win that way is Silver, not Gold");
            Assert.IsTrue(win.newBest);
            Assert.IsFalse(Fresh(store).CanRetryEasier("meadow"), "the win clears the streak in the save");
            var again = p.RecordWin("meadow", 2, 1, 300, Difficulty.Normal);
            Assert.AreEqual(Medal.Bronze, again.medal);
            Assert.IsFalse(again.newBest, "a worse medal is not a new best");
            Assert.AreEqual(Medal.Silver, p.BestMedal("meadow"));
        }

        // --- the final boss's setup (ticket 33.4) ---

        [Test]
        public void BossStep_IsABossObjective_WithPrimesCrewAndSkill()
        {
            var setup = Def.Find("boss").setup;
            Assert.IsTrue(setup.IsBoss);
            Assert.AreEqual(SdfBattle.MatchObjective.Boss, setup.ToMatchSetup().objective);
            Assert.AreEqual(4, setup.redSize);
            Assert.AreEqual(5, setup.blueSize, "Prime's crew: four, and the Participation Award for Sore Loser");
            Assert.AreEqual(0.70f, setup.blueSkill, 1e-4f);
            Assert.AreEqual(0.60f, setup.redSkill, 1e-4f);
            Assert.AreEqual(8, setup.blueRespawnSeconds);
            Assert.IsTrue(setup.sporesOn);
            Assert.AreEqual(1, setup.bossStartPhase);
            Assert.IsFalse(Def.Find("computer").setup.IsBoss);
        }

        [Test]
        public void TheBoss_SlotsInByData_AsTheLastBattle_OnTheArenaScene()
        {
            var boss = Def.Find("boss");
            Assert.AreEqual("Arena", boss.scene, "33.9: the Champion's Arena is Assets/Scenes/Arena.unity");
            Assert.AreEqual("Arena", Def.Find("beat-boss").scene, "B7 plays in the same scene");
            Assert.AreSame(boss, Def.Battles().Last(), "the last arena of Level Select");
            Assert.IsTrue(boss.setup.IsBoss);
            Assert.AreEqual(MatchObjective.Boss, boss.setup.objective);
            // Until the scene is in the build the step is skipped with its beat, the ending follows the computer
            var p = Fresh(null, scene => scene != "Arena");
            Assert.IsFalse(Walk(p).Contains("boss"));
        }

        [Test]
        public void BossSetup_FollowsTheDifficulty_AndRetryEasier()
        {
            var setup = Def.Find("boss").setup;
            var easy = DifficultyRules.Apply(setup, Difficulty.Easy);
            var normal = DifficultyRules.Apply(setup, Difficulty.Normal);
            var hard = DifficultyRules.Apply(setup, Difficulty.Hard);
            Assert.AreEqual(5, easy.BossRules().lives);
            Assert.AreEqual(3, normal.BossRules().lives);
            Assert.AreEqual(2, hard.BossRules().lives);
            Assert.AreEqual(20300, easy.BossRules().TotalHealth);
            Assert.AreEqual(20300, normal.BossRules().TotalHealth);
            Assert.AreEqual(25400, hard.BossRules().TotalHealth);
            Assert.AreEqual(0.55f, easy.blueSkill, 1e-4f, "the crew's skill is the campaign's, 0.55 / 0.7 / 0.85");
            Assert.AreEqual(0.70f, normal.blueSkill, 1e-4f);
            Assert.AreEqual(0.85f, hard.blueSkill, 1e-4f);
            Assert.AreEqual(SdfBattle.MatchObjective.Boss, hard.objective, "the objective survives the copy");

            var retry = DifficultyRules.Apply(setup, Difficulty.Hard, 1);
            Assert.AreEqual(0.6f, retry.BossRules().damageScale, "Easy's damage row");
            Assert.AreEqual(1.3f, retry.BossRules().windUpScale, "Easy's wind-up row");
            Assert.AreEqual(25400, retry.BossRules().TotalHealth, "on the chosen difficulty's health");
        }

        // --- beats, the sequencer and the warm-up (ticket 32.7) ---

        [Test]
        public void Beats_AreDataForAllSeven_PlayedInTheNextBattlesScene()
        {
            foreach (var id in new[] { "beat-fungal", "beat-chessboard", "beat-toybox", "beat-kitchen", "beat-bathroom", "beat-computer", "beat-boss" })
            {
                var beat = Beats.Find(id);
                Assert.IsNotNull(beat, id);
                var step = Def.Find(id);
                var battle = Def.Steps[Def.IndexOf(id) + 1];
                Assert.IsTrue(battle.IsBattle);
                Assert.AreEqual(battle.scene, step.scene, "a beat plays in the scene of the battle after it");
                Assert.AreEqual(step.title, beat.title);
                Assert.That(beat.shots.Length, Is.InRange(3, 7));
                Assert.That(beat.Seconds, Is.InRange(25f, 60f), "30 to 60 seconds, give or take the opening card");
                foreach (var shot in beat.shots)
                {
                    Assert.IsNotNull(shot.move);
                    if (!string.IsNullOrEmpty(shot.move.actor))
                        Assert.IsNotNull(beat.Actor(shot.move.actor), $"{id}: no actor {shot.move.actor}");
                    // In the boss's arena Prime is the real, giant Prime (Anchor.Prime), not a puppet of the beat
                    var nativePrime = shot.speaker == Beats.Prime && beat.shots.Any(s => s.move.anchor == Anchor.Prime && string.IsNullOrEmpty(s.move.actor));
                    if (!string.IsNullOrEmpty(shot.speaker) && shot.speaker != Beats.Ref && !nativePrime)
                        Assert.IsNotNull(beat.Actor(shot.speaker), $"{id}: the speaker {shot.speaker} is on the stage");
                }
            }
            Assert.IsFalse(Beats.Has("meadow"));
            foreach (var step in Def.Steps)
                if (step.kind == StepKind.Cutscene && step.id.StartsWith("beat-"))
                    Assert.IsTrue(Beats.Has(step.id), step.id + " has beat data");
        }

        [Test]
        public void Sequencer_RunsTheShotsInOrder_ThenTheArrival_ThenIsDone()
        {
            var beat = Beats.Find("beat-fungal");
            var seq = new BeatSequencer(beat);
            Assert.AreEqual(BeatStage.Shots, seq.Stage);
            Assert.AreEqual(0, seq.ShotIndex);
            seq.Tick(beat.shots[0].seconds + 0.01f, false);
            Assert.AreEqual(1, seq.ShotIndex);
            Assert.AreEqual(0, seq.ShotTime, 0.05f);
            for (var i = 1; i < beat.shots.Length; i++)
                seq.Tick(beat.shots[i].seconds + 0.01f, false);
            Assert.AreEqual(BeatStage.Arrival, seq.Stage);
            Assert.IsFalse(seq.Skipped);
            seq.Tick(BeatSequencer.ArrivalSeconds + 0.01f, false);
            Assert.IsTrue(seq.Done);
            Assert.AreEqual(1f, seq.Arrival);
        }

        [Test]
        public void Sequencer_AClickMovesOnEarly_ButNotInTheShotsFirstMoments()
        {
            var seq = new BeatSequencer(Beats.Find("beat-chessboard"));
            seq.Advance();
            Assert.AreEqual(0, seq.ShotIndex, "a click at the very start of a shot does not eat it");
            seq.Tick(0.5f, false);
            seq.Advance();
            Assert.AreEqual(1, seq.ShotIndex);
            Assert.AreEqual(0, seq.ShotTime);
        }

        [Test]
        public void Sequencer_HoldingSkipForPointSevenSecondsJumpsToTheHandOver()
        {
            var seq = new BeatSequencer(Beats.Find("beat-fungal"));
            seq.Tick(0.3f, true);
            seq.Tick(0.3f, true);
            Assert.AreEqual(BeatStage.Shots, seq.Stage, "0.6 s is not enough");
            Assert.AreEqual(0.6f / SkipHold.Seconds, seq.SkipProgress, 0.01f);
            seq.Tick(0.2f, true);
            Assert.AreEqual(BeatStage.Arrival, seq.Stage);
            Assert.IsTrue(seq.Skipped);
        }

        [Test]
        public void Sequencer_LettingGoOfSkipStartsTheHoldOver()
        {
            var seq = new BeatSequencer(Beats.Find("beat-fungal"));
            seq.Tick(0.6f, true);
            seq.Tick(0.1f, false);
            seq.Tick(0.6f, true);
            Assert.AreEqual(BeatStage.Shots, seq.Stage);
            Assert.IsFalse(seq.Skipped);
        }

        [Test]
        public void Sequencer_TheCaptionFadesInAndOutAndIsEmptyWithoutALine()
        {
            var beat = Beats.Find("beat-fungal");
            var seq = new BeatSequencer(beat);
            Assert.AreEqual(0, seq.CaptionAlpha, 1e-4f, "faded in from nothing");
            seq.Tick(1f, false);
            Assert.AreEqual(1, seq.CaptionAlpha, 1e-4f);
            seq.Tick(beat.shots[0].seconds - 1f - 0.05f, false);
            Assert.Less(seq.CaptionAlpha, 0.5f, "fading out at the end");
            var last = new BeatSequencer(new[] { beat.shots.Last() });
            last.Tick(1f, false);
            Assert.AreEqual(0, last.CaptionAlpha, "a shot with no line has no caption");
        }

        // --- the boss fight's transitions and victory (ticket 33.8) ---

        static readonly string[] BossBeatIds = { BossBeats.Transition12, BossBeats.Transition23, BossBeats.Victory };

        static readonly HashSet<string> KnownCues = new HashSet<string>
        {
            BossBeats.CopterRises, BossBeats.BalconyFalls, BossBeats.CopterDown, BossBeats.CrownFlies, BossBeats.PrimeClimbs,
            BossBeats.Shrink, BossBeats.CrownRolls, BossBeats.Cheer, BossBeats.PixelBreak,
        };

        static IEnumerable<string> CuesOf(Beat beat) =>
            beat.shots.SelectMany(s => s.cue.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));

        [Test]
        public void BossBeats_AreWellFormed_TransitionsAtMostSixSeconds_VictoryAboutTwenty()
        {
            foreach (var id in BossBeatIds)
            {
                var beat = Beats.Find(id);
                Assert.IsNotNull(beat, id);
                Assert.IsFalse(Def.Steps.Any(s => s.id == id), "not a campaign step: the fight plays them");
                foreach (var shot in beat.shots)
                {
                    Assert.IsNotNull(shot.move, id);
                    Assert.Greater(shot.seconds, 0.5f, id);
                    // A line is spoken by the boss's cast: Prime, the Ref, 42 (no puppet: their view)
                    if (!string.IsNullOrEmpty(shot.line))
                        Assert.Contains(shot.speaker, new[] { Beats.Prime, Beats.Ref, Cast.You }, $"{id}: {shot.line}");
                    else
                        Assert.AreEqual("", shot.speaker, "no line, no speaker");
                    Assert.IsEmpty(shot.move.actor ?? "", "Prime and 42 are anchors in the arena, not actors");
                    foreach (var cue in shot.cue.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                        Assert.IsTrue(KnownCues.Contains(cue), $"{id}: unknown cue '{cue}'");
                }
            }
            Assert.LessOrEqual(Beats.Find(BossBeats.Transition12).Seconds, 6.001f);
            Assert.LessOrEqual(Beats.Find(BossBeats.Transition23).Seconds, 6.001f);
            Assert.That(Beats.Find(BossBeats.Victory).Seconds, Is.InRange(18f, 26f), "about 20 s");
        }

        [Test]
        public void BossBeats_SayWhatTicket33Says_InOrder()
        {
            var t12 = Beats.Find(BossBeats.Transition12);
            Assert.AreEqual(new[] { Beats.Prime, Beats.Ref, Beats.Prime }, t12.shots.Select(s => s.speaker).ToArray());
            StringAssert.Contains("timeout", t12.shots[0].line);
            StringAssert.Contains("helicopter", t12.shots[2].line);
            CollectionAssert.AreEqual(new[] { BossBeats.CopterRises, BossBeats.BalconyFalls }, CuesOf(t12), "the copter rises as Prime asks for it");

            var t23 = Beats.Find(BossBeats.Transition23);
            Assert.AreEqual(new[] { "", Beats.Prime, Beats.Ref, Cast.You }, t23.shots.Select(s => s.speaker).ToArray(), "the pfft, Prime, the Ref, 42");
            StringAssert.Contains("rule four", t23.shots[3].line);
            CollectionAssert.AreEqual(new[] { BossBeats.CopterDown, BossBeats.CrownFlies, BossBeats.PrimeClimbs }, CuesOf(t23));

            var victory = Beats.Find(BossBeats.Victory);
            CollectionAssert.AreEqual(
                new[] { BossBeats.Shrink, BossBeats.CrownRolls, BossBeats.Cheer, BossBeats.PixelBreak }, CuesOf(victory),
                "the shrink, the crown to 42 and on, the crowd, the break-up");
            StringAssert.Contains("recount", victory.shots[1].line);
            StringAssert.Contains("Still second", victory.shots[2].line);
            Assert.AreEqual(Beats.Ref, victory.shots[4].speaker);
            Assert.AreEqual(Cast.You, victory.shots[5].speaker, "42 hands the crown on");
            var seven = victory.Actor(Cast.Red[1]);
            Assert.IsNotNull(seven, "Mushroom 7 is on the stage to wear the crown");
            Assert.AreEqual(Anchor.You, seven.anchor, "beside 42");
            Assert.AreEqual(Beats.SevenRight, seven.right);
            Assert.AreEqual(Beats.SevenForward, seven.forward);
        }

        [Test]
        public void BossBeats_B7_PrimeIsTheRealOneInTheArena()
        {
            var b7 = Beats.Find("beat-boss");
            Assert.IsNull(b7.Actor(Beats.Prime), "he is the giant Prime of the arena, not a puppet");
            Assert.IsTrue(b7.shots.Where(s => s.speaker == Beats.Prime).All(s => s.move.anchor == Anchor.Prime), "his shots are on him");
            Assert.IsEmpty(CuesOf(b7), "B7 changes nothing in the arena");
        }

        [Test]
        public void BossBeats_ThePronounsHold_AndTheOtherBeatsHaveNoCues()
        {
            foreach (var id in BossBeatIds)
                foreach (var shot in Beats.Find(id).shots)
                {
                    var line = " " + shot.line.ToLower() + " ";
                    if (shot.speaker == Cast.You)
                        Assert.IsFalse(line.Contains(" she ") || line.Contains(" he "), "42 is they/them");
                }
            foreach (var id in new[] { "beat-fungal", "beat-chessboard", "beat-toybox", "beat-kitchen", "beat-bathroom", "beat-computer", "ending" })
                Assert.IsEmpty(CuesOf(Beats.Find(id)), id);
        }

        [Test]
        public void BossBeats_Skipping_HoldingTheKeysEndsAtOnce_AndAClickMovesOn()
        {
            foreach (var id in BossBeatIds)
            {
                var seq = new BeatSequencer(Beats.Find(id));
                seq.Tick(0.4f, true);
                seq.Tick(0.4f, true);
                Assert.IsTrue(seq.Skipped, id + ": held for 0.7 s");
                Assert.AreNotEqual(BeatStage.Shots, seq.Stage);
            }
            var clicked = new BeatSequencer(Beats.Find(BossBeats.Transition12));
            clicked.Tick(0.5f, false);
            clicked.Advance();
            Assert.AreEqual(1, clicked.ShotIndex);
        }

        [Test]
        public void BossBeats_ForTransition_NamesOnlyTheTwoTransitions()
        {
            Assert.AreEqual(BossBeats.Transition12, BossBeats.ForTransition(BossPhase.Transition12));
            Assert.AreEqual(BossBeats.Transition23, BossBeats.ForTransition(BossPhase.Transition23));
            foreach (var phase in new[] { BossPhase.Intro, BossPhase.Phase1, BossPhase.Phase2, BossPhase.Phase3, BossPhase.Victory, BossPhase.Defeat })
                Assert.IsNull(BossBeats.ForTransition(phase), phase.ToString());
            Assert.LessOrEqual(BossBeats.QuickTransitionSeconds, 2f, "a retry's transition is short");
        }

        [Test]
        public void WarmUp_EachPromptIsClearedByDoingIt_InOrder()
        {
            var warm = WarmUp.Default(true);
            Assert.AreEqual(4, warm.Count);
            StringAssert.Contains("WASD", warm.Prompt.text);
            warm.Report(WarmUpAction.Jump);
            Assert.AreEqual(0, warm.Index, "a jump before its prompt does not count");
            warm.Report(WarmUpAction.Walk);
            Assert.AreEqual(0, warm.Index, "walking alone does not clear walk and look");
            warm.Report(WarmUpAction.Look);
            Assert.AreEqual(1, warm.Index);
            warm.Report(WarmUpAction.Jump);
            StringAssert.Contains("MINIGUN", warm.Prompt.text);
            warm.Report(WarmUpAction.Fire);
            Assert.AreEqual(2, warm.Index, "firing early does not clear the pod prompt");
            warm.Report(WarmUpAction.TakeMinigun);
            StringAssert.Contains("fire", warm.Prompt.text);
            warm.Report(WarmUpAction.Fire);
            Assert.IsTrue(warm.Finished);
            Assert.IsNull(warm.Prompt);
        }

        [Test]
        public void WarmUp_AMinigunTakenEarlyClearsItsPromptAtOnce_AndWithoutAPodThereAreTwoPrompts()
        {
            var warm = WarmUp.Default(true);
            warm.Report(WarmUpAction.TakeMinigun);
            warm.Report(WarmUpAction.Walk | WarmUpAction.Look);
            warm.Report(WarmUpAction.Jump);
            StringAssert.Contains("fire", warm.Prompt.text, "the pod prompt went by with it");
            Assert.AreEqual(2, WarmUp.Default(false).Count);
        }

        [Test]
        public void TheMeadowAsksForTheWarmUp_OnlyOnAFirstPlay()
        {
            Assert.IsTrue(Def.Find("meadow").setup.warmUp);
            Assert.IsTrue(Def.Find("meadow").setup.ToMatchSetup().warmUp);
            Assert.IsFalse(Def.Find("meadow").setup.sporesOn, "spores off in the meadow");
            Assert.IsFalse(Def.Find("fungal").setup.warmUp);
            Assert.IsFalse(Def.Find("chessboard").setup.warmUp);
        }

        [Test]
        public void ContinueAfterAReload_GoesToTheBeatAfterAWin_ThenToItsBattle()
        {
            var store = new MemoryCampaignStorage();
            var p = Fresh(store, scene => scene == "Boot" || scene == "Meadow" || scene == "Fungal" || scene == "Chessboard");
            p.CompleteCutscene("intro");
            p.RecordWin("meadow", 2, 0, 300, Difficulty.Normal);
            var again = Fresh(store, scene => scene == "Boot" || scene == "Meadow" || scene == "Fungal" || scene == "Chessboard");
            Assert.AreEqual("beat-fungal", again.NextStep().id, "a beat not yet seen plays first");
            again.CompleteCutscene("beat-fungal");
            Assert.AreEqual("fungal", Fresh(store, scene => scene != "Toybox").NextStep().id);
        }

        // --- the remaining arenas by data (ticket 32.9) ---

        [Test]
        public void TheWholeOrder_FlowsThroughWithEveryScenePresent_AndUnlocksOneArenaAtATime()
        {
            var scenes = new HashSet<string>(Def.Steps.Select(s => s.scene));
            var p = Fresh(null, scenes.Contains);
            var played = new List<string>();
            for (var step = p.NextStep(); step != null; step = p.NextStep())
            {
                played.Add(step.id);
                if (step.IsBattle)
                {
                    Assert.IsTrue(p.IsUnlocked(step.id), step.id);
                    var next = Def.Steps.Skip(Def.IndexOf(step.id) + 1).FirstOrDefault(s => s.IsBattle);
                    if (next != null) Assert.IsFalse(p.IsUnlocked(next.id), next.id + " stays locked until " + step.id + " is won");
                    p.RecordWin(step.id, 3, 1, 400, Difficulty.Normal);
                }
                else p.CompleteCutscene(step.id);
                Assert.Less(played.Count, 40, "the walk must end");
            }
            CollectionAssert.AreEqual(Def.Steps.Select(s => s.id).ToArray(), played.ToArray());
            Assert.IsTrue(p.Completed);
        }

        static List<string> Walk(CampaignProgress p)
        {
            var ids = new List<string>();
            for (var step = p.NextStep(); step != null && ids.Count < 60; step = p.NextStep())
            {
                ids.Add(step.id);
                if (step.IsBattle) p.RecordWin(step.id, 3, 1, 400, Difficulty.Normal);
                else p.CompleteCutscene(step.id);
            }
            return ids;
        }

        [Test]
        public void AFakeDefinition_WithAnAddedLevel_FlowsWithoutAnyOtherChange()
        {
            var steps = Def.Steps.ToList();
            var at = Def.IndexOf("ending");
            steps.Insert(at, new CampaignStep("beat-extra", StepKind.Cutscene, "Extra", "Extra"));
            steps.Insert(at + 1, new CampaignStep("extra", StepKind.Battle, "Extra", "Extra", Def.Find("computer").setup));
            var def = new CampaignDefinition(steps);
            var all = new HashSet<string>(steps.Select(s => s.scene));
            var with = Walk(CampaignProgress.Load(def, new MemoryCampaignStorage(), all.Contains));
            var without = Walk(CampaignProgress.Load(def, new MemoryCampaignStorage(), scene => scene != "Extra" && all.Contains(scene)));
            Assert.AreEqual(9, def.Battles().Count());
            CollectionAssert.AreEqual(steps.Select(s => s.id).ToArray(), with.ToArray());
            CollectionAssert.AreEqual(steps.Select(s => s.id).Where(id => id != "beat-extra" && id != "extra").ToArray(), without.ToArray());
        }

        [Test]
        public void EachMissingScene_IsSkippedWithItsBeat_AndTheRestOfTheOrderHolds()
        {
            foreach (var missing in new[] { "Toybox", "Kitchen", "Bathroom", "Computer", "Arena" })
            {
                var p = Fresh(null, scene => scene != missing);
                var seen = Walk(p);
                var expected = Def.Steps.Where(s => s.scene != missing).Select(s => s.id).ToArray();
                CollectionAssert.AreEqual(expected, seen.ToArray(), missing);
                Assert.IsTrue(p.Completed, missing);
            }
        }

        // --- the ending and the credits (ticket 32.8) ---

        [Test]
        public void EndingAndCredits_PlayOverBoot_SoTheyAreNeverSkippedAsMissingScenes()
        {
            Assert.AreEqual("Boot", Def.Find("ending").scene, "Home.unity is editor-only; the ending plays over Boot's backdrop");
            Assert.AreEqual("Boot", Def.Find("credits").scene);
            var p = Fresh(null, scene => scene == "Boot" || scene == "Meadow" || scene == "Fungal" || scene == "Chessboard");
            WinThrough(p, "chessboard");
            Assert.AreEqual("beat-toybox", p.NextId);
            Assert.AreEqual("ending", p.NextStep().id, "the arenas to come have no scene; the ending is reached");
            p.CompleteCutscene("ending");
            Assert.AreEqual("credits", p.NextStep().id);
            Assert.IsFalse(p.Completed, "complete only once the credits are seen");
            p.CompleteCutscene("credits");
            Assert.IsTrue(p.Completed);
        }

        [Test]
        public void TheEnding_IsTheThreeLinesOfSection3_WithNoActorsAndNoCameraMove()
        {
            var ending = Beats.Find("ending");
            Assert.IsNotNull(ending);
            Assert.AreEqual(0, ending.actors.Length, "no props, no puppets: it plays over the GUI's own shapes");
            var lines = ending.shots.Where(s => !string.IsNullOrEmpty(s.line)).ToList();
            Assert.AreEqual(Beats.Ref, lines.First(s => s.line.Contains("Champion")).speaker);
            Assert.AreEqual(Beats.Prime, lines.First(s => s.line.Contains("Best of three")).speaker);
            Assert.AreEqual(Cast.You, lines.First(s => s.line.Contains("minigun")).speaker);
            StringAssert.Contains("actual rulebook", lines.First(s => s.speaker == Beats.Ref).line);
            Assert.That(ending.Seconds, Is.InRange(10f, 30f));
            var seq = new BeatSequencer(ending);
            seq.Tick(0.1f, true);
            seq.Tick(SkipHold.Seconds, true);
            Assert.IsTrue(seq.Skipped, "holding the keys jumps to the end, on the first viewing too");
        }

        [Test]
        public void Credits_NameTheAuthorTheToolsAndBothFontNotices()
        {
            Assert.IsTrue(CreditsRoll.HasFontNotices());
            var text = CreditsRoll.RichText();
            StringAssert.Contains(CreditsRoll.Author, text);
            StringAssert.Contains("Unity", text);
            StringAssert.Contains("Fredoka", text);
            StringAssert.Contains("Lilita One", text);
            StringAssert.Contains("SIL Open Font License", text);
            Assert.IsFalse(text.ToLowerInvariant().Contains("online"), "single player, no talk of online play");
        }

        [Test]
        public void CreditsScroll_TakesFortyFiveSeconds_AndTheTextLeavesAtTheTop()
        {
            var scroll = new CreditsScroll();
            Assert.AreEqual(0f, scroll.Offset(1080, 2000), 0.001f);
            for (var i = 0; i < 225; i++) scroll.Tick(0.1f, false);
            Assert.AreEqual(0.5f, scroll.Progress, 0.001f);
            Assert.AreEqual((1080 + 2000) * 0.5f, scroll.Offset(1080, 2000), 0.5f);
            Assert.IsFalse(scroll.Done);
            for (var i = 0; i < 226; i++) scroll.Tick(0.1f, false);
            Assert.IsTrue(scroll.Done);
            Assert.IsFalse(scroll.Skipped);
            Assert.AreEqual(1080 + 2000, scroll.Offset(1080, 2000), 0.001f);
        }

        [Test]
        public void CreditsScroll_SkipsWhenTheKeysAreHeldFor07Seconds_ButNotWhenTapped()
        {
            var tapped = new CreditsScroll();
            tapped.Tick(0.5f, true);
            tapped.Tick(0.1f, false);
            tapped.Tick(0.5f, true);
            Assert.IsFalse(tapped.Done, "letting go starts the hold over");
            var held = new CreditsScroll();
            held.Tick(0.4f, true);
            Assert.That(held.SkipProgress, Is.InRange(0.5f, 0.6f));
            held.Tick(0.4f, true);
            Assert.IsTrue(held.Done);
            Assert.IsTrue(held.Skipped);
        }
    }
}
