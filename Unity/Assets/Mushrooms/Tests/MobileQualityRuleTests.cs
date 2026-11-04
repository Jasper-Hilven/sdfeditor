using NUnit.Framework;

namespace SdfMushrooms.Tests
{
    /// <summary>What a touch device gets in quality (ticket 58): it starts one above the lowest level, the cuts only on the lowest and only on touch, a capped render target.</summary>
    public sealed class MobileQualityRuleTests
    {
        private static readonly string[] Names = { "Ultra Epic", "Epic", "Ultra High", "High", "Integrated GPU / Web", "Ultra Low" };

        [Test]
        public void StartsOneAboveTheLowestLevel() => Assert.AreEqual(Names.Length - 2, MobileQualityRule.StartIndex(Names));

        [Test]
        public void CutsOnlyOnTouchOnTheLowestLevel()
        {
            Assert.IsTrue(MobileQualityRule.Cuts(true, "Ultra Low"));
            Assert.IsFalse(MobileQualityRule.Cuts(false, "Ultra Low"), "desktop is unchanged");
            Assert.IsFalse(MobileQualityRule.Cuts(true, "Integrated GPU / Web"));
            Assert.IsFalse(MobileQualityRule.Cuts(true, "Ultra High"));
            Assert.IsFalse(MobileQualityRule.Cuts(true, null));
        }

        [Test]
        public void RenderScaleIsCappedByScreenHeight()
        {
            Assert.AreEqual(0.5f, MobileQualityRule.RenderScale(0.5f, 1080), 1e-4);
            Assert.AreEqual(0.75f, MobileQualityRule.RenderScale(0.75f, 450), 1e-4, "a small window keeps the preset's");
            Assert.AreEqual(0.5f, MobileQualityRule.RenderScale(1f, 1080), 1e-4, "a 1080p phone at preset 1 draws 540 lines");
            Assert.AreEqual(0.35f, MobileQualityRule.RenderScale(1f, 4000), 1e-4);
            Assert.AreEqual(1f, MobileQualityRule.RenderScale(1f, 0));
        }
    }
}
