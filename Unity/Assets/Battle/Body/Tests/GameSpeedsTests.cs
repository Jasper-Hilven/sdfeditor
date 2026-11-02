using NUnit.Framework;
using UnityEngine;

namespace SdfBody.Tests
{
    /// <summary>The body at the game's speeds (FirstPersonController: walk 4.4 m/s; the flag carrier 0.9, Frenzy 1.6): the velocity becomes the tree position and the playback matches the feet to the ground.</summary>
    public class GameSpeedsTests
    {
        const float Walk = 4.4f;

        static LocomotionTuning Tuning() => ScriptableObject.CreateInstance<LocomotionTuning>();

        [Test]
        public void TheWalkSpeedIsOneOnTheRunRing()
        {
            var t = Tuning();
            try
            {
                Assert.AreEqual(Walk, t.RunSpeed, 1e-5f);
                var p = LocomotionMath.TreePosition(new Vector2(0, Walk), t.RunSpeed, t.MaxTreeRadius);
                Assert.AreEqual(0f, p.x, 1e-5f);
                Assert.AreEqual(1f, p.y, 1e-5f);
            }
            finally { Object.DestroyImmediate(t); }
        }

        [TestCase(0.9f, 0.9f)]
        [TestCase(1f, 1f)]
        [TestCase(1.6f, 1.6f)]
        [TestCase(3f, 1.65f)]
        public void TheSpeedMultipliersMoveTheTreePositionAndItIsClamped(float multiplier, float expectedY)
        {
            var t = Tuning();
            try
            {
                var p = LocomotionMath.TreePosition(new Vector2(0, Walk * multiplier), t.RunSpeed, t.MaxTreeRadius);
                Assert.AreEqual(expectedY, p.y, 1e-4f);
            }
            finally { Object.DestroyImmediate(t); }
        }

        [Test]
        public void AWorldVelocityBecomesTheBodyFrameVelocity()
        {
            // Facing east (yaw 90): walking east is forward, walking south is to the right
            var rot = Quaternion.Euler(0, 90, 0);
            var forward = LocomotionMath.BodyVelocity(new Vector3(Walk, 0, 0), rot);
            var right = LocomotionMath.BodyVelocity(new Vector3(0, 0, -Walk), rot);
            var back = LocomotionMath.BodyVelocity(new Vector3(-Walk, 0, 0), rot);
            Assert.AreEqual(0f, forward.x, 1e-4f); Assert.AreEqual(Walk, forward.y, 1e-4f);
            Assert.AreEqual(Walk, right.x, 1e-4f); Assert.AreEqual(0f, right.y, 1e-4f);
            Assert.AreEqual(-Walk, back.y, 1e-4f);
            // The jump and the fall do not count: only the flat velocity
            Assert.AreEqual(Walk, LocomotionMath.BodyVelocity(new Vector3(Walk, 9f, 0), rot).y, 1e-4f);
        }

        /// <summary>The ground the feet cover (the tree's natural ground speed at the playback speed) equals the body's speed: no foot sliding.</summary>
        [TestCase(LocomotionClips.Stance.Rifle, 0f, 1f, 0.9f)]
        [TestCase(LocomotionClips.Stance.Rifle, 0f, 1f, 1f)]
        [TestCase(LocomotionClips.Stance.Rifle, 0f, 1f, 1.6f)]
        [TestCase(LocomotionClips.Stance.Rifle, 1f, 0f, 1f)]
        [TestCase(LocomotionClips.Stance.Rifle, 0f, -1f, 1f)]
        [TestCase(LocomotionClips.Stance.Pistol, 0f, 1f, 1f)]
        [TestCase(LocomotionClips.Stance.Heavy, 0f, 1f, 1.6f)]
        public void TheFeetMatchTheGroundAtTheGameSpeeds(LocomotionClips.Stance stance, float x, float y, float multiplier)
        {
            var t = Tuning();
            try
            {
                var velocity = new Vector2(x, y).normalized * Walk * multiplier;
                var p = LocomotionMath.TreePosition(velocity, t.RunSpeed, t.MaxTreeRadius);
                var walk = t.WalkRing(stance);
                var run = t.RunRing(stance);
                var sprint = t.SprintRing(stance);
                var playback = LocomotionMath.Playback(p, t.RunSpeed, walk, run, sprint, t.MinPlayback, t.MaxPlayback);
                var feet = LocomotionMath.TreeGroundSpeed(p, walk, run, sprint) * playback;
                Assert.AreEqual(velocity.magnitude, feet, velocity.magnitude * 0.05f, $"playback {playback}");
                Assert.That(playback, Is.InRange(t.MinPlayback, t.MaxPlayback));
            }
            finally { Object.DestroyImmediate(t); }
        }

        [Test]
        public void StandingStillPlaysTheIdleAtItsOwnPace()
        {
            var t = Tuning();
            try
            {
                Assert.AreEqual(1f, LocomotionMath.Playback(Vector2.zero, t.RunSpeed, t.WalkRing(), t.RunRing(), t.SprintRing(), t.MinPlayback, t.MaxPlayback), 1e-5f);
            }
            finally { Object.DestroyImmediate(t); }
        }

        [Test]
        public void EveryTreeClipHasAMeasuredSpeedAndAScale()
        {
            var t = Tuning();
            try
            {
                foreach (var tree in LocomotionClips.Trees)
                    foreach (var c in tree)
                    {
                        if (c.Ring == LocomotionRing.Centre) { Assert.AreEqual(1f, t.TimeScaleOf(c)); continue; }
                        Assert.Greater(t.SpeedOf(c.Name), 0f, c.Name + " has no measured speed");
                        Assert.That(t.TimeScaleOf(c), Is.InRange(t.MinPlayback, t.MaxPlayback), c.Name);
                        // Each clip's feet cover its ring's speed in the tree (unless the clamp bites)
                        var tree2 = t.TreeSpeedOf(c);
                        var ring = t.RadiusOf(c.Ring) * t.RunSpeed;
                        if (t.TimeScaleOf(c) > t.MinPlayback + 1e-4f && t.TimeScaleOf(c) < t.MaxPlayback - 1e-4f)
                            Assert.AreEqual(ring, tree2, 1e-3f, c.Name);
                    }
            }
            finally { Object.DestroyImmediate(t); }
        }
    }
}
