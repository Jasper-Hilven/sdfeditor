using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
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
        foreach (var mesh in model.Meshes)
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
            meshes.Add(new
            {
                name = mesh.Name,
                primitives,
                extras = new { part = mesh.Name, contouredTriangles = mesh.Statistics.ContouredTriangles },
            });
        }
        Pad(binary, 0);

        var document = new
        {
            asset = new { version = "2.0", generator = "SDFPipeline (sdf mesh)" },
            scene = 0,
            scenes = new[] { new { name, nodes = Enumerable.Range(0, meshes.Count).ToArray() } },
            nodes = model.Meshes.Select((m, i) => new { name = m.Name, mesh = i }).ToArray(),
            meshes,
            // The material table, in order, so a primitive's material is its region's number; the vertex colours give the colour
            materials = model.Materials.Select(m => GltfMaterial(m.Name, m.Definition)).ToArray(),
            extensionsUsed = new[] { "KHR_materials_ior", "KHR_materials_clearcoat" },
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
                },
            },
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var jsonLength = Align(json.Length);
        var binaryLength = (int)binary.Length;
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
        binary.ToArray().CopyTo(span[(binaryStart + 8)..]);
        return glb;
    }

    /// <summary>
    /// Writes the file in one step: the bytes go to a hidden file next to it that then replaces it, so a game engine watching the folder
    /// never imports half a file. The hidden name starts with a dot, which Unity and most file watchers skip.
    /// </summary>
    public static void Save(string path, MeshedModel model)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(full)}.tmp");
        File.WriteAllBytes(temporary, Write(model, Path.GetFileNameWithoutExtension(full)));
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

    private static float[] Bound(float[] values, int components, Func<float, float, float> pick)
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

    private static byte[] Bytes(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static byte[] Bytes(uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static int Align(int length) => (length + 3) & ~3;

    private static void Pad(MemoryStream stream, byte value)
    {
        while (stream.Length % 4 != 0)
            stream.WriteByte(value);
    }
}
