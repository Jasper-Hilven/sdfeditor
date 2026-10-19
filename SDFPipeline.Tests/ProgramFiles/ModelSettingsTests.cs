using System.Text.Json;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Tests.ProgramFiles;

public class ModelSettingsTests
{
    [Theory]
    [InlineData("format 1\nSphere radius=1", ColliderShape.None, null)]
    [InlineData("format 1\ncollider convex\nSphere radius=1", ColliderShape.Convex, null)]
    [InlineData("format 1\n# walked in\ncollider mesh lod=1\n\nSphere radius=1", ColliderShape.Mesh, 1)]
    [InlineData("format 1\ncollider capsule\nr = 1\nSphere radius=$r", ColliderShape.Capsule, null)]
    public void Read_TakesTheColliderLine(string text, ColliderShape shape, int? lod)
    {
        Assert.Equal(new ModelSettings(shape, lod), ModelSettings.Read(text));
    }

    [Fact]
    public void ProgramReader_LeavesTheSettingsOutOfTheShape()
    {
        var plain = SdfProgramFile.Read("format 1\nSphere radius=1");
        var withCollider = SdfProgramFile.Read("format 1\ncollider mesh\nSphere radius=1");

        Assert.Equal(SdfProgramFile.Write(plain), SdfProgramFile.Write(withCollider));
        // A variable may still be named collider
        SdfProgramFile.Read("format 1\ncollider = 2\nSphere radius=$collider");
    }

    [Theory]
    [InlineData("format 1\ncollider box\nSphere radius=1", 2, "A collider is none, convex, mesh or capsule")]
    [InlineData("format 1\ncollider mesh\ncollider convex\nSphere radius=1", 3, "The collider is given twice, first on line 2")]
    [InlineData("format 1\ncollider capsule lod=1\nSphere radius=1", 2, "A capsule collider is not made from a mesh")]
    [InlineData("format 1\ncollider mesh level=1\nSphere radius=1", 2, "only lod= can follow")]
    [InlineData("format 1\nshadows soft\nSphere radius=1", 2, "Shadows are on, off or near")]
    [InlineData("format 1\noutline\nSphere radius=1", 2, "The outline is on or off")]
    [InlineData("format 1\ncull -3\nSphere radius=1", 2, "Give the distance in metres")]
    [InlineData("format 1\ncull\nSphere radius=1", 2, "Give the distance in metres")]
    [InlineData("format 1\ncull 30\noutline off\ncull 40\nSphere radius=1", 4, "The cull is given twice, first on line 2")]
    [InlineData("format 1\nshadows on off\nSphere radius=1", 2, "Unexpected 'off'")]
    public void Read_ExplainsWhatIsWrongAndWhere(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => SdfProgramFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_TakesShadowsOutlineAndCull()
    {
        var settings = ModelSettings.Read("format 1\nshadows near\noutline off\ncull 35.5\nSphere radius=1");

        Assert.Equal(new ModelSettings(ColliderShape.None, null) { Shadows = ShadowCasting.Near, Outline = false, Cull = 35.5f }, settings);
        Assert.Equal((ShadowCasting.On, true, (float?)null), (ModelSettings.Default.Shadows, ModelSettings.Default.Outline, ModelSettings.Default.Cull));
    }

    [Fact]
    public void Part_HasSettingsOfItsOwn_OverTheModels()
    {
        const string lid = "format 1\ncollider none\nshadows off\nSphere radius=1";
        var model = new ModelSettings(ColliderShape.Mesh, 1) { Outline = false, Cull = 40 };

        // The program reader leaves them out of the shape, as a model's
        SdfProgramFile.Read("format 1\nPart part=lid", name => name == "lid" ? lid : null);
        Assert.True(ModelSettings.HasSettings(lid));
        Assert.False(ModelSettings.HasSettings("format 1\nlid = 1\nSphere radius=$lid"));
        // What the part sets wins, the rest is the model's
        Assert.Equal(new ModelSettings(ColliderShape.None, null) { Shadows = ShadowCasting.Off, Outline = false, Cull = 40 }, ModelSettings.Read(lid, model));
    }

    private static JsonElement Collider(string text, int lods)
    {
        var program = SdfProgramFile.Read(text);
        var model = ModelMesher.Mesh(program, new MeshingOptions { Resolution = 24, LodCount = lods }) with { Settings = ModelSettings.Read(text) };
        var glb = GlbWriter.Write(model, "test");
        var length = BitConverter.ToInt32(glb, 12);
        using var json = JsonDocument.Parse(glb.AsMemory(20, length));
        return json.RootElement.GetProperty("extras").GetProperty("sdf").TryGetProperty("collider", out var collider)
            ? collider.Clone()
            : default;
    }

    [Fact]
    public void Glb_GivesTheColliderInItsExtras_FromTheCoarsestLevelUnlessAsked()
    {
        var convex = Collider("format 1\ncollider convex\nSphere radius=0.5", 3);
        Assert.Equal("convex", convex.GetProperty("shape").GetString());
        Assert.Equal(2, convex.GetProperty("lod").GetInt32());

        var asked = Collider("format 1\ncollider mesh lod=1\nSphere radius=0.5", 3);
        Assert.Equal(("mesh", 1), (asked.GetProperty("shape").GetString(), asked.GetProperty("lod").GetInt32()));
        // A level the file does not have: the coarsest it has
        Assert.Equal(0, Collider("format 1\ncollider mesh lod=3\nSphere radius=0.5", 1).GetProperty("lod").GetInt32());

        Assert.Equal(JsonValueKind.Undefined, Collider("format 1\nSphere radius=0.5", 1).ValueKind);
    }

    [Fact]
    public void Glb_MakesALargeConvexColliderAMesh()
    {
        var outcrop = Collider("format 1\ncollider convex\nBox size=(2, 0.5, 1)", 1);

        Assert.Equal("mesh", outcrop.GetProperty("shape").GetString());
        Assert.Equal("convex", outcrop.GetProperty("asked").GetString());
    }

    [Fact]
    public void Glb_FitsACapsuleUprightThroughTheModel()
    {
        var capsule = Collider("format 1\ncollider capsule\nTranslate translation=(0, 1, 0)\n    CappedCylinder radius=0.3 height=1", 1);

        Assert.Equal("capsule", capsule.GetProperty("shape").GetString());
        Assert.Equal(0, capsule.GetProperty("base")[1].GetSingle(), 1);
        Assert.Equal(2, capsule.GetProperty("top")[1].GetSingle(), 1);
        Assert.Equal(0.3f, capsule.GetProperty("radius").GetSingle(), 1);
    }

    [Fact]
    public void ContentRocksAndCastles_HaveColliders()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

        Assert.Equal(ColliderShape.Mesh, ModelSettings.Load(Path.Combine(content, "models", "buildings", "castle0.sdf")).Collider);
        Assert.All(Directory.GetFiles(Path.Combine(content, "models", "rocks"), "*.sdf"),
            rock => Assert.Equal(ColliderShape.Convex, ModelSettings.Load(rock).Collider));
    }
}
