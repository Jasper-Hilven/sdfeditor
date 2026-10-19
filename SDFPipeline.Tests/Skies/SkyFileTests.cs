using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFPipeline.Skies;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Skies;

public class SkyFileTests
{
    private const string Night =
        """
        # A night sky
        format 1

        light azimuth=140 elevation=28 color=(0.78, 0.8, 1.0)
        tone brightness=0.75 shadowTint=(0.8, 0.85, 1.1) glowKeep=1
        gradient softness=0.6
            band from=60 color=(0.035, 0.03, 0.11)
            band from=14 color=(0.14, 0.09, 0.27)
            band from=-6 color=(0.26, 0.28, 0.38)
        abyss
            band from=-6 color=(0.12, 0.08, 0.2)
            band from=-30 color=(0.03, 0.02, 0.06)
            glow elevation=-55 width=12 color=(0.05, 0.25, 0.28)
        stars density=0.6 size=1.6 twinkle=0.3 tint=0.25 minElevation=8
        fade start=25 end=180 amount=0.55
        moon model=eye-moon size=9 glow=0.6
            turn amount=12 period=40
            blink every=(9, 25) duration=0.35
        floaters model=floating-cap0 count=4 distance=(250, 400) elevation=(-10, 18) scale=8 drift=0.6 bob=1.5
        spores
            abyss count=60 color=(0.3, 0.95, 0.85) altColor=(1.0, 0.7, 0.3) rise=1.2 size=0.25 from=-80 to=20
            near count=25 radius=35 color=(0.55, 0.95, 0.75) size=0.06 drift=0.3
        post exposure=-0.1 bloom=0.25
        """;

    private const string Day =
        """
        format 1
        light azimuth=38.7 elevation=32 color=(1, 0.88, 0.74)
        tone brightness=1 shadowTint=(1, 1, 1) glowKeep=0
        gradient softness=8
            band from=24 color=(0.26, 0.42, 0.74)
            band from=-4 color=(0.56, 0.66, 0.8)
        post exposure=-0.26 bloom=0.175
        """;

    private static readonly Func<string, string?> Presets = name => name switch { "night" => Night, "day" => Day, _ => null };

    private static SkyDefinition Resolve(string level) => SkyFile.Resolve(LevelFile.Read(level).Sky, Presets).Definition;

    [Fact]
    public void Read_TakesEveryLine()
    {
        var sky = SkyFile.Read(Night);

        Assert.Equal(new SkyLight(140, 28, new Vector3(0.78f, 0.8f, 1)), sky.Light);
        Assert.Equal(new SkyTone(0.75f, new Vector3(0.8f, 0.85f, 1.1f), 1), sky.Tone);
        Assert.Equal(0.6f, sky.Gradient.Softness);
        Assert.Equal([60f, 14, -6], sky.Gradient.Bands.Select(b => b.From));
        Assert.Equal([-6f, -30], sky.Abyss!.Bands.Select(b => b.From));
        Assert.Equal(new SkyGlow(-55, 12, new Vector3(0.05f, 0.25f, 0.28f)), sky.Abyss.Glow);
        Assert.Equal(new SkyStars(0.6f, 1.6f, 0.3f, 0.25f, 8), sky.Stars);
        Assert.Equal(new SkyFade(25, 180, 0.55f), sky.Fade);
        Assert.Equal(new SkyMoon("eye-moon", 9, 0.6f, new SkyTurn(12, 40), new SkyBlink(new Vector2(9, 25), 0.35f)), sky.Moon);
        Assert.Equal(new SkyFloaters("floating-cap0", 4, new Vector2(250, 400), new Vector2(-10, 18), 8, 0.6f, 1.5f), sky.Floaters);
        Assert.Equal(60, sky.Spores!.Abyss!.Count);
        Assert.Equal(35, sky.Spores.Near!.Radius);
        Assert.Equal(new SkyPost(-0.1f, 0.25f), sky.Post);
        Assert.Equal(["eye-moon", "floating-cap0"], sky.Models);
    }

    [Fact]
    public void Read_LeavesOutTheOptionalLines()
    {
        var sky = SkyFile.Read(Day);

        Assert.Equal(SkyTone.Identity, sky.Tone);
        Assert.Null(sky.Abyss);
        Assert.Null(sky.Stars);
        Assert.Null(sky.Fade);
        Assert.Null(sky.Moon);
        Assert.Null(sky.Floaters);
        Assert.Null(sky.Spores);
        Assert.Empty(sky.Models);
    }

    [Fact]
    public void Read_TakesParametersIndentedBelowTheirLine()
    {
        var sky = SkyFile.Read(Day.Replace("tone brightness=1 shadowTint=(1, 1, 1) glowKeep=0", "tone\n    brightness=0.9\n    shadowTint=(1, 1, 1) glowKeep=0"));

        Assert.Equal(0.9f, sky.Tone.Brightness);
    }

    [Theory]
    [InlineData("format 2\n", 1, "only format 1")]
    [InlineData("format 1\nlight azimuth=1 elevation=2 color=(1, 1, 1)\nsun azimuth=1", 3, "no 'sun'")]
    [InlineData("format 1\ntone brightness=1 shadowTint=(1, 1, 1) glowKeep=0 shine=2", 2, "no parameter 'shine'")]
    [InlineData("format 1\ntone brightness=1 brightness=1", 2, "given twice")]
    [InlineData("format 1\npost exposure=0 bloom=0\npost exposure=0 bloom=0", 3, "given twice, first on line 2")]
    [InlineData("format 1\nmoon model=eye-moon size=9 glow=0\n    turn amount=1 period=1\n    turn amount=1 period=1", 4, "given twice")]
    [InlineData("format 1\nlight azimuth=1 elevation=2 color=(1, 1, 1)\n    band from=1 color=(1, 1, 1)", 3, "Only parameters")]
    [InlineData("format 1\ngradient softness=1\n    band from=1 color=(1, 1, 1) replace=true", 3, "no parameter 'replace'")]
    public void Read_RejectsWhatIsNotASky_OnItsLine(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => SkyFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Theory]
    [InlineData("tone brightness=1 shadowTint=(1, 1, 1) glowKeep=0", "tone brightness=7 shadowTint=(1, 1, 1) glowKeep=0", 3, "'brightness' of the sky's tone is 7")]
    [InlineData("tone brightness=1 shadowTint=(1, 1, 1) glowKeep=0", "tone brightness=1 shadowTint=(1, 1) glowKeep=0", 3, "'shadowTint'")]
    [InlineData("tone brightness=1 shadowTint=(1, 1, 1) glowKeep=0", "tone brightness=1 shadowTint=(1, 1, 1)", 3, "missing parameter 'glowKeep'")]
    [InlineData("    band from=-4 color=(0.56, 0.66, 0.8)", "    band from=30 color=(0.56, 0.66, 0.8)", 6, "from the highest down")]
    [InlineData("post exposure=-0.26 bloom=0.175", "", 6, "needs a 'post' line")]
    public void Read_RejectsBadValues_OnTheirLine(string from, string to, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => SkyFile.Read(Day.Replace(from, to)));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_RejectsMoreBandsThanTheShadersTake()
    {
        var bands = string.Concat(Enumerable.Range(0, 9).Select(i => $"    band from={80 - 10 * i} color=(1, 1, 1)\n"));
        var exception = Assert.Throws<SdfProgramFileException>(() => SkyFile.Read(Day.Replace("gradient softness=8\n", $"gradient softness=8\n{bands}").Replace("    band from=24", "#").Replace("    band from=-4", "#")));

        Assert.Contains($"at most {SkyFile.MaxBands} bands", exception.Problem);
    }

    [Fact]
    public void Resolve_WithoutASkySection_TakesTheDayPreset()
    {
        var resolved = SkyFile.Resolve(LevelFile.Read("format 1\nplay size=(40, 30)").Sky, Presets);

        Assert.Equal("day", resolved.Preset);
        Assert.Equal(SkyFile.Read(Day), resolved.Definition, new DefinitionComparer());
        Assert.Equal(16, resolved.Hash.Length);
    }

    [Fact]
    public void Resolve_MergesOverridesPerKey()
    {
        var sky = Resolve("format 1\nplay size=(40, 30)\nsky preset=night\n    fade end=200\n    moon size=10\n    tone brightness=0.8");

        Assert.Equal(new SkyFade(25, 200, 0.55f), sky.Fade);
        Assert.Equal(10, sky.Moon!.Size);
        Assert.Equal(0.6f, sky.Moon.Glow);
        Assert.Equal(new SkyTurn(12, 40), sky.Moon.Turn);
        Assert.Equal(new SkyTone(0.8f, new Vector3(0.8f, 0.85f, 1.1f), 1), sky.Tone);
    }

    [Fact]
    public void Resolve_MergesEntriesByNameAndIndex()
    {
        var sky = Resolve(
            """
            format 1
            play size=(40, 30)
            sky preset=night
                gradient
                    band color=(1, 0, 0)
                    band from=20
                moon
                    blink duration=0.5
            """);

        Assert.Equal([new SkyBand(60, new Vector3(1, 0, 0)), new SkyBand(20, new Vector3(0.14f, 0.09f, 0.27f)), new SkyBand(-6, new Vector3(0.26f, 0.28f, 0.38f))],
            sky.Gradient.Bands);
        Assert.Equal(new SkyBlink(new Vector2(9, 25), 0.5f), sky.Moon!.Blink);
    }

    [Fact]
    public void Resolve_ReplaceDropsThePresetsEntries()
    {
        var sky = Resolve("format 1\nplay size=(40, 30)\nsky preset=night\n    gradient replace=true softness=2\n        band from=0 color=(0.5, 0.5, 0.5)");

        Assert.Equal(2, sky.Gradient.Softness);
        Assert.Equal([new SkyBand(0, new Vector3(0.5f))], sky.Gradient.Bands);
    }

    [Fact]
    public void Resolve_AddsALineThePresetLacks_WhichMustBeComplete()
    {
        var sky = Resolve("format 1\nplay size=(40, 30)\nsky preset=day\n    fade start=10 end=50 amount=0.3");
        Assert.Equal(new SkyFade(10, 50, 0.3f), sky.Fade);

        var exception = Assert.Throws<SdfProgramFileException>(() => Resolve("format 1\nplay size=(40, 30)\nsky preset=day\n    fade end=50"));
        Assert.Equal(4, exception.Line);
        Assert.Contains("missing parameter 'start'", exception.Problem);
    }

    [Fact]
    public void Resolve_NamesTheLevelsLine_ForAProblemInTheLevel()
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => Resolve("format 1\nplay size=(40, 30)\nsky preset=night\n    moon size=900"));

        Assert.Equal(4, exception.Line);
        Assert.Contains("'size' of the sky's moon is 900", exception.Problem);
    }

    [Fact]
    public void Resolve_NamesThePresetsFileAndLine_ForAProblemInThePreset()
    {
        var broken = Night.Replace("glowKeep=1", "glowKeep=3");
        var exception = Assert.Throws<SdfProgramFileException>(() =>
            SkyFile.Resolve(LevelFile.Read("format 1\nplay size=(40, 30)\nsky preset=night").Sky, _ => broken));

        Assert.Equal(3, exception.Line);
        Assert.StartsWith("content/skies/night.sky:5: ", exception.Problem);
    }

    [Theory]
    [InlineData("format 1\nplay size=(40, 30)\nsky preset=dusk", 3, "no sky preset 'dusk'")]
    [InlineData("format 1\nplay size=(40, 30)\nsky", 3, "needs a preset")]
    [InlineData("format 1\nplay size=(40, 30)\nsky preset=night\n    fade end=200\n    fade end=210", 5, "overridden twice")]
    [InlineData("format 1\nplay size=(40, 30)\nsky preset=night\n    weather rain=1", 4, "no 'weather'")]
    [InlineData("format 1\nplay size=(40, 30)\nsky preset=night\n    fade far=200", 4, "no parameter 'far'")]
    public void Resolve_RejectsABadSection_OnItsLine(string level, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => Resolve(level));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Bands_JoinTheGradientAndTheAbyss_FromTheZenithDown()
    {
        var bands = SkyDocument.Bands(SkyFile.Read(Night));

        Assert.Equal([60f, 14, -6, -30, -90], bands.Select(b => b.From));
        Assert.Equal(new Vector3(0.12f, 0.08f, 0.2f), bands[3].Color);
        Assert.Equal([24f, -90], SkyDocument.Bands(SkyFile.Read(Day)).Select(b => b.From));
    }

    [Fact]
    public void Bake_WritesTheSkyAndItsModels_ByteIdenticalEveryTime()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-sky-");
        try
        {
            var level = LevelFile.Read("format 1\nplay size=(40, 30)\nhills\n    ring width=8 height=3\nedge margin=2\nsky preset=night\n    fade end=200");
            var sky = SkyFile.Resolve(level.Sky, Presets);
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            ModelFootprint? Models(string name) => new ModelFootprint(new Vector2(-1, -1), new Vector2(1, 1));
            LevelBaker.Bake(level, "small", materials, Path.Combine(root.FullName, "a"), Models, sky: sky);
            LevelBaker.Bake(level, "small", materials, Path.Combine(root.FullName, "b"), Models, sky: sky);

            var json = File.ReadAllBytes(Path.Combine(root.FullName, "a", LevelBaker.LevelJson));
            Assert.Equal(json, File.ReadAllBytes(Path.Combine(root.FullName, "b", LevelBaker.LevelJson)));
            using var document = JsonDocument.Parse(json);
            var written = document.RootElement.GetProperty("sky");
            Assert.Equal(200, written.GetProperty("fade").GetProperty("end").GetSingle());
            Assert.Equal(5, written.GetProperty("bands").GetArrayLength());
            Assert.Equal(4, written.GetProperty("floaters").GetProperty("layout").GetArrayLength());
            Assert.Equal(60, written.GetProperty("spores").GetProperty("abyss").GetProperty("points").GetArrayLength());
            Assert.Equal(5, written.GetProperty("spores").GetProperty("abyss").GetProperty("points")[0].GetArrayLength());
            Assert.Equal(sky.Hash, written.GetProperty("hash").GetString());
            Assert.Equal(["eye-moon", "floating-cap0"], document.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetString()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_NamesTheSkyLine_ForASkyModelThatIsNotThere()
    {
        var level = LevelFile.Read("format 1\nplay size=(40, 30)\nsky preset=night");
        var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

        var exception = Assert.Throws<SdfProgramFileException>(() =>
            LevelBaker.Bake(level, "small", materials, Path.GetTempPath(), _ => null, sky: SkyFile.Resolve(level.Sky, Presets)));

        Assert.Equal(3, exception.Line);
        Assert.Contains("eye-moon", exception.Problem);
    }

    [Fact]
    public void Layout_PutsTheAbyssSporesJustOutsideTheIslandsEdge()
    {
        var sky = SkyFile.Read(Night);
        var layout = SkyLayout.Of(sky, 1, new Vector2(20, 15), 10);

        Assert.Equal(60, layout.AbyssSpores!.Count);
        foreach (var spore in layout.AbyssSpores)
        {
            var (x, z) = (spore.Values[0], spore.Values[1]);
            var outside = MathF.Sqrt(MathF.Pow(MathF.Max(MathF.Abs(x) - 20, 0), 2) + MathF.Pow(MathF.Max(MathF.Abs(z) - 15, 0), 2)) - 10;
            Assert.InRange(outside, SkyLayout.SporeReach.X - 0.01f, SkyLayout.SporeReach.Y + 0.01f);
        }
        Assert.Equal(layout, SkyLayout.Of(sky, 1, new Vector2(20, 15), 10), new LayoutComparer());
    }

    /// <summary>Records with lists compare the lists by reference; these compare what they hold.</summary>
    private sealed class DefinitionComparer : IEqualityComparer<SkyDefinition>
    {
        public bool Equals(SkyDefinition? a, SkyDefinition? b) =>
            JsonSerializer.Serialize(SkyDocument.Of(a!)) == JsonSerializer.Serialize(SkyDocument.Of(b!));

        public int GetHashCode(SkyDefinition sky) => 0;
    }

    private sealed class LayoutComparer : IEqualityComparer<SkyLayout>
    {
        public bool Equals(SkyLayout? a, SkyLayout? b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

        public int GetHashCode(SkyLayout layout) => 0;
    }
}
