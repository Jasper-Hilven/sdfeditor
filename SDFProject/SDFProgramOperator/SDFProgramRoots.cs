namespace SDFProject;

// The roots of a program are the trees hanging directly off it: every variable value, every function body and the scene
public static class SDFProgramRoots
{
    /// <summary>The program with every root replaced by the result of mapRoot, in declaration order.</summary>
    public static SDFProgram MapRoots(this SDFProgram program, Func<Evaluable, Evaluable> mapRoot)
    {
        var functions = program.functions.Select(f => f.With(
            f.variables.Select(v => v with { value = mapRoot(v.value) }).ToArray(),
            (SDFEvaluable)mapRoot(f.functionBody))).ToArray();

        return program with { functions = functions, scene = (SDFEvaluable)mapRoot(program.scene) };
    }

    /// <summary>
    /// Replaces the only reference to the pointer id anywhere in the program. The pointers in the tables are
    /// updated in place, the program's own pointers are left untouched.
    /// </summary>
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

        // A pointer id stands for exactly one node, so it has exactly one parent
        if (parentCount != 1)
            throw new InvalidOperationException($"Node '{id}' is referenced {parentCount} times, expected exactly once");

        return result;
    }

    public static SDFFunctionDefinition With(this SDFFunctionDefinition function, VariableDeclaration[] variables, SDFEvaluable functionBody) =>
        new SDFFunction(function.name, function.id, function.argumentNames, variables, functionBody);

    public static HashSet<string> VariableIds(this SDFProgram program) =>
        program.functions.SelectMany(f => f.variables).Select(v => v.id).ToHashSet();

    // Pointer ids and variable ids share one id space
    public static string AllocateId(ref long nextFreePointer, Func<string, bool> isTaken)
    {
        string id;
        do id = (nextFreePointer++).ToString();
        while (isTaken(id));
        return id;
    }
}
