using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class CleanEmptyNodesFromUnionsAndIntersectionsTests
{
    [Fact]
    public void RemovesEmptyNodesFromUnionsAndIntersections()
    {
        var program = Program(new Union(new Empty(), Sphere(1), new Intersection(Sphere(2), new Empty())));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1), Intersection(Sphere(2)))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void UnionOfOnlyEmptyNodes_BecomesEmpty()
    {
        var program = Program(new Union(Sphere(1), new Union(new Empty(), new Empty())));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void NestedUnionsOfOnlyEmptyNodes_AreAllRemoved()
    {
        var program = Program(new Union(Sphere(1), new Union(new Intersection(new Empty()), new Union())));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SmoothUnionOfOnlyEmptyNodes_BecomesEmpty()
    {
        var program = Program(new Union(Sphere(1), new SmoothUnion(new ConstValueFloat(0.1f), new Empty())));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SubtractionOfEmptyNode_BecomesItsFirstChild()
    {
        var program = Program(new Union(new Subtraction(Sphere(1), new Union(new Empty()))));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SceneOfOnlyEmptyNodes_BecomesEmpty()
    {
        var program = Program(new Union(new Empty(), new Intersection(new Empty())));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.IsType<Empty>(result.scene);
        AssertPointersConsistent(result);
    }

    [Fact]
    public void CleansFunctionBodies()
    {
        var program = Program(new Empty(), Function(new Union(Sphere(1), new Empty())));

        var result = Operator.ApplyOperation(program, new CleanEmptyNodesFromUnionsAndIntersections());

        Assert.Equal("Union(Sphere(1))", DescribeBody(result));
        AssertPointersConsistent(result);
    }
}
