using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator;

public interface SDFProgramOperation;

public record AddNodeNextTo(string onNodeId, SDFNodeType type) : SDFProgramOperation;

public record InlineVariable(string variableId) : SDFProgramOperation;

public record ExtractVariable(string onNodeId, string variableName): SDFProgramOperation;

public record EmptyNode(string nodeId): SDFProgramOperation; 

public record CleanEmptyNodesFromUnionsAndIntersections(): SDFProgramOperation;

public enum TransformKind { Rotate, Scale, Translate }

public record AdjustTransform(string nodeId, TransformKind kind, V3 delta) : SDFProgramOperation;

public record ToggleNodeSmoothness(string nodeId) : SDFProgramOperation;

public record ReplaceNode(string nodeId, SDFNodeType type) : SDFProgramOperation;

public record WrapInSubtraction(string nodeId) : SDFProgramOperation;

public record WrapInIntersection(string nodeId) : SDFProgramOperation;
public record WrapInDistortion(string nodeId) : SDFProgramOperation;
public record WrapInDetail(string nodeId) : SDFProgramOperation;

public record PaintNode(string nodeId, V3 color) : SDFProgramOperation;
public record SetMaterial(string nodeId, string material) : SDFProgramOperation;

public record DissolveNode(string nodeId) : SDFProgramOperation;

public record SetNodeParameter(string nodeId, string parameter, float[] components) : SDFProgramOperation;
