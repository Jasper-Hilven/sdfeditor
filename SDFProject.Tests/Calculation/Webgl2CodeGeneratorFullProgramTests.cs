using SDFProject.Calculation;
using SDFProject.ProgramDefinition;
using Xunit.Abstractions;
using static SDFProject.Tests.SDFProgramTestHelpers;

namespace SDFProject.Tests;

public class Webgl2CodeGeneratorFullProgramTests(ITestOutputHelper output)
{
    private static readonly Webgl2CodeGenerator Generator = new();

    [Fact]
    public void SmoothUnionOfSphereAndBox()
    {
        var program = Program(new SmoothUnion(
            new ConstValueFloat(0.25f),
            Sphere(1),
            new Box(new ConstValueV3(new V3(0.5f, 1, 1.5f)))));

        var code = Generator.GenerateShaderCode(program);
        output.WriteLine(code);

        Assert.Equal("""
            float maxComponent(vec3 v) { return max(v.x, max(v.y, v.z)); }

            float opSmoothMin(float a, float b, float k)
            {
                if (k <= 0.0) return min(a, b);
                k *= 4.0;
                float h = max(k - abs(a - b), 0.0);
                return min(a, b) - h * h * 0.25 / k;
            }

            float sdSphere(vec3 p, float r) { return length(p) - r; }

            float sdBox(vec3 p, vec3 b)
            {
                vec3 q = abs(p) - b;
                return length(max(q, 0.0)) + min(maxComponent(q), 0.0);
            }

            float sdf_scene(vec3 p)
            {
                float d0 = sdSphere(p, 1.0);
                float d1 = sdBox(p, vec3(0.5, 1.0, 1.5));
                float d2 = opSmoothMin(d0, d1, 0.25);
                return d2;
            }
            """ + "\n\n" + Webgl2CodeGenerator.MaterialCode + "\n\n" + """
            SurfaceMaterial sdf_material(vec3 p)
            {
                return colorMaterial(vec3(0.85, 0.62, 0.42));
            }
            """, code);
    }
}
