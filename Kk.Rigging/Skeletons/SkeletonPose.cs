using System.Numerics;

namespace Kk.Rigging.Skeletons;

/// <summary>
/// Unity local transforms of a skeleton's joints (a Generic clip's keys, as a Mixamo clip on the Synty paths has them) against kk's
/// poses (a rotation in the bone frame about the head and a translation in the bone frame, docs/sdf-format-2.md "Posing"), and the
/// world positions they give, straight from the skeleton file.
/// </summary>
public static class SkeletonPose
{
    /// <summary>
    /// The kk pose that puts each named joint at the given Unity local rotation (and position): the key is the rest local rotation's
    /// inverse times the mirrored Unity rotation, since a joint's glTF local rotation is its rest local rotation times the key.
    /// </summary>
    public static Pose FromUnityLocals(SkeletonFile file, IReadOnlyDictionary<string, Quaternion> rotations, IReadOnlyDictionary<string, Vector3>? positions = null)
    {
        var pose = new Pose();
        foreach (var j in file.Joints)
        {
            var hasRotation = rotations.TryGetValue(j.Name, out var unityRotation);
            var position = Vector3.Zero;
            var hasPosition = positions is not null && positions.TryGetValue(j.Name, out position);
            if (!hasRotation && !hasPosition)
                continue;
            var rest = M3.FromQuaternion(SkeletonFile.MirrorRotation(j.Rotation));
            var key = hasRotation ? rest.Transposed() * M3.FromQuaternion(SkeletonFile.MirrorRotation(Quaternion.Normalize(unityRotation))) : M3.Identity;
            var move = hasPosition
                ? rest.Transposed() * (D3.From(SkeletonFile.MirrorPosition(position)) - D3.From(SkeletonFile.MirrorPosition(j.Position)))
                : D3.Zero;
            pose.Set(j.Name, new BonePose(key.ToQuaternion(), move.ToVector3()));
        }
        return pose;
    }

    /// <summary>
    /// Every joint's world position and rotation in Unity space when the named joints take the given local transforms and the rest keep
    /// the file's (forward kinematics, double precision); the skeleton's root sits at the model's origin.
    /// </summary>
    public static (Vector3 Position, Quaternion Rotation)[] UnityWorld(SkeletonFile file, IReadOnlyDictionary<string, Quaternion>? rotations = null, IReadOnlyDictionary<string, Vector3>? positions = null)
    {
        var world = new (D3 Position, M3 Rotation)[file.Joints.Count];
        foreach (var j in file.Joints)
        {
            var r = M3.FromQuaternion(rotations is not null && rotations.TryGetValue(j.Name, out var q) ? Quaternion.Normalize(q) : j.Rotation);
            var p = D3.From(positions is not null && positions.TryGetValue(j.Name, out var v) ? v : j.Position);
            world[j.Index] = j.Parent < 0 ? (p, r) : (world[j.Parent].Position + world[j.Parent].Rotation * p, world[j.Parent].Rotation * r);
        }
        return world.Select(w => (w.Position.ToVector3(), w.Rotation.ToQuaternion())).ToArray();
    }
}
