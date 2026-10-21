using System.Numerics;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 24.1: the machines (content/models/machines/) are one model per moving piece, each with its origin at its pivot, put
/// together in Unity at the pivots below (the same as the ticket's pivot table and each file's header): a tank and a helicopter
/// within 0.8 m in every dimension, a turret about 2 m high, the scouts' eyes on the turning piece, red and blue the same shapes.
/// </summary>
public class MachinesTests
{
    public static readonly string[] Teams = ["red", "blue"];

    /// <summary>A piece, where its pivot is in its parent's space, the axis it moves about, and the pieces hanging from it.</summary>
    private sealed record Piece(string Name, Vector3 Pivot, Vector3 Axis, Piece[] Children, bool Eyes = false);

    private static readonly Vector3 Up = Vector3.UnitY, Side = Vector3.UnitX;

    private static readonly Dictionary<string, Piece> Machines = new()
    {
        ["tank"] = new Piece("body", Vector3.Zero, Vector3.Zero,
        [
            new Piece("turret", new Vector3(0, 0.3f, 0), Up,
                [new Piece("barrel", new Vector3(0, 0.05f, 0.13f), Side, [])], Eyes: true),
        ]),
        ["helicopter"] = new Piece("body", Vector3.Zero, Vector3.Zero,
        [
            new Piece("rotor", new Vector3(0, 0.56f, 0), Up, []),
            new Piece("tail-rotor", new Vector3(0.025f, 0.4f, -0.35f), Side, []),
        ], Eyes: true),
        ["turret"] = new Piece("foot", Vector3.Zero, Vector3.Zero,
        [
            new Piece("head", new Vector3(0, 1.5f, 0), Up,
                [new Piece("barrel", new Vector3(0, 0, 0.3f), Side, [])], Eyes: true),
        ]),
    };

    private static string ModelPath(string machine, string team, string piece)
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return Path.Combine(content, "models", "machines", $"{machine}-{team}-{piece}.sdf");
    }

    private static Func<Vector3, float> Distance(string machine, string team, string piece) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(machine, team, piece))).Distance;

    private static IEnumerable<Piece> All(Piece piece) => piece.Children.SelectMany(All).Prepend(piece);

    /// <summary>
    /// The machine put together, each moving piece turned by <paramref name="angle"/> radians about its axis: the distance at a point
    /// is the nearest piece's, each piece asked in its own space.
    /// </summary>
    private static Func<Vector3, float> Assembled(string machine, string team, float angle = 0)
    {
        var pieces = new List<(Func<Vector3, float> Sdf, Matrix4x4 ToLocal)>();
        void Add(Piece piece, Matrix4x4 parentToWorld)
        {
            var local = piece.Axis == Vector3.Zero ? Matrix4x4.Identity : Matrix4x4.CreateFromAxisAngle(piece.Axis, angle);
            var toWorld = local * Matrix4x4.CreateTranslation(piece.Pivot) * parentToWorld;
            Matrix4x4.Invert(toWorld, out var toLocal);
            pieces.Add((Distance(machine, team, piece.Name), toLocal));
            foreach (var child in piece.Children)
                Add(child, toWorld);
        }
        Add(Machines[machine], Matrix4x4.Identity);
        return p => pieces.Min(x => x.Sdf(Vector3.Transform(p, x.ToLocal)));
    }

    /// <summary>
    /// The model's extent, sampled on a grid of <paramref name="step"/> within the box: the grid points inside it, so the box is
    /// up to a step too small on each side, never too big (the distances of ellipsoids and smooth unions are only bounds, but the
    /// sign is exact).
    /// </summary>
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

    public static TheoryData<string, string> SmallMachines() => new()
    {
        { "tank", "red" }, { "tank", "blue" }, { "helicopter", "red" }, { "helicopter", "blue" },
    };

    [Theory]
    [MemberData(nameof(SmallMachines))]
    public void TankAndHelicopter_AreWithinEightyCentimetres_TurnedAnyWay_AndStandOnTheGround(string machine, string team)
    {
        foreach (var angle in new[] { 0f, MathF.PI / 2, MathF.PI })
        {
            var (min, max) = Extent(Assembled(machine, team, angle), new Vector3(-0.6f, -0.1f, -0.6f), new Vector3(0.6f, 0.9f, 0.6f), 0.02f);
            var size = max - min;
            // Up to a step too small on each side, so a step less than 80 cm
            const float most = 0.8f - 0.02f;
            Assert.True(size.X <= most && size.Y <= most && size.Z <= most, $"{machine} {team} turned {angle}: {size}");
            // Big enough to read at a distance: over half a metre long and high
            Assert.True(size.Y > 0.5f && Math.Max(size.X, size.Z) > 0.55f, $"{machine} {team}: {size}");
            // On the ground (the grid's own step aside)
            Assert.InRange(min.Y, -0.01f, 0.03f);
        }
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    public void Turret_IsAboutTwoMetresHigh_OnTheGround(string team)
    {
        var (min, max) = Extent(Assembled("turret", team), new Vector3(-0.9f, -0.1f, -0.9f), new Vector3(0.9f, 2.4f, 1.3f), 0.03f);

        Assert.InRange(max.Y - min.Y, 1.85f, 2.15f);
        Assert.InRange(min.Y, -0.01f, 0.04f);
        // Narrower than it is high, so it stands like a mushroom
        Assert.InRange(max.X - min.X, 0.9f, 1.4f);
    }

    /// <summary>The part the machines' eyes are: the scouts' eye (content/parts/eye.sdf), meshed coarser.</summary>
    private const string MachineEye = "eye_machine";

    [Fact]
    public void TheMachineEye_IsTheScoutsEye()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var scouts = SdfProgramFile.Load(Path.Combine(content, "parts", "eye.sdf"));
        var machines = SdfProgramFile.Load(Path.Combine(content, "parts", "machines", MachineEye + ".sdf"));
        var scoutsDistance = new SdfCalculatorCpu().Prepare(scouts).Distance;
        var machinesDistance = new SdfCalculatorCpu().Prepare(machines).Distance;

        // The same ball, all round it and in front, painted with the same materials
        for (var i = 0; i < 200; i++)
        {
            var z = 1 - 2 * (i + 0.5f) / 200;
            var ring = MathF.Sqrt(1 - z * z);
            var angle = i * 2.3999632f;
            var direction = new Vector3(ring * MathF.Cos(angle), ring * MathF.Sin(angle), z);
            foreach (var radius in new[] { 0.1f, 0.12f, 0.14f })
                Assert.Equal(scoutsDistance(direction * radius), machinesDistance(direction * radius), 4);
        }
        Assert.Equal(Materials(Path.Combine(content, "parts", "eye.sdf")),
            Materials(Path.Combine(content, "parts", "machines", MachineEye + ".sdf")));
        Assert.True(PartSplitter.ModelDetail(machines) < 1);
    }

    /// <summary>The materials a file's Material nodes name, in order.</summary>
    private static string[] Materials(string path) =>
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), @"^\s*Material\s+material=(\S+)",
            System.Text.RegularExpressions.RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();

    [Theory]
    [InlineData("tank")]
    [InlineData("helicopter")]
    [InlineData("turret")]
    public void EachPiece_IsAModel_AndTheScoutsEyesAreOnTheTurningPieceOnly(string machine)
    {
        foreach (var team in Teams)
        foreach (var piece in All(Machines[machine]))
        {
            var path = ModelPath(machine, team, piece.Name);
            Assert.True(File.Exists(path), path);
            var meshes = PartSplitter.Split(SdfProgramFile.Load(path));

            // The piece's own shape, and the eye part where the eyes go: meshed once, used twice, so each eye is a child of its own
            // in the piece's prefab (eye_machine.0, eye_machine.1) that the eyes' script can turn. It is the scouts' eye meshed
            // coarser (its file's Detail), so the pair costs far less than the machine
            Assert.Contains(meshes, m => m.Name == PartSplitter.BodyName);
            Assert.DoesNotContain(meshes, m => m.Part == "eye");
            var eyes = meshes.Where(m => m.Part == MachineEye).ToArray();
            if (piece.Eyes)
            {
                var eye = Assert.Single(eyes);
                Assert.Equal(2, eye.Instances.Count);
                Assert.InRange(eye.Detail, 0.1f, 0.5f);
            }
            else
                Assert.Empty(eyes);
            Assert.Equal(piece.Eyes ? 2 : 1, meshes.Count);
        }
    }

    [Theory]
    [InlineData("tank")]
    [InlineData("helicopter")]
    [InlineData("turret")]
    public void EachPiece_MeetsItsParentAtItsPivot(string machine)
    {
        foreach (var team in Teams)
        {
            void Check(Piece parent)
            {
                var parentSdf = Distance(machine, team, parent.Name);
                foreach (var child in parent.Children)
                {
                    // The pivot is inside or on the child, and the parent is no further from it than the child reaches round it (a
                    // knuckle's radius, a hub's), so the two touch there and nothing floats free of what it turns on
                    var depth = Distance(machine, team, child.Name)(Vector3.Zero);
                    var gap = parentSdf(child.Pivot);
                    Assert.True(depth <= 0.005f, $"{machine} {child.Name}'s pivot is {depth} off it");
                    Assert.True(gap + depth <= 0.01f, $"{machine} {parent.Name} is {gap} from {child.Name}'s pivot, which is {-depth} deep in it");
                    Check(child);
                }
            }
            Check(Machines[machine]);
        }
    }

    [Fact]
    public void BlueModels_AreTheRedOnes_InTheOtherSidesMaterials()
    {
        foreach (var (machine, root) in Machines)
        foreach (var piece in All(root))
        {
            var red = File.ReadAllText(ModelPath(machine, "red", piece.Name));
            var blue = File.ReadAllText(ModelPath(machine, "blue", piece.Name));
            Assert.Equal(Blue(red), blue);
        }
    }

    /// <summary>
    /// The red file as the blue one reads: the red materials and names become blue, and the comments' colours the blue side's
    /// (whole words only, so "centred" stays).
    /// </summary>
    public static string Blue(string red)
    {
        var text = red.Replace("machine_red_", "machine_blue_").Replace("rosy red", "sky blue").Replace("glossy scarlet", "velvety cornflower blue")
            .Replace("scarlet", "cornflower blue").Replace("cream warts", "pale flecks");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\bred\b", "blue");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\bRed\b", "Blue");
    }
}
