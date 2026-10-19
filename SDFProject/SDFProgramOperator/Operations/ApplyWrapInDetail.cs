using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyWrapInDetail
{
    /// <summary>Half the vertices: a Detail node is added to make a big, simple model cheaper, and 1 would change nothing.</summary>
    public const float DefaultDetail = 0.5f;

    public static SDFProgram DoApplyWrapInDetail(SDFProgram program, WrapInDetail operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!CanWrap(program, operation.nodeId))
            throw new InvalidOperationException($"Only the shape at the root of the scene can get a detail, but node '{operation.nodeId}' is not it");

        program = program.ReplaceOnlyReference(tables, operation.nodeId,
            node => new Detail((SDFEvaluable)node, new ConstValueFloat(DefaultDetail)));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    /// <summary>
    /// The mesh builder reads a Detail node at the root of a file only, so only the scene's root can be wrapped in one, and only once.
    /// </summary>
    public static bool CanWrap(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return PointerTables.PointerId(program.scene) == nodeId && tables.Contains(nodeId) && tables.Get(nodeId) is not (Empty or Detail);
    }
}
