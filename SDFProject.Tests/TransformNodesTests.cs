using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class TransformNodesTests
{
    private static readonly SdfCalculatorCpu Calculator = new();

    private static readonly SDFEvaluable LongBox = new Box(new ConstValueV3(new V3(2, 0.5f, 0.5f)));

    private static float Distance(SDFEvaluable primitive, float scale, V3 translation, V3 rotation, V3 position) =>
        Distance(primitive, new V3(scale, scale, scale), translation, rotation, position);

    private static float Distance(SDFEvaluable primitive, V3 scale, V3 translation, V3 rotation, V3 position) =>
        Calculator.Calculate(position, Program(
            new Translate(new Scale(new Rotate(primitive, new ConstValueV3(rotation)), new ConstValueV3(scale)), new ConstValueV3(translation))));

    private static readonly V3 NoRotation = new(0, 0, 0);

    private static readonly V3 QuarterTurnAroundZ = new(0, 0, MathF.PI / 2);

    [Fact]
    public void Translates()
    {
        Assert.Equal(1f, Distance(Sphere(1), 1, new V3(1, 2, 3), NoRotation, new V3(1, 4, 3)), 0.0001f);
    }

    [Fact]
    public void Rotates()
    {
        Assert.Equal(1f, Distance(LongBox, 1, new V3(0, 0, 0), QuarterTurnAroundZ, new V3(0, 3, 0)), 0.0001f);
    }

    [Fact]
    public void ScalesTheDistance()
    {
        Assert.Equal(2f, Distance(Sphere(1), 3, new V3(0, 0, 0), NoRotation, new V3(5, 0, 0)), 0.0001f);
    }

    [Fact]
    public void ScalesPerAxis()
    {
        Assert.Equal(.5f, Distance(Sphere(1), new V3(3, .5f, 1), new V3(0, 0, 0), NoRotation, new V3(0, 1, 0)), 0.0001f);
        Assert.InRange(Distance(Sphere(1), new V3(3, .5f, 1), new V3(0, 0, 0), NoRotation, new V3(5, 0, 0)), 0f, 2f);
    }

    [Fact]
    public void ScalesPerAxisAfterRotating()
    {
        Assert.Equal(0f, Distance(LongBox, new V3(1, 3, 1), new V3(0, 0, 0), QuarterTurnAroundZ, new V3(0, 6, 0)), 0.0001f);
    }

    [Fact]
    public void RotatesAroundItsOwnOriginAfterBeingMoved()
    {
        Assert.Equal(1f, Distance(LongBox, 2, new V3(10, 0, 0), QuarterTurnAroundZ, new V3(10, 5, 0)), 0.0001f);
    }

    [Fact]
    public void RotatesAroundXThenYThenZ()
    {
        Assert.Equal(1f, Distance(LongBox, 1, new V3(0, 0, 0), new V3(MathF.PI / 2, 0, MathF.PI / 2), new V3(0, 3, 0)), 0.0001f);
    }
}
