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
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

    public static SDFPartialActionLabel Label(ISDFPartialAction action, SDFPartialCommandNodeProgram program)
    {
        var label = Label(action);
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

    private static string NodeTypeLabel(SDFNodeType type) => type switch
    {
        SDFNodeType.Sphere => "sphere",
        SDFNodeType.Box => "box",
        SDFNodeType.RoundBox => "rbox",
        SDFNodeType.Torus => "torus",
        SDFNodeType.BoxFrame => "frame",
        SDFNodeType.CappedTorus => "ctorus",
        SDFNodeType.Link => "link",
        SDFNodeType.InfiniteCylinder => "∞cyl",
        SDFNodeType.Cone => "cone",
        SDFNodeType.InfiniteCone => "∞cone",
        SDFNodeType.Plane => "plane",
        SDFNodeType.HexPrism => "hex",
        SDFNodeType.Capsule => "caps",
        SDFNodeType.VerticalCapsule => "vcaps",
        SDFNodeType.CappedCylinder => "cyl",
        SDFNodeType.ArbitraryCappedCylinder => "acyl",
        SDFNodeType.RoundedCylinder => "rcyl",
        SDFNodeType.CappedCone => "ccone",
        SDFNodeType.ArbitraryCappedCone => "acone",
        SDFNodeType.SolidAngle => "angle",
        SDFNodeType.CutSphere => "cutsph",
        SDFNodeType.CutHollowSphere => "hollow",
        SDFNodeType.RoundCone => "rcone",
        SDFNodeType.ArbitraryRoundCone => "arcone",
        SDFNodeType.VesicaSegment => "vesica",
        SDFNodeType.Rhombus => "rhomb",
        SDFNodeType.Octahedron => "octa",
        SDFNodeType.OctahedronBound => "octa≈",
        SDFNodeType.Pyramid => "pyr",
        SDFNodeType.Triangle => "tri",
        SDFNodeType.Quad => "quad",
        SDFNodeType.Ellipsoid => "ellips",
        SDFNodeType.TriPrism => "triprism",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
