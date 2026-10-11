using SDFProject.Definitions;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class AdjustScaleTranslateRotateTests
{
    private static ScaleTranslateRotate Transform(SDFEvaluable primitive, float scale, V3 translation, V3 rotation) =>
        new(primitive, new ConstValueFloat(scale), new ConstValueV3(translation), new ConstValueV3(rotation));

    private static string TransformId(SDFProgram program) =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is ScaleTranslateRotate).Key;

    private static void AssertTransform(SDFProgram program, string id, float scale, V3 translation, V3 rotation)
    {
        var node = Assert.IsType<ScaleTranslateRotate>(program.pointers.SDFEvaluablePointers[id]);
        Assert.Equal(new ConstValueFloat(scale), node.scale);
        Assert.Equal(new ConstValueV3(translation), node.translation);
        Assert.Equal(new ConstValueV3(rotation), node.rotation);
    }

    private static readonly AdjustScaleTranslateRotate Adjustment = new("", 0.5f, new V3(1, 2, 3), new V3(0.1f, 0.2f, 0.3f));

    [Fact]
    public void OnScaleTranslateRotate_AddsValuesToIt()
    {
        var program = Program(new Union(Transform(Sphere(1), 2, new V3(1, 1, 1), new V3(1, 0, 0))));
        var id = TransformId(program);

        var result = Operator.ApplyOperation(program, Adjustment with { nodeId = id });

        AssertTransform(result, id, 2.5f, new V3(2, 3, 4), new V3(1.1f, 0.2f, 0.3f));
        Assert.Equal("Union(ScaleTranslateRotate(Sphere(1), 2.5, ConstValueV3, ConstValueV3))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void OnChildOfScaleTranslateRotate_AddsValuesToParent()
    {
        var program = Program(new Union(Transform(Sphere(1), 2, new V3(1, 1, 1), new V3(1, 0, 0))));
        var id = TransformId(program);

        var result = Operator.ApplyOperation(program, Adjustment with { nodeId = SphereId(program, 1) });

        AssertTransform(result, id, 2.5f, new V3(2, 3, 4), new V3(1.1f, 0.2f, 0.3f));
        Assert.Equal("Union(ScaleTranslateRotate(Sphere(1), 2.5, ConstValueV3, ConstValueV3))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void OnOtherNode_WrapsItInScaleTranslateRotate()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = Operator.ApplyOperation(program, Adjustment with { nodeId = SphereId(program, 2) });

        Assert.Equal("Union(Sphere(1), ScaleTranslateRotate(Sphere(2), 1.5, ConstValueV3, ConstValueV3))", DescribeScene(result));
        AssertTransform(result, TransformId(result), 1.5f, new V3(1, 2, 3), new V3(0.1f, 0.2f, 0.3f));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void NonConstantValue_Throws()
    {
        var transform = new ScaleTranslateRotate(Sphere(1), new VariableReferenceFloat("v"), new ConstValueV3(new V3(0, 0, 0)), new ConstValueV3(new V3(0, 0, 0)));
        var program = Program(new Union(transform));

        Assert.Throws<InvalidOperationException>(() => Operator.ApplyOperation(program, Adjustment with { nodeId = TransformId(program) }));
    }

    [Fact]
    public void UnknownNode_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<KeyNotFoundException>(() => Operator.ApplyOperation(program, Adjustment with { nodeId = "missing" }));
    }
}
