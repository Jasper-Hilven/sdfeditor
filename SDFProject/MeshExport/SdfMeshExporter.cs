using System.Numerics;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFProject.MeshExport;

public static class SdfMeshExporter
{
    public const int DefaultResolution = 96;

    public static Func<Vector3, float> DistanceFunction(SDFProgram program)
    {
        var calculator = new SdfCalculatorCpu();
        return p => calculator.Calculate(new V3(p.X, p.Y, p.Z), program);
    }

    public static SdfBounds? FindBounds(SDFProgram program) => SdfBoundsFinder.Find(DistanceFunction(program));

    public static SdfMesh ToMesh(SDFProgram program, int resolution = DefaultResolution)
    {
        var sdf = DistanceFunction(program);
        var bounds = SdfBoundsFinder.Find(sdf);
        return bounds is null ? SdfMesh.Empty : MarchingTetrahedra.Mesh(sdf, bounds, resolution);
    }

    public static byte[] ToStl(SDFProgram program, int resolution = DefaultResolution) => StlWriter.Write(ToMesh(program, resolution));

    public static string ToObj(SDFProgram program, int resolution = DefaultResolution) => ObjWriter.Write(ToMesh(program, resolution));
}
