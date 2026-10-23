// The menus' wall of comic panels and the screen of 42's monitor on the desk (ticket 35): unlit, a picture times the vertex colour times a tint.
// Cull Off, so a card is seen from either side whichever way its quad was wound. Every pass is URP's forward-only one (WebGL2, no extras).
Shader "SdfCampaign/WallCard"
{
    Properties
    {
        _MainTex("Picture", 2D) = "white" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
        // 0 for a card (uv straight on the quad), 1 for the monitor's screen (uv from the part's own xy, as the story's screen shader does)
        _Screen("Screen mode (0 or 1)", Float) = 0
        _ScreenRect("Screen rect (centre x, centre y, half width, half height)", Vector) = (0, 0.59, 0.34, 0.191)
        _Glow("Glow (screen brightness)", Float) = 1
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _Screen;
                float4 _ScreenRect;
                float _Glow;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionOS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color;
                output.positionOS = input.positionOS;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                float2 uv = input.uv;
                if (_Screen > 0.5)
                {
                    uv = (input.positionOS.xy - _ScreenRect.xy) / (2.0 * _ScreenRect.zw) + 0.5;
                    uv.x = 1.0 - uv.x;
                }
                float4 picture = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float3 color = picture.rgb * input.color.rgb * _Tint.rgb;
                if (_Screen > 0.5)
                {
                    // Scanlines and the small glow of a lit tube
                    color *= 0.92 + 0.08 * sin(uv.y * 3.14159 * 2.0 * 160.0);
                    float2 centred = uv - 0.5;
                    color *= (1.12 - 0.35 * dot(centred, centred)) * _Glow;
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
