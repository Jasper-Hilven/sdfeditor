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
        SDFPartialKeyBindings.ActionFor(partialCommandProgram, inputKey) is { } action
            ? Operate(partialCommandProgram, action)
            : partialCommandProgram;

    public static SDFPartialCommandNodeProgram Operate(SDFPartialCommandNodeProgram partialCommandProgram, ISDFPartialAction action)
    {
        if (TakesText(partialCommandProgram.state.Mode))
            return OperateOnText(partialCommandProgram, action);
        if (action is InsertText or DeleteLastCharacter or ConfirmText or CancelText or Undo or Redo or ChooseRedoBranch or CancelRedoChoice or GoToHistoryEntry)
            return partialCommandProgram;
        if (LockingPart(partialCommandProgram, action) is { } part)
            throw new InvalidOperationException($"This node belongs to part '{part}'; edit it by opening content/parts/{part}.sdf");

        if (MaterialTransition(partialCommandProgram, action) is { } material)
            return material;
        if (EditingTransition(partialCommandProgram, action) is { } editing)
            return editing;

        var result = TargetMode(action) is { } mode
            ? SwitchMode(partialCommandProgram, mode)
            : Apply(partialCommandProgram, InteractiveOperation(action, partialCommandProgram));

        return NodeTypeToAdd(action) is not null || NodeTypeToReplaceWith(action) is not null ? SwitchMode(result, Mode.Home) : result;
    }

    /// <summary>
    /// The part whose node is selected, when the action would change it: inside a model a part can be looked at but not edited, so an
    /// edit can never get lost when the model is saved.
    /// </summary>
    public static string? LockingPart(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        if (action is SelectNext or SelectPrevious or SelectParent or SelectFirstChild or DecreaseZoomLevel or IncreaseZoomLevel
            or CancelMaterials or StopEditingParameter or StopEditingMaterialParameter || TargetMode(action) is not null)
            return null;
        var interactive = program.interactiveProgram;
        return interactive.SelectedNodeId is { } id ? Parts.PartContaining(interactive.Program, id) : null;
    }

    public static bool TakesText(SDFPartialCommandNodeProgramMode mode) =>
        mode is Mode.ExtractVariable or Mode.TypeParameterValue or Mode.TypeMaterialValue;

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
        if (program.state.Mode == Mode.TypeMaterialValue)
            return text.Length > 0 && EditedMaterial(program) is { } edited
                ? WithMaterialParameter(program, edited, ParseComponents(text, edited.Parameter.Name, edited.Parameter.ComponentCount))
                : program;

        return text.Length > 0 && EditedParameter(program) is { } parameter
            ? Apply(program, new InteractiveSetNodeParameter(parameter.Name, ParseComponents(text, parameter.Name, parameter.ComponentCount)))
            : program;
    }

    public static string SuggestedVariableName(SDFPartialCommandNodeProgram program) =>
        ApplyExtractVariable.UnusedVariableName(program.interactiveProgram.Program);

    private static float[] ParseComponents(string text, string name, int componentCount)
    {
        var parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var components = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
                throw new FormatException($"'{parts[i]}' is not a number");
        }
        if (components.Length != componentCount)
            throw new FormatException($"{name} takes {componentCount} number(s), but '{text}' has {components.Length}");
        return components;
    }

    private static SDFPartialCommandNodeProgram LeaveText(SDFPartialCommandNodeProgram program) =>
        SwitchMode(program, program.state.Mode switch
        {
            Mode.TypeParameterValue => Mode.EditParameter,
            Mode.TypeMaterialValue => Mode.EditMaterialParameter,
            _ => Mode.Home,
        });

    /// <summary>The material of the selected node being edited, with the parameter being edited.</summary>
    public sealed record EditedMaterialParameter(string Name, MaterialDefinition Material, MaterialParameter Parameter);

    private static SDFPartialCommandNodeProgram? MaterialTransition(SDFPartialCommandNodeProgram program, ISDFPartialAction action)
    {
        var editingValue = program.state.Mode == Mode.EditMaterialParameter;
        return action switch
        {
            ShowMaterials => SwitchMode(program, Mode.Materials),
            CancelMaterials => SwitchMode(program, Mode.Material),
            AssignMaterial assign => SwitchMode(Apply(program, new InteractiveSetMaterial(assign.name)), Mode.Material),
            EditMaterial => program with { state = new SDFPartialCommandNodeProgramState(Mode.EditMaterial) },
            PickMaterialParameter pick => program with
            {
                state = new SDFPartialCommandNodeProgramState(Mode.EditMaterialParameter, MaterialDefinition.Parameters[pick.index].Name),
            },
            StopEditingMaterialParameter => SwitchMode(program, Mode.EditMaterial),
            TypeParameterValue when editingValue => SwitchMode(program, Mode.TypeMaterialValue),
            ParameterXDown when editingValue => AdjustMaterial(program, 0, -1),
            ParameterXUp when editingValue => AdjustMaterial(program, 0, 1),
            ParameterYDown when editingValue => AdjustMaterial(program, 1, -1),
            ParameterYUp when editingValue => AdjustMaterial(program, 1, 1),
            ParameterZDown when editingValue => AdjustMaterial(program, 2, -1),
            ParameterZUp when editingValue => AdjustMaterial(program, 2, 1),
            _ => null,
        };
    }

    /// <summary>The material of the selected node: on a Material node, or on the node a Material node wraps.</summary>
    public static string? SelectedMaterialName(SDFPartialCommandNodeProgram program)
    {
        var interactive = program.interactiveProgram;
        if (interactive.SelectedNodeId is not { } id || !interactive.Program.pointers.ContainsPointer(id))
            return null;
        var tables = new PointerTables(interactive.Program.pointers);
        return ApplySetMaterial.MaterialNodeOf(tables, id) is { } materialNodeId ? ((Material)tables.Get(materialNodeId)).material : null;
    }

    public static EditedMaterialParameter? EditedMaterial(SDFPartialCommandNodeProgram program) =>
        program.state.Mode is Mode.EditMaterialParameter or Mode.TypeMaterialValue
        && program.state.EditedParameter is { } parameterName
        && MaterialDefinition.FindParameter(parameterName) is { } parameter
        && SelectedMaterialName(program) is { } name
        && program.Materials.Materials.TryGetValue(name, out var material)
            ? new EditedMaterialParameter(name, material, parameter)
            : null;

    /// <summary>Steps one component of the edited material parameter by the zoom level's step, like a node parameter.</summary>
    private static SDFPartialCommandNodeProgram AdjustMaterial(SDFPartialCommandNodeProgram program, int component, float sign)
    {
        var edited = EditedMaterial(program) ?? throw new InvalidOperationException("No material parameter is being edited");
        if (component >= edited.Parameter.ComponentCount)
            throw new InvalidOperationException($"{edited.Parameter.Name} has no component {component}");

        var step = SdfInteractiveNodeProgramOperator.TranslateZoomScaleToTranslation(program.interactiveProgram.StrZoomLevel);
        var components = edited.Parameter.Get(edited.Material);
        components[component] += SdfInteractiveNodeProgramOperator.SnapDelta(components[component], step, sign);
        return WithMaterialParameter(program, edited, components);
    }

    /// <summary>Colours stay between 0 and 1 and numbers stay positive; directions can point anywhere.</summary>
    private static SDFPartialCommandNodeProgram WithMaterialParameter(SDFPartialCommandNodeProgram program, EditedMaterialParameter edited, float[] components)
    {
        var limited = edited.Parameter.Kind switch
        {
            MaterialParameterKind.Color => components.Select(c => Math.Clamp(c, 0, 1)).ToArray(),
            MaterialParameterKind.Number => components.Select(c => Math.Max(c, 0)).ToArray(),
            _ => components,
        };
        return program with { Materials = program.Materials.With(edited.Name, edited.Parameter.With(edited.Material, limited)) };
    }

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

    public static bool SelectedIsColor(SDFPartialCommandNodeProgram program) => SelectedNode(program) is Color or Material;

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
        DetailNode => new InteractiveWrapInDetail(),
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
