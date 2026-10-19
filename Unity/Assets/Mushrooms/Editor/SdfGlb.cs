using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// A .glb written by the SDF pipeline (SDFPipeline/Meshing/GlbWriter.cs), read in full: every mesh with all of its vertex attributes,
    /// including the pipeline's own ones (_SURFACE, _LOCAL_POSITION, ...) that glTF importers skip, its material regions, and the
    /// material table from extras.sdf. Only what the pipeline writes is supported: float and unsigned int accessors, tightly packed,
    /// one buffer in the GLB's binary chunk.
    /// <para>
    /// Levels of detail (sdf mesh --lods): each part's LOD0 mesh is one of <see cref="Meshes"/>, as in a file without levels, and its
    /// coarser levels hang off it in <see cref="MeshData.Lods"/>. The levels are found the way glTF viewers find them, through the
    /// MSFT_lod extension on the part's node, so a mesh only counts as a level when a part lists it; every other mesh is a part.
    /// </para>
    /// </summary>
    public sealed class SdfGlb
    {
        public sealed class Primitive
        {
            public int Material;
            public int[] Indices;
        }

        public sealed class MeshData
        {
            public string Name;
            public int VertexCount;
            public Dictionary<string, float[]> Attributes = new Dictionary<string, float[]>();
            public List<Primitive> Primitives = new List<Primitive>();

            /// <summary>The part's coarser levels of detail, LOD1 first; empty for a level itself and in files without levels.</summary>
            public List<MeshData> Lods = new List<MeshData>();

            /// <summary>Which level of detail this mesh is: 0 for a part's own mesh.</summary>
            public int Lod;

            /// <summary>How far a level may be off the surface, in metres (the pipeline's error limit for it); 0 for LOD0.</summary>
            public float MaxError;

            /// <summary>
            /// The mesh at a level of detail: this one at 0, else that level, or the coarsest there is when the part has fewer levels
            /// than the model (which the pipeline does not write, but a LOD group needs a mesh for every part at every level).
            /// </summary>
            public MeshData AtLevel(int level) => level <= 0 || Lods.Count == 0 ? this : Lods[Math.Min(level, Lods.Count) - 1];

            /// <summary>The attribute's floats, <paramref name="components"/> per vertex; null when the file does not have it.</summary>
            public float[] Get(string name, int components)
            {
                if (!Attributes.TryGetValue(name, out var values))
                    return null;
                if (values.Length != VertexCount * components)
                    throw new InvalidDataException($"{Name}: {name} has {values.Length} floats, expected {VertexCount} × {components}");
                return values;
            }
        }

        public sealed class Material
        {
            public string Name;
            public JObject Parameters;

            public float Number(string name, float fallback = 0) =>
                Parameters.TryGetValue(name, out var token) && token.Type is JTokenType.Float or JTokenType.Integer ? token.Value<float>() : fallback;

            public Vector3 Vector(string name, Vector3 fallback = default) =>
                Parameters.TryGetValue(name, out var token) && token is JArray { Count: 3 } a
                    ? new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>())
                    : fallback;

            /// <summary>A colour parameter as a linear colour, as the material file gives it.</summary>
            public Color Color(string name)
            {
                var v = Vector(name);
                return new Color(v.x, v.y, v.z, 1);
            }
        }

        /// <summary>The parts, each at LOD0.</summary>
        public List<MeshData> Meshes = new List<MeshData>();
        public List<Material> Materials = new List<Material>();
        public float CellSize;

        /// <summary>Levels of detail, LOD0 included: 1 for a file without levels.</summary>
        public int LodCount => 1 + Meshes.Select(m => m.Lods.Count).DefaultIfEmpty(0).Max();

        public static SdfGlb Read(string path) => Parse(File.ReadAllBytes(path), path);

        public static SdfGlb Parse(byte[] glb, string name)
        {
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67)
                throw new InvalidDataException($"{name} is not a binary glTF file");
            var jsonLength = (int)BitConverter.ToUInt32(glb, 12);
            var json = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));
            var binaryStart = 20 + jsonLength + 8;
            var binaryLength = glb.Length > binaryStart ? (int)BitConverter.ToUInt32(glb, 20 + jsonLength) : 0;

            var views = (JArray)json["bufferViews"];
            var accessors = (JArray)json["accessors"];

            float[] Floats(int accessorIndex)
            {
                var accessor = accessors[accessorIndex];
                var view = views[accessor.Value<int>("bufferView")];
                var offset = binaryStart + view.Value<int?>("byteOffset").GetValueOrDefault() + accessor.Value<int?>("byteOffset").GetValueOrDefault();
                var count = accessor.Value<int>("count") * Components(accessor.Value<string>("type"));
                if (accessor.Value<int>("componentType") != 5126)
                    throw new InvalidDataException($"{name}: accessor {accessorIndex} is not float");
                CheckRange(offset, count * 4);
                var values = new float[count];
                Buffer.BlockCopy(glb, offset, values, 0, count * 4);
                return values;
            }

            int[] Ints(int accessorIndex)
            {
                var accessor = accessors[accessorIndex];
                var view = views[accessor.Value<int>("bufferView")];
                var offset = binaryStart + view.Value<int?>("byteOffset").GetValueOrDefault() + accessor.Value<int?>("byteOffset").GetValueOrDefault();
                var count = accessor.Value<int>("count");
                var values = new int[count];
                switch (accessor.Value<int>("componentType"))
                {
                    case 5125:
                        CheckRange(offset, count * 4);
                        Buffer.BlockCopy(glb, offset, values, 0, count * 4);
                        break;
                    case 5123:
                        CheckRange(offset, count * 2);
                        for (var i = 0; i < count; i++) values[i] = BitConverter.ToUInt16(glb, offset + 2 * i);
                        break;
                    default:
                        throw new InvalidDataException($"{name}: accessor {accessorIndex} has unsupported index type");
                }
                return values;
            }

            void CheckRange(int offset, int length)
            {
                if (offset < binaryStart || offset + length > binaryStart + binaryLength)
                    throw new InvalidDataException($"{name}: an accessor reaches outside the binary chunk; is the file complete?");
            }

            var result = new SdfGlb();
            var meshes = new List<MeshData>();
            foreach (var mesh in (JArray)json["meshes"] ?? new JArray())
            {
                var data = new MeshData
                {
                    Name = mesh.Value<string>("name") ?? $"mesh{meshes.Count}",
                    MaxError = mesh["extras"]?.Value<float?>("maxError").GetValueOrDefault() ?? 0,
                };
                var primitives = (JArray)mesh["primitives"];
                var attributes = (JObject)primitives[0]["attributes"];
                foreach (var attribute in attributes.Properties())
                    data.Attributes[attribute.Name] = Floats(attribute.Value.Value<int>());
                data.VertexCount = data.Attributes["POSITION"].Length / 3;
                foreach (var primitive in primitives)
                {
                    if (!JToken.DeepEquals(primitive["attributes"], attributes))
                        throw new InvalidDataException($"{name}: the primitives of {data.Name} do not share their vertices");
                    data.Primitives.Add(new Primitive
                    {
                        Material = primitive.Value<int?>("material").GetValueOrDefault(),
                        Indices = Ints(primitive.Value<int>("indices")),
                    });
                }
                meshes.Add(data);
            }

            // Each part's node lists the nodes of its levels, coarsest last; those nodes' meshes are the levels
            var nodes = (JArray)json["nodes"] ?? new JArray();
            var isLevel = new bool[meshes.Count];
            foreach (var node in nodes)
            {
                var ids = node["extensions"]?["MSFT_lod"]?["ids"] as JArray;
                var meshIndex = node.Value<int?>("mesh");
                if (ids == null || meshIndex is not int part || part < 0 || part >= meshes.Count)
                    continue;
                foreach (var id in ids)
                {
                    var level = nodes[id.Value<int>()].Value<int?>("mesh");
                    if (level is not int index || index < 0 || index >= meshes.Count || index == part)
                        throw new InvalidDataException($"{name}: a level of detail of {meshes[part].Name} has no mesh");
                    meshes[part].Lods.Add(meshes[index]);
                    meshes[index].Lod = meshes[part].Lods.Count;
                    isLevel[index] = true;
                }
            }
            result.Meshes.AddRange(meshes.Where((m, i) => !isLevel[i]));

            var sdf = json["extras"]?["sdf"];
            if (sdf == null)
                throw new InvalidDataException($"{name} was not written by the SDF pipeline: it has no extras.sdf");
            result.CellSize = sdf.Value<float?>("cellSize").GetValueOrDefault();
            foreach (var material in (JArray)sdf["materials"])
                result.Materials.Add(new Material { Name = material.Value<string>("name"), Parameters = (JObject)material["parameters"] });
            return result;
        }

        private static int Components(string type) => type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new InvalidDataException($"Unsupported accessor type {type}"),
        };
    }
}
