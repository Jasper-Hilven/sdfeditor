using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyAdjustScaleTranslateRotate
{
    public static SDFProgram DoApplyAdjustScaleTranslateRotate(SDFProgram program, AdjustScaleTranslateRotate operation)
    {
        var tables = new PointerTables(program.pointers);
        if (tables.PointerTo(operation.nodeId) is not SDFEvaluablePointerable)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.nodeId}'");

        var adjustedId = AdjustedId(tables, operation.nodeId);

        if (adjustedId is not null)
        {
            tables.Set(adjustedId, Adjust((ScaleTranslateRotate)tables.Get(adjustedId), operation));
            return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
        }

        var identity = new ScaleTranslateRotate(new Empty(), new ConstValueFloat(1), new ConstValueV3(new V3(0, 0, 0)), new ConstValueV3(new V3(0, 0, 0)));
        program = program.ReplaceOnlyReference(tables, operation.nodeId, child => Adjust(identity with { primitive = (SDFEvaluable)child }, operation));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    // The scale, translation and rotation an adjustment of the node starts from: those of the ScaleTranslateRotate it would adjust, or the identity
    public static (float Scale, V3 Translation, V3 Rotation) CurrentTransform(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        if (AdjustedId(tables, nodeId) is not { } adjustedId)
            return (1, new V3(0, 0, 0), new V3(0, 0, 0));

        var node = (ScaleTranslateRotate)tables.Get(adjustedId);
        return (Constant(node.scale, nameof(node.scale)), Constant(node.translation, nameof(node.translation)), Constant(node.rotation, nameof(node.rotation)));
    }

    // Only an SDF node can be adjusted, and only when the ScaleTranslateRotate it would adjust holds constants
    public static bool CanAdjust(SDFProgram program, string nodeId)
    {
        var tables = new PointerTables(program.pointers);
        // An empty scene has nothing to transform
        if (!tables.Contains(nodeId) || tables.PointerTo(nodeId) is not SDFEvaluablePointerable || tables.Get(nodeId) is Empty)
            return false;

        return AdjustedId(tables, nodeId) is not { } adjustedId
               || tables.Get(adjustedId) is ScaleTranslateRotate { scale: ConstValueFloat, translation: ConstValueV3, rotation: ConstValueV3 };
    }

    // A ScaleTranslateRotate has a single SDF child, so a node whose parent is one is always that parent's primitive
    private static string? AdjustedId(PointerTables tables, string nodeId) =>
        tables.Get(nodeId) is ScaleTranslateRotate ? nodeId
        : tables.ParentOf(nodeId) is { } parentId && tables.Get(parentId) is ScaleTranslateRotate ? parentId
        : null;

    private static ScaleTranslateRotate Adjust(ScaleTranslateRotate node, AdjustScaleTranslateRotate operation) => node with
    {
        scale = new ConstValueFloat(Constant(node.scale, nameof(node.scale)) + operation.scale),
        translation = new ConstValueV3(Add(Constant(node.translation, nameof(node.translation)), operation.translation)),
        rotation = new ConstValueV3(Add(Constant(node.rotation, nameof(node.rotation)), operation.rotation)),
    };

    // There are no arithmetic nodes, so only constant values can be adjusted
    private static float Constant(FloatEvaluable value, string name) => value as ConstValueFloat is { } c
        ? c.value
        : throw new InvalidOperationException($"The {name} of a ScaleTranslateRotate can only be adjusted when it is a constant, but it is a {value.GetType().Name}");

    private static V3 Constant(V3Evaluable value, string name) => value as ConstValueV3 is { } c
        ? c.v3
        : throw new InvalidOperationException($"The {name} of a ScaleTranslateRotate can only be adjusted when it is a constant, but it is a {value.GetType().Name}");

    private static V3 Add(V3 a, V3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
}
