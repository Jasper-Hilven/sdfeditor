// The ink line around a toon mushroom: the inverted hull. MushroomBuilder gives each part a second renderer with this material on the
// same mesh; it draws only the back faces, pushed out along the normal by a fixed number of pixels, so they peek out around the
// silhouette and wherever the part passes in front of something further away (eyes and hands over the body, the cap over the stem).
// The line takes the colour of the surface under it, darkened (_OutlineTone), so each mushroom keeps its own palette.
// It cross-fades with its part between levels of detail (SdfLodFade.hlsl); MushroomBuilder leaves it off the coarsest level.
// Keep in step with the toon branch of the IDE's preview (IDE/wwwroot/js/sdf-renderer.js).
Shader "SdfMushrooms/Toon Outline"
{
    Properties
    {
        _OutlineWidth("Width (pixels at 1080p)", Float) = 4
        _OutlineTone("Tone (multiplies the base colour, linear)", Vector) = (0.18, 0.13, 0.14, 0)
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    // Both passes only transform the mesh; neither depends on the pass
    #define SHADERPASS SHADERPASS_FORWARD_UNLIT
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "SdfLodFade.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _OutlineWidth;
        float4 _OutlineTone;
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
        float3 color : TEXCOORD0;
    };

    // Pushes the vertex out along its normal as seen on screen, by the same number of pixels at any distance
    // (scaled with the screen height so lines keep their weight at any resolution)
    Varyings OutlineVert(Attributes input)
    {
        Varyings output;
        float3 positionRWS = TransformObjectToWorld(input.positionOS);
        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        float4 positionCS = TransformWorldToHClip(positionRWS);
        float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
        float2 normalPixels = normalCS * _ScreenSize.xy;
        float length2 = dot(normalPixels, normalPixels);
        if (length2 > 1e-12)
        {
            float pixels = _OutlineWidth * _ScreenSize.y / 1080.0;
            positionCS.xy += normalPixels * rsqrt(length2) * pixels * 2.0 * _ScreenSize.zw * positionCS.w;
        }
        output.positionCS = positionCS;
        output.color = input.color.rgb;
        return output;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Stencil
            {
                WriteMask 8
                Ref 0
                Comp Always
                Pass Replace
            }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes input) { return OutlineVert(input); }

            #ifdef WRITE_MSAA_DEPTH
            float4 Frag(Varyings input) : SV_Target0
            {
                SdfLodFade(input.positionCS);
                return float4(input.positionCS.z, input.positionCS.z, input.positionCS.z, 1.0);
            }
            #else
            void Frag(Varyings input)
            {
                SdfLodFade(input.positionCS);
            }
            #endif
            ENDHLSL
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Stencil
            {
                WriteMask 3
                Ref 0
                Comp Always
                Pass Replace
            }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma vertex Vert
            #pragma fragment Frag

            Varyings Vert(Attributes input) { return OutlineVert(input); }

            float4 Frag(Varyings input) : SV_Target0
            {
                SdfLodFade(input.positionCS);
                return float4(input.color * _OutlineTone.rgb * _DeExposureMultiplier, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
