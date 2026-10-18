using SDFProject.ProgramDefinition;

namespace SDFPipeline.ProgramFiles;

public enum ValueKind
{
    Float,
    Int,
    V2,
    V3,
    Sdf,
    Sdf2D,
}

internal static class ValueKinds
{
    public static string Keyword(ValueKind kind) => kind switch
    {
        ValueKind.Float => "float",
        ValueKind.Int => "int",
        ValueKind.V2 => "v2",
        ValueKind.V3 => "v3",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only values have a type keyword"),
    };

    public static ValueKind? FromKeyword(string keyword) => keyword switch
    {
        "float" => ValueKind.Float,
        "int" => ValueKind.Int,
        "v2" => ValueKind.V2,
        "v3" => ValueKind.V3,
        _ => null,
    };

    public static string Describe(ValueKind kind) => kind switch
    {
        ValueKind.Float => "a number",
        ValueKind.Int => "a whole number",
        ValueKind.V2 => "a 2D vector like (x, y)",
        ValueKind.V3 => "a 3D vector like (x, y, z)",
        ValueKind.Sdf => "a 3D shape",
        ValueKind.Sdf2D => "a 2D shape",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ValueKind? OfSite(Type type) =>
        type == typeof(FloatEvaluable) ? ValueKind.Float
        : type == typeof(IntegerEvaluable) ? ValueKind.Int
        : type == typeof(V2Evaluable) ? ValueKind.V2
        : type == typeof(V3Evaluable) ? ValueKind.V3
        : type == typeof(SDFEvaluable) ? ValueKind.Sdf
        : type == typeof(SDF2DEvaluable) ? ValueKind.Sdf2D
        : null;

    public static ValueKind Of(Evaluable evaluable) => evaluable switch
    {
        FloatEvaluable => ValueKind.Float,
        IntegerEvaluable => ValueKind.Int,
        V2Evaluable => ValueKind.V2,
        V3Evaluable => ValueKind.V3,
        SDFEvaluable => ValueKind.Sdf,
        SDF2DEvaluable => ValueKind.Sdf2D,
        _ => throw new NotSupportedException($"{evaluable.GetType().Name} has no value kind"),
    };

    public static Evaluable VariableReference(ValueKind kind, string variableId) => kind switch
    {
        ValueKind.Float => new VariableReferenceFloat(variableId),
        ValueKind.Int => new VariableReferenceInt(variableId),
        ValueKind.V2 => new VariableReferenceV2(variableId),
        ValueKind.V3 => new VariableReferenceV3(variableId),
        ValueKind.Sdf => new VariableReferenceSDF(variableId),
        ValueKind.Sdf2D => new VariableReferenceSDF2D(variableId),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static Evaluable ArgumentReference(ValueKind kind, int index) => kind switch
    {
        ValueKind.Float => new FunctionParameterValueFloat(index),
        ValueKind.Int => new FunctionParameterValueInt(index),
        ValueKind.V2 => new FunctionParameterValueV2(index),
        ValueKind.V3 => new FunctionParameterValueV3(index),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Function arguments can only be values"),
    };
}
