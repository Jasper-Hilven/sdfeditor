using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyCleanEmptyNodesFromUnionsAndIntersections
{
    public static SDFProgram DoApplyCleanEmptyNodesFromUnionsAndIntersections(SDFProgram program, CleanEmptyNodesFromUnionsAndIntersections operation)
    {
        while (FindNodeWithOnlyEmptyPrimitives(program) is { } id)
            program = ApplyEmptyNode.DoApplyEmptyNode(program, new EmptyNode(id));

        return RemoveEmptyPrimitives(program);
    }

    private static string? FindNodeWithOnlyEmptyPrimitives(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);
        return program.pointers.SDFEvaluablePointers
            .FirstOrDefault(e => Primitives(e.Value) is { } primitives && primitives.All(p => Resolve(tables, p) is Empty))
            .Key;
    }

    private static Evaluable Resolve(PointerTables tables, Evaluable node) =>
        PointerTables.PointerId(node) is { } id ? tables.Get(id) : node;

    private static SDFEvaluable[]? Primitives(SDFEvaluable node) => node switch
    {
        Union e => e.primitives,
        Intersection e => e.primitives,
        SmoothUnion e => e.primitives,
        SmoothIntersection e => e.primitives,
        _ => null,
    };

    private static SDFProgram RemoveEmptyPrimitives(SDFProgram program)
    {
        var tables = new PointerTables(program.pointers);

        Evaluable Clean(Evaluable node)
        {
            if (PointerTables.PointerId(node) is not { } id)
                return Simplify(EvaluableChildren.MapChildren(node, Clean));

            var simplified = Simplify(EvaluableChildren.MapChildren(tables.Get(id), Clean));

            if (simplified is Empty || PointerTables.PointerId(simplified) is not null)
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

        Evaluable RemoveWith(Evaluable discarded, Evaluable result)
        {
            tables.RemoveSubtree(discarded);
            return result;
        }

        program = program.MapRoots(Clean);
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }
}
