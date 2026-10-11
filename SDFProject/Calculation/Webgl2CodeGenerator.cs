using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.Calculation;

public partial class Webgl2CodeGenerator
{
    public const string SceneFunctionName = "sdf_scene";

    public string[] generateSdfCode(SDFProgram program) => Generate(program).functions;

    /// <summary>The library methods the generated functions use, followed by those functions, ready to paste into a shader.</summary>
    public string GenerateShaderCode(SDFProgram program)
    {
        var (functions, context) = Generate(program);
        return string.Join("\n\n", functions.Prepend(Webgl2SdfLibrary.CodeFor(context.UsedLibraryMethods.Keys)));
    }

    /// <summary>How often each method or constant of <see cref="Webgl2SdfLibrary"/> occurs in the generated functions.</summary>
    public IReadOnlyDictionary<string, int> CountLibraryMethodUses(SDFProgram program) => Generate(program).context.UsedLibraryMethods;

    private static (string[] functions, GeneratorContext context) Generate(SDFProgram program)
    {
        var context = new GeneratorContext(program, FunctionNames(program), ParameterTypes(program), []);

        var code = OrderCalleesFirst(program).Select(f => GenerateFunction(context, f)).ToList();
        code.Add(GenerateScene(context, program.scene));
        return (code.ToArray(), context);
    }

    private sealed record GeneratorContext(
        SDFProgram Program,
        Dictionary<string, string> FunctionNames,
        Dictionary<string, string[]> ParameterTypes,
        Dictionary<string, int> UsedLibraryMethods)
    {
        public void CountLibraryMethodUses(string line)
        {
            foreach (Match identifier in IdentifierPattern().Matches(line))
            {
                if (Webgl2SdfLibrary.Contains(identifier.Value))
                    UsedLibraryMethods[identifier.Value] = UsedLibraryMethods.GetValueOrDefault(identifier.Value) + 1;
            }
        }
    }

    private static string GenerateFunction(GeneratorContext context, SDFFunctionDefinition function)
    {
        var types = context.ParameterTypes[function.id];
        var parameterNames = types
            .Select((_, i) => Identifier($"a{i}_{(i < function.argumentNames.Length ? function.argumentNames[i] : "")}"))
            .ToArray();
        var writer = new FunctionWriter(context, parameterNames);

        for (var i = 0; i < function.variables.Length; i++)
        {
            var variable = function.variables[i];
            var name = Identifier($"v{i}_{variable.name}");
            writer.Declare(variable, name);
            if (variable.value is not (SDFEvaluable or SDF2DEvaluable))
                writer.Line($"{Type(variable.value)} {name} = {writer.Value(variable.value)};");
        }

        writer.Line($"return {writer.Sdf(function.functionBody, "p")};");

        var parameters = string.Concat(types.Select((type, i) => $", {type} {parameterNames[i]}"));
        return writer.ToFunction($"float {context.FunctionNames[function.id]}(vec3 p{parameters})");
    }

    private static string GenerateScene(GeneratorContext context, SDFEvaluable scene)
    {
        var writer = new FunctionWriter(context, []);
        writer.Line($"return {writer.Sdf(scene, "p")};");
        return writer.ToFunction($"float {SceneFunctionName}(vec3 p)");
    }

    private sealed class FunctionWriter(GeneratorContext context, string[] parameterNames)
    {
        private readonly SDFPointers pointers = context.Program.pointers;
        private readonly List<string> lines = [];
        private readonly Dictionary<string, (VariableDeclaration declaration, string name)> variables = [];
        private readonly HashSet<string> inlining = [];
        private int nextTemporary;

        public void Line(string line)
        {
            context.CountLibraryMethodUses(line);
            lines.Add(line);
        }

        public void Declare(VariableDeclaration variable, string name) => variables[variable.id] = (variable, name);

        public string ToFunction(string signature)
        {
            var code = new StringBuilder();
            code.Append(signature).Append("\n{\n");
            foreach (var line in lines)
                code.Append("    ").Append(line).Append('\n');
            return code.Append('}').ToString();
        }

        private string Temporary(string type, string prefix, string expression)
        {
            var name = $"{prefix}{nextTemporary++}";
            Line($"{type} {name} = {expression};");
            return name;
        }

        private string Distance(string expression) => Temporary("float", "d", expression);
        private string Position3(string expression) => Temporary("vec3", "p", expression);
        private string Position2(string expression) => Temporary("vec2", "q", expression);

        public string Sdf(SDFEvaluable evaluable, string p)
        {
            string F(FloatEvaluable e) => Float(e);
            string V2(V2Evaluable e) => Vec2(e);
            string V3(V3Evaluable e) => Vec3(e);

            return evaluable switch
            {
                SDFEvaluablePointerable e => Sdf(pointers.SDFEvaluablePointers[e.PointerId], p),
                VariableReferenceSDF r => Inline(r.variableId, value => Sdf((SDFEvaluable)value, p)),

                ArbitraryCappedCone e => Distance($"sdArbitraryCappedCone({p}, {V3(e.a)}, {V3(e.b)}, {F(e.radiusA)}, {F(e.radiusB)})"),
                ArbitraryCappedCylinder e => Distance($"sdArbitraryCappedCylinder({p}, {V3(e.a)}, {V3(e.b)}, {F(e.radius)})"),
                ArbitraryRoundCone e => Distance($"sdArbitraryRoundCone({p}, {V3(e.a)}, {V3(e.b)}, {F(e.radiusA)}, {F(e.radiusB)})"),
                Box e => Distance($"sdBox({p}, {V3(e.size)})"),
                BoxFrame e => Distance($"sdBoxFrame({p}, {V3(e.size)}, {F(e.edgeThickness)})"),
                CappedCone e => Distance($"sdCappedCone({p}, {F(e.height)}, {F(e.bottomRadius)}, {F(e.topRadius)})"),
                CappedCylinder e => Distance($"sdCappedCylinder({p}, {F(e.radius)}, {F(e.height)})"),
                CappedTorus e => Distance($"sdCappedTorus({p}, {V2(e.sinCos)}, {F(e.majorRadius)}, {F(e.minorRadius)})"),
                Capsule e => Distance($"sdCapsule({p}, {V3(e.a)}, {V3(e.b)}, {F(e.radius)})"),
                Cone e => Distance($"sdCone({p}, {V2(e.sinCos)}, {F(e.height)})"),
                CutHollowSphere e => Distance($"sdCutHollowSphere({p}, {F(e.radius)}, {F(e.cutHeight)}, {F(e.thickness)})"),
                CutSphere e => Distance($"sdCutSphere({p}, {F(e.radius)}, {F(e.cutHeight)})"),
                DeathStar e => Distance($"sdDeathStar({p}, {F(e.radiusA)}, {F(e.radiusB)}, {F(e.distance)})"),
                Ellipsoid e => Distance($"sdEllipsoid({p}, {V3(e.radii)})"),
                Empty => Distance("SDF_INFINITY"),
                HexPrism e => Distance($"sdHexPrism({p}, {V2(e.size)})"),
                InfiniteCone e => Distance($"sdInfiniteCone({p}, {V2(e.sinCos)})"),
                InfiniteCylinder e => Distance($"sdInfiniteCylinder({p}, {V2(e.center)}, {F(e.radius)})"),
                Link e => Distance($"sdLink({p}, {F(e.length)}, {F(e.majorRadius)}, {F(e.minorRadius)})"),
                Octahedron e => Distance($"sdOctahedron({p}, {F(e.size)})"),
                OctahedronBound e => Distance($"sdOctahedronBound({p}, {F(e.size)})"),
                Plane e => Distance($"sdPlane({p}, {V3(e.normal)}, {F(e.height)})"),
                Pyramid e => Distance($"sdPyramid({p}, {F(e.height)})"),
                Quad e => Distance($"sdQuad({p}, {V3(e.a)}, {V3(e.b)}, {V3(e.c)}, {V3(e.d)})"),
                Rhombus e => Distance($"sdRhombus({p}, {F(e.lengthA)}, {F(e.lengthB)}, {F(e.height)}, {F(e.roundness)})"),
                RoundBox e => Distance($"sdRoundBox({p}, {V3(e.size)}, {F(e.roundness)})"),
                RoundCone e => Distance($"sdRoundCone({p}, {F(e.bottomRadius)}, {F(e.topRadius)}, {F(e.height)})"),
                RoundedCylinder e => Distance($"sdRoundedCylinder({p}, {F(e.radius)}, {F(e.roundness)}, {F(e.height)})"),
                SolidAngle e => Distance($"sdSolidAngle({p}, {V2(e.sinCos)}, {F(e.radius)})"),
                Sphere e => Distance($"sdSphere({p}, {F(e.radius)})"),
                // innerRadius/outerRadius are the radii of the hole and the outer rim of the torus
                Torus e => Distance($"sdTorus({p}, ({F(e.outerRadius)} + {F(e.innerRadius)}) * 0.5, ({F(e.outerRadius)} - {F(e.innerRadius)}) * 0.5)"),
                TriPrism e => Distance($"sdTriPrism({p}, {V2(e.size)})"),
                Triangle e => Distance($"sdTriangle({p}, {V3(e.a)}, {V3(e.b)}, {V3(e.c)})"),
                VerticalCapsule e => Distance($"sdVerticalCapsule({p}, {F(e.height)}, {F(e.radius)})"),
                VesicaSegment e => Distance($"sdVesicaSegment({p}, {V3(e.a)}, {V3(e.b)}, {F(e.width)})"),

                Revolution e => Sdf2D(e.primitive, Position2($"vec2(length({p}.xz) - {F(e.offset)}, {p}.y)")),
                Extrusion e => Distance($"opExtrusion({p}, {Sdf2D(e.primitive, Position2($"{p}.xy"))}, {F(e.height)})"),

                Elongate e => Sdf(e.primitive, Position3($"{p} - clamp({p}, -{V3(e.amount)}, {V3(e.amount)})")),
                ElongateExactInterior e => ElongateExactInterior(e, p),
                Round e => Distance($"{Sdf(e.primitive, p)} - {F(e.radius)}"),
                Onion e => Distance($"abs({Sdf(e.primitive, p)}) - {F(e.thickness)}"),

                Union e => Combine(e.primitives, p, (a, b) => $"min({a}, {b})", "SDF_INFINITY"),
                Intersection e => Combine(e.primitives, p, (a, b) => $"max({a}, {b})", "-SDF_INFINITY"),
                Subtraction e => Distance($"max({Sdf(e.a, p)}, -{Sdf(e.b, p)})"),
                Xor e => Distance($"opXor({Sdf(e.a, p)}, {Sdf(e.b, p)})"),
                SmoothUnion e => Combine(e.primitives, p, (a, b) => $"opSmoothMin({a}, {b}, {F(e.smoothness)})", "SDF_INFINITY"),
                SmoothIntersection e => Combine(e.primitives, p, (a, b) => $"-opSmoothMin(-{a}, -{b}, {F(e.smoothness)})", "-SDF_INFINITY"),
                SmoothSubtraction e => Distance($"-opSmoothMin(-{Sdf(e.a, p)}, {Sdf(e.b, p)}, {F(e.smoothness)})"),

                ScaleTranslateRotate e => ScaleTranslateRotate(e, p),

                SymmetryX e => Sdf(e.primitive, Position3($"vec3(abs({p}.x), {p}.yz)")),
                SymmetryXZ e => Sdf(e.primitive, Position3($"vec3(abs({p}.x), {p}.y, abs({p}.z))")),
                Repetition e => Sdf(e.primitive, Position3($"opRepeat({p}, {V3(e.spacing)})")),
                LimitedRepetition e => Sdf(e.primitive, Position3($"opRepeatLimited({p}, {F(e.spacing)}, {V3(e.limits)})")),

                Displace e => Distance($"{Sdf(e.primitive, p)} + {Sdf(e.displacement, p)}"),
                Twist e => Sdf(e.primitive, Position3($"opTwist({p}, {F(e.amount)})")),
                CheapBend e => Sdf(e.primitive, Position3($"opCheapBend({p}, {F(e.amount)})")),

                SDFFunctionCall call => Distance($"{FunctionName(call.functionId)}({p}{string.Concat(call.arguments.Select(a => ", " + Value(a)))})"),

                _ => throw new NotImplementedException($"Unknown {nameof(SDFEvaluable)}: {evaluable.GetType().Name}"),
            };
        }

        private string Sdf2D(SDF2DEvaluable evaluable, string p)
        {
            string F(FloatEvaluable e) => Float(e);
            string V2(V2Evaluable e) => Vec2(e);

            return evaluable switch
            {
                SDF2DEvaluablePointerable e => Sdf2D(pointers.SDF2DEvaluablePointers[e.PointerId], p),
                VariableReferenceSDF2D r => Inline(r.variableId, value => Sdf2D((SDF2DEvaluable)value, p)),

                Circle e => Distance($"sdCircle({p}, {F(e.radius)})"),
                Box2D e => Distance($"sdBox2D({p}, {V2(e.size)})"),
                RoundedBox2D e => Distance($"sdRoundedBox2D({p}, {V2(e.size)}, {F(e.radiusTopRight)}, {F(e.radiusBottomRight)}, {F(e.radiusTopLeft)}, {F(e.radiusBottomLeft)})"),
                OrientedBox2D e => Distance($"sdOrientedBox2D({p}, {V2(e.a)}, {V2(e.b)}, {F(e.thickness)})"),
                Segment2D e => Distance($"sdSegment2D({p}, {V2(e.a)}, {V2(e.b)})"),
                Rhombus2D e => Distance($"sdRhombus2D({p}, {V2(e.size)})"),
                Trapezoid2D e => Distance($"sdTrapezoid2D({p}, {F(e.bottomRadius)}, {F(e.topRadius)}, {F(e.height)})"),
                Parallelogram2D e => Distance($"sdParallelogram2D({p}, {F(e.width)}, {F(e.height)}, {F(e.skew)})"),
                EquilateralTriangle2D e => Distance($"sdEquilateralTriangle2D({p}, {F(e.radius)})"),
                IsoscelesTriangle2D e => Distance($"sdIsoscelesTriangle2D({p}, {V2(e.size)})"),
                Triangle2D e => Distance($"sdTriangle2D({p}, {V2(e.a)}, {V2(e.b)}, {V2(e.c)})"),
                UnevenCapsule2D e => Distance($"sdUnevenCapsule2D({p}, {F(e.bottomRadius)}, {F(e.topRadius)}, {F(e.height)})"),
                Pentagon2D e => Distance($"sdPentagon2D({p}, {F(e.radius)})"),
                Hexagon2D e => Distance($"sdHexagon2D({p}, {F(e.radius)})"),
                Octagon2D e => Distance($"sdOctagon2D({p}, {F(e.radius)})"),
                Hexagram2D e => Distance($"sdHexagram2D({p}, {F(e.radius)})"),
                Star2D e => Distance($"sdStar2D({p}, {F(e.radius)}, {Int(e.points)}, {F(e.innerFactor)})"),
                Pie2D e => Distance($"sdPie2D({p}, {V2(e.sinCos)}, {F(e.radius)})"),
                Arc2D e => Distance($"sdArc2D({p}, {V2(e.sinCos)}, {F(e.radius)}, {F(e.thickness)})"),
                Ring2D e => Distance($"sdRing2D({p}, {V2(e.sinCos)}, {F(e.radius)}, {F(e.thickness)})"),
                Moon2D e => Distance($"sdMoon2D({p}, {F(e.distance)}, {F(e.radiusA)}, {F(e.radiusB)})"),
                Vesica2D e => Distance($"sdVesica2D({p}, {F(e.width)}, {F(e.height)})"),
                Egg2D e => Distance($"sdEgg2D({p}, {F(e.radiusA)}, {F(e.radiusB)})"),
                Heart2D => Distance($"sdHeart2D({p})"),
                Cross2D e => Distance($"sdCross2D({p}, {V2(e.size)}, {F(e.roundness)})"),
                RoundedX2D e => Distance($"sdRoundedX2D({p}, {F(e.width)}, {F(e.roundness)})"),
                Ellipse2D e => Distance($"sdEllipse2D({p}, {V2(e.radii)})"),
                Parabola2D e => Distance($"sdParabola2D({p}, {F(e.k)})"),
                Horseshoe2D e => Distance($"sdHorseshoe2D({p}, {V2(e.sinCos)}, {F(e.radius)}, {V2(e.size)})"),

                Round2D e => Distance($"{Sdf2D(e.primitive, p)} - {F(e.radius)}"),
                Onion2D e => Distance($"abs({Sdf2D(e.primitive, p)}) - {F(e.thickness)}"),
                Union2D e => Combine2D(e.primitives, p, (a, b) => $"min({a}, {b})", "SDF_INFINITY"),
                Subtraction2D e => Distance($"max({Sdf2D(e.a, p)}, -{Sdf2D(e.b, p)})"),
                Intersection2D e => Combine2D(e.primitives, p, (a, b) => $"max({a}, {b})", "-SDF_INFINITY"),
                SmoothUnion2D e => Combine2D(e.primitives, p, (a, b) => $"opSmoothMin({a}, {b}, {F(e.smoothness)})", "SDF_INFINITY"),
                Translate2D e => Sdf2D(e.primitive, Position2($"{p} - {V2(e.offset)}")),
                Rotate2D e => Sdf2D(e.primitive, Position2($"opRotate2D({p}, cos({F(e.angle)}), sin({F(e.angle)}))")),

                _ => throw new NotImplementedException($"Unknown {nameof(SDF2DEvaluable)}: {evaluable.GetType().Name}"),
            };
        }

        private string ElongateExactInterior(ElongateExactInterior e, string p)
        {
            var q = Position3($"abs({p}) - {Vec3(e.amount)}");
            var d = Sdf(e.primitive, Position3($"max({q}, 0.0)"));
            return Distance($"{d} + min(maxComponent({q}), 0.0)");
        }

        // Undoes the transformation in reverse order: translate, then rotate, then scale
        private string ScaleTranslateRotate(ScaleTranslateRotate e, string p)
        {
            var scale = Float(e.scale);
            var q = Position3($"opInverseRotateXYZ({p} - {Vec3(e.translation)}, {Vec3(e.rotation)}) / {scale}");
            return Distance($"{Sdf(e.primitive, q)} * {scale}");
        }

        private string Combine(SDFEvaluable[] primitives, string p, Func<string, string, string> combine, string empty) =>
            primitives.Length == 0
                ? Distance(empty)
                : primitives.Skip(1).Aggregate(Sdf(primitives[0], p), (d, primitive) => Distance(combine(d, Sdf(primitive, p))));

        private string Combine2D(SDF2DEvaluable[] primitives, string p, Func<string, string, string> combine, string empty) =>
            primitives.Length == 0
                ? Distance(empty)
                : primitives.Skip(1).Aggregate(Sdf2D(primitives[0], p), (d, primitive) => Distance(combine(d, Sdf2D(primitive, p))));

        private string Inline(string variableId, Func<Evaluable, string> generate)
        {
            if (!variables.TryGetValue(variableId, out var variable))
                throw new InvalidOperationException($"Variable '{variableId}' is not declared in this function");
            if (!inlining.Add(variableId))
                throw new InvalidOperationException($"Variable '{variable.declaration.name}' refers to itself");

            var result = generate(variable.declaration.value);
            inlining.Remove(variableId);
            return result;
        }

        private string FunctionName(string functionId) =>
            context.FunctionNames.TryGetValue(functionId, out var name)
                ? name
                : throw new KeyNotFoundException($"There is no function with id '{functionId}'");

        public string Value(Evaluable evaluable) => evaluable switch
        {
            FloatEvaluable e => Float(e),
            V2Evaluable e => Vec2(e),
            V3Evaluable e => Vec3(e),
            IntegerEvaluable e => Int(e),
            _ => throw new NotSupportedException($"Values of type {evaluable.GetType().Name} are not supported"),
        };

        private string Float(FloatEvaluable evaluable) => evaluable switch
        {
            ConstValueFloat c => Literal(c.value),
            FunctionParameterValueFloat fp => Parameter(fp.functionParameterIndex),
            VariableReferenceFloat r => Variable(r.variableId),
            FloatEvaluablePointerable e => Float(pointers.FloatEvaluablePointers[e.PointerId]),
            _ => throw new NotImplementedException($"Unknown {nameof(FloatEvaluable)}: {evaluable.GetType().Name}"),
        };

        private string Vec2(V2Evaluable evaluable) => evaluable switch
        {
            ConstValueV2 c => $"vec2({Literal(c.v2.x)}, {Literal(c.v2.y)})",
            FunctionParameterValueV2 fp => Parameter(fp.functionParameterIndex),
            VariableReferenceV2 r => Variable(r.variableId),
            _ => throw new NotImplementedException($"Unknown {nameof(V2Evaluable)}: {evaluable.GetType().Name}"),
        };

        private string Vec3(V3Evaluable evaluable) => evaluable switch
        {
            ConstValueV3 c => $"vec3({Literal(c.v3.x)}, {Literal(c.v3.y)}, {Literal(c.v3.z)})",
            FunctionParameterValueV3 fp => Parameter(fp.functionParameterIndex),
            VariableReferenceV3 r => Variable(r.variableId),
            _ => throw new NotImplementedException($"Unknown {nameof(V3Evaluable)}: {evaluable.GetType().Name}"),
        };

        private string Int(IntegerEvaluable evaluable) => evaluable switch
        {
            ConstValueInt c => c.value < 0 ? $"({c.value.ToString(CultureInfo.InvariantCulture)})" : c.value.ToString(CultureInfo.InvariantCulture),
            FunctionParameterValueInt fp => Parameter(fp.functionParameterIndex),
            VariableReferenceInt r => Variable(r.variableId),
            IntegerEvaluablePointerable e => Int(pointers.IntegerEvaluablePointers[e.PointerId]),
            _ => throw new NotImplementedException($"Unknown {nameof(IntegerEvaluable)}: {evaluable.GetType().Name}"),
        };

        private string Parameter(int index) =>
            index >= 0 && index < parameterNames.Length
                ? parameterNames[index]
                : throw new InvalidOperationException($"There is no function parameter {index} here");

        private string Variable(string variableId) =>
            variables.TryGetValue(variableId, out var variable)
                ? variable.name
                : throw new InvalidOperationException($"Variable '{variableId}' is not declared in this function");
    }

    // Negative literals get parentheses, so that "-" + literal never turns into the decrement operator "--"
    private static string Literal(float value)
    {
        if (!float.IsFinite(value))
            throw new NotSupportedException($"GLSL has no literal for {value}");

        var text = value.ToString(CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E'))
            text += ".0";
        return value < 0 || text.StartsWith('-') ? $"({text})" : text;
    }

    private static string Type(Evaluable value) => value switch
    {
        FloatEvaluable => "float",
        V2Evaluable => "vec2",
        V3Evaluable => "vec3",
        IntegerEvaluable => "int",
        _ => throw new NotSupportedException($"Values of type {value.GetType().Name} have no GLSL type"),
    };

    // GLSL identifiers only allow letters, digits and single underscores
    private static string Identifier(string name) =>
        MultipleUnderscores().Replace(InvalidIdentifierCharacters().Replace(name, "_"), "_").TrimEnd('_');

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex InvalidIdentifierCharacters();

    [GeneratedRegex(@"\b[A-Za-z_]\w*\b")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("_{2,}")]
    private static partial Regex MultipleUnderscores();

    private static Dictionary<string, string> FunctionNames(SDFProgram program)
    {
        var names = new Dictionary<string, string>();
        foreach (var function in program.Functions)
        {
            var name = Identifier($"sdf_{function.name}_{function.id}");
            // Different ids can become the same identifier once their invalid characters are replaced
            var unique = name;
            for (var i = 2; names.ContainsValue(unique) || unique == SceneFunctionName; i++)
                unique = $"{name}_{i}";
            names[function.id] = unique;
        }
        return names;
    }

    // Parameters are only referenced by index, so their types come from how the function uses them, or else from what callers pass
    private static Dictionary<string, string[]> ParameterTypes(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);
        var known = program.Functions.ToDictionary(f => f.id, _ => new Dictionary<int, string>());

        void Add(string functionId, int index, string type, string source)
        {
            if (!known[functionId].TryAdd(index, type) && known[functionId][index] != type)
                throw new InvalidOperationException($"Parameter {index} of function '{functionId}' is used as both {known[functionId][index]} and {type} ({source})");
        }

        foreach (var function in program.Functions)
        {
            foreach (var node in Roots(function).SelectMany(root => Descendants(tables, root)))
            {
                if (node is FunctionParameterValue parameter)
                    Add(function.id, ParameterIndex(parameter), Type((Evaluable)parameter), "in its body");
            }
        }

        // Unused parameters still need a type in the signature, which callers can tell
        var callers = program.Functions.SelectMany(Roots).Append(program.scene);
        foreach (var call in callers.SelectMany(root => Descendants(tables, root)).OfType<SDFFunctionCall>())
        {
            if (!known.TryGetValue(call.functionId, out var types))
                continue;
            for (var i = 0; i < call.arguments.Length; i++)
                if (!types.ContainsKey(i))
                    types[i] = Type(call.arguments[i]);
        }

        return program.Functions.ToDictionary(f => f.id, f =>
        {
            var types = known[f.id];
            var count = Math.Max(f.argumentNames.Length, types.Count == 0 ? 0 : types.Keys.Max() + 1);
            return Enumerable.Range(0, count).Select(i => types.GetValueOrDefault(i, "float")).ToArray();
        });
    }

    private static int ParameterIndex(FunctionParameterValue parameter) => parameter switch
    {
        FunctionParameterValueFloat p => p.functionParameterIndex,
        FunctionParameterValueV2 p => p.functionParameterIndex,
        FunctionParameterValueV3 p => p.functionParameterIndex,
        FunctionParameterValueInt p => p.functionParameterIndex,
        _ => throw new NotImplementedException($"Unknown {nameof(FunctionParameterValue)}: {parameter.GetType().Name}"),
    };

    // GLSL has no recursion and needs every function declared before it is called
    private static List<SDFFunctionDefinition> OrderCalleesFirst(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);
        var functions = program.Functions.ToDictionary(f => f.id);
        var ordered = new List<SDFFunctionDefinition>();
        var done = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(SDFFunctionDefinition function)
        {
            if (done.Contains(function.id))
                return;
            if (!visiting.Add(function.id))
                throw new InvalidOperationException($"Function '{function.name}' calls itself, which GLSL does not allow");

            foreach (var call in Roots(function).SelectMany(root => Descendants(tables, root)).OfType<SDFFunctionCall>())
            {
                if (!functions.TryGetValue(call.functionId, out var callee))
                    throw new KeyNotFoundException($"There is no function with id '{call.functionId}'");
                Visit(callee);
            }

            visiting.Remove(function.id);
            done.Add(function.id);
            ordered.Add(function);
        }

        foreach (var function in program.Functions)
            Visit(function);
        return ordered;
    }

    private static IEnumerable<Evaluable> Roots(SDFFunctionDefinition function) =>
        function.variables.Select(v => v.value).Append(function.functionBody);

    // Every node reachable from the root, looking through pointers
    private static IEnumerable<Evaluable> Descendants(PointerTables tables, Evaluable root)
    {
        var pending = new Stack<Evaluable>([root]);
        while (pending.TryPop(out var node))
        {
            if (PointerTables.PointerId(node) is { } id)
                node = tables.Get(id);

            yield return node;
            foreach (var child in EvaluableChildren.Children(node))
                pending.Push(child);
        }
    }
}
