using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfBody.Tests
{
    public class LocomotionMathTests
    {
        const float Run = 4.8f;
        const float Eps = 1e-4f;

        static void V(Vector2 e, Vector2 a, float eps = Eps)
        {
            Assert.AreEqual(e.x, a.x, eps, "x of " + a);
            Assert.AreEqual(e.y, a.y, eps, "y of " + a);
        }

        static LocomotionSpeedRing Ring(float radius, float[] angles, float[] speeds)
            => new LocomotionSpeedRing { Radius = radius, Angles = angles, Speeds = speeds };

        static LocomotionSpeedRing Walk() => Ring(0.5f, new[] { 90f, -90f, 180f, 0f }, new[] { 2.4f, 2.4f, 2.4f, 2.4f });
        static LocomotionSpeedRing RunR() => Ring(1f, new[] { 90f, -90f, 180f, 0f }, new[] { 4.8f, 4.8f, 4.8f, 4.8f });

        // Tree position
        [Test] public void TreePosition_WalkForward() => V(new Vector2(0, 0.5f), LocomotionMath.TreePosition(new Vector2(0, 2.4f), Run, 1.35f));
        [Test] public void TreePosition_StrafeRight() => V(new Vector2(0.5f, 0), LocomotionMath.TreePosition(new Vector2(2.4f, 0), Run, 1.35f));
        [Test] public void TreePosition_Sprint() => V(new Vector2(0, 1.3f), LocomotionMath.TreePosition(new Vector2(0, 6.24f), Run, 1.35f));
        [Test] public void TreePosition_Capped() => Assert.AreEqual(1.35f, LocomotionMath.TreePosition(new Vector2(100, 100), Run, 1.35f).magnitude, Eps);
        [Test] public void TreePosition_BadInputGivesZero()
        {
            V(Vector2.zero, LocomotionMath.TreePosition(new Vector2(float.NaN, 1), Run, 1.35f));
            V(Vector2.zero, LocomotionMath.TreePosition(new Vector2(float.PositiveInfinity, 1), Run, 1.35f));
            V(Vector2.zero, LocomotionMath.TreePosition(new Vector2(1, 1), 0f, 1.35f));
        }

        // Body frame
        [TestCase(0f), TestCase(37f), TestCase(90f), TestCase(-135f), TestCase(180f)]
        public void BodyVelocity_IsTheFlatVelocityInTheBodysFrame(float yaw)
        {
            var w = new Vector3(3f, 7f, -2f);
            var rot = Quaternion.Euler(0, yaw, 0);
            var expected = new Vector2(Vector3.Dot(new Vector3(3f, 0, -2f), rot * Vector3.right), Vector3.Dot(new Vector3(3f, 0, -2f), rot * Vector3.forward));
            V(expected, LocomotionMath.BodyVelocity(w, rot), 1e-3f);
        }

        // Damp
        [Test] public void DampFraction_TimeConstants()
        {
            Assert.AreEqual(0.632f, LocomotionMath.DampFraction(0.1f, 0.1f), 1e-3f);
            Assert.AreEqual(0.950f, LocomotionMath.DampFraction(0.1f, 0.3f), 1e-3f);
        }
        [Test] public void DampFraction_IndependentOfFrameSplit()
        {
            float v = 0f;
            for (int i = 0; i < 10; i++) v = LocomotionMath.Damp(v, 1f, 0.2f, 0.01f);
            Assert.AreEqual(LocomotionMath.Damp(0f, 1f, 0.2f, 0.1f), v, 1e-5f);
        }
        [Test] public void DampFraction_EdgeCases()
        {
            Assert.AreEqual(1f, LocomotionMath.DampFraction(0f, 0.016f));
            Assert.AreEqual(0f, LocomotionMath.DampFraction(0.1f, 0f));
            Assert.AreEqual(0f, LocomotionMath.DampFraction(0.1f, float.NaN));
        }
        [Test] public void Damp_NeverOvershoots()
        {
            float v = 0f;
            for (int i = 0; i < 200; i++)
            {
                v = LocomotionMath.Damp(v, 1f, 0.1f, 0.05f);
                Assert.LessOrEqual(v, 1f);
            }
            var v2 = LocomotionMath.Damp(Vector2.zero, Vector2.one, 0.1f, 1f);
            Assert.LessOrEqual(v2.x, 1f);
        }

        // AngularLerp
        static readonly float[] Ang = { 0f, 90f, 180f, -90f };
        [Test] public void AngularLerp_ExactAtEntries()
        {
            var vals = new[] { 1f, 2f, 3f, 4f };
            for (int i = 0; i < 4; i++) Assert.AreEqual(vals[i], LocomotionMath.AngularLerp(Ang[i], Ang, vals), Eps);
        }
        [Test] public void AngularLerp_Midway() => Assert.AreEqual(1.5f, LocomotionMath.AngularLerp(45f, Ang, new[] { 1f, 2f, 3f, 4f }), Eps);
        [Test] public void AngularLerp_WrapsBetween170And170()
        {
            var a = new[] { 170f, -170f, 0f };
            var v = new[] { 10f, 20f, 0f };
            Assert.AreEqual(15f, LocomotionMath.AngularLerp(180f, a, v), Eps);
            Assert.AreEqual(15f, LocomotionMath.AngularLerp(-180f, a, v), Eps);
        }
        [Test] public void AngularLerp_UnsortedAndSmall()
        {
            Assert.AreEqual(0f, LocomotionMath.AngularLerp(10f, new float[0], new float[0]));
            Assert.AreEqual(7f, LocomotionMath.AngularLerp(10f, new[] { 50f }, new[] { 7f }));
            Assert.AreEqual(1.5f, LocomotionMath.AngularLerp(45f, new[] { 90f, 0f }, new[] { 2f, 1f }), Eps);
        }

        // Ground speed
        [Test] public void GroundSpeed_AtRings()
        {
            Assert.AreEqual(2.4f, LocomotionMath.TreeGroundSpeed(new Vector2(0, 0.5f), Walk(), RunR()), Eps);
            Assert.AreEqual(1.2f, LocomotionMath.TreeGroundSpeed(new Vector2(0, 0.25f), Walk(), RunR()), Eps);
            Assert.AreEqual(4.8f, LocomotionMath.TreeGroundSpeed(new Vector2(0, 1f), Walk(), RunR()), Eps);
            Assert.AreEqual(3.6f, LocomotionMath.TreeGroundSpeed(new Vector2(0, 0.75f), Walk(), RunR()), Eps);
            Assert.AreEqual(4.8f, LocomotionMath.TreeGroundSpeed(new Vector2(0, 1.3f), Walk(), RunR()), Eps);
        }

        // Playback
        [Test] public void Playback_OneAtWalkRingForward()
            => Assert.AreEqual(1f, LocomotionMath.Playback(new Vector2(0, 0.5f), Run, Walk(), RunR(), 0.1f, 5f), Eps);
        [Test] public void Playback_ConstantInsideWalkRing_FromHalfTheRing_AndOneAtRest()
        {
            // The clips' feet cover 1.2 m/s at the walk ring where the body goes 2.4: the ratio is 2 from half the ring on, 1 at rest.
            var slow = Ring(0.5f, new[] { 90f, -90f, 180f, 0f }, new[] { 1.2f, 1.2f, 1.2f, 1.2f });
            Assert.AreEqual(2f, LocomotionMath.Playback(new Vector2(0, 0.25f), Run, slow, RunR(), 0.1f, 5f), Eps);
            Assert.AreEqual(2f, LocomotionMath.Playback(new Vector2(0, 0.4f), Run, slow, RunR(), 0.1f, 5f), Eps);
            Assert.AreEqual(2f, LocomotionMath.Playback(new Vector2(0, 0.5f), Run, slow, RunR(), 0.1f, 5f), Eps);
            Assert.AreEqual(1.5f, LocomotionMath.Playback(new Vector2(0, 0.125f), Run, slow, RunR(), 0.1f, 5f), Eps);
            Assert.AreEqual(1f, LocomotionMath.Playback(Vector2.zero, Run, slow, RunR(), 0.1f, 5f), Eps);
        }
        [Test] public void Playback_Clamped()
        {
            var slow = Ring(0.5f, new[] { 90f }, new[] { 10f });
            Assert.AreEqual(0.6f, LocomotionMath.Playback(new Vector2(0, 0.5f), Run, slow, slow, 0.6f, 1.8f), Eps);
            var fast = Ring(0.5f, new[] { 90f }, new[] { 0.5f });
            Assert.AreEqual(1.8f, LocomotionMath.Playback(new Vector2(0, 0.5f), Run, fast, fast, 0.6f, 1.8f), Eps);
        }
        [Test] public void Playback_ZeroNaturalGivesOne()
        {
            var z = Ring(0.5f, new[] { 90f }, new[] { 0f });
            Assert.AreEqual(1f, LocomotionMath.Playback(new Vector2(0, 0.5f), Run, z, z, 0.6f, 1.8f), Eps);
            Assert.AreEqual(1f, LocomotionMath.Playback(new Vector2(0, 1f), Run, z, z, 0.6f, 1.8f), Eps);
        }
        [Test] public void Playback_SameRingTwice()
        {
            var c = Ring(0.5f, new[] { 90f, -90f, 180f, 0f }, new[] { 1.4f, 1.4f, 1.4f, 1.4f });
            float p = LocomotionMath.Playback(new Vector2(0, 1f), Run, c, c, 0.6f, 1.8f);
            Assert.IsFalse(float.IsNaN(p) || float.IsInfinity(p));
            Assert.AreEqual(1.8f, p, Eps);
        }

        // Clips
        [Test] public void Clips_Counts()
        {
            Assert.AreEqual(18, LocomotionClips.Stand.Length);
            Assert.AreEqual(18, LocomotionClips.Pistol.Length);
            Assert.AreEqual(18, LocomotionClips.Heavy.Length);
            Assert.AreEqual(8, LocomotionClips.InRing(LocomotionClips.Stand, LocomotionRing.Walk).Count());
            Assert.AreEqual(8, LocomotionClips.InRing(LocomotionClips.Stand, LocomotionRing.Run).Count());
            Assert.AreEqual(1, LocomotionClips.InRing(LocomotionClips.Stand, LocomotionRing.Sprint).Count());
        }
        [Test] public void Clips_NamesUniqueAndConventional()
        {
            var all = LocomotionClips.Stand.ToList();
            Assert.AreEqual(all.Count, all.Select(c => c.Name).Distinct().Count());
            foreach (var c in all) StringAssert.IsMatch("^(soldier|body)_[a-z0-9]+_[a-z0-9_]+$", c.Name);
        }
        [Test] public void Clips_DirectionsAreUnit()
        {
            foreach (var c in LocomotionClips.Trees.SelectMany(t => t))
            {
                if (c.Ring == LocomotionRing.Centre) Assert.AreEqual(0f, c.Direction.magnitude);
                else Assert.AreEqual(1f, c.Direction.magnitude, 1e-5f, c.Name);
            }
        }
        [Test] public void Clips_PositionUsesRingRadius()
        {
            var f = LocomotionClips.Stand.First(c => c.Name == "soldier_loco_sprint_f");
            V(new Vector2(0, 1.65f), f.Position(0.5f, 1f, 1.65f));
            V(Vector2.zero, LocomotionClips.Stand[0].Position(0.5f, 1f, 1.65f));
        }

        // Tuning
        [Test] public void Tuning_Defaults()
        {
            var t = ScriptableObject.CreateInstance<LocomotionTuning>();
            try
            {
                Assert.AreEqual(4.4f, t.RunSpeed, "the game's walk speed (FirstPersonController.walkSpeed)");
                Assert.AreEqual(0.5f, t.WalkRadius);
                Assert.AreEqual(1f, t.RunRadius);
                Assert.AreEqual(1.65f, t.SprintRadius);
                Assert.AreEqual(2.2f, t.WalkSpeed, Eps);
                Assert.AreEqual(1.65f, t.MaxTreeRadius);
                Assert.AreEqual(8, t.WalkRing().Angles.Length);
                Assert.AreEqual(8, t.WalkRing().Speeds.Length);
                Assert.AreEqual(8, t.RunRing().Angles.Length);
                Assert.AreEqual(8, t.RunRing(LocomotionClips.Stance.Pistol).Angles.Length);
                Assert.AreEqual(0f, t.SpeedOf("nope"));
                var names = LocomotionTuning.DefaultClipSpeeds().Select(c => c.Clip).ToList();
                foreach (var c in LocomotionClips.Trees.SelectMany(t => t).Where(c => c.Ring != LocomotionRing.Centre))
                    CollectionAssert.Contains(names, c.Name);
                Assert.AreEqual(17 + 5, names.Count);
            }
            finally { Object.DestroyImmediate(t); }
        }

        // DirectionFrom
        [Test] public void DirectionFrom_Frame()
        {
            V(new Vector2(0, 1), LocomotionMath.DirectionFrom(new Vector3(0, 0, 5), Vector3.zero, Quaternion.identity));
            V(new Vector2(1, 0), LocomotionMath.DirectionFrom(new Vector3(0, 0, -5), Vector3.zero, Quaternion.Euler(0, 90, 0)));
            V(new Vector2(0, -1), LocomotionMath.DirectionFrom(new Vector3(0, 0, -5), Vector3.zero, Quaternion.identity));
            V(new Vector2(-1, 0), LocomotionMath.DirectionFrom(new Vector3(-2, 3, 0), Vector3.zero, Quaternion.identity));
            V(new Vector2(0, 1), LocomotionMath.DirectionFrom(new Vector3(11, 0, 5), new Vector3(10, 0, 5) , Quaternion.Euler(0, 90, 0)));
            V(Vector2.zero, LocomotionMath.DirectionFrom(new Vector3(0, 4, 0), Vector3.zero, Quaternion.identity));
        }
    }
}
