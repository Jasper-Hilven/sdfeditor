using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SdfHeads.Tests
{
    /// <summary>The head's hit shape and the fairness rule (ticket 45): every head's radii within 15 % of one reference, whatever its size.</summary>
    public sealed class HeadHitTests
    {
        [Test]
        public void TheBandIsFifteenPercentEachWayOfTheReference()
        {
            var band = HeadHit.Band(0.2f);
            Assert.AreEqual(0.17f, band.x, 1e-5f);
            Assert.AreEqual(0.23f, band.y, 1e-5f);
        }

        [Test]
        public void AHeadOfTheReferenceSizeKeepsItsSizeShare()
        {
            // A bounds that gives exactly the reference radii: size = 2 r / share
            var size = new Vector3(HeadHit.ReferenceAcross * 2 / HeadHit.BoundsShare, HeadHit.ReferenceUp * 2 / HeadHit.BoundsShare, HeadHit.ReferenceAcross * 2 / HeadHit.BoundsShare);
            var shape = HeadHit.Fit(new Vector3(0, 0.1f, 0), size);
            Assert.AreEqual(HeadHit.ReferenceAcross, shape.RadiusAcross, 1e-5f);
            Assert.AreEqual(HeadHit.ReferenceUp, shape.RadiusUp, 1e-5f);
            Assert.AreEqual(0.1f, shape.Centre.y, 1e-6f, "the centre is the bounds'");
        }

        [Test]
        public void ABigHeadIsHeldToTheTopOfTheBand()
        {
            var shape = HeadHit.Fit(Vector3.zero, new Vector3(1.2f, 1.4f, 1.2f));
            Assert.AreEqual(HeadHit.ReferenceAcross * 1.15f, shape.RadiusAcross, 1e-5f);
            Assert.AreEqual(HeadHit.ReferenceUp * 1.15f, shape.RadiusUp, 1e-5f);
        }

        [Test]
        public void ASmallHeadIsHeldToTheBottomOfTheBand()
        {
            var shape = HeadHit.Fit(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f));
            Assert.AreEqual(HeadHit.ReferenceAcross * 0.85f, shape.RadiusAcross, 1e-5f);
            Assert.AreEqual(HeadHit.ReferenceUp * 0.85f, shape.RadiusUp, 1e-5f);
        }

        [Test]
        public void TheDeeperOfWidthAndDepthSetsTheAcrossRadius()
        {
            var wide = HeadHit.Fit(Vector3.zero, new Vector3(0.40f, 0.44f, 0.20f));
            var deep = HeadHit.Fit(Vector3.zero, new Vector3(0.20f, 0.44f, 0.40f));
            Assert.AreEqual(wide.RadiusAcross, deep.RadiusAcross, 1e-6f);
        }

        [TestCase(0.05f, 0.05f)]
        [TestCase(0.3f, 0.3f)]
        [TestCase(0.46f, 0.54f)]
        [TestCase(0.63f, 0.65f)]
        [TestCase(0.9f, 1.2f)]
        [TestCase(3f, 3f)]
        public void WhateverTheHeadsSizeItsShapeIsFair(float width, float height)
        {
            var shape = HeadHit.Fit(Vector3.zero, new Vector3(width, height, width));
            Assert.IsTrue(HeadHit.IsFair(shape), shape.ToString());
        }

        [Test]
        public void ShapesOutsideTheBandAreNotFair()
        {
            Assert.IsFalse(HeadHit.IsFair(new HeadShape(Vector3.zero, 0.3f, 0.22f)));
            Assert.IsFalse(HeadHit.IsFair(new HeadShape(Vector3.zero, 0.2f, 0.15f)));
            Assert.IsTrue(HeadHit.IsFair(new HeadShape(Vector3.zero, 0.2f, 0.22f)));
        }

        [Test]
        public void TwelveHeadsOfVeryDifferentSizesAreAllFairAndAlikeWithinTheBand()
        {
            var sizes = new[] { 0.3f, 0.36f, 0.4f, 0.42f, 0.46f, 0.5f, 0.55f, 0.6f, 0.63f, 0.7f, 0.8f, 1.1f };
            var shapes = sizes.Select(s => HeadHit.Fit(Vector3.zero, new Vector3(s, s * 1.1f, s))).ToArray();
            Assert.IsTrue(HeadHit.AreFair(shapes));
            Assert.LessOrEqual(shapes.Max(s => s.RadiusAcross) / shapes.Min(s => s.RadiusAcross), (1.15f / 0.85f) + 1e-4f);
            Assert.LessOrEqual(shapes.Max(s => HeadHit.RelativeVolume(s)) / shapes.Min(s => HeadHit.RelativeVolume(s)), Mathf.Pow(1.15f / 0.85f, 3) + 1e-3f);
        }

        [Test]
        public void ASingleUnfairShapeFailsTheSet()
        {
            var shapes = new[] { new HeadShape(Vector3.zero, 0.2f, 0.22f), new HeadShape(Vector3.zero, 0.4f, 0.22f) };
            Assert.IsFalse(HeadHit.AreFair(shapes));
            Assert.IsTrue(HeadHit.AreFair(new HeadShape[0]));
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
