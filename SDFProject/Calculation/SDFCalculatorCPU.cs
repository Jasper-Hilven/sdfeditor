using System.Numerics;
using SDFProject.Definitions;
using static SDFProject.Calculation.SDFMath;
using Heart2D = SDFProject.Definitions.Heart2D;
using Plane = SDFProject.Definitions.Plane;

namespace SDFProject.Calculation;

public class SDFCalculatorCPU
{
    private sealed record FunctionInfo(SDFFunctionDefinition definition, Dictionary<string, int> variableIndices);

    private readonly record struct EvaluationContext(
        object[] parameterValues,
        object[] variableValues,
        Dictionary<string, int> variableIndices,
        Dictionary<string, SDFEvaluable> SdfEvaluables,
        Dictionary<string, SDF2DEvaluable> Sdf2DEvaluables,
        Dictionary<string, FloatEvaluable> FloatEvaluables,
        Dictionary<string, IntegerEvaluable> IntegerEvaluables,
        int visibleVariableCount)
    {

        public int VariableIndex(string variableId) =>
            variableIndices.TryGetValue(variableId, out var index) && index < visibleVariableCount
                ? index
                : throw new InvalidOperationException($"Variable '{variableId}' is not declared before it is used");

        public object VariableValue(string variableId) => variableValues[VariableIndex(variableId)];
    }

    public float Calculate(V3 position, SDFProgram program)
    {
        var idToFunctions = program.functions.ToDictionary(f => f.id,
            f => new FunctionInfo(f, f.variables.Select((v, i) => (v.id, i)).ToDictionary(x => x.id, x => x.i)));

        var pointers = program.pointers;
        var context = new EvaluationContext([], [], new Dictionary<string, int>(), pointers.SDFEvaluablePointers, pointers.SDF2DEvaluablePointers,
            pointers.FloatEvaluablePointers, pointers.IntegerEvaluablePointers, 0);
        return Calculate(new Vector3(position.x, position.y, position.z), program.scene, idToFunctions, context);
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
        var declarations = function.definition.variables;
        var variableValues = new object[declarations.Length];
        var context = new EvaluationContext(
            call.arguments.Select(a => EvaluateValue(a, callerContext)).ToArray(),
            variableValues,
            function.variableIndices,
            callerContext.SdfEvaluables,
            callerContext.Sdf2DEvaluables,
            callerContext.FloatEvaluables,
            callerContext.IntegerEvaluables,
            0);

        for (var i = 0; i < declarations.Length; i++)
        {
            variableValues[i] = declarations[i].value is SDFEvaluable or SDF2DEvaluable
                ? declarations[i].value
                : EvaluateValue(declarations[i].value, context with { visibleVariableCount = i });
        }

        return Calculate(p, function.definition.functionBody, idToFunctions, context with { visibleVariableCount = declarations.Length });
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
            ArbitraryCappedCone e => SDFMath.ArbitraryCappedCone(p, Vec3(e.a), Vec3(e.b), Float(e.radiusA), Float(e.radiusB)),
            ArbitraryCappedCylinder e => SDFMath.ArbitraryCappedCylinder(p, Vec3(e.a), Vec3(e.b), Float(e.radius)),
            ArbitraryRoundCone e => SDFMath.ArbitraryRoundCone(p, Vec3(e.a), Vec3(e.b), Float(e.radiusA), Float(e.radiusB)),
            Box e => SDFMath.Box(p, Vec3(e.size)),
            BoxFrame e => SDFMath.BoxFrame(p, Vec3(e.size), Float(e.edgeThickness)),
            CappedCone e => SDFMath.CappedCone(p, Float(e.height), Float(e.bottomRadius), Float(e.topRadius)),
            CappedCylinder e => SDFMath.CappedCylinder(p, Float(e.radius), Float(e.height)),
            CappedTorus e => SDFMath.CappedTorus(p, Vec2(e.sinCos), Float(e.majorRadius), Float(e.minorRadius)),
            Capsule e => SDFMath.Capsule(p, Vec3(e.a), Vec3(e.b), Float(e.radius)),
            CheapBend e => Sdf(e.primitive, CalculateCheapBend(p, Float(e.amount))),
            Cone e => SDFMath.Cone(p, Vec2(e.sinCos), Float(e.height)),
            CutHollowSphere e => SDFMath.CutHollowSphere(p, Float(e.radius), Float(e.cutHeight), Float(e.thickness)),
            CutSphere e => SDFMath.CutSphere(p, Float(e.radius), Float(e.cutHeight)),
            DeathStar e => SDFMath.DeathStar(p, Float(e.radiusA), Float(e.radiusB), Float(e.distance)),
            Displace e => Sdf(e.primitive, p) + Sdf(e.displacement, p),
            Empty => float.PositiveInfinity,
            Ellipsoid e => SDFMath.Ellipsoid(p, Vec3(e.radii)),
            Elongate e => Sdf(e.primitive, p - Vector3.Clamp(p, -Vec3(e.amount), Vec3(e.amount))),
            ElongateExactInterior e => CalculateElongateExactInterior(p, e, Vec3(e.amount), idToFunctions, context),
            Extrusion e => CalculateExtrusion(p, Sdf2D(e.primitive, XY(p)), Float(e.height)),
            HexPrism e => SDFMath.HexPrism(p, Vec2(e.size)),
            InfiniteCone e => SDFMath.InfiniteCone(p, Vec2(e.sinCos)),
            InfiniteCylinder e => SDFMath.InfiniteCylinder(p, Vec2(e.center), Float(e.radius)),
            Intersection e => CalculateIntersection(p, e, idToFunctions, context),
            LimitedRepetition e => Sdf(e.primitive, RepeatLimited(p, Float(e.spacing), Vec3(e.limits))),
            Link e => SDFMath.Link(p, Float(e.length), Float(e.majorRadius), Float(e.minorRadius)),
            Octahedron e => SDFMath.Octahedron(p, Float(e.size)),
            OctahedronBound e => SDFMath.OctahedronBound(p, Float(e.size)),
            Onion e => MathF.Abs(Sdf(e.primitive, p)) - Float(e.thickness),
            Plane e => SDFMath.Plane(p, Vec3(e.normal), Float(e.height)),
            Pyramid e => SDFMath.Pyramid(p, Float(e.height)),
            Quad e => SDFMath.Quad(p, Vec3(e.a), Vec3(e.b), Vec3(e.c), Vec3(e.d)),
            Repetition e => Sdf(e.primitive, Repeat(p, Vec3(e.spacing))),
            Revolution e => Sdf2D(e.primitive, new Vector2(Length(XZ(p)) - Float(e.offset), p.Y)),
            Rhombus e => SDFMath.Rhombus(p, Float(e.lengthA), Float(e.lengthB), Float(e.height), Float(e.roundness)),
            Round e => Sdf(e.primitive, p) - Float(e.radius),
            RoundBox e => SDFMath.RoundBox(p, Vec3(e.size), Float(e.roundness)),
            RoundCone e => SDFMath.RoundCone(p, Float(e.bottomRadius), Float(e.topRadius), Float(e.height)),
            RoundedCylinder e => SDFMath.RoundedCylinder(p, Float(e.radius), Float(e.roundness), Float(e.height)),
            SDFFunctionCall call => CalculateFunctionCall(p, call, idToFunctions, context),
            ScaleTranslateRotate e => CalculateScaleTranslateRotate(p, e, Float(e.scale), Vec3(e.translation), Vec3(e.rotation), idToFunctions, context),
            SmoothIntersection e => CalculateSmoothIntersection(p, e, Float(e.smoothness), idToFunctions, context),
            SmoothSubtraction e => -SmoothMin(-Sdf(e.a, p), Sdf(e.b, p), Float(e.smoothness)),
            SmoothUnion e => CalculateSmoothUnion(p, e, Float(e.smoothness), idToFunctions, context),
            SolidAngle e => SDFMath.SolidAngle(p, Vec2(e.sinCos), Float(e.radius)),
            Sphere e => SDFMath.Sphere(p, Float(e.radius)),
            Subtraction e => MathF.Max(Sdf(e.a, p), -Sdf(e.b, p)),
            SymmetryX e => Sdf(e.primitive, p with { X = MathF.Abs(p.X) }),
            SymmetryXZ e => Sdf(e.primitive, p with { X = MathF.Abs(p.X), Z = MathF.Abs(p.Z) }),
            Torus e => CalculateTorus(p, Float(e.innerRadius), Float(e.outerRadius)),
            TriPrism e => SDFMath.TriPrism(p, Vec2(e.size)),
            Triangle e => SDFMath.Triangle(p, Vec3(e.a), Vec3(e.b), Vec3(e.c)),
            Twist e => Sdf(e.primitive, CalculateTwist(p, Float(e.amount))),
            Union e => CalculateUnion(p, e, idToFunctions, context),
            VariableReferenceSDF r => CalculateVariable(p, r.variableId, idToFunctions, context),
            VerticalCapsule e => SDFMath.VerticalCapsule(p, Float(e.height), Float(e.radius)),
            VesicaSegment e => SDFMath.VesicaSegment(p, Vec3(e.a), Vec3(e.b), Float(e.width)),
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
            Circle e => SDFMath.Circle(p, Float(e.radius)),
            Box2D e => SDFMath.Box2D(p, Vec2(e.size)),
            RoundedBox2D e => SDFMath.RoundedBox2D(p, Vec2(e.size), Float(e.radiusTopRight), Float(e.radiusBottomRight), Float(e.radiusTopLeft), Float(e.radiusBottomLeft)),
            OrientedBox2D e => SDFMath.OrientedBox2D(p, Vec2(e.a), Vec2(e.b), Float(e.thickness)),
            Segment2D e => SDFMath.Segment2D(p, Vec2(e.a), Vec2(e.b)),
            Rhombus2D e => SDFMath.Rhombus2D(p, Vec2(e.size)),
            Trapezoid2D e => SDFMath.Trapezoid2D(p, Float(e.bottomRadius), Float(e.topRadius), Float(e.height)),
            Parallelogram2D e => SDFMath.Parallelogram2D(p, Float(e.width), Float(e.height), Float(e.skew)),
            EquilateralTriangle2D e => SDFMath.EquilateralTriangle2D(p, Float(e.radius)),
            IsoscelesTriangle2D e => SDFMath.IsoscelesTriangle2D(p, Vec2(e.size)),
            Triangle2D e => SDFMath.Triangle2D(p, Vec2(e.a), Vec2(e.b), Vec2(e.c)),
            UnevenCapsule2D e => SDFMath.UnevenCapsule2D(p, Float(e.bottomRadius), Float(e.topRadius), Float(e.height)),
            Pentagon2D e => SDFMath.Pentagon2D(p, Float(e.radius)),
            Hexagon2D e => SDFMath.Hexagon2D(p, Float(e.radius)),
            Octagon2D e => SDFMath.Octagon2D(p, Float(e.radius)),
            Hexagram2D e => SDFMath.Hexagram2D(p, Float(e.radius)),
            Star2D e => SDFMath.Star2D(p, Float(e.radius), Calculate(e.points, context), Float(e.innerFactor)),
            Pie2D e => SDFMath.Pie2D(p, Vec2(e.sinCos), Float(e.radius)),
            Arc2D e => SDFMath.Arc2D(p, Vec2(e.sinCos), Float(e.radius), Float(e.thickness)),
            Ring2D e => SDFMath.Ring2D(p, Vec2(e.sinCos), Float(e.radius), Float(e.thickness)),
            Moon2D e => SDFMath.Moon2D(p, Float(e.distance), Float(e.radiusA), Float(e.radiusB)),
            Vesica2D e => SDFMath.Vesica2D(p, Float(e.width), Float(e.height)),
            Egg2D e => SDFMath.Egg2D(p, Float(e.radiusA), Float(e.radiusB)),
            Heart2D => SDFMath.Heart2D(p),
            Cross2D e => SDFMath.Cross2D(p, Vec2(e.size), Float(e.roundness)),
            RoundedX2D e => SDFMath.RoundedX2D(p, Float(e.width), Float(e.roundness)),
            Ellipse2D e => SDFMath.Ellipse2D(p, Vec2(e.radii)),
            Parabola2D e => SDFMath.Parabola2D(p, Float(e.k)),
            Horseshoe2D e => SDFMath.Horseshoe2D(p, Vec2(e.sinCos), Float(e.radius), Vec2(e.size)),

            Round2D e => Sdf2D(e.primitive, p) - Float(e.radius),
            Onion2D e => MathF.Abs(Sdf2D(e.primitive, p)) - Float(e.thickness),
            Union2D e => CalculateUnion2D(p, e, context),
            Subtraction2D e => MathF.Max(Sdf2D(e.a, p), -Sdf2D(e.b, p)),
            Intersection2D e => CalculateIntersection2D(p, e, context),
            SmoothUnion2D e => CalculateSmoothUnion2D(p, e, Float(e.smoothness), context),
            Translate2D e => Sdf2D(e.primitive, p - Vec2(e.offset)),
            Rotate2D e => Sdf2D(e.primitive, SDFMath.Rotate(p, MathF.Cos(Float(e.angle)), MathF.Sin(Float(e.angle)))),

            _ => throw new NotImplementedException($"Unknown {nameof(SDF2DEvaluable)}: {evaluable.GetType().Name}"),
        };
    }

    private float Calculate(FloatEvaluable floatEvaluable, EvaluationContext context)
    {
        return floatEvaluable switch
        {
            ConstValueFloat c => c.value,
            FunctionParameterValueFloat fp => (float)context.parameterValues[fp.functionParameterIndex],
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
            FunctionParameterValueV2 fp => (Vector2)context.parameterValues[fp.functionParameterIndex],
            VariableReferenceV2 r => (Vector2)context.VariableValue(r.variableId),
            _ => throw new NotImplementedException($"Unknown {nameof(V2Evaluable)}: {v2Evaluable.GetType().Name}"),
        };
    }

    private Vector3 Calculate(V3Evaluable v3Evaluable, EvaluationContext context)
    {
        return v3Evaluable switch
        {
            ConstValueV3 c => new Vector3(c.v3.x, c.v3.y, c.v3.z),
            FunctionParameterValueV3 fp => (Vector3)context.parameterValues[fp.functionParameterIndex],
            VariableReferenceV3 r => (Vector3)context.VariableValue(r.variableId),
            _ => throw new NotImplementedException($"Unknown {nameof(V3Evaluable)}: {v3Evaluable.GetType().Name}"),
        };
    }

    private int Calculate(IntegerEvaluable intEvaluable, EvaluationContext context)
    {
        return intEvaluable switch
        {
            ConstValueInt c => c.value,
            FunctionParameterValueInt fp => (int)context.parameterValues[fp.functionParameterIndex],
            VariableReferenceInt r => (int)context.VariableValue(r.variableId),
            IntegerEvaluablePointerable e => Calculate(context.IntegerEvaluables[e.PointerId], context),
            _ => throw new NotImplementedException($"Unknown {nameof(IntegerEvaluable)}: {intEvaluable.GetType().Name}"),
        };
    }

    private float CalculateVariable(Vector3 p, string variableId, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var index = context.VariableIndex(variableId);
        return Calculate(p, (SDFEvaluable)context.variableValues[index], idToFunctions, context with { visibleVariableCount = index });
    }

    private float CalculateVariable(Vector2 p, string variableId, EvaluationContext context)
    {
        var index = context.VariableIndex(variableId);
        return Calculate(p, (SDF2DEvaluable)context.variableValues[index], context with { visibleVariableCount = index });
    }

    // innerRadius/outerRadius are the radii of the hole and the outer rim of the torus
    private static float CalculateTorus(Vector3 p, float innerRadius, float outerRadius) =>
        SDFMath.Torus(p, (outerRadius + innerRadius) * 0.5f, (outerRadius - innerRadius) * 0.5f);

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

    // An empty union contains nothing, so it is infinitely far away
    private float CalculateUnion(Vector3 p, Union e, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var d = float.PositiveInfinity;
        foreach (var primitive in e.primitives)
            d = MathF.Min(d, Calculate(p, primitive, idToFunctions, context));
        return d;
    }

    // An empty intersection is unconstrained, so it contains everything
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

    // Undoes the transformation in reverse order: translate, then rotate, then scale
    private float CalculateScaleTranslateRotate(Vector3 p, ScaleTranslateRotate e, float s, Vector3 translation, Vector3 rotation, Dictionary<string, FunctionInfo> idToFunctions, EvaluationContext context)
    {
        var rotateXThenYThenZ = Quaternion.Concatenate(
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, rotation.X), Quaternion.CreateFromAxisAngle(Vector3.UnitY, rotation.Y)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rotation.Z));
        var q = Vector3.Transform(p - translation, Quaternion.Inverse(rotateXThenYThenZ));
        return Calculate(q / s, e.primitive, idToFunctions, context) * s;
    }

    // A spacing of 0 on an axis disables repetition along that axis
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

    private static Vector3 CalculateTwist(Vector3 p, float k)
    {
        var xz = SDFMath.Rotate(XZ(p), MathF.Cos(k * p.Y), MathF.Sin(k * p.Y));
        return new Vector3(xz.X, p.Y, xz.Y);
    }

    private static Vector3 CalculateCheapBend(Vector3 p, float k)
    {
        var xy = SDFMath.Rotate(XY(p), MathF.Cos(k * p.X), MathF.Sin(k * p.X));
        return new Vector3(xy.X, xy.Y, p.Z);
    }
}
