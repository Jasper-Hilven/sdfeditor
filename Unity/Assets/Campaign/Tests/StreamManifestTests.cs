using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfCampaign.Tests
{
    public sealed class StreamManifestTests
    {
        static StreamManifest Sample() => new StreamManifest
        {
            bundles = new[]
            {
                new StreamedBundle { name = "shared-1", file = "shared-1-aaa.bundle", hash = "a", size = 100 },
                new StreamedBundle { name = "fungal-assets", file = "fungal-assets-bbb.bundle", hash = "b", size = 200 },
                new StreamedBundle { name = "fungal", file = "fungal-ccc.bundle", hash = "c", size = 10 },
                new StreamedBundle { name = "meadow", file = "meadow-ddd.bundle", hash = "d", size = 5 },
            },
            entries = new[]
            {
                new StreamedEntry { key = "scene:Fungal", bundles = new[] { "shared-1", "fungal-assets", "fungal", "missing" } },
                new StreamedEntry { key = "scene:Meadow", bundles = new[] { "shared-1", "meadow" } },
            },
        };

        [Test]
        public void BundlesComeInLoadOrderAndSkipUnknownOnes()
        {
            var names = Sample().Bundles("scene:Fungal").Select(b => b.name).ToArray();
            Assert.AreEqual(new[] { "shared-1", "fungal-assets", "fungal" }, names);
            Assert.IsEmpty(Sample().Bundles("scene:Nope"));
            Assert.IsTrue(Sample().Has("scene:Meadow"));
            Assert.IsFalse(Sample().Has("scene:Kitchen"));
        }

        [Test]
        public void RemainingCountsOnlyWhatIsNotInYet()
        {
            var m = Sample();
            Assert.AreEqual(310, m.Remaining("scene:Fungal", null));
            Assert.AreEqual(210, m.Remaining("scene:Fungal", name => name == "shared-1"));
        }

        [Test]
        public void ManifestSurvivesJson()
        {
            var back = StreamManifest.Parse(Sample().ToJson());
            Assert.AreEqual(4, back.bundles.Length);
            Assert.AreEqual("meadow-ddd.bundle", back.Bundle("meadow").file);
            Assert.AreEqual(2, back.entries.Length);
        }

        [Test]
        public void NoManifestMeansNothingIsStreamed()
        {
            Assert.IsTrue(StreamManifest.Parse("").IsEmpty);
            Assert.IsTrue(StreamManifest.Parse(null).IsEmpty);
            Assert.IsFalse(StreamManifest.Parse("").Has("scene:Fungal"));
        }

        static Dictionary<string, IEnumerable<string>> Sevens()
        {
            // Seven levels: assets all use (the soldier, heads, weapons), a pair's, and each one's own
            var map = new Dictionary<string, IEnumerable<string>>();
            foreach (var level in LevelRotation.Levels)
            {
                var name = level.ToLowerInvariant();
                var assets = new List<string> { "Soldier.mesh", "Head.mesh", "Minigun.mesh", "Sparks.mat", $"{level}.mesh" };
                if (name == "kitchen" || name == "bathroom")
                    assets.Add("Tiles.mat");
                map[name] = assets;
            }
            return map;
        }

        [Test]
        public void EveryLevelHasAKeyAndBootIsTheOnlyPlayerScene()
        {
            StreamLayout.Plan(Sevens(), out _, out var entries, LevelRotation.Levels.ToDictionary(l => l.ToLowerInvariant()));
            CollectionAssert.AreEquivalent(LevelRotation.Levels.Select(StreamManifest.SceneKey), entries.Select(e => e.key));
            Assert.AreEqual(7, entries.Count);
            Assert.IsTrue(StreamLayout.IsPlayerScene("Boot"));
            foreach (var level in LevelRotation.Levels)
                Assert.IsFalse(StreamLayout.IsPlayerScene(level), level + " is streamed");
        }

        [Test]
        public void NoAssetIsInTwoBundlesAndTheSceneBundleHoldsOnlyTheScene()
        {
            StreamLayout.Plan(Sevens(), out var bundles, out _);
            var assets = bundles.Where(b => !b.isScene).SelectMany(b => b.assets).ToList();
            CollectionAssert.AllItemsAreUnique(assets);
            Assert.AreEqual(Sevens().SelectMany(p => p.Value).Distinct().Count(), assets.Count, "every asset is in a bundle");
            Assert.AreEqual(7, bundles.Count(b => b.isScene));
            CollectionAssert.AllItemsAreUnique(bundles.Select(b => b.name));
        }

        [Test]
        public void TheAssetsEveryLevelUsesAreOneSharedBundleLoadedFirst()
        {
            StreamLayout.Plan(Sevens(), out var bundles, out var entries);
            var common = bundles.Single(b => b.assets.Contains("Soldier.mesh"));
            StringAssert.StartsWith("shared-", common.name);
            CollectionAssert.AreEquivalent(new[] { "Soldier.mesh", "Head.mesh", "Minigun.mesh", "Sparks.mat" }, common.assets);
            foreach (var entry in entries)
                Assert.AreEqual(common.name, entry.bundles[0], entry.key + " loads the common bundle first");
        }

        [Test]
        public void TheLoadOrderIsSharedThenAssetsThenScene()
        {
            StreamLayout.Plan(Sevens(), out _, out var entries);
            var kitchen = entries.Single(e => e.key == "scene:kitchen").bundles;
            Assert.AreEqual(4, kitchen.Length);
            StringAssert.StartsWith("shared-", kitchen[0]);
            StringAssert.StartsWith("shared-", kitchen[1]);
            Assert.AreEqual("kitchen-assets", kitchen[2]);
            Assert.AreEqual("kitchen", kitchen[3]);
            var meadow = entries.Single(e => e.key == "scene:meadow").bundles;
            CollectionAssert.AreEqual(new[] { meadow[0], "meadow-assets", "meadow" }, meadow);
        }

        static StreamedEntry Entry(string key) => new StreamedEntry { key = key, bundles = new[] { "x" } };
    }
}
