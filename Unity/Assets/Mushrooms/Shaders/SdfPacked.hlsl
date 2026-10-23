// The compact vertex layout of a meshed SDF model (MushroomBuilder.Fill, SdfMushrooms.SdfPackedMesh): the position is four SNorm16, the
// normal and the colours 8-bit, and the vertex shaders unpack what needs it here.
//
//   POSITION  xyz = the position over the mesh's scale, w = the scale over 128 (the scale is the mesh's largest coordinate, rounded
//             up). A mesh that is not packed (a position of three floats) arrives with w = 1, and its xyz are the position itself:
//             a w of 0.999 or more means that.
//   UV1       a packed mesh holds the position in the shape's own coordinates the same way: xyz over its own scale, w = the scale over
//             128 (the thickness it held before is not used by the toon look). Another mesh has the position and the thickness.
//
// Every other channel is read as it was (the 8-bit and half float formats widen on the way into the shader).

#ifndef SDF_PACKED_INCLUDED
#define SDF_PACKED_INCLUDED

bool SdfIsPacked(float4 position)
{
    return position.w < 0.999;
}

float3 SdfUnpackPosition(float4 position)
{
    return SdfIsPacked(position) ? position.xyz * (position.w * 128.0) : position.xyz;
}

// The vertex's UV1 as the fragment shader reads it: xyz = the position in the shape's own coordinates
float4 SdfUnpackLocal(float4 uv1, float4 position)
{
    return SdfIsPacked(position) ? float4(uv1.xyz * (uv1.w * 128.0), 0.0) : uv1;
}

#endif
