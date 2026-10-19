using System.Numerics;
using SDFPipeline.Meshing;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class DualContouringTests
{
    [Fact]
    public void Sphere_IsClosed_Outward_AndOnTheSurface()
    {
        var (contoured, grid) = Contour("Sphere radius=0.5", 24);
        var mesh = contoured.Mesh;

        AssertClosedAndOriented(mesh);
        Assert.InRange(SignedVolume(mesh), 0.95f * 4 / 3f * MathF.PI * 0.125f, 1.01f * 4 / 3f * MathF.PI * 0.125f);
        Assert.All(mesh.Positions, p => Assert.InRange(p.Length() - 0.5f, -0.05f * grid.CellSize, 0.05f * grid.CellSize));
    }

    [Fact]
    public void Box_HasVerticesExactlyOnItsCorners()
    {
        var (contoured, grid) = Contour("Box size=(0.5, 0.3, 0.2)", 20);
        var mesh = contoured.Mesh;

        AssertClosedAndOriented(mesh);
        foreach (var corner in BoxCorners(new Vector3(0.5f, 0.3f, 0.2f)))
            Assert.Contains(mesh.Positions, p => Vector3.Distance(p, corner) < 1e-3f * grid.CellSize);
    }

    [Fact]
    public void EllipsoidCentre_WhichIsNaN_DoesNotBreakIt()
    {
        var mesh = Contour("Ellipsoid radii=(0.3, 0.2, 0.25)", 16).mesh.Mesh;

        AssertClosedAndOriented(mesh);
        Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z)));
    }

    internal static IEnumerable<Vector3> BoxCorners(Vector3 half) =>
        Enumerable.Range(0, 8).Select(c => new Vector3((c & 1) == 0 ? -half.X : half.X, (c & 2) == 0 ? -half.Y : half.Y, (c & 4) == 0 ? -half.Z : half.Z));
}
