using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 27.7: the toy box's block castle (toy-castle0, laid out from content/parts/toys/castle/ as castle0 is) has the footprint and heights 27's Bases section
/// gives, castle0's floor heights (walk 5 m, rings 5.3 m, steps of 0.3 m up to the walk) and an open gate, and the two pennants are 3 m poles with a 1.2 x 2.5 m cloth.
/// </summary>
public class ToyCastleTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static string ModelPath(string name) => Path.Combine(Content, "models", "toys", name + ".sdf");

    private static Func<Vector3, float> Field(string name)
        => new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(name))).Distance;

    private static bool Inside(Func<Vector3, float> field, float x, float y, float z) => field(new Vector3(x, y, z)) <= 0.005f;

    /// <summary>The height of the highest surface under <paramref name="from"/> at (x, z), to a millimetre; NaN if there is none.</summary>
    private static float TopAt(Func<Vector3, float> field, float x, float z, float from)
    {
        for (var y = from; y > 0; y -= 0.01f)
        {
            if (!Inside(field, x, y, z)) continue;
            float low = y, high = y + 0.01f;
            for (var i = 0; i < 12; i++)
            {
                var mid = (low + high) / 2;
                if (Inside(field, x, mid, z)) low = mid; else high = mid;
            }
            return (low + high) / 2;
        }
        return float.NaN;
    }

    private static readonly string[] Parts =
    [
        "toy_castle_keep", "toy_castle_merlon", "toy_castle_roof_lilac", "toy_castle_roof_mustard",
        "toy_castle_stair", "toy_castle_tower", "toy_castle_wall_bay", "toy_castle_wall_gate",
    ];

    [Fact]
    public void Castle_UsesExactlyTheCastlePartsInToysCastle_AndTheSetIsDrawnTheRightNumberOfTimes()
    {
        var used = Regex.Matches(File.ReadAllText(ModelPath("toy-castle0")), @"^\s*Part part=(\w+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(Parts, used.Distinct().Order());

        var folder = Path.Combine(Content, "parts", "toys", "castle");
        Assert.Equal(Parts, Directory.GetFiles(folder, "*.sdf").Select(Path.GetFileNameWithoutExtension).Order());

        // 3 + 5 + 6 wall bays plus the gate, 3 merlons on each bay but the gate's, four towers and roofs (two of each), a keep, two stairs (one mirrored)
        Assert.Equal(15, used.Count(p => p == "toy_castle_wall_bay"));
        Assert.Equal(1, used.Count(p => p == "toy_castle_wall_gate"));
        Assert.Equal(15, used.Count(p => p == "toy_castle_merlon") / 3);
        Assert.Equal(4, used.Count(p => p == "toy_castle_tower"));
        Assert.Equal(2, used.Count(p => p == "toy_castle_roof_lilac"));
        Assert.Equal(2, used.Count(p => p == "toy_castle_roof_mustard"));
        Assert.Equal(1, used.Count(p => p == "toy_castle_keep"));
        Assert.Equal(1, used.Count(p => p == "toy_castle_stair"));
    }

    [Fact]
    public void Castle_HasAMeshColliderAndEveryPartExceptTheMerlonsToo()
    {
        Assert.Equal(ColliderShape.Mesh, ModelSettings.Load(ModelPath("toy-castle0")).Collider);
        foreach (var part in Parts)
            Assert.Equal(part == "toy_castle_merlon" ? ColliderShape.None : ColliderShape.Mesh,
                ModelSettings.Load(Path.Combine(Content, "parts", "toys", "castle", part + ".sdf")).Collider);
    }

    [Fact]
    public void Castle_HasTheBasesFootprintAndHeights_AndStandsOnTheGround()
    {
        var bounds = SdfBoundsFinder.Find(Field("toy-castle0"), 20f);
        Assert.NotNull(bounds);
        var size = bounds.Max - bounds.Min;
        // 26 m between the tower centres plus the rings, 19 m wall to wall plus the rings, the keep's roof to 12 m (the finder rounds out to its octree cells)
        Assert.InRange(size.X, 30f, 32.5f);
        Assert.InRange(size.Z, 19.5f, 22.2f);
        Assert.InRange(size.Y, 11.9f, 14f);
        Assert.InRange(bounds.Min.Y, -0.7f, 0.05f);
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.4f, 0.4f);
        // The keep's roof ends in a bead at 12.1 m, the towers' roofs at 10 m
        Assert.True(Inside(Field("toy-castle0"), 0f, 12.0f, -4.6f));
        Assert.False(Inside(Field("toy-castle0"), 0f, 12.3f, -4.6f));
        Assert.True(Inside(Field("toy-castle0"), 13f, 9.9f, 7.5f));
        Assert.False(Inside(Field("toy-castle0"), 13f, 10.3f, 7.5f));
        // The rear and front faces of the walls are at z = -9.5 and 9.5, as the level's reach of the castle (x = -52.5 and -71.5 after the turn) needs
        var field = Field("toy-castle0");
        Assert.True(Inside(field, 7.8f, 2.5f, 9.45f));
        Assert.False(Inside(field, 7.8f, 2.5f, 9.6f));
        Assert.True(Inside(field, 7.8f, 2.5f, -9.45f));
        Assert.False(Inside(field, 7.8f, 2.5f, -9.6f));
    }

    [Fact]
    public void Castle_HasTheWallWalkAtFiveMetres_AllRoundTheWall()
    {
        var field = Field("toy-castle0");
        // The inner half of each wall, clear of the merlons on the outer edge; the walk of the gate bay is the walkway between its slabs
        var points = new List<(float X, float Z)>();
        foreach (var x in new[] { -10.4f, -5.2f, 5.2f, 10.4f }) points.Add((x, 8.0f));
        foreach (var x in new[] { -10.4f, -5.2f, 0f, 5.2f, 10.4f }) points.Add((x, -8.0f));
        foreach (var z in new[] { -1.3f, 0f, 1.3f }) { points.Add((-12.5f, z)); points.Add((12.5f, z)); }
        points.Add((0f, 8.5f));
        foreach (var (x, z) in points)
            Assert.True(MathF.Abs(TopAt(field, x, z, 5.1f) - 5f) < 0.012f, $"the walk at ({x}, {z}) is at {TopAt(field, x, z, 5.1f)}");
    }

    [Fact]
    public void Castle_HasTheRingsAtFiveAndAThirdAndTheKeepsToo()
    {
        var field = Field("toy-castle0");
        // The towers' rings: 2.2 m from each centre (the upper shaft is 1.4 m), on the outer side clear of the wall's merlons
        foreach (var (cx, cz) in new[] { (13f, 7.5f), (-13f, 7.5f), (13f, -7.5f), (-13f, -7.5f) })
            foreach (var angle in new[] { 0.385f, 0.785f, 1.185f })
            {
                var x = cx + MathF.Sign(cx) * 2.2f * MathF.Cos(angle);
                var z = cz + MathF.Sign(cz) * 2.2f * MathF.Sin(angle);
                Assert.True(MathF.Abs(TopAt(field, x, z, 5.6f) - 5.3f) < 0.012f, $"the ring at ({x}, {z}) is at {TopAt(field, x, z, 5.6f)}");
            }
        // The keep's ring, the ledge round its top block (2.6 m from the middle in x; the keep stands at z = -4.6)
        Assert.Equal(5.3f, TopAt(field, 2.6f, -4.6f, 5.6f), 0.012f);
        Assert.Equal(5.3f, TopAt(field, -2.6f, -2.2f, 5.6f), 0.012f);
        // The step from the walk (5 m) to the rings is 0.3 m, under the controller's 0.4 m
        Assert.InRange(5.3f - 5f, 0f, 0.4f);
    }

    [Fact]
    public void Castle_StairsClimbInStepsOfThirtyCentimetres_AndEndUnderTheWalk_OnBothSides()
    {
        var field = Field("toy-castle0");
        foreach (var side in new[] { 1f, -1f })
        {
            var previous = 0f;
            for (var step = 0; step < 16; step++)
            {
                // Step i: 0.6 m treads from z = 5 towards -z, its top 0.3 m higher than the one before
                var z = 5f - (0.6f * step + 0.3f);
                var top = TopAt(field, side * 11.3f, z, 5.2f);
                Assert.Equal(0.3f * (step + 1), top, 0.012f);
                Assert.Equal(0.3f, top - previous, 0.02f);
                previous = top;
            }
            // The top step (4.8 m) is 0.2 m under the walk, which is next to it on the wall (x = 12.5 at the inner face)
            Assert.InRange(5f - previous, 0.05f, 0.4f);
            Assert.Equal(5f, TopAt(field, side * 12.5f, -4.3f, 5.1f), 0.012f);
        }
    }

    [Fact]
    public void Castle_GateIsOpenForTheRoadAndTheWalkRunsThroughTheTower()
    {
        var field = Field("toy-castle0");
        // The passage, 3 m wide and 3.5 m high at its crown, open all along z through the gate bay (z 7.5 to 9.5)
        for (var z = 7.0f; z <= 10.0f; z += 0.25f)
        {
            Assert.False(Inside(field, 0f, 0.3f, z), $"the gate is shut at z = {z}");
            Assert.False(Inside(field, 1.3f, 1.8f, z), $"the gate is shut beside its middle at z = {z}");
            Assert.False(Inside(field, 0f, 3.2f, z), $"the arch is shut at z = {z}");
        }
        // Stone on either side and above the arch's crown
        Assert.True(Inside(field, 2.2f, 1.5f, 8.5f));
        Assert.True(Inside(field, -2.2f, 1.5f, 8.5f));
        Assert.True(Inside(field, 0f, 4.5f, 8.5f));
        // The walkway through the gate tower: 2.3 m clear over the walk, 1.7 m wide, with the tower's slabs either side and the block over it
        for (var x = -2.4f; x <= 2.4f; x += 0.2f)
        {
            Assert.False(Inside(field, x, 6.0f, 8.5f), $"the walkway is shut at x = {x}");
            Assert.True(Inside(field, x, 7.5f, 8.5f), $"the block over the walkway is missing at x = {x}");
        }
        Assert.True(Inside(field, 0f, 6f, 9.7f));
        Assert.True(Inside(field, 0f, 6f, 7.3f));
    }

    [Theory]
    [InlineData("pennant-red")]
    [InlineData("pennant-blue")]
    public void Pennant_IsAThreeMetrePoleWithAClothOfOnePointTwoByTwoPointFive(string name)
    {
        var field = Field(name);
        var bounds = SdfBoundsFinder.Find(field, 4f);
        Assert.NotNull(bounds);
        var size = bounds.Max - bounds.Min;
        Assert.InRange(size.Y, 2.95f, 3.4f);
        Assert.InRange(size.X, 1.45f, 1.9f);
        Assert.InRange(size.Z, 0.1f, 0.4f);
        Assert.InRange(bounds.Min.Y, -0.3f, 0.05f);
        Assert.Equal(ColliderShape.None, ModelSettings.Load(ModelPath(name)).Collider);

        // The cloth: from the crossbar (2.8 m) 2.5 m down on its inner side to 0.3 m, a notch cut up from the middle of its lower edge
        Assert.True(Inside(field, 0.7f, 2.6f, 0f));
        Assert.True(Inside(field, 0.15f, 0.4f, 0f));
        Assert.True(Inside(field, 1.25f, 0.4f, 0f));
        Assert.False(Inside(field, 0.7f, 0.4f, 0f), "the swallow tail's notch");
        Assert.True(Inside(field, 0.7f, 1.4f, 0f));
        Assert.False(Inside(field, 1.45f, 1.4f, 0f), "past the cloth's width of 1.2 m");
        // The pole stands 3 m high at the origin
        Assert.True(Inside(field, 0f, 2.5f, 0f));
        Assert.True(Inside(field, 0f, 3.0f, 0f));
    }

    [Fact]
    public void PennantMaterials_AreTheTeamsColours_AndTheCastleKeepsToToyMaterials()
    {
        var root = Path.GetDirectoryName(Content)!;
        string BaseColor(string file)
        {
            var line = File.ReadAllLines(Path.Combine(root, "materials", "buildings", file)).First(l => l.StartsWith("baseColor"));
            return line[(line.IndexOf('(') + 1)..line.IndexOf(')')];
        }
        float[] Values(string text) => text.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var red = Values(BaseColor("team_red_cloth.sdfmat"));
        var blue = Values(BaseColor("team_blue_cloth.sdfmat"));
        Assert.True(red[0] > 0.6f && red[0] > red[1] + 0.35f && red[0] > red[2] + 0.35f);
        Assert.True(blue[2] > blue[0] + 0.25f && blue[2] > blue[1] + 0.1f);

        // No red or blue in the castle: its parts name only toy_ materials
        var files = Directory.GetFiles(Path.Combine(Content, "parts", "toys", "castle"), "*.sdf").Append(ModelPath("toy-castle0"));
        foreach (var file in files)
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"material=(\w+)"))
                Assert.StartsWith("toy_", m.Groups[1].Value);
    }
}
