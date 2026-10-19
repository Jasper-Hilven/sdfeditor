# 01 — Ground

A level file in the repo, baked by the CLI into terrain chunks with LODs and colliders, and a Unity builder that turns the baked
level into a scene: toon-shaded grass ground, border hills, invisible walls. Grass only; other ground materials come in 02.

Depends on: 00 (URP).

## Level file

`content/levels/meadow.level`, text in the style of `.sdf` (same tokenizer/indentation conventions, `format 1` header, comments).
Units are metres; x across (150 m), z along (100 m), y up; origin at the centre of the playable area. This ticket needs:

```
format 1

play size=(150, 100)                      # the playable rectangle, centred on the origin
seed 1

hills
    ring width=28 height=10               # rise outside the rectangle, crest about 8-12 m
    noise amplitude=2.5 scale=18          # unevenness along the crest and on the slopes

edge                                      # where the island ends, behind the hill crests (the underside comes in 04)
    margin=6
```

The format must leave room for 02's regions, placements and scatter rules. Write a reader with good errors (line numbers), like
`SdfProgramFileReader`/`MaterialFile`.

### Height function

`h(x, z)` in 2D: 0 inside the play rectangle (allowing a little gentle noise, ≤ 0.3 m, kept flat near where castles will go in 02);
outside it, `d` = distance to the rectangle, height rises smoothly with `d` (smoothstep or cosine to `height` over `width`), plus
noise that grows with `d`. Keep slopes inside the rectangle walkable; the hills may be steep (they are blocked by walls anyway).

## Baking: `sdf level`

New CLI command in `SDFPipeline.Cli/Program.cs` and code in `SDFPipeline/` (e.g. `SDFPipeline/Levels/`):

```
sdf level content/levels/meadow.level --into Unity/Assets/Level/Generated/meadow
```

- **Extent**: the play rectangle plus the hill ring plus the edge margin: about 210 × 160 m.
- **Chunks**: 25 m × 25 m tiles over that extent (about 9 × 7). Each is a regular grid at **0.5 m** (50 × 50 quads, 5,000
  triangles), shared edge vertices with its neighbours so there are no cracks.
- **Simplify** with the existing `QuadricSimplifier` (`SDFPipeline/Meshing/QuadricSimplifier.cs`), keeping chunk borders fixed
  (border vertices must not move, so neighbours keep matching). Flat grass falls to ~1–2k triangles.
- **LODs**: LOD0 from the 0.5 m grid, LOD1 from 1 m, LOD2 from 2 m (or simplification steps with growing error, as `ModelMesher`
  does). Border vertices of coarser levels must still meet neighbours at the same LOD; accept small skirts (a 0.5 m vertical strip
  down from each border) to hide cracks between chunks at different LODs.
- **Vertex data**: a slim layout for the ground (position, normal; AO optional). Not the fat model layout of 7 UV sets.
- **Output** in the generated folder:
  - `chunk_<i>_<j>.glb`: one per chunk, its LODs as separate meshes (`ground`, `ground_LOD1`, …, same naming as models) so the
    existing `SdfGlb` reader can read it.
  - `level.json`: extent, chunk size and grid, play rectangle, seed, and (in later tickets) everything else Unity needs.
  - Colliders: Unity builds them from the LOD1 mesh (1 m); no separate file needed.
- Deterministic: same file and seed → byte-identical output.
- `sdf level <directory> --into <folder>` bakes every `.level` file in the directory, each into `<folder>/<level name>/`, like
  `sdf mesh <directory>` does for models. Add both forms to the CLI usage text.

## Part of "Pipeline to Unity"

Updating Unity from Rider must update the level too. The three run configs in `.run/` (`Pipeline to Unity`,
`Pipeline to Unity (fast)`, `Pipeline to Unity (superfast)`) each run, in this order:

1. `sdf mesh content/models --into Unity/Assets/Mushrooms/Generated …` (as today, with their own resolution and LODs);
2. `sdf level content/levels --into Unity/Assets/Level/Generated`.

Models first: the level places model prefabs (02, 04), so they must be built before the level scene is. Either chain them in Rider
(a `Level: Bake` config run as a "before launch" / after task of each pipeline config), or give the CLI one command that does both,
e.g. `sdf unity [--resolution n] [--lods n]` that meshes `content/models` and bakes `content/levels` into the Unity folders; prefer
the CLI command, so the order cannot be got wrong and the run configs stay one line each. The level bake has no resolution option of
its own: it is fast at full quality, so all three configs bake it the same way.

## Unity: `LevelBuilder`

New editor script, e.g. `Unity/Assets/Level/Editor/LevelBuilder.cs`, modelled on `MushroomScene.cs` / `MushroomBuilder.cs`:

- Watches `Assets/Level/Generated/<level>/` (like `GeneratedFolderWatcher`) and rebuilds when it changes; menu item
  *Tools > SDF Level > Rebuild*. It also rebuilds after `MushroomBuilder` has (re)built prefabs (its `Built` event), so placed models
  are current, and it waits for that build when both change in one pipeline run.
- Builds a **new scene per level**, `Assets/Scenes/<Level>.unity` (here `Meadow.unity`), separate from `Mushrooms.unity` (the model
  showcase, which stays as it is). The level scene holds the whole level as a **static** scene: everything generated is marked static
  (batching, occluder/occludee, navigation, contribute GI off — the toon look uses no lightmaps), so Unity can batch it and nothing
  in it is moved at runtime. Only the player controller and the foliage renderer (03) are dynamic. It is regenerated from the baked
  data on every rebuild — never edited by hand — and added to the build settings after Mushrooms.
- Contents:
  - a GameObject per chunk with a `LODGroup` over its LODs (cross-fade), static, shadow receiving and casting;
  - a `MeshCollider` per chunk from LOD1;
  - four invisible box colliders around the play rectangle, tall (≈ 20 m), so players cannot walk up the hills;
  - sun, sky and camera as in the Mushrooms scene; a simple first-person controller (CharacterController, 1.5 m eye height) to walk
    the level.
- **Ground shader**: `SdfGround.shader` (URP), toon-lit with the same `SdfToonShade` function and the `MushroomStyle` settings as the
  models, so ground and models share bands, rim and colours. Material parameters come from a material in `materials/` — create
  `materials/ground/ground_grass.sdfmat` — passed as material properties (base colour, variation colour and scale, speckles, detail),
  evaluated in world space (world XZ plays the role of the models' local position). No ink outline on the ground.

## Acceptance

- `sdf level content/levels/meadow.level --into …` bakes in a few seconds, deterministic.
- Each of the three `Pipeline to Unity` run configs meshes the models and bakes the level; afterwards `Meadow.unity` is up to date
  without touching Unity by hand.
- `Meadow.unity` is a separate scene from `Mushrooms.unity`, entirely static apart from the player (and later the foliage renderer).
- Unity builds the level scene automatically after a bake; walking in first person over the whole play area works; the walls stop
  the player at the rectangle's edge.
- No cracks between chunks at any LOD; LODs cross-fade.
- Hills read as hills from 1.5 m eye height and hide what is behind them.
- Ground looks like part of the same toon world as the models (compare next to a tree and a mushroom).
- Triangle count of all ground chunks at LOD0 reported by the bake; on screen the ground costs < 100k triangles.

## Status

Done (not committed):

- **Level file**: `content/levels/meadow.level` (sections `play`, `seed`, `hills` with `ring`/`noise`, `edge`; parameters on the section's
  line or on `name=value` lines indented below it, so 02/03's settings lines fit). Reader `SDFPipeline/Levels/LevelFile.cs` on the
  program files' tokenizer, with line-numbered errors (unknown or repeated section/parameter, missing `play`, ranges such as play noise
  ≤ 0.3 m). Records in `LevelDefinition.cs`.
- **Height**: `LevelTerrain.cs`: play-area noise (default 0.2 m at 14 m), smoothstep rise to the crest at the ring width, noise growing
  with the rise, back slope falling 35 % of the height to the edge; island = rounded rectangle at ring width + margin. Seeded integer-hash
  value noise (`ValueNoise`), so output is machine-independent.
- **Chunks**: `GroundMesher.cs`, 9 × 7 chunks of 25 m on global grid lines (neighbours compute shared borders identically). Grid
  points beyond the island edge are moved onto it; triangles with no corner on the island are dropped, so the edge is a smooth curve.
  `QuadricSimplifier` got a `locked` vertex option (open-edge vertices: chunk borders and the island edge never move); simplified
  vertices are put back on h(x, z). LOD0/1/2 from 0.5 m / 1 m / 2.083 m grids (25/12; 12.5 cells is not whole) at 0.8 / 3 / 10 cm
  simplification error; 0.5 m outward-facing skirts on chunk borders. Normals from the height function.
- **Bake**: `LevelBaker.cs` writes `chunk_<i>_<j>.glb` (meshes `ground`, `ground_LOD1`, `ground_LOD2` with POSITION + NORMAL, MSFT_lod,
  `maxError` per mesh) and `level.json` (play, hills, edge, extent, spawn, chunk grid and per-chunk triangles, LOD stats, collider LOD,
  grass material parameters from `materials/ground/ground_grass.sdfmat`). Stale chunk files are deleted; level.json is written last.
- **CLI**: `sdf level <file|directory> --into <folder>` (and `sdf level --into` for content/levels), and `sdf unity [--resolution]
  [--lods]` = `sdf mesh content/models` into `Unity/Assets/Mushrooms/Generated`, then `sdf level content/levels` into
  `Unity/Assets/Level/Generated`. The three `Pipeline to Unity` run configs are now one line each: `unity --resolution 512 --lods 3`,
  `128 / 2`, `64 / 2`.
- **Unity**: `Assets/Level/Editor/LevelBuilder.cs` (rebuilds a level's scene when its generated folder changes, after a quiet second
  and after stale model prefabs are built; on `MushroomBuilder.Built` for levels whose level.json lists those models in `"models"`; on
  editor load if stale, stamped with a hash of level.json; menu *Tools > SDF Level > Rebuild*; batch
  `SdfLevel.Editor.LevelBuilder.BuildAllInBatch`). `Meadow.unity` holds the lighting from `MushroomScene.BuildLighting` (factored out
  of the mushroom scene with `CreateCamera`), static chunk objects with cross-fading LOD groups (heights from each level's measured
  error, 2 px at 1080p, coarsest never culled) and a LOD1 `MeshCollider`, four 25 m walls, and the player (`Assets/Level/Runtime/
  FirstPersonController.cs`, CharacterController, camera at 1.5 m, 60° FOV). Added to the build settings after Mushrooms.
  `GeneratedFolderWatcher` also polls `Assets/Level/Generated`. `Assets/Level/Shaders/SdfGround.shader`: URP, same passes, instancing
  and cross-fade as SdfToon, `SdfToonShade` with the Mushroom Style; colour from the material in world space; rim set to 0 on the
  ground (from eye height almost all ground is at a grazing angle, so the rim lit the field beyond ~5 m in a ring).
- **Snapshots**: `Assets/Level/Editor/LevelSnapshot.cs`: batch `SdfLevel.Editor.LevelSnapshot.Capture -snapshotDir <dir>`, menu, or
  `Unity/Temp/sdf-level-snapshot.txt` in a running editor. Eye-height views in four directions, the corner, from above, next to a tree,
  mushroom and rock, and on the web preset; logs the ground triangles each eye view draws.

Verified:

- `dotnet build` clean; `dotnet test`: 177 pipeline tests (43 new: reader, height, chunk borders shared exactly at every LOD, skirts
  facing out, island edge, byte-identical bake) and 533 project tests pass.
- `sdf level content/levels/meadow.level --into …` bakes 63 chunks in about 1 s, byte-identical across runs. LOD0 over all chunks:
  51,182 triangles (21,056 of them skirts; flat play chunks ≈ 270 + 200 skirt, hill chunks ≈ 700 + 200), within 9.7 cm of h
  (play area ≈ 1–3 cm); LOD1 21,377; LOD2 9,216. `sdf unity --resolution 64 --lods 2` checked in a scratch copy of content.
- The open editor rebuilt `Meadow.unity` by itself after each bake (no compile or shader errors); a byte-identical re-bake does not
  rebuild. Batch mode could not open the project while the editor was open, so `BuildAllInBatch` and `LevelSnapshot.Capture` were run
  on an APFS clone of the project: both succeed with no errors.
- Snapshots (high and web presets): hills read as hills from 1.5 m and hide everything behind them; the ground sits with tree0,
  amanita-scout and rock1 in the same toon look; no cracks seen between chunks from above. The ground draws 12.7–15.3k triangles
  per eye-height view (10.1k on the web preset), far under 100k.
- Walking (CharacterController moved in script, on the clone): straight and diagonal runs from the centre stay grounded and stop at
  ±74.6 m / ±49.6 m, the walls.

To check by hand:

- Walk in Play mode (mouse look, run, jump), especially up to the walls and along the hill foot.
- LOD cross-fades in motion and cracks at chunk borders when neighbours are at different levels, from the play area.
- The look of the ground over time (speckle size and the green against the turfs once 03 scatters them), and the web build.
- Seen from outside the island, the back slopes of the hills show coarse-triangle shapes in the shadow band; never visible from inside.

Notes for the next tickets:

- Coordinates: level.json and the .glb files are in pipeline coordinates; Unity mirrors x (as for models). Placements (02) must use
  `-x` in Unity, and yaw flips sign.
- `LevelBuilder` rebuilds on `MushroomBuilder.Built` only for models listed in level.json's `"models"` array: 02 must write it.
- The reader rejects unknown sections, so 02–04 add theirs to `LevelFile.Sections`. Model names in 03's scatter (`grass-turf0`) have
  hyphens, which the tokenizer's `ReadName` does not take: those entry lines need their own name reader.
- `sdf level` loads the materials library, so 02's `ground_dirt` etc. go to `materials/ground/` and into level.json's `"materials"` by role.
- Vertices are exactly on h(x, z); faces are within the logged error. Sample h (`LevelTerrain.Height`) for scatter heights.
