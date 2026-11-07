using System.Numerics;
using SDFPipeline.Meshing;
using SDFProject.MeshExport;
using Xunit;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class BorderCutTests
{
    private static readonly MeshingOptions Options = new() { Resolution = 64, LodCount = 3 };

    /// <summary>A red ball with a blue half and a white quarter: two borders, meeting the third at two points where all three meet.</summary>
    private const string ThreeMaterials = """
        Union
            Color color=(0.9, 0.2, 0.2)
                Sphere radius=0.3
            Color color=(0.1, 0.3, 0.9)
                Intersection
                    Sphere radius=0.3
                    Translate translation=(0.5, 0, 0)
                        Box size=(0.5, 0.5, 0.5)
            Color color=(1, 1, 1)
                Intersection
                    Sphere radius=0.3
                    Translate translation=(-0.5, 0.5, 0)
                        Box size=(0.5, 0.5, 0.5)
        """;

    private static string TwoBalls(string union) => $"""
        {union}
            Color color=(0.9, 0.2, 0.2)
                Sphere radius=0.3
            Color color=(0.1, 0.3, 0.9)
                Translate translation=(0.35, 0, 0)
                    Sphere radius=0.2
        """;

    private static IEnumerable<PartMesh> Levels(MeshedModel model) => model.Meshes.SelectMany(m => new[] { m }.Concat(m.Lods));

    /// <summary>The spots where copies of a border point with different materials stand, and how far their colours are apart.</summary>
    private static (Vector3 Position, int Materials, float ColourJump)[] BorderPoints(PartMesh lod) =>
        lod.Positions.Select((p, v) => (p, v)).GroupBy(x => x.p)
            .Select(g => (g.Key, g.Select(x => lod.Materials[x.v].First).Distinct().Count(),
                g.Max(a => g.Max(b => Vector3.Distance(lod.Materials[a.v].BaseColor, lod.Materials[b.v].BaseColor)))))
            .Where(b => b.Item2 > 1).ToArray();

    /// <summary>
    /// The cut only splits faces within their own planes: the surface keeps its area and the volume it encloses, so no face moved,
    /// overlaps another or turned over. Moving border points off the contour's faces, onto the surface
    /// beside a crease, turned thin faces over.
    /// </summary>
    [Fact]
    public void TheCut_SplitsFacesInTheirOwnPlanes_AndEveryFaceIsOfOneRegion()
    {
        var sdf = Distance(Read("Sphere radius=0.3"));
        var grid = ContouringGrid.Around(new SdfBounds(new Vector3(-0.3f), new Vector3(0.3f)), 24);
        var mesh = DualContouring.Contour(sdf, grid).Mesh;
        int RegionAt(Vector3 p) => (p.X > 0.02f ? 1 : 0) + (p.Y > 0.05f * MathF.Sin(9 * p.Z) ? 2 : 0);
        var regions = mesh.Positions.Select(RegionAt).ToArray();
        var cut = BorderCut.Cut(mesh, regions, RegionAt);

        (double Area, double Volume) Measure(TriangleMesh m)
        {
            double area = 0, volume = 0;
            for (var t = 0; t < m.Triangles.Length; t += 3)
            {
                var (a, b, c) = (m.Positions[m.Triangles[t]], m.Positions[m.Triangles[t + 1]], m.Positions[m.Triangles[t + 2]]);
                area += Vector3.Cross(b - a, c - a).Length() / 2;
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6;
            }
            return (area, volume);
        }
        var (area, volume) = Measure(mesh);
        var (cutArea, cutVolume) = Measure(cut.Mesh);
        Assert.Equal(area, cutArea, area * 1e-4);
        Assert.Equal(volume, cutVolume, volume * 1e-4);

        Assert.Contains(cut.Parents, p => p.Length == 3);
        for (var t = 0; t < cut.Mesh.Triangles.Length; t += 3)
            foreach (var v in cut.Mesh.Triangles[t..(t + 3)])
                Assert.True(cut.IsBorderPoint(v) || regions[v] == cut.FaceRegions[t / 3], "an uncut vertex is in a face of another region");
    }

    [Fact]
    public void WhereThreeMaterialsMeet_EveryFaceIsStillOfOne_AndTheMeetingPointIsAVertex_OnEveryLevel()
    {
        foreach (var lod in Levels(ModelMesher.Mesh(Read(ThreeMaterials), Options)))
        {
            for (var t = 0; t < lod.Triangles.Length; t += 3)
                Assert.Single(lod.Triangles[t..(t + 3)].Select(v => lod.Materials[v].First).Distinct());
            Assert.Contains(BorderPoints(lod), b => b.Materials == 3);
            AssertClosedAndOriented(Welded(lod));
            AssertNoFaceTurnedOver(lod, Distance(Read(ThreeMaterials)));
        }
    }

    [Fact]
    public void AUnionChangesColourSharplyAtTheBorder_ASmoothUnionBlendsAcrossIt()
    {
        var hard = Levels(ModelMesher.Mesh(Read(TwoBalls("Union")), Options)).ToArray();
        var smooth = Levels(ModelMesher.Mesh(Read(TwoBalls("SmoothUnion smoothness=0.08")), Options)).ToArray();
        foreach (var lod in hard.Concat(smooth))
            for (var t = 0; t < lod.Triangles.Length; t += 3)
                Assert.Single(lod.Triangles[t..(t + 3)].Select(v => lod.Materials[v].First).Distinct());

        // Red against blue is a jump of about 1; blended evenly at the border, both sides have nearly the same colour there
        Assert.All(hard, lod => Assert.All(BorderPoints(lod), b => Assert.True(b.ColourJump > 0.8f, $"a jump of only {b.ColourJump}")));
        Assert.All(smooth, lod => Assert.All(BorderPoints(lod), b => Assert.True(b.ColourJump < 0.1f, $"a jump of {b.ColourJump}")));
        Assert.All(hard.Concat(smooth), lod => Assert.NotEmpty(BorderPoints(lod)));
        Assert.All(hard, lod => AssertNoFaceTurnedOver(lod, Distance(Read(TwoBalls("Union")))));
    }

    /// <summary>An eye set into a head: a small ball of another colour half sunk into a big one, meeting it in a fold.</summary>
    private const string EyeInHead = """
        Union
            Color color=(0.6, 0.8, 0.6)
                Sphere radius=0.3
            Color color=(1, 1, 1)
                Translate translation=(0, 0, 0.3)
                    Sphere radius=0.1
        """;

    [Fact]
    public void WhereAnEyeMeetsTheHead_TheSkinKeepsTheHeadsNormal_AndTheEyeItsOwn()
    {
        // At the fold either copy of a border point takes its own side's normal, not the half-eye, half-skin normal of the fold itself
        var model = ModelMesher.Mesh(Read(EyeInHead), Options);
        var white = model.Materials.Select((m, i) => (m, i)).Single(x => x.m.Definition.baseColor.x > 0.9f).i;
        foreach (var lod in Levels(model).Take(1))
        {
            var checkedPoints = 0;
            for (var v = 0; v < lod.Positions.Length; v++)
            {
                if (!BorderPoints(lod).Any(b => b.Position == lod.Positions[v]))
                    continue;
                var p = lod.Positions[v];
                var expected = Vector3.Normalize(lod.Materials[v].First == white ? p - new Vector3(0, 0, 0.3f) : p);
                var angle = MathF.Acos(Math.Clamp(Vector3.Dot(lod.Normals[v], expected), -1, 1)) * 180 / MathF.PI;
                Assert.True(angle < 15, $"a border copy at {p} has a normal {angle:0} degrees off its own sphere's");
                checkedPoints++;
            }
            Assert.True(checkedPoints >= 16, $"only {checkedPoints} border copies");
        }
    }

    /// <summary>No face points against the surface at all three of its corners.</summary>
    private static void AssertNoFaceTurnedOver(PartMesh lod, Func<Vector3, float> sdf)
    {
        const float h = 1e-4f;
        Vector3 Gradient(Vector3 p) => new(sdf(p + new Vector3(h, 0, 0)) - sdf(p - new Vector3(h, 0, 0)),
            sdf(p + new Vector3(0, h, 0)) - sdf(p - new Vector3(0, h, 0)), sdf(p + new Vector3(0, 0, h)) - sdf(p - new Vector3(0, 0, h)));
        for (var t = 0; t < lod.Triangles.Length; t += 3)
        {
            var corners = lod.Triangles[t..(t + 3)].Select(v => lod.Positions[v]).ToArray();
            var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            Assert.False(corners.All(p => Vector3.Dot(normal, Gradient(p)) < 0), $"the face at {corners[0]} is turned over");
        }
    }
}
