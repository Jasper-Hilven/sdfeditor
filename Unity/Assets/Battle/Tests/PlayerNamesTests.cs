using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfBattle.Tests
{
    /// <summary>The bots' names: thousands of handles, each fitting the HUD, and a match's draw never repeats one.</summary>
    public sealed class PlayerNamesTests
    {
        [Test]
        public void ThePool_HoldsThousands_EachDifferentAndShort()
        {
            var all = PlayerNames.All;

            Assert.GreaterOrEqual(all.Count, 2000);
            Assert.AreEqual(all.Count, all.Select(PlayerNames.Key).Distinct().Count(), "no two names read alike");
            foreach (var name in all)
            {
                Assert.LessOrEqual(name.Length, PlayerNames.MaxLength, name);
                Assert.AreEqual(name.Trim(), name, name);
                Assert.IsNotEmpty(PlayerNames.Key(name), name);
            }
            CollectionAssert.DoesNotContain(all, Cast.You, "your name is yours alone");
        }

        [Test]
        public void TheHandles_AreAllInThePool()
        {
            Assert.GreaterOrEqual(PlayerNames.Handles.Count, 300);
            CollectionAssert.IsSubsetOf(PlayerNames.Handles, PlayerNames.All);
        }

        [Test]
        public void TheDraw_NeverRepeatsANameOrYours()
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var draw = new NameCast(new Random(seed)).Reserve(Cast.You);
                var names = Enumerable.Range(0, 12).Select(_ => draw.Draw()).ToList();

                Assert.AreEqual(names.Count, names.Select(PlayerNames.Key).Distinct().Count(), string.Join(", ", names));
                CollectionAssert.DoesNotContain(names, Cast.You);
            }
        }

        [Test]
        public void TheSameSeed_DrawsTheSameNames_AndMatchesDiffer()
        {
            List<string> Draw(int seed)
            {
                var draw = new NameCast(new Random(seed));
                return Enumerable.Range(0, 8).Select(_ => draw.Draw()).ToList();
            }

            CollectionAssert.AreEqual(Draw(7), Draw(7));
            CollectionAssert.AreNotEqual(Draw(7), Draw(8));
        }

        [Test]
        public void ADrawnOutPool_FallsBackToPlainNames()
        {
            var draw = new NameCast(new Random(1)).Reserve(PlayerNames.All);

            Assert.AreEqual("Player 1", draw.Draw());
            Assert.AreEqual("Player 2", draw.Draw());
        }

        [Test]
        public void TheSetupsNames_AreKept_AndNeverDrawn()
        {
            var setup = new MatchSetup { redNames = new[] { "Me", "ToastyMage" } };
            var draw = new NameCast(new Random(3)).Reserve(setup.redNames);

            Assert.AreEqual("ToastyMage", setup.NameOf(Team.Red, 2, draw));
            var drawn = Enumerable.Range(3, 300).Select(n => setup.NameOf(Team.Red, n, draw)).ToList();
            CollectionAssert.DoesNotContain(drawn, "ToastyMage");
            CollectionAssert.AllItemsAreUnique(drawn);
        }
    }
}
