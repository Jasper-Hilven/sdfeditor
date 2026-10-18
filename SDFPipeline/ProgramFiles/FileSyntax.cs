using System.Globalization;
using System.Text;

namespace SDFPipeline.ProgramFiles;

internal static class FileSyntax
{
    public const string FormatKeyword = "format";
    public const string FunctionKeyword = "function";
    public const string PartKeyword = "Part";
    public const string PartParameter = "part";

    public static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>A name as it appears in a file: bare when it is an identifier, quoted otherwise.</summary>
    public static string Name(string name) => IsIdentifier(name) ? name : Quote(name);

    /// <summary>The shortest text that reads back to exactly the same float.</summary>
    public static string Number(float value) =>
        float.IsFinite(value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"{value} cannot be written to a file, only finite numbers can");

    private static string Quote(string name)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in name)
        {
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }
        return quoted.Append('"').ToString();
    }
}
