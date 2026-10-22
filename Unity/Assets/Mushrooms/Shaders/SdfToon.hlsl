// The cartoon look of a meshed SDF model, for SdfToon.shader. Keep in step with the toon branch of the IDE's preview
// (IDE/wwwroot/js/sdf-renderer.js, `sdf render --style toon`): same bands, highlight, rim and outline, same defaults.
//
// Colour comes from the same per-vertex data as the realistic SdfSurface (see SdfSurface.hlsl for the packing): the base colour,
// its variation patches (toned down by _ToonVariation.x), speckles and the edge colour where the material is thin. Nothing comes from
// the material, so every mushroom shares one: the edge colour, which the realistic look takes per region, is worked out per vertex
// by MushroomBuilder (UV6 rgb = edge colour of the vertex's materials, a = how far it has turned to it). The light is the
// scene's main light (URP's: the sun source of the lighting settings, else the brightest directional light), with its cascaded
// shadows, reduced to its hue: a toon surface shows the same flat tones at any light intensity. The sky (SdfSky.hlsl) tints the shadow
// band and scales the result by its brightness, all but the glowing edges.

#ifndef SDF_TOON_INCLUDED
#define SDF_TOON_INCLUDED

#include "Assets/Mushrooms/Shaders/SdfSky.hlsl"
#include "Assets/Mushrooms/Shaders/SdfComic.hlsl"

// x = shade threshold (light below it is the shadow tone), y = lit threshold (light above it is the lit tone),
// z = softness of the band edges (in the same units, -1 to 1), w = how much the baked ambient occlusion darkens (0 to 1)
// _ToonShadowTone, _ToonMidTone, _ToonLitTone: rgb = multipliers of the colour in the three bands (linear)
// _ToonSpecular x = strength, y = threshold of the highlight, z = roughness at which highlights are gone
// _ToonRim      x = strength, y = width (0 to 1 of the way in from the silhouette)
// _ToonVariation x = how much of the material's colour patches and blotches show (speckles always show)

float3 SdfToonColor(float4 VertexColor, float4 UV1, float4 UV2, float4 UV3, float4 UV4, float4 UV5, float4 UV6)
{
    float3 local = UV1.xyz;
    float detailScale = max(UV2.y, 0.001);
    // Patches and blotches only, then toned down; speckles at full strength on top
    float3 varied = SdfVariedColor(VertexColor.rgb, local, UV3, UV2.w, detailScale, float4(0, 0, 0, 0), float2(0, 0));
    float3 color = lerp(VertexColor.rgb, varied, _ToonVariation.x);
    if (UV4.a > 0.0 && UV5.x > 0.0)
    {
        float2 fleck = SdfSpeckle(local, UV4.a, UV5.x, UV5.y);
        color = lerp(color, saturate(UV4.rgb * fleck.y), fleck.x);
    }
    color = lerp(color, UV6.rgb, saturate(UV6.a));
    return color;
}

// n, v, l: unit normal, direction to the eye and to the light; shadow 1 = in the light; tint = the light's hue (max channel 1);
// glow = how far the albedo turned to the glowing edge colour (UV6.a), which keeps its strength under a dark sky (SkyTone)
float3 SdfToonShade(float3 albedo, float3 n, float3 v, float3 l, float shadow, float3 tint, float roughness, float specular,
    float wetness, float occlusion, float glow)
{
    float nl = dot(n, l);
    // Cast shadow counts as facing away, so it falls in the shadow band with a crisp edge
    float light = lerp(-1.0, nl, shadow);
    float soft = max(_ToonBands.z, 1e-4);
    // The story's panels (SdfComic.hlsl): hard band edges, and a stronger and wider rim
    float comic = step(0.5, _SdfComic.x);
    soft = lerp(soft, 0.004, comic);
    float mid = smoothstep(_ToonBands.x - soft, _ToonBands.x + soft, light);
    float lit = smoothstep(_ToonBands.y - soft, _ToonBands.y + soft, light);
    float3 tone = lerp(_ToonShadowTone.rgb * SkyShadowTint(), lerp(_ToonMidTone.rgb, _ToonLitTone.rgb, lit) * tint, mid);
    float3 color = albedo * tone * lerp(1.0, occlusion, _ToonBands.w);
    gSdfAlbedo = albedo;
    gSdfBand = lerp(0.25, lerp(0.6, 1.0, step(0.5, lit)), step(0.5, mid));

    // Hard highlight: Blinn-Phong as sharp as the surface is smooth (wet is glossy), cut at a threshold; none on rough surfaces
    float r = lerp(roughness, 0.1, saturate(wetness));
    float a = max(r * r, 0.01);
    float shininess = 2.0 / (a * a) - 2.0;
    float3 h = normalize(l + v);
    float highlight = pow(saturate(dot(n, h)), shininess);
    float edge = max(fwidth(highlight), 1e-3);
    float strength = _ToonSpecular.x * saturate(2.0 * specular + wetness) * (1.0 - smoothstep(_ToonSpecular.z * 0.6, _ToonSpecular.z, r));
    color += smoothstep(_ToonSpecular.y - edge, _ToonSpecular.y + edge, highlight) * strength * mid * tint;

    // Rim: a band of light along the silhouette on the lit side
    float rim = 1.0 - saturate(dot(n, v));
    float rimEdge = max(fwidth(rim), 1e-3);
    float rimWidth = _ToonRim.y * lerp(1.0, 1.3, comic);
    color += smoothstep(1.0 - rimWidth - rimEdge, 1.0 - rimWidth + rimEdge, rim) * _ToonRim.x * lerp(1.0, 1.8, comic) * mid * tint * albedo;
    return SkyTone(color, glow);
}

#endif
