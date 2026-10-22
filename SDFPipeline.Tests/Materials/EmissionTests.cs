using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.Materials;
using SDFPipeline.Meshing;
using SDFProject.ProgramDefinition;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Materials;

/// <summary>Emission (ticket 30.1): a material's glow, carried to the toon shaders and a strict no-op at 0.</summary>
public class EmissionTests
{
    private static readonly MeshingOptions Options = new() { Resolution = 32 };

    [Fact]
    public void Emission_DefaultsToZero_AndIsLeftOutOfFilesThatDoNotGlow()
    {
        Assert.Equal(0, MaterialDefinition.Default.emission);
        Assert.Equal(0, MaterialFile.Read("format 1\nbaseColor = (1, 0.8, 0.2)").emission);

        var plain = MaterialFile.Write(MaterialDefinition.Default);
        Assert.DoesNotContain("emission", plain);
        Assert.Equal(MaterialDefinition.Default, MaterialFile.Read(plain));
    }

    [Fact]
    public void Emission_ParsesAndRoundTrips()
    {
        var gold = MaterialFile.Read("format 1\nbaseColor = (1, 0.8, 0.2)\nemission = 0.75");

        Assert.Equal(0.75f, gold.emission);
        var text = MaterialFile.Write(gold);
        Assert.Contains("emission = 0.75", text);
        Assert.Equal(gold, MaterialFile.Read(text));
    }

    [Fact]
    public void Emission_IsWeightedLikeEveryOtherParameterWhereMaterialsMix()
    {
        var glow = MaterialDefinition.Default with { emission = 1 };
        var mix = SurfaceAttributes.Mix([(glow, 0.25f), (MaterialDefinition.Default, 0.75f)]);

        Assert.Equal(0.25f, mix.emission, 4);
    }

    [Fact]
    public void Glb_CarriesEmissionPerVertexOnlyOnMeshesThatGlow()
    {
        var program = Read("Union\n    Material material=lamp\n        Translate translation=(-0.5, 0, 0)\n            Sphere radius=0.3\n    Material material=wall\n        Translate translation=(0.5, 0, 0)\n            Sphere radius=0.3");
        var library = MaterialLibrary.Empty.With("lamp", MaterialDefinition.Default with { emission = 0.6f }).With("wall", MaterialDefinition.Default);
        var glowing = ModelMesher.Mesh(program, Options with { Materials = library });
        var plain = ModelMesher.Mesh(Read("Material material=wall\n    Sphere radius=0.3"), Options with { Materials = library });

        var (json, binary) = Chunks(GlbWriter.Write(glowing, "lamp"));
        var mesh = json.RootElement.GetProperty("meshes")[0];
        var attributes = mesh.GetProperty("primitives")[0].GetProperty("attributes");
        var accessor = json.RootElement.GetProperty("accessors")[attributes.GetProperty(GlbWriter.EmissionAttribute).GetInt32()];
        var view = json.RootElement.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var values = new float[accessor.GetProperty("count").GetInt32()];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(binary.AsSpan(view.GetProperty("byteOffset").GetInt32() + 4 * i));

        Assert.Equal("SCALAR", accessor.GetProperty("type").GetString());
        Assert.Equal(glowing.Meshes[0].Positions.Length, values.Length);
        // The lamp's own vertices glow at its amount, the wall's not at all, and the blend between is in range
        Assert.Contains(values, v => MathF.Abs(v - 0.6f) < 1e-4f);
        Assert.Contains(values, v => v == 0);
        Assert.All(values, v => Assert.InRange(v, 0, 0.6f + 1e-4f));
        Assert.Equal(0.6f, json.RootElement.GetProperty("extras").GetProperty("sdf").GetProperty("materials")[0].GetProperty("parameters").GetProperty("emission").GetSingle());

        // Nothing glows: no attribute, and the materials list no emission, so the file is as it was
        var (plainJson, _) = Chunks(GlbWriter.Write(plain, "wall"));
        Assert.DoesNotContain(GlbWriter.EmissionAttribute, plainJson.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetRawText());
        Assert.DoesNotContain("emission", plainJson.RootElement.GetProperty("extras").GetProperty("sdf").GetProperty("materials").GetRawText());
    }

    [Fact]
    public void Bake_ListsAGroundRegionsEmission_AndNothingForRegionsThatDoNotGlow()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var level = new LevelDefinition(new PlayArea(new Vector2(20, 10), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4))
            {
                Regions =
                [
                    new GroundRegion("trace", [new CircleRegion(Vector2.Zero, 4, 1, 5)], GroundPattern.Plain, 0.6f, 0.05f),
                    new GroundRegion("pad", [new CircleRegion(new Vector2(8, 0), 2, 1, 5)], GroundPattern.Plain, 0.6f, 0.05f),
                ],
            };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default)
                .With("ground_trace", MaterialDefinition.Default with { emission = 0.8f }).With("ground_pad", MaterialDefinition.Default);

            LevelBaker.Bake(level, "small", materials, root.FullName);

            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var baked = json.RootElement.GetProperty("materials");
            Assert.Equal(0.8f, baked.GetProperty("trace").GetProperty("parameters").GetProperty("emission").GetSingle());
            Assert.False(baked.GetProperty("pad").GetProperty("parameters").TryGetProperty("emission", out _));
            Assert.False(baked.GetProperty("grass").GetProperty("parameters").TryGetProperty("emission", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static (JsonDocument Json, byte[] Binary) Chunks(byte[] glb)
    {
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        var binaryStart = 20 + jsonLength;
        var binaryLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binaryStart));
        return (JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength)), glb.AsSpan(binaryStart + 8, binaryLength).ToArray());
    }
}
