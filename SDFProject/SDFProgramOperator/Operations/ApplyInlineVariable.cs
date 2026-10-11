using SDFProject.Definitions;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyInlineVariable
{
    public static SDFProgram DoApplyInlineVariable(SDFProgram program, InlineVariable operation)
    {
        var functionIndex = Array.FindIndex(program.functions, f => f.variables.Any(v => v.id == operation.variableId));
        if (functionIndex < 0)
            throw new KeyNotFoundException($"There is no variable with id '{operation.variableId}'");

        var function = program.functions[functionIndex];
        var variableIndex = Array.FindIndex(function.variables, v => v.id == operation.variableId);
        var value = function.variables[variableIndex].value;
        var tables = new PointerTables(program.pointers);
        var useCount = 0;

        // The first use takes over the variable's nodes, every further use gets its own copy so that
        // each pointer id keeps standing for exactly one node
        Evaluable ReplaceWithValue(Evaluable child)
        {
            if (child is not VariableReference reference || reference.variableId != operation.variableId)
                return child;

            useCount++;
            return useCount == 1 ? value : tables.Expand(value);
        }

        // A variable is only visible to the variables declared after it and to the function body
        var variables = function.variables.ToList();
        for (var i = variableIndex + 1; i < variables.Count; i++)
            variables[i] = variables[i] with { value = tables.MapTree(variables[i].value, ReplaceWithValue) };
        var body = (SDFEvaluable)tables.MapTree(function.functionBody, ReplaceWithValue);

        if (useCount == 0)
            tables.RemoveSubtree(value);

        variables.RemoveAt(variableIndex);

        var functions = program.functions.ToArray();
        functions[functionIndex] = function.With(variables.ToArray(), body);

        return program with { functions = functions, pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
