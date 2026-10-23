using System.Numerics;
using System.Runtime.InteropServices;
using SDFPipeline.Materials;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Gpu;

/// <summary>The cut-off box meshing puts round an SDF (see <c>DualContouring</c>): the SDF clipped half a cell inside the grid, NaN looked at again a hair away.</summary>
public readonly record struct GpuClip(Vector3 Center, Vector3 Extent, Vector3 Jitter);

/// <summary>The grid meshing contours on: corner (i, j, k) is at <c>Origin + (i, j, k) × CellSize</c>, computed exactly like <c>ContouringGrid.Corner</c>.</summary>
public readonly record struct ContouringGridSpec(Vector3 Origin, float CellSize)
{
    public Vector3 Corner(int i, int j, int k) => Origin + new Vector3(i, j, k) * CellSize;
}

/// <summary>
/// An SDF program compiled for the GPU: the distance function written by <see cref="Webgl2CodeGenerator"/> (the same GLSL the browser's
/// renderer runs) inside compute kernels that evaluate it for many points at once. Each kernel is compiled when first used. The CPU
/// evaluator (<c>SdfCalculatorCpu</c>) stays the reference: the GPU gives the same distances up to float rounding.
/// All kernels run in 32-bit floats and put their points and results in flat float arrays (x, y, z in a row).
/// </summary>
public sealed class GpuSdf
{
    /// <summary>The cut-off distance a clipped SDF reports where the SDF is empty or NaN, in place of infinity (which fast-math shader compilers may not keep).</summary>
    private const string FarValue = "1e30";

    private readonly GpuContext context;
    private readonly string sceneCode;
    private readonly object kernels = new();

    private GpuSdf(GpuContext context, string sceneCode)
    {
        this.context = context;
        this.sceneCode = sceneCode;
    }

    public GpuContext Context => context;

    /// <summary>Compiles the program's distance function for the GPU, or returns null (with the reason in <paramref name="why"/>) if there is no GPU or the program does not compile.</summary>
    public static GpuSdf? TryCreate(SDFProgram program, MaterialLibrary? materials, out string? why)
    {
        why = null;
        if (GpuContext.Shared is not { } context)
        {
            why = GpuContext.WhyNot;
            return null;
        }
        var sdf = new GpuSdf(context, new Webgl2CodeGenerator(materials).GenerateDistanceCode(program));
        try
        {
            // The plain distance kernel is the cheapest way to find out whether the program compiles at all
            sdf.KernelFor();
            return sdf;
        }
        catch (GpuException exception)
        {
            why = exception.Message;
            return null;
        }
    }

    // ---------------------------------------------------------------- Kernels

    /// <summary>
    /// One compute shader for everything, so a program is compiled once (the GPU compiler's time goes with the size of the SDF, and the
    /// SDF is inlined where it is called): the uniform block holds the mode in ui[0].w, the item count in ui[0].x, whether to clip in
    /// ui[0].y, and floats in uf; three float buffers carry the inputs and results (integers as whole floats).
    /// </summary>
    private const string Kernel = $$"""
        #version 450
        layout(local_size_x = 64) in;
        layout(std140, set = 0, binding = 0) uniform Params { vec4 uf[4]; ivec4 ui[4]; };
        layout(std430, set = 0, binding = 1) readonly buffer In { float inp[]; };
        layout(std430, set = 0, binding = 2) buffer OutA { float outA[]; };
        layout(std430, set = 0, binding = 3) buffer OutB { float outB[]; };

        // @SCENE@

        // The SDF, clipped when ui[0].y is 1 (the clip of GpuClip: centre in uf[1], extent in uf[2], jitter in uf[3]): half a cell inside the
        // grid, NaN looked at again a hair away. One call site of the scene function, so the compiler inlines it once.
        float sdf_eval(vec3 p)
        {
            bool clipped = ui[0].y == 1;
            float d = 0.0;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                d = sdf_scene(attempt == 0 ? p : p + uf[3].xyz);
                if (!clipped || !isnan(d)) break;
            }
            if (!clipped) return d;
            if (isnan(d)) d = {{FarValue}};
            vec3 q = abs(p - uf[1].xyz) - uf[2].xyz;
            float box = length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            return max(d, box);
        }

        bool isfin(float x) { return !(isnan(x) || isinf(x)); }

        vec3 point(uint i) { return vec3(inp[3u * i], inp[3u * i + 1u], inp[3u * i + 2u]); }

        // Mode 0: the distance at each point of In (x, y, z in a row), to OutA
        // Mode 1: the central-difference gradient (step uf[0].x) at each point, to OutA (3 floats each)
        // Mode 5: the gradient from four samples on a tetrahedron (step uf[0].x), as the simplifier's facing test takes it, to OutA (3 floats each)
        // Mode 2: the simplifier's sign bracket for each segment of In (point, direction, length: 7 floats), eps in uf[0].x, 1.0 or 0.0 to OutA
        // Mode 4: for blocks of 8 x 8 x 8 cells of the contouring grid (cell size uf[0].w) whose 1313 sample positions each are in In (the 9 x 9 x 9
        //         corners come first, x fastest) and whose SDF values at them are in OutA: for every block, corner and axis (3): where the surface crosses the edge and the
        //         normal there, 4 floats (the normal's direction, not normalised, then the position along the edge), to OutB; zeros where
        //         the edge's corners are on one side

        bool within(vec3 p, vec3 n, float dist, float eps)
        {
            float previous = sdf_eval(p - n * dist);
            if (previous >= -eps && previous <= eps) return true;
            for (int i = 1; i <= 4; i++)
            {
                float value = sdf_eval(p + n * (dist * float(i - 2) / 2.0));
                if ((value >= -eps && value <= eps) || ((value > 0.0) != (previous > 0.0))) return true;
                previous = value;
            }
            return false;
        }

        float bisect(vec3 a, vec3 direction, float da)
        {
            float t0 = 0.0, t1 = 1.0;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                float t = (t0 + t1) * 0.5;
                if ((sdf_eval(a + direction * t) < 0.0) == (da < 0.0)) t0 = t; else t1 = t;
            }
            return (t0 + t1) * 0.5;
        }

        float findRoot(vec3 a, vec3 direction, float da, float db, float cell)
        {
            if (!isfin(da) || !isfin(db)) return bisect(a, direction, da);
            float t0 = 0.0, t1 = 1.0, d0 = da, d1 = db;
            float tolerance = cell * 1e-5;
            int side = 0;
            float t = 0.5;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                float guess = (t0 * d1 - t1 * d0) / (d1 - d0);
                t = isnan(guess) ? (t0 + t1) * 0.5 : clamp(guess, t0, t1);
                float d = sdf_eval(a + direction * t);
                if (abs(d) < tolerance || !isfin(d)) break;
                if ((d < 0.0) == (d0 < 0.0))
                {
                    t0 = t; d0 = d;
                    if (side == -1) d1 *= 0.5;
                    side = -1;
                }
                else
                {
                    t1 = t; d1 = d;
                    if (side == 1) d0 *= 0.5;
                    side = 1;
                }
            }
            return t;
        }

        vec3 normalAt(vec3 p, float cell)
        {
            float center = sdf_eval(p);
            float step = cell * 0.05;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                vec3 plus, minus;
                plus.x = sdf_eval(p + vec3(step, 0.0, 0.0)); minus.x = sdf_eval(p - vec3(step, 0.0, 0.0));
                plus.y = sdf_eval(p + vec3(0.0, step, 0.0)); minus.y = sdf_eval(p - vec3(0.0, step, 0.0));
                plus.z = sdf_eval(p + vec3(0.0, 0.0, step)); minus.z = sdf_eval(p - vec3(0.0, 0.0, step));
                vec3 second = abs(plus + minus - 2.0 * center);
                bool kinked = second.x > 0.05 * step || second.y > 0.05 * step || second.z > 0.05 * step;
                if (!kinked) return (plus - minus) / (2.0 * step);
                if (step <= cell * 1e-4) return vec3(0.0);
                step *= 0.125;
            }
            return vec3(0.0);
        }

        void main()
        {
            uint i = gl_GlobalInvocationID.x;
            int mode = ui[0].w;
            if (mode == 0 || mode == 1 || mode == 2 || mode == 5)
            {
                if (i >= uint(ui[0].x)) return;
                if (mode == 5)
                {
                    vec3 p = point(i);
                    float h = uf[0].x;
                    vec3 a = vec3(h, -h, -h), b = vec3(-h, -h, h), c = vec3(-h, h, -h), d = vec3(h, h, h);
                    vec3 g = a * sdf_eval(p + a) + b * sdf_eval(p + b) + c * sdf_eval(p + c) + d * sdf_eval(p + d);
                    outA[3u * i] = g.x;
                    outA[3u * i + 1u] = g.y;
                    outA[3u * i + 2u] = g.z;
                }
                else if (mode == 0)
                {
                    outA[i] = sdf_eval(point(i));
                }
                else if (mode == 1)
                {
                    vec3 p = point(i);
                    float h = uf[0].x;
                    vec3 g = vec3(
                        sdf_eval(p + vec3(h, 0, 0)) - sdf_eval(p - vec3(h, 0, 0)),
                        sdf_eval(p + vec3(0, h, 0)) - sdf_eval(p - vec3(0, h, 0)),
                        sdf_eval(p + vec3(0, 0, h)) - sdf_eval(p - vec3(0, 0, h))) / (2.0 * h);
                    outA[3u * i] = g.x;
                    outA[3u * i + 1u] = g.y;
                    outA[3u * i + 2u] = g.z;
                }
                else
                {
                    uint s = 7u * i;
                    outA[i] = within(vec3(inp[s], inp[s + 1u], inp[s + 2u]), vec3(inp[s + 3u], inp[s + 4u], inp[s + 5u]), inp[s + 6u], uf[0].x) ? 1.0 : 0.0;
                }
            }
            else
            {
                uint b = i / (729u * 3u);
                if (b >= uint(ui[0].x)) return;
                int axis = int(i % 3u);
                int c = int((i / 3u) % 729u);
                ivec3 local = ivec3(c % 9, (c / 9) % 9, c / 81);
                vec4 result = vec4(0.0);
                bool inside = axis == 0 ? local.x < 8 : (axis == 1 ? local.y < 8 : local.z < 8);
                if (inside)
                {
                    int other = c + (axis == 0 ? 1 : (axis == 1 ? 9 : 81));
                    float da = outA[b * 1313u + uint(c)];
                    float db = outA[b * 1313u + uint(other)];
                    if ((da < 0.0) != (db < 0.0))
                    {
                        float cell = uf[0].w;
                        vec3 a = point(b * 1313u + uint(c));
                        vec3 direction = vec3(axis == 0 ? 1.0 : 0.0, axis == 1 ? 1.0 : 0.0, axis == 2 ? 1.0 : 0.0) * cell;
                        float t = findRoot(a, direction, da, db, cell);
                        result = vec4(normalAt(a + direction * t, cell), t);
                    }
                }
                outB[4u * i] = result.x;
                outB[4u * i + 1u] = result.y;
                outB[4u * i + 2u] = result.z;
                outB[4u * i + 3u] = result.w;
            }
        }
        """;

    private const int DistanceMode = 0, GradientMode = 1, BracketMode = 2, CrossingsMode = 4, TetrahedronMode = 5;

    private GpuKernel? compiled;

    private GpuKernel KernelFor()
    {
        lock (kernels)
        {
            if (compiled is not null)
                return compiled;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            compiled = context.Compile(Kernel.Replace("// @SCENE@", sceneCode), 3, "sdf");
            if (Environment.GetEnvironmentVariable("SDF_GPU_TRACE") is { Length: > 0 })
                Console.Error.WriteLine($"GPU compile: {sw.Elapsed.TotalSeconds:0.000} s, {sceneCode.Length} chars of scene");
            return compiled;
        }
    }

    /// <summary>Runs the kernel in a mode over <paramref name="items"/> items; the buffers not used are a stand-in.</summary>
    private void Run(int mode, int items, Span<float> floats, Span<int> ints, GpuBuffer input, GpuBuffer a, GpuBuffer? b = null)
    {
        ints[3] = mode;
        using var stand = b is null ? context.Output<float>(4) : null;
        context.Run(KernelFor(), floats, ints, items, input, a, b ?? stand!);
    }

    // ---------------------------------------------------------------- Evaluating

    private static void Clip(Span<float> floats, GpuClip? clip, Span<int> ints)
    {
        if (clip is not { } c)
            return;
        ints[1] = 1;
        c.Center.CopyTo(floats[4..]);
        c.Extent.CopyTo(floats[8..]);
        c.Jitter.CopyTo(floats[12..]);
    }

    /// <summary>The SDF at each point; with a <paramref name="clip"/>, the clipped SDF meshing uses.</summary>
    public float[] Distances(ReadOnlySpan<Vector3> points, GpuClip? clip = null)
    {
        var result = new float[points.Length];
        for (var start = 0; start < points.Length; start += BatchItems)
        {
            var count = Math.Min(BatchItems, points.Length - start);
            var flat = Flatten(points.Slice(start, count));
            Span<float> floats = stackalloc float[16];
            Span<int> ints = stackalloc int[16];
            ints[0] = count;
            Clip(floats, clip, ints);
            using var input = context.Storage<float>(flat);
            using var output = context.Output<float>(count);
            Run(DistanceMode, count, floats, ints, input, output);
            context.Read<float>(output, count).CopyTo(result.AsSpan(start));
        }
        return result;
    }

    /// <summary>The central-difference gradient (step <paramref name="step"/>) at each point.</summary>
    public Vector3[] Gradients(ReadOnlySpan<Vector3> points, float step, GpuClip? clip = null)
    {
        var result = new Vector3[points.Length];
        for (var start = 0; start < points.Length; start += BatchItems / 3)
        {
            var count = Math.Min(BatchItems / 3, points.Length - start);
            var flat = Flatten(points.Slice(start, count));
            Span<float> floats = stackalloc float[16];
            Span<int> ints = stackalloc int[16];
            ints[0] = count;
            floats[0] = step;
            Clip(floats, clip, ints);
            using var input = context.Storage<float>(flat);
            using var output = context.Output<float>(3L * count);
            Run(GradientMode, count, floats, ints, input, output);
            var values = context.Read<float>(output, 3 * count);
            for (var i = 0; i < count; i++)
                result[start + i] = new Vector3(values[3 * i], values[3 * i + 1], values[3 * i + 2]);
        }
        return result;
    }

    /// <summary>The gradient from four samples on a tetrahedron of size <paramref name="step"/> (the simplifier's facing test) at each point; not divided by the step, as the CPU's.</summary>
    public Vector3[] TetrahedronGradients(ReadOnlySpan<Vector3> points, float step)
    {
        var result = new Vector3[points.Length];
        for (var start = 0; start < points.Length; start += BatchItems / 4)
        {
            var count = Math.Min(BatchItems / 4, points.Length - start);
            var flat = Flatten(points.Slice(start, count));
            Span<float> floats = stackalloc float[16];
            Span<int> ints = stackalloc int[16];
            ints[0] = count;
            floats[0] = step;
            using var input = context.Storage<float>(flat);
            using var output = context.Output<float>(3L * count);
            Run(TetrahedronMode, count, floats, ints, input, output);
            var values = context.Read<float>(output, 3 * count);
            for (var i = 0; i < count; i++)
                result[start + i] = new Vector3(values[3 * i], values[3 * i + 1], values[3 * i + 2]);
        }
        return result;
    }

    /// <summary>
    /// For each segment (a point, a unit direction and a length), whether the SDF changes sign or is within <paramref name="eps"/> of zero
    /// at the segment's sample points: the simplifier's check that a face lies on the surface. Samples at the point minus the
    /// direction times the length, then at the point plus the direction times the length times -0.5, 0, 0.5 and 1.
    /// </summary>
    public bool[] SurfaceWithin(ReadOnlySpan<(Vector3 Point, Vector3 Direction, float Length)> segments, float eps)
    {
        var result = new bool[segments.Length];
        for (var start = 0; start < segments.Length; start += BatchItems / 4)
        {
            var count = Math.Min(BatchItems / 4, segments.Length - start);
            var flat = new float[7 * count];
            for (var i = 0; i < count; i++)
            {
                var (point, direction, length) = segments[start + i];
                point.CopyTo(flat, 7 * i);
                direction.CopyTo(flat, 7 * i + 3);
                flat[7 * i + 6] = length;
            }
            Span<float> floats = stackalloc float[16];
            Span<int> ints = stackalloc int[16];
            ints[0] = count;
            floats[0] = eps;
            using var input = context.Storage<float>(flat);
            using var output = context.Output<float>(count);
            Run(BracketMode, count, floats, ints, input, output);
            var found = context.Read<float>(output, count);
            for (var i = 0; i < count; i++)
                result[start + i] = found[i] != 0;
        }
        return result;
    }

    /// <summary>Points per dispatch: a few million at most, which keeps every buffer well under the GPU's size limits.</summary>
    private const int BatchItems = 1 << 21;

    /// <summary>Values the block kernel makes per block: 729 corners and 584 sub-cube centres.</summary>
    public const int ValuesPerBlock = 1313;

    /// <summary>Blocks per dispatch, so the crossings buffer (729 × 3 × 16 bytes each) stays small.</summary>
    public const int BlocksPerBatch = 512;

    /// <summary>The values and crossings of a batch of blocks, see <see cref="Blocks"/>.</summary>
    public sealed record BlockData(float[] Values, float[] Crossings);

    /// <summary>
    /// For a batch of blocks of 8 × 8 × 8 cells (their lowest cell's grid indices) of the grid with the given origin and cell size, the
    /// clipped SDF at each block's 729 corners and 584 sub-cube centres (in the order <c>DualContouring</c> reads them), and at every
    /// grid edge in the block whose corners differ in sign the crossing: 4 floats, the normal's direction and where on the edge. The
    /// sample positions are made here on the CPU, with the same float arithmetic as the CPU contouring: a surface lying exactly on a
    /// grid plane (a symmetric model cut at its middle) must give the same signs on both, and the GPU's fused multiply-adds would not.
    /// </summary>
    public BlockData Blocks(ReadOnlySpan<(int I, int J, int K)> blocks, ContouringGridSpec grid, GpuClip clip)
    {
        if (blocks.Length > BlocksPerBatch)
            throw new ArgumentOutOfRangeException(nameof(blocks), $"At most {BlocksPerBatch} blocks at a time");
        var positions = new float[3 * ValuesPerBlock * blocks.Length];
        for (var b = 0; b < blocks.Length; b++)
        {
            var (bi, bj, bk) = blocks[b];
            var at = 3 * ValuesPerBlock * b;
            void Put(Vector3 p)
            {
                positions[at++] = p.X;
                positions[at++] = p.Y;
                positions[at++] = p.Z;
            }
            for (var k = 0; k <= 8; k++)
                for (var j = 0; j <= 8; j++)
                    for (var i = 0; i <= 8; i++)
                        Put(grid.Corner(bi + i, bj + j, bk + k));
            foreach (var size in (ReadOnlySpan<int>)[4, 2, 1])
            {
                var dim = 8 / size;
                var half = new Vector3(size * grid.CellSize * 0.5f);
                for (var k = 0; k < dim; k++)
                    for (var j = 0; j < dim; j++)
                        for (var i = 0; i < dim; i++)
                            Put(grid.Corner(bi + i * size, bj + j * size, bk + k * size) + half);
            }
        }

        Span<float> floats = stackalloc float[16];
        Span<int> ints = stackalloc int[16];
        ints[0] = blocks.Length * ValuesPerBlock;
        Clip(floats, clip, ints);
        using var input = context.Storage<float>(positions);
        using var values = context.Output<float>((long)ValuesPerBlock * blocks.Length);
        using var crossings = context.Output<float>(729L * 3 * 4 * blocks.Length);
        Run(DistanceMode, ValuesPerBlock * blocks.Length, floats, ints, input, values);
        ints[0] = blocks.Length;
        floats[3] = grid.CellSize;
        Run(CrossingsMode, 729 * 3 * blocks.Length, floats, ints, input, values, crossings);
        return new BlockData(context.Read<float>(values, ValuesPerBlock * blocks.Length), context.Read<float>(crossings, 729 * 3 * 4 * blocks.Length));
    }

    private static float[] Flatten(ReadOnlySpan<Vector3> points)
    {
        var flat = new float[3 * points.Length];
        for (var i = 0; i < points.Length; i++)
            points[i].CopyTo(flat, 3 * i);
        return flat;
    }
}
