using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// The ground of a level as a height over the plane, h(x, z), from 2D shapes and noise only.
/// <list type="bullet">
/// <item>Inside the playable rectangle: 0, plus the gentle unevenness of <see cref="PlayArea.NoiseAmplitude"/>.</item>
/// <item>Outside it, with d the distance to the rectangle: the hills rise smoothly (smoothstep) to their crest at d = ring width, then
/// fall back by <see cref="BackSlopeDrop"/> of their height towards the island's edge; their noise grows with d from nothing at the
/// rectangle to full strength at the crest, so the playable area's edge stays clean while the crest is uneven.</item>
/// </list>
/// The island ends where d = ring width + edge margin (<see cref="EdgeDistance"/>): a rounded rectangle around the playable one. With a
/// rim, its last metres bend down to the rim's height, the same all round (<see cref="IslandEdge.Rim"/>).
/// <para>
/// Placements that flatten (<see cref="Placement.Flatten"/>) then level the ground in a disc round their point to the height it had
/// there, the base their model stands on, and blend back to it over their falloff (smoothstep), one after another in the file's order.
/// </para>
/// </summary>
public sealed class LevelTerrain
{
    /// <summary>How far the back of the hills falls from the crest to the island's edge, as a share of their height.</summary>
    public const float BackSlopeDrop = 0.35f;

    /// <summary>The step of the central differences the normal is taken from, in metres.</summary>
    private const float NormalStep = 0.05f;

    private readonly LevelDefinition level;
    private readonly ValueNoise playNoise;
    private readonly ValueNoise hillNoise;
    private readonly (Vector2 At, float Radius, float Falloff, float Base)[] plateaus;

    public LevelTerrain(LevelDefinition level)
    {
        this.level = level;
        playNoise = new ValueNoise(level.Seed, 1);
        hillNoise = new ValueNoise(level.Seed, 2);
        plateaus = level.Placements.Where(p => p.Flatten > 0)
            .Select(p => (p.At, p.Flatten, MathF.Max(p.FlattenFalloff, 1e-3f), NaturalHeight(p.At.X, p.At.Y)))
            .ToArray();
    }

    public LevelDefinition Level => level;

    /// <summary>The distance from the playable rectangle at which the island ends.</summary>
    public float EdgeDistance => level.Hills.Width + level.Edge.Margin;

    /// <summary>The island's bounding half-size: the playable rectangle grown by <see cref="EdgeDistance"/> on every side.</summary>
    public Vector2 HalfExtent => level.Play.HalfSize + new Vector2(EdgeDistance);

    /// <summary>The distance from the point to the playable rectangle; 0 inside it.</summary>
    public float DistanceOutside(float x, float z)
    {
        var half = level.Play.HalfSize;
        var dx = MathF.Max(MathF.Abs(x) - half.X, 0);
        var dz = MathF.Max(MathF.Abs(z) - half.Y, 0);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public bool OnIsland(float x, float z) => DistanceOutside(x, z) <= EdgeDistance;

    /// <summary>The point of the island's edge nearest a point outside it, straight towards the playable rectangle.</summary>
    public Vector2 OntoEdge(float x, float z)
    {
        var half = level.Play.HalfSize;
        var nearest = new Vector2(Math.Clamp(x, -half.X, half.X), Math.Clamp(z, -half.Y, half.Y));
        var away = new Vector2(x, z) - nearest;
        var length = away.Length();
        return length > 0 ? nearest + away * (EdgeDistance / length) : new Vector2(x, z);
    }

    public float Height(float x, float z)
    {
        var height = NaturalHeight(x, z);
        foreach (var (at, radius, falloff, level) in plateaus)
        {
            var distance = Vector2.Distance(new Vector2(x, z), at);
            // Exactly level within the radius, so a model's base meets it
            if (distance <= radius)
                height = level;
            else if (distance < radius + falloff)
                height += (level - height) * (1 - Smoothstep((distance - radius) / falloff));
        }
        return height;
    }

    /// <summary>The ground before the placements flatten it.</summary>
    public float NaturalHeight(float x, float z)
    {
        var d = DistanceOutside(x, z);
        var play = level.Play;
        var hills = level.Hills;

        // Toward the crest the play area's bumps give way to the hills'; inside the rectangle they are all there is
        var rise = hills.Width > 0 ? Rise(d) : 0f;
        var height = play.NoiseAmplitude * playNoise.Sample(x / play.NoiseScale, z / play.NoiseScale) * (1 - rise);
        if (hills.Width <= 0)
            return height;

        var back = level.Edge.Margin > 0 ? Smoothstep((d - hills.Width) / level.Edge.Margin) : 0f;
        height += hills.Height * (rise - BackSlopeDrop * back);
        height += hills.NoiseAmplitude * rise * hillNoise.Fractal(x / hills.NoiseScale, z / hills.NoiseScale);
        return Rimmed(height, d);
    }

    /// <summary>
    /// How far up the hills the ground is at distance d from the playable rectangle, 0 to 1 (1 at the crest): a smooth S curve, or with
    /// a terrace (<see cref="HillRing.Terrace"/>, ticket 33.3) steps of that depth up a straight slope, each a flat tread then a riser over
    /// the last third (<see cref="HillRing.TreadShare"/>), so the stands of the arena climb in tiers that the crowd stands on.
    /// </summary>
    private float Rise(float d)
    {
        var hills = level.Hills;
        if (hills.Terrace <= 0)
            return Smoothstep(d / hills.Width);
        var step = MathF.Floor(d / hills.Terrace);
        var along = d / hills.Terrace - step;
        var riser = Smoothstep((along - HillRing.TreadShare) / (1 - HillRing.TreadShare));
        return MathF.Min(1, (step + riser) * hills.Terrace / hills.Width);
    }

    /// <summary>
    /// With a rim (<see cref="IslandEdge.Rim"/>), the ground over the last shoulder metres bent down to the rim's height: by the square of
    /// how far into the shoulder it is, so it leaves the slope smoothly and meets the rim at a slant, not level, which keeps the line
    /// where the underside's top cuts the ground close to the edge.
    /// </summary>
    private float Rimmed(float height, float d)
    {
        if (level.Edge.Rim is not { } rim)
            return height;
        var t = Math.Clamp((d - (EdgeDistance - level.Edge.Shoulder)) / level.Edge.Shoulder, 0, 1);
        return height + (rim - height) * t * t;
    }

    /// <summary>The distance from the playable rectangle's edge: positive outside it (<see cref="DistanceOutside"/>), negative inside.</summary>
    public float SignedDistance(float x, float z)
    {
        var half = level.Play.HalfSize;
        var (dx, dz) = (MathF.Abs(x) - half.X, MathF.Abs(z) - half.Y);
        return dx > 0 || dz > 0 ? DistanceOutside(x, z) : MathF.Max(dx, dz);
    }

    /// <summary>The ground's upward unit normal, from the height's slope.</summary>
    public Vector3 Normal(float x, float z)
    {
        var slopeX = (Height(x + NormalStep, z) - Height(x - NormalStep, z)) / (2 * NormalStep);
        var slopeZ = (Height(x, z + NormalStep) - Height(x, z - NormalStep)) / (2 * NormalStep);
        return Vector3.Normalize(new Vector3(-slopeX, 1, -slopeZ));
    }

    /// <summary>0 below 0, 1 above 1, and an S curve between with no slope at either end.</summary>
    private static float Smoothstep(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// Smooth 2D value noise from -1 to 1 in features about one unit wide, the same on every machine for the same seed: the lattice values
/// come from an integer hash, not from a random generator.
/// </summary>
public sealed class ValueNoise(int seed, int stream)
{
    private readonly uint key = Mix((uint)seed * 0x9E3779B1u + (uint)stream * 0x85EBCA77u);

    public float Sample(float x, float z)
    {
        var x0 = MathF.Floor(x);
        var z0 = MathF.Floor(z);
        var (i, j) = ((int)x0, (int)z0);
        var (fx, fz) = (Fade(x - x0), Fade(z - z0));
        var a = Lattice(i, j) + (Lattice(i + 1, j) - Lattice(i, j)) * fx;
        var b = Lattice(i, j + 1) + (Lattice(i + 1, j + 1) - Lattice(i, j + 1)) * fx;
        return a + (b - a) * fz;
    }

    /// <summary>Three octaves, each half the size and strength of the one before, still from -1 to 1.</summary>
    public float Fractal(float x, float z) =>
        (Sample(x, z) + 0.5f * Sample(x * 2.03f + 17.1f, z * 2.03f - 5.3f) + 0.25f * Sample(x * 4.07f - 31.7f, z * 4.07f + 11.9f)) / 1.75f;

    private float Lattice(int i, int j) => Mix(key ^ Mix((uint)i * 0x27D4EB2Fu ^ Mix((uint)j * 0x165667B1u))) / (float)uint.MaxValue * 2 - 1;

    /// <summary>The quintic fade: no slope or curvature at the lattice points, so the noise has no creases.</summary>
    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }
}
