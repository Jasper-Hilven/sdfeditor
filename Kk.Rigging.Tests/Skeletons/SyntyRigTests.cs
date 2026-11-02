using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Kk.Rigging.Skeletons;
using SDFPipeline.Meshing;

namespace Kk.Rigging.Tests.Skeletons;

/// <summary>
/// A format 2 model on the Synty skeleton (content/skeletons/synty.skeleton): bones written by alias, placed on the file's joints; the
/// GLB's joints are the file's names, parents and local rest transforms (mirrored to glTF); poses and Mixamo keys agree with the file.
/// </summary>
public class SyntyRigTests
{
    private const double Metres = 1e-5, Radians = 1e-5;

    private static readonly Lazy<SkeletonFile> Synty = new(() => SkeletonFile.Load(Path.Combine(TestSupport.Repository(), "content", "skeletons", "synty.skeleton")));

    private static RigModel Read(string text) => RigModel.Read(text, TestSupport.Parts(), name => name == "synty" ? Synty.Value : null);

    /// <summary>A small soldier-like model: hips, the spine, mirrored legs and arms with a hand socket; the fingers are not written.</summary>
    private const string Small = """
        format 2
        skeleton=synty
        collider bones

        Material material=creature_crust_body
            Bone name=hips head=(0, 0.876, 0) tail=(0, 0.979, -0.015)
                Capsule a=(0, 0.85, 0) b=(0, 0.98, 0) radius=0.12
                Bone name=spine_01 head=(0, 0.979, -0.015) tail=(0, 1.16, -0.006)
                    Capsule a=(0, 0.98, 0) b=(0, 1.16, 0) radius=0.11
                    Bone name=spine_02 head=(0, 1.16, -0.006) tail=(0, 1.337, -0.033)
                        Capsule a=(0, 1.16, 0) b=(0, 1.34, 0) radius=0.12
                        Bone name=spine_03 head=(0, 1.337, -0.033) tail=(0, 1.449, -0.024)
                            Capsule a=(0, 1.34, 0) b=(0, 1.45, 0) radius=0.12
                            SymmetryX
                                Bone name=clavicle head=(0.0745, 1.392, 0.0135) tail=(0.191, 1.39, -0.048)
                                    Bone name=shoulder head=(0.191, 1.39, -0.048) tail=(0.529, 1.361, -0.053)
                                        Capsule a=(0.19, 1.39, -0.05) b=(0.53, 1.36, -0.05) radius=0.05
                                        Bone name=elbow head=(0.529, 1.361, -0.053) tail=(0.8, 1.361, -0.034)
                                            Capsule a=(0.53, 1.36, -0.05) b=(0.8, 1.36, -0.03) radius=0.04
                                            Bone name=hand head=(0.8, 1.361, -0.034) tail=(0.9, 1.36, -0.067)
                                                Sphere radius=0.05
                                                Socket name=grip at=(0.85, 1.36, 0) dir=(0, 0, 1)
                            Bone name=neck head=(0, 1.449, -0.024) tail=(0, 1.569, -0.005)
                                Bone name=head head=(0, 1.569, -0.005) tail=(0, 1.75, 0)
                                    Translate translation=(0, 1.68, 0.02)
                                        Sphere radius=0.13
                SymmetryX
                    Bone name=upper_leg head=(0.099, 0.832, -0.02) tail=(0.112, 0.432, -0.02)
                        Capsule a=(0.1, 0.83, -0.02) b=(0.11, 0.43, -0.02) radius=0.07
                        Bone name=lower_leg head=(0.112, 0.432, -0.02) tail=(0.114, 0.056, -0.037)
                            Capsule a=(0.11, 0.43, -0.02) b=(0.11, 0.06, -0.04) radius=0.055
                            Bone name=ankle head=(0.114, 0.056, -0.037) tail=(0.114, -0.003, 0.06)
                                Capsule a=(0.11, 0.05, -0.04) b=(0.11, 0.02, 0.12) radius=0.04
        """;

    private static readonly Lazy<(RigModel Model, RigMesh Mesh, byte[] Glb)> SmallMeshed = new(() =>
    {
        var model = Read(Small);
        var mesh = RigMesher.Mesh(model, TestSupport.Parts(), new MeshingOptions { Resolution = 64 });
        return (model, mesh, RigMesher.Glb(mesh, "small"));
    });

    [Fact]
    public void The_file_has_the_clip_paths_of_rifleaimidle()
    {
        // The 39 paths rifleaimidle.anim keys, from medievalminiguns f198d418; the FBX adds 11 bones no clip keys (ends, face, toes)
        string[] keyed =
        [
            "Root", "Hips", "Spine_01", "Spine_02", "Spine_03", "Clavicle_L", "Shoulder_L", "Elbow_L", "Hand_L", "Finger_01", "Finger_02", "Finger_03",
            "IndexFinger_01", "IndexFinger_02", "IndexFinger_03", "Thumb_01", "Thumb_02", "Clavicle_R", "Shoulder_R", "Elbow_R", "Hand_R",
            "Finger_011", "Finger_021", "Finger_031", "IndexFinger_011", "IndexFinger_021", "IndexFinger_031", "Thumb_011", "Thumb_021", "Neck", "Head",
            "UpperLeg_L", "LowerLeg_L", "Ankle_L", "Ball_L", "UpperLeg_R", "LowerLeg_R", "Ankle_R", "Ball_R",
        ];
        var file = Synty.Value;
        Assert.Equal(50, file.Joints.Count);
        foreach (var name in keyed)
            Assert.True(file.IndexOf(name) >= 0, name);
        string Path(int j) => file.Joints[j].Parent < 0 ? file.Joints[j].Name : Path(file.Joints[j].Parent) + "/" + file.Joints[j].Name;
        Assert.Equal("Root/Hips/Spine_01/Spine_02/Spine_03/Clavicle_R/Shoulder_R/Elbow_R/Hand_R/Finger_011/Finger_021/Finger_031", Path(file.IndexOf("Finger_031")));
        Assert.Equal("Root/Hips/UpperLeg_L/LowerLeg_L/Ankle_L/Ball_L", Path(file.IndexOf("Ball_L")));
    }

    [Fact]
    public void The_rest_pose_is_a_T_pose_facing_Z_with_the_left_side_on_X()
    {
        var file = Synty.Value;
        var rest = file.ModelRest();
        D3 At(string name) => rest[file.IndexOf(name)].Position;
        Assert.Equal(0, At("Root").Length, 6);
        Assert.InRange(At("Head").Y, 1.5, 1.6);
        Assert.True(At("Hand_L").X > 0.75 && At("Hand_R").X < -0.75, "format 2's +X is the model's left");
        Assert.Equal(At("Shoulder_L").Y, At("Hand_L").Y, 1); // arms out level
        Assert.True(At("Toes_L").Z > At("Ankle_L").Z, "the feet point to +Z");
        foreach (var j in file.Joints.Where(j => j.Name.EndsWith("_L", StringComparison.Ordinal)))
        {
            var mirror = rest[file.IndexOf(j.Name[..^2] + "_R")].Position;
            Assert.True((At(j.Name) - new D3(-mirror.X, mirror.Y, mirror.Z)).Length < 1e-3, $"{j.Name} mirrors its _R");
        }
    }

    [Fact]
    public void Aliases_and_symmetry_give_the_Synty_names_in_the_files_order()
    {
        var model = Read(Small);
        Assert.False(model.HasErrors, string.Join("\n", model.Issues));
        var file = Synty.Value;
        Assert.Equal(file.Joints.Select(j => j.Name), model.Skeleton.Bones.Select(b => b.Name));
        Assert.Equal(file.Joints.Select(j => j.Parent), model.Skeleton.Bones.Select(b => b.Parent));
        Assert.Same(file, model.External);
        Assert.Equal("upper_leg_R", model.AliasOf("UpperLeg_R"));
        // The unwritten bones (fingers, face, feet ends) are one warning; the others are the small model's own (bones without geometry)
        var warning = Assert.Single(model.Issues, i => i.Message.Contains("does not write"));
        Assert.All(model.Issues, i => Assert.False(i.IsError));
        Assert.Equal(4, model.Issues.Count);
        Assert.Contains("finger_01_R", warning.Message);
        Assert.DoesNotContain("upper_leg_L", warning.Message);
        // The socket hangs off the hand it was written in, on both sides
        Assert.Equal(file.IndexOf("Hand_L"), model.Skeleton.Sockets.Single(s => s.Name == "grip_L").Bone);
        Assert.Equal(file.IndexOf("Hand_R"), model.Skeleton.Sockets.Single(s => s.Name == "grip_R").Bone);
    }

    [Fact]
    public void Heads_and_frames_are_the_skeletons()
    {
        var model = Read(Small);
        var rest = Synty.Value.ModelRest();
        foreach (var b in model.Skeleton.Bones)
        {
            Assert.True((D3.From(b.Head) - rest[b.Index].Position).Length < Metres, b.Name);
            Assert.True(Angle(b.Frame, rest[b.Index].Rotation.ToQuaternion()) < Radians, b.Name);
        }
    }

    [Fact]
    public void Write_keeps_the_skeleton_line()
    {
        var text = Read(Small).Write();
        Assert.StartsWith("format 2\nskeleton=synty\n", text);
        Assert.Equal(50, Read(text).Skeleton.Bones.Count);
    }

    [Theory]
    [InlineData("Bone name=upper_arm head=(0, 1, 0)", "not a bone of the synty skeleton")]
    [InlineData("Bone name=hips head=(0, 0.876, 0)\n        Bone name=neck head=(0, 1.449, -0.024)", "its parent in the synty skeleton is 'spine_03'")]
    public void Unknown_bones_and_wrong_nesting_are_errors(string bones, string message)
    {
        var model = Read($"format 2\nskeleton=synty\nMaterial material=creature_crust_body\n    {bones}\n            Sphere radius=0.1\n");
        Assert.Contains(model.Issues, i => i.IsError && i.Message.Contains(message));
    }

    [Fact]
    public void A_head_far_from_the_joint_is_a_warning_and_the_joint_wins()
    {
        var model = Read("format 2\nskeleton=synty\nMaterial material=creature_crust_body\n    Bone name=hips head=(0, 0.8, 0)\n        Sphere radius=0.1\n");
        Assert.False(model.HasErrors);
        Assert.Contains(model.Issues, i => !i.IsError && i.Message.Contains("7.6 cm from the synty skeleton's joint Hips"));
        Assert.Equal(0.8762761f, model.Skeleton.Find("Hips")!.Head.Y, 5);
    }

    [Fact]
    public void A_missing_skeleton_or_a_second_skeleton_line_is_reported()
    {
        Assert.Contains(RigModel.Read("format 2\nskeleton=nobody\nSphere radius=1\n", null, _ => null).Issues, i => i.IsError && i.Message.Contains("no skeleton 'nobody'"));
        Assert.Throws<RigFormatException>(() => Read("format 2\nskeleton=synty\nskeleton=synty\nSphere radius=1\n"));
        Assert.Throws<RigFormatException>(() => Read("format 1\nskeleton=synty\nSphere radius=1\n"));
    }

    [Fact]
    public void The_glb_joints_are_the_skeleton_files_local_rest_transforms_mirrored()
    {
        var (_, _, bytes) = SmallMeshed.Value;
        var glb = GlbJson(bytes, out var bin);
        var file = Synty.Value;
        var nodes = glb.GetProperty("nodes");
        var skin = glb.GetProperty("skins")[0];
        var joints = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
        Assert.Equal(file.Joints.Count, joints.Length);
        var parentOf = new Dictionary<int, int>();
        for (var n = 0; n < nodes.GetArrayLength(); n++)
            if (nodes[n].TryGetProperty("children", out var children))
                foreach (var c in children.EnumerateArray())
                    parentOf[c.GetInt32()] = n;
        Assert.Contains(joints[0], glb.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()));
        Assert.Equal("synty", glb.GetProperty("extras").GetProperty("rig").GetProperty("skeleton").GetString());

        var inverse = Floats(glb, bin, skin.GetProperty("inverseBindMatrices").GetInt32());
        var rest = file.ModelRest();
        foreach (var j in file.Joints)
        {
            var node = nodes[joints[j.Index]];
            Assert.Equal(j.Name, node.GetProperty("name").GetString());
            if (j.Parent >= 0)
                Assert.Equal(joints[j.Parent], parentOf[joints[j.Index]]);
            var t = node.TryGetProperty("translation", out var tr) ? new Vector3(tr[0].GetSingle(), tr[1].GetSingle(), tr[2].GetSingle()) : Vector3.Zero;
            var r = node.TryGetProperty("rotation", out var ro) ? new Quaternion(ro[0].GetSingle(), ro[1].GetSingle(), ro[2].GetSingle(), ro[3].GetSingle()) : Quaternion.Identity;
            Assert.True((t - SkeletonFile.MirrorPosition(j.Position)).Length() < Metres, $"{j.Name} translation {t}");
            Assert.True(Angle(r, SkeletonFile.MirrorRotation(j.Rotation)) < Radians, $"{j.Name} rotation {r}");

            // Inverse bind matrix = inverse of the rest world transform (column-major)
            var world = new Aff(rest[j.Index].Rotation, rest[j.Index].Position);
            var m = inverse.AsSpan(16 * j.Index, 16);
            var bound = new Aff(new M3(m[0], m[4], m[8], m[1], m[5], m[9], m[2], m[6], m[10]), new D3(m[12], m[13], m[14])).After(world);
            Assert.True((bound.Apply(new D3(0.3, -0.2, 0.5)) - new D3(0.3, -0.2, 0.5)).Length < 1e-5, $"{j.Name} inverse bind");
        }
    }

    [Fact]
    public void Hit_capsules_run_head_to_tail_although_the_joint_axes_are_the_skeletons()
    {
        var (model, mesh, bytes) = SmallMeshed.Value;
        Assert.NotEmpty(mesh.Hits);
        var glb = GlbJson(bytes, out _);
        var nodes = glb.GetProperty("nodes");
        foreach (var hit in mesh.Hits)
        {
            var bone = model.Skeleton.Bones[hit.Bone];
            var node = Enumerable.Range(0, nodes.GetArrayLength()).Select(i => nodes[i]).Single(n => n.GetProperty("name").GetString() == "hit_" + bone.Name);
            var t = node.GetProperty("translation");
            var r = node.TryGetProperty("rotation", out var ro) ? new Quaternion(ro[0].GetSingle(), ro[1].GetSingle(), ro[2].GetSingle(), ro[3].GetSingle()) : Quaternion.Identity;
            // In model space: the node sits at the bone's midpoint and its +Y runs head to tail
            var frame = M3.FromQuaternion(bone.Frame);
            var mid = D3.From(bone.Head) + frame * new D3(t[0].GetDouble(), t[1].GetDouble(), t[2].GetDouble());
            Assert.True((mid - D3.From((bone.Head + bone.Tail) / 2)).Length < 1e-5, bone.Name);
            var y = frame * (M3.FromQuaternion(r) * D3.UnitY);
            Assert.True((y - D3.From(Vector3.Normalize(bone.Tail - bone.Head))).Length < 1e-5, bone.Name);
        }
    }

    [Fact]
    public void A_hand_written_pose_moves_the_joints_as_the_skeleton_file_does()
    {
        var model = Read(Small);
        var file = Synty.Value;
        // Elbow_R bent 1.2 rad about its own joint axis Z, the hips turned: through kk's pose, and as Unity local rotations
        var pose = new Pose().Rotate("Elbow_R", 0, 0, 1.2f).Rotate("Hips", 0.3f, 0, 0);
        var unity = new Dictionary<string, Quaternion>();
        foreach (var (name, p) in pose.Bones)
        {
            var j = file.Find(name)!;
            // glTF local = rest local x key; back to Unity by the mirror
            unity[name] = SkeletonFile.MirrorRotation(SkeletonFile.MirrorRotation(j.Rotation) * p.Rotation);
        }
        AssertSame(model, pose, SkeletonPose.UnityWorld(file, unity));
    }

    [Fact]
    public void A_converted_Mixamo_key_moves_the_joints_where_Unity_put_them()
    {
        var model = Read(Small);
        var file = Synty.Value;
        // The source FBX's imported transforms (the rifle aim stance baked into it), Unity local values from medievalminiguns f198d418
        var rotations = new Dictionary<string, Quaternion>
        {
            ["Root"] = new(-0.111136883f, 0.340234548f, 0.0273006726f, 0.933350861f),
            ["Hips"] = new(0.461136639f, -0.5360532f, -0.461136639f, 0.5360532f),
            ["Spine_01"] = new(0.985653758f, -0.168772951f, -0.00151539815f, 0.000372631563f),
            ["Spine_02"] = new(0.9989623f, 0.04506514f, 0.006592043f, -0.000202747746f),
            ["Spine_03"] = new(0.9677523f, -0.2518199f, -0.006485135f, 0.0005033893f),
            ["Clavicle_R"] = new(0.8060261f, 0.380258471f, 0.441715539f, -0.103018634f),
            ["Shoulder_R"] = new(0.239856243f, 0.6949601f, 0.450788945f, 0.506249666f),
            ["Elbow_R"] = new(-0.0005327985f, 0.756316841f, 0.0005901915f, 0.654205f),
            ["Hand_R"] = new(-0.3218374f, -0.105923906f, -0.207982972f, 0.917575f),
            ["Neck"] = new(0.0656418f, -0.000212550091f, 0.0453161f, 0.9968137f),
            ["Head"] = new(0.491439044f, -0.569133341f, 0.4279158f, 0.501460969f),
            ["UpperLeg_L"] = new(-0.368760079f, -0.124795415f, -0.1317f, 0.911645353f),
            ["LowerLeg_L"] = new(0.632865965f, 0.180694476f, -0.130463257f, 0.741491437f),
            ["Ankle_L"] = new(0.8584975f, 0.5035732f, 0.033426065f, -0.0909886f),
        };
        var positions = new Dictionary<string, Vector3> { ["Root"] = new(0.118053548f, -0.00681526167f, 0.178558111f) };
        var expected = SkeletonPose.UnityWorld(file, rotations, positions);
        // Unity's own world positions of those joints in the imported FBX (its log, 4 decimals)
        Assert.True((expected[file.IndexOf("Hand_R")].Position - new Vector3(0.1934f, 1.2313f, 0.2368f)).Length() < 2e-4);
        Assert.True((expected[file.IndexOf("Head")].Position - new Vector3(0.0740f, 1.4947f, 0.0835f)).Length() < 2e-4);
        Assert.True((expected[file.IndexOf("Ankle_L")].Position - new Vector3(-0.1392f, 0.0589f, 0.2061f)).Length() < 2e-4);
        AssertSame(model, SkeletonPose.FromUnityLocals(file, rotations, positions), expected);
    }

    /// <summary>kk's posed joints (PoseMath, as rig pose and the skin check use) against the expected Unity world transforms, mirrored.</summary>
    private static void AssertSame(RigModel model, Pose pose, (Vector3 Position, Quaternion Rotation)[] unityWorld)
    {
        var maps = PoseMath.Transforms(model.Skeleton, pose);
        foreach (var b in model.Skeleton.Bones)
        {
            var head = maps[b.Index].Apply(D3.From(b.Head));
            var expected = D3.From(SkeletonFile.MirrorPosition(unityWorld[b.Index].Position));
            Assert.True((head - expected).Length < Metres, $"{b.Name}: {head} against {expected}");
            var frame = (maps[b.Index].Linear * M3.FromQuaternion(b.Frame)).ToQuaternion();
            var angle = Angle(frame, SkeletonFile.MirrorRotation(unityWorld[b.Index].Rotation));
            Assert.True(angle < Radians, $"{b.Name}: frame off by {angle} rad");
        }
    }

    /// <summary>The angle of the rotation between two orientations, in double precision (atan2 of the relative quaternion, not acos of a dot).</summary>
    private static double Angle(Quaternion a, Quaternion b)
    {
        double ax = a.X, ay = a.Y, az = a.Z, aw = a.W, bx = b.X, by = b.Y, bz = b.Z, bw = b.W;
        // conj(a) * b
        var w = aw * bw + ax * bx + ay * by + az * bz;
        var x = aw * bx - ax * bw - ay * bz + az * by;
        var y = aw * by + ax * bz - ay * bw - az * bx;
        var z = aw * bz - ax * by + ay * bx - az * bw;
        return 2 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w));
    }

    private static JsonElement GlbJson(byte[] bytes, out byte[] bin)
    {
        JsonElement json = default;
        bin = [];
        for (var at = 12; at < bytes.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
            var kind = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
            if (kind == 0x4E4F534A)
                json = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, at + 8, length)).RootElement.Clone();
            else
                bin = bytes.AsSpan(at + 8, length).ToArray();
            at += 8 + length;
        }
        return json;
    }

    private static float[] Floats(JsonElement json, byte[] bin, int accessor)
    {
        var a = json.GetProperty("accessors")[accessor];
        var view = json.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
        var n = a.GetProperty("count").GetInt32() * (a.GetProperty("type").GetString() == "MAT4" ? 16 : 1);
        var offset = view.GetProperty("byteOffset").GetInt32();
        return Enumerable.Range(0, n).Select(i => BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(offset + 4 * i))).ToArray();
    }
}
