using System.Numerics;
using System.Reflection;
using SDFPipeline.Gpu;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using Xunit.Abstractions;

namespace SDFPipeline.Tests.Gpu;

/// <summary>
/// The GPU evaluator against the CPU one, which is the reference (ticket 39). All of these are skipped where there is no GPU.
/// </summary>
public class GpuSdfTests(ITestOutputHelper output)
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static SDFProgram ProgramOf(SDFEvaluable scene) =>
        SDFProgramNormalizePointers.Pointerify(new SDFProgram([], new SDFPointers(new(), new(), new(), new(), 0), [], scene));

    private static Vector3[] RandomPoints(SdfBounds bounds, int count, int seed)
    {
        var random = new Random(seed);
        var points = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var u = new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle());
            points[i] = bounds.Min - bounds.Size * 0.1f + u * bounds.Size * 1.2f;
        }
        return points;
    }

    /// <summary>Compares the two evaluators: distances within <paramref name="tolerance"/>, and the same side wherever the CPU is further than that from zero.</summary>
    private static (int Compared, float Worst) AssertSame(float[] gpu, Func<Vector3, float> cpu, Vector3[] points, float tolerance, string what)
    {
        var compared = 0;
        var worst = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var expected = cpu(points[i]);
            if (!float.IsFinite(expected) || MathF.Abs(expected) > 1e10f)
            {
                // Where there is no surface at all (an empty shape, a degenerate one), both must say so
                Assert.True(!float.IsFinite(gpu[i]) || MathF.Abs(gpu[i]) > 1e10f, $"{what}: at {points[i]} the CPU gives {expected}, the GPU {gpu[i]}");
                continue;
            }
            if (float.IsNaN(gpu[i]))
                continue;
            compared++;
            var difference = MathF.Abs(expected - gpu[i]);
            worst = MathF.Max(worst, difference);
            Assert.True(difference <= tolerance, $"{what}: at {points[i]} the CPU gives {expected}, the GPU {gpu[i]}");
            if (MathF.Abs(expected) > tolerance)
                Assert.True(expected < 0 == gpu[i] < 0, $"{what}: at {points[i]} the CPU gives {expected}, the GPU {gpu[i]}: not on the same side");
        }
        return (compared, worst);
    }

    // ---------------------------------------------------------------- Every node type

    private static readonly Type[] NodeTypes = typeof(SDFEvaluable).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(SDFEvaluable).IsAssignableFrom(t))
        .Where(t => !typeof(Pointerable).IsAssignableFrom(t) && !typeof(VariableReference).IsAssignableFrom(t))
        .OrderBy(t => t.Name)
        .ToArray();

    /// <summary>A node of the type with plain constant values, shapes from a small stock for its children, or null if it takes something this does not make (a function call, a material name).</summary>
    private static SDFEvaluable? Build(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var arguments = new List<object>();
        var counter = 0;
        foreach (var parameter in constructor.GetParameters())
        {
            var t = parameter.ParameterType;
            object? value =
                t == typeof(FloatEvaluable) ? new ConstValueFloat(new[] { 0.5f, 0.2f, 0.35f, 0.15f }[counter++ % 4])
                : t == typeof(IntegerEvaluable) ? new ConstValueInt(3)
                : t == typeof(V2Evaluable) ? new ConstValueV2(new V2(0.6f, 0.8f))
                : t == typeof(V3Evaluable) ? new ConstValueV3(new V3(0.2f + 0.15f * (counter % 4), 0.5f - 0.1f * (counter % 3), 0.3f + 0.07f * (counter++ % 5)))
                : t == typeof(SDFEvaluable) ? (counter++ % 2 == 0 ? new Sphere(new ConstValueFloat(0.5f)) : new Box(new ConstValueV3(new V3(0.4f, 0.3f, 0.5f))))
                : t == typeof(SDFEvaluable[]) ? new SDFEvaluable[] { new Sphere(new ConstValueFloat(0.45f)), new Box(new ConstValueV3(new V3(0.3f, 0.4f, 0.35f))) }
                : t == typeof(SDF2DEvaluable) ? new Circle(new ConstValueFloat(0.4f))
                : t == typeof(SDF2DEvaluable[]) ? new SDF2DEvaluable[] { new Circle(new ConstValueFloat(0.4f)), new Circle(new ConstValueFloat(0.3f)) }
                : null;
            if (value is null)
                return null;
            arguments.Add(value);
        }
        var node = (SDFEvaluable)constructor.Invoke(arguments.ToArray());
        return node;
    }

    public static TheoryData<string> NodeTypeNames() => new(NodeTypes.Where(t => Build(t) is not null).Select(t => t.Name));

    [Fact]
    public void MostNodeTypesCanBeBuiltForTheParityTests()
    {
        var skipped = NodeTypes.Where(t => Build(t) is null).Select(t => t.Name).ToArray();
        output.WriteLine($"{NodeTypes.Length - skipped.Length} node types tested; not built here: {string.Join(", ", skipped)}");
        Assert.True(NodeTypes.Length - skipped.Length >= 60, string.Join(", ", skipped));
    }

    [Theory]
    [MemberData(nameof(NodeTypeNames))]
    public void EveryNodeType_GivesTheCpusDistances(string name)
    {
        if (GpuContext.Shared is null)
            return;
        var node = Build(NodeTypes.Single(t => t.Name == name))!;
        var program = ProgramOf(node);
        var cpu = new SdfCalculatorCpu().Prepare(program).Distance;
        var gpu = GpuSdf.TryCreate(program, null, out var why);
        Assert.True(gpu is not null, $"{name} does not compile for the GPU: {why}");

        var bounds = new SdfBounds(new Vector3(-1.2f), new Vector3(1.2f));
        var points = RandomPoints(bounds, 4000, name.GetHashCode() & 0xffff);
        var (compared, worst) = AssertSame(gpu!.Distances(points), cpu, points, 2e-4f, name);
        Assert.True(compared > 100 || name == "Empty", $"{name}: only {compared} finite distances to compare");
        output.WriteLine($"{name}: {compared} points, worst difference {worst:g3}");
    }

    // ---------------------------------------------------------------- Real models

    [GpuFact]
    public void RealModels_GiveTheCpusDistances()
    {
        foreach (var model in new[] { "characters/amanita-scout", "kitchen/kitchen-room", "terrain/island-underside", "toys/toy-castle0", "chess/chess-pawn0" })
        {
            var path = Path.Combine(Content, "models", model + ".sdf");
            if (!File.Exists(path))
                continue;
            var program = PartSplitter.Expanded(SdfProgramFile.Load(path));
            var cpu = new SdfCalculatorCpu().Prepare(program).Distance;
            var bounds = SDFPipeline.Rendering.RenderFramer.ModelBounds(program)!;
            var gpu = GpuSdf.TryCreate(program, null, out var why);
            Assert.True(gpu is not null, $"{model} does not compile for the GPU: {why}");
            var points = RandomPoints(bounds, model.Contains("castle") ? 1500 : 6000, 7);
            var (compared, worst) = AssertSame(gpu!.Distances(points), cpu, points, bounds.LongestSide * 1e-4f, model);
            Assert.True(compared > 100, model);
            output.WriteLine($"{model}: {compared} points, worst difference {worst:g3} m");
        }
    }

    // ---------------------------------------------------------------- Meshing

    private static (SDFProgram Program, Func<Vector3, float> Cpu, ContouringGrid Grid) Part(string model, string part)
    {
        var program = SdfProgramFile.Load(Path.Combine(Content, "models", model + ".sdf"));
        var source = PartSplitter.Split(program, new List<string>()).First(s => s.Name == part);
        var cpu = new SdfCalculatorCpu().Prepare(source.Program).Distance;
        var options = new MeshingOptions();
        var bounds = SDFPipeline.Rendering.RenderFramer.ModelBounds(source.Program)!;
        return (source.Program, cpu, ContouringGrid.Around(bounds, options.CellsAlong(bounds.LongestSide, source.Detail)));
    }

    [GpuFact]
    public void Contouring_OnTheGpu_MakesTheCpusMesh()
    {
        // The eye of the amanita scout is cut by a plane lying exactly on the grid: its signs must come out as on the CPU
        foreach (var (model, part) in new[] { ("characters/amanita-scout", "eye"), ("characters/amanita-scout", "hand"), ("characters/amanita-scout", "amanita_scout") })
        {
            var (program, cpu, grid) = Part(model, part);
            var gpu = GpuSdf.TryCreate(program, null, out var why);
            Assert.True(gpu is not null, why);

            var expected = DualContouring.Contour(cpu, grid).Mesh;
            var actual = DualContouring.Contour(cpu, grid, gpu).Mesh;

            // The same cells have vertices, in the same order; they sit where the CPU puts them up to float rounding of the crossings
            Assert.Equal(expected.Positions.Length, actual.Positions.Length);
            var distances = expected.Positions.Zip(actual.Positions, (a, b) => Vector3.Distance(a, b) / grid.CellSize).ToArray();
            var far = distances.Count(d => d > 0.02f);
            output.WriteLine($"{part}: {distances.Length} vertices, {far} further than 0.02 cells from the CPU's, worst {distances.Max():0.000} cells, triangles {expected.TriangleCount}/{actual.TriangleCount}");
            Assert.True(far <= distances.Length / 50, $"{part}: {far} of {distances.Length} vertices moved");
            Assert.InRange(distances.Max(), 0, 1.75f); // a vertex stays in its cell, whose diagonal is 1.73 cells
            Assert.Equal(expected.TriangleCount, actual.TriangleCount);
        }
    }

    [GpuFact]
    public void Contouring_OnTheGpu_IsTheSameEveryTime()
    {
        var (program, cpu, grid) = Part("characters/amanita-scout", "eye");
        var gpu = GpuSdf.TryCreate(program, null, out _)!;
        var first = DualContouring.Contour(cpu, grid, gpu).Mesh;
        var second = DualContouring.Contour(cpu, grid, gpu).Mesh;
        Assert.Equal(first.Positions, second.Positions);
        Assert.Equal(first.Triangles, second.Triangles);
    }

    [GpuFact]
    public void Blocks_DoNotDependOnWhatTheyAreBatchedWith()
    {
        var (program, _, grid) = Part("characters/amanita-scout", "eye");
        var gpu = GpuSdf.TryCreate(program, null, out _)!;
        var spec = new ContouringGridSpec(grid.Origin, grid.CellSize);
        var clip = new GpuClip(grid.Origin + (grid.Max - grid.Origin) * 0.5f, (grid.Max - grid.Origin) * 0.5f - new Vector3(grid.CellSize * 0.5f), new Vector3(0.001f));
        (int, int, int)[] blocks = [(16, 16, 16), (24, 16, 24), (24, 24, 24), (16, 24, 24)];
        var together = gpu.Blocks(blocks, spec, clip);
        for (var b = 0; b < blocks.Length; b++)
        {
            var alone = gpu.Blocks([blocks[b]], spec, clip);
            Assert.Equal(alone.Values, together.Values.AsSpan(b * GpuSdf.ValuesPerBlock, GpuSdf.ValuesPerBlock).ToArray());
            Assert.Equal(alone.Crossings, together.Crossings.AsSpan(b * 729 * 3 * 4, 729 * 3 * 4).ToArray());
        }
    }

    // ---------------------------------------------------------------- Simplifying

    [GpuFact]
    public void SignBracket_OnTheGpu_MatchesTheCpus()
    {
        var (program, cpu, grid) = Part("characters/amanita-scout", "eye");
        var gpu = GpuSdf.TryCreate(program, null, out _)!;
        var random = new Random(3);
        var bounds = SDFPipeline.Rendering.RenderFramer.ModelBounds(program)!;
        var segments = Enumerable.Range(0, 3000).Select(_ =>
        {
            var p = RandomPoints(bounds, 1, random.Next())[0];
            var n = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f));
            return (p, n, grid.CellSize * (0.05f + random.NextSingle()));
        }).ToArray();
        var eps = grid.CellSize * 1e-4f;

        bool Reference((Vector3 p, Vector3 n, float d) s)
        {
            var previous = cpu(s.p - s.n * s.d);
            if (previous >= -eps && previous <= eps) return true;
            for (var i = 1; i <= 4; i++)
            {
                var value = cpu(s.p + s.n * (s.d * (i - 2) / 2));
                if (value >= -eps && value <= eps || (value > 0) != (previous > 0)) return true;
                previous = value;
            }
            return false;
        }

        var found = gpu.SurfaceWithin(segments, eps);
        var different = segments.Where((s, i) => Reference(s) != found[i]).Count();
        output.WriteLine($"{segments.Length} segments, {segments.Count(Reference)} bracket the surface, {different} differ");
        Assert.True(different <= 3, $"{different} of {segments.Length} segments differ");
    }

    [GpuFact]
    public void Simplifying_OnTheGpu_GivesTheCpusMesh()
    {
        var (program, cpu, grid) = Part("characters/amanita-scout", "eye");
        var gpu = GpuSdf.TryCreate(program, null, out _)!;
        var contoured = DualContouring.Contour(cpu, grid);
        var options = new MeshingOptions();
        float[] limits = [options.LodErrorInCells(0) * grid.CellSize, options.LodErrorInCells(1) * grid.CellSize];

        var onCpu = QuadricSimplifier.SimplifyInSteps(contoured, cpu, limits, grid.CellSize, options.CreaseAngleDegrees, batched: true).ToArray();
        var onGpu = QuadricSimplifier.SimplifyInSteps(contoured, cpu, limits, grid.CellSize, options.CreaseAngleDegrees, batched: true, gpu: gpu).ToArray();
        for (var level = 0; level < limits.Length; level++)
        {
            output.WriteLine($"LOD{level}: {onCpu[level].TriangleCount} triangles on the CPU, {onGpu[level].TriangleCount} with the GPU");
            Assert.InRange(onGpu[level].TriangleCount, onCpu[level].TriangleCount * 0.98, onCpu[level].TriangleCount * 1.02 + 2);
            Assert.True(onGpu[level].TrackedError <= limits[level] * 1.001f, $"tracked error {onGpu[level].TrackedError} over the limit {limits[level]}");
        }
    }

    [GpuFact]
    public void MeshingAModel_WithTheGpuOn_GivesClosedMeshesLikeTheCpus()
    {
        var program = SdfProgramFile.Load(Path.Combine(Content, "models", "characters", "amanita-scout.sdf"));
        var materials = SDFPipeline.Materials.MaterialFiles.LoadLibrary(Path.Combine(Path.GetDirectoryName(Content)!, "materials"));
        var onCpu = ModelMesher.Mesh(program, new MeshingOptions { Resolution = 64, LodCount = 3, Materials = materials });
        var onGpu = ModelMesher.Mesh(program, new MeshingOptions { Resolution = 64, LodCount = 3, Materials = materials, UseGpu = true, GpuMinPredictedCpuSeconds = 0 });
        Assert.Equal(onCpu.Meshes.Select(m => m.Name), onGpu.Meshes.Select(m => m.Name));
        foreach (var (cpu, gpu) in onCpu.Meshes.Zip(onGpu.Meshes))
        {
            output.WriteLine($"{cpu.Name}: {cpu.TriangleCount} triangles on the CPU, {gpu.TriangleCount} on the GPU; LODs {string.Join("/", cpu.Lods.Select(l => l.TriangleCount))} against {string.Join("/", gpu.Lods.Select(l => l.TriangleCount))}");
            SDFPipeline.Tests.Meshing.MeshTestPrograms.AssertClosedAndOriented(SDFPipeline.Tests.Meshing.MeshTestPrograms.Welded(gpu));
            Assert.InRange(gpu.TriangleCount, cpu.TriangleCount * 0.92, cpu.TriangleCount * 1.08 + 4);
            Assert.True(gpu.Statistics.TrackedError <= gpu.MaxError * 1.001f);
        }
    }

    [Fact]
    public void WithoutAGpu_MeshingFallsBackToTheCpu()
    {
        // Whatever machine this runs on: turning the GPU off in the options must give a mesh, and the CPU path is the reference
        var program = SDFPipeline.Tests.Meshing.MeshTestPrograms.Read("Sphere radius=0.5");
        var mesh = ModelMesher.Mesh(program, new MeshingOptions { UseGpu = false });
        Assert.NotEmpty(mesh.Meshes);
        Assert.True(mesh.Meshes[0].TriangleCount > 50);
    }
}
