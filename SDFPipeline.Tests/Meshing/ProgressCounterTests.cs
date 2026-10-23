using SDFPipeline.Meshing;

namespace SDFPipeline.Tests.Meshing;

public class ProgressCounterTests
{
    [Fact]
    public void LinesCountUpAgainstTheTotal()
    {
        var progress = new ProgressCounter(32);
        for (var i = 1; i < 12; i++)
            progress.Finish("meshed", "a");
        Assert.Equal("[ 37%] 12/32 meshed: castle0 (2 LODs)", progress.Finish("meshed", "castle0 (2 LODs)"));
    }

    [Fact]
    public void RoundsDownSoOnlyTheLastItemReachesOneHundredPercent()
    {
        Assert.Equal("[ 99%] 199/200 meshed: x", ProgressCounter.Line(199, 200, "meshed", "x"));
        Assert.Equal("[100%] 200/200 meshed: x", ProgressCounter.Line(200, 200, "meshed", "x"));
        Assert.Equal("[  3%]  1/30 baked: x", ProgressCounter.Line(1, 30, "baked", "x"));
    }

    [Fact]
    public void ParallelFinishesAreAllCounted()
    {
        var progress = new ProgressCounter(1000);
        var lines = new string[1000];
        Parallel.For(0, 1000, i => lines[i] = progress.Finish("meshed", "m"));
        Assert.Equal(1000, lines.Distinct().Count());
        Assert.Contains("[100%] 1000/1000 meshed: m", lines);
    }
}
