namespace SDFPipeline.Materials;

/// <summary>The patterns the game engine's toon look draws on a surface, numbered as its shader (SdfPattern.hlsl) numbers them.</summary>
public enum MaterialPatternKind
{
    /// <summary>Courses of blocks in running bond with grout between them; flagstones on faces that look up.</summary>
    Stones = 1,

    /// <summary>Fine fibres running up a stem, along the part's y axis.</summary>
    Fibres = 2,

    /// <summary>Gill lines running out from the part's y axis, as under a cap.</summary>
    Gills = 3,
}

/// <summary>
/// Detail a material's surface shows in the game engine without costing vertices (ticket 06): drawn per pixel by the toon shader in
/// the part's own space, so it stays on the part and repeats on every use of it, and faded to the material's mean colour with distance
/// before its lines would shimmer. A line of the material file, after the parameters:
/// <code>pattern stones size=0.55 grout=0.035</code>
/// The preview and <c>sdf render</c> do not draw patterns: they are not a parameter of <see cref="SDFProject.ProgramDefinition.MaterialDefinition"/>,
/// so materials and meshes without one stay exactly as they were.
/// </summary>
/// <param name="Size">The pattern's scale in metres: a block's length, or the spacing of fibres or gills.</param>
/// <param name="Grout">The width of the lines between, in metres, drawn in ink like the outline.</param>
public sealed record MaterialPattern(MaterialPatternKind Kind, float Size, float Grout)
{
    public const string Keyword = "pattern";

    /// <summary>The names the material file writes the kinds as.</summary>
    public static string NameOf(MaterialPatternKind kind) => kind.ToString().ToLowerInvariant();
}
