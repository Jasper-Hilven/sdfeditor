using System.Numerics;
using SDFPipeline.Meshing;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class SimplificationTests
{
    private static TriangleMesh Simplified(string text, int resolution, out ContouringGrid grid)
    {
        (var mesh, grid) = Contour(text, resolution);
        return QuadricSimplifier.Simplify(mesh, Distance(Read(text)), MeshingOptions.DefaultMaxErrorInCells * grid.CellSize, grid.CellSize, MeshingOptions.DefaultCreaseAngleDegrees);
    }

    [Fact]
    public void Box_BecomesEightCornersAndTwelveTriangles()
    {
        var mesh = Simplified("Box size=(0.5, 0.3, 0.2)", 24, out var grid);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(8, mesh.Positions.Distinct().Count());
        foreach (var corner in DualContouringTests.BoxCorners(new Vector3(0.5f, 0.3f, 0.2f)))
            Assert.Contains(mesh.Positions, p => Vector3.Distance(p, corner) < 1e-3f * grid.CellSize);
        AssertClosedAndOriented(mesh);
    }

    [Fact]
    public void LargeFlatWalls_SimplifyToTheirCorners_Quickly()
    {
        // A ring of wall, a castle's in miniature: tens of thousands of contoured triangles on flat faces, where every collapse costs
        // nothing. Taken in no particular order they once grew fans of thousands of faces and took over a minute
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var mesh = Simplified("Subtraction\n    Box size=(1.5, 0.25, 1)\n    Box size=(1.34, 0.5, 0.84)", 300, out _);

        Assert.Equal(32, mesh.TriangleCount);
        AssertClosedAndOriented(mesh);
        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0, 20);
    }

    [Fact]
    public void RotatedBox_BecomesEightCornersAndTwelveTriangles()
    {
        var mesh = Simplified("Rotate rotation=(0.3, 0.5, 0.2)\n    Box size=(0.5, 0.3, 0.2)", 24, out var grid);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(8, mesh.Positions.Distinct().Count());
        var rotation = Quaternion.Concatenate(
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f));
        foreach (var corner in DualContouringTests.BoxCorners(new Vector3(0.5f, 0.3f, 0.2f)).Select(c => Vector3.Transform(c, rotation)))
            Assert.Contains(mesh.Positions, p => Vector3.Distance(p, corner) < 0.01f * grid.CellSize);
        AssertClosedAndOriented(mesh);
    }

    [Fact]
    public void Sphere_StaysClosedAndOnTheSurface()
    {
        var (before, _) = Contour("Sphere radius=0.5", 32);
        var mesh = Simplified("Sphere radius=0.5", 32, out var grid);

        Assert.InRange(mesh.TriangleCount, 100, before.Mesh.TriangleCount * 2 / 3);
        AssertClosedAndOriented(mesh);
        Assert.All(mesh.Positions, p => Assert.InRange(MathF.Abs(p.Length() - 0.5f), 0, 0.2f * grid.CellSize));
        Assert.InRange(SignedVolume(mesh), 0.97f * 4 / 3f * MathF.PI * 0.125f, 1.01f * 4 / 3f * MathF.PI * 0.125f);
    }
}
