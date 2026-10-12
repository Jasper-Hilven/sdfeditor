using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class ExtractVariableTests
{
    [Fact]
    public void MovesNodeIntoVariableAndReferencesIt()
    {
        var program = Program(new Empty(), Function(new Union(Sphere(1), Sphere(2))));

        var result = SdfProgramOperator.ApplyOperation(program, new ExtractVariable(SphereId(program, 2), "v"));

        var variable = Assert.Single(result.Functions[0].variables);
        Assert.Equal("v", variable.name);
        Assert.Equal("Sphere(2)", Describe(result, variable.value));
        Assert.Equal($"Union(Sphere(1), ${variable.id})", DescribeBody(result));
        Assert.False(result.pointers.ContainsPointer(variable.id));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void NodeInScene_Throws()
    {
        var program = Program(new Union(Sphere(1)));

        Assert.Throws<InvalidOperationException>(() => SdfProgramOperator.ApplyOperation(program, new ExtractVariable(SphereId(program, 1), "v")));
    }
}
