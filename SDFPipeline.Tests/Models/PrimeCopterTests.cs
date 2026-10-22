using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using Xunit.Abstractions;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 33.5: the Prime-copter (content/models/machines/primecopter-*.sdf, 24's blue helicopter at seven times, with a four-sac rim and wide
/// runners) is one model per piece, each with its origin at its pivot, put together in Unity at the pivots below (the same as each
/// file's header and PrimeCopterBuilder's): within 5.5 m in every dimension with the rotor and tail rotor turned any way, on the
/// ground, every piece meeting the body at its pivot, the four sacs 1 m round and 2.5 m apart. And its wreck (arena/), a landmark
/// 7 to 9 m across with a level top 2 m up that a ramp of debris leads to.
/// </summary>
public class PrimeCopterModelTests(ITestOutputHelper output)
{
    /// <summary>A piece, its model's name, where its pivot is in the body's space (the .sdf's, x as in the files), and the axis it turns about.</summary>
    private sealed record Piece(string Name, Vector3 Pivot, Vector3 Axis);

    private static readonly Piece[] Parts =
    [
        new("primecopter-rotor", new Vector3(0, 3.92f, 0), Vector3.UnitY),
        new("primecopter-tail-rotor", new Vector3(0.175f, 2.8f, -2.15f), Vector3.UnitX),
        new("primecopter-sac", new Vector3(-1.25f, 1.3f, 1.25f), Vector3.Zero),
        new("primecopter-sac", new Vector3(1.25f, 1.3f, 1.25f), Vector3.Zero),
        new("primecopter-sac", new Vector3(-1.25f, 1.3f, -1.25f), Vector3.Zero),
        new("primecopter-sac", new Vector3(1.25f, 1.3f, -1.25f), Vector3.Zero),
    ];

    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelPath(string model, string folder = "machines") => Path.Combine(Content(), "models", folder, model + ".sdf");

    private static Func<Vector3, float> Distance(string model, string folder = "machines") =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(model, folder))).Distance;

    /// <summary>
    /// The grid points inside a model within the box, <paramref name="step"/> apart: each side of the extent they span is up to a step
    /// short, never too big (the bounds finder's cells are too coarse for these sizes).
    /// </summary>
    private static List<Vector3> Solid(string model, Vector3 from, Vector3 to, float step, string folder = "machines")
    {
        var sdf = Distance(model, folder);
        var points = new List<Vector3>();
        for (var x = from.X; x <= to.X; x += step)
        for (var y = from.Y; y <= to.Y; y += step)
        for (var z = from.Z; z <= to.Z; z += step)
        {
            var p = new Vector3(x, y, z);
            if (sdf(p) <= 0)
                points.Add(p);
        }
        return points;
    }

    private static (Vector3 Min, Vector3 Max) Extent(IEnumerable<Vector3> points) =>
        (points.Aggregate(new Vector3(float.MaxValue), Vector3.Min), points.Aggregate(new Vector3(float.MinValue), Vector3.Max));

    // The pieces' solid points once, each in its own space
    private static readonly Lazy<(Vector3 Min, Vector3 Max)> BodyExtent =
        new(() => Extent(Solid("primecopter-body", new Vector3(-3, -0.1f, -3.4f), new Vector3(3, 4.4f, 2.6f), 0.06f)));

    private static readonly Lazy<List<Vector3>> RotorPoints =
        new(() => Solid("primecopter-rotor", new Vector3(-2.9f, -0.1f, -2.9f), new Vector3(2.9f, 1.4f, 2.9f), 0.06f));

    private static readonly Lazy<List<Vector3>> TailRotorPoints =
        new(() => Solid("primecopter-tail-rotor", new Vector3(-0.2f, -0.7f, -0.7f), new Vector3(0.9f, 0.7f, 0.7f), 0.03f));

    private static readonly Lazy<(Vector3 Min, Vector3 Max)> SacExtent =
        new(() => Extent(Solid("primecopter-sac", new Vector3(-0.7f, -1.4f, -0.7f), new Vector3(0.7f, 0.3f, 0.7f), 0.03f)));

    /// <summary>The copter put together, the rotor turned by <paramref name="angle"/> about y and the tail rotor about x: its extent.</summary>
    private static (Vector3 Min, Vector3 Max) Assembled(float angle)
    {
        var (min, max) = BodyExtent.Value;
        void Add(IEnumerable<Vector3> points, Piece piece)
        {
            var turn = Matrix4x4.CreateFromAxisAngle(piece.Axis, angle);
            var (lo, hi) = Extent(points.Select(p => Vector3.Transform(p, turn) + piece.Pivot));
            min = Vector3.Min(min, lo);
            max = Vector3.Max(max, hi);
        }
        Add(RotorPoints.Value, Parts[0]);
        Add(TailRotorPoints.Value, Parts[1]);
        var sac = SacExtent.Value;
        foreach (var part in Parts.Skip(2))
        {
            min = Vector3.Min(min, sac.Min + part.Pivot);
            max = Vector3.Max(max, sac.Max + part.Pivot);
        }
        return (min, max);
    }

    [Fact]
    public void Copter_IsWithinFiveAndAHalfMetres_TheRotorAndTailRotorTurnedAnyWay_AndStandsOnTheGround()
    {
        foreach (var angle in new[] { 0f, MathF.PI / 4, MathF.PI / 2, MathF.PI, 5 * MathF.PI / 4 })
        {
            var (min, max) = Assembled(angle);
            var size = max - min;
            output.WriteLine($"turned {angle:0.00}: {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m, from {min} to {max}");
            Assert.True(size.X <= 5.5f && size.Y <= 5.5f && size.Z <= 5.5f, $"turned {angle}: {size}");
            // About the ticket's 5.0 x 4.7 x 5.2
            Assert.InRange(size.X, 4.5f, 5.5f);
            Assert.InRange(size.Y, 4.2f, 5.5f);
            Assert.InRange(size.Z, 4.5f, 5.5f);
            Assert.InRange(min.Y, -0.1f, 0.1f);
        }
    }

    [Fact]
    public void EveryPiece_MeetsTheBodyAtItsPivot()
    {
        var body = Distance("primecopter-body");
        foreach (var part in Parts)
        {
            // The pivot is inside or on the piece, and the body is no further from it than the piece reaches round it, so the two
            // touch there and nothing floats free of what it hangs from
            var depth = Distance(part.Name)(Vector3.Zero);
            var gap = body(part.Pivot);
            output.WriteLine($"{part.Name} at {part.Pivot}: {depth:0.000} deep, the body {gap:0.000} from it");
            Assert.True(depth <= 0.005f, $"{part.Name}'s pivot is {depth} off it");
            Assert.True(gap + depth <= 0.01f, $"the body is {gap} from {part.Name}'s pivot, which is {-depth} deep in it");
        }
    }

    [Fact]
    public void TheFourSacs_AreAMetreRound_HangBelowTheirSockets_AndAreTwoAndAHalfMetresApart()
    {
        var (min, max) = SacExtent.Value;
        var size = max - min;
        output.WriteLine($"sac: {size}, from {min} to {max}");
        Assert.InRange(size.X, 0.9f, 1.2f);
        Assert.InRange(size.Z, 0.9f, 1.2f);
        // Hung from its top: the puffball and its neck are under its pivot, bar the neck's rounded top inside the socket
        Assert.True(max.Y <= 0.2f, $"the sac reaches {max.Y} over its pivot");
        Assert.InRange(min.Y, -1.3f, -1.0f);

        var pivots = Parts.Where(p => p.Name == "primecopter-sac").Select(p => p.Pivot).ToArray();
        Assert.Equal(4, pivots.Length);
        // The sides of the square the sacs hang at
        var sides = pivots.SelectMany((a, i) => pivots.Skip(i + 1).Select(b => Vector3.Distance(a, b))).OrderBy(d => d).Take(4).ToArray();
        foreach (var side in sides)
            Assert.InRange(side, 2.4f, 2.6f);
        // The bottom of a sac clears the ground and the runners
        foreach (var pivot in pivots)
            Assert.True(pivot.Y + min.Y > 0.15f, $"a sac hangs to {pivot.Y + min.Y}");
        // Inside the runners' x, so the runners and the sacs do not touch
        Assert.True(pivots.All(p => MathF.Abs(p.X) + size.X / 2 < 2.1f - 0.15f));
    }

    [Fact]
    public void TheRunners_AreWide_ForPrimesHands_AndTheBodyHasTheScoutsEyesOnly()
    {
        var body = Distance("primecopter-body");
        // A runner under each side at the grips Unity puts Prime's hands on (x +-2.1, 0.17 up, 0.1 along)
        foreach (var x in new[] { -2.1f, 2.1f })
            Assert.True(body(new Vector3(x, 0.17f, 0.1f)) <= 0.0f, $"no runner at x = {x}");
        // Open between them: the sacs and Prime hang there
        Assert.True(body(new Vector3(0, 0.2f, 0)) > 0.5f);

        var program = SdfProgramFile.Load(ModelPath("primecopter-body"));
        var meshes = PartSplitter.Split(program);
        Assert.DoesNotContain(meshes, m => m.Part == "eye");
        var eyes = Assert.Single(meshes, m => m.Part == "eye_machine");
        Assert.Equal(2, eyes.Instances.Count);
        Assert.True(PartSplitter.ModelDetail(program) < 1);
    }

    [Fact]
    public void ThePieces_UseTheBlueMachineMaterials_AndTheSacsOwn()
    {
        var materials = Path.Combine(Content(), "..", "materials");
        string[] Used(string model, string folder = "machines") =>
            Regex.Matches(File.ReadAllText(ModelPath(model, folder)), @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).Distinct().ToArray();

        foreach (var model in new[] { "primecopter-body", "primecopter-rotor", "primecopter-tail-rotor" })
            foreach (var material in Used(model))
            {
                Assert.StartsWith("machine_blue_", material);
                Assert.True(File.Exists(Path.Combine(materials, "machines", material + ".sdfmat")), material);
            }
        Assert.Contains("prime_sac", Used("primecopter-sac"));
        Assert.True(File.Exists(Path.Combine(materials, "characters", "prime", "prime_sac.sdfmat")));
        foreach (var material in Used("primecopter-wreck", "arena"))
            Assert.True(Directory.EnumerateFiles(materials, material + ".sdfmat", SearchOption.AllDirectories).Any(), material);
        Assert.Contains("machine_blue_cap", Used("primecopter-wreck", "arena"));
        Assert.Contains("arena_stone", Used("primecopter-wreck", "arena"));
    }

    [Fact]
    public void TheWreck_IsAboutEightMetresAcross_ThreeAndAHalfHigh_WithALevelTopTwoMetresUp()
    {
        var (min, max) = Extent(Solid("primecopter-wreck", new Vector3(-4.8f, -0.3f, -4.2f), new Vector3(4.2f, 4.4f, 4.8f), 0.1f, "arena"));
        var size = max - min;
        output.WriteLine($"wreck: {size.X:0.00} x {size.Y:0.00} x {size.Z:0.00} m, from {min} to {max}");
        // Up to a cell (10 cm) short on each side
        Assert.InRange(size.X, 6.5f, 9f);
        Assert.InRange(size.Z, 7.5f, 9.5f);
        Assert.InRange(size.Y, 3f, 4.3f);
        Assert.InRange(min.Y, -0.2f, 0.15f);

        var sdf = Distance("primecopter-wreck", "arena");
        // The crown's place: the middle of a level top at 2 m, a metre of room over it
        foreach (var (x, z) in new[] { (0f, 0f), (0.8f, -0.5f), (-0.8f, 0.3f) })
        {
            Assert.True(sdf(new Vector3(x, 1.9f, z)) < 0, $"not solid under the top at ({x}, {z})");
            Assert.True(sdf(new Vector3(x, 2.1f, z)) > 0, $"not clear over the top at ({x}, {z})");
        }
        Assert.True(sdf(new Vector3(0, 3.0f, 0)) > 0.5f, "room for the crown over the top");
    }

    [Fact]
    public void TheWrecksRamp_RunsFromTheGroundUpToTheTop_Walkable()
    {
        var sdf = Distance("primecopter-wreck", "arena");
        // The ramp's surface on its middle line (x = 0) from z = 3.8 on the ground to z = 0.9 at the top, 34.6 degrees
        var previous = 0f;
        foreach (var z in new[] { 3.4f, 3.0f, 2.5f, 2.0f, 1.5f, 1.1f })
        {
            var top = 2f * (3.8f - z) / 2.9f;
            Assert.True(sdf(new Vector3(0, top - 0.12f, z)) < 0, $"not solid under the ramp at z = {z}");
            Assert.True(sdf(new Vector3(0, top + 0.12f, z)) > 0, $"not clear over the ramp at z = {z}");
            Assert.True(top > previous);
            previous = top;
        }
        var slope = MathF.Atan2(2f, 2.9f) * 180 / MathF.PI;
        Assert.InRange(slope, 25f, 40f);
        // 3.6 m wide: a body's width either side of the middle is ramp too
        Assert.True(sdf(new Vector3(1.4f, 2f * (3.8f - 2.5f) / 2.9f - 0.12f, 2.5f)) < 0);
        Assert.True(sdf(new Vector3(-1.4f, 2f * (3.8f - 2.5f) / 2.9f - 0.12f, 2.5f)) < 0);
    }
}
