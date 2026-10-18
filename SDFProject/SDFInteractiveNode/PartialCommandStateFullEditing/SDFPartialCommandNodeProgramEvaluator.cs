using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode.InteractiveCommands;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public class SDFPartialCommandNodeProgramEvaluator
{
    public static bool IsActionAvailable(SDFPartialCommandNodeProgram program, InputKey Key) =>
        SDFPartialKeyBindings.ActionFor(program, Key) is { } action && IsActionAvailable(program, action);

    public static bool IsActionAvailable(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        if (SDFPartialCommandNodeProgramOperator.TakesText(program.state.Mode))
            return action is InsertText or DeleteLastCharacter or ConfirmText or CancelText;
        if (action is InsertText or DeleteLastCharacter or ConfirmText or CancelText)
            return false;
        if (action is Undo or Redo or ChooseRedoBranch or CancelRedoChoice or GoToHistoryEntry)
            return true;
        if (SDFPartialCommandNodeProgramOperator.LockingPart(program, action) is not null)
            return false;

        if (action is AddNode &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveAddNodeNextTo(SDFNodeType.Box)))
            return false;
        if (action is ReplaceNode &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveReplaceNode(SDFNodeType.Box)))
            return false;
        if (action is ExtractVariable &&
            !SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveExtractVariable("name")))
            return false;

        if (MaterialActionAvailable(program, action) is { } material)
            return material;

        switch (action)
        {
            case EditColor:
                return SDFPartialCommandNodeProgramOperator.ColorParameter(program) is not null;
            case RemoveColor:
                return SDFPartialCommandNodeProgramOperator.SelectedIsColor(program);
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

        return SDFPartialCommandNodeProgramOperator.TargetMode(action) is { } targetMode
            ? targetMode != program.state.Mode
            : SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram,
                SDFPartialCommandNodeProgramOperator.InteractiveOperation(action, program));
    }

    /// <summary>Whether a material action can be done, or null when the action has nothing to do with materials.</summary>
    private static bool? MaterialActionAvailable(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        var editingValue = program.state.Mode == SDFPartialCommandNodeProgramMode.EditMaterialParameter;
        var edited = SDFPartialCommandNodeProgramOperator.EditedMaterial(program);
        var canHaveMaterial = SDFInteractiveNodeProgramEvaluator.IsActionAvailable(program.interactiveProgram, new InteractiveSetMaterial("material"));
        var selectedMaterial = SDFPartialCommandNodeProgramOperator.SelectedMaterialName(program);
        return action switch
        {
            ShowMaterials => program.state.Mode != SDFPartialCommandNodeProgramMode.Materials && program.Materials.Names.Any() && canHaveMaterial,
            CancelMaterials or StopEditingMaterialParameter => true,
            AssignMaterial assign => program.Materials.Contains(assign.name) && canHaveMaterial,
            EditMaterial or PickMaterialParameter => selectedMaterial is not null && program.Materials.Contains(selectedMaterial)
                                                     && (action is not PickMaterialParameter pick || pick.index >= 0 && pick.index < MaterialDefinition.Parameters.Count),
            TypeParameterValue or ParameterXDown or ParameterXUp when editingValue => edited is not null,
            ParameterYDown or ParameterYUp when editingValue => edited is { Parameter.ComponentCount: >= 2 },
            ParameterZDown or ParameterZUp when editingValue => edited is { Parameter.ComponentCount: 3 },
            _ => null,
        };
    }
}
