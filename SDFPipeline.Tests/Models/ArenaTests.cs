using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.Materials;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;
using Xunit.Abstractions;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 33.2: the Champion's Arena's models (content/models/arena/, the crowd in arena/crowd/) are the sizes their headers say, within
/// the triangle budgets of ticket 33 §9 (LOD0 at 256 cells, each LOD below the one above), use only materials that exist, and the
/// arena is fungal: none of them comes from home/, chess/, games/ or the meadow's trees/.
/// </summary>
public class ArenaTests(ITestOutputHelper output)
{
    /// <summary>The models by their path under models/ without the extension, and their LOD0 triangle budget.</summary>
    public static readonly Dictionary<string, int> Budgets = new()
    {
        ["arena/royal-box"] = 20_000,
        ["arena/royal-box-balcony"] = 4_000,
        ["arena/royal-box-balcony-fallen"] = 4_000,
        ["arena/champions-corner"] = 8_000,
        ["arena/champion-plinth"] = 3_000,
        ["arena/arena-pillar0"] = 5_000,
        ["arena/arena-pillar1"] = 5_000,
        ["arena/arena-pillar-toppled"] = 5_000,
        ["arena/shelter-toadstool0"] = 6_000,
        ["arena/shelter-toadstool1"] = 6_000,
        ["arena/arena-barrier0"] = 3_000,
        ["arena/arena-barrier1"] = 3_000,
        ["arena/arena-wall"] = 2_000,
        ["arena/floodlight-mushroom"] = 15_000,
        ["arena/primecopter-wreck"] = 15_000,
        ["arena/crowd/crowd-mushroom0"] = 1_200,
        ["arena/crowd/crowd-mushroom1"] = 1_200,
        ["arena/crowd/crowd-mushroom2"] = 1_200,
        ["arena/crowd/crowd-mushroom3"] = 1_200,
        ["arena/crowd/crowd-mushroom-blue-fan"] = 1_200,
    };

    public static TheoryData<string> AllModels() => new(Budgets.Keys);

    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string name) => Path.Combine(Content(), "models", name + ".sdf");

    private static string MaterialsFolder() => Path.Combine(Content(), "..", ModelFiles.MaterialsFolderName);

    /// <summary>How far the finder's bounds may be off: its cells are half / 64 and each side is up to two or three cells out.</summary>
    private float tolerance;

    private void InRange(float value, float low, float high) => Assert.InRange(value, low - tolerance, high + tolerance);

    /// <summary>The model's bounds, searched within <paramref name="half"/> metres of the origin (the finder's cells are about a 150th of that, so keep it near the model's size).</summary>
    private SdfBounds Bounds(string name, float half)
    {
        tolerance = half / 16;
        var bounds = SdfBoundsFinder.Find(new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(name))).Distance, half)!;
        output.WriteLine($"{name}: {bounds.Size.X:0.00} x {bounds.Size.Y:0.00} x {bounds.Size.Z:0.00} m, from {bounds.Min} to {bounds.Max}");
        return bounds;
    }

    [Theory]
    [MemberData(nameof(AllModels))]
    public void Model_MeshesWithinItsBudget_EachLodSmallerThanTheOneAbove_AndUsesOnlyMaterialsThatExist(string name)
    {
        var program = SdfProgramFile.Load(ModelPath(name));
        var model = ModelMesher.Mesh(program, new MeshingOptions
        {
            Resolution = 256, LodCount = 3,
            Materials = MaterialFiles.LoadLibrary(MaterialsFolder()), Patterns = MaterialFiles.LoadPatterns(MaterialsFolder()),
        });

        // Every mesh once: the body and each part (a part used twice, the crowd's eyes, is one mesh drawn twice)
        var lod0 = model.Meshes.Sum(m => m.TriangleCount);
        var lod1 = model.Meshes.Sum(m => m.Lods[0].TriangleCount);
        var lod2 = model.Meshes.Sum(m => m.Lods[1].TriangleCount);
        output.WriteLine($"{name}: {lod0} / {lod1} / {lod2} triangles (budget {Budgets[name]})");

        Assert.InRange(lod0, 1, Budgets[name]);
        Assert.True(lod1 < lod0 && lod2 < lod1, $"{name}: LODs {lod0} / {lod1} / {lod2}");
        Assert.DoesNotContain(model.Materials, m => m.Definition == MaterialDefinition.Missing);
    }

    [Fact]
    public void Pillars_AreSixMetresHigh_AndTwoPointTwoRound()
    {
        foreach (var name in new[] { "arena/arena-pillar0", "arena/arena-pillar1" })
        {
            var bounds = Bounds(name, 8);
            InRange(bounds.Size.Y, 5.9f, 6.1f);
            InRange(bounds.Min.Y, -0.1f, 0.1f);
            InRange(Math.Max(bounds.Size.X, bounds.Size.Z), 2.1f, 2.3f);
        }
    }

    [Fact]
    public void ToppledPillar_IsSixMetresLong_AndAboutOnePointOneHigh()
    {
        var bounds = Bounds("arena/arena-pillar-toppled", 8);
        InRange(bounds.Size.Z, 5.9f, 6.1f);
        InRange(bounds.Size.Y, 1.0f, 1.2f);
        InRange(bounds.Min.Y, -0.1f, 0.1f);
    }

    [Fact]
    public void Barriers_HaveTheirTopBetweenPointEightAndOneMetre_AndAreFourMetresLong()
    {
        foreach (var name in new[] { "arena/arena-barrier0", "arena/arena-barrier1" })
        {
            var bounds = Bounds(name, 4);
            InRange(bounds.Max.Y, 0.8f, 1.0f);
            InRange(bounds.Min.Y, -0.1f, 0.1f);
            InRange(bounds.Size.X, 3.8f, 4.6f);
        }
    }

    [Fact]
    public void CrowdMushrooms_AreUnderOnePointSixMetres()
    {
        foreach (var name in Budgets.Keys.Where(k => k.StartsWith("arena/crowd/")))
        {
            var bounds = Bounds(name, 2);
            InRange(bounds.Size.Y, 1.0f, 1.6f);
        }
    }

    [Fact]
    public void Wall_IsEightMetresLong_TwoAndAHalfHigh_AndAMetreThick()
    {
        var bounds = Bounds("arena/arena-wall", 5);
        InRange(bounds.Size.X, 7.9f, 8.2f);
        InRange(bounds.Size.Y, 2.4f, 2.6f);
        InRange(bounds.Size.Z, 0.9f, 1.2f);
    }

    [Fact]
    public void Landmarks_AreTheirSizes()
    {
        // Royal Box: 28 wide, 16 deep, its back wall 12 high (the shelf brackets stand out a little at the ends)
        var box = Bounds("arena/royal-box", 16);
        InRange(box.Size.X, 27.5f, 30f);
        InRange(box.Size.Y, 11.8f, 12.5f);
        InRange(box.Size.Z, 15.8f, 17.2f);

        var balcony = Bounds("arena/royal-box-balcony", 14);
        InRange(balcony.Size.X, 11.8f, 12.2f);
        InRange(balcony.Size.Z, 7.8f, 8.2f);
        InRange(balcony.Size.Y, 0.7f, 0.9f);

        // The ramp: about 10 m long in all and rising to the dais's 4 m
        var fallen = Bounds("arena/royal-box-balcony-fallen", 14);
        InRange(fallen.Max.Y, 3.8f, 4.6f);
        InRange(fallen.Min.Y, -0.1f, 0.1f);
        InRange(fallen.Size.X, 11.8f, 12.2f);

        var corner = Bounds("arena/champions-corner", 12);
        InRange(corner.Size.X, 13.8f, 14.3f);
        InRange(corner.Size.Z, 19.8f, 20.3f);
        InRange(corner.Size.Y, 0.25f, 0.8f);

        var plinth = Bounds("arena/champion-plinth", 2);
        InRange(plinth.Size.Y, 1.1f, 1.3f);
        InRange(Math.Max(plinth.Size.X, plinth.Size.Z), 1.1f, 1.3f);

        var flood = Bounds("arena/floodlight-mushroom", 16);
        InRange(flood.Size.Y, 13.8f, 14.3f);
        InRange(flood.Size.X, 5.8f, 6.4f);

        foreach (var name in new[] { "arena/shelter-toadstool0", "arena/shelter-toadstool1" })
        {
            var shelter = Bounds(name, 6);
            InRange(shelter.Max.Y, 5.3f, 5.7f);
            InRange(shelter.Size.X, 7.8f, 8.3f);
        }
    }

    /// <summary>The materials a file and the parts it uses name, from the file texts.</summary>
    private static IEnumerable<string> MaterialsUsed(string text) =>
        Regex.Matches(text, @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value);

    [Fact]
    public void ArenaModels_UseTheArenasMaterials_ThePrimeGold_AndTheEyes()
    {
        var folders = new[] { "arena", "crowd" };
        var all = Directory.EnumerateFiles(MaterialsFolder(), "*.sdfmat", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var name in Budgets.Keys)
        {
            var text = File.ReadAllText(ModelPath(name));
            foreach (var material in MaterialsUsed(text))
                Assert.True(all.Contains(material), $"{name} uses {material}, which has no .sdfmat");
            // Every cap, crowd or banner material lives in materials/arena/ (the wreck of the Prime-copter, 33.5, is in the machines' blue)
            foreach (var material in MaterialsUsed(text).Where(m => m != "prime_gold" && !m.StartsWith("machine_blue_")))
                Assert.True(File.Exists(Path.Combine(MaterialsFolder(), "arena", material + ".sdfmat")), $"{name}: {material} is not in materials/arena/");
        }
        Assert.NotEmpty(folders);
    }

    [Fact]
    public void RoyalBannerAndFans_AreTheOnlyBlueInTheArenasMaterials()
    {
        // 33 §1: no team red or blue except where named: the banner, the fans' scarf and Champion's Corner's red
        var named = new HashSet<string> { "arena_banner", "arena_banner_trim", "crowd_scarf_blue", "arena_team_red" };
        foreach (var file in Directory.EnumerateFiles(Path.Combine(MaterialsFolder(), "arena"), "*.sdfmat"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (named.Contains(name)) continue;
            var line = File.ReadAllLines(file).Single(l => l.StartsWith("baseColor"));
            var c = Regex.Matches(line, @"[0-9.]+").Select(m => float.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.False(c[2] > c[0] + 0.15f && c[2] > c[1] + 0.15f, $"{name} is blue");
            Assert.False(c[0] > c[1] + 0.5f && c[0] > c[2] + 0.5f, $"{name} is red");
        }
    }

    [Fact]
    public void ContentArena_IsFungal()
    {
        var models = Path.Combine(Content(), "models");
        var arena = Directory.EnumerateFiles(Path.Combine(models, "arena"), "*.sdf", SearchOption.AllDirectories).ToArray();
        Assert.Equal(Budgets.Count, arena.Length);

        // Not from the home room, the chess cube, the board games or the meadow's trees (the meadow's mushroom-free trees)
        var foreign = new[] { "home", "chess", "games", "trees" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(models, folder), "*.sdf", SearchOption.AllDirectories))
            .Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var file in arena)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            Assert.DoesNotContain(name, foreign);
            var text = File.ReadAllText(file);
            // The parts the arena uses are its own shelter caps and the scouts' coarse eye
            foreach (Match part in Regex.Matches(text, @"Part\s+part=(\S+)"))
                Assert.Matches(@"^(shelter_cap\d|eye_machine)$", part.Groups[1].Value);
            // And the materials are the arena's own (or Prime's gold), none of the home room's, chess or games'
            foreach (var material in MaterialsUsed(text))
                Assert.DoesNotMatch(@"^(home_|chess_|card_|castle_|pod_)", material);
        }
    }
}
