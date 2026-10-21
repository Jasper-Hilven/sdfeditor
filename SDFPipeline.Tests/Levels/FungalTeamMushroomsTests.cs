using System.Numerics;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Levels;

/// <summary>
/// Ticket 19: the fungal level's team mushrooms, reddish round the west castle and bluish round the east, placed by fungal.level
/// (the castle model is not touched). The two sides mirror each other across x = 0; those with a y stand on the castle, those
/// without on the ground clear of its walls.
/// </summary>
public class FungalTeamMushroomsTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static readonly LevelDefinition Level =
        LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, "fungal" + LevelFile.Extension));

    private static IReadOnlyList<Placement> Team(string colour) =>
        Level.Placements.Where(p => p.Model.StartsWith($"team-{colour}-", StringComparison.Ordinal)).ToList();

    [Fact]
    public void RedStandsWest_BlueEast_EachMirroringTheOther()
    {
        var red = Team("red");
        var blue = Team("blue");

        Assert.NotEmpty(red);
        Assert.All(red, p => Assert.True(p.At.X < 0, $"{p.Model} on line {p.Line} is not on the west side"));
        Assert.All(blue, p => Assert.True(p.At.X > 0, $"{p.Model} on line {p.Line} is not on the east side"));
        Assert.Equal(red.Count, blue.Count);
        foreach (var (west, east) in red.Zip(blue))
        {
            Assert.Equal(west.Model.Replace("team-red-", "team-blue-"), east.Model);
            Assert.Equal(new Vector2(-west.At.X, west.At.Y), east.At);
            Assert.Equal(0, ((west.Yaw + east.Yaw) % 360 + 360) % 360, 3);
            Assert.Equal(west.Scale, east.Scale);
            // The heights are each castle's own: its towers are scaled a little differently on either side
            Assert.True(MathF.Abs(west.Y - east.Y) < 1, $"{west.Model} on line {west.Line} and its mirror stand {west.Y} and {east.Y} m up");
        }
    }

    [Fact]
    public void LiftedOnesStandOnTheCastle_TheOthersOnTheGroundClearOfItsWalls()
    {
        var castles = Level.Placements.Where(p => p.Model == "castle0").ToList();
        var castle = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelFiles.All(Path.Combine(Content, "models"))
            .Single(f => Path.GetFileNameWithoutExtension(f) == "castle0"))).Distance;

        var problems = new List<string>();
        foreach (var placement in Team("red").Concat(Team("blue")))
        {
            var home = castles.MinBy(c => Vector2.Distance(c.At, placement.At))!;
            var local = Placement2D.ToLocal(placement.At - home.At, home.Yaw);
            if (placement.Y > 0)
            {
                // Its base is on the castle's surface: within 10 cm of it, as its stems go on below the base
                var distance = castle(new Vector3(local.X, placement.Y, local.Y));
                if (MathF.Abs(distance) > 0.1f)
                    problems.Add($"{placement.Model} on line {placement.Line} is {distance:0.00} m from the castle's surface at its base");
            }
            else
            {
                // On the ground and clear of the walls a metre up, where a clump is widest
                var distance = castle(new Vector3(local.X, 1, local.Y));
                if (distance < 0.3f)
                    problems.Add($"{placement.Model} on line {placement.Line} stands {distance:0.00} m from the castle a metre up");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
