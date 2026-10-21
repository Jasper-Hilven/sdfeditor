using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 17.2: the flag stand (content/models/pickups/flag-stand.sdf) is a low base the player can walk onto or round, no higher
/// than the weapon pod and with the pod's bevel rule, with a socket in the middle; the flags (flag-red.sdf, flag-blue.sdf) are a
/// pole of about 2 m with a cloth, the same shape in two colours, whose pole fits the socket.
/// </summary>
public class FlagStandTests
{
    private const float StandTop = 0.25f, PodHeight = 0.3f, SlopeLimitDegrees = 45, SocketBottom = 0.15f, SocketRadius = 0.045f;

    private static Func<Vector3, float> Model(string name)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "pickups", name + ".sdf"))).Distance;
    }

    /// <summary>The height of the highest surface over (x, z) below <paramref name="from"/>, or NaN if there is none.</summary>
    private static float TopAt(Func<Vector3, float> sdf, float x, float z, float from = 1)
    {
        for (var y = from; y >= -0.1f; y -= 0.002f)
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
    public void StandBounds_AreNoHigherThanThePod_AndStandOnTheGround()
    {
        var bounds = SdfBoundsFinder.Find(Model("flag-stand"))!;

        // The finder rounds out to its octree cells, a few centimetres
        Assert.InRange(bounds.Max.Y, 0.29f, 0.36f);
        Assert.InRange(bounds.Min.Y, -0.06f, 0.01f);
        Assert.InRange(bounds.Max.X - bounds.Min.X, 2f, 2.5f);
    }

    [Fact]
    public void StandTop_IsFlat_NothingIsHigherThanThePod_AndTheEdgeSlopesWithinTheSlopeLimit()
    {
        var sdf = Model("flag-stand");

        Assert.Equal(StandTop, TopAt(sdf, 0.65f, 0), 0.01f);
        Assert.Equal(StandTop, TopAt(sdf, 0, -0.3f), 0.01f);

        // Nowhere over the stand is higher than the weapon pod's top
        for (var x = -1.1f; x <= 1.1f; x += 0.05f)
        for (var z = -1.1f; z <= 1.1f; z += 0.05f)
        {
            var top = TopAt(sdf, x, z);
            Assert.True(float.IsNaN(top) || top <= PodHeight + 0.005f, $"({x}, {z}) is {top} m high");
        }

        // Along the bevel the surface falls from the top rim to the foot; no stretch is steeper than the slope limit
        float? previous = null;
        var steepest = 0f;
        for (var x = 0.78f; x <= 1.02f; x += 0.02f)
        {
            var top = TopAt(sdf, x, 0);
            if (previous != null)
                steepest = Math.Max(steepest, MathF.Atan2(previous.Value - top, 0.02f) * 180 / MathF.PI);
            previous = top;
        }
        Assert.InRange(steepest, 20, SlopeLimitDegrees);
    }

    [Fact]
    public void Stand_HasASocketInTheMiddle_WithACollarAroundIt()
    {
        var sdf = Model("flag-stand");

        // The hole goes down to 15 cm; the collar round it is the pod's height
        Assert.Equal(SocketBottom, TopAt(sdf, 0, 0), 0.01f);
        Assert.Equal(SocketBottom, TopAt(sdf, 0.03f, 0), 0.01f);
        Assert.Equal(PodHeight, TopAt(sdf, 0.065f, 0), 0.01f);
    }

    [Theory]
    [InlineData("flag-red")]
    [InlineData("flag-blue")]
    public void Flag_IsAPoleOfAboutTwoMetres_WithAClothFlyingTowardsPlusX(string name)
    {
        var sdf = Model(name);
        var bounds = SdfBoundsFinder.Find(sdf)!;

        Assert.InRange(bounds.Max.Y, 2f, 2.2f);
        Assert.InRange(bounds.Min.Y, -0.06f, 0.01f);
        // The cloth flies about 0.9 m out along +x, thin and with a gentle wave in z
        Assert.InRange(bounds.Max.X, 0.85f, 1.05f);
        Assert.InRange(bounds.Max.Z - bounds.Min.Z, 0.05f, 0.35f);

        // The pole's foot is on the origin and fits the socket
        Assert.True(sdf(new Vector3(0, 0.01f, 0)) < 0);
        Assert.True(sdf(new Vector3(0, 0.1f, 0)) < 0);
        Assert.True(sdf(new Vector3(SocketRadius - 0.005f, 0.1f, 0)) > 0);
        Assert.True(sdf(new Vector3(0, 0.1f, SocketRadius - 0.005f)) > 0);

        // The cloth is there half-way out, at its middle height, and not under it
        Assert.Contains(Enumerable.Range(-10, 21), i => sdf(new Vector3(0.45f, 1.62f, i * 0.01f)) < 0);
        Assert.DoesNotContain(Enumerable.Range(-10, 21), i => sdf(new Vector3(0.45f, 1.2f, i * 0.01f)) < 0);
    }

    [Fact]
    public void BothFlags_HaveTheSameShape()
    {
        var red = Model("flag-red");
        var blue = Model("flag-blue");
        var random = new Random(17);
        for (var i = 0; i < 500; i++)
        {
            var p = new Vector3(random.NextSingle() * 1.2f - 0.1f, random.NextSingle() * 2.3f - 0.1f, random.NextSingle() * 0.4f - 0.2f);
            Assert.Equal(red(p), blue(p), 1e-5f);
        }
    }
}
