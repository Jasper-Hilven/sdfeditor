using System.Numerics;
using SDFPipeline.Meshing;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

/// <summary>The chanterelle scout's hard places: a thin wavy cap rim whose field is not an exact distance, and a thin eyelid shell.</summary>
public class ThinFeatureTests
{
    private const string RimAndLid = """
        Union
            RadialWave amount=0.08 count=7
                Distort amount=0.006 frequency=3
                    Revolution offset=0
                        Translate2D offset=(0.12, 1.3)
                            Rotate2D angle=-1.0667
                                UnevenCapsule2D bottomRadius=0.07 topRadius=0.018 height=0.66
            Translate translation=(0.1, 0.86, 0.13)
                Round radius=0.006
                    Intersection
                        Onion thickness=0.004
                            Sphere radius=0.13
                        Plane normal=(0, -1, 0) height=0.051
        """;

    // From 64 cells along the model's 1.6 m height on, the rim's 1.8 cm lip is at least a cell across; the exponent 0 keeps exactly that many
    [Theory]
    [InlineData(64)]
    [InlineData(96)]
    public void ThinRimAndShell_AreClosed_Unfolded_AndOnTheSurface(int resolution)
    {
        var program = Read(RimAndLid);
        var sdf = Distance(program);
        var model = ModelMesher.Mesh(program, new MeshingOptions { Resolution = resolution, SizeExponent = 0 });
        var cellSize = model.Grid!.CellSize;
        var mesh = Assert.Single(model.Meshes);

        var welded = Welded(mesh);
        AssertClosedAndOriented(welded);
        for (var t = 0; t < welded.TriangleCount; t++)
        {
            var (a, b, c) = (welded.Positions[welded.Triangles[3 * t]], welded.Positions[welded.Triangles[3 * t + 1]], welded.Positions[welded.Triangles[3 * t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            Assert.True(normal.LengthSquared() > 0, $"Triangle {t} is flat");
            Assert.True(Vector3.Dot(normal, Gradient(sdf, (a + b + c) / 3, cellSize * 1e-3f)) > 0, $"Triangle {t} at {(a + b + c) / 3} faces against the surface");
        }
        Assert.All(mesh.Positions, p => Assert.InRange(MathF.Abs(sdf(p)), 0, 0.5f * cellSize));
    }

    [Fact]
    public void ThinShell_GetsAVertexForEachSide()
    {
        // A shell a third of a cell thick: plain dual contouring folds both sides into one vertex per cell
        var (contoured, grid) = Contour("Onion thickness=0.02\n    Sphere radius=0.5", 9);
        var sdf = Distance(Read("Onion thickness=0.02\n    Sphere radius=0.5"));

        AssertClosedAndOriented(contoured.Mesh);
        Assert.Contains(contoured.Mesh.Positions, p => p.Length() < 0.49f);
        Assert.Contains(contoured.Mesh.Positions, p => p.Length() > 0.51f);
        Assert.All(contoured.Mesh.Positions, p => Assert.InRange(MathF.Abs(sdf(p)), 0, 0.25f * grid.CellSize));
    }

    private static Vector3 Gradient(Func<Vector3, float> sdf, Vector3 p, float h) =>
        new(sdf(p + new Vector3(h, 0, 0)) - sdf(p - new Vector3(h, 0, 0)),
            sdf(p + new Vector3(0, h, 0)) - sdf(p - new Vector3(0, h, 0)),
            sdf(p + new Vector3(0, 0, h)) - sdf(p - new Vector3(0, 0, h)));
}
