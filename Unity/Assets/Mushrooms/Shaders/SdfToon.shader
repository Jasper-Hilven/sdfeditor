// Cartoon shading of a meshed SDF model for HDRP: flat colour in three bands (shadow, mid, lit) from the sun and its cascaded
// shadows, a hard highlight and a rim light. The outline is a second renderer with SdfToonOutline.shader. The vertex data is the same
// as for the realistic SdfSurface.shadergraph (see SdfSurface.hlsl), so MushroomBuilder can give a prefab either look.
//
// Written by hand rather than as a Shader Graph because HDRP's graphs cannot replace its lighting. It draws like HDRP's own Unlit
// (ForwardOnly, colour not scaled by exposure) but lights itself, and has the passes HDRP needs to put it in the depth prepass and in
// shadow maps.
Shader "SdfMushrooms/Toon"
{
    Properties
    {
        _ToonBands("Bands (shade, lit threshold, softness, occlusion)", Vector) = (0, 0.45, 0.03, 0.5)
        _ToonShadowTone("Shadow tone (linear)", Vector) = (0.55, 0.5, 0.66, 0)
        _ToonMidTone("Mid tone (linear)", Vector) = (0.82, 0.8, 0.85, 0)
        _ToonLitTone("Lit tone (linear)", Vector) = (1.1, 1.1, 1.1, 0)
        _ToonSpecular("Highlight (strength, threshold, max roughness)", Vector) = (0.6, 0.6, 0.75, 0)
        _ToonRim("Rim (strength, width)", Vector) = (0.25, 0.3, 0, 0)
        _ToonVariation("Colour variation (amount)", Vector) = (0.5, 0, 0, 0)

        // Only declared because SdfSurface.hlsl, which this shader includes, reads them: the toon look takes everything per vertex,
        // so one material draws every mushroom
        [HideInInspector] _EdgeColor("Edge colour", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DetailAxis("Detail axis", Vector) = (0, 1, 0, 0)
        [HideInInspector] _Subsurface("Subsurface", Vector) = (0, 0.25, 0, 0)
        [HideInInspector] _Look("Look", Vector) = (1, 0.2, 0, 0)
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

    // One constant buffer for every pass, so the SRP batcher can draw them
    CBUFFER_START(UnityPerMaterial)
        float4 _ToonBands;
        float4 _ToonShadowTone;
        float4 _ToonMidTone;
        float4 _ToonLitTone;
        float4 _ToonSpecular;
        float4 _ToonRim;
        float4 _ToonVariation;
        float4 _EdgeColor;
        float4 _DetailAxis;
        float4 _Subsurface;
        float4 _Look;
    CBUFFER_END

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Depth (and, with MSAA, depth as colour) for HDRP's prepass, which every opaque forward-only object must be in
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            // As HDRP's Unlit: clear the "trace reflection ray" bit
            Stencil
            {
                WriteMask 8
                Ref 0
                Comp Always
                Pass Replace
            }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                return output;
            }

            #ifdef WRITE_MSAA_DEPTH
            float4 Frag(Varyings input) : SV_Target0
            {
                return float4(input.positionCS.z, input.positionCS.z, input.positionCS.z, 1.0);
            }
            #else
            void Frag(Varyings input) {}
            #endif
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            // As HDRP's Unlit: no deferred lighting or subsurface scattering on these pixels
            Stencil
            {
                WriteMask 3
                Ref 0
                Comp Always
                Pass Replace
            }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            // Only the sun's shadows are sampled; these just satisfy HDRP's shadow code
            #define PUNCTUAL_SHADOW_MEDIUM
            #define AREA_SHADOW_MEDIUM
            #define LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
            #define SHADERPASS SHADERPASS_FORWARD_UNLIT

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
            #include "SdfSurface.hlsl"
            #include "SdfToon.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float4 uv4 : TEXCOORD4;
                float2 uv5 : TEXCOORD5;
                float4 uv6 : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float4 uv0 : TEXCOORD3;
                float4 uv1 : TEXCOORD4;
                float4 uv2 : TEXCOORD5;
                float4 uv3 : TEXCOORD6;
                float4 uv4 : TEXCOORD7;
                float2 uv5 : TEXCOORD8;
                float4 uv6 : TEXCOORD9;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionRWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.uv2 = input.uv2;
                output.uv3 = input.uv3;
                output.uv4 = input.uv4;
                output.uv5 = input.uv5;
                output.uv6 = input.uv6;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionRWS);

                // The main directional light: the one with shadows, else the first; a white light from above without any
                float3 l = float3(0, 1, 0);
                float3 tint = float3(1, 1, 1);
                float shadow = 1.0;
                if (_DirectionalLightCount > 0)
                {
                    uint index = _DirectionalShadowIndex >= 0 ? (uint)_DirectionalShadowIndex : 0;
                    DirectionalLightData light = _DirectionalLightDatas[index];
                    l = -light.forward;
                    tint = light.color / max(Max3(light.color.r, light.color.g, light.color.b), 1e-6);
                    if (_DirectionalShadowIndex >= 0 && light.shadowDimmer > 0.0 && dot(n, l) > 0.0)
                    {
                        HDShadowContext shadowContext = InitShadowContext();
                        float attenuation = GetDirectionalShadowAttenuation(shadowContext, input.positionCS.xy, input.positionRWS, n,
                            light.shadowIndex, l);
                        shadow = lerp(1.0, attenuation, light.shadowDimmer);
                    }
                }

                float3 albedo = SdfToonColor(input.color, input.uv1, input.uv2, input.uv3, input.uv4, float4(input.uv5, 0, 0), input.uv6);
                float3 color = SdfToonShade(albedo, n, v, l, shadow, tint, input.uv0.x, input.uv0.y, input.color.a, saturate(input.uv0.w));
                // Like HDRP's Unlit: the colour is shown as it is, not scaled by the exposure
                return float4(color * _DeExposureMultiplier, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZClip [_ZClip]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                return output;
            }

            void Frag(Varyings input) {}
            ENDHLSL
        }
    }

    Fallback Off
}
