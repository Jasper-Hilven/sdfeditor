using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode.InteractiveCommands;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

// Whether pressing the key in the current mode does something
public class SDFPartialCommandNodeProgramEvaluator
{
    public static bool IsActionAvailable(SDFPartialCommandNodeProgram program, InputKey Key)
    {
        if (!SDFPartialKeyBindings.Bindings.TryGetValue((program.state.Mode, Key), out var action))
            return false;

        // Starting to pick a node type is only worth it when the picked node can be added or replaced
        if (action == SDFPartialActionEnum.AddNode &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveAddNodeNextTo(SDFNodeType.Box)))
            return false;
        if (action == SDFPartialActionEnum.ReplaceNode &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveReplaceNode(SDFNodeType.Box)))
            return false;

        return SDFPartialCommandNodeProgramOperator.TargetMode(action) is { } mode
            ? mode != program.state.Mode
            : SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram,
                SDFPartialCommandNodeProgramOperator.InteractiveOperation(action));
    }
}
