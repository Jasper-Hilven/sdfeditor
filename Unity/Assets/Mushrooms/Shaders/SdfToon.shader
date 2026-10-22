// Cartoon shading of a meshed SDF model for URP: flat colour in three bands (shadow, mid, lit) from the sun and its cascaded shadows,
// a hard highlight and a rim light. The outline is a second renderer with SdfToonOutline.shader. The vertex data is packed as
// SdfSurface.hlsl describes, the same as the IDE's preview reads it.
//
// Written by hand rather than as a Shader Graph so it can light itself: it draws in URP's forward pass whichever path the renderer
// takes (UniversalForwardOnly), reads the main light and its shadows, and has the passes URP needs to put it in shadow maps and in the
// depth (and depth-normals) prepass. Every pass takes part in LOD cross-fade (URP's LODCrossFade.hlsl, a dither on the pixel's screen
// position), so the levels of a model's LOD group dissolve into each other in colour, depth and shadow alike, and the outline drops
// the same pixels as its surface. Every pass also supports GPU instancing, for meshes drawn many times with
// Graphics.RenderMeshInstanced; the instanced renderer's variant (SDF_SCATTER, see SdfScatter.hlsl) adds wind, a tint at the base and
// a dithered fade between its rings of distance.
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
        _ToonInk("Ink of the patterns' lines (the colour times this, linear)", Vector) = (0.18, 0.13, 0.14, 0)

        // Only declared because SdfSurface.hlsl, which this shader includes, reads them: the toon look takes everything per vertex,
        // so one material draws every mushroom
        [HideInInspector] _EdgeColor("Edge colour", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DetailAxis("Detail axis", Vector) = (0, 1, 0, 0)
        [HideInInspector] _Subsurface("Subsurface", Vector) = (0, 0.25, 0, 0)
        [HideInInspector] _Look("Look", Vector) = (1, 0.2, 0, 0)
    }

    HLSLINCLUDE

    // WebGL 2 and up: no compute, no geometry shaders
    #pragma target 3.5

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #if defined(LOD_FADE_CROSSFADE)
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
    #endif
    #pragma multi_compile_local _ SDF_SCATTER
    #include "SdfScatter.hlsl"

    // One constant buffer for every pass, so the SRP batcher can draw them
    CBUFFER_START(UnityPerMaterial)
        float4 _ToonBands;
        float4 _ToonShadowTone;
        float4 _ToonMidTone;
        float4 _ToonLitTone;
        float4 _ToonSpecular;
        float4 _ToonRim;
        float4 _ToonVariation;
        float4 _ToonInk;
        float4 _EdgeColor;
        float4 _DetailAxis;
        float4 _Subsurface;
        float4 _Look;
    CBUFFER_END

    // Drops this pixel from one of the two levels a LOD group is fading between, or the instanced renderer's rings are; nothing without
    // the keywords
    void SdfLodFade(float4 positionCS)
    {
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(positionCS);
    #endif
        SdfScatterFade(positionCS);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SdfSurface.hlsl"
            #include "SdfToon.hlsl"
            #include "SdfPattern.hlsl"
            #include "SdfStaticShadow.hlsl"
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
                float4 uv7 : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float4 uv0 : TEXCOORD3;
                float4 uv1 : TEXCOORD4;
                float4 uv2 : TEXCOORD5;
                float4 uv3 : TEXCOORD6;
                float4 uv4 : TEXCOORD7;
                float2 uv5 : TEXCOORD8;
                float4 uv6 : TEXCOORD9;
                float4 uv7 : TEXCOORD10;
                // The pixel's position and normal in the part's own space, for the pattern (see SdfPattern.hlsl)
                float3 positionOS : TEXCOORD11;
                float3 normalOS : TEXCOORD12;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                SdfScatterTint(input.positionOS, input.color, input.uv3, input.uv4, input.uv6);
                input.positionOS = SdfScatterWind(input.positionOS);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.uv2 = input.uv2;
                output.uv3 = input.uv3;
                output.uv4 = input.uv4;
                output.uv5 = input.uv5;
                output.uv6 = input.uv6;
                output.uv7 = input.uv7;
                output.positionOS = input.positionOS;
                output.normalOS = input.normalOS;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // The main light (the sun) with its cascaded shadows, faded out towards the shadow distance; a white light from above
                // without any
                float3 l = float3(0, 1, 0);
                float3 tint = float3(1, 1, 1);
                float shadow = 1.0;
                float brightest = Max3(_MainLightColor.r, _MainLightColor.g, _MainLightColor.b);
                if (brightest > 0.0)
                {
                    l = _MainLightPosition.xyz;
                    tint = _MainLightColor.rgb / brightest;
                    if (dot(n, l) > 0.0)
                    {
                        // The level's fixed sun shadow where there is one (SdfStaticShadow.hlsl), else the light's cascades
                        if (SdfStaticShadowOn())
                            shadow = SdfStaticShadow(input.positionWS);
                        else
                        {
                            Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                            shadow = light.shadowAttenuation;
                        }
                    }
                }

                float3 albedo = SdfToonColor(input.color, input.uv1, input.uv2, input.uv3, input.uv4, float4(input.uv5, 0, 0), input.uv6);
                // The pattern, in the part's own space (each instance's, under GPU instancing)
                SdfPattern(input.uv7, input.positionOS, normalize(input.normalOS), input.positionWS,
                    distance(input.positionWS, GetCameraPositionWS()), albedo, n);
                float3 color = SdfToonShade(albedo, n, v, l, shadow, tint, input.uv0.x, input.uv0.y, input.color.a, saturate(input.uv0.w),
                    saturate(input.uv6.a));
                // The colour is shown as it is: the light's intensity does not scale it, only tone mapping follows; far away it fades
                // toward the sky
                return SdfToonOutput(SkyFade(color, input.positionWS), normalize(input.normalWS), input.positionWS);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            // Set by URP for the light being rendered: the normal bias pushes the surface in along its normal, away from the light
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                input.positionOS = SdfScatterWind(input.positionOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection)));
                return output;
            }

            void Frag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
            }
            ENDHLSL
        }

        // Depth for URP's prepass (when the camera needs a depth texture or the renderer primes depth)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                input.positionOS = SdfScatterWind(input.positionOS);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Depth and normals for URP's prepass when an effect needs them (screen-space ambient occlusion, for one)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                input.positionOS = SdfScatterWind(input.positionOS);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                return float4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5)), 0.0);
            #else
                return float4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
