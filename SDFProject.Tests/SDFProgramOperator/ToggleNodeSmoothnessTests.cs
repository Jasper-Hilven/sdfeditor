using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class ToggleNodeSmoothnessTests
{
    private static string NodeId<T>(SDFProgram program) where T : SDFEvaluable =>
        program.pointers.SDFEvaluablePointers.Single(e => e.Value is T).Key;

    [Fact]
    public void Union_BecomesSmoothUnionWithDefaultSmoothness()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new ToggleNodeSmoothness(NodeId<Union>(program)));

        Assert.Equal("SmoothUnion(0.25, Sphere(1), Sphere(2))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void Subtraction_BecomesSmoothSubtraction()
    {
        var program = Program(new Subtraction(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new ToggleNodeSmoothness(NodeId<Subtraction>(program)));

        Assert.Equal("SmoothSubtraction(Sphere(1), Sphere(2), 0.25)", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SmoothIntersection_BecomesIntersectionAndDropsSmoothness()
    {
        var program = Program(new SmoothIntersection(new ConstValueFloat(0.5f), Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new ToggleNodeSmoothness(NodeId<SmoothIntersection>(program)));

        Assert.Equal("Intersection(Sphere(1), Sphere(2))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void TogglingTwice_GivesTheOriginalBack()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));
        var id = NodeId<Union>(program);

        var once = SdfProgramOperator.ApplyOperation(program, new ToggleNodeSmoothness(id));
        var twice = SdfProgramOperator.ApplyOperation(once, new ToggleNodeSmoothness(id));

        Assert.Equal("Union(Sphere(1), Sphere(2))", DescribeScene(twice));
        AssertPointersConsistent(twice);
    }

    [Fact]
    public void Primitive_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<InvalidOperationException>(() =>
            SdfProgramOperator.ApplyOperation(program, new ToggleNodeSmoothness(SphereId(program, 1))));
    }
}
