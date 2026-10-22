using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>
/// Ticket 29.6: the level file's <c>nofly at=(x, z) size=(w, d) yaw=deg</c> lines (boxes the machine helicopters fly round: the bathroom's
/// tub, giant duck, pyramids and tall bottles stand in their flight band) and their bake into the battle block of level.json. The pilot's
/// rules are tested in Unity (<c>NoFlyZonesTests</c>).
/// </summary>
public class NoFlyTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static string FungalText => File.ReadAllText(Path.Combine(Content, LevelFile.FolderName, "fungal" + LevelFile.Extension));

    [Fact]
    public void TheThreeLevels_HaveNoBoxes()
    {
        foreach (var name in new[] { "meadow", "fungal", "chessboard" })
            Assert.Empty(LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, name + LevelFile.Extension)).NoFly);
    }

    [Fact]
    public void Read_TakesAnyNumberOfNoFlyLines()
    {
        var level = LevelFile.Read(FungalText + "\nnofly at=(0, 38.75) size=(51, 22.5) yaw=180\nnofly at=(0, 0) size=(7.5, 10.5)\n");

        Assert.Equal(2, level.NoFly.Count);
        Assert.Equal(new Vector2(0, 38.75f), level.NoFly[0].At);
        Assert.Equal(new Vector2(51, 22.5f), level.NoFly[0].Size);
        Assert.Equal(180f, level.NoFly[0].Yaw);
        Assert.Equal(0f, level.NoFly[1].Yaw);
    }

    [Theory]
    [InlineData("nofly size=(5, 5)", "at")]
    [InlineData("nofly at=(0, 0)", "size")]
    [InlineData("nofly at=(0, 0) size=(0, 5)", "size")]
    [InlineData("nofly at=(0, 0) size=(5, 5) yaw=720", "yaw")]
    [InlineData("nofly at=(0, 0) size=(5, 5) height=9", "height")]
    public void Read_RefusesAMalformedLine_NamingTheParameter(string line, string parameter)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(FungalText + "\n" + line + "\n"));

        Assert.Contains(parameter, exception.Message);
    }

    [Fact]
    public void Bake_WritesTheBoxesIntoTheBattleBlock_AndNothingWithoutThem()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-nofly-");
        try
        {
            var level = new LevelDefinition(new PlayArea(new Vector2(40, 30), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4))
                { Battle = BattleSidesTests.Battle(new Vector2(-12, 0), new Vector2(3, 8), new Vector2(3, -8), machines: true) };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

            JsonElement Battle(LevelDefinition l)
            {
                LevelBaker.Bake(l, "small", materials, root.FullName);
                using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
                return json.RootElement.GetProperty("battle").Clone();
            }

            Assert.False(Battle(level).TryGetProperty("noFly", out _));

            var boxes = Battle(level with { NoFly = [new NoFlySpec(new Vector2(0, 38.75f), new Vector2(51, 22.5f), 180, 1), new NoFlySpec(new Vector2(-26, -15), new Vector2(10.8f, 3.6f), 10, 2)] })
                .GetProperty("noFly");
            Assert.Equal(2, boxes.GetArrayLength());
            Assert.Equal(38.75f, boxes[0].GetProperty("at")[1].GetSingle());
            Assert.Equal(51f, boxes[0].GetProperty("size")[0].GetSingle());
            Assert.Equal(180f, boxes[0].GetProperty("yaw").GetSingle());
            Assert.Equal(10f, boxes[1].GetProperty("yaw").GetSingle());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
