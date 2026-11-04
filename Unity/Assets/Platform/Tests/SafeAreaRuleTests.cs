using NUnit.Framework;

namespace SdfPlatform.Tests
{
    public class SafeAreaRuleTests
    {
        [Test]
        public void TheWholeScreenHasNoInsets()
        {
            Assert.IsTrue(SafeAreaRule.FromRect(0, 0, 800, 450, 800, 450).IsNone);
        }

        [Test]
        public void ANotchedLandscapeScreenGivesItsInsets()
        {
            // 844 x 390 with 47 px cut at each side and 21 px at the bottom
            var i = SafeAreaRule.FromRect(47, 21, 750, 369, 844, 390);
            Assert.AreEqual(47f / 844, i.Left, 1e-5f);
            Assert.AreEqual(47f / 844, i.Right, 1e-5f);
            Assert.AreEqual(21f / 390, i.Bottom, 1e-5f);
            Assert.AreEqual(0, i.Top, 1e-5f);
        }

        [Test]
        public void CssDistancesBecomeFractionsOfTheViewport()
        {
            var i = SafeAreaRule.FromCss(44, 0, 44, 21, 800, 400);
            Assert.AreEqual(0.055f, i.Left, 1e-5f);
            Assert.AreEqual(0.055f, i.Right, 1e-5f);
            Assert.AreEqual(21f / 400, i.Bottom, 1e-5f);
        }

        [Test]
        public void TheLargerOfTwoAnswersWins()
        {
            var a = new SafeInsets(0.05f, 0, 0, 0.1f);
            var b = new SafeInsets(0.02f, 0.03f, 0, 0.05f);
            var u = SafeAreaRule.Union(a, b);
            Assert.AreEqual(0.05f, u.Left);
            Assert.AreEqual(0.03f, u.Bottom);
            Assert.AreEqual(0.1f, u.Top);
        }

        [Test]
        public void AbsurdAnswersCannotPushTheGuiOffTheScreen()
        {
            var i = SafeAreaRule.Clamp(new SafeInsets(0.9f, -1, 0.9f, float.NaN));
            Assert.LessOrEqual(i.Left + i.Right, SafeAreaRule.MaxAxis + 1e-5f);
            Assert.AreEqual(0, i.Bottom);
            Assert.AreEqual(0, i.Top);
        }

        [Test]
        public void ADegenerateScreenHasNoInsets()
        {
            Assert.IsTrue(SafeAreaRule.FromRect(0, 0, 0, 0, 0, 0).IsNone);
            Assert.IsTrue(SafeAreaRule.FromCss(10, 10, 10, 10, 0, 0).IsNone);
        }
    }
}
