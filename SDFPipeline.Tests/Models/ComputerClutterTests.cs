using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 30.3: the computer level's ground clutter (content/models/computer/: SMD parts, solder blobs, a via pad, dust bunnies, a zip tie
/// tail, a thermal paste dot) is 250 times life size, rests on y = 0, is centred on its origin and is the size the level's tables give;
/// every material the models and the ground regions name exists.
/// </summary>
public class ComputerClutterTests
{
    // The finder rounds out to its octree cells, up to some 25 cm on the biggest of these
    private const float Pad = 0.1f;

    private static SdfBounds Bounds(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "computer", name + ".sdf"))).Distance;
        var bounds = SdfBoundsFinder.Find(sdf, 4f);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the allowed range of the width (x), height (y) and depth (z)
    public static TheoryData<string, float, float, float, float, float, float> Models() => new()
    {
        { "smd-resistor0", 0.48f, 0.5f, 0.12f, 0.14f, 0.3f, 0.32f },
        { "smd-resistor1", 0.48f, 0.5f, 0.12f, 0.14f, 0.3f, 0.32f },
        { "smd-cap0", 0.48f, 0.5f, 0.3f, 0.32f, 0.3f, 0.32f },
        { "solder-blob0", 0.2f, 0.4f, 0.1f, 0.15f, 0.2f, 0.4f },
        { "solder-blob1", 0.2f, 0.4f, 0.08f, 0.14f, 0.2f, 0.4f },
        { "via-pad0", 1.15f, 1.25f, 0.035f, 0.045f, 1.15f, 1.25f },
        { "dust-bunny0", 0.4f, 0.6f, 0.2f, 0.45f, 0.4f, 0.6f },
        { "dust-bunny1", 0.4f, 0.65f, 0.2f, 0.45f, 0.4f, 0.65f },
        { "zip-tie-tail0", 3f, 4.2f, 0.24f, 0.45f, 1.8f, 2.2f },
        { "thermal-paste-dot0", 0.5f, 1f, 0.12f, 0.25f, 0.5f, 1f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;

        Assert.True(size.X >= minX - Pad && size.X <= maxX + Pad, $"{name} is {size.X:0.000} m wide, not {minX} to {maxX}");
        Assert.True(size.Y >= minY - Pad && size.Y <= maxY + Pad, $"{name} is {size.Y:0.000} m high, not {minY} to {maxY}");
        Assert.True(size.Z >= minZ - Pad && size.Z <= maxZ + Pad, $"{name} is {size.Z:0.000} m deep, not {minZ} to {maxZ}");
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -0.15f, 0.02f);
        // Centred on its origin, the base's middle
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.3f, 0.3f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.3f, 0.3f);
    }

    [Theory]
    [InlineData("materials/computer/pc_epoxy_black.sdfmat")]
    [InlineData("materials/computer/pc_tin.sdfmat")]
    [InlineData("materials/computer/pc_solder.sdfmat")]
    [InlineData("materials/computer/pc_ceramic_tan.sdfmat")]
    [InlineData("materials/computer/pc_dust.sdfmat")]
    [InlineData("materials/computer/pc_zip_white.sdfmat")]
    [InlineData("materials/computer/pc_paste_grey.sdfmat")]
    [InlineData("materials/computer/pc_via_hole.sdfmat")]
    [InlineData("materials/ground/ground_pcb.sdfmat")]
    [InlineData("materials/ground/ground_trace.sdfmat")]
    [InlineData("materials/ground/ground_silk.sdfmat")]
    [InlineData("materials/ground/ground_pad.sdfmat")]
    public void Materials_Exist_AndParse(string path)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var file = Path.Combine(Path.GetDirectoryName(content)!, path);
        Assert.True(File.Exists(file), file);
        SDFPipeline.Materials.MaterialFile.Read(File.ReadAllText(file));
    }

    [Fact]
    public void GroundTrace_Glows_AndTheOtherGroundMaterialsDoNot()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        float Emission(string name) =>
            SDFPipeline.Materials.MaterialFile.Read(File.ReadAllText(Path.Combine(Path.GetDirectoryName(content)!, "materials", "ground", name + ".sdfmat"))).emission;
        Assert.Equal(1f, Emission("ground_trace"));
        Assert.Equal(0f, Emission("ground_pcb"));
        Assert.Equal(0f, Emission("ground_silk"));
        Assert.Equal(0f, Emission("ground_pad"));
    }
}
