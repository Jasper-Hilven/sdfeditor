using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// What a battle object is: a side's spawn place or flag stand, a pod (a side's or a neutral one), or one of a side's machines' places
/// (ticket 24): a turret, or the zone where its helicopter and tanks start and respawn.
/// </summary>
public enum BattleKind
{
    Spawn,
    Flag,
    Pod,
    Turret,
    Machines,
}

/// <summary>
/// A gameplay object of a battle side or a neutral one (ticket 17): a spawn place, a flag stand, a pod, a turret or the machine zone
/// (ticket 24), standing on the ground at
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

    /// <summary>
    /// The room a turret takes up round its axis (content/models/machines/turret-*): its mossy foot is 1.37 m across, its cap 1.27 m,
    /// and the barrel reaches 0.9 m from the axis under the cap's rim.
    /// </summary>
    public const float TurretRadius = 1;

    /// <summary>A machine zone's radius when the level file gives none (ticket 24).</summary>
    public const float DefaultMachineZone = 3;

    /// <summary>The least and greatest machine zone: room for a helicopter and two tanks (each under 0.8 m) side by side, and no more
    /// than a corner of open ground.</summary>
    public const float MinMachineZone = 1.5f, MaxMachineZone = 8;

    /// <summary>A spawn zone's radius when the level file gives none (ticket 20).</summary>
    public const float DefaultSpawnZone = 3;

    /// <summary>The least and greatest spawn zone a level file may give: room for one player (the controller's radius is 0.3 m), and
    /// well inside a castle's courtyard.</summary>
    public const float MinSpawnZone = 0.5f, MaxSpawnZone = 10;

    /// <summary>
    /// A spawn's zone (ticket 20): the side's players appear anywhere within this radius round <see cref="At"/>, at a free spot,
    /// facing <see cref="Yaw"/>; or the machine zone's (ticket 24), where the side's helicopter and tanks start and respawn. Only a
    /// spawn and a machine zone have one; the level file's <c>radius</c>, <see cref="DefaultSpawnZone"/> or
    /// <see cref="DefaultMachineZone"/> without it.
    /// </summary>
    public float ZoneRadius { get; init; } = DefaultSpawnZone;

    /// <summary>Whether it is a zone (a spawn or the machine zone) with a <see cref="ZoneRadius"/>.</summary>
    public bool IsZone => Kind is BattleKind.Spawn or BattleKind.Machines;

    /// <summary>How far round <see cref="At"/> it takes up the ground: a zone all of it, which must be open, level ground.</summary>
    public float Radius => Kind switch
    {
        BattleKind.Pod => PodRadius,
        BattleKind.Flag => FlagRadius,
        BattleKind.Turret => TurretRadius,
        _ => ZoneRadius,
    };

    /// <summary>How the level file writes it: spawn, flag, pod minigun, turret, machines.</summary>
    public string Name => Kind == BattleKind.Pod ? $"pod {Pod}" : Kind.ToString().ToLowerInvariant();
}

/// <summary>
/// One of the level's two battle sides, <c>west</c> (−x) or <c>east</c> (+x): its spawn place, its flag stand, its weapon pods (one or
/// more, each a different weapon of <see cref="BattleLayout.Weapons"/>, in that list's order) and its health pod (and any more health
/// pods, <see cref="BattleSide.MoreHealth"/>); on a level with machines (ticket 24) also its <see cref="BattleLayout.TurretsPerSide"/>
/// turrets and its machine zone. On the boss layout (ticket 33.3, <see cref="LevelLayout.Boss"/>) a side may have no weapon pods and no
/// health pod (Prime's side uses the neutral pods), so <see cref="Health"/> is null there.
/// </summary>
public sealed record BattleSide(string Name, BattleObject Spawn, BattleObject Flag, IReadOnlyList<BattleObject> Weapons, BattleObject? Health)
{
    public const string West = "west";
    public const string East = "east";

    /// <summary>The side's health pods after <see cref="Health"/>, the first; none unless the level file gives more than one.</summary>
    public IReadOnlyList<BattleObject> MoreHealth { get; init; } = [];

    /// <summary>The side's turrets (ticket 24), each standing where it is and respawning there; none on a level without machines.</summary>
    public IReadOnlyList<BattleObject> Turrets { get; init; } = [];

    /// <summary>
    /// The zone where the side's helicopter and tanks start and respawn (ticket 24), behind its castle; null on a level without
    /// machines.
    /// </summary>
    public BattleObject? MachineSpawn { get; init; }

    /// <summary>The spawn, the flag, the weapon pods, the health pod, any more health pods, then the turrets and the machine zone if there are any, in that order.</summary>
    public IReadOnlyList<BattleObject> Objects =>
        [Spawn, Flag, .. Weapons, .. Health is null ? Array.Empty<BattleObject>() : [Health], .. MoreHealth, .. Turrets, .. MachineSpawn is null ? Array.Empty<BattleObject>() : [MachineSpawn]];
}

/// <summary>
/// The level's battle layout (ticket 17): the two sides, and the two neutral pods between them, one long-range
/// (<see cref="LongRange"/>) and one short-range (<see cref="ShortRange"/>).
/// </summary>
public sealed record BattleLayout(BattleSide West, BattleSide East, IReadOnlyList<BattleObject> Neutral)
{
    /// <summary>
    /// The weapons a pod can hold (Unity's WeaponIds.Pickups), in the order a side keeps its weapon pods. A side has any of them, each
    /// once, the two sides the same ones; the levels' sides began with the minigun and the pistol.
    /// </summary>
    public static readonly IReadOnlyList<string> Weapons = ["minigun", "pistol", "sniper", "electro-gun", "flamethrower", "poison-gun", "blob-gun"];

    /// <summary>The health pod's pickup; a side has one or more.</summary>
    public const string Health = "health";

    /// <summary>How many turrets each side has on a level with machines (ticket 24).</summary>
    public const int TurretsPerSide = 4;

    /// <summary>The neutral long-range pods; a level has one of them.</summary>
    public static readonly IReadOnlyList<string> LongRange = ["sniper", "electro-gun"];

    /// <summary>The neutral short-range pods; a level has one of them.</summary>
    public static readonly IReadOnlyList<string> ShortRange = ["flamethrower", "poison-gun", "blob-gun"];

    public IReadOnlyList<BattleSide> Sides => [West, East];

    /// <summary>Whether the sides have machines (ticket 24): turrets and a machine zone. Both sides have them or neither, but on the boss layout (ticket 33.3) Prime's side alone may.</summary>
    public bool HasMachines => West.MachineSpawn is not null || East.MachineSpawn is not null;

    /// <summary>Every object: the west side's, the east side's, then the neutral pods.</summary>
    public IEnumerable<BattleObject> Objects => West.Objects.Concat(East.Objects).Concat(Neutral);
}
