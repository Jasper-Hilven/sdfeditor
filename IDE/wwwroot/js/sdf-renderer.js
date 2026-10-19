const VERTEX_SHADER = `#version 300 es
in vec2 position;
void main() { gl_Position = vec4(position, 0.0, 1.0); }`;

const fragmentShader = sdfCode => `#version 300 es
precision highp float;
precision highp int;

uniform vec2 uResolution;
uniform vec2 uViewportOrigin;
uniform vec3 uCameraPosition;
uniform vec3 uCameraTarget;
uniform vec3 uCameraUp;
uniform float uOrthoScale;
uniform vec3 uLightPosition;
uniform int uStyle; // 0 realistic, 1 toon

out vec4 fragColor;

#line 1
${sdfCode}

const int MAX_STEPS = 200;
const float MAX_DISTANCE = 100.0;
const float SURFACE_DISTANCE = 0.001;
const float RENDER_PI = 3.14159265;

// Soft daylight: a warm sun (uLightPosition is the direction towards it), a blue sky above and dim bounce light from the ground below.
const vec3 SUN_COLOR = vec3(1.0, 0.9, 0.78) * 2.4;
const vec3 SKY_COLOR = vec3(0.45, 0.6, 0.9) * 0.55;
const vec3 GROUND_COLOR = vec3(0.34, 0.28, 0.22) * 0.3;
const float EXPOSURE = 0.6;

float map(vec3 p) { return sdf_scene(p); }

// Returns MAX_DISTANCE + 1 for a miss. A ray that runs out of steps only hits when it ended close to a surface; otherwise rays
// grazing along a plane would show as a band of surface above it.
float raymarch(vec3 ro, vec3 rd)
{
    float t = 0.0;
    float d = MAX_DISTANCE;
    for (int i = 0; i < MAX_STEPS; i++)
    {
        d = map(ro + rd * t);
        if (d < SURFACE_DISTANCE) return t;
        t += d;
        if (t > MAX_DISTANCE) return MAX_DISTANCE + 1.0;
    }
    return d < 0.01 ? t : MAX_DISTANCE + 1.0;
}

vec3 calcNormal(vec3 p)
{
    const vec2 k = vec2(1.0, -1.0);
    const float e = 0.0005;
    return normalize(
        k.xyy * map(p + k.xyy * e) +
        k.yyx * map(p + k.yyx * e) +
        k.yxy * map(p + k.yxy * e) +
        k.xxx * map(p + k.xxx * e));
}

float softShadow(vec3 ro, vec3 rd, float maxT)
{
    float result = 1.0;
    float t = 0.02;
    for (int i = 0; i < 64 && t < maxT; i++)
    {
        float d = map(ro + rd * t);
        if (d < SURFACE_DISTANCE) return 0.0;
        result = min(result, 8.0 * d / t);
        t += clamp(d, 0.01, 0.5);
    }
    return clamp(result, 0.0, 1.0);
}

float ambientOcclusion(vec3 p, vec3 n)
{
    float occlusion = 0.0;
    float weight = 1.0;
    for (int i = 1; i <= 5; i++)
    {
        float h = 0.03 * float(i);
        occlusion += (h - map(p + n * h)) * weight;
        weight *= 0.85;
    }
    return clamp(1.0 - 4.0 * occlusion, 0.0, 1.0);
}

// Value noise for procedural surface detail: plain arithmetic, so a Unity shader can evaluate the same function in object space.
float shadeHash(vec3 p)
{
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float shadeNoise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(shadeHash(i), shadeHash(i + vec3(1, 0, 0)), u.x), mix(shadeHash(i + vec3(0, 1, 0)), shadeHash(i + vec3(1, 1, 0)), u.x), u.y),
        mix(mix(shadeHash(i + vec3(0, 0, 1)), shadeHash(i + vec3(1, 0, 1)), u.x), mix(shadeHash(i + vec3(0, 1, 1)), shadeHash(i + vec3(1, 1, 1)), u.x), u.y),
        u.z);
}

float shadeDetail(vec3 p)
{
    return 0.6 * shadeNoise(p) + 0.3 * shadeNoise(p * 2.07 + 13.1) + 0.1 * shadeNoise(p * 4.13 + 29.7);
}

// The detail pattern at a position in the shape's own coordinates: round grain, or with detailStretch above 1 fibres that run out
// from detailAxis and along it (radial lines on a cap or iris, lengthwise lines on a stem).
float detailPattern(vec3 local, SurfaceMaterial m)
{
    float scale = max(m.detailScale, 0.001);
    if (m.detailStretch <= 1.0) return shadeDetail(local / scale);
    vec3 axis = normalize(m.detailAxis);
    float along = dot(local, axis);
    vec3 radial = local - axis * along;
    float r = length(radial);
    vec3 around = r > 1e-5 ? radial / r : vec3(0.0);
    return shadeDetail(around * (0.5 / scale) + axis * ((r + along) / (scale * m.detailStretch)));
}

// The pattern at a world position: the material function tells where that is in the shape's own coordinates.
float detailPatternAt(vec3 p, SurfaceMaterial m)
{
    return detailPattern(sdf_material(p).localPosition, m);
}

// Bumps the normal with the detail pattern, differentiated in world space so it stays right on rotated shapes.
vec3 detailNormal(vec3 p, vec3 n, SurfaceMaterial m)
{
    if (m.detailStrength <= 0.0) return n;
    float scale = max(m.detailScale, 0.001);
    float e = scale * 0.08;
    float base = detailPattern(m.localPosition, m);
    vec3 gradient = vec3(
        detailPatternAt(p + vec3(e, 0, 0), m) - base,
        detailPatternAt(p + vec3(0, e, 0), m) - base,
        detailPatternAt(p + vec3(0, 0, e), m) - base) * (scale / e);
    gradient -= n * dot(gradient, n);
    return normalize(n - m.detailStrength * 0.5 * gradient);
}

// Small flecks scattered by position: one in each cell speckleScale wide that the hash picks (speckleDensity of them), placed, sized
// and toned at random, with a ragged soft edge. x is how far the colour goes to the fleck's, y the fleck's tone (0.65 to 1.35).
vec2 speckle(vec3 local, float scale, float density, float size)
{
    vec3 p = local / scale;
    vec3 cell = floor(p);
    float ragged = 0.35 * (shadeNoise(p * 3.1 + 5.3) - 0.5);
    vec2 result = vec2(0.0, 1.0);
    for (int z = -1; z <= 1; z++)
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        vec3 c = cell + vec3(x, y, z);
        if (shadeHash(c + 0.37) >= density) continue;
        vec3 center = c + 0.15 + 0.7 * vec3(shadeHash(c + 11.3), shadeHash(c + 23.7), shadeHash(c + 41.9));
        float radius = max(size, 0.01) * (0.5 + shadeHash(c + 57.1));
        float weight = 1.0 - smoothstep(0.55, 1.0, length(p - center) / radius + ragged);
        if (weight > result.x) result = vec2(weight, 0.65 + 0.7 * shadeHash(c + 73.3));
    }
    return result;
}

// Large soft patches where the base colour drifts towards variationColor, then blotches of lighter and darker colour, a few detail
// features wide, then speckles. All are functions of the shape-local position, so a mesh can bake them per vertex.
vec3 variedColor(SurfaceMaterial m)
{
    vec3 color = m.baseColor;
    if (m.variationScale > 0.0)
        color = mix(color, m.variationColor, smoothstep(0.3, 0.75, shadeDetail(m.localPosition / m.variationScale + 37.9)));
    if (m.colorVariation > 0.0)
    {
        float variation = shadeDetail(m.localPosition / max(m.detailScale * 5.0, 0.001) + 71.3) * 2.0 - 1.0;
        color = clamp(color * (1.0 + m.colorVariation * variation), 0.0, 1.0);
    }
    if (m.speckleScale > 0.0 && m.speckleDensity > 0.0)
    {
        vec2 fleck = speckle(m.localPosition, m.speckleScale, m.speckleDensity, m.speckleSize);
        color = mix(color, clamp(m.speckleColor * fleck.y, 0.0, 1.0), fleck.x);
    }
    return color;
}

// 1 where the material is much thinner than edgeThickness, 0 where it is thicker: how far the colour shifts to edgeColor.
// A mesh renderer bakes the same thickness per vertex.
float thinness(vec3 p, vec3 n, SurfaceMaterial m)
{
    if (m.edgeThickness <= 0.0) return 0.0;
    float inside = 0.0;
    for (int i = 1; i <= 5; i++)
        inside += map(p - n * m.edgeThickness * float(i) / 5.0) < 0.0 ? 1.0 : 0.0;
    return 1.0 - inside / 5.0;
}

// Metres of material between p and the sun. Exact because the SDF is known everywhere; a mesh renderer bakes it per vertex instead.
float thicknessTowardsLight(vec3 p, vec3 n, vec3 l, float radius)
{
    float stepLength = max(radius, 0.005) * 0.75;
    float inside = 0.0;
    vec3 start = p - n * 0.003;
    for (int i = 1; i <= 8; i++)
    {
        float d = map(start + l * stepLength * float(i));
        inside += stepLength * clamp(0.5 - d / stepLength, 0.0, 1.0);
    }
    return inside;
}

// Sunlight that went through the material: the thinner the material towards the sun, the more of it glows in subsurfaceColor,
// most of all when looking towards the sun.
vec3 transmission(vec3 p, vec3 n, vec3 v, vec3 l, SurfaceMaterial m)
{
    if (m.subsurfaceRadius <= 0.0) return vec3(0.0);
    float thickness = thicknessTowardsLight(p, n, l, m.subsurfaceRadius);
    float awayFromSun = clamp(0.4 - 0.6 * dot(n, l), 0.0, 1.0);
    float towardsSun = 0.6 + 1.4 * pow(max(dot(v, -l), 0.0), 3.0);
    return m.subsurfaceColor * exp(-thickness / m.subsurfaceRadius) * awayFromSun * towardsSun;
}

// GGX microfacet specular with a height-correlated Smith visibility term, as in Unity HDRP.
vec3 specularGGX(vec3 n, vec3 v, vec3 l, float roughness, vec3 f0)
{
    vec3 h = normalize(v + l);
    float nl = max(dot(n, l), 0.0);
    float nv = max(dot(n, v), 1e-4);
    float nh = max(dot(n, h), 0.0);
    float vh = max(dot(v, h), 0.0);
    float a = max(roughness * roughness, 0.002);
    float a2 = a * a;
    float dd = nh * nh * (a2 - 1.0) + 1.0;
    float distribution = a2 / (RENDER_PI * dd * dd);
    float visibility = 0.5 / max(nl * sqrt(nv * nv * (1.0 - a2) + a2) + nv * sqrt(nl * nl * (1.0 - a2) + a2), 1e-5);
    vec3 fresnel = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
    return distribution * visibility * fresnel * nl;
}

vec3 skyLight(vec3 direction)
{
    return mix(GROUND_COLOR, SKY_COLOR, smoothstep(-0.3, 0.4, direction.y));
}

vec3 acesToneMap(vec3 x)
{
    return clamp(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

vec3 background(vec3 rd)
{
    return mix(vec3(0.09, 0.09, 0.11), vec3(0.22, 0.24, 0.3), clamp(rd.y * 0.5 + 0.5, 0.0, 1.0));
}

// ---- Toon style: the cartoon look of the Unity toon shader (Unity/Assets/Mushrooms/Shaders/SdfToon.hlsl, SdfToonOutline.shader).
// Keep the two in step; these constants are the defaults of Unity's Mushroom Style asset.
const float TOON_SHADE_THRESHOLD = 0.0;       // light (-1..1, cast shadow counts as -1) below which is the shadow tone
const float TOON_LIT_THRESHOLD = 0.45;        // and above which is the lit tone; the mid tone is between
const float TOON_BAND_SOFTNESS = 0.03;
const float TOON_OCCLUSION = 0.5;             // how much ambient occlusion darkens
const vec3 TOON_SHADOW_TONE = vec3(0.55, 0.5, 0.66);
const vec3 TOON_MID_TONE = vec3(0.82, 0.8, 0.85);
const vec3 TOON_LIT_TONE = vec3(1.1);
const float TOON_HIGHLIGHT = 0.6;
const float TOON_HIGHLIGHT_THRESHOLD = 0.6;
const float TOON_HIGHLIGHT_MAX_ROUGHNESS = 0.75;
const float TOON_RIM = 0.25;
const float TOON_RIM_WIDTH = 0.3;
const float TOON_VARIATION = 0.5;             // how much of the colour patches and blotches show; speckles always do
const float TOON_OUTLINE_WIDTH = 4.0;         // pixels at 1080 pixels high
const vec3 TOON_OUTLINE_TONE = vec3(0.18, 0.13, 0.14);

// The material's colour with its patches toned down, speckles at full strength
vec3 toonColor(SurfaceMaterial m)
{
    vec3 varied = m.baseColor;
    if (m.variationScale > 0.0)
        varied = mix(varied, m.variationColor, smoothstep(0.3, 0.75, shadeDetail(m.localPosition / m.variationScale + 37.9)));
    if (m.colorVariation > 0.0)
    {
        float variation = shadeDetail(m.localPosition / max(m.detailScale * 5.0, 0.001) + 71.3) * 2.0 - 1.0;
        varied = clamp(varied * (1.0 + m.colorVariation * variation), 0.0, 1.0);
    }
    vec3 color = mix(m.baseColor, varied, TOON_VARIATION);
    if (m.speckleScale > 0.0 && m.speckleDensity > 0.0)
    {
        vec2 fleck = speckle(m.localPosition, m.speckleScale, m.speckleDensity, m.speckleSize);
        color = mix(color, clamp(m.speckleColor * fleck.y, 0.0, 1.0), fleck.x);
    }
    return color;
}

vec3 toonShade(vec3 albedo, vec3 n, vec3 v, vec3 l, float shadow, vec3 tint, SurfaceMaterial m, float occlusion)
{
    float nl = dot(n, l);
    float light = mix(-1.0, nl, shadow);
    float mid = smoothstep(TOON_SHADE_THRESHOLD - TOON_BAND_SOFTNESS, TOON_SHADE_THRESHOLD + TOON_BAND_SOFTNESS, light);
    float lit = smoothstep(TOON_LIT_THRESHOLD - TOON_BAND_SOFTNESS, TOON_LIT_THRESHOLD + TOON_BAND_SOFTNESS, light);
    vec3 tone = mix(TOON_SHADOW_TONE, mix(TOON_MID_TONE, TOON_LIT_TONE, lit) * tint, mid);
    vec3 color = albedo * tone * mix(1.0, occlusion, TOON_OCCLUSION);

    float r = mix(m.roughness, 0.1, clamp(m.wetness, 0.0, 1.0));
    float a = max(r * r, 0.01);
    float shininess = 2.0 / (a * a) - 2.0;
    float highlight = pow(clamp(dot(n, normalize(l + v)), 0.0, 1.0), shininess);
    float strength = TOON_HIGHLIGHT * clamp(2.0 * m.specular + m.wetness, 0.0, 1.0)
                   * (1.0 - smoothstep(TOON_HIGHLIGHT_MAX_ROUGHNESS * 0.6, TOON_HIGHLIGHT_MAX_ROUGHNESS, r));
    color += smoothstep(TOON_HIGHLIGHT_THRESHOLD - 0.02, TOON_HIGHLIGHT_THRESHOLD + 0.02, highlight) * strength * mid * tint;

    float rim = 1.0 - clamp(dot(n, v), 0.0, 1.0);
    color += smoothstep(1.0 - TOON_RIM_WIDTH - 0.02, 1.0 - TOON_RIM_WIDTH + 0.02, rim) * TOON_RIM * mid * tint * albedo;
    return color;
}

// Metres one pixel covers at distance t along the ray
float pixelSize(float t, bool orthographic)
{
    return orthographic ? 2.0 * uOrthoScale / uResolution.y : t / (1.6 * uResolution.y);
}

// The toon pixel. The ink line is what Unity's inverted hull draws: wherever the ray passed within the line's width of a surface
// and went on (past the silhouette, or behind a part in front of another), it takes that surface's colour, darkened.
vec3 toonPixel(vec3 ro, vec3 rd, bool orthographic)
{
    float outlinePixels = TOON_OUTLINE_WIDTH * uResolution.y / 1080.0;
    float t = 0.0;
    float hit = -1.0;
    float previous = MAX_DISTANCE;
    float previousT = 0.0;
    float closest = 1e9;
    float closestT = 0.0;
    for (int i = 0; i < MAX_STEPS; i++)
    {
        float d = map(ro + rd * t);
        if (d < SURFACE_DISTANCE) { hit = t; break; }
        // A step where the distance grows again: the ray passed its closest to a surface at the step before
        if (d > previous)
        {
            float pixels = previous / pixelSize(previousT, orthographic);
            if (pixels < closest) { closest = pixels; closestT = previousT; }
        }
        previous = d;
        previousT = t;
        t += d;
        if (t > MAX_DISTANCE) break;
    }
    if (hit < 0.0 && previous < 0.01) hit = t;

    if (closest < outlinePixels)
        return sdf_material(ro + rd * closestT).baseColor * TOON_OUTLINE_TONE;
    if (hit < 0.0)
        return background(rd);

    vec3 p = ro + rd * hit;
    vec3 n = calcNormal(p);
    SurfaceMaterial m = sdf_material(p);
    vec3 l = normalize(uLightPosition);
    vec3 albedo = mix(toonColor(m), m.edgeColor, thinness(p, n, m));
    float shadow = softShadow(p + n * 0.002, l, 30.0);
    vec3 tint = SUN_COLOR / max(max(SUN_COLOR.r, SUN_COLOR.g), SUN_COLOR.b);
    // Shown as Unity shows it: HDRP's ACES tone mapping, which the preview's tone curve matches at its exposure
    return acesToneMap(toonShade(albedo, n, -rd, l, shadow, tint, m, ambientOcclusion(p, n)) * EXPOSURE);
}

void main()
{
    vec2 uv = (gl_FragCoord.xy - uViewportOrigin - 0.5 * uResolution) / uResolution.y;

    vec3 forward = normalize(uCameraTarget - uCameraPosition);
    vec3 right = normalize(cross(forward, uCameraUp));
    vec3 up = cross(right, forward);
    bool orthographic = uOrthoScale > 0.0;
    vec3 ro = orthographic ? uCameraPosition + 2.0 * uOrthoScale * (uv.x * right + uv.y * up) : uCameraPosition;
    vec3 rd = orthographic ? forward : normalize(uv.x * right + uv.y * up + 1.6 * forward);

    if (uStyle == 1)
    {
        fragColor = vec4(pow(toonPixel(ro, rd, orthographic), vec3(1.0 / 2.2)), 1.0);
        return;
    }

    float t = raymarch(ro, rd);
    if (t > MAX_DISTANCE)
    {
        fragColor = vec4(pow(background(rd), vec3(1.0 / 2.2)), 1.0);
        return;
    }

    vec3 p = ro + rd * t;
    vec3 geometricNormal = calcNormal(p);
    SurfaceMaterial m = sdf_material(p);
    vec3 n = detailNormal(p, geometricNormal, m);
    vec3 v = -rd;
    vec3 l = normalize(uLightPosition);
    vec3 albedo = mix(variedColor(m), m.edgeColor, thinness(p, geometricNormal, m));
    albedo *= 1.0 - 0.2 * m.wetness;
    vec3 f0 = vec3(0.08 * m.specular);

    float shadow = softShadow(p + geometricNormal * 0.002, l, 30.0);
    float occlusion = ambientOcclusion(p, geometricNormal);

    // Direct sun: wrapped diffuse for scattering materials, tinted where light wraps past the terminator
    float wrap = m.subsurfaceRadius > 0.0 ? 0.35 : 0.0;
    float nl = dot(n, l);
    float diffuse = clamp((nl + wrap) / (1.0 + wrap), 0.0, 1.0);
    vec3 scatterTint = wrap > 0.0 ? mix(m.subsurfaceColor, vec3(1.0), smoothstep(0.0, 0.7, nl)) : vec3(1.0);
    vec3 sun = (albedo * diffuse * scatterTint + RENDER_PI * specularGGX(n, v, l, m.roughness, f0)) * SUN_COLOR * shadow;

    // Sky and ground bounce, with rough-aware Fresnel for the reflection
    float nv = max(dot(n, v), 0.0);
    vec3 ambientFresnel = f0 + (max(vec3(1.0 - m.roughness), f0) - f0) * pow(1.0 - nv, 5.0);
    vec3 ambient = albedo * skyLight(n) + ambientFresnel * skyLight(reflect(-v, n)) * (1.0 - 0.6 * m.roughness);

    vec3 color = sun + ambient * occlusion + albedo * transmission(p, geometricNormal, v, l, m) * SUN_COLOR * 1.5;

    // Wet coat: a sharp, smooth layer that follows the shape, not the detail bumps under it
    if (m.wetness > 0.0)
    {
        float coatFresnel = 0.04 + 0.96 * pow(1.0 - max(dot(geometricNormal, v), 0.0), 5.0);
        vec3 coat = RENDER_PI * specularGGX(geometricNormal, v, l, 0.06, vec3(0.04)) * SUN_COLOR * shadow
                  + coatFresnel * skyLight(reflect(-v, geometricNormal)) * occlusion;
        color = color * (1.0 - m.wetness * coatFresnel) + m.wetness * coat;
    }

    color = acesToneMap(color * EXPOSURE);

    if (!orthographic)
    {
        float fog = 1.0 - exp(-0.002 * t * t);
        color = mix(color, background(rd), fog);
    }

    fragColor = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}`;

export function createRenderer(canvas) {
    const gl = canvas.getContext('webgl2', { preserveDrawingBuffer: true });
    if (!gl) throw new Error('This browser does not support WebGL2');

    const camera = { yaw: 0.7, pitch: 0.35, distance: 8, target: [0, 0, 0] };
    let lightPosition = [4, 6, 3];
    let style = 0;
    let program = null;
    let quadView = false;
    let rotateView = false;

    function compile(type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const log = gl.getShaderInfoLog(shader);
            gl.deleteShader(shader);
            throw new Error(log);
        }
        return shader;
    }

    function createProgram(sdfCode) {
        const vertex = compile(gl.VERTEX_SHADER, VERTEX_SHADER);
        const fragment = compile(gl.FRAGMENT_SHADER, fragmentShader(sdfCode));
        const created = gl.createProgram();
        gl.attachShader(created, vertex);
        gl.attachShader(created, fragment);
        gl.bindAttribLocation(created, 0, 'position');
        gl.linkProgram(created);
        gl.deleteShader(vertex);
        gl.deleteShader(fragment);
        if (!gl.getProgramParameter(created, gl.LINK_STATUS)) {
            const log = gl.getProgramInfoLog(created);
            gl.deleteProgram(created);
            throw new Error(log);
        }
        return created;
    }

    function setCode(sdfCode) {
        try {
            const created = createProgram(sdfCode);
            if (program) gl.deleteProgram(program);
            program = created;
            requestRender();
            return null;
        } catch (error) {
            console.error(error.message);
            return error.message;
        }
    }

    function cameraPosition() {
        const { yaw, pitch, distance, target } = camera;
        return [
            target[0] + distance * Math.cos(pitch) * Math.sin(yaw),
            target[1] + distance * Math.sin(pitch),
            target[2] + distance * Math.cos(pitch) * Math.cos(yaw),
        ];
    }

    const orbitView = () => ({ position: cameraPosition(), up: [0, 1, 0], orthoScale: 0 });

    function axisView(direction, up) {
        const far = 50;
        return {
            position: camera.target.map((value, i) => value + direction[i] * far),
            up,
            orthoScale: camera.orthoScale ?? camera.distance * 0.5 / 1.6,
        };
    }

    const quadViews = () => [
        axisView([1, 0, 0], [0, 1, 0]),
        axisView([0, 1, 0], [0, 0, -1]),
        axisView([0, 0, 1], [0, 1, 0]),
        orbitView(),
    ];

    function resize() {
        const scale = window.devicePixelRatio || 1;
        const width = Math.round(canvas.clientWidth * scale);
        const height = Math.round(canvas.clientHeight * scale);
        if (canvas.width === width && canvas.height === height) return false;
        canvas.width = width;
        canvas.height = height;
        return true;
    }

    function drawView(view, x, y, width, height) {
        gl.viewport(x, y, width, height);
        gl.uniform2f(gl.getUniformLocation(program, 'uResolution'), width, height);
        gl.uniform2f(gl.getUniformLocation(program, 'uViewportOrigin'), x, y);
        gl.uniform3fv(gl.getUniformLocation(program, 'uCameraPosition'), view.position);
        gl.uniform3fv(gl.getUniformLocation(program, 'uCameraTarget'), camera.target);
        gl.uniform3fv(gl.getUniformLocation(program, 'uCameraUp'), view.up);
        gl.uniform1f(gl.getUniformLocation(program, 'uOrthoScale'), view.orthoScale);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    const ROTATION_SPEED = Math.PI / 6;
    let lastFrameTime = null;

    function render(time) {
        renderRequested = false;
        if (rotateView) {
            if (lastFrameTime !== null) {
                camera.yaw += (time - lastFrameTime) / 1000 * ROTATION_SPEED;
                orbitChanged = true;
            }
            lastFrameTime = time;
            requestRender(false);
        } else {
            lastFrameTime = null;
        }

        const all = resize() || allChanged;
        const orbit = all || orbitChanged;
        allChanged = orbitChanged = false;
        if (!program || !orbit) return;

        gl.useProgram(program);
        gl.uniform3fv(gl.getUniformLocation(program, 'uLightPosition'), lightPosition);
        gl.uniform1i(gl.getUniformLocation(program, 'uStyle'), style);
        if (!quadView) {
            drawView(orbitView(), 0, 0, canvas.width, canvas.height);
            return;
        }

        const left = Math.floor(canvas.width / 2);
        const bottom = Math.floor(canvas.height / 2);
        const cells = [
            [0, bottom, left, canvas.height - bottom],
            [left, bottom, canvas.width - left, canvas.height - bottom],
            [0, 0, left, bottom],
            [left, 0, canvas.width - left, bottom],
        ];
        const views = quadViews();
        const first = all ? 0 : 3;
        for (let i = first; i < 4; i++) drawView(views[i], ...cells[i]);
    }

    let renderRequested = false;
    let allChanged = false;
    let orbitChanged = false;
    function requestRender(all = true) {
        if (all) allChanged = true;
        else orbitChanged = true;
        if (!renderRequested) {
            renderRequested = true;
            requestAnimationFrame(render);
        }
    }

    function setQuadView(enabled) {
        quadView = enabled;
        requestRender();
    }

    function setRotateView(enabled) {
        rotateView = enabled;
        requestRender(false);
    }

    // Frames the views on a model: any of target, distance, yaw, pitch and orthoScale (half the height an axis view shows).
    function setCamera(settings) {
        Object.assign(camera, settings);
        requestRender();
    }

    // Points the sun: any vector towards it, its length does not matter.
    function setLight(direction) {
        lightPosition = direction;
        requestRender();
    }

    // Picks the look: 'realistic' (the default) or 'toon', the cartoon look of the Unity toon shader.
    function setStyle(name) {
        style = name === 'toon' ? 1 : 0;
        requestRender();
    }

    // Draws every view right away instead of on the next animation frame, for when the pixels are read back at once.
    function renderNow() {
        allChanged = true;
        render(performance.now());
    }

    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

    let dragging = null;
    canvas.addEventListener('pointerdown', event => {
        dragging = { x: event.clientX, y: event.clientY };
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointermove', event => {
        if (!dragging) return;
        camera.yaw -= (event.clientX - dragging.x) * 0.01;
        camera.pitch = Math.max(-1.5, Math.min(1.5, camera.pitch + (event.clientY - dragging.y) * 0.01));
        dragging = { x: event.clientX, y: event.clientY };
        requestRender(false);
    });
    canvas.addEventListener('pointerup', () => dragging = null);
    canvas.addEventListener('pointercancel', () => dragging = null);

    canvas.addEventListener('wheel', event => {
        event.preventDefault();
        camera.distance = Math.max(0.5, Math.min(60, camera.distance * Math.exp(event.deltaY * 0.001)));
        requestRender();
    }, { passive: false });

    const resizeObserver = new ResizeObserver(() => requestRender());
    resizeObserver.observe(canvas);

    return {
        setCode,
        setQuadView,
        setRotateView,
        setCamera,
        setLight,
        setStyle,
        renderNow,
        dispose: () => {
            rotateView = false;
            resizeObserver.disconnect();
        },
    };
}
