using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SdfStory.Layout;

namespace SdfStory.Tests
{
    /// <summary>Ticket 31.6: the lettering's pure layout: text fitting, balloon placement in the safe area, tails, device outlines, the coverage mask.</summary>
    public sealed class StoryLetterLayoutTests
    {
        private static readonly Box Frame = new Box(0, 0, 1920, 1080);

        // A stand-in for the font: every character is 0.5 of the size wide, lines are 1.2 of the size high, words break at spaces
        private static Pt FakeMeasure(string text, float size, float width)
        {
            var words = text.Split(' ');
            var perChar = size * 0.5f;
            var lines = 1;
            var x = 0f;
            var widest = 0f;
            foreach (var word in words)
            {
                var w = word.Length * perChar;
                if (x > 0 && x + perChar + w > width)
                {
                    lines++;
                    x = 0;
                }
                x += (x > 0 ? perChar : 0) + w;
                widest = System.Math.Max(widest, x);
            }
            return new Pt(widest, lines * size * 1.2f);
        }

        private const string Sentence = "42! You're here! Prime's rigged the whole tournament. Every level. Also, welcome.";

        [Test]
        public void SafeAreaIsInsideTheBorderAndTheEdgeMargin()
        {
            var safe = StoryLetterLayout.SafeArea(Frame);
            Assert.AreEqual(StoryLetterLayout.BorderWidth + StoryLetterLayout.EdgeMargin, safe.X, 0.001f);
            Assert.AreEqual(1920 - 2 * safe.X, safe.W, 0.001f);
            Assert.AreEqual(1080 - 2 * safe.Y, safe.H, 0.001f);
            Assert.GreaterOrEqual(safe.X, 5f);
        }

        [Test]
        public void FitSizeIsTheLargestThatFits()
        {
            Pt Measure(float size, float width) => FakeMeasure(Sentence, size, width);
            var size = StoryLetterLayout.FitSize(Measure, 400, 150, 10, 60);
            Assert.LessOrEqual(Measure(size, 400).Y, 150);
            Assert.Greater(Measure(size + 1.5f, 400).Y, 150);
        }

        [Test]
        public void FitSizeKeepsTheMaximumWhenItFits()
        {
            Pt Measure(float size, float width) => FakeMeasure("Hi", size, width);
            Assert.AreEqual(40, StoryLetterLayout.FitSize(Measure, 400, 400, 10, 40));
        }

        [Test]
        public void FitSizeNeverGoesBelowTheMinimum()
        {
            Pt Measure(float size, float width) => FakeMeasure(Sentence, size, width);
            Assert.GreaterOrEqual(StoryLetterLayout.FitSize(Measure, 100, 5, 18, 60), 18);
        }

        [Test]
        public void ColumnWidthIsTheNarrowestThatReadsWide()
        {
            Pt Measure(float size, float width) => FakeMeasure(Sentence, size, width);
            var width = StoryLetterLayout.ColumnWidth(Measure, 36, 150, 800, 0.42f);
            Assert.LessOrEqual(Measure(36, width).Y, width * 0.42f + 0.01f);
            Assert.GreaterOrEqual(width, 150);
            Assert.LessOrEqual(width, 800);
            Assert.Greater(Measure(36, width - 40).Y, (width - 40) * 0.42f);
        }

        [Test]
        public void ColumnWidthFallsBackToTheWidest()
        {
            Pt Measure(float size, float width) => FakeMeasure(Sentence, size, width);
            Assert.AreEqual(300, StoryLetterLayout.ColumnWidth(Measure, 60, 100, 300, 0.05f));
        }

        [TestCase(0f, 0f)]
        [TestCase(1900f, 1070f)]
        [TestCase(-50f, 500f)]
        [TestCase(960f, 2000f)]
        public void PlacedBoxesStayInsideTheSafeArea(float x, float y)
        {
            var safe = StoryLetterLayout.SafeArea(Frame);
            var box = StoryLetterLayout.Place(new Box(x, y, 500, 200), safe, new List<Box>());
            Assert.IsTrue(box.IsInside(safe), box.ToString());
            Assert.AreEqual(500, box.W);
            Assert.AreEqual(200, box.H);
        }

        [Test]
        public void PlacedBoxesKeepApart()
        {
            var safe = StoryLetterLayout.SafeArea(Frame);
            var first = new Box(400, 100, 500, 200);
            var second = StoryLetterLayout.Place(new Box(450, 150, 500, 200), safe, new List<Box> { first });
            Assert.IsFalse(second.Overlaps(first), second.ToString());
            Assert.IsTrue(second.IsInside(safe));
        }

        [Test]
        public void PlacedBoxesAreNotMovedWhenFree()
        {
            var safe = StoryLetterLayout.SafeArea(Frame);
            var wanted = new Box(1200, 600, 300, 150);
            var box = StoryLetterLayout.Place(wanted, safe, new List<Box> { new Box(100, 100, 200, 100) });
            Assert.AreEqual(wanted.X, box.X);
            Assert.AreEqual(wanted.Y, box.Y);
        }

        [Test]
        public void ABoxTooBigForTheAreaIsCentred()
        {
            var area = new Box(0, 0, 100, 100);
            var box = StoryLetterLayout.ClampInto(new Box(10, 10, 300, 40), area);
            Assert.AreEqual(-100, box.X, 0.001f);
            Assert.AreEqual(10, box.Y, 0.001f);
        }

        [Test]
        public void BalloonBodyFillsItsBoxAndStaysInside()
        {
            var box = new Box(100, 200, 400, 160);
            var body = StoryLetterLayout.BalloonBody(box);
            Assert.AreEqual(box.X, body.Min(p => p.X), 0.5f);
            Assert.AreEqual(box.Right, body.Max(p => p.X), 0.5f);
            Assert.AreEqual(box.Y, body.Min(p => p.Y), 0.5f);
            Assert.AreEqual(box.Bottom, body.Max(p => p.Y), 0.5f);
        }

        [Test]
        public void BodyAroundHoldsItsContent()
        {
            var body = StoryLetterLayout.BodyAround(new Pt(500, 300), 300, 100);
            Assert.Greater(body.W, 300);
            Assert.Greater(body.H, 100);
            Assert.AreEqual(500, body.Center.X, 0.001f);
            Assert.AreEqual(300, body.Center.Y, 0.001f);
        }

        [Test]
        public void TailPointsAtTheTargetAndStopsShort()
        {
            var body = new Box(400, 100, 400, 160);
            var target = new Pt(600, 700);
            var tail = StoryLetterLayout.Tail(body, target, 20, 600);
            Assert.NotNull(tail);
            Assert.AreEqual(3, tail.Length);
            var tip = tail[1];
            Assert.AreEqual(20, Pt.Distance(tip, target), 1f);
            Assert.AreEqual(600, tip.X, 2f);
            // The base lies inside the body
            Assert.IsTrue(body.Contains(tail[0]));
            Assert.IsTrue(body.Contains(tail[2]));
        }

        [Test]
        public void TailIsCappedInLength()
        {
            var body = new Box(400, 100, 400, 160);
            var tail = StoryLetterLayout.Tail(body, new Pt(600, 1050), 20, 100);
            var edgeY = body.Bottom;
            Assert.LessOrEqual(tail[1].Y - edgeY, 101);
        }

        [Test]
        public void NoTailWhenTheTargetIsInsideOrAtTheBody()
        {
            var body = new Box(400, 100, 400, 160);
            Assert.IsNull(StoryLetterLayout.Tail(body, body.Center, 20, 300));
            Assert.IsNull(StoryLetterLayout.Tail(body, new Pt(600, 270), 20, 300));
        }

        [Test]
        public void NoteArrowLeavesTheSideFacingTheTargetAndStopsShort()
        {
            var box = new Box(400, 300, 200, 200);
            var arrow = StoryLetterLayout.NoteArrow(box, new Pt(100, 400), 10, 600);
            Assert.NotNull(arrow);
            Assert.AreEqual(3, arrow.Length);
            Assert.AreEqual(10, Pt.Distance(arrow[1], new Pt(100, 400)), 1f);
            Assert.AreEqual(400, arrow[1].Y, 1f);
            Assert.IsTrue(box.Contains(arrow[0]));
            Assert.IsTrue(box.Contains(arrow[2]));
            Assert.IsNull(StoryLetterLayout.NoteArrow(box, box.Center, 10, 600));
        }

        [Test]
        public void JaggedBoxStaysWithinItsReach()
        {
            var box = new Box(300, 100, 600, 160);
            var points = StoryLetterLayout.JaggedBox(box, 40, 14, 5);
            Assert.GreaterOrEqual(points.Length, 30);
            var reach = box.Grow(14.01f);
            Assert.IsTrue(points.All(p => reach.Contains(p)));
            // Some points go out beyond the plain box, and some stay inside it
            Assert.IsTrue(points.Any(p => !box.Contains(p)));
            Assert.IsTrue(points.Any(p => box.Contains(p)));
        }

        [Test]
        public void BurstHasSpikesWithinItsReach()
        {
            var centre = new Pt(500, 400);
            var points = StoryLetterLayout.Burst(centre, 200, 120, 15, 3);
            Assert.AreEqual(30, points.Length);
            Assert.IsTrue(points.All(p => System.Math.Abs(p.X - centre.X) <= 200 * 1.081f && System.Math.Abs(p.Y - centre.Y) <= 120 * 1.081f));
            var outer = points.Where((p, i) => i % 2 == 0).Average(p => Pt.Distance(p, centre) / 200);
            var inner = points.Where((p, i) => i % 2 == 1).Average(p => Pt.Distance(p, centre) / 200);
            Assert.Greater(outer, inner);
        }

        [Test]
        public void FittedBurstHoldsTheTurnedTextWithMargin()
        {
            foreach (var (w, h, angle) in new[] { (300f, 70f, -8f), (120f, 70f, 8f), (700f, 90f, -4f), (200f, 80f, 25f) })
            {
                var centre = new Pt(900, 500);
                var fill = StoryLetterLayout.FitBurst(centre, new Pt(w, h), angle, 12, 5).Select(p => centre + (p - centre) * StoryLetterLayout.BurstFill).ToArray();
                var turn = angle * System.Math.PI / 180;
                var cos = (float)System.Math.Cos(turn);
                var sin = (float)System.Math.Sin(turn);
                foreach (var (x, y) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var lx = x * (w / 2 + 12);
                    var ly = y * (h / 2 + 12);
                    var corner = new Pt(centre.X + lx * cos + ly * sin, centre.Y - lx * sin + ly * cos);
                    Assert.IsTrue(StoryLetterLayout.Inside(fill, corner), $"{w}x{h} at {angle}: corner {x},{y} outside");
                }
            }
        }

        [Test]
        public void LayoutIsDeterministic()
        {
            var a = StoryLetterLayout.Burst(new Pt(10, 10), 50, 50, 12, 9);
            var b = StoryLetterLayout.Burst(new Pt(10, 10), 50, 50, 12, 9);
            CollectionAssert.AreEqual(a.Select(p => p.ToString()), b.Select(p => p.ToString()));
            var c = StoryLetterLayout.Burst(new Pt(10, 10), 50, 50, 12, 10);
            CollectionAssert.AreNotEqual(a.Select(p => p.ToString()), c.Select(p => p.ToString()));
        }

        [Test]
        public void BorderStaysInsideTheFrameAndHasWidth()
        {
            var ring = StoryLetterLayout.WobblyRect(Frame, StoryLetterLayout.BorderWidth, 1.8f, 11);
            foreach (var p in ring)
            {
                var depth = System.Math.Min(System.Math.Min(p.X, 1920 - p.X), System.Math.Min(p.Y, 1080 - p.Y));
                Assert.GreaterOrEqual(depth, StoryLetterLayout.BorderWidth - 1.81f);
                Assert.LessOrEqual(depth, StoryLetterLayout.BorderWidth + 1.81f);
            }
            Assert.Greater(ring.Length, 20);
        }

        [Test]
        public void BorderRingCoversOnlyTheEdge()
        {
            var mask = new CoverageMask(200, 100);
            var outer = new[] { new Pt(0, 0), new Pt(200, 0), new Pt(200, 100), new Pt(0, 100) };
            mask.Fill(outer, StoryLetterLayout.WobblyRect(new Box(0, 0, 200, 100), 8, 0, 1, 40));
            Assert.AreEqual(1, mask.At(2, 50), 0.01f);
            Assert.AreEqual(1, mask.At(100, 3), 0.01f);
            Assert.AreEqual(0, mask.At(100, 50), 0.01f);
            Assert.AreEqual(200 * 100 - 184 * 84, mask.Area(), 1f);
        }

        [Test]
        public void RadialLinesRunOutwardsAndLeaveTheMiddleClear()
        {
            var centre = Frame.Center;
            var lines = StoryLetterLayout.RadialLines(Frame, centre, 1.5f, 0.55f, 4);
            Assert.AreEqual(165, lines.Count);
            foreach (var line in lines)
            {
                Assert.Greater(Pt.Distance(line.To, centre), Pt.Distance(line.From, centre));
                Assert.GreaterOrEqual(Pt.Distance(line.From, centre), 40);
            }
            Assert.Greater(lines.Average(l => Pt.Distance(l.From, centre)), 300);
        }

        [Test]
        public void ParallelLinesFollowTheAngle()
        {
            foreach (var angle in new[] { 0f, 45f, 90f })
            {
                var lines = StoryLetterLayout.ParallelLines(Frame, angle, 1, 2);
                Assert.Greater(lines.Count, 10);
                var radians = angle * System.Math.PI / 180;
                foreach (var line in lines)
                {
                    var along = (line.To - line.From).Normalized();
                    // Counter-clockwise on a screen with y down: the line's direction is (cos, -sin) up to sign
                    Assert.AreEqual(1f, System.Math.Abs(along.X * System.Math.Cos(radians) - along.Y * System.Math.Sin(radians)), 0.001f);
                }
            }
        }

        [Test]
        public void MotionLinesTrailBehindTheThing()
        {
            var at = new Pt(960, 540);
            var lines = StoryLetterLayout.MotionLines(at, 0, 300, 1);
            Assert.AreEqual(3, lines.Count);
            // Moving right: the streaks lie to the left of the thing
            Assert.IsTrue(lines.All(l => l.To.X <= at.X + 1 && l.From.X < l.To.X));
            Assert.IsTrue(lines.Any(l => l.From.X < at.X - 150));
        }

        [Test]
        public void MaskCoversARectangleExactly()
        {
            var mask = new CoverageMask(50, 40);
            mask.Fill(new[] { new Pt(10, 10), new Pt(30, 10), new Pt(30, 30), new Pt(10, 30) });
            Assert.AreEqual(400, mask.Area(), 0.5f);
            Assert.AreEqual(1, mask.At(15, 15), 0.001f);
            Assert.AreEqual(0, mask.At(5, 15), 0.001f);
        }

        [Test]
        public void MaskAntialiasesAFractionalEdge()
        {
            var mask = new CoverageMask(20, 20);
            mask.Fill(new[] { new Pt(5.5f, 5), new Pt(15, 5), new Pt(15, 15), new Pt(5.5f, 15) });
            Assert.AreEqual(0.5f, mask.At(5, 8), 0.02f);
            Assert.AreEqual(1, mask.At(6, 8), 0.001f);
        }

        [Test]
        public void MaskDiscHasItsArea()
        {
            var mask = new CoverageMask(100, 100);
            mask.Fill(CoverageMask.Disc(new Pt(50, 50), 20, 64));
            Assert.AreEqual(System.Math.PI * 400, mask.Area(), 12f);
        }

        [Test]
        public void MaskCombinesOverlapsWithTheLargerCoverage()
        {
            var mask = new CoverageMask(40, 40);
            var square = new[] { new Pt(10, 10), new Pt(20, 10), new Pt(20, 20), new Pt(10, 20) };
            mask.Fill(square);
            mask.Fill(square);
            Assert.AreEqual(100, mask.Area(), 0.5f);
        }

        [Test]
        public void MaskFillLeavesAHoleForASecondContour()
        {
            var mask = new CoverageMask(40, 40);
            mask.Fill(
                new[] { new Pt(0, 0), new Pt(40, 0), new Pt(40, 40), new Pt(0, 40) },
                new[] { new Pt(10, 10), new Pt(30, 10), new Pt(30, 30), new Pt(10, 30) });
            Assert.AreEqual(0, mask.At(20, 20), 0.001f);
            Assert.AreEqual(1, mask.At(2, 2), 0.001f);
            Assert.AreEqual(1600 - 400, mask.Area(), 0.5f);
        }

        [Test]
        public void MaskStrokeHasTheLinesWidth()
        {
            var mask = new CoverageMask(100, 40);
            mask.Stroke(new[] { new Pt(10, 20), new Pt(90, 20) }, 6, false);
            Assert.AreEqual(1, mask.At(50, 20), 0.001f);
            Assert.AreEqual(1, mask.At(50, 18), 0.001f);
            Assert.AreEqual(0, mask.At(50, 14), 0.001f);
            Assert.AreEqual(80 * 6, mask.Area(), 8f);
        }

        [Test]
        public void MaskClipsToItsBounds()
        {
            var mask = new CoverageMask(20, 20);
            mask.Fill(new[] { new Pt(-50, -50), new Pt(50, -50), new Pt(50, 50), new Pt(-50, 50) });
            Assert.AreEqual(400, mask.Area(), 0.5f);
        }

        [Test]
        public void RandomIsDeterministicAndInRange()
        {
            var a = new StoryRandom(5);
            var b = new StoryRandom(5);
            for (var i = 0; i < 100; i++)
            {
                var value = a.Value();
                Assert.AreEqual(value, b.Value());
                Assert.GreaterOrEqual(value, 0);
                Assert.Less(value, 1);
            }
        }
    }
}
