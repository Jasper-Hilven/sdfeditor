using System.Globalization;
using SDFProject.ProgramDefinition;
using SDFProject.SDFInteractiveNode;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.Calculation;

public class YamlRender
{
    private const string Indent = "    ";

    public string[] GetRender(SdfInteractiveNodeProgram program)
    {
        var tables = new PointerTables(program.Program.pointers);
        var lines = new List<string>();
        Render(program.Program.scene, 0);
        return lines.ToArray();

        void Render(Evaluable node, int depth)
        {
            var id = PointerTables.PointerId(node);
            node = Resolve(tables, node);

            var parameters = new List<string>();
            var children = new List<Evaluable>();
            foreach (var (name, value) in Properties(node))
            {
                switch (value)
                {
                    case Evaluable child when IsNode(tables, child):
                        children.Add(child);
                        break;
                    case Evaluable child:
                        parameters.Add($"{name}={Value(tables, child)}");
                        break;
                    case Evaluable[] elements:
                        children.AddRange(elements.Where(e => IsNode(tables, e)));
                        var values = elements.Where(e => !IsNode(tables, e)).Select(e => Value(tables, e)).ToArray();
                        if (values.Length > 0)
                            parameters.Add($"{name}=[{string.Join(", ", values)}]");
                        break;
                    default:
                        parameters.Add($"{name}={value}");
                        break;
                }
            }

            var text = string.Join(" ", parameters.Prepend(node.GetType().Name));
            if (id is not null && id == program.SelectedNodeId)
                text = $"{text} <---";
            lines.Add(string.Concat(Enumerable.Repeat(Indent, depth)) + text);

            foreach (var child in children)
                Render(child, depth + 1);
        }
    }

    private static Evaluable Resolve(PointerTables tables, Evaluable node) =>
        PointerTables.PointerId(node) is { } id ? tables.Get(id) : node;

    // Shapes and operations get their own line, plain values are shown as parameters of their parent
    private static bool IsNode(PointerTables tables, Evaluable evaluable) =>
        Resolve(tables, evaluable) is SDFEvaluable or SDF2DEvaluable;

    private static string Value(PointerTables tables, Evaluable evaluable) => Resolve(tables, evaluable) switch
    {
        ConstValueFloat c => Number(c.value),
        ConstValueInt c => c.value.ToString(CultureInfo.InvariantCulture),
        ConstValueV2 c => $"({Number(c.v2.x)}, {Number(c.v2.y)})",
        ConstValueV3 c => $"({Number(c.v3.x)}, {Number(c.v3.y)}, {Number(c.v3.z)})",
        FunctionParameterValueFloat p => $"arg{p.functionParameterIndex}",
        FunctionParameterValueInt p => $"arg{p.functionParameterIndex}",
        FunctionParameterValueV2 p => $"arg{p.functionParameterIndex}",
        FunctionParameterValueV3 p => $"arg{p.functionParameterIndex}",
        VariableReference v => $"${v.variableId}",
        var other => other.GetType().Name,
    };

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static IEnumerable<(string name, object? value)> Properties(Evaluable node)
    {
        var type = node.GetType();
        return type.GetConstructors().Single().GetParameters()
            .Select(p => (p.Name!, type.GetProperty(p.Name!)?.GetValue(node)));
    }
}
