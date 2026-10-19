using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

public class LevelScatterTests
{
    private static readonly LevelDefinition Small = new(
        new PlayArea(new Vector2(40, 30), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4));

    private static readonly ModelFootprint Rock = new(new Vector2(-1, -1.5f), new Vector2(1, 1.5f));

    private static readonly DrawGroup Foliage = new("foliage", [6, 15, 35], 1, 1, 1, 1, 5, 0.04f, 0.5f, 0.08f, 0.5f, 0.7f, 1);

    private static ScatterLayer Layer(float density, float spacing, float cluster = 1, string[]? avoid = null, float margin = 0, float maxSlope = 90,
        float reach = 3) =>
        new([new ScatterEntry("turf", 3, 2), new ScatterEntry("flower", 1, 3)], "foliage", density, cluster, 6, spacing, avoid ?? [], margin,
            maxSlope, reach, ScatterLayer.DefaultScale, 1);

    private static readonly Vector3 Green = new(0.1f, 0.4f, 0.1f);

    private static ScatteredLevel Scatter(LevelDefinition level)
    {
        var terrain = new LevelTerrain(level);
        var regions = new GroundRegions(level, _ => Rock);
        return LevelScatter.Scatter(level, terrain, regions, _ => Rock, _ => Green);
    }

    [Fact]
    public void Read_TakesScatterLayersAndDrawGroups()
    {
        var level = LevelFile.Read(
            """
            format 1
            play size=(150, 100)
            scatter
                grass-turf0 weight=3
                grass-turf1
                density=0.6 cluster=2 clusterScale=6 spacing=0.45 avoid=dirt,tiles,path margin=0.5 maxSlope=25
            scatter group=rocks
                rock0
                density=0.01 spacing=3 scale=(0.5, 2) reach=20
            draw foliage
                rings=[6, 15, 35] outline=1 shadows=1 fade=5
                wind=0.04 windHeight=0.5 tintHeight=0.08 lowDensity=0.5 lowDistance=0.7
            draw rocks rings=[20, 60]
            """);

        var grass = level.Scatters[0];
        Assert.Equal([new ScatterEntry("grass-turf0", 3, 4), new ScatterEntry("grass-turf1", 1, 5)], grass.Entries);
        Assert.Equal(("foliage", 0.6f, 2f, 6f, 0.45f), (grass.Group, grass.Density, grass.Cluster, grass.ClusterScale, grass.Spacing));
        Assert.Equal(["dirt", "tiles", "path"], grass.Avoid);
        Assert.Equal((0.5f, 25f, ScatterLayer.DefaultReach, ScatterLayer.DefaultScale), (grass.Margin, grass.MaxSlope, grass.Reach, grass.Scale));
        var rocks = level.Scatters[1];
        Assert.Equal(("rocks", new Vector2(0.5f, 2), 20f, ScatterLayer.DefaultCluster), (rocks.Group, rocks.Scale, rocks.Reach, rocks.Cluster));
        Assert.Empty(rocks.Avoid);

        Assert.Equal(["foliage", "rocks"], level.DrawGroups.Select(g => g.Name));
        var foliage = level.DrawGroups[0];
        Assert.Equal([6f, 15f, 35f], foliage.Rings);
        Assert.Equal((1, 1, 1, DrawGroup.DefaultBlend, 5f), (foliage.OutlineRings, foliage.ShadowRings, foliage.LowShadowRings, foliage.Blend, foliage.Fade));
        Assert.Equal((0.04f, 0.5f, 0.08f, 0.5f, 0.7f), (foliage.Wind, foliage.WindHeight, foliage.TintHeight, foliage.LowDensity, foliage.LowDistance));
        // Left out: no outline or shadows, every instance at full distance on the low preset too, no wind or tint
        var group = level.DrawGroups[1];
        Assert.Equal((0, 0, 0, 0f, 0f, 1f, 1f), (group.OutlineRings, group.ShadowRings, group.LowShadowRings, group.Wind, group.TintHeight, group.LowDensity, group.LowDistance));
    }

    [Theory]
    [InlineData("scatter\n    turf\n    density=1", 3, "Scatter is missing parameter 'spacing'")]
    [InlineData("scatter density=1 spacing=1", 3, "The scatter needs the models it scatters indented below it")]
    [InlineData("scatter density=1 spacing=1\n    turf weight=0", 3, "needs a model with a weight above 0")]
    [InlineData("scatter density=1 spacing=1\n    turf\n    turf", 5, "Model 'turf' is given twice in this scatter, first on line 4")]
    [InlineData("scatter density=1 spacing=1\n    turf size=2", 4, "Scatter entry turf has no parameter 'size'; its parameters are weight")]
    [InlineData("scatter density=1 spacing=1 avoid=(1, 2)\n    turf", 3, "'avoid' of scatter needs regions like dirt,tiles,path")]
    [InlineData("scatter density=1 spacing=1 scale=(2, 1)\n    turf", 3, "the least first")]
    [InlineData("scatter density=1 spacing=1 group=trees\n    turf", 3, "The scatter draws in group 'trees', which has no 'draw trees' section")]
    [InlineData("draw foliage", 3, "Draw group foliage is missing parameter 'rings'")]
    [InlineData("draw foliage rings=[6, 6]", 3, "each further than the one before")]
    [InlineData("draw foliage rings=[6, 15] outline=3", 3, "'outline' of draw group foliage needs how many of the nearest rings get the outline, 0 to 2")]
    [InlineData("draw foliage rings=[6, 15] shadows=0.5", 3, "'shadows' of draw group foliage needs")]
    [InlineData("draw foliage rings=[6, 15] fade=20", 3, "'fade' of draw group foliage is 20, but needs metres over which the last ring fades out, 0 to 15")]
    [InlineData("draw foliage rings=[6]\ndraw foliage rings=[8]", 4, "Draw group 'foliage' is given twice, first on line 3")]
    public void Read_ExplainsWhatIsWrongWithAScatterOrDrawGroup(string sections, int line, string problem)
    {
        var text = $"format 1\nplay size=(1, 1)\n{sections}\n";
        if (!sections.StartsWith("draw") && !sections.Contains("group="))
            text += "draw foliage rings=[6]\n";

        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void PoissonDisk_KeepsItsSpacing_AndLeavesNoGapAnotherPointWouldFit()
    {
        var points = LevelScatter.PoissonDisk(Vector2.Zero, new Vector2(20, 10), 0.5f, new Random32(1, 2));

        // A grid of cells half the spacing wide: no two points share one, and nearest neighbours are at least the spacing apart
        var cells = new HashSet<(int, int)>();
        foreach (var p in points)
            Assert.True(cells.Add(((int)(p.X / 0.35f), (int)(p.Y / 0.35f))));
        for (var i = 0; i < points.Count; i += 37)
            Assert.True(points.Where((_, j) => j != i).Min(q => Vector2.Distance(points[i], q)) >= 0.5f - 1e-5f);
        // Maximal: every probe has a point within two spacings
        for (var x = 0.5f; x < 19.5f; x += 1.3f)
            for (var y = 0.5f; y < 9.5f; y += 1.1f)
                Assert.Contains(points, p => Vector2.Distance(p, new Vector2(x, y)) < 1);
        Assert.Equal(points, LevelScatter.PoissonDisk(Vector2.Zero, new Vector2(20, 10), 0.5f, new Random32(1, 2)));
    }

    [Fact]
    public void Scatter_IsTheSameEveryTime_AndChangesWithTheSeed()
    {
        var level = Small with { Scatters = [Layer(0.5f, 0.5f, cluster: 2)], DrawGroups = [Foliage] };

        var first = Scatter(level);

        Assert.Equal(first.Instances, Scatter(level).Instances);
        Assert.NotEqual(first.Instances, Scatter(level with { Seed = 4 }).Instances);
        Assert.Equal([new ScatterPrototype("turf", "foliage"), new ScatterPrototype("flower", "foliage")], first.Prototypes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Scatter_KeepsTheDensityOnAverage_WithOrWithoutClusters(float cluster)
    {
        var level = Small with { Scatters = [Layer(0.6f, 0.45f, cluster, reach: 0)], DrawGroups = [Foliage] };

        var scattered = Scatter(level);

        // The playable rectangle is all there is to scatter on
        var expected = 0.6f * 40 * 30;
        Assert.InRange(scattered.Instances.Count, expected * 0.9f, expected * 1.1f);
        Assert.Equal(scattered.Instances.Count, scattered.LayerCounts.Single());
        // Picked by weight, 3 to 1; turned and scaled within the layer's range
        var turfs = scattered.Instances.Count(i => i.Prototype == 0);
        Assert.InRange(turfs / (float)scattered.Instances.Count, 0.7f, 0.8f);
        Assert.All(scattered.Instances, i => Assert.InRange(i.Scale, 0.85f, 1.15f));
        Assert.All(scattered.Instances, i => Assert.InRange(i.Yaw, -180, 180));
    }

    [Fact]
    public void Scatter_Clusters_DenserInPlacesAndEmptyInOthers()
    {
        var even = Scatter(Small with { Scatters = [Layer(0.6f, 0.45f, reach: 0)], DrawGroups = [Foliage] });
        var clustered = Scatter(Small with { Scatters = [Layer(0.6f, 0.45f, cluster: 3, reach: 0)], DrawGroups = [Foliage] });

        // Counted in 4 m squares: the clustered scatter has emptier and fuller squares than the even one
        static int[] Counts(ScatteredLevel scattered) => Enumerable.Range(0, 70)
            .Select(c => scattered.Instances.Count(i => (int)((i.Position.X + 20) / 4) + 10 * (int)((i.Position.Z + 15) / 4) == c)).ToArray();
        var (evenCounts, clusteredCounts) = (Counts(even), Counts(clustered));
        Assert.True(clusteredCounts.Max() > evenCounts.Max() * 1.5f);
        Assert.True(clusteredCounts.Count(c => c < 2) > evenCounts.Count(c => c < 2) + 5);
    }

    [Fact]
    public void Scatter_KeepsOffAvoidedRegions_Placements_SteepGroundAndTheHills()
    {
        var level = Small with
        {
            Regions = [new GroundRegion("dirt", [new CircleRegion(new Vector2(-10, 0), 4, 1, 3)], GroundPattern.Plain, 0.6f, 0.05f)],
            Placements =
            [
                new Placement("rock", new Vector2(8, 5), 30, 2, 0, 3, null, 1, 1),
                new Placement("castle", new Vector2(10, -8), 0, 1, 3, 3, null, 1, 2),
            ],
            Scatters = [Layer(1.5f, 0.4f, avoid: ["dirt"], margin: 0.5f, maxSlope: 25, reach: 2)],
            DrawGroups = [Foliage],
        };
        var terrain = new LevelTerrain(level);
        var regions = new GroundRegions(level, _ => Rock);

        var scattered = Scatter(level);

        Assert.NotEmpty(scattered.Instances);
        foreach (var instance in scattered.Instances)
        {
            var p = new Vector2(instance.Position.X, instance.Position.Z);
            Assert.True(regions.Distance(0, p) >= 0.5f);
            // The rock's footprint, 4 × 6 m at scale 2, turned 30°, grown by the margin
            var local = Placement2D.ToLocal(p - new Vector2(8, 5), 30) / 2;
            Assert.False(MathF.Abs(local.X) < 1.25f && MathF.Abs(local.Y) < 1.75f);
            Assert.True(Vector2.Distance(p, new Vector2(10, -8)) >= 3.5f);
            Assert.True(terrain.DistanceOutside(p.X, p.Y) <= 2);
            Assert.True(terrain.Normal(p.X, p.Y).Y >= MathF.Cos(25 * MathF.PI / 180));
            Assert.Equal(terrain.Height(p.X, p.Y), instance.Position.Y);
        }
        // Up to the reach, where the hills are still gentle
        Assert.Contains(scattered.Instances, i => terrain.DistanceOutside(i.Position.X, i.Position.Z) > 1);
    }

    [Fact]
    public void Scatter_NamesTheLineOfAnAvoidedRegionThatIsNotThere()
    {
        var level = Small with { Scatters = [Layer(1, 1, avoid: ["swamp"]) with { Line = 9 }], DrawGroups = [Foliage] };

        var exception = Assert.Throws<SdfProgramFileException>(() => Scatter(level));

        Assert.Equal(9, exception.Line);
        Assert.Contains("avoids 'swamp', which is not a region", exception.Problem);
    }

    [Fact]
    public void Srgba_PacksTheTintAsSrgbBytes()
    {
        Assert.Equal(0xFF000000u, LevelScatter.Srgba(Vector3.Zero));
        Assert.Equal(0xFFFFFFFFu, LevelScatter.Srgba(Vector3.One));
        // Linear 0.0513 is sRGB 0.25
        Assert.Equal(0xFF0000_40u, LevelScatter.Srgba(new Vector3(0.0513f, 0, 0)));
    }

    [Fact]
    public void Bake_WritesInstancesSortedByCell_ByteIdenticalEveryTime_AndListsTheScatteredModels()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var level = Small with
            {
                Placements = [new Placement("rock", new Vector2(8, 5), 30, 1, 0, 3, null, 1, 1)],
                Scatters = [Layer(0.3f, 0.5f, cluster: 2)],
                DrawGroups = [Foliage, Foliage with { Name = "trees" }],
            };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            ModelFootprint? Models(string name) => name is "rock" or "turf" or "flower" ? Rock : null;

            var first = LevelBaker.Bake(level, "small", materials, Path.Combine(root.FullName, "a"), Models);
            LevelBaker.Bake(level, "small", materials, Path.Combine(root.FullName, "b"), Models);

            var bytes = File.ReadAllBytes(Path.Combine(root.FullName, "a", InstanceFile.FileName));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root.FullName, "b", InstanceFile.FileName)));
            Assert.Equal("SDFI"u8.ToArray(), bytes[..4]);
            var records = InstanceFile.ReadRecords(bytes, out var cellsX, out var cellsZ, out var cells);
            Assert.Equal(first.Scattered.Instances.Count, records.Count);
            Assert.Equal(first.Scattered.Instances.OrderBy(i => i.Hash), records.OrderBy(i => i.Hash));
            // Every record in its cell, the cells one after another
            var origin = -new LevelTerrain(level).HalfExtent;
            var next = 0;
            for (var cell = 0; cell < cells.Length; cell++)
            {
                Assert.Equal(next, cells[cell].First);
                next += cells[cell].Count;
                for (var r = cells[cell].First; r < cells[cell].First + cells[cell].Count; r++)
                {
                    var i = (int)MathF.Floor((records[r].Position.X - origin.X) / InstanceFile.CellSize);
                    var j = (int)MathF.Floor((records[r].Position.Z - origin.Y) / InstanceFile.CellSize);
                    Assert.Equal(cell, Math.Clamp(j, 0, cellsZ - 1) * cellsX + Math.Clamp(i, 0, cellsX - 1));
                }
            }
            Assert.Equal(records.Count, next);

            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "a", LevelBaker.LevelJson)));
            var document = json.RootElement;
            Assert.Equal(["flower", "rock", "turf"], document.GetProperty("models").EnumerateArray().Select(m => m.GetString()));
            var instances = document.GetProperty("instances");
            Assert.Equal(records.Count, instances.GetProperty("count").GetInt32());
            Assert.Equal(["turf", "flower"], instances.GetProperty("prototypes").EnumerateArray().Select(p => p.GetProperty("model").GetString()));
            Assert.Equal(["foliage", "trees"], document.GetProperty("drawGroups").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
            Assert.Equal([6f, 15f, 35f], document.GetProperty("drawGroups")[0].GetProperty("rings").EnumerateArray().Select(r => r.GetSingle()));

            // Nothing scattered any more: the file goes, and level.json has no instances
            LevelBaker.Bake(level with { Scatters = [] }, "small", materials, Path.Combine(root.FullName, "a"), Models);
            Assert.False(File.Exists(Path.Combine(root.FullName, "a", InstanceFile.FileName)));
            using var without = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "a", LevelBaker.LevelJson)));
            Assert.False(without.RootElement.TryGetProperty("instances", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_NamesTheLineOfAScatteredModelThatIsNotThere()
    {
        var level = Small with { Scatters = [Layer(1, 1)], DrawGroups = [Foliage] };
        var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

        var exception = Assert.Throws<SdfProgramFileException>(() =>
            LevelBaker.Bake(level, "small", materials, Path.GetTempPath(), name => name == "turf" ? Rock : null));

        Assert.Equal(3, exception.Line);
        Assert.Contains("There is no model 'flower'", exception.Problem);
    }
}
