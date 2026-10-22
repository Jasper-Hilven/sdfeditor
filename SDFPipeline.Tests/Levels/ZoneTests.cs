using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>
/// Ticket 29.7: the level file's <c>zone region rule</c> lines (soap slicks and conducting puddles named after ground regions) and their
/// bake into zones.bin and level.json's zones block. The rules at runtime are tested in Unity (<c>ZoneRulesTests</c>).
/// </summary>
public class ZoneTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static string FungalText => File.ReadAllText(Path.Combine(Content, LevelFile.FolderName, "fungal" + LevelFile.Extension));

    [Fact]
    public void TheThreeLevels_HaveNoZones()
    {
        foreach (var name in new[] { "meadow", "fungal", "chessboard" })
            Assert.Empty(LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, name + LevelFile.Extension)).Zones);
    }

    [Fact]
    public void Read_TakesAnyNumberOfZoneLines()
    {
        var level = LevelFile.Read(FungalText + "\nzone trail slick\nzone trail wet\n");

        Assert.Equal(2, level.Zones.Count);
        Assert.Equal(new ZoneSpec("trail", ZoneRule.Slick, level.Zones[0].Line), level.Zones[0]);
        Assert.Equal(ZoneRule.Wet, level.Zones[1].Rule);
        Assert.Equal("trail", level.Zones[1].Region);
    }

    [Theory]
    [InlineData("zone", "region")]
    [InlineData("zone trail", "rule")]
    [InlineData("zone trail icy", "icy")]
    [InlineData("zone nowhere slick", "nowhere")]
    [InlineData("zone trail slick extra", "extra")]
    public void Read_RefusesAMalformedLine_NamingTheProblem(string line, string mention)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(FungalText + "\n" + line + "\n"));

        Assert.Contains(mention, exception.Message);
    }

    [Fact]
    public void Read_RefusesTheSameRuleOnTheSameRegionTwice()
    {
        var level = FungalText;
        var first = LevelFile.Read(level).Regions[0].Name;

        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(level + $"\nzone {first} wet\nzone {first} wet\n"));

        Assert.Contains("twice", exception.Message);
    }

    private static (LevelDefinition Level, MaterialLibrary Materials) SmallLevel(params ZoneSpec[] zones)
    {
        var level = new LevelDefinition(new PlayArea(new Vector2(40, 30), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4))
        {
            Regions =
            [
                new GroundRegion("dirt", [new CircleRegion(new Vector2(-10, 0), 4, 0, 5)], GroundPattern.Plain, GroundRegion.DefaultStoneSize, GroundRegion.DefaultGroutWidth),
                new GroundRegion("path", [new BoxRegion(new Vector2(10, 0), new Vector2(6, 6), 0, 0, 0, 5)], GroundPattern.Plain, GroundRegion.DefaultStoneSize, GroundRegion.DefaultGroutWidth),
            ],
            Zones = zones,
        };
        var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default)
            .With(GroundRegion.MaterialPrefix + "dirt", MaterialDefinition.Default).With(GroundRegion.MaterialPrefix + "path", MaterialDefinition.Default);
        return (level, materials);
    }

    [Fact]
    public void Bake_WritesTheMaskAndTheZonesBlock_AndNothingWithoutZones()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-zones-");
        try
        {
            JsonElement Bake(LevelDefinition level, MaterialLibrary materials)
            {
                LevelBaker.Bake(level, "small", materials, root.FullName);
                using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
                return json.RootElement.Clone();
            }

            var (plain, materials) = SmallLevel();
            Assert.False(Bake(plain, materials).TryGetProperty("zones", out _));
            Assert.False(File.Exists(Path.Combine(root.FullName, ZoneMap.FileName)));

            var (zoned, _) = SmallLevel(new ZoneSpec("dirt", ZoneRule.Slick, 1), new ZoneSpec("path", ZoneRule.Wet, 2), new ZoneSpec("path", ZoneRule.Slick, 3));
            var zones = Bake(zoned, materials).GetProperty("zones");
            var cells = File.ReadAllBytes(Path.Combine(root.FullName, ZoneMap.FileName));

            Assert.Equal(ZoneMap.FileName, zones.GetProperty("file").GetString());
            Assert.Equal(1f, zones.GetProperty("cellSize").GetSingle());
            Assert.Equal(40, zones.GetProperty("cells")[0].GetInt32());
            Assert.Equal(30, zones.GetProperty("cells")[1].GetInt32());
            Assert.Equal(-20f, zones.GetProperty("min")[0].GetSingle());
            Assert.Equal(-15f, zones.GetProperty("min")[1].GetSingle());
            Assert.Equal(3, zones.GetProperty("rules").GetArrayLength());
            Assert.Equal(40 * 30, cells.Length);

            byte At(float x, float z) => cells[(int)(z + 15) * 40 + (int)(x + 20)];
            Assert.Equal(ZoneMap.SlickBit, At(-10.5f, 0.5f));
            Assert.Equal(ZoneMap.SlickBit | ZoneMap.WetBit, At(10.5f, 0.5f));
            Assert.Equal(0, At(0.5f, 0.5f));
            Assert.Equal(0, At(-18.5f, 12.5f));

            // The mask is the same bake after bake
            var again = SmallLevel(zoned.Zones.ToArray()).Level;
            Bake(again, materials);
            Assert.Equal(cells, File.ReadAllBytes(Path.Combine(root.FullName, ZoneMap.FileName)));

            // Taking the zones away takes the file away
            Bake(plain, materials);
            Assert.False(File.Exists(Path.Combine(root.FullName, ZoneMap.FileName)));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_RefusesAZoneOfAMissingRegion()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-zones-");
        try
        {
            var (level, materials) = SmallLevel(new ZoneSpec("suds", ZoneRule.Slick, 7));

            var exception = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(level, "small", materials, root.FullName));

            Assert.Contains("suds", exception.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
