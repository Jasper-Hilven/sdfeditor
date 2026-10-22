using NUnit.Framework;
using UnityEngine;

namespace SdfStory.Tests
{
    /// <summary>The built viewer prefab (Tools > SDF Story > Build Intro Viewer) is wired up and holds the panels.</summary>
    public sealed class StoryIntroPrefabTests
    {
        [Test]
        public void ThePrefabHoldsTwelvePanelsAndEveryReference()
        {
            var prefab = Resources.Load<GameObject>(StoryIntro.ResourceName);
            Assert.IsNotNull(prefab, "Resources/StoryIntro.prefab");
            var intro = prefab.GetComponent<StoryIntro>();
            Assert.AreEqual(12, intro.panels.Length);
            foreach (var panel in intro.panels)
                Assert.IsNotNull(panel);
            Assert.IsNotNull(intro.source);
            Assert.IsNotNull(intro.canvasRoot);
            Assert.IsNotNull(intro.frame);
            Assert.IsNotNull(intro.current);
            Assert.IsNotNull(intro.incoming);
            Assert.IsNotNull(intro.next);
            Assert.IsNotNull(intro.skip);
            Assert.IsNotNull(intro.panelClick);
            Assert.IsNotNull(intro.nextLabel);
        }

        [Test]
        public void TheButtonsRunNoGuiCommand()
        {
            var prefab = Resources.Load<GameObject>(StoryIntro.ResourceName);
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.AreNotEqual("GuiCommand", component.GetType().Name);
        }
    }
}
