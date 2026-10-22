using System.Numerics;
using System.Text.RegularExpressions;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 30.7: the computer machine set (capacitor turret, SOIC chip tank, blower-fan helicopter): <c>content/models/machines/&lt;kind&gt;-computer-&lt;team&gt;-&lt;piece&gt;.sdf</c>,
/// named by the rule of SdfBattle.MachineSets (<c>&lt;kind&gt;-&lt;set&gt;-&lt;team&gt;-&lt;piece&gt;</c>, set <c>computer</c>), with the pivots and eye parts of the
/// mushroom set (ticket 24.1, <see cref="MachinesTests"/>), as Unity's MachineBuilder puts every set together: within 0.8 m for the tank and
/// helicopter, about 2 m for the turret, the pieces meeting at their pivots, the scouts' eyes on the turning piece, blue the red shapes.
/// </summary>
public class ComputerMachinesTests
{
    private const string Set = "computer";
    private static readonly string[] Teams = ["red", "blue"];

    private sealed record Piece(string Name, Vector3 Pivot, Vector3 Axis, Piece[] Children, bool Eyes = false);

    // The pivots MachineBuilder hard-codes for every set (the mushroom set's)
    private static readonly Dictionary<string, Piece> Machines = new()
    {
        ["tank"] = new Piece("body", Vector3.Zero, Vector3.Zero,
        [
            new Piece("turret", new Vector3(0, 0.3f, 0), Vector3.UnitY,
                [new Piece("barrel", new Vector3(0, 0.05f, 0.13f), Vector3.UnitX, [])], Eyes: true),
        ]),
        ["helicopter"] = new Piece("body", Vector3.Zero, Vector3.Zero,
        [
            new Piece("rotor", new Vector3(0, 0.56f, 0), Vector3.UnitY, []),
            new Piece("tail-rotor", new Vector3(0.025f, 0.4f, -0.35f), Vector3.UnitX, []),
        ], Eyes: true),
        ["turret"] = new Piece("foot", Vector3.Zero, Vector3.Zero,
        [
            new Piece("head", new Vector3(0, 1.5f, 0), Vector3.UnitY,
                [new Piece("barrel", new Vector3(0, 0, 0.3f), Vector3.UnitX, [])], Eyes: true),
        ]),
    };

    private static string ContentFolder()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static string ModelName(string machine, string team, string piece) => $"{machine}-{Set}-{team}-{piece}";

    private static string ModelPath(string machine, string team, string piece) =>
        Path.Combine(ContentFolder(), "models", "machines", ModelName(machine, team, piece) + ".sdf");

    private static Func<Vector3, float> Distance(string machine, string team, string piece) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(ModelPath(machine, team, piece))).Distance;

    private static IEnumerable<Piece> All(Piece piece) => piece.Children.SelectMany(All).Prepend(piece);

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

    /// <summary>The grid points inside the shape: the extent is up to a step too small on each side, never too big.</summary>
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
            const float most = 0.8f - 0.02f;
            Assert.True(size.X <= most && size.Y <= most && size.Z <= most, $"{machine} {team} turned {angle}: {size}");
            Assert.True(size.Y > 0.5f && Math.Max(size.X, size.Z) > 0.55f, $"{machine} {team}: {size}");
            Assert.InRange(min.Y, -0.01f, 0.03f);
        }
    }

    [Theory]
    [InlineData("red")]
    [InlineData("blue")]
    public void Capacitor_IsAboutTwoMetresHigh_OnTheGround_AndStandsOnARoundFoot(string team)
    {
        var (min, max) = Extent(Assembled("turret", team), new Vector3(-0.9f, -0.1f, -0.9f), new Vector3(0.9f, 2.4f, 1.4f), 0.03f);

        Assert.InRange(max.Y - min.Y, 1.85f, 2.15f);
        Assert.InRange(min.Y, -0.01f, 0.04f);
        // 0.8 m across the foot
        Assert.InRange(max.X - min.X, 0.76f, 0.9f);
    }

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

            // The same eye part as the mushroom set's, its two uses a child each in the piece's prefab (eye_machine.0, eye_machine.1)
            Assert.Contains(meshes, m => m.Name == PartSplitter.BodyName);
            Assert.DoesNotContain(meshes, m => m.Part == "eye");
            var eyes = meshes.Where(m => m.Part == "eye_machine").ToArray();
            if (piece.Eyes)
            {
                var eye = Assert.Single(eyes);
                Assert.Equal(2, eye.Instances.Count);
                Assert.InRange(eye.Detail, 0.03f, 0.5f);
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
            Assert.Equal(MachinesTests.Blue(File.ReadAllText(ModelPath(machine, "red", piece.Name))),
                File.ReadAllText(ModelPath(machine, "blue", piece.Name)));
    }

    /// <summary>The materials a file's Material nodes name.</summary>
    private static string[] Materials(string path) =>
        Regex.Matches(File.ReadAllText(path), @"^\s*Material\s+material=(\S+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToArray();

    [Fact]
    public void EachTeam_UsesItsOwnPaint_NeverTheOthers_AndEveryMaterialExists()
    {
        var materials = Directory.GetFiles(Path.Combine(ContentFolder(), "..", "materials"), "*.sdfmat", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var team in Teams)
        {
            var other = team == "red" ? "blue" : "red";
            foreach (var (machine, root) in Machines)
            foreach (var piece in All(root))
            {
                var used = Materials(ModelPath(machine, team, piece.Name));
                Assert.NotEmpty(used);
                foreach (var material in used)
                {
                    Assert.Contains(material, materials);
                    Assert.DoesNotContain(other, material.Split('_'));
                }
                // The team's colour is the paint of the sleeve, the chip's top and the board; the metal heads, cans and rotors are plain
                if (piece.Name is "body" or "foot")
                    Assert.Contains($"machine_{team}_paint", used);
            }
        }
    }

    [Fact]
    public void TheModelNames_FollowTheMachineSetRule_OfTheMachineBuilder()
    {
        // SdfBattle.MachineSets.TryParsePiece (Unity, Core) reads <kind>-<set>-<team>-<piece>; the same pattern here
        var pattern = new Regex("^(?<kind>tank|helicopter|turret)-(?:(?<set>[a-z0-9]+)-)?(?<team>red|blue)-(?<piece>[a-z0-9-]+)$");
        foreach (var team in Teams)
        foreach (var (machine, root) in Machines)
        foreach (var piece in All(root))
        {
            var match = pattern.Match(ModelName(machine, team, piece.Name));
            Assert.True(match.Success);
            Assert.Equal((machine, Set, team, piece.Name),
                (match.Groups["kind"].Value, match.Groups["set"].Value, match.Groups["team"].Value, match.Groups["piece"].Value));
        }
    }
}
