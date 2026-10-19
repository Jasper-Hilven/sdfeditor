using System.Numerics;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;

namespace SDFPipeline.Tests.Levels;

public class LevelFileTests
{
    private const string Meadow =
        """
        # The meadow
        format 1

        play size=(150, 100)                      # the playable rectangle
        seed 7

        hills
            ring width=28 height=10
            noise amplitude=2.5 scale=18

        edge
            margin=6
        """;

    [Fact]
    public void Read_TakesEverySection()
    {
        var level = LevelFile.Read(Meadow);

        Assert.Equal(new Vector2(150, 100), level.Play.Size);
        Assert.Equal(PlayArea.DefaultNoiseAmplitude, level.Play.NoiseAmplitude);
        Assert.Equal(7, level.Seed);
        Assert.Equal(new HillRing(28, 10, 2.5f, 18), level.Hills);
        Assert.Equal(new IslandEdge(6), level.Edge);
    }

    [Fact]
    public void Read_LeftOutSectionsKeepTheirDefaults()
    {
        var level = LevelFile.Read("format 1\nplay size=(40, 30) noise=0 noiseScale=5");

        Assert.Equal(new PlayArea(new Vector2(40, 30), 0, 5), level.Play);
        Assert.Equal(LevelDefinition.DefaultSeed, level.Seed);
        Assert.Equal(HillRing.None, level.Hills);
        Assert.Equal(IslandEdge.None, level.Edge);
    }

    [Fact]
    public void Read_TakesParametersOnTheLineOrIndentedBelowIt()
    {
        var level = LevelFile.Read("format 1\nplay\n    size=(150, 100)\n    noise=0.1\nedge margin=4");

        Assert.Equal(new Vector2(150, 100), level.Play.Size);
        Assert.Equal(0.1f, level.Play.NoiseAmplitude);
        Assert.Equal(4, level.Edge.Margin);
    }

    [Fact]
    public void Read_TakesRegionsInOrder_WithTheirShapes()
    {
        var level = LevelFile.Read(
            """
            format 1
            play size=(150, 100)
            regions
                dirt
                    Circle center=(-60, 0) radius=18 noise=1.2
                    Box2D center=(1, 2) size=(4, 6) angle=30
                tiles stone=0.5
                    RoundedBox2D center=(-62, 0) size=(14, 10) radius=1
                path pattern=stones grout=0.1
                    Polyline points=[(-45, 0), (-10, 6), (20, -4)] width=3 noise=0.6 noiseScale=4
            """);

        Assert.Equal(["dirt", "tiles", "path"], level.Regions.Select(r => r.Name));
        Assert.Equal("ground_dirt", level.Regions[0].Material);
        Assert.Equal(new CircleRegion(new Vector2(-60, 0), 18, 1.2f, RegionShape.DefaultNoiseScale), level.Regions[0].Shapes[0]);
        Assert.Equal(new BoxRegion(new Vector2(1, 2), new Vector2(4, 6), 30, 0, 0, RegionShape.DefaultNoiseScale), level.Regions[0].Shapes[1]);
        Assert.Equal(GroundPattern.Plain, level.Regions[0].Pattern);
        // The region named tiles is stones unless it says otherwise; any region can be
        Assert.Equal((GroundPattern.Stones, 0.5f, GroundRegion.DefaultGroutWidth), (level.Regions[1].Pattern, level.Regions[1].StoneSize, level.Regions[1].GroutWidth));
        Assert.Equal((GroundPattern.Stones, 0.1f), (level.Regions[2].Pattern, level.Regions[2].GroutWidth));
        var path = Assert.IsType<PolylineRegion>(level.Regions[2].Shapes.Single());
        Assert.Equal([new Vector2(-45, 0), new Vector2(-10, 6), new Vector2(20, -4)], path.Points);
        Assert.Equal((3f, 0.6f, 4f), (path.Width, path.Noise, path.NoiseScale));
    }

    [Fact]
    public void Read_TakesPlacements_ModelNamesWithHyphens()
    {
        var level = LevelFile.Read(
            """
            format 1
            play size=(150, 100)
            place
                castle0 at=(-62, 0) yaw=90 ground=tiles flatten=22
                amanita-scout at=(3, 4) scale=2
                    yaw=-45
            """);

        Assert.Equal(new Placement("castle0", new Vector2(-62, 0), 90, 1, 22, Placement.DefaultFlattenFalloff, "tiles", Placement.DefaultGroundMargin, 4),
            level.Placements[0]);
        Assert.Equal(new Placement("amanita-scout", new Vector2(3, 4), -45, 2, 0, Placement.DefaultFlattenFalloff, null, Placement.DefaultGroundMargin, 5),
            level.Placements[1]);
    }

    [Fact]
    public void Read_RegionsBase_NamesTheGroundOutsideTheRegions()
    {
        var level = LevelFile.Read("format 1\nplay size=(40, 30)\nregions base=moss\n    litter\n        Circle center=(0, 0) radius=5");

        Assert.Equal("moss", level.Base);
        Assert.Equal("ground_moss", level.BaseMaterial);
        Assert.Equal(["litter"], level.Regions.Select(r => r.Name));
    }

    [Fact]
    public void Read_WithoutBase_TheGroundIsGrass()
    {
        var level = LevelFile.Read("format 1\nplay size=(40, 30)\nregions\n    dirt\n        Circle center=(0, 0) radius=5");

        Assert.Equal(("grass", "ground_grass"), (level.Base, level.BaseMaterial));
        Assert.Equal("grass", LevelFile.Read("format 1\nplay size=(40, 30)").Base);
    }

    [Fact]
    public void Read_RegionsLine_RejectsUnknownParametersAndANumberAsBase()
    {
        var unknown = Assert.Throws<SdfProgramFileException>(() =>
            LevelFile.Read("format 1\nplay size=(40, 30)\nregions ground=moss\n    dirt\n        Circle center=(0, 0) radius=5"));
        Assert.Equal(3, unknown.Line);
        var number = Assert.Throws<SdfProgramFileException>(() =>
            LevelFile.Read("format 1\nplay size=(40, 30)\nregions base=3\n    dirt\n        Circle center=(0, 0) radius=5"));
        Assert.Equal(3, number.Line);
    }

    [Fact]
    public void ContentMeadowAndChessboard_KeepGrassAsTheirBaseGround()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        foreach (var name in new[] { "meadow", "chessboard" })
            Assert.Equal("grass", LevelFile.Load(Path.Combine(content, LevelFile.FolderName, name + LevelFile.Extension)).Base);
    }

    [Fact]
    public void ContentMeadow_IsAValidLevel()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "meadow" + LevelFile.Extension));

        Assert.Equal(new Vector2(150, 100), level.Play.Size);
        Assert.InRange(level.Hills.Height, 8, 12);
        Assert.Equal(["dirt", "path", "tiles"], level.Regions.Select(r => r.Name));
        // A castle on each short side, on a plateau of tiles
        var castles = level.Placements.Where(p => p.Model == "castle0").ToArray();
        Assert.Equal([-62f, 62f], castles.Select(c => c.At.X).Order());
        Assert.All(castles, c => Assert.Equal(("tiles", true), (c.Ground, c.Flatten > 0)));
        // Grass turfs and flowers, off the dirt, path and tiles, drawn in the foliage group
        Assert.Equal(["grass-turf0", "flower0"], level.Scatters.Select(s => s.Entries[0].Model));
        Assert.All(level.Scatters, s => Assert.Equal("foliage", s.Group));
        Assert.All(level.Scatters, s => Assert.Equal(["dirt", "tiles", "path"], s.Avoid));
        Assert.Equal([6f, 15f, 35f], level.DrawGroups.Single(g => g.Name == "foliage").Rings);
    }

    [Fact]
    public void ContentChessboard_IsAFlatCheckerboard_WithPiecesOfBothColoursOnIt()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension));

        Assert.Equal((new Vector2(160, 160), 0f), (level.Play.Size, level.Play.NoiseAmplitude));
        // 20 × 20 squares of 8 m, the corner squares dark, in a rim
        var regions = new GroundRegions(level, _ => throw new InvalidOperationException("No placement paints the ground"));
        for (var i = 0; i < 20; i++)
        for (var j = 0; j < 20; j++)
        {
            var centre = new Vector2(-76 + 8 * i, -76 + 8 * j);
            Assert.Equal((i + j) % 2 == 0 ? "dark" : "light", regions.At(centre)?.Name);
            // Just over the edge to the next square along x, or onto the rim past the last
            Assert.Equal(i == 19 ? "rim" : (i + j) % 2 == 0 ? "light" : "dark", regions.At(centre + new Vector2(4.2f, 0))?.Name);
        }
        Assert.Equal("rim", regions.At(new Vector2(81.5f, 0))?.Name);
        Assert.Null(regions.At(new Vector2(84, 0)));
        // Every piece on the board, both colours of every kind among them, each model in content/models
        var models = LevelModels.In(Path.Combine(content, "models"));
        Assert.InRange(level.Placements.Count, 60, 160);
        Assert.All(level.Placements, p => Assert.True(MathF.Abs(p.At.X) < 80 && MathF.Abs(p.At.Y) < 80, $"{p.Model} on line {p.Line}"));
        Assert.All(level.Placements, p => Assert.NotNull(models(p.Model)));
        foreach (var piece in new[] { "pawn", "rook", "knight", "bishop", "queen", "king" })
        foreach (var colour in new[] { "light", "dark" })
            Assert.Contains(level.Placements, p => p.Model == $"{piece}-{colour}");
    }

    [Fact]
    public void ContentChessboard_SquareEdges_AreStraightInTheRegionMapAndNotWobbledByTheShader()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension));
        Assert.All(level.Regions, r => Assert.Equal(0f, r.Wobble));

        var regions = new GroundRegions(level, _ => throw new InvalidOperationException("No placement paints the ground"));
        var dark = level.Regions.ToList().FindIndex(r => r.Name == "dark");
        var map = RegionMap.Bake(regions, new Vector2(-24, -24), new Vector2(24, 24));

        // Bilinear, as the shader's sampler filters it: the stored distance is the true one at the texel centres
        float Sample(Vector2 p)
        {
            var u = (p.X - map.Min.X) / RegionMap.TexelSize - 0.5f;
            var v = (p.Y - map.Min.Y) / RegionMap.TexelSize - 0.5f;
            var (i, j) = ((int)MathF.Floor(u), (int)MathF.Floor(v));
            var (fx, fy) = (u - i, v - j);
            float At(int a, int b) => map.At(dark, a, b);
            return (At(i, j) * (1 - fx) + At(i + 1, j) * fx) * (1 - fy) + (At(i, j + 1) * (1 - fx) + At(i + 1, j + 1) * fx) * fy;
        }

        // Along each square edge x = 8k and z = 8k (away from the corners, where the edges meet), the filtered distance crosses 0 within
        // 1 cm of the edge: the edge is straight, whichever side is dark
        foreach (var edge in new[] { -16f, -8f, 0f, 8f, 16f })
        foreach (var along in new[] { -20f, -12f, -4f, 4f, 12f, 20f })
        {
            foreach (var (a, b) in new[] { (new Vector2(edge, along), Vector2.UnitX), (new Vector2(along, edge), Vector2.UnitY) })
            {
                var left = Sample(a - b * 0.3f);
                var right = Sample(a + b * 0.3f);
                Assert.True(left * right < 0, $"the edge at {a} should lie between its two sides");
                // Linear between the two samples: where it crosses 0
                var crossing = -0.3f + 0.6f * left / (left - right);
                Assert.InRange(MathF.Abs(crossing), 0, 0.01f);
            }
        }
    }

    [Fact]
    public void ContentChessboard_GamePieces_UseAllTwentyModelsOnTheBoard()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension));

        var games = Directory.GetFiles(Path.Combine(content, "models", "games"), "*.sdf").Select(Path.GetFileNameWithoutExtension).ToList();
        Assert.Equal(20, games.Count);
        foreach (var model in games)
            Assert.Contains(level.Placements, p => p.Model == model);
        Assert.All(level.Placements.Where(p => games.Contains(p.Model)),
            p => Assert.True(MathF.Abs(p.At.X) < 76 && MathF.Abs(p.At.Y) < 76, $"{p.Model} on line {p.Line} is off the board"));
    }

    [Fact]
    public void ContentChessboard_GamePieces_DoNotOverlapEachOtherOrTheChessPieces()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var path = Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension);
        var level = LevelFile.Load(path);
        var lines = File.ReadAllLines(path);
        var footprints = LevelModels.In(Path.Combine(content, "models"));
        var games = Directory.GetFiles(Path.Combine(content, "models", "games"), "*.sdf").Select(Path.GetFileNameWithoutExtension).ToHashSet();

        var problems = OverlapProblems(level.Placements, lines, footprints, games);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ContentChessboard_OverlapCheck_FindsAGamePieceMovedOntoAChessPiece()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var path = Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension);
        var level = LevelFile.Load(path);
        var lines = File.ReadAllLines(path);
        var footprints = LevelModels.In(Path.Combine(content, "models"));
        var games = Directory.GetFiles(Path.Combine(content, "models", "games"), "*.sdf").Select(Path.GetFileNameWithoutExtension).ToHashSet();

        // The first domino lands in the middle of the first chess piece, and the first chess piece's neighbour die lands on a game piece
        var chess = level.Placements.First(p => !games.Contains(p.Model));
        var moved = level.Placements.Select(p => p.Model == "domino-6-3-lying" ? p with { At = chess.At } : p).ToList();

        Assert.NotEmpty(OverlapProblems(moved, lines, footprints, games));
    }

    /// <summary>
    /// Every footprint circle (centre of the model's bounds on the ground, half its larger side, both times the scale) of a game piece
    /// that sinks into another: within a group of the file (the comments in the place block start groups) by more than a quarter of
    /// the smaller radius, anywhere else (other groups, chess pieces) at all.
    /// </summary>
    private static List<string> OverlapProblems(IReadOnlyList<Placement> placements, string[] lines, Func<string, ModelFootprint?> footprints,
        ICollection<string?> games)
    {
        // A group is the last comment above a placement
        int GroupOf(int line)
        {
            for (var i = Math.Min(line, lines.Length) - 1; i >= 0; i--)
                if (lines[i].TrimStart().StartsWith('#'))
                    return i;
            return -1;
        }

        var circles = placements.Select(p =>
        {
            var footprint = footprints(p.Model)!;
            var centre = p.At + Placement2D.ToLevel(footprint.Center * p.Scale, p.Yaw);
            return (Placement: p, Centre: centre, Radius: MathF.Max(footprint.Size.X, footprint.Size.Y) / 2 * p.Scale, Group: GroupOf(p.Line),
                Game: games.Contains(p.Model));
        }).ToList();

        var problems = new List<string>();
        for (var i = 0; i < circles.Count; i++)
        for (var j = i + 1; j < circles.Count; j++)
        {
            var (a, b) = (circles[i], circles[j]);
            if (!a.Game && !b.Game)
                continue;
            var allowed = a.Game && b.Game && a.Group == b.Group ? 0.25f * MathF.Min(a.Radius, b.Radius) : 0;
            var depth = a.Radius + b.Radius - Vector2.Distance(a.Centre, b.Centre);
            if (depth > allowed)
                problems.Add($"{a.Placement.Model} (line {a.Placement.Line}) and {b.Placement.Model} (line {b.Placement.Line}) overlap by {depth:0.00} m, " +
                             $"allowed {allowed:0.00} m");
        }
        return problems;
    }

    [Fact]
    public void ContentChessPieces_HaveNoEyesOrHands()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        // 19 pieces: six kinds in both colours, plus the fallen variants
        var pieces = Directory.GetFiles(Path.Combine(content, "models", "chess"), "*.sdf");
        Assert.Equal(19, pieces.Length);
        Assert.All(pieces, file =>
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("part=eye", StringComparison.OrdinalIgnoreCase), Path.GetFileName(file));
            Assert.False(text.Contains("part=hand", StringComparison.OrdinalIgnoreCase), Path.GetFileName(file));
        });
    }

    [Theory]
    [InlineData("", 1, "The file is empty")]
    [InlineData("play size=(1, 1)", 1, "must start with 'format 1'")]
    [InlineData("format 2\nplay size=(1, 1)", 1, "only format 1 can be read")]
    [InlineData("format 1\nseed 1", 2, "The level has no playable area")]
    [InlineData("format 1\nplay size=(1, 1)\nmeadow", 3, "A level has no section 'meadow'; its sections are play, seed, hills, edge, regions, place")]
    [InlineData("format 1\nplay size=(1, 1)\nplay size=(2, 2)", 3, "'play' is given twice, first on line 2")]
    [InlineData("format 1\nplay", 2, "Play is missing parameter 'size'")]
    [InlineData("format 1\nplay size=(1, 1, 1)", 2, "'size' of play needs a size like (150, 100)")]
    [InlineData("format 1\nplay size=(0, 1)", 2, "needs a positive width and length")]
    [InlineData("format 1\nplay size=(1, 1) noise=2", 2, "'noise' of play is 2, but needs metres of unevenness, 0 to 0.3")]
    [InlineData("format 1\nplay size=(1, 1) colour=2", 2, "Play has no parameter 'colour'; its parameters are size, noise, noiseScale")]
    [InlineData("format 1\nplay size=(1, 1) size=(2, 2)", 2, "Parameter 'size' of play is given twice")]
    [InlineData("format 1\nplay size=(1, 1)\nseed 1.5", 3, "The seed is a whole number")]
    [InlineData("format 1\nplay size=(1, 1)\nseed 1\n    margin=2", 4, "Nothing can be indented below 'seed'")]
    [InlineData("format 1\nplay size=(1, 1)\nhills", 3, "The hills need a ring indented below them")]
    [InlineData("format 1\nplay size=(1, 1)\nhills\n    ring width=28", 4, "The hills' ring is missing parameter 'height'")]
    [InlineData("format 1\nplay size=(1, 1)\nhills\n    ring width=28 height=10\n    trees density=1", 5, "The hills have no 'trees'")]
    [InlineData("format 1\nplay size=(1, 1)\nhills\n    ring width=28 height=10\n    ring width=2 height=1", 5, "The hills' ring is given twice, first on line 4")]
    [InlineData("format 1\nplay size=(1, 1)\nhills\n    ring width=28 height=x", 4, "'height' of the hills' ring needs the crest's height in metres")]
    [InlineData("format 1\nplay size=(1, 1)\nedge\n    margin=6\n    width=2", 5, "Edge has no parameter 'width'; its parameters are margin")]
    [InlineData("format 1\nplay size=(1, 1)\nedge\n    margin 6", 4, "Only parameters like name=value can be indented below edge")]
    [InlineData("format 1\nplay size=(1, 1)\n\tseed 1", 3, "Indent with spaces, not tabs")]
    [InlineData("format 1\nplay size=(1, 1)\nregions", 3, "The regions need a region indented below them")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    dirt", 4, "Region 'dirt' needs shapes indented below it")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    dirt\n        Star center=(0, 0)", 5, "A region has no shape 'Star'; its shapes are Circle, Box2D, RoundedBox2D, Polyline")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    dirt\n        Circle center=(0, 0)", 5, "Circle is missing parameter 'radius'")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    dirt\n        Box2D center=(0, 0) size=(1, 1) radius=1", 5, "Box2D has no parameter 'radius'")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    dirt\n        Polyline points=[(0, 0)] width=1", 5, "'points' of Polyline needs points like")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    dirt pattern=bricks\n        Circle center=(0, 0) radius=1", 4, "'pattern' of region dirt needs one of plain, stones")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    a\n        Circle center=(0, 0) radius=1\n    a\n        Circle center=(0, 0) radius=1", 6, "Region 'a' is given twice, first on line 4")]
    [InlineData("format 1\nplay size=(1, 1)\nregions\n    a\n        Circle center=(0, 0) radius=1\n    b\n        Circle center=(0, 0) radius=1\n    c\n        Circle center=(0, 0) radius=1\n    d\n        Circle center=(0, 0) radius=1\n    e\n        Circle center=(0, 0) radius=1", 12, "A level has at most 4 regions")]
    [InlineData("format 1\nplay size=(1, 1)\nplace", 3, "Nothing is placed")]
    [InlineData("format 1\nplay size=(1, 1)\nplace\n    -rock at=(0, 0)", 4, "Expected a model's name")]
    [InlineData("format 1\nplay size=(1, 1)\nplace\n    rock1 yaw=3", 4, "Placement rock1 is missing parameter 'at'")]
    [InlineData("format 1\nplay size=(1, 1)\nplace\n    rock1 at=(0, 0) ground=(1, 2)", 4, "'ground' of placement rock1 needs a name")]
    public void Read_ExplainsWhatIsWrongAndWhere(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }
}
