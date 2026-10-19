// LOD cross-fade for the hand-written toon shaders (SdfToon.shader, SdfToonOutline.shader), as HDRP's Lit does it (LitData.hlsl);
// the realistic SdfSurface.shadergraph gets the same from HDRP through its "Support LOD CrossFade" option. While a LOD group with
// cross-fade changes level it draws both levels, and each pixel is dropped from one of them by a dither pattern: unity_LODFade.x
// runs from 1 to 0 on the level that goes and from -1 to 0 on the one that comes, so the two keep complementary pixels.
//
// The pattern's seed is the pixel's direction from the camera (HDRP's ComputeFadeMaskSeed), worked out from the pixel itself rather
// than from the mesh: the ink outline is pushed out from its mesh by a few pixels, and only a seed that depends on the pixel alone
// lets the outline, its surface and their depth prepass drop the same pixels. Include after HDRP's ShaderVariables.hlsl, in passes
// with #pragma multi_compile _ LOD_FADE_CROSSFADE; without the keyword it compiles to nothing.

#ifndef SDF_LOD_FADE_INCLUDED
#define SDF_LOD_FADE_INCLUDED

void SdfLodFade(float4 positionCS)
{
#ifdef LOD_FADE_CROSSFADE
    PositionInputs posInput = GetPositionInput(positionCS.xy, _ScreenSize.zw, positionCS.z, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
    float3 v = GetWorldSpaceNormalizeViewDir(posInput.positionWS);
    LODDitheringTransition(ComputeFadeMaskSeed(v, posInput.positionSS), unity_LODFade.x);
#endif
}

#endif
