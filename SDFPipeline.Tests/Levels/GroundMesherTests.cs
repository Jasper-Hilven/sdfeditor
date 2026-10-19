using System.Numerics;
using SDFPipeline.Levels;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

public class GroundMesherTests
{
    /// <summary>A smaller meadow, so the tests run quickly: 3 × 2 chunks of 25 m with hills, edge and noise.</summary>
    private static readonly LevelDefinition Small = new(
        new PlayArea(new Vector2(20, 10), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4));

    private static IEnumerable<Vector3> UpperVertices(GroundMesh mesh) =>
        mesh.Triangles.Take(mesh.Triangles.Length - 3 * mesh.SkirtTriangles).Select(v => mesh.Positions[v]).Distinct();

    [Fact]
    public void Layout_CoversTheIslandWithWholeChunks()
    {
        var layout = GroundLayout.Around(new LevelTerrain(Small), 25);

        Assert.Equal((3, 2), (layout.CountX, layout.CountZ));
        Assert.Equal(new Vector2(-37.5f, -25), layout.Origin);
    }

    [Theory]
    [InlineData(50, 0.008f)]
    [InlineData(25, 0.03f)]
    [InlineData(12, 0.1f)]
    public void Neighbours_ShareEveryBorderVertex(int cells, float maxError)
    {
        var terrain = new LevelTerrain(Small);
        var layout = GroundLayout.Around(terrain, 25);
        var detail = new GroundDetail(cells, maxError);
        var meshes = new Dictionary<(int, int), GroundMesh>();
        for (var j = 0; j < layout.CountZ; j++)
            for (var i = 0; i < layout.CountX; i++)
                meshes[(i, j)] = GroundMesher.Mesh(terrain, layout, i, j, detail)!;

        foreach (var ((i, j), mesh) in meshes)
        {
            var max = layout.Max(i, j);
            if (meshes.TryGetValue((i + 1, j), out var right))
                Assert.Equal(Border(mesh, v => v.X == max.X), Border(right, v => v.X == max.X));
            if (meshes.TryGetValue((i, j + 1), out var above))
                Assert.Equal(Border(mesh, v => v.Z == max.Y), Border(above, v => v.Z == max.Y));
        }

        static Vector3[] Border(GroundMesh mesh, Func<Vector3, bool> on) =>
            UpperVertices(mesh).Where(on).OrderBy(v => v.X).ThenBy(v => v.Z).ToArray();
    }

    [Fact]
    public void Mesh_FollowsTheHeightAndFacesUp()
    {
        var terrain = new LevelTerrain(Small);
        var layout = GroundLayout.Around(terrain, 25);

        var mesh = GroundMesher.Mesh(terrain, layout, 1, 1, new GroundDetail(50, 0.008f))!;

        Assert.InRange(mesh.MaxError, 0, 0.12f);
        foreach (var vertex in UpperVertices(mesh))
            Assert.Equal(terrain.Height(vertex.X, vertex.Z), vertex.Y, 4);
        var upper = mesh.TriangleCount - mesh.SkirtTriangles;
        for (var t = 0; t < upper; t++)
        {
            var (a, b, c) = (mesh.Positions[mesh.Triangles[3 * t]], mesh.Positions[mesh.Triangles[3 * t + 1]], mesh.Positions[mesh.Triangles[3 * t + 2]]);
            Assert.True(Vector3.Cross(b - a, c - a).Y > 0, $"Triangle {t} faces down");
        }
        // Simplified well below the grid's 5,000 triangles, skirts included
        Assert.InRange(mesh.TriangleCount, 200, 2500);
    }

    [Fact]
    public void Skirts_HangFromTheChunkBordersFacingOut()
    {
        var terrain = new LevelTerrain(Small);
        var layout = GroundLayout.Around(terrain, 25);
        var (min, max) = (layout.Min(1, 0), layout.Max(1, 0));

        var mesh = GroundMesher.Mesh(terrain, layout, 1, 0, new GroundDetail(25, 0.03f))!;

        Assert.True(mesh.SkirtTriangles > 0);
        var centre = (min + max) / 2;
        for (var t = mesh.TriangleCount - mesh.SkirtTriangles; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = (mesh.Positions[mesh.Triangles[3 * t]], mesh.Positions[mesh.Triangles[3 * t + 1]], mesh.Positions[mesh.Triangles[3 * t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            var middle = (a + b + c) / 3;
            Assert.True(MathF.Abs(normal.Y) < 1e-3f * normal.Length(), "A skirt is not upright");
            Assert.True(Vector2.Dot(new Vector2(normal.X, normal.Z), new Vector2(middle.X, middle.Z) - centre) > 0, "A skirt faces into the chunk");
        }
    }

    [Fact]
    public void Mesh_EndsOnTheIslandsEdge()
    {
        var terrain = new LevelTerrain(Small);
        var layout = GroundLayout.Around(terrain, 25);

        var mesh = GroundMesher.Mesh(terrain, layout, 0, 0, new GroundDetail(50, 0.008f))!;

        Assert.All(UpperVertices(mesh), v => Assert.InRange(terrain.DistanceOutside(v.X, v.Z), 0, terrain.EdgeDistance + 1e-3f));
        Assert.Contains(UpperVertices(mesh), v => MathF.Abs(terrain.DistanceOutside(v.X, v.Z) - terrain.EdgeDistance) < 1e-3f);
    }

    [Fact]
    public void Bake_IsByteIdenticalEveryTime()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var first = LevelBaker.Bake(Small, "small", materials, Path.Combine(root.FullName, "a"));
            LevelBaker.Bake(Small, "small", materials, Path.Combine(root.FullName, "b"));

            Assert.Equal(6, first.ChunkFiles.Count);
            foreach (var file in first.ChunkFiles.Append(LevelBaker.LevelJson))
                Assert.Equal(File.ReadAllBytes(Path.Combine(root.FullName, "a", file)), File.ReadAllBytes(Path.Combine(root.FullName, "b", file)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_WithoutTheGrassMaterial_SaysWhichFileIsMissing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LevelBaker.Bake(Small, "small", MaterialLibrary.Empty, Path.GetTempPath()));

        Assert.Contains("materials/ground/ground_grass.sdfmat", exception.Message);
    }
}
