// The white flash of the sky when the Champion's Arena breaks up (ticket 33.8): one quad that covers the whole view whatever its
// transform (its vertices are the screen's corners), white at _Color's alpha, over the world and under the GUI. WebGL 2, no textures.
Shader "SdfBattle/Boss Flash"
{
    Properties
    {
        _Color("Colour", Color) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+100" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // A unit quad is -0.5..0.5 across: twice that is the whole clip space
                output.positionCS = float4(input.positionOS.xy * 2, 0, 1);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_Color.rgb, _Color.a);
            }
            ENDHLSL
        }
    }
}
