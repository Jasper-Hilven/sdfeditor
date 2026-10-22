using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 27.6: the toy box's six landmarks (jack-in-the-box, block tower, rocking horse, ring stacker, Ferris wheel, teddy; content/models/toys/) have the
/// sizes 27's Landmarks table gives, and the openings they exist for (the tower's window, the horse's belly, the wheel's frame) are open.
/// </summary>
public class ToyLandmarksTests
{
    private static Func<Vector3, float> Field(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "toys", name + ".sdf"))).Distance;
    }

    private static SdfBounds Bounds(string name)
    {
        var bounds = SdfBoundsFinder.Find(Field(name), 20f);
        Assert.NotNull(bounds);
        return bounds;
    }

    private static bool Inside(Func<Vector3, float> field, float x, float y, float z) => field(new Vector3(x, y, z)) <= 0.005f;

    // Name, then the size ranges in x, y and z: the finder rounds out to its octree cells, up to some 25 cm at these sizes
    public static TheoryData<string, float, float, float, float, float, float> Landmarks() => new()
    {
        { "jack-in-the-box0", 4.8f, 6.0f, 10.8f, 11.6f, 4.8f, 6.0f },
        { "block-tower0", 5.2f, 6.6f, 10.2f, 11.5f, 5.2f, 6.6f },
        { "rocking-horse0", 3.7f, 5.2f, 8.8f, 9.8f, 11.7f, 13.2f },
        { "ring-stacker0", 7.8f, 8.8f, 10.8f, 12.0f, 7.8f, 8.8f },
        { "ferris-wheel0", 11.7f, 14.6f, 13.7f, 14.8f, 3.7f, 5.8f },
        { "teddy0", 12.5f, 13.6f, 15.5f, 16.7f, 8.6f, 10.6f },
    };

    [Theory]
    [MemberData(nameof(Landmarks))]
    public void Landmark_HasTheTablesSize_AndStandsOnTheGround(string name, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;
        Assert.InRange(size.X, minX, maxX);
        Assert.InRange(size.Y, minY, maxY);
        Assert.InRange(size.Z, minZ, maxZ);
        Assert.InRange(bounds.Min.Y, -0.4f, 0.05f);
        // Centred on its origin, the middle of its footprint (the lean, the lid and the legs shift it a little)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.8f, 0.8f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -1.2f, 1.2f);
    }

    [Fact]
    public void BlockTower_HasAWindowAtFiveMetres_AlongZ_AndIsSolidBesideIt()
    {
        var field = Field("block-tower0");
        // The third layer (y 3.3 to 4.95) lacks its two middle blocks; the layers' lean moves the window about 0.25 m towards +x
        for (var z = -4f; z <= 4f; z += 0.25f)
            Assert.False(Inside(field, 0.25f, 4.1f, z), $"the window is shut at z = {z}");
        // Beside it the row's end blocks stand, and the layers under and over the window are whole
        Assert.True(Inside(field, -1.4f, 4.1f, 0.9f));
        Assert.True(Inside(field, 1.9f, 4.1f, 0.9f));
        Assert.True(Inside(field, 0.25f, 2.0f, 0f));
        Assert.True(Inside(field, 0.3f, 6.2f, 0f));
        // The mid-layer is whole: the layer below is full width where the window's floor is
        Assert.True(Inside(field, 0.3f, 2.5f, -1.5f));
    }

    [Fact]
    public void RockingHorse_BellyIsAtFourMetres_WithNothingUnderIt_AndRockersOnTheGround()
    {
        var field = Field("rocking-horse0");
        // Under the belly between the legs and the rockers it is open all the way along x at belly height
        for (var x = -1.2f; x <= 1.2f; x += 0.2f)
            Assert.False(Inside(field, x, 3.5f, 0f), $"something under the belly at x = {x}");
        Assert.True(Inside(field, 0f, 4.2f, -0.5f), "the belly");
        Assert.False(Inside(field, 0f, 3.8f, -0.5f), "the belly hangs below 4 m");
        // The rockers: 0.8 m thick arcs, the bottom of the arc on the ground, beech
        Assert.True(Inside(field, 1.6f, 0.4f, 0f));
        Assert.True(Inside(field, -1.6f, 0.4f, 0f));
        Assert.False(Inside(field, 1.6f, 1.3f, 0f), "above the arc between the legs");
    }

    [Fact]
    public void FerrisWheel_LegsLeaveFourMetresOrMore_UnderFourMetres()
    {
        var field = Field("ferris-wheel0");
        foreach (var z in new[] { -1.75f, 1.75f })
            for (var y = 0.8f; y <= 4f; y += 0.4f)
            {
                // The leg runs from the hub (0, 8) to the foot (5, 0.4): half the clear gap, less the leg's 0.4 m radius across its slant (0.5 m flat)
                var legX = 5f * (8f - y) / 7.6f;
                var halfGap = legX - 0.5f;
                Assert.True(halfGap >= 2f, $"legs closer than 4 m at y = {y}");
                for (var x = -(halfGap - 0.05f); x <= halfGap - 0.05f; x += 0.25f)
                    Assert.False(Inside(field, x, y, z), $"a part stands between the legs at ({x}, {y}, {z})");
            }
        // The wheel and its hub are there: the hub brass, the rim at the top, and the legs themselves
        Assert.True(Inside(field, 0f, 8f, 0f), "hub");
        Assert.True(Inside(field, 0f, 13.8f, 1f), "rim at the top");
        Assert.True(Inside(field, 1.6f, 4.9f, 1.75f), "a leg");
        // Six gondolas hang from the rim: one at 30 degrees from the top
        Assert.True(Inside(field, 5.7f * MathF.Sin(MathF.PI / 6), 8 + 5.7f * MathF.Cos(MathF.PI / 6) - 1.35f, 0f), "a gondola cab");
    }

    [Fact]
    public void RingStacker_HasFiveRingsOnAPeg_AndAYellowBallOnTop()
    {
        var field = Field("ring-stacker0");
        // Ring middles at 1.75, 3.55, 5.3, 7.0 and 8.65 m, the ring's centre line at 2.95, 2.7, 2.45, 2.2 and 1.95 m from the axis
        var ys = new[] { 1.75f, 3.65f, 5.4f, 7.05f, 8.65f };
        var radii = new[] { 3.05f, 2.7f, 2.4f, 2.1f, 1.65f };
        for (var i = 0; i < 5; i++)
            Assert.True(Inside(field, radii[i], ys[i], 0f), $"ring {i}");
        Assert.True(Inside(field, 0f, 10.3f, 0f), "peg");
        Assert.True(Inside(field, 0f, 10.9f, 0f), "ball");
        Assert.False(Inside(field, 0f, 11.4f, 0f), "nothing over 11 m");
    }

    [Fact]
    public void JackInTheBox_HasTheBoxOpen_ASpring_AndAJesterToElevenMetres()
    {
        var field = Field("jack-in-the-box0");
        Assert.True(Inside(field, 2.3f, 2f, 0f), "box wall");
        Assert.False(Inside(field, 1.5f, 3.2f, 1.5f), "the box is open");
        Assert.True(Inside(field, 1.0f, 4.9f, 0f), "spring");
        Assert.True(Inside(field, 0f, 9.35f, 0f), "jester head");
        Assert.True(Inside(field, 1.7f, 10.4f, 0f), "bell at the cap's horn");
        Assert.False(Inside(field, 0f, 11.4f, 0f), "nothing over 11 m");
    }

    [Fact]
    public void Teddy_SitsWithTheBackToTheWall_LegsOutAndHeadHigh()
    {
        var field = Field("teddy0");
        Assert.True(Inside(field, 3f, 2f, 3f), "a leg out in front");
        Assert.True(Inside(field, -3f, 2f, 3f), "the other leg");
        Assert.True(Inside(field, 0f, 7f, -1.7f), "the torso");
        Assert.True(Inside(field, 0.6f, 11.3f, 0f), "the head");
        // Between the legs, in front of the belly, the ground is free
        Assert.False(Inside(field, 0f, 1f, 4f));
    }
}
