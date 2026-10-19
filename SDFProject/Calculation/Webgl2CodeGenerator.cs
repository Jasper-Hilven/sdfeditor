using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.Calculation;

/// <summary>Generates WebGL2 GLSL: the distance function, and a material function that resolves named materials in <paramref name="materials"/>.</summary>
public partial class Webgl2CodeGenerator(MaterialLibrary? materials = null)
{
    private readonly MaterialLibrary materials = materials ?? MaterialLibrary.Empty;

    public const string SceneFunctionName = "sdf_scene";

    public string[] generateSdfCode(SDFProgram program) => Generate(program, materials).functions;

    public const string MaterialFunctionName = "sdf_material";
    public const string DefaultColor = "vec3(0.85, 0.62, 0.42)";
    public const string MaterialType = "SurfaceMaterial";
    public static readonly string DefaultMaterial = $"colorMaterial({DefaultColor}, p)";

    /// <summary>The material struct and its helpers; the renderer shades with the fields of what <see cref="MaterialFunctionName"/> returns.</summary>
    public static readonly string MaterialCode = $$"""
        struct {{MaterialType}}
        {
            vec3 baseColor;
            float roughness;
            float specular;
            vec3 subsurfaceColor;
            float subsurfaceRadius;
            float detailStrength;
            float detailScale;
            float colorVariation;
            float wetness;
            float detailStretch;
            vec3 detailAxis;
            vec3 edgeColor;
            float edgeThickness;
            vec3 variationColor;
            float variationScale;
            vec3 speckleColor;
            float speckleScale;
            float speckleDensity;
            float speckleSize;
            vec3 localPosition;
        };

        {{MaterialType}} colorMaterial(vec3 color, vec3 localPosition)
        {
            return {{MaterialLiteral(MaterialDefinition.Default, "localPosition", "color")}};
        }

        {{MaterialType}} mixMaterial({{MaterialType}} a, {{MaterialType}} b, float t)
        {
            return {{MaterialType}}(
                mix(a.baseColor, b.baseColor, t),
                mix(a.roughness, b.roughness, t),
                mix(a.specular, b.specular, t),
                mix(a.subsurfaceColor, b.subsurfaceColor, t),
                mix(a.subsurfaceRadius, b.subsurfaceRadius, t),
                mix(a.detailStrength, b.detailStrength, t),
                mix(a.detailScale, b.detailScale, t),
                mix(a.colorVariation, b.colorVariation, t),
                mix(a.wetness, b.wetness, t),
                mix(a.detailStretch, b.detailStretch, t),
                mix(a.detailAxis, b.detailAxis, t),
                mix(a.edgeColor, b.edgeColor, t),
                mix(a.edgeThickness, b.edgeThickness, t),
                mix(a.variationColor, b.variationColor, t),
                mix(a.variationScale, b.variationScale, t),
                mix(a.speckleColor, b.speckleColor, t),
                mix(a.speckleScale, b.speckleScale, t),
                mix(a.speckleDensity, b.speckleDensity, t),
                mix(a.speckleSize, b.speckleSize, t),
                mix(a.localPosition, b.localPosition, t));
        }

        {{MaterialType}} pickMaterial(bool first, {{MaterialType}} a, {{MaterialType}} b)
        {
            if (first) return a;
            return b;
        }
        """;

    /// <summary>A material value; <paramref name="localPosition"/> is the position in the coordinates of the shape it is on.</summary>
    public static string MaterialLiteral(MaterialDefinition m, string localPosition, string? baseColor = null) =>
        $"{MaterialType}({baseColor ?? Vec3Literal(m.baseColor)}, {Literal(m.roughness)}, {Literal(m.specular)}, {Vec3Literal(m.subsurfaceColor)}, " +
        $"{Literal(m.subsurfaceRadius)}, {Literal(m.detailStrength)}, {Literal(m.detailScale)}, {Literal(m.colorVariation)}, " +
        $"{Literal(m.wetness)}, {Literal(m.detailStretch)}, {Vec3Literal(m.detailAxis)}, {Vec3Literal(m.edgeColor)}, {Literal(m.edgeThickness)}, " +
        $"{Vec3Literal(m.variationColor)}, {Literal(m.variationScale)}, " +
        $"{Vec3Literal(m.speckleColor)}, {Literal(m.speckleScale)}, {Literal(m.speckleDensity)}, {Literal(m.speckleSize)}, {localPosition})";

    private static string Vec3Literal(V3 v) => $"vec3({Literal(v.x)}, {Literal(v.y)}, {Literal(v.z)})";

    public string GenerateShaderCode(SDFProgram program)
    {
        var (functions, material, context) = Generate(program, materials);
        return string.Join("\n\n", functions.Append(material).Prepend(Webgl2SdfLibrary.CodeFor(context.UsedLibraryMethods.Keys)));
    }

    public IReadOnlyDictionary<string, int> CountLibraryMethodUses(SDFProgram program) => Generate(program, materials).context.UsedLibraryMethods;

    private static (string[] functions, string material, GeneratorContext context) Generate(SDFProgram program, MaterialLibrary materials)
    {
        var context = new GeneratorContext(program, FunctionNames(program), ParameterTypes(program), [], materials);

        var code = OrderCalleesFirst(program).Select(f => GenerateFunction(context, f)).ToList();
        code.Add(GenerateScene(context, program));
        return (code.ToArray(), GenerateMaterial(context, program), context);
    }

    private static string GenerateMaterial(GeneratorContext context, SDFProgram program)
    {
        var writer = new FunctionWriter(context, []);
        DeclareVariables(writer, program.sceneVariables);
        var signature = $"{MaterialType} {MaterialFunctionName}(vec3 p)";
        if (!writer.ContainsColor(program.scene))
            return $"{MaterialCode}\n\n{signature}\n{{\n    return {DefaultMaterial};\n}}";

        var (_, material) = writer.Surface(program.scene, "p", DefaultMaterial);
        writer.Line($"return {material};");
        return $"{MaterialCode}\n\n{writer.ToFunction(signature)}";
    }

    private sealed record GeneratorContext(
        SDFProgram Program,
        Dictionary<string, string> FunctionNames,
        Dictionary<string, string[]> ParameterTypes,
        Dictionary<string, int> UsedLibraryMethods,
        MaterialLibrary Materials)
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
        DeclareVariables(writer, function.variables);
        writer.Line($"return {writer.Sdf(function.functionBody, "p")};");

        var parameters = string.Concat(types.Select((type, i) => $", {type} {parameterNames[i]}"));
        return writer.ToFunction($"float {context.FunctionNames[function.id]}(vec3 p{parameters})");
    }

    private static string GenerateScene(GeneratorContext context, SDFProgram program)
    {
        var writer = new FunctionWriter(context, []);
        DeclareVariables(writer, program.sceneVariables);
        writer.Line($"return {writer.Sdf(program.scene, "p")};");
        return writer.ToFunction($"float {SceneFunctionName}(vec3 p)");
    }

    private static void DeclareVariables(FunctionWriter writer, VariableDeclaration[] variables)
    {
        for (var i = 0; i < variables.Length; i++)
        {
            var variable = variables[i];
            var name = Identifier($"v{i}_{variable.name}");
            writer.Declare(variable, name);
            if (variable.value is not (SDFEvaluable or SDF2DEvaluable))
                writer.Line($"{Type(variable.value)} {name} = {writer.Value(variable.value)};");
        }
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

            if (Transformed(evaluable, p) is { } transformed)
                return Sdf(transformed.child, transformed.p);

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
                Torus e => Distance($"sdTorus({p}, ({F(e.outerRadius)} + {F(e.innerRadius)}) * 0.5, ({F(e.outerRadius)} - {F(e.innerRadius)}) * 0.5)"),
                TriPrism e => Distance($"sdTriPrism({p}, {V2(e.size)})"),
                Triangle e => Distance($"sdTriangle({p}, {V3(e.a)}, {V3(e.b)}, {V3(e.c)})"),
                VerticalCapsule e => Distance($"sdVerticalCapsule({p}, {F(e.height)}, {F(e.radius)})"),
                VesicaSegment e => Distance($"sdVesicaSegment({p}, {V3(e.a)}, {V3(e.b)}, {F(e.width)})"),

                Revolution e => Sdf2D(e.primitive, Position2($"vec2(length({p}.xz) - {F(e.offset)}, {p}.y)")),
                Extrusion e => Distance($"opExtrusion({p}, {Sdf2D(e.primitive, Position2($"{p}.xy"))}, {F(e.height)})"),

                ElongateExactInterior e => ElongateExactInterior(e, p),
                Round e => Distance($"{Sdf(e.primitive, p)} - {F(e.radius)}"),
                Onion e => Distance($"abs({Sdf(e.primitive, p)}) - {F(e.thickness)}"),
                Color e => Sdf(e.primitive, p),
                Material e => Sdf(e.primitive, p),
                Detail e => Sdf(e.primitive, p),
                Distort e => Distance($"{Sdf(e.primitive, p)} + opDistort({p}, {F(e.amount)}, {F(e.frequency)})"),

                Union e => Combine(e.primitives, p, (a, b) => $"min({a}, {b})", "SDF_INFINITY"),
                Intersection e => Combine(e.primitives, p, (a, b) => $"max({a}, {b})", "-SDF_INFINITY"),
                Subtraction e => Distance($"max({Sdf(e.a, p)}, -{Sdf(e.b, p)})"),
                Xor e => Distance($"opXor({Sdf(e.a, p)}, {Sdf(e.b, p)})"),
                SmoothUnion e => Combine(e.primitives, p, (a, b) => $"opSmoothMin({a}, {b}, {F(e.smoothness)})", "SDF_INFINITY"),
                SmoothIntersection e => Combine(e.primitives, p, (a, b) => $"-opSmoothMin(-{a}, -{b}, {F(e.smoothness)})", "-SDF_INFINITY"),
                SmoothSubtraction e => Distance($"-opSmoothMin(-{Sdf(e.a, p)}, {Sdf(e.b, p)}, {F(e.smoothness)})"),

                Scale e => ScaleSdf(e, p),


                Displace e => Distance($"{Sdf(e.primitive, p)} + {Sdf(e.displacement, p)}"),

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

        private (SDFEvaluable child, string p)? Transformed(SDFEvaluable evaluable, string p) => evaluable switch
        {
            Elongate e => (e.primitive, Position3($"{p} - clamp({p}, -{Vec3(e.amount)}, {Vec3(e.amount)})")),
            Translate e => (e.primitive, Position3($"{p} - {Vec3(e.translation)}")),
            Rotate e => (e.primitive, Position3($"opInverseRotateXYZ({p}, {Vec3(e.rotation)})")),
            SymmetryX e => (e.primitive, Position3($"vec3(abs({p}.x), {p}.yz)")),
            SymmetryXZ e => (e.primitive, Position3($"vec3(abs({p}.x), {p}.y, abs({p}.z))")),
            Repetition e => (e.primitive, Position3($"opRepeat({p}, {Vec3(e.spacing)})")),
            LimitedRepetition e => (e.primitive, Position3($"opRepeatLimited({p}, {Float(e.spacing)}, {Vec3(e.limits)})")),
            Twist e => (e.primitive, Position3($"opTwist({p}, {Float(e.amount)})")),
            CheapBend e => (e.primitive, Position3($"opCheapBend({p}, {Float(e.amount)})")),
            RadialWave e => (e.primitive, Position3($"opRadialWave({p}, {Float(e.amount)}, {Float(e.count)})")),
            _ => null,
        };

        public (string distance, string material) Surface(SDFEvaluable evaluable, string p, string material)
        {
            if (!ContainsColor(evaluable))
                return (Sdf(evaluable, p), material);
            if (Transformed(evaluable, p) is { } transformed)
                return Surface(transformed.child, transformed.p, material);

            return evaluable switch
            {
                SDFEvaluablePointerable e => Surface(pointers.SDFEvaluablePointers[e.PointerId], p, material),
                VariableReferenceSDF r => Inline(r.variableId, value => Surface((SDFEvaluable)value, p, material)),
                Color e => Surface(e.primitive, p, MaterialValue($"colorMaterial({Vec3(e.color)}, {p})")),
                Material e => Surface(e.primitive, p, MaterialValue(MaterialLiteral(context.Materials.Resolve(e.material), p))),
                Detail e => Surface(e.primitive, p, material),
                Round e => Modified(e.primitive, p, material, d => $"{d} - {Float(e.radius)}"),
                Onion e => Modified(e.primitive, p, material, d => $"abs({d}) - {Float(e.thickness)}"),
                Displace e => Modified(e.primitive, p, material, d => $"{d} + {Sdf(e.displacement, p)}"),
                Distort e => Modified(e.primitive, p, material, d => $"{d} + opDistort({p}, {Float(e.amount)}, {Float(e.frequency)})"),
                Union e => CombineMaterials(e.primitives, p, material, (a, b) => Pick($"{a.distance} < {b.distance}", a, b, $"min({a.distance}, {b.distance})"), "SDF_INFINITY"),
                Intersection e => CombineMaterials(e.primitives, p, material, (a, b) => Pick($"{a.distance} > {b.distance}", a, b, $"max({a.distance}, {b.distance})"), "-SDF_INFINITY"),
                SmoothUnion e => CombineMaterials(e.primitives, p, material, (a, b) => Blend(a, b, e.smoothness, 1, $"opSmoothMin({a.distance}, {b.distance}, {Float(e.smoothness)})"), "SDF_INFINITY"),
                SmoothIntersection e => CombineMaterials(e.primitives, p, material, (a, b) => Blend(a, b, e.smoothness, -1, $"-opSmoothMin(-{a.distance}, -{b.distance}, {Float(e.smoothness)})"), "-SDF_INFINITY"),
                Subtraction e => Modified(e.a, p, material, d => $"max({d}, -{Sdf(e.b, p)})"),
                SmoothSubtraction e => Modified(e.a, p, material, d => $"-opSmoothMin(-{d}, {Sdf(e.b, p)}, {Float(e.smoothness)})"),
                Xor e => CombineMaterials([e.a, e.b], p, material, (a, b) => Pick($"abs({a.distance}) < abs({b.distance})", a, b, $"opXor({a.distance}, {b.distance})"), "SDF_INFINITY"),
                Scale e => ScaleSurface(e, p, material),
                ElongateExactInterior e => ElongateExactInteriorSurface(e, p, material),
                _ => (Sdf(evaluable, p), material),
            };
        }

        private string MaterialValue(string expression) => Temporary(MaterialType, "m", expression);

        private (string distance, string material) Modified(SDFEvaluable child, string p, string material, Func<string, string> distance)
        {
            var (d, m) = Surface(child, p, material);
            return (Distance(distance(d)), m);
        }

        private (string distance, string material) Pick(string firstWins, (string distance, string material) a, (string distance, string material) b, string distance) =>
            (Distance(distance), MaterialValue($"pickMaterial({firstWins}, {a.material}, {b.material})"));

        private (string distance, string material) Blend((string distance, string material) a, (string distance, string material) b, FloatEvaluable smoothness, int sign, string distance)
        {
            var weight = $"clamp(0.5 + {Literal(0.125f * sign)} * ({b.distance} - {a.distance}) / max({Float(smoothness)}, 1e-6), 0.0, 1.0)";
            return (Distance(distance), MaterialValue($"mixMaterial({b.material}, {a.material}, {weight})"));
        }

        private (string distance, string material) CombineMaterials(SDFEvaluable[] primitives, string p, string material,
            Func<(string distance, string material), (string distance, string material), (string distance, string material)> combine, string empty) =>
            primitives.Length == 0
                ? (Distance(empty), material)
                : primitives.Skip(1).Aggregate(Surface(primitives[0], p, material), (result, primitive) => combine(result, Surface(primitive, p, material)));

        private (string distance, string material) ScaleSurface(Scale e, string p, string material)
        {
            var scale = Vec3(e.scale);
            var (d, m) = Surface(e.primitive, Position3($"{p} / {scale}"), material);
            return (Distance($"{d} * minComponent({scale})"), m);
        }

        private (string distance, string material) ElongateExactInteriorSurface(ElongateExactInterior e, string p, string material)
        {
            var q = Position3($"abs({p}) - {Vec3(e.amount)}");
            var (d, m) = Surface(e.primitive, Position3($"max({q}, 0.0)"), material);
            return (Distance($"{d} + min(maxComponent({q}), 0.0)"), m);
        }

        public bool ContainsColor(Evaluable evaluable) => ContainsColor(evaluable, []);

        private bool ContainsColor(Evaluable evaluable, HashSet<string> visitedVariables)
        {
            var node = PointerTables.PointerId(evaluable) is { } id ? new PointerTables(pointers).Get(id) : evaluable;
            return node switch
            {
                Color or Material => true,
                SDFFunctionCall => false,
                VariableReferenceSDF r => visitedVariables.Add(r.variableId)
                                          && variables.TryGetValue(r.variableId, out var variable)
                                          && ContainsColor(variable.declaration.value, visitedVariables),
                _ => EvaluableChildren.Children(node).Any(child => ContainsColor(child, visitedVariables)),
            };
        }

        private string ElongateExactInterior(ElongateExactInterior e, string p)
        {
            var q = Position3($"abs({p}) - {Vec3(e.amount)}");
            var d = Sdf(e.primitive, Position3($"max({q}, 0.0)"));
            return Distance($"{d} + min(maxComponent({q}), 0.0)");
        }

        private string ScaleSdf(Scale e, string p)
        {
            var scale = Vec3(e.scale);
            return Distance($"{Sdf(e.primitive, Position3($"{p} / {scale}"))} * minComponent({scale})");
        }

        private string Combine(SDFEvaluable[] primitives, string p, Func<string, string, string> combine, string empty) =>
            primitives.Length == 0
                ? Distance(empty)
                : primitives.Skip(1).Aggregate(Sdf(primitives[0], p), (d, primitive) => Distance(combine(d, Sdf(primitive, p))));

        private string Combine2D(SDF2DEvaluable[] primitives, string p, Func<string, string, string> combine, string empty) =>
            primitives.Length == 0
                ? Distance(empty)
                : primitives.Skip(1).Aggregate(Sdf2D(primitives[0], p), (d, primitive) => Distance(combine(d, Sdf2D(primitive, p))));

        private T Inline<T>(string variableId, Func<Evaluable, T> generate)
        {
            if (!variables.TryGetValue(variableId, out var variable))
                throw new InvalidOperationException($"Variable '{variableId}' is not declared in this function or scene");
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
                : throw new InvalidOperationException($"Variable '{variableId}' is not declared in this function or scene");
    }

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
            var unique = name;
            for (var i = 2; names.ContainsValue(unique) || unique == SceneFunctionName; i++)
                unique = $"{name}_{i}";
            names[function.id] = unique;
        }
        return names;
    }

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

        var callers = program.AllRoots();
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
