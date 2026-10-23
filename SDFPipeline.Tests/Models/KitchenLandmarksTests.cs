using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 28.5: the kitchen's landmarks (content/models/kitchen/ sink, tap, rolling pin, burners, kettle, plate stack, pepper mill, toppled cereal box, fruit bowl,
/// cake, blender) are 60 times life size and the size 28's "Landmarks" table gives; the sink is deeper than the match's kill depth.
/// </summary>
public class KitchenLandmarksTests
{
    private static string ContentFolder()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static Func<Vector3, float> Distance(string name) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(ContentFolder(), "models", "kitchen", name + ".sdf"))).Distance;

    private static SdfBounds Bounds(string name, float halfExtent)
    {
        var bounds = SdfBoundsFinder.Find(Distance(name), halfExtent);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the model's real width (x), height (y: top above y = 0) and depth (z) in metres. The bounds finder rounds out to its octree cells, by about a tenth of the size
    public static TheoryData<string, float, float, float> Models() => new()
    {
        { "tap0", 3.0f, 18.05f, 15.5f },
        { "burner-large", 10.8f, 0.16f, 10.8f },
        { "burner-small", 8.4f, 0.16f, 8.4f },
        { "kettle0", 9.6f, 13.0f, 9.3f },
        { "plate-stack0", 10.2f, 6.0f, 10.2f },
        { "pepper-mill0", 3.6f, 12.4f, 3.6f },
        { "cereal-box-toppled0", 18.0f, 12.3f, 6.0f },
        { "fruit-bowl0", 15.0f, 9.3f, 15.0f },
        { "cake0", 12.0f, 6.2f, 12.0f },
        { "blender0", 8.6f, 15.0f, 7.65f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float width, float height, float depth)
    {
        var longest = Math.Max(width, Math.Max(height, depth));
        // The finder starts from a cube round the origin that must hold the whole model
        var bounds = Bounds(name, 1.1f * longest);
        var size = bounds.Max - bounds.Min;

        AssertSize(name, "wide", size.X, width, longest);
        AssertSize(name, "high", size.Y, height, longest);
        AssertSize(name, "deep", size.Z, depth, longest);
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -(0.15f + 0.06f * height + 0.03f * longest), 0.03f);
        // The origin is inside the footprint (the spout, the handle, the spill and the crank put the middle off it)
        Assert.InRange(bounds.Min.X, -width, 0.1f);
        Assert.InRange(bounds.Max.X, -0.1f, width);
        Assert.InRange(bounds.Min.Z, -depth, 0.1f);
        Assert.InRange(bounds.Max.Z, -0.1f, depth);
    }

    // The finder rounds out to its octree cells and stops early on thin or tall shapes, so it can come out a tenth of the longest side too big; the lower limit is tight
    private static void AssertSize(string name, string axis, float actual, float expected, float longest) =>
        Assert.True(actual >= expected - 0.15f && actual <= expected * 1.15f + 0.3f + longest / 10, $"{name} is {actual:0.00} m {axis}, not about {expected}");

    /// <summary>The sink is 30 x 25 m with its rim at y = 0 and the bowl 13.2 m down: the rim flush with the ground, the opening the size of the ground's hole.</summary>
    [Fact]
    public void Sink_IsTheTablesSize_WithItsRimAtTheGround()
    {
        var bounds = Bounds("sink0", 17f);
        Assert.InRange(bounds.Max.X - bounds.Min.X, 29.9f, 34f);
        Assert.InRange(bounds.Max.Z - bounds.Min.Z, 24.9f, 29f);
        Assert.InRange(bounds.Max.Y, -0.05f, 0.9f);
        Assert.InRange(bounds.Min.Y, -15.5f, -13.1f);

        var distance = Distance("sink0");
        // The rim's top is the ground's height, 1.5 m wide round a 27 x 22 m opening
        Assert.True(distance(new Vector3(14.2f, -0.1f, 0)) < 0, "the rim is solid just under y = 0");
        Assert.True(distance(new Vector3(14.2f, 0.05f, 0)) > 0, "nothing stands over the rim");
        Assert.True(distance(new Vector3(13.2f, -3, 0)) > 0, "the opening is 27 m wide below the rim");
        Assert.True(distance(new Vector3(13.8f, -3, 0)) < 0, "the bowl's wall is next to it");
        Assert.True(distance(new Vector3(0, -3, 10.8f)) > 0, "the opening is 22 m deep below the rim");
    }

    /// <summary>
    /// A scout who falls in dies because the bowl is deeper than <c>Match.killDepth</c>. That constant lives in Unity code (Unity/Assets/Battle/Runtime/Match.cs), which
    /// this test project does not reference, so the test reads its value from the source text: changing either one without the other fails here.
    /// </summary>
    [Fact]
    public void Sink_IsDeeperThanTheMatchKillDepth()
    {
        var root = Path.GetDirectoryName(ContentFolder())!;
        var source = File.ReadAllText(Path.Combine(root, "Unity", "Assets", "Battle", "Runtime", "Match.cs"));
        var match = Regex.Match(source, @"public\s+float\s+killDepth\s*=\s*([0-9.]+)f?\s*;");
        Assert.True(match.Success, "Match.killDepth is no longer a plain 'public float killDepth = <number>;'");
        var killDepth = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        // The bowl's floor (its top 12.7 m down), away from the water and the drain: air just over it, steel just under it
        var distance = Distance("sink0");
        Assert.True(distance(new Vector3(-6, -killDepth - 0.5f, 8)) > 0, "above the floor is empty");
        Assert.True(distance(new Vector3(-6, -killDepth - 0.9f, 8)) < 0, "the floor is steel");
        Assert.True(Bounds("sink0", 17f).Min.Y < -killDepth, "the bowl reaches below the kill depth");
        // Not so deep that it is a different level's pit: the table says 13.2 m
        Assert.True(Bounds("sink0", 17f).Min.Y > -killDepth - 3.5f);
    }

    /// <summary>The rolling pin is 30 m long along z and 3 m thick in the middle, 1.1 m at the tips, with its tips on y = 0 and its top rising to the middle.</summary>
    [Fact]
    public void RollingPin_IsALongTaperedBridge()
    {
        var distance = Distance("rolling-pin0");
        var bounds = Bounds("rolling-pin0", 17f);
        Assert.InRange(bounds.Max.Z - bounds.Min.Z, 29.8f, 35f);
        // Middle top 2.05 m, tip top 1.15 m (a tip is jumped onto from the rim)
        Assert.True(distance(new Vector3(0, 2.0f, 0)) < 0 && distance(new Vector3(0, 2.15f, 0)) > 0);
        Assert.True(distance(new Vector3(0, 1.1f, 14.4f)) < 0 && distance(new Vector3(0, 1.3f, 14.4f)) > 0);
        // The pin is walkable from the tip: its top rises steadily from tip to middle
        var previous = 0f;
        for (var z = 14.4f; z >= 0; z -= 1.2f)
        {
            var top = Top(distance, z);
            Assert.True(top >= previous - 0.02f, $"the pin's top falls at z = {z}");
            previous = top;
        }
    }

    private static float Top(Func<Vector3, float> distance, float z)
    {
        var y = 3f;
        while (y > -2f && distance(new Vector3(0, y, z)) > 0) y -= 0.01f;
        return y;
    }

    /// <summary>The burners are flat dark rings: no taller than a coil, wide as the table says, centred on their origin.</summary>
    [Theory]
    [InlineData("burner-large", 10.8f)]
    [InlineData("burner-small", 8.4f)]
    public void Burner_IsAFlatRingOnTheGlass(string name, float across)
    {
        var distance = Distance(name);
        Assert.True(distance(new Vector3(0, 0.08f, 0)) < 0, "the centre plate is solid");
        Assert.True(distance(new Vector3(0, 0.25f, 0)) > 0, "it is under 0.2 m high");
        Assert.True(distance(new Vector3(across / 2 - 0.2f, 0.08f, 0)) < 0, "the outer coil reaches the table's size");
        Assert.True(distance(new Vector3(across / 2 + 0.2f, 0.08f, 0)) > 0);
    }
}
