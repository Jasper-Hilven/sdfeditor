using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyWrapInIntersection
{
    public static SDFProgram DoApplyWrapInIntersection(SDFProgram program, WrapInIntersection operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!CanWrap(program, operation.nodeId))
            throw new InvalidOperationException($"Only a non-empty SDF node can be intersected, but node '{operation.nodeId}' is not one");

        var sphere = SDFNodeDefaultValues.CreateNode(SDFNodeType.Sphere);
        program = program.ReplaceOnlyReference(tables, operation.nodeId, node => new Intersection((SDFEvaluable)node, sphere));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanWrap(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && tables.PointerTo(nodeId) is SDFEvaluablePointerable && tables.Get(nodeId) is not Empty;
    }
}
