using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.SDFProgramOperator.Operations;

namespace SDFProject.SDFInteractiveNode.InteractiveCommands;

public class SdfInteractiveNodeProgramOperator
{
    public const int MaxStrZoomLevel = 3;

    private static readonly V3 UnitX = new(1, 0, 0);
    private static readonly V3 UnitY = new(0, 1, 0);
    private static readonly V3 UnitZ = new(0, 0, 1);
    private static readonly V3 AllAxes = new(1, 1, 1);

    private readonly SDFProgramOperator.SdfProgramOperator _programOperator = new();

    public static SdfInteractiveNodeProgram ApplyOperation(SdfInteractiveNodeProgram program, ISdfInteractiveNodeProgramOperation operation)
    {
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

        var pointers = program.Program.pointers;
        switch (operation)
        {
            case SelectNext:
                return program with { SelectedNodeId = NodeNavigation.Next(program.Program, Selected()) };
            case SelectPrevious:
                return program with { SelectedNodeId = NodeNavigation.Previous(program.Program, Selected()) };
            case SelectNextSibling:
                return program with { SelectedNodeId = NodeNavigation.NextSiblingOrParent(pointers, Selected()) };
            case SelectPreviousSibling:
                return program with { SelectedNodeId = NodeNavigation.PreviousSiblingOrParent(pointers, Selected()) };
            case SelectParent:
                return program with { SelectedNodeId = NodeNavigation.Parent(pointers, Selected()) };
            case SelectFirstChild:
                return program with { SelectedNodeId = NodeNavigation.FirstChild(pointers, Selected()) };
        }

        var zoomLevel = program.StrZoomLevel;
        V3 Current(TransformKind kind) => ApplyAdjustTransform.Current(program.Program, Selected(), kind);
        AdjustTransform Scale(V3 axes, float sign)
        {
            var step = TranslateZoomScaleToScale(zoomLevel);
            var scale = Current(TransformKind.Scale);
            float Delta(float value, float onAxis) => onAxis == 0 ? 0 : SnapDelta(value, step, sign);
            return new(Selected(), TransformKind.Scale, new V3(Delta(scale.x, axes.x), Delta(scale.y, axes.y), Delta(scale.z, axes.z)));
        }
        AdjustTransform Translate(V3 axis, float sign) =>
            new(Selected(), TransformKind.Translate, Times(axis, SnapDelta(Dot(Current(TransformKind.Translate), axis), TranslateZoomScaleToTranslation(zoomLevel), sign)));
        AdjustTransform Rotate(V3 axis, float sign) =>
            new(Selected(), TransformKind.Rotate, Times(axis, SnapDelta(Dot(Current(TransformKind.Rotate), axis) * 180 / MathF.PI, TranslateZoomScaleToDegree(zoomLevel), sign) * MathF.PI / 180));

        SetNodeParameter AdjustParameter(AdjustNodeParameterAtCurrentZoomLevel o)
        {
            var node = new PointerTables(pointers).Get(Selected());
            var parameter = NodeParameters.Find(node, o.Parameter)
                            ?? throw new KeyNotFoundException($"A {node.GetType().Name} has no parameter '{o.Parameter}'");
            var step = parameter.Kind == NodeParameterKind.Angle
                ? TranslateZoomScaleToDegree(zoomLevel)
                : TranslateZoomScaleToTranslation(zoomLevel);
            var components = parameter.Components();
            components[o.Component] += SnapDelta(components[o.Component], step, o.Sign);
            return new SetNodeParameter(Selected(), o.Parameter, components);
        }

        SDFProgramOperation programOperation = operation switch
        {
            InteractiveAddNodeNextTo o => new AddNodeNextTo(Selected(), o.Type),
            InteractiveReplaceNode o => new ReplaceNode(Selected(), o.Type),
            InteractiveInlineVariable => new InlineVariable(VariableToInline(program.Program, Selected())),
            InteractiveExtractVariable o => new ExtractVariable(Selected(), o.VariableName),
            InteractiveEmptyNode => new EmptyNode(Selected()),
            InteractiveCleanEmptyNodesFromUnionsAndIntersections => new CleanEmptyNodesFromUnionsAndIntersections(),
            InteractiveAdjustTransform o => new AdjustTransform(Selected(), o.Kind, o.Delta),
            ToggleSmoothness => new ToggleNodeSmoothness(Selected()),
            InteractiveWrapInSubtraction => new WrapInSubtraction(Selected()),
            InteractiveWrapInIntersection => new WrapInIntersection(Selected()),
            InteractiveWrapInDistortion => new WrapInDistortion(Selected()),
            InteractiveWrapInDetail => new WrapInDetail(Selected()),
            InteractivePaint o => new PaintNode(Selected(), o.Color),
            InteractiveSetMaterial o => new SetMaterial(Selected(), o.Material),
            InteractiveDissolveNode => new DissolveNode(Selected()),
            InteractiveSetNodeParameter o => new SetNodeParameter(Selected(), o.Parameter, o.Components),
            AdjustNodeParameterAtCurrentZoomLevel o => AdjustParameter(o),
            ScaleUpAtCurrentZoomLevel => Scale(AllAxes, 1),
            ScaleDownAtCurrentZoomLevel => Scale(AllAxes, -1),
            ScaleXUpAtCurrentZoomLevel => Scale(UnitX, 1),
            ScaleXDownAtCurrentZoomLevel => Scale(UnitX, -1),
            ScaleYUpAtCurrentZoomLevel => Scale(UnitY, 1),
            ScaleYDownAtCurrentZoomLevel => Scale(UnitY, -1),
            ScaleZUpAtCurrentZoomLevel => Scale(UnitZ, 1),
            ScaleZDownAtCurrentZoomLevel => Scale(UnitZ, -1),
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

    public static SdfInteractiveNodeProgram ApplyOperation(SdfInteractiveNodeProgram program, SDFProgramOperation operation)
    {
        var dissolvedInto = operation is DissolveNode dissolve ? ApplyDissolveNode.FirstChildId(program.Program, dissolve.nodeId) : null;
        var inlinedInto = operation is InlineVariable inline ? InlinedValueId(program, inline.variableId) : null;
        var result = SdfProgramOperator.ApplyOperation(program.Program, operation);

        var selectedNodeId = operation switch
        {
            AddNodeNextTo o => AddedNodeId(result, o.onNodeId) ?? SurvivingSelection(program, result),
            WrapInSubtraction o => SubtractedNodeId(result, o.nodeId) ?? SurvivingSelection(program, result),
            WrapInIntersection o => IntersectedNodeId(result, o.nodeId) ?? SurvivingSelection(program, result),
            WrapInDistortion o => new PointerTables(result.pointers).ParentOf(o.nodeId) ?? SurvivingSelection(program, result),
            WrapInDetail o => new PointerTables(result.pointers).ParentOf(o.nodeId) ?? SurvivingSelection(program, result),
            PaintNode o => ApplyPaintNode.ColorNodeOf(new PointerTables(result.pointers), o.nodeId) ?? SurvivingSelection(program, result),
            SetMaterial o => ApplySetMaterial.MaterialNodeOf(new PointerTables(result.pointers), o.nodeId) ?? SurvivingSelection(program, result),
            DissolveNode when dissolvedInto is not null && new PointerTables(result.pointers).Contains(dissolvedInto) => dissolvedInto,
            InlineVariable when inlinedInto is not null && new PointerTables(result.pointers).Contains(inlinedInto) => inlinedInto,
            AdjustTransform => SurvivingPrimitive(program, result) ?? SurvivingSelection(program, result),
            _ => SurvivingSelection(program, result),
        };

        return program with { Program = result, SelectedNodeId = selectedNodeId };
    }

    private static V3 Times(V3 v, float factor) => new(v.x * factor, v.y * factor, v.z * factor);

    private static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    internal static float SnapDelta(float value, float step, float sign)
    {
        const float tolerance = 1e-3f;
        var steps = value / step;
        var target = sign > 0 ? MathF.Floor(steps + tolerance) + 1 : MathF.Ceiling(steps - tolerance) - 1;
        return target * step - value;
    }

    private static string VariableToInline(SDFProgram program, string nodeId) =>
        FindVariableToInline(program, nodeId)
        ?? throw new InvalidOperationException($"Node '{nodeId}' is neither a variable reference nor part of a variable's value");

    public static string? FindVariableToInline(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && tables.Get(nodeId) is VariableReference reference
            ? reference.variableId
            : FindVariableContaining(program, nodeId);
    }

    private static string? InlinedValueId(SdfInteractiveNodeProgram program, string variableId)
    {
        var tables = new PointerTables(program.Program.pointers);
        if (program.SelectedNodeId is not { } id || !tables.Contains(id) || tables.Get(id) is not VariableReference reference
            || reference.variableId != variableId)
            return null;

        return PointerTables.PointerId(program.Program.Scopes().SelectMany(s => s.variables).Single(v => v.id == variableId).value);
    }

    public static string? FindVariableContaining(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return program.Scopes()
            .SelectMany(s => s.variables)
            .FirstOrDefault(v => tables.Subtree(v.value).Contains(nodeId))?.id;
    }

    private static string? AddedNodeId(SDFProgram result, string onNodeId)
    {
        var tables = new PointerTables(result.pointers);
        if (tables.ParentOf(onNodeId) is not { } parentId || tables.Get(parentId) is not Union union)
            return null;

        var index = Array.IndexOf(union.primitives, tables.PointerTo(onNodeId));
        return PointerTables.PointerId(union.primitives[index + 1]);
    }

    private static string? SubtractedNodeId(SDFProgram result, string nodeId)
    {
        var tables = new PointerTables(result.pointers);
        return tables.ParentOf(nodeId) is { } parentId && tables.Get(parentId) is Subtraction subtraction
            ? PointerTables.PointerId(subtraction.b)
            : null;
    }

    private static string? IntersectedNodeId(SDFProgram result, string nodeId)
    {
        var tables = new PointerTables(result.pointers);
        return tables.ParentOf(nodeId) is { } parentId && tables.Get(parentId) is Intersection { primitives.Length: 2 } intersection
            ? PointerTables.PointerId(intersection.primitives[1])
            : null;
    }

    private static string? SurvivingPrimitive(SdfInteractiveNodeProgram program, SDFProgram result)
    {
        var tables = new PointerTables(program.Program.pointers);
        var id = program.SelectedNodeId;
        while (id is not null && !result.pointers.ContainsPointer(id))
            id = tables.Get(id) switch
            {
                Scale e => PointerTables.PointerId(e.primitive),
                Rotate e => PointerTables.PointerId(e.primitive),
                Translate e => PointerTables.PointerId(e.primitive),
                _ => null,
            };
        return id;
    }

    private static string? SurvivingSelection(SdfInteractiveNodeProgram program, SDFProgram result)
    {
        var tables = new PointerTables(program.Program.pointers);
        var id = program.SelectedNodeId;
        while (id is not null && !result.pointers.ContainsPointer(id))
            id = tables.ParentOf(id);

        return id ?? (program.SelectedNodeId is null ? null : PointerTables.PointerId(result.scene));
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
