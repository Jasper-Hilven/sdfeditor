using System.Buffers.Binary;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Rendering;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Tests.Rendering;

public class RenderTests
{
    private const float Tolerance = 0.15f;

    private static SDFProgram Read(string text) => SdfProgramFile.Read("format 1\n" + text);

    private static void AssertNear(float expected, float actual) => Assert.InRange(actual, expected - Tolerance, expected + Tolerance);

    [Fact]
    public void Frame_CentersOnTheModelAndFitsItInTheAxisViews()
    {
        var framing = RenderFramer.Frame(Read(
            """
            Translate translation=(0, 2, 0)
                Sphere radius=1
            """));

        AssertNear(0, framing.Camera.Target[0]);
        AssertNear(2, framing.Camera.Target[1]);
        AssertNear(0, framing.Camera.Target[2]);
        AssertNear(1.15f, framing.Camera.OrthoScale);
        Assert.NotNull(framing.Bounds);
    }

    [Fact]
    public void Frame_LeavesOutTheGroundPlane()
    {
        var framing = RenderFramer.Frame(Read(
            """
            Union
                Translate translation=(0, 1, 0)
                    Sphere radius=0.5
                Plane normal=(0, 1, 0) height=0
            """));

        AssertNear(1, framing.Camera.Target[1]);
        AssertNear(0.575f, framing.Camera.OrthoScale);
    }

    [Fact]
    public void Frame_WithNothingFinite_UsesTheIdeCamera()
    {
        var framing = RenderFramer.Frame(Read("Plane normal=(0, 1, 0) height=0"));

        Assert.Null(framing.Bounds);
        Assert.Equal(RenderCamera.Default, framing.Camera);
    }

    [Fact]
    public void Frame_DoesNotChangeTheProgram()
    {
        var program = Read(
            """
            Union
                Sphere radius=1
                Plane normal=(0, 1, 0) height=0
            """);
        var text = SdfProgramFile.Write(program);

        RenderFramer.Frame(program);

        Assert.Equal(text, SdfProgramFile.Write(program));
        Assert.Contains(new PointerTables(program.pointers).Expand(program.scene) is Union u ? u.primitives : [], p => p is Plane);
    }

    /// <summary>Needs Chrome: renders for real, so it also proves the generated shader compiles in WebGL.</summary>
    [Fact]
    public async Task Render_MakesAPngOfTheRequestedSize()
    {
        var program = Read(
            """
            Color color=(0.9, 0.2, 0.2)
                Sphere radius=0.5
            """);

        var rendered = await SdfRenderer.RenderAsync(program, new RenderOptions { Size = 256 });

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], rendered.Png[..4]);
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(rendered.Png.AsSpan(16, 4)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(rendered.Png.AsSpan(20, 4)));
    }
}
