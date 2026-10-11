namespace SDFProject.ProgramDefinition;

public class SDFNodeDefaultValues
{
        public static SDFEvaluable CreateNode(SDFNodeType type)
    {
        FloatEvaluable F(float value) => new ConstValueFloat(value);
        V2Evaluable V2(float x, float y) => new ConstValueV2(new V2(x, y));
        V3Evaluable V3(float x, float y, float z) => new ConstValueV3(new V3(x, y, z));

        // Angles are passed as (sin, cos)
        var sinCos30 = V2(0.5f, 0.8660254f);
        var sinCos120 = V2(0.8660254f, -0.5f);

        return type switch
        {
            SDFNodeType.Sphere => new Sphere(F(0.5f)),
            SDFNodeType.Box => new Box(V3(0.5f, 0.5f, 0.5f)),
            SDFNodeType.RoundBox => new RoundBox(V3(0.5f, 0.5f, 0.5f), F(0.1f)),
            SDFNodeType.Torus => new Torus(F(0.3f), F(0.5f)),
            SDFNodeType.BoxFrame => new BoxFrame(V3(0.5f, 0.5f, 0.5f), F(0.05f)),
            SDFNodeType.CappedTorus => new CappedTorus(sinCos120, F(0.4f), F(0.1f)),
            SDFNodeType.Link => new Link(F(0.3f), F(0.3f), F(0.1f)),
            SDFNodeType.InfiniteCylinder => new InfiniteCylinder(V2(0f, 0f), F(0.5f)),
            SDFNodeType.Cone => new Cone(sinCos30, F(1f)),
            SDFNodeType.InfiniteCone => new InfiniteCone(sinCos30),
            SDFNodeType.Plane => new Plane(V3(0f, 1f, 0f), F(0f)),
            SDFNodeType.HexPrism => new HexPrism(V2(0.5f, 0.5f)),
            SDFNodeType.Capsule => new Capsule(V3(0f, 0f, 0f), V3(0f, 1f, 0f), F(0.25f)),
            SDFNodeType.VerticalCapsule => new VerticalCapsule(F(1f), F(0.25f)),
            SDFNodeType.CappedCylinder => new CappedCylinder(F(0.5f), F(0.5f)),
            SDFNodeType.ArbitraryCappedCylinder => new ArbitraryCappedCylinder(V3(0f, 0f, 0f), V3(0f, 1f, 0f), F(0.5f)),
            SDFNodeType.RoundedCylinder => new RoundedCylinder(F(0.5f), F(0.1f), F(0.5f)),
            SDFNodeType.CappedCone => new CappedCone(F(0.5f), F(0.5f), F(0.25f)),
            SDFNodeType.ArbitraryCappedCone => new ArbitraryCappedCone(V3(0f, 0f, 0f), V3(0f, 1f, 0f), F(0.5f), F(0.25f)),
            SDFNodeType.SolidAngle => new SolidAngle(sinCos30, F(0.5f)),
            SDFNodeType.CutSphere => new CutSphere(F(0.5f), F(0.2f)),
            SDFNodeType.CutHollowSphere => new CutHollowSphere(F(0.5f), F(0.2f), F(0.05f)),
            SDFNodeType.DeathStar => new DeathStar(F(0.5f), F(0.35f), F(0.5f)),
            SDFNodeType.RoundCone => new RoundCone(F(0.5f), F(0.2f), F(0.8f)),
            SDFNodeType.ArbitraryRoundCone => new ArbitraryRoundCone(V3(0f, 0f, 0f), V3(0f, 1f, 0f), F(0.5f), F(0.2f)),
            SDFNodeType.VesicaSegment => new VesicaSegment(V3(0f, 0f, 0f), V3(0f, 1f, 0f), F(0.3f)),
            SDFNodeType.Rhombus => new Rhombus(F(0.6f), F(0.3f), F(0.1f), F(0.05f)),
            SDFNodeType.Octahedron => new Octahedron(F(0.5f)),
            SDFNodeType.OctahedronBound => new OctahedronBound(F(0.5f)),
            SDFNodeType.Pyramid => new Pyramid(F(1f)),
            SDFNodeType.Triangle => new Triangle(V3(0f, 0f, 0f), V3(1f, 0f, 0f), V3(0f, 1f, 0f)),
            SDFNodeType.Quad => new Quad(V3(0f, 0f, 0f), V3(1f, 0f, 0f), V3(1f, 1f, 0f), V3(0f, 1f, 0f)),
            SDFNodeType.Ellipsoid => new Ellipsoid(V3(0.5f, 0.3f, 0.4f)),
            SDFNodeType.TriPrism => new TriPrism(V2(0.5f, 0.5f)),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}