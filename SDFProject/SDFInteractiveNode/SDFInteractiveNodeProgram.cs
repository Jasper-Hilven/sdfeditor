using SDFProject.ProgramDefinition;

namespace SDFProject.SDFInteractiveNode;

public record SdfInteractiveNodeProgram(
    SDFProgram Program,
    string? SelectedNodeId,
    int StrZoomLevel);
    