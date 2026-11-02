namespace Kk.Rigging;

/// <summary>The rigid motions a pose gives each bone, in model space.</summary>
internal static class PoseMath
{
    /// <summary>
    /// For each bone, the map from a rest-pose point of its geometry to the posed point: the parent's map applied after turning about
    /// the rest head by the pose's rotation (given in the bone frame) and moving by its translation (also in the bone frame).
    /// </summary>
    public static Aff[] Transforms(Skeleton skeleton, Pose pose)
    {
        var maps = new Aff[skeleton.Bones.Count];
        foreach (var bone in skeleton.Bones)
        {
            var p = pose.Get(bone.Name);
            var frame = M3.FromQuaternion(bone.Frame);
            var rotation = frame * M3.FromQuaternion(p.Rotation) * frame.Transposed();
            var head = D3.From(bone.Head);
            var local = new Aff(rotation, head - rotation * head + frame * D3.From(p.Translation));
            maps[bone.Index] = bone.Parent < 0 ? local : maps[bone.Parent].After(local);
        }
        return maps;
    }
}
