using System.Text;

namespace SDFProject.MeshExport;

public static class StlWriter
{
    public const int HeaderSize = 80;
    public const int TriangleSize = 50;

    public static byte[] Write(SdfMesh mesh, string header = "SDFProject mesh export")
    {
        var bytes = new byte[HeaderSize + 4 + TriangleSize * mesh.TriangleCount];
        using var writer = new BinaryWriter(new MemoryStream(bytes));

        var headerBytes = Encoding.ASCII.GetBytes(header);
        writer.Write(headerBytes, 0, Math.Min(headerBytes.Length, HeaderSize));
        writer.Seek(HeaderSize, SeekOrigin.Begin);
        writer.Write((uint)mesh.TriangleCount);

        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.Triangle(t);
            var n = mesh.Normal(t);
            foreach (var v in new[] { n, a, b, c })
            {
                writer.Write(v.X);
                writer.Write(v.Y);
                writer.Write(v.Z);
            }
            writer.Write((ushort)0);
        }

        return bytes;
    }
}
