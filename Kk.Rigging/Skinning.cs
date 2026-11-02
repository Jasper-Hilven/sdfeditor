using System.Numerics;
using SDFPipeline.Meshing;

namespace Kk.Rigging;

/// <summary>
/// Skin weights from the SDF. A vertex of the final mesh is weighted to the bones whose own geometry is nearly as close to it as the
/// closest bone's: bone <c>b</c> weighs <c>smoothstep(1 - (d_b - d_min) / W)</c> where <c>d</c> is the distance to a bone's own geometry and
/// <c>W</c> the width of the blend between <c>b</c> and the nearest bone, which is the child's <c>falloff</c> when one is the other's
/// parent, and 0 (rigid) for any other pair. The nearest bone weighs 1. At most four bones are kept and the weights are normalised.
/// </summary>
public static class Skinning
{
    /// <param name="distances">For each bone, the distance to its own geometry at a point, or null for a bone without geometry.</param>
    public static SkinWeights Weights(Skeleton skeleton, IReadOnlyList<Func<Vector3, float>?> distances, IReadOnlyList<Vector3> positions)
    {
        var joints = new ushort[4 * positions.Count];
        var weights = new float[4 * positions.Count];
        Parallel.For(0, positions.Count, v =>
        {
            var (j, w) = WeightsAt(skeleton, distances, positions[v]);
            for (var k = 0; k < 4; k++)
            {
                joints[4 * v + k] = j[k];
                weights[4 * v + k] = w[k];
            }
        });
        return new SkinWeights(joints, weights);
    }

    public static (ushort[] Joints, float[] Weights) WeightsAt(Skeleton skeleton, IReadOnlyList<Func<Vector3, float>?> distances, Vector3 p)
    {
        var bones = skeleton.Bones;
        var d = new float[bones.Count];
        var owner = -1;
        for (var b = 0; b < bones.Count; b++)
        {
            d[b] = distances[b]?.Invoke(p) ?? float.PositiveInfinity;
            if (float.IsNaN(d[b]))
                d[b] = float.PositiveInfinity;
            if (owner < 0 || d[b] < d[owner])
                owner = b;
        }
        if (owner < 0 || float.IsPositiveInfinity(d[owner]))
            owner = 0;   // not near anything: the root carries it

        var candidates = new List<(int Bone, float Weight)> { (owner, 1) };
        for (var b = 0; b < bones.Count; b++)
        {
            if (b == owner || float.IsPositiveInfinity(d[b]))
                continue;
            var width = Width(bones, owner, b);
            if (width <= 0)
                continue;
            var t = 1 - (d[b] - d[owner]) / width;
            if (t <= 0)
                continue;
            candidates.Add((b, t * t * (3 - 2 * Math.Min(t, 1))));
        }

        var top = candidates.OrderByDescending(c => c.Weight).ThenBy(c => c.Bone).Take(4).ToList();
        var sum = top.Sum(c => c.Weight);
        var joints = new ushort[4];
        var weights = new float[4];
        for (var k = 0; k < top.Count; k++)
        {
            joints[k] = (ushort)top[k].Bone;
            weights[k] = top[k].Weight / sum;
        }
        return (joints, weights);
    }

    private static float Width(IReadOnlyList<RigBone> bones, int a, int b)
    {
        if (bones[b].Parent == a)
            return bones[b].Falloff;
        if (bones[a].Parent == b)
            return bones[a].Falloff;
        return 0;
    }

    /// <summary>The skinned (linear blend) position of a rest-pose vertex under a pose.</summary>
    internal static Vector3 Skin(IReadOnlyList<Aff> maps, ReadOnlySpan<ushort> joints, ReadOnlySpan<float> weights, Vector3 rest)
    {
        var result = D3.Zero;
        var p = D3.From(rest);
        for (var k = 0; k < 4; k++)
            if (weights[k] > 0)
                result += maps[joints[k]].Apply(p) * weights[k];
        return result.ToVector3();
    }
}
