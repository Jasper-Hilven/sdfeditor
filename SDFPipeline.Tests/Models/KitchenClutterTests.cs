using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 28.3: the kitchen's worktop clutter (content/models/kitchen/ crumbs, rice, cereal loops, coffee beans, sprinkles, sugar and salt drifts,
/// peas) is 60 times life size, rests on y = 0, and is the size 28's "Ground clutter" table gives.
/// </summary>
public class KitchenClutterTests
{
    // The finder rounds out to its octree cells, up to some 14 cm on each axis at these sizes
    private const float Pad = 0.15f;

    private static SdfBounds Bounds(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "kitchen", name + ".sdf"))).Distance;
        var bounds = SdfBoundsFinder.Find(sdf, 8f);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the allowed range of the width (x), height (y) and depth (z); the wide, flat drifts are rounded out the most
    public static TheoryData<string, float, float, float, float, float, float> Models() => new()
    {
        { "crumb0", 0.4f, 0.5f, 0.15f, 0.25f, 0.3f, 0.4f },
        { "crumb1", 0.25f, 0.33f, 0.1f, 0.18f, 0.2f, 0.3f },
        { "crumb2", 0.12f, 0.2f, 0.08f, 0.14f, 0.1f, 0.18f },
        { "rice-grain0", 0.4f, 0.46f, 0.1f, 0.18f, 0.1f, 0.18f },
        { "rice-grain1", 0.4f, 0.46f, 0.1f, 0.18f, 0.1f, 0.18f },
        { "cereal-loop0", 0.75f, 0.9f, 0.22f, 0.35f, 0.75f, 0.9f },
        { "cereal-loop1", 0.75f, 0.9f, 0.22f, 0.35f, 0.75f, 0.9f },
        { "cereal-loop2", 0.75f, 0.9f, 0.18f, 0.28f, 0.75f, 0.9f },
        { "coffee-bean0", 0.68f, 0.82f, 0.28f, 0.4f, 0.45f, 0.62f },
        { "coffee-bean1", 0.64f, 0.82f, 0.28f, 0.4f, 0.45f, 0.62f },
        { "sprinkle0", 0.34f, 0.42f, 0.09f, 0.15f, 0.09f, 0.15f },
        { "sprinkle1", 0.34f, 0.42f, 0.09f, 0.15f, 0.09f, 0.15f },
        { "sprinkle2", 0.34f, 0.42f, 0.09f, 0.15f, 0.09f, 0.15f },
        { "sprinkle3", 0.34f, 0.42f, 0.09f, 0.15f, 0.09f, 0.15f },
        { "sugar-drift0", 2.2f, 3.3f, 0.14f, 0.4f, 1.8f, 2.6f },
        { "sugar-drift1", 1.4f, 1.9f, 0.12f, 0.3f, 1.3f, 1.9f },
        { "pea0", 0.45f, 0.55f, 0.45f, 0.55f, 0.45f, 0.55f },
        { "pea1", 0.9f, 1.1f, 0.45f, 0.58f, 0.85f, 1.05f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;

        Assert.True(size.X >= minX && size.X <= maxX, $"{name} is {size.X:0.00} m wide, not {minX} to {maxX}");
        Assert.True(size.Y >= minY && size.Y <= maxY, $"{name} is {size.Y:0.00} m high, not {minY} to {maxY}");
        Assert.True(size.Z >= minZ && size.Z <= maxZ, $"{name} is {size.Z:0.00} m deep, not {minZ} to {maxZ}");
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -Pad, 0.02f);
        // Centred on its origin, the base's middle (the drifts and the peas are off by what their lumps slip)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.4f, 0.4f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.4f, 0.4f);
    }

    /// <summary>Clutter is no cover: every model is well under the knee-cover height.</summary>
    [Theory]
    [InlineData("crumb0")]
    [InlineData("rice-grain0")]
    [InlineData("cereal-loop0")]
    [InlineData("coffee-bean0")]
    [InlineData("sprinkle0")]
    [InlineData("sugar-drift0")]
    [InlineData("pea1")]
    public void Clutter_IsUnderHalfAMetreHigh(string name) =>
        Assert.True(Bounds(name).Max.Y < 0.65f, $"{name} is {Bounds(name).Max.Y} m high");

    /// <summary>No scarlet or blue props: the kitchen's palette keeps those for the teams.</summary>
    [Fact]
    public void KitchenMaterials_HaveNoRedOrBlueBase()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var folder = Path.Combine(Path.GetDirectoryName(content)!, "materials", "kitchen");
        foreach (var file in Directory.GetFiles(folder, "*.sdfmat"))
        {
            var line = File.ReadAllLines(file).First(l => l.StartsWith("baseColor"));
            var values = line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float r = values[0], g = values[1], b = values[2];
            Assert.False(r > 0.6f && r > g + 0.35f && r > b + 0.35f, $"{Path.GetFileName(file)} is red");
            Assert.False(b > r + 0.25f && b > g + 0.1f, $"{Path.GetFileName(file)} is blue");
        }
    }
}
