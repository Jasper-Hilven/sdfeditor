namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public record SDFPartialCommandNodeProgram(
    SdfInteractiveNodeProgram interactiveProgram,
    SDFPartialCommandNodeProgramState state);

public record SDFPartialCommandNodeProgramState(SDFPartialCommandNodeProgramMode Mode, 
    string? EditedParameter = null, string Text = "");

public enum SDFPartialCommandNodeProgramMode
{
    Home,
    Scale,
    Rotate,
    Translate,
    AddNode,
    AddNodeMore,
    ReplaceNode,
    ReplaceNodeMore,
    InlineVariable,
    ExtractVariable,
    EditNode,
    EditParameter,
    TypeParameterValue,
}