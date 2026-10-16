using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class NodeParametersTests
{
    [Fact]
    public void Of_ListsValueParametersInDeclaredOrder()
    {
        var parameters = NodeParameters.Of(new CappedCylinder(new ConstValueFloat(1), new ConstValueFloat(2)));

        Assert.Equal(["radius", "height"], parameters.Select(p => p.Name));
        Assert.All(parameters, p => Assert.Equal(NodeParameterKind.Number, p.Kind));
    }

    [Fact]
    public void Of_LeavesOutChildNodes()
    {
        var parameters = NodeParameters.Of(new SmoothUnion(new ConstValueFloat(0.25f), Sphere(1), Sphere(2)));

        Assert.Equal(["smoothness"], parameters.Select(p => p.Name));
    }

    [Fact]
    public void SinCos_IsAnAngleInDegrees()
    {
        var thirty = MathF.PI / 6;
        var parameter = NodeParameters.Of(new Cone(new ConstValueV2(new V2(MathF.Sin(thirty), MathF.Cos(thirty))), new ConstValueFloat(1)))[0];

        Assert.Equal(NodeParameterKind.Angle, parameter.Kind);
        Assert.Equal(30f, Assert.Single(parameter.Components()), 0.001f);
    }

    [Fact]
    public void With_ReplacesOnlyThatParameter()
    {
        var node = NodeParameters.With(new CappedCylinder(new ConstValueFloat(1), new ConstValueFloat(2)), "height", [3]);

        Assert.Equal(new CappedCylinder(new ConstValueFloat(1), new ConstValueFloat(3)), node);
    }

    [Fact]
    public void With_AngleWritesSineAndCosine()
    {
        var node = (Cone)NodeParameters.With(new Cone(new ConstValueV2(new V2(0, 1)), new ConstValueFloat(1)), "sinCos", [90]);

        var sinCos = ((ConstValueV2)node.sinCos).v2;
        Assert.Equal(1f, sinCos.x, 0.0001f);
        Assert.Equal(0f, sinCos.y, 0.0001f);
    }

    [Fact]
    public void VariableReference_IsNotConstantAndCannotBeSet()
    {
        var node = new Sphere(new VariableReferenceFloat("r"));

        Assert.False(NodeParameters.Of(node)[0].IsConstant);
        Assert.Throws<InvalidOperationException>(() => NodeParameters.With(node, "radius", [1]));
    }

    [Fact]
    public void EveryNodeType_HasAtMostFourParameters()
    {
        Assert.All(Enum.GetValues<SDFNodeType>(), type =>
            Assert.InRange(NodeParameters.Of(SDFNodeDefaultValues.CreateNode(type)).Count, 1, 4));
    }

    [Fact]
    public void SetNodeParameter_ChangesTheNodeInTheProgram()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfProgramOperator.ApplyOperation(program, new SetNodeParameter(SphereId(program, 2), "radius", [3]));

        Assert.Equal("Union(Sphere(1), Sphere(3))", DescribeScene(result));
        AssertPointersConsistent(result);
    }
}
