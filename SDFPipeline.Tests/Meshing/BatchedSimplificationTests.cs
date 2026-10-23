using System.Numerics;
using SDFPipeline.Meshing;
using SDFProject.Calculation;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

/// <summary>
/// The batched simplifier (rounds of half-edge folds over independent vertices), measured against the SDF itself rather than only the
/// contoured mesh, which is an approximation of it too.
/// </summary>
public class BatchedSimplificationTests
{
    private const float MaxErrorInCells = MeshingOptions.DefaultMaxErrorInCells;

    private const string Eye = "Union\n    Material material=white\n        Sphere radius=0.5\n    Material material=pupil\n        Translate translation=(0, 0, 0.45)\n            Sphere radius=0.2";

    private const string ThreeBalls = """
        Union
            Material material=red
                Sphere radius=0.4
            Material material=green
                Translate translation=(0.5, 0, 0)
                    Sphere radius=0.4
            Material material=blue
                Translate translation=(0.25, 0.4, 0)
                    Sphere radius=0.4
        """;

    private sealed record Setup(string Text, ContouredMesh Contoured, ContouringGrid Grid, Func<Vector3, float> Sdf, SdfCalculatorCpu.Prepared Shape, int[] Regions);

    private static Setup Prepare(string text, int resolution)
    {
        var shape = new SdfCalculatorCpu().Prepare(Read(text));
        var grid = ContouringGrid.Around(SDFProject.MeshExport.SdfBoundsFinder.Find(shape.Distance)!, resolution);
        var contoured = DualContouring.Contour(shape.Distance, grid);
        var ids = new Dictionary<MaterialSource, int>();
        var regions = contoured.Mesh.Positions.Select(p => RegionAt(shape, ids, p)).ToArray();
        return new Setup(text, contoured, grid, shape.Distance, shape, regions);
    }

    private static int RegionAt(SdfCalculatorCpu.Prepared shape, Dictionary<MaterialSource, int> ids, Vector3 p)
    {
        var source = shape.Surface(p).Materials.MaxBy(m => m.Weight).Source;
        if (!ids.TryGetValue(source, out var id))
            ids[source] = id = ids.Count;
        return id;
    }

    private static TriangleMesh Simplify(Setup s, float errorInCells = MaxErrorInCells, bool[]? locked = null, bool batched = true) =>
        QuadricSimplifier.Simplify(s.Contoured, s.Sdf, errorInCells * s.Grid.CellSize, s.Grid.CellSize, MeshingOptions.DefaultCreaseAngleDegrees,
            s.Regions, locked, batched: batched);

    /// <summary>
    /// The sample points of a mesh with the outward normal of the triangle each is on: its vertices, and in every triangle its centroid and
    /// barycentric points, more on big triangles.
    /// </summary>
    private static IEnumerable<(Vector3 P, Vector3 N)> Samples(TriangleMesh mesh, float cell)
    {
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = (mesh.Positions[mesh.Triangles[3 * t]], mesh.Positions[mesh.Triangles[3 * t + 1]], mesh.Positions[mesh.Triangles[3 * t + 2]]);
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < 1e-24f)
                continue;
            var normal = Vector3.Normalize(cross);
            var longest = MathF.Sqrt(MathF.Max(Vector3.DistanceSquared(a, b), MathF.Max(Vector3.DistanceSquared(b, c), Vector3.DistanceSquared(c, a))));
            var n = Math.Clamp((int)MathF.Ceiling(longest / (2 * cell)), 1, 12);
            for (var i = 0; i <= n; i++)
            for (var j = 0; i + j <= n; j++)
                yield return (a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n), normal);
            yield return ((a + b + c) / 3, normal);
        }
    }

    /// <summary>
    /// How well a mesh sits on the SDF's surface, by the sign of the SDF, which is right even where its size is not (a union is not
    /// exact inside, an intersection not outside, a twist may overestimate): a sample is on the surface within <c>b</c> when the SDF is
    /// not positive at b behind it and not negative at b in front of it, along the face's normal.
    /// </summary>
    private sealed record Fit(int Samples, double ShareFailing, float SmallestBracket, float EstimateMax)
    {
        public override string ToString() => $"{ShareFailing:P3} of {Samples} samples outside the bracket, smallest bracket {SmallestBracket * 1000:0.00} mm, first-order d/|grad d| up to {EstimateMax * 1000:0.00} mm";
    }

    /// <summary>
    /// Whether the SDF changes sign, or is zero, along the normal within <paramref name="b"/> either way of the point: five samples
    /// along the segment, so a wall thinner than the bracket, which is on the same side at both ends, is still found.
    /// </summary>
    private static bool SignChanges(Setup s, Vector3 p, Vector3 n, float b, float eps)
    {
        var previous = s.Sdf(p - n * b);
        if (MathF.Abs(previous) <= eps)
            return true;
        for (var i = 1; i <= 4; i++)
        {
            var value = s.Sdf(p + n * (b * (i - 2) / 2));
            if (MathF.Abs(value) <= eps || (value > 0) != (previous > 0))
                return true;
            previous = value;
        }
        return false;
    }

    private static Fit Measure(TriangleMesh mesh, Setup s, float bracket)
    {
        var samples = Samples(mesh, s.Grid.CellSize).ToArray();
        // Float noise on a surface the SDF has exactly (a box's face) is a hair either side of 0
        var eps = s.Grid.CellSize * 1e-4f;
        int Failing(float b)
        {
            var failing = 0;
            Parallel.ForEach(samples.Chunk(4000), chunk =>
            {
                var local = chunk.Count(x => !SignChanges(s, x.P, x.N, b, eps));
                Interlocked.Add(ref failing, local);
            });
            return failing;
        }
        var share = Failing(bracket) / (double)samples.Length;
        // The smallest bracket that holds for every sample
        float low = 0, high = s.Grid.CellSize * 4;
        for (var i = 0; i < 12; i++)
        {
            var middle = (low + high) / 2;
            if (Failing(middle) == 0) high = middle; else low = middle;
        }
        var estimate = 0f;
        var gate = new object();
        Parallel.ForEach(samples.Chunk(4000), chunk =>
        {
            var local = chunk.Max(x =>
            {
                var h = s.Grid.CellSize * 1e-2f;
                var gradient = Gradient(s.Sdf, x.P, h);
                return MathF.Abs(s.Sdf(x.P)) / MathF.Max(gradient.Length() / 2 / h, 1e-6f);
            });
            lock (gate) estimate = MathF.Max(estimate, local);
        });
        return new Fit(samples.Length, share, high, estimate);
    }

    private static HashSet<int>[] RingRegions(TriangleMesh mesh, int[] regionOfVertex)
    {
        var rings = Enumerable.Range(0, mesh.Positions.Length).Select(_ => new HashSet<int>()).ToArray();
        for (var t = 0; t < mesh.TriangleCount; t++)
        for (var c = 0; c < 3; c++)
        {
            var (u, v) = (mesh.Triangles[3 * t + c], mesh.Triangles[3 * t + (c + 1) % 3]);
            rings[u].Add(regionOfVertex[v]);
            rings[u].Add(regionOfVertex[u]);
        }
        return rings;
    }

    [Theory]
    [InlineData("Sphere radius=0.5", 48)]
    [InlineData("Subtraction\n    Box size=(1.5, 0.25, 1)\n    Box size=(1.34, 0.5, 0.84)", 120)]
    [InlineData(Eye, 80)]
    [InlineData(ThreeBalls, 80)]
    public void EveryPointOfTheSimplifiedSurface_IsWithinTheErrorOfTheSdf(string text, int resolution)
    {
        var s = Prepare(text, resolution);
        var bound = MaxErrorInCells * s.Grid.CellSize;
        // The contoured mesh is an approximation too: its own fit is the baseline, and a fold may add the bound to it
        var before = Measure(s.Contoured.Mesh, s, bound);
        var mesh = Simplify(s);
        var after = Measure(mesh, s, bound + before.SmallestBracket);
        var greedyMesh = Simplify(s, batched: false);
        var greedy = Measure(greedyMesh, s, bound + before.SmallestBracket);
        Console.WriteLine($"  greedy: {greedyMesh.TriangleCount} triangles; {greedy}");
        Assert.InRange(mesh.TrackedError, 0, bound * 1.0001f);
        Console.WriteLine($"{text.Split('\n')[0]}: tracked error {mesh.TrackedError * 1000:0.00} mm, {mesh.TrackedTests:N0} point tests; {s.Contoured.Mesh.TriangleCount} -> {mesh.TriangleCount} triangles (bound {bound * 1000:0.00} mm, cell {s.Grid.CellSize * 1000:0.00} mm)\n  before: {before}\n  after:  {after}");
        // A union's SDF is not exact inside, so the contoured mesh itself may leave a few samples outside a bracket: no more may
        Assert.True(after.ShareFailing <= before.ShareFailing + 1e-4, $"{after.ShareFailing} after, {before.ShareFailing} before");
        Assert.True(after.SmallestBracket <= before.SmallestBracket + bound * 1.05f + 1e-5f, $"{after.SmallestBracket} after, {before.SmallestBracket} before, bound {bound}");
        AssertClosedAndOriented(mesh);
    }

    [Fact]
    public void FlatHeavyMeshes_LoseMostTriangles()
    {
        var s = Prepare("Subtraction\n    Box size=(1.5, 0.25, 1)\n    Box size=(1.34, 0.5, 0.84)", 120);
        var mesh = Simplify(s);
        Console.WriteLine($"walls: {s.Contoured.Mesh.TriangleCount} -> {mesh.TriangleCount}");
        Assert.True(mesh.TriangleCount * 20 < s.Contoured.Mesh.TriangleCount);
    }

    [Fact]
    public void LockedVertices_StayWhereTheyAre()
    {
        var s = Prepare("Sphere radius=0.5", 48);
        var locked = s.Contoured.Mesh.Positions.Select(p => MathF.Abs(p.Y) < s.Grid.CellSize * 0.6f).ToArray();
        var mesh = Simplify(s, locked: locked);
        var after = mesh.Positions.ToHashSet();
        for (var v = 0; v < locked.Length; v++)
            if (locked[v] && s.Contoured.Mesh.Triangles.Contains(v))
                Assert.Contains(s.Contoured.Mesh.Positions[v], after);
        // and the rest still shrank
        Assert.True(mesh.TriangleCount < s.Contoured.Mesh.TriangleCount / 2);
    }

    [Theory]
    [InlineData(Eye, 80)]
    [InlineData(ThreeBalls, 80)]
    public void MaterialBorders_KeepTheirRegionPairs_Shape_AndArea(string text, int resolution)
    {
        var s = Prepare(text, resolution);
        var mesh = Simplify(s);
        var bound = MaxErrorInCells * s.Grid.CellSize;

        // Pairs: no vertex gains a material among its neighbours that it did not have
        var before = RingRegions(s.Contoured.Mesh, s.Regions);
        var regionAfter = mesh.Sources!.Select(v => s.Regions[v]).ToArray();
        var after = RingRegions(mesh, regionAfter);
        for (var v = 0; v < mesh.Positions.Length; v++)
            Assert.True(after[v].IsSubsetOf(before[mesh.Sources![v]]), $"vertex {v}: {string.Join(",", after[v])} vs {string.Join(",", before[mesh.Sources![v]])}");

        // Corners where three regions meet are kept
        var corners = Enumerable.Range(0, s.Regions.Length).Where(v => before[v].Count >= 3 && s.Contoured.Mesh.Triangles.Contains(v)).ToArray();
        foreach (var corner in corners)
            Assert.Contains(s.Contoured.Mesh.Positions[corner], mesh.Positions);

        // Shape: the border edges after lie within the bound of the border edges before
        var borderBefore = BorderEdges(s.Contoured.Mesh, s.Regions);
        var borderAfter = BorderEdges(mesh, regionAfter);
        if (text == Eye || text == ThreeBalls)
            Assert.NotEmpty(borderBefore);
        var worst = 0f;
        foreach (var (a, b) in borderAfter)
            foreach (var p in new[] { a, (a + b) / 2, b })
                worst = MathF.Max(worst, borderBefore.Min(e => DistanceToSegment(p, e.A, e.B)));
        Console.WriteLine($"{text.Split('\n')[1].Trim()}: border {borderBefore.Count} -> {borderAfter.Count} edges, worst {worst * 1000:0.00} mm from the old border (bound {bound * 1000:0.00} mm)");
        Assert.True(worst <= bound * 1.5f + 1e-5f, $"{worst} vs {bound}");

        // Area per region
        var areaBefore = Areas(s.Contoured.Mesh, s.Regions);
        var areaAfter = Areas(mesh, regionAfter);
        foreach (var (region, area) in areaBefore)
        {
            Console.WriteLine($"region {region}: {area:0.0000} -> {areaAfter.GetValueOrDefault(region):0.0000} m2");
            Assert.InRange(areaAfter.GetValueOrDefault(region), area * 0.97f, area * 1.03f);
        }
    }

    [Theory]
    [InlineData(Eye, 80)]
    [InlineData(ThreeBalls, 80)]
    public void TrianglesOfAMaterial_LieWhereTheSdfSaysItIs(string text, int resolution)
    {
        var s = Prepare(text, resolution);
        var mesh = Simplify(s);
        var ids = new Dictionary<MaterialSource, int>();
        // The regions are numbered in the order the materials are first met, as in Prepare: meet them in the same order
        foreach (var p in s.Contoured.Mesh.Positions)
            RegionAt(s.Shape, ids, p);
        var regionAfter = mesh.Sources!.Select(v => s.Regions[v]).ToArray();
        var reach = MaxErrorInCells * s.Grid.CellSize + s.Grid.CellSize;
        var wrong = 0;
        var total = 0;
        var inside = s.Grid.CellSize * 0.05f;
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var corners = new[] { mesh.Triangles[3 * t], mesh.Triangles[3 * t + 1], mesh.Triangles[3 * t + 2] };
            if (corners.Select(c => regionAfter[c]).Distinct().Count() != 1)
                continue;
            var region = regionAfter[corners[0]];
            var (a, b, c) = (mesh.Positions[corners[0]], mesh.Positions[corners[1]], mesh.Positions[corners[2]]);
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() < 1e-24f)
                continue;
            var normal = Vector3.Normalize(cross);
            // The material is the one the SDF gives just inside the surface, not decided by a distance
            foreach (var p in new[] { (a + b + c) / 3, (a + b) / 2, (b + c) / 2, (c + a) / 2 })
            {
                total++;
                if (RegionAt(s.Shape, ids, p - normal * inside) == region)
                    continue;
                // Not its own material here: it must be within the reach of a point that is
                var u = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                var w = Vector3.Cross(normal, u);
                var near = Enumerable.Range(0, 16).Any(k =>
                {
                    var angle = k * MathF.PI / 8;
                    var q = p + (u * MathF.Cos(angle) + w * MathF.Sin(angle)) * reach;
                    // The surface near q, found along the normal by the sign: the first point inside it
                    return RegionAt(s.Shape, ids, q - normal * inside) == region || RegionAt(s.Shape, ids, q - normal * (inside + reach)) == region;
                });
                if (!near)
                    wrong++;
            }
        }
        Console.WriteLine($"{text.Split('\n')[1].Trim()}: {wrong} of {total} samples in the wrong material and not within {reach * 1000:0.0} mm of the right one");
        Assert.Equal(0, wrong);
    }

    private static Vector3 Gradient(Func<Vector3, float> sdf, Vector3 p, float h) =>
        new(sdf(p + new Vector3(h, 0, 0)) - sdf(p - new Vector3(h, 0, 0)), sdf(p + new Vector3(0, h, 0)) - sdf(p - new Vector3(0, h, 0)),
            sdf(p + new Vector3(0, 0, h)) - sdf(p - new Vector3(0, 0, h)));

    /// <summary>
    /// The border as a mesh holds it, on each side: the edges between two vertices of one region that both have a neighbour in another
    /// (the vertices simplification may fold along, and the chain it keeps).
    /// </summary>
    private static List<(Vector3 A, Vector3 B)> BorderEdges(TriangleMesh mesh, int[] regionOfVertex)
    {
        var onBorder = new bool[mesh.Positions.Length];
        for (var t = 0; t < mesh.TriangleCount; t++)
        for (var c = 0; c < 3; c++)
        {
            var (u, v) = (mesh.Triangles[3 * t + c], mesh.Triangles[3 * t + (c + 1) % 3]);
            if (regionOfVertex[u] != regionOfVertex[v])
                onBorder[u] = onBorder[v] = true;
        }
        var edges = new HashSet<(int, int)>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        for (var c = 0; c < 3; c++)
        {
            var (u, v) = (mesh.Triangles[3 * t + c], mesh.Triangles[3 * t + (c + 1) % 3]);
            // A link of the chain has the other region on one of its sides: a face whose third corner is in it
            var third = mesh.Triangles[3 * t + (c + 2) % 3];
            if (regionOfVertex[u] == regionOfVertex[v] && onBorder[u] && onBorder[v] && regionOfVertex[third] != regionOfVertex[u])
                edges.Add(u < v ? (u, v) : (v, u));
        }
        return edges.Select(e => (mesh.Positions[e.Item1], mesh.Positions[e.Item2])).ToList();
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
        return Vector3.Distance(p, a + ab * t);
    }

    private static Dictionary<int, float> Areas(TriangleMesh mesh, int[] regionOfVertex)
    {
        var areas = new Dictionary<int, float>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var corners = new[] { mesh.Triangles[3 * t], mesh.Triangles[3 * t + 1], mesh.Triangles[3 * t + 2] };
            var votes = corners.GroupBy(c => regionOfVertex[c]).OrderByDescending(g => g.Count()).First().Key;
            var (a, b, c) = (mesh.Positions[corners[0]], mesh.Positions[corners[1]], mesh.Positions[corners[2]]);
            areas[votes] = areas.GetValueOrDefault(votes) + Vector3.Cross(b - a, c - a).Length() / 2;
        }
        return areas;
    }

    [Fact]
    public void Calibration_CoarseLevels_GreedyAgainstBatched()
    {
        foreach (var (text, resolution) in new[] { ("Sphere radius=0.5", 64), (Eye, 80), (ThreeBalls, 80) })
        {
            var s = Prepare(text, resolution);
            var cell = s.Grid.CellSize;
            var greedyLevels = QuadricSimplifier.SimplifyInSteps(s.Contoured, s.Sdf, new[] { 0.08f * cell, 0.5f * cell, 1.5f * cell }, cell, 30, s.Regions).ToArray();
            for (var level = 0; level < 3; level++)
            {
                var fit = Measure(greedyLevels[level], s, 0.5f * cell);
                Console.WriteLine($"calibration {text.Split('\n')[0]} greedy LOD{level}: {greedyLevels[level].TriangleCount} triangles, bracket {fit.SmallestBracket / cell:0.00} cells");
            }
            foreach (var errors in new[] { new[] { 0.2f, 0.5f, 1.5f }, new[] { 0.2f, 1.0f, 3.0f }, new[] { 0.2f, 1.5f, 4.5f } })
            {
                var levels = QuadricSimplifier.SimplifyInSteps(s.Contoured, s.Sdf, errors.Select(e => e * cell).ToArray(), cell, 30, s.Regions, true).ToArray();
                for (var level = 0; level < 3; level++)
                {
                    var fit = Measure(levels[level], s, 0.5f * cell);
                    Console.WriteLine($"calibration {text.Split('\n')[0]} batched {string.Join("/", errors)}: LOD{level}: {levels[level].TriangleCount} triangles, bracket {fit.SmallestBracket / cell:0.00} cells");
                }
            }
        }
    }

    [Theory]
    [InlineData("Sphere radius=0.5", 48)]
    [InlineData(Eye, 64)]
    [InlineData(ThreeBalls, 64)]
    public void TheResult_IsTheSameWhateverTheNumberOfThreads(string text, int resolution)
    {
        var s = Prepare(text, resolution);
        TriangleMesh Run(int threads)
        {
            QuadricSimplifier.MaxDegreeOfParallelism = threads;
            try { return Simplify(s); }
            finally { QuadricSimplifier.MaxDegreeOfParallelism = -1; }
        }
        var one = Run(1);
        foreach (var threads in new[] { 2, 3, -1, -1 })
        {
            var other = Run(threads);
            Assert.Equal(one.Positions, other.Positions);
            Assert.Equal(one.Triangles, other.Triangles);
            Assert.Equal(one.Sources, other.Sources);
        }
    }
}
