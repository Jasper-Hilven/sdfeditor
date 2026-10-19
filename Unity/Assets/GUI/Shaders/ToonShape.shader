// Every panel, button, bar, ring, splat and glow of the GUI: a 2D signed distance field evaluated per pixel on one quad, in the toon
// look of the models: a flat fill with a darker lower band and a glossy highlight, a thick ink outline, a hard drop shadow, optional
// amanita spots, an animated wobbly edge and a soft outer glow. Crisp at any resolution and scale (world-space canvases too), and one
// material draws every shape, so a canvas batches them: ToonShape.cs puts each shape's parameters in its vertices.
//
// Vertex data (ToonShape.cs):
//   TEXCOORD0  xy position relative to the rect's centre, zw half size of the rect (canvas units)
//   TEXCOORD1  x kind, y corner radius, z outline width, w drop shadow offset
//   TEXCOORD2  x fill amount, y fill mode (0 none, 1 horizontal, 2 vertical, 3 radial), z spots, w wobble
//   TEXCOORD3  x glow width, y seed, z shading, w line thickness (ring, cross, check)
//   COLOR      fill colour
// The ink and spot colours are the material's, shared by the whole GUI like the models share one outline tone.
//
// A plain UI shader (no lighting), so it works on overlay canvases in URP and in WebGL 2 alike; it supports masks (stencil, rect
// clipping) like UI/Default.
Shader "SdfGui/Toon Shape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _InkColor ("Ink", Color) = (0.17, 0.11, 0.12, 1)
        _SpotColor ("Spots", Color) = (1, 0.95, 0.85, 1)
        _ShadowOpacity ("Drop shadow opacity", Range(0, 1)) = 0.5
        _ShadeTone ("Lower band tone", Color) = (0.78, 0.72, 0.86, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // Premultiplied alpha: the layers are composited in the shader
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 local : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 fill : TEXCOORD2;
                float4 extra : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _InkColor;
            fixed4 _SpotColor;
            fixed4 _ShadeTone;
            float _ShadowOpacity;
            float4 _ClipRect;
            float _UIVertexColorAlwaysGammaSpace;

            #define KIND_BOX 0
            #define KIND_PILL 1
            #define KIND_RING 2
            #define KIND_CAP 3
            #define KIND_SPLAT 4
            #define KIND_BURST 5
            #define KIND_GLOW 6
            #define KIND_VIGNETTE 7
            #define KIND_ARROW 8
            #define KIND_CROSS 9
            #define KIND_CHECK 10

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Like UI/Default: the canvas hands colours in gamma when it is told to, otherwise already linear
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                o.color = v.color;
                o.local = v.uv0;
                o.shape = v.uv1;
                o.fill = v.uv2;
                o.extra = v.uv3;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float RoundBox(float2 p, float2 b, float r)
            {
                r = min(r, min(b.x, b.y));
                float2 q = abs(p) - b + r;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
            }

            // Distance to an ellipse, approximated (exact enough for edges a few pixels wide)
            float Ellipse(float2 p, float2 r)
            {
                float k0 = length(p / r);
                float k1 = length(p / (r * r));
                return k0 * (k0 - 1) / max(k1, 1e-4);
            }

            float Segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                return length(pa - ba * saturate(dot(pa, ba) / dot(ba, ba)));
            }

            // Isosceles triangle with its tip at the origin and its base q.y further down y, q.x wide on each side
            float Triangle(float2 p, float2 q)
            {
                p.x = abs(p.x);
                float2 a = p - q * saturate(dot(p, q) / dot(q, q));
                float2 b = p - q * float2(saturate(p.x / q.x), 1);
                float s = -sign(q.y);
                float2 d = min(float2(dot(a, a), s * (p.x * q.y - p.y * q.x)), float2(dot(b, b), s * (p.y - q.y)));
                return -sqrt(d.x) * sign(d.y);
            }

            // The shape's signed distance in canvas units, negative inside; its outer edge includes the outline
            float Shape(float kind, float2 p, float2 b, float radius, float thickness, float seed)
            {
                float m = min(b.x, b.y);
                if (kind == KIND_BOX)
                    return RoundBox(p, b, radius);
                if (kind == KIND_PILL)
                    return RoundBox(p, b, m);
                if (kind == KIND_RING)
                    return abs(length(p) - (m - thickness * 0.5)) - thickness * 0.5;
                if (kind == KIND_CAP)
                {
                    // A mushroom cap: half an ellipse on a flat, slightly rounded rim
                    float dome = Ellipse(p - float2(0, -b.y * 0.55), float2(b.x, b.y * 1.55));
                    float rim = RoundBox(p - float2(0, -b.y * 0.72), float2(b.x * 0.92, b.y * 0.28), b.y * 0.2);
                    return min(max(dome, -(p.y + b.y * 0.55)), rim);
                }
                if (kind == KIND_SPLAT)
                {
                    // A blob with uneven lobes and a few droplets around it
                    float a = atan2(p.y, p.x);
                    float r0 = m * 0.62;
                    float r = r0 * (0.86 + 0.09 * sin(5 * a + seed * 6.28) + 0.06 * sin(9 * a + seed * 11.0) + 0.04 * sin(14 * a + seed * 3.0));
                    float d = (length(p) - r) * 0.8;
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = seed * 17.0 + i * 1.71;
                        float2 at = float2(cos(angle), sin(angle)) * r0 * (1.2 + 0.12 * i);
                        d = min(d, length(p - at) - r0 * (0.16 - 0.025 * i));
                    }
                    return d;
                }
                if (kind == KIND_BURST)
                {
                    float a = atan2(p.y, p.x);
                    float r = m * (0.84 + 0.16 * cos(12 * a + seed * 6.28));
                    return (length(p) - r) * 0.75;
                }
                if (kind == KIND_ARROW)
                {
                    // Pointing right; a rounded tip and base
                    return Triangle(float2(p.y, b.x - p.x - radius), float2(b.y - radius, 2 * b.x - 2 * radius)) - radius;
                }
                if (kind == KIND_CROSS)
                {
                    float2 q = float2(p.x - p.y, p.x + p.y) * 0.70710678;
                    float arm = m - thickness * 0.5;
                    return min(Segment(q, float2(-arm, 0), float2(arm, 0)), Segment(q, float2(0, -arm), float2(0, arm))) - thickness * 0.5;
                }
                if (kind == KIND_CHECK)
                {
                    float2 a = float2(-0.62, 0.02) * m;
                    float2 c = float2(-0.18, -0.48) * m;
                    float2 e = float2(0.66, 0.5) * m;
                    return min(Segment(p, a, c), Segment(p, c, e)) - thickness * 0.5;
                }
                return RoundBox(p, b, radius);
            }

            float4 Over(float4 top, float4 bottom)
            {
                return top + bottom * (1 - top.a);
            }

            float4 Layer(float3 colour, float alpha)
            {
                return float4(colour * alpha, alpha);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.local.xy;
                float2 b = i.local.zw;
                float kind = round(i.shape.x);
                float radius = i.shape.y;
                float outline = i.shape.z;
                float shadow = i.shape.w;
                float fillAmount = i.fill.x;
                float fillMode = round(i.fill.y);
                float spots = i.fill.z;
                float wobble = i.fill.w;
                float glow = i.extra.x;
                float seed = i.extra.y;
                float shading = i.extra.z;
                float thickness = i.extra.w;
                float3 fillColour = i.color.rgb;
                float aa = max(length(fwidth(p)), 1e-4) * 0.75;
                float4 colour;

                if (kind == KIND_GLOW)
                {
                    // A soft spot of light, brighter in the middle
                    float r = length(p / b);
                    float a = pow(saturate(1 - r), 1.6) + 0.35 * pow(saturate(1 - r * 1.8), 2);
                    colour = Layer(fillColour, saturate(a));
                }
                else if (kind == KIND_VIGNETTE)
                {
                    // Dark or tinted edges of the screen; the fill amount is where it starts, from the centre out
                    float r = length(p / b * float2(1, 0.92));
                    colour = Layer(fillColour, smoothstep(fillAmount, fillAmount + 0.75, r));
                }
                else
                {
                    float t = _Time.y;
                    float edgeNoise = wobble * sin(p.x * 0.045 + t * 3.1 + seed * 10.0) * sin(p.y * 0.052 - t * 2.3 + seed * 7.0);
                    float d = Shape(kind, p, b, radius, thickness, seed) + edgeNoise;

                    float inside = 1 - smoothstep(-aa, aa, d);
                    float inFill = 1 - smoothstep(-aa, aa, d + outline);

                    // What part the fill covers: the rest of a bar or ring is a dark trough
                    float filled = 1;
                    if (fillMode == 1)
                        filled = smoothstep(-aa, aa, lerp(-b.x, b.x, fillAmount) - p.x);
                    else if (fillMode == 2)
                        filled = smoothstep(-aa, aa, lerp(-b.y, b.y, fillAmount) - p.y);
                    else if (fillMode == 3)
                    {
                        float angle = atan2(p.x, p.y) / 6.2831853;
                        angle = angle < 0 ? angle + 1 : angle;
                        filled = step(angle, fillAmount) * step(0.0001, fillAmount);
                    }
                    float3 trough = lerp(fillColour, _InkColor.rgb, 0.72);
                    float3 body = lerp(trough, fillColour, filled);

                    // Toon bands, like the models' shadow tone and highlight: a darker band along the bottom edge, following the shape
                    // (where a point a little lower is outside it), and a small gloss spot near the top left
                    float bandHeight = min(b.y * 0.5, 14);
                    float below = Shape(kind, p - float2(0, bandHeight), b, radius, thickness, seed) + edgeNoise;
                    body = lerp(body, body * _ShadeTone.rgb, smoothstep(-aa, aa, below + outline) * shading);
                    float2 glossSize = max(min(b * float2(0.24, 0.16), float2(34, 8)), 1.5);
                    float2 glossAt = float2(max(-0.42 * b.x, -b.x + outline + glossSize.x + 16), max(0.5 * b.y, b.y - outline - glossSize.y - 8));
                    float gloss = Ellipse(p - glossAt, glossSize);
                    body = lerp(body, lerp(body, float3(1, 1, 1), 0.4), (1 - smoothstep(-aa, aa, gloss)) * shading * filled);

                    // Amanita spots, scattered over the fill
                    if (spots > 0)
                    {
                        float cell = max(18, min(b.x, b.y) * 0.55);
                        float2 q = p + seed * 37.0;
                        float2 id = floor(q / cell);
                        float spot = 1e5;
                        for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            float2 c = id + float2(x, y);
                            float h = Hash(c);
                            if (h < 0.45)
                                continue;
                            float2 centre = (c + 0.2 + 0.6 * float2(Hash(c + 3.1), Hash(c + 7.7))) * cell;
                            spot = min(spot, length(q - centre) - cell * (0.12 + 0.22 * (h - 0.45)));
                        }
                        body = lerp(body, _SpotColor.rgb, (1 - smoothstep(-aa, aa, spot)) * spots);
                    }

                    colour = Layer(body, inFill);
                    colour = Over(colour, Layer(_InkColor.rgb, saturate(inside - inFill)));

                    if (shadow > 0)
                    {
                        float ds = Shape(kind, p + float2(-shadow * 0.35, shadow), b, radius, thickness, seed) + edgeNoise;
                        colour = Over(colour, Layer(_InkColor.rgb, (1 - smoothstep(-aa, aa, ds)) * _ShadowOpacity));
                    }
                    if (glow > 0)
                    {
                        float g = saturate(1 - max(d, 0) / glow);
                        colour = Over(colour, Layer(fillColour, g * g * 0.75 * (1 - inside)));
                    }
                }

                colour *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                colour *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(colour.a - 0.001);
                #endif
                return colour;
            }
            ENDCG
        }
    }
}
