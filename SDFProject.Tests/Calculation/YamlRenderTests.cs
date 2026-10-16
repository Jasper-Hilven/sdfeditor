using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class YamlRenderTests
{
    private static readonly YamlRender Render = new();

    [Fact]
    public void InitialProgram_IsSelectedCube()
    {
        var program = SdfInteractiveNodeProgramConstants.GetInitialProgram();

        Assert.Equal(["Box size=(0.5, 0.5, 0.5) <---"], Render.GetRender(program));
    }

    [Fact]
    public void ChildrenAreIndentedBelowTheirParent()
    {
        var program = Program(new SmoothUnion(new ConstValueFloat(0.25f), Sphere(1), new Round(Sphere(2), new ConstValueFloat(0.1f))));

        Assert.Equal(
        [
            "SmoothUnion smoothness=0.25",
            "    Sphere radius=1",
            "    Round radius=0.1 <---",
            "        Sphere radius=2",
        ], Render.GetRender(new SdfInteractiveNodeProgram(program, NodeId<Round>(program), 0)));
    }

    private static string NodeId<T>(SDFProgram program) where T : SDFEvaluable =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is T).Key;
}
