using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyExtractVariable
{
    public static SDFProgram DoApplyExtractVariable(SDFProgram program, ExtractVariable operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!tables.Contains(operation.onNodeId))
            throw new KeyNotFoundException($"There is no node with pointer '{operation.onNodeId}'");

        var (functionIndex, declarationIndex) = FindOwner(program, tables, operation.onNodeId);

        var variableIds = program.VariableIds();
        var nextFreePointer = program.pointers.nextFreePointer;
        var variableId = SDFProgramRoots.AllocateId(ref nextFreePointer, id => tables.Contains(id) || variableIds.Contains(id));
        var onNode = tables.PointerTo(operation.onNodeId);
        Evaluable reference = onNode switch
        {
            SDFEvaluable => new VariableReferenceSDF(variableId),
            SDF2DEvaluable => new VariableReferenceSDF2D(variableId),
            FloatEvaluable => new VariableReferenceFloat(variableId),
            IntegerEvaluable => new VariableReferenceInt(variableId),
            _ => throw new NotSupportedException($"Variables of type {onNode.GetType().Name} are not supported"),
        };

        program = program.ReplaceOnlyReference(tables, operation.onNodeId, _ => reference);
        var function = program.Functions[functionIndex];

        // Declared right before its only use, so every variable the extracted nodes reference is still visible
        var variables = function.variables.ToList();
        variables.Insert(declarationIndex, new VariableDeclaration(operation.variableName, variableId, onNode));

        var functions = program.Functions.ToArray();
        functions[functionIndex] = function.With(variables.ToArray(), function.functionBody);

        return program with { Functions = functions, pointers = tables.ToPointers(nextFreePointer) };
    }

    // Variables can only be declared inside a function, so the node has to be part of a function's variable or body
    public static bool CanExtract(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && program.Functions.Any(f =>
            f.variables.Any(v => tables.Subtree(v.value).Contains(nodeId)) || tables.Subtree(f.functionBody).Contains(nodeId));
    }

    private static (int functionIndex, int declarationIndex) FindOwner(SDFProgram program, PointerTables tables, string nodeId)
    {
        for (var f = 0; f < program.Functions.Length; f++)
        {
            var function = program.Functions[f];
            for (var v = 0; v < function.variables.Length; v++)
            {
                if (tables.Subtree(function.variables[v].value).Contains(nodeId))
                    return (f, v);
            }

            if (tables.Subtree(function.functionBody).Contains(nodeId))
                return (f, function.variables.Length);
        }

        if (tables.Subtree(program.scene).Contains(nodeId))
            throw new InvalidOperationException($"Node '{nodeId}' is part of the scene, variables can only be declared inside a function");

        throw new InvalidOperationException($"Node '{nodeId}' is not used anywhere in the program");
    }
}
