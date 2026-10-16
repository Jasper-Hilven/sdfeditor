using SDFProject.SDFProgramOperator.Operations;
using SDFProject.SDFProgramOperator.Helpers;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using System.Globalization;
using Mode = SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public class SDFPartialCommandNodeProgramOperator
{
    public static SDFPartialCommandNodeProgram Operate(SDFPartialCommandNodeProgram partialCommandProgram, InputKey inputKey) =>
        SDFPartialKeyBindings.Bindings.TryGetValue((partialCommandProgram.state.Mode, inputKey), out var action)
            ? Operate(partialCommandProgram, action)
            : partialCommandProgram;

    public static SDFPartialCommandNodeProgram Operate(SDFPartialCommandNodeProgram partialCommandProgram, ISDFPartialAction action)
    {
        if (TakesText(partialCommandProgram.state.Mode))
            return OperateOnText(partialCommandProgram, action);
        if (action is InsertText or DeleteLastCharacter or ConfirmText or CancelText or Undo or Redo or ChooseRedoBranch or CancelRedoChoice or GoToHistoryEntry)
            return partialCommandProgram;

        if (EditingTransition(partialCommandProgram, action) is { } editing)
            return editing;

        var result = TargetMode(action) is { } mode
            ? SwitchMode(partialCommandProgram, mode)
            : Apply(partialCommandProgram, InteractiveOperation(action, partialCommandProgram));

        return NodeTypeToAdd(action) is not null || NodeTypeToReplaceWith(action) is not null ? SwitchMode(result, Mode.Home) : result;
    }

    public static bool TakesText(SDFPartialCommandNodeProgramMode mode) =>
        mode is Mode.ExtractVariable or Mode.TypeParameterValue;

    private static SDFPartialCommandNodeProgram OperateOnText(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        var text = program.state.Text;
        return action switch
        {
            InsertText insert => program with { state = program.state with { Text = text + insert.text } },
            DeleteLastCharacter => program with { state = program.state with { Text = text.Length == 0 ? text : text[..^1] } },
            ConfirmText => LeaveText(Confirm(program)),
            CancelText => LeaveText(program),
            _ => program,
        };
    }

    private static SDFPartialCommandNodeProgram Confirm(SDFPartialCommandNodeProgram program)
    {
        var text = program.state.Text.Trim();
        if (program.state.Mode == Mode.ExtractVariable)
            return Apply(program, new InteractiveExtractVariable(text.Length > 0 ? text : SuggestedVariableName(program)));

        return text.Length > 0 && EditedParameter(program) is { } parameter
            ? Apply(program, new InteractiveSetNodeParameter(parameter.Name, ParseComponents(text, parameter)))
            : program;
    }

    public static string SuggestedVariableName(SDFPartialCommandNodeProgram program) =>
        ApplyExtractVariable.UnusedVariableName(program.interactiveProgram.Program);

    private static float[] ParseComponents(string text, NodeParameter parameter)
    {
        var parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var components = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
                throw new FormatException($"'{parts[i]}' is not a number");
        }
        if (components.Length != parameter.ComponentCount)
            throw new FormatException($"{parameter.Name} takes {parameter.ComponentCount} number(s), but '{text}' has {components.Length}");
        return components;
    }

    private static SDFPartialCommandNodeProgram LeaveText(SDFPartialCommandNodeProgram program) =>
        SwitchMode(program, program.state.Mode == Mode.TypeParameterValue ? Mode.EditParameter : Mode.Home);

    private static SDFPartialCommandNodeProgram? EditingTransition(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        var editable = EditableParameters(program);
        return action switch
        {
            EditColor when ColorParameter(program) is { } color => Edit(program, color),
            EditNode when editable.Count == 1 => Edit(program, editable[0]),
            EditNode => SwitchMode(program, Mode.EditNode),
            StopEditingParameter => SwitchMode(program, editable.Count == 1 ? Mode.Home : Mode.EditNode),
            _ when ParameterToEdit(program, action) is { } parameter => Edit(program, parameter),
            _ => null,
        };
    }

    private static SDFPartialCommandNodeProgram Edit(SDFPartialCommandNodeProgram program, NodeParameter parameter) =>
        program with { state = new SDFPartialCommandNodeProgramState(Mode.EditParameter, parameter.Name) };

    public static NodeParameter? ColorParameter(SDFPartialCommandNodeProgram program) =>
        SelectedNode(program) is Color ? Parameters(program).FirstOrDefault(p => p is { Name: "color", IsConstant: true }) : null;

    public static bool SelectedIsColor(SDFPartialCommandNodeProgram program) => SelectedNode(program) is Color;

    private static Evaluable? SelectedNode(SDFPartialCommandNodeProgram program)
    {
        var interactive = program.interactiveProgram;
        return interactive.SelectedNodeId is { } id && interactive.Program.pointers.ContainsPointer(id)
            ? new PointerTables(interactive.Program.pointers).Get(id)
            : null;
    }

    public static IReadOnlyList<NodeParameter> Parameters(SDFPartialCommandNodeProgram program)
    {
        var interactive = program.interactiveProgram;
        if (interactive.SelectedNodeId is not { } id || !interactive.Program.pointers.ContainsPointer(id))
            return [];
        return NodeParameters.Of(new PointerTables(interactive.Program.pointers).Get(id));
    }

    private static IReadOnlyList<NodeParameter> EditableParameters(SDFPartialCommandNodeProgram program) =>
        Parameters(program).Where(p => p.IsConstant).ToArray();

    public static NodeParameter? ParameterToEdit(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        var index = action switch
        {
            EditParameter1 => 0,
            EditParameter2 => 1,
            EditParameter3 => 2,
            EditParameter4 => 3,
            _ => -1,
        };
        var parameters = Parameters(program);
        return index >= 0 && index < parameters.Count ? parameters[index] : null;
    }

    public static NodeParameter? EditedParameter(SDFPartialCommandNodeProgram program) =>
        program.state.EditedParameter is { } name ? Parameters(program).FirstOrDefault(p => p.Name == name) : null;

    public static SDFPartialCommandNodeProgramMode? TargetMode(ISDFPartialAction action) => action switch
    {
        SwitchToHomeMode => Mode.Home,
        SwitchToScaleMode => Mode.Scale,
        SwitchToRotateMode => Mode.Rotate,
        SwitchToTranslateMode => Mode.Translate,
        AddNode => Mode.AddNode,
        CancelAddNode => Mode.Home,
        ShowMoreNodeTypes => Mode.AddNodeMore,
        ShowFirstNodeTypes => Mode.AddNode,
        ReplaceNode => Mode.ReplaceNode,
        CancelReplaceNode => Mode.Home,
        ShowMoreReplaceNodeTypes => Mode.ReplaceNodeMore,
        ShowFirstReplaceNodeTypes => Mode.ReplaceNode,
        ExtractVariable => Mode.ExtractVariable,
        TypeParameterValue => Mode.TypeParameterValue,
        SwitchToMaterialMode => Mode.Material,
        _ => null,
    };

    public static SDFNodeType? NodeTypeToAdd(ISDFPartialAction action) => action switch
    {
        AddSphere => SDFNodeType.Sphere,
        AddBox => SDFNodeType.Box,
        AddRoundBox => SDFNodeType.RoundBox,
        AddTorus => SDFNodeType.Torus,
        AddBoxFrame => SDFNodeType.BoxFrame,
        AddCappedTorus => SDFNodeType.CappedTorus,
        AddLink => SDFNodeType.Link,
        AddInfiniteCylinder => SDFNodeType.InfiniteCylinder,
        AddCone => SDFNodeType.Cone,
        AddInfiniteCone => SDFNodeType.InfiniteCone,
        AddPlane => SDFNodeType.Plane,
        AddHexPrism => SDFNodeType.HexPrism,
        AddCapsule => SDFNodeType.Capsule,
        AddVerticalCapsule => SDFNodeType.VerticalCapsule,
        AddCappedCylinder => SDFNodeType.CappedCylinder,
        AddArbitraryCappedCylinder => SDFNodeType.ArbitraryCappedCylinder,
        AddRoundedCylinder => SDFNodeType.RoundedCylinder,
        AddCappedCone => SDFNodeType.CappedCone,
        AddArbitraryCappedCone => SDFNodeType.ArbitraryCappedCone,
        AddSolidAngle => SDFNodeType.SolidAngle,
        AddCutSphere => SDFNodeType.CutSphere,
        AddCutHollowSphere => SDFNodeType.CutHollowSphere,
        AddRoundCone => SDFNodeType.RoundCone,
        AddArbitraryRoundCone => SDFNodeType.ArbitraryRoundCone,
        AddVesicaSegment => SDFNodeType.VesicaSegment,
        AddRhombus => SDFNodeType.Rhombus,
        AddOctahedron => SDFNodeType.Octahedron,
        AddOctahedronBound => SDFNodeType.OctahedronBound,
        AddPyramid => SDFNodeType.Pyramid,
        AddTriangle => SDFNodeType.Triangle,
        AddQuad => SDFNodeType.Quad,
        AddEllipsoid => SDFNodeType.Ellipsoid,
        AddTriPrism => SDFNodeType.TriPrism,
        _ => null,
    };

    public static SDFNodeType? NodeTypeToReplaceWith(ISDFPartialAction action) => action switch
    {
        ReplaceWithSphere => SDFNodeType.Sphere,
        ReplaceWithBox => SDFNodeType.Box,
        ReplaceWithRoundBox => SDFNodeType.RoundBox,
        ReplaceWithTorus => SDFNodeType.Torus,
        ReplaceWithBoxFrame => SDFNodeType.BoxFrame,
        ReplaceWithCappedTorus => SDFNodeType.CappedTorus,
        ReplaceWithLink => SDFNodeType.Link,
        ReplaceWithInfiniteCylinder => SDFNodeType.InfiniteCylinder,
        ReplaceWithCone => SDFNodeType.Cone,
        ReplaceWithInfiniteCone => SDFNodeType.InfiniteCone,
        ReplaceWithPlane => SDFNodeType.Plane,
        ReplaceWithHexPrism => SDFNodeType.HexPrism,
        ReplaceWithCapsule => SDFNodeType.Capsule,
        ReplaceWithVerticalCapsule => SDFNodeType.VerticalCapsule,
        ReplaceWithCappedCylinder => SDFNodeType.CappedCylinder,
        ReplaceWithArbitraryCappedCylinder => SDFNodeType.ArbitraryCappedCylinder,
        ReplaceWithRoundedCylinder => SDFNodeType.RoundedCylinder,
        ReplaceWithCappedCone => SDFNodeType.CappedCone,
        ReplaceWithArbitraryCappedCone => SDFNodeType.ArbitraryCappedCone,
        ReplaceWithSolidAngle => SDFNodeType.SolidAngle,
        ReplaceWithCutSphere => SDFNodeType.CutSphere,
        ReplaceWithCutHollowSphere => SDFNodeType.CutHollowSphere,
        ReplaceWithRoundCone => SDFNodeType.RoundCone,
        ReplaceWithArbitraryRoundCone => SDFNodeType.ArbitraryRoundCone,
        ReplaceWithVesicaSegment => SDFNodeType.VesicaSegment,
        ReplaceWithRhombus => SDFNodeType.Rhombus,
        ReplaceWithOctahedron => SDFNodeType.Octahedron,
        ReplaceWithOctahedronBound => SDFNodeType.OctahedronBound,
        ReplaceWithPyramid => SDFNodeType.Pyramid,
        ReplaceWithTriangle => SDFNodeType.Triangle,
        ReplaceWithQuad => SDFNodeType.Quad,
        ReplaceWithEllipsoid => SDFNodeType.Ellipsoid,
        ReplaceWithTriPrism => SDFNodeType.TriPrism,
        _ => null,
    };

    public static ISdfInteractiveNodeProgramOperation InteractiveOperation(ISDFPartialAction action, SDFPartialCommandNodeProgram program) => action switch
    {
        ParameterXDown => AdjustParameter(program.state, 0, -1),
        ParameterXUp => AdjustParameter(program.state, 0, 1),
        ParameterYDown => AdjustParameter(program.state, 1, -1),
        ParameterYUp => AdjustParameter(program.state, 1, 1),
        ParameterZDown => AdjustParameter(program.state, 2, -1),
        ParameterZUp => AdjustParameter(program.state, 2, 1),
        _ when NodeTypeToAdd(action) is { } type => new InteractiveAddNodeNextTo(type),
        _ when NodeTypeToReplaceWith(action) is { } type => new InteractiveReplaceNode(type),
        SelectNext => new SDFInteractiveNode.SelectNext(),
        SelectPrevious => new SDFInteractiveNode.SelectPrevious(),
        SelectParent => new SDFInteractiveNode.SelectParent(),
        SelectFirstChild => new SDFInteractiveNode.SelectFirstChild(),
        TranslateXDown => new TranslateXDownAtCurrentZoomLevel(),
        TranslateXUp => new TranslateXUpAtCurrentZoomLevel(),
        TranslateYDown => new TranslateYDownAtCurrentZoomLevel(),
        TranslateYUp => new TranslateYUpAtCurrentZoomLevel(),
        TranslateZDown => new TranslateZDownAtCurrentZoomLevel(),
        TranslateZUp => new TranslateZUpAtCurrentZoomLevel(),
        RotateXDown => new RotateXDownAtCurrentZoomLevel(),
        RotateXUp => new RotateXUpAtCurrentZoomLevel(),
        RotateYDown => new RotateYDownAtCurrentZoomLevel(),
        RotateYUp => new RotateYUpAtCurrentZoomLevel(),
        RotateZDown => new RotateZDownAtCurrentZoomLevel(),
        RotateZUp => new RotateZUpAtCurrentZoomLevel(),
        DecreaseZoomLevel => new DecreaseStrZoomLevel(),
        IncreaseZoomLevel => new IncreaseStrZoomLevel(),
        ScaleDown => new ScaleDownAtCurrentZoomLevel(),
        ScaleUp => new ScaleUpAtCurrentZoomLevel(),
        ScaleXDown => new ScaleXDownAtCurrentZoomLevel(),
        ScaleXUp => new ScaleXUpAtCurrentZoomLevel(),
        ScaleYDown => new ScaleYDownAtCurrentZoomLevel(),
        ScaleYUp => new ScaleYUpAtCurrentZoomLevel(),
        ScaleZDown => new ScaleZDownAtCurrentZoomLevel(),
        ScaleZUp => new ScaleZUpAtCurrentZoomLevel(),
        ToggleSmoothness => new SDFInteractiveNode.ToggleSmoothness(),
        SubtractNode => new InteractiveWrapInSubtraction(),
        IntersectNode => new InteractiveWrapInIntersection(),
        DistortNode => new InteractiveWrapInDistortion(),
        DissolveNode => new InteractiveDissolveNode(),
        InlineVariable => new InteractiveInlineVariable(),
        EmptyNode => new InteractiveEmptyNode(),
        ApplyInteractiveOperation apply => apply.operation,
        PaintColor paint => new InteractivePaint(paint.color),
        RemoveColor => new InteractiveDissolveNode(),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static AdjustNodeParameterAtCurrentZoomLevel AdjustParameter(SDFPartialCommandNodeProgramState state, int component, float sign) =>
        new(state.EditedParameter ?? throw new InvalidOperationException("No parameter is being edited"), component, sign);

    private static SDFPartialCommandNodeProgram SwitchMode(SDFPartialCommandNodeProgram program, SDFPartialCommandNodeProgramMode mode)
    {
        return program with
        {
            state = program.state with { Mode = mode, Text = "" }
        };
    }

    private static SDFPartialCommandNodeProgram Apply(SDFPartialCommandNodeProgram partialCommandProgram,
        ISdfInteractiveNodeProgramOperation operation)
    {
        return partialCommandProgram with
        {
            interactiveProgram = SdfInteractiveNodeProgramOperator.ApplyOperation(
                partialCommandProgram.interactiveProgram, operation)
        };
    }
}
