using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyInlineVariable
{
    public static SDFProgram DoApplyInlineVariable(SDFProgram program, InlineVariable operation)
    {
        var scopes = program.Scopes();
        var scopeIndex = Array.FindIndex(scopes, s => s.variables.Any(v => v.id == operation.variableId));
        if (scopeIndex < 0)
            throw new KeyNotFoundException($"There is no variable with id '{operation.variableId}'");

        var scope = scopes[scopeIndex];
        var variableIndex = Array.FindIndex(scope.variables, v => v.id == operation.variableId);
        var value = scope.variables[variableIndex].value;
        var tables = new PointerTables(program.pointers);
        var useCount = 0;

        var removedReferenceIds = new List<string>();

        Evaluable ReplaceWithValue(Evaluable child)
        {
            var referenceId = PointerTables.PointerId(child);
            var node = referenceId is null ? child : tables.Get(referenceId);
            if (node is not VariableReference reference || reference.variableId != operation.variableId)
                return child;

            if (referenceId is not null)
                removedReferenceIds.Add(referenceId);
            useCount++;
            return useCount == 1 ? value : tables.Expand(value);
        }

        var variables = scope.variables.ToList();
        for (var i = variableIndex + 1; i < variables.Count; i++)
            variables[i] = variables[i] with { value = tables.MapTree(variables[i].value, ReplaceWithValue) };
        var body = (SDFEvaluable)tables.MapTree(scope.body, ReplaceWithValue);

        foreach (var id in removedReferenceIds)
            tables.Remove(id);
        if (useCount == 0)
            tables.RemoveSubtree(value);

        variables.RemoveAt(variableIndex);

        program = program.WithScope(scopeIndex, new VariableScope(variables.ToArray(), body));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
