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

/// <summary>
/// A model's own settings, from the lines between its format line and its shape that start with a setting's name, like
/// <code>
/// format 1
/// collider mesh lod=1
/// </code>
/// <list type="bullet">
/// <item><c>collider</c>: <c>none</c> (the default), <c>convex</c>, <c>mesh</c> or <c>capsule</c>, and for the mesh shapes optionally
/// <c>lod=</c>, the level of detail it is made from (the coarsest there is by default, or the coarsest that is meshed when the model has
/// fewer levels). The mesh pipeline writes it into the .glb's extras (<see cref="Meshing.GlbWriter"/>).</item>
/// <item><c>surface part=&lt;name&gt;</c>: for a piece cut from a part (an exploding mushroom's pieces), the part whose thickness and
/// occlusion its surface takes where the surface is the part's own, so the piece is shaded as the whole was there and the pieces look
/// like the whole when put together; the cut faces are measured on the piece (<see cref="Meshing.MeshingOptions.SurfaceOf"/>).</item>
/// </list>
/// The settings do not change the shape: the IDE reads such a file as any other.
/// </summary>
/// <param name="ColliderLod">The level of detail the collider is made from; null for the coarsest.</param>
/// <param name="SurfacePart">The part the surface is measured on, or null for the model itself.</param>
public sealed record ModelSettings(ColliderShape Collider, int? ColliderLod, string? SurfacePart = null)
{
    public const string ColliderKeyword = "collider";
    public const string SurfaceKeyword = "surface";

    /// <summary>A convex collider round something larger than this, in metres, would block walkable ground: it gets the mesh instead.</summary>
    public const float LargestConvexSize = 3;

    public static readonly ModelSettings Default = new(ColliderShape.None, null);

    private static readonly string[] Keywords = [ColliderKeyword, SurfaceKeyword];

    public static ModelSettings Load(string path) => Read(File.ReadAllText(path));

    /// <exception cref="SdfProgramFileException">A setting line is not valid.</exception>
    public static ModelSettings Read(string text) => Read(SourceLine.Parse(text).Skip(1).ToList());

    /// <summary>Whether a line of a program file, at its top level, is a setting rather than a declaration or a shape.</summary>
    internal static bool IsSetting(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() is { } name && Keywords.Contains(name) && cursor.Peek() is not ('=' or ':');
    }

    /// <summary>The settings among a file's top-level lines (after the format line).</summary>
    internal static ModelSettings Read(IReadOnlyList<SourceLine> lines)
    {
        var settings = Default;
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
            if (keyword == SurfaceKeyword)
            {
                if (cursor.TryReadName() != "part" || !cursor.TryConsume('=') || cursor.ReadValue() is not RawName { Name: var part })
                    throw cursor.Error("The surface names the part it is measured on, like 'surface part=amanita_scout'");
                cursor.ExpectEnd();
                settings = settings with { SurfacePart = part };
                continue;
            }

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
            cursor.ExpectEnd();
            settings = settings with { Collider = shape, ColliderLod = lod };
        }
        return settings;
    }
}
