using System.Diagnostics;
using System.Numerics;
using SDFPipeline.Gpu;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Rendering;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Meshing;

/// <param name="Time">How long the mesh took; for a level of detail, how long it took on top of the level before.</param>
/// <param name="TrackedError">With batched simplification, the furthest any vertex folded away is from the surface that replaced it, in metres.</param>
public sealed record MeshStatistics(int ContouredTriangles, int SimplifiedTriangles, int Vertices, TimeSpan Time, float TrackedError = 0, SimplifyTimings? Timings = null)
{
    /// <summary>How long dual contouring took (level 0 only; the sampling of the SDF is nearly all of it).</summary>
    public TimeSpan ContourTime { get; init; }
}


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
        var warnings = new List<string>();
        var sources = PartSplitter.Split(program, warnings).ToArray();

        // The meshes are made at the same time, as simplifying one runs on a single core. Each numbers its materials in a table of its
        // own; they are merged in mesh order afterwards, which numbers them as meshing one after the other would
        var tables = sources.Select(_ => new MaterialTable(options.Materials, options.Patterns)).ToArray();
        var made = new PartMesh?[sources.Length];
        Parallel.For(0, sources.Length, new ParallelOptions { MaxDegreeOfParallelism = MeshesAtOnce }, s =>
            made[s] = MeshOne(sources[s], calculator, whole, surface, GridFor(sources[s], grid, options), tables[s], options));
        var table = new MaterialTable(options.Materials, options.Patterns);
        var meshes = new List<PartMesh>();
        for (var s = 0; s < sources.Length; s++)
        {
            var numbers = tables[s].Materials.Select(m => table.IndexOf(m.Source)).ToArray();
            if (made[s] is { } mesh)
                meshes.Add(Renumbered(mesh, numbers) with { Lods = mesh.Lods.Select(lod => Renumbered(lod, numbers)).ToArray() });
        }
        return new MeshedModel(meshes, table.Materials, grid) { Detail = detail, Warnings = warnings };
    }

    /// <summary>
    /// How many of a model's meshes are made at the same time: each holds its contour and simplification in memory, and its own
    /// contouring is already parallel.
    /// </summary>
    private const int MeshesAtOnce = 4;

    /// <summary>The mesh with its vertices' material numbers changed from its own table's to the model's.</summary>
    private static PartMesh Renumbered(PartMesh mesh, int[] numbers) => mesh with
    {
        Materials = mesh.Materials.Select(m => m with { First = numbers[m.First], Second = numbers[m.Second] }).ToArray(),
    };

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

    /// <summary>
    /// How many CPU-seconds contouring and simplifying will spend on SDF samples: about 500 per cell along the longest side of the grid,
    /// squared (the surface's cells, and per vertex the samples of contouring and of the simplifier's checks), each as long as the CPU
    /// takes for one evaluation, measured here on points spread through the grid.
    /// </summary>
    private static float PredictedCpuSeconds(Func<Vector3, float> sdf, ContouringGrid grid)
    {
        var longest = Math.Max(grid.Cells.X, Math.Max(grid.Cells.Y, grid.Cells.Z));
        const int warmUp = 20, samples = 300;
        var random = new Random(1);
        var size = grid.Max - grid.Origin;
        Vector3 Point() => grid.Origin + size * new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle());
        for (var i = 0; i < warmUp; i++)
            sdf(Point());
        var points = Enumerable.Range(0, samples).Select(_ => Point()).ToArray();
        var started = Stopwatch.GetTimestamp();
        foreach (var point in points)
            sdf(point);
        var microseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000 / samples;
        var predicted = (float)(500.0 * longest * longest * microseconds * 1e-6);
        if (Environment.GetEnvironmentVariable("SDF_GPU_TRACE") is { Length: > 0 })
            Console.Error.WriteLine($"CPU evaluation: {microseconds:0.0} us, predicted {predicted:0.0} CPU-seconds");
        return predicted;
    }

    private static PartMesh? MeshOne(MeshSource source, SdfCalculatorCpu calculator, SdfCalculatorCpu.Prepared whole,
        SdfCalculatorCpu.Prepared? surface, ContouringGrid grid, MaterialTable table, MeshingOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var shape = calculator.Prepare(source.Program);
        string? gpuProblem = null;
        var gpu = options.UseGpu && PredictedCpuSeconds(shape.Distance, grid) >= options.GpuMinPredictedCpuSeconds
            ? GpuSdf.TryCreate(source.Program, options.Materials, out gpuProblem)
            : null;
        if (options.UseGpu && gpu is null && gpuProblem is not null)
            GpuNotes.Once($"{source.Name}: meshed on the CPU, the GPU could not take it: {gpuProblem}");
        var contoured = DualContouring.Contour(shape.Distance, grid, gpu);
        var contourTime = stopwatch.Elapsed;
        if (contoured.Mesh.TriangleCount == 0)
            return null;

        // Materials are evaluated in parallel but numbered in vertex order, so the table comes out the same every time
        var cellSize = grid.CellSize;
        var regions = Evaluate(contoured.Mesh.Positions, p => Dominant(shape.Surface(p))).Select(table.IndexOf).ToArray();
        int RegionAt(Vector3 p)
        {
            var dominant = Dominant(shape.Surface(p));
            lock (table) return table.IndexOf(dominant);
        }
        contoured = MaterialBorders.Straightened(contoured, regions, RegionAt, shape.Distance, cellSize);
        // The faces across material borders are cut along the SDF's border, so each face is of one material and the border runs along
        // edges, through points where it crosses the contour's edges and where three materials meet; simplification keeps it so. The
        // contour's edges are within a fraction of a cell of the surface, so the material is sampled on them as they are
        var cut = BorderCut.Cut(contoured.Mesh, regions, RegionAt);
        contoured = WithBorderPoints(contoured, cut, shape.Distance, cellSize);
        var vertexRegions = regions.Concat(Enumerable.Repeat(-1, contoured.Mesh.Positions.Length - regions.Length)).ToArray();

        var maxErrors = Enumerable.Range(0, Math.Max(options.LodCount, 1)).Select(level => options.LodErrorInCells(level) * cellSize).ToArray();
        var levels = new List<PartMesh>();
        // How far each vertex has blended into a second material: a fold that changes it much is dearer (batched simplification). A
        // border point, where either material may be the stronger, takes the least of the vertices it lies between
        var batched = options.Simplifier == Simplifier.Batched;
        var blends = batched ? Evaluate(contoured.Mesh.Positions[..regions.Length], p => Blend(shape.Surface(p))) : null;
        if (blends is not null)
            blends = blends.Concat(cut.Parents.Select(parents => parents.Min(v => blends[v]))).ToArray();
        // Where the model asks to keep its density (a rigged model's joints), the contour's vertices stay
        var kept = options.KeepDensity.Count > 0 && source.Instances.Count == 0
            ? contoured.Mesh.Positions.Select(p => options.KeepDensity.Any(region => region.Contains(p))).ToArray()
            : null;
        foreach (var simplified in QuadricSimplifier.SimplifyInSteps(contoured, shape.Distance, maxErrors, cellSize, options.CreaseAngleDegrees,
                     null, batched, blends, gpu, kept, cut.FaceRegions))
        {
            var level = levels.Count;
            // A coarse level's vertices may sit off the surface by up to its error limit, outside on round shapes: their material,
            // thickness and occlusion are those of the nearest point of the surface, as LOD0's are of the surface at its vertices
            var onSurface = level == 0
                ? simplified.Positions
                : Evaluate(simplified.Positions, p => MaterialBorders.OntoSurface(p, shape.Distance, cellSize * 1e-3f));
            var samples = Evaluate(onSurface, shape.Surface);
            InRegions(onSurface, samples, simplified.Sources!, vertexRegions, contoured.Mesh.Positions, shape, table, cellSize);
            var materialOfSource = samples.Select(sample => SurfaceAttributes.Material(sample, table)).ToArray();

            // The border points are split, one copy per side, and each copy takes the material just inside its own side
            var (shaded, shadedRegions) = BorderCut.SplitByRegion(
                CreaseNormals.Split(simplified, shape.Distance, cellSize, options.CreaseAngleDegrees), simplified.FaceRegions!);
            var facesOf = new List<int>[shaded.Positions.Length];
            for (var t = 0; t < shaded.Triangles.Length; t++)
                if (cut.IsBorderPoint(simplified.Sources![shaded.Source[shaded.Triangles[t]]]))
                    (facesOf[shaded.Triangles[t]] ??= []).Add(t / 3);
            var borderSamples = new SurfaceSample?[shaded.Positions.Length];
            var insidePoints = new Vector3[shaded.Positions.Length];
            Parallel.For(0, shaded.Positions.Length, v =>
            {
                if (facesOf[v] is { } faces)
                    (borderSamples[v], insidePoints[v]) = InsideItsSide(shaded, faces, v, shadedRegions[v], onSurface, shape, RegionAt, cellSize);
            });
            OneSidedNormals(shaded, facesOf, insidePoints, shape.Distance, cellSize, options.CreaseAngleDegrees);
            var materials = shaded.Source.Select((from, v) =>
                OwnFirst(borderSamples[v] is { } sample ? SurfaceAttributes.Material(sample, table) : materialOfSource[from], shadedRegions[v])).ToArray();
            OfItsSide(materials, shaded.Triangles, shadedRegions);

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

            var statistics = new MeshStatistics(contoured.Mesh.TriangleCount, shaded.TriangleCount, shaded.Positions.Length, stopwatch.Elapsed, simplified.TrackedError, simplified.Timings) { ContourTime = level == 0 ? contourTime : default };
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
            if (region < 0)
                return; // a border point, sampled on each side of the border instead
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

    /// <summary>
    /// The contour as cut along its borders, each border point with the SDF's tangent plane there as its Hermite data. The points stay
    /// where the cut put them, on the contour's own edges and faces (within a fraction of a cell of the surface): moved onto the
    /// surface, a point next to a crease could turn the thin faces beside it over.
    /// </summary>
    private static ContouredMesh WithBorderPoints(ContouredMesh contoured, CutMesh cut, Func<Vector3, float> sdf, float cellSize)
    {
        var positions = cut.Mesh.Positions;
        var planes = new SurfacePlane[positions.Length][];
        Array.Copy(contoured.Planes, planes, cut.OriginalCount);
        Parallel.For(cut.OriginalCount, positions.Length, v =>
        {
            var onSurface = MaterialBorders.OntoSurface(positions[v], sdf, cellSize * 1e-3f);
            var normal = DualContouring.Normal(sdf, onSurface, cellSize);
            planes[v] = normal.LengthSquared() > 0 ? [new SurfacePlane(onSurface, Vector3.Normalize(normal))] : [];
        });
        return new ContouredMesh(cut.Mesh, planes);
    }

    /// <summary>How far into its faces a border point's copy is sampled, as shares of the way to their middle, nearest first.</summary>
    private static readonly float[] InsideSteps = [0.05f, 0.1f, 0.2f, 0.4f, 0.7f];

    /// <summary>
    /// The surface sampled for a copy of a border point: it stands exactly on the border, where either material may be the stronger,
    /// so it is sampled a little way into its own faces (all of its region), at the first step where its region is the stronger.
    /// </summary>
    private static (SurfaceSample Sample, Vector3 Point) InsideItsSide(ShadedMesh shaded, List<int> faces, int vertex, int region, Vector3[] onSurface,
        SdfCalculatorCpu.Prepared shape, Func<Vector3, int> regionAt, float cellSize)
    {
        var at = onSurface[shaded.Source[vertex]];
        var middle = Vector3.Zero;
        foreach (var face in faces)
            for (var c = 0; c < 3; c++)
                middle += onSurface[shaded.Source[shaded.Triangles[3 * face + c]]];
        middle /= 3 * faces.Count;
        foreach (var step in InsideSteps)
        {
            var sampled = MaterialBorders.OntoSurface(Vector3.Lerp(at, middle, step), shape.Distance, cellSize * 1e-3f);
            if (regionAt(sampled) == region)
                return (shape.Surface(sampled), sampled);
        }
        // Its faces are thin, or the border bends away inside them: the nearest of their corners that is of its region
        var nearest = faces.SelectMany(face => Enumerable.Range(0, 3).Select(c => onSurface[shaded.Source[shaded.Triangles[3 * face + c]]]))
            .Where(p => p != at).OrderBy(p => Vector3.DistanceSquared(p, at)).FirstOrDefault(p => regionAt(p) == region, at);
        return (shape.Surface(nearest), nearest);
    }

    /// <summary>
    /// The normals of the copies of each border point, from the SDF just inside each copy's own side (where its material is sampled).
    /// The cut splits faces in their own planes, so where two shapes meet in a fold (an eye set into a head) the faces either side of
    /// the border lie in one plane across the fold, and the crease split sees no crease: the copies would share the SDF's normal at
    /// the fold itself, half eye and half skin, and the skin beside the eye would shade as if it bent towards the eye. Copies whose
    /// own normals are within the crease angle of each other (paint on a smooth surface, a smooth union) share their average, so the
    /// border shows no seam; the others keep their own.
    /// </summary>
    private static void OneSidedNormals(ShadedMesh shaded, List<int>?[] facesOf, Vector3[] insidePoints, Func<Vector3, float> sdf,
        float cellSize, float creaseAngleDegrees)
    {
        var own = new Vector3[shaded.Positions.Length];
        Parallel.For(0, own.Length, v =>
        {
            if (facesOf[v] is null)
                return;
            var gradient = DualContouring.Normal(sdf, insidePoints[v], cellSize);
            own[v] = gradient.LengthSquared() > 0 && float.IsFinite(gradient.X + gradient.Y + gradient.Z) ? Vector3.Normalize(gradient) : Vector3.Zero;
        });
        var creaseCosine = MathF.Cos(creaseAngleDegrees * MathF.PI / 180);
        var copiesOf = new Dictionary<int, List<int>>();
        for (var v = 0; v < own.Length; v++)
            if (facesOf[v] is not null)
                (copiesOf.TryGetValue(shaded.Source[v], out var list) ? list : copiesOf[shaded.Source[v]] = []).Add(v);
        foreach (var copies in copiesOf.Values)
            foreach (var v in copies)
            {
                if (own[v] == Vector3.Zero)
                    continue;
                var sum = Vector3.Zero;
                foreach (var w in copies)
                    if (own[w] != Vector3.Zero && Vector3.Dot(own[v], own[w]) >= creaseCosine)
                        sum += own[w];
                shaded.Normals[v] = Vector3.Normalize(sum);
            }
    }

    /// <summary>
    /// The rare vertex whose material still is not its region's (a copy of a border point whose faces are all slivers along the border,
    /// so no sample inside them is of its region) takes the material of a neighbour on its side that has it.
    /// </summary>
    private static void OfItsSide(VertexMaterial[] materials, int[] triangles, int[] regions)
    {
        for (var t = 0; t < triangles.Length; t++)
        {
            var v = triangles[t];
            if (materials[v].First == regions[v])
                continue;
            var face = t - t % 3;
            for (var c = 0; c < 3; c++)
                if (triangles[face + c] is var w && materials[w].First == regions[v])
                {
                    materials[v] = materials[w];
                    break;
                }
        }
    }

    /// <summary>
    /// The vertex's material with its own region first. Where materials are blended evenly (a smooth union of two), either may come
    /// out the stronger at a vertex; the faces' region says which side of the border it is on.
    /// </summary>
    private static VertexMaterial OwnFirst(VertexMaterial material, int region) =>
        material.First != region && material.Second == region
            ? material with { First = material.Second, Second = material.First, Blend = 1 - material.Blend }
            : material;

    /// <summary>0 where one material has the surface to itself, towards 1 as another takes a share.</summary>
    private static float Blend(SurfaceSample sample)
    {
        var total = sample.Materials.Sum(s => s.Weight);
        return total > 0 ? 1 - sample.Materials.Max(s => s.Weight) / total : 0;
    }

    private static MaterialSource Dominant(SurfaceSample sample) => sample.Materials.MaxBy(s => s.Weight).Source;

    private static T[] Evaluate<T>(Vector3[] positions, Func<Vector3, T> evaluate)
    {
        var results = new T[positions.Length];
        Parallel.For(0, positions.Length, i => results[i] = evaluate(positions[i]));
        return results;
    }
}
