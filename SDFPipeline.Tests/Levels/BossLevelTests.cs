using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>
/// Ticket 33.3: the level format's additions for the final boss's arena (layout boss, phases= on placements, terrace= on the hills'
/// ring, the boss section, area= and facing= on trees sections), their bake into level.json, and the Champion's Arena level itself.
/// </summary>
public class BossLevelTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    private const string Sides =
        "sides\n    west\n        spawn at=(-50, -18) yaw=90\n        flag at=(-47, 0)\n    east\n        spawn at=(52, 28) yaw=-150\n        flag at=(36, 0)\n" +
        "neutral\n    pod electro-gun at=(0, 26)\n    pod flamethrower at=(0, -22)\n";

    private const string Spots = "boss\n    prime at=(49, 0) yaw=-90 y=4\n    crash at=(6, 0)\n    plinth at=(-50, 10) yaw=90\n    shield at=(49, 0) radius=7 y=4\n";

    private static string Level(string extra = "", string layout = "layout boss\n", string sides = Sides, string spots = Spots) =>
        "format 1\nplay size=(120, 80)\n" + layout + sides + spots + extra;

    // ---- the format

    [Fact]
    public void Read_LayoutBoss_TakesSidesWithoutPodsAndMachinesOnOneSide_AndTheBossSection()
    {
        var level = LevelFile.Read(Level());

        Assert.Equal(LevelLayout.Boss, level.Layout);
        var battle = level.Battle!;
        Assert.Empty(battle.East.Weapons);
        Assert.Null(battle.East.Health);
        Assert.Equal(["spawn", "flag"], battle.East.Objects.Select(o => o.Name));
        var boss = level.Boss!;
        Assert.Equal(new BossSpot(new Vector2(49, 0), -90, 4, 15), boss.Prime);
        Assert.Equal(new Vector2(6, 0), boss.Crash.At);
        Assert.Equal(new Vector2(-50, 10), boss.Plinth.At);
        Assert.Equal((new Vector2(49, 0), 7f, 4f), (boss.Shield.At, boss.Shield.Radius, boss.Shield.Y));
    }

    [Fact]
    public void Read_LayoutLine_MayBeAnywhere_AndMirroredIsTheDefault()
    {
        var late = LevelFile.Read(Level(layout: "", extra: "layout boss\n"));
        var plain = LevelFile.Read("format 1\nplay size=(40, 30)");

        Assert.Equal(LevelLayout.Boss, late.Layout);
        Assert.Equal(LevelLayout.Mirrored, plain.Layout);
        Assert.Null(plain.Boss);
        Assert.Equal(LevelLayout.Mirrored, LevelFile.Read("format 1\nplay size=(40, 30)\nlayout mirrored").Layout);
    }

    [Fact]
    public void Read_ASideWithMachinesAlone_IsAllowedOnTheBossLayoutOnly()
    {
        const string east = "        turret at=(32, 9) yaw=-60\n        turret at=(32, -9)\n        turret at=(48, 20)\n        turret at=(48, -20)\n        machines at=(50, -26) radius=4\n";
        var sides = Sides.Replace("        flag at=(36, 0)\n", "        flag at=(36, 0)\n" + east);

        var level = LevelFile.Read(Level(sides: sides));
        var plain = PlainSides.Replace("        flag at=(47, 0)\n", "        flag at=(47, 0)\n" + east);
        var error = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(Level(sides: plain, layout: "", spots: "")));

        Assert.True(level.Battle!.HasMachines);
        Assert.Null(level.Battle.West.MachineSpawn);
        Assert.Contains("so side west needs them too", error.Problem);
    }

    [Fact]
    public void Read_Phases_AreOneOrARange()
    {
        var level = LevelFile.Read(Level("place\n    rock0 at=(1, 2) phases=3\n    rock1 at=(3, 4) phases=1-2\n    rock2 at=(5, 6) phases=2-3 yaw=10\n    rock3 at=(7, 8)\n"));

        Assert.Equal([new PhaseRange(3, 3), new PhaseRange(1, 2), new PhaseRange(2, 3), (PhaseRange?)null], level.Placements.Select(p => p.Phases));
        Assert.Equal(["3", "1-2", "2-3"], level.Placements.Take(3).Select(p => p.Phases!.Value.ToString()));
        Assert.True(new PhaseRange(1, 2).Contains(2));
        Assert.False(new PhaseRange(1, 2).Contains(3));
        Assert.Equal(10, level.Placements[2].Yaw);
    }

    [Fact]
    public void Read_HillsTerrace_IsTheStepDepth_ZeroWithoutIt()
    {
        var terraced = LevelFile.Read("format 1\nplay size=(120, 80)\nhills\n    ring width=24 height=12 terrace=1.5\n    noise amplitude=0.4").Hills;
        var smooth = LevelFile.Read("format 1\nplay size=(120, 80)\nhills\n    ring width=24 height=12").Hills;

        Assert.Equal(1.5f, terraced.Terrace);
        Assert.Equal(0, smooth.Terrace);
    }

    [Fact]
    public void Read_TreesAreaAndFacing()
    {
        var level = LevelFile.Read("format 1\nplay size=(120, 80)\ntrees\n    crowd0\n    band=(2, 20) density=0.1 spacing=1 area=[62, -10, 76, 10] facing=15\ndraw trees rings=[25, 60]");

        var layer = level.Trees.Single();
        Assert.Equal((new Vector2(62, -10), new Vector2(76, 10)), layer.Area);
        Assert.Equal(15f, layer.Facing);
    }

    /// <summary>A level with the plain, mirrored sides (pods and all) in place of the boss's.</summary>
    private const string PlainSides =
        "sides\n    west\n        spawn at=(-50, -18)\n        flag at=(-47, 0)\n        pod minigun at=(-40, 5)\n        pod health at=(-40, 9)\n" +
        "    east\n        spawn at=(50, -18)\n        flag at=(47, 0)\n        pod minigun at=(40, 5)\n        pod health at=(40, 9)\n" +
        "neutral\n    pod electro-gun at=(0, 26)\n    pod flamethrower at=(0, -22)\n";

    /// <summary>The 1-based number of the first line of the text that starts with the marker once indented lines are trimmed.</summary>
    private static int At(string text, string marker, int skip = 0) =>
        text.Split('\n').Select((l, i) => (l, i)).Where(t => t.l.TrimStart().StartsWith(marker, StringComparison.Ordinal)).ElementAt(skip).i + 1;

    private static string WithSpots(string spots) => Level(spots: spots);

    public static TheoryData<string, int, string> Errors
    {
        get
        {
            var data = new TheoryData<string, int, string>();
            void Add(string text, int line, string problem) => data.Add(text, line, problem);
            Add(Level(layout: "layout fancy\n"), 3, "A layout is one of mirrored, boss; there is no layout 'fancy'");
            Add(Level(layout: "layout boss extra=1\n"), 3, "Unexpected 'extra=1'");
            Add(Level(layout: "layout boss\nlayout boss\n"), 4, "'layout' is given twice, first on line 3");
            Add(Level(spots: ""), 3, "The boss layout needs a 'boss' section listing prime, crash, plinth, shield");
            Add(Level(sides: "", spots: Spots), 3, "The boss layout is for a level with battle sides");
            var plainBoss = Level(layout: "", sides: PlainSides);
            Add(plainBoss, At(plainBoss, "boss"), "The 'boss' section belongs to a boss level; add 'layout boss'");
            var plainPhases = Level("place\n    rock0 at=(1, 2) phases=1-2\n", layout: "", sides: PlainSides, spots: "");
            Add(plainPhases, At(plainPhases, "rock0"), "'phases' tags a model for the boss fight's phases, in a boss level");
            foreach (var bad in new[] { "0", "4", "3-1", "1.5" })
            {
                var text = Level($"place\n    rock0 at=(1, 2) phases={bad}\n");
                Add(text, At(text, "rock0"), "'phases' of placement rock0 needs phases like 3 or 1-2, each 1 to 3, the first not after the last");
            }
            var noCrash = WithSpots(Spots.Replace("    crash at=(6, 0)\n", ""));
            Add(noCrash, At(noCrash, "boss"), "The boss section needs its crash indented below it");
            var twice = WithSpots(Spots + "    prime at=(1, 1)\n");
            Add(twice, At(twice, "prime", 1), $"The boss's prime is given twice, first on line {At(twice, "prime")}");
            var king = WithSpots(Spots + "    king at=(1, 1)\n");
            Add(king, At(king, "king"), "The boss section has no 'king'; it has prime, crash, plinth, shield");
            var noRadius = WithSpots(Spots.Replace("radius=7 ", ""));
            Add(noRadius, At(noRadius, "shield"), "The boss's shield is missing parameter 'radius'");
            var crashYaw = WithSpots(Spots.Replace("crash at=(6, 0)", "crash at=(6, 0) yaw=3"));
            Add(crashYaw, At(crashYaw, "crash"), "The boss's crash has no parameter 'yaw'");
            Add("format 1\nplay size=(1, 1)\nhills\n    ring width=24 height=12 terrace=30", 4, "The terrace's steps (30 m deep) are deeper than the ring is wide (24 m)");
            Add("format 1\nplay size=(1, 1)\ntrees\n    t\n    band=(0, 5) density=1 spacing=1 area=[1, 2, 0, 5]", 5, "The area needs the least x and z and then the greatest");
            Add("format 1\nplay size=(1, 1)\ntrees\n    t\n    band=(0, 5) density=1 spacing=1 facing=200", 5, "'facing' of trees section is 200");
            Add("format 1\nplay size=(1, 1)\nscatter\n    t\n    density=1 spacing=1 facing=20", 5, "Scatter has no parameter 'facing'");
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Read_ExplainsWhatIsWrongWithTheBossFeatures(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Contains(problem, exception.Problem);
        Assert.Equal(line, exception.Line);
    }

    // ---- the terrace

    [Fact]
    public void Terrace_RisesInFlatTreadsAndRisers_ToTheCrest()
    {
        var play = new PlayArea(new Vector2(120, 80), 0, 14);
        var terraced = new LevelTerrain(new LevelDefinition(play, 1, new HillRing(24, 12, 0, 18) { Terrace = 1.5f }, IslandEdge.None));
        var smooth = new LevelTerrain(new LevelDefinition(play, 1, new HillRing(24, 12, 0, 18), IslandEdge.None));

        // A tread is flat over two thirds of the step and a riser climbs the rest, 0.75 m a step on average
        Assert.Equal(0, terraced.Height(60.2f, 0), 4);
        Assert.Equal(terraced.Height(60.2f, 0), terraced.Height(60.9f, 0), 4);
        Assert.Equal(0.75f, terraced.Height(61.6f, 0), 2);
        Assert.Equal(0.75f, terraced.Height(62.3f, 0), 2);
        Assert.InRange(terraced.Height(61.25f, 0), 0.05f, 0.7f);
        Assert.Equal(12, terraced.Height(84, 0), 3);
        // Never far from the smooth slope's own line, the stands' average slope (1 / 2 up the middle), and always rising
        var last = -1f;
        for (var d = 0f; d <= 24; d += 0.1f)
        {
            var height = terraced.Height(60 + d, 0);
            Assert.True(height >= last - 1e-4f, $"the ground falls at {d} m");
            Assert.InRange(height, d / 2 - 1.5f, d / 2 + 1.5f);
            last = height;
        }
        // Treads are walkable-flat, risers steep
        Assert.True(terraced.Normal(60.5f, 0).Y > 0.99f);
        Assert.True(terraced.Normal(61.25f, 0).Y < 0.8f);
                // Inside the play area, no difference
        Assert.Equal(smooth.Height(10, 10), terraced.Height(10, 10));
    }

    // ---- the scatter's area and facing

    [Fact]
    public void Scatter_AreaKeepsTheInstancesInside_AndFacingTurnsThemToTheMiddle()
    {
        var play = new PlayArea(new Vector2(40, 30), 0, 14);
        var level = new LevelDefinition(play, 3, new HillRing(12, 6, 0, 10), new IslandEdge(4));
        ScatterLayer Layer(float? facing, bool area) => new([new ScatterEntry("crowd", 1, 2)], "crowd", 0.3f, 1, 8, 1, [], 0, 90, 0,
            ScatterLayer.DefaultScale, 1) { Band = new Vector2(1, 10), Area = area ? (new Vector2(22, -6), new Vector2(30, 6)) : null, Facing = facing };
        ModelFootprint Crowd(string _) => new(new Vector2(-0.4f), new Vector2(0.4f));
        ScatteredLevel Scatter(ScatterLayer layer)
        {
            var l = level with { Trees = [layer] };
            return LevelScatter.Scatter(l, new LevelTerrain(l), new GroundRegions(l, Crowd), Crowd, _ => Vector3.One);
        }

        var inside = Scatter(Layer(null, true));
        var everywhere = Scatter(Layer(null, false));
        var facing = Scatter(Layer(10, false));

        Assert.NotEmpty(inside.Instances);
        Assert.All(inside.Instances, i => Assert.True(i.Position.X is >= 22 and <= 30 && i.Position.Z is >= -6 and <= 6));
        Assert.True(everywhere.Instances.Count > inside.Instances.Count * 3);
        // Same points, so the same count; each faces the middle give or take 10 degrees: front (+z turned by yaw) towards (-x, -z)
        Assert.Equal(everywhere.Instances.Count, facing.Instances.Count);
        Assert.All(facing.Instances, i =>
        {
            var wanted = MathF.Atan2(-i.Position.X, -i.Position.Z) * 180 / MathF.PI;
            var off = MathF.Abs(((i.Yaw - wanted) % 360 + 540) % 360 - 180);
            Assert.True(off <= 10.01f, $"{off} degrees off at ({i.Position.X}, {i.Position.Z})");
        });
        Assert.Contains(everywhere.Instances, i => MathF.Abs(i.Yaw - MathF.Atan2(-i.Position.X, -i.Position.Z) * 180 / MathF.PI) > 30);
    }

    // ---- the bake

    [Fact]
    public void Bake_WritesThePhaseTags_TheTerrace_AndTheBossSpots_IntoLevelJson()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var level = LevelFile.Read(
                "format 1\nplay size=(120, 80) noise=0\nhills\n    ring width=24 height=12 terrace=1.5\nedge margin=6\nlayout boss\n" +
                "place\n    rock0 at=(20, 10) phases=1-2\n    rock0 at=(-20, 10)\n" + Sides + Spots);
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var terrain = new LevelTerrain(level);

            LevelBaker.Bake(level, "boss", materials, root.FullName, _ => new ModelFootprint(new Vector2(-1), new Vector2(1)));

            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var placements = json.RootElement.GetProperty("placements");
            Assert.Equal([1, 2], placements[0].GetProperty("phases").EnumerateArray().Select(v => v.GetInt32()));
            Assert.False(placements[1].TryGetProperty("phases", out _));
            Assert.Equal(1.5f, json.RootElement.GetProperty("hills").GetProperty("terrace").GetSingle());
            var boss = json.RootElement.GetProperty("boss");
            Assert.Equal([49f, terrain.Height(49, 0) + 4, 0f], boss.GetProperty("prime").GetProperty("position").EnumerateArray().Select(v => v.GetSingle()));
            Assert.Equal(-90f, boss.GetProperty("prime").GetProperty("yaw").GetSingle());
            Assert.Equal(6f, boss.GetProperty("crash").GetProperty("position")[0].GetSingle());
            Assert.Equal(90f, boss.GetProperty("plinth").GetProperty("yaw").GetSingle());
            Assert.Equal(7f, boss.GetProperty("shield").GetProperty("radius").GetSingle());
            // The east side has no pods; its flag is still there
            var east = json.RootElement.GetProperty("battle").GetProperty("sides")[1];
            Assert.Equal(0, east.GetProperty("pods").GetArrayLength());

            // A level that is not a boss level has no boss and no tags
            LevelBaker.Bake(LevelFile.Read("format 1\nplay size=(120, 80)"), "plain", materials, root.FullName);
            using var plain = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            Assert.False(plain.RootElement.TryGetProperty("boss", out _));
            Assert.False(plain.RootElement.GetProperty("hills").TryGetProperty("terrace", out _));

            // A spot outside the play area is an error on its line
            var outside = LevelFile.Read(Level(spots: Spots.Replace("crash at=(6, 0)", "crash at=(66, 0)")));
            var problem = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(outside, "boss", materials, root.FullName));
            Assert.Equal(16, problem.Line);
            Assert.Contains("boss's crash", problem.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // ---- the Champion's Arena

    private static LevelDefinition Arena() => LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, "arena" + LevelFile.Extension));

    [Fact]
    public void ContentArena_IsAValidBossLevel_WithTheRegionsLayoutAndFillOfTheTicket()
    {
        var level = Arena();

        Assert.Equal(LevelLayout.Boss, level.Layout);
        Assert.Equal(new Vector2(120, 80), level.Play.Size);
        Assert.Equal((24f, 12f, 1.5f), (level.Hills.Width, level.Hills.Height, level.Hills.Terrace));
        Assert.Equal("sand", level.Base);
        Assert.Equal(["stands", "track", "field", "emblem"], level.Regions.Select(r => r.Name));
        Assert.All(level.Regions, r => Assert.Equal(0, r.Wobble));
        Assert.Equal("dusk", level.Sky?.Preset);
        Assert.Equal("arena-underside", level.Edge.Underside?.Model);
        foreach (var material in level.Regions.Select(r => r.Material).Append(level.BaseMaterial))
            Assert.True(File.Exists(Path.Combine(Content, "..", "materials", "ground", material + ".sdfmat")), material);
        // The placed pieces: 46 wall segments, 14 barriers, 4 floodlights, 5 pillars and 2 toppled, 4 shelters, the box with its two balconies
        Assert.Equal(46, level.Placements.Count(p => p.Model == "arena-wall"));
        Assert.Equal(14, level.Placements.Count(p => p.Model.StartsWith("arena-barrier", StringComparison.Ordinal)));
        Assert.Equal(4, level.Placements.Count(p => p.Model == "floodlight-mushroom"));
        Assert.Equal(5, level.Placements.Count(p => p.Model.StartsWith("arena-pillar", StringComparison.Ordinal) && p.Model != "arena-pillar-toppled"));
        Assert.Equal(4, level.Placements.Count(p => p.Model.StartsWith("shelter-toadstool", StringComparison.Ordinal)));
        Assert.Equal(new[] { "royal-box", "royal-box-balcony", "royal-box-balcony-fallen" }, level.Placements.Where(p => p.Model.StartsWith("royal-box", StringComparison.Ordinal)).Select(p => p.Model));
        // The Champion's Corner stands wholly inside the floor, turned to the field, with the red flag stand in the middle of it
        var corner = level.Placements.Single(p => p.Model == "champions-corner");
        Assert.Equal(90, corner.Yaw);
        Assert.Equal(corner.At, level.Battle!.West.Flag.At);
        var footprint = LevelModels.In(Path.Combine(Content, "models"))("champions-corner")!;
        Assert.True(corner.At.X - footprint.Max.Y >= -level.Play.HalfSize.X, "the corner reaches past the west edge");
        // The crowd: about 700 in the stands, the fans a block of about 60
        var crowd = level.Trees.Where(t => t.Group == "crowd").ToArray();
        Assert.Equal(2, crowd.Length);
        Assert.Equal("crowd-mushroom-blue-fan", crowd[0].Entries.Single().Model);
        Assert.NotNull(crowd[0].Area);
        Assert.All(crowd, c => Assert.True(c.Facing > 0));
        Assert.Contains(level.DrawGroups, g => g.Name == "crowd" && g.LowDensity == 0.6f && g.Rings.SequenceEqual([20, 50, 1000]));
    }

    [Fact]
    public void ContentArena_Bakes_WithTheCrowdOnTheTreads_AndEveryPlacementOnTheIsland()
    {
        var level = Arena();
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var materials = MaterialLibrary.Empty;
            foreach (var name in level.Regions.Select(r => r.Material).Append(level.BaseMaterial))
                materials = materials.With(name, MaterialDefinition.Default);
            var footprints = LevelModels.In(Path.Combine(Content, "models"));
            var exist = LevelModels.Exist(Path.Combine(Content, "models"));

            var baked = LevelBaker.Bake(level, "arena", materials, root.FullName, footprints, exist);

            var crowd = baked.Scattered.Instances.Where(i => baked.Scattered.Prototypes[i.Prototype].Group == "crowd").ToArray();
            Assert.InRange(crowd.Count(i => baked.Scattered.Prototypes[i.Prototype].Model == "crowd-mushroom-blue-fan"), 40, 90);
            Assert.InRange(crowd.Length, 600, 900);
            var terrain = new LevelTerrain(level);
            // On the treads, not the risers; up in the stands; every one faces the arena floor
            Assert.All(crowd, i =>
            {
                Assert.True(terrain.Normal(i.Position.X, i.Position.Z).Y > 0.7f);
                Assert.True(terrain.SignedDistance(i.Position.X, i.Position.Z) is >= 2 and <= 20);
                Assert.True(i.Position.Y >= 0.5f, "a crowd member stands above the floor, from the first tier up");
            });
            Assert.Equal(46, baked.Placements.Count(p => p.Model == "arena-wall"));
            // The balcony; the ramp, the three pillars of phases 1-2; the toppled pillars and the wreck of the Prime-copter (33.5)
            Assert.Equal(new[] { "1", "1-2", "1-2", "1-2", "2-3", "3", "3", "3" }, baked.Placements.Where(p => p.Phases is not null).Select(p => p.Phases!.Value.ToString()).Order(StringComparer.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ContentArena_IsFungal_NoModelFromAnotherTheme()
    {
        var level = Arena();
        var models = Path.Combine(Content, "models");
        var folderOf = Directory.GetFiles(models, "*.sdf", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension)
            .ToDictionary(g => g.Key!, g => g.Select(f => Path.GetRelativePath(models, Path.GetDirectoryName(f)!).Replace('\\', '/')).ToArray());

        var named = level.Placements.Select(p => p.Model)
            .Concat(level.Trees.SelectMany(t => t.Entries.Select(e => e.Model)))
            .Append(level.Edge.Underside!.Model)
            .Distinct().ToArray();

        Assert.NotEmpty(named);
        foreach (var model in named)
        {
            Assert.True(folderOf.TryGetValue(model, out var folders), $"{model} is not in content/models");
            Assert.All(folders!, folder =>
                Assert.True(folder.Split('/')[0] is "arena" or "fungi" or "rocks" or "terrain", $"{model} comes from {folder}, not the fungal arena's own, the fungal level's or the rocks"));
        }
    }

    [Fact]
    public void ContentArena_PlacedModels_DoNotOverlap_ExceptInsideAGroupOrBetweenPhases()
    {
        var path = Path.Combine(Content, LevelFile.FolderName, "arena" + LevelFile.Extension);
        var level = LevelFile.Load(path);
        var lines = File.ReadAllLines(path);
        var footprints = LevelModels.In(Path.Combine(Content, "models"));
        // A group is the last comment above a placement
        int GroupOf(int line)
        {
            for (var i = line - 1; i >= 0; i--)
                if (lines[i].TrimStart().StartsWith('#'))
                    return i;
            return -1;
        }
        Vector2[] Corners(Placement p)
        {
            var f = footprints(p.Model)!;
            return new[] { f.Min, new Vector2(f.Max.X, f.Min.Y), f.Max, new Vector2(f.Min.X, f.Max.Y) }.Select(c => p.At + Placement2D.ToLevel(c * p.Scale, p.Yaw)).ToArray();
        }
        bool Overlap(Placement a, Placement b)
        {
            var (first, second) = (Corners(a), Corners(b));
            foreach (var box in new[] { first, second })
            for (var i = 0; i < 4; i++)
            {
                var edge = box[(i + 1) % 4] - box[i];
                var normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
                var (p, q) = (first.Select(c => Vector2.Dot(c, normal)).ToArray(), second.Select(c => Vector2.Dot(c, normal)).ToArray());
                // A hand's breadth of slack: the footprints are the finder's coarse bounds
                if (MathF.Min(p.Max(), q.Max()) - MathF.Max(p.Min(), q.Min()) <= 0.3f)
                    return false;
            }
            return true;
        }

        var problems = new List<string>();
        var all = level.Placements;
        for (var i = 0; i < all.Count; i++)
        for (var j = i + 1; j < all.Count; j++)
        {
            var (a, b) = (all[i], all[j]);
            if (GroupOf(a.Line) == GroupOf(b.Line))
                continue;
            // Models of different phases are never there together
            if (a.Phases is { } pa && b.Phases is { } pb && (pa.Last < pb.First || pb.Last < pa.First))
                continue;
            if (Overlap(a, b))
                problems.Add($"{a.Model} (line {a.Line}) and {b.Model} (line {b.Line}) overlap");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ContentArena_EveryPlacedModelAndEveryBattleObject_IsInsideTheFloor()
    {
        var level = Arena();
        var footprints = LevelModels.In(Path.Combine(Content, "models"));

        foreach (var p in level.Placements.Where(p => p.Model is not ("arena-wall" or "royal-box" or "royal-box-balcony-fallen")))
        {
            var f = footprints(p.Model)!;
            var corners = new[] { f.Min, new Vector2(f.Max.X, f.Min.Y), f.Max, new Vector2(f.Min.X, f.Max.Y) }.Select(c => p.At + Placement2D.ToLevel(c * p.Scale, p.Yaw));
            Assert.All(corners, c => Assert.True(MathF.Abs(c.X) <= level.Play.HalfSize.X + 0.01f && MathF.Abs(c.Y) <= level.Play.HalfSize.Y + 0.01f, $"{p.Model} on line {p.Line} reaches ({c.X:0.0}, {c.Y:0.0})"));
        }
    }
}
