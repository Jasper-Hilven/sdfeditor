using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Tests.ProgramFiles;

public class DetailFileTests
{
    private static readonly Dictionary<string, string> Parts = new()
    {
        ["crown"] = "format 1\nDetail detail=0.25\n    Sphere radius=1",
        ["buried"] = "format 1\nUnion\n    Detail detail=0.5\n        Sphere radius=1",
    };

    private static SDFProgram Read(string text) => SdfProgramFile.Read("format 1\n" + text, name => Parts.GetValueOrDefault(name));

    private static Evaluable Scene(SDFProgram program) => new PointerTables(program.pointers).Expand(program.scene);

    [Fact]
    public void Detail_IsReadAtTheRoot_AndWrittenBackTheSame()
    {
        var text = "format 1\n\nDetail detail=0.5\n    Union\n        Sphere radius=1\n        Translate translation=(0, 1, 0)\n            Box size=(0.5, 0.5, 0.5)\n";

        var program = SdfProgramFile.Read(text);

        var detail = Assert.IsType<Detail>(Scene(program));
        Assert.Equal(new ConstValueFloat(0.5f), detail.detail);
        Assert.IsType<Union>(detail.primitive);
        Assert.Equal(text, SdfProgramFile.Write(program));
        Assert.Equal(text, SdfProgramFile.Write(SdfProgramFile.Read(SdfProgramFile.Write(program))));
    }

    [Fact]
    public void Detail_KeepsTheDistancesOfItsShape()
    {
        var shape = "SmoothUnion smoothness=0.1\n    Sphere radius=1\n    Translate translation=(0.8, 0.5, 0)\n        Box size=(0.4, 0.4, 0.4)";
        var plain = Read(shape);
        var detailed = Read("Detail detail=0.3\n" + string.Join('\n', shape.Split('\n').Select(line => "    " + line)));

        var calculator = new SdfCalculatorCpu();
        foreach (var point in new[] { new V3(0, 0, 0), new V3(1.3f, 0.4f, -0.2f), new V3(-2, 1, 0.5f), new V3(0.8f, 0.9f, 0) })
            Assert.Equal(calculator.Calculate(point, plain), calculator.Calculate(point, detailed));
    }

    [Fact]
    public void Detail_AtTheRootOfAPartFile_IsThePartsValue()
    {
        var program = Read("Translate translation=(0, 2, 0)\n    Part part=crown");

        var crown = program.sceneVariables.Single(v => v.name == "part:crown");
        Assert.Equal(new ConstValueFloat(0.25f), Assert.IsType<Detail>(new PointerTables(program.pointers).Expand(crown.value)).detail);
        Assert.DoesNotContain("Detail", SdfProgramFile.Write(program));
    }

    [Theory]
    [InlineData("Union\n    Detail detail=0.5\n        Sphere radius=1", 3, "Detail only works as the shape of the file itself")]
    [InlineData("crown =\n    Detail detail=0.5\n        Sphere radius=1\n$crown", 3, "Detail only works as the shape of the file itself")]
    [InlineData("function f()\n    Detail detail=0.5\n        Sphere radius=1\nSDFFunctionCall functionId=f", 3, "Detail only works as the shape of the file itself")]
    [InlineData("Detail detail=0.5\n    Detail detail=0.5\n        Sphere radius=1", 3, "Detail only works as the shape of the file itself")]
    [InlineData("Part part=buried", 2, "In part 'buried', line 3: Detail only works as the shape of the file itself")]
    [InlineData("Detail detail=0.01\n    Sphere radius=1", 2, "must be between 0.05 and 4, not 0.01")]
    [InlineData("Detail detail=5\n    Sphere radius=1", 2, "must be between 0.05 and 4, not 5")]
    [InlineData("Detail detail=-1\n    Sphere radius=1", 2, "must be between 0.05 and 4, not -1")]
    [InlineData("d = 0.5\nDetail detail=$d\n    Sphere radius=1", 3, "The detail of Detail must be a number, like detail=0.5")]
    [InlineData("Detail detail=0.5", 2, "Detail needs a shape indented below it for 'primitive'")]
    public void Detail_ElsewhereOrOutOfRange_IsExplained(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Theory]
    [InlineData("0.05")]
    [InlineData("1")]
    [InlineData("4")]
    public void Detail_AcceptsTheWholeRange(string detail) =>
        Assert.IsType<Detail>(Scene(Read($"Detail detail={detail}\n    Sphere radius=1")));
}
