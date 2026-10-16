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

        var (scopeIndex, declarationIndex) = FindOwner(program, tables, operation.onNodeId);

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
        var scope = program.Scopes()[scopeIndex];

        var variables = scope.variables.ToList();
        variables.Insert(declarationIndex, new VariableDeclaration(operation.variableName, variableId, onNode));
        program = program.WithScope(scopeIndex, scope with { variables = variables.ToArray() });

        return program with { pointers = tables.ToPointers(nextFreePointer) };
    }

    public static bool CanExtract(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return tables.Contains(nodeId) && program.AllRoots().Any(root => tables.Subtree(root).Contains(nodeId));
    }

    public static string UnusedVariableName(SDFProgram program)
    {
        var names = program.Scopes().SelectMany(s => s.variables).Select(v => v.name).ToHashSet();
        var i = 1;
        while (names.Contains($"var{i}"))
            i++;
        return $"var{i}";
    }

    private static (int scopeIndex, int declarationIndex) FindOwner(SDFProgram program, PointerTables tables, string nodeId)
    {
        var scopes = program.Scopes();
        for (var s = 0; s < scopes.Length; s++)
        {
            var scope = scopes[s];
            for (var v = 0; v < scope.variables.Length; v++)
            {
                if (tables.Subtree(scope.variables[v].value).Contains(nodeId))
                    return (s, v);
            }

            if (tables.Subtree(scope.body).Contains(nodeId))
                return (s, scope.variables.Length);
        }

        throw new InvalidOperationException($"Node '{nodeId}' is not used anywhere in the program");
    }
}
