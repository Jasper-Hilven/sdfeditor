# 02 — Ground materials and castles

Grass, dirt, path and tiles on the ground with crisp edges; castles and rocks placed by the level file on flattened plateaus; their
colliders.

Depends on: 01 (level file, `sdf level`, `LevelBuilder`, ground shader).

## Level file additions

```
regions                                   # 2D signed-distance shapes, combined like SDF nodes; later entries win
    dirt
        Circle center=(-60, 0) radius=18 noise=1.2
    tiles
        RoundedBox2D center=(-62, 0) size=(14, 10) radius=1
    path
        Polyline points=[(-45, 0), (-10, 6), (20, -4), (45, 0)] width=3 noise=0.6

place
    castle0 at=(-62, 0) yaw=90 ground=tiles flatten=22
    castle0 at=(62, 0) yaw=-90 ground=tiles flatten=22
    rock1 at=(10, 30) yaw=40
    rock3 at=(-20, -35) yaw=0
```

- Regions reuse the pipeline's 2D SDF shapes where possible (`Circle`, `Box2D`, `RoundedBox2D`, `Segment2D`… in
  `SDFProject/Calculation/SdfMath.cs`) plus a polyline for paths; `noise` wobbles the edge.
- `place <model> at= yaw= [scale=]` references any model in `content/models/` by name. `flatten=r` flattens the height to the
  model's base height within r (smooth falloff), and `ground=` paints a region around its footprint.
- Castles are new models (not part of this ticket's art, but needed to test it): start with a simple blockout castle,
  `content/models/buildings/castle0.sdf`, walls, towers, a gate, a courtyard, walkable stairs (slope ≤ 40°), ~30 × 20 m footprint. Splitting it into parts, wall detail and the mushroom theme are ticket
  06.

## Ground materials

- **Material distance texture**, baked by `sdf level`: world-space over the whole extent at **0.125–0.25 m per texel** (≈ 1200 × 800
  for the play area plus hills at 0.17 m), RGBA8 or RG16, one channel per region (dirt, tiles, path; grass is where none is), storing
  the signed distance to the region's edge, clamped to ±2 m and remapped. Bilinear filtering of distances gives crisp, anti-aliased
  edges at any distance (unlike per-vertex or splat weights). Write it as a PNG into the generated folder, referenced from
  `level.json`.
- **Ground shader** (`SdfGround.shader` from 01): samples the texture, turns distances into weights with a narrow smoothstep (edge
  width ≈ 0.1 m, plus a little world-space noise so edges are not ruler-straight), blends the material parameters of
  `ground_grass`, `ground_dirt`, `ground_path`, `ground_tiles` (new `.sdfmat` files in `materials/ground/`), then shades with
  `SdfToonShade`.
- **Tiles drawn in the shader**: a world-space pattern (square or offset brick grid, ≈ 0.6 m stones) with grout lines, per-stone
  colour jitter, and a bevelled normal near the grout. No geometry. Door steps, kerbs and similar hero stones can be placed as models.

## Placements

- `sdf level` writes placements to `level.json` (model name, position on the baked ground height, yaw, scale) and flattens the
  heightfield under them before chunking.
- `LevelBuilder` instantiates the model prefabs built by `MushroomBuilder` (meshes come from the normal model pipeline:
  `content/models/...` → `Generated/...glb` → prefab); placements of a missing prefab log a warning.
- `place` is for the **few unique models** (castles, a handful of big rocks): they become ordinary static prefab instances in the
  level scene. Anything placed in quantity (grass, flowers, trees, small rocks) is scattered instead and drawn by the instanced
  renderer (03, 04).

## Colliders (baked by the pipeline, applied in Unity)

- **Castles**: a static non-convex `MeshCollider` from the coarsest LOD that still keeps stairs and walkways. If that is too rough,
  allow a named collision part in the castle's `.sdf` (a part named `collision`, meshed but not rendered).
- **Rocks**: convex `MeshCollider` from the coarsest LOD; non-convex for large outcrops (bounding size > 3 m).
- How the builder knows: a per-model setting, e.g. a header line in the `.sdf` (`collider convex|mesh|capsule|none`) that the
  pipeline writes into the glb extras, which `MushroomBuilder` reads and turns into the right collider on the prefab. Default `none`.

## Acceptance

- Dirt, path and tiled areas show with crisp edges from 1.5 m and from far away; no visible texel stair-stepping.
- Tiles read as stones in the toon style; colours fit the models' palette.
- Both castles stand level on flattened ground, surrounded by their tiles/dirt; the courtyard and stairs are walkable.
- Rocks and castles block the player; foliage-free areas are ready for 03 (the distance texture is the mask).
- Re-baking with a changed region or placement updates the Unity scene automatically.

## Status

Done (not committed). Castle validation deferred to 06, see below.

- **Level file**: new sections `regions` and `place` in `LevelFile.cs` (records in `LevelDefinition.cs`). Regions are named by role, each
  with shapes indented below it: `Circle`, `Box2D`, `RoundedBox2D` (centre, full size, angle, radius) and `Polyline` (points, width), each
  with `noise`/`noiseScale`. A region line takes `pattern=plain|stones`, `stone=` and `grout=` (`tiles` is stones by default). There are
  at most four regions. Placements are read as `<model> at=(x, z) [yaw=] [scale=] [flatten=] [falloff=] [ground=] [groundMargin=]`. Model
  names go through a new `Cursor.ReadFileName`, which takes hyphens (`grass-turf0`, `amanita-scout`); 03 should use it for scatter entries.
  `content/levels/meadow.level` has dirt round both castles, a path from gate to gate, tile aprons, both castles (`ground=tiles flatten=22`)
  and four rocks.
- **Regions and map**: `GroundRegions.cs` holds the shapes as exact 2D distances (as `SdfMath`'s) plus seeded value-noise wobble. A
  placement's `ground=` adds a rounded box over its turned, scaled footprint (from the model's bounds, `LevelModels.cs`) to that region.
  `RegionMap.cs` bakes `regions.png`, RGBA8 at 0.17 m per texel (1283 × 989), one channel per region, signed distance clamped to ±2 m.
  The PNG writer is our own, so the output is deterministic. level.json `regions` gives the texture, encoding, mapping, hash and channels
  (name, material, pattern, stone/grout).
- **Flattening and placements**: `LevelTerrain.Height` levels a disc of radius `flatten` to the natural height at the placement and
  blends back over `falloff` (default 6 m), before chunking. level.json has `placements` (model, base position on the baked height, yaw,
  scale, in pipeline coordinates) and the sorted `"models"` list. Missing models and placements off the island are errors with the line.
- **Materials**: `materials/ground/ground_dirt|path|tiles.sdfmat`, in level.json `materials` by role. Tiles use `edgeColor` as the grout.
- **Ground shader** (`SdfGround.shader`): samples the map and turns distance into weight with a smoothstep of ±0.05 m (widened to the
  pixel's footprint by `fwidth`), with ±0.06 m world noise. Later regions cover earlier ones. Each layer's colour is world-space
  variation and speckles, then `SdfToonShade`. Stones: an offset bond of 0.78 × 0.6 m stones (random shift per row), grout lines
  anti-aliased by pixel size, colour per stone between base and variation, and a bevel that leans the normal near the grout. The pattern
  fades to its average where a pixel covers a good part of a stone. No textures beyond the map, so it runs on WebGL 2.
- **Colliders**: a `collider none|convex|mesh|capsule [lod=n]` header line in a model's `.sdf` (`ModelSettings.cs`). The program reader
  skips it, and parts may not have one. The CLI passes it to `GlbWriter`, which writes `extras.sdf.collider`: the LOD (coarsest by
  default), convex turned into mesh above 3 m, and the capsule's axis and radius. `MushroomBuilder` (version 10) adds the colliders to
  the prefab root. `LevelBuilder` also makes a convex collider concave when the placement's scale takes it past 3 m (rock1 at 2.2).
  Rocks 0–4 are `collider convex`; `castle0` is `collider mesh`.
- **Unity**: `LevelBuilder` (version 2) imports `regions.png` as linear, uncompressed and bilinear with mips. It sets the region
  materials and mapping on the ground material (x mirrored) and places the prefabs under `Level/Placed` at (-x, y, z), turned by -yaw,
  scaled and static. A missing prefab logs a warning. `GeneratedFolderWatcher` also watches the levels' `.json` and `.png`.
  `LevelSnapshot` adds castle and region shots and the collider walks.

Verified:

- `dotnet build` clean. `dotnet test`: 217 pipeline tests and 533 project tests pass. New tests cover shapes, turning, region order and
  painted footprints, the map's texel values, the PNG rows, flattening, a byte-identical bake with placements and `"models"`, hyphenated
  names, reader errors, the collider line, and the glb extras.
- `sdf level content/levels/meadow.level` bakes in about 3 s, byte-identical across runs and with what is in
  `Unity/Assets/Level/Generated/meadow`: 63 chunks, LOD0 50,884 triangles, 3 regions, 6 placements.
- The open editor rebuilt `Meadow.unity` by itself one second after the last bake wrote level.json; its stamp matches that
  level.json. On the APFS clone (`.claude/jobs/c6233055/tmp/unity_clone`, refreshed from `Unity/`), `LevelBuilder.BuildAllInBatch`
  and `LevelSnapshot.Capture` run with no compile or shader errors.
- Snapshots from the 1.5 m camera, looked at:
  - The dirt, path and tile edges are crisp up close and at the far castle, about 75 m away. There is no texel stair-stepping. From
    above the edges stay clean, and the web preset looks the same.
  - The tiles read as bevelled flagstones with dark grout in the toon bands.
  - Ground triangles per eye view are 13–24k in the field and about 40k at a castle, where the flattened plateau adds some. All are
    under 100k.
- Rocks block the player, by script (CharacterController walked straight at each one):
  - rock1 (concave at 4.6 m) stops 1.57 m from its middle;
  - rock3 stops 1.11 m from its middle;
  - rock4 stops 0.76 m from its middle;
  - rock2 is the 0.65 m slab "low enough to step onto". The walker climbs onto it (0.59 m), so its collider is there, but it does not
    block.

To check by hand:

- Walk the region edges and tiles in Play mode, in motion (shimmer of the grout and speckles far away, the edge noise), on both presets.
- Whether rock2 should block (a taller slab, or a lower `stepOffset`) or stay steppable as its file says.
- Re-bake with a changed region or placement while the editor is open and watch the scene follow. The mechanism is 01's and the last
  bake rebuilt the scene, but a changed region was not tried this time.
- Colours of dirt, path and tiles next to the turfs once 03 scatters them.

Deferred to 06 (on the user's request): the castle's walkability, its stairs, courtyard and wall-walk, and whether its mesh collider
(LOD2 of the blockout) blocks and carries the player. Also deferred are a `collision` part, if the LOD turns out too rough, and the
castle's look. `content/models/buildings/castle0.sdf` stays the blockout. The snapshot script still walks the stair and the front wall
and logs the result: 5.05 m up and stopped at the wall. Those numbers were not reviewed for this ticket.

Notes for the next tickets:

- 03: mask foliage with the region map. A channel value below 0.5 means inside the region. Or call `GroundRegions.Distance` in the
  baker (the same function, noise included). Read scatter model names with `Cursor.ReadFileName`, and add new sections to
  `LevelFile.Sections`. Placements and their plateaus are in `LevelDefinition.Placements`; keep scatter off them.
- 06: the model settings live in `ModelSettings.cs` (the keyword list `Keywords`, which the program reader skips). The glb extras are
  written in `GlbWriter.Collider`, and the prefab side is `MushroomBuilder.AddCollider`, which today adds one collider per part to the
  root. The IDE's "save .sdf" writes the program back without the settings lines (as it drops comments).
- Placements in Unity: x negated, yaw negated. Prefabs come from `MushroomBuilder.PrefabFolder/<model>.prefab`, by file name.
