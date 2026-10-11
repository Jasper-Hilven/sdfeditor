using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFInteractiveNode;

public class SdfInteractiveNodeProgramOperator
{
    // From this level on the Translate methods give their finest step, so zooming in further changes nothing
    public const int MaxStrZoomLevel = 3;

    private static readonly V3 Zero = new(0, 0, 0);
    private static readonly V3 UnitX = new(1, 0, 0);
    private static readonly V3 UnitY = new(0, 1, 0);
    private static readonly V3 UnitZ = new(0, 0, 1);

    private readonly SDFProgramOperator.SDFProgramOperator _programOperator = new();

    public SdfInteractiveNodeProgram ApplyOperation(SdfInteractiveNodeProgram program, ISdfInteractiveNodeProgramOperation operation)
    {
        // Zooming only changes the step size of the scale, translate and rotate operations, not the program
        switch (operation)
        {
            case IncreaseStrZoomLevel:
                return program with { StrZoomLevel = Math.Min(program.StrZoomLevel + 1, MaxStrZoomLevel) };
            case DecreaseStrZoomLevel:
                return program with { StrZoomLevel = Math.Max(program.StrZoomLevel - 1, 0) };
            case ZoomFullyOutStrZoomLevel:
                return program with { StrZoomLevel = 0 };
        }

        string Selected() => program.SelectedNodeId
                             ?? throw new InvalidOperationException($"{operation.GetType().Name} needs a selected node, but no node is selected");

        var zoomLevel = program.StrZoomLevel;
        AdjustScaleTranslateRotate Scale(float sign) =>
            new(Selected(), sign * TranslateZoomScaleToScale(zoomLevel), Zero, Zero);
        AdjustScaleTranslateRotate Translate(V3 axis, float sign) =>
            new(Selected(), 0, Times(axis, sign * TranslateZoomScaleToTranslation(zoomLevel)), Zero);
        // The rotation of a ScaleTranslateRotate is in radians
        AdjustScaleTranslateRotate Rotate(V3 axis, float sign) =>
            new(Selected(), 0, Zero, Times(axis, sign * TranslateZoomScaleToDegree(zoomLevel) * MathF.PI / 180));

        SDFProgramOperation programOperation = operation switch
        {
            InteractiveAddNodeNextTo o => new AddNodeNextTo(Selected(), o.Type),
            InteractiveInlineVariable => new InlineVariable(VariableContaining(program.Program, Selected())),
            InteractiveExtractVariable o => new ExtractVariable(Selected(), o.VariableName),
            InteractiveEmptyNode => new EmptyNode(Selected()),
            InteractiveCleanEmptyNodesFromUnionsAndIntersections => new CleanEmptyNodesFromUnionsAndIntersections(),
            InteractiveAdjustScaleTranslateRotate o => new AdjustScaleTranslateRotate(Selected(), o.Scale, o.Translation, o.Rotation),
            ScaleUpAtCurrentZoomLevel => Scale(1),
            ScaleDownAtCurrentZoomLevel => Scale(-1),
            TranslateXUpAtCurrentZoomLevel => Translate(UnitX, 1),
            TranslateXDownAtCurrentZoomLevel => Translate(UnitX, -1),
            TranslateYUpAtCurrentZoomLevel => Translate(UnitY, 1),
            TranslateYDownAtCurrentZoomLevel => Translate(UnitY, -1),
            TranslateZUpAtCurrentZoomLevel => Translate(UnitZ, 1),
            TranslateZDownAtCurrentZoomLevel => Translate(UnitZ, -1),
            RotateXUpAtCurrentZoomLevel => Rotate(UnitX, 1),
            RotateXDownAtCurrentZoomLevel => Rotate(UnitX, -1),
            RotateYUpAtCurrentZoomLevel => Rotate(UnitY, 1),
            RotateYDownAtCurrentZoomLevel => Rotate(UnitY, -1),
            RotateZUpAtCurrentZoomLevel => Rotate(UnitZ, 1),
            RotateZDownAtCurrentZoomLevel => Rotate(UnitZ, -1),
            _ => throw new NotSupportedException($"Unknown {nameof(ISdfInteractiveNodeProgramOperation)}: {operation.GetType().Name}"),
        };

        return ApplyOperation(program, programOperation);
    }

    public SdfInteractiveNodeProgram ApplyOperation(SdfInteractiveNodeProgram program, SDFProgramOperation operation)
    {
        var result = _programOperator.ApplyOperation(program.Program, operation);

        var selectedNodeId = operation switch
        {
            AddNodeNextTo o => AddedNodeId(result, o.onNodeId) ?? SurvivingSelection(program, result),
            _ => SurvivingSelection(program, result),
        };

        return program with { Program = result, SelectedNodeId = selectedNodeId };
    }

    private static V3 Times(V3 v, float factor) => new(v.x * factor, v.y * factor, v.z * factor);

    private static string VariableContaining(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return program.Functions
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

    private static string? SurvivingSelection(SdfInteractiveNodeProgram program, SDFProgram result)
    {
        var tables = new PointerTables(program.Program.pointers);
        var id = program.SelectedNodeId;
        while (id is not null && !result.pointers.ContainsPointer(id))
            id = tables.ParentOf(id);
        return id;
    }
    
    public static float TranslateZoomScaleToDegree(int scale)
    {
        if (scale == 0)
            return 90;
        if (scale == 1)
            return 15;
        if (scale == 2)
            return 1;
        return .1f;
    }
    public static float TranslateZoomScaleToTranslation(int scale)
    {
        if (scale == 0)
            return .5f;
        if (scale == 1)
            return .1f;
        if (scale == 2)
            return .02f;
        if (scale == 3)
            return .005f;
        return .001f;
    }
    public static float TranslateZoomScaleToScale(int scale)
    {
        if (scale == 0)
            return .5f;
        if (scale == 1)
            return .1f;
        if (scale == 2)
            return .02f;
        return .005f;
    }
}
