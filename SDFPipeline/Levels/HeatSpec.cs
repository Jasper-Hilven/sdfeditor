using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// One ring of a level's heat (ticket 28.10): a round patch of floor that gets hot, at <paramref name="At"/> (x, z) with
/// <paramref name="Radius"/> metres, in a <paramref name="Group"/> that heats as one (a kitchen's mirrored burner pair).
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors found while baking.</param>
public sealed record HeatRing(string Group, Vector2 At, float Radius, int Line);

/// <summary>
/// The heat of a level (ticket 28.10, the kitchen's hob): the level file's <c>heat</c> section. Rings heat in turns, one group at a time in
/// the order the groups first appear: a group warms for <see cref="Warm"/> seconds (a dull glow, harmless), is hot for <see cref="Hot"/>
/// (it hurts <see cref="Damage"/> a second, and bots keep off at <see cref="Cost"/> times the path cost) and cools for <see cref="Cool"/>,
/// then the next group starts; the groups run once through every <see cref="Cycle"/> seconds, which leaves the cold glass between their
/// rounds, the first group starting <see cref="First"/> seconds into a round. A kill by it is nobody's and the kill feed names it
/// <see cref="Cause"/>. The same rules are Unity's HeatRules.
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors found while baking.</param>
public sealed record HeatSpec(float Cycle, float First, float Warm, float Hot, float Cool, float Damage, float Cost, string Cause, IReadOnlyList<HeatRing> Rings, int Line)
{
    public const float DefaultCycle = 45, DefaultFirst = 20, DefaultWarm = 3, DefaultHot = 12, DefaultCool = 3, DefaultDamage = 10, DefaultCost = 50;
    public const string DefaultCause = "the heat";

    /// <summary>The most groups a level has: each is a navmesh area of its own (Burner, Burner 2, ...).</summary>
    public const int MaxGroups = 4;

    /// <summary>The groups' names, in the order of their turns.</summary>
    public IReadOnlyList<string> Groups => Rings.Select(r => r.Group).Distinct().ToArray();

    /// <summary>Seconds one group's turn takes: warming, hot and cooling.</summary>
    public float Span => Warm + Hot + Cool;
}
