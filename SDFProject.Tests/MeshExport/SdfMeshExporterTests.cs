using System.Diagnostics;
using System.Numerics;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using Xunit.Abstractions;
using static SDFProject.Tests.SDFProgramTestHelpers;
using Plane = SDFProject.ProgramDefinition.Plane;

namespace SDFProject.Tests;

public class SdfMeshExporterTests(ITestOutputHelper output)
{
    private static readonly SDFProgram UnitSphere = Program(Sphere(1));

    private static float CellSize(SDFProgram program, int resolution) =>
        SdfMeshExporter.FindBounds(program)!.LongestSide / resolution;

    private static float SignedVolume(SdfMesh mesh) =>
        Enumerable.Range(0, mesh.TriangleCount)
            .Select(mesh.Triangle)
            .Sum(t => Vector3.Dot(t.A, Vector3.Cross(t.B, t.C))) / 6f;

    private static void AssertClosedAndConsistentlyOriented(SdfMesh mesh)
    {
        var directedEdges = new Dictionary<(int, int), int>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        for (var e = 0; e < 3; e++)
        {
            var edge = (mesh.Indices[3 * t + e], mesh.Indices[3 * t + (e + 1) % 3]);
            directedEdges[edge] = directedEdges.GetValueOrDefault(edge) + 1;
        }

        Assert.All(directedEdges, kv =>
        {
            Assert.Equal(1, kv.Value);
            Assert.Equal(1, directedEdges.GetValueOrDefault((kv.Key.Item2, kv.Key.Item1)));
        });
    }

    private static (Vector3 Min, Vector3 Max) Extent(SdfMesh mesh) =>
        (mesh.Vertices.Aggregate(Vector3.Min), mesh.Vertices.Aggregate(Vector3.Max));

    [Fact]
    public void SphereVerticesLieOnTheSurface()
    {
        var mesh = SdfMeshExporter.ToMesh(UnitSphere, 48);
        var cell = CellSize(UnitSphere, 48);

        Assert.NotEmpty(mesh.Vertices);
        Assert.All(mesh.Vertices, v => Assert.InRange(v.Length(), 1 - cell, 1 + cell));
    }

    [Fact]
    public void SphereMeshIsClosed()
    {
        AssertClosedAndConsistentlyOriented(SdfMeshExporter.ToMesh(UnitSphere, 48));
    }

    [Fact]
    public void SphereMeshIsOrientedOutwardWithTheRightVolume()
    {
        var volume = SignedVolume(SdfMeshExporter.ToMesh(UnitSphere, 48));

        Assert.InRange(volume, 4f / 3f * MathF.PI * 0.97f, 4f / 3f * MathF.PI * 1.03f);
    }

    [Fact]
    public void SphereNormalsPointAwayFromTheCenter()
    {
        var mesh = SdfMeshExporter.ToMesh(UnitSphere, 32);

        var outward = Enumerable.Range(0, mesh.TriangleCount)
            .Count(t => Vector3.Dot(mesh.Normal(t), mesh.Triangle(t).A) > 0);
        Assert.True(outward >= mesh.TriangleCount * 0.99, $"{outward} of {mesh.TriangleCount} triangles face outward");
    }

    [Fact]
    public void OffCenterSphereIsFound()
    {
        var program = Program(new Translate(Sphere(0.25f), new ConstValueV3(new V3(20, -7, 3))));
        var mesh = SdfMeshExporter.ToMesh(program, 32);
        var (min, max) = Extent(mesh);

        Assert.Equal(new Vector3(20, -7, 3), (min + max) / 2, new Vector3EqualityComparer(0.02f));
        Assert.InRange(max.X - min.X, 0.45f, 0.5f);
        AssertClosedAndConsistentlyOriented(mesh);
    }

    [Fact]
    public void InitialProgramBoxHasTheRightExtent()
    {
        var program = SdfInteractiveNodeProgramConstants.GetInitialProgram().Program;
        var mesh = SdfMeshExporter.ToMesh(program);
        var cell = CellSize(program, SdfMeshExporter.DefaultResolution);
        var (min, max) = Extent(mesh);

        Assert.Equal(new Vector3(-0.5f), min, new Vector3EqualityComparer(cell));
        Assert.Equal(new Vector3(0.5f), max, new Vector3EqualityComparer(cell));
        Assert.InRange(SignedVolume(mesh), 0.97f, 1.03f);
        AssertClosedAndConsistentlyOriented(mesh);
    }

    [Fact]
    public void BoundsOfInitialProgramAreTight()
    {
        var bounds = SdfMeshExporter.FindBounds(SdfInteractiveNodeProgramConstants.GetInitialProgram().Program)!;

        Assert.InRange(bounds.LongestSide, 1f, 1.5f);
    }

    [Fact]
    public void EmptySceneGivesEmptyMesh()
    {
        var program = Program(new Empty());

        Assert.True(SdfMeshExporter.ToMesh(program).IsEmpty);
        Assert.Equal(84, SdfMeshExporter.ToStl(program).Length);
        Assert.Equal("", SdfMeshExporter.ToObj(program));
    }

    [Fact]
    public void InfinitePlaneIsClampedAndClosed()
    {
        var program = Program(new Plane(new ConstValueV3(new V3(0, 1, 0)), new ConstValueFloat(0)));
        var mesh = SdfMeshExporter.ToMesh(program, 24);
        var (min, max) = Extent(mesh);

        Assert.False(mesh.IsEmpty);
        Assert.InRange(min.X, -55f, -45f);
        Assert.InRange(max.X, 45f, 55f);
        Assert.InRange(max.Y, -0.5f, 0.5f);
        AssertClosedAndConsistentlyOriented(mesh);
        Assert.True(SignedVolume(mesh) > 0);
    }

    [Fact]
    public void InfiniteCylinderIsClampedAndClosed()
    {
        var program = Program(new InfiniteCylinder(new ConstValueV2(new V2(0, 0)), new ConstValueFloat(5)));
        var mesh = SdfMeshExporter.ToMesh(program, 48);

        Assert.False(mesh.IsEmpty);
        AssertClosedAndConsistentlyOriented(mesh);
        Assert.True(SignedVolume(mesh) > 0);
    }

    [Fact]
    public void StlHasHeaderTriangleCountAndOneRecordPerTriangle()
    {
        var mesh = SdfMeshExporter.ToMesh(UnitSphere, 16);
        var stl = StlWriter.Write(mesh);

        Assert.Equal(84 + 50 * mesh.TriangleCount, stl.Length);
        Assert.Equal((uint)mesh.TriangleCount, BitConverter.ToUInt32(stl, 80));
        var (a, _, _) = mesh.Triangle(0);
        Assert.Equal(a.X, BitConverter.ToSingle(stl, 84 + 12));
        Assert.Equal(a.Y, BitConverter.ToSingle(stl, 84 + 16));
        Assert.Equal(a.Z, BitConverter.ToSingle(stl, 84 + 20));
    }

    [Fact]
    public void ObjHasOneLinePerVertexAndFaceWithOneBasedIndices()
    {
        var mesh = SdfMeshExporter.ToMesh(UnitSphere, 16);
        var lines = ObjWriter.Write(mesh).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(mesh.Vertices.Length, lines.Count(l => l.StartsWith("v ")));
        var faces = lines.Where(l => l.StartsWith("f ")).ToList();
        Assert.Equal(mesh.TriangleCount, faces.Count);
        var indices = faces.SelectMany(f => f.Split(' ').Skip(1).Select(int.Parse)).ToList();
        Assert.Equal(1, indices.Min());
        Assert.Equal(mesh.Vertices.Length, indices.Max());
    }

    [Fact]
    public void InitialProgramAtDefaultResolutionIsReasonablyFast()
    {
        var program = SdfInteractiveNodeProgramConstants.GetInitialProgram().Program;
        SdfMeshExporter.ToMesh(program, 16);

        var stopwatch = Stopwatch.StartNew();
        var mesh = SdfMeshExporter.ToMesh(program);
        stopwatch.Stop();

        output.WriteLine($"{stopwatch.ElapsedMilliseconds} ms, {mesh.Vertices.Length} vertices, {mesh.TriangleCount} triangles");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }

    private sealed class Vector3EqualityComparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance * MathF.Sqrt(3f);

        public int GetHashCode(Vector3 v) => 0;
    }
}
