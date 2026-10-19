using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Meshing;

/// <summary>
/// Writes a meshed model as binary glTF 2.0 (.glb): one node and mesh per part, in metres with +Y up and the front towards +Z, which is
/// glTF's own convention, so nothing is converted. Besides POSITION, NORMAL and COLOR_0 (the albedo the preview lights the surface with,
/// linear: base colour with its variation patches, speckles, edge colour and wet darkening baked in), each vertex carries the attributes below;
/// they are floats, which every importer can read into a spare UV channel. The material table, with each material's parameters, is in
/// the file's extras under "sdf".
/// <para>
/// Each mesh has one primitive per material region, all sharing the mesh's vertices: a triangle belongs to the material that weighs most
/// at most of its corners. Each glTF material is a material of the table, with its roughness in core glTF, its specular level as
/// KHR_materials_ior and its wetness as KHR_materials_clearcoat, so plain glTF viewers show the right gloss per region. Shaders that
/// read the attributes below get every parameter blended smoothly across region borders instead.
/// </para>
/// <para>
/// Levels of detail (<see cref="MeshingOptions.LodCount"/>) leave LOD0 as it would be without them: the scene holds a node per part with
/// its LOD0 mesh, named after the part. Each further level is a mesh and node of its own named body_LOD1, body_LOD2 and so on, with the
/// part and level in the mesh's extras, written after all of LOD0. The levels' nodes are not in the scene, so viewers show LOD0 alone;
/// each LOD0 node lists its levels, coarsest last, in the MSFT_lod extension, which viewers that know it use to switch between them.
/// </para>
/// <para>
/// Part instances (<see cref="PartMesh.Instances"/>): a part meshed once in its own space for several uses is one mesh (with its levels),
/// as glTF splits nodes from meshes, and each use is a node with its matrix, named part.0, part.1 and so on; the first use's node is
/// where a mesh in place would have its node, the others follow all LOD0 nodes. Each use has its own level nodes (part_LOD1.0 ...),
/// with the same matrix, listed by its MSFT_lod. A use whose matrix mirrors (negative determinant) is drawn with its triangles turned
/// round, as glTF asks of viewers. Models without instances are written as before.
/// </para>
/// <para>
/// Settings (<see cref="ModelSettings"/>): the model's collider, and its shadows, outline and cull where they are not the default, are in
/// extras.sdf; a part whose file has settings of its own has them, resolved, in extras.sdf.parts under its mesh's name.
/// </para>
/// </summary>
public static class GlbWriter
{
    public const string Extension = ".glb";

    /// <summary>The two materials that weigh most at the vertex, as indices into the material table (whole numbers stored as floats).</summary>
    public const string MaterialIdsAttribute = "_MATERIAL_IDS";

    /// <summary>How much of the second material the vertex has: 0 is all the first, 1 all the second.</summary>
    public const string MaterialBlendAttribute = "_MATERIAL_BLEND";

    /// <summary>Metres of material straight in from the vertex, for light shining through and the edge colour of thin material.</summary>
    public const string ThicknessAttribute = "_THICKNESS";

    /// <summary>Ambient occlusion as the raymarched preview computes it, 0 closed in to 1 open.</summary>
    public const string OcclusionAttribute = "_OCCLUSION";

    /// <summary>The vertex in the coordinates of the shape its material is on, which the procedural surface detail is drawn in.</summary>
    public const string LocalPositionAttribute = "_LOCAL_POSITION";

    /// <summary>The base colour of the material mix, linear, before variation, edge colour and wetness.</summary>
    public const string BaseColorAttribute = "_BASE_COLOR";

    /// <summary>The material mix's roughness, specular, wetness and subsurfaceRadius.</summary>
    public const string SurfaceAttribute = "_SURFACE";

    /// <summary>The material mix's detailStrength, detailScale, detailStretch and colorVariation.</summary>
    public const string DetailAttribute = "_DETAIL";

    /// <summary>The material mix's variationColor (linear) and variationScale.</summary>
    public const string VariationAttribute = "_VARIATION";

    /// <summary>The material mix's speckleColor (linear) and speckleScale.</summary>
    public const string SpeckleAttribute = "_SPECKLE";

    /// <summary>The material mix's speckleDensity and speckleSize.</summary>
    public const string SpeckleShapeAttribute = "_SPECKLE_SHAPE";

    /// <summary>What a level of detail's mesh and node are called after the part's name, followed by the level: body_LOD1.</summary>
    public const string LodSuffix = "_LOD";

    /// <summary>What separates a part's name from the number of the use in an instance's node name: eye.0, eye_LOD1.0.</summary>
    public const char InstanceSeparator = '.';

    /// <summary>The glTF extension that lists a node's levels of detail (Microsoft's, which Babylon.js reads).</summary>
    public const string LodExtension = "MSFT_lod";

    /// <summary>The roughness of the wet coat, as the preview draws it.</summary>
    public const float CoatRoughness = 0.06f;

    private const uint Magic = 0x46546C67; // "glTF"
    private const uint JsonChunk = 0x4E4F534A; // "JSON"
    private const uint BinaryChunk = 0x004E4942; // "BIN\0"
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;
    private const int Float = 5126;
    private const int UnsignedInt = 5125;
    private const int Triangles = 4;

    public static byte[] Write(MeshedModel model, string name)
    {
        var binary = new MemoryStream();
        var bufferViews = new List<object>();
        var accessors = new List<object>();

        int View(ReadOnlySpan<byte> bytes, int target)
        {
            Pad(binary, 0);
            bufferViews.Add(new { buffer = 0, byteOffset = binary.Length, byteLength = bytes.Length, target });
            binary.Write(bytes);
            return bufferViews.Count - 1;
        }

        int Accessor(float[] values, string type, int components, bool bounds = false)
        {
            var view = View(Bytes(values), ArrayBuffer);
            var count = values.Length / components;
            object accessor = bounds
                ? new { bufferView = view, componentType = Float, count, type, min = Bound(values, components, Math.Min), max = Bound(values, components, Math.Max) }
                : new { bufferView = view, componentType = Float, count, type };
            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        var meshes = new List<object>();
        int AddMesh(PartMesh mesh, string meshName, object extras)
        {
            var mixes = mesh.Materials.Select(m => m.Mix).ToArray();
            var spacing = HalfEdgeLengths(mesh);
            var albedo = mesh.Materials.Select((m, v) => SurfaceAttributes.Albedo(m.Mix, m.LocalPosition, mesh.Thickness[v], spacing[v])).ToArray();
            var attributes = new Dictionary<string, int>
            {
                ["POSITION"] = Accessor(Flatten(mesh.Positions), "VEC3", 3, bounds: true),
                ["NORMAL"] = Accessor(Flatten(mesh.Normals), "VEC3", 3),
                ["COLOR_0"] = Accessor(Flatten(albedo), "VEC3", 3),
                [MaterialIdsAttribute] = Accessor(mesh.Materials.SelectMany(m => new float[] { m.First, m.Second }).ToArray(), "VEC2", 2),
                [MaterialBlendAttribute] = Accessor(mesh.Materials.Select(m => m.Blend).ToArray(), "SCALAR", 1),
                [ThicknessAttribute] = Accessor(mesh.Thickness, "SCALAR", 1),
                [OcclusionAttribute] = Accessor(mesh.Occlusion, "SCALAR", 1),
                [LocalPositionAttribute] = Accessor(Flatten(mesh.Materials.Select(m => m.LocalPosition).ToArray()), "VEC3", 3),
                [BaseColorAttribute] = Accessor(Flatten(mesh.Materials.Select(m => m.BaseColor).ToArray()), "VEC3", 3),
                [SurfaceAttribute] = Accessor(mixes.SelectMany(m => new[] { m.roughness, m.specular, m.wetness, m.subsurfaceRadius }).ToArray(), "VEC4", 4),
                [DetailAttribute] = Accessor(mixes.SelectMany(m => new[] { m.detailStrength, m.detailScale, m.detailStretch, m.colorVariation }).ToArray(), "VEC4", 4),
                [VariationAttribute] = Accessor(mixes.SelectMany(m => new[] { m.variationColor.x, m.variationColor.y, m.variationColor.z, m.variationScale }).ToArray(), "VEC4", 4),
                [SpeckleAttribute] = Accessor(mixes.SelectMany(m => new[] { m.speckleColor.x, m.speckleColor.y, m.speckleColor.z, m.speckleScale }).ToArray(), "VEC4", 4),
                [SpeckleShapeAttribute] = Accessor(mixes.SelectMany(m => new[] { m.speckleDensity, m.speckleSize }).ToArray(), "VEC2", 2),
            };

            var primitives = new List<object>();
            foreach (var (material, triangles) in Regions(mesh))
            {
                var indexView = View(Bytes(triangles.Select(i => (uint)i).ToArray()), ElementArrayBuffer);
                accessors.Add(new
                {
                    bufferView = indexView, componentType = UnsignedInt, count = triangles.Length, type = "SCALAR",
                    min = new[] { triangles.Min() }, max = new[] { triangles.Max() },
                });
                primitives.Add(new { attributes, indices = accessors.Count - 1, material, mode = Triangles });
            }
            meshes.Add(new { name = meshName, primitives, extras });
            return meshes.Count - 1;
        }

        foreach (var mesh in model.Meshes)
        {
            if (mesh.Instances.Count > 0)
                AddMesh(mesh, mesh.Name, new { part = mesh.Part, contouredTriangles = mesh.Statistics.ContouredTriangles, instances = mesh.Instances.Count });
            else
                AddMesh(mesh, mesh.Name, new { part = mesh.Name, contouredTriangles = mesh.Statistics.ContouredTriangles });
        }

        // Each mesh's uses: one node in the model's space, or a node per instance of a part meshed in its own. The first use's node has
        // the mesh's number; the other uses' nodes follow all of those
        object Node(string nodeName, int mesh, Matrix4x4? placement, List<int>? lods = null) => new
        {
            name = nodeName,
            mesh,
            matrix = placement is { } m ? ColumnMajor(m) : null,
            extensions = lods is null ? null : new Dictionary<string, object> { [LodExtension] = new { ids = lods } },
        };
        var uses = model.Meshes.Select(m => m.Instances.Count > 0 ? m.Instances.Select(p => (Matrix4x4?)p).ToArray() : [null]).ToArray();
        string UseName(string meshName, int mesh, int use) => uses[mesh][use] is null ? meshName : $"{meshName}{InstanceSeparator}{use}";
        var nodes = model.Meshes.Select((m, i) => Node(UseName(m.Name, i, 0), i, uses[i][0])).ToList();
        var useNodes = model.Meshes.Select((_, i) => new List<int> { i }).ToArray();
        for (var i = 0; i < model.Meshes.Count; i++)
        for (var use = 1; use < uses[i].Length; use++)
        {
            nodes.Add(Node(UseName(model.Meshes[i].Name, i, use), i, uses[i][use]));
            useNodes[i].Add(nodes.Count - 1);
        }

        // The levels of detail come after all of LOD0, so LOD0's meshes and nodes keep their numbers. Their nodes are not in the scene:
        // MSFT_lod on each LOD0 node lists them, and viewers that do not know it show LOD0 alone
        for (var i = 0; i < model.Meshes.Count; i++)
        {
            var mesh = model.Meshes[i];
            if (mesh.Lods.Count == 0)
                continue;
            var levelMeshes = mesh.Lods.Select((lod, l) => AddMesh(lod, LodName(mesh.Name, l + 1),
                new { part = mesh.Name, lod = l + 1, maxError = lod.MaxError, contouredTriangles = lod.Statistics.ContouredTriangles })).ToArray();
            for (var use = 0; use < uses[i].Length; use++)
            {
                var ids = new List<int>();
                for (var level = 1; level <= mesh.Lods.Count; level++)
                {
                    nodes.Add(Node(UseName(LodName(mesh.Name, level), i, use), levelMeshes[level - 1], uses[i][use]));
                    ids.Add(nodes.Count - 1);
                }
                nodes[useNodes[i][use]] = Node(UseName(mesh.Name, i, use), i, uses[i][use], ids);
            }
        }
        var lodCount = 1 + model.Meshes.Select(m => m.Lods.Count).DefaultIfEmpty(0).Max();
        var instanced = model.Meshes.Any(m => m.Instances.Count > 0);
        Pad(binary, 0);

        var document = new
        {
            asset = new { version = "2.0", generator = "SDFPipeline (sdf mesh)" },
            scene = 0,
            scenes = new[] { new { name, nodes = useNodes.SelectMany(u => u).Order().ToArray() } },
            nodes,
            meshes,
            // The material table, in order, so a primitive's material is its region's number; the vertex colours give the colour
            materials = model.Materials.Select(m => GltfMaterial(m.Name, m.Definition)).ToArray(),
            extensionsUsed = lodCount > 1
                ? new[] { "KHR_materials_ior", "KHR_materials_clearcoat", LodExtension }
                : new[] { "KHR_materials_ior", "KHR_materials_clearcoat" },
            accessors,
            bufferViews,
            buffers = new[] { new { byteLength = binary.Length } },
            extras = new
            {
                sdf = new
                {
                    units = "metres",
                    up = "+Y",
                    front = "+Z",
                    cellSize = model.Grid?.CellSize,
                    materials = model.Materials.Select(m => new { name = m.Name, parameters = Parameters(m.Definition) }).ToArray(),
                    attributes = new Dictionary<string, string>
                    {
                        [MaterialIdsAttribute] = "the two materials that weigh most at the vertex, indices into materials",
                        [MaterialBlendAttribute] = "share of the second material, 0 to 1",
                        [ThicknessAttribute] = "metres of material straight in from the vertex",
                        [OcclusionAttribute] = "ambient occlusion, 0 closed in to 1 open",
                        [LocalPositionAttribute] = "position in the coordinates of the shape the material is on, for procedural detail",
                        [BaseColorAttribute] = "base colour of the material mix, linear, before variation, edge colour and wetness",
                        [SurfaceAttribute] = "roughness, specular, wetness and subsurfaceRadius of the material mix",
                        [DetailAttribute] = "detailStrength, detailScale, detailStretch and colorVariation of the material mix",
                        [VariationAttribute] = "variationColor (linear) and variationScale of the material mix",
                        [SpeckleAttribute] = "speckleColor (linear) and speckleScale of the material mix",
                        [SpeckleShapeAttribute] = "speckleDensity and speckleSize of the material mix",
                        ["COLOR_0"] = "albedo of the material mix, linear: variation, speckles (averaged over the vertex's share of the surface), edge colour and wet darkening baked in",
                    },
                    // Each mesh's primitives are its material regions; the number is the material's index in materials
                    regions = "primitive material = index into materials, by the material that weighs most at the triangle",
                    collider = model.Settings.Collider == ColliderShape.Capsule
                        ? Collider(model.Settings, TrunkMeshes(model), 1, lodCount)
                        : Collider(model.Settings, [(ModelPositions(model), [])], 1, lodCount),
                    // Left out where they are the default, so such files stay as they were
                    shadows = model.Settings.Shadows == ShadowCasting.On ? null : Lower(model.Settings.Shadows),
                    outline = model.Settings.Outline ? (bool?)null : false,
                    cull = model.Settings.Cull,
                    parts = PartSettings(model, lodCount),
                    instances = instanced
                        ? $"a part used several times is one mesh in its own space, each use a node named part{InstanceSeparator}k with its matrix, and its levels' nodes part{LodSuffix}n{InstanceSeparator}k with the same"
                        : null,
                    // Left out without levels of detail, so such files stay as they were
                    lods = lodCount > 1
                        ? new
                        {
                            count = lodCount,
                            names = $"a part's LOD0 mesh and node have its name, LOD k's are the name and {LodSuffix}k, listed by {LodExtension} on LOD0's node",
                            extras = "a level's mesh has extras part, lod, and maxError: how far the level may be off the surface, in metres",
                        }
                        : null,
                },
            },
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        return Container(json, binary.ToArray());
    }

    /// <summary>
    /// What the game engine gives the model or a part to collide with (<see cref="ModelSettings"/>), resolved for this file: the level of
    /// detail a convex or mesh collider is made from (the one asked for, or the coarsest, never beyond the levels there are), a convex one
    /// larger than <see cref="ModelSettings.LargestConvexSize"/> made a mesh, as its hull would block walkable ground round a large
    /// outcrop, and a capsule fitted to the <paramref name="meshes"/> (<see cref="Capsule"/>; in the file's coordinates, or a part
    /// instance's own). <paramref name="scale"/> is the largest a part's instances make it. Null for none, so files without a collider
    /// stay as they were.
    /// </summary>
    /// <param name="meshes">What the collider goes round: positions, and for a capsule the triangles between them.</param>
    private static object? Collider(ModelSettings settings, IReadOnlyList<(Vector3[] Positions, int[] Triangles)> meshes, float scale, int lodCount)
    {
        var positions = meshes.SelectMany(m => m.Positions).ToArray();
        if (settings.Collider == ColliderShape.None || positions.Length == 0)
            return null;
        var min = positions.Aggregate(Vector3.Min);
        var max = positions.Aggregate(Vector3.Max);
        var size = max - min;
        switch (settings.Collider)
        {
            case ColliderShape.Capsule:
                var (centre, radius) = Capsule(meshes, min, max);
                return new
                {
                    shape = "capsule",
                    @base = new[] { centre.X, min.Y, centre.Y },
                    top = new[] { centre.X, max.Y, centre.Y },
                    radius,
                };
            default:
                var largest = MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * scale;
                var convex = settings.Collider == ColliderShape.Convex && largest <= ModelSettings.LargestConvexSize;
                return new
                {
                    shape = convex ? "convex" : "mesh",
                    lod = Math.Min(settings.ColliderLod ?? lodCount - 1, lodCount - 1),
                    asked = settings.Collider.ToString().ToLowerInvariant(),
                };
        }
    }

    /// <summary>The lowest and highest share of a capsule's height that its radius is measured in: above a trunk's flared foot, below its fork.</summary>
    public static readonly (float From, float To) CapsuleBand = (0.25f, 0.45f);

    /// <summary>The share of the cross-sections' points in <see cref="CapsuleBand"/> that are within a capsule's radius of its axis.</summary>
    public const float CapsuleShare = 0.8f;

    /// <summary>How many cross-sections through <see cref="CapsuleBand"/> a capsule is fitted to.</summary>
    private const int CapsuleSections = 6;

    /// <summary>
    /// An upright capsule round a trunk, from the lowest to the highest of the positions, fitted to cross-sections of the meshes at a
    /// few heights between <see cref="CapsuleBand"/>'s shares of that height (where the triangles' edges cross them, so a side that
    /// simplification left without vertices is measured all the same): its axis (x, z) through their points' mean, its radius as far out
    /// as <see cref="CapsuleShare"/> of them, so a tree's flared foot, a root or a branch reaching into the band leaves it as thick as the
    /// trunk is where a player walks into it. Without a cross-section, round the whole bounds.
    /// </summary>
    internal static (Vector2 Centre, float Radius) Capsule(IReadOnlyList<(Vector3[] Positions, int[] Triangles)> meshes, Vector3 min, Vector3 max)
    {
        var height = max.Y - min.Y;
        var points = new List<Vector2>();
        for (var k = 0; k < CapsuleSections; k++)
        {
            var y = min.Y + height * (CapsuleBand.From + (CapsuleBand.To - CapsuleBand.From) * k / (CapsuleSections - 1));
            foreach (var (positions, triangles) in meshes)
                for (var t = 0; t < triangles.Length; t += 3)
                    for (var e = 0; e < 3; e++)
                    {
                        // Every edge that crosses, once for each of its two triangles: twice each moves neither the mean nor the share
                        var (a, b) = (positions[triangles[t + e]], positions[triangles[t + (e + 1) % 3]]);
                        if ((a.Y < y) == (b.Y < y))
                            continue;
                        var p = Vector3.Lerp(a, b, (y - a.Y) / (b.Y - a.Y));
                        points.Add(new Vector2(p.X, p.Z));
                    }
        }
        if (points.Count < 3)
            return (new Vector2(min.X + max.X, min.Z + max.Z) / 2, MathF.Max(max.X - min.X, max.Z - min.Z) / 2);
        var centre = points.Aggregate(Vector2.Add) / points.Count;
        var distances = points.Select(p => Vector2.Distance(p, centre)).Order().ToArray();
        return (centre, distances[Math.Min((int)(CapsuleShare * distances.Length), distances.Length - 1)]);
    }

    /// <summary>
    /// The meshes a model's capsule is fitted to, at LOD0 in the model's space: its body (a tree's trunk and branches, without the
    /// canopy, which is a part), or every mesh, each part instance where its use puts it, when the model is nothing but parts.
    /// </summary>
    private static (Vector3[] Positions, int[] Triangles)[] TrunkMeshes(MeshedModel model)
    {
        var body = model.Meshes.Where(m => m.Part is null).ToArray();
        return (body.Length > 0 ? body : model.Meshes).SelectMany(m => m.Instances.Count == 0
            ? [(m.Positions, m.Triangles)]
            : m.Instances.Select(placement => (m.Positions.Select(p => Vector3.Transform(p, placement)).ToArray(), m.Triangles))).ToArray();
    }

    /// <summary>Every vertex of LOD0 in the model's space, each part instance's where its use puts it.</summary>
    private static Vector3[] ModelPositions(MeshedModel model) =>
        model.Meshes.SelectMany(m => m.Instances.Count == 0
            ? m.Positions
            : m.Instances.SelectMany(placement => m.Positions.Select(p => Vector3.Transform(p, placement)))).ToArray();

    /// <summary>
    /// The resolved settings of each mesh whose part has settings of its own, by mesh name: collider (shape none when it has none, so
    /// it does not fall back to the model's), shadows, outline and cull. Null when no part has any, so such files stay as they were.
    /// </summary>
    private static Dictionary<string, object>? PartSettings(MeshedModel model, int lodCount)
    {
        var parts = new Dictionary<string, object>();
        foreach (var mesh in model.Meshes)
        {
            if (mesh.Part is not { } part || !model.PartSettings.TryGetValue(part, out var settings))
                continue;
            var scale = mesh.Instances.Select(m => new Vector3(m.M11, m.M12, m.M13).Length()).DefaultIfEmpty(1).Max();
            parts[mesh.Name] = new
            {
                part,
                collider = Collider(settings, [(mesh.Positions, mesh.Triangles)], scale, lodCount) ?? new { shape = "none" },
                shadows = Lower(settings.Shadows),
                outline = settings.Outline,
                cull = settings.Cull,
            };
        }
        return parts.Count > 0 ? parts : null;
    }

    private static string Lower(Enum value) => value.ToString().ToLowerInvariant();

    /// <summary>A placement as glTF's matrix: column-major for column vectors, which is System.Numerics' row-vector matrix row by row.</summary>
    private static float[] ColumnMajor(Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    /// <summary>The GLB container around a glTF document and its binary buffer (already padded to four bytes).</summary>
    internal static byte[] Container(byte[] json, byte[] binary)
    {
        var jsonLength = Align(json.Length);
        var binaryLength = binary.Length;
        var total = 12 + 8 + jsonLength + 8 + binaryLength;

        var glb = new byte[total];
        var span = glb.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], JsonChunk);
        json.CopyTo(span[20..]);
        span.Slice(20 + json.Length, jsonLength - json.Length).Fill((byte)' ');
        var binaryStart = 20 + jsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(span[binaryStart..], (uint)binaryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(binaryStart + 4)..], BinaryChunk);
        binary.CopyTo(span[(binaryStart + 8)..]);
        return glb;
    }

    /// <summary>The name of a part's mesh and node at a level of detail; LOD0 has the part's own name.</summary>
    public static string LodName(string part, int level) => level == 0 ? part : $"{part}{LodSuffix}{level}";

    /// <summary>
    /// Writes the file in one step: the bytes go to a hidden file next to it that then replaces it, so a game engine watching the folder
    /// never imports half a file. The hidden name starts with a dot, which Unity and most file watchers skip.
    /// </summary>
    public static void Save(string path, MeshedModel model)
    {
        var full = Path.GetFullPath(path);
        SaveAtomically(full, Write(model, Path.GetFileNameWithoutExtension(full)));
    }

    /// <summary>Writes the bytes to a hidden file next to the path that then replaces it, as <see cref="Save"/> does.</summary>
    internal static void SaveAtomically(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(full)}.tmp");
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, full, overwrite: true);
    }

    /// <summary>Half the average length of the edges at each vertex: how much surface its baked colour stands for.</summary>
    private static float[] HalfEdgeLengths(PartMesh mesh)
    {
        var sum = new float[mesh.Positions.Length];
        var count = new int[mesh.Positions.Length];
        for (var t = 0; t < mesh.Triangles.Length; t += 3)
        for (var e = 0; e < 3; e++)
        {
            int a = mesh.Triangles[t + e], b = mesh.Triangles[t + (e + 1) % 3];
            var length = Vector3.Distance(mesh.Positions[a], mesh.Positions[b]);
            sum[a] += length;
            sum[b] += length;
            count[a]++;
            count[b]++;
        }
        return sum.Select((s, v) => count[v] > 0 ? 0.5f * s / count[v] : 0).ToArray();
    }

    /// <summary>The triangles of each material, as indices into the mesh's vertices, in material order; materials without triangles are left out.</summary>
    public static IEnumerable<(int Material, int[] Triangles)> Regions(PartMesh mesh)
    {
        var regions = new SortedDictionary<int, List<int>>();
        for (var t = 0; t < mesh.Triangles.Length; t += 3)
        {
            var corners = mesh.Triangles.AsSpan(t, 3);
            var region = RegionOf(mesh.Materials[corners[0]], mesh.Materials[corners[1]], mesh.Materials[corners[2]]);
            if (!regions.TryGetValue(region, out var triangles))
                regions[region] = triangles = [];
            triangles.AddRange(corners);
        }
        return regions.Select(r => (r.Key, r.Value.ToArray()));
    }

    /// <summary>The material most corners have first; when all three differ, the one of the corner that is least blended.</summary>
    private static int RegionOf(VertexMaterial a, VertexMaterial b, VertexMaterial c) =>
        a.First == b.First || a.First == c.First ? a.First
        : b.First == c.First ? b.First
        : new[] { a, b, c }.MinBy(m => m.Blend).First;

    /// <summary>
    /// A material of the table as glTF PBR: a dielectric with its roughness, the specular level as an index of refraction (the preview's
    /// F0 is 0.08 × specular) and wetness as a clear coat. The base colour factor is white, because the vertex colours carry the colour.
    /// </summary>
    private static object GltfMaterial(string name, MaterialDefinition m)
    {
        var f0 = MathF.Sqrt(Math.Clamp(0.08f * m.specular, 0.0001f, 0.25f));
        var extensions = new Dictionary<string, object> { ["KHR_materials_ior"] = new { ior = (1 + f0) / (1 - f0) } };
        if (m.wetness > 0)
            extensions["KHR_materials_clearcoat"] = new { clearcoatFactor = m.wetness, clearcoatRoughnessFactor = CoatRoughness };
        return new
        {
            name,
            pbrMetallicRoughness = new { baseColorFactor = new[] { 1f, 1f, 1f, 1f }, metallicFactor = 0f, roughnessFactor = Math.Clamp(m.roughness, 0, 1) },
            extensions,
            extras = new { sdf = Parameters(m) },
        };
    }

    /// <summary>Each parameter by name: a number, or [r, g, b] / [x, y, z].</summary>
    private static Dictionary<string, object> Parameters(MaterialDefinition material) =>
        MaterialDefinition.Parameters.ToDictionary(p => p.Name, p => p.Get(material) is [var single] ? (object)single : p.Get(material));

    private static float[] Flatten(Vector3[] vectors) => vectors.SelectMany(v => new[] { v.X, v.Y, v.Z }).ToArray();

    internal static float[] Bound(float[] values, int components, Func<float, float, float> pick)
    {
        var result = new float[components];
        for (var c = 0; c < components; c++)
        {
            var bound = values.Length > c ? values[c] : 0;
            for (var i = c; i < values.Length; i += components)
                bound = pick(bound, values[i]);
            result[c] = bound;
        }
        return result;
    }

    internal static byte[] Bytes(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    internal static byte[] Bytes(uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static int Align(int length) => (length + 3) & ~3;

    internal static void Pad(MemoryStream stream, byte value)
    {
        while (stream.Length % 4 != 0)
            stream.WriteByte(value);
    }
}
