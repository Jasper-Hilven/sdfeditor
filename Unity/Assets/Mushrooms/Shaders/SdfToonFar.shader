// The sky's far layer: toon models that hang in the sky (the eye moon, floating caps), drawn as if infinitely far away. Their objects are
// laid out round the origin, which stands for the camera: the vertex stage adds the camera's position, so they keep their direction and
// size wherever the player goes, and squeezes their depth into a thin slab just inside the far plane, keeping their order among
// themselves (the lids behind the eye stay behind it) while the level is always in front of them; the camera's far plane (400 m) need
// not reach them. They draw after the skybox (URP draws it after opaque geometry), in the transparent queue.
//
// Shaded like SdfToon.shader with the light at the eye, so they are lit full-face, and the sky's tone (SdfSky.hlsl), plus their own glow
// (_FarGlow), which takes them above 1 for bloom. No shadows, no outline, no fade: the sky does not fade into itself.
Shader "SdfMushrooms/Toon Far"
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
        _FarGlow("Glow (the colour times 1 + this)", Float) = 0
        _FarDepth("Distances from the camera the depth slab spans (metres)", Vector) = (60, 140, 0, 0)

        [HideInInspector] _EdgeColor("Edge colour", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DetailAxis("Detail axis", Vector) = (0, 1, 0, 0)
        [HideInInspector] _Subsurface("Subsurface", Vector) = (0, 0.25, 0, 0)
        [HideInInspector] _Look("Look", Vector) = (1, 0.2, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-400" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            // WebGL 2 and up: no compute, no geometry shaders
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ToonBands;
                float4 _ToonShadowTone;
                float4 _ToonMidTone;
                float4 _ToonLitTone;
                float4 _ToonSpecular;
                float4 _ToonRim;
                float4 _ToonVariation;
                float _FarGlow;
                float4 _FarDepth;
                float4 _EdgeColor;
                float4 _DetailAxis;
                float4 _Subsurface;
                float4 _Look;
            CBUFFER_END

            #include "SdfSurface.hlsl"
            #include "SdfToon.hlsl"
            #include "SdfPacked.hlsl"

            struct Attributes
            {
                float4 positionPacked : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float4 uv4 : TEXCOORD4;
                float3 uv5 : TEXCOORD5;
                float4 uv6 : TEXCOORD6;
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
                float3 uv5 : TEXCOORD8;
                float4 uv6 : TEXCOORD9;
            };

            Varyings Vert(Attributes input)
            {
                float3 positionOS = SdfUnpackPosition(input.positionPacked);
                Varyings output;
                // Laid out round the origin: the camera carries it along
                output.positionWS = TransformObjectToWorld(positionOS) + _WorldSpaceCameraPos;
                float4 positionCS = TransformWorldToHClip(output.positionWS);
                // Into the slab just inside the far plane, nearer in front, whichever way the platform's depth runs. Reversed (float)
                // depth puts the level's last few metres before 400 m very close to 0, so the slab is far thinner there than with
                // OpenGL's (WebGL's) 24-bit depth, where it still has hundreds of steps
                float along = saturate((length(output.positionWS - _WorldSpaceCameraPos) - _FarDepth.x) / max(_FarDepth.y - _FarDepth.x, 1e-3));
            #if UNITY_REVERSED_Z
                positionCS.z = positionCS.w * (1e-8 + 1e-6 * (1.0 - along));
            #else
                positionCS.z = positionCS.w * (1.0 - 1e-6 - 1e-4 * (1.0 - along));
            #endif
                output.positionCS = positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.uv0 = input.uv0;
                output.uv1 = SdfUnpackLocal(input.uv1, input.positionPacked);
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
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 albedo = SdfToonColor(input.color, input.uv1, input.uv2, input.uv3, input.uv4, float4(input.uv5.xy, 0, 0), input.uv6);
                // Lit from the eye, unshadowed, in white
                float3 color = SdfToonShade(albedo, n, v, v, 1.0, float3(1, 1, 1), input.uv0.x, input.uv0.y, input.color.a,
                    saturate(input.uv0.w), saturate(input.uv6.a));
                color = SdfToonEmit(color, albedo, input.uv5.z);
                return SdfToonOutput(color * (1.0 + _FarGlow), n, input.positionWS);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
