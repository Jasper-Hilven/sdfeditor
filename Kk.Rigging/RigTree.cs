using System.Globalization;
using SDFPipeline.ProgramFiles;

namespace Kk.Rigging;

/// <summary>A format 2 file that cannot be read at all (syntax). Problems that leave the file readable are <see cref="RigIssue"/>s.</summary>
public sealed class RigFormatException(int line, string problem) : Exception($"line {line}: {problem}")
{
    public int Line { get; } = line;
    public string Problem { get; } = problem;
}

/// <summary>A problem found in a model by <see cref="RigModel.Read"/>: an error stops meshing and posing, a warning does not.</summary>
public sealed record RigIssue(int Line, bool IsError, string Message)
{
    public override string ToString() => $"{(IsError ? "error" : "warning")}: line {Line}: {Message}";
}

internal enum NodeKind { Plain, Bone, Socket, PartUse }

/// <summary>
/// A line of the scene tree. Plain nodes are format 1 nodes kept as text (their parameters are only read when the rigging needs them);
/// Bone and Socket are format 2; PartUse is a <c>Part</c> line whose part has bones, so its tree is inlined as the only child.
/// </summary>
internal sealed class RigNode
{
    public NodeKind Kind;
    public int Line;
    /// <summary>The node type: Translate, Bone, Part ... or "$" for a variable reference.</summary>
    public string Type = "";
    /// <summary>The line as written (trimmed, comment removed); for a PartUse the Part line itself, not the inlined tree.</summary>
    public string Text = "";
    public readonly List<RigNode> Children = [];
    public RigNode? Parent;
    public Dictionary<string, RawValue> Args = [];
    public bool HasBone, HasSocket;

    public bool IsSymmetry => Type is "SymmetryX" or "SymmetryXZ";

    public bool Contains(RigNode other)
    {
        for (var n = other; n is not null; n = n.Parent)
            if (n == this)
                return true;
        return false;
    }

    public IEnumerable<RigNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants())
                yield return d;
        }
    }
}

/// <summary>What was read from a format 2 text: the pieces the rigging works with.</summary>
internal sealed class RigSource
{
    public int Format;
    public bool HitColliders;
    /// <summary>The top-level setting lines other than <c>collider bones</c>, as written.</summary>
    public List<string> Settings = [];
    /// <summary>The external skeleton the model is rigged onto (<c>skeleton=synty</c>), or null; see docs/skeleton-synty.md.</summary>
    public string? SkeletonName;
    public int SkeletonLine;
    /// <summary>Top-level variable declarations and functions, as written (with their indented lines).</summary>
    public List<string> Declarations = [];
    public RigNode Scene = null!;
}

internal static class RigReader
{
    private static readonly HashSet<string> BoneKeys = ["name", "head", "tail", "roll", "falloff", "spin", "hinge", "limit", "hit"];
    private static readonly HashSet<string> SocketKeys = ["name", "at", "dir", "up"];

    public static RigSource Read(string text, PartSource? parts, List<string>? partStack = null)
    {
        try
        {
            return ReadChecked(text, parts, partStack);
        }
        catch (SdfProgramFileException e)
        {
            throw new RigFormatException(e.Line, e.Problem);
        }
    }

    private static RigSource ReadChecked(string text, PartSource? parts, List<string>? partStack)
    {
        var lines = SourceLine.Parse(text);
        if (lines.Count == 0)
            throw new RigFormatException(1, "The file is empty, it must start with 'format 2'");
        var source = new RigSource { Format = ReadFormat(lines[0]) };

        var sceneLines = new List<SourceLine>();
        foreach (var line in lines.Skip(1))
        {
            if (IsSkeletonLine(line))
            {
                if (source.SkeletonName is not null)
                    throw new RigFormatException(line.Number, $"The skeleton is already given on line {source.SkeletonLine}");
                if (source.Format != 2)
                    throw new RigFormatException(line.Number, "Only a format 2 model can name a skeleton");
                if (partStack is { Count: > 0 })
                    throw new RigFormatException(line.Number, "A part cannot name a skeleton; the model that uses it does");
                if (line.Children.Count > 0)
                    throw new RigFormatException(line.Children[0].Number, "Nothing can be indented below the skeleton line");
                var cursor = new Cursor(line);
                cursor.TryReadName();
                cursor.Expect('=');
                source.SkeletonName = cursor.ReadName("a skeleton name, like skeleton=synty");
                cursor.ExpectEnd();
                source.SkeletonLine = line.Number;
            }
            else if (ModelSettings.IsSetting(line))
            {
                if (line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries) is ["collider", "bones"])
                    source.HitColliders = true;
                else
                    source.Settings.Add(line.Text);
            }
            else if (IsFunction(line) || IsDeclaration(line))
            {
                RejectRigNodes(line);
                source.Declarations.Add(Render(line, 0));
            }
            else
                sceneLines.Add(line);
        }
        if (sceneLines.Count == 0)
            throw new RigFormatException(lines[0].Number, "The file has no shape");
        if (sceneLines.Count > 1)
            throw new RigFormatException(sceneLines[1].Number, $"The file already has its shape on line {sceneLines[0].Number}; combine shapes with a Union");

        source.Scene = ReadNode(sceneLines[0], null, parts, partStack ?? []);
        Mark(source.Scene);
        return source;
    }

    private static int ReadFormat(SourceLine line)
    {
        var cursor = new Cursor(line);
        if (cursor.TryReadName() != "format" || cursor.ReadValue() is not RawNumber { IsWhole: true } number)
            throw new RigFormatException(line.Number, "The file must start with 'format 2'");
        var version = int.Parse(number.Text, CultureInfo.InvariantCulture);
        if (version is not (1 or 2))
            throw new RigFormatException(line.Number, $"This file has format {version}, but only formats 1 and 2 can be read");
        return version;
    }

    /// <summary>A top-level <c>skeleton=&lt;name&gt;</c> line: the external skeleton the bones are placed on.</summary>
    private static bool IsSkeletonLine(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() == "skeleton" && cursor.Peek() == '=';
    }

    private static bool IsFunction(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() == "function" && cursor.Peek() is not ('=' or ':' or null);
    }

    private static bool IsDeclaration(SourceLine line)
    {
        var cursor = new Cursor(line);
        return cursor.TryReadName() is not null && cursor.Peek() is '=' or ':';
    }

    private static void RejectRigNodes(SourceLine line)
    {
        foreach (var child in line.Children)
        {
            var name = new Cursor(child).TryReadName();
            if (name is "Bone" or "Socket")
                throw new RigFormatException(child.Number, $"A {name} must be in the model's shape tree, not in a variable or function: bones are placed where they are used");
            RejectRigNodes(child);
        }
    }

    private static string Render(SourceLine line, int depth) =>
        string.Concat(new string(' ', depth * 4), line.Text, "\n", string.Concat(line.Children.Select(c => Render(c, depth + 1))));

    private static RigNode ReadNode(SourceLine line, RigNode? parent, PartSource? parts, List<string> partStack)
    {
        var cursor = new Cursor(line);
        var node = new RigNode { Line = line.Number, Text = line.Text, Parent = parent };

        if (cursor.Peek() == '$')
        {
            node.Type = "$";
            AddChildren(node, line, parts, partStack);
            return node;
        }

        var type = cursor.ReadName("a node type");
        node.Type = type;
        switch (type)
        {
            case "Bone":
                node.Kind = NodeKind.Bone;
                node.Args = ReadArgs(cursor, BoneKeys, "Bone");
                if (!node.Args.ContainsKey("name"))
                    throw cursor.Error("A Bone needs a name, like Bone name=head head=(0, 1, 0)");
                if (!node.Args.ContainsKey("head"))
                    throw cursor.Error("A Bone needs a head, the point it turns about, like head=(0, 1, 0)");
                AddChildren(node, line, parts, partStack);
                break;
            case "Socket":
                node.Kind = NodeKind.Socket;
                node.Args = ReadArgs(cursor, SocketKeys, "Socket");
                if (!node.Args.ContainsKey("name") || !node.Args.ContainsKey("at"))
                    throw cursor.Error("A Socket needs a name and a position, like Socket name=mouth at=(0, 0.5, 1)");
                if (line.Children.Count > 0)
                    throw new RigFormatException(line.Children[0].Number, "Nothing can be indented below a Socket");
                break;
            case "Part":
                return ReadPart(node, line, cursor, parts, partStack);
            default:
                AddChildren(node, line, parts, partStack);
                break;
        }
        return node;
    }

    private static void AddChildren(RigNode node, SourceLine line, PartSource? parts, List<string> partStack)
    {
        foreach (var child in line.Children)
            node.Children.Add(ReadNode(child, node, parts, partStack));
    }

    private static Dictionary<string, RawValue> ReadArgs(Cursor cursor, HashSet<string> allowed, string what)
    {
        var args = new Dictionary<string, RawValue>();
        while (!cursor.AtEnd)
        {
            var key = cursor.ReadName("an attribute like name=value");
            if (!allowed.Contains(key))
                throw cursor.Error($"{what} has no attribute '{key}'; its attributes are {string.Join(", ", allowed)}");
            cursor.Expect('=');
            if (!args.TryAdd(key, cursor.ReadValue()))
                throw cursor.Error($"Attribute '{key}' is given twice");
        }
        return args;
    }

    /// <summary>
    /// A part without bones stays a <c>Part</c> line for the pipeline. A part with bones is inlined: its tree becomes the only child,
    /// its bones and sockets prefixed with the use's name (<c>as=</c> or the part's name).
    /// </summary>
    private static RigNode ReadPart(RigNode node, SourceLine line, Cursor cursor, PartSource? parts, List<string> partStack)
    {
        var args = ReadArgs(cursor, ["part", "as"], "Part");
        if (args.GetValueOrDefault("part") is not RawName { Name: var part })
            throw cursor.Error("Write a part as Part part=eye");
        var alias = args.TryGetValue("as", out var a) ? (a as RawName)?.Name ?? throw cursor.Error("Name the use, like as=claw") : part;
        if (line.Children.Count > 0)
            throw new RigFormatException(line.Children[0].Number, "Nothing can be indented below a part");

        node.Text = $"Part part={part}";
        var text = parts?.Invoke(part);
        if (text is null || !MentionsRigNodes(text))
        {
            if (text is null && parts is not null)
                throw cursor.Error($"There is no part '{part}'; parts are the files in content/parts/");
            return node; // plain: the pipeline resolves it (and reports a missing part)
        }

        if (partStack.Contains(part))
            throw cursor.Error($"Part '{part}' uses itself: {string.Join(" -> ", partStack.Append(part))}");
        partStack.Add(part);
        RigSource inner;
        try
        {
            inner = Read(text, parts, partStack);
        }
        catch (RigFormatException e)
        {
            throw cursor.Error($"In part '{part}', line {e.Line}: {e.Problem}");
        }
        finally
        {
            partStack.Remove(part);
        }
        if (inner.Scene.Kind != NodeKind.Bone && !inner.Scene.HasBone && !inner.Scene.HasSocket)
            return node; // the part (and the parts it uses) have no bones: the pipeline resolves it as it does any part
        if (inner.Declarations.Count > 0)
            throw cursor.Error($"Part '{part}' has bones and declares variables or functions, which cannot be inlined yet; put the bones in a part without them");

        node.Kind = NodeKind.PartUse;
        node.Text = alias == part ? $"Part part={part}" : $"Part part={part} as={alias}";
        Prefix(inner.Scene, alias + "_");
        inner.Scene.Parent = node;
        node.Children.Add(inner.Scene);
        return node;
    }

    private static bool MentionsRigNodes(string text) =>
        text.Split('\n').Any(l => l.TrimStart().StartsWith("Bone ", StringComparison.Ordinal) || l.TrimStart().StartsWith("Socket ", StringComparison.Ordinal)
                                  || l.TrimStart().StartsWith("Part ", StringComparison.Ordinal));

    private static void Prefix(RigNode root, string prefix)
    {
        foreach (var n in new[] { root }.Concat(root.Descendants()))
            if (n.Kind is NodeKind.Bone or NodeKind.Socket && n.Args.GetValueOrDefault("name") is RawName name)
                n.Args["name"] = new RawName(prefix + name.Name);
    }

    private static void Mark(RigNode node)
    {
        foreach (var child in node.Children)
        {
            Mark(child);
            node.HasBone |= child.HasBone || child.Kind == NodeKind.Bone;
            node.HasSocket |= child.HasSocket || child.Kind == NodeKind.Socket;
        }
    }
}
