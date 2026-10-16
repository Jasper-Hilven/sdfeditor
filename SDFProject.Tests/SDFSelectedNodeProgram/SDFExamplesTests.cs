using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

namespace SDFProject.Tests;

public class SDFExamplesTests
{
    private static SDFPartialCommandNodeProgram Initial() => new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

    public static TheoryData<string> Names() => new(SDFExamples.All.Select(e => e.Name));

    private static SDFExample Example(string name) => SDFExamples.All.Single(e => e.Name == name);

    private static string[] View(SDFPartialCommandNodeProgram program) => new YamlRender().GetRender(program.interactiveProgram);

    [Fact]
    public void ThereAreNineteenExamplesWithDistinctNames()
    {
        Assert.Equal(19, SDFExamples.All.Count);
        Assert.Equal(19, SDFExamples.All.Select(e => e.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Example_IsOneLinearHistoryEndingInHome(string name)
    {
        var history = Example(name).Load(Initial());

        Assert.True(history.Entries.Count > 50);
        Assert.Equal(history.Entries.Count - 1, history.Position);
        Assert.All(history.Entries.Select((entry, i) => (entry, i)), e => Assert.Equal(e.i == 0 ? null : e.i - 1, e.entry.Parent));
        Assert.Equal(SDFPartialCommandNodeProgramMode.Home, history.Current.state.Mode);
        Assert.Equal(0, history.Current.interactiveProgram.StrZoomLevel);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Example_UsesOnlyActionsAKeyCanProduce(string name)
    {
        var bound = SDFPartialKeyBindings.Bindings.Values.Select(a => a.GetType()).ToHashSet();

        var unbound = Example(name).Entries(Initial()).Select(e => e.Action).Where(a => a is not InsertText && !bound.Contains(a.GetType()));

        Assert.Empty(unbound);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Example_SurvivesExportAndImport(string name)
    {
        var history = Example(name).Load(Initial());

        var imported = SDFPartialHistorySerializer.Deserialize(SDFPartialHistorySerializer.Serialize(history), Initial());

        Assert.Equal(View(history.Current), View(imported.Current));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Example_IsColouredAndRenders(string name)
    {
        var current = Example(name).Load(Initial()).Current;
        var program = current.interactiveProgram.Program;

        var shader = new Webgl2CodeGenerator().GenerateShaderCode(program);
        var distance = new SdfCalculatorCpu().Calculate(new V3(0, 0.5f, 3), program);

        Assert.Contains("sdf_material", shader);
        Assert.Contains(View(current), line => line.TrimStart().StartsWith("Color "));
        Assert.True(float.IsFinite(distance));
    }

    [Fact]
    public void StepForward_FromTheStart_PlaysBackToTheSameScene()
    {
        var history = Example("Die").Load(Initial());

        var played = history.Path(history.Position).Aggregate(history.GoTo(null), (h, entry) => h.StepForward(entry));

        Assert.Equal(history.Position, played.Position);
        Assert.Equal(View(history.Current), View(played.Current));
        Assert.Equal(history.Entries, played.Entries);
    }

    [Fact]
    public void StepForward_ToAnEntryThatDoesNotFollow_Throws()
    {
        var history = Example("Die").Load(Initial()).GoTo(null);

        Assert.Throws<ArgumentException>(() => history.StepForward(5));
    }
}
