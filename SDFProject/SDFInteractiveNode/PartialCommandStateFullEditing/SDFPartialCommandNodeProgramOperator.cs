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

        return action switch
        {
            SDFPartialActionEnum.SelectNext => Apply(partialCommandProgram, new SelectNext()),
            SDFPartialActionEnum.SelectPrevious => Apply(partialCommandProgram, new SelectPrevious()),
            SDFPartialActionEnum.SelectParent => Apply(partialCommandProgram, new SelectParent()),
            SDFPartialActionEnum.SelectFirstChild => Apply(partialCommandProgram, new SelectFirstChild()),
            SDFPartialActionEnum.SwitchToHomeMode => SwitchMode(partialCommandProgram, Home),
            SDFPartialActionEnum.SwitchToScaleRotateTranslateMode => SwitchMode(partialCommandProgram, ScaleRotateTranslate),
            SDFPartialActionEnum.TranslateXDown => Translate(partialCommandProgram, Dimension.X, false),
            SDFPartialActionEnum.TranslateXUp => Translate(partialCommandProgram, Dimension.X, true),
            SDFPartialActionEnum.TranslateYDown => Translate(partialCommandProgram, Dimension.Y, false),
            SDFPartialActionEnum.TranslateYUp => Translate(partialCommandProgram, Dimension.Y, true),
            SDFPartialActionEnum.TranslateZDown => Translate(partialCommandProgram, Dimension.Z, false),
            SDFPartialActionEnum.TranslateZUp => Translate(partialCommandProgram, Dimension.Z, true),
            SDFPartialActionEnum.RotateXDown => Rotate(partialCommandProgram, Dimension.X, false),
            SDFPartialActionEnum.RotateXUp => Rotate(partialCommandProgram, Dimension.X, true),
            SDFPartialActionEnum.RotateYDown => Rotate(partialCommandProgram, Dimension.Y, false),
            SDFPartialActionEnum.RotateYUp => Rotate(partialCommandProgram, Dimension.Y, true),
            SDFPartialActionEnum.RotateZDown => Rotate(partialCommandProgram, Dimension.Z, false),
            SDFPartialActionEnum.RotateZUp => Rotate(partialCommandProgram, Dimension.Z, true),
            SDFPartialActionEnum.DecreaseZoomLevel => Apply(partialCommandProgram, new DecreaseStrZoomLevel()),
            SDFPartialActionEnum.IncreaseZoomLevel => Apply(partialCommandProgram, new IncreaseStrZoomLevel()),
            SDFPartialActionEnum.ScaleDown => Apply(partialCommandProgram, new ScaleDownAtCurrentZoomLevel()),
            SDFPartialActionEnum.ScaleUp => Apply(partialCommandProgram, new ScaleUpAtCurrentZoomLevel()),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

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

    public static SDFPartialCommandNodeProgram Translate(SDFPartialCommandNodeProgram program,
        Dimension dimension, bool positiveDirection)
    {
        ISdfInteractiveNodeProgramOperation command = (dimension, positiveDirection) switch
        {
            (Dimension.X, true) => new TranslateXUpAtCurrentZoomLevel(),
            (Dimension.X, false) => new TranslateXDownAtCurrentZoomLevel(),
            (Dimension.Y, true) => new TranslateYUpAtCurrentZoomLevel(),
            (Dimension.Y, false) => new TranslateYDownAtCurrentZoomLevel(),
            (Dimension.Z, true) => new TranslateZUpAtCurrentZoomLevel(),
            (Dimension.Z, false) => new TranslateZDownAtCurrentZoomLevel(),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
        };
        return Apply(program, command);
    }

    public static SDFPartialCommandNodeProgram Rotate(SDFPartialCommandNodeProgram program,
        Dimension dimension, bool positiveDirection)
    {
        ISdfInteractiveNodeProgramOperation command = (dimension, positiveDirection) switch
        {
            (Dimension.X, true) => new RotateXUpAtCurrentZoomLevel(),
            (Dimension.X, false) => new RotateXDownAtCurrentZoomLevel(),
            (Dimension.Y, true) => new RotateYUpAtCurrentZoomLevel(),
            (Dimension.Y, false) => new RotateYDownAtCurrentZoomLevel(),
            (Dimension.Z, true) => new RotateZUpAtCurrentZoomLevel(),
            (Dimension.Z, false) => new RotateZDownAtCurrentZoomLevel(),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
        };
        return Apply(program, command);
    }
}

public enum Dimension
{
    X, Y, Z
}
