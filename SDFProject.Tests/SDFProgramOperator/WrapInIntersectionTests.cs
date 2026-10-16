using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class WrapInIntersectionTests
{
    [Fact]
    public void ChildOfUnion_BecomesFirstChildOfIntersection()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new WrapInIntersection(SphereId(program, 1)));

        Assert.Equal("Union(Intersection(Sphere(1), Sphere(0.5)), Sphere(2))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SceneRoot_IsWrapped()
    {
        var program = Program(Sphere(1));

        var result = SdfProgramOperator.ApplyOperation(program, new WrapInIntersection(SphereId(program, 1)));

        Assert.Equal("Intersection(Sphere(1), Sphere(0.5))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void Interactive_SelectsTheIntersectedSphere()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfInteractiveNodeProgramOperator.ApplyOperation(
            new SdfInteractiveNodeProgram(program, SphereId(program, 1), 0), new InteractiveWrapInIntersection());

        Assert.Equal(SphereId(result.Program, 0.5f), result.SelectedNodeId);
    }

    [Fact]
    public void EmptyNode_CannotBeIntersected()
    {
        var program = Program(new Empty());

        Assert.False(ApplyWrapInIntersection.CanWrap(program, PointerTables.PointerId(program.scene)!));
    }

    [Fact]
    public void HKey_IntersectsTheSelectionInHomeMode()
    {
        var initial = new SDFPartialCommandNodeProgram(
            SdfInteractiveNodeProgramConstants.GetInitialProgram(), new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

        var result = SDFPartialCommandNodeProgramOperator.Operate(initial, FingerKeyLayouts.AzertyBeFingerKeyToIndex["h"]);

        Assert.Equal("Intersection(Box(ConstValueV3), Sphere(0.5))", DescribeScene(result.interactiveProgram.Program));
        Assert.Equal(FingerKeyLayouts.QwertyFingerKeyToIndex["h"], FingerKeyLayouts.AzertyBeFingerKeyToIndex["h"]);
    }
}
