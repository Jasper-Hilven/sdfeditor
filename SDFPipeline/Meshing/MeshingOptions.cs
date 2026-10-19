using SDFProject.ProgramDefinition;

namespace SDFPipeline.Meshing;

public sealed record MeshingOptions
{
    public const int DefaultResolution = 128;
    public const int MinResolution = 16;
    public const int MaxResolution = 1024;
    public const float DefaultReferenceSize = 2;
    public const float DefaultSizeExponent = 0.5f;
    public const float DefaultCreaseAngleDegrees = 30;
    public const float DefaultMaxErrorInCells = 0.08f;
    public const float DefaultMaxThickness = 0.25f;

    /// <summary>
    /// Grid cells along the longest side of something <see cref="ReferenceSize"/> metres long. The model, and each part, which is
    /// meshed on a grid of its own, gets cells by its own size: see <see cref="CellsAlong"/>.
    /// </summary>
    public int Resolution { get; init; } = DefaultResolution;

    /// <summary>The size in metres at which the longest side gets <see cref="Resolution"/> cells.</summary>
    public float ReferenceSize { get; init; } = DefaultReferenceSize;

    /// <summary>
    /// How the number of cells follows size: 1 gives everything the same cell size, so triangles go with surface area; 0 gives
    /// everything <see cref="Resolution"/> cells whatever its size. In between, smaller things get fewer cells but finer ones, so a
    /// small part like an eye keeps its pupil without costing as much as the body.
    /// </summary>
    public float SizeExponent { get; init; } = DefaultSizeExponent;

    /// <summary>Grid cells along a longest side this many metres long, at least <see cref="MinResolution"/> (or the resolution if lower).</summary>
    public int CellsAlong(float longestSide)
    {
        var cells = Resolution * MathF.Pow(MathF.Max(longestSide, 1e-4f) / ReferenceSize, SizeExponent);
        return (int)MathF.Ceiling(Math.Clamp(cells, Math.Min(MinResolution, Resolution), MaxResolution));
    }

    /// <summary>Where neighbouring faces meet at more than this, the normals are hard and simplification keeps the crease.</summary>
    public float CreaseAngleDegrees { get; init; } = DefaultCreaseAngleDegrees;

    /// <summary>
    /// How far simplification may move the surface, as a fraction of a grid cell: the average distance of a merged vertex from the
    /// planes of the faces it replaces. 0 turns simplification off.
    /// </summary>
    public float MaxErrorInCells { get; init; } = DefaultMaxErrorInCells;

    /// <summary>Thickness is measured up to this many metres; thicker material lets no light through anyway.</summary>
    public float MaxThickness { get; init; } = DefaultMaxThickness;

    /// <summary>The materials the program's Material nodes refer to; unknown names become <see cref="MaterialDefinition.Missing"/>.</summary>
    public MaterialLibrary Materials { get; init; } = MaterialLibrary.Empty;

    public static readonly MeshingOptions Default = new();
}
