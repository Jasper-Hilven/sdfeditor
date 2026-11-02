using System.Numerics;
using SDFPipeline.ProgramFiles;
using SDFProject.Calculation;

namespace Kk.Rigging;

/// <summary>The worst distance between the skinned mesh and the exact posed surface among the vertices a bone carries.</summary>
/// <param name="Pose">The name of the pose where the worst gap was.</param>
public sealed record BoneGap(string Bone, int Vertices, float MaxGap, float MeanGap, string Pose);

/// <summary>
/// <c>kk rig check</c>'s quality measure for skin weights: pose the rest mesh by linear blend skinning, evaluate the exact posed SDF
/// (<see cref="RigModel.PosedProgram"/>) at the skinned vertices, and report the largest |distance| per bone.
/// </summary>
public static class SkinGap
{
    /// <summary>A few poses per bone: turned about its hinge axis (or its frame's X, then Z, for a ball joint) by 0.6 radian each way, kept inside the bone's limit.</summary>
    public static IReadOnlyList<(string Name, Pose Pose)> StandardPoses(Skeleton skeleton, float angle = 0.6f)
    {
        var poses = new List<(string, Pose)>();
        foreach (var bone in skeleton.Bones.Skip(1))
        {
            var axes = bone.SpinAxis is not null ? [] : bone.Hinge is { } hinge ? new[] { hinge } : [Vector3.UnitX, Vector3.UnitZ];
            foreach (var axis in axes)
            foreach (var sign in new[] { 1f, -1f })
            {
                var a = sign * angle;
                if (bone.Hinge is not null && bone.Limit is { } limit)
                    a = Math.Clamp(a, limit.Min, limit.Max);
                if (Math.Abs(a) < 1e-3f)
                    continue;
                var q = Quaternion.CreateFromAxisAngle(axis, a);
                poses.Add(($"{bone.Name} {(axis == Vector3.UnitX ? "x" : axis == Vector3.UnitZ ? "z" : "hinge")}{(sign > 0 ? "+" : "-")}{Math.Abs(a):0.##}",
                    new Pose().Set(bone.Name, new BonePose(q, Vector3.Zero))));
            }
        }
        return poses;
    }

    public static IReadOnlyList<BoneGap> Measure(RigModel model, PartSource? parts, RigMesh mesh, IEnumerable<(string Name, Pose Pose)> poses)
    {
        var skeleton = mesh.Skeleton;
        var body = mesh.Body;
        var count = skeleton.Bones.Count;
        var max = new float[count];
        var sum = new double[count];
        var vertices = new int[count];
        var worst = new string[count];
        var carrier = new int[body.Positions.Length];
        for (var v = 0; v < carrier.Length; v++)
        {
            carrier[v] = mesh.Weights.Joints[4 * v];
            vertices[carrier[v]]++;
        }

        var measured = 0;
        foreach (var (name, pose) in poses)
        {
            var distance = new SdfCalculatorCpu().Prepare(SdfProgramFile.Read(model.PosedProgram(pose), parts)).Distance;
            var maps = PoseMath.Transforms(skeleton, pose);
            var gaps = new float[carrier.Length];
            Parallel.For(0, gaps.Length, v =>
            {
                var skinned = Skinning.Skin(maps, mesh.Weights.Joints.AsSpan(4 * v, 4), mesh.Weights.Weights.AsSpan(4 * v, 4), body.Positions[v]);
                gaps[v] = MathF.Abs(distance(skinned));
            });
            for (var v = 0; v < gaps.Length; v++)
            {
                var b = carrier[v];
                sum[b] += gaps[v];
                if (gaps[v] > max[b])
                {
                    max[b] = gaps[v];
                    worst[b] = name;
                }
            }
            measured++;
        }

        return skeleton.Bones.Where(b => vertices[b.Index] > 0)
            .Select(b => new BoneGap(b.Name, vertices[b.Index], max[b.Index], measured == 0 ? 0 : (float)(sum[b.Index] / (measured * vertices[b.Index])), worst[b.Index] ?? "-"))
            .ToList();
    }
}
