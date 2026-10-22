// The ground of a level for URP, in the toon look of the models: the same three bands, highlight and shadows (SdfToonShade with
// the Mushroom Style settings), so ground and models read as one world. The models take their material per vertex; the ground's
// vertices carry only position and normal, and its materials (materials/ground/ground_<role>.sdfmat, handed over by the level bake
// in level.json) come from the material's properties below. Their patches, blotches and speckles are drawn in world space, where the
// world position plays the role of a model's own coordinates (UV1 in SdfSurface.hlsl), so the pattern runs on across chunks.
//
// Regions: grass everywhere, and up to four regions over it (dirt, path, tiles, ...), each with its own material. Where each lies
// comes from the level's region map (_GroundRegions, regions.png): per channel the signed distance to the region's edge, filtered
// bilinearly, so a narrow smoothstep where it crosses 0 draws crisp, anti-aliased edges at any distance; a little world-space noise
// keeps them from looking ruled. Later regions are drawn over earlier ones. A region with the stones pattern is drawn as flagstones:
// an offset grid of stones in world space with grout lines (the material's edge colour), a tone per stone between its base and
// variation colours, and a bevel near the grout that tilts the normal, so the toon bands pick out each stone. No geometry.
//
// Like SdfToon.shader: forward only, shadow caster, depth and depth-normals passes, LOD cross-fade in every pass (the chunks' levels
// of detail dissolve into each other) and GPU instancing. No ink outline: the ground has no silhouette worth one, and its hills
// are outlined by the trees on them.
Shader "SdfMushrooms/Ground"
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

        _GroundBaseColor("Base colour (linear) and emission (0 to 1)", Vector) = (0.17, 0.36, 0.08, 0)
        _GroundVariation("Variation colour (linear) and scale (m)", Vector) = (0.3, 0.44, 0.09, 9)
        _GroundSurface("Roughness, specular, colour variation, detail scale (m)", Vector) = (0.9, 0.3, 0.16, 0.5)
        _GroundSpeckle("Speckle colour (linear) and scale (m)", Vector) = (0.42, 0.56, 0.16, 0.35)
        _GroundSpeckleShape("Speckle density and size", Vector) = (0.22, 0.22, 0, 0)

        // The region map: per channel the signed distance to a region's edge, (d / range + 1) / 2. Its mapping takes world x and z to
        // its uv (scale x, scale z, offset x, offset z); _GroundRegionShape is (range in metres, number of regions, edge width in
        // metres, unused); _GroundRegionWobble is per channel how far in metres the shader's noise moves the region's edge
        [NoScaleOffset] _GroundRegions("Region map", 2D) = "white" {}
        _GroundRegionMapping("Region map mapping", Vector) = (0, 0, 0, 0)
        _GroundRegionShape("Region range, count, edge width, unused", Vector) = (2, 0, 0.05, 0)
        _GroundRegionWobble("Region edge wobble per channel (m)", Vector) = (0.06, 0.06, 0.06, 0.06)

        // Each region's material as the grass's above (with its emission in the base colour's alpha), plus its pattern: x = speckle
        // density, y = speckle size, z = 1 for stones, w = stone size (m); and the grout: rgb = colour (linear), a = width (m)
        _Region0Base("Region 0 base colour and emission (0 to 1)", Vector) = (0, 0, 0, 0)
        _Region0Variation("Region 0 variation colour and scale", Vector) = (0, 0, 0, 0)
        _Region0Surface("Region 0 roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _Region0Speckle("Region 0 speckle colour and scale", Vector) = (0, 0, 0, 0)
        _Region0Pattern("Region 0 speckle density, size, stones, stone size", Vector) = (0, 0, 0, 0.6)
        _Region0Grout("Region 0 grout colour and width", Vector) = (0, 0, 0, 0)
        _Region1Base("Region 1 base colour and emission (0 to 1)", Vector) = (0, 0, 0, 0)
        _Region1Variation("Region 1 variation colour and scale", Vector) = (0, 0, 0, 0)
        _Region1Surface("Region 1 roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _Region1Speckle("Region 1 speckle colour and scale", Vector) = (0, 0, 0, 0)
        _Region1Pattern("Region 1 speckle density, size, stones, stone size", Vector) = (0, 0, 0, 0.6)
        _Region1Grout("Region 1 grout colour and width", Vector) = (0, 0, 0, 0)
        _Region2Base("Region 2 base colour and emission (0 to 1)", Vector) = (0, 0, 0, 0)
        _Region2Variation("Region 2 variation colour and scale", Vector) = (0, 0, 0, 0)
        _Region2Surface("Region 2 roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _Region2Speckle("Region 2 speckle colour and scale", Vector) = (0, 0, 0, 0)
        _Region2Pattern("Region 2 speckle density, size, stones, stone size", Vector) = (0, 0, 0, 0.6)
        _Region2Grout("Region 2 grout colour and width", Vector) = (0, 0, 0, 0)
        _Region3Base("Region 3 base colour and emission (0 to 1)", Vector) = (0, 0, 0, 0)
        _Region3Variation("Region 3 variation colour and scale", Vector) = (0, 0, 0, 0)
        _Region3Surface("Region 3 roughness, specular, colour variation, detail scale", Vector) = (0.9, 0.3, 0, 0.5)
        _Region3Speckle("Region 3 speckle colour and scale", Vector) = (0, 0, 0, 0)
        _Region3Pattern("Region 3 speckle density, size, stones, stone size", Vector) = (0, 0, 0, 0.6)
        _Region3Grout("Region 3 grout colour and width", Vector) = (0, 0, 0, 0)

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
    #if defined(LOD_FADE_CROSSFADE)
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
    #endif

    // One constant buffer for every pass, so the SRP batcher can draw them
    CBUFFER_START(UnityPerMaterial)
        float4 _ToonBands;
        float4 _ToonShadowTone;
        float4 _ToonMidTone;
        float4 _ToonLitTone;
        float4 _ToonSpecular;
        float4 _ToonRim;
        float4 _ToonVariation;
        float4 _GroundBaseColor;
        float4 _GroundVariation;
        float4 _GroundSurface;
        float4 _GroundSpeckle;
        float4 _GroundSpeckleShape;
        float4 _GroundRegionMapping;
        float4 _GroundRegionShape;
        float4 _GroundRegionWobble;
        float4 _Region0Base, _Region0Variation, _Region0Surface, _Region0Speckle, _Region0Pattern, _Region0Grout;
        float4 _Region1Base, _Region1Variation, _Region1Surface, _Region1Speckle, _Region1Pattern, _Region1Grout;
        float4 _Region2Base, _Region2Variation, _Region2Surface, _Region2Speckle, _Region2Pattern, _Region2Grout;
        float4 _Region3Base, _Region3Variation, _Region3Surface, _Region3Speckle, _Region3Pattern, _Region3Grout;
        float4 _EdgeColor;
        float4 _DetailAxis;
        float4 _Subsurface;
        float4 _Look;
    CBUFFER_END

    TEXTURE2D(_GroundRegions);
    SAMPLER(sampler_GroundRegions);

    // Drops this pixel from one of the two levels a LOD group is fading between; nothing without the keyword
    void SdfLodFade(float4 positionCS)
    {
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(positionCS);
    #endif
    }

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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Mushrooms/Shaders/SdfSurface.hlsl"
            #include "Assets/Mushrooms/Shaders/SdfToon.hlsl"
            #include "Assets/Mushrooms/Shaders/SdfStaticShadow.hlsl"
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

            // One ground material: its colours and surface, and its pattern (see the properties)
            struct GroundLayer
            {
                float3 base;
                float emission;
                float4 variation;
                float4 surface;
                float4 speckle;
                float4 pattern;
                float4 grout;
            };

            GroundLayer Layer(float4 base, float4 variation, float4 surface, float4 speckle, float4 pattern, float4 grout)
            {
                GroundLayer layer;
                layer.base = base.rgb;
                layer.emission = base.a;
                layer.variation = variation;
                layer.surface = surface;
                layer.speckle = speckle;
                layer.pattern = pattern;
                layer.grout = grout;
                return layer;
            }

            // The material's colour at a point of the world, as SdfToonColor draws a model's: patches and blotches toned down by the
            // style's colour variation, speckles at full strength on top
            float3 SdfGroundColor(GroundLayer layer, float3 base, float3 positionWS)
            {
                float3 varied = SdfVariedColor(base, positionWS, layer.variation, layer.surface.z, max(layer.surface.w, 0.001),
                    float4(0, 0, 0, 0), float2(0, 0));
                float3 color = lerp(base, varied, _ToonVariation.x);
                if (layer.speckle.a > 0.0 && layer.pattern.x > 0.0)
                {
                    float2 fleck = SdfSpeckle(positionWS, layer.speckle.a, layer.pattern.x, layer.pattern.y);
                    color = lerp(color, saturate(layer.speckle.rgb * fleck.y), fleck.x);
                }
                return color;
            }

            // Flagstones in world space: rows `size` deep of stones 1.3 × size long, each row shifted by its own random amount (an
            // uneven bond). Gives the stone's tone (0 to 1, per stone), how much of the pixel is grout, and the bevel: the direction
            // in world xz from the stone's middle towards its nearest edge, scaled by how far into the bevel the pixel is. `pixel` is the
            // pixel's size on the ground in metres, taken outside the branches that call this, where derivatives are not to be had
            void SdfStones(float2 p, float size, float grout, float pixel, out float tone, out float groutMask, out float2 bevel)
            {
                float2 cellSize = float2(1.3, 1.0) * size;
                float row = floor(p.y / cellSize.y);
                float2 q = float2(p.x / cellSize.x + SdfHash(float3(row, 7.13, 3.71)), p.y / cellSize.y);
                float2 cell = floor(q);
                float2 f = q - cell;
                tone = SdfHash(float3(cell, 19.7));

                // Distance to the stone's edge in metres, along x and along z
                float2 toEdge = min(f, 1.0 - f) * cellSize;
                float edge = min(toEdge.x, toEdge.y);
                float aa = max(pixel, 1e-4);
                float half_ = grout * 0.5;
                groutMask = 1.0 - smoothstep(half_ - aa, half_ + aa, edge);

                // Bevelled near the grout: the normal leans out over the last few centimetres of the stone
                float bevelWidth = max(0.08 * size, 0.02);
                float into = saturate(1.0 - (edge - half_) / bevelWidth) * (1.0 - groutMask);
                float2 outward = toEdge.x < toEdge.y ? float2(f.x < 0.5 ? -1.0 : 1.0, 0.0) : float2(0.0, f.y < 0.5 ? -1.0 : 1.0);
                bevel = outward * into * into;

                // Far away, where a pixel covers a good part of a stone, the pattern would shimmer: it gives way to its average, grout
                // spread thinly over even stone
                float far = saturate(pixel / (0.3 * size) - 0.3);
                groutMask = lerp(groutMask, grout * (1.0 / cellSize.x + 1.0 / cellSize.y), far);
                tone = lerp(tone, 0.5, far);
                bevel *= 1.0 - far;
            }

            // Shades one layer at the point: its colour, and for stones the normal leaned by the bevel
            float3 LayerColor(GroundLayer layer, float3 positionWS, float pixel, inout float3 n)
            {
                if (layer.pattern.z < 0.5)
                    return SdfGroundColor(layer, layer.base, positionWS);

                float tone, groutMask;
                float2 bevel;
                SdfStones(positionWS.xz, max(layer.pattern.w, 0.05), layer.grout.a, pixel, tone, groutMask, bevel);
                // Each stone its own tone between the base and the variation colour, a little lighter or darker
                float3 stone = lerp(layer.base, layer.variation.rgb, tone * 0.85) * (0.88 + 0.24 * SdfHash(float3(tone * 97.0, 3.1, 5.9)));
                float3 color = SdfGroundColor(layer, stone, positionWS);
                n = normalize(n + float3(bevel.x, 0.0, bevel.y) * 0.9);
                return lerp(color, layer.grout.rgb, groutMask);
            }

            // How much of the region the point is in: 1 inside, 0 outside, a narrow, anti-aliased edge between
            float RegionWeight(float encoded, float wobble)
            {
                float d = (encoded * 2.0 - 1.0) * _GroundRegionShape.x + wobble;
                float width = max(_GroundRegionShape.z, fwidth(d) * 0.75);
                return 1.0 - smoothstep(-width, width, d);
            }

            float4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SdfLodFade(input.positionCS);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // The main light (the sun) with its cascaded shadows, as SdfToon.shader reads it
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

                // Grass, with each region drawn over it as far as the point is in it, later ones over earlier ones
                float3 positionWS = input.positionWS;
                float4 encoded = SAMPLE_TEXTURE2D(_GroundRegions, sampler_GroundRegions, positionWS.xz * _GroundRegionMapping.xy + _GroundRegionMapping.zw);
                float wobble = (SdfNoise(positionWS * 1.7 + 11.3) - 0.5) * 2.0;
                float count = _GroundRegionShape.y;
                float pixel = length(fwidth(positionWS.xz)) * 0.7;
                float4 weights = float4(
                    count > 0.5 ? RegionWeight(encoded.r, wobble * _GroundRegionWobble.x) : 0.0,
                    count > 1.5 ? RegionWeight(encoded.g, wobble * _GroundRegionWobble.y) : 0.0,
                    count > 2.5 ? RegionWeight(encoded.b, wobble * _GroundRegionWobble.z) : 0.0,
                    count > 3.5 ? RegionWeight(encoded.a, wobble * _GroundRegionWobble.w) : 0.0);
                // How much of each layer shows: a region covers the ones before it
                float4 shown = weights;
                shown.z *= 1.0 - weights.w;
                shown.y *= (1.0 - weights.z) * (1.0 - weights.w);
                shown.x *= (1.0 - weights.y) * (1.0 - weights.z) * (1.0 - weights.w);
                float grassShown = saturate(1.0 - dot(shown, float4(1, 1, 1, 1)));

                float3 albedo = float3(0, 0, 0);
                float2 surface = float2(0, 0);
                float3 normal = float3(0, 0, 0);
                // What the glowing materials add after the bands: their colour times their emission, as far as they show
                float3 glow = float3(0, 0, 0);
                if (grassShown > 0.001)
                {
                    GroundLayer grass = Layer(_GroundBaseColor, _GroundVariation, _GroundSurface, _GroundSpeckle,
                        float4(_GroundSpeckleShape.xy, 0, 0), float4(0, 0, 0, 0));
                    float3 grassColor = SdfGroundColor(grass, grass.base, positionWS);
                    albedo += grassShown * grassColor;
                    glow += grassShown * grass.emission * grassColor;
                    surface += grassShown * _GroundSurface.xy;
                    normal += grassShown * n;
                }
                #define SDF_GROUND_REGION(i, w) \
                    if (w > 0.001) \
                    { \
                        GroundLayer layer = Layer(_Region##i##Base, _Region##i##Variation, _Region##i##Surface, _Region##i##Speckle, \
                            _Region##i##Pattern, _Region##i##Grout); \
                        float3 layerNormal = n; \
                        float3 layerColor = LayerColor(layer, positionWS, pixel, layerNormal); \
                        albedo += w * layerColor; \
                        glow += w * layer.emission * layerColor; \
                        surface += w * _Region##i##Surface.xy; \
                        normal += w * layerNormal; \
                    }
                SDF_GROUND_REGION(0, shown.x)
                SDF_GROUND_REGION(1, shown.y)
                SDF_GROUND_REGION(2, shown.z)
                SDF_GROUND_REGION(3, shown.w)
                #undef SDF_GROUND_REGION
                n = normalize(normal);

                // Dry and open: no wetness, and no baked occlusion (1)
                float3 color = SdfToonShade(albedo, n, v, l, shadow, tint, surface.x, surface.y, 0.0, 1.0, 0.0);
                color += glow;
                return SdfToonOutput(MixFog(SkyFade(color, input.positionWS), input.fogFactor), normalize(input.normalWS), input.positionWS);
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
