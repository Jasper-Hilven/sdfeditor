using System.Globalization;
using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Skies;

namespace SDFPipeline.Levels;

/// <summary>
/// The text format of a level, content/levels/&lt;name&gt;.level, in the style of program files: a format line, then one section per
/// line, each with its parameters as name=value after it or on lines indented below it, and its entries (like the hills' ring) indented
/// below it. Comments start with '#'. The file name is the level's name.
/// <code>
/// format 1
///
/// play size=(150, 100)                      # the playable rectangle, centred on the origin
/// seed 1
///
/// hills
///     ring width=28 height=10               # rise outside the rectangle to a crest this high, this far out
///     noise amplitude=2.5 scale=18          # unevenness along the crest and on the slopes
///
/// edge                                      # where the island ends, behind the hill crests
///     margin=6 rim=3                        # the ground bends down to 3 m high at the edge, all round
///     underside island-underside            # the rocky underside below it, from content/models/
///
/// regions base=moss                         # the ground other than the base (grass unless base= names another), 2D shapes; where regions overlap, the later one wins
///     dirt
///         Circle center=(-60, 0) radius=18 noise=1.2
///     tiles stone=0.6 grout=0.05             # drawn as stones (the region named tiles is, unless pattern=plain)
///         RoundedBox2D center=(-62, 0) size=(14, 10) radius=1
///     path
///         Polyline points=[(-45, 0), (-10, 6), (20, -4), (45, 0)] width=3 noise=0.6
///
/// place                                     # unique models from content/models/, by name
///     castle0 at=(-62, 0) yaw=90 ground=tiles flatten=22
///     rock1 at=(10, 30) yaw=40
///     team-red-crown0 at=(-67, 0) y=20.35      # 20.35 m above the ground there: on top of the castle's keep
///
/// scatter                                   # models by the thousand, drawn instanced
///     grass-turf0 weight=3
///     grass-turf1
///     density=0.6 cluster=2 clusterScale=6 spacing=0.45 avoid=dirt,tiles,path margin=0.5 maxSlope=25
///
/// trees                                     # trees on the border hills, in a band of distance from the playable rectangle
///     tree0 weight=2
///     pine0
///     band=(12, 28) density=0.012 spacing=6 maxSlope=35
///
/// draw foliage                              # how the instanced renderer draws the group the scatter is in
///     rings=[6, 15, 35] outline=1 shadows=1 fade=5 wind=0.05 windHeight=0.5
///
/// sky preset=night                          # content/skies/night.sky, with any of its lines overridden below
///     fade end=200
///
/// room size=(160, 160, 160)                 # a closed room over the playable area: walls on its edges, a ceiling at y=160 (see <see cref="RoomSpec"/>)
/// room size=(150, 64, 100) walls=toybox-wall ceiling=toybox-lid     # the same with the faces in the named materials, not the checker
///      glow=north height=3 color=(1, 0.82, 0.5) light=(0, 0.77, 0.64) lightColor=(1, 0.93, 0.7) ambient=(0.45, 0.4, 0.6)
///
/// fog density=0.0075 color=(0.9, 0.93, 0.92)  # a haze (Unity's exponential squared fog; the camera of a room clears to its colour)
/// steam at=(-15, 16.5, 0) size=8 rise=40 count=22    # a plume of soft puffs 8 m wide that rise 40 m, 22 alive at a time (80 in all)
/// drip at=(0, 57, 49.5) water=16.5 every=2 size=0.15 # a drop falls from the point into water at that height every 2 s
///
/// sides                                     # the two battle sides, west (-x) and east (+x), see <see cref="BattleLayout"/>
///     west
///         spawn at=(-57, 0) yaw=90 radius=3
///         flag at=(-61, 0)
///         pod minigun at=(-48, 5.5)            # the side's weapons, any of the seven, each once; both sides the same
///         pod pistol at=(-48, -5.5)
///         pod health at=(-51, 9)
///         turret at=(-47, 10) yaw=60            # a level with machines (ticket 24): four turrets per side
///         turret at=(-47, -10) yaw=120
///         turret at=(-62, 20) yaw=30
///         turret at=(-62, -20) yaw=150
///         machines at=(-70, 22.5) yaw=90 radius=3  # where the side's helicopter and tanks start and respawn
///     east
///         ...
/// neutral                                   # the pods between the sides
///     pod sniper at=(0, 30)
///     pod flamethrower at=(0, -30)
/// </code>
/// Only <c>play</c> is required: without <c>hills</c> the ground stays flat up to the edge, without <c>edge</c> the island ends at the
/// crest (so a level with neither bakes the flat floor alone, with no hill, rim or underside), without <c>scatter</c>, <c>trees</c> and
/// <c>draw</c> nothing is scattered or drawn instanced, without <c>sky</c> the bake holds no sky at all, and the seed is
/// <see cref="LevelDefinition.DefaultSeed"/>.
/// <para>
/// A region's shapes are <c>Circle</c> (center, radius), <c>Box2D</c> (center, size, angle), <c>RoundedBox2D</c> (the same and radius)
/// and <c>Polyline</c> (points, width), each with optional <c>noise</c> (metres the edge wobbles) and <c>noiseScale</c>. Sizes are full
/// widths, angles degrees. A region's own line may give <c>wobble</c>, metres the ground shader moves its edge with fine noise (0.06 by
/// default; 0 keeps the edge ruled straight). The ground outside every region is grass; <c>regions base=moss</c> makes it
/// materials/ground/ground_moss.sdfmat instead. level.json names the base ground's material under the role <c>grass</c> whatever it is, so
/// Unity needs no change. A region's material is materials/ground/ground_&lt;region&gt;.sdfmat; there are at most four regions.
/// </para>
/// <para>
/// A placement names a model as its file is named, hyphens and all (amanita-scout), then <c>at</c> (x, z), and optionally <c>yaw</c>
/// (degrees, counter-clockwise from above), <c>scale</c>, <c>flatten</c> (the radius of level ground round it), <c>falloff</c> (metres
/// over which that ground blends back), <c>ground</c> (a region painted under its footprint), <c>groundMargin</c> (how far that
/// reaches past the footprint) and <c>y</c> (metres above the ground there that its base stands, to put it on top of another model).
/// </para>
/// <para>
/// A scatter (<see cref="ScatterLayer"/>; there may be several) lists the models it picks from below it, each named as its file is, with
/// an optional <c>weight</c> (1 by default), and takes <c>density</c> (instances per square metre) and <c>spacing</c> (the least distance
/// between them), and optionally <c>group</c> (the draw group, foliage by default), <c>cluster</c> and <c>clusterScale</c>, <c>avoid</c>
/// (regions, as names separated by commas), <c>margin</c>, <c>maxSlope</c> (degrees), <c>reach</c> (metres up the hills) and
/// <c>scale</c> (the least and greatest, like (0.85, 1.15)), on its line or on lines of their own below it. Every group a scatter
/// draws in needs a <c>draw</c> section (<see cref="DrawGroup"/>), one per group: <c>rings</c> (where each ring of the camera's
/// surroundings ends, nearest first), and optionally <c>outline</c>, <c>shadows</c> and <c>lowShadows</c> (how many of the nearest
/// rings get them), <c>blend</c>, <c>fade</c>, <c>wind</c>, <c>windHeight</c>, <c>tintHeight</c>, <c>lowDensity</c> and
/// <c>lowDistance</c>. A group may be drawn without a scatter in it, for instances that come from elsewhere.
/// </para>
/// <para>
/// A trees section (<see cref="LevelDefinition.Trees"/>; there may be several) is written like a scatter, in group trees by default,
/// with a <c>band</c> (the least and greatest distance from the playable rectangle's edge in metres, negative inside it) in place of
/// <c>reach</c>.
/// </para>
/// <para>
/// The edge takes a <c>margin</c>, and optionally a <c>rim</c> (the ground's height at the edge, in metres) with its <c>shoulder</c>
/// (how far in the ground starts bending down to it) and, on a line of its own below it, the <c>underside</c> model.
/// </para>
/// <para>
/// The sky names a preset in content/skies/ and may override its lines (see <see cref="SkyFile"/>); without a sky line the level is
/// baked with no sky data. The room takes a <c>size</c> (width, height, depth; across and along must be the play size) and optionally a
/// <c>square</c> (8 m by default), the side of the checker's squares. With <c>walls</c> and <c>ceiling</c> (the names of
/// materials/room/&lt;name&gt;.sdfmat files) the room's faces are drawn in those instead of the checker: the room may be any size, and
/// the squares go. Unity picks the pattern by the material's name (LevelRoom). <c>glow</c> (north is +z, south -z, east +x, west -x) adds
/// a band of emissive light along the top of that wall, <c>height</c> metres tall (3) in <c>color</c>; <c>light</c> is the direction
/// towards the room's lamp, <c>lightColor</c> its hue and <c>ambient</c> the flat ambient light (sRGB, each part 0 to 1), each replacing
/// the indoor default of the chessboard (overhead, warm white) when given.
/// </para>
/// <para>
/// The <c>fog</c> line (ticket 29.1; a level has at most one) gives the level a haze: a surface <c>d</c> metres from the camera keeps
/// <c>exp(-(density * d)^2)</c> of its colour and the rest is the fog's <c>color</c> (sRGB, each part 0 to 1; write a hex colour as
/// its parts, as '#' starts a comment). Unity draws it with its built-in fog in the toon shaders. <c>steam</c> (any number of lines) is a
/// plume of puffs <c>size</c> metres wide born at <c>at</c> that rise <c>rise</c> metres and fade, <c>count</c> of them alive at a time
/// (20 by default; the level's plumes hold 80 in all, half as many on the low preset). <c>drip</c> is one drop of <c>size</c> metres
/// (0.15) that falls from <c>at</c> to the height <c>water</c> every <c>every</c> seconds (2) and lands in a ring.
/// </para>
/// <para>
/// A <c>machines</c> line of its own (ticket 27.2; not the sides' <c>machines</c> zone line) names the set of machine models the level's
/// turrets, tanks and helicopters use: <c>machines set=toys ceiling=20</c>. Without it, or with <c>set=mushroom</c>, they are the
/// fungal machines of ticket 24 (turret-red, ...); another set has <c>&lt;kind&gt;-&lt;set&gt;-&lt;team&gt;</c> models (turret-toys-red). With it,
/// a level with a room also gives its helicopters the room's limits (6-12 m, 16 m over bases, 3 m off the walls) under the flight
/// <c>ceiling</c> (metres over the floor, at least 17, at most 2 under the lid; 20, or the room's height less 8 if lower, by default).
/// </para>
/// <para>
/// <c>nofly</c> lines (ticket 29.6, any number) are boxes the machine helicopters fly round, for the things in their flight band that stand
/// up to the room's flight ceiling and past it (the bathroom's tub 16.5 m, giant duck and pyramids 9 m, tall bottles 6.6 m): <c>nofly
/// at=(0, 38.75) size=(51, 22.5) yaw=180</c>, the box's centre (x, z), its full width (x) and depth (z) and its turn in degrees (as a
/// <c>place</c> line's yaw), from the floor to the lid. They are written by hand next to the <c>place</c> lines they follow (derived boxes
/// would need every model's measured height in the baker), and baked as <c>noFly</c> into the battle block of level.json. The pilot
/// (<c>SdfBattle.NoFlyZones</c>) keeps 4 m off a box, never starts or ends a waypoint inside one and flies round it by the corners.
/// Only helicopters keep to them; scouts, tanks and shots are unaffected.
/// </para>
/// <para>
/// <c>zone</c> lines (ticket 29.7, any number) give a ground region a rule: <c>zone suds slick</c> (players slide: ground acceleration x0.25,
/// braking x0.1, top speed x1.3; bots only get x1.2 top speed) or <c>zone puddle wet</c> (a scout in it, or out of it less than 2 s ago,
/// takes x1.5 damage from the electro gun). The region is one of the <c>regions</c> (or painted by a placement's <c>ground=</c>); a region
/// may have both rules, one line each, and a rule is given once per region. They are baked as a byte mask over the playable area, 1 m
/// cells (zones.bin, described under <c>zones</c> in level.json), read at runtime by position: no colliders, no navmesh change. Where
/// regions overlap the later region wins, as it is drawn. A level without <c>zone</c> lines bakes no mask.
/// </para>
/// <para>
/// A <c>mover</c> (ticket 27.3; there may be several, each with its own name) is a vehicle on a fixed timetable: <c>mover train speed=7
/// gap=0.5 phase=0</c>, with below it its closed path, <c>Stadium center=(0, 0) halfLength=12 radius=20</c> (straights along z at
/// x = centre ± radius, half-circle ends; anticlockwise from above, arc length 0 at the east straight's south end) or <c>Polyline
/// points=[(-10, 0), (0, 8), (10, 0)]</c> (corners, closed); optionally <c>heights keys=[(0, 0), (30, 3.8)]</c>, (arc length, y) keys
/// of the cars' height above y = 0, linear between them and round the loop (0 everywhere by default); and its cars, head first, one
/// <c>car toy-train-engine length=4.8</c> per line (a model from content/models/ and its length along the path). Its position is
/// <c>speed * (round clock + phase)</c> metres along the path, <c>phase</c> in seconds, so nothing is saved; the cars follow the path's
/// centre line, <c>gap</c> metres apart (0.5 by default). The train must be shorter than the path. See <see cref="MoverSpec"/>.
/// Further options of the <c>mover</c> line (ticket 27.10, all off by default): <c>pushes=3 damage=20</c> make a scout or tank in a car's way
/// get pushed that many metres off its side and take that much damage, at most once per car and victim in <c>cooldown=1</c> seconds, and
/// <c>name="Toy train"</c> is how the kill feed names a kill by it (no weapon, credit or spores; the mover's own name, capitalised, by default);
/// <c>puff=1 puffAt=(0, 2.7, 1.7)</c> gives a puff from the first car every second at that point of its frame, <c>whistle=2</c> one that many
/// seconds before each climb of the path (a bridge); <c>crossing=25</c> makes a bot whose path crosses the track wait at its edge while the
/// head is within 25 m of the crossing, or the train is on it.
/// </para>
/// <para>
/// The sides (<see cref="BattleLayout"/>) list <c>west</c> and <c>east</c>, each with exactly one <c>spawn</c>, one <c>flag</c> and one
/// or more weapon <c>pod</c>s and one or more <c>pod health</c>, below it. A side's weapon pods are any of minigun, pistol, sniper,
/// electro-gun, flamethrower, poison-gun and blob-gun, each at most once, and both sides have the same ones (most levels give each side a
/// minigun and a pistol). <c>neutral</c> lists exactly two pods, one of sniper or electro-gun and one of flamethrower, poison-gun or
/// blob-gun. Each takes <c>at</c> (x, z), standing on the ground there like a placement, and optionally
/// <c>yaw</c>; a spawn also <c>radius</c>, its spawn zone (3 m by default), within which the side's players appear. A level has both
/// sections or neither. A level with machines (ticket 24) gives each side exactly four <c>turret</c> lines (<c>at</c>, <c>yaw</c>: where
/// the turret stands and the way it faces at rest) and one <c>machines</c> line (<c>at</c>, <c>yaw</c>, <c>radius</c>, 3 m by default:
/// the zone where the side's helicopter and tanks start and respawn); both sides have them or neither.
/// </para>
/// </summary>
public static class LevelFile
{
    public const int FormatVersion = 1;

    public const string Extension = ".level";

    /// <summary>The folder of the content folder that holds the levels: content/levels/.</summary>
    public const string FolderName = "levels";

    private const string Play = "play";
    private const string Seed = "seed";
    private const string Hills = "hills";
    private const string Edge = "edge";
    private const string Regions = "regions";
    private const string Place = "place";
    private const string Scatter = "scatter";
    private const string Trees = "trees";
    private const string Draw = "draw";
    private const string Sky = "sky";
    private const string Room = "room";
    private const string Fog = "fog";
    private const string Steam = "steam";
    private const string Drip = "drip";
    private const string Ring = "ring";
    private const string Noise = "noise";
    private const string Underside = "underside";
    private const string Sides = "sides";
    private const string Neutral = "neutral";
    private const string Spawn = "spawn";
    private const string Turret = "turret";
    private const string Machines = "machines";
    private const string NoFly = "nofly";
    private const string Zone = "zone";
    private const string Flag = "flag";
    private const string Pod = "pod";
    private const string Mover = "mover";

    private static readonly string[] Sections = [Play, Seed, Hills, Edge, Regions, Place, Scatter, Trees, Draw, Sky, Room, Fog, Steam, Drip, Sides, Neutral, Machines, NoFly, Zone, Mover];

    /// <summary>The sections that may be given more than once, each time with something else: a scatter or trees per layer, a draw per group.</summary>
    private static readonly string[] Repeatable = [Scatter, Trees, Draw, Mover, Steam, Drip, NoFly, Zone];

    private static readonly string[] ShapeTypes = ["Circle", "Box2D", "RoundedBox2D", "Polyline"];

    /// <summary>"content/levels/meadow.level" is the level meadow.</summary>
    public static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    public static LevelDefinition Load(string path) => Read(File.ReadAllText(path));

    /// <summary>Every level file in the directory and its subdirectories, in name order.</summary>
    public static IReadOnlyList<string> All(string directory) =>
        Directory.EnumerateFiles(directory, "*" + Extension, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <exception cref="SdfProgramFileException">The text is not a valid level; the exception names the line.</exception>
    public static LevelDefinition Read(string text)
    {
        var lines = SourceLine.Parse(text);
        if (lines.Count == 0)
            throw new SdfProgramFileException(1, $"The file is empty, it must start with '{FileSyntax.FormatKeyword} {FormatVersion}'");
        ReadFormat(lines[0]);

        PlayArea? play = null;
        var seed = LevelDefinition.DefaultSeed;
        var hills = HillRing.None;
        var edge = IslandEdge.None;
        IReadOnlyList<GroundRegion> regions = [];
        var baseGround = LevelDefinition.DefaultBase;
        IReadOnlyList<Placement> placements = [];
        var scatters = new List<ScatterLayer>();
        var trees = new List<ScatterLayer>();
        var drawGroups = new List<DrawGroup>();
        SkySection? sky = null;
        RoomSpec? room = null;
        var roomLine = 0;
        FogSpec? fog = null;
        var steam = new List<SteamSpec>();
        var drips = new List<DripSpec>();
        var noFly = new List<NoFlySpec>();
        var zones = new List<ZoneSpec>();
        MachineSet? machineSet = null;
        var machineSetLine = 0;
        var movers = new List<MoverSpec>();
        (BattleSide West, BattleSide East)? sides = null;
        IReadOnlyList<BattleObject>? neutral = null;
        var seen = new Dictionary<string, int>();
        foreach (var line in lines.Skip(1))
        {
            var cursor = new Cursor(line);
            var section = cursor.ReadName($"a section: {string.Join(", ", Sections)}");
            if (!Sections.Contains(section))
                throw cursor.Error($"A level has no section '{section}'; its sections are {string.Join(", ", Sections)}");
            if (seen.TryGetValue(section, out var first) && !Repeatable.Contains(section))
                throw cursor.Error($"'{section}' is given twice, first on line {first}");
            seen[section] = line.Number;

            switch (section)
            {
                case Play:
                    play = ReadPlay(line, cursor);
                    break;
                case Seed:
                    seed = ReadSeed(line, cursor);
                    break;
                case Hills:
                    hills = ReadHills(line, cursor);
                    break;
                case Edge:
                    edge = ReadEdge(line, cursor);
                    break;
                case Regions:
                    (regions, baseGround) = ReadRegions(line, cursor);
                    break;
                case Place:
                    placements = ReadPlacements(line, cursor);
                    break;
                case Scatter:
                    scatters.Add(ReadScatter(line, cursor, trees: false));
                    break;
                case Trees:
                    trees.Add(ReadScatter(line, cursor, trees: true));
                    break;
                case Draw:
                    var group = ReadDrawGroup(line, cursor);
                    if (drawGroups.FirstOrDefault(g => g.Name == group.Name) is { } earlier)
                        throw cursor.Error($"Draw group '{group.Name}' is given twice, first on line {earlier.Line}");
                    drawGroups.Add(group);
                    break;
                case Sky:
                    sky = SkyFile.ReadSection(line, cursor);
                    break;
                case Room:
                    room = ReadRoom(line, cursor);
                    roomLine = line.Number;
                    break;
                case Fog:
                    fog = ReadFog(line, cursor);
                    break;
                case Steam:
                    steam.Add(ReadSteam(line, cursor, steam.Sum(s => s.Count)));
                    break;
                case Drip:
                    drips.Add(ReadDrip(line, cursor));
                    break;
                case Sides:
                    sides = ReadSides(line, cursor);
                    break;
                case Neutral:
                    neutral = ReadNeutral(line, cursor);
                    break;
                case Mover:
                    var mover = ReadMover(line, cursor);
                    if (movers.FirstOrDefault(m => m.Name == mover.Name) is { } twin)
                        throw new SdfProgramFileException(line.Number, $"Mover '{mover.Name}' is given twice, first on line {twin.Line}");
                    movers.Add(mover);
                    break;
                case NoFly:
                    noFly.Add(ReadNoFly(line, cursor));
                    break;
                case Zone:
                    var zone = ReadZone(line, cursor);
                    if (zones.FirstOrDefault(z => z.Region == zone.Region && z.Rule == zone.Rule) is { } sameZone)
                        throw new SdfProgramFileException(line.Number, $"The region '{zone.Region}' is given the rule {ZoneRuleName(zone.Rule)} twice, first on line {sameZone.Line}");
                    zones.Add(zone);
                    break;
                case Machines:
                    machineSet = ReadMachineSet(line, cursor);
                    machineSetLine = line.Number;
                    break;
            }
        }
        if ((sides is null) != (neutral is null))
            throw new SdfProgramFileException(seen[sides is null ? Neutral : Sides],
                $"A level with battle sides has neutral pods between them, and the other way round; add a '{(sides is null ? Sides : Neutral)}' section");

        if (machineSet is not null)
        {
            if (sides is null || sides.Value.West.MachineSpawn is null)
                throw new SdfProgramFileException(machineSetLine,
                    $"The '{Machines}' line names the models of the sides' machines, but the level's sides have no turrets and machine zone; add them to the sides, or leave the line out");
            if (machineSet.Ceiling is not null && room is null)
                throw new SdfProgramFileException(machineSetLine, $"'ceiling' of {Machines} is a closed room's flight ceiling; the level has no '{Room}' line");
            if (machineSet.Ceiling is { } ceiling && room is not null && ceiling > room.Size.Y - 2)
                throw new SdfProgramFileException(machineSetLine,
                    $"'ceiling' of {Machines} ({FileSyntax.Number(ceiling)}) must be 2 m or more under the room's lid ({FileSyntax.Number(room.Size.Y)} m high)");
            if (room is not null && machineSet.CeilingIn(room) is { } least && least < MachineSet.LeastCeiling)
                throw new SdfProgramFileException(machineSetLine,
                    $"The helicopters' flight ceiling ({FileSyntax.Number(least)} m) is under {FileSyntax.Number(MachineSet.LeastCeiling)} m, too low to clear a base; the room is too low, or give a higher 'ceiling'");
        }

        // A zone names a region of the level: a listed one, or one a placement paints
        foreach (var zone in zones)
            if (regions.All(r => r.Name != zone.Region) && placements.All(p => p.Ground != zone.Region))
                throw new SdfProgramFileException(zone.Line,
                    $"The zone names the region '{zone.Region}', which the level does not have; its regions are {string.Join(", ", regions.Select(r => r.Name))}");

        foreach (var scatter in scatters.Concat(trees))
            if (drawGroups.All(g => g.Name != scatter.Group))
                throw new SdfProgramFileException(scatter.Line,
                    $"The {(scatter.Band is null ? "scatter" : "trees section")} draws in group '{scatter.Group}', which has no '{Draw} {scatter.Group}' section saying how to draw it");

        if (play is null)
            throw new SdfProgramFileException(lines[^1].Number, $"The level has no playable area; add a line like '{Play} size=(150, 100)'");
        if (room is not null && (MathF.Abs(room.Size.X - play.Size.X) > 1e-3f || MathF.Abs(room.Size.Z - play.Size.Y) > 1e-3f))
            throw new SdfProgramFileException(roomLine,
                $"The room's walls stand on the playable area's edges; its size ({FileSyntax.Number(room.Size.X)}, {FileSyntax.Number(room.Size.Z)}) across and along must be the play size ({FileSyntax.Number(play.Size.X)}, {FileSyntax.Number(play.Size.Y)})");
        return new LevelDefinition(play, seed, hills, edge) { Regions = regions, Base = baseGround, Placements = placements, Scatters = scatters, Trees = trees, DrawGroups = drawGroups, Sky = sky, Room = room, Fog = fog, Steam = steam, Drips = drips, Machines = machineSet, NoFly = noFly, Zones = zones, Movers = movers,
            Battle = sides is { } s ? new BattleLayout(s.West, s.East, neutral!) : null };
    }

    private static (BattleSide West, BattleSide East) ReadSides(SourceLine line, Cursor cursor)
    {
        cursor.ExpectEnd();
        var sides = new Dictionary<string, BattleSide>();
        var lines = new Dictionary<string, int>();
        foreach (var child in line.Children)
        {
            var childCursor = new Cursor(child);
            var name = childCursor.ReadName($"a side, {BattleSide.West} or {BattleSide.East}");
            if (name is not (BattleSide.West or BattleSide.East))
                throw childCursor.Error($"The sides are {BattleSide.West} and {BattleSide.East}; there is no side '{name}'");
            if (lines.TryGetValue(name, out var first))
                throw childCursor.Error($"Side {name} is given twice, first on line {first}");
            lines[name] = child.Number;
            childCursor.ExpectEnd();
            sides[name] = ReadSide(name, child, childCursor);
        }
        foreach (var name in new[] { BattleSide.West, BattleSide.East })
            if (!sides.ContainsKey(name))
                throw cursor.Error($"The sides need both {BattleSide.West} and {BattleSide.East} indented below them; {name} is missing");
        // Machines (ticket 24) on both sides or on neither
        var (west, east) = (sides[BattleSide.West], sides[BattleSide.East]);
        if ((west.MachineSpawn is null) != (east.MachineSpawn is null))
        {
            var (with, without) = west.MachineSpawn is null ? (east, west) : (west, east);
            throw new SdfProgramFileException(lines[without.Name],
                $"Side {with.Name} has machines (turrets and a machine zone), so side {without.Name} needs them too: {BattleLayout.TurretsPerSide} '{Turret}' lines and a '{Machines}' line");
        }
        // The sides mirror each other, so they have the same weapon pods
        var (westWeapons, eastWeapons) = (west.Weapons.Select(p => p.Pod!).ToArray(), east.Weapons.Select(p => p.Pod!).ToArray());
        if (!westWeapons.SequenceEqual(eastWeapons))
            throw new SdfProgramFileException(lines[BattleSide.East],
                $"The sides mirror each other, so they have the same weapon pods; west has {string.Join(", ", westWeapons)}, east {string.Join(", ", eastWeapons)}");
        return (west, east);
    }

    /// <summary>
    /// A side's objects: exactly one spawn and one flag, one or more weapon pods, each a different one of
    /// <see cref="BattleLayout.Weapons"/> (kept in that list's order), and one or more health pods (those after the first are
    /// <see cref="BattleSide.MoreHealth"/>); and, on a level with
    /// machines (ticket 24), exactly <see cref="BattleLayout.TurretsPerSide"/> turrets and one machine zone.
    /// </summary>
    private static BattleSide ReadSide(string name, SourceLine line, Cursor cursor)
    {
        var objects = new Dictionary<string, BattleObject>();
        var turrets = new List<BattleObject>();
        var moreHealth = new List<BattleObject>();
        var weapons = new List<BattleObject>();
        foreach (var child in line.Children)
        {
            var item = ReadBattleObject($"side {name}", child, [Spawn, Flag, Pod, Turret, Machines]);
            if (item.Kind == BattleKind.Pod && item.Pod != BattleLayout.Health && !BattleLayout.Weapons.Contains(item.Pod))
                throw new SdfProgramFileException(child.Number,
                    $"A side's pods are {BattleLayout.Health} and the weapons {string.Join(", ", BattleLayout.Weapons)}; {item.Pod} is none of them");
            if (item.Kind == BattleKind.Turret)
            {
                if (turrets.Count == BattleLayout.TurretsPerSide)
                    throw new SdfProgramFileException(child.Number,
                        $"Side {name} has {BattleLayout.TurretsPerSide} turrets already (lines {string.Join(", ", turrets.Select(t => t.Line))}); a side has exactly {BattleLayout.TurretsPerSide}");
                turrets.Add(item);
                continue;
            }
            if (item.Kind == BattleKind.Pod && item.Pod == BattleLayout.Health && objects.ContainsKey(item.Name))
            {
                moreHealth.Add(item);
                continue;
            }
            if (objects.TryGetValue(item.Name, out var first))
                throw new SdfProgramFileException(child.Number, $"Side {name} has its {item.Name} twice, first on line {first.Line}");
            objects[item.Name] = item;
            if (item.Kind == BattleKind.Pod && item.Pod != BattleLayout.Health)
                weapons.Add(item);
        }
        BattleObject Get(string what) => objects.GetValueOrDefault(what)
            ?? throw cursor.Error($"Side {name} has no {what}; a side has a {Spawn}, a {Flag}, one or more weapon pods and one or more health pods");
        var machines = objects.GetValueOrDefault(Machines);
        if (machines is null && turrets.Count > 0)
            throw cursor.Error($"Side {name} has turrets but no machine zone; a side with machines has a '{Machines}' line, like '{Machines} at=(-70, 22) radius=3'");
        if (machines is not null && turrets.Count < BattleLayout.TurretsPerSide)
            throw cursor.Error($"Side {name} has {turrets.Count} turrets; a side with machines has exactly {BattleLayout.TurretsPerSide} '{Turret}' lines");
        var spawn = Get(Spawn);
        var flag = Get(Flag);
        if (weapons.Count == 0)
            throw cursor.Error($"Side {name} has no weapon pod; a side has one or more, of {string.Join(", ", BattleLayout.Weapons)}, like '{Pod} minigun at=(-48, 6)'");
        var ordered = weapons.OrderBy(p => BattleLayout.Weapons.ToList().IndexOf(p.Pod!)).ToArray();
        return new BattleSide(name, spawn, flag, ordered, Get($"{Pod} {BattleLayout.Health}"))
            { MoreHealth = moreHealth, Turrets = turrets, MachineSpawn = machines };
    }

    /// <summary>The neutral pods: exactly one of <see cref="BattleLayout.LongRange"/> and one of <see cref="BattleLayout.ShortRange"/>.</summary>
    private static IReadOnlyList<BattleObject> ReadNeutral(SourceLine line, Cursor cursor)
    {
        cursor.ExpectEnd();
        BattleObject? longRange = null, shortRange = null;
        foreach (var child in line.Children)
        {
            var pod = ReadBattleObject("the neutral pods", child, [Pod]);
            var isLong = BattleLayout.LongRange.Contains(pod.Pod);
            if (!isLong && !BattleLayout.ShortRange.Contains(pod.Pod))
                throw new SdfProgramFileException(child.Number,
                    $"The neutral pods are one of {string.Join(", ", BattleLayout.LongRange)} and one of {string.Join(", ", BattleLayout.ShortRange)}; " +
                    $"{pod.Pod} is none of them");
            ref var slot = ref isLong ? ref longRange : ref shortRange;
            if (slot is not null)
                throw new SdfProgramFileException(child.Number,
                    $"The neutral pods have one {(isLong ? "long-range" : "short-range")} pod, but {slot.Pod} is on line {slot.Line} already");
            slot = pod;
        }
        if (longRange is null || shortRange is null)
            throw cursor.Error($"The neutral pods need one of {string.Join(", ", longRange is null ? BattleLayout.LongRange : BattleLayout.ShortRange)} " +
                               $"indented below them, like '{Pod} {(longRange is null ? BattleLayout.LongRange : BattleLayout.ShortRange)[0]} at=(0, 30)'");
        return [longRange, shortRange];
    }

    /// <summary>
    /// A spawn, flag, pod, turret or machines line: its kind (a pod's pickup after it, like pod minigun), at and yaw, and a zone's
    /// radius.
    /// </summary>
    private static BattleObject ReadBattleObject(string owner, SourceLine line, string[] kinds)
    {
        var cursor = new Cursor(line);
        var what = kinds.Length == 1 ? $"'{Pod}'" : $"{string.Join(", ", kinds.SkipLast(1).Select(k => $"'{k}'"))} or '{kinds[^1]}'";
        var kind = cursor.ReadName(what);
        if (!kinds.Contains(kind))
            throw cursor.Error($"Expected {what} in {owner}, found '{kind}'");
        var pod = kind == Pod ? cursor.ReadFileName("the pod's pickup, like minigun or electro-gun") : null;
        var name = pod is null ? kind : $"{Pod} {pod}";
        var parameters = Parameters.Read(name, line, cursor);
        var at = parameters.Vector2("at", "a point like (-58, 0), x and z in metres");
        var yaw = parameters.Number("yaw", 0, -360, 360, "degrees, -360 to 360");
        if (kind is not (Spawn or Machines))
        {
            parameters.Done("at", "yaw");
            var objectKind = kind switch { Flag => BattleKind.Flag, Turret => BattleKind.Turret, _ => BattleKind.Pod };
            return new BattleObject(objectKind, pod, at, yaw, line.Number);
        }
        // A spawn is a zone: the side's players appear anywhere within its radius; the machine zone the same for its helicopter and tanks
        var (fallback, least, greatest) = kind == Spawn
            ? (BattleObject.DefaultSpawnZone, BattleObject.MinSpawnZone, BattleObject.MaxSpawnZone)
            : (BattleObject.DefaultMachineZone, BattleObject.MinMachineZone, BattleObject.MaxMachineZone);
        var radius = parameters.Number("radius", fallback, least, greatest, $"metres, {FileSyntax.Number(least)} to {FileSyntax.Number(greatest)}");
        parameters.Done("at", "yaw", "radius");
        return new BattleObject(kind == Spawn ? BattleKind.Spawn : BattleKind.Machines, null, at, yaw, line.Number) { ZoneRadius = radius };
    }

    /// <summary>machines set=toys ceiling=20: the machine models and a room's helicopter ceiling (ticket 27.2).</summary>
    private static MachineSet ReadMachineSet(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Machines, line, cursor);
        var name = parameters.Name("set", MachineSet.Default, null)!;
        if (!MachineSet.IsName(name))
            throw parameters.Error("set", $"A machine set is named in lower case letters and digits, not red or blue, like set=toys; '{name}' is not");
        float? ceiling = parameters.Has("ceiling")
            ? parameters.Number("ceiling", null, MachineSet.LeastCeiling, 1000, $"metres over the floor, {FileSyntax.Number(MachineSet.LeastCeiling)} or more")
            : null;
        parameters.Done("set", "ceiling");
        return new MachineSet(name, ceiling);
    }

    private static readonly string[] ZoneRules = ["slick", "wet"];

    private static string ZoneRuleName(ZoneRule rule) => rule == ZoneRule.Slick ? "slick" : "wet";

    /// <summary>zone suds slick: a ground region and its rule (ticket 29.7).</summary>
    private static ZoneSpec ReadZone(SourceLine line, Cursor cursor)
    {
        var region = cursor.ReadName("a region's name, like suds");
        var rule = cursor.ReadName($"the zone's rule: {string.Join(" or ", ZoneRules)}");
        if (!ZoneRules.Contains(rule))
            throw cursor.Error($"A zone has no rule '{rule}'; its rules are {string.Join(" and ", ZoneRules)}");
        cursor.ExpectEnd();
        return new ZoneSpec(region, rule == "slick" ? ZoneRule.Slick : ZoneRule.Wet, line.Number);
    }

    /// <summary>nofly at=(0, 38.75) size=(51, 22.5) yaw=180: a box the helicopters fly round (ticket 29.6).</summary>
    private static NoFlySpec ReadNoFly(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(NoFly, line, cursor);
        var at = parameters.Vector2("at", "a point like (0, 38.75), x and z in metres");
        var size = parameters.Vector2("size", "a size like (51, 22.5), the box's full width and depth in metres");
        var yaw = parameters.Number("yaw", 0, -360, 360, "degrees, like a placed model's yaw");
        parameters.Done("at", "size", "yaw");
        if (size.X <= 0 || size.Y <= 0)
            throw parameters.Error("size", "A no-fly box needs a size of more than 0 in both directions");
        return new NoFlySpec(at, size, yaw, line.Number);
    }

    private static readonly string[] MoverShapes = ["Stadium", "Polyline"];

    /// <summary>
    /// mover train speed=7 gap=0.5 phase=0, with below it the path (<c>Stadium center=(0, 0) halfLength=12 radius=20</c> or
    /// <c>Polyline points=[...]</c>, closed), optionally <c>heights keys=[(0, 0), (6, 3.8)]</c> and the cars, head first, as
    /// <c>car toy-train-engine length=4.8</c> (ticket 27.3).
    /// </summary>
    private static MoverSpec ReadMover(SourceLine line, Cursor cursor)
    {
        var name = cursor.ReadName("the mover's name, like train");
        var parameters = Parameters.Read($"mover {name}", line with { Children = [] }, cursor);
        var speed = parameters.Number("speed", null, 0.1f, 100, "metres per second, 0.1 to 100");
        var gap = parameters.Number("gap", 0.5f, 0, 20, "metres between the cars, 0 to 20");
        var phase = parameters.Number("phase", 0, -100000, 100000, "seconds the timetable is ahead at the round's start");
        // What the cars do to what they meet and what they give off (ticket 27.10); all off by default
        var pushes = parameters.Number("pushes", 0, 0, 50, "metres a scout or tank in a car's way is pushed off its side, 0 to 50");
        var damage = parameters.Number("damage", 0, 0, 1000, "damage of one hit by a car, 0 to 1000");
        var hazardName = parameters.Name("name", null, null);
        var cooldown = parameters.Number("cooldown", 1, 0, 60, "seconds before a car hits the same victim again, 0 to 60");
        var puff = parameters.Number("puff", 0, 0, 600, "seconds between puffs from the first car, 0 (none) to 600");
        var puffAt = parameters.Has("puffAt") ? parameters.Vector3("puffAt", "a point like (0, 2.5, 1.7) in the first car's frame (x right, y up, z forward)") : new Vector3(0, 2.5f, 0);
        var whistle = parameters.Number("whistle", 0, 0, 600, "seconds before each climb a whistle puff is given, 0 (none) to 600");
        var crossing = parameters.Number("crossing", 0, 0, 1000, "metres from the head within which bots wait to cross the track, 0 (they do not) to 1000");
        parameters.Done("speed", "gap", "phase", "pushes", "damage", "name", "cooldown", "puff", "puffAt", "whistle", "crossing");

        IReadOnlyList<Vector2>? path = null;
        SourceLine? pathLine = null;
        (IReadOnlyList<Vector2> Keys, SourceLine Line)? heights = null;
        var cars = new List<MoverCar>();
        foreach (var child in line.Children)
        {
            var childCursor = new Cursor(child);
            var entry = childCursor.ReadName($"the path ({string.Join(" or ", MoverShapes)}), 'heights' or 'car'");
            if (entry is "Stadium" or "Polyline")
            {
                if (path is not null)
                    throw childCursor.Error($"Mover {name} has its path twice, first on line {pathLine!.Number}");
                var shape = Parameters.Read($"mover {name}'s {entry}", child, childCursor);
                if (entry == "Stadium")
                {
                    var center = shape.Vector2("center", new Vector2(0, 0), "a point like (0, 0)");
                    var halfLength = shape.Number("halfLength", null, 0, 1000, "half the straights' length in metres, 0 to 1000");
                    var radius = shape.Number("radius", null, 1, 1000, "the ends' radius in metres, 1 to 1000");
                    shape.Done("center", "halfLength", "radius");
                    path = MoverSpec.Stadium(center, halfLength, radius);
                }
                else
                {
                    var points = shape.Points("points", "corners like [(-10, 0), (0, 8), (10, 0)] (x, z in metres), closed by a leg back to the first");
                    shape.Done("points");
                    if (points.Length < 3)
                        throw childCursor.Error($"Mover {name}'s polyline needs three corners or more to be a loop");
                    path = points;
                }
                pathLine = child;
            }
            else if (entry == "heights")
            {
                if (heights is not null)
                    throw childCursor.Error($"Mover {name} has its heights twice, first on line {heights.Value.Line.Number}");
                var keys = Parameters.Read($"mover {name}'s heights", child, childCursor);
                var list = keys.Points("keys", "(arc length, y) keys like [(0, 0), (30, 3.8)], metres");
                keys.Done("keys");
                heights = (list, child);
            }
            else if (entry == "car")
            {
                var model = childCursor.ReadFileName("a model's name, like toy-train-engine");
                var car = Parameters.Read($"mover {name}'s car {model}", child, childCursor);
                var length = car.Number("length", null, 0.1f, 200, "the car's length along the path in metres, 0.1 to 200");
                car.Done("length");
                cars.Add(new MoverCar(model, length, child.Number));
            }
            else
                throw childCursor.Error($"A mover has a path ({string.Join(" or ", MoverShapes)}), 'heights' and 'car' lines; there is no '{entry}'");
        }

        if (path is null)
            throw cursor.Error($"Mover {name} needs a path indented below it, like 'Stadium center=(0, 0) halfLength=12 radius=20'");
        if (cars.Count == 0)
            throw cursor.Error($"Mover {name} needs one or more cars indented below it, like 'car toy-train-engine length=4.8'");
        var length1 = MoverSpec.PathLength(path);
        var keyList = heights?.Keys ?? [];
        var previous = -1f;
        foreach (var key in keyList)
        {
            if (key.X < 0 || key.X >= length1)
                throw new SdfProgramFileException(heights!.Value.Line.Number,
                    $"Mover {name}'s height key at arc length {FileSyntax.Number(key.X)} is off the path, which is {FileSyntax.Number(length1)} m long (keys are from 0 up to its length)");
            if (key.X <= previous)
                throw new SdfProgramFileException(heights!.Value.Line.Number, $"Mover {name}'s height keys must go along the path, each arc length more than the one before");
            if (key.Y is < -1000 or > 1000)
                throw new SdfProgramFileException(heights!.Value.Line.Number, $"Mover {name}'s height {FileSyntax.Number(key.Y)} is not a height in metres");
            previous = key.X;
        }
        var spec = new MoverSpec(name, speed, phase, gap, path, keyList, cars, line.Number)
        {
            Pushes = pushes, Damage = damage, DisplayName = hazardName, Cooldown = cooldown, Puff = puff, PuffAt = puffAt, Whistle = whistle, Crossing = crossing,
        };
        if (spec.TrainLength > length1 - 1)
            throw new SdfProgramFileException(line.Number,
                $"Mover {name}'s cars and gaps take {FileSyntax.Number(spec.TrainLength)} m, but the path is {FileSyntax.Number(length1)} m round; the train would meet itself");
        return spec;
    }

    private static readonly string[] GlowWalls = ["north", "south", "east", "west"];

    private static RoomSpec ReadRoom(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Room, line, cursor);
        var size = parameters.Vector3("size", "a size like (160, 160, 160), width, height and depth in metres");
        var walls = parameters.Name("walls", null, null);
        var ceiling = parameters.Name("ceiling", null, null);
        if ((walls is null) != (ceiling is null))
            throw parameters.Error(walls is null ? "ceiling" : "walls", "The room needs both walls= and ceiling= (the materials of its faces, materials/room/<name>.sdfmat), or neither for the chessboard's checker");
        if (walls is not null && parameters.Has("square"))
            throw parameters.Error("square", "A room with walls= and ceiling= has no checker; leave out square");
        var square = parameters.Number("square", RoomSpec.DefaultSquare, 0.5f, 100, "a square's side in metres, 0.5 to 100");
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw parameters.Error("size", "The room needs a positive width, height and depth, like size=(160, 160, 160)");
        if (walls is null)
            foreach (var length in new[] { size.X, size.Y, size.Z })
                if (MathF.Abs(length / square - MathF.Round(length / square)) > 1e-3f)
                    throw parameters.Error("size", $"The room's sides must be whole numbers of squares of {FileSyntax.Number(square)} m");

        // glow=north height=3 color=(1, 0.82, 0.5): a band of light along the top of a wall
        var glowWall = parameters.Name("glow", null, GlowWalls);
        var hasHeight = parameters.Has("height");
        var hasColor = parameters.Has("color");
        var glowHeight = parameters.Number("height", RoomGlow.DefaultHeight, 0.1f, size.Y, $"metres, 0.1 up to the room's height ({FileSyntax.Number(size.Y)})");
        var glowColor = hasColor ? Colour(parameters, "color") : RoomGlow.DefaultColor;
        if (glowWall is null && (hasHeight || hasColor))
            throw parameters.Error(hasHeight ? "height" : "color", "height= and color= belong to glow=<north, south, east or west>");
        RoomGlow? glow = glowWall is null ? null : new RoomGlow(glowWall switch
        {
            "north" => RoomFace.PosZ,
            "south" => RoomFace.NegZ,
            "east" => RoomFace.PosX,
            _ => RoomFace.NegX,
        }, glowHeight, glowColor);

        // light=(0, 0.77, 0.64) lightColor=(1, 0.95, 0.7) ambient=(0.4, 0.35, 0.5): the lamp's direction (towards it) and hue, the ambient
        Vector3? direction = null;
        if (parameters.Has("light"))
        {
            direction = parameters.Vector3("light", "the direction towards the lamp, like (0, 0.77, 0.64)");
            if (direction.Value.LengthSquared() < 1e-6f)
                throw parameters.Error("light", "The lamp's direction needs a length, like (0, 0.77, 0.64)");
        }
        Vector3? lampColor = parameters.Has("lightColor") ? Colour(parameters, "lightColor") : null;
        Vector3? ambient = parameters.Has("ambient") ? Colour(parameters, "ambient") : null;
        parameters.Done("size", "square", "walls", "ceiling", "glow", "height", "color", "light", "lightColor", "ambient");
        return new RoomSpec(size, square)
        {
            Walls = walls,
            Ceiling = ceiling,
            Glow = glow,
            Light = direction is null && lampColor is null && ambient is null ? null : new RoomLighting(direction, lampColor, ambient),
        };
    }

    /// <summary>fog density=0.0075 color=(0.9, 0.93, 0.92): the haze (ticket 29.1).</summary>
    private static FogSpec ReadFog(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Fog, line, cursor);
        var density = parameters.Number("density", null, FogSpec.MinDensity, FogSpec.MaxDensity,
            $"a density from {FileSyntax.Number(FogSpec.MinDensity)} to {FileSyntax.Number(FogSpec.MaxDensity)} (0.0075 is 57% clear at 100 m)");
        var color = Colour(parameters, "color");
        parameters.Done("density", "color");
        return new FogSpec(density, color);
    }

    /// <summary>steam at=(-15, 16.5, 0) size=8 rise=40 count=22: a plume of steam (ticket 29.1); <paramref name="before"/> puffs are already spoken for.</summary>
    private static SteamSpec ReadSteam(SourceLine line, Cursor cursor, int before)
    {
        var parameters = Parameters.Read(Steam, line, cursor);
        var at = parameters.Vector3("at", "a point like (-15, 16.5, 0), x, height and z in metres");
        var size = parameters.Number("size", null, 0.1f, 50, "the puffs' width in metres, 0.1 to 50");
        var rise = parameters.Number("rise", null, 0.1f, 200, "how far the puffs rise in metres, 0.1 to 200");
        var count = parameters.Count("count", SteamSpec.DefaultCount, LevelFx.MaxSteamParticles, $"a whole number of puffs alive at a time, 1 to {LevelFx.MaxSteamParticles}");
        parameters.Done("at", "size", "rise", "count");
        if (count < 1)
            throw parameters.Error("count", "A plume needs at least one puff");
        if (before + count > LevelFx.MaxSteamParticles)
            throw parameters.Error("count", $"The level's steam holds at most {LevelFx.MaxSteamParticles} puffs in all; this plume would make it {before + count}");
        return new SteamSpec(at, size, rise, count, line.Number);
    }

    /// <summary>drip at=(0, 57, 49.5) water=16.5 every=2 size=0.15: a drop falling into water (ticket 29.1).</summary>
    private static DripSpec ReadDrip(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Drip, line, cursor);
        var at = parameters.Vector3("at", "a point like (0, 57, 49.5), x, height and z in metres");
        var water = parameters.Number("water", null, -1000, 1000, "the height the drop lands at in metres");
        var every = parameters.Number("every", DripSpec.DefaultEvery, 0.2f, 60, "seconds between drops, 0.2 to 60");
        var size = parameters.Number("size", DripSpec.DefaultSize, 0.01f, 5, "the drop's width in metres, 0.01 to 5");
        parameters.Done("at", "water", "every", "size");
        if (water >= at.Y)
            throw parameters.Error("water", $"The drop falls from {FileSyntax.Number(at.Y)} m, so 'water' must be lower than that");
        return new DripSpec(at, water, every, size, line.Number);
    }

    /// <summary>A colour like (1, 0.82, 0.5), sRGB with each part 0 to 1.</summary>
    private static Vector3 Colour(Parameters parameters, string name)
    {
        var color = parameters.Vector3(name, "a colour like (1, 0.82, 0.5), each part 0 to 1");
        return color.X is < 0 or > 1 || color.Y is < 0 or > 1 || color.Z is < 0 or > 1
            ? throw parameters.Error(name, $"'{name}' needs a colour like (1, 0.82, 0.5), each part 0 to 1")
            : color;
    }

    private static PlayArea ReadPlay(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Play, line, cursor);
        var size = parameters.Vector2("size", "a size like (150, 100)");
        if (size.X <= 0 || size.Y <= 0)
            throw parameters.Error("size", "The playable area needs a positive width and length, like size=(150, 100)");
        var noise = parameters.Number("noise", PlayArea.DefaultNoiseAmplitude, 0, PlayArea.MaxNoiseAmplitude,
            $"metres of unevenness, 0 to {FileSyntax.Number(PlayArea.MaxNoiseAmplitude)} so the playable area stays easy to walk");
        var noiseScale = parameters.Number("noiseScale", PlayArea.DefaultNoiseScale, 1, 1000, "the width of the bumps in metres, 1 to 1000");
        parameters.Done("size", "noise", "noiseScale");
        return new PlayArea(size, noise, noiseScale);
    }

    private static int ReadSeed(SourceLine line, Cursor cursor)
    {
        if (cursor.ReadValue() is not RawNumber { IsWhole: true } number
            || !int.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
            throw cursor.Error("The seed is a whole number, like 'seed 1'");
        cursor.ExpectEnd();
        NothingBelow(line, Seed);
        return seed;
    }

    private static HillRing ReadHills(SourceLine line, Cursor cursor)
    {
        cursor.ExpectEnd();
        (float Width, float Height)? ring = null;
        var (amplitude, scale) = (0f, HillRing.DefaultNoiseScale);
        var seen = new Dictionary<string, int>();
        foreach (var child in line.Children)
        {
            var childCursor = new Cursor(child);
            var entry = childCursor.ReadName($"'{Ring}' or '{Noise}'");
            if (entry is not (Ring or Noise))
                throw childCursor.Error($"The hills have no '{entry}'; they have a {Ring} (width, height) and {Noise} (amplitude, scale)");
            if (seen.TryGetValue(entry, out var first))
                throw childCursor.Error($"The hills' {entry} is given twice, first on line {first}");
            seen[entry] = child.Number;

            var parameters = Parameters.Read($"the hills' {entry}", child, childCursor);
            if (entry == Ring)
            {
                ring = (parameters.Number("width", null, 1, 1000, "the ring's width in metres, 1 to 1000"),
                        parameters.Number("height", null, 0, 500, "the crest's height in metres, 0 to 500"));
                parameters.Done("width", "height");
            }
            else
            {
                amplitude = parameters.Number("amplitude", null, 0, 100, "metres of unevenness, 0 to 100");
                scale = parameters.Number("scale", HillRing.DefaultNoiseScale, 1, 1000, "the width of the bumps in metres, 1 to 1000");
                parameters.Done("amplitude", "scale");
            }
        }

        return ring is { } r
            ? new HillRing(r.Width, r.Height, amplitude, scale)
            : throw cursor.Error($"The hills need a ring indented below them, like '{Ring} width=28 height=10'");
    }

    private static IslandEdge ReadEdge(SourceLine line, Cursor cursor)
    {
        // Below it: lines of parameters, and the underside's model on a line of its own (a model's name may have hyphens, which a
        // parameter's value may not)
        IslandUnderside? underside = null;
        var parameterLines = new List<SourceLine>();
        foreach (var child in line.Children)
        {
            var probe = new Cursor(child);
            if (probe.TryReadName() != Underside || probe.Peek() == '=')
            {
                parameterLines.Add(child);
                continue;
            }
            var childCursor = new Cursor(child);
            childCursor.ReadName(Underside);
            if (underside is not null)
                throw childCursor.Error($"The edge's {Underside} is given twice, first on line {underside.Line}");
            var model = childCursor.ReadFileName("the underside's model, like island-underside");
            childCursor.ExpectEnd();
            if (child.Children.Count > 0)
                throw new SdfProgramFileException(child.Children[0].Number, $"Nothing can be indented below the {Underside}");
            underside = new IslandUnderside(model, child.Number);
        }

        var parameters = Parameters.Read(Edge, line with { Children = parameterLines }, cursor);
        var margin = parameters.Number("margin", null, 0, 1000, "metres behind the hills' crest, 0 to 1000");
        float? rim = parameters.Has("rim") ? parameters.Number("rim", null, -1000, 1000, "the ground's height at the edge in metres, -1000 to 1000") : null;
        var shoulderGiven = parameters.Has("shoulder");
        var shoulder = parameters.Number("shoulder", IslandEdge.DefaultShoulder, 0.1f, 100,
            "metres in from the edge over which the ground bends down to the rim, 0.1 to 100");
        parameters.Done("margin", "rim", "shoulder");
        if (rim is null && shoulderGiven)
            throw parameters.Error("shoulder", "A shoulder bends the ground down to a rim; give the edge a rim too, like rim=3");
        if (rim is not null && shoulder > margin)
            throw parameters.Error(shoulderGiven ? "shoulder" : "rim",
                $"The shoulder ({FileSyntax.Number(shoulder)} m) is wider than the margin behind the crest ({FileSyntax.Number(margin)} m)");
        if (underside is not null && rim is null)
            throw new SdfProgramFileException(underside.Line, "The underside meets the ground along the rim; give the edge a rim, like rim=3");
        return new IslandEdge(margin) { Rim = rim, Shoulder = shoulder, Underside = underside };
    }

    private static (IReadOnlyList<GroundRegion> Regions, string Base) ReadRegions(SourceLine line, Cursor cursor)
    {
        // The regions line's own parameters: base=moss, the ground outside the regions (materials/ground/ground_moss.sdfmat)
        var baseParameters = Parameters.Read(Regions, line with { Children = [] }, cursor);
        var baseGround = baseParameters.Name("base", LevelDefinition.DefaultBase, null)!;
        baseParameters.Done("base");
        var regions = new List<GroundRegion>();
        var seen = new Dictionary<string, int>();
        foreach (var child in line.Children)
        {
            var childCursor = new Cursor(child);
            var name = childCursor.ReadName("a region's name, like dirt; its material is materials/ground/ground_dirt.sdfmat");
            if (seen.TryGetValue(name, out var first))
                throw childCursor.Error($"Region '{name}' is given twice, first on line {first}; give it more shapes instead");
            seen[name] = child.Number;
            if (regions.Count == GroundRegions.MaxRegions)
                throw childCursor.Error($"A level has at most {GroundRegions.MaxRegions} regions besides grass");

            // The region's own parameters are on its line; its shapes are indented below it
            var parameters = Parameters.Read($"region {name}", child with { Children = [] }, childCursor);
            var pattern = parameters.Name("pattern", name == GroundRegion.Tiles ? "stones" : "plain", ["plain", "stones"]) == "stones"
                ? GroundPattern.Stones
                : GroundPattern.Plain;
            var stone = parameters.Number("stone", GroundRegion.DefaultStoneSize, 0.1f, 10, "the stones' length in metres, 0.1 to 10");
            var grout = parameters.Number("grout", GroundRegion.DefaultGroutWidth, 0, 0.5f, "the grout's width in metres, 0 to 0.5");
            var wobble = parameters.Number("wobble", GroundRegion.DefaultWobble, 0, 0.5f, "metres the shader wobbles the edge, 0 (straight) to 0.5");
            parameters.Done("pattern", "stone", "grout", "wobble");
            if (child.Children.Count == 0)
                throw childCursor.Error($"Region '{name}' needs shapes indented below it, like 'Circle center=(0, 0) radius=10'");
            regions.Add(new GroundRegion(name, child.Children.Select(ReadShape).ToArray(), pattern, stone, grout, wobble));
        }
        return regions.Count > 0 ? (regions, baseGround) : throw cursor.Error("The regions need a region indented below them, like 'dirt'");
    }

    private static RegionShape ReadShape(SourceLine line)
    {
        var cursor = new Cursor(line);
        var type = cursor.ReadName($"a shape: {string.Join(", ", ShapeTypes)}");
        if (!ShapeTypes.Contains(type))
            throw cursor.Error($"A region has no shape '{type}'; its shapes are {string.Join(", ", ShapeTypes)}");
        var parameters = Parameters.Read(type, line, cursor);
        var noise = parameters.Number("noise", 0, 0, 100, "metres the edge wobbles, 0 to 100");
        var noiseScale = parameters.Number("noiseScale", RegionShape.DefaultNoiseScale, 0.1f, 1000, "the width of the wobbles in metres, 0.1 to 1000");
        RegionShape shape;
        switch (type)
        {
            case "Circle":
                shape = new CircleRegion(parameters.Vector2("center", "a centre like (-60, 0)"),
                    parameters.Number("radius", null, 0, 10000, "a radius in metres"), noise, noiseScale);
                parameters.Done("center", "radius", "noise", "noiseScale");
                break;
            case "Polyline":
                var points = parameters.Points("points", "points like [(-45, 0), (-10, 6), (20, -4)]");
                shape = new PolylineRegion(points, parameters.Number("width", null, 0, 1000, "a width in metres"), noise, noiseScale);
                parameters.Done("points", "width", "noise", "noiseScale");
                break;
            default:
                var center = parameters.Vector2("center", "a centre like (-62, 0)");
                var size = parameters.Vector2("size", "a size like (14, 10), its full width and length");
                if (size.X <= 0 || size.Y <= 0)
                    throw parameters.Error("size", $"{type} needs a positive width and length, like size=(14, 10)");
                var angle = parameters.Number("angle", 0, -360, 360, "degrees, -360 to 360");
                var rounded = type == "RoundedBox2D";
                var radius = rounded ? parameters.Number("radius", null, 0, 1000, "the corners' radius in metres") : 0;
                shape = new BoxRegion(center, size, angle, radius, noise, noiseScale);
                parameters.Done(rounded ? ["center", "size", "radius", "angle", "noise", "noiseScale"] : ["center", "size", "angle", "noise", "noiseScale"]);
                break;
        }
        return shape;
    }

    private static IReadOnlyList<Placement> ReadPlacements(SourceLine line, Cursor cursor)
    {
        cursor.ExpectEnd();
        var placements = new List<Placement>();
        foreach (var child in line.Children)
        {
            var childCursor = new Cursor(child);
            var model = childCursor.ReadFileName("a model's name, like castle0 or amanita-scout");
            var owner = $"placement {model}";
            var parameters = Parameters.Read(owner, child, childCursor);
            var at = parameters.Vector2("at", "a point like (-62, 0), x and z in metres");
            var yaw = parameters.Number("yaw", 0, -360, 360, "degrees, -360 to 360");
            var scale = parameters.Number("scale", 1, 0.05f, 20, "a scale, 0.05 to 20");
            var flatten = parameters.Number("flatten", 0, 0, 200, "the radius of level ground in metres, 0 to 200");
            var falloff = parameters.Number("falloff", Placement.DefaultFlattenFalloff, 0.5f, 200, "metres, 0.5 to 200");
            var ground = parameters.Name("ground", null, null);
            var margin = parameters.Number("groundMargin", Placement.DefaultGroundMargin, 0, 50, "metres, 0 to 50");
            var y = parameters.Number("y", 0, 0, 100, "metres above the ground, 0 to 100");
            parameters.Done("at", "yaw", "scale", "flatten", "falloff", "ground", "groundMargin", "y");
            placements.Add(new Placement(model, at, yaw, scale, flatten, falloff, ground, margin, child.Number) { Y = y });
        }
        return placements.Count > 0 ? placements : throw cursor.Error("Nothing is placed; indent placements below, like 'rock1 at=(10, 30)'");
    }

    /// <param name="trees">A trees section: group trees by default, and a band instead of a reach.</param>
    private static ScatterLayer ReadScatter(SourceLine line, Cursor cursor, bool trees)
    {
        var what = trees ? "trees section" : Scatter;
        // Below it are the models it picks from, one per line, and lines of its own parameters (name=value), like any section's
        var entries = new List<ScatterEntry>();
        var parameterLines = new List<SourceLine>();
        foreach (var child in line.Children)
        {
            var probe = new Cursor(child);
            if (probe.TryReadName() is not null && probe.Peek() == '=')
            {
                parameterLines.Add(child);
                continue;
            }
            var childCursor = new Cursor(child);
            var model = childCursor.ReadFileName(trees
                ? "a model's name, like tree0, or a parameter like density=0.01"
                : "a model's name, like grass-turf0, or a parameter like density=0.6");
            if (entries.FirstOrDefault(e => e.Model == model) is { } earlier)
                throw childCursor.Error($"Model '{model}' is given twice in this {what}, first on line {earlier.Line}; give it a larger weight instead");
            var entry = Parameters.Read($"{(trees ? Trees : Scatter)} entry {model}", child, childCursor);
            var weight = entry.Number("weight", 1, 0, 1000, "how often it is picked against the others, 0 to 1000");
            entry.Done("weight");
            entries.Add(new ScatterEntry(model, weight, child.Number));
        }
        if (entries.Count == 0)
            throw cursor.Error(trees
                ? "The trees section needs the models it places indented below it, like 'tree0 weight=2'"
                : "The scatter needs the models it scatters indented below it, like 'grass-turf0 weight=3'");
        if (entries.All(e => e.Weight <= 0))
            throw cursor.Error($"The {what} needs a model with a weight above 0");

        var parameters = Parameters.Read(what, line with { Children = parameterLines }, cursor);
        var group = parameters.Name("group", trees ? ScatterLayer.DefaultTreesGroup : ScatterLayer.DefaultGroup, null)!;
        var density = parameters.Number("density", null, 0, 100, "instances per square metre, 0 to 100");
        var cluster = parameters.Number("cluster", ScatterLayer.DefaultCluster, 1, 100,
            "how many times the density the densest clusters reach, 1 (no clusters) to 100");
        var clusterScale = parameters.Number("clusterScale", ScatterLayer.DefaultClusterScale, 0.5f, 1000, "the clusters' width in metres, 0.5 to 1000");
        var spacing = parameters.Number("spacing", null, 0.05f, 100, "the least distance between instances in metres, 0.05 to 100");
        var avoid = parameters.Names("avoid", "regions like dirt,tiles,path");
        var margin = parameters.Number("margin", 0, 0, 100, "metres kept clear round the regions avoided, 0 to 100");
        var maxSlope = parameters.Number("maxSlope", ScatterLayer.DefaultMaxSlope, 0, 90, "degrees, 0 to 90");
        var reach = trees ? 0 : parameters.Number("reach", ScatterLayer.DefaultReach, 0, 1000, "metres up the hills from the playable area, 0 to 1000");
        Vector2? band = null;
        if (trees)
        {
            var range = parameters.Vector2("band", "the least and greatest distance from the playable area's edge in metres, like (12, 28)");
            if (range.Y <= range.X)
                throw parameters.Error("band", "The band needs its least distance first and a greatest beyond it, like (12, 28)");
            band = range;
        }
        var scale = parameters.Vector2("scale", ScatterLayer.DefaultScale, "the least and greatest scale, like (0.85, 1.15)");
        if (scale.X <= 0 || scale.Y < scale.X)
            throw parameters.Error("scale", "The scale needs a least and a greatest above 0, the least first, like (0.85, 1.15)");
        parameters.Done("group", "density", "cluster", "clusterScale", "spacing", "avoid", "margin", "maxSlope", trees ? "band" : "reach", "scale");
        return new ScatterLayer(entries, group, density, cluster, clusterScale, spacing, avoid, margin, maxSlope, reach, scale, line.Number) { Band = band };
    }

    private static DrawGroup ReadDrawGroup(SourceLine line, Cursor cursor)
    {
        var name = cursor.ReadName("a draw group's name, like foliage");
        var owner = $"draw group {name}";
        var parameters = Parameters.Read(owner, line, cursor);
        var rings = parameters.Numbers("rings", "the distances in metres where each ring ends, nearest first, like [6, 15, 35]");
        if (rings.Length == 0 || rings[0] <= 0 || rings.Zip(rings.Skip(1)).Any(p => p.Second <= p.First))
            throw parameters.Error("rings", "The rings need distances above 0, each further than the one before, like [6, 15, 35]");
        var outline = parameters.Count("outline", 0, rings.Length, $"how many of the nearest rings get the outline, 0 to {rings.Length}");
        var shadows = parameters.Count("shadows", 0, rings.Length, $"how many of the nearest rings cast shadows, 0 to {rings.Length}");
        var lowShadows = parameters.Count("lowShadows", shadows, rings.Length,
            $"how many of the nearest rings cast shadows on the integrated GPU / web preset, 0 to {rings.Length}");
        var blend = parameters.Number("blend", DrawGroup.DefaultBlend, 0, 100, "metres over which neighbouring rings cross-fade, 0 to 100");
        var fade = parameters.Number("fade", 0, 0, rings[^1], $"metres over which the last ring fades out, 0 to {FileSyntax.Number(rings[^1])}");
        var wind = parameters.Number("wind", 0, 0, 10, "metres of sway at windHeight, 0 to 10");
        var windHeight = parameters.Number("windHeight", 1, 0.01f, 1000, "the height in metres that sways the full amount, 0.01 to 1000");
        var tintHeight = parameters.Number("tintHeight", 0, 0, 1000, "metres above the base that blend into the ground's colour, 0 to 1000");
        var lowDensity = parameters.Number("lowDensity", 1, 0, 1, "the share of instances the integrated GPU / web preset draws, 0 to 1");
        var lowDistance = parameters.Number("lowDistance", 1, 0.1f, 1, "what the integrated GPU / web preset multiplies the distances by, 0.1 to 1");
        parameters.Done("rings", "outline", "shadows", "lowShadows", "blend", "fade", "wind", "windHeight", "tintHeight", "lowDensity", "lowDistance");
        return new DrawGroup(name, rings, outline, shadows, lowShadows, blend, fade, wind, windHeight, tintHeight, lowDensity, lowDistance, line.Number);
    }

    private static void NothingBelow(SourceLine line, string what)
    {
        if (line.Children.Count > 0)
            throw new SdfProgramFileException(line.Children[0].Number, $"Nothing can be indented below '{what}'");
    }

    private static void ReadFormat(SourceLine line)
    {
        var cursor = new Cursor(line);
        if (cursor.TryReadName() != FileSyntax.FormatKeyword || cursor.ReadValue() is not RawNumber { IsWhole: true } number)
            throw cursor.Error($"The file must start with '{FileSyntax.FormatKeyword} {FormatVersion}'");
        cursor.ExpectEnd();
        if (int.Parse(number.Text, CultureInfo.InvariantCulture) is var version && version != FormatVersion)
            throw cursor.Error($"This level has format {version}, but only format {FormatVersion} can be read");
        NothingBelow(line, FileSyntax.FormatKeyword);
    }

    /// <summary>
    /// The name=value parameters of a section or entry: those after its name, then those on the lines indented below it, each line only
    /// parameters (<c>margin=6</c>). Each is taken once; <see cref="Done"/> rejects any that are left, naming the ones there are.
    /// </summary>
    private sealed class Parameters
    {
        private readonly string owner;
        private readonly Dictionary<string, (RawValue Value, Cursor Cursor)> values = [];
        private readonly Dictionary<string, Cursor> taken = [];
        private readonly Cursor ownerCursor;

        private Parameters(string owner, Cursor ownerCursor)
        {
            this.owner = owner;
            this.ownerCursor = ownerCursor;
        }

        public static Parameters Read(string owner, SourceLine line, Cursor cursor)
        {
            var parameters = new Parameters(owner, cursor);
            parameters.ReadLine(cursor);
            foreach (var child in line.Children)
            {
                if (child.Children.Count > 0)
                    throw new SdfProgramFileException(child.Children[0].Number, "Parameters have nothing indented below them");
                var childCursor = new Cursor(child);
                var probe = new Cursor(child);
                if (probe.TryReadName() is null || probe.Peek() != '=')
                    throw childCursor.Error($"Only parameters like name=value can be indented below {owner}");
                parameters.ReadLine(childCursor);
            }
            return parameters;
        }

        private void ReadLine(Cursor cursor)
        {
            while (!cursor.AtEnd)
            {
                var name = cursor.ReadName("a parameter like name=value");
                cursor.Expect('=');
                var value = cursor.ReadValue();
                // Names may be listed separated by commas, like avoid=dirt,tiles,path
                if (value is RawName && cursor.Peek() == ',')
                {
                    var names = new List<RawValue> { value };
                    while (cursor.TryConsume(','))
                        names.Add(new RawName(cursor.ReadName("a name after ','")));
                    value = new RawList(names.ToArray());
                }
                if (!values.TryAdd(name, (value, cursor)))
                    throw cursor.Error($"Parameter '{name}' of {owner} is given twice");
            }
        }

        /// <param name="fallback">The value when the parameter is left out; null when it is required.</param>
        public float Number(string name, float? fallback, float min, float max, string expected)
        {
            if (!values.Remove(name, out var given))
                return fallback ?? throw ownerCursor.Error($"{Capitalized(owner)} is missing parameter '{name}': {expected}");
            taken[name] = given.Cursor;
            if (given.Value is not RawNumber number)
                throw given.Cursor.Error($"'{name}' of {owner} needs {expected}");
            var value = float.Parse(number.Text, CultureInfo.InvariantCulture);
            return value >= min && value <= max
                ? value
                : throw given.Cursor.Error($"'{name}' of {owner} is {number.Text}, but needs {expected}");
        }

        /// <param name="fallback">The value when the parameter is left out.</param>
        public Vector2 Vector2(string name, Vector2 fallback, string expected) => values.ContainsKey(name) ? Vector2(name, expected) : fallback;

        public Vector2 Vector2(string name, string expected)
        {
            if (!values.Remove(name, out var given))
                throw ownerCursor.Error($"{Capitalized(owner)} is missing parameter '{name}': {expected}");
            taken[name] = given.Cursor;
            return given.Value is RawVector { Components: [var x, var y] }
                ? new Vector2(float.Parse(x.Text, CultureInfo.InvariantCulture), float.Parse(y.Text, CultureInfo.InvariantCulture))
                : throw given.Cursor.Error($"'{name}' of {owner} needs {expected}");
        }

        public Vector3 Vector3(string name, string expected)
        {
            if (!values.Remove(name, out var given))
                throw ownerCursor.Error($"{Capitalized(owner)} is missing parameter '{name}': {expected}");
            taken[name] = given.Cursor;
            return given.Value is RawVector { Components: [var x, var y, var z] }
                ? new Vector3(float.Parse(x.Text, CultureInfo.InvariantCulture), float.Parse(y.Text, CultureInfo.InvariantCulture), float.Parse(z.Text, CultureInfo.InvariantCulture))
                : throw given.Cursor.Error($"'{name}' of {owner} needs {expected}");
        }

        public Vector2[] Points(string name, string expected)
        {
            if (!values.Remove(name, out var given))
                throw ownerCursor.Error($"{Capitalized(owner)} is missing parameter '{name}': {expected}");
            taken[name] = given.Cursor;
            if (given.Value is not RawList { Items: { Length: >= 2 } items } || items.Any(i => i is not RawVector { Components.Length: 2 }))
                throw given.Cursor.Error($"'{name}' of {owner} needs {expected}, at least two");
            return items.Cast<RawVector>()
                .Select(v => new Vector2(float.Parse(v.Components[0].Text, CultureInfo.InvariantCulture), float.Parse(v.Components[1].Text, CultureInfo.InvariantCulture)))
                .ToArray();
        }

        /// <summary>A list of numbers, like rings=[6, 15, 35].</summary>
        public float[] Numbers(string name, string expected)
        {
            if (!values.Remove(name, out var given))
                throw ownerCursor.Error($"{Capitalized(owner)} is missing parameter '{name}': {expected}");
            taken[name] = given.Cursor;
            return given.Value is RawList { Items: var items } && items.All(i => i is RawNumber)
                ? items.Cast<RawNumber>().Select(n => float.Parse(n.Text, CultureInfo.InvariantCulture)).ToArray()
                : throw given.Cursor.Error($"'{name}' of {owner} needs {expected}");
        }

        /// <summary>A whole number from 0 to <paramref name="max"/>, like outline=1.</summary>
        public int Count(string name, int fallback, int max, string expected)
        {
            if (!values.Remove(name, out var given))
                return fallback;
            taken[name] = given.Cursor;
            return given.Value is RawNumber { IsWhole: true } number
                   && int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count <= max
                ? count
                : throw given.Cursor.Error($"'{name}' of {owner} needs {expected}");
        }

        /// <summary>Bare names separated by commas, like avoid=dirt,tiles,path (or one name, or a list in brackets); none when left out.</summary>
        public string[] Names(string name, string expected)
        {
            if (!values.Remove(name, out var given))
                return [];
            taken[name] = given.Cursor;
            return given.Value switch
            {
                RawName single => [single.Name],
                RawList { Items: var items } when items.All(i => i is RawName) => items.Cast<RawName>().Select(n => n.Name).ToArray(),
                _ => throw given.Cursor.Error($"'{name}' of {owner} needs {expected}"),
            };
        }

        /// <summary>A bare name, like pattern=stones.</summary>
        /// <param name="choices">The names it may be; null for any.</param>
        public string? Name(string name, string? fallback, string[]? choices)
        {
            if (!values.Remove(name, out var given))
                return fallback;
            taken[name] = given.Cursor;
            var expected = choices is null ? "a name" : $"one of {string.Join(", ", choices)}";
            return given.Value is RawName { Name: var value } && (choices is null || choices.Contains(value))
                ? value
                : throw given.Cursor.Error($"'{name}' of {owner} needs {expected}");
        }

        /// <summary>Whether the parameter is given and not taken yet.</summary>
        public bool Has(string name) => values.ContainsKey(name);

        /// <summary>An error on the line of a parameter that was read.</summary>
        public SdfProgramFileException Error(string name, string problem) => taken.GetValueOrDefault(name, ownerCursor).Error(problem);

        public void Done(params string[] known)
        {
            if (values.FirstOrDefault() is { Key: { } unknown } left)
                throw left.Value.Cursor.Error($"{Capitalized(owner)} has no parameter '{unknown}'; its parameters are {string.Join(", ", known)}");
        }

        private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];
    }
}
