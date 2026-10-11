using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFInteractiveNode;

public record SdfInteractiveNodeProgram(
    SDFProgram Program,
    string? SelectedNodeId,
    int StrZoomLevel);

public static class SdfInteractiveNodeProgramConstants
{
    public static SdfInteractiveNodeProgram GetInitialProgram()
    {
        var noPointers = new SDFPointers(new(), new(), new(), new(), 0);
        var cube = SDFNodeDefaultValues.CreateNode(SDFNodeType.Box);
        var program = SDFProgramNormalizePointers.Pointerify(new SDFProgram([], noPointers, cube));
        return new SdfInteractiveNodeProgram(program, PointerTables.PointerId(program.scene), 0);
    }
}
