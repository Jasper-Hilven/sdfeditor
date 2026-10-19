using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDFPipeline.Meshing;
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

/// <summary>A placement as baked: the model's base at <see cref="Position"/>, on the (flattened) ground.</summary>
public sealed record PlacedModel(string Model, Vector3 Position, float Yaw, float Scale);

/// <summary>
/// Bakes a level for the game engine into a folder of its own (sdf level): the ground as chunk_&lt;i&gt;_&lt;j&gt;.glb, one per chunk
/// of <see cref="GroundLayout"/> that has ground, each with its levels of detail as separate meshes named like a model's (ground,
/// ground_LOD1, ...), and level.json with everything else the engine needs: the extent, the chunk grid, the playable rectangle, the seed,
/// where the player starts, the ground's materials by region, the region map (regions.png, see <see cref="RegionMap"/>), the placed
/// models, the scattered ones (instances.bin, see <see cref="InstanceFile"/> and <see cref="LevelScatter"/>) with their draw groups, and
/// the list of every model's name. Unity's LevelBuilder turns it into the level's scene; the ground's colliders are made there, from
/// LOD1, the placed models bring theirs on their prefabs, and the scattered ones have none.
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
    /// <exception cref="ProgramFiles.SdfProgramFileException">
    /// A placement or scatter names a model that is not there, a placement stands off the island, or a scatter avoids a region the level
    /// does not have.
    /// </exception>
    public static BakedLevel Bake(LevelDefinition level, string name, MaterialLibrary materials, string folder,
        Func<string, ModelFootprint?>? models = null, Func<string, bool>? modelExists = null)
    {
        var terrain = new LevelTerrain(level);
        var layout = GroundLayout.Around(terrain, ChunkSize);
        foreach (var material in level.Regions.Select(r => r.Material).Prepend(GrassMaterial))
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
        var regions = new GroundRegions(level, model => footprints[model]);
        foreach (var region in regions.Regions)
            if (!materials.Contains(region.Material))
                throw new InvalidOperationException($"There is no material file for {region.Material}; the ground needs materials/ground/{region.Material}.sdfmat");
        var placed = level.Placements
            .Select(p => new PlacedModel(p.Model, new Vector3(p.At.X, terrain.Height(p.At.X, p.At.Y), p.At.Y), p.Yaw, p.Scale))
            .ToArray();

        // The scattered models need only be there; the ground's colour under each instance is its material's average
        foreach (var entry in level.Scatters.SelectMany(s => s.Entries))
            if (!(modelExists ?? (name => models?.Invoke(name) is not null))(entry.Model))
                throw new ProgramFiles.SdfProgramFileException(entry.Line, $"There is no model '{entry.Model}' in content/models/");
        var groundColors = regions.Regions.Select(r => r.Material).Prepend(GrassMaterial)
            .Select(m => AverageColor(materials.Resolve(m))).ToArray();
        var scattered = LevelScatter.Scatter(level, terrain, regions, model => footprints[model], p =>
        {
            var region = regions.At(p);
            return groundColors[region is null ? 0 : regions.Regions.ToList().IndexOf(region) + 1];
        });

        // Each chunk on its own, in parallel; the results go in order, so the files do not depend on which finished first
        var chunks = new GroundMesh?[layout.CountX * layout.CountZ][];
        Parallel.For(0, chunks.Length, index =>
        {
            var (i, j) = (index % layout.CountX, index / layout.CountX);
            chunks[index] = Details.Select(detail => GroundMesher.Mesh(terrain, layout, i, j, detail)).ToArray();
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
            edge = new { margin = level.Edge.Margin, distance = terrain.EdgeDistance },
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
            // The ground's material by region: grass, then each region's
            materials = regions.Regions.Select(r => (Role: r.Name, Material: r.Material)).Prepend((Role: "grass", Material: GrassMaterial))
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
            // Every model the level uses, placed or scattered, once, so Unity rebuilds the level when one of their prefabs is rebuilt
            models = placed.Select(p => p.Model).Concat(scattered.Prototypes.Select(p => p.Model)).Distinct().Order(StringComparer.Ordinal).ToArray(),
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        GlbWriter.SaveAtomically(Path.Combine(folder, LevelJson), [.. json, (byte)'\n']);
        return new BakedLevel(folder, layout, files, triangles, skirts, maxErrors) { Regions = regions.Regions, Placements = placed, Scattered = scattered };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static float[] Array(Vector2 v) => [v.X, v.Y];

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
