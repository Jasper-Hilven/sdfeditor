using SDFPipeline.Materials;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Rendering;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Tests.Materials;

public class MaterialTests
{
    private static readonly MaterialDefinition Cap = new(new V3(0.86f, 0.45f, 0.06f), 0.65f, 0.4f, new V3(1, 0.55f, 0.15f), 0.03f, 0.35f, 0.03f, 0.15f)
    {
        wetness = 0.35f,
        detailStretch = 6,
        detailAxis = new V3(0, 0, 1),
        edgeColor = new V3(0.78f, 0.32f, 0.02f),
        edgeThickness = 0.1f,
        variationColor = new V3(0.82f, 0.36f, 0.03f),
        variationScale = 0.16f,
        speckleColor = new V3(0.5f, 0.2f, 0.03f),
        speckleScale = 0.026f,
        speckleDensity = 0.6f,
        speckleSize = 0.26f,
    };

    private static SDFProgram Read(string text) => SdfProgramFile.Read("format 1\n" + text);

    private static string MaterialFunction(SDFProgram program, MaterialLibrary library)
    {
        var code = new Webgl2CodeGenerator(library).GenerateShaderCode(program);
        return code[code.IndexOf("SurfaceMaterial sdf_material(vec3 p)", StringComparison.Ordinal)..];
    }

    [Fact]
    public void MaterialFile_RoundTripsEveryParameter()
    {
        var text = MaterialFile.Write(Cap);

        Assert.Equal(Cap, MaterialFile.Read(text));
        Assert.Contains("baseColor = (0.86, 0.45, 0.06)", text);
        Assert.Contains("subsurfaceRadius = 0.03", text);
        Assert.Contains("detailAxis = (0, 0, 1)", text);
        Assert.Contains("variationColor = (0.82, 0.36, 0.03)", text);
        Assert.Contains("speckleDensity = 0.6", text);
    }

    [Fact]
    public void MaterialFile_LeftOutParametersKeepTheirDefaults()
    {
        var material = MaterialFile.Read(
            """
            # A glossy black pupil
            format 1

            baseColor = (0.03, 0.03, 0.04)
            roughness = 0.05
            """);

        Assert.Equal(MaterialDefinition.Default with { baseColor = new V3(0.03f, 0.03f, 0.04f), roughness = 0.05f }, material);
    }

    [Theory]
    [InlineData("format 1\nshininess = 0.5", 2, "Materials have no parameter 'shininess'")]
    [InlineData("format 1\nroughness = (1, 2, 3)", 2, "'roughness' needs a number")]
    [InlineData("format 1\nbaseColor = 0.5", 2, "'baseColor' needs a colour like (0.9, 0.5, 0.1)")]
    [InlineData("format 1\nroughness = 0.5\nroughness = 0.6", 3, "given twice")]
    [InlineData("format 1\ndetailAxis = 1", 2, "'detailAxis' needs a direction like (0, 1, 0)")]
    [InlineData("roughness = 0.5", 1, "must start with 'format 1'")]
    public void MaterialFile_ExplainsWhatIsWrongAndWhere(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => MaterialFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void LoadLibrary_NamesMaterialsAfterTheirFiles()
    {
        var folder = Directory.CreateTempSubdirectory("sdf-materials-");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder.FullName, "eyes"));
            MaterialFile.Save(Path.Combine(folder.FullName, "chanterelle_cap.sdfmat"), Cap);
            MaterialFile.Save(Path.Combine(folder.FullName, "eyes", "eye_white.sdfmat"), MaterialDefinition.Default);

            var library = MaterialFiles.LoadLibrary(folder.FullName);

            Assert.Equal(["chanterelle_cap", "eye_white"], library.Names);
            Assert.Equal(Cap, library.Resolve("chanterelle_cap"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadLibrary_ReportsTheFileOfABrokenMaterial()
    {
        var folder = Directory.CreateTempSubdirectory("sdf-materials-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "broken.sdfmat"), "format 1\nshininess = 1");

            var exception = Assert.Throws<InvalidOperationException>(() => MaterialFiles.LoadLibrary(folder.FullName));

            Assert.Contains("broken.sdfmat:2:", exception.Message);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void MaterialNode_RoundTripsThroughProgramFiles()
    {
        var program = Read(
            """
            Material material=chanterelle_cap
                Sphere radius=1
            """);

        var material = Assert.IsType<Material>(new PointerTables(program.pointers).Expand(program.scene));
        Assert.Equal("chanterelle_cap", material.material);
        Assert.Contains("Material material=chanterelle_cap", SdfProgramFile.Write(program));
        Assert.Equal(["chanterelle_cap"], ModelFiles.MaterialNames(program));
    }

    [Fact]
    public void MaterialNode_UsesTheLibraryDefinition_AndUnknownNamesShowMagenta()
    {
        var library = MaterialLibrary.Empty.With("chanterelle_cap", Cap);

        Assert.Contains(Webgl2CodeGenerator.MaterialLiteral(Cap, "p"), MaterialFunction(Read("Material material=chanterelle_cap\n    Sphere radius=1"), library));
        Assert.Contains(Webgl2CodeGenerator.MaterialLiteral(MaterialDefinition.Missing, "p"), MaterialFunction(Read("Material material=nope\n    Sphere radius=1"), library));
    }

    [Fact]
    public void SmoothUnion_BlendsAColourWithAMaterial()
    {
        var program = Read(
            """
            SmoothUnion smoothness=0.1
                Material material=chanterelle_cap
                    Sphere radius=1
                Color color=(1, 1, 1)
                    Sphere radius=0.5
            """);

        var material = MaterialFunction(program, MaterialLibrary.Empty.With("chanterelle_cap", Cap));

        Assert.Contains("mixMaterial(", material);
        Assert.Contains("colorMaterial(vec3(1.0, 1.0, 1.0), p)", material);
    }

    [Fact]
    public void RadialWave_RoundTripsAndMovesTheSurface()
    {
        var program = Read(
            """
            RadialWave amount=0.1 count=4
                Box size=(1, 0.1, 1)
            """);

        Assert.Contains("RadialWave amount=0.1 count=4", SdfProgramFile.Write(program));
        // A quarter wave around from +x, the surface at radius 0.8 is 0.08 higher than the flat box's top at 0.1
        var crest = new V3(0.8f * MathF.Cos(MathF.PI / 8), 0.18f, 0.8f * MathF.Sin(MathF.PI / 8));
        Assert.InRange(new SdfCalculatorCpu().Calculate(crest, program), -0.01f, 0.01f);
        Assert.Contains("opRadialWave(", new Webgl2CodeGenerator().GenerateShaderCode(program));
    }

    [Fact]
    public void MaterialNode_DoesNotChangeTheDistance()
    {
        var plain = Read("Sphere radius=1");
        var withMaterial = Read("Material material=chanterelle_cap\n    Sphere radius=1");

        Assert.Equal(new SdfCalculatorCpu().Calculate(new V3(0.3f, 1.2f, 0), plain), new SdfCalculatorCpu().Calculate(new V3(0.3f, 1.2f, 0), withMaterial));
    }

    /// <summary>Needs Chrome: proves the material struct, the GGX shading and the subsurface code compile in WebGL.</summary>
    [Fact]
    public async Task Render_WithMaterials_Compiles()
    {
        var program = Read(
            """
            Union
                Material material=chanterelle_cap
                    Sphere radius=0.5
                Color color=(1, 0, 0)
                    Translate translation=(1, 0, 0)
                        Sphere radius=0.3
            """);

        var rendered = await SdfRenderer.RenderAsync(program, new RenderOptions { Size = 128, Materials = MaterialLibrary.Empty.With("chanterelle_cap", Cap) });

        Assert.NotEmpty(rendered.Png);
    }
}
