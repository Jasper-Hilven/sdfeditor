using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Operations;
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
    public void NodeInScene_BecomesSceneVariable()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new ExtractVariable(SphereId(program, 2), "v"));

        var variable = Assert.Single(result.sceneVariables);
        Assert.Equal("v", variable.name);
        Assert.Equal("Sphere(2)", Describe(result, variable.value));
        Assert.Equal($"Union(Sphere(1), ${variable.id})", DescribeScene(result));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void NodeInSceneVariable_IsDeclaredBeforeThatVariable()
    {
        var program = Program(new Union(new Round(Sphere(1), new ConstValueFloat(0.1f))));
        var roundId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is Round).Key;

        var outer = SdfProgramOperator.ApplyOperation(program, new ExtractVariable(roundId, "outer"));
        var result = SdfProgramOperator.ApplyOperation(outer, new ExtractVariable(SphereId(outer, 1), "inner"));

        Assert.Equal(["inner", "outer"], result.sceneVariables.Select(v => v.name));
        Assert.Equal($"Round(${result.sceneVariables[0].id}, 0.1)", Describe(result, result.sceneVariables[1].value));
        AssertPointersConsistent(result);
    }

    [Fact]
    public void SceneVariable_RoundTripsThroughInline()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var extracted = SdfProgramOperator.ApplyOperation(program, new ExtractVariable(SphereId(program, 2), "v"));
        var inlined = SdfProgramOperator.ApplyOperation(extracted, new InlineVariable(extracted.sceneVariables[0].id));

        Assert.Empty(inlined.sceneVariables);
        Assert.Equal("Union(Sphere(1), Sphere(2))", DescribeScene(inlined));
        Assert.Equal(SphereId(program, 2), SphereId(inlined, 2));
        AssertPointersConsistent(inlined);
    }

    [Fact]
    public void UnusedVariableName_SkipsTakenNames()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));
        Assert.Equal("var1", ApplyExtractVariable.UnusedVariableName(program));

        var result = SdfProgramOperator.ApplyOperation(program, new ExtractVariable(SphereId(program, 2), "var1"));

        Assert.Equal("var2", ApplyExtractVariable.UnusedVariableName(result));
    }
}
