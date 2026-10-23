using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Levels;

public static partial class LevelFile
{
    private const string Heat = "heat";
    private const string HeatRingLine = "ring";

    /// <summary>
    /// heat cycle=45 first=20 warm=3 hot=12 cool=3 damage=10 cost=50 name="the hob" (ticket 28.10), with below it one line per ring,
    /// <c>ring group=back at=(-10.5, 3) radius=5.4</c>. Every parameter of the heat line has the default shown.
    /// </summary>
    private static HeatSpec ReadHeat(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Heat, line with { Children = [] }, cursor);
        var cycle = parameters.Number("cycle", HeatSpec.DefaultCycle, 1, 3600, "seconds for the groups' turns to run through, 1 to 3600");
        var first = parameters.Number("first", HeatSpec.DefaultFirst, 0, 3600, "seconds into a round the first group starts to warm, 0 to 3600");
        var warm = parameters.Number("warm", HeatSpec.DefaultWarm, 0.1f, 600, "seconds a group warms (harmlessly), 0.1 to 600");
        var hot = parameters.Number("hot", HeatSpec.DefaultHot, 0.1f, 600, "seconds a group is hot, 0.1 to 600");
        var cool = parameters.Number("cool", HeatSpec.DefaultCool, 0.1f, 600, "seconds a group cools (harmlessly), 0.1 to 600");
        var damage = parameters.Number("damage", HeatSpec.DefaultDamage, 0, 1000, "damage a second to what stands in a hot ring, 0 to 1000");
        var cost = parameters.Number("cost", HeatSpec.DefaultCost, 1, 1000, "the factor bots and tanks pay for a warming or hot ring, 1 to 1000");
        var cause = parameters.Name("name", HeatSpec.DefaultCause, null)!;
        parameters.Done("cycle", "first", "warm", "hot", "cool", "damage", "cost", "name");

        var rings = new List<HeatRing>();
        foreach (var child in line.Children)
        {
            var childCursor = new Cursor(child);
            var entry = childCursor.ReadName($"a '{HeatRingLine}' line");
            if (entry != HeatRingLine)
                throw childCursor.Error($"The heat has '{HeatRingLine}' lines, like '{HeatRingLine} group=back at=(-10.5, 3) radius=5.4'; there is no '{entry}'");
            var ring = Parameters.Read("a heat ring", child, childCursor);
            var group = ring.Name("group", null, null) ?? throw childCursor.Error("A heat ring needs 'group', the name of the pair that heats together, like group=back");
            var at = ring.Vector2("at", "a point like (-10.5, 3), x and z in metres");
            var radius = ring.Number("radius", null, 0.5f, 100, "metres, 0.5 to 100");
            ring.Done("group", "at", "radius");
            rings.Add(new HeatRing(group, at, radius, child.Number));
        }
        if (rings.Count == 0)
            throw cursor.Error($"The heat needs one or more rings indented below it, like '{HeatRingLine} group=back at=(-10.5, 3) radius=5.4'");
        var spec = new HeatSpec(cycle, first, warm, hot, cool, damage, cost, cause, rings, line.Number);
        if (spec.Groups.Count > HeatSpec.MaxGroups)
            throw cursor.Error($"The heat has {spec.Groups.Count} groups ({string.Join(", ", spec.Groups)}), but at most {HeatSpec.MaxGroups} can take turns");
        if (spec.Span * spec.Groups.Count > cycle)
            throw cursor.Error($"The {spec.Groups.Count} group(s) take {FileSyntax.Number(spec.Span)} s each to warm, burn and cool, which is more than the cycle of {FileSyntax.Number(cycle)} s; lengthen the cycle or shorten their turns");
        return spec;
    }
}
