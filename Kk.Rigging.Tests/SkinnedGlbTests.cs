using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SDFPipeline.Meshing;
using SDFPipeline.ProgramFiles;

namespace Kk.Rigging.Tests;

public class SkinnedGlbTests
{
    private static MeshedModel Mesh()
    {
        var program = SdfProgramFile.Read("format 1\nCapsule a=(0, 0, 0) b=(0, 1, 0) radius=0.2\n");
        return ModelMesher.Mesh(program, MeshingOptions.Default with { LodCount = 1, Resolution = MeshingOptions.MinResolution });
    }

    private static RigBone Bone(int index, string name, int parent, Vector3 head, Vector3 tail, HitMode hit = HitMode.Auto)
    {
        var y = D3.From(tail - head).Normalized();
        var x = (D3.UnitX - y * D3.UnitX.Dot(y)).Normalized();
        var z = x.Cross(y);
        var frame = M3.FromColumns(x, y, z).ToQuaternion();
        return new RigBone(index, name, parent, head, tail, frame, 0.1f, null, null, null, hit, 0);
    }

    private static Skeleton MakeSkeleton()
    {
        var root = Bone(0, "root", -1, new(0, 0, 0), new(0, 0.4f, 0));
        var mid = Bone(1, "mid", 0, new(0, 0.4f, 0), new(0.3f, 0.8f, 0)) with { Hinge = new Vector3(1, 0, 0), Limit = (-1f, 1f), SpinAxis = new Vector3(0, 1, 0), Hit = HitMode.Radius, HitRadius = 0.05f };
        var tip = Bone(2, "tip", 1, new(0.3f, 0.8f, 0), new(0.3f, 1.1f, 0.1f), HitMode.None);
        return new Skeleton([root, mid, tip], [new RigSocket("hand", 2, new(0.3f, 1.1f, 0.1f), Vector3.UnitZ, Vector3.UnitY)]);
    }

    private static SkinWeights MakeWeights(PartMesh mesh)
    {
        var joints = new ushort[4 * mesh.Positions.Length];
        var weights = new float[joints.Length];
        for (var v = 0; v < mesh.Positions.Length; v++)
        {
            var t = Math.Clamp(mesh.Positions[v].Y / 0.5f, 0, 2); // 0 root .. 1 mid .. 2 tip
            var a = Math.Min((int)t, 1);
            var f = t - a;
            joints[4 * v] = (ushort)a; weights[4 * v] = 1 - f;
            joints[4 * v + 1] = (ushort)(a + 1); weights[4 * v + 1] = f;
        }
        return new SkinWeights(joints, weights);
    }

    private static BakedClip MakeClip(Quaternion rotation) => new("idle", 1, true,
        [new BakedTrack("mid", [0f, 1f], [rotation, Quaternion.Identity], [Vector3.Zero, new Vector3(0, 0.1f, 0)])],
        "[{\"kind\":\"sway\"}]");

    private static byte[] Build(out Skeleton skeleton, Quaternion? clipRotation = null)
    {
        var meshed = Mesh();
        Assert.Single(meshed.Meshes);
        skeleton = MakeSkeleton();
        return SkinnedGlb.Write(meshed, "test", skeleton, MakeWeights(meshed.Meshes[0]),
            [new HitCapsule(1, 0.05f, 0.5f)], [MakeClip(clipRotation ?? Quaternion.Normalize(new Quaternion(0.2f, 0.1f, 0.3f, 0.9f)))]);
    }

    private sealed class Glb
    {
        public JsonElement Json;
        public byte[] Bin = [];
        public int JsonLength, BinLength;

        public static Glb Read(byte[] bytes)
        {
            var g = new Glb();
            Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            Assert.Equal((uint)bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
            var at = 12;
            while (at < bytes.Length)
            {
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
                var kind = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
                Assert.Equal(0, length % 4);
                var body = bytes.AsSpan(at + 8, length);
                if (kind == 0x4E4F534A) { g.Json = JsonDocument.Parse(Encoding.UTF8.GetString(body)).RootElement.Clone(); g.JsonLength = length; }
                else { g.Bin = body.ToArray(); g.BinLength = length; }
                at += 8 + length;
            }
            Assert.Equal(bytes.Length, at);
            return g;
        }

        public float[] Floats(int accessor)
        {
            var a = Json.GetProperty("accessors")[accessor];
            var view = Json.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
            var n = a.GetProperty("count").GetInt32() * Components(a.GetProperty("type").GetString()!);
            var offset = view.GetProperty("byteOffset").GetInt32() + (a.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0);
            Assert.Equal(5126, a.GetProperty("componentType").GetInt32());
            return Enumerable.Range(0, n).Select(i => BinaryPrimitives.ReadSingleLittleEndian(Bin.AsSpan(offset + 4 * i))).ToArray();
        }

        public ushort[] Shorts(int accessor)
        {
            var a = Json.GetProperty("accessors")[accessor];
            var view = Json.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
            var n = a.GetProperty("count").GetInt32() * 4;
            var offset = view.GetProperty("byteOffset").GetInt32();
            return Enumerable.Range(0, n).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(Bin.AsSpan(offset + 2 * i))).ToArray();
        }

        public static int Components(string type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new InvalidOperationException(type) };

        public int NodeNamed(string name)
        {
            var nodes = Json.GetProperty("nodes");
            for (var i = 0; i < nodes.GetArrayLength(); i++)
                if (nodes[i].TryGetProperty("name", out var n) && n.GetString() == name)
                    return i;
            throw new InvalidOperationException(name);
        }

        public int ParentOf(int node)
        {
            var nodes = Json.GetProperty("nodes");
            for (var i = 0; i < nodes.GetArrayLength(); i++)
                if (nodes[i].TryGetProperty("children", out var c) && c.EnumerateArray().Any(x => x.GetInt32() == node))
                    return i;
            return -1;
        }

        public (M3 Rotation, D3 Translation) Local(int node)
        {
            var n = Json.GetProperty("nodes")[node];
            var rotation = n.TryGetProperty("rotation", out var r) ? Quaternion(r) : System.Numerics.Quaternion.Identity;
            var t = n.TryGetProperty("translation", out var tr) ? new D3(tr[0].GetDouble(), tr[1].GetDouble(), tr[2].GetDouble()) : D3.Zero;
            return (M3.FromQuaternion(rotation), t);
        }

        public Aff World(int node)
        {
            var (r, t) = Local(node);
            var local = new Aff(r, t);
            var p = ParentOf(node);
            return p < 0 ? local : World(p).After(local);
        }

        public static Quaternion Quaternion(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle());
    }

    [Fact]
    public void Container_lengths_are_consistent_and_accessors_stay_in_the_buffer()
    {
        var bytes = Build(out _);
        var g = Glb.Read(bytes);
        Assert.Equal(g.BinLength, g.Json.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32());
        foreach (var view in g.Json.GetProperty("bufferViews").EnumerateArray())
            Assert.True(view.GetProperty("byteOffset").GetInt32() + view.GetProperty("byteLength").GetInt32() <= g.BinLength);
        foreach (var a in g.Json.GetProperty("accessors").EnumerateArray())
        {
            var view = g.Json.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
            var size = a.GetProperty("componentType").GetInt32() switch { 5126 or 5125 => 4, 5123 => 2, _ => throw new InvalidOperationException() };
            Assert.True(a.GetProperty("count").GetInt32() * Glb.Components(a.GetProperty("type").GetString()!) * size <= view.GetProperty("byteLength").GetInt32());
        }
    }

    [Fact]
    public void Skin_joints_are_named_in_order_and_inverse_bind_matrices_undo_the_rest_world()
    {
        var g = Glb.Read(Build(out var skeleton));
        var skin = g.Json.GetProperty("skins")[0];
        var joints = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
        Assert.Equal(["root", "mid", "tip"], joints.Select(j => g.Json.GetProperty("nodes")[j].GetProperty("name").GetString()!).ToArray());
        Assert.Equal(joints[0], skin.GetProperty("skeleton").GetInt32());
        Assert.Contains(joints[0], g.Json.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()));
        var ibm = g.Floats(skin.GetProperty("inverseBindMatrices").GetInt32());
        Assert.Equal(48, ibm.Length);
        for (var i = 0; i < 3; i++)
        {
            var world = g.World(joints[i]);
            // Model-space rest world matches the skeleton
            Assert.True((world.Offset - D3.From(skeleton.Bones[i].Head)).Length < 1e-5);
            // IBM (column-major, column vectors) times the joint's world is the identity
            D3 Col(int c) => new(ibm[16 * i + 4 * c], ibm[16 * i + 4 * c + 1], ibm[16 * i + 4 * c + 2]);
            var linear = M3.FromColumns(Col(0), Col(1), Col(2));
            var product = new Aff(linear, Col(3)).After(world);
            foreach (var p in new[] { D3.Zero, D3.UnitX, D3.UnitY, D3.UnitZ, new D3(0.3, -0.7, 1.1) })
                Assert.True((product.Apply(p) - p).Length < 1e-5, $"joint {i}");
        }
    }

    [Fact]
    public void Mesh_keeps_its_attributes_gains_valid_skin_data_and_extras_sdf()
    {
        var g = Glb.Read(Build(out _));
        var meshNode = g.Json.GetProperty("nodes").EnumerateArray().First(n => n.TryGetProperty("mesh", out _));
        Assert.Equal(0, meshNode.GetProperty("skin").GetInt32());
        Assert.Equal(-1, g.ParentOf(g.NodeNamed(meshNode.GetProperty("name").GetString()!)));
        var mesh = g.Json.GetProperty("meshes")[0];
        var count = -1;
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            var a = primitive.GetProperty("attributes");
            foreach (var key in new[] { "POSITION", "NORMAL", "COLOR_0", "_MATERIAL_IDS", "_MATERIAL_BLEND", "_THICKNESS", "JOINTS_0", "WEIGHTS_0" })
                Assert.True(a.TryGetProperty(key, out _), key);
            Assert.Equal(5123, g.Json.GetProperty("accessors")[a.GetProperty("JOINTS_0").GetInt32()].GetProperty("componentType").GetInt32());
            var weights = g.Floats(a.GetProperty("WEIGHTS_0").GetInt32());
            var joints = g.Shorts(a.GetProperty("JOINTS_0").GetInt32());
            count = weights.Length / 4;
            Assert.Equal(g.Json.GetProperty("accessors")[a.GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32(), count);
            for (var v = 0; v < count; v++)
            {
                Assert.True(MathF.Abs(weights[4 * v] + weights[4 * v + 1] + weights[4 * v + 2] + weights[4 * v + 3] - 1) < 1e-4f);
                for (var k = 0; k < 4; k++)
                    Assert.True(joints[4 * v + k] < 3);
            }
        }
        var extras = g.Json.GetProperty("extras");
        Assert.Equal("metres", extras.GetProperty("sdf").GetProperty("units").GetString());
        Assert.Equal(3, extras.GetProperty("rig").GetProperty("bones").GetInt32());
        Assert.Equal("hand", extras.GetProperty("rig").GetProperty("sockets")[0].GetString());
        Assert.Equal("idle", extras.GetProperty("rig").GetProperty("clips")[0].GetString());
    }

    [Fact]
    public void Sockets_and_hit_capsules_hang_under_their_joints()
    {
        var g = Glb.Read(Build(out var skeleton));
        var tip = g.NodeNamed("tip");
        var socket = g.NodeNamed("socket_hand");
        Assert.Equal(tip, g.ParentOf(socket));
        var world = g.World(socket);
        Assert.True((world.Offset - D3.From(skeleton.Sockets[0].Position)).Length < 1e-5);
        Assert.True((world.Linear.Column(2) - D3.UnitZ).Length < 1e-5);
        Assert.True((world.Linear.Column(1) - D3.UnitY).Length < 1e-5);

        var hit = g.NodeNamed("hit_mid");
        Assert.Equal(g.NodeNamed("mid"), g.ParentOf(hit));
        var node = g.Json.GetProperty("nodes")[hit];
        var length = (skeleton.Bones[1].Tail - skeleton.Bones[1].Head).Length();
        Assert.Equal(length / 2, node.GetProperty("translation")[1].GetSingle(), 1e-5);
        Assert.False(node.TryGetProperty("rotation", out _));
        var extras = node.GetProperty("extras");
        Assert.Equal("capsule", extras.GetProperty("kind").GetString());
        Assert.Equal(0.05f, extras.GetProperty("radius").GetSingle());
        Assert.Equal(0.5f, extras.GetProperty("height").GetSingle());

        var mid = g.Json.GetProperty("nodes")[g.NodeNamed("mid")].GetProperty("extras");
        Assert.True(mid.GetProperty("bone").GetBoolean());
        Assert.Equal(2, mid.GetProperty("limit").GetArrayLength());
        Assert.Equal(1f, mid.GetProperty("hinge")[0].GetSingle());
        Assert.Equal(0.05f, mid.GetProperty("hit").GetSingle());
        Assert.Equal("none", g.Json.GetProperty("nodes")[g.NodeNamed("tip")].GetProperty("extras").GetProperty("hit").GetString());
    }

    [Fact]
    public void Animation_channels_are_node_local()
    {
        var clipRotation = Quaternion.Normalize(new Quaternion(0.2f, 0.1f, 0.3f, 0.9f));
        var g = Glb.Read(Build(out var skeleton, clipRotation));
        var animation = g.Json.GetProperty("animations")[0];
        Assert.Equal("idle", animation.GetProperty("name").GetString());
        Assert.True(animation.GetProperty("extras").GetProperty("loop").GetBoolean());
        Assert.Equal("sway", animation.GetProperty("extras").GetProperty("layers")[0].GetProperty("kind").GetString());
        var mid = g.NodeNamed("mid");
        var channels = animation.GetProperty("channels").EnumerateArray().ToArray();
        Assert.Equal(2, channels.Length);
        var samplers = animation.GetProperty("samplers");

        var rotationChannel = channels.Single(c => c.GetProperty("target").GetProperty("path").GetString() == "rotation");
        Assert.Equal(mid, rotationChannel.GetProperty("target").GetProperty("node").GetInt32());
        var sampler = samplers[rotationChannel.GetProperty("sampler").GetInt32()];
        Assert.Equal("LINEAR", sampler.GetProperty("interpolation").GetString());
        var timeAccessor = g.Json.GetProperty("accessors")[sampler.GetProperty("input").GetInt32()];
        Assert.Equal(0f, timeAccessor.GetProperty("min")[0].GetSingle());
        Assert.Equal(1f, timeAccessor.GetProperty("max")[0].GetSingle());
        var output = g.Floats(sampler.GetProperty("output").GetInt32());
        var rest = M3.FromQuaternion(skeleton.Bones[0].Frame).Transposed() * M3.FromQuaternion(skeleton.Bones[1].Frame);
        var expected = (rest * M3.FromQuaternion(clipRotation)).ToQuaternion();
        var actual = new Quaternion(output[0], output[1], output[2], output[3]);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, actual)) > 0.99999f);
        // Key 1 is the clip's identity: the node's rest rotation
        var restQ = g.Local(mid).Rotation.ToQuaternion();
        Assert.True(MathF.Abs(Quaternion.Dot(restQ, new Quaternion(output[4], output[5], output[6], output[7]))) > 0.99999f);

        var translationChannel = channels.Single(c => c.GetProperty("target").GetProperty("path").GetString() == "translation");
        var t = g.Floats(samplers[translationChannel.GetProperty("sampler").GetInt32()].GetProperty("output").GetInt32());
        var (restRotation, restTranslation) = g.Local(mid);
        var expectedT = restTranslation + restRotation * new D3(0, 0.1, 0);
        Assert.True((new D3(t[3], t[4], t[5]) - expectedT).Length < 1e-5);
        Assert.True((new D3(t[0], t[1], t[2]) - restTranslation).Length < 1e-5);
    }

    [Fact]
    public void Unknown_bone_in_a_clip_and_bad_input_throw()
    {
        var meshed = Mesh();
        var skeleton = MakeSkeleton();
        var weights = MakeWeights(meshed.Meshes[0]);
        var bad = new BakedClip("x", 1, false, [new BakedTrack("nope", [0f], [Quaternion.Identity], null)], null);
        Assert.Throws<ArgumentException>(() => SkinnedGlb.Write(meshed, "t", skeleton, weights, [], [bad]));
        Assert.Throws<ArgumentException>(() => SkinnedGlb.Write(meshed, "t", skeleton, new SkinWeights([], []), [], []));
        Assert.Throws<ArgumentException>(() => SkinnedGlb.Write(MeshedModel.Empty, "t", skeleton, weights, [], []));
    }

    [Fact]
    public void Written_file_reads_back()
    {
        var bytes = Build(out _);
        var path = Path.Combine(Path.GetTempPath(), $"skinned-{Guid.NewGuid():N}.glb");
        try
        {
            File.WriteAllBytes(path, bytes);
            var g = Glb.Read(File.ReadAllBytes(path));
            Assert.Equal("2.0", g.Json.GetProperty("asset").GetProperty("version").GetString());
        }
        finally { File.Delete(path); }
    }
}
