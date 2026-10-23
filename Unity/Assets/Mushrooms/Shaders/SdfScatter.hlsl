// What the instanced renderer (SdfLevel.InstancedScatterRenderer) adds to the toon shaders when it draws scattered models: wind,
// the instance's tint at its base, and the dithered fade between the rings of distance round the camera. Only in the SDF_SCATTER
// variant, which the renderer's own copies of the toon and outline materials turn on; every other renderer draws the shaders as they
// were, with nothing of this compiled in (and stays SRP batcher compatible). Both SdfToon.shader and SdfToonOutline.shader call the
// same functions in every pass, so the surface, its outline, its depth and its shadow sway and fade alike.
//
//   _SdfWind        global, set by the Wind component: xy = direction in world xz (unit), z = strength (1 = as authored), w = phase
//                   (seconds of wind, times its speed)
//   _CrowdCheer     global, set by the boss's arena (ticket 33.6): 0 to 1, how hard the arena's crowd cheers; the draw groups that cheer
//                   (_ScatterWind.w = 1: the level's "crowd" group) sway up to four times as far and hop with it
//   _ScatterWind    per draw: x = metres of sway at y metres above the model's base, z = height of the base that takes the tint (m),
//                   w = 1 for a group that cheers with _CrowdCheer
//   _ScatterRing    per draw, metres from _ScatterCamera to the instance's base: the ring fades in from x (none) to y (all) and out
//                   from z (all) to w (none); x = y = -1 for no fade in
//   _ScatterCamera  per draw: the camera the rings are measured from (the same in the shadow pass as in the colour pass)
//   _ScatterTint    per instance: rgb = the ground's colour under it (linear), a = how far the base turns to it

#ifndef SDF_SCATTER_INCLUDED
#define SDF_SCATTER_INCLUDED

#include "SdfPacked.hlsl"

#if defined(SDF_SCATTER)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

float4 _SdfWind;
float _CrowdCheer;
float4 _ScatterWind;
float4 _ScatterRing;
float4 _ScatterCamera;

UNITY_INSTANCING_BUFFER_START(SdfScatter)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ScatterTint)
UNITY_INSTANCING_BUFFER_END(SdfScatter)

// The instance's base: its transform's origin
float3 SdfScatterOrigin()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

// The vertex swayed by the wind, in object space, so each pass goes on to transform it as it did. The whole instance bends one way
// with its height above its base (squared, so the base stays put and the top goes furthest), by a phase that runs along the wind in
// world space: neighbours move together and gusts roll across the field. A small per-instance offset keeps them out of lockstep.
float3 SdfScatterWind(float3 positionOS)
{
    float cheer = saturate(_CrowdCheer) * _ScatterWind.w;
    float strength = _SdfWind.z * _ScatterWind.x * (1.0 + 3.0 * cheer);
    float3 origin = SdfScatterOrigin();
    // The cheering crowd hops: each instance on its own beat, up to 0.2 m at full cheer, in world metres so every size hops alike
    if (cheer > 0.0)
    {
        float hopBeat = frac(sin(dot(origin.xz, float2(39.346, 11.135))) * 43758.5453);
        float hop = cheer * 0.2 * abs(sin(_Time.y * 6.0 + hopBeat * 6.2831853));
        positionOS += mul((float3x3)UNITY_MATRIX_I_M, float3(0.0, hop, 0.0));
    }
    if (strength <= 0.0)
        return positionOS;
    float2 direction = _SdfWind.xy;
    float along = dot(origin.xz, direction);
    float jitter = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
    float phase = _SdfWind.w - along * 0.15 + jitter * 0.8;
    float gust = 0.6 + 0.4 * sin(_SdfWind.w * 0.23 - along * 0.04);
    float sway = gust * (0.55 + 0.45 * sin(phase)) + 0.12 * sin(phase * 2.7 + 1.3);
    float bend = saturate(positionOS.y / max(_ScatterWind.y, 1e-3));
    float3 offsetWS = float3(direction.x, 0.0, direction.y) * (strength * bend * bend * sway);
    return positionOS + mul((float3x3)UNITY_MATRIX_I_M, offsetWS);
}

// How far the vertex turns to the instance's tint: fully at the base, none above the tint height
float SdfScatterTintWeight(float3 positionOS, out float3 tint)
{
    float4 value = UNITY_ACCESS_INSTANCED_PROP(SdfScatter, _ScatterTint);
    tint = value.rgb;
    return _ScatterWind.z > 0.0 ? value.a * (1.0 - smoothstep(0.0, _ScatterWind.z, positionOS.y)) : 0.0;
}

// The base of the model in the ground's colour: its own colour, its patches and speckles turn to the tint, and its thin edges no
// longer turn to their edge colour, so a turf's mound of earth runs into the ground it stands on
void SdfScatterTint(float3 positionOS, inout float4 color, inout float4 uv3, inout float4 uv4, inout float4 uv6)
{
    float3 tint;
    float w = SdfScatterTintWeight(positionOS, tint);
    color.rgb = lerp(color.rgb, tint, w);
    uv3.rgb = lerp(uv3.rgb, tint, w);
    uv4.rgb = lerp(uv4.rgb, tint, w);
    uv6.a *= 1.0 - w;
}

// The outline only has the colour
void SdfScatterTintColor(float3 positionOS, inout float4 color)
{
    float3 tint;
    float w = SdfScatterTintWeight(positionOS, tint);
    color.rgb = lerp(color.rgb, tint, w);
}

// Drops this pixel where the instance is fading between rings: with the dither of URP's LOD cross-fade (on the pixel's screen position),
// a ring fading out keeps the pixels whose dither is below its share and the next ring fading in keeps the others, so where two rings
// overlap their levels of detail dissolve into each other, and at the last ring's end the instance dissolves away
void SdfScatterFade(float4 positionCS)
{
    float d = distance(SdfScatterOrigin(), _ScatterCamera.xyz);
    float dither = SAMPLE_TEXTURE2D(_DitheringTexture, sampler_PointRepeat, positionCS.xy * _DitheringTextureInvSize).a;
    if (d < _ScatterRing.y)
        clip(dither - (1.0 - saturate((d - _ScatterRing.x) / max(_ScatterRing.y - _ScatterRing.x, 1e-4))));
    else if (d > _ScatterRing.z)
        clip((1.0 - saturate((d - _ScatterRing.z) / max(_ScatterRing.w - _ScatterRing.z, 1e-4))) - dither - 1e-4);
}

#else

#define SdfScatterWind(positionOS) (positionOS)
#define SdfScatterTint(positionOS, color, uv3, uv4, uv6)
#define SdfScatterTintColor(positionOS, color)
#define SdfScatterFade(positionCS)

#endif

#endif
