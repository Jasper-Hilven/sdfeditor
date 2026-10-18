using System.Numerics;

namespace SDFProject.MeshExport;

public static class SdfBoundsFinder
{
    public const float DefaultHalfExtent = 50f;

    private const float Slack = 1.5f;
    private const int Refinement = 32;
    private const int MaxLevels = 16;
    private const int MaxEvaluationsPerLevel = 100_000;

    private readonly record struct Cell(Vector3 Center, float HalfSize)
    {
        public SdfBounds Bounds => new(Center - new Vector3(HalfSize), Center + new Vector3(HalfSize));

        public IEnumerable<Cell> Children()
        {
            var h = HalfSize * 0.5f;
            for (var i = 0; i < 8; i++)
                yield return new Cell(Center + new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h), h);
        }
    }

    public static SdfBounds? Find(Func<Vector3, float> sdf, float halfExtent = DefaultHalfExtent)
    {
        var cells = new List<Cell> { new(Vector3.Zero, halfExtent) };
        for (var level = 0; ; level++)
        {
            var near = cells.Where(c => IsNearSurface(sdf(c.Center), c.HalfSize)).ToList();
            if (near.Count == 0) return null;

            var bounds = near.Select(c => c.Bounds).Aggregate((a, b) => a.Union(b)).Clamp(halfExtent);
            var halfSize = near[0].HalfSize;
            if (level >= MaxLevels || 2f * halfSize <= bounds.LongestSide / Refinement || near.Count * 8 > MaxEvaluationsPerLevel)
                return bounds;

            cells = near.SelectMany(c => c.Children()).ToList();
        }
    }

    // NaN, as at the exact centre of an ellipsoid, says nothing about the surface, so the cell is looked at more closely; infinity is empty space.
    private static bool IsNearSurface(float distance, float halfSize) =>
        float.IsNaN(distance) || float.IsFinite(distance) && MathF.Abs(distance) <= halfSize * MathF.Sqrt(3f) * Slack;
}
