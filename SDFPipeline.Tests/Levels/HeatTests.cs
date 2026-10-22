using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>Ticket 28.10: the <c>heat</c> section of a level file (rings that heat in turns), what the bake writes for Unity's HeatRules, and the kitchen's hob.</summary>
public class HeatTests
{
    private const string Header = "format 1\nplay size=(150, 100) noise=0\n";

    private const string Heat =
        "heat cycle=45 first=20 warm=3 hot=12 cool=3 damage=10 cost=50 name=\"the hob\"\n" +
        "    ring group=back at=(-10.5, 3) radius=5.4\n" +
        "    ring group=back at=(10.5, 3) radius=5.4\n" +
        "    ring group=front at=(-10.5, -15) radius=4.2\n" +
        "    ring group=front at=(10.5, -15) radius=4.2\n";

    [Fact]
    public void Read_TakesTheNumbers_AndTheRingsInTheOrderOfTheirGroups()
    {
        var heat = LevelFile.Read(Header + Heat).Heat!;

        Assert.Equal((45f, 20f, 3f, 12f, 3f), (heat.Cycle, heat.First, heat.Warm, heat.Hot, heat.Cool));
        Assert.Equal((10f, 50f, "the hob"), (heat.Damage, heat.Cost, heat.Cause));
        Assert.Equal(["back", "front"], heat.Groups);
        Assert.Equal(4, heat.Rings.Count);
        Assert.Equal(new HeatRing("back", new Vector2(-10.5f, 3), 5.4f, 4), heat.Rings[0]);
        Assert.Equal(18f, heat.Span);
        Assert.Equal(Header.Count(c => c == '\n') + 1, heat.Line);
    }

    [Fact]
    public void Read_GivesTheHeatLineItsDefaults()
    {
        var heat = LevelFile.Read(Header + "heat\n    ring group=a at=(0, 0) radius=3\n").Heat!;

        Assert.Equal((45f, 20f, 3f, 12f, 3f, 10f, 50f, "the heat"), (heat.Cycle, heat.First, heat.Warm, heat.Hot, heat.Cool, heat.Damage, heat.Cost, heat.Cause));
        Assert.Equal(["a"], heat.Groups);
    }

    [Fact]
    public void ALevelWithoutHeat_HasNone() => Assert.Null(LevelFile.Read(Header).Heat);

    [Theory]
    [InlineData("heat wind=3\n    ring group=a at=(0, 0) radius=3\n", "no parameter 'wind'")]
    [InlineData("heat damage=-1\n    ring group=a at=(0, 0) radius=3\n", "'damage'")]
    [InlineData("heat cost=0\n    ring group=a at=(0, 0) radius=3\n", "'cost'")]
    [InlineData("heat\n", "needs one or more rings")]
    [InlineData("heat\n    pit group=a at=(0, 0) radius=3\n", "there is no 'pit'")]
    [InlineData("heat\n    ring at=(0, 0) radius=3\n", "needs 'group'")]
    [InlineData("heat\n    ring group=a radius=3\n", "missing parameter 'at'")]
    [InlineData("heat\n    ring group=a at=(0, 0)\n", "missing parameter 'radius'")]
    [InlineData("heat\n    ring group=a at=(0, 0) radius=3 colour=1\n", "no parameter 'colour'")]
    [InlineData("heat cycle=30\n    ring group=a at=(0, 0) radius=3\n    ring group=b at=(9, 0) radius=3\n", "more than the cycle")]
    [InlineData("heat\n    ring group=a at=(0, 0) radius=3\n    ring group=b at=(9, 0) radius=3\n    ring group=c at=(18, 0) radius=3\n", "more than the cycle")]
    [InlineData("heat cycle=100\n    ring group=a at=(0, 0) radius=3\n    ring group=b at=(9, 0) radius=3\n    ring group=c at=(18, 0) radius=3\n" +
                "    ring group=d at=(27, 0) radius=3\n    ring group=e at=(36, 0) radius=3\n", "at most 4")]
    public void Read_RejectsAHeatSectionThatIsNotOne(string section, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + section));

        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_RejectsAHeatSectionTwice()
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Heat + Heat));

        Assert.Contains("given twice", exception.Problem);
    }

    [Fact]
    public void Bake_WritesTheHeat_AndNothingForALevelWithout()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-heat-");
        try
        {
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var edge = "hills\n    ring width=12 height=6\nedge\n    margin=4\n";

            JsonElement Bake(string text)
            {
                LevelBaker.Bake(LevelFile.Read(text), "heat", materials, root.FullName, modelExists: _ => true);
                using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
                return json.RootElement.Clone();
            }

            var baked = Bake(Header + edge + Heat).GetProperty("heat");
            Assert.Equal(45f, baked.GetProperty("cycle").GetSingle());
            Assert.Equal(20f, baked.GetProperty("first").GetSingle());
            Assert.Equal(3f, baked.GetProperty("warm").GetSingle());
            Assert.Equal(12f, baked.GetProperty("hot").GetSingle());
            Assert.Equal(3f, baked.GetProperty("cool").GetSingle());
            Assert.Equal(10f, baked.GetProperty("damage").GetSingle());
            Assert.Equal(50f, baked.GetProperty("cost").GetSingle());
            Assert.Equal("the hob", baked.GetProperty("cause").GetString());
            Assert.Equal(["back", "front"], baked.GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
            var rings = baked.GetProperty("rings");
            Assert.Equal(4, rings.GetArrayLength());
            Assert.Equal((0, "back", -10.5f, 3f, 5.4f), (rings[0].GetProperty("group").GetInt32(), rings[0].GetProperty("name").GetString(),
                rings[0].GetProperty("at")[0].GetSingle(), rings[0].GetProperty("at")[1].GetSingle(), rings[0].GetProperty("radius").GetSingle()));
            Assert.Equal(1, rings[3].GetProperty("group").GetInt32());
            Assert.Equal(0f, rings[0].GetProperty("y").GetSingle(), 0.5f);

            // Other levels bake unchanged: no heat key
            Assert.False(Bake(Header + edge).TryGetProperty("heat", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_RejectsARingOffTheIsland()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-heat-");
        try
        {
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var level = LevelFile.Read(Header + "hills\n    ring width=12 height=6\nedge\n    margin=4\nheat\n    ring group=a at=(400, 0) radius=3\n");

            var off = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(level, "heat", materials, root.FullName, modelExists: _ => true));
            Assert.Contains("beyond the island", off.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static string KitchenText()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return File.ReadAllText(Path.Combine(content, LevelFile.FolderName, "kitchen" + LevelFile.Extension));
    }

    [Fact]
    public void Check_RejectsARingOverASafePlace_OfTheKitchen()
    {
        // The west flag stand is at (-65, 0); a ring round it would burn whoever takes the flag
        var level = LevelFile.Read(KitchenText() + "    ring group=front at=(-65, 0) radius=3\n");

        var covered = Assert.Throws<SdfProgramFileException>(() => HeatBake.Check(level, new LevelTerrain(level)));
        Assert.Contains("covers the flag", covered.Problem);
    }

    [Fact]
    public void ContentKitchen_HasTheHobsFourRings_InTwoMirroredPairs_ClearOfTheKettle()
    {
        var level = LevelFile.Read(KitchenText());

        var heat = level.Heat!;
        Assert.Equal((45f, 20f, 3f, 12f, 3f, 10f, 50f, "the hob"), (heat.Cycle, heat.First, heat.Warm, heat.Hot, heat.Cool, heat.Damage, heat.Cost, heat.Cause));
        // Back pair (the large rings, z = 3) first, then the front pair (the small rings, z = -15)
        Assert.Equal(["back", "front"], heat.Groups);
        Assert.Equal(4, heat.Rings.Count);
        foreach (var ring in heat.Rings)
        {
            var mirror = heat.Rings.Single(r => r.At == new Vector2(-ring.At.X, ring.At.Y));
            Assert.Equal((ring.Group, ring.Radius), (mirror.Group, mirror.Radius));
            // A burner stands at the ring's centre, and the ring is its outer radius (burner-large 5.4 m, burner-small 4.2 m)
            var burner = level.Placements.Single(p => p.Model.StartsWith("burner") && p.At == ring.At);
            Assert.Equal(ring.Group == "back" ? "burner-large" : "burner-small", burner.Model);
            Assert.Equal(ring.Group == "back" ? 5.4f : 4.2f, ring.Radius);
        }
        Assert.Equal([3f, 3f], heat.Rings.Where(r => r.Group == "back").Select(r => r.At.Y));
        Assert.Equal([-15f, -15f], heat.Rings.Where(r => r.Group == "front").Select(r => r.At.Y));
        // The kettle is never on a ring, and no ring touches another or the sink
        var kettle = level.Placements.Single(p => p.Model == "kettle0");
        Assert.All(heat.Rings, r => Assert.True(Vector2.Distance(r.At, kettle.At) > r.Radius + 4.7f, $"the kettle ({kettle.At}) is too near the ring at {r.At}"));
        foreach (var a in heat.Rings)
        foreach (var b in heat.Rings.Where(b => b != a))
            Assert.True(Vector2.Distance(a.At, b.At) > a.Radius + b.Radius, $"the rings at {a.At} and {b.At} touch");
        // Nobody spawns or fetches anything on a hot ring
        HeatBake.Check(level, new LevelTerrain(level));
    }
}
