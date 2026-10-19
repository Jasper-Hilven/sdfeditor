using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Skies;

/// <summary>
/// The text format of a sky preset, content/skies/&lt;name&gt;.sky, in the style of level files: a format line, then one line per part of
/// the sky, each with its parameters as name=value after it or on lines indented below it, and its entries (bands, the moon's turn and
/// blink) indented below it. Comments start with '#'. The file name is the preset's name.
/// <code>
/// format 1
///
/// light azimuth=140 elevation=28 color=(0.78, 0.8, 1.0)    # the main light; a moon hangs in its direction
/// tone brightness=0.75 shadowTint=(0.8, 0.85, 1.1) glowKeep=1
/// gradient softness=0.6                                      # band edges, in degrees
///     band from=32 color=(0.07, 0.05, 0.19)                  # each band from an elevation up to the next one
///     band from=-6 color=(0.26, 0.28, 0.38)
/// abyss
///     band from=-6 color=(0.12, 0.08, 0.2)                   # each band from an elevation down to the next one
///     glow elevation=-55 width=12 color=(0.05, 0.25, 0.28)
/// stars density=0.6 size=1.6 twinkle=0.3 tint=0.25 minElevation=8
/// fade start=25 end=180 amount=0.55
/// moon model=eye-moon size=9 glow=0.6
///     turn amount=12 period=40
///     blink every=(9, 25) duration=0.35
/// floaters model=floating-cap0 count=4 distance=(250, 400) elevation=(-10, 18) scale=8 drift=0.6 bob=1.5
/// spores
///     abyss count=600 color=(0.3, 0.95, 0.85) altColor=(1, 0.7, 0.3) rise=1.2 size=0.25 from=-80 to=20
///     near count=250 radius=35 color=(0.55, 0.95, 0.75) size=0.06 drift=0.3
/// post exposure=-0.1 bloom=0.25
/// </code>
/// <c>light</c>, <c>tone</c>, <c>gradient</c> and <c>post</c> are required; the other lines may be left out (no abyss, stars, fade, moon,
/// floaters or spores). A line that is given is complete: every one of its parameters has a value. The gradient's lowest band reaches
/// down to where the abyss begins, or to the nadir; together the gradient and the abyss have at most <see cref="MaxBands"/> bands.
/// Unknown or repeated lines and parameters are rejected with their line, as in level files.
/// <para>
/// A level picks a preset in its <c>sky</c> section and may override it (<see cref="ReadSection"/>): any line of the preset given again
/// there overrides those of its parameters it gives, and an entry indented below it overrides the preset's entry of the same name and
/// index (the second band the second band). <c>replace=true</c> on a line drops the preset's entries below it for the ones given.
/// <see cref="Resolve(SkySection?, Func{string, string?})"/> merges the two into a <see cref="SkyDefinition"/>.
/// </para>
/// </summary>
public static class SkyFile
{
    public const int FormatVersion = 1;

    public const string Extension = ".sky";

    /// <summary>The folder of the content folder that holds the presets: content/skies/.</summary>
    public const string FolderName = "skies";

    /// <summary>The preset of a level without a sky section: neutral daylight that leaves the toon look as it is.</summary>
    public const string DefaultPreset = "day";

    /// <summary>The bands the shaders take, the gradient's and the abyss's together.</summary>
    public const int MaxBands = 8;

    public const int MaxFloaters = 16;

    public const int MaxSpores = 5000;

    private const string Replace = "replace";
    private const string Model = "model";

    /// <summary>Every line a sky may have, with its parameters and the entries indented below it.</summary>
    private static readonly Shape[] Lines =
    [
        new("light", ["azimuth", "elevation", "color"], [], Required: true),
        new("tone", ["brightness", "shadowTint", "glowKeep"], [], Required: true),
        new("gradient", ["softness"], [new("band", ["from", "color"], [], Repeated: true)], Required: true),
        new("abyss", [], [new("band", ["from", "color"], [], Repeated: true), new("glow", ["elevation", "width", "color"], [])]),
        new("stars", ["density", "size", "twinkle", "tint", "minElevation"], []),
        new("fade", ["start", "end", "amount"], []),
        new("moon", [Model, "size", "glow"], [new("turn", ["amount", "period"], []), new("blink", ["every", "duration"], [])]),
        new("floaters", [Model, "count", "distance", "elevation", "scale", "drift", "bob"], []),
        new("spores", [],
        [
            new("abyss", ["count", "color", "altColor", "rise", "size", "from", "to"], []),
            new("near", ["count", "radius", "color", "size", "drift"], []),
        ]),
        new("post", ["exposure", "bloom"], [], Required: true),
    ];

    /// <summary>"content/skies/night.sky" is the preset night.</summary>
    public static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>The presets in a folder by name, content/skies/&lt;name&gt;.sky; null for a name that has no file there.</summary>
    public static Func<string, string?> PresetsIn(string folder) => name =>
        FileSyntax.IsIdentifier(name) && Path.Combine(folder, name + Extension) is var path && File.Exists(path) ? File.ReadAllText(path) : null;

    public static SkyDefinition Load(string path) => Read(File.ReadAllText(path));

    /// <summary>A preset on its own.</summary>
    /// <exception cref="SdfProgramFileException">The text is not a valid, complete sky; the exception names the line.</exception>
    public static SkyDefinition Read(string text)
    {
        try
        {
            return Build(Parse(text, null));
        }
        catch (SkyException exception)
        {
            throw new SdfProgramFileException(exception.Line, exception.Problem);
        }
    }

    /// <summary>
    /// A level's sky section: <c>sky preset=night</c>, with lines of the preset indented below it to override. The preset is only
    /// looked up when the level is baked (<see cref="Resolve(SkySection?, Func{string, string?})"/>).
    /// </summary>
    internal static SkySection ReadSection(SourceLine line, Cursor cursor)
    {
        try
        {
            var section = new RawLine("sky", line.Number, null);
            ReadParameters(cursor, section, ["preset"], "the sky");
            var overrides = new List<RawLine>();
            foreach (var child in line.Children)
            {
                if (IsParameterLine(child))
                {
                    ReadParameterLine(child, section, ["preset"], "the sky");
                    continue;
                }
                var entry = ReadLine(child, Lines, null, overrides: true, "a line of a sky preset to override");
                if (overrides.FirstOrDefault(o => o.Name == entry.Name) is { } first)
                    throw new SkyException(null, child.Number, $"The sky's {entry.Name} is overridden twice, first on line {first.Line}");
                overrides.Add(entry);
            }
            var preset = section.Values.TryGetValue("preset", out var given)
                ? given.Value is RawName { Name: var name } ? name : throw given.Error("'preset' of the sky needs a preset's name, like preset=night")
                : throw new SkyException(null, line.Number, "The sky needs a preset, like 'sky preset=night' for content/skies/night.sky");
            return new SkySection(preset, line.Number, overrides);
        }
        catch (SkyException exception)
        {
            throw new SdfProgramFileException(exception.Line, exception.Problem);
        }
    }

    /// <summary>
    /// The sky of a level: its preset (<see cref="DefaultPreset"/> without a section) with the section's overrides merged in, and a hash
    /// of the preset's file, so a bake that uses it changes when the file does.
    /// </summary>
    /// <param name="presets">The text of a preset by name; null for a name that has none.</param>
    /// <exception cref="SdfProgramFileException">
    /// The preset is missing, or the merged sky is not valid. A problem in the level is on its line; one in the preset is on the level's
    /// sky line, naming the preset's file and line.
    /// </exception>
    public static ResolvedSky Resolve(SkySection? section, Func<string, string?> presets)
    {
        var name = section?.Preset ?? DefaultPreset;
        var line = section?.Line ?? 1;
        var file = $"{ModelFiles.ContentFolderName}/{FolderName}/{name}{Extension}";
        var text = presets(name) ?? throw new SdfProgramFileException(line,
            section is null
                ? $"A level without a sky section takes the preset {DefaultPreset}, but there is no {file}"
                : $"There is no sky preset '{name}': there is no {file}");
        try
        {
            List<RawLine> lines;
            try
            {
                lines = Parse(text, file);
            }
            catch (SdfProgramFileException exception)
            {
                throw new SkyException(file, exception.Line, exception.Problem);
            }
            foreach (var entry in section?.Overrides ?? [])
                Merge(lines, entry);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
            return new ResolvedSky(Build(lines), name, hash);
        }
        catch (SkyException exception)
        {
            throw exception.File is null
                ? new SdfProgramFileException(exception.Line, exception.Problem)
                : new SdfProgramFileException(line, $"{exception.File}:{exception.Line}: {exception.Problem}");
        }
    }

    /// <summary>The preset's lines, each checked against <see cref="Lines"/>, but not yet for completeness.</summary>
    private static List<RawLine> Parse(string text, string? file)
    {
        var lines = SourceLine.Parse(text);
        if (lines.Count == 0)
            throw new SkyException(file, 1, $"The file is empty, it must start with '{FileSyntax.FormatKeyword} {FormatVersion}'");
        ReadFormat(lines[0], file);

        var read = new List<RawLine>();
        foreach (var line in lines.Skip(1))
        {
            var entry = ReadLine(line, Lines, file, overrides: false, "a line of a sky");
            if (read.FirstOrDefault(r => r.Name == entry.Name) is { } first)
                throw new SkyException(file, line.Number, $"'{entry.Name}' is given twice, first on line {first.Line}");
            read.Add(entry);
        }
        return read;
    }

    private static void ReadFormat(SourceLine line, string? file)
    {
        var cursor = new Cursor(line);
        if (cursor.TryReadName() != FileSyntax.FormatKeyword || cursor.ReadValue() is not RawNumber { IsWhole: true } number || !cursor.AtEnd)
            throw new SkyException(file, line.Number, $"The file must start with '{FileSyntax.FormatKeyword} {FormatVersion}'");
        if (int.Parse(number.Text, CultureInfo.InvariantCulture) is var version && version != FormatVersion)
            throw new SkyException(file, line.Number, $"This sky has format {version}, but only format {FormatVersion} can be read");
        if (line.Children.Count > 0)
            throw new SkyException(file, line.Children[0].Number, $"Nothing can be indented below '{FileSyntax.FormatKeyword}'");
    }

    /// <summary>A line and what is indented below it: lines of parameters, or entries of the kinds its shape has.</summary>
    private static RawLine ReadLine(SourceLine line, IReadOnlyList<Shape> shapes, string? file, bool overrides, string what)
    {
        try
        {
            var cursor = new Cursor(line);
            var names = string.Join(", ", shapes.Select(s => s.Name));
            var name = cursor.ReadName($"{what}: {names}");
            var shape = shapes.FirstOrDefault(s => s.Name == name)
                        ?? throw cursor.Error($"A sky has no '{name}' here; there can be {names}");
            var raw = new RawLine(name, line.Number, file);
            // replace=true only means something where a level overrides a line with entries below it
            string[] keys = overrides && shape.Children.Length > 0 ? [.. shape.Keys, Replace] : shape.Keys;
            ReadParameters(cursor, raw, keys, $"the sky's {name}");
            foreach (var child in line.Children)
            {
                if (IsParameterLine(child))
                {
                    ReadParameterLine(child, raw, keys, $"the sky's {name}");
                    continue;
                }
                if (shape.Children.Length == 0)
                    throw new SdfProgramFileException(child.Number, $"Only parameters like name=value can be indented below {name}");
                var entry = ReadLine(child, shape.Children, file, overrides, $"an entry of {name}");
                if (!shape.Children.First(s => s.Name == entry.Name).Repeated && raw.Children.FirstOrDefault(c => c.Name == entry.Name) is { } first)
                    throw new SdfProgramFileException(child.Number, $"The {name}'s {entry.Name} is given twice, first on line {first.Line}");
                raw.Children.Add(entry);
            }
            if (raw.Values.TryGetValue(Replace, out var replace))
                raw.Replace = replace.Value is RawName { Name: "true" or "false" } flag
                    ? flag.Name == "true"
                    : throw replace.Error($"'{Replace}' of the sky's {name} is true or false");
            return raw;
        }
        catch (SdfProgramFileException exception)
        {
            throw new SkyException(file, exception.Line, exception.Problem);
        }
    }

    private static bool IsParameterLine(SourceLine line)
    {
        var probe = new Cursor(line);
        return probe.TryReadName() is not null && probe.Peek() == '=';
    }

    private static void ReadParameterLine(SourceLine line, RawLine raw, string[] keys, string owner)
    {
        if (line.Children.Count > 0)
            throw new SdfProgramFileException(line.Children[0].Number, "Parameters have nothing indented below them");
        ReadParameters(new Cursor(line), raw, keys, owner);
    }

    /// <summary>name=value pairs to the end of the line; a model is a file name, hyphens and all (eye-moon).</summary>
    private static void ReadParameters(Cursor cursor, RawLine raw, string[] keys, string owner)
    {
        while (!cursor.AtEnd)
        {
            var key = cursor.ReadName("a parameter like name=value");
            if (!keys.Contains(key))
                throw cursor.Error(keys.Length == 0
                    ? $"{Capitalized(owner)} has no parameters, only lines indented below it"
                    : $"{Capitalized(owner)} has no parameter '{key}'; its parameters are {string.Join(", ", keys)}");
            cursor.Expect('=');
            RawValue value = key == Model ? new RawName(cursor.ReadFileName("a model's name, like eye-moon")) : cursor.ReadValue();
            if (!raw.Values.TryAdd(key, new RawSetting(value, raw.File, cursor.Line.Number)))
                throw cursor.Error($"Parameter '{key}' of {owner} is given twice");
        }
    }

    /// <summary>
    /// Merges an override into the preset's lines: its parameters replace the preset's of the same name, its entries merge into the
    /// preset's entry of the same name and index; a line or entry the preset does not have is added.
    /// </summary>
    private static void Merge(List<RawLine> lines, RawLine change)
    {
        if (lines.FirstOrDefault(l => l.Name == change.Name) is { } existing)
            MergeInto(existing, change);
        else
            lines.Add(change);
    }

    private static void MergeInto(RawLine target, RawLine change)
    {
        foreach (var (key, value) in change.Values.Where(v => v.Key != Replace))
            target.Values[key] = value;
        if (change.Replace)
            target.Children.Clear();
        foreach (var group in change.Children.GroupBy(c => c.Name))
        {
            var ofName = target.Children.Where(c => c.Name == group.Key).ToArray();
            var index = 0;
            foreach (var entry in group)
            {
                if (index < ofName.Length)
                    MergeInto(ofName[index], entry);
                else
                    target.Children.Add(entry);
                index++;
            }
        }
    }

    private static SkyDefinition Build(List<RawLine> lines)
    {
        RawLine? Line(string name) => lines.FirstOrDefault(l => l.Name == name);
        foreach (var shape in Lines.Where(s => s.Required))
            if (Line(shape.Name) is null)
                throw new SkyException(null, lines.Count > 0 ? lines.Max(LastLine) : 1, $"A sky needs a '{shape.Name}' line");
        RawLine Entry(RawLine line, string name) =>
            line.Children.FirstOrDefault(c => c.Name == name) ?? throw new SkyException(line.File, line.Line, $"The sky's {line.Name} needs '{name}' indented below it");

        var light = Values.Of(Line("light")!);
        var definition = new SkyDefinition(
            new SkyLight(light.Number("azimuth", -360, 360, "degrees from +z towards +x, -360 to 360"),
                light.Number("elevation", -90, 90, "degrees above the horizon, -90 to 90"), light.Color("color")),
            Tone(Values.Of(Line("tone")!)),
            Gradient(Line("gradient")!),
            Post(Values.Of(Line("post")!)));

        if (definition.Gradient.Bands.Count > MaxBands)
            throw new SkyException(Line("gradient")!.File, Line("gradient")!.Line, $"The gradient and the abyss have at most {MaxBands} bands together");
        if (Line("abyss") is { } abyssLine)
        {
            var bands = Bands(abyssLine);
            var glow = abyssLine.Children.FirstOrDefault(c => c.Name == "glow") is { } glowLine ? Values.Of(glowLine) : null;
            definition = definition with
            {
                Abyss = new SkyAbyss(bands, glow is null ? null : new SkyGlow(glow.Number("elevation", -90, 0, "degrees below the horizon, -90 to 0"),
                    glow.Number("width", 0.1f, 90, "degrees, 0.1 to 90"), glow.Color("color"))),
            };
            if (bands[0].From > definition.Gradient.Bands[^1].From)
                throw new SkyException(abyssLine.Children[0].File, abyssLine.Children[0].Line,
                    "The abyss begins below the gradient's lowest band: its first band's 'from' is at most that band's");
            if (bands.Count + definition.Gradient.Bands.Count > MaxBands)
                throw new SkyException(abyssLine.File, abyssLine.Line, $"The gradient and the abyss have at most {MaxBands} bands together");
        }

        if (Line("stars") is { } starsLine)
        {
            var stars = Values.Of(starsLine);
            definition = definition with
            {
                Stars = new SkyStars(stars.Number("density", 0, 1, "the share of the sky's cells with a star, 0 to 1"),
                    stars.Number("size", 0.5f, 8, "pixels, 0.5 to 8"), stars.Number("twinkle", 0, 1, "0 to 1"),
                    stars.Number("tint", 0, 1, "0 to 1"), stars.Number("minElevation", -90, 90, "degrees, -90 to 90")),
            };
        }

        if (Line("fade") is { } fadeLine)
        {
            var fade = Values.Of(fadeLine);
            var start = fade.Number("start", 0, 10000, "metres, 0 to 10000");
            var end = fade.Number("end", 0, 10000, "metres, 0 to 10000");
            if (end <= start)
                throw fade.Error("end", "The fade ends further away than it starts");
            definition = definition with { Fade = new SkyFade(start, end, fade.Number("amount", 0, 1, "0 to 1")) };
        }

        if (Line("moon") is { } moonLine)
        {
            var moon = Values.Of(moonLine);
            var turn = Values.Of(Entry(moonLine, "turn"));
            var blink = Values.Of(Entry(moonLine, "blink"));
            definition = definition with
            {
                Moon = new SkyMoon(moon.Model(), moon.Number("size", 0.1f, 90, "degrees across, 0.1 to 90"), moon.Number("glow", 0, 10, "0 to 10"),
                    new SkyTurn(turn.Number("amount", 0, 45, "degrees, 0 to 45"), turn.Number("period", 0.1f, 1000, "seconds, 0.1 to 1000")),
                    new SkyBlink(blink.Range("every", 0.1f, 1000, "seconds between blinks, like (9, 25)"),
                        blink.Number("duration", 0.01f, 10, "seconds, 0.01 to 10"))),
            };
        }

        if (Line("floaters") is { } floatersLine)
        {
            var floaters = Values.Of(floatersLine);
            definition = definition with
            {
                Floaters = new SkyFloaters(floaters.Model(), floaters.Count("count", MaxFloaters),
                    floaters.Range("distance", 1, 10000, "metres, like (250, 400)"), floaters.Range("elevation", -90, 90, "degrees, like (-10, 18)"),
                    floaters.Number("scale", 0.01f, 1000, "0.01 to 1000"), floaters.Number("drift", 0, 100, "metres a second, 0 to 100"),
                    floaters.Number("bob", 0, 100, "metres, 0 to 100")),
            };
        }

        if (Line("spores") is { } sporesLine)
        {
            SkyAbyssSpores? abyss = null;
            SkyNearSpores? near = null;
            if (sporesLine.Children.FirstOrDefault(c => c.Name == "abyss") is { } abyssLine2)
            {
                var spores = Values.Of(abyssLine2);
                var from = spores.Number("from", -1000, 1000, "metres, -1000 to 1000");
                var to = spores.Number("to", -1000, 1000, "metres, -1000 to 1000");
                if (to <= from)
                    throw spores.Error("to", "The spores rise: 'to' is above 'from'");
                abyss = new SkyAbyssSpores(spores.Count("count", MaxSpores), spores.Color("color"), spores.Color("altColor"),
                    spores.Number("rise", 0, 100, "metres a second, 0 to 100"), spores.Number("size", 0.001f, 10, "metres, 0.001 to 10"), from, to);
            }
            if (sporesLine.Children.FirstOrDefault(c => c.Name == "near") is { } nearLine)
            {
                var spores = Values.Of(nearLine);
                near = new SkyNearSpores(spores.Count("count", MaxSpores), spores.Number("radius", 1, 1000, "metres, 1 to 1000"), spores.Color("color"),
                    spores.Number("size", 0.001f, 10, "metres, 0.001 to 10"), spores.Number("drift", 0, 100, "metres a second, 0 to 100"));
            }
            definition = definition with { Spores = new SkySpores(abyss, near) };
        }
        return definition;
    }

    private static int LastLine(RawLine line) => line.Children.Count > 0 ? Math.Max(line.Line, line.Children.Max(LastLine)) : line.Line;

    private static SkyTone Tone(Values tone) => new(tone.Number("brightness", 0, 2, "0 to 2"),
        tone.Vector3("shadowTint", 0, 4, "a multiplier per channel like (0.8, 0.85, 1.1), 0 to 4"), tone.Number("glowKeep", 0, 1, "0 to 1"));

    private static SkyGradient Gradient(RawLine line) =>
        new(Values.Of(line).Number("softness", 0, 20, "degrees, 0 to 20"), Bands(line));

    private static SkyPost Post(Values post) => new(post.Number("exposure", -10, 10, "EV, -10 to 10"), post.Number("bloom", 0, 10, "0 to 10"));

    /// <summary>The bands below a line, highest first.</summary>
    private static IReadOnlyList<SkyBand> Bands(RawLine line)
    {
        var bands = new List<SkyBand>();
        foreach (var bandLine in line.Children.Where(c => c.Name == "band"))
        {
            var band = Values.Of(bandLine);
            var from = band.Number("from", -90, 90, "degrees above the horizon, -90 to 90");
            if (bands.Count > 0 && from >= bands[^1].From)
                throw band.Error("from", $"The {line.Name}'s bands go from the highest down: each one's 'from' is below the one before");
            bands.Add(new SkyBand(from, band.Color("color")));
        }
        return bands.Count > 0 ? bands : throw new SkyException(line.File, line.Line, $"The sky's {line.Name} needs bands indented below it, like 'band from=10 color=(0.1, 0.1, 0.3)'");
    }

    private static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>What a line may hold: its parameters, and the kinds of entries indented below it.</summary>
    private sealed record Shape(string Name, string[] Keys, Shape[] Children, bool Required = false, bool Repeated = false);

    /// <summary>The parameters of one line, each required, read and checked with its own place for errors.</summary>
    private sealed class Values(RawLine line)
    {
        private readonly string owner = line.Name;

        public static Values Of(RawLine line) => new(line);

        private RawSetting Get(string name, string expected) =>
            line.Values.TryGetValue(name, out var value)
                ? value
                : throw new SkyException(line.File, line.Line, $"The sky's {owner} is missing parameter '{name}': {expected}");

        public SkyException Error(string name, string problem) =>
            line.Values.TryGetValue(name, out var value) ? value.Error(problem) : new SkyException(line.File, line.Line, problem);

        public float Number(string name, float min, float max, string expected)
        {
            var given = Get(name, expected);
            if (given.Value is not RawNumber number)
                throw given.Error($"'{name}' of the sky's {owner} needs {expected}");
            var value = float.Parse(number.Text, CultureInfo.InvariantCulture);
            return value >= min && value <= max ? value : throw given.Error($"'{name}' of the sky's {owner} is {number.Text}, but needs {expected}");
        }

        public int Count(string name, int max)
        {
            var expected = $"a whole number, 0 to {max}";
            var given = Get(name, expected);
            return given.Value is RawNumber { IsWhole: true } number && int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count <= max
                ? count
                : throw given.Error($"'{name}' of the sky's {owner} needs {expected}");
        }

        public Vector3 Color(string name) => Vector3(name, 0, 100, "a linear colour like (0.26, 0.28, 0.38)");

        public Vector3 Vector3(string name, float min, float max, string expected)
        {
            var given = Get(name, expected);
            if (given.Value is not RawVector { Components: [var x, var y, var z] })
                throw given.Error($"'{name}' of the sky's {owner} needs {expected}");
            var value = new Vector3(Parse(x), Parse(y), Parse(z));
            return value.X >= min && value.Y >= min && value.Z >= min && value.X <= max && value.Y <= max && value.Z <= max
                ? value
                : throw given.Error($"'{name}' of the sky's {owner} needs {expected}, each from {FileSyntax.Number(min)} to {FileSyntax.Number(max)}");
        }

        /// <summary>A range (least, greatest), like every=(9, 25).</summary>
        public Vector2 Range(string name, float min, float max, string expected)
        {
            var given = Get(name, expected);
            if (given.Value is not RawVector { Components: [var a, var b] })
                throw given.Error($"'{name}' of the sky's {owner} needs {expected}");
            var value = new Vector2(Parse(a), Parse(b));
            if (value.X < min || value.Y > max)
                throw given.Error($"'{name}' of the sky's {owner} needs {expected}, from {FileSyntax.Number(min)} to {FileSyntax.Number(max)}");
            return value.X <= value.Y ? value : throw given.Error($"'{name}' of the sky's {owner} is a range from the least to the greatest, {expected}");
        }

        public string Model()
        {
            var given = Get(SkyFile.Model, "a model's name, like eye-moon");
            return ((RawName)given.Value).Name;
        }

        private static float Parse(RawNumber number) => float.Parse(number.Text, CultureInfo.InvariantCulture);
    }
}

/// <summary>A line of a sky (a preset's, or a level's override), with its parameters and entries as written.</summary>
internal sealed class RawLine(string name, int line, string? file)
{
    public string Name { get; } = name;

    public int Line { get; } = line;

    /// <summary>The preset's file it is in; null for a level's own line.</summary>
    public string? File { get; } = file;

    public Dictionary<string, RawSetting> Values { get; } = [];

    public List<RawLine> Children { get; } = [];

    /// <summary>An override that replaces the preset's entries below its line rather than merging into them.</summary>
    public bool Replace { get; set; }
}

/// <summary>A parameter's value as written, and where: in the level (<see cref="File"/> null) or in a preset's file.</summary>
internal sealed record RawSetting(RawValue Value, string? File, int Line)
{
    public SkyException Error(string problem) => new(File, Line, problem);
}

/// <summary>A problem on a line of a level (<see cref="File"/> null) or of a preset.</summary>
internal sealed class SkyException(string? file, int line, string problem) : Exception(problem)
{
    public string? File { get; } = file;

    public int Line { get; } = line;

    public string Problem { get; } = problem;
}

/// <summary>
/// A level's sky section: the preset it picks and the lines it overrides, read but not yet merged, since the preset is only looked up
/// when the level is baked.
/// </summary>
public sealed class SkySection
{
    internal SkySection(string preset, int line, IReadOnlyList<RawLine> overrides)
    {
        Preset = preset;
        Line = line;
        Overrides = overrides;
    }

    public string Preset { get; }

    /// <summary>The section's line in the level file.</summary>
    public int Line { get; }

    internal IReadOnlyList<RawLine> Overrides { get; }

    /// <summary>The names of the preset's lines the level overrides, in its order.</summary>
    public IReadOnlyList<string> Overridden => Overrides.Select(o => o.Name).ToArray();
}

/// <param name="Preset">The preset's name (for the report; level.json holds only the resolved sky).</param>
/// <param name="Hash">A short hash of the preset's file.</param>
public sealed record ResolvedSky(SkyDefinition Definition, string Preset, string Hash);
