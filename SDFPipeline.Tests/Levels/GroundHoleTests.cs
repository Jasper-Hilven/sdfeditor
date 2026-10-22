using System.Numerics;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>Ticket 28.1: a hole region leaves the ground out.</summary>
public class GroundHoleTests
{
    private const string SinkLevel =
        """
        format 1
        play size=(60, 60) noise=0
        edge margin=4
        regions
            dirt
                Circle center=(-20, -20) radius=5
            sink hole=1
                RoundedBox2D center=(1.3, 3.3) size=(27, 22) radius=2
        """;

    private static readonly Vector2 SinkCenter = new(1.3f, 3.3f);
    private static readonly Vector2 SinkHalf = new(13.5f, 11);

    private static LevelDefinition Level() => LevelFile.Read(SinkLevel);

    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> GroundTriangles(GroundMesh mesh) =>
        Enumerable.Range(0, mesh.TriangleCount - mesh.SkirtTriangles)
            .Select(t => (mesh.Positions[mesh.Triangles[3 * t]], mesh.Positions[mesh.Triangles[3 * t + 1]], mesh.Positions[mesh.Triangles[3 * t + 2]]));

    [Fact]
    public void Read_HoleRegion_IsAHoleAndNoRegion()
    {
        var level = Level();

        Assert.Equal(["dirt"], level.Regions.Select(r => r.Name));
        var hole = Assert.Single(level.Holes);
        Assert.Equal("sink", hole.Name);
        Assert.Equal(7, hole.Line);
        Assert.IsType<BoxRegion>(Assert.Single(hole.Shapes));
    }

    [Fact]
    public void Read_HoleDoesNotCountAgainstTheFourRegions()
    {
        var level = LevelFile.Read(
            "format 1\nplay size=(40, 30)\nregions\n    a\n        Circle center=(0, 0) radius=1\n    b\n        Circle center=(0, 0) radius=1\n" +
            "    c\n        Circle center=(0, 0) radius=1\n    d\n        Circle center=(0, 0) radius=1\n    pit hole=1\n        Circle center=(0, 0) radius=1");

        Assert.Equal(4, level.Regions.Count);
        Assert.Single(level.Holes);
    }

    [Fact]
    public void Read_OnlyAHole_IsFine()
    {
        var level = LevelFile.Read("format 1\nplay size=(40, 30)\nregions\n    pit hole=1\n        Circle center=(0, 0) radius=1");

        Assert.Empty(level.Regions);
        Assert.Single(level.Holes);
    }

    [Theory]
    [InlineData("regions\n    pit hole=1", 4, "needs shapes")]
    [InlineData("regions\n    pit hole=1 pattern=stones\n        Circle center=(0, 0) radius=1", 4, "pattern")]
    [InlineData("regions\n    pit hole=1 wobble=0.1\n        Circle center=(0, 0) radius=1", 4, "wobble")]
    [InlineData("regions\n    pit hole=2\n        Circle center=(0, 0) radius=1", 4, "hole")]
    [InlineData("regions\n    pit hole=1\n        Circle center=(0, 0) radius=1\n    pit\n        Circle center=(0, 0) radius=1", 6, "twice")]
    [InlineData("regions\n    pit hole=1\n        Hexagon center=(0, 0) radius=1", 5, "Hexagon")]
    public void Read_HoleErrors(string regions, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read("format 1\nplay size=(40, 30)\n" + regions));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Mesh_LeavesTheGroundOutInsideAndKeepsItOutside()
    {
        var level = Level();
        var terrain = new LevelTerrain(level);
        var holes = new GroundHoles(level.Holes, level.Seed);
        var layout = GroundLayout.Around(terrain, LevelBaker.ChunkSize);

        foreach (var detail in LevelBaker.Details)
        {
            var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();
            for (var j = 0; j < layout.CountZ; j++)
                for (var i = 0; i < layout.CountX; i++)
                    if (GroundMesher.Mesh(terrain, layout, i, j, detail, holes) is { } mesh)
                        triangles.AddRange(GroundTriangles(mesh));

            // Inside: no vertex is in the hole (the edge's own vertices sit on it) and no triangle's middle, but for the little a chord
            // of a rounded corner (radius 2) cuts off its arc
            var cell = LevelBaker.ChunkSize / detail.Cells;
            var half = MathF.Min(cell * 1.5f / 2, 2); // a chord is at most a cell's diagonal
            var chord = 2 - MathF.Sqrt(4 - half * half) + 0.01f;
            foreach (var (a, b, c) in triangles)
            {
                var middle = (a + b + c) / 3;
                Assert.True(holes.Distance(new Vector2(middle.X, middle.Z)) > -chord, $"A triangle at {middle} is inside the hole");
                foreach (var v in (ReadOnlySpan<Vector3>)[a, b, c])
                    Assert.True(holes.Distance(new Vector2(v.X, v.Z)) > -0.01f, $"A vertex at {v} is inside the hole");
            }
            // The edge is where it was asked for, to a hair (a flat ground and a ruled edge), whatever the cells
            var nearEdge = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Where(v => MathF.Abs(holes.Distance(new Vector2(v.X, v.Z))) < 0.01f).ToArray();
            Assert.NotEmpty(nearEdge);
            foreach (var v in nearEdge)
            {
                var offset = new Vector2(v.X, v.Z) - SinkCenter;
                Assert.InRange(MathF.Abs(offset.X), 0, SinkHalf.X + 0.01f);
                Assert.InRange(MathF.Abs(offset.Y), 0, SinkHalf.Y + 0.01f);
            }
            Assert.Contains(nearEdge, v => MathF.Abs(MathF.Abs(v.X - SinkCenter.X) - SinkHalf.X) < 0.01f);
            Assert.Contains(nearEdge, v => MathF.Abs(MathF.Abs(v.Z - SinkCenter.Y) - SinkHalf.Y) < 0.01f);

            // Outside: the ground is there, a point just beyond the edge is under a triangle, and the area is the island's less the hole's
            foreach (var p in new[] { new Vector2(SinkCenter.X + SinkHalf.X + 0.3f, SinkCenter.Y), new Vector2(SinkCenter.X, SinkCenter.Y - SinkHalf.Y - 0.3f), new Vector2(-20, -20) })
                Assert.True(triangles.Any(t => Covers(t, p)), $"No ground at {p}");
            foreach (var p in new[] { SinkCenter, SinkCenter + new Vector2(10, 8), SinkCenter - new Vector2(12, 9) })
                Assert.False(triangles.Any(t => Covers(t, p)), $"Ground at {p}, in the hole");
        }
    }

    [Fact]
    public void Mesh_ChunkBordersCrossedByTheHoleStillMeetExactly()
    {
        var level = Level();
        var terrain = new LevelTerrain(level);
        var holes = new GroundHoles(level.Holes, level.Seed);
        var layout = GroundLayout.Around(terrain, LevelBaker.ChunkSize);
        // The sink is 27 m across: it crosses at least one border of the 25 m chunks
        var crossed = 0;
        foreach (var detail in LevelBaker.Details)
            for (var j = 0; j < layout.CountZ; j++)
                for (var i = 0; i + 1 < layout.CountX; i++)
                {
                    var left = GroundMesher.Mesh(terrain, layout, i, j, detail, holes);
                    var right = GroundMesher.Mesh(terrain, layout, i + 1, j, detail, holes);
                    if (left is null || right is null)
                        continue;
                    var x = layout.Max(i, j).X;
                    Vector3[] Border(GroundMesh mesh) => GroundTriangles(mesh).SelectMany(t => new[] { t.A, t.B, t.C })
                        .Where(v => v.X == x).Distinct().OrderBy(v => v.Z).ToArray();
                    var (l, r) = (Border(left), Border(right));
                    Assert.Equal(l, r);
                    if (l.Any(v => holes.Distance(new Vector2(v.X, v.Z)) < 0.01f) || l.Length < 3)
                        crossed++;
                }
        Assert.True(crossed > 0, "No chunk border is crossed by the hole");
    }

    [Fact]
    public void Mesh_WithoutHoles_IsTheSameAsBefore()
    {
        var level = LevelFile.Read(SinkLevel.Replace("sink hole=1", "sink").Replace("RoundedBox2D center=(1.3, 3.3) size=(27, 22) radius=2", "RoundedBox2D center=(1.3, 3.3) size=(27, 22) radius=2"));
        var terrain = new LevelTerrain(level);
        var layout = GroundLayout.Around(terrain, LevelBaker.ChunkSize);
        var detail = LevelBaker.Details[0];

        var plain = GroundMesher.Mesh(terrain, layout, 1, 1, detail)!;
        var none = GroundMesher.Mesh(terrain, layout, 1, 1, detail, GroundHoles.None)!;

        Assert.Equal(plain.Positions, none.Positions);
        Assert.Equal(plain.Triangles, none.Triangles);
    }

    [Fact]
    public void Bake_SkipsChunksInsideTheHole_AndLeavesTheRegionMapAlone()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default).With("ground_dirt", MaterialDefinition.Default);
            var with = Level();
            var without = with with { Holes = [] };

            var holed = LevelBaker.Bake(with, "a", materials, Path.Combine(root.FullName, "a"));
            var solid = LevelBaker.Bake(without, "b", materials, Path.Combine(root.FullName, "b"));

            Assert.True(holed.Triangles[0] < solid.Triangles[0]);
            // Not a region: one channel (dirt) in both bakes, the same map
            Assert.Equal(File.ReadAllBytes(Path.Combine(root.FullName, "b", RegionMap.FileName)), File.ReadAllBytes(Path.Combine(root.FullName, "a", RegionMap.FileName)));
            Assert.Equal(["dirt"], holed.Regions.Select(r => r.Name));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void Scatter_KeepsClearOfAHole()
    {
        var level = LevelFile.Read(
            SinkLevel + "\nscatter\n    grass-turf0\n    density=2 spacing=0.5 margin=1\ndraw foliage\n    rings=[6, 15, 35]\n");
        var terrain = new LevelTerrain(level);
        var holes = new GroundHoles(level.Holes, level.Seed);
        var footprint = new ModelFootprint(new Vector2(-0.1f), new Vector2(0.1f));

        var scattered = LevelScatter.Scatter(level, terrain, new GroundRegions(level, _ => footprint), _ => footprint, _ => Vector3.One, holes);

        Assert.NotEmpty(scattered.Instances);
        foreach (var instance in scattered.Instances)
            Assert.True(holes.Distance(new Vector2(instance.Position.X, instance.Position.Z)) >= 1 - 1e-3f);
    }

    private static bool Covers((Vector3 A, Vector3 B, Vector3 C) t, Vector2 p)
    {
        float Side(Vector3 u, Vector3 v) => (v.X - u.X) * (p.Y - u.Z) - (v.Z - u.Z) * (p.X - u.X);
        var (s1, s2, s3) = (Side(t.A, t.B), Side(t.B, t.C), Side(t.C, t.A));
        return (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
    }
}
