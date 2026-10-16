using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyToggleNodeSmoothness
{
    public const float DefaultSmoothness = 0.25f;

    public static SDFProgram DoApplyToggleNodeSmoothness(SDFProgram program, ToggleNodeSmoothness operation)
    {
        var tables = new PointerTables(program.pointers);
        var node = tables.Get(operation.nodeId);
        if (!CanToggle(node))
            throw new InvalidOperationException($"Only unions, intersections and subtractions can be made smooth, but node '{operation.nodeId}' is a {node.GetType().Name}");

        var smoothness = new ConstValueFloat(DefaultSmoothness);
        Evaluable toggled = node switch
        {
            Union e => new SmoothUnion(smoothness, e.primitives),
            Intersection e => new SmoothIntersection(smoothness, e.primitives),
            Subtraction e => new SmoothSubtraction(e.a, e.b, smoothness),
            Union2D e => new SmoothUnion2D(smoothness, e.primitives),
            SmoothUnion e => RemoveWith(e.smoothness, new Union(e.primitives)),
            SmoothIntersection e => RemoveWith(e.smoothness, new Intersection(e.primitives)),
            SmoothSubtraction e => RemoveWith(e.smoothness, new Subtraction(e.a, e.b)),
            SmoothUnion2D e => RemoveWith(e.smoothness, new Union2D(e.primitives)),
            _ => node,
        };
        tables.Set(operation.nodeId, toggled);

        // Drops the pointers of the smoothness that disappears with the smooth variant
        Evaluable RemoveWith(Evaluable discarded, Evaluable result)
        {
            tables.RemoveSubtree(discarded);
            return result;
        }

        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static bool CanToggle(Evaluable node) => node is
        Union or Intersection or Subtraction or Union2D or
        SmoothUnion or SmoothIntersection or SmoothSubtraction or SmoothUnion2D;
}
