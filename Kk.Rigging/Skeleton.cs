using System.Numerics;

namespace Kk.Rigging;

/// <summary>How a bone's hit capsule is made (the <c>hit</c> attribute).</summary>
public enum HitMode { Auto, None, Radius }

/// <summary>The kind of a bone's <c>spin</c> axis.</summary>

/// <summary>
/// A bone of the expanded skeleton (mirroring and parts already applied). Positions are in the model's space (metres, +Y up, front
/// +Z, +X the model's left). <see cref="Frame"/> is the rest orientation of the bone frame in model space: column X is the frame's
/// +X, column Y runs from head to tail, column Z = X cross Y.
/// </summary>
/// <param name="Parent">Index of the parent bone; -1 for <c>root</c>.</param>
/// <param name="Falloff">Width of the skin-weight blend with the parent in metres; 0 is rigid.</param>
/// <param name="SpinAxis">The free rotation axis in the bone frame's coordinates, or null for a bone that does not spin.</param>
/// <param name="Hinge">The hinge axis in the bone frame's coordinates, or null for a ball joint.</param>
/// <param name="Limit">With a hinge: (min, max) angle in radians; without: (cone, twist). Null when there is none.</param>
public sealed record RigBone(
    int Index,
    string Name,
    int Parent,
    Vector3 Head,
    Vector3 Tail,
    Quaternion Frame,
    float Falloff,
    Vector3? SpinAxis,
    Vector3? Hinge,
    (float Min, float Max)? Limit,
    HitMode Hit,
    float HitRadius);

/// <summary>An attachment point, in the model's space. <see cref="Dir"/> and <see cref="Up"/> are unit vectors.</summary>
public sealed record RigSocket(string Name, int Bone, Vector3 Position, Vector3 Dir, Vector3 Up);

/// <summary>The bones (root first, parents before children) and sockets of a model.</summary>
public sealed class Skeleton(IReadOnlyList<RigBone> bones, IReadOnlyList<RigSocket> sockets)
{
    public IReadOnlyList<RigBone> Bones { get; } = bones;
    public IReadOnlyList<RigSocket> Sockets { get; } = sockets;

    /// <summary>The name of the external skeleton the bones are (<c>skeleton=synty</c>), or null when the nesting made them.</summary>
    public string? External { get; init; }

    public int IndexOf(string name) => Bones.FirstOrDefault(b => b.Name == name)?.Index ?? -1;

    public RigBone? Find(string name) => Bones.FirstOrDefault(b => b.Name == name);

    /// <summary>The model-space matrix (column vectors) of the bone's rest frame: origin at the head.</summary>
    public Matrix4x4 RestWorld(int bone)
    {
        var b = Bones[bone];
        var m = Matrix4x4.CreateFromQuaternion(b.Frame);
        m.Translation = b.Head;
        return m;
    }
}

/// <summary>A bone's pose: a rotation in the bone's frame about its head, and an optional translation in the bone's frame.</summary>
public readonly record struct BonePose(Quaternion Rotation, Vector3 Translation)
{
    public static readonly BonePose Identity = new(Quaternion.Identity, Vector3.Zero);

    public bool IsIdentity => Rotation == Quaternion.Identity && Translation == Vector3.Zero;

    /// <summary>A rotation given as Euler angles in radians, in format 1's order (about X, then Y, then Z).</summary>
    public static Quaternion FromEuler(float x, float y, float z) => M3.EulerXyz(x, y, z).ToQuaternion();
}

/// <summary>A pose of a whole model: a <see cref="BonePose"/> for the bones that are moved, named as the skeleton names them.</summary>
public sealed class Pose
{
    private readonly Dictionary<string, BonePose> bones = [];

    public static Pose Rest => new();

    public IReadOnlyDictionary<string, BonePose> Bones => bones;

    public BonePose Get(string bone) => bones.GetValueOrDefault(bone, BonePose.Identity);

    public Pose Set(string bone, BonePose pose)
    {
        bones[bone] = pose;
        return this;
    }

    public Pose Rotate(string bone, float x, float y, float z) => Set(bone, new BonePose(BonePose.FromEuler(x, y, z), Get(bone).Translation));

    /// <summary>Reads <c>bone=x,y,z</c> entries separated by ';' (rotation in radians, about X, then Y, then Z).</summary>
    public static Pose Parse(string text)
    {
        var pose = new Pose();
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2);
            var numbers = parts.Length == 2 ? parts[1].Split(',', StringSplitOptions.TrimEntries) : [];
            if (numbers.Length != 3 || numbers.Any(n => !float.TryParse(n, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new FormatException($"A pose entry is bone=x,y,z (radians), not '{entry}'");
            var v = numbers.Select(n => float.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            pose.Rotate(parts[0].Trim(), v[0], v[1], v[2]);
        }
        return pose;
    }
}
