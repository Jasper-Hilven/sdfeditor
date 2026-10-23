using System.Numerics;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Levels;

/// <summary>
/// One scattered model where it stands: <see cref="Prototype"/> indexes <see cref="ScatteredLevel.Prototypes"/>, its base (the model's
/// y = 0) is at <see cref="Position"/> on the ground, turned <see cref="Yaw"/> degrees as placements are turned and scaled by
/// <see cref="Scale"/>. <see cref="Tint"/> is the ground's colour under it, sRGB, packed r | g &lt;&lt; 8 | b &lt;&lt; 16 | a &lt;&lt; 24
/// with a the strength (255). <see cref="Hash"/> is a random number of its own, which the renderer thins the instances by, so the same
/// ones go at a lower density.
/// </summary>
public sealed record ScatteredInstance(int Prototype, Vector3 Position, float Yaw, float Scale, uint Tint, uint Hash);

/// <summary>The models the scatter layers use, once each in the order they first appear, with the draw group each is drawn in.</summary>
public sealed record ScatterPrototype(string Model, string Group);

/// <summary>Everything the level's scatter layers place, and how many each placed.</summary>
public sealed record ScatteredLevel(IReadOnlyList<ScatterPrototype> Prototypes, IReadOnlyList<ScatteredInstance> Instances, IReadOnlyList<int> LayerCounts)
{
    public static readonly ScatteredLevel None = new([], [], []);
}

/// <summary>
/// Spreads the level's scatter layers (<see cref="ScatterLayer"/>) over the ground, the same way for the same file and seed on every
/// machine: each layer's points are Poisson-disk samples (Bridson's algorithm) over the playable rectangle grown by the layer's reach,
/// from a random generator seeded by the level's seed and the layer's index, then kept or dropped by fixed rules and a per-point hash.
/// <list type="bullet">
/// <item>Dropped: beyond the reach up the hills, off the island, within the margin of an avoided region (its distance, noise and all, as
/// the region map has it), on a placement's footprint grown by the margin, or on ground steeper than the layer's maximum slope (a
/// placement's level ground is not kept clear: a region painted over it does that).</item>
/// <item>Kept with a probability that gives the layer's density on average: the density times the clustering, over the samples' own
/// density. The clustering is value noise about clusterScale metres wide, turned into weights from none up to cluster times the
/// average: soft patches over about 1 / cluster of the ground, thinning out between them, scaled so that the weights average 1
/// over the ground that is kept. At cluster 1 the scatter is even.</item>
/// </list>
/// Each kept point picks its model by weight, a yaw and a scale at random, and takes the ground's height and colour there. Last, an
/// instance within <see cref="BattleClearing"/> of a battle object (<see cref="BattleLayout"/>) is left out, so the pods, flag stands
/// and spawns stand on open ground, the pods in a clearing of trees and rocks that shows them from afar; so is an instance on the
/// footprint, grown by the layer's margin, of an earlier layer's instance it keeps clear of (<see cref="KeepsClearOf"/>). This is done
/// after the random draws, so the rest of the scatter stays where it was without them.
/// <para>
/// The trees sections (<see cref="LevelDefinition.Trees"/>) are scattered the same way after the scatter layers, with streams of their
/// own (so adding trees leaves the grass where it was), over the band of distance they give instead of the reach; each tree stands on
/// the lowest ground within <see cref="FootRadius"/> of its point.
/// </para>
/// </summary>
public static class LevelScatter
{
    /// <summary>How far round a tree's point the ground is sampled for the lowest, which its base is put on, in metres.</summary>
    public const float FootRadius = 0.5f;

    /// <summary>
    /// How far the ground's colour goes from its base towards its variation colour on average: the ground shader shows half its
    /// patches (the toon style's colour variation) and about half the ground is in a patch.
    /// </summary>
    public const float GroundVariationShare = 0.25f;

    /// <summary>
    /// How far a scattered instance stays from a battle object's edge, in metres, besides the layer's margin: a little for the foliage,
    /// so no grass grows through a pod; more for the trees sections (trees, rocks), whose crowns and boulders reach past their point,
    /// and most round a pod (<see cref="PodTreeClearing"/>), so it stands in a clearing that is seen from afar.
    /// </summary>
    public static float BattleClearing(ScatterLayer layer, BattleObject item) =>
        layer.Margin + (layer.Band is null ? 0.2f : item.Kind == BattleKind.Pod ? PodTreeClearing : 1.5f);

    /// <summary>How far the trees sections' instances stay from a pod's edge, besides the layer's margin, in metres.</summary>
    public const float PodTreeClearing = 6;

    /// <summary>
    /// Whether a layer's points keep clear of an earlier layer's instances, their footprints grown by the later layer's margin, as they
    /// keep clear of the placements: a trees section keeps clear of the earlier trees sections in its own draw group, so pieces, rocks
    /// or trunks of one group do not cut through each other and leave no pinch points between them. The scatter layers (ground cover,
    /// grass under the crowns) and other groups (rocks round the trees' trunks) may still overlap; the trees' footprints are their
    /// crowns', far wider than their trunks.
    /// </summary>
    public static bool KeepsClearOf(ScatterLayer layer, ScatterLayer earlier) =>
        layer.Band is not null && earlier.Band is not null && layer.Group == earlier.Group;

    /// <summary>How many candidates Bridson's algorithm tries round each point before it gives the point up.</summary>
    private const int Candidates = 30;

    /// <param name="footprintOf">Where a model stands on the ground, for the placements and for the models of the trees sections.</param>
    /// <param name="groundColor">The ground's colour at a point, linear RGB.</param>
    /// <param name="holes">Where the ground is left out; nothing is scattered within a layer's margin of one.</param>
    /// <exception cref="SdfProgramFileException">A layer avoids a region the level does not have, or uses a model in two groups.</exception>
    public static ScatteredLevel Scatter(LevelDefinition level, LevelTerrain terrain, GroundRegions regions,
        Func<string, ModelFootprint> footprintOf, Func<Vector2, Vector3> groundColor, GroundHoles? holes = null)
    {
        holes ??= GroundHoles.None;
        var layers = level.Scatters.Concat(level.Trees).ToArray();
        if (layers.Length == 0)
            return ScatteredLevel.None;

        var prototypes = new List<ScatterPrototype>();
        foreach (var layer in layers)
            foreach (var entry in layer.Entries)
            {
                var known = prototypes.FindIndex(p => p.Model == entry.Model);
                if (known < 0)
                    prototypes.Add(new ScatterPrototype(entry.Model, layer.Group));
                else if (prototypes[known].Group != layer.Group)
                    throw new SdfProgramFileException(entry.Line,
                        $"{entry.Model} is drawn in group {prototypes[known].Group} by an earlier scatter; a model is drawn in one group only");
            }

        var instances = new List<ScatteredInstance>();
        var counts = new List<int>();
        for (var index = 0; index < layers.Length; index++)
        {
            var before = instances.Count;
            // Each layer's streams: the scatters' from 200 and 300 on, as before there were trees, the trees' from 400 and 500 on
            var stream = index < level.Scatters.Count ? index : 200 + index - level.Scatters.Count;
            // The earlier layers' instances this one keeps clear of, as obstacles like the placements
            var offset = 0;
            var obstacles = new List<Obstacle>();
            for (var earlier = 0; earlier < index; earlier++)
            {
                if (KeepsClearOf(layers[index], layers[earlier]))
                    foreach (var instance in instances.Skip(offset).Take(counts[earlier]))
                        obstacles.Add(new Obstacle(new Vector2(instance.Position.X, instance.Position.Z), instance.Yaw, instance.Scale,
                            footprintOf(prototypes[instance.Prototype].Model)));
                offset += counts[earlier];
            }
            ScatterLayer(level, layers[index], stream, terrain, regions, holes, footprintOf, groundColor, prototypes, instances, obstacles);
            counts.Add(instances.Count - before);
        }
        return new ScatteredLevel(prototypes, instances, counts);
    }

    private static void ScatterLayer(LevelDefinition level, ScatterLayer layer, int index, LevelTerrain terrain, GroundRegions regions, GroundHoles holes,
        Func<string, ModelFootprint> footprintOf, Func<Vector2, Vector3> groundColor, List<ScatterPrototype> prototypes,
        List<ScatteredInstance> instances, IReadOnlyList<Obstacle> obstacles)
    {
        var avoided = layer.Avoid.Select(name =>
        {
            var region = regions.Regions.ToList().FindIndex(r => r.Name == name);
            return region >= 0
                ? region
                : throw new SdfProgramFileException(layer.Line,
                    $"The scatter avoids '{name}', which is not a region of the level; its regions are {string.Join(", ", regions.Regions.Select(r => r.Name))}");
        }).ToArray();
        var placements = level.Placements.Where(p => !p.Frame).Select(p => new Obstacle(p.At, p.Yaw, p.Scale, footprintOf(p.Model))).ToArray();
        var cosMaxSlope = MathF.Cos(layer.MaxSlope * MathF.PI / 180);
        var battle = level.Battle?.Objects.ToArray() ?? [];

        bool Allowed(Vector2 p)
        {
            if (layer.Band is { } band)
            {
                var distance = terrain.SignedDistance(p.X, p.Y);
                if (distance < band.X || distance > band.Y)
                    return false;
            }
            else if (terrain.DistanceOutside(p.X, p.Y) > layer.Reach)
            {
                return false;
            }
            if (!terrain.OnIsland(p.X, p.Y))
                return false;
            if (layer.Area is { } area && (p.X < area.Min.X || p.X > area.Max.X || p.Y < area.Min.Y || p.Y > area.Max.Y))
                return false;
            foreach (var region in avoided)
                if (regions.Distance(region, p) < layer.Margin)
                    return false;
            if (holes.Any && holes.Distance(p) < layer.Margin)
                return false;
            foreach (var placement in placements)
                if (placement.Covers(p, layer.Margin))
                    return false;
            return terrain.Normal(p.X, p.Y).Y >= cosMaxSlope;
        }

        var half = level.Play.HalfSize + new Vector2(layer.Band is { } outer ? MathF.Max(outer.Y, 0) : layer.Reach);
        var random = new Random32(level.Seed, 200 + index);
        var samples = PoissonDisk(-half, half, layer.Spacing, random);
        var samplesPerSquareMetre = samples.Count / (4 * half.X * half.Y);
        // The rules on all cores; the points stay in their order, so the result does not depend on which finished first
        var allows = new bool[samples.Count];
        Parallel.For(0, samples.Count, i => allows[i] = Allowed(samples[i]));
        var allowed = samples.Where((_, i) => allows[i]).ToArray();
        if (allowed.Length == 0)
            return;

        // The clustering: a weight per point from none to the most, scaled so that the kept points average the layer's density
        var weights = new float[allowed.Length];
        if (layer.Cluster > 1)
        {
            // Patches of plenty with soft edges over about 1 / cluster of the ground, thinning out between them: centred on the noise
            // value that much of the ground is above, as wide as the noise's spread
            var noise = new ValueNoise(level.Seed, 300 + index);
            var values = allowed.Select(p => noise.Fractal(p.X / layer.ClusterScale, p.Y / layer.ClusterScale)).ToArray();
            var sorted = values.Order().ToArray();
            var centre = sorted[Math.Clamp((int)(sorted.Length * (1 - 1 / layer.Cluster)), 0, sorted.Length - 1)];
            var mean = values.Average();
            var spread = MathF.Max(MathF.Sqrt(values.Average(v => (v - mean) * (v - mean))), 1e-4f);
            for (var i = 0; i < allowed.Length; i++)
                weights[i] = Smoothstep(0.5f + (values[i] - centre) / (2 * spread));
            var scale = AverageToOne(weights, layer.Cluster);
            for (var i = 0; i < weights.Length; i++)
                weights[i] = MathF.Min(layer.Cluster, weights[i] * scale);
        }
        else
        {
            Array.Fill(weights, 1f);
        }

        var entries = layer.Entries.Where(e => e.Weight > 0).ToArray();
        var totalWeight = entries.Sum(e => e.Weight);
        for (var i = 0; i < allowed.Length; i++)
        {
            var p = allowed[i];
            var keep = layer.Density * weights[i] / samplesPerSquareMetre;
            if (random.NextFloat() >= keep)
                continue;

            var pick = random.NextFloat() * totalWeight;
            var entry = entries[^1];
            foreach (var candidate in entries)
            {
                if (pick < candidate.Weight)
                {
                    entry = candidate;
                    break;
                }
                pick -= candidate.Weight;
            }
            var yaw = random.NextFloat() * 360 - 180;
            // Facing the middle of the play area (the arena's crowd looks at the floor), give or take the layer's facing; the random
            // draw above is made all the same, so the rest of the scatter stays where it was
            if (layer.Facing is { } jitter)
                yaw = MathF.Atan2(-p.X, -p.Y) * 180 / MathF.PI + (yaw / 180) * jitter;
            var scale = layer.Scale.X + (layer.Scale.Y - layer.Scale.X) * random.NextFloat();
            var height = layer.Band is null ? terrain.Height(p.X, p.Y) : LowestAround(terrain, p, FootRadius);
            var hash = random.NextUInt();
            if (battle.Any(item => Vector2.Distance(p, item.At) < item.Radius + BattleClearing(layer, item)))
                continue;
            if (obstacles.Any(o => o.Covers(p, layer.Margin)))
                continue;
            instances.Add(new ScatteredInstance(prototypes.FindIndex(m => m.Model == entry.Model), new Vector3(p.X, height, p.Y),
                yaw, scale, Srgba(groundColor(p)), hash));
        }
    }

    /// <summary>
    /// A model on the ground that a layer keeps clear of: a placement, or an earlier layer's instance. <see cref="Reach"/> is how far its
    /// footprint's furthest corner is from its point, in metres.
    /// </summary>
    private sealed record Obstacle(Vector2 At, float Yaw, float Scale, ModelFootprint Footprint)
    {
        public float Reach { get; } = Scale * MathF.Max(MathF.Max(Footprint.Min.Length(), Footprint.Max.Length()),
            MathF.Max(new Vector2(Footprint.Min.X, Footprint.Max.Y).Length(), new Vector2(Footprint.Max.X, Footprint.Min.Y).Length()));

        /// <summary>Whether the point is on the footprint grown by the margin.</summary>
        public bool Covers(Vector2 p, float margin)
        {
            // A quick test first: beyond the grown footprint's furthest corner, the margin out from the corner on both axes, it cannot be on it
            var reach = Reach + margin * MathF.Sqrt(2);
            if (Vector2.DistanceSquared(p, At) > reach * reach)
                return false;
            // In the model's own plane, its footprint grown by the margin
            var local = Placement2D.ToLocal(p - At, Yaw) / Scale;
            var grow = margin / Scale;
            return local.X > Footprint.Min.X - grow && local.X < Footprint.Max.X + grow && local.Y > Footprint.Min.Y - grow && local.Y < Footprint.Max.Y + grow;
        }
    }

    /// <summary>The lowest ground at the point and at eight points round it <paramref name="radius"/> away.</summary>
    private static float LowestAround(LevelTerrain terrain, Vector2 p, float radius)
    {
        var lowest = terrain.Height(p.X, p.Y);
        for (var k = 0; k < 8; k++)
        {
            var angle = k * MathF.PI / 4;
            lowest = MathF.Min(lowest, terrain.Height(p.X + radius * MathF.Cos(angle), p.Y + radius * MathF.Sin(angle)));
        }
        return lowest;
    }

    /// <summary>
    /// The factor that makes the weights, each capped at <paramref name="max"/>, average 1 (by bisection: capping makes the average grow
    /// more slowly than the factor).
    /// </summary>
    private static float AverageToOne(float[] weights, float max)
    {
        float Average(float factor) => weights.Sum(w => MathF.Min(max, w * factor)) / weights.Length;
        var (low, high) = (0f, 1f);
        while (Average(high) < 1 && high < 1e6f)
            high *= 2;
        for (var step = 0; step < 40; step++)
        {
            var middle = (low + high) / 2;
            if (Average(middle) < 1)
                low = middle;
            else
                high = middle;
        }
        return high;
    }

    /// <summary>
    /// Points in the rectangle [min, max], none closer than <paramref name="spacing"/> to another and no gap where another would fit
    /// (Bridson, "Fast Poisson disk sampling in arbitrary dimensions", 2007), in the order they were made.
    /// </summary>
    public static List<Vector2> PoissonDisk(Vector2 min, Vector2 max, float spacing, Random32 random)
    {
        var cell = spacing / MathF.Sqrt(2);
        var (width, height) = ((int)MathF.Ceiling((max.X - min.X) / cell), (int)MathF.Ceiling((max.Y - min.Y) / cell));
        var grid = new int[width * height];
        Array.Fill(grid, -1);
        var points = new List<Vector2>();
        var active = new List<int>();
        int Cell(float v, float from, int count) => Math.Clamp((int)((v - from) / cell), 0, count - 1);

        void Add(Vector2 p)
        {
            grid[Cell(p.Y, min.Y, height) * width + Cell(p.X, min.X, width)] = points.Count;
            active.Add(points.Count);
            points.Add(p);
        }

        bool Free(Vector2 p)
        {
            var (i, j) = (Cell(p.X, min.X, width), Cell(p.Y, min.Y, height));
            for (var y = Math.Max(j - 2, 0); y <= Math.Min(j + 2, height - 1); y++)
                for (var x = Math.Max(i - 2, 0); x <= Math.Min(i + 2, width - 1); x++)
                    if (grid[y * width + x] is var other and >= 0 && Vector2.DistanceSquared(points[other], p) < spacing * spacing)
                        return false;
            return true;
        }

        Add(min + (max - min) * new Vector2(random.NextFloat(), random.NextFloat()));
        while (active.Count > 0)
        {
            var slot = (int)(random.NextUInt() % (uint)active.Count);
            var from = points[active[slot]];
            var found = false;
            for (var attempt = 0; attempt < Candidates && !found; attempt++)
            {
                // Uniform over the ring from one to two spacings out
                var angle = random.NextFloat() * 2 * MathF.PI;
                var radius = spacing * MathF.Sqrt(1 + 3 * random.NextFloat());
                var candidate = from + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                if (candidate.X < min.X || candidate.X >= max.X || candidate.Y < min.Y || candidate.Y >= max.Y || !Free(candidate))
                    continue;
                Add(candidate);
                found = true;
            }
            if (!found)
            {
                active[slot] = active[^1];
                active.RemoveAt(active.Count - 1);
            }
        }
        return points;
    }

    /// <summary>A linear colour as sRGB bytes, full strength: r | g &lt;&lt; 8 | b &lt;&lt; 16 | 255 &lt;&lt; 24.</summary>
    public static uint Srgba(Vector3 linear)
    {
        static uint Channel(float c)
        {
            c = Math.Clamp(c, 0, 1);
            var srgb = c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
            return (uint)MathF.Round(srgb * 255);
        }
        return Channel(linear.X) | Channel(linear.Y) << 8 | Channel(linear.Z) << 16 | 255u << 24;
    }

    private static float Smoothstep(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// A small random generator that gives the same numbers on every machine for the same seed and stream (SplitMix32-style: a counter
/// through an integer hash), unlike <see cref="Random"/>, whose algorithm .NET may change.
/// </summary>
public sealed class Random32(int seed, int stream)
{
    private uint state = (uint)seed * 0x9E3779B1u ^ (uint)stream * 0x85EBCA77u;

    public uint NextUInt()
    {
        state += 0x9E3779B9u;
        var h = state;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    /// <summary>From 0 up to, not including, 1.</summary>
    public float NextFloat() => (NextUInt() >> 8) / 16777216f;
}
