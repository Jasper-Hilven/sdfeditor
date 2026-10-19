using System.Globalization;
using SDFPipeline.Meshing;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class DetailMeshingTests
{
    /// <summary>A crown-like blob two metres across: big, smooth and curved every way, the kind of shape Detail is for.</summary>
    private const string Blob = """
        SmoothUnion smoothness=0.15
            Ellipsoid radii=(0.9, 0.7, 0.8)
            Translate translation=(0.7, -0.1, 0.3)
                Ellipsoid radii=(0.5, 0.45, 0.5)
            Translate translation=(-0.6, 0.1, -0.4)
                Ellipsoid radii=(0.55, 0.5, 0.45)
        """;

    private static readonly Dictionary<string, string> Parts = new()
    {
        ["blob"] = "format 1\n" + Blob,
        ["coarse_blob"] = "format 1\nDetail detail=0.5\n" + Indented(Blob),
    };

    private static readonly MeshingOptions Options = new() { Resolution = 64, LodCount = 3 };

    private static string Indented(string text) => string.Join('\n', text.Split('\n').Select(line => "    " + line));

    private static string WithDetail(float detail, string text) =>
        string.Create(CultureInfo.InvariantCulture, $"Detail detail={detail}\n{Indented(text)}");

    private static IEnumerable<Evaluable> Nodes(Evaluable node) => EvaluableChildren.Children(node).SelectMany(Nodes).Prepend(node);

    private static MeshedModel Mesh(string text) => ModelMesher.Mesh(Read(text, name => Parts.GetValueOrDefault(name)), Options);

    /// <summary>The vertices of each level of detail of the model's only mesh, LOD0 first.</summary>
    private static int[] Vertices(MeshedModel model) =>
        model.Meshes.Single().Lods.Prepend(model.Meshes.Single()).Select(m => m.Statistics.Vertices).ToArray();

    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void Detail_ScalesTheVertices_AtEveryLevelOfDetail(float detail)
    {
        var plain = Vertices(Mesh(Blob));
        var detailed = Vertices(Mesh(WithDetail(detail, Blob)));

        Assert.Equal(Options.LodCount, detailed.Length);
        for (var level = 0; level < plain.Length; level++)
            Assert.InRange((float)detailed[level] / plain[level], detail * 0.8f, detail * 1.25f);
    }

    [Fact]
    public void Detail_KeepsTheErrorLimitsInCells()
    {
        var plain = Mesh(Blob);
        var detailed = Mesh(WithDetail(0.5f, Blob));

        Assert.Equal(plain.Grid!.CellSize * 2, detailed.Grid!.CellSize, 2e-3f);
        var plainLevels = plain.Meshes.Single().Lods.Prepend(plain.Meshes.Single()).ToArray();
        var detailedLevels = detailed.Meshes.Single().Lods.Prepend(detailed.Meshes.Single()).ToArray();
        for (var level = 0; level < plainLevels.Length; level++)
        {
            Assert.Equal(Options.LodErrorInCells(level), plainLevels[level].MaxError / plain.Grid.CellSize, 4);
            Assert.Equal(Options.LodErrorInCells(level), detailedLevels[level].MaxError / detailed.Grid.CellSize, 4);
        }
    }

    [Fact]
    public void Detail_AtTheModelsRoot_AppliesToTheBodyAndItsParts()
    {
        var model = Mesh(WithDetail(0.5f, "Union\n    Sphere radius=0.6\n    Translate translation=(0, 1.2, 0)\n        Part part=blob"));

        Assert.Equal(0.5f, model.Detail);
        Assert.Equal(["body", "blob"], model.Meshes.Select(m => m.Name));
        Assert.All(model.Meshes, mesh => Assert.Equal(0.5f, mesh.Detail));
        Assert.All(model.Meshes.SelectMany(mesh => mesh.Lods), lod => Assert.Equal(0.5f, lod.Detail));
    }

    [Fact]
    public void Detail_AtTheRootOfAPartFile_MultipliesTheModelsForThatPartOnly()
    {
        var sources = PartSplitter.Split(Read(
            WithDetail(0.5f, "Union\n    Sphere radius=0.6\n    Translate translation=(0, 1.2, 0)\n        Part part=coarse_blob\n    Translate translation=(0, -1.2, 0)\n        Part part=blob"),
            name => Parts.GetValueOrDefault(name)));

        Assert.Equal([("body", 0.5f), ("blob", 0.5f), ("coarse_blob", 0.25f)], sources.Select(s => (s.Name, s.Detail)));
        // The detail has left the graph, so no mesh's program has a Detail node in it any more
        Assert.All(sources, source => Assert.DoesNotContain(Nodes(source.Program.scene), node => node is Detail));
    }

    [Fact]
    public void Detail_OfAPart_ScalesItsVertices()
    {
        var plain = Vertices(Mesh("Part part=blob"));
        var coarse = Vertices(Mesh("Part part=coarse_blob"));

        Assert.InRange((float)coarse[0] / plain[0], 0.4f, 0.625f);
    }

    [Fact]
    public void Detail_AroundNothingButParts_LeavesNoBody()
    {
        var sources = PartSplitter.Split(Read(WithDetail(0.5f, "Part part=blob"), name => Parts.GetValueOrDefault(name)));

        Assert.Equal(["blob"], sources.Select(s => s.Name));
    }

    [Fact]
    public void CellsAlong_FollowsTheDetail_WithinTheLimits()
    {
        var options = new MeshingOptions { Resolution = 128, ReferenceSize = 2 };

        Assert.Equal(128, options.CellsAlong(2, 1));
        Assert.Equal(64, options.CellsAlong(2, 0.5f));
        Assert.Equal(256, options.CellsAlong(2, 2));
        Assert.Equal(MeshingOptions.MinResolution, options.CellsAlong(0.5f, Detail.MinDetail));
        Assert.Equal(MeshingOptions.MaxResolution, options.CellsAlong(20, Detail.MaxDetail));
    }
}
