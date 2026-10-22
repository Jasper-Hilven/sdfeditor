namespace SDFPipeline.Levels;

/// <summary>
/// Data pulses (ticket 30.9): bright bands that run along the region polylines flagged <c>pulse</c>, from each one's first point to its
/// last, and speed up a scout who runs with one. The level file's <c>pulses every=4 speed=18 length=3 boost=1.35 for=2.5</c> line. A pulse
/// starts at the first point every <paramref name="Every"/> seconds, runs <paramref name="Speed"/> metres per second and is
/// <paramref name="Length"/> metres long (its head in front); a scout on the line within 1.5 m of one gets <paramref name="Boost"/> times
/// the move speed for <paramref name="For"/> seconds, refreshed, not stacked. The same formula is Unity's PulseRules and the SdfPulse shader.
/// </summary>
/// <param name="Line">The line of the level file it is on, for errors found while baking.</param>
public sealed record PulseSpec(float Every, float Speed, float Length, float Boost, float For, int Line)
{
    public const float DefaultEvery = 4, DefaultSpeed = 18, DefaultLength = 3, DefaultBoost = 1.35f, DefaultFor = 2.5f;

    /// <summary>Metres between one pulse's head and the next: they are this far apart on the line.</summary>
    public float Period => Every * Speed;
}
