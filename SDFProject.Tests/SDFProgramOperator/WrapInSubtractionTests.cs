using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class WrapInSubtractionTests
{
    [Fact]
    public void ChildOfUnion_BecomesFirstArgumentOfSubtraction()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new WrapInSubtraction(SphereId(program, 1)));

        Assert.Equal("Union(Subtraction(Sphere(1), Sphere(0.5)), Sphere(2))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SceneRoot_IsWrapped()
    {
        var program = Program(Sphere(1));

        var result = SdfProgramOperator.ApplyOperation(program, new WrapInSubtraction(SphereId(program, 1)));

        Assert.Equal("Subtraction(Sphere(1), Sphere(0.5))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void Interactive_SelectsTheCutSphere()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfInteractiveNodeProgramOperator.ApplyOperation(
            new SdfInteractiveNodeProgram(program, SphereId(program, 1), 0), new InteractiveWrapInSubtraction());

        Assert.Equal(SphereId(result.Program, 0.5f), result.SelectedNodeId);
    }

    [Fact]
    public void EmptyNode_CannotBeSubtractedFrom()
    {
        var program = Program(new Empty());

        Assert.False(ApplyWrapInSubtraction.CanWrap(program, PointerTables.PointerId(program.scene)!));
    }

    [Fact]
    public void GKey_SubtractsFromTheSelectionInHomeMode()
    {
        var initial = new SDFPartialCommandNodeProgram(
            SdfInteractiveNodeProgramConstants.GetInitialProgram(), new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

        var result = SDFPartialCommandNodeProgramOperator.Operate(initial, FingerKeyLayouts.AzertyBeFingerKeyToIndex["g"]);

        Assert.Equal("Subtraction(Box(ConstValueV3), Sphere(0.5))", DescribeScene(result.interactiveProgram.Program));
        Assert.Equal(FingerKeyLayouts.QwertyFingerKeyToIndex["g"], FingerKeyLayouts.AzertyBeFingerKeyToIndex["g"]);
    }
}
