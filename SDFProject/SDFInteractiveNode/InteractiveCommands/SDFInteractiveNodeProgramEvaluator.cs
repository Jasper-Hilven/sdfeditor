using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;

namespace SDFProject.SDFInteractiveNode.InteractiveCommands;

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

        var pointers = program.Program.pointers;
        if (program.SelectedNodeId is not { } id || !pointers.ContainsPointer(id))
            return false;

        var tables = new PointerTables(pointers);
        var isSdfNode = tables.PointerTo(id) is SDFEvaluablePointerable;

        return operation switch
        {
            SelectNext => NodeNavigation.Next(program.Program, id) != id,
            SelectPrevious => NodeNavigation.Previous(program.Program, id) != id,
            SelectNextSibling => NodeNavigation.NextSiblingOrParent(pointers, id) != id,
            SelectPreviousSibling => NodeNavigation.PreviousSiblingOrParent(pointers, id) != id,
            SelectParent => NodeNavigation.Parent(pointers, id) != id,
            SelectFirstChild => NodeNavigation.FirstChild(pointers, id) != id,
            InteractiveAddNodeNextTo or InteractiveReplaceNode => isSdfNode,
            InteractiveEmptyNode => isSdfNode && tables.Get(id) is not Empty,
            InteractiveInlineVariable => SdfInteractiveNodeProgramOperator.FindVariableToInline(program.Program, id) is not null,
            InteractiveExtractVariable => ApplyExtractVariable.CanExtract(program.Program, id),
            ToggleSmoothness => ApplyToggleNodeSmoothness.CanToggle(tables.Get(id)),
            InteractiveWrapInSubtraction => ApplyWrapInSubtraction.CanWrap(program.Program, id),
            InteractiveWrapInIntersection => ApplyWrapInIntersection.CanWrap(program.Program, id),
            InteractiveWrapInDistortion => ApplyWrapInDistortion.CanWrap(program.Program, id),
            InteractivePaint => ApplyPaintNode.CanPaint(program.Program, id),
            InteractiveSetMaterial => ApplyPaintNode.CanPaint(program.Program, id),
            InteractiveDissolveNode => ApplyDissolveNode.CanDissolve(program.Program, id),
            InteractiveSetNodeParameter o => ApplySetNodeParameter.CanSet(program.Program, id, o.Parameter)
                                             && NodeParameters.Find(tables.Get(id), o.Parameter)!.ComponentCount == o.Components.Length,
            AdjustNodeParameterAtCurrentZoomLevel o => ApplySetNodeParameter.CanSet(program.Program, id, o.Parameter)
                                                       && o.Component >= 0
                                                       && o.Component < NodeParameters.Find(tables.Get(id), o.Parameter)!.ComponentCount,
            InteractiveAdjustTransform o => ApplyAdjustTransform.CanAdjust(program.Program, id, o.Kind),
            ScaleUpAtCurrentZoomLevel or ScaleDownAtCurrentZoomLevel
                or ScaleXUpAtCurrentZoomLevel or ScaleXDownAtCurrentZoomLevel
                or ScaleYUpAtCurrentZoomLevel or ScaleYDownAtCurrentZoomLevel
                or ScaleZUpAtCurrentZoomLevel or ScaleZDownAtCurrentZoomLevel
                => ApplyAdjustTransform.CanAdjust(program.Program, id, TransformKind.Scale),
            TranslateXUpAtCurrentZoomLevel or TranslateXDownAtCurrentZoomLevel
                or TranslateYUpAtCurrentZoomLevel or TranslateYDownAtCurrentZoomLevel
                or TranslateZUpAtCurrentZoomLevel or TranslateZDownAtCurrentZoomLevel
                => ApplyAdjustTransform.CanAdjust(program.Program, id, TransformKind.Translate),
            RotateXUpAtCurrentZoomLevel or RotateXDownAtCurrentZoomLevel
                or RotateYUpAtCurrentZoomLevel or RotateYDownAtCurrentZoomLevel
                or RotateZUpAtCurrentZoomLevel or RotateZDownAtCurrentZoomLevel
                => ApplyAdjustTransform.CanAdjust(program.Program, id, TransformKind.Rotate),
            _ => throw new NotSupportedException($"Unknown {nameof(ISdfInteractiveNodeProgramOperation)}: {operation.GetType().Name}"),
        };
    }
}
