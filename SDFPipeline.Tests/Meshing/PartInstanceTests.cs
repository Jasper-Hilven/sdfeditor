using System.Numerics;
using System.Text;
using System.Text.Json;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class PartInstanceTests
{
    /// <summary>A part with no symmetry of its own, so a wrong turn or mirror of it shows, standing on its origin.</summary>
    private static readonly Dictionary<string, string> Parts = new()
    {
        ["post"] = "format 1\nUnion\n    Translate translation=(0, 0.3, 0)\n        Box size=(0.2, 0.6, 0.1)\n    Translate translation=(0.15, 0.5, 0)\n        Sphere radius=0.08",
        ["cap"] = "format 1\ncollider none\nshadows near\noutline off\ncull 30\nEllipsoid radii=(0.2, 0.1, 0.2)",
    };

    private static List<string> warnings = [];

    private static IReadOnlyList<MeshSource> Split(string text)
    {
        warnings = [];
        return PartSplitter.Split(Read(text, name => Parts.GetValueOrDefault(name)), warnings);
    }

    private static MeshSource Post(string text) => Split(text).Single(s => s.Part == "post");

    /// <summary>
    /// Each instance puts the part where the model has it: the model's distance at a placed point is the part's distance there, times
    /// the placement's scale. The model has nothing but the part's uses, all apart, so the nearest one is the only one that counts.
    /// </summary>
    private static void AssertInstancesMatchTheModel(string text, int count)
    {
        var program = Read(text, name => Parts.GetValueOrDefault(name));
        var post = Post(text);
        var model = new SdfCalculatorCpu().Prepare(PartSplitter.Expanded(program));
        var part = new SdfCalculatorCpu().Prepare(post.Program);

        Assert.Equal(count, post.Instances.Count);
        Assert.Empty(warnings);
        var samples = new[] { new Vector3(0, 0.1f, 0), new Vector3(0.25f, 0.5f, 0.02f), new Vector3(-0.12f, 0.4f, 0.06f), new Vector3(0.1f, 0.62f, -0.05f) };
        foreach (var placement in post.Instances)
        {
            var scale = new Vector3(placement.M11, placement.M12, placement.M13).Length();
            foreach (var sample in samples)
                Assert.Equal(part.Distance(sample) * scale, model.Distance(Vector3.Transform(sample, placement)), 3);
        }
        // Every use is somewhere else
        Assert.Equal(count, post.Instances.Select(m => m.Translation).Distinct().Count());
    }

    [Theory]
    [InlineData("""
        Union
            Translate translation=(1, 0, 0)
                Part part=post
            Translate translation=(-2, 0.5, 1)
                Part part=post
        """, 2)]
    [InlineData("""
        Union
            Translate translation=(1, 0, 0)
                Rotate rotation=(0.3, 1.2, -0.4)
                    Part part=post
            Translate translation=(-1, 0, 0)
                Scale scale=(0.5, 0.5, 0.5)
                    Rotate rotation=(0, 0, 0.7)
                        Part part=post
        """, 2)]
    [InlineData("""
        SymmetryX
            Translate translation=(1, 0, 0.5)
                Rotate rotation=(0, 0.4, 0)
                    Part part=post
        """, 2)]
    public void Instances_PlaceThePartAsTheModelDoes(string text, int count) => AssertInstancesMatchTheModel(text, count);

    [Fact]
    public void NestedSymmetries_MirrorEveryWay()
    {
        const string text = """
            SymmetryXZ
                Translate translation=(2, 0, 2)
                    SymmetryX
                        Translate translation=(0.5, 0, 0)
                            Rotate rotation=(0, 0.5, 0)
                                Part part=post
            """;
        AssertInstancesMatchTheModel(text, 8);

        // Each mirror turns the triangles round; two mirrors turn them back
        var determinants = Post(text).Instances.Select(m => MathF.Sign(m.GetDeterminant())).ToArray();
        Assert.Equal(4, determinants.Count(d => d < 0));
    }

    [Fact]
    public void AMirrorImageBehindAnOuterMirror_IsNeverSeen_AndLeftOut()
    {
        AssertInstancesMatchTheModel("SymmetryX\n    SymmetryX\n        Translate translation=(1, 0, 0)\n            Part part=post", 2);
    }

    [Fact]
    public void LimitedRepetition_GivesACopyPerCell()
    {
        const string text = """
            LimitedRepetition spacing=1 limits=(2, 0, 1)
                Part part=post
            """;
        AssertInstancesMatchTheModel(text, 5 * 1 * 3);
        Assert.Equal(new Vector3(-2, 0, -1), Post(text).Instances[0].Translation);
    }

    [Fact]
    public void APartUsedOnce_IsMeshedInPlace_AsBefore()
    {
        var post = Post("Translate translation=(1, 0, 0)\n    Part part=post");

        Assert.Empty(post.Instances);
        Assert.Empty(warnings);
        Assert.True(Distance(post.Program)(new Vector3(1, 0.3f, 0)) < 0);
    }

    [Theory]
    [InlineData("SmoothUnion smoothness=0.1\n    Part part=post\n    Translate translation=(1, 0, 0)\n        Part part=post", "inside a SmoothUnion")]
    [InlineData("SymmetryX\n    Translate translation=(1, 0, 0)\n        Scale scale=(1, 2, 1)\n            Part part=post", "under a non-uniform Scale")]
    [InlineData("SymmetryX\n    Translate translation=(1, 0, 0)\n        Distort amount=0.01 frequency=3\n            Part part=post", "inside a Distort")]
    [InlineData("SymmetryX\n    Translate translation=(1, 0, 0)\n        Displace\n            Part part=post\n            Sphere radius=0.01", "inside a Displace")]
    [InlineData("Union\n    Part part=post\n    Intersection\n        Part part=post\n        Sphere radius=0.4", "inside an Intersection")]
    [InlineData("Union\n    Part part=post\n    Subtraction\n        Box size=(3, 3, 3)\n        Part part=post", "the cutter of a Subtraction")]
    [InlineData("SymmetryX\n    Translate translation=(0.05, 0, 0)\n        Part part=post", "across the mirror of a SymmetryX")]
    [InlineData("LimitedRepetition spacing=0.2 limits=(2, 0, 0)\n    Part part=post", "wider than the cell of a LimitedRepetition")]
    [InlineData("LimitedRepetition spacing=1 limits=(1.5, 0, 0)\n    Part part=post", "limits are not whole numbers")]
    [InlineData("Repetition spacing=(1, 0, 0)\n    Part part=post", "under an endless Repetition")]
    public void AUseThatIsNotRigid_KeepsThePartInPlace_WithAWarning(string text, string why)
    {
        var post = Post(text);

        Assert.Empty(post.Instances);
        var warning = Assert.Single(warnings);
        Assert.Contains("Part 'post'", warning);
        Assert.Contains(why, warning);
    }

    [Fact]
    public void UsesUnderDifferentMaterials_GetAMeshEach()
    {
        var sources = Split("""
            Union
                Material material=red
                    SymmetryX
                        Translate translation=(1, 0, 0)
                            Part part=post
                Translate translation=(0, 0, 2)
                    Material material=brown
                        Part part=post
                Translate translation=(0, 0, -2)
                    Material material=red
                        Material material=brown
                            Part part=post
            """).Where(s => s.Part is not null).ToArray();

        // The innermost material is the one the part shows
        Assert.Equal(["post", "post@brown"], sources.Select(s => s.Name));
        Assert.Equal([2, 2], sources.Select(s => s.Instances.Count));
        Assert.All(sources, s => Assert.Equal("post", s.Part));
        var surface = new SdfCalculatorCpu().Prepare(sources[1].Program).Surface(new Vector3(0, 0.3f, 0.05f));
        Assert.Equal("brown", surface.Materials.MaxBy(m => m.Weight).Source.Name);
    }

    private static readonly MeshingOptions Options = new() { Resolution = 32, LodCount = 2 };

    private static JsonElement Json(MeshedModel model)
    {
        var glb = GlbWriter.Write(model, "test");
        var length = BitConverter.ToInt32(glb, 12);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(glb, 20, length));
        return json.RootElement.Clone();
    }

    [Fact]
    public void Mesh_IsMadeOnce_InThePartsSpace_AndDrawnPerInstance()
    {
        var program = Read("SymmetryX\n    Translate translation=(1, 0, 0)\n        Part part=post", name => Parts.GetValueOrDefault(name));
        var model = ModelMesher.Mesh(program, Options);
        var post = Assert.Single(model.Meshes);

        Assert.Equal(2, post.Instances.Count);
        Assert.InRange(post.Positions.Average(p => p.X), -0.2f, 0.2f);
        Assert.All(post.Thickness, t => Assert.InRange(t, 0, Options.MaxThickness));

        var root = Json(model);
        var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(["post.0", "post.1", "post_LOD1.0", "post_LOD1.1"], nodes.Select(n => n.GetProperty("name").GetString()));
        Assert.Equal([0, 1], root.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()));
        Assert.Equal(2, root.GetProperty("meshes").GetArrayLength());
        Assert.Equal(2, root.GetProperty("meshes")[0].GetProperty("extras").GetProperty("instances").GetInt32());
        // glTF's matrices are column-major: the translation is last, and the mirrored use's x axis points the other way
        var mirrored = nodes[1].GetProperty("matrix").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Assert.Equal([-1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1], mirrored);
        // Each use lists its own level, placed as it is
        Assert.Equal(2, nodes[0].GetProperty("extensions").GetProperty(GlbWriter.LodExtension).GetProperty("ids")[0].GetInt32());
        Assert.Equal(mirrored, nodes[3].GetProperty("matrix").EnumerateArray().Select(v => v.GetSingle()));
        Assert.Equal(1, nodes[3].GetProperty("mesh").GetInt32());
    }

    [Fact]
    public void Glb_WithoutRepeatedParts_HasNoInstances()
    {
        var program = Read("Union\n    Sphere radius=0.3\n    Translate translation=(1, 0, 0)\n        Part part=post", name => Parts.GetValueOrDefault(name));
        var root = Json(ModelMesher.Mesh(program, Options));

        Assert.DoesNotContain("matrix", root.GetRawText());
        Assert.DoesNotContain("instances", root.GetRawText());
        Assert.DoesNotContain("parts", root.GetProperty("extras").GetProperty("sdf").GetRawText());
        Assert.Equal(["body", "post", "body_LOD1", "post_LOD1"], root.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()));
    }

    [Fact]
    public void Glb_GivesEachPartWithSettingsItsOwn_OverTheModels()
    {
        const string text = "format 1\ncollider mesh\nshadows off\nUnion\n    Part part=post\n    SymmetryX\n        Translate translation=(0.5, 1, 0)\n            Part part=cap";
        var program = SdfProgramFile.Read(text, name => Parts.GetValueOrDefault(name));
        var settings = ModelSettings.Read(text);
        var model = ModelMesher.Mesh(program, Options) with
        {
            Settings = settings,
            PartSettings = new Dictionary<string, ModelSettings> { ["cap"] = ModelSettings.Read(Parts["cap"], settings) },
        };
        var sdf = Json(model).GetProperty("extras").GetProperty("sdf");

        Assert.Equal("mesh", sdf.GetProperty("collider").GetProperty("shape").GetString());
        Assert.Equal("off", sdf.GetProperty("shadows").GetString());
        Assert.False(sdf.TryGetProperty("outline", out _));
        var parts = sdf.GetProperty("parts");
        Assert.False(parts.TryGetProperty("post", out _));
        var cap = parts.GetProperty("cap");
        Assert.Equal("none", cap.GetProperty("collider").GetProperty("shape").GetString());
        Assert.Equal("near", cap.GetProperty("shadows").GetString());
        Assert.False(cap.GetProperty("outline").GetBoolean());
        Assert.Equal(30, cap.GetProperty("cull").GetSingle());
    }
}
