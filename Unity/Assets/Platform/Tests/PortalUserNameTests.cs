using NUnit.Framework;

namespace SdfPlatform.Tests
{
    /// <summary>The CrazyGames username as the game's name (ticket 65): logged out is nothing, odd characters go, long names are cut.</summary>
    public sealed class PortalUserNameTests
    {
        const int Max = 16;

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("...")]
        [TestCase("你好")]
        public void LoggedOutOrUnusable_IsEmpty(string raw) => Assert.AreEqual("", PortalUserName.Clean(raw, Max));

        [Test]
        public void LoggedIn_KeepsTheName() => Assert.AreEqual("Jasper_42", PortalUserName.Clean("Jasper_42", Max));

        [Test]
        public void LatinLetters_Stay() => Assert.AreEqual("Zoë Müller", PortalUserName.Clean("Zoë Müller", Max));

        [Test]
        public void TooLong_IsCut_NotOverflowing()
        {
            var name = PortalUserName.Clean("TheMostLegendaryPlayerEver_2026", Max);
            Assert.LessOrEqual(name.Length, Max);
            Assert.AreEqual("TheMostLegendary", name);
            Assert.AreEqual("Ab", PortalUserName.Clean("Ab" + new string(' ', 30) + "Cd", 3), "a cut ends on a letter, not a space");
        }

        [Test]
        public void OddCharacters_AreDropped_SoNoBoxGlyphs()
        {
            Assert.AreEqual("Cool", PortalUserName.Clean("★Cool★", Max), "stars");
            Assert.AreEqual("Fire", PortalUserName.Clean("Fire🔥", Max), "emoji (a surrogate pair)");
            Assert.AreEqual("Ana Bo", PortalUserName.Clean("Ana  \t Bo", Max), "spaces collapse");
            Assert.AreEqual("bTextb", PortalUserName.Clean("<b>Text</b>", Max), "no rich text");
            Assert.That(PortalUserName.Clean("<color=red>X", Max), Does.Not.Contain("<"));
        }
    }
}
