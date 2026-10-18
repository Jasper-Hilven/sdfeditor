using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public record SDFPartialActionLabel(string Symbol, string Text);

public static class SDFPartialActionLabels
{
    public static SDFPartialActionLabel Label(ISDFPartialAction action)
    {
        if (SDFPartialCommandNodeProgramOperator.NodeTypeToAdd(action) is { } added)
            return new("+", NodeTypeLabel(added));
        if (SDFPartialCommandNodeProgramOperator.NodeTypeToReplaceWith(action) is { } replacement)
            return new("⇄", NodeTypeLabel(replacement));

        return action switch
        {
            SelectNext => new("↓", "next"),
            SelectPrevious => new("↑", "prev"),
            SelectParent => new("←", "parent"),
            SelectFirstChild => new("→", "child"),
            SwitchToHomeMode => new("⌂", "home"),
            SwitchToScaleMode => new("⊞", "scale"),
            SwitchToRotateMode => new("↻", "rotate"),
            SwitchToTranslateMode => new("✥", "move"),
            TranslateXDown => new("✥", "x−"),
            TranslateXUp => new("✥", "x+"),
            TranslateYDown => new("✥", "y−"),
            TranslateYUp => new("✥", "y+"),
            TranslateZDown => new("✥", "z−"),
            TranslateZUp => new("✥", "z+"),
            RotateXDown => new("↺", "x−"),
            RotateXUp => new("↻", "x+"),
            RotateYDown => new("↺", "y−"),
            RotateYUp => new("↻", "y+"),
            RotateZDown => new("↺", "z−"),
            RotateZUp => new("↻", "z+"),
            DecreaseZoomLevel => new("◎", "coarser"),
            IncreaseZoomLevel => new("◉", "finer"),
            ScaleDown => new("⊟", "all−"),
            ScaleUp => new("⊞", "all+"),
            ScaleXDown => new("⊟", "x−"),
            ScaleXUp => new("⊞", "x+"),
            ScaleYDown => new("⊟", "y−"),
            ScaleYUp => new("⊞", "y+"),
            ScaleZDown => new("⊟", "z−"),
            ScaleZUp => new("⊞", "z+"),
            ToggleSmoothness => new("≈", "smooth"),
            SubtractNode => new("⊖", "subtract"),
            IntersectNode => new("∩", "intersect"),
            DistortNode => new("≋", "distort"),
            DissolveNode => new("⤒", "dissolve"),
            ExtractVariable => new("$", "extract"),
            InlineVariable => new("⇲", "inline"),
            EditNode => new("✎", "edit"),
            EditParameter1 => new("✎", "#1"),
            EditParameter2 => new("✎", "#2"),
            EditParameter3 => new("✎", "#3"),
            EditParameter4 => new("✎", "#4"),
            StopEditingParameter => new("«", "back"),
            ParameterXDown => new("−", "x"),
            ParameterXUp => new("+", "x"),
            ParameterYDown => new("−", "y"),
            ParameterYUp => new("+", "y"),
            ParameterZDown => new("−", "z"),
            ParameterZUp => new("+", "z"),
            TypeParameterValue => new("#", "type"),
            EmptyNode => new("∅", "empty"),
            AddNode => new("+", "add"),
            ReplaceNode => new("⇄", "replace"),
            CancelAddNode or CancelReplaceNode => new("✕", "cancel"),
            ShowMoreNodeTypes or ShowMoreReplaceNodeTypes => new("»", "more"),
            ShowFirstNodeTypes or ShowFirstReplaceNodeTypes => new("«", "back"),
            InsertText insert => new("⌨", insert.text),
            DeleteLastCharacter => new("⌫", "delete"),
            ConfirmText => new("⏎", "ok"),
            CancelText => new("✕", "cancel"),
            ApplyInteractiveOperation => new("⚙", "operation"),
            Undo => new("↶", "undo"),
            SwitchToMaterialMode => new("◐", "material"),
            PaintColor paint => new("■", paint.name),
            EditColor => new("✎", "color"),
            RemoveColor => new("◌", "unpaint"),
            ShowMaterials => new("▦", "materials"),
            CancelMaterials => new("«", "back"),
            AssignMaterial assign => new("◆", assign.name),
            EditMaterial => new("✎", "material"),
            PickMaterialParameter pick => new("✎", pick.index >= 0 && pick.index < MaterialDefinition.Parameters.Count
                ? MaterialDefinition.Parameters[pick.index].Name
                : $"#{pick.index + 1}"),
            StopEditingMaterialParameter => new("«", "back"),
            Redo => new("↷", "redo"),
            ChooseRedoBranch choose => new("⎇", $"#{choose.index + 1}"),
            CancelRedoChoice => new("✕", "cancel"),
            GoToHistoryEntry => new("●", "go to"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

    public static SDFPartialActionLabel Label(ISDFPartialAction action, SDFPartialCommandNodeProgram program)
    {
        var label = Label(action);
        if (SDFPartialCommandNodeProgramOperator.EditedMaterial(program) is { } material)
            return MaterialValueLabel(label, action, material.Parameter);
        if (SDFPartialCommandNodeProgramOperator.ParameterToEdit(program, action) is { } picked)
            return label with { Text = picked.Name };

        if (action is not (ParameterXDown or ParameterXUp)
            || SDFPartialCommandNodeProgramOperator.EditedParameter(program) is not { } edited)
            return label;

        var up = action is ParameterXUp;
        return edited.Kind switch
        {
            NodeParameterKind.Angle => new(up ? "↻" : "↺", up ? "°+" : "°−"),
            NodeParameterKind.Number => label with { Text = edited.Name },
            _ => label,
        };
    }

    /// <summary>Colours step their red, green and blue; a single number shows its name.</summary>
    private static SDFPartialActionLabel MaterialValueLabel(SDFPartialActionLabel label, ISDFPartialAction action, MaterialParameter parameter) =>
        (parameter.Kind, action) switch
        {
            (MaterialParameterKind.Color, ParameterXDown or ParameterXUp) => label with { Text = "red" },
            (MaterialParameterKind.Color, ParameterYDown or ParameterYUp) => label with { Text = "green" },
            (MaterialParameterKind.Color, ParameterZDown or ParameterZUp) => label with { Text = "blue" },
            (MaterialParameterKind.Number, ParameterXDown or ParameterXUp) => label with { Text = parameter.Name },
            _ => label,
        };

    private static string NodeTypeLabel(SDFNodeType type) => type switch
    {
        SDFNodeType.Sphere => "sphere",
        SDFNodeType.Box => "box",
        SDFNodeType.RoundBox => "round box",
        SDFNodeType.Torus => "torus",
        SDFNodeType.BoxFrame => "box frame",
        SDFNodeType.CappedTorus => "capped torus",
        SDFNodeType.Link => "link",
        SDFNodeType.InfiniteCylinder => "∞ cylinder",
        SDFNodeType.Cone => "cone",
        SDFNodeType.InfiniteCone => "∞ cone",
        SDFNodeType.Plane => "plane",
        SDFNodeType.HexPrism => "hexagonal prism",
        SDFNodeType.Capsule => "capsule",
        SDFNodeType.VerticalCapsule => "vertical capsule",
        SDFNodeType.CappedCylinder => "capped cylinder",
        SDFNodeType.ArbitraryCappedCylinder => "arbitrary cylinder",
        SDFNodeType.RoundedCylinder => "rounded cylinder",
        SDFNodeType.CappedCone => "capped cone",
        SDFNodeType.ArbitraryCappedCone => "arbitrary cone",
        SDFNodeType.SolidAngle => "solid angle",
        SDFNodeType.CutSphere => "cut sphere",
        SDFNodeType.CutHollowSphere => "cut hollow sphere",
        SDFNodeType.RoundCone => "round cone",
        SDFNodeType.ArbitraryRoundCone => "arbitrary round cone",
        SDFNodeType.VesicaSegment => "vesica segment",
        SDFNodeType.Rhombus => "rhombus",
        SDFNodeType.Octahedron => "octahedron",
        SDFNodeType.OctahedronBound => "octahedron ≈",
        SDFNodeType.Pyramid => "pyramid",
        SDFNodeType.Triangle => "triangle",
        SDFNodeType.Quad => "quadrilateral",
        SDFNodeType.Ellipsoid => "ellipsoid",
        SDFNodeType.TriPrism => "triangular prism",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
