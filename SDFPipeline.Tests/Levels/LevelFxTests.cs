using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.Materials;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>The level file's fog, steam and drip lines (ticket 29.1).</summary>
public class LevelFxTests
{
    private const string Base = "format 1\nplay size=(150, 100) noise=0\n";

    private const string Bathroom = Base +
        "fog density=0.0075 color=(0.902, 0.933, 0.918)\n" +
        "steam at=(-15, 16.5, 0) size=8 rise=40 count=22\n" +
        "steam at=(0, 16.5, 0) size=8 rise=40 count=22\n" +
        "steam at=(15, 16.5, 0) size=8 rise=40 count=22\n" +
        "steam at=(0, 0.5, -28) size=3 rise=10 count=12\n" +
        "drip at=(0, 57, 49.5) water=16.5\n";

    [Fact]
    public void Read_TakesFogSteamAndDrip()
    {
        var level = LevelFile.Read(Bathroom);

        Assert.Equal(new FogSpec(0.0075f, new Vector3(0.902f, 0.933f, 0.918f)), level.Fog);
        Assert.Equal(4, level.Steam.Count);
        Assert.Equal(new Vector3(-15, 16.5f, 0), level.Steam[0].At);
        Assert.Equal((8f, 40f, 22), (level.Steam[0].Size, level.Steam[0].Rise, level.Steam[0].Count));
        Assert.Equal(78, level.Steam.Sum(s => s.Count));
        var drip = Assert.Single(level.Drips);
        Assert.Equal((new Vector3(0, 57, 49.5f), 16.5f), (drip.At, drip.Water));
        // The defaults: a drop every 2 s, 15 cm wide
        Assert.Equal((DripSpec.DefaultEvery, DripSpec.DefaultSize), (drip.Every, drip.Size));
    }

    [Fact]
    public void Read_ALevelWithoutThem_HasNone()
    {
        var level = LevelFile.Read(Base);

        Assert.Null(level.Fog);
        Assert.Empty(level.Steam);
        Assert.Empty(level.Drips);
    }

    [Fact]
    public void Read_SteamDefaultsItsCount_AndTheOptionsMayBeIndentedBelow()
    {
        var level = LevelFile.Read(Base + "steam at=(0, 1, 0)\n    size=4\n    rise=8\n");

        Assert.Equal(SteamSpec.DefaultCount, level.Steam[0].Count);
        Assert.Equal(4, level.Steam[0].Size);
    }

    [Theory]
    [InlineData("fog density=0.0075", 3, "missing parameter 'color'")]
    [InlineData("fog color=(1, 1, 1)", 3, "missing parameter 'density'")]
    [InlineData("fog density=0 color=(1, 1, 1)", 3, "'density' of fog is 0")]
    [InlineData("fog density=0.5 color=(1, 1, 1)", 3, "'density' of fog is 0.5")]
    [InlineData("fog density=0.01 color=(1, 2, 1)", 3, "each part 0 to 1")]
    [InlineData("fog density=0.01 color=#E6EEEA", 3, "Expected")]
    [InlineData("fog density=0.01 color=(1, 1, 1) mist=1", 3, "no parameter 'mist'")]
    [InlineData("fog density=0.01 color=(1, 1, 1)\nfog density=0.02 color=(1, 1, 1)", 4, "'fog' is given twice, first on line 3")]
    [InlineData("steam size=8 rise=40", 3, "missing parameter 'at'")]
    [InlineData("steam at=(0, 1, 0) rise=40", 3, "missing parameter 'size'")]
    [InlineData("steam at=(0, 1) size=8 rise=40", 3, "'at' of steam needs a point")]
    [InlineData("steam at=(0, 1, 0) size=8 rise=0", 3, "'rise' of steam is 0")]
    [InlineData("steam at=(0, 1, 0) size=8 rise=40 count=0", 3, "needs at least one puff")]
    [InlineData("steam at=(0, 1, 0) size=8 rise=40 count=81", 3, "'count' of steam needs a whole number of puffs alive at a time, 1 to 80")]
    [InlineData("steam at=(0, 1, 0) size=8 rise=40 count=50\nsteam at=(1, 1, 0) size=8 rise=40 count=31", 4, "at most 80 puffs in all; this plume would make it 81")]
    [InlineData("drip water=1", 3, "missing parameter 'at'")]
    [InlineData("drip at=(0, 5, 0)", 3, "missing parameter 'water'")]
    [InlineData("drip at=(0, 5, 0) water=5", 3, "'water' must be lower")]
    [InlineData("drip at=(0, 5, 0) water=1 every=0", 3, "'every' of drip is 0")]
    public void Read_ExplainsWhatIsWrongWithFogSteamAndDrip(string lines, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Base + lines + "\n"));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_AllowsTheLevelsBudgetExactly()
    {
        var level = LevelFile.Read(Base + "steam at=(0, 1, 0) size=8 rise=40 count=50\nsteam at=(1, 1, 0) size=8 rise=40 count=30\n");

        Assert.Equal(LevelFx.MaxSteamParticles, level.Steam.Sum(s => s.Count));
    }

    private static MaterialLibrary Library() =>
        MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

    private static JsonElement BakeToJson(string text)
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-fx-");
        try
        {
            LevelBaker.Bake(LevelFile.Read(text), "fx", Library(), root.FullName);
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            return document.RootElement.Clone();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_WritesFogSteamAndDrips()
    {
        var json = BakeToJson(Bathroom);

        var fog = json.GetProperty("fog");
        Assert.Equal(0.0075f, fog.GetProperty("density").GetSingle());
        Assert.Equal(new[] { 0.902f, 0.933f, 0.918f }, fog.GetProperty("color").EnumerateArray().Select(e => e.GetSingle()));
        var steam = json.GetProperty("steam").EnumerateArray().ToArray();
        Assert.Equal(4, steam.Length);
        Assert.Equal(new[] { -15f, 16.5f, 0f }, steam[0].GetProperty("at").EnumerateArray().Select(e => e.GetSingle()));
        Assert.Equal((8f, 40f, 22), (steam[0].GetProperty("size").GetSingle(), steam[0].GetProperty("rise").GetSingle(), steam[0].GetProperty("count").GetInt32()));
        var drip = json.GetProperty("drips").EnumerateArray().Single();
        Assert.Equal(new[] { 0f, 57f, 49.5f }, drip.GetProperty("at").EnumerateArray().Select(e => e.GetSingle()));
        Assert.Equal((16.5f, 2f, 0.15f), (drip.GetProperty("water").GetSingle(), drip.GetProperty("every").GetSingle(), drip.GetProperty("size").GetSingle()));
    }

    [Fact]
    public void Bake_ALevelWithoutThem_HasNoFogSteamOrDripsBlocks()
    {
        var json = BakeToJson(Base);

        Assert.False(json.TryGetProperty("fog", out _));
        Assert.False(json.TryGetProperty("steam", out _));
        Assert.False(json.TryGetProperty("drips", out _));
    }

    [Fact]
    public void ContentRoomMaterials_BathroomWallAndCeiling_AreThere_WhiteWithPaleGroutAndNoTeamColours()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var library = MaterialFiles.LoadLibrary(Path.Combine(Path.GetDirectoryName(content)!, "materials"));

        foreach (var name in new[] { "bathroom-wall", "bathroom-ceiling" })
        {
            Assert.True(library.Contains(name), name);
            var colour = library.Resolve(name).baseColor;
            // Bathroom white: bright, and never red or blue (the team colours)
            Assert.True(colour.x > 0.8f && colour.y > 0.8f && colour.z > 0.75f, $"{name} is white");
            Assert.True(MathF.Abs(colour.x - colour.z) < 0.15f, $"{name} is neither red nor blue");
        }
        var wall = library.Resolve("bathroom-wall");
        Assert.True(wall.edgeThickness > 0, "the wall has grout lines");
        Assert.True(wall.edgeColor.y >= wall.edgeColor.x, "the grout is grey-mint, not warm");
        Assert.Equal(0, library.Resolve("bathroom-ceiling").edgeThickness);
    }

    [Fact]
    public void Bake_ABathroomRoom_WithTheContentLooksAndFog_Bakes()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        var library = MaterialFiles.LoadLibrary(Path.Combine(Path.GetDirectoryName(content)!, "materials"));
        var root = Directory.CreateTempSubdirectory("sdf-level-fx-");
        try
        {
            var level = LevelFile.Read(Base + "room size=(150, 70, 100) walls=bathroom-wall ceiling=bathroom-ceiling\n" +
                                       "    light=(0, 0.77, 0.64) lightColor=(1, 0.973, 0.925) ambient=(0.45, 0.4, 0.35)\n" +
                                       "fog density=0.0075 color=(0.902, 0.933, 0.918)\n");
            LevelBaker.Bake(level, "bath", library, root.FullName);
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));

            var room = document.RootElement.GetProperty("room");
            Assert.Equal("bathroom-wall", room.GetProperty("walls").GetProperty("name").GetString());
            Assert.Equal("bathroom-ceiling", room.GetProperty("ceiling").GetProperty("name").GetString());
            Assert.True(document.RootElement.TryGetProperty("fog", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
