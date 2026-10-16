using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class Webgl2CodeGeneratorTests
{
    private static readonly Webgl2CodeGenerator Generator = new();

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void Scene_BecomesSceneFunction()
    {
        var code = Generator.generateSdfCode(Program(new Union(Sphere(1), Sphere(2))));

        Assert.Equal(Lines(
            "float sdf_scene(vec3 p)",
            "{",
            "    float d0 = sdSphere(p, 1.0);",
            "    float d1 = sdSphere(p, 2.0);",
            "    float d2 = min(d0, d1);",
            "    return d2;",
            "}"), Assert.Single(code));
    }

    [Fact]
    public void GeneratesOneFunctionPerFunctionDefinitionPlusScene()
    {
        var a = new SDFFunction("a", "fa", [], [], Sphere(1));
        var b = new SDFFunction("b", "fb", [], [], Sphere(2));

        var code = Generator.generateSdfCode(Program(new Union(new SDFFunctionCall("fa"), new SDFFunctionCall("fb")), a, b));

        Assert.Equal(3, code.Length);
        Assert.StartsWith("float sdf_a_fa(vec3 p)", code[0]);
        Assert.StartsWith("float sdf_b_fb(vec3 p)", code[1]);
        Assert.StartsWith($"float {Webgl2CodeGenerator.SceneFunctionName}(vec3 p)", code[2]);
        Assert.Contains("sdf_a_fa(p)", code[2]);
    }

    [Fact]
    public void ValueVariables_BecomeOneLineEach()
    {
        var function = new SDFFunction("f", "f", ["radius"],
            [
                new VariableDeclaration("r", "vr", new FunctionParameterValueFloat(0)),
                new VariableDeclaration("size", "vs", new ConstValueV3(new V3(1, 2, 3))),
            ],
            new Union(new Sphere(new VariableReferenceFloat("vr")), new Box(new VariableReferenceV3("vs"))));

        var code = Generator.generateSdfCode(Program(new SDFFunctionCall("f", new ConstValueFloat(0.5f)), function));

        Assert.Equal(Lines(
            "float sdf_f_f(vec3 p, float a0_radius)",
            "{",
            "    float v0_r = a0_radius;",
            "    vec3 v1_size = vec3(1.0, 2.0, 3.0);",
            "    float d0 = sdSphere(p, v0_r);",
            "    float d1 = sdBox(p, v1_size);",
            "    float d2 = min(d0, d1);",
            "    return d2;",
            "}"), code[0]);
        Assert.Contains("sdf_f_f(p, 0.5)", code[1]);
    }

    [Fact]
    public void SdfVariables_AreEvaluatedAtEveryUse()
    {
        var function = new SDFFunction("f", "f", [], [new VariableDeclaration("ball", "b", Sphere(1))],
            new Union(
                new VariableReferenceSDF("b"),
                new Translate(new VariableReferenceSDF("b"), new ConstValueV3(new V3(1, 0, 0)))));

        var code = Generator.generateSdfCode(Program(new SDFFunctionCall("f"), function))[0];

        Assert.Contains("float d0 = sdSphere(p, 1.0);", code);
        Assert.Contains("float d2 = sdSphere(p1, 1.0);", code);
    }

    [Fact]
    public void Pointers_AreResolved()
    {
        var program = Program(new Union(new Round(Sphere(1), new ConstValueFloat(0.1f))));

        var code = Assert.Single(Generator.generateSdfCode(program));

        Assert.NotEmpty(program.pointers.SDFEvaluablePointers);
        foreach (var id in program.pointers.SDFEvaluablePointers.Keys)
            Assert.DoesNotContain($"_{id}", code);
        Assert.Contains("sdSphere(p, 1.0)", code);
    }

    [Fact]
    public void Functions_AreDeclaredBeforeTheirCallers()
    {
        var caller = new SDFFunction("caller", "c", [], [], new SDFFunctionCall("e"));
        var callee = new SDFFunction("callee", "e", [], [], Sphere(1));

        var code = Generator.generateSdfCode(Program(new SDFFunctionCall("c"), caller, callee));

        Assert.StartsWith("float sdf_callee_e", code[0]);
        Assert.StartsWith("float sdf_caller_c", code[1]);
    }

    [Fact]
    public void Recursion_Throws()
    {
        var function = new SDFFunction("loop", "l", [], [], new SDFFunctionCall("l"));

        Assert.Throws<InvalidOperationException>(() => Generator.generateSdfCode(Program(new SDFFunctionCall("l"), function)));
    }

    [Fact]
    public void ParameterTypes_ComeFromUseOrElseFromCallers()
    {
        var function = new SDFFunction("f", "f", ["size", "unused"], [], new Box(new FunctionParameterValueV3(0)));

        var code = Generator.generateSdfCode(Program(new SDFFunctionCall("f", new ConstValueV3(new V3(1, 1, 1)), new ConstValueInt(3)), function));

        Assert.StartsWith("float sdf_f_f(vec3 p, vec3 a0_size, int a1_unused)", code[0]);
    }

    [Fact]
    public void NegativeLiterals_GetParentheses()
    {
        var code = Assert.Single(Generator.generateSdfCode(Program(new Elongate(Sphere(1), new ConstValueV3(new V3(-1, 0, 0))))));

        Assert.Contains("-vec3((-1.0), 0.0, 0.0)", code);
    }

    [Fact]
    public void ShaderCode_ContainsOnlyUsedLibraryMethods()
    {
        var code = Generator.GenerateShaderCode(Program(new Union(Sphere(1), Sphere(2))));

        Assert.Contains("float sdSphere(vec3 p, float r)", code);
        Assert.DoesNotContain("sdBox", code);
        Assert.DoesNotContain("SDF_INFINITY", code);
        Assert.EndsWith("}", code);
    }

    [Fact]
    public void ShaderCode_ContainsWhatUsedLibraryMethodsNeed()
    {
        var code = Generator.GenerateShaderCode(Program(new Repetition(new CappedCone(new ConstValueFloat(1), new ConstValueFloat(0.5f), new ConstValueFloat(0.2f)), new ConstValueV3(new V3(2, 0, 2)))));

        Assert.Contains("vec3 opRepeat(", code);
        Assert.Contains("float opRepeatAxis(", code);
        Assert.Contains("float opRoundAwayFromZero(", code);
        Assert.Contains("float sdCappedCone(", code);
        Assert.Contains("float dot2(vec2 v)", code);
        Assert.Contains("float dot2(vec3 v)", code);
        Assert.True(code.IndexOf("float opRoundAwayFromZero(", StringComparison.Ordinal) < code.IndexOf("float opRepeatAxis(", StringComparison.Ordinal));
        Assert.True(code.IndexOf("float sdCappedCone(", StringComparison.Ordinal) < code.IndexOf("float sdf_scene(", StringComparison.Ordinal));
    }

    [Fact]
    public void CountsLibraryMethodUses()
    {
        var program = Program(new Union(Sphere(1), Sphere(2), new Box(new ConstValueV3(new V3(1, 1, 1))), new Union()));

        var uses = Generator.CountLibraryMethodUses(program);

        Assert.Equal(2, uses["sdSphere"]);
        Assert.Equal(1, uses["sdBox"]);
        Assert.Equal(1, uses["SDF_INFINITY"]);
        Assert.False(uses.ContainsKey("sdTorus"));
    }

    [Fact]
    public void Library_ContainsEveryConstant()
    {
        var constants = typeof(Webgl2SdfLibrary)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var constant in constants)
            Assert.Contains(constant, Webgl2SdfLibrary.Code);
    }
}
