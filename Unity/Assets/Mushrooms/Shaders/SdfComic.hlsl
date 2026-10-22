// The capture-only comic look of the story intro's panels (ticket 31.5), shared by the toon shaders. The editor's StoryCapture sets
// these globals while it renders a panel; the game never does, and unset they are all zero, which reads as off: the toon look as it was.
// Everything is vectors and plain maths, no keywords, no compute, so it builds for WebGL 2 like the rest.
//
// _SdfComic x = mode: 0 off, 1 the colour pass (hard bands, a stronger rim, the story's lamp and monitor lights, wider outlines),
//                2 the aux pass (see SdfToonOutput: normal, depth and band for the post pass); y = number of lights (at most 4);
//                z = the outline width multiplier
// _SdfComicLightPos[i]   xyz = where, w = 1 / range squared
// _SdfComicLightColor[i] rgb = colour, a = intensity
// The toon look takes only the main light, so the lamp and monitor of 42's room, which are point lights, shade the surfaces here: two hard
// steps of their own, added to the sun's bands.

#ifndef SDF_COMIC_INCLUDED
#define SDF_COMIC_INCLUDED

#define SDF_COMIC_MAX_LIGHTS 4

float4 _SdfComic;
float4 _SdfComicLightPos[SDF_COMIC_MAX_LIGHTS];
float4 _SdfComicLightColor[SDF_COMIC_MAX_LIGHTS];

// Set by SdfToonShade for the pixel being shaded: its colour before lighting, and the band it fell in (0.25 shadow, 0.6 mid, 1 lit)
static float3 gSdfAlbedo = float3(1, 1, 1);
static float gSdfBand = 1.0;

// The story's point lights on a surface: in two hard steps by how squarely and how closely the light reaches it
float3 SdfComicLights(float3 n, float3 positionWS)
{
    float3 sum = float3(0, 0, 0);
    for (int i = 0; i < SDF_COMIC_MAX_LIGHTS; i++)
    {
        if (i >= (int)_SdfComic.y)
            break;
        float3 d = _SdfComicLightPos[i].xyz - positionWS;
        float d2 = dot(d, d);
        float a = saturate(1.0 - d2 * _SdfComicLightPos[i].w);
        a *= a;
        float e = dot(n, d * rsqrt(max(d2, 1e-6))) * a * _SdfComicLightColor[i].a;
        float edge = max(fwidth(e), 1e-3);
        float level = smoothstep(0.3 - edge, 0.3 + edge, e) * 0.3 + smoothstep(1.1 - edge, 1.1 + edge, e) * 0.4;
        sum += _SdfComicLightColor[i].rgb * level;
    }
    return sum;
}

// What a toon pixel writes: the colour as it is when the comic look is off; in the colour pass the colour with the story's lights added; in
// the aux pass (a second render of the same view that the post pass reads) rg = the geometric normal, octahedral encoded, b = the distance
// along the view, a = the band, raised to mid where a light lifts a shadow. n must be the surface's own normal, not a patterned one.
float4 SdfToonOutput(float3 color, float3 n, float3 positionWS)
{
    if (_SdfComic.x < 0.5)
        return float4(color, 1.0);
    float3 lights = SdfComicLights(n, positionWS);
    if (_SdfComic.x < 1.5)
        // Where the sun already lights the surface the lights add little: they fill the shadow and mid bands
    return float4(color + gSdfAlbedo * lights * lerp(1.0, 0.2, step(0.9, gSdfBand)), 1.0);
    float band = lerp(gSdfBand, max(gSdfBand, 0.6), step(0.25, dot(lights, float3(0.33, 0.33, 0.33))));
    return float4(PackNormalOctQuadEncode(n), length(positionWS - GetCameraPositionWS()), band);
}

#endif
