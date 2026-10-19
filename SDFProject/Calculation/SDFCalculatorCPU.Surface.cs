using System.Collections.Concurrent;
using System.Numerics;
using SDFProject.ProgramDefinition;
using static SDFProject.Calculation.SdfMath;

namespace SDFProject.Calculation;

/// <summary>Where a surface's look comes from: a named material, a flat colour, or neither, which is the default look.</summary>
public readonly record struct MaterialSource(string? Name, V3? Color)
{
    public static readonly MaterialSource Default = new(null, null);

    public static MaterialSource Named(string name) => new(name, null);

    public static MaterialSource OfColor(V3 color) => new(null, color);

    /// <summary>The material it stands for; a name that is not in the library is <see cref="MaterialDefinition.Missing"/>, as in the renderer.</summary>
    public MaterialDefinition Resolve(MaterialLibrary library) =>
        Name is { } name ? library.Resolve(name) : Color is { } color ? MaterialDefinition.FromColor(color) : MaterialDefinition.Default;
}

/// <summary>One material's share of a surface point, and where the point is in the coordinates of the shape the material is on.</summary>
public readonly record struct MaterialShare(MaterialSource Source, float Weight, Vector3 LocalPosition);

/// <summary>
/// The distance and the material mix at a point, as the WebGL2 renderer's material function finds them: the shares add up to 1, and
/// mixing their materials by weight gives exactly the material the renderer shades with.
/// </summary>
public sealed record SurfaceSample(float Distance, IReadOnlyList<MaterialShare> Materials);

public partial class SdfCalculatorCpu
{
    /// <summary>
    /// Prepares a program for evaluation at many positions, from many threads: the function table and the variables are worked out once
    /// rather than for every position as <see cref="Calculate(V3, SDFProgram)"/> does.
    /// </summary>
    public Prepared Prepare(SDFProgram program) => new(this, program);

    /// <summary>A program ready to evaluate; safe to use from several threads at once.</summary>
    public sealed class Prepared
    {
        private readonly SdfCalculatorCpu calculator;
        private readonly Dictionary<string, VariableDeclaration> sceneVariables;
        private readonly ConcurrentDictionary<Evaluable, bool> containsColor = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<string, FunctionInfo> Functions { get; }
        internal EvaluationContext Context { get; }

        public SDFProgram Program { get; }

        internal Prepared(SdfCalculatorCpu calculator, SDFProgram program)
        {
            this.calculator = calculator;
            Program = program;
            Functions = program.Functions.ToDictionary(f => f.id, f => new FunctionInfo(f, VariableIndices(f.variables)));
            sceneVariables = program.sceneVariables.ToDictionary(v => v.id);

            var pointers = program.pointers;
            var variableValues = new object[program.sceneVariables.Length];
            var context = new EvaluationContext([], variableValues, VariableIndices(program.sceneVariables), pointers.SDFEvaluablePointers,
                pointers.SDF2DEvaluablePointers, pointers.FloatEvaluablePointers, pointers.IntegerEvaluablePointers, 0);
            calculator.EvaluateVariables(program.sceneVariables, variableValues, context);
            Context = context with { VisibleVariableCount = program.sceneVariables.Length };
        }

        public float Distance(Vector3 p) => calculator.Calculate(p, Program.scene, Functions, Context);

        /// <summary>The distance and the material mix at <paramref name="p"/>; it mirrors the renderer's material function.</summary>
        public SurfaceSample Surface(Vector3 p)
        {
            var (distance, materials) = calculator.Surface(p, Program.scene, [new MaterialShare(MaterialSource.Default, 1, p)], this, Context);
            return new SurfaceSample(distance, materials);
        }

        /// <summary>Whether a colour or material is below the node; function bodies never count, as in the renderer.</summary>
        internal bool ContainsColor(Evaluable node) =>
            containsColor.TryGetValue(node, out var known) ? known : containsColor[node] = FindColor(node, []);

        private bool FindColor(Evaluable node, HashSet<string> visitedVariables)
        {
            if (node is SDFEvaluablePointerable pointer)
                node = Program.pointers.SDFEvaluablePointers[pointer.PointerId];
            return node switch
            {
                Color or ProgramDefinition.Material => true,
                SDFFunctionCall => false,
                VariableReferenceSDF r => visitedVariables.Add(r.variableId)
                                          && sceneVariables.TryGetValue(r.variableId, out var variable)
                                          && FindColor(variable.value, visitedVariables),
                _ => SDFProgramOperator.Helpers.EvaluableChildren.Children(node).Any(child => FindColor(child, visitedVariables)),
            };
        }
    }

    private (float distance, MaterialShare[] materials) Surface(Vector3 p, SDFEvaluable evaluable, MaterialShare[] material, Prepared prepared,
        EvaluationContext context)
    {
        float Float(FloatEvaluable e) => Calculate(e, context);
        Vector3 Vec3(V3Evaluable e) => Calculate(e, context);
        float Sdf(SDFEvaluable e, Vector3 q) => Calculate(q, e, prepared.Functions, context);
        (float distance, MaterialShare[] materials) Walk(SDFEvaluable e, Vector3 q, MaterialShare[] m) => Surface(q, e, m, prepared, context);
        (float, MaterialShare[]) Modified(SDFEvaluable e, Func<float, float> distance)
        {
            var (d, m) = Walk(e, p, material);
            return (distance(d), m);
        }

        (float, MaterialShare[]) Combine(SDFEvaluable[] primitives, float empty,
            Func<(float distance, MaterialShare[] materials), (float distance, MaterialShare[] materials), (float, MaterialShare[])> combine)
        {
            if (primitives.Length == 0) return (empty, material);
            var result = Walk(primitives[0], p, material);
            for (var i = 1; i < primitives.Length; i++)
                result = combine(result, Walk(primitives[i], p, material));
            return result;
        }

        // The renderer's mixMaterial(b, a, weight): b's share shrinks as a gets nearer than b
        (float, MaterialShare[]) Blend((float distance, MaterialShare[] materials) a, (float distance, MaterialShare[] materials) b, float k, int sign,
            float distance) =>
            (distance, Mix(b.materials, a.materials, Clamp(0.5f + 0.125f * sign * (b.distance - a.distance) / MathF.Max(k, 1e-6f), 0f, 1f)));

        (float, MaterialShare[]) ScaleSurface(Scale e)
        {
            var (d, m) = Walk(e.primitive, p / Vec3(e.scale), material);
            return (d * MinComponent(Vec3(e.scale)), m);
        }

        (float, MaterialShare[]) ElongateExactInteriorSurface(ElongateExactInterior e)
        {
            var q = Vector3.Abs(p) - Vec3(e.amount);
            var (d, m) = Walk(e.primitive, Max0(q), material);
            return (d + MathF.Min(MaxComponent(q), 0f), m);
        }

        if (!prepared.ContainsColor(evaluable))
            return (Sdf(evaluable, p), material);

        switch (evaluable)
        {
            case Elongate e: return Walk(e.primitive, p - Vector3.Clamp(p, -Vec3(e.amount), Vec3(e.amount)), material);
            case Translate e: return Walk(e.primitive, p - Vec3(e.translation), material);
            case Rotate e: return Walk(e.primitive, InverseRotateXYZ(p, Vec3(e.rotation)), material);
            case SymmetryX e: return Walk(e.primitive, p with { X = MathF.Abs(p.X) }, material);
            case SymmetryXZ e: return Walk(e.primitive, p with { X = MathF.Abs(p.X), Z = MathF.Abs(p.Z) }, material);
            case Repetition e: return Walk(e.primitive, Repeat(p, Vec3(e.spacing)), material);
            case LimitedRepetition e: return Walk(e.primitive, RepeatLimited(p, Float(e.spacing), Vec3(e.limits)), material);
            case Twist e: return Walk(e.primitive, CalculateTwist(p, Float(e.amount)), material);
            case CheapBend e: return Walk(e.primitive, CalculateCheapBend(p, Float(e.amount)), material);
            case RadialWave e: return Walk(e.primitive, CalculateRadialWave(p, Float(e.amount), Float(e.count)), material);
        }

        return evaluable switch
        {
            SDFEvaluablePointerable e => Walk(context.SdfEvaluables[e.PointerId], p, material),
            VariableReferenceSDF r => SurfaceOfVariable(p, r.variableId, material, prepared, context),
            Color e => Walk(e.primitive, p, [new MaterialShare(MaterialSource.OfColor(ToV3(Vec3(e.color))), 1, p)]),
            ProgramDefinition.Material e => Walk(e.primitive, p, [new MaterialShare(MaterialSource.Named(e.material), 1, p)]),
            Round e => Modified(e.primitive, d => d - Float(e.radius)),
            Onion e => Modified(e.primitive, d => MathF.Abs(d) - Float(e.thickness)),
            Displace e => Modified(e.primitive, d => d + Sdf(e.displacement, p)),
            Distort e => Modified(e.primitive, d => d + SdfMath.Distort(p, Float(e.amount), Float(e.frequency))),
            Union e => Combine(e.primitives, float.PositiveInfinity, (a, b) => (MathF.Min(a.distance, b.distance), a.distance < b.distance ? a.materials : b.materials)),
            Intersection e => Combine(e.primitives, float.NegativeInfinity, (a, b) => (MathF.Max(a.distance, b.distance), a.distance > b.distance ? a.materials : b.materials)),
            SmoothUnion e => Combine(e.primitives, float.PositiveInfinity, (a, b) =>
                Blend(a, b, Float(e.smoothness), 1, SmoothMin(a.distance, b.distance, Float(e.smoothness)))),
            SmoothIntersection e => Combine(e.primitives, float.NegativeInfinity, (a, b) =>
                Blend(a, b, Float(e.smoothness), -1, -SmoothMin(-a.distance, -b.distance, Float(e.smoothness)))),
            Subtraction e => Modified(e.a, d => MathF.Max(d, -Sdf(e.b, p))),
            SmoothSubtraction e => Modified(e.a, d => -SmoothMin(-d, Sdf(e.b, p), Float(e.smoothness))),
            Xor e => Combine([e.a, e.b], float.PositiveInfinity, (a, b) =>
                (CalculateXor(a.distance, b.distance), MathF.Abs(a.distance) < MathF.Abs(b.distance) ? a.materials : b.materials)),
            Scale e => ScaleSurface(e),
            ElongateExactInterior e => ElongateExactInteriorSurface(e),
            _ => (Sdf(evaluable, p), material),
        };
    }

    private (float distance, MaterialShare[] materials) SurfaceOfVariable(Vector3 p, string variableId, MaterialShare[] material, Prepared prepared,
        EvaluationContext context)
    {
        var index = context.VariableIndex(variableId);
        return Surface(p, (SDFEvaluable)context.VariableValues[index], material, prepared, context with { VisibleVariableCount = index });
    }

    private static V3 ToV3(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary><paramref name="b"/> × (1 − t) + <paramref name="a"/> × t, with each material once and shares that drop to nothing left out.</summary>
    private static MaterialShare[] Mix(MaterialShare[] b, MaterialShare[] a, float t)
    {
        if (t <= 0f) return b;
        if (t >= 1f) return a;

        var mixed = new List<MaterialShare>(a.Length + b.Length);
        void Add(MaterialShare share, float scale)
        {
            var weight = share.Weight * scale;
            var index = mixed.FindIndex(s => s.Source == share.Source);
            if (index < 0)
            {
                mixed.Add(share with { Weight = weight });
                return;
            }
            var existing = mixed[index];
            var total = existing.Weight + weight;
            var local = total > 0 ? (existing.LocalPosition * existing.Weight + share.LocalPosition * weight) / total : existing.LocalPosition;
            mixed[index] = new MaterialShare(share.Source, total, local);
        }

        foreach (var share in b) Add(share, 1f - t);
        foreach (var share in a) Add(share, t);
        return mixed.ToArray();
    }
}
