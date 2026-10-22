using NUnit.Framework;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>The death burst's numbers: who bursts, which way the pieces fly, how much blood, how many corpses stay.</summary>
    public sealed class GoreRulesTests
    {
        [Test]
        public void OnlyAKill_WithGoreOn_Bursts()
        {
            Assert.IsTrue(GoreRules.Bursts(GoreRules.Splatty, DeathCause.Killed));
            Assert.IsTrue(GoreRules.Bursts(GoreRules.ExtraSplatty, DeathCause.Killed));
            Assert.IsTrue(GoreRules.Bursts(GoreRules.UltraGore, DeathCause.Killed));
            Assert.IsFalse(GoreRules.Bursts(GoreRules.Off, DeathCause.Killed));
            Assert.IsFalse(GoreRules.Bursts(GoreRules.Splatty, DeathCause.Fell));
            Assert.AreEqual(0, GoreRules.BloodGobs(GoreRules.Off, WeaponIds.Pistol));
        }

        [Test]
        public void TheEyesAndHands_FlyAwayFromTheHit_AndUp()
        {
            // Shot from the west: the pieces go east, whichever side of the body they were on
            var away = Vector3.right;
            foreach (var outward in new[] { Vector3.left, Vector3.forward, Vector3.back })
            {
                var velocity = GoreRules.Launch(GibKind.Loose, outward, away, false, 1, Vector3.zero, 0.5f);
                Assert.Greater(Vector3.Dot(velocity, away), -0.01f, $"{outward}");
                Assert.Greater(velocity.y, 0);
                Assert.Greater(velocity.magnitude, 4);
            }
        }

        [Test]
        public void AHeadshot_PopsTheCapHigher_AndTheEyesFurther()
        {
            var cap = GoreRules.Launch(GibKind.Cap, Vector3.zero, Vector3.right, false, 1, Vector3.zero, 0.5f);
            var poppedCap = GoreRules.Launch(GibKind.Cap, Vector3.zero, Vector3.right, true, 1, Vector3.zero, 0.5f);
            Assert.Greater(poppedCap.y, cap.y * 1.5f);
            var eye = GoreRules.Launch(GibKind.Loose, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f);
            var poppedEye = GoreRules.Launch(GibKind.Loose, Vector3.left, Vector3.right, true, 1, Vector3.zero, 0.5f);
            Assert.Greater(poppedEye.magnitude, eye.magnitude);
        }

        [Test]
        public void TheStem_SlumpsNearTheSpot_AndACannonThrowsHarder()
        {
            var stem = GoreRules.Launch(GibKind.Stem, Vector3.left, Vector3.right, false, 1, Vector3.zero, 1);
            Assert.Less(stem.magnitude, 2);
            var shot = GoreRules.Launch(GibKind.Loose, Vector3.left, Vector3.right, false, GoreRules.Strength(WeaponIds.Pistol), Vector3.zero, 0.5f);
            var cannon = GoreRules.Launch(GibKind.Loose, Vector3.left, Vector3.right, false, GoreRules.Strength("cannon"), Vector3.zero, 0.5f);
            Assert.Greater(cannon.magnitude, shot.magnitude * 1.4f);
        }

        [Test]
        public void UltraGore_TriplesTheBlood_AndFlingsEveryPiece()
        {
            Assert.AreEqual(GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol) * 3, GoreRules.BloodGobs(GoreRules.UltraGore, WeaponIds.Pistol));
            Assert.IsFalse(GoreRules.Flings(GoreRules.ExtraSplatty));
            Assert.IsTrue(GoreRules.Flings(GoreRules.UltraGore));
            var stem = GoreRules.Launch(GibKind.Stem, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f, true);
            Assert.Greater(stem.magnitude, 5);
            var eye = GoreRules.Launch(GibKind.Loose, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f);
            var flungEye = GoreRules.Launch(GibKind.Loose, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f, true);
            Assert.Greater(flungEye.magnitude, eye.magnitude * 1.4f);
        }

        [Test]
        public void TheBlood_IsLots_LessWhenCooked()
        {
            Assert.GreaterOrEqual(GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol), 20);
            Assert.Greater(GoreRules.BloodGobs(GoreRules.ExtraSplatty, WeaponIds.Pistol), GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol));
            Assert.Less(GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Flamethrower), GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol));
        }

        [Test]
        public void TheBlood_TakesTheTeamsHue_KeepingTheGoosShade()
        {
            var goo = new Color(0.85f, 0.18f, 0.65f);
            Color.RGBToHSV(goo, out _, out var s, out var v);
            Color.RGBToHSV(new Color(0.15f, 0.4f, 0.9f), out var blue, out _, out _);
            var blood = GoreRules.Recolour(goo, blue);
            Color.RGBToHSV(blood, out var h2, out var s2, out var v2);
            Assert.AreEqual(blue, h2, 1e-3f);
            Assert.AreEqual(s, s2, 1e-3f);
            Assert.AreEqual(v, v2, 1e-3f);
            Assert.Less(GoreRules.Recolour(goo, null, 0.3f).maxColorComponent, goo.maxColorComponent * 0.31f);
        }

        [Test]
        public void PastTheLimit_TheOldestCorpseGoes()
        {
            var budget = new CorpseBudget<string>(3);
            Assert.IsNull(budget.Add("a"));
            Assert.IsNull(budget.Add("b"));
            Assert.IsNull(budget.Add("c"));
            Assert.AreEqual("a", budget.Add("d"));
            budget.Remove("c");
            Assert.IsNull(budget.Add("e"));
            Assert.AreEqual("b", budget.Add("f"));
            Assert.AreEqual(3, budget.Count);
        }
    }
}
