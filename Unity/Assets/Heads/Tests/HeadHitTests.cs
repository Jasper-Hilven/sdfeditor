using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfHeads.Tests
{
    /// <summary>The head's hit shape (ticket 45): an ellipsoid tight to the visible head, the shot test and the capsule resize.</summary>
    public sealed class HeadHitTests
    {
        // A head ellipsoid 0.30 across (x), 0.28 up, 0.32 deep (z), its centre 0.03 forward and 0.05 below the socket
        static readonly HeadShape Shape = new HeadShape(new Vector3(0, -0.05f, 0.03f), new Vector3(0.30f, 0.28f, 0.32f));
        static readonly Vector3 Centre = new Vector3(0, SdfRig.SoldierBody.HeadTopHeight - 0.05f, 0.03f);

        [Test]
        public void AShotJustInsideTheHeadHitsAndJustOutsideMisses()
        {
            // From the front, along -Z, at the head's height: the ellipsoid's front is 0.32 ahead of its centre
            var ahead = Centre + Vector3.forward * 0.32f;
            var from = Centre + Vector3.forward * 5;
            Assert.IsTrue(HeadHit.Hit(Shape, Vector3.zero, 0, from + Vector3.right * 0.299f, Vector3.back, 0, out var t, out var normal));
            Assert.Less(t, 5f);
            Assert.Greater(normal.x, 0.9f, "a graze at the edge faces sideways");
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, from + Vector3.right * 0.301f, Vector3.back, 0, out _, out _));
            // Up and down
            Assert.IsTrue(HeadHit.Hit(Shape, Vector3.zero, 0, from + Vector3.up * 0.279f, Vector3.back, 0, out _, out _));
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, from + Vector3.up * 0.281f, Vector3.back, 0, out _, out _));
            // The tip straight on is at 0.32 deep
            Assert.IsTrue(HeadHit.Hit(Shape, Vector3.zero, 0, from, Vector3.back, 0, out t, out normal));
            Assert.AreEqual(5 - 0.32f, t, 1e-3f);
            Assert.Greater(normal.z, 0.99f, "straight on, the normal faces the shooter");
            Assert.AreEqual(ahead.z, (from + Vector3.back * t).z, 1e-3f);
        }

        [Test]
        public void TheShapeTurnsWithThePlayer()
        {
            // Turned a quarter turn the deep axis (0.32) lies along x: a shot from the side 0.31 off-centre in z hits, at 0.33 it misses
            var turned = Quaternion.Euler(0, 90, 0);
            var centre = Vector3.up * (SdfRig.SoldierBody.HeadTopHeight - 0.05f) + turned * new Vector3(0, 0, 0.03f);
            Assert.IsTrue(HeadHit.Hit(Shape, Vector3.zero, 90, centre + Vector3.right * 5 + Vector3.forward * 0.299f, Vector3.left, 0, out _, out _));
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 90, centre + Vector3.right * 5 + Vector3.forward * 0.31f, Vector3.left, 0, out _, out _));
            // A shot 0.31 up the x axis from the front misses unturned (x radius 0.30) and hits turned 90 degrees (the 0.32 now lies along x)... along z at 90 degrees the width is 0.32
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, Centre + Vector3.back * 0 + Vector3.forward * 5 + Vector3.right * 0.31f, Vector3.back, 0, out _, out _));
        }

        [Test]
        public void ABallSweptAlongTheShotWidensTheShape()
        {
            var from = Centre + Vector3.forward * 5 + Vector3.right * 0.33f;
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, from, Vector3.back, 0, out _, out _));
            Assert.IsTrue(HeadHit.Hit(Shape, Vector3.zero, 0, from, Vector3.back, 0.05f, out _, out _));
        }

        [Test]
        public void AShotStartingInsideOrPointingAwayMisses()
        {
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, Centre, Vector3.forward, 0, out _, out _));
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, Centre + Vector3.forward * 5, Vector3.forward, 0, out _, out _));
        }

        [Test]
        public void ThePlayerStandingElsewhereMovesTheShape()
        {
            var feet = new Vector3(10, 2, -4);
            var from = feet + Centre + Vector3.up * 0 + Vector3.forward * 5;
            Assert.IsTrue(HeadHit.Hit(Shape, feet, 0, from, Vector3.back, 0, out _, out _));
            Assert.IsFalse(HeadHit.Hit(Shape, Vector3.zero, 0, from, Vector3.back, 0, out _, out _));
        }

        [Test]
        public void TheCapsuleIsResizedToTheShapeThroughTheHeadsFrame()
        {
            // A bone 1.5 m up with the capsule on a child; the head's frame sits 0.25 m above the bone
            var root = new GameObject("soldier").transform;
            var bone = new GameObject("Head").transform;
            bone.SetParent(root, false);
            bone.localPosition = new Vector3(0, 1.5f, 0);
            var capsuleObject = new GameObject("capsule");
            capsuleObject.transform.SetParent(bone, false);
            var capsule = capsuleObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = 0.13f;
            capsule.height = 0.3f;
            var frame = new GameObject("socket").transform;
            frame.SetParent(bone, false);
            frame.localPosition = new Vector3(0, 0.25f, 0);

            var shape = new HeadShape(new Vector3(0, -0.04f, 0), 0.2f, 0.22f);
            HeadHit.FitCapsule(capsule, shape, frame);

            Assert.AreEqual(0.2f, capsule.radius, 1e-5f);
            Assert.AreEqual(0.44f, capsule.height, 1e-5f);
            // The capsule's world extent is the shape's: from 1.5 + 0.25 - 0.04 - 0.22 up to + 0.22
            var middle = capsuleObject.transform.TransformPoint(capsule.center).y;
            Assert.AreEqual(1.5f + 0.25f - 0.04f, middle, 1e-4f);
            Object.DestroyImmediate(root.gameObject);
        }

        [Test]
        public void TheCapsuleFollowsAnUpsideDownBone()
        {
            var bone = new GameObject("Head");
            bone.transform.rotation = Quaternion.Euler(180, 0, 0);
            var capsule = bone.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            var frame = new GameObject("frame").transform;
            HeadHit.FitCapsule(capsule, new HeadShape(Vector3.zero, 0.2f, 0.22f), frame);
            Assert.AreEqual(0.44f, capsule.height, 1e-5f);
            Assert.AreEqual(0, bone.transform.TransformPoint(capsule.center).y, 1e-4f);
            Object.DestroyImmediate(bone);
            Object.DestroyImmediate(frame.gameObject);
        }
    }
}
