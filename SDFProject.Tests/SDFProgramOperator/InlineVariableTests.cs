using SDFProject.Definitions;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class InlineVariableTests
{
    [Fact]
    public void UndoesExtractVariable()
    {
        var program = Program(new Empty(), Function(new Union(Sphere(1), Sphere(2))));
        var extracted = Operator.ApplyOperation(program, new ExtractVariable(SphereId(program, 2), "v"));

        var result = Operator.ApplyOperation(extracted, new InlineVariable(extracted.functions[0].variables[0].id));

        Assert.Empty(result.functions[0].variables);
        Assert.Equal("Union(Sphere(1), Sphere(2))", DescribeBody(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void UsedTwice_GivesEachUseItsOwnNodes()
    {
        var variable = new VariableDeclaration("v", "var", Sphere(1));
        var program = Program(new Empty(), Function(new Union(new VariableReferenceSDF("var"), new VariableReferenceSDF("var")), variable));

        var result = Operator.ApplyOperation(program, new InlineVariable("var"));

        Assert.Equal("Union(Sphere(1), Sphere(1))", DescribeBody(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void Unused_RemovesVariableAndItsPointers()
    {
        var variable = new VariableDeclaration("v", "var", Sphere(1));
        var program = Program(new Empty(), Function(new Union(Sphere(2)), variable));

        var result = Operator.ApplyOperation(program, new InlineVariable("var"));

        Assert.Empty(result.functions[0].variables);
        Assert.Equal("Union(Sphere(2))", DescribeBody(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void UnknownVariable_Throws()
    {
        var program = Program(new Empty(), Function(new Union(Sphere(1))));

        Assert.Throws<KeyNotFoundException>(() => Operator.ApplyOperation(program, new InlineVariable("missing")));
    }
}
