using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 29.2: the bathroom's floor clutter (content/models/bathroom/ suds, droplets, a bubble, hairpins, cotton swabs, bath beads) is
/// 30 times life size, rests on y = 0 and is the size the level's tables give; its materials hold no red or blue.
/// </summary>
public class BathroomFloorClutterTests
{
    // The finder rounds out to its octree cells, a few centimetres on each axis at these sizes
    private const float Pad = 0.06f;

    private static SdfBounds Bounds(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "bathroom", name + ".sdf"))).Distance;
        var bounds = SdfBoundsFinder.Find(sdf, 2f);
        Assert.NotNull(bounds);
        return bounds;
    }

    // Name, then the allowed range of the width (x), height (y) and depth (z)
    public static TheoryData<string, float, float, float, float, float, float> Models() => new()
    {
        { "suds0", 1.1f, 1.5f, 0.35f, 0.45f, 1.1f, 1.5f },
        { "suds1", 1f, 1.5f, 0.2f, 0.4f, 0.9f, 1.4f },
        { "suds2", 0.55f, 0.8f, 0.2f, 0.3f, 0.5f, 0.75f },
        { "droplet0", 0.25f, 0.3f, 0.1f, 0.12f, 0.25f, 0.3f },
        { "droplet1", 0.18f, 0.26f, 0.07f, 0.1f, 0.15f, 0.22f },
        { "droplet2", 0.1f, 0.14f, 0.05f, 0.07f, 0.1f, 0.14f },
        { "bubble0", 0.38f, 0.42f, 0.38f, 0.42f, 0.38f, 0.42f },
        { "hairpin0", 0.2f, 0.35f, 0.08f, 0.14f, 1.45f, 1.55f },
        { "hairpin1", 0.4f, 0.75f, 0.08f, 0.14f, 1.45f, 1.55f },
        { "swab0", 0.25f, 0.5f, 0.25f, 0.5f, 2.2f, 2.35f },
        { "swab-half0", 0.25f, 0.35f, 0.25f, 0.35f, 1.05f, 1.2f },
        { "bath-bead0", 0.28f, 0.34f, 0.28f, 0.34f, 0.28f, 0.34f },
        { "bath-bead1", 0.28f, 0.34f, 0.26f, 0.32f, 0.28f, 0.34f },
        { "bath-bead2", 0.28f, 0.34f, 0.28f, 0.34f, 0.28f, 0.34f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var bounds = Bounds(name);
        var size = bounds.Max - bounds.Min;

        Assert.True(size.X >= minX && size.X <= maxX + Pad, $"{name} is {size.X:0.000} m wide, not {minX} to {maxX}");
        Assert.True(size.Y >= minY && size.Y <= maxY + Pad, $"{name} is {size.Y:0.000} m high, not {minY} to {maxY}");
        Assert.True(size.Z >= minZ && size.Z <= maxZ + Pad, $"{name} is {size.Z:0.000} m deep, not {minZ} to {maxZ}");
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -0.1f, 0.02f);
        // Centred on its origin, the base's middle (the finder's cells round the long swabs out by up to a few tens of centimetres)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.15f, 0.15f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.4f, 0.4f);
    }

    /// <summary>Ground clutter has no collider: the files must not ask for one.</summary>
    public static TheoryData<string> Names() => new(Models().Select(m => (string)m[0]));

    [Theory]
    [MemberData(nameof(Names))]
    public void Clutter_HasNoCollider(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var text = File.ReadAllText(Path.Combine(content, "models", "bathroom", name + ".sdf"));
        Assert.DoesNotContain("collider", text.Replace("Ground clutter: no collider.", ""));
    }

    /// <summary>No red or blue props: the bathroom's palette keeps those for the teams (team colours only on scouts, flags, machines).</summary>
    [Fact]
    public void FloorClutterMaterials_HaveNoRedOrBlueBase()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var root = Path.Combine(Path.GetDirectoryName(content)!, "materials");
        var files = new[] { "bath_foam", "bath_water", "bath_bubble", "bath_bronze", "bath_swab_stick", "bath_cotton", "bath_pearl_yellow", "bath_pearl_lilac", "bath_pearl_mint" }
            .Select(n => Path.Combine(root, "bathroom", n + ".sdfmat"))
            .Concat(new[] { "bathroom-tile", "grout", "wet", "puddle", "suds" }.Select(n => Path.Combine(root, "ground", $"ground_{n}.sdfmat")));
        foreach (var file in files)
        {
            Assert.True(File.Exists(file), $"{file} is missing");
            var line = File.ReadAllLines(file).First(l => l.StartsWith("baseColor"));
            var values = line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float r = values[0], g = values[1], b = values[2];
            // Red: red far above green and blue. Blue: blue the highest channel, over green by a clear margin
            Assert.False(r > 0.6f && r > g + 0.35f && r > b + 0.35f, $"{Path.GetFileName(file)} is red");
            Assert.False(b > r + 0.25f && b > g + 0.1f, $"{Path.GetFileName(file)} is blue");
        }
    }

    /// <summary>The level's regions name these ground materials; each must load as a ground material.</summary>
    [Fact]
    public void GroundMaterials_NameTheBathroomRegions()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var folder = Path.Combine(Path.GetDirectoryName(content)!, "materials", "ground");
        foreach (var name in new[] { "bathroom-tile", "grout", "wet", "puddle", "suds" })
            Assert.True(File.Exists(Path.Combine(folder, $"ground_{name}.sdfmat")), $"ground_{name}.sdfmat is missing");
    }
}
