using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 30.4: the computer level's knee cover (SMD parts 0.6-1.1 m high) and tall blockers (capacitors, choke, pin header, pin heatsink, SATA
/// ports, PCIe x1 slot) in content/models/computer/ are 250 times life size, rest on y = 0, are centred on their origin and are the size the
/// level's tables give; every material they name exists.
/// </summary>
public class ComputerKneeAndTallTests
{
    // The finder rounds out to its octree cells, up to some 35 cm on the biggest of these
    private const float Pad = 0.35f;

    private static SdfBounds Bounds(string name, float search = 4f)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "computer", name + ".sdf"))).Distance;
        var bounds = SdfBoundsFinder.Find(sdf, search);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the table's width (x), height (y) and depth (z)
    public static TheoryData<string, float, float, float> Models() => new()
    {
        { "tantalum-cap0", 1.8f, 0.72f, 1.1f },
        { "smd-inductor0", 1.5f, 0.75f, 1.5f },
        { "crystal-smd0", 2.85f, 1.05f, 1.2f },
        { "to220-lying0", 4f, 1.1f, 2.5f },
        { "screw-mount0", 1.5f, 0.63f, 1.5f },
        { "smd-can-cap0", 1f, 1.1f, 1f },
        { "cap-electrolytic0", 2f, 3f, 2f },
        { "cap-electrolytic1", 2.5f, 4f, 2.5f },
        { "cap-electrolytic2", 2.5f, 5f, 2.5f },
        { "cap-big0", 4f, 6.25f, 4f },
        { "choke0", 3.25f, 2.5f, 3.25f },
        { "pin-header0", 3.2f, 2.1f, 1.3f },
        { "heatsink-pin0", 3.5f, 2.5f, 3.5f },
        { "sata-ports0", 3.75f, 3.75f, 2.5f },
        { "pcie-x1-slot0", 6.25f, 2.75f, 1.9f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float width, float height, float depth)
    {
        var bounds = Bounds(name, Math.Max(width, Math.Max(height, depth)) > 4.5f ? 8f : 4f);
        var size = bounds.Max - bounds.Min;

        Assert.InRange(size.X, width - 0.05f, width + Pad);
        Assert.InRange(size.Y, height - 0.05f, height + Pad);
        Assert.InRange(size.Z, depth - 0.05f, depth + Pad);
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -Pad, 0.02f);
        // Centred on its origin, the base's middle
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.3f, 0.3f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.3f, 0.3f);
    }

    [Theory]
    [InlineData("tantalum-cap0")]
    [InlineData("smd-inductor0")]
    [InlineData("crystal-smd0")]
    [InlineData("to220-lying0")]
    [InlineData("screw-mount0")]
    [InlineData("smd-can-cap0")]
    public void KneeCover_StaysBetweenCrouchAndJumpHeight(string name)
    {
        var height = Bounds(name).Max.Y;
        Assert.InRange(height, 0.6f, 1.1f + Pad);
    }

    [Fact]
    public void PinHeader_PinsAreCloserThanAPlayerCanSlipThrough()
    {
        // Ten pins in two rows of five at a 0.635 m pitch, each 0.16 m thick: 0.475 m between them, so the footprint is shut
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var text = File.ReadAllText(Path.Combine(content, "models", "computer", "pin-header0.sdf"));
        Assert.Contains("spacing=0.635", text);
        Assert.Contains("Box size=(0.08, 0.75, 0.08)", text);
    }

    [Fact]
    public void EveryMaterialTheModelsNameExists()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var root = Path.GetDirectoryName(content)!;
        foreach (var name in Models().Select(row => (string)row[0]!))
        {
            var text = File.ReadAllText(Path.Combine(content, "models", "computer", name + ".sdf"));
            var materials = Regex.Matches(text, @"material=(\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(materials);
            foreach (var material in materials)
            {
                var file = Path.Combine(root, "materials", "computer", material + ".sdfmat");
                Assert.True(File.Exists(file), $"{name} names {material}, which is missing");
                SDFPipeline.Materials.MaterialFile.Read(File.ReadAllText(file));
            }
        }
    }
}
