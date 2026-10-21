using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 17.1: the health pod's cross (content/models/pickups/health-cross.sdf) is a chunky rounded plus about 0.5 m across and
/// 0.15 m thick, facing along z, standing on the ground; its arms are solid and the corners between them are empty.
/// </summary>
public class HealthCrossTests
{
    private static Func<Vector3, float> Cross()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "pickups", "health-cross.sdf"))).Distance;
    }

    [Fact]
    public void Bounds_AreAboutHalfAMetreAcross_FifteenCentimetresThick_OnTheGround()
    {
        var bounds = SdfBoundsFinder.Find(Cross())!;

        // The finder rounds out to its octree cells, a few centimetres
        Assert.InRange(bounds.Max.X - bounds.Min.X, 0.48f, 0.58f);
        Assert.InRange(bounds.Max.Y - bounds.Min.Y, 0.48f, 0.58f);
        Assert.InRange(bounds.Max.Z - bounds.Min.Z, 0.14f, 0.22f);
        Assert.InRange(bounds.Min.Y, -0.05f, 0.01f);
        // Centred on x and z, so it turns on the spot
        Assert.InRange((bounds.Max.X + bounds.Min.X) / 2, -0.03f, 0.03f);
        Assert.InRange((bounds.Max.Z + bounds.Min.Z) / 2, -0.03f, 0.03f);
    }

    [Fact]
    public void Plus_HasSolidArms_AndEmptyCorners_AndIsFifteenCentimetresThick()
    {
        var sdf = Cross();
        const float middle = 0.25f;

        // Inside the middle and each arm's end
        Assert.True(sdf(new Vector3(0, middle, 0)) < 0);
        Assert.True(sdf(new Vector3(0.22f, middle, 0)) < 0);
        Assert.True(sdf(new Vector3(-0.22f, middle, 0)) < 0);
        Assert.True(sdf(new Vector3(0, middle + 0.22f, 0)) < 0);
        Assert.True(sdf(new Vector3(0, middle - 0.22f, 0)) < 0);

        // The corners between the arms are open: a plus, not a square
        Assert.True(sdf(new Vector3(0.18f, middle + 0.18f, 0)) > 0.05f);
        Assert.True(sdf(new Vector3(-0.18f, middle - 0.18f, 0)) > 0.05f);

        // 15 cm thick through the red face, a little less through the white rim it stands proud of
        Assert.True(sdf(new Vector3(0, middle, 0.08f)) > 0 && sdf(new Vector3(0, middle, -0.08f)) > 0);
        Assert.True(sdf(new Vector3(0, middle, 0.07f)) < 0 && sdf(new Vector3(0, middle, -0.07f)) < 0);
        Assert.True(sdf(new Vector3(0.24f, middle, 0.07f)) > 0);
    }
}
