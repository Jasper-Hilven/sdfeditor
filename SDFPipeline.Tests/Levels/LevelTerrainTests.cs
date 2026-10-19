using System.Numerics;
using SDFPipeline.Levels;

namespace SDFPipeline.Tests.Levels;

public class LevelTerrainTests
{
    private static readonly LevelDefinition Meadow = new(
        new PlayArea(new Vector2(150, 100), 0.2f, 14), 1, new HillRing(28, 10, 2.5f, 18), new IslandEdge(6));

    private static IEnumerable<Vector2> Grid(float minX, float maxX, float minZ, float maxZ, float step)
    {
        for (var z = minZ; z <= maxZ; z += step)
            for (var x = minX; x <= maxX; x += step)
                yield return new Vector2(x, z);
    }

    [Fact]
    public void Height_IsFlatButForGentleNoiseInsideThePlayArea()
    {
        var terrain = new LevelTerrain(Meadow);

        foreach (var p in Grid(-75, 75, -50, 50, 1.3f))
        {
            Assert.InRange(terrain.Height(p.X, p.Y), -PlayArea.MaxNoiseAmplitude, PlayArea.MaxNoiseAmplitude);
            // Walkable everywhere: well under 10°
            Assert.InRange(Vector3.Dot(terrain.Normal(p.X, p.Y), Vector3.UnitY), MathF.Cos(10 * MathF.PI / 180), 1);
        }
    }

    [Fact]
    public void Height_WithoutNoise_IsZeroInsideAndRisesSmoothlyToTheCrest()
    {
        var terrain = new LevelTerrain(Meadow with { Play = Meadow.Play with { NoiseAmplitude = 0 }, Hills = Meadow.Hills with { NoiseAmplitude = 0 } });

        Assert.Equal(0, terrain.Height(0, 0));
        Assert.Equal(0, terrain.Height(-74.9f, 49.9f));
        // Along +x out of the rectangle: up to the crest at 28 m out, then down the back towards the edge 6 m further
        var previous = 0f;
        for (var d = 0f; d <= 28; d += 0.5f)
        {
            var height = terrain.Height(75 + d, 0);
            Assert.True(height >= previous, $"The hills fall at {d} m out");
            previous = height;
        }
        Assert.Equal(10, terrain.Height(75 + 28, 0), 3);
        Assert.Equal(10 * (1 - LevelTerrain.BackSlopeDrop), terrain.Height(75 + 34, 0), 3);
        // The same profile out of every side, and round the corners
        Assert.Equal(terrain.Height(75 + 10, 0), terrain.Height(0, -50 - 10), 4);
        Assert.Equal(terrain.Height(75 + 10, 0), terrain.Height(75 + 6, 50 + 8), 4);
    }

    [Fact]
    public void Height_HasCrestsAbout8To12MetresHigh()
    {
        var terrain = new LevelTerrain(Meadow);

        var crest = Grid(-75, 75, 50 + 28, 50 + 28, 2).Concat(Grid(75 + 28, 75 + 28, -50, 50, 2)).Select(p => terrain.Height(p.X, p.Y)).ToArray();

        Assert.InRange(crest.Min(), 7, 10);
        Assert.InRange(crest.Max(), 10, 13);
    }

    [Fact]
    public void Height_DependsOnTheSeedOnly()
    {
        var a = new LevelTerrain(Meadow);
        var b = new LevelTerrain(Meadow);
        var other = new LevelTerrain(Meadow with { Seed = 2 });

        Assert.Equal(a.Height(90.3f, 12.7f), b.Height(90.3f, 12.7f));
        Assert.NotEqual(a.Height(90.3f, 12.7f), other.Height(90.3f, 12.7f));
    }

    [Fact]
    public void Island_EndsOnARoundedRectangleBehindTheCrest()
    {
        var terrain = new LevelTerrain(Meadow);

        Assert.Equal(34, terrain.EdgeDistance);
        Assert.Equal(new Vector2(75 + 34, 50 + 34), terrain.HalfExtent);
        Assert.True(terrain.OnIsland(75 + 33, 0));
        Assert.False(terrain.OnIsland(75 + 35, 0));
        Assert.False(terrain.OnIsland(75 + 30, 50 + 30));
        var onEdge = terrain.OntoEdge(75 + 40, 50 + 40);
        Assert.Equal(34, terrain.DistanceOutside(onEdge.X, onEdge.Y), 3);
    }

    [Fact]
    public void ValueNoise_StaysBetweenMinusOneAndOne()
    {
        var noise = new ValueNoise(1, 1);

        var samples = Grid(-20, 20, -20, 20, 0.37f).Select(p => noise.Fractal(p.X, p.Y)).ToArray();

        Assert.All(samples, s => Assert.InRange(s, -1, 1));
        Assert.True(samples.Max() - samples.Min() > 1, "The noise barely varies");
    }
}
