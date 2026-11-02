using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfHeads.Tests
{
    /// <summary>The twelve-head table (ticket 45): its rows, the calendar, the blood colours (ticket 51) and the models behind the built ones.</summary>
    public sealed class MonsterHeadsTests
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [Test]
        public void ThereAreTwelveHeadsOnePerMonthInCalendarOrder()
        {
            Assert.AreEqual(12, MonsterHeads.Count);
            for (var i = 0; i < 12; i++)
                Assert.AreEqual(i + 1, MonsterHeads.All[i].Month, MonsterHeads.All[i].Id);
        }

        [Test]
        public void TheIdsAreTheTickets()
        {
            CollectionAssert.AreEqual(
                new[] { "yeti", "vampire", "leprechaun", "swamp", "cyclops", "cthulhu", "alien", "mummy", "werewolf", "pumpkin", "frankenstein", "santa" },
                MonsterHeads.All.Select(h => h.Id).ToArray());
        }

        [Test]
        public void EveryHeadHasANameAndAPrefabPathByItsId()
        {
            foreach (var head in MonsterHeads.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(head.DisplayName), head.Id);
                Assert.AreEqual($"Assets/Mushrooms/{head.Id}.prefab", head.PrefabPath);
                Assert.AreEqual($"Assets/Mushrooms/{head.Id}_fast.prefab", head.FastPrefabPath);
                Assert.AreEqual($"content/models/characters/monsters/{head.Id}.sdf", head.ModelPath);
            }
        }

        [Test]
        public void TheDisplayNamesAreDistinct() =>
            Assert.AreEqual(12, MonsterHeads.All.Select(h => h.DisplayName).Distinct().Count());

        [Test]
        public void LookupByIdAndMonth()
        {
            Assert.AreEqual("Abominable Snowman", MonsterHeads.ById("yeti").Value.DisplayName);
            Assert.AreEqual(10, MonsterHeads.ById("pumpkin").Value.Month);
            Assert.IsNull(MonsterHeads.ById("dragon"));
            Assert.IsNull(MonsterHeads.ById(null));
            Assert.AreEqual("santa", MonsterHeads.OfMonth(12).Value.Id);
            Assert.AreEqual("yeti", MonsterHeads.OfMonth(1).Value.Id);
            Assert.IsNull(MonsterHeads.OfMonth(0));
            Assert.IsNull(MonsterHeads.OfMonth(13));
        }

        // ---------------------------------------------------------------- blood colours

        [Test]
        public void EveryHeadHasABloodColourAndNoTwoAreAlike()
        {
            var heads = MonsterHeads.All;
            for (var i = 0; i < heads.Count; i++)
                for (var j = i + 1; j < heads.Count; j++)
                {
                    var a = heads[i].Blood;
                    var b = heads[j].Blood;
                    var distance = Mathf.Sqrt(Sq(a.r - b.r) + Sq(a.g - b.g) + Sq(a.b - b.b));
                    Assert.GreaterOrEqual(distance, 0.2f, $"{heads[i].Id} and {heads[j].Id}");
                }
        }

        [Test]
        public void NoBloodIsRealisticRed()
        {
            var red = new Color(0.8f, 0.05f, 0.05f);
            foreach (var head in MonsterHeads.All)
            {
                var c = head.Blood;
                Assert.Greater(Mathf.Sqrt(Sq(c.r - red.r) + Sq(c.g - red.g) + Sq(c.b - red.b)), 0.4f, head.Id);
            }
        }

        static float Sq(float x) => x * x;

        // ---------------------------------------------------------------- what is built

        [Test]
        public void TheBuiltHeadsAreInCalendarOrderAndStartWithThePhaseOnePair()
        {
            var built = MonsterHeads.BuiltIds().ToArray();
            CollectionAssert.Contains(built, "yeti");
            CollectionAssert.Contains(built, "pumpkin");
            var months = built.Select(id => MonsterHeads.ById(id).Value.Month).ToArray();
            CollectionAssert.AreEqual(months.OrderBy(m => m).ToArray(), months);
        }

        [Test]
        public void EveryBuiltHeadHasItsModelAndMaterials()
        {
            foreach (var head in MonsterHeads.All)
            {
                var exists = File.Exists(Path.Combine(Root, head.ModelPath));
                Assert.AreEqual(head.Built, exists, $"{head.Id}: Built says {head.Built}, the model file {(exists ? "exists" : "is missing")}");
            }
            Assert.IsTrue(Directory.Exists(Path.Combine(Root, "materials", "characters", "monsters")));
        }

        [Test]
        public void ThePumpkinsInsidesAreEmissive()
        {
            var glow = File.ReadAllText(Path.Combine(Root, "materials", "characters", "monsters", "pumpkin_glow.sdfmat"));
            StringAssert.Contains("emission = 1", glow);
            var model = File.ReadAllText(Path.Combine(Root, "content", "parts", "pumpkin_head.sdf"));
            StringAssert.Contains("material=pumpkin_glow", model);
        }

        [Test]
        public void TheRandomStandInPicksOnlyBuiltHeadsAndEveryOneOfThem()
        {
            var built = MonsterHeads.BuiltIds();
            var seen = built.ToDictionary(id => id, id => 0);
            for (var draw = 0; draw < built.Count * 4; draw++)
            {
                var id = MonsterHeads.RandomBuiltId(count => draw % count);
                Assert.IsTrue(seen.ContainsKey(id), id);
                seen[id]++;
            }
            Assert.IsTrue(seen.Values.All(n => n > 0), "every built head can be drawn");
        }

        [Test]
        public void TheRandomStandInClampsAWildGenerator()
        {
            Assert.IsNotNull(MonsterHeads.RandomBuiltId(count => 99));
            Assert.IsNotNull(MonsterHeads.RandomBuiltId(count => -3));
        }
    }
}
