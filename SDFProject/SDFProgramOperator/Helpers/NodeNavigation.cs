using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator.Helpers;

public static class NodeNavigation
{
    public static string NextSiblingOrParent(SDFPointers pointers, string id) => SiblingOrParent(pointers, id, 1);

    public static string PreviousSiblingOrParent(SDFPointers pointers, string id) => SiblingOrParent(pointers, id, -1);

    public static string Parent(SDFPointers pointers, string id) => new PointerTables(pointers).ParentOf(id) ?? id;

    public static string FirstChild(SDFPointers pointers, string id) =>
        ChildIds(new PointerTables(pointers), id).FirstOrDefault() ?? id;

    public static string Next(SDFProgram program, string id)
    {
        var tables = new PointerTables(program.pointers);
        if (ChildIds(tables, id).FirstOrDefault() is { } firstChild)
            return firstChild;

        var current = id;
        for (; tables.ParentOf(current) is { } parentId; current = parentId)
        {
            var siblings = ChildIds(tables, parentId);
            var index = siblings.IndexOf(current) + 1;
            if (index < siblings.Count)
                return siblings[index];
        }

        var roots = ScopeRootIds(program, current);
        var rootIndex = roots.IndexOf(current) + 1;
        return rootIndex > 0 && rootIndex < roots.Count ? roots[rootIndex] : id;
    }

    public static string Previous(SDFProgram program, string id)
    {
        var tables = new PointerTables(program.pointers);
        if (tables.ParentOf(id) is not { } parentId)
        {
            var roots = ScopeRootIds(program, id);
            var rootIndex = roots.IndexOf(id) - 1;
            return rootIndex >= 0 ? DeepestLastDescendant(tables, roots[rootIndex]) : id;
        }

        var siblings = ChildIds(tables, parentId);
        var index = siblings.IndexOf(id) - 1;
        return index < 0 ? parentId : DeepestLastDescendant(tables, siblings[index]);
    }

    private static string DeepestLastDescendant(PointerTables tables, string id)
    {
        var current = id;
        while (ChildIds(tables, current).LastOrDefault() is { } lastChild)
            current = lastChild;
        return current;
    }

    private static List<string> ScopeRootIds(SDFProgram program, string rootId) =>
        program.Scopes()
            .Select(scope => scope.Roots.Select(PointerTables.PointerId).OfType<string>().ToList())
            .FirstOrDefault(ids => ids.Contains(rootId)) ?? [];

    private static string SiblingOrParent(SDFPointers pointers, string id, int offset)
    {
        var tables = new PointerTables(pointers);
        if (tables.ParentOf(id) is not { } parentId)
            return id;

        var siblings = ChildIds(tables, parentId);
        var index = siblings.IndexOf(id) + offset;
        return index >= 0 && index < siblings.Count ? siblings[index] : parentId;
    }

    private static List<string> ChildIds(PointerTables tables, string id) =>
        EvaluableChildren.Children(tables.Get(id))
            .Select(PointerTables.PointerId)
            .OfType<string>()
            .ToList();
}
