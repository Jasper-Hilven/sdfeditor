using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.Materials;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

public class RoomTests
{
    /// <summary>The faces in the order level.json lists them.</summary>
    private static readonly RoomFace[] Faces = [RoomFace.Floor, RoomFace.Ceiling, RoomFace.NegZ, RoomFace.PosZ, RoomFace.NegX, RoomFace.PosX];

    /// <summary>A face's corner in whole squares and its two axes, in the room's lattice (x across, y up, z along).</summary>
    private static (Vector3 Origin, Vector3 U, Vector3 V) Geometry(RoomFace face, (int X, int Y, int Z) n) => face switch
    {
        RoomFace.Floor => (Vector3.Zero, Vector3.UnitX, Vector3.UnitZ),
        RoomFace.Ceiling => (new Vector3(0, n.Y, 0), Vector3.UnitX, Vector3.UnitZ),
        RoomFace.NegZ => (Vector3.Zero, Vector3.UnitX, Vector3.UnitY),
        RoomFace.PosZ => (new Vector3(0, 0, n.Z), Vector3.UnitX, Vector3.UnitY),
        RoomFace.NegX => (Vector3.Zero, Vector3.UnitZ, Vector3.UnitY),
        _ => (new Vector3(n.X, 0, 0), Vector3.UnitZ, Vector3.UnitY),
    };

    /// <summary>Every pair of squares that share an edge of the cube's surface, with that edge's two end points.</summary>
    private static List<(RoomFace Face, (Vector3, Vector3) Edge, bool Dark)[]> Neighbours((int X, int Y, int Z) n)
    {
        var bySegment = new Dictionary<(Vector3, Vector3), List<(RoomFace Face, int U, int V)>>();
        foreach (var face in Faces)
        {
            var (countU, countV) = RoomChecker.Size(face, n);
            var (origin, u, v) = Geometry(face, n);
            for (var i = 0; i < countU; i++)
            for (var j = 0; j < countV; j++)
            {
                var corner = origin + u * i + v * j;
                foreach (var (from, to) in new[] { (corner, corner + u), (corner + v, corner + u + v), (corner, corner + v), (corner + u, corner + u + v) })
                {
                    var key = Vector3.Dot(from - to, new Vector3(1, 1024, 1048576)) < 0 ? (from, to) : (to, from);
                    if (!bySegment.TryGetValue(key, out var list))
                        bySegment[key] = list = [];
                    list.Add((face, i, j));
                }
            }
        }
        return bySegment.Select(entry => entry.Value.Select(s => (s.Face, entry.Key, RoomChecker.IsDark(s.Face, s.U, s.V, n))).ToArray()).ToList();
    }

    [Fact]
    public void IsDark_FloorCornersAreDark_AndTheFloorIsAPlainBoard()
    {
        var n = (X: 20, Y: 20, Z: 20);

        // As the level's regions have it: (i + j) even is dark, so the -x -z corner and its opposite are dark, the other two light
        foreach (var (u, v) in new[] { (0, 0), (19, 19) })
            Assert.True(RoomChecker.IsDark(RoomFace.Floor, u, v, n), $"floor corner ({u}, {v})");
        foreach (var (u, v) in new[] { (19, 0), (0, 19) })
            Assert.False(RoomChecker.IsDark(RoomFace.Floor, u, v, n), $"floor corner ({u}, {v})");
        Assert.False(RoomChecker.IsDark(RoomFace.Floor, 1, 0, n));
        Assert.False(RoomChecker.IsDark(RoomFace.Floor, 0, 1, n));
        Assert.True(RoomChecker.IsDark(RoomFace.Floor, 1, 1, n));
    }

    [Theory]
    [InlineData(20, 20, 20)]
    [InlineData(5, 4, 6)]
    [InlineData(3, 7, 2)]
    public void IsDark_AlternatesAcrossEveryEdge_ButTheFourVerticalOnes(int x, int y, int z)
    {
        var n = (X: x, Y: y, Z: z);
        var repeated = new List<(Vector3, Vector3)>();
        var pairs = 0;
        foreach (var squares in Neighbours(n))
        {
            Assert.Equal(2, squares.Length);
            pairs++;
            if (squares[0].Dark == squares[1].Dark)
                repeated.Add(squares[0].Edge);
        }

        // Every square has four neighbours: 2 * squares pairs in all
        Assert.Equal(2 * (2 * x * z + 2 * x * y + 2 * y * z), pairs);
        // Across the floor's and the ceiling's edges, and inside each face, the colours differ; the only repeats run up a corner of the room
        Assert.NotEmpty(repeated);
        Assert.All(repeated, segment =>
        {
            var (a, b) = segment;
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Z, b.Z);
            Assert.True(a.X == 0 || a.X == n.X);
            Assert.True(a.Z == 0 || a.Z == n.Z);
        });
        // Every segment up all four corners repeats: y squares high, four corners
        Assert.Equal(4 * y, repeated.Count);
    }

    [Fact]
    public void Read_TakesARoom_AndKeepsTheSquareAtEightMetres()
    {
        var level = LevelFile.Read("format 1\nplay size=(160, 160)\nroom size=(160, 160, 160)");

        Assert.Equal(new RoomSpec(new Vector3(160, 160, 160), 8), level.Room);
        Assert.Equal((20, 20, 20), level.Room!.Squares);
        Assert.Null(LevelFile.Read("format 1\nplay size=(160, 160)").Room);
    }

    [Theory]
    [InlineData("format 1\nplay size=(160, 160)\nroom size=(160, 160)", 3, "'size' of room needs a size like (160, 160, 160)")]
    [InlineData("format 1\nplay size=(160, 160)\nroom", 3, "Room is missing parameter 'size'")]
    [InlineData("format 1\nplay size=(160, 160)\nroom size=(160, 100, 160)", 3, "whole numbers of squares of 8 m")]
    [InlineData("format 1\nplay size=(160, 160)\nroom size=(80, 160, 160)", 3, "must be the play size")]
    [InlineData("format 1\nplay size=(160, 160)\nroom size=(160, 160, 160) colour=red", 3, "Room has no parameter 'colour'")]
    [InlineData("format 1\nplay size=(160, 160)\nroom size=(160, 160, 160)\nroom size=(160, 160, 160)", 4, "'room' is given twice")]
    public void Read_ExplainsWhatIsWrongWithARoom(string text, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(text));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    [Fact]
    public void Bake_ALevelWithNoHillsEdgeScatterOrSky_HoldsTheFlatFloorAndTheRoomOnly()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-room-");
        try
        {
            var level = LevelFile.Read("format 1\nplay size=(48, 32) noise=0\nroom size=(48, 24, 32)");
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);
            var baked = LevelBaker.Bake(level, "box", materials, root.FullName);

            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var json = document.RootElement;
            Assert.False(json.TryGetProperty("sky", out _));
            Assert.False(json.TryGetProperty("instances", out _));
            Assert.False(json.GetProperty("edge").TryGetProperty("underside", out _));
            Assert.False(json.GetProperty("edge").TryGetProperty("rim", out _));
            Assert.Equal(0, json.GetProperty("hills").GetProperty("width").GetSingle());
            Assert.Empty(json.GetProperty("drawGroups").EnumerateArray());
            Assert.Empty(json.GetProperty("models").EnumerateArray());
            Assert.False(File.Exists(Path.Combine(root.FullName, "instances.bin")));
            // The ground is the floor alone: no bigger than the play area
            Assert.Equal(new[] { -24f, -16f }, json.GetProperty("extent").GetProperty("min").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(new[] { 24f, 16f }, json.GetProperty("extent").GetProperty("max").EnumerateArray().Select(e => e.GetSingle()));

            var room = json.GetProperty("room");
            Assert.Equal(new[] { 6, 3, 4 }, room.GetProperty("squares").EnumerateArray().Select(e => e.GetInt32()));
            var faces = room.GetProperty("faces").EnumerateArray().ToArray();
            Assert.Equal(["floor", "ceiling", "wall-z-", "wall-z+", "wall-x-", "wall-x+"], faces.Select(f => f.GetProperty("face").GetString()));
            // The floor's rows are the checker, the corner dark
            var floor = faces[0].GetProperty("dark").EnumerateArray().Select(r => r.GetString()!).ToArray();
            Assert.Equal(["101010", "010101", "101010", "010101"], floor);
            Assert.Equal(new[] { -24f, 0f, -16f }, faces[0].GetProperty("origin").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(new[] { -24f, 24f, -16f }, faces[1].GetProperty("origin").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(new[] { 0f, -1f, 0f }, faces[1].GetProperty("normal").EnumerateArray().Select(e => e.GetSingle()));
            var n = level.Room!.Squares;
            for (var f = 0; f < faces.Length; f++)
            {
                var rows = faces[f].GetProperty("dark").EnumerateArray().Select(r => r.GetString()!).ToArray();
                for (var v = 0; v < rows.Length; v++)
                for (var u = 0; u < rows[v].Length; u++)
                    Assert.Equal(RoomChecker.IsDark(Faces[f], u, v, n), rows[v][u] == '1');
            }
            Assert.True(baked.ChunkFiles.Count > 0);
            // None of the walls-and-ceiling blocks, and the square and dark rows as ever
            foreach (var name in new[] { "walls", "ceiling", "glow", "light" })
                Assert.False(room.TryGetProperty(name, out _), name);
            Assert.Equal(8f, room.GetProperty("square").GetSingle());
            Assert.All(faces, f =>
            {
                Assert.False(f.TryGetProperty("extent", out _));
                Assert.True(f.TryGetProperty("dark", out _));
            });
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private const string ToyBox = "format 1\nplay size=(150, 100) noise=0\n";

    [Fact]
    public void Read_TakesNamedWallsAndCeiling_GlowAndLight_OfAnySize()
    {
        var level = LevelFile.Read(ToyBox + "room size=(150, 64, 100) walls=toybox-wall ceiling=toybox-lid glow=north height=3 color=(1, 0.8, 0.5)\n" +
                                   "    light=(0, 0.77, 0.64) lightColor=(1, 0.93, 0.7) ambient=(0.45, 0.4, 0.6)");

        var room = level.Room!;
        Assert.Equal(new Vector3(150, 64, 100), room.Size);
        Assert.Equal(("toybox-wall", "toybox-lid"), (room.Walls, room.Ceiling));
        Assert.False(room.IsChecker);
        Assert.Equal(new RoomGlow(RoomFace.PosZ, 3, new Vector3(1, 0.8f, 0.5f)), room.Glow);
        Assert.Equal(new RoomLighting(new Vector3(0, 0.77f, 0.64f), new Vector3(1, 0.93f, 0.7f), new Vector3(0.45f, 0.4f, 0.6f)), room.Light);
    }

    [Fact]
    public void Read_GlowTakesDefaults_AndTheLightMayBePartial()
    {
        var room = LevelFile.Read(ToyBox + "room size=(150, 64, 100) walls=a ceiling=b glow=west ambient=(0.2, 0.2, 0.3)").Room!;

        Assert.Equal(new RoomGlow(RoomFace.NegX, RoomGlow.DefaultHeight, RoomGlow.DefaultColor), room.Glow);
        Assert.Equal(new RoomLighting(null, null, new Vector3(0.2f, 0.2f, 0.3f)), room.Light);
        // A checker room can have a glow too, and a room without either has neither
        Assert.NotNull(LevelFile.Read("format 1\nplay size=(160, 160)\nroom size=(160, 160, 160) glow=south").Room!.Glow);
        Assert.Null(LevelFile.Read(ToyBox + "room size=(150, 64, 100) walls=a ceiling=b").Room!.Glow);
        Assert.Null(LevelFile.Read(ToyBox + "room size=(150, 64, 100) walls=a ceiling=b").Room!.Light);
    }

    [Theory]
    [InlineData("room size=(150, 64, 100) walls=toybox-wall", 3, "needs both walls= and ceiling=")]
    [InlineData("room size=(150, 64, 100) ceiling=toybox-lid", 3, "needs both walls= and ceiling=")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b square=8", 3, "has no checker; leave out square")]
    [InlineData("room size=(150, 64, 100)", 3, "whole numbers of squares of 8 m")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b glow=up", 3, "'glow' of room needs one of north, south, east, west")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b height=3", 3, "belong to glow=")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b color=(1, 1, 1)", 3, "belong to glow=")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b glow=north height=70", 3, "'height' of room is 70")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b glow=north color=(2, 0, 0)", 3, "each part 0 to 1")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b light=(0, 0, 0)", 3, "needs a length")]
    [InlineData("room size=(150, 64, 100) walls=a ceiling=b ambient=(0.5, 0.5)", 3, "'ambient' of room needs a colour")]
    [InlineData("room size=(150, 64, 90) walls=a ceiling=b", 3, "must be the play size")]
    public void Read_ExplainsWhatIsWrongWithARoomOfMaterials(string room, int line, string problem)
    {
        var exception = Assert.Throws<SdfProgramFileException>(() => LevelFile.Read(ToyBox + room));

        Assert.Equal(line, exception.Line);
        Assert.Contains(problem, exception.Problem);
    }

    private static MaterialLibrary Library(params string[] names) =>
        names.Aggregate(MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default),
            (library, name) => library.With(name, MaterialDefinition.Default with { baseColor = new V3(0.5f, 0.25f, 0.125f) }));

    [Fact]
    public void Bake_ARoomOfMaterials_IsNotWholeSquares_AndWritesLooksSizesGlowAndLight()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-room-");
        try
        {
            var level = LevelFile.Read(ToyBox + "room size=(150, 64, 100) walls=toybox-wall ceiling=toybox-lid glow=north height=3 color=(1, 0.8, 0.5)\n" +
                                       "    light=(0, 0.77, 0.64) ambient=(0.45, 0.4, 0.6)");
            LevelBaker.Bake(level, "box", Library("toybox-wall", "toybox-lid"), root.FullName);
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, LevelBaker.LevelJson)));
            var room = document.RootElement.GetProperty("room");

            Assert.Equal(new[] { 150f, 64f, 100f }, room.GetProperty("size").EnumerateArray().Select(e => e.GetSingle()));
            // No checker: no squares, no dark rows, the lengths in metres instead (150 and 100 are not whole numbers of 8 m)
            Assert.False(room.TryGetProperty("squares", out _));
            Assert.False(room.TryGetProperty("square", out _));
            var faces = room.GetProperty("faces").EnumerateArray().ToDictionary(f => f.GetProperty("face").GetString()!);
            Assert.Equal(["floor", "ceiling", "wall-z-", "wall-z+", "wall-x-", "wall-x+"], faces.Keys);
            Assert.All(faces.Values, f => Assert.False(f.TryGetProperty("dark", out _)));
            float[] Extent(string face) => faces[face].GetProperty("extent").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            Assert.Equal(new[] { 150f, 100f }, Extent("floor"));
            Assert.Equal(new[] { 150f, 100f }, Extent("ceiling"));
            Assert.Equal(new[] { 150f, 64f }, Extent("wall-z-"));
            Assert.Equal(new[] { 150f, 64f }, Extent("wall-z+"));
            Assert.Equal(new[] { 100f, 64f }, Extent("wall-x-"));
            Assert.Equal(new[] { 100f, 64f }, Extent("wall-x+"));
            // The materials, with their parameters for Unity
            Assert.Equal("toybox-wall", room.GetProperty("walls").GetProperty("name").GetString());
            Assert.Equal("toybox-lid", room.GetProperty("ceiling").GetProperty("name").GetString());
            Assert.Equal(0.5f, room.GetProperty("walls").GetProperty("parameters").GetProperty("baseColor")[0].GetSingle());
            // The glow on the north wall (+z)
            var glow = room.GetProperty("glow");
            Assert.Equal(("wall-z+", 3f), (glow.GetProperty("wall").GetString(), glow.GetProperty("height").GetSingle()));
            Assert.Equal(new[] { 1f, 0.8f, 0.5f }, glow.GetProperty("color").EnumerateArray().Select(e => e.GetSingle()));
            // The light: the direction and ambient, the colour left to the default
            var light = room.GetProperty("light");
            Assert.Equal(new[] { 0f, 0.77f, 0.64f }, light.GetProperty("direction").EnumerateArray().Select(e => e.GetSingle()));
            Assert.Equal(new[] { 0.45f, 0.4f, 0.6f }, light.GetProperty("ambient").EnumerateArray().Select(e => e.GetSingle()));
            Assert.False(light.TryGetProperty("color", out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_ARoomWithAMissingMaterial_SaysWhichFileIsMissing()
    {
        var level = LevelFile.Read(ToyBox + "room size=(150, 64, 100) walls=toybox-wall ceiling=toybox-lid");
        var root = Directory.CreateTempSubdirectory("sdf-level-room-");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LevelBaker.Bake(level, "box", Library("toybox-lid"), root.FullName));

            Assert.Contains("materials/room/toybox-wall.sdfmat", exception.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ContentRoomMaterials_ToyBoxWallAndLid_AreThere_WithSeams_AndNotTeamColoured()
    {
        // materials/ stands next to content/ in the repository root
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory);
        Assert.NotNull(content);
        var folder = Path.Combine(Path.GetDirectoryName(content)!, "materials");
        var library = MaterialFiles.LoadLibrary(folder);

        foreach (var name in new[] { "toybox-wall", "toybox-lid" })
        {
            Assert.True(library.Contains(name), name);
            var material = library.Resolve(name);
            Assert.True(material.edgeThickness > 0, $"{name} has dark seams");
            Assert.True(material.baseColor.x >= material.baseColor.z, $"{name} is warm, not bluish");
        }
    }
}
