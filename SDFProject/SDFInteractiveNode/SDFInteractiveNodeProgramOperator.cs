namespace SDFProject.SDFInteractiveNodeProgram;

public class SDFInteractiveNodeProgramOperator
{
    private readonly SDFProgramOperator programOperator = new();

    public SDFInteractiveNodeProgram ApplyOperation(SDFInteractiveNodeProgram program, SDFInteractiveNodeProgramOperation operation)
    {
        string Selected() => program.SelectedNodeId
                             ?? throw new InvalidOperationException($"{operation.GetType().Name} needs a selected node, but no node is selected");

        SDFProgramOperation programOperation = operation switch
        {
            InteractiveAddNodeNextTo o => new AddNodeNextTo(Selected(), o.type),
            InteractiveInlineVariable => new InlineVariable(VariableContaining(program.Program, Selected())),
            InteractiveExtractVariable o => new ExtractVariable(Selected(), o.variableName),
            InteractiveEmptyNode => new EmptyNode(Selected()),
            InteractiveCleanEmptyNodesFromUnionsAndIntersections => new CleanEmptyNodesFromUnionsAndIntersections(),
            InteractiveAdjustScaleTranslateRotate o => new AdjustScaleTranslateRotate(Selected(), o.scale, o.translation, o.rotation),
            _ => throw new NotSupportedException($"Unknown {nameof(SDFInteractiveNodeProgramOperation)}: {operation.GetType().Name}"),
        };

        return ApplyOperation(program, programOperation);
    }

    public SDFInteractiveNodeProgram ApplyOperation(SDFInteractiveNodeProgram program, SDFProgramOperation operation)
    {
        var result = programOperator.ApplyOperation(program.Program, operation);

        var selectedNodeId = operation switch
        {
            AddNodeNextTo o => AddedNodeId(result, o.onNodeId) ?? SurvivingSelection(program, result),
            _ => SurvivingSelection(program, result),
        };

        return program with { Program = result, SelectedNodeId = selectedNodeId };
    }

    private static string VariableContaining(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return program.functions
                   .SelectMany(f => f.variables)
                   .FirstOrDefault(v => tables.Subtree(v.value).Contains(nodeId))?.id
               ?? throw new InvalidOperationException($"Node '{nodeId}' is not part of a variable's value");
    }

    private static string? AddedNodeId(SDFProgram result, string onNodeId)
    {
        var tables = new PointerTables(result.pointers);
        if (tables.ParentOf(onNodeId) is not { } parentId || tables.Get(parentId) is not Union union)
            return null;

        var index = Array.IndexOf(union.primitives, tables.PointerTo(onNodeId));
        return PointerTables.PointerId(union.primitives[index + 1]);
    }

    private static string? SurvivingSelection(SDFInteractiveNodeProgram program, SDFProgram result)
    {
        var tables = new PointerTables(program.Program.pointers);
        var id = program.SelectedNodeId;
        while (id is not null && !result.pointers.ContainsPointer(id))
            id = tables.ParentOf(id);
        return id;
    }
}
