using System.Globalization;
using Kk.Rigging.Skeletons;

namespace Kk.Rigging;

/// <summary>
/// A model rigged onto an external skeleton (<c>skeleton=synty</c>, docs/skeleton-synty.md): the bones are written by their aliases
/// (format 2 names, so <c>SymmetryX</c> makes the sides), and after the walk they are put on the skeleton file's joints: its names, its
/// order and parents, its rest heads and its joint frames. Bones of the skeleton the model does not write are added without geometry,
/// so the GLB always carries the whole skeleton and every clip path stays whole.
/// </summary>
public sealed partial class RigModel
{
    /// <summary>How far (metres) a written head may be from the skeleton's joint before <c>kk rig check</c> warns.</summary>
    public const double SkeletonHeadTolerance = 0.02;

    /// <summary>The external skeleton the model is rigged onto, or null for a model whose skeleton is its nesting.</summary>
    public SkeletonFile? External { get; private set; }

    /// <summary>The alias (format 2 name) of a bone of an external skeleton, or the bone's own name.</summary>
    public string AliasOf(string bone) => External?.Find(bone)?.Alias ?? bone;

    private void ApplySkeleton(string name, Func<string, SkeletonFile?> skeletons)
    {
        SkeletonFile? file;
        try
        {
            file = skeletons(name);
        }
        catch (SkeletonFormatException e)
        {
            Error(Source.SkeletonLine, $"The skeleton '{name}' cannot be read: {e.Message}");
            return;
        }
        if (file is null)
        {
            Error(Source.SkeletonLine, $"There is no skeleton '{name}'; skeletons are content/skeletons/<name>.skeleton");
            return;
        }
        if (file.Name != name)
        {
            Error(Source.SkeletonLine, $"The skeleton file for '{name}' calls itself '{file.Name}'");
            return;
        }
        External = file;
        var joints = file.Joints;
        var rest = file.ModelRest();

        // Every written bone is a joint of the skeleton, nested as the skeleton nests it
        var placed = new ExpBone?[joints.Count];
        placed[0] = Expanded[0];
        var ok = true;
        foreach (var bone in Expanded.Skip(1))
        {
            var j = file.IndexOfAlias(bone.Name);
            if (j <= 0)
            {
                var hint = file.IndexOf(bone.Name) is var byName and > 0 ? $" (write {joints[byName].Name} as {joints[byName].Alias})" : "";
                Error(bone.Line, $"Bone '{bone.Name}' is not a bone of the {file.Name} skeleton{hint}; its bones are {string.Join(", ", joints.Skip(1).Select(x => x.Alias))}");
                ok = false;
                continue;
            }
            placed[j] = bone;
        }
        if (!ok)
            return;
        foreach (var bone in Expanded.Skip(1))
        {
            var j = file.IndexOfAlias(bone.Name);
            var parent = file.IndexOfAlias(Expanded[bone.Parent].Name);
            var expected = joints[j].Parent;
            if (parent != expected)
            {
                Error(bone.Line, $"Bone '{bone.Name}' ({joints[j].Name}) is inside '{Expanded[bone.Parent].Name}' in the model, but its parent in the {file.Name} skeleton is '{joints[expected].Alias}' ({joints[expected].Name})");
                ok = false;
            }
            var gap = (bone.Head - rest[j].Position).Length;
            if (gap > SkeletonHeadTolerance)
                Warn(bone.Line, string.Create(CultureInfo.InvariantCulture,
                    $"Bone '{bone.Name}': its head is {gap * 100:0.#} cm from the {file.Name} skeleton's joint {joints[j].Name} at ({rest[j].Position.X:0.####}, {rest[j].Position.Y:0.####}, {rest[j].Position.Z:0.####}); the skeleton's joint is used"));
        }
        if (!ok)
            return;

        // The skeleton's order, names, parents, heads and frames
        var remap = new int[Expanded.Count];
        var missing = new List<string>();
        var bones = new List<ExpBone>(joints.Count);
        for (var j = 0; j < joints.Count; j++)
        {
            var b = placed[j];
            if (b is null)
            {
                b = new ExpBone { Hit = HitMode.None, Line = Source.SkeletonLine };
                missing.Add(joints[j].Alias);
            }
            else
                remap[b.Index] = j;
            var oldFrame = b.Frame;
            b.Index = j;
            b.Name = joints[j].Name;
            b.Parent = joints[j].Parent;
            b.Head = rest[j].Position;
            b.Frame = rest[j].Rotation;
            b.HeadL = b.Node is null ? b.Head : b.Chain.Inverse().Apply(b.Head);
            if (placed[j] is null)
            {
                // No tail was written: toward the first child's head, or along the joint's own X (the Synty bones' axis) for a leaf
                var child = joints.FirstOrDefault(c => c.Parent == j);
                var tail = child is not null ? rest[child.Index].Position : b.Head + b.Frame * new D3(0.05, 0, 0);
                b.Tail = (tail - b.Head).Length > 1e-6 ? tail : b.Head + b.Frame * new D3(0.05, 0, 0);
                b.HeadL = b.Head;
                b.TailL = b.Tail;
            }
            else if ((b.Tail - b.Head).Length < 1e-6)
                b.Tail = b.Head + b.Frame * new D3(0, 0.1, 0);
            if (b.Spin is { } spin)
                b.Spin = (b.Frame.Transposed() * (oldFrame * spin)).Normalized();
            bones.Add(b);
        }
        Expanded = bones;
        sockets = sockets.Select(s => s with { Bone = remap[s.Bone] }).ToList();
        if (missing.Count > 0)
            Warn(Source.SkeletonLine, $"The model does not write {missing.Count} bones of the {file.Name} skeleton; they are added without geometry, so clips keep their paths: {string.Join(", ", missing)}");
    }
}
