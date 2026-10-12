using SDFProject.SDFInteractiveNode.InteractiveCommands;
using static SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing.SDFPartialCommandNodeProgramMode;

namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public class SDFPartialCommandNodeProgramOperator
{
    public static SDFPartialCommandNodeProgram Operate(SDFPartialCommandNodeProgram partialCommandProgram,
        InputKey inputKey)
    {
        var ret = (partialCommandProgram.state.Mode, inputKey) switch
        {
            (Home, InputKey.RMiddleHome) =>
                Apply(partialCommandProgram, new SelectNext()),
            (Home, InputKey.RMiddleUp) =>
                Apply(partialCommandProgram, new SelectPrevious()),
            (Home, InputKey.RIndexHome) =>
                Apply(partialCommandProgram, new SelectParent()),
            (Home, InputKey.RRingHome) =>
                Apply(partialCommandProgram, new SelectFirstChild()),
            (_, InputKey.LPinkHome) =>
                SwitchMode(partialCommandProgram, Home),
            (Home, InputKey.LRingHome) =>
                SwitchMode(partialCommandProgram, ScaleRotateTranslate),
            (ScaleRotateTranslate, InputKey.LMiddleHome) => Translate(partialCommandProgram, Dimension.X, false),
            (ScaleRotateTranslate, InputKey.LIndexHome) => Translate(partialCommandProgram, Dimension.X, true),
            (ScaleRotateTranslate, InputKey.RIndexHome) => Translate(partialCommandProgram, Dimension.Y, false),
            (ScaleRotateTranslate, InputKey.RMiddleHome) => Translate(partialCommandProgram, Dimension.Y, true),
            (ScaleRotateTranslate, InputKey.RRingHome) => Translate(partialCommandProgram, Dimension.Z, false),
            (ScaleRotateTranslate, InputKey.RPinkHome) => Translate(partialCommandProgram, Dimension.Z, true),
            (ScaleRotateTranslate, InputKey.LMiddleUp) => Rotate(partialCommandProgram, Dimension.X, false),
            (ScaleRotateTranslate, InputKey.LIndexUp) => Rotate(partialCommandProgram, Dimension.X, true),
            (ScaleRotateTranslate, InputKey.RIndexUp) => Rotate(partialCommandProgram, Dimension.Y, false),
            (ScaleRotateTranslate, InputKey.RMiddleUp) => Rotate(partialCommandProgram, Dimension.Y, true),
            (ScaleRotateTranslate, InputKey.RRingUp) => Rotate(partialCommandProgram, Dimension.Z, false),
            (ScaleRotateTranslate, InputKey.RPinkUp) => Rotate(partialCommandProgram, Dimension.Z, true),
            (ScaleRotateTranslate, InputKey.LMiddleDown) => Apply(partialCommandProgram, new DecreaseStrZoomLevel()),
            (ScaleRotateTranslate, InputKey.LIndexDown) => Apply(partialCommandProgram, new IncreaseStrZoomLevel()),
            (ScaleRotateTranslate, InputKey.RIndexDown) => Apply(partialCommandProgram, new ScaleDownAtCurrentZoomLevel()),
            (ScaleRotateTranslate, InputKey.RMiddleDown) => Apply(partialCommandProgram, new ScaleUpAtCurrentZoomLevel()),
            _=> partialCommandProgram
        };

        return ret;
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