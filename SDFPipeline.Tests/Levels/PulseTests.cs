using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>Ticket 30.9: the <c>pulse</c> flag on a region polyline, the <c>pulses</c> line, and what the bake writes for Unity's overlay and PulseRules.</summary>
public class PulseTests
{
    private const string Header = "format 1\nplay size=(150, 100) noise=0\n";

    private const string Regions =
        "regions base=pcb\n" +
        "    trace wobble=0\n" +
        "        Polyline points=[(-40, 0), (-20, 0), (-10, 10)] width=0.8 pulse\n" +
        "        Polyline points=[(-40, 2), (-20, 2)] width=0.8\n" +
        "        Polyline points=[(40, 0), (20, 0)] width=0.8 pulse=on\n";

    private const string Pulses = "pulses every=4 speed=18 length=3 boost=1.35 for=2.5\n";

    [Fact]
    public void Read_TakesTheFlaggedPolylines_AndThePulsesLine()
    {
        var level = LevelFile.Read(Header + Regions + Pulses);

        var pulses = level.Pulses!;
        Assert.Equal((4f, 18f, 3f, 1.35f, 2.5f), (pulses.Every, pulses.Speed, pulses.Length, pulses.Boost, pulses.For));
        Assert.Equal(72f, pulses.Period);
        Assert.Equal(2, level.PulseLines.Count());
        Assert.Equal(new Vector2(-40, 0), level.PulseLines.First().Points[0]);
        Assert.False(level.Regions[0].Shapes.OfType<PolylineRegion>().ElementAt(1).Pulse);
    }

    [Fact]
    public void Read_GivesThePulsesLineItsDefaults()
    {
        var pulses = LevelFile.Read(Header + Regions + "pulses\n").Pulses!;

        Assert.Equal((4f, 18f, 3f, 1.35f, 2.5f), (pulses.Every, pulses.Speed, pulses.Length, pulses.Boost, pulses.For));
    }

    [Fact]
    public void ALevelWithoutPulses_HasNone()
    {
        var level = LevelFile.Read(Header + Regions.Replace(" pulse=on", "").Replace(" pulse", ""));

        Assert.Null(level.Pulses);
        Assert.Empty(level.PulseLines);
    }

    [Theory]
    [InlineData("pulses every=4 speed=18 length=3 boost=1.35 for=2.5 wind=3\n", "no parameter 'wind'")]
    [InlineData("pulses every=0\n", "'every'")]
    [InlineData("pulses boost=0.5\n", "'boost'")]
    [InlineData("pulses length=70\n", "fill the line")]
    [InlineData(Pulses + Pulses, "given twice")]
    public void Read_RejectsAPulsesLineThatIsNotOne(string line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Regions + line));

        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_RejectsAFlagWithoutALine_AndALineWithoutAFlag()
    {
        var flagOnly = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Regions));
        Assert.Contains("needs a 'pulses' line", flagOnly.Problem);

        var lineOnly = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Regions.Replace(" pulse=on", "").Replace(" pulse", "") + Pulses));
        Assert.Contains("no region polyline has the 'pulse' flag", lineOnly.Problem);
        Assert.Equal(Header.Count(c => c == '\n') + Regions.Count(c => c == '\n') + 1, lineOnly.Line);
    }

    [Fact]
    public void Read_RejectsANoisyPulseLine_AndAFlagOnTheWrongShape()
    {
        var noisy = Assert.Throws<SdfProgramFileException>(() =>
            LevelFile.Read(Header + Regions.Replace("(-10, 10)] width=0.8 pulse", "(-10, 10)] width=0.8 noise=0.5 pulse") + Pulses));
        Assert.Contains("noise=0", noisy.Problem);

        var circle = Assert.Throws<SdfProgramFileException>(() =>
            LevelFile.Read(Header + "regions base=pcb\n    pad\n        Circle center=(0, 0) radius=2 pulse\n" + Pulses));
        Assert.Contains("no parameter 'pulse'", circle.Problem);

        var flagged = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Regions.Replace("pulse=on", "pulse=3") + Pulses));
        Assert.Contains("flag", flagged.Problem);
    }

    [Fact]
    public void Bake_WritesThePulses_AndNothingForALevelWithout()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-pulses-");
        try
        {
            var level = LevelFile.Read(Header + "hills\n    ring width=12 height=6\nedge\n    margin=4\n" + Regions + Pulses);
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default).With("ground_pcb", MaterialDefinition.Default)
                .With("ground_trace", MaterialDefinition.Default);

            JsonElement Bake(LevelDefinition l)
            {
                LevelBaker.Bake(l, "pulses", materials, root.FullName, modelExists: _ => true);
                using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
                return json.RootElement.Clone();
            }

            var baked = Bake(level).GetProperty("pulses");
            Assert.Equal(4f, baked.GetProperty("every").GetSingle());
            Assert.Equal(18f, baked.GetProperty("speed").GetSingle());
            Assert.Equal(3f, baked.GetProperty("length").GetSingle());
            Assert.Equal(1.35f, baked.GetProperty("boost").GetSingle());
            Assert.Equal(2.5f, baked.GetProperty("for").GetSingle());
            var lines = baked.GetProperty("lines");
            Assert.Equal(2, lines.GetArrayLength());
            Assert.Equal(0.8f, lines[0].GetProperty("width").GetSingle());
            Assert.Equal(20f + MathF.Sqrt(200), lines[0].GetProperty("length").GetSingle(), 1e-3f);
            Assert.Equal(3, lines[0].GetProperty("points").GetArrayLength());
            Assert.Equal(-40f, lines[0].GetProperty("points")[0][0].GetSingle());
            Assert.Equal(3, lines[0].GetProperty("y").GetArrayLength());
            Assert.Equal(40f, lines[1].GetProperty("points")[0][0].GetSingle());

            // Other levels bake unchanged: no pulses key
            var without = LevelFile.Read(Header + "hills\n    ring width=12 height=6\nedge\n    margin=4\n" + Regions.Replace(" pulse=on", "").Replace(" pulse", ""));
            Assert.False(Bake(without).TryGetProperty("pulses", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_RejectsAPulseLineOffTheIsland()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-pulses-");
        try
        {
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default).With("ground_pcb", MaterialDefinition.Default)
                .With("ground_trace", MaterialDefinition.Default);
            var level = LevelFile.Read(Header + Regions.Replace("(-10, 10)", "(-10, 400)") + Pulses);

            var off = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(level, "pulses", materials, root.FullName, modelExists: _ => true));
            Assert.Contains("beyond the island", off.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ContentComputer_PulsesRunFromEachBaseToTheMiddle()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "computer" + LevelFile.Extension));

        Assert.Equal((4f, 18f, 3f, 1.35f, 2.5f), (level.Pulses!.Every, level.Pulses.Speed, level.Pulses.Length, level.Pulses.Boost, level.Pulses.For));
        // Every trace of the three buses a side pulses, and nothing else does
        var traces = level.Regions.Single(r => r.Name == "trace").Shapes.OfType<PolylineRegion>().ToArray();
        Assert.Equal(18, traces.Length);
        Assert.All(traces, t => Assert.True(t.Pulse));
        Assert.Equal(18, level.PulseLines.Count());
        // West ones start at the west RAM end and run east, east ones the mirror: they end nearer the middle than they start
        foreach (var line in level.PulseLines)
        {
            var (first, last) = (line.Points[0], line.Points[^1]);
            Assert.True(MathF.Abs(last.X) < MathF.Abs(first.X), $"A pulse line from {first} to {last} does not run to the middle");
            Assert.Equal(first.X < 0, last.X > first.X);
        }
        // Parallel traces of a bus are as long as each other, so one band crosses all three together
        var lengths = traces.Take(3).Select(t => t.Points.Zip(t.Points.Skip(1), Vector2.Distance).Sum()).ToArray();
        Assert.All(lengths, l => Assert.Equal(lengths[0], l, 0.05f));
    }
}
