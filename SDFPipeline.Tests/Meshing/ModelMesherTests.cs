using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SDFPipeline.Meshing;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class ModelMesherTests
{
    private static readonly Dictionary<string, string> Parts = new()
    {
        ["eye"] = "format 1\nUnion\n    Material material=white\n        Sphere radius=0.1\n    Material material=pupil\n        Translate translation=(0, 0, 0.09)\n            Sphere radius=0.03",
        ["hand"] = "format 1\nEllipsoid radii=(0.06, 0.08, 0.05)",
    };

    /// <summary>Like the chanterelle scout: a body, mirrored eyes and hands, and a part used only to cut a hole.</summary>
    private const string Scout = """
        Union
            Subtraction
                Material material=flesh
                    VerticalCapsule height=0.6 radius=0.2
                Translate translation=(0, 0.1, 0.2)
                    Part part=hand
            SymmetryX
                Translate translation=(0.1, 0.5, 0.17)
                    Part part=eye
            SymmetryX
                Translate translation=(0.35, 0.3, 0)
                    Part part=hand
        """;

    private static SDFProgram ReadScout() => Read(Scout, name => Parts.GetValueOrDefault(name));

    private static readonly MeshingOptions Options = new() { Resolution = 48 };

    [Fact]
    public void Split_GivesTheBodyAndEachPartOnItsOwn()
    {
        var sources = PartSplitter.Split(ReadScout());

        Assert.Equal(["body", "eye", "hand"], sources.Select(s => s.Name));
        float D(string name, float x, float y, float z) =>
            new SdfCalculatorCpu().Prepare(sources.Single(s => s.Name == name).Program).Distance(new Vector3(x, y, z));

        // Both eyes and both hands are in their part's mesh, placed and mirrored, and in no other
        Assert.True(D("eye", 0.1f, 0.5f, 0.17f) < 0 && D("eye", -0.1f, 0.5f, 0.17f) < 0);
        Assert.True(D("body", 0.1f, 0.5f, 0.3f) > 0 && D("hand", 0.1f, 0.5f, 0.3f) > 0);
        Assert.True(D("hand", 0.35f, 0.31f, 0) < 0 && D("hand", -0.35f, 0.31f, 0) < 0);
        Assert.True(D("body", 0.35f, 0.3f, 0) > 0);
        // The hand that cuts a hole in the body is part of the body's shape, not a hand of its own
        Assert.True(D("body", 0, 0.1f, 0.19f) > 0);
        Assert.True(D("hand", 0, 0.1f, 0.19f) > 0);
        Assert.True(D("body", 0, 0.3f, 0) < 0);
    }

    [Fact]
    public void SmallerThings_GetFewerButFinerCells()
    {
        var options = new MeshingOptions { Resolution = 128, ReferenceSize = 2 };

        Assert.Equal(128, options.CellsAlong(2));
        Assert.Equal(64, options.CellsAlong(0.5f));
        Assert.Equal(MeshingOptions.MinResolution, options.CellsAlong(0.001f));
        Assert.Equal(MeshingOptions.MaxResolution, options.CellsAlong(1000));
        Assert.Equal(128, (options with { SizeExponent = 0 }).CellsAlong(0.5f));
        Assert.Equal(32, (options with { SizeExponent = 1 }).CellsAlong(0.5f));

        int Triangles(float radius) => ModelMesher.Mesh(Read(string.Create(CultureInfo.InvariantCulture, $"Sphere radius={radius}")), options).Meshes.Sum(m => m.TriangleCount);
        Assert.True(Triangles(0.1f) < Triangles(1), "A small sphere costs as much as a big one");
    }

    [Fact]
    public void Mesh_MakesClosedMeshesOnTheSurface_WithTheirOwnMaterials()
    {
        var program = ReadScout();
        var model = ModelMesher.Mesh(program, Options);
        var cellSize = model.Grid!.CellSize;

        Assert.Equal(["body", "eye", "hand"], model.Meshes.Select(m => m.Name));
        Assert.Equal(["flesh", "white", "pupil", "default"], model.Materials.Select(m => m.Name));
        foreach (var mesh in model.Meshes)
        {
            Assert.True(mesh.Statistics.SimplifiedTriangles < mesh.Statistics.ContouredTriangles);
            AssertClosedAndOriented(Welded(mesh));
            Assert.True(SignedVolume(Welded(mesh)) > 0);
            var sdf = Distance(PartSplitter.Split(program).Single(s => s.Name == mesh.Name).Program);
            Assert.All(mesh.Positions, p => Assert.InRange(MathF.Abs(sdf(p)), 0, 0.25f * cellSize));
            Assert.All(mesh.Normals, n => Assert.Equal(1, n.Length(), 3));
            Assert.All(mesh.Thickness, t => Assert.InRange(t, 0, Options.MaxThickness));
            Assert.All(mesh.Occlusion, o => Assert.InRange(o, 0, 1));
        }

        var eye = model.Meshes.Single(m => m.Name == "eye");
        var names = eye.Materials.Select(m => model.Materials[m.First].Name).ToHashSet();
        Assert.Equal(["white", "pupil"], names.Order().Reverse());
    }

    [Fact]
    public void Glb_ReadsBackWithEveryAccessorInItsBuffer()
    {
        var model = ModelMesher.Mesh(ReadScout(), Options with { Materials = MaterialLibrary.Empty.With("flesh", MaterialDefinition.Default) });
        var glb = GlbWriter.Write(model, "scout");

        Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(glb));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        Assert.Equal((uint)glb.Length, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        Assert.Equal(0x4E4F534Au, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)));
        Assert.Equal(0, jsonLength % 4);
        var binaryStart = 20 + jsonLength;
        var binaryLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binaryStart));
        Assert.Equal(0x004E4942u, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binaryStart + 4)));
        Assert.Equal(0, binaryLength % 4);
        Assert.Equal(glb.Length, binaryStart + 8 + binaryLength);
        var binary = glb.AsSpan(binaryStart + 8, binaryLength).ToArray();

        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));
        var root = json.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        Assert.Equal(binaryLength, root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());
        var views = root.GetProperty("bufferViews").EnumerateArray().ToArray();
        var accessors = root.GetProperty("accessors").EnumerateArray().ToArray();
        foreach (var view in views)
        {
            Assert.Equal(0, view.GetProperty("byteOffset").GetInt32() % 4);
            Assert.True(view.GetProperty("byteOffset").GetInt32() + view.GetProperty("byteLength").GetInt32() <= binaryLength);
        }

        var meshes = root.GetProperty("meshes").EnumerateArray().ToArray();
        Assert.Equal(model.Meshes.Select(m => m.Name), meshes.Select(m => m.GetProperty("name").GetString()));
        Assert.Equal(model.Meshes.Select(m => m.Name), root.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()));
        for (var i = 0; i < meshes.Length; i++)
        {
            var mesh = model.Meshes[i];
            var primitives = meshes[i].GetProperty("primitives").EnumerateArray().ToArray();
            var primitive = primitives[0];
            var attributes = primitive.GetProperty("attributes");
            Assert.Equal(["POSITION", "NORMAL", "COLOR_0", "_MATERIAL_IDS", "_MATERIAL_BLEND", "_THICKNESS", "_OCCLUSION", "_LOCAL_POSITION",
                    "_BASE_COLOR", "_SURFACE", "_DETAIL", "_VARIATION", "_SPECKLE", "_SPECKLE_SHAPE"],
                attributes.EnumerateObject().Select(a => a.Name));
            foreach (var attribute in attributes.EnumerateObject())
            {
                var accessor = accessors[attribute.Value.GetInt32()];
                Assert.Equal(mesh.Positions.Length, accessor.GetProperty("count").GetInt32());
                var components = accessor.GetProperty("type").GetString() switch { "VEC4" => 4, "VEC3" => 3, "VEC2" => 2, _ => 1 };
                Assert.Equal(mesh.Positions.Length * components * 4, views[accessor.GetProperty("bufferView").GetInt32()].GetProperty("byteLength").GetInt32());
            }

            var position = accessors[attributes.GetProperty("POSITION").GetInt32()];
            var floats = Floats(binary, views[position.GetProperty("bufferView").GetInt32()]);
            for (var c = 0; c < 3; c++)
            {
                Assert.Equal(floats.Where((_, j) => j % 3 == c).Min(), position.GetProperty("min")[c].GetSingle());
                Assert.Equal(floats.Where((_, j) => j % 3 == c).Max(), position.GetProperty("max")[c].GetSingle());
            }
            Assert.Equal(mesh.Positions[^1].Z, floats[^1]);

            // One primitive per material region, all on the same vertices, together holding every triangle once
            Assert.All(primitives, p => Assert.Equal(attributes.GetRawText(), p.GetProperty("attributes").GetRawText()));
            Assert.Equal(primitives.Length, primitives.Select(p => p.GetProperty("material").GetInt32()).Distinct().Count());
            var counted = 0;
            foreach (var region in primitives)
            {
                var indices = accessors[region.GetProperty("indices").GetInt32()];
                counted += indices.GetProperty("count").GetInt32();
                Assert.Equal(5125, indices.GetProperty("componentType").GetInt32());
                Assert.True(indices.GetProperty("max")[0].GetInt32() < mesh.Positions.Length);
                Assert.InRange(region.GetProperty("material").GetInt32(), 0, model.Materials.Count - 1);
            }
            Assert.Equal(mesh.Triangles.Length, counted);
        }

        // Each material of the table is a glTF material, in the same order, with its roughness and gloss in core glTF and extensions
        var gltfMaterials = root.GetProperty("materials").EnumerateArray().ToArray();
        Assert.Equal(model.Materials.Select(m => m.Name), gltfMaterials.Select(m => m.GetProperty("name").GetString()));
        Assert.Equal(MaterialDefinition.Default.roughness, gltfMaterials[0].GetProperty("pbrMetallicRoughness").GetProperty("roughnessFactor").GetSingle());
        Assert.Equal(1.5f, gltfMaterials[0].GetProperty("extensions").GetProperty("KHR_materials_ior").GetProperty("ior").GetSingle(), 3);

        var materials = root.GetProperty("extras").GetProperty("sdf").GetProperty("materials").EnumerateArray().ToArray();
        Assert.Equal(model.Materials.Select(m => m.Name), materials.Select(m => m.GetProperty("name").GetString()));
        Assert.Equal(MaterialDefinition.Default.roughness, materials[0].GetProperty("parameters").GetProperty("roughness").GetSingle());
        Assert.Equal(3, materials[0].GetProperty("parameters").GetProperty("baseColor").GetArrayLength());
    }

    private static float[] Floats(byte[] binary, JsonElement view)
    {
        var offset = view.GetProperty("byteOffset").GetInt32();
        return Enumerable.Range(0, view.GetProperty("byteLength").GetInt32() / 4)
            .Select(i => BinaryPrimitives.ReadSingleLittleEndian(binary.AsSpan(offset + 4 * i)))
            .ToArray();
    }

}
