using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 28.7: the kitchen's backdrop (content/models/kitchen/ kitchen-room, counter-base, worktop-edge): the room's floor, ceiling, walls, worktop run, window, light strips,
/// fridge, door and pendant lamps where the ticket puts them, the island's base set under the worktop and the worktop's edge meeting the ground at y = 0. Probes of the SDF
/// (negative inside), since the bounds finder overstates big thin shells.
/// </summary>
public class KitchenBackdropTests
{
    private static string ContentFolder()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string name) => Path.Combine(ContentFolder(), "models", "kitchen", name + ".sdf");

    private static Func<Vector3, float> Distance(string name) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(name))).Distance;

    private static void AssertInside(Func<Vector3, float> distance, double x, double y, double z, string what) =>
        Assert.True(distance(new Vector3((float)x, (float)y, (float)z)) < 0, $"({x}, {y}, {z}) should be inside: {what}");

    private static void AssertOutside(Func<Vector3, float> distance, double x, double y, double z, string what) =>
        Assert.True(distance(new Vector3((float)x, (float)y, (float)z)) > 0, $"({x}, {y}, {z}) should be empty: {what}");

    [Fact]
    public void Room_FloorIs55BelowTheWorktop_CeilingIs101Above()
    {
        var room = Distance("kitchen-room");
        AssertInside(room, 0, -56, 0, "slate floor under y = -55");
        AssertOutside(room, 0, -54, 0, "room above the floor");
        AssertOutside(room, 0, 100, 0, "room under the ceiling");
        AssertInside(room, 0, 102, 20, "ceiling over y = 101");
        // The worktop's own level is open air in the middle of the room: the level places the island there
        AssertOutside(room, 0, 0, 0, "the island's place");
    }

    [Fact]
    public void Room_WallsAreOutsideTheIslandsEdge_And300By270()
    {
        var room = Distance("kitchen-room");
        AssertOutside(room, 149, 20, 0, "room inside the west wall");
        AssertInside(room, 151, 20, 0, "east wall");
        AssertOutside(room, -149, 20, 0, "room inside the east wall");
        AssertInside(room, -151, 20, 0, "west wall");
        AssertOutside(room, 100, 90, 134, "room inside the north wall, above the cabinets");
        AssertInside(room, 100, 90, 136, "north wall");
        AssertOutside(room, -20, 20, -134, "room inside the south wall");
        AssertInside(room, -20, 20, -136, "south wall");
        // The play area's 76 x 51 half extents sit well inside: the nearest counter front is 44 m from the island's end
        AssertOutside(room, 119, -26, 0, "west counter front 30 m out from the wall");
        AssertInside(room, 140, -26, 0, "west counter body");
    }

    [Fact]
    public void Room_NorthRun_HasWorktopAtYZero_TileBandAndWindow()
    {
        var room = Distance("kitchen-room");
        AssertInside(room, 0, -0.5, 120, "worktop slab under y = 0");
        AssertOutside(room, 0, 0.5, 120, "air over the worktop");
        AssertInside(room, 100, 20, 134.5, "white tile band");
        AssertOutside(room, 100, 20, 132.5, "room in front of the tiles");
        AssertInside(room, 100, 50, 125, "upper cabinet beside the window");
        // The window: open reveal, daylight pane at the back, frame bars, the sill and the basil on it
        AssertOutside(room, 20, 30, 134, "open reveal in front of the pane");
        AssertInside(room, 20, 30, 137, "daylight pane");
        AssertInside(room, 0, 60, 136, "mullion");
        AssertInside(room, 0, 10.5, 130, "sill under the window");
        AssertInside(room, -18, 21, 131, "basil on the sill");
        AssertOutside(room, 0, 30, 120, "air in front of the sill");
    }

    [Fact]
    public void Room_WestAndEastRuns_HaveUpperCabinetsAndLightStripsUnderThem()
    {
        var room = Distance("kitchen-room");
        foreach (var side in new[] { -1f, 1f })
        {
            AssertInside(room, side * 140, -0.5f, 0, "worktop");
            AssertInside(room, side * 140, 60, 0, "upper cabinet");
            AssertOutside(room, side * 140, 20, 0, "gap between worktop and the cabinets");
            AssertInside(room, side * 140, 35, 0, "light strip under the upper cabinet");
            AssertOutside(room, side * 140, 32, 0, "air under the strip");
        }
    }

    [Fact]
    public void Room_SouthWall_HasAFridgeWithMagnetsAndADoor()
    {
        var room = Distance("kitchen-room");
        AssertInside(room, -88, 50, -110, "tall fridge, 108 m from the floor");
        AssertOutside(room, -88, 56, -110, "air over the fridge");
        AssertInside(room, -94, 35, -99.5f, "a magnet on the freezer door");
        AssertOutside(room, -88, 0, -98, "room in front of the fridge");
        AssertInside(room, 79, 0, -132.5f, "door leaf");
        AssertOutside(room, 79, 0, -130, "room in front of the door");
        AssertInside(room, 54, 0, -134, "architrave");
        AssertOutside(room, 79, 70, -134, "air over the architrave");
    }

    [Fact]
    public void Room_PendantLamps_HangOverTheIsland_ShadesAbove65()
    {
        var room = Distance("kitchen-room");
        foreach (var x in new[] { -40f, 40f })
        {
            AssertInside(room, x, 70, 0, "shade");
            AssertInside(room, x, 64.8f, 0, "glowing underside");
            AssertOutside(room, x, 62.5f, 0, "air under the shade");
            AssertInside(room, x, 90, 0, "cord to the ceiling");
        }
        AssertOutside(room, 0, 70, 0, "no lamp between them");
    }

    [Fact]
    public void Room_UsesEightMaterialsAtMost_AllInTheKitchenFolder()
    {
        var used = Regex.Matches(File.ReadAllText(ModelPath("kitchen-room")), @"material=(\S+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.InRange(used.Count, 1, 8);
        foreach (var name in used)
        {
            Assert.StartsWith("kitchen_", name);
            Assert.True(File.Exists(Path.Combine(ContentFolder(), "..", "materials", "kitchen", name + ".sdfmat")), $"{name} has no material file");
        }
        Assert.Contains("collider none", File.ReadAllText(ModelPath("kitchen-room")));
    }

    [Fact]
    public void CounterBase_StandsOnTheFloor_UnderTheGround_SetBack3mUnderTheWorktop()
    {
        var baseModel = Distance("counter-base");
        AssertInside(baseModel, 0, -54, 0, "plinth at the floor");
        AssertOutside(baseModel, 0, -56, 0, "nothing under the floor");
        AssertInside(baseModel, 0, -1.5f, 0, "top just under the ground");
        AssertOutside(baseModel, 0, -0.5f, 0, "nothing at the ground's plane");
        AssertInside(baseModel, 72.5f, -30, 5, "door front on the east side");
        AssertOutside(baseModel, 74.5f, -30, 5, "beyond the base's 146 m, 3 m back from the worktop's 152");
        AssertInside(baseModel, 0, -30, 47.5f, "door front on the north side");
        AssertOutside(baseModel, 0, -30, 49, "beyond the base's 96 m");
        // The plinth is set back further than the doors
        AssertOutside(baseModel, 72, -53, 5, "toe recess");
        AssertInside(baseModel, 68, -53, 5, "plinth");
    }

    [Fact]
    public void WorktopEdge_TopAtYZero_InnerEdgeAtThePlayArea_FlangeUnderTheGround()
    {
        var edge = Distance("worktop-edge");
        AssertInside(edge, 75.2f, -0.1f, 0, "slab's lip, top at y = 0");
        AssertOutside(edge, 75.5f, 0.1f, 0, "air over the lip");
        AssertOutside(edge, 74.5f, -0.5f, 0, "the play area's ground is not part of the frame");
        AssertOutside(edge, 74.9f, 0.1f, 0, "the ground's level at the inner edge");
        AssertInside(edge, 75.2f, -2.3f, 0, "slab is 2.4 m thick");
        AssertOutside(edge, 75.5f, -2.5f, 0, "nothing under the slab");
        AssertOutside(edge, 76.5f, -1, 0, "outside the 152 m");
        AssertInside(edge, 75.9f, -1.5f, 0, "outer face");
        // The flange under the ground closes the gap to the counter-base
        AssertInside(edge, 74, -1.8f, 0, "flange");
        AssertOutside(edge, 72.5f, -1.8f, 0, "open inside the flange");
        AssertInside(edge, 0, -1.8f, 49, "flange on the north side");
        AssertOutside(edge, 0, -0.5f, 49.5f, "open over the flange");
        AssertInside(edge, 0, -0.5f, 50.5f, "lip on the north side");
    }
}
