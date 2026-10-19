using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.ProgramFiles;

/// <summary>
/// Ticket 15.2: the stone castle (castle1) keeps the fly agaric castle's (castle0) floor heights of ticket 09, part for part
/// (stone_X against castle_X), so a change to one cannot drift the walks and rings of the two apart. Found from the shapes: the
/// heights of the flat up-facing surfaces between 4.9 and 5.4 m with open air over them, seen from above over the part's footprint.
/// </summary>
public class CastleFloorHeightTests
{
    private const float Lowest = 4.9f, Highest = 5.4f, Clearance = 0.3f, Step = 0.4f, Reach = 8f;

    /// <summary>The part, and the heights its floors are at (ticket 09's scheme).</summary>
    public static TheoryData<string, float[]> Parts => new()
    {
        { "wall_bay", [5.00f] },
        { "wall_gate", [5.00f] },
        { "wall_joint", [5.15f] },
        { "tower_large", [5.30f] },
        { "tower_small", [5.30f] },
        { "gate_turret", [5.30f] },
        { "keep", [5.30f] },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void StoneCastle_HasTheFlyAgaricCastlesFloorHeights_PerPart(string part, float[] expected)
    {
        var fungal = Heights("castle0", "castle_" + part);
        var stone = Heights("castle1", "stone_" + part);

        Assert.Equal(expected, fungal);
        Assert.Equal(fungal, stone);
    }

    /// <summary>The distinct heights (to the centimetre) of the part's floors.</summary>
    private static float[] Heights(string model, string part)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var path = Path.Combine(content, "models", "buildings", model + ".sdf");
        var sdf = new SdfCalculatorCpu().Prepare(SdfProgramFile.LoadPart(path, part)).Distance;

        var heights = new SortedSet<float>();
        for (var z = -Reach; z <= Reach; z += Step)
            for (var x = -Reach; x <= Reach; x += Step)
            {
                // The highest surface in the band seen from above, with open air over it
                if (sdf(new Vector3(x, Highest, z)) <= 0) continue;
                for (var y = Highest; y > Lowest; y -= 0.01f)
                {
                    if (sdf(new Vector3(x, y - 0.01f, z)) > 0) continue;
                    var top = Surface(sdf, x, z, y - 0.01f, y);
                    // Flat (the surface does not slope over 10 cm round) and with nothing over it
                    if (sdf(new Vector3(x, top + Clearance, z)) > Clearance * 0.9f
                        && Math.Abs(Surface(sdf, x + 0.1f, z, top - 0.05f, top + 0.05f) - top) < 0.004f
                        && Math.Abs(Surface(sdf, x, z + 0.1f, top - 0.05f, top + 0.05f) - top) < 0.004f)
                        heights.Add(MathF.Round(top, 2));
                    break;
                }
            }
        return heights.ToArray();
    }

    /// <summary>The height between <paramref name="low"/> (inside) and <paramref name="high"/> (outside) where the shape's surface lies; NaN if not bracketed.</summary>
    private static float Surface(Func<Vector3, float> sdf, float x, float z, float low, float high)
    {
        if (sdf(new Vector3(x, high, z)) <= 0 || sdf(new Vector3(x, low, z)) > 0) return float.NaN;
        for (var i = 0; i < 12; i++)
        {
            var mid = (low + high) / 2;
            if (sdf(new Vector3(x, mid, z)) > 0) high = mid; else low = mid;
        }
        return (low + high) / 2;
    }
}
