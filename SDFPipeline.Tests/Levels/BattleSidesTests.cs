using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

/// <summary>
/// Ticket 17.3: every level's battle sides and neutral pods. Each level has both sides, complete and mirrored across x = 0; nothing
/// stands in a placed model (seen from its shape, so a flag stand in a castle's courtyard is fine and one in its wall is not), on
/// another battle object, off the playable area or on the path's middle; the scatter keeps clear of them; the bake writes them.
/// </summary>
public class BattleSidesTests
{
    private static readonly string Content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;

    /// <summary>How high over its ground a battle object needs open air: a player, a floating weapon, a flag on its pole.</summary>
    private const float Headroom = 2.3f;

    /// <summary>
    /// Where the check for models starts, over the object's ground: above a pod's top (0.3 m) and a flag stand's. What is lower lies
    /// inside the pod or stand, or under a player's feet: the fungal castle's mycelium threads, flat on the ground with no collider.
    /// </summary>
    private const float Foot = 0.35f;

    /// <summary>The least room round a battle object's edge, in metres, to a model or to another battle object.</summary>
    private const float Gap = 0.2f;

    private static readonly string[] LevelNames = ["meadow", "fungal", "chessboard"];

    public static TheoryData<string> Levels => new(LevelNames);

    private static LevelDefinition Load(string name) => LevelFile.Load(Path.Combine(Content, LevelFile.FolderName, name + LevelFile.Extension));

    [Fact]
    public void EveryContentLevel_IsInTheList()
    {
        var all = LevelFile.All(Path.Combine(Content, LevelFile.FolderName)).Select(LevelFile.NameOf).Order(StringComparer.Ordinal);

        Assert.Equal(LevelNames.Order(StringComparer.Ordinal), all);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void Level_HasBothSides_Complete_AndMirroredAcrossX(string name)
    {
        var battle = Load(name).Battle;

        Assert.NotNull(battle);
        Assert.Equal([BattleSide.West, BattleSide.East], battle.Sides.Select(s => s.Name));
        Assert.Equal([BattleKind.Spawn, BattleKind.Flag, BattleKind.Pod, BattleKind.Pod, BattleKind.Pod], battle.West.Objects.Select(o => o.Kind));
        Assert.Equal([null, null, "minigun", "pistol", "health"], battle.West.Objects.Select(o => o.Pod));
        Assert.Equal(battle.West.Objects.Select(o => o.Name), battle.East.Objects.Select(o => o.Name));
        foreach (var (west, east) in battle.West.Objects.Zip(battle.East.Objects))
        {
            Assert.True(west.At.X < 0, $"{name}: the west {west.Name} is at x = {west.At.X}");
            Assert.Equal(new Vector2(-west.At.X, west.At.Y), east.At);
            Assert.Equal(0, ((west.Yaw + east.Yaw) % 360 + 360) % 360, 3);
        }
        // Each spawn faces the other side
        Assert.InRange(battle.West.Spawn.Yaw, 45, 135);

        Assert.Equal(2, battle.Neutral.Count);
        Assert.Single(battle.Neutral, p => BattleLayout.LongRange.Contains(p.Pod));
        Assert.Single(battle.Neutral, p => BattleLayout.ShortRange.Contains(p.Pod));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void BattleObjects_StandInThePlayArea_ClearOfEachOther_AndOffThePathsMiddle(string name)
    {
        var level = Load(name);
        var objects = level.Battle!.Objects.ToArray();
        var footprints = LevelModels.In(Path.Combine(Content, "models"));
        var regions = new GroundRegions(level, model => footprints(model)!);

        var problems = new List<string>();
        foreach (var item in objects)
        {
            if (MathF.Abs(item.At.X) > level.Play.HalfSize.X - item.Radius || MathF.Abs(item.At.Y) > level.Play.HalfSize.Y - item.Radius)
                problems.Add($"the {item.Name} on line {item.Line} is not wholly in the playable area");
            // A path's or trail's middle is where the players run; its edge may pass under a pod
            for (var index = 0; index < regions.Regions.Count; index++)
                if (regions.Regions[index].Name is "path" or "trail" && regions.Distance(index, item.At) < item.Radius - 0.5f)
                    problems.Add($"the {item.Name} on line {item.Line} stands on the {regions.Regions[index].Name}");
        }
        for (var i = 0; i < objects.Length; i++)
        for (var j = i + 1; j < objects.Length; j++)
            if (Vector2.Distance(objects[i].At, objects[j].At) < objects[i].Radius + objects[j].Radius + 2 * Gap)
                problems.Add($"the {objects[i].Name} on line {objects[i].Line} and the {objects[j].Name} on line {objects[j].Line} are too close");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void BattleObjects_DoNotOverlapAPlacedModel(string name)
    {
        var level = Load(name);

        var problems = OverlapProblems(level, level.Battle!.Objects);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Ticket 20: each side's spawn zone, where its players appear anywhere at random, is level ground: nobody spawns on a slope or a
    /// step. Its openness (no wall, keep or stairs in it) is <see cref="BattleObjects_DoNotOverlapAPlacedModel"/>'s, as a spawn's
    /// footprint is its whole zone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Levels))]
    public void SpawnZones_LieOnLevelGround(string name)
    {
        var level = Load(name);
        var terrain = new LevelTerrain(level);

        foreach (var side in level.Battle!.Sides)
        {
            var zone = side.Spawn;
            var heights = new List<float>();
            for (var x = -zone.ZoneRadius; x <= zone.ZoneRadius; x += 0.25f)
            for (var z = -zone.ZoneRadius; z <= zone.ZoneRadius; z += 0.25f)
                if (x * x + z * z <= zone.ZoneRadius * zone.ZoneRadius)
                    heights.Add(terrain.Height(zone.At.X + x, zone.At.Y + z));

            Assert.True(heights.Max() - heights.Min() < 0.2f,
                $"{name}: the {side.Name} spawn zone (radius {zone.ZoneRadius}) rises {heights.Max() - heights.Min():0.00} m from its lowest point to its highest");
        }
    }

    [Fact]
    public void OverlapCheck_FindsAFlagStandMovedIntoACastleWall_AndAPodMovedOntoARock()
    {
        var level = Load("meadow");
        var castle = level.Placements.First(p => p.Model == "castle1");
        var rock = level.Placements.First(p => p.Model.StartsWith("rock", StringComparison.Ordinal));
        // The castle's gate bay is 7.9 m from its middle towards its front (+z), turned by its yaw; the bay 3.5 m along from it
        var wall = castle.At + Placement2D.ToLevel(new Vector2(3.48f, 7.56f), castle.Yaw);

        var problems = OverlapProblems(level, [
            new BattleObject(BattleKind.Flag, null, wall, 0, 1),
            new BattleObject(BattleKind.Pod, "minigun", rock.At, 0, 2),
            level.Battle!.West.Flag,
        ]);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("line 1") && p.Contains("castle1"));
        Assert.Contains(problems, p => p.Contains("line 2") && p.Contains(rock.Model));
    }

    [Fact]
    public void Scatter_KeepsClearOfTheBattleObjects_AndLeavesTheRestWhereItWas()
    {
        var play = new PlayArea(new Vector2(40, 30), 0.2f, 14);
        var grass = new ScatterLayer([new ScatterEntry("turf", 1, 2)], "foliage", 1, 1, 6, 0.4f, [], 0.5f, 90, 3, ScatterLayer.DefaultScale, 1);
        var trees = new ScatterLayer([new ScatterEntry("tree", 1, 3)], "trees", 0.1f, 1, 8, 2, [], 1, 90, 0, ScatterLayer.DefaultScale, 4)
            { Band = new Vector2(-15, -2) };
        var open = new LevelDefinition(play, 3, HillRing.None, IslandEdge.None) { Scatters = [grass], Trees = [trees] };
        var battle = Battle(new Vector2(-12, 0), new Vector2(3, 8), new Vector2(3, -8));
        var level = open with { Battle = battle };
        ModelFootprint Rock(string _) => new(new Vector2(-1), new Vector2(1));
        ScatteredLevel Scatter(LevelDefinition l) =>
            LevelScatter.Scatter(l, new LevelTerrain(l), new GroundRegions(l, Rock), Rock, _ => Vector3.One);

        var cleared = Scatter(level);
        var all = Scatter(open);

        var layers = new[] { grass, trees };
        var kept = all.Instances.Where((instance, index) =>
        {
            var layer = layers[index < all.LayerCounts[0] ? 0 : 1];
            var p = new Vector2(instance.Position.X, instance.Position.Z);
            return battle.Objects.All(o => Vector2.Distance(p, o.At) >= o.Radius + LevelScatter.BattleClearing(layer));
        }).ToArray();
        Assert.True(kept.Length < all.Instances.Count - 20, $"{all.Instances.Count - kept.Length} instances were in the way");
        Assert.Equal(kept, cleared.Instances);
        Assert.Contains(cleared.Instances, i => Vector2.Distance(new Vector2(i.Position.X, i.Position.Z), battle.West.Flag.At) < 4);
    }

    [Fact]
    public void Bake_WritesTheSidesAndTheNeutralPods_OnTheGround_IntoLevelJson()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var level = new LevelDefinition(new PlayArea(new Vector2(40, 30), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4))
                { Battle = Battle(new Vector2(-12, 0), new Vector2(3, 8), new Vector2(3, -8)) };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var terrain = new LevelTerrain(level);

            LevelBaker.Bake(level, "small", materials, root.FullName);

            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var battle = json.RootElement.GetProperty("battle");
            var sides = battle.GetProperty("sides");
            Assert.Equal(["west", "east"], sides.EnumerateArray().Select(s => s.GetProperty("side").GetString()));
            var east = sides[1];
            Assert.Equal(["minigun", "pistol", "health"], east.GetProperty("pods").EnumerateArray().Select(p => p.GetProperty("pod").GetString()));
            Assert.False(east.GetProperty("flag").TryGetProperty("pod", out _));
            var spawn = east.GetProperty("spawn");
            var position = spawn.GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Assert.Equal(new[] { 12f, terrain.Height(12, 0), 0f }, position);
            Assert.Equal(-90f, spawn.GetProperty("yaw").GetSingle());
            // The spawn's zone (ticket 20); a flag or a pod has none
            Assert.Equal(2f, spawn.GetProperty("radius").GetSingle());
            Assert.False(east.GetProperty("flag").TryGetProperty("radius", out _));
            Assert.Equal(["sniper", "flamethrower"], battle.GetProperty("neutral").EnumerateArray().Select(p => p.GetProperty("pod").GetString()));

            var outside = level with { Battle = level.Battle! with { Neutral = [level.Battle.Neutral[0] with { At = new Vector2(0, 14.5f), Line = 21 }, level.Battle.Neutral[1]] } };
            var problem = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(outside, "small", materials, root.FullName));
            Assert.Equal(21, problem.Line);
            Assert.Contains("inside the playable area", problem.Problem);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>A layout with the west side round <paramref name="west"/> (its mirror the east), and the neutral pods at the two points.</summary>
    private static BattleLayout Battle(Vector2 west, Vector2 sniper, Vector2 flamethrower)
    {
        BattleSide Side(string name, float sign)
        {
            BattleObject At(BattleKind kind, string? pod, float x, float z) => new(kind, pod, new Vector2(sign * (-west.X + x), west.Y + z), 0, 1);
            return new BattleSide(name, At(BattleKind.Spawn, null, 0, 0) with { Yaw = sign * -90, ZoneRadius = 2 }, At(BattleKind.Flag, null, 3, 0),
                At(BattleKind.Pod, "minigun", -4, 4), At(BattleKind.Pod, "pistol", -4, -4), At(BattleKind.Pod, "health", 3, 5));
        }
        return new BattleLayout(Side(BattleSide.West, -1), Side(BattleSide.East, 1),
            [new BattleObject(BattleKind.Pod, "sniper", sniper, 0, 1), new BattleObject(BattleKind.Pod, "flamethrower", flamethrower, 0, 1)]);
    }

    /// <summary>
    /// Each battle object that sinks into a placed model: where its disc (grown by <see cref="Gap"/>) meets the model's footprint, the
    /// model's shape is sampled over the disc from <see cref="Foot"/> up to <see cref="Headroom"/>, and any point inside it counts.
    /// A castle's footprint holds its courtyard, which is open; its walls, towers, keep and stairs are not.
    /// </summary>
    private static List<string> OverlapProblems(LevelDefinition level, IEnumerable<BattleObject> objects)
    {
        var terrain = new LevelTerrain(level);
        var footprints = LevelModels.In(Path.Combine(Content, "models"));
        var shapes = new Dictionary<string, Func<Vector3, float>>();
        var files = ModelFiles.All(Path.Combine(Content, "models")).ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, f => f);
        Func<Vector3, float> ShapeOf(string model) =>
            shapes.TryGetValue(model, out var shape) ? shape : shapes[model] = new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(files[model])).Distance;

        var problems = new List<string>();
        foreach (var item in objects)
        {
            var radius = item.Radius + Gap;
            var ground = terrain.Height(item.At.X, item.At.Y);
            foreach (var placement in level.Placements)
            {
                var footprint = footprints(placement.Model)!;
                var local = Placement2D.ToLocal(item.At - placement.At, placement.Yaw) / placement.Scale;
                var grow = radius / placement.Scale;
                if (local.X < footprint.Min.X - grow || local.X > footprint.Max.X + grow || local.Y < footprint.Min.Y - grow || local.Y > footprint.Max.Y + grow)
                    continue;

                var sdf = ShapeOf(placement.Model);
                var baseHeight = terrain.Height(placement.At.X, placement.At.Y) + placement.Y;
                Vector3 Local(Vector2 point, float height)
                {
                    var plane = Placement2D.ToLocal(point - placement.At, placement.Yaw) / placement.Scale;
                    return new Vector3(plane.X, (ground + height - baseHeight) / placement.Scale, plane.Y);
                }

                var hit = false;
                for (var height = Foot; height <= Headroom && !hit; height += 0.2f)
                {
                    // Clear all round at this height when the shape is further from the middle than the disc reaches
                    if (sdf(Local(item.At, height)) * placement.Scale > radius)
                        continue;
                    for (var x = -radius; x <= radius && !hit; x += 0.15f)
                    for (var z = -radius; z <= radius && !hit; z += 0.15f)
                        if (x * x + z * z <= radius * radius && sdf(Local(item.At + new Vector2(x, z), height)) < 0)
                        {
                            hit = true;
                            problems.Add($"the {item.Name} on line {item.Line} at ({item.At.X}, {item.At.Y}) runs into {placement.Model} " +
                                         $"(line {placement.Line}) {height:0.0} m up, at ({item.At.X + x:0.0}, {item.At.Y + z:0.0})");
                        }
                }
            }
        }
        return problems;
    }
}
