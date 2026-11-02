using NUnit.Framework;
using UnityEngine;

namespace SdfHeads.Tests
{
    /// <summary>The tight fit of a mesh and the common head size (ticket 45).</summary>
    public sealed class HeadFitTests
    {
        static void AddBox(HeadFitter f, Vector3 centre, Vector3 half, bool flip = false)
        {
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
                c[i] = centre + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (var q in quads)
            {
                if (flip)
                {
                    f.Add(c[q[0]], c[q[2]], c[q[1]]);
                    f.Add(c[q[0]], c[q[3]], c[q[2]]);
                }
                else
                {
                    f.Add(c[q[0]], c[q[1]], c[q[2]]);
                    f.Add(c[q[0]], c[q[2]], c[q[3]]);
                }
            }
        }

        static HeadFit FitBox(Vector3 centre, Vector3 half, bool flip = false)
        {
            var f = new HeadFitter();
            AddBox(f, centre, half, flip);
            return f.Result();
        }

        static void AddSphere(HeadFitter f, Vector3 centre, Vector3 radii, int rings = 64, int segments = 96)
        {
            Vector3 P(int r, int s)
            {
                var a = Mathf.PI * r / rings;
                var b = 2 * Mathf.PI * s / segments;
                return centre + new Vector3(radii.x * Mathf.Sin(a) * Mathf.Cos(b), radii.y * Mathf.Cos(a), radii.z * Mathf.Sin(a) * Mathf.Sin(b));
            }
            for (var r = 0; r < rings; r++)
                for (var s = 0; s < segments; s++)
                {
                    f.Add(P(r, s), P(r + 1, s), P(r + 1, s + 1));
                    f.Add(P(r, s), P(r + 1, s + 1), P(r, s + 1));
                }
        }

        static HeadFit FitSphere(Vector3 centre, Vector3 radii)
        {
            var f = new HeadFitter();
            AddSphere(f, centre, radii);
            return f.Result();
        }

        [Test]
        public void ABoxHasItsCentreAndVolume()
        {
            var fit = FitBox(new Vector3(0.1f, -0.2f, 0.3f), new Vector3(0.2f, 0.1f, 0.15f));
            Assert.AreEqual(0.4f * 0.2f * 0.3f, fit.Volume, 1e-6f);
            Assert.AreEqual(0.1f, fit.Centre.x, 1e-5f);
            Assert.AreEqual(-0.2f, fit.Centre.y, 1e-5f);
            Assert.AreEqual(0.3f, fit.Centre.z, 1e-5f);
        }

        [Test]
        public void TheWindingDoesNotMatterAsLongAsItIsTheSameAllOver()
        {
            var a = FitBox(Vector3.zero, new Vector3(0.2f, 0.1f, 0.15f));
            var b = FitBox(Vector3.zero, new Vector3(0.2f, 0.1f, 0.15f), true);
            Assert.AreEqual(a.Volume, b.Volume, 1e-6f);
            Assert.AreEqual(a.Radii.x, b.Radii.x, 1e-6f);
        }

        [Test]
        public void AnEllipsoidMeshFitsItselfAndTheFitHasTheMeshsVolume()
        {
            var radii = new Vector3(0.3f, 0.25f, 0.35f);
            var fit = FitSphere(new Vector3(0.01f, -0.06f, 0.02f), radii);
            Assert.AreEqual(radii.x, fit.Radii.x, 0.01f * radii.x);
            Assert.AreEqual(radii.y, fit.Radii.y, 0.01f * radii.y);
            Assert.AreEqual(radii.z, fit.Radii.z, 0.01f * radii.z);
            Assert.AreEqual(-0.06f, fit.Centre.y, 1e-3f);
            Assert.AreEqual(4f / 3f * Mathf.PI * fit.Radii.x * fit.Radii.y * fit.Radii.z, fit.Volume, 1e-4f, "the ellipsoid has the mesh's volume");
        }

        [Test]
        public void AThinSpikeBarelyMovesTheFit()
        {
            // A ball and a 4 cm wide, 30 cm long spike 25 cm over its top: the fit stays near the ball
            var ball = FitSphere(Vector3.zero, Vector3.one * 0.25f);
            var f = new HeadFitter();
            AddSphere(f, Vector3.zero, Vector3.one * 0.25f);
            AddBox(f, new Vector3(0, 0.4f, 0), new Vector3(0.02f, 0.15f, 0.02f));
            var withSpike = f.Result();
            Assert.AreEqual(ball.Radii.x, withSpike.Radii.x, 0.01f);
            Assert.Less(withSpike.Radii.y, ball.Radii.y * 1.12f);
            Assert.Less(withSpike.Volume, ball.Volume * 1.02f);
        }

        [Test]
        public void NothingGivesAZeroFit()
        {
            var fit = new HeadFitter().Result();
            Assert.AreEqual(0, fit.Volume);
            Assert.AreEqual(Vector3.zero, fit.Radii);
        }

        [Test]
        public void TheTargetIsOnePointTwoTimesTheMeanOfPhaseOne()
        {
            var t = HeadSize.TargetRadii;
            Assert.AreEqual(1.2f * (0.2701f + 0.2130f) / 2, t.x, 1e-5f);
            Assert.AreEqual(1.2f * (0.2519f + 0.2164f) / 2, t.y, 1e-5f);
            Assert.AreEqual(1.2f * (0.2882f + 0.2030f) / 2, t.z, 1e-5f);
            Assert.AreEqual(1.728f * (0.08214f + 0.03918f) / 2, HeadSize.TargetVolume, 1e-5f);
        }

        [Test]
        public void ScalingBringsAFitToTheTarget()
        {
            var fit = FitSphere(Vector3.zero, new Vector3(0.21f, 0.22f, 0.20f));
            var s = HeadSize.ScaleFor(fit);
            var scaled = new HeadFit(fit.Centre * s, fit.Radii * s, fit.Volume * s * s * s);
            Assert.AreEqual(HeadSize.TargetVolume, scaled.Volume, 1e-5f);
            Assert.IsTrue(HeadSize.IsNormalized(scaled), HeadSize.Deviation(scaled).ToString());
        }

        [Test]
        public void ASizeTenPercentOffIsTheEdge()
        {
            var r = HeadSize.TargetRadii;
            var v = HeadSize.TargetVolume;
            Assert.IsTrue(HeadSize.IsNormalized(new HeadFit(Vector3.zero, r * 1.09f, v * 1.09f)));
            Assert.IsFalse(HeadSize.IsNormalized(new HeadFit(Vector3.zero, r * 1.12f, v)));
            Assert.IsFalse(HeadSize.IsNormalized(new HeadFit(Vector3.zero, r, v * 0.85f)));
            Assert.IsFalse(HeadSize.IsNormalized(new HeadFit(Vector3.zero, new Vector3(r.x, r.y * 0.8f, r.z), v)));
        }
    }
}
