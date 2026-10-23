using System.Numerics;
using SDFPipeline.Levels;

namespace SDFPipeline.Tests.Levels;

/// <summary>The ground chunks simplified in batched rounds with every folded vertex tracked: the seams between chunks must stay shut.</summary>
public class GroundBatchedTests
{
    private static readonly LevelDefinition Small = new(
        new PlayArea(new Vector2(20, 10), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4));

    private static IEnumerable<Vector3> UpperVertices(GroundMesh mesh) =>
        mesh.Triangles.Take(mesh.Triangles.Length - 3 * mesh.SkirtTriangles).Select(v => mesh.Positions[v]).Distinct();

    [Theory]
    [InlineData(50, 0.008f)]
    [InlineData(25, 0.03f)]
    [InlineData(12, 0.1f)]
    public void Neighbours_ShareEveryBorderVertex_AndTheErrorIsKept(int cells, float maxError)
    {
        var terrain = new LevelTerrain(Small);
        var layout = GroundLayout.Around(terrain, 25);
        var batched = new GroundDetail(cells, maxError, Batched: true);
        var greedy = new GroundDetail(cells, maxError);
        var meshes = new Dictionary<(int, int), GroundMesh>();
        for (var j = 0; j < layout.CountZ; j++)
            for (var i = 0; i < layout.CountX; i++)
                meshes[(i, j)] = GroundMesher.Mesh(terrain, layout, i, j, batched)!;

        foreach (var ((i, j), mesh) in meshes)
        {
            var max = layout.Max(i, j);
            if (meshes.TryGetValue((i + 1, j), out var right))
                Assert.Equal(Border(mesh, v => v.X == max.X), Border(right, v => v.X == max.X));
            if (meshes.TryGetValue((i, j + 1), out var above))
                Assert.Equal(Border(mesh, v => v.Z == max.Y), Border(above, v => v.Z == max.Y));
            foreach (var vertex in UpperVertices(mesh))
                Assert.Equal(terrain.Height(vertex.X, vertex.Z), vertex.Y, 4);
        }
        var before = meshes.Values.Sum(m => m.TriangleCount);
        var others = Enumerable.Range(0, layout.CountZ).SelectMany(j => Enumerable.Range(0, layout.CountX).Select(i => GroundMesher.Mesh(terrain, layout, i, j, greedy)!)).ToArray();
        var other = others.Sum(m => m.TriangleCount);
        // The bound is the distance along the faces' normals; MaxError is measured straight up, which on a steep hill is more. The
        // batched rounds must keep to the same as, or better than, the greedy ones did
        Console.WriteLine($"ground {cells} cells, {maxError} m: {before} triangles batched (worst {meshes.Values.Max(m => m.MaxError) * 1000:0.0} mm), {other} greedy (worst {others.Max(m => m.MaxError) * 1000:0.0} mm)");
        Assert.True(meshes.Values.Max(m => m.MaxError) <= others.Max(m => m.MaxError) * 1.05f + 1e-4f);

        static Vector3[] Border(GroundMesh mesh, Func<Vector3, bool> on) =>
            UpperVertices(mesh).Where(on).OrderBy(v => v.X).ThenBy(v => v.Z).ToArray();
    }

    [Fact]
    public void Table_TrianglesAgainstError_BatchedAndGreedy()
    {
        var terrain = new LevelTerrain(Small);
        var layout = GroundLayout.Around(terrain, 25);
        foreach (var (cells, error) in new[] { (50, 0.008f), (25, 0.03f), (12, 0.1f) })
            foreach (var factor in new[] { 1f, 2f, 3f, 4f })
            {
                var batched = Enumerable.Range(0, layout.CountZ).SelectMany(j => Enumerable.Range(0, layout.CountX).Select(i => GroundMesher.Mesh(terrain, layout, i, j, new GroundDetail(cells, error * factor, true))!)).ToArray();
                Console.WriteLine($"table {cells} cells bound {error * factor * 1000:0.0} mm: batched {batched.Sum(m => m.TriangleCount)} triangles, worst {batched.Max(m => m.MaxError) * 1000:0.0} mm");
            }
    }
}
