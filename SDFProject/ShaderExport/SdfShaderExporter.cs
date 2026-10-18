using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFProject.ShaderExport;

public static class SdfShaderExporter
{
    public const string ResolutionUniform = "uResolution";
    public const string CameraPositionUniform = "uCameraPosition";
    public const string CameraTargetUniform = "uCameraTarget";
    public const string CameraUpUniform = "uCameraUp";

    private static readonly Webgl2CodeGenerator Generator = new();

    public static string ToShadertoy(SDFProgram program) =>
        string.Join("\n\n", Generator.GenerateShaderCode(program), Renderer, ShadertoyEntryPoint) + "\n";

    public static string ToGlslFragment(SDFProgram program) =>
        string.Join("\n\n", GlslHeader, Generator.GenerateShaderCode(program), Renderer, GlslEntryPoint) + "\n";

    private const string GlslHeader = $$"""
        #version 300 es
        precision highp float;
        precision highp int;

        uniform vec2 {{ResolutionUniform}};
        uniform vec3 {{CameraPositionUniform}};
        uniform vec3 {{CameraTargetUniform}};
        uniform vec3 {{CameraUpUniform}};

        out vec4 fragColor;
        """;

    private const string Renderer = $$"""
        const int RENDER_MAX_STEPS = 200;
        const float RENDER_MAX_DISTANCE = 100.0;
        const float RENDER_SURFACE_DISTANCE = 0.001;
        const vec3 RENDER_LIGHT_POSITION = vec3(4.0, 6.0, 3.0);

        float renderRaymarch(vec3 ro, vec3 rd)
        {
            float t = 0.0;
            for (int i = 0; i < RENDER_MAX_STEPS; i++)
            {
                vec3 p = ro + rd * t;
                float d = {{Webgl2CodeGenerator.SceneFunctionName}}(p);
                if (d < RENDER_SURFACE_DISTANCE || t > RENDER_MAX_DISTANCE) break;
                t += d;
            }
            return t;
        }

        vec3 renderNormal(vec3 p)
        {
            const vec2 k = vec2(1.0, -1.0);
            const float e = 0.0005;
            return normalize(
                k.xyy * {{Webgl2CodeGenerator.SceneFunctionName}}(p + k.xyy * e) +
                k.yyx * {{Webgl2CodeGenerator.SceneFunctionName}}(p + k.yyx * e) +
                k.yxy * {{Webgl2CodeGenerator.SceneFunctionName}}(p + k.yxy * e) +
                k.xxx * {{Webgl2CodeGenerator.SceneFunctionName}}(p + k.xxx * e));
        }

        float renderSoftShadow(vec3 ro, vec3 rd, float maxT)
        {
            float result = 1.0;
            float t = 0.02;
            for (int i = 0; i < 64 && t < maxT; i++)
            {
                float d = {{Webgl2CodeGenerator.SceneFunctionName}}(ro + rd * t);
                if (d < RENDER_SURFACE_DISTANCE) return 0.0;
                result = min(result, 8.0 * d / t);
                t += clamp(d, 0.01, 0.5);
            }
            return clamp(result, 0.0, 1.0);
        }

        float renderAmbientOcclusion(vec3 p, vec3 n)
        {
            float occlusion = 0.0;
            float weight = 1.0;
            for (int i = 1; i <= 5; i++)
            {
                float h = 0.03 * float(i);
                occlusion += (h - {{Webgl2CodeGenerator.SceneFunctionName}}(p + n * h)) * weight;
                weight *= 0.85;
            }
            return clamp(1.0 - 4.0 * occlusion, 0.0, 1.0);
        }

        vec3 renderBackground(vec3 rd)
        {
            return mix(vec3(0.09, 0.09, 0.11), vec3(0.22, 0.24, 0.3), clamp(rd.y * 0.5 + 0.5, 0.0, 1.0));
        }

        vec4 renderPixel(vec2 pixel, vec2 resolution, vec3 cameraPosition, vec3 cameraTarget, vec3 cameraUp)
        {
            vec2 uv = (pixel - 0.5 * resolution) / resolution.y;

            vec3 forward = normalize(cameraTarget - cameraPosition);
            vec3 right = normalize(cross(forward, cameraUp));
            vec3 up = cross(right, forward);
            vec3 ro = cameraPosition;
            vec3 rd = normalize(uv.x * right + uv.y * up + 1.6 * forward);

            float t = renderRaymarch(ro, rd);
            if (t > RENDER_MAX_DISTANCE)
                return vec4(pow(renderBackground(rd), vec3(1.0 / 2.2)), 1.0);

            vec3 p = ro + rd * t;
            vec3 n = renderNormal(p);

            vec3 toLight = RENDER_LIGHT_POSITION - p;
            float lightDistance = length(toLight);
            vec3 l = toLight / lightDistance;
            vec3 h = normalize(l - rd);

            float attenuation = 1.0 / (1.0 + 0.02 * lightDistance * lightDistance);
            float shadow = renderSoftShadow(p + n * 0.002, l, lightDistance);
            float diffuse = max(dot(n, l), 0.0);
            float specular = pow(max(dot(n, h), 0.0), 48.0) * diffuse;
            float occlusion = renderAmbientOcclusion(p, n);

            vec3 albedo = sdf_material(p).baseColor;
            vec3 lightColor = vec3(1.0, 0.95, 0.88) * 3.5;
            vec3 color = albedo * vec3(0.12, 0.13, 0.17) * occlusion
                       + (albedo * diffuse + 0.5 * specular) * lightColor * attenuation * shadow;

            color = 1.0 - exp(-1.4 * color);

            float fog = 1.0 - exp(-0.002 * t * t);
            color = mix(color, renderBackground(rd), fog);

            return vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
        }
        """;

    private const string ShadertoyEntryPoint = """
        void mainImage(out vec4 fragColor, in vec2 fragCoord)
        {
            float yaw = 0.7 + 0.5235988 * iTime;
            float pitch = 0.35;
            if (iMouse.z > 0.0)
            {
                yaw -= (iMouse.x - iMouse.z) * 0.01;
                pitch = clamp(pitch - (iMouse.y - abs(iMouse.w)) * 0.01, -1.5, 1.5);
            }
            float orbitDistance = 8.0;
            vec3 target = vec3(0.0);
            vec3 cameraPosition = target + orbitDistance * vec3(cos(pitch) * sin(yaw), sin(pitch), cos(pitch) * cos(yaw));
            fragColor = renderPixel(fragCoord, iResolution.xy, cameraPosition, target, vec3(0.0, 1.0, 0.0));
        }
        """;

    private const string GlslEntryPoint = $$"""
        void main()
        {
            fragColor = renderPixel(gl_FragCoord.xy, {{ResolutionUniform}}, {{CameraPositionUniform}}, {{CameraTargetUniform}}, {{CameraUpUniform}});
        }
        """;
}
