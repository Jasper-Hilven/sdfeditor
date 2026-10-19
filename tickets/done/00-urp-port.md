# 00 — URP port

Move the Unity project from HDRP to URP, so one project builds for Mac, Windows and the web. The toon look must stay as it is.

## Why

HDRP cannot build for WebGL (nor for Unity 6's WebGPU backend); only URP and the Built-in pipeline can. On URP, Unity's WebGL
exporter replaces a custom browser renderer. URP's lower fixed cost per frame also suits the integrated-GPU min-spec. The toon look
lights itself in hand-written HLSL, so it depends little on the pipeline; what is lost (HDRP's sky, fog, the realistic
`SdfSurface.shadergraph` look) is unused.

## Current state

- `Unity/Packages/manifest.json`: `com.unity.render-pipelines.high-definition` 17.3.0, Unity 6000.3.
- `Unity/Assets/Mushrooms/Shaders/`
  - `SdfToon.shader`: hand-written, draws like HDRP's Unlit (ForwardOnly, colour not scaled by exposure) and lights itself. Includes
    HDRP's `ShaderVariables.hlsl`, `ShaderPass.cs.hlsl` and `HDShadow.hlsl`; reads the sun from `_DirectionalLightDatas` and samples its
    shadow through `HDShadowContext`. Has a depth prepass pass for HDRP's forward-only opaques.
  - `SdfToonOutline.shader`: the inverted-hull ink line.
  - `SdfToon.hlsl`: the toon shading function (`SdfToonShade`): bands, highlight, rim, glow. Pipeline-independent; keep.
  - `SdfSurface.hlsl`: unpacks the per-vertex data (colour, UV0–UV6). Pipeline-independent; keep.
  - `SdfLodFade.hlsl`: LOD cross-fade dither, written after HDRP's `ComputeFadeMaskSeed`.
  - `SdfSurface.shadergraph`: the old realistic look, unused since builder version 8.
- `Unity/Assets/Mushrooms/Editor/`
  - `HdrpProjectSetup.cs`: makes HDRP the pipeline, linear colour, LOD cross-fade on every quality level, raises HDRP's subsurface
    sample budget, clears the diffusion profile list. Also builds stale prefabs and opens the scene when the editor loads.
  - `MushroomScene.cs`: builds `Assets/Scenes/Mushrooms.unity`: camera with `HDAdditionalCameraData` (TAA), sun as an HD light
    (100,000 lux, 5600 K), a volume profile with physically based sky, fixed exposure 14, ACES tone mapping, contact shadows.
  - `MushroomSnapshot.cs`: renders snapshots and LOD captures; copies `HDAdditionalCameraData`.
  - `MushroomBuilder.cs`: glb → prefab, toon only. Pipeline-independent except comments.
  - `MushroomStyle.cs`: toon settings asset.

## Work

1. **Packages**: replace HDRP with `com.unity.render-pipelines.universal` (same 17.x line). Remove HDRP assets
   (`Assets/HDRPDefaultResources`, `Assets/Settings/HDRenderPipelineAsset.asset`, the volume profile in `Assets/Scenes/Mushrooms/`).
2. **`SdfToon.shader`** on URP:
   - `Tags { "RenderPipeline"="UniversalPipeline" }`; passes `UniversalForward` (or `UniversalForwardOnly`), `ShadowCaster`,
     `DepthOnly`, `DepthNormals`.
   - Includes: URP `Core.hlsl`, `Lighting.hlsl`, `Shadows.hlsl`. Sun from `GetMainLight(shadowCoord)`; shadow attenuation from it
     replaces the HDRP shadow context. Keep calling `SdfToonShade` with the same inputs, so the look does not change.
   - Colour: URP has no exposure to undo; check the result matches with the scene's lighting (see step 5).
   - `#pragma multi_compile_instancing` and instancing macros (`UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_SETUP_INSTANCE_ID`): ticket 03
     draws foliage with `Graphics.RenderMeshInstanced`.
   - LOD cross-fade: `#pragma multi_compile _ LOD_FADE_CROSSFADE` and URP's `LODFadeCrossFade` (from
     `Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl`) instead of `SdfLodFade.hlsl`, or port that file's
     seed to URP's; outline, surface and depth passes must drop the same pixels.
   - Main-light shadow keywords: `_MAIN_LIGHT_SHADOWS`, `_MAIN_LIGHT_SHADOWS_CASCADE`, `_SHADOWS_SOFT`.
3. **`SdfToonOutline.shader`**: same pipeline tags and includes, instancing, cross-fade; cull front, no shadow casting.
4. **Project setup**: rename `HdrpProjectSetup.cs` → `RenderPipelineSetup.cs`. It creates/assigns a URP asset and renderer, linear
   colour, and two quality levels:
   - **High**: shadow distance 60 m, 4 cascades, 2048–4096 shadow map, MSAA 4× (or FXAA/SMAA if MSAA breaks the outline), render
     scale 1.
   - **Integrated GPU / Web**: shadow distance 30 m, 1 cascade, 1024 shadow map, render scale 0.75, FXAA, no HDR, LOD bias 0.7.
   - LOD cross-fade on for both. Drop the diffusion-profile and subsurface-budget code.
5. **Scene** (`MushroomScene.cs`): camera without `HDAdditionalCameraData` (URP additional camera data if needed, anti-aliasing per
   preset); sun as a plain directional light at a URP-sensible intensity (≈ 1–2, colour temperature 5600 K) tuned so the toon bands
   look as before; a gradient or procedural skybox; post-processing volume with tone mapping (ACES or neutral, whichever matches the
   current look best) — no fog. Ground material on URP Lit (or the toon shader).
6. **Snapshots** (`MushroomSnapshot.cs`): drop `HDAdditionalCameraData` copying.
7. **Delete** `SdfSurface.shadergraph` and any HDRP-only code paths left; update comments that mention HDRP (builder, shaders).
8. Raise `MushroomBuilder.BuilderVersion` so prefabs rebuild once.
9. **Web build**: add a WebGL2 build target (and a menu item or run config to build it), compression Brotli or gzip; build the
   Mushrooms scene and open it in a browser.

## Acceptance

- Project opens with no HDRP package and no compile errors; Mushrooms scene renders.
- Toon look matches the HDRP one: compare `MushroomSnapshot` captures before (take them first, on HDRP) and after — bands,
  highlight, rim, glow, outline width and colour, speckles and variation.
- LOD groups cross-fade without popping; the outline fades with its surface.
- Sun shadows land on models and the ground, on both presets.
- A WebGL build of the Mushrooms scene runs in Chrome and Safari on the M4 Max.
- An instanced draw (`Graphics.RenderMeshInstanced`) of a prefab mesh with the toon material renders correctly (smoke test for 03).

## Notes

- Take the "before" snapshots on HDRP before changing anything.
- Keep the per-vertex data layout (`SdfSurface.hlsl`, `MushroomBuilder.Fill`) untouched; it is shared with the IDE's preview.

## Status

Done (not committed):

- Packages: HDRP replaced by `com.unity.render-pipelines.universal` 17.3.0. HDRP assets, `HDRPProjectSettings.asset`, the HDRP volume
  profile, `SdfSurface.shadergraph` and `SdfLodFade.hlsl` deleted. `Mushrooms.unity` and `Forest Floor.mat` were regenerated
  by the builder, not hand-edited (the scene kept its GUID).
- `SdfToon.shader`: URP passes `UniversalForwardOnly`, `ShadowCaster`, `DepthOnly`, `DepthNormals`; sun from `_MainLight*` plus
  `GetMainLight(shadowCoord, positionWS, 1)` (cascades, soft shadows, distance fade); instancing in every pass; cross-fade through URP's
  `LODFadeCrossFade` (dither on the screen position, so outline, surface, depth and shadow drop the same pixels); target 3.5 (WebGL 2).
  `SdfToonShade` is called with unchanged inputs. `SdfToonOutline.shader` works the same way, with front-face culling, no shadow
  caster, and depth/depth-normals passes so depth priming keeps it.
- `RenderPipelineSetup.cs` (was `HdrpProjectSetup.cs`): one forward `Universal Renderer`, `URP High` / `URP Integrated GPU and Web`
  assets and exactly two quality levels with the ticket's values. Standalone starts on High; WebGL, iOS and Android start on the low
  preset. The presets are code: they are put back on every editor load.
- `QualityPresetCamera` (runtime, `Assets/Mushrooms/Runtime/`): FXAA on the camera when the active asset has no MSAA.
- `MushroomScene.cs`: URP camera (post-processing on), a plain directional sun at 1.5 and 5600 K with a warm filter, `Skybox/Procedural`,
  flat ambient, and a volume `Post Processing.asset` with ACES, bloom 0.175 (threshold 0) and post exposure -0.26 EV. HDRP's bloom
  *lerped* 15 % of the blur in and its physically based sky warmed the sun; the filter, bloom and exposure values were fitted to the
  HDRP snapshots. Ground is on URP Lit.
- `MushroomSnapshot.cs`: no HDRP camera data; copies the URP camera settings and the preset's anti-aliasing (MSAA samples on the render
  target). A new batch entry `CaptureLevelsOfDetail` runs without the editor window.
- `MushroomBuilder.BuilderVersion` 9; the HDRP-only realistic-look cleanup is gone; comments updated.
- Web: `WebBuild.cs` (Tools > SDF Mushrooms > Build Web / Build and Run Web; batch `SdfMushrooms.Editor.WebBuild.Build`), WebGL 2 only,
  gzip with decompression fallback, output `Unity/Builds/Web` (git-ignored). Run config `.run/Unity Web Build.run.xml` builds, serves on
  :8642 and opens the browser.

Verified (batchmode, M4 Max):

- Project opens with no `error CS` and no shader errors for the toon shaders; prefabs rebuilt; scene built.
- HDRP "before" snapshots taken first. URP "after" lit-band colour per channel is within about 1–4 % of HDRP; mean absolute difference
  is about 1.5–3 sRGB levels per close-up. Bands, highlight, rim, outline, speckles and variation match by eye. The willow glow is
  slightly different: URP's bloom is additive, HDRP's was a blend.
- LOD snapshots: cross-fade captures show both levels' pixels (dithered).
- Sun shadows land on the models and the ground on High. On the web preset the close-ups have shadows; the overview camera is beyond
  the 30 m shadow distance, so it shows none.
- `Graphics.RenderMeshInstanced` of the amanita's meshes (64 instances, toon + outline) renders correctly, with shadows.
- WebGL build succeeds (23 MB, about 2 min) and runs in headless Chrome (WebGL 2). Console: only URP's "shader not supported" for
  `CoreCopy`, `StencilDitherMaskSeed` (unused: cross-fade uses blue noise) and `HDRDebugView`, which are expected on WebGL.

To check by hand:

- A side-by-side look in the editor's Game view (snapshots only compared headless renders), in particular the sky, which the scene
  camera barely shows.
- LOD cross-fade *in motion*: no popping, and the outline fading with its surface.
- The web build in desktop Chrome and in Safari, with frame rate.
- Batch builds with `-buildTarget WebGL` leave the project on WebGL. The verification run switched it back to macOS. After using the
  run config, expect one platform switch when the editor next opens.
