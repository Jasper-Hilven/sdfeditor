using System.Numerics;
using System.Text.RegularExpressions;
using SdfBattle;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 30.5: the computer level's landmark models (cooler, GPU, battery, VRM and chipset heatsinks, PCIe x16 slot, ribbon bridge, power connector,
/// case fan) in content/models/computer/ are 250 times life size, rest on y = 0, are the size the level's tables give and name only materials that
/// exist; the bridge's ramps are 38 degrees with no gap under them; the parts that spin (Blades, FanWest, FanEast, Fan0 to Fan2) are named as the
/// spinning rule knows them, and the rule turns them steadily.
/// </summary>
public class ComputerLandmarksTests
{
    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static Func<Vector3, float> Sdf(string name) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(Content(), "models", "computer", name + ".sdf"))).Distance;

    /// <summary>
    /// The extent of the shape, found by sphere tracing a grid of rays in from each side: more exact than the octree finder, which rounds a size
    /// out by a cell of its search box. <paramref name="lo"/> and <paramref name="hi"/> hold the whole shape.
    /// </summary>
    private static (Vector3 Min, Vector3 Max) Measure(Func<Vector3, float> sdf, Vector3 lo, Vector3 hi, float step)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var axis = 0; axis < 3; axis++)
        {
            int b = (axis + 1) % 3, c = (axis + 2) % 3;
            for (var u = lo[b]; u <= hi[b]; u += step)
            for (var v = lo[c]; v <= hi[c]; v += step)
            foreach (var side in new[] { -1, 1 })
            {
                var p = new Vector3();
                p[b] = u;
                p[c] = v;
                var from = side < 0 ? lo[axis] - 1 : hi[axis] + 1;
                var length = hi[axis] - lo[axis] + 2;
                for (var t = 0f; t < length;)
                {
                    p[axis] = from - side * t;
                    var d = sdf(p);
                    if (d < 0.01f)
                    {
                        if (side < 0) min[axis] = Math.Min(min[axis], p[axis]);
                        else max[axis] = Math.Max(max[axis], p[axis]);
                        break;
                    }
                    t += d;
                }
            }
        }
        return (min, max);
    }

    // Name, then the width (x), height (y) and depth (z) of the table, how much a ring or stripe may add to it, and the ray grid's step
    public static TheoryData<string, float, float, float, float, float> Models() => new()
    {
        { "case-fan0", 30f, 30f, 6.25f, 0.2f, 0.4f },
        { "cpu-cooler0", 25f, 40f, 31f, 0.2f, 0.4f },
        { "gpu0", 67f, 28f, 10f, 0.2f, 0.5f },
        { "coin-battery0", 6f, 5.5f, 2.5f, 0.2f, 0.1f },
        { "vrm-heatsink0", 15f, 7.5f, 5f, 0.2f, 0.2f },
        { "chipset-heatsink0", 10f, 6.25f, 10f, 0.2f, 0.2f },
        { "pcie-x16-slot0", 22.25f, 2.75f, 1.9f, 0.2f, 0.1f },
        { "ribbon-bridge0", 5f, 4f, 16f, 0.2f, 0.2f },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Size_IsTheTablesSize_AndTheModelStandsOnTheGround(string name, float width, float height, float depth, float extra, float step)
    {
        var (min, max) = Measure(Sdf(name), new Vector3(-width / 2 - 1, -0.5f, -depth / 2 - 1), new Vector3(width / 2 + 1, height + 1, depth / 2 + 1), step);
        var size = max - min;

        Assert.InRange(size.X, width - 0.05f, width + extra);
        Assert.InRange(size.Y, height - 0.05f, height + extra);
        Assert.InRange(size.Z, depth - 0.05f, depth + extra);
        Assert.InRange(min.Y, -0.02f, 0.02f);
        Assert.InRange((min.X + max.X) / 2, -0.2f, 0.2f);
        Assert.InRange((min.Z + max.Z) / 2, -0.2f, 0.2f);
    }

    [Fact]
    public void PowerConnector_FootIs5Point3By2Point3_AndTheBundleRisesTo64Metres()
    {
        var sdf = Sdf("power-connector0");
        var (min, max) = Measure(sdf, new Vector3(-4, -0.5f, -9), new Vector3(4, 65, 3), 0.25f);
        Assert.InRange(max.Y - min.Y, 63.95f, 64.4f);
        Assert.InRange(min.Y, -0.02f, 0.02f);
        // The plug and header are 5.3 x 2.3 m; the bundle (4 x 2 m) leans back (-z) as it rises, so the depth is the lean's
        Assert.InRange(max.X - min.X, 5.25f, 5.5f);
        Assert.True(sdf(new Vector3(2.5f, 0.6f, 1.0f)) < 0, "the header's corner is solid");
        Assert.True(sdf(new Vector3(0, 0.6f, 1.4f)) > 0, "nothing outside the 2.3 m foot");
        Assert.True(sdf(new Vector3(0, 63.5f, -6.4f)) < 0.6f, "the bundle's top is still a bundle of wires at the ceiling");
        // Eight wires 1 m thick side by side: a closed bundle below 7.5 m, no gap a scout could slip between
        for (var y = 4f; y <= 7.5f; y += 1.5f)
            for (var x = -1.5f; x <= 1.5f; x += 1f)
                Assert.True(sdf(new Vector3(x, y, 0)) < 0.01f, $"a wire at x {x}, y {y}");
    }

    [Fact]
    public void RibbonBridge_RampsAre38Degrees_WithAFlatTopAndNoGapUnderThem()
    {
        var sdf = Sdf("ribbon-bridge0");
        // The top surface above (x, z): the highest y that is inside
        float Top(float z)
        {
            for (var y = 6f; y > 0; y -= 0.005f)
                if (sdf(new Vector3(0.5f, y, z)) < 0)
                    return y;
            return 0;
        }

        foreach (var side in new[] { -1f, 1f })
        {
            var slope = (Top(side * 3.2f) - Top(side * 5.2f)) / 2f;
            var degrees = MathF.Atan(slope) * 180 / MathF.PI;
            Assert.InRange(degrees, 35f, 41f);
        }
        Assert.InRange(Top(0), 3.95f, 4.05f);
        Assert.InRange(Top(1.9f), 3.95f, 4.05f);
        Assert.InRange(Top(-1.9f), 3.95f, 4.05f);

        // Solid from the board up to the surface on the ramps and the top, over the cable's whole width: nothing to crawl under
        for (var z = -7f; z <= 7f; z += 0.25f)
        {
            var top = Top(z);
            for (var x = -2.4f; x <= 2.4f; x += 0.4f)
                for (var y = 0.05f; y < top - 0.1f; y += 0.25f)
                    Assert.True(sdf(new Vector3(x, y, z)) < 0, $"a gap under the bridge at ({x}, {y}, {z})");
        }
    }

    // Model, then the parts it spins
    public static TheoryData<string, string> Spinning() => new()
    {
        { "case-fan0", "Blades" },
        { "cpu-cooler0", "FanWest" },
        { "cpu-cooler0", "FanEast" },
        { "gpu0", "Fan0" },
        { "gpu0", "Fan1" },
        { "gpu0", "Fan2" },
    };

    [Theory]
    [MemberData(nameof(Spinning))]
    public void SpinningParts_AreUsedByTheirModel_HaveAFile_AndAreKnownToTheSpinningRule(string model, string part)
    {
        var text = File.ReadAllText(Path.Combine(Content(), "models", "computer", model + ".sdf"));
        Assert.Contains($"Part part={part}", text);
        Assert.True(File.Exists(Path.Combine(Content(), "parts", part + ".sdf")), $"{part} has no part file");
        Assert.True(SpinRules.TryFind(part, out _));
        // The part is white, so the blades read against the frame
        Assert.Contains("material=pc_fan_white", File.ReadAllText(Path.Combine(Content(), "parts", part + ".sdf")));
    }

    // Model, then how many different materials it and its parts use
    public static TheoryData<string, int> MaterialCounts() => new()
    {
        { "case-fan0", 3 }, { "cpu-cooler0", 4 }, { "gpu0", 4 }, { "coin-battery0", 2 }, { "vrm-heatsink0", 2 },
        { "chipset-heatsink0", 3 }, { "pcie-x16-slot0", 2 }, { "ribbon-bridge0", 2 }, { "power-connector0", 3 },
    };

    [Theory]
    [MemberData(nameof(MaterialCounts))]
    public void EveryMaterialTheModelNames_Exists_AndTheCountIsTheTables(string name, int count)
    {
        var content = Content();
        var root = Path.GetDirectoryName(content)!;
        var texts = new List<string> { File.ReadAllText(Path.Combine(content, "models", "computer", name + ".sdf")) };
        foreach (Match part in Regex.Matches(texts[0], @"Part part=(\w+)"))
            texts.Add(File.ReadAllText(Path.Combine(content, "parts", part.Groups[1].Value + ".sdf")));
        var materials = texts.SelectMany(t => Regex.Matches(t, @"material=(\w+)").Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.Equal(count, materials.Count);
        foreach (var material in materials)
        {
            var file = Path.Combine(root, "materials", "computer", material + ".sdfmat");
            Assert.True(File.Exists(file), $"{name} names {material}, which is missing");
            SDFPipeline.Materials.MaterialFile.Read(File.ReadAllText(file));
        }
    }

    [Fact]
    public void CaseFansRing_Glows_AndNothingElseDoes()
    {
        var root = Path.GetDirectoryName(Content())!;
        var glowing = Directory.GetFiles(Path.Combine(root, "materials", "computer"), "pc_*.sdfmat")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^emission = [^0]", RegexOptions.Multiline))
            .Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
        Assert.Equal(new[] { "pc_led_ring" }, glowing);
    }

    [Theory]
    [InlineData("Blades", "Blades")]
    [InlineData("Blades_LOD1", "Blades")]
    [InlineData("FanWest_LOD12", "FanWest")]
    [InlineData("body_LOD1", "body")]
    [InlineData("Fan0_LODx", "Fan0_LODx")]
    [InlineData("Fan0_LOD", "Fan0_LOD")]
    public void PartName_StripsTheLevelOfDetailSuffix(string objectName, string part) => Assert.Equal(part, SpinRules.PartName(objectName));

    [Theory]
    [InlineData("Blades")]
    [InlineData("FanWest")]
    [InlineData("FanEast")]
    [InlineData("Fan0")]
    [InlineData("Fan1")]
    [InlineData("Fan2")]
    public void TheSpinningRule_KnowsTheNamedParts_AndTheirCoarserLevels(string name)
    {
        Assert.True(SpinRules.TryFind(name, out var rule));
        Assert.True(SpinRules.TryFind(name + "_LOD2", out var coarse));
        Assert.Equal(rule, coarse);
        // Slow turns: a revolution takes between 4 and 15 seconds
        Assert.InRange(MathF.Abs(rule.degreesPerSecond), 24f, 90f);
        // A unit axis
        Assert.Equal(1f, rule.axisX * rule.axisX + rule.axisY * rule.axisY + rule.axisZ * rule.axisZ, 4);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("fan")]
    [InlineData("Fan3")]
    [InlineData("Blades2")]
    public void TheSpinningRule_LeavesOtherPartsStill(string name) => Assert.False(SpinRules.TryFind(name, out _));

    [Fact]
    public void Angle_IsASteadyTurnFromThePhase_ThatWrapsAndAgreesAtOneClock()
    {
        SpinRules.TryFind("Fan2", out var rule);
        Assert.Equal(20f, SpinRules.Angle(rule, 0), 3);
        Assert.Equal(20f + 60f * 1.5f, SpinRules.Angle(rule, 1.5f), 3);
        // A revolution (6 s at 60 degrees a second) later it is where it was
        Assert.Equal(SpinRules.Angle(rule, 2f), SpinRules.Angle(rule, 8f), 2);
        Assert.InRange(SpinRules.Angle(rule, 1234.5f), 0f, 360f);
        // Backwards turns stay in range too
        var back = new SpinRule(0, 0, 1, -30);
        Assert.InRange(SpinRules.Angle(back, 7f), 0f, 360f);
        Assert.Equal(150f, SpinRules.Angle(back, 7f), 3);
        // The same clock gives the part and its coarser levels the same angle
        SpinRules.TryFind("Fan2_LOD1", out var coarse);
        Assert.Equal(SpinRules.Angle(rule, 3.3f), SpinRules.Angle(coarse, 3.3f));
    }
}
