using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SdfMushrooms.Tests
{
    public sealed class VertexCacheOptimizerTests
    {
        /// <summary>A grid of n x n quads, two triangles each, numbered at random so the order is bad.</summary>
        private static (int[] Indices, int Vertices) ShuffledGrid(int n, int seed)
        {
            var random = new System.Random(seed);
            var vertices = (n + 1) * (n + 1);
            var number = Enumerable.Range(0, vertices).OrderBy(_ => random.Next()).ToArray();
            var triangles = new List<int[]>();
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                int a = number[y * (n + 1) + x], b = number[y * (n + 1) + x + 1], c = number[(y + 1) * (n + 1) + x], d = number[(y + 1) * (n + 1) + x + 1];
                triangles.Add(new[] { a, b, c });
                triangles.Add(new[] { b, d, c });
            }
            return (triangles.OrderBy(_ => random.Next()).SelectMany(t => t).ToArray(), vertices);
        }

        private static double TransformedVertices(int[] indices, int cacheSize = 32)
        {
            var cache = new Queue<int>();
            var misses = 0;
            foreach (var v in indices)
            {
                if (cache.Contains(v)) continue;
                misses++;
                cache.Enqueue(v);
                if (cache.Count > cacheSize) cache.Dequeue();
            }
            return misses / (indices.Length / 3.0);
        }

        [Test]
        public void TheSameTriangles_RenumberedInTheOrderOfFirstUse()
        {
            var (indices, vertices) = ShuffledGrid(20, 1);
            var (optimised, order) = VertexCacheOptimizer.Optimize(indices, vertices);

            Assert.AreEqual(indices.Length, optimised.Length);
            Assert.AreEqual(vertices, order.Length);
            Assert.AreEqual(vertices, order.Distinct().Count());
            // As sets of old vertex numbers, the triangles are the same
            string Key(int a, int b, int c) => string.Join(",", new[] { a, b, c }.OrderBy(v => v));
            var before = Enumerable.Range(0, indices.Length / 3).Select(t => Key(indices[3 * t], indices[3 * t + 1], indices[3 * t + 2])).OrderBy(k => k);
            var after = Enumerable.Range(0, optimised.Length / 3).Select(t => Key(order[optimised[3 * t]], order[optimised[3 * t + 1]], order[optimised[3 * t + 2]])).OrderBy(k => k);
            CollectionAssert.AreEqual(before, after);
            // and the winding of each triangle is kept: the corners in the same cyclic order
            var windings = new HashSet<string>(Enumerable.Range(0, indices.Length / 3).Select(t => string.Join(",", Rotate(indices[3 * t], indices[3 * t + 1], indices[3 * t + 2]))));
            for (var t = 0; t < optimised.Length / 3; t++)
                Assert.IsTrue(windings.Contains(string.Join(",", Rotate(order[optimised[3 * t]], order[optimised[3 * t + 1]], order[optimised[3 * t + 2]]))));
            // Vertices come in the order they are first used
            var seen = 0;
            foreach (var v in optimised)
            {
                Assert.LessOrEqual(v, seen);
                if (v == seen) seen++;
            }
        }

        private static int[] Rotate(int a, int b, int c)
        {
            var smallest = System.Math.Min(a, System.Math.Min(b, c));
            return smallest == a ? new[] { a, b, c } : smallest == b ? new[] { b, c, a } : new[] { c, a, b };
        }

        [Test]
        public void AShuffledGrid_NeedsFarFewerVertexTransforms()
        {
            var (indices, vertices) = ShuffledGrid(40, 2);
            var (optimised, _) = VertexCacheOptimizer.Optimize(indices, vertices);

            var before = TransformedVertices(indices);
            var after = TransformedVertices(optimised);
            TestContext.WriteLine($"transformed vertices per triangle: {before:0.00} before, {after:0.00} after");
            Assert.Less(after, before * 0.5);
            Assert.Less(after, 0.8);
        }

        [Test]
        public void UnusedVerticesAreLeftOut()
        {
            var (optimised, order) = VertexCacheOptimizer.Optimize(new[] { 5, 7, 9, 7, 8, 9 }, 12);
            Assert.AreEqual(6, optimised.Length);
            Assert.AreEqual(4, order.Length);
            CollectionAssert.AreEquivalent(new[] { 5, 7, 8, 9 }, order);
        }
    }
}
