using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Tests.ProgramFiles;

public class PartTests
{
    private static readonly Dictionary<string, string> Files = new()
    {
        ["eye"] = "format 1\nradius = 0.12\nSphere radius=$radius",
        ["face"] = "format 1\nUnion\n    Translate translation=(-0.2, 0, 0)\n        Part part=eye\n    Translate translation=(0.2, 0, 0)\n        Part part=eye",
        ["loop"] = "format 1\nPart part=loop",
        ["broken"] = "format 1\nSpere radius=1",
    };

    private static string? Part(string name) => Files.GetValueOrDefault(name);

    private static SDFProgram Read(string text) => SdfProgramFile.Read("format 1\n" + text, Part);

    [Fact]
    public void Part_IsReadAsASharedVariable_AndWrittenBackAsAReference()
    {
        var program = Read(
            """
            Union
                Part part=eye
                Translate translation=(1, 0, 0)
                    Part part=eye
            """);

        Assert.Equal(["part:eye/radius", "part:eye"], program.sceneVariables.Select(v => v.name));
        var text = SdfProgramFile.Write(program);
        Assert.Equal(2, text.Split('\n').Count(l => l.Trim() == "Part part=eye"));
        Assert.DoesNotContain("radius", text);
        Assert.Equal(text, SdfProgramFile.Write(SdfProgramFile.Read(text, Part)));
    }

    [Fact]
    public void Part_RendersLikeItsFile()
    {
        var withPart = Read("Translate translation=(1, 0, 0)\n    Part part=eye");
        var inline = Read("Translate translation=(1, 0, 0)\n    Sphere radius=0.12");

        Assert.Equal(new SdfCalculatorCpu().Calculate(new V3(1.3f, 0.1f, 0), inline), new SdfCalculatorCpu().Calculate(new V3(1.3f, 0.1f, 0), withPart));
    }

    [Fact]
    public void Parts_CanUseParts_EachLoadedOnce()
    {
        var program = Read("Union\n    Part part=face\n    Part part=eye");

        Assert.Equal(["part:eye/radius", "part:eye", "part:face"], program.sceneVariables.Select(v => v.name));
        Assert.Contains("Part part=eye", SdfProgramFile.Write(program));
        Assert.Contains("Part part=face", SdfProgramFile.Write(program));
    }

    [Fact]
    public void NodesInsideAPart_BelongToIt()
    {
        var program = Read("Union\n    Part part=eye\n    Box size=(1, 1, 1)");
        var tables = new PointerTables(program.pointers);
        var sphere = program.pointers.SDFEvaluablePointers.Single(p => p.Value is Sphere).Key;
        var box = program.pointers.SDFEvaluablePointers.Single(p => p.Value is Box).Key;

        Assert.Equal("eye", Parts.PartContaining(program, sphere));
        Assert.Null(Parts.PartContaining(program, box));
        Assert.NotNull(tables.Get(sphere));
    }

    [Theory]
    [InlineData("Part part=nope", "There is no part 'nope'")]
    [InlineData("Part part=loop", "Part 'loop' uses itself: loop → loop")]
    [InlineData("Part part=broken", "In part 'broken', line 2: Unknown node type 'Spere'")]
    [InlineData("Part size=1", "Write a part as Part part=eye")]
    [InlineData("Extrusion height=1\n    Part part=eye", "Expected a 2D shape, but part 'eye' is a 3D shape")]
    public void Part_ExplainsWhatIsWrong(string text, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => Read(text));

        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void WithoutAContentFolder_PartsCannotBeFound()
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => SdfProgramFile.Read("format 1\nPart part=eye"));

        Assert.Contains("not read from a content folder", exception.Problem);
    }

    [Fact]
    public void Load_FindsPartsInTheContentFolderAboveTheFile()
    {
        var root = Directory.CreateTempSubdirectory("sdf-parts-");
        try
        {
            var content = Path.Combine(root.FullName, ModelFiles.ContentFolderName);
            Directory.CreateDirectory(Path.Combine(content, ModelFiles.PartsFolderName));
            Directory.CreateDirectory(Path.Combine(content, "models"));
            File.WriteAllText(Path.Combine(content, ModelFiles.PartsFolderName, "eye.sdf"), Files["eye"]);
            var model = Path.Combine(content, "models", "scout.sdf");
            File.WriteAllText(model, "format 1\nPart part=eye");

            Assert.Contains(SdfProgramFile.Load(model).sceneVariables, v => v.name == "part:eye");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
