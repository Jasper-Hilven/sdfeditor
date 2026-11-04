using NUnit.Framework;

namespace SdfPlatform.Tests
{
    public class DeviceRuleTests
    {
        static DeviceKind Resolve(string url, string sdk, bool mobilePlatform, bool editor, out DeviceSource source) =>
            DeviceRule.Resolve(url, sdk, mobilePlatform, editor, out source);

        [Test]
        public void NothingKnownIsADesktop()
        {
            Assert.AreEqual(DeviceKind.Desktop, Resolve("", null, false, false, out var source));
            Assert.AreEqual(DeviceSource.Default, source);
        }

        [TestCase("https://x/index.html?device=mobile", DeviceKind.Mobile)]
        [TestCase("https://x/?a=1&device=Tablet", DeviceKind.Tablet)]
        [TestCase("https://x/?DEVICE=desktop#top", DeviceKind.Desktop)]
        public void TheUrlOverrideBeatsEverything(string url, DeviceKind expected)
        {
            Assert.AreEqual(expected, Resolve(url, "mobile", true, true, out var source));
            Assert.AreEqual(DeviceSource.Url, source);
        }

        [TestCase("https://x/?device=phone")]
        [TestCase("https://x/?other=mobile")]
        [TestCase("https://x/#?device=mobile")]
        [TestCase("https://x/?device")]
        public void ABadOrMissingUrlValueIsIgnored(string url)
        {
            Assert.AreEqual(DeviceKind.Desktop, Resolve(url, "desktop", false, false, out var source));
            Assert.AreEqual(DeviceSource.Sdk, source);
        }

        [TestCase("mobile", DeviceKind.Mobile)]
        [TestCase("tablet", DeviceKind.Tablet)]
        [TestCase("desktop", DeviceKind.Desktop)]
        public void TheSdkTypeBeatsThePlatform(string type, DeviceKind expected)
        {
            Assert.AreEqual(expected, Resolve(null, type, expected == DeviceKind.Desktop, false, out var source));
            Assert.AreEqual(DeviceSource.Sdk, source);
        }

        [Test]
        public void AnUnknownSdkTypeFallsToThePlatform()
        {
            Assert.AreEqual(DeviceKind.Mobile, Resolve(null, "watch", true, false, out var source));
            Assert.AreEqual(DeviceSource.Platform, source);
            Assert.AreEqual(DeviceKind.Desktop, Resolve(null, null, false, false, out source));
            Assert.AreEqual(DeviceSource.Default, source);
        }

        [Test]
        public void TheEditorSwitchBeatsTheSdkButNotTheUrl()
        {
            Assert.AreEqual(DeviceKind.Mobile, Resolve(null, "desktop", false, true, out var source));
            Assert.AreEqual(DeviceSource.Editor, source);
            Assert.AreEqual(DeviceKind.Tablet, Resolve("?device=tablet", "desktop", false, true, out source));
            Assert.AreEqual(DeviceSource.Url, source);
        }

        [Test]
        public void TouchIsMobileOrTablet()
        {
            Assert.IsFalse(DeviceRule.IsTouch(DeviceKind.Desktop));
            Assert.IsTrue(DeviceRule.IsTouch(DeviceKind.Mobile));
            Assert.IsTrue(DeviceRule.IsTouch(DeviceKind.Tablet));
        }
    }
}
