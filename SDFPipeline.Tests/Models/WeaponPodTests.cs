using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 16: the weapon pickup pod (content/models/pickups/weapon-pod.sdf) is a low pedestal the player can walk onto: 30 cm tall,
/// a sloped foot no steeper than the character controller's slope limit, a flat top, and a glowing ring inlaid in the top.
/// </summary>
public class WeaponPodTests
{
    private const float Height = 0.3f, SlopeLimitDegrees = 45;

    private static Func<Vector3, float> Pod()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "pickups", "weapon-pod.sdf"))).Distance;
    }

    /// <summary>The height of the highest surface over (x, z), or NaN if there is none.</summary>
    private static float TopAt(Func<Vector3, float> sdf, float x, float z)
    {
        for (var y = 1f; y >= -0.1f; y -= 0.002f)
        {
            if (sdf(new Vector3(x, y, z)) > 0) continue;
            float low = y, high = y + 0.002f;
            for (var i = 0; i < 20; i++)
            {
                var mid = (low + high) / 2;
                if (sdf(new Vector3(x, mid, z)) > 0) high = mid; else low = mid;
            }
            return (low + high) / 2;
        }
        return float.NaN;
    }

    [Fact]
    public void Bounds_AreAboutThirtyCentimetresHigh_AndStandOnTheGround()
    {
        var bounds = SdfBoundsFinder.Find(Pod())!;

        // The finder rounds out to its octree cells, a few centimetres
        Assert.InRange(bounds.Max.Y, 0.29f, 0.36f);
        Assert.InRange(bounds.Min.Y, -0.06f, 0.01f);
        // Wide enough to stand on with the weapon's size in mind
        Assert.InRange(bounds.Max.X - bounds.Min.X, 2f, 3f);
    }

    [Fact]
    public void Top_IsFlatAtThirtyCentimetres_AndTheEdgeSlopesWithinTheSlopeLimit()
    {
        var sdf = Pod();

        Assert.Equal(Height, TopAt(sdf, 0.8f, 0), 0.01f);
        Assert.Equal(Height, TopAt(sdf, 0, -0.3f), 0.01f);

        // Along the bevel the surface falls from the top rim to the foot; no stretch is steeper than the slope limit
        float? previous = null;
        var steepest = 0f;
        for (var x = 0.85f; x <= 1.25f; x += 0.02f)
        {
            var top = TopAt(sdf, x, 0);
            if (previous != null)
                steepest = Math.Max(steepest, MathF.Atan2(previous.Value - top, 0.02f) * 180 / MathF.PI);
            previous = top;
        }
        Assert.InRange(steepest, 20, SlopeLimitDegrees);
    }

    [Fact]
    public void GlowRing_IsInlaidInTheTop_UnderTheStone()
    {
        var sdf = Pod();

        // The stone's groove is filled by the ring, whose top is a centimetre under the stone's
        Assert.Equal(Height - 0.01f, TopAt(sdf, 0.615f, 0), 0.006f);
        Assert.Equal(Height, TopAt(sdf, 0.5f, 0), 0.01f);
        Assert.Equal(Height, TopAt(sdf, 0.75f, 0), 0.01f);
    }
}
