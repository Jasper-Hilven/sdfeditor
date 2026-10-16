using System.Text.RegularExpressions;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.ShaderExport;
using Xunit.Abstractions;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class SdfShaderExporterTests(ITestOutputHelper output)
{
    private static readonly string[] ShadertoyBuiltIns =
    [
        "iResolution", "iTime", "iTimeDelta", "iFrameRate", "iFrame", "iChannelTime", "iChannelResolution",
        "iMouse", "iChannel0", "iChannel1", "iChannel2", "iChannel3", "iDate", "iSampleRate", "main", "mainImage",
    ];

    public static TheoryData<string> Programs() => ["initial", "library", "function"];

    private static SDFProgram CreateProgram(string name) => name switch
    {
        "initial" => SdfInteractiveNodeProgramConstants.GetInitialProgram().Program,
        "library" => Program(new Union(
            new SmoothSubtraction(
                new Rotate(new Box(new ConstValueV3(new V3(1, 0.5f, 0.75f))), new ConstValueV3(new V3(0.1f, 0.2f, 0.3f))),
                Sphere(0.8f),
                new ConstValueFloat(0.1f)),
            new Translate(
                new Twist(new Torus(new ConstValueFloat(0.2f), new ConstValueFloat(0.6f)), new ConstValueFloat(0.5f)),
                new ConstValueV3(new V3(2, 0, 0))),
            new Revolution(new Star2D(new ConstValueFloat(0.5f), new ConstValueInt(5), new ConstValueFloat(0.4f)), new ConstValueFloat(1)),
            new Extrusion(new Circle(new ConstValueFloat(0.3f)), new ConstValueFloat(0.2f)),
            new LimitedRepetition(Sphere(0.1f), new ConstValueFloat(0.5f), new ConstValueV3(new V3(2, 0, 2))),
            new Onion(new Scale(Sphere(1), new ConstValueV3(new V3(1, 2, 1))), new ConstValueFloat(0.05f)))),
        "function" => Program(
            new Union(
                new SDFFunctionCall("f", new ConstValueFloat(1)),
                new Translate(new SDFFunctionCall("f", new ConstValueFloat(0.5f)), new ConstValueV3(new V3(0, 2, 0)))),
            new SDFFunction("f", "f", ["iTime"], [], new Sphere(new FunctionParameterValueFloat(0)))),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public void ShadertoyOutputDefinesSceneAndMainImage(string name)
    {
        var code = SdfShaderExporter.ToShadertoy(CreateProgram(name));
        output.WriteLine(code);

        Assert.Contains($"float {Webgl2CodeGenerator.SceneFunctionName}(vec3 p)", code);
        Assert.Contains("void mainImage(out vec4 fragColor, in vec2 fragCoord)", code);
        Assert.Contains("iResolution", code);
        Assert.Contains("iTime", code);
        Assert.Contains("iMouse", code);
        Assert.DoesNotContain("#version", code);
        Assert.DoesNotContain("precision", code);
        Assert.DoesNotContain("uniform", code);
        Assert.DoesNotMatch(@"\bvoid\s+main\s*\(", code);
        Assert.DoesNotContain("out vec4 fragColor;", code);
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void GlslOutputIsCompleteFragmentShader(string name)
    {
        var code = SdfShaderExporter.ToGlslFragment(CreateProgram(name));
        output.WriteLine(code);

        Assert.StartsWith("#version 300 es\n", code);
        Assert.Contains("precision highp float;", code);
        Assert.Contains("out vec4 fragColor;", code);
        Assert.Contains($"uniform vec2 {SdfShaderExporter.ResolutionUniform};", code);
        Assert.Contains($"uniform vec3 {SdfShaderExporter.CameraPositionUniform};", code);
        Assert.Contains($"uniform vec3 {SdfShaderExporter.CameraTargetUniform};", code);
        Assert.Contains($"uniform vec3 {SdfShaderExporter.CameraUpUniform};", code);
        Assert.Contains($"float {Webgl2CodeGenerator.SceneFunctionName}(vec3 p)", code);
        Assert.Matches(@"\bvoid main\(\)", code);
        Assert.DoesNotContain("mainImage", code);
        Assert.DoesNotMatch(@"\bi(Time|Mouse|Resolution)\b", code.Replace("a0_iTime", ""));
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void OutputsContainNoComments(string name)
    {
        var program = CreateProgram(name);
        foreach (var code in new[] { SdfShaderExporter.ToShadertoy(program), SdfShaderExporter.ToGlslFragment(program) })
        {
            Assert.DoesNotContain("//", code);
            Assert.DoesNotContain("/*", code);
        }
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void ShadertoyDeclarationsDoNotCollideWithBuiltInsOrReservedNames(string name)
    {
        var code = SdfShaderExporter.ToShadertoy(CreateProgram(name));
        var declared = Regex.Matches(code, @"\b(?:float|int|bool|vec[234]|void)\s+([A-Za-z_]\w*)")
            .Select(m => m.Groups[1].Value)
            .Where(id => id != "mainImage")
            .ToList();

        Assert.NotEmpty(declared);
        Assert.Empty(declared.Intersect(ShadertoyBuiltIns));
        Assert.DoesNotContain(declared, id => id.StartsWith("gl_") || id.Contains("__"));
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void TopLevelFunctionsAreDeclaredOnce(string name)
    {
        var code = SdfShaderExporter.ToShadertoy(CreateProgram(name));
        var functions = Regex.Matches(code, @"^(?:float|int|vec[234]|void)\s+(\w+)\((\w+)", RegexOptions.Multiline)
            .Select(m => $"{m.Groups[1].Value}({m.Groups[2].Value}")
            .ToList();

        Assert.Equal(functions.Count, functions.Distinct().Count());
    }

    [Fact]
    public void BothOutputsShareTheSceneCode()
    {
        var program = CreateProgram("library");
        var scene = new Webgl2CodeGenerator().GenerateShaderCode(program);

        Assert.Contains(scene, SdfShaderExporter.ToShadertoy(program));
        Assert.Contains(scene, SdfShaderExporter.ToGlslFragment(program));
    }

    [Fact]
    public void BracesAndParenthesesAreBalanced()
    {
        var program = CreateProgram("library");
        foreach (var code in new[] { SdfShaderExporter.ToShadertoy(program), SdfShaderExporter.ToGlslFragment(program) })
        {
            Assert.Equal(code.Count(c => c == '{'), code.Count(c => c == '}'));
            Assert.Equal(code.Count(c => c == '('), code.Count(c => c == ')'));
        }
    }
}
