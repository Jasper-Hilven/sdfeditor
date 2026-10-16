using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyEmptyNode
{
    public static SDFProgram DoApplyEmptyNode(SDFProgram program, EmptyNode operation)
    {
        var tables = new PointerTables(program.pointers);
        if (tables.PointerTo(operation.nodeId) is not SDFEvaluablePointerable node)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.nodeId}'");

        program = program.ReplaceOnlyReference(tables, operation.nodeId, _ => new Empty());

        tables.RemoveSubtree(node);

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
