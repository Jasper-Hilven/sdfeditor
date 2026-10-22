// The monitor's screen in the story intro's panels (ticket 31.5), editor only: it draws a texture unlit, with scanlines, a glitch (rows of
// blocks shifted sideways, a tear, a colour split, blue pixel blocks and a blue tint) and the soap-film bulge, a dome pushed out of the
// screen as a vertex offset by a uniform (StoryCapture swaps the screen's 12-triangle slab for a grid of the same size, so there are
// vertices to push). The screen part has no UVs (the mesher writes none), so they come from its position in the monitor's space, from
// _ScreenRect (centre x, centre y, half width, half height) in the part's own, Unity space. Unlit, so the lamp and the sun leave it be.
// In the capture's aux pass (SdfComic.hlsl) it writes the flat screen's normal, distance and the lit band, like the toon shaders.
Shader "SdfStory/Screen"
{
    Properties
    {
        _MainTex("Picture", 2D) = "white" {}
        _ScreenRect("Screen rect (centre x, centre y, half width, half height)", Vector) = (0, 0.59, 0.34, 0.191)
        _Glitch("Glitch (0 to 1)", Float) = 0
        _Bulge("Bulge (0 to 1)", Float) = 0
        _BulgeHeight("Bulge height at 1 (metres)", Float) = 0.16
        _Seed("Glitch seed", Float) = 1
        _Mirror("Mirror u (1 when the part's x runs against the picture's)", Float) = 1
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _ScreenRect;
                float _Glitch;
                float _Bulge;
                float _BulgeHeight;
                float _Seed;
                float _Mirror;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = input.positionOS;
                float2 unit = (position.xy - _ScreenRect.xy) / _ScreenRect.zw;
                // A dome, highest in the middle and flat at the rim, with the middle pushed out a little sideways as a film is
                float dome = saturate(1.0 - dot(unit, unit));
                position.z += _Bulge * _BulgeHeight * dome;
                position.xy += unit * _Bulge * 0.02 * dome;
                output.positionOS = input.positionOS;
                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(float3(0, 0, 1));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                if (_SdfComic.x > 1.5)
                    return float4(PackNormalOctQuadEncode(normalize(input.normalWS)), length(input.positionWS - GetCameraPositionWS()), 1.0);

                float2 uv = (input.positionOS.xy - _ScreenRect.xy) / (2.0 * _ScreenRect.zw) + 0.5;
                uv.x = lerp(uv.x, 1.0 - uv.x, _Mirror);
                float g = _Glitch;

                // Rows of blocks shifted sideways, and one tear band shifted further
                float row = floor(uv.y * 22.0);
                float shifted = step(1.0 - g * 0.55, Hash(float2(row, _Seed)));
                uv.x += shifted * (Hash(float2(row, _Seed + 7.0)) - 0.5) * 0.5 * g;
                float tearAt = frac(_Seed * 0.37 + 0.31);
                float inTear = step(abs(uv.y - tearAt), 0.035 * g);
                uv.x += inTear * 0.18 * g;
                uv = frac(uv);

                // A colour split that grows with the glitch
                float split = 0.012 * g;
                float3 color;
                color.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(split, 0)).r;
                color.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                color.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(split, 0)).b;

                // Pixel blocks of blue, and the blue tint over the rest
                float2 cell = floor(uv * float2(34.0, 19.0));
                float block = step(1.0 - g * 0.32, Hash(cell + _Seed * 3.1));
                float3 blue = float3(0.1, 0.3, 1.0) * (0.55 + 0.7 * Hash(cell + 11.0));
                color = lerp(color, blue, block);
                color = lerp(color, color * float3(0.55, 0.8, 1.25) + float3(0.0, 0.03, 0.12), saturate(g * 1.1));

                // Scanlines, and a small glow at the middle as a lit tube has
                float scan = 0.9 + 0.1 * sin(uv.y * 3.14159 * 2.0 * 180.0);
                color *= scan;
                float2 centred = uv - 0.5;
                color *= 1.12 - 0.35 * dot(centred, centred);
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
