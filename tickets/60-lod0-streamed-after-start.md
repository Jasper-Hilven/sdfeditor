# 60 — Each level's LOD0 streamed after the match starts

Part of **Minigun Monsters**, toward CrazyGames' 20 MB mobile homepage limit (ticket 59: the download counted is everything up to the
first `gameplayStart`, which fires when the HUD of the first match is up).

## Today

- Player 16.23 MB (after 59 part 1 and 1b). Every level streams as bundles (tickets 38, 52; `StreamLayout`, `StreamedBundles`,
  `ContentStreamer`, `SceneLoader`): a scene bundle, `<level>-assets` and `shared-<hash>` bundles, LZ4. A first play is the Fungal
  level, about 13.3 MB of bundles, so about 29.5 MB before `gameplayStart`.
- Level meshes across all bundles (uncompressed, unique assets): **LOD0 34.9 MB (54%)**, LOD1 10.6, LOD2 8.0, ground chunks 7.3,
  navmeshes 2.5, colliders 1.8; 65 MB of meshes is about 28 MB LZ4. So LOD0 is about 15 MB of the 42 MB of bundles.

## Decisions

- A level loads and starts with **LOD1 and LOD2 only**; its LOD0 meshes come in a separate `<level>-lod0` bundle, fetched right after
  `gameplayStart` (after the HUD is up), in the background, and swapped in as they arrive. Until then the LOD group's LOD0 slot shows
  the LOD1 mesh (no hole, no pop to nothing).
- Once loaded, the game looks exactly as today.
- Ground chunks, colliders and navmeshes are not touched (collisions must be exact from the first frame).
- The instanced renderer's content (foliage, crest clusters: `InstancedScatterRenderer`) counts too if it has LOD0 meshes in the level
  bundles; handle it the same way, or say why not.
- The next-level prefetch (during a match) fetches the next level's base bundles first, then its `-lod0` bundle.
- The `-lod0` bundle is released with the level's other bundles.
- Phones still download LOD0 (a later decision may skip it on touch at Ultra Low; leave a single switch for it, off).

## Work

- Find how LOD0 meshes are referenced in the level scenes and their prefabs (`MushroomBuilder` prefabs with `LODGroup`, the generated
  `_body`, `_body_LOD1`, `_body_LOD2` meshes in `Assets/Mushrooms/Meshes`, the level scene builders) and in the instanced data.
- At bundle build time (`StreamedBundles`), make the level's scene and `-assets` bundles reference LOD1 in the LOD0 slot, and put the
  real LOD0 meshes in `<level>-lod0`, with a small map (which renderer or prefab gets which mesh). Never modify the source prefabs or
  scenes on disk permanently: work on copies or restore after the build, and the editor and Play mode keep showing LOD0 as today.
- Runtime: a `Lod0Streamer` (Campaign/Runtime, next to `ContentStreamer`) that, after `gameplayStart`, loads the bundle and swaps the
  meshes (including on objects spawned later, such as respawned pickups and machines, if they use level meshes).
- `StreamManifest` / `StreamLayout`: the new bundle per level, tested like the existing layout.
- Measure: per level, the bytes before `gameplayStart` before and after (build log + a headless Chrome network count on
  `http://localhost:PORT`, not 127.0.0.1: CrazySDK's site lock), and the size of each `-lod0` bundle. A screenshot of the same view
  just after start (LOD1 in the LOD0 slot) and after the swap.

## Done when

- The first match's download before `gameplayStart` drops by roughly the level's LOD0 share (numbers per level in the Status).
- After the swap, the level renders with today's LOD0 meshes (a test or a play-mode check compares mesh names per renderer).
- Desktop and touch play tests, EditMode tests and the CrazyGames build pass; the release build in headless Chrome loads to a match and
  swaps without errors.

## Status

Not started.
