using System;
using NUnit.Framework;
using SdfHeads;

namespace SdfCampaign.Tests
{
    public sealed class NextLevelCardTests
    {
        // ---------------------------------------------------------------- The countdown

        [Test]
        public void TheCountdownIsTenSecondsAndShowsTenDownToOne()
        {
            var c = new NextLevelCountdown();
            Assert.AreEqual(10f, NextLevelCountdown.Seconds);
            Assert.AreEqual(10, c.Shown);
            Assert.IsFalse(c.Tick(4.2f, false));
            Assert.AreEqual(6, c.Shown, "5.8 s left shows 6");
            Assert.IsFalse(c.Tick(5.7f, false));
            Assert.AreEqual(1, c.Shown, "0.1 s left shows 1");
            Assert.IsFalse(c.Done);
            Assert.IsTrue(c.Tick(0.2f, false), "the tick that runs out finishes it");
            Assert.IsTrue(c.Done);
            Assert.AreEqual(0, c.Shown);
        }

        [Test]
        public void ItFinishesOnceAndNotAgain()
        {
            var c = new NextLevelCountdown();
            Assert.IsTrue(c.Tick(20, false));
            Assert.IsFalse(c.Tick(1, false));
            Assert.IsFalse(c.Skip(false), "a skip after the end does nothing");
        }

        [Test]
        public void APauseStopsTheClock()
        {
            var c = new NextLevelCountdown();
            c.Tick(3, false);
            Assert.IsFalse(c.Tick(100, true), "paused: no time passes");
            Assert.AreEqual(7f, c.Remaining, 0.0001f);
            Assert.IsFalse(c.Skip(true), "paused: the skip key does nothing");
            Assert.IsFalse(c.Done);
            Assert.IsTrue(c.Tick(7, false), "resumed: the rest runs out");
        }

        [Test]
        public void SkipEndsTheCountdownNow()
        {
            var c = new NextLevelCountdown();
            c.Tick(1, false);
            Assert.IsTrue(c.Skip(false));
            Assert.IsTrue(c.Done);
            Assert.AreEqual(0, c.Shown);
        }

        [Test]
        public void ANonPositiveStepChangesNothing()
        {
            var c = new NextLevelCountdown();
            Assert.IsFalse(c.Tick(0, false));
            Assert.IsFalse(c.Tick(-5, false));
            Assert.AreEqual(10f, c.Remaining);
        }

        // ---------------------------------------------------------------- Once per match end

        [Test]
        public void TheAdIsTakenOncePerMatchEnd()
        {
            var once = new OncePerMatch();
            Assert.IsFalse(once.Take(), "nothing armed, nothing to take");
            once.Arm();
            Assert.IsTrue(once.Take());
            Assert.IsFalse(once.Take(), "a second countdown end in the same match asks nothing");
            once.Arm();
            Assert.IsTrue(once.Take(), "the next match end arms it again");
        }

        // ---------------------------------------------------------------- Difficulty line

        [Test]
        public void TheDifficultyLineSaysWhichWayItMoved()
        {
            Assert.AreEqual("Tougher opponents next", NextLevelCard.DifficultyLine(Difficulty.Normal, Difficulty.Hard));
            Assert.AreEqual("Easier opponents next", NextLevelCard.DifficultyLine(Difficulty.Normal, Difficulty.Easy));
            Assert.AreEqual("Easier opponents next", NextLevelCard.DifficultyLine(Difficulty.Hard, Difficulty.Normal));
            Assert.AreEqual("Tougher opponents next", NextLevelCard.DifficultyLine(Difficulty.SuperEasy, Difficulty.Easy));
        }

        [Test]
        public void NoLineWhenTheDifficultyStays()
        {
            foreach (var d in new[] { Difficulty.SuperEasy, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
                Assert.IsNull(NextLevelCard.DifficultyLine(d, d), d.ToString());
        }

        [Test]
        public void TheCountdownAndTheLevelNameRead()
        {
            Assert.AreEqual("Next level in 10…", NextLevelCard.CountdownLine(10));
            Assert.AreEqual("Next level in 1…", NextLevelCard.CountdownLine(1));
            Assert.AreEqual("KITCHEN", NextLevelCard.LevelName("Kitchen"));
            Assert.AreEqual("", NextLevelCard.LevelName(null));
        }

        // ---------------------------------------------------------------- Head cycling over the unlocked list

        // The card's A and D go through HeadUnlocks.Previous / Next: a cycle over the heads you can wear, in calendar order, wrapping
        [Test]
        public void TheHeadCycleWrapsOverTheUnlockedHeads()
        {
            var p = new HeadProgress { unlocked = new[] { "frankenstein" } };
            var now = new DateTime(2026, 10, 15);
            // October's head (pumpkin) and the one unlocked by a win: two heads, so each step goes to the other one
            Assert.AreEqual("frankenstein", HeadUnlocks.Next(now, p, "pumpkin"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Next(now, p, "frankenstein"));
            Assert.AreEqual("frankenstein", HeadUnlocks.Previous(now, p, "pumpkin"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Previous(now, p, "frankenstein"));
        }

        [Test]
        public void ASingleHeadCyclesToItself()
        {
            var p = new HeadProgress();
            var now = new DateTime(2026, 10, 15);
            Assert.AreEqual("pumpkin", HeadUnlocks.Next(now, p, "pumpkin"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Previous(now, p, "pumpkin"));
        }

        // A win that unlocks a head chooses it for you; switching back on the card returns to the one you had
        [Test]
        public void AnUnlockedHeadIsWornAndSwitchingBackWorks()
        {
            var p = new HeadProgress();
            var now = new DateTime(2026, 10, 15);
            var unlocked = HeadUnlocks.OnMatchWon(now, p);
            Assert.AreEqual("frankenstein", unlocked);
            Assert.IsTrue(HeadUnlocks.SetCurrent(now, p, unlocked));
            Assert.AreEqual("frankenstein", HeadUnlocks.Current(now, p));
            Assert.AreEqual("pumpkin", HeadUnlocks.Previous(now, p, "frankenstein"));
        }
    }
}
