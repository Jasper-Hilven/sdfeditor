namespace SDFProject;

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
        var function = program.functions[functionIndex];

        // Declared right before its only use, so every variable the extracted nodes reference is still visible
        var variables = function.variables.ToList();
        variables.Insert(declarationIndex, new VariableDeclaration(operation.variableName, variableId, onNode));

        var functions = program.functions.ToArray();
        functions[functionIndex] = function.With(variables.ToArray(), function.functionBody);

        return program with { functions = functions, pointers = tables.ToPointers(nextFreePointer) };
    }

    private static (int functionIndex, int declarationIndex) FindOwner(SDFProgram program, PointerTables tables, string nodeId)
    {
        for (var f = 0; f < program.functions.Length; f++)
        {
            var function = program.functions[f];
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
