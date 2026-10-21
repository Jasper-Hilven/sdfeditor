// The glow column over a capture-the-flag flag (ticket 21): a tall band of the team's colour standing on the flag, readable from across
// the field. One quad (uv.x -1..1 across, uv.y 0..1 up), turned about the world's up axis to face the camera wherever the object is
// turned (a carried flag leans; the column stays upright), so only the object's origin counts. It widens with distance so it keeps a
// few pixels far off. Cel-like rather than soft: a bright core, the team colour, a faint rim, each band hard-edged, fading out up the
// column and close to the camera (standing in it does not blind you). Added on top of the scene (additive, no depth write) after the
// opaque models; WebGL 2, no textures.
Shader "SdfBattle/Flag Glow Column"
{
    Properties
    {
        _Color("Colour (linear)", Color) = (0.85, 0.12, 0.1, 1)
        _Intensity("Intensity", Float) = 1.6
        _Height("Height (m)", Float) = 45
        _Width("Width close by (m)", Float) = 0.9
        _AngularWidth("Least width per metre of distance", Float) = 0.008
        _NearFade("Fades out closer than this to the camera (m)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Height;
                float _Width;
                float _AngularWidth;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 shape : TEXCOORD0; // across (-1..1), up (0..1), the near fade
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float3 toCamera = GetCameraPositionWS() - origin;
                float2 flat = toCamera.xz;
                float distance = max(length(flat), 1e-3);
                float3 right = float3(-flat.y, 0, flat.x) / distance;
                float halfWidth = 0.5 * max(_Width, distance * _AngularWidth);
                float3 positionWS = origin + right * (input.uv.x * halfWidth) + float3(0, input.uv.y * _Height, 0);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.shape = float3(input.uv.x, input.uv.y, saturate((length(GetCameraPositionWS() - positionWS) - 1) / max(_NearFade, 1e-3)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = abs(input.shape.x);
                float up = input.shape.y;
                // Hard bands across: a near-white core, the team colour, a faint rim
                float3 colour = _Color.rgb;
                float3 band = across < 0.28 ? lerp(colour, float3(1, 1, 1), 0.55) * 1.4 : across < 0.62 ? colour : colour * 0.35;
                // Up the column: full at the foot, gone at the top, in a few steps
                float fade = 1 - up;
                fade = fade * fade;
                fade = floor(fade * 6 + 0.5) / 6;
                float strength = _Intensity * fade * input.shape.z;
                return half4(band * strength, 1);
            }
            ENDHLSL
        }
    }
}
