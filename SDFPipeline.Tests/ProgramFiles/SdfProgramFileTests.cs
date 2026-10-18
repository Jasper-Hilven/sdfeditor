using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Tests.ProgramFiles;

public class SdfProgramFileTests
{
    private static readonly SDFPointers NoPointers = new(new(), new(), new(), new(), 0);

    private static SDFProgram Program(SDFEvaluable scene, VariableDeclaration[]? variables = null, params SDFFunctionDefinition[] functions) =>
        SDFProgramNormalizePointers.Pointerify(new SDFProgram(functions, NoPointers, variables ?? [], scene));

    private static FloatEvaluable F(float value) => new ConstValueFloat(value);

    private static V3Evaluable V3(float x, float y, float z) => new ConstValueV3(new V3(x, y, z));

    private static SDFProgram ExampleProgram(string name)
    {
        var initial = new SDFPartialCommandNodeProgram(
            SdfInteractiveNodeProgramConstants.GetInitialProgram(),
            new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));
        return SDFExamples.All.Single(e => e.Name == name).Load(initial).Current.interactiveProgram.Program;
    }

    public static TheoryData<string> ExampleNames() => new(SDFExamples.All.Select(e => e.Name));

    private static readonly V3[] SamplePoints =
        (from x in new[] { -1.7f, -0.6f, 0.15f, 0.9f, 2.1f }
         from y in new[] { -0.4f, 0.3f, 0.95f, 1.6f, 2.4f }
         from z in new[] { -1.3f, -0.2f, 0.45f, 1.8f }
         select new V3(x, y, z)).ToArray();

    private static void AssertSameDistances(SDFProgram expected, SDFProgram actual)
    {
        var calculator = new SdfCalculatorCpu();
        foreach (var point in SamplePoints)
            Assert.Equal(calculator.Calculate(point, expected), calculator.Calculate(point, actual));
    }

    private static void AssertRoundTrips(SDFProgram program)
    {
        var text = SdfProgramFile.Write(program);
        var read = SdfProgramFile.Read(text);

        Assert.Equal(text, SdfProgramFile.Write(read));
        AssertSameDistances(program, read);
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Example_RoundTripsThroughText(string name) => AssertRoundTrips(ExampleProgram(name));

    [Fact]
    public void InitialProgram_RoundTripsThroughText() =>
        AssertRoundTrips(SdfInteractiveNodeProgramConstants.GetInitialProgram().Program);

    [Fact]
    public void Write_ShowsOneNodePerLineWithChildrenIndented()
    {
        var program = Program(
            new SmoothUnion(F(0.08f),
                new Color(new Translate(new CutSphere(new VariableReferenceFloat("r"), F(0.15f)), V3(0, 0.6f, 0)), V3(0.9f, 0.2f, 0.2f)),
                new CappedCylinder(F(0.16f), F(0.4f))),
            [new VariableDeclaration("capRadius", "r", F(0.7f))]);

        Assert.Equal(
            """
            format 1

            capRadius = 0.7
            SmoothUnion smoothness=0.08
                Color color=(0.9, 0.2, 0.2)
                    Translate translation=(0, 0.6, 0)
                        CutSphere radius=$capRadius cutHeight=0.15
                CappedCylinder radius=0.16 height=0.4

            """,
            SdfProgramFile.Write(program));
    }

    [Fact]
    public void Write_KeepsEveryBitOfAFloat()
    {
        var radius = 0.1f + 1e-7f;

        var read = SdfProgramFile.Read(SdfProgramFile.Write(Program(new Sphere(F(radius)))));

        var sphere = Assert.IsType<Sphere>(new PointerTables(read.pointers).Expand(read.scene));
        Assert.Equal(radius, Assert.IsType<ConstValueFloat>(sphere.radius).value);
    }

    [Fact]
    public void Functions_RoundTripWithTypedArguments()
    {
        var function = new SDFFunction("block", "b", ["size", "unused"], [new VariableDeclaration("edge", "e", new FunctionParameterValueV3(0))],
            new Box(new VariableReferenceV3("e")));
        var program = Program(new SDFFunctionCall("b", V3(1, 0.5f, 1), new ConstValueInt(3)), null, function);

        var text = SdfProgramFile.Write(program);

        Assert.Contains("function block(size: v3, unused: int)", text);
        Assert.Contains("SDFFunctionCall functionId=block arguments=[(1, 0.5, 1), 3]", text);
        AssertRoundTrips(program);
    }

    [Fact]
    public void Read_AcceptsHandWrittenText()
    {
        var program = SdfProgramFile.Read(
            """
            # A toadstool, written by hand
            format 1

            "cap radius" = 0.7      # names that are not identifiers are quoted
            stem =
                CappedCylinder radius=0.16 height=0.4

            SmoothUnion smoothness=0.08
                Translate translation=(0, 1, 0)
                    CutSphere radius=$"cap radius" cutHeight=0   # whole numbers are fine for numbers
                $stem
            """);

        var expected = Program(
            new SmoothUnion(F(0.08f),
                new Translate(new CutSphere(F(0.7f), F(0)), V3(0, 1, 0)),
                new CappedCylinder(F(0.16f), F(0.4f))));
        AssertSameDistances(expected, program);
        Assert.Equal(["cap radius", "stem"], program.sceneVariables.Select(v => v.name));
    }

    [Fact]
    public void Read_GivesEveryNodeItsOwnPointer()
    {
        var program = SdfProgramFile.Read(
            """
            format 1
            Union
                Sphere radius=1
                Sphere radius=1
            """);

        Assert.Equal(3, program.pointers.SDFEvaluablePointers.Count);
    }

    [Theory]
    [InlineData("", 1, "The file is empty")]
    [InlineData("Sphere radius=1", 1, "must start with 'format 1'")]
    [InlineData("format 2\nSphere radius=1", 1, "only format 1 can be read")]
    [InlineData("format 1\nSpere radius=1", 2, "Unknown node type 'Spere'")]
    [InlineData("format 1\nsphere radius=1", 2, "did you mean Sphere")]
    [InlineData("format 1\nSphere size=1", 2, "Sphere is missing parameter 'radius'")]
    [InlineData("format 1\nSphere radius=1 size=2", 2, "Sphere has no parameter 'size'; its parameters are radius")]
    [InlineData("format 1\nBox size=(1, 2)", 2, "Expected a 3D vector like (x, y, z), but a 2D vector is a 2D vector like (x, y)")]
    [InlineData("format 1\nSphere radius=$r", 2, "Variable 'r' is not declared before this line in the file")]
    [InlineData("format 1\nTranslate translation=(0, 1, 0)", 2, "Translate needs a shape indented below it for 'primitive'")]
    [InlineData("format 1\nTranslate translation=(0, 1, 0)\n    Sphere radius=1\n    Sphere radius=2", 4, "no room for another shape")]
    [InlineData("format 1\nSphere radius=1\nSphere radius=2", 3, "already has its shape on line 2")]
    [InlineData("format 1\nr = 1", 2, "The file has no shape")]
    [InlineData("format 1\nExtrusion height=1\n    Sphere radius=1", 3, "Expected a 2D shape, but Sphere is a 3D shape")]
    [InlineData("format 1\nn = 2.5\nExtrusion height=1\n    Star2D radius=1 points=$n innerFactor=0.5", 4, "Expected a whole number, but variable 'n' is a number")]
    [InlineData("format 1\nSphere radius=@r", 2, "only exists inside a function")]
    [InlineData("format 1\nSDFFunctionCall functionId=f", 2, "There is no function 'f'")]
    [InlineData("format 1\nfunction f(a: float)\n    Sphere radius=@a\nSDFFunctionCall functionId=f", 4, "takes 1 argument(s), but 0 are given")]
    [InlineData("format 1\nSphere radius=1\n\tSphere radius=1", 3, "Indent with spaces, not tabs")]
    public void Read_ExplainsWhatIsWrongAndWhere(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => SdfProgramFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Write_RefusesVariablesItCouldNotTellApart()
    {
        var program = Program(new Union(new VariableReferenceSDF("a"), new VariableReferenceSDF("b")),
            [new VariableDeclaration("x", "a", new Sphere(F(1))), new VariableDeclaration("x", "b", new Sphere(F(2)))]);

        Assert.Throws<InvalidOperationException>(() => SdfProgramFile.Write(program));
    }

    [Fact]
    public void SaveAndLoad_UseTheSameText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{SdfProgramFile.Extension}");
        try
        {
            var program = ExampleProgram("Mushrooms");
            SdfProgramFile.Save(path, program);

            Assert.Equal(SdfProgramFile.Write(program), SdfProgramFile.Write(SdfProgramFile.Load(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
