using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>Ticket 27.3: the level file's <c>mover</c> section, the stadium path, and what the bake writes for Unity's PathMover.</summary>
public class MoverTests
{
    private const string Header = "format 1\nplay size=(150, 100) noise=0\n";

    private const string Train =
        "mover train speed=7 gap=0.6\n" +
        "    Stadium center=(0, 0) halfLength=12 radius=20\n" +
        "    heights keys=[(0, 0), (6, 3.8), (18, 3.8), (24, 0)]\n" +
        "    car toy-train-engine length=4.8\n" +
        "    car toy-train-flat length=4.2\n" +
        "    car toy-train-wagon length=4.2\n";

    [Fact]
    public void Read_TakesAMover_WithAStadiumHeightsAndCars()
    {
        var level = LevelFile.Read(Header + Train);

        var mover = Assert.Single(level.Movers);
        Assert.Equal("train", mover.Name);
        Assert.Equal(7f, mover.Speed);
        Assert.Equal(0.6f, mover.Gap);
        Assert.Equal(0f, mover.Phase);
        Assert.Equal(["toy-train-engine", "toy-train-flat", "toy-train-wagon"], mover.Cars.Select(c => c.Model));
        Assert.Equal(new Vector2(6, 3.8f), mover.Heights[1]);
        // Two straights of 24 m and two half circles of radius 20
        Assert.Equal(48 + 2 * MathF.PI * 20, mover.Length, 0.5f);
        Assert.Equal(mover.Length / 7, mover.LapSeconds, 1e-3f);
        Assert.Equal(4.8f + 4.2f + 4.2f + 1.2f, mover.TrainLength, 1e-4f);
    }

    [Fact]
    public void ALevelWithoutAMover_HasNone() => Assert.Empty(LevelFile.Read(Header).Movers);

    [Fact]
    public void Stadium_RunsAnticlockwise_FromTheEastStraightsSouthEnd()
    {
        var path = MoverSpec.Stadium(new Vector2(0, 0), 12, 20);

        Assert.Equal(new Vector2(20, -12), path[0]);
        Assert.Equal(new Vector2(20, 12), path[1]);
        // Signed area is positive going anticlockwise (x east, z north)
        var area = 0f;
        for (var i = 0; i < path.Count; i++)
            area += path[i].X * path[(i + 1) % path.Count].Y - path[(i + 1) % path.Count].X * path[i].Y;
        Assert.True(area > 0);
        Assert.Contains(path, p => Vector2.Distance(p, new Vector2(-20, 12)) < 1e-3f);
        Assert.Contains(path, p => Vector2.Distance(p, new Vector2(-20, -12)) < 1e-3f);
        Assert.All(path, p => Assert.InRange(p.X, -20.001f, 20.001f));
    }

    [Fact]
    public void Stadium_WithNoStraights_IsACircle()
    {
        var path = MoverSpec.Stadium(new Vector2(5, 5), 0, 10);

        Assert.Equal(2 * MathF.PI * 10, MoverSpec.PathLength(path), 0.3f);
        Assert.Equal(path.Count, path.Distinct().Count());
    }

    [Fact]
    public void Read_TakesAPolylineLoop_AndAPhase()
    {
        var level = LevelFile.Read(Header +
            "mover cart speed=2 phase=5\n    Polyline points=[(-10, 0), (0, 8), (10, 0), (0, -8)]\n    car cart0 length=3\n");

        var mover = Assert.Single(level.Movers);
        Assert.Equal(4, mover.Path.Count);
        Assert.Equal(5f, mover.Phase);
        Assert.Empty(mover.Heights);
        Assert.Equal(4 * MathF.Sqrt(10 * 10 + 8 * 8), mover.Length, 1e-3f);
    }

    [Fact]
    public void Read_AllowsSeveralMovers_ButNotTheSameNameTwice()
    {
        var other = Train.Replace("mover train", "mover tram");
        Assert.Equal(2, LevelFile.Read(Header + Train + other).Movers.Count);

        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Train + Train));
        Assert.Contains("given twice", exception.Problem);
    }

    [Theory]
    [InlineData("mover t speed=7\n    car a length=2\n", "needs a path")]
    [InlineData("mover t speed=7\n    Stadium halfLength=12 radius=20\n", "needs one or more cars")]
    [InlineData("mover t\n    Stadium halfLength=12 radius=20\n    car a length=2\n", "missing parameter 'speed'")]
    [InlineData("mover t speed=7\n    Stadium halfLength=12 radius=20\n    Polyline points=[(0, 0), (1, 0), (1, 1)]\n    car a length=2\n", "path twice")]
    [InlineData("mover t speed=7\n    Polyline points=[(0, 0), (1, 0)]\n    car a length=2\n", "three corners")]
    [InlineData("mover t speed=7\n    Stadium halfLength=12 radius=20\n    car a\n", "missing parameter 'length'")]
    [InlineData("mover t speed=7\n    Stadium halfLength=12 radius=20\n    wheel a\n", "no 'wheel'")]
    [InlineData("mover t speed=7\n    Stadium halfLength=12 radius=20\n    heights keys=[(0, 0), (500, 1)]\n    car a length=2\n", "off the path")]
    [InlineData("mover t speed=7\n    Stadium halfLength=12 radius=20\n    heights keys=[(10, 0), (5, 1)]\n    car a length=2\n", "go along the path")]
    [InlineData("mover t speed=7\n    Stadium halfLength=0 radius=3\n    car a length=19\n", "meet itself")]
    [InlineData("mover t speed=0\n    Stadium halfLength=12 radius=20\n    car a length=2\n", "speed")]
    public void Read_RejectsAMoverThatIsNotOne(string mover, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + mover));

        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Bake_WritesTheMover_AndNothingForALevelWithout()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-mover-");
        try
        {
            var level = LevelFile.Read(Header + "hills\n    ring width=12 height=6\nedge\n    margin=4\n" + Train);
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

            JsonElement Bake(LevelDefinition l, Func<string, bool>? exists = null)
            {
                LevelBaker.Bake(l, "mover", materials, root.FullName, modelExists: exists ?? (_ => true));
                using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
                return json.RootElement.Clone();
            }

            var without = Bake(level with { Movers = [] });
            Assert.False(without.TryGetProperty("movers", out _));

            var baked = Bake(level);
            var mover = baked.GetProperty("movers")[0];
            Assert.Equal("train", mover.GetProperty("name").GetString());
            Assert.Equal(7f, mover.GetProperty("speed").GetSingle());
            Assert.Equal(0.6f, mover.GetProperty("gap").GetSingle());
            Assert.Equal(level.Movers[0].Length, mover.GetProperty("length").GetSingle(), 1e-2f);
            Assert.Equal(level.Movers[0].Path.Count, mover.GetProperty("path").GetArrayLength());
            Assert.Equal(20f, mover.GetProperty("path")[0][0].GetSingle());
            Assert.Equal(-12f, mover.GetProperty("path")[0][1].GetSingle());
            Assert.Equal(4, mover.GetProperty("heights").GetArrayLength());
            var cars = mover.GetProperty("cars");
            Assert.Equal(3, cars.GetArrayLength());
            Assert.Equal("toy-train-engine", cars[0].GetProperty("model").GetString());
            Assert.Equal(4.8f, cars[0].GetProperty("length").GetSingle());
            Assert.Contains(baked.GetProperty("models").EnumerateArray().Select(m => m.GetString()), m => m == "toy-train-wagon");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_RejectsACarWithoutAModel_AndAPathOffTheIsland()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-mover-");
        try
        {
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var level = LevelFile.Read(Header + Train);

            var missing = Assert.Throws<SdfProgramFileException>(() => { LevelBaker.Bake(level, "mover", materials, root.FullName, modelExists: name => name != "toy-train-flat"); });
            Assert.Contains("toy-train-flat", missing.Problem);

            var wide = LevelFile.Read(Header + Train.Replace("radius=20", "radius=200"));
            var off = Assert.Throws<SdfProgramFileException>(() => { LevelBaker.Bake(wide, "mover", materials, root.FullName, modelExists: _ => true); });
            Assert.Contains("beyond the island", off.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
