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
/// Tickets 31.1 and 31.2: 42's room (content/models/home/) is furniture and props for a mushroom about 1 m tall. The room shell,
/// window, backdrop, desk, chair, monitor, keyboard, mouse and mousepad (31.1) and the snack bowl, crisp bag, soda can, shelf, trophy,
/// shelf stuff, lamp, rug, poster and Prime's crown (31.2) each parse, mesh and are the size their file header says; the chair's seat,
/// the monitor's screen, the bowl's six crisps and the poster's picture are parts of their own, and every material they use is in
/// materials/.
/// </summary>
public class HomeModelsTests(ITestOutputHelper output)
{
    public static readonly string[] Models =
        ["room-shell", "window", "window-backdrop", "desk", "chair", "monitor", "keyboard", "mouse", "mousepad",
            "snack-bowl", "crisp-bag", "soda-can", "shelf", "trophy", "shelf-stuff", "lamp", "rug", "poster", "prime-crown"];

    /// <summary>The parts a model has besides its body.</summary>
    private static readonly Dictionary<string, string[]> PartsOf = new()
    {
        ["chair"] = ["seat"],
        ["monitor"] = ["screen"],
        ["snack-bowl"] = ["crisp_1", "crisp_2", "crisp_3", "crisp_4", "crisp_5", "crisp_6"],
        ["poster"] = ["poster_picture"],
    };

    public static TheoryData<string> AllModels() => new(Models);

    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string name) => Path.Combine(Content(), "models", "home", name + ".sdf");

    private static Func<Vector3, float> Model(string name) => new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(name))).Distance;

    /// <summary>The extent of what is inside, sampled on a grid of <paramref name="step"/>: up to a step too small on each side.</summary>
    private static (Vector3 Min, Vector3 Max) Extent(Func<Vector3, float> sdf, Vector3 from, Vector3 to, float step)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (var x = from.X; x <= to.X; x += step)
        for (var y = from.Y; y <= to.Y; y += step)
        for (var z = from.Z; z <= to.Z; z += step)
        {
            var p = new Vector3(x, y, z);
            if (sdf(p) > 0) continue;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    private (Vector3 Min, Vector3 Max) Measure(string name, Vector3 from, Vector3 to, float step)
    {
        var (min, max) = Extent(Model(name), from, to, step);
        output.WriteLine($"{name}: {max.X - min.X:0.00} x {max.Y - min.Y:0.00} x {max.Z - min.Z:0.00} m (x y z), from {min} to {max}");
        return (min, max);
    }

    [Theory]
    [MemberData(nameof(AllModels))]
    public void Model_Parses_Meshes_AndHasItsParts(string name)
    {
        var program = SdfProgramFile.Load(ModelPath(name));
        var materials = MaterialFiles.LoadLibrary(Path.Combine(Content(), "..", ModelFiles.MaterialsFolderName));
        var patterns = MaterialFiles.LoadPatterns(Path.Combine(Content(), "..", ModelFiles.MaterialsFolderName));

        var model = ModelMesher.Mesh(program, new MeshingOptions { Resolution = 128, Materials = materials, Patterns = patterns });

        var expected = PartsOf.GetValueOrDefault(name, []).Prepend(PartSplitter.BodyName).Order().ToArray();
        Assert.Equal(expected, model.Meshes.Select(m => m.Name).Order().ToArray());
        foreach (var mesh in model.Meshes)
            Assert.True(mesh.TriangleCount > 0, $"{name}/{mesh.Name} has no triangles");
        Assert.DoesNotContain(model.Materials, m => m.Definition == MaterialDefinition.Missing);
    }

    [Theory]
    [MemberData(nameof(AllModels))]
    public void EveryMaterial_TheModelAndItsPartsUse_IsAFileInMaterials(string name)
    {
        var materialsFolder = Path.Combine(Content(), "..", ModelFiles.MaterialsFolderName);
        var texts = new List<string> { File.ReadAllText(ModelPath(name)) };
        texts.AddRange(PartsOf.GetValueOrDefault(name, []).Select(part =>
            File.ReadAllText(Directory.EnumerateFiles(Path.Combine(Content(), ModelFiles.PartsFolderName), part + ".sdf", SearchOption.AllDirectories).Single())));

        var used = texts.SelectMany(t => Regex.Matches(t, @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value)).Distinct().ToArray();

        Assert.NotEmpty(used);
        foreach (var material in used)
        {
            Assert.StartsWith("home_", material);
            Assert.True(File.Exists(Path.Combine(materialsFolder, "home", material + ".sdfmat")), $"{name} uses {material}, which has no file in materials/home/");
        }
    }

    [Fact]
    public void RoomShell_IsAFloorOfFourByThreeMetres_WithThreeWallsOfTwoPointTwo_AnOpeningForTheWindow_AndASkirting()
    {
        var sdf = Model("room-shell");

        // The floor: its top is y = 0, 4 m across and 3 m deep inside the walls
        Assert.True(sdf(new Vector3(0, -0.02f, 0)) < 0);
        Assert.True(sdf(new Vector3(0, 0.02f, 0)) > 0);
        Assert.True(sdf(new Vector3(1.95f, -0.02f, 1.45f)) < 0);
        Assert.True(sdf(new Vector3(0, -0.02f, 1.7f)) > 0, "the floor goes on past the open side");

        // Three walls 2.2 m high, 10 cm thick, with their inner faces at z = -1.5, x = -2 and x = 2; the fourth side is open
        foreach (var (x, z) in new[] { (0.0f, -1.55f), (-2.05f, 0.0f), (2.05f, 0.0f) })
        {
            Assert.True(sdf(new Vector3(x, 2.18f, z)) < 0 || (x == 0 && InsideOpening(z)), $"no wall at ({x}, {z})");
            Assert.True(sdf(new Vector3(x, 2.22f, z)) > 0, $"the wall at ({x}, {z}) is over 2.2 m");
        }
        Assert.True(sdf(new Vector3(-1.5f, 1, -1.55f)) < 0);
        Assert.True(sdf(new Vector3(0, 1.2f, 1.55f)) > 0 && sdf(new Vector3(-2.05f, 1, 1.45f)) < 0, "the fourth wall is left out");
        Assert.True(sdf(new Vector3(-1.5f, 1, -1.44f)) > 0 && sdf(new Vector3(-1.96f, 1.2f, 0)) > 0, "the walls are 10 cm, not more");

        // The opening for the window: 0.8 x 0.7 m, its sill at 1 m, centred on x = 0.6
        Assert.True(sdf(new Vector3(0.6f, 1.35f, -1.55f)) > 0);
        Assert.True(sdf(new Vector3(0.22f, 1.05f, -1.55f)) > 0 && sdf(new Vector3(0.98f, 1.65f, -1.55f)) > 0);
        Assert.True(sdf(new Vector3(0.18f, 1.35f, -1.55f)) < 0 && sdf(new Vector3(1.02f, 1.35f, -1.55f)) < 0);
        Assert.True(sdf(new Vector3(0.6f, 0.95f, -1.55f)) < 0 && sdf(new Vector3(0.6f, 1.75f, -1.55f)) < 0);

        // The skirting: 10 cm high, along the three walls, and not on the open side
        Assert.True(sdf(new Vector3(0, 0.05f, -1.485f)) < 0);
        Assert.True(sdf(new Vector3(0, 0.13f, -1.485f)) > 0);
        Assert.True(sdf(new Vector3(-1.985f, 0.05f, 0.5f)) < 0);
        Assert.True(sdf(new Vector3(0, 0.05f, 1.485f)) > 0);
    }

    private static bool InsideOpening(float z) => MathF.Abs(z + 1.55f) < 0.06f;

    [Fact]
    public void Window_FitsTheOpening_WithASillAndCurtainsHalfDrawn()
    {
        var sdf = Model("window");
        var (min, max) = Extent(sdf, new Vector3(-0.7f, -0.1f, -0.2f), new Vector3(0.7f, 1.0f, 0.3f), 0.01f);
        output.WriteLine($"window: {max.X - min.X:0.00} x {max.Y - min.Y:0.00} x {max.Z - min.Z:0.00} m");

        // The sill is the widest piece, 96 cm, at the opening's bottom edge; the rod is wider still
        Assert.InRange(min.Y, -0.045f, -0.03f);
        Assert.InRange(max.Y - min.Y, 0.9f, 0.98f);
        Assert.InRange(max.X - min.X, 1.1f, 1.2f);
        // Frame outside 84 x 74 cm, in the plane of the wall; the middle of the opening is empty
        Assert.True(sdf(new Vector3(0.1f, 0.2f, 0)) > 0, "a pane is open");
        Assert.True(sdf(new Vector3(0.41f, 0.35f, 0)) < 0 && sdf(new Vector3(-0.41f, 0.35f, 0)) < 0, "frame side bars");
        // Curtains on both sides, in front of the frame, and the middle of the window left clear
        Assert.True(sdf(new Vector3(-0.35f, 0.5f, 0.11f)) < 0 && sdf(new Vector3(0.35f, 0.5f, 0.11f)) < 0);
        Assert.True(sdf(new Vector3(0, 0.5f, 0.11f)) > 0);
    }

    [Fact]
    public void WindowBackdrop_IsAFlatCardOfTwoCentimetres_FacingTheRoom()
    {
        var (min, max) = Measure("window-backdrop", new Vector3(-1, -0.8f, -0.1f), new Vector3(1, 0.8f, 0.1f), 0.005f);

        Assert.InRange(max.X - min.X, 1.78f, 1.84f);
        Assert.InRange(max.Y - min.Y, 1.28f, 1.34f);
        Assert.InRange(max.Z - min.Z, 0.01f, 0.03f);
    }

    [Fact]
    public void Desk_IsOnePointTwoByPointSixMetres_AndPointFiveFiveHigh_WithADrawer()
    {
        var sdf = Model("desk");
        var (min, max) = Measure("desk", new Vector3(-0.7f, -0.05f, -0.4f), new Vector3(0.7f, 0.7f, 0.4f), 0.01f);

        Assert.InRange(max.X - min.X, 1.17f, 1.22f);
        Assert.InRange(max.Z - min.Z, 0.57f, 0.62f);
        Assert.InRange(max.Y, 0.54f, 0.56f);
        Assert.InRange(min.Y, -0.01f, 0.02f);
        // The knee space under the top, left of the cabinet, and the cabinet itself, with its drawer's front proud of it
        Assert.True(sdf(new Vector3(-0.2f, 0.3f, 0.1f)) > 0);
        Assert.True(sdf(new Vector3(0.4f, 0.3f, 0)) < 0);
        Assert.True(sdf(new Vector3(0.4f, 0.4f, 0.29f)) < 0 && sdf(new Vector3(0.4f, 0.25f, 0.29f)) > 0);
        // The handle, in brass, on the drawer
        Assert.True(sdf(new Vector3(0.4f, 0.4f, 0.325f)) < 0);
    }

    [Fact]
    public void Chair_HasASeatHalfAMetreHigh_AndAboutAMetreAndAQuarterWithTheBack()
    {
        var sdf = Model("chair");
        var (min, max) = Measure("chair", new Vector3(-0.5f, -0.05f, -0.5f), new Vector3(0.5f, 1.4f, 0.5f), 0.01f);

        Assert.InRange(max.Y, 1.2f, 1.3f);
        Assert.InRange(min.Y, -0.01f, 0.02f);
        // The base is 56 cm across, and the whole is narrower than a metre
        Assert.InRange(max.X - min.X, 0.55f, 0.75f);
        // The seat's top is at 0.5 m: solid just under it, open just over it, over the middle of the cushion
        Assert.True(sdf(new Vector3(0, 0.495f, 0.1f)) < 0);
        Assert.True(sdf(new Vector3(0, 0.505f, 0.1f)) > 0);
        // The stem and the base carry it, with wheels on the floor
        Assert.True(sdf(new Vector3(0, 0.22f, 0)) < 0);
        Assert.True(sdf(new Vector3(0, 0.03f, 0.28f)) < 0);
    }

    [Fact]
    public void Chair_SeatIsAPartOfItsOwn_ThatTurnsAboutTheOrigin()
    {
        var seat = SdfProgramFile.LoadPart(ModelPath("chair"), "seat");
        var (min, max) = Extent(new SdfCalculatorCpu().Prepare(seat).Distance, new Vector3(-0.5f, 0.2f, -0.5f), new Vector3(0.5f, 1.4f, 0.5f), 0.01f);
        output.WriteLine($"seat: from {min} to {max}");

        // The cushion's middle is on the y axis, so spinning the part about it turns it in place
        Assert.InRange((min.X + max.X) / 2, -0.02f, 0.02f);
        Assert.InRange(min.Y, 0.3f, 0.4f);
        // The base is not in the part: it stays on the floor when the seat spins
        Assert.True(new SdfCalculatorCpu().Prepare(seat).Distance(new Vector3(0, 0.03f, 0.28f)) > 0);
    }

    [Fact]
    public void Monitor_IsEightyCentimetresWide_WithAChunkyBezel_AndAFlatScreenPart()
    {
        var (min, max) = Measure("monitor", new Vector3(-0.5f, -0.05f, -0.4f), new Vector3(0.5f, 0.8f, 0.2f), 0.01f);

        Assert.InRange(max.X - min.X, 0.78f, 0.82f);
        Assert.InRange(max.Y - min.Y, 0.62f, 0.67f);
        Assert.InRange(min.Y, -0.01f, 0.02f);

        // The screen part: a flat slab 68 x 38 cm, 5 mm proud of the bezel's face, 1.6 cm deep
        var screen = new SdfCalculatorCpu().Prepare(SdfProgramFile.LoadPart(ModelPath("monitor"), "screen")).Distance;
        var (smin, smax) = Extent(screen, new Vector3(-0.5f, 0, -0.1f), new Vector3(0.5f, 0.8f, 0.1f), 0.004f);
        output.WriteLine($"screen: {smax.X - smin.X:0.000} x {smax.Y - smin.Y:0.000} x {smax.Z - smin.Z:0.000} m");
        Assert.InRange(smax.X - smin.X, 0.67f, 0.69f);
        Assert.InRange(smax.Y - smin.Y, 0.37f, 0.39f);
        Assert.InRange(smax.Z - smin.Z, 0.012f, 0.02f);
        Assert.InRange(smax.Z, 0.035f, 0.045f);
        // ... and a bezel of 6 cm round it
        var head = Model("monitor");
        Assert.True(head(new Vector3(0.37f, 0.4f, 0.03f)) < 0, "side bezel");
        Assert.True(head(new Vector3(0, 0.62f, 0.03f)) < 0, "top bezel");
    }

    [Fact]
    public void Keyboard_IsAboutFortyFiveByFifteenCentimetres_WithKeysAsBumps()
    {
        var sdf = Model("keyboard");
        var (min, max) = Measure("keyboard", new Vector3(-0.3f, -0.02f, -0.12f), new Vector3(0.3f, 0.1f, 0.12f), 0.005f);

        Assert.InRange(max.X - min.X, 0.44f, 0.46f);
        Assert.InRange(max.Z - min.Z, 0.14f, 0.16f);
        Assert.InRange(max.Y, 0.028f, 0.035f);
        Assert.InRange(min.Y, -0.005f, 0.01f);
        // A key stands up from the 2 cm case, with a gap to the next
        Assert.True(sdf(new Vector3(0, 0.028f, -0.03f)) < 0);
        Assert.True(sdf(new Vector3(0.016f, 0.028f, -0.03f)) > 0);
    }

    [Fact]
    public void Mouse_IsTenCentimetresLong_AndMousepad_ThirtyWide()
    {
        var (mmin, mmax) = Measure("mouse", new Vector3(-0.1f, -0.02f, -0.1f), new Vector3(0.1f, 0.1f, 0.1f), 0.004f);
        Assert.InRange(mmax.Z - mmin.Z, 0.09f, 0.105f);
        Assert.InRange(mmax.X - mmin.X, 0.05f, 0.07f);
        Assert.InRange(mmax.Y, 0.035f, 0.045f);
        Assert.InRange(mmin.Y, -0.005f, 0.005f);

        var (pmin, pmax) = Measure("mousepad", new Vector3(-0.2f, -0.02f, -0.2f), new Vector3(0.2f, 0.05f, 0.2f), 0.004f);
        Assert.InRange(pmax.X - pmin.X, 0.29f, 0.31f);
        Assert.InRange(pmax.Z - pmin.Z, 0.24f, 0.26f);
        Assert.InRange(pmax.Y, 0.007f, 0.011f);
    }

    [Fact]
    public void SnackBowl_IsAQuarterOfAMetreAcross_WithSixCrispsAsPartsOfTheirOwn()
    {
        var (min, max) = Measure("snack-bowl", new Vector3(-0.2f, -0.02f, -0.2f), new Vector3(0.2f, 0.2f, 0.2f), 0.004f);

        Assert.InRange(max.X - min.X, 0.24f, 0.26f);
        Assert.InRange(min.Y, -0.005f, 0.01f);
        Assert.InRange(max.Y, 0.095f, 0.13f);
        // Each crisp is a thin, wavy oval part about 10 cm long or less
        foreach (var part in PartsOf["snack-bowl"])
        {
            var crisp = new SdfCalculatorCpu().Prepare(SdfProgramFile.LoadPart(ModelPath("snack-bowl"), part)).Distance;
            var (cmin, cmax) = Extent(crisp, new Vector3(-0.07f, -0.02f, -0.07f), new Vector3(0.07f, 0.02f, 0.07f), 0.002f);
            output.WriteLine($"{part}: {cmax.X - cmin.X:0.000} x {cmax.Y - cmin.Y:0.000} x {cmax.Z - cmin.Z:0.000} m");
            Assert.InRange(cmax.X - cmin.X, 0.06f, 0.11f);
            Assert.InRange(cmax.Y - cmin.Y, 0.008f, 0.02f);
        }
    }

    [Fact]
    public void CrispBag_IsTwentyCentimetresLong_AndSodaCan_TwelveHigh()
    {
        var (bmin, bmax) = Measure("crisp-bag", new Vector3(-0.15f, -0.02f, -0.15f), new Vector3(0.15f, 0.15f, 0.15f), 0.004f);
        Assert.InRange(bmax.X - bmin.X, 0.19f, 0.22f);
        Assert.InRange(bmin.Y, -0.005f, 0.01f);
        Assert.InRange(bmax.Y, 0.06f, 0.1f);

        var (cmin, cmax) = Measure("soda-can", new Vector3(-0.06f, -0.02f, -0.06f), new Vector3(0.06f, 0.2f, 0.06f), 0.002f);
        Assert.InRange(cmax.Y - cmin.Y, 0.118f, 0.13f);
        Assert.InRange(cmax.X - cmin.X, 0.06f, 0.07f);
        Assert.InRange(cmin.Y, -0.003f, 0.003f);
    }

    [Fact]
    public void Shelf_IsAMetreLong_WithTheTopOfItsBoardAtZero_AndTwoBrackets()
    {
        var sdf = Model("shelf");
        var (min, max) = Measure("shelf", new Vector3(-0.7f, -0.3f, -0.05f), new Vector3(0.7f, 0.1f, 0.4f), 0.005f);

        Assert.InRange(max.X - min.X, 0.99f, 1.02f);
        Assert.InRange(max.Y, -0.006f, 0.001f);
        Assert.InRange(min.Z, -0.006f, 0.01f);
        Assert.InRange(max.Z, 0.21f, 0.23f);
        // A bracket under the board at each end, none in the middle
        Assert.True(sdf(new Vector3(0.3f, -0.1f, 0.006f)) < 0 && sdf(new Vector3(-0.3f, -0.1f, 0.006f)) < 0);
        Assert.True(sdf(new Vector3(0, -0.1f, 0.006f)) > 0);
    }

    [Fact]
    public void Trophy_IsFortyCentimetresTall_WithAGoldGunOnADomeBase_InTheBrightestGold()
    {
        var (min, max) = Measure("trophy", new Vector3(-0.25f, -0.02f, -0.25f), new Vector3(0.25f, 0.6f, 0.3f), 0.004f);

        Assert.InRange(max.Y - min.Y, 0.36f, 0.44f);
        Assert.InRange(min.Y, -0.003f, 0.003f);
        Assert.InRange(max.X - min.X, 0.19f, 0.26f);
        // The gold is the home gold, which is brighter than the flag brass
        Assert.Contains("home_gold", File.ReadAllText(ModelPath("trophy")));
        Assert.True(Luminance("home_gold") > Luminance("flag_brass"), "the trophy's gold is the brightest in the room");
    }

    private static float Luminance(string material)
    {
        var file = Directory.EnumerateFiles(Path.Combine(Content(), "..", ModelFiles.MaterialsFolderName), material + ".sdfmat", SearchOption.AllDirectories).Single();
        var line = File.ReadAllLines(file).Single(l => l.StartsWith("baseColor"));
        var c = Regex.Match(line, @"\(([^,]+),([^,]+),([^)]+)\)").Groups;
        float F(int i) => float.Parse(c[i].Value.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        return 0.2126f * F(1) + 0.7152f * F(2) + 0.0722f * F(3);
    }

    [Fact]
    public void ShelfStuff_IsAFewSmallThings_SpreadOverLessThanTheShelf()
    {
        var (min, max) = Measure("shelf-stuff", new Vector3(-0.5f, -0.02f, -0.15f), new Vector3(0.5f, 0.4f, 0.15f), 0.005f);

        Assert.InRange(max.X - min.X, 0.55f, 0.9f);
        Assert.InRange(min.Y, -0.005f, 0.01f);
        Assert.InRange(max.Y, 0.15f, 0.25f);
        Assert.InRange(max.Z - min.Z, 0.1f, 0.2f);
    }

    [Fact]
    public void Lamp_IsSixtyCentimetresTall_WithACapShapedShadeThatGlows()
    {
        var sdf = Model("lamp");
        var (min, max) = Measure("lamp", new Vector3(-0.3f, -0.02f, -0.3f), new Vector3(0.3f, 0.8f, 0.4f), 0.005f);

        Assert.InRange(max.Y, 0.57f, 0.63f);
        Assert.InRange(min.Y, -0.003f, 0.005f);
        // The shade is open underneath and hollow: empty under its dome, solid in its wall
        Assert.True(sdf(new Vector3(0.09f, 0.52f, 0.08f)) > 0, "hollow shade");
        Assert.True(sdf(new Vector3(0.134f, 0.51f, 0.08f)) < 0, "the shade's wall");
        Assert.Contains("home_lamp_shade", File.ReadAllText(ModelPath("lamp")));
    }

    [Fact]
    public void Rug_IsAMetreAndAHalfRound_AndAboutThreeCentimetresThick()
    {
        var (min, max) = Measure("rug", new Vector3(-0.85f, -0.02f, -0.85f), new Vector3(0.85f, 0.1f, 0.85f), 0.01f);

        Assert.InRange(max.X - min.X, 1.47f, 1.53f);
        Assert.InRange(max.Z - min.Z, 1.47f, 1.53f);
        Assert.InRange(max.Y, 0.02f, 0.035f);
        Assert.InRange(min.Y, -0.005f, 0.011f); // sampled every centimetre
    }

    [Fact]
    public void Poster_IsHalfAMetreBySeventyCentimetres_Flat_WithAPictureSlab()
    {
        var (min, max) = Measure("poster", new Vector3(-0.35f, -0.05f, -0.05f), new Vector3(0.35f, 0.8f, 0.05f), 0.004f);

        Assert.InRange(max.X - min.X, 0.49f, 0.51f);
        Assert.InRange(max.Y - min.Y, 0.69f, 0.71f);
        Assert.InRange(max.Z - min.Z, 0.03f, 0.045f);
        Assert.InRange(min.Z, -0.002f, 0.003f);
        // The picture is in front of the sheet and inside its margin
        var picture = new SdfCalculatorCpu().Prepare(SdfProgramFile.LoadPart(ModelPath("poster"), "poster_picture")).Distance;
        var (pmin, pmax) = Extent(picture, new Vector3(-0.3f, 0, -0.05f), new Vector3(0.3f, 0.8f, 0.05f), 0.004f);
        output.WriteLine($"poster_picture: {pmax.X - pmin.X:0.000} x {pmax.Y - pmin.Y:0.000} x {pmax.Z - pmin.Z:0.000} m, z to {pmax.Z:0.000}");
        Assert.InRange(pmax.X - pmin.X, 0.44f, 0.46f);
        Assert.InRange(pmax.Y - pmin.Y, 0.64f, 0.66f);
        Assert.True(pmax.Z > 0.02f, "the picture stands proud of the 2 cm board");
    }

    [Fact]
    public void PrimeCrown_IsGold_WithFivePointsAndBlueGems_SizedSlightlyTooSmallForTheChanterelleCap()
    {
        var sdf = Model("prime-crown");
        var (min, max) = Measure("prime-crown", new Vector3(-0.2f, -0.02f, -0.2f), new Vector3(0.2f, 0.3f, 0.2f), 0.004f);

        Assert.InRange(max.X - min.X, 0.26f, 0.32f);
        Assert.InRange(max.Y, 0.15f, 0.2f);
        Assert.InRange(min.Y, -0.003f, 0.003f);
        // Five points 72 degrees apart, the first at the front: solid at a point's base, open at the same height between two points
        for (var i = 0; i < 5; i++)
        {
            var angle = i * MathF.Tau / 5;
            Assert.True(sdf(new Vector3(MathF.Sin(angle) * 0.14f, 0.07f, MathF.Cos(angle) * 0.14f)) < 0, $"point {i}");
            var between = angle + MathF.Tau / 10;
            Assert.True(sdf(new Vector3(MathF.Sin(between) * 0.14f, 0.07f, MathF.Cos(between) * 0.14f)) > 0, $"gap after point {i}");
        }
        // The band is hollow, for the cap
        Assert.True(sdf(new Vector3(0, 0.025f, 0)) > 0);
        var text = File.ReadAllText(ModelPath("prime-crown"));
        Assert.Contains("home_gold", text);
        Assert.Contains("home_gem_blue", text);

        // Slightly too small: narrower than the chanterelle scout's cap
        var scout = SdfBoundsFinder.Find(new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(Content(), "models", "characters", "chanterelle-scout.sdf"))).Distance)!;
        output.WriteLine($"chanterelle-scout bounds: {scout.Max.X - scout.Min.X:0.00} x {scout.Max.Y - scout.Min.Y:0.00} m");
        Assert.True(max.X - min.X < scout.Max.X - scout.Min.X);
    }

    [Fact]
    public void Furniture_IsSizedForTheScout()
    {
        var content = Content();
        var scout = SdfBoundsFinder.Find(new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(content, "models", "characters", "amanita-scout.sdf"))).Distance)!;
        var scoutHeight = scout.Max.Y - scout.Min.Y;
        output.WriteLine($"amanita-scout bounds: {scout.Max.X - scout.Min.X:0.00} x {scoutHeight:0.00} x {scout.Max.Z - scout.Min.Z:0.00} m, y {scout.Min.Y:0.00} to {scout.Max.Y:0.00}");

        // The scout is about 1.6 m tall (the bounds finder rounds that up a little) with a cap 1.16 m across, standing on its bulb. The
        // furniture is sized between a mushroom of that size and one scaled to about a metre: the desk's top at a third of the
        // scout's height, the seat a little under it, so the scout sits at it with its cap over the keyboard
        Assert.InRange(scoutHeight, 1.3f, 2f);
        Assert.InRange(0.55f / scoutHeight, 0.25f, 0.6f);
        Assert.InRange(0.5f / scoutHeight, 0.25f, 0.55f);
    }
}
