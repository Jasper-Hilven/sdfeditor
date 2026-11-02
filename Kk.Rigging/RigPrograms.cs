using System.Globalization;
using System.Text;
using SDFPipeline.ProgramFiles;

namespace Kk.Rigging;

/// <summary>A node of a format 1 program being written; null stands for "nothing here".</summary>
internal sealed class OutNode(string text, List<OutNode>? children = null)
{
    public string Text { get; } = text;
    public List<OutNode> Children { get; } = children ?? [];

    public void Render(StringBuilder sb, int depth)
    {
        sb.Append(' ', depth * 4).Append(Text).Append('\n');
        foreach (var child in Children)
            child.Render(sb, depth + 1);
    }
}

public sealed partial class RigModel
{
    private static readonly HashSet<string> Booleans = ["Union", "SmoothUnion", "Subtraction", "SmoothSubtraction", "Intersection", "SmoothIntersection", "Xor"];

    private enum Mode { Rest, Stripped, Posed, Path }

    /// <summary>What the builder knows while it goes down the tree.</summary>
    private sealed record Ctx(Mode Mode, Pose? Pose = null, ExpBone? Target = null, int Sx = 0, int Sz = 0);

    /// <summary>
    /// The model with its bones and sockets taken out: a format 1 program for the rest pose, as the pipeline meshes and renders it. A
    /// bone is a marker, so everything is written where it was; symmetries stay folds.
    /// </summary>
    public string RestProgram() => RestProgramOrNull() ?? throw new InvalidOperationException("The model has no geometry");

    private string? RestProgramOrNull() => Text(Make(Source.Scene, new Ctx(Mode.Rest)));

    /// <summary>
    /// The model posed: each posed bone's subtree wrapped in Translate(head), Rotate, Translate(-head) (and a Translate for the pose's
    /// translation), so the pipeline evaluates and renders the exact posed shape, blends and all. A symmetry that contains bones is
    /// written as its two (four) sides, each clipped to its half-space, so the sides can be posed apart.
    /// </summary>
    public string PosedProgram(Pose pose)
    {
        foreach (var name in pose.Bones.Keys)
            if (Skeleton.IndexOf(name) < 0)
                throw new ArgumentException($"The pose names a bone '{name}' that the model does not have (it has {string.Join(", ", Skeleton.Bones.Select(b => b.Name))})");
        return Text(Make(Source.Scene, new Ctx(Mode.Posed, pose))) ?? throw new InvalidOperationException("The model has no geometry");
    }

    /// <summary>
    /// A format 1 program for the geometry the bone owns on its own, in the rest pose: the bone's subtree without its child bones' subtrees,
    /// inside the transforms (and, for a mirrored bone, the half-space) it sits in. Null when the bone has no geometry of its own.
    /// Distances to it are what skin weights and hit capsules are fitted with.
    /// </summary>
    public string? BoneProgram(int bone)
    {
        var target = Expanded[bone];
        if (bone != 0 && target.Node is null)
            return null; // a bone of an external skeleton that the model does not write
        return Text(bone == 0 ? Make(Source.Scene, new Ctx(Mode.Stripped)) : MakePath(target.Node!, new Ctx(Mode.Path, Target: target, Sx: target.Sx, Sz: target.Sz)));
    }

    private string? Text(OutNode? scene)
    {
        if (scene is null)
            return null;
        var sb = new StringBuilder("format 1\n");
        foreach (var setting in Source.Settings)
            sb.Append(setting).Append('\n');
        sb.Append('\n');
        foreach (var declaration in Source.Declarations)
            sb.Append(declaration).Append('\n');
        scene.Render(sb, 0);
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------------------------------------------

    private OutNode? Make(RigNode n, Ctx c)
    {
        switch (n.Kind)
        {
            case NodeKind.Socket:
                return null;
            case NodeKind.PartUse:
                return Make(n.Children[0], c);
            case NodeKind.Bone:
                if (c.Mode == Mode.Stripped)
                    return null;
                var inner = Combine("Union", n.Children.Select(child => Make(child, c)));
                if (c.Mode != Mode.Posed || inner is null)
                    return inner;
                return Posed(Lookup(n, c.Sx, c.Sz)!, c.Pose!, inner);
        }

        if (c.Mode == Mode.Posed && n.IsSymmetry && n.HasBone)
            return PosedSymmetry(n, c);

        return Assemble(n, n.Children.Select(child => Make(child, c)).ToList());
    }

    /// <summary>Removes what is not there (a null child) from a node the way its type allows, so the result is still a valid program.</summary>
    private static OutNode? Assemble(RigNode n, List<OutNode?> kids)
    {
        if (n.Type is "Union" or "SmoothUnion" or "Intersection" or "SmoothIntersection")
        {
            var present = kids.OfType<OutNode>().ToList();
            return present.Count switch { 0 => null, 1 => present[0], _ => new OutNode(n.Text, present) };
        }
        if (n.Type is "Subtraction" or "SmoothSubtraction" or "Xor")
        {
            if (kids is [var a, var b])
                return a is null ? null : b is null ? a : new OutNode(n.Text, [a, b]);
        }
        if (kids.Any(k => k is null))
            return null;
        return new OutNode(n.Text, kids.OfType<OutNode>().ToList());
    }

    private static OutNode? Combine(string type, IEnumerable<OutNode?> kids)
    {
        var present = kids.OfType<OutNode>().ToList();
        return present.Count switch { 0 => null, 1 => present[0], _ => new OutNode(type, present) };
    }

    // Posing -------------------------------------------------------------------------------------------------------------------------

    private static string N(double value)
    {
        var rounded = Math.Round(value, 7);
        return (rounded == 0 ? 0 : rounded).ToString("0.#######", CultureInfo.InvariantCulture);
    }

    private static string V(D3 v) => $"({N(v.X)}, {N(v.Y)}, {N(v.Z)})";

    private OutNode Posed(ExpBone bone, Pose pose, OutNode inner)
    {
        var p = pose.Get(bone.Name);
        if (p.IsIdentity)
            return inner;

        // The rotation is given in the bone's frame; the wrapper sits in the bone node's local space, whose map to the model is bone.Chain
        var linear = bone.Chain.Linear;
        var inverse = linear.Inverse();
        var rotationWorld = bone.Frame * M3.FromQuaternion(p.Rotation) * bone.Frame.Transposed();
        var rotationLocal = inverse * rotationWorld * linear;
        var node = inner;
        if (p.Rotation != System.Numerics.Quaternion.Identity)
        {
            node = new OutNode($"Translate translation={V(-bone.HeadL)}", [node]);
            node = new OutNode($"Rotate rotation={V(rotationLocal.ToEulerXyz())}", [node]);
            node = new OutNode($"Translate translation={V(bone.HeadL)}", [node]);
        }
        if (p.Translation != System.Numerics.Vector3.Zero)
        {
            var local = inverse * (bone.Frame * D3.From(p.Translation));
            node = new OutNode($"Translate translation={V(local)}", [node]);
        }
        return node;
    }

    private OutNode? PosedSymmetry(RigNode n, Ctx c)
    {
        var xz = n.Type == "SymmetryXZ";
        var sides = new List<OutNode>();
        foreach (var sz in xz ? new[] { 1, -1 } : new[] { 0 })
        foreach (var sx in new[] { 1, -1 })
        {
            var inner = Combine("Union", n.Children.Select(child => Make(child, c with { Sx = sx, Sz = sz })));
            if (inner is not null)
                sides.Add(Clipped(new OutNode(n.Text, [inner]), sx, sz));
        }
        return Combine("Union", sides);
    }

    /// <summary>The symmetry's geometry on one side only: intersected with the half-space (quarter-space) that side is.</summary>
    private static OutNode Clipped(OutNode symmetric, int sx, int sz)
    {
        var parts = new List<OutNode> { symmetric, new($"Plane normal=({-sx}, 0, 0) height=0") };
        if (sz != 0)
            parts.Add(new OutNode($"Plane normal=(0, 0, {-sz}) height=0"));
        return new OutNode("Intersection", parts);
    }

    // Own geometry -------------------------------------------------------------------------------------------------------------------

    /// <summary>The program of the geometry the target bone owns: walks the path from the top down to the bone's node.</summary>
    private OutNode? MakePath(RigNode bone, Ctx c)
    {
        var target = c.Target!;
        return Path(Source.Scene);

        OutNode? Path(RigNode n)
        {
            if (n == target.Node)
                return Combine("Union", n.Children.Select(child => Make(child, new Ctx(Mode.Stripped))));
            var next = n.Children.FirstOrDefault(child => child.Contains(target.Node!));
            if (next is null)
                return null;
            if (n.Kind is NodeKind.Bone or NodeKind.PartUse)
                return Path(next);
            if (Booleans.Contains(n.Type) || n.Type == "Detail")
                return Path(next);
            var inner = Path(next);
            if (inner is null)
                return null;
            var node = new OutNode(n.Text, [inner]);
            return n.IsSymmetry && n.HasBone ? Clipped(node, target.Sx, target.Sz) : node;
        }
    }
}
