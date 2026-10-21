using System.Numerics;
using SDFPipeline.Skies;

namespace SDFPipeline.Levels;

/// <summary>
/// A level as its file describes it (see <see cref="LevelFile"/>), in metres: x across, z along, y up, the origin at the centre of the
/// playable area.
/// </summary>
/// <param name="Seed">Seeds every bit of noise in the level, so the same file and seed always bake the same ground.</param>
public sealed record LevelDefinition(PlayArea Play, int Seed, HillRing Hills, IslandEdge Edge)
{
    public const int DefaultSeed = 1;

    public const string DefaultBase = "grass";

    /// <summary>
    /// The ground outside every region, named as a region is: <c>moss</c> is materials/ground/ground_moss.sdfmat. Set by the regions
    /// line's <c>base=</c>; grass when left out. level.json names its material under the role <c>grass</c> whatever it is.
    /// </summary>
    public string Base { get; init; } = DefaultBase;

    /// <summary>The material of the ground outside every region.</summary>
    public string BaseMaterial => GroundRegion.MaterialPrefix + Base;

    /// <summary>The ground's regions other than the base ground, in the file's order: where they overlap, the later one wins.</summary>
    public IReadOnlyList<GroundRegion> Regions { get; init; } = [];

    /// <summary>The unique models placed in the level (castles, big rocks), in the file's order.</summary>
    public IReadOnlyList<Placement> Placements { get; init; } = [];

    /// <summary>The models scattered over the ground by the thousand (grass turfs, flowers), one layer per scatter section, in the file's order.</summary>
    public IReadOnlyList<ScatterLayer> Scatters { get; init; } = [];

    /// <summary>
    /// The trees on the border hills and the clusters along their crests, one layer per trees section, in the file's order: scattered
    /// like <see cref="Scatters"/>, but in a band of distance from the playable rectangle (<see cref="ScatterLayer.Band"/>).
    /// </summary>
    public IReadOnlyList<ScatterLayer> Trees { get; init; } = [];

    /// <summary>How the instanced renderer draws each group of scattered models, by the group's name.</summary>
    public IReadOnlyList<DrawGroup> DrawGroups { get; init; } = [];

    /// <summary>The sky the level picks, with its overrides, resolved when it is baked; null for <see cref="SkyFile.DefaultPreset"/>.</summary>
    public SkySection? Sky { get; init; }

    /// <summary>The closed room over the playable area (walls and ceiling), or null for a level open to the sky.</summary>
    public RoomSpec? Room { get; init; }

    /// <summary>The two battle sides and the neutral pods between them (ticket 17), or null for a level without them.</summary>
    public BattleLayout? Battle { get; init; }
}

/// <summary>
/// A closed room of six chessboards over the playable area: the floor (the playable rectangle, centred on the origin at y = 0), four walls
/// standing on its edges and a ceiling at y = <c>Size.Y</c> facing down. Its squares are <paramref name="Square"/> metres across and
/// coloured by <see cref="RoomChecker"/>.
/// </summary>
public sealed record RoomSpec(Vector3 Size, float Square)
{
    public const float DefaultSquare = 8;

    /// <summary>How many squares the room has along x, y and z.</summary>
    public (int X, int Y, int Z) Squares => ((int)MathF.Round(Size.X / Square), (int)MathF.Round(Size.Y / Square), (int)MathF.Round(Size.Z / Square));
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

/// <summary>
/// Where the island ends: <paramref name="Margin"/> metres behind the hills' crest. With a <see cref="Rim"/>, the ground's last
/// <see cref="Shoulder"/> metres bend down to that height, the same all round, so the rocky underside (<see cref="Underside"/>) meets it
/// along one level line.
/// </summary>
public sealed record IslandEdge(float Margin)
{
    public const float DefaultShoulder = 2;

    public static readonly IslandEdge None = new(0);

    /// <summary>The ground's height at the island's edge, in metres; null to leave it where the hills' back slope ends.</summary>
    public float? Rim { get; init; }

    /// <summary>How far in from the edge the ground starts bending down to the <see cref="Rim"/>, in metres.</summary>
    public float Shoulder { get; init; } = DefaultShoulder;

    /// <summary>The model that is the island's underside below the edge; null for none.</summary>
    public IslandUnderside? Underside { get; init; }
}

/// <summary>
/// The island's rocky underside: a model from content/models/ by name, its origin at the playable area's centre at height 0, its top
/// following the island's edge just outside it at the edge's <see cref="IslandEdge.Rim"/> height. Drawn without outline or shadows.
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors found while baking (a model that is not there, or does not fit).</param>
public sealed record IslandUnderside(string Model, int Line);

/// <summary>
/// A region of the ground drawn in another material than grass: <see cref="Name"/> is its role (dirt, tiles, path), and its material is
/// materials/ground/ground_&lt;role&gt;.sdfmat. Its area is the union of its <see cref="Shapes"/>, plus the ground the placements that
/// name it paint (see <see cref="Placement.Ground"/>).
/// </summary>
/// <param name="Pattern">How the material is drawn: plainly, or as stones with grout lines between them.</param>
/// <param name="StoneSize">The stones' length in metres, for <see cref="GroundPattern.Stones"/>.</param>
/// <param name="GroutWidth">The width of the grout lines between stones in metres, for <see cref="GroundPattern.Stones"/>.</param>
/// <param name="Wobble">Metres the ground shader wobbles the region's edge with fine noise, so that it is not ruled; 0 keeps it straight.</param>
public sealed record GroundRegion(string Name, IReadOnlyList<RegionShape> Shapes, GroundPattern Pattern, float StoneSize, float GroutWidth, float Wobble = GroundRegion.DefaultWobble)
{
    /// <summary>The region that is drawn as stones unless its line says otherwise.</summary>
    public const string Tiles = "tiles";

    public const float DefaultStoneSize = 0.6f;
    public const float DefaultGroutWidth = 0.05f;
    public const float DefaultWobble = 0.06f;

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
/// <see cref="At"/> (or <see cref="Y"/> metres above it), turned <see cref="Yaw"/> degrees about +y (counter-clockwise seen from above,
/// as glTF turns: +z towards +x).
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

    /// <summary>
    /// How far above the ground at <see cref="At"/> the model's base is, in metres: 0 (the default) stands it on the ground; more puts
    /// it on something there, like the team mushrooms on a castle's tower caps and parapets (ticket 19), whose heights are the castle's
    /// over its own base, which stands on the same level ground (its plateau).
    /// </summary>
    public float Y { get; init; }
}

/// <summary>A model a scatter layer picks from, <see cref="Weight"/> times as often as one of weight 1.</summary>
/// <param name="Line">The line of the level file it is on, for errors found while baking.</param>
public sealed record ScatterEntry(string Model, float Weight, int Line);

/// <summary>
/// Models scattered over the ground, drawn by the instanced renderer in draw group <see cref="Group"/>. Points are spread
/// <see cref="Spacing"/> metres apart or more (Poisson-disk sampling), and each is kept so that, on average, <see cref="Density"/>
/// instances stand on a square metre; clustering noise <see cref="ClusterScale"/> metres wide makes that up to <see cref="Cluster"/>
/// times as many in places and none in others. Nothing stands within <see cref="Margin"/> metres of a region in <see cref="Avoid"/>, on
/// a placement's footprint, on ground steeper than <see cref="MaxSlope"/> degrees, or more than <see cref="Reach"/>
/// metres up the hills from the playable rectangle.
/// </summary>
/// <param name="Scale">The smallest and largest scale an instance gets, at random between them.</param>
/// <param name="Line">The line of the level file the section is on.</param>
/// <remarks>
/// A trees section (<see cref="LevelDefinition.Trees"/>) is a layer with a <see cref="Band"/> instead of a reach: its instances stand
/// between Band.X and Band.Y metres from the playable rectangle's edge (negative inside it), and each stands on the lowest ground within
/// <see cref="LevelScatter.FootRadius"/> of its point, so a trunk on a slope does not float.
/// </remarks>
public sealed record ScatterLayer(IReadOnlyList<ScatterEntry> Entries, string Group, float Density, float Cluster, float ClusterScale,
    float Spacing, IReadOnlyList<string> Avoid, float Margin, float MaxSlope, float Reach, Vector2 Scale, int Line)
{
    public const string DefaultGroup = "foliage";
    public const string DefaultTreesGroup = "trees";
    public const float DefaultCluster = 1;
    public const float DefaultClusterScale = 8;
    public const float DefaultMaxSlope = 90;
    public const float DefaultReach = 3;
    public static readonly Vector2 DefaultScale = new(0.85f, 1.15f);

    /// <summary>For a trees section: the least and greatest distance from the playable rectangle's edge, negative inside it; null for a scatter.</summary>
    public Vector2? Band { get; init; }
}

/// <summary>
/// How the instanced renderer draws a group of scattered models: in rings round the camera, ring k (out to <see cref="Rings"/>[k]
/// metres) with level of detail k (or the model's coarsest), nothing beyond the last. The first <see cref="OutlineRings"/> rings get the
/// ink outline and the first <see cref="ShadowRings"/> cast shadows (<see cref="LowShadowRings"/> on the integrated GPU / web preset).
/// Neighbouring rings cross-fade by dither over <see cref="Blend"/> metres round their border, and the last fades out over its last
/// <see cref="Fade"/> metres. The models sway <see cref="Wind"/> metres at <see cref="WindHeight"/> metres above their base (less below,
/// none at it), and their lowest <see cref="TintHeight"/> metres blend into the instance's tint, the ground's colour (0 for none). The
/// integrated GPU / web preset draws <see cref="LowDensity"/> of the instances (the same ones each frame) with every distance times
/// <see cref="LowDistance"/>.
/// </summary>
/// <param name="Line">The line of the level file the section is on.</param>
public sealed record DrawGroup(string Name, IReadOnlyList<float> Rings, int OutlineRings, int ShadowRings, int LowShadowRings, float Blend,
    float Fade, float Wind, float WindHeight, float TintHeight, float LowDensity, float LowDistance, int Line)
{
    public const float DefaultBlend = 1;
}
