using SDFProject.ProgramDefinition;
using SDFProject.SDFProgramOperator.Helpers;

namespace SDFProject.SDFProgramOperator.Operations;

public class ApplyAdjustTransform
{
    public static SDFProgram DoApplyAdjustTransform(SDFProgram program, AdjustTransform operation)
    {
        var tables = new PointerTables(program.pointers);
        if (!tables.Contains(operation.nodeId) || tables.PointerTo(operation.nodeId) is not SDFEvaluablePointerable)
            throw new KeyNotFoundException($"There is no SDF node with pointer '{operation.nodeId}'");

        var (baseId, chain) = Chain(tables, operation.nodeId);

        if (chain.FirstOrDefault(id => KindOf(tables.Get(id)) == operation.kind) is { } adjustedId)
        {
            var node = (SDFEvaluable)tables.Get(adjustedId);
            var adjusted = Add(Constant(ValueOf(node), operation.kind), operation.delta);
            if (IsIdentity(operation.kind, adjusted))
            {
                program = program.ReplaceOnlyReference(tables, adjustedId, _ => PrimitiveOf(node));
                tables.Remove(adjustedId);
            }
            else
                tables.Set(adjustedId, Create(operation.kind, PrimitiveOf(node), adjusted));
            return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
        }

        var value = Add(Identity(operation.kind), operation.delta);
        if (IsIdentity(operation.kind, value))
            return program;

        var wrappedId = baseId;
        foreach (var id in chain.TakeWhile(id => KindOf(tables.Get(id)) < operation.kind))
            wrappedId = id;

        program = program.ReplaceOnlyReference(tables, wrappedId, child => Create(operation.kind, (SDFEvaluable)child, value));
        return program with { pointers = tables.ToPointers(program.pointers.nextFreePointer) };
    }

    public static V3 Current(SDFProgram program, string nodeId, TransformKind kind)
    {
        var tables = new PointerTables(program.pointers);
        var (_, chain) = Chain(tables, nodeId);
        return chain.FirstOrDefault(id => KindOf(tables.Get(id)) == kind) is { } id
            ? Constant(ValueOf((SDFEvaluable)tables.Get(id)), kind)
            : Identity(kind);
    }

    public static bool CanAdjust(SDFProgram program, string nodeId, TransformKind kind)
    {
        var tables = new PointerTables(program.pointers);
        if (!tables.Contains(nodeId) || tables.PointerTo(nodeId) is not SDFEvaluablePointerable || tables.Get(nodeId) is Empty)
            return false;

        var (_, chain) = Chain(tables, nodeId);
        return chain.FirstOrDefault(id => KindOf(tables.Get(id)) == kind) is not { } id
               || ValueOf((SDFEvaluable)tables.Get(id)) is ConstValueV3;
    }

    private static (string BaseId, List<string> Chain) Chain(PointerTables tables, string nodeId)
    {
        var baseId = nodeId;
        while (tables.Get(baseId) is SDFEvaluable node && KindOf(node) is not null && PointerTables.PointerId(PrimitiveOf(node)) is { } primitiveId)
            baseId = primitiveId;

        var chain = KindOf(tables.Get(baseId)) is null ? new List<string>() : [baseId];
        var current = baseId;
        while (tables.ParentOf(current) is { } parentId && KindOf(tables.Get(parentId)) is not null)
        {
            chain.Add(parentId);
            current = parentId;
        }

        return (baseId, chain);
    }

    private static TransformKind? KindOf(Evaluable node) => node switch
    {
        Scale => TransformKind.Scale,
        Rotate => TransformKind.Rotate,
        Translate => TransformKind.Translate,
        _ => null,
    };

    private static SDFEvaluable PrimitiveOf(SDFEvaluable node) => node switch
    {
        Scale e => e.primitive,
        Rotate e => e.primitive,
        Translate e => e.primitive,
        _ => throw new ArgumentException($"A {node.GetType().Name} is not a transform", nameof(node)),
    };

    private static V3Evaluable ValueOf(SDFEvaluable node) => node switch
    {
        Scale e => e.scale,
        Rotate e => e.rotation,
        Translate e => e.translation,
        _ => throw new ArgumentException($"A {node.GetType().Name} is not a transform", nameof(node)),
    };

    private static SDFEvaluable Create(TransformKind kind, SDFEvaluable primitive, V3 value) => kind switch
    {
        TransformKind.Scale => new Scale(primitive, new ConstValueV3(value)),
        TransformKind.Rotate => new Rotate(primitive, new ConstValueV3(value)),
        TransformKind.Translate => new Translate(primitive, new ConstValueV3(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static V3 Identity(TransformKind kind) => kind == TransformKind.Scale ? new V3(1, 1, 1) : new V3(0, 0, 0);

    private static bool IsIdentity(TransformKind kind, V3 value)
    {
        const float tolerance = 1e-5f;
        var identity = Identity(kind);
        return MathF.Abs(value.x - identity.x) < tolerance && MathF.Abs(value.y - identity.y) < tolerance && MathF.Abs(value.z - identity.z) < tolerance;
    }

    private static V3 Constant(V3Evaluable value, TransformKind kind) => value as ConstValueV3 is { } c
        ? c.v3
        : throw new InvalidOperationException($"A {kind} can only be adjusted when its value is a constant, but it is a {value.GetType().Name}");

    private static V3 Add(V3 a, V3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
}
