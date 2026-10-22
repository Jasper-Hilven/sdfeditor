using System.Numerics;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Levels;

/// <summary>What the bake does for a level's heat (ticket 28.10): checks the rings and writes level.json's "heat" for Unity.</summary>
public static class HeatBake
{
    /// <summary>The rings lie on the island, and none covers a spawn, flag stand, pod or turret (the level's safe places).</summary>
    public static void Check(LevelDefinition level, LevelTerrain terrain)
    {
        if (level.Heat is not { } heat)
            return;
        foreach (var ring in heat.Rings)
        {
            if (!terrain.OnIsland(ring.At.X, ring.At.Y))
                throw new SdfProgramFileException(ring.Line, $"The heat ring at ({ring.At.X}, {ring.At.Y}) is beyond the island's edge");
            foreach (var item in level.Battle?.Objects ?? [])
                if (Vector2.Distance(item.At, ring.At) < ring.Radius)
                    throw new SdfProgramFileException(ring.Line,
                        $"The heat ring at ({ring.At.X}, {ring.At.Y}) with radius {ring.Radius} covers the {item.Name} at ({item.At.X}, {item.At.Y}); nobody should be burnt where they spawn, or taking a flag or pod");
        }
    }

    /// <summary>
    /// The "heat" of level.json, or null: the numbers of HeatSpec, and the rings in the order of their groups' turns, each with its group's
    /// index (0 is the first to heat) and name, its centre (x, z), radius and the ground's height at its centre. Unity's HeatRules plays the
    /// cycle on the round clock; the models standing at a ring's centre glow with it, and the ring is a navmesh area.
    /// </summary>
    public static object? Document(LevelDefinition level, LevelTerrain terrain)
    {
        if (level.Heat is not { } heat)
            return null;
        var groups = heat.Groups;
        return new
        {
            cycle = heat.Cycle,
            first = heat.First,
            warm = heat.Warm,
            hot = heat.Hot,
            cool = heat.Cool,
            damage = heat.Damage,
            cost = heat.Cost,
            cause = heat.Cause,
            groups = groups.ToArray(),
            rings = heat.Rings.Select(r => new
            {
                group = groups.ToList().IndexOf(r.Group),
                name = r.Group,
                at = new[] { r.At.X, r.At.Y },
                radius = r.Radius,
                y = terrain.Height(r.At.X, r.At.Y),
            }).ToArray(),
        };
    }
}
