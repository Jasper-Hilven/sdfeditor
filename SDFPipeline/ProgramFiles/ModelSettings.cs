using System.Globalization;

namespace SDFPipeline.ProgramFiles;

/// <summary>What the game engine gives a model to collide with (Unity's MushroomBuilder adds it to the prefab).</summary>
public enum ColliderShape
{
    /// <summary>Nothing collides with it: grass, flowers, props.</summary>
    None,

    /// <summary>The convex hull of a level of detail: rocks and other compact solids.</summary>
    Convex,

    /// <summary>The mesh of a level of detail as it is, concave and static: castles, whose courtyards and stairs are walked in.</summary>
    Mesh,

    /// <summary>An upright capsule: tree trunks.</summary>
    Capsule,
}

/// <summary>Which levels of detail of a model or part cast shadows.</summary>
public enum ShadowCasting
{
    /// <summary>Every level: the default.</summary>
    On,

    /// <summary>None: roofs and caps that would only shade what is under them anyway, and small detail.</summary>
    Off,

    /// <summary>LOD0 only, so the shadow goes when the part is far: detail pieces near the player.</summary>
    Near,
}

/// <summary>
/// A model's own settings, from the lines between its format line and its shape that start with a setting's name, like
/// <code>
/// format 1
/// collider mesh lod=1
/// shadows near
/// </code>
/// <list type="bullet">
/// <item><c>collider</c>: <c>none</c> (the default), <c>convex</c>, <c>mesh</c> or <c>capsule</c>, and for the mesh shapes optionally
/// <c>lod=</c>, the level of detail it is made from (the coarsest there is by default, or the coarsest that is meshed when the model has
/// fewer levels);</item>
/// <item><c>shadows on|off|near</c>: which levels cast shadows, every one (the default), none, or LOD0 only;</item>
/// <item><c>outline on|off</c>: whether the toon ink line is drawn round it (on by default);</item>
/// <item><c>cull &lt;metres&gt;</c>: not drawn beyond that distance, for small detail; by default only when it is a few pixels tall.</item>
/// </list>
/// A part's file may have the same lines: they are that part's settings in every model that uses it, and what it does not set it takes
/// from the model (<see cref="Read(string, ModelSettings)"/>). The settings do not change the shape: the IDE reads such a file as any
/// other, and the mesh pipeline writes them into the .glb's extras (<see cref="Meshing.GlbWriter"/>).
/// </summary>
/// <param name="ColliderLod">The level of detail the collider is made from; null for the coarsest.</param>
public sealed record ModelSettings(ColliderShape Collider, int? ColliderLod)
{
    public const string ColliderKeyword = "collider";
    public const string ShadowsKeyword = "shadows";
    public const string OutlineKeyword = "outline";
    public const string CullKeyword = "cull";

    /// <summary>A convex collider round something larger than this, in metres, would block walkable ground: it gets the mesh instead.</summary>
    public const float LargestConvexSize = 3;

    public static readonly ModelSettings Default = new(ColliderShape.None, null);

    private static readonly string[] Keywords = [ColliderKeyword, ShadowsKeyword, OutlineKeyword, CullKeyword];

    public ShadowCasting Shadows { get; init; } = ShadowCasting.On;

    public bool Outline { get; init; } = true;

    /// <summary>The distance in metres beyond which it is not drawn; null to leave that to the size it has on screen.</summary>
    public float? Cull { get; init; }

    public static ModelSettings Load(string path) => Read(File.ReadAllText(path));

    /// <exception cref="SdfProgramFileException">A setting line is not valid.</exception>
    public static ModelSettings Read(string text) => Read(text, Default);

    /// <summary>
    /// The settings of a file that starts from <paramref name="defaults"/>: a part's, over the model that uses it, so what the part's
    /// file sets wins and the rest is the model's.
    /// </summary>
    /// <exception cref="SdfProgramFileException">A setting line is not valid.</exception>
    public static ModelSettings Read(string text, ModelSettings defaults) => Read(SourceLine.Parse(text).Skip(1).ToList(), defaults);

    /// <summary>Whether the file has any setting line, so it has settings of its own.</summary>
    public static bool HasSettings(string text) => SourceLine.Parse(text).Skip(1).Any(IsSetting);

    /// <summary>Whether a line of a program file, at its top level, is a setting rather than a declaration or a shape.</summary>
    internal static bool IsSetting(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() is { } name && Keywords.Contains(name) && cursor.Peek() is not ('=' or ':');
    }

    /// <summary>The settings among a file's top-level lines (after the format line).</summary>
    internal static ModelSettings Read(IReadOnlyList<SourceLine> lines, ModelSettings? defaults = null)
    {
        var settings = defaults ?? Default;
        var seen = new Dictionary<string, int>();
        foreach (var line in lines.Where(IsSetting))
        {
            var cursor = new Cursor(line);
            var keyword = cursor.ReadName("a setting");
            if (seen.TryGetValue(keyword, out var first))
                throw cursor.Error($"The {keyword} is given twice, first on line {first}");
            seen[keyword] = line.Number;
            if (line.Children.Count > 0)
                throw new SdfProgramFileException(line.Children[0].Number, "Nothing can be indented below a setting");

            settings = keyword switch
            {
                ColliderKeyword => ReadCollider(cursor, settings),
                ShadowsKeyword => settings with
                {
                    Shadows = cursor.TryReadName() switch
                    {
                        "on" => ShadowCasting.On,
                        "off" => ShadowCasting.Off,
                        "near" => ShadowCasting.Near,
                        _ => throw cursor.Error("Shadows are on, off or near (LOD0 only), like 'shadows near'"),
                    },
                },
                OutlineKeyword => settings with
                {
                    Outline = cursor.TryReadName() switch
                    {
                        "on" => true,
                        "off" => false,
                        _ => throw cursor.Error("The outline is on or off, like 'outline off'"),
                    },
                },
                _ => settings with
                {
                    Cull = cursor.AtEnd ? throw cursor.Error("Give the distance in metres beyond which it is not drawn, like 'cull 35'")
                        : cursor.ReadValue() is RawNumber number && float.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres)
                          && metres > 0 && float.IsFinite(metres)
                            ? metres
                            : throw cursor.Error("Give the distance in metres beyond which it is not drawn, like 'cull 35'"),
                },
            };
            cursor.ExpectEnd();
        }
        return settings;
    }

    private static ModelSettings ReadCollider(Cursor cursor, ModelSettings settings)
    {
        var shapeName = cursor.TryReadName();
        var shape = shapeName switch
        {
            "none" => ColliderShape.None,
            "convex" => ColliderShape.Convex,
            "mesh" => ColliderShape.Mesh,
            "capsule" => ColliderShape.Capsule,
            _ => throw cursor.Error("A collider is none, convex, mesh or capsule, like 'collider mesh'"),
        };
        int? lod = null;
        if (!cursor.AtEnd)
        {
            if (cursor.TryReadName() != "lod" || !cursor.TryConsume('=') || cursor.ReadValue() is not RawNumber { IsWhole: true } number
                || !int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level > 3)
                throw cursor.Error("After the collider's shape only lod= can follow, the level of detail it is made from, 0 to 3");
            if (shape is not (ColliderShape.Convex or ColliderShape.Mesh))
                throw cursor.Error($"A {shapeName} collider is not made from a mesh, so it takes no lod");
            lod = level;
        }
        return settings with { Collider = shape, ColliderLod = lod };
    }
}
