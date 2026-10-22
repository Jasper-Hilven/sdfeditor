// Chanterelle Prime's spore shield (ticket 33.6): a dome of radius 7 m over his dais in phase 1, drawn as a toon sphere: a hard-edged
// bright rim where it is seen edge-on (the fresnel band), hexagonal spore cells by the sphere's own angles (no UVs, no textures), and
// a ripple at each blocked hit (up to four, their world points and ages in _Ripple0-3: xyz = the point, w = seconds since; w < 0 for
// none). Dithered rather than blended, like the effects (SdfEffect.shader): opaque pixels, a 4 x 4 ordered dither for its translucency,
// so it needs no sorting and keeps hard edges. Both faces are drawn, for whoever stands inside it. Only the dome's upper half is drawn.
// _Grow (0 to 1) swells it from its foot while it regrows. Vertex/fragment, WebGL 2, no compute.
Shader "SdfBattle/Boss Shield"
{
    Properties
    {
        _Tone("Dome tone (linear)", Color) = (0.35, 0.8, 1, 1)
        _Rim("Rim tone (linear)", Color) = (0.85, 1, 1, 1)
        _Grow("Grown (0 to 1)", Range(0, 1)) = 1
        _Cells("Hexagonal cells round the dome", Float) = 14
        _Ripple0("Ripple 0 (world point, age)", Vector) = (0, 0, 0, -1)
        _Ripple1("Ripple 1", Vector) = (0, 0, 0, -1)
        _Ripple2("Ripple 2", Vector) = (0, 0, 0, -1)
        _Ripple3("Ripple 3", Vector) = (0, 0, 0, -1)
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
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tone;
                float4 _Rim;
                float _Grow;
                float _Cells;
                float4 _Ripple0;
                float4 _Ripple1;
                float4 _Ripple2;
                float4 _Ripple3;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 directionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Swelling from the foot: the dome's height grows first, its width a little behind
                float3 p = input.positionOS;
                float grow = saturate(_Grow);
                p.y = (p.y + 0.5) * grow - 0.5;
                p.xz *= lerp(0.6, 1.0, grow);
                output.positionWS = TransformObjectToWorld(p);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = normalize(input.positionOS);
                return output;
            }

            // Distance of a point in a hexagon's own space to the hexagon's middle, in the unit where its edge is at 0.5
            float HexDistance(float2 p)
            {
                p = abs(p);
                return max(dot(p, normalize(float2(1.0, 1.7320508))), p.x);
            }

            // The hexagonal cell grid: x = distance to the nearest cell edge (0 on it, up to 0.5 at the middle)
            float HexEdge(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 hC = floor(float4(p, p - float2(1.0, 0.5)) / s.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + 0.5) * s);
                float2 local = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                return 0.5 - HexDistance(local);
            }

            void Dissolve(float opacity, float2 pixel)
            {
                if (opacity >= 1.0)
                    return;
                uint2 p = uint2(pixel) & 3u;
                const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                clip(opacity - (bayer[p.y * 4u + p.x] + 0.5) / 16.0);
            }

            float Ring(float4 ripple, float3 positionWS)
            {
                if (ripple.w < 0.0)
                    return 0.0;
                float d = distance(positionWS, ripple.xyz);
                float radius = ripple.w * 9.0;
                float life = saturate(1.0 - ripple.w / 0.9);
                return step(abs(d - radius), 0.35 + 0.3 * life) * life;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                // Only the upper half of the dome
                clip(input.directionOS.y + 0.02);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float edgeOn = 1.0 - abs(dot(normalize(input.normalWS), view));
                // Hard bands: the rim where it is seen edge-on, the cell lines inside it, the rest a faint pane
                float rim = step(0.72, edgeOn);
                float2 angles = float2(atan2(input.directionOS.z, input.directionOS.x) * 0.31831 * _Cells * 0.5, input.directionOS.y * _Cells * 0.5);
                float lines = step(HexEdge(angles), 0.045);
                float ripple = saturate(Ring(_Ripple0, input.positionWS) + Ring(_Ripple1, input.positionWS) + Ring(_Ripple2, input.positionWS) + Ring(_Ripple3, input.positionWS));
                float opacity = saturate(0.2 + 0.5 * rim + 0.4 * lines + 0.6 * ripple);
                Dissolve(opacity, input.positionCS.xy);
                float3 colour = _Tone.rgb;
                colour = lerp(colour, _Rim.rgb, saturate(rim + lines * 0.6 + ripple));
                return float4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
