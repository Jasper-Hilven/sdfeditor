using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Operations;
using static SDFProject.Tests.SDFProgramTestHelpers;
using DissolveNode = SDFProject.SDFProgramOperator.DissolveNode;

namespace SDFProject.Tests;

public class DissolveNodeTests
{
    private static string NodeId<T>(SDFProgram program) where T : SDFEvaluable =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is T).Key;

    [Fact]
    public void Union_BecomesItsFirstChild_AndTheOtherChildrenAreRemoved()
    {
        var program = Program(new Translate(new Union(Sphere(1), Sphere(2), Sphere(3)), new ConstValueV3(new V3(1, 0, 0))));

        var result = SdfProgramOperator.ApplyOperation(program, new DissolveNode(NodeId<Union>(program)));

        Assert.Equal("Translate(Sphere(1), ConstValueV3)", DescribeScene(result));
        Assert.Single(result.pointers.SDFEvaluablePointers.Values.OfType<Sphere>());
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SmoothUnion_SkipsTheSmoothness()
    {
        var program = Program(new SmoothUnion(new ConstValueFloat(0.25f), Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new DissolveNode(NodeId<SmoothUnion>(program)));

        Assert.Equal("Sphere(1)", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void Translate_BecomesItsPrimitive()
    {
        var program = Program(new Union(new Translate(Sphere(1), new ConstValueV3(new V3(1, 0, 0))), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new DissolveNode(NodeId<Translate>(program)));

        Assert.Equal("Union(Sphere(1), Sphere(2))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void Primitive_CannotBeDissolved()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.False(ApplyDissolveNode.CanDissolve(program, SphereId(program, 1)));
        Assert.True(ApplyDissolveNode.CanDissolve(program, NodeId<Union>(program)));
    }

    [Fact]
    public void Interactive_SelectsTheFirstChild()
    {
        var program = Program(new Union(new Intersection(Sphere(1), Sphere(2)), Sphere(3)));
        var firstChild = SphereId(program, 1);

        var result = SdfInteractiveNodeProgramOperator.ApplyOperation(
            new SdfInteractiveNodeProgram(program, NodeId<Intersection>(program), 0), new InteractiveDissolveNode());

        Assert.Equal("Union(Sphere(1), Sphere(3))", DescribeScene(result.Program));
        Assert.Equal(firstChild, result.SelectedNodeId);
    }

    [Fact]
    public void TKey_DissolvesTheSelectionInHomeMode()
    {
        var program = Program(new Union(new Intersection(Sphere(1), Sphere(2)), Sphere(3)));
        var initial = new SDFPartialCommandNodeProgram(
            new SdfInteractiveNodeProgram(program, NodeId<Intersection>(program), 0),
            new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

        var result = SDFPartialCommandNodeProgramOperator.Operate(initial, FingerKeyLayouts.AzertyBeFingerKeyToIndex["t"]);

        Assert.Equal("Union(Sphere(1), Sphere(3))", DescribeScene(result.interactiveProgram.Program));
        Assert.Equal(FingerKeyLayouts.QwertyFingerKeyToIndex["t"], FingerKeyLayouts.AzertyBeFingerKeyToIndex["t"]);
    }
}
