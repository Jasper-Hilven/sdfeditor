using System.Collections.Concurrent;
using System.Reflection;
using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator.Helpers;

public enum NodeParameterKind
{
    Number,
    Vector2,
    Vector3,
    Angle,
}

public sealed record NodeParameter(string Name, NodeParameterKind Kind, Evaluable Value)
{
    public bool IsConstant => Value is ConstValueFloat or ConstValueV2 or ConstValueV3;

    public int ComponentCount => Kind switch
    {
        NodeParameterKind.Vector2 => 2,
        NodeParameterKind.Vector3 => 3,
        _ => 1,
    };

    public float[] Components() => (Kind, Value) switch
    {
        (NodeParameterKind.Angle, ConstValueV2 c) => [MathF.Atan2(c.v2.x, c.v2.y) * 180 / MathF.PI],
        (_, ConstValueFloat c) => [c.value],
        (_, ConstValueV2 c) => [c.v2.x, c.v2.y],
        (_, ConstValueV3 c) => [c.v3.x, c.v3.y, c.v3.z],
        _ => throw new InvalidOperationException($"Parameter '{Name}' is a {Value.GetType().Name}, only constants can be edited"),
    };

    public Evaluable ToConstant(float[] components)
    {
        if (components.Length != ComponentCount)
            throw new ArgumentException($"Parameter '{Name}' has {ComponentCount} components, but {components.Length} were given");

        return Kind switch
        {
            NodeParameterKind.Number => new ConstValueFloat(components[0]),
            NodeParameterKind.Vector2 => new ConstValueV2(new V2(components[0], components[1])),
            NodeParameterKind.Vector3 => new ConstValueV3(new V3(components[0], components[1], components[2])),
            NodeParameterKind.Angle => new ConstValueV2(new V2(MathF.Sin(components[0] * MathF.PI / 180), MathF.Cos(components[0] * MathF.PI / 180))),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
        };
    }
}

public static class NodeParameters
{
    private sealed record NodeShape(ConstructorInfo constructor, PropertyInfo[] properties);

    private static readonly ConcurrentDictionary<Type, NodeShape> Shapes = new();

    public static IReadOnlyList<NodeParameter> Of(Evaluable node)
    {
        var shape = Shapes.GetOrAdd(node.GetType(), GetShape);
        return shape.properties
            .Where(p => KindOf(p) is not null)
            .Select(p => new NodeParameter(p.Name, KindOf(p)!.Value, (Evaluable)p.GetValue(node)!))
            .ToArray();
    }

    public static NodeParameter? Find(Evaluable node, string name) => Of(node).FirstOrDefault(p => p.Name == name);

    public static Evaluable With(Evaluable node, string name, float[] components)
    {
        var parameter = Find(node, name)
                        ?? throw new KeyNotFoundException($"A {node.GetType().Name} has no parameter '{name}'");
        if (!parameter.IsConstant)
            throw new InvalidOperationException($"Parameter '{name}' is a {parameter.Value.GetType().Name}, only constants can be edited");

        var shape = Shapes.GetOrAdd(node.GetType(), GetShape);
        var arguments = shape.properties
            .Select(p => p.Name == name ? parameter.ToConstant(components) : p.GetValue(node))
            .ToArray();
        return (Evaluable)shape.constructor.Invoke(arguments);
    }

    private static NodeParameterKind? KindOf(PropertyInfo property) =>
        property.PropertyType == typeof(FloatEvaluable) ? NodeParameterKind.Number
        : property.PropertyType == typeof(V2Evaluable) ? property.Name == "sinCos" ? NodeParameterKind.Angle : NodeParameterKind.Vector2
        : property.PropertyType == typeof(V3Evaluable) ? NodeParameterKind.Vector3
        : null;

    private static NodeShape GetShape(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var properties = constructor.GetParameters()
            .Select(p => type.GetProperty(p.Name!)
                         ?? throw new InvalidOperationException($"{type.Name} has no property for constructor parameter '{p.Name}'"))
            .ToArray();
        return new NodeShape(constructor, properties);
    }
}
