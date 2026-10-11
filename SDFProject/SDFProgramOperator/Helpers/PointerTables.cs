using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator.Helpers;

public sealed class PointerTables
{
    private readonly Dictionary<string, SDFEvaluable> sdf;
    private readonly Dictionary<string, SDF2DEvaluable> sdf2D;
    private readonly Dictionary<string, FloatEvaluable> floats;
    private readonly Dictionary<string, IntegerEvaluable> integers;

    public PointerTables(SDFPointers pointers)
    {
        sdf = new(pointers.SDFEvaluablePointers);
        sdf2D = new(pointers.SDF2DEvaluablePointers);
        floats = new(pointers.FloatEvaluablePointers);
        integers = new(pointers.IntegerEvaluablePointers);
    }

    public bool Contains(string id) =>
        sdf.ContainsKey(id) || sdf2D.ContainsKey(id) || floats.ContainsKey(id) || integers.ContainsKey(id);

    public Evaluable Get(string id) =>
        sdf.TryGetValue(id, out var s) ? s
        : sdf2D.TryGetValue(id, out var s2D) ? s2D
        : floats.TryGetValue(id, out var f) ? f
        : integers.TryGetValue(id, out var i) ? i
        : throw new KeyNotFoundException($"There is no node with pointer '{id}'");

    public void Set(string id, Evaluable node)
    {
        if (sdf.ContainsKey(id)) sdf[id] = (SDFEvaluable)node;
        else if (sdf2D.ContainsKey(id)) sdf2D[id] = (SDF2DEvaluable)node;
        else if (floats.ContainsKey(id)) floats[id] = (FloatEvaluable)node;
        else if (integers.ContainsKey(id)) integers[id] = (IntegerEvaluable)node;
        else throw new KeyNotFoundException($"There is no node with pointer '{id}'");
    }

    public void Remove(string id)
    {
        sdf.Remove(id);
        sdf2D.Remove(id);
        floats.Remove(id);
        integers.Remove(id);
    }

    /// <summary>Removes the pointers of every node reachable from the root, including the root itself when it is a pointer.</summary>
    public void RemoveSubtree(Evaluable root)
    {
        foreach (var id in Subtree(root))
            Remove(id);
    }

    public string? ParentOf(string id)
    {
        var pointer = PointerTo(id);
        return AllNodes().FirstOrDefault(entry => EvaluableChildren.Children(entry.node).Contains(pointer)).id;
    }

    private IEnumerable<(string id, Evaluable node)> AllNodes() =>
        sdf.Select(e => (e.Key, (Evaluable)e.Value))
            .Concat(sdf2D.Select(e => (e.Key, (Evaluable)e.Value)))
            .Concat(floats.Select(e => (e.Key, (Evaluable)e.Value)))
            .Concat(integers.Select(e => (e.Key, (Evaluable)e.Value)));

    public Evaluable PointerTo(string id) =>
        sdf.ContainsKey(id) ? new SDFEvaluablePointerable(id)
        : sdf2D.ContainsKey(id) ? new SDF2DEvaluablePointerable(id)
        : floats.ContainsKey(id) ? new FloatEvaluablePointerable(id)
        : integers.ContainsKey(id) ? new IntegerEvaluablePointerable(id)
        : throw new KeyNotFoundException($"There is no node with pointer '{id}'");

    public static string? PointerId(Evaluable node) => node switch
    {
        SDFEvaluablePointerable p => p.PointerId,
        SDF2DEvaluablePointerable p => p.PointerId,
        FloatEvaluablePointerable p => p.PointerId,
        IntegerEvaluablePointerable p => p.PointerId,
        _ => null,
    };

    public List<string> Subtree(Evaluable root)
    {
        var ids = new List<string>();
        var pending = new Stack<Evaluable>([root]);
        while (pending.TryPop(out var node))
        {
            if (PointerId(node) is { } id)
            {
                ids.Add(id);
                node = Get(id);
            }

            foreach (var child in EvaluableChildren.Children(node))
                pending.Push(child);
        }
        return ids;
    }

    public Evaluable Expand(Evaluable node)
    {
        if (PointerId(node) is { } id)
            node = Get(id);
        return EvaluableChildren.MapChildren(node, Expand);
    }

    public Evaluable MapTree(Evaluable root, Func<Evaluable, Evaluable> map)
    {
        // Walk the original tree and map the root last, so nodes produced by map are never mapped again
        foreach (var id in Subtree(root))
            Set(id, EvaluableChildren.MapChildren(Get(id), map));
        if (PointerId(root) is null)
            root = EvaluableChildren.MapChildren(root, map);
        return map(root);
    }

    public SDFPointers ToPointers(long nextFreePointer) => new(sdf, sdf2D, floats, integers, nextFreePointer);
}
