using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Text;
using SDFPipeline.ProgramFiles;

namespace Kk.Rigging;

/// <summary>A bone after mirroring and parts are expanded, with the maths the rigging needs (internal; <see cref="RigBone"/> is the public view).</summary>
internal sealed class ExpBone
{
    public int Index;
    public string Name = "";
    public int Parent = -1;
    public RigNode? Node;
    /// <summary>Which side of the enclosing symmetry this copy is: +1 (L or F), -1 (R or B), 0 outside any expanded symmetry.</summary>
    public int Sx, Sz;
    /// <summary>The bone node's local space to the model's space; includes the mirror for the mirrored copies.</summary>
    public Aff Chain = Aff.Identity;
    public D3 HeadL, TailL, Head, Tail;
    public M3 Frame = M3.Identity;
    public double Falloff;
    public D3? Spin, Hinge;
    public (double Min, double Max)? Limit;
    public HitMode Hit;
    public double HitRadius;
    public int Line;
}

/// <summary>
/// A model in <c>.sdf</c> format 2 (docs/sdf-format-2.md), read onto the format 1 language: the skeleton, and format 1 programs for the
/// rest pose, for any pose, and for each bone's own geometry. The pipeline parses, meshes and renders those as it does any model.
/// </summary>
public sealed partial class RigModel
{
    internal RigSource Source = null!;
    internal List<ExpBone> Expanded = [];
    private readonly Dictionary<(RigNode, int, int), ExpBone> byNode = [];
    private readonly List<RigIssue> issues = [];
    private List<RigSocket> sockets = [];

    public Skeleton Skeleton { get; private set; } = null!;

    public IReadOnlyList<RigIssue> Issues => issues;

    public bool HasErrors => issues.Any(i => i.IsError);

    /// <summary>Whether the file asks for <c>collider bones</c>: one hit capsule per bone.</summary>
    public bool HitColliders => Source.HitColliders;

    /// <summary>The model's settings other than <c>collider bones</c> (shadows, outline ...), as written.</summary>
    public IReadOnlyList<string> Settings => Source.Settings;

    /// <summary>Whether the model has any bone besides the implicit root.</summary>
    public bool HasBones => Expanded.Count > 1;

    /// <param name="skeletons">
    /// Finds the external skeleton a <c>skeleton=&lt;name&gt;</c> line names; null looks for <c>content/skeletons/&lt;name&gt;.skeleton</c>
    /// from the current directory up.
    /// </param>
    public static RigModel Read(string text, PartSource? parts = null, Func<string, Skeletons.SkeletonFile?>? skeletons = null)
    {
        var model = new RigModel { Source = RigReader.Read(text, parts) };
        model.Build(skeletons ?? (name => Skeletons.SkeletonFile.FindFile(name, Directory.GetCurrentDirectory()) is { } file ? Skeletons.SkeletonFile.Load(file) : null));
        return model;
    }

    /// <summary>Loads a model, finding its parts in content/parts/ of the content folder the file is in, and its skeleton in content/skeletons/ above it.</summary>
    public static RigModel Load(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Read(File.ReadAllText(path), ModelFiles.FindContentFolder(folder) is { } content ? ModelFiles.PartsIn(content) : null,
            name => Skeletons.SkeletonFile.FindFile(name, folder) is { } file ? Skeletons.SkeletonFile.Load(file) : null);
    }

    /// <summary>The model as format 2 text again: the same tree, comments and layout normalised, parts not inlined.</summary>
    public string Write()
    {
        var text = new StringBuilder("format 2\n");
        if (Source.SkeletonName is { } skeleton)
            text.Append("skeleton=").Append(skeleton).Append('\n');
        if (Source.HitColliders)
            text.Append("collider bones\n");
        foreach (var setting in Source.Settings)
            text.Append(setting).Append('\n');
        if (Source.Declarations.Count > 0 || true)
            text.Append('\n');
        foreach (var declaration in Source.Declarations)
            text.Append(declaration).Append('\n');
        WriteNode(text, Source.Scene, 0);
        return text.ToString();
    }

    private static void WriteNode(StringBuilder text, RigNode node, int depth)
    {
        text.Append(' ', depth * 4).Append(node.Text).Append('\n');
        if (node.Kind != NodeKind.PartUse)
            foreach (var child in node.Children)
                WriteNode(text, child, depth + 1);
    }

    internal ExpBone? Lookup(RigNode node, int sx, int sz) => byNode.GetValueOrDefault((node, sx, sz));

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Reading the skeleton

    private void Error(int line, string message) => issues.Add(new RigIssue(line, true, message));
    private void Warn(int line, string message) => issues.Add(new RigIssue(line, false, message));

    private sealed record Walk(Aff Chain, int ParentBone, double? Smooth, string Suffix, int Sx, int Sz, bool InSymmetry);

    private static readonly HashSet<string> AllowedAbove =
    [
        "Union", "SmoothUnion", "Subtraction", "SmoothSubtraction", "Intersection", "SmoothIntersection",
        "Translate", "Rotate", "Scale", "Material", "Color", "SymmetryX", "SymmetryXZ", "Part", "Detail", "$",
    ];

    /// <summary>
    /// A bone's default falloff, the width of its weight blend, in units of the smoothness of the SmoothUnion that joins it. The
    /// pipeline's smooth minimum scales its parameter by 4: two shapes blend while their distances differ by less than 4 x smoothness,
    /// but the surface only moves visibly where they differ by under about 2 x smoothness (the fillet is s/4 high there and a sixteenth
    /// of s at 3 x). Judged on the test worm's bends in the skin renders, 2 gives smooth elbows with the least gap to the exact posed
    /// surface.
    /// </summary>
    public const double SmoothnessReach = 2;

    private static readonly string[] Suffixes = ["_L", "_R", "_F", "_B"];

    private void Build(Func<string, Skeletons.SkeletonFile?> skeletons)
    {
        var root = new ExpBone { Index = 0, Name = "root", Parent = -1, HeadL = D3.Zero, TailL = new D3(0, 0.1, 0), Head = D3.Zero, Tail = new D3(0, 0.1, 0), Hit = HitMode.Auto };
        Expanded.Add(root);
        var socketList = new List<(RigNode Node, string Name, int Bone, Aff Chain)>();
        WalkNode(Source.Scene, new Walk(Aff.Identity, 0, null, "", 0, 0, false), socketList);
        if (Source.SkeletonName is { } skeletonName)
            ApplySkeleton(skeletonName, skeletons);

        // Frames, spin axes, and the public skeleton
        foreach (var b in Expanded)
            Finish(b);
        Skeleton = new Skeleton(
            Expanded.Select(b => new RigBone(b.Index, b.Name, b.Parent, b.Head.ToVector3(), b.Tail.ToVector3(), b.Frame.ToQuaternion(), (float)b.Falloff,
                b.Spin?.ToVector3(), b.Hinge?.ToVector3(), b.Limit is { } l ? ((float)l.Min, (float)l.Max) : null, b.Hit, (float)b.HitRadius)).ToArray(),
            sockets);
        // The sockets were collected during the walk (their frames need no bone frames)
        Skeleton = new Skeleton(Skeleton.Bones, sockets) { External = External?.Name };

        foreach (var bone in Expanded.Skip(1).Where(b => b.Node is not null))
            if (BoneProgram(bone.Index) is null)
                Warn(bone.Line, $"Bone '{bone.Name}' has no geometry of its own, so it gets no skin weight and no hit capsule");
        if (RestProgramOrNull() is null)
            Error(Source.Scene.Line, "The model has no geometry");
    }

    private void WalkNode(RigNode n, Walk w, List<(RigNode, string, int, Aff)> socketList)
    {
        switch (n.Kind)
        {
            case NodeKind.Bone:
                WalkBone(n, w, socketList);
                return;
            case NodeKind.Socket:
                WalkSocket(n, w);
                return;
            case NodeKind.PartUse:
                foreach (var child in n.Children)
                    WalkNode(child, w, socketList);
                return;
        }

        if (!(n.HasBone || n.HasSocket))
            return;

        if ((n.HasBone) && !AllowedAbove.Contains(n.Type))
            Error(n.Line, $"{n.Type} is above a bone but depends on the space it is in, so it would slide over a posed limb; put it inside the bone it shapes");

        var chain = w.Chain;
        var next = w;
        switch (n.Type)
        {
            case "Translate" or "Rotate" or "Scale":
                chain = chain.After(TransformOf(n));
                next = w with { Chain = chain };
                break;
            case "SmoothUnion":
                next = w with { Smooth = Params(n).GetValueOrDefault("smoothness") is RawNumber s ? double.Parse(s.Text, CultureInfo.InvariantCulture) : null };
                break;
            case "SymmetryX" or "SymmetryXZ" when n.HasBone:
                if (w.InSymmetry)
                {
                    Error(n.Line, "A symmetry with bones inside another symmetry with bones is not supported; write the bones of one side and use a Part for the shared shape");
                    break;
                }
                var xz = n.Type == "SymmetryXZ";
                foreach (var sz in xz ? new[] { 1, -1 } : new[] { 0 })
                foreach (var sx in new[] { 1, -1 })
                {
                    var mirror = new Aff(M3.Diagonal(sx, 1, sz == 0 ? 1 : sz), D3.Zero);
                    var suffix = (xz ? (sz > 0 ? "_F" : "_B") : "") + (sx > 0 ? "_L" : "_R");
                    var side = next with { Chain = chain.After(mirror), Suffix = w.Suffix + suffix, Sx = sx, Sz = sz, InSymmetry = true };
                    foreach (var child in n.Children)
                        WalkNode(child, side, socketList);
                }
                return;
            case "SymmetryX" or "SymmetryXZ":
                // Only sockets inside: the geometry stays a fold, the sockets get both sides
                var xzOnlySockets = n.Type == "SymmetryXZ";
                foreach (var sz in xzOnlySockets ? new[] { 1, -1 } : new[] { 0 })
                foreach (var sx in new[] { 1, -1 })
                {
                    var mirror = new Aff(M3.Diagonal(sx, 1, sz == 0 ? 1 : sz), D3.Zero);
                    var suffix = (xzOnlySockets ? (sz > 0 ? "_F" : "_B") : "") + (sx > 0 ? "_L" : "_R");
                    var side = next with { Chain = chain.After(mirror), Suffix = w.Suffix + suffix, Sx = w.Sx, Sz = w.Sz };
                    foreach (var child in n.Children)
                        WalkNode(child, side, socketList);
                }
                return;
        }

        foreach (var child in n.Children)
            WalkNode(child, next, socketList);
    }

    private static readonly Regex NamePattern = new("^[a-z0-9_]+(_[LRFB])*$");

    private string? ReadName(RigNode n, Walk w)
    {
        if (n.Args["name"] is not RawName { Name: var name } || !NamePattern.IsMatch(name))
        {
            Error(n.Line, $"A {n.Type} name is lowercase letters, digits and underscores (with _L, _R, _F, _B only as a mirror suffix), like leg_front");
            return null;
        }
        if (w.InSymmetry && Suffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)))
            Error(n.Line, $"The name '{name}' ends in a mirror suffix, but a symmetry above it adds one; name the bone without it");
        return name + w.Suffix;
    }

    private void WalkBone(RigNode n, Walk w, List<(RigNode, string, int, Aff)> socketList)
    {
        var name = ReadName(n, w) ?? "bone" + Expanded.Count;
        if (name == "root")
            Error(n.Line, "The name 'root' is reserved for the implicit root bone");
        else if (Expanded.Any(b => b.Name == name))
            Error(n.Line, $"There are two bones named '{name}' (after mirroring and parts); bone names must be unique");

        var bone = new ExpBone { Index = Expanded.Count, Name = name, Parent = w.ParentBone, Node = n, Sx = w.Sx, Sz = w.Sz, Chain = w.Chain, Line = n.Line };
        bone.HeadL = VectorOf(n, "head", null, this) ?? D3.Zero;
        bone.TailL = VectorOf(n, "tail", bone.HeadL + new D3(0, 0.1, 0), this)!.Value;
        bone.Head = w.Chain.Apply(bone.HeadL);
        bone.Tail = w.Chain.Apply(bone.TailL);
        if ((bone.Tail - bone.Head).Length < 1e-6)
        {
            Error(n.Line, $"Bone '{name}' has its tail at its head; the tail sets the bone's direction and length");
            bone.Tail = bone.Head + new D3(0, 0.1, 0);
        }

        bone.Falloff = n.Args.TryGetValue("falloff", out var f)
            ? NumberOf(f, n, "falloff", this) is var v && v >= 0 ? v : 0
            : (w.Smooth ?? 0) * SmoothnessReach;
        if (n.Args.TryGetValue("falloff", out _) && NumberOf(n.Args["falloff"], n, "falloff", this) < 0)
            Error(n.Line, "falloff is a width in metres, 0 or more");

        bone.Hit = HitMode.Auto;
        if (n.Args.TryGetValue("hit", out var hit))
        {
            switch (hit)
            {
                case RawName { Name: "auto" }: break;
                case RawName { Name: "none" }: bone.Hit = HitMode.None; break;
                case RawNumber r when double.Parse(r.Text, CultureInfo.InvariantCulture) > 0:
                    bone.Hit = HitMode.Radius;
                    bone.HitRadius = double.Parse(r.Text, CultureInfo.InvariantCulture);
                    break;
                default: Error(n.Line, "hit is auto, none, or a radius in metres"); break;
            }
        }

        var roll = n.Args.TryGetValue("roll", out var rollValue) ? NumberOf(rollValue, n, "roll", this) : 0;
        bone.Frame = FrameOf(bone.Head, bone.Tail, roll);

        // spin / hinge / limit, in the bone frame's coordinates
        if (n.Args.TryGetValue("spin", out var spin))
        {
            var axis = AxisOf(spin, n, "spin", true, this);
            if (axis is { } a)
                bone.Spin = spin is RawName { Name: "bone" }
                    ? D3.UnitY
                    : (bone.Frame.Transposed() * (w.Chain.Linear * a)).Normalized();
        }
        if (n.Args.TryGetValue("hinge", out var hinge))
            bone.Hinge = AxisOf(hinge, n, "hinge", false, this)?.Normalized();
        if (n.Args.TryGetValue("limit", out var limit))
        {
            if (limit is RawVector { Components.Length: 2 } pair)
            {
                var (min, max) = (double.Parse(pair.Components[0].Text, CultureInfo.InvariantCulture), double.Parse(pair.Components[1].Text, CultureInfo.InvariantCulture));
                bone.Limit = (min, max);
                if (bone.Hinge is not null && min > max)
                    Error(n.Line, "limit=(min, max) needs min <= max");
            }
            else
                Error(n.Line, "limit is a pair of radians like limit=(-0.5, 0.8)");
        }
        if (bone.Spin is not null && bone.Hinge is not null)
            Error(n.Line, "A bone either spins freely (spin) or is a hinge, not both");

        Expanded.Add(bone);
        byNode[(n, w.Sx, w.Sz)] = bone;
        var inside = w with { ParentBone = bone.Index, Smooth = null };
        foreach (var child in n.Children)
            WalkNode(child, inside, socketList);
    }

    private void WalkSocket(RigNode n, Walk w)
    {
        var name = ReadName(n, w) ?? "socket" + sockets.Count;
        if (sockets.Any(s => s.Name == name))
            Error(n.Line, $"There are two sockets named '{name}' (after mirroring and parts); socket names must be unique");
        var position = VectorOf(n, "at", D3.Zero, this)!.Value;
        var dir = VectorOf(n, "dir", D3.UnitZ, this)!.Value;
        var up = VectorOf(n, "up", D3.UnitY, this)!.Value;
        var (worldDir, worldUp) = ((w.Chain.Linear * dir).Normalized(), (w.Chain.Linear * up).Normalized());
        sockets.Add(new RigSocket(name, w.ParentBone, w.Chain.Apply(position).ToVector3(), worldDir.ToVector3(), worldUp.ToVector3()));
    }

    private static void Finish(ExpBone bone) { }

    /// <summary>
    /// The rest orientation of a bone's frame in model space: +Y from head to tail, +X the projection of the model's +X (or +Z when the
    /// bone lies along X), turned about Y by <paramref name="roll"/> radians; Z = X cross Y.
    /// </summary>
    internal static M3 FrameOf(D3 head, D3 tail, double roll)
    {
        var y = (tail - head).Normalized();
        var reference = Math.Abs(y.Dot(D3.UnitX)) > 0.999 ? D3.UnitZ : D3.UnitX;
        var x = (reference - y * y.Dot(reference)).Normalized();
        if (roll != 0)
            x = M3.Rotation(y, roll) * x;
        return M3.FromColumns(x, y, x.Cross(y));
    }

    private static D3? AxisOf(RawValue value, RigNode n, string what, bool allowBone, RigModel model)
    {
        switch (value)
        {
            case RawName { Name: "x" }: return D3.UnitX;
            case RawName { Name: "y" }: return D3.UnitY;
            case RawName { Name: "z" }: return D3.UnitZ;
            case RawName { Name: "bone" } when allowBone: return D3.UnitY;
            case RawVector { Components.Length: 3 } v:
                var d = new D3(Num(v.Components[0]), Num(v.Components[1]), Num(v.Components[2]));
                if (d.Length > 1e-9)
                    return d.Normalized();
                break;
        }
        model.Error(n.Line, $"{what} is an axis: x, y, z{(allowBone ? ", bone" : "")} or a vector like (0, 1, 0)");
        return null;
    }

    private static double Num(RawNumber n) => double.Parse(n.Text, CultureInfo.InvariantCulture);

    private static double NumberOf(RawValue value, RigNode n, string what, RigModel model)
    {
        if (value is RawNumber number)
            return Num(number);
        model.Error(n.Line, $"{what} is a number");
        return 0;
    }

    private static D3? VectorOf(RigNode n, string key, D3? fallback, RigModel model)
    {
        if (!n.Args.TryGetValue(key, out var value))
            return fallback;
        if (value is RawVector { Components.Length: 3 } v)
            return new D3(Num(v.Components[0]), Num(v.Components[1]), Num(v.Components[2]));
        model.Error(n.Line, $"{key} is a point like (0, 1, 0)");
        return fallback;
    }

    /// <summary>The parameters of a plain node's line (name=value pairs); constant values only.</summary>
    private static Dictionary<string, RawValue> Params(RigNode n)
    {
        var values = new Dictionary<string, RawValue>();
        try
        {
            var cursor = new Cursor(new SourceLine(n.Line, 0, n.Text, []));
            cursor.TryReadName();
            while (!cursor.AtEnd)
            {
                var key = cursor.ReadName("a parameter");
                cursor.Expect('=');
                values[key] = cursor.ReadValue();
            }
        }
        catch (SdfProgramFileException)
        {
        }
        return values;
    }

    /// <summary>The local-to-parent map of a Translate, Rotate or uniform Scale above a bone or socket (identity, with an error, when it cannot be read).</summary>
    private Aff TransformOf(RigNode n)
    {
        var p = Params(n);
        RawVector? Vec(string key) => p.GetValueOrDefault(key) as RawVector is { Components.Length: 3 } v ? v : null;
        switch (n.Type)
        {
            case "Translate" when Vec("translation") is { } t:
                return new Aff(M3.Identity, new D3(Num(t.Components[0]), Num(t.Components[1]), Num(t.Components[2])));
            case "Rotate" when Vec("rotation") is { } r:
                return new Aff(M3.EulerXyz(Num(r.Components[0]), Num(r.Components[1]), Num(r.Components[2])), D3.Zero);
            case "Scale" when Vec("scale") is { } s:
                var (sx, sy, sz) = (Num(s.Components[0]), Num(s.Components[1]), Num(s.Components[2]));
                if (Math.Abs(sx - sy) > 1e-9 || Math.Abs(sx - sz) > 1e-9)
                    Error(n.Line, "A non-uniform Scale above a bone would stretch a posed limb; scale uniformly, or put the Scale inside the bone");
                if (sx <= 0)
                    Error(n.Line, "A Scale above a bone must be positive");
                return new Aff(M3.Scale(sx > 0 ? sx : 1), D3.Zero);
        }
        Error(n.Line, $"{n.Type} above a bone must be given plain numbers (not variables), so the bone's position in the model can be worked out");
        return Aff.Identity;
    }
}
