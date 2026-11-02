using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfCampaign.Tests
{
    public sealed class LevelRotationTests
    {
        [Test]
        public void ThereAreSevenLevelsAndTheMeadowIsOne()
        {
            Assert.AreEqual(7, LevelRotation.Levels.Distinct().Count());
            CollectionAssert.Contains(LevelRotation.Levels, "Meadow");
            Assert.AreEqual(3, LevelRotation.Memory);
        }

        [Test]
        public void NeverPicksOneOfTheLastThree()
        {
            var rotation = new LevelRotation(new[] { "Fungal", "Kitchen", "Computer" });
            var random = new System.Random(1);
            for (var i = 0; i < 5000; i++)
            {
                var next = rotation.Next(random);
                CollectionAssert.DoesNotContain(new[] { "Fungal", "Kitchen", "Computer" }, next);
                CollectionAssert.Contains(LevelRotation.Levels, next);
            }
        }

        [Test]
        public void PicksEvenlyFromTheFourLeft()
        {
            var rotation = new LevelRotation(new[] { "Meadow", "Fungal", "Chessboard" });
            var random = new System.Random(7);
            var counts = new Dictionary<string, int>();
            const int draws = 40000;
            for (var i = 0; i < draws; i++)
            {
                var next = rotation.Next(random);
                counts[next] = counts.TryGetValue(next, out var c) ? c + 1 : 1;
            }
            Assert.AreEqual(4, counts.Count, "the four levels not among the last three");
            foreach (var pair in counts)
                Assert.That(pair.Value, Is.InRange(draws / 4 - 800, draws / 4 + 800), pair.Key);
        }

        [Test]
        public void PicksEvenlyFromAllSevenWithNothingPlayed()
        {
            var rotation = new LevelRotation();
            var random = new System.Random(3);
            var counts = new Dictionary<string, int>();
            const int draws = 70000;
            for (var i = 0; i < draws; i++)
            {
                var next = rotation.Next(random);
                counts[next] = counts.TryGetValue(next, out var c) ? c + 1 : 1;
            }
            Assert.AreEqual(7, counts.Count);
            foreach (var pair in counts)
                Assert.That(pair.Value, Is.InRange(draws / 7 - 800, draws / 7 + 800), pair.Key);
        }

        [Test]
        public void NoLevelRepeatsWithinFourMatches()
        {
            var rotation = new LevelRotation();
            var random = new System.Random(42);
            var played = new List<string>();
            for (var match = 0; match < 2000; match++)
            {
                var level = match == 0 ? "Meadow" : rotation.Next(random);
                rotation.Played(level);
                played.Add(level);
            }
            for (var i = 3; i < played.Count; i++)
                Assert.AreEqual(4, played.Skip(i - 3).Take(4).Distinct().Count(), $"matches {i - 3} to {i} repeat a level");
        }

        [Test]
        public void PlayedKeepsOnlyTheLastThree()
        {
            var rotation = new LevelRotation();
            foreach (var level in new[] { "Meadow", "Fungal", "Chessboard", "Toybox", "Kitchen" })
                rotation.Played(level);
            CollectionAssert.AreEqual(new[] { "Chessboard", "Toybox", "Kitchen" }, rotation.History);
        }

        [Test]
        public void TheSavedHistoryIsTheLastThreeInOrder()
        {
            var rotation = new LevelRotation(new[] { "Meadow", "Fungal", "Chessboard", "Toybox" });
            CollectionAssert.AreEqual(new[] { "Fungal", "Chessboard", "Toybox" }, rotation.History);
        }

        [Test]
        public void UnknownNamesInASavedHistoryAreDropped()
        {
            var rotation = new LevelRotation(new[] { "Gone", "Fungal" });
            CollectionAssert.AreEqual(new[] { "Fungal" }, rotation.History);
        }

        [Test]
        public void TheSameSeedGivesTheSameRotation()
        {
            var a = new LevelRotation();
            var b = new LevelRotation();
            var ra = new System.Random(9);
            var rb = new System.Random(9);
            for (var i = 0; i < 50; i++)
            {
                var la = a.Next(ra);
                var lb = b.Next(rb);
                Assert.AreEqual(la, lb);
                a.Played(la);
                b.Played(lb);
            }
        }
    }
}
