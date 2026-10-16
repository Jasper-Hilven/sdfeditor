using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyReplaceNode
{
    public static SDFProgram DoApplyReplaceNode(SDFProgram program, ReplaceNode operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!tables.Contains(operation.nodeId) || tables.PointerTo(operation.nodeId) is not SDFEvaluablePointerable)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.nodeId}'");

        foreach (var child in EvaluableChildren.Children(tables.Get(operation.nodeId)))
            tables.RemoveSubtree(child);

        tables.Set(operation.nodeId, SDFNodeDefaultValues.CreateNode(operation.type));

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
