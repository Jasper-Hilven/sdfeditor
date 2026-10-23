using NUnit.Framework;

namespace SdfPlatform.Tests
{
    public class PortalPolicyTests
    {
        [Test]
        public void GameplayChangesReportOnceEach()
        {
            var t = new GameplayTracker();
            Assert.AreEqual(GameplayChange.None, t.Set(false));
            Assert.AreEqual(GameplayChange.Start, t.Set(true));
            Assert.AreEqual(GameplayChange.None, t.Set(true));
            Assert.AreEqual(GameplayChange.Stop, t.Set(false));
            Assert.AreEqual(GameplayChange.None, t.Set(false));
            Assert.AreEqual(GameplayChange.Start, t.Set(true));
        }

        [Test]
        public void ForgottenStateIsReportedAgain()
        {
            var t = new GameplayTracker();
            t.Set(true);
            t.Forget();
            Assert.IsFalse(t.Playing);
            Assert.AreEqual(GameplayChange.Start, t.Set(true));
        }

        [Test]
        public void NoAdBeforeABattleWasPlayed()
        {
            var p = new AdBreakPolicy();
            Assert.IsFalse(p.ShouldAsk(1000));
            p.BattleEnded(10);
            Assert.IsFalse(p.ShouldAsk(1000), "a quick retry earns no ad");
        }

        [Test]
        public void AdAfterEnoughPlayAndThenNotAgainUntilMorePlay()
        {
            var p = new AdBreakPolicy();
            p.BattleEnded(60);
            Assert.IsTrue(p.ShouldAsk(200));
            p.Asked(200);
            Assert.IsFalse(p.ShouldAsk(210), "no play since");
            p.BattleEnded(120);
            Assert.IsFalse(p.ShouldAsk(300), "too soon after the last ask");
            Assert.IsTrue(p.ShouldAsk(400));
        }

        [Test]
        public void ShortBattlesAddUp()
        {
            var p = new AdBreakPolicy();
            p.BattleEnded(20);
            p.BattleEnded(20);
            Assert.IsFalse(p.ShouldAsk(500));
            p.BattleEnded(20);
            Assert.IsTrue(p.ShouldAsk(500));
        }

        [Test]
        public void NegativeAndZeroSecondsAreIgnored()
        {
            var p = new AdBreakPolicy();
            p.BattleEnded(-50);
            p.BattleEnded(0);
            Assert.AreEqual(0, p.Played);
        }
    }
}
