using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 27.5: the toy box's tall blockers (alphabet blocks, Duplo towers, skittles, a spinning top), the railway (a 30 degree curve piece
/// that twelve make a ring of, a bridge with an arch for the road) and the three train cars (content/models/toys/) have the sizes 27's tables give.
/// </summary>
public class ToyBlockersTrackTrainTests
{
    private static Func<Vector3, float> Field(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "toys", name + ".sdf"))).Distance;
    }

    private static SdfBounds Bounds(string name, float halfExtent = 8f)
    {
        var bounds = SdfBoundsFinder.Find(Field(name), halfExtent);
        Assert.NotNull(bounds);
        return bounds;
    }

    private static bool Inside(Func<Vector3, float> field, float x, float y, float z) => field(new Vector3(x, y, z)) <= 0.005f;

    // Name, height range, widest footprint side range: the finder rounds out to its octree cells, up to some 25 cm at these sizes
    public static TheoryData<string, float, float, float, float> Blockers() => new()
    {
        { "alpha-block0", 2.3f, 2.7f, 2.2f, 2.7f },
        { "alpha-block1", 4.7f, 5.2f, 2.4f, 3.6f },
        { "alpha-block2", 7.1f, 7.5f, 2.6f, 4f },
        { "duplo-tower0", 4.5f, 4.9f, 3.8f, 5.8f },
        { "duplo-tower1", 6.7f, 7.2f, 3.8f, 6.3f },
        { "skittle0", 5.9f, 6.3f, 2.3f, 3.4f },
        { "skittle1", 5.9f, 6.3f, 2.1f, 3.2f },
        { "spinning-top0", 3.5f, 4.2f, 4.1f, 4.7f },
    };

    [Theory]
    [MemberData(nameof(Blockers))]
    public void TallBlocker_HasTheTablesHeight_AndStandsOnTheGround(string name, float minHeight, float maxHeight, float minSide, float maxSide)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;
        Assert.InRange(size.Y, minHeight, maxHeight);
        Assert.InRange(MathF.Max(size.X, size.Z), minSide, maxSide);
        Assert.InRange(bounds.Min.Y, -0.2f, 0.02f);
        // Centred on its origin, the base's middle (a stair of bricks or a top on its rim are off by a little)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.7f, 0.7f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.7f, 0.7f);
    }

    // ---- the curve piece

    private const float Radius = 20f;
    private static readonly float Step = MathF.PI / 6;

    private static Vector3 OnArc(float degrees, float radius, float y) =>
        new(radius * MathF.Sin(degrees * MathF.PI / 180), y, radius * MathF.Cos(degrees * MathF.PI / 180));

    [Fact]
    public void TrackCurve_StartsOnThePlusZAxis_AndEndsThirtyDegreesOn_AsItsHeadSays()
    {
        var field = Field("track-curve0");
        foreach (var radius in new[] { 19.2f, 20.8f })
        {
            // The rails run the whole arc: in just after the start and just before the end, out just beyond either
            Assert.True(field(OnArc(0.3f, radius, 0.2f)) <= 0.005f, $"rail at {radius} m is missing at the start");
            Assert.True(field(OnArc(29.7f, radius, 0.2f)) <= 0.005f, $"rail at {radius} m is missing at the end");
            Assert.True(field(OnArc(-0.6f, radius, 0.2f)) > 0f, $"rail at {radius} m starts early");
            Assert.True(field(OnArc(30.6f, radius, 0.2f)) > 0f, $"rail at {radius} m ends late");
        }
        // The end point of the centreline, (10, 0, 17.32), is the start's turned by 30 degrees
        var end = OnArc(30, Radius, 0);
        Assert.Equal(10f, end.X, 0.01f);
        Assert.Equal(17.32f, end.Z, 0.01f);
    }

    [Fact]
    public void TrackCurve_IsTwoPointFourWide_AndThreeTenthsHigh()
    {
        var field = Field("track-curve0");
        // A sleeper (the first sits half a pitch, 30/13/2 degrees, in) reaches across the 2.4 m
        var half = 30f / 13 / 2;
        Assert.True(field(OnArc(half, 18.9f, 0.05f)) <= 0.005f);
        Assert.True(field(OnArc(half, 21.1f, 0.05f)) <= 0.005f);
        Assert.True(field(OnArc(half, 21.5f, 0.05f)) > 0f);
        Assert.True(field(OnArc(half, 18.5f, 0.05f)) > 0f);
        // Rails to 0.3 m, nothing above
        Assert.True(field(OnArc(5, 19.2f, 0.28f)) <= 0.005f);
        Assert.True(field(OnArc(5, 19.2f, 0.36f)) > 0f);
    }

    [Fact]
    public void TwelveTrackCurves_AtThirtyDegreeYawSteps_CloseARing()
    {
        var field = Field("track-curve0");
        foreach (var radius in new[] { 19.2f, 20.8f })
            for (var degrees = 0f; degrees < 360f; degrees += 1f)
            {
                var p = OnArc(degrees + 0.25f, radius, 0.2f);
                var covered = false;
                for (var k = 0; k < 12 && !covered; k++)
                {
                    // Copy k is the piece turned k steps about the centre: look up the point turned back
                    var back = Matrix4x4.CreateRotationY(k * Step);
                    covered = field(Vector3.Transform(p, back)) <= 0.005f;
                }
                Assert.True(covered, $"the rail at {radius} m has a gap at {degrees} degrees");
            }
    }

    // ---- the bridge

    [Fact]
    public void TrackBridge_RampsAndDeckClimbToThreePointEight_OverAnArchForTheRoad()
    {
        var field = Field("track-bridge0");
        // The deck's track on top, to 3.8 m
        Assert.True(Inside(field, 0.8f, 3.75f, 0f), "rail on the deck");
        Assert.False(Inside(field, 0f, 3.95f, 0f), "nothing over 3.8 m");
        Assert.False(Inside(field, 0.8f, 4.0f, 0f), "nothing over 3.8 m");
        // Both ramps carry a slab under the track: the top 3.5 m at the deck's end falling to the ground over 8 m
        foreach (var sign in new[] { 1f, -1f })
        {
            Assert.True(Inside(field, 0f, 1.6f, sign * 8f), "ramp slab at 8 m");
            Assert.False(Inside(field, 0f, 0.3f, sign * 8f), "open under the ramp between its bents");
            Assert.False(Inside(field, 0f, 2.2f, sign * 8f), "nothing over the ramp's top");
            Assert.True(Inside(field, 0f, 0.1f, sign * 11.5f), "ramp's foot");
        }
        // The arch: 6 m wide (z +-3) and 3.2 m clear at the crown, the road runs through along x
        Assert.False(Inside(field, 0f, 3.1f, 0f), "arch crown clear to 3.1 m");
        Assert.True(Inside(field, 0f, 3.3f, 0f), "deck over the crown");
        Assert.False(Inside(field, 0f, 1f, 2.9f), "arch edge");
        Assert.False(Inside(field, 0f, 1f, -2.9f), "arch edge");
        Assert.True(Inside(field, 0f, 1f, 3.5f), "pier");
        Assert.True(Inside(field, 0f, 1f, -3.5f), "pier");
    }

    [Fact]
    public void TrackBridge_IsTwentyFourLong_AndTwoPointFourWide()
    {
        var bounds = Bounds("track-bridge0", 16f);
        var size = bounds.Max - bounds.Min;
        // Cells of about 80 cm round it out
        Assert.InRange(size.Z, 24f, 26.5f);
        Assert.InRange(size.X, 2.4f, 4.2f);
        Assert.InRange(size.Y, 3.8f, 5f);
        Assert.InRange(bounds.Min.Y, -0.6f, 0.02f);
    }

    // ---- the train

    [Theory]
    [InlineData("toy-train-engine", 2.5f, 2.95f)]
    [InlineData("toy-train-flat", 1f, 1.35f)]
    [InlineData("toy-train-wagon", 1.75f, 2.2f)]
    public void TrainCar_HasItsHeightAndLength_StandsOnItsWheels_AndIsCentred(string name, float minHeight, float maxHeight)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;
        Assert.InRange(size.Y, minHeight, maxHeight);
        // 1.8 m wide, rounded out by its cells
        Assert.InRange(size.X, 1.8f, 2.4f);
        // The body 4.8 / 4.2 m and a 0.4 m coupler at each end
        var body = name == "toy-train-engine" ? 4.8f : 4.2f;
        Assert.InRange(size.Z, body + 0.8f - 0.05f, body + 0.8f + 0.7f);
        // Wheels' bottoms on y = 0, travelling along +z from the middle of the car
        Assert.InRange(bounds.Min.Y, -0.2f, 0.02f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.3f, 0.3f);
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.15f, 0.15f);
    }

    [Fact]
    public void FlatWagonDeck_IsOneMetreUp_AndTheOpenWagonIsHollow()
    {
        var flat = Field("toy-train-flat");
        Assert.True(Inside(flat, 0f, 0.95f, 0f));
        Assert.False(Inside(flat, 0f, 1.05f, 0f));
        Assert.True(Inside(flat, 0.85f, 0.95f, 1.9f));

        var wagon = Field("toy-train-wagon");
        Assert.True(Inside(wagon, 0.85f, 1.75f, 0f), "side rim at 1.8 m");
        Assert.False(Inside(wagon, 0f, 1.5f, 0f), "open top");
    }

    [Fact]
    public void TrainCars_HaveACouplerAtEachEnd_ThatMeetsTheNextOne()
    {
        // The knob of a 0.4 m bar sits at z = body/2 + 0.4 in front and behind: two cars a gap of 0.8 apart touch
        foreach (var (name, half) in new[] { ("toy-train-engine", 2.4f), ("toy-train-flat", 2.1f), ("toy-train-wagon", 2.1f) })
        {
            var field = Field(name);
            Assert.True(Inside(field, 0f, 0.55f, half + 0.38f), $"{name} front knob");
            Assert.True(Inside(field, 0f, 0.55f, -half - 0.38f), $"{name} rear knob");
        }
    }
}
