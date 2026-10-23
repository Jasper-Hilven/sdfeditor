// One face of a room that is not a chessboard (a wall or the ceiling of the toy box, kitchen, ...; ticket 27.1) for URP, in the toon look
// of the ground (SdfGround.shader, SdfRoomFace.shader): the same three bands, highlight and fog-free colours. The face is a flat quad;
// everything on it is drawn here from the world position (no UVs, no textures), so its lines are ruled straight at any distance
// (anti-aliased by the pixel's size) and cost no geometry.
//
// A point of the face is (a, b) = ((world - origin) . u, (world - origin) . v) in metres, with the face's origin and unit axes from the
// level's room block (level.json "room", x mirrored for Unity); the face is _RoomExtent.xy metres along u and v. What is drawn on it:
//   - boards: strips _Planks.x metres wide along u (they run along v), each with its own tone step, a dark seam between them (the
//     material's edge colour, _Planks.z metres wide each side of the join) and the material's patches and speckles for knots;
//   - a frieze (walls): a band from _Frieze.x to _Frieze.y metres up in which a stencilled alphabet A-Z runs along the face, one block
//     letter per cell _Frieze.z metres wide and _Frieze.w tall, in four paint colours in turn (_FriezeColor0..3); the letters read
//     left to right seen from inside the room (_RoomExtent.z is 1 where u runs to the viewer's right, else -1);
//   - dovetail joints (walls): at both ends of the face, tails _Dovetail.x metres long in rows _Dovetail.y metres high, outlined in ink;
//     _Dovetail.z / .w say which rows (even or odd, 0 or 1) have a tail at the start / far end, so that two walls meeting at a corner
//     take opposite rows and interlock;
//   - battens (ceiling): two boards _Battens.z metres wide across the planks at the fractions _Battens.x and .y of the face's length
//     along v, outlined in ink (a fraction below 0 leaves one out).
// Anything with a zero size is left out. The look of a room is chosen in LevelRoom (by the material's name), which sets these.
//
// Light: the room's directional lamp seen from the face's own side, leaned toward the face's normal (_RoomExtent.w of the way) as in
// SdfRoomFace.shader. No cast shadows, none received. Forward only, depth and depth-normals passes, no ink outline pass.
Shader "SdfMushrooms/RoomPlanks"
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

        // The face: origin (world, metres), the unit directions of its u and v axes, and (length along u, length along v, text direction,
        // lean of the light toward the face's normal)
        _RoomOrigin("Face origin", Vector) = (0, 0, 0, 0)
        _RoomU("Face u axis", Vector) = (1, 0, 0, 0)
        _RoomV("Face v axis", Vector) = (0, 1, 0, 0)
        _RoomExtent("Length u, length v, text direction, light lean", Vector) = (10, 10, 1, 1)

        // The boards (the material's own values: materials/room/<name>.sdfmat)
        _WoodBase("Base colour (linear)", Vector) = (0, 0, 0, 0)
        _WoodVariation("Variation colour and scale", Vector) = (0, 0, 0, 0)
        _WoodSurface("Roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _WoodSpeckle("Speckle colour and scale", Vector) = (0, 0, 0, 0)
        _WoodPattern("Speckle density and size", Vector) = (0, 0, 0, 0)
        _WoodEdge("Seam colour (linear)", Vector) = (0, 0, 0, 0)

        _Planks("Board width, tone step, seam half width, unused", Vector) = (6, 0.08, 0.1, 0)
        _Battens("Fractions along v (below 0 none), width, unused", Vector) = (-1, -1, 3, 0)
        _Frieze("Bottom, top, cell width, letter height", Vector) = (0, 0, 3, 2.4)
        _FriezeColor0("Frieze paint 0 (linear)", Vector) = (1, 1, 1, 0)
        _FriezeColor1("Frieze paint 1 (linear)", Vector) = (1, 1, 1, 0)
        _FriezeColor2("Frieze paint 2 (linear)", Vector) = (1, 1, 1, 0)
        _FriezeColor3("Frieze paint 3 (linear)", Vector) = (1, 1, 1, 0)
        _Dovetail("Tail length, row height, start parity, far parity", Vector) = (0, 4, 0, 1)

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
        float4 _WoodBase, _WoodVariation, _WoodSurface, _WoodSpeckle, _WoodPattern, _WoodEdge;
        float4 _Planks;
        float4 _Battens;
        float4 _Frieze;
        float4 _FriezeColor0, _FriezeColor1, _FriezeColor2, _FriezeColor3;
        float4 _Dovetail;
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
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // The stencilled capitals A-Z as 5 x 5 pixel glyphs, row-major from the top row: bit (row * 5 + column) is set where the glyph is painted
            static const uint kGlyphs[26] =
            {
                0x118FE2Eu, 0x0F8BE2Fu, 0x1E0843Eu, 0x0F8C62Fu, 0x1F0BC3Fu, 0x010BC3Fu, 0x0E8E43Eu, 0x118FE31u, 0x1F2109Fu, 0x064A11Cu,
                0x1149D31u, 0x1F08421u, 0x118D771u, 0x11CD671u, 0x0E8C62Eu, 0x010BE2Fu, 0x164D62Eu, 0x114BE2Fu, 0x0F8383Eu, 0x042109Fu,
                0x0E8C631u, 0x0454631u, 0x11DD631u, 0x1151151u, 0x0421151u, 0x1F1111Fu
            };

            float Hash(float n)
            {
                return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
            }

            // The board material's colour at a point of the world, as SdfRoomFace.shader's SquareColor
            float3 WoodColor(float3 positionWS)
            {
                float3 varied = SdfVariedColor(_WoodBase.rgb, positionWS, _WoodVariation, _WoodSurface.z, max(_WoodSurface.w, 0.001), float4(0, 0, 0, 0), float2(0, 0));
                float3 color = lerp(_WoodBase.rgb, varied, _ToonVariation.x);
                if (_WoodSpeckle.a > 0.0 && _WoodPattern.x > 0.0)
                {
                    float2 fleck = SdfSpeckle(positionWS, _WoodSpeckle.a, _WoodPattern.x, _WoodPattern.y);
                    color = lerp(color, saturate(_WoodSpeckle.rgb * fleck.y), fleck.x);
                }
                return color;
            }

            // 1 inside a line of the given half width (metres) round a signed distance of 0, anti-aliased over the pixel's size in metres
            float RuledLine(float gap, float halfWidth, float pixel)
            {
                return 1.0 - smoothstep(halfWidth - pixel, halfWidth + pixel, abs(gap));
            }

            // 1 where the signed distance (metres) is negative, anti-aliased
            float Inside(float gap, float pixel)
            {
                return 1.0 - smoothstep(-pixel, pixel, gap);
            }

            float3 FriezeColor(uint index)
            {
                uint k = index & 3u;
                return k == 0u ? _FriezeColor0.rgb : k == 1u ? _FriezeColor1.rgb : k == 2u ? _FriezeColor2.rgb : _FriezeColor3.rgb;
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

                float3 offset = input.positionWS - _RoomOrigin.xyz;
                float2 p = float2(dot(offset, _RoomU.xyz), dot(offset, _RoomV.xyz));
                // The pixel's size in metres along each axis
                float2 pixel = max(fwidth(p), 1e-4);
                float pixelMax = max(pixel.x, pixel.y);

                float3 albedo = WoodColor(input.positionWS);

                // Boards: their tone steps and seams. The ceiling's battens cover the seams under them
                float board = floor(p.x / _Planks.x);
                albedo *= 1.0 + (Hash(board) - 0.5) * 2.0 * _Planks.y;
                // Grain: faint streaks along the board
                albedo *= 1.0 + (Hash(board * 7.0 + floor(p.x * 5.0)) - 0.5) * _Planks.y * 0.6;
                float across = p.x - board * _Planks.x;
                float seam = RuledLine(across, _Planks.z, pixel.x) + RuledLine(across - _Planks.x, _Planks.z, pixel.x);
                // Fade the seam out where it is thinner than a pixel, so it does not shimmer, towards the average of seam and board
                float seamFade = saturate(_Planks.z * 2.0 / max(pixel.x * 2.0, 1e-4));
                float seamMask = saturate(seam) * lerp(0.35, 1.0, seamFade);
                float3 ink = _WoodEdge.rgb;

                // Frieze: the stencilled alphabet in a band
                float bandHeight = _Frieze.y - _Frieze.x;
                if (bandHeight > 0.0)
                {
                    float reading = _RoomExtent.z > 0.0 ? p.x : _RoomExtent.x - p.x;
                    float cellWidth = _Frieze.z;
                    float letterHeight = _Frieze.w;
                    float pixelSize = letterHeight / 5.0;
                    float cell = floor(reading / cellWidth);
                    float inCell = reading - cell * cellWidth - (cellWidth - 5.0 * pixelSize) * 0.5;
                    float fromTop = (_Frieze.x + bandHeight * 0.5 + letterHeight * 0.5 - p.y) / pixelSize;
                    float col = floor(inCell / pixelSize);
                    float row = floor(fromTop);
                    float lettered = 0.0;
                    if (col >= 0.0 && col < 5.0 && row >= 0.0 && row < 5.0 && cell >= 0.0)
                    {
                        uint index = (uint)cell;
                        uint glyph = kGlyphs[index % 26u];
                        lettered = (float)((glyph >> (uint)(row * 5.0 + col)) & 1u);
                        // Letters thinner than a pixel on screen give way to the board
                        lettered *= saturate(2.0 - pixelMax / pixelSize * 2.0);
                        albedo = lerp(albedo, FriezeColor(index % 26u), lettered * 0.95);
                    }
                    // A ruled line along both edges of the band
                    float edges = max(RuledLine(p.y - _Frieze.x, 0.12, pixel.y), RuledLine(p.y - _Frieze.y, 0.12, pixel.y));
                    albedo = lerp(albedo, ink, edges * 0.8);
                }

                // Dovetails: tails outlined at both ends of the face
                if (_Dovetail.x > 0.0)
                {
                    float fromStart = p.x;
                    float fromEnd = _RoomExtent.x - p.x;
                    bool atStart = fromStart < fromEnd;
                    float d = atStart ? fromStart : fromEnd;
                    float parity = atStart ? _Dovetail.z : _Dovetail.w;
                    float rows = p.y / _Dovetail.y;
                    float rowIndex = floor(rows);
                    bool tailRow = fmod(rowIndex + parity, 2.0) < 0.5;
                    // The tail is wider at its tip (away from the corner) than at its root
                    float halfHeight = lerp(0.22, 0.44, saturate(d / _Dovetail.x)) * _Dovetail.y;
                    float fromMiddle = abs((rows - rowIndex - 0.5) * _Dovetail.y);
                    float tail = max(fromMiddle - halfHeight, d - _Dovetail.x);
                    if (tailRow)
                    {
                        albedo *= lerp(1.0, 0.86, Inside(tail, pixelMax));
                        albedo = lerp(albedo, ink, RuledLine(tail, 0.1, pixelMax) * 0.9);
                    }
                }

                // Battens: boards across the planks
                if (_Battens.z > 0.0 && (_Battens.x >= 0.0 || _Battens.y >= 0.0))
                {
                    float batten = 0.0;
                    float outline = 0.0;
                    for (int k = 0; k < 2; k++)
                    {
                        float fraction = k == 0 ? _Battens.x : _Battens.y;
                        if (fraction < 0.0)
                            continue;
                        float gap = abs(p.y - fraction * _RoomExtent.y) - _Battens.z * 0.5;
                        batten = max(batten, Inside(gap, pixel.y));
                        outline = max(outline, RuledLine(gap, 0.1, pixel.y));
                    }
                    seamMask *= 1.0 - batten;
                    albedo = lerp(albedo, albedo * float3(0.78, 0.74, 0.7), batten);
                    albedo = lerp(albedo, ink, outline * 0.9);
                }
                albedo = lerp(albedo, ink, seamMask);

                float3 color = SdfToonShade(albedo, n, v, l, 1.0, tint, _WoodSurface.x, _WoodSurface.y, 0.0, 1.0, 0.0);
                return float4(MixFog(SkyFade(color, input.positionWS), input.fogFactor), 1.0);
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
