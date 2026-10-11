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
