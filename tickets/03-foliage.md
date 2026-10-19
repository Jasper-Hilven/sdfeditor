# 03 — Foliage and the instanced renderer

Grass turfs and flowers scattered over the ground by baked placement data, drawn with GPU instancing in distance rings, with wind.
No compute shaders (WebGL2).

The renderer built here is **general**: it draws everything the level scatters in quantity — grass and flowers here, the trees and
crest clusters in 04, small scattered rocks if wanted — as instances, never as GameObjects. Only the few unique placed models
(castles, big rocks; ticket 02) stay ordinary static prefabs in the scene.

Depends on: 02 (material distance texture as mask), 00 (instancing in the toon shaders).

## Inputs

- Turfs: `content/models/grass/grass-turf0..4.sdf`, now ≈ 4.7–4.9k triangles at LOD0 each, meshed with `--lods 2`/`3`.
- Flowers: `content/models/flowers/flower0..4.sdf`.
- Aim for the turfs at **1.5–3k triangles at LOD0** before the density test (fewer or fatter blades, or `Detail` a little lower);
  mesh both groups with `--lods 3` so there are three levels.

## Level file additions

```
scatter
    grass-turf0 weight=3
    grass-turf1 weight=1
    grass-turf2 weight=1.5
    grass-turf3 weight=1
    grass-turf4 weight=1
    density=0.6 cluster=2 clusterScale=6 spacing=0.45 avoid=dirt,tiles,path margin=0.5 maxSlope=25
scatter
    flower0 weight=2
    flower1 weight=1
    flower2 weight=0.4
    flower3 weight=1
    flower4 weight=1
    density=0.12 cluster=3 clusterScale=10 spacing=0.6 avoid=dirt,tiles,path margin=0.8 maxSlope=20
```

## Baking

`sdf level` places instances deterministically from the seed:

- Poisson-disk sampling at `spacing`, kept with probability from `density` modulated by clustering noise (`clusterScale` metres,
  up to `cluster` × density locally), rejected inside regions in `avoid` or within `margin` of them (read from the distance field),
  above `maxSlope`, outside the play area and up the hills beyond the first few metres.
- Per instance: prototype index, position (on the ground height), yaw (random), scale (0.85–1.15), tint (the ground colour under it,
  so a turf's soil mound blends in).
- Targets: about **7k turfs** and **1.5k flowers** over the ≈ 11,000 m² of grass. The ground shader must read as grass on its own;
  turfs are clumps on top, not a carpet.
- Output `instances.bin` in the generated folder, sorted by **12.5 m cell**: a header (prototype names, each prototype's **draw
  group** — `foliage`, `trees`, … — cell grid, per-cell offset and count) then 32-byte records (prototype u16, flags u16, position
  3×f32, yaw f32, scale f32, tint RGBA8). Document the format in the reader. One file holds every scattered instance of the level;
  04 adds the trees and crest clusters to it.

## Unity: instanced renderer

A runtime component, `InstancedScatterRenderer`, that `LevelBuilder` adds to the level scene (one per level) and points at
`instances.bin` and the prototype prefabs built by `MushroomBuilder`:

- Loads instances; per prototype and LOD keeps the mesh of every part and the shared toon material (+ outline material). A
  prototype with several parts (a tree's trunk and canopy) is drawn as one instanced draw per part per LOD, all sharing the
  instance's transform.
- **Draw groups** carry their own settings: ring distances, which rings get outlines and cast shadows, fade-out distance, wind
  strength, and how the integrated-GPU / web preset scales them. Foliage uses the table below; trees get theirs in 04.
- Each frame: cull cells against the camera frustum; for visible cells sort instances into the LOD ring by distance to the camera;
  fill per-prototype, per-LOD matrix arrays; draw with `Graphics.RenderMeshInstanced` (≤ 1023 per call; batch as needed).
- **Foliage rings** (first-person, 1.5 m eye height):

  | Distance | Level | Outline | Shadows |
  |---|---|---|---|
  | 0–6 m | LOD0 | yes | cast |
  | 6–15 m | LOD1 | no | no |
  | 15–35 m | LOD2, dithered fade-out over the last 5 m | no | no |
  | > 35 m | none | | |

  Integrated GPU / web preset: density × 0.5 (drop every other instance by a per-instance hash, so the pattern stays stable) and
  distances × 0.7.
- Visible at once: ≈ 1k instances high, ≈ 400 low. About 10 prototypes × 3 levels ≈ 30 draws plus ≈ 10 outline draws.
- Draw count depends on prototypes × parts × LODs, not on the number of instances; this is what keeps the whole level within the
  integrated-GPU / web budget of ≤ 200 draws.
- Instanced objects are not static-batched (static batching merges meshes and breaks per-instance LOD); the renderer's GameObject is
  the one non-static object besides the player.
- Per-instance tint passed through `MaterialPropertyBlock`/instanced property, or packed into the instance data.
- Fade by distance in the shader (dither with the same pattern as LOD cross-fade), so the CPU never fades per instance.
- **Wind** in the toon vertex shader: sway by height above the instance's base (vertex y in model space), a world-space noise phase
  so neighbours move together, strength and speed as shader globals set by a `Wind` component. Same function for turfs, flowers
  and (in 04) tree canopies. The outline shader must sway identically.
- No colliders on foliage. (Prototypes that need collision — the trees in 04 — get static collider-only GameObjects written by
  `LevelBuilder`; the renderer itself never creates colliders.)

## Acceptance

- Grass and flowers look natural from 1.5 m: clusters, no grid pattern, nothing on dirt, path or tiles, nothing in castles.
- No popping between rings (cross-fade/dither), stable as the camera moves.
- Frame budget on the high preset: foliage ≤ 700k triangles, ≤ 50 draws; on integrated GPU / web ≤ 200k triangles.
- Wind looks gentle and coherent; outlines move with their surfaces.
- Re-baking with a different seed or density updates the scene.
- The renderer takes a second draw group (e.g. a test group of rocks) with its own rings without code changes — ready for 04.

## Status

Done (not committed):

- **Turfs**: `Detail` lowered on `grass-turf0..4.sdf` (0.4 / 0.5 / 0.36 / 0.4 / 0.42; turf1 got a `Detail` round its shape). At the
  default 128 cells with `--lods 3`, LOD0 / LOD1 / LOD2 are: turf0 2,242 / 338 / 98, turf1 2,500 / 658 / 222, turf2 1,868 / 564 / 260,
  turf3 2,038 / 510 / 222, and turf4 2,210 / 584 / 248. Only the turfs were re-meshed into `Unity/Assets/Mushrooms/Generated/grass`. The
  flowers already had three levels at 128 cells (flower0 4.8k, flower1 2k, flower2 6.8k, flower3 6.1k, flower4 12.5k at LOD0) and are
  unchanged.
- **Level file**: new sections `scatter` (repeatable: its models with `weight`, then `density`, `spacing`, and optionally `group`,
  `cluster`, `clusterScale`, `avoid` with names separated by commas, `margin`, `maxSlope`, `reach` and `scale`) and `draw <group>` (one
  per group: `rings`, and optionally `outline`, `shadows`, `lowShadows`, `blend`, `fade`, `wind`, `windHeight`, `tintHeight`,
  `lowDensity` and `lowDistance`). A scatter whose group has no `draw` section is an error at its line. Records `ScatterLayer`,
  `ScatterEntry` and `DrawGroup` are in `LevelDefinition.cs`. `content/levels/meadow.level` has the ticket's two scatters and
  `draw foliage rings=[6, 15, 35] outline=1 shadows=1 blend=1 fade=5 wind=0.04 windHeight=0.5 tintHeight=0.08 lowDensity=0.5
  lowDistance=0.7`.
- **Scatter** (`SDFPipeline/Levels/LevelScatter.cs`):
  - Bridson Poisson-disk sampling over the play area plus `reach` (3 m by default), using a seeded `Random32`.
  - A point is dropped when it is within `margin` of an avoided region (`GroundRegions.Distance`), on a placement's flattened disc or
    rotated footprint grown by the margin, steeper than `maxSlope`, or off the island.
  - Clustering: value noise turned into soft patches over about 1 / `cluster` of the ground, then scaled by bisection so the weights
    average 1 and peak at `cluster`. At `cluster=1` the scatter is even.
  - Each kept point gets a model by weight, a yaw, a scale of 0.85–1.15, the height `h(x, z)`, and a tint (below). The rules run in
    parallel, but the result is order-stable.
  - Tint: the ground material's mean colour (its base, 25 % of the way to its variation colour) in sRGB. The shader's patches are not
    followed point by point.
- **Bake**: `instances.bin` (`SDFPipeline/Levels/InstanceFile.cs`, format documented there and in the Unity reader) is sorted by 12.5 m
  cell over the island (18 × 14). Its records are 32 bytes: prototype u16, flags u16 (0), position 3 × f32, yaw, scale, tint RGBA8 and a
  u32 hash, which is used to thin the instances.
  - level.json gains `instances` (file, hash, counts per prototype and per scatter, cell grid) and `drawGroups`. `models` now also lists
    the scattered models.
  - Without a scatter, `instances.bin` is deleted.
  - `LevelModels.Exist` checks scattered models by name, without sampling their shape, which would add about 3 s.
  - The meadow: 7,205 turfs and 1,402 flowers. The bake takes 3.6 s (3.2 s before) and is byte-identical across runs.
- **Shaders**:
  - New `Assets/Mushrooms/Shaders/SdfScatter.hlsl`, compiled only in the `SDF_SCATTER` variant (`multi_compile_local`). The renderer
    turns it on in its own material copies, so other renderers compile none of it and stay SRP-batcher compatible. It adds:
    - wind: a sway in object space that grows with the square of the height above the base, with a phase that travels along the wind in
      world space plus a little per-instance jitter;
    - the per-instance tint, an instanced property, faded out over `tintHeight`;
    - the ring fade: dither from URP's LOD cross-fade texture on the instance's distance to the camera, complementary between two rings.
  - `SdfToon.shader` and `SdfToonOutline.shader` changed in only a few places: the include and pragma, one call in `SdfLodFade` (so
    every pass fades), and one line per vertex function (wind, plus tint in the forward and outline passes). Outline, depth and shadow
    sway exactly like the surface.
- **Unity** (`Assets/Level/Runtime/`):
  - `InstancedScatterRenderer`:
    - draws for every camera from `beginCameraRendering`;
    - skips cells that are out of reach or out of the frustum;
    - tests each instance's sphere against the frustum, with a 2 m margin in shadow-casting rings so shadows from just outside the
      view still fall in it;
    - puts each instance into every ring it is in (two near a border);
    - issues `Graphics.RenderMeshInstanced` per prototype × part × ring, in batches of at most 500, plus the outline draw in outlined
      rings.
    - The low preset is chosen by quality-level name, and thins instances by hash.
    - It records `LastFrame` stats.
  - `ScatterInstances` reads the bin, and `Wind` sets the `_SdfWind` global.
  - `LevelBuilder` (version 3) copies the bin to `Assets/Level/Meshes/<level>/instances.bytes`, but only when it changed. It adds a
    non-static root `Scatter` with the renderer (groups from level.json, prototypes by prefab, the shared toon and outline materials)
    and a root `Wind`.
  - `GeneratedFolderWatcher` also watches `*.bin`.
  - `LevelSnapshot` logs foliage instances, draws and triangles per eye view. It adds `foliage_close`, `foliage_rings` and
    `foliage_field` shots on both presets, plus `wind_0` / `wind_1` with the percentage of pixels that differ.
- `Meadow.unity`, the turf prefabs and meshes, `instances.bytes` and the new scripts' `.meta` files were built on the APFS clone and
  copied back. The user's editor was not running during this ticket, so its automatic rebuild was not observed.

Verified:

- `dotnet build` is clean; the two warnings in `SDFEvaluable.cs` were already there. `dotnet test`: 243 pipeline tests (26 new) and
  533 project tests pass. The new tests cover:
  - the reader: sections, comma lists, defaults and 14 error cases;
  - Poisson spacing and maximality;
  - determinism, and that a different seed gives a different scatter;
  - density within 10 % at `cluster` 1, 2 and 3, and the 3 : 1 weights;
  - that clusters are denser and leave emptier squares;
  - avoidance of regions, footprints, plateaus, slope and reach;
  - unknown regions and models reported with their line;
  - a byte-identical bin with every record in its cell, the level.json fields, and the bin deleted when nothing is scattered.
- `sdf level` bakes byte-identically: `instances.bin`, level.json and the region map are the same across three runs.
- On the clone, `LevelBuilder.BuildAllInBatch` and `LevelSnapshot.Capture` run with no compile errors.
  - The first snapshot run found a real shader error in the outline's scatter variant (`tint` used before it was set). Metal dropped
    the outline because of it. It is fixed, and later runs have no shader errors.
  - A WebGL build on the clone (`WebBuild.Build`, `-buildTarget WebGL`, which compiles every SDF_SCATTER variant for WebGL 2) succeeds
    with no shader errors: 25 MB. It was not run in a browser.
- Snapshots from 1.5 m:
  - Clumps and drifts, no grid pattern. Nothing on the dirt, path or tiles, and nothing round the castles or under the rocks.
  - Mounds take the ground's green, and outlines and shadows are on the nearest ring. Shadows from turfs just outside the view fall
    into it.
  - Ring borders show as a dithered speckle in stills, both at 6 m (where the outline dissolves) and at 15 m. The 30–35 m fade is soft.
  - The web preset shows half the instances out to about 24 m, where it ends visibly but dithered.
  - Wind moves the tips a few centimetres and keeps the base still; the outline follows its blade. 10.1 % (high) and 3.2 % (web) of
    the pixels of the close view differ between two wind phases.
- Cost per eye view at 1080p and 60° FOV. "Draws" here are `RenderMeshInstanced` calls; the triangles include outlines; shadow-caster
  triangles are counted once, though each shadow cascade they reach draws them again.

  | Preset | Views | Instances | Draws (outlines) | Triangles (outlines) | Shadow casters |
  |---|---|---|---|---|---|
  | High | eye ±x/±z, corner, foliage_* | 76–806 | 24–40 (6–10) | 301k–575k (64k–172k) | 64k–172k |
  | Integrated GPU / web | eye_+x, foliage_* | 72–187 | 29–32 (6–8) | 110k–152k (36k–60k) | 36k–60k |

  Budgets: high ≤ 700k triangles and ≤ 50 draws, met; web ≤ 200k triangles, met. With the shadow casters added, the worst high view
  (eye_-z) is 738k. Visible instances are about 600–800 on high and 130–190 on web, below the ticket's ≈ 1k / ≈ 400 estimates (web is
  0.5 density × 0.7² area).
- Second draw group without code changes: a test level on the clone only added `scatter group=rocks` (rock0, rock3, rock4 at
  scale 0.3–0.6) with `draw rocks rings=[20, 60] outline=2 shadows=1 blend=2 fade=8`. It drew 54 rocks with their own rings and
  outlines, at +6 draws on high. Re-baking it while the scene was built from the earlier bake made `BuildStale` rebuild the scene:
  "8,661 scattered".

To check by hand:

- In motion: ring cross-fades while walking and turning (they are stateless and depend on distance, so they should be stable), wind
  over time, and frame time on both presets and in the web build in a browser.
- The `Pipeline to Unity` (512) run config would re-mesh the turfs at 7.9k–9.4k triangles at LOD0, which breaks the foliage budget.
  The fast configs use `--lods 2`, which gives the third ring LOD1. Both need a per-model resolution or LOD setting, or a rule for 05.
- Flowers are now the heaviest foliage per instance (flower4 is 12.5k at LOD0 and 1.1k at LOD2). Lower their `Detail` in 05 if the
  shadow cascades matter.
- `SDF_SCATTER` doubles the toon shaders' variants, so build and shader-compile time grows.

Notes for the next tickets:

- **Adding a group (04 trees, crest clusters, small rocks)** is level-file data only: a `draw <group>` section and `scatter group=<group>`.
  - Ticket 04's tree table maps to `rings=[25, 60, 1000] outline=3 shadows=2 lowShadows=1 lowDistance=0.7`.
  - For canopy wind, use `wind=` with `windHeight=` about the tree's height. The sway goes with (height / windHeight)², so trunks
    barely move.
  - Tree colliders stay `LevelBuilder`'s job, as collider-only objects; the renderer never makes colliders.
  - A scatter with a different placement rule (a band to the crest) needs a new option in `LevelScatter`.
- **Prototypes**: `InstancedScatterRenderer.LevelsOf(prefab)` reads a prefab's LOD groups, with no instantiation. Level k is every LOD
  group's level k (its coarsest where it has fewer levels), plus renderers in no group at every level. Each part keeps its transform
  relative to the root, and is outlined if that part has an outline at any level. This also fits 06's layout of a child per instance,
  each with its own LOD group.
- **06, step 7 (castle parts as a static, windless group)**:
  - Add `draw castle rings=[...] outline=... shadows=...` with no `wind` to the level file.
  - In `LevelBuilder` after `AddScatter`, append `ScatterPrototype`s with explicit `levels` (each `ScatterPart`: mesh, transform,
    `outline`) and `group = "castle"`, plus `ScatterPlacement`s (prototype index, Unity position, rotation, scale). Placements are
    never tinted or thinned.
  - Mirrored transforms (a negative determinant) were not tried with `RenderMeshInstanced`; check culling there, as 06 says.
  - Per-part `shadows near|off` maps onto ring counts per group, or needs a per-part flag on `ScatterPart`; that flag does not exist yet.
- **Shader hooks for 06's pattern work**: everything scatter-related is in `SdfScatter.hlsl`. `SdfToon.shader`'s `Frag` is unchanged
  apart from `SdfLodFade`, which now also calls `SdfScatterFade`.
