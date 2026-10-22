using System.Numerics;
using System.Text.RegularExpressions;
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
                team-red-sprig0 at=(-55, 7) y=5.5
            """);

        Assert.Equal(new Placement("castle0", new Vector2(-62, 0), 90, 1, 22, Placement.DefaultFlattenFalloff, "tiles", Placement.DefaultGroundMargin, 4),
            level.Placements[0]);
        Assert.Equal(new Placement("amanita-scout", new Vector2(3, 4), -45, 2, 0, Placement.DefaultFlattenFalloff, null, Placement.DefaultGroundMargin, 5),
            level.Placements[1]);
        // y lifts a model's base off the ground, onto another model (ticket 19); 0 by default
        Assert.Equal(0, level.Placements[0].Y);
        Assert.Equal(5.5f, level.Placements[2].Y);
    }

    [Fact]
    public void Read_PlacementBelowTheGround_IsAnError()
    {
        var error = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(
            """
            format 1
            play size=(150, 100)
            place
                rock1 at=(3, 4) y=-1
            """));

        Assert.Equal(4, error.Line);
        Assert.Contains("metres above the ground", error.Problem);
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
    public void Read_TakesTheBattleSides_AndTheNeutralPods()
    {
        var level = LevelFile.Read(
            """
            format 1
            play size=(150, 100)
            sides
                east
                    pod health at=(51, 11)
                    flag at=(61, 0) yaw=30
                    spawn at=(57, 0) yaw=-90
                    pod pistol at=(48, -6)
                    pod minigun at=(48, 6)
                west
                    spawn
                        at=(-57, 0)
                        yaw=90
                    flag at=(-61, 0) yaw=-30
                    pod minigun at=(-48, 6)
                    pod pistol at=(-48, -6)
                    pod health at=(-51, 11)
            neutral
                pod poison-gun at=(0, -30)
                pod electro-gun at=(0, 30)
            """);

        var battle = level.Battle!;
        Assert.Equal(("west", "east"), (battle.West.Name, battle.East.Name));
        Assert.Equal(new BattleObject(BattleKind.Spawn, null, new Vector2(-57, 0), 90, 11), battle.West.Spawn);
        Assert.Equal(new BattleObject(BattleKind.Flag, null, new Vector2(61, 0), 30, 6), battle.East.Flag);
        Assert.Equal(new BattleObject(BattleKind.Pod, "minigun", new Vector2(48, 6), 0, 9), battle.East.Weapons[0]);
        Assert.Equal(["spawn", "flag", "pod minigun", "pod pistol", "pod health"], battle.East.Objects.Select(o => o.Name));
        // The long-range pod first, whichever order the file gives them in
        Assert.Equal(["electro-gun", "poison-gun"], battle.Neutral.Select(p => p.Pod));
        Assert.Equal(BattleObject.PodRadius, battle.Neutral[0].Radius);
        Assert.Null(LevelFile.Read("format 1\nplay size=(10, 10)").Battle);
    }

    [Fact]
    public void Read_TakesMoreHealthPods_AfterTheFirst()
    {
        const string Side = "        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n";
        var level = LevelFile.Read("format 1\nplay size=(150, 100)\nsides\n    west\n" + Side +
                                   "        pod health at=(-28, -43)\n        pod health at=(-28, 43)\n        pod health at=(-73.5, -9)\n" +
                                   "    east\n" + Side + "        pod health at=(51, 11)\n" +
                                   "neutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)");

        var west = level.Battle!.West;
        Assert.Equal(new Vector2(-28, -43), west.Health.At);
        Assert.Equal([new Vector2(-28, 43), new Vector2(-73.5f, -9)], west.MoreHealth.Select(p => p.At));
        Assert.Equal(["spawn", "flag", "pod minigun", "pod pistol", "pod health", "pod health", "pod health"], west.Objects.Select(o => o.Name));
        Assert.Empty(level.Battle.East.MoreHealth);
    }

    [Fact]
    public void Read_TakesAnyWeaponPodsOnTheSides_InTheWeaponsOrder()
    {
        const string Weapons = "        pod poison-gun at=({0}66, -14)\n        pod electro-gun at=({0}66, 6)\n";
        var level = LevelFile.Read(BattleLevel(SideLines("west", -1, weapons: string.Format(Weapons, "-")),
            SideLines("east", 1, weapons: string.Format(Weapons, ""))));

        var battle = level.Battle!;
        // Kept in BattleLayout.Weapons' order, whichever order the file gives them in
        Assert.Equal(["electro-gun", "poison-gun"], battle.West.Weapons.Select(p => p.Pod));
        Assert.Equal(new Vector2(66, 6), battle.East.Weapons[0].At);
        Assert.Equal(["spawn", "flag", "pod electro-gun", "pod poison-gun", "pod health"], battle.West.Objects.Select(o => o.Name));
        // One weapon pod is enough, any of the seven
        var one = LevelFile.Read(BattleLevel(SideLines("west", -1, weapons: "        pod sniper at=(-48, 6)\n"),
            SideLines("east", 1, weapons: "        pod sniper at=(48, 6)\n"))).Battle!;
        Assert.Equal(["sniper"], one.East.Weapons.Select(p => p.Pod));
        var all = string.Concat(BattleLayout.Weapons.Select((w, i) => $"        pod {w} at=({{0}}40, {i * 4})\n"));
        var seven = LevelFile.Read(BattleLevel(SideLines("west", -1, weapons: string.Format(all, "-")), SideLines("east", 1, weapons: string.Format(all, "")))).Battle!;
        Assert.Equal(BattleLayout.Weapons, seven.West.Weapons.Select(p => p.Pod));
    }

    public static TheoryData<string, int, string> SideWeaponErrors => new()
    {
        // A weapon that is none, the health pod's place taken by nothing else
        { BattleLevel(SideLines("west", -1, weapons: "        pod cannon at=(-48, 6)\n"), SideLines("east", 1)), 7,
            "A side's pods are health and the weapons minigun, pistol, sniper, electro-gun, flamethrower, poison-gun, blob-gun; cannon is none of them" },
        // A weapon twice on one side
        { BattleLevel(SideLines("west", -1, weapons: "        pod minigun at=(-48, 6)\n        pod minigun at=(-48, -6)\n"), SideLines("east", 1)), 8,
            "Side west has its pod minigun twice, first on line 7" },
        // Sides that do not mirror: another weapon, one weapon fewer
        { BattleLevel(SideLines("west", -1), SideLines("east", 1, weapons: "        pod minigun at=(48, 6)\n        pod sniper at=(48, -6)\n")), 10,
            "The sides mirror each other, so they have the same weapon pods; west has minigun, pistol, east minigun, sniper" },
        { BattleLevel(SideLines("west", -1), SideLines("east", 1, weapons: "        pod minigun at=(48, 6)\n")), 10,
            "west has minigun, pistol, east minigun" },
        // No weapon pod at all, only health
        { BattleLevel(SideLines("west", -1, weapons: ""), SideLines("east", 1)), 4,
            "Side west has no weapon pod; a side has one or more" },
    };

    [Theory]
    [MemberData(nameof(SideWeaponErrors))]
    public void Read_ChecksTheSidesWeaponPods(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Read_TakesASpawnZonesRadius_ThreeMetresWithoutOne()
    {
        const string Side = "        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\n";
        var level = LevelFile.Read("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) yaw=90 radius=2.5\n" + Side +
                                   "    east\n        spawn at=(57, 0) yaw=-90\n" + Side.Replace("(-", "(") +
                                   "neutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)\n");

        var battle = level.Battle!;
        Assert.Equal(2.5f, battle.West.Spawn.ZoneRadius);
        Assert.Equal(2.5f, battle.West.Spawn.Radius);
        Assert.Equal(BattleObject.DefaultSpawnZone, battle.East.Spawn.ZoneRadius);
        Assert.Equal(3f, battle.East.Spawn.Radius);
        // A flag or a pod takes up its own foot, whatever its zone would be
        Assert.Equal(BattleObject.FlagRadius, battle.West.Flag.Radius);
    }

    /// <summary>
    /// A side's lines (ticket 17's five), at x mirrored by <paramref name="sign"/>, with <paramref name="extra"/> after them; with
    /// <paramref name="weapons"/>, those weapon pod lines in place of the minigun and the pistol.
    /// </summary>
    private static string SideLines(string name, float sign, string extra = "", string? weapons = null)
    {
        string X(float x) => (sign * x).ToString(System.Globalization.CultureInfo.InvariantCulture);
        weapons ??= $"        pod minigun at=({X(48)}, 6)\n        pod pistol at=({X(48)}, -6)\n";
        return $"    {name}\n        spawn at=({X(57)}, 0) yaw={X(90)}\n        flag at=({X(61)}, 0)\n" + weapons +
               $"        pod health at=({X(51)}, 11)\n" + extra;
    }

    /// <summary>Ticket 24's machine lines of a side at x mirrored by <paramref name="sign"/>: <paramref name="turrets"/> turrets, and the
    /// machine zone when <paramref name="zone"/>.</summary>
    private static string MachineLines(float sign, int turrets = 4, bool zone = true, string zoneExtra = " radius=2.5")
    {
        string X(float x) => (sign * x).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lines = string.Concat(Enumerable.Range(0, turrets).Select(i => $"        turret at=({X(45)}, {i * 5 - 8}) yaw={X(90)}\n"));
        return lines + (zone ? $"        machines at=({X(70)}, 20) yaw={X(90)}{zoneExtra}\n" : "");
    }

    private static string BattleLevel(string west, string east) =>
        "format 1\nplay size=(150, 100)\nsides\n" + west + east + "neutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)\n";

    [Fact]
    public void Read_TakesEachSidesTurretsAndMachineZone_AndLevelsWithoutMachines()
    {
        var level = LevelFile.Read(BattleLevel(SideLines("west", -1, MachineLines(-1)), SideLines("east", 1, MachineLines(1, zoneExtra: ""))));

        var battle = level.Battle!;
        Assert.True(battle.HasMachines);
        // Lines 4-9 the west side's first six, then its four turrets and the zone
        Assert.Equal(Enumerable.Range(0, 4).Select(i => new BattleObject(BattleKind.Turret, null, new Vector2(-45, i * 5 - 8), -90, 10 + i)),
            battle.West.Turrets);
        Assert.Equal(new BattleObject(BattleKind.Machines, null, new Vector2(-70, 20), -90, 14) { ZoneRadius = 2.5f }, battle.West.MachineSpawn);
        Assert.Equal(2.5f, battle.West.MachineSpawn!.Radius);
        Assert.Equal(BattleObject.DefaultMachineZone, battle.East.MachineSpawn!.ZoneRadius);
        Assert.Equal(BattleObject.TurretRadius, battle.East.Turrets[0].Radius);
        Assert.Equal(["spawn", "flag", "pod minigun", "pod pistol", "pod health", "turret", "turret", "turret", "turret", "machines"],
            battle.East.Objects.Select(o => o.Name));
        Assert.Equal(22, battle.Objects.Count());

        // A level without machines is a level as before
        var plain = LevelFile.Read(BattleLevel(SideLines("west", -1), SideLines("east", 1))).Battle!;
        Assert.False(plain.HasMachines);
        Assert.Empty(plain.West.Turrets);
        Assert.Null(plain.East.MachineSpawn);
        Assert.Equal(5, plain.West.Objects.Count);
    }

    public static TheoryData<string, int, string> MachineErrors => new()
    {
        // Three turrets, five turrets
        { BattleLevel(SideLines("west", -1, MachineLines(-1, turrets: 3)), SideLines("east", 1, MachineLines(1))), 4,
            "Side west has 3 turrets; a side with machines has exactly 4 'turret' lines" },
        { BattleLevel(SideLines("west", -1, MachineLines(-1, turrets: 5)), SideLines("east", 1, MachineLines(1))), 14,
            "Side west has 4 turrets already (lines 10, 11, 12, 13); a side has exactly 4" },
        // Turrets without a zone, a zone without turrets, a zone twice
        { BattleLevel(SideLines("west", -1, MachineLines(-1, zone: false)), SideLines("east", 1, MachineLines(1))), 4,
            "Side west has turrets but no machine zone" },
        { BattleLevel(SideLines("west", -1, MachineLines(-1, turrets: 0)), SideLines("east", 1, MachineLines(1))), 4,
            "Side west has 0 turrets; a side with machines has exactly 4 'turret' lines" },
        { BattleLevel(SideLines("west", -1, MachineLines(-1) + "        machines at=(-70, -20)\n"), SideLines("east", 1, MachineLines(1))), 15,
            "Side west has its machines twice, first on line 14" },
        // Machines on one side only
        { BattleLevel(SideLines("west", -1, MachineLines(-1)), SideLines("east", 1)), 15,
            "Side west has machines (turrets and a machine zone), so side east needs them too: 4 'turret' lines and a 'machines' line" },
        { BattleLevel(SideLines("west", -1), SideLines("east", 1, MachineLines(1))), 4, "so side west needs them too" },
        // The parameters: a zone's radius in range, a turret with no radius
        { BattleLevel(SideLines("west", -1, MachineLines(-1, zoneExtra: " radius=9")), SideLines("east", 1, MachineLines(1))), 14,
            "'radius' of machines is 9, but needs metres, 1.5 to 8" },
        { BattleLevel(SideLines("west", -1, MachineLines(-1, zoneExtra: " scale=2")), SideLines("east", 1, MachineLines(1))), 14,
            "Machines has no parameter 'scale'; its parameters are at, yaw, radius" },
        { BattleLevel(SideLines("west", -1, "        turret at=(-45, 0) radius=2\n"), SideLines("east", 1, MachineLines(1))), 10,
            "Turret has no parameter 'radius'; its parameters are at, yaw" },
        { BattleLevel(SideLines("west", -1, "        turret yaw=90\n"), SideLines("east", 1, MachineLines(1))), 10, "Turret is missing parameter 'at'" },
        // Machines belong to a side, not between them
        { "format 1\nplay size=(150, 100)\nneutral\n    turret at=(0, 32)", 4, "Expected 'pod' in the neutral pods, found 'turret'" },
    };

    [Theory]
    [MemberData(nameof(MachineErrors))]
    public void Read_ChecksTheMachines(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
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
        Assert.Equal(["path", "tiles"], level.Regions.Select(r => r.Name));
        // A castle on each short side, on a plateau of grass, tiled only under its footprint
        var castles = level.Placements.Where(p => p.Model == "castle1").ToArray();
        Assert.Equal([-62f, 62f], castles.Select(c => c.At.X).Order());
        Assert.All(castles, c => Assert.Equal(("tiles", 0f, true), (c.Ground, c.GroundMargin, c.Flatten > 0)));
        // Grass turfs and flowers, off the path and tiles, drawn in the foliage group
        Assert.Equal(["grass-turf0", "flower0"], level.Scatters.Select(s => s.Entries[0].Model));
        Assert.All(level.Scatters, s => Assert.Equal("foliage", s.Group));
        Assert.All(level.Scatters, s => Assert.Equal(["tiles", "path"], s.Avoid));
        Assert.Equal([6f, 15f, 35f], level.DrawGroups.Single(g => g.Name == "foliage").Rings);
    }

    [Fact]
    public void ContentMeadow_HasNoFungi()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "meadow" + LevelFile.Extension));
        var models = Path.Combine(content, "models");
        var partsFolder = Path.Combine(content, ModelFiles.PartsFolderName);
        var files = Directory.GetFiles(models, "*.sdf", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension)
            .ToDictionary(g => g.Key!, g => g.ToArray());

        var named = level.Placements.Select(p => p.Model)
            .Concat(level.Scatters.SelectMany(s => s.Entries.Select(e => e.Model)))
            .Concat(level.Trees.SelectMany(t => t.Entries.Select(e => e.Model)))
            .Distinct()
            .ToArray();
        Assert.NotEmpty(named);
        Assert.DoesNotContain("castle0", named);

        var materials = new SortedSet<string>();
        var parts = new HashSet<string>();
        var pending = new Stack<string>();
        foreach (var model in named)
        {
            Assert.True(files.TryGetValue(model, out var paths), $"{model} is not in content/models");
            foreach (var path in paths!)
            {
                var top = Path.GetRelativePath(models, path).Replace('\\', '/').Split('/')[0];
                Assert.DoesNotContain(top, new[] { "mushrooms", "fungi", "chess" });
                pending.Push(path);
            }
        }
        while (pending.Count > 0)
        {
            var text = File.ReadAllText(pending.Pop());
            foreach (Match m in Regex.Matches(text, @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline))
                materials.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"^\s*Part\s+part=(\S+)", RegexOptions.Multiline))
                if (parts.Add(m.Groups[1].Value))
                    foreach (var file in Directory.EnumerateFiles(partsFolder, m.Groups[1].Value + ".sdf", SearchOption.AllDirectories))
                        pending.Push(file);
        }
        Assert.NotEmpty(materials);
        Assert.All(materials, m => Assert.False(m.StartsWith("amanita_") || m.StartsWith("game_") || m.StartsWith("chess_"), m));
        Assert.NotEqual("night", level.Sky?.Preset);
        Assert.Equal("day", level.Sky?.Preset);
    }

    /// <summary>
    /// Ticket 27.9: the toy box is one theme, toys. Every model it names (placed, scattered, strewn, or a train car) is in
    /// models/toys/, and the toy team's machines are in models/machines/; no material, in them or in their parts, is from another
    /// theme's folder: only toys/, machines/, the machines' eyes (the scouts' floating eyes), the team pennants' cloth in buildings/, and the ground and room materials.
    /// </summary>
    [Fact]
    public void ContentToyBox_HasOnlyToys()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "toybox" + LevelFile.Extension));
        var models = Path.Combine(content, "models");
        var partsFolder = Path.Combine(content, ModelFiles.PartsFolderName);
        var files = Directory.GetFiles(models, "*.sdf", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension)
            .ToDictionary(g => g.Key!, g => g.ToArray());

        var named = level.Placements.Select(p => p.Model)
            .Concat(level.Scatters.SelectMany(s => s.Entries.Select(e => e.Model)))
            .Concat(level.Trees.SelectMany(t => t.Entries.Select(e => e.Model)))
            .Concat(level.Movers.SelectMany(m => m.Cars.Select(c => c.Model)))
            .Distinct()
            .ToArray();
        Assert.NotEmpty(named);
        Assert.Equal("toy", level.MachineSetName);
        // The toy machines: every piece of each kind and team, named as the set rule says
        var machines = Directory.GetFiles(Path.Combine(models, "machines"), "*-toy-*.sdf").Select(Path.GetFileNameWithoutExtension).ToArray();
        Assert.Equal(18, machines.Length);

        var toys = Path.Combine(models, "toys");
        var pending = new Stack<string>();
        foreach (var model in named)
        {
            Assert.True(files.TryGetValue(model, out var paths), $"{model} is not in content/models");
            Assert.Single(paths!);
            Assert.True(Path.GetDirectoryName(paths![0]) == toys, $"{model} is not from models/toys/: {paths[0]}");
            pending.Push(paths[0]);
        }
        foreach (var model in machines)
            pending.Push(files[model!].Single());

        var materials = new SortedSet<string>();
        var parts = new HashSet<string>();
        while (pending.Count > 0)
        {
            var text = File.ReadAllText(pending.Pop());
            foreach (Match m in Regex.Matches(text, @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline))
                materials.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"^\s*Part\s+part=(\S+)", RegexOptions.Multiline))
                if (parts.Add(m.Groups[1].Value))
                    foreach (var file in Directory.EnumerateFiles(partsFolder, m.Groups[1].Value + ".sdf", SearchOption.AllDirectories))
                    {
                        Assert.Contains(Path.GetRelativePath(partsFolder, file).Replace('\\', '/').Split('/')[0], new[] { "toys", "machines" });
                        pending.Push(file);
                    }
        }
        Assert.NotEmpty(materials);
        var materialFolders = Directory.GetFiles(Path.Combine(content, "..", "materials"), "*.sdfmat", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, f => Path.GetFileName(Path.GetDirectoryName(f))!);
        foreach (var material in materials)
        {
            Assert.True(materialFolders.TryGetValue(material, out var folder), $"{material} has no material file");
            Assert.True(folder is "toys" or "machines" or "eye" || (folder == "buildings" && material is "team_red_cloth" or "team_blue_cloth"),
                $"{material} is in materials/{folder}/, not a toy material");
        }
        // The floor and the room: their own materials, no other theme's ground
        Assert.Equal("felt", level.Base);
        Assert.Equal(["playmat", "road", "foam"], level.Regions.Select(r => r.Name));
        Assert.Equal(("toybox-wall", "toybox-lid"), (level.Room!.Walls, level.Room.Ceiling));
        Assert.Null(level.Sky);
        Assert.Equal(0, level.Hills.Height);
    }

    /// <summary>
    /// Every solid toy has a `collider` line (a comment about one does not make a collider: the prefab has none without the line, and a
    /// scene built from it has nothing to bump into); the ground clutter, the track's curves and the pennants have none.
    /// </summary>
    [Fact]
    public void ContentToyBox_SolidToysHaveColliders()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        string[] flat = ["puzzle-piece", "confetti", "brick-plate", "bead", "crayon-stub", "track-curve", "pennant"];
        foreach (var file in Directory.GetFiles(Path.Combine(content, "models", "toys"), "*.sdf"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var line = Regex.Match(File.ReadAllText(file), @"^collider (\w+)", RegexOptions.Multiline);
            if (flat.Any(name.StartsWith))
                Assert.True(!line.Success || line.Groups[1].Value == "none", $"{name} is flat and should have no collider");
            else
                Assert.True(line.Success && line.Groups[1].Value is "convex" or "mesh", $"{name} is solid and has no `collider convex|mesh` line");
        }
    }

    [Fact]
    public void ContentFungal_IsAValidLevel()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "fungal" + LevelFile.Extension));

        Assert.Equal(new Vector2(150, 100), level.Play.Size);
        Assert.InRange(level.Hills.Height, 8, 12);
        // Dirt outside the regions, and only the trail, plain
        Assert.Equal("dirt", level.Base);
        Assert.Equal(["trail"], level.Regions.Select(r => r.Name));
        Assert.All(level.Regions, r => Assert.Equal(GroundPattern.Plain, r.Pattern));
        // A castle on each short side, on a plateau with nothing painted round it, so the fungi grow up to its walls
        var castles = level.Placements.Where(p => p.Model == "castle0").ToArray();
        Assert.Equal([-62f, 62f], castles.Select(c => c.At.X).Order());
        Assert.All(castles, c => Assert.Equal((null, true), (c.Ground, c.Flatten > 0)));
        // Moss turfs and fungi tufts, off the trail only
        Assert.Equal(["moss-turf0", "fungi-tuft0"], level.Scatters.Select(s => s.Entries[0].Model));
        Assert.All(level.Scatters, s => Assert.Equal(["trail"], s.Avoid));
        // The ground materials exist, plain
        foreach (var region in level.Regions.Select(r => r.Material).Append("ground_dirt"))
            Assert.True(File.Exists(Path.Combine(content, "..", "materials", "ground", region + ".sdfmat")), region);
    }

    [Fact]
    public void ContentFungal_HasNothingButFungi()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "fungal" + LevelFile.Extension));
        var models = Path.Combine(content, "models");
        var folderOf = Directory.GetFiles(models, "*.sdf", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension)
            .ToDictionary(g => g.Key!, g => g.Select(f => Path.GetRelativePath(models, Path.GetDirectoryName(f)!).Replace('\\', '/')).ToArray());

        var named = level.Placements.Select(p => p.Model)
            .Concat(level.Scatters.SelectMany(s => s.Entries.Select(e => e.Model)))
            .Concat(level.Trees.SelectMany(t => t.Entries.Select(e => e.Model)))
            .Distinct()
            .ToArray();
        Assert.NotEmpty(named);
        foreach (var model in named)
        {
            Assert.True(folderOf.TryGetValue(model, out var folders), $"{model} is not in content/models");
            Assert.All(folders!, folder =>
            {
                var top = folder.Split('/')[0];
                Assert.True(model == "castle0" || top is "fungi" or "mushrooms" or "rocks" or "terrain", $"{model} comes from {folder}");
                Assert.DoesNotContain(top, new[] { "grass", "flowers", "trees" });
            });
        }
    }

    [Fact]
    public void ContentChessboard_IsAFlatCheckerboard_WithPiecesOfBothColoursOnIt()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension));

        Assert.Equal((new Vector2(160, 160), 0f), (level.Play.Size, level.Play.NoiseAmplitude));
        // 20 × 20 squares of 8 m, the corner squares dark, and nothing round the board: the room's walls stand on its edges
        Assert.Equal(["light", "dark"], level.Regions.Select(r => r.Name));
        Assert.Equal(HillRing.None, level.Hills);
        Assert.Equal(IslandEdge.None, level.Edge);
        // The fill: the clutter scattered in group tokens and kept off the walls (reach 0), the strewn pieces in group pieces inside the
        // walls, each group drawn
        Assert.NotEmpty(level.Scatters);
        Assert.All(level.Scatters, s => Assert.Equal(("tokens", 0f), (s.Group, s.Reach)));
        Assert.NotEmpty(level.Trees);
        Assert.All(level.Trees, t => Assert.Equal("pieces", t.Group));
        Assert.All(level.Trees, t => Assert.True(t.Band!.Value.Y < 0, $"the trees section on line {t.Line} reaches under the walls"));
        Assert.Equal(["tokens", "pieces"], level.DrawGroups.Select(g => g.Name));
        Assert.Null(level.Sky);
        Assert.Equal(new RoomSpec(new Vector3(160, 160, 160), 8), level.Room);
        var regions = new GroundRegions(level, _ => throw new InvalidOperationException("No placement paints the ground"));
        for (var i = 0; i < 20; i++)
        for (var j = 0; j < 20; j++)
        {
            var centre = new Vector2(-76 + 8 * i, -76 + 8 * j);
            Assert.Equal((i + j) % 2 == 0 ? "dark" : "light", regions.At(centre)?.Name);
            // Just over the edge to the next square along x, or off the board past the last
            Assert.Equal(i == 19 ? null : (i + j) % 2 == 0 ? "light" : "dark", regions.At(centre + new Vector2(4.2f, 0))?.Name);
        }
        Assert.Null(regions.At(new Vector2(81.5f, 0)));
        // Every piece on the board, both colours of every kind among them, each model in content/models
        var models = LevelModels.In(Path.Combine(content, "models"));
        Assert.InRange(level.Placements.Count, 60, 260);
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
    public void ContentChessboard_GamePieces_UseEveryGamesModelOnTheBoard()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var level = LevelFile.Load(Path.Combine(content, LevelFile.FolderName, "chessboard" + LevelFile.Extension));

        var games = Directory.GetFiles(Path.Combine(content, "models", "games"), "*.sdf").Select(Path.GetFileNameWithoutExtension).ToList();
        // The twenty pieces of the first board are each placed by hand at least once
        string[] first =
        [
            "die-red", "die-red-tumbled", "die-ivory", "die-d8", "monopoly-house", "monopoly-house-toppled", "monopoly-hotel", "domino-chain",
            "domino-5-5-standing", "domino-6-3-lying", "domino-2-6-side", "meeple-standing", "meeple-fallen", "poker-chip-stack",
            "poker-chip-rolling", "card-house", "card-face-down", "card-three-lying", "card-king-stuck", "card-ace-held",
        ];
        Assert.Subset(games.ToHashSet(), first.ToHashSet());
        foreach (var model in first)
            Assert.Contains(level.Placements, p => p.Model == model);
        // Every other one, the fill's clutter, pieces and landmarks, is placed or scattered
        var scattered = level.Scatters.Concat(level.Trees).SelectMany(s => s.Entries).Select(e => e.Model).ToHashSet();
        foreach (var model in games)
            Assert.True(level.Placements.Any(p => p.Model == model) || scattered.Contains(model), $"{model} is on the board nowhere");
        // Each placed one wholly inside the walls
        var footprints = LevelModels.In(Path.Combine(content, "models"));
        Assert.All(level.Placements.Where(p => games.Contains(p.Model)), p =>
        {
            var footprint = footprints(p.Model)!;
            foreach (var corner in new[] { footprint.Min, footprint.Max, new Vector2(footprint.Min.X, footprint.Max.Y), new Vector2(footprint.Max.X, footprint.Min.Y) })
            {
                var at = p.At + Placement2D.ToLevel(corner * p.Scale, p.Yaw);
                Assert.True(MathF.Abs(at.X) < 80 && MathF.Abs(at.Y) < 80, $"{p.Model} on line {p.Line} is off the board");
            }
        });
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
            // Within a group, their turned footprints themselves: the domino run's tiles stand closer than their circles allow
            var depth = a.Game && b.Game && a.Group == b.Group
                ? BoxOverlap(a.Placement, b.Placement, footprints)
                : a.Radius + b.Radius - Vector2.Distance(a.Centre, b.Centre);
            if (depth > allowed)
                problems.Add($"{a.Placement.Model} (line {a.Placement.Line}) and {b.Placement.Model} (line {b.Placement.Line}) overlap by {depth:0.00} m, " +
                             $"allowed {allowed:0.00} m");
        }
        return problems;
    }

    /// <summary>How far two placements' turned footprints sink into each other, the least along any of their edges' normals; 0 when apart.</summary>
    private static float BoxOverlap(Placement a, Placement b, Func<string, ModelFootprint?> footprints)
    {
        Vector2[] Corners(Placement p)
        {
            var f = footprints(p.Model)!;
            return new[] { f.Min, new Vector2(f.Max.X, f.Min.Y), f.Max, new Vector2(f.Min.X, f.Max.Y) }
                .Select(c => p.At + Placement2D.ToLevel(c * p.Scale, p.Yaw)).ToArray();
        }

        var (first, second) = (Corners(a), Corners(b));
        var least = float.MaxValue;
        foreach (var box in new[] { first, second })
        for (var i = 0; i < 4; i++)
        {
            var edge = box[(i + 1) % 4] - box[i];
            var normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            var (p, q) = (first.Select(c => Vector2.Dot(c, normal)).ToArray(), second.Select(c => Vector2.Dot(c, normal)).ToArray());
            var overlap = MathF.Min(p.Max(), q.Max()) - MathF.Max(p.Min(), q.Min());
            if (overlap <= 0)
                return 0;
            least = MathF.Min(least, overlap);
        }
        return least;
    }

    [Fact]
    public void ContentChessPieces_HaveNoEyesOrHands()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);

        // 21 pieces: six kinds in both colours, plus the fallen variants and the two halves of the giant fallen king
        var pieces = Directory.GetFiles(Path.Combine(content, "models", "chess"), "*.sdf");
        Assert.Equal(21, pieces.Length);
        Assert.All(pieces, file =>
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("part=eye", StringComparison.OrdinalIgnoreCase), Path.GetFileName(file));
            Assert.False(text.Contains("part=hand", StringComparison.OrdinalIgnoreCase), Path.GetFileName(file));
            // board games only: no mushroom cap, stem or gills materials
            Assert.DoesNotMatch(@"material=\S*(cap|stem|gills)\b", text);
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
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\n    east\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\n", 3, "A level with battle sides has neutral pods between them, and the other way round; add a 'neutral' section")]
    [InlineData("format 1\nplay size=(150, 100)\nneutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)", 3, "add a 'sides' section")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\nneutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)", 3, "The sides need both west and east indented below them; east is missing")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\n    north\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\nneutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)", 10, "The sides are west and east; there is no side 'north'")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\n    west\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\nneutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)", 10, "Side west is given twice, first on line 4")]
    [InlineData("format 1\nplay size=(150, 100)\nsides extra=1\n    west\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\n    east\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\nneutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)", 3, "Unexpected 'extra=1'")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0)\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n    east\n        spawn at=(-57, 0) yaw=90\n        flag at=(-61, 0)\n        pod minigun at=(-48, 6)\n        pod pistol at=(-48, -6)\n        pod health at=(-51, 11)\nneutral\n    pod sniper at=(0, 32)\n    pod flamethrower at=(0, -32)", 4, "Side west has no pod health; a side has a spawn, a flag, one or more weapon pods and one or more health pods")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0)\n        spawn at=(-58, 0)\n", 6, "Side west has its spawn twice, first on line 5")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        pod sniper at=(-57, 0)\n", 4, "Side west has no spawn")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        pod rocket at=(-57, 0)\n", 5, "A side's pods are health and the weapons minigun, pistol, sniper, electro-gun, flamethrower, poison-gun, blob-gun; rocket is none of them")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        tower at=(-57, 0)\n", 5, "Expected 'spawn', 'flag', 'pod', 'turret' or 'machines' in side west, found 'tower'")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        flag yaw=3\n", 5, "Flag is missing parameter 'at'")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        pod minigun at=(-48, 6) scale=2\n", 5, "Pod minigun has no parameter 'scale'; its parameters are at, yaw")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        pod minigun at=(-48, 6) radius=2\n", 5, "Pod minigun has no parameter 'radius'; its parameters are at, yaw")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) radius=12\n", 5, "'radius' of spawn is 12, but needs metres, 0.5 to 10")]
    [InlineData("format 1\nplay size=(150, 100)\nsides\n    west\n        spawn at=(-57, 0) scale=2\n", 5, "Spawn has no parameter 'scale'; its parameters are at, yaw, radius")]
    [InlineData("format 1\nplay size=(150, 100)\nneutral\n    pod sniper at=(0, 32)\n    pod electro-gun at=(0, -32)", 5, "The neutral pods have one long-range pod, but sniper is on line 4 already")]
    [InlineData("format 1\nplay size=(150, 100)\nneutral\n    pod blob-gun at=(0, 32)\n    pod poison-gun at=(0, -32)", 5, "The neutral pods have one short-range pod, but blob-gun is on line 4 already")]
    [InlineData("format 1\nplay size=(150, 100)\nneutral\n    pod sniper at=(0, 32)", 3, "The neutral pods need one of flamethrower, poison-gun, blob-gun indented below them")]
    [InlineData("format 1\nplay size=(150, 100)\nneutral\n    pod minigun at=(0, 32)", 4, "The neutral pods are one of sniper, electro-gun and one of flamethrower, poison-gun, blob-gun; minigun is none of them")]
    [InlineData("format 1\nplay size=(150, 100)\nneutral\n    spawn at=(0, 32)", 4, "Expected 'pod' in the neutral pods, found 'spawn'")]
    public void Read_ExplainsWhatIsWrongAndWhere(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }
}
