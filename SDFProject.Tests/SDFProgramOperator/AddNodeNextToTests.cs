using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class AddNodeNextToTests
{
    [Fact]
    public void InsideUnion_InsertsSiblingAfterNode()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new AddNodeNextTo(SphereId(program, 1), SDFNodeType.Sphere));

        Assert.Equal("Union(Sphere(1), Sphere(0.5), Sphere(2))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void OutsideUnion_WrapsNodeInUnion()
    {
        var program = Program(new Intersection(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new AddNodeNextTo(SphereId(program, 2), SDFNodeType.Sphere));

        Assert.Equal("Intersection(Sphere(1), Union(Sphere(2), Sphere(0.5)))", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void UnknownNode_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<KeyNotFoundException>(() => SdfProgramOperator.ApplyOperation(program, new AddNodeNextTo("missing", SDFNodeType.Sphere)));
    }
}
