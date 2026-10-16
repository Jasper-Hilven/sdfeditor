using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator.Helpers;
using Distort = SDFProject.ProgramDefinition.Distort;
using DissolveNode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.DissolveNode;

namespace SDFProject.Tests;

public class DistortTests
{
    private static SDFPartialActionHistory Start() => SDFPartialActionHistory.Start(new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home)));

    private static Evaluable Selected(SDFPartialActionHistory history)
    {
        var interactive = history.Current.interactiveProgram;
        return new PointerTables(interactive.Program.pointers).Get(interactive.SelectedNodeId!);
    }

    private static string[] View(SDFPartialActionHistory history) => new YamlRender().GetRender(history.Current.interactiveProgram);

    [Fact]
    public void B_InHome_DistortsTheSelectedNode()
    {
        var key = FingerKeyLayouts.AzertyBeFingerKeyToIndex["b"];

        Assert.Equal(key, FingerKeyLayouts.QwertyFingerKeyToIndex["b"]);
        Assert.Equal(new DistortNode(), SDFPartialKeyBindings.Bindings[(SDFPartialCommandNodeProgramMode.Home, key)]);
    }

    [Fact]
    public void DistortNode_WrapsTheSelectionAndSelectsTheDistortion()
    {
        var history = Start().Do(new DistortNode());

        var distort = Assert.IsType<Distort>(Selected(history));
        Assert.Equal(new ConstValueFloat(0.03f), distort.amount);
        Assert.Equal(new ConstValueFloat(6f), distort.frequency);
        Assert.Equal(SDFPartialCommandNodeProgramMode.Home, history.Current.state.Mode);
        Assert.Contains(View(history), line => line.Contains("Box"));
    }

    [Fact]
    public void Distortion_HasEditableAmountAndFrequency()
    {
        var history = Start().Do(new DistortNode()).Do(new EditNode()).Do(new EditParameter1())
            .Do(new TypeParameterValue()).Do(new InsertText("0.08")).Do(new ConfirmText());

        Assert.Equal(new ConstValueFloat(0.08f), Assert.IsType<Distort>(Selected(history)).amount);
        Assert.Equal(["amount", "frequency"], SDFPartialCommandNodeProgramOperator.Parameters(history.Current).Select(p => p.Name));
    }

    [Fact]
    public void Dissolve_RemovesTheDistortionAgain()
    {
        var plain = Start();

        var dissolved = plain.Do(new DistortNode()).Do(new DissolveNode());

        Assert.Equal(View(plain), View(dissolved));
    }

    [Fact]
    public void Distortion_MovesTheSurfaceByAtMostTheAmount()
    {
        var sphere = new Sphere(new ConstValueFloat(1));
        var cpu = new SdfCalculatorCpu();
        var plain = Program(sphere);
        var distorted = Program(new Distort(sphere, new ConstValueFloat(0.05f), new ConstValueFloat(6)));

        var differences = Enumerable.Range(0, 200)
            .Select(i => new V3(MathF.Sin(i * 0.37f) * 1.2f, MathF.Cos(i * 0.53f) * 1.1f, MathF.Sin(i * 0.71f + 1)))
            .Select(p => cpu.Calculate(p, distorted) - cpu.Calculate(p, plain))
            .ToArray();

        Assert.All(differences, d => Assert.InRange(d, -0.05f, 0.05f));
        Assert.True(differences.Max() - differences.Min() > 0.02f);
    }

    [Fact]
    public void Shader_IncludesTheNoiseAndKeepsTheColour()
    {
        var program = Program(new Distort(new Color(new Sphere(new ConstValueFloat(1)), new ConstValueV3(new V3(1, 0, 0))),
            new ConstValueFloat(0.03f), new ConstValueFloat(6)));

        var shader = new Webgl2CodeGenerator().GenerateShaderCode(program);

        Assert.Contains("float sdfHash(vec3 c)", shader);
        Assert.Contains("float sdfValueNoise(vec3 p)", shader);
        Assert.Contains("float opDistort(vec3 p, float amount, float frequency)", shader);
        Assert.Contains("vec3(1.0, 0.0, 0.0)", shader.Split(Webgl2CodeGenerator.MaterialFunctionName)[^1]);
    }

    private static SDFProgram Program(SDFEvaluable scene) =>
        SDFProgramNormalizePointers.Pointerify(new SDFProgram([], new SDFPointers(new(), new(), new(), new(), 0), [], scene));
}
