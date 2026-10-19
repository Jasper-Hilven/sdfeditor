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
