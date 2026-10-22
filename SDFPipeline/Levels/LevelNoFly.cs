using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// A box the machine helicopters never fly into (ticket 29.6; the level file's <c>nofly at=(0, 38.75) size=(51, 22.5) yaw=180</c> line, any
/// number of them): a rectangle on the floor plan, <paramref name="At"/> its centre (x, z), <paramref name="Size"/> its full width (x) and
/// depth (z) before it is turned by <paramref name="Yaw"/> degrees (the same turn as a placed model's, so a model's footprint and its
/// <c>place</c> line give the box), as tall as the room. Helicopters fly round it (<c>SdfBattle.NoFlyZones</c>).
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors.</param>
public sealed record NoFlySpec(Vector2 At, Vector2 Size, float Yaw, int Line);

/// <summary>What the baker writes into the battle block of level.json for the no-fly boxes; null when the level has none, so other levels are unchanged.</summary>
public static class LevelNoFly
{
    public static object? Document(LevelDefinition level) =>
        level.NoFly.Count == 0
            ? null
            : level.NoFly.Select(b => new { at = new[] { b.At.X, b.At.Y }, size = new[] { b.Size.X, b.Size.Y }, yaw = b.Yaw }).ToArray();
}
