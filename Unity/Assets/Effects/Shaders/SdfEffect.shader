// The special effects of the toon world: lightning, tracers, muzzle flashes, sparks, puffs, droplets and spores, all from one dynamic
// mesh (FxBuffer) in one draw. Each quad is either a streak or a ball, spread out here so it faces the camera, so the mesh holds
// only where things are and needs no camera of its own:
// - a streak is a line from one point to another (position = the end, normal = the line's direction), as wide as uv0.z on each
//   side; it is a hard-edged band of colour with a bright core along its middle (uv1.rgb, uv1.a = the core's share of the width),
//   like a cel-painted bolt or tracer, and no soft glow: the scene's bloom adds the little there is;
// - a glowing ball (uv0.w = 1) is a disc of the band colour with a core in its middle, for flashes and spores;
// - a lit ball (uv0.w = 2) is a disc shaded as a sphere in the toon bands, lit by the sun, with the ink line round it the models
//   have (its tone as the outline's), for puffs of smoke and dust and drops of goop; uv1.rgb is a glow added in its shade.
// Opacity (colour alpha) dissolves a quad with an ordered dither rather than blending it, so it fades and still has hard edges.
// Drawn after the opaque models with the depth test but without writing depth, so URP's depth priming leaves it alone; WebGL 2,
// no textures.
Shader "SdfMushrooms/Effect"
{
    Properties
    {
        _ShadeTone("Shade tone of lit balls (linear)", Vector) = (0.55, 0.5, 0.66, 0)
        _LitThreshold("Light above which lit balls are in the lit tone (-1 to 1)", Float) = 0.05
        _InkTone("Ink line tone (multiplies the colour, linear)", Vector) = (0.18, 0.13, 0.14, 0)
        _InkWidth("Ink line width of lit balls (pixels)", Float) = 2.5
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
            Blend Off

            HLSLPROGRAM
            // WebGL 2 and up
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadeTone;
                float _LitThreshold;
                float4 _InkTone;
                float _InkWidth;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 core : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float halfWidth = input.uv0.z;
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                if (input.uv0.w < 0.5)
                {
                    // A streak: out to the side, across the line as the camera sees it
                    float3 along = TransformObjectToWorldDir(input.normalOS);
                    float3 side = cross(along, GetWorldSpaceNormalizeViewDir(positionWS));
                    float length2 = dot(side, side);
                    side = length2 > 1e-8 ? side * rsqrt(length2) : right;
                    positionWS += side * input.uv0.y * halfWidth;
                }
                else
                {
                    // A ball: a disc facing the camera
                    positionWS += (right * input.uv0.x + up * input.uv0.y) * halfWidth;
                }
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.shape = input.uv0;
                output.core = input.uv1;
                return output;
            }

            // Dissolves pixels with a 4 × 4 ordered dither as the opacity drops, so a fading effect keeps hard edges
            void Dissolve(float opacity, float2 pixel)
            {
                if (opacity >= 1.0)
                    return;
                uint2 p = uint2(pixel) & 3u;
                const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                clip(opacity - (bayer[p.y * 4u + p.x] + 0.5) / 16.0);
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                float kind = input.shape.w;
                float d = kind < 0.5 ? abs(input.shape.y) : length(input.shape.xy);
                clip(1.0 - d);
                Dissolve(input.color.a, input.positionCS.xy);
                float edge = max(fwidth(d), 1e-4);
                float3 band = input.color.rgb;

                if (kind < 1.5)
                {
                    // Streak or glowing ball: a flat band with a hard-edged core
                    float core = 1.0 - smoothstep(input.core.a - edge, input.core.a + edge, d);
                    return float4(lerp(band, input.core.rgb, core), 1.0);
                }

                // Lit ball: the disc as a sphere seen from the camera, in two toon bands from the sun, with an ink line round it
                float3 normalVS = float3(input.shape.x, input.shape.y, sqrt(saturate(1.0 - d * d)));
                float3 lightVS = mul((float3x3)UNITY_MATRIX_V, _MainLightPosition.xyz);
                float lit = smoothstep(_LitThreshold - 0.03, _LitThreshold + 0.03, dot(normalVS, lightVS));
                float3 color = lerp(band * _ShadeTone.rgb + input.core.rgb, band, lit);
                float ink = _InkWidth * edge;
                color = lerp(color, band * _InkTone.rgb, smoothstep(1.0 - ink - edge, 1.0 - ink + edge, d));
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
