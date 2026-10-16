using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class SDFPartialAddNodeTests
{
    private static SDFPartialCommandNodeProgram Initial() => new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(Home));

    private static InputKey KeyFor(SDFPartialCommandNodeProgramMode mode, ISDFPartialAction action) =>
        SDFPartialKeyBindings.Bindings.Single(b => b.Key.Mode == mode && b.Value.Equals(action)).Key.Key;

    private static SDFPartialCommandNodeProgram Press(SDFPartialCommandNodeProgram program, ISDFPartialAction action) =>
        SDFPartialCommandNodeProgramOperator.Operate(program, KeyFor(program.state.Mode, action));

    [Fact]
    public void EveryNodeType_HasAKeyOnOneOfTheAddNodePages()
    {
        var bound = SDFPartialKeyBindings.Bindings
            .Where(b => b.Key.Mode is SDFPartialCommandNodeProgramMode.AddNode or AddNodeMore)
            .Select(b => SDFPartialCommandNodeProgramOperator.NodeTypeToAdd(b.Value))
            .OfType<SDFNodeType>();

        Assert.Equal(Enum.GetValues<SDFNodeType>().Order(), bound.Order());
    }

    [Fact]
    public void AddingANode_AddsItNextToTheSelectionAndReturnsHome()
    {
        var adding = Press(Initial(), new AddNode());
        Assert.Equal(SDFPartialCommandNodeProgramMode.AddNode, adding.state.Mode);

        var result = Press(adding, new AddSphere());

        Assert.Equal(Home, result.state.Mode);
        Assert.Equal("Union(Box(ConstValueV3), Sphere(0.5))", DescribeScene(result.interactiveProgram.Program));
    }

    [Fact]
    public void NodeTypesOnTheSecondPage_CanBeAdded()
    {
        var more = Press(Press(Initial(), new AddNode()), new ShowMoreNodeTypes());
        Assert.Equal(AddNodeMore, more.state.Mode);

        var result = Press(more, new AddOctahedronBound());

        Assert.Equal(Home, result.state.Mode);
        Assert.Contains("OctahedronBound", DescribeScene(result.interactiveProgram.Program));
    }

    [Fact]
    public void Cancel_ReturnsHomeWithoutChangingTheProgram()
    {
        var initial = Initial();

        var result = Press(Press(initial, new AddNode()), new CancelAddNode());

        Assert.Equal(Home, result.state.Mode);
        Assert.Same(initial.interactiveProgram, result.interactiveProgram);
    }

    [Fact]
    public void StartAddNode_NeedsASelectedNode()
    {
        var program = Initial() with { interactiveProgram = Initial().interactiveProgram with { SelectedNodeId = null } };

        Assert.False(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(program, KeyFor(Home, new AddNode())));
    }

    [Fact]
    public void EveryNodeType_HasAKeyOnOneOfTheReplaceNodePages()
    {
        var bound = SDFPartialKeyBindings.Bindings
            .Where(b => b.Key.Mode is SDFPartialCommandNodeProgramMode.ReplaceNode or ReplaceNodeMore)
            .Select(b => SDFPartialCommandNodeProgramOperator.NodeTypeToReplaceWith(b.Value))
            .OfType<SDFNodeType>();

        Assert.Equal(Enum.GetValues<SDFNodeType>().Order(), bound.Order());
    }

    [Fact]
    public void ReplacingANode_KeepsItSelectedAndReturnsHome()
    {
        var initial = Initial();
        var replacing = Press(initial, new ReplaceNode());
        Assert.Equal(SDFPartialCommandNodeProgramMode.ReplaceNode, replacing.state.Mode);

        var result = Press(Press(replacing, new ShowMoreReplaceNodeTypes()), new ReplaceWithOctahedronBound());

        Assert.Equal(Home, result.state.Mode);
        Assert.Equal("OctahedronBound(0.5)", DescribeScene(result.interactiveProgram.Program));
        Assert.Equal(initial.interactiveProgram.SelectedNodeId, result.interactiveProgram.SelectedNodeId);
        AssertPointersConsistent(result.interactiveProgram.Program);
    }

    [Fact]
    public void CancelReplace_ReturnsHomeWithoutChangingTheProgram()
    {
        var initial = Initial();

        var result = Press(Press(initial, new ReplaceNode()), new CancelReplaceNode());

        Assert.Equal(Home, result.state.Mode);
        Assert.Same(initial.interactiveProgram, result.interactiveProgram);
    }
}
