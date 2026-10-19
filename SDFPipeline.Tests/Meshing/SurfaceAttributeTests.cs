using System.Numerics;
using SDFPipeline.Meshing;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using static SDFPipeline.Tests.Meshing.MeshTestPrograms;

namespace SDFPipeline.Tests.Meshing;

public class SurfaceAttributeTests
{
    private static readonly MaterialLibrary Library = MaterialLibrary.Empty
        .With("red", MaterialDefinition.Default with { baseColor = new V3(1, 0, 0) })
        .With("blue", MaterialDefinition.Default with { baseColor = new V3(0, 0, 1) });

    private static SurfaceSample Sample(string text, Vector3 p) => new SdfCalculatorCpu().Prepare(Read(text)).Surface(p);

    private const string TwoSpheres = """
        {0}
            Material material=red
                Translate translation=(-0.3, 0, 0)
                    Sphere radius=0.4
            Material material=blue
                Translate translation=(0.3, 0, 0)
                    Sphere radius=0.4
        """;

    [Fact]
    public void Union_PicksTheNearerShapesMaterial()
    {
        var sample = Sample(string.Format(TwoSpheres, "Union"), new Vector3(-0.1f, 0.3f, 0));

        var share = Assert.Single(sample.Materials);
        Assert.Equal(MaterialSource.Named("red"), share.Source);
        Assert.Equal(1, share.Weight);
        Assert.Equal(new Vector3(-0.1f, 0.3f, 0), share.LocalPosition); // where the Material node is, above its Translate, as in the renderer
    }

    [Fact]
    public void SmoothUnion_BlendsWithTheRenderersWeight()
    {
        var p = new Vector3(-0.05f, 0.3f, 0);
        var sample = Sample(string.Format(TwoSpheres, "SmoothUnion smoothness=0.5"), p);

        // The renderer: mixMaterial(b, a, clamp(0.5 + 0.125 (dB − dA) / k, 0, 1)), a being the first shape
        var dRed = (p - new Vector3(-0.3f, 0, 0)).Length() - 0.4f;
        var dBlue = (p - new Vector3(0.3f, 0, 0)).Length() - 0.4f;
        var redWeight = Math.Clamp(0.5f + 0.125f * (dBlue - dRed) / 0.5f, 0, 1);
        Assert.Equal(2, sample.Materials.Count);
        Assert.Equal(redWeight, sample.Materials.Single(s => s.Source.Name == "red").Weight, 5);
        Assert.Equal(1 - redWeight, sample.Materials.Single(s => s.Source.Name == "blue").Weight, 5);

        var table = new MaterialTable(Library);
        var vertex = SurfaceAttributes.Material(sample, table);
        Assert.Equal("red", table.Materials[vertex.First].Name);
        Assert.Equal(1 - redWeight, vertex.Blend, 5);
        Assert.Equal(new Vector3(redWeight, 0, 1 - redWeight), vertex.BaseColor);
    }

    [Fact]
    public void ColorsAndUnknownNames_BecomeTheirOwnMaterials()
    {
        var table = new MaterialTable(Library);
        var color = SurfaceAttributes.Material(Sample("Color color=(0.2, 0.4, 0.6)\n    Sphere radius=1", new Vector3(1, 0, 0)), table);
        var missing = SurfaceAttributes.Material(Sample("Material material=nope\n    Sphere radius=1", new Vector3(1, 0, 0)), table);
        var plain = SurfaceAttributes.Material(Sample("Sphere radius=1", new Vector3(1, 0, 0)), table);

        Assert.Equal(MaterialDefinition.FromColor(new V3(0.2f, 0.4f, 0.6f)), table.Materials[color.First].Definition);
        Assert.Equal("color(0.2, 0.4, 0.6)", table.Materials[color.First].Name);
        Assert.Equal(MaterialDefinition.Missing, table.Materials[missing.First].Definition);
        Assert.Equal(MaterialDefinition.Default, table.Materials[plain.First].Definition);
        Assert.Equal("default", table.Materials[plain.First].Name);
    }

    [Fact]
    public void Thickness_IsThinForAPlateAndCappedForABlock()
    {
        var plate = Distance(Read("Box size=(0.5, 0.01, 0.5)"));
        var block = Distance(Read("Box size=(0.5, 0.5, 0.5)"));

        Assert.Equal(0.02f, SurfaceAttributes.Thickness(plate, new Vector3(0, 0.01f, 0), Vector3.UnitY, 0.25f, 0.01f), 3);
        Assert.Equal(0.25f, SurfaceAttributes.Thickness(block, new Vector3(0, 0.5f, 0), Vector3.UnitY, 0.25f, 0.01f));
    }

    [Fact]
    public void Occlusion_IsOpenOnAnOutsideAndClosedInACorner()
    {
        var sdf = Distance(Read("Union\n    Box size=(1, 0.1, 1)\n    Translate translation=(0, 0.5, -0.6)\n        Box size=(1, 0.5, 0.1)"));

        Assert.Equal(1, SurfaceAttributes.Occlusion(sdf, new Vector3(0, 0.1f, 0.8f), Vector3.UnitY));
        Assert.True(SurfaceAttributes.Occlusion(sdf, new Vector3(0, 0.1f, -0.48f), Vector3.UnitY) < 0.7f);
    }

    [Fact]
    public void Mix_WeighsEveryParameterLikeTheRenderer()
    {
        var wet = MaterialDefinition.Default with { roughness = 0.2f, wetness = 1, variationColor = new V3(1, 0, 0), variationScale = 0.4f };
        var dry = MaterialDefinition.Default with { roughness = 0.6f, wetness = 0 };

        var mix = SurfaceAttributes.Mix([(wet, 0.25f), (dry, 0.75f)]);

        Assert.Equal(0.5f, mix.roughness, 5);
        Assert.Equal(0.25f, mix.wetness, 5);
        Assert.Equal(new V3(0.25f, 0, 0), mix.variationColor);
        Assert.Equal(0.1f, mix.variationScale, 5);
        Assert.Same(wet, SurfaceAttributes.Mix([(wet, 1)]));
    }

    [Fact]
    public void Albedo_BakesVariationEdgeColourAndWetnessIntoTheBaseColour()
    {
        var plain = MaterialDefinition.Default with { colorVariation = 0 };
        var local = new Vector3(0.13f, 0.71f, -0.4f);
        var baseColor = new Vector3(plain.baseColor.x, plain.baseColor.y, plain.baseColor.z);

        Assert.Equal(baseColor, SurfaceAttributes.Albedo(plain, local, 0.1f));
        Assert.Equal(baseColor * 0.9f, SurfaceAttributes.Albedo(plain with { wetness = 0.5f }, local, 0.1f));
        var edged = plain with { edgeColor = new V3(0, 1, 0), edgeThickness = 0.1f };
        Assert.Equal(new Vector3(0, 1, 0), SurfaceAttributes.Albedo(edged, local, 0));
        Assert.Equal(baseColor, SurfaceAttributes.Albedo(edged, local, 0.2f));

        // Variation moves the colour around but keeps it a colour; the noise is the renderer's, 0 to 1
        var varied = Enumerable.Range(0, 50).Select(i => SurfaceAttributes.Albedo(plain with { colorVariation = 0.3f }, local * i, 0.1f)).ToArray();
        Assert.Contains(varied, c => c != baseColor);
        Assert.All(varied, c => Assert.True(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1));
        Assert.All(Enumerable.Range(0, 200), i => Assert.InRange(SurfaceAttributes.ShadeDetail(new Vector3(i * 0.37f, i * -1.3f, i * 0.11f)), 0, 1));
    }

    [Fact]
    public void Speckles_CoverAboutTheirDensityAndAreOffByDefault()
    {
        var speckled = MaterialDefinition.Default with
        {
            colorVariation = 0, speckleColor = new V3(0, 0, 0), speckleScale = 0.02f, speckleDensity = 0.5f, speckleSize = 0.3f,
        };
        var points = Enumerable.Range(0, 4000).Select(i => new Vector3(i * 0.0137f, MathF.Sin(i) * 0.3f, i * -0.0071f)).ToArray();

        // Each fleck covers a small round part of its cell, so the surface is mostly the base colour with some dark flecks
        var covered = points.Count(p => SurfaceAttributes.Speckle(p, 0.02f, 0.5f, 0.3f).Weight > 0.5f) / (float)points.Length;
        Assert.InRange(covered, 0.03f, 0.4f);
        Assert.Contains(points, p => SurfaceAttributes.VariedColor(speckled, p).X < 0.1f);
        Assert.All(points.Take(200), p => Assert.Equal(0, SurfaceAttributes.Speckle(p, 0.02f, 0, 0.3f).Weight));
        Assert.Equal(0, MaterialDefinition.Default.speckleScale);
    }

    [Fact]
    public void Albedo_AveragesSpecklesSmallerThanTheVertexSpacing()
    {
        var speckled = MaterialDefinition.Default with
        {
            colorVariation = 0, speckleColor = new V3(0, 0, 0), speckleScale = 0.01f, speckleDensity = 0.6f, speckleSize = 0.35f,
        };
        var points = Enumerable.Range(0, 300).Select(i => new Vector3(i * 0.0213f, MathF.Cos(i) * 0.2f, i * 0.0117f)).ToArray();

        // Sampled at a point a vertex is either all fleck or all base colour; averaged over 5 cm it is a steady, slightly darker tint
        var sampled = points.Select(p => SurfaceAttributes.Albedo(speckled, p, 1).X).ToArray();
        var filtered = points.Select(p => SurfaceAttributes.Albedo(speckled, p, 1, 0.05f).X).ToArray();
        Assert.True(Spread(filtered) < Spread(sampled) / 2);
        Assert.True(filtered.Average() < speckled.baseColor.x);
    }

    private static float Spread(float[] values)
    {
        var mean = values.Average();
        return MathF.Sqrt(values.Average(v => (v - mean) * (v - mean)));
    }
}
