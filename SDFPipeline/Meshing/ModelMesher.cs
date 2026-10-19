using System.Diagnostics;
using System.Numerics;
using SDFPipeline.Rendering;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Meshing;

public sealed record MeshStatistics(int ContouredTriangles, int SimplifiedTriangles, int Vertices, TimeSpan Time);

/// <summary>
/// One mesh of a model, in metres with +Y up and the front towards +Z. Every per-vertex array has one entry per position; triangle i is
/// Triangles[3i..3i+2], counter-clockwise seen from outside.
/// </summary>
/// <param name="Materials">The material mix at each vertex, from the SDF.</param>
/// <param name="Thickness">Metres of material straight in from each vertex, up to <see cref="MeshingOptions.MaxThickness"/>.</param>
/// <param name="Occlusion">The renderer's ambient occlusion at each vertex, 0 closed in to 1 open, measured against the whole model.</param>
public sealed record PartMesh(
    string Name,
    Vector3[] Positions,
    Vector3[] Normals,
    VertexMaterial[] Materials,
    float[] Thickness,
    float[] Occlusion,
    int[] Triangles,
    MeshStatistics Statistics)
{
    public int TriangleCount => Triangles.Length / 3;
}

/// <summary>A model as meshes: the body and one per part, sharing one material table.</summary>
public sealed record MeshedModel(IReadOnlyList<PartMesh> Meshes, IReadOnlyList<MeshMaterial> Materials, ContouringGrid? Grid)
{
    public static readonly MeshedModel Empty = new([], [], null);
}

/// <summary>
/// Turns a model into meshes for a game engine: dual contouring on a grid around the whole model (and one around each part), material borders lined up with the SDF's, simplification that keeps creases
/// and material borders, hard normals on creases, and per-vertex material, thickness and occlusion computed from the SDF so the mesh
/// can be shaded like the raymarched preview.
/// </summary>
public static class ModelMesher
{
    public static MeshedModel Mesh(SDFProgram program, MeshingOptions? options = null)
    {
        options ??= MeshingOptions.Default;
        var bounds = RenderFramer.ModelBounds(program);
        if (bounds is null)
            return MeshedModel.Empty;

        var grid = ContouringGrid.Around(bounds, options.CellsAlong(bounds.LongestSide));
        var calculator = new SdfCalculatorCpu();
        var whole = calculator.Prepare(PartSplitter.Expanded(program));
        var table = new MaterialTable(options.Materials);
        var meshes = new List<PartMesh>();
        foreach (var source in PartSplitter.Split(program))
        {
            if (MeshOne(source, calculator, whole, GridFor(source, grid, options), table, options) is { } mesh)
                meshes.Add(mesh);
        }
        return new MeshedModel(meshes, table.Materials, grid);
    }

    /// <summary>
    /// The body is meshed on the model's grid. A part, being a mesh of its own that overlaps the body rather than joining it, gets a grid
    /// by its own size, so small parts like eyes get fewer triangles than the body but finer cells: on the model's grid a pupil would be
    /// a few cells wide and come out a triangle.
    /// </summary>
    private static ContouringGrid GridFor(MeshSource source, ContouringGrid modelGrid, MeshingOptions options) =>
        source.Name == PartSplitter.BodyName || RenderFramer.ModelBounds(source.Program) is not { } bounds
            ? modelGrid
            : ContouringGrid.Around(bounds, options.CellsAlong(bounds.LongestSide));

    private static PartMesh? MeshOne(MeshSource source, SdfCalculatorCpu calculator, SdfCalculatorCpu.Prepared whole, ContouringGrid grid,
        MaterialTable table, MeshingOptions options)
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
        var simplified = QuadricSimplifier.Simplify(contoured, shape.Distance, options.MaxErrorInCells * cellSize, cellSize, options.CreaseAngleDegrees, regions);
        var shaded = CreaseNormals.Split(simplified, shape.Distance, cellSize, options.CreaseAngleDegrees);

        var samples = Evaluate(simplified.Positions, shape.Surface);
        var materialOfSource = samples.Select(sample => SurfaceAttributes.Material(sample, table)).ToArray();
        var materials = shaded.Source.Select(v => materialOfSource[v]).ToArray();

        var thickness = new float[shaded.Positions.Length];
        var occlusion = new float[shaded.Positions.Length];
        Parallel.For(0, shaded.Positions.Length, v =>
        {
            thickness[v] = SurfaceAttributes.Thickness(whole.Distance, shaded.Positions[v], shaded.Normals[v], options.MaxThickness, cellSize);
            occlusion[v] = SurfaceAttributes.Occlusion(whole.Distance, shaded.Positions[v], shaded.Normals[v]);
        });

        var statistics = new MeshStatistics(contoured.Mesh.TriangleCount, simplified.TriangleCount, shaded.Positions.Length, stopwatch.Elapsed);
        return new PartMesh(source.Name, shaded.Positions, shaded.Normals, materials, thickness, occlusion, shaded.Triangles, statistics);
    }

    private static MaterialSource Dominant(SurfaceSample sample) => sample.Materials.MaxBy(s => s.Weight).Source;

    private static T[] Evaluate<T>(Vector3[] positions, Func<Vector3, T> evaluate)
    {
        var results = new T[positions.Length];
        Parallel.For(0, positions.Length, i => results[i] = evaluate(positions[i]));
        return results;
    }
}
