// The glowing disc on the ground round a pod (PodBeacon): a hard-edged ring of the pod's colour at the edge, a faint fill inside it,
// and a ring that swells from the middle out to the edge and fades, again and again, so the pod catches the eye. One flat quad
// (uv -1..1 across both ways), scaled to the disc's radius. Cel-like bands, each edge smoothed by a pixel only, so it does not shimmer.
// Added on top of the ground (additive, no depth write, pulled towards the camera a little so it does not fight the ground); WebGL 2,
// no textures.
Shader "SdfBattle/Pod Ground Disc"
{
    Properties
    {
        _Color("Colour (linear)", Color) = (1, 0.5, 0.05, 1)
        _Intensity("Intensity", Float) = 1
        _Period("Seconds for one pulse", Float) = 1.6
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
            Offset -1, -1
            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Period;
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

            // 1 inside the band from inner to outer, 0 outside, smoothed over a pixel
            float Band(float r, float inner, float outer, float pixel)
            {
                return saturate((r - inner) / pixel + 0.5) * saturate((outer - r) / pixel + 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                float pixel = max(fwidth(r), 1e-4);
                float3 colour = _Color.rgb;
                // The edge's ring, bright, and the faint fill inside it
                float rim = Band(r, 0.86, 0.97, pixel);
                float fill = Band(r, -1, 0.86, pixel) * 0.12;
                // The pulse: a ring from the middle to the edge, fading as it goes, in steps
                float phase = frac(_Time.y / max(_Period, 0.1));
                float at = lerp(0.3, 0.9, phase);
                float pulse = Band(r, at - 0.05, at + 0.05, pixel) * floor((1 - phase) * 4 + 0.5) / 4;
                float strength = max(max(rim, fill), pulse);
                float3 band = rim > 0.5 ? lerp(colour, float3(1, 1, 1), 0.35) : colour;
                return half4(band * strength * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
