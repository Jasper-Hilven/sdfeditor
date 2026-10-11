using SDFProject.Definitions;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyAddNodeNextTo
{
    public static SDFProgram DoApplyAddNodeNextTo(SDFProgram program, AddNodeNextTo operation)
    {
        var tables = new PointerTables(program.pointers);
        if (tables.PointerTo(operation.onNodeId) is not SDFEvaluablePointerable onNode)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.onNodeId}'");

        var newNode = SDFNodeDefaultValues.CreateNode(operation.type);

        var parentId = tables.ParentOf(operation.onNodeId);
        if (parentId is not null && tables.Get(parentId) is Union union)
        {
            program = program.ReplaceOnlyReference(tables, operation.onNodeId, child => child);
            var primitives = union.primitives.ToList();
            primitives.Insert(primitives.IndexOf(onNode) + 1, newNode);
            tables.Set(parentId, new Union(primitives.ToArray()));
        }
        else
        {
            program = program.ReplaceOnlyReference(tables, operation.onNodeId, child => new Union((SDFEvaluable)child, newNode));
        }

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
