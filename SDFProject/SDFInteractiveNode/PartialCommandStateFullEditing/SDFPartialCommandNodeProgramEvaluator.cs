using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode.InteractiveCommands;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public class SDFPartialCommandNodeProgramEvaluator
{
    public static bool IsActionAvailable(SDFPartialCommandNodeProgram program, InputKey Key) =>
        SDFPartialKeyBindings.Bindings.TryGetValue((program.state.Mode, Key), out var action) && IsActionAvailable(program, action);

    public static bool IsActionAvailable(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        if (SDFPartialCommandNodeProgramOperator.TakesText(program.state.Mode))
            return action is InsertText or DeleteLastCharacter or ConfirmText or CancelText;
        if (action is InsertText or DeleteLastCharacter or ConfirmText or CancelText)
            return false;
        if (action is Undo or Redo or ChooseRedoBranch or CancelRedoChoice or GoToHistoryEntry)
            return true;

        if (action is AddNode &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveAddNodeNextTo(SDFNodeType.Box)))
            return false;
        if (action is ReplaceNode &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveReplaceNode(SDFNodeType.Box)))
            return false;
        if (action is ExtractVariable &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveExtractVariable("name")))
            return false;

        switch (action)
        {
            case EditNode:
                return SDFPartialCommandNodeProgramOperator.Parameters(program).Any(p => p.IsConstant);
            case EditParameter1 or EditParameter2
                or EditParameter3 or EditParameter4:
                return SDFPartialCommandNodeProgramOperator.ParameterToEdit(program, action) is { IsConstant: true };
            case StopEditingParameter:
                return true;
            case TypeParameterValue:
                return SDFPartialCommandNodeProgramOperator.EditedParameter(program) is { IsConstant: true };
        }

        return SDFPartialCommandNodeProgramOperator.TargetMode(action) is { } mode
            ? mode != program.state.Mode
            : SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram,
                SDFPartialCommandNodeProgramOperator.InteractiveOperation(action, program));
    }
}
