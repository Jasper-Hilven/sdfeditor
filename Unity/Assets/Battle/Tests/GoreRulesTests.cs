using System.Linq;
using NUnit.Framework;
using SdfHeads;
using UnityEngine;

namespace SdfBattle.Tests
{
    /// <summary>The gore's numbers (ticket 51): only the head bursts, in its own blood colour; the body never does; how many corpses stay.</summary>
    public sealed class GoreRulesTests
    {
        static float Distance(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        [Test]
        public void OnlyAKill_WithGoreOn_BurstsTheHead()
        {
            Assert.IsTrue(GoreRules.Bursts(GoreRules.Splatty, DeathCause.Killed));
            Assert.IsTrue(GoreRules.Bursts(GoreRules.ExtraSplatty, DeathCause.Killed));
            Assert.IsTrue(GoreRules.Bursts(GoreRules.UltraGore, DeathCause.Killed));
            Assert.IsFalse(GoreRules.Bursts(GoreRules.Off, DeathCause.Killed));
            Assert.IsFalse(GoreRules.Bursts(GoreRules.Splatty, DeathCause.Fell));
            Assert.AreEqual(0, GoreRules.BloodGobs(GoreRules.Off, WeaponIds.Pistol));
        }

        [Test]
        public void GoreOff_PopsTheHeadInAPuff_NoPieces()
        {
            Assert.IsTrue(GoreRules.Puffs(GoreRules.Off, DeathCause.Killed));
            Assert.IsFalse(GoreRules.Puffs(GoreRules.Splatty, DeathCause.Killed));
            Assert.IsFalse(GoreRules.Puffs(GoreRules.Off, DeathCause.Fell));
        }

        [Test]
        public void NoLevelBurstsTheBody_ButEveryKillRagdollsIt()
        {
            for (var level = GoreRules.Off; level <= GoreRules.UltraGore; level++)
                Assert.IsFalse(GoreRules.BodyBursts(level), $"level {level}");
            Assert.IsTrue(GoreRules.Ragdolls(DeathCause.Killed));
            Assert.IsFalse(GoreRules.Ragdolls(DeathCause.Fell));
        }

        [Test]
        public void ThePieces_FlyAwayFromTheHit_AndUp()
        {
            // Shot from the west: the pieces go east, whichever side of the head they were on
            var away = Vector3.right;
            foreach (var outward in new[] { Vector3.left, Vector3.forward, Vector3.back })
            {
                var velocity = GoreRules.Launch(GibKind.Piece, outward, away, false, 1, Vector3.zero, 0.5f);
                Assert.Greater(Vector3.Dot(velocity, away), -0.01f, $"{outward}");
                Assert.Greater(velocity.y, 0);
                Assert.Greater(velocity.magnitude, 3);
            }
        }

        [Test]
        public void AHeadshot_PopsThePiecesHigher_AndACannonThrowsHarder()
        {
            var plain = GoreRules.Launch(GibKind.Piece, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f);
            var popped = GoreRules.Launch(GibKind.Piece, Vector3.left, Vector3.right, true, 1, Vector3.zero, 0.5f);
            Assert.Greater(popped.y, plain.y * 1.3f);
            var cannon = GoreRules.Launch(GibKind.Piece, Vector3.left, Vector3.right, false, GoreRules.Strength("cannon"), Vector3.zero, 0.5f);
            var pistol = GoreRules.Launch(GibKind.Piece, Vector3.left, Vector3.right, false, GoreRules.Strength(WeaponIds.Pistol), Vector3.zero, 0.5f);
            Assert.Greater(cannon.magnitude, pistol.magnitude * 1.4f);
        }

        [Test]
        public void UltraGore_TriplesTheBlood_AndFlingsEveryPiece()
        {
            Assert.AreEqual(GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol) * 3, GoreRules.BloodGobs(GoreRules.UltraGore, WeaponIds.Pistol));
            Assert.IsFalse(GoreRules.Flings(GoreRules.ExtraSplatty));
            Assert.IsTrue(GoreRules.Flings(GoreRules.UltraGore));
            var piece = GoreRules.Launch(GibKind.Piece, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f);
            var flung = GoreRules.Launch(GibKind.Piece, Vector3.left, Vector3.right, false, 1, Vector3.zero, 0.5f, true);
            Assert.Greater(flung.magnitude, piece.magnitude * 1.4f);
        }

        [Test]
        public void TheBlood_IsLots_LessWhenCooked()
        {
            Assert.GreaterOrEqual(GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol), 20);
            Assert.Greater(GoreRules.BloodGobs(GoreRules.ExtraSplatty, WeaponIds.Pistol), GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol));
            Assert.Less(GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Flamethrower), GoreRules.BloodGobs(GoreRules.Splatty, WeaponIds.Pistol));
        }

        // ---------------------------------------------------------------- the blood's colour

        [Test]
        public void EveryHeadHasABloodColour_FromTheTable()
        {
            foreach (var head in MonsterHeads.All)
            {
                var blood = GoreRules.BloodOf(head.Id);
                Assert.AreEqual(head.Blood, blood, head.Id);
                Assert.AreEqual(1f, blood.a, head.Id);
            }
        }

        [Test]
        public void TheBloodColours_AreDistinctFromEachOther_FromHumanRed_AndFromBothTeams()
        {
            var humanRed = new Color(0.8f, 0.05f, 0.05f);
            var heads = MonsterHeads.All;
            for (var i = 0; i < heads.Count; i++)
            {
                var a = GoreRules.BloodOf(heads[i].Id);
                Assert.Greater(Distance(a, humanRed), 0.4f, $"{heads[i].Id} vs human red");
                Assert.Greater(Distance(a, GoreRules.TeamRed), 0.3f, $"{heads[i].Id} vs the red team");
                Assert.Greater(Distance(a, GoreRules.TeamBlue), 0.3f, $"{heads[i].Id} vs the blue team");
                for (var j = i + 1; j < heads.Count; j++)
                    Assert.GreaterOrEqual(Distance(a, GoreRules.BloodOf(heads[j].Id)), 0.2f, $"{heads[i].Id} vs {heads[j].Id}");
            }
        }

        [Test]
        public void AnUnknownHead_StillBleedsAColour_NotRed()
        {
            var blood = GoreRules.BloodOf("nobody");
            Assert.Greater(Distance(blood, GoreRules.TeamRed), 0.3f);
        }

        [Test]
        public void TheGoo_TakesTheBloodsColour_KeepingItsShading()
        {
            var blood = MonsterHeads.ById("yeti").Value.Blood;
            var bright = GoreRules.Tint(new Color(0.9f, 0.2f, 0.6f), blood);
            var dark = GoreRules.Tint(new Color(0.3f, 0.06f, 0.2f), blood);
            Color.RGBToHSV(blood, out var hue, out var sat, out _);
            foreach (var shade in new[] { bright, dark })
            {
                Color.RGBToHSV(shade, out var h, out var s, out _);
                Assert.AreEqual(hue, h, 0.01f);
                Assert.AreEqual(sat, s, 0.05f);
            }
            Assert.Greater(bright.maxColorComponent, dark.maxColorComponent);
            Assert.Less(GoreRules.Tint(new Color(0.9f, 0.2f, 0.6f), blood, 0.3f).maxColorComponent, bright.maxColorComponent * 0.31f);
            Assert.AreEqual(0.5f, GoreRules.Tint(new Color(1, 1, 1, 0.5f), blood).a, 1e-5f);
        }

        // ---------------------------------------------------------------- the ragdoll's numbers

        [Test]
        public void TheRagdoll_SimulatesThreeSecondsAtMost_AndSettlesEarlier()
        {
            Assert.AreEqual(3f, GoreRules.RagdollSeconds);
            Assert.Less(GoreRules.RagdollSettleSeconds, GoreRules.RagdollSeconds);
            Assert.Greater(GoreRules.RagdollPush, 0);
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
