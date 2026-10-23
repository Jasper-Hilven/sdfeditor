using System;
using System.Collections.Generic;
using System.Linq;

namespace SdfMushrooms
{
    /// <summary>
    /// Puts a mesh's triangles in an order the GPU's vertex cache likes and its vertices in the order they are first used (Tom Forsyth's
    /// "Linear-Speed Vertex Cache Optimisation"), losslessly: the same triangles, only renumbered. Triangles that share vertices end up
    /// next to each other and the vertex stream is read front to back, which draws faster and, being runs of similar neighbours, compresses
    /// better. Unity's own <c>Mesh.OptimizeIndexBuffers</c> crashes on these meshes' custom vertex layout, hence this.
    /// </summary>
    public static class VertexCacheOptimizer
    {
        private const int CacheSize = 32;
        private const float LastTriangleScore = 0.75f;
        private const float CacheDecayPower = 1.5f;
        private const float ValenceBoostScale = 2.0f;
        private const float ValenceBoostPower = 0.5f;

        private static readonly float[] CacheScore = BuildCacheScores();

        private static float[] BuildCacheScores()
        {
            var scores = new float[CacheSize];
            for (var i = 0; i < CacheSize; i++)
                scores[i] = i < 3 ? LastTriangleScore : MathF.Pow(1f - (i - 3) / (float)(CacheSize - 3), CacheDecayPower);
            return scores;
        }

        private static float VertexScore(int cachePosition, int liveTriangles)
        {
            if (liveTriangles == 0)
                return -1;
            var score = cachePosition >= 0 ? CacheScore[cachePosition] : 0;
            return score + ValenceBoostScale * MathF.Pow(liveTriangles, -ValenceBoostPower);
        }

        /// <param name="indices">Three per triangle, each below <paramref name="vertexCount"/>.</param>
        /// <returns>
        /// The triangles renumbered, and for each new vertex number the old one it was: vertex <c>i</c> of the new mesh is vertex
        /// <c>Order[i]</c> of the old. Vertices no triangle uses are dropped.
        /// </returns>
        public static (int[] Indices, int[] Order) Optimize(int[] indices, int vertexCount)
        {
            var triangleCount = indices.Length / 3;
            // Adjacency: the triangles of each vertex
            var start = new int[vertexCount + 1];
            foreach (var v in indices)
                start[v + 1]++;
            for (var v = 0; v < vertexCount; v++)
                start[v + 1] += start[v];
            var fill = new int[vertexCount];
            var adjacent = new int[indices.Length];
            for (var t = 0; t < triangleCount; t++)
            for (var c = 0; c < 3; c++)
            {
                var v = indices[3 * t + c];
                adjacent[start[v] + fill[v]++] = t;
            }

            var live = new int[vertexCount];
            var cachePosition = new int[vertexCount];
            var vertexScore = new float[vertexCount];
            for (var v = 0; v < vertexCount; v++)
            {
                live[v] = start[v + 1] - start[v];
                cachePosition[v] = -1;
                vertexScore[v] = VertexScore(-1, live[v]);
            }
            var triangleScore = new float[triangleCount];
            var drawn = new bool[triangleCount];
            for (var t = 0; t < triangleCount; t++)
                triangleScore[t] = vertexScore[indices[3 * t]] + vertexScore[indices[3 * t + 1]] + vertexScore[indices[3 * t + 2]];

            var output = new int[indices.Length];
            var cache = new List<int>(CacheSize + 3);
            var nextUndrawn = 0;
            var best = -1;
            for (var emitted = 0; emitted < triangleCount; emitted++)
            {
                if (best < 0 || drawn[best])
                {
                    while (drawn[nextUndrawn])
                        nextUndrawn++;
                    best = nextUndrawn;
                }
                drawn[best] = true;
                var corners = new[] { indices[3 * best], indices[3 * best + 1], indices[3 * best + 2] };
                for (var c = 0; c < 3; c++)
                {
                    output[3 * emitted + c] = corners[c];
                    live[corners[c]]--;
                }

                // The three vertices go to the front of the cache, the rest move back and the last ones fall out
                var previous = new List<int>(cache);
                cache.Clear();
                for (var c = 2; c >= 0; c--)
                    if (!cache.Contains(corners[c]))
                        cache.Insert(0, corners[c]);
                foreach (var v in previous)
                    if (!cache.Contains(v))
                        cache.Add(v);
                var fallenOut = cache.Count > CacheSize ? cache.GetRange(CacheSize, cache.Count - CacheSize) : new List<int>();
                if (cache.Count > CacheSize)
                    cache.RemoveRange(CacheSize, cache.Count - CacheSize);
                foreach (var v in fallenOut)
                    cachePosition[v] = -1;

                var bestScore = -1f;
                best = -1;
                for (var i = 0; i < cache.Count; i++)
                {
                    var v = cache[i];
                    cachePosition[v] = i;
                    vertexScore[v] = VertexScore(i, live[v]);
                }
                foreach (var v in corners)
                    vertexScore[v] = VertexScore(cachePosition[v], live[v]);
                foreach (var v in fallenOut.Concat(cache))
                {
                    vertexScore[v] = VertexScore(cachePosition[v], live[v]);
                    for (var a = start[v]; a < start[v + 1]; a++)
                    {
                        var t = adjacent[a];
                        if (drawn[t])
                            continue;
                        triangleScore[t] = vertexScore[indices[3 * t]] + vertexScore[indices[3 * t + 1]] + vertexScore[indices[3 * t + 2]];
                        if (triangleScore[t] > bestScore)
                        {
                            bestScore = triangleScore[t];
                            best = t;
                        }
                    }
                }
            }

            // Vertices in the order they are first used
            var newNumber = new int[vertexCount];
            Array.Fill(newNumber, -1);
            var order = new List<int>(vertexCount);
            for (var i = 0; i < output.Length; i++)
            {
                var v = output[i];
                if (newNumber[v] < 0)
                {
                    newNumber[v] = order.Count;
                    order.Add(v);
                }
                output[i] = newNumber[v];
            }
            return (output, order.ToArray());
        }
    }
}
