using System;
using System.Collections.Generic;
using System.Text;

namespace SdfRig.Editor
{
    /// <summary>A GLB read just far enough for rigged models: the JSON document, the binary chunk, and accessors read as floats or ints.</summary>
    public sealed class GlbFile
    {
        // 07.1: switch to the shared importer's packed vertex layout when BattleRoyale.Look.Editor lands
        // (this reader and RigBuilder's vertex packing are the stand-ins for 07's .glb reader).
        public object Json { get; private set; }
        public byte[] Bin { get; private set; } = Array.Empty<byte>();

        public List<object> Nodes => MiniJson.List(MiniJson.Get(Json, "nodes")) ?? new List<object>();
        public List<object> Meshes => MiniJson.List(MiniJson.Get(Json, "meshes")) ?? new List<object>();
        public List<object> Skins => MiniJson.List(MiniJson.Get(Json, "skins")) ?? new List<object>();
        public List<object> Animations => MiniJson.List(MiniJson.Get(Json, "animations")) ?? new List<object>();
        public List<object> Accessors => MiniJson.List(MiniJson.Get(Json, "accessors")) ?? new List<object>();
        public List<object> BufferViews => MiniJson.List(MiniJson.Get(Json, "bufferViews")) ?? new List<object>();

        public static GlbFile Parse(byte[] glb)
        {
            if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67)
                throw new FormatException("Not a GLB file");
            var file = new GlbFile();
            for (var at = 12; at + 8 <= glb.Length;)
            {
                var length = (int)BitConverter.ToUInt32(glb, at);
                var kind = BitConverter.ToUInt32(glb, at + 4);
                if (kind == 0x4E4F534A)
                    file.Json = MiniJson.Parse(Encoding.UTF8.GetString(glb, at + 8, length));
                else if (kind == 0x004E4942)
                {
                    file.Bin = new byte[length];
                    Buffer.BlockCopy(glb, at + 8, file.Bin, 0, length);
                }
                at += 8 + length;
            }
            if (file.Json == null)
                throw new FormatException("The GLB has no JSON chunk");
            return file;
        }

        public int AccessorCount(int accessor) => MiniJson.Int(MiniJson.Get(Accessors[accessor], "count"));

        /// <summary>Values as floats (normalised integer types are scaled to 0..1 / -1..1); <paramref name="components"/> per element.</summary>
        public float[] ReadFloats(int accessor, out int components)
        {
            var acc = Accessors[accessor];
            var normalized = MiniJson.Bool(MiniJson.Get(acc, "normalized"));
            var componentType = MiniJson.Int(MiniJson.Get(acc, "componentType"));
            var values = ReadDoubles(accessor, out components);
            var r = new float[values.Length];
            var scale = 1.0;
            if (normalized)
                switch (componentType)
                {
                    case 5120: scale = 1 / 127.0; break;
                    case 5121: scale = 1 / 255.0; break;
                    case 5122: scale = 1 / 32767.0; break;
                    case 5123: scale = 1 / 65535.0; break;
                }
            for (var i = 0; i < r.Length; i++)
                r[i] = (float)Math.Max(values[i] * scale, normalized && (componentType == 5120 || componentType == 5122) ? -1 : double.MinValue);
            return r;
        }

        /// <summary>Values as ints (indices, joints).</summary>
        public int[] ReadInts(int accessor, out int components)
        {
            var d = ReadDoubles(accessor, out components);
            var r = new int[d.Length];
            for (var i = 0; i < r.Length; i++)
                r[i] = (int)d[i];
            return r;
        }

        private double[] ReadDoubles(int accessor, out int components)
        {
            var acc = Accessors[accessor];
            var componentType = MiniJson.Int(MiniJson.Get(acc, "componentType"));
            var count = MiniJson.Int(MiniJson.Get(acc, "count"));
            components = ComponentCount(MiniJson.String(MiniJson.Get(acc, "type")));
            var result = new double[count * components];
            var viewIndex = MiniJson.Get(acc, "bufferView");
            if (viewIndex == null)
                return result;
            var view = BufferViews[MiniJson.Int(viewIndex)];
            var size = ComponentSize(componentType);
            var offset = MiniJson.Int(MiniJson.Get(view, "byteOffset")) + MiniJson.Int(MiniJson.Get(acc, "byteOffset"));
            var stride = MiniJson.Int(MiniJson.Get(view, "byteStride"));
            if (stride == 0)
                stride = size * components;
            for (var e = 0; e < count; e++)
                for (var c = 0; c < components; c++)
                    result[e * components + c] = Component(componentType, offset + e * stride + c * size);
            return result;
        }

        private double Component(int componentType, int at)
        {
            switch (componentType)
            {
                case 5120: return (sbyte)Bin[at];
                case 5121: return Bin[at];
                case 5122: return BitConverter.ToInt16(Bin, at);
                case 5123: return BitConverter.ToUInt16(Bin, at);
                case 5125: return BitConverter.ToUInt32(Bin, at);
                case 5126: return BitConverter.ToSingle(Bin, at);
                default: throw new FormatException($"Unknown glTF component type {componentType}");
            }
        }

        private static int ComponentSize(int componentType)
        {
            switch (componentType)
            {
                case 5120: case 5121: return 1;
                case 5122: case 5123: return 2;
                default: return 4;
            }
        }

        private static int ComponentCount(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
                default: throw new FormatException($"Unknown accessor type {type}");
            }
        }
    }
}
