// The surface of a meshed SDF model, as the IDE's raymarched preview (IDE/wwwroot/js/sdf-renderer.js) shades it. The toon look
// (SdfToon.hlsl) takes its colour functions; SdfSurface_float is what the realistic look's Shader Graph called, kept with the packing
// it documents. Every material parameter comes per vertex, blended exactly as the preview blends materials, packed by
// MushroomBuilder.Fill into the vertex colour and the UV channels:
//
//   colour  rgb = base colour (linear), a = wetness
//   UV0     x = roughness, y = specular, z = subsurfaceRadius (m), w = ambient occlusion (0 closed in to 1 open)
//   UV1     xyz = position in the shape's own coordinates (for the procedural detail), w = thickness (m of material straight in)
//   UV2     x = detailStrength, y = detailScale, z = detailStretch, w = colorVariation
//   UV3     rgb = variationColor (linear), a = variationScale
//   UV4     rgb = speckleColor (linear), a = speckleScale (m)
//   UV5     x = speckleDensity, y = speckleSize
//
// The mesh's own colour (glTF COLOR_0, with the variation and speckles baked and averaged per vertex for plain viewers) is not
// used: everything is drawn here per pixel from the unvaried base colour, so nothing is applied twice.
//
// Per region (the material, one per submesh) come the diffusion profile and the properties below.

#ifndef SDF_SURFACE_INCLUDED
#define SDF_SURFACE_INCLUDED

// Material properties, declared by the shader that includes this (in the material's constant buffer):
//   _Subsurface  x = the region's subsurfaceRadius (m), y = thickness (m) that maps to the profile's thickest
//   _DetailAxis  xyz = the axis fibres run out from and along, in the shape's own coordinates
//   _EdgeColor   rgb = colour of thin material (linear), a = edgeThickness (m), 0 is off
//   _Look        x = detail normal strength (1 = as the preview), y = darkening of wet surfaces (0.2 = as the preview)

// Value noise, exactly the preview's shadeHash / shadeNoise / shadeDetail.
float SdfHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float SdfNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(SdfHash(i), SdfHash(i + float3(1, 0, 0)), u.x), lerp(SdfHash(i + float3(0, 1, 0)), SdfHash(i + float3(1, 1, 0)), u.x), u.y),
        lerp(lerp(SdfHash(i + float3(0, 0, 1)), SdfHash(i + float3(1, 0, 1)), u.x), lerp(SdfHash(i + float3(0, 1, 1)), SdfHash(i + float3(1, 1, 1)), u.x), u.y),
        u.z);
}

float SdfDetail(float3 p)
{
    return 0.6 * SdfNoise(p) + 0.3 * SdfNoise(p * 2.07 + 13.1) + 0.1 * SdfNoise(p * 4.13 + 29.7);
}

// The preview's detailPattern: round grain, or with stretch above 1 fibres that run out from the axis and along it.
float SdfDetailPattern(float3 local, float scale, float stretch, float3 axis)
{
    scale = max(scale, 0.001);
    if (stretch <= 1.0) return SdfDetail(local / scale);
    axis = normalize(axis);
    float along = dot(local, axis);
    float3 radial = local - axis * along;
    float r = length(radial);
    float3 around = r > 1e-5 ? radial / r : float3(0, 0, 0);
    return SdfDetail(around * (0.5 / scale) + axis * ((r + along) / (scale * stretch)));
}

// The preview's speckle: flecks in the cells `scale` wide that the hash picks (`density` of them), placed, sized and toned at
// random, with a ragged soft edge. x = how far the colour goes to the fleck's, y = the fleck's tone (0.65 to 1.35).
float2 SdfSpeckle(float3 local, float scale, float density, float size)
{
    float3 p = local / scale;
    float3 cell = floor(p);
    float ragged = 0.35 * (SdfNoise(p * 3.1 + 5.3) - 0.5);
    float2 result = float2(0.0, 1.0);
    [loop] for (int z = -1; z <= 1; z++)
    [loop] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float3 c = cell + float3(x, y, z);
        if (SdfHash(c + 0.37) >= density) continue;
        float3 center = c + 0.15 + 0.7 * float3(SdfHash(c + 11.3), SdfHash(c + 23.7), SdfHash(c + 41.9));
        float radius = max(size, 0.01) * (0.5 + SdfHash(c + 57.1));
        float weight = 1.0 - smoothstep(0.55, 1.0, length(p - center) / radius + ragged);
        if (weight > result.x) result = float2(weight, 0.65 + 0.7 * SdfHash(c + 73.3));
    }
    return result;
}

// The preview's variedColor: large soft patches of variationColor, lighter and darker blotches a few detail features wide, then
// speckles.
float3 SdfVariedColor(float3 baseColor, float3 local, float4 variation, float colorVariation, float detailScale, float4 speckle,
    float2 speckleShape)
{
    float3 color = baseColor;
    if (variation.a > 0.0)
        color = lerp(color, variation.rgb, smoothstep(0.3, 0.75, SdfDetail(local / variation.a + 37.9)));
    if (colorVariation > 0.0)
    {
        float blotch = SdfDetail(local / max(detailScale * 5.0, 0.001) + 71.3) * 2.0 - 1.0;
        color = saturate(color * (1.0 + colorVariation * blotch));
    }
    if (speckle.a > 0.0 && speckleShape.x > 0.0)
    {
        float2 fleck = SdfSpeckle(local, speckle.a, speckleShape.x, speckleShape.y);
        color = lerp(color, saturate(speckle.rgb * fleck.y), fleck.x);
    }
    return color;
}

void SdfSurface_float(float4 VertexColor, float4 UV0, float4 UV1, float4 UV2, float4 UV3, float4 UV4, float4 UV5, float3 PositionWS, float3 NormalWS,
    out float3 BaseColor, out float3 Normal, out float Smoothness, out float Occlusion, out float CoatMask, out float SubsurfaceMask,
    out float Thickness)
{
    float3 local = UV1.xyz;
    float thickness = UV1.w;
    float wetness = VertexColor.a;
    float detailStrength = UV2.x;
    float detailScale = max(UV2.y, 0.001);

    // Albedo: variation, the edge colour where the material is thin, darker where wet
    float3 albedo = SdfVariedColor(VertexColor.rgb, local, UV3, UV2.w, detailScale, UV4, UV5.xy);
    if (_EdgeColor.a > 0.0)
        albedo = lerp(albedo, _EdgeColor.rgb, saturate(1.0 - thickness / _EdgeColor.a));
    BaseColor = albedo * (1.0 - _Look.y * wetness);

    // Detail bumps: the preview bends the normal by strength / 2 times the pattern's gradient times its scale, which is the gradient
    // of this height in metres. Its screen-space derivatives give the gradient along the surface (Mikkelsen's surface gradient), so
    // no tangents are needed. Fades out where a pixel covers a whole detail feature, which would only flicker.
    float3 n = normalize(NormalWS);
    Normal = n;
    float strength = detailStrength * _Look.x;
    if (strength > 0.0)
    {
        float height = SdfDetailPattern(local, detailScale, UV2.z, _DetailAxis.xyz) * detailScale * strength * 0.5;
        float3 dpdx = ddx(PositionWS);
        float3 dpdy = ddy(PositionWS);
        float footprint = max(length(ddx(local)), length(ddy(local)));
        float fade = saturate(detailScale / max(footprint, 1e-6) - 1.0);
        float3 r1 = cross(dpdy, n);
        float3 r2 = cross(n, dpdx);
        float det = dot(dpdx, r1);
        if (abs(det) > 1e-12)
        {
            float3 gradient = (ddx(height) * r1 + ddy(height) * r2) / det;
            Normal = normalize(n - fade * gradient);
        }
    }

    Smoothness = 1.0 - saturate(UV0.x);
    Occlusion = saturate(UV0.w);
    CoatMask = saturate(wetness);

    // Light scattering inside: as strong as the region's own material where the mix has its radius, none where it has none
    SubsurfaceMask = _Subsurface.x > 0.0 ? saturate(UV0.z / _Subsurface.x) : 0.0;
    Thickness = saturate(thickness / max(_Subsurface.y, 1e-4));
}

// Half precision variant, for Shader Graphs set to half precision
void SdfSurface_half(half4 VertexColor, half4 UV0, half4 UV1, half4 UV2, half4 UV3, half4 UV4, half4 UV5, half3 PositionWS, half3 NormalWS,
    out half3 BaseColor, out half3 Normal, out half Smoothness, out half Occlusion, out half CoatMask, out half SubsurfaceMask,
    out half Thickness)
{
    float3 baseColor, normal;
    float smoothness, occlusion, coatMask, subsurfaceMask, thickness;
    SdfSurface_float(VertexColor, UV0, UV1, UV2, UV3, UV4, UV5, PositionWS, NormalWS, baseColor, normal, smoothness, occlusion, coatMask,
        subsurfaceMask, thickness);
    BaseColor = baseColor;
    Normal = normal;
    Smoothness = smoothness;
    Occlusion = occlusion;
    CoatMask = coatMask;
    SubsurfaceMask = subsurfaceMask;
    Thickness = thickness;
}

#endif
