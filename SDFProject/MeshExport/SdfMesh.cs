using System.Numerics;

namespace SDFProject.MeshExport;

public sealed record SdfMesh(Vector3[] Vertices, int[] Indices)
{
    public static readonly SdfMesh Empty = new([], []);

    public int TriangleCount => Indices.Length / 3;

    public bool IsEmpty => Indices.Length == 0;

    public (Vector3 A, Vector3 B, Vector3 C) Triangle(int index) =>
        (Vertices[Indices[3 * index]], Vertices[Indices[3 * index + 1]], Vertices[Indices[3 * index + 2]]);

    public Vector3 Normal(int index)
    {
        var (a, b, c) = Triangle(index);
        var n = Vector3.Cross(b - a, c - a);
        var length = n.Length();
        return length > 0f ? n / length : Vector3.Zero;
    }
}
