namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;
//Keys are Blazor's KeyboardEventArgs.Key (the layout-dependent character), so one map per layout.
//Case-insensitive so Shift+letter still resolves; shifted punctuation (e.g. "?" on AZERTY) does not.

public record SDFPartialCommandNodeProgram(
    SdfInteractiveNodeProgram interactiveProgram,
    SDFPartialCommandNodeProgramState state);

public record SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode Mode);



public enum SDFPartialCommandNodeProgramMode
{
    Home,
    ScaleRotateTranslate,
    AddNode,
    // The second page of node types to add, for the ones that do not fit on the keys of AddNode
    AddNodeMore,
    ReplaceNode,
    ReplaceNodeMore,
    InlineVariable,
    ExtractVariable
}