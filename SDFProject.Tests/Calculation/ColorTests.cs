using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class ColorTests
{
    private static readonly Webgl2CodeGenerator Generator = new();

    private static SDFEvaluable Painted(float r, float g, float b, SDFEvaluable primitive) =>
        new Color(primitive, new ConstValueV3(new V3(r, g, b)));

    private static string Material(SDFProgram program)
    {
        var code = Generator.GenerateShaderCode(program);
        return code[code.IndexOf("SurfaceMaterial sdf_material(vec3 p)", StringComparison.Ordinal)..];
    }

    private static string ColorNodeId(SDFProgram program) => program.pointers.SDFEvaluablePointers.Single(e => e.Value is Color).Key;

    [Fact]
    public void ColorNode_DoesNotChangeTheDistance()
    {
        var plain = Program(new Union(Sphere(1), Sphere(2)));
        var painted = Program(new Union(Painted(0.9f, 0.2f, 0.1f, Sphere(1)), Sphere(2)));

        Assert.Equal(Generator.generateSdfCode(plain), Generator.generateSdfCode(painted));
        Assert.Equal(new SdfCalculatorCpu().Calculate(new V3(0.3f, 1.2f, 0), plain),
            new SdfCalculatorCpu().Calculate(new V3(0.3f, 1.2f, 0), painted));
    }

    [Fact]
    public void PaintedShape_ReturnsItsColor()
    {
        var material = Material(Program(Painted(0.9f, 0.2f, 0.1f, Sphere(1))));

        Assert.Contains("SurfaceMaterial m0 = colorMaterial(vec3(0.9, 0.2, 0.1));", material);
        Assert.Contains("return m0;", material);
    }

    [Fact]
    public void Union_PicksTheColorOfTheNearerShape_AndUnpaintedShapesKeepTheDefault()
    {
        var material = Material(Program(new Union(Painted(0.9f, 0.2f, 0.1f, Sphere(1)), Sphere(2))));

        Assert.Contains($", m0, {Webgl2CodeGenerator.DefaultMaterial})", material);
    }

    [Fact]
    public void InnermostColor_Wins()
    {
        var material = Material(Program(Painted(0, 0, 1, Painted(1, 0, 0, Sphere(1)))));

        var returned = material.Split('\n').Single(l => l.Contains("return")).Trim();
        Assert.Equal("return m1;", returned);
        Assert.Contains("SurfaceMaterial m1 = colorMaterial(vec3(1.0, 0.0, 0.0));", material);
    }

    [Fact]
    public void Subtraction_KeepsTheColorOfTheBase()
    {
        var material = Material(Program(new Subtraction(Painted(1, 0, 0, Sphere(1)), Painted(0, 0, 1, Sphere(0.5f)))));

        Assert.DoesNotContain("vec3(0.0, 0.0, 1.0)", material);
        Assert.Contains("return m0;", material);
    }

    [Fact]
    public void SmoothUnion_BlendsTheColors()
    {
        var material = Material(Program(new SmoothUnion(new ConstValueFloat(0.25f), Painted(1, 0, 0, Sphere(1)), Painted(0, 0, 1, Sphere(1)))));

        Assert.Contains("mixMaterial(", material);
    }

    [Fact]
    public void Transforms_MoveTheColoredShapeToo()
    {
        var material = Material(Program(new Translate(Painted(1, 0, 0, Sphere(1)), new ConstValueV3(new V3(2, 0, 0)))));

        Assert.Contains("vec3 p0 = p - vec3(2.0, 0.0, 0.0);", material);
    }

    [Fact]
    public void ColorsInsideAVariable_AreFound()
    {
        var program = SDFProgramNormalizePointers.Pointerify(new SDFProgram([], NoPointers,
            [new VariableDeclaration("part", "part", Painted(1, 0, 0, Sphere(1)))],
            new Union(new VariableReferenceSDF("part"), Sphere(2))));

        Assert.Contains("vec3(1.0, 0.0, 0.0)", Material(program));
    }

    [Fact]
    public void Painting_WrapsTheNodeAndSelectsTheColorNode()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = SdfInteractiveNodeProgramOperator.ApplyOperation(
            new SdfInteractiveNodeProgram(program, SphereId(program, 1), 0), new InteractivePaint(new V3(1, 0, 0)));

        Assert.Equal("Union(Color(Sphere(1), ConstValueV3), Sphere(2))", DescribeScene(result.Program));
        Assert.Equal(ColorNodeId(result.Program), result.SelectedNodeId);
        AssertPointersConsistent(result.Program);
    }

    [Fact]
    public void PaintingAgain_RecolorsInsteadOfNesting_AlsoFromTheShapeInside()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));
        var red = SdfInteractiveNodeProgramOperator.ApplyOperation(
            new SdfInteractiveNodeProgram(program, SphereId(program, 1), 0), new InteractivePaint(new V3(1, 0, 0)));

        var blue = SdfInteractiveNodeProgramOperator.ApplyOperation(red, new InteractivePaint(new V3(0, 0, 1)));
        var green = SdfInteractiveNodeProgramOperator.ApplyOperation(blue with { SelectedNodeId = SphereId(blue.Program, 1) }, new InteractivePaint(new V3(0, 1, 0)));

        Assert.Equal("Union(Color(Sphere(1), ConstValueV3), Sphere(2))", DescribeScene(green.Program));
        Assert.Equal(new V3(0, 1, 0), ((ConstValueV3)((Color)green.Program.pointers.SDFEvaluablePointers[ColorNodeId(green.Program)]).color).v3);
        Assert.Equal(ColorNodeId(red.Program), ColorNodeId(green.Program));
        Assert.Equal(ColorNodeId(green.Program), green.SelectedNodeId);
    }

    [Fact]
    public void EmptyNode_CannotBePainted()
    {
        var program = Program(new Empty());

        Assert.False(ApplyPaintNode.CanPaint(program, PointerTables.PointerId(program.scene)!));
    }

    private static SDFPartialActionHistory Start() => SDFPartialActionHistory.Start(new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home)));

    private static SDFPartialActionHistory Press(SDFPartialActionHistory history, string letter) =>
        history.Do(history.ActionFor(FingerKeyLayouts.AzertyBeFingerKeyToIndex[letter])!);

    private static InputKey KeyOf(SDFPartialCommandNodeProgramMode mode, ISDFPartialAction action) =>
        SDFPartialKeyBindings.Bindings.Single(b => b.Key.Mode == mode && b.Value.Equals(action)).Key.Key;

    [Fact]
    public void CKey_OpensMaterialMode_WhereEveryPaletteColorHasAKey()
    {
        var material = Press(Start(), "c");

        Assert.Equal(SDFPartialCommandNodeProgramMode.Material, material.Current.state.Mode);
        Assert.Equal(FingerKeyLayouts.QwertyFingerKeyToIndex["c"], FingerKeyLayouts.AzertyBeFingerKeyToIndex["c"]);
        foreach (var paint in SDFPartialKeyBindings.Palette)
            KeyOf(SDFPartialCommandNodeProgramMode.Material, paint);
    }

    [Fact]
    public void InMaterialMode_PaintEditAndRemoveColor()
    {
        var material = Press(Start(), "c");
        Assert.False(material.IsAvailable(KeyOf(SDFPartialCommandNodeProgramMode.Material, new RemoveColor())));
        Assert.False(material.IsAvailable(KeyOf(SDFPartialCommandNodeProgramMode.Material, new EditColor())));

        var red = material.Do(material.ActionFor(KeyOf(SDFPartialCommandNodeProgramMode.Material, SDFPartialKeyBindings.Palette[0]))!);
        Assert.Equal(SDFPartialCommandNodeProgramMode.Material, red.Current.state.Mode);
        Assert.Equal("Color(Box(ConstValueV3), ConstValueV3)", DescribeScene(red.Current.interactiveProgram.Program));
        Assert.True(red.IsAvailable(KeyOf(SDFPartialCommandNodeProgramMode.Material, new RemoveColor())));

        var editing = Press(red, "v");
        Assert.Equal(SDFPartialCommandNodeProgramMode.EditParameter, editing.Current.state.Mode);
        Assert.Equal("color", editing.Current.state.EditedParameter);

        var removed = Press(red, "t");
        Assert.Equal("Box(ConstValueV3)", DescribeScene(removed.Current.interactiveProgram.Program));
    }

    [Fact]
    public void Paints_ReplayAndSurviveTheFile()
    {
        var history = Press(Start(), "c")
            .Do(SDFPartialKeyBindings.Palette[3])
            .Do(SDFPartialKeyBindings.Palette[6]);

        var loaded = SDFPartialHistorySerializer.Deserialize(SDFPartialHistorySerializer.Serialize(history), Start().Initial);

        Assert.Equal(Material(history.Current.interactiveProgram.Program), Material(history.Replay().interactiveProgram.Program));
        Assert.Equal(Material(history.Current.interactiveProgram.Program), Material(loaded.Current.interactiveProgram.Program));
        Assert.Contains("vec3(0.25, 0.45, 0.85)", Material(loaded.Current.interactiveProgram.Program));
    }
}
