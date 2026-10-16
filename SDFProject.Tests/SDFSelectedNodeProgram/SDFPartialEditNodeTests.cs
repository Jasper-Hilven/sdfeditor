using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.Tests;

public class SDFPartialEditNodeTests
{
    private static SDFPartialCommandNodeProgram Initial() => new(
        SdfInteractiveNodeProgramConstants.GetInitialProgram(),
        new SDFPartialCommandNodeProgramState(Home));

    private static InputKey KeyFor(SDFPartialCommandNodeProgramMode mode, ISDFPartialAction action) =>
        SDFPartialKeyBindings.Bindings.Single(b => b.Key.Mode == mode && b.Value.Equals(action)).Key.Key;

    private static SDFPartialCommandNodeProgram Press(SDFPartialCommandNodeProgram program, ISDFPartialAction action) =>
        SDFPartialCommandNodeProgramOperator.Operate(program, KeyFor(program.state.Mode, action));

    private static bool IsAvailable(SDFPartialCommandNodeProgram program, ISDFPartialAction action) =>
        SDFPartialCommandNodeProgramEvaluator.IsActionAvailable(program, KeyFor(program.state.Mode, action));

    private static SDFPartialCommandNodeProgram WithSelected(ISDFPartialAction addNodeType) =>
        Press(Press(Initial(), new AddNode()), addNodeType);

    private static float[] Value(SDFPartialCommandNodeProgram program, string parameter)
    {
        var interactive = program.interactiveProgram;
        var node = new PointerTables(interactive.Program.pointers).Get(interactive.SelectedNodeId!);
        return NodeParameters.Find(node, parameter)!.Components();
    }

    [Fact]
    public void SingleParameter_IsEditedWithoutPickingIt()
    {
        var editing = Press(Initial(), new EditNode());

        Assert.Equal(new SDFPartialCommandNodeProgramState(EditParameter, "size"), editing.state);
        Assert.Equal(Home, Press(editing, new StopEditingParameter()).state.Mode);
    }

    [Fact]
    public void VectorComponents_StepAtTheZoomLevel()
    {
        var editing = Press(Initial(), new EditNode());
        var step = SdfInteractiveNodeProgramOperator.TranslateZoomScaleToTranslation(0);

        var result = Press(Press(editing, new ParameterXUp()), new ParameterZDown());

        Assert.Equal([0.5f + step, 0.5f, 0.5f - step], Value(result, "size"));
        Assert.Equal(EditParameter, result.state.Mode);
    }

    [Fact]
    public void SeveralParameters_ArePickedByPosition()
    {
        var picking = Press(WithSelected(new AddCappedCylinder()), new EditNode());
        Assert.Equal(SDFPartialCommandNodeProgramMode.EditNode, picking.state.Mode);
        Assert.False(IsAvailable(picking, new EditParameter3()));

        var editing = Press(picking, new EditParameter2());
        Assert.Equal("height", editing.state.EditedParameter);
        Assert.False(IsAvailable(editing, new ParameterYUp()));

        var result = Press(editing, new ParameterXUp());
        Assert.Equal([1f], Value(result, "height"));
        Assert.Equal(SDFPartialCommandNodeProgramMode.EditNode, Press(result, new StopEditingParameter()).state.Mode);
    }

    [Fact]
    public void Angle_StepsInDegrees()
    {
        var editing = Press(Press(WithSelected(new AddCone()), new EditNode()), new EditParameter1());

        var result = Press(editing, new ParameterXUp());

        Assert.Equal(90f, Assert.Single(Value(result, "sinCos")), 0.001f);
    }

    [Fact]
    public void TypingAValue_SetsTheParameterOnConfirm()
    {
        var editing = Press(Initial(), new EditNode());
        Assert.True(IsAvailable(editing, new TypeParameterValue()));

        var typing = Press(editing, new TypeParameterValue());
        Assert.Equal(SDFPartialCommandNodeProgramMode.TypeParameterValue, typing.state.Mode);
        typing = SDFPartialCommandNodeProgramOperator.Operate(typing, new InsertText("1, 2"));
        typing = SDFPartialCommandNodeProgramOperator.Operate(typing, new InsertText(" 3"));
        Assert.Equal("1, 2 3", typing.state.Text);

        var result = Press(typing, new ConfirmText());

        Assert.Equal([1f, 2f, 3f], Value(result, "size"));
        Assert.Equal(SDFPartialCommandNodeProgramMode.EditParameter, result.state.Mode);
        Assert.Equal("", result.state.Text);
    }

    [Fact]
    public void TypingAValue_WithTheWrongNumberOfComponents_ThrowsAndKeepsTheText()
    {
        var typing = SDFPartialCommandNodeProgramOperator.Operate(
            Press(Press(Initial(), new EditNode()), new TypeParameterValue()), new InsertText("1"));

        Assert.Throws<FormatException>(() => Press(typing, new ConfirmText()));
        Assert.Equal("1", typing.state.Text);
    }

    [Fact]
    public void TypingAValue_CancelOrEmptyConfirm_KeepsTheValue()
    {
        var typing = Press(Press(Initial(), new EditNode()), new TypeParameterValue());

        var cancelled = Press(SDFPartialCommandNodeProgramOperator.Operate(typing, new InsertText("9")), new CancelText());
        var confirmedEmpty = Press(typing, new ConfirmText());

        Assert.Equal([0.5f, 0.5f, 0.5f], Value(cancelled, "size"));
        Assert.Equal([0.5f, 0.5f, 0.5f], Value(confirmedEmpty, "size"));
        Assert.Equal(SDFPartialCommandNodeProgramMode.EditParameter, cancelled.state.Mode);
    }

    [Fact]
    public void TextActions_OutsideATextMode_DoNothing()
    {
        var initial = Initial();

        Assert.Same(initial, SDFPartialCommandNodeProgramOperator.Operate(initial, new InsertText("x")));
        Assert.Same(initial, SDFPartialCommandNodeProgramOperator.Operate(initial, new ConfirmText()));
    }

    [Fact]
    public void SetNodeParameter_SetsTheExactValue()
    {
        var program = Initial().interactiveProgram;

        var result = SdfInteractiveNodeProgramOperator.ApplyOperation(program, new InteractiveSetNodeParameter("size", [1, 2, 3]));

        var node = (Box)new PointerTables(result.Program.pointers).Get(result.SelectedNodeId!);
        Assert.Equal(new ConstValueV3(new V3(1, 2, 3)), node.size);
    }
}
