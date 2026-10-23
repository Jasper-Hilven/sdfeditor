using NUnit.Framework;

namespace SdfCampaign.Tests
{
    public sealed class LayeredStorageTests
    {
        [Test]
        public void ReadsThePrimaryFirstThenTheFallback()
        {
            var primary = new MemoryCampaignStorage();
            var fallback = new MemoryCampaignStorage();
            fallback.Set("a", "old");
            var layered = new LayeredCampaignStorage(primary, fallback);

            Assert.IsTrue(layered.TryGet("a", out var v));
            Assert.AreEqual("old", v);

            primary.Set("a", "new");
            Assert.IsTrue(layered.TryGet("a", out v));
            Assert.AreEqual("new", v);

            Assert.IsFalse(layered.TryGet("missing", out _));
        }

        [Test]
        public void WritesGoToThePrimaryOnlyAndLeaveTheFallbackAlone()
        {
            var primary = new MemoryCampaignStorage();
            var fallback = new MemoryCampaignStorage();
            fallback.Set("a", "old");
            var layered = new LayeredCampaignStorage(primary, fallback);

            layered.Set("a", "new");

            Assert.AreEqual("new", primary.values["a"]);
            Assert.AreEqual("old", fallback.values["a"], "nothing destructive");
        }
    }
}
