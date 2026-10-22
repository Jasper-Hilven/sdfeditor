// The data pulses of a level (ticket 30.9): bands of light running along flat strips laid 2 cm over the traces. One mesh for every line,
// UV.x = metres along the line from its first corner, UV.y = 0..1 across. The band is drawn from time alone, so there is no per-frame work
// on the CPU: a head leaves the first corner every _Every seconds, runs _Speed metres per second and the band is _Length metres behind it.
// The same formula as SdfBattle.PulseRules.Behind (which decides who is boosted): d = frac((speed * t - u) / period) * period, lit where
// d < length. Toon: hard bands, a white head, then gold, then a dimmer tail. Unlit and additive (no depth write, so it only brightens
// the trace), no lights, no textures, WebGL 2. _Time.y is the scene's time, which is also what the rule's clock is.
Shader "SdfLevel/Pulse"
{
    Properties
    {
        _Every("Seconds between pulses", Float) = 4
        _Speed("Metres per second", Float) = 18
        _Length("Pulse length (m)", Float) = 3
        _Gold("Gold (linear)", Color) = (1.0, 0.62, 0.12, 1)
        _White("Head (linear)", Color) = (1.0, 0.95, 0.8, 1)
        _Intensity("Intensity", Float) = 1.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            // Over the 2 cm: keeps the strip in front of the ground at a distance, where the depth buffer cannot tell them apart
            Offset -2, -2
            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Every;
                float _Speed;
                float _Length;
                float4 _Gold;
                float4 _White;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float period = max(_Speed * _Every, 1e-3);
                float x = (_Speed * _Time.y - input.uv.x) / period;
                // Metres behind the head of the nearest pulse ahead of this point, 0 at the head (PulseRules.Behind)
                float behind = (x - floor(x)) * period;
                float along = behind / max(_Length, 1e-3);
                if (along >= 1)
                    discard;
                // Hard bands along the pulse: the head, the body, the tail; and a thin dim rim along each edge
                float3 colour = along < 0.25 ? _White.rgb : along < 0.7 ? _Gold.rgb : _Gold.rgb * 0.55;
                float edge = abs(input.uv.y - 0.5) * 2;
                colour *= edge > 0.85 ? 0.6 : 1.0;
                return half4(colour * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
