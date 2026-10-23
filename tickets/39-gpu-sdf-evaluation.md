# 39 — SDF evaluation and mesh-reduction scoring on the GPU

Meshing every model takes about 2 hours at the default 128 cells, and most of that is evaluating SDF programs: dual contouring samples
the SDF millions of times per model (`SDFPipeline/Meshing/DualContouring.cs`), on the CPU's 14 cores. Ticket 37's mesh reduction adds
more evaluations (the SDF sign-bracket check) and per-round scoring of every vertex. Both are many small, identical, independent
calculations: GPU work. This ticket moves them there, behind a switch, with the CPU path kept as the reference and the fallback.

Depends on: 37 (the batched simplifier and its timing table per phase, which decides what is worth moving), the SDF code generators
(`SDFProject/Calculation/Webgl2CodeGenerator.cs`, `Webgl2SdfLibrary.cs`, `SDFProject/ShaderExport/SdfShaderExporter.cs`).

## Decisions to make

- **GPU API from .NET on macOS**: Metal is the only GPU API there. Candidates: Silk.NET's WebGPU bindings over wgpu-native (Metal on
  Mac, Vulkan/DX12 elsewhere; WGSL compute shaders), Veldrid (compute shaders, Metal backend), or Metal directly through a binding.
  ILGPU and ComputeSharp have no Metal backend. Pick one, pin its version, and check it runs headless from the CLI.
- **Shader language**: generate the SDF programs as compute shaders the same way the WebGL 2 generator writes GLSL: a WGSL (or MSL)
  generator next to `Webgl2CodeGenerator.cs`, sharing its node coverage, so every node type of `SDFCalculatorCPU.cs` has a GPU form.
- **What moves first**, from 37's timings: most likely (1) the meshing's SDF sampling (the grid samples, the near-surface cell test,
  the finite-difference normals), then (2) the simplifier's sign-bracket test, then (3) the simplifier's per-round scoring and
  winner picking, only if 37's table shows scoring dominates after CPU threading. Folds, topology edits and the tracked points'
  re-binding stay on the CPU.

## Steps

1. A GPU evaluator: compile an SDF program to a compute shader, upload a batch of points, read back distances (and materials where
   the caller needs them). Batches large enough to hide the transfer cost.
2. Parity tests in SDFPipeline.Tests: for every node type and a set of real models, GPU and CPU distances agree within float
   tolerance on random points near and away from the surface; signs agree everywhere the CPU value is not within tolerance of 0;
   materials agree. Skipped with a clear message when no GPU is present (CI).
3. Meshing on the GPU: `DualContouring` takes its samples from the GPU evaluator (whole blocks at once), with the same output as the
   CPU path on the parity models (vertex positions within tolerance, same topology where samples are not on a sign boundary).
4. The simplifier's SDF bracket test on the GPU; then scoring/winners if 37's numbers say so.
5. Timing: meshing and simplification of the test models, CPU vs GPU, in a table here. Test with a few models only, never a full
   re-mesh (about 2 hours); leave the full run's command to the user.

## Rules

- The default meshing resolution stays 128 cells; the GPU path must give the same meshes as the CPU path.
- `--gpu` / `--cpu` switch on `sdf mesh`, `sdf unity` and `sdf level`, defaulting to the GPU when one is found, the CPU otherwise.
- Deterministic output: the same mesh regardless of batch sizes or thread counts.

## Status

Not started. Waiting on ticket 37's timing table. The full re-mesh of every model (with 37's reduction and formats) waits for this
ticket: it runs once, on the GPU path, when 39 is done.

## Next (for the agent that takes this ticket)

1. Read 37's timing table (`tickets/37-vertex-compression.md`, "Numbers" and the simplifier timings) and `DualContouring.cs`: confirm
   where the meshing time goes (SDF sampling vs the rest) on 3-4 test models at 128 cells.
2. Choose the GPU route (WebGPU via Silk.NET over wgpu-native, or Veldrid, or Metal bindings), pin it, and prove a headless compute
   dispatch from `SDFPipeline.Cli` on this Mac (M4 Max).
3. The WGSL/MSL generator for SDF programs next to `Webgl2CodeGenerator.cs`, covering every node type of `SDFCalculatorCPU.cs`; the
   parity tests (distances within tolerance, signs, materials) on all node types and on real models.
4. Dual contouring's sampling on the GPU behind `--gpu`/`--cpu`; the same meshes as the CPU path on the parity models; then 37's
   sign-bracket test on the GPU; scoring only if the numbers say so.
5. Timings CPU vs GPU on the test models in this ticket. Never a full re-mesh here: that is 37's last step, after this ticket.
