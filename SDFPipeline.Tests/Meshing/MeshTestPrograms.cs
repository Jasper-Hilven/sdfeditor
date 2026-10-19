using System.Numerics;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Meshing;

internal static class MeshTestPrograms
{
    public static SDFProgram Read(string text, PartSource? parts = null) => SdfProgramFile.Read("format 1\n" + text, parts);

    public static Func<Vector3, float> Distance(SDFProgram program) => new SdfCalculatorCpu().Prepare(program).Distance;

    public static (ContouredMesh mesh, ContouringGrid grid) Contour(string text, int resolution)
    {
        var sdf = Distance(Read(text));
        var grid = ContouringGrid.Around(SdfBoundsFinder.Find(sdf)!, resolution);
        return (DualContouring.Contour(sdf, grid), grid);
    }

    /// <summary>The mesh with the copies made for hard normals joined again, to check it is closed.</summary>
    public static TriangleMesh Welded(PartMesh mesh)
    {
        var first = new Dictionary<Vector3, int>();
        var index = mesh.Positions.Select(p => first.TryAdd(p, first.Count) ? first[p] : first[p]).ToArray();
        return new TriangleMesh(first.Keys.ToArray(), mesh.Triangles.Select(i => index[i]).ToArray());
    }

    /// <summary>Every edge is shared by exactly two faces that run along it in opposite directions: closed and consistently oriented.</summary>
    public static void AssertClosedAndOriented(TriangleMesh mesh)
    {
        var directed = new Dictionary<(int, int), int>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        for (var c = 0; c < 3; c++)
        {
            var edge = (mesh.Triangles[3 * t + c], mesh.Triangles[3 * t + (c + 1) % 3]);
            directed[edge] = directed.GetValueOrDefault(edge) + 1;
        }
        Assert.All(directed, entry =>
        {
            Assert.Equal(1, entry.Value);
            Assert.True(directed.ContainsKey((entry.Key.Item2, entry.Key.Item1)), $"Edge {entry.Key} has no face on its other side");
        });
    }

    /// <summary>The volume the mesh encloses: positive when its faces point outwards.</summary>
    public static float SignedVolume(TriangleMesh mesh)
    {
        var volume = 0f;
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = (mesh.Positions[mesh.Triangles[3 * t]], mesh.Positions[mesh.Triangles[3 * t + 1]], mesh.Positions[mesh.Triangles[3 * t + 2]]);
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
        }
        return volume;
    }
}
