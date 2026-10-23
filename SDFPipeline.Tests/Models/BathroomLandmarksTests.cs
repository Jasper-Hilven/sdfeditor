using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 29.4: the bathroom's five big landmarks (content/models/bathroom/ bathtub0, shower-riser0, towel-drape0, rubber-duck0, toilet-roll-pyramid0) have the sizes
/// 29's Landmarks table gives and the colliders it asks for; the towel hangs steeper than 60 degrees and its pooled roll's sides over 50; the pyramid's roll gaps are shut.
/// </summary>
public class BathroomLandmarksTests
{
    private static string ModelPath(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return Path.Combine(content, "models", "bathroom", name + ".sdf");
    }

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

    // Name, then the search box (half width, lowest y, highest y, half depth), the grid step and the allowed size in x, y and z (a grid step of rounding)
    public static IEnumerable<object[]> Landmarks() => new List<object[]>
    {
        new object[] { "bathtub0", 27f, -1f, 18f, 13f, 0.25f, 50.7f, 51.3f, 16.2f, 16.9f, 22.2f, 22.9f },
        new object[] { "rubber-duck0", 5f, -0.5f, 10f, 7f, 0.1f, 7.3f, 7.8f, 8.8f, 9.1f, 10.2f, 11.2f },
        new object[] { "towel-drape0", 12f, -0.5f, 18f, 4f, 0.1f, 20.8f, 21.2f, 16.2f, 16.7f, 4.8f, 5.2f },
        new object[] { "toilet-roll-pyramid0", 7f, -0.5f, 10f, 3f, 0.1f, 10.6f, 11f, 8.8f, 9.2f, 3.5f, 3.9f },
        // A wall fitting: its lowest point is the rail's foot, 18.5 m up
        new object[] { "shower-riser0", 6f, 17f, 62f, 9f, 0.1f, 6f, 12f, 40f, 44.5f, 8f, 11.5f },
    };

    [Theory]
    [MemberData(nameof(Landmarks))]
    public void Landmark_HasTheTablesSize_AndStandsOnItsBase(string name, float halfWidth, float lowY, float highY, float halfDepth, float step,
        float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var (min, max) = Measure(Field(name), new Vector3(-halfWidth, lowY, -halfDepth), new Vector3(halfWidth, highY, halfDepth), step);
        var size = max - min;
        Assert.InRange(size.X, minX, maxX);
        Assert.InRange(size.Y, minY, maxY);
        Assert.InRange(size.Z, minZ, maxZ);
        // Standing on y = 0 (the riser's lowest point is the rail's foot at 18.5 m); centred on x
        if (name == "shower-riser0") Assert.InRange(min.Y, 18f, 19.5f);
        else Assert.InRange(min.Y, -0.5f, 0.2f);
        Assert.InRange((min.X + max.X) / 2, -0.4f, 0.4f);
    }


    [Theory]
    [InlineData("bathtub0", "collider mesh lod=1")]
    [InlineData("shower-riser0", "collider none")]
    [InlineData("towel-drape0", "collider mesh")]
    [InlineData("rubber-duck0", "collider convex")]
    [InlineData("toilet-roll-pyramid0", "collider mesh")]
    public void Landmark_HasItsColliderLine(string name, string line)
    {
        Assert.Contains(File.ReadAllLines(ModelPath(name)), l => l.Trim() == line);
    }

    [Fact]
    public void Bathtub_IsHalfFull_WithTheFrontFaceAtPlusZ_AndAnOpenBasin()
    {
        var field = Field("bathtub0");
        // The basin is empty above the water line (y = 8) and holds water below it
        Assert.False(Inside(field, 22f, 12f, 0f));
        Assert.True(Inside(field, 22f, 6f, 0f));
        Assert.False(Inside(field, 22f, 9f, 8f));
        // The front panel (z 9.25 to 11.25) and rim stand; the floor of the basin is 3 m up
        Assert.True(Inside(field, -15f, 8f, 10.2f));
        Assert.True(Inside(field, -15f, 16f, 10.2f));
        Assert.True(Inside(field, 0f, 1.5f, 0f));
        Assert.False(Inside(field, -15f, 8f, 11.6f));
        // The sheet of water creeps over the front rim at the middle only
        Assert.True(Inside(field, 0f, 8f, 11.35f));
        Assert.False(Inside(field, 8f, 8f, 11.35f));
    }

    /// <summary>The first z (scanning from the front) at which the field is solid, at height y.</summary>
    private static float FrontZ(Func<Vector3, float> field, float y)
    {
        for (var z = 4f; z > -3f; z -= 0.01f)
            if (Inside(field, 0f, y, z)) return z;
        return float.NaN;
    }

    [Fact]
    public void Towel_HangsSteeperThan60Degrees_AndItsRollsSidesAreOver50()
    {
        var field = Field("towel-drape0");
        // The hanging part, from the top of the pooled roll to just under the rim
        for (var y = 3.5f; y < 15.5f; y += 0.5f)
        {
            var a = FrontZ(field, y);
            var b = FrontZ(field, y + 0.5f);
            Assert.False(float.IsNaN(a) || float.IsNaN(b), $"no towel at y = {y}");
            Assert.True(MathF.Atan2(0.5f, MathF.Abs(a - b)) > MathF.PI / 3, $"the towel is shallower than 60 degrees at y = {y}");
        }
        // The pooled roll: 2 to 3 m high, reaching about 4.5 m out from the tub's face (z -2.5 to 2.5)
        Assert.InRange(FrontZ(field, 1.3f), 2.3f, 2.6f);
        var top = 0f;
        for (var y = 1f; y < 6f; y += 0.01f)
            if (Inside(field, 0f, y, 2.0f)) top = y;
        Assert.InRange(top, 1.5f, 3f);
        // The upper flank of the roll, short of its crown, is steep
        for (var y = 1.3f; y < 2.1f; y += 0.2f)
        {
            var a = FrontZ(field, y);
            var b = FrontZ(field, y + 0.2f);
            Assert.True(MathF.Atan2(0.2f, MathF.Abs(a - b)) > 50f * MathF.PI / 180f, $"the roll's flank is shallower than 50 degrees at y = {y}");
        }
        // The towel's back is solid up to the tub's face: no cavity behind it; its width is 21 m
        Assert.True(Inside(field, 0f, 2f, -2.3f));
        Assert.True(Inside(field, 0f, 10f, -2.3f));
        Assert.True(Inside(field, 10.3f, 8f, -2f));
        Assert.False(Inside(field, 10.8f, 8f, -2f));
    }

    [Fact]
    public void Pyramid_HasNoGapOf60CentimetresOrMoreBetweenItsRolls()
    {
        var field = Field("toilet-roll-pyramid0");
        // Along x at the middle of the depth, from the floor up: free space between two solid stretches is a gap
        for (var y = 0.15f; y < 8.5f; y += 0.05f)
        {
            var gap = 0f;
            var seenSolid = false;
            for (var x = -5.6f; x <= 5.6f; x += 0.02f)
            {
                if (Inside(field, x, y, 0f))
                {
                    if (seenSolid) Assert.True(gap < 0.6f, $"a {gap:0.00} m gap at y = {y:0.00}, x = {x:0.00}");
                    seenSolid = true;
                    gap = 0f;
                }
                else gap += 0.02f;
            }
        }
    }

    [Fact]
    public void RubberDuck_FacesPlusZ_WithItsBeakInFrontAndItsHeadOnTop()
    {
        var field = Field("rubber-duck0");
        Assert.True(Inside(field, 0f, 5.9f, 5.0f), "the beak");
        Assert.False(Inside(field, 0f, 5.9f, 5.8f));
        Assert.True(Inside(field, 0f, 8.7f, 2.2f), "the head");
        Assert.False(Inside(field, 0f, 9.4f, 2.2f));
        Assert.True(Inside(field, 0f, 4.8f, -4.8f), "the tail");
        // The eyes are painted on the head's surface: the head is solid where they sit, not a floating part
        Assert.True(Inside(field, 1.6f, 7.3f, 3.4f));
    }

    [Fact]
    public void LandmarkMaterials_HaveNoRedOrBlueBase()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var root = Path.Combine(Path.GetDirectoryName(content)!, "materials", "bathroom");
        foreach (var name in new[] { "bath_enamel", "bath_chrome", "bath_towel_yellow", "bath_towel_white", "bath_water", "bath_foam", "bath_paper", "bath_paper_core", "bath_rubber_yellow", "bath_beak_orange" })
        {
            var file = Path.Combine(root, name + ".sdfmat");
            Assert.True(File.Exists(file), $"{file} is missing");
            var line = File.ReadAllLines(file).First(l => l.StartsWith("baseColor"));
            var v = line[(line.IndexOf('(') + 1)..line.IndexOf(')')].Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.False(v[0] > 0.6f && v[0] > v[1] + 0.35f && v[0] > v[2] + 0.35f, $"{name} is red");
            Assert.False(v[2] > v[0] + 0.25f && v[2] > v[1] + 0.1f, $"{name} is blue");
        }
    }
}
