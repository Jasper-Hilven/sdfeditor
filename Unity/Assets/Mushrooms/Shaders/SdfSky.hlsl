// The sky's light on the toon world, shared by every shader that needs it (SdfToon.hlsl, SdfToonOutline.shader, SdfGround.shader,
// SdfSky.shader). Keep in step with the toon branch of the IDE's preview (IDE/wwwroot/js/sdf-renderer.js, uSky*).
//
// Globals, set by SkySettings from the level's resolved sky (level.json "sky", content/skies/*.sky); vectors only, which WebGL 2 takes:
// _SkyTone       x = brightness of the shaded colour, y = glowKeep (how much glowing edges keep from it), z = 1 once set
// _SkyShadowTint rgb = multiplier of the shadow band's tone
// _SkyBandColors[8], _SkyBandFrom[8]: the gradient and abyss together from the zenith down; band i is its colour from its elevation up
//                to band i - 1's; _SkyBandFrom x = sin of the elevation, y = half the edge's width in the same units
// _SkyBands      x = band count
// _SkyAbyssGlow  x = sin of the glow's elevation, y = its half width (sin), _SkyAbyssGlowColor rgb
// _SkyFade       x = start, y = end (metres), z = amount at the end
// Unset (a scene without SkySettings, a prefab preview) they are all zero, which reads as the identity: the toon look as it was.

#ifndef SDF_SKY_INCLUDED
#define SDF_SKY_INCLUDED

#define SDF_SKY_MAX_BANDS 8

float4 _SkyTone;
float4 _SkyShadowTint;
float4 _SkyBandColors[SDF_SKY_MAX_BANDS];
float4 _SkyBandFrom[SDF_SKY_MAX_BANDS];
float4 _SkyBands;
float4 _SkyAbyssGlow;
float4 _SkyAbyssGlowColor;
float4 _SkyFade;

// The fade never looks further down than this (sin of -2°): the ground fades toward the band behind it, not into the abyss
#define SDF_SKY_FADE_LOWEST -0.0349

// The shadow band's multiplier: 1 until set
float3 SkyShadowTint()
{
    return _SkyTone.z > 0.5 ? _SkyShadowTint.rgb : float3(1, 1, 1);
}

// The shaded colour at the sky's brightness, glowing parts (glow 0 to 1) kept from it by glowKeep. With the identity (unset, or the day
// preset: brightness 1, glowKeep 0) it is exactly the colour: x * 1 and lerp(a, b, 0) are exact
float3 SkyTone(float3 color, float glow)
{
    float2 tone = _SkyTone.z > 0.5 ? _SkyTone.xy : float2(1, 0);
    return lerp(color * tone.x, color, glow * tone.y);
}

// The banded sky in a direction, the band edges anti-aliased over at least a pixel
float3 SkyGradient(float3 direction)
{
    int count = (int)_SkyBands.x;
    if (count <= 0)
        return float3(0, 0, 0);
    float y = direction.y / max(length(direction), 1e-5);
    float pixel = fwidth(y);
    float3 color = _SkyBandColors[SDF_SKY_MAX_BANDS - 1].rgb;
    // From the lowest band up: each one covers what is above its elevation
    [unroll]
    for (int i = SDF_SKY_MAX_BANDS - 1; i >= 0; i--)
    {
        if (i < count)
        {
            float edge = max(_SkyBandFrom[i].y, pixel);
            float above = i == count - 1 ? 1.0 : smoothstep(_SkyBandFrom[i].x - edge, _SkyBandFrom[i].x + edge, y);
            color = lerp(color, _SkyBandColors[i].rgb, above);
        }
    }
    float glow = (y - _SkyAbyssGlow.x) / max(_SkyAbyssGlow.y, 1e-4);
    return color + _SkyAbyssGlowColor.rgb * exp(-glow * glow);
}

// A surface's colour faded toward the sky behind it with distance, in place of fog; exactly the colour without a fade
float3 SkyFade(float3 color, float3 positionWS)
{
    if (_SkyFade.z <= 0.0)
        return color;
    float3 offset = positionWS - _WorldSpaceCameraPos;
    float distance = length(offset);
    float3 direction = offset / max(distance, 1e-4);
    direction.y = max(direction.y, SDF_SKY_FADE_LOWEST);
    float amount = smoothstep(_SkyFade.x, _SkyFade.y, distance) * _SkyFade.z;
    return lerp(color, SkyGradient(direction), amount);
}

#endif
