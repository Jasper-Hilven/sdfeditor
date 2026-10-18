using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;

namespace SDFProject.SDFProgramOperator;

public class SdfProgramOperator
{
    public static SDFProgram ApplyOperation(SDFProgram program, SDFProgramOperation operation)
    {
        var result = operation switch
        {
            AddNodeNextTo o => ApplyAddNodeNextTo.DoApplyAddNodeNextTo(program, o),
            InlineVariable o => ApplyInlineVariable.DoApplyInlineVariable(program, o),
            ExtractVariable o => ApplyExtractVariable.DoApplyExtractVariable(program, o),
            EmptyNode o => ApplyEmptyNode.DoApplyEmptyNode(program, o),
            CleanEmptyNodesFromUnionsAndIntersections o => ApplyCleanEmptyNodesFromUnionsAndIntersections.DoApplyCleanEmptyNodesFromUnionsAndIntersections(program, o),
            AdjustTransform o => ApplyAdjustTransform.DoApplyAdjustTransform(program, o),
            ToggleNodeSmoothness o => ApplyToggleNodeSmoothness.DoApplyToggleNodeSmoothness(program, o),
            ReplaceNode o => ApplyReplaceNode.DoApplyReplaceNode(program, o),
            WrapInSubtraction o => ApplyWrapInSubtraction.DoApplyWrapInSubtraction(program, o),
            WrapInIntersection o => ApplyWrapInIntersection.DoApplyWrapInIntersection(program, o),
            WrapInDistortion o => ApplyWrapInDistortion.DoApplyWrapInDistortion(program, o),
            PaintNode o => ApplyPaintNode.DoApplyPaintNode(program, o),
            SetMaterial o => ApplySetMaterial.DoApplySetMaterial(program, o),
            DissolveNode o => ApplyDissolveNode.DoApplyDissolveNode(program, o),
            SetNodeParameter o => ApplySetNodeParameter.DoApplySetNodeParameter(program, o),
            _ => throw new NotSupportedException($"Unknown {nameof(SDFProgramOperation)}: {operation.GetType().Name}"),
        };

        return SDFProgramNormalizePointers.Pointerify(result);
    }
}
