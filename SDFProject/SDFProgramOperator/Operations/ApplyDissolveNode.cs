using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyDissolveNode
{
    public static SDFProgram DoApplyDissolveNode(SDFProgram program, DissolveNode operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!CanDissolve(program, operation.nodeId))
            throw new InvalidOperationException($"Only an SDF node with an SDF child can be dissolved, but node '{operation.nodeId}' is not one");

        var node = tables.Get(operation.nodeId);
        var firstChild = FirstChild(node)!;
        program = program.ReplaceOnlyReference(tables, operation.nodeId, _ => firstChild);

        foreach (var child in EvaluableChildren.Children(node).Where(child => !ReferenceEquals(child, firstChild)))
            tables.RemoveSubtree(child);
        tables.Remove(operation.nodeId);

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanDissolve(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && tables.PointerTo(nodeId) is SDFEvaluablePointerable && FirstChild(tables.Get(nodeId)) is not null;
    }

    public static string? FirstChildId(SDFProgram program, string nodeId) =>
        CanDissolve(program, nodeId) ? PointerTables.PointerId(FirstChild(new PointerTables(program.pointers).Get(nodeId))!) : null;

    private static SDFEvaluable? FirstChild(Evaluable node) =>
        EvaluableChildren.Children(node).OfType<SDFEvaluable>().FirstOrDefault();
}
