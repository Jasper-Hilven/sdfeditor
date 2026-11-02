using NUnit.Framework;
using UnityEngine;

namespace SdfBody.Tests
{
    /// <summary>Ticket 06's pure rules: the death picked from the shot's direction, the reload stretch, the automatic-fire rule.</summary>
    public class CombatRulesTests
    {
        [Test]
        public void A_shot_from_straight_ahead_is_0_1_and_from_the_right_1_0()
        {
            // The body faces +z; the shooter 10 m in front, then 10 m to the right (the ticket's rule).
            var rot = Quaternion.Euler(0f, 37f, 0f);
            var at = new Vector3(3f, 0f, -2f);
            var front = LocomotionMath.DirectionFrom(at + rot * Vector3.forward * 10f, at, rot);
            var right = LocomotionMath.DirectionFrom(at + rot * Vector3.right * 10f, at, rot);
            Assert.AreEqual(0f, front.x, 1e-4f); Assert.AreEqual(1f, front.y, 1e-4f);
            Assert.AreEqual(1f, right.x, 1e-4f); Assert.AreEqual(0f, right.y, 1e-4f);
            Assert.AreEqual(0f, CombatRules.HitBlend(front), 1e-4f);
            Assert.AreEqual(1f, CombatRules.HitBlend(right), 1e-4f);
        }

        [Test]
        public void The_reload_is_stretched_to_the_weapon_and_clamped()
        {
            Assert.AreEqual(1f, CombatRules.ReloadSpeed(3.3f, 3.3f, 0.25f, 4f), 1e-5f);
            Assert.AreEqual(1.5f, CombatRules.ReloadSpeed(3.3f, 2.2f, 0.25f, 4f), 1e-5f);
            Assert.AreEqual(0.66f, CombatRules.ReloadSpeed(3.3f, 5f, 0.25f, 4f), 1e-5f);
            Assert.AreEqual(4f, CombatRules.ReloadSpeed(3.3f, 0.1f, 0.25f, 4f), 1e-5f);
            Assert.AreEqual(1f, CombatRules.ReloadSpeed(3.3f, 0f, 0.25f, 4f), "no time: as the clip");
            Assert.AreEqual(1f, CombatRules.ReloadSpeed(3.3f, float.NaN, 0.25f, 4f));
        }

        [Test]
        public void Shots_close_together_are_automatic_until_they_stop()
        {
            var c = new FireCadence();
            Assert.IsFalse(c.Shot(0f, 0.16f), "the first shot is single");
            Assert.IsTrue(c.Shot(1f / 12f, 0.16f), "12 shots a second: automatic");
            Assert.IsTrue(c.Shot(2f / 12f, 0.16f));
            Assert.IsFalse(c.Tick(0.3f, 0.22f), "still within the hold");
            Assert.IsTrue(c.Tick(0.5f, 0.22f), "stopped: ends once");
            Assert.IsFalse(c.Automatic);
            Assert.IsFalse(c.Tick(0.6f, 0.22f));
            Assert.IsFalse(c.Shot(2f, 0.16f), "a pistol at 2 shots a second stays single");
            Assert.IsFalse(c.Shot(2.5f, 0.16f));
            Assert.IsFalse(c.Shot(2.75f, 0.16f), "4 shots a second: single, restarted");
            c.Shot(2.8f, 0.16f);
            c.Reset();
            Assert.IsFalse(c.Automatic);
            Assert.IsFalse(c.Shot(2.85f, 0.16f), "after a reset the next shot is single");
        }

        [Test]
        public void The_hit_blend_is_the_side()
        {
            Assert.AreEqual(1f, CombatRules.HitBlend(new Vector2(1f, 0f)));
            Assert.AreEqual(-1f, CombatRules.HitBlend(new Vector2(-3f, 0f)));
            Assert.AreEqual(0f, CombatRules.HitBlend(new Vector2(0f, 1f)));
            Assert.AreEqual(0f, CombatRules.HitBlend(new Vector2(float.NaN, 1f)));
        }

        [Test]
        public void The_tuning_defaults_are_sane()
        {
            var t = ScriptableObject.CreateInstance<CombatTuning>();
            try
            {
                Assert.Less(1f / 12f, t.BurstInterval, "12 shots a second is automatic");
                Assert.Greater(0.25f, t.BurstInterval, "4 shots a second (a pistol) is not");
                Assert.Greater(t.BurstHoldSeconds, t.BurstInterval);
            }
            finally { Object.DestroyImmediate(t); }
        }
    }
}
