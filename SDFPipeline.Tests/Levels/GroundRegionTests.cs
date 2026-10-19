using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using SDFPipeline.Levels;
using SDFPipeline.ProgramFiles;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Tests.Levels;

public class GroundRegionTests
{
    private static readonly LevelDefinition Small = new(
        new PlayArea(new Vector2(20, 10), 0.2f, 14), 3, new HillRing(12, 6, 1.5f, 10), new IslandEdge(4));

    private static readonly ModelFootprint Castle = new(new Vector2(-3, -2), new Vector2(3, 2));

    [Fact]
    public void Shapes_AreSignedDistances_NegativeInside()
    {
        var circle = new CircleRegion(new Vector2(2, 1), 3, 0, 5);
        Assert.Equal(-3, circle.Distance(new Vector2(2, 1)), 5);
        Assert.Equal(2, circle.Distance(new Vector2(7, 1)), 5);

        // A 4 × 2 box turned 90°: 2 wide and 4 long
        var box = new BoxRegion(Vector2.Zero, new Vector2(4, 2), 90, 0, 0, 5);
        Assert.Equal(-1, box.Distance(Vector2.Zero), 4);
        Assert.Equal(0, box.Distance(new Vector2(1, 0)), 4);
        Assert.Equal(0, box.Distance(new Vector2(0, 2)), 4);
        Assert.Equal(1, box.Distance(new Vector2(0, 3)), 4);

        // Rounding keeps the box's size but cuts its corners
        var rounded = new BoxRegion(Vector2.Zero, new Vector2(4, 4), 0, 1, 0, 5);
        Assert.Equal(0, rounded.Distance(new Vector2(2, 0)), 4);
        Assert.Equal(MathF.Sqrt(2) - 1, rounded.Distance(new Vector2(2, 2)), 4);

        var path = new PolylineRegion([new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10)], 2, 0, 5);
        Assert.Equal(-1, path.Distance(new Vector2(5, 0)), 5);
        Assert.Equal(2, path.Distance(new Vector2(5, 3)), 5);
        Assert.Equal(0, path.Distance(new Vector2(11, 5)), 5);
    }

    [Fact]
    public void Placement2D_TurnsCounterClockwiseSeenFromAbove()
    {
        // A model's front, +z, turned 90° faces +x
        var front = Placement2D.ToLevel(new Vector2(0, 1), 90);
        Assert.Equal(1, front.X, 5);
        Assert.Equal(0, front.Y, 5);
        Assert.Equal(new Vector2(0.3f, -2), Placement2D.ToLocal(Placement2D.ToLevel(new Vector2(0.3f, -2), 37), 37));
    }

    [Fact]
    public void Regions_LaterOnesWin_AndPlacementsPaintTheirFootprint()
    {
        var level = Small with
        {
            Regions =
            [
                new GroundRegion("dirt", [new CircleRegion(Vector2.Zero, 6, 0, 5)], GroundPattern.Plain, 0.6f, 0.05f),
                new GroundRegion("path", [new PolylineRegion([new Vector2(-10, 0), new Vector2(10, 0)], 2, 0, 5)], GroundPattern.Plain, 0.6f, 0.05f),
            ],
            Placements = [new Placement("castle", new Vector2(5, 0), 90, 1, 0, 6, "tiles", 1, 1)],
        };

        var regions = new GroundRegions(level, _ => Castle);

        Assert.Equal(["dirt", "path", "tiles"], regions.Regions.Select(r => r.Name));
        Assert.Equal(GroundPattern.Stones, regions.Regions[2].Pattern);
        Assert.Null(regions.At(new Vector2(-8, 4)));
        Assert.Equal("dirt", regions.At(new Vector2(-3, 3))?.Name);
        Assert.Equal("path", regions.At(new Vector2(-3, 0))?.Name);
        // The castle's 6 × 4 footprint turned 90° is 4 wide along x and 6 long along z, grown by the 1 m margin
        Assert.Equal("tiles", regions.At(new Vector2(5, 0))?.Name);
        Assert.Equal(0, regions.Distance(2, new Vector2(5 + 3, 0)), 4);
        Assert.Equal(0, regions.Distance(2, new Vector2(5, 4)), 4);
    }

    [Fact]
    public void Regions_APlacementCannotAddAFifth()
    {
        var full = Enumerable.Range(0, GroundRegions.MaxRegions)
            .Select(i => new GroundRegion($"r{i}", [new CircleRegion(Vector2.Zero, 1, 0, 5)], GroundPattern.Plain, 0.6f, 0.05f)).ToArray();
        var level = Small with { Regions = full, Placements = [new Placement("castle", Vector2.Zero, 0, 1, 0, 6, "tiles", 1, 7)] };

        var exception = Assert.Throws<SdfProgramFileException>(() => new GroundRegions(level, _ => Castle));

        Assert.Equal(7, exception.Line);
    }

    [Fact]
    public void RegionMap_StoresEachRegionsDistanceAtTheTexelCentres()
    {
        var level = Small with { Regions = [new GroundRegion("dirt", [new CircleRegion(new Vector2(1, -2), 4, 0, 5)], GroundPattern.Plain, 0.6f, 0.05f)] };
        var regions = new GroundRegions(level, _ => Castle);

        var map = RegionMap.Bake(regions, new Vector2(-10, -8), new Vector2(10, 8));

        Assert.Equal(((int)MathF.Ceiling(20 / RegionMap.TexelSize), (int)MathF.Ceiling(16 / RegionMap.TexelSize)), (map.Width, map.Height));
        var step = RegionMap.Range / 255 * 1.01f;
        for (var j = 0; j < map.Height; j += 7)
        for (var i = 0; i < map.Width; i += 7)
        {
            var centre = map.Min + (new Vector2(i, j) + new Vector2(0.5f)) * RegionMap.TexelSize;
            var expected = Math.Clamp(regions.Distance(0, centre), -RegionMap.Range, RegionMap.Range);
            Assert.InRange(map.At(0, i, j), expected - step, expected + step);
            // The channels without a region are far outside
            Assert.Equal(RegionMap.Range, map.At(1, i, j));
        }
    }

    [Fact]
    public void Png_HoldsTheImageRowsFromTheTop()
    {
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i * 11);

        var png = Png.Rgba(3, 2, pixels);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Take(4));
        Assert.Equal(Unfiltered(png, 3, 2), pixels);
    }

    /// <summary>The pixels of a PNG this writer wrote: its one IDAT inflated, each row's Sub filter undone.</summary>
    private static byte[] Unfiltered(byte[] png, int width, int height)
    {
        var at = 8;
        byte[] data = [];
        while (at < png.Length)
        {
            var length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
            if (System.Text.Encoding.ASCII.GetString(png, at + 4, 4) == "IDAT")
                data = png[(at + 8)..(at + 8 + length)];
            at += 12 + length;
        }
        var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress))
            zlib.CopyTo(inflated);
        var raw = inflated.ToArray();
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(1, raw[y * (1 + width * 4)]);
            for (var x = 0; x < width * 4; x++)
                pixels[y * width * 4 + x] = (byte)(raw[y * (1 + width * 4) + 1 + x] + (x >= 4 ? pixels[y * width * 4 + x - 4] : 0));
        }
        return pixels;
    }

    [Fact]
    public void Flatten_LevelsTheGroundRoundAPlacementToItsBaseHeight()
    {
        var at = new Vector2(8, 3);
        var natural = new LevelTerrain(Small);
        var terrain = new LevelTerrain(Small with { Placements = [new Placement("castle", at, 0, 1, 5, 3, null, 1, 1)] });
        var level = natural.Height(at.X, at.Y);

        foreach (var offset in new[] { Vector2.Zero, new Vector2(4.9f, 0), new Vector2(-3, 3.5f) })
            Assert.Equal(level, terrain.Height(at.X + offset.X, at.Y + offset.Y), 5);
        // Beyond the falloff the ground is its own again
        foreach (var offset in new[] { new Vector2(8.1f, 0), new Vector2(0, -9) })
            Assert.Equal(natural.Height(at.X + offset.X, at.Y + offset.Y), terrain.Height(at.X + offset.X, at.Y + offset.Y));
        // Smooth in between: no step anywhere along a line through the falloff, nothing steeper than about 60°
        for (var d = 4.9f; d < 8.1f; d += 0.05f)
            Assert.InRange(MathF.Abs(terrain.Height(at.X + d + 0.05f, at.Y) - terrain.Height(at.X + d, at.Y)), 0, 0.09f);
    }

    [Fact]
    public void Bake_WritesTheRegionMapPlacementsAndTheirModels_ByteIdenticalEveryTime()
    {
        var root = Directory.CreateTempSubdirectory("sdf-level-");
        try
        {
            var level = Small with
            {
                Regions = [new GroundRegion("dirt", [new CircleRegion(Vector2.Zero, 6, 1, 5)], GroundPattern.Plain, 0.6f, 0.05f)],
                Placements =
                [
                    new Placement("castle", new Vector2(-4, 1), 90, 1, 5, 3, "tiles", 1, 1),
                    new Placement("grass-rock", new Vector2(6, -2), -30, 1.5f, 0, 3, null, 1, 2),
                    new Placement("castle", new Vector2(4, 1), -90, 1, 0, 3, null, 1, 3),
                ],
            };
            var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default)
                .With("ground_dirt", MaterialDefinition.Default).With("ground_tiles", MaterialDefinition.Default);
            ModelFootprint? Models(string name) => name is "castle" or "grass-rock" ? Castle : null;

            var first = LevelBaker.Bake(level, "small", materials, Path.Combine(root.FullName, "a"), Models);
            LevelBaker.Bake(level, "small", materials, Path.Combine(root.FullName, "b"), Models);

            foreach (var file in first.ChunkFiles.Append(LevelBaker.LevelJson).Append(RegionMap.FileName))
                Assert.Equal(File.ReadAllBytes(Path.Combine(root.FullName, "a", file)), File.ReadAllBytes(Path.Combine(root.FullName, "b", file)));

            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "a", LevelBaker.LevelJson)));
            var document = json.RootElement;
            Assert.Equal(["castle", "grass-rock"], document.GetProperty("models").EnumerateArray().Select(m => m.GetString()));
            var placements = document.GetProperty("placements").EnumerateArray().ToArray();
            Assert.Equal(3, placements.Length);
            var terrain = new LevelTerrain(level);
            var position = placements[1].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Assert.Equal([6, terrain.Height(6, -2), -2], position);
            Assert.Equal(-30, placements[1].GetProperty("yaw").GetSingle());
            Assert.Equal(1.5f, placements[1].GetProperty("scale").GetSingle());
            Assert.Equal(["grass", "dirt", "tiles"], document.GetProperty("materials").EnumerateObject().Select(p => p.Name));
            var channels = document.GetProperty("regions").GetProperty("channels").EnumerateArray().ToArray();
            Assert.Equal(["dirt", "tiles"], channels.Select(c => c.GetProperty("name").GetString()));
            Assert.Equal("stones", channels[1].GetProperty("pattern").GetString());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bake_NamesTheLineOfAModelThatIsNotThere()
    {
        var level = Small with { Placements = [new Placement("tower9", Vector2.Zero, 0, 1, 0, 3, null, 1, 12)] };
        var materials = MaterialLibrary.Empty.With(LevelBaker.GrassMaterial, MaterialDefinition.Default);

        var exception = Assert.Throws<SdfProgramFileException>(() => LevelBaker.Bake(level, "small", materials, Path.GetTempPath(), _ => null));

        Assert.Equal(12, exception.Line);
        Assert.Contains("There is no model 'tower9'", exception.Problem);
    }

    [Fact]
    public void LevelModels_FindsContentModelsByName_WithTheirFootprint()
    {
        var content = ModelFiles.FindContentFolder(AppContext.BaseDirectory)!;
        var models = LevelModels.In(Path.Combine(content, "models"));

        var castle = models("castle0");

        Assert.NotNull(castle);
        // About 31 × 21 m within its wall, its gate towards +z; the mycelium and young fly agarics outside the wall add a few metres
        Assert.InRange(castle.Size.X, 30, 38);
        Assert.InRange(castle.Size.Y, 20, 29);
        Assert.NotNull(models("amanita-scout"));
        Assert.Null(models("no-such-model"));
    }
}
