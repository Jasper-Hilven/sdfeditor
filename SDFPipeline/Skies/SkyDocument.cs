using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDFPipeline.Skies;

/// <summary>
/// A resolved sky as the engines take it: the "sky" object of level.json, which Unity's level builder reads, and what the IDE's preview
/// and <c>sdf render --sky</c> hand the renderer (without a layout). Directions are in pipeline coordinates; Unity mirrors x.
/// <para>
/// Besides the sky's own lines it holds <c>bands</c>, the gradient and the abyss as one list of bands from the zenith down, each from its
/// <c>from</c> up to the band above, as the shaders take them; and with a level, where the floaters and spores are (<see cref="SkyLayout"/>).
/// </para>
/// </summary>
public static class SkyDocument
{
    /// <summary>
    /// The gradient and the abyss as one list from the zenith down, each band from its <see cref="SkyBand.From"/> up to the one above:
    /// the gradient's bands, its lowest reaching down to where the abyss begins, then the abyss's, each down to the next and the last to
    /// the nadir (-90).
    /// </summary>
    public static IReadOnlyList<SkyBand> Bands(SkyDefinition sky)
    {
        var gradient = sky.Gradient.Bands;
        var abyss = sky.Abyss?.Bands ?? [];
        var bands = new List<SkyBand>(gradient.Take(gradient.Count - 1))
        {
            gradient[^1] with { From = abyss.Count > 0 ? abyss[0].From : -90 },
        };
        for (var i = 0; i < abyss.Count; i++)
            bands.Add(abyss[i] with { From = i + 1 < abyss.Count ? abyss[i + 1].From : -90 });
        return bands;
    }

    /// <summary>The sky as JSON-ready objects; nulls are lines the sky does not have.</summary>
    /// <param name="hash">A hash of the preset's file, so the document changes when the file does; null to leave it out.</param>
    /// <param name="layout">Where the level's floaters and spores are; null to leave them out (a preview has no level).</param>
    public static object Of(SkyDefinition sky, string? hash = null, SkyLayout? layout = null)
    {
        var light = sky.Light;
        var tone = sky.Tone;
        return new
        {
            hash,
            light = new { azimuth = light.Azimuth, elevation = light.Elevation, direction = Array(light.Direction), color = Array(light.Color) },
            tone = new { brightness = tone.Brightness, shadowTint = Array(tone.ShadowTint), glowKeep = tone.GlowKeep },
            gradient = new { softness = sky.Gradient.Softness, bands = sky.Gradient.Bands.Select(Band).ToArray() },
            abyss = sky.Abyss is { } abyss
                ? new
                {
                    bands = abyss.Bands.Select(Band).ToArray(),
                    glow = abyss.Glow is { } glow ? new { elevation = glow.Elevation, width = glow.Width, color = Array(glow.Color) } : null,
                }
                : null,
            // The gradient and abyss together, from the zenith down, each from its elevation up to the band above (see Bands)
            bands = Bands(sky).Select(Band).ToArray(),
            stars = sky.Stars is { } stars
                ? new { density = stars.Density, size = stars.Size, twinkle = stars.Twinkle, tint = stars.Tint, minElevation = stars.MinElevation, seed = layout?.StarSeed }
                : null,
            fade = sky.Fade is { } fade ? new { start = fade.Start, end = fade.End, amount = fade.Amount } : null,
            moon = sky.Moon is { } moon
                ? new
                {
                    model = moon.Model,
                    size = moon.Size,
                    glow = moon.Glow,
                    turn = new { amount = moon.Turn.Amount, period = moon.Turn.Period },
                    blink = new { every = Array(moon.Blink.Every), duration = moon.Blink.Duration },
                    seed = layout?.MoonSeed,
                }
                : null,
            floaters = sky.Floaters is { } floaters
                ? new
                {
                    model = floaters.Model,
                    count = floaters.Count,
                    distance = Array(floaters.Distance),
                    elevation = Array(floaters.Elevation),
                    scale = floaters.Scale,
                    drift = floaters.Drift,
                    bob = floaters.Bob,
                    // Each floater: azimuth and elevation in degrees, distance in metres, phase of its bob (0 to 1)
                    layout = layout?.Floaters.Select(f => new InlineNumbers(f.Azimuth, f.Elevation, f.Distance, f.Phase)).ToArray(),
                }
                : null,
            spores = sky.Spores is { } spores
                ? new
                {
                    abyss = spores.Abyss is { } a
                        ? new
                        {
                            count = a.Count,
                            color = Array(a.Color),
                            altColor = Array(a.AltColor),
                            rise = a.Rise,
                            size = a.Size,
                            from = a.From,
                            to = a.To,
                            // Each spore: x and z in metres, the phase of its rise (0 to 1), 1 for the other colour, its size factor
                            points = layout?.AbyssSpores,
                        }
                        : null,
                    near = spores.Near is { } n
                        ? new
                        {
                            count = n.Count,
                            radius = n.Radius,
                            color = Array(n.Color),
                            size = n.Size,
                            drift = n.Drift,
                            // Each mote: x, y and z in the box round the camera (-0.5 to 0.5, times the box's width), the phase of its drift
                            points = layout?.NearSpores,
                        }
                        : null,
                }
                : null,
            post = new { exposure = sky.Post.Exposure, bloom = sky.Post.Bloom },
        };
    }

    private static object Band(SkyBand band) => new { from = band.From, color = Array(band.Color) };

    private static float[] Array(Vector3 v) => [v.X, v.Y, v.Z];

    private static float[] Array(Vector2 v) => [v.X, v.Y];
}

/// <summary>
/// A short list of numbers written on one line of an indented JSON file, rounded to <see cref="Decimals"/> places, so a list of
/// hundreds of points takes a line per point rather than a line per number.
/// </summary>
[JsonConverter(typeof(InlineNumbersConverter))]
public sealed class InlineNumbers(params float[] values)
{
    public const int Decimals = 3;

    public IReadOnlyList<float> Values { get; } = values;

    private sealed class InlineNumbersConverter : JsonConverter<InlineNumbers>
    {
        public override InlineNumbers Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<float[]>(ref reader, options) ?? []);

        public override void Write(Utf8JsonWriter writer, InlineNumbers value, JsonSerializerOptions options) =>
            writer.WriteRawValue("[" + string.Join(", ", value.Values.Select(v =>
                MathF.Round(v, Decimals).ToString("0.###", CultureInfo.InvariantCulture))) + "]");
    }
}
