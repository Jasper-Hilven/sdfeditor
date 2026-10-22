using System.Numerics;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Levels;

/// <summary>
/// A 2D shape of a ground region, on the level's plane (x across, z along; <see cref="Vector2.Y"/> is z), as a signed distance in
/// metres: negative inside. The distances are those of the pipeline's 2D SDF nodes (SDFProject/Calculation/SdfMath.cs: Circle, Box2D,
/// RoundedBox2D, Segment2D), placed and turned here rather than through Translate2D and Rotate2D nodes, and wobbled by
/// <see cref="Noise"/> metres of value noise in features <see cref="NoiseScale"/> metres wide, so edges are not drawn with a ruler.
/// </summary>
public abstract record RegionShape(float Noise, float NoiseScale)
{
    public const float DefaultNoiseScale = 5;

    /// <summary>The exact distance to the shape, before its noise.</summary>
    public abstract float Distance(Vector2 p);
}

public sealed record CircleRegion(Vector2 Center, float Radius, float Noise, float NoiseScale) : RegionShape(Noise, NoiseScale)
{
    public override float Distance(Vector2 p) => Vector2.Distance(p, Center) - Radius;
}

/// <summary>
/// A rectangle <see cref="Size"/> metres across (its full width and length, like the playable area's size), turned
/// <see cref="Angle"/> degrees as placements are turned, its corners rounded by <see cref="Radius"/> metres inside its size
/// (RoundedBox2D with the same radius at every corner; Box2D at 0).
/// </summary>
public sealed record BoxRegion(Vector2 Center, Vector2 Size, float Angle, float Radius, float Noise, float NoiseScale) : RegionShape(Noise, NoiseScale)
{
    public override float Distance(Vector2 p)
    {
        var local = Placement2D.ToLocal(p - Center, Angle);
        var r = MathF.Min(Radius, MathF.Min(Size.X, Size.Y) / 2);
        var q = Vector2.Abs(local) - Size / 2 + new Vector2(r);
        return MathF.Min(MathF.Max(q.X, q.Y), 0) + Vector2.Max(q, Vector2.Zero).Length() - r;
    }
}

/// <summary>A band <see cref="Width"/> metres wide along the line through <see cref="Points"/>, with round ends: a path.</summary>
public sealed record PolylineRegion(IReadOnlyList<Vector2> Points, float Width, float Noise, float NoiseScale) : RegionShape(Noise, NoiseScale)
{
    /// <summary>Data pulses run along this line, from its first point to its last (ticket 30.9, <see cref="PulseSpec"/>).</summary>
    public bool Pulse { get; init; }

    public override float Distance(Vector2 p)
    {
        var nearest = float.PositiveInfinity;
        for (var i = 0; i + 1 < Points.Count; i++)
        {
            var (a, b) = (Points[i], Points[i + 1]);
            var ab = b - a;
            var t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(Vector2.Dot(ab, ab), 1e-12f), 0, 1);
            nearest = MathF.Min(nearest, Vector2.Distance(p, a + ab * t));
        }
        return nearest - Width / 2;
    }
}

/// <summary>How a placement turns: by yaw degrees about +y, counter-clockwise seen from above (+z towards +x), as glTF turns.</summary>
public static class Placement2D
{
    /// <summary>A point of the model's own plane (x, z) where it lands in the level, before moving it to the placement's point.</summary>
    public static Vector2 ToLevel(Vector2 local, float yawDegrees)
    {
        var (sin, cos) = MathF.SinCos(yawDegrees * MathF.PI / 180);
        return new Vector2(local.X * cos + local.Y * sin, -local.X * sin + local.Y * cos);
    }

    /// <summary>The inverse of <see cref="ToLevel"/>.</summary>
    public static Vector2 ToLocal(Vector2 level, float yawDegrees) => ToLevel(level, -yawDegrees);
}

/// <summary>Where a model stands on the ground: its bounds on the plane (x, z), in its own metres, before it is turned or scaled.</summary>
public sealed record ModelFootprint(Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;

    public Vector2 Center => (Min + Max) / 2;

    /// <summary>How high the model's bounds reach (y max) in its own metres; 0 where unknown (ticket 30.7: the helicopters' keep-outs).</summary>
    public float Height { get; init; }
}

/// <summary>
/// The ground's regions as the level is baked: those of the file, with the ground that placements paint added to the region they name
/// (a region a placement names but the file does not list is added after the listed ones). The ground at a point is grass, or the last
/// region that covers it. Each region's own distance (<see cref="Distance"/>) is also what masks it out of the foliage scatter.
/// </summary>
public sealed class GroundRegions
{
    /// <summary>The regions fit in the four channels of the region map.</summary>
    public const int MaxRegions = 4;

    private readonly ValueNoise[][] noise;

    public GroundRegions(LevelDefinition level, Func<string, ModelFootprint> footprintOf)
    {
        var regions = level.Regions.ToList();
        foreach (var placement in level.Placements)
        {
            if (placement.Ground is not { } name)
                continue;
            var footprint = footprintOf(placement.Model);
            var shape = new BoxRegion(
                placement.At + Placement2D.ToLevel(footprint.Center * placement.Scale, placement.Yaw),
                footprint.Size * placement.Scale + new Vector2(2 * placement.GroundMargin),
                placement.Yaw, placement.GroundMargin, 0, RegionShape.DefaultNoiseScale);
            var index = regions.FindIndex(r => r.Name == name);
            if (index < 0)
            {
                if (regions.Count == MaxRegions)
                    throw new SdfProgramFileException(placement.Line,
                        $"The ground '{name}' would be one region too many, a level has at most {MaxRegions}; paint one of the regions listed");
                var stones = name == GroundRegion.Tiles ? GroundPattern.Stones : GroundPattern.Plain;
                regions.Add(new GroundRegion(name, [shape], stones, GroundRegion.DefaultStoneSize, GroundRegion.DefaultGroutWidth));
            }
            else
            {
                regions[index] = regions[index] with { Shapes = [.. regions[index].Shapes, shape] };
            }
        }
        Regions = regions;
        noise = regions.Select((r, i) => r.Shapes.Select((_, j) => new ValueNoise(level.Seed, 100 + 16 * i + j)).ToArray()).ToArray();
    }

    public IReadOnlyList<GroundRegion> Regions { get; }

    /// <summary>The signed distance in metres to the edge of region <paramref name="index"/>, with its noise: negative inside it.</summary>
    public float Distance(int index, Vector2 p)
    {
        var shapes = Regions[index].Shapes;
        var distance = float.PositiveInfinity;
        for (var s = 0; s < shapes.Count; s++)
        {
            var shape = shapes[s];
            var d = shape.Distance(p);
            if (shape.Noise > 0)
                d += shape.Noise * noise[index][s].Fractal(p.X / shape.NoiseScale, p.Y / shape.NoiseScale);
            distance = MathF.Min(distance, d);
        }
        return distance;
    }

    /// <summary>The region the ground at the point is drawn in, the last that covers it; null for grass.</summary>
    public GroundRegion? At(Vector2 p)
    {
        for (var index = Regions.Count - 1; index >= 0; index--)
            if (Distance(index, p) < 0)
                return Regions[index];
        return null;
    }
}
