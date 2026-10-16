using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyReplaceNode
{
    public static SDFProgram DoApplyReplaceNode(SDFProgram program, ReplaceNode operation)
    {
        var tables = new PointerTables(program.pointers);
        // Node types are SDF nodes, so only SDF nodes can be replaced by one
        if (!tables.Contains(operation.nodeId) || tables.PointerTo(operation.nodeId) is not SDFEvaluablePointerable)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.nodeId}'");

        // Variable references are leaves of the subtree, so they disappear with it while the variables are kept
        foreach (var child in EvaluableChildren.Children(tables.Get(operation.nodeId)))
            tables.RemoveSubtree(child);

        // The node keeps its id, so a selection of it stays on the new node; normalizing gives the new children their pointers
        tables.Set(operation.nodeId, SDFNodeDefaultValues.CreateNode(operation.type));

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
