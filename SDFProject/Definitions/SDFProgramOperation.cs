namespace SDFProject;

public interface SDFProgramOperation;

public record AddNodeNextTo(string onNodeId, SDFNodeType type) : SDFProgramOperation;

public record InlineVariable(string variableId) : SDFProgramOperation;

public record ExtractVariable(string onNodeId, string variableName): SDFProgramOperation;

public record EmptyNode(string nodeId): SDFProgramOperation; 

//Remove empty nodes from unions and intersections. If union or intsection is empty or only contains empty nodes, replace them with an empty node.
// If subtraction second node is empty, remove the subtraction and put the child of the subtraction instead of the subtraction
public record CleanEmptyNodesFromUnionsAndIntersections(): SDFProgramOperation;

// Adds the values to the node when it is a ScaleTranslateRotate, else to its parent when that is a ScaleTranslateRotate,
// else wraps the node in a new ScaleTranslateRotate with these values added to the identity transformation
public record AdjustScaleTranslateRotate(string nodeId, float scale, V3 translation, V3 rotation) : SDFProgramOperation;
