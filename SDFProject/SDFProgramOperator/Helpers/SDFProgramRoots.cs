using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator.Helpers;

public sealed record VariableScope(VariableDeclaration[] variables, SDFEvaluable body)
{
    public IEnumerable<Evaluable> Roots => variables.Select(v => v.value).Append(body);
}

public static class SDFProgramRoots
{
    public static SDFProgram MapRoots(this SDFProgram program, Func<Evaluable, Evaluable> mapRoot)
    {
        VariableDeclaration[] MapVariables(VariableDeclaration[] variables) =>
            variables.Select(v => v with { value = mapRoot(v.value) }).ToArray();

        var functions = program.Functions.Select(f => f.With(MapVariables(f.variables), (SDFEvaluable)mapRoot(f.functionBody))).ToArray();

        return program with
        {
            Functions = functions,
            sceneVariables = MapVariables(program.sceneVariables),
            scene = (SDFEvaluable)mapRoot(program.scene),
        };
    }

    public static VariableScope[] Scopes(this SDFProgram program) =>
        program.Functions.Select(f => new VariableScope(f.variables, f.functionBody))
            .Append(new VariableScope(program.sceneVariables, program.scene))
            .ToArray();

    public static SDFProgram WithScope(this SDFProgram program, int index, VariableScope scope)
    {
        if (index == program.Functions.Length)
            return program with { sceneVariables = scope.variables, scene = scope.body };

        var functions = program.Functions.ToArray();
        functions[index] = functions[index].With(scope.variables, scope.body);
        return program with { Functions = functions };
    }

    public static IEnumerable<Evaluable> AllRoots(this SDFProgram program) => program.Scopes().SelectMany(s => s.Roots);

    public static SDFProgram ReplaceOnlyReference(this SDFProgram program, PointerTables tables, string id, Func<Evaluable, Evaluable> replace)
    {
        var pointer = tables.PointerTo(id);
        var parentCount = 0;

        Evaluable Replace(Evaluable child)
        {
            if (!pointer.Equals(child))
                return child;

            parentCount++;
            return replace(child);
        }

        var result = program.MapRoots(root => tables.MapTree(root, Replace));

        if (parentCount != 1)
            throw new InvalidOperationException($"Node '{id}' is referenced {parentCount} times, expected exactly once");

        return result;
    }

    public static SDFFunctionDefinition With(this SDFFunctionDefinition function, VariableDeclaration[] variables, SDFEvaluable functionBody) =>
        new SDFFunction(function.name, function.id, function.argumentNames, variables, functionBody);

    public static HashSet<string> VariableIds(this SDFProgram program) =>
        program.Scopes().SelectMany(s => s.variables).Select(v => v.id).ToHashSet();

    public static string AllocateId(ref long nextFreePointer, Func<string, bool> isTaken)
    {
        string id;
        do id = (nextFreePointer++).ToString();
        while (isTaken(id));
        return id;
    }
}
