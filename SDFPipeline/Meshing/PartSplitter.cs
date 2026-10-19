using System.Numerics;
using SDFPipeline.Rendering;
using SDFProject.Calculation;
using SDFProject.MeshExport;
using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Meshing;

/// <summary>A program to mesh on its own, named after what it is: the model's own geometry, or one of its parts.</summary>
/// <param name="Detail">
/// How many times the vertices its size gives the mesh: the <see cref="SDFProject.ProgramDefinition.Detail"/> at the root of the model,
/// and for a part, times the one at the root of the part's file.
/// </param>
public sealed record MeshSource(string Name, SDFProgram Program, float Detail = 1)
{
    /// <summary>The part this mesh is of, or null for the body.</summary>
    public string? Part { get; init; }

    /// <summary>
    /// For a part meshed once in its own file's space: where each of its uses puts it, from the part's space into the model's (row
    /// vectors, as System.Numerics has them: a point is p × matrix). Empty for a mesh made in place, in the model's space.
    /// </summary>
    public IReadOnlyList<Matrix4x4> Instances { get; init; } = [];
}

/// <summary>
/// Splits a model into one program per mesh by rewriting its graph: the body is the model with its parts taken out, and each part is
/// the model with only the branches that lead to that part (all of its uses together, placed and mirrored as in the model).
/// <para>
/// A part belongs to its own mesh where it adds to the shape: under unions, transforms, modifiers like Material or Round, and as the
/// kept side of a subtraction. A part that only shapes something else, as the cutter of a subtraction or inside an intersection, stays
/// in that mesh. Meshes overlap where the model blends a part in with a smooth union: each mesh is the shape on its own, so the fillet
/// between them is lost, and the part inside a smooth union is meshed without the blend.
/// </para>
/// <para>
/// The Detail nodes at the roots of the model and of its parts' files leave the graph for <see cref="MeshSource.Detail"/>: they do not
/// change the shape, and a model that is a Detail around nothing but parts then has no body left to mesh.
/// </para>
/// <para>
/// Part instances: a part used more than once, every use reaching it through rigid placements only, is meshed once in its own file's
/// space, and each use becomes an instance (<see cref="MeshSource.Instances"/>), so a castle's four towers cost one mesh. Rigid are
/// Translate, Rotate, a uniform Scale, SymmetryX and SymmetryXZ (each a copy and its mirror image, as long as the part stays on its side
/// of the mirror), LimitedRepetition with whole limits (a copy per cell, as long as the part stays in its cell) and Repetition without
/// spacing. A Material or Color above a use is allowed: the innermost one is the part's look there, and uses with different looks get a
/// mesh each, the first named after the part and the others part@material (part@2 for a colour). Anything else on the way, such as a
/// smooth union, a non-uniform Scale, Distort or Displace, a subtraction or an intersection, would change the part per use; such a part
/// is meshed in place as above, all of its uses together, with a warning naming the node. A part used once is always meshed in place,
/// so models without repeated parts mesh as they did before instances existed.
/// </para>
/// </summary>
public static class PartSplitter
{
    public const string BodyName = "body";

    /// <summary>The program with all pointers replaced by the nodes they point to, which also evaluates faster.</summary>
    public static SDFProgram Expanded(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);
        return program.MapRoots(tables.Expand);
    }

    /// <summary>The body first (left out when the model is nothing but parts), then each part in name order.</summary>
    /// <param name="warnings">Gets a line for each part used several times that has to be meshed in place, and why.</param>
    public static IReadOnlyList<MeshSource> Split(SDFProgram program, ICollection<string>? warnings = null)
    {
        var expanded = Expanded(program);
        var rewriter = new Rewriter(expanded);
        var modelDetail = DetailOf(expanded.scene);
        var sources = new List<MeshSource>();
        if (rewriter.Strip(expanded.scene) is not Empty)
            sources.Add(new MeshSource(BodyName, expanded with { scene = rewriter.Strip(expanded.scene) }, modelDetail));
        var uses = new UseFinder(expanded, rewriter).Find();
        foreach (var (id, name) in rewriter.Parts.OrderBy(p => p.Value, StringComparer.Ordinal))
        {
            var detail = modelDetail * DetailOf(rewriter.Value(id));
            if (Instanced(name, uses.GetValueOrDefault(id, []), warnings) is { } groups)
            {
                foreach (var (meshName, look, placements) in groups)
                {
                    var shape = rewriter.Strip(rewriter.Value(id));
                    var scene = look switch
                    {
                        SDFProject.ProgramDefinition.Material m => m with { primitive = shape },
                        Color c => c with { primitive = shape },
                        _ => shape,
                    };
                    sources.Add(new MeshSource(meshName, expanded with { scene = scene }, detail) { Part = name, Instances = placements });
                }
            }
            else if (rewriter.Keep(expanded.scene, id) is { } kept)
                sources.Add(new MeshSource(name, expanded with { scene = kept }, detail) { Part = name });
        }
        return sources;
    }

    /// <summary>
    /// The meshes of a part used more than once through rigid placements only: one per look, with the placement of each use under it.
    /// Null when the part is meshed in place: used once, or with a use that is not rigid, which <paramref name="warnings"/> hears of.
    /// </summary>
    private static List<(string Name, SDFEvaluable? Look, Matrix4x4[] Placements)>? Instanced(string part, IReadOnlyList<PartUse> uses,
        ICollection<string>? warnings)
    {
        var copies = uses.Sum(u => (long)u.Copies);
        if (copies < 2)
            return null;
        if (uses.FirstOrDefault(u => u.NotRigid is not null) is { } bent)
        {
            var times = copies >= EndlessCopies ? "endlessly" : $"{copies} times";
            warnings?.Add($"Part '{part}' is used {times} but meshed in place, all uses as one mesh: a use is {bent.NotRigid}");
            return null;
        }

        var groups = new List<(string Name, SDFEvaluable? Look, Matrix4x4[] Placements)>();
        foreach (var group in uses.GroupBy(u => LookKey(u.Look)))
        {
            var look = group.First().Look;
            var name = groups.Count == 0 ? part
                : look is SDFProject.ProgramDefinition.Material m ? $"{part}@{m.material}"
                : $"{part}@{groups.Count + 1}";
            groups.Add((name, look, group.Select(u => u.Placement).ToArray()));
        }
        return groups;
    }

    /// <summary>What tells looks apart: the material's name, or the colour's value as written.</summary>
    private static string LookKey(SDFEvaluable? look) => look switch
    {
        SDFProject.ProgramDefinition.Material m => "material " + m.material,
        Color c => "color " + c.color,
        _ => "",
    };

    /// <summary>The detail the model sets for all of its meshes: that of the Detail node at its root, or 1 without one.</summary>
    public static float ModelDetail(SDFProgram program) => DetailOf(new PointerTables(program.pointers).Expand(program.scene));

    /// <summary>
    /// The detail a file's shape sets, read from its root only: a Detail node anywhere else has no effect, which the file reader
    /// rejects, so a program made in code that has one there is meshed as if it had none.
    /// </summary>
    private static float DetailOf(Evaluable root) => root is Detail { detail: ConstValueFloat { value: var detail } } ? detail : 1;

    /// <summary>
    /// One use of a part: where it puts the part, the innermost Material or Color above it, and, when the way to it is not rigid, the
    /// node that makes it so, in words, with how many copies of the part that one use stands for, as it is not split into them.
    /// </summary>
    private sealed record PartUse(string PartId, Matrix4x4 Placement, SDFEvaluable? Look, string? NotRigid, int Copies = 1);

    /// <summary>The copies an endless Repetition stands for.</summary>
    private const int EndlessCopies = int.MaxValue;

    /// <summary>
    /// Finds every use of every part that adds to the shape or shapes something else, expanding symmetries and repetitions into a use
    /// per copy, with its placement worked out on the way down.
    /// </summary>
    private sealed class UseFinder(SDFProgram program, Rewriter rewriter)
    {
        private static readonly Matrix4x4 MirrorX = Matrix4x4.CreateScale(-1, 1, 1);
        private static readonly Matrix4x4 MirrorZ = Matrix4x4.CreateScale(1, 1, -1);

        private readonly SdfCalculatorCpu.Prepared values = new SdfCalculatorCpu().Prepare(program);
        private readonly Dictionary<string, SdfBounds?> bounds = [];

        public Dictionary<string, List<PartUse>> Find() =>
            Uses(program.scene).GroupBy(u => u.PartId).ToDictionary(g => g.Key, g => g.ToList());

        private IEnumerable<PartUse> Uses(SDFEvaluable node) => node switch
        {
            VariableReferenceSDF r when rewriter.Parts.ContainsKey(r.variableId) => [new PartUse(r.variableId, Matrix4x4.Identity, null, null)],
            VariableReferenceSDF r => Uses(rewriter.Value(r.variableId)),
            Union e => e.primitives.SelectMany(Uses),
            Detail e => Uses(e.primitive),
            SDFFunctionCall => [],
            Translate e => Placed(Uses(e.primitive), Matrix4x4.CreateTranslation(values.Value(e.translation))),
            Rotate e => Placed(Uses(e.primitive), Matrix4x4.CreateFromQuaternion(RotationXyz(values.Value(e.rotation)))),
            Scale e => values.Value(e.scale) is var s && s.X == s.Y && s.Y == s.Z && s.X > 0
                ? Placed(Uses(e.primitive), Matrix4x4.CreateScale(s.X))
                : NotRigid(Uses(e.primitive), "under a non-uniform Scale"),
            SymmetryX e => Mirrored(Uses(e.primitive), "SymmetryX", [Matrix4x4.Identity, MirrorX], mirrorZ: false),
            SymmetryXZ e => Mirrored(Uses(e.primitive), "SymmetryXZ", [Matrix4x4.Identity, MirrorX, MirrorZ, MirrorX * MirrorZ], mirrorZ: true),
            Repetition e => values.Value(e.spacing) == Vector3.Zero
                ? Uses(e.primitive)
                : NotRigid(Uses(e.primitive), "under an endless Repetition").Select(u => u with { Copies = EndlessCopies }),
            LimitedRepetition e => Repeated(Uses(e.primitive), values.Value(e.spacing), values.Value(e.limits)),
            SDFProject.ProgramDefinition.Material or Color => Uses(EvaluableChildren.Children(node).OfType<SDFEvaluable>().Single())
                .Select(u => u.Look is null ? u with { Look = node } : u),
            Subtraction e => NotRigid(Uses(e.a), "the kept side of a Subtraction").Concat(NotRigid(Uses(e.b), "the cutter of a Subtraction")),
            SmoothSubtraction e => NotRigid(Uses(e.a), "the kept side of a SmoothSubtraction").Concat(NotRigid(Uses(e.b), "the cutter of a SmoothSubtraction")),
            _ => NotRigid(EvaluableChildren.Children(node).OfType<SDFEvaluable>().SelectMany(Uses), $"inside {Named(node)}"),
        };

        private static string Named(SDFEvaluable node) =>
            node.GetType().Name is var name && "AEIOU".Contains(name[0]) ? $"an {name}" : $"a {name}";

        /// <summary>The rotation Rotate turns its shape by: about x, then y, then z, in radians, as the calculator undoes it.</summary>
        private static Quaternion RotationXyz(Vector3 r) => Quaternion.Concatenate(
            Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, r.X), Quaternion.CreateFromAxisAngle(Vector3.UnitY, r.Y)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, r.Z));

        private static IEnumerable<PartUse> Placed(IEnumerable<PartUse> uses, Matrix4x4 placement) =>
            uses.Select(u => u with { Placement = u.Placement * placement });

        private static IEnumerable<PartUse> NotRigid(IEnumerable<PartUse> uses, string why) =>
            uses.Select(u => u.NotRigid is null ? u with { NotRigid = why } : u);

        /// <summary>
        /// A copy of each use per mirror image. The symmetry shows only what is on the positive side of its mirrors: a use wholly behind
        /// one is never seen (as the image of an inner symmetry often is) and is left out, and a part that reaches across would be cut
        /// there and joined to its image, which copies cannot show, so that use is not rigid.
        /// </summary>
        private IEnumerable<PartUse> Mirrored(IEnumerable<PartUse> uses, string node, Matrix4x4[] mirrors, bool mirrorZ)
        {
            foreach (var use in uses)
            {
                if (use.NotRigid is null && PlacedBounds(use) is { } placed)
                {
                    var slack = Slack(placed);
                    if (placed.Max.X <= slack || mirrorZ && placed.Max.Z <= slack)
                        continue;
                    if (placed.Min.X < -slack || mirrorZ && placed.Min.Z < -slack)
                    {
                        yield return use with { NotRigid = $"across the mirror of a {node}", Copies = Many(use.Copies, mirrors.Length) };
                        continue;
                    }
                }
                if (use.NotRigid is not null)
                    yield return use with { Copies = Many(use.Copies, mirrors.Length) };
                else
                    foreach (var mirror in mirrors)
                        yield return use with { Placement = use.Placement * mirror };
            }
        }

        /// <summary>
        /// A copy of each use per cell, as RepeatLimited picks the cell: whole limits only, and the part must stay inside its cell along
        /// each axis it repeats on, as otherwise the cell walls would cut it.
        /// </summary>
        private IEnumerable<PartUse> Repeated(IEnumerable<PartUse> uses, float spacing, Vector3 limits)
        {
            if (spacing == 0)
                return uses;
            var cells = (int)Math.Min((2L * (int)limits.X + 1) * (2L * (int)limits.Y + 1) * (2L * (int)limits.Z + 1), EndlessCopies);
            if (limits.X < 0 || limits.Y < 0 || limits.Z < 0 || limits != Vector3.Round(limits))
                return NotRigid(uses, "under a LimitedRepetition whose limits are not whole numbers").Select(u => u with { Copies = Many(u.Copies, 2) });
            return uses.SelectMany(use =>
            {
                var half = spacing / 2;
                if (use.NotRigid is null && PlacedBounds(use) is { } placed && (
                        limits.X > 0 && (placed.Min.X < -half - Slack(placed) || placed.Max.X > half + Slack(placed))
                        || limits.Y > 0 && (placed.Min.Y < -half - Slack(placed) || placed.Max.Y > half + Slack(placed))
                        || limits.Z > 0 && (placed.Min.Z < -half - Slack(placed) || placed.Max.Z > half + Slack(placed))))
                    return [use with { NotRigid = "wider than the cell of a LimitedRepetition", Copies = Many(use.Copies, cells) }];
                if (use.NotRigid is not null)
                    return [use with { Copies = Many(use.Copies, cells) }];
                return
                    from x in Cells(limits.X)
                    from y in Cells(limits.Y)
                    from z in Cells(limits.Z)
                    select use with { Placement = use.Placement * Matrix4x4.CreateTranslation(spacing * new Vector3(x, y, z)) };
            });
        }

        private static int Many(int copies, int times) => (int)Math.Min((long)copies * times, EndlessCopies);

        private static IEnumerable<int> Cells(float limit) => Enumerable.Range(-(int)limit, 2 * (int)limit + 1);

        /// <summary>
        /// The bounds finder's boxes are a little larger than the shape, about a sixteenth of its size at most, so a part that only
        /// touches a mirror or a cell wall is not taken for one that crosses it.
        /// </summary>
        private static float Slack(SdfBounds placed) => placed.LongestSide / 16;

        /// <summary>The part's bounds where the use puts it, as a box round the placed corners; null for a part with no finite shape.</summary>
        private SdfBounds? PlacedBounds(PartUse use)
        {
            if (!bounds.TryGetValue(use.PartId, out var own))
                bounds[use.PartId] = own = RenderFramer.ModelBounds(program with { scene = rewriter.Strip(rewriter.Value(use.PartId)) });
            if (own is null)
                return null;
            var corners = Enumerable.Range(0, 8).Select(i => Vector3.Transform(new Vector3(
                (i & 1) == 0 ? own.Min.X : own.Max.X, (i & 2) == 0 ? own.Min.Y : own.Max.Y, (i & 4) == 0 ? own.Min.Z : own.Max.Z), use.Placement)).ToArray();
            return new SdfBounds(corners.Aggregate(Vector3.Min), corners.Aggregate(Vector3.Max));
        }
    }

    private sealed class Rewriter(SDFProgram program)
    {
        private readonly Dictionary<string, VariableDeclaration> variables = program.sceneVariables.ToDictionary(v => v.id);

        /// <summary>The variables that are parts themselves, by id, with the part's name.</summary>
        public Dictionary<string, string> Parts { get; } = program.sceneVariables
            .Where(v => SDFProject.ProgramDefinition.Parts.IsPartItself(v.name))
            .ToDictionary(v => v.id, v => SDFProject.ProgramDefinition.Parts.PartOf(v.name)!);

        public SDFEvaluable Value(string variableId) => (SDFEvaluable)variables[variableId].value;

        /// <summary>The shape with every part that adds to it taken out; the variables on the way are written out in place.</summary>
        public SDFEvaluable Strip(SDFEvaluable node) => node switch
        {
            VariableReferenceSDF r when Parts.ContainsKey(r.variableId) => new Empty(),
            VariableReferenceSDF r => Strip(Value(r.variableId)),
            Union e => Combined(e.primitives.Select(Strip).Where(p => p is not Empty).ToArray(), kept => new Union(kept)),
            SmoothUnion e => Combined(e.primitives.Select(Strip).Where(p => p is not Empty).ToArray(), kept => new SmoothUnion(e.smoothness, kept)),
            Subtraction e => e with { a = Strip(e.a) },
            SmoothSubtraction e => e with { a = Strip(e.a) },
            Displace e => e with { primitive = Strip(e.primitive) },
            Detail e => Strip(e.primitive),
            Intersection or SmoothIntersection or Xor or SDFFunctionCall => node,
            _ => (SDFEvaluable)EvaluableChildren.MapChildren(node, child => child is SDFEvaluable shape ? Strip(shape) : child),
        };

        /// <summary>Only the branches that lead to the part, and the part itself with any parts it uses taken out; null when none lead there.</summary>
        public SDFEvaluable? Keep(SDFEvaluable node, string partId) => node switch
        {
            VariableReferenceSDF r when r.variableId == partId => Strip(Value(partId)),
            VariableReferenceSDF r when Parts.ContainsKey(r.variableId) => null,
            VariableReferenceSDF r => Keep(Value(r.variableId), partId),
            Union e => Kept(e.primitives, partId, kept => new Union(kept)),
            SmoothUnion e => Kept(e.primitives, partId, kept => new SmoothUnion(e.smoothness, kept)),
            Subtraction e => Keep(e.a, partId) is { } a ? e with { a = a } : null,
            SmoothSubtraction e => Keep(e.a, partId) is { } a ? e with { a = a } : null,
            Displace e => Keep(e.primitive, partId) is { } primitive ? e with { primitive = primitive } : null,
            Detail e => Keep(e.primitive, partId),
            Intersection or SmoothIntersection or Xor or SDFFunctionCall => null,
            _ => KeepOnlyChild(node, partId),
        };

        private SDFEvaluable? Kept(SDFEvaluable[] primitives, string partId, Func<SDFEvaluable[], SDFEvaluable> combine)
        {
            var kept = primitives.Select(p => Keep(p, partId)).OfType<SDFEvaluable>().ToArray();
            return kept.Length == 0 ? null : combine(kept);
        }

        /// <summary>A node around one shape (a transform or modifier) is kept when its shape is; a shape of its own never is.</summary>
        private SDFEvaluable? KeepOnlyChild(SDFEvaluable node, string partId)
        {
            var children = EvaluableChildren.Children(node).OfType<SDFEvaluable>().ToArray();
            if (children is not [var child] || Keep(child, partId) is not { } kept)
                return null;
            return (SDFEvaluable)EvaluableChildren.MapChildren(node, c => ReferenceEquals(c, child) ? kept : c);
        }

        private static SDFEvaluable Combined(SDFEvaluable[] kept, Func<SDFEvaluable[], SDFEvaluable> combine) =>
            kept.Length == 0 ? new Empty() : combine(kept);
    }
}
