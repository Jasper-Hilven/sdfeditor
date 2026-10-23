using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>
/// Ticket 27.2: a level names the set of machine models its turrets, tanks and helicopters use (<c>machines set=toys ceiling=20</c>),
/// and a room level's helicopters get a flight ceiling. The default set is the fungal machines, and the three levels as they were.
/// </summary>
public class MachineSetTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static string FungalText => File.ReadAllText(Path.Combine(Content, LevelFile.FolderName, "fungal" + LevelFile.Extension));

    [Fact]
    public void TheDefault_IsTheMushroomSet_AndTheThreeLevelsKeepIt()
    {
        foreach (var name in new[] { "meadow", "fungal", "chessboard" })
        {
            var level = LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, name + LevelFile.Extension));

            Assert.Null(level.Machines);
            Assert.Equal("mushroom", level.MachineSetName);
        }
    }

    [Theory]
    [InlineData("turret", "mushroom", "red", "turret-red")]
    [InlineData("tank", "mushroom", "blue", "tank-blue")]
    [InlineData("turret", "toys", "red", "turret-toys-red")]
    [InlineData("helicopter", "toys", "blue", "helicopter-toys-blue")]
    public void ModelName_IsKindSetTeam_ExceptInTheDefaultSet(string kind, string set, string team, string expected) =>
        Assert.Equal(expected, MachineSet.ModelName(kind, set, team));

    [Fact]
    public void Read_TakesAMachinesLine_NamedLikeTheSidesZoneLineButOnItsOwn()
    {
        var level = LevelFile.Read(FungalText + "\nmachines set=toys\n");

        Assert.Equal(new MachineSet("toys", null), level.Machines);
        Assert.Equal("toys", level.MachineSetName);
        Assert.True(level.Battle!.HasMachines);
        Assert.Equal(new MachineSet("mushroom", null), LevelFile.Read(FungalText + "\nmachines\n").Machines);
    }

    private const string Room = "format 1\nplay size=(150, 100) noise=0\nroom size=(150, 64, 100) walls=toybox-wall ceiling=toybox-lid\n";

    private static string RoomWithSides(string machines) => Room + SidesOf(FungalText) + machines;

    /// <summary>The fungal level's sides and neutral sections, whose positions do not matter to the parser.</summary>
    private static string SidesOf(string text)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("sides"));
        return string.Join('\n', lines.Skip(start)) + "\n";
    }

    [Fact]
    public void Read_TakesAHelicopterFloor_InAnyLevel_AndItIsLeftOutByDefault()
    {
        // Ticket 28.8: machines set=kitchen floor=0, no room needed (the kitchen's backdrop is not a room)
        var kitchen = LevelFile.Read(FungalText + "\nmachines set=kitchen floor=0\n").Machines!;
        Assert.Equal(new MachineSet("kitchen", null, 0), kitchen);
        Assert.Equal(-2.5f, LevelFile.Read(FungalText + "\nmachines set=kitchen floor=-2.5\n").Machines!.Floor);
        Assert.Null(LevelFile.Read(FungalText + "\nmachines set=kitchen\n").Machines!.Floor);
        var bad = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(FungalText + "\nmachines set=kitchen floor=5000\n"));
        Assert.Contains("floor", bad.Problem);
    }

    [Fact]
    public void Read_ARoomLevel_HasTheCeilingItNamesOr20OrEightUnderTheLid()
    {
        Assert.Equal(25f, LevelFile.Read(RoomWithSides("machines set=toys ceiling=25\n")).Machines!.Ceiling);
        var plain = LevelFile.Read(RoomWithSides("machines set=toys\n"));
        Assert.Null(plain.Machines!.Ceiling);
        Assert.Equal(20f, plain.Machines.CeilingIn(plain.Room));
        Assert.Equal(16f, MachineSet.DefaultCeiling(24));
        Assert.Equal(20f, MachineSet.DefaultCeiling(64));
        Assert.Null(plain.Machines.CeilingIn(null));
    }

    [Theory]
    [InlineData("machines set=Toys", "lower case letters and digits")]
    [InlineData("machines set=red", "not red or blue")]
    [InlineData("machines set=toys speed=3", "Machines has no parameter 'speed'")]
    [InlineData("machines set=toys ceiling=70", "2 m or more under the room's lid")]
    [InlineData("machines set=toys ceiling=10", "'ceiling' of machines is 10")]
    public void Read_ExplainsWhatIsWrongWithAMachinesLine(string machines, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(RoomWithSides(machines + "\n")));

        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_CeilingNeedsARoom_AndTheLineNeedsMachinesOnTheSides_AndTheLineIsGivenOnce()
    {
        var noRoom = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(FungalText + "\nmachines set=toys ceiling=20\n"));
        Assert.Contains("no 'room' line", noRoom.Problem);

        var noSides = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read("format 1\nplay size=(150, 100)\nmachines set=toys\n"));
        Assert.Contains("have no turrets", noSides.Problem);

        var twice = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(FungalText + "\nmachines set=toys\nmachines set=tin\n"));
        Assert.Contains("'machines' is given twice", twice.Problem);
    }

    [Fact]
    public void Read_ALowRoom_NeedsAHigherCeiling()
    {
        var low = RoomWithSides("machines set=toys\n").Replace("room size=(150, 64, 100)", "room size=(150, 20, 100)");

        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(low));

        Assert.Contains("too low to clear a base", exception.Problem);
    }

    [Fact]
    public void Bake_WritesTheSetAndTheCeilingOnlyWhenTheLevelGivesThem()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-machines-");
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

            // As baked before: neither field
            var plain = Battle(level);
            Assert.False(plain.TryGetProperty("machineSet", out _) || plain.TryGetProperty("flightCeiling", out _));
            // The default set named: still neither
            var named = Battle(level with { Machines = new MachineSet("mushroom", null) });
            Assert.False(named.TryGetProperty("machineSet", out _) || named.TryGetProperty("flightCeiling", out _));

            // Ticket 28.8: the helicopters' floor only when the machines line gives it, 0 included
            Assert.False(named.TryGetProperty("helicopterFloor", out _));
            Assert.False(Battle(level with { Machines = new MachineSet("kitchen", null) }).TryGetProperty("helicopterFloor", out _));
            Assert.Equal(0f, Battle(level with { Machines = new MachineSet("kitchen", null, 0) }).GetProperty("helicopterFloor").GetSingle());

            // Another set, and in a room its ceiling (the given one, or the default 20)
            var toys = Battle(level with { Machines = new MachineSet("toys", null) });
            Assert.Equal("toys", toys.GetProperty("machineSet").GetString());
            Assert.False(toys.TryGetProperty("flightCeiling", out _));
            var room = level with { Room = new RoomSpec(new Vector3(40, 64, 30), 8), Machines = new MachineSet("toys", null) };
            Assert.Equal(20f, Battle(room).GetProperty("flightCeiling").GetSingle());
            Assert.Equal(24f, Battle(room with { Machines = new MachineSet("toys", 24) }).GetProperty("flightCeiling").GetSingle());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_WritesTheTallModelsFootprints_AsKeepOuts_OnlyInARoomWithMachines()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-keepouts-");
        try
        {
            // A 10 m tower 4 x 2 m, placed twice (turned a quarter, and scaled to 0.5: 5 m, so no keep-out), and a 3 m rock
            ModelFootprint? Models(string name) => name switch
            {
                "tower" => new ModelFootprint(new Vector2(-2, -1), new Vector2(2, 1)) { Height = 10 },
                "rock" => new ModelFootprint(new Vector2(-1, -1), new Vector2(1, 1)) { Height = 3 },
                _ => null,
            };
            Placement Place(string model, float x, float z, float yaw, float scale) => new(model, new Vector2(x, z), yaw, scale, 0, 6, null, 2, 1);
            var level = new LevelDefinition(new PlayArea(new Vector2(40, 30), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4))
            {
                Battle = BattleSidesTests.Battle(new Vector2(-12, 0), new Vector2(3, 8), new Vector2(3, -8), machines: true),
                Placements = [Place("tower", 5, 3, 90, 1), Place("tower", -8, -8, 0, 0.5f), Place("rock", 0, 0, 0, 1)],
            };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var room = level with { Room = new RoomSpec(new Vector3(40, 64, 30), 8), Machines = new MachineSet("computer", null) };

            JsonElement Battle(LevelDefinition l)
            {
                LevelBaker.Bake(l, "small", materials, root.FullName, Models);
                using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
                return json.RootElement.GetProperty("battle").Clone();
            }

            var boxes = Battle(room).GetProperty("helicopterKeepOuts");
            Assert.Equal(1, boxes.GetArrayLength());
            // Turned 90 degrees (+z towards +x) the 4 x 2 footprint is 2 across and 4 along z
            var box = boxes[0].EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Assert.Equal([4f, 1f, 6f, 5f], box.Select(v => MathF.Round(v, 3)).ToArray());
            // No room, or no machines line: as baked before
            Assert.False(Battle(level with { Machines = new MachineSet("computer", null) }).TryGetProperty("helicopterKeepOuts", out _));
            Assert.False(Battle(room with { Machines = null }).TryGetProperty("helicopterKeepOuts", out _));
            // Nothing tall: no field
            Assert.False(Battle(room with { Placements = [Place("rock", 0, 0, 0, 1)] }).TryGetProperty("helicopterKeepOuts", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
