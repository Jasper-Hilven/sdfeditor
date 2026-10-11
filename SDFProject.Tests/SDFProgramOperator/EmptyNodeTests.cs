using SDFProject.Definitions;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class EmptyNodeTests
{
    [Fact]
    public void ReplacesNodeAndRemovesItsPointers()
    {
        var program = Program(new Union(Sphere(1), new Elongate(Sphere(2), new ConstValueV3(new V3(1, 0, 0)))));
        var elongateId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is Elongate).Key;

        var result = Operator.ApplyOperation(program, new EmptyNode(elongateId));

        Assert.Equal("Union(Sphere(1), Empty)", DescribeScene(result));
        Assert.False(result.pointers.ContainsPointer(elongateId));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void UnknownNode_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<KeyNotFoundException>(() => Operator.ApplyOperation(program, new EmptyNode("missing")));
    }
}
