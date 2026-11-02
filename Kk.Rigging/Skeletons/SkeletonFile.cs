using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Kk.Rigging.Skeletons;

/// <summary>A skeleton file that cannot be read. The message names the line, counted from 1.</summary>
public sealed class SkeletonFormatException(int line, string problem) : Exception($"line {line}: {problem}")
{
    public int Line { get; } = line;
    public string Problem { get; } = problem;
}

/// <summary>One bone of a <see cref="SkeletonFile"/>: local rest transform in Unity space, exactly as written in the file. Parent is -1 for the first bone.</summary>
public sealed record SkeletonJoint(int Index, string Name, string Alias, int Parent, Vector3 Position, Quaternion Rotation);

/// <summary>
/// An external skeleton (bone names, format 2 aliases, hierarchy, local rest transforms) read from a <c>.skeleton</c> text file.
/// Joints are in file order, so a parent always comes before its children.
/// </summary>
public sealed class SkeletonFile
{
    private static readonly Regex NameFormat = new("^[A-Za-z0-9_]+$");
    private static readonly Regex SkeletonNameFormat = new("^[a-z0-9_]+$");
    private static readonly Regex AliasFormat = new("^[a-z0-9_]+(_[LRFB])*$");
    private static readonly Regex BoneLine = new(@"^bone\s+(\S+)\s+(\S+)\s+(\S+)\s*(.*)$");
    private static readonly Regex Field = new(@"\s*([a-z]+)=\(([^)]*)\)");

    private readonly Dictionary<string, int> byName = new();
    private readonly Dictionary<string, int> byAlias = new();

    public string Name { get; }
    public IReadOnlyList<SkeletonJoint> Joints { get; }

    private SkeletonFile(string name, List<SkeletonJoint> joints)
    {
        Name = name;
        Joints = joints;
        foreach (var joint in joints)
        {
            byName[joint.Name] = joint.Index;
            byAlias[joint.Alias] = joint.Index;
        }
    }

    public static SkeletonFile Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads the text of a <c>.skeleton</c> file; throws <see cref="SkeletonFormatException"/> on the first problem.</summary>
    public static SkeletonFile Parse(string text)
    {
        var lines = text.Split('\n');
        string? name = null, space = null, up = null, forward = null, scale = null;
        var joints = new List<SkeletonJoint>();
        var names = new Dictionary<string, int>();
        var aliases = new HashSet<string>();
        var lastLine = lines.Length;
        var firstBone = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var number = i + 1;
            var line = lines[i];
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            var keyword = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
            if (keyword == "bone")
            {
                if (firstBone == 0)
                {
                    firstBone = number;
                    CheckHeader(number, name, space, up, forward, scale);
                }
                joints.Add(ParseBone(number, line, joints, names, aliases));
                continue;
            }

            if (firstBone != 0)
                throw new SkeletonFormatException(number, $"'{keyword}' is not a bone line; the header must come before the first bone");
            var value = line[keyword.Length..].Trim();
            if (value.Length == 0 || value.Contains(' ') || value.Contains('\t'))
                throw new SkeletonFormatException(number, $"'{keyword}' needs exactly one value");
            switch (keyword)
            {
                case "skeleton":
                    Once(number, keyword, name);
                    if (!SkeletonNameFormat.IsMatch(value))
                        throw new SkeletonFormatException(number, $"The skeleton name '{value}' must use only a-z, 0-9 and _");
                    name = value;
                    break;
                case "space":
                    Once(number, keyword, space);
                    if (value != "unity") throw new SkeletonFormatException(number, $"Unsupported space '{value}'; only 'unity' is supported");
                    space = value;
                    break;
                case "up":
                    Once(number, keyword, up);
                    if (value != "+y") throw new SkeletonFormatException(number, $"Unsupported up axis '{value}'; only '+y' is supported");
                    up = value;
                    break;
                case "forward":
                    Once(number, keyword, forward);
                    if (value != "+z") throw new SkeletonFormatException(number, $"Unsupported forward axis '{value}'; only '+z' is supported");
                    forward = value;
                    break;
                case "scale":
                    Once(number, keyword, scale);
                    if (value != "1") throw new SkeletonFormatException(number, $"Unsupported scale '{value}'; only 1 (metres per unit) is supported");
                    scale = value;
                    break;
                default:
                    throw new SkeletonFormatException(number, $"Unknown line '{keyword}'; expected a header line or 'bone'");
            }
        }

        if (firstBone == 0)
        {
            CheckHeader(lastLine, name, space, up, forward, scale);
            throw new SkeletonFormatException(lastLine, "The file has no bones");
        }
        return new SkeletonFile(name!, joints);
    }

    private static void Once(int line, string keyword, object? seen)
    {
        if (seen is not null) throw new SkeletonFormatException(line, $"'{keyword}' is given twice");
    }

    private static void CheckHeader(int line, params string?[] values)
    {
        string[] keywords = ["skeleton", "space", "up", "forward", "scale"];
        for (var i = 0; i < keywords.Length; i++)
            if (values[i] is null)
                throw new SkeletonFormatException(line, $"The header is missing '{keywords[i]}'; it must come before the first bone");
    }

    private static SkeletonJoint ParseBone(int line, string text, List<SkeletonJoint> joints, Dictionary<string, int> names, HashSet<string> aliases)
    {
        var match = BoneLine.Match(text);
        if (!match.Success)
            throw new SkeletonFormatException(line, "A bone line reads 'bone <Name> <alias> <parent> pos=(x, y, z) rot=(x, y, z, w)'");
        string name = match.Groups[1].Value, alias = match.Groups[2].Value, parentName = match.Groups[3].Value;
        var index = joints.Count;

        if (!NameFormat.IsMatch(name)) throw new SkeletonFormatException(line, $"The bone name '{name}' must use only letters, digits and _");
        if (names.ContainsKey(name)) throw new SkeletonFormatException(line, $"The bone name '{name}' is already used on bone {names[name]}");
        if (!AliasFormat.IsMatch(alias)) throw new SkeletonFormatException(line, $"The alias '{alias}' of bone '{name}' must look like upper_leg_L: a-z, 0-9 and _, then optional _L _R _F _B");
        if (!aliases.Add(alias)) throw new SkeletonFormatException(line, $"The alias '{alias}' is already used by another bone");

        int parent;
        if (parentName == "-")
        {
            if (index != 0) throw new SkeletonFormatException(line, $"Bone '{name}' has no parent; only the first bone may be the root");
            if (alias != "root") throw new SkeletonFormatException(line, $"The first bone's alias must be 'root', not '{alias}'");
            parent = -1;
        }
        else
        {
            if (index == 0) throw new SkeletonFormatException(line, "The first bone must have '-' as its parent");
            if (!names.TryGetValue(parentName, out parent))
                throw new SkeletonFormatException(line, $"The parent '{parentName}' of bone '{name}' is not a bone written earlier in the file");
        }

        Vector3? pos = null, scale = null;
        Quaternion? rot = null;
        var rest = match.Groups[4].Value;
        var consumed = 0;
        foreach (Match field in Field.Matches(rest))
        {
            if (field.Index != consumed)
                throw new SkeletonFormatException(line, $"Cannot read '{rest[consumed..field.Index].Trim()}'; expected key=(numbers)");
            consumed = field.Index + field.Length;
            var key = field.Groups[1].Value;
            var numbers = field.Groups[2].Value;
            switch (key)
            {
                case "pos": Once(line, key, pos.HasValue ? "x" : null); pos = ToVector(line, key, Numbers(line, key, numbers, 3)); break;
                case "scale": Once(line, key, scale.HasValue ? "x" : null); scale = ToVector(line, key, Numbers(line, key, numbers, 3)); break;
                case "rot":
                    Once(line, key, rot.HasValue ? "x" : null);
                    var q = Numbers(line, key, numbers, 4);
                    var length = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
                    if (Math.Abs(length - 1) > 1e-3)
                        throw new SkeletonFormatException(line, $"The rotation of '{name}' is not a unit quaternion (its length is {length.ToString("0.#####", CultureInfo.InvariantCulture)})");
                    rot = Quaternion.Normalize(new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]));
                    break;
                default: throw new SkeletonFormatException(line, $"Unknown field '{key}'; expected pos, rot or scale");
            }
        }
        if (rest[consumed..].Trim().Length > 0)
            throw new SkeletonFormatException(line, $"Cannot read '{rest[consumed..].Trim()}'; expected key=(numbers)");
        if (pos is null) throw new SkeletonFormatException(line, $"Bone '{name}' has no pos=(x, y, z)");
        if (rot is null) throw new SkeletonFormatException(line, $"Bone '{name}' has no rot=(x, y, z, w)");
        if (scale is { } s && (Math.Abs(s.X - 1) > 1e-4 || Math.Abs(s.Y - 1) > 1e-4 || Math.Abs(s.Z - 1) > 1e-4))
            throw new SkeletonFormatException(line, $"Bone '{name}' has a scale other than (1, 1, 1): scaled bones are not supported");

        names[name] = index;
        return new SkeletonJoint(index, name, alias, parent, pos.Value, rot.Value);
    }

    private static double[] Numbers(int line, string key, string text, int count)
    {
        var parts = text.Split(',');
        if (parts.Length != count)
            throw new SkeletonFormatException(line, $"{key} needs {count} numbers, found {parts.Length}");
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var part = parts[i].Trim();
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                throw new SkeletonFormatException(line, $"'{part}' in {key} is not a number");
            if (!float.IsFinite((float)values[i]))
                throw new SkeletonFormatException(line, $"'{part}' in {key} is too large");
        }
        return values;
    }

    private static Vector3 ToVector(int line, string key, double[] v) => new((float)v[0], (float)v[1], (float)v[2]);

    /// <summary>The text of the file: the header, then one bone line per joint. Floats use the round-trip format, so <see cref="Parse"/> gives the same joints back.</summary>
    public string Write()
    {
        var text = new StringBuilder();
        text.Append("skeleton ").Append(Name).Append('\n');
        text.Append("space unity\nup +y\nforward +z\nscale 1\n");
        foreach (var j in Joints)
        {
            var parent = j.Parent < 0 ? "-" : Joints[j.Parent].Name;
            text.Append($"bone {j.Name} {j.Alias} {parent} pos=({F(j.Position.X)}, {F(j.Position.Y)}, {F(j.Position.Z)}) ")
                .Append($"rot=({F(j.Rotation.X)}, {F(j.Rotation.Y)}, {F(j.Rotation.Z)}, {F(j.Rotation.W)})\n");
        }
        return text.ToString();
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The index of the joint with this exact name, or -1.</summary>
    public int IndexOf(string name) => byName.GetValueOrDefault(name, -1);

    /// <summary>The index of the joint with this alias, or -1.</summary>
    public int IndexOfAlias(string alias) => byAlias.GetValueOrDefault(alias, -1);

    /// <summary>The joint with this name, or else this alias; null when there is none.</summary>
    public SkeletonJoint? Find(string nameOrAlias)
    {
        var index = IndexOf(nameOrAlias);
        if (index < 0) index = IndexOfAlias(nameOrAlias);
        return index < 0 ? null : Joints[index];
    }

    /// <summary>Unity to glTF is a mirror of X: (x, y, z) becomes (-x, y, z).</summary>
    public static Vector3 MirrorPosition(Vector3 unity) => new(-unity.X, unity.Y, unity.Z);

    /// <summary>The same mirror for a rotation: (x, y, z, w) becomes (x, -y, -z, w). Applying it twice gives back the input.</summary>
    public static Quaternion MirrorRotation(Quaternion unity) => new(unity.X, -unity.Y, -unity.Z, unity.W);

    /// <summary>
    /// The world rest transform of every joint in the format 2 / glTF model space (right-handed, +Y up, faces +Z, +X is the model's left).
    /// Each local transform is mirrored first, then composed in double precision (world = parentWorld * local).
    /// </summary>
    internal (D3 Position, M3 Rotation)[] ModelRest()
    {
        var world = new (D3 Position, M3 Rotation)[Joints.Count];
        foreach (var j in Joints)
        {
            var position = D3.From(MirrorPosition(j.Position));
            var rotation = M3.FromQuaternion(MirrorRotation(j.Rotation));
            world[j.Index] = j.Parent < 0
                ? (position, rotation)
                : (world[j.Parent].Position + world[j.Parent].Rotation * position, world[j.Parent].Rotation * rotation);
        }
        return world;
    }

    /// <summary>
    /// Finds <c>content/skeletons/&lt;name&gt;.skeleton</c>: from <paramref name="startDirectory"/> up through its parents, looking for
    /// <c>skeletons/&lt;name&gt;.skeleton</c> in a folder named content, else <c>content/skeletons/&lt;name&gt;.skeleton</c>. Null when not found.
    /// </summary>
    public static string? FindFile(string name, string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var relative = directory.Name == "content" ? "skeletons" : Path.Combine("content", "skeletons");
            var candidate = Path.Combine(directory.FullName, relative, name + ".skeleton");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
