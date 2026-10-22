using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// A place the ground is left out, the level file's <c>hole=1</c> region (ticket 28.1): the kitchen's sink, a bathtub. Shaped like a
/// ground region (<see cref="Shapes"/>, their union) but no region: it has no material, no channel in the region map and is not
/// something a scatter can <c>avoid</c> by name (scatters keep clear of every hole by themselves).
/// </summary>
/// <param name="Line">The line of the level file it is on.</param>
public sealed record GroundHole(string Name, IReadOnlyList<RegionShape> Shapes, int Line);

/// <summary>
/// The holes of a level as the baker uses them: <see cref="Distance"/> is the signed distance in metres to the nearest hole's edge
/// (negative inside), with each shape's noise as in <see cref="GroundRegions"/>. <see cref="GroundMesher"/> leaves the ground out inside
/// and moves the grid points there onto the edge, so on a flat ground a ruled edge (<c>RoundedBox2D</c>, no noise) is cut crisp to the
/// shape at every level of detail, not to the grid's cells.
/// </summary>
public sealed class GroundHoles
{
    public static readonly GroundHoles None = new([], 1);

    private const float Epsilon = 1e-4f;

    private readonly (RegionShape Shape, ValueNoise Noise)[] shapes;

    public GroundHoles(IReadOnlyList<GroundHole> holes, int seed)
    {
        shapes = holes.SelectMany((hole, i) => hole.Shapes.Select((shape, j) => (shape, new ValueNoise(seed, 700 + 16 * i + j)))).ToArray();
    }

    public bool Any => shapes.Length > 0;

    public float Distance(Vector2 p)
    {
        var distance = float.PositiveInfinity;
        foreach (var (shape, noise) in shapes)
        {
            var d = shape.Distance(p);
            if (shape.Noise > 0)
                d += shape.Noise * noise.Fractal(p.X / shape.NoiseScale, p.Y / shape.NoiseScale);
            distance = MathF.Min(distance, d);
        }
        return distance;
    }

    /// <summary>Whether the point is in a hole or on its edge (within a hair, for the rounding of the edge's own points).</summary>
    public bool Contains(Vector2 p) => Any && Distance(p) <= Epsilon;

    /// <summary>The point of the hole's edge nearest a point inside it: stepping out along the distance's gradient, a few times for curved edges.</summary>
    public Vector2 OntoEdge(Vector2 p)
    {
        const float h = 0.02f;
        for (var step = 0; step < 4; step++)
        {
            var d = Distance(p);
            if (MathF.Abs(d) < Epsilon)
                break;
            var gradient = new Vector2(Distance(p + new Vector2(h, 0)) - Distance(p - new Vector2(h, 0)),
                Distance(p + new Vector2(0, h)) - Distance(p - new Vector2(0, h))) / (2 * h);
            var length = gradient.Length();
            if (length < 1e-6f)
                break;
            p -= gradient / length * d;
        }
        return p;
    }
}
