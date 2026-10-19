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
    public void Read_TakesTheSurfaceLine_NextToTheCollider()
    {
        Assert.Equal(new ModelSettings(ColliderShape.None, null, "amanita_scout"),
            ModelSettings.Read("format 1\nsurface part=amanita_scout\nSphere radius=1"));
        Assert.Equal(new ModelSettings(ColliderShape.Convex, null, "rock"),
            ModelSettings.Read("format 1\ncollider convex\nsurface part=rock\nSphere radius=1"));
        Assert.Null(ModelSettings.Read("format 1\nSphere radius=1").SurfacePart);
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
    [InlineData("format 1\nsurface amanita_scout\nSphere radius=1", 2, "The surface names the part it is measured on")]
    [InlineData("format 1\nsurface part=a\nsurface part=b\nSphere radius=1", 3, "The surface is given twice, first on line 2")]
    public void Read_ExplainsWhatIsWrongAndWhere(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => SdfProgramFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Part_HasNoSettingsOfItsOwn()
    {
        var exception = Assert.Throws<SdfProgramFileException>(() =>
            SdfProgramFile.Read("format 1\nPart part=lid", name => name == "lid" ? "format 1\ncollider convex\nSphere radius=1" : null));

        Assert.Contains("A part has no settings of its own", exception.Problem);
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
