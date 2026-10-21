using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>What a battle object is: a side's spawn place or flag stand, or a pod (a side's or a neutral one).</summary>
public enum BattleKind
{
    Spawn,
    Flag,
    Pod,
}

/// <summary>
/// A gameplay object of a battle side or a neutral one (ticket 17): a spawn place, a flag stand or a pod, standing on the ground at
/// <see cref="At"/> (x, z) like a placement, turned <see cref="Yaw"/> degrees (counter-clockwise from above; a spawn faces +z at 0,
/// +x at 90). Inside a castle it stands on the courtyard, which is the castle's floor: the castles have no floor of their own there.
/// </summary>
/// <param name="Pod">The pod's pickup (minigun, health, sniper), its prefab Assets/Pickups/Pods/&lt;pod&gt;-pod.prefab; null for a
/// spawn or a flag.</param>
/// <param name="Line">The line of the level file it is on, for errors found while baking.</param>
public sealed record BattleObject(BattleKind Kind, string? Pod, Vector2 At, float Yaw, int Line)
{
    /// <summary>The pod's foot radius (content/models/pickups/weapon-pod.sdf).</summary>
    public const float PodRadius = 1.3f;

    /// <summary>The flag stand's foot radius (content/models/pickups/flag-stand.sdf).</summary>
    public const float FlagRadius = 1.05f;

    /// <summary>A spawn zone's radius when the level file gives none (ticket 20).</summary>
    public const float DefaultSpawnZone = 3;

    /// <summary>The least and greatest spawn zone a level file may give: room for one player (the controller's radius is 0.3 m), and
    /// well inside a castle's courtyard.</summary>
    public const float MinSpawnZone = 0.5f, MaxSpawnZone = 10;

    /// <summary>
    /// A spawn's zone (ticket 20): the side's players appear anywhere within this radius round <see cref="At"/>, at a free spot,
    /// facing <see cref="Yaw"/>. Only a spawn has one; the level file's <c>radius</c>, <see cref="DefaultSpawnZone"/> without it.
    /// </summary>
    public float ZoneRadius { get; init; } = DefaultSpawnZone;

    /// <summary>How far round <see cref="At"/> it takes up the ground: a spawn its whole zone, which must be open, level ground.</summary>
    public float Radius => Kind switch
    {
        BattleKind.Pod => PodRadius,
        BattleKind.Flag => FlagRadius,
        _ => ZoneRadius,
    };

    /// <summary>How the level file writes it: spawn, flag, pod minigun.</summary>
    public string Name => Kind == BattleKind.Pod ? $"pod {Pod}" : Kind.ToString().ToLowerInvariant();
}

/// <summary>
/// One of the level's two battle sides, <c>west</c> (−x) or <c>east</c> (+x): its spawn place, its flag stand, and its minigun, pistol
/// and health pods, exactly one of each.
/// </summary>
public sealed record BattleSide(string Name, BattleObject Spawn, BattleObject Flag, BattleObject Minigun, BattleObject Pistol, BattleObject Health)
{
    public const string West = "west";
    public const string East = "east";

    /// <summary>The spawn, the flag and the three pods, in that order.</summary>
    public IReadOnlyList<BattleObject> Objects => [Spawn, Flag, Minigun, Pistol, Health];
}

/// <summary>
/// The level's battle layout (ticket 17): the two sides, and the two neutral pods between them, one long-range
/// (<see cref="LongRange"/>) and one short-range (<see cref="ShortRange"/>).
/// </summary>
public sealed record BattleLayout(BattleSide West, BattleSide East, IReadOnlyList<BattleObject> Neutral)
{
    /// <summary>The pods every side has, one of each.</summary>
    public static readonly IReadOnlyList<string> SidePods = ["minigun", "pistol", "health"];

    /// <summary>The neutral long-range pods; a level has one of them.</summary>
    public static readonly IReadOnlyList<string> LongRange = ["sniper", "electro-gun"];

    /// <summary>The neutral short-range pods; a level has one of them.</summary>
    public static readonly IReadOnlyList<string> ShortRange = ["flamethrower", "poison-gun", "blob-gun"];

    public IReadOnlyList<BattleSide> Sides => [West, East];

    /// <summary>Every object: the west side's, the east side's, then the neutral pods.</summary>
    public IEnumerable<BattleObject> Objects => West.Objects.Concat(East.Objects).Concat(Neutral);
}
