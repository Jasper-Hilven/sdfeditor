using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using Xunit.Abstractions;

namespace SDFPipeline.Tests.Models;

/// <summary>
/// Ticket 33.1: Chanterelle Prime's pieces (content/models/characters/prime/, and the crown in home/) are one model per moving piece, each
/// with its origin at its pivot, put together in Unity at the pivots below (the same as each file's header and the PrimeBuilder's):
/// assembled he is 1.41 m tall at scout scale (6.5 m at the boss scene's 4.6 times) and every piece touches its parent.
/// </summary>
public class PrimeTests(ITestOutputHelper output)
{
    /// <summary>A piece, its folder under models/, where its pivot is in its parent's space, and the pieces hanging from it.</summary>
    private sealed record Piece(string Name, string Folder, Vector3 Pivot, params Piece[] Children);

    private static readonly Piece Prime = new("prime-stem", "characters/prime", Vector3.Zero,
        new Piece("prime-cap", "characters/prime", new Vector3(0, 0.9216f, 0),
            new Piece("prime-crown", "home", new Vector3(0, 0.187f, 0)),
            new Piece("prime-cape", "characters/prime", new Vector3(0, 0.338f, -0.432f))),
        new Piece("prime-medal", "characters/prime", new Vector3(0, 0.662f, 0.112f)));

    private sealed record Placed(Piece Piece, Func<Vector3, float> Sdf, Matrix4x4 ToWorld);

    private static string Content()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        return content;
    }

    private static Func<Vector3, float> Distance(Piece piece) =>
        new SdfCalculatorCpu().Prepare(SdfProgramFile.Load(Path.Combine(Content(), "models", piece.Folder, piece.Name + ".sdf"))).Distance;

    private static IEnumerable<Piece> All(Piece piece) => piece.Children.SelectMany(All).Prepend(piece);

    /// <summary>The pieces at their pivots (each one's matrix to world), the cap turned forward by <paramref name="kneel"/> radians about x.</summary>
    private static List<Placed> Assemble(float kneel = 0)
    {
        var pieces = new List<Placed>();
        void Add(Piece piece, Matrix4x4 parentToWorld)
        {
            var turn = piece.Name == "prime-cap" ? Matrix4x4.CreateRotationX(kneel) : Matrix4x4.Identity;
            var toWorld = turn * Matrix4x4.CreateTranslation(piece.Pivot) * parentToWorld;
            pieces.Add(new Placed(piece, Distance(piece), toWorld));
            foreach (var child in piece.Children)
                Add(child, toWorld);
        }
        Add(Prime, Matrix4x4.Identity);
        return pieces;
    }

    /// <summary>The distance in world space to the nearest of the pieces chosen, each asked in its own space.</summary>
    private static Func<Vector3, float> Union(List<Placed> pieces, Func<Piece, bool> only)
    {
        var used = pieces.Where(p => only(p.Piece)).Select(p =>
        {
            Matrix4x4.Invert(p.ToWorld, out var toLocal);
            return (p.Sdf, toLocal);
        }).ToList();
        return p => used.Min(x => x.Sdf(Vector3.Transform(p, x.toLocal)));
    }

    /// <summary>The extent of the pieces chosen standing upright: each one's own bounds, moved to its pivot.</summary>
    private static SdfBounds UprightBounds(List<Placed> pieces, Func<Piece, bool> only) =>
        pieces.Where(p => only(p.Piece)).Select(p =>
        {
            var local = SdfBoundsFinder.Find(p.Sdf, 3)!;
            return new SdfBounds(local.Min + p.ToWorld.Translation, local.Max + p.ToWorld.Translation);
        }).Aggregate((a, b) => a.Union(b));

    [Fact]
    public void Prime_AssembledAtHisPivots_IsOneMetreFortyOneTall_StandingOnTheGround()
    {
        var bounds = UprightBounds(Assemble(), _ => true);
        output.WriteLine($"Prime assembled: {bounds.Min} to {bounds.Max}, {bounds.Size.Y:0.000} m tall, {bounds.Size.X:0.00} across");

        Assert.InRange(bounds.Size.Y, 1.41f * 0.95f, 1.41f * 1.05f);
        // The bounds are found on a grid of 2 cm cells, so a cell too big on each side
        Assert.InRange(bounds.Min.Y, -0.04f, 0.04f);
        // A wide funnel cap over a slender stem
        Assert.InRange(bounds.Size.X, 0.8f, 1.2f);
    }

    [Fact]
    public void Prime_TheCrownIsPerchedOnTheCap_SlightlyTooSmall_AndNotAboveItsRim()
    {
        var pieces = Assemble();
        var cap = UprightBounds(pieces, p => p.Name == "prime-cap");
        var crown = UprightBounds(pieces, p => p.Name == "prime-crown");
        output.WriteLine($"cap top {cap.Max.Y:0.000}, crown {crown.Min.Y:0.000} to {crown.Max.Y:0.000}, {crown.Size.X:0.00} across");

        Assert.True(crown.Max.Y <= cap.Max.Y + 0.01f);
        Assert.True(crown.Size.X < 0.5f * cap.Size.X);
    }

    [Fact]
    public void EveryPiece_TouchesItsParent_StandingAndKneeling()
    {
        foreach (var kneel in new[] { 0f, 35 * MathF.PI / 180 })
        {
            var pieces = Assemble(kneel);
            foreach (var (piece, parent) in All(Prime).SelectMany(p => p.Children.Select(c => (c, p))))
            {
                var child = pieces.Single(p => p.Piece == piece);
                var parentSdf = Union(pieces, p => p == parent);
                var box = SdfBoundsFinder.Find(child.Sdf, 3)!;
                // The smallest distance to the parent over the child's grid points inside it
                var best = float.MaxValue;
                const float step = 0.01f;
                for (var x = box.Min.X; x <= box.Max.X; x += step)
                for (var y = box.Min.Y; y <= box.Max.Y; y += step)
                for (var z = box.Min.Z; z <= box.Max.Z; z += step)
                {
                    var local = new Vector3(x, y, z);
                    if (child.Sdf(local) > 0) continue;
                    best = Math.Min(best, parentSdf(Vector3.Transform(local, child.ToWorld)));
                }
                output.WriteLine($"{piece.Name} (cap turned {kneel:0.00}) is {best:0.0000} m from {parent.Name}");
                Assert.True(best <= 0.005f, $"{piece.Name} is {best} m from {parent.Name} (cap turned {kneel})");
            }
        }
    }

    [Fact]
    public void ThePieces_UseTheirOwnMaterials_AndTheScoutsEyesAndHands()
    {
        string Text(Piece p) => File.ReadAllText(Path.Combine(Content(), "models", p.Folder, p.Name + ".sdf"));
        var cap = Prime.Children[0];
        var cape = cap.Children[1];
        var medal = Prime.Children[1];
        Assert.Contains("prime_cape", Text(cape));
        Assert.Contains("prime_fur", Text(cape));
        Assert.Contains("prime_silver", Text(medal));
        Assert.Contains("prime_ribbon", Text(medal));
        Assert.Contains("chanterelle_cap", Text(cap));
        Assert.Contains("chanterelle_flesh", Text(Prime));
        Assert.Contains("part=eye", Text(Prime));
        Assert.Contains("part=hand", Text(Prime));
        var materials = Path.Combine(Content(), "..", "materials", "characters", "prime");
        foreach (var material in new[] { "prime_cape", "prime_fur", "prime_silver", "prime_ribbon", "prime_gold" })
            Assert.True(File.Exists(Path.Combine(materials, material + ".sdfmat")), material);
    }
}
