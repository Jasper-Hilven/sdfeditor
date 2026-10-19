using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// A level as its file describes it (see <see cref="LevelFile"/>), in metres: x across, z along, y up, the origin at the centre of the
/// playable area.
/// </summary>
/// <param name="Seed">Seeds every bit of noise in the level, so the same file and seed always bake the same ground.</param>
public sealed record LevelDefinition(PlayArea Play, int Seed, HillRing Hills, IslandEdge Edge)
{
    public const int DefaultSeed = 1;

    /// <summary>The ground's regions other than grass, in the file's order: where they overlap, the later one wins.</summary>
    public IReadOnlyList<GroundRegion> Regions { get; init; } = [];

    /// <summary>The unique models placed in the level (castles, big rocks), in the file's order.</summary>
    public IReadOnlyList<Placement> Placements { get; init; } = [];
}

/// <summary>
/// The playable rectangle, centred on the origin. It is flat but for a gentle unevenness of up to <paramref name="NoiseAmplitude"/>
/// metres in features about <paramref name="NoiseScale"/> metres wide, so walking it stays easy.
/// </summary>
public sealed record PlayArea(Vector2 Size, float NoiseAmplitude, float NoiseScale)
{
    public const float DefaultNoiseAmplitude = 0.2f;
    public const float DefaultNoiseScale = 14;
    public const float MaxNoiseAmplitude = 0.3f;

    public Vector2 HalfSize => Size / 2;
}

/// <summary>
/// The hills around the playable rectangle: they rise from its edge to a crest <paramref name="Height"/> metres high
/// <paramref name="Width"/> metres out, uneven by up to <paramref name="NoiseAmplitude"/> metres in features about
/// <paramref name="NoiseScale"/> metres wide, more so the further out.
/// </summary>
public sealed record HillRing(float Width, float Height, float NoiseAmplitude, float NoiseScale)
{
    public const float DefaultNoiseScale = 18;

    /// <summary>No hills: the ground stays at the playable area's height up to the edge.</summary>
    public static readonly HillRing None = new(0, 0, 0, DefaultNoiseScale);
}

/// <summary>Where the island ends: <paramref name="Margin"/> metres behind the hills' crest.</summary>
public sealed record IslandEdge(float Margin)
{
    public static readonly IslandEdge None = new(0);
}

/// <summary>
/// A region of the ground drawn in another material than grass: <see cref="Name"/> is its role (dirt, tiles, path), and its material is
/// materials/ground/ground_&lt;role&gt;.sdfmat. Its area is the union of its <see cref="Shapes"/>, plus the ground the placements that
/// name it paint (see <see cref="Placement.Ground"/>).
/// </summary>
/// <param name="Pattern">How the material is drawn: plainly, or as stones with grout lines between them.</param>
/// <param name="StoneSize">The stones' length in metres, for <see cref="GroundPattern.Stones"/>.</param>
/// <param name="GroutWidth">The width of the grout lines between stones in metres, for <see cref="GroundPattern.Stones"/>.</param>
public sealed record GroundRegion(string Name, IReadOnlyList<RegionShape> Shapes, GroundPattern Pattern, float StoneSize, float GroutWidth)
{
    /// <summary>The region that is drawn as stones unless its line says otherwise.</summary>
    public const string Tiles = "tiles";

    public const float DefaultStoneSize = 0.6f;
    public const float DefaultGroutWidth = 0.05f;

    /// <summary>The prefix of a region's material: the region dirt is drawn in ground_dirt.</summary>
    public const string MaterialPrefix = "ground_";

    public string Material => MaterialPrefix + Name;
}

public enum GroundPattern
{
    Plain,

    /// <summary>A world-space offset grid of stones (an offset brick bond), with grout lines and bevelled edges, drawn by the shader.</summary>
    Stones,
}

/// <summary>
/// A unique model in the level, by its name in content/models/ (castle0, rock1). Its base, the model's y = 0, sits on the ground at
/// <see cref="At"/>, turned <see cref="Yaw"/> degrees about +y (counter-clockwise seen from above, as glTF turns: +z towards +x).
/// </summary>
/// <param name="Flatten">Within this many metres of <see cref="At"/> the ground is levelled to the height it had there, so the model stands level;
/// beyond it the ground goes back to its own height over <see cref="FlattenFalloff"/> metres. 0 leaves the ground as it is.</param>
/// <param name="Ground">The region the ground under the model's footprint, grown by <see cref="GroundMargin"/>, is painted with; null for none.</param>
/// <param name="Line">The line of the level file it is on, for errors found while baking (a model that is not there).</param>
public sealed record Placement(string Model, Vector2 At, float Yaw, float Scale, float Flatten, float FlattenFalloff, string? Ground,
    float GroundMargin, int Line)
{
    public const float DefaultFlattenFalloff = 6;
    public const float DefaultGroundMargin = 2;
}
