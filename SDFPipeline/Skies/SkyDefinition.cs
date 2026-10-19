using System.Numerics;

namespace SDFPipeline.Skies;

/// <summary>
/// A sky as a preset in content/skies/ describes it, with a level's overrides merged in (see <see cref="SkyFile"/>): the light of the
/// toon world, the banded gradient behind it, and what floats and glows in it. Directions are in pipeline coordinates, colours linear.
/// Elevations are degrees above the horizon (negative below it); azimuths are degrees from +z towards +x, the way a placement's yaw
/// turns a model's front. The lines a preset may leave out are null.
/// </summary>
public sealed record SkyDefinition(SkyLight Light, SkyTone Tone, SkyGradient Gradient, SkyPost Post)
{
    /// <summary>The abyss below the island: bands darkening downward and a faint glow deep below; null for none.</summary>
    public SkyAbyss? Abyss { get; init; }

    public SkyStars? Stars { get; init; }

    /// <summary>The fade toward the horizon colour with distance, in place of fog; null for none.</summary>
    public SkyFade? Fade { get; init; }

    public SkyMoon? Moon { get; init; }

    public SkyFloaters? Floaters { get; init; }

    public SkySpores? Spores { get; init; }

    /// <summary>The models the sky draws (moon, floaters), each once, in name order.</summary>
    public IReadOnlyList<string> Models =>
        new[] { Moon?.Model, Floaters?.Model }.OfType<string>().Distinct().Order(StringComparer.Ordinal).ToArray();
}

/// <summary>
/// The main light, from <see cref="Azimuth"/> and <see cref="Elevation"/>: the sun, or a moon, which then hangs in that direction. The
/// toon shaders take only its direction and hue.
/// </summary>
public sealed record SkyLight(float Azimuth, float Elevation, Vector3 Color)
{
    /// <summary>The unit direction towards the light.</summary>
    public Vector3 Direction => SkyDirections.Of(Azimuth, Elevation);
}

/// <summary>
/// Multipliers on the toon bands: the shaded colour times <see cref="Brightness"/>, the shadow band's tone times
/// <see cref="ShadowTint"/>; glowing edges are kept from the brightness by <see cref="GlowKeep"/> (0 to 1). (1, (1, 1, 1), 0) changes
/// nothing.
/// </summary>
public sealed record SkyTone(float Brightness, Vector3 ShadowTint, float GlowKeep)
{
    public static readonly SkyTone Identity = new(1, Vector3.One, 0);
}

/// <summary>A band of the sky's gradient: its colour from the elevation <see cref="From"/> to the next band's (up, or in the abyss down).</summary>
public sealed record SkyBand(float From, Vector3 Color);

/// <summary>The sky above the horizon: bands from the zenith down, highest first, cut with soft edges <see cref="Softness"/> degrees wide.</summary>
public sealed record SkyGradient(float Softness, IReadOnlyList<SkyBand> Bands);

/// <summary>
/// The sky below the gradient: bands, highest first, each from its elevation down to the next one's, the last down to the nadir; and a
/// glow band <see cref="SkyGlow.Width"/> degrees wide round <see cref="SkyGlow.Elevation"/>, null for none.
/// </summary>
public sealed record SkyAbyss(IReadOnlyList<SkyBand> Bands, SkyGlow? Glow);

public sealed record SkyGlow(float Elevation, float Width, Vector3 Color);

/// <param name="Density">The share of the sky's cells that hold a star, 0 to 1.</param>
/// <param name="Size">The stars' width in pixels (at 1080 lines).</param>
/// <param name="Twinkle">How much their brightness wavers, 0 to 1.</param>
/// <param name="Tint">How far the tinted ones are tinted, 0 to 1.</param>
/// <param name="MinElevation">No stars below this elevation.</param>
public sealed record SkyStars(float Density, float Size, float Twinkle, float Tint, float MinElevation);

/// <summary>Beyond <see cref="Start"/> metres a surface fades toward the sky behind it, up to <see cref="Amount"/> at <see cref="End"/>.</summary>
public sealed record SkyFade(float Start, float End, float Amount);

/// <summary>
/// A model hung in the sky at the light (<see cref="SkyLight"/>), drawn infinitely far, <see cref="Size"/> degrees across, glowing by
/// <see cref="Glow"/>. An eye: its iris wanders and it blinks.
/// </summary>
public sealed record SkyMoon(string Model, float Size, float Glow, SkyTurn Turn, SkyBlink Blink);

/// <summary>The iris wanders up to <see cref="Amount"/> degrees, by smooth noise about <see cref="Period"/> seconds a swing.</summary>
public sealed record SkyTurn(float Amount, float Period);

/// <summary>A blink every <see cref="Every"/> seconds (a random time in that range), <see cref="Duration"/> seconds long.</summary>
public sealed record SkyBlink(Vector2 Every, float Duration);

/// <summary>
/// <see cref="Count"/> copies of a model hanging in the middle distance, drawn like the moon: each at a distance and elevation in those
/// ranges, as big as the model <see cref="Scale"/> times over would look from there, drifting round at <see cref="Drift"/> metres a
/// second and bobbing <see cref="Bob"/> metres.
/// </summary>
public sealed record SkyFloaters(string Model, int Count, Vector2 Distance, Vector2 Elevation, float Scale, float Drift, float Bob);

public sealed record SkySpores(SkyAbyssSpores? Abyss, SkyNearSpores? Near);

/// <summary>
/// Glowing spores rising past the island's edge, in a ring round it, from <see cref="From"/> to <see cref="To"/> metres (heights, the
/// playable area at 0) at <see cref="Rise"/> metres a second, about <see cref="Size"/> metres across; some in <see cref="AltColor"/>.
/// </summary>
public sealed record SkyAbyssSpores(int Count, Vector3 Color, Vector3 AltColor, float Rise, float Size, float From, float To);

/// <summary>Motes drifting round the player, within <see cref="Radius"/> metres, at <see cref="Drift"/> metres a second.</summary>
public sealed record SkyNearSpores(int Count, float Radius, Vector3 Color, float Size, float Drift);

/// <summary>The post-processing: exposure in EV and bloom intensity (ACES tone mapping always).</summary>
public sealed record SkyPost(float Exposure, float Bloom);

public static class SkyDirections
{
    /// <summary>The unit direction at an azimuth (degrees from +z towards +x) and elevation (degrees up).</summary>
    public static Vector3 Of(float azimuth, float elevation)
    {
        var (a, e) = (azimuth * MathF.PI / 180, elevation * MathF.PI / 180);
        return new Vector3(MathF.Sin(a) * MathF.Cos(e), MathF.Sin(e), MathF.Cos(a) * MathF.Cos(e));
    }
}
