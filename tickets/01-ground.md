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
