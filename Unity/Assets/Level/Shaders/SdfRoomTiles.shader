// One face of a tiled room (the bathroom's walls and ceiling; ticket 29.1) for URP, in the toon look of the other room faces
// (SdfRoomPlanks.shader, SdfRoomFace.shader): the same three bands and highlight, drawn from the world position (no UVs, no textures),
// so the grout lines are ruled straight at any distance (anti-aliased by the pixel's size) and cost no geometry.
//
// A point of the face is (a, b) = ((world - origin) . u, (world - origin) . v) in metres, with the face's origin and unit axes from the
// level's room block (level.json "room", x mirrored for Unity). What is drawn on it:
//   - tiles _Tile.x metres square in a checker of two colours (_TileBase, _TileAlt), each with its own faint tone step (_Tile.y), and a
//     grout line (_TileGrout, _Tile.z metres wide each side of a join) between them; a tile size of 0 leaves the face one plain colour
//     (the ceiling), and _Tile.w scales the colours (the toon bands darken a white face into grey, so a white room asks for a little over 1).
//     Where the tiles get smaller than a few pixels the checker and the grout melt into their average, so they do not shimmer.
// The look of a room is chosen in LevelRoom (by the material's name), which sets these.
//
// Light: the room's directional lamp seen from the face's own side, leaned toward the face's normal (_RoomExtent.w of the way) as in
// SdfRoomFace.shader. No cast shadows, none received. Fog like the toon shaders (the bathroom's haze). Forward only, depth and
// depth-normals passes, no ink outline pass.
Shader "SdfMushrooms/RoomTiles"
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

        // The face: origin (world, metres), the unit directions of its u and v axes, and (length along u, length along v, unused,
        // lean of the light toward the face's normal)
        _RoomOrigin("Face origin", Vector) = (0, 0, 0, 0)
        _RoomU("Face u axis", Vector) = (1, 0, 0, 0)
        _RoomV("Face v axis", Vector) = (0, 1, 0, 0)
        _RoomExtent("Length u, length v, unused, light lean", Vector) = (10, 10, 1, 1)

        // The tiles (the base and the grout are the material's own values: materials/room/<name>.sdfmat; the second tile is the look's)
        _TileBase("Tile colour (linear)", Vector) = (1, 1, 1, 0)
        _TileAlt("Second tile colour (linear)", Vector) = (1, 1, 1, 0)
        _TileGrout("Grout colour (linear)", Vector) = (0, 0, 0, 0)
        _TileSurface("Roughness, specular, unused, unused", Vector) = (0.5, 0.4, 0, 0)
        _Tile("Tile size, tone step, grout half width, brightness", Vector) = (5, 0.02, 0.06, 1)

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
        float4 _RoomExtent;
        float4 _TileBase, _TileAlt, _TileGrout, _TileSurface;
        float4 _Tile;
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
            #pragma multi_compile_fog
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
                half fogFactor : TEXCOORD2;
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
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float n)
            {
                return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
            }

            // 1 inside a line of the given half width (metres) round a signed distance of 0, anti-aliased over the pixel's size in metres
            float RuledLine(float gap, float halfWidth, float pixel)
            {
                return 1.0 - smoothstep(halfWidth - pixel, halfWidth + pixel, abs(gap));
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // The lamp's direction and hue, leaned toward the face's normal
                float3 l = float3(0, 1, 0);
                float3 tint = float3(1, 1, 1);
                float brightest = Max3(_MainLightColor.r, _MainLightColor.g, _MainLightColor.b);
                if (brightest > 0.0)
                {
                    l = _MainLightPosition.xyz;
                    tint = _MainLightColor.rgb / brightest;
                }
                l = normalize(l + n * _RoomExtent.w);

                float3 albedo = _TileBase.rgb;
                if (_Tile.x > 0.0)
                {
                    float3 offset = input.positionWS - _RoomOrigin.xyz;
                    float2 p = float2(dot(offset, _RoomU.xyz), dot(offset, _RoomV.xyz));
                    // The pixel's size in metres along each axis
                    float2 pixel = max(fwidth(p), 1e-4);
                    float pixelMax = max(pixel.x, pixel.y);

                    // The tile and its colour in the checker, with its own faint tone step
                    float2 cell = floor(p / _Tile.x);
                    float second = abs(fmod(cell.x + cell.y, 2.0));
                    albedo = lerp(_TileBase.rgb, _TileAlt.rgb, second);
                    albedo *= 1.0 + (Hash(cell.x * 57.0 + cell.y * 131.0) - 0.5) * 2.0 * _Tile.y;

                    // The grout: a line either side of every join
                    float2 across = p - cell * _Tile.x;
                    float grout = saturate(RuledLine(across.x, _Tile.z, pixel.x) + RuledLine(across.x - _Tile.x, _Tile.z, pixel.x)
                                           + RuledLine(across.y, _Tile.z, pixel.y) + RuledLine(across.y - _Tile.x, _Tile.z, pixel.y));

                    // Far away, where a tile is only a few pixels, checker and grout melt into their average
                    float far = smoothstep(0.1, 0.4, pixelMax / _Tile.x);
                    float3 average = lerp((_TileBase.rgb + _TileAlt.rgb) * 0.5, _TileGrout.rgb, 0.12);
                    albedo = lerp(albedo, average, far);
                    grout *= 1.0 - far;
                    albedo = lerp(albedo, _TileGrout.rgb, grout);
                }
                albedo *= _Tile.w;

                float3 color = SdfToonShade(albedo, n, v, l, 1.0, tint, _TileSurface.x, _TileSurface.y, 0.0, 1.0, 0.0);
                color = MixFog(SkyFade(color, input.positionWS), input.fogFactor);
                return float4(color, 1.0);
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
