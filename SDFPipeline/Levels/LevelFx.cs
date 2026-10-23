using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// The haze of a level (ticket 29.1): the level file's <c>fog density=0.0075 color=(0.9, 0.93, 0.92)</c> line. Unity's built-in fog,
/// exponential squared: a surface <c>d</c> metres from the camera keeps <c>exp(-(density * d)^2)</c> of its own colour and the rest is
/// <paramref name="Color"/> (sRGB, each part 0 to 1); at 0.0075 that is 87% at 50 m, 57% at 100 m and 28% at 150 m. The camera of a
/// level in a room clears to the same colour. A level without the line has no fog at all.
/// </summary>
public sealed record FogSpec(float Density, Vector3 Color)
{
    public const float MinDensity = 0.0005f;
    public const float MaxDensity = 0.05f;
}

/// <summary>
/// A plume of steam (the level file's <c>steam at=(-15, 16.5, 0) size=8 rise=40 count=22</c> line): soft puffs <paramref name="Size"/>
/// metres wide born at <paramref name="At"/> that rise <paramref name="Rise"/> metres and fade away, <paramref name="Count"/> of them
/// alive at a time (half as many on the low preset). All the level's plumes together hold at most <see cref="LevelFx.MaxSteamParticles"/>.
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors.</param>
public sealed record SteamSpec(Vector3 At, float Size, float Rise, int Count, int Line)
{
    public const int DefaultCount = 20;
}

/// <summary>
/// A tap's drip (the level file's <c>drip at=(0, 57, 49.5) water=16.5 every=2 size=0.15</c> line): a drop of <paramref name="Size"/>
/// metres across falls from <paramref name="At"/> to the height <paramref name="Water"/> every <paramref name="Every"/> seconds and
/// lands in a small ring.
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors.</param>
public sealed record DripSpec(Vector3 At, float Water, float Every, float Size, int Line)
{
    public const float DefaultEvery = 2;
    public const float DefaultSize = 0.15f;
}

/// <summary>What the baker writes into level.json for the fog, steam and drips; each null when the level has none, so other levels are unchanged.</summary>
public static class LevelFx
{
    /// <summary>The most steam particles a level holds in all (overdraw is the cost, not the count).</summary>
    public const int MaxSteamParticles = 80;

    public static object? FogDocument(LevelDefinition level) =>
        level.Fog is { } fog ? new { density = fog.Density, color = Array(fog.Color) } : null;

    public static object? SteamDocument(LevelDefinition level) =>
        level.Steam.Count == 0
            ? null
            : level.Steam.Select(s => new { at = Array(s.At), size = s.Size, rise = s.Rise, count = s.Count }).ToArray();

    public static object? DripDocument(LevelDefinition level) =>
        level.Drips.Count == 0
            ? null
            : level.Drips.Select(d => new { at = Array(d.At), water = d.Water, every = d.Every, size = d.Size }).ToArray();

    private static float[] Array(Vector3 v) => [v.X, v.Y, v.Z];
}
