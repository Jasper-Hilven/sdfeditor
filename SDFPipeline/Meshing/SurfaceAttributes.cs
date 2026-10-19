using System.Numerics;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Meshing;

/// <summary>A material of the model's material table: a Material node's name, "color(r, g, b)" for a Color node, or "default".</summary>
public sealed record MeshMaterial(string Name, MaterialDefinition Definition, MaterialSource Source);

/// <summary>
/// The materials a model's meshes use, numbered in the order they are first met. Vertices refer to them by number; shaders look up
/// their parameters in the table. Not for use from several threads at once.
/// </summary>
public sealed class MaterialTable(MaterialLibrary library)
{
    private readonly Dictionary<MaterialSource, int> indices = [];
    private readonly List<MeshMaterial> materials = [];

    public IReadOnlyList<MeshMaterial> Materials => materials;

    /// <summary>The material's number, adding it to the table the first time it is met.</summary>
    public int IndexOf(MaterialSource source)
    {
        if (indices.TryGetValue(source, out var index))
            return index;
        index = materials.Count;
        indices[source] = index;
        materials.Add(new MeshMaterial(NameOf(source), source.Resolve(library), source));
        return index;
    }

    public MaterialDefinition this[int index] => materials[index].Definition;

    private static string NameOf(MaterialSource source) =>
        source.Name ?? (source.Color is { } c ? FormattableString.Invariant($"color({c.x:0.###}, {c.y:0.###}, {c.z:0.###})") : "default");
}

/// <summary>
/// What a vertex's shader needs of the material there: the two materials that weigh most and how far it is from the first to the second,
/// and <paramref name="Mix"/>, every parameter of the whole mix as the renderer shades with it (its base colour is <paramref name="BaseColor"/>).
/// </summary>
public readonly record struct VertexMaterial(int First, int Second, float Blend, Vector3 BaseColor, Vector3 LocalPosition, MaterialDefinition Mix);

/// <summary>Per-vertex data computed from the SDF rather than the mesh, so the mesh can be shaded like the raymarched preview.</summary>
public static class SurfaceAttributes
{
    /// <summary>
    /// The material mix at a point, as the renderer's material function finds it, reduced to the two strongest materials; the base colour
    /// and the shape-local position are those of the whole mix, exactly as the renderer mixes them.
    /// </summary>
    public static VertexMaterial Material(SurfaceSample sample, MaterialTable table)
    {
        var shares = sample.Materials.OrderByDescending(s => s.Weight).ToArray();
        var first = table.IndexOf(shares[0].Source);
        var second = shares.Length > 1 ? table.IndexOf(shares[1].Source) : first;
        var pair = shares[0].Weight + (shares.Length > 1 ? shares[1].Weight : 0);
        var blend = shares.Length > 1 && pair > 0 ? shares[1].Weight / pair : 0;

        var local = Vector3.Zero;
        foreach (var share in shares)
            local += share.Weight * share.LocalPosition;
        var mix = Mix(shares.Select(share => (table[table.IndexOf(share.Source)], share.Weight)));
        return new VertexMaterial(first, second, blend, new Vector3(mix.baseColor.x, mix.baseColor.y, mix.baseColor.z), local, mix);
    }

    /// <summary>Every parameter weighted by its material's share, as the renderer's mixMaterial does; the weights add up to 1.</summary>
    public static MaterialDefinition Mix(IEnumerable<(MaterialDefinition Material, float Weight)> shares)
    {
        var shareArray = shares.ToArray();
        if (shareArray is [var (only, _)])
            return only;
        var mix = MaterialDefinition.Default;
        foreach (var parameter in MaterialDefinition.Parameters)
        {
            var sum = new float[parameter.ComponentCount];
            foreach (var (material, weight) in shareArray)
            {
                var values = parameter.Get(material);
                for (var c = 0; c < sum.Length; c++)
                    sum[c] += weight * values[c];
            }
            mix = parameter.With(mix, sum);
        }
        return mix;
    }

    /// <summary>
    /// The colour the renderer lights the surface with: the base colour drifting towards variationColor in large patches, blotched
    /// lighter and darker and speckled (all drawn at the shape-local position), shifted to edgeColor where the material is thinner than
    /// edgeThickness, and darkened where it is wet. Baked per vertex, so any glTF viewer shows the colour the preview shows.
    /// </summary>
    /// <param name="thickness">Metres of material straight in from the surface, as <see cref="Thickness"/> measures it.</param>
    /// <param name="filterRadius">
    /// How far around the vertex its colour stands for, in metres, about half the distance to its neighbours. Speckles and blotches
    /// smaller than that are averaged over the ball rather than sampled at its centre, so a coarse mesh shows their overall tint
    /// instead of a random fleck or none. 0 samples the centre only.
    /// </param>
    public static Vector3 Albedo(MaterialDefinition m, Vector3 localPosition, float thickness, float filterRadius = 0)
    {
        var color = filterRadius > 0 ? FilteredColor(m, localPosition, filterRadius) : VariedColor(m, localPosition);
        if (m.edgeThickness > 0)
            color = Vector3.Lerp(color, new Vector3(m.edgeColor.x, m.edgeColor.y, m.edgeColor.z), Thinness(thickness, m.edgeThickness));
        return color * (1 - 0.2f * m.wetness);
    }

    /// <summary>The renderer's variedColor: large soft patches of variationColor, blotches a few detail features wide, then speckles.</summary>
    public static Vector3 VariedColor(MaterialDefinition m, Vector3 local)
    {
        var color = new Vector3(m.baseColor.x, m.baseColor.y, m.baseColor.z);
        if (m.variationScale > 0)
        {
            var patch = SmoothStep(0.3f, 0.75f, ShadeDetail(local / m.variationScale + new Vector3(37.9f)));
            color = Vector3.Lerp(color, new Vector3(m.variationColor.x, m.variationColor.y, m.variationColor.z), patch);
        }
        if (m.colorVariation > 0)
        {
            var variation = ShadeDetail(local / MathF.Max(m.detailScale * 5, 0.001f) + new Vector3(71.3f)) * 2 - 1;
            color = Vector3.Clamp(color * (1 + m.colorVariation * variation), Vector3.Zero, Vector3.One);
        }
        if (m.speckleScale > 0 && m.speckleDensity > 0)
        {
            var (weight, tone) = Speckle(local, m.speckleScale, m.speckleDensity, m.speckleSize);
            var fleck = Vector3.Clamp(new Vector3(m.speckleColor.x, m.speckleColor.y, m.speckleColor.z) * tone, Vector3.Zero, Vector3.One);
            color = Vector3.Lerp(color, fleck, weight);
        }
        return color;
    }

    /// <summary>
    /// The renderer's speckle: flecks in the cells <paramref name="scale"/> wide that the hash picks (<paramref name="density"/> of
    /// them), each placed, sized and toned at random, with a ragged soft edge. Weight is how far the colour goes to the fleck's, tone
    /// how light the fleck is (0.65 to 1.35 times speckleColor).
    /// </summary>
    public static (float Weight, float Tone) Speckle(Vector3 local, float scale, float density, float size)
    {
        var p = local / scale;
        var cell = new Vector3(MathF.Floor(p.X), MathF.Floor(p.Y), MathF.Floor(p.Z));
        var ragged = 0.35f * (ShadeNoise(p * 3.1f + new Vector3(5.3f)) - 0.5f);
        var result = (Weight: 0f, Tone: 1f);
        for (var z = -1; z <= 1; z++)
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            var c = cell + new Vector3(x, y, z);
            if (ShadeHash(c + new Vector3(0.37f)) >= density)
                continue;
            var center = c + new Vector3(0.15f) + 0.7f * new Vector3(
                ShadeHash(c + new Vector3(11.3f)), ShadeHash(c + new Vector3(23.7f)), ShadeHash(c + new Vector3(41.9f)));
            var radius = MathF.Max(size, 0.01f) * (0.5f + ShadeHash(c + new Vector3(57.1f)));
            var weight = 1 - SmoothStep(0.55f, 1, (p - center).Length() / radius + ragged);
            if (weight > result.Weight)
                result = (weight, 0.65f + 0.7f * ShadeHash(c + new Vector3(73.3f)));
        }
        return result;
    }

    /// <summary>The varied colour averaged over a ball: its centre and 24 points spread through it, the same for every vertex.</summary>
    private static Vector3 FilteredColor(MaterialDefinition m, Vector3 local, float radius)
    {
        var sum = VariedColor(m, local);
        foreach (var offset in FilterOffsets)
            sum += VariedColor(m, local + offset * radius);
        return sum / (FilterOffsets.Length + 1);
    }

    /// <summary>Points spread evenly through the unit ball: a golden-angle spiral over the sphere, at radii that fill it evenly.</summary>
    private static readonly Vector3[] FilterOffsets = Enumerable.Range(0, 24).Select(i =>
    {
        var y = 1 - 2 * (i + 0.5f) / 24;
        var around = MathF.Sqrt(1 - y * y);
        var angle = i * 2.3999632f;
        var r = MathF.Cbrt((i % 4 + 0.5f) / 4);
        return r * new Vector3(around * MathF.Cos(angle), y, around * MathF.Sin(angle));
    }).ToArray();

    /// <summary>
    /// 1 where the material is much thinner than <paramref name="edgeThickness"/>, 0 where it is as thick: the renderer counts which of
    /// five steps in along the normal are inside, which for a measured thickness is this ramp.
    /// </summary>
    public static float Thinness(float thickness, float edgeThickness) =>
        edgeThickness <= 0 ? 0 : Math.Clamp(1 - thickness / edgeThickness, 0, 1);

    /// <summary>The renderer's value noise for surface detail, three octaves, 0 to 1; the Unity shader has the same function.</summary>
    public static float ShadeDetail(Vector3 p) =>
        0.6f * ShadeNoise(p) + 0.3f * ShadeNoise(p * 2.07f + new Vector3(13.1f)) + 0.1f * ShadeNoise(p * 4.13f + new Vector3(29.7f));

    private static float ShadeNoise(Vector3 p)
    {
        var i = new Vector3(MathF.Floor(p.X), MathF.Floor(p.Y), MathF.Floor(p.Z));
        var f = p - i;
        var u = f * f * (new Vector3(3) - 2 * f);
        float H(float x, float y, float z) => ShadeHash(i + new Vector3(x, y, z));
        return Lerp(
            Lerp(Lerp(H(0, 0, 0), H(1, 0, 0), u.X), Lerp(H(0, 1, 0), H(1, 1, 0), u.X), u.Y),
            Lerp(Lerp(H(0, 0, 1), H(1, 0, 1), u.X), Lerp(H(0, 1, 1), H(1, 1, 1), u.X), u.Y),
            u.Z);
    }

    private static float ShadeHash(Vector3 p)
    {
        p = Fract(p * 0.3183099f + new Vector3(0.1f)) * 17;
        return Fract(p.X * p.Y * p.Z * (p.X + p.Y + p.Z));
    }

    private static Vector3 Fract(Vector3 v) => new(Fract(v.X), Fract(v.Y), Fract(v.Z));

    private static float Fract(float x) => x - MathF.Floor(x);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Metres of material straight in from the surface, along −<paramref name="normal"/>, up to <paramref name="max"/>: what light shining
    /// through has to cross, and what the edge colour of thin material (edgeThickness) is measured against. Steps through the inside
    /// by the distance to the nearest surface, which never oversteps the far side.
    /// </summary>
    public static float Thickness(Func<Vector3, float> sdf, Vector3 position, Vector3 normal, float max, float cellSize)
    {
        var minimumStep = cellSize * 0.01f;
        var t = cellSize * 0.01f;
        var inside = false;
        var lastInside = t;
        for (var step = 0; step < 256 && t < max; step++)
        {
            var d = sdf(position - normal * t);
            if (float.IsNaN(d))
                d = -minimumStep;
            if (d < 0)
            {
                inside = true;
                lastInside = t;
                t += MathF.Max(-d, minimumStep);
                continue;
            }
            if (inside)
                return Refine(sdf, position, normal, lastInside, t);
            // Not in yet: the vertex sits a hair outside the surface
            if (t > cellSize)
                return 0;
            t += MathF.Max(d, minimumStep);
        }
        return inside ? max : 0;
    }

    private static float Refine(Func<Vector3, float> sdf, Vector3 position, Vector3 normal, float inside, float outside)
    {
        for (var i = 0; i < 8; i++)
        {
            var middle = (inside + outside) * 0.5f;
            if (sdf(position - normal * middle) < 0) inside = middle;
            else outside = middle;
        }
        return (inside + outside) * 0.5f;
    }

    /// <summary>The renderer's ambient occlusion, 0 (closed in) to 1 (open), with the same five samples along the normal.</summary>
    public static float Occlusion(Func<Vector3, float> sdf, Vector3 position, Vector3 normal)
    {
        var occlusion = 0f;
        var weight = 1f;
        for (var i = 1; i <= 5; i++)
        {
            var h = 0.03f * i;
            var d = sdf(position + normal * h);
            occlusion += (h - (float.IsFinite(d) ? d : h)) * weight;
            weight *= 0.85f;
        }
        return Math.Clamp(1 - 4 * occlusion, 0, 1);
    }
}
