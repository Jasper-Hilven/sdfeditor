using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyWrapInDistortion
{
    public const float DefaultAmount = 0.03f;
    public const float DefaultFrequency = 6f;

    public static SDFProgram DoApplyWrapInDistortion(SDFProgram program, WrapInDistortion operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!CanWrap(program, operation.nodeId))
            throw new InvalidOperationException($"Only a non-empty SDF node can be distorted, but node '{operation.nodeId}' is not one");

        program = program.ReplaceOnlyReference(tables, operation.nodeId,
            node => new Distort((SDFEvaluable)node, new ConstValueFloat(DefaultAmount), new ConstValueFloat(DefaultFrequency)));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanWrap(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && tables.PointerTo(nodeId) is SDFEvaluablePointerable && tables.Get(nodeId) is not Empty;
    }
}
