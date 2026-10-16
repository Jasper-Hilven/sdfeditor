using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using static SDFProject.Tests.SDFProgramTestHelpers;
using ToggleSmoothness = SDFProject.SDFInteractiveNode.ToggleSmoothness;
using SelectParent = SDFProject.SDFInteractiveNode.SelectParent;
using SelectFirstChild = SDFProject.SDFInteractiveNode.SelectFirstChild;
using SelectNext = SDFProject.SDFInteractiveNode.SelectNext;
using SelectPrevious = SDFProject.SDFInteractiveNode.SelectPrevious;

namespace SDFProject.Tests;

public class SDFInteractiveNodeProgramEvaluatorTests
{
    private static SdfInteractiveNodeProgram Interactive(SDFProgram program, string? selectedNodeId, int zoomLevel = 0) =>
        new(program, selectedNodeId, zoomLevel);

    private static string UnionId(SDFProgram program) =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is Union).Key;

    [Fact]
    public void ToggleSmoothness_OnlyOnUnionsIntersectionsAndSubtractions()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        Assert.True(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(Interactive(program, UnionId(program)), new ToggleSmoothness()));
        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(Interactive(program, SphereId(program, 1)), new ToggleSmoothness()));
    }

    [Fact]
    public void Selection_OnlyWhenThereIsSomewhereToMove()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));
        var root = Interactive(program, UnionId(program));
        var lastSphere = Interactive(program, SphereId(program, 2));

        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(root, new SelectParent()));
        Assert.True(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(root, new SelectFirstChild()));
        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(lastSphere, new SelectNext()));
        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(lastSphere, new SelectFirstChild()));
        Assert.True(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(lastSphere, new SelectPrevious()));
    }

    [Fact]
    public void Zoom_OnlyWithinTheZoomLevels()
    {
        var program = Program(Sphere(1));

        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(Interactive(program, null), new DecreaseStrZoomLevel()));
        Assert.True(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(Interactive(program, null), new IncreaseStrZoomLevel()));
        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(
            Interactive(program, null, SdfInteractiveNodeProgramOperator.MaxStrZoomLevel), new IncreaseStrZoomLevel()));
    }

    [Fact]
    public void NodeOperations_NeedASelection()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(Interactive(program, null), new ScaleUpAtCurrentZoomLevel()));
        Assert.True(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(Interactive(program, SphereId(program, 1)), new ScaleUpAtCurrentZoomLevel()));
    }

    [Fact]
    public void Extract_AnywhereInTheScene_InlineOnlyInsideAVariable()
    {
        var program = Program(Sphere(1));
        var sphere = Interactive(program, SphereId(program, 1));

        Assert.True(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(sphere, new InteractiveExtractVariable("v")));
        Assert.False(SDFInteractiveNodeProgramEvaluator.IsActionAvailable(sphere, new InteractiveInlineVariable()));
    }

    [Fact]
    public void Keys_UnboundOrSwitchingToTheCurrentModeAreUnavailable()
    {
        var program = new SDFPartialCommandNodeProgram(
            SdfInteractiveNodeProgramConstants.GetInitialProgram(),
            new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

        Assert.False(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(program, InputKey.LPinkHome));
        Assert.True(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(program, InputKey.RMiddleDown));
        Assert.False(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(program, InputKey.Enter));
        Assert.False(SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(program, InputKey.LMiddleHome));
    }
}
