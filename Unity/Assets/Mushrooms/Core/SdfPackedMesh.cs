using System;
using System.Buffers.Binary;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms
{
    /// <summary>
    /// The compact vertex layout of a meshed SDF model, to keep the build small (ticket 37): what <c>MushroomBuilder.Fill</c> writes and
    /// <c>SdfPacked.hlsl</c> unpacks.
    /// <code>
    /// Position  4 x SNorm16   xyz = position over the mesh's scale, w = scale / 128 (the scale is the largest coordinate, rounded up)
    /// Normal    4 x SNorm8    xyz (w unused)
    /// Color     4 x UNorm8    base colour, wetness
    /// UV0       4 x UNorm8    roughness, specular, subsurface radius, occlusion
    /// UV1       4 x SNorm16   the position in the shape's own coordinates over its own scale, w = scale / 128
    /// UV2       4 x Float16   detail
    /// UV3       4 x Float16   variation colour and scale
    /// UV4       4 x Float16   speckle colour and scale
    /// UV5       4 x UNorm8    speckle density, size, emission
    /// UV6       4 x UNorm8    edge colour, amount
    /// UV7       4 x Float16   pattern (only on a mesh with a patterned material)
    /// </code>
    /// 60 bytes a vertex, 68 with the pattern, against 144 to 160 as floats. The position is uniform over the mesh: its error is the
    /// scale over 32767 whatever the distance from the origin. A mesh with a coordinate beyond <see cref="MaxScale"/> keeps float
    /// positions and UV1 (w = 1 in the shader, which then reads them as they are).
    /// <para>
    /// Unity hands <c>Mesh.vertices</c> and the like back <i>normalised</i> for such a mesh: use <see cref="Positions"/> to read the
    /// positions, and a separate float mesh for a collider.
    /// </para>
    /// </summary>
    public static class SdfPackedMesh
    {
        /// <summary>The scale is a multiple of 1/32767 of this, so it fits the w of an SNorm16.</summary>
        public const float Range = 128;

        public const int Steps = 32767;

        /// <summary>The largest coordinate a packed mesh holds; the shader takes a w of 0.999 or more for an unpacked mesh.</summary>
        public const float MaxScale = 127;

        /// <summary>The scale code and scale for a mesh whose largest coordinate is <paramref name="largest"/>, or false if it is too large.</summary>
        public static bool TryScale(float largest, out short code, out float scale)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt(largest / Range * Steps));
            while (Scale((short)Mathf.Min(steps, short.MaxValue)) < largest && steps < short.MaxValue)
                steps++;
            code = (short)steps;
            scale = Scale(code);
            return scale >= largest && scale <= MaxScale;
        }

        /// <summary>The scale a code stands for, as the shader works it out: w * 128.</summary>
        public static float Scale(short code) => code / (float)Steps * Range;

        public static short Quantise(float value, float scale) =>
            (short)Mathf.Clamp(Mathf.RoundToInt(value / scale * Steps), -Steps, Steps);

        public static float Dequantise(short quantised, float scale) => quantised / (float)Steps * scale;

        public static bool IsPacked(Mesh mesh) =>
            mesh.HasVertexAttribute(VertexAttribute.Position) && mesh.GetVertexAttributeFormat(VertexAttribute.Position) == VertexAttributeFormat.SNorm16;

        /// <summary>The mesh's positions in its own space, whichever way it stores them (it must be readable).</summary>
        public static Vector3[] Positions(Mesh mesh)
        {
            if (!IsPacked(mesh))
                return mesh.vertices;
            var stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            var stride = mesh.GetVertexBufferStride(stream);
            var offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            var access = Mesh.AcquireReadOnlyMeshData(mesh);
            var data = access[0].GetVertexData<byte>(stream);
            var positions = new Vector3[mesh.vertexCount];
            for (var v = 0; v < positions.Length; v++)
            {
                var at = v * stride + offset;
                short Short(int i) => (short)(data[at + i] | (data[at + i + 1] << 8));
                var scale = Scale(Short(6));
                positions[v] = new Vector3(Dequantise(Short(0), scale), Dequantise(Short(2), scale), Dequantise(Short(4), scale));
            }
            access.Dispose();
            return positions;
        }

        /// <summary>The mesh's vertex bytes of a stream, copied (it must be readable).</summary>
        public static byte[] VertexBytes(Mesh mesh, int stream = 0)
        {
            using var access = Mesh.AcquireReadOnlyMeshData(mesh);
            return access[0].GetVertexData<byte>(stream).ToArray();
        }

        // ---------------------------------------------------------------- Writing

        public static void WriteSNorm16(Span<byte> at, float value) =>
            BinaryPrimitives.WriteInt16LittleEndian(at, (short)Mathf.Clamp(Mathf.RoundToInt(value * Steps), -Steps, Steps));

        public static void WriteInt16(Span<byte> at, short value) => BinaryPrimitives.WriteInt16LittleEndian(at, value);

        public static void WriteFloat(Span<byte> at, float value) =>
            BinaryPrimitives.WriteInt32LittleEndian(at, BitConverter.SingleToInt32Bits(value));

        public static void WriteSNorm8(Span<byte> at, float value) =>
            at[0] = unchecked((byte)(sbyte)Mathf.Clamp(Mathf.RoundToInt(value * 127), -127, 127));

        public static void WriteUNorm8(Span<byte> at, float value) => at[0] = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255), 0, 255);

        public static void WriteHalf(Span<byte> at, float value) => BinaryPrimitives.WriteUInt16LittleEndian(at, ToHalf(value));

        /// <summary>The IEEE half float nearest the value (ties to even); beyond its range, infinity.</summary>
        public static ushort ToHalf(float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            var sign = (bits >> 16) & 0x8000;
            var exponent = ((bits >> 23) & 0xff) - 127 + 15;
            var mantissa = bits & 0x7fffff;
            if (((bits >> 23) & 0xff) == 0xff)
                return (ushort)(sign | 0x7c00 | (mantissa != 0 ? 0x200 : 0));
            if (exponent >= 31)
                return (ushort)(sign | 0x7c00);
            if (exponent <= 0)
            {
                if (exponent < -10)
                    return (ushort)sign;
                mantissa |= 0x800000;
                var shift = 14 - exponent;
                var half = mantissa >> shift;
                var rest = mantissa & ((1 << shift) - 1);
                if (rest > (1 << (shift - 1)) || (rest == (1 << (shift - 1)) && (half & 1) != 0))
                    half++;
                return (ushort)(sign | half);
            }
            var result = sign | (exponent << 10) | (mantissa >> 13);
            var low = mantissa & 0x1fff;
            if (low > 0x1000 || (low == 0x1000 && (result & 1) != 0))
                result++;
            return (ushort)result;
        }

        public static float FromHalf(ushort half)
        {
            var sign = (half & 0x8000) << 16;
            var exponent = (half >> 10) & 0x1f;
            var mantissa = half & 0x3ff;
            if (exponent == 0)
            {
                if (mantissa == 0)
                    return BitConverter.Int32BitsToSingle(sign);
                var value = mantissa / 1024f * Mathf.Pow(2, -14);
                return sign != 0 ? -value : value;
            }
            if (exponent == 31)
                return BitConverter.Int32BitsToSingle(sign | 0x7f800000 | (mantissa << 13));
            return BitConverter.Int32BitsToSingle(sign | ((exponent - 15 + 127) << 23) | (mantissa << 13));
        }
    }
}
