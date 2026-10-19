using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplySetNodeParameter
{
    public static SDFProgram DoApplySetNodeParameter(SDFProgram program, SetNodeParameter operation)
    {
        var tables = new PointerTables(program.pointers);
        tables.Set(operation.nodeId, Limited(NodeParameters.With(tables.Get(operation.nodeId), operation.parameter, operation.components)));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    /// <summary>A Detail's detail stays in the range a file accepts, so a model saved from the IDE can always be read back.</summary>
    private static Evaluable Limited(Evaluable node) =>
        node is Detail { detail: ConstValueFloat { value: var value } } detail
            ? detail with { detail = new ConstValueFloat(Math.Clamp(value, Detail.MinDetail, Detail.MaxDetail)) }
            : node;

    public static bool CanSet(SDFProgram program, string nodeId, string parameter)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && NodeParameters.Find(tables.Get(nodeId), parameter) is { IsConstant: true };
    }
}
