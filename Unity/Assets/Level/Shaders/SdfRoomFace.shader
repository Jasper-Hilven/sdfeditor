// One face of a room (a wall or the ceiling of the chessboard cube) for URP, in the toon look of the ground (SdfGround.shader): the
// same three bands, highlight and light/dark square materials, so the faces read as the floor's own board folded up round the room.
// The face is a flat quad; its squares are drawn here from the world position, so their edges are ruled straight at any distance
// (filtered by the pixel's size, anti-aliased, no noise) and need no geometry or texture.
//
// A square is (a, b) = floor((world - origin) . u / size), floor((world - origin) . v / size) with the face's origin and unit axes from the
// level's room block (level.json "room", x mirrored for Unity); it is dark when a + b + parity is even, where _RoomFace.y is 0 if the
// square (0, 0) is dark and 1 if light. Each square carries the ground material of its colour, with its blotches and speckles in world
// space as on the floor.
//
// Light: the room's directional lamp, hung overhead, seen from the face's own side. A wall only grazes an overhead lamp and the ceiling
// faces away from it, so the light direction used here leans toward the face's normal (_RoomFace.z of the way): the walls then fall in
// the lit and mid bands by how the lamp tilts toward them, and the ceiling in the mid band, as in a lit room. The faces receive no
// cast shadows and cast none (the ceiling would shade the whole room from the lamp). Forward only, depth and depth-normals passes,
// no ink line (as the ground).
Shader "SdfMushrooms/RoomFace"
{
    Properties
    {
        _ToonBands("Bands (shade, lit threshold, softness, occlusion)", Vector) = (0, 0.45, 0.03, 0.5)
        _ToonShadowTone("Shadow tone (linear)", Vector) = (0.55, 0.5, 0.66, 0)
        _ToonMidTone("Mid tone (linear)", Vector) = (0.82, 0.8, 0.85, 0)
        _ToonLitTone("Lit tone (linear)", Vector) = (1.1, 1.1, 1.1, 0)
        _ToonSpecular("Highlight (strength, threshold, max roughness)", Vector) = (0.6, 0.6, 0.75, 0)
        _ToonRim("Rim (strength, width)", Vector) = (0, 0.3, 0, 0)
        _ToonVariation("Colour variation (amount)", Vector) = (0.5, 0, 0, 0)

        // The face: origin (world, metres), the unit directions of its u and v axes, and (square size in metres, parity, lean of the
        // light toward the face's normal, unused)
        _RoomOrigin("Face origin", Vector) = (0, 0, 0, 0)
        _RoomU("Face u axis", Vector) = (1, 0, 0, 0)
        _RoomV("Face v axis", Vector) = (0, 1, 0, 0)
        _RoomFace("Square size, parity, light lean, unused", Vector) = (8, 0, 1, 0)

        // The two square materials (materials/ground/ground_<role>.sdfmat), as the ground takes them
        _LightBase("Light squares base colour (linear)", Vector) = (0, 0, 0, 0)
        _LightVariation("Light variation colour and scale", Vector) = (0, 0, 0, 0)
        _LightSurface("Light roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _LightSpeckle("Light speckle colour and scale", Vector) = (0, 0, 0, 0)
        _LightPattern("Light speckle density and size", Vector) = (0, 0, 0, 0)
        _DarkBase("Dark squares base colour (linear)", Vector) = (0, 0, 0, 0)
        _DarkVariation("Dark variation colour and scale", Vector) = (0, 0, 0, 0)
        _DarkSurface("Dark roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _DarkSpeckle("Dark speckle colour and scale", Vector) = (0, 0, 0, 0)
        _DarkPattern("Dark speckle density and size", Vector) = (0, 0, 0, 0)

        // Only declared because SdfSurface.hlsl, which this shader includes, reads them
        [HideInInspector] _EdgeColor("Edge colour", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DetailAxis("Detail axis", Vector) = (0, 1, 0, 0)
        [HideInInspector] _Subsurface("Subsurface", Vector) = (0, 0.25, 0, 0)
        [HideInInspector] _Look("Look", Vector) = (1, 0.2, 0, 0)
    }

    HLSLINCLUDE

    // WebGL 2 and up: no compute, no geometry shaders
    #pragma target 3.5

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _ToonBands;
        float4 _ToonShadowTone;
        float4 _ToonMidTone;
        float4 _ToonLitTone;
        float4 _ToonSpecular;
        float4 _ToonRim;
        float4 _ToonVariation;
        float4 _RoomOrigin;
        float4 _RoomU;
        float4 _RoomV;
        float4 _RoomFace;
        float4 _LightBase, _LightVariation, _LightSurface, _LightSpeckle, _LightPattern;
        float4 _DarkBase, _DarkVariation, _DarkSurface, _DarkSpeckle, _DarkPattern;
        float4 _EdgeColor;
        float4 _DetailAxis;
        float4 _Subsurface;
        float4 _Look;
    CBUFFER_END

    struct Attributes
    {
        float3 positionOS : POSITION;
        float3 normalOS : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

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
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Mushrooms/Shaders/SdfSurface.hlsl"
            #include "Assets/Mushrooms/Shaders/SdfToon.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // The square material's colour at a point of the world, as SdfGround.shader's SdfGroundColor
            float3 SquareColor(float4 base, float4 variation, float4 surface, float4 speckle, float4 pattern, float3 positionWS)
            {
                float3 varied = SdfVariedColor(base.rgb, positionWS, variation, surface.z, max(surface.w, 0.001), float4(0, 0, 0, 0), float2(0, 0));
                float3 color = lerp(base.rgb, varied, _ToonVariation.x);
                if (speckle.a > 0.0 && pattern.x > 0.0)
                {
                    float2 fleck = SdfSpeckle(positionWS, speckle.a, pattern.x, pattern.y);
                    color = lerp(color, saturate(speckle.rgb * fleck.y), fleck.x);
                }
                return color;
            }

            // 0 where floor(a) + floor(b) is even, 1 where odd, the pixel's footprint averaged: a square wave in each of a and b, filtered
            // over the pixel (a box filter of the wave's integral), so edges are straight and anti-aliased
            float OddSquare(float2 ab)
            {
                float2 w = max(fwidth(ab), 1e-4);
                float2 i = 2.0 * (abs(frac((ab - 0.5 * w) * 0.5) - 0.5) - abs(frac((ab + 0.5 * w) * 0.5) - 0.5)) / w;
                return 0.5 - 0.5 * i.x * i.y;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // The lamp's direction and hue, leaned toward the face's normal (see the top)
                float3 l = float3(0, 1, 0);
                float3 tint = float3(1, 1, 1);
                float brightest = Max3(_MainLightColor.r, _MainLightColor.g, _MainLightColor.b);
                if (brightest > 0.0)
                {
                    l = _MainLightPosition.xyz;
                    tint = _MainLightColor.rgb / brightest;
                }
                l = normalize(l + n * _RoomFace.z);

                float3 offset = input.positionWS - _RoomOrigin.xyz;
                float2 ab = float2(dot(offset, _RoomU.xyz), dot(offset, _RoomV.xyz)) / _RoomFace.x;
                // odd = 1 on squares whose a + b is odd; the square (0, 0) is dark when the parity is 0
                float odd = OddSquare(ab);
                float dark = _RoomFace.y < 0.5 ? 1.0 - odd : odd;

                float3 positionWS = input.positionWS;
                float3 albedo = SquareColor(_LightBase, _LightVariation, _LightSurface, _LightSpeckle, _LightPattern, positionWS);
                float2 surface = _LightSurface.xy;
                if (dark > 0.001)
                {
                    float3 darkColor = SquareColor(_DarkBase, _DarkVariation, _DarkSurface, _DarkSpeckle, _DarkPattern, positionWS);
                    albedo = lerp(albedo, darkColor, dark);
                    surface = lerp(surface, _DarkSurface.xy, dark);
                }

                float3 color = SdfToonShade(albedo, n, v, l, 1.0, tint, surface.x, surface.y, 0.0, 1.0, 0.0);
                return SdfToonOutput(SkyFade(color, input.positionWS), normalize(input.normalWS), input.positionWS);
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
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

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
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float Frag(Varyings input) : SV_Target0
            {
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
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

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
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target0
            {
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
