using NUnit.Framework;

namespace SdfCampaign.Tests
{
    public sealed class GameSaveTests
    {
        [Test]
        public void WithNoSaveTheDefaultIsSuperEasyWithNothingPlayed()
        {
            var save = GameSave.Load(new MemoryCampaignStorage());
            Assert.AreEqual(GameSave.CurrentVersion, save.version);
            Assert.AreEqual(Difficulty.SuperEasy, save.Difficulty);
            Assert.IsEmpty(save.lastLevels);
        }

        [Test]
        public void SavedDifficultyAndLevelsSurviveAReload()
        {
            var storage = new MemoryCampaignStorage();
            var save = new GameSave { Difficulty = Difficulty.Hard, lastLevels = new[] { "Fungal", "Kitchen", "Computer" } };
            save.Save(storage);

            var back = GameSave.Load(storage);
            Assert.AreEqual(Difficulty.Hard, back.Difficulty);
            CollectionAssert.AreEqual(new[] { "Fungal", "Kitchen", "Computer" }, back.lastLevels);
            Assert.AreEqual(GameSave.CurrentVersion, back.version);
        }

        [Test]
        public void EveryDifficultyRoundTrips()
        {
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                var storage = new MemoryCampaignStorage();
                new GameSave { Difficulty = d }.Save(storage);
                Assert.AreEqual(d, GameSave.Load(storage).Difficulty, d.ToString());
            }
        }

        [Test]
        public void TheJsonRoundTripsTheSameFields()
        {
            var save = new GameSave { Difficulty = Difficulty.Easy, lastLevels = new[] { "Toybox" } };
            var back = GameSave.Parse(save.ToJson());
            Assert.AreEqual(Difficulty.Easy, back.Difficulty);
            CollectionAssert.AreEqual(new[] { "Toybox" }, back.lastLevels);
        }

        [Test]
        public void SaveWritesUnderItsOwnKey()
        {
            var storage = new MemoryCampaignStorage();
            new GameSave().Save(storage);
            Assert.IsTrue(storage.values.ContainsKey(GameSave.Key));
            Assert.AreEqual(1, storage.values.Count);
        }

        [Test]
        public void TheOldCampaignSaveIsIgnored()
        {
            var storage = new MemoryCampaignStorage();
            storage.Set("campaign.v1", "{\"version\":1,\"difficulty\":3}");
            var save = GameSave.Load(storage);
            Assert.AreEqual(Difficulty.SuperEasy, save.Difficulty);
            Assert.IsEmpty(save.lastLevels);
        }

        [Test]
        public void GarbageReadsAsTheDefault()
        {
            Assert.AreEqual(Difficulty.SuperEasy, GameSave.Parse("not json at all {").Difficulty);
            Assert.AreEqual(Difficulty.SuperEasy, GameSave.Parse("").Difficulty);
            Assert.AreEqual(Difficulty.SuperEasy, GameSave.Parse(null).Difficulty);
        }

        [Test]
        public void ASaveOfAnotherVersionReadsAsTheDefault()
        {
            var save = GameSave.Parse("{\"version\":99,\"difficulty\":\"hard\",\"lastLevels\":[\"Fungal\"]}");
            Assert.AreEqual(Difficulty.SuperEasy, save.Difficulty);
            Assert.IsEmpty(save.lastLevels);
        }

        [Test]
        public void ASaveWithoutLevelsReadsWithNone()
        {
            var save = GameSave.Parse("{\"version\":1,\"difficulty\":\"easy\"}");
            Assert.AreEqual(Difficulty.Easy, save.Difficulty);
            Assert.IsNotNull(save.lastLevels);
            Assert.IsEmpty(save.lastLevels);
        }

        [Test]
        public void AVersion1SaveMigratesToTheCurrentVersionWithNoHeads()
        {
            var save = GameSave.Parse("{\"version\":1,\"difficulty\":\"hard\",\"lastLevels\":[\"Fungal\"]}");
            Assert.AreEqual(GameSave.CurrentVersion, save.version);
            Assert.AreEqual(Difficulty.Hard, save.Difficulty);
            CollectionAssert.AreEqual(new[] { "Fungal" }, save.lastLevels);
            Assert.IsNotNull(save.heads);
            Assert.IsEmpty(save.heads.unlocked);
            Assert.IsEmpty(save.heads.monthsSeen);
            Assert.AreEqual("", save.heads.current);
            Assert.AreEqual(2, GameSave.CurrentVersion);
        }

        [Test]
        public void TheHeadsSurviveAReload()
        {
            var storage = new MemoryCampaignStorage();
            var save = new GameSave();
            save.heads.unlocked = new[] { "frankenstein", "santa" };
            save.heads.monthsSeen = new[] { 10, 11 };
            save.heads.current = "santa";
            save.Save(storage);
            var back = GameSave.Load(storage);
            CollectionAssert.AreEqual(new[] { "frankenstein", "santa" }, back.heads.unlocked);
            CollectionAssert.AreEqual(new[] { 10, 11 }, back.heads.monthsSeen);
            Assert.AreEqual("santa", back.heads.current);
        }

        [Test]
        public void AMigratedSaveKeepsItsHeadsOnceSavedAgain()
        {
            var storage = new MemoryCampaignStorage();
            storage.Set(GameSave.Key, "{\"version\":1,\"difficulty\":\"easy\"}");
            var save = GameSave.Load(storage);
            SdfHeads.HeadUnlocks.OnMatchWon(new System.DateTime(2026, 10, 5), save.heads);
            save.Save(storage);
            Assert.AreEqual("frankenstein", GameSave.Load(storage).heads.unlocked[0]);
        }

        [Test]
        public void AResetSaveIsAFirstPlayInTheFungalLevelWithTheWarmUpAndTheDefaults()
        {
            var storage = new MemoryCampaignStorage();
            var played = new GameSave { Difficulty = Difficulty.Hard, lastLevels = new[] { "Fungal", "Kitchen", "Computer" } };
            played.heads.unlocked = new[] { "frankenstein", "yeti" };
            played.heads.current = "yeti";
            played.heads.monthsSeen = new[] { 1, 2 };
            played.Save(storage);

            var fresh = GameSave.Reset(storage);
            var back = GameSave.Load(storage);

            foreach (var save in new[] { fresh, back })
            {
                Assert.AreEqual(AutoDifficulty.Start, save.Difficulty);
                Assert.IsEmpty(save.lastLevels);
                Assert.IsEmpty(save.heads.unlocked);
                Assert.IsEmpty(save.heads.monthsSeen);
                Assert.AreEqual("", save.heads.current);
                var rotation = new LevelRotation(save.lastLevels);
                Assert.IsTrue(rotation.IsFirstPlay);
                Assert.AreEqual(LevelRotation.FirstPlayLevel, rotation.First(new System.Random(1)));
                Assert.IsTrue(rotation.WarmUpOn(rotation.First(new System.Random(1))));
            }
        }

        [Test]
        public void ResettingThroughTheLayeredStorageClearsBothLayers()
        {
            var portal = new MemoryCampaignStorage();
            var prefs = new MemoryCampaignStorage();
            new GameSave { Difficulty = Difficulty.Hard, lastLevels = new[] { "Kitchen" } }.Save(prefs);
            new GameSave { Difficulty = Difficulty.Normal, lastLevels = new[] { "Meadow" } }.Save(portal);
            var layered = new LayeredCampaignStorage(portal, prefs);

            GameSave.Reset(layered);

            foreach (var layer in new[] { portal, prefs })
            {
                var save = GameSave.Load(layer);
                Assert.AreEqual(AutoDifficulty.Start, save.Difficulty);
                Assert.IsEmpty(save.lastLevels);
            }
            Assert.IsEmpty(GameSave.Load(layered).lastLevels);
            // The portal alone being wiped cannot bring the old save back from the fallback
            portal.Remove(GameSave.Key);
            Assert.IsEmpty(GameSave.Load(layered).lastLevels);
        }
    }
}
