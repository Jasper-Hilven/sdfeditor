// One face of the computer case room (a steel wall or the smoked glass lid; ticket 30.2) for URP, in the toon look of the other room
// shaders (SdfRoomPlanks.shader, SdfRoomFace.shader): the same three bands, highlight and fog-free colours. The face is a flat quad;
// everything on it is drawn here from the world position (no UVs, no textures), so its lines are ruled straight at any distance
// (anti-aliased by the pixel's size) and cost no geometry.
//
// A point of the face is (a, b) = ((world - origin) . u, (world - origin) . v) in metres, with the face's origin and unit axes from the
// level's room block (level.json "room", x mirrored for Unity); the face is _RoomExtent.xy metres along u and v. _Lid.x is 0 on a wall:
//   - panels: a grid of steel panels _Panels.xy metres across, each with its own tone step (_Panels.z), brushed streaks along u, a dark
//     seam between them (the material's edge colour, _Panels.w metres wide each side of the join) and a rivet in each panel corner;
//   - vents: a honeycomb of hexagonal holes _VentCell.x metres across, in the rectangle from _Vent.x to _Vent.y metres up and _Vent.z to
//     _Vent.w metres along u, inside a frame _VentCell.y metres wide; none where _Vent.y is not above _Vent.x (LevelRoom sets it on the
//     west and east walls only).
// _Lid.x is 1 on the lid (the smoked glass):
//   - a frame _Lid.y metres wide round the edge in the seam colour;
//   - a faint diagonal reflection streak, a wide band and a thin one beside it, at _Lid.z metres along the diagonal, _Lid.w wide;
//   - a soft warm glow patch (the PSU's light seen through the glass) at the fractions _Patch.xy of the face, _Patch.z metres across,
//     in _PatchColor at strength _Patch.w, added flat after the light bands in three steps.
// Anything with a zero size is left out. The look of a room is chosen in LevelRoom (by the material's name), which sets these.
//
// Light: the room's directional lamp seen from the face's own side, leaned toward the face's normal (_RoomExtent.w of the way) as in
// SdfRoomFace.shader. No cast shadows, none received. Forward only, depth and depth-normals passes, no ink outline pass.
Shader "SdfMushrooms/RoomCase"
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

        // The metal or glass (the material's own values: materials/room/<name>.sdfmat)
        _MetalBase("Base colour (linear)", Vector) = (0, 0, 0, 0)
        _MetalVariation("Variation colour and scale", Vector) = (0, 0, 0, 0)
        _MetalSurface("Roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _MetalSpeckle("Speckle colour and scale", Vector) = (0, 0, 0, 0)
        _MetalPattern("Speckle density and size", Vector) = (0, 0, 0, 0)
        _MetalEdge("Seam colour (linear)", Vector) = (0, 0, 0, 0)

        _Panels("Panel width, panel height, tone step, seam half width", Vector) = (16, 16, 0.06, 0.08)
        _Vent("Bottom, top (metres up), start, end (metres along u)", Vector) = (0, 0, 0, 0)
        _VentCell("Hexagon width, frame width, unused, unused", Vector) = (4, 0.6, 0, 0)
        _Lid("Lid (0 or 1), frame width, streak position, streak width", Vector) = (0, 1.2, 0, 8)
        _Patch("Glow patch u fraction, v fraction, diameter, strength", Vector) = (0.6, 0.35, 40, 0)
        _PatchColor("Glow patch colour (linear)", Vector) = (1, 0.6, 0.3, 0)

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
        float4 _MetalBase, _MetalVariation, _MetalSurface, _MetalSpeckle, _MetalPattern, _MetalEdge;
        float4 _Panels;
        float4 _Vent;
        float4 _VentCell;
        float4 _Lid;
        float4 _Patch;
        float4 _PatchColor;
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

            float Hash(float n)
            {
                return frac(sin(n * 12.9898 + 4.1414) * 43758.5453);
            }

            // The face material's colour at a point of the world, as SdfRoomPlanks.shader's WoodColor
            float3 MetalColor(float3 positionWS)
            {
                float3 varied = SdfVariedColor(_MetalBase.rgb, positionWS, _MetalVariation, _MetalSurface.z, max(_MetalSurface.w, 0.001), float4(0, 0, 0, 0), float2(0, 0));
                float3 color = lerp(_MetalBase.rgb, varied, _ToonVariation.x);
                if (_MetalSpeckle.a > 0.0 && _MetalPattern.x > 0.0)
                {
                    float2 fleck = SdfSpeckle(positionWS, _MetalSpeckle.a, _MetalPattern.x, _MetalPattern.y);
                    color = lerp(color, saturate(_MetalSpeckle.rgb * fleck.y), fleck.x);
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

            // The distance (in cells, 0.5 at the cell's edge) from the centre of the nearest hexagon of a flat-topped grid of cells 1 wide
            float HexDistance(float2 q)
            {
                const float2 r = float2(1.0, 1.7320508);
                float2 h = r * 0.5;
                float2 a = fmod(q, r) - h;
                float2 b = fmod(q - h + r, r) - h;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                g = abs(g);
                return max(dot(g, float2(0.5, 0.8660254)), g.x);
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

                float3 albedo = MetalColor(input.positionWS);
                float3 ink = _MetalEdge.rgb;
                float3 glow = float3(0, 0, 0);

                if (_Lid.x < 0.5)
                {
                    // Panels: a tone step per panel, and brushed streaks along u that fade out when thinner than a pixel
                    float2 cell = floor(p / _Panels.xy);
                    albedo *= 1.0 + (Hash(cell.x + cell.y * 37.0) - 0.5) * 2.0 * _Panels.z;
                    float streaks = (Hash(floor(p.y * 6.0) + cell.x * 11.0 + cell.y * 53.0) - 0.5) * 0.12;
                    albedo *= 1.0 + streaks * saturate(1.0 - pixel.y * 6.0);

                    // Seams between the panels, rivets in each panel's corners
                    float2 local = p - cell * _Panels.xy;
                    float2 toEdge = min(local, _Panels.xy - local);
                    float seam = max(RuledLine(toEdge.x, _Panels.w, pixel.x), RuledLine(toEdge.y, _Panels.w, pixel.y));
                    float seamFade = saturate(_Panels.w * 2.0 / max(pixelMax * 2.0, 1e-4));
                    seam = saturate(seam) * lerp(0.35, 1.0, seamFade);
                    float2 corner = abs(local - _Panels.xy * 0.5) - (_Panels.xy * 0.5 - 0.9);
                    float rivet = Inside(length(corner) - 0.28, pixelMax) * saturate(1.0 - pixelMax * 2.5);
                    albedo = lerp(albedo, albedo * 1.6 + 0.02, rivet);
                    albedo = lerp(albedo, ink, seam);

                    // Vents: honeycomb holes in a framed rectangle
                    if (_Vent.y > _Vent.x)
                    {
                        float2 centre = float2((_Vent.z + _Vent.w) * 0.5, (_Vent.x + _Vent.y) * 0.5);
                        float2 halfSize = float2(_Vent.w - _Vent.z, _Vent.y - _Vent.x) * 0.5;
                        float2 d = abs(p - centre) - halfSize;
                        float rect = max(d.x, d.y);
                        float frame = _VentCell.y;
                        // The frame: a darker plate with an ink outline
                        albedo = lerp(albedo, albedo * 0.7, Inside(rect, pixelMax));
                        albedo = lerp(albedo, ink, RuledLine(rect, 0.12, pixelMax));
                        // The holes: hexagons a little smaller than their cells, black, with a faint steel rim
                        float hex = HexDistance((p - float2(_Vent.z, _Vent.x)) / _VentCell.x + float2(0.5, 0.5));
                        float hole = 1.0 - smoothstep(0.38 - pixelMax / _VentCell.x, 0.38 + pixelMax / _VentCell.x, hex);
                        hole *= Inside(rect + frame, pixelMax) * saturate(1.0 - pixelMax / _VentCell.x * 1.5);
                        float rim = 1.0 - smoothstep(0.0, 0.07 + pixelMax / _VentCell.x, abs(hex - 0.38));
                        albedo = lerp(albedo, albedo * 1.4, rim * Inside(rect + frame, pixelMax) * 0.6);
                        albedo = lerp(albedo, ink * 0.4, hole);
                    }
                }
                else
                {
                    // The smoked glass: a frame in the seam colour round the edge
                    float2 toEdge = min(p, _RoomExtent.xy - p);
                    float edge = min(toEdge.x, toEdge.y);
                    float framed = Inside(edge - _Lid.y, pixelMax);
                    albedo = lerp(albedo, ink, framed);
                    albedo = lerp(albedo, ink * 2.0, RuledLine(edge - _Lid.y, 0.1, pixelMax) * 0.8);

                    // A faint diagonal reflection: a wide band and a thin one beside it, hard-edged like a cel highlight
                    float along = (p.x + p.y) * 0.70710678 - _Lid.z;
                    float wide = Inside(abs(along) - _Lid.w * 0.5, pixelMax);
                    float thin = Inside(abs(along - _Lid.w * 0.9) - _Lid.w * 0.1, pixelMax);
                    albedo += (wide * 0.035 + thin * 0.05) * (1.0 - framed);

                    // The warm glow patch, off-centre, in three flat steps softened a little
                    float2 centre = _Patch.xy * _RoomExtent.xy;
                    float falloff = saturate(1.0 - length(p - centre) / max(_Patch.z * 0.5, 1e-3));
                    float steps = smoothstep(0.0, 0.08, falloff) * 0.35 + smoothstep(0.3, 0.38, falloff) * 0.35 + smoothstep(0.6, 0.68, falloff) * 0.3;
                    glow += _PatchColor.rgb * steps * _Patch.w;
                }

                float3 color = SdfToonShade(albedo, n, v, l, 1.0, tint, _MetalSurface.x, _MetalSurface.y, 0.0, 1.0, 0.0);
                color += glow;
                return float4(SkyFade(color, input.positionWS), 1.0);
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
