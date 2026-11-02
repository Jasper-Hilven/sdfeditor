using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>The players' names (ticket 32): the cast, in order, and a plain name past it.</summary>
    public sealed class CastTests
    {
        [Test]
        public void RedStartsWithYou_AndBlueWithBigGerald()
        {
            Assert.AreEqual("Nightowl42", Cast.Name(Team.Red, 1));
            Assert.AreEqual("Pixelpuss", Cast.Name(Team.Red, 4));
            Assert.AreEqual("Big Gerald", Cast.Name(Team.Blue, 1));
            Assert.AreEqual("Grandma Gunner", Cast.Name(Team.Blue, 5));
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
