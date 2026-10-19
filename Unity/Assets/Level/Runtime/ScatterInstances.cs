using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SdfLevel
{
    /// <summary>
    /// The scattered instances of a level, as the SDF pipeline bakes them (sdf level, SDFPipeline/Levels/InstanceFile.cs) into
    /// instances.bin; <see cref="InstancedScatterRenderer"/> draws them. Little-endian throughout; a string is a u16 byte count and that
    /// many bytes of UTF-8:
    /// <code>
    /// "SDFI"                    magic
    /// u32 version               1
    /// u32 group count, then each group's name (the draw groups: foliage, trees, ...)
    /// u32 prototype count, then per prototype its model's name and its draw group's index (u16)
    /// f32 cell size, f32 origin x, f32 origin z, u32 cells along x, u32 cells along z
    /// per cell, row by row from the lowest z (cell (i, j) is number j × cells along x + i): u32 first record, u32 records
    /// u32 record count
    /// the records, 32 bytes each, sorted by cell:
    ///     u16 prototype, u16 flags (0, none defined yet), f32 x, f32 y, f32 z (the base), f32 yaw (degrees, counter-clockwise from
    ///     above), f32 scale, u8 r, g, b, a (the tint: the ground's colour under it, sRGB, a its strength), u32 hash (a random number
    ///     of the instance's own, which the low preset thins by)
    /// </code>
    /// Positions, yaws and the cell grid are the pipeline's, like level.json's; Unity mirrors x, so <see cref="Position"/> and
    /// <see cref="Rotation"/> negate x and the yaw.
    /// </summary>
    public sealed class ScatterInstances
    {
        public const int Version = 1;
        public const int RecordSize = 32;

        public struct Record
        {
            public int Prototype;
            public int Flags;
            /// <summary>The base, in the pipeline's coordinates.</summary>
            public Vector3 PipelinePosition;
            /// <summary>Degrees, counter-clockwise seen from above, in the pipeline's coordinates.</summary>
            public float Yaw;
            public float Scale;
            public Color32 Tint;
            public uint Hash;

            /// <summary>The base in Unity's coordinates.</summary>
            public Vector3 Position => new Vector3(-PipelinePosition.x, PipelinePosition.y, PipelinePosition.z);

            public Quaternion Rotation => Quaternion.Euler(0, -Yaw, 0);

            /// <summary>The hash as a share from 0 up to 1: kept at a density d when below d.</summary>
            public float Share => Hash / 4294967296f;
        }

        public string[] Groups;
        public string[] Prototypes;
        public int[] PrototypeGroups;
        public float CellSize;
        /// <summary>The grid's lowest corner, pipeline x and z.</summary>
        public Vector2 Origin;
        public int CellsX;
        public int CellsZ;
        public int[] CellFirst;
        public int[] CellCount;
        public Record[] Records;

        /// <exception cref="InvalidDataException">The bytes are not an instances.bin this code can read.</exception>
        public static ScatterInstances Read(byte[] bytes)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            if (bytes.Length < 8 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "SDFI")
                throw new InvalidDataException("This is not an instances.bin of the SDF pipeline");
            var version = reader.ReadInt32();
            if (version != Version)
                throw new InvalidDataException($"instances.bin has version {version}, but only {Version} can be read; bake the level again");

            string ReadString() => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
            var instances = new ScatterInstances { Groups = new string[reader.ReadInt32()] };
            for (var g = 0; g < instances.Groups.Length; g++)
                instances.Groups[g] = ReadString();
            var prototypes = reader.ReadInt32();
            instances.Prototypes = new string[prototypes];
            instances.PrototypeGroups = new int[prototypes];
            for (var p = 0; p < prototypes; p++)
            {
                instances.Prototypes[p] = ReadString();
                instances.PrototypeGroups[p] = reader.ReadUInt16();
            }
            instances.CellSize = reader.ReadSingle();
            instances.Origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            instances.CellsX = reader.ReadInt32();
            instances.CellsZ = reader.ReadInt32();
            var cells = instances.CellsX * instances.CellsZ;
            instances.CellFirst = new int[cells];
            instances.CellCount = new int[cells];
            for (var c = 0; c < cells; c++)
            {
                instances.CellFirst[c] = reader.ReadInt32();
                instances.CellCount[c] = reader.ReadInt32();
            }
            var count = reader.ReadInt32();
            if (reader.BaseStream.Length - reader.BaseStream.Position != (long)count * RecordSize)
                throw new InvalidDataException($"instances.bin should hold {count} records of {RecordSize} bytes after its header; bake the level again");
            instances.Records = new Record[count];
            for (var r = 0; r < count; r++)
            {
                instances.Records[r] = new Record
                {
                    Prototype = reader.ReadUInt16(),
                    Flags = reader.ReadUInt16(),
                    PipelinePosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    Yaw = reader.ReadSingle(),
                    Scale = reader.ReadSingle(),
                    Tint = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
                    Hash = reader.ReadUInt32(),
                };
                if (instances.Records[r].Prototype >= prototypes)
                    throw new InvalidDataException($"Record {r} of instances.bin names prototype {instances.Records[r].Prototype} of {prototypes}");
            }
            return instances;
        }

        /// <summary>The cell a point (Unity's coordinates) is in, clamped to the grid.</summary>
        public int CellOf(Vector3 position)
        {
            var i = Mathf.Clamp(Mathf.FloorToInt((-position.x - Origin.x) / CellSize), 0, CellsX - 1);
            var j = Mathf.Clamp(Mathf.FloorToInt((position.z - Origin.y) / CellSize), 0, CellsZ - 1);
            return j * CellsX + i;
        }
    }
}
