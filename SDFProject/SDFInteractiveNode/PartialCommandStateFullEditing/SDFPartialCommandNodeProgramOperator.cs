using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode.InteractiveCommands;
using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public class SDFPartialCommandNodeProgramOperator
{
    public static SDFPartialCommandNodeProgram Operate(SDFPartialCommandNodeProgram partialCommandProgram,
        InputKey inputKey)
    {
        // A key without a binding in the current mode does nothing
        if (!SDFPartialKeyBindings.Bindings.TryGetValue((partialCommandProgram.state.Mode, inputKey), out var action))
            return partialCommandProgram;

        var result = TargetMode(action) is { } mode
            ? SwitchMode(partialCommandProgram, mode)
            : Apply(partialCommandProgram, InteractiveOperation(action));

        // Adding or replacing a node finishes the substate that picks the node type
        return NodeTypeToAdd(action) is not null || NodeTypeToReplaceWith(action) is not null ? SwitchMode(result, Home) : result;
    }

    // The mode an action switches to, or null when the action operates on the program instead
    public static SDFPartialCommandNodeProgramMode? TargetMode(SDFPartialActionEnum action) => action switch
    {
        SDFPartialActionEnum.SwitchToHomeMode => Home,
        SDFPartialActionEnum.SwitchToScaleRotateTranslateMode => ScaleRotateTranslate,
        SDFPartialActionEnum.AddNode => AddNode,
        SDFPartialActionEnum.CancelAddNode => Home,
        SDFPartialActionEnum.ShowMoreNodeTypes => AddNodeMore,
        SDFPartialActionEnum.ShowFirstNodeTypes => AddNode,
        SDFPartialActionEnum.ReplaceNode => ReplaceNode,
        SDFPartialActionEnum.CancelReplaceNode => Home,
        SDFPartialActionEnum.ShowMoreReplaceNodeTypes => ReplaceNodeMore,
        SDFPartialActionEnum.ShowFirstReplaceNodeTypes => ReplaceNode,
        _ => null,
    };

    public static SDFNodeType? NodeTypeToAdd(SDFPartialActionEnum action) => action switch
    {
        SDFPartialActionEnum.AddSphere => SDFNodeType.Sphere,
        SDFPartialActionEnum.AddBox => SDFNodeType.Box,
        SDFPartialActionEnum.AddRoundBox => SDFNodeType.RoundBox,
        SDFPartialActionEnum.AddTorus => SDFNodeType.Torus,
        SDFPartialActionEnum.AddBoxFrame => SDFNodeType.BoxFrame,
        SDFPartialActionEnum.AddCappedTorus => SDFNodeType.CappedTorus,
        SDFPartialActionEnum.AddLink => SDFNodeType.Link,
        SDFPartialActionEnum.AddInfiniteCylinder => SDFNodeType.InfiniteCylinder,
        SDFPartialActionEnum.AddCone => SDFNodeType.Cone,
        SDFPartialActionEnum.AddInfiniteCone => SDFNodeType.InfiniteCone,
        SDFPartialActionEnum.AddPlane => SDFNodeType.Plane,
        SDFPartialActionEnum.AddHexPrism => SDFNodeType.HexPrism,
        SDFPartialActionEnum.AddCapsule => SDFNodeType.Capsule,
        SDFPartialActionEnum.AddVerticalCapsule => SDFNodeType.VerticalCapsule,
        SDFPartialActionEnum.AddCappedCylinder => SDFNodeType.CappedCylinder,
        SDFPartialActionEnum.AddArbitraryCappedCylinder => SDFNodeType.ArbitraryCappedCylinder,
        SDFPartialActionEnum.AddRoundedCylinder => SDFNodeType.RoundedCylinder,
        SDFPartialActionEnum.AddCappedCone => SDFNodeType.CappedCone,
        SDFPartialActionEnum.AddArbitraryCappedCone => SDFNodeType.ArbitraryCappedCone,
        SDFPartialActionEnum.AddSolidAngle => SDFNodeType.SolidAngle,
        SDFPartialActionEnum.AddCutSphere => SDFNodeType.CutSphere,
        SDFPartialActionEnum.AddCutHollowSphere => SDFNodeType.CutHollowSphere,
        SDFPartialActionEnum.AddDeathStar => SDFNodeType.DeathStar,
        SDFPartialActionEnum.AddRoundCone => SDFNodeType.RoundCone,
        SDFPartialActionEnum.AddArbitraryRoundCone => SDFNodeType.ArbitraryRoundCone,
        SDFPartialActionEnum.AddVesicaSegment => SDFNodeType.VesicaSegment,
        SDFPartialActionEnum.AddRhombus => SDFNodeType.Rhombus,
        SDFPartialActionEnum.AddOctahedron => SDFNodeType.Octahedron,
        SDFPartialActionEnum.AddOctahedronBound => SDFNodeType.OctahedronBound,
        SDFPartialActionEnum.AddPyramid => SDFNodeType.Pyramid,
        SDFPartialActionEnum.AddTriangle => SDFNodeType.Triangle,
        SDFPartialActionEnum.AddQuad => SDFNodeType.Quad,
        SDFPartialActionEnum.AddEllipsoid => SDFNodeType.Ellipsoid,
        SDFPartialActionEnum.AddTriPrism => SDFNodeType.TriPrism,
        _ => null,
    };

    public static SDFNodeType? NodeTypeToReplaceWith(SDFPartialActionEnum action) => action switch
    {
        SDFPartialActionEnum.ReplaceWithSphere => SDFNodeType.Sphere,
        SDFPartialActionEnum.ReplaceWithBox => SDFNodeType.Box,
        SDFPartialActionEnum.ReplaceWithRoundBox => SDFNodeType.RoundBox,
        SDFPartialActionEnum.ReplaceWithTorus => SDFNodeType.Torus,
        SDFPartialActionEnum.ReplaceWithBoxFrame => SDFNodeType.BoxFrame,
        SDFPartialActionEnum.ReplaceWithCappedTorus => SDFNodeType.CappedTorus,
        SDFPartialActionEnum.ReplaceWithLink => SDFNodeType.Link,
        SDFPartialActionEnum.ReplaceWithInfiniteCylinder => SDFNodeType.InfiniteCylinder,
        SDFPartialActionEnum.ReplaceWithCone => SDFNodeType.Cone,
        SDFPartialActionEnum.ReplaceWithInfiniteCone => SDFNodeType.InfiniteCone,
        SDFPartialActionEnum.ReplaceWithPlane => SDFNodeType.Plane,
        SDFPartialActionEnum.ReplaceWithHexPrism => SDFNodeType.HexPrism,
        SDFPartialActionEnum.ReplaceWithCapsule => SDFNodeType.Capsule,
        SDFPartialActionEnum.ReplaceWithVerticalCapsule => SDFNodeType.VerticalCapsule,
        SDFPartialActionEnum.ReplaceWithCappedCylinder => SDFNodeType.CappedCylinder,
        SDFPartialActionEnum.ReplaceWithArbitraryCappedCylinder => SDFNodeType.ArbitraryCappedCylinder,
        SDFPartialActionEnum.ReplaceWithRoundedCylinder => SDFNodeType.RoundedCylinder,
        SDFPartialActionEnum.ReplaceWithCappedCone => SDFNodeType.CappedCone,
        SDFPartialActionEnum.ReplaceWithArbitraryCappedCone => SDFNodeType.ArbitraryCappedCone,
        SDFPartialActionEnum.ReplaceWithSolidAngle => SDFNodeType.SolidAngle,
        SDFPartialActionEnum.ReplaceWithCutSphere => SDFNodeType.CutSphere,
        SDFPartialActionEnum.ReplaceWithCutHollowSphere => SDFNodeType.CutHollowSphere,
        SDFPartialActionEnum.ReplaceWithDeathStar => SDFNodeType.DeathStar,
        SDFPartialActionEnum.ReplaceWithRoundCone => SDFNodeType.RoundCone,
        SDFPartialActionEnum.ReplaceWithArbitraryRoundCone => SDFNodeType.ArbitraryRoundCone,
        SDFPartialActionEnum.ReplaceWithVesicaSegment => SDFNodeType.VesicaSegment,
        SDFPartialActionEnum.ReplaceWithRhombus => SDFNodeType.Rhombus,
        SDFPartialActionEnum.ReplaceWithOctahedron => SDFNodeType.Octahedron,
        SDFPartialActionEnum.ReplaceWithOctahedronBound => SDFNodeType.OctahedronBound,
        SDFPartialActionEnum.ReplaceWithPyramid => SDFNodeType.Pyramid,
        SDFPartialActionEnum.ReplaceWithTriangle => SDFNodeType.Triangle,
        SDFPartialActionEnum.ReplaceWithQuad => SDFNodeType.Quad,
        SDFPartialActionEnum.ReplaceWithEllipsoid => SDFNodeType.Ellipsoid,
        SDFPartialActionEnum.ReplaceWithTriPrism => SDFNodeType.TriPrism,
        _ => null,
    };

    public static ISdfInteractiveNodeProgramOperation InteractiveOperation(SDFPartialActionEnum action) => action switch
    {
        _ when NodeTypeToAdd(action) is { } type => new InteractiveAddNodeNextTo(type),
        _ when NodeTypeToReplaceWith(action) is { } type => new InteractiveReplaceNode(type),
        SDFPartialActionEnum.SelectNext => new SelectNext(),
        SDFPartialActionEnum.SelectPrevious => new SelectPrevious(),
        SDFPartialActionEnum.SelectParent => new SelectParent(),
        SDFPartialActionEnum.SelectFirstChild => new SelectFirstChild(),
        SDFPartialActionEnum.TranslateXDown => new TranslateXDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.TranslateXUp => new TranslateXUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.TranslateYDown => new TranslateYDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.TranslateYUp => new TranslateYUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.TranslateZDown => new TranslateZDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.TranslateZUp => new TranslateZUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.RotateXDown => new RotateXDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.RotateXUp => new RotateXUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.RotateYDown => new RotateYDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.RotateYUp => new RotateYUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.RotateZDown => new RotateZDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.RotateZUp => new RotateZUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.DecreaseZoomLevel => new DecreaseStrZoomLevel(),
        SDFPartialActionEnum.IncreaseZoomLevel => new IncreaseStrZoomLevel(),
        SDFPartialActionEnum.ScaleDown => new ScaleDownAtCurrentZoomLevel(),
        SDFPartialActionEnum.ScaleUp => new ScaleUpAtCurrentZoomLevel(),
        SDFPartialActionEnum.ToggleSmoothness => new ToggleSmoothness(),
        SDFPartialActionEnum.EmptyNode => new InteractiveEmptyNode(),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static SDFPartialCommandNodeProgram SwitchMode(SDFPartialCommandNodeProgram program, SDFPartialCommandNodeProgramMode mode)
    {
        return program with
        {
            state = program.state with { Mode = mode }
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
