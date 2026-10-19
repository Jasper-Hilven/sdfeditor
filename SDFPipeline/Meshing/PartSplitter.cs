using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFPipeline.Meshing;

/// <summary>A program to mesh on its own, named after what it is: the model's own geometry, or one of its parts.</summary>
/// <param name="Detail">
/// How many times the vertices its size gives the mesh: the <see cref="SDFProject.ProgramDefinition.Detail"/> at the root of the model,
/// and for a part, times the one at the root of the part's file.
/// </param>
public sealed record MeshSource(string Name, SDFProgram Program, float Detail = 1);

/// <summary>
/// Splits a model into one program per mesh by rewriting its graph: the body is the model with its parts taken out, and each part is
/// the model with only the branches that lead to that part (all of its uses together, placed and mirrored as in the model). Parts used
/// by a part are meshes of their own too, as if the model used them itself.
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
    public static IReadOnlyList<MeshSource> Split(SDFProgram program)
    {
        var expanded = Expanded(program);
        var rewriter = new Rewriter(expanded);
        var modelDetail = DetailOf(expanded.scene);
        var sources = new List<MeshSource>();
        if (rewriter.Strip(expanded.scene) is not Empty)
            sources.Add(new MeshSource(BodyName, expanded with { scene = rewriter.Strip(expanded.scene) }, modelDetail));
        foreach (var (id, name) in rewriter.Parts.OrderBy(p => p.Value, StringComparer.Ordinal))
            if (rewriter.Keep(expanded.scene, id) is { } kept)
                sources.Add(new MeshSource(name, expanded with { scene = kept }, modelDetail * DetailOf(rewriter.Value(id))));
        return sources;
    }

    /// <summary>The detail the model sets for all of its meshes: that of the Detail node at its root, or 1 without one.</summary>
    public static float ModelDetail(SDFProgram program) => DetailOf(new PointerTables(program.pointers).Expand(program.scene));

    /// <summary>
    /// The detail a file's shape sets, read from its root only: a Detail node anywhere else has no effect, which the file reader
    /// rejects, so a program made in code that has one there is meshed as if it had none.
    /// </summary>
    private static float DetailOf(Evaluable root) => root is Detail { detail: ConstValueFloat { value: var detail } } ? detail : 1;

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
            _ => StripAround(node),
        };

        /// <summary>A node around one shape (a transform or modifier) is nothing when its shape is; others keep their stripped children.</summary>
        private SDFEvaluable StripAround(SDFEvaluable node)
        {
            var children = EvaluableChildren.Children(node).OfType<SDFEvaluable>().ToArray();
            if (children is [var child] && Strip(child) is Empty)
                return new Empty();
            return (SDFEvaluable)EvaluableChildren.MapChildren(node, c => c is SDFEvaluable shape ? Strip(shape) : c);
        }

        /// <summary>Only the branches that lead to the part, and the part itself with any parts it uses taken out; null when none lead there.</summary>
        public SDFEvaluable? Keep(SDFEvaluable node, string partId) => node switch
        {
            VariableReferenceSDF r when r.variableId == partId => Strip(Value(partId)),
            // Through another part to the parts it uses, so a model that is one part (amanita-scout, whose shape is the amanita_scout part
            // its exploding pieces cut up) still gets that part's eyes and hands as meshes of their own
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
