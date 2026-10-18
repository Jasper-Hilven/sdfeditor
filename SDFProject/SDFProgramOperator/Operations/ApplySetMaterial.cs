using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

/// <summary>Gives a node a named material: wraps it in a Material node, or renames the material it already has, like painting a colour.</summary>
public class ApplySetMaterial
{
    public static SDFProgram DoApplySetMaterial(SDFProgram program, SetMaterial operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!ApplyPaintNode.CanPaint(program, operation.nodeId))
            throw new InvalidOperationException($"Only a non-empty SDF node can get a material, but node '{operation.nodeId}' is not one");

        if (MaterialNodeOf(tables, operation.nodeId) is { } materialNodeId)
            tables.Set(materialNodeId, ((Material)tables.Get(materialNodeId)) with { material = operation.material });
        else
            program = program.ReplaceOnlyReference(tables, operation.nodeId, node => new Material((SDFEvaluable)node, operation.material));

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    /// <summary>The Material node that gives the node its material: the node itself or its parent.</summary>
    public static string? MaterialNodeOf(PointerTables tables, string nodeId) =>
        tables.Get(nodeId) is Material ? nodeId
        : tables.ParentOf(nodeId) is { } parentId && tables.Get(parentId) is Material ? parentId
        : null;
}
