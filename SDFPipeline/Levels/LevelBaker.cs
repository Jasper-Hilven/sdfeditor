using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDFPipeline.Meshing;
using SDFPipeline.Skies;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Levels;

/// <summary>What a bake wrote: the chunk files and the triangles of the ground at each level of detail, over all chunks.</summary>
public sealed record BakedLevel(string Folder, GroundLayout Layout, IReadOnlyList<string> ChunkFiles, int[] Triangles, int[] SkirtTriangles,
    float[] MaxErrors)
{
    /// <summary>The regions as baked into the region map, those that placements paint included.</summary>
    public IReadOnlyList<GroundRegion> Regions { get; init; } = [];

    /// <summary>The placed models where they stand, in pipeline coordinates.</summary>
    public IReadOnlyList<PlacedModel> Placements { get; init; } = [];

    /// <summary>The scattered instances, as written to instances.bin.</summary>
    public ScatteredLevel Scattered { get; init; } = ScatteredLevel.None;
}

/// <summary>A placement as baked: the model's base at <see cref="Position"/>, on the (flattened) ground or its <see cref="Placement.Y"/> above it.</summary>
public sealed record PlacedModel(string Model, Vector3 Position, float Yaw, float Scale);

/// <summary>
/// Bakes a level for the game engine into a folder of its own (sdf level): the ground as chunk_&lt;i&gt;_&lt;j&gt;.glb, one per chunk
/// of <see cref="GroundLayout"/> that has ground, each with its levels of detail as separate meshes named like a model's (ground,
/// ground_LOD1, ...), and level.json with everything else the engine needs: the extent, the chunk grid, the playable rectangle, the seed,
/// where the player starts, the ground's materials by region, the region map (regions.png, see <see cref="RegionMap"/>), the placed
/// models, the scattered ones and the trees (instances.bin, see <see cref="InstanceFile"/> and <see cref="LevelScatter"/>) with their
/// draw groups, the island's edge with its underside model, the resolved sky (<see cref="SkyDocument"/>) with where its floaters and
/// spores are, the battle sides and neutral pods (<see cref="BattleLayout"/>) on the ground, and the list of every model's name. Unity's LevelBuilder turns it into the level's scene; the ground's colliders are made
/// there, from LOD1, the placed models bring theirs on their prefabs, and a scattered model whose prefab has a collider (the trees' trunk
/// capsules) gets a collider-only object per instance.
/// <para>
/// The output depends on the file and the materials only, never on the machine's timing: the same input bakes byte-identical files.
/// Chunk files from an earlier bake that this one no longer makes are deleted, as is instances.bin when nothing is scattered;
/// level.json is written last, so a watcher that sees it change knows the chunks are complete.
/// </para>
/// </summary>
public static class LevelBaker
{
    public const string LevelJson = "level.json";

    public const string ChunkPrefix = "chunk_";

    /// <summary>The ground's level-of-detail meshes are named like a model's part: ground, ground_LOD1, ground_LOD2.</summary>
    public const string GroundMeshName = "ground";

    /// <summary>The material of grass ground, from the materials folder (materials/ground/ground_grass.sdfmat).</summary>
    public const string GrassMaterial = "ground_grass";

    public const float ChunkSize = 25;

    /// <summary>
    /// LOD0 from a 0.5 m grid, LOD1 from 1 m, LOD2 from about 2 m, each simplified further: open grass flattens to a few large triangles
    /// at every level, while the hills keep the triangles their shape needs.
    /// </summary>
    public static readonly IReadOnlyList<GroundDetail> Details = [new(50, 0.008f), new(25, 0.03f), new(12, 0.1f)];

    /// <summary>The level of detail Unity makes the colliders from: 1 m is fine enough to walk on and cheap to collide with.</summary>
    public const int ColliderLevel = 1;

    /// <param name="models">
    /// Where each placed model stands on the ground, by name (see <see cref="LevelModels"/>); null when there is no model to be had, or
    /// when the level places none.
    /// </param>
    /// <param name="modelExists">
    /// Whether a model is there, by name, for the scattered models, which need no footprint (working one out samples the model's shape);
    /// null to ask <paramref name="models"/>.
    /// </param>
    /// <param name="sky">The level's sky, its preset and overrides resolved (<see cref="SkyFile.Resolve(SkySection?, Func{string, string?})"/>);
    /// null to bake without one, which Unity lights like the model showcase.</param>
    /// <exception cref="ProgramFiles.SdfProgramFileException">
    /// A placement or scatter names a model that is not there, a placement stands off the island, a scatter avoids a region the level
    /// does not have, or the sky draws a model that is not there.
    /// </exception>
    public static BakedLevel Bake(LevelDefinition level, string name, MaterialLibrary materials, string folder,
        Func<string, ModelFootprint?>? models = null, Func<string, bool>? modelExists = null, ResolvedSky? sky = null)
    {
        var terrain = new LevelTerrain(level);
        var layout = GroundLayout.Around(terrain, ChunkSize);
        foreach (var material in level.Regions.Select(r => r.Material).Prepend(level.BaseMaterial))
            if (!materials.Contains(material))
                throw new InvalidOperationException($"There is no material file for {material}; the ground needs materials/ground/{material}.sdfmat");

        var footprints = new Dictionary<string, ModelFootprint>();
        foreach (var placement in level.Placements)
        {
            if (!footprints.ContainsKey(placement.Model))
                footprints[placement.Model] = models?.Invoke(placement.Model)
                    ?? throw new ProgramFiles.SdfProgramFileException(placement.Line, $"There is no model '{placement.Model}' in content/models/");
            if (!terrain.OnIsland(placement.At.X, placement.At.Y))
                throw new ProgramFiles.SdfProgramFileException(placement.Line, $"{placement.Model} at ({placement.At.X}, {placement.At.Y}) is beyond the island's edge");
        }
        foreach (var model in sky?.Definition.Models ?? [])
            if (models?.Invoke(model) is null)
                throw new ProgramFiles.SdfProgramFileException(level.Sky?.Line ?? 1, $"The sky draws the model '{model}', but there is none in content/models/");
        // The underside need only be there: its footprint would take seconds to find for a model 200 m across. That it fits the island
        // is the model's business (the meadow's is checked against its level by a test)
        if (level.Edge.Underside is { } underside && !(modelExists ?? (name => models?.Invoke(name) is not null))(underside.Model))
            throw new ProgramFiles.SdfProgramFileException(underside.Line, $"There is no model '{underside.Model}' in content/models/");
        var regions = new GroundRegions(level, model => footprints[model]);
        var holes = new GroundHoles(level.Holes, level.Seed);
        foreach (var region in regions.Regions)
            if (!materials.Contains(region.Material))
                throw new InvalidOperationException($"There is no material file for {region.Material}; the ground needs materials/ground/{region.Material}.sdfmat");
        // The backdrop (ticket 28.2) is a model like the underside: it need only be there
        if (level.Backdrop is { } backdropModel && !(modelExists ?? (name => models?.Invoke(name) is not null))(backdropModel.Model))
            throw new ProgramFiles.SdfProgramFileException(backdropModel.Line, $"There is no model '{backdropModel.Model}' in content/models/");
        foreach (var face in new[] { level.Room?.Walls, level.Room?.Ceiling })
            if (face is not null && !materials.Contains(face))
                throw new InvalidOperationException($"There is no material file for {face}; the room's walls and ceiling need materials/room/{face}.sdfmat");
        var placed = level.Placements
            .Select(p => new PlacedModel(p.Model, new Vector3(p.At.X, terrain.Height(p.At.X, p.At.Y) + p.Y, p.At.Y), p.Yaw, p.Scale))
            .ToArray();
        // The battle objects stand wholly inside the playable rectangle, where the player can reach them
        foreach (var item in level.Battle?.Objects ?? [])
            if (MathF.Abs(item.At.X) > level.Play.HalfSize.X - item.Radius || MathF.Abs(item.At.Y) > level.Play.HalfSize.Y - item.Radius)
                throw new ProgramFiles.SdfProgramFileException(item.Line,
                    $"The {item.Name} at ({item.At.X}, {item.At.Y}) is not {item.Radius} m or more inside the playable area, where the player can reach it");

        // The scattered models need only be there; the ground's colour under each instance is its material's average
        foreach (var entry in level.Scatters.Concat(level.Trees).SelectMany(s => s.Entries))
            if (!(modelExists ?? (name => models?.Invoke(name) is not null))(entry.Model))
                throw new ProgramFiles.SdfProgramFileException(entry.Line, $"There is no model '{entry.Model}' in content/models/");
        // The movers' cars (ticket 27.3) need their models, and their paths stay on the island
        foreach (var mover in level.Movers)
        {
            foreach (var car in mover.Cars)
                if (!(modelExists ?? (name => models?.Invoke(name) is not null))(car.Model))
                    throw new ProgramFiles.SdfProgramFileException(car.Line, $"There is no model '{car.Model}' in content/models/");
            foreach (var corner in mover.Path)
                if (!terrain.OnIsland(corner.X, corner.Y))
                    throw new ProgramFiles.SdfProgramFileException(mover.Line, $"Mover {mover.Name}'s path goes beyond the island's edge, at ({corner.X}, {corner.Y})");
        }
        var groundColors = regions.Regions.Select(r => r.Material).Prepend(level.BaseMaterial)
            .Select(m => AverageColor(materials.Resolve(m))).ToArray();
        // The trees sections' models need their footprints too where a later section keeps clear of them (LevelScatter.KeepsClearOf),
        // worked out when first asked for; with no models to be had, such an instance is only its point
        ModelFootprint FootprintOf(string model) => footprints.TryGetValue(model, out var known)
            ? known
            : footprints[model] = models?.Invoke(model) ?? new ModelFootprint(Vector2.Zero, Vector2.Zero);
        var scattered = LevelScatter.Scatter(level, terrain, regions, FootprintOf, p =>
        {
            var region = regions.At(p);
            return groundColors[region is null ? 0 : regions.Regions.ToList().IndexOf(region) + 1];
        }, holes);

        // Each chunk on its own, in parallel; the results go in order, so the files do not depend on which finished first
        var chunks = new GroundMesh?[layout.CountX * layout.CountZ][];
        Parallel.For(0, chunks.Length, index =>
        {
            var (i, j) = (index % layout.CountX, index / layout.CountX);
            chunks[index] = Details.Select(detail => GroundMesher.Mesh(terrain, layout, i, j, detail, holes)).ToArray();
        });

        Directory.CreateDirectory(folder);
        var files = new List<string>();
        var chunkEntries = new List<object>();
        for (var index = 0; index < chunks.Length; index++)
        {
            var (i, j) = (index % layout.CountX, index / layout.CountX);
            if (chunks[index].Any(lod => lod is null))
                continue;
            var lods = chunks[index].Select(lod => lod!).ToArray();
            var file = $"{ChunkPrefix}{i}_{j}{GlbWriter.Extension}";
            GlbWriter.SaveAtomically(Path.Combine(folder, file), GroundGlb(file, layout, i, j, lods));
            files.Add(file);
            chunkEntries.Add(new
            {
                file,
                i,
                j,
                min = Array(layout.Min(i, j)),
                max = Array(layout.Max(i, j)),
                heights = new[] { lods[0].Positions.Min(p => p.Y), lods[0].Positions.Max(p => p.Y) },
                triangles = lods.Select(l => l.TriangleCount).ToArray(),
            });
        }

        // Chunks an earlier bake made that this one does not (the level shrank)
        foreach (var stale in Directory.EnumerateFiles(folder, $"{ChunkPrefix}*{GlbWriter.Extension}").Order(StringComparer.Ordinal))
            if (!files.Contains(Path.GetFileName(stale)))
                File.Delete(stale);

        var baked = chunks.Where(c => c.All(lod => lod is not null)).ToArray();
        int[] Sum(Func<GroundMesh, int> count) => Enumerable.Range(0, Details.Count).Select(l => baked.Sum(c => count(c[l]!))).ToArray();
        var triangles = Sum(m => m.TriangleCount);
        var skirts = Sum(m => m.SkirtTriangles);
        var maxErrors = Enumerable.Range(0, Details.Count).Select(l => baked.Max(c => c[l]!.MaxError)).ToArray();

        // The region map before level.json, which names its hash: a watcher that sees level.json change finds the map complete
        var map = RegionMap.Bake(regions, -terrain.HalfExtent, terrain.HalfExtent);
        var png = map.ToPng();
        GlbWriter.SaveAtomically(Path.Combine(folder, RegionMap.FileName), png);

        // The scattered instances, the same way: in a grid of cells over the island
        var cellsX = (int)MathF.Ceiling(2 * terrain.HalfExtent.X / InstanceFile.CellSize - 1e-4f);
        var cellsZ = (int)MathF.Ceiling(2 * terrain.HalfExtent.Y / InstanceFile.CellSize - 1e-4f);
        var groups = level.DrawGroups.Select(g => g.Name).ToArray();
        var instancesPath = Path.Combine(folder, InstanceFile.FileName);
        byte[]? instances = null;
        if (scattered.Instances.Count > 0)
        {
            instances = InstanceFile.Write(scattered, groups, -terrain.HalfExtent, cellsX, cellsZ);
            GlbWriter.SaveAtomically(instancesPath, instances);
        }
        else if (File.Exists(instancesPath))
        {
            File.Delete(instancesPath);
        }

        var play = level.Play;
        var hills = level.Hills;
        var document = new
        {
            format = 1,
            name,
            generator = "SDFPipeline (sdf level)",
            units = "metres",
            up = "+Y",
            // Positions are the pipeline's, as in the .glb files: Unity's importer mirrors x, as it does for models
            coordinates = "x across, z along, y up, origin at the centre of the playable area; Unity mirrors x like it does for the .glb files",
            seed = level.Seed,
            play = new { size = Array(play.Size), min = Array(-play.HalfSize), max = Array(play.HalfSize), noise = play.NoiseAmplitude, noiseScale = play.NoiseScale },
            hills = new { width = hills.Width, height = hills.Height, noise = hills.NoiseAmplitude, noiseScale = hills.NoiseScale },
            // With a rim, the ground's last shoulder metres bend down to the rim's height all round, where the underside model (placed
            // at the origin, as a unique model, without outline or shadows) meets it
            edge = new
            {
                margin = level.Edge.Margin,
                distance = terrain.EdgeDistance,
                rim = level.Edge.Rim,
                shoulder = level.Edge.Rim is null ? (float?)null : level.Edge.Shoulder,
                underside = level.Edge.Underside?.Model,
            },
            extent = new { min = Array(-terrain.HalfExtent), max = Array(terrain.HalfExtent) },
            spawn = new[] { 0, terrain.Height(0, 0), 0 },
            ground = new
            {
                chunkSize = layout.ChunkSize,
                chunkCount = new[] { layout.CountX, layout.CountZ },
                origin = Array(layout.Origin),
                lods = Details.Select((d, l) => new
                {
                    mesh = GlbWriter.LodName(GroundMeshName, l),
                    cellSize = layout.ChunkSize / d.Cells,
                    simplifyError = d.MaxError,
                    maxError = maxErrors[l],
                    triangles = triangles[l],
                    skirtTriangles = skirts[l],
                }).ToArray(),
                colliderLod = ColliderLevel,
                skirtDepth = GroundMesher.SkirtDepth,
                chunks = chunkEntries,
            },
            // The ground's material by region: the base ground under the role "grass" (whatever its material), then each region's
            materials = regions.Regions.Select(r => (Role: r.Name, Material: r.Material)).Prepend((Role: "grass", Material: level.BaseMaterial))
                .ToDictionary(r => r.Role, r => (object)new { name = r.Material, parameters = Parameters(materials.Resolve(r.Material)) }),
            regions = new
            {
                texture = RegionMap.FileName,
                encoding = "RGBA8, one channel per region in order (r first): (d / range + 1) / 2 with d the signed distance to the region's " +
                           "edge in metres at the texel's centre, negative inside; 1 where a channel has no region; rows from the highest z down",
                texelSize = RegionMap.TexelSize,
                range = RegionMap.Range,
                texels = new[] { map.Width, map.Height },
                min = Array(map.Min),
                max = Array(map.Max),
                hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png))[..16].ToLowerInvariant(),
                // Where regions overlap the later one wins; grass is where none is
                channels = regions.Regions.Select((r, index) => new
                {
                    channel = "rgba"[index].ToString(),
                    name = r.Name,
                    material = r.Material,
                    pattern = r.Pattern == GroundPattern.Stones ? "stones" : "plain",
                    stoneSize = r.Pattern == GroundPattern.Stones ? r.StoneSize : (float?)null,
                    groutWidth = r.Pattern == GroundPattern.Stones ? r.GroutWidth : (float?)null,
                    wobble = r.Wobble,
                    shapes = r.Shapes.Count,
                }).ToArray(),
            },
            // The unique models, standing on the ground: position is the base of the model (its y = 0); yaw is in degrees about +y,
            // counter-clockwise seen from above. Unity mirrors x, so it places them at -x and turns them by -yaw
            placements = placed.Select(p => new { model = p.Model, position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, yaw = p.Yaw, scale = p.Scale }).ToArray(),
            // The models scattered by the thousand (instances.bin), drawn by the instanced renderer in draw groups, each group as its
            // settings below say (see DrawGroup in SDFPipeline/Levels/LevelDefinition.cs); null when nothing is scattered
            instances = instances is null ? null : new
            {
                file = InstanceFile.FileName,
                encoding = "see SDFPipeline/Levels/InstanceFile.cs: header (groups, prototypes, cell grid, per-cell first record and count), then " +
                           "32-byte records (prototype u16, flags u16, position 3 × f32, yaw f32, scale f32, tint RGBA8, hash u32), sorted by cell",
                count = scattered.Instances.Count,
                hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(instances))[..16].ToLowerInvariant(),
                cellSize = InstanceFile.CellSize,
                cellCount = new[] { cellsX, cellsZ },
                origin = Array(-terrain.HalfExtent),
                prototypes = scattered.Prototypes.Select((p, index) => new
                {
                    model = p.Model,
                    group = p.Group,
                    count = scattered.Instances.Count(i => i.Prototype == index),
                }).ToArray(),
                scatters = level.Scatters.Select((s, index) => new
                {
                    group = s.Group,
                    models = s.Entries.Select(e => e.Model).ToArray(),
                    density = s.Density,
                    count = scattered.LayerCounts[index],
                }).ToArray(),
                // The trees sections, after the scatters: band is the least and greatest distance from the playable rectangle's edge
                trees = level.Trees.Count == 0 ? null : level.Trees.Select((s, index) => new
                {
                    group = s.Group,
                    models = s.Entries.Select(e => e.Model).ToArray(),
                    density = s.Density,
                    band = Array(s.Band!.Value),
                    count = scattered.LayerCounts[level.Scatters.Count + index],
                }).ToArray(),
            },
            // How the instanced renderer draws each group: rings[k] is where ring k (level of detail k) ends, in metres; the first outline
            // rings get the ink line and the first shadows (lowShadows on the integrated GPU / web preset) cast shadows; rings cross-fade
            // over blend metres and the last fades out over fade; wind is metres of sway at windHeight, tintHeight how high the base
            // takes the ground's colour; the low preset draws lowDensity of the instances with the distances times lowDistance
            drawGroups = level.DrawGroups.Select(g => new
            {
                name = g.Name,
                rings = g.Rings,
                outline = g.OutlineRings,
                shadows = g.ShadowRings,
                lowShadows = g.LowShadowRings,
                blend = g.Blend,
                fade = g.Fade,
                wind = g.Wind,
                windHeight = g.WindHeight,
                tintHeight = g.TintHeight,
                lowDensity = g.LowDensity,
                lowDistance = g.LowDistance,
            }).ToArray(),
            // The sky, resolved from its preset and the level's overrides, with where its floaters and spores are (seeded by the level)
            sky = sky is null ? null : SkyDocument.Of(sky.Definition, sky.Hash, SkyLayout.Of(sky.Definition, level.Seed, play.HalfSize, terrain.EdgeDistance)),
            // The closed room, if the level has one: six chessboards over the floor, for Unity to build walls and ceiling from (the floor
            // itself is the ground above). Null otherwise
            room = level.Room is { } room ? RoomDocument(room, materials) : level.Backdrop is { } backdrop ? BackdropDocument(backdrop) : null,
            // The battle sides (west at -x, east at +x), each with its spawn, flag stand, weapon pods and health pods (and on a
            // level with machines its turrets and machine zone, ticket 24), and the neutral pods between them: position is on the ground
            // (a castle's courtyard is), yaw in degrees as a placement's. Unity builds them from Assets/Pickups (pods/<pod>-pod.prefab,
            // flags/flag-stand-<side>.prefab) and Assets/Machines/Prefabs (turret-<team>.prefab). Null for a level without sides
            battle = level.Battle is { } battle ? BattleDocument(battle, terrain, level) : null,
            // Vehicles on a closed path and a timetable (ticket 27.3), or none: the path's corners (x, z), its length, (arc length, y)
            // height keys, and the cars head first with their lengths. Their position is speed * (round clock + phase) metres along the
            // path, anticlockwise as the corners go; Unity's PathMover places each model's prefab there, with a kinematic convex collider
            movers = level.Movers.Count == 0 ? null : level.Movers.Select(m => new
            {
                name = m.Name,
                speed = m.Speed,
                phase = m.Phase,
                gap = m.Gap,
                // What the cars do (ticket 27.10): the hit, the kill feed's name, the puffs and how far ahead bots wait to cross
                pushes = m.Pushes,
                damage = m.Damage,
                hazard = m.HazardName,
                cooldown = m.Cooldown,
                puff = m.Puff,
                puffAt = Array(m.PuffAt),
                whistle = m.Whistle,
                crossing = m.Crossing,
                length = m.Length,
                path = m.Path.Select(Array).ToArray(),
                heights = m.Heights.Count == 0 ? null : m.Heights.Select(Array).ToArray(),
                cars = m.Cars.Select(c => new { model = c.Model, length = c.Length }).ToArray(),
            }).ToArray(),
            // Every model the level uses, placed, scattered, under the island or in the sky, once, so Unity rebuilds the level when one of
            // their prefabs is rebuilt
            models = placed.Select(p => p.Model).Concat(scattered.Prototypes.Select(p => p.Model))
                .Concat(level.Edge.Underside is { } under ? [under.Model] : [])
                .Concat(level.Backdrop is { } backdropUsed ? [backdropUsed.Model] : [])
                .Concat(level.Movers.SelectMany(m => m.Cars.Select(c => c.Model)))
                .Concat(sky?.Definition.Models ?? []).Distinct().Order(StringComparer.Ordinal).ToArray(),
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        GlbWriter.SaveAtomically(Path.Combine(folder, LevelJson), [.. json, (byte)'\n']);
        return new BakedLevel(folder, layout, files, triangles, skirts, maxErrors) { Regions = regions.Regions, Placements = placed, Scattered = scattered };
    }

    private static object BattleDocument(BattleLayout battle, LevelTerrain terrain, LevelDefinition level)
    {
        object Item(BattleObject item) => new
        {
            pod = item.Pod,
            position = new[] { item.At.X, terrain.Height(item.At.X, item.At.Y), item.At.Y },
            yaw = item.Yaw,
            // A spawn's zone (ticket 20): its players appear anywhere within this radius; the machine zone's (ticket 24) the same for the
            // side's helicopter and tanks; none for a flag, pod or turret
            radius = item.IsZone ? item.ZoneRadius : (float?)null,
        };
        return new
        {
            // Ticket 27.2, on a level with machines and a machines line (else left out, the fungal machines and the old room flight):
            // the set of machine models (turret-<set>-<team>) and, in a room, the helicopters' flight ceiling over the floor in metres
            machineSet = level.Machines is { } set && set.Name != MachineSet.Default ? set.Name : null,
            flightCeiling = level.Machines?.CeilingIn(level.Room),
            sides = battle.Sides.Select(side => new
            {
                side = side.Name,
                spawn = Item(side.Spawn),
                flag = Item(side.Flag),
                // The weapon pods (minigun and pistol on most levels), then the health pods
                pods = side.Weapons.Append(side.Health).Concat(side.MoreHealth).Select(Item).ToArray(),
                // Ticket 24, on a level with machines (else left out): the side's turrets, each where it stands, facing yaw at rest
                // (Assets/Machines/Prefabs/turret-<team>.prefab), and the zone its helicopter and tanks start and respawn in
                turrets = side.Turrets.Count > 0 ? side.Turrets.Select(Item).ToArray() : null,
                machines = side.MachineSpawn is { } machines ? Item(machines) : null,
            }).ToArray(),
            neutral = battle.Neutral.Select(Item).ToArray(),
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static float[] Array(Vector2 v) => [v.X, v.Y];

    private static float[] Array(Vector3 v) => [v.X, v.Y, v.Z];

    /// <summary>
    /// The room for level.json: its size and square size, and for each face its origin corner, the directions of its u and v axes (one
    /// square is <c>square</c> metres along each), the way it faces (into the room), its squares along u and v, and which squares are
    /// dark (<see cref="RoomChecker"/>) as one string per row v = 0, 1, ..., a '1' for each dark square u = 0, 1, ... A room with named
    /// wall and ceiling materials (ticket 27.1) has instead, per face, its <c>extent</c> in metres along u and v, and the block names
    /// <c>walls</c> and <c>ceiling</c> with their material's parameters. The glow strip and the light, when the level gives them, are
    /// <c>glow</c> (the face's name, its height and colour) and <c>light</c> (direction towards the lamp, colour, ambient: each or none).
    /// The checker room's block is as it always was.
    /// </summary>
    private static object RoomDocument(RoomSpec room, MaterialLibrary materials)
    {
        var (size, squares) = (room.Size, room.Squares);
        var (hx, hz) = (size.X / 2, size.Z / 2);
        float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];
        object Face(RoomFace face, string name, Vector3 origin, Vector3 u, Vector3 v, Vector3 normal)
        {
            var (countU, countV) = room.IsChecker ? RoomChecker.Size(face, squares) : (0, 0);
            // The face's two lengths in metres: along u and v
            float Length(Vector3 axis) => MathF.Abs(Vector3.Dot(axis, size));
            return new
            {
                face = name,
                origin = new[] { origin.X, origin.Y, origin.Z },
                u = new[] { u.X, u.Y, u.Z },
                v = new[] { v.X, v.Y, v.Z },
                normal = new[] { normal.X, normal.Y, normal.Z },
                squares = room.IsChecker ? new[] { countU, countV } : null,
                dark = room.IsChecker
                    ? Enumerable.Range(0, countV)
                        .Select(row => new string(Enumerable.Range(0, countU).Select(col => RoomChecker.IsDark(face, col, row, squares) ? '1' : '0').ToArray()))
                        .ToArray()
                    : null,
                extent = room.IsChecker ? null : new[] { Length(u), Length(v) },
            };
        }

        object? Look(string? material) => material is null ? null : new { name = material, parameters = Parameters(materials.Resolve(material)) };
        return new
        {
            size = new[] { size.X, size.Y, size.Z },
            square = room.IsChecker ? room.Square : (float?)null,
            squares = room.IsChecker ? new[] { squares.X, squares.Y, squares.Z } : null,
            note = room.IsChecker
                ? "the floor is the ground at y = 0, centred on the origin; walls stand on its edges, the ceiling is at y = size.y facing down; " +
                  "a face's square (u, v) spans origin + u * square * (u axis) + v * square * (v axis) and the next square along each; " +
                  "colour alternates across every edge of the cube but the four vertical ones, where the walls' squares match (three squares meet at a corner, so some repeat is forced)"
                : "the floor is the ground at y = 0, centred on the origin; walls stand on its edges, the ceiling is at y = size.y facing down; " +
                  "a face spans origin + (0..extent[0]) * (u axis) + (0..extent[1]) * (v axis); walls and ceiling are drawn in the materials named below",
            walls = Look(room.Walls),
            ceiling = Look(room.Ceiling),
            // The emissive band along the top of a wall: the wall's face name, its height in metres and its colour (sRGB)
            glow = room.Glow is { } glow ? new { wall = FaceName(glow.Wall), height = glow.Height, color = Vec(glow.Color) } : null,
            // The lamp: the direction towards it (the pipeline's axes), its colour and the ambient (sRGB); parts left out keep Unity's indoor default
            light = room.Light is { } light
                ? new { direction = light.Direction is { } d ? Vec(d) : null, color = light.Color is { } c ? Vec(c) : null, ambient = light.Ambient is { } a ? Vec(a) : null }
                : null,
            faces = new[]
            {
                Face(RoomFace.Floor, "floor", new Vector3(-hx, 0, -hz), Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
                Face(RoomFace.Ceiling, "ceiling", new Vector3(-hx, size.Y, -hz), Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitY),
                Face(RoomFace.NegZ, "wall-z-", new Vector3(-hx, 0, -hz), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
                Face(RoomFace.PosZ, "wall-z+", new Vector3(-hx, 0, hz), Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ),
                Face(RoomFace.NegX, "wall-x-", new Vector3(-hx, 0, -hz), Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
                Face(RoomFace.PosX, "wall-x+", new Vector3(hx, 0, -hz), Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitX),
            },
        };
    }

    /// <summary>
    /// The room block of a level with a backdrop (ticket 28.2): <c>form</c> "backdrop" (a closed room's block has no form), the model, where
    /// it stands (x, y, z; y absolute) and its yaw, the camera's far plane and clear colour (sRGB), and the lamp as a room's <c>light</c>.
    /// There are no faces, no size and no walls: Unity builds no colliders for it, and the play area's edges are open.
    /// </summary>
    private static object BackdropDocument(BackdropSpec backdrop) => new
    {
        form = "backdrop",
        model = backdrop.Model,
        at = Array(backdrop.At),
        yaw = backdrop.Yaw,
        farPlane = backdrop.FarPlane,
        clear = Array(backdrop.Clear),
        light = backdrop.Light is { } light
            ? new { direction = light.Direction is { } d ? Array(d) : null, color = light.Color is { } c ? Array(c) : null, ambient = light.Ambient is { } a ? Array(a) : null }
            : null,
    };

    private static string FaceName(RoomFace face) => face switch
    {
        RoomFace.NegZ => "wall-z-",
        RoomFace.PosZ => "wall-z+",
        RoomFace.NegX => "wall-x-",
        RoomFace.PosX => "wall-x+",
        RoomFace.Ceiling => "ceiling",
        _ => "floor",
    };

    /// <summary>
    /// A ground material's colour as the ground shader shows it on average (linear): its base, some of the way towards its patches'
    /// colour (<see cref="LevelScatter.GroundVariationShare"/>). The shader's patches and speckles are not followed point by point.
    /// </summary>
    private static Vector3 AverageColor(MaterialDefinition material)
    {
        var baseColor = new Vector3(material.baseColor.x, material.baseColor.y, material.baseColor.z);
        // Without patches (no variation scale) the shader shows the base alone
        return material.variationScale > 0
            ? Vector3.Lerp(baseColor, new Vector3(material.variationColor.x, material.variationColor.y, material.variationColor.z), LevelScatter.GroundVariationShare)
            : baseColor;
    }

    /// <summary>Each parameter by name, as the .glb files of models give them: a number, or [r, g, b] (linear) / [x, y, z].</summary>
    private static Dictionary<string, object> Parameters(MaterialDefinition material) =>
        MaterialDefinition.Parameters.ToDictionary(p => p.Name, p => p.Get(material) is [var single] ? (object)single : p.Get(material));

    /// <summary>
    /// A chunk as binary glTF: a mesh and node per level of detail with only POSITION and NORMAL (the ground's material comes from
    /// level.json, not from its vertices), LOD0's node listing the others through MSFT_lod as a model's parts do, so SdfGlb in Unity
    /// reads it like a model with one part, "ground".
    /// </summary>
    private static byte[] GroundGlb(string file, GroundLayout layout, int i, int j, IReadOnlyList<GroundMesh> lods)
    {
        var binary = new MemoryStream();
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        int View(byte[] bytes, int target)
        {
            GlbWriter.Pad(binary, 0);
            bufferViews.Add(new { buffer = 0, byteOffset = binary.Length, byteLength = bytes.Length, target });
            binary.Write(bytes);
            return bufferViews.Count - 1;
        }

        var meshes = new List<object>();
        for (var level = 0; level < lods.Count; level++)
        {
            var lod = lods[level];
            var positions = lod.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();
            var normals = lod.Normals.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();
            accessors.Add(new
            {
                bufferView = View(GlbWriter.Bytes(positions), 34962), componentType = 5126, count = lod.Positions.Length, type = "VEC3",
                min = GlbWriter.Bound(positions, 3, Math.Min), max = GlbWriter.Bound(positions, 3, Math.Max),
            });
            var position = accessors.Count - 1;
            accessors.Add(new { bufferView = View(GlbWriter.Bytes(normals), 34962), componentType = 5126, count = lod.Normals.Length, type = "VEC3" });
            var normal = accessors.Count - 1;
            accessors.Add(new
            {
                bufferView = View(GlbWriter.Bytes(lod.Triangles.Select(t => (uint)t).ToArray()), 34963), componentType = 5125,
                count = lod.Triangles.Length, type = "SCALAR", min = new[] { lod.Triangles.Min() }, max = new[] { lod.Triangles.Max() },
            });
            meshes.Add(new
            {
                name = GlbWriter.LodName(GroundMeshName, level),
                primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = position, ["NORMAL"] = normal }, indices = accessors.Count - 1, mode = 4 } },
                extras = new { part = GroundMeshName, lod = level, maxError = lod.MaxError, cellSize = lod.CellSize, skirtTriangles = lod.SkirtTriangles },
            });
        }
        GlbWriter.Pad(binary, 0);

        var nodes = new List<object> { new { name = GroundMeshName, mesh = 0, extensions = new Dictionary<string, object> { [GlbWriter.LodExtension] = new { ids = Enumerable.Range(1, lods.Count - 1).ToArray() } } } };
        nodes.AddRange(Enumerable.Range(1, lods.Count - 1).Select(level => new { name = GlbWriter.LodName(GroundMeshName, level), mesh = level }));
        var document = new
        {
            asset = new { version = "2.0", generator = "SDFPipeline (sdf level)" },
            scene = 0,
            scenes = new[] { new { name = Path.GetFileNameWithoutExtension(file), nodes = new[] { 0 } } },
            nodes,
            meshes,
            extensionsUsed = new[] { GlbWriter.LodExtension },
            accessors,
            bufferViews,
            buffers = new[] { new { byteLength = binary.Length } },
            extras = new
            {
                sdf = new
                {
                    units = "metres",
                    up = "+Y",
                    cellSize = lods[0].CellSize,
                    // The ground has no material table: level.json gives its material
                    materials = System.Array.Empty<object>(),
                    chunk = new { i, j, min = Array(layout.Min(i, j)), max = Array(layout.Max(i, j)) },
                    lods = new
                    {
                        count = lods.Count,
                        names = $"LOD0's mesh and node are {GroundMeshName}, LOD k's are {GroundMeshName}{GlbWriter.LodSuffix}k, listed by {GlbWriter.LodExtension} on LOD0's node",
                        extras = "a level's mesh has extras lod, maxError (how far it is off the height function, in metres) and skirtTriangles (the last ones)",
                    },
                },
            },
        };
        return GlbWriter.Container(JsonSerializer.SerializeToUtf8Bytes(document), binary.ToArray());
    }

    /// <summary>"1,234" style counts for the report.</summary>
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
