using NUnit.Framework;

namespace SdfCampaign.Tests
{
    public sealed class GameSaveTests
    {
        [Test]
        public void WithNoSaveTheDefaultIsNormalWithNothingPlayed()
        {
            var save = GameSave.Load(new MemoryCampaignStorage());
            Assert.AreEqual(GameSave.CurrentVersion, save.version);
            Assert.AreEqual(Difficulty.Normal, save.Difficulty);
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
            Assert.AreEqual(Difficulty.Normal, save.Difficulty);
            Assert.IsEmpty(save.lastLevels);
        }

        [Test]
        public void GarbageReadsAsTheDefault()
        {
            Assert.AreEqual(Difficulty.Normal, GameSave.Parse("not json at all {").Difficulty);
            Assert.AreEqual(Difficulty.Normal, GameSave.Parse("").Difficulty);
            Assert.AreEqual(Difficulty.Normal, GameSave.Parse(null).Difficulty);
        }

        [Test]
        public void ASaveOfAnotherVersionReadsAsTheDefault()
        {
            var save = GameSave.Parse("{\"version\":99,\"difficulty\":\"hard\",\"lastLevels\":[\"Fungal\"]}");
            Assert.AreEqual(Difficulty.Normal, save.Difficulty);
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
    }
}
