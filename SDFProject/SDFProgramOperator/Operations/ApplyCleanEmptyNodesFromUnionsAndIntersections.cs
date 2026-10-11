using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyCleanEmptyNodesFromUnionsAndIntersections
{
    public static SDFProgram DoApplyCleanEmptyNodesFromUnionsAndIntersections(SDFProgram program, CleanEmptyNodesFromUnionsAndIntersections operation)
    {
        program = ReplaceInlineNodesWithoutPrimitives(program);

        while (FindNodeWithOnlyEmptyPrimitives(program) is { } id)
            program = ApplyEmptyNode.DoApplyEmptyNode(program, new EmptyNode(id));

        return RemoveEmptyPrimitives(program);
    }

    // A union without primitives has no children, so it lives inline in its parent without a pointer that EmptyNode could target
    private static SDFProgram ReplaceInlineNodesWithoutPrimitives(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);
        Evaluable Replace(Evaluable node) => node is SDFEvaluable sdf && Primitives(sdf) is [] ? new Empty() : node;

        program = program.MapRoots(root => tables.MapTree(root, Replace));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    private static string? FindNodeWithOnlyEmptyPrimitives(SDFProgram program) =>
        program.pointers.SDFEvaluablePointers
            .FirstOrDefault(e => Primitives(e.Value) is { } primitives && primitives.All(p => p is Empty))
            .Key;

    private static SDFEvaluable[]? Primitives(SDFEvaluable node) => node switch
    {
        Union e => e.primitives,
        Intersection e => e.primitives,
        SmoothUnion e => e.primitives,
        SmoothIntersection e => e.primitives,
        _ => null,
    };

    // Every union and intersection has a non-empty primitive left at this point, so none of them disappears
    private static SDFProgram RemoveEmptyPrimitives(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);

        Evaluable Clean(Evaluable node)
        {
            if (PointerTables.PointerId(node) is not { } id)
                return Simplify(EvaluableChildren.MapChildren(node, Clean));

            var simplified = Simplify(EvaluableChildren.MapChildren(tables.Get(id), Clean));

            // A subtraction replaced by its first child hands over that child's pointer,
            // or puts it inline in the parent when the child has no children of its own
            if (!EvaluableChildren.HasChildren(simplified) || PointerTables.PointerId(simplified) is not null)
            {
                tables.Remove(id);
                return simplified;
            }

            tables.Set(id, simplified);
            return node;
        }

        Evaluable Simplify(Evaluable node) => node switch
        {
            Union e => new Union(WithoutEmpty(e.primitives)),
            Intersection e => new Intersection(WithoutEmpty(e.primitives)),
            SmoothUnion e => new SmoothUnion(e.smoothness, WithoutEmpty(e.primitives)),
            SmoothIntersection e => new SmoothIntersection(e.smoothness, WithoutEmpty(e.primitives)),
            Subtraction { b: Empty } e => e.a,
            SmoothSubtraction { b: Empty } e => RemoveWith(e.smoothness, e.a),
            _ => node,
        };

        static SDFEvaluable[] WithoutEmpty(SDFEvaluable[] primitives) => primitives.Where(p => p is not Empty).ToArray();

        // Drops the pointers of a subtree that disappears together with its parent
        Evaluable RemoveWith(Evaluable discarded, Evaluable result)
        {
            tables.RemoveSubtree(discarded);
            return result;
        }

        program = program.MapRoots(Clean);
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
