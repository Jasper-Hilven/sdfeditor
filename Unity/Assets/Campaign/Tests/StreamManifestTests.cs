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
                new StreamedBundle { name = "comic-beat-fungal", file = "comic-beat-fungal-ddd.bundle", hash = "d", size = 5, comic = "beat-fungal" },
                new StreamedBundle { name = "comic-ending", file = "comic-ending-eee.bundle", hash = "e", size = 7, comic = "ending" },
            },
            entries = new[]
            {
                new StreamedEntry { key = "scene:Fungal", bundles = new[] { "shared-1", "fungal-assets", "fungal", "comic-beat-fungal", "missing" } },
                new StreamedEntry { key = "comic:beat-fungal", bundles = new[] { "comic-beat-fungal" } },
                new StreamedEntry { key = "comic:ending", bundles = new[] { "comic-ending" } },
            },
        };

        [Test]
        public void BundlesComeInLoadOrderAndSkipUnknownOnes()
        {
            var names = Sample().Bundles("scene:Fungal").Select(b => b.name).ToArray();
            Assert.AreEqual(new[] { "shared-1", "fungal-assets", "fungal", "comic-beat-fungal" }, names);
            Assert.IsEmpty(Sample().Bundles("scene:Nope"));
            Assert.IsTrue(Sample().Has("comic:ending"));
            Assert.IsFalse(Sample().Has("scene:Meadow"));
        }

        [Test]
        public void RemainingCountsOnlyWhatIsNotInYet()
        {
            var m = Sample();
            Assert.AreEqual(315, m.Remaining("scene:Fungal", null));
            Assert.AreEqual(215, m.Remaining("scene:Fungal", name => name == "shared-1"));
        }

        [Test]
        public void ManifestSurvivesJson()
        {
            var back = StreamManifest.Parse(Sample().ToJson());
            Assert.AreEqual(5, back.bundles.Length);
            Assert.AreEqual("beat-fungal", back.Bundle("comic-beat-fungal").comic);
            Assert.AreEqual(3, back.entries.Length);
        }

        [Test]
        public void NoManifestMeansNothingIsStreamed()
        {
            Assert.IsTrue(StreamManifest.Parse("").IsEmpty);
            Assert.IsTrue(StreamManifest.Parse(null).IsEmpty);
            Assert.IsFalse(StreamManifest.Parse("").Has("scene:Fungal"));
        }

        [Test]
        public void UpcomingIsTheNextArenaAndItsComic()
        {
            var def = CampaignDefinition.Default();
            var m = new StreamManifest
            {
                bundles = new[] { new StreamedBundle { name = "x" } },
                entries = new[]
                {
                    Entry("scene:Fungal"), Entry("comic:beat-fungal"), Entry("scene:Chessboard"), Entry("comic:beat-chessboard"), Entry("comic:ending"),
                },
            };
            Assert.AreEqual(new[] { "scene:Fungal", "comic:beat-fungal" }, StreamPlan.Upcoming(def, CampaignDefinition.Meadow, m));
            Assert.AreEqual(new[] { "scene:Chessboard", "comic:beat-chessboard" }, StreamPlan.Upcoming(def, CampaignDefinition.Fungal, m));
            // After the boss only the ending's comic is left (the credits and the ending play over Boot)
            Assert.AreEqual(new[] { "comic:ending" }, StreamPlan.Upcoming(def, CampaignDefinition.Boss, m));
            Assert.IsEmpty(StreamPlan.Upcoming(def, "nope", m));
        }

        static StreamedEntry Entry(string key) => new StreamedEntry { key = key, bundles = new[] { "x" } };
    }
}
