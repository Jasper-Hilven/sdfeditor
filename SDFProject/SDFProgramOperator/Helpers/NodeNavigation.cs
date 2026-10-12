using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator.Helpers;

/// <summary>Moves a node selection through the pointer tree; every method returns the given id when there is nowhere to move.</summary>
public static class NodeNavigation
{
    public static string NextSiblingOrParent(SDFPointers pointers, string id) => SiblingOrParent(pointers, id, 1);

    public static string PreviousSiblingOrParent(SDFPointers pointers, string id) => SiblingOrParent(pointers, id, -1);

    public static string Parent(SDFPointers pointers, string id) => new PointerTables(pointers).ParentOf(id) ?? id;

    public static string FirstChild(SDFPointers pointers, string id) =>
        ChildIds(new PointerTables(pointers), id).FirstOrDefault() ?? id;

    // Depth-first order: the first child, else the next sibling of the node or of its nearest ancestor that has one
    public static string Next(SDFPointers pointers, string id)
    {
        var tables = new PointerTables(pointers);
        if (ChildIds(tables, id).FirstOrDefault() is { } firstChild)
            return firstChild;

        for (var current = id; tables.ParentOf(current) is { } parentId; current = parentId)
        {
            var siblings = ChildIds(tables, parentId);
            var index = siblings.IndexOf(current) + 1;
            if (index < siblings.Count)
                return siblings[index];
        }

        return id;
    }

    // Reverse of Next: the deepest last descendant of the previous sibling, else the parent
    public static string Previous(SDFPointers pointers, string id)
    {
        var tables = new PointerTables(pointers);
        if (tables.ParentOf(id) is not { } parentId)
            return id;

        var siblings = ChildIds(tables, parentId);
        var index = siblings.IndexOf(id) - 1;
        if (index < 0)
            return parentId;

        var current = siblings[index];
        while (ChildIds(tables, current).LastOrDefault() is { } lastChild)
            current = lastChild;
        return current;
    }

    private static string SiblingOrParent(SDFPointers pointers, string id, int offset)
    {
        var tables = new PointerTables(pointers);
        if (tables.ParentOf(id) is not { } parentId)
            return id;

        var siblings = ChildIds(tables, parentId);
        var index = siblings.IndexOf(id) + offset;
        return index >= 0 && index < siblings.Count ? siblings[index] : parentId;
    }

    // Only children behind a pointer are nodes that can be selected, inline children are leaves without an id
    private static List<string> ChildIds(PointerTables tables, string id) =>
        EvaluableChildren.Children(tables.Get(id))
            .Select(PointerTables.PointerId)
            .OfType<string>()
            .ToList();
}
