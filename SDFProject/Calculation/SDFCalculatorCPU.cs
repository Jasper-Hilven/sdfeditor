using System.Numerics;
using SDFProject.ProgramDefinition;
using static SDFProject.Calculation.SdfMath;
using Heart2D = SDFProject.ProgramDefinition.Heart2D;
using Plane = SDFProject.ProgramDefinition.Plane;

namespace SDFProject.Calculation;

public partial class SdfCalculatorCpu
{
    internal sealed record FunctionInfo(SDFFunctionDefinition Definition, Dictionary<string, int> VariableIndices);

    internal readonly record struct EvaluationContext(
        object[] ParameterValues,
        object[] VariableValues,
        Dictionary<string, int> VariableIndices,
        Dictionary<string, SDFEvaluable> SdfEvaluables,
        Dictionary<string, SDF2DEvaluable> Sdf2DEvaluables,
        Dictionary<string, FloatEvaluable> FloatEvaluables,
        Dictionary<string, IntegerEvaluable> IntegerEvaluables,
        int VisibleVariableCount)
    {
        public int VariableIndex(string variableId) =>
            VariableIndices.TryGetValue(variableId, out var index) && index < VisibleVariableCount
                ? index
                : throw new InvalidOperationException($"Variable '{variableId}' is not declared before it is used");

        public object VariableValue(string variableId) => VariableValues[VariableIndex(variableId)];
    }

    public float Calculate(V3 position, SDFProgram program)
    {
        var idToFunctions = program.Functions.ToDictionary(f => f.id,
            f => new FunctionInfo(f, VariableIndices(f.variables)));

        var pointers = program.pointers;
        var variables = program.sceneVariables;
        var variableValues = new object[variables.Length];
        var context = new EvaluationContext([], variableValues, VariableIndices(variables), pointers.SDFEvaluablePointers, pointers.SDF2DEvaluablePointers,
            pointers.FloatEvaluablePointers, pointers.IntegerEvaluablePointers, 0);
        EvaluateVariables(variables, variableValues, context);
        return Calculate(new Vector3(position.x, position.y, position.z), program.scene, idToFunctions, context with { VisibleVariableCount = variables.Length });
    }

    private static Dictionary<string, int> VariableIndices(VariableDeclaration[] variables) =>
        variables.Select((v, i) => (v.id, i)).ToDictionary(x => x.id, x => x.i);

    private void EvaluateVariables(VariableDeclaration[] declarations, object[] variableValues, EvaluationContext context)
    {
        for (var i = 0; i < declarations.Length; i++)
        {
            variableValues[i] = declarations[i].value is SDFEvaluable or SDF2DEvaluable
                ? declarations[i].value
                : EvaluateValue(declarations[i].value, context with { VisibleVariableCount = i });
        }
    }

    private object EvaluateValue(Evaluable evaluable, EvaluationContext context)
    {
        return evaluable switch
        {
            FloatEvaluable e => Calculate(e, context),
            V2Evaluable e => Calculate(e, context),
            V3Evaluable e => Calculate(e, context),
            IntegerEvaluable e => Calculate(e, context),
            _ => throw new NotSupportedException($"Function arguments of type {evaluable.GetType().Name} are not supported"),
        };
    }

    private float CalculateFunctionCall(Vector3 p, SDFFunctionCall call, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext callerContext)
    {
        var function = idToFunctions[call.functionId];
        var declarations = function.Definition.variables;
        var variableValues = new object[declarations.Length];
        var context = new EvaluationContext(
            call.arguments.Select(a => EvaluateValue(a, callerContext)).ToArray(),
            variableValues,
            function.VariableIndices,
            callerContext.SdfEvaluables,
            callerContext.Sdf2DEvaluables,
            callerContext.FloatEvaluables,
            callerContext.IntegerEvaluables,
            0);

        EvaluateVariables(declarations, variableValues, context);
        return Calculate(p, function.Definition.functionBody, idToFunctions, context with { VisibleVariableCount = declarations.Length });
    }

    private float Calculate(Vector3 p, SDFEvaluable evaluable, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        float Float(FloatEvaluable e) => Calculate(e, context);
        Vector2 Vec2(V2Evaluable e) => Calculate(e, context);
        Vector3 Vec3(V3Evaluable e) => Calculate(e, context);
        float Sdf(SDFEvaluable e, Vector3 q) => Calculate(q, e, idToFunctions, context);
        float Sdf2D(SDF2DEvaluable e, Vector2 q) => Calculate(q, e, context);

        return evaluable switch
        {
            ArbitraryCappedCone e => SdfMath.ArbitraryCappedCone(p, Vec3(e.a), Vec3(e.b), Float(e.radiusA), Float(e.radiusB)),
            ArbitraryCappedCylinder e => SdfMath.ArbitraryCappedCylinder(p, Vec3(e.a), Vec3(e.b), Float(e.radius)),
            ArbitraryRoundCone e => SdfMath.ArbitraryRoundCone(p, Vec3(e.a), Vec3(e.b), Float(e.radiusA), Float(e.radiusB)),
            Box e => SdfMath.Box(p, Vec3(e.size)),
            BoxFrame e => SdfMath.BoxFrame(p, Vec3(e.size), Float(e.edgeThickness)),
            CappedCone e => SdfMath.CappedCone(p, Float(e.height), Float(e.bottomRadius), Float(e.topRadius)),
            CappedCylinder e => SdfMath.CappedCylinder(p, Float(e.radius), Float(e.height)),
            CappedTorus e => SdfMath.CappedTorus(p, Vec2(e.sinCos), Float(e.majorRadius), Float(e.minorRadius)),
            Capsule e => SdfMath.Capsule(p, Vec3(e.a), Vec3(e.b), Float(e.radius)),
            CheapBend e => Sdf(e.primitive, CalculateCheapBend(p, Float(e.amount))),
            Cone e => SdfMath.Cone(p, Vec2(e.sinCos), Float(e.height)),
            CutHollowSphere e => SdfMath.CutHollowSphere(p, Float(e.radius), Float(e.cutHeight), Float(e.thickness)),
            CutSphere e => SdfMath.CutSphere(p, Float(e.radius), Float(e.cutHeight)),
            Displace e => Sdf(e.primitive, p) + Sdf(e.displacement, p),
            Empty => float.PositiveInfinity,
            Ellipsoid e => SdfMath.Ellipsoid(p, Vec3(e.radii)),
            Elongate e => Sdf(e.primitive, p - Vector3.Clamp(p, -Vec3(e.amount), Vec3(e.amount))),
            ElongateExactInterior e => CalculateElongateExactInterior(p, e, Vec3(e.amount), idToFunctions, context),
            Extrusion e => CalculateExtrusion(p, Sdf2D(e.primitive, Xy(p)), Float(e.height)),
            HexPrism e => SdfMath.HexPrism(p, Vec2(e.size)),
            InfiniteCone e => SdfMath.InfiniteCone(p, Vec2(e.sinCos)),
            InfiniteCylinder e => SdfMath.InfiniteCylinder(p, Vec2(e.center), Float(e.radius)),
            Intersection e => CalculateIntersection(p, e, idToFunctions, context),
            LimitedRepetition e => Sdf(e.primitive, RepeatLimited(p, Float(e.spacing), Vec3(e.limits))),
            Link e => SdfMath.Link(p, Float(e.length), Float(e.majorRadius), Float(e.minorRadius)),
            Octahedron e => SdfMath.Octahedron(p, Float(e.size)),
            OctahedronBound e => SdfMath.OctahedronBound(p, Float(e.size)),
            Onion e => MathF.Abs(Sdf(e.primitive, p)) - Float(e.thickness),
            Distort e => Sdf(e.primitive, p) + SdfMath.Distort(p, Float(e.amount), Float(e.frequency)),
            Plane e => SdfMath.Plane(p, Vec3(e.normal), Float(e.height)),
            Pyramid e => SdfMath.Pyramid(p, Float(e.height)),
            Quad e => SdfMath.Quad(p, Vec3(e.a), Vec3(e.b), Vec3(e.c), Vec3(e.d)),
            Repetition e => Sdf(e.primitive, Repeat(p, Vec3(e.spacing))),
            Revolution e => Sdf2D(e.primitive, new Vector2(Length(Xz(p)) - Float(e.offset), p.Y)),
            Rhombus e => SdfMath.Rhombus(p, Float(e.lengthA), Float(e.lengthB), Float(e.height), Float(e.roundness)),
            Rotate e => Sdf(e.primitive, InverseRotateXYZ(p, Vec3(e.rotation))),
            Round e => Sdf(e.primitive, p) - Float(e.radius),
            Color e => Sdf(e.primitive, p),
            Material e => Sdf(e.primitive, p),
            RoundBox e => SdfMath.RoundBox(p, Vec3(e.size), Float(e.roundness)),
            RoundCone e => SdfMath.RoundCone(p, Float(e.bottomRadius), Float(e.topRadius), Float(e.height)),
            RoundedCylinder e => SdfMath.RoundedCylinder(p, Float(e.radius), Float(e.roundness), Float(e.height)),
            SDFFunctionCall call => CalculateFunctionCall(p, call, idToFunctions, context),
            Scale e => CalculateScale(p, e, Vec3(e.scale), idToFunctions, context),
            SmoothIntersection e => CalculateSmoothIntersection(p, e, Float(e.smoothness), idToFunctions, context),
            SmoothSubtraction e => -SmoothMin(-Sdf(e.a, p), Sdf(e.b, p), Float(e.smoothness)),
            SmoothUnion e => CalculateSmoothUnion(p, e, Float(e.smoothness), idToFunctions, context),
            SolidAngle e => SdfMath.SolidAngle(p, Vec2(e.sinCos), Float(e.radius)),
            Sphere e => SdfMath.Sphere(p, Float(e.radius)),
            Subtraction e => MathF.Max(Sdf(e.a, p), -Sdf(e.b, p)),
            SymmetryX e => Sdf(e.primitive, p with { X = MathF.Abs(p.X) }),
            SymmetryXZ e => Sdf(e.primitive, p with { X = MathF.Abs(p.X), Z = MathF.Abs(p.Z) }),
            Torus e => CalculateTorus(p, Float(e.innerRadius), Float(e.outerRadius)),
            Translate e => Sdf(e.primitive, p - Vec3(e.translation)),
            TriPrism e => SdfMath.TriPrism(p, Vec2(e.size)),
            Triangle e => SdfMath.Triangle(p, Vec3(e.a), Vec3(e.b), Vec3(e.c)),
            Twist e => Sdf(e.primitive, CalculateTwist(p, Float(e.amount))),
            RadialWave e => Sdf(e.primitive, CalculateRadialWave(p, Float(e.amount), Float(e.count))),
            Union e => CalculateUnion(p, e, idToFunctions, context),
            VariableReferenceSDF r => CalculateVariable(p, r.variableId, idToFunctions, context),
            VerticalCapsule e => SdfMath.VerticalCapsule(p, Float(e.height), Float(e.radius)),
            VesicaSegment e => SdfMath.VesicaSegment(p, Vec3(e.a), Vec3(e.b), Float(e.width)),
            Xor e => CalculateXor(Sdf(e.a, p), Sdf(e.b, p)),
            SDFEvaluablePointerable e => Calculate(p, context.SdfEvaluables[e.PointerId], idToFunctions, context),

            _ => throw new NotImplementedException($"Unknown {nameof(SDFEvaluable)}: {evaluable.GetType().Name}"),
        };
    }

    private float Calculate(Vector2 p, SDF2DEvaluable evaluable, EvaluationContext context)
    {
        float Float(FloatEvaluable e) => Calculate(e, context);
        Vector2 Vec2(V2Evaluable e) => Calculate(e, context);
        float Sdf2D(SDF2DEvaluable e, Vector2 q) => Calculate(q, e, context);

        return evaluable switch
        {
            VariableReferenceSDF2D r => CalculateVariable(p, r.variableId, context),
            SDF2DEvaluablePointerable e => Calculate(p, context.Sdf2DEvaluables[e.PointerId], context),
            Circle e => SdfMath.Circle(p, Float(e.radius)),
            Box2D e => SdfMath.Box2D(p, Vec2(e.size)),
            RoundedBox2D e => SdfMath.RoundedBox2D(p, Vec2(e.size), Float(e.radiusTopRight), Float(e.radiusBottomRight), Float(e.radiusTopLeft), Float(e.radiusBottomLeft)),
            OrientedBox2D e => SdfMath.OrientedBox2D(p, Vec2(e.a), Vec2(e.b), Float(e.thickness)),
            Segment2D e => SdfMath.Segment2D(p, Vec2(e.a), Vec2(e.b)),
            Rhombus2D e => SdfMath.Rhombus2D(p, Vec2(e.size)),
            Trapezoid2D e => SdfMath.Trapezoid2D(p, Float(e.bottomRadius), Float(e.topRadius), Float(e.height)),
            Parallelogram2D e => SdfMath.Parallelogram2D(p, Float(e.width), Float(e.height), Float(e.skew)),
            EquilateralTriangle2D e => SdfMath.EquilateralTriangle2D(p, Float(e.radius)),
            IsoscelesTriangle2D e => SdfMath.IsoscelesTriangle2D(p, Vec2(e.size)),
            Triangle2D e => SdfMath.Triangle2D(p, Vec2(e.a), Vec2(e.b), Vec2(e.c)),
            UnevenCapsule2D e => SdfMath.UnevenCapsule2D(p, Float(e.bottomRadius), Float(e.topRadius), Float(e.height)),
            Pentagon2D e => SdfMath.Pentagon2D(p, Float(e.radius)),
            Hexagon2D e => SdfMath.Hexagon2D(p, Float(e.radius)),
            Octagon2D e => SdfMath.Octagon2D(p, Float(e.radius)),
            Hexagram2D e => SdfMath.Hexagram2D(p, Float(e.radius)),
            Star2D e => SdfMath.Star2D(p, Float(e.radius), Calculate(e.points, context), Float(e.innerFactor)),
            Pie2D e => SdfMath.Pie2D(p, Vec2(e.sinCos), Float(e.radius)),
            Arc2D e => SdfMath.Arc2D(p, Vec2(e.sinCos), Float(e.radius), Float(e.thickness)),
            Ring2D e => SdfMath.Ring2D(p, Vec2(e.sinCos), Float(e.radius), Float(e.thickness)),
            Moon2D e => SdfMath.Moon2D(p, Float(e.distance), Float(e.radiusA), Float(e.radiusB)),
            Vesica2D e => SdfMath.Vesica2D(p, Float(e.width), Float(e.height)),
            Egg2D e => SdfMath.Egg2D(p, Float(e.radiusA), Float(e.radiusB)),
            Heart2D => SdfMath.Heart2D(p),
            Cross2D e => SdfMath.Cross2D(p, Vec2(e.size), Float(e.roundness)),
            RoundedX2D e => SdfMath.RoundedX2D(p, Float(e.width), Float(e.roundness)),
            Ellipse2D e => SdfMath.Ellipse2D(p, Vec2(e.radii)),
            Parabola2D e => SdfMath.Parabola2D(p, Float(e.k)),
            Horseshoe2D e => SdfMath.Horseshoe2D(p, Vec2(e.sinCos), Float(e.radius), Vec2(e.size)),

            Round2D e => Sdf2D(e.primitive, p) - Float(e.radius),
            Onion2D e => MathF.Abs(Sdf2D(e.primitive, p)) - Float(e.thickness),
            Union2D e => CalculateUnion2D(p, e, context),
            Subtraction2D e => MathF.Max(Sdf2D(e.a, p), -Sdf2D(e.b, p)),
            Intersection2D e => CalculateIntersection2D(p, e, context),
            SmoothUnion2D e => CalculateSmoothUnion2D(p, e, Float(e.smoothness), context),
            Translate2D e => Sdf2D(e.primitive, p - Vec2(e.offset)),
            Rotate2D e => Sdf2D(e.primitive, SdfMath.Rotate(p, MathF.Cos(Float(e.angle)), MathF.Sin(Float(e.angle)))),

            _ => throw new NotImplementedException($"Unknown {nameof(SDF2DEvaluable)}: {evaluable.GetType().Name}"),
        };
    }

    private float Calculate(FloatEvaluable floatEvaluable, EvaluationContext context)
    {
        return floatEvaluable switch
        {
            ConstValueFloat c => c.value,
            FunctionParameterValueFloat fp => (float)context.ParameterValues[fp.functionParameterIndex],
            VariableReferenceFloat r => (float)context.VariableValue(r.variableId),
            FloatEvaluablePointerable e => Calculate(context.FloatEvaluables[e.PointerId], context),
            _ => throw new NotImplementedException($"Unknown {nameof(FloatEvaluable)}: {floatEvaluable.GetType().Name}"),
        };
    }

    private Vector2 Calculate(V2Evaluable v2Evaluable, EvaluationContext context)
    {
        return v2Evaluable switch
        {
            ConstValueV2 c => new Vector2(c.v2.x, c.v2.y),
            FunctionParameterValueV2 fp => (Vector2)context.ParameterValues[fp.functionParameterIndex],
            VariableReferenceV2 r => (Vector2)context.VariableValue(r.variableId),
            _ => throw new NotImplementedException($"Unknown {nameof(V2Evaluable)}: {v2Evaluable.GetType().Name}"),
        };
    }

    private Vector3 Calculate(V3Evaluable v3Evaluable, EvaluationContext context)
    {
        return v3Evaluable switch
        {
            ConstValueV3 c => new Vector3(c.v3.x, c.v3.y, c.v3.z),
            FunctionParameterValueV3 fp => (Vector3)context.ParameterValues[fp.functionParameterIndex],
            VariableReferenceV3 r => (Vector3)context.VariableValue(r.variableId),
            _ => throw new NotImplementedException($"Unknown {nameof(V3Evaluable)}: {v3Evaluable.GetType().Name}"),
        };
    }

    private int Calculate(IntegerEvaluable intEvaluable, EvaluationContext context)
    {
        return intEvaluable switch
        {
            ConstValueInt c => c.value,
            FunctionParameterValueInt fp => (int)context.ParameterValues[fp.functionParameterIndex],
            VariableReferenceInt r => (int)context.VariableValue(r.variableId),
            IntegerEvaluablePointerable e => Calculate(context.IntegerEvaluables[e.PointerId], context),
            _ => throw new NotImplementedException($"Unknown {nameof(IntegerEvaluable)}: {intEvaluable.GetType().Name}"),
        };
    }

    private float CalculateVariable(Vector3 p, string variableId, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var index = context.VariableIndex(variableId);
        return Calculate(p, (SDFEvaluable)context.VariableValues[index], idToFunctions, context with { VisibleVariableCount = index });
    }

    private float CalculateVariable(Vector2 p, string variableId, EvaluationContext context)
    {
        var index = context.VariableIndex(variableId);
        return Calculate(p, (SDF2DEvaluable)context.VariableValues[index], context with { VisibleVariableCount = index });
    }

    private static float CalculateTorus(Vector3 p, float innerRadius, float outerRadius) =>
        SdfMath.Torus(p, (outerRadius + innerRadius) * 0.5f, (outerRadius - innerRadius) * 0.5f);

    private static float CalculateExtrusion(Vector3 p, float d, float h)
    {
        var w = new Vector2(d, MathF.Abs(p.Z) - h);
        return MathF.Min(MathF.Max(w.X, w.Y), 0f) + Length(Max0(w));
    }

    private float CalculateElongateExactInterior(Vector3 p, ElongateExactInterior e, Vector3 h, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var q = Vector3.Abs(p) - h;
        return Calculate(Max0(q), e.primitive, idToFunctions, context) + MathF.Min(MaxComponent(q), 0f);
    }

    private float CalculateUnion(Vector3 p, Union e, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var d = float.PositiveInfinity;
        foreach (var primitive in e.primitives)
            d = MathF.Min(d, Calculate(p, primitive, idToFunctions, context));
        return d;
    }

    private float CalculateIntersection(Vector3 p, Intersection e, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var d = float.NegativeInfinity;
        foreach (var primitive in e.primitives)
            d = MathF.Max(d, Calculate(p, primitive, idToFunctions, context));
        return d;
    }

    private float CalculateSmoothUnion(Vector3 p, SmoothUnion e, float k, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        if (e.primitives.Length == 0) return float.PositiveInfinity;
        var d = Calculate(p, e.primitives[0], idToFunctions, context);
        for (var i = 1; i < e.primitives.Length; i++)
            d = SmoothMin(d, Calculate(p, e.primitives[i], idToFunctions, context), k);
        return d;
    }

    private float CalculateSmoothIntersection(Vector3 p, SmoothIntersection e, float k, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        if (e.primitives.Length == 0) return float.NegativeInfinity;
        var d = Calculate(p, e.primitives[0], idToFunctions, context);
        for (var i = 1; i < e.primitives.Length; i++)
            d = -SmoothMin(-d, -Calculate(p, e.primitives[i], idToFunctions, context), k);
        return d;
    }

    private float CalculateUnion2D(Vector2 p, Union2D e, EvaluationContext context)
    {
        var d = float.PositiveInfinity;
        foreach (var primitive in e.primitives)
            d = MathF.Min(d, Calculate(p, primitive, context));
        return d;
    }

    private float CalculateIntersection2D(Vector2 p, Intersection2D e, EvaluationContext context)
    {
        var d = float.NegativeInfinity;
        foreach (var primitive in e.primitives)
            d = MathF.Max(d, Calculate(p, primitive, context));
        return d;
    }

    private float CalculateSmoothUnion2D(Vector2 p, SmoothUnion2D e, float k, EvaluationContext context)
    {
        if (e.primitives.Length == 0) return float.PositiveInfinity;
        var d = Calculate(p, e.primitives[0], context);
        for (var i = 1; i < e.primitives.Length; i++)
            d = SmoothMin(d, Calculate(p, e.primitives[i], context), k);
        return d;
    }

    private static float CalculateXor(float a, float b) => MathF.Max(MathF.Min(a, b), -MathF.Max(a, b));

    private float CalculateScale(Vector3 p, Scale e, Vector3 s, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context) =>
        Calculate(p / s, e.primitive, idToFunctions, context) * MinComponent(s);

    private static Vector3 InverseRotateXYZ(Vector3 p, Vector3 rotation)
    {
        var rotateXThenYThenZ = Quaternion.Concatenate(
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, rotation.X), Quaternion.CreateFromAxisAngle(Vector3.UnitY, rotation.Y)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rotation.Z));
        return Vector3.Transform(p, Quaternion.Inverse(rotateXThenYThenZ));
    }

    private static float RepeatAxis(float x, float s) => s == 0f ? x : x - s * MathF.Round(x / s, MidpointRounding.AwayFromZero);

    private static Vector3 Repeat(Vector3 p, Vector3 s) => new(RepeatAxis(p.X, s.X), RepeatAxis(p.Y, s.Y), RepeatAxis(p.Z, s.Z));

    private static Vector3 RepeatLimited(Vector3 p, float s, Vector3 l)
    {
        if (s == 0f) return p;
        var id = new Vector3(
            MathF.Round(p.X / s, MidpointRounding.AwayFromZero),
            MathF.Round(p.Y / s, MidpointRounding.AwayFromZero),
            MathF.Round(p.Z / s, MidpointRounding.AwayFromZero));
        return p - s * Vector3.Clamp(id, -l, l);
    }

    private static Vector3 CalculateRadialWave(Vector3 p, float amount, float count)
    {
        var r = Length(Xz(p));
        return r < 1e-5f ? p : p with { Y = p.Y - amount * r * MathF.Sin(count * MathF.Atan2(p.Z, p.X)) };
    }

    private static Vector3 CalculateTwist(Vector3 p, float k)
    {
        var xz = SdfMath.Rotate(Xz(p), MathF.Cos(k * p.Y), MathF.Sin(k * p.Y));
        return new Vector3(xz.X, p.Y, xz.Y);
    }

    private static Vector3 CalculateCheapBend(Vector3 p, float k)
    {
        var xy = SdfMath.Rotate(Xy(p), MathF.Cos(k * p.X), MathF.Sin(k * p.X));
        return new Vector3(xy.X, xy.Y, p.Z);
    }
}
