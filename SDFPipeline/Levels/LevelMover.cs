using System.Numerics;

namespace SDFPipeline.Levels;

/// <summary>
/// A vehicle that runs a closed path all round long on a fixed timetable (ticket 27.3): the level file's <c>mover</c> section. Its
/// position is a function of the round clock alone (<c>distance = speed * (clock + phase)</c>), so a respawn or a new round saves nothing.
/// <paramref name="Path"/> is the path's corners (x, z) in metres, closed by a last leg back to the first; <paramref name="Heights"/> are
/// (arc length, y) keys, the height of the cars' bottoms above y = 0 along it, linear between keys and round the loop; the
/// <paramref name="Cars"/> follow the path's centre line, the first ahead, <paramref name="Gap"/> metres apart.
/// </summary>
/// <param name="Phase">Seconds the timetable is ahead at clock 0.</param>
/// <param name="Line">The line of the level file it is on, for errors found while baking.</param>
public sealed record MoverSpec(string Name, float Speed, float Phase, float Gap, IReadOnlyList<Vector2> Path, IReadOnlyList<Vector2> Heights,
    IReadOnlyList<MoverCar> Cars, int Line)
{
    /// <summary>Metres a scout or tank in a car's way is pushed off its side (ticket 27.10; 0: not pushed).</summary>
    public float Pushes { get; init; }

    /// <summary>Damage of one hit by a car (0: none). A kill shows in the kill feed as <see cref="HazardName"/>, with no weapon, credit or spores.</summary>
    public float Damage { get; init; }

    /// <summary>What the kill feed calls the mover; the mover's own name when the file gives none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Seconds before one car may hit the same victim again.</summary>
    public float Cooldown { get; init; } = 1;

    /// <summary>Seconds between puffs from the first car (0: none).</summary>
    public float Puff { get; init; }

    /// <summary>Where the puff leaves the first car, in the car's own frame (x right, y up, z forward).</summary>
    public Vector3 PuffAt { get; init; } = new(0, 2.5f, 0);

    /// <summary>Seconds before each climb of the path (a bridge) the first car gives a whistle puff (0: none).</summary>
    public float Whistle { get; init; }

    /// <summary>Metres from the train's head to a crossing within which a bot waits at the track's edge (0: bots do not wait).</summary>
    public float Crossing { get; init; }

    /// <summary>The name the kill feed writes for a kill by this mover.</summary>
    public string HazardName => DisplayName ?? (Name.Length == 0 ? Name : char.ToUpperInvariant(Name[0]) + Name[1..]);

    /// <summary>The path's length in metres, all the way round.</summary>
    public float Length => PathLength(Path);

    /// <summary>The cars' lengths and the gaps between them: the room the whole train takes on the path.</summary>
    public float TrainLength => Cars.Sum(c => c.Length) + Gap * (Cars.Count - 1);

    /// <summary>A lap in seconds.</summary>
    public float LapSeconds => Length / Speed;

    public static float PathLength(IReadOnlyList<Vector2> path)
    {
        var length = 0f;
        for (var i = 0; i < path.Count; i++)
            length += Vector2.Distance(path[i], path[(i + 1) % path.Count]);
        return length;
    }

    /// <summary>
    /// The path of a stadium: two straights parallel to z at x = centre.X ± <paramref name="radius"/>, each from
    /// centre.Y − <paramref name="halfLength"/> to centre.Y + halfLength, joined by two semicircles of that radius about
    /// (centre.X, centre.Y ± halfLength). Anticlockwise seen from above; it starts at the east straight's south end
    /// (centre.X + radius, centre.Y − halfLength), heading north (+z). The arcs are corners every 5 degrees.
    /// </summary>
    public static IReadOnlyList<Vector2> Stadium(Vector2 center, float halfLength, float radius)
    {
        const int steps = 36;
        var points = new List<Vector2> { new(center.X + radius, center.Y - halfLength) };
        // The north end, from the east straight round to the west one
        for (var i = 0; i <= steps; i++)
        {
            var angle = MathF.PI * i / steps;
            points.Add(new Vector2(center.X + radius * MathF.Cos(angle), center.Y + halfLength + radius * MathF.Sin(angle)));
        }
        // The south end, from the west straight back round to the east one
        for (var i = 0; i < steps; i++)
        {
            var angle = MathF.PI + MathF.PI * i / steps;
            points.Add(new Vector2(center.X + radius * MathF.Cos(angle), center.Y - halfLength + radius * MathF.Sin(angle)));
        }
        return Dedup(points);
    }

    /// <summary>The corners without one on top of the one before it (or, at the end, on the first).</summary>
    private static IReadOnlyList<Vector2> Dedup(List<Vector2> points)
    {
        var result = new List<Vector2>();
        foreach (var point in points)
            if (result.Count == 0 || Vector2.Distance(result[^1], point) > 1e-3f)
                result.Add(point);
        while (result.Count > 1 && Vector2.Distance(result[^1], result[0]) <= 1e-3f)
            result.RemoveAt(result.Count - 1);
        return result;
    }
}

/// <summary>A car of a <see cref="MoverSpec"/>: a model from content/models/ and its length along the path in metres.</summary>
public sealed record MoverCar(string Model, float Length, int Line);
