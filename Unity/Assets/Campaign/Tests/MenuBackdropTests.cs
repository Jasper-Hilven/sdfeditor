using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SdfBattle;
using UnityEngine;

namespace SdfCampaign.Tests
{
    /// <summary>The menus' backdrop rules (ticket 35): the camera shots, the desk's props, the wall's layout, the thumbnails and the choice of world.</summary>
    public sealed class MenuBackdropTests
    {
        static readonly CampaignDefinition Def = CampaignDefinition.Default();

        static CampaignProgress Fresh() => CampaignProgress.Load(Def, new MemoryCampaignStorage());

        static KeyValuePair<string, int> C(string name, int count) => new KeyValuePair<string, int>(name, count);

        [Test]
        public void Shots_AreDifferentPlaces()
        {
            var wide = MenuShots.Pose(MenuShot.Wide);
            var monitor = MenuShots.Pose(MenuShot.Monitor);
            var wall = MenuShots.Pose(MenuShot.Wall);
            Assert.Less(monitor.fov, wide.fov);
            Assert.Less(monitor.position.z, wide.position.z, "the monitor shot is pushed in");
            Assert.AreNotEqual(wall.position, wide.position);
        }

        [Test]
        public void Follow_IsFrameRateIndependent()
        {
            var one = MenuShots.Follow(0.1f);
            var two = 1 - (1 - MenuShots.Follow(0.05f)) * (1 - MenuShots.Follow(0.05f));
            Assert.AreEqual(one, two, 1e-5f);
            Assert.AreEqual(0.5f, MenuShots.Follow(MenuShots.HalfLife), 1e-5f);
            Assert.AreEqual(0, MenuShots.Follow(0));
        }

        [Test]
        public void Drift_IsSmallAndSlow()
        {
            for (var t = 0f; t < 600; t += 0.37f)
            {
                var d = MenuShots.Drift(t);
                Assert.Less(d.magnitude, 0.12f);
                Assert.Less((MenuShots.Drift(t + 0.02f) - d).magnitude, 0.002f);
            }
        }

        [Test]
        public void Props_OnePerArenaWon()
        {
            var p = Fresh();
            Assert.AreEqual(0, DeskProgress.Earned(p).Count);
            p.RecordWin("meadow", 3, 1, 400, Difficulty.Normal);
            p.RecordWin("fungal", 3, 1, 400, Difficulty.Normal);
            CollectionAssert.AreEqual(new[] { "flower0", "puffball0" }, DeskProgress.Earned(p).Select(x => x.model).ToArray());
            Assert.AreEqual(Def.Battles().Count(), DeskProgress.Props.Count, "every arena has its prop");
            CollectionAssert.AreEquivalent(Def.Battles().Select(b => b.id).ToArray(), DeskProgress.Props.Select(x => x.arena).ToArray());
        }

        [Test]
        public void Trophy_IsGoldOnlyWhenComplete()
        {
            var p = Fresh();
            Assert.IsFalse(DeskProgress.TrophyGold(p));
            Assert.IsTrue(DeskProgress.TrophyGold(p, previewComplete: true));
            foreach (var step in Def.Steps)
                if (step.IsBattle) p.RecordWin(step.id, 3, 1, 400, Difficulty.Normal);
                else p.CompleteCutscene(step.id);
            Assert.IsTrue(DeskProgress.TrophyGold(p));
            Assert.IsFalse(DeskProgress.TrophyGold(null));
        }

        [Test]
        public void Tint_RunsFromCoolToGold()
        {
            var start = WallTint.For(0);
            var end = WallTint.For(1);
            Assert.Greater(start.b, start.r);
            Assert.Greater(end.r, end.b);
            Assert.AreEqual(WallTint.For(0), WallTint.For(-3));
            Assert.AreEqual(WallTint.For(1), WallTint.For(9));
            var p = Fresh();
            Assert.AreEqual(0, WallTint.Fraction(Def, p));
            p.RecordWin("meadow", 3, 1, 400, Difficulty.Normal);
            Assert.AreEqual(1 / 8f, WallTint.Fraction(Def, p), 1e-5f);
        }

        [Test]
        public void SeenComics_OnlyWhatWasSeen_InOrder()
        {
            var p = Fresh();
            System.Func<string, int> counts = id => id == "ending" ? 0 : 4;
            Assert.AreEqual(0, WallContent.SeenComics(Def, p, counts).Count);
            p.CompleteCutscene("intro");
            p.RecordWin("meadow", 3, 1, 400, Difficulty.Normal);
            p.CompleteCutscene("beat-fungal");
            CollectionAssert.AreEqual(new[] { "intro", "beat-fungal" }, WallContent.SeenComics(Def, p, counts));
            // A comic with no panels is left out; the credits are no comic
            Assert.IsFalse(WallContent.SeenComics(Def, p, _ => 0).Any());
        }

        [Test]
        public void Wall_ShowsOnlySeenPanels_BlankElsewhere()
        {
            var cards = WallLayout.Build(new[] { C("intro", 12), C("beat-fungal", 4) });
            Assert.AreEqual(16, cards.Count(c => !c.Blank));
            Assert.AreEqual(WallLayout.MinimumSheets, cards.Count, "blank sheets make it up to the minimum");
            CollectionAssert.AreEqual(Enumerable.Range(0, 12).ToArray(), cards.Where(c => c.comic == "intro").Select(c => c.panel).ToArray());
            // Nothing is seen: a wall of blank sheets, and no name of a comic anywhere
            var empty = WallLayout.Build(new KeyValuePair<string, int>[0]);
            Assert.AreEqual(WallLayout.MinimumSheets, empty.Count);
            Assert.IsTrue(empty.All(c => c.Blank));
        }

        [Test]
        public void Wall_IsDeterministic_AndKeepsInsideTheWall()
        {
            var a = WallLayout.Build(new[] { C("intro", 12), C("beat-fungal", 4) });
            var b = WallLayout.Build(new[] { C("intro", 12), C("beat-fungal", 4) });
            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].centre, b[i].centre);
                Assert.AreEqual(a[i].tilt, b[i].tilt);
                Assert.LessOrEqual(Mathf.Abs(a[i].tilt), 7.01f);
                Assert.LessOrEqual(Mathf.Abs(a[i].centre.x), WallLayout.Columns * WallLayout.CellWidth / 2);
                Assert.LessOrEqual(Mathf.Abs(a[i].centre.y), WallLayout.Rows * WallLayout.CellHeight / 2);
            }
        }

        [Test]
        public void Wall_ThinsToItsCapacity_AndAvoidsKeepOutPlaces()
        {
            var many = WallLayout.Build(new[] { C("intro", 12), C("a", 40), C("b", 40) });
            Assert.LessOrEqual(many.Count, WallLayout.Capacity);
            Assert.IsTrue(many.Any(c => c.comic == "a") && many.Any(c => c.comic == "b"), "every comic keeps some panels");
            var free = WallLayout.Build(new[] { C("intro", 12) }, centre => !(Mathf.Abs(centre.x) < 0.6f && centre.y < 0.3f));
            Assert.IsTrue(free.All(c => !(Mathf.Abs(c.centre.x) < 0.55f && c.centre.y < 0.25f)));
            Assert.AreEqual(12, free.Count(c => !c.Blank));
        }

        [Test]
        public void Wall_NoTwoCardsOverlapMuch()
        {
            var cards = WallLayout.Build(new[] { C("intro", 12), C("a", 30) });
            for (var i = 0; i < cards.Count; i++)
            for (var j = i + 1; j < cards.Count; j++)
            {
                var d = cards[i].centre - cards[j].centre;
                Assert.IsTrue(Mathf.Abs(d.x) > WallLayout.CardWidth * 0.85f || Mathf.Abs(d.y) > WallLayout.CardHeight * 0.8f, $"cards {i} and {j} sit on each other");
            }
        }

        [Test]
        public void AtlasIndex_RoundTrips()
        {
            var text = WallAtlas.WriteIndex(new[] { C("intro", 12), C("beat-fungal", 4) });
            var parsed = WallAtlas.ParseIndex(text);
            Assert.AreEqual(12, parsed["intro"]);
            Assert.AreEqual(4, parsed["beat-fungal"]);
            Assert.AreEqual(0, WallAtlas.ParseIndex(null).Count);
            Assert.AreEqual(0, WallAtlas.ParseIndex("garbage\nintro x\n").Count);
        }

        [Test]
        public void AtlasCells_TileTheAtlas()
        {
            Assert.AreEqual(3, WallAtlas.Rows(12));
            Assert.AreEqual(2, WallAtlas.Rows(5));
            var first = WallAtlas.Cell(0, 12);
            Assert.AreEqual(new Rect(0, 2 / 3f, 0.25f, 1 / 3f), first, "the first panel is the top left");
            var last = WallAtlas.Cell(11, 12);
            Assert.AreEqual(0.75f, last.x, 1e-5f);
            Assert.AreEqual(0f, last.y, 1e-5f);
        }

        [Test]
        public void Backdrop_AutoIsTheDesk_ExceptOnTheLowestQualityOrWithoutModels()
        {
            Assert.AreEqual(BackdropMode.Desk, BackdropChoice.Resolve(BackdropChoice.Auto, false, true));
            Assert.AreEqual(BackdropMode.FlatWall, BackdropChoice.Resolve(BackdropChoice.Auto, true, true));
            Assert.AreEqual(BackdropMode.FlatWall, BackdropChoice.Resolve(BackdropChoice.Auto, false, false));
            Assert.AreEqual(BackdropMode.FlatWall, BackdropChoice.Resolve(BackdropChoice.ForceFlat, false, true));
            Assert.AreEqual(BackdropMode.Desk, BackdropChoice.Resolve(BackdropChoice.ForceDesk, true, true));
            Assert.AreEqual(BackdropMode.FlatWall, BackdropChoice.Resolve(BackdropChoice.ForceDesk, false, false));
        }
    }
}
