using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplySetNodeParameter
{
    public static SDFProgram DoApplySetNodeParameter(SDFProgram program, SetNodeParameter operation)
    {
        var tables = new PointerTables(program.pointers);
        tables.Set(operation.nodeId, NodeParameters.With(tables.Get(operation.nodeId), operation.parameter, operation.components));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanSet(SDFProgram program, string nodeId, string parameter)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && NodeParameters.Find(tables.Get(nodeId), parameter) is { IsConstant: true };
    }
}
