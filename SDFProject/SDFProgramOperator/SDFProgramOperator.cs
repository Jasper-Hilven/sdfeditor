namespace SDFProject;

public class SDFProgramOperator
{
    public SDFProgram ApplyOperation(SDFProgram program, SDFProgramOperation operation)
    {
        var result = operation switch
        {
            AddNodeNextTo o => ApplyAddNodeNextTo.DoApplyAddNodeNextTo(program, o),
            InlineVariable o => ApplyInlineVariable.DoApplyInlineVariable(program, o),
            ExtractVariable o => ApplyExtractVariable.DoApplyExtractVariable(program, o),
            EmptyNode o => ApplyEmptyNode.DoApplyEmptyNode(program, o),
            CleanEmptyNodesFromUnionsAndIntersections o => ApplyCleanEmptyNodesFromUnionsAndIntersections.DoApplyCleanEmptyNodesFromUnionsAndIntersections(program, o),
            AdjustScaleTranslateRotate o => ApplyAdjustScaleTranslateRotate.DoApplyAdjustScaleTranslateRotate(program, o),
            _ => throw new NotSupportedException($"Unknown {nameof(SDFProgramOperation)}: {operation.GetType().Name}"),
        };

        return SDFProgramMapMaker.Pointerify(result);
    }

    


}
