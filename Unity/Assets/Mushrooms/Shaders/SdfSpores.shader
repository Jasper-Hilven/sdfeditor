// Glowing spores and motes for URP: one mesh of quads per set (LevelSky builds it from the level's seeded layout), so a set is one draw,
// moved entirely in the vertex stage on _Time. No particle system, no compute (WebGL 2).
// - Abyss spores (_SporeMode 0): each rises from _SporeRange.x to .y (heights) at its base x, z, swaying a little, and starts over at the
//   bottom; they fade in and out at the ends.
// - Near motes (_SporeMode 1): each drifts on its own heading through a box _SporeBox metres wide that wraps round the camera (the
//   position relative to the camera, frac'd: a repeating volume), so there are always some round the player at no cost on the CPU;
//   they fade towards the box's edge, where they wrap, and right at the camera.
// Each is a camera-facing quad expanded in the vertex stage, sized in metres but kept between 1.5 pixels and _SporeMaxPixels so far ones
// stay visible and near ones cheap. The fragment draws a bright core and a soft halo, added on (so they read as glowing without bloom),
// above 1 so bloom catches them where it is on.
Shader "SdfMushrooms/Spores"
{
    Properties
    {
        _SporeMode("Mode (0 abyss, 1 near)", Float) = 0
        _SporeColor("Colour (linear)", Vector) = (0.3, 0.95, 0.85, 0)
        _SporeAltColor("Other colour (linear)", Vector) = (1, 0.7, 0.3, 0)
        _SporeSize("Size (core diameter in metres)", Float) = 0.25
        _SporeRise("Rise (metres a second)", Float) = 1.2
        _SporeRange("Heights (from, to)", Vector) = (-80, 20, 0, 0)
        _SporeBox("Box width round the camera (metres)", Float) = 70
        _SporeDrift("Drift (metres a second)", Float) = 0.3
        _SporeMaxPixels("Largest core radius (pixels at 1080p)", Float) = 6
        _SporeGlow("Brightness of the core", Float) = 2.5
        _SporeShift("Shift (abyss: x, z metres; near: x, y, z of the box; w: on in the cycle)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            // WebGL 2 and up: no compute, no geometry shaders
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SporeMode;
                float4 _SporeColor;
                float4 _SporeAltColor;
                float _SporeSize;
                float _SporeRise;
                float4 _SporeRange;
                float _SporeBox;
                float _SporeDrift;
                float _SporeMaxPixels;
                float _SporeGlow;
                float4 _SporeShift;
            CBUFFER_END

            // The quad reaches this many core radii out, for the halo
            #define HALO_REACH 3.0

            struct Attributes
            {
                float3 positionOS : POSITION;   // abyss: the base x, z in the world; near: the place in the box, -0.5 to 0.5
                float4 uv0 : TEXCOORD0;         // corner (-1 or 1, twice), phase (0 to 1), 1 for the other colour
                float2 uv1 : TEXCOORD1;         // x = size factor
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float3 color : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                // Epic's extra copies of a set move every spore elsewhere and on in its cycle (QualityExtras); zero for the set itself
                float phase = frac(input.uv0.z + _SporeShift.w);
                float time = _Time.y;
                float3 centre;
                float fade;
                if (_SporeMode < 0.5)
                {
                    float height = max(_SporeRange.y - _SporeRange.x, 1e-3);
                    float t = frac(phase + time * _SporeRise / height);
                    centre = float3(input.positionOS.x + _SporeShift.x, _SporeRange.x + t * height, input.positionOS.z + _SporeShift.z);
                    centre.xz += 0.8 * float2(sin(time * 0.31 + phase * 41.0), cos(time * 0.27 + phase * 29.0));
                    fade = smoothstep(0.0, 0.12, t) * (1.0 - smoothstep(0.85, 1.0, t));
                }
                else
                {
                    float box = max(_SporeBox, 1.0);
                    float angle = phase * 6.2832;
                    float3 heading = float3(cos(angle), 0.35 * sin(phase * 47.0), sin(angle));
                    float3 wander = 0.6 * float3(sin(time * 0.37 + phase * 23.0), sin(time * 0.29 + phase * 31.0), cos(time * 0.33 + phase * 17.0));
                    float3 place = (input.positionOS + _SporeShift.xyz) * box + heading * _SporeDrift * time + wander;
                    float3 relative = frac((place - _WorldSpaceCameraPos) / box + 0.5) - 0.5;
                    centre = _WorldSpaceCameraPos + relative * box;
                    fade = (1.0 - smoothstep(0.3, 0.5, length(relative))) * smoothstep(0.4, 1.5, length(relative * box));
                }

                // A quad facing the camera, its core between 1.5 pixels and _SporeMaxPixels in radius (at 1080 lines): the core's radius
                // in pixels from the centre and a point that far above it in view space
                float3 centreVS = TransformWorldToView(centre);
                float4 positionCS = TransformWViewToHClip(centreVS);
                float4 aboveCS = TransformWViewToHClip(centreVS + float3(0, _SporeSize * input.uv1.x * 0.5, 0));
                float pixels = abs(aboveCS.y / aboveCS.w - positionCS.y / positionCS.w) * 0.5 * _ScreenSize.y;
                float scale = _ScreenSize.y / 1080.0;
                float coreRadius = clamp(pixels, 1.5 * scale, _SporeMaxPixels * scale);
                // A core smaller than a pixel dims instead, so far spores do not all look the same size
                fade *= saturate(pixels / scale + 0.35);
                positionCS.xy += input.uv0.xy * coreRadius * HALO_REACH * 2.0 * _ScreenSize.zw * positionCS.w;

                Varyings output;
                output.positionCS = positionCS;
                output.corner = input.uv0.xy;
                output.color = (input.uv0.w > 0.5 ? _SporeAltColor.rgb : _SporeColor.rgb) * fade;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                // In core radii from the centre
                float d = length(input.corner) * HALO_REACH;
                float core = 1.0 - smoothstep(0.6, 1.0, d);
                float halo = exp(-1.2 * d * d) * 0.6 + exp(-0.45 * d) * 0.12 * (1.0 - smoothstep(2.4, 3.0, d));
                return float4(input.color * (core * _SporeGlow + halo), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
