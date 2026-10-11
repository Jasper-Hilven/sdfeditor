using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFProgramOperator;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class SdfInteractiveNodeProgramOperatorTests
{
    private static readonly SdfInteractiveNodeProgramOperator InteractiveNodeOperator = new();

    private static SdfInteractiveNodeProgram Interactive(SDFProgram program, string? selectedNodeId) => new(program, selectedNodeId, 0);

    private static string Selected(SdfInteractiveNodeProgram program) =>
        Describe(program.Program, new SDFEvaluablePointerable(program.SelectedNodeId!));

    private static string NodeId<T>(SDFProgram program) where T : SDFEvaluable =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is T).Key;

    #region Interactive operations

    [Fact]
    public void AddNodeNextTo_AddsNextToSelectedNodeAndSelectsIt()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, SphereId(program, 1)), new InteractiveAddNodeNextTo(SDFNodeType.Sphere));

        Assert.Equal("Union(Sphere(1), Sphere(0.5), Sphere(2))", DescribeScene(result.Program));
        Assert.Equal("Sphere(0.5)", Selected(result));
    }

    [Fact]
    public void AddNodeNextTo_WrappedInNewUnion_SelectsAddedNode()
    {
        var program = Program(new Intersection(Sphere(1), Sphere(2)));

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, SphereId(program, 2)), new InteractiveAddNodeNextTo(SDFNodeType.Sphere));

        Assert.Equal("Intersection(Sphere(1), Union(Sphere(2), Sphere(0.5)))", DescribeScene(result.Program));
        Assert.Equal("Sphere(0.5)", Selected(result));
    }

    [Fact]
    public void ExtractVariable_ExtractsSelectedNodeAndKeepsItSelected()
    {
        var program = Program(new Empty(), Function(new Union(Sphere(1), Sphere(2))));
        var selected = SphereId(program, 2);

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, selected), new InteractiveExtractVariable("v"));

        var variable = Assert.Single(result.Program.Functions[0].variables);
        Assert.Equal($"Union(Sphere(1), ${variable.id})", DescribeBody(result.Program));
        Assert.Equal(selected, result.SelectedNodeId);
    }

    [Fact]
    public void InlineVariable_InlinesVariableContainingSelectedNode()
    {
        var program = Program(new Empty(), Function(new Union(Sphere(1), Sphere(2))));
        var selected = SphereId(program, 2);
        var extracted = InteractiveNodeOperator.ApplyOperation(Interactive(program, selected), new InteractiveExtractVariable("v"));

        var result = InteractiveNodeOperator.ApplyOperation(extracted, new InteractiveInlineVariable());

        Assert.Empty(result.Program.Functions[0].variables);
        Assert.Equal("Union(Sphere(1), Sphere(2))", DescribeBody(result.Program));
        Assert.Equal(selected, result.SelectedNodeId);
    }

    [Fact]
    public void InlineVariable_SelectedNodeNotInVariable_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<InvalidOperationException>(() =>
            InteractiveNodeOperator.ApplyOperation(Interactive(program, SphereId(program, 1)), new InteractiveInlineVariable()));
    }

    [Fact]
    public void EmptyNode_EmptiesSelectedNodeAndSelectsParent()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, SphereId(program, 1)), new InteractiveEmptyNode());

        Assert.Equal("Union(Empty, Sphere(2))", DescribeScene(result.Program));
        Assert.Equal(NodeId<Union>(program), result.SelectedNodeId);
    }

    [Fact]
    public void CleanEmptyNodes_WorksWithoutSelection()
    {
        var program = Program(new Union(Sphere(1), new Empty()));

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, null), new InteractiveCleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1))", DescribeScene(result.Program));
        Assert.Null(result.SelectedNodeId);
    }

    [Fact]
    public void AdjustScaleTranslateRotate_WrapsSelectedNodeAndKeepsItSelected()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));
        var selected = SphereId(program, 1);

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, selected),
            new InteractiveAdjustScaleTranslateRotate(1, new V3(1, 0, 0), new V3(0, 0, 0)));

        Assert.Equal("Union(ScaleTranslateRotate(Sphere(1), 2, ConstValueV3, ConstValueV3), Sphere(2))", DescribeScene(result.Program));
        Assert.Equal(selected, result.SelectedNodeId);
    }

    [Fact]
    public void OperationNeedingSelection_WithoutSelection_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<InvalidOperationException>(() => InteractiveNodeOperator.ApplyOperation(Interactive(program, null), new InteractiveEmptyNode()));
    }

    [Fact]
    public void KeepsStrZoomLevel()
    {
        var program = Program(new Union(Sphere(1)));

        var result = InteractiveNodeOperator.ApplyOperation(new SdfInteractiveNodeProgram(program, SphereId(program, 1), 3), new InteractiveEmptyNode());

        Assert.Equal(3, result.StrZoomLevel);
    }

    #endregion

    #region Zoom level

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public void IncreaseStrZoomLevel_ZoomsInUpToMaximum(int level, int expected)
    {
        var program = Program(new Union(Sphere(1)));

        var result = InteractiveNodeOperator.ApplyOperation(new SdfInteractiveNodeProgram(program, null, level), new IncreaseStrZoomLevel());

        Assert.Equal(expected, result.StrZoomLevel);
        Assert.Same(program, result.Program);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    public void DecreaseStrZoomLevel_ZoomsOutDownToZero(int level, int expected)
    {
        var program = Program(new Union(Sphere(1)));

        var result = InteractiveNodeOperator.ApplyOperation(new SdfInteractiveNodeProgram(program, null, level), new DecreaseStrZoomLevel());

        Assert.Equal(expected, result.StrZoomLevel);
    }

    [Fact]
    public void ZoomFullyOutStrZoomLevel_ResetsToZero()
    {
        var program = Program(new Union(Sphere(1)));

        var result = InteractiveNodeOperator.ApplyOperation(new SdfInteractiveNodeProgram(program, null, 3), new ZoomFullyOutStrZoomLevel());

        Assert.Equal(0, result.StrZoomLevel);
    }

    #endregion

    #region Scale, translate and rotate at the current zoom level

    private static ScaleTranslateRotate AdjustSelectedSphere(int zoomLevel, ISdfInteractiveNodeProgramOperation operation)
    {
        var program = Program(new Union(Sphere(1)));
        var result = InteractiveNodeOperator.ApplyOperation(new SdfInteractiveNodeProgram(program, SphereId(program, 1), zoomLevel), operation);
        return (ScaleTranslateRotate)result.Program.pointers.SDFEvaluablePointers.Values.Single(n => n is ScaleTranslateRotate);
    }

    private static void AssertTransform(ScaleTranslateRotate node, float scale, V3 translation, V3 rotation)
    {
        Assert.Equal(scale, ((ConstValueFloat)node.scale).value, 0.0001f);
        AssertV3(translation, ((ConstValueV3)node.translation).v3);
        AssertV3(rotation, ((ConstValueV3)node.rotation).v3);
    }

    private static void AssertV3(V3 expected, V3 actual)
    {
        Assert.Equal(expected.x, actual.x, 0.0001f);
        Assert.Equal(expected.y, actual.y, 0.0001f);
        Assert.Equal(expected.z, actual.z, 0.0001f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ScaleUpAndDown_UseScaleStepOfZoomLevel(int zoomLevel)
    {
        var step = SdfInteractiveNodeProgramOperator.TranslateZoomScaleToScale(zoomLevel);

        AssertTransform(AdjustSelectedSphere(zoomLevel, new ScaleUpAtCurrentZoomLevel()), 1 + step, new V3(0, 0, 0), new V3(0, 0, 0));
        AssertTransform(AdjustSelectedSphere(zoomLevel, new ScaleDownAtCurrentZoomLevel()), 1 - step, new V3(0, 0, 0), new V3(0, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Translate_MovesAlongAxisByTranslationStepOfZoomLevel(int zoomLevel)
    {
        var step = SdfInteractiveNodeProgramOperator.TranslateZoomScaleToTranslation(zoomLevel);
        var none = new V3(0, 0, 0);

        AssertTransform(AdjustSelectedSphere(zoomLevel, new TranslateXUpAtCurrentZoomLevel()), 1, new V3(step, 0, 0), none);
        AssertTransform(AdjustSelectedSphere(zoomLevel, new TranslateXDownAtCurrentZoomLevel()), 1, new V3(-step, 0, 0), none);
        AssertTransform(AdjustSelectedSphere(zoomLevel, new TranslateYUpAtCurrentZoomLevel()), 1, new V3(0, step, 0), none);
        AssertTransform(AdjustSelectedSphere(zoomLevel, new TranslateYDownAtCurrentZoomLevel()), 1, new V3(0, -step, 0), none);
        AssertTransform(AdjustSelectedSphere(zoomLevel, new TranslateZUpAtCurrentZoomLevel()), 1, new V3(0, 0, step), none);
        AssertTransform(AdjustSelectedSphere(zoomLevel, new TranslateZDownAtCurrentZoomLevel()), 1, new V3(0, 0, -step), none);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Rotate_TurnsAroundAxisByDegreeStepOfZoomLevelInRadians(int zoomLevel)
    {
        var step = SdfInteractiveNodeProgramOperator.TranslateZoomScaleToDegree(zoomLevel) * MathF.PI / 180;
        var none = new V3(0, 0, 0);

        AssertTransform(AdjustSelectedSphere(zoomLevel, new RotateXUpAtCurrentZoomLevel()), 1, none, new V3(step, 0, 0));
        AssertTransform(AdjustSelectedSphere(zoomLevel, new RotateXDownAtCurrentZoomLevel()), 1, none, new V3(-step, 0, 0));
        AssertTransform(AdjustSelectedSphere(zoomLevel, new RotateYUpAtCurrentZoomLevel()), 1, none, new V3(0, step, 0));
        AssertTransform(AdjustSelectedSphere(zoomLevel, new RotateYDownAtCurrentZoomLevel()), 1, none, new V3(0, -step, 0));
        AssertTransform(AdjustSelectedSphere(zoomLevel, new RotateZUpAtCurrentZoomLevel()), 1, none, new V3(0, 0, step));
        AssertTransform(AdjustSelectedSphere(zoomLevel, new RotateZDownAtCurrentZoomLevel()), 1, none, new V3(0, 0, -step));
    }

    [Fact]
    public void RotateZUp_AtZoomLevelZero_TurnsAQuarter()
    {
        AssertTransform(AdjustSelectedSphere(0, new RotateZUpAtCurrentZoomLevel()), 1, new V3(0, 0, 0), new V3(0, 0, MathF.PI / 2));
    }

    [Fact]
    public void RepeatedTranslate_AddsUpOnSameNode()
    {
        var program = Program(new Union(Sphere(1)));
        var interactive = new SdfInteractiveNodeProgram(program, SphereId(program, 1), 0);

        interactive = InteractiveNodeOperator.ApplyOperation(interactive, new TranslateXUpAtCurrentZoomLevel());
        interactive = InteractiveNodeOperator.ApplyOperation(interactive, new TranslateXUpAtCurrentZoomLevel());

        var node = Assert.Single(interactive.Program.pointers.SDFEvaluablePointers.Values.OfType<ScaleTranslateRotate>());
        AssertV3(new V3(2 * SdfInteractiveNodeProgramOperator.TranslateZoomScaleToTranslation(0), 0, 0), ((ConstValueV3)node.translation).v3);
    }

    #endregion

    #region Selection after an operation

    [Fact]
    public void SelectsNearestSurvivingAncestorWhenAncestorsAreRemovedToo()
    {
        var program = Program(new Intersection(Sphere(1), new Union(new Union(new Empty()))));
        var innerUnionId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is Union { primitives: [Empty] }).Key;

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, innerUnionId), new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal(NodeId<Intersection>(program), result.SelectedNodeId);
    }

    [Fact]
    public void ClearsSelectionWhenNodeAndAllAncestorsAreRemoved()
    {
        var program = Program(new Union(Sphere(1)));

        var result = InteractiveNodeOperator.ApplyOperation(Interactive(program, SphereId(program, 1)), new EmptyNode(NodeId<Union>(program)));

        Assert.Null(result.SelectedNodeId);
    }

    #endregion
}
