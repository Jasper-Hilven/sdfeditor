using NUnit.Framework;
using UnityEngine;

namespace SdfStory.Tests
{
    /// <summary>The built viewer prefabs (Tools > SDF Story > Build Intro Viewer) are wired up, the intro's holds its panels, and the rendered beat comics are where the viewer looks (ticket 34).</summary>
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
            foreach (var name in new[] { StoryIntro.ResourceName, StoryIntro.ViewerResourceName })
            {
                var prefab = Resources.Load<GameObject>(name);
                Assert.IsNotNull(prefab, $"Resources/{name}.prefab");
                foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                    Assert.AreNotEqual("GuiCommand", component.GetType().Name);
            }
        }

        [Test]
        public void TheOtherComicsViewerIsTheSameScreenWithoutTheIntrosPanels()
        {
            // Ticket 34: a beat's comic is shown in it, so that the intro's twelve panels are not loaded with it
            var prefab = Resources.Load<GameObject>(StoryIntro.ViewerResourceName);
            Assert.IsNotNull(prefab, "Resources/StoryViewer.prefab (Tools > SDF Story > Build Intro Viewer)");
            var viewer = prefab.GetComponent<StoryIntro>();
            Assert.AreEqual(0, viewer.panels.Length);
            Assert.IsNotNull(viewer.current);
            Assert.IsNotNull(viewer.incoming);
            Assert.IsNotNull(viewer.next);
            Assert.IsNotNull(viewer.skip);
            Assert.IsNotNull(viewer.panelClick);
            Assert.IsNotNull(viewer.nextLabel);
        }

        [Test]
        public void TheFungalBeatsComicIsRenderedAsFourPanelsInItsFolder()
        {
            // B1 "Nightfall" (content/story/beat-fungal.txt), rendered by the capture into Resources/Comics/beat-fungal/panel-NN
            Assert.IsTrue(StoryIntro.HasComic("beat-fungal"), "render it with Tools > SDF Story > Render Comic...");
            Assert.AreEqual("Comics/beat-fungal/panel-01", StoryIntro.PanelResource("beat-fungal", 1));
            for (var number = 1; number <= 4; number++)
            {
                var panel = Resources.Load<Texture2D>(StoryIntro.PanelResource("beat-fungal", number));
                Assert.IsNotNull(panel, $"panel {number}");
                Assert.AreEqual(1920, panel.width);
                Assert.AreEqual(1080, panel.height);
            }
            Assert.IsNull(Resources.Load<Texture2D>(StoryIntro.PanelResource("beat-fungal", 5)), "four panels, no more");
            Assert.IsFalse(StoryIntro.HasComic("beat-no-such-level"));
            Assert.IsFalse(StoryIntro.HasComic(""));
        }
    }
}
