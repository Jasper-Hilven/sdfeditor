using System.Collections.Concurrent;
using System.Reflection;

namespace SDFProject;

// Evaluables are positional records: the public constructor's parameters match their properties by name,
// so the direct children of any node can be read and replaced generically
public static class EvaluableChildren
{
    private sealed record NodeShape(ConstructorInfo constructor, PropertyInfo[] properties);

    private static readonly ConcurrentDictionary<Type, NodeShape> Shapes = new();

    public static bool HasChildren(Evaluable node)
    {
        var shape = Shapes.GetOrAdd(node.GetType(), GetShape);
        return shape.properties.Any(p => p.GetValue(node) is Evaluable or Evaluable[] { Length: > 0 });
    }

    public static IEnumerable<Evaluable> Children(Evaluable node)
    {
        var shape = Shapes.GetOrAdd(node.GetType(), GetShape);
        foreach (var property in shape.properties)
        {
            switch (property.GetValue(node))
            {
                case Evaluable child:
                    yield return child;
                    break;
                case Evaluable[] children:
                    foreach (var child in children)
                        yield return child;
                    break;
            }
        }
    }

    public static Evaluable MapChildren(Evaluable node, Func<Evaluable, Evaluable> map)
    {
        var shape = Shapes.GetOrAdd(node.GetType(), GetShape);
        var changed = false;

        var arguments = new object?[shape.properties.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var value = shape.properties[i].GetValue(node);
            switch (value)
            {
                case Evaluable child:
                    var mappedChild = map(child);
                    changed |= !ReferenceEquals(mappedChild, child);
                    arguments[i] = mappedChild;
                    break;
                case Evaluable[] children:
                    var mapped = Array.CreateInstance(children.GetType().GetElementType()!, children.Length);
                    for (var j = 0; j < children.Length; j++)
                    {
                        var mappedElement = map(children[j]);
                        changed |= !ReferenceEquals(mappedElement, children[j]);
                        mapped.SetValue(mappedElement, j);
                    }
                    arguments[i] = mapped;
                    break;
                default:
                    arguments[i] = value;
                    break;
            }
        }

        return changed ? (Evaluable)shape.constructor.Invoke(arguments) : node;
    }

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
