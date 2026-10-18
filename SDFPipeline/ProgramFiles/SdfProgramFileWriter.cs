using System.Globalization;
using System.Text;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFPipeline.ProgramFiles.FileSyntax;

namespace SDFPipeline.ProgramFiles;

/// <summary>
/// Writes a program as text: one node per line, children indented below their parent, values as name=value.
/// Pointer ids are not written; reuse is only expressed through variables, which are written by name.
/// </summary>
internal sealed class SdfProgramFileWriter
{
    private const string Indent = "    ";

    private readonly SDFProgram program;
    private readonly PointerTables tables;
    private readonly Dictionary<string, string> variableNames;
    private readonly Dictionary<string, SDFFunctionDefinition> functions;
    private readonly Dictionary<string, ValueKind?[]> argumentKinds;
    private readonly StringBuilder text = new();
    private SDFFunctionDefinition? currentFunction;

    private SdfProgramFileWriter(SDFProgram program)
    {
        this.program = program;
        tables = new PointerTables(program.pointers);
        variableNames = program.Scopes().SelectMany(s => s.variables).ToDictionary(v => v.id, v => v.name);
        functions = program.Functions.ToDictionary(f => f.id);

        RequireUnique(program.Functions.Select(f => f.name), "functions");
        foreach (var function in program.Functions)
            RequireUnique(function.argumentNames, $"arguments of function '{function.name}'");
        foreach (var scope in program.Scopes())
            RequireUnique(scope.variables.Select(v => v.name), "variables in one scope");

        argumentKinds = InferArgumentKinds();
    }

    public static string Write(SDFProgram program) => new SdfProgramFileWriter(program).Write();

    private string Write()
    {
        Line(0, $"{FormatKeyword} {SdfProgramFile.FormatVersion}");

        var scopes = program.Scopes();
        for (var i = 0; i < program.Functions.Length; i++)
        {
            currentFunction = program.Functions[i];
            text.Append('\n');
            Line(0, Header(currentFunction));
            WriteScope(scopes[i], 1);
        }

        currentFunction = null;
        text.Append('\n');
        WriteScope(scopes[^1], 0);
        return text.ToString();
    }

    private string Header(SDFFunctionDefinition function)
    {
        var kinds = argumentKinds[function.id];
        var arguments = function.argumentNames.Select((name, i) =>
            kinds[i] is { } kind ? $"{Name(name)}: {ValueKinds.Keyword(kind)}" : Name(name));
        return $"{FunctionKeyword} {Name(function.name)}({string.Join(", ", arguments)})";
    }

    private void WriteScope(VariableScope scope, int depth)
    {
        foreach (var variable in scope.variables.Where(v => !Parts.IsPartVariable(v.name)))
        {
            var value = tables.Expand(variable.value);
            if (value is SDFEvaluable or SDF2DEvaluable)
            {
                Line(depth, $"{Name(variable.name)} =");
                WriteNode(value, depth + 1);
            }
            else
            {
                var type = value is ConstValueInt ? $": {ValueKinds.Keyword(ValueKind.Int)}" : "";
                Line(depth, $"{Name(variable.name)}{type} = {Value(value)}");
            }
        }

        WriteNode(tables.Expand(scope.body), depth);
    }

    private void WriteNode(Evaluable node, int depth)
    {
        if (node is VariableReference reference)
        {
            Line(depth, variableNames.GetValueOrDefault(reference.variableId) is { } name && Parts.IsPartItself(name)
                ? $"{PartKeyword} {PartParameter}={Name(Parts.PartOf(name)!)}"
                : Reference(reference));
            return;
        }

        var shape = NodeTypes.Of(node.GetType());
        var parts = new List<string> { shape.Type.Name };
        var children = new List<Evaluable>();
        foreach (var slot in shape.Slots)
        {
            var value = slot.Property.GetValue(node);
            switch (slot.Category)
            {
                case SlotCategory.Value:
                    parts.Add($"{slot.Name}={Value((Evaluable)value!)}");
                    break;
                case SlotCategory.FunctionName:
                    parts.Add($"{slot.Name}={Name(Function((string)value!).name)}");
                    break;
                case SlotCategory.MaterialName:
                    parts.Add($"{slot.Name}={Name((string)value!)}");
                    break;
                case SlotCategory.Arguments when value is Evaluable[] { Length: > 0 } arguments:
                    parts.Add($"{slot.Name}=[{string.Join(", ", arguments.Select(Value))}]");
                    break;
                case SlotCategory.Node:
                    children.Add((Evaluable)value!);
                    break;
                case SlotCategory.Nodes:
                    children.AddRange((Evaluable[])value!);
                    break;
            }
        }

        Line(depth, string.Join(" ", parts));
        foreach (var child in children)
            WriteNode(child, depth + 1);
    }

    private string Value(Evaluable value) => value switch
    {
        ConstValueFloat c => Number(c.value),
        ConstValueInt c => c.value.ToString(CultureInfo.InvariantCulture),
        ConstValueV2 c => $"({Number(c.v2.x)}, {Number(c.v2.y)})",
        ConstValueV3 c => $"({Number(c.v3.x)}, {Number(c.v3.y)}, {Number(c.v3.z)})",
        VariableReference r => Reference(r),
        FunctionParameterValueFloat p => Argument(p.functionParameterIndex),
        FunctionParameterValueInt p => Argument(p.functionParameterIndex),
        FunctionParameterValueV2 p => Argument(p.functionParameterIndex),
        FunctionParameterValueV3 p => Argument(p.functionParameterIndex),
        _ => throw new NotSupportedException($"{value.GetType().Name} cannot be written as a value"),
    };

    private string Reference(VariableReference reference) =>
        variableNames.TryGetValue(reference.variableId, out var name)
            ? "$" + Name(name)
            : throw new InvalidOperationException($"Variable '{reference.variableId}' is used but never declared");

    private string Argument(int index) =>
        currentFunction is null ? throw new InvalidOperationException("Function arguments can only be used inside a function")
        : index < currentFunction.argumentNames.Length ? "@" + Name(currentFunction.argumentNames[index])
        : throw new InvalidOperationException($"Function '{currentFunction.name}' has no argument {index}");

    private SDFFunctionDefinition Function(string id) =>
        functions.TryGetValue(id, out var function) ? function : throw new InvalidOperationException($"There is no function '{id}'");

    /// <summary>
    /// Functions do not store the types of their arguments, so they are taken from how the arguments are used and what they are called
    /// with. The header writes them, so a reader knows that, for example, a call's 3 is a whole number.
    /// </summary>
    private Dictionary<string, ValueKind?[]> InferArgumentKinds()
    {
        var kinds = program.Functions.ToDictionary(f => f.id, f => new ValueKind?[f.argumentNames.Length]);

        void Record(string functionId, int index, Evaluable use)
        {
            var function = Function(functionId);
            if (index >= function.argumentNames.Length)
                throw new InvalidOperationException($"Function '{function.name}' has no argument {index}");

            var kind = ValueKinds.Of(use);
            if (kinds[functionId][index] is { } known && known != kind)
                throw new InvalidOperationException(
                    $"Argument '{function.argumentNames[index]}' of function '{function.name}' is used as both {ValueKinds.Describe(known)} and {ValueKinds.Describe(kind)}");
            kinds[functionId][index] = kind;
        }

        var scopes = program.Scopes();
        for (var i = 0; i < scopes.Length; i++)
        {
            var functionId = i < program.Functions.Length ? program.Functions[i].id : null;
            foreach (var node in scopes[i].Roots.SelectMany(root => Walk(tables.Expand(root))))
            {
                switch (node)
                {
                    case FunctionParameterValue parameter when functionId is null:
                        throw new InvalidOperationException($"{parameter.GetType().Name} is used outside a function");
                    case FunctionParameterValueFloat p: Record(functionId!, p.functionParameterIndex, p); break;
                    case FunctionParameterValueInt p: Record(functionId!, p.functionParameterIndex, p); break;
                    case FunctionParameterValueV2 p: Record(functionId!, p.functionParameterIndex, p); break;
                    case FunctionParameterValueV3 p: Record(functionId!, p.functionParameterIndex, p); break;
                    case SDFFunctionCall call:
                        for (var a = 0; a < call.arguments.Length; a++)
                            Record(call.functionId, a, call.arguments[a]);
                        break;
                }
            }
        }

        return kinds;
    }

    private static IEnumerable<Evaluable> Walk(Evaluable node) =>
        EvaluableChildren.Children(node).SelectMany(Walk).Prepend(node);

    private static void RequireUnique(IEnumerable<string> names, string what)
    {
        if (names.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            throw new InvalidOperationException($"Two {what} are named '{duplicate.Key}', so a file cannot tell them apart");
    }

    private void Line(int depth, string line) =>
        text.Append(string.Concat(Enumerable.Repeat(Indent, depth))).Append(line).Append('\n');
}
