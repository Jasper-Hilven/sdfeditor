using System.Globalization;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.Tests;

// Builds small programs and inspects the result of operations on them
public static class SDFProgramTestHelpers
{
    public static readonly SDFProgramOperator.SDFProgramOperator Operator = new();

    public static readonly SDFPointers NoPointers = new(new(), new(), new(), new(), 0);

    public static SDFEvaluable Sphere(float radius) => new Sphere(new ConstValueFloat(radius));

    public static SDFProgram Program(SDFEvaluable scene, params SDFFunctionDefinition[] functions) =>
        SDFProgramNormalizePointers.Pointerify(new SDFProgram(functions, NoPointers, scene));

    public static SDFFunction Function(SDFEvaluable body, params VariableDeclaration[] variables) =>
        new("f", "f", [], variables, body);

    public static string SphereId(SDFProgram program, float radius) =>
        program.pointers.SDFEvaluablePointers
            .Single(e => e.Value is Sphere { radius: ConstValueFloat c } && c.value == radius).Key;

    // Writes the tree with all pointers expanded, e.g. "Union(Sphere(1), Empty)"
    public static string Describe(SDFProgram program, Evaluable root)
    {
        var tables = new PointerTables(program.pointers);

        string Write(Evaluable node)
        {
            node = tables.Expand(node);
            return node switch
            {
                ConstValueFloat c => c.value.ToString(CultureInfo.InvariantCulture),
                VariableReference r => $"${r.variableId}",
                _ when EvaluableChildren.HasChildren(node) =>
                    $"{node.GetType().Name}({string.Join(", ", EvaluableChildren.Children(node).Select(Write))})",
                _ => node.GetType().Name,
            };
        }

        return Write(root);
    }

    public static string DescribeScene(SDFProgram program) => Describe(program, program.scene);

    public static string DescribeBody(SDFProgram program) => Describe(program, program.Functions[0].functionBody);

    // Every pointer in the tables is used exactly once, and every used pointer exists
    public static void AssertPointersConsistent(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);
        var used = program.Functions
            .SelectMany(f => f.variables.Select(v => v.value).Append(f.functionBody))
            .Append(program.scene)
            .SelectMany(tables.Subtree)
            .ToList();

        var stored = program.pointers.SDFEvaluablePointers.Keys
            .Concat(program.pointers.SDF2DEvaluablePointers.Keys)
            .Concat(program.pointers.FloatEvaluablePointers.Keys)
            .Concat(program.pointers.IntegerEvaluablePointers.Keys);

        Assert.Equal(used.Count, used.Distinct().Count());
        Assert.Equal(stored.Order(), used.Order());
    }
}
