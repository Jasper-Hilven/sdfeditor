using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Rendering;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>Ticket 04: the trees sections, the island's rim and its underside.</summary>
public class LevelTreesTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static readonly LevelDefinition Small = new(
        new PlayArea(new Vector2(40, 30), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4));

    private static readonly ModelFootprint Rock = new(new Vector2(-1, -1.5f), new Vector2(1, 1.5f));

    private static readonly DrawGroup Foliage = new("foliage", [6, 15, 35], 1, 1, 1, 1, 5, 0.04f, 0.5f, 0.08f, 0.5f, 0.7f, 1);
    private static readonly DrawGroup Trees = new("trees", [25, 60, 1000], 3, 2, 1, 2, 0, 0.06f, 5, 0, 1, 0.7f, 1);

    private static ScatterLayer Grass => new([new ScatterEntry("turf", 1, 2)], "foliage", 0.5f, 1, 6, 0.5f, [], 0, 90, 3,
        ScatterLayer.DefaultScale, 1);

    private static ScatterLayer Front(Vector2 band, float density = 0.05f, float spacing = 3) =>
        new([new ScatterEntry("tree0", 2, 2), new ScatterEntry("pine0", 1, 3)], "trees", density, 1, 8, spacing, [], 0, 90, 0,
            ScatterLayer.DefaultScale, 1) { Band = band };

    private static ScatteredLevel Scatter(LevelDefinition level)
    {
        var terrain = new LevelTerrain(level);
        return LevelScatter.Scatter(level, terrain, new GroundRegions(level, _ => Rock), _ => Rock, _ => Vector3.One);
    }

    [Fact]
    public void Read_TakesTreesSections_InGroupTreesUnlessTold_WithTheirBand()
    {
        var level = LevelFile.Read(
            """
            format 1
            play size=(150, 100)
            edge
                margin=6 rim=3 shoulder=2.5
                underside island-underside
            trees
                tree0 weight=2
                pine0
                band=(12, 28) density=0.012 spacing=6 scale=(0.95, 1.4) maxSlope=35
            trees group=clusters
                cluster0
                band=(-3, 6) density=0.05 spacing=3.5 avoid=dirt margin=2
            draw trees rings=[25, 60, 1000] outline=3 shadows=2 lowShadows=1
            draw clusters rings=[45, 1000] outline=2
            """);

        Assert.Empty(level.Scatters);
        Assert.Equal(2, level.Trees.Count);
        var front = level.Trees[0];
        Assert.Equal([new ScatterEntry("tree0", 2, 7), new ScatterEntry("pine0", 1, 8)], front.Entries);
        Assert.Equal(("trees", 0.012f, 6f, new Vector2(12, 28)), (front.Group, front.Density, front.Spacing, front.Band));
        Assert.Equal((new Vector2(0.95f, 1.4f), 35f), (front.Scale, front.MaxSlope));
        var clusters = level.Trees[1];
        Assert.Equal(("clusters", new Vector2(-3, 6), 2f), (clusters.Group, clusters.Band, clusters.Margin));
        Assert.Equal(["dirt"], clusters.Avoid);

        Assert.Equal(6, level.Edge.Margin);
        Assert.Equal((3f, 2.5f), (level.Edge.Rim, level.Edge.Shoulder));
        Assert.Equal(new IslandUnderside("island-underside", 5), level.Edge.Underside);
    }

    [Theory]
    [InlineData("trees density=1 spacing=1\n    tree0", 3, "Trees section is missing parameter 'band'")]
    [InlineData("trees density=1 spacing=1 band=(5, 5)\n    tree0", 3, "The band needs its least distance first")]
    [InlineData("trees density=1 spacing=1 band=(0, 5) reach=3\n    tree0", 3, "Trees section has no parameter 'reach'")]
    [InlineData("trees density=1 spacing=1 band=(0, 5)", 3, "The trees section needs the models it places indented below it")]
    [InlineData("trees density=1 spacing=1 band=(0, 5)\n    tree0\n    tree0", 5, "Model 'tree0' is given twice in this trees section")]
    [InlineData("trees density=1 spacing=1 band=(0, 5) group=clusters\n    tree0", 3, "The trees section draws in group 'clusters', which has no 'draw clusters' section")]
    [InlineData("edge margin=6 shoulder=2", 3, "A shoulder bends the ground down to a rim")]
    [InlineData("edge margin=1.5 rim=3", 3, "The shoulder (2 m) is wider than the margin behind the crest (1.5 m)")]
    [InlineData("edge margin=6\n    underside island-underside", 4, "The underside meets the ground along the rim")]
    [InlineData("edge margin=6 rim=3\n    underside a\n    underside b", 5, "The edge's underside is given twice, first on line 4")]
    [InlineData("edge margin=6 rim=3\n    underside", 4, "Expected the underside's model")]
    public void Read_ExplainsWhatIsWrongWithTreesOrTheEdge(string sections, int line, string problem)
    {
        var text = $"format 1\nplay size=(1, 1)\n{sections}\ndraw trees rings=[6]\n";

        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Rim_BendsTheLastShoulderDownToItsHeight_AllRound_AndLeavesTheRestAlone()
    {
        var plain = new LevelTerrain(Small);
        var rimmed = new LevelTerrain(Small with { Edge = Small.Edge with { Rim = 2, Shoulder = 1.5f } });
        var edge = plain.EdgeDistance;

        for (var angle = 0f; angle < 2 * MathF.PI; angle += 0.05f)
        {
            var at = rimmed.OntoEdge(100 * MathF.Cos(angle), 100 * MathF.Sin(angle));
            // Exactly the rim at the edge, whatever the hills' noise
            Assert.Equal(2, rimmed.Height(at.X, at.Y), 4);
            // Untouched before the shoulder, and falling over it to the edge
            var inward = at - Vector2.Normalize(at - new Vector2(Math.Clamp(at.X, -20, 20), Math.Clamp(at.Y, -15, 15)));
            Assert.Equal(plain.Height(at.X - (at.X - inward.X) * 1.6f, at.Y - (at.Y - inward.Y) * 1.6f),
                rimmed.Height(at.X - (at.X - inward.X) * 1.6f, at.Y - (at.Y - inward.Y) * 1.6f));
        }
        Assert.Equal(plain.Height(0, 0), rimmed.Height(0, 0));
        Assert.Equal(edge, rimmed.EdgeDistance);
        Assert.Equal(-3, rimmed.SignedDistance(17, 0), 4);
        Assert.Equal(-15, rimmed.SignedDistance(0, 0), 4);
        Assert.Equal(5, rimmed.SignedDistance(25, 0), 4);
    }

    [Fact]
    public void Trees_StandInTheirBand_OnTheLowestGroundRoundThem_AndLeaveTheScatterAsItWas()
    {
        var grassOnly = Small with { Scatters = [Grass], DrawGroups = [Foliage, Trees] };
        var level = grassOnly with { Trees = [Front(new Vector2(4, 10)), Front(new Vector2(-2, 1), 0.2f, 1.5f)] };
        var terrain = new LevelTerrain(level);

        var scattered = Scatter(level);

        var grass = Scatter(grassOnly);
        Assert.Equal(grass.Instances, scattered.Instances.Take(grass.Instances.Count));
        Assert.Equal(3, scattered.LayerCounts.Count);
        var front = scattered.Instances.Skip(scattered.LayerCounts[0]).Take(scattered.LayerCounts[1]).ToArray();
        var edge = scattered.Instances.Skip(scattered.LayerCounts[0] + scattered.LayerCounts[1]).ToArray();
        Assert.NotEmpty(front);
        Assert.NotEmpty(edge);
        Assert.All(front, i => Assert.InRange(terrain.SignedDistance(i.Position.X, i.Position.Z), 4, 10));
        Assert.All(edge, i => Assert.InRange(terrain.SignedDistance(i.Position.X, i.Position.Z), -2, 1));
        Assert.Contains(edge, i => terrain.SignedDistance(i.Position.X, i.Position.Z) < 0);
        Assert.All(front.Concat(edge), i =>
        {
            Assert.True(i.Position.Y <= terrain.Height(i.Position.X, i.Position.Z));
            Assert.True(i.Position.Y >= terrain.Height(i.Position.X, i.Position.Z) - 1);
        });
        // On the slope the foot goes down to the lowest ground round it
        Assert.Contains(front, i => i.Position.Y < terrain.Height(i.Position.X, i.Position.Z) - 0.05f);
        Assert.Equal(["turf", "tree0", "pine0"], scattered.Prototypes.Select(p => p.Model));
        Assert.Equal(["foliage", "trees", "trees"], scattered.Prototypes.Select(p => p.Group));

        // About the density over the band's area: a ring of the rectangle's perimeter and the corners' quarter circles
        var area = 2 * (40 + 30) * 6 + MathF.PI * (10 * 10 - 4 * 4);
        Assert.InRange(front.Length, 0.6f * 0.05f * area, 1.3f * 0.05f * area);
        Assert.Equal(scattered.Instances, Scatter(level).Instances);
    }

    [Fact]
    public void Bake_WritesTheTreesAndTheEdge_IntoLevelJson()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var level = Small with
            {
                Edge = Small.Edge with { Rim = 1.5f, Shoulder = 2, Underside = new IslandUnderside("underside", 9) },
                Scatters = [Grass],
                Trees = [Front(new Vector2(4, 10))],
                DrawGroups = [Foliage, Trees],
            };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            ModelFootprint? Models(string name) => name is "turf" or "tree0" or "pine0" or "underside" ? Rock : null;

            var baked = LevelBaker.Bake(level, "small", materials, root.FullName, Models);

            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var document = json.RootElement;
            Assert.Equal(["pine0", "tree0", "turf", "underside"], document.GetProperty("models").EnumerateArray().Select(m => m.GetString()));
            var edge = document.GetProperty("edge");
            Assert.Equal((1.5f, 2f, "underside"), (edge.GetProperty("rim").GetSingle(), edge.GetProperty("shoulder").GetSingle(), edge.GetProperty("underside").GetString()));
            var trees = document.GetProperty("instances").GetProperty("trees")[0];
            Assert.Equal("trees", trees.GetProperty("group").GetString());
            Assert.Equal([4f, 10f], trees.GetProperty("band").EnumerateArray().Select(b => b.GetSingle()));
            Assert.Equal(baked.Scattered.LayerCounts[1], trees.GetProperty("count").GetInt32());

            var missing = Assert.Throws<SdfProgramFileException>(() =>
                LevelBaker.Bake(level, "small", materials, root.FullName, name => name == "underside" ? null : Models(name)));
            Assert.Equal(9, missing.Line);
            Assert.Contains("There is no model 'underside'", missing.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Framing_FindsAModelLargerThanTheUsualSearch()
    {
        var program = SdfProgramFile.Read("format 1\nBox size=(110, 2, 85)");

        var bounds = RenderFramer.Frame(program).Bounds;

        Assert.NotNull(bounds);
        Assert.InRange(bounds.Max.X, 110, 125);
        Assert.InRange(bounds.Min.Z, -100, -85);
    }

    [Fact]
    public void Meadow_TreesAndClusters_AreInContent_AndHaveTrunkCapsules()
    {
        var level = LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, "meadow.level"));
        var models = Path.Combine(Content, "models");
        var exists = LevelModels.Exist(models);

        var clusters = level.Trees.Where(t => t.Group == "clusters").SelectMany(t => t.Entries).Select(e => e.Model).Distinct().ToArray();
        Assert.InRange(clusters.Length, 8, 12);
        Assert.All(level.Trees.SelectMany(t => t.Entries), e => Assert.True(exists(e.Model), e.Model));
        foreach (var file in Directory.GetFiles(Path.Combine(models, "trees"), "*.sdf", SearchOption.AllDirectories))
            Assert.Equal(ColliderShape.Capsule, ModelSettings.Load(file).Collider);
        Assert.Equal("island-underside", level.Edge.Underside?.Model);
    }

    [Fact]
    public void Meadow_FieldBetweenTheCastles_HasTreesAndRocks()
    {
        var level = LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, "meadow.level"));
        var models = Path.Combine(Content, "models");
        var exists = LevelModels.Exist(models);

        var field = level.Trees.Where(t => t.Group == "trees" && t.Band is { } b && b.X == -50 && b.Y == -8).ToArray();
        var fieldRocks = level.Trees.Where(t => t.Group == "rocks").ToArray();
        Assert.Single(field);
        Assert.Single(fieldRocks);
        Assert.Contains(level.DrawGroups, g => g.Name == "rocks");
        Assert.All(field.Concat(fieldRocks).SelectMany(t => t.Entries).Select(e => e.Model), m => Assert.True(exists(m), m));
    }

    /// <summary>
    /// The meadow's underside meets its ground with no gap: just past the edge it is there a little above the rim, a metre past it it is
    /// not (a lip, not a shelf), and nowhere further in does it reach up through the ground.
    /// </summary>
    [Fact]
    public void MeadowUnderside_MeetsTheGroundAtTheEdge_AndStaysBelowItInside()
    {
        var level = LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, "meadow.level"));
        var terrain = new LevelTerrain(level);
        var rim = level.Edge.Rim!.Value;
        var program = SdfProgramFile.Load(Path.Combine(Content, "models", "terrain", level.Edge.Underside!.Model + SdfProgramFile.Extension));
        var distance = SdfMeshExporter.DistanceFunction(program);
        var half = level.Play.HalfSize;

        for (var angle = 0f; angle < 2 * MathF.PI; angle += 0.01f)
        {
            var edge = terrain.OntoEdge(1000 * MathF.Cos(angle), 1000 * MathF.Sin(angle));
            var nearest = new Vector2(Math.Clamp(edge.X, -half.X, half.X), Math.Clamp(edge.Y, -half.Y, half.Y));
            var outward = Vector2.Normalize(edge - nearest);
            Vector3 At(float out_, float y) => new(edge.X + outward.X * out_, y, edge.Y + outward.Y * out_);

            Assert.True(distance(At(0.2f, rim + 0.1f)) < 0, $"no underside just past the edge at {edge}");
            Assert.True(distance(At(1, rim + 0.1f)) > 0, $"the underside's lip reaches a metre past the edge at {edge}");
            // Inside the edge, from the shoulder in, the ground is over it with room for both meshes' errors
            for (var inward = 0.6f; inward < 8; inward += 0.4f)
            {
                var p = edge - outward * inward;
                Assert.True(distance(new Vector3(p.X, terrain.Height(p.X, p.Y) - 0.1f, p.Y)) > 0, $"the underside comes up through the ground at {p}");
            }
        }
        for (var x = -half.X - 34; x <= half.X + 34; x += 3.1f)
            for (var z = -half.Y - 34; z <= half.Y + 34; z += 3.1f)
                if (terrain.DistanceOutside(x, z) < terrain.EdgeDistance - 0.6f)
                    Assert.True(distance(new Vector3(x, terrain.Height(x, z) - 0.3f, z)) > 0, $"the underside comes up through the ground at ({x}, {z})");
    }
}
