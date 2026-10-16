using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class AdjustTransformTests
{
    private static ConstValueV3 C(float x, float y, float z) => new(new V3(x, y, z));

    private static string IdOf<T>(SDFProgram program) where T : SDFEvaluable =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is T).Key;

    private static V3 ValueOf<T>(SDFProgram program) where T : SDFEvaluable =>
        program.pointers.SDFEvaluablePointers.Values.OfType<T>().Single() switch
        {
            Scale e => ((ConstValueV3)e.scale).v3,
            Rotate e => ((ConstValueV3)e.rotation).v3,
            Translate e => ((ConstValueV3)e.translation).v3,
            var e => throw new ArgumentException(e.GetType().Name),
        };

    private static SDFProgram Apply(SDFProgram program, string nodeId, TransformKind kind, V3 delta)
    {
        var result = SdfProgramOperator.ApplyOperation(program, new AdjustTransform(nodeId, kind, delta));
        AssertPointersConsistent(result);
        return result;
    }

    [Fact]
    public void OnPlainNode_AddsOnlyTheAdjustedTransform()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = Apply(program, SphereId(program, 2), TransformKind.Translate, new V3(1, 2, 3));

        Assert.Equal("Union(Sphere(1), Translate(Sphere(2), ConstValueV3))", DescribeScene(result));
        Assert.Equal(new V3(1, 2, 3), ValueOf<Translate>(result));
    }

    [Theory]
    [InlineData(TransformKind.Translate)]
    [InlineData(TransformKind.Scale)]
    [InlineData(TransformKind.Rotate)]
    public void MissingTransform_IsInsertedInTheOrderTranslateScaleRotate(TransformKind kind)
    {
        var program = Program(new Union(Sphere(1)));
        foreach (var other in Enum.GetValues<TransformKind>().Where(k => k != kind))
            program = Apply(program, SphereId(program, 1), other, new V3(.5f, .5f, .5f));

        var result = Apply(program, SphereId(program, 1), kind, new V3(.5f, .5f, .5f));

        Assert.Equal("Union(Translate(Scale(Rotate(Sphere(1), ConstValueV3), ConstValueV3), ConstValueV3))", DescribeScene(result));
    }

    [Theory]
    [InlineData(TransformKind.Translate)]
    [InlineData(TransformKind.Scale)]
    [InlineData(TransformKind.Rotate)]
    public void FromAnyNodeInTheChain_AdjustsTheExistingTransform(TransformKind selected)
    {
        var program = Program(new Union(new Translate(new Scale(new Rotate(Sphere(1), C(0, 0, 0.5f)), C(2, 2, 2)), C(1, 1, 1))));
        var selectedId = selected switch
        {
            TransformKind.Translate => IdOf<Translate>(program),
            TransformKind.Scale => IdOf<Scale>(program),
            _ => IdOf<Rotate>(program),
        };

        var result = Apply(program, selectedId, TransformKind.Scale, new V3(.5f, .5f, .5f));

        Assert.Equal(new V3(2.5f, 2.5f, 2.5f), ValueOf<Scale>(result));
        Assert.Equal(new V3(1, 1, 1), ValueOf<Translate>(result));
        Assert.Equal(new V3(0, 0, .5f), ValueOf<Rotate>(result));
    }

    [Fact]
    public void ReachingTheIdentity_RemovesTheTransform()
    {
        var program = Program(new Union(new Translate(new Scale(Sphere(1), C(1.5f, 1, 1)), C(1, 0, 0))));
        var scaleId = IdOf<Scale>(program);

        var result = Apply(program, SphereId(program, 1), TransformKind.Scale, new V3(-.5f, 0, 0));

        Assert.Equal("Union(Translate(Sphere(1), ConstValueV3))", DescribeScene(result));
        Assert.False(result.pointers.ContainsPointer(scaleId));
    }

    [Fact]
    public void ReachingTheIdentity_UpToRounding_RemovesTheTransform()
    {
        var program = Program(new Union(new Rotate(Sphere(1), C(0, 0, 15 * MathF.PI / 180))));

        var result = Apply(program, SphereId(program, 1), TransformKind.Rotate, new V3(0, 0, -MathF.PI / 12));

        Assert.Equal("Union(Sphere(1))", DescribeScene(result));
    }

    [Fact]
    public void IdentityDelta_OnPlainNode_AddsNothing()
    {
        var program = Program(new Union(Sphere(1)));

        var result = Apply(program, SphereId(program, 1), TransformKind.Translate, new V3(0, 0, 0));

        Assert.Equal("Union(Sphere(1))", DescribeScene(result));
    }

    [Fact]
    public void NonConstantValue_Throws()
    {
        var program = Program(new Union(new Translate(Sphere(1), new VariableReferenceV3("v"))));

        Assert.Throws<InvalidOperationException>(() =>
            SdfProgramOperator.ApplyOperation(program, new AdjustTransform(SphereId(program, 1), TransformKind.Translate, new V3(1, 0, 0))));
    }

    [Fact]
    public void UnknownNode_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<KeyNotFoundException>(() =>
            SdfProgramOperator.ApplyOperation(program, new AdjustTransform("missing", TransformKind.Translate, new V3(1, 0, 0))));
    }
}
