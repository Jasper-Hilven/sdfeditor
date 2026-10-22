using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 28.4: the kitchen's knee cover and tall blockers (content/models/kitchen/ sugar cubes, cookies, butter, caps, lid, teaspoon, sponge, garlic, tins, jars,
/// shaker, mugs, bottle, carton, plates) are 60 times life size, rest on y = 0, and are the size 28's "Knee cover" and "Tall blockers" tables give.
/// </summary>
public class KitchenKneeBlockersTests
{
    private static SdfBounds Bounds(string name, float halfExtent = 16f)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "kitchen", name + ".sdf"))).Distance;
        var bounds = SdfBoundsFinder.Find(sdf, halfExtent);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the model's real width (x), height (y) and depth (z) in metres. The bounds finder rounds out to its octree cells, by about a tenth of the size
    public static TheoryData<string, float, float, float> Models() => new()
    {
        { "sugar-cube0", 1.17f, 0.96f, 1.17f },
        { "sugar-cube1", 1.3f, 1.1f, 1.3f },
        { "sugar-cube2", 2.3f, 1.35f, 1.2f },
        { "cookie0", 3.6f, 0.72f, 3.6f },
        { "cookie-bitten0", 3.6f, 0.72f, 3.6f },
        { "butter-pat0", 1.8f, 0.9f, 1.8f },
        { "bottle-cap0", 1.9f, 0.46f, 1.9f },
        { "bottle-cap1", 1.9f, 0.46f, 1.9f },
        { "jar-lid0", 4.2f, 0.72f, 4.2f },
        { "teaspoon0", 8.4f, 1.0f, 2.4f },
        { "sponge0", 6.5f, 1.5f, 4.9f },
        { "garlic-clove0", 1.8f, 1.0f, 0.85f },
        { "tin-can0", 4.5f, 6.66f, 4.5f },
        { "tin-can1", 4.5f, 7.2f, 4.5f },
        { "tin-can-lying0", 6.6f, 4.5f, 4.5f },
        { "jam-jar0", 4.2f, 5.4f, 4.2f },
        { "honey-jar0", 4.8f, 6.0f, 4.8f },
        { "spice-jar0", 2.7f, 6.0f, 2.7f },
        { "spice-jar1", 2.7f, 6.0f, 2.7f },
        { "salt-shaker0", 3.0f, 4.8f, 3.0f },
        { "mug0", 6.15f, 6.0f, 4.8f },
        { "mug-tipped0", 7.0f, 4.8f, 6.1f },
        { "vinegar-bottle0", 3.6f, 12.0f, 3.6f },
        { "milk-carton0", 4.2f, 14.4f, 4.2f },
        { "plate-stack-low0", 10.2f, 3.6f, 10.2f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float width, float height, float depth)
    {
        // The finder starts from a cube just over the model's longest side (the models stand on y = 0, so it must reach their height): a bigger one makes tall, thin models come out coarser
        var bounds = Bounds(name, 1.1f * Math.Max(width, Math.Max(height, depth)));
        var size = bounds.Max - bounds.Min;

        var longest = Math.Max(width, Math.Max(height, depth));
        AssertSize(name, "wide", size.X, width, longest);
        AssertSize(name, "high", size.Y, height, longest);
        AssertSize(name, "deep", size.Z, depth, longest);
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -(0.15f + 0.06f * height + 0.03f * longest), 0.03f);
        // Centred on its origin (the spoon, the lying tin and the tipped mug hold a little more to one side)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -(0.3f + 0.08f * width), 0.3f + 0.08f * width);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -(0.3f + 0.08f * depth), 0.3f + 0.08f * depth);
    }

    // The finder rounds out to its octree cells and stops early on thin or tall shapes, so it can come out a tenth of the longest side too big (the meshes are
    // within 0.1 m of the tables' sizes); the lower limit is tight
    private static void AssertSize(string name, string axis, float actual, float expected, float longest) =>
        Assert.True(actual >= expected - 0.15f && actual <= expected * 1.15f + 0.3f + longest / 10, $"{name} is {actual:0.00} m {axis}, not about {expected}");

    /// <summary>Knee cover stays under 1.6 m (the sponge is the tallest, a low wall); tall blockers are over 3.4 m, so each is one or the other.</summary>
    [Theory]
    [InlineData("sugar-cube2", true)]
    [InlineData("cookie0", true)]
    [InlineData("butter-pat0", true)]
    [InlineData("bottle-cap0", true)]
    [InlineData("jar-lid0", true)]
    [InlineData("teaspoon0", true)]
    [InlineData("sponge0", true)]
    [InlineData("garlic-clove0", true)]
    [InlineData("tin-can-lying0", false)]
    [InlineData("jam-jar0", false)]
    [InlineData("spice-jar0", false)]
    [InlineData("salt-shaker0", false)]
    [InlineData("mug-tipped0", false)]
    [InlineData("plate-stack-low0", false)]
    public void Height_PutsTheModelInItsClass(string name, bool kneeCover)
    {
        var top = Bounds(name).Max.Y;
        if (kneeCover)
            Assert.True(top < 1.7f, $"{name} is {top} m high, over knee cover");
        else
            Assert.True(top > 3.4f, $"{name} is {top} m high, under a tall blocker");
    }
}
