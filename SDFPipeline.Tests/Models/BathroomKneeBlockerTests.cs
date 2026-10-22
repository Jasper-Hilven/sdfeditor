using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 29.3: the bathroom's knee cover (soap bars, sponges, ducklings, bath bombs, a washcloth, bottle caps) and tall blockers (shampoo
/// bottles, a lotion pump, toilet rolls) in content/models/bathroom/ are 30 times life size, rest on y = 0, are the size the level's tables
/// give, all carry a convex collider, and their materials hold no red or blue.
/// </summary>
public class BathroomKneeBlockerTests
{
    private static string ContentFolder()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    /// <summary>
    /// The model's extent. The finder's box is too coarse to size a model by (it rounds out by whole octree cells), so inside that box the field
    /// is sampled on a 5 cm grid and the extent of the points inside the solid is taken.
    /// </summary>
    private static (Vector3 Min, Vector3 Max) Measure(string name, float extent)
    {
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(ContentFolder(), "models", "bathroom", name + ".sdf"))).Distance;
        var box = SdfBoundsFinder.Find(sdf, extent);
        Assert.NotNull(box);
        const float step = 0.05f;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var x = box.Min.X; x <= box.Max.X; x += step)
            for (var y = box.Min.Y; y <= box.Max.Y; y += step)
                for (var z = box.Min.Z; z <= box.Max.Z; z += step)
                {
                    var p = new Vector3(x, y, z);
                    if (sdf(p) > 0) continue;
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
        return (min, max);
    }

    // Name, then the allowed range of the width (x), height (y) and depth (z)
    public static TheoryData<string, float, float, float, float, float, float> Models() => new()
    {
        { "soap-bar0", 2.8f, 3.2f, 0.8f, 1f, 1.6f, 2f },
        { "soap-bar1", 2.8f, 3.2f, 0.8f, 1f, 1.6f, 2f },
        { "soap-bar2", 2.4f, 2.9f, 0.5f, 0.8f, 1.4f, 2.2f },
        { "sponge0", 3.4f, 3.8f, 1f, 1.2f, 2.2f, 2.6f },
        { "sponge1", 3.2f, 3.6f, 1f, 1.2f, 2.2f, 2.6f },
        { "duckling0", 0.8f, 1.2f, 0.95f, 1.15f, 1.2f, 1.5f },
        { "duckling1", 0.8f, 1.2f, 0.85f, 1.1f, 1.1f, 1.4f },
        { "duckling2", 0.8f, 1.3f, 0.65f, 0.85f, 1.1f, 1.5f },
        { "bath-bomb0", 1.1f, 1.3f, 1f, 1.15f, 1.1f, 1.3f },
        { "bath-bomb1", 1.1f, 1.3f, 1f, 1.15f, 1.1f, 1.3f },
        { "bath-bomb-half0", 0.6f, 0.8f, 0.5f, 0.7f, 0.6f, 0.8f },
        { "washcloth0", 4.3f, 4.6f, 0.8f, 1.05f, 4.3f, 4.6f },
        { "bottle-cap0", 1.1f, 1.3f, 0.7f, 0.8f, 1.1f, 1.4f },
        { "bottle-cap1", 0.8f, 1f, 0.55f, 0.7f, 0.8f, 1f },
        { "shampoo0", 3.4f, 4.2f, 6.5f, 6.7f, 2.2f, 2.8f },
        { "shampoo1", 3f, 3.6f, 5.3f, 5.6f, 2f, 2.6f },
        { "shampoo2", 3.4f, 4.2f, 2.2f, 2.7f, 6.5f, 6.7f },
        { "lotion-pump0", 2.6f, 3.1f, 5.8f, 6.1f, 2.6f, 3.1f },
        { "toilet-roll0", 3.5f, 3.7f, 2.9f, 3.1f, 3.5f, 3.7f },
        { "toilet-roll1", 3.5f, 3.9f, 5.9f, 6.1f, 3.5f, 3.9f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        // The tall bottles and the stacked rolls reach past 4 m, so they get the wider search box
        var extent = maxY > 5f || maxZ > 5f ? 8f : 4f;
        const float pad = 0.1f;
        var bounds = Measure(name, extent);
        var size = bounds.Max - bounds.Min;

        Assert.True(size.X >= minX - pad && size.X <= maxX + pad, $"{name} is {size.X:0.000} m wide, not {minX} to {maxX}");
        Assert.True(size.Y >= minY - pad && size.Y <= maxY + pad, $"{name} is {size.Y:0.000} m high, not {minY} to {maxY}");
        Assert.True(size.Z >= minZ - pad && size.Z <= maxZ + pad, $"{name} is {size.Z:0.000} m deep, not {minZ} to {maxZ}");
        // Resting on y = 0, the finder's cells aside
        Assert.InRange(bounds.Min.Y, -0.1f, 0.05f);
        // Centred on its origin, the base's middle (the lying bottle and the ducks are centred on their bodies' middle)
        Assert.InRange((bounds.Min.X + bounds.Max.X) / 2, -0.3f, 0.3f);
        Assert.InRange((bounds.Min.Z + bounds.Max.Z) / 2, -0.3f, 0.3f);
    }

    public static TheoryData<string> Names() => new(Models().Select(m => (string)m[0]));

    /// <summary>Every solid model has a collider line (the toy box once lost all its colliders), and it is a convex one.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Model_HasAConvexColliderLine(string name)
    {
        var text = File.ReadAllLines(Path.Combine(ContentFolder(), "models", "bathroom", name + ".sdf"));
        Assert.Contains(text, l => l.Trim() == "collider convex");
    }

    /// <summary>
    /// Every bathroom model that is not floor clutter (no collider none, no ground-clutter note) has a collider line: nothing solid ships without one.
    /// </summary>
    [Fact]
    public void EverySolidBathroomModel_HasAColliderLine()
    {
        var folder = Path.Combine(ContentFolder(), "models", "bathroom");
        var solid = Directory.GetFiles(folder, "*.sdf").Where(f => !File.ReadAllText(f).Contains("Ground clutter: no collider.")).ToList();
        Assert.True(solid.Count >= 20, "the knee cover and tall blockers are expected to be found");
        foreach (var file in solid)
            Assert.True(File.ReadAllLines(file).Any(l => l.StartsWith("collider ")), $"{Path.GetFileName(file)} has no collider line");
    }

    /// <summary>The ducks use the family's yellow and an orange beak: orange, not red.</summary>
    [Fact]
    public void DuckMaterials_AreTheDuckFamilyColours()
    {
        var folder = Path.Combine(Path.GetDirectoryName(ContentFolder())!, "materials", "bathroom");
        Assert.Contains("baseColor = (0.969, 0.824, 0.29)", File.ReadAllText(Path.Combine(folder, "bath_rubber_yellow.sdfmat")));
        Assert.Contains("baseColor = (0.949, 0.604, 0.227)", File.ReadAllText(Path.Combine(folder, "bath_beak_orange.sdfmat")));
    }

    /// <summary>No red or blue props: the bathroom's palette keeps those for the teams (team colours only on scouts, flags, machines).</summary>
    [Fact]
    public void KneeBlockerMaterials_HaveNoRedOrBlueBase()
    {
        var folder = Path.Combine(Path.GetDirectoryName(ContentFolder())!, "materials", "bathroom");
        var names = new[]
        {
            "soap_cream", "soap_lilac", "soap_mint", "sponge_yellow", "scrub_mint", "rubber_yellow", "beak_orange", "eye_dark", "bomb_lilac", "bomb_yellow", "bomb_cut",
            "terry_peach", "terry_lilac", "plastic_white", "plastic_mint", "plastic_lilac", "plastic_yellow", "plastic_cream", "plastic_peach", "paper", "paper_core",
        };
        foreach (var name in names)
        {
            var file = Path.Combine(folder, $"bath_{name}.sdfmat");
            Assert.True(File.Exists(file), $"{file} is missing");
            var line = File.ReadAllLines(file).First(l => l.StartsWith("baseColor"));
            var values = line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float r = values[0], g = values[1], b = values[2];
            Assert.False(r > 0.6f && r > g + 0.35f && r > b + 0.35f, $"{Path.GetFileName(file)} is red");
            Assert.False(b > r + 0.25f && b > g + 0.1f, $"{Path.GetFileName(file)} is blue");
            // Pink: red and blue both clearly over green
            Assert.False(r > g + 0.2f && b > g + 0.1f && r > 0.85f, $"{Path.GetFileName(file)} is pink");
        }
    }
}
