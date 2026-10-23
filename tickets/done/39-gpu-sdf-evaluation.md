# 39 — SDF evaluation and mesh-reduction scoring on the GPU

Meshing every model takes about 2 hours at the default 128 cells, and most of that is evaluating SDF programs: dual contouring samples
the SDF millions of times per model (`SDFPipeline/Meshing/DualContouring.cs`), on the CPU's 14 cores. Ticket 37's mesh reduction adds
more evaluations (the SDF sign-bracket check) and per-round scoring of every vertex. Both are many small, identical, independent
calculations: GPU work. This ticket moves them there, behind a switch, with the CPU path kept as the reference and the fallback.

Depends on: 37 (the batched simplifier and its timing table per phase, which decides what is worth moving), the SDF code generators
(`SDFProject/Calculation/Webgl2CodeGenerator.cs`, `Webgl2SdfLibrary.cs`, `SDFProject/ShaderExport/SdfShaderExporter.cs`).

## What was decided

- **GPU API: WebGPU through Silk.NET over wgpu-native 2.23.0** (NuGet `Silk.NET.WebGPU`, `.Extensions.WGPU`, `.Native.WGPU`, all pinned in
  `SDFPipeline.csproj`; Metal on the Mac, Vulkan or DX12 elsewhere). It runs headless from the CLI: an adapter, a device and compute
  dispatches need no window. Veldrid and a Metal binding were not needed.
- **Shader language: the WebGL 2 generator's own GLSL.** wgpu-native takes a GLSL 450 compute shader and translates it (naga), so there is
  no second generator to keep in step: `Webgl2CodeGenerator.GenerateDistanceCode` gives the library plus `sdf_scene`, and
  `SDFPipeline/Gpu/GpuSdf.cs` wraps it in one compute shader with a mode switch (distances, gradients, tetrahedron gradients, the
  simplifier's sign bracket, the contouring's edge crossings), so a program is compiled once. Every node type of the CPU evaluator has a
  GPU form because it is the browser renderer's code.
- **Distances only.** Materials stay on the CPU: the mesher's material step takes the dominant material source of a point
  (`Surface`), which the GLSL material function (mixed structs for the preview) does not give, and it is a small share of the time.

## What was done

- `SDFPipeline/Gpu/`: `GpuContext` (device, buffers, dispatch, readback, errors; one per process, null without a GPU or with the
  environment variable `SDF_NO_GPU`), `GpuSdf` (the kernels, below), `GpuNotes`.
- **Contouring** (`DualContouring.Contour(sdf, grid, gpu)`): the octree's node centres level by level in one batch each; then, for
  batches of 512 blocks of 8 cells, one dispatch for the clipped SDF at the 729 corners and 584 sub-cube centres of each block and one
  for every sign-changing grid edge's crossing (the port of `FindRoot` and `Normal`, including the kink test), read back and handed
  to the unchanged `BlockContourer` (the next batch is computed while the CPU contours this one); the quads' facing tests
  (`Assemble`) take their gradients from one batch. Sample positions are made on the CPU with the CPU's arithmetic: a shape cut
  exactly on a grid plane (the amanita scout's eye) gave different signs when the GPU computed `origin + i * cell` with a fused
  multiply-add, and a different mesh.
- **Simplifier** (`QuadricSimplifier`, `Gpu` property): the round's sign-bracket checks of the folds chosen (the 37 table's biggest SDF
  cost: 106 CPU seconds on the kitchen room) are one GPU batch; the facing tests' tetrahedron gradients, asked for in scoring and in the
  sequential fold phase, are noted first (a collecting pass of `BestFold` / `KeepsFacing`), answered in one batch and read from a
  cache (half-edge folds never move a vertex, so the cache stays valid for the whole run). The gradient prefill costs about as much
  as the scoring it speeds up, so it switches itself on only once a round's scoring is seen to spend over half its time on gradients
  (and the folds phase over 30 %).
- `MeshingOptions.UseGpu` (off by default in the library, so the tests and callers keep the CPU as the reference) and
  `GpuMinPredictedCpuSeconds` (10): a mesh goes to the GPU only if the CPU is predicted to spend that long on its SDF samples
  (500 samples per squared cell count along the grid, times the CPU's measured cost of one evaluation on 300 points of the grid); the
  GPU's fixed cost per mesh is 0.1 to 0.3 s. A program that does not compile for the GPU is meshed on the CPU, with a note on stderr.
- CLI: `--gpu` (every mesh on the GPU, fails if there is none) and `--cpu` on `sdf mesh` and `sdf unity`, accepted and ignored by
  `sdf level` (a level's ground is a height field, no SDF program); by default the GPU where it pays. `sdf mesh` prints which it uses.
  `--timings` now also prints each mesh's contouring time. `SDF_GPU_TRACE=1` prints the prediction and the shader compile time.
- Tests (`SDFPipeline.Tests/Gpu/GpuSdfTests.cs`, 69 tests, all passing; the ones needing a GPU are skipped with the reason where there
  is none, and the node-type theory returns early): all 61 buildable node types give the CPU's distances (within 2e-4 and the same
  side) on random points; four real models (amanita scout, kitchen room, island underside, toy castle) the same within 1e-4 of their
  size; contouring gives the CPU's cells and vertices (same count, 98 % within 0.02 cells, all inside their cell, same triangle
  count), the same mesh every time, and block results do not depend on the batch; the sign bracket matches on 3,000 segments; the
  simplifier gives the same triangle counts (662 and 106 on the test eye); meshing a whole model with the GPU on is closed and
  within 8 % of the CPU's triangles. The whole SDFPipeline.Tests suite passes (1,431).

## Numbers

M4 Max, 14 cores, 128 cells, one process per run (wall seconds including the CLI's start, two runs each):

| Model | `--cpu` | `--gpu` (every mesh) | default (where it pays) |
|---|---|---|---|
| amanita-scout (3 meshes) | 3.2 | 3.3 | 3.5 |
| castle0 (14 meshes) | 10.0 | 10.2 | 9.9 |
| island-underside | 6.0 | 5.9 | 5.8 |
| toy-castle0 (8 meshes) | 5.4 | 5.6 | 5.6 |
| **kitchen-room** | **24.9** | **11.3** | **11.2** |

Directories with `--lods 3`, the models meshed 4 at a time as in a full run: kitchen (61 models) 64.3 s on the CPU, 42.1 s by default;
games (58) 85.6 s, 58.2 s; machines (94) 16.7 s, 18.1 s (all cheap programs, the GPU does not pay, within noise). So about a third off on
the models whose SDFs are big, nothing for the rest.

Where the kitchen room's time goes (CPU, then default): contouring 4.75 s, 1.05 s; the sign bracket 106 CPU seconds (check-ahead phase
5.99 s wall), 2.0 CPU seconds (0.54 s); scoring 6.08 s, 4.79 s; the sequential folds 5.72 s, 2.55 s.

Why the rest does not move: after the SDF work leaves the CPU, a model's time is the simplifier's own bookkeeping (rings, quadrics,
manifold checks, tracked points: scoring and the sequential folds) and the per-vertex material, thickness and occlusion samples, which
this ticket's list leaves on the CPU, and the GPU's fixed cost per mesh. The small models (most of the library) never were SDF-bound.

How close the meshes are: on the same contour the simplifier's choices are identical with the GPU (the same triangle counts at every
level in the toy castle test). The contour itself matches up to float rounding of the edge crossings (up to 7e-5 of a cell in the
position along an edge, 5e-5 in normals); where the QEF is ill-conditioned that moves a vertex, about 0.5 % of them by over 0.02 of a
cell, in the worst case by up to a cell, and the simplifier then folds a little differently: final triangle counts differ by about a
per cent (the toy castle's parts by -2 % to +5 %; its 92-triangle merlon by 12). The same program gives the same mesh every time and for
any batch size. It is not bit-identical to the CPU's, and cannot be (different float arithmetic); a mesh is valid either way.

Things found on the way (kept in the code as comments): integers carried as float bits in a GPU buffer arrive as denormals and are
flushed (whole numbers as floats instead); `isnan` and `inf` handling survives Metal's fast math in these programs, but the clip
reports 1e30 rather than infinity for an empty SDF; one call site of the scene function per shader keeps the compile short.

## Status

Done, in the main checkout, pipeline only; committed. No full re-mesh was run (37's last step).

## Next (for the agent that takes ticket 37's last step)

1. Run 37's full re-mesh with `sdf unity` (the default picks the GPU for the meshes it pays for; add `--gpu` only to see it in the log):
   the GPU path needs nothing else, and falls back to the CPU on a machine without one. Expect the kitchen-like, big-SDF models to be a
   third faster and the rest unchanged, so a full run is still long: run it in the foreground.
2. If a later pass wants more: the simplifier's scoring and sequential fold phases are now the biggest wall phases of the big models
   (CPU bookkeeping, not SDF samples); the per-vertex material, thickness and occlusion samples (`MeshOne`) are SDF-bound and could
   batch like the bracket does; the GPU's 0.1 to 0.3 s per mesh could be shared by compiling a model's parts in one go.
