using System.Reflection;
using System.Text.RegularExpressions;
using SDFProject.SDFInteractiveNode;

namespace IDE.Operations;

// One button per operation; the parameters of its constructor are asked in a popup before it runs
public sealed partial class OperationDefinition(Type type)
{
    public static readonly OperationDefinition[] All = typeof(ISdfInteractiveNodeProgramOperation).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISdfInteractiveNodeProgramOperation).IsAssignableFrom(t))
        .Select(t => new OperationDefinition(t))
        .ToArray();

    private ConstructorInfo Constructor => type.GetConstructors().Single();

    public ParameterInfo[] Parameters => Constructor.GetParameters();

    // "InteractiveAddNodeNextTo" is shown as "Add Node Next To"
    public string Label => string.Join(" ", Words().Matches(type.Name.Replace("Interactive", "")).Select(m => m.Value));

    public ISdfInteractiveNodeProgramOperation Create(object?[] arguments) =>
        (ISdfInteractiveNodeProgramOperation)Constructor.Invoke(arguments);

    [GeneratedRegex("[A-Z][a-z]*|[0-9]+")]
    private static partial Regex Words();
}
