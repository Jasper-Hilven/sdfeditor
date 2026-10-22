using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 30.6: the computer level's bases, ram-bank-red and ram-bank-blue in content/models/computer/: 36 m long (z), 4.6 m wide (x) and 11 m tall,
/// centred on x = 0 and z = 0 and standing on y = 0, both ends flat and flush; two DIMMs 0.5 m apart; four materials, the light bar the only
/// glowing one and the side's colour; the blue file is the red file with the colour changed.
/// </summary>
public class ComputerRamBankTests
{
    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string team) => Path.Combine(Content(), "models", "computer", $"ram-bank-{team}.sdf");

    private static Func<Vector3, float> Sdf(string team) => new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(team))).Distance;

    /// <summary>The extent along one axis: the first and last hit of sphere-traced rays over the grid of the other two axes</summary>
    private static (float Min, float Max) Extent(Func<Vector3, float> sdf, int axis)
    {
        var lo = new Vector3(-4, -0.5f, -20);
        var hi = new Vector3(4, 12, 20);
        int b = (axis + 1) % 3, c = (axis + 2) % 3;
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var u = lo[b]; u <= hi[b]; u += 0.25f)
        for (var v = lo[c]; v <= hi[c]; v += 0.25f)
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
                    if (side < 0) min = Math.Min(min, p[axis]);
                    else max = Math.Max(max, p[axis]);
                    break;
                }
                t += d;
            }
        }
        return (min, max);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    public void Size_Is36By4Point6By11_StandsOnTheGround_AndBothEndsAreFlushAndFlat(string team)
    {
        var sdf = Sdf(team);
        var x = Extent(sdf, 0);
        var y = Extent(sdf, 1);
        var z = Extent(sdf, 2);
        Assert.InRange(x.Max - x.Min, 4.55f, 4.8f);
        Assert.InRange(y.Max - y.Min, 10.95f, 11.2f);
        Assert.InRange(z.Max - z.Min, 35.95f, 36.2f);
        Assert.InRange(y.Min, -0.02f, 0.02f);
        Assert.InRange((x.Min + x.Max) / 2, -0.1f, 0.1f);
        Assert.InRange((z.Min + z.Max) / 2, -0.1f, 0.1f);

        // Flush ends: the full width of each DIMM's slot is solid right up to z = +-17.9 (the same at both ends), nothing sticks out beyond
        foreach (var end in new[] { -1f, 1f })
        {
            foreach (var x0 in new[] { -1.275f, 1.275f })
            {
                Assert.True(sdf(new Vector3(x0, 1f, end * 17.9f)) < 0, $"the slot reaches the end at x {x0}, z {end * 17.9f}");
                Assert.True(sdf(new Vector3(x0, 1f, end * 18.1f)) > 0, "nothing past the end");
            }
        }
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    public void TheTwoDimms_AreHalfAMetreApart_WithTheirHeatspreadersAndLightBars(string team)
    {
        var sdf = Sdf(team);
        foreach (var y in new[] { 0.5f, 1f, 6f, 10.1f })
            Assert.True(sdf(new Vector3(0, y, 0)) > 0.2f, $"the gap between the DIMMs is open at y {y}");
        foreach (var x in new[] { -1.275f, 1.275f })
        {
            Assert.True(sdf(new Vector3(x, 6, 0)) < 0, "a heatspreader");
            Assert.True(sdf(new Vector3(x, 10.1f, 0)) < 0, "a light bar");
            Assert.True(sdf(new Vector3(x, 1f, 0)) < 0, "a slot");
        }
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    public void FourMaterials_AllExist_AndOnlyTheLightBarGlows_InTheSidesColour(string team)
    {
        var root = Path.GetDirectoryName(Content())!;
        var text = File.ReadAllText(ModelPath(team));
        var materials = Regex.Matches(text, @"material=(\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(4, materials.Count);
        Assert.Contains($"pc_ram_light_{team}", materials);
        foreach (var material in materials)
        {
            var file = Path.Combine(root, "materials", "computer", material + ".sdfmat");
            Assert.True(File.Exists(file), $"{team} bank names {material}, which is missing");
            var glows = Regex.IsMatch(File.ReadAllText(file), @"^emission = [^0]", RegexOptions.Multiline);
            Assert.Equal(material.StartsWith("pc_ram_light_"), glows);
        }

        var line = File.ReadAllLines(Path.Combine(root, "materials", "computer", $"pc_ram_light_{team}.sdfmat")).First(l => l.StartsWith("baseColor"));
        var c = line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',')
            .Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (team == "red") Assert.True(c[0] > 0.8f && c[2] < 0.4f, "red light");
        else Assert.True(c[2] > 0.8f && c[0] < 0.4f, "blue light");
    }

    [Fact]
    public void TheBlueBank_IsTheRedBankWithBlueLight()
    {
        var red = File.ReadAllText(ModelPath("red"));
        var blue = File.ReadAllText(ModelPath("blue"));
        var asBlue = Regex.Replace(Regex.Replace(red.Replace("pc_ram_light_red", "pc_ram_light_blue"), @"\bred\b", "blue"), @"\bRed\b", "Blue");
        Assert.Equal(asBlue, blue);
    }
}
