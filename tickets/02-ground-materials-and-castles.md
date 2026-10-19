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
  `content/models/buildings/castle0.sdf`, walls, towers, a gate, a courtyard, walkable stairs (slope ≤ 40°), ~30 × 20 m footprint.

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
