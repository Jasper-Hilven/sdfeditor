using System.Diagnostics;
using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Rendering;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Meshing;

/// <param name="Time">How long the mesh took; for a level of detail, how long it took on top of the level before.</param>
public sealed record MeshStatistics(int ContouredTriangles, int SimplifiedTriangles, int Vertices, TimeSpan Time);

/// <summary>
/// One mesh of a model, in metres with +Y up and the front towards +Z. Every per-vertex array has one entry per position; triangle i is
/// Triangles[3i..3i+2], counter-clockwise seen from outside.
/// </summary>
/// <param name="Materials">The material mix at each vertex, from the SDF.</param>
/// <param name="Thickness">Metres of material straight in from each vertex, up to <see cref="MeshingOptions.MaxThickness"/>.</param>
/// <param name="Occlusion">The renderer's ambient occlusion at each vertex, 0 closed in to 1 open, measured against the whole model.</param>
/// <param name="MaxError">How far simplification may have moved the surface, in metres.</param>
public sealed record PartMesh(
    string Name,
    Vector3[] Positions,
    Vector3[] Normals,
    VertexMaterial[] Materials,
    float[] Thickness,
    float[] Occlusion,
    int[] Triangles,
    float MaxError,
    MeshStatistics Statistics)
{
    public int TriangleCount => Triangles.Length / 3;

    /// <summary>The detail this mesh was made with, see <see cref="MeshSource.Detail"/>.</summary>
    public float Detail { get; init; } = 1;

    /// <summary>The part this mesh is of, or null for the body; see <see cref="MeshSource.Part"/>.</summary>
    public string? Part { get; init; }

    /// <summary>
    /// For a part meshed once in its own space, where each use puts it (see <see cref="MeshSource.Instances"/>); empty for a mesh in the
    /// model's space. Only LOD0 has them: the levels are drawn where it is.
    /// </summary>
    public IReadOnlyList<Matrix4x4> Instances { get; init; } = [];

    /// <summary>
    /// The cheaper levels of detail of this mesh, LOD1 first, each with its own vertices and per-vertex data; empty when
    /// <see cref="MeshingOptions.LodCount"/> is 1. This mesh is LOD0.
    /// </summary>
    public IReadOnlyList<PartMesh> Lods { get; init; } = [];
}

/// <summary>A model as meshes: the body and one per part, sharing one material table.</summary>
public sealed record MeshedModel(IReadOnlyList<PartMesh> Meshes, IReadOnlyList<MeshMaterial> Materials, ContouringGrid? Grid)
{
    public static readonly MeshedModel Empty = new([], [], null);

    /// <summary>The detail the model sets for all of its meshes, see <see cref="MeshSource.Detail"/>; parts may multiply it with their own.</summary>
    public float Detail { get; init; } = 1;

    /// <summary>The model's own settings from its file, such as its collider, which the .glb's extras hand on to the game engine.</summary>
    public ModelSettings Settings { get; init; } = ModelSettings.Default;

    /// <summary>
    /// The settings of the parts whose files have settings lines, by part name, each read over the model's own (see
    /// <see cref="ModelSettings.Read(string, ModelSettings)"/>); the other parts take the model's settings.
    /// </summary>
    public IReadOnlyDictionary<string, ModelSettings> PartSettings { get; init; } = new Dictionary<string, ModelSettings>();

    /// <summary>What the mesher could not do as the model asks, such as a repeated part it had to mesh in place, one line each.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Turns a model into meshes for a game engine: dual contouring on a grid around the whole model (and one around each part), material borders lined up with the SDF's, simplification that keeps creases
/// and material borders, hard normals on creases, and per-vertex material, thickness and occlusion computed from the SDF so the mesh
/// can be shaded like the raymarched preview. Levels of detail are simplified on from the same contour, each with per-vertex data of its own.
/// </summary>
public static class ModelMesher
{
    public static MeshedModel Mesh(SDFProgram program, MeshingOptions? options = null)
    {
        options ??= MeshingOptions.Default;
        var bounds = RenderFramer.ModelBounds(program);
        if (bounds is null)
            return MeshedModel.Empty;

        var detail = PartSplitter.ModelDetail(program);
        var grid = ContouringGrid.Around(bounds, options.CellsAlong(bounds.LongestSide, detail));
        var calculator = new SdfCalculatorCpu();
        var whole = calculator.Prepare(PartSplitter.Expanded(program));
        var surface = options.SurfaceOf is { } cutFrom ? calculator.Prepare(PartSplitter.Expanded(cutFrom)) : null;
        var table = new MaterialTable(options.Materials);
        var meshes = new List<PartMesh>();
        var warnings = new List<string>();
        foreach (var source in PartSplitter.Split(program, warnings))
        {
            if (MeshOne(source, calculator, whole, surface, GridFor(source, grid, options), table, options) is { } mesh)
                meshes.Add(mesh);
        }
        return new MeshedModel(meshes, table.Materials, grid) { Detail = detail, Warnings = warnings };
    }

    /// <summary>
    /// The body is meshed on the model's grid. A part, being a mesh of its own that overlaps the body rather than joining it, gets a grid
    /// by its own size, so small parts like eyes get fewer triangles than the body but finer cells: on the model's grid a pupil would be
    /// a few cells wide and come out a triangle. Both have the cells of their <see cref="MeshSource.Detail"/>; the error limits are in
    /// cells, so every level of detail follows it without being told.
    /// </summary>
    private static ContouringGrid GridFor(MeshSource source, ContouringGrid modelGrid, MeshingOptions options) =>
        source.Name == PartSplitter.BodyName || RenderFramer.ModelBounds(source.Program) is not { } bounds
            ? modelGrid
            : ContouringGrid.Around(bounds, options.CellsAlong(bounds.LongestSide, source.Detail));

    private static PartMesh? MeshOne(MeshSource source, SdfCalculatorCpu calculator, SdfCalculatorCpu.Prepared whole,
        SdfCalculatorCpu.Prepared? surface, ContouringGrid grid, MaterialTable table, MeshingOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var shape = calculator.Prepare(source.Program);
        var contoured = DualContouring.Contour(shape.Distance, grid);
        if (contoured.Mesh.TriangleCount == 0)
            return null;

        // Materials are evaluated in parallel but numbered in vertex order, so the table comes out the same every time
        var cellSize = grid.CellSize;
        var regions = Evaluate(contoured.Mesh.Positions, p => Dominant(shape.Surface(p))).Select(table.IndexOf).ToArray();
        contoured = MaterialBorders.Straightened(contoured, regions, p =>
        {
            var source = Dominant(shape.Surface(p));
            lock (table) return table.IndexOf(source);
        }, shape.Distance, cellSize);
        var maxErrors = Enumerable.Range(0, Math.Max(options.LodCount, 1)).Select(level => options.LodErrorInCells(level) * cellSize).ToArray();
        var levels = new List<PartMesh>();
        foreach (var simplified in QuadricSimplifier.SimplifyInSteps(contoured, shape.Distance, maxErrors, cellSize, options.CreaseAngleDegrees, regions))
        {
            var level = levels.Count;
            var shaded = CreaseNormals.Split(simplified, shape.Distance, cellSize, options.CreaseAngleDegrees);

            // A coarse level's vertices may sit off the surface by up to its error limit, outside on round shapes: their material,
            // thickness and occlusion are those of the nearest point of the surface, as LOD0's are of the surface at its vertices
            var onSurface = level == 0
                ? simplified.Positions
                : Evaluate(simplified.Positions, p => MaterialBorders.OntoSurface(p, shape.Distance, cellSize * 1e-3f));
            var samples = Evaluate(onSurface, shape.Surface);
            InRegions(onSurface, samples, simplified.Sources!, regions, contoured.Mesh.Positions, shape, table, cellSize);
            var materialOfSource = samples.Select(sample => SurfaceAttributes.Material(sample, table)).ToArray();
            var materials = shaded.Source.Select(v => materialOfSource[v]).ToArray();

            // A part meshed once for all of its uses is measured against the model where its first use puts it, and its thickness
            // given back in its own metres
            var thickness = new float[shaded.Positions.Length];
            var occlusion = new float[shaded.Positions.Length];
            var placement = source.Instances.Count > 0 ? source.Instances[0] : (Matrix4x4?)null;
            var scale = placement is { } m ? new Vector3(m.M11, m.M12, m.M13).Length() : 1;
            Parallel.For(0, shaded.Positions.Length, v =>
            {
                var position = onSurface[shaded.Source[v]];
                var normal = shaded.Normals[v];
                var size = 1f;
                if (placement is { } placed)
                {
                    position = Vector3.Transform(position, placed);
                    normal = Vector3.Normalize(Vector3.TransformNormal(normal, placed));
                    size = scale;
                }
                // A piece's surface where it is the shape it was cut from is measured on that shape (see MeshingOptions.SurfaceOf)
                var measured = surface is not null && MathF.Abs(surface.Distance(position)) <= cellSize * size * 0.5f ? surface : whole;
                thickness[v] = SurfaceAttributes.Thickness(measured.Distance, position, normal, options.MaxThickness * size, cellSize * size) / size;
                occlusion[v] = SurfaceAttributes.Occlusion(measured.Distance, position, normal);
            });

            var statistics = new MeshStatistics(contoured.Mesh.TriangleCount, simplified.TriangleCount, shaded.Positions.Length, stopwatch.Elapsed);
            levels.Add(new PartMesh(source.Name, shaded.Positions, shaded.Normals, materials, thickness, occlusion, shaded.Triangles, maxErrors[level], statistics)
            {
                Detail = source.Detail,
                Part = source.Part,
            });
            stopwatch.Restart();
        }
        return levels[0] with { Lods = levels.Skip(1).ToArray(), Instances = source.Instances };
    }

    /// <summary>
    /// Gives each vertex the material of its region. Simplification keeps regions apart, but a merged vertex may come to stand where
    /// another material is, such as a cap vertex in the corner under the cap where the stem goes in: sampled there, it would take the
    /// stem's colour, and the faces it shares with the cap would run from white to red across the whole cap. Such a vertex is sampled
    /// instead at the nearest point of its own region on the way back to where contouring put it, which is in its region. Only the
    /// material is sampled there: the vertex stays where it is.
    /// </summary>
    private static void InRegions(Vector3[] positions, SurfaceSample[] samples, int[] sources, int[] regions, Vector3[] contouredPositions,
        SdfCalculatorCpu.Prepared shape, MaterialTable table, float cellSize)
    {
        int RegionAt(Vector3 p)
        {
            var source = Dominant(shape.Surface(p));
            lock (table) return table.IndexOf(source);
        }

        Parallel.For(0, positions.Length, v =>
        {
            var region = regions[sources[v]];
            int sampled;
            lock (table) sampled = table.IndexOf(Dominant(samples[v]));
            if (sampled == region)
                return;
            float inside = 1, outside = 0;
            var (from, to) = (positions[v], contouredPositions[sources[v]]);
            for (var step = 0; step < RegionBisectionSteps; step++)
            {
                var middle = (inside + outside) * 0.5f;
                if (RegionAt(MaterialBorders.OntoSurface(Vector3.Lerp(from, to, middle), shape.Distance, cellSize * 1e-3f)) == region)
                    inside = middle;
                else
                    outside = middle;
            }
            var found = MaterialBorders.OntoSurface(Vector3.Lerp(from, to, inside), shape.Distance, cellSize * 1e-3f);
            if (RegionAt(found) == region)
                samples[v] = shape.Surface(found);
        });
    }

    private const int RegionBisectionSteps = 10;

    private static MaterialSource Dominant(SurfaceSample sample) => sample.Materials.MaxBy(s => s.Weight).Source;

    private static T[] Evaluate<T>(Vector3[] positions, Func<Vector3, T> evaluate)
    {
        var results = new T[positions.Length];
        Parallel.For(0, positions.Length, i => results[i] = evaluate(positions[i]));
        return results;
    }
}
