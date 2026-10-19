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
/// regions                                   # the ground other than grass, 2D shapes; where regions overlap, the later one wins
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
/// </code>
/// Only <c>play</c> is required: without <c>hills</c> the ground stays flat up to the edge, without <c>edge</c> the island ends at the
/// crest (so a level with neither bakes the flat floor alone, with no hill, rim or underside), without <c>scatter</c>, <c>trees</c> and
/// <c>draw</c> nothing is scattered or drawn instanced, without <c>sky</c> the bake holds no sky at all, and the seed is
/// <see cref="LevelDefinition.DefaultSeed"/>.
/// <para>
/// A region's shapes are <c>Circle</c> (center, radius), <c>Box2D</c> (center, size, angle), <c>RoundedBox2D</c> (the same and radius)
/// and <c>Polyline</c> (points, width), each with optional <c>noise</c> (metres the edge wobbles) and <c>noiseScale</c>. Sizes are full
/// widths, angles degrees. A region's own line may give <c>wobble</c>, metres the ground shader moves its edge with fine noise (0.06 by
/// default; 0 keeps the edge ruled straight). A region's material is materials/ground/ground_&lt;region&gt;.sdfmat; there are at most four regions.
/// </para>
/// <para>
/// A placement names a model as its file is named, hyphens and all (amanita-scout), then <c>at</c> (x, z), and optionally <c>yaw</c>
/// (degrees, counter-clockwise from above), <c>scale</c>, <c>flatten</c> (the radius of level ground round it), <c>falloff</c> (metres
/// over which that ground blends back), <c>ground</c> (a region painted under its footprint) and <c>groundMargin</c> (how far that
/// reaches past the footprint).
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
/// <c>square</c> (8 m by default), the side of the checker's squares.
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
    private const string Ring = "ring";
    private const string Noise = "noise";
    private const string Underside = "underside";

    private static readonly string[] Sections = [Play, Seed, Hills, Edge, Regions, Place, Scatter, Trees, Draw, Sky, Room];

    /// <summary>The sections that may be given more than once, each time with something else: a scatter or trees per layer, a draw per group.</summary>
    private static readonly string[] Repeatable = [Scatter, Trees, Draw];

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
        IReadOnlyList<Placement> placements = [];
        var scatters = new List<ScatterLayer>();
        var trees = new List<ScatterLayer>();
        var drawGroups = new List<DrawGroup>();
        SkySection? sky = null;
        RoomSpec? room = null;
        var roomLine = 0;
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
                    regions = ReadRegions(line, cursor);
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
            }
        }

        foreach (var scatter in scatters.Concat(trees))
            if (drawGroups.All(g => g.Name != scatter.Group))
                throw new SdfProgramFileException(scatter.Line,
                    $"The {(scatter.Band is null ? "scatter" : "trees section")} draws in group '{scatter.Group}', which has no '{Draw} {scatter.Group}' section saying how to draw it");

        if (play is null)
            throw new SdfProgramFileException(lines[^1].Number, $"The level has no playable area; add a line like '{Play} size=(150, 100)'");
        if (room is not null && (MathF.Abs(room.Size.X - play.Size.X) > 1e-3f || MathF.Abs(room.Size.Z - play.Size.Y) > 1e-3f))
            throw new SdfProgramFileException(roomLine,
                $"The room's walls stand on the playable area's edges; its size ({FileSyntax.Number(room.Size.X)}, {FileSyntax.Number(room.Size.Z)}) across and along must be the play size ({FileSyntax.Number(play.Size.X)}, {FileSyntax.Number(play.Size.Y)})");
        return new LevelDefinition(play, seed, hills, edge) { Regions = regions, Placements = placements, Scatters = scatters, Trees = trees, DrawGroups = drawGroups, Sky = sky, Room = room };
    }

    private static RoomSpec ReadRoom(SourceLine line, Cursor cursor)
    {
        var parameters = Parameters.Read(Room, line, cursor);
        var size = parameters.Vector3("size", "a size like (160, 160, 160), width, height and depth in metres");
        var square = parameters.Number("square", RoomSpec.DefaultSquare, 0.5f, 100, "a square's side in metres, 0.5 to 100");
        parameters.Done("size", "square");
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw parameters.Error("size", "The room needs a positive width, height and depth, like size=(160, 160, 160)");
        foreach (var length in new[] { size.X, size.Y, size.Z })
            if (MathF.Abs(length / square - MathF.Round(length / square)) > 1e-3f)
                throw parameters.Error("size", $"The room's sides must be whole numbers of squares of {FileSyntax.Number(square)} m");
        return new RoomSpec(size, square);
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

    private static IReadOnlyList<GroundRegion> ReadRegions(SourceLine line, Cursor cursor)
    {
        cursor.ExpectEnd();
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
        return regions.Count > 0 ? regions : throw cursor.Error("The regions need a region indented below them, like 'dirt'");
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
            parameters.Done("at", "yaw", "scale", "flatten", "falloff", "ground", "groundMargin");
            placements.Add(new Placement(model, at, yaw, scale, flatten, falloff, ground, margin, child.Number));
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
