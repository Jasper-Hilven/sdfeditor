using NUnit.Framework;

namespace SdfStory.Tests
{
    /// <summary>Ticket 31.8: the intro viewer's pure logic: panel index, Next and Skip, the last panel's label, the 16:9 letterbox.</summary>
    public sealed class StoryIntroLogicTests
    {
        [Test]
        public void StartsOnTheFirstPanelReadingNext()
        {
            var logic = new StoryIntroLogic(12);
            Assert.AreEqual(0, logic.Index);
            Assert.AreEqual("NEXT", logic.NextLabel);
            Assert.IsFalse(logic.Finished);
        }

        [Test]
        public void NextWalksThroughTheTwelvePanelsAndEndsOnPlay()
        {
            var logic = new StoryIntroLogic(12);
            for (var i = 1; i < 12; i++)
            {
                Assert.IsTrue(logic.Next());
                Assert.AreEqual(i, logic.Index);
                Assert.IsFalse(logic.Finished);
                Assert.AreEqual(i == 11 ? "PLAY" : "NEXT", logic.NextLabel);
            }
            Assert.IsFalse(logic.Next());
            Assert.IsTrue(logic.Finished);
            Assert.IsFalse(logic.Skipped);
            Assert.AreEqual(11, logic.Index);
        }

        [Test]
        public void SkipEndsFromAnyPanelAndNothingMovesAfterwards()
        {
            var logic = new StoryIntroLogic(12);
            logic.Next();
            logic.Next();
            logic.Skip();
            Assert.IsTrue(logic.Finished);
            Assert.IsTrue(logic.Skipped);
            Assert.AreEqual(2, logic.Index);
            Assert.IsFalse(logic.Next());
            Assert.AreEqual(2, logic.Index);
        }

        [Test]
        public void SkipAfterTheEndDoesNotCountAsSkipped()
        {
            var logic = new StoryIntroLogic(1);
            logic.Next();
            logic.Skip();
            Assert.IsTrue(logic.Finished);
            Assert.IsFalse(logic.Skipped);
        }

        [Test]
        public void OnePanelIsAlreadyTheLastAndAnEmptyIntroEndsAtOnce()
        {
            var one = new StoryIntroLogic(1);
            Assert.IsTrue(one.OnLast);
            Assert.AreEqual("PLAY", one.NextLabel);
            var none = new StoryIntroLogic(0);
            Assert.IsFalse(none.Next());
            Assert.IsTrue(none.Finished);
        }

        private static void AssertBox(NormalizedRect r, float x, float y, float w, float h)
        {
            Assert.AreEqual(x, r.X, 1e-4f, "x");
            Assert.AreEqual(y, r.Y, 1e-4f, "y");
            Assert.AreEqual(w, r.Width, 1e-4f, "width");
            Assert.AreEqual(h, r.Height, 1e-4f, "height");
        }

        [Test]
        public void SixteenByNineFillsTheScreen()
        {
            AssertBox(StoryIntroLogic.Letterbox(1920, 1080), 0, 0, 1, 1);
            AssertBox(StoryIntroLogic.Letterbox(1280, 720), 0, 0, 1, 1);
        }

        [Test]
        public void TallerScreensGetBarsAboveAndBelow()
        {
            // 4:3: the 16:9 panel is 75% as tall as the screen
            AssertBox(StoryIntroLogic.Letterbox(1024, 768), 0, 0.125f, 1, 0.75f);
            // 16:10
            var r = StoryIntroLogic.Letterbox(1920, 1200);
            AssertBox(r, 0, (1 - r.Height) * 0.5f, 1, (16f / 10f) / (16f / 9f));
            // A portrait phone
            var portrait = StoryIntroLogic.Letterbox(1080, 2400);
            Assert.AreEqual(1, portrait.Width, 1e-4f);
            Assert.AreEqual(portrait.Y * 2 + portrait.Height, 1, 1e-4f);
        }

        [Test]
        public void WiderScreensGetBarsAtTheSides()
        {
            // 21:9 (2560 x 1080): the panel is 16:9 of the screen's height
            var r = StoryIntroLogic.Letterbox(2560, 1080);
            Assert.AreEqual(1, r.Height, 1e-4f);
            Assert.AreEqual(1920f / 2560f, r.Width, 1e-4f);
            Assert.AreEqual((1 - r.Width) * 0.5f, r.X, 1e-4f);
            AssertBox(StoryIntroLogic.Letterbox(3840, 1080), 0.25f, 0, 0.5f, 1);
        }

        [Test]
        public void TheLetterboxKeepsTheSixteenByNineShapeInPixels()
        {
            foreach (var (w, h) in new[] { (800f, 600f), (1366f, 768f), (2560f, 1080f), (1000f, 1000f), (3440f, 1440f) })
            {
                var r = StoryIntroLogic.Letterbox(w, h);
                Assert.AreEqual(16f / 9f, r.Width * w / (r.Height * h), 1e-3f, $"{w} x {h}");
            }
        }

        [Test]
        public void ADegenerateScreenFallsBackToFullScreen()
        {
            AssertBox(StoryIntroLogic.Letterbox(0, 0), 0, 0, 1, 1);
        }
    }
}
