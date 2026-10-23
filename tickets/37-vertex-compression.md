# 37 — Mesh reduction and vertex compression (WebGL build size)

The last release WebGL build was **174.5 MB compressed**; its uncompressed report says Meshes 536 MB of 600 MB. CrazyGames wants
≤ 250 MB in all and ideally ≤ 50 MB for the first download (ticket 38 streams the rest). This ticket shrinks the meshes in three ways:
fewer triangles (a new simplifier in the SDF pipeline), smaller vertices (a compact vertex layout in the Unity builders and shaders), and
better compression (vertex order, Brotli).

**Status**: code done and tested on a few models and one scene. The full re-mesh, the full Unity rebuild and the release web build are
**not run**: they wait for ticket 39 (GPU SDF evaluation), see "Left to do".

## Numbers (read these first)

Tested on 6 models (coffee-bean0, amanita-scout, toy-castle0, castle0, island-underside, kitchen-room), all at the default 128 cells,
and fungal's scene. Nothing was re-meshed at 256 or 512.

| What | Before | After |
|---|---|---|
| Vertex data per vertex (all channels) | 144 B (160 with a pattern) | **60 B** (68 with a pattern); 66 to 72 B where a mesh has a coordinate over 127 m |
| The 6 models' meshes (vertex + index data, as built) | 34.6 MB | **16.8 MB** (-51 %; includes the new float collider meshes, 0.35 MB) |
| The same data compressed, per vertex (gzip 9, 300 old vs 99 new assets) | 45.5 B | **21.2 B** (-53 %) |
| Brotli 9 against gzip 9 on that data | -7 % | -14 % |
| LOD0 triangles of the 6 models (greedy simplifier then, batched now; both at 128 cells) | 75,651 | **59,544** (-21 %) |
| Same, all three levels (LOD0 + LOD1 + LOD2) | 100,640 | **83,270** (-17 %) |

**Extrapolation to the release build** (not measured; no web build was run): meshes are about 156 of the 174.5 MB compressed (536 of
600 MB uncompressed, which at the measured 3.4:1 is 156 MB). The compact vertices take that to about 0.43 of itself with Brotli (0.47 from
the vertex size, a further 0.93 from Brotli over gzip), about 67 MB; with the 17 to 21 % fewer triangles the mesh part is about 58 MB; the
other 18 MB stay. **Expect about 75 to 85 MB compressed** for the whole build, down from 174.5 MB, before any streaming (ticket 38). The
uncompressed meshes go from 536 MB to about 230 MB. The 50 MB first download is ticket 38's.

Measured before and after, with the same glbs (the old layout was rebuilt in the same editor with `-floatMeshes`), on the fungal level
(68 snapshots, castle courtyard and gate, stairs, eye-level views, the island from below): see "Checked by eye".

## Part A — triangle reduction (SDFPipeline)

The pipeline already simplified after contouring (`QuadricSimplifier`: edge collapses one at a time, cheapest first, merged vertex at the
quadric's optimum, locked borders for ground chunks, LODs simplified on from LOD0). The user asked for a different algorithm with a
guaranteed bound; it is now the default (`MeshingOptions.Simplifier = Batched`, `--simplifier greedy` keeps the old one).

### The batched simplifier (`SDFPipeline/Meshing/QuadricSimplifier.cs`, `CollapseBatched`)

Rounds (Wu and Kobbelt's multiple-choice decimation), each:

1. **Score** every vertex whose surroundings changed (parallel): the cost of a half-edge collapse into its best neighbour, so the vertex
   lands on one that exists and nothing moves. QEM only picks the three cheapest neighbours to price. The price is the **worst distance
   (squared) of any tracked point the fold touches** (below), plus, where the material blends, the change in the blend times the cell size.
2. **Winners** (parallel sweeps): a vertex wins if it beats every neighbour still in play, then its neighbours sit the round out; repeated
   until none is undecided. Ties go by a fixed hash of the vertex index, then the index. Order-independent, so deterministic.
3. **Fold** the cheapest third of the winners. The costly checks (the SDF sign bracket, the tracked points) run in parallel before, as
   the faces around a winner are changed by no other winner's fold; the folds are then made one at a time in order, each after a cheap look
   at the faces as they are (no pinch, no flipped face).
4. Rescore what changed. Stop when no winner is under the bound. Each LOD continues from the one before with a larger bound.

**Tracked points** (the user's point: error must not add up unseen): every vertex folded away stays as a point at its original position,
bound to the triangle of the current mesh it lies on (a bucket per triangle). A fold re-projects the points on the faces around the vertex
(and the vertex) onto the faces that replace them; it is allowed only if each stays within the bound of them, and lands on a face of its
own material (a face of two materials takes it only if half or more of its weight is on corners of its material). So **every original
vertex is within the bound of the simplified surface** (reported as `tracked` per level in `sdf mesh` output; it equals the bound at
every level). Cost of tracking on the test models: 0.2 to 2.7 s CPU of tracked-point projection per model (the projection is only the
fan around the vertex, then the faces that replace it), see the timing table.

**SDF sign bracket** (the SDF is not exact: union is not exact inside, intersection and subtraction not outside, twists may overestimate):
a fold is also allowed only if each face's centre and edge middles have the SDF change sign (or be 0) along the face's normal within the
bound either way. Five samples along the segment, not two at its ends, so a wall thinner than the bound, the same side at both ends, still
counts. Distances d/|grad d| are only reported.

**Materials** (`regions`: the dominant material per contoured vertex, the same the old simplifier used): a vertex whose neighbours all
share its region folds freely (into a border vertex too); one on a border between two regions folds only along its chain, into a
neighbour of its own region that shares a face with the other region (the border keeps its pair of regions), and only while every border
vertex folded into that chain stays within the bound of the new chord; one where three regions meet stays. Locked vertices (ground chunk
borders) stay; free ones fold into them. Material attributes are re-sampled from the SDF at the surviving vertices afterwards, as before.
UV seams do not exist in these meshes; normal discontinuities are the creases, held by the crease quadrics and by the tracked points
(a corner vertex cannot go, its point would be too far from the faces that replace it).

**Settings** (`sdf mesh` / `sdf unity`): `--simplifier batched|greedy`, `--max-error <cells>` (default 0.3 batched, 0.08 greedy; 0 off),
LOD limits 2 and 6 cells for LOD1 and LOD2 in the batched mode (`MeshingOptions.FirstBatchedLodErrorInCells`, growth 3), `--timings`
prints where the time went. The default 0.3 cells is chosen so the triangle count is at or below the greedy one: the greedy 0.08 is an
average that its worst faces overshoot by about two and a half times (measured against the SDF below), where the batched bound holds on
every vertex. Ground chunks use the batched mode with bounds of 32 mm, 12 cm and 40 cm (was 8 mm, 3 cm, 10 cm greedy): 4,212, 1,480 and
527 triangles over the test level's chunks against 4,676, 1,656 and about 625, with 2.5 to 3 times less worst error (worst 43 mm against 114 mm
at the finest level; 2.5 to 3 times less at the three levels). Neighbouring chunks share every border vertex (tested).

### Checked against the SDF, per test (SDFPipeline.Tests, `BatchedSimplificationTests`, `GroundBatchedTests`)

- **Error bound**: sphere (r 0.5, 48 cells): contoured 18,780 triangles, batched 2,310, tracked error 1.79 mm = the bound; SDF bracket
  0.33 mm before, 1.68 mm after, 0 % of 16,155 samples outside the bracket (vertices, centroids and barycentric points, more on big
  triangles). The greedy simplifier on the same: 794 triangles, bracket 4.41 mm, 11.9 % of samples outside the bound's bracket.
  Eye (two materials): 42,192 -> 4,138; bracket 0.36 -> 1.15 mm; greedy 1,496 triangles, 2.62 mm (2.1 times the bound).
  Walls (flat-heavy): 32,448 -> 64 triangles (greedy 32), bracket 0.03 mm both before and after.
- **Seams and borders**: locked vertices keep their positions; ground chunks share every border vertex at all three levels.
- **Material borders**: no vertex gains a material among its neighbours it did not have; corners where 3 regions meet survive; the border
  chain after is within 0.95 mm (bound 1.22) and 1.13 mm (bound 1.37) of the chain before; region areas change by 0.1 to 0.6 % (eye) and
  0.2 % (three balls); no triangle of a material is further than a cell and the bound from a point where the SDF gives that material just
  inside the surface.
- **Triangle counts**: the walls lose 99.8 %.
- **Same result at any thread count** (`MaxDegreeOfParallelism` 1, 2, 3, all): positions, triangles and sources identical. (The whole
  pipeline is not bit-for-bit repeatable between runs or core counts, greedy included: that was already so, upstream of the simplifier.)

### Triangle counts of the test models (128 cells, `--lods 3`)

| Model | Contoured | Greedy L0 / L1 / L2 | Batched L0 / L1 / L2 | Tracked error mm L0 / L1 / L2 |
|---|---|---|---|---|
| coffee-bean0 | 964 | 238 / 48 / 20 | 162 / 30 / 16 | 14.6 / 82 / 234 |
| amanita-scout (body, eyes, hands) | 93,176 | 6,910 / 1,782 / 736 | 4,934 / 1,136 / 834 | 4.4 / 29 / 83 |
| toy-castle0 (parts) | 115,176 | 17,748 / 3,896 / 1,588 | 12,236 / 2,516 / 1,800 | 52 / 351 / 754 |
| castle0 (parts) | 142,892 | 16,281 / 3,831 / 1,530 | 15,612 / 4,653 / 3,593 | 68 / 450 / 1,313 |
| island-underside | 222,528 | 14,386 / 3,494 / 1,876 | 10,582 / 2,310 / 1,942 | 297 / 1,982 / 5,701 |
| kitchen-room | 302,720 | 20,088 / 4,320 / 1,868 | 16,018 / 2,634 / 2,262 | 662 / 3,831 / 8,027 |
| Sum | 877,456 | 75,651 / 17,371 / 7,618 | 59,544 / 13,279 / 10,447 | |

Honest reading: at the same triangle count the greedy simplifier, which places each merged vertex at the quadric's optimum, fits the
surface about as well on spheres and better at LOD2 (a sphere: 86 triangles at 1.5 cells against 174 at 1.48 for batched in an early test), because the
batched one can only land on existing vertices. What the batched one gives is the **guarantee** (every original vertex within the bound,
checked by the SDF, materials kept in their regions) and, with the default 0.3 cells, 17 to 21 % fewer triangles in all. LOD2 has more
triangles than before (10,447 against 7,618): far levels are about a quarter of the triangles (LOD1 and LOD2 together, 28 % of the three levels), and `FirstBatchedLodErrorInCells`
or `LodErrorGrowth` raises it if they matter.

### Where the time goes (`--timings`, 128 cells, 14 cores, wall seconds summed over a model's meshes that run 4 at a time)

| Model | Meshing in all | Greedy | Score | Winners | Check ahead | Folds one by one | (merge, rebind) | Sign bracket CPU | Tracked points CPU | Folds |
|---|---|---|---|---|---|---|---|---|---|---|
| coffee-bean0 | 0.2 s | 0.55 s | 0.1 | 0.0 | 0.0 | 0.0 | 0.00, 0.00 | 0.1 | 0.0 | 474 |
| amanita-scout | 3.3 s | 2.2 s | 3.5 | 0.4 | 0.7 | 0.6 | 0.09, 0.02 | 2.9 | 0.2 | 46,171 |
| toy-castle0 | 6.1 s | 3.8 s | 5.5 | 1.0 | 1.4 | 3.2 | 0.07, 0.04 | 4.9 | 0.8 | 56,688 |
| castle0 | 12.8 s | 8.0 s | 9.7 | 1.8 | 1.8 | 8.3 | 0.05, 0.02 | 6.5 | 1.4 | 69,660 |
| island-underside | 6.7 s | 4.7 s | 3.2 | 0.2 | 0.6 | 0.8 | 0.05, 0.03 | 7.5 | 0.3 | 110,288 |
| kitchen-room | 31.3 s | 46.3 s | 8.1 | 0.3 | 6.5 | 8.3 | 0.06, 0.05 | **119.4** | 2.7 | 150,229 |
| amanita-scout, one core | 14.4 s | | 7.7 | 0.5 | 7.8 | 0.8 | | 12.3 | 0.3 | 46,171 |

Read for ticket 39: the **SDF evaluations dominate**: the sign bracket is 3 to 5 times the tracked-point work in CPU time (119 CPU
seconds on the kitchen room, parallel across 14 cores, so 6.5 s wall); scoring is the biggest wall phase (it calls the SDF through the
facing test's gradient); the sequential fold phase is mostly its cheap facing and manifold re-check, which also calls the SDF for its
gradient (the merge itself is 0.05 to 0.1 s, the tracked-point rebinding 0.01 to 0.05 s). Winner picking is cheap (0.2 to 1.8 s). Meshing
before simplification (contouring and the materials, the remainder of the "Meshing in all" column) is the rest; on the kitchen room it is
small next to the simplifier now. So what to move to the GPU first: the bracket's SDF samples, then the gradient samples in scoring.

## Part B — vertex compression (Unity)

### What a mesh held

`MushroomBuilder.Fill` wrote every attribute as Float32: position 3, normal 3, colour 4, UV0 to UV4 four each, UV5 two or three, UV6 four,
and UV7 four on meshes with a patterned material: **144 B per vertex, 148 with emission, 160 with a pattern**; no tangents, 16-bit indices
below 65,536 vertices (already). All of it is read: the toon shaders (`SdfToon`, `SdfToonFar`, `SdfToonOutline`, with `SdfToon.hlsl`,
`SdfScatter.hlsl`, `SdfPattern.hlsl`) read position, normal, colour, UV0 to UV7 (the outline reads position, normal, colour). Ranges
measured over 150 meshes:

| Channel | Holds | Range | Now |
|---|---|---|---|
| Position | metres | up to 64 m from the origin (small meshes under 2 m) | SNorm16 x4, scale in w |
| Normal | unit | -1 to 1 | SNorm8 x4 |
| Colour | base colour (linear), wetness | 0 to 1 | UNorm8 x4 |
| UV0 | roughness, specular, subsurface, occlusion | 0 to 1 | UNorm8 x4 |
| UV1 | position in the shape's frame (for the noise), thickness | up to 64 m; thickness unused by the toon look | SNorm16 x4, scale in w (thickness dropped) |
| UV2 | detail strength, scale, stretch, variation | scale 0.002 to 0.3, stretch 1 to 12 | Float16 x4 |
| UV3 | variation colour, scale | colour 0 to 1, scale up to 6 | Float16 x4 |
| UV4 | speckle colour, scale | 0 to 1, scale up to 0.45 | Float16 x4 |
| UV5 | speckle density, size, emission | 0 to 1 | UNorm8 x4 (was 2 or 3 floats) |
| UV6 | edge colour, amount | 0 to 1 | UNorm8 x4 |
| UV7 (pattern meshes) | pattern | 0 to 2 | Float16 x4 |

Stride **60 B** (68 with UV7). No channel was dropped except the thickness in UV1's w (the shader does not read it, and the field now holds
the scale). Tangents did not exist.

### Decisions

- **Positions: SNorm16 with the mesh's scale in the position's w**, not Float16 (error grows with distance from the origin) and not a
  material property or a transform (a property per mesh breaks the SRP batcher and instancing; folding it into transforms changes every
  hierarchy, LOD group and collider). The scale is the mesh's largest coordinate rounded up to a multiple of 128/32767, stored as
  `w = scale/128` (an SNorm16 holds it exactly; the builder uses the value the GPU will decode, so there is no error from it). Error is
  `scale/32767` for every vertex: 0.04 mm on a 1.3 m scout, 2 mm on a 64 m mesh. The shaders unpack with `SdfUnpackPosition`
  (`Mushrooms/Shaders/SdfPacked.hlsl`), a `w >= 0.999` meaning an unpacked mesh whose position is read as it is, so **float meshes still
  draw** (old assets, procedural meshes, a coordinate beyond 127 m such as the kitchen room's body: those keep Float32 positions and UV1;
  72 B a vertex). UV1's local position is unpacked the same way, in the vertex shader (`SdfUnpackLocal`), so the fragment shader is
  unchanged. All three toon shaders, every pass (forward, shadow caster, depth, depth normals, outline, far), decode.
- **Normals: SNorm8 x4**, not octahedral. An octahedral normal as 2 x SNorm16 is also 4 bytes, so it saves nothing over 4 x SNorm8 and
  costs a decode in every pass; 8 bits an axis (about 0.5 degrees) shows nothing in the toon look (checked by eye, bands and outlines).
  Octahedral 2 x SNorm8 (2 bytes, about 1.4 degrees) would save another 2 B a vertex if ever needed.
- **Colliders get meshes of their own** (`<model>_<part>_collider.asset`, Float32 positions and indices only): Unity hands the
  positions of a packed mesh back as fractions, which a `MeshCollider` would cook. Added cost: 0.35 MB over the 6 models.
- **Everything that read mesh data was checked**: `LevelBuilder.Mirrored` (mirrored copies for the instanced renderer) now mirrors the
  raw bytes (negates the x of the position and normal in whatever format) instead of going through `Mesh.vertices`; `CastleChecks` reads
  positions through `SdfPackedMesh.Positions`; the level's `MarkStatic` leaves **static batching** off for packed meshes (batching would
  merge the fractions; the SRP batcher draws them); `Gore.Tinted` (corpse recolouring, through `GetColors`/`GetUVs`/`SetUVs`, which convert formats) was read, not run in the batch; bounds are set from the true positions; `StaticSunShadow` and the instanced renderer draw with the same shaders.
- **Vertex cache order** (`Core/VertexCacheOptimizer.cs`, Forsyth's algorithm in C#): triangles reordered for the GPU's vertex cache,
  vertices renumbered in order of first use, lossless. On a shuffled grid it takes 2.95 transformed vertices per triangle to 0.68 (the
  ideal is 0.5). It runs in the builder, so every rebuild keeps it. **Unity's own `Mesh.OptimizeIndexBuffers` crashes the editor
  (segfault in `OptimizeVertexCache`) on these custom-layout meshes**, hence our own.
- **16-bit indices** below 65,536 vertices: already so, kept.
- **Not used**: `MeshUtility.SetMeshCompression` and the Vertex Compression player setting apply to imported meshes with standard layouts
  and would replace these formats; **Optimize Mesh Data** (`StripUnusedMeshComponents`) strips channels no material's shader reads, and
  every channel is read; the ground chunks (position and normal only, 37 MB for all levels) stay Float32 for now, as their shader and
  the static batching of the ground are separate.
- **WebGL compression: Brotli** instead of gzip (`WebBuild.cs`, one-line edit, `decompressionFallback` kept, so any static server still
  works): 7 to 14 % smaller on mesh data than gzip 9.

### Checked by eye

`PackedMeshTool.SnapshotBothWays` rebuilt 15 models of fungal (castle0, island-underside, amanita-scout, rocks, giant mushrooms,
parasol, moss turf, fungi tuft, team crown, bracket tower, inkcap, puffball) once with float meshes (`-floatMeshes`, the old layout) and
once compact, rebuilt the level scene over each, and took the level's 68 snapshots each way (1920 x 1080). Compared pixel by pixel and
by eye (castle courtyard, gate, stairs, eye-level views, the island from below): **no cracks, faceting, shimmer, outline or lighting
change**. In the median view 0.03 % of the pixels differ by more than 24 levels (of 255); the largest share that differs is 1.2 % (the island from
below), and the difference image is the animated spore particles only; on the castle the difference image shows only faint hairlines
at outlines (the normal's 8 bits) and spores. The collider checks the level snapshot logs (courtyard, wall-walk, stair, gate, tower,
rocks) give the **same results both ways**.

### Tests

- `SDFPipeline.Tests`: `BatchedSimplificationTests` (error bound against the SDF, seams, material borders, areas, material per
  triangle, determinism across thread counts, a flat-heavy mesh), `GroundBatchedTests`. The whole suite passes (1,361 tests) with
  **two thresholds relaxed** in `LevelOfDetailTests`, both about the coarsest level of four: a level must be at most 0.6 of the one before
  unless it is already at 60 triangles or fewer (the capsule with a band stops at 52), and the thin wavy rim's
  last level must be under a sixth of LOD0 (74 of 632 triangles; was under a tenth). The batched simplifier only folds into vertices that
  exist and keeps a material border's loops, so thin shells and bands plateau earlier than the greedy placement did.
- Unity EditMode: **612 tests pass**, including the new `SdfMushrooms.Tests` (`SdfPackedMeshTests`: scale covers the largest coordinate,
  quantising error within half a step, half floats are the IEEE bits, a packed mesh reads back; `VertexCacheOptimizerTests`: same
  triangles and windings, first-use numbering, 2.95 -> 0.68 vertices per triangle).

### Limits and what was not done

- Batched LOD2 has more triangles than greedy's (10,447 against 7,618 over the 6 models) and a thin shell or a material band stops
  folding at a few dozen triangles. Both are limits of folding only into existing vertices; the limits are tunable (above).
- The batched mesher takes 0.7 to 1.6 times the greedy one's wall time on the test models at 14 cores (kitchen-room faster, the others
  slower); on one core amanita-scout takes 14.4 s against 3.3 s on 14.
- Ground chunks stay Float32 (only 37 MB over all levels); the packed layout is for the models.
- `Gore.Tinted` and the corpse recolouring were read, not exercised in the batch; a packed mesh has Float16 and UNorm8 channels there.
- No release web build was run and no full rebuild, so the build-size figures above beyond the 6 models are an extrapolation.
- The pipeline's .glb output differs between runs and core counts even with the greedy simplifier (already so before this ticket);
  only the simplifier itself was made independent of the thread count, and tested.

## Left to do (the user's step, after ticket 39)

Not run on purpose (the full re-mesh takes about 2 hours; ticket 39 moves SDF evaluation to the GPU first). Then, all at the default 128:

1. Re-mesh everything, with LODs: `dotnet run --project SDFPipeline.Cli -c Release -- unity --lods 3` (the `Pipeline to Unity (fast)`
   run config with `--lods 3`; default simplifier batched, default 0.3 cells). Castles are at 128 too, no special case.
2. Rebake a level if its ground should use the batched chunks: `sdf level content/levels/<level>.level --into
   Unity/Assets/Level/Generated/<level>` (`sdf unity` already does all levels).
3. Rebuild Unity: close the editor, then `Unity -batchmode -quit -projectPath Unity -executeMethod SdfLevel.Editor.UpdateAll.UpdateInBatch`.
   The new glbs make every prefab stale, so every mesh is rebuilt in the compact format (6 models took 3 seconds). To rebuild without
   re-meshing: Tools > SDF Mushrooms > Rebuild All (`MushroomBuilder.BuildAll`), or raise `BuilderVersion` to 16.
4. Release web build: `Unity -batchmode -quit -projectPath Unity -buildTarget WebGL -executeMethod SdfMushrooms.Editor.WebBuild.Build`
   (the "Unity Web Build" run config), then read "Build Report" in `~/Library/Logs/Unity/Editor.log`: Meshes MB and the complete build
   size against the 174.5 MB / 536 MB above.
5. Compare snapshots if wanted: `PackedMeshTool.SnapshotBothWays -models a,b,c -level <level> -snapshotDir <folder>`.

## Files

- Pipeline: `SDFPipeline/Meshing/QuadricSimplifier.cs` (batched mode), `MeshingOptions.cs`, `ModelMesher.cs`, `DualContouring.cs`
  (`TriangleMesh.TrackedError`, `SimplifyTimings`), `Levels/GroundMesher.cs`, `Levels/LevelBaker.cs`, `SDFPipeline.Cli/Program.cs`.
- Unity: `Mushrooms/Core/SdfPackedMesh.cs`, `VertexCacheOptimizer.cs` (asmdef `SdfMushrooms.Core`), `Mushrooms/Tests`,
  `Mushrooms/Editor/MushroomBuilder.cs` (`Fill`, `FillCollider`, `FillFloats` for comparisons), `PackedMeshTool.cs`, `WebBuild.cs`,
  `Mushrooms/Shaders/SdfPacked.hlsl`, `SdfToon.shader`, `SdfToonFar.shader`, `SdfToonOutline.shader`, `SdfScatter.hlsl`,
  `Level/Editor/LevelBuilder.cs` (`Mirrored`, `MarkStatic`), `CastleChecks.cs`.
