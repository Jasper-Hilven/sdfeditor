using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;

namespace SDFProject.SDFInteractiveNode.InteractiveCommands;

// Whether an operation does something on the program, so applying it neither fails nor leaves everything as it was
public class SDFInteractiveNodeProgramEvaluator
{
    public static bool IsActionAvailable(SdfInteractiveNodeProgram program,
        ISdfInteractiveNodeProgramOperation operation)
    {
        switch (operation)
        {
            case IncreaseStrZoomLevel:
                return program.StrZoomLevel < SdfInteractiveNodeProgramOperator.MaxStrZoomLevel;
            case DecreaseStrZoomLevel:
            case ZoomFullyOutStrZoomLevel:
                return program.StrZoomLevel > 0;
            case InteractiveCleanEmptyNodesFromUnionsAndIntersections:
                return true;
        }

        // Every other operation works on the selected node
        var pointers = program.Program.pointers;
        if (program.SelectedNodeId is not { } id || !pointers.ContainsPointer(id))
            return false;

        var tables = new PointerTables(pointers);
        var isSdfNode = tables.PointerTo(id) is SDFEvaluablePointerable;

        return operation switch
        {
            SelectNext => NodeNavigation.Next(pointers, id) != id,
            SelectPrevious => NodeNavigation.Previous(pointers, id) != id,
            SelectNextSibling => NodeNavigation.NextSiblingOrParent(pointers, id) != id,
            SelectPreviousSibling => NodeNavigation.PreviousSiblingOrParent(pointers, id) != id,
            SelectParent => NodeNavigation.Parent(pointers, id) != id,
            SelectFirstChild => NodeNavigation.FirstChild(pointers, id) != id,
            InteractiveAddNodeNextTo or InteractiveReplaceNode => isSdfNode,
            InteractiveEmptyNode => isSdfNode && tables.Get(id) is not Empty,
            InteractiveInlineVariable => SdfInteractiveNodeProgramOperator.FindVariableContaining(program.Program, id) is not null,
            InteractiveExtractVariable => ApplyExtractVariable.CanExtract(program.Program, id),
            ToggleSmoothness => ApplyToggleNodeSmoothness.CanToggle(tables.Get(id)),
            InteractiveAdjustScaleTranslateRotate
                or ScaleUpAtCurrentZoomLevel or ScaleDownAtCurrentZoomLevel
                or TranslateXUpAtCurrentZoomLevel or TranslateXDownAtCurrentZoomLevel
                or TranslateYUpAtCurrentZoomLevel or TranslateYDownAtCurrentZoomLevel
                or TranslateZUpAtCurrentZoomLevel or TranslateZDownAtCurrentZoomLevel
                or RotateXUpAtCurrentZoomLevel or RotateXDownAtCurrentZoomLevel
                or RotateYUpAtCurrentZoomLevel or RotateYDownAtCurrentZoomLevel
                or RotateZUpAtCurrentZoomLevel or RotateZDownAtCurrentZoomLevel
                => ApplyAdjustScaleTranslateRotate.CanAdjust(program.Program, id),
            _ => throw new NotSupportedException($"Unknown {nameof(ISdfInteractiveNodeProgramOperation)}: {operation.GetType().Name}"),
        };
    }
}
