using System.Globalization;
using System.Text;

namespace SDFProject.MeshExport;

public static class ObjWriter
{
    public static string Write(SdfMesh mesh)
    {
        var builder = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        foreach (var v in mesh.Vertices)
            builder.Append(culture, $"v {v.X:R} {v.Y:R} {v.Z:R}\n");

        for (var t = 0; t < mesh.TriangleCount; t++)
            builder.Append(culture, $"f {mesh.Indices[3 * t] + 1} {mesh.Indices[3 * t + 1] + 1} {mesh.Indices[3 * t + 2] + 1}\n");

        return builder.ToString();
    }
}
