// The comic post pass of the story intro's panels (ticket 31.5), editor only, never shipped. StoryCapture renders a view twice, a colour
// pass (_MainTex) and an aux pass (_Aux, see SdfComic.hlsl: rg = the normal octahedral encoded, b = the distance along the view, a = the
// toon band: 0 nothing drawn, 0.25 shadow, 0.6 mid, 1 lit), and this lays the comic over the colour:
//   - an ink line, slightly wobbly, where the aux pass jumps in depth or turns in normal (creases and contacts the mesh outline misses),
//   - a halftone: the shadow band filled with a dot screen at 45 degrees, in a darker shade of the surface colour,
//   - warm paper: a faint grain over everything and a slight posterisation.
// Sizes are in pixels at 1080p and follow _Scale (the height of the image over 1080), so a 2160 render has the same look as a 1080 one.
Shader "Hidden/SdfStory/ComicPost"
{
    Properties
    {
        _MainTex("Colour", 2D) = "white" {}
        _Aux("Aux", 2D) = "black" {}
        _Scale("Pixels per 1080p pixel", Float) = 2
        _Ink("Ink line (pixels at 1080p, depth threshold, normal threshold)", Vector) = (2.6, 0.07, 0.45, 0)
        _InkColor("Ink colour", Color) = (0.1, 0.065, 0.09, 1)
        _Halftone("Halftone (cell in pixels at 1080p, dot radius, darkness)", Vector) = (6, 0.36, 0.58, 0)
        _Paper("Paper (grain, posterise levels, tint strength)", Vector) = (0.03, 14, 0.55, 0)
        _Seed("Seed", Float) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _Aux;
            float _Scale;
            float4 _Ink;
            float4 _InkColor;
            float4 _Halftone;
            float4 _Paper;
            float _Seed;

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise, 0 to 1
            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float4 aux(float2 uv) { return tex2Dlod(_Aux, float4(uv, 0, 0)); }

            float3 toGamma(float3 c) { return pow(max(c, 0.0), 1.0 / 2.2); }
            float3 toLinear(float3 c) { return pow(max(c, 0.0), 2.2); }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 texel = _MainTex_TexelSize.xy;
                float2 pixel = uv / texel;
                float3 col = tex2D(_MainTex, uv).rgb;

                // The ink line: the aux pass sampled across a point warped by slow noise, so the line wanders by a pixel or two
                float2 warp = (float2(noise(pixel / (70.0 * _Scale) + _Seed), noise(pixel / (70.0 * _Scale) + 31.7 + _Seed)) - 0.5) * 3.2 * _Scale * texel;
                float2 p = uv + warp;
                float2 r = _Ink.x * 0.5 * _Scale * texel;
                float4 a0 = aux(p + float2(-r.x, -r.y));
                float4 a1 = aux(p + float2(r.x, r.y));
                float4 b0 = aux(p + float2(r.x, -r.y));
                float4 b1 = aux(p + float2(-r.x, r.y));
                float depthEdge = max(abs(a0.b - a1.b) / (min(a0.b, a1.b) + 0.15), abs(b0.b - b1.b) / (min(b0.b, b1.b) + 0.15));
                float both = step(0.5, min(min(a0.a, a1.a), min(b0.a, b1.a)));
                float normalEdge = max(length(a0.rg - a1.rg), length(b0.rg - b1.rg)) * both;
                float edge = max(smoothstep(_Ink.y, _Ink.y * 1.6, depthEdge), smoothstep(_Ink.z, _Ink.z * 1.5, normalEdge));
                // Empty pixels (nothing drawn there) next to drawn ones are the mesh outline's business
                edge *= step(0.5, max(max(a0.a, a1.a), max(b0.a, b1.a)));

                // The halftone: dots on a 45 degree grid in the shadow band
                float cell = _Halftone.x * _Scale;
                float2 q = float2(pixel.x + pixel.y, pixel.y - pixel.x) * 0.70710678 / cell;
                float d = length(frac(q) - 0.5);
                float aa = 0.9 / cell;
                float dots = 1.0 - smoothstep(_Halftone.y - aa, _Halftone.y + aa, d);
                float band = aux(uv).a;
                float shadow = step(0.1, band) * step(band, 0.4);
                float luma = dot(col, float3(0.3, 0.59, 0.11));
                float3 dark = lerp(luma.xxx, col, 1.3) * _Halftone.z;
                col = lerp(col, dark, dots * shadow);

                // Ink over it
                col = lerp(col, _InkColor.rgb, edge * 0.92);

                // Warm paper: a tint, a grain and a few levels fewer
                float3 g = toGamma(col);
                // The fine grain is faint (it is what makes a JPG big); the paper's mottling is a slow noise
                float grain = (hash(floor(pixel / (_Scale * 2.0)) + _Seed) - 0.5) * _Paper.x * 0.3 + (noise(pixel / (40.0 * _Scale) + 9.0) - 0.5) * _Paper.x;
                g *= lerp(float3(1, 1, 1), float3(1.03, 0.985, 0.9), _Paper.z);
                g += grain;
                float3 levels = floor(g * _Paper.y + 0.5) / _Paper.y;
                g = lerp(g, levels, 0.55);
                return float4(toLinear(saturate(g)), 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
