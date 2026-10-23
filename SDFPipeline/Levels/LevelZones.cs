using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>What a zone does to whoever stands in it (ticket 29.7): the rules themselves are in <c>SdfBattle.ZoneRules</c>.</summary>
public enum ZoneRule
{
    /// <summary>Soap: players slide (ground acceleration x0.25, braking x0.1, top speed x1.3).</summary>
    Slick,

    /// <summary>Water: a scout standing in it, or out of it less than 2 s ago, takes x1.5 damage from the electro gun.</summary>
    Wet,
}

/// <summary>
/// A zone of the level (ticket 29.7; the level file's <c>zone suds slick</c> line, any number of them): the ground region named
/// <paramref name="Region"/> follows <paramref name="Rule"/>. A region may have both rules, one line each.
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors.</param>
public sealed record ZoneSpec(string Region, ZoneRule Rule, int Line);

/// <summary>
/// The zone mask of a level (ticket 29.7): a byte per <see cref="CellSize"/> metre cell over the playable area, <see cref="Min"/> the corner
/// of the first cell, the cells in rows from the lowest z up, x fastest (cell (i, j) is index <c>j * Width + i</c>, covering
/// [min + (i, j), min + (i + 1, j + 1)]); each byte holds <see cref="SlickBit"/> and <see cref="WetBit"/> flags. A cell holds a flag when
/// the ground region drawn at its centre (the last that covers it, as in the region map) has a zone line with that rule. Written to
/// zones.bin and read at runtime by position; no colliders, no navmesh change.
/// </summary>
public sealed record ZoneMap(Vector2 Min, int Width, int Height, byte[] Cells)
{
    public const float CellSize = 1;
    public const byte SlickBit = 1;
    public const byte WetBit = 2;
    public const string FileName = "zones.bin";

    public Vector2 Max => Min + new Vector2(Width, Height) * CellSize;

    public static byte BitOf(ZoneRule rule) => rule == ZoneRule.Slick ? SlickBit : WetBit;

    public static ZoneMap Bake(LevelDefinition level, GroundRegions regions)
    {
        var bits = new Dictionary<string, byte>();
        foreach (var zone in level.Zones)
        {
            if (regions.Regions.All(r => r.Name != zone.Region))
                throw new ProgramFiles.SdfProgramFileException(zone.Line, $"The zone names the region '{zone.Region}', which the level does not have");
            bits[zone.Region] = (byte)(bits.GetValueOrDefault(zone.Region) | BitOf(zone.Rule));
        }
        var min = -level.Play.HalfSize;
        var width = (int)MathF.Ceiling(level.Play.Size.X / CellSize - 1e-4f);
        var height = (int)MathF.Ceiling(level.Play.Size.Y / CellSize - 1e-4f);
        var cells = new byte[width * height];
        Parallel.For(0, height, j =>
        {
            for (var i = 0; i < width; i++)
                if (regions.At(min + new Vector2(i + 0.5f, j + 0.5f) * CellSize) is { } region && bits.TryGetValue(region.Name, out var flags))
                    cells[j * width + i] = flags;
        });
        return new ZoneMap(min, width, height, cells);
    }

    /// <summary>What the baker writes into level.json for the zones; null when the level has none, so other levels are unchanged.</summary>
    public static object? Document(LevelDefinition level, ZoneMap? map, string? hash) =>
        map is null || level.Zones.Count == 0
            ? null
            : new
            {
                file = FileName,
                encoding = "one byte per cell, rows from the lowest z up, x fastest (index = j * cells.x + i); bit 0 slick (soap: players slide), bit 1 wet (water: the electro gun's x1.5)",
                cellSize = CellSize,
                cells = new[] { map.Width, map.Height },
                min = new[] { map.Min.X, map.Min.Y },
                max = new[] { map.Max.X, map.Max.Y },
                hash,
                rules = level.Zones.Select(z => new { region = z.Region, rule = z.Rule == ZoneRule.Slick ? "slick" : "wet" }).ToArray(),
            };
}
