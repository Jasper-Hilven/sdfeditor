using System;
using System.Linq;
using NUnit.Framework;

namespace SdfHeads.Tests
{
    public sealed class HeadUnlocksTests
    {
        static DateTime Day(int month, int day = 15, int year = 2026) => new DateTime(year, month, day);

        [Test]
        public void TheHeadOfTheMonthComesFromTheDate()
        {
            for (var m = 1; m <= 12; m++)
                Assert.AreEqual(MonsterHeads.All[m - 1].Id, HeadUnlocks.HeadOfMonth(Day(m)));
            Assert.AreEqual("yeti", HeadUnlocks.HeadOfMonth(Day(1, 1)));
            Assert.AreEqual("yeti", HeadUnlocks.HeadOfMonth(Day(1, 31)));
            Assert.AreEqual("santa", HeadUnlocks.HeadOfMonth(Day(12, 31)));
            Assert.AreEqual("pumpkin", HeadUnlocks.HeadOfMonth(Day(10, 31)));
            Assert.AreEqual("frankenstein", HeadUnlocks.HeadOfMonth(Day(11, 1)));
        }

        [Test]
        public void ANewPlayerHasOnlyTheHeadOfTheMonthAndWearsIt()
        {
            var p = new HeadProgress();
            CollectionAssert.AreEqual(new[] { "pumpkin" }, HeadUnlocks.Available(Day(10), p));
            Assert.AreEqual("pumpkin", HeadUnlocks.Current(Day(10), p));
        }

        [Test]
        public void AWinUnlocksTheNextHeadAfterTheCurrentMonth()
        {
            var p = new HeadProgress();
            Assert.AreEqual("frankenstein", HeadUnlocks.OnMatchWon(Day(10), p));
            Assert.AreEqual("santa", HeadUnlocks.OnMatchWon(Day(10), p));
            CollectionAssert.AreEqual(new[] { "pumpkin", "frankenstein", "santa" }, HeadUnlocks.Available(Day(10), p));
        }

        [Test]
        public void TheUnlockOrderWrapsAtDecemberInNovember()
        {
            var p = new HeadProgress();
            var order = new[] { "santa", "yeti", "vampire", "leprechaun" };
            foreach (var expected in order)
                Assert.AreEqual(expected, HeadUnlocks.OnMatchWon(Day(11), p));
        }

        [Test]
        public void TheWrapStartsAfterDecemberToo()
        {
            var p = new HeadProgress();
            Assert.AreEqual("yeti", HeadUnlocks.OnMatchWon(Day(12), p));
            Assert.AreEqual("vampire", HeadUnlocks.OnMatchWon(Day(12), p));
        }

        [Test]
        public void AWinSkipsHeadsAlreadyYours()
        {
            var p = new HeadProgress { monthsSeen = new[] { 11 } };
            // In October: November's head is yours from an earlier month, so the win gives December's
            Assert.AreEqual("santa", HeadUnlocks.OnMatchWon(Day(10), p));
        }

        [Test]
        public void WithAllTwelveAWinUnlocksNothing()
        {
            var p = new HeadProgress();
            for (var i = 0; i < 11; i++)
                Assert.IsNotNull(HeadUnlocks.OnMatchWon(Day(3), p), "win " + i);
            Assert.AreEqual(12, HeadUnlocks.Available(Day(3), p).Count);
            Assert.IsNull(HeadUnlocks.OnMatchWon(Day(3), p));
            Assert.AreEqual(12, HeadUnlocks.Available(Day(3), p).Count);
        }

        [Test]
        public void EveryHeadIsUnlockedOnceInCalendarOrderFromTheMonth()
        {
            var p = new HeadProgress();
            var got = Enumerable.Range(0, 11).Select(_ => HeadUnlocks.OnMatchWon(Day(5), p)).ToList();
            CollectionAssert.AreEqual(new[] { 6, 7, 8, 9, 10, 11, 12, 1, 2, 3, 4 }, got.Select(id => MonsterHeads.ById(id).Value.Month));
        }

        [Test]
        public void APlayedMonthKeepsItsHeadAfterTheMonthEnds()
        {
            var p = new HeadProgress();
            Assert.IsTrue(HeadUnlocks.RecordPlayed(Day(10, 31), p));
            Assert.IsFalse(HeadUnlocks.RecordPlayed(Day(10, 2), p));
            CollectionAssert.AreEqual(new[] { "pumpkin", "frankenstein" }, HeadUnlocks.Available(Day(11, 1), p));
        }

        [Test]
        public void AMonthNotPlayedInIsLostWhenItEnds()
        {
            var p = new HeadProgress();
            CollectionAssert.DoesNotContain(HeadUnlocks.Available(Day(11), p), "pumpkin");
        }

        [Test]
        public void AClockMovedBackwardsTakesNothingAway()
        {
            var p = new HeadProgress();
            HeadUnlocks.RecordPlayed(Day(10), p);
            HeadUnlocks.OnMatchWon(Day(10), p);   // frankenstein
            var before = HeadUnlocks.Available(Day(10), p);
            var back = HeadUnlocks.Available(Day(3), p);
            foreach (var id in before)
                CollectionAssert.Contains(back, id);
            CollectionAssert.Contains(back, "leprechaun");   // and the March head, now
            // a win in the past gives the next after March, not what is already yours
            Assert.AreEqual("swamp", HeadUnlocks.OnMatchWon(Day(3), p));
        }

        [Test]
        public void ThePickedHeadIsKeptAndAnUnlockedOneOnlyCanBePicked()
        {
            var p = new HeadProgress();
            Assert.IsFalse(HeadUnlocks.SetCurrent(Day(10), p, "santa"));
            Assert.IsFalse(HeadUnlocks.SetCurrent(Day(10), p, "nonsense"));
            Assert.IsFalse(HeadUnlocks.SetCurrent(Day(10), p, null));
            HeadUnlocks.OnMatchWon(Day(10), p);
            Assert.IsTrue(HeadUnlocks.SetCurrent(Day(10), p, "frankenstein"));
            Assert.AreEqual("frankenstein", HeadUnlocks.Current(Day(10), p));
            // the next month it is still the pick
            Assert.AreEqual("frankenstein", HeadUnlocks.Current(Day(11, 5), p));
        }

        [Test]
        public void AnInvalidPickFallsBackToTheHeadOfTheMonth()
        {
            var p = new HeadProgress { current = "santa" };
            Assert.AreEqual("pumpkin", HeadUnlocks.Current(Day(10), p));
            p = new HeadProgress { current = "bogus" };
            Assert.AreEqual("yeti", HeadUnlocks.Current(Day(1), p));
        }

        [Test]
        public void PreviousAndNextCycleOverTheUnlockedAndWrap()
        {
            var p = new HeadProgress();
            HeadUnlocks.OnMatchWon(Day(10), p);   // frankenstein
            HeadUnlocks.OnMatchWon(Day(10), p);   // santa
            Assert.AreEqual("frankenstein", HeadUnlocks.Next(Day(10), p, "pumpkin"));
            Assert.AreEqual("santa", HeadUnlocks.Next(Day(10), p, "frankenstein"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Next(Day(10), p, "santa"));
            Assert.AreEqual("santa", HeadUnlocks.Previous(Day(10), p, "pumpkin"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Previous(Day(10), p, "frankenstein"));
        }

        [Test]
        public void WithOneHeadTheCycleStaysPut()
        {
            var p = new HeadProgress();
            Assert.AreEqual("pumpkin", HeadUnlocks.Next(Day(10), p, "pumpkin"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Previous(Day(10), p, "pumpkin"));
            Assert.AreEqual("pumpkin", HeadUnlocks.Next(Day(10), p, "santa"));
        }

        [Test]
        public void ACorruptedProgressWithNullsStillWorks()
        {
            var p = new HeadProgress { unlocked = null, monthsSeen = null, current = null };
            CollectionAssert.AreEqual(new[] { "pumpkin" }, HeadUnlocks.Available(Day(10), p));
            Assert.AreEqual("frankenstein", HeadUnlocks.OnMatchWon(Day(10), p));
        }

        [Test]
        public void BotsDrawDistinctHeadsFromAllTwelve()
        {
            var cast = new HeadCast(new Random(7));
            cast.Reserve("yeti");
            var drawn = Enumerable.Range(0, 11).Select(_ => cast.Draw()).ToList();
            Assert.AreEqual(11, drawn.Distinct().Count());
            CollectionAssert.DoesNotContain(drawn, "yeti");
            Assert.AreEqual(12, cast.Used.Distinct().Count());
        }

        [Test]
        public void BotsCanWearLockedHeadsAndDrawAgainOnceAllTwelveAreUsed()
        {
            var cast = new HeadCast(new Random(1));
            var all = Enumerable.Range(0, 12).Select(_ => cast.Draw()).ToList();
            Assert.AreEqual(12, all.Distinct().Count());
            Assert.IsNotNull(MonsterHeads.ById(cast.Draw()));   // the thirteenth repeats
        }

        [Test]
        public void TheSameSeedDrawsTheSameHeads()
        {
            var a = new HeadCast(new Random(42));
            var b = new HeadCast(new Random(42));
            for (var i = 0; i < 8; i++)
                Assert.AreEqual(a.Draw(), b.Draw());
        }

        [Test]
        public void TheDrawReachesEveryHeadAcrossMatches()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            var rng = new Random(3);
            for (var match = 0; match < 60; match++)
            {
                var cast = new HeadCast(rng);
                for (var i = 0; i < 7; i++)
                    seen.Add(cast.Draw());
            }
            Assert.AreEqual(12, seen.Count);
        }

        [Test]
        public void AnUnbuiltHeadShowsAsTheNearestBuiltOne()
        {
            Assert.AreEqual("yeti", HeadStandIn.Of("yeti"));
            Assert.AreEqual("pumpkin", HeadStandIn.Of("pumpkin"));
            Assert.AreEqual("yeti", HeadStandIn.Of("vampire"));
            Assert.AreEqual("yeti", HeadStandIn.Of("santa"));
            Assert.AreEqual("pumpkin", HeadStandIn.Of("werewolf"));
            Assert.AreEqual("pumpkin", HeadStandIn.Of("frankenstein"));
            Assert.IsNull(HeadStandIn.Of("nonsense"));
        }

        [Test]
        public void EveryTableHeadHasAStandInAndNoneBuiltMeansNull()
        {
            foreach (var head in MonsterHeads.All)
                Assert.IsNotNull(HeadStandIn.Of(head.Id), head.Id);
            Assert.IsNull(HeadStandIn.Of("yeti", h => false));
            Assert.AreEqual("alien", HeadStandIn.Of("mummy", h => h.Id == "alien" || h.Id == "santa"));
        }

        [Test]
        public void ThePlaceholderIsSaidInTheCycleLabel()
        {
            Assert.AreEqual("Abominable Snowman", HeadStandIn.Label("yeti"));
            StringAssert.EndsWith("(placeholder)", HeadStandIn.Label("vampire"));
            Assert.AreEqual("", HeadStandIn.Label("zzz"));
        }
    }
}
