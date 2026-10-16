using V3 = SDFProject.ProgramDefinition.V3;
using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public static class SDFPartialKeyBindings
{
    private static readonly ISDFPartialAction[] FirstPageNodeTypes =
    [
        new AddSphere(),
        new AddBox(),
        new AddRoundBox(),
        new AddTorus(),
        new AddBoxFrame(),
        new AddCappedTorus(),
        new AddLink(),
        new AddInfiniteCylinder(),
        new AddCone(),
        new AddInfiniteCone(),
        new AddPlane(),
        new AddHexPrism(),
        new AddCapsule(),
        new AddVerticalCapsule(),
        new AddCappedCylinder(),
        new AddRoundedCylinder(),
        new AddCappedCone(),
        new AddSolidAngle(),
        new AddCutSphere(),
        new AddCutHollowSphere(),
        new AddRoundCone(),
        new AddRhombus(),
        new AddOctahedron(),
        new AddPyramid(),
        new AddTriangle(),
        new AddQuad(),
    ];

    private static readonly ISDFPartialAction[] MoreNodeTypes =
    [
        new AddEllipsoid(),
        new AddTriPrism(),
        new AddArbitraryCappedCylinder(),
        new AddArbitraryCappedCone(),
        new AddArbitraryRoundCone(),
        new AddVesicaSegment(),
        new AddOctahedronBound(),
    ];

    private static readonly ISDFPartialAction[] FirstPageReplaceNodeTypes =
    [
        new ReplaceWithSphere(),
        new ReplaceWithBox(),
        new ReplaceWithRoundBox(),
        new ReplaceWithTorus(),
        new ReplaceWithBoxFrame(),
        new ReplaceWithCappedTorus(),
        new ReplaceWithLink(),
        new ReplaceWithInfiniteCylinder(),
        new ReplaceWithCone(),
        new ReplaceWithInfiniteCone(),
        new ReplaceWithPlane(),
        new ReplaceWithHexPrism(),
        new ReplaceWithCapsule(),
        new ReplaceWithVerticalCapsule(),
        new ReplaceWithCappedCylinder(),
        new ReplaceWithRoundedCylinder(),
        new ReplaceWithCappedCone(),
        new ReplaceWithSolidAngle(),
        new ReplaceWithCutSphere(),
        new ReplaceWithCutHollowSphere(),
        new ReplaceWithRoundCone(),
        new ReplaceWithRhombus(),
        new ReplaceWithOctahedron(),
        new ReplaceWithPyramid(),
        new ReplaceWithTriangle(),
        new ReplaceWithQuad(),
    ];

    private static readonly ISDFPartialAction[] MoreReplaceNodeTypes =
    [
        new ReplaceWithEllipsoid(),
        new ReplaceWithTriPrism(),
        new ReplaceWithArbitraryCappedCylinder(),
        new ReplaceWithArbitraryCappedCone(),
        new ReplaceWithArbitraryRoundCone(),
        new ReplaceWithVesicaSegment(),
        new ReplaceWithOctahedronBound(),
    ];

    public static readonly IReadOnlyList<PaintColor> Palette =
    [
        new("red", new V3(0.85f, 0.2f, 0.2f)),
        new("orange", new V3(0.95f, 0.55f, 0.15f)),
        new("yellow", new V3(0.95f, 0.85f, 0.25f)),
        new("lime", new V3(0.6f, 0.85f, 0.25f)),
        new("green", new V3(0.25f, 0.65f, 0.3f)),
        new("teal", new V3(0.2f, 0.65f, 0.65f)),
        new("blue", new V3(0.25f, 0.45f, 0.85f)),
        new("purple", new V3(0.55f, 0.35f, 0.8f)),
        new("pink", new V3(0.9f, 0.45f, 0.7f)),
        new("brown", new V3(0.5f, 0.33f, 0.2f)),
        new("tan", new V3(0.85f, 0.62f, 0.42f)),
        new("white", new V3(0.92f, 0.92f, 0.92f)),
        new("grey", new V3(0.5f, 0.5f, 0.5f)),
        new("black", new V3(0.1f, 0.1f, 0.1f)),
    ];

    public static readonly Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), ISDFPartialAction> Bindings =
        BuildKeyBindings();

    private static Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), ISDFPartialAction> BuildKeyBindings()
    {
        var bindings = new Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), ISDFPartialAction>
        {
            [(Home, InputKey.RMiddleHome)] = new SelectNext(),
            [(Home, InputKey.RMiddleUp)] = new SelectPrevious(),
            [(Home, InputKey.RIndexHome)] = new SelectParent(),
            [(Home, InputKey.RRingHome)] = new SelectFirstChild(),
            [(Home, InputKey.RMiddleDown)] = new SwitchToScaleMode(),
            [(Home, InputKey.RRingDown)] = new SwitchToRotateMode(),
            [(Home, InputKey.RPinkDown)] = new SwitchToTranslateMode(),
            [(Home, InputKey.LMiddleHome)] = new ToggleSmoothness(),
            [(Home, InputKey.LIndexHome)] = new AddNode(),
            [(Home, InputKey.LMiddleUp)] = new EmptyNode(),
            [(Home, InputKey.LIndexUp)] = new ReplaceNode(),
            [(Home, InputKey.LIndexDown)] = new EditNode(),
            [(Home, InputKey.LIndex2Home)] = new SubtractNode(),
            [(Home, InputKey.RIndex2Home)] = new IntersectNode(),
            [(Home, InputKey.LIndex2Down)] = new DistortNode(),
            [(Home, InputKey.LIndex2Up)] = new DissolveNode(),
            [(Home, InputKey.RIndexUp)] = new ExtractVariable(),
            [(Home, InputKey.RIndex2Up)] = new InlineVariable(),
            [(Home, InputKey.LMiddleDown)] = new SwitchToMaterialMode(),
            [(SDFPartialCommandNodeProgramMode.Material, InputKey.RMiddleHome)] = new SelectNext(),
            [(SDFPartialCommandNodeProgramMode.Material, InputKey.RMiddleUp)] = new SelectPrevious(),
            [(SDFPartialCommandNodeProgramMode.Material, InputKey.RIndexHome)] = new SelectParent(),
            [(SDFPartialCommandNodeProgramMode.Material, InputKey.RRingHome)] = new SelectFirstChild(),
            [(SDFPartialCommandNodeProgramMode.Material, InputKey.LIndexDown)] = new EditColor(),
            [(SDFPartialCommandNodeProgramMode.Material, InputKey.LIndex2Up)] = new RemoveColor(),
            [(SDFPartialCommandNodeProgramMode.EditNode, InputKey.LRingHome)] = new EditParameter1(),
            [(SDFPartialCommandNodeProgramMode.EditNode, InputKey.LMiddleHome)] = new EditParameter2(),
            [(SDFPartialCommandNodeProgramMode.EditNode, InputKey.LIndexHome)] = new EditParameter3(),
            [(SDFPartialCommandNodeProgramMode.EditNode, InputKey.LIndex2Home)] = new EditParameter4(),
            [(EditParameter, InputKey.LMiddleHome)] = new ParameterXDown(),
            [(EditParameter, InputKey.LIndexHome)] = new ParameterXUp(),
            [(EditParameter, InputKey.RIndexHome)] = new ParameterYDown(),
            [(EditParameter, InputKey.RMiddleHome)] = new ParameterYUp(),
            [(EditParameter, InputKey.RRingHome)] = new ParameterZDown(),
            [(EditParameter, InputKey.RPinkHome)] = new ParameterZUp(),
            [(EditParameter, InputKey.LMiddleDown)] = new DecreaseZoomLevel(),
            [(EditParameter, InputKey.LIndexDown)] = new IncreaseZoomLevel(),
            [(EditParameter, InputKey.LRingHome)] = new TypeParameterValue(),
            [(Translate, InputKey.LMiddleHome)] = new TranslateXDown(),
            [(Translate, InputKey.LIndexHome)] = new TranslateXUp(),
            [(Translate, InputKey.RIndexHome)] = new TranslateYDown(),
            [(Translate, InputKey.RMiddleHome)] = new TranslateYUp(),
            [(Translate, InputKey.RRingHome)] = new TranslateZDown(),
            [(Translate, InputKey.RPinkHome)] = new TranslateZUp(),
            [(Rotate, InputKey.LMiddleHome)] = new RotateXDown(),
            [(Rotate, InputKey.LIndexHome)] = new RotateXUp(),
            [(Rotate, InputKey.RIndexHome)] = new RotateYDown(),
            [(Rotate, InputKey.RMiddleHome)] = new RotateYUp(),
            [(Rotate, InputKey.RRingHome)] = new RotateZDown(),
            [(Rotate, InputKey.RPinkHome)] = new RotateZUp(),
            [(Scale, InputKey.LMiddleHome)] = new ScaleXDown(),
            [(Scale, InputKey.LIndexHome)] = new ScaleXUp(),
            [(Scale, InputKey.RIndexHome)] = new ScaleYDown(),
            [(Scale, InputKey.RMiddleHome)] = new ScaleYUp(),
            [(Scale, InputKey.RRingHome)] = new ScaleZDown(),
            [(Scale, InputKey.RPinkHome)] = new ScaleZUp(),
            [(Scale, InputKey.RIndexUp)] = new ScaleDown(),
            [(Scale, InputKey.RMiddleUp)] = new ScaleUp(),
        };

        foreach (var mode in Enum.GetValues<SDFPartialCommandNodeProgramMode>())
            bindings[(mode, InputKey.LPinkHome)] = new SwitchToHomeMode();

        foreach (var mode in new[] { Home, Scale, Rotate, Translate, SDFPartialCommandNodeProgramMode.EditNode, EditParameter, SDFPartialCommandNodeProgramMode.Material })
            bindings[(mode, InputKey.LPinkDown)] = new Undo();

        foreach (var mode in Enum.GetValues<SDFPartialCommandNodeProgramMode>().Where(m => !SDFPartialCommandNodeProgramOperator.TakesText(m)))
            bindings[(mode, InputKey.LRingDown)] = new Redo();

        foreach (var mode in new[] { Scale, Rotate, Translate })
        {
            bindings[(mode, InputKey.LMiddleDown)] = new DecreaseZoomLevel();
            bindings[(mode, InputKey.LIndexDown)] = new IncreaseZoomLevel();
        }

        bindings[(EditParameter, InputKey.LPinkHome)] = new StopEditingParameter();

        foreach (var mode in Enum.GetValues<SDFPartialCommandNodeProgramMode>().Where(SDFPartialCommandNodeProgramOperator.TakesText))
        {
            bindings[(mode, InputKey.Enter)] = new ConfirmText();
            bindings[(mode, InputKey.LPinkHome)] = new CancelText();
        }

        bindings[(SDFPartialCommandNodeProgramMode.AddNode, InputKey.LPinkHome)] = new CancelAddNode();
        bindings[(AddNodeMore, InputKey.LPinkHome)] = new CancelAddNode();
        bindings[(SDFPartialCommandNodeProgramMode.AddNode, InputKey.LPinkDown)] = new Undo();
        bindings[(AddNodeMore, InputKey.LPinkDown)] = new Undo();
        bindings[(SDFPartialCommandNodeProgramMode.AddNode, InputKey.RPinkDown)] = new ShowMoreNodeTypes();
        bindings[(AddNodeMore, InputKey.RPinkDown)] = new ShowFirstNodeTypes();
        AddInFreeKeys(bindings, SDFPartialCommandNodeProgramMode.AddNode, FirstPageNodeTypes);
        AddInFreeKeys(bindings, AddNodeMore, MoreNodeTypes);

        bindings[(SDFPartialCommandNodeProgramMode.ReplaceNode, InputKey.LPinkHome)] = new CancelReplaceNode();
        bindings[(ReplaceNodeMore, InputKey.LPinkHome)] = new CancelReplaceNode();
        bindings[(SDFPartialCommandNodeProgramMode.ReplaceNode, InputKey.LPinkDown)] = new Undo();
        bindings[(ReplaceNodeMore, InputKey.LPinkDown)] = new Undo();
        bindings[(SDFPartialCommandNodeProgramMode.ReplaceNode, InputKey.RPinkDown)] = new ShowMoreReplaceNodeTypes();
        bindings[(ReplaceNodeMore, InputKey.RPinkDown)] = new ShowFirstReplaceNodeTypes();
        AddInFreeKeys(bindings, SDFPartialCommandNodeProgramMode.ReplaceNode, FirstPageReplaceNodeTypes);
        AddInFreeKeys(bindings, ReplaceNodeMore, MoreReplaceNodeTypes);

        AddInFreeKeys(bindings, SDFPartialCommandNodeProgramMode.Material, Palette.ToArray<ISDFPartialAction>());

        return bindings;
    }

    public static readonly IReadOnlyList<InputKey> RedoChoiceKeys = FingerKeyLayouts.KeyColumns.Skip(1).Select(c => c.Home)
        .Concat(FingerKeyLayouts.KeyColumns.Select(c => c.Up))
        .Concat(FingerKeyLayouts.KeyColumns.Select(c => c.Down))
        .ToArray();

    private static void AddInFreeKeys(Dictionary<(SDFPartialCommandNodeProgramMode Mode, InputKey Key), ISDFPartialAction> bindings,
        SDFPartialCommandNodeProgramMode mode, ISDFPartialAction[] actions)
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