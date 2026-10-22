using System.Globalization;
using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 29.5: the bathroom's small landmarks and base parts (content/models/bathroom/ bath-brush0, bath-scale0, floor-drain0, window-frosted0, soap-dish0, toothbrush-cup0,
/// toothbrush-red0, toothbrush-blue0) have the sizes 29's tables give and the colliders it asks for; the scale's skirt is a 26.6 degree slope all round, the soap dish's gates
/// are 4 m and 2.5 m wide in a 1.2 m rim, and only the two team toothbrushes carry a red or a blue.
/// </summary>
public class BathroomSmallLandmarksTests
{
    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string name) => Path.Combine(Content(), "models", "bathroom", name + ".sdf");

    private static Func<Vector3, float> Field(string name) => new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(name))).Distance;

    private static bool Inside(Func<Vector3, float> field, float x, float y, float z) => field(new Vector3(x, y, z)) <= 0.005f;

    /// <summary>The solid's extent measured on a grid inside a search box: the octree finder rounds out to whole cells, too coarse for these sizes.</summary>
    private static (Vector3 Min, Vector3 Max) Measure(Func<Vector3, float> field, Vector3 low, Vector3 high, float step)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var x = low.X; x <= high.X; x += step)
        for (var y = low.Y; y <= high.Y; y += step)
        for (var z = low.Z; z <= high.Z; z += step)
        {
            if (field(new Vector3(x, y, z)) > 0f) continue;
            min = Vector3.Min(min, new Vector3(x, y, z));
            max = Vector3.Max(max, new Vector3(x, y, z));
        }
        return (min, max);
    }

    /// <summary>The highest solid y on a vertical line, searched down from <paramref name="top"/> in steps of 5 mm (-1 when there is none).</summary>
    private static float TopAt(Func<Vector3, float> field, float x, float z, float top)
    {
        for (var y = top; y >= 0f; y -= 0.005f)
            if (field(new Vector3(x, y, z)) <= 0f) return y;
        return -1f;
    }

    /// <summary>Length of the open run along one axis through (x, y, z) when that point is open, in steps of 5 cm; the run is bounded by solid at both ends.</summary>
    private static float GapWidth(Func<Vector3, float> field, Vector3 from, Vector3 axis)
    {
        Assert.False(Inside(field, from.X, from.Y, from.Z), "the probe is not in the gap");
        var width = 0f;
        for (var sign = -1; sign <= 1; sign += 2)
        for (var d = 0.05f; d < 8f; d += 0.05f)
        {
            var p = from + axis * (sign * d);
            if (Inside(field, p.X, p.Y, p.Z)) { width += d - 0.05f; break; }
        }
        return width + 0.05f;
    }

    // Name, search box (half width x, lowest y, highest y, half depth z), grid step, then the allowed size in x, y and z
    public static IEnumerable<object[]> Sizes() => new List<object[]>
    {
        new object[] { "bath-brush0", 8f, -0.3f, 3.5f, 3.5f, 0.1f, 11.8f, 12.2f, 2.3f, 2.5f, 2.3f, 2.5f },
        new object[] { "bath-scale0", 7.5f, -0.2f, 1.2f, 7.5f, 0.1f, 12.4f, 12.8f, 0.75f, 0.9f, 12.4f, 12.8f },
        new object[] { "floor-drain0", 2.6f, -0.05f, 0.2f, 2.6f, 0.025f, 4.4f, 4.6f, 0.04f, 0.07f, 4.4f, 4.6f },
        new object[] { "soap-dish0", 16.5f, -0.3f, 2.5f, 12f, 0.1f, 29.8f, 30.3f, 1.1f, 1.3f, 20.8f, 21.3f },
        new object[] { "toothbrush-cup0", 1.6f, -0.2f, 4f, 1.6f, 0.03f, 2.2f, 2.4f, 3.25f, 3.45f, 2.2f, 2.4f },
        new object[] { "toothbrush-red0", 1f, -0.3f, 6.5f, 1f, 0.03f, 0.3f, 0.6f, 5.6f, 5.8f, 0.4f, 0.8f },
        new object[] { "toothbrush-blue0", 1f, -0.3f, 6.5f, 1f, 0.03f, 0.3f, 0.6f, 5.6f, 5.8f, 0.4f, 0.8f },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Model_HasTheTablesSize_AndStandsOnItsBase(string name, float halfWidth, float lowY, float highY, float halfDepth, float step,
        float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var (min, max) = Measure(Field(name), new Vector3(-halfWidth, lowY, -halfDepth), new Vector3(halfWidth, highY, halfDepth), step);
        var size = max - min;
        Assert.InRange(size.X, minX, maxX);
        Assert.InRange(size.Y, minY, maxY);
        Assert.InRange(size.Z, minZ, maxZ);
        Assert.InRange(min.Y, -0.1f, 0.1f);
        Assert.InRange((min.X + max.X) / 2, -0.4f, 0.4f);
        if (name != "toothbrush-red0" && name != "toothbrush-blue0") Assert.InRange((min.Z + max.Z) / 2, -0.4f, 0.4f);
    }

    [Theory]
    [InlineData("bath-brush0", "collider convex")]
    [InlineData("bath-scale0", "collider convex")]
    [InlineData("floor-drain0", "collider none")]
    [InlineData("window-frosted0", "collider none")]
    [InlineData("soap-dish0", "collider mesh")]
    [InlineData("toothbrush-cup0", "collider convex")]
    [InlineData("toothbrush-red0", "collider none")]
    [InlineData("toothbrush-blue0", "collider none")]
    public void Model_HasItsColliderLine(string name, string line)
    {
        Assert.Contains(File.ReadAllLines(ModelPath(name)), l => l.Trim() == line);
    }

    [Fact]
    public void BathBrush_HasAThinHandle_AndATallHeadAtThePlusXEnd()
    {
        var field = Field("bath-brush0");
        // Handle 0.9 m thick lying on the floor
        Assert.True(Inside(field, -3f, 0.85f, 0f));
        Assert.False(Inside(field, -3f, 0.95f, 0f));
        Assert.True(Inside(field, -3f, 0.2f, 0.3f));
        Assert.False(Inside(field, -3f, 0.45f, 0.55f));
        // The head: 3.6 long, 2.4 wide, bristles up to 2.4 m
        Assert.True(Inside(field, 4.2f, 2.3f, 0f));
        Assert.False(Inside(field, 4.2f, 2.5f, 0f));
        Assert.True(Inside(field, 5.8f, 1.5f, 1f));
        Assert.False(Inside(field, 6.2f, 1.5f, 0f));
        Assert.False(Inside(field, 4.2f, 1.5f, 1.3f));
        Assert.False(Inside(field, 2.1f, 1.5f, 0f), "the handle is low, the head's bristles start 2.4 m along");
    }

    [Fact]
    public void BathScale_SkirtIsA27DegreeSlopeAllRound_AndTheTopJoinsTheFloor()
    {
        var field = Field("bath-scale0");
        // Top at 0.75 (glass and trim), the body's edge at the floor 6.3 m out
        Assert.InRange(TopAt(field, 0f, -2f, 1f), 0.73f, 0.77f);
        Assert.True(Inside(field, 6.25f, 0.01f, 0f));
        Assert.False(Inside(field, 6.4f, 0.01f, 0f));
        foreach (var (sx, sz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        {
            var h1 = TopAt(field, sx * 5.2f + 0.0f, sz * 5.2f, 1f);
            var h2 = TopAt(field, sx * 6.0f, sz * 6.0f, 1f);
            Assert.True(h1 > 0f && h2 > 0f);
            var angle = MathF.Atan((h1 - h2) / 0.8f) * 180f / MathF.PI;
            Assert.InRange(angle, 25f, 28.5f);
            // The skirt is 1.5 m wide: the top's edge is 4.8 m out
            Assert.InRange(TopAt(field, sx * 4.7f, sz * 4.7f, 1f), 0.72f, 0.77f);
        }
    }

    [Fact]
    public void BathScale_ShowsA42_InTheDisplay()
    {
        var field = Field("bath-scale0");
        // The vertical bars of the 4 (upper left and upper right, lower right) and of the 2 (upper right, lower left) stand 0.05 m proud of the glass
        Assert.True(Inside(field, -0.95f, 0.78f, 2.7f), "4, upper left");
        Assert.True(Inside(field, -0.3f, 0.78f, 3.3f), "4, lower right");
        Assert.True(Inside(field, 0.94f, 0.78f, 2.7f), "2, upper right");
        Assert.True(Inside(field, 0.3f, 0.78f, 3.3f), "2, lower left");
        // No bar where the 4's lower left is open
        Assert.False(Inside(field, -0.95f, 0.78f, 3.3f));
    }

    [Fact]
    public void FloorDrain_IsFlush_WithAnOpenGrate()
    {
        var field = Field("floor-drain0");
        Assert.True(Inside(field, 0f, 0.03f, 0f), "the hub");
        Assert.True(Inside(field, 2.1f, 0.03f, 0f), "the rim");
        Assert.True(Inside(field, 1.5f, 0.03f, 0f), "a ring crossing a spoke");
        // Between two rings and two spokes the grate is open, showing the pipe below
        Assert.False(Inside(field, 1.8f * MathF.Cos(0.39f), 0.04f, 1.8f * MathF.Sin(0.39f)));
        Assert.False(Inside(field, 1.2f * MathF.Cos(0.39f), 0.04f, 1.2f * MathF.Sin(0.39f)));
    }

    [Fact]
    public void WindowFrosted_HasA24By20Pane_AFrame_AndASill_OnTheWallPlane()
    {
        var field = Field("window-frosted0");
        var (min, max) = Measure(field, new Vector3(-16f, 38f, -0.5f), new Vector3(16f, 66f, 5f), 0.2f);
        Assert.InRange(min.Z, -0.01f, 0.2f);
        Assert.InRange((min.X + max.X) / 2, -0.4f, 0.4f);
        Assert.InRange(max.Y - min.Y, 22.2f, 23.6f);
        Assert.InRange(max.X - min.X, 26f, 29.3f);
        // The pane (y 42 to 62, x -12 to 12) and the frame round it
        Assert.True(Inside(field, 6f, 52f, 0.3f));
        Assert.True(Inside(field, -11.8f, 42.2f, 0.3f));
        Assert.True(Inside(field, 11.8f, 61.8f, 0.3f));
        Assert.True(Inside(field, 12.6f, 52f, 0.6f), "frame, side");
        Assert.True(Inside(field, 0f, 62.6f, 0.6f), "frame, top");
        Assert.False(Inside(field, 6f, 52f, 1.0f), "nothing in front of the pane");
        Assert.False(Inside(field, 6f, 64f, 0.3f));
        // The sill under it, with its front edge 3 m from the wall
        Assert.True(Inside(field, 0f, 40.3f, 2.7f));
        Assert.False(Inside(field, 0f, 40.3f, 3.3f));
    }

    [Fact]
    public void SoapDish_HasA1_2MetreRim_WithTheThreeGatesOfTheTable()
    {
        var field = Field("soap-dish0");
        // Rim 1.2 m tall, 1.5 m thick, the floor inside open
        Assert.True(Inside(field, -8f, 1.15f, 9.75f));
        Assert.False(Inside(field, -8f, 1.25f, 9.75f));
        Assert.False(Inside(field, 0f, 0.5f, 0f), "no floor");
        Assert.False(Inside(field, -8f, 0.5f, 8.8f));
        Assert.False(Inside(field, -8f, 0.5f, 10.7f));
        Assert.True(Inside(field, 13.8f, 0.6f, -4f), "the short end is solid away from its gate");
        // The gates, measured along the rim at mid height: main 4 m at z +10, ends 2.5 m
        Assert.InRange(GapWidth(field, new Vector3(0f, 0.6f, 10.2f), Vector3.UnitX), 3.9f, 4.15f);
        Assert.InRange(GapWidth(field, new Vector3(14.2f, 0.6f, 0f), Vector3.UnitZ), 2.4f, 2.65f);
        Assert.InRange(GapWidth(field, new Vector3(-14.2f, 0.6f, 0f), Vector3.UnitZ), 2.4f, 2.65f);
        // The gates are open down to the floor and the rest of the long side is solid
        Assert.False(Inside(field, 0f, 0.05f, 10.2f));
        Assert.True(Inside(field, 2.5f, 0.6f, 10.2f));
        Assert.True(Inside(field, -2.5f, 0.6f, 10.2f));
        Assert.True(Inside(field, 0f, 0.6f, -10.2f), "the far long side has no gate");
    }

    [Fact]
    public void SoapDish_OutsideIsSteepOverFiftyFiveDegrees()
    {
        var field = Field("soap-dish0");
        // The outside face on the far long side (z = -10.5): where the solid ends at two heights
        float EdgeAt(float y)
        {
            for (var z = -11.5f; z < -8f; z += 0.005f)
                if (field(new Vector3(-8f, y, z)) <= 0f) return z;
            return float.NaN;
        }
        var low = EdgeAt(0.1f);
        var high = EdgeAt(0.6f);
        Assert.InRange(low, -10.6f, -10.4f);
        var angle = MathF.Atan2(0.5f, MathF.Abs(high - low)) * 180f / MathF.PI;
        Assert.True(angle > 55f, $"outside at {angle} degrees");
    }

    [Fact]
    public void SoapDish_HeaderSaysWhichAxisTheMainGateFaces()
    {
        var header = string.Join('\n', File.ReadAllLines(ModelPath("soap-dish0")).TakeWhile(l => l.StartsWith('#')));
        Assert.Contains("faces local +z", header);
        Assert.Contains("yaw 90", header);
        Assert.Contains("yaw -90", header);
    }

    [Fact]
    public void ToothbrushCup_IsAHollowTumbler_WithAThickFloor()
    {
        var field = Field("toothbrush-cup0");
        Assert.True(Inside(field, 0f, 0.2f, 0f), "the floor");
        Assert.False(Inside(field, 0f, 1.5f, 0f), "hollow");
        Assert.True(Inside(field, 1.0f, 3.2f, 0f), "the wall at the lip");
        Assert.False(Inside(field, 1.0f, 3.5f, 0f));
        // 1.9 m at the foot
        Assert.True(Inside(field, 0.9f, 0.1f, 0f));
        Assert.False(Inside(field, 1.0f, 0.1f, 0f));
    }

    [Theory]
    [InlineData("toothbrush-red0", "bath_toothbrush_red")]
    [InlineData("toothbrush-blue0", "bath_toothbrush_blue")]
    public void Toothbrush_IsFlat_5Point7Long_WithBristlesOnTheFrontOfTheHead(string name, string material)
    {
        var field = Field(name);
        Assert.Contains(File.ReadAllLines(ModelPath(name)), l => l.Contains("material=" + material));
        Assert.Contains(File.ReadAllLines(ModelPath(name)), l => l.Contains("material=bath_bristle_white"));
        Assert.True(Inside(field, 0f, 1f, 0f), "the handle");
        Assert.False(Inside(field, 0.5f, 1f, 0f), "the handle is 0.45 wide");
        Assert.False(Inside(field, 0f, 1f, 0.3f), "the handle is 0.25 thick");
        Assert.True(Inside(field, 0f, 5.1f, 0.3f), "bristles");
        Assert.False(Inside(field, 0f, 3.5f, 0.3f), "no bristles on the handle");
        Assert.False(Inside(field, 0f, 5.1f, -0.3f), "bristles on the front only");
        Assert.True(Inside(field, 0f, 5.65f, 0f));
        Assert.False(Inside(field, 0f, 5.8f, 0f));
    }

    private static float[] BaseColor(string material)
    {
        var root = Path.Combine(Path.GetDirectoryName(Content())!, "materials", "bathroom");
        var file = Path.Combine(root, material + ".sdfmat");
        Assert.True(File.Exists(file), $"{file} is missing");
        var line = File.ReadAllLines(file).First(l => l.StartsWith("baseColor"));
        return line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    }

    [Fact]
    public void OnlyTheTwoTeamToothbrushes_HaveARedOrBlueBase()
    {
        foreach (var name in new[] { "bath_wood", "bath_bristle", "bath_glass_top", "bath_display_dark", "bath_display_lit", "bath_drain_dark", "bath_window_glass",
                     "bath_window_frame", "bath_ceramic", "bath_glass_frosted", "bath_bristle_white" })
        {
            var v = BaseColor(name);
            Assert.False(v[0] > 0.6f && v[0] > v[1] + 0.35f && v[0] > v[2] + 0.35f, $"{name} is red");
            Assert.False(v[2] > v[0] + 0.25f && v[2] > v[1] + 0.1f, $"{name} is blue");
        }
        var red = BaseColor("bath_toothbrush_red");
        Assert.True(red[0] > 0.6f && red[0] > red[1] + 0.35f && red[0] > red[2] + 0.35f);
        var blue = BaseColor("bath_toothbrush_blue");
        Assert.True(blue[2] > blue[0] + 0.25f && blue[2] > blue[1] + 0.1f);
    }

    [Fact]
    public void NoOtherModelOfThe29_5Set_UsesTheTeamMaterials()
    {
        foreach (var name in new[] { "bath-brush0", "bath-scale0", "floor-drain0", "window-frosted0", "soap-dish0", "toothbrush-cup0" })
            Assert.DoesNotContain(File.ReadAllLines(ModelPath(name)), l => l.Contains("toothbrush_red") || l.Contains("toothbrush_blue"));
    }
}
