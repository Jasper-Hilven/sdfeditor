using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms.Tests
{
    public sealed class SdfPackedMeshTests
    {
        [TestCase(0f)]
        [TestCase(0.001f)]
        [TestCase(1.1646231f)]
        [TestCase(16.1f)]
        [TestCase(64.01091766f)]
        [TestCase(126.9f)]
        public void TheScaleCoversTheLargestCoordinate(float largest)
        {
            Assert.IsTrue(SdfPackedMesh.TryScale(largest, out var code, out var scale));
            Assert.GreaterOrEqual(scale, largest);
            Assert.AreEqual(SdfPackedMesh.Scale(code), scale);
            // No more than a step too large
            Assert.LessOrEqual(scale, largest + SdfPackedMesh.Range / SdfPackedMesh.Steps * 1.001f + 1e-6f);
        }

        [Test]
        public void ACoordinateBeyondTheRangeIsNotPacked() =>
            Assert.IsFalse(SdfPackedMesh.TryScale(128.5f, out _, out _));

        [Test]
        public void QuantisingMovesAPointByHalfAStepAtMost()
        {
            var random = new System.Random(1);
            foreach (var largest in new[] { 0.5f, 2.3f, 20f, 64f })
            {
                SdfPackedMesh.TryScale(largest, out _, out var scale);
                var step = scale / SdfPackedMesh.Steps;
                for (var i = 0; i < 1000; i++)
                {
                    var value = (float)(random.NextDouble() * 2 - 1) * largest;
                    var error = Mathf.Abs(SdfPackedMesh.Dequantise(SdfPackedMesh.Quantise(value, scale), scale) - value);
                    Assert.LessOrEqual(error, step * 0.51f, $"{value} in {largest}");
                }
            }
        }

        [Test]
        public void TheExtremesRoundTripWithoutWrapping()
        {
            SdfPackedMesh.TryScale(3f, out _, out var scale);
            Assert.AreEqual(scale, SdfPackedMesh.Dequantise(SdfPackedMesh.Quantise(scale, scale), scale), 1e-5f);
            Assert.AreEqual(-scale, SdfPackedMesh.Dequantise(SdfPackedMesh.Quantise(-scale * 2, scale), scale), 1e-5f);
        }

        [TestCase(0f)]
        [TestCase(1f)]
        [TestCase(-2.5f)]
        [TestCase(0.333f)]
        [TestCase(12f)]
        [TestCase(0.002f)]
        [TestCase(0.035f)]
        public void HalfFloatsAreWithinTheirPrecision(float value)
        {
            var back = SdfPackedMesh.FromHalf(SdfPackedMesh.ToHalf(value));
            Assert.AreEqual(value, back, Mathf.Abs(value) * 1e-3f + 1e-7f);
        }

        [TestCase(1f, 0x3c00)]
        [TestCase(0.5f, 0x3800)]
        [TestCase(-2f, 0xc000)]
        [TestCase(65504f, 0x7bff)]
        [TestCase(6.1035156e-5f, 0x0400)]
        [TestCase(0f, 0)]
        public void HalfFloatsAreTheIeeeBitsTheGpuReads(float value, int bits) =>
            Assert.AreEqual(bits, SdfPackedMesh.ToHalf(value));

        [Test]
        public void AMeshWrittenPackedReadsBackThroughPositions()
        {
            var truth = new[] { new Vector3(-1.2f, 0.4f, 0.9f), new Vector3(1.19f, -0.001f, 0.3f), new Vector3(0, 1.1f, -1.05f) };
            SdfPackedMesh.TryScale(1.2f, out var code, out var scale);
            var data = new byte[3 * 8];
            for (var v = 0; v < 3; v++)
            {
                SdfPackedMesh.WriteInt16(data.AsSpan(v * 8), SdfPackedMesh.Quantise(truth[v].x, scale));
                SdfPackedMesh.WriteInt16(data.AsSpan(v * 8 + 2), SdfPackedMesh.Quantise(truth[v].y, scale));
                SdfPackedMesh.WriteInt16(data.AsSpan(v * 8 + 4), SdfPackedMesh.Quantise(truth[v].z, scale));
                SdfPackedMesh.WriteInt16(data.AsSpan(v * 8 + 6), code);
            }
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.SNorm16, 4));
            mesh.SetVertexBufferData(data, 0, 0, data.Length);
            mesh.SetIndexBufferParams(3, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 0, 1, 2 }, 0, 0, 3);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3), MeshUpdateFlags.DontRecalculateBounds);
            Assert.IsTrue(SdfPackedMesh.IsPacked(mesh));
            var positions = SdfPackedMesh.Positions(mesh);
            var step = scale / SdfPackedMesh.Steps;
            for (var v = 0; v < 3; v++)
                Assert.LessOrEqual((positions[v] - truth[v]).magnitude, step, $"vertex {v}");
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void NormalsAndColoursRoundTripToTheirStep()
        {
            var bytes = new byte[2];
            SdfPackedMesh.WriteSNorm8(bytes, 0.6f);
            Assert.AreEqual(0.6f, (sbyte)bytes[0] / 127f, 1f / 254 + 1e-6f);
            SdfPackedMesh.WriteSNorm8(bytes, -3f);
            Assert.AreEqual(-127, (sbyte)bytes[0]);
            SdfPackedMesh.WriteUNorm8(bytes, 0.2f);
            Assert.AreEqual(0.2f, bytes[0] / 255f, 1f / 510 + 1e-6f);
            SdfPackedMesh.WriteUNorm8(bytes, 7f);
            Assert.AreEqual(255, bytes[0]);
        }
    }
}
