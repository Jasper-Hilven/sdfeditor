using System.Text;
using System.Text.RegularExpressions;

namespace SDFPipeline.ProgramFiles;

/// <summary>A non-empty line of a file, with the lines indented below it as its children.</summary>
internal sealed record SourceLine(int Number, int Indent, string Text, List<SourceLine> Children)
{
    public static List<SourceLine> Parse(string text)
    {
        var roots = new List<SourceLine>();
        var open = new Stack<SourceLine>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var content = StripComment(lines[i].TrimEnd('\r'), i + 1);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var indent = content.Length - content.TrimStart(' ').Length;
            if (content[indent] == '\t')
                throw new SdfProgramFileException(i + 1, "Indent with spaces, not tabs");

            var line = new SourceLine(i + 1, indent, content.Trim(), []);
            while (open.Count > 0 && open.Peek().Indent >= indent)
                open.Pop();
            (open.Count > 0 ? open.Peek().Children : roots).Add(line);
            open.Push(line);
        }
        return roots;
    }

    private static string StripComment(string line, int number)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case '\\' when quoted:
                    i++;
                    break;
                case '"':
                    quoted = !quoted;
                    break;
                case '#' when !quoted:
                    return line[..i];
            }
        }
        return quoted ? throw new SdfProgramFileException(number, "A quoted name is not closed") : line;
    }
}

internal abstract record RawValue;
internal sealed record RawNumber(string Text) : RawValue
{
    public bool IsWhole => !Text.Any(c => c is '.' or 'e' or 'E');
}
internal sealed record RawVector(RawNumber[] Components) : RawValue;
internal sealed record RawList(RawValue[] Items) : RawValue;
internal sealed record RawVariable(string Name) : RawValue;
internal sealed record RawArgument(string Name) : RawValue;
internal sealed record RawName(string Name) : RawValue;

/// <summary>Reads the tokens of one line, from left to right.</summary>
internal sealed partial class Cursor(SourceLine line)
{
    private readonly string text = line.Text;
    private int position;

    public SourceLine Line => line;

    public bool AtEnd => Peek() is null;

    public char? Peek()
    {
        while (position < text.Length && text[position] == ' ')
            position++;
        return position < text.Length ? text[position] : null;
    }

    public bool TryConsume(char c)
    {
        if (Peek() != c)
            return false;
        position++;
        return true;
    }

    public void Expect(char c)
    {
        if (!TryConsume(c))
            throw Error($"Expected '{c}'{Found()}");
    }

    public void ExpectEnd()
    {
        if (!AtEnd)
            throw Error($"Unexpected '{text[position..]}'");
    }

    public string ReadName(string what) => TryReadName() ?? throw Error($"Expected {what}{Found()}");

    public string? TryReadName()
    {
        if (Peek() == '"')
            return ReadQuoted();

        var start = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
            position++;
        var name = text[start..position];
        if (FileSyntax.IsIdentifier(name))
            return name;

        position = start;
        return null;
    }

    /// <summary>
    /// A name that may also hold '-' after its first letter or digit, like the model grass-turf0: the name of a file in content/, which
    /// <see cref="ReadName"/> (the identifiers of programs) does not take. Quoted names are read as there.
    /// </summary>
    public string ReadFileName(string what)
    {
        if (Peek() == '"')
            return ReadQuoted();

        var start = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '-'))
            position++;
        if (position > start && char.IsAsciiLetterOrDigit(text[start]))
            return text[start..position];

        position = start;
        throw Error($"Expected {what}{Found()}");
    }

    public RawValue ReadValue()
    {
        switch (Peek())
        {
            case '$':
                position++;
                return new RawVariable(ReadName("a variable name after '$'"));
            case '@':
                position++;
                return new RawArgument(ReadName("an argument name after '@'"));
            case '(':
                position++;
                var components = new List<RawNumber> { ReadNumber() };
                while (TryConsume(','))
                    components.Add(ReadNumber());
                Expect(')');
                return new RawVector(components.ToArray());
            case '[':
                position++;
                var items = new List<RawValue>();
                if (!TryConsume(']'))
                {
                    do items.Add(ReadValue());
                    while (TryConsume(','));
                    Expect(']');
                }
                return new RawList(items.ToArray());
            case { } c when char.IsAsciiDigit(c) || c is '-' or '+' or '.':
                return ReadNumber();
            default:
                // A bare name may hold '-' between letters and digits, like the material toybox-wall (the names of files in content/)
                var name = ReadName("a value");
                while (position + 1 < text.Length && text[position] == '-' && char.IsAsciiLetterOrDigit(text[position + 1]))
                {
                    var start = ++position;
                    while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_'))
                        position++;
                    name += "-" + text[start..position];
                }
                return new RawName(name);
        }
    }

    private RawNumber ReadNumber()
    {
        Peek();
        var match = NumberPattern().Match(text, position);
        if (!match.Success)
            throw Error($"Expected a number{Found()}");
        position += match.Length;
        return new RawNumber(match.Value);
    }

    private string ReadQuoted()
    {
        position++;
        var name = new StringBuilder();
        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
                name.Append(text[position] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', var c => c });
            }
            else
            {
                name.Append(text[position]);
            }
            position++;
        }
        Expect('"');
        return name.ToString();
    }

    public SdfProgramFileException Error(string problem) => new(line.Number, problem);

    private string Found() => position < text.Length ? $", found '{text[position..]}'" : " at the end of the line";

    [GeneratedRegex(@"\G[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?")]
    private static partial Regex NumberPattern();
}
