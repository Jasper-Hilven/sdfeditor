namespace SDFProject;

public sealed record SDFProgram(
    SDFFunctionDefinition[] functions,
    SDFPointers pointers,
    SDFEvaluable scene);

public sealed record SDFPointers(
    Dictionary<string, SDFEvaluable> SDFEvaluablePointers,
    Dictionary<string, SDF2DEvaluable> SDF2DEvaluablePointers,
    Dictionary<string, FloatEvaluable> FloatEvaluablePointers,
    Dictionary<string, IntegerEvaluable> IntegerEvaluablePointers,
    long nextFreePointer)
{
    public bool ContainsPointer(string id) =>
        SDFEvaluablePointers.ContainsKey(id) || SDF2DEvaluablePointers.ContainsKey(id) ||
        FloatEvaluablePointers.ContainsKey(id) || IntegerEvaluablePointers.ContainsKey(id);
}

public interface SDFFunctionDefinition
{
    string name { get; }
    string id{ get; }
    string[] argumentNames{ get; }
        VariableDeclaration[] variables{ get; }
    SDFEvaluable functionBody{ get; }
    
}

public sealed record SDFFunction(
    string name,
    string id,
    string[] argumentNames,
    VariableDeclaration[] variables,
    SDFEvaluable functionBody) : SDFFunctionDefinition;

public sealed record VariableDeclaration(string name, string id, Evaluable value);

public interface VariableReference: Evaluable;
public sealed record VariableReferenceSDF(string variableId) : SDFEvaluable, VariableReference;
public sealed record VariableReferenceSDF2D(string variableId) : SDF2DEvaluable, VariableReference;
public sealed record VariableReferenceFloat(string variableId) : FloatEvaluable, VariableReference;
public sealed record VariableReferenceV3(string variableId) : V3Evaluable, VariableReference;
public sealed record VariableReferenceV2(string variableId) : V2Evaluable, VariableReference;
public sealed record VariableReferenceInt(string variableId) : IntegerEvaluable, VariableReference;


public interface Evaluable;
public sealed record SDFFunctionCall(string functionId, params Evaluable[] arguments) : SDFEvaluable;
public interface SDFEvaluable : Evaluable;
public sealed record SDFEvaluablePointerable(string PointerId) : Pointerable, SDFEvaluable;


public sealed record Empty : SDFEvaluable;
public sealed record Sphere(FloatEvaluable radius): SDFEvaluable;
public sealed record Box(V3Evaluable size) : SDFEvaluable;
public sealed record RoundBox(V3Evaluable size, FloatEvaluable roundness) : SDFEvaluable;
public sealed record Torus(FloatEvaluable innerRadius, FloatEvaluable outerRadius) : SDFEvaluable;
public sealed record BoxFrame(V3Evaluable size, FloatEvaluable edgeThickness) : SDFEvaluable;
public sealed record CappedTorus(V2Evaluable sinCos, FloatEvaluable majorRadius, FloatEvaluable minorRadius) : SDFEvaluable;
public sealed record Link(FloatEvaluable length, FloatEvaluable majorRadius, FloatEvaluable minorRadius) : SDFEvaluable;
public sealed record InfiniteCylinder(V2Evaluable center, FloatEvaluable radius) : SDFEvaluable;
public sealed record Cone(V2Evaluable sinCos, FloatEvaluable height) : SDFEvaluable;
public sealed record InfiniteCone(V2Evaluable sinCos) : SDFEvaluable;
public sealed record Plane(V3Evaluable normal, FloatEvaluable height) : SDFEvaluable;
public sealed record HexPrism(V2Evaluable size) : SDFEvaluable;
public sealed record Capsule(V3Evaluable a, V3Evaluable b, FloatEvaluable radius) : SDFEvaluable;
public sealed record VerticalCapsule(FloatEvaluable height, FloatEvaluable radius) : SDFEvaluable;
public sealed record CappedCylinder(FloatEvaluable radius, FloatEvaluable height) : SDFEvaluable;
public sealed record ArbitraryCappedCylinder(V3Evaluable a, V3Evaluable b, FloatEvaluable radius) : SDFEvaluable;
public sealed record RoundedCylinder(FloatEvaluable radius, FloatEvaluable roundness, FloatEvaluable height) : SDFEvaluable;
public sealed record CappedCone(FloatEvaluable height, FloatEvaluable bottomRadius, FloatEvaluable topRadius) : SDFEvaluable;
public sealed record ArbitraryCappedCone(V3Evaluable a, V3Evaluable b, FloatEvaluable radiusA, FloatEvaluable radiusB) : SDFEvaluable;
public sealed record SolidAngle(V2Evaluable sinCos, FloatEvaluable radius) : SDFEvaluable;
public sealed record CutSphere(FloatEvaluable radius, FloatEvaluable cutHeight) : SDFEvaluable;
public sealed record CutHollowSphere(FloatEvaluable radius, FloatEvaluable cutHeight, FloatEvaluable thickness) : SDFEvaluable;
public sealed record DeathStar(FloatEvaluable radiusA, FloatEvaluable radiusB, FloatEvaluable distance) : SDFEvaluable;
public sealed record RoundCone(FloatEvaluable bottomRadius, FloatEvaluable topRadius, FloatEvaluable height) : SDFEvaluable;
public sealed record ArbitraryRoundCone(V3Evaluable a, V3Evaluable b, FloatEvaluable radiusA, FloatEvaluable radiusB) : SDFEvaluable;
public sealed record VesicaSegment(V3Evaluable a, V3Evaluable b, FloatEvaluable width) : SDFEvaluable;
public sealed record Rhombus(FloatEvaluable lengthA, FloatEvaluable lengthB, FloatEvaluable height, FloatEvaluable roundness) : SDFEvaluable;
public sealed record Octahedron(FloatEvaluable size) : SDFEvaluable;
public sealed record OctahedronBound(FloatEvaluable size) : SDFEvaluable;
public sealed record Pyramid(FloatEvaluable height) : SDFEvaluable;
public sealed record Triangle(V3Evaluable a, V3Evaluable b, V3Evaluable c) : SDFEvaluable;
public sealed record Quad(V3Evaluable a, V3Evaluable b, V3Evaluable c, V3Evaluable d) : SDFEvaluable;
public sealed record Ellipsoid(V3Evaluable radii) : SDFEvaluable;
public sealed record TriPrism(V2Evaluable size) : SDFEvaluable;

public sealed record Revolution(SDF2DEvaluable primitive, FloatEvaluable offset) : SDFEvaluable;
public sealed record Extrusion(SDF2DEvaluable primitive, FloatEvaluable height) : SDFEvaluable;

public sealed record Elongate(SDFEvaluable primitive, V3Evaluable amount) : SDFEvaluable;
public sealed record ElongateExactInterior(SDFEvaluable primitive, V3Evaluable amount) : SDFEvaluable;
public sealed record Round(SDFEvaluable primitive, FloatEvaluable radius) : SDFEvaluable;
public sealed record Onion(SDFEvaluable primitive, FloatEvaluable thickness) : SDFEvaluable;

public sealed record Union(params SDFEvaluable[] primitives) : SDFEvaluable;
public sealed record Subtraction(SDFEvaluable a, SDFEvaluable b) : SDFEvaluable;
public sealed record Intersection(SDFEvaluable a, SDFEvaluable b) : SDFEvaluable;
public sealed record Xor(SDFEvaluable a, SDFEvaluable b) : SDFEvaluable;
public sealed record SmoothUnion(FloatEvaluable smoothness, params SDFEvaluable[] primitives) : SDFEvaluable;
public sealed record SmoothSubtraction(SDFEvaluable a, SDFEvaluable b, FloatEvaluable smoothness) : SDFEvaluable;
public sealed record SmoothIntersection(SDFEvaluable a, SDFEvaluable b, FloatEvaluable smoothness) : SDFEvaluable;

public sealed record Translate(SDFEvaluable primitive, V3Evaluable offset) : SDFEvaluable;
public sealed record Rotate(SDFEvaluable primitive, V3Evaluable axis, FloatEvaluable angle) : SDFEvaluable;
public sealed record Scale(SDFEvaluable primitive, FloatEvaluable factor) : SDFEvaluable;

public sealed record SymmetryX(SDFEvaluable primitive) : SDFEvaluable;
public sealed record SymmetryXZ(SDFEvaluable primitive) : SDFEvaluable;
public sealed record Repetition(SDFEvaluable primitive, V3Evaluable spacing) : SDFEvaluable;
public sealed record LimitedRepetition(SDFEvaluable primitive, FloatEvaluable spacing, V3Evaluable limits) : SDFEvaluable;

public sealed record Displace(SDFEvaluable primitive, SDFEvaluable displacement) : SDFEvaluable;
public sealed record Twist(SDFEvaluable primitive, FloatEvaluable amount) : SDFEvaluable;
public sealed record CheapBend(SDFEvaluable primitive, FloatEvaluable amount) : SDFEvaluable;

public interface SDF2DEvaluable : Evaluable;
public sealed record SDF2DEvaluablePointerable(string PointerId) : Pointerable, SDF2DEvaluable;
public sealed record Circle(FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Box2D(V2Evaluable size) : SDF2DEvaluable;
public sealed record RoundedBox2D(V2Evaluable size, FloatEvaluable radiusTopRight, FloatEvaluable radiusBottomRight, FloatEvaluable radiusTopLeft, FloatEvaluable radiusBottomLeft) : SDF2DEvaluable;
public sealed record OrientedBox2D(V2Evaluable a, V2Evaluable b, FloatEvaluable thickness) : SDF2DEvaluable;
public sealed record Segment2D(V2Evaluable a, V2Evaluable b) : SDF2DEvaluable;
public sealed record Rhombus2D(V2Evaluable size) : SDF2DEvaluable;
public sealed record Trapezoid2D(FloatEvaluable bottomRadius, FloatEvaluable topRadius, FloatEvaluable height) : SDF2DEvaluable;
public sealed record Parallelogram2D(FloatEvaluable width, FloatEvaluable height, FloatEvaluable skew) : SDF2DEvaluable;
public sealed record EquilateralTriangle2D(FloatEvaluable radius) : SDF2DEvaluable;
public sealed record IsoscelesTriangle2D(V2Evaluable size) : SDF2DEvaluable;
public sealed record Triangle2D(V2Evaluable a, V2Evaluable b, V2Evaluable c) : SDF2DEvaluable;
public sealed record UnevenCapsule2D(FloatEvaluable bottomRadius, FloatEvaluable topRadius, FloatEvaluable height) : SDF2DEvaluable;
public sealed record Pentagon2D(FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Hexagon2D(FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Octagon2D(FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Hexagram2D(FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Star2D(FloatEvaluable radius, IntegerEvaluable points, FloatEvaluable innerFactor) : SDF2DEvaluable;
public sealed record Pie2D(V2Evaluable sinCos, FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Arc2D(V2Evaluable sinCos, FloatEvaluable radius, FloatEvaluable thickness) : SDF2DEvaluable;
public sealed record Ring2D(V2Evaluable sinCos, FloatEvaluable radius, FloatEvaluable thickness) : SDF2DEvaluable;
public sealed record Moon2D(FloatEvaluable distance, FloatEvaluable radiusA, FloatEvaluable radiusB) : SDF2DEvaluable;
public sealed record Vesica2D(FloatEvaluable width, FloatEvaluable height) : SDF2DEvaluable;
public sealed record Egg2D(FloatEvaluable radiusA, FloatEvaluable radiusB) : SDF2DEvaluable;
public sealed record Heart2D : SDF2DEvaluable;
public sealed record Cross2D(V2Evaluable size, FloatEvaluable roundness) : SDF2DEvaluable;
public sealed record RoundedX2D(FloatEvaluable width, FloatEvaluable roundness) : SDF2DEvaluable;
public sealed record Ellipse2D(V2Evaluable radii) : SDF2DEvaluable;
public sealed record Parabola2D(FloatEvaluable k) : SDF2DEvaluable;
public sealed record Horseshoe2D(V2Evaluable sinCos, FloatEvaluable radius, V2Evaluable size) : SDF2DEvaluable;

public sealed record Round2D(SDF2DEvaluable primitive, FloatEvaluable radius) : SDF2DEvaluable;
public sealed record Onion2D(SDF2DEvaluable primitive, FloatEvaluable thickness) : SDF2DEvaluable;
public sealed record Union2D(params SDF2DEvaluable[] primitives) : SDF2DEvaluable;
public sealed record Subtraction2D(SDF2DEvaluable a, SDF2DEvaluable b) : SDF2DEvaluable;
public sealed record Intersection2D(SDF2DEvaluable a, SDF2DEvaluable b) : SDF2DEvaluable;
public sealed record SmoothUnion2D(FloatEvaluable smoothness, params SDF2DEvaluable[] primitives) : SDF2DEvaluable;
public sealed record Translate2D(SDF2DEvaluable primitive, V2Evaluable offset) : SDF2DEvaluable;
public sealed record Rotate2D(SDF2DEvaluable primitive, FloatEvaluable angle) : SDF2DEvaluable;

public interface FunctionParameterValue;
public interface FloatEvaluable : Evaluable;
public sealed record FloatEvaluablePointerable(string PointerId) : Pointerable, FloatEvaluable;
public sealed record FunctionParameterValueFloat(int functionParameterIndex): FloatEvaluable, FunctionParameterValue;
public sealed record ConstValueFloat(float value): FloatEvaluable;

public record V3(float x, float y, float z);

public interface V3Evaluable : Evaluable;
public sealed record FunctionParameterValueV3(int functionParameterIndex): V3Evaluable, FunctionParameterValue;
public sealed record ConstValueV3(V3 v3): V3Evaluable;


public record V2(float x, float y);
public interface V2Evaluable : Evaluable;
public sealed record FunctionParameterValueV2(int functionParameterIndex): V2Evaluable, FunctionParameterValue;
public sealed record ConstValueV2(V2 v2): V2Evaluable;

public interface IntegerEvaluable : Evaluable;
public sealed record IntegerEvaluablePointerable(string PointerId) : Pointerable, IntegerEvaluable;
public sealed record FunctionParameterValueInt(int functionParameterIndex): IntegerEvaluable, FunctionParameterValue;
public sealed record ConstValueInt(int value): IntegerEvaluable;

public abstract record Pointerable
{
    string PointerId;
}
