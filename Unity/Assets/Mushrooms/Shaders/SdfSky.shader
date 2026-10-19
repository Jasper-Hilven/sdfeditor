// The sky of a level for URP, as a skybox material: the banded gradient and the abyss of the sky's globals (SdfSky.hlsl, set by
// SkySettings), round stars, and a soft halo round the moon. No textures, nothing but arithmetic on the view direction (target 3.5,
// WebGL 2).
//
// Stars: the direction is put on the face of a cube it points through, cut into cells; a hash per cell says whether it holds a star,
// where in the cell (kept clear of the cell's edges, so a star needs no neighbours) and how bright and tinted. Each star is a round dot
// at least 1.5 pixels wide whatever the resolution (its radius follows fwidth of the face position), so it does not shimmer as the view
// turns, twinkling slowly on _Time. None below the stars' lowest elevation.
Shader "SdfMushrooms/Sky"
{
    Properties
    {
        _SkyStars("Stars (density, size in pixels, twinkle, tint)", Vector) = (0, 1.6, 0.3, 0.25)
        _SkyStarsPlace("Stars (sin of the lowest elevation, seed)", Vector) = (0.14, 0, 0, 0)
        _SkyMoonDirection("Moon (direction towards it, radius in radians)", Vector) = (0, 1, 0, 0)
        _SkyMoonHalo("Moon halo (rgb, strength)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" }

        Pass
        {
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            // WebGL 2 and up: no compute, no geometry shaders
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SdfSky.hlsl"

            float4 _SkyStars;
            float4 _SkyStarsPlace;
            float4 _SkyMoonDirection;
            float4 _SkyMoonHalo;

            // Cells along a cube face's side
            #define STAR_CELLS 24.0

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.direction = input.positionOS;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // The stars in a direction: the cell of the cube face it points through, and the star that cell may hold
            float3 Stars(float3 d)
            {
                float3 a = abs(d);
                float3 face;
                float2 uv;
                if (a.x >= a.y && a.x >= a.z)
                {
                    face = float3(sign(d.x), 0, 0);
                    uv = d.yz / a.x;
                }
                else if (a.y >= a.z)
                {
                    face = float3(0, sign(d.y), 0);
                    uv = d.xz / a.y;
                }
                else
                {
                    face = float3(0, 0, sign(d.z));
                    uv = d.xy / a.z;
                }
                float2 p = (uv * 0.5 + 0.5) * STAR_CELLS;
                // The footprint of a pixel on the face, taken before the cell's floor so it has no seams; capped where the faces meet
                float pixel = clamp(length(fwidth(p)), 1e-4, 0.1);
                float2 cell = floor(p);
                float3 key = face * 101.0 + float3(cell, _SkyStarsPlace.y);
                if (Hash(key + 0.37) >= _SkyStars.x)
                    return float3(0, 0, 0);
                float2 centre = cell + 0.25 + 0.5 * float2(Hash(key + 11.3), Hash(key + 23.7));
                // At least 1.5 pixels wide, so the dot covers whole pixels and does not shimmer
                float radius = max(_SkyStars.y, 1.5) * 0.5 * pixel;
                float distance = length(p - centre);
                float disc = 1.0 - smoothstep(radius - 0.5 * pixel, radius + 0.5 * pixel, distance);
                float brightness = 0.35 + 0.65 * Hash(key + 41.9);
                float twinkle = 1.0 - _SkyStars.z * (0.5 + 0.5 * sin(_Time.y * (0.6 + 1.4 * Hash(key + 57.1)) + 6.2832 * Hash(key + 73.3)));
                // A few faintly tinted, warm or cool
                float3 tint = Hash(key + 91.7) < 0.5 ? float3(1.0, 0.82, 0.62) : float3(0.68, 0.82, 1.0);
                float3 color = lerp(float3(1, 1, 1), tint, _SkyStars.w * step(0.6, Hash(key + 83.9)));
                return color * disc * brightness * twinkle;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                float3 d = normalize(input.direction);
                float3 color = SkyGradient(d);
                if (_SkyStars.x > 0.0)
                    color += Stars(d) * smoothstep(_SkyStarsPlace.x, _SkyStarsPlace.x + 0.07, d.y);
                // The moon's halo: a soft glow falling off from its rim, which reads as glowing without bloom
                if (_SkyMoonHalo.a > 0.0)
                {
                    float angle = acos(clamp(dot(d, _SkyMoonDirection.xyz), -1.0, 1.0));
                    float outside = max(angle - _SkyMoonDirection.w * 0.85, 0.0) / max(_SkyMoonDirection.w, 1e-3);
                    color += _SkyMoonHalo.rgb * _SkyMoonHalo.a * exp(-2.5 * outside);
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
