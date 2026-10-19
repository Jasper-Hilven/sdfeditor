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
///     margin=6
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
/// sky preset=night                          # content/skies/night.sky, with any of its lines overridden below
///     fade end=200
/// </code>
/// Only <c>play</c> is required: without <c>hills</c> the ground stays flat up to the edge, without <c>edge</c> the island ends at the
/// crest, and the seed is <see cref="LevelDefinition.DefaultSeed"/>.
/// <para>
/// A region's shapes are <c>Circle</c> (center, radius), <c>Box2D</c> (center, size, angle), <c>RoundedBox2D</c> (the same and radius)
/// and <c>Polyline</c> (points, width), each with optional <c>noise</c> (metres the edge wobbles) and <c>noiseScale</c>. Sizes are full
/// widths, angles degrees. A region's material is materials/ground/ground_&lt;region&gt;.sdfmat; there are at most four regions.
/// </para>
/// <para>
/// A placement names a model as its file is named, hyphens and all (amanita-scout), then <c>at</c> (x, z), and optionally <c>yaw</c>
/// (degrees, counter-clockwise from above), <c>scale</c>, <c>flatten</c> (the radius of level ground round it), <c>falloff</c> (metres
/// over which that ground blends back), <c>ground</c> (a region painted under its footprint) and <c>groundMargin</c> (how far that
/// reaches past the footprint). Later formats add sections (scatter rules) next to these, each read the same way.
/// </para>
/// <para>
/// The sky names a preset in content/skies/ and may override its lines (see <see cref="SkyFile"/>); without it the level has the
/// <see cref="SkyFile.DefaultPreset"/> sky.
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
    private const string Sky = "sky";
    private const string Ring = "ring";
    private const string Noise = "noise";

    private static readonly string[] Sections = [Play, Seed, Hills, Edge, Regions, Place, Sky];

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
        SkySection? sky = null;
        var seen = new Dictionary<string, int>();
        foreach (var line in lines.Skip(1))
        {
            var cursor = new Cursor(line);
            var section = cursor.ReadName($"a section: {string.Join(", ", Sections)}");
            if (!Sections.Contains(section))
                throw cursor.Error($"A level has no section '{section}'; its sections are {string.Join(", ", Sections)}");
            if (seen.TryGetValue(section, out var first))
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
                case Sky:
                    sky = SkyFile.ReadSection(line, cursor);
                    break;
            }
        }

        if (play is null)
            throw new SdfProgramFileException(lines[^1].Number, $"The level has no playable area; add a line like '{Play} size=(150, 100)'");
        return new LevelDefinition(play, seed, hills, edge) { Regions = regions, Placements = placements, Sky = sky };
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
        var parameters = Parameters.Read(Edge, line, cursor);
        var margin = parameters.Number("margin", null, 0, 1000, "metres behind the hills' crest, 0 to 1000");
        parameters.Done("margin");
        return new IslandEdge(margin);
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
            parameters.Done("pattern", "stone", "grout");
            if (child.Children.Count == 0)
                throw childCursor.Error($"Region '{name}' needs shapes indented below it, like 'Circle center=(0, 0) radius=10'");
            regions.Add(new GroundRegion(name, child.Children.Select(ReadShape).ToArray(), pattern, stone, grout));
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
                if (!values.TryAdd(name, (cursor.ReadValue(), cursor)))
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

        public Vector2 Vector2(string name, string expected)
        {
            if (!values.Remove(name, out var given))
                throw ownerCursor.Error($"{Capitalized(owner)} is missing parameter '{name}': {expected}");
            taken[name] = given.Cursor;
            return given.Value is RawVector { Components: [var x, var y] }
                ? new Vector2(float.Parse(x.Text, CultureInfo.InvariantCulture), float.Parse(y.Text, CultureInfo.InvariantCulture))
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
