using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 28.6: the kitchen's base, the roll-top bread box (content/models/kitchen/bread-box0.sdf, its slats the part bread_box_slat) 24 x 15.6 x 13.2 m, open at the front
/// (+z) with a 0.3 m floor and a hall 22.8 m wide and 12 m high, the flag stand and the base health pod inside it, and the two gingham tea towels draped over its lid.
/// </summary>
public class KitchenBreadBoxTests
{
    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string name) => Path.Combine(Content(), "models", "kitchen", name + ".sdf");

    private static Func<Vector3, float> Distance(string name) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(name))).Distance;

    private static SdfBounds Bounds(string name, float halfExtent)
    {
        var bounds = SdfBoundsFinder.Find(Distance(name), halfExtent);
        Assert.NotNull(bounds);
        return bounds;
    }

    private static readonly Func<Vector3, float> Box = Distance("bread-box0");

    // The level turns the box by yaw 90 (west) and -90 (east): local +z is then world +x, local x is world -z. Flag stand (-65, 0) and base health pod (-65, -8) on the west box at
    // (-62, 0), east (65, 0) and (65, -8) on the box at (62, 0), written in the box's own frame (see 28 "Bases" and "Pods and lanes")
    private static readonly Vector2 FlagStand = new(0, -3);
    private static readonly Vector2 WestHealthPod = new(8, -3);
    private static readonly Vector2 EastHealthPod = new(-8, -3);

    /// <summary>24 x 15.6 x 13.2 m, its middle on the origin and its base on y = 0 (the finder rounds out to its cells and the slats' part sits in it).</summary>
    [Fact]
    public void Size_IsTheTablesSize_AndTheBoxStandsOnTheGround()
    {
        var bounds = Bounds("bread-box0", 16f);
        var size = bounds.Max - bounds.Min;
        Assert.InRange(size.X, 23.85f, 27f);
        Assert.InRange(size.Y, 13.05f, 15.5f);
        Assert.InRange(size.Z, 15.45f, 18.5f);
        Assert.InRange(bounds.Min.Y, -0.5f, 0.03f);
        Assert.InRange(bounds.Max.Y, 13.0f, 14.9f);
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.5f, 0.5f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.5f, 0.5f);
    }

    /// <summary>The lid's roll top is the highest point, 13.2 m, over the back; the deck is at 12.6 m.</summary>
    [Fact]
    public void Lid_RisesToThirteenPointTwo_OverTheRolledUpTop()
    {
        Assert.InRange(Top(0, -4.2f), 13.1f, 13.3f);
        Assert.InRange(Top(0, 3.3f), 12.55f, 12.7f);
        Assert.InRange(Top(10f, 3.3f), 12.55f, 12.7f);
    }

    private static float Top(float x, float z)
    {
        // Walk down from above until the surface
        for (var y = 14f; y > 0; y -= 0.01f)
            if (Box(new Vector3(x, y, z)) <= 0) return y;
        return 0;
    }

    /// <summary>The bottom board's top is 0.3 m up, flat from the back to the front, and the front edge is rounded off so it can be stepped onto.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(4f, 3f)]
    [InlineData(-4f, 5f)]
    [InlineData(0f, -3f)]
    public void Floor_TopIsThirtyCentimetres(float x, float z) => Assert.InRange(Top(x, z, 1f), 0.29f, 0.31f);

    // As Top, but starting under the lid
    private static float Top(float x, float z, float from)
    {
        for (var y = from; y > 0; y -= 0.005f)
            if (Box(new Vector3(x, y, z)) <= 0) return y;
        return 0;
    }

    [Fact]
    public void Floor_FrontEdgeIsBevelled_UnderTheBotsStep()
    {
        // At the front face the board is lower than 0.3 m, so the climb from the worktop there is under 0.4 m at every point
        Assert.True(Top(0, 7.75f, 1f) < 0.3f, "the board's front edge is not rounded off");
        Assert.True(Top(0, 7.75f, 1f) <= 0.3f);
        // And no step on the way is over 0.4 m: heights along the front stay within 0.4 of the worktop
        var previous = 0f;
        for (var z = 7.9f; z > 6.5f; z -= 0.05f)
        {
            var top = Top(0, z, 1f);
            Assert.True(top - previous < 0.2f, $"a step of {top - previous} m at z = {z}");
            previous = top;
        }
    }

    /// <summary>The walls are 0.6 m thick: the hall is 22.8 m wide (x +-11.4) and ends at the back wall's inner face, z = -7.2, with the outside at x = +-12 and z = -7.8.</summary>
    [Fact]
    public void Walls_AreSixTenthsThick_AndTheHallIs22Point8Wide()
    {
        foreach (var side in new[] { -1f, 1f })
        {
            Assert.True(Box(new Vector3(side * 11.3f, 6, 0)) > 0, "the hall is narrower than 22.8 m");
            Assert.True(Box(new Vector3(side * 11.5f, 6, 0)) < 0, "the wall's inner face is not at 11.4");
            Assert.True(Box(new Vector3(side * 11.9f, 6, 0)) < 0, "the wall is thinner than 0.6 m");
            Assert.True(Box(new Vector3(side * 12.1f, 6, 0)) > 0, "the wall is thicker than 0.6 m");
        }

        Assert.True(Box(new Vector3(0, 6, -7.1f)) > 0, "the hall does not reach the back wall");
        Assert.True(Box(new Vector3(0, 6, -7.3f)) < 0, "the back wall's inner face is not at 7.2");
        Assert.True(Box(new Vector3(0, 6, -7.7f)) < 0);
        Assert.True(Box(new Vector3(0, 6, -7.9f)) > 0);
    }

    /// <summary>The hall is 12 m high under the roll top: free up to 12 m, the deck's slats over it (the part is inside the model's distance).</summary>
    [Fact]
    public void Hall_IsTwelveMetresHigh_UnderTheSlats()
    {
        foreach (var z in new[] { -2.1f, 0.3f, 3.3f, 6.3f })
        {
            Assert.True(Box(new Vector3(0, 11.9f, z)) > 0, $"the hall is lower than 12 m at z = {z}");
            Assert.True(Box(new Vector3(0, 12.3f, z)) < 0.05f, $"no lid over the hall at z = {z}");
        }
    }

    /// <summary>The front is open the whole width of the hall, up to the lid: from 0.3 m over the floor to 11.9 m, the whole 22.8 m, nothing stands in the opening.</summary>
    [Fact]
    public void Front_IsOpenTheWholeWidth()
    {
        for (var x = -11.3f; x <= 11.3f; x += 0.25f)
        for (var y = 0.7f; y < 11.9f; y += 0.5f)
            Assert.True(Box(new Vector3(x, y, 7.6f)) > 0, $"the opening is blocked at x = {x}, y = {y}");
    }

    /// <summary>No wall between the front and the flag stand: a body 0.7 m wide walks in along the centre line at its height to the stand, and in from the base health pods' side.</summary>
    [Fact]
    public void FlagStand_IsReachableFromTheFront()
    {
        for (var z = 9f; z >= FlagStand.Y; z -= 0.1f)
        foreach (var y in new[] { 0.9f, 1.5f, 3f, 6f })
            Assert.True(Box(new Vector3(FlagStand.X, y, z)) > 0.4f, $"something stands in the way at z = {z}, y = {y}");
    }

    /// <summary>The flag stand and the base health pod lie in the hall, 4 m in front of the back wall, clear of the walls, the loaf and the jar.</summary>
    [Theory]
    [InlineData(0f, -3f)]
    [InlineData(8f, -3f)]
    [InlineData(-8f, -3f)]
    public void StandsAndPods_AreInsideTheHall_AndClearOfTheWalls(float x, float z)
    {
        Assert.InRange(x, -11.4f + 3f, 11.4f - 3f);
        Assert.InRange(z, -7.2f + 3f, 7.8f);
        // Four metres from the back wall's inner face to the flag stand, as 28 puts it (world -65 is 4.2 m from the outer face 69.8 less 0.6)
        // and 1.5 m of clear floor round it, standing a metre up
        for (var angle = 0f; angle < MathF.Tau; angle += MathF.PI / 6)
            Assert.True(Box(new Vector3(x + 1.5f * MathF.Cos(angle), 1.0f, z + 1.5f * MathF.Sin(angle))) > 0.2f, $"the ({x}, {z}) spot is crowded at {angle}");
        Assert.True(Box(new Vector3(x, 1.0f, z)) > 0.6f);
        // A pod's room above it too
        Assert.True(Box(new Vector3(x, 4f, z)) > 2f, "no room above");
    }

    [Fact]
    public void FlagStandAndPods_MatchTheLevelsWorldPositions()
    {
        Assert.Equal(-3f, FlagStand.Y);
        // Both boxes: the stand is 4 m in front of the back wall's centre line z = -7 (outer face -7.8, inner -7.2: 4.2 m from the inner face, as 28 puts it)
        Assert.InRange(FlagStand.Y - -7.2f, 4f, 4.3f);
        Assert.Equal(8f, Math.Abs(WestHealthPod.X));
        Assert.Equal(-EastHealthPod.X, WestHealthPod.X);
    }

    /// <summary>The loaf is 1.3 m high with its crust wall, a cover for a bot in the back corner, and the jar stands in the other.</summary>
    [Fact]
    public void LoafAndJar_DressTheBackCorners()
    {
        Assert.InRange(Top(-10f, -5.8f, 4f), 1.35f, 1.55f);
        Assert.InRange(Top(8.8f, -6f, 4f), 2.5f, 2.7f);
    }

    /// <summary>The slats are the part bread_box_slat, used 26 times (18 on the deck, 7 round the roll, 1 behind them), and the part has no collider; the box has a mesh collider.</summary>
    [Fact]
    public void Slats_AreOnePartUsed26Times()
    {
        var source = File.ReadAllText(ModelPath("bread-box0"));
        Assert.Equal(26, Regex.Matches(source, @"^\s*Part part=bread_box_slat", RegexOptions.Multiline).Count);
        Assert.True(File.Exists(Path.Combine(Content(), "parts", "kitchen", "bread_box_slat.sdf")));
        Assert.Equal(ColliderShape.Mesh, ModelSettings.Load(ModelPath("bread-box0")).Collider);
        Assert.Equal(ColliderShape.None, ModelSettings.Load(Path.Combine(Content(), "parts", "kitchen", "bread_box_slat.sdf")).Collider);
    }

    /// <summary>The box uses only the kitchen's materials (and no red or blue; the towels' team cloth is in the towel models only).</summary>
    [Theory]
    [InlineData("bread-box0")]
    public void Box_UsesNoTeamMaterials(string name)
    {
        var source = File.ReadAllText(ModelPath(name));
        foreach (Match match in Regex.Matches(source, @"material=(\w+)"))
            Assert.StartsWith("kitchen_", match.Groups[1].Value);
    }

    /// <summary>The towels lie on the lid and hang down the +x side: on the deck (12.6 m) and the wall's outer face (12 m), clear of the steel pull, no collider, team cloth with cream.</summary>
    [Theory]
    [InlineData("tea-towel-red", "team_red_cloth")]
    [InlineData("tea-towel-blue", "team_blue_cloth")]
    public void TeaTowel_IsDrapedOverTheLidAndDownOneSide(string name, string teamMaterial)
    {
        // The cloth is thin and off the origin, so the bounds finder cannot find it: probe its edges instead. The real cloth is x 3.4 to 12.2, y 6.8 to 12.8, z -1.8 to 6.7
        var cloth = Distance(name);
        Assert.True(cloth(new Vector3(3.7f, 12.7f, 3)) < 0.01f && cloth(new Vector3(3.0f, 12.7f, 3)) > 0.2f, "the deck part does not start at x = 3.4");
        Assert.True(cloth(new Vector3(12.1f, 7.1f, 3)) < 0.01f && cloth(new Vector3(12.1f, 6.3f, 3)) > 0.2f, "the side does not end at y = 6.8");
        Assert.True(cloth(new Vector3(8, 12.7f, 6.4f)) < 0.01f && cloth(new Vector3(8, 12.7f, 7.2f)) > 0.2f, "the cloth does not end at z = 6.7");
        Assert.True(cloth(new Vector3(8, 12.7f, -1.5f)) < 0.01f && cloth(new Vector3(8, 12.7f, -2.3f)) > 0.2f, "the cloth does not start at z = -1.8");

        var source = File.ReadAllText(ModelPath(name));
        Assert.Contains("material=" + teamMaterial, source);
        Assert.Contains("material=kitchen_gingham_cream", source);
        Assert.Equal(ColliderShape.None, ModelSettings.Load(ModelPath(name)).Collider);

        // It rests on the lid and the wall: its inside face meets the deck (12.6) and the wall (12.0), no gap and not through them
        var distance = cloth;
        Assert.True(distance(new Vector3(8, 12.7f, 3)) < 0.01f, "no cloth on the deck");
        Assert.True(distance(new Vector3(8, 12.45f, 3)) > 0.1f, "the cloth reaches into the lid");
        Assert.True(distance(new Vector3(12.1f, 9, 3)) < 0.01f, "no cloth on the wall");
        Assert.True(distance(new Vector3(11.8f, 9, 3)) > 0.1f, "the cloth reaches into the wall");
        // The steel pull at the front stays clear (z 6.7 and beyond)
        Assert.True(distance(new Vector3(0, 12.65f, 7.15f)) > 0.3f);
    }

    /// <summary>The towel rests where the box has its lid: the box's solid is under it everywhere on the deck.</summary>
    [Theory]
    [InlineData("tea-towel-red")]
    [InlineData("tea-towel-blue")]
    public void TeaTowel_SitsOnTheBox(string name)
    {
        var towel = Distance(name);
        for (var x = 4f; x <= 11.5f; x += 1.5f)
        for (var z = -1.5f; z <= 6.5f; z += 2f)
            if (towel(new Vector3(x, 12.7f, z)) < 0.01f)
                Assert.True(Box(new Vector3(x, 12.5f, z)) < 0f, $"cloth over nothing at ({x}, {z})");
    }
}
