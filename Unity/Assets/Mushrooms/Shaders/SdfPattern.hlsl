// Detail without vertices (ticket 06): patterns a material's file asks for (`pattern stones size=0.55 grout=0.035`), drawn per pixel by
// SdfToon.shader in the part's own space, so they stick to the part, repeat on every use of it and stay put when its levels of detail
// change. A few ALU per pixel and no textures, for WebGL 2.
//
// The pattern comes per vertex in UV7, which only meshes with a patterned material have (MushroomBuilder.Fill; the other channels are
// as SdfSurface.hlsl describes them): x = kind (0 none, 1 stones, 2 fibres, 3 gills, as SDFPipeline's MaterialPatternKind), y = size
// (m), z = grout (m), w = weight, 1 inside the material and 0 on the vertices at its border, so the pattern fades out across the faces
// that blend into the next material.
//
// The lines between are drawn in ink, the surface colour times _ToonInk (the outline's tone), so joints read as drawn. They thin out
// and blend to the material's mean colour (its colour with the ink averaged in) by distance, from SDF_PATTERN_FADE.x to .y metres,
// where the instanced castle parts hand over from LOD0 to LOD1, and sooner where screen-space derivatives say a line would be under
// about a pixel wide: the pattern never shimmers and looks the same at every level.

#ifndef SDF_PATTERN_INCLUDED
#define SDF_PATTERN_INCLUDED

#include "Assets/Mushrooms/Shaders/SdfSurface.hlsl"

// Metres from the camera over which the pattern fades to the mean colour
#define SDF_PATTERN_FADE float2(25.0, 40.0)

// A stone course is this share of a block's length high
#define SDF_STONE_COURSE 0.55
// How much a block's colour varies from the next (fraction of the colour, either way)
#define SDF_STONE_JITTER 0.12
// How far the normal leans out over a block's bevelled edge, and how wide the bevel is in grout widths
#define SDF_STONE_BEVEL float2(0.55, 2.5)

// Distance in metres to the nearest line of a running bond of blocks size long and size * SDF_STONE_COURSE high, in the plane's two
// coordinates; the direction to that line in the plane, and the block's number for its colour.
float SdfStoneLines(float2 uv, float size, float plane, out float2 towards, out float block)
{
    float height = size * SDF_STONE_COURSE;
    float row = floor(uv.y / height);
    float along = uv.x / size + 0.5 * frac(row * 0.5) * 2.0;
    float column = floor(along);
    float fu = along - column;
    float fv = uv.y / height - row;
    float du = min(fu, 1.0 - fu) * size;
    float dv = min(fv, 1.0 - fv) * height;
    towards = du < dv ? float2(fu < 0.5 ? -1.0 : 1.0, 0.0) : float2(0.0, fv < 0.5 ? -1.0 : 1.0);
    block = SdfHash(float3(column, row, plane * 17.0 + 3.1));
    return min(du, dv);
}

// Flagstones on faces that look up or down: square slabs size wide, every other row shifted by half
float SdfFlagstoneLines(float2 uv, float size, out float2 towards, out float block)
{
    float row = floor(uv.y / size);
    float along = uv.x / size + 0.5 * frac(row * 0.5) * 2.0;
    float column = floor(along);
    float fu = along - column;
    float fv = uv.y / size - row;
    float du = min(fu, 1.0 - fu) * size;
    float dv = min(fv, 1.0 - fv) * size;
    towards = du < dv ? float2(fu < 0.5 ? -1.0 : 1.0, 0.0) : float2(0.0, fv < 0.5 ? -1.0 : 1.0);
    block = SdfHash(float3(column, row, 91.7));
    return min(du, dv);
}

// Gill lines running out from the part's y axis about size apart: where they would spread to twice that, a shorter gill starts between
// each pair, as on a real cap. Distance in metres to the nearest.
float SdfGillLines(float3 p, float size)
{
    float r = max(length(p.xz), 1e-4);
    float around = 6.2831853 * r;
    float count = exp2(floor(log2(max(around / size, 1.0))));
    float turn = atan2(p.z, p.x) / 6.2831853 * count;
    float f = frac(turn);
    return min(f, 1.0 - f) * around / count;
}

// Fibres running up a stem: streaks of noise drawn out tenfold along y, size apart across them. Distance (in metres, roughly) to the
// middle of the nearest streak.
float SdfFibreLines(float3 p, float size)
{
    float n = SdfNoise(float3(p.x / size, p.y / (size * 10.0), p.z / size) + 7.3);
    return abs(n - 0.5) * size * 2.5;
}

// Draws the pattern into the albedo and, for bevelled blocks, the normal. pattern = UV7; positionOS and normalOS are the pixel's in
// the part's own space; viewDistance in metres.
void SdfPattern(float4 pattern, float3 positionOS, float3 normalOS, float3 positionWS, float viewDistance, inout float3 albedo,
    inout float3 normalWS)
{
    float kind = round(pattern.x);
    float size = max(pattern.y, 1e-3);
    float grout = pattern.z;
    float weight = saturate(pattern.w);
    if (kind < 0.5 || weight <= 0.0)
        return;

    // Metres one pixel covers here, from the screen-space derivatives of the position
    float pixel = max(length(fwidth(positionWS)), 1e-5);
    float shown = 1.0 - smoothstep(SDF_PATTERN_FADE.x, SDF_PATTERN_FADE.y, viewDistance);
    // Lines thinner than about a pixel and a half fade out, blocks fewer than about six pixels long lose their colour
    float lines = shown * saturate(grout / pixel - 0.75);
    float blocks = shown * saturate(size / pixel / 6.0 - 0.5);

    float3 ink = albedo * _ToonInk.rgb;
    float distanceToLine;
    float coverage;
    float3 color = albedo;
    float3 an = abs(normalOS);
    if (kind < 1.5)
    {
        // Stones: courses on the side faces, flagstones on faces that look up, each in the plane the face is nearest
        float2 towards;
        float block;
        float3 acrossOS;
        float3 upOS;
        if (an.y > 0.7)
        {
            distanceToLine = SdfFlagstoneLines(positionOS.xz, size, towards, block);
            acrossOS = float3(1, 0, 0);
            upOS = float3(0, 0, 1);
            coverage = 2.0 * grout / size;
        }
        else
        {
            bool facesX = an.x > an.z;
            float2 uv = facesX ? positionOS.zy : positionOS.xy;
            distanceToLine = SdfStoneLines(uv, size, facesX ? 1.0 : 2.0, towards, block);
            acrossOS = facesX ? float3(0, 0, 1) : float3(1, 0, 0);
            upOS = float3(0, 1, 0);
            coverage = grout / size + grout / (size * SDF_STONE_COURSE);
        }
        color = saturate(albedo * (1.0 + SDF_STONE_JITTER * (2.0 * block - 1.0) * blocks));

        // The block's edge is bevelled: the normal leans out towards the joint, so the edges catch another toon band
        float bevel = 1.0 - saturate((distanceToLine - 0.5 * grout) / (SDF_STONE_BEVEL.y * grout));
        float3 leanOS = acrossOS * towards.x + upOS * towards.y;
        float3 leanWS = TransformObjectToWorldDir(leanOS - normalOS * dot(leanOS, normalOS));
        normalWS = normalize(normalWS + leanWS * (SDF_STONE_BEVEL.x * bevel * lines * weight));
    }
    else if (kind < 2.5)
    {
        // Fibres show on the stem's sides, not on its flat rings
        distanceToLine = SdfFibreLines(positionOS, size);
        coverage = grout / size;
        lines *= 1.0 - smoothstep(0.5, 0.8, an.y);
    }
    else
    {
        distanceToLine = SdfGillLines(positionOS, size);
        coverage = grout / size;
    }

    float aa = max(fwidth(distanceToLine), 1e-5);
    float drawn = 1.0 - smoothstep(0.5 * grout - aa, 0.5 * grout + aa, distanceToLine);
    // Far off, the mean colour: the blocks' colours averaged out, and the ink spread over the surface as the lines cover it
    float3 base = lerp(albedo, color, blocks);
    float3 mean = lerp(base, ink, saturate(coverage));
    albedo = lerp(albedo, lerp(mean, lerp(base, ink, drawn), lines), weight);
}

#endif
