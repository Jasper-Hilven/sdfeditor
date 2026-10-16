using System.Globalization;
using System.Reflection;
using SDFProject.ProgramDefinition;

namespace IDE.Operations;

public sealed class OperationParameterInput(ParameterInfo parameter)
{
    public string Name => parameter.Name!;

    public Type Type => parameter.ParameterType;

    public string[] Texts { get; } = parameter.ParameterType == typeof(V3) ? ["0", "0", "0"]
        : parameter.ParameterType.IsEnum ? [Enum.GetNames(parameter.ParameterType)[0]]
        : parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(float[]) ? [""]
        : ["0"];

    public object Parse()
    {
        try
        {
            return Type == typeof(string) ? Texts[0]
                : Type == typeof(float) ? Float(Texts[0])
                : Type == typeof(int) ? int.Parse(Texts[0], CultureInfo.InvariantCulture)
                : Type == typeof(V3) ? new V3(Float(Texts[0]), Float(Texts[1]), Float(Texts[2]))
                : Type == typeof(float[]) ? Texts[0].Split(',').Select(Float).ToArray()
                : Type.IsEnum ? Enum.Parse(Type, Texts[0])
                : throw new NotSupportedException($"Parameters of type {Type.Name} cannot be entered");
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"'{string.Join(", ", Texts)}' is not a valid value for {Name}");
        }
    }

    private static float Float(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
