using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class ScaleTranslateRotateTests
{
    private static readonly SdfCalculatorCpu Calculator = new();

    // A box that is long along the x-axis
    private static readonly SDFEvaluable LongBox = new Box(new ConstValueV3(new V3(2, 0.5f, 0.5f)));

    private static float Distance(SDFEvaluable primitive, float scale, V3 translation, V3 rotation, V3 position) =>
        Calculator.Calculate(position, Program(new ScaleTranslateRotate(
            primitive,
            new ConstValueFloat(scale),
            new ConstValueV3(translation),
            new ConstValueV3(rotation))));

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
        // Rotated a quarter turn around z, the box is long along the y-axis
        Assert.Equal(1f, Distance(LongBox, 1, new V3(0, 0, 0), QuarterTurnAroundZ, new V3(0, 3, 0)), 0.0001f);
    }

    [Fact]
    public void ScalesTheDistance()
    {
        Assert.Equal(2f, Distance(Sphere(1), 3, new V3(0, 0, 0), NoRotation, new V3(5, 0, 0)), 0.0001f);
    }

    [Fact]
    public void RotatesAroundItsOwnOriginAfterBeingMoved()
    {
        // Scaled to length 4 and turned to lie along y, centered on the offset instead of swinging around the world origin
        Assert.Equal(1f, Distance(LongBox, 2, new V3(10, 0, 0), QuarterTurnAroundZ, new V3(10, 5, 0)), 0.0001f);
    }

    [Fact]
    public void RotatesAroundXThenYThenZ()
    {
        // Turning around x leaves the box along x, turning around z afterwards puts it along y.
        // In the opposite order it would end up along z
        Assert.Equal(1f, Distance(LongBox, 1, new V3(0, 0, 0), new V3(MathF.PI / 2, 0, MathF.PI / 2), new V3(0, 3, 0)), 0.0001f);
    }
}
