using System.Globalization;
using System.Numerics;
using System.Text;

namespace Kk.Rigging;

/// <summary>
/// Hit capsules (<c>collider bones</c>): one per bone from head to tail, the radius fitted to the bone's own surface (the 90th percentile
/// of the distance from the vertices the bone carries to the segment) or the bone's <c>hit=</c> radius; none for <c>hit=none</c> or a bone
/// that carries no vertex.
/// </summary>
public static class HitFit
{
    public const double Percentile = 0.9;

    public static IReadOnlyList<HitCapsule> Capsules(Skeleton skeleton, IReadOnlyList<Vector3> positions, SkinWeights weights)
    {
        var carried = new List<float>[skeleton.Bones.Count];
        for (var b = 0; b < carried.Length; b++)
            carried[b] = [];
        for (var v = 0; v < positions.Count; v++)
        {
            // Slot 0 holds the heaviest joint: the vertex is on that bone's surface, however much it is blended
            var bone = skeleton.Bones[weights.Joints[4 * v]];
            carried[bone.Index].Add(SegmentDistance(positions[v], bone.Head, bone.Tail));
        }

        var capsules = new List<HitCapsule>();
        foreach (var bone in skeleton.Bones)
        {
            if (bone.Hit == HitMode.None)
                continue;
            float radius;
            if (bone.Hit == HitMode.Radius)
                radius = bone.HitRadius;
            else if (carried[bone.Index].Count > 0)
            {
                var sorted = carried[bone.Index].Order().ToList();
                radius = sorted[Math.Min(sorted.Count - 1, (int)(Percentile * sorted.Count))];
            }
            else
                continue;
            if (radius <= 0)
                continue;
            var length = Vector3.Distance(bone.Head, bone.Tail);
            capsules.Add(new HitCapsule(bone.Index, radius, length + 2 * radius));
        }
        return capsules;
    }

    public static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / Math.Max(ab.LengthSquared(), 1e-12f), 0, 1);
        return (p - (a + ab * t)).Length();
    }

    /// <summary>The capsules as a format 1 program (a Union of Capsule nodes) for <c>sdf render</c>, moved by a pose.</summary>
    public static string PreviewProgram(Skeleton skeleton, IReadOnlyList<HitCapsule> hits, Pose? pose = null)
    {
        var maps = PoseMath.Transforms(skeleton, pose ?? new Pose());
        var text = new StringBuilder("format 1\n\n");
        string V(D3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.#####}, {v.Y:0.#####}, {v.Z:0.#####})");
        var lines = hits.Select(h =>
        {
            var bone = skeleton.Bones[h.Bone];
            var (a, b) = (maps[h.Bone].Apply(D3.From(bone.Head)), maps[h.Bone].Apply(D3.From(bone.Tail)));
            return string.Create(CultureInfo.InvariantCulture, $"Capsule a={V(a)} b={V(b)} radius={h.Radius:0.#####}");
        }).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("The model has no hit capsules (it needs 'collider bones', and bones with geometry)");
        if (lines.Count == 1)
            return text.Append(lines[0]).Append('\n').ToString();
        text.Append("Union\n");
        foreach (var line in lines)
            text.Append("    ").Append(line).Append('\n');
        return text.ToString();
    }
}
