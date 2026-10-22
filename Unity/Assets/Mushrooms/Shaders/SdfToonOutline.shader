// The ink line around a toon mushroom: the inverted hull. MushroomBuilder gives each part a second renderer with this material on the
// same mesh; it draws only the back faces, pushed out along the normal by a fixed number of pixels, so they peek out around the
// silhouette and wherever the part passes in front of something further away (eyes and hands over the body, the cap over the stem).
// The line takes the colour of the surface under it, darkened (_OutlineTone), so each mushroom keeps its own palette.
// It cross-fades with its part between levels of detail (URP's dither on the pixel's screen position, as SdfToon.shader); MushroomBuilder
// leaves it off the coarsest level. Drawn by the instanced renderer it sways, takes the tint and fades exactly as its surface does
// (SdfScatter.hlsl). It casts no shadow, and is in the depth prepasses so depth priming does not drop it.
// Keep in step with the toon branch of the IDE's preview (IDE/wwwroot/js/sdf-renderer.js).
Shader "SdfMushrooms/Toon Outline"
{
    Properties
    {
        _OutlineWidth("Width (pixels at 1080p)", Float) = 4
        _OutlineTone("Tone (multiplies the base colour, linear)", Vector) = (0.18, 0.13, 0.14, 0)
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
#include "SdfComic.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _OutlineWidth;
        float4 _OutlineTone;
    CBUFFER_END

    struct Attributes
    {
        float3 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 color : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float3 positionWS : TEXCOORD2;
        half fogFactor : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    // Pushes the vertex out along its normal as seen on screen, by the same number of pixels at any distance
    // (scaled with the screen height so lines keep their weight at any resolution or render scale)
    Varyings OutlineVert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        SdfScatterTintColor(input.positionOS, input.color);
        input.positionOS = SdfScatterWind(input.positionOS);
        float3 positionWS = TransformObjectToWorld(input.positionOS);
        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        float4 positionCS = TransformWorldToHClip(positionWS);
        float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
        float2 normalPixels = normalCS * _ScreenSize.xy;
        float length2 = dot(normalPixels, normalPixels);
        if (length2 > 1e-12)
        {
            float pixels = _OutlineWidth * _ScreenSize.y / 1080.0 * lerp(1.0, _SdfComic.z, step(0.5, _SdfComic.x));
            positionCS.xy += normalPixels * rsqrt(length2) * pixels * 2.0 * _ScreenSize.zw * positionCS.w;
        }
        output.positionCS = positionCS;
        output.color = input.color.rgb;
        output.normalWS = normalWS;
        output.positionWS = positionWS;
        output.fogFactor = ComputeFogFactor(positionCS.z);
        return output;
    }

    // Drops this pixel from one of the two levels a LOD group is fading between, the same pixels as the surface's; nothing without
    // the keyword
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

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            #include "SdfSky.hlsl"

            Varyings Vert(Attributes input) { return OutlineVert(input); }

            // Faded toward the sky and into the fog like its surface, so far lines melt into the haze with it
            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                // The aux pass of the story's panels (SdfComic.hlsl) has no outline: the post pass draws its own ink
                clip(1.5 - _SdfComic.x);
                return float4(MixFog(SkyFade(input.color * _OutlineTone.rgb, input.positionWS), input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Front
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes input) { return OutlineVert(input); }

            float Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes input) { return OutlineVert(input); }

            // The back faces' normals turned round, towards the camera like the surface's
            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                float3 normalWS = -normalize(input.normalWS);
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
