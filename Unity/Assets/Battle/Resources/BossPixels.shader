// The pixel blocks the Champion's Arena breaks into when Prime is beaten (ticket 33.8, after 31's pixel spores): flat blue cubes, two
// tones by the way a face looks (top light, sides mid, underside dark) so each reads as a block, a pixel of the picture coming apart.
// Drawn many at a time with GPU instancing (BossStaging), no textures, no lights; WebGL 2.
Shader "SdfBattle/Boss Pixels"
{
    Properties
    {
        _Color("Colour (linear)", Color) = (0.15, 0.4, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float up = input.normalWS.y;
                float shade = up > 0.5 ? 1.0 : (up < -0.5 ? 0.45 : 0.7);
                return half4(_Color.rgb * shade, 1);
            }
            ENDHLSL
        }
    }
}
