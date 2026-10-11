using SDFProject.Definitions;

namespace SDFProject.SDFInteractiveNode;

public record SDFInteractiveNodeProgram(
    SDFProgram Program,
    string? SelectedNodeId,
    int StrZoomLevel);
    