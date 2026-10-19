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
    public const int DefaultLodCount = 1;
    public const int MaxLodCount = 4;

    /// <summary>The error limit of LOD1, in cells: six times LOD0's, which takes away about three quarters of its triangles.</summary>
    public const float FirstLodErrorInCells = 0.5f;

    /// <summary>
    /// How much the error limit grows from one level of detail to the next. A curved surface needs triangles in proportion to one over
    /// the error, so each level has about a third of the triangles of the one before; flat and sharp-edged shapes, which need few
    /// triangles anyway, lose less.
    /// </summary>
    public const float LodErrorGrowth = 3;

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

    /// <summary>
    /// Grid cells along a longest side this many metres long, at least <see cref="MinResolution"/> (or the resolution if lower).
    /// </summary>
    /// <param name="detail">
    /// The <see cref="Detail"/> of the mesh: how many times the vertices its size gives it. The limits apply after it, so no detail
    /// takes a mesh below the cells it needs to keep its shape, or above the grid memory allows.
    /// </param>
    public int CellsAlong(float longestSide, float detail = 1)
    {
        var cells = Resolution * MathF.Pow(MathF.Max(longestSide, 1e-4f) / ReferenceSize, SizeExponent) * CellsForDetail(detail);
        return (int)MathF.Ceiling(Math.Clamp(cells, Math.Min(MinResolution, Resolution), MaxResolution));
    }

    /// <summary>
    /// How many times the cells along a side give a mesh <paramref name="detail"/> times the vertices: as many times, not the square
    /// root. The contour's vertices grow with the square of the cells, but simplification takes them down to what the error limit
    /// needs, and that limit is in cells: a curved surface needs vertices in proportion to one over the error, so the simplified mesh's
    /// vertices grow with the cells themselves, at every level of detail. Shapes curved one way only, like thin branches, gain fewer
    /// vertices per added cell, so they lose a little less than the detail asks.
    /// </summary>
    public static float CellsForDetail(float detail) => detail;

    /// <summary>Where neighbouring faces meet at more than this, the normals are hard and simplification keeps the crease.</summary>
    public float CreaseAngleDegrees { get; init; } = DefaultCreaseAngleDegrees;

    /// <summary>
    /// How far simplification may move the surface, as a fraction of a grid cell: the average distance of a merged vertex from the
    /// planes of the faces it replaces. 0 turns simplification off.
    /// </summary>
    public float MaxErrorInCells { get; init; } = DefaultMaxErrorInCells;

    /// <summary>
    /// How many levels of detail each mesh gets, LOD0 included: 1 makes only LOD0, the mesh as simplified by <see cref="MaxErrorInCells"/>.
    /// Each further level is simplified on from the one before with a larger error limit (see <see cref="LodErrorInCells"/>), on the same
    /// grid: a coarser grid would be cheaper to contour, but loses thin features and material borders, and opens holes where the
    /// surface is thinner than its cells.
    /// </summary>
    public int LodCount { get; init; } = DefaultLodCount;

    /// <summary>The error limit of a level of detail, in cells of its mesh's grid: <see cref="MaxErrorInCells"/> for LOD0, then growing.</summary>
    public float LodErrorInCells(int level) =>
        level == 0 ? MaxErrorInCells : FirstLodErrorInCells * MathF.Pow(LodErrorGrowth, level - 1);

    /// <summary>Thickness is measured up to this many metres; thicker material lets no light through anyway.</summary>
    public float MaxThickness { get; init; } = DefaultMaxThickness;

    /// <summary>The materials the program's Material nodes refer to; unknown names become <see cref="MaterialDefinition.Missing"/>.</summary>
    public MaterialLibrary Materials { get; init; } = MaterialLibrary.Empty;

    public static readonly MeshingOptions Default = new();
}
