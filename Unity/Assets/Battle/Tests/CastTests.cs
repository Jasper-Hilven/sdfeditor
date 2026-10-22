using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>The players' names (ticket 32): the cast, in order, and a plain name past it.</summary>
    public sealed class CastTests
    {
        [Test]
        public void RedStartsWithYou_AndBlueWithTheRunnerUp()
        {
            Assert.AreEqual("Mushroom 42", Cast.Name(Team.Red, 1));
            Assert.AreEqual("Mushroom 99", Cast.Name(Team.Red, 4));
            Assert.AreEqual("Runner-Up", Cast.Name(Team.Blue, 1));
            Assert.AreEqual("Participation Award", Cast.Name(Team.Blue, 5));
        }

        [Test]
        public void PastTheList_ThePlayerHasAPlainName()
        {
            Assert.AreEqual("Red 5", Cast.Name(Team.Red, 5));
            Assert.AreEqual("Blue 6", Cast.Name(Team.Blue, 6));
            Assert.AreEqual("Blue 0", Cast.Name(Team.Blue, 0));
        }
    }
}
