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

    private const string Behaving =
        "mover train speed=7 gap=0.6 pushes=3 damage=20 name=\"Toy train\" cooldown=1.5 puff=1 puffAt=(0, 2.7, 1.7) whistle=2 crossing=25\n" +
        "    Stadium center=(0, 0) halfLength=12 radius=20\n" +
        "    car toy-train-engine length=4.8\n";

    [Fact]
    public void Read_TakesTheMoversBehaviour_PushDamageNamePuffsAndCrossing()
    {
        var mover = Assert.Single(LevelFile.Read(Header + Behaving).Movers);

        Assert.Equal(3f, mover.Pushes);
        Assert.Equal(20f, mover.Damage);
        Assert.Equal("Toy train", mover.HazardName);
        Assert.Equal(1.5f, mover.Cooldown);
        Assert.Equal(1f, mover.Puff);
        Assert.Equal(new Vector3(0, 2.7f, 1.7f), mover.PuffAt);
        Assert.Equal(2f, mover.Whistle);
        Assert.Equal(25f, mover.Crossing);
    }

    [Fact]
    public void Read_AMoverWithNoBehaviour_IsHarmless_AndNamedAfterItself()
    {
        var mover = Assert.Single(LevelFile.Read(Header + Train).Movers);

        Assert.Equal(0f, mover.Pushes);
        Assert.Equal(0f, mover.Damage);
        Assert.Equal(1f, mover.Cooldown);
        Assert.Equal(0f, mover.Puff);
        Assert.Equal(0f, mover.Whistle);
        Assert.Equal(0f, mover.Crossing);
        Assert.Equal("Train", mover.HazardName);
    }

    [Theory]
    [InlineData("pushes=-1", "pushes")]
    [InlineData("damage=5000", "damage")]
    [InlineData("puffAt=3", "puffAt")]
    [InlineData("crossing=x", "crossing")]
    [InlineData("hurts=3", "hurts")]
    public void Read_RejectsABadBehaviourOption(string option, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Header + Train.Replace("gap=0.6", "gap=0.6 " + option)));

        Assert.Contains(problem, exception.Problem);
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

            // Harmless by default
            Assert.Equal(0f, mover.GetProperty("pushes").GetSingle());
            Assert.Equal(0f, mover.GetProperty("damage").GetSingle());
            Assert.Equal("Train", mover.GetProperty("hazard").GetString());

            // The behaviour of ticket 27.10
            var behaving = Bake(LevelFile.Read(Header + "hills\n    ring width=12 height=6\nedge\n    margin=4\n" + Behaving)).GetProperty("movers")[0];
            Assert.Equal(3f, behaving.GetProperty("pushes").GetSingle());
            Assert.Equal(20f, behaving.GetProperty("damage").GetSingle());
            Assert.Equal("Toy train", behaving.GetProperty("hazard").GetString());
            Assert.Equal(1.5f, behaving.GetProperty("cooldown").GetSingle());
            Assert.Equal(1f, behaving.GetProperty("puff").GetSingle());
            Assert.Equal(2.7f, behaving.GetProperty("puffAt")[1].GetSingle(), 1e-4f);
            Assert.Equal(2f, behaving.GetProperty("whistle").GetSingle());
            Assert.Equal(25f, behaving.GetProperty("crossing").GetSingle());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>Ticket 27.10: the toy box's train knocks aside 3 m for 20 damage as "Toy train", puffs every second, whistles 2 s before a bridge, and bots wait 25 m ahead.</summary>
    [Fact]
    public void ContentToyBox_TheTrainPushesHurtsPuffsAndMakesBotsWait()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "toybox" + LevelFile.Extension));

        var train = Assert.Single(level.Movers);
        Assert.Equal((3f, 20f, "Toy train"), (train.Pushes, train.Damage, train.HazardName));
        Assert.Equal((1f, 2f, 25f), (train.Puff, train.Whistle, train.Crossing));
        Assert.Equal(1f, train.Cooldown);
        // The puff leaves the chimney, near the engine's front and above its roof
        Assert.True(train.PuffAt.Z > 1 && train.PuffAt.Y > 2.5f);
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
