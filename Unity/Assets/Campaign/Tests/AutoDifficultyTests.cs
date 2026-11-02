using NUnit.Framework;

namespace SdfCampaign.Tests
{
    public sealed class AutoDifficultyTests
    {
        static Difficulty Next(Difficulty from, int yours, int enemy) => AutoDifficulty.Next(from, new CaptureResult(yours, enemy));

        [Test]
        public void ALossIsOneStepEasier()
        {
            Assert.AreEqual(Difficulty.Easy, Next(Difficulty.Normal, 1, 3));
            Assert.AreEqual(Difficulty.SuperEasy, Next(Difficulty.Easy, 0, 3));
            Assert.AreEqual(Difficulty.Normal, Next(Difficulty.Hard, 2, 3));
        }

        [Test]
        public void AWinWithTheEnemyOnNothingIsOneStepHarder()
        {
            Assert.AreEqual(Difficulty.Hard, Next(Difficulty.Normal, 3, 0));
            Assert.AreEqual(Difficulty.Normal, Next(Difficulty.Easy, 3, 0));
            Assert.AreEqual(Difficulty.Easy, Next(Difficulty.SuperEasy, 3, 0));
        }

        [Test]
        public void AWinWithTheEnemyScoringStaysTheSame()
        {
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                Assert.AreEqual(d, Next(d, 3, 1), $"3-1 from {d}");
                Assert.AreEqual(d, Next(d, 3, 2), $"3-2 from {d}");
            }
        }

        [Test]
        public void EveryBranchFromEveryLevelStaysInTheLadder()
        {
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                var easier = Next(d, 0, 3);
                var harder = Next(d, 3, 0);
                Assert.That((int)easier, Is.InRange((int)Difficulty.SuperEasy, (int)Difficulty.Hard));
                Assert.That((int)harder, Is.InRange((int)Difficulty.SuperEasy, (int)Difficulty.Hard));
                Assert.That((int)easier, Is.LessThanOrEqualTo((int)d));
                Assert.That((int)harder, Is.GreaterThanOrEqualTo((int)d));
            }
        }

        [Test]
        public void TheEasiestDoesNotGoLowerAndTheHardestDoesNotGoHigher()
        {
            Assert.AreEqual(Difficulty.SuperEasy, Next(Difficulty.SuperEasy, 0, 3));
            Assert.AreEqual(Difficulty.Hard, Next(Difficulty.Hard, 3, 0));
        }

        [Test]
        public void ADrawKeepsTheDifficultyAsItIs()
        {
            // Sudden death means a draw cannot end a match; if it ever did, nothing moves
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
                Assert.AreEqual(d, Next(d, 2, 2));
        }

        [Test]
        public void ASequenceMovesOneStepAtATime()
        {
            var d = Difficulty.Normal;
            d = Next(d, 0, 3);
            Assert.AreEqual(Difficulty.Easy, d);
            d = Next(d, 3, 0);
            Assert.AreEqual(Difficulty.Normal, d);
            d = Next(d, 3, 0);
            Assert.AreEqual(Difficulty.Hard, d);
            d = Next(d, 3, 0);
            Assert.AreEqual(Difficulty.Hard, d, "clamped at the top");
        }
    }
}
