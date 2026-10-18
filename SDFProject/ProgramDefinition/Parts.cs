using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.ProgramDefinition;

/// <summary>
/// A part is a model in its own file (content/parts/eye.sdf) that other models use with "Part part=eye". Loaded into a program it is a
/// scene variable named "part:eye", reused through references like any variable; its own variables are named "part:eye/...". Files keep
/// the reference and never copy the part, so a part is edited in its own file only.
/// </summary>
public static class Parts
{
    public const string Prefix = "part:";

    public static string VariableName(string part) => Prefix + part;

    public static string InnerVariableName(string part, string variable) => $"{Prefix}{part}/{variable}";

    public static bool IsPartVariable(string variableName) => variableName.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The part a variable belongs to: "eye" for both "part:eye" and "part:eye/radius".</summary>
    public static string? PartOf(string variableName) =>
        IsPartVariable(variableName) ? variableName[Prefix.Length..].Split('/')[0] : null;

    /// <summary>The variable that is the part itself, rather than one of its inner variables.</summary>
    public static bool IsPartItself(string variableName) => IsPartVariable(variableName) && !variableName.Contains('/');

    /// <summary>The part the node belongs to, or null when the node is the model's own: nodes inside a part can only be edited in its file.</summary>
    public static string? PartContaining(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        return program.Scopes()
            .SelectMany(scope => scope.variables)
            .Where(v => IsPartVariable(v.name))
            .FirstOrDefault(v => tables.Subtree(v.value).Contains(nodeId)) is { } variable
            ? PartOf(variable.name)
            : null;
    }
}
