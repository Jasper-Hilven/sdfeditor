using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfHeads.Tests
{
    /// <summary>The twelve explosions (ticket 46): one per head, palettes from the blood colours, the debris and specials the ticket's table lists, and the gore levels' scaling.</summary>
    public sealed class MonsterExplosionsTests
    {
        [Test]
        public void EveryHeadHasAnExplosionOfItsOwn()
        {
            Assert.AreEqual(12, MonsterExplosions.All.Count);
            foreach (var head in MonsterHeads.All)
            {
                var explosion = MonsterExplosions.Of(head.Id);
                Assert.IsNotNull(explosion, head.Id);
                Assert.AreEqual(head.Id, explosion.headId);
            }
            Assert.IsNull(MonsterExplosions.Of("nobody"));
            Assert.IsNull(MonsterExplosions.Of(null));
        }

        [Test]
        public void ThePaletteStartsFromTheBloodColour()
        {
            foreach (var head in MonsterHeads.All)
            {
                var explosion = MonsterExplosions.Of(head.Id);
                Assert.AreEqual(head.Blood, explosion.goop, $"{head.Id} goop is the blood");
                // the splat is the blood too (no second tinted copy of the goop models at a death), the mist turned toward the ticket's colour
                Assert.AreEqual(head.Blood, explosion.splat, $"{head.Id} splat is the blood");
                Assert.Greater(explosion.mist.r + explosion.mist.g + explosion.mist.b, 0.05f, head.Id);
            }
        }

        [TestCase("yeti", DebrisKind.IceShard, DebrisKind.Snowball)]
        [TestCase("vampire", DebrisKind.Bat)]
        [TestCase("leprechaun", DebrisKind.Shamrock)]
        [TestCase("swamp", DebrisKind.WeedTangle)]
        [TestCase("cyclops", DebrisKind.Eyeball)]
        [TestCase("cthulhu", DebrisKind.Tentacle)]
        [TestCase("mummy", DebrisKind.BandageStrip)]
        [TestCase("werewolf", DebrisKind.FurTuft, DebrisKind.Fang)]
        [TestCase("pumpkin", DebrisKind.PumpkinSeed)]
        [TestCase("frankenstein", DebrisKind.Bolt)]
        [TestCase("santa", DebrisKind.CandyCane, DebrisKind.ConfettiRed)]
        public void TheDebrisIsTheTicketsTable(string id, params DebrisKind[] expected)
        {
            CollectionAssert.AreEquivalent(expected, MonsterExplosions.Of(id).debris.Select(d => d.kind).ToArray());
        }

        [Test]
        public void TheAlienIsAcidSplattersWithNoDebris()
        {
            var alien = MonsterExplosions.Of("alien");
            Assert.AreEqual(0, alien.debris.Length);
            Assert.Greater(alien.extraGobs, 0);
            Assert.Greater(alien.splats, 0);
            Assert.Greater(alien.glints, 0, "glowing, sizzling");
        }

        [Test]
        public void TheSpecialsAreScriptedAndFixedInNumber()
        {
            Assert.AreEqual(ExplosionSpecial.Bats, MonsterExplosions.Of("vampire").special);
            Assert.AreEqual(ExplosionSpecial.Eyeball, MonsterExplosions.Of("cyclops").special);
            Assert.AreEqual(ExplosionSpecial.Tentacles, MonsterExplosions.Of("cthulhu").special);
            Assert.AreEqual(ExplosionSpecial.ElectricSparks, MonsterExplosions.Of("frankenstein").special);
            Assert.Greater(MonsterExplosions.Of("frankenstein").bolts, 0);
            var motionOf = new System.Collections.Generic.Dictionary<ExplosionSpecial, DebrisMotion>
            {
                { ExplosionSpecial.Bats, DebrisMotion.Fly }, { ExplosionSpecial.Eyeball, DebrisMotion.Bounce }, { ExplosionSpecial.Tentacles, DebrisMotion.Deflate },
            };
            foreach (var explosion in MonsterExplosions.All)
            {
                if (!motionOf.TryGetValue(explosion.special, out var motion))
                    continue;
                var spec = explosion.debris.Single(d => d.motion == motion);
                Assert.IsTrue(spec.fixedCount, explosion.headId);
                for (var level = 1; level <= 3; level++)
                    Assert.AreEqual(spec.count, MonsterExplosions.CountOf(spec, level), $"{explosion.headId} at {level}");
            }
            // the one giant eyeball
            Assert.AreEqual(1, MonsterExplosions.Of("cyclops").debris[0].count);
        }

        [Test]
        public void TheMummyHasTheSandCloud()
        {
            var mummy = MonsterExplosions.Of("mummy");
            foreach (var other in MonsterExplosions.All.Where(e => e.headId != "mummy" && e.headId != "vampire"))
                Assert.Greater(mummy.mistPuffs, other.mistPuffs, other.headId);
            Assert.Greater(mummy.mistSize.y, 0.4f);
        }

        [Test]
        public void EveryExplosionDoesSomethingAndItsSoundIsOneOfFive()
        {
            foreach (var explosion in MonsterExplosions.All)
            {
                var things = explosion.debris.Length + explosion.mistPuffs + explosion.extraGobs + explosion.glints + explosion.sparks + explosion.drops + explosion.bolts;
                Assert.Greater(things, 0, explosion.headId);
            }
            Assert.AreEqual(ExplosionSound.Ice, MonsterExplosions.Of("yeti").sound);
            Assert.AreEqual(ExplosionSound.Sparks, MonsterExplosions.Of("frankenstein").sound);
            Assert.AreEqual(5, System.Enum.GetValues(typeof(ExplosionSound)).Length);
        }

        [Test]
        public void TheGoreLevelsThrowMore()
        {
            Assert.AreEqual(0, MonsterExplosions.DebrisScale(0));
            Assert.Less(MonsterExplosions.DebrisScale(1), MonsterExplosions.DebrisScale(2));
            Assert.Less(MonsterExplosions.DebrisScale(2), MonsterExplosions.DebrisScale(3));
            Assert.Less(MonsterExplosions.MistScale(1), MonsterExplosions.MistScale(3));
            Assert.AreEqual(0, MonsterExplosions.ExtraSplats(1));
            Assert.Less(MonsterExplosions.ExtraSplats(2), MonsterExplosions.ExtraSplats(3));
            Assert.AreEqual(1, MonsterExplosions.SpeedScale(2));
            Assert.Greater(MonsterExplosions.SpeedScale(3), 1);
            var santa = MonsterExplosions.Of("santa");
            Assert.AreEqual(0, MonsterExplosions.DebrisOf(santa, 0), "off throws nothing: the head only puffs");
            Assert.Less(MonsterExplosions.DebrisOf(santa, 1), MonsterExplosions.DebrisOf(santa, 2));
            Assert.Less(MonsterExplosions.DebrisOf(santa, 2), MonsterExplosions.DebrisOf(santa, 3));
        }

        [Test]
        public void EightDeathsAtOnceFitTheDebrisPoolAtEveryLevel()
        {
            // The pool is 640 at the web's quality; the most debris any head throws, eight at once, at Ultra Gore
            const int pool = 640;
            foreach (var explosion in MonsterExplosions.All)
                Assert.LessOrEqual(MonsterExplosions.DebrisOf(explosion, 3) * 8, pool, explosion.headId);
        }

        [Test]
        public void EveryDebrisKindHasAModelName()
        {
            var names = System.Enum.GetValues(typeof(DebrisKind)).Cast<DebrisKind>().Select(DebrisKinds.ModelOf).ToArray();
            Assert.AreEqual(DebrisKinds.Count, names.Distinct().Count());
            foreach (var name in names)
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "content", "models", "effects", "monster", name + ".sdf"))), name);
        }
    }
}
