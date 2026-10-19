using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// The material distance texture of a level: a world-space RGBA8 image over the whole island, one channel per ground region (R the
/// first, G the second, ...; see <see cref="GroundRegions"/>), each texel the signed distance from its centre to that region's edge,
/// clamped to ±<see cref="Range"/> metres and stored as (d / Range + 1) / 2, so 0.5 is the edge, below it inside, above it outside.
/// Channels without a region hold 1: far outside.
/// <para>
/// The ground shader filters it bilinearly and draws each edge with a narrow smoothstep where the distance crosses 0: filtered distances
/// stay straight between texels, so the edges are crisp and anti-aliased from up close and far away, which per-vertex or splat weights
/// are not.
/// </para>
/// Texel (i, j) covers [<see cref="Min"/> + (i, j) × <see cref="TexelSize"/>, + <see cref="TexelSize"/>]; its value is the distance at
/// its centre. The PNG's rows run from the highest z down, so that, as image importers do, v = 0 is the lowest z:
/// u = (x - min.x) / width, v = (z - min.z) / length.
/// </summary>
public sealed record RegionMap(Vector2 Min, int Width, int Height, byte[] Pixels)
{
    /// <summary>Metres per texel: 1,283 × 989 texels over the meadow's 218 × 168 m.</summary>
    public const float TexelSize = 0.17f;

    /// <summary>The distances are clamped to this many metres either side of an edge, which is all the shader's smoothstep needs.</summary>
    public const float Range = 2;

    public const string FileName = "regions.png";

    public Vector2 Max => Min + new Vector2(Width, Height) * TexelSize;

    public static RegionMap Bake(GroundRegions regions, Vector2 min, Vector2 max)
    {
        var width = (int)MathF.Ceiling((max.X - min.X) / TexelSize - 1e-4f);
        var height = (int)MathF.Ceiling((max.Y - min.Y) / TexelSize - 1e-4f);
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);
        var count = Math.Min(regions.Regions.Count, 4);
        Parallel.For(0, height, j =>
        {
            var z = min.Y + (j + 0.5f) * TexelSize;
            var row = (height - 1 - j) * width * 4;
            for (var i = 0; i < width; i++)
            {
                var p = new Vector2(min.X + (i + 0.5f) * TexelSize, z);
                for (var c = 0; c < count; c++)
                    pixels[row + 4 * i + c] = Encode(regions.Distance(c, p));
            }
        });
        return new RegionMap(min, width, height, pixels);
    }

    public static byte Encode(float distance) => (byte)MathF.Round((Math.Clamp(distance / Range, -1, 1) + 1) / 2 * 255);

    public static float Decode(byte value) => (value / 255f * 2 - 1) * Range;

    /// <summary>The stored distance of a channel at a texel, (0, 0) being the texel at <see cref="Min"/>.</summary>
    public float At(int channel, int i, int j) => Decode(Pixels[((Height - 1 - j) * Width + i) * 4 + channel]);

    public byte[] ToPng() => Png.Rgba(Width, Height, Pixels);
}

/// <summary>A minimal PNG writer: 8-bit RGBA, each row filtered by its difference to the left (smooth images compress well that way).</summary>
public static class Png
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <param name="rgba">Rows from the top, four bytes per pixel.</param>
    public static byte[] Rgba(int width, int height, byte[] rgba)
    {
        var filtered = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 4);
            filtered[row] = 1; // Sub: each byte minus the same byte of the pixel to its left
            for (var x = 0; x < width * 4; x++)
            {
                var value = rgba[y * width * 4 + x];
                var left = x >= 4 ? rgba[y * width * 4 + x - 4] : (byte)0;
                filtered[row + 1 + x] = (byte)(value - left);
            }
        }

        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(filtered);

        var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var bytes = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
            bytes[i] = (byte)type[i];
        data.CopyTo(bytes, 4);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        png.Write(bytes);
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFFu);
        png.Write(crcBytes);
    }
}
