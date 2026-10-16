using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

// Which action each finger key does in each mode; SDFPartialCommandNodeProgramOperator carries the actions out
public static class SDFPartialKeyBindings
{
    // Declared before Bindings since static fields initialize in order
    private static readonly SDFPartialActionEnum[] FirstPageNodeTypes =
    [
        SDFPartialActionEnum.AddSphere,
        SDFPartialActionEnum.AddBox,
        SDFPartialActionEnum.AddRoundBox,
        SDFPartialActionEnum.AddTorus,
        SDFPartialActionEnum.AddBoxFrame,
        SDFPartialActionEnum.AddCappedTorus,
        SDFPartialActionEnum.AddLink,
        SDFPartialActionEnum.AddInfiniteCylinder,
        SDFPartialActionEnum.AddCone,
        SDFPartialActionEnum.AddPlane,
        SDFPartialActionEnum.AddHexPrism,
        SDFPartialActionEnum.AddCapsule,
        SDFPartialActionEnum.AddVerticalCapsule,
        SDFPartialActionEnum.AddCappedCylinder,
        SDFPartialActionEnum.AddRoundedCylinder,
        SDFPartialActionEnum.AddCappedCone,
        SDFPartialActionEnum.AddSolidAngle,
        SDFPartialActionEnum.AddCutSphere,
        SDFPartialActionEnum.AddCutHollowSphere,
        SDFPartialActionEnum.AddDeathStar,
        SDFPartialActionEnum.AddRoundCone,
        SDFPartialActionEnum.AddRhombus,
        SDFPartialActionEnum.AddOctahedron,
        SDFPartialActionEnum.AddPyramid,
        SDFPartialActionEnum.AddTriangle,
        SDFPartialActionEnum.AddQuad,
        SDFPartialActionEnum.AddEllipsoid,
        SDFPartialActionEnum.AddTriPrism,
    ];

    private static readonly SDFPartialActionEnum[] MoreNodeTypes =
    [
        SDFPartialActionEnum.AddInfiniteCone,
        SDFPartialActionEnum.AddArbitraryCappedCylinder,
        SDFPartialActionEnum.AddArbitraryCappedCone,
        SDFPartialActionEnum.AddArbitraryRoundCone,
        SDFPartialActionEnum.AddVesicaSegment,
        SDFPartialActionEnum.AddOctahedronBound,
    ];

    private static readonly SDFPartialActionEnum[] FirstPageReplaceNodeTypes =
    [
        SDFPartialActionEnum.ReplaceWithSphere,
        SDFPartialActionEnum.ReplaceWithBox,
        SDFPartialActionEnum.ReplaceWithRoundBox,
        SDFPartialActionEnum.ReplaceWithTorus,
        SDFPartialActionEnum.ReplaceWithBoxFrame,
        SDFPartialActionEnum.ReplaceWithCappedTorus,
        SDFPartialActionEnum.ReplaceWithLink,
        SDFPartialActionEnum.ReplaceWithInfiniteCylinder,
        SDFPartialActionEnum.ReplaceWithCone,
        SDFPartialActionEnum.ReplaceWithPlane,
        SDFPartialActionEnum.ReplaceWithHexPrism,
        SDFPartialActionEnum.ReplaceWithCapsule,
        SDFPartialActionEnum.ReplaceWithVerticalCapsule,
        SDFPartialActionEnum.ReplaceWithCappedCylinder,
        SDFPartialActionEnum.ReplaceWithRoundedCylinder,
        SDFPartialActionEnum.ReplaceWithCappedCone,
        SDFPartialActionEnum.ReplaceWithSolidAngle,
        SDFPartialActionEnum.ReplaceWithCutSphere,
        SDFPartialActionEnum.ReplaceWithCutHollowSphere,
        SDFPartialActionEnum.ReplaceWithDeathStar,
        SDFPartialActionEnum.ReplaceWithRoundCone,
        SDFPartialActionEnum.ReplaceWithRhombus,
        SDFPartialActionEnum.ReplaceWithOctahedron,
        SDFPartialActionEnum.ReplaceWithPyramid,
        SDFPartialActionEnum.ReplaceWithTriangle,
        SDFPartialActionEnum.ReplaceWithQuad,
        SDFPartialActionEnum.ReplaceWithEllipsoid,
        SDFPartialActionEnum.ReplaceWithTriPrism,
    ];

    private static readonly SDFPartialActionEnum[] MoreReplaceNodeTypes =
    [
        SDFPartialActionEnum.ReplaceWithInfiniteCone,
        SDFPartialActionEnum.ReplaceWithArbitraryCappedCylinder,
        SDFPartialActionEnum.ReplaceWithArbitraryCappedCone,
        SDFPartialActionEnum.ReplaceWithArbitraryRoundCone,
        SDFPartialActionEnum.ReplaceWithVesicaSegment,
        SDFPartialActionEnum.ReplaceWithOctahedronBound,
    ];

    public static readonly Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), SDFPartialActionEnum> Bindings =
        BuildKeyBindings();

    private static Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), SDFPartialActionEnum> BuildKeyBindings()
    {
        var bindings = new Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), SDFPartialActionEnum>
        {
            [(Home, InputKey.RMiddleHome)] = SDFPartialActionEnum.SelectNext,
            [(Home, InputKey.RMiddleUp)] = SDFPartialActionEnum.SelectPrevious,
            [(Home, InputKey.RIndexHome)] = SDFPartialActionEnum.SelectParent,
            [(Home, InputKey.RRingHome)] = SDFPartialActionEnum.SelectFirstChild,
            [(Home, InputKey.LRingHome)] = SDFPartialActionEnum.SwitchToScaleRotateTranslateMode,
            [(Home, InputKey.LMiddleHome)] = SDFPartialActionEnum.ToggleSmoothness,
            [(Home, InputKey.LIndexHome)] = SDFPartialActionEnum.AddNode,
            [(Home, InputKey.LMiddleUp)] = SDFPartialActionEnum.EmptyNode,
            [(Home, InputKey.LIndexUp)] = SDFPartialActionEnum.ReplaceNode,
            [(ScaleRotateTranslate, InputKey.LMiddleHome)] = SDFPartialActionEnum.TranslateXDown,
            [(ScaleRotateTranslate, InputKey.LIndexHome)] = SDFPartialActionEnum.TranslateXUp,
            [(ScaleRotateTranslate, InputKey.RIndexHome)] = SDFPartialActionEnum.TranslateYDown,
            [(ScaleRotateTranslate, InputKey.RMiddleHome)] = SDFPartialActionEnum.TranslateYUp,
            [(ScaleRotateTranslate, InputKey.RRingHome)] = SDFPartialActionEnum.TranslateZDown,
            [(ScaleRotateTranslate, InputKey.RPinkHome)] = SDFPartialActionEnum.TranslateZUp,
            [(ScaleRotateTranslate, InputKey.LMiddleUp)] = SDFPartialActionEnum.RotateXDown,
            [(ScaleRotateTranslate, InputKey.LIndexUp)] = SDFPartialActionEnum.RotateXUp,
            [(ScaleRotateTranslate, InputKey.RIndexUp)] = SDFPartialActionEnum.RotateYDown,
            [(ScaleRotateTranslate, InputKey.RMiddleUp)] = SDFPartialActionEnum.RotateYUp,
            [(ScaleRotateTranslate, InputKey.RRingUp)] = SDFPartialActionEnum.RotateZDown,
            [(ScaleRotateTranslate, InputKey.RPinkUp)] = SDFPartialActionEnum.RotateZUp,
            [(ScaleRotateTranslate, InputKey.LMiddleDown)] = SDFPartialActionEnum.DecreaseZoomLevel,
            [(ScaleRotateTranslate, InputKey.LIndexDown)] = SDFPartialActionEnum.IncreaseZoomLevel,
            [(ScaleRotateTranslate, InputKey.RIndexDown)] = SDFPartialActionEnum.ScaleDown,
            [(ScaleRotateTranslate, InputKey.RMiddleDown)] = SDFPartialActionEnum.ScaleUp,
        };

        // Going back to the home mode works from every mode
        foreach (var mode in Enum.GetValues<SDFPartialCommandNodeProgramMode>())
            bindings[(mode, InputKey.LPinkHome)] = SDFPartialActionEnum.SwitchToHomeMode;

        // The node types to add fill the keys left after cancel and the page switch, over two pages
        bindings[(AddNode, InputKey.LPinkHome)] = SDFPartialActionEnum.CancelAddNode;
        bindings[(AddNodeMore, InputKey.LPinkHome)] = SDFPartialActionEnum.CancelAddNode;
        bindings[(AddNode, InputKey.LPinkDown)] = SDFPartialActionEnum.ShowMoreNodeTypes;
        bindings[(AddNodeMore, InputKey.LPinkDown)] = SDFPartialActionEnum.ShowFirstNodeTypes;
        AddInFreeKeys(bindings, AddNode, FirstPageNodeTypes);
        AddInFreeKeys(bindings, AddNodeMore, MoreNodeTypes);

        // Replacing picks the node type on the same keys as adding
        bindings[(ReplaceNode, InputKey.LPinkHome)] = SDFPartialActionEnum.CancelReplaceNode;
        bindings[(ReplaceNodeMore, InputKey.LPinkHome)] = SDFPartialActionEnum.CancelReplaceNode;
        bindings[(ReplaceNode, InputKey.LPinkDown)] = SDFPartialActionEnum.ShowMoreReplaceNodeTypes;
        bindings[(ReplaceNodeMore, InputKey.LPinkDown)] = SDFPartialActionEnum.ShowFirstReplaceNodeTypes;
        AddInFreeKeys(bindings, ReplaceNode, FirstPageReplaceNodeTypes);
        AddInFreeKeys(bindings, ReplaceNodeMore, MoreReplaceNodeTypes);

        return bindings;
    }

    private static void AddInFreeKeys(Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), SDFPartialActionEnum> bindings,
        SDFPartialCommandNodeProgramMode mode, SDFPartialActionEnum[] actions)
    {
        var freeKeys = Enum.GetValues<InputKey>()
            .Where(key => key != InputKey.Enter && !bindings.ContainsKey((mode, key)))
            .ToArray();
        if (actions.Length > freeKeys.Length)
            throw new InvalidOperationException($"{actions.Length} actions do not fit on the {freeKeys.Length} free keys of {mode}");

        for (var i = 0; i < actions.Length; i++)
            bindings[(mode, freeKeys[i])] = actions[i];
    }
}