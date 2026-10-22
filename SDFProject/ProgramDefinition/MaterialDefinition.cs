using System.Collections.Immutable;

namespace SDFProject.ProgramDefinition;

/// <summary>
/// How a surface looks, in renderer-independent physical terms that map onto Unity HDRP's Lit material: a dielectric with a base colour,
/// roughness and specular level, light scattering through it (subsurface), a wet coat, procedural detail in the shape's own coordinates,
/// a colour shift where the material is thin, large patches of a second colour, and small speckles. Lengths are in metres.
/// </summary>
public sealed record MaterialDefinition(
    V3 baseColor,
    float roughness,
    float specular,
    V3 subsurfaceColor,
    float subsurfaceRadius,
    float detailStrength,
    float detailScale,
    float colorVariation)
{
    /// <summary>0 dry to 1 soaked: a sharp glossy coat over the surface (HDRP's coat mask), darkening it a little.</summary>
    public float wetness { get; init; }

    /// <summary>1 for round grain; higher stretches the detail into fibres that run out from <see cref="detailAxis"/> and along it.</summary>
    public float detailStretch { get; init; } = 1;

    /// <summary>The axis fibres run out from, in the shape's own coordinates: (0, 1, 0) for a cap or stem, (0, 0, 1) for an iris.</summary>
    public V3 detailAxis { get; init; } = new(0, 1, 0);

    /// <summary>The colour the surface takes on where the material is thinner than <see cref="edgeThickness"/>.</summary>
    public V3 edgeColor { get; init; } = new(0, 0, 0);

    /// <summary>How thin the material must be to show <see cref="edgeColor"/>; 0 turns the edge colour off.</summary>
    public float edgeThickness { get; init; }

    /// <summary>The colour of large, soft patches the base colour drifts towards, like the uneven tones of a real mushroom.</summary>
    public V3 variationColor { get; init; } = new(0, 0, 0);

    /// <summary>How large the <see cref="variationColor"/> patches are; 0 turns them off.</summary>
    public float variationScale { get; init; }

    /// <summary>The colour of small flecks scattered over the surface, each a little lighter or darker than the next.</summary>
    public V3 speckleColor { get; init; } = new(0, 0, 0);

    /// <summary>How far apart the speckles are, in metres; 0 turns them off.</summary>
    public float speckleScale { get; init; }

    /// <summary>How many of the places a speckle could be have one, 0 none to 1 all.</summary>
    public float speckleDensity { get; init; }

    /// <summary>A speckle's radius as a share of <see cref="speckleScale"/>; sizes vary from half to one and a half times this.</summary>
    public float speckleSize { get; init; } = 0.3f;

    /// <summary>
    /// 0 to 1: how much the surface glows. A toon renderer adds this much of the base colour after its light bands, flat and unbanded, so
    /// a lit face keeps its colour in shadow; 0 (the default) adds nothing.
    /// </summary>
    public float emission { get; init; }

    /// <summary>What a shape without a colour or material looks like.</summary>
    public static readonly MaterialDefinition Default = new(new V3(0.85f, 0.62f, 0.42f), 0.5f, 0.5f, new V3(0, 0, 0), 0, 0, 0.05f, 0);

    /// <summary>Stands in for a material name that is not in the library, bright magenta so it cannot be missed.</summary>
    public static readonly MaterialDefinition Missing = Default with { baseColor = new V3(1, 0, 1), roughness = 0.3f };

    public static MaterialDefinition FromColor(V3 color) => Default with { baseColor = color };

    public static readonly IReadOnlyList<MaterialParameter> Parameters =
    [
        new(nameof(baseColor), MaterialParameterKind.Color, m => Components(m.baseColor), (m, v) => m with { baseColor = Color(v) }),
        new(nameof(roughness), MaterialParameterKind.Number, m => [m.roughness], (m, v) => m with { roughness = v[0] }),
        new(nameof(specular), MaterialParameterKind.Number, m => [m.specular], (m, v) => m with { specular = v[0] }),
        new(nameof(subsurfaceColor), MaterialParameterKind.Color, m => Components(m.subsurfaceColor), (m, v) => m with { subsurfaceColor = Color(v) }),
        new(nameof(subsurfaceRadius), MaterialParameterKind.Number, m => [m.subsurfaceRadius], (m, v) => m with { subsurfaceRadius = v[0] }),
        new(nameof(detailStrength), MaterialParameterKind.Number, m => [m.detailStrength], (m, v) => m with { detailStrength = v[0] }),
        new(nameof(detailScale), MaterialParameterKind.Number, m => [m.detailScale], (m, v) => m with { detailScale = v[0] }),
        new(nameof(colorVariation), MaterialParameterKind.Number, m => [m.colorVariation], (m, v) => m with { colorVariation = v[0] }),
        new(nameof(wetness), MaterialParameterKind.Number, m => [m.wetness], (m, v) => m with { wetness = v[0] }),
        new(nameof(detailStretch), MaterialParameterKind.Number, m => [m.detailStretch], (m, v) => m with { detailStretch = v[0] }),
        new(nameof(detailAxis), MaterialParameterKind.Direction, m => Components(m.detailAxis), (m, v) => m with { detailAxis = Color(v) }),
        new(nameof(edgeColor), MaterialParameterKind.Color, m => Components(m.edgeColor), (m, v) => m with { edgeColor = Color(v) }),
        new(nameof(edgeThickness), MaterialParameterKind.Number, m => [m.edgeThickness], (m, v) => m with { edgeThickness = v[0] }),
        new(nameof(variationColor), MaterialParameterKind.Color, m => Components(m.variationColor), (m, v) => m with { variationColor = Color(v) }),
        new(nameof(variationScale), MaterialParameterKind.Number, m => [m.variationScale], (m, v) => m with { variationScale = v[0] }),
        new(nameof(speckleColor), MaterialParameterKind.Color, m => Components(m.speckleColor), (m, v) => m with { speckleColor = Color(v) }),
        new(nameof(speckleScale), MaterialParameterKind.Number, m => [m.speckleScale], (m, v) => m with { speckleScale = v[0] }),
        new(nameof(speckleDensity), MaterialParameterKind.Number, m => [m.speckleDensity], (m, v) => m with { speckleDensity = v[0] }),
        new(nameof(speckleSize), MaterialParameterKind.Number, m => [m.speckleSize], (m, v) => m with { speckleSize = v[0] }),
        new(nameof(emission), MaterialParameterKind.Number, m => [m.emission], (m, v) => m with { emission = v[0] }),
    ];

    /// <summary>
    /// The parameters a file or a bake lists for the material: all of them, except emission when it is 0, so materials that do not glow
    /// are written as they were before it existed.
    /// </summary>
    public static IEnumerable<MaterialParameter> Listed(MaterialDefinition material) =>
        Parameters.Where(p => p.Name != nameof(emission) || material.emission != 0);

    public static MaterialParameter? FindParameter(string name) => Parameters.FirstOrDefault(p => p.Name == name);

    private static float[] Components(V3 v) => [v.x, v.y, v.z];

    private static V3 Color(float[] v) => new(v[0], v[1], v[2]);
}

public enum MaterialParameterKind
{
    Number,
    Color,
    Direction,
}

public sealed record MaterialParameter(
    string Name,
    MaterialParameterKind Kind,
    Func<MaterialDefinition, float[]> Get,
    Func<MaterialDefinition, float[], MaterialDefinition> Set)
{
    public int ComponentCount => Kind == MaterialParameterKind.Number ? 1 : 3;

    public MaterialDefinition With(MaterialDefinition material, float[] components) =>
        components.Length == ComponentCount
            ? Set(material, components)
            : throw new ArgumentException($"Material parameter '{Name}' has {ComponentCount} components, but {components.Length} were given");
}

/// <summary>The named materials models can refer to, sorted by name.</summary>
public sealed record MaterialLibrary(ImmutableSortedDictionary<string, MaterialDefinition> Materials)
{
    public static readonly MaterialLibrary Empty = new(ImmutableSortedDictionary<string, MaterialDefinition>.Empty.WithComparers(StringComparer.Ordinal));

    public IEnumerable<string> Names => Materials.Keys;

    public bool Contains(string name) => Materials.ContainsKey(name);

    public MaterialDefinition Resolve(string name) => Materials.GetValueOrDefault(name) ?? MaterialDefinition.Missing;

    public MaterialLibrary With(string name, MaterialDefinition material) => new(Materials.SetItem(name, material));

    public static MaterialLibrary Of(IEnumerable<KeyValuePair<string, MaterialDefinition>> materials) =>
        new(materials.ToImmutableSortedDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));
}
