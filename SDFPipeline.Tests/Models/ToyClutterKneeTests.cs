using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 27.4: the toy box's ground clutter (content/models/toys/ puzzle pieces, confetti, brick plates, beads, a crayon stub) and knee
/// cover (bricks, a brick stack, marbles, a crayon pile, toy cars, a jack, an arch block) are 60 times life size, rest on y = 0, stay under
/// the knee-cover height (the clutter under half a metre) and are the size the level's tables give.
/// </summary>
public class ToyClutterKneeTests
{
    // The finder rounds out to its octree cells, up to some 14 cm on each axis at these sizes
    private const float Pad = 0.15f;

    private static SdfBounds Bounds(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "toys", name + ".sdf"))).Distance;
        var bounds = SdfBoundsFinder.Find(sdf, 8f);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the allowed range of the width (x), height (y) and depth (z); the finder rounds out to its octree cells, a few centimetres
    public static TheoryData<string, float, float, float, float, float, float> Models() => new()
    {
        { "puzzle-piece0", 1.1f, 1.3f, 0.1f, 0.2f, 1.1f, 1.3f },
        { "puzzle-piece1", 1.1f, 1.3f, 0.1f, 0.2f, 1.1f, 1.3f },
        { "puzzle-piece2", 1.1f, 1.3f, 0.1f, 0.2f, 1.1f, 1.3f },
        { "confetti0", 0.9f, 1.45f, 0.01f, 0.08f, 0.9f, 1.45f },
        { "confetti1", 0.9f, 1.45f, 0.01f, 0.08f, 0.9f, 1.45f },
        { "confetti2", 0.9f, 1.45f, 0.01f, 0.08f, 0.9f, 1.45f },
        { "confetti3", 0.9f, 1.45f, 0.01f, 0.08f, 0.9f, 1.45f },
        { "brick-plate0", 0.44f, 0.6f, 0.18f, 0.3f, 0.44f, 0.6f },
        { "brick-plate1", 0.9f, 1.1f, 0.18f, 0.3f, 0.44f, 0.6f },
        { "bead0", 0.5f, 0.65f, 0.38f, 0.5f, 0.5f, 0.65f },
        { "bead1", 0.5f, 0.65f, 0.38f, 0.5f, 0.5f, 0.65f },
        { "bead2", 0.5f, 0.65f, 0.38f, 0.55f, 0.5f, 0.65f },
        { "crayon-stub0", 1.1f, 1.3f, 0.44f, 0.55f, 0.44f, 0.55f },
        { "brick0", 1.85f, 2.05f, 0.65f, 0.78f, 0.9f, 1.05f },
        { "brick1", 1.85f, 2.4f, 0.65f, 0.78f, 0.9f, 2f },
        { "brick-stack0", 2.7f, 3.3f, 1f, 1.15f, 1.7f, 2.2f },
        { "toy-marble0", 0.9f, 1.05f, 0.9f, 1.05f, 0.9f, 1.05f },
        { "toy-marble1", 0.9f, 1.05f, 0.9f, 1.05f, 0.9f, 1.05f },
        { "toy-marble2", 0.9f, 1.05f, 0.9f, 1.05f, 0.9f, 1.05f },
        { "crayon-pile0", 5.3f, 6.1f, 0.9f, 1.1f, 1.5f, 2.1f },
        { "toy-car0", 1.4f, 1.65f, 0.9f, 1.05f, 3.1f, 3.35f },
        { "toy-car1", 1.4f, 1.65f, 0.9f, 1.05f, 3.1f, 3.35f },
        { "toy-car-tipped0", 0.9f, 1.1f, 1.4f, 1.65f, 3.1f, 3.35f },
        { "jack0", 0.75f, 1.1f, 0.6f, 0.8f, 0.75f, 1.1f },
        { "arch-block0", 2.3f, 2.5f, 1f, 1.15f, 1.1f, 1.3f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;

        Assert.True(size.X >= minX && size.X <= maxX + Pad, $"{name} is {size.X:0.00} m wide, not {minX} to {maxX}");
        Assert.True(size.Y >= minY && size.Y <= maxY + Pad, $"{name} is {size.Y:0.00} m high, not {minY} to {maxY}");
        Assert.True(size.Z >= minZ && size.Z <= maxZ + Pad, $"{name} is {size.Z:0.00} m deep, not {minZ} to {maxZ}");
        // Resting on y = 0, the finder's cells aside (the big crayon pile has the biggest)
        Assert.InRange(bounds.Min.Y, -Pad, 0.02f);
        // Centred on its origin, the base's middle (the crayon pile is off by what its crayons slip)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.4f, 0.4f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.4f, 0.4f);
    }

    [Theory]
    [InlineData("puzzle-piece0")]
    [InlineData("confetti0")]
    [InlineData("brick-plate0")]
    [InlineData("bead0")]
    [InlineData("crayon-stub0")]
    public void Clutter_IsUnderHalfAMetreHigh(string name) =>
        Assert.True(Bounds(name).Max.Y < 0.65f, $"{name} is {Bounds(name).Max.Y} m high");

    [Theory]
    [InlineData("brick0")]
    [InlineData("brick-stack0")]
    [InlineData("toy-marble0")]
    [InlineData("crayon-pile0")]
    [InlineData("toy-car0")]
    [InlineData("jack0")]
    [InlineData("arch-block0")]
    public void KneeCover_IsAboutAMetreHigh_Jumpable(string name)
    {
        var height = Bounds(name).Max.Y;
        Assert.InRange(height, 0.6f, 1.3f);
    }

    [Fact]
    public void TheTippedCar_IsTheHighestKneeCover_AsTheTableSays()
    {
        Assert.InRange(Bounds("toy-car-tipped0").Max.Y, 1.4f, 1.8f);
    }

    /// <summary>No red or blue props: the toy box's palette keeps those for the teams.</summary>
    [Fact]
    public void ToyMaterials_HaveNoRedOrBlueBase()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var folder = Path.Combine(Path.GetDirectoryName(content)!, "materials", "toys");
        foreach (var file in Directory.GetFiles(folder, "*.sdfmat"))
        {
            var line = File.ReadAllLines(file).First(l => l.StartsWith("baseColor"));
            var values = line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float r = values[0], g = values[1], b = values[2];
            // Red: red far above green and blue. Blue: blue the highest channel, over green by a clear margin
            Assert.False(r > 0.6f && r > g + 0.35f && r > b + 0.35f, $"{Path.GetFileName(file)} is red");
            Assert.False(b > r + 0.25f && b > g + 0.1f, $"{Path.GetFileName(file)} is blue");
        }
    }
}
