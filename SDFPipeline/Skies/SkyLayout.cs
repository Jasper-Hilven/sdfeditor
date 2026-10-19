using System.Numerics;

namespace SDFPipeline.Skies;

/// <summary>A floater's place: the direction it hangs in, how far, and the phase of its bob (0 to 1).</summary>
public sealed record FloaterPlace(float Azimuth, float Elevation, float Distance, float Phase);

/// <summary>
/// Where a level's sky puts what is scattered in it, seeded from the level's seed so the same level always bakes the same sky: the
/// floaters, the abyss spores in a ring round the island's edge (x and z just outside it, a phase, the colour, a size factor), the motes in
/// their box round the camera, and seeds for the stars and the moon's blinks.
/// </summary>
public sealed record SkyLayout(IReadOnlyList<FloaterPlace> Floaters, IReadOnlyList<InlineNumbers>? AbyssSpores,
    IReadOnlyList<InlineNumbers>? NearSpores, int StarSeed, int MoonSeed)
{
    /// <summary>How far outside the island's edge the abyss spores rise, in metres: from just past it to well beyond.</summary>
    public static readonly Vector2 SporeReach = new(1.5f, 30);

    /// <summary>The share of abyss spores in the other colour.</summary>
    public const float AltShare = 0.3f;

    /// <param name="edgeHalfSize">The half-size of the rectangle the island's edge is drawn round.</param>
    /// <param name="edgeRadius">How far the edge is from that rectangle: the island is the rectangle grown by this, with round corners.</param>
    public static SkyLayout Of(SkyDefinition sky, int seed, Vector2 edgeHalfSize, float edgeRadius)
    {
        var random = new SeededRandom(seed, 7);
        var floaters = new List<FloaterPlace>();
        if (sky.Floaters is { } f)
        {
            // Spread round the sky: one per equal slice of azimuth, at a random place in it
            for (var i = 0; i < f.Count; i++)
                floaters.Add(new FloaterPlace((i + 0.15f + 0.7f * random.Next()) * 360 / f.Count,
                    Lerp(f.Elevation, random.Next()), Lerp(f.Distance, random.Next()), random.Next()));
        }

        List<InlineNumbers>? abyss = null;
        if (sky.Spores?.Abyss is { } a)
        {
            abyss = [];
            for (var i = 0; i < a.Count; i++)
            {
                var (point, outward) = OnOutline(edgeHalfSize, edgeRadius, random.Next());
                // More of them close to the edge than far out
                var t = random.Next();
                var position = point + outward * Lerp(SporeReach, t * t);
                abyss.Add(new InlineNumbers(position.X, position.Y, random.Next(), random.Next() < AltShare ? 1 : 0, 0.6f + 0.8f * random.Next()));
            }
        }

        List<InlineNumbers>? near = null;
        if (sky.Spores?.Near is { } n)
        {
            near = [];
            for (var i = 0; i < n.Count; i++)
                near.Add(new InlineNumbers(random.Next() - 0.5f, random.Next() - 0.5f, random.Next() - 0.5f, random.Next()));
        }
        return new SkyLayout(floaters, abyss, near, (int)(random.NextUint() >> 8), (int)(random.NextUint() >> 8));
    }

    /// <summary>
    /// The point a share <paramref name="t"/> (0 to 1) of the way round the island's edge, and the edge's outward normal there: four
    /// straight sides and four quarter circles, by length.
    /// </summary>
    public static (Vector2 Point, Vector2 Outward) OnOutline(Vector2 halfSize, float radius, float t)
    {
        var quarter = MathF.PI / 2 * radius;
        float[] lengths = [2 * halfSize.Y, quarter, 2 * halfSize.X, quarter, 2 * halfSize.Y, quarter, 2 * halfSize.X, quarter];
        var along = t * lengths.Sum();
        var side = 0;
        while (side < lengths.Length - 1 && along > lengths[side])
            along -= lengths[side++];
        var u = lengths[side] > 0 ? along / lengths[side] : 0;
        // Anticlockwise from the +x side's lower end: +x side, corner (+x, +z), +z side, corner (-x, +z), -x side, ...
        (Vector2 Point, Vector2 Outward) Straight(Vector2 from, Vector2 to, Vector2 outward) => (Vector2.Lerp(from, to, u) + outward * radius, outward);
        (Vector2 Point, Vector2 Outward) Corner(Vector2 centre, float startAngle)
        {
            var angle = startAngle + u * MathF.PI / 2;
            var outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            return (centre + outward * radius, outward);
        }
        var (hx, hz) = (halfSize.X, halfSize.Y);
        return side switch
        {
            0 => Straight(new Vector2(hx, -hz), new Vector2(hx, hz), Vector2.UnitX),
            1 => Corner(new Vector2(hx, hz), 0),
            2 => Straight(new Vector2(hx, hz), new Vector2(-hx, hz), Vector2.UnitY),
            3 => Corner(new Vector2(-hx, hz), MathF.PI / 2),
            4 => Straight(new Vector2(-hx, hz), new Vector2(-hx, -hz), -Vector2.UnitX),
            5 => Corner(new Vector2(-hx, -hz), MathF.PI),
            6 => Straight(new Vector2(-hx, -hz), new Vector2(hx, -hz), -Vector2.UnitY),
            _ => Corner(new Vector2(hx, -hz), 3 * MathF.PI / 2),
        };
    }

    private static float Lerp(Vector2 range, float t) => range.X + (range.Y - range.X) * t;

    /// <summary>Numbers from an integer hash of a counter, the same on every machine for the same seed and stream.</summary>
    private sealed class SeededRandom(int seed, int stream)
    {
        private readonly uint key = Mix((uint)seed * 0x9E3779B1u + (uint)stream * 0x85EBCA77u);
        private uint counter;

        public uint NextUint() => Mix(key ^ Mix(++counter * 0x27D4EB2Fu));

        /// <summary>From 0 up to but not including 1.</summary>
        public float Next() => (NextUint() >> 8) / 16777216f;

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
}
