// The covers' hero material (CoverStudio): a hard toon look for a high-detail SDF mesh, harder than the game's (three flat tones, a hard
// highlight and rim, a black ink outline). Everything it needs is in
// the mesh (COLOR_0 albedo with the pipeline's variation and speckles baked in, the material mix's roughness, specular, wetness and
// subsurface radius, the thickness, the ambient occlusion and the emission) and in the properties below (three lights and a sky set by
// CoverStudio per shot); it reads no scene lights or shadows. Soft wrapped diffuse that lets light into the material (subsurface), GGX
// highlights with a wet clear coat, light through thin parts from the back, a hemisphere ambient under the occlusion, a coloured rim and
// the glow of emissive materials, tone-mapped in the shader (the cover cameras render without post-processing).
// It is in an Editor folder: it never goes into a build.
Shader "Hidden/SdfMedia/CoverHero"
{
    Properties
    {
        _KeyDir ("Key light, towards the light (world)", Vector) = (0.5, 0.7, 0.6, 0)
        _KeyColor ("Key light colour", Color) = (1, 0.95, 0.88, 1)
        _FillDir ("Fill light", Vector) = (-0.7, 0.2, 0.5, 0)
        _FillColor ("Fill light colour", Color) = (0.3, 0.32, 0.4, 1)
        _RimDir ("Rim light (from behind)", Vector) = (0, 0.4, -1, 0)
        _RimColor ("Rim light colour", Color) = (1, 0.3, 0.2, 1)
        _SkyColor ("Ambient from above", Color) = (0.3, 0.3, 0.35, 1)
        _GroundColor ("Ambient from below", Color) = (0.12, 0.08, 0.08, 1)
        _EmissionBoost ("Glow strength", Float) = 3
        _Exposure ("Exposure", Float) = 1
        _OutlinePixels ("Ink outline width (pixels of the render)", Float) = 6
        _InkColor ("Ink colour", Color) = (0.05, 0.03, 0.04, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _KeyDir, _KeyColor, _FillDir, _FillColor, _RimDir, _RimColor, _SkyColor, _GroundColor;
            float _EmissionBoost, _Exposure;

            struct Attributes
            {
                float3 position : POSITION;
                float3 normal : NORMAL;
                float4 albedo : COLOR;
                // roughness, specular, wetness, subsurface radius (m)
                float4 surface : TEXCOORD0;
                // thickness (m), occlusion (0 closed to 1 open), emission (0 to 1)
                float4 extra : TEXCOORD1;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 albedo : TEXCOORD2;
                float4 surface : TEXCOORD3;
                float3 extra : TEXCOORD4;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.position);
                output.position = TransformWorldToHClip(output.world);
                output.normal = TransformObjectToWorldNormal(input.normal);
                output.albedo = input.albedo.rgb;
                output.surface = input.surface;
                output.extra = input.extra.xyz;
                return output;
            }

            float Ggx(float nh, float alpha)
            {
                float a2 = alpha * alpha;
                float d = nh * nh * (a2 - 1) + 1;
                return a2 / (PI * d * d);
            }

            float3 Schlick(float3 f0, float vh)
            {
                return f0 + (1 - f0) * pow(1 - vh, 5);
            }

            // One light: wrapped diffuse (wider the deeper light gets in), GGX highlight, a sharper clear-coat highlight for wet materials,
            // and light through the material from behind where it is thin
            float3 Shade(float3 n, float3 v, float3 l, float3 colour, float3 albedo, float4 surface, float thickness)
            {
                float roughness = clamp(surface.x, 0.08, 1);
                float specular = surface.y;
                float wetness = surface.z;
                float radius = surface.w;
                float wrap = saturate(radius * 12);
                float nl = dot(n, l);
                float diffuse = saturate((nl + wrap) / ((1 + wrap) * (1 + wrap)));
                // The light that went in comes out warmer and more saturated
                float3 scattered = lerp(albedo, albedo * albedo * 1.6, wrap * saturate(1 - nl) * 0.6);
                float3 h = normalize(l + v);
                float nh = saturate(dot(n, h));
                float vh = saturate(dot(v, h));
                float lit = saturate(nl);
                float3 f0 = 0.08 * specular;
                float3 highlight = Ggx(nh, roughness * roughness) * Schlick(f0, vh) * 0.25 / max(vh * vh, 0.1) * lit;
                float3 coat = Ggx(nh, 0.12) * Schlick(0.04, vh) * 0.25 / max(vh * vh, 0.1) * lit * wetness;
                float through = exp(-thickness / max(radius * 2, 0.004)) * pow(saturate(dot(v, -l)), 3) * saturate(radius * 30);
                return colour * (scattered * diffuse + highlight + coat + albedo * through * 0.8);
            }

            float3 Tonemap(float3 x)
            {
                // Narkowicz's ACES fit
                x *= _Exposure;
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            // Hard toon bands: a step with a sliver of smoothing (screen-space, so the edge stays anti-aliased and crisp)
            float Band(float x, float edge)
            {
                float w = max(fwidth(x), 1e-4);
                return smoothstep(edge - w, edge + w, x);
            }

            float4 Fragment(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normal);
                float3 v = normalize(GetCameraPositionWS() - input.world);
                float3 albedo = input.albedo;
                float roughness = clamp(input.surface.x, 0.15, 1);
                float wetness = input.surface.z;
                float occlusion = input.extra.y;
                float emission = input.extra.z;

                // Three flat tones from the key light: deep shadow, shadow, lit; the shadows tinted by the backdrop (the rim colour) rather
                // than greyed, the way the game's toon shader tints with the sky
                float3 key = normalize(_KeyDir.xyz);
                float nl = dot(n, key) * lerp(0.6, 1, occlusion);
                float lit = Band(nl, 0.08);
                float deep = 1 - Band(nl, -0.45);
                float3 shadowTint = lerp(float3(0.42, 0.36, 0.48), _RimColor.rgb * 0.35, 0.45);
                float3 tone = lerp(albedo * shadowTint, albedo * _KeyColor.rgb, lit);
                tone = lerp(tone, albedo * shadowTint * 0.6, deep);
                // Crevices: darkened by the occlusion (soft: a hard step streaks on the fine surface detail)
                tone *= lerp(0.55, 1, smoothstep(0.15, 0.6, occlusion));

                // A hard highlight: a flat spot whose size follows the roughness, brighter on wet materials
                float3 h = normalize(key + v);
                float nh = saturate(dot(n, h));
                float spot = Band(nh, 1 - 0.03 * roughness * roughness - 0.002) * lit * Band(occlusion, 0.6);
                tone += spot * (0.18 + wetness * 0.35) * _KeyColor.rgb;

                // A hard rim on the side away from the key, in the backdrop's colour
                float nv = saturate(dot(n, v));
                float rim = Band(1 - nv, 0.8) * Band(dot(n, normalize(_RimDir.xyz)) + 0.35, 0.3);
                tone = lerp(tone, _RimColor.rgb * 0.5 + albedo * 0.5, rim * 0.7);

                // Glowing materials stay flat and bright
                tone = lerp(tone, albedo * (1 + _EmissionBoost * 0.5), saturate(emission * _EmissionBoost));
                return float4(saturate(tone * _Exposure), 1);
            }
            ENDHLSL
        }

        // The ink outline: the mesh pushed out along its normals in screen space (a constant width in pixels) with its front faces culled
        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _OutlinePixels;
            float4 _InkColor;

            struct Attributes
            {
                float3 position : POSITION;
                float3 normal : NORMAL;
            };

            float4 Vertex(Attributes input) : SV_POSITION
            {
                float4 clip = TransformWorldToHClip(TransformObjectToWorld(input.position));
                float3 normal = TransformObjectToWorldNormal(input.normal);
                float2 direction = mul((float3x3)UNITY_MATRIX_VP, normal).xy;
                direction = normalize(direction + 1e-6);
                clip.xy += direction * _OutlinePixels * 2 / _ScreenParams.xy * clip.w;
                return clip;
            }

            float4 Fragment() : SV_Target
            {
                return float4(_InkColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
