using System.Numerics;

namespace SDFProject.MeshExport;

public sealed record SdfBounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public float LongestSide => MathF.Max(Size.X, MathF.Max(Size.Y, Size.Z));

    public SdfBounds Union(SdfBounds other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public SdfBounds Clamp(float halfExtent) =>
        new(Vector3.Clamp(Min, new Vector3(-halfExtent), new Vector3(halfExtent)),
            Vector3.Clamp(Max, new Vector3(-halfExtent), new Vector3(halfExtent)));
}
