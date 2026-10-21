// The level's fixed sun shadow (SdfLevel.StaticSunShadow): one shadow map over the whole level, rendered once from the sun, fixed in
// the world. It replaces URP's cascaded shadows of the main light where it is on, so no shadow ever changes with where the camera is:
// no cascade borders, no casters culled away from the view, the same (low) resolution everywhere.
//
//   _SdfStaticShadowMap      the depth of the casters seen from the sun (a shadow map: compared, 1 = in the light)
//   _SdfStaticShadowMatrix   world to the map's (u, v, depth)
//   _SdfStaticShadowParams   x = 1 when on (0: the main light's own shadows), y = one texel in u and v
#ifndef SDF_STATIC_SHADOW_INCLUDED
#define SDF_STATIC_SHADOW_INCLUDED

TEXTURE2D_SHADOW(_SdfStaticShadowMap);
SAMPLER_CMP(sampler_SdfStaticShadowMap);
float4x4 _SdfStaticShadowMatrix;
float4 _SdfStaticShadowParams;

bool SdfStaticShadowOn()
{
    return _SdfStaticShadowParams.x > 0.0;
}

// How far the point is in the light, 0 to 1: four bilinear comparisons half a texel apart, a soft edge two texels wide that the toon
// bands then make crisp. Outside the map, in the light.
float SdfStaticShadow(float3 positionWS)
{
    float3 coord = mul(_SdfStaticShadowMatrix, float4(positionWS, 1.0)).xyz;
    if (any(coord.xy < 0.0) || any(coord.xy > 1.0))
        return 1.0;
    float texel = _SdfStaticShadowParams.y * 0.5;
    float sum = SAMPLE_TEXTURE2D_SHADOW(_SdfStaticShadowMap, sampler_SdfStaticShadowMap, float3(coord.xy + float2(-texel, -texel), coord.z));
    sum += SAMPLE_TEXTURE2D_SHADOW(_SdfStaticShadowMap, sampler_SdfStaticShadowMap, float3(coord.xy + float2(texel, -texel), coord.z));
    sum += SAMPLE_TEXTURE2D_SHADOW(_SdfStaticShadowMap, sampler_SdfStaticShadowMap, float3(coord.xy + float2(-texel, texel), coord.z));
    sum += SAMPLE_TEXTURE2D_SHADOW(_SdfStaticShadowMap, sampler_SdfStaticShadowMap, float3(coord.xy + float2(texel, texel), coord.z));
    return sum * 0.25;
}

#endif
