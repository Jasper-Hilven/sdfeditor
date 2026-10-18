using System.Reflection;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.ProgramFiles;

internal enum SlotCategory
{
    Value,
    Node,
    Nodes,
    Arguments,
    FunctionName,
    MaterialName,
}

internal sealed record NodeSlot(string Name, Type Type, SlotCategory Category, PropertyInfo Property);

internal sealed record NodeShape(Type Type, ConstructorInfo Constructor, NodeSlot[] Slots)
{
    public ValueKind Kind => typeof(SDFEvaluable).IsAssignableFrom(Type) ? ValueKind.Sdf : ValueKind.Sdf2D;
}

/// <summary>Every node a file can name: the 3D and 2D shape records, looked up by their type name.</summary>
internal static class NodeTypes
{
    private static readonly Dictionary<string, NodeShape> ByName = typeof(SDFEvaluable).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .Where(t => typeof(SDFEvaluable).IsAssignableFrom(t) || typeof(SDF2DEvaluable).IsAssignableFrom(t))
        .Where(t => !typeof(Pointerable).IsAssignableFrom(t) && !typeof(VariableReference).IsAssignableFrom(t))
        .Select(GetShape)
        .ToDictionary(s => s.Type.Name);

    public static NodeShape? Find(string name) => ByName.GetValueOrDefault(name);

    public static NodeShape Of(Type type) =>
        ByName.TryGetValue(type.Name, out var shape) && shape.Type == type
            ? shape
            : throw new NotSupportedException($"{type.Name} is not a node that can be written to a file");

    public static IEnumerable<string> NamesLike(string name) =>
        ByName.Keys.Where(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)
                               || k.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static NodeShape GetShape(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var slots = constructor.GetParameters()
            .Select(p => type.GetProperty(p.Name!) is { } property
                ? new NodeSlot(p.Name!, p.ParameterType, CategoryOf(type, p.Name!, p.ParameterType), property)
                : throw new InvalidOperationException($"{type.Name} has no property for constructor parameter '{p.Name}'"))
            .ToArray();
        return new NodeShape(type, constructor, slots);
    }

    private static SlotCategory CategoryOf(Type node, string name, Type type) =>
        type == typeof(SDFEvaluable) || type == typeof(SDF2DEvaluable) ? SlotCategory.Node
        : type == typeof(SDFEvaluable[]) || type == typeof(SDF2DEvaluable[]) ? SlotCategory.Nodes
        : type == typeof(Evaluable[]) ? SlotCategory.Arguments
        : type == typeof(string) && name == nameof(SDFFunctionCall.functionId) ? SlotCategory.FunctionName
        : type == typeof(string) && name == nameof(Material.material) ? SlotCategory.MaterialName
        : ValueKinds.OfSite(type) is not null ? SlotCategory.Value
        : throw new NotSupportedException($"{node.Name} has a parameter of type {type.Name}, which files cannot hold");
}
