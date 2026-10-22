using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.Materials;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>Ticket 28.2: the room as a backdrop model beyond a play area with open edges.</summary>
public class BackdropTests
{
    private const string Open = "format 1\nplay size=(150, 100) noise=0\n";

    [Fact]
    public void Read_TakesABackdrop_WithDefaults()
    {
        var level = LevelFile.Read(Open + "backdrop kitchen");

        Assert.Equal(new BackdropSpec("kitchen", Vector3.Zero, 0, BackdropSpec.DefaultFarPlane, BackdropSpec.DefaultClear, 3), level.Backdrop);
        Assert.Null(level.Room);
        Assert.Null(LevelFile.Read(Open).Backdrop);
    }

    [Fact]
    public void Read_TakesPlaceFarPlaneClearColourAndLight()
    {
        var level = LevelFile.Read(Open + "backdrop kitchen at=(0, -55, 10) yaw=90 farPlane=600 clear=(0.95, 0.9, 0.78)\n" +
                                   "    light=(0, 0.82, 0.57) lightColor=(1, 0.96, 0.88) ambient=(0.3, 0.3, 0.35)");

        var backdrop = level.Backdrop!;
        Assert.Equal("kitchen", backdrop.Model);
        Assert.Equal(new Vector3(0, -55, 10), backdrop.At);
        Assert.Equal(90, backdrop.Yaw);
        Assert.Equal(600, backdrop.FarPlane);
        Assert.Equal(new Vector3(0.95f, 0.9f, 0.78f), backdrop.Clear);
        Assert.Equal(new RoomLighting(new Vector3(0, 0.82f, 0.57f), new Vector3(1, 0.96f, 0.88f), new Vector3(0.3f, 0.3f, 0.35f)), backdrop.Light);
    }

    [Theory]
    [InlineData("backdrop", 3, "model's name")]
    [InlineData("backdrop kitchen at=(0, -55)", 3, "'at' of backdrop needs a point")]
    [InlineData("backdrop kitchen farPlane=10", 3, "'farPlane' of backdrop is 10")]
    [InlineData("backdrop kitchen clear=(2, 0, 0)", 3, "each part 0 to 1")]
    [InlineData("backdrop kitchen light=(0, 0, 0)", 3, "needs a length")]
    [InlineData("backdrop kitchen size=(1, 1, 1)", 3, "Backdrop has no parameter 'size'")]
    [InlineData("backdrop kitchen\nbackdrop kitchen", 4, "'backdrop' is given twice")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b\nbackdrop kitchen", 4, "not both")]
    [InlineData("backdrop kitchen\nroom size=(150, 64, 100) walls=a ceiling=b", 3, "not both")]
    public void Read_ExplainsWhatIsWrongWithABackdrop(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Open + text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Bake_ABackdrop_WritesTheRoomBlockWithItsForm_AndNoFaces()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-backdrop-");
        try
        {
            var level = LevelFile.Read(Open + "backdrop kitchen at=(0, -55, 0) yaw=0 farPlane=600 clear=(0.95, 0.9, 0.78) light=(0, 0.82, 0.57)");
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            LevelBaker.Bake(level, "open", materials, root.FullName, modelExists: name => name == "kitchen");

            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var json = document.RootElement;
            var room = json.GetProperty("room");
            Assert.Equal("backdrop", room.GetProperty("form").GetString());
            Assert.Equal("kitchen", room.GetProperty("model").GetString());
            Assert.Equal(new[] { 0f, -55f, 0f }, room.GetProperty("at").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(600, room.GetProperty("farPlane").GetSingle());
            Assert.Equal(new[] { 0.95f, 0.9f, 0.78f }, room.GetProperty("clear").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(new[] { 0f, 0.82f, 0.57f }, room.GetProperty("light").GetProperty("direction").EnumerateArray().Select(e => e.GetSingle()));
            Assert.False(room.TryGetProperty("faces", out _));
            Assert.False(room.TryGetProperty("size", out _));
            Assert.Equal(["kitchen"], json.GetProperty("models").EnumerateArray().Select(e => e.GetString()));
            // The ground is the play area alone: nothing is drawn beyond its edge
            Assert.Equal(new[] { -75f, -50f }, json.GetProperty("extent").GetProperty("min").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(new[] { 75f, 50f }, json.GetProperty("extent").GetProperty("max").EnumerateArray().Select(e => e.GetSingle()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_ABackdropWithoutItsModel_NamesTheLine()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-backdrop-");
        try
        {
            var level = LevelFile.Read(Open + "\nbackdrop nothing-here");
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

            var exception = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(level, "open", materials, root.FullName, modelExists: _ => false));

            Assert.Equal(4, exception.Line);
            Assert.Contains("no model 'nothing-here'", exception.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("chessboard")]
    [InlineData("toybox")]
    [InlineData("meadow")]
    [InlineData("fungal")]
    public void CommittedLevels_HaveNoBackdrop(string name)
    {
        var path = Path.Combine(FindContent(), "levels", name + LevelFile.Extension);

        Assert.Null(LevelFile.Load(path).Backdrop);
    }

    private static string FindContent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "content", "levels")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("content/"), "content");
    }
}
