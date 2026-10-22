// Flat colour for the pieces the story intro's capture builds in code (ticket 31.7), editor only, never shipped: the warp tunnel's rings and the
// pixel spores. The colour is the vertex colour times _Tint, in the comic's hard steps from a fixed light (or not shaded at all with _Emissive),
// two sided so the tunnel shows from inside. Like SdfStory/Screen it writes the capture's aux pass (SdfComic.hlsl), so the post pass inks it.
Shader "SdfStory/Flat"
{
    Properties
    {
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _Emissive("Emissive (1 = not shaded)", Float) = 0
        _LightDir("Light direction (towards the light)", Vector) = (0.3, 0.6, -0.7, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Mushrooms/Shaders/SdfComic.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Emissive;
                float4 _LightDir;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                float3 n = normalize(input.normalWS);
                // Two sided: the normal turned to face the eye
                float3 toEye = normalize(GetCameraPositionWS() - input.positionWS);
                n *= sign(dot(n, toEye) + 1e-5);

                float3 base = input.color.rgb * _Tint.rgb;
                float lit = dot(n, normalize(_LightDir.xyz));
                float band = lerp(lit > 0.35 ? 1.0 : (lit > -0.25 ? 0.6 : 0.25), 1.0, _Emissive);
                float shade = lerp(band > 0.9 ? 1.08 : (band > 0.5 ? 0.82 : 0.6), 1.0, _Emissive);
                gSdfAlbedo = base;
                gSdfBand = band;
                return SdfToonOutput(base * shade, n, input.positionWS);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
