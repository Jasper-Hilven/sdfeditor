using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>The cheat typed blind: the code completes on its last letter, in any case, after anything else typed.</summary>
    public sealed class CheatCodeTests
    {
        private static int Completions(CheatCode cheat, string text)
        {
            var count = 0;
            foreach (var c in text)
                if (cheat.Feed(c))
                    count++;
            return count;
        }

        [Test]
        public void TheCodeCompletesOnItsLastLetter()
        {
            var cheat = new CheatCode(CheatCode.Skip);
            Assert.AreEqual(0, Completions(cheat, "skipthisgamechea"));
            Assert.IsTrue(cheat.Feed('t'));
        }

        [Test]
        public void AnythingTypedBeforeOrInBetween_AndAnyCase()
        {
            Assert.AreEqual(1, Completions(new CheatCode(CheatCode.Skip), "wasdwasd skipskipthisSKIPTHISGAMECHEATqq"));
        }

        [Test]
        public void AWrongLetterStartsItOver_AndItWorksAgain()
        {
            var cheat = new CheatCode(CheatCode.Skip);
            Assert.AreEqual(0, Completions(cheat, "skipthisgamexcheat"));
            Assert.AreEqual(2, Completions(cheat, "skipthisgamecheatskipthisgamecheat"));
        }
    }
}
