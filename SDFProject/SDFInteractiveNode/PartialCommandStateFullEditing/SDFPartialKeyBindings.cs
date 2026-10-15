using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

// Which action each finger key does in each mode; SDFPartialCommandNodeProgramOperator carries the actions out
public static class SDFPartialKeyBindings
{
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

        return bindings;
    }
}

public enum SDFPartialActionEnum
{
    SelectNext,
    SelectPrevious,
    SelectParent,
    SelectFirstChild,
    SwitchToHomeMode,
    SwitchToScaleRotateTranslateMode,
    TranslateXDown,
    TranslateXUp,
    TranslateYDown,
    TranslateYUp,
    TranslateZDown,
    TranslateZUp,
    RotateXDown,
    RotateXUp,
    RotateYDown,
    RotateYUp,
    RotateZDown,
    RotateZUp,
    DecreaseZoomLevel,
    IncreaseZoomLevel,
    ScaleDown,
    ScaleUp
}
