using SDFProject.ProgramDefinition;
namespace SDFProject.SDFInteractiveNode.PartialCommandStateFullEditing;

public record SDFPartialCommandNodeProgram(
    SdfInteractiveNodeProgram interactiveProgram,
    SDFPartialCommandNodeProgramState state)
{
    /// <summary>The materials Material nodes can use; editing them is part of the program, so undo, redo and replay cover it.</summary>
    public MaterialLibrary Materials { get; init; } = MaterialLibrary.Empty;
}

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
    Material,
    /// <summary>Every material of the library on a key, to give the selected node.</summary>
    Materials,
    /// <summary>The parameters of the selected node's material on keys.</summary>
    EditMaterial,
    EditMaterialParameter,
    TypeMaterialValue,
}