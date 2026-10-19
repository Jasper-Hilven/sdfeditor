using System.Numerics;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.ProgramFiles;

/// <summary>
/// The exploding amanita scout (content/models/characters/exploding/): its pieces are the scout cut by cells that split all of space,
/// so together they are the scout, with no gap and no overlap, and each eye and hand comes off whole.
/// </summary>
public class ExplodingPiecesTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private static Func<Vector3, float> Load(string path) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Read(File.ReadAllText(path), ModelFiles.PartsIn(Content))).Distance;

    private static readonly Func<Vector3, float> Scout = Load(Path.Combine(Content, "models", "characters", "amanita-scout.sdf"));

    private static readonly Func<Vector3, float> Core = Load(Path.Combine(Content, "parts", "amanita_scout_core.sdf"));

    private static readonly (string name, Func<Vector3, float> distance)[] Pieces =
        Directory.GetFiles(Path.Combine(Content, "models", "characters", "exploding"), "amanita-scout-*.sdf")
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileNameWithoutExtension(path), Load(path)))
            .ToArray();

    [Fact]
    public void Pieces_FillTheScoutExactly_WithoutOverlapping()
    {
        Assert.Equal(14, Pieces.Length);
        var inside = 0;
        // A grid over the scout's bounds (1.42 × 1.81 × 1.46 m), a little beyond them
        const int n = 36;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        for (var k = 0; k < n; k++)
        {
            var p = new Vector3(-0.8f + 1.6f * i / (n - 1), -0.1f + 1.9f * j / (n - 1), -0.8f + 1.6f * k / (n - 1));
            var scout = Scout(p);
            var distances = Pieces.Select(piece => piece.distance(p)).ToArray();
            // Inside the scout: in exactly one piece; outside: in none. Away from the surface, to stay clear of rounding
            if (MathF.Abs(scout) < 1e-4f)
                continue;
            var containing = distances.Count(d => d < 0);
            Assert.True(scout < 0 ? containing == 1 : containing == 0,
                $"At {p} the scout is {scout:0.0000} away and {containing} pieces hold it ({string.Join(", ", Pieces.Where((_, x) => distances[x] < 0).Select(x => x.name))})");
            // Where a piece's surface is the scout's own, the distance is the scout's
            Assert.True(distances.Min() >= scout - 1e-4f, $"At {p} a piece reaches beyond the scout");
            inside += scout < 0 ? 1 : 0;
        }
        Assert.True(inside > 1000);
    }

    [Theory]
    [InlineData("amanita-scout-eye0", 0.13f, 0.86f, 0.3f)]
    [InlineData("amanita-scout-eye1", -0.13f, 0.86f, 0.3f)]
    [InlineData("amanita-scout-hand0", 0.29f, 0.55f, 0.1f)]
    [InlineData("amanita-scout-hand1", -0.29f, 0.55f, 0.1f)]
    public void EyesAndHands_ComeOffWhole_WithNothingOfTheStem(string name, float x, float y, float z)
    {
        var piece = Pieces.Single(p => p.name == name).distance;
        var centre = new Vector3(x, y, z);
        // Within 25 cm of the eye or hand and outside the core, the piece is the scout: in it where the scout is, and with the
        // scout's surface where it is near
        for (var i = 0; i < 2000; i++)
        {
            var p = centre + RandomInBall(i) * 0.25f;
            var scout = Scout(p);
            if (Core(p) > 0 && p.X * x > 0 && (p.Y > 0.705f) == (y > 0.705f))
            {
                Assert.Equal(scout < 0, piece(p) < 0);
                if (MathF.Abs(scout) < 0.002f)
                    Assert.Equal(scout, piece(p), 4);
            }
        }
        // The stem beside it is not in it, and the cell keeps clear of the stem and of the eye or hand: no cut face on either
        Assert.True(piece(new Vector3(0, y, 0)) > 0);
        Assert.True(Core(new Vector3(0, y, 0)) < -0.01f);
    }

    [Fact]
    public void TheModel_IsMeshedAsTheScoutWithItsEyesAndHands_AndEachPieceAsOneMesh()
    {
        var scout = PartSplitter.Split(SdfProgramFile.Read(
            File.ReadAllText(Path.Combine(Content, "models", "characters", "amanita-scout.sdf")), ModelFiles.PartsIn(Content)));
        Assert.Equal(["amanita_scout", "eye", "hand"], scout.Select(s => s.Name));

        var piece = PartSplitter.Split(SdfProgramFile.Read(
            File.ReadAllText(Path.Combine(Content, "models", "characters", "exploding", "amanita-scout-eye0.sdf")), ModelFiles.PartsIn(Content)));
        Assert.Equal([PartSplitter.BodyName], piece.Select(s => s.Name));
    }

    /// <summary>A point in the unit ball, the same for the same index.</summary>
    private static Vector3 RandomInBall(int index)
    {
        var random = new Random(index);
        while (true)
        {
            var p = new Vector3(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1);
            if (p.LengthSquared() <= 1)
                return p;
        }
    }
}
