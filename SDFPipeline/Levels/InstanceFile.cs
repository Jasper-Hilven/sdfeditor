using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SDFPipeline.Levels;

/// <summary>
/// The scattered instances of a level as one binary file, instances.bin, for the game engine's instanced renderer (Unity's
/// InstancedScatterRenderer, which reads it and documents the same layout). Little-endian throughout; a string is a u16 byte count and
/// that many bytes of UTF-8.
/// <code>
/// "SDFI"                    magic
/// u32 version               1
/// u32 group count, then each group's name (the draw groups: foliage, trees, ...)
/// u32 prototype count, then per prototype its model's name and its draw group's index (u16)
/// f32 cell size, f32 origin x, f32 origin z, u32 cells along x, u32 cells along z
/// per cell, row by row from the lowest z (cell (i, j) is number j × cells along x + i): u32 first record, u32 records
/// u32 record count
/// the records, 32 bytes each, sorted by cell:
///     u16 prototype, u16 flags (0, none defined yet), f32 x, f32 y, f32 z (the base), f32 yaw (degrees, counter-clockwise from above),
///     f32 scale, u8 r, g, b, a (the tint, sRGB, a its strength), u32 hash (a random number of the instance's own)
/// </code>
/// Positions are the pipeline's, like level.json's: Unity mirrors x, so it negates x and the yaw. Cell (i, j) holds the instances whose
/// base is in [origin + (i, j) × size, + size] (those beyond the grid in the nearest cell).
/// </summary>
public static class InstanceFile
{
    public const string FileName = "instances.bin";
    public const int Version = 1;
    public const int RecordSize = 32;

    /// <summary>The cells the instances are sorted into: half a ground chunk, so the renderer culls them about as finely.</summary>
    public const float CellSize = 12.5f;

    public static byte[] Write(ScatteredLevel scattered, IReadOnlyList<string> groups, Vector2 origin, int cellsX, int cellsZ)
    {
        int CellOf(ScatteredInstance instance)
        {
            var i = Math.Clamp((int)MathF.Floor((instance.Position.X - origin.X) / CellSize), 0, cellsX - 1);
            var j = Math.Clamp((int)MathF.Floor((instance.Position.Z - origin.Y) / CellSize), 0, cellsZ - 1);
            return j * cellsX + i;
        }
        // A stable sort: within a cell the instances keep the order they were scattered in
        var sorted = scattered.Instances.Select((instance, index) => (Instance: instance, Cell: CellOf(instance), Index: index))
            .OrderBy(e => e.Cell).ThenBy(e => e.Index).ToArray();

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("SDFI"u8);
        writer.Write(Version);
        writer.Write(groups.Count);
        foreach (var group in groups)
            WriteString(writer, group);
        writer.Write(scattered.Prototypes.Count);
        foreach (var prototype in scattered.Prototypes)
        {
            WriteString(writer, prototype.Model);
            var group = groups.ToList().IndexOf(prototype.Group);
            writer.Write((ushort)(group >= 0 ? group : throw new InvalidOperationException($"{prototype.Model} is in group {prototype.Group}, which is not listed")));
        }
        writer.Write(CellSize);
        writer.Write(origin.X);
        writer.Write(origin.Y);
        writer.Write(cellsX);
        writer.Write(cellsZ);
        var first = 0;
        for (var cell = 0; cell < cellsX * cellsZ; cell++)
        {
            var count = 0;
            while (first + count < sorted.Length && sorted[first + count].Cell == cell)
                count++;
            writer.Write(first);
            writer.Write(count);
            first += count;
        }
        writer.Write(sorted.Length);
        foreach (var (instance, _, _) in sorted)
        {
            writer.Write((ushort)instance.Prototype);
            writer.Write((ushort)0);
            writer.Write(instance.Position.X);
            writer.Write(instance.Position.Y);
            writer.Write(instance.Position.Z);
            writer.Write(instance.Yaw);
            writer.Write(instance.Scale);
            writer.Write(instance.Tint);
            writer.Write(instance.Hash);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    /// <summary>The records of a file <see cref="Write"/> wrote, for tests and tools: each instance's prototype, base, yaw, scale and tint.</summary>
    public static IReadOnlyList<ScatteredInstance> ReadRecords(byte[] file, out int cellsX, out int cellsZ, out (int First, int Count)[] cells)
    {
        var span = file.AsSpan();
        var at = 8;
        string ReadString(ReadOnlySpan<byte> s)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(s[at..]);
            var text = Encoding.UTF8.GetString(s.Slice(at + 2, length));
            at += 2 + length;
            return text;
        }
        var groups = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
        at += 4;
        for (var g = 0; g < groups; g++)
            ReadString(span);
        var prototypes = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
        at += 4;
        for (var p = 0; p < prototypes; p++)
        {
            ReadString(span);
            at += 2;
        }
        at += 12;
        cellsX = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
        cellsZ = BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]);
        at += 8;
        cells = new (int, int)[cellsX * cellsZ];
        for (var c = 0; c < cells.Length; c++, at += 8)
            cells[c] = (BinaryPrimitives.ReadInt32LittleEndian(span[at..]), BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]));
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
        at += 4;
        var records = new ScatteredInstance[count];
        for (var r = 0; r < count; r++, at += RecordSize)
        {
            var record = span.Slice(at, RecordSize);
            records[r] = new ScatteredInstance(BinaryPrimitives.ReadUInt16LittleEndian(record),
                new Vector3(BinaryPrimitives.ReadSingleLittleEndian(record[4..]), BinaryPrimitives.ReadSingleLittleEndian(record[8..]),
                    BinaryPrimitives.ReadSingleLittleEndian(record[12..])),
                BinaryPrimitives.ReadSingleLittleEndian(record[16..]), BinaryPrimitives.ReadSingleLittleEndian(record[20..]),
                BinaryPrimitives.ReadUInt32LittleEndian(record[24..]), BinaryPrimitives.ReadUInt32LittleEndian(record[28..]));
        }
        return records;
    }
}
