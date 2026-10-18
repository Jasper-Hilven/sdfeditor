using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.Tests.SDFProgramTestHelpers;
using Mode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.Tests;

public class PartLockTests
{
    /// <summary>A model that uses part 'eye', a sphere, moved to the right.</summary>
    private static readonly SDFProgram Model = SDFProgramNormalizePointers.Pointerify(new SDFProgram([], NoPointers,
        [new VariableDeclaration(Parts.VariableName("eye"), "eye", Sphere(0.12f))],
        new Translate(new VariableReferenceSDF("eye"), new ConstValueV3(new V3(1, 0, 0)))));

    private static SDFPartialCommandNodeProgram Selecting(string nodeId) =>
        new(new SdfInteractiveNodeProgram(Model, nodeId, 0), new SDFPartialCommandNodeProgramState(Mode.Home));

    private static string SphereInsideThePart => SphereId(Model, 0.12f);

    [Fact]
    public void InsideAPart_EditsAreUnavailable_AndRefused()
    {
        var inside = Selecting(SphereInsideThePart);

        Assert.False(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(inside, new EditNode()));
        Assert.False(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(inside, new DissolveNode()));
        var refused = Assert.Throws<InvalidOperationException>(() => SDFPartialCommandNodeProgramOperator.Operate(inside, new DissolveNode()));
        Assert.Contains("content/parts/eye.sdf", refused.Message);
    }

    [Fact]
    public void InsideAPart_LookingAroundStillWorks()
    {
        var inside = Selecting(SphereInsideThePart);

        Assert.Null(SDFPartialCommandNodeProgramOperator.LockingPart(inside, new SDFInteractiveNode.PartialCommandStateFullEditing.SelectNext()));
        Assert.Null(SDFPartialCommandNodeProgramOperator.LockingPart(inside, new IncreaseZoomLevel()));
        Assert.True(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(inside, new SwitchToTranslateMode()));
    }

    [Fact]
    public void TheModelAroundThePart_StaysEditable()
    {
        var translate = PointerTables.PointerId(Model.scene)!;

        Assert.True(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(Selecting(translate), new EditNode()));
    }
}
