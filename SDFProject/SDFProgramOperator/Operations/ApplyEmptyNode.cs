using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyEmptyNode
{
    public static SDFProgram DoApplyEmptyNode(SDFProgram program, EmptyNode operation)
    {
        var tables = new PointerTables(program.pointers);
        // Empty only exists as an SDF node, so only SDF nodes can be emptied
        if (tables.PointerTo(operation.nodeId) is not SDFEvaluablePointerable node)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.nodeId}'");

        program = program.ReplaceOnlyReference(tables, operation.nodeId, _ => new Empty());

        // Variable references are leaves of the subtree, so they disappear with it while the variables are kept
        tables.RemoveSubtree(node);

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
