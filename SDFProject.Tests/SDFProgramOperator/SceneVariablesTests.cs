using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.Tests.SDFProgramTestHelpers;
using ExtractVariable = SDFProject.SDFProgramOperator.ExtractVariable;
using SelectNext = SDFProject.SDFInteractiveNode.SelectNext;

namespace SDFProject.Tests;

public class SceneVariablesTests
{
    private static SDFPartialCommandNodeProgram Home(SDFProgram program, string selectedId) =>
        new(new SdfInteractiveNodeProgram(program, selectedId, 0), new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));

    private static SDFPartialCommandNodeProgram Press(SDFPartialCommandNodeProgram program, string letter) =>
        SDFPartialCommandNodeProgramOperator.Operate(program, FingerKeyLayouts.AzertyBeFingerKeyToIndex[letter]);

    private static SDFPartialCommandNodeProgram Act(SDFPartialCommandNodeProgram program, ISDFPartialAction action) =>
        SDFPartialCommandNodeProgramOperator.Operate(program, action);

    private static SDFPartialCommandNodeProgram Extract(SDFPartialCommandNodeProgram program) =>
        SDFPartialCommandNodeProgramOperator.Operate(Press(program, "u"), InputKey.Enter);

    private static SDFProgram Extracted(out SDFProgram original)
    {
        original = Program(new Union(Sphere(1), new Translate(Sphere(2), new ConstValueV3(new V3(2, 0, 0)))));
        var translateId = original.pointers.SDFEvaluablePointers.Single(e => e.Value is Translate).Key;
        return SdfProgramOperator.ApplyOperation(original, new ExtractVariable(translateId, "moved"));
    }

    [Fact]
    public void UKeyAndEnter_ExtractWithTheSuggestedName_AndKeepTheSelection()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var typing = Press(Home(program, SphereId(program, 2)), "u");
        Assert.Equal(SDFPartialCommandNodeProgramMode.ExtractVariable, typing.state.Mode);
        Assert.Equal("var1", SDFPartialCommandNodeProgramOperator.SuggestedVariableName(typing));
        var result = SDFPartialCommandNodeProgramOperator.Operate(typing, InputKey.Enter);

        Assert.Equal(SDFPartialCommandNodeProgramMode.Home, result.state.Mode);
        var variable = Assert.Single(result.interactiveProgram.Program.sceneVariables);
        Assert.Equal("var1", variable.name);
        Assert.Equal(SphereId(program, 2), result.interactiveProgram.SelectedNodeId);
        Assert.Equal(FingerKeyLayouts.QwertyFingerKeyToIndex["u"], FingerKeyLayouts.AzertyBeFingerKeyToIndex["u"]);
    }

    [Fact]
    public void TypedName_IsUsedForTheVariable()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));
        var typing = Press(Home(program, SphereId(program, 2)), "u");

        typing = Act(Act(Act(typing, new InsertText("my")), new InsertText("boxx")), new DeleteLastCharacter());
        var result = Act(typing, new ConfirmText());

        Assert.Equal("mybox", Assert.Single(result.interactiveProgram.Program.sceneVariables).name);
    }

    [Fact]
    public void CancellingTheName_ExtractsNothing()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = Act(Act(Press(Home(program, SphereId(program, 2)), "u"), new InsertText("v")), new CancelText());

        Assert.Empty(result.interactiveProgram.Program.sceneVariables);
        Assert.Equal(SDFPartialCommandNodeProgramMode.Home, result.state.Mode);
    }

    [Fact]
    public void YKey_InlinesTheVariableHoldingTheSelection()
    {
        var program = Program(new Union(Sphere(1), Sphere(2)));

        var result = Press(Extract(Home(program, SphereId(program, 2))), "y");

        Assert.Empty(result.interactiveProgram.Program.sceneVariables);
        Assert.Equal("Union(Sphere(1), Sphere(2))", DescribeScene(result.interactiveProgram.Program));
        Assert.Equal(SphereId(program, 2), result.interactiveProgram.SelectedNodeId);
        Assert.Equal(FingerKeyLayouts.QwertyFingerKeyToIndex["y"], FingerKeyLayouts.AzertyBeFingerKeyToIndex["y"]);
    }

    [Fact]
    public void SceneRoot_CanBeExtractedAndInlinedAgain()
    {
        var initial = Home(SdfInteractiveNodeProgramConstants.GetInitialProgram().Program, SdfInteractiveNodeProgramConstants.GetInitialProgram().SelectedNodeId!);

        var extracted = Extract(initial);
        var inlined = Press(extracted, "y");

        Assert.Single(extracted.interactiveProgram.Program.sceneVariables);
        AssertPointersConsistent(extracted.interactiveProgram.Program);
        Assert.Empty(inlined.interactiveProgram.Program.sceneVariables);
        Assert.Equal("Box(ConstValueV3)", DescribeScene(inlined.interactiveProgram.Program));
        AssertPointersConsistent(inlined.interactiveProgram.Program);
    }

    [Fact]
    public void Navigation_GoesThroughTheVariablesBeforeTheScene()
    {
        var program = Extracted(out _);
        var translateId = PointerTables.PointerId(program.sceneVariables[0].value)!;
        var sceneId = PointerTables.PointerId(program.scene)!;

        Assert.Equal(SphereId(program, 2), NodeNavigation.Next(program, translateId));
        Assert.Equal(sceneId, NodeNavigation.Next(program, SphereId(program, 2)));
        Assert.Equal(SphereId(program, 2), NodeNavigation.Previous(program, sceneId));
        Assert.Equal(translateId, NodeNavigation.Previous(program, translateId));
        var referenceId = program.pointers.SDFEvaluablePointers.Single(e => e.Value is VariableReferenceSDF).Key;
        Assert.Equal(referenceId, NodeNavigation.Next(program, SphereId(program, 1)));
        Assert.Equal(referenceId, NodeNavigation.Next(program, referenceId));
    }

    [Fact]
    public void Calculation_IsUnchangedByExtracting()
    {
        var program = Extracted(out var original);
        var calculator = new SdfCalculatorCpu();

        foreach (var point in new[] { new V3(0, 0, 0), new V3(2, 0, 0), new V3(4, 1, 0) })
            Assert.Equal(calculator.Calculate(point, original), calculator.Calculate(point, program), 5);
    }

    [Fact]
    public void ValueVariablesOfTheScene_AreDeclaredInTheSceneFunction()
    {
        var program = SDFProgramNormalizePointers.Pointerify(new SDFProgram([], NoPointers,
            [new VariableDeclaration("r", "r", new ConstValueFloat(2))], new Sphere(new VariableReferenceFloat("r"))));

        Assert.Contains("float v0_r = 2.0;", new Webgl2CodeGenerator().GenerateShaderCode(program));
        Assert.Equal(-2, new SdfCalculatorCpu().Calculate(new V3(0, 0, 0), program), 5);
    }

    [Fact]
    public void ProgramView_ShowsTheVariablesAboveTheScene()
    {
        var program = Extracted(out _);
        var translateId = PointerTables.PointerId(program.sceneVariables[0].value)!;

        Assert.Equal(
        [
            "moved =",
            "    Translate translation=(2, 0, 0) <---",
            "        Sphere radius=2",
            "Union",
            "    Sphere radius=1",
            "    $moved",
        ], new YamlRender().GetRender(new SdfInteractiveNodeProgram(program, translateId, 0)));
    }

    private static SDFPartialCommandNodeProgram BoxVariableNextToEmpty()
    {
        var initial = SdfInteractiveNodeProgramConstants.GetInitialProgram();
        var extracted = SdfInteractiveNodeProgramOperator.ApplyOperation(initial, new InteractiveExtractVariable("mybox"));
        var referenceId = PointerTables.PointerId(extracted.Program.scene)!;
        var withSphere = SdfInteractiveNodeProgramOperator.ApplyOperation(
            extracted with { SelectedNodeId = referenceId }, new InteractiveAddNodeNextTo(SDFNodeType.Sphere));
        var withEmpty = SdfInteractiveNodeProgramOperator.ApplyOperation(withSphere, new InteractiveEmptyNode());
        return new(withEmpty with { SelectedNodeId = referenceId },
            new SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode.Home));
    }

    [Fact]
    public void EmptyNextToAReference_CanBeSelected()
    {
        var program = BoxVariableNextToEmpty();
        Assert.Equal(
        [
            "mybox =",
            "    Box size=(0.5, 0.5, 0.5)",
            "Union",
            "    $mybox <---",
            "    Empty",
        ], new YamlRender().GetRender(program.interactiveProgram));

        var next = SdfInteractiveNodeProgramOperator.ApplyOperation(program.interactiveProgram, new SelectNext());

        Assert.Equal("    Empty <---", new YamlRender().GetRender(next).Last());
    }

    [Fact]
    public void YKey_OnAReference_InlinesItsVariableAndSelectsTheValue()
    {
        var program = BoxVariableNextToEmpty();
        var boxId = PointerTables.PointerId(program.interactiveProgram.Program.sceneVariables[0].value);

        var result = Press(program, "y");

        Assert.Empty(result.interactiveProgram.Program.sceneVariables);
        Assert.Equal("Union(Box(ConstValueV3), Empty)", DescribeScene(result.interactiveProgram.Program));
        Assert.Equal(boxId, result.interactiveProgram.SelectedNodeId);
        AssertPointersConsistent(result.interactiveProgram.Program);
    }
}
