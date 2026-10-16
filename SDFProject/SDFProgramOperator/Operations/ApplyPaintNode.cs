using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyPaintNode
{
    public static SDFProgram DoApplyPaintNode(SDFProgram program, PaintNode operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!CanPaint(program, operation.nodeId))
            throw new InvalidOperationException($"Only a non-empty SDF node can be painted, but node '{operation.nodeId}' is not one");

        var color = new ConstValueV3(operation.color);
        if (ColorNodeOf(tables, operation.nodeId) is { } colorNodeId)
        {
            var existing = (Color)tables.Get(colorNodeId);
            tables.RemoveSubtree(existing.color);
            tables.Set(colorNodeId, existing with { color = color });
        }
        else
        {
            program = program.ReplaceOnlyReference(tables, operation.nodeId, node => new Color((SDFEvaluable)node, color));
        }

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanPaint(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && tables.PointerTo(nodeId) is SDFEvaluablePointerable && tables.Get(nodeId) is not Empty;
    }

    public static string? ColorNodeOf(PointerTables tables, string nodeId) =>
        tables.Get(nodeId) is Color ? nodeId
        : tables.ParentOf(nodeId) is { } parentId && tables.Get(parentId) is Color ? parentId
        : null;
}
