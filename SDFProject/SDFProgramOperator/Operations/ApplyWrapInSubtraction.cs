using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyWrapInSubtraction
{
    public static SDFProgram DoApplyWrapInSubtraction(SDFProgram program, WrapInSubtraction operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!CanWrap(program, operation.nodeId))
            throw new InvalidOperationException($"Only a non-empty SDF node can be subtracted from, but node '{operation.nodeId}' is not one");

        var cut = SDFNodeDefaultValues.CreateNode(SDFNodeType.Sphere);
        program = program.ReplaceOnlyReference(tables, operation.nodeId, node => new Subtraction((SDFEvaluable)node, cut));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanWrap(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && tables.PointerTo(nodeId) is SDFEvaluablePointerable && tables.Get(nodeId) is not Empty;
    }
}
