using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SDFPipeline.Meshing;

namespace Kk.Rigging;

/// <summary>Skin weights of the mesh's vertices: four joints and four weights per vertex, vertex-major.</summary>
/// <param name="Joints">Bone indices (<see cref="RigBone.Index"/>), 4 per vertex; unused slots hold joint 0 with weight 0.</param>
/// <param name="Weights">4 per vertex; they sum to 1 per vertex.</param>
public sealed record SkinWeights(ushort[] Joints, float[] Weights);

/// <summary>A bone's hit capsule; <paramref name="Height"/> is the whole capsule including its end caps, along the bone's Y axis.</summary>
public sealed record HitCapsule(int Bone, float Radius, float Height);

/// <summary>
/// Turns the pipeline's GLB of a rigged model's mesh into a skinned glTF: the bytes <see cref="GlbWriter"/> writes are edited, so every
/// per-vertex material attribute stays as the pipeline made it. Added: a joint node per bone (local axes = the bone frame), skin with
/// inverse bind matrices, JOINTS_0 and WEIGHTS_0, socket and hit nodes under their joints, and the clips as animations.
/// </summary>
public static class SkinnedGlb
{
    private const uint JsonChunk = 0x4E4F534A;
    private const int ArrayBuffer = 34962;
    private const int Float = 5126;
    private const int UnsignedShort = 5123;

    public static byte[] Write(MeshedModel meshed, string name, Skeleton skeleton, SkinWeights weights, IReadOnlyList<HitCapsule> hits, IReadOnlyList<BakedClip> clips)
    {
        if (meshed.Meshes.Count != 1)
            throw new ArgumentException($"A skinned model has exactly one mesh, not {meshed.Meshes.Count}", nameof(meshed));
        var mesh = meshed.Meshes[0];
        if (mesh.Lods.Count != 0 || mesh.Instances.Count != 0)
            throw new ArgumentException("A skinned mesh has no levels of detail and no instances", nameof(meshed));
        var bones = skeleton.Bones;
        if (bones.Count == 0)
            throw new ArgumentException("The skeleton has no bones", nameof(skeleton));
        for (var i = 0; i < bones.Count; i++)
            if (bones[i].Index != i || bones[i].Parent >= i || (i == 0) != (bones[i].Parent < 0))
                throw new ArgumentException($"Bone {bones[i].Name} is out of order: root first, parents before children, Index = position", nameof(skeleton));

        var vertexCount = mesh.Positions.Length;
        if (weights.Joints.Length != 4 * vertexCount || weights.Weights.Length != 4 * vertexCount)
            throw new ArgumentException($"Weights need 4 entries for each of the {vertexCount} vertices", nameof(weights));
        for (var v = 0; v < vertexCount; v++)
        {
            var sum = 0f;
            for (var k = 0; k < 4; k++)
            {
                var w = weights.Weights[4 * v + k];
                if (w < 0 || float.IsNaN(w))
                    throw new ArgumentException($"Vertex {v} has a weight of {w}", nameof(weights));
                if (w > 0 && weights.Joints[4 * v + k] >= bones.Count)
                    throw new ArgumentException($"Vertex {v} is weighted to joint {weights.Joints[4 * v + k]}, but there are {bones.Count} bones", nameof(weights));
                sum += w;
            }
            if (MathF.Abs(sum - 1) > 1e-3f)
                throw new ArgumentException($"Vertex {v}'s weights sum to {sum}, not 1", nameof(weights));
        }

        var (document, binary) = Parse(GlbWriter.Write(meshed, name));
        var buffer = new MemoryStream();
        buffer.Write(binary);
        var accessors = document["accessors"]!.AsArray();
        var bufferViews = document["bufferViews"]!.AsArray();
        var nodes = document["nodes"]!.AsArray();

        int AddView(ReadOnlySpan<byte> bytes, int? target)
        {
            while (buffer.Length % 4 != 0)
                buffer.WriteByte(0);
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = (int)buffer.Length, ["byteLength"] = bytes.Length };
            if (target is { } t)
                view["target"] = t;
            buffer.Write(bytes);
            bufferViews.Add(view);
            return bufferViews.Count - 1;
        }

        int AddAccessor(ReadOnlySpan<byte> bytes, int? target, int componentType, int count, string type, float[]? min = null, float[]? max = null)
        {
            var accessor = new JsonObject { ["bufferView"] = AddView(bytes, target), ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (min is not null)
                accessor["min"] = new JsonArray(min.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
            if (max is not null)
                accessor["max"] = new JsonArray(max.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        // The vertex count must be what GlbWriter wrote, or the weights would not line up
        var meshNodes = document["meshes"]!.AsArray();
        if (meshNodes.Count != 1)
            throw new InvalidOperationException("The pipeline's file has more than one mesh");
        var primitives = meshNodes[0]!["primitives"]!.AsArray();
        var written = accessors[primitives[0]!["attributes"]!["POSITION"]!.GetValue<int>()]!["count"]!.GetValue<int>();
        if (written != vertexCount)
            throw new InvalidOperationException($"The pipeline wrote {written} vertices, the weights are for {vertexCount}");

        var jointsAccessor = AddAccessor(Bytes(weights.Joints), ArrayBuffer, UnsignedShort, vertexCount, "VEC4");
        var weightsAccessor = AddAccessor(Bytes(weights.Weights), ArrayBuffer, Float, vertexCount, "VEC4");
        foreach (var primitive in primitives)
        {
            var attributes = primitive!["attributes"]!.AsObject();
            attributes["JOINTS_0"] = jointsAccessor;
            attributes["WEIGHTS_0"] = weightsAccessor;
        }

        // Rest frames in double precision
        var frames = bones.Select(b => M3.FromQuaternion(b.Frame)).ToArray();
        var heads = bones.Select(b => D3.From(b.Head)).ToArray();
        var restRotation = new M3[bones.Count];
        var restTranslation = new D3[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var p = bones[i].Parent;
            restRotation[i] = p < 0 ? frames[i] : frames[p].Transposed() * frames[i];
            restTranslation[i] = p < 0 ? heads[i] : frames[p].Transposed() * (heads[i] - heads[p]);
        }

        // Nodes: the mesh's nodes get the skin; then the joints, then sockets and hit capsules
        var firstJoint = nodes.Count;
        foreach (var node in nodes)
            if (node!["mesh"] is not null)
                node["skin"] = 0;
        var children = new List<int>[bones.Count];
        for (var i = 0; i < bones.Count; i++)
            children[i] = [];
        for (var i = 0; i < bones.Count; i++)
        {
            var b = bones[i];
            var joint = new JsonObject { ["name"] = b.Name };
            SetRotation(joint, restRotation[i].ToQuaternion());
            SetTranslation(joint, restTranslation[i].ToVector3());
            var extras = JointExtras(b);
            if (extras is not null)
                joint["extras"] = extras;
            nodes.Add(joint);
            if (b.Parent >= 0)
                children[b.Parent].Add(firstJoint + i);
        }

        var socketNames = new List<string>();
        foreach (var socket in skeleton.Sockets)
        {
            if (socket.Bone < 0 || socket.Bone >= bones.Count)
                throw new ArgumentException($"Socket {socket.Name} is on bone {socket.Bone}, which does not exist", nameof(skeleton));
            var f = frames[socket.Bone].Transposed();
            var z = D3.From(socket.Dir).Normalized();
            var up = D3.From(socket.Up);
            var y = (up - z * up.Dot(z));
            if (y.Length < 1e-6)
                y = Math.Abs(z.Y) < 0.9 ? D3.UnitY : D3.UnitX;
            y = (y - z * y.Dot(z)).Normalized();
            var x = y.Cross(z);
            var local = f * M3.FromColumns(x, y, z);
            var node = new JsonObject { ["name"] = "socket_" + socket.Name };
            SetRotation(node, local.ToQuaternion());
            SetTranslation(node, (f * (D3.From(socket.Position) - heads[socket.Bone])).ToVector3());
            nodes.Add(node);
            children[socket.Bone].Add(nodes.Count - 1);
            socketNames.Add(socket.Name);
        }

        foreach (var hit in hits)
        {
            if (hit.Bone < 0 || hit.Bone >= bones.Count)
                throw new ArgumentException($"A hit capsule is on bone {hit.Bone}, which does not exist", nameof(hits));
            // Head to tail in the joint's frame: along +Y for a bone frame; a joint of an external skeleton (whose axes are the
            // skeleton's, not the bone's) gets the turn from +Y to that direction as the node's rotation
            var along = frames[hit.Bone].Transposed() * (D3.From(bones[hit.Bone].Tail) - heads[hit.Bone]);
            var length = along.Length;
            var direction = along.Normalized();
            var node = new JsonObject { ["name"] = "hit_" + bones[hit.Bone].Name };
            if ((direction - D3.UnitY).Length < 1e-6)
                node["translation"] = new JsonArray(0f, (float)(length / 2), 0f);
            else
            {
                SetTranslation(node, (along * 0.5).ToVector3());
                SetRotation(node, TurnFromY(direction));
            }
            node["extras"] = new JsonObject { ["kind"] = "capsule", ["radius"] = hit.Radius, ["height"] = hit.Height };
            nodes.Add(node);
            children[hit.Bone].Add(nodes.Count - 1);
        }

        for (var i = 0; i < bones.Count; i++)
            if (children[i].Count > 0)
                nodes[firstJoint + i]!["children"] = new JsonArray(children[i].Select(c => (JsonNode)JsonValue.Create(c)).ToArray());
        document["scenes"]![0]!["nodes"]!.AsArray().Add(firstJoint);

        // Skin: inverse of each bone's rest world matrix, column-major
        var inverse = new float[16 * bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var world = new Aff(frames[i], heads[i]).Inverse();
            var l = world.Linear;
            float[] m = [(float)l.A, (float)l.D, (float)l.G, 0, (float)l.B, (float)l.E, (float)l.H, 0, (float)l.C, (float)l.F, (float)l.I, 0,
                (float)world.Offset.X, (float)world.Offset.Y, (float)world.Offset.Z, 1];
            m.CopyTo(inverse, 16 * i);
        }
        var ibm = AddAccessor(Bytes(inverse), null, Float, bones.Count, "MAT4");
        document["skins"] = new JsonArray(new JsonObject
        {
            ["name"] = name,
            ["joints"] = new JsonArray(Enumerable.Range(firstJoint, bones.Count).Select(n => (JsonNode)JsonValue.Create(n)).ToArray()),
            ["inverseBindMatrices"] = ibm,
            ["skeleton"] = firstJoint,
        });

        // Animations: node-local values
        var animations = new JsonArray();
        foreach (var clip in clips)
        {
            var samplers = new JsonArray();
            var channels = new JsonArray();
            foreach (var track in clip.Tracks)
            {
                var bone = skeleton.IndexOf(track.Bone);
                if (bone < 0)
                    throw new ArgumentException($"Clip {clip.Name} has a track for {track.Bone}, which is not a bone", nameof(clips));
                var times = track.Times;
                if (times.Length == 0)
                    continue;
                int Time() => AddAccessor(Bytes(times), null, Float, times.Length, "SCALAR", [times.Min()], [times.Max()]);

                void Channel(int output, string path)
                {
                    samplers.Add(new JsonObject { ["input"] = Time(), ["interpolation"] = "LINEAR", ["output"] = output });
                    channels.Add(new JsonObject
                    {
                        ["sampler"] = samplers.Count - 1,
                        ["target"] = new JsonObject { ["node"] = firstJoint + bone, ["path"] = path },
                    });
                }

                if (track.Rotations is { } rotations)
                {
                    if (rotations.Length != times.Length)
                        throw new ArgumentException($"Clip {clip.Name}, bone {track.Bone}: {rotations.Length} rotations for {times.Length} times", nameof(clips));
                    var values = new float[4 * rotations.Length];
                    var previous = Quaternion.Identity;
                    for (var k = 0; k < rotations.Length; k++)
                    {
                        var q = (restRotation[bone] * M3.FromQuaternion(rotations[k])).ToQuaternion();
                        // Same hemisphere as the previous key, so LINEAR takes the short way
                        if (k > 0 && Quaternion.Dot(q, previous) < 0)
                            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
                        previous = q;
                        values[4 * k] = q.X; values[4 * k + 1] = q.Y; values[4 * k + 2] = q.Z; values[4 * k + 3] = q.W;
                    }
                    Channel(AddAccessor(Bytes(values), null, Float, rotations.Length, "VEC4"), "rotation");
                }
                if (track.Translations is { } translations)
                {
                    if (translations.Length != times.Length)
                        throw new ArgumentException($"Clip {clip.Name}, bone {track.Bone}: {translations.Length} translations for {times.Length} times", nameof(clips));
                    var values = new float[3 * translations.Length];
                    for (var k = 0; k < translations.Length; k++)
                    {
                        var t = (restTranslation[bone] + restRotation[bone] * D3.From(translations[k])).ToVector3();
                        values[3 * k] = t.X; values[3 * k + 1] = t.Y; values[3 * k + 2] = t.Z;
                    }
                    Channel(AddAccessor(Bytes(values), null, Float, translations.Length, "VEC3"), "translation");
                }
            }
            var extras = new JsonObject { ["loop"] = clip.Loop, ["length"] = clip.Length };
            if (clip.LayersJson is { } layers)
                extras["layers"] = JsonNode.Parse(layers);
            animations.Add(new JsonObject { ["name"] = clip.Name, ["samplers"] = samplers, ["channels"] = channels, ["extras"] = extras });
        }
        if (animations.Count > 0)
            document["animations"] = animations;

        var rootExtras = document["extras"] as JsonObject ?? (JsonObject)(document["extras"] = new JsonObject());
        rootExtras["rig"] = new JsonObject
        {
            ["bones"] = bones.Count,
            ["sockets"] = new JsonArray(socketNames.Select(s => (JsonNode)JsonValue.Create(s)).ToArray()),
            ["clips"] = new JsonArray(clips.Select(c => (JsonNode)JsonValue.Create(c.Name)).ToArray()),
        };
        if (skeleton.External is { } external)
            rootExtras["rig"]!["skeleton"] = external;

        while (buffer.Length % 4 != 0)
            buffer.WriteByte(0);
        document["buffers"]![0]!["byteLength"] = (int)buffer.Length;
        return GlbWriter.Container(Encoding.UTF8.GetBytes(document.ToJsonString()), buffer.ToArray());
    }

    private static JsonObject? JointExtras(RigBone b)
    {
        var extras = new JsonObject { ["bone"] = true };
        extras["falloff"] = b.Falloff;
        if (b.SpinAxis is { } spin)
            extras["spin"] = Vec(spin);
        if (b.Hinge is { } hinge)
            extras["hinge"] = Vec(hinge);
        if (b.Limit is { } limit)
            extras["limit"] = new JsonArray(limit.Min, limit.Max);
        switch (b.Hit)
        {
            case HitMode.Auto: extras["hit"] = "auto"; break;
            case HitMode.None: extras["hit"] = "none"; break;
            case HitMode.Radius: extras["hit"] = b.HitRadius; break;
        }
        return extras;
    }

    private static JsonArray Vec(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The shortest rotation taking +Y to the unit vector <paramref name="d"/>.</summary>
    private static Quaternion TurnFromY(D3 d)
    {
        var axis = D3.UnitY.Cross(d);
        var cos = Math.Clamp(D3.UnitY.Dot(d), -1, 1);
        if (axis.Length < 1e-9)
            return cos > 0 ? Quaternion.Identity : new Quaternion(1, 0, 0, 0);
        return M3.Rotation(axis, Math.Acos(cos)).ToQuaternion();
    }

    private static void SetRotation(JsonObject node, Quaternion q)
    {
        if (q.X != 0 || q.Y != 0 || q.Z != 0 || q.W != 1)
            node["rotation"] = new JsonArray(q.X, q.Y, q.Z, q.W);
    }

    private static void SetTranslation(JsonObject node, Vector3 t)
    {
        if (t != Vector3.Zero)
            node["translation"] = new JsonArray(t.X, t.Y, t.Z);
    }

    private static byte[] Bytes(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 * i), values[i]);
        return bytes;
    }

    private static byte[] Bytes(ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), values[i]);
        return bytes;
    }

    /// <summary>The JSON document and the binary chunk of a GLB.</summary>
    private static (JsonObject Document, byte[] Binary) Parse(byte[] glb)
    {
        var span = glb.AsSpan();
        if (BinaryPrimitives.ReadUInt32LittleEndian(span) != 0x46546C67 || BinaryPrimitives.ReadUInt32LittleEndian(span[8..]) != glb.Length)
            throw new InvalidDataException("Not a GLB");
        JsonObject? document = null;
        byte[] binary = [];
        for (var at = 12; at + 8 <= glb.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[at..]);
            var kind = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 4)..]);
            var body = span.Slice(at + 8, length);
            if (kind == JsonChunk)
                document = JsonNode.Parse(Encoding.UTF8.GetString(body))!.AsObject();
            else if (kind == 0x004E4942)
                binary = body.ToArray();
            at += 8 + length;
        }
        return (document ?? throw new InvalidDataException("The GLB has no JSON chunk"), binary);
    }
}
